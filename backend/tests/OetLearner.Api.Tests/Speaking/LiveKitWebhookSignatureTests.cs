using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// Phase 3 webhook security tests. Exercises the same HMAC-SHA256
/// verification that the <c>POST /v1/webhooks/livekit</c> endpoint relies
/// on. The endpoint contract is:
///
/// <list type="bullet">
///   <item>No <c>Authorization</c> header → 401. (Mirrored here as
///         "empty signature → false".)</item>
///   <item>Wrong signature → 401. ("invalid signature → false".)</item>
///   <item>Matching HMAC of the raw payload → 200. ("valid signature →
///         true".)</item>
/// </list>
///
/// We exercise the gateway stub directly rather than wire up the full
/// <c>WebApplicationFactory</c>, because the endpoint delegates 1:1 to
/// <see cref="ILiveKitGateway.VerifyWebhookSignature"/> and the stub's
/// crypto path is the actual subject under test.
/// </summary>
public sealed class LiveKitWebhookSignatureTests
{
    private const string SigningSecret = "test-livekit-webhook-secret-1234567890";
    private const string Payload = """
        {
            "event": "recording_finished",
            "id": "evt_test_001",
            "room": "oet-speaking-session-abc",
            "egressId": "egress-xyz"
        }
        """;

    private static LiveKitGatewayStub CreateGateway(string secret = SigningSecret)
    {
        var options = Options.Create(new LiveKitOptions
        {
            Provider = "disabled",
            ApiKey = string.Empty,
            ApiSecret = string.Empty,
            WssUrl = "wss://example.livekit.cloud",
            WebhookSigningSecret = secret,
        });
        return new LiveKitGatewayStub(options, NullLogger<LiveKitGatewayStub>.Instance);
    }

    private static string ComputeSignature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ── Test 1: missing Authorization header / empty signature ────────
    [Fact]
    public void LiveKitWebhook_RejectsRequestWithoutAuthorizationHeader()
    {
        var gateway = CreateGateway();

        // The endpoint short-circuits with 401 when the Authorization header
        // is missing — modelled here as an empty signature input. The gateway
        // MUST also refuse to verify against an empty signature, both for
        // defence-in-depth and because some HTTP clients drop empty headers
        // to a single whitespace character.
        Assert.False(gateway.VerifyWebhookSignature(Payload, signature: string.Empty));
        Assert.False(gateway.VerifyWebhookSignature(Payload, signature: "   "));
    }

    // ── Test 2: signature present but does not match ──────────────────
    [Fact]
    public void LiveKitWebhook_RejectsInvalidSignature()
    {
        var gateway = CreateGateway();

        // Right shape, wrong content — must be rejected.
        var tampered = ComputeSignature(Payload, secret: "wrong-secret");
        Assert.False(gateway.VerifyWebhookSignature(Payload, tampered));

        // Same secret but a different payload — also rejected because
        // verification is bound to the body bytes, not the secret alone.
        var differentPayload = Payload.Replace("evt_test_001", "evt_tampered_002");
        var sigForDifferent = ComputeSignature(differentPayload, SigningSecret);
        Assert.False(gateway.VerifyWebhookSignature(Payload, sigForDifferent));

        // Non-hex garbage — rejected.
        Assert.False(gateway.VerifyWebhookSignature(Payload, "not-a-valid-signature"));
    }

    // ── Test 3: signature matches the configured secret + body ────────
    [Fact]
    public void LiveKitWebhook_AcceptsValidSignature()
    {
        var gateway = CreateGateway();
        var signature = ComputeSignature(Payload, SigningSecret);

        Assert.True(gateway.VerifyWebhookSignature(Payload, signature));

        // Case-insensitive on the hex digits — providers vary between
        // upper and lower case, so the verifier normalises both sides.
        Assert.True(gateway.VerifyWebhookSignature(Payload, signature.ToUpperInvariant()));
    }

    // ── Defensive check: when no secret is configured, verification
    //                    must NOT silently pass. Otherwise an unconfigured
    //                    prod deploy would accept arbitrary webhook calls.
    [Fact]
    public void LiveKitWebhook_RejectsWhenSecretNotConfigured()
    {
        var gateway = CreateGateway(secret: string.Empty);
        var signature = ComputeSignature(Payload, SigningSecret);

        Assert.False(gateway.VerifyWebhookSignature(Payload, signature));
    }
}
