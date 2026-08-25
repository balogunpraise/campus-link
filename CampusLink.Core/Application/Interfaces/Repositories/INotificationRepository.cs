using CampusLink.Core.Domain.Entities;

namespace CampusLink.Core.Application.Interfaces.Repositories;

public interface INotificationRepository : IRepositoryBase<Notification>
{
    IQueryable<Notification> GetForUser(Guid userId, bool onlyUnread, bool trackChanges);

    Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default);
}

public interface IReviewRepository : IRepositoryBase<Review>
{
    IQueryable<Review> GetForReviewee(Guid revieweeId, bool trackChanges);

    // Null when the reviewee has no reviews yet.
    Task<double?> GetAverageRatingAsync(Guid revieweeId, CancellationToken cancellationToken = default);

    // One review per interaction; pass whichever of the two ids applies.
    Task<bool> HasReviewedAsync(
        Guid reviewerId,
        Guid? shareRequestId,
        Guid? matchId,
        CancellationToken cancellationToken = default);
}
