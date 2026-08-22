using CampusLink.Core.Domain.Enums;

namespace CampusLink.Core.Domain.Entities;

// A school/university. Every user belongs to exactly one institution, but
// almost everything else (resources, study groups, buddy matching) can be
// opened up beyond it — see VisibilityScope.
public class Institution : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    // Short handle used in URLs and display, e.g. "unilag", "mit".
    public string Slug { get; set; } = string.Empty;

    public string ShortName { get; set; }
    public InstitutionType Type { get; set; } = InstitutionType.University;

    // ISO 3166-1 alpha-2, uppercase ("NG", "GB", "CN"). A code rather than a
    // display name so filtering works the same for every country and the client
    // owns localization of the label.
    public string CountryCode { get; set; } = string.Empty;

    public string State { get; set; }
    public string City { get; set; }

    // Default time zone for the campus; users can override on their profile.
    public string TimeZoneId { get; set; }

    public string LogoUrl { get; set; }
    public string WebsiteUrl { get; set; }

    // Institutions can be created by students signing up before an admin has
    // vetted the school. Unverified ones are still usable but flagged in the UI.
    public bool IsVerified { get; set; }

    // Lets a school opt its students out of cross-institution discovery.
    public bool AllowsCrossInstitutionSharing { get; set; } = true;

    // Set when a student proposed this school through sign-up rather than it
    // coming from the seed dataset or an admin. Feeds the moderation queue.
    public bool IsUserSubmitted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<InstitutionEmailDomain> EmailDomains { get; set; } = new List<InstitutionEmailDomain>();
    public ICollection<AppUser> Members { get; set; } = new List<AppUser>();
    public ICollection<Course> Courses { get; set; } = new List<Course>();
}

// Email domains that prove membership of an institution (a school often has
// several: "student.unilag.edu.ng", "unilag.edu.ng", ...). Sign-up matches the
// user's email suffix against these to auto-verify them.
public class InstitutionEmailDomain : BaseEntity
{
    public Guid InstitutionId { get; set; }
    public Institution Institution { get; set; } = null!;

    // Stored lowercase, without the "@", e.g. "student.unilag.edu.ng".
    public string Domain { get; set; } = string.Empty;

    // Alumni/staff domains shouldn't necessarily grant student status.
    public bool IsStudentDomain { get; set; } = true;
}
