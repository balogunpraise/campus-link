using CampusLink.Core.Application.Interfaces.Repositories;
using CampusLink.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CampusLink.Infrastructure.Data.Repositories;

public class NotificationRepository(AppDbContext context)
    : RepositoryBase<Notification>(context), INotificationRepository
{
    public IQueryable<Notification> GetForUser(Guid userId, bool onlyUnread, bool trackChanges)
    {
        var query = FindByCondition(n => n.UserId == userId, trackChanges);

        if (onlyUnread)
            query = query.Where(n => !n.IsRead);

        return query.OrderByDescending(n => n.CreatedAt);
    }

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        FindByCondition(n => n.UserId == userId && !n.IsRead, false)
            .CountAsync(cancellationToken);

    public Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        FindByCondition(n => n.UserId == userId && !n.IsRead, false)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), cancellationToken);
}

public class ReviewRepository(AppDbContext context)
    : RepositoryBase<Review>(context), IReviewRepository
{
    public IQueryable<Review> GetForReviewee(Guid revieweeId, bool trackChanges) =>
        FindByCondition(r => r.RevieweeId == revieweeId, trackChanges)
            .Include(r => r.Reviewer)
            .OrderByDescending(r => r.CreatedAt);

    public async Task<double?> GetAverageRatingAsync(
        Guid revieweeId,
        CancellationToken cancellationToken = default) =>
        // AverageAsync throws on an empty set, so project to a nullable first:
        // a student with no reviews yet is normal, not an error.
        await FindByCondition(r => r.RevieweeId == revieweeId, false)
            .AverageAsync(r => (double?)r.Rating, cancellationToken);

    public Task<bool> HasReviewedAsync(
        Guid reviewerId,
        Guid? shareRequestId,
        Guid? matchId,
        CancellationToken cancellationToken = default) =>
        ExistsAsync(
            r => r.ReviewerId == reviewerId &&
                 ((shareRequestId != null && r.RelatedShareRequestId == shareRequestId) ||
                  (matchId != null && r.RelatedMatchId == matchId)),
            cancellationToken);
}
