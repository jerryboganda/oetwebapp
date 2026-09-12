using System.Security.Claims;
using OetLearner.Api.Services.DeviceIntegrity;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Device-integrity verification surface (security standard MOB-09 / MOB-10).
///
/// <para>
/// Flow:
///   1. POST /v1/device-integrity/challenge → a single-use, user-bound, 90s
///      nonce (mirrors <c>VideoAttestationService.IssueChallengeAsync</c>). The
///      client feeds this nonce into the Play Integrity / App Attest SDK.
///   2. POST /v1/device-integrity/verify with the platform, the nonce, and the
///      platform attestation material → a sanitised verdict.
/// </para>
///
/// <para>
/// SECURITY POSTURE: this is an ADVISORY RISK SIGNAL, never an access control.
/// Both endpoints require an authenticated caller (<c>RequireAuthorization</c>)
/// and are rate-limited per user. The verdict NEVER grants, denies, or elevates
/// access — a caller reaches this endpoint only after normal authentication, and
/// nothing downstream is permitted to gate authn/authz on the result.
/// </para>
/// </summary>
public static class DeviceAttestationEndpoints
{
    public static IEndpointRouteBuilder MapDeviceAttestationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/device-integrity")
            .RequireAuthorization()
            .RequireRateLimiting("PerUserWrite");

        group.MapPost("/challenge", async (
            HttpContext http,
            DeviceIntegrityChallengeRequest? request,
            IDeviceIntegrityService service,
            CancellationToken ct) =>
        {
            var platform = request?.Platform?.Trim();
            if (string.IsNullOrWhiteSpace(platform))
            {
                return Results.BadRequest(new { code = "platform_required", message = "platform is required." });
            }

            var challenge = await service.IssueChallengeAsync(LearnerId(http), platform, ct);
            return Results.Ok(new DeviceIntegrityChallengeResponse(challenge.Nonce, challenge.ExpiresAt));
        })
        .WithName("IssueDeviceIntegrityChallenge")
        .WithSummary("Issues a single-use, 90s, user-bound nonce the client feeds into the platform attestation SDK.");

        group.MapPost("/verify", async (
            HttpContext http,
            DeviceIntegrityVerifyRequest? request,
            IDeviceIntegrityService service,
            CancellationToken ct) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Platform))
            {
                return Results.BadRequest(new { code = "platform_required", message = "platform is required." });
            }

            if (string.IsNullOrWhiteSpace(request.Nonce))
            {
                return Results.BadRequest(new { code = "nonce_required", message = "nonce (from /challenge) is required." });
            }

            if (string.IsNullOrWhiteSpace(request.IntegrityToken))
            {
                return Results.BadRequest(new { code = "integrity_token_required", message = "integrityToken is required." });
            }

            // RISK SIGNAL ONLY. The returned verdict is advisory telemetry for a
            // downstream policy layer; it is never consulted by the auth/authz
            // stack and this response never encodes an access decision.
            var verdict = await service.VerifyAsync(
                LearnerId(http), request.Platform.Trim(), request.IntegrityToken, request.KeyId, request.Nonce, ct);

            return Results.Ok(new DeviceIntegrityVerdictResponse(
                verdict.Trusted, verdict.Provider, verdict.Verdict, verdict.Reason));
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("VerifyDeviceIntegrity")
        .WithSummary("Verifies a Play Integrity token / App Attest object. Advisory risk signal — never gates access.");

        return app;
    }

    private static string LearnerId(HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}

public sealed record DeviceIntegrityChallengeRequest(string? Platform);

public sealed record DeviceIntegrityChallengeResponse(string Nonce, DateTimeOffset ExpiresAt);

public sealed record DeviceIntegrityVerifyRequest(
    string? Platform,
    string? IntegrityToken,
    string? Nonce,
    string? KeyId = null);

/// <summary>Client-facing verdict. Deliberately omits
/// <c>DeviceIntegrityVerdict.RawSummaryJson</c> (sanitised server-side anyway) to
/// keep the wire contract minimal and avoid leaking provider internals.</summary>
public sealed record DeviceIntegrityVerdictResponse(
    bool Trusted,
    string Provider,
    string Verdict,
    string? Reason);
