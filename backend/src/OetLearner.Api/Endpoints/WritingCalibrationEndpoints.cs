using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin endpoints for the 50-letter calibration harness (spec §33).
/// Three GETs + one POST + one runner action under
/// <c>/v1/admin/writing/calibration</c>. All routes are gated by the
/// existing <c>AdminOnly</c> policy.
/// </summary>
public static class WritingCalibrationEndpoints
{
    public static IEndpointRouteBuilder MapWritingCalibrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/writing/calibration")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        group.MapGet("/letters", async (
            HttpContext http,
            IWritingCalibrationService service,
            CancellationToken ct)
            => Results.Ok(await service.ListLettersAsync(http.WritingV2UserId(), ct)))
            .WithName("WritingCalibrationListLetters");

        group.MapPost("/letters", async (
            [FromBody] WritingCalibrationLetterCreateRequest request,
            HttpContext http,
            IWritingCalibrationService service,
            CancellationToken ct)
            => Results.Ok(await service.AddCalibrationLetterAsync(http.WritingV2UserId(), request, ct)))
            .WithName("WritingCalibrationAddLetter");

        group.MapPost("/run", async (
            HttpContext http,
            IWritingCalibrationService service,
            CancellationToken ct)
            => Results.Ok(await service.RunCalibrationAsync(http.WritingV2UserId(), ct)))
            .WithName("WritingCalibrationRun");

        group.MapGet("/runs/latest", async (
            IWritingCalibrationService service,
            CancellationToken ct) =>
        {
            var run = await service.GetLatestRunAsync(ct);
            return run is null ? Results.NoContent() : Results.Ok(run);
        })
        .WithName("WritingCalibrationLatestRun");

        return app;
    }
}
