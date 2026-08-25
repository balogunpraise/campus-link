using CampusLink.Core.Application.Common;
using CampusLink.Core.Application.Dtos.Calls;
using CampusLink.Core.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusLink.Api.Controllers;

// HTTP side of calling: ICE configuration, discovering an in-progress call on
// page load, history, and lifecycle actions for clients that aren't holding a
// hub connection. Signaling itself is hub-only — it's far too chatty for HTTP.
[ApiController]
[Route("api/calls")]
[Authorize]
public class CallsController(ICallService calls, ICallNotifier notifier, IIceServerProvider ice) : ControllerBase
{
    // Fetched right before a call and re-fetched when it expires. Credentials
    // are per-user and short-lived, so this can't be cached across users.
    [HttpGet("ice-servers")]
    [ProducesResponseType(typeof(IceServersResponse), StatusCodes.Status200OK)]
    public IActionResult IceServers() => Ok(ice.GetIceServers(User.GetUserId()));

    [HttpPost]
    [ProducesResponseType(typeof(CallResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Start(StartCallRequest request, CancellationToken cancellationToken)
    {
        var result = await calls.StartAsync(User.GetUserId(), request, cancellationToken);

        if (!result.Succeeded)
            return ToProblem(result);

        var callees = result.Value.Participants
            .Where(p => p.UserId != User.GetUserId())
            .Select(p => p.UserId)
            .ToList();

        await notifier.IncomingCallAsync(callees, result.Value);

        return CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CallResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await calls.GetAsync(User.GetUserId(), id, cancellationToken));

    // What a client calls on load to find a call already in progress that it
    // can still join.
    [HttpGet("conversations/{conversationId:guid}/active")]
    [ProducesResponseType(typeof(CallResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Active(Guid conversationId, CancellationToken cancellationToken) =>
        ToActionResult(await calls.GetActiveForConversationAsync(
            User.GetUserId(), conversationId, cancellationToken));

    [HttpGet("conversations/{conversationId:guid}/history")]
    [ProducesResponseType(typeof(IReadOnlyList<CallResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> History(
        Guid conversationId,
        [FromQuery] int limit = 25,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await calls.GetHistoryAsync(User.GetUserId(), conversationId, limit, cancellationToken));

    [HttpPost("{id:guid}/join")]
    [ProducesResponseType(typeof(CallResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Join(Guid id, CancellationToken cancellationToken) =>
        await Act(calls.JoinAsync(User.GetUserId(), id, cancellationToken));

    [HttpPost("{id:guid}/decline")]
    [ProducesResponseType(typeof(CallResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Decline(Guid id, CancellationToken cancellationToken) =>
        await Act(calls.DeclineAsync(User.GetUserId(), id, cancellationToken));

    [HttpPost("{id:guid}/leave")]
    [ProducesResponseType(typeof(CallResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Leave(Guid id, CancellationToken cancellationToken) =>
        await Act(calls.LeaveAsync(User.GetUserId(), id, cancellationToken));

    [HttpPatch("{id:guid}/media-state")]
    [ProducesResponseType(typeof(CallResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> MediaState(
        Guid id,
        UpdateMediaStateRequest request,
        CancellationToken cancellationToken) =>
        await Act(calls.UpdateMediaStateAsync(User.GetUserId(), id, request, cancellationToken));

    // Every lifecycle action has to reach the other participants' sockets,
    // otherwise a REST-driven hangup leaves the other side staring at a live
    // call that isn't.
    private async Task<IActionResult> Act(Task<Result<CallResponse>> action)
    {
        var result = await action;

        if (result.Succeeded)
            await notifier.CallUpdatedAsync(
                result.Value.Id, result.Value.Participants.Select(p => p.UserId).ToList(), result.Value);

        return ToActionResult(result);
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
