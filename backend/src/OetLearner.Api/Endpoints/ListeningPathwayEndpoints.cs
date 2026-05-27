using System.Security.Claims;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Endpoints;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Module Pathway — Phase 1 endpoints (A7)
//
// Surfaces the 5-stage learner flow described in OET_LISTENING_MODULE_PATHWAY.md
// §5–§6 by composing IListeningLearnerPathwayService (A6) with the existing
// IListeningSkillScoringService (A2). All routes mount under
// /v1/listening-pathway and require the LearnerOnly auth policy + the PerUser
// rate-limit partition.
//
// Mirrors ReadingPathwayEndpoints.cs in shape and convention so that audits
// against the Reading pathway can be applied here without translation. Key
// differences from Reading:
//   • Includes the dedicated /audio-check route (the listening flow blocks
//     diagnostic start on audio confirmation).
//   • The diagnostic question fetch derives a base URL from the request so the
//     pathway service can stitch absolute audio playback links.
//   • Cross-user access surfaces as 404, never 403 — the service throws
//     InvalidOperationException("Session not found") for foreign GUIDs so
//     callers cannot probe for the existence of other learners' sessions.
//   • Idempotent diagnostic submission: re-POSTing /diagnostic/submit returns
//     the cached result rather than re-grading.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Maps the OET Listening Module Pathway (Phase 1) endpoints under
/// <c>/v1/listening-pathway</c>. Composes the pathway service (A6) and the
/// skill-scoring service (A2) into a learner-facing HTTP surface.
/// </summary>
public static class ListeningPathwayEndpoints
{
    /// <summary>
    /// Registers all listening pathway routes on the supplied
    /// <paramref name="app"/>. Routes are protected by the
    /// <c>LearnerOnly</c> authorization policy and the <c>PerUser</c>
    /// rate-limit partition.
    /// </summary>
    public static IEndpointRouteBuilder MapListeningPathwayEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/listening-pathway")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        // ─────────────────────────────────────────────────────────────────
        // §5.2 Profile & stage
        // ─────────────────────────────────────────────────────────────────

        group.MapGet("/profile", async (
            HttpContext http,
            IListeningLearnerPathwayService svc,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            try
            {
                var profile = await svc.GetProfileAsync(userId, ct);
                return Results.Ok(profile);
            }
            catch (InvalidOperationException ex) when (IsNotFound(ex))
            {
                // Onboarding hasn't happened yet — the frontend renders the
                // intake form rather than a profile card.
                return Results.NotFound(new
                {
                    code = "listening_profile_not_found",
                    error = ex.Message,
                    message = ex.Message,
                });
            }
        })
        .WithName("ListeningGetProfile");

        group.MapPost("/onboarding", async (
            ListeningStartOnboardingRequest request,
            HttpContext http,
            IListeningLearnerPathwayService svc,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            try
            {
                var profile = await svc.StartOnboardingAsync(userId, request, ct);
                return Results.Ok(profile);
            }
            catch (ArgumentException ex)
            {
                return BadRequest("invalid_onboarding_request", ex.Message);
            }
        })
        .WithName("ListeningStartOnboarding");

        // ─────────────────────────────────────────────────────────────────
        // §5.4 Audio check — must pass before diagnostic
        // ─────────────────────────────────────────────────────────────────

