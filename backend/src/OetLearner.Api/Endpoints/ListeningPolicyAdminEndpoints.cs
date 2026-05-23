using System.Security.Claims;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin endpoints for the global ListeningPolicy singleton + per-user
/// overrides. Mirrors <see cref="ReadingPolicyAdminEndpoints"/>.
/// </summary>
public static class ListeningPolicyAdminEndpoints
{
    public static IEndpointRouteBuilder MapListeningPolicyAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/listening-policy")
            .RequireAuthorization("AdminContentWrite")
            .RequireRateLimiting("PerUserWrite");

        group.MapGet("", async (IListeningPolicyService svc, CancellationToken ct) =>
        {
            var policy = await svc.GetGlobalAsync(ct);
            return Results.Ok(policy);
        });

        group.MapPut("", async (
            ListeningPolicy next,
            IListeningPolicyService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var updated = await svc.UpsertGlobalAsync(next, adminId, ct);
            return Results.Ok(updated);
        });

        group.MapGet("/users/{userId}", async (
            string userId, IListeningPolicyService svc, CancellationToken ct) =>
        {
            var row = await svc.GetUserOverrideAsync(userId, ct);
            return Results.Ok(row);
        });

        group.MapPut("/users/{userId}", async (
            string userId, ListeningUserPolicyOverride next,
            IListeningPolicyService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var row = await svc.UpsertUserOverrideAsync(userId, next, adminId, ct);
            return Results.Ok(row);
        });

        return app;
    }
}
