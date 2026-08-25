using CampusLink.Core.Application.Dtos.Calls;
using CampusLink.Core.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace CampusLink.Api.Hubs;

// Call events are addressed per user, not per group: an incoming call has to
// reach someone who isn't in the call's SignalR group yet (and may not even
// have the conversation on screen), and signaling is point-to-point by nature.
public class SignalRCallNotifier(IHubContext<CallHub> hub) : ICallNotifier
{
    public Task IncomingCallAsync(IReadOnlyList<Guid> userIds, CallResponse call) =>
        hub.Clients.Users(userIds.Select(id => id.ToString()).ToList())
            .SendAsync("IncomingCall", call);

    public Task CallUpdatedAsync(Guid callId, IReadOnlyList<Guid> userIds, CallResponse call) =>
        // Sent to every participant rather than the call group, so someone who
        // was ringing and never joined the group still learns it ended.
        hub.Clients.Users(userIds.Select(id => id.ToString()).ToList())
            .SendAsync("CallUpdated", call);

    public Task SignalAsync(Guid targetUserId, CallSignal signal) =>
        hub.Clients.User(targetUserId.ToString()).SendAsync("CallSignal", signal);
}

// Clients.User(...) needs to know what "user" means. Without this it uses the
// NameIdentifier claim by default — which is already our user id, but pinning
// it explicitly means a future claims change can't silently misroute calls.
public class UserIdProvider : IUserIdProvider
{
    public string GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
}
