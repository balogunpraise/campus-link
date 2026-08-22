using CampusLink.Core.Application.Common;
using CampusLink.Core.Application.Dtos.Auth;
using CampusLink.Core.Application.Interfaces;
using CampusLink.Core.Application.Interfaces.Repositories;
using CampusLink.Core.Domain.Entities;
using CampusLink.Core.Domain.Enums;
using CampusLink.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CampusLink.Infrastructure.Services;

public class AuthService(
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    IJwtTokenGenerator tokenGenerator,
    IRepositoryManager repository,
    ILogger<AuthService> logger) : IAuthService
{
    private const string DefaultRole = "Student";

    public async Task<Result<AuthResponse>> RegisterAsync(
        RegisterRequest request,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var userName = request.UserName.Trim();
        var phone = NormalizePhone(request.PhoneNumber);

        if (await userManager.FindByNameAsync(userName) is not null)
            return Result<AuthResponse>.Failure(AuthErrorCode.Conflict, "That username is already taken.");

        if (await userManager.FindByEmailAsync(email) is not null)
            return Result<AuthResponse>.Failure(AuthErrorCode.Conflict, "That email is already registered.");

        // Phone is a login identifier, so it has to be unique — Identity does
        // not enforce this on its own.
        // Checked against both "+234..." and "234..." forms, otherwise the same
        // number could be registered twice and login would be ambiguous.
        if (phone is not null &&
            await userManager.Users.AnyAsync(
                u => PhoneVariants(phone).Contains(u.PhoneNumber), cancellationToken))
            return Result<AuthResponse>.Failure(AuthErrorCode.Conflict, "That phone number is already registered.");

        var institutionResult = await ResolveInstitutionAsync(email, request.InstitutionId, cancellationToken);
        if (!institutionResult.Succeeded)
            return Result<AuthResponse>.Failure(institutionResult.ErrorCode, institutionResult.Errors);

        var (institution, isDomainVerified) = institutionResult.Value;

        var user = new AppUser
        {
            UserName = userName,
            Email = email,
            PhoneNumber = phone,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            StudentNumber = request.StudentNumber?.Trim() ?? string.Empty,
            Department = request.Department?.Trim() ?? string.Empty,
            YearOfStudy = request.YearOfStudy,
            InstitutionId = institution.Id,

            // A matching institution email domain is the whole point of the
            // verification design: it's what unlocks cross-school features.
            MembershipStatus = isDomainVerified
                ? InstitutionMembershipStatus.Verified
                : InstitutionMembershipStatus.Unverified,
            VerifiedAt = isDomainVerified ? DateTime.UtcNow : null,

            // Sensible defaults for cross-school scheduling; the student can
            // override them on their profile.
            TimeZoneId = institution.TimeZoneId,
            City = institution.City,
            CountryCode = institution.CountryCode
        };

        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
            return Result<AuthResponse>.Failure(
                AuthErrorCode.ValidationFailed,
                created.Errors.Select(e => e.Description));

        await EnsureRoleExistsAsync(DefaultRole);
        await userManager.AddToRoleAsync(user, DefaultRole);

        logger.LogInformation(
            "Registered user {UserId} at institution {InstitutionId} (verified: {Verified})",
            user.Id, institution.Id, isDomainVerified);

        return Result<AuthResponse>.Success(
            await IssueTokensAsync(user, ipAddress, cancellationToken));
    }

    public async Task<Result<AuthResponse>> LoginAsync(
        LoginRequest request,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(request.Identifier, cancellationToken);

        // Same response whether the account doesn't exist or the password is
        // wrong — otherwise this endpoint enumerates who's registered.
        if (user is null)
        {
            logger.LogInformation("Failed login for unknown identifier from {Ip}", ipAddress);
            return InvalidCredentials();
        }

        if (await userManager.IsLockedOutAsync(user))
            return Result<AuthResponse>.Failure(
                AuthErrorCode.AccountLocked,
                "Too many failed attempts. Try again later.");

        if (user.MembershipStatus == InstitutionMembershipStatus.Revoked)
            return Result<AuthResponse>.Failure(
                AuthErrorCode.AccountLocked,
                "This account's institution membership has been revoked. Contact your school administrator.");

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            // Feeds Identity's lockout counter.
            await userManager.AccessFailedAsync(user);
            logger.LogInformation("Failed login for user {UserId} from {Ip}", user.Id, ipAddress);

            return InvalidCredentials();
        }

        await userManager.ResetAccessFailedCountAsync(user);

        return Result<AuthResponse>.Success(await IssueTokensAsync(user, ipAddress, cancellationToken));
    }

    public async Task<Result<AuthResponse>> RefreshAsync(
        string refreshToken,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        var hash = tokenGenerator.HashRefreshToken(refreshToken);
        var stored = await repository.RefreshTokens.GetByHashAsync(hash, true, cancellationToken);

        if (stored is null)
            return Result<AuthResponse>.Failure(AuthErrorCode.InvalidToken, "Invalid refresh token.");

        // Reuse detection: this token was already rotated away, so whoever just
        // presented it is holding a copy of a spent credential. Assume theft and
        // drop every session for the account.
        if (stored.ReplacedByTokenHash is not null)
        {
            await repository.RefreshTokens.RevokeAllForUserAsync(
                stored.UserId, "Refresh token reuse detected", ipAddress, cancellationToken);

            logger.LogWarning(
                "Refresh token reuse detected for user {UserId} from {Ip}; all sessions revoked",
                stored.UserId, ipAddress);

            return Result<AuthResponse>.Failure(
                AuthErrorCode.InvalidToken, "Invalid refresh token. Please sign in again.");
        }

        if (!stored.IsActive)
            return Result<AuthResponse>.Failure(AuthErrorCode.InvalidToken, "Refresh token has expired.");

        var user = await userManager.Users
            .Include(u => u.Institution)
            .FirstOrDefaultAsync(u => u.Id == stored.UserId, cancellationToken);

        if (user is null || user.MembershipStatus == InstitutionMembershipStatus.Revoked)
            return Result<AuthResponse>.Failure(AuthErrorCode.InvalidToken, "Invalid refresh token.");

        var response = await IssueTokensAsync(user, ipAddress, cancellationToken, rotating: stored);

        return Result<AuthResponse>.Success(response);
    }

    public async Task<Result<bool>> RevokeAsync(
        string refreshToken,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        var hash = tokenGenerator.HashRefreshToken(refreshToken);
        var stored = await repository.RefreshTokens.GetByHashAsync(hash, true, cancellationToken);

        // Idempotent: logging out twice, or with a token that already expired,
        // is a success from the caller's point of view.
        if (stored is null || !stored.IsActive)
            return Result<bool>.Success(true);

        stored.RevokedAt = DateTime.UtcNow;
        stored.RevokedByIp = ipAddress;
        stored.RevokedReason = "Signed out";

        repository.RefreshTokens.Update(stored);
        await repository.SaveAsync(cancellationToken);

        return Result<bool>.Success(true);
    }

    public async Task<Result<UserSummary>> GetCurrentUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.Users
            .Include(u => u.Institution)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
            return Result<UserSummary>.Failure(AuthErrorCode.NotFound, "User not found.");

        return Result<UserSummary>.Success(await ToSummaryAsync(user, cancellationToken));
    }

    // ----- helpers -----

    // The three-identifier login. Cheapest disambiguation first: an "@" can
    // only be an email, a leading "+" or all-digits can only be a phone.
    // Anything else is a username — but we still fall back across the other two
    // so an odd-looking username or a phone typed with spaces still resolves.
    private async Task<AppUser> ResolveUserAsync(string identifier, CancellationToken cancellationToken)
    {
        var trimmed = identifier?.Trim();

        if (string.IsNullOrEmpty(trimmed))
            return null;

        if (trimmed.Contains('@'))
            return await userManager.FindByEmailAsync(trimmed.ToLowerInvariant());

        var phone = NormalizePhone(trimmed);
        if (phone is not null)
        {
            var candidates = PhoneVariants(phone);

            var byPhone = await userManager.Users
                .FirstOrDefaultAsync(u => candidates.Contains(u.PhoneNumber), cancellationToken);

            if (byPhone is not null)
                return byPhone;
        }

        return await userManager.FindByNameAsync(trimmed);
    }

    // Phone numbers are stored stripped of spaces, dashes and parentheses (the
    // leading "+" is kept) so the same number typed three different ways still
    // matches one row. Returns null for anything that isn't plausibly a phone
    // number, which is how the caller knows to treat it as a username.
    private static string NormalizePhone(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        var hasPlus = trimmed.StartsWith('+');
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());

        if (digits.Length is < 7 or > 15)
            return null;

        // A username made only of digits would collide with this; requiring 7+
        // digits keeps that unlikely, and the username fallback still runs.
        if (trimmed.Any(c => !char.IsDigit(c) && c is not ('+' or '-' or ' ' or '(' or ')')))
            return null;

        return hasPlus ? $"+{digits}" : digits;
    }

    // Students type the leading "+" inconsistently, and "+2348012345678" and
    // "2348012345678" are the same number, so both forms are always checked.
    // (A local form like "08012345678" is a genuinely different digit string —
    // reconciling that needs a country code and E.164 parsing.)
    private static string[] PhoneVariants(string phone) =>
        phone.StartsWith('+')
            ? [phone, phone[1..]]
            : [phone, $"+{phone}"];

    private async Task<Result<(Institution Institution, bool IsDomainVerified)>> ResolveInstitutionAsync(
        string email,
        Guid? requestedInstitutionId,
        CancellationToken cancellationToken)
    {
        var domain = email[(email.IndexOf('@') + 1)..];
        var byDomain = await repository.Institutions.GetByEmailDomainAsync(domain, cancellationToken);

        // A recognised school domain wins over whatever the client sent — you
        // can't claim to be at a different school than your email says.
        if (byDomain is not null)
            return Result<(Institution, bool)>.Success((byDomain, true));

        if (!requestedInstitutionId.HasValue)
            return Result<(Institution, bool)>.Failure(
                AuthErrorCode.ValidationFailed,
                "We don't recognise that email domain. Please select your institution.");

        var institution = await repository.Institutions
            .FindByIdAsync(requestedInstitutionId.Value, false, cancellationToken);

        if (institution is null)
            return Result<(Institution, bool)>.Failure(AuthErrorCode.NotFound, "Institution not found.");

        return Result<(Institution, bool)>.Success((institution, false));
    }

    private async Task<AuthResponse> IssueTokensAsync(
        AppUser user,
        string ipAddress,
        CancellationToken cancellationToken,
        RefreshToken rotating = null)
    {
        var roles = await userManager.GetRolesAsync(user);
        var (accessToken, accessExpires) = tokenGenerator.CreateAccessToken(user, roles);
        var (refreshToken, refreshHash, refreshExpires) = tokenGenerator.CreateRefreshToken();

        await repository.RefreshTokens.CreateAsync(
            new RefreshToken
            {
                UserId = user.Id,
                TokenHash = refreshHash,
                ExpiresAt = refreshExpires,
                CreatedByIp = ipAddress
            },
            cancellationToken);

        // Rotation: retire the presented token in the same transaction as the
        // new one, so there's never a window with two live tokens.
        if (rotating is not null)
        {
            rotating.RevokedAt = DateTime.UtcNow;
            rotating.RevokedByIp = ipAddress;
            rotating.RevokedReason = "Rotated on refresh";
            rotating.ReplacedByTokenHash = refreshHash;

            repository.RefreshTokens.Update(rotating);
        }

        await repository.SaveAsync(cancellationToken);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAt = accessExpires,
            RefreshTokenExpiresAt = refreshExpires,
            User = await ToSummaryAsync(user, cancellationToken, roles)
        };
    }

    private async Task<UserSummary> ToSummaryAsync(
        AppUser user,
        CancellationToken cancellationToken,
        IList<string> roles = null)
    {
        roles ??= await userManager.GetRolesAsync(user);

        // Only the paths that already joined Institution have it loaded; the
        // rest pay one small lookup rather than dragging the join everywhere.
        var institutionName = user.Institution?.Name
                              ?? (await repository.Institutions
                                  .FindByIdAsync(user.InstitutionId, false, cancellationToken))?.Name;

        return new UserSummary
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            FirstName = user.FirstName,
            LastName = user.LastName,
            InstitutionId = user.InstitutionId,
            InstitutionName = institutionName,
            MembershipStatus = user.MembershipStatus.ToString(),
            IsVerifiedMember = user.IsVerifiedMember,
            Roles = roles.ToArray()
        };
    }

    private async Task EnsureRoleExistsAsync(string role)
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new AppRole { Name = role });
    }

    private static Result<AuthResponse> InvalidCredentials() =>
        Result<AuthResponse>.Failure(AuthErrorCode.InvalidCredentials, "Invalid credentials.");
}
