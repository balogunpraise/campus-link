using System.ComponentModel.DataAnnotations;

namespace CampusLink.Core.Application.Dtos.Chat;

// Opens (or returns) a one-to-one thread. Idempotent: calling it twice gives
// back the same conversation rather than creating a second one.
public class StartDirectConversationRequest
{
    [Required]
    public Guid RecipientUserId { get; set; }
}

public class CreateGroupConversationRequest
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string Title { get; set; }

    // The creator is added automatically as admin.
    public List<Guid> ParticipantUserIds { get; set; } = [];

    // Optional: binds the thread to an existing study group.
    public Guid? StudyGroupId { get; set; }
}

public class SendMessageRequest
{
    [Required, StringLength(4000, MinimumLength = 1)]
    public string Content { get; set; }

    public Guid? ReplyToMessageId { get; set; }

    // Echoed back on the broadcast so the sender can match the saved message to
    // the optimistic bubble it already drew.
    [StringLength(64)]
    public string ClientMessageId { get; set; }
}

public class EditMessageRequest
{
    [Required, StringLength(4000, MinimumLength = 1)]
    public string Content { get; set; }
}

public class AddParticipantsRequest
{
    [Required, MinLength(1)]
    public List<Guid> UserIds { get; set; } = [];
}

public class MarkReadRequest
{
    // Defaults to now when omitted.
    public DateTime? ReadAt { get; set; }
}

public class ConversationSummary
{
    public Guid Id { get; set; }
    public string Type { get; set; }

    // For a direct thread this is the other student's name, resolved per-viewer.
    public string Title { get; set; }

    public Guid? StudyGroupId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
    public DateTime? LastReadAt { get; set; }
    public bool IsMuted { get; set; }
    public string MyRole { get; set; }

    public ChatMessageResponse LastMessage { get; set; }
    public IReadOnlyList<ChatParticipantResponse> Participants { get; set; } = [];
}

public class ChatParticipantResponse
{
    public Guid UserId { get; set; }
    public string UserName { get; set; }
    public string FullName { get; set; }
    public string ProfilePictureUrl { get; set; }
    public string InstitutionName { get; set; }
    public string Role { get; set; }
    public bool HasLeft { get; set; }
}

public class ChatMessageResponse
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid SenderId { get; set; }
    public string SenderUserName { get; set; }
    public string SenderFullName { get; set; }

    // Null when deleted — the row survives for moderation, the text doesn't
    // travel to clients.
    public string Content { get; set; }

    public DateTime SentAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public bool IsDeleted { get; set; }
    public Guid? ReplyToMessageId { get; set; }
    public string ClientMessageId { get; set; }
}

// Newest-first page plus the cursor for the next one.
public class MessagePage
{
    public IReadOnlyList<ChatMessageResponse> Items { get; set; } = [];
    public DateTime? NextCursor { get; set; }
    public bool HasMore { get; set; }
}

// Broadcast when someone reads up to a point in the thread.
public class ReadReceiptEvent
{
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public DateTime ReadAt { get; set; }
}

public class TypingEvent
{
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; }
    public bool IsTyping { get; set; }
}
