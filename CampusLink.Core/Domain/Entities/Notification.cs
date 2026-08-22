using CampusLink.Core.Domain.Enums;

namespace CampusLink.Core.Domain.Entities;

public class Notification : BaseEntity
{

    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public NotificationType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Loosely-coupled pointer to whatever triggered the notification
    // (a Resource, ResourceShareRequest, StudyBuddyMatch, etc.) so this
    // table doesn't need a nullable FK column per entity type.
    public Guid? RelatedEntityId { get; set; }
    public string? RelatedEntityType { get; set; }
}