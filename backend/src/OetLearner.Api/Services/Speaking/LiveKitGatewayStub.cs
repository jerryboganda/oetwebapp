using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Placeholder <see cref="ILiveKitGateway"/> implementation used until the
/// real LiveKit SDK is wired in.
///
/// Behaviour:
/// <list type="bullet">
///   <item><see cref="CreateRoomAsync"/>, <see cref="MintAccessTokenAsync"/>,
///         and <see cref="StartEgressAsync"/> return synthetic identifiers
///         (<c>sid-…</c>, <c>tkn-…</c>, <c>egress-…</c>) so the rest of
///         the Phase 3 pipeline can run end-to-end in dev without a
///         provider account.</item>
///   <item><see cref="MintAccessTokenAsync"/> returns a base64-encoded
///         payload that mimics a JWT — sufficient for the API surface
///         contract but obviously not valid for real connections.</item>
///   <item><see cref="VerifyWebhookSignature"/> is REAL — it performs an
///         HMAC-SHA256 verification against
///         <see cref="LiveKitOptions.WebhookSigningSecret"/>, so the
///         webhook security tests work today.</item>
/// </list>
/// </summary>
public sealed class LiveKitGatewayStub : ILiveKitGateway
{
    private readonly IOptions<LiveKitOptions> _options;
    private readonly ILogger<LiveKitGatewayStub> _logger;

    public LiveKitGatewayStub(IOptions<LiveKitOptions> options, ILogger<LiveKitGatewayStub> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<LiveKitRoomCreationResult> CreateRoomAsync(string roomName, int maxDurationSeconds, CancellationToken ct)
    {
        var sid = $"sid-{Guid.NewGuid():N}";
        _logger.LogInformation(
            "LiveKitGatewayStub.CreateRoom name={RoomName} maxDuration={MaxDurationSeconds}s sid={Sid}",
            roomName,
            maxDurationSeconds,
            sid);
        return Task.FromResult(new LiveKitRoomCreationResult(sid, _options.Value.WssUrl));
    }

    public Task<string> MintAccessTokenAsync(
        string roomName,
        string identity,
        LiveKitTokenCapabilities caps,
        TimeSpan ttl,
        CancellationToken ct)
    {
        // Build a small JSON envelope that mirrors the payload the real
        // LiveKit token would carry, then base64-encode it so it visually
        // resembles a JWT segment. Real callers must NOT attempt to
        // connect using this — the gateway swap is what makes tokens
        // legitimate.
        var expiresAt = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();
        var payload = JsonSerializer.Serialize(new
        {
            identity,
            roomName,
            exp = expiresAt,
            canPublishAudio = caps.CanPublishAudio,
            canPublishVideo = caps.CanPublishVideo,
            canSubscribe = caps.CanSubscribe,
        });

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        var token = $"tkn-{Guid.NewGuid():N}.{encoded}";

        _logger.LogInformation(
            "LiveKitGatewayStub.MintAccessToken room={RoomName} identity={Identity} ttl={Ttl} canPubAudio={Audio} canPubVideo={Video} canSub={Sub}",
            roomName,
            identity,
            ttl,
            caps.CanPublishAudio,
            caps.CanPublishVideo,
            caps.CanSubscribe);

        return Task.FromResult(token);
    }

    public Task<string> StartEgressAsync(string roomName, string outputUrl, CancellationToken ct)
    {
        var egressId = $"egress-{Guid.NewGuid():N}";
        _logger.LogInformation(
            "LiveKitGatewayStub.StartEgress room={RoomName} output={OutputUrl} egressId={EgressId}",
            roomName,
            outputUrl,
            egressId);
        return Task.FromResult(egressId);
    }

    public Task<bool> StopEgressAsync(string egressId, CancellationToken ct)
    {
        _logger.LogInformation("LiveKitGatewayStub.StopEgress egressId={EgressId}", egressId);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Verify an HMAC-SHA256 signature against the configured webhook
    /// signing secret. Uses <see cref="CryptographicOperations.FixedTimeEquals"/>
    /// to avoid leaking timing information on signature mismatches.
    /// </summary>
    public bool VerifyWebhookSignature(string payload, string signature)
    {
        if (payload is null) throw new ArgumentNullException(nameof(payload));
        if (string.IsNullOrWhiteSpace(signature))
        {
            _logger.LogInformation("LiveKitGatewayStub.VerifyWebhookSignature missing_signature");
            return false;
        }

        var secret = _options.Value.WebhookSigningSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogWarning("LiveKitGatewayStub.VerifyWebhookSignature secret_not_configured");
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expected = Convert.ToHexString(computed).ToLowerInvariant();
        var supplied = signature.Trim().ToLowerInvariant();

        // Pad / truncate to equal length so FixedTimeEquals is well-defined.
        // Length mismatch is itself a mismatch — but we still want a constant-time path.
        if (supplied.Length != expected.Length)
        {
            _logger.LogInformation("LiveKitGatewayStub.VerifyWebhookSignature length_mismatch");
            return false;
        }

        var ok = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(supplied),
            Encoding.UTF8.GetBytes(expected));

        _logger.LogInformation(
            "LiveKitGatewayStub.VerifyWebhookSignature result={Result}",
            ok ? "match" : "mismatch");

        return ok;
    }
}
