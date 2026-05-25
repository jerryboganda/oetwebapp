using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Streak Service — Reading Module Pathway WS5
//
// Tracks daily activity streaks per learner.  A day "qualifies" when the
// learner answers ≥8 questions.  CurrentStreak is the number of consecutive
// qualifying days up to and including today; LongestStreak is the all-time
// high watermark stored on the latest StreakRecord row.
// ═════════════════════════════════════════════════════════════════════════════

public interface IStreakService
{
    /// <summary>Record N questions answered today.  Creates or updates today's
    /// <see cref="StreakRecord"/> and recomputes the streak counters.</summary>
    Task RecordActivityAsync(string userId, int questionsAnswered, CancellationToken ct);

    /// <summary>Return the current streak status without mutating anything.</summary>
    Task<StreakStatusDto> GetStreakStatusAsync(string userId, CancellationToken ct);
}

public sealed record StreakStatusDto(
    int CurrentStreak,
    int LongestStreak,
    int QuestionsToday,
    bool StreakQualifiesForToday);

public sealed class StreakService(LearnerDbContext db) : IStreakService
{
    /// <summary>Minimum questions per day to count as a streak day.</summary>
    private const int StreakThreshold = 8;

    // ── RecordActivityAsync ───────────────────────────────────────────────────

    public async Task RecordActivityAsync(string userId, int questionsAnswered, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Upsert today's row
        var record = await db.StreakRecords
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Date == today, ct);

        if (record is null)
        {
            record = new StreakRecord
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Date = today,
                QuestionsAnsweredToday = 0
            };
            db.StreakRecords.Add(record);
        }

        record.QuestionsAnsweredToday += questionsAnswered;
        record.HasActivity = record.QuestionsAnsweredToday >= StreakThreshold;

        // Recompute CurrentStreak: count consecutive prior qualifying days
        int streak = record.HasActivity ? 1 : 0;

        if (record.HasActivity)
        {
            // Walk backwards from yesterday; stop at first gap
            var checkDate = today.AddDays(-1);
            while (true)
            {
                var prior = await db.StreakRecords
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.UserId == userId && r.Date == checkDate, ct);

                if (prior is null || !prior.HasActivity)
                    break;

                streak++;
                checkDate = checkDate.AddDays(-1);
            }
        }

        record.CurrentStreak = streak;
        record.LongestStreak = Math.Max(record.LongestStreak, streak);

        // Also update LongestStreak on any previous row that may hold a higher value
        // (safe because LongestStreak is monotonically non-decreasing on today's row)
        var previousBest = await db.StreakRecords
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.Date < today)
            .MaxAsync(r => (int?)r.LongestStreak, ct) ?? 0;

        record.LongestStreak = Math.Max(record.LongestStreak, previousBest);

        await db.SaveChangesAsync(ct);
    }

    // ── GetStreakStatusAsync ──────────────────────────────────────────────────

    public async Task<StreakStatusDto> GetStreakStatusAsync(string userId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var todayRecord = await db.StreakRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Date == today, ct);

        // If no record yet today, the current streak is from yesterday (if it was qualifying)
        int currentStreak;
        int longestStreak;
        int questionsToday;
        bool qualifies;

        if (todayRecord is not null)
        {
            currentStreak = todayRecord.CurrentStreak;
            longestStreak = todayRecord.LongestStreak;
            questionsToday = todayRecord.QuestionsAnsweredToday;
            qualifies = todayRecord.HasActivity;
        }
        else
        {
            questionsToday = 0;
            qualifies = false;

            // Carry forward streak from yesterday
            var yesterday = today.AddDays(-1);
            var yesterdayRecord = await db.StreakRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.UserId == userId && r.Date == yesterday, ct);

            currentStreak = yesterdayRecord?.HasActivity == true ? yesterdayRecord.CurrentStreak : 0;
            longestStreak = await db.StreakRecords
                .AsNoTracking()
                .Where(r => r.UserId == userId)
                .MaxAsync(r => (int?)r.LongestStreak, ct) ?? 0;
        }

        return new StreakStatusDto(currentStreak, longestStreak, questionsToday, qualifies);
    }
}
