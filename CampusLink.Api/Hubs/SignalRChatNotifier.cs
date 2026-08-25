using CampusLink.Core.Application.Dtos.Chat;
using CampusLink.Core.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace CampusLink.Api.Hubs;

// Lets the REST endpoints push to connected sockets. Without this, a message
// posted over HTTP would only reach people who happened to refresh — the two
// doors have to feed the same real-time stream.
public class SignalRChatNotifier(IHubContext<ChatHub> hub) : IChatNotifier
{
    public Task MessageSentAsync(Guid conversationId, ChatMessageResponse message) =>
        hub.Clients.Group(ChatHub.ConversationGroup(conversationId)).SendAsync("ReceiveMessage", message);

    public Task MessageUpdatedAsync(Guid conversationId, ChatMessageResponse message) =>
        hub.Clients.Group(ChatHub.ConversationGroup(conversationId)).SendAsync("MessageUpdated", message);

    // Membership changed: tell each affected user on their personal channel, so
    // their client can join the new thread's group on the live connection.
    public Task ConversationChangedAsync(Guid conversationId, IReadOnlyList<Guid> participantUserIds)
    {
        var groups = participantUserIds.Select(id => ChatHub.UserGroup(id)).ToList();

        return hub.Clients.Groups(groups).SendAsync("ConversationChanged", new { conversationId });
    }

    public Task ReadReceiptAsync(Guid conversationId, ReadReceiptEvent receipt) =>
        hub.Clients.Group(ChatHub.ConversationGroup(conversationId)).SendAsync("ReadReceipt", receipt);
}
