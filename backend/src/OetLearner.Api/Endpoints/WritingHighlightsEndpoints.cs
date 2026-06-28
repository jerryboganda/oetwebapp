using OetLearner.Api.Contracts;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Learner-facing persistence for Case Notes PDF highlights, keyed by
/// (user, scenario) so marks pre-load on every attempt. The exam surfaces
/// (practice / mock / paper) GET on load and PUT (debounced) as marks change.
/// </summary>
public static class WritingHighlightsEndpoints
{
    public static IEndpointRouteBuilder MapWritingHighlightsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing/highlights")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapGet("/{scenarioId:guid}", async (
            Guid scenarioId,
            HttpContext http,
            IWritingCaseNoteHighlightService service,
            CancellationToken ct) =>
        {
            var json = await service.GetAsync(http.WritingV2UserId(), scenarioId, ct);
            return Results.Ok(new WritingHighlightsResponse(json));
        })
        .WithName("GetWritingHighlights");

        group.MapPut("/{scenarioId:guid}", async (
            Guid scenarioId,
            WritingHighlightsUpsertRequest request,
            HttpContext http,
            IWritingCaseNoteHighlightService service,
            CancellationToken ct) =>
        {
            var json = await service.SaveAsync(http.WritingV2UserId(), scenarioId, request.HighlightsJson, ct);
            return Results.Ok(new WritingHighlightsResponse(json));
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("PutWritingHighlights");

        return app;
    }
}
