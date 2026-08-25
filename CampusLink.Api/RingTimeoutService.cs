using CampusLink.Core.Application.Interfaces;
using CampusLink.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace CampusLink.Api;

// A ring timeout can't be the caller's responsibility — they might close the
// laptop mid-ring, leaving a call that rings forever and blocks the "one live
// call per conversation" rule. This sweeps them server-side.
public class RingTimeoutService(
    IServiceScopeFactory scopeFactory,
    IOptions<WebRtcSettings> settings,
    ILogger<RingTimeoutService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ringTimeout = TimeSpan.FromSeconds(settings.Value.RingTimeoutSeconds);

        // Half the timeout: nobody waits more than ~1.5x the configured ring.
        var interval = TimeSpan.FromSeconds(Math.Max(5, settings.Value.RingTimeoutSeconds / 2.0));

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var calls = scope.ServiceProvider.GetRequiredService<ICallService>();

                var expired = await calls.ExpireStaleRingingAsync(ringTimeout, stoppingToken);

                if (expired > 0)
                    logger.LogInformation("Expired {Count} unanswered call(s)", expired);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed sweep must not kill the loop — the next tick retries.
                logger.LogError(ex, "Ring timeout sweep failed");
            }
        }
    }
}
