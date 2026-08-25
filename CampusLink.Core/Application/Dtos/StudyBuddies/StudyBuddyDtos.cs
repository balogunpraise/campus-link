using System.ComponentModel.DataAnnotations;
using CampusLink.Core.Application.Dtos.Common;
using CampusLink.Core.Domain.Enums;

namespace CampusLink.Core.Application.Dtos.StudyBuddies;

// Create or replace the caller's own study buddy profile. Subjects are the
// "interests" the whole feature matches on — and they're shared across schools,
// which is what lets a student here pair with one at another university.
public class UpsertStudyBuddyProfileRequest
{
    [StringLength(500)]
    public string LearningStyle { get; set; }

    [StringLength(500)]
    public string AvailabilityNotes { get; set; }

    public bool IsActive { get; set; } = true;

    public VisibilityScope MatchScope { get; set; } = VisibilityScope.Institution;

    public MeetingMode PreferredMeetingMode { get; set; } = MeetingMode.InPerson;

    [Range(0, 24)]
    public int? MaxTimeZoneOffsetHours { get; set; }

    // Replaces the existing set wholesale — simpler for clients than diffing,
    // and these lists are small.
    [Required, MinLength(1)]
    public List<Guid> SubjectIds { get; set; } = [];

    // ISO 639-1 codes ("en", "fr", "yo"). Only used to rank cross-school
    // candidates.
    public List<string> LanguageCodes { get; set; } = [];
}

public class StudyBuddyProfileResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; }
    public string FullName { get; set; }
    public string LearningStyle { get; set; }
    public string AvailabilityNotes { get; set; }
    public bool IsActive { get; set; }
    public string MatchScope { get; set; }
    public string PreferredMeetingMode { get; set; }
    public int? MaxTimeZoneOffsetHours { get; set; }
    public Guid InstitutionId { get; set; }
    public string InstitutionName { get; set; }
    public IReadOnlyList<SubjectSummary> Subjects { get; set; } = [];
    public IReadOnlyList<string> LanguageCodes { get; set; } = [];
}

public class SubjectSummary
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Slug { get; set; }
    public string Field { get; set; }
}

public class StudyBuddyCandidateRequest : PageRequest
{
    // Defaults to every subject on the caller's own profile — "find me someone
    // who studies what I study". Narrow it to focus on one subject.
    public List<Guid> SubjectIds { get; set; } = [];

    public MeetingMode? MeetingMode { get; set; }

    // Exclude students at other institutions even if the caller's scope allows
    // them (useful when someone specifically wants to meet in person).
    public bool SameInstitutionOnly { get; set; }
}

public class StudyBuddyCandidate
{
    public Guid UserId { get; set; }
    public string UserName { get; set; }
    public string FullName { get; set; }
    public string Bio { get; set; }
    public string ProfilePictureUrl { get; set; }

    public Guid InstitutionId { get; set; }
    public string InstitutionName { get; set; }
    public bool IsSameInstitution { get; set; }

    public string LearningStyle { get; set; }
    public string AvailabilityNotes { get; set; }
    public string PreferredMeetingMode { get; set; }

    // The interests the two students actually have in common — the reason this
    // candidate is in the list, and what a request has to be about.
    public IReadOnlyList<SubjectSummary> SharedSubjects { get; set; } = [];
    public int SharedSubjectCount { get; set; }

    public IReadOnlyList<string> LanguageCodes { get; set; } = [];

    // Set when a request already exists in either direction, so the client can
    // show "Pending" instead of a button that will 409.
    public string ExistingMatchStatus { get; set; }
}

public class SendStudyBuddyRequestRequest
{
    [Required]
    public Guid RecipientUserId { get; set; }

    // Optional. When given it must be an interest both students list — that's
    // what the interest-matching flow sends, and it's what makes the request
    // about something specific. Omitted for a directed request to someone found
    // by name in the people directory, where a shared interest may not exist
    // yet (a classmate, or someone who hasn't filled in a profile).
    public Guid? SubjectId { get; set; }

    // Optional, and only valid when both students are at the same institution —
    // a course belongs to one school.
    public Guid? CourseId { get; set; }

    public MeetingMode? MeetingMode { get; set; }

    [StringLength(500)]
    public string Message { get; set; }
}

public class RespondToStudyBuddyRequest
{
    [Required]
    public bool Accept { get; set; }
}

public class StudyBuddyMatchResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; }
    public string MeetingMode { get; set; }
    public bool IsCrossInstitution { get; set; }

    public SubjectSummary Subject { get; set; }
    public string Message { get; set; }

    // The other student, whichever side the caller is on.
    public Guid CounterpartUserId { get; set; }
    public string CounterpartUserName { get; set; }
    public string CounterpartFullName { get; set; }
    public string CounterpartInstitutionName { get; set; }

    // True when the caller sent it — the client renders outgoing and incoming
    // requests differently.
    public bool IsOutgoing { get; set; }

    public DateTime RequestedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}
