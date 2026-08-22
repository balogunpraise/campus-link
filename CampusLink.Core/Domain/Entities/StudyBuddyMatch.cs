using CampusLink.Core.Domain.Enums;

namespace CampusLink.Core.Domain.Entities;

// A pairing between two students, who may be at different schools. Modeled as
// a directed request (Requester -> Recipient) so the workflow (pending ->
// accepted/declined) mirrors ResourceShareRequest, then Status moves to Ended
// when the pairing concludes.
public class StudyBuddyMatch : BaseEntity
{
    public Guid RequesterId { get; set; }
    public AppUser Requester { get; set; } = null!;

    public Guid RecipientId { get; set; }
    public AppUser Recipient { get; set; } = null!;

    // What they're pairing up on. A subject rather than a course, because the
    // two students may not share an institution — and therefore can't share a
    // course row.
    public Guid? SubjectId { get; set; }
    public Subject? Subject { get; set; }

    // Only set when both students are at the same institution and taking the
    // same course.
    public Guid? CourseId { get; set; }
    public Course? Course { get; set; }

    // Set on create from the two users' institutions; lets the app default to
    // online meetings and skip in-person prompts.
    public bool IsCrossInstitution { get; set; }
    public MeetingMode MeetingMode { get; set; } = MeetingMode.InPerson;

    public MatchStatus Status { get; set; } = MatchStatus.Pending;

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}
