using CampusLink.Core.Domain.Entities.LinkingEntities;
using CampusLink.Core.Domain.Enums;

namespace CampusLink.Core.Domain.Entities;

// One-to-one extension of AppUser with study-buddy-specific
// preferences, kept separate so the core user table stays lean and this
// stays optional (not every user opts into study buddy matching).
public class StudyBuddyProfile : BaseEntity
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public string LearningStyle { get; set; } = string.Empty;
    public string AvailabilityNotes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // How far the matcher may look for this student. Institution-only keeps the
    // old single-school behaviour; Global opens them to every verified student.
    public VisibilityScope MatchScope { get; set; } = VisibilityScope.Institution;

    // A student open to other schools usually can't meet in person, so they
    // declare how they're willing to work together.
    public MeetingMode PreferredMeetingMode { get; set; } = MeetingMode.InPerson;

    // Free-form language tags ("en", "fr", "yo") used to rank cross-school
    // candidates; matching on the same campus rarely needs it.
    public ICollection<StudyBuddyLanguage> Languages { get; set; } = new List<StudyBuddyLanguage>();

    // Widest acceptable time-zone offset difference (hours) for a remote buddy.
    // Null means no limit.
    public int? MaxTimeZoneOffsetHours { get; set; }

    // Subjects (shared across schools), not courses — this is what makes
    // matching a student here with one at another university possible.
    public ICollection<StudyBuddySubject> PreferredSubjects { get; set; } = new List<StudyBuddySubject>();
}
