using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// WS6 — Speaking result-visibility: the learner-effective read plus the admin
/// get/upsert. Mirrors <see cref="WritingResultVisibilityEndpoints"/>.
///
/// Auth:
///  • learner read  → "LearnerOnly".
///  • admin routes  → "AdminOnly" group + AdminContent{Read,Write} permission.
/// </summary>
public static class SpeakingResultVisibilityEndpoints
{
    public static IEndpointRouteBuilder MapSpeakingResultVisibilityEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Learner: effective result-visibility for a card (or global) ─────────────
        app.MapGet("/v1/speaking/result-visibility", async (
            [FromQuery] string? rolePlayCardId,
            ISpeakingResultVisibilityService service,
            CancellationToken ct) =>
        {
            var dto = await service.ResolveDtoAsync(rolePlayCardId, ct);
            return Results.Ok(dto);
        })
        .RequireAuthorization("LearnerOnly")
        .WithTags("Speaking results")
        .WithName("GetSpeakingEffectiveResultVisibility");

        // ── Admin: result-visibility config get/upsert ──────────────────────────────
        var admin = app.MapGroup("/v1/admin/speaking/result-visibility")
            .RequireAuthorization("AdminOnly")
            .WithTags("SpeakingResultVisibilityAdmin");

        admin.MapGet("", async (
            [FromQuery] string? rolePlayCardId,
            ISpeakingResultVisibilityService service,
            CancellationToken ct) =>
        {
            var dto = await service.ResolveDtoAsync(rolePlayCardId, ct);
            return Results.Ok(dto);
        })
        .WithAdminRead("AdminContentRead")
        .WithName("GetSpeakingResultVisibilityConfig");

        admin.MapPut("", async (
            SpeakingResultVisibilityUpsertRequest request,
            ISpeakingResultVisibilityService service,
            CancellationToken ct) =>
        {
            var dto = await service.UpsertAsync(request.ToDto(), request.RolePlayCardId, ct);
            return Results.Ok(dto);
        })
        .WithAdminWrite("AdminContentWrite")
        .WithName("UpdateSpeakingResultVisibilityConfig");

        return app;
    }
}

/// <summary>
/// Admin upsert payload: the visibility flags plus the optional target
/// rolePlayCardId (null → the global default row). Mirrors the frontend
/// <c>SpeakingResultVisibilityDto &amp; { rolePlayCardId? }</c> PUT body.
/// </summary>
public sealed record SpeakingResultVisibilityUpsertRequest(
    string? RolePlayCardId,
    bool ShowSubmissionReceived,
    bool ShowAiEstimate,
    bool ShowReadinessBand,
    bool ShowTutorScore,
    bool ShowFullCriteria,
    bool ShowTranscript,
    bool ShowTutorComments,
    bool ShowRecommendedDrills,
    bool AllowReattempt)
{
    public SpeakingResultVisibilityDto ToDto() => new(
        RolePlayCardId,
        ShowSubmissionReceived,
        ShowAiEstimate,
        ShowReadinessBand,
        ShowTutorScore,
        ShowFullCriteria,
        ShowTranscript,
        ShowTutorComments,
        ShowRecommendedDrills,
        AllowReattempt,
        DateTimeOffset.UtcNow);
}
