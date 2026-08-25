using CampusLink.Core.Application.Interfaces.Repositories;
using CampusLink.Core.Domain.Entities;
using CampusLink.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CampusLink.Infrastructure.Data.Repositories;

public class CallRepository(AppDbContext context)
    : RepositoryBase<Call>(context), ICallRepository
{
    public Task<Call> GetActiveForConversationAsync(
        Guid conversationId,
        bool trackChanges,
        CancellationToken cancellationToken = default) =>
        FindByCondition(
                c => c.ConversationId == conversationId &&
                     (c.Status == CallStatus.Ringing || c.Status == CallStatus.Active),
                trackChanges)
            .Include(c => c.Participants).ThenInclude(p => p.User)
            .Include(c => c.StartedBy)
            .OrderByDescending(c => c.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Call> GetWithParticipantsAsync(
        Guid callId,
        bool trackChanges,
        CancellationToken cancellationToken = default) =>
        FindByCondition(c => c.Id == callId, trackChanges)
            .Include(c => c.Participants).ThenInclude(p => p.User)
            .Include(c => c.StartedBy)
            .FirstOrDefaultAsync(cancellationToken);

    public IQueryable<Call> GetHistory(Guid conversationId, bool trackChanges) =>
        FindByCondition(c => c.ConversationId == conversationId, trackChanges)
            .Include(c => c.Participants).ThenInclude(p => p.User)
            .Include(c => c.StartedBy)
            .OrderByDescending(c => c.StartedAt);

    public IQueryable<Call> GetStaleRinging(DateTime startedBeforeUtc, bool trackChanges) =>
        FindByCondition(
                c => c.Status == CallStatus.Ringing && c.StartedAt < startedBeforeUtc,
                trackChanges)
            .Include(c => c.Participants);

    public Task<CallParticipant> GetParticipantAsync(
        Guid callId,
        Guid userId,
        bool trackChanges,
        CancellationToken cancellationToken = default)
    {
        var query = Context.Set<CallParticipant>().AsQueryable();

        if (!trackChanges)
            query = query.AsNoTracking();

        return query.FirstOrDefaultAsync(p => p.CallId == callId && p.UserId == userId, cancellationToken);
    }
}
