using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
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
        // Surfaces OET-spec numbers (42 q, 30 raw pass, 350 scaled pass, etc.)
        // so the /listening/test-rules page can source them from the API
        // instead of hard-coding them in JSX. No learner data is read here.
        app.MapGet("/v1/listening-papers/policy/test-rules", () => Results.Ok(new
        {
            questionCount = OetScoring.ListeningReadingRawMax,
            durationMinutes = 40,
            partA = new { items = 24, extracts = 2, itemType = "short-answer" },
            partB = new { items = 6, extracts = 6, itemType = "mcq-3-option" },
            partC = new { items = 12, extracts = 2, itemType = "mcq-3-option" },
            passRawAnchor = OetScoring.ListeningReadingRawPass,
            passScaledAnchor = OetScoring.ScaledPassGradeB,
            scaledMax = OetScoring.ScaledMax,
        }))
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
                ct: ct);
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
            .WithDescription("Stores a monotonic sectionCursor on the attempt; rejects any request that would move it backwards (server-side one-way enforcement).");

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
            Results.Ok(await service.SubmitAsync(http.UserId(), attemptId, request?.Answers, ct)))
            .RequireRateLimiting("PerUserWrite")
            .WithName("SubmitListeningPaperAttempt")
            .WithSummary("Submit and server-grade a Listening attempt");

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
