namespace CampusLink.Core.Domain.Entities.LinkingEntities;

// Many-to-many join: which students are enrolled in which cour
public class UserCourse                                
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;         
    public Guid CourseId { get; set; }                         
    public Course Course { get; set; } = null!;                
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
}       