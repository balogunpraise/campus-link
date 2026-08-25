using CampusLink.Core.Domain.Enums;

namespace CampusLink.Core.Domain.Entities;

// A call always happens inside a Conversation, so membership, cross-institution
// permission and history all come from the chat model rather than a second set
// of rules. Media never touches this server — these rows are the lifecycle and
// the audit trail; the audio/video flows peer-to-peer (or via TURN).
public class Call : BaseEntity
{
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    public Guid StartedById { get; set; }
    public AppUser StartedBy { get; set; } = null!;

    public CallType Type { get; set; } = CallType.Audio;
    public CallStatus Status { get; set; } = CallStatus.Ringing;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    // Set when the first callee actually joins — the gap between StartedAt and
    // this is ring time, not talk time.
    public DateTime? ConnectedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    public CallEndReason? EndReason { get; set; }

    public ICollection<CallParticipant> Participants { get; set; } = new List<CallParticipant>();

    // Mesh topology: every peer sends its stream to every other peer, so upload
    // bandwidth grows with participant count. Past a handful it collapses on a
    // phone — that's the ceiling an SFU would lift.
    public const int MaxParticipants = 4;

    public bool IsLive => Status is CallStatus.Ringing or CallStatus.Active;

    public TimeSpan? Duration =>
        ConnectedAt.HasValue && EndedAt.HasValue ? EndedAt - ConnectedAt : null;
}

public class CallParticipant
{
    public Guid CallId { get; set; }
    public Call Call { get; set; } = null!;

    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    // Null until they answer; a row exists from the moment they're rung so the
    // caller can see "ringing" per person.
    public DateTime? JoinedAt { get; set; }
    public DateTime? LeftAt { get; set; }

    public bool IsMuted { get; set; }
    public bool IsCameraOn { get; set; }

    // Screen share is just another track, but the UI needs to know who to make
    // the main tile, so the state is tracked explicitly.
    public bool IsSharingScreen { get; set; }

    public bool Declined { get; set; }

    public bool IsInCall => JoinedAt.HasValue && LeftAt is null;
}
