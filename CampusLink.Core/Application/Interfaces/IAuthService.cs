using CampusLink.Core.Application.Common;
using CampusLink.Core.Application.Dtos.Auth;

namespace CampusLink.Core.Application.Interfaces;

public interface IAuthService
{
    Task<Result<AuthResponse>> RegisterAsync(
        RegisterRequest request,
        string ipAddress,
        CancellationToken cancellationToken = default);

    // Identifier is a username, email, or phone number — resolved by the
    // service, the caller doesn't say which.
    Task<Result<AuthResponse>> LoginAsync(
        LoginRequest request,
        string ipAddress,
        CancellationToken cancellationToken = default);

    // Rotates the refresh token: the presented one is retired and a new pair
    // issued, so a stolen token is usable at most once before reuse is detected.
    Task<Result<AuthResponse>> RefreshAsync(
        string refreshToken,
        string ipAddress,
        CancellationToken cancellationToken = default);

    // Logout. Idempotent — revoking an already-dead token is not an error.
    Task<Result<bool>> RevokeAsync(
        string refreshToken,
        string ipAddress,
        CancellationToken cancellationToken = default);

    Task<Result<UserSummary>> GetCurrentUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
