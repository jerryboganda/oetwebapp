using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Grammar;

/// <summary>
/// Orchestration layer for Grammar operations (non-grading).
/// Grading lives in <see cref="IGrammarGradingService"/>. This service
/// owns publish-gate evaluation, recommendation creation, topic mastery
/// rollups, review-queue fan-out, and XP emission.
/// </summary>
public interface IGrammarService
{
    Task<GrammarPublishGateResult> EvaluatePublishGateAsync(string lessonId, CancellationToken ct);
    Task<bool> PublishLessonAsync(string lessonId, string adminId, string adminName, CancellationToken ct);
    Task<int> UnpublishLessonAsync(string lessonId, string adminId, string adminName, CancellationToken ct);
    Task<GrammarPostGradeSummary> ApplyPostGradeHooksAsync(string userId, GrammarGradingResult result, CancellationToken ct);
    Task<string> UpsertRecommendationAsync(
        string userId, string lessonId, string source, string? sourceRefId, string? ruleId, double relevance, CancellationToken ct);
    Task<double> ComputeReadinessContributionAsync(string userId, string examTypeCode, CancellationToken ct);
}

public sealed record GrammarPublishGateResult(bool CanPublish, IReadOnlyList<string> Errors);

public sealed record GrammarPostGradeSummary(
    int XpAwarded,
    int ReviewItemsCreated,
    bool TopicMastered,
    int MasteryScore);

