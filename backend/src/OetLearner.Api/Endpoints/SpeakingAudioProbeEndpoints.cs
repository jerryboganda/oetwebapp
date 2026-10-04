using System.Security.Claims;
using System.Text.Json;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// The release gate for the Speaking audio judge (owner spec 4 Oct 2026). An admin sends one clip and the phrase that
/// was really said in it; the endpoint runs the stage's own pipeline (ffmpeg, one mp3, the pinned OpenAI audio model)
/// and reports what the model heard, how closely that matches the phrase, and the Intelligibility it judged. The
/// stage stays behind the <c>speaking_audio_assessment</c> flag until a probe has passed
/// (<c>docs/speaking/scoring.md</c>). Only numbers are audited, never audio.
/// </summary>
public static class SpeakingAudioProbeEndpoints
{
    private const long MaxClipBytes = 8 * 1024 * 1024;

    public static IEndpointRouteBuilder MapSpeakingAudioProbeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/speaking/audio-assess")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser")
            .WithTags("Speaking grader calibration");

        group.MapPost("/probe", ProbeAsync)
            .DisableAntiforgery()
            .WithName("SpeakingAudioAssessProbe")
            .WithSummary("Judge one uploaded clip with the audio stage and report what the model heard (release probe).")
            .WithAdminWrite("AdminContentWrite");

        return app;
    }

    private static async Task<IResult> ProbeAsync(
        HttpContext http,
        ISpeakingAudioEvidenceService audio,
        LearnerDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        // ReadFormAsync (not IFormFile binding) keeps this off the antiforgery requirement, like the other uploads.
        if (!http.Request.HasFormContentType)
        {
            throw ApiException.Validation("speaking_audio_probe_multipart_required",
                "Send multipart/form-data with an \"audio\" file and the \"phrase\" that was spoken in it.");
        }

        var form = await http.Request.ReadFormAsync(ct);
        var clip = form.Files.GetFile("audio")
            ?? throw ApiException.Validation("speaking_audio_probe_audio_required", "Attach the clip in the \"audio\" field.");
        var phrase = form["phrase"].ToString().Trim();
        if (phrase.Length is 0 or > 400)
        {
            throw ApiException.Validation("speaking_audio_probe_phrase_required", "Give the phrase that was spoken (1-400 characters).");
        }

        if (clip.Length is 0 or > MaxClipBytes)
        {
            throw ApiException.Validation("speaking_audio_probe_clip_size", "The clip must be between 1 byte and 8 MB.");
        }

        SpeakingAudioProbeResult result;
        try
        {
            await using var stream = clip.OpenReadStream();
            result = await audio.ProbeAsync(stream, clip.ContentType, phrase, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Results.Json(new { status = "error", error = "timeout", message = "The audio judge did not answer in time." },
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A diagnostic: the admin needs to see what failed (ffmpeg missing, the provider refusing the audio, ...).
            var message = ex.Message.Length <= 500 ? ex.Message : ex.Message[..500];
            return Results.Json(new { status = "error", error = ex.GetType().Name, message },
                statusCode: StatusCodes.Status502BadGateway);
        }

        var evidence = result.Evidence;
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            OccurredAt = clock.GetUtcNow(),
            ActorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("Authenticated admin id is required."),
            ActorName = http.User.FindFirstValue(ClaimTypes.Name) ?? http.User.Identity?.Name ?? "admin",
            Action = "SpeakingAudioProbeRun",
            ResourceType = "SpeakingAudioStage",
            ResourceId = SpeakingAudioAssessmentOptions.FeatureFlagKey,
            Details = JsonSerializer.Serialize(new
            {
                status = evidence.Status,
                reason = evidence.Reason,
                openingSimilarity = Math.Round(result.OpeningSimilarity, 2),
                intelligibility = evidence.IntelligibilityScore,
                observations = evidence.Observations.Count,
                model = evidence.Model,
                durationMs = result.DurationMs,
                latencyMs = result.LatencyMs,
            }),
        });
        await db.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            status = evidence.Status,
            reason = evidence.Reason,
            reasonText = evidence.IsAudio ? null : SpeakingAudioEvidenceService.ReasonText(evidence.Reason),
            heardOpening = result.HeardOpening,
            openingSimilarity = Math.Round(result.OpeningSimilarity, 3),
            intelligibilityScore = evidence.IntelligibilityScore,
            intelligibilityRationale = evidence.IntelligibilityRationale,
            observations = evidence.Observations,
            audioQuality = evidence.AudioQuality,
            confidence = evidence.Confidence,
            patientVoiceBleed = evidence.PatientVoiceBleed,
            fluency = evidence.Fluency,
            model = evidence.Model,
            durationMs = result.DurationMs,
            latencyMs = result.LatencyMs,
        });
    }
}
