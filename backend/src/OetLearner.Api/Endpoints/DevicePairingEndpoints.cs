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
///      and POSTs it to /v1/device-pairing/redeem (no auth required on that
///      path — the code IS the one-time credential).
///   3. The redeem response carries the bound <c>authAccountId</c>. The
///      mobile client exchanges that for a standard refresh-token pair via
///      an existing sign-in/handoff path (out of scope for this scaffold).
///
/// Security:
///   * Initiate requires authentication + the "PerUser" rate limiter.
///   * Redeem is anonymous but rate-limited ("Public") — the 90-second TTL
///     plus the 32^6 (~10^9) code space makes brute-forcing intractable.
///   * Single-use: the service marks the code redeemed atomically.
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

            return service.Redeem(request.Code) switch
            {
                DevicePairingRedeemResult.Success success =>
                    Results.Ok(new DevicePairingRedeemResponse(success.AuthAccountId)),
                DevicePairingRedeemResult.Expired =>
                    Results.StatusCode(StatusCodes.Status410Gone),
                DevicePairingRedeemResult.AlreadyRedeemed =>
                    Results.Conflict(new { error = "already_redeemed" }),
                _ => Results.NotFound(new { error = "not_found" }),
            };
        });

        return app;
    }

    private static string AuthAccountId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(AuthTokenService.AuthAccountIdClaimType)
           ?? throw new InvalidOperationException("Authenticated auth account id is required.");
}

public sealed record DevicePairingInitiateResponse(string Code, DateTimeOffset ExpiresAt);

public sealed record DevicePairingRedeemRequest(string Code);

public sealed record DevicePairingRedeemResponse(string AuthAccountId);
