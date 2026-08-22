namespace CampusLink.Core.Domain.Entities;

// Access tokens are short-lived and stateless; this is the long-lived half that
// lets a student stay signed in. Persisted (rather than another JWT) so a
// session can actually be revoked — logout, password change, or a stolen token.
public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    // SHA-256 of the token we handed out, never the token itself: a leaked
    // database shouldn't hand over working sessions.
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }

    // Set when this token was rotated on refresh. If a token that was already
    // replaced is presented again, it was probably stolen — the reuse detection
    // in AuthService revokes the whole chain.
    public string ReplacedByTokenHash { get; set; }

    public string CreatedByIp { get; set; }
    public string RevokedByIp { get; set; }

    // Why it was revoked, for support ("logged out", "password changed").
    public string RevokedReason { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsActive => !IsRevoked && !IsExpired;
}
