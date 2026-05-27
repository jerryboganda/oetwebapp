using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// ListeningLessonService — Phase 2 of OET_LISTENING_MODULE_PATHWAY.md §7.
//
// Manages the 8 foundation lessons (one per L1..L8 sub-skill). Each lesson is
// a 6-step "bootcamp":
//
//   1. Watch — short video intro to the sub-skill.
//   2. Read  — markdown body explaining the technique + healthcare examples.
//   3. Drill 1, 2, 3 — graduated practice questions (referenced by id, not
//      embedded — the player calls back into the listening drill surface).
//   4. Mini-quiz — 5-item check; learner must score >= 4 to complete the
//      lesson.
//
// Completion rules (mirrors LessonService.cs for Reading):
//   • VideoWatched && BodyRead && all 3 drills done && QuizScore >= 4
//     → CompletedAt is stamped.
//   • QuizAttempts increments only when a QuizScore is submitted.
//   • Re-submitting an update after CompletedAt is set is a no-op for the
//     timestamp — the lesson stays "completed at its first success".
//
// The Drill/Quiz id arrays are stored on ListeningLesson as JSON-serialised
// string arrays (DrillQuestionIdsJson / QuizQuestionIdsJson). We deserialise
// them on the way out so the DTO carries materialised IReadOnlyList<string>.
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningLessonService
{
    /// <summary>Fetch all published lessons (optionally filtered by sub-skill
    /// code L1..L8) with a per-learner CompletedByUser flag.</summary>
    Task<IReadOnlyList<LessonDto>> ListAsync(string userId, string? skillCode, CancellationToken ct);

    /// <summary>Fetch a single lesson + the learner's progress row (or null
    /// when the learner has never engaged with the lesson).</summary>
    Task<LessonDetailDto?> GetBySlugAsync(string userId, string slug, CancellationToken ct);

    /// <summary>Upsert a per-learner progress row. Null fields on the update
    /// leave the corresponding column unchanged. Auto-stamps CompletedAt
    /// when all completion conditions are met.</summary>
    Task<LearnerListeningLessonProgress> UpdateProgressAsync(
        string userId,
        Guid lessonId,
        LessonProgressUpdate update,
        CancellationToken ct);
}

/// <summary>Index projection — one row per lesson on the lessons listing.</summary>
public sealed record LessonDto(
    Guid Id,
    string Slug,
    string Title,
    string TitleAr,
    string SkillCode,
    int OrderIndex,
    int EstimatedMinutes,
    bool IsPublished,
    bool CompletedByUser);

/// <summary>Detail projection — includes body markdown, drill/quiz ids, and
/// the learner's progress envelope.</summary>
public sealed record LessonDetailDto(
    Guid Id,
    string Slug,
    string Title,
    string TitleAr,
    string SkillCode,
    int EstimatedMinutes,
    string? VideoUrl,
    string BodyMarkdownEn,
    string BodyMarkdownAr,
    IReadOnlyList<string> DrillQuestionIds,
    IReadOnlyList<string> QuizQuestionIds,
    LearnerListeningLessonProgress? Progress);

/// <summary>Partial update payload for a lesson progress record.
/// Null fields leave the corresponding column unchanged.</summary>
public sealed record LessonProgressUpdate(
    bool? VideoWatched,
    bool? BodyRead,
    bool? Drill1Completed,
    bool? Drill2Completed,
    bool? Drill3Completed,
    int? QuizScore);

public sealed class ListeningLessonService(LearnerDbContext db) : IListeningLessonService
{
    /// <summary>Minimum quiz score (out of 5) to count the lesson as completed.</summary>
    private const int QuizPassThreshold = 4;

