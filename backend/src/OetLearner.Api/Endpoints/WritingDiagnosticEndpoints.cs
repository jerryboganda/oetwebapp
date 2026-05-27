using OetLearner.Api.Contracts;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

public static class WritingDiagnosticEndpoints
{
    public static IEndpointRouteBuilder MapWritingDiagnosticEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing/diagnostic")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapPost("/start", async (
            WritingDiagnosticStartRequest? request,
            HttpContext http,
            IWritingOnboardingService service,
            CancellationToken ct) =>
        {
            var session = await service.StartDiagnosticAsync(http.WritingV2UserId(), request?.ScenarioId, ct);
            return Results.Created($"/v1/writing/diagnostic/sessions/{session.Id}", session);
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("StartWritingDiagnostic");

        group.MapGet("/sessions/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingOnboardingService service,
            CancellationToken ct) =>
        {
            var session = await service.GetDiagnosticSessionAsync(http.WritingV2UserId(), id, ct);
            return session is null ? Results.NotFound() : Results.Ok(session);
        })
        .WithName("GetWritingDiagnosticSession");

        group.MapPost("/sessions/{id:guid}/begin-writing", async (
            Guid id,
            HttpContext http,
            IWritingOnboardingService service,
            CancellationToken ct) =>
        {
            var session = await service.BeginDiagnosticWritingPhaseAsync(http.WritingV2UserId(), id, ct);
            return session is null ? Results.NotFound() : Results.Ok(session);
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("BeginWritingDiagnosticWritingPhase");

        group.MapPost("/sessions/{id:guid}/submit", async (
            Guid id,
            WritingDiagnosticSubmitRequest request,
            HttpContext http,
            IWritingOnboardingService service,
            CancellationToken ct) =>
        {
            var submission = await service.SubmitDiagnosticAsync(http.WritingV2UserId(), id, request, ct);
            return submission is null ? Results.NotFound() : Results.Accepted($"/v1/writing/diagnostic/sessions/{id}/results", submission);
        })
        .RequireRateLimiting("writing-submissions-free")
        .WithName("SubmitWritingDiagnostic");

        group.MapGet("/sessions/{id:guid}/results", async (
            Guid id,
            HttpContext http,
            IWritingOnboardingService service,
            CancellationToken ct) =>
        {
            var results = await service.GetDiagnosticResultsAsync(http.WritingV2UserId(), id, ct);
            return results is null ? Results.NotFound() : Results.Ok(results);
        })
        .WithName("GetWritingDiagnosticResults");

        return app;
    }
}
