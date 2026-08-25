using System.Security.Cryptography;
using System.Text;
using CampusLink.Core.Application.Dtos.Calls;
using CampusLink.Core.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace CampusLink.Infrastructure.Identity;

public class WebRtcSettings
{
    public const string SectionName = "WebRtc";

    // Left empty on purpose: the configuration binder *appends* to a collection
    // that already has items, so a non-empty default would duplicate whatever
    // appsettings supplies. The fallback lives in the provider instead.
    public string[] StunUrls { get; set; } = [];

    // Public STUN is fine and free — it only reveals your public address, it
    // never carries media.
    public static readonly string[] DefaultStunUrls =
    [
        "stun:stun.l.google.com:19302",
        "stun:stun1.l.google.com:19302"
    ];

    // e.g. ["turn:turn.example.com:3478", "turns:turn.example.com:5349"].
    // Empty means relay is unavailable: calls will still work for most pairs
    // but fail behind symmetric NAT, which is common on campus networks.
    public string[] TurnUrls { get; set; } = [];

    // Must equal coturn's `static-auth-secret` with `use-auth-secret` enabled.
    // Never sent to clients — it only ever signs derived credentials.
    public string TurnSecret { get; set; }

    // How long a minted credential stays valid. Long enough to place a call and
    // survive a reconnect; short enough that a leaked one is worthless quickly.
    public int CredentialTtlSeconds { get; set; } = 3600;

    // Ringing calls older than this are swept to NoAnswer.
    public int RingTimeoutSeconds { get; set; } = 45;
}

// Implements coturn's REST-API credential scheme: the username is an expiry
// timestamp (optionally with an id after a colon) and the password is the
// base64 HMAC-SHA1 of that username under the shared secret. coturn recomputes
// the same HMAC to validate, so no per-user accounts exist on the TURN server
// and credentials expire on their own.
public class IceServerProvider(IOptions<WebRtcSettings> settings) : IIceServerProvider
{
    private readonly WebRtcSettings _settings = settings.Value;

    public IceServersResponse GetIceServers(Guid userId)
    {
        var servers = new List<IceServer>();

        // Distinct(): appsettings.json and appsettings.{Env}.json both binding
        // this array would otherwise hand the browser the same URL twice.
        var stun = (_settings.StunUrls.Length > 0 ? _settings.StunUrls : WebRtcSettings.DefaultStunUrls)
            .Distinct()
            .ToArray();

        if (stun.Length > 0)
            servers.Add(new IceServer { Urls = stun });

        var expiresAt = DateTime.UtcNow.AddSeconds(_settings.CredentialTtlSeconds);

        if (_settings.TurnUrls.Length > 0 && !string.IsNullOrWhiteSpace(_settings.TurnSecret))
        {
            var unixExpiry = DateTimeOffset.UtcNow
                .AddSeconds(_settings.CredentialTtlSeconds)
                .ToUnixTimeSeconds();

            // Tagging the user id makes relayed sessions attributable in coturn's
            // logs, which is how you find out who's burning relay bandwidth.
            var username = $"{unixExpiry}:{userId}";

            servers.Add(new IceServer
            {
                Urls = _settings.TurnUrls,
                Username = username,
                Credential = Sign(username, _settings.TurnSecret)
            });
        }

        return new IceServersResponse { IceServers = servers, ExpiresAt = expiresAt };
    }

    // HMAC-SHA1 is not a free choice here — it's what the coturn REST scheme
    // specifies, and it's a MAC over a short-lived token rather than a hash of
    // anything secret.
    private static string Sign(string username, string secret)
    {
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secret));

        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username)));
    }
}
