namespace CampusLink.Core.Domain.Enums;

public enum ResourceType
{
    PhysicalBook,
    PhysicalNotes,
    PhysicalEquipment,
    DigitalPdf,
    DigitalSlides,
    DigitalVideo,
    DigitalOther
}

public enum ResourceCondition
{
    New,
    Good,
    Fair,
    Worn
}

// Lifecycle of one share request. Digital resources jump Pending -> Approved ->
// Completed (an access grant has nothing to hand over). Physical loans walk the
// full path, and the two AwaitingX states are the half-confirmed ones: one party
// has confirmed a handover or return and the other hasn't yet.
// Values are pinned: these persist as ints, so the original members keep their
// numbers and new states are appended rather than shifting stored rows.
public enum ShareRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Completed = 3,
    Cancelled = 4,
    Overdue = 5,

    // Physical only: approved, but owner and borrower haven't both confirmed
    // the item actually changed hands.
    AwaitingHandover = 6,

    // Both confirmed the handover; the borrower has the item.
    OnLoan = 7,

    // One party has confirmed the return, the other hasn't.
    AwaitingReturn = 8,

    // The two sides disagree, or one confirmed and the other never did. Needs a
    // human — nothing here resolves automatically.
    Disputed = 9
}

public enum MatchStatus
{
    Pending,
    Accepted,
    Declined,
    Ended
}

public enum GroupMemberRole
{
    Admin,
    Member
}

public enum NotificationType
{
    ShareRequestReceived,
    ShareRequestUpdated,
    MatchRequestReceived,
    MatchRequestUpdated,
    GroupInvite,
    NewMessage,
    ReviewReceived
}

public enum InstitutionType
{
    University,
    Polytechnic,
    College,
    HighSchool,
    Bootcamp,
    Other
}

// How far beyond the owner's own school something is discoverable.
public enum VisibilityScope
{
    // Only members of the owner's institution.
    Institution = 0,

    // Members of the owner's institution plus any institution it is
    // explicitly partnered with (see InstitutionPartnership).
    PartnerInstitutions = 1,

    // Every verified student on CampusLink, regardless of school.
    Global = 2
}

// Physical items can only realistically change hands in person, so groups and
// listings declare how they operate; cross-institution discovery filters on it.
public enum MeetingMode
{
    InPerson,
    Online,
    Hybrid
}

public enum InstitutionMembershipStatus
{
    // Signed up but email domain didn't match — limited to their own school
    // until verified.
    Unverified,

    // Email domain matched an InstitutionEmailDomain, or an admin approved.
    Verified,

    // Manually revoked (e.g. graduated, fraudulent claim).
    Revoked
}

public enum PartnershipStatus
{
    Pending,
    Active,
    Ended
}

public enum ConversationType
{
    // Exactly two students, created on demand and deduplicated by DirectKey.
    Direct = 0,

    // Any number of students; may be attached to a StudyGroup.
    Group = 1
}

public enum ParticipantRole
{
    Member = 0,

    // Can rename the group, add and remove participants.
    Admin = 1
}

public enum CallType
{
    Audio = 0,
    Video = 1,

    // Started as a screen share rather than escalating into one. Screen share
    // is a track, not a separate connection, so this is only the initial intent.
    ScreenShare = 2
}

public enum CallStatus
{
    // Invitations sent, nobody has answered yet.
    Ringing = 0,

    // At least two people connected.
    Active = 1,

    Ended = 2
}

public enum CallEndReason
{
    // Everyone left, or the last remaining participant hung up.
    Completed = 0,

    // Nobody answered before the ring timeout.
    NoAnswer = 1,

    // Every callee declined.
    Declined = 2,

    // Caller gave up before anyone answered.
    Cancelled = 3,

    // Connection lost and never recovered.
    Failed = 4
}
