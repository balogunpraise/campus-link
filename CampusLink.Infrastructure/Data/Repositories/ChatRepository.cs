using CampusLink.Core.Application.Interfaces.Repositories;
using CampusLink.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CampusLink.Infrastructure.Data.Repositories;

public class ConversationRepository(AppDbContext context)
    : RepositoryBase<Conversation>(context), IConversationRepository
{
    public IQueryable<Conversation> GetForUser(Guid userId, bool trackChanges) =>
        // Threads they've left drop off the list but the rows survive.
        FindAll(trackChanges)
            .Where(c => c.Participants.Any(p => p.UserId == userId && p.LeftAt == null))
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt);

    public Task<Conversation> GetDirectAsync(
        Guid userAId,
        Guid userBId,
        bool trackChanges,
        CancellationToken cancellationToken = default)
    {
        var key = Conversation.BuildDirectKey(userAId, userBId);

        return FindByCondition(c => c.DirectKey == key, trackChanges)
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<Conversation> GetWithParticipantsAsync(
        Guid conversationId,
        bool trackChanges,
        CancellationToken cancellationToken = default) =>
        FindByCondition(c => c.Id == conversationId, trackChanges)
            .Include(c => c.Participants).ThenInclude(p => p.User)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<ConversationParticipant> GetParticipantAsync(
        Guid conversationId,
        Guid userId,
        bool trackChanges,
        CancellationToken cancellationToken = default)
    {
        var query = Context.Set<ConversationParticipant>().AsQueryable();

        if (!trackChanges)
            query = query.AsNoTracking();

        return query.FirstOrDefaultAsync(
            p => p.ConversationId == conversationId && p.UserId == userId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetConversationIdsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await Context.Set<ConversationParticipant>()
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.LeftAt == null)
            .Select(p => p.ConversationId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetParticipantUserIdsAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default) =>
        await Context.Set<ConversationParticipant>()
            .AsNoTracking()
            .Where(p => p.ConversationId == conversationId && p.LeftAt == null)
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken);
}

public class ChatMessageRepository(AppDbContext context)
    : RepositoryBase<ChatMessage>(context), IChatMessageRepository
{
    public IQueryable<ChatMessage> GetPage(Guid conversationId, DateTime? before, bool trackChanges)
    {
        var query = FindByCondition(m => m.ConversationId == conversationId, trackChanges);

        // Keyset cursor rather than Skip(): an offset shifts underneath the
        // client every time a new message lands mid-scroll.
        if (before.HasValue)
            query = query.Where(m => m.SentAt < before.Value);

        return query.OrderByDescending(m => m.SentAt);
    }

    public Task<int> CountUnreadAsync(
        Guid conversationId,
        DateTime? lastReadAt,
        Guid excludeSenderId,
        CancellationToken cancellationToken = default)
    {
        // Your own messages are never unread to you.
        var query = FindByCondition(
            m => m.ConversationId == conversationId &&
                 m.SenderId != excludeSenderId &&
                 m.DeletedAt == null,
            false);

        if (lastReadAt.HasValue)
            query = query.Where(m => m.SentAt > lastReadAt.Value);

        return query.CountAsync(cancellationToken);
    }
}
