using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Pronunciation;

/// <summary>
/// Spaced-repetition scheduler for pronunciation drills. Uses a SM-2-lite
/// algorithm on each <see cref="LearnerPronunciationProgress"/> row:
///
///   - New phoneme (AttemptCount = 1):
///       quality &gt;= 85%  → interval = 1 day, ease += 0.10
///       quality &gt;= 70%  → interval = 1 day, ease stable
///       quality &lt; 70%   → interval = same-day (repeat), ease -= 0.15 (floor 1.3)
///   - Subsequent attempts:
///       interval = round(prev_interval * ease), capped at 30 days
///       ease adjusted by quality same as above
///
/// <see cref="GetDueDrillsAsync"/> returns drills that (a) target a phoneme
/// the learner has not yet practiced, OR (b) target a phoneme whose
/// <see cref="LearnerPronunciationProgress.NextDueAt"/> has passed.
/// </summary>
public interface IPronunciationSchedulerService
{
    Task UpdateScheduleAsync(string userId, string phonemeCode, double lastScore0To100, CancellationToken ct);

    Task<IReadOnlyList<PronunciationDrill>> GetDueDrillsAsync(string userId, int limit, CancellationToken ct);
}

public sealed class PronunciationSchedulerService(LearnerDbContext db) : IPronunciationSchedulerService
{
    public async Task UpdateScheduleAsync(string userId, string phonemeCode, double lastScore0To100, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phonemeCode)) return;

        var row = await db.LearnerPronunciationProgress
            .FirstOrDefaultAsync(p => p.UserId == userId && p.PhonemeCode == phonemeCode, ct);
        if (row is null) return; // caller should have upserted by now

        var quality = Math.Clamp(lastScore0To100, 0, 100);
        var ease = row.Ease <= 1.29 ? 2.5 : row.Ease;

        if (quality >= 85)
        {
            ease += 0.10;
        }
        else if (quality < 70)
        {
            ease -= 0.15;
        }
        ease = Math.Max(1.3, Math.Min(2.9, ease));

        int interval;
        if (quality < 60)
        {
            interval = 0; // repeat soon
        }
        else if (row.IntervalDays <= 0)
        {
            interval = 1;
        }
        else
        {
            interval = (int)Math.Round(Math.Max(1, row.IntervalDays * ease));
            interval = Math.Min(30, interval);
        }

        row.Ease = ease;
        row.IntervalDays = interval;
        row.NextDueAt = DateTimeOffset.UtcNow.AddDays(Math.Max(0, interval));

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PronunciationDrill>> GetDueDrillsAsync(string userId, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 20);
        var now = DateTimeOffset.UtcNow;

        var progress = await db.LearnerPronunciationProgress
            .Where(p => p.UserId == userId)
            .ToListAsync(ct);

        // Phonemes that are due or not yet started. DateTimeOffset comparisons
        // stay client-side so the local SQLite E2E database behaves like Npgsql.
        var dueCodes = progress
            .Where(p => p.NextDueAt is null || p.NextDueAt <= now)
            .OrderBy(p => p.AverageScore) // weakest first
            .Select(p => p.PhonemeCode)
            .Take(limit * 2)
            .ToList();

        var drills = new List<PronunciationDrill>();
        // First, drills targeting weak phonemes
        if (dueCodes.Count > 0)
        {
            drills.AddRange(await db.PronunciationDrills
                .Where(d => d.Status == "active" && dueCodes.Contains(d.TargetPhoneme))
                .OrderBy(d => d.OrderIndex)
                .Take(limit)
                .ToListAsync(ct));
        }

        // Then, drills for phonemes never attempted (cold-start)
        if (drills.Count < limit)
        {
            var practiced = progress
                .Select(p => p.PhonemeCode)
                .ToList();
            var fresh = await db.PronunciationDrills
                .Where(d => d.Status == "active" && !practiced.Contains(d.TargetPhoneme))
                .OrderBy(d => d.Difficulty == "easy" ? 0 : d.Difficulty == "medium" ? 1 : 2)
                .ThenBy(d => d.OrderIndex)
                .Take(limit - drills.Count)
                .ToListAsync(ct);
            drills.AddRange(fresh);
        }

        // Dedup
        return drills.GroupBy(d => d.Id).Select(g => g.First()).Take(limit).ToList();
    }
}
