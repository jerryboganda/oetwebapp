using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Phase 2 (B.3, D.1, F) of the OET Speaking module roadmap.
///
/// HTTP surface for the typed Speaking session lifecycle + AI assessment.
///
/// Routes:
///   * POST   /v1/speaking/sessions
///   * GET    /v1/speaking/sessions/{id}
///   * POST   /v1/speaking/sessions/{id}/leave-queue     (leave the live AI session admission line)
///   * POST   /v1/speaking/sessions/{id}/start-roleplay
///   * POST   /v1/speaking/sessions/{id}/end
///   * POST   /v1/speaking/sessions/{id}/consent
///   * POST   /v1/speaking/sessions/{id}/ai-assess      (sync — runs the assessor; 202 while a fallback transcript is pending; re-runnable after a failure)
///   * GET    /v1/speaking/sessions/{id}/ai-assessment   (returns latest persisted row)
///   * GET    /v1/speaking/sessions/{id}/results         (assessmentState / retryable / failureReason)
///   * POST   /v1/speaking/sessions/{id}/recording       (recorder fallback upload, multipart field "audio")
///   * GET    /v1/speaking/sessions/{id}/transcript      (latest transcript snapshot)
///
/// All routes require the learner policy <c>LearnerOnly</c> and are
/// owner-checked at the service layer via the
/// <see cref="SpeakingSessionService"/> IDOR guard (returns NotFound for
/// non-owners so session ids don't leak).
///
/// NOTE: This file ships the extension method only. Program.cs wiring is
/// the responsibility of the integration agent.
/// </summary>
public static class SpeakingSessionEndpoints
{
    public static IEndpointRouteBuilder MapSpeakingSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/v1/speaking/sessions")
            .RequireAuthorization("LearnerOnly")
            .WithTags("Speaking sessions");