    // ── ListAsync ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<LessonDto>> ListAsync(
        string userId,
        string? skillCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }

        var query = db.ListeningLessons
            .AsNoTracking()
            .Where(l => l.IsPublished);

        if (!string.IsNullOrWhiteSpace(skillCode))
        {
            var normalisedSkill = skillCode.Trim().ToUpperInvariant();
            query = query.Where(l => l.SkillCode == normalisedSkill);
        }

        var lessons = await query
            .OrderBy(l => l.SkillCode)
            .ThenBy(l => l.OrderIndex)
            .ToListAsync(ct);

        if (lessons.Count == 0)
        {
            return Array.Empty<LessonDto>();
        }

        // Fetch the learner's completion flags for the projected lessons in a
        // single round-trip. We project to a HashSet of LessonId for O(1)
        // membership tests when building the DTO list.
        var lessonIds = lessons.Select(l => l.Id).ToList();
        var completedLessonIds = await db.LearnerListeningLessonProgresses
            .AsNoTracking()
            .Where(p => p.UserId == userId
                && lessonIds.Contains(p.LessonId)
                && p.CompletedAt != null)
            .Select(p => p.LessonId)
            .ToListAsync(ct);
        var completedSet = new HashSet<Guid>(completedLessonIds);

        return lessons
            .Select(l => new LessonDto(
                l.Id,
                l.Slug,
                l.Title,
                l.TitleAr,
                l.SkillCode,
                l.OrderIndex,
                l.EstimatedMinutes,
                l.IsPublished,
                CompletedByUser: completedSet.Contains(l.Id)))
            .ToList();
    }

    // ── GetBySlugAsync ───────────────────────────────────────────────────────

    public async Task<LessonDetailDto?> GetBySlugAsync(
        string userId,
        string slug,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("slug is required.", nameof(slug));
        }

        var normalisedSlug = slug.Trim();
        var lesson = await db.ListeningLessons
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Slug == normalisedSlug && l.IsPublished, ct);

        if (lesson is null)
        {
            return null;
        }

        var progress = await db.LearnerListeningLessonProgresses
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.LessonId == lesson.Id, ct);

        var drillIds = DeserialiseStringList(lesson.DrillQuestionIdsJson);
        var quizIds = DeserialiseStringList(lesson.QuizQuestionIdsJson);

        return new LessonDetailDto(
            lesson.Id,
            lesson.Slug,
            lesson.Title,
            lesson.TitleAr,
            lesson.SkillCode,
            lesson.EstimatedMinutes,
            lesson.VideoUrl,
            lesson.BodyMarkdownEn,
            lesson.BodyMarkdownAr,
            drillIds,
            quizIds,
            progress);
    }

    // ── UpdateProgressAsync ─────────────────────────────────────────────────

    public async Task<LearnerListeningLessonProgress> UpdateProgressAsync(
        string userId,
        Guid lessonId,
        LessonProgressUpdate update,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }
        if (lessonId == Guid.Empty)
        {
            throw new ArgumentException("lessonId is required.", nameof(lessonId));
        }

        // Defend against orphan progress rows pointing at a deleted/missing
        // lesson — surface as InvalidOperationException so the endpoint maps
        // it to 404.
        var lessonExists = await db.ListeningLessons
            .AsNoTracking()
            .AnyAsync(l => l.Id == lessonId, ct);
        if (!lessonExists)
        {
            throw new InvalidOperationException($"Listening lesson {lessonId} not found.");
        }

        var progress = await db.LearnerListeningLessonProgresses
            .FirstOrDefaultAsync(p => p.UserId == userId && p.LessonId == lessonId, ct);

        if (progress is null)
        {
            progress = new LearnerListeningLessonProgress
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                LessonId = lessonId,
            };
            db.LearnerListeningLessonProgresses.Add(progress);
        }

        // Apply partial updates — null means "leave as-is".
        if (update.VideoWatched.HasValue)
        {
            progress.VideoWatched = update.VideoWatched.Value;
        }
        if (update.BodyRead.HasValue)
        {
            progress.BodyRead = update.BodyRead.Value;
        }
        if (update.Drill1Completed.HasValue)
        {
            progress.Drill1Completed = update.Drill1Completed.Value;
        }
        if (update.Drill2Completed.HasValue)
        {
            progress.Drill2Completed = update.Drill2Completed.Value;
        }
        if (update.Drill3Completed.HasValue)
        {
            progress.Drill3Completed = update.Drill3Completed.Value;
        }
        if (update.QuizScore.HasValue)
        {
            progress.QuizScore = update.QuizScore.Value;
            // QuizAttempts is only incremented when a fresh score lands so
            // the count reflects actual submissions, not idle re-saves.
            progress.QuizAttempts++;
        }

        // Auto-stamp completion when all six gates are satisfied. We never
        // unset CompletedAt on a subsequent partial update — that would be
        // surprising and erase the "first completion" semantics that learner-
        // analytics depend on.
        var allDrillsDone = progress.Drill1Completed
            && progress.Drill2Completed
            && progress.Drill3Completed;
        var quizPassed = progress.QuizScore >= QuizPassThreshold;
        var contentConsumed = progress.VideoWatched && progress.BodyRead;

        if (contentConsumed
            && allDrillsDone
            && quizPassed
            && progress.CompletedAt is null)
        {
            progress.CompletedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return progress;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Defensive JSON deserialisation for the DrillQuestionIdsJson and
    /// QuizQuestionIdsJson columns. Returns an empty list (rather than
    /// throwing) for malformed payloads so a single bad row can't break the
    /// detail endpoint.
    /// </summary>
    private static IReadOnlyList<string> DeserialiseStringList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }
        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json);
            return parsed ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
