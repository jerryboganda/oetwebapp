using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Grammar;

/// <summary>
/// Backend-authoritative publish gate for grammar lessons. Mirrors the
/// Reading / Writing pattern: clients may surface "can publish?" via the
/// GET endpoint, but the actual publish action re-evaluates server-side
/// before flipping <see cref="GrammarLesson.Status"/>.
/// </summary>
public interface IGrammarPublishGateService
{
    Task<GrammarPublishGateResult> EvaluateAsync(string lessonId, CancellationToken ct);
    Task<GrammarPublishGateResult> PublishAsync(string lessonId, string? adminId, string? adminName, CancellationToken ct);
    Task<object> UnpublishAsync(string lessonId, string? adminId, string? adminName, CancellationToken ct);
    Task<GrammarLessonStats> GetStatsAsync(string lessonId, CancellationToken ct);
}

public sealed record GrammarPublishGateResult(bool CanPublish, IReadOnlyList<string> Errors);

public sealed record GrammarLessonStats(
    string LessonId,
    int Attempts,
    int UniqueLearners,
    double AverageMasteryScore,
    int ReviewItemsCreated);

public sealed class GrammarPublishGateService(
    LearnerDbContext db,
    ILogger<GrammarPublishGateService> logger) : IGrammarPublishGateService
{
    public async Task<GrammarPublishGateResult> EvaluateAsync(string lessonId, CancellationToken ct)
    {
        var entity = await db.GrammarLessons.FirstOrDefaultAsync(x => x.Id == lessonId, ct)
            ?? throw ApiException.NotFound("GRAMMAR_NOT_FOUND", $"Grammar lesson '{lessonId}' not found.");

        return EvaluateEntity(entity);
    }

    public async Task<GrammarPublishGateResult> PublishAsync(string lessonId, string? adminId, string? adminName, CancellationToken ct)
    {
        var entity = await db.GrammarLessons.FirstOrDefaultAsync(x => x.Id == lessonId, ct)
            ?? throw ApiException.NotFound("GRAMMAR_NOT_FOUND", $"Grammar lesson '{lessonId}' not found.");

        var verdict = EvaluateEntity(entity);
        if (!verdict.CanPublish) return verdict;

        entity.Status = "active";
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            ActorId = adminId ?? "system",
            ActorName = adminName ?? "system",
            Action = "GrammarLessonPublished",
            ResourceType = "GrammarLesson",
            ResourceId = lessonId,
            Details = $"Published lesson \"{entity.Title}\" via publish-gate service.",
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Grammar lesson {LessonId} published by {AdminId}", lessonId, adminId);
        return verdict;
    }

    public async Task<object> UnpublishAsync(string lessonId, string? adminId, string? adminName, CancellationToken ct)
    {
        var entity = await db.GrammarLessons.FirstOrDefaultAsync(x => x.Id == lessonId, ct)
            ?? throw ApiException.NotFound("GRAMMAR_NOT_FOUND", $"Grammar lesson '{lessonId}' not found.");

        entity.Status = "draft";
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            ActorId = adminId ?? "system",
            ActorName = adminName ?? "system",
            Action = "GrammarLessonUnpublished",
            ResourceType = "GrammarLesson",
            ResourceId = lessonId,
            Details = $"Unpublished lesson \"{entity.Title}\" via publish-gate service.",
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return new { id = lessonId, status = entity.Status };
    }

    public async Task<GrammarLessonStats> GetStatsAsync(string lessonId, CancellationToken ct)
    {
        var entity = await db.GrammarLessons.FirstOrDefaultAsync(x => x.Id == lessonId, ct)
            ?? throw ApiException.NotFound("GRAMMAR_NOT_FOUND", $"Grammar lesson '{lessonId}' not found.");

        var progressRows = await db.Set<LearnerGrammarProgress>()
            .Where(p => p.LessonId == lessonId)
            .Select(p => new { p.UserId, p.ExerciseScore })
            .ToListAsync(ct);

        var attempts = progressRows.Count;
        var uniqueLearners = progressRows.Select(r => r.UserId).Distinct().Count();
        var avgMastery = progressRows.Count > 0
            ? progressRows.Where(r => r.ExerciseScore.HasValue).Select(r => (double)r.ExerciseScore!.Value).DefaultIfEmpty(0).Average()
            : 0;

        var reviewItems = await db.Set<ReviewItem>()
            .Where(r => r.SourceType == "grammar_error" && r.SourceId != null && r.SourceId.StartsWith(lessonId + ":"))
            .CountAsync(ct);

        return new GrammarLessonStats(
            LessonId: lessonId,
            Attempts: attempts,
            UniqueLearners: uniqueLearners,
            AverageMasteryScore: Math.Round(avgMastery, 1),
            ReviewItemsCreated: reviewItems);
    }

    // ---------------------------------------------------------------------

    private static GrammarPublishGateResult EvaluateEntity(GrammarLesson entity)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(entity.Title)) errors.Add("Title is required.");
        if (string.IsNullOrWhiteSpace(entity.Description)) errors.Add("Description is required.");
        if (string.IsNullOrWhiteSpace(entity.Category)) errors.Add("Category is required.");

        int contentBlockCount = 0;
        int exerciseCount = 0;
        bool hasProvenance = false;
        int appliedRuleIdsCount = 0;

        if (!string.IsNullOrWhiteSpace(entity.ContentHtml))
        {
            try
            {
                using var doc = JsonDocument.Parse(entity.ContentHtml);
                var root = doc.RootElement;
                if (root.TryGetProperty("contentBlocks", out var cb) && cb.ValueKind == JsonValueKind.Array)
                    contentBlockCount = cb.GetArrayLength();
                if (root.TryGetProperty("exercises", out var ex) && ex.ValueKind == JsonValueKind.Array)
                    exerciseCount = ex.GetArrayLength();
                if (root.TryGetProperty("sourceProvenance", out var sp) && sp.ValueKind == JsonValueKind.String)
                    hasProvenance = !string.IsNullOrWhiteSpace(sp.GetString());
                if (root.TryGetProperty("appliedRuleIds", out var ar) && ar.ValueKind == JsonValueKind.Array)
                    appliedRuleIdsCount = ar.GetArrayLength();
            }
            catch (JsonException)
            {
                errors.Add("Lesson content is not valid JSON. Re-save via the editor.");
            }
        }
        else
        {
            errors.Add("Lesson content is empty.");
        }

        if (contentBlockCount == 0) errors.Add("Add at least one content block.");
        if (exerciseCount < 3) errors.Add("At least 3 exercises are required before publishing.");
        if (exerciseCount > 12) errors.Add("At most 12 exercises are allowed per lesson.");
        if (!hasProvenance) errors.Add("sourceProvenance is required (cite the rulebook version).");
        if (appliedRuleIdsCount == 0) errors.Add("At least one appliedRuleId (from the grammar rulebook) is required.");

        return new GrammarPublishGateResult(CanPublish: errors.Count == 0, Errors: errors);
    }
}
