using CampusLink.Core.Application.Common;
using CampusLink.Core.Application.Dtos.Chat;

namespace CampusLink.Core.Application.Interfaces;

// All chat rules live here, not in the hub. The SignalR hub and the REST
// controller are two doors into the same service, so a client that can't hold a
// socket open still gets identical behaviour and identical permission checks.
public interface IChatService
{
    Task<Result<ConversationSummary>> StartDirectAsync(
        Guid userId,
        Guid recipientUserId,
        CancellationToken cancellationToken = default);

    Task<Result<ConversationSummary>> CreateGroupAsync(
        Guid userId,
        CreateGroupConversationRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ConversationSummary>>> GetMyConversationsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<ConversationSummary>> GetConversationAsync(
        Guid userId,
        Guid conversationId,
        CancellationToken cancellationToken = default);

    Task<Result<MessagePage>> GetMessagesAsync(
        Guid userId,
        Guid conversationId,
        DateTime? before,
        int limit,
        CancellationToken cancellationToken = default);

    // Persists first, then the caller broadcasts — a message that didn't commit
    // must never appear in someone's window.
    Task<Result<ChatMessageResponse>> SendMessageAsync(
        Guid userId,
        Guid conversationId,
        SendMessageRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<ChatMessageResponse>> EditMessageAsync(
        Guid userId,
        Guid messageId,
        string content,
        CancellationToken cancellationToken = default);

    Task<Result<ChatMessageResponse>> DeleteMessageAsync(
        Guid userId,
        Guid messageId,
        CancellationToken cancellationToken = default);

    Task<Result<ReadReceiptEvent>> MarkReadAsync(
        Guid userId,
        Guid conversationId,
        DateTime? readAt,
        CancellationToken cancellationToken = default);

    Task<Result<ConversationSummary>> AddParticipantsAsync(
        Guid userId,
        Guid conversationId,
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken = default);

    // Removing yourself is leaving; removing someone else requires admin.
    Task<Result<bool>> RemoveParticipantAsync(
        Guid userId,
        Guid conversationId,
        Guid targetUserId,
        CancellationToken cancellationToken = default);

    // Membership check used by the hub before joining a connection to a group.
    Task<bool> IsParticipantAsync(
        Guid userId,
        Guid conversationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetMyConversationIdsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

// Implemented in the API layer over SignalR. The service depends on this rather
// than on IHubContext so Infrastructure doesn't have to know a hub exists.
public interface IChatNotifier
{
    Task MessageSentAsync(Guid conversationId, ChatMessageResponse message);
    Task MessageUpdatedAsync(Guid conversationId, ChatMessageResponse message);
    Task ConversationChangedAsync(Guid conversationId, IReadOnlyList<Guid> participantUserIds);
    Task ReadReceiptAsync(Guid conversationId, ReadReceiptEvent receipt);
}
