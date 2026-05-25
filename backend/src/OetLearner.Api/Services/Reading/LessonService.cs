using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Lesson Service — Reading Module Pathway WS5
//
// Manages the 8 foundation lessons authored by the content team.  Each lesson
// has a prerequisite chain; a learner must complete the prior lesson before
// the next is unlocked.
//
// Completion rules:
//   • VideoWatched AND BodyRead AND all 3 drills done AND QuizScore >= 4
//     → CompletedAt is stamped.
//   • QuizAttempts is incremented only when a QuizScore is submitted.
//   • Pass threshold: QuizScore >= 4 (out of 5).
// ═════════════════════════════════════════════════════════════════════════════

public interface ILessonService
{
    /// <summary>Fetch a published lesson by slug, or null if not found / unpublished.</summary>
    Task<ReadingLesson?> GetLessonAsync(string slug, CancellationToken ct);

    /// <summary>Fetch all published lessons in <see cref="ReadingLesson.OrderIndex"/> order.</summary>
    Task<List<ReadingLesson>> GetLessonsAsync(CancellationToken ct);

    /// <summary>Upsert the learner's progress on a lesson.  Automatically
    /// stamps <see cref="LearnerLessonProgress.CompletedAt"/> when all
    /// completion conditions are met.</summary>
    Task<LearnerLessonProgress> UpdateProgressAsync(
        string userId,
        Guid lessonId,
        LessonProgressUpdate update,
        CancellationToken ct);

    /// <summary>Return true when the lesson has no prerequisite, or the
    /// prerequisite lesson's <see cref="LearnerLessonProgress.CompletedAt"/>
    /// is set for this learner.</summary>
    Task<bool> IsLessonUnlockedAsync(string userId, Guid lessonId, CancellationToken ct);
}

/// <summary>Partial update payload for a lesson progress record.
/// Null fields are left unchanged.</summary>
public sealed record LessonProgressUpdate(
    bool? VideoWatched,
    bool? BodyRead,
    bool? Drill1Completed,
    bool? Drill2Completed,
    bool? Drill3Completed,
    int? QuizScore);

public sealed class LessonService(LearnerDbContext db) : ILessonService
{
    private const int PassThreshold = 4; // QuizScore >= 4 out of 5

    // ── GetLessonAsync ────────────────────────────────────────────────────────

    public async Task<ReadingLesson?> GetLessonAsync(string slug, CancellationToken ct)
        => await db.ReadingLessons
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Slug == slug && l.IsPublished, ct);

    // ── GetLessonsAsync ───────────────────────────────────────────────────────

    public async Task<List<ReadingLesson>> GetLessonsAsync(CancellationToken ct)
        => await db.ReadingLessons
            .AsNoTracking()
            .Where(l => l.IsPublished)
            .OrderBy(l => l.OrderIndex)
            .ToListAsync(ct);

    // ── UpdateProgressAsync ───────────────────────────────────────────────────

    public async Task<LearnerLessonProgress> UpdateProgressAsync(
        string userId,
        Guid lessonId,
        LessonProgressUpdate update,
        CancellationToken ct)
    {
        var progress = await db.LearnerLessonProgresses
            .FirstOrDefaultAsync(p => p.UserId == userId && p.LessonId == lessonId, ct);

        if (progress is null)
        {
            progress = new LearnerLessonProgress
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                LessonId = lessonId
            };
            db.LearnerLessonProgresses.Add(progress);
        }

        // Apply partial updates — null means "leave as-is"
        if (update.VideoWatched.HasValue)
            progress.VideoWatched = update.VideoWatched.Value;
        if (update.BodyRead.HasValue)
            progress.BodyRead = update.BodyRead.Value;
        if (update.Drill1Completed.HasValue)
            progress.Drill1Completed = update.Drill1Completed.Value;
        if (update.Drill2Completed.HasValue)
            progress.Drill2Completed = update.Drill2Completed.Value;
        if (update.Drill3Completed.HasValue)
            progress.Drill3Completed = update.Drill3Completed.Value;

        if (update.QuizScore.HasValue)
        {
            progress.QuizScore = update.QuizScore.Value;
            progress.QuizAttempts++;  // only incremented when a score is submitted
        }

        // Auto-stamp completion when all conditions met
        bool allDrillsDone = progress.Drill1Completed &&
                             progress.Drill2Completed &&
                             progress.Drill3Completed;
        bool quizPassed = progress.QuizScore >= PassThreshold;
        bool contentConsumed = progress.VideoWatched && progress.BodyRead;

        if (contentConsumed && allDrillsDone && quizPassed && progress.CompletedAt is null)
            progress.CompletedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return progress;
    }

    // ── IsLessonUnlockedAsync ─────────────────────────────────────────────────

    public async Task<bool> IsLessonUnlockedAsync(string userId, Guid lessonId, CancellationToken ct)
    {
        var lesson = await db.ReadingLessons
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == lessonId, ct);

        if (lesson is null)
            return false;

        // No prerequisite → always unlocked
        if (lesson.PrerequisiteLessonId is null)
            return true;

        // Prerequisite must be completed (CompletedAt set) by this learner
        return await db.LearnerLessonProgresses
            .AsNoTracking()
            .AnyAsync(p =>
                p.UserId == userId &&
                p.LessonId == lesson.PrerequisiteLessonId.Value &&
                p.CompletedAt != null, ct);
    }
}
