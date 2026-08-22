using CampusLink.Core.Domain.Enums;

namespace CampusLink.Core.Domain.Entities;

public class Resource : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ResourceType Type { get; set; }

    // Only set for digital resources (blob storage / CDN URL).
    public string FileUrl { get; set; }

    // Only meaningful for physical resources, so it has to be nullable — a PDF
    // has no condition. This is Nullable<ResourceCondition> on a value type, not
    // a nullable reference annotation, so it stands regardless of the project's
    // <Nullable>disable</Nullable> setting.
    public ResourceCondition? Condition { get; set; }

    public Guid OwnerId { get; set; }
    public AppUser Owner { get; set; } = null!;

    // Denormalized from Owner so browse queries can filter by school without
    // joining AppUser. Set on create; it doesn't follow the owner if they move.
    public Guid InstitutionId { get; set; }
    public Institution Institution { get; set; } = null!;

    // The owner's own course (institution-specific).
    public Guid? CourseId { get; set; }
    public Course? Course { get; set; }

    // Shared taxonomy entry, usually copied from Course.SubjectId. This is what
    // students at other schools search on.
    public Guid? SubjectId { get; set; }
    public Subject Subject { get; set; }

    // How widely this listing is discoverable. Physical items default to the
    // owner's campus because they have to change hands in person.
    public VisibilityScope Visibility { get; set; } = VisibilityScope.Institution;

    // Where a physical hand-off happens; ignored for digital resources.
    public string? PickupLocation { get; set; }

    public bool IsAvailable { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<ResourceShareRequest> ShareRequests { get; set; } = new List<ResourceShareRequest>();

    public bool IsPhysical =>
        Type is ResourceType.PhysicalBook or ResourceType.PhysicalNotes or ResourceType.PhysicalEquipment;

    // Physical items can't sensibly be lent to another campus.
    public bool CanBeSharedAcrossInstitutions => !IsPhysical && Visibility != VisibilityScope.Institution;
}

// Tracks the full lifecycle of one student borrowing/downloading another
// student's resource: request -> approve/reject -> (for physical items)
// return. This is deliberately its own entity rather than a status flag on
// Resource, because a resource can be requested by many different students
// over time and each request needs its own timeline.
public class ResourceShareRequest : BaseEntity
{
    public Guid ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;

    public Guid RequesterId { get; set; }
    public AppUser Requester { get; set; } = null!;

    // True when requester and owner are at different schools — set on create so
    // reporting and moderation can single these out cheaply.
    public bool IsCrossInstitution { get; set; }

    public ShareRequestStatus Status { get; set; } = ShareRequestStatus.Pending;

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }

    // ----- Physical loans only -----
    //
    // A handover happens in the world, not on the server, so neither side alone
    // can be trusted to record it. Both parties confirm each transition: the
    // loan only starts when owner and borrower agree it was handed over, and
    // only ends when both agree it came back. One side confirming leaves the
    // request in a half-confirmed state, which is exactly the signal that
    // something went wrong.
    public DateTime? OwnerConfirmedHandoverAt { get; set; }
    public DateTime? BorrowerConfirmedReceiptAt { get; set; }

    public DateTime? BorrowerConfirmedReturnAt { get; set; }
    public DateTime? OwnerConfirmedReturnAt { get; set; }

    public DateTime? DueDate { get; set; }

    // Canonical "this loan is closed" timestamp — set only when both sides have
    // confirmed the return, so reporting never has to re-derive the handshake.
    public DateTime? ReturnedAt { get; set; }

    // Raised when the two sides disagree, or when one confirms and the other
    // never does. Nothing is auto-resolved: this is the queue a human works.
    public DateTime? DisputedAt { get; set; }
    public string? DisputeReason { get; set; }

    public string? Notes { get; set; }

    public bool HandoverConfirmed =>
        OwnerConfirmedHandoverAt.HasValue && BorrowerConfirmedReceiptAt.HasValue;

    public bool ReturnConfirmed =>
        BorrowerConfirmedReturnAt.HasValue && OwnerConfirmedReturnAt.HasValue;

    // Exactly one side has confirmed. Benign for a few hours (people confirm at
    // different times); past that it's worth nudging, then flagging.
    public bool HandoverAwaitingCounterparty =>
        !HandoverConfirmed && (OwnerConfirmedHandoverAt.HasValue || BorrowerConfirmedReceiptAt.HasValue);

    public bool ReturnAwaitingCounterparty =>
        !ReturnConfirmed && (BorrowerConfirmedReturnAt.HasValue || OwnerConfirmedReturnAt.HasValue);

    public bool IsOverdue =>
        DueDate.HasValue && !ReturnConfirmed && DateTime.UtcNow > DueDate.Value;

    // ----- transitions -----
    //
    // These live on the entity so the two-party rule can't be bypassed by a
    // service that forgets half of it.

    // Returns true when this confirmation completed the handover.
    public bool ConfirmHandover(bool byOwner, DateTime? at = null)
    {
        var now = at ?? DateTime.UtcNow;

        if (Status is not (ShareRequestStatus.Approved or ShareRequestStatus.AwaitingHandover))
            throw new InvalidOperationException(
                $"Handover cannot be confirmed while the request is {Status}.");

        if (byOwner)
            OwnerConfirmedHandoverAt ??= now;
        else
            BorrowerConfirmedReceiptAt ??= now;

        Status = HandoverConfirmed
            ? ShareRequestStatus.OnLoan
            : ShareRequestStatus.AwaitingHandover;

        return HandoverConfirmed;
    }

    // Returns true when this confirmation closed the loan.
    public bool ConfirmReturn(bool byOwner, DateTime? at = null)
    {
        var now = at ?? DateTime.UtcNow;

        // The item can't come back before it went out. Guarding here means a
        // borrower can't quietly close a loan that never actually started.
        if (!HandoverConfirmed)
            throw new InvalidOperationException(
                "Return cannot be confirmed before both parties have confirmed the handover.");

        if (byOwner)
            OwnerConfirmedReturnAt ??= now;
        else
            BorrowerConfirmedReturnAt ??= now;

        if (!ReturnConfirmed)
        {
            Status = ShareRequestStatus.AwaitingReturn;
            return false;
        }

        ReturnedAt = now;
        Status = ShareRequestStatus.Completed;

        return true;
    }

    public void RaiseDispute(string reason, DateTime? at = null)
    {
        DisputedAt ??= at ?? DateTime.UtcNow;
        DisputeReason = reason;
        Status = ShareRequestStatus.Disputed;
    }
}
