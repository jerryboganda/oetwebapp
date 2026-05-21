using System.Security.Claims;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Mocks Module Phase 1 — learner-facing read-only entitlement endpoints.
/// Currently exposes the per-mock-type credit summary consumed by the
/// dashboard / paywall CTA. Mutations (credit consumption) happen inside
/// <see cref="MockEntitlementService.DebitAsync"/> when an attempt starts.
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
            return Results.Ok(summary);
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
