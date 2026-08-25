using CampusLink.Core.Domain.Entities;

namespace CampusLink.Core.Application.Interfaces.Repositories;

public interface IConversationRepository : IRepositoryBase<Conversation>
{
    // The caller's threads, most recently active first.
    IQueryable<Conversation> GetForUser(Guid userId, bool trackChanges);

    // Resolves an existing direct thread between two students, whichever order
    // they're given in.
    Task<Conversation> GetDirectAsync(
        Guid userAId,
        Guid userBId,
        bool trackChanges,
        CancellationToken cancellationToken = default);

    Task<Conversation> GetWithParticipantsAsync(
        Guid conversationId,
        bool trackChanges,
        CancellationToken cancellationToken = default);

    // Membership check that doesn't drag the whole graph back — called on every
    // send, so it stays a single indexed lookup.
    Task<ConversationParticipant> GetParticipantAsync(
        Guid conversationId,
        Guid userId,
        bool trackChanges,
        CancellationToken cancellationToken = default);

    // Every conversation id the user belongs to — the hub subscribes a new
    // connection to exactly these groups.
    Task<IReadOnlyList<Guid>> GetConversationIdsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetParticipantUserIdsAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);
}

public interface IChatMessageRepository : IRepositoryBase<ChatMessage>
{
    // Newest-first page. `before` is the cursor: keyset pagination, because
    // offset paging over a busy thread shifts under you as messages arrive.
    IQueryable<ChatMessage> GetPage(Guid conversationId, DateTime? before, bool trackChanges);

    Task<int> CountUnreadAsync(
        Guid conversationId,
        DateTime? lastReadAt,
        Guid excludeSenderId,
        CancellationToken cancellationToken = default);
}
