using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace CampusLink.Api;

public static class RateLimitPolicies
{
    public const string PublicRead = "public-read";
    public const string PublicWrite = "public-write";

    public static IServiceCollection AddCampusLinkRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Typeahead search fires on nearly every keystroke, so this has to
            // be generous enough for real typing while still stopping a scrape.
            options.AddPolicy(PublicRead, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        Window = TimeSpan.FromMinutes(1)
                    }));

            // Proposing an institution writes a row that a human has to review,
            // so it's deliberately tight.
            options.AddPolicy(PublicWrite, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromHours(1)
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString();

                await context.HttpContext.Response.WriteAsync(
                    "Too many requests. Please slow down.", cancellationToken);
            };
        });

    // Behind a proxy every request shares the load balancer's socket address,
    // which would put all users in one bucket — prefer the forwarded client IP.
    private static string PartitionKey(HttpContext context) =>
        context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded)
            ? forwarded.ToString().Split(',')[0].Trim()
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
