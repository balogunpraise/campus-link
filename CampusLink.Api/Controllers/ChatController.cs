using CampusLink.Core.Application.Common;
using CampusLink.Core.Application.Dtos.Chat;
using CampusLink.Core.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusLink.Api.Controllers;

// The HTTP half of chat: creating threads, loading history, and a send fallback
// for clients that can't hold a socket open. Writes here are pushed to
// connected sockets through IChatNotifier, so both paths stay in sync.
[ApiController]
[Route("api/chat")]
[Authorize]
public class ChatController(IChatService chat, IChatNotifier notifier) : ControllerBase
{
    [HttpPost("conversations/direct")]
    [ProducesResponseType(typeof(ConversationSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StartDirect(
        StartDirectConversationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await chat.StartDirectAsync(User.GetUserId(), request.RecipientUserId, cancellationToken);

        if (result.Succeeded)
            await notifier.ConversationChangedAsync(
                result.Value.Id, result.Value.Participants.Select(p => p.UserId).ToList());

        return ToActionResult(result);
    }

    [HttpPost("conversations/group")]
    [ProducesResponseType(typeof(ConversationSummary), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateGroup(
        CreateGroupConversationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await chat.CreateGroupAsync(User.GetUserId(), request, cancellationToken);

        if (!result.Succeeded)
            return ToProblem(result);

        await notifier.ConversationChangedAsync(
            result.Value.Id, result.Value.Participants.Select(p => p.UserId).ToList());

        return CreatedAtAction(nameof(GetConversation), new { id = result.Value.Id }, result.Value);
    }

    [HttpGet("conversations")]
    [ProducesResponseType(typeof(IReadOnlyList<ConversationSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConversations(CancellationToken cancellationToken) =>
        ToActionResult(await chat.GetMyConversationsAsync(User.GetUserId(), cancellationToken));

    [HttpGet("conversations/{id:guid}")]
    [ProducesResponseType(typeof(ConversationSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConversation(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await chat.GetConversationAsync(User.GetUserId(), id, cancellationToken));

    // Newest-first page. Pass the previous page's nextCursor as `before` to
    // scroll further back.
    [HttpGet("conversations/{id:guid}/messages")]
    [ProducesResponseType(typeof(MessagePage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMessages(
        Guid id,
        [FromQuery] DateTime? before,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await chat.GetMessagesAsync(User.GetUserId(), id, before, limit, cancellationToken));

    [HttpPost("conversations/{id:guid}/messages")]
    [ProducesResponseType(typeof(ChatMessageResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendMessage(
        Guid id,
        SendMessageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await chat.SendMessageAsync(User.GetUserId(), id, request, cancellationToken);

        if (!result.Succeeded)
            return ToProblem(result);

        await notifier.MessageSentAsync(id, result.Value);

        return CreatedAtAction(nameof(GetMessages), new { id }, result.Value);
    }

    [HttpPut("messages/{messageId:guid}")]
    [ProducesResponseType(typeof(ChatMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EditMessage(
        Guid messageId,
        EditMessageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await chat.EditMessageAsync(User.GetUserId(), messageId, request.Content, cancellationToken);

        if (result.Succeeded)
            await notifier.MessageUpdatedAsync(result.Value.ConversationId, result.Value);

        return ToActionResult(result);
    }

    [HttpDelete("messages/{messageId:guid}")]
    [ProducesResponseType(typeof(ChatMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMessage(Guid messageId, CancellationToken cancellationToken)
    {
        var result = await chat.DeleteMessageAsync(User.GetUserId(), messageId, cancellationToken);

        if (result.Succeeded)
            await notifier.MessageUpdatedAsync(result.Value.ConversationId, result.Value);

        return ToActionResult(result);
    }

    [HttpPost("conversations/{id:guid}/read")]
    [ProducesResponseType(typeof(ReadReceiptEvent), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(
        Guid id,
        MarkReadRequest request,
        CancellationToken cancellationToken)
    {
        var result = await chat.MarkReadAsync(User.GetUserId(), id, request?.ReadAt, cancellationToken);

        if (result.Succeeded)
            await notifier.ReadReceiptAsync(id, result.Value);

        return ToActionResult(result);
    }

    [HttpPost("conversations/{id:guid}/participants")]
    [ProducesResponseType(typeof(ConversationSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddParticipants(
        Guid id,
        AddParticipantsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await chat.AddParticipantsAsync(User.GetUserId(), id, request.UserIds, cancellationToken);

        if (result.Succeeded)
            await notifier.ConversationChangedAsync(
                id, result.Value.Participants.Select(p => p.UserId).ToList());

        return ToActionResult(result);
    }

    // Removing yourself is leaving the conversation.
    [HttpDelete("conversations/{id:guid}/participants/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveParticipant(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await chat.RemoveParticipantAsync(User.GetUserId(), id, userId, cancellationToken);

        if (!result.Succeeded)
            return ToProblem(result);

        await notifier.ConversationChangedAsync(id, [userId]);

        return NoContent();
    }

    private IActionResult ToActionResult<T>(Result<T> result) =>
        result.Succeeded ? Ok(result.Value) : ToProblem(result);

    private IActionResult ToProblem<T>(Result<T> result)
    {
        var status = result.ErrorCode switch
        {
            AuthErrorCode.NotFound => StatusCodes.Status404NotFound,
            AuthErrorCode.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        return Problem(
            title: result.ErrorCode.ToString(),
            detail: string.Join(" ", result.Errors),
            statusCode: status);
    }
}
