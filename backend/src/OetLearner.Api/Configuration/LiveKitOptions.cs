namespace OetLearner.Api.Configuration;

/// <summary>
/// Configuration block for the LiveKit (or LiveKit-compatible) gateway
/// that powers Phase 3 live-tutor rooms in the OET Speaking module.
///
/// All fields are bound from the <c>LiveKit</c> section of
/// <c>appsettings.json</c>. When <see cref="Provider"/> is set to
/// <c>disabled</c> (the default in dev), <see cref="IsEnabled"/> returns
/// <c>false</c> and callers should short-circuit to the stub gateway —
/// no provider SDK is contacted.
/// </summary>
public sealed class LiveKitOptions
{
    public const string SectionName = "LiveKit";

    /// <summary>Provider key. <c>livekit_cloud</c> in prod;
    /// <c>disabled</c> short-circuits to the stub.</summary>
    public string Provider { get; set; } = "livekit_cloud";

    /// <summary>LiveKit API key — used to mint access tokens.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>LiveKit API secret — JWT signing key for access tokens.</summary>
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>LiveKit signalling endpoint (returned to clients alongside
    /// the access token so the frontend SDK knows where to connect).</summary>
    public string WssUrl { get; set; } = "wss://example.livekit.cloud";

    /// <summary>Shared secret used to verify <c>Authorization</c>-header
    /// HMAC signatures on inbound LiveKit webhooks.</summary>
    public string WebhookSigningSecret { get; set; } = string.Empty;

    /// <summary>Hard ceiling for room duration before the backend
    /// auto-ends it. Defaults to 30 minutes.</summary>
    public int DefaultMaxDurationSeconds { get; set; } = 1800;

    /// <summary>When true, the gateway requests track-composite egress
    /// for the room as soon as it transitions to <c>Active</c>.</summary>
    public bool EgressEnabled { get; set; } = true;

    /// <summary>Destination bucket for egress recordings (e.g. an
    /// S3-compatible URL like <c>s3://oet-speaking-recordings</c>).
    /// Production requires this when egress is enabled. The full output
    /// path emitted to LiveKit is
    /// <c>{EgressBucket}/oet-speaking/{RoomName}.ogg</c>.</summary>
    public string EgressBucket { get; set; } = string.Empty;

    /// <summary>Optional region for the egress S3 bucket.</summary>
    public string EgressBucketRegion { get; set; } = string.Empty;

    /// <summary>Server-side S3 credentials used by LiveKit Egress.</summary>
    public string EgressAccessKey { get; set; } = string.Empty;
    public string EgressSecret { get; set; } = string.Empty;

    /// <summary>Optional S3-compatible endpoint for the egress bucket.</summary>
    public string EgressEndpoint { get; set; } = string.Empty;
    public bool EgressForcePathStyle { get; set; }

    /// <summary>True when the provider is configured for live calls.
    /// False short-circuits room provisioning to the stub gateway, which
    /// returns synthetic identifiers.</summary>
    public bool IsEnabled => !string.Equals(Provider, "disabled", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ApiSecret)
        && !string.IsNullOrWhiteSpace(WebhookSigningSecret)
        && (!EgressEnabled
            || (!string.IsNullOrWhiteSpace(EgressBucket)
                && !string.IsNullOrWhiteSpace(EgressAccessKey)
                && !string.IsNullOrWhiteSpace(EgressSecret)));
}
