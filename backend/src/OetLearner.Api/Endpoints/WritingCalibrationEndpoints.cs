using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin endpoints for the 50-letter calibration harness (spec §33).
/// Three GETs + one POST + one runner action under
/// <c>/v1/admin/writing/calibration</c>. Routes layer granular content
/// permissions on top of the group-level <c>AdminOnly</c> gate — reads use
/// <c>AdminContentRead</c>, writes use <c>AdminContentWrite</c> with the
/// per-user write rate-limit bucket — mirroring the sibling writing-admin
/// surfaces (e.g. <see cref="WritingTaskAdminEndpoints"/>).
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
            .WithName("WritingCalibrationListLetters")
            .WithAdminRead("AdminContentRead");

        group.MapPost("/letters", async (
            [FromBody] WritingCalibrationLetterCreateRequest request,
            HttpContext http,
            IWritingCalibrationService service,
            CancellationToken ct)
            => Results.Ok(await service.AddCalibrationLetterAsync(http.WritingV2UserId(), request, ct)))
            .WithName("WritingCalibrationAddLetter")
            .WithAdminWrite("AdminContentWrite");

        group.MapPost("/run", async (
            HttpContext http,
            IWritingCalibrationService service,
            CancellationToken ct)
            => Results.Ok(await service.RunCalibrationAsync(http.WritingV2UserId(), ct)))
            .WithName("WritingCalibrationRun")
            .WithAdminWrite("AdminContentWrite");

        group.MapGet("/runs/latest", async (
            IWritingCalibrationService service,
            CancellationToken ct) =>
        {
            var run = await service.GetLatestRunAsync(ct);
            return run is null ? Results.NoContent() : Results.Ok(run);
        })
        .WithName("WritingCalibrationLatestRun")
        .WithAdminRead("AdminContentRead");

        return app;
    }
}
