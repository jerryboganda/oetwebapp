using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// XP Service — Reading Module Pathway WS5
//
// Manages learner XP totals, level calculation, and badge awards (spec §14.2–3).
//
// XP amounts:
//   10  per question answered
//   25  per correct answer
//   100 per lesson completed
//   250 per mock completed
//
// Level thresholds (10 levels):
//   L1:  0–500       L6:  5501–8000
//   L2:  501–1000    L7:  8001–10000
//   L3:  1001–2000   L8:  10001–12500
//   L4:  2001–3500   L9:  12501–15000
//   L5:  3501–5500   L10: 15001+
// ═════════════════════════════════════════════════════════════════════════════

public interface IXpService
{
    /// <summary>Add <paramref name="xpAmount"/> XP to the learner's total.
    /// Returns the new total XP.</summary>
    Task<int> AwardXpAsync(string userId, int xpAmount, string reason, CancellationToken ct);

    /// <summary>Return the learner's current <see cref="LearnerXp"/> row,
    /// creating it with zeroes if it does not yet exist.</summary>
    Task<LearnerXp> GetXpAsync(string userId, CancellationToken ct);

    /// <summary>Evaluate all badge conditions and award any newly-qualifying
    /// badges not yet in the <see cref="LearnerBadge"/> table.
    /// Returns the list of newly-awarded badge codes.</summary>
    Task<List<string>> CheckAndAwardBadgesAsync(string userId, CancellationToken ct);
}

// ── XP amount constants (spec §14.2) ─────────────────────────────────────────
public static class XpAmounts
{
    public const int PerQuestionAnswered  = 10;
    public const int PerCorrectAnswer     = 25;
    public const int PerLessonCompleted   = 100;
    public const int PerMockCompleted     = 250;
}

public sealed class XpService(LearnerDbContext db) : IXpService
{
    // ── Level thresholds ─────────────────────────────────────────────────────

    /// <summary>Minimum XP to *reach* each level (index = level - 1).</summary>
    private static readonly int[] LevelFloors = [0, 501, 1001, 2001, 3501, 5501, 8001, 10001, 12501, 15001];

    /// <summary>XP ceiling (exclusive) for each level, or int.MaxValue for L10.</summary>
    private static readonly int[] LevelCeilings = [500, 1000, 2000, 3500, 5500, 8000, 10000, 12500, 15000, int.MaxValue];

    private static int ComputeLevel(int totalXp)
    {
        for (int i = LevelFloors.Length - 1; i >= 0; i--)
        {
            if (totalXp >= LevelFloors[i])
                return i + 1; // levels are 1-based
        }
        return 1;
    }

    private static int ComputeXpToNextLevel(int totalXp, int level)
    {
        if (level >= LevelFloors.Length)
            return 0; // L10 — no next level
        return LevelCeilings[level - 1] - totalXp + 1; // +1: ceiling is inclusive upper bound of the tier
    }

    // ── AwardXpAsync ──────────────────────────────────────────────────────────

    public async Task<int> AwardXpAsync(string userId, int xpAmount, string reason, CancellationToken ct)
    {
        var xp = await GetOrCreateXpRowAsync(userId, ct);
        xp.TotalXp += xpAmount;
        xp.CurrentLevel = ComputeLevel(xp.TotalXp);
        xp.XpToNextLevel = ComputeXpToNextLevel(xp.TotalXp, xp.CurrentLevel);
        xp.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return xp.TotalXp;
    }

    // ── GetXpAsync ────────────────────────────────────────────────────────────

    public async Task<LearnerXp> GetXpAsync(string userId, CancellationToken ct)
        => await GetOrCreateXpRowAsync(userId, ct);

    // ── CheckAndAwardBadgesAsync ──────────────────────────────────────────────

    public async Task<List<string>> CheckAndAwardBadgesAsync(string userId, CancellationToken ct)
    {
        // Pre-load already-earned badges to avoid duplicates
        var earned = await db.LearnerBadges
            .AsNoTracking()
            .Where(b => b.UserId == userId)
            .Select(b => b.BadgeCode)
            .ToHashSetAsync(ct);

        var newBadges = new List<string>();

        // ── "foundation_graduate" ── all 8 lessons with QuizScore >= 4 ────────
        if (!earned.Contains("foundation_graduate"))
        {
            var passingLessons = await db.LearnerLessonProgresses
                .AsNoTracking()
                .Where(lp => lp.UserId == userId && lp.QuizScore >= 4)
                .CountAsync(ct);
            if (passingLessons >= 8)
                newBadges.Add("foundation_graduate");
        }

        // ── "bookworm" ── 50 vocab words with RetentionScore >= 90 ────────────
        if (!earned.Contains("bookworm"))
        {
            var masteredWords = await db.LearnerVocabularyItems
                .AsNoTracking()
                .Where(v => v.UserId == userId && v.RetentionScore >= 90)
                .CountAsync(ct);
            if (masteredWords >= 50)
                newBadges.Add("bookworm");
        }

        // ── "speed_demon" ── 3 recent Part A drill sessions < 15 min / 10 Qs ──
        if (!earned.Contains("speed_demon"))
        {
            const int TargetSeconds = 15 * 60;
            const int RequiredQuestions = 10;

            var fastSessions = await db.ReadingPracticeSessions
                .AsNoTracking()
                .Where(s =>
                    s.UserId == userId &&
                    s.SessionType == "drill" &&
                    s.FocusSkill == "A" &&
                    s.DurationSeconds != null &&
                    s.DurationSeconds < TargetSeconds &&
                    s.TotalQuestions >= RequiredQuestions)
                .CountAsync(ct);
            if (fastSessions >= 3)
                newBadges.Add("speed_demon");
        }

        // ── "bullseye" ── any lesson with QuizScore == 5 AND QuizAttempts == 1 ─
        if (!earned.Contains("bullseye"))
        {
            var perfectFirst = await db.LearnerLessonProgresses
                .AsNoTracking()
                .AnyAsync(lp =>
                    lp.UserId == userId &&
                    lp.QuizScore == 5 &&
                    lp.QuizAttempts == 1, ct);
            if (perfectFirst)
                newBadges.Add("bullseye");
        }

        // ── "mock_master" ── any mock session with Score >= 35 (out of 42) ─────
        if (!earned.Contains("mock_master"))
        {
            var highMock = await db.ReadingPracticeSessions
                .AsNoTracking()
                .AnyAsync(s =>
                    s.UserId == userId &&
                    s.SessionType == "mock" &&
                    s.Score >= 35, ct);
            if (highMock)
                newBadges.Add("mock_master");
        }

        // Persist newly-earned badges
        foreach (var code in newBadges)
        {
            db.LearnerBadges.Add(new LearnerBadge
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                BadgeCode = code,
                EarnedAt = DateTimeOffset.UtcNow
            });
        }

        if (newBadges.Count > 0)
            await db.SaveChangesAsync(ct);

        return newBadges;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<LearnerXp> GetOrCreateXpRowAsync(string userId, CancellationToken ct)
    {
        var xp = await db.LearnerXps.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (xp is not null)
            return xp;

        xp = new LearnerXp
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TotalXp = 0,
            CurrentLevel = 1,
            XpToNextLevel = LevelCeilings[0], // 500 XP to reach L2
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.LearnerXps.Add(xp);
        await db.SaveChangesAsync(ct);
        return xp;
    }
}
