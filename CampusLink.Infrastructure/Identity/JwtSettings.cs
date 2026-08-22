namespace CampusLink.Infrastructure.Identity;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; }
    public string Audience { get; set; }

    // HMAC signing key. Must come from configuration/secrets, never source.
    public string Key { get; set; }

    // Short by design: the access token can't be revoked, so its blast radius
    // is however long this is. Sessions survive via the refresh token.
    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 14;

    // Allowed clock drift between this server and whoever issued the token.
    public int ClockSkewSeconds { get; set; } = 30;
}

// Claim types beyond the standard set. InstitutionId in particular is on the
// token so authorization can scope a request to one school without a database
// round-trip on every call.
public static class CampusLinkClaims
{
    public const string InstitutionId = "institution_id";
    public const string MembershipStatus = "membership_status";
    public const string FullName = "full_name";
}
