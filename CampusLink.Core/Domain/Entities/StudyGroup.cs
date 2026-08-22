using CampusLink.Core.Domain.Entities.LinkingEntities;
using CampusLink.Core.Domain.Enums;

namespace CampusLink.Core.Domain.Entities;

public class StudyGroup : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // The institution that "hosts" the group (the creator's). A group can still
    // admit outsiders — that's what Visibility controls.
    public Guid HostInstitutionId { get; set; }
    public Institution HostInstitution { get; set; } = null!;

    // Only set for a group tied to one school's course.
    public Guid? CourseId { get; set; }
    public Course? Course { get; set; }

    // Set instead of (or alongside) CourseId for groups meant to draw members
    // from several schools.
    public Guid? SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public VisibilityScope Visibility { get; set; } = VisibilityScope.Institution;
    public MeetingMode MeetingMode { get; set; } = MeetingMode.InPerson;

    // Where the group meets: a room for in-person groups, a call link for
    // online ones.
    public string? MeetingLocation { get; set; }
    public string? MeetingLink { get; set; }

    // Scheduling across schools needs a reference zone.
    public string? TimeZoneId { get; set; }

    public Guid CreatedById { get; set; }
    public AppUser CreatedBy { get; set; } = null!;

    public int MaxMembers { get; set; } = 10;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<StudyGroupMember> Members { get; set; } = new List<StudyGroupMember>();
}
