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
