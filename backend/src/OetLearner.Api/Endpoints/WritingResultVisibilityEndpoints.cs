using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// WS-B4 Section D/E: learner-facing gated feedback + rewrite comparison, the
/// learner-effective result-visibility read, and the admin result-visibility
/// get/upsert. Routes ARE the contract (see lib/writing/exam-api.ts §15).
///
/// Auth mirrors neighbouring endpoints:
///  • learner routes  → "LearnerOnly", owner-checked in the service.
///  • admin routes    → "AdminOnly" group + AdminContent{Read,Write} permission.
/// </summary>
public static class WritingResultVisibilityEndpoints
{
    public static IEndpointRouteBuilder MapWritingResultVisibilityEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Learner: gated feedback + rewrite comparison (owner-checked in service) ──
        var learner = app.MapGroup("/v1/writing/submissions")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        learner.MapGet("/{id:guid}/feedback", async (
            Guid id,
            HttpContext http,
            IWritingResultFeedbackService service,
            CancellationToken ct) =>
        {
            var dto = await service.GetFeedbackAsync(http.WritingV2UserId(), id, ct);
            return Results.Ok(dto);
        })
        .WithName("GetWritingSubmissionFeedback");

        learner.MapGet("/{id:guid}/rewrite-comparison", async (
            Guid id,
            HttpContext http,
            IWritingResultFeedbackService service,
            CancellationToken ct) =>
        {
            var dto = await service.GetRewriteComparisonAsync(http.WritingV2UserId(), id, ct);
            return Results.Ok(dto);
        })
        .WithName("GetWritingRewriteComparison");

        // ── Learner: effective result-visibility for a scenario (or global) ─────────
        app.MapGet("/v1/writing/result-visibility", async (
            [FromQuery] Guid? scenarioId,
            IWritingResultVisibilityService service,
            CancellationToken ct) =>
        {
            var dto = await service.ResolveDtoAsync(scenarioId, ct);
            return Results.Ok(dto);
        })
        .RequireAuthorization("LearnerOnly")
        .RequireRateLimiting("PerUser")
        .WithName("GetWritingEffectiveResultVisibility");

        // ── Admin: result-visibility config get/upsert ──────────────────────────────
        var admin = app.MapGroup("/v1/admin/writing/result-visibility")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser")
            .WithTags("WritingResultVisibilityAdmin");

        admin.MapGet("", async (
            [FromQuery] Guid? scenarioId,
            IWritingResultVisibilityService service,
            CancellationToken ct) =>
        {
            var dto = await service.ResolveDtoAsync(scenarioId, ct);
            return Results.Ok(dto);
        })
        .WithAdminRead("AdminContentRead")
        .WithName("GetWritingResultVisibilityConfig");

        admin.MapPut("", async (
            WritingResultVisibilityUpsertRequest request,
            IWritingResultVisibilityService service,
            CancellationToken ct) =>
        {
            var dto = await service.UpsertAsync(request.ToDto(), request.ScenarioId, ct);
            return Results.Ok(dto);
        })
        .WithAdminWrite("AdminContentWrite")
        .WithName("UpdateWritingResultVisibilityConfig");

        return app;
    }
}

/// <summary>
/// Admin upsert payload: the visibility flags plus the optional target scenarioId
/// (null → the global default row). Mirrors the frontend
/// <c>WritingResultVisibilityDto &amp; { scenarioId? }</c> PUT body.
/// </summary>
public sealed record WritingResultVisibilityUpsertRequest(
    Guid? ScenarioId,
    bool ShowSubmissionReceived,
    bool ShowAiEstimate,
    bool ShowTutorScore,
    bool ShowFullCriteria,
    bool ShowAnnotatedResponse,
    bool ShowMissingContent,
    bool ShowModelAnswer,
    bool ShowContentChecklist,
    bool AllowRewrite)
{
    public WritingResultVisibilityDto ToDto() => new(
        ScenarioId,
        ShowSubmissionReceived,
        ShowAiEstimate,
        ShowTutorScore,
        ShowFullCriteria,
        ShowAnnotatedResponse,
        ShowMissingContent,
        ShowModelAnswer,
        ShowContentChecklist,
        AllowRewrite,
        DateTimeOffset.UtcNow);
}
