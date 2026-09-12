namespace OetLearner.Api.Services.DeviceIntegrity;

/// <summary>Stable provider codes surfaced on <see cref="DeviceIntegrityVerdict.Provider"/>.</summary>
public static class DeviceIntegrityProviders
{
    public const string PlayIntegrity = "play_integrity";
    public const string AppAttest = "app_attest";

    /// <summary>No hardware-attestation provider applies (unknown platform, disabled,
    /// or the caller hit an error before provider selection).</summary>
    public const string Unavailable = "unavailable";
}

/// <summary>Platform labels accepted from clients. Long forms match the video
/// attestation platform values ("capacitor-android" / "capacitor-ios"); the short
/// forms are accepted for convenience.</summary>
public static class DeviceIntegrityPlatforms
{
    public const string Android = "capacitor-android";
    public const string Ios = "capacitor-ios";
    public const string AndroidShort = "android";
    public const string IosShort = "ios";

    public static bool IsAndroid(string? platform)
    {
        var trimmed = platform?.Trim();
        return string.Equals(trimmed, Android, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, AndroidShort, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsIos(string? platform)
    {
        var trimmed = platform?.Trim();
        return string.Equals(trimmed, Ios, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, IosShort, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Input to a verifier. <see cref="Nonce"/> has ALREADY been issued and consumed
/// by <c>DeviceIntegrityService</c> before the verifier runs; verifiers must
/// additionally bind that nonce into the provider payload where the platform
/// supports it (Play Integrity's <c>requestDetails.nonce</c>) so a captured
/// integrity token cannot be replayed against a fresh challenge.
/// </summary>
public sealed record DeviceIntegrityRequest(
    string UserId,
    string Platform,
    string? IntegrityToken,
    string? KeyId,
    string? Nonce);

/// <summary>
/// Sanitised outcome of device-integrity verification. This is a RISK SIGNAL —
/// callers must NEVER use <see cref="Trusted"/> to make an authentication or
/// authorization decision; it only feeds a risk/policy layer.
/// </summary>
/// <param name="Trusted">True only when the provider affirmatively attested a
/// genuine app on a healthy device. A provider error, an unimplemented path, or
/// an inconclusive verdict is always <c>false</c>.</param>
/// <param name="Provider">One of <see cref="DeviceIntegrityProviders"/>.</param>
/// <param name="Verdict">Coarse result: "trusted" | "not_trusted" | "unknown" |
/// "not_implemented".</param>
/// <param name="Reason">Short machine-readable reason; safe to log. Never
/// contains secrets, the raw token, or PII.</param>
/// <param name="RawSummaryJson">Sanitised provider summary (VERDICT ENUMS ONLY,
/// never the raw token / assertion / nonce / certificate digests). Null when the
/// provider did not return a parseable payload.</param>
public sealed record DeviceIntegrityVerdict(
    bool Trusted,
    string Provider,
    string Verdict,
    string? Reason,
    string? RawSummaryJson)
{
    /// <summary>
    /// Provider errored, was unreachable/disabled, could not be parsed, or the
    /// platform is unsupported. NEVER carries sensitive data and is NEVER trusted.
    /// </summary>
    public static DeviceIntegrityVerdict Unknown(
        string reason, string provider = DeviceIntegrityProviders.Unavailable) =>
        new(false, provider, "unknown", reason, null);
}

/// <summary>
/// One hardware-attestation adapter (Play Integrity, App Attest, …).
/// Implementations must NEVER throw for a provider failure — return an
/// <see cref="DeviceIntegrityVerdict.Unknown"/> verdict instead (the composing
/// service also defends against a throw, but verifiers should not rely on it).
/// Implementations must NEVER return <c>Trusted = true</c> on any error,
/// timeout, unparseable response, or unimplemented path.
/// </summary>
public interface IDeviceIntegrityVerifier
{
    /// <summary>Stable provider code (see <see cref="DeviceIntegrityProviders"/>).</summary>
    string Provider { get; }

    /// <summary>True when this provider has the configuration it needs to run.</summary>
    bool IsConfigured { get; }

    /// <summary>True when this provider is the right one for the given platform.</summary>
    bool Handles(string platform);

    Task<DeviceIntegrityVerdict> VerifyAsync(DeviceIntegrityRequest request, CancellationToken ct);
}

/// <summary>
/// Picks the verifier for a platform at call time (mirrors the repo's
/// provider-selector pattern — see <c>IPronunciationAsrProviderSelector</c> /
/// <c>IPaperExtractionProviderSelector</c>). Returns null when no verifier
/// handles the platform, which the caller surfaces as an "unknown" verdict
/// rather than a hard error.
/// </summary>
public interface IDeviceIntegrityVerifierSelector
{
    IDeviceIntegrityVerifier? Select(string platform);
}

public sealed class DeviceIntegrityVerifierSelector(IEnumerable<IDeviceIntegrityVerifier> verifiers)
    : IDeviceIntegrityVerifierSelector
{
    public IDeviceIntegrityVerifier? Select(string platform) =>
        verifiers.FirstOrDefault(v => v.Handles(platform));
}
