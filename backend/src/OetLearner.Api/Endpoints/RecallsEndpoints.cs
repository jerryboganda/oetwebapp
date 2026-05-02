using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services.Recalls;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Unified Recalls endpoints. See <c>docs/RECALLS-MODULE-PLAN.md</c> §3.
/// All routes require <c>LearnerOnly</c> authorization.
/// </summary>
public static class RecallsEndpoints
{
    public static IEndpointRouteBuilder MapRecallsEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");
        var recalls = v1.MapGroup("/recalls");

        recalls.MapGet("/today", async (HttpContext http, RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetTodayAsync(http.UserId(), ct)));

        recalls.MapGet("/queue", async (
            HttpContext http,
            [FromQuery] int limit,
            RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetQueueAsync(http.UserId(), limit, ct)));

        recalls.MapPost("/star", async (
            HttpContext http,
            RecallsStarRequest request,
            RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.StarAsync(http.UserId(), request, ct)));

        recalls.MapPost("/listen-type", async (
            HttpContext http,
            RecallsListenTypeRequest request,
            RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListenAndTypeAsync(http.UserId(), request, ct)));

        recalls.MapGet("/audio/{termId}", async (
            string termId,
            [FromQuery] string? speed,
            RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.EnsureAudioAsync(termId, speed ?? "normal", ct)));

        recalls.MapGet("/library", async (
            HttpContext http,
            [FromQuery] string? bucket,
            [FromQuery] string? topic,
            RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetLibraryAsync(http.UserId(), bucket, topic, ct)));

        recalls.MapPost("/explain", async (
            HttpContext http,
            RecallsExplainRequest request,
            RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.ExplainMistakeAsync(http.UserId(), request, ct)));

        recalls.MapGet("/quiz", async (
            HttpContext http,
            [FromQuery] string? mode,
            [FromQuery] int limit,
            RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetQuizAsync(http.UserId(), mode ?? "listen_and_type", limit, ct)));

        recalls.MapGet("/report/week", async (
            HttpContext http, RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetWeeklyReportAsync(http.UserId(), ct)));

        recalls.MapGet("/revision-plan", async (
            HttpContext http, RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetRevisionPlanAsync(http.UserId(), ct)));

        // Admin-only: CSV bulk upload of vocabulary terms (spec §8).
        var adminRecalls = app.MapGroup("/v1/admin/recalls").RequireAuthorization("AdminContentWrite");
        adminRecalls.MapPost("/bulk-upload", async (
            IReadOnlyList<RecallsBulkUploadRow> rows,
            RecallsService svc, CancellationToken ct) =>
            Results.Ok(await svc.BulkUploadAsync(rows, ct)));

        return app;
    }
}

file static class RecallsHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
