using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Pronunciation;

namespace OetLearner.Api.Endpoints;

public static class PronunciationEndpoints
{
    public static IEndpointRouteBuilder MapPronunciationEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");
        var pron = v1.MapGroup("/pronunciation");

        pron.MapGet("/profile", async (HttpContext http, PronunciationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetProfileAsync(http.UserId(), ct)));

        pron.MapGet("/my-progress", async (HttpContext http, PronunciationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetMyProgressAsync(http.UserId(), ct)));

        pron.MapGet("/entitlement", async (HttpContext http, PronunciationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetEntitlementAsync(http.UserId(), ct)));

        pron.MapGet("/drills", async (
            [FromQuery] string? profession,
            [FromQuery] string? difficulty,
            [FromQuery] string? focus,
            PronunciationService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.GetDrillsAsync(profession, difficulty, focus, ct)));

        pron.MapGet("/drills/due", async (
            HttpContext http,
            [FromQuery] int? limit,
            PronunciationService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.GetDueDrillsAsync(http.UserId(), limit ?? 6, ct)));

        pron.MapGet("/drills/{drillId}", async (string drillId, PronunciationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetDrillAsync(drillId, ct)));

        // Initialise an attempt (checks entitlement, allocates an attempt id).
        pron.MapPost("/drills/{drillId}/attempt/init", async (
            string drillId,
            HttpContext http,
            PronunciationService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.InitAttemptAsync(http.UserId(), drillId, ct)));

        // Upload the audio blob for an attempt and synchronously score it.
        pron.MapPost("/drills/{drillId}/attempt/{attemptId}/audio", async (
            string drillId,
            string attemptId,
            HttpContext http,
            PronunciationService svc,
            CancellationToken ct) =>
        {
            if (!http.Request.HasFormContentType)
            {
                // Raw-body upload path — use Content-Type header + body stream
                var mime = http.Request.ContentType ?? "application/octet-stream";
                var duration = TryParseInt(http.Request.Headers["X-Audio-Duration-Ms"]);
                var result = await svc.UploadAndScoreAsync(
                    http.UserId(), drillId, attemptId,
                    http.Request.Body, mime,
                    http.Request.ContentLength, duration, ct);
                return Results.Ok(result);
            }

            var form = await http.Request.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null)
                throw ApiException.Validation("AUDIO_MISSING", "Form upload must include an 'audio' file.");

            var durationForm = TryParseInt(form["durationMs"].ToString());
            await using var stream = file.OpenReadStream();
            var fr = await svc.UploadAndScoreAsync(
                http.UserId(), drillId, attemptId,
                stream, file.ContentType ?? "application/octet-stream",
                file.Length, durationForm, ct);
            return Results.Ok(fr);
        }).DisableAntiforgery();

        // Back-compat alias: POST /drills/{id}/attempt  { audioUrl } — now rejects
        // with a clear upgrade notice rather than pretending to score.
        pron.MapPost("/drills/{drillId}/attempt", (string drillId) =>
            Results.Problem(
                title: "Use /attempt/init + /attempt/{attemptId}/audio",
                detail: "The legacy JSON-body attempt endpoint has been retired. Call POST /attempt/init then upload the audio blob to /attempt/{attemptId}/audio.",
                statusCode: StatusCodes.Status410Gone));

        pron.MapGet("/assessment/{assessmentId}", async (
            string assessmentId, HttpContext http, PronunciationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAssessmentAsync(http.UserId(), assessmentId, ct)));

        pron.MapGet("/speaking-linked", async (
            HttpContext http, [FromQuery] int? limit, PronunciationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetSpeakingLinkedAssessmentsAsync(http.UserId(), limit ?? 20, ct)));

        // Minimal-pair listening discrimination: learner submits N rounds of
        // "which did you hear — A or B?"  We persist aggregate accuracy.
        pron.MapPost("/drills/{drillId}/discrimination", async (
            string drillId,
            HttpContext http,
            DiscriminationSubmitRequest request,
            PronunciationService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.SubmitDiscriminationAsync(
                http.UserId(), drillId, request.RoundsTotal, request.RoundsCorrect, ct)));

        return app;
    }

    private static int? TryParseInt(string? s)
        => int.TryParse(s, out var v) ? v : null;
}

public record DiscriminationSubmitRequest(int RoundsTotal, int RoundsCorrect);

file static class PronunciationHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
