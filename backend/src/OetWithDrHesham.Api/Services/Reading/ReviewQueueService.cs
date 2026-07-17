using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Review Queue Service — WS3
//
// Manages the spaced-repetition wrong-answer review queue for reading
// practice.  Incorrect answers from a completed practice session are enqueued
// at ReviewIntervalIndex=0 (next review in 1 day).  Each correct review answer
// increments ConsecutiveCorrect; two consecutive correct answers clear the item
// from the queue entirely.
//
// Interval schedule (ReviewIntervalIndex): 0→1day, 1→3days, 2→7days.
// An incorrect review answer resets ConsecutiveCorrect to 0 and advances
// (or stays at) the current interval index (max 2).
// ═════════════════════════════════════════════════════════════════════════════

public interface IReviewQueueService
{
    /// <summary>
    /// Enqueue all incorrect attempts from the given practice session that are
    /// not already in the review queue.  Sets InReviewQueue=true,
    /// NextReviewAt=now+1day, ReviewIntervalIndex=0.
    /// </summary>
    Task EnqueueWrongAnswersAsync(string userId, Guid sessionId, CancellationToken ct);

    /// <summary>
    /// Advance the spaced-repetition state for the most-recent attempt row for
    /// <paramref name="questionId"/> belonging to <paramref name="userId"/>.
    /// <list type="bullet">
    ///   <item>Correct: increment ConsecutiveCorrect.  If ≥2, remove from queue.</item>
    ///   <item>Incorrect: advance interval index (max 2), reset ConsecutiveCorrect.</item>
    /// </list>
    /// </summary>
    Task AdvanceReviewIntervalAsync(string userId, Guid questionId, bool isCorrect, CancellationToken ct);

    /// <summary>Return the count of items that are due for review (NextReviewAt ≤ now).</summary>
    Task<int> GetQueueSizeAsync(string userId, CancellationToken ct);
}

public sealed class ReviewQueueService(LearnerDbContext db) : IReviewQueueService
{
    // Days to next review keyed by ReviewIntervalIndex (0=1day, 1=3days, 2=7days)
    private static readonly int[] IntervalDays = [1, 3, 7];

    // ═══════════════════════════════════════════════════════════════════════
    // EnqueueWrongAnswersAsync
    // ═══════════════════════════════════════════════════════════════════════

    public async Task EnqueueWrongAnswersAsync(string userId, Guid sessionId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Load all incorrect attempts from the session that are not yet enqueued
        var wrongAttempts = await db.ReadingQuestionAttempts
            .Where(a => a.UserId == userId
                && a.PracticeSessionId == sessionId
                && !a.IsCorrect
                && !a.InReviewQueue)
            .ToListAsync(ct);

        foreach (var attempt in wrongAttempts)
        {
            attempt.InReviewQueue = true;
            attempt.NextReviewAt = now.AddDays(IntervalDays[0]);
            attempt.ReviewIntervalIndex = 0;
            attempt.ConsecutiveCorrect = 0;
        }

        if (wrongAttempts.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AdvanceReviewIntervalAsync
    // ═══════════════════════════════════════════════════════════════════════

    public async Task AdvanceReviewIntervalAsync(
        string userId, Guid questionId, bool isCorrect, CancellationToken ct)
    {
        // Operate on the most-recent attempt row for this question that is
        // currently in the review queue.
        var attempt = await db.ReadingQuestionAttempts
            .Where(a => a.UserId == userId
                && a.ReadingQuestionId == questionId
                && a.InReviewQueue)
            .OrderByDescending(a => a.AttemptedAt)
            .FirstOrDefaultAsync(ct);

        if (attempt is null) return;

        var now = DateTimeOffset.UtcNow;

        if (isCorrect)
        {
            attempt.ConsecutiveCorrect++;
            if (attempt.ConsecutiveCorrect >= 2)
            {
                // Mastered — remove from queue
                attempt.InReviewQueue = false;
                attempt.NextReviewAt = null;
            }
            else
            {
                // Keep in queue, advance interval
                attempt.ReviewIntervalIndex = Math.Min(
                    attempt.ReviewIntervalIndex + 1,
                    IntervalDays.Length - 1);
                attempt.NextReviewAt = now.AddDays(IntervalDays[attempt.ReviewIntervalIndex]);
            }
        }
        else
        {
            // Incorrect review answer — advance interval, reset consecutive streak
            attempt.ReviewIntervalIndex = Math.Min(
                attempt.ReviewIntervalIndex + 1,
                IntervalDays.Length - 1);
            attempt.NextReviewAt = now.AddDays(IntervalDays[attempt.ReviewIntervalIndex]);
            attempt.ConsecutiveCorrect = 0;
        }

        await db.SaveChangesAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetQueueSizeAsync
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<int> GetQueueSizeAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.ReadingQuestionAttempts
            .AsNoTracking()
            .CountAsync(a => a.UserId == userId
                && a.InReviewQueue
                && a.NextReviewAt != null
                && a.NextReviewAt <= now, ct);
    }
}
