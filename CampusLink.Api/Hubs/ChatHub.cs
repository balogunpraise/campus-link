using CampusLink.Core.Application.Dtos.Chat;
using CampusLink.Core.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CampusLink.Api.Hubs;

// Real-time transport only. Every rule — membership, permissions, persistence —
// lives in IChatService, so a client using the REST endpoints instead gets
// identical behaviour and identical checks.
[Authorize]
public class ChatHub(IChatService chat, ILogger<ChatHub> logger) : Hub
{
    public static string ConversationGroup(Guid conversationId) => $"conversation:{conversationId}";
    public static string UserGroup(Guid userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User!.GetUserId();

        // Subscribe the connection to every thread the user belongs to, so
        // clients don't have to join each one by hand and can't join one they
        // don't belong to.
        foreach (var conversationId in await chat.GetMyConversationIdsAsync(userId, Context.ConnectionAborted))
            await Groups.AddToGroupAsync(Context.ConnectionId, ConversationGroup(conversationId));

        // Personal channel, for events that aren't tied to one thread (being
        // added to a new group, for instance).
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));

        logger.LogDebug("Chat connection {ConnectionId} opened for user {UserId}", Context.ConnectionId, userId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        // SignalR removes the connection's group memberships automatically.
        if (exception is not null)
            logger.LogDebug(exception, "Chat connection {ConnectionId} dropped", Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    public async Task<ChatMessageResponse> SendMessage(Guid conversationId, SendMessageRequest request)
    {
        var userId = Context.User!.GetUserId();
        var result = await chat.SendMessageAsync(userId, conversationId, request, Context.ConnectionAborted);

        if (!result.Succeeded)
            throw new HubException(string.Join(" ", result.Errors));

        // Broadcast after the write commits: a message that didn't persist must
        // never show up in someone's window.
        await Clients.Group(ConversationGroup(conversationId)).SendAsync("ReceiveMessage", result.Value);

        return result.Value;
    }

    public async Task MarkRead(Guid conversationId, DateTime? readAt)
    {
        var userId = Context.User!.GetUserId();
        var result = await chat.MarkReadAsync(userId, conversationId, readAt, Context.ConnectionAborted);

        if (!result.Succeeded)
            throw new HubException(string.Join(" ", result.Errors));

        await Clients.Group(ConversationGroup(conversationId)).SendAsync("ReadReceipt", result.Value);
    }

    // Ephemeral and deliberately not persisted — typing state is worthless a
    // second later. Sent to others only, never echoed to the typist.
    public async Task Typing(Guid conversationId, bool isTyping)
    {
        var userId = Context.User!.GetUserId();

        if (!await chat.IsParticipantAsync(userId, conversationId, Context.ConnectionAborted))
            throw new HubException("Conversation not found.");

        await Clients.OthersInGroup(ConversationGroup(conversationId)).SendAsync("UserTyping", new TypingEvent
        {
            ConversationId = conversationId,
            UserId = userId,
            UserName = Context.User!.Identity?.Name,
            IsTyping = isTyping
        });
    }

    // Called after the client creates or is added to a conversation, so the
    // open socket starts receiving it without reconnecting.
    public async Task JoinConversation(Guid conversationId)
    {
        var userId = Context.User!.GetUserId();

        if (!await chat.IsParticipantAsync(userId, conversationId, Context.ConnectionAborted))
            throw new HubException("Conversation not found.");

        await Groups.AddToGroupAsync(Context.ConnectionId, ConversationGroup(conversationId));
    }

    public Task LeaveConversation(Guid conversationId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, ConversationGroup(conversationId));
}
