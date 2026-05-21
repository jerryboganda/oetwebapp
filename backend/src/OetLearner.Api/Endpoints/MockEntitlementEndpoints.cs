using System.Security.Claims;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Mocks Module Phase 1 — learner-facing read-only entitlement endpoints.
/// Currently exposes the per-mock-type credit summary consumed by the
/// dashboard / paywall CTA. Mutations (credit consumption) happen inside
/// <see cref="MockEntitlementService.DebitAsync"/> when an attempt starts.
///
/// Phase 8b — the response shape projects each summary item to expose
/// <c>available</c> alongside <c>remaining</c> so the setup screen can gate
/// bundle-type buttons by the "available" semantic (granted minus consumed,
/// or <c>int.MaxValue</c> for subscribers).
/// </summary>
public static class MockEntitlementEndpoints
{
    public static IEndpointRouteBuilder MapMockEntitlementEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1/mocks/entitlements").RequireAuthorization("LearnerOnly");

        v1.MapGet("/summary", async (
            HttpContext http,
            IMockEntitlementService service,
            CancellationToken ct) =>
        {
            var userId = http.UserId();
            var summary = await service.SummariseAsync(userId, ct);
            // Project to a shape that pairs `remaining` with an `available`
            // alias and surfaces `anyExhausted` server-side so clients don't
            // have to recompute it.
            var items = summary.Items.Select(item => new
            {
                mockType = item.MockType,
                label = item.Label,
                granted = item.Granted,
                consumed = item.Consumed,
                remaining = item.Remaining,
                available = item.Remaining,
                unlimited = item.Unlimited,
            }).ToList();
            var anyExhausted = items.Any(i => i.granted > 0 && i.available <= 0 && !i.unlimited);
            return Results.Ok(new
            {
                tier = summary.Tier,
                hasEligibleSubscription = summary.HasEligibleSubscription,
                isTrial = summary.IsTrial,
                items,
                anyExhausted,
            });
        });

        return app;
    }
}

file static class MockEntitlementHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
