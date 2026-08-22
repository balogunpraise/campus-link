using CampusLink.Core.Application.Interfaces.Repositories;
using CampusLink.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CampusLink.Infrastructure.Data.Repositories;

public class RefreshTokenRepository(AppDbContext context)
    : RepositoryBase<RefreshToken>(context), IRefreshTokenRepository
{
    public Task<RefreshToken> GetByHashAsync(
        string tokenHash,
        bool trackChanges,
        CancellationToken cancellationToken = default) =>
        FindByCondition(t => t.TokenHash == tokenHash, trackChanges)
            .FirstOrDefaultAsync(cancellationToken);

    public IQueryable<RefreshToken> GetActiveForUser(Guid userId, bool trackChanges) =>
        FindByCondition(
                t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow,
                trackChanges)
            .OrderByDescending(t => t.CreatedAt);

    public Task<int> RevokeAllForUserAsync(
        Guid userId,
        string reason,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Bulk update: this runs on password change and on token-reuse
        // detection, where the point is to kill sessions immediately rather
        // than wait for the caller's SaveAsync.
        return FindByCondition(t => t.UserId == userId && t.RevokedAt == null, false)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.RevokedAt, now)
                      .SetProperty(t => t.RevokedReason, reason)
                      .SetProperty(t => t.RevokedByIp, ipAddress),
                cancellationToken);
    }

    public Task<int> DeleteExpiredAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default) =>
        FindByCondition(t => t.ExpiresAt < olderThanUtc, false)
            .ExecuteDeleteAsync(cancellationToken);
}
