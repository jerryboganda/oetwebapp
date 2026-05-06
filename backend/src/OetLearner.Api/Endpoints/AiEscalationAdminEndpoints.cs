using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class AiEscalationAdminEndpoints
{
    public static IEndpointRouteBuilder MapAiEscalationAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        admin.MapGet("/ai-config/escalation-stats", async ([FromServices] IAIEscalationStatsService service, CancellationToken ct, string? configId)
            => Results.Ok(await service.GetStatsAsync(configId, ct)))
            .WithAdminRead("AdminAiConfig");

        admin.MapGet("/ai-config/escalation-stats/configs", async ([FromServices] IAIEscalationStatsService service, CancellationToken ct)
            => Results.Ok(await service.GetConfigStatsAsync(ct)))
            .WithAdminRead("AdminAiConfig");

        admin.MapGet("/ai-config/escalation-stats/{taskType}", async (string taskType, [FromServices] IAIEscalationStatsService service, CancellationToken ct)
            => Results.Ok(await service.GetStatsByTaskTypeAsync(taskType, ct)))
            .WithAdminRead("AdminAiConfig");

        return app;
    }
}
