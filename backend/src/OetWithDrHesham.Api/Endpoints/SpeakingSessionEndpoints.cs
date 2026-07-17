using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Speaking;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// Phase 2 (B.3, D.1, F) of the OET Speaking module roadmap.
///
/// HTTP surface for the typed Speaking session lifecycle + AI assessment.
///
/// Routes:
///   * POST   /v1/speaking/sessions
///   * GET    /v1/speaking/sessions/{id}
///   * POST   /v1/speaking/sessions/{id}/start-roleplay
///   * POST   /v1/speaking/sessions/{id}/end
///   * POST   /v1/speaking/sessions/{id}/consent
///   * POST   /v1/speaking/sessions/{id}/ai-assess      (sync — runs the assessor)
///   * GET    /v1/speaking/sessions/{id}/ai-assessment   (returns latest persisted row)
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
            .WithSummary("Transition warm-up → prep. The only authorised exit from warm-up.")
            .Produces<SpeakingSessionDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        learner.MapPost("/{id}/start-roleplay", StartRolePlayAsync)
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
            .WithSummary("Synchronously score the session with the AI scorer (advisory).")
            .Produces<SpeakingAiAssessmentProjection>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        learner.MapGet("/{id}/ai-assessment", GetAiAssessmentAsync)
            .WithSummary("Get the latest persisted advisory AI assessment for the session.")
            .Produces<SpeakingAiAssessmentProjection>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        learner.MapGet("/{id}/transcript", GetTranscriptAsync)
            .WithSummary("Get the latest transcript revision for the caller's own session.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

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
        CancellationToken ct)
    {
        // Owner check via the session service first — returns NotFound if
        // the caller doesn't own the session. The assessor then loads
        // by id without re-checking ownership.
        var userId = ResolveUserId(http);
        _ = await sessions.GetSessionForLearnerAsync(userId, id, ct);
        var assessment = await assessor.RunAssessmentAsync(id, ct);
        return Results.Ok(assessment);
    }

    // ─────────────────────────────────────────────────────────────────
    // GET /v1/speaking/sessions/{id}/ai-assessment
    // ─────────────────────────────────────────────────────────────────
    private static async Task<IResult> GetAiAssessmentAsync(
        HttpContext http,
        string id,
        SpeakingSessionService sessions,
        SpeakingAiAssessmentService assessor,
        CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        _ = await sessions.GetSessionForLearnerAsync(userId, id, ct);
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
    // GET /v1/speaking/sessions/{id}/transcript
    // ─────────────────────────────────────────────────────────────────
    private static async Task<IResult> GetTranscriptAsync(
        HttpContext http,
        string id,
        LearnerDbContext db,
        SpeakingSessionService sessions,
        CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        _ = await sessions.GetSessionForLearnerAsync(userId, id, ct);

        var transcript = await db.SpeakingTranscripts.AsNoTracking()
            .Where(t => t.SpeakingSessionId == id && t.IsLatest)
            .OrderByDescending(t => t.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        if (transcript is null)
        {
            return Results.NotFound(new
            {
                errorCode = "speaking_transcript_not_found",
                message = "No transcript has been generated for this session yet.",
            });
        }

        return Results.Ok(new
        {
            transcriptId = transcript.Id,
            sessionId = transcript.SpeakingSessionId,
            provider = transcript.Provider,
            language = transcript.Language,
            wordCount = transcript.WordCount,
            meanConfidence = transcript.MeanConfidence,
            isLatest = transcript.IsLatest,
            generatedAt = transcript.GeneratedAt,
            segments = ParseSegments(transcript.SegmentsJson),
        });
    }

    private static JsonElement ParseSegments(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) json = "[]";
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch
        {
            using var fallback = JsonDocument.Parse("[]");
            return fallback.RootElement.Clone();
        }
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
