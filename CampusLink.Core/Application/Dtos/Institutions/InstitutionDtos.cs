using System.ComponentModel.DataAnnotations;
using CampusLink.Core.Application.Dtos.Common;
using CampusLink.Core.Domain.Enums;

namespace CampusLink.Core.Application.Dtos.Institutions;

public class InstitutionSearchRequest : PageRequest
{
    // Free text: name or short name, typo-tolerant.
    public string Search { get; set; }

    // ISO 3166-1 alpha-2. Optional — an international student may be looking
    // for a school outside the country they're browsing from.
    public string CountryCode { get; set; }

    // Hide schools students typed in themselves but nobody has vetted.
    public bool VerifiedOnly { get; set; }
}

public class InstitutionSummary
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Slug { get; set; }
    public string ShortName { get; set; }
    public string Type { get; set; }
    public string CountryCode { get; set; }
    public string State { get; set; }
    public string City { get; set; }
    public string LogoUrl { get; set; }
    public bool IsVerified { get; set; }
}

public class InstitutionDetail : InstitutionSummary
{
    public string WebsiteUrl { get; set; }
    public string TimeZoneId { get; set; }
    public bool AllowsCrossInstitutionSharing { get; set; }
    public IReadOnlyList<string> EmailDomains { get; set; } = Array.Empty<string>();
    public int MemberCount { get; set; }
}

public class CountrySummary
{
    public string CountryCode { get; set; }
    public int InstitutionCount { get; set; }
}

// Result of matching an email address against the known domains. The client
// calls this when the email field blurs, so most students never see a picker.
public class InstitutionResolveResponse
{
    public bool Resolved { get; set; }
    public string Domain { get; set; }
    public InstitutionSummary Institution { get; set; }

    // Only meaningful when Resolved is false: this is what the student should
    // do instead (search the directory, or propose their school).
    public string Message { get; set; }
}

public class ProposeInstitutionRequest
{
    [Required, StringLength(200, MinimumLength = 3)]
    public string Name { get; set; }

    [Required, StringLength(2, MinimumLength = 2)]
    public string CountryCode { get; set; }

    public InstitutionType Type { get; set; } = InstitutionType.University;

    [StringLength(32)]
    public string ShortName { get; set; }

    [StringLength(128)]
    public string City { get; set; }

    [StringLength(128)]
    public string State { get; set; }

    [Url, StringLength(256)]
    public string WebsiteUrl { get; set; }

    // Optional: the school's student email domain. Claiming one that another
    // institution already owns is rejected — that's how accounts get verified.
    [StringLength(128)]
    public string EmailDomain { get; set; }
}
