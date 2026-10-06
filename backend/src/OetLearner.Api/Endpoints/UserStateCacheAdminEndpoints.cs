using OetLearner.Api.Services.Caching;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Read-only counters and switch state of the per-process <see cref="UserStateCache"/>
/// (hits / misses / sets / invalidations per kind, entry count, TTL, config and runtime switch).
/// Per process: it describes the API slot that answered the request.
/// Operator runbook and the worst-case staleness table: docs/ops/user-state-cache.md.
/// </summary>
public static class UserStateCacheAdminEndpoints
{
    public static IEndpointRouteBuilder MapUserStateCacheAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/system/user-state-cache")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        group.MapGet("", (UserStateCache cache) => Results.Ok(cache.Snapshot()))
            .WithAdminRead("AdminSystemAdmin");

        return app;
    }
}