public sealed class GrammarService(
    LearnerDbContext db,
    IGrammarPolicyService policy,
    IGrammarReviewFanOut reviewFanOut,
    ILogger<GrammarService> logger) : IGrammarService
{
    public async Task<GrammarPublishGateResult> EvaluatePublishGateAsync(string lessonId, CancellationToken ct)
    {
        var errors = new List<string>();

        var lesson = await db.GrammarLessons.FirstOrDefaultAsync(l => l.Id == lessonId, ct);
        if (lesson is null)
        {
            errors.Add("Lesson not found.");
            return new GrammarPublishGateResult(false, errors);
        }

        if (string.IsNullOrWhiteSpace(lesson.TopicId))
            errors.Add("TopicId is required before publish.");
        else
        {
            var topic = await db.GrammarTopics.FirstOrDefaultAsync(t => t.Id == lesson.TopicId, ct);
            if (topic is null) errors.Add("TopicId references an unknown topic.");
            else if (topic.Status != "published") errors.Add("Owning topic must be published first.");
        }

        if (string.IsNullOrWhiteSpace(lesson.SourceProvenance))
            errors.Add("SourceProvenance is required for publish.");

        var blockCount = await db.GrammarContentBlocks.CountAsync(b => b.LessonId == lessonId, ct);
        if (blockCount < 1) errors.Add("At least 1 content block is required.");

        var exercises = await db.GrammarExercises
            .Where(e => e.LessonId == lessonId)
            .ToListAsync(ct);
        if (exercises.Count < 3) errors.Add($"At least 3 exercises required (found {exercises.Count}).");

        foreach (var ex in exercises)
        {
            if (string.IsNullOrWhiteSpace(ex.CorrectAnswerJson) || ex.CorrectAnswerJson == "[]")
                errors.Add($"Exercise {ex.Id} is missing CorrectAnswer.");
            if (string.IsNullOrWhiteSpace(ex.ExplanationMarkdown))
                errors.Add($"Exercise {ex.Id} is missing Explanation.");
            if (ex.PromptMarkdown?.Contains("TODO", StringComparison.OrdinalIgnoreCase) == true
                || ex.PromptMarkdown?.Contains("TBD", StringComparison.OrdinalIgnoreCase) == true)
                errors.Add($"Exercise {ex.Id} contains TODO/TBD placeholder.");
        }

        // Content blocks TODO/TBD check
        var blocks = await db.GrammarContentBlocks
            .Where(b => b.LessonId == lessonId)
            .Select(b => b.ContentMarkdown)
            .ToListAsync(ct);
        foreach (var md in blocks)
        {
            if (md?.Contains("TODO", StringComparison.OrdinalIgnoreCase) == true
                || md?.Contains("TBD", StringComparison.OrdinalIgnoreCase) == true)
            {
                errors.Add("One or more content blocks contain TODO/TBD placeholder.");
                break;
            }
        }

        return new GrammarPublishGateResult(errors.Count == 0, errors);
    }

    public async Task<bool> PublishLessonAsync(string lessonId, string adminId, string adminName, CancellationToken ct)
    {
        var gate = await EvaluatePublishGateAsync(lessonId, ct);
        if (!gate.CanPublish)
            throw ApiException.Conflict("GRAMMAR_PUBLISH_GATE_FAILED",
                string.Join(" | ", gate.Errors));

        var lesson = await db.GrammarLessons.FirstAsync(l => l.Id == lessonId, ct);
        lesson.PublishState = "published";
        lesson.Status = "active";
        lesson.PublishedAt ??= DateTimeOffset.UtcNow;
        lesson.UpdatedAt = DateTimeOffset.UtcNow;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N")[..24],
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorName = adminName,
            Action = "Published",
            ResourceType = "GrammarLesson",
            ResourceId = lessonId,
            Details = $"Published grammar lesson: {lesson.Title}",
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Grammar lesson {LessonId} published by {AdminId}", lessonId, adminId);
        return true;
    }

    public async Task<int> UnpublishLessonAsync(string lessonId, string adminId, string adminName, CancellationToken ct)
    {
        var lesson = await db.GrammarLessons.FirstOrDefaultAsync(l => l.Id == lessonId, ct)
            ?? throw ApiException.NotFound("GRAMMAR_NOT_FOUND", $"Grammar lesson '{lessonId}' not found.");

        lesson.PublishState = "draft";
        lesson.UpdatedAt = DateTimeOffset.UtcNow;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N")[..24],
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorName = adminName,
            Action = "Unpublished",
            ResourceType = "GrammarLesson",
            ResourceId = lessonId,
            Details = $"Unpublished grammar lesson: {lesson.Title}",
        });

        return await db.SaveChangesAsync(ct);
    }

    public async Task<GrammarPostGradeSummary> ApplyPostGradeHooksAsync(
        string userId, GrammarGradingResult result, CancellationToken ct)
    {
        var lesson = await db.GrammarLessons
            .FirstOrDefaultAsync(l => l.Id == result.LessonId, ct)
            ?? throw new InvalidOperationException("Lesson vanished between grading and hooks.");

        var config = await policy.GetEffectiveAsync(lesson.ExamTypeCode, ct);

        // ── Fan out wrong answers to the review queue ───────────────────
        int reviewItems = 0;
        if (config.ReviewQueueEnabled)
        {
            reviewItems = await reviewFanOut.CreateReviewItemsAsync(userId, lesson, result, ct);
        }

        // ── Topic mastery rollup ─────────────────────────────────────────
        bool topicMastered = false;
        if (!string.IsNullOrWhiteSpace(lesson.TopicId))
        {
            topicMastered = await UpdateTopicMasteryAsync(userId, lesson.TopicId, config.MasteryThreshold, ct);
        }

        // ── XP emission (sum of applicable events) ──────────────────────
        int xp = config.XpLessonComplete;
        if (result.Mastered) xp += config.XpLessonMastered;
        if (topicMastered) xp += config.XpTopicMastered;

        // Note: actual XP crediting is attempted via GamificationService
        // if it's available. We do a soft resolve to avoid circular refs.
        // See GrammarLearnerEndpoints where gamification is called.

        return new GrammarPostGradeSummary(
            XpAwarded: xp,
            ReviewItemsCreated: reviewItems,
            TopicMastered: topicMastered,
            MasteryScore: result.MasteryScore);
    }

    public async Task<string> UpsertRecommendationAsync(
        string userId, string lessonId, string source, string? sourceRefId, string? ruleId, double relevance, CancellationToken ct)
    {
        var existing = await db.GrammarRecommendations
            .FirstOrDefaultAsync(r => r.UserId == userId && r.LessonId == lessonId, ct);
        if (existing is not null)
        {
            // Boost existing, reset dismissal if a new signal arrives.
            existing.Relevance = Math.Min(10.0, existing.Relevance + Math.Max(0.1, relevance * 0.5));
            existing.Source = source;
            existing.SourceRefId = sourceRefId;
            existing.RuleId = ruleId;
            existing.DismissedAt = null;
            await db.SaveChangesAsync(ct);
            return existing.Id;
        }

        var id = $"GRC-{Guid.NewGuid():N}"[..16];
        db.GrammarRecommendations.Add(new GrammarRecommendation
        {
            Id = id,
            UserId = userId,
            LessonId = lessonId,
            Source = source,
            SourceRefId = sourceRefId,
            RuleId = ruleId,
            Relevance = Math.Clamp(relevance, 0.1, 10.0),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return id;
    }

    public async Task<double> ComputeReadinessContributionAsync(string userId, string examTypeCode, CancellationToken ct)
    {
        // Weighted average mastery across all published topics the user has touched.
        var topicIds = await db.GrammarTopics
            .Where(t => t.ExamTypeCode == examTypeCode && t.Status == "published")
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (topicIds.Count == 0) return 0.0;

        var summaries = await db.LearnerGrammarMasterySummaries
            .Where(s => s.UserId == userId && topicIds.Contains(s.TopicId))
            .ToListAsync(ct);

        if (summaries.Count == 0) return 0.0;

        var sum = summaries.Sum(s => s.AvgMasteryScore);
        return sum / topicIds.Count; // Dilute by topics not yet touched
    }

    // ── Internals ───────────────────────────────────────────────────────

    private async Task<bool> UpdateTopicMasteryAsync(string userId, string topicId, int masteryThreshold, CancellationToken ct)
    {
        var lessonIds = await db.GrammarLessons
            .Where(l => l.TopicId == topicId && (l.PublishState == "published" || l.Status == "active"))
            .Select(l => l.Id)
            .ToListAsync(ct);

        if (lessonIds.Count == 0) return false;

        var progresses = await db.LearnerGrammarProgress
            .Where(p => p.UserId == userId && lessonIds.Contains(p.LessonId))
            .ToListAsync(ct);

        var summary = await db.LearnerGrammarMasterySummaries
            .FirstOrDefaultAsync(s => s.UserId == userId && s.TopicId == topicId, ct);
        if (summary is null)
        {
            summary = new LearnerGrammarMasterySummary
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
            };
            db.LearnerGrammarMasterySummaries.Add(summary);
        }

        summary.LessonsCompleted = progresses.Count(p => p.Status == "completed");
        summary.LessonsMastered = progresses.Count(p => p.MasteryScore >= masteryThreshold);
        summary.AvgMasteryScore = progresses.Count == 0 ? 0 : progresses.Average(p => (double)p.MasteryScore);
        summary.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return summary.LessonsMastered >= lessonIds.Count;
    }
}
