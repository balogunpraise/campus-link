namespace CampusLink.Core.Domain.Entities;

// A rating left by one student about another, tied back to whichever
// interaction prompted it (a completed resource share or a study buddy
// match). Exactly one of the two FKs should be set — enforced with a
// CHECK constraint in the DbContext configuration.
public class Review :  BaseEntity
{

    public Guid ReviewerId { get; set; }
    public AppUser Reviewer { get; set; } = null!;

    public Guid RevieweeId { get; set; }
    public AppUser Reviewee { get; set; } = null!;

    public int Rating { get; set; } // 1-5
    public string Comment { get; set; }

    public Guid? RelatedShareRequestId { get; set; }
    public ResourceShareRequest? RelatedShareRequest { get; set; }

    public Guid? RelatedMatchId { get; set; }
    public StudyBuddyMatch? RelatedMatch { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}