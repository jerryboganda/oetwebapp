using OetLearner.Api.Contracts;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

public static class WritingMockEndpoints
{
    public static IEndpointRouteBuilder MapWritingMockEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing/mocks")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapGet("/", async (
            HttpContext http,
            IWritingMockService service,
            CancellationToken ct)
            => Results.Ok(await service.ListMocksAsync(http.WritingV2UserId(), ct)))
            .WithName("ListWritingMocks");

        group.MapPost("/start", async (
            WritingMockStartRequest request,
            HttpContext http,
            IWritingMockService service,
            CancellationToken ct) =>
        {
            var session = await service.StartMockAsync(http.WritingV2UserId(), request, ct);
            return Results.Created($"/v1/writing/mocks/sessions/{session.Id}", session);
        })
        .RequireRateLimiting("writing-submissions-free")
        .WithName("StartWritingMock");

        group.MapGet("/sessions/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingMockService service,
            CancellationToken ct) =>
        {
            var session = await service.GetMockSessionAsync(http.WritingV2UserId(), id, ct);
            return session is null ? Results.NotFound() : Results.Ok(session);
        })
        .WithName("GetWritingMockSession");

        group.MapPost("/sessions/{id:guid}/begin-writing", async (
            Guid id,
            HttpContext http,
            IWritingMockService service,
            CancellationToken ct) =>
        {
            var session = await service.BeginMockWritingAsync(http.WritingV2UserId(), id, ct);
            return session is null ? Results.NotFound() : Results.Ok(session);
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("BeginWritingMockWritingPhase");

        group.MapPost("/sessions/{id:guid}/submit", async (
            Guid id,
            WritingMockSubmitRequest request,
            HttpContext http,
            IWritingMockService service,
            CancellationToken ct) =>
        {
            var submission = await service.SubmitMockAsync(http.WritingV2UserId(), id, request, ct);
            return submission is null ? Results.NotFound() : Results.Accepted($"/v1/writing/mocks/sessions/{id}/results", submission);
        })
        .RequireRateLimiting("writing-submissions-free")
        .WithName("SubmitWritingMock");

        group.MapGet("/sessions/{id:guid}/results", async (
            Guid id,
            HttpContext http,
            IWritingMockService service,
            CancellationToken ct) =>
        {
            var results = await service.GetMockResultsAsync(http.WritingV2UserId(), id, ct);
            return results is null ? Results.NotFound() : Results.Ok(results);
        })
        .WithName("GetWritingMockResults");

        return app;
    }
}
