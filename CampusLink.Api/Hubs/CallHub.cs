using CampusLink.Core.Application.Dtos.Calls;
using CampusLink.Core.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CampusLink.Api.Hubs;

// WebRTC signaling. The server never sees media — it relays the handshake
// (offer, answer, ICE candidates) between two participants of the same live
// call, and broadcasts lifecycle changes. All permission logic is in
// ICallService, same as chat.
[Authorize]
public class CallHub(ICallService calls, ICallNotifier notifier, ILogger<CallHub> logger) : Hub
{
    public static string CallGroup(Guid callId) => $"call:{callId}";

    public async Task<CallResponse> StartCall(StartCallRequest request)
    {
        var userId = Context.User!.GetUserId();
        var result = await calls.StartAsync(userId, request, Context.ConnectionAborted);

        if (!result.Succeeded)
            throw new HubException(string.Join(" ", result.Errors));

        await Groups.AddToGroupAsync(Context.ConnectionId, CallGroup(result.Value.Id));
        TrackJoin(result.Value.Id);

        // Ring everyone else on their personal channel — they aren't in the
        // call's group yet, and may not even have the conversation open.
        var callees = result.Value.Participants
            .Where(p => p.UserId != userId)
            .Select(p => p.UserId)
            .ToList();

        await notifier.IncomingCallAsync(callees, result.Value);

        logger.LogInformation("Call {CallId} signalled to {Count} callees", result.Value.Id, callees.Count);

        return result.Value;
    }

    public async Task<CallResponse> Join(Guid callId)
    {
        var result = await calls.JoinAsync(Context.User!.GetUserId(), callId, Context.ConnectionAborted);

        if (!result.Succeeded)
            throw new HubException(string.Join(" ", result.Errors));

        await Groups.AddToGroupAsync(Context.ConnectionId, CallGroup(callId));
        TrackJoin(callId);
        await Broadcast(result.Value);

        return result.Value;
    }

    public async Task<CallResponse> Decline(Guid callId)
    {
        var result = await calls.DeclineAsync(Context.User!.GetUserId(), callId, Context.ConnectionAborted);

        if (!result.Succeeded)
            throw new HubException(string.Join(" ", result.Errors));

        await Broadcast(result.Value);

        return result.Value;
    }

    public async Task<CallResponse> Leave(Guid callId)
    {
        var result = await calls.LeaveAsync(Context.User!.GetUserId(), callId, Context.ConnectionAborted);

        if (!result.Succeeded)
            throw new HubException(string.Join(" ", result.Errors));

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, CallGroup(callId));
        TrackLeave(callId);
        await Broadcast(result.Value);

        return result.Value;
    }

    // Mute, camera on/off, screen share on/off. Broadcast so peers can update
    // tiles without waiting to infer it from the media stream.
    public async Task<CallResponse> UpdateMediaState(Guid callId, UpdateMediaStateRequest request)
    {
        var result = await calls.UpdateMediaStateAsync(
            Context.User!.GetUserId(), callId, request, Context.ConnectionAborted);

        if (!result.Succeeded)
            throw new HubException(string.Join(" ", result.Errors));

        await Broadcast(result.Value);

        return result.Value;
    }

    // The relay. Deliberately opaque: the server checks that both parties are
    // in the same live call, stamps who it came from, and forwards. It never
    // parses SDP, so browser-side WebRTC changes need no server change.
    public async Task SendSignal(CallSignal signal)
    {
        var userId = Context.User!.GetUserId();

        if (!await calls.CanSignalAsync(userId, signal.TargetUserId, signal.CallId, Context.ConnectionAborted))
            throw new HubException("You're not in that call.");

        // Overwritten rather than trusted — a client could otherwise claim to be
        // someone else in the negotiation.
        signal.FromUserId = userId;

        await notifier.SignalAsync(signal.TargetUserId, signal);
    }

    // A dropped connection has to end the user's presence in the call,
    // otherwise a ghost participant holds a seat and the call never ends.
    // Which calls this connection is in is tracked per-connection: cheap, and
    // it dies with the connection.
    public override async Task OnDisconnectedAsync(Exception exception)
    {
        var userId = Context.User?.GetUserId();

        if (userId.HasValue && Context.Items.TryGetValue(JoinedCallsKey, out var value) &&
            value is HashSet<Guid> joined)
        {
            foreach (var callId in joined.ToList())
            {
                // CancellationToken.None: the connection is already gone, so
                // Context.ConnectionAborted is signalled and would cancel this.
                var result = await calls.LeaveAsync(userId.Value, callId, CancellationToken.None);

                if (result.Succeeded)
                    await Broadcast(result.Value);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private const string JoinedCallsKey = "joined-calls";

    private void TrackJoin(Guid callId)
    {
        if (Context.Items.TryGetValue(JoinedCallsKey, out var value) && value is HashSet<Guid> joined)
            joined.Add(callId);
        else
            Context.Items[JoinedCallsKey] = new HashSet<Guid> { callId };
    }

    private void TrackLeave(Guid callId)
    {
        if (Context.Items.TryGetValue(JoinedCallsKey, out var value) && value is HashSet<Guid> joined)
            joined.Remove(callId);
    }

    private Task Broadcast(CallResponse call) =>
        notifier.CallUpdatedAsync(call.Id, call.Participants.Select(p => p.UserId).ToList(), call);
}
