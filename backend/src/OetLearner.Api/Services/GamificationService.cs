using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
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
        var candidates = await db.Achievements
            .AsNoTracking()
            .Where(a =>
                a.Status == "active" &&
                !db.LearnerAchievements.Any(la =>
                    la.UserId == userId &&
                    la.AchievementId == a.Id))
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return;

        var prerequisites = await (
            from user in db.Users
            where user.Id == userId
            join learnerXp in db.LearnerXPs on user.Id equals learnerXp.UserId into xpRows
            from xpRow in xpRows.DefaultIfEmpty()
            join learnerStreak in db.LearnerStreaks on user.Id equals learnerStreak.UserId into streakRows
            from streak in streakRows.DefaultIfEmpty()
            select new
            {
                Xp = xpRow,
                CurrentStreak = (int?)streak.CurrentStreak ?? 0,
                AttemptCount = db.Attempts.Count(a => a.UserId == user.Id),
                VocabAdded = db.LearnerVocabularies.Count(v => v.UserId == user.Id),
                VocabMastered = db.LearnerVocabularies.Count(v =>
                    v.UserId == user.Id &&
                    v.Mastery == "mastered")
            })
            .SingleOrDefaultAsync(ct);

        var xp = prerequisites?.Xp;

        var toAward = new List<Achievement>();
        foreach (var ach in candidates)
        {
            if (MeetsCriteria(
                ach,
                xp,
                prerequisites?.CurrentStreak ?? 0,
                prerequisites?.AttemptCount ?? 0,
                prerequisites?.VocabAdded ?? 0,
                prerequisites?.VocabMastered ?? 0))
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
                ExamTypeCode = OetLearner.Api.Services.Common.ExamCodes.DefaultCode,
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

    private static bool MeetsCriteria(
        Achievement ach,
        LearnerXP? xp,
        int currentStreak,
        int attemptCount,
        int vocabAdded,
        int vocabMastered)
    {
        try
        {
            var criteria = System.Text.Json.JsonDocument.Parse(ach.CriteriaJson).RootElement;
            var type = criteria.GetProperty("type").GetString();
            return type switch
            {
                "attempt_count" => attemptCount >= criteria.GetProperty("threshold").GetInt32(),
                "streak_days" => currentStreak >= criteria.GetProperty("threshold").GetInt32(),
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

    // ════════════════════════════════════════════
    //  Study Commitment
    // ════════════════════════════════════════════

    public async Task<object> GetStudyCommitmentAsync(string userId, CancellationToken ct)
    {
        var commitment = await db.StudyCommitments
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.IsActive)
            .FirstOrDefaultAsync(ct);

        var streak = await db.LearnerStreaks
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, ct);

        return new
        {
            hasCommitment = commitment is not null,
            dailyMinutes = commitment?.DailyMinutes ?? 0,
            freezeProtections = commitment?.FreezeProtections ?? 0,
            freezeProtectionsUsed = commitment?.FreezeProtectionsUsed ?? 0,
            currentStreak = streak?.CurrentStreak ?? 0,
            longestStreak = streak?.LongestStreak ?? 0
        };
    }

    public async Task<object> SetStudyCommitmentAsync(string userId, StudyCommitmentRequest request, CancellationToken ct)
    {
        if (request.DailyMinutes < 5 || request.DailyMinutes > 480)
            throw ApiException.Validation("invalid_minutes", "Daily minutes must be between 5 and 480.");

        var existing = await db.StudyCommitments
            .FirstOrDefaultAsync(c => c.UserId == userId && c.IsActive, ct);

        if (existing is not null)
        {
            existing.DailyMinutes = request.DailyMinutes;
            existing.FreezeProtections = 3;
        }
        else
        {
            db.StudyCommitments.Add(new StudyCommitment
            {
                Id = $"SC-{Guid.NewGuid():N}",
                UserId = userId,
                DailyMinutes = request.DailyMinutes,
                FreezeProtections = 3,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync(ct);
        return new { dailyMinutes = request.DailyMinutes, active = true };
    }

    // ════════════════════════════════════════════
    //  Certificates
    // ════════════════════════════════════════════

    public async Task<object> GetCertificatesAsync(string userId, CancellationToken ct)
    {
        var certs = await db.LearnerCertificates
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.IssuedAt)
            .Select(c => new
            {
                id = c.Id,
                type = c.CertificateType,
                title = c.Title,
                description = c.Description,
                downloadUrl = c.DownloadUrl,
                issuedAt = c.IssuedAt
            })
            .ToListAsync(ct);

        return new { certificates = certs };
    }

    public async Task IssueCertificateAsync(string userId, string type, string title, string description, CancellationToken ct)
    {
        var existing = await db.LearnerCertificates
            .AnyAsync(c => c.UserId == userId && c.CertificateType == type, ct);
        if (existing) return;

        db.LearnerCertificates.Add(new LearnerCertificate
        {
            Id = $"CERT-{Guid.NewGuid():N}",
            UserId = userId,
            CertificateType = type,
            Title = title,
            Description = description,
            IssuedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }
}