        learner.MapPost("", CreateAsync)
            .WithSummary("Create a typed Speaking session against a published role-play card.")
            .Produces<CreateSpeakingSessionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        learner.MapGet("/{id}", GetAsync)
            .WithSummary("Get the caller's own Speaking session (learner-safe card projection).")
            .Produces<SpeakingSessionDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        learner.MapPost("/{id}/start-warmup", StartWarmupAsync)
            .WithSummary("Mark the unscored warm-up conversation as started (Phase 3).")
            .Produces<SpeakingSessionDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        learner.MapPost("/{id}/finish-warmup", FinishWarmupAsync)
            .WithSummary("Transition warm-up → prep. The only authorised exit from warm-up. While the live AI session cap is full the session stays in warmup, nothing is held or timed, and the response carries `admission` (position, estimated wait): repeat the call.")
            .Produces<SpeakingSessionDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        learner.MapPost("/{id}/leave-queue", LeaveQueueAsync)
            .WithSummary("Leave the live AI session admission line: this card's waiting place is released at once (the card stays in warmup). A no-op when the card is not waiting.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        learner.MapPost("/{id}/start-roleplay", StartRolePlayAsync)
            .RequireRateLimiting("AiLiveSpeaking")
            .WithSummary("Transition the session from prep → active.")
            .Produces<SpeakingSessionDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        learner.MapPost("/{id}/end", EndAsync)
            .WithSummary("Transition the session from active → finished (snapshots elapsed time).")
            .Produces<SpeakingSessionDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        learner.MapPost("/{id}/submit", SubmitForMarkingAsync)
            .WithSummary("Submit the finished role-play for marking (WS4 two-recording gate, §14.2).")
            .Produces<SpeakingSessionDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        learner.MapPost("/{id}/consent", ConsentAsync)
            .WithSummary("Stamp the session with the consent version the learner accepted.")
            .Produces<SpeakingSessionDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        learner.MapPost("/{id}/ai-assess", AiAssessAsync)
            .RequireRateLimiting("AiScoring")
            .WithSummary("Score the session with the AI scorer. 202 {state:\"processing\"} while a recorder-fallback transcript is pending; call again to retry a failed assessment (never charged twice).")
            .Produces<SpeakingAiAssessmentProjection>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        learner.MapGet("/{id}/results", GetResultsAsync)
            .WithSummary("Learner-facing grading state: assessmentState (processing|completed|failed), retryable, failureReason, isFreeSample, cardId, usesV11, inputKind (recording|live_voice|null = nothing received yet). 404 only when the session is not the caller's.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        learner.MapPost("/{id}/recording", UploadRecordingAsync)
            .RequireRateLimiting("PerUserWrite")
            .WithSummary("Recorder fallback: upload the role-play recording (multipart field \"audio\", optional durationSeconds). 202 {status:\"received\"}; 409 recording_already_received on a repeat.")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        learner.MapGet("/{id}/ai-assessment", GetAiAssessmentAsync)
            .WithSummary("Get the latest persisted advisory AI assessment for the session.")
            .Produces<SpeakingAiAssessmentProjection>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        // GET /{id}/transcript is served by SpeakingTranscriptionEndpoints; a second
        // mapping here made every transcript read fail with AmbiguousMatchException.

        learner.MapGet("/{id}/clock", GetClockAsync)
            .WithSummary("Authoritative server-computed session clock (WS1, §1.2/§22.5).")
            .Produces<SpeakingSessionClock>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        learner.MapPost("/{id}/technical-issue", ReportTechnicalIssueAsync)
            .WithSummary("Flag a technical issue on the session (§22.5); never affects scoring.")
            .Produces<SpeakingSessionDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    // ─────────────────────────────────────────────────────────────────
    // POST /v1/speaking/sessions
    // ─────────────────────────────────────────────────────────────────
    private static async Task<IResult> CreateAsync(
        HttpContext http,
        [FromBody] CreateSpeakingSessionRequest body,
        SpeakingSessionService sessions,
        CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        var resp = await sessions.CreateSessionAsync(userId, body, ct);
        return Results.Ok(resp);
    }

    // ─────────────────────────────────────────────────────────────────
    // GET /v1/speaking/sessions/{id}
    // ─────────────────────────────────────────────────────────────────
    private static async Task<IResult> GetAsync(
        HttpContext http,
        string id,
        SpeakingSessionService sessions,
        CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        var detail = await sessions.GetSessionForLearnerAsync(userId, id, ct);
        return Results.Ok(detail);
    }

    // ─────────────────────────────────────────────────────────────────
    // POST /v1/speaking/sessions/{id}/start-warmup
    // ─────────────────────────────────────────────────────────────────
    private static async Task<IResult> StartWarmupAsync(
        HttpContext http,
        string id,
        SpeakingSessionService sessions,
        CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        var detail = await sessions.StartWarmupAsync(userId, id, ct);
        return Results.Ok(detail);
    }

    // ─────────────────────────────────────────────────────────────────
    // POST /v1/speaking/sessions/{id}/finish-warmup
    // ─────────────────────────────────────────────────────────────────
    private static async Task<IResult> FinishWarmupAsync(
        HttpContext http,
        string id,
        SpeakingSessionService sessions,
        CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        var detail = await sessions.FinishWarmupAsync(userId, id, ct);
        return Results.Ok(detail);
    }

    // ─────────────────────────────────────────────────────────────────
    // POST /v1/speaking/sessions/{id}/leave-queue
    // ─────────────────────────────────────────────────────────────────
    private static async Task<IResult> LeaveQueueAsync(
        HttpContext http,
        string id,
        SpeakingSessionService sessions,
        CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        await sessions.LeaveAdmissionQueueAsync(userId, id, ct);
        return Results.NoContent();
    }

    // ─────────────────────────────────────────────────────────────────
    // POST /v1/speaking/sessions/{id}/start-roleplay
    // ─────────────────────────────────────────────────────────────────
    private static async Task<IResult> StartRolePlayAsync(
        HttpContext http,
        string id,
        SpeakingSessionService sessions,
        CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        var detail = await sessions.StartRolePlayAsync(userId, id, ct);
        return Results.Ok(detail);
    }

    // ─────────────────────────────────────────────────────────────────
    // POST /v1/speaking/sessions/{id}/end
    // ─────────────────────────────────────────────────────────────────
    private static async Task<IResult> EndAsync(
        HttpContext http,
        string id,
        SpeakingSessionService sessions,
        CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        var detail = await sessions.EndSessionAsync(userId, id, ct);
        return Results.Ok(detail);
    }

    // ─────────────────────────────────────────────────────────────────
    // POST /v1/speaking/sessions/{id}/submit
    // ─────────────────────────────────────────────────────────────────
    private static async Task<IResult> SubmitForMarkingAsync(
        HttpContext http,
        string id,
        SpeakingSessionService sessions,
        CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        var detail = await sessions.SubmitForMarkingAsync(userId, id, ct);
        return Results.Ok(detail);
    }

    // ─────────────────────────────────────────────────────────────────
    // POST /v1/speaking/sessions/{id}/consent
    // ─────────────────────────────────────────────────────────────────
    private static async Task<IResult> ConsentAsync(
        HttpContext http,
        string id,
        [FromBody] SpeakingConsentRequest body,
        SpeakingSessionService sessions,
        CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        if (body is null)
        {
            throw ApiException.Validation("CONSENT_VERSION_REQUIRED",
                "Consent version is required.");
        }
        var detail = await sessions.MarkConsentAsync(userId, id, body.ConsentVersion, ct);
        return Results.Ok(detail);
    }

    // ─────────────────────────────────────────────────────────────────
    // POST /v1/speaking/sessions/{id}/ai-assess
    // ─────────────────────────────────────────────────────────────────
    private static async Task<IResult> AiAssessAsync(
        HttpContext http,
        string id,
        SpeakingSessionService sessions,
        SpeakingAiAssessmentService assessor,
        SpeakingSimulationV11AssessmentService v11Assessor,
        SpeakingSessionRecordingService recordings,
        CancellationToken ct)
    {
        // Owner check via the session service first — returns NotFound if
        // the caller doesn't own the session. The assessor then loads
        // by id without re-checking ownership.
        var userId = ResolveUserId(http);
        _ = await sessions.GetSessionForLearnerAsync(userId, id, ct);
        var canonical = http.RequestServices.GetRequiredService<ISpeakingCanonicalAssessmentService>();
        if (await recordings.DeferAssessmentUntilTranscribedAsync(id, canonical, ct))
        {
            // Recorder fallback: the assessment runs automatically as soon as
            // the uploaded recording is transcribed.
            return Results.Accepted(value: new { state = SpeakingAssessmentState.Processing });
        }
        // Max-reasoning grading can take minutes; a proxy/browser timeout must
        // not cancel a grade that is already running (the page keeps polling).
        await canonical.AssessNowAsync(id, CancellationToken.None);
        if (await canonical.UsesV11Async(id, ct))
        {
            var v11Latest = await v11Assessor.GetLatestAsync(id, ct);
            return v11Latest is null ? Results.Accepted() : Results.Ok(v11Latest);
        }

        var assessment = await assessor.GetLatestAsync(id, ct);
        return assessment is null ? Results.Accepted() : Results.Ok(assessment);
    }

    // ─────────────────────────────────────────────────────────────────
    // GET /v1/speaking/sessions/{id}/ai-assessment
    // ─────────────────────────────────────────────────────────────────
    private static async Task<IResult> GetAiAssessmentAsync(
        HttpContext http,
        string id,
        SpeakingSessionService sessions,
        SpeakingAiAssessmentService assessor,
        SpeakingSimulationV11AssessmentService v11Assessor,
        CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        _ = await sessions.GetSessionForLearnerAsync(userId, id, ct);
        var canonical = http.RequestServices.GetRequiredService<ISpeakingCanonicalAssessmentService>();
        if (await canonical.UsesV11Async(id, ct))
        {
            var v11Latest = await v11Assessor.GetLatestAsync(id, ct);
            if (v11Latest is null)
            {
                return Results.NotFound(new
                {
                    errorCode = "speaking_v11_assessment_not_found",
                    message = "No v1.1 assessment has been generated for this session yet.",
                });
            }
            return Results.Ok(v11Latest);
        }

        var latest = await assessor.GetLatestAsync(id, ct);
        if (latest is null)
        {
            return Results.NotFound(new
            {
                errorCode = "speaking_ai_assessment_not_found",
                message = "No AI assessment has been generated for this session yet.",
            });
        }
        return Results.Ok(latest);
    }

    // ─────────────────────────────────────────────────────────────────
    // GET /v1/speaking/sessions/{id}/results
    // ─────────────────────────────────────────────────────────────────
    private static async Task<IResult> GetResultsAsync(
        HttpContext http,
        string id,
        SpeakingSessionService sessions,
        LearnerDbContext db,
        CancellationToken ct)
    {
        // Lean status payload: the page loads the detailed assessment from
        // /ai-assessment, /transcript and the v1.1 endpoints. "No assessment
        // yet" is assessmentState=processing, never a 404.
        var userId = ResolveUserId(http);
        var session = await sessions.GetSessionForLearnerAsync(userId, id, ct);
        var canonical = http.RequestServices.GetRequiredService<ISpeakingCanonicalAssessmentService>();
        var state = await canonical.GetStateAsync(id, ct);
        var isFreeSample = session.IsFreeSample;
        return Results.Ok(new
        {
            sessionId = id,
            assessmentState = state.AssessmentState,
            retryable = state.Retryable,
            failureReason = state.FailureReason,
            isFreeSample,
            cardId = session.RolePlayCardId,
            // Pages call the v1.1 report endpoints only when true; otherwise
            // they 404 on every poll.
            usesV11 = await canonical.UsesV11Async(id, ct),
            // What the learner handed in, so the pages do not describe a live conversation as a full-session recording.
            inputKind = await ReadInputKindAsync(db, id, ct),
        });
    }

    /// <summary>
    /// "live_voice" when the latest transcript or a saved microphone clip came from the live
    /// voice flow; otherwise "recording" when role-play audio was uploaded for recorder fallback or
    /// tutor egress, archived or not; otherwise null. The unscored warm-up recording never counts.
    /// </summary>
    private static async Task<string?> ReadInputKindAsync(LearnerDbContext db, string sessionId, CancellationToken ct)
    {
        var liveTranscript = await db.SpeakingTranscripts.AsNoTracking()
            .AnyAsync(t => t.SpeakingSessionId == sessionId
                && t.IsLatest
                && t.Provider.StartsWith(LiveVoiceService.TranscriptProviderPrefix), ct);
        if (liveTranscript)
        {
            return "live_voice";
        }

        var liveCandidateAudio = await db.SpeakingRecordings.AsNoTracking()
            .AnyAsync(r => r.SpeakingSessionId == sessionId
                && !r.IsWarmup
                && r.Source == SpeakingRecordingSource.ConversationHub, ct);
        if (liveCandidateAudio)
        {
            return "live_voice";
        }

        if (await db.SpeakingRecordings.AsNoTracking()
                .AnyAsync(r => r.SpeakingSessionId == sessionId && !r.IsWarmup, ct))
        {
            return "recording";
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────
    // POST /v1/speaking/sessions/{id}/recording
    // ─────────────────────────────────────────────────────────────────
    private static async Task<IResult> UploadRecordingAsync(
        HttpContext http,
        string id,
        SpeakingSessionRecordingService recordings,
        CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        // ReadFormAsync (not IFormFile binding) keeps this learner upload off
        // the antiforgery requirement, like the other upload endpoints.
        if (!http.Request.HasFormContentType)
        {
            throw ApiException.Validation("speaking_recording_multipart_required",
                "Upload the recording as multipart/form-data with an \"audio\" field.");
        }

        var form = await http.Request.ReadFormAsync(ct);
        var audio = form.Files.GetFile("audio")
            ?? throw ApiException.Validation("empty_audio_upload", "Attach the recording in the \"audio\" field.");
        int? durationSeconds = int.TryParse(form["durationSeconds"].ToString(), out var parsed) ? parsed : null;

        await using var stream = audio.OpenReadStream();
        var stored = await recordings.ReceiveAsync(
            userId, id, stream, audio.ContentType, audio.Length, durationSeconds, ct);
        if (!stored)
        {
            // The client treats this as success: the first upload is kept.
            throw ApiException.Conflict("recording_already_received",
                "A recording has already been received for this session.");
        }

        return Results.Accepted(value: new { status = "received" });
    }

    // ─────────────────────────────────────────────────────────────────
    // GET /v1/speaking/sessions/{id}/clock
    // ─────────────────────────────────────────────────────────────────
    private static async Task<IResult> GetClockAsync(
        HttpContext http,
        string id,
        SpeakingSessionService sessions,
        CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        var clock = await sessions.GetClockAsync(userId, id, ct);
        return Results.Ok(clock);
    }

    // ─────────────────────────────────────────────────────────────────
    // POST /v1/speaking/sessions/{id}/technical-issue
    // ─────────────────────────────────────────────────────────────────
    private static async Task<IResult> ReportTechnicalIssueAsync(
        HttpContext http,
        string id,
        [FromBody] SpeakingTechnicalIssueRequest? body,
        SpeakingSessionService sessions,
        CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        var detail = await sessions.ReportTechnicalIssueAsync(userId, id, body?.Note, ct);
        return Results.Ok(detail);
    }

    private static string ResolveUserId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw ApiException.Unauthorized("speaking_session_unauthenticated",
               "You must be signed in to interact with a Speaking session.");
}
