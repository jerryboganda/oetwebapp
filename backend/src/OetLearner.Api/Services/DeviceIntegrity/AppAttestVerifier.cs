using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.DeviceIntegrity;

/// <summary>
/// Apple App Attest (iOS) — security standard MOB-10.
///
/// <para>
/// HONEST GAP — DELIBERATE AND DOCUMENTED. This verifier implements the
/// request/verdict plumbing, the configured / not-configured gates, the
/// rpId (Relying Party id) derivation, and the nonce requirement, but it does
/// NOT yet perform the cryptographic validation of the attestation object or the
/// assertion. It therefore returns an explicit <c>"not_implemented"</c> verdict
/// with <c>Trusted = false</c>. It never claims a device is attested.
/// </para>
///
/// <para>
/// WHY NOT HAND-ROLL IT: <c>DCAppAttestService.attestKey()</c> produces a binary
/// <c>attestationObject</c> (CBOR) carrying the authenticator data and an
/// <c>x5c</c> certificate chain rooted at Apple's App Attest Root CA. Verifying
/// it correctly needs all four of the following, and a partial implementation
/// that accepts a forged object is strictly worse than an honest gap:
/// (1) CBOR decoding of the attestation object (<c>fmt == "apple-appattest"</c>,
/// <c>authData</c>, <c>att</c>, <c>x5c</c>); (2) DER/ASN.1 construction of the
/// certificate chain plus X.509 path validation rooted at Apple's App Attest
/// Root CA; (3) reconstruction of the client-data nonce —
/// <c>SHA-256(authenticatorData || SHA-256(clientDataHash))</c> — and its
/// comparison against the OID <c>1.2.840.113635.100.8.2</c> extension inside the
/// leaf credential certificate; and (4) for assertions, ECDSA P-256 verification
/// over <c>SHA-256(authenticatorData || SHA-256(clientDataHash))</c> plus
/// monotonic signature-counter enforcement. There is no CBOR primitive in the
/// BCL and .NET ships no in-box App Attest validator.
/// </para>
///
/// <para>
/// TO FINISH (what is required): add the Microsoft-published
/// <c>System.Formats.Cbor</c> NuGet package (MIT — the only new dependency
/// needed) and implement the four steps above with <c>CborReader</c> plus
/// <c>X509Chain</c> configured with <c>CustomRootTrust</c> anchored at Apple's
/// App Attest Root CA (embed that PEM as an assembly resource, like the existing
/// brand-asset resources). Assertions additionally require persisting the
/// per-key public key and sign counter, which needs a new table/column —
/// currently out of scope because <c>Data/**</c> and <c>Domain/**</c> are owned
/// elsewhere. NOTE: verification is entirely LOCAL — there is no Apple server
/// round-trip, so no JWT is minted and the ES256 <c>.p8</c> handling mirrored
/// from <c>MobilePushDispatcher.CreateApnsJwt</c> is NOT applicable here.
/// </para>
/// </summary>
public sealed class AppAttestVerifier(
    IOptions<DeviceAttestationOptions> options,
    ILogger<AppAttestVerifier> logger) : IDeviceIntegrityVerifier
{
    private readonly DeviceAttestationOptions _options = options.Value;

    public string Provider => DeviceIntegrityProviders.AppAttest;

    public bool IsConfigured => _options.IosConfigured;

    public bool Handles(string platform) => DeviceIntegrityPlatforms.IsIos(platform);

    /// <summary>
    /// The rpId an App Attest assertion is bound to — base64 of
    /// <c>SHA-256("{TeamID}.{BundleID}")</c>. Exposed (and logged) so operators
    /// can confirm the native client is attesting for the SAME app identity the
    /// server would validate against. Null when either value is unconfigured.
    /// </summary>
    public string? ComputeRpIdBase64()
    {
        if (string.IsNullOrWhiteSpace(_options.AppleTeamId) || string.IsNullOrWhiteSpace(_options.AppleBundleId))
        {
            return null;
        }

        var rpId = $"{_options.AppleTeamId.Trim()}.{_options.AppleBundleId.Trim()}";
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
    }

    public Task<DeviceIntegrityVerdict> VerifyAsync(DeviceIntegrityRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!_options.Enabled || !_options.IosEnabled)
        {
            return Task.FromResult(DeviceIntegrityVerdict.Unknown("app_attest_disabled", Provider));
        }

        if (string.IsNullOrWhiteSpace(_options.AppleTeamId) || string.IsNullOrWhiteSpace(_options.AppleBundleId))
        {
            return Task.FromResult(DeviceIntegrityVerdict.Unknown("app_attest_not_configured", Provider));
        }

        // rpId is derived (not secret) and logged for operator diagnostics.
        var rpId = ComputeRpIdBase64();
        logger.LogInformation(
            "App Attest verification requested but not implemented; returning an explicit unavailable verdict "
            + "(never trusted). keyId={KeyId}, rpId={RpId}.",
            request.KeyId ?? "none",
            rpId ?? "unknown");

        return Task.FromResult(new DeviceIntegrityVerdict(
            Trusted: false,
            Provider: Provider,
            Verdict: "not_implemented",
            Reason: "app_attest_verification_unavailable: attestation/assertion cryptographic validation "
                + "requires CBOR decoding (System.Formats.Cbor) and X.509 chain validation to Apple's App Attest "
                + "Root CA; not implemented in this pass — no verdict is produced.",
            RawSummaryJson: null));
    }
}
