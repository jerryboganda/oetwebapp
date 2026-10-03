using OetLearner.Api.Contracts;
using OetLearner.Api.Services;
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
            ILoggerFactory loggers,
            CancellationToken ct) =>
        {
            try
            {
                var draft = await service.SaveAsync(http.WritingV2UserId(), scenarioId, mode,
                    new WritingDraftV2SaveRequest(request.Content, request.WordCount, request.TimeSpentSeconds,
                        request.ExpectedVersion, request.Phase, request.ReadingSecondsRemaining, request.WritingSecondsRemaining), ct);
                return Results.Ok(WritingV2ResponseMapper.ToResponse(draft));
            }
            catch (ApiException conflict) when (conflict.StatusCode == StatusCodes.Status409Conflict)
            {
                // An expected compare-and-set outcome the client resolves (re-read + adopt or
                // ask the learner), not a server fault: log it as such instead of letting the
                // exception handler record an unhandled error. Same body as the global handler.
                loggers.CreateLogger("OetLearner.Api.Endpoints.WritingDraftV2").LogInformation(
                    "Writing draft save refused ({Code}) for scenario {ScenarioId} mode {Mode}, expectedVersion {ExpectedVersion}.",
                    conflict.ErrorCode, scenarioId, mode, request.ExpectedVersion);
                return Results.Content(JsonSupport.Serialize(new
                {
                    code = conflict.ErrorCode,
                    message = conflict.Message,
                    fieldErrors = Array.Empty<object>(),
                    retryable = conflict.Retryable,
                    supportHint = conflict.SupportHint,
                    correlationId = http.Items.TryGetValue("CorrelationId", out var cid) ? cid as string : null,
                }), "application/problem+json", statusCode: StatusCodes.Status409Conflict);
            }
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
