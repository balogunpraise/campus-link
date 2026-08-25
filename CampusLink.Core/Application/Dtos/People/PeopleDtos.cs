using CampusLink.Core.Application.Dtos.Common;
using CampusLink.Core.Application.Dtos.StudyBuddies;

namespace CampusLink.Core.Application.Dtos.People;

// Directory search. Distinct from study-buddy candidates: that finds people who
// share your interests, this finds a person you're looking for by name.
public class PeopleSearchRequest : PageRequest
{
    // Name, username, or a fragment of either. Typo-tolerant.
    public string Search { get; set; }

    // Defaults to the caller's own school when nothing else is specified — the
    // common case is finding a classmate.
    public Guid? InstitutionId { get; set; }

    public bool SameInstitutionOnly { get; set; }

    public string Department { get; set; }
    public int? YearOfStudy { get; set; }

    // Narrow to people who list this subject as a study interest.
    public Guid? SubjectId { get; set; }

    // Only people who have opted into study-buddy matching.
    public bool StudyBuddiesOnly { get; set; }
}

public class PersonSummary
{
    public Guid UserId { get; set; }
    public string UserName { get; set; }
    public string FullName { get; set; }
    public string Bio { get; set; }
    public string ProfilePictureUrl { get; set; }

    public Guid InstitutionId { get; set; }
    public string InstitutionName { get; set; }
    public string CountryCode { get; set; }
    public bool IsSameInstitution { get; set; }

    public string Department { get; set; }
    public int YearOfStudy { get; set; }

    public bool HasStudyBuddyProfile { get; set; }
    public bool IsOpenToStudyBuddies { get; set; }

    // Interests the two of you have in common. Empty when the caller has no
    // study-buddy profile of their own.
    public IReadOnlyList<SubjectSummary> SharedSubjects { get; set; } = [];

    // Their listed interests, so a searcher without a profile still sees what
    // this person studies.
    public IReadOnlyList<SubjectSummary> Subjects { get; set; } = [];

    // Set when a study-buddy request already exists in either direction, so the
    // client shows "Pending" instead of a button that will 409.
    public string ExistingMatchStatus { get; set; }

    // Whether a request may be sent right now — lets the UI disable the button
    // rather than discover the rule from an error response.
    public bool CanSendRequest { get; set; }
    public string CannotSendReason { get; set; }
}
