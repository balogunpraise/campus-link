using CampusLink.Core.Domain.Entities;

namespace CampusLink.Core.Application.Interfaces.Repositories;

public interface IRefreshTokenRepository : IRepositoryBase<RefreshToken>
{
    // Lookup is by hash — we never store the token we handed out.
    Task<RefreshToken> GetByHashAsync(
        string tokenHash,
        bool trackChanges,
        CancellationToken cancellationToken = default);

    IQueryable<RefreshToken> GetActiveForUser(Guid userId, bool trackChanges);

    // Kills every live session for a user: password change, or reuse of an
    // already-rotated token (which means someone else has a copy).
    Task<int> RevokeAllForUserAsync(
        Guid userId,
        string reason,
        string ipAddress,
        CancellationToken cancellationToken = default);

    // Housekeeping: drop tokens that expired or were revoked long ago.
    Task<int> DeleteExpiredAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default);
}
