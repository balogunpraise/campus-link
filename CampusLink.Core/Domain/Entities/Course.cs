using CampusLink.Core.Domain.Entities.LinkingEntities;

namespace CampusLink.Core.Domain.Entities;

// A course as it exists at one school. Course codes are only meaningful inside
// an institution, so Code is unique per institution rather than globally, and
// cross-school features go through Subject instead.
public class Course : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;

    public Guid InstitutionId { get; set; }
    public Institution Institution { get; set; } = null!;

    // Maps this course onto the shared taxonomy so a student here can be
    // matched with someone taking the equivalent course elsewhere.
    public Guid? SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public ICollection<UserCourse> Enrollments { get; set; } = new List<UserCourse>();
    public ICollection<Resource> Resources { get; set; } = new List<Resource>();
    public ICollection<StudyGroup> StudyGroups { get; set; } = new List<StudyGroup>();
}
