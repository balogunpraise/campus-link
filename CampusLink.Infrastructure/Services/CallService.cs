using CampusLink.Core.Application.Common;
using CampusLink.Core.Application.Dtos.Calls;
using CampusLink.Core.Application.Interfaces;
using CampusLink.Core.Application.Interfaces.Repositories;
using CampusLink.Core.Domain.Entities;
using CampusLink.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CampusLink.Infrastructure.Services;

public class CallService(
    IRepositoryManager repository,
    ILogger<CallService> logger) : ICallService
{
    public async Task<Result<CallResponse>> StartAsync(
        Guid userId,
        StartCallRequest request,
        CancellationToken cancellationToken = default)
    {
        var conversation = await repository.Conversations
            .GetWithParticipantsAsync(request.ConversationId, false, cancellationToken);

        if (conversation is null || conversation.Participants.All(p => p.UserId != userId || !p.IsActive))
            return Fail("Conversation not found.", AuthErrorCode.NotFound);

        var members = conversation.Participants.Where(p => p.IsActive).ToList();

        // Mesh topology has a hard ceiling; refusing here is better than a call
        // that degrades into unusable video on someone's phone.
        if (members.Count > Call.MaxParticipants)
            return Fail(
                $"Calls support at most {Call.MaxParticipants} participants; this conversation has {members.Count}.",
                AuthErrorCode.ValidationFailed);

        if (members.Count < 2)
            return Fail("There's nobody else in this conversation to call.", AuthErrorCode.ValidationFailed);

        // One call per conversation, so "join the call" is never ambiguous. An
        // existing live call is returned rather than rejected — the caller
        // almost certainly wants to join it.
        var existing = await repository.Calls
            .GetActiveForConversationAsync(request.ConversationId, false, cancellationToken);

        if (existing is not null)
            return await JoinAsync(userId, existing.Id, cancellationToken);

        var call = new Call
        {
            ConversationId = request.ConversationId,
            StartedById = userId,
            Type = request.Type,
            Status = CallStatus.Ringing
        };

        foreach (var member in members)
        {
            call.Participants.Add(new CallParticipant
            {
                UserId = member.UserId,

                // The initiator is in from the start; everyone else is ringing
                // until they answer.
                JoinedAt = member.UserId == userId ? DateTime.UtcNow : null,
                IsCameraOn = member.UserId == userId && request.Type == CallType.Video
            });
        }

        await repository.Calls.CreateAsync(call, cancellationToken);
        await repository.SaveAsync(cancellationToken);

        logger.LogInformation(
            "Call {CallId} started by {UserId} in conversation {ConversationId} ({Type}, {Count} invited)",
            call.Id, userId, request.ConversationId, request.Type, members.Count);

        return await LoadResponseAsync(call.Id, cancellationToken);
    }

    public async Task<Result<CallResponse>> JoinAsync(
        Guid userId,
        Guid callId,
        CancellationToken cancellationToken = default)
    {
        var call = await repository.Calls.GetWithParticipantsAsync(callId, true, cancellationToken);

        if (call is null)
            return Fail("Call not found.", AuthErrorCode.NotFound);

        // Membership is the conversation's, not the call's — someone added to a
        // group mid-call can still join.
        var member = await repository.Conversations
            .GetParticipantAsync(call.ConversationId, userId, false, cancellationToken);

        if (member is null || !member.IsActive)
            return Fail("Call not found.", AuthErrorCode.NotFound);

        if (!call.IsLive)
            return Fail("That call has ended.", AuthErrorCode.Conflict);

        var participant = call.Participants.FirstOrDefault(p => p.UserId == userId);

        if (participant is null)
        {
            if (call.Participants.Count(p => p.IsInCall) >= Call.MaxParticipants)
                return Fail($"This call is full ({Call.MaxParticipants} participants).", AuthErrorCode.Conflict);

            participant = new CallParticipant { CallId = callId, UserId = userId };
            call.Participants.Add(participant);
        }
        else if (participant.IsInCall)
        {
            // Already in — joining twice is a no-op, not an error (a client may
            // retry after a flaky reconnect).
            return await LoadResponseAsync(callId, cancellationToken);
        }

        if (call.Participants.Count(p => p.IsInCall) >= Call.MaxParticipants)
            return Fail($"This call is full ({Call.MaxParticipants} participants).", AuthErrorCode.Conflict);

        participant.JoinedAt = DateTime.UtcNow;
        participant.LeftAt = null;
        participant.Declined = false;

        // Two people connected means it stopped being a ringing invitation.
        if (call.Status == CallStatus.Ringing && call.Participants.Count(p => p.IsInCall) >= 2)
        {
            call.Status = CallStatus.Active;
            call.ConnectedAt ??= DateTime.UtcNow;
        }

        repository.Calls.Update(call);
        await repository.SaveAsync(cancellationToken);

        return await LoadResponseAsync(callId, cancellationToken);
    }

    public async Task<Result<CallResponse>> DeclineAsync(
        Guid userId,
        Guid callId,
        CancellationToken cancellationToken = default)
    {
        var call = await repository.Calls.GetWithParticipantsAsync(callId, true, cancellationToken);

        if (call is null)
            return Fail("Call not found.", AuthErrorCode.NotFound);

        var participant = call.Participants.FirstOrDefault(p => p.UserId == userId);

        if (participant is null)
            return Fail("Call not found.", AuthErrorCode.NotFound);

        participant.Declined = true;
        participant.LeftAt ??= DateTime.UtcNow;

        // If everyone invited has now declined, the call is over rather than
        // ringing into the void.
        if (call.Status == CallStatus.Ringing &&
            call.Participants.Where(p => p.UserId != call.StartedById).All(p => p.Declined))
            EndCall(call, CallEndReason.Declined);

        repository.Calls.Update(call);
        await repository.SaveAsync(cancellationToken);

        return await LoadResponseAsync(callId, cancellationToken);
    }

    public async Task<Result<CallResponse>> LeaveAsync(
        Guid userId,
        Guid callId,
        CancellationToken cancellationToken = default)
    {
        var call = await repository.Calls.GetWithParticipantsAsync(callId, true, cancellationToken);

        if (call is null)
            return Fail("Call not found.", AuthErrorCode.NotFound);

        var participant = call.Participants.FirstOrDefault(p => p.UserId == userId);

        if (participant is null)
            return Fail("Call not found.", AuthErrorCode.NotFound);

        if (participant.LeftAt is null)
        {
            participant.LeftAt = DateTime.UtcNow;
            participant.IsSharingScreen = false;
        }

        if (call.IsLive)
        {
            var remaining = call.Participants.Count(p => p.IsInCall);

            if (call.Status == CallStatus.Ringing && userId == call.StartedById)
                // The caller hung up before anyone picked up.
                EndCall(call, CallEndReason.Cancelled);
            else if (remaining < 2)
                // A one-person call isn't a call. This also covers the last
                // person hanging up.
                EndCall(call, CallEndReason.Completed);
        }

        repository.Calls.Update(call);
        await repository.SaveAsync(cancellationToken);

        return await LoadResponseAsync(callId, cancellationToken);
    }

    public async Task<Result<CallResponse>> UpdateMediaStateAsync(
        Guid userId,
        Guid callId,
        UpdateMediaStateRequest request,
        CancellationToken cancellationToken = default)
    {
        var participant = await repository.Calls.GetParticipantAsync(callId, userId, true, cancellationToken);

        if (participant is null || !participant.IsInCall)
            return Fail("Call not found.", AuthErrorCode.NotFound);

        if (request.IsMuted.HasValue) participant.IsMuted = request.IsMuted.Value;
        if (request.IsCameraOn.HasValue) participant.IsCameraOn = request.IsCameraOn.Value;
        if (request.IsSharingScreen.HasValue) participant.IsSharingScreen = request.IsSharingScreen.Value;

        await repository.SaveAsync(cancellationToken);

        return await LoadResponseAsync(callId, cancellationToken);
    }

    public async Task<Result<CallResponse>> GetActiveForConversationAsync(
        Guid userId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var member = await repository.Conversations
            .GetParticipantAsync(conversationId, userId, false, cancellationToken);

        if (member is null || !member.IsActive)
            return Fail("Conversation not found.", AuthErrorCode.NotFound);

        var call = await repository.Calls
            .GetActiveForConversationAsync(conversationId, false, cancellationToken);

        return call is null
            ? Fail("No active call.", AuthErrorCode.NotFound)
            : Result<CallResponse>.Success(ToResponse(call));
    }

    public async Task<Result<CallResponse>> GetAsync(
        Guid userId,
        Guid callId,
        CancellationToken cancellationToken = default)
    {
        var call = await repository.Calls.GetWithParticipantsAsync(callId, false, cancellationToken);

        if (call is null)
            return Fail("Call not found.", AuthErrorCode.NotFound);

        var member = await repository.Conversations
            .GetParticipantAsync(call.ConversationId, userId, false, cancellationToken);

        if (member is null || !member.IsActive)
            return Fail("Call not found.", AuthErrorCode.NotFound);

        return Result<CallResponse>.Success(ToResponse(call));
    }

    public async Task<Result<IReadOnlyList<CallResponse>>> GetHistoryAsync(
        Guid userId,
        Guid conversationId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var member = await repository.Conversations
            .GetParticipantAsync(conversationId, userId, false, cancellationToken);

        if (member is null || !member.IsActive)
            return Result<IReadOnlyList<CallResponse>>.Failure(
                AuthErrorCode.NotFound, "Conversation not found.");

        var calls = await repository.Calls
            .GetHistory(conversationId, false)
            .Take(Math.Clamp(limit <= 0 ? 25 : limit, 1, 100))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<CallResponse>>.Success(calls.Select(ToResponse).ToList());
    }

    public async Task<bool> CanSignalAsync(
        Guid fromUserId,
        Guid toUserId,
        Guid callId,
        CancellationToken cancellationToken = default)
    {
        var call = await repository.Calls.GetWithParticipantsAsync(callId, false, cancellationToken);

        if (call is null || !call.IsLive)
            return false;

        // Both ends must belong to this call. The sender must have joined; the
        // target may still be ringing, since the offer can arrive before they
        // answer.
        var from = call.Participants.FirstOrDefault(p => p.UserId == fromUserId);
        var to = call.Participants.FirstOrDefault(p => p.UserId == toUserId);

        return from is { IsInCall: true } && to is not null && to.LeftAt is null;
    }

    public async Task<int> ExpireStaleRingingAsync(
        TimeSpan ringTimeout,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - ringTimeout;

        var stale = await repository.Calls
            .GetStaleRinging(cutoff, true)
            .ToListAsync(cancellationToken);

        foreach (var call in stale)
            EndCall(call, CallEndReason.NoAnswer);

        if (stale.Count > 0)
            await repository.SaveAsync(cancellationToken);

        return stale.Count;
    }

    // ----- helpers -----

    private static void EndCall(Call call, CallEndReason reason)
    {
        call.Status = CallStatus.Ended;
        call.EndedAt = DateTime.UtcNow;
        call.EndReason = reason;

        foreach (var participant in call.Participants.Where(p => p.LeftAt is null))
        {
            participant.LeftAt = call.EndedAt;
            participant.IsSharingScreen = false;
        }
    }

    private async Task<Result<CallResponse>> LoadResponseAsync(Guid callId, CancellationToken cancellationToken)
    {
        var call = await repository.Calls.GetWithParticipantsAsync(callId, false, cancellationToken);

        return Result<CallResponse>.Success(ToResponse(call));
    }

    private static CallResponse ToResponse(Call call) => new()
    {
        Id = call.Id,
        ConversationId = call.ConversationId,
        StartedById = call.StartedById,
        StartedByName = call.StartedBy is null
            ? null
            : $"{call.StartedBy.FirstName} {call.StartedBy.LastName}".Trim(),
        Type = call.Type.ToString(),
        Status = call.Status.ToString(),
        EndReason = call.EndReason?.ToString(),
        StartedAt = call.StartedAt,
        ConnectedAt = call.ConnectedAt,
        EndedAt = call.EndedAt,
        DurationSeconds = call.Duration?.TotalSeconds,
        Participants = call.Participants.Select(p => new CallParticipantResponse
        {
            UserId = p.UserId,
            UserName = p.User?.UserName,
            FullName = p.User is null ? null : $"{p.User.FirstName} {p.User.LastName}".Trim(),
            JoinedAt = p.JoinedAt,
            LeftAt = p.LeftAt,
            IsInCall = p.IsInCall,
            IsMuted = p.IsMuted,
            IsCameraOn = p.IsCameraOn,
            IsSharingScreen = p.IsSharingScreen,
            Declined = p.Declined
        }).ToList()
    };

    private static Result<CallResponse> Fail(string message, AuthErrorCode code) =>
        Result<CallResponse>.Failure(code, message);
}
