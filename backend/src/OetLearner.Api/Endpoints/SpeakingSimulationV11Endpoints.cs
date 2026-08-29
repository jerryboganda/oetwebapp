using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Endpoints;

/// <summary>Learner-only v1.1 assessment and report routes. Legacy
/// <c>/ai-assess</c> remains available for pre-v1.1 sessions; these routes
/// are explicit so a report cannot be mistaken for an official OET result.</summary>
public static class SpeakingSimulationV11Endpoints
{
    public static IEndpointRouteBuilder MapSpeakingSimulationV11Endpoints(
        this IEndpointRouteBuilder app)
    {
        var sessions = app.MapGroup("/v1/speaking/sessions")
            .RequireAuthorization("LearnerOnly")
            .WithTags("Speaking simulation v1.1");

        sessions.MapPost("/{id}/v1.1-assess", AssessSessionAsync)
            .WithSummary("Generate the released ten-criterion v1.1 practice assessment.")
            .Produces<SpeakingSimulationV11AssessmentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        sessions.MapGet("/{id}/v1.1-assessment", GetSessionAssessmentAsync)
            .WithSummary("Get the latest v1.1 card report, including source-linked evidence.")
            .Produces<SpeakingSimulationV11AssessmentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        sessions.MapGet("/{id}/v1.1-audio/{recordingId}", GetLearnerAudioAsync)
            .WithSummary("Stream one authenticated original learner-audio turn for report playback.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        var exams = app.MapGroup("/v1/speaking/exams")
            .RequireAuthorization("LearnerOnly")
            .WithTags("Speaking simulation v1.1");

        exams.MapPost("/{id}/v1.1-combined-assess", AssessCombinedAsync)
            .WithSummary("Derive the combined v1.1 report only when both card reports are valid.")
            .Produces<SpeakingSimulationV11AssessmentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        exams.MapGet("/{id}/v1.1-combined-report", GetCombinedAsync)
            .WithSummary("Get the latest combined v1.1 practice report.")
            .Produces<SpeakingSimulationV11AssessmentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> AssessSessionAsync(
        HttpContext http,
        string id,
        SpeakingSessionService sessions,
        SpeakingSimulationV11AssessmentService assessor,
        CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        _ = await sessions.GetSessionForLearnerAsync(userId, id, ct);
        var canonical = http.RequestServices.GetRequiredService<ISpeakingCanonicalAssessmentService>();
        await canonical.AssessNowAsync(id, ct);
        var latest = await assessor.GetLatestAsync(id, ct);
        return latest is null ? Results.Accepted() : Results.Ok(latest);
    }

    private static async Task<IResult> GetSessionAssessmentAsync(
        HttpContext http,
        string id,
        SpeakingSessionService sessions,
        SpeakingSimulationV11AssessmentService assessor,
        CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        _ = await sessions.GetSessionForLearnerAsync(userId, id, ct);
        var report = await assessor.GetLatestAsync(id, ct);
        return report is null
            ? Results.NotFound(new
            {
                errorCode = "speaking_v11_assessment_not_found",
                message = "No v1.1 assessment has been generated for this session yet.",
            })
            : Results.Ok(report);
    }

    private static async Task<IResult> AssessCombinedAsync(
        HttpContext http,
        string id,
        SpeakingExamService exams,
        SpeakingSimulationV11AssessmentService assessor,
        CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        _ = await exams.GetExamForLearnerAsync(userId, id, ct);
        return Results.Ok(await assessor.RunCombinedAssessmentAsync(id, ct));
    }

    private static async Task<IResult> GetLearnerAudioAsync(
        HttpContext http,
        string id,
        string recordingId,
        LearnerDbContext db,
        IFileStorage storage,
        CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        var recording = await db.SpeakingRecordings
            .AsNoTracking()
            .Include(x => x.MediaAsset)
            .Join(
                db.SpeakingSessions.AsNoTracking(),
                recording => recording.SpeakingSessionId,
                session => session.Id,
                (recording, session) => new { recording, session })
            .FirstOrDefaultAsync(x => x.recording.Id == recordingId
                && x.recording.SpeakingSessionId == id
                && x.session.UserId == userId
                && !x.recording.IsArchived,
                ct);

        if (recording?.recording.MediaAsset is null
            || string.IsNullOrWhiteSpace(recording.recording.MediaAsset.StoragePath))
        {
            return Results.NotFound(new
            {
                errorCode = "speaking_v11_audio_not_found",
                message = "That original audio turn is not available.",
            });
        }

        var media = recording.recording.MediaAsset;
        var stream = await storage.OpenReadAsync(media.StoragePath, ct);
        return Results.File(stream, media.MimeType, enableRangeProcessing: true);
    }

    private static async Task<IResult> GetCombinedAsync(
        HttpContext http,
        string id,
        SpeakingExamService exams,
        SpeakingSimulationV11AssessmentService assessor,
        CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        _ = await exams.GetExamForLearnerAsync(userId, id, ct);
        var report = await assessor.GetLatestCombinedAsync(id, ct);
        return report is null
            ? Results.NotFound(new
            {
                errorCode = "speaking_v11_combined_report_not_found",
                message = "No combined v1.1 report exists for this exam yet.",
            })
            : Results.Ok(report);
    }

    private static string ResolveUserId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw ApiException.Unauthorized(
                "speaking_v11_unauthenticated",
                "You must be signed in to access a Speaking simulation report.");
}
