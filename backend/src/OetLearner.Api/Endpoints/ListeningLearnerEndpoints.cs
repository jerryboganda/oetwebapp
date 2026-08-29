using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Assessment;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Endpoints;

public static class ListeningLearnerEndpoints
{
    public sealed record ListeningSubmitRequest(Dictionary<string, string?>? Answers);

    // Question-paper PDF annotations (Part B/C). Mirrors the Reading DTO shape so
    // the shared PDF viewer's create/delete callbacks work unchanged. Persisted in
    // the module-agnostic ReadingPaperAnnotation store (keyed user + paper + asset).
    private sealed record ListeningPaperAnnotationDto(
        string Id,
        string PaperId,
        string ContentPaperAssetId,
        int PageNumber,
        string Kind,
        object? Geometry,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record ListeningPaperAnnotationMutationDto(
        string ContentPaperAssetId,
        int PageNumber,
        string Kind,
        JsonElement GeometryJson);

    // The learner identifies the question paper by its media asset id (parsed
    // from the `/v1/media/{id}/content` URL it already holds), so annotations are
    // keyed by that id. Validate it belongs to a QuestionPaper asset on this paper.
    private static async Task<bool> ListeningAnnotationAssetExistsAsync(
        LearnerDbContext db, string paperId, string mediaAssetId, CancellationToken ct) =>
        await db.ContentPaperAssets.AsNoTracking().AnyAsync(
            a => a.MediaAssetId == mediaAssetId && a.PaperId == paperId && a.Role == PaperAssetRole.QuestionPaper, ct);

    private static object? SafeParseAnnotationJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return null; }
    }

    public static IEndpointRouteBuilder MapListeningLearnerEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Public test-rules constants (anonymous-allowed) ──
        // Structural values are safe to publish. Pass anchors are derived only
        // from a complete, effective owner table; no legacy formula constant
        // becomes a public scaled-score or pass claim.
        app.MapGet("/v1/listening-papers/policy/test-rules", async (
            LearnerDbContext db,
            IListeningPolicyService listeningPolicy,
            CancellationToken ct) =>
        {
            var policy = await listeningPolicy.GetGlobalAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var effectiveTables = await db.AssessmentScoreConversionTables
                .AsNoTracking()
                .Include(table => table.Rows)
                .Where(table => table.Assessment == "listening"
                    && table.ScopeKey == "default"
                    && table.EffectiveFrom <= now
                    && (table.Status == AssessmentGovernanceStatus.Effective
                        || table.Status == AssessmentGovernanceStatus.Locked))
                .OrderByDescending(table => table.EffectiveFrom)
                .ThenByDescending(table => table.VersionKey)
                .Take(2)
                .ToListAsync(ct);

            AssessmentScoreConversionTable? table = effectiveTables.Count == 1
                || (effectiveTables.Count > 1
                    && effectiveTables[0].EffectiveFrom != effectiveTables[1].EffectiveFrom)
                ? effectiveTables[0]
                : null;
            var validation = table is null
                ? null
                : AssessmentScoreTableValidator.Validate(
                    table.Assessment,
                    table.Rows.Select(row => new AssessmentScoreTableRowInput(
                        row.RawScore, row.ConvertedScore, row.Grade, row.Passed)).ToArray());
            var passingRow = validation?.IsValid == true
                ? table!.Rows
                    .Where(row => row.Passed == true)
                    .OrderBy(row => row.RawScore)
                    .FirstOrDefault()
                : null;

            return Results.Ok(new
            {
                questionCount = AssessmentScoreTableValidator.RawMaximum,
                durationMinutes = Math.Max(1, policy.FullPaperTimerMinutes),
                partA = new { items = 24, extracts = 2, itemType = "short-answer" },
                partB = new { items = 6, extracts = 6, itemType = "mcq-3-option" },
                partC = new { items = 12, extracts = 2, itemType = "mcq-3-option" },
                passRawAnchor = passingRow?.RawScore,
                passScaledAnchor = passingRow?.ConvertedScore,
                scaledMax = AssessmentScoreTableValidator.ConvertedMaximum,
                conversionTableVersion = table?.VersionKey,
                conversionUnavailableReason = passingRow is null
                    ? validation?.ErrorCode ?? "score_table_not_configured"
                    : null,
            });
        })
            .AllowAnonymous()
            .WithName("GetListeningTestRulesPolicy")
            .WithSummary("OET Listening test-rules constants (anonymous-allowed)");

        var group = app.MapGroup("/v1/listening-papers")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        // ── Course pathway snapshot (diagnostic → drills → mocks → ready) ──
        group.MapGet("/me/pathway", async (
            IListeningPathwayService pathway, HttpContext http, CancellationToken ct) =>
        {
            var snap = await pathway.GetPathwayAsync(http.UserId(), ct);
            return Results.Ok(snap);
        })
            .WithName("GetListeningPathway")
            .WithSummary("Get the learner's Listening course pathway snapshot");

        // ── Phase 6: per-learner Listening analytics ──
        group.MapGet("/me/analytics", async (
            IListeningAnalyticsService analytics, HttpContext http, CancellationToken ct) =>
        {
            var data = await analytics.GetMyAnalyticsAsync(http.UserId(), ct);
            return Results.Ok(data);
        })
            .WithName("GetListeningStudentAnalytics")
            .WithSummary("Per-learner Listening analytics: per-part accuracy, top weaknesses, action plan");

        // ── Phase 10: 12-stage Listening curriculum metadata ──
        group.MapGet("/me/curriculum", async (
            IListeningCurriculumService curriculum, HttpContext http, CancellationToken ct) =>
        {
            var data = await curriculum.GetCurriculumAsync(http.UserId(), ct);
            return Results.Ok(data);
        })
            .WithName("GetListeningCurriculum")
            .WithSummary("12-stage Listening curriculum + per-stage completion");

        group.MapGet("/papers/{paperId}/session", async (
            string paperId,
            string? mode,
            string? attemptId,
            string? pathwayStage,
            HttpContext http,
            ListeningLearnerService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetSessionAsync(http.UserId(), paperId, mode, attemptId, pathwayStage, ct)))
            .WithName("GetListeningPaperSession")
            .WithSummary("Get a learner-safe Listening paper session")
            .WithDescription("Returns Listening audio metadata, learner-safe questions, policy, and optional attempt state without exposing answer keys before submit.");

        // ── Question-paper PDF annotations (Part B/C) ───────────────────────
        // Per-learner highlight / strikethrough / freehand marks on the uploaded
        // question paper, mirroring Reading. Reuses the module-agnostic
        // ReadingPaperAnnotation store (no migration). Auth is LearnerOnly at the
        // group level; writes are tied to a real QuestionPaper asset on this paper.
        group.MapGet("/papers/{paperId}/annotations", async (
            string paperId, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var userId = http.UserId();
            var rows = await db.ReadingPaperAnnotations.AsNoTracking()
                .Where(a => a.UserId == userId && a.PaperId == paperId)
                .OrderBy(a => a.ContentPaperAssetId).ThenBy(a => a.PageNumber).ThenBy(a => a.CreatedAt)
                .Take(2000)
                .ToListAsync(ct);
            return Results.Ok(rows.Select(a => new ListeningPaperAnnotationDto(
                a.Id, a.PaperId, a.ContentPaperAssetId, a.PageNumber, a.Kind.ToString(),
                SafeParseAnnotationJson(a.GeometryJson), a.CreatedAt, a.UpdatedAt)));
        })
            .WithName("GetListeningPaperAnnotations")
            .WithSummary("List the learner's question-paper annotations for a Listening paper");

        group.MapPost("/papers/{paperId}/annotations", async (
            string paperId, ListeningPaperAnnotationMutationDto dto, LearnerDbContext db,
            HttpContext http, CancellationToken ct) =>
        {
            var userId = http.UserId();
            if (!await ListeningAnnotationAssetExistsAsync(db, paperId, dto.ContentPaperAssetId, ct))
                return Results.NotFound();
            if (!Enum.TryParse<ReadingPaperAnnotationKind>(dto.Kind, ignoreCase: true, out var kind))
                return Results.BadRequest();
            var now = DateTimeOffset.UtcNow;
            var row = new ReadingPaperAnnotation
            {
                Id = $"rpa_{Guid.NewGuid():N}",
                UserId = userId,
                PaperId = paperId,
                ContentPaperAssetId = dto.ContentPaperAssetId,
                PageNumber = dto.PageNumber,
                Kind = kind,
                GeometryJson = dto.GeometryJson.GetRawText(),
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.ReadingPaperAnnotations.Add(row);
            await db.SaveChangesAsync(ct);
            return Results.Created(
                $"/v1/listening-papers/papers/{paperId}/annotations/{row.Id}",
                new ListeningPaperAnnotationDto(row.Id, row.PaperId, row.ContentPaperAssetId,
                    row.PageNumber, row.Kind.ToString(), SafeParseAnnotationJson(row.GeometryJson), row.CreatedAt, row.UpdatedAt));
        })
            .RequireRateLimiting("PerUserWrite")
            .WithName("CreateListeningPaperAnnotation")
            .WithSummary("Create a question-paper annotation for a Listening paper");

        group.MapDelete("/papers/{paperId}/annotations/{annotationId}", async (
            string paperId, string annotationId, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var userId = http.UserId();
            var row = await db.ReadingPaperAnnotations
                .FirstOrDefaultAsync(a => a.Id == annotationId && a.UserId == userId && a.PaperId == paperId, ct);
            if (row is null) return Results.NotFound();
            db.ReadingPaperAnnotations.Remove(row);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
            .RequireRateLimiting("PerUserWrite")
            .WithName("DeleteListeningPaperAnnotation")
            .WithSummary("Delete a question-paper annotation for a Listening paper");

        group.MapPost("/papers/{paperId}/attempts", async (
            string paperId,
            ListeningAttemptStartRequest request,
            HttpContext http,
            MockService mockService,
            ListeningLearnerService service,
            CancellationToken ct) =>
        {
            var userId = http.UserId();
            var hasMockBinding = await mockService.ValidateSectionContentAttemptBindingTargetIfRequestedAsync(
                userId,
                request.MockAttemptId,
                request.MockSectionId,
                "listening",
                paperId,
                ct);
            var started = await service.StartAttemptAsync(
                userId,
                paperId,
                request.Mode,
                request.PathwayStage,
                forceNewAttempt: hasMockBinding,
                ct: ct,
                // Mock sections are billed once via the mock credit — skip the
                // per-paper Listening objective-practice debit so they don't
                // double-charge the learner's Listening test allowance.
                billObjectivePractice: !hasMockBinding);
            await mockService.BindSectionContentAttemptIfRequestedAsync(
                userId,
                request.MockAttemptId,
                request.MockSectionId,
                ExtractAttemptId(started),
                "listening",
                paperId,
                ct);
            return Results.Ok(started);
        })
            .RequireRateLimiting("PerUserWrite")
            .WithName("StartListeningPaperAttempt")
            .WithSummary("Start or resume a Listening paper attempt");

        group.MapPost("/papers/{paperId}/practice/parts/{partCode}", async (
            string paperId,
            string partCode,
            HttpContext http,
            ListeningLearnerService service,
            CancellationToken ct) =>
        {
            var started = await service.StartPartPracticeAttemptAsync(http.UserId(), paperId, partCode, ct);
            return Results.Ok(started);
        })
            .RequireRateLimiting("PerUserWrite")
            .WithName("StartListeningPartPracticeAttempt")
            .WithSummary("Start or resume a Part A/B/C Listening practice attempt");

        group.MapGet("/attempts/{attemptId}", async (
            string attemptId,
            HttpContext http,
            ListeningLearnerService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetAttemptAsync(http.UserId(), attemptId, ct)))
            .WithName("GetListeningPaperAttempt")
            .WithSummary("Get a Listening attempt for resume state");

        group.MapPut("/attempts/{attemptId}/answers/{questionId}", async (
            string attemptId,
            string questionId,
            ListeningAnswerSaveRequest request,
            HttpContext http,
            ListeningLearnerService service,
            CancellationToken ct) =>
        {
            await service.SaveAnswerAsync(http.UserId(), attemptId, questionId, request, ct);
            return Results.NoContent();
        })
            .RequireRateLimiting("PerUserWrite")
            .WithName("SaveListeningPaperAnswer")
            .WithSummary("Autosave one Listening answer");

        group.MapPatch("/attempts/{attemptId}/heartbeat", async (
            string attemptId,
            HeartbeatRequest request,
            HttpContext http,
            ListeningLearnerService service,
            CancellationToken ct) =>
            Results.Ok(await service.HeartbeatAsync(http.UserId(), attemptId, request, ct)))
            .RequireRateLimiting("PerUserWrite")
            .WithName("HeartbeatListeningPaperAttempt")
            .WithSummary("Persist Listening attempt playback/activity heartbeat");

        group.MapPost("/attempts/{attemptId}/advance-section", async (
            string attemptId,
            ListeningAdvanceSectionRequest request,
            HttpContext http,
            ListeningLearnerService service,
            CancellationToken ct) =>
            Results.Ok(await service.AdvanceSectionAsync(http.UserId(), attemptId, request, ct)))
            .RequireRateLimiting("PerUserWrite")
            .WithName("AdvanceListeningPaperSection")
            .WithSummary("Advance the one-way Listening section cursor")
            .WithDescription("Stores a monotonic sectionCursor on the attempt; rejects backward moves and forward skips (server-side one-way enforcement).");

        group.MapPost("/attempts/{attemptId}/integrity-events", async (
            string attemptId,
            ListeningIntegrityEventRequest request,
            HttpContext http,
            ListeningLearnerService service,
            CancellationToken ct) =>
        {
            await service.RecordIntegrityEventAsync(http.UserId(), attemptId, request, ct);
            return Results.NoContent();
        })
            .RequireRateLimiting("PerUserWrite")
            .WithName("RecordListeningIntegrityEvent")
            .WithSummary("Record an OET@Home Listening integrity event");

        group.MapPost("/attempts/{attemptId}/submit", async (
            string attemptId,
            ListeningSubmitRequest? request,
            HttpContext http,
            ListeningLearnerService service,
            CancellationToken ct) =>
        {
            var idempotencyKey = http.Request.Headers.TryGetValue("Idempotency-Key", out var header)
                ? header.ToString()
                : null;
            return Results.Ok(await service.SubmitAsync(
                http.UserId(), attemptId, request?.Answers, idempotencyKey, ct));
        })
            .RequireRateLimiting("PerUserWrite")
            .WithName("SubmitListeningPaperAttempt")
            .WithSummary("Submit and server-grade a Listening attempt");

        // Grounded post-submit AI explanation. The service derives the
        // learner answer from the owned submitted attempt and requires an
        // effective author-approved rationale before invoking the gateway.
        group.MapGet("/attempts/{attemptId}/questions/{questionId}/ai-explanation", async (
            string attemptId,
            string questionId,
            string? language,
            HttpContext http,
            IListeningExplanationService explanationService,
            CancellationToken ct) =>
        {
            try
            {
                var explanation = await explanationService.GetSubmittedAttemptExplanationAsync(
                    http.UserId(), attemptId, questionId, language ?? "en", ct);
                return Results.Ok(new
                {
                    explanation,
                    grounded = true,
                    advisoryOnly = true,
                    marksUnaffected = true,
                });
            }
            catch (ListeningGroundedExplanationUnavailableException ex)
            {
                return Results.Conflict(new
                {
                    code = "grounded_ai_unavailable",
                    error = ex.Message,
                    message = "A grounded explanation is unavailable until effective author-approved evidence exists.",
                });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new
                {
                    code = "grounded_ai_not_ready",
                    error = ex.Message,
                    message = ex.Message,
                });
            }
        }).RequireRateLimiting("PerUser");

        // Grounded post-submit candidate Q&A. The service derives all answer,
        // rationale, and transcript evidence from the owned attempt; the
        // caller cannot supply or replace marking evidence.
        group.MapPost("/attempts/{attemptId}/questions/{questionId}/ai-qna", async (
            string attemptId,
            string questionId,
            ListeningQuestionQnaRequest request,
            HttpContext http,
            IListeningQuestionQnaService qnaService,
            CancellationToken ct) =>
        {
            try
            {
                var response = await qnaService.AskAsync(
                    http.UserId(), attemptId, questionId, request, ct);
                return Results.Ok(response);
            }
            catch (ListeningQuestionQnaUnavailableException ex)
            {
                return Results.Conflict(new
                {
                    code = "grounded_ai_unavailable",
                    error = ex.Message,
                    message = "Grounded Listening Q&A is unavailable until the submitted evidence and AI pathway are ready.",
                });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new
                {
                    code = "grounded_ai_not_ready",
                    error = ex.Message,
                    message = ex.Message,
                });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { code = "invalid_request", error = ex.Message });
            }
        })
            .RequireRateLimiting("AiInteractive")
            .WithName("AskListeningQuestionGroundedAi")
            .WithSummary("Ask grounded advisory AI about a submitted Listening question");

        group.MapPost("/attempts/{attemptId}/answer-reports", async (
            string attemptId,
            AnswerKeyReportCreateRequest request,
            AnswerKeyReportService reports,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(await reports.CreateListeningAsync(http.UserId(), attemptId, request, ct)))
            .RequireRateLimiting("PerUserWrite");

        group.MapGet("/attempts/{attemptId}/answer-reports", async (
            string attemptId,
            AnswerKeyReportService reports,
            HttpContext http,
            CancellationToken ct) =>
            Results.Ok(new { items = await reports.ListListeningForAttemptAsync(http.UserId(), attemptId, ct) }));

        group.MapGet("/attempts/{attemptId}/review", async (
            string attemptId,
            HttpContext http,
            ListeningLearnerService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetReviewAsync(http.UserId(), attemptId, ct)))
            .WithName("GetListeningPaperReview")
            .WithSummary("Get policy-safe Listening result and transcript-backed review");

        group.MapGet("/drills/{drillId}", async (
            string drillId,
            string? paperId,
            string? attemptId,
            ListeningLearnerService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetDrillAsync(drillId, paperId, attemptId, ct)))
            .WithName("GetListeningPaperDrill")
            .WithSummary("Get a Listening drill recommendation");

        return app;
    }

    private static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");

    private static string ExtractAttemptId(object started)
    {
        var value = started.GetType().GetProperty("attemptId")?.GetValue(started) as string;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Listening attempt start response did not include an attempt id.");
        }

        return value;
    }
}

public sealed record ListeningAttemptStartRequest(string? Mode, string? PathwayStage, string? MockAttemptId, string? MockSectionId);
