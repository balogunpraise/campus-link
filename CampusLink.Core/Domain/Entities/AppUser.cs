using CampusLink.Core.Domain.Entities.LinkingEntities;
using CampusLink.Core.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace CampusLink.Core.Domain.Entities;

public class AppUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    // Only unique within an institution — two schools can issue the same number.
    // Null when the student didn't give one: the uniqueness index skips nulls,
    // so an empty string here would collide across every such account.
    public string? StudentNumber { get; set; }
    public string Department { get; set; } = string.Empty;
    public int YearOfStudy { get; set; }
    public string Bio { get; set; }
    public string ProfilePictureUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ----- Institution membership -----
    public Guid InstitutionId { get; set; }
    public Institution Institution { get; set; } = null!;

    // Unverified members are confined to their own institution; verification
    // (matching email domain or admin approval) unlocks cross-school features.
    public InstitutionMembershipStatus MembershipStatus { get; set; } = InstitutionMembershipStatus.Unverified;
    public DateTime? VerifiedAt { get; set; }

    // Cross-school coordination needs these; defaults come from the institution.
    public string? TimeZoneId { get; set; }
    public string? City { get; set; }

    // ISO 3166-1 alpha-2, matching Institution.CountryCode.
    public string? CountryCode { get; set; }

    // Master switch: opt out of being surfaced to students from other schools.
    public bool AllowCrossInstitutionDiscovery { get; set; } = true;

    // Navigation properties
    public ICollection<Resource> OwnedResources { get; set; } = new List<Resource>();
    public ICollection<ResourceShareRequest> ShareRequestsMade { get; set; } = new List<ResourceShareRequest>();
    public StudyBuddyProfile? StudyBuddyProfile { get; set; }
    public ICollection<StudyBuddyMatch> MatchesInitiated { get; set; } = new List<StudyBuddyMatch>();
    public ICollection<StudyBuddyMatch> MatchesReceived { get; set; } = new List<StudyBuddyMatch>();
    public ICollection<StudyGroupMember> GroupMemberships { get; set; } = new List<StudyGroupMember>();
    public ICollection<UserCourse> EnrolledCourses { get; set; } = new List<UserCourse>();
    public ICollection<Review> ReviewsGiven { get; set; } = new List<Review>();
    public ICollection<Review> ReviewsReceived { get; set; } = new List<Review>();

    public bool IsVerifiedMember => MembershipStatus == InstitutionMembershipStatus.Verified;
}
