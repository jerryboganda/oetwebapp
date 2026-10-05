using OetLearner.Api.Services.Admin;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// <c>GET /v1/admin/ops/snapshot</c>: the owner's one-call view of the load on the primary VPS (background job
/// queue depth by type, database connections by application name, live Speaking admitted/queued counts and a
/// remote-worker placeholder). Read-only, counts only. Same policy as the other system views
/// (<c>AdminSystemAdmin</c>, per-user rate limit).
/// </summary>
public static class AdminOpsSnapshotEndpoints
{
    public static IEndpointRouteBuilder MapAdminOpsSnapshotEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/ops")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        group.MapGet("/snapshot", async (AdminOpsSnapshotService service, CancellationToken ct)
            => Results.Ok(await service.GetAsync(ct)))
            .WithAdminRead("AdminSystemAdmin");

        return app;
    }
}
