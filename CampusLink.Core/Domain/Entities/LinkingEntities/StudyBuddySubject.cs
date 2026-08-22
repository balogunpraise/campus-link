namespace CampusLink.Core.Domain.Entities.LinkingEntities;

// Many-to-many: a study buddy profile lists the subjects it wants a buddy for.
// Deliberately keyed on Subject rather than Course so two students at different
// schools studying the same topic still match.
public class StudyBuddySubject
{
    public Guid StudyBuddyProfileId { get; set; }
    public StudyBuddyProfile StudyBuddyProfile { get; set; } = null!;

    public Guid SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;

    // Optionally pins the local course this interest came from, so the UI can
    // show "CSC201 - Data Structures" to same-school candidates.
    public Guid? CourseId { get; set; }
    public Course? Course { get; set; }
}

// A language a student is comfortable studying in. Only matters once matching
// crosses institutions (and countries).
public class StudyBuddyLanguage
{
    public Guid StudyBuddyProfileId { get; set; }
    public StudyBuddyProfile StudyBuddyProfile { get; set; } = null!;

    // ISO 639-1 code, lowercase, e.g. "en".
    public string LanguageCode { get; set; } = string.Empty;
}
