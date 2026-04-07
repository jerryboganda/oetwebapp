using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public class GamificationService(LearnerDbContext db)
{
    // ── XP ──────────────────────────────────────────────────────────────

    public async Task<object> GetXpAsync(string userId, CancellationToken ct)
    {
        var xp = await EnsureXpAsync(userId, ct);
        return MapXp(xp);
    }

    public async Task<object> AwardXpAsync(string userId, int amount, string reason, CancellationToken ct)
    {
        if (amount <= 0) throw ApiException.Validation("INVALID_XP_AMOUNT", "XP amount must be positive.");

        var xp = await EnsureXpAsync(userId, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Reset weekly/monthly buckets if periods have rolled over
        if (xp.WeekStartDate < today.AddDays(-(int)today.DayOfWeek))
        {
            xp.WeeklyXP = 0;
            xp.WeekStartDate = today.AddDays(-(int)today.DayOfWeek);
        }
        if (xp.MonthStartDate.Month != today.Month || xp.MonthStartDate.Year != today.Year)
        {
            xp.MonthlyXP = 0;
            xp.MonthStartDate = new DateOnly(today.Year, today.Month, 1);
        }

        xp.TotalXP += amount;
        xp.WeeklyXP += amount;
        xp.MonthlyXP += amount;
        xp.Level = ComputeLevel(xp.TotalXP);

        await db.SaveChangesAsync(ct);

        // Queue achievement check
        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"job-achv-{Guid.NewGuid():N}",
            Type = JobType.AchievementCheck,
            State = AsyncState.Queued,
            ResourceId = userId,
            PayloadJson = JsonSupport.Serialize(new { userId, trigger = "xp_awarded", amount, reason }),
            CreatedAt = DateTimeOffset.UtcNow,
            AvailableAt = DateTimeOffset.UtcNow,
            LastTransitionAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);

        return new { awarded = amount, reason, xp = MapXp(xp) };
    }

    // ── Streaks ──────────────────────────────────────────────────────────

    public async Task<object> GetStreakAsync(string userId, CancellationToken ct)
    {
        var streak = await EnsureStreakAsync(userId, ct);
        return MapStreak(streak);
    }

    public async Task<object> RecordActivityAsync(string userId, CancellationToken ct)
    {
        var streak = await EnsureStreakAsync(userId, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);

        if (streak.LastActiveDate == today)
            return new { updated = false, streak = MapStreak(streak) };

        if (streak.LastActiveDate == yesterday)
        {
            streak.CurrentStreak++;
        }
        else if (streak.LastActiveDate < yesterday)
        {
            // Streak broken — check freeze
            if (streak.StreakFreezeCount > streak.StreakFreezeUsedCount && streak.LastActiveDate == today.AddDays(-2))
            {
                streak.StreakFreezeUsedCount++;
                streak.LastFreezeUsedDate = today;
                streak.CurrentStreak++;
            }
            else
            {
                streak.CurrentStreak = 1;
            }
        }

        if (streak.CurrentStreak > streak.LongestStreak)
            streak.LongestStreak = streak.CurrentStreak;

        streak.LastActiveDate = today;
        await db.SaveChangesAsync(ct);

        return new { updated = true, streak = MapStreak(streak) };
    }

    // ── Achievements ────────────────────────────────────────────────────

    public async Task<object> GetAchievementsAsync(string userId, CancellationToken ct)
    {
        var all = await db.Achievements.Where(a => a.Status == "active")
            .OrderBy(a => a.SortOrder).ToListAsync(ct);

        var unlocked = await db.LearnerAchievements
            .Where(la => la.UserId == userId)
            .ToDictionaryAsync(la => la.AchievementId, ct);

        return all.Select(a => new
        {
            id = a.Id,
            code = a.Code,
            label = a.Label,
            description = a.Description,
            category = a.Category,
            iconUrl = a.IconUrl,
            xpReward = a.XPReward,
            sortOrder = a.SortOrder,
            unlocked = unlocked.TryGetValue(a.Id, out var la),
            unlockedAt = unlocked.TryGetValue(a.Id, out var la2) ? la2.UnlockedAt : (DateTimeOffset?)null
        });
    }

    public async Task CheckAndAwardAchievementsAsync(string userId, string trigger, CancellationToken ct)
    {
        var already = await db.LearnerAchievements
            .Where(la => la.UserId == userId)
            .Select(la => la.AchievementId)
            .ToHashSetAsync(ct);

        var candidates = await db.Achievements
            .Where(a => a.Status == "active" && !already.Contains(a.Id))
            .ToListAsync(ct);

        var xp = await db.LearnerXPs.FindAsync([userId], ct);
        var streak = await db.LearnerStreaks.FindAsync([userId], ct);
        var attemptCount = await db.Attempts.CountAsync(a => a.UserId == userId, ct);
        var vocabAdded = await db.LearnerVocabularies.CountAsync(v => v.UserId == userId, ct);
        var vocabMastered = await db.LearnerVocabularies.CountAsync(v => v.UserId == userId && v.Mastery == "mastered", ct);

        var toAward = new List<Achievement>();
        foreach (var ach in candidates)
        {
            if (await MeetsCriteriaAsync(ach, userId, xp, streak, attemptCount, vocabAdded, vocabMastered, ct))
                toAward.Add(ach);
        }

        if (toAward.Count == 0) return;

        foreach (var ach in toAward)
        {
            db.LearnerAchievements.Add(new LearnerAchievement
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AchievementId = ach.Id,
                UnlockedAt = DateTimeOffset.UtcNow,
                Notified = false
            });
            if (ach.XPReward > 0 && xp != null)
            {
                xp.TotalXP += ach.XPReward;
                xp.WeeklyXP += ach.XPReward;
                xp.MonthlyXP += ach.XPReward;
                xp.Level = ComputeLevel(xp.TotalXP);
            }
        }
        await db.SaveChangesAsync(ct);
    }

    // ── Leaderboard ──────────────────────────────────────────────────────

    public async Task<object> GetLeaderboardAsync(string? examTypeCode, string period, CancellationToken ct)
    {
        var query = db.LeaderboardEntries
            .Where(e => e.Period == period && e.OptedIn);
        if (!string.IsNullOrEmpty(examTypeCode))
            query = query.Where(e => e.ExamTypeCode == examTypeCode);

        var entries = await query.OrderBy(e => e.Rank).Take(100).ToListAsync(ct);
        return entries.Select(e => new
        {
            rank = e.Rank,
            displayName = e.DisplayName,
            xp = e.XP,
            examTypeCode = e.ExamTypeCode,
            period = e.Period,
            periodStart = e.PeriodStart
        });
    }

    public async Task<object> GetLeaderboardPositionAsync(string userId, string? examTypeCode, string period, CancellationToken ct)
    {
        var query = db.LeaderboardEntries.Where(e => e.UserId == userId && e.Period == period);
        if (!string.IsNullOrEmpty(examTypeCode))
            query = query.Where(e => e.ExamTypeCode == examTypeCode);

        var entry = await query.FirstOrDefaultAsync(ct);
        if (entry == null) return new { rank = (int?)null, xp = 0L, optedIn = false };
        return new { rank = entry.Rank, xp = entry.XP, optedIn = entry.OptedIn };
    }

    public async Task<object> SetLeaderboardOptInAsync(string userId, bool optedIn, CancellationToken ct)
    {
        var entries = await db.LeaderboardEntries.Where(e => e.UserId == userId).ToListAsync(ct);
        foreach (var e in entries) e.OptedIn = optedIn;
        if (entries.Count == 0)
        {
            var user = await db.Users.FindAsync([userId], ct);
            db.LeaderboardEntries.Add(new LeaderboardEntry
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                DisplayName = user?.DisplayName ?? "Learner",
                ExamTypeCode = "oet",
                Period = "weekly",
                PeriodStart = DateOnly.FromDateTime(DateTime.UtcNow),
                XP = 0,
                Rank = 9999,
                OptedIn = optedIn
            });
        }
        await db.SaveChangesAsync(ct);
        return new { optedIn };
    }

    // ── Private helpers ──────────────────────────────────────────────────

    private async Task<LearnerXP> EnsureXpAsync(string userId, CancellationToken ct)
    {
        var xp = await db.LearnerXPs.FindAsync([userId], ct);
        if (xp != null) return xp;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        xp = new LearnerXP
        {
            UserId = userId,
            TotalXP = 0,
            WeeklyXP = 0,
            MonthlyXP = 0,
            Level = 1,
            WeekStartDate = today.AddDays(-(int)today.DayOfWeek),
            MonthStartDate = new DateOnly(today.Year, today.Month, 1)
        };
        db.LearnerXPs.Add(xp);
        await db.SaveChangesAsync(ct);
        return xp;
    }

    private async Task<LearnerStreak> EnsureStreakAsync(string userId, CancellationToken ct)
    {
        var streak = await db.LearnerStreaks.FindAsync([userId], ct);
        if (streak != null) return streak;

        streak = new LearnerStreak
        {
            UserId = userId,
            CurrentStreak = 0,
            LongestStreak = 0,
            LastActiveDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            StreakFreezeCount = 1,
            StreakFreezeUsedCount = 0
        };
        db.LearnerStreaks.Add(streak);
        await db.SaveChangesAsync(ct);
        return streak;
    }

    private static int ComputeLevel(long totalXp)
    {
        // Level thresholds: 1=0, 2=100, 3=300, 4=600, 5=1000, 6=1500, 7=2100, 8=2800, ...
        // Formula: level = floor((1 + sqrt(1 + 8*xp/100)) / 2)
        if (totalXp <= 0) return 1;
        var level = (int)Math.Floor((1 + Math.Sqrt(1 + 8.0 * totalXp / 100)) / 2);
        return Math.Min(level, 100);
    }

    private static object MapXp(LearnerXP xp) => new
    {
        totalXP = xp.TotalXP,
        weeklyXP = xp.WeeklyXP,
        monthlyXP = xp.MonthlyXP,
        level = xp.Level,
        nextLevelXP = ComputeLevelThreshold(xp.Level + 1),
        currentLevelXP = ComputeLevelThreshold(xp.Level)
    };

    private static long ComputeLevelThreshold(int level)
    {
        if (level <= 1) return 0;
        return (long)(100 * level * (level - 1) / 2);
    }

    private static object MapStreak(LearnerStreak s) => new
    {
        currentStreak = s.CurrentStreak,
        longestStreak = s.LongestStreak,
        lastActiveDate = s.LastActiveDate,
        streakFreezesAvailable = s.StreakFreezeCount - s.StreakFreezeUsedCount
    };

    private static async Task<bool> MeetsCriteriaAsync(
        Achievement ach, string userId, LearnerXP? xp, LearnerStreak? streak,
        int attemptCount, int vocabAdded, int vocabMastered, CancellationToken ct)
    {
        try
        {
            var criteria = System.Text.Json.JsonDocument.Parse(ach.CriteriaJson).RootElement;
            var type = criteria.GetProperty("type").GetString();
            return type switch
            {
                "attempt_count" => attemptCount >= criteria.GetProperty("threshold").GetInt32(),
                "streak_days" => (streak?.CurrentStreak ?? 0) >= criteria.GetProperty("threshold").GetInt32(),
                "total_xp" => (xp?.TotalXP ?? 0) >= criteria.GetProperty("threshold").GetInt64(),
                "vocab_added" => vocabAdded >= criteria.GetProperty("threshold").GetInt32(),
                "vocab_mastered" => vocabMastered >= criteria.GetProperty("threshold").GetInt32(),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }
}
