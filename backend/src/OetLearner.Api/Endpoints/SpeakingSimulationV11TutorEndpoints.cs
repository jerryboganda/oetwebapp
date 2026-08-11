using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// v1.1 human-review routes. Expert access is still assignment/claim checked
/// by the service; learners receive only the latest human revision and never
/// receive tutor identity or the immutable original-report snapshot.
/// </summary>
public static class SpeakingSimulationV11TutorEndpoints
{
    public static IEndpointRouteBuilder MapSpeakingSimulationV11TutorEndpoints(
        this IEndpointRouteBuilder app)
    {
        var expert = app.MapGroup("/v1/expert/speaking/simulation-v1.1")
            .RequireAuthorization("ExpertOnly")
            .WithTags("Speaking simulation v1.1 tutor review");

        expert.MapPost("/sessions/{id}/overrides", async (
            HttpContext http,
            string id,
            SpeakingSimulationV11TutorOverrideRequest request,
            SpeakingSimulationV11TutorOverrideService service,
            CancellationToken ct) =>
        {
            var response = await service.CreateAsync(ResolveUserId(http), id, request, ct);
            return Results.Created(
                $"/v1/expert/speaking/simulation-v1.1/sessions/{id}/overrides/{response.OverrideId}",
                response);
        })
            .WithSummary("Record an immutable human revision of a completed v1.1 report.")
            .Produces<SpeakingSimulationV11TutorOverrideResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        expert.MapGet("/sessions/{id}/overrides", async (
            HttpContext http,
            string id,
            SpeakingSimulationV11TutorOverrideService service,
            CancellationToken ct) => Results.Ok(
                await service.GetForTutorAsync(ResolveUserId(http), id, ct)))
            .WithSummary("List the immutable v1.1 human revisions for an assigned session.")
            .Produces<IReadOnlyList<SpeakingSimulationV11TutorOverrideResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden);

        expert.MapGet("/sessions/{id}/audio/{recordingId}", async (
            HttpContext http,
            string id,
            string recordingId,
            TutorAssessmentService tutorAssessment,
            LearnerDbContext db,
            IFileStorage storage,
            CancellationToken ct) =>
        {
            await tutorAssessment.GetSessionContextForTutorAsync(ResolveUserId(http), id, ct);
            var recording = await db.SpeakingRecordings
                .AsNoTracking()
                .Include(x => x.MediaAsset)
                .FirstOrDefaultAsync(x => x.Id == recordingId
                    && x.SpeakingSessionId == id
                    && !x.IsArchived,
                    ct);
            if (recording?.MediaAsset is null
                || string.IsNullOrWhiteSpace(recording.MediaAsset.StoragePath))
            {
                return Results.NotFound(new
                {
                    errorCode = "speaking_v11_tutor_audio_not_found",
                    message = "That original audio turn is not available.",
                });
            }

            var media = recording.MediaAsset;
            var stream = await storage.OpenReadAsync(media.StoragePath, ct);
            return Results.File(stream, media.MimeType, enableRangeProcessing: true);
        })
            .WithSummary("Stream an assigned session's original learner audio for evidence review.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        var learner = app.MapGroup("/v1/speaking/sessions")
            .RequireAuthorization("LearnerOnly")
            .WithTags("Speaking simulation v1.1");

        learner.MapGet("/{id}/v1.1-tutor-override", async (
            HttpContext http,
            string id,
            SpeakingSimulationV11TutorOverrideService service,
            CancellationToken ct) =>
        {
            var response = await service.GetForLearnerAsync(ResolveUserId(http), id, ct);
            return response is null
                ? Results.NotFound(new
                {
                    errorCode = "speaking_v11_tutor_override_not_found",
                    message = "No human v1.1 revision has been recorded for this session.",
                })
                : Results.Ok(response);
        })
            .WithSummary("Get the latest human v1.1 revision without tutor identity or AI snapshot data.")
            .Produces<SpeakingSimulationV11LearnerTutorOverrideResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static string ResolveUserId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw ApiException.Unauthorized(
                "speaking_v11_unauthenticated",
                "You must be signed in to access this Speaking review.");
}
