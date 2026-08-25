using CampusLink.Core.Domain.Enums;

namespace CampusLink.Core.Domain.Entities;

// A chat thread. Direct (exactly two students) and Group (any number) are the
// same entity, because messages, membership, read state and delivery all behave
// identically — only the creation rules differ.
public class Conversation : BaseEntity
{
    public ConversationType Type { get; set; } = ConversationType.Direct;

    // Groups only; a direct thread is titled by the other person's name.
    public string Title { get; set; }

    // Deterministic key for a pair: the two user ids sorted and joined. It
    // carries a unique index, which is what stops two students ending up with
    // two parallel direct threads if they both tap "message" at once. Null for
    // group conversations.
    public string DirectKey { get; set; }

    // Set when the thread belongs to a StudyGroup, so a group's chat and its
    // membership stay one concept rather than two.
    public Guid? StudyGroupId { get; set; }
    public StudyGroup StudyGroup { get; set; }

    public Guid CreatedById { get; set; }
    public AppUser CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Denormalized so the conversation list sorts by recent activity without a
    // correlated subquery over every message.
    public DateTime? LastMessageAt { get; set; }

    public ICollection<ConversationParticipant> Participants { get; set; } = new List<ConversationParticipant>();
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();

    public static string BuildDirectKey(Guid userA, Guid userB) =>
        userA.CompareTo(userB) < 0 ? $"{userA}:{userB}" : $"{userB}:{userA}";
}

public class ConversationParticipant
{
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public ParticipantRole Role { get; set; } = ParticipantRole.Member;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    // Everything after this timestamp is unread. One column beats a read
    // receipt row per message per participant, which is what a chat schema's
    // row count dies of first.
    public DateTime? LastReadAt { get; set; }

    public DateTime? MutedUntil { get; set; }

    // Soft-leave: the row stays so old messages still resolve to a name, and so
    // rejoining is possible.
    public DateTime? LeftAt { get; set; }

    public bool IsActive => LeftAt is null;
}

public class ChatMessage : BaseEntity
{
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    public Guid SenderId { get; set; }
    public AppUser Sender { get; set; } = null!;

    public string Content { get; set; } = string.Empty;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }

    // Soft delete: the row survives so replies to it still make sense and so
    // moderation can see what was actually said.
    public DateTime? DeletedAt { get; set; }

    public Guid? ReplyToMessageId { get; set; }
    public ChatMessage ReplyToMessage { get; set; }

    // Client-generated id echoed back on broadcast, so a sender can reconcile
    // the optimistic bubble it already drew instead of rendering it twice.
    public string ClientMessageId { get; set; }

    public bool IsDeleted => DeletedAt.HasValue;
}
