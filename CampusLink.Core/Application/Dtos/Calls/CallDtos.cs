using System.ComponentModel.DataAnnotations;
using CampusLink.Core.Domain.Enums;

namespace CampusLink.Core.Application.Dtos.Calls;

public class StartCallRequest
{
    [Required]
    public Guid ConversationId { get; set; }

    public CallType Type { get; set; } = CallType.Audio;
}

public class CallResponse
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid StartedById { get; set; }
    public string StartedByName { get; set; }
    public string Type { get; set; }
    public string Status { get; set; }
    public string EndReason { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? ConnectedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public double? DurationSeconds { get; set; }

    public IReadOnlyList<CallParticipantResponse> Participants { get; set; } = [];
}

public class CallParticipantResponse
{
    public Guid UserId { get; set; }
    public string UserName { get; set; }
    public string FullName { get; set; }
    public DateTime? JoinedAt { get; set; }
    public DateTime? LeftAt { get; set; }
    public bool IsInCall { get; set; }
    public bool IsMuted { get; set; }
    public bool IsCameraOn { get; set; }
    public bool IsSharingScreen { get; set; }
    public bool Declined { get; set; }
}

public class UpdateMediaStateRequest
{
    public bool? IsMuted { get; set; }
    public bool? IsCameraOn { get; set; }
    public bool? IsSharingScreen { get; set; }
}

// One envelope for every WebRTC handshake message. The server deliberately
// doesn't parse SDP or candidates — it checks that both parties are in the call
// and forwards the blob. Keeping the relay dumb means browser-side WebRTC
// changes never require a server change.
public class CallSignal
{
    [Required]
    public Guid CallId { get; set; }

    // Who this is for. Signaling in a mesh is point-to-point, not broadcast:
    // each pair negotiates its own peer connection.
    [Required]
    public Guid TargetUserId { get; set; }

    // "offer", "answer", "candidate", or "renegotiate" — meaningful to the
    // browser, opaque here.
    [Required, StringLength(32)]
    public string Kind { get; set; }

    // SDP for offer/answer, the ICE candidate payload for "candidate".
    [Required]
    public string Payload { get; set; }

    // Filled in by the server on relay so the receiver knows who it came from
    // without trusting the sender's own claim.
    public Guid FromUserId { get; set; }
}

// STUN/TURN configuration handed to the browser's RTCPeerConnection. TURN
// credentials are short-lived and derived per user — the shared secret never
// leaves the server.
public class IceServersResponse
{
    public IReadOnlyList<IceServer> IceServers { get; set; } = [];
    public DateTime ExpiresAt { get; set; }
}

public class IceServer
{
    public IReadOnlyList<string> Urls { get; set; } = [];
    public string Username { get; set; }
    public string Credential { get; set; }
}
