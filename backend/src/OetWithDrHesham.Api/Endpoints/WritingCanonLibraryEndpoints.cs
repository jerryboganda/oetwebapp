using Microsoft.AspNetCore.Mvc;
using OetWithDrHesham.Api.Services.Writing;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// V2 learner-facing canon library. Mounted at /v1/writing/v2/canon — the
/// legacy /v1/writing/canon list endpoint in WritingPathwayEndpoints returns
/// V1 DTOs and we can't double-register. V2 adds the missing rule-detail and
/// per-learner violation-history routes.
/// </summary>
public static class WritingCanonLibraryEndpoints
{
    public static IEndpointRouteBuilder MapWritingCanonLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing/v2/canon")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapGet("/", async (
            [FromQuery] string? search,
            [FromQuery] string? severity,
            [FromQuery] string? category,
            HttpContext http,
            IWritingCanonService service,
            CancellationToken ct)
            => Results.Ok(await service.ListCanonRulesV2Async(
                http.WritingV2UserId(),
                search,
                severity,
                category,
                ct)))
            .WithName("ListWritingCanonRulesV2");

        group.MapGet("/{ruleId}", async (
            string ruleId,
            HttpContext http,
            IWritingCanonService service,
            CancellationToken ct) =>
        {
            var rule = await service.GetCanonRuleAsync(http.WritingV2UserId(), ruleId, ct);
            return rule is null ? Results.NotFound() : Results.Ok(rule);
        })
        .WithName("GetWritingCanonRuleV2");

        group.MapGet("/{ruleId}/violations/mine", async (
            string ruleId,
            HttpContext http,
            IWritingCanonService service,
            CancellationToken ct)
            => Results.Ok(await service.GetMyViolationsForRuleAsync(http.WritingV2UserId(), ruleId, ct)))
            .WithName("GetMyWritingCanonViolationsForRule");

        return app;
    }
}
