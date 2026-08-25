using CampusLink.Core.Application.Common;
using CampusLink.Core.Application.Dtos.Chat;
using CampusLink.Core.Application.Interfaces;
using CampusLink.Core.Application.Interfaces.Repositories;
using CampusLink.Core.Domain.Entities;
using CampusLink.Core.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CampusLink.Infrastructure.Services;

public class ChatService(
    IRepositoryManager repository,
    UserManager<AppUser> userManager,
    ILogger<ChatService> logger) : IChatService
{
    private const int MaxGroupParticipants = 100;
    private const int MaxPageSize = 100;

    // Editing is a correction window, not a rewrite-history button.
    private static readonly TimeSpan EditWindow = TimeSpan.FromMinutes(15);

    public async Task<Result<ConversationSummary>> StartDirectAsync(
        Guid userId,
        Guid recipientUserId,
        CancellationToken cancellationToken = default)
    {
        if (userId == recipientUserId)
            return Fail<ConversationSummary>("You can't start a conversation with yourself.",
                AuthErrorCode.ValidationFailed);

        var existing = await repository.Conversations
            .GetDirectAsync(userId, recipientUserId, true, cancellationToken);

        if (existing is not null)
        {
            // Reopening a thread you'd left rejoins you rather than making a
            // second one — DirectKey is unique, so a new row would fail anyway.
            var mine = existing.Participants.FirstOrDefault(p => p.UserId == userId);

            if (mine is { LeftAt: not null })
            {
                mine.LeftAt = null;
                await repository.SaveAsync(cancellationToken);
            }

            return await GetConversationAsync(userId, existing.Id, cancellationToken);
        }

        var permission = await CanMessageAsync(userId, recipientUserId, cancellationToken);
        if (!permission.Succeeded)
            return Result<ConversationSummary>.Failure(permission.ErrorCode, permission.Errors);

        var conversation = new Conversation
        {
            Type = ConversationType.Direct,
            DirectKey = Conversation.BuildDirectKey(userId, recipientUserId),
            CreatedById = userId
        };

        conversation.Participants.Add(new ConversationParticipant { UserId = userId });
        conversation.Participants.Add(new ConversationParticipant { UserId = recipientUserId });

        await repository.Conversations.CreateAsync(conversation, cancellationToken);
        await repository.SaveAsync(cancellationToken);

        return await GetConversationAsync(userId, conversation.Id, cancellationToken);
    }

    public async Task<Result<ConversationSummary>> CreateGroupAsync(
        Guid userId,
        CreateGroupConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        var invitees = request.ParticipantUserIds
            .Where(id => id != userId)
            .Distinct()
            .ToList();

        if (invitees.Count + 1 > MaxGroupParticipants)
            return Fail<ConversationSummary>(
                $"A group can have at most {MaxGroupParticipants} participants.", AuthErrorCode.ValidationFailed);

        // Every invitee must be someone the creator is allowed to message —
        // otherwise a group would be a way around the direct-message rules.
        foreach (var invitee in invitees)
        {
            var allowed = await CanMessageAsync(userId, invitee, cancellationToken);

            if (!allowed.Succeeded)
                return Fail<ConversationSummary>(
                    $"You can't add user {invitee} to a conversation.", AuthErrorCode.ValidationFailed);
        }

        var conversation = new Conversation
        {
            Type = ConversationType.Group,
            Title = request.Title.Trim(),
            StudyGroupId = request.StudyGroupId,
            CreatedById = userId
        };

        conversation.Participants.Add(new ConversationParticipant
        {
            UserId = userId,
            Role = ParticipantRole.Admin
        });

        foreach (var invitee in invitees)
            conversation.Participants.Add(new ConversationParticipant { UserId = invitee });

        await repository.Conversations.CreateAsync(conversation, cancellationToken);
        await repository.SaveAsync(cancellationToken);

        logger.LogInformation(
            "Group conversation {ConversationId} created by {UserId} with {Count} participants",
            conversation.Id, userId, conversation.Participants.Count);

        return await GetConversationAsync(userId, conversation.Id, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<ConversationSummary>>> GetMyConversationsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var conversations = await repository.Conversations
            .GetForUser(userId, false)
            .Include(c => c.Participants).ThenInclude(p => p.User).ThenInclude(u => u.Institution)
            .ToListAsync(cancellationToken);

        var summaries = new List<ConversationSummary>(conversations.Count);

        foreach (var conversation in conversations)
            summaries.Add(await ToSummaryAsync(conversation, userId, cancellationToken));

        return Result<IReadOnlyList<ConversationSummary>>.Success(summaries);
    }

    public async Task<Result<ConversationSummary>> GetConversationAsync(
        Guid userId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await repository.Conversations
            .FindByCondition(c => c.Id == conversationId, false)
            .Include(c => c.Participants).ThenInclude(p => p.User).ThenInclude(u => u.Institution)
            .FirstOrDefaultAsync(cancellationToken);

        if (conversation is null || conversation.Participants.All(p => p.UserId != userId || !p.IsActive))
            return Fail<ConversationSummary>("Conversation not found.", AuthErrorCode.NotFound);

        return Result<ConversationSummary>.Success(
            await ToSummaryAsync(conversation, userId, cancellationToken));
    }

    public async Task<Result<MessagePage>> GetMessagesAsync(
        Guid userId,
        Guid conversationId,
        DateTime? before,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var participant = await repository.Conversations
            .GetParticipantAsync(conversationId, userId, false, cancellationToken);

        if (participant is null || !participant.IsActive)
            return Fail<MessagePage>("Conversation not found.", AuthErrorCode.NotFound);

        var take = Math.Clamp(limit <= 0 ? 50 : limit, 1, MaxPageSize);

        // One extra row tells us whether another page exists without a count.
        var rows = await repository.ChatMessages
            .GetPage(conversationId, before, false)
            .Include(m => m.Sender)
            .Take(take + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > take;
        var items = rows.Take(take).Select(ToMessageResponse).ToList();

        return Result<MessagePage>.Success(new MessagePage
        {
            Items = items,
            HasMore = hasMore,
            NextCursor = hasMore ? items[^1].SentAt : null
        });
    }

    public async Task<Result<ChatMessageResponse>> SendMessageAsync(
        Guid userId,
        Guid conversationId,
        SendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        var participant = await repository.Conversations
            .GetParticipantAsync(conversationId, userId, false, cancellationToken);

        if (participant is null || !participant.IsActive)
            return Fail<ChatMessageResponse>("Conversation not found.", AuthErrorCode.NotFound);

        var content = request.Content?.Trim();

        if (string.IsNullOrEmpty(content))
            return Fail<ChatMessageResponse>("Message can't be empty.", AuthErrorCode.ValidationFailed);

        // A reply has to point at a message in this same thread, otherwise it
        // leaks the existence of messages elsewhere.
        if (request.ReplyToMessageId.HasValue)
        {
            var target = await repository.ChatMessages
                .FindByIdAsync(request.ReplyToMessageId.Value, false, cancellationToken);

            if (target is null || target.ConversationId != conversationId)
                return Fail<ChatMessageResponse>(
                    "The message being replied to isn't in this conversation.", AuthErrorCode.ValidationFailed);
        }

        var message = new ChatMessage
        {
            ConversationId = conversationId,
            SenderId = userId,
            Content = content,
            ReplyToMessageId = request.ReplyToMessageId,
            ClientMessageId = request.ClientMessageId
        };

        await repository.ChatMessages.CreateAsync(message, cancellationToken);

        // Keeps the conversation list sorted without scanning messages.
        var conversation = await repository.Conversations.FindByIdAsync(conversationId, true, cancellationToken);
        conversation.LastMessageAt = message.SentAt;
        repository.Conversations.Update(conversation);

        // Sending is also reading: you've obviously seen your own thread.
        var mine = await repository.Conversations
            .GetParticipantAsync(conversationId, userId, true, cancellationToken);
        mine.LastReadAt = message.SentAt;

        await repository.SaveAsync(cancellationToken);

        return Result<ChatMessageResponse>.Success(
            await LoadMessageResponseAsync(message.Id, cancellationToken));
    }

    public async Task<Result<ChatMessageResponse>> EditMessageAsync(
        Guid userId,
        Guid messageId,
        string content,
        CancellationToken cancellationToken = default)
    {
        var message = await repository.ChatMessages.FindByIdAsync(messageId, true, cancellationToken);

        if (message is null || message.SenderId != userId)
            return Fail<ChatMessageResponse>("Message not found.", AuthErrorCode.NotFound);

        if (message.IsDeleted)
            return Fail<ChatMessageResponse>("That message was deleted.", AuthErrorCode.Conflict);

        if (DateTime.UtcNow - message.SentAt > EditWindow)
            return Fail<ChatMessageResponse>(
                $"Messages can only be edited within {EditWindow.TotalMinutes:0} minutes of sending.",
                AuthErrorCode.Conflict);

        message.Content = content.Trim();
        message.EditedAt = DateTime.UtcNow;
        repository.ChatMessages.Update(message);

        await repository.SaveAsync(cancellationToken);

        return Result<ChatMessageResponse>.Success(
            await LoadMessageResponseAsync(message.Id, cancellationToken));
    }

    public async Task<Result<ChatMessageResponse>> DeleteMessageAsync(
        Guid userId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var message = await repository.ChatMessages.FindByIdAsync(messageId, true, cancellationToken);

        if (message is null)
            return Fail<ChatMessageResponse>("Message not found.", AuthErrorCode.NotFound);

        var isSender = message.SenderId == userId;

        // Group admins can remove someone else's message; nobody else can.
        var participant = await repository.Conversations
            .GetParticipantAsync(message.ConversationId, userId, false, cancellationToken);

        if (participant is null || !participant.IsActive ||
            (!isSender && participant.Role != ParticipantRole.Admin))
            return Fail<ChatMessageResponse>("Message not found.", AuthErrorCode.NotFound);

        if (message.IsDeleted)
            return Result<ChatMessageResponse>.Success(ToMessageResponse(message));

        // Soft delete: content is cleared so it stops travelling to clients,
        // the row stays so replies and moderation still resolve.
        message.DeletedAt = DateTime.UtcNow;
        message.Content = string.Empty;
        repository.ChatMessages.Update(message);

        await repository.SaveAsync(cancellationToken);

        return Result<ChatMessageResponse>.Success(
            await LoadMessageResponseAsync(message.Id, cancellationToken));
    }

    public async Task<Result<ReadReceiptEvent>> MarkReadAsync(
        Guid userId,
        Guid conversationId,
        DateTime? readAt,
        CancellationToken cancellationToken = default)
    {
        var participant = await repository.Conversations
            .GetParticipantAsync(conversationId, userId, true, cancellationToken);

        if (participant is null || !participant.IsActive)
            return Fail<ReadReceiptEvent>("Conversation not found.", AuthErrorCode.NotFound);

        var stamp = readAt ?? DateTime.UtcNow;

        // Never move the marker backwards — a late-arriving receipt from a
        // second device would otherwise resurrect messages as unread.
        if (participant.LastReadAt is null || stamp > participant.LastReadAt)
        {
            participant.LastReadAt = stamp;
            await repository.SaveAsync(cancellationToken);
        }

        return Result<ReadReceiptEvent>.Success(new ReadReceiptEvent
        {
            ConversationId = conversationId,
            UserId = userId,
            ReadAt = participant.LastReadAt!.Value
        });
    }

    public async Task<Result<ConversationSummary>> AddParticipantsAsync(
        Guid userId,
        Guid conversationId,
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        var conversation = await repository.Conversations
            .GetWithParticipantsAsync(conversationId, true, cancellationToken);

        if (conversation is null)
            return Fail<ConversationSummary>("Conversation not found.", AuthErrorCode.NotFound);

        var me = conversation.Participants.FirstOrDefault(p => p.UserId == userId && p.IsActive);

        if (me is null)
            return Fail<ConversationSummary>("Conversation not found.", AuthErrorCode.NotFound);

        // A direct thread is defined by its two people; adding a third would
        // break the DirectKey invariant. Start a group instead.
        if (conversation.Type == ConversationType.Direct)
            return Fail<ConversationSummary>(
                "You can't add people to a direct conversation — create a group instead.",
                AuthErrorCode.Conflict);

        if (me.Role != ParticipantRole.Admin)
            return Fail<ConversationSummary>("Only group admins can add participants.", AuthErrorCode.Conflict);

        var active = conversation.Participants.Count(p => p.IsActive);

        foreach (var target in userIds.Distinct())
        {
            var existing = conversation.Participants.FirstOrDefault(p => p.UserId == target);

            if (existing is { IsActive: true })
                continue;

            var allowed = await CanMessageAsync(userId, target, cancellationToken);
            if (!allowed.Succeeded)
                return Fail<ConversationSummary>(
                    $"You can't add user {target} to a conversation.", AuthErrorCode.ValidationFailed);

            if (active >= MaxGroupParticipants)
                return Fail<ConversationSummary>(
                    $"A group can have at most {MaxGroupParticipants} participants.",
                    AuthErrorCode.Conflict);

            if (existing is not null)
            {
                // Rejoin rather than insert — the composite key already exists.
                existing.LeftAt = null;
                existing.JoinedAt = DateTime.UtcNow;
            }
            else
            {
                conversation.Participants.Add(new ConversationParticipant
                {
                    ConversationId = conversationId,
                    UserId = target
                });
            }

            active++;
        }

        await repository.SaveAsync(cancellationToken);

        return await GetConversationAsync(userId, conversationId, cancellationToken);
    }

    public async Task<Result<bool>> RemoveParticipantAsync(
        Guid userId,
        Guid conversationId,
        Guid targetUserId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await repository.Conversations
            .GetWithParticipantsAsync(conversationId, true, cancellationToken);

        if (conversation is null)
            return Fail<bool>("Conversation not found.", AuthErrorCode.NotFound);

        var me = conversation.Participants.FirstOrDefault(p => p.UserId == userId && p.IsActive);

        if (me is null)
            return Fail<bool>("Conversation not found.", AuthErrorCode.NotFound);

        var isSelf = userId == targetUserId;

        if (!isSelf && (conversation.Type == ConversationType.Direct || me.Role != ParticipantRole.Admin))
            return Fail<bool>("Only group admins can remove other participants.", AuthErrorCode.Conflict);

        var target = conversation.Participants.FirstOrDefault(p => p.UserId == targetUserId && p.IsActive);

        if (target is null)
            return Fail<bool>("That person isn't in this conversation.", AuthErrorCode.NotFound);

        target.LeftAt = DateTime.UtcNow;

        // Don't strand a group without an admin.
        if (target.Role == ParticipantRole.Admin &&
            conversation.Type == ConversationType.Group &&
            conversation.Participants.Count(p => p.IsActive && p.Role == ParticipantRole.Admin) == 0)
        {
            var successor = conversation.Participants
                .Where(p => p.IsActive)
                .OrderBy(p => p.JoinedAt)
                .FirstOrDefault();

            if (successor is not null)
                successor.Role = ParticipantRole.Admin;
        }

        await repository.SaveAsync(cancellationToken);

        return Result<bool>.Success(true);
    }

    public async Task<bool> IsParticipantAsync(
        Guid userId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var participant = await repository.Conversations
            .GetParticipantAsync(conversationId, userId, false, cancellationToken);

        return participant is { LeftAt: null };
    }

    public Task<IReadOnlyList<Guid>> GetMyConversationIdsAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        repository.Conversations.GetConversationIdsForUserAsync(userId, cancellationToken);

    // ----- helpers -----

    // Who may message whom. Deliberately the same shape as the study-buddy
    // visibility rule: same school is always fine, and reaching across schools
    // needs either an accepted pairing or the other student being open to it.
    private async Task<Result<bool>> CanMessageAsync(
        Guid userId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        var me = await LoadUserAsync(userId, cancellationToken);
        var them = await LoadUserAsync(targetUserId, cancellationToken);

        if (me is null || them is null)
            return Result<bool>.Failure(AuthErrorCode.NotFound, "That student doesn't exist.");

        if (me.InstitutionId == them.InstitutionId)
            return Result<bool>.Success(true);

        // An accepted study buddy pairing is explicit consent to be contacted,
        // whatever the discovery settings say.
        var match = await repository.StudyBuddyMatches.GetBetweenAsync(userId, targetUserId, cancellationToken);

        if (match is { Status: MatchStatus.Accepted })
            return Result<bool>.Success(true);

        if (!them.AllowCrossInstitutionDiscovery ||
            them.MembershipStatus != InstitutionMembershipStatus.Verified)
            return Result<bool>.Failure(
                AuthErrorCode.NotFound, "That student isn't accepting messages from other institutions.");

        return Result<bool>.Success(true);
    }

    private Task<AppUser> LoadUserAsync(Guid userId, CancellationToken cancellationToken) =>
        userManager.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    private async Task<ConversationSummary> ToSummaryAsync(
        Conversation conversation,
        Guid viewerId,
        CancellationToken cancellationToken)
    {
        var mine = conversation.Participants.FirstOrDefault(p => p.UserId == viewerId);

        var lastMessage = await repository.ChatMessages
            .GetPage(conversation.Id, null, false)
            .Include(m => m.Sender)
            .FirstOrDefaultAsync(cancellationToken);

        var unread = await repository.ChatMessages
            .CountUnreadAsync(conversation.Id, mine?.LastReadAt, viewerId, cancellationToken);

        // A direct thread has no stored title — it's named after whoever you're
        // talking to, which differs per viewer.
        var title = conversation.Type == ConversationType.Group
            ? conversation.Title
            : conversation.Participants
                  .Where(p => p.UserId != viewerId)
                  .Select(p => $"{p.User?.FirstName} {p.User?.LastName}".Trim())
                  .FirstOrDefault()
              ?? "Conversation";

        return new ConversationSummary
        {
            Id = conversation.Id,
            Type = conversation.Type.ToString(),
            Title = title,
            StudyGroupId = conversation.StudyGroupId,
            CreatedAt = conversation.CreatedAt,
            LastMessageAt = conversation.LastMessageAt,
            UnreadCount = unread,
            LastReadAt = mine?.LastReadAt,
            IsMuted = mine?.MutedUntil > DateTime.UtcNow,
            MyRole = mine?.Role.ToString(),
            LastMessage = lastMessage is null ? null : ToMessageResponse(lastMessage),
            Participants = conversation.Participants.Select(p => new ChatParticipantResponse
            {
                UserId = p.UserId,
                UserName = p.User?.UserName,
                FullName = $"{p.User?.FirstName} {p.User?.LastName}".Trim(),
                ProfilePictureUrl = p.User?.ProfilePictureUrl,
                InstitutionName = p.User?.Institution?.Name,
                Role = p.Role.ToString(),
                HasLeft = !p.IsActive
            }).ToList()
        };
    }

    private async Task<ChatMessageResponse> LoadMessageResponseAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var message = await repository.ChatMessages
            .FindByCondition(m => m.Id == messageId, false)
            .Include(m => m.Sender)
            .FirstAsync(cancellationToken);

        return ToMessageResponse(message);
    }

    private static ChatMessageResponse ToMessageResponse(ChatMessage m) => new()
    {
        Id = m.Id,
        ConversationId = m.ConversationId,
        SenderId = m.SenderId,
        SenderUserName = m.Sender?.UserName,
        SenderFullName = m.Sender is null ? null : $"{m.Sender.FirstName} {m.Sender.LastName}".Trim(),
        Content = m.IsDeleted ? null : m.Content,
        SentAt = m.SentAt,
        EditedAt = m.EditedAt,
        IsDeleted = m.IsDeleted,
        ReplyToMessageId = m.ReplyToMessageId,
        ClientMessageId = m.ClientMessageId
    };

    private static Result<T> Fail<T>(string message, AuthErrorCode code) =>
        Result<T>.Failure(code, message);
}
