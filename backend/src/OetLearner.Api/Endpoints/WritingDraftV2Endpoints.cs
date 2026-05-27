using OetLearner.Api.Contracts;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

public static class WritingDraftV2Endpoints
{
    public static IEndpointRouteBuilder MapWritingDraftV2Endpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing/drafts")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapPut("/{scenarioId:guid}/{mode}", async (
            Guid scenarioId,
            string mode,
            WritingDraftV2UpsertRequest request,
            HttpContext http,
            IWritingDraftServiceV2 service,
            CancellationToken ct) =>
        {
            var draft = await service.SaveAsync(http.WritingV2UserId(), scenarioId, mode,
                new WritingDraftV2SaveRequest(request.Content, request.WordCount, request.TimeSpentSeconds), ct);
            return Results.Ok(WritingV2ResponseMapper.ToResponse(draft));
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("PutWritingDraftV2");

        group.MapGet("/{scenarioId:guid}/{mode}", async (
            Guid scenarioId,
            string mode,
            HttpContext http,
            IWritingDraftServiceV2 service,
            CancellationToken ct) =>
        {
            var draft = await service.GetAsync(http.WritingV2UserId(), scenarioId, mode, ct);
            return draft is null ? Results.NotFound() : Results.Ok(WritingV2ResponseMapper.ToResponse(draft));
        })
        .WithName("GetWritingDraftV2");

        group.MapDelete("/{scenarioId:guid}/{mode}", async (
            Guid scenarioId,
            string mode,
            HttpContext http,
            IWritingDraftServiceV2 service,
            CancellationToken ct) =>
        {
            await service.DeleteAsync(http.WritingV2UserId(), scenarioId, mode, ct);
            return Results.NoContent();
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("DeleteWritingDraftV2");

        return app;
    }
}
