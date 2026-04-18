using System.Security.Claims;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin endpoints for the global ReadingPolicy singleton + per-user
/// overrides. Slice R6. See <c>docs/READING-AUTHORING-POLICY.md</c>.
/// </summary>
public static class ReadingPolicyAdminEndpoints
{
    public static IEndpointRouteBuilder MapReadingPolicyAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/reading-policy")
            .RequireAuthorization("AdminContentWrite")
            .RequireRateLimiting("PerUserWrite");

        group.MapGet("", async (IReadingPolicyService svc, CancellationToken ct) =>
        {
            var policy = await svc.GetGlobalAsync(ct);
            return Results.Ok(policy);
        });

        group.MapPut("", async (
            ReadingPolicy next,
            IReadingPolicyService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var updated = await svc.UpsertGlobalAsync(next, adminId, ct);
            return Results.Ok(updated);
        });

        group.MapGet("/users/{userId}", async (
            string userId, IReadingPolicyService svc, CancellationToken ct) =>
        {
            var row = await svc.GetUserOverrideAsync(userId, ct);
            return Results.Ok(row);
        });

        group.MapPut("/users/{userId}", async (
            string userId, ReadingUserPolicyOverride next,
            IReadingPolicyService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var row = await svc.UpsertUserOverrideAsync(userId, next, adminId, ct);
            return Results.Ok(row);
        });

        return app;
    }
}
