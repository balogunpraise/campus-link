using CampusLink.Core.Application.Common;
using CampusLink.Core.Application.Dtos.Calls;
using CampusLink.Core.Domain.Enums;

namespace CampusLink.Core.Application.Interfaces;

// Call lifecycle and permissions. Media is peer-to-peer and never passes
// through here; this decides who may ring whom, who may join, and when a call
// is over.
public interface ICallService
{
    // Rings every other active member of the conversation. Fails if one is
    // already live — a conversation has at most one call at a time.
    Task<Result<CallResponse>> StartAsync(
        Guid userId,
        StartCallRequest request,
        CancellationToken cancellationToken = default);

    // Answer a ringing call, or join one already in progress.
    Task<Result<CallResponse>> JoinAsync(
        Guid userId,
        Guid callId,
        CancellationToken cancellationToken = default);

    Task<Result<CallResponse>> DeclineAsync(
        Guid userId,
        Guid callId,
        CancellationToken cancellationToken = default);

    // Hang up. Ends the whole call when nobody's left, or when the caller
    // withdraws before anyone answered.
    Task<Result<CallResponse>> LeaveAsync(
        Guid userId,
        Guid callId,
        CancellationToken cancellationToken = default);

    Task<Result<CallResponse>> UpdateMediaStateAsync(
        Guid userId,
        Guid callId,
        UpdateMediaStateRequest request,
        CancellationToken cancellationToken = default);

    // The live call in a conversation, if any — what a client polls on load to
    // discover an in-progress call it can still join.
    Task<Result<CallResponse>> GetActiveForConversationAsync(
        Guid userId,
        Guid conversationId,
        CancellationToken cancellationToken = default);

    Task<Result<CallResponse>> GetAsync(
        Guid userId,
        Guid callId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<CallResponse>>> GetHistoryAsync(
        Guid userId,
        Guid conversationId,
        int limit,
        CancellationToken cancellationToken = default);

    // Both parties must be in the same live call before the server will relay
    // anything between them.
    Task<bool> CanSignalAsync(
        Guid fromUserId,
        Guid toUserId,
        Guid callId,
        CancellationToken cancellationToken = default);

    // Sweeps calls nobody answered. Ring timeouts can't be a client's job — the
    // caller might close their laptop.
    Task<int> ExpireStaleRingingAsync(
        TimeSpan ringTimeout,
        CancellationToken cancellationToken = default);
}

// Real-time fan-out, implemented over SignalR in the API layer.
public interface ICallNotifier
{
    Task IncomingCallAsync(IReadOnlyList<Guid> userIds, CallResponse call);
    Task CallUpdatedAsync(Guid callId, IReadOnlyList<Guid> userIds, CallResponse call);
    Task SignalAsync(Guid targetUserId, CallSignal signal);
}

// Mints short-lived TURN credentials. Separate from the service because it's
// pure crypto over configuration — no database, no call state.
public interface IIceServerProvider
{
    IceServersResponse GetIceServers(Guid userId);
}