        group.MapPost("/audio-check", async (
            AudioCheckRequest request,
            HttpContext http,
            IListeningLearnerPathwayService svc,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            try
            {
                var result = await svc.SubmitAudioCheckAsync(userId, request, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex) when (IsNotFound(ex))
            {
                // Missing profile — learner attempted audio-check before
                // onboarding. Surface as 404 so the frontend redirects
                // them back to the intake form.
                return Results.NotFound(new
                {
                    code = "listening_profile_not_found",
                    error = ex.Message,
                    message = ex.Message,
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest("invalid_audio_check_request", ex.Message);
            }
        })
        .WithName("ListeningSubmitAudioCheck");

        // ─────────────────────────────────────────────────────────────────
        // §6.1 Diagnostic — start
        // ─────────────────────────────────────────────────────────────────

        group.MapPost("/diagnostic/start", async (
            HttpContext http,
            IListeningLearnerPathwayService svc,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            try
            {
                var result = await svc.StartDiagnosticAsync(userId, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex) when (IsNotFound(ex))
            {
                return Results.NotFound(new
                {
                    code = "listening_profile_not_found",
                    error = ex.Message,
                    message = ex.Message,
                });
            }
            catch (InvalidOperationException ex)
            {
                // Stage gate failure (e.g. learner still on audio_check) or
                // a deployment missing the seeded diagnostic paper. Both are
                // 400-class precondition failures rather than 404s.
                return BadRequest("diagnostic_start_failed", ex.Message);
            }
        })
        .WithName("ListeningStartDiagnostic");

        // ─────────────────────────────────────────────────────────────────
        // §6.2 Diagnostic — fetch question pool (learner-safe projection)
        // ─────────────────────────────────────────────────────────────────

        group.MapGet("/diagnostic/sessions/{sessionId:guid}/questions", async (
            Guid sessionId,
            HttpContext http,
            IListeningLearnerPathwayService svc,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var baseUrl = BuildBaseUrl(http);
            try
            {
                var questions = await svc.GetDiagnosticQuestionsAsync(userId, sessionId, baseUrl, ct);
                return Results.Ok(questions);
            }
            catch (InvalidOperationException ex) when (IsNotFound(ex))
            {
                // Cross-user GUID-probe protection: foreign sessions look
                // identical to missing ones.
                return Results.NotFound(new
                {
                    code = "diagnostic_session_not_found",
                    error = ex.Message,
                    message = ex.Message,
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest("diagnostic_session_invalid", ex.Message);
            }
        })
        .WithName("ListeningGetDiagnosticQuestions");

        // ─────────────────────────────────────────────────────────────────
        // §6.3 Diagnostic — auto-save per-question answer
        // ─────────────────────────────────────────────────────────────────

        group.MapPost("/diagnostic/sessions/{sessionId:guid}/attempts/{questionId}", async (
            Guid sessionId,
            string questionId,
            DiagnosticAnswerSubmission submission,
            HttpContext http,
            IListeningLearnerPathwayService svc,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            if (string.IsNullOrWhiteSpace(questionId))
            {
                return BadRequest("invalid_question_id", "questionId is required.");
            }

            try
            {
                await svc.SaveDiagnosticAnswerAsync(userId, sessionId, questionId, submission, ct);
                return Results.NoContent();
            }
            catch (InvalidOperationException ex) when (IsNotFound(ex))
            {
                return Results.NotFound(new
                {
                    code = "diagnostic_session_not_found",
                    error = ex.Message,
                    message = ex.Message,
                });
            }
            catch (InvalidOperationException ex)
            {
                // Session already submitted — answers are immutable.
                return BadRequest("diagnostic_answer_locked", ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest("invalid_diagnostic_answer", ex.Message);
            }
        })
        .WithName("ListeningSaveDiagnosticAnswer");

        // ─────────────────────────────────────────────────────────────────
        // §6.3 Diagnostic — submit (idempotent)
        // ─────────────────────────────────────────────────────────────────

        group.MapPost("/diagnostic/submit", async (
            ListeningSubmitDiagnosticRequest request,
            HttpContext http,
            IListeningLearnerPathwayService svc,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            try
            {
                // Service handles idempotent repeats by returning the cached
                // result envelope instead of re-grading the session.
                var result = await svc.SubmitDiagnosticAsync(userId, request, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex) when (IsNotFound(ex))
            {
                return Results.NotFound(new
                {
                    code = "diagnostic_session_not_found",
                    error = ex.Message,
                    message = ex.Message,
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest("diagnostic_submit_failed", ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest("invalid_diagnostic_submission", ex.Message);
            }
        })
        .WithName("ListeningSubmitDiagnostic");

        // ─────────────────────────────────────────────────────────────────
        // §6.4 Diagnostic — fetch persisted results
        // ─────────────────────────────────────────────────────────────────

        group.MapGet("/diagnostic-results/{sessionId:guid}", async (
            Guid sessionId,
            HttpContext http,
            IListeningLearnerPathwayService svc,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            try
            {
                var result = await svc.GetDiagnosticResultsAsync(userId, sessionId, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex) when (IsNotFound(ex) || IsNotCompleted(ex))
            {
                // Both "missing/foreign session" and "session not yet
                // submitted" surface as 404 — the frontend treats both as
                // "results screen unavailable, redirect to in-progress
                // diagnostic player or landing page".
                return Results.NotFound(new
                {
                    code = "diagnostic_results_not_available",
                    error = ex.Message,
                    message = ex.Message,
                });
            }
        })
        .WithName("ListeningGetDiagnosticResults");

        // ─────────────────────────────────────────────────────────────────
        // §25.7 Practice/diagnostic — auto-save Part-A scratch notes
        // ─────────────────────────────────────────────────────────────────

        group.MapPost("/practice/sessions/{sessionId:guid}/notes", async (
            Guid sessionId,
            SaveNotesRequest request,
            HttpContext http,
            IListeningLearnerPathwayService svc,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            try
            {
                await svc.SaveSessionNotesAsync(
                    userId, sessionId, request.QuestionId, request.NoteMarkdown, ct);
                return Results.Ok(new { savedAt = clock.GetUtcNow() });
            }
            catch (InvalidOperationException ex) when (IsNotFound(ex))
            {
                return Results.NotFound(new
                {
                    code = "practice_session_not_found",
                    error = ex.Message,
                    message = ex.Message,
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest("invalid_notes_request", ex.Message);
            }
        })
        .WithName("ListeningSaveSessionNotes");

        // ─────────────────────────────────────────────────────────────────
        // §6.4 / §27 Multi-week pathway
        // ─────────────────────────────────────────────────────────────────

        group.MapGet("/pathway", async (
            HttpContext http,
            IListeningLearnerPathwayService svc,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            try
            {
                var pathway = await svc.GetPathwayAsync(userId, ct);
                return Results.Ok(pathway);
            }
            catch (InvalidOperationException ex) when (IsNotFound(ex) || IsNoPathwayYet(ex))
            {
                return Results.NotFound(new
                {
                    code = "listening_pathway_not_generated",
                    error = ex.Message,
                    message = ex.Message,
                });
            }
        })
        .WithName("ListeningGetPathway");

        group.MapGet("/stage", async (
            HttpContext http,
            IListeningLearnerPathwayService svc,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            // GetStageAsync is safe pre-onboarding — returns HasProfile=false
            // rather than throwing — so no catch needed.
            var status = await svc.GetStageAsync(userId, ct);
            return Results.Ok(status);
        })
        .WithName("ListeningGetStage");

        // ─────────────────────────────────────────────────────────────────
        // §6.5 / §7.4 Skill + accent dashboards
        // ─────────────────────────────────────────────────────────────────

        group.MapGet("/skills/scores", async (
            HttpContext http,
            IListeningSkillScoringService scoring,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var (skills, _) = await scoring.GetScoresAsync(userId, ct);

            var response = skills
                .Select(s => new SkillScoreDto
                {
                    SkillCode = s.SkillCode,
                    Label = ResolveSkillLabel(s.SkillCode),
                    CurrentScore = s.CurrentScore,
                    DiagnosticScore = s.DiagnosticScore,
                    QuestionsAttempted = s.QuestionsAttempted,
                    QuestionsCorrect = s.QuestionsCorrect,
                })
                .ToList();

            return Results.Ok((IReadOnlyList<SkillScoreDto>)response);
        })
        .WithName("ListeningGetSkillScores");

        group.MapGet("/accents/progress", async (
            HttpContext http,
            IListeningSkillScoringService scoring,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var (_, accents) = await scoring.GetScoresAsync(userId, ct);

            var response = accents
                .Select(a => new AccentProgressDto
                {
                    Accent = a.Accent,
                    Label = ResolveAccentLabel(a.Accent),
                    AccuracyPercentage = a.AccuracyPercentage,
                    QuestionsAttempted = a.QuestionsAttempted,
                    MinutesListened = a.MinutesListened,
                    SelfConfidenceRating = a.SelfConfidenceRating,
                })
                .ToList();

            return Results.Ok((IReadOnlyList<AccentProgressDto>)response);
        })
        .WithName("ListeningGetAccentProgress");

        // ─────────────────────────────────────────────────────────────────
        // §14 Dictation drill subsystem (Phase 4)
        //
        // Three routes power the dictation page:
        //   1. POST /dictation/sessions       — start a new drill set
        //   2. POST /dictation/{id}/submit    — grade one drill answer
        //   3. GET  /dictation/stats          — header stats
        // Mounted on the same /v1/listening-pathway group so LearnerOnly + the
        // PerUser rate-limit partition apply automatically.
        // ─────────────────────────────────────────────────────────────────

        group.MapPost("/dictation/sessions", async (
            DictationSessionRequest request,
            HttpContext http,
            IDictationService dictation,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            // Clamp here as well so a malicious client cannot exhaust the
            // backend by asking for thousands of drills in one go.
            var target = request?.TargetCount is int t && t > 0 ? Math.Min(t, 25) : 8;

            try
            {
                var set = await dictation.SelectDrillSetAsync(userId, target, ct);
                return Results.Ok(set);
            }
            catch (ArgumentException ex)
            {
                return BadRequest("invalid_dictation_request", ex.Message);
            }
        })
        .WithName("ListeningStartDictationSession");

        group.MapPost("/dictation/{drillId:guid}/submit", async (
            Guid drillId,
            DictationSubmitRequest request,
            HttpContext http,
            IDictationService dictation,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var answer = request?.LearnerAnswer ?? string.Empty;

            try
            {
                var result = await dictation.SubmitAnswerAsync(userId, drillId, answer, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex) when (IsNotFound(ex))
            {
                return Results.NotFound(new
                {
                    code = "dictation_drill_not_found",
                    error = ex.Message,
                    message = ex.Message,
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest("invalid_dictation_submission", ex.Message);
            }
        })
        .WithName("ListeningSubmitDictation");

        group.MapGet("/dictation/stats", async (
            HttpContext http,
            IDictationService dictation,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var stats = await dictation.GetStatsAsync(userId, ct);
            return Results.Ok(stats);
        })
        .WithName("ListeningGetDictationStats");

        // ─────────────────────────────────────────────────────────────────
        // §15 Pronunciation library (Phase 4) — SM-2 spaced repetition.
        //
        // Six routes mirror the Reading vocabulary endpoints but operate
        // against the PronunciationCard + LearnerPronunciationCard tables.
        // All routes inherit LearnerOnly auth + the PerUser rate-limit
        // partition from the parent group.
        // ─────────────────────────────────────────────────────────────────

        group.MapGet("/pronunciation", async (
            HttpContext http,
            IPronunciationService pronunciation,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var cards = await pronunciation.GetUserCardsAsync(userId, ct);
            return Results.Ok(cards);
        })
        .WithName("ListeningGetPronunciationCards");

        group.MapPost("/pronunciation", async (
            PronunciationAddRequest request,
            HttpContext http,
            IPronunciationService pronunciation,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var word = request?.Word ?? string.Empty;
            var source = request?.Source ?? "manual";

            try
            {
                var card = await pronunciation.AddCardAsync(userId, word, source, ct);
                return Results.Ok(card);
            }
            catch (ArgumentException ex)
            {
                return BadRequest("invalid_pronunciation_request", ex.Message);
            }
        })
        .WithName("ListeningAddPronunciationCard");

        group.MapGet("/pronunciation/due", async (
            int? max,
            HttpContext http,
            IPronunciationService pronunciation,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var requested = max is int m && m > 0 ? m : 25;
            var cards = await pronunciation.GetDueForReviewAsync(userId, requested, ct);
            return Results.Ok(cards);
        })
        .WithName("ListeningGetPronunciationDue");

        group.MapPost("/pronunciation/{cardId:guid}/review", async (
            Guid cardId,
            PronunciationReviewRequest request,
            HttpContext http,
            IPronunciationService pronunciation,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var quality = request?.Quality ?? -1;

            try
            {
                var result = await pronunciation.SubmitReviewAsync(userId, cardId, quality, ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new
                {
                    code = "pronunciation_card_not_found",
                    error = ex.Message,
                    message = ex.Message,
                });
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return BadRequest("invalid_pronunciation_quality", ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest("invalid_pronunciation_review", ex.Message);
            }
        })
        .WithName("ListeningSubmitPronunciationReview");

        group.MapDelete("/pronunciation/{cardId:guid}", async (
            Guid cardId,
            HttpContext http,
            IPronunciationService pronunciation,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            await pronunciation.RemoveCardAsync(userId, cardId, ct);
            return Results.NoContent();
        })
        .WithName("ListeningRemovePronunciationCard");

        group.MapGet("/pronunciation/stats", async (
            HttpContext http,
            IPronunciationService pronunciation,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var stats = await pronunciation.GetStatsAsync(userId, ct);
            return Results.Ok(stats);
        })
        .WithName("ListeningGetPronunciationStats");

        // ─────────────────────────────────────────────────────────────────
        // §8.1 / §10 Daily plan engine (Phase 3)
        //
        // The 4 plan routes below mirror the Reading daily-plan endpoints
        // (cf. ReadingPathwayEndpoints.cs §23.2) but use the Listening
        // service surface and an idempotent generator. Cross-user item IDs
        // surface as 404 to prevent enumeration probes.
        // ─────────────────────────────────────────────────────────────────

        group.MapGet("/plan/today", async (
            HttpContext http,
            IListeningDailyPlanService svc,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

            var items = await svc.GetTodayAsync(userId, today, ct);
            if (items.Count == 0)
            {
                // First-touch today → generate + return the freshly created
                // plan items.
                items = await svc.GenerateForTodayIfMissingAsync(userId, today, ct);
            }

            return Results.Ok(items.Select(ToPlanItemDto).ToList());
        })
        .WithName("ListeningGetTodayPlan");

        group.MapPost("/plan/items/{id:guid}/start", async (
            Guid id,
            HttpContext http,
            IListeningDailyPlanService svc,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            try
            {
                var item = await svc.StartItemAsync(userId, id, ct);
                return Results.Ok(ToPlanItemDto(item));
            }
            catch (InvalidOperationException ex) when (IsNotFound(ex))
            {
                return Results.NotFound(new
                {
                    code = "listening_plan_item_not_found",
                    error = ex.Message,
                    message = ex.Message,
                });
            }
        })
        .WithName("ListeningStartPlanItem");

        group.MapPost("/plan/items/{id:guid}/complete", async (
            Guid id,
            HttpContext http,
            IListeningDailyPlanService svc,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            try
            {
                var item = await svc.CompleteItemAsync(userId, id, ct);
                return Results.Ok(ToPlanItemDto(item));
            }
            catch (InvalidOperationException ex) when (IsNotFound(ex))
            {
                return Results.NotFound(new
                {
                    code = "listening_plan_item_not_found",
                    error = ex.Message,
                    message = ex.Message,
                });
            }
        })
        .WithName("ListeningCompletePlanItem");

        group.MapPost("/plan/items/{id:guid}/skip", async (
            Guid id,
            ListeningSkipPlanItemRequest? request,
            HttpContext http,
            IListeningDailyPlanService svc,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            try
            {
                var item = await svc.SkipItemAsync(userId, id, request?.Reason, ct);
                return Results.Ok(ToPlanItemDto(item));
            }
            catch (InvalidOperationException ex) when (IsNotFound(ex))
            {
                return Results.NotFound(new
                {
                    code = "listening_plan_item_not_found",
                    error = ex.Message,
                    message = ex.Message,
                });
            }
        })
        .WithName("ListeningSkipPlanItem");

        return app;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Request DTOs — kept private to the endpoints file because they only
    // exist to shape the JSON body for the dictation + pronunciation routes.
    // ─────────────────────────────────────────────────────────────────────

    private sealed record DictationSessionRequest(int? TargetCount);
    private sealed record DictationSubmitRequest(string? LearnerAnswer);
    private sealed record PronunciationAddRequest(string Word, string? Source);
    private sealed record PronunciationReviewRequest(int Quality);
    private sealed record ListeningSkipPlanItemRequest(string? Reason);

    /// <summary>
    /// Lift a <see cref="OetLearner.Api.Domain.ListeningDailyPlanItem"/> into
    /// the camelCase DTO the frontend client expects. We parse the payload JSON
    /// into a <see cref="System.Text.Json.JsonElement"/> so the wire shape is
    /// a JSON object rather than an opaque string. Bad payloads degrade to
    /// an empty object so a corrupt row doesn't break the whole plan fetch.
    /// </summary>
    private static object ToPlanItemDto(OetLearner.Api.Domain.ListeningDailyPlanItem item)
    {
        System.Text.Json.JsonElement payload;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(
                string.IsNullOrWhiteSpace(item.PayloadJson) ? "{}" : item.PayloadJson);
            payload = doc.RootElement.Clone();
        }
        catch (System.Text.Json.JsonException)
        {
            using var doc = System.Text.Json.JsonDocument.Parse("{}");
            payload = doc.RootElement.Clone();
        }

        return new
        {
            id = item.Id,
            planDate = item.PlanDate.ToString("yyyy-MM-dd"),
            ordinal = item.Ordinal,
            itemType = item.ItemType,
            focusSkill = item.FocusSkill,
            focusAccent = item.FocusAccent,
            estimatedMinutes = item.EstimatedMinutes,
            payload,
            status = item.Status,
            startedAt = item.StartedAt,
            completedAt = item.CompletedAt,
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Display labels for L1..L8 — mirrors the lookup inside
    /// <see cref="ListeningLearnerPathwayService"/> so the standalone score
    /// endpoints don't rely on the pathway service's private dictionary.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> SkillLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["L1"] = "Detail capture",
            ["L2"] = "Note-taking speed",
            ["L3"] = "Spelling accuracy",
            ["L4"] = "Gist comprehension",
            ["L5"] = "Distractor recognition",
            ["L6"] = "Inference",
            ["L7"] = "Speaker stance",
            ["L8"] = "Accent adaptation",
        };

    /// <summary>Display labels for the 4 accent rows.</summary>
    private static readonly IReadOnlyDictionary<string, string> AccentLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["british"] = "British (UK)",
            ["australian"] = "Australian",
            ["us"] = "North American",
            ["non_native"] = "Non-native",
        };

    /// <summary>
    /// Extract the authenticated learner's ID from the request. Throws when
    /// the route is unexpectedly reached without auth — the auth policy
    /// would have rejected anonymous traffic upstream, so this is a defence
    /// in depth rather than a normal control flow.
    /// </summary>
    private static string RequireUserId(HttpContext http)
    {
        return http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("auth required");
    }

    /// <summary>
    /// Compose the scheme + host of the inbound request into a base URL
    /// the pathway service can prepend to relative audio paths. We trim a
    /// trailing slash so the service can concatenate without double slashes.
    /// </summary>
    private static string BuildBaseUrl(HttpContext http)
    {
        var request = http.Request;
        var scheme = request.Scheme;
        var host = request.Host.HasValue ? request.Host.Value : string.Empty;
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }
        var pathBase = request.PathBase.HasValue ? request.PathBase.Value : string.Empty;
        return ($"{scheme}://{host}{pathBase}").TrimEnd('/');
    }

    /// <summary>
    /// Heuristic for "session/profile not found" exceptions thrown by the
    /// service. The service uses InvalidOperationException uniformly — we
    /// pattern-match the message because creating a dedicated exception
    /// type would ripple through the Reading pathway as well.
    /// </summary>
    private static bool IsNotFound(Exception ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("no listening profile exists", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Session present but diagnostic not yet submitted.</summary>
    private static bool IsNotCompleted(Exception ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("has not been submitted", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Pathway row missing because the diagnostic hasn't completed.</summary>
    private static bool IsNoPathwayYet(Exception ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("no listening pathway", StringComparison.OrdinalIgnoreCase)
            || message.Contains("complete the diagnostic", StringComparison.OrdinalIgnoreCase);
    }

    private static IResult BadRequest(string code, string message)
        => Results.BadRequest(new { code, error = message, message });

    private static string ResolveSkillLabel(string code)
        => SkillLabels.TryGetValue(code, out var label) ? label : code;

    private static string ResolveAccentLabel(string code)
        => AccentLabels.TryGetValue(code, out var label) ? label : code;
}
