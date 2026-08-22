using System.ComponentModel.DataAnnotations;

namespace CampusLink.Core.Application.Dtos.Auth;

public class RegisterRequest
{
    [Required, StringLength(64, MinimumLength = 3)]
    public string UserName { get; set; }

    [Required, EmailAddress]
    public string Email { get; set; }

    // Optional, but if given it becomes a third way to sign in, so it has to be
    // unique across the platform.
    [Phone]
    public string PhoneNumber { get; set; }

    [Required, StringLength(128, MinimumLength = 8)]
    public string Password { get; set; }

    [Required, StringLength(64)]
    public string FirstName { get; set; }

    [Required, StringLength(64)]
    public string LastName { get; set; }

    // Optional: when the email domain already identifies a school we use that
    // and ignore this. Required only when the domain is unknown (e.g. a gmail
    // address), in which case the account stays Unverified until an admin or
    // an institution email confirms it.
    public Guid? InstitutionId { get; set; }

    [StringLength(32)]
    public string StudentNumber { get; set; }

    [StringLength(128)]
    public string Department { get; set; }

    public int YearOfStudy { get; set; }
}

// One field for all three identifier types. The client doesn't have to know or
// tell us which one the student typed.
public class LoginRequest
{
    [Required]
    public string Identifier { get; set; }

    [Required]
    public string Password { get; set; }
}

public class RefreshRequest
{
    [Required]
    public string RefreshToken { get; set; }
}

public class RevokeRequest
{
    [Required]
    public string RefreshToken { get; set; }
}

public class AuthResponse
{
    public string AccessToken { get; set; }
    public string RefreshToken { get; set; }
    public DateTime AccessTokenExpiresAt { get; set; }
    public DateTime RefreshTokenExpiresAt { get; set; }
    public UserSummary User { get; set; }
}

public class UserSummary
{
    public Guid Id { get; set; }
    public string UserName { get; set; }
    public string Email { get; set; }
    public string PhoneNumber { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }

    public Guid InstitutionId { get; set; }
    public string InstitutionName { get; set; }

    // Unverified members are confined to their own school, so the client needs
    // this to decide whether to show cross-institution features.
    public string MembershipStatus { get; set; }
    public bool IsVerifiedMember { get; set; }

    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
}
