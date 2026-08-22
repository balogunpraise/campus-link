using CampusLink.Core.Domain.Enums;

namespace CampusLink.Core.Domain.Entities.LinkingEntities;

// Many-to-many join between StudyGroup and AppUser, with a role
// so group creators/admins can manage membership.
public class StudyGroupMember
{
    public Guid StudyGroupId { get; set; }
    public StudyGroup StudyGroup { get; set; } = null!;

    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    // True when the member's institution differs from the group's host, set on
    // join. Lets a group cap or display its outside members.
    public bool IsExternalMember { get; set; }

    public GroupMemberRole Role { get; set; } = GroupMemberRole.Member;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
