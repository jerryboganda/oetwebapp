using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin endpoints for the Reading Authoring subsystem. See
/// <c>docs/READING-AUTHORING-PLAN.md</c>.
///
/// Authorisation: requires <c>AdminContentWrite</c> (already used for
/// content papers). Publish actions go through the existing paper publish
/// flow; this surface just builds the Reading structure.
/// </summary>
public static class ReadingAuthoringAdminEndpoints
{
    public static IEndpointRouteBuilder MapReadingAuthoringAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/papers/{paperId}/reading")
            .RequireAuthorization("AdminContentWrite")
            .RequireRateLimiting("PerUserWrite");

        // Full structure (admin view — includes correct answers)
        group.MapGet("/structure", async (
            string paperId, IReadingStructureService svc, CancellationToken ct) =>
        {
            var structure = await svc.GetAdminStructureAsync(paperId, ct);
            return Results.Ok(structure);
        });

        group.MapGet("/manifest", async (
            string paperId, IReadingStructureService svc, CancellationToken ct) =>
        {
            var manifest = await svc.ExportManifestAsync(paperId, ct);
            return Results.Ok(manifest);
        });

        group.MapPost("/manifest", async (
            string paperId,
            ReadingStructureManifestImportDto dto,
            IReadingStructureService svc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try
            {
                var result = await svc.ImportManifestAsync(paperId, dto.Manifest, dto.ReplaceExisting, adminId, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (DbUpdateException)
            {
                return Results.Conflict(new { error = "Reading structure import could not be applied because existing learner data depends on this structure." });
            }
        });

        // Bootstrap canonical A/B/C parts (idempotent)
        group.MapPost("/ensure-canonical", async (
            string paperId, IReadingStructureService svc, CancellationToken ct) =>
        {
            await svc.EnsureCanonicalPartsAsync(paperId, ct);
            return Results.NoContent();
        });

        group.MapPut("/parts/{partCode}", async (
            string paperId, string partCode,
            ReadingPartUpsertDto dto,
            IReadingStructureService svc, HttpContext http, CancellationToken ct) =>
        {
            if (!Enum.TryParse<ReadingPartCode>(partCode, ignoreCase: true, out var code))
                return Results.BadRequest(new { error = "Invalid part code." });
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var part = await svc.UpsertPartAsync(new ReadingPartUpsert(
                paperId, code, dto.TimeLimitMinutes, dto.Instructions), adminId, ct);
            return Results.Ok(part);
        });

        group.MapPost("/texts", async (
            string paperId, ReadingTextUpsertDto dto,
            IReadingStructureService svc, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var partMatchesRoute = await db.ReadingParts.AsNoTracking()
                .AnyAsync(p => p.Id == dto.ReadingPartId && p.PaperId == paperId, ct);
            if (!partMatchesRoute)
                return Results.BadRequest(new { error = "Reading part does not belong to this paper." });

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try
            {
                var text = await svc.UpsertTextAsync(new ReadingTextUpsert(
                    dto.Id, dto.ReadingPartId, dto.DisplayOrder, dto.Title, dto.Source,
                    dto.BodyHtml, dto.WordCount, dto.TopicTag), adminId, ct);
                return Results.Ok(text);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapDelete("/texts/{textId}", async (
            string paperId, string textId, IReadingStructureService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var removed = await svc.RemoveTextAsync(paperId, textId, adminId, ct);
            return removed ? Results.NoContent() : Results.NotFound();
        });

        group.MapPost("/questions", async (
            string paperId, ReadingQuestionUpsertDto dto,
            IReadingStructureService svc, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var partMatchesRoute = await db.ReadingParts.AsNoTracking()
                .AnyAsync(p => p.Id == dto.ReadingPartId && p.PaperId == paperId, ct);
            if (!partMatchesRoute)
                return Results.BadRequest(new { error = "Reading part does not belong to this paper." });

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try
            {
                var q = await svc.UpsertQuestionAsync(new ReadingQuestionUpsert(
                    dto.Id, dto.ReadingPartId, dto.ReadingTextId, dto.DisplayOrder,
                    dto.Points, dto.QuestionType, dto.Stem, dto.OptionsJson,
                    dto.CorrectAnswerJson, dto.AcceptedSynonymsJson, dto.CaseSensitive,
                    dto.ExplanationMarkdown, dto.SkillTag), adminId, ct);
                return Results.Ok(q);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapDelete("/questions/{questionId}", async (
            string paperId, string questionId, IReadingStructureService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var removed = await svc.RemoveQuestionAsync(paperId, questionId, adminId, ct);
            return removed ? Results.NoContent() : Results.NotFound();
        });

        group.MapPost("/parts/{partId}/reorder-texts", async (
            string paperId, string partId, ReorderDto dto,
            IReadingStructureService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            await svc.ReorderTextsAsync(paperId, partId, dto.OrderedIds, adminId, ct);
            return Results.NoContent();
        });

        group.MapPost("/parts/{partId}/reorder-questions", async (
            string paperId, string partId, ReorderDto dto,
            IReadingStructureService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            await svc.ReorderQuestionsAsync(paperId, partId, dto.OrderedIds, adminId, ct);
            return Results.NoContent();
        });

        group.MapGet("/validate", async (
            string paperId, IReadingStructureService svc, CancellationToken ct) =>
        {
            var report = await svc.ValidatePaperAsync(paperId, ct);
            return Results.Ok(report);
        });

        // ── Phase 4 — distractor metadata ────────────────────────────────
        group.MapPut("/questions/{questionId}/distractors", async (
            string paperId, string questionId,
            ReadingDistractorsDto dto,
            IReadingReviewService reviewSvc,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            // Ensure the question is on this paper before mutating.
            var match = await db.ReadingQuestions.AsNoTracking()
                .Where(q => q.Id == questionId)
                .Join(db.ReadingParts.AsNoTracking(), q => q.ReadingPartId, p => p.Id, (q, p) => p.PaperId)
                .FirstOrDefaultAsync(ct);
            if (match is null) return Results.NotFound();
            if (!string.Equals(match, paperId, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "Question does not belong to this paper." });

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try
            {
                var q = await reviewSvc.SetDistractorsAsync(questionId, dto.Distractors, adminId, ct);
                return Results.Ok(new { q.Id, q.OptionDistractorsJson });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // ── Phase 4 — review-state lifecycle ─────────────────────────────
        group.MapGet("/questions/{questionId}/review-history", async (
            string paperId, string questionId,
            IReadingReviewService reviewSvc, LearnerDbContext db, CancellationToken ct) =>
        {
            var match = await db.ReadingQuestions.AsNoTracking()
                .Where(q => q.Id == questionId)
                .Join(db.ReadingParts.AsNoTracking(), q => q.ReadingPartId, p => p.Id, (q, p) => p.PaperId)
                .FirstOrDefaultAsync(ct);
            if (match is null) return Results.NotFound();
            if (!string.Equals(match, paperId, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "Question does not belong to this paper." });

            var history = await reviewSvc.GetHistoryAsync(questionId, ct);
            return Results.Ok(history);
        });

        group.MapPost("/questions/{questionId}/review-transition", async (
            string paperId, string questionId,
            ReadingReviewTransitionDto dto,
            IReadingReviewService reviewSvc,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var match = await db.ReadingQuestions.AsNoTracking()
                .Where(q => q.Id == questionId)
                .Join(db.ReadingParts.AsNoTracking(), q => q.ReadingPartId, p => p.Id, (q, p) => p.PaperId)
                .FirstOrDefaultAsync(ct);
            if (match is null) return Results.NotFound();
            if (!string.Equals(match, paperId, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "Question does not belong to this paper." });

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var displayName = http.User.FindFirstValue(ClaimTypes.Name);
            try
            {
                var result = await reviewSvc.TransitionStateAsync(new ReadingReviewTransitionArgs(
                    QuestionId: questionId,
                    ToState: dto.ToState,
                    ReviewerUserId: adminId,
                    ReviewerDisplayName: displayName,
                    Note: dto.Note,
                    IsAdminOverride: dto.IsAdminOverride), ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // ── Phase 5 — paper analytics ────────────────────────────────────
        group.MapGet("/analytics", async (
            string paperId, IReadingAnalyticsService analytics, CancellationToken ct) =>
        {
            var data = await analytics.GetPaperAnalyticsAsync(paperId, ct);
            return Results.Ok(data);
        });

        // ── Phase 6 — AI PDF extraction → admin approval ────────────────
        group.MapPost("/extractions", async (
            string paperId,
            ReadingExtractionRequestDto dto,
            HttpContext http,
            IReadingExtractionService svc,
            CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try
            {
                var draft = await svc.CreateDraftAsync(paperId, dto?.MediaAssetId, adminId, ct);
                return Results.Ok(draft);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapGet("/extractions", async (
            string paperId, IReadingExtractionService svc, CancellationToken ct) =>
        {
            var drafts = await svc.ListDraftsAsync(paperId, ct);
            return Results.Ok(drafts);
        });

        group.MapGet("/extractions/{draftId}", async (
            string paperId, string draftId, IReadingExtractionService svc, CancellationToken ct) =>
        {
            var draft = await svc.GetDraftAsync(draftId, ct);
            if (draft is null || draft.PaperId != paperId)
                return Results.NotFound();
            return Results.Ok(draft);
        });

        group.MapPost("/extractions/{draftId}/approve", async (
            string paperId, string draftId,
            HttpContext http, IReadingExtractionService svc, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var existing = await svc.GetDraftAsync(draftId, ct);
            if (existing is null || existing.PaperId != paperId) return Results.NotFound();
            try
            {
                var draft = await svc.ApproveDraftAsync(draftId, adminId, ct);
                return Results.Ok(draft);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPost("/extractions/{draftId}/reject", async (
            string paperId, string draftId,
            ReadingExtractionRejectDto? dto,
            HttpContext http, IReadingExtractionService svc, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var existing = await svc.GetDraftAsync(draftId, ct);
            if (existing is null || existing.PaperId != paperId) return Results.NotFound();
            try
            {
                var draft = await svc.RejectDraftAsync(draftId, adminId, dto?.Reason, ct);
                return Results.Ok(draft);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        return app;
    }
}

public sealed record ReadingExtractionRequestDto(string? MediaAssetId);

public sealed record ReadingExtractionRejectDto(string? Reason);

public sealed record ReadingDistractorsDto(IReadOnlyDictionary<string, ReadingDistractorCategory> Distractors);

public sealed record ReadingReviewTransitionDto(
    ReadingReviewState ToState,
    string? Note,
    bool IsAdminOverride);

public sealed record ReadingPartUpsertDto(int? TimeLimitMinutes, string? Instructions);

public sealed record ReadingStructureManifestImportDto(
    bool ReplaceExisting,
    ReadingStructureManifest Manifest);

public sealed record ReadingTextUpsertDto(
    string? Id,
    string ReadingPartId,
    int DisplayOrder,
    string Title,
    string? Source,
    string BodyHtml,
    int WordCount,
    string? TopicTag);

public sealed record ReadingQuestionUpsertDto(
    string? Id,
    string ReadingPartId,
    string? ReadingTextId,
    int DisplayOrder,
    int Points,
    ReadingQuestionType QuestionType,
    string Stem,
    string OptionsJson,
    string CorrectAnswerJson,
    string? AcceptedSynonymsJson,
    bool CaseSensitive,
    string? ExplanationMarkdown,
    string? SkillTag);

public sealed record ReorderDto(IReadOnlyList<string> OrderedIds);
