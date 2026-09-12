namespace OetLearner.Api.Configuration;

/// <summary>
/// Server-side verification of Google Play Integrity (Android) and Apple App
/// Attest (iOS) — security standard MOB-09 / MOB-10.
///
/// <para>
/// CORE PRINCIPLE — an integrity verdict is a RISK SIGNAL, never an
/// authentication or authorization decision. A device that fails verification,
/// or whose verification cannot be completed, MUST NOT be locked out of sign-in,
/// session refresh, entitlement, exam submission, or any other access-control
/// path. Those remain governed exclusively by the auth/authz stack
/// (JWT bearer + <c>RequireAuthorization</c> policies). The verdict is fed to a
/// risk/policy layer that may, for a specific critical flow that opted in, add
/// step-up friction or extra logging — it never substitutes for a credential
/// and never grants access on its own.
/// </para>
///
/// <para>
/// WHY <see cref="FailClosedOnProviderError"/> DEFAULTS TO FALSE (fail-open):
/// Google's and Apple's verification endpoints are third-party dependencies. If
/// Play Integrity is rate-limited, region-blocked, returns a transient 5xx, or
/// Apple's verification material is briefly unavailable, a fail-closed default
/// would lock every legitimate user out of the app through no fault of their
/// own — a self-inflicted availability incident strictly worse than the
/// incremental risk signal lost. The standard's "deny on unknown" guidance
/// applies to ACCESS decisions (authn/authz); here, "unknown" merely means "no
/// extra risk signal for this call", which is logged for review and does not
/// block anything. Operators who consciously accept the availability trade-off
/// for a high-value flow can flip this on; that flip is auditable and recorded
/// in telemetry on every verification.
/// </para>
///
/// Bound from the <c>DeviceAttestation</c> configuration section.
/// </summary>
public sealed class DeviceAttestationOptions
{
    public const string SectionName = "DeviceAttestation";

    /// <summary>
    /// Master switch. Default <c>false</c> — verification is dormant and every
    /// call returns an "unknown" verdict until an operator explicitly enables
    /// it, so shipping this code changes no runtime behaviour until configured.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Enable Play Integrity (Android) verification.</summary>
    public bool AndroidEnabled { get; set; }

    /// <summary>Enable App Attest (iOS) verification.</summary>
    public bool IosEnabled { get; set; }

    /// <summary>
    /// Numeric Google Cloud project number that owns the Play Integrity API
    /// integration (GCP → the "Project number", e.g. <c>123456789012</c>). This
    /// is NOT the project id and NOT an API key.
    /// </summary>
    public string? GoogleCloudProjectNumber { get; set; }

    /// <summary>
    /// Android application id (the Gradle <c>applicationId</c>, e.g.
    /// <c>com.oetwithdrhesham.app</c>). Sent as the <c>{packageName}</c> path
    /// segment of the Play Integrity <c>decodeIntegrityToken</c> call and
    /// cross-checked against the token's <c>requestDetails.requestPackageName</c>.
    /// </summary>
    public string? AndroidPackageName { get; set; }

    /// <summary>
    /// Google service-account JSON with access to the Play Integrity API.
    /// OPTIONAL: when null/blank, the verifier falls back to the SAME Firebase
    /// service-account JSON already used by <c>MobilePushDispatcher</c> for FCM
    /// (<c>PushSettings.FcmServiceAccountJson</c>), so an operator who already
    /// configured push does not have to paste a second key — provided that key
    /// has been granted the Play Integrity scope in GCP. A secret; never logged
    /// or returned to a client.
    /// </summary>
    public string? GoogleServiceAccountJson { get; set; }

    /// <summary>Apple Developer Team ID (10-char, e.g. <c>ABCDE12345</c>).</summary>
    public string? AppleTeamId { get; set; }

    /// <summary>iOS bundle id (e.g. <c>com.oetwithdrhesham.app</c>).</summary>
    public string? AppleBundleId { get; set; }

    /// <summary>
    /// App Attest key id (<c>DCAppAttestService</c> key identifier).
    /// Informational / diagnostic — a real assertion carries its own key id; this
    /// is a fallback for logging and for operators confirming native wiring.
    /// </summary>
    public string? AppleKeyId { get; set; }

    /// <summary>
    /// Apple App Attest / distribution private key (PEM, PKCS#8, ES256). Reserved
    /// for future use (see
    /// <c>OetLearner.Api.Services.DeviceIntegrity.AppAttestVerifier</c>): the
    /// App Attest verification path validates the attestation/assertion LOCALLY
    /// against Apple's App Attest root CA and makes no Apple server round-trip,
    /// so no signed JWT is minted today and this key is not yet read. A secret;
    /// never logged or returned.
    /// </summary>
    public string? ApplePrivateKeyPem { get; set; }

    /// <summary>
    /// When true, critical flows (e.g. exam submission, device trust) are
    /// expected to consult the verdict. Default <c>false</c> — verification runs
    /// and is logged, but no flow is required to act on it yet.
    /// </summary>
    public bool RequireForCriticalFlows { get; set; }

    /// <summary>
    /// When true, a NON-trusted verdict MAY be treated as blocking by a
    /// downstream RISK policy. Default <c>false</c> — integrity is a risk signal.
    /// Even when true this option never causes the auth/authz stack to reject a
    /// request; it only lets a policy layer apply step-up/deny for the specific
    /// critical flow that opted in.
    /// </summary>
    public bool TreatAsBlocking { get; set; }

    /// <summary>
    /// When true, a provider error (network failure, 5xx, malformed response)
    /// yields a blocking "unknown" verdict rather than an advisory one. Default
    /// <c>false</c> — see the class-level remarks for why fail-open is the safe
    /// default for a pure risk signal. The standard's "deny on unknown" guidance
    /// targets access decisions, not advisory signals.
    /// </summary>
    public bool FailClosedOnProviderError { get; set; }

    /// <summary>Android path is fully configured and enabled.</summary>
    public bool AndroidConfigured =>
        Enabled && AndroidEnabled
        && !string.IsNullOrWhiteSpace(GoogleCloudProjectNumber)
        && !string.IsNullOrWhiteSpace(AndroidPackageName);

    /// <summary>iOS path is configured and enabled. (Cryptographic verification
    /// itself is currently an explicit, honest "unavailable" gap — see
    /// AppAttestVerifier.)</summary>
    public bool IosConfigured =>
        Enabled && IosEnabled
        && !string.IsNullOrWhiteSpace(AppleTeamId)
        && !string.IsNullOrWhiteSpace(AppleBundleId);
}
