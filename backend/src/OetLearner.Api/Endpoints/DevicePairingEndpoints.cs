using System.Security.Claims;
using OetLearner.Api.Services;
using OetLearner.Api.Services.DevicePairing;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// H13 scaffold: QR-based device pairing for signed-in handoff between a
/// web/desktop session and the mobile app.
///
/// Flow (caller responsibility):
///   1. Web/desktop client (authenticated) hits POST /v1/device-pairing/initiate
///      → receives a 6-char <c>code</c> and <c>expiresAt</c>. Client renders a
///      QR code whose payload is a deep link such as
///      <c>https://app.oetwithdrhesham.co.uk/pair?code=XXXXXX</c>.
///   2. Mobile app handles the deep link, reads the <c>code</c> query param,
///      and POSTs it plus a device-generated challenge to
///      /v1/device-pairing/redeem.
///   3. The redeem response carries a short-lived one-time handoff token.
///   4. Mobile exchanges that handoff token plus the same challenge at
///      /v1/device-pairing/exchange to receive a normal auth session.
///
/// Security:
///   * Initiate requires authentication + the "PerUser" rate limiter.
///   * Redeem/exchange are anonymous but tightly rate-limited by IP.
///   * Single-use: both the code and the handoff token are consumed atomically.
///   * Redeem never exposes the underlying auth account id.
/// </summary>
public static class DevicePairingEndpoints
{
    public static IEndpointRouteBuilder MapDevicePairingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/device-pairing");

        group.MapPost("/initiate", (HttpContext http, IDevicePairingCodeService service) =>
        {
            var accountId = http.AuthAccountId();
            var result = service.Initiate(accountId);
            return Results.Ok(new DevicePairingInitiateResponse(
                Code: result.Code,
                ExpiresAt: result.ExpiresAt));
        })
        .RequireAuthorization()
        .RequireRateLimiting("PerUser");

        group.MapPost("/redeem", (DevicePairingRedeemRequest request, IDevicePairingCodeService service) =>
        {
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return Results.BadRequest(new { error = "code_required" });
            }

            if (string.IsNullOrWhiteSpace(request.DeviceChallenge))
            {
                return Results.BadRequest(new { error = "device_challenge_required" });
            }

            return service.Redeem(request.Code, request.DeviceChallenge) switch
            {
                DevicePairingRedeemResult.Success success =>
                    Results.Ok(new DevicePairingRedeemResponse(success.HandoffToken, success.ExpiresAt)),
                DevicePairingRedeemResult.Expired =>
                    Results.StatusCode(StatusCodes.Status410Gone),
                DevicePairingRedeemResult.AlreadyRedeemed =>
                    Results.Conflict(new { error = "already_redeemed" }),
                DevicePairingRedeemResult.InvalidDeviceChallenge =>
                    Results.BadRequest(new { error = "invalid_device_challenge" }),
                _ => Results.NotFound(new { error = "not_found" }),
            };
        })
        .AllowAnonymous()
        .RequireRateLimiting("DevicePairingRedeem");

        group.MapPost("/exchange", async (
            DevicePairingExchangeRequest request,
            IDevicePairingCodeService pairingService,
            AuthService authService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.HandoffToken))
            {
                return Results.BadRequest(new { error = "handoff_token_required" });
            }

            if (string.IsNullOrWhiteSpace(request.DeviceChallenge))
            {
                return Results.BadRequest(new { error = "device_challenge_required" });
            }

            return pairingService.Exchange(request.HandoffToken, request.DeviceChallenge) switch
            {
                DevicePairingExchangeResult.Success success =>
                    Results.Ok(await authService.CompleteDirectSignInAsync(success.AuthAccountId, markEmailVerified: false, ct)),
                DevicePairingExchangeResult.Expired =>
                    Results.StatusCode(StatusCodes.Status410Gone),
                DevicePairingExchangeResult.AlreadyConsumed =>
                    Results.Conflict(new { error = "already_consumed" }),
                DevicePairingExchangeResult.ChallengeMismatch =>
                    Results.Forbid(),
                _ => Results.NotFound(new { error = "not_found" }),
            };
        })
        .AllowAnonymous()
        .RequireRateLimiting("DevicePairingRedeem");

        return app;
    }

    private static string AuthAccountId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(AuthTokenService.AuthAccountIdClaimType)
           ?? throw new InvalidOperationException("Authenticated auth account id is required.");
}

public sealed record DevicePairingInitiateResponse(string Code, DateTimeOffset ExpiresAt);

public sealed record DevicePairingRedeemRequest(string Code, string DeviceChallenge);

public sealed record DevicePairingRedeemResponse(string HandoffToken, DateTimeOffset ExpiresAt);

public sealed record DevicePairingExchangeRequest(string HandoffToken, string DeviceChallenge);
