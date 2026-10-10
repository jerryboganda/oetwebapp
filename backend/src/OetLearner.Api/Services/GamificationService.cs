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
        var readingTotal = await GetReadingTotalXpAsync(userId, ct);
        var mergedTotal = xp.TotalXP + readingTotal;
        return MapXp(xp, mergedTotal, ComputeLevel(mergedTotal));
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

        // This used to queue a JobType.AchievementCheck row here (one per XP award,
        // i.e. one per answered Reading question) and save a second time. No handler
        // ever existed for it, so each row was claimed, run as a silent no-op and
        // completed: pure queue and write load. Achievements are evaluated inline by
        // CheckAndAwardAchievementsAsync.

        return new { awarded = amount, reason, xp = MapXp(xp) };
    }

    // ── Streaks ──────────────────────────────────────────────────────────

    public async Task<object> GetStreakAsync(string userId, CancellationToken ct)
    {
        var streak = await EnsureStreakAsync(userId, ct);
        var reading = await GetLatestStreakRecordAsync(userId, ct);
        var engagement = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.CurrentStreak, u.LongestStreak, u.LastPracticeDate })
            .SingleOrDefaultAsync(ct);

        var mergedCurrent = Math.Max(streak.CurrentStreak, Math.Max(reading?.CurrentStreak ?? 0, engagement?.CurrentStreak ?? 0));
        var mergedLongest = Math.Max(streak.LongestStreak, Math.Max(reading?.LongestStreak ?? 0, engagement?.LongestStreak ?? 0));

        var lastActive = streak.LastActiveDate;
        if (reading is not null && reading.HasActivity && reading.Date > lastActive)
            lastActive = reading.Date;
        if (engagement?.LastPracticeDate is { } lastPractice)
        {
            var practiceDate = DateOnly.FromDateTime(lastPractice.UtcDateTime);
            if (practiceDate > lastActive) lastActive = practiceDate;
        }

        return MapStreak(streak, mergedCurrent, mergedLongest, lastActive);
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
            unlockedAt = unlocked.TryGetValue(a.Id, out var la2) ? la2.UnlockedAt : (DateTimeOffset?)null,
            // False means this achievement's criteria are NOT measured yet, so it can never be
            // awarded. Surfaced explicitly rather than leaving it looking permanently locked:
            // a "locked" badge and a "not implemented" badge are different things to a learner,
            // and only one of them is true. See EvaluableCriteriaTypes.
            evaluable = IsEvaluable(a)
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

        // Drop anything this service cannot evaluate before doing the prerequisite queries.
        // Without this filter MeetsCriteria returns false for them on every call forever, so
        // they would sit in the candidate set for the life of the account and be re-parsed on
        // every trigger. See EvaluableCriteriaTypes for why they are not evaluated, and why
        // that is a wiring gap rather than a bug in the criteria themselves.
        candidates = candidates.Where(IsEvaluable).ToList();

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
                EngagementCurrentStreak = user.CurrentStreak,
                AttemptCount = db.Attempts.Count(a => a.UserId == user.Id),
                VocabAdded = db.LearnerVocabularies.Count(v => v.UserId == user.Id),
                VocabMastered = db.LearnerVocabularies.Count(v =>
                    v.UserId == user.Id &&
                    v.Mastery == "mastered"),
                ForumPosts = db.ForumThreads.Count(t => t.AuthorUserId == user.Id)
                    + db.ForumReplies.Count(r => r.AuthorUserId == user.Id),
                ReferralsConverted = db.Referrals.Count(r => r.ReferrerUserId == user.Id),
                PronunciationDrills = db.PronunciationAttempts.Count(p =>
                    p.UserId == user.Id && p.Status == "completed")
            })
            .SingleOrDefaultAsync(ct);

        var readingTotal = await GetReadingTotalXpAsync(userId, ct);
        var latestReadingStreak = await GetLatestStreakRecordAsync(userId, ct);

        var xp = prerequisites?.Xp;
        var mergedTotalXp = (xp?.TotalXP ?? 0) + readingTotal;
        var mergedStreak = Math.Max(prerequisites?.CurrentStreak ?? 0,
            Math.Max(latestReadingStreak?.CurrentStreak ?? 0, prerequisites?.EngagementCurrentStreak ?? 0));

        var toAward = new List<Achievement>();
        foreach (var ach in candidates)
        {
            if (MeetsCriteria(
                ach,
                xp is null
                    ? (readingTotal > 0 ? new LearnerXP { TotalXP = readingTotal, Level = ComputeLevel(readingTotal) } : null)
                    : new LearnerXP { TotalXP = mergedTotalXp, Level = ComputeLevel(mergedTotalXp), WeeklyXP = xp.WeeklyXP, MonthlyXP = xp.MonthlyXP },
                mergedStreak,
                prerequisites?.AttemptCount ?? 0,
                prerequisites?.VocabAdded ?? 0,
                prerequisites?.VocabMastered ?? 0,
                prerequisites?.ForumPosts ?? 0,
                prerequisites?.ReferralsConverted ?? 0,
                prerequisites?.PronunciationDrills ?? 0))
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

    /// <summary>
    /// Fire-and-forget-safe wrapper around <see cref="CheckAndAwardAchievementsAsync"/>: an
    /// achievement evaluation must never break the learner flow that triggered it.
    /// </summary>
    public async Task TryAwardSafeAsync(string userId, string trigger, CancellationToken ct)
    {
        try
        {
            await CheckAndAwardAchievementsAsync(userId, trigger, ct);
        }
        catch
        {
            // Deliberately swallowed: a gamification failure must not fail a vocabulary
            // word, a forum post, a referral apply or a pronunciation attempt.
        }
    }

    // ── Leaderboard ──────────────────────────────────────────────────────

    public async Task<object> GetLeaderboardAsync(string? examTypeCode, string period, CancellationToken ct)
    {
        var ranked = await BuildRankedLeaderboardAsync(examTypeCode, period, ct);

        return ranked.Take(100).Select(r => new
        {
            rank = r.Rank,
            displayName = r.DisplayName,
            xp = r.Xp,
            // Additive aliases — same value as `xp`/the learner's level, exposed
            // under the names the leaderboard UI reads. `xp` is retained so the
            // existing wire contract is unchanged.
            totalXp = r.Xp,
            level = r.Level,
            examTypeCode = r.ExamTypeCode,
            period = NormalisePeriod(period),
            periodStart = PeriodStart(period)
        });
    }

    public async Task<object> GetLeaderboardPositionAsync(string userId, string? examTypeCode, string period, CancellationToken ct)
    {
        var ranked = await BuildRankedLeaderboardAsync(examTypeCode, period, ct);
        var mine = ranked.FirstOrDefault(r => r.UserId == userId);

        if (mine is null)
        {
            // Not opted in (or no XP row yet) — no rank to report. Surface the
            // learner's own XP anyway so the UI can show progress before opt-in.
            var ownXp = await db.LearnerXPs.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId, ct);
            var own = ownXp is null ? 0L : PeriodXp(ownXp, period);
            return new { rank = (int?)null, xp = own, totalXp = own, level = ownXp?.Level ?? 1, optedIn = false };
        }

        return new { rank = (int?)mine.Rank, xp = mine.Xp, totalXp = mine.Xp, level = mine.Level, optedIn = true };
    }

    /// <summary>
    /// Builds the live leaderboard ordered by the requested period's XP.
    /// <para>
    /// <see cref="LeaderboardEntry"/> is used purely as the opt-in registry —
    /// its own XP/Rank columns are never maintained (rows are only ever written
    /// by <see cref="SetLeaderboardOptInAsync"/>), so the standings are computed
    /// from <see cref="LearnerXP"/>, which is the table
    /// <see cref="AwardXpAsync"/> actually updates. Opt-in is a single
    /// learner-level choice, so <c>Period</c> is deliberately not part of the
    /// gate — otherwise "monthly"/"alltime" would always be empty, because
    /// opt-in only ever writes a "weekly" row.
    /// </para>
    /// </summary>
    private async Task<List<RankedLeaderboardRow>> BuildRankedLeaderboardAsync(
        string? examTypeCode, string period, CancellationToken ct)
    {
        var registry = db.LeaderboardEntries.AsNoTracking().Where(e => e.OptedIn);
        if (!string.IsNullOrEmpty(examTypeCode))
            registry = registry.Where(e => e.ExamTypeCode == examTypeCode);

        var joined = await (from e in registry
                            join x in db.LearnerXPs.AsNoTracking() on e.UserId equals x.UserId
                            select new
                            {
                                e.UserId,
                                e.DisplayName,
                                e.ExamTypeCode,
                                Xp = x,
                            }).ToListAsync(ct);

        var periodIsAllTime = string.Equals(NormalisePeriod(period), "alltime", StringComparison.OrdinalIgnoreCase);
        Dictionary<string, long> readingXpByUser = [];
        if (periodIsAllTime && joined.Count > 0)
        {
            var userIds = joined.Select(r => r.UserId).ToHashSet();
            readingXpByUser = await db.LearnerXps.AsNoTracking()
                .Where(x => userIds.Contains(x.UserId))
                .ToDictionaryAsync(x => x.UserId, x => (long)x.TotalXp, ct);
        }

        return joined
            // A learner may hold more than one registry row (one per exam type);
            // collapse to a single standing so they cannot occupy two ranks.
            .GroupBy(r => r.UserId)
            .Select(g => g.First())
            .Select(r =>
            {
                var reading = readingXpByUser.GetValueOrDefault(r.UserId);
                var total = periodIsAllTime ? r.Xp.TotalXP + reading : PeriodXp(r.Xp, period);
                return new RankedLeaderboardRow(
                    r.UserId,
                    r.DisplayName,
                    r.ExamTypeCode,
                    total,
                    periodIsAllTime ? ComputeLevel(r.Xp.TotalXP + reading) : r.Xp.Level,
                    0);
            })
            .OrderByDescending(r => r.Xp)
            .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select((r, i) => r with { Rank = i + 1 })
            .ToList();
    }

    private sealed record RankedLeaderboardRow(
        string UserId, string DisplayName, string ExamTypeCode, long Xp, int Level, int Rank);

    private static string NormalisePeriod(string? period) =>
        (period ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "monthly" => "monthly",
            "alltime" => "alltime",
            _ => "weekly",
        };

    /// <summary>
    /// Reads the bucket for the requested period, treating a bucket whose
    /// period has already rolled over as zero. <see cref="AwardXpAsync"/> resets
    /// the weekly/monthly buckets lazily (only when XP is next awarded), so a
    /// learner who has been inactive since last week still carries last week's
    /// <c>WeeklyXP</c> on their row — counting it would rank stale scores.
    /// </summary>
    private static long PeriodXp(LearnerXP xp, string period)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return NormalisePeriod(period) switch
        {
            "alltime" => xp.TotalXP,
            "monthly" => xp.MonthStartDate.Year == today.Year && xp.MonthStartDate.Month == today.Month
                ? xp.MonthlyXP
                : 0L,
            _ => xp.WeekStartDate >= today.AddDays(-(int)today.DayOfWeek) ? xp.WeeklyXP : 0L,
        };
    }

    private static DateOnly PeriodStart(string period)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return NormalisePeriod(period) switch
        {
            "alltime" => DateOnly.MinValue,
            "monthly" => new DateOnly(today.Year, today.Month, 1),
            _ => today.AddDays(-(int)today.DayOfWeek),
        };
    }

    /// <summary>
    /// Records the learner's leaderboard opt-in choice. The row acts purely as
    /// the opt-in registry (see <see cref="BuildRankedLeaderboardAsync"/>): the
    /// <c>XP</c>, <c>Rank</c> and <c>PeriodStart</c> columns are vestigial —
    /// nothing maintains them and no read path consults them. Standings come
    /// from <see cref="LearnerXP"/>.
    /// </summary>
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
                Rank = 0,
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

    /// <summary>
    /// XP and streaks are also written by the Reading pathway (LearnerXp /
    /// StreakRecord) and the engagement mirror on Users. Merge those sources at
    /// read time so the top-bar badges, /achievements and the leaderboard
    /// reflect all learner activity, not just grammar/study-plan awards.
    /// </summary>
    private async Task<long> GetReadingTotalXpAsync(string userId, CancellationToken ct) =>
        await db.LearnerXps.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => (long?)x.TotalXp)
            .SingleOrDefaultAsync(ct) ?? 0L;

    private async Task<StreakRecord?> GetLatestStreakRecordAsync(string userId, CancellationToken ct) =>
        await db.StreakRecords.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.Date)
            .FirstOrDefaultAsync(ct);

    private static int ComputeLevel(long totalXp)
    {
        // Level thresholds: 1=0, 2=100, 3=300, 4=600, 5=1000, 6=1500, 7=2100, 8=2800, ...
        // Formula: level = floor((1 + sqrt(1 + 8*xp/100)) / 2)
        if (totalXp <= 0) return 1;
        var level = (int)Math.Floor((1 + Math.Sqrt(1 + 8.0 * totalXp / 100)) / 2);
        return Math.Min(level, 100);
    }

    private static object MapXp(LearnerXP xp, long? totalXP = null, int? level = null)
    {
        var total = totalXP ?? xp.TotalXP;
        var lvl = level ?? xp.Level;
        return new
        {
            totalXP = total,
            weeklyXP = xp.WeeklyXP,
            monthlyXP = xp.MonthlyXP,
            level = lvl,
            nextLevelXP = ComputeLevelThreshold(lvl + 1),
            currentLevelXP = ComputeLevelThreshold(lvl)
        };
    }

    private static long ComputeLevelThreshold(int level)
    {
        if (level <= 1) return 0;
        return (long)(100 * level * (level - 1) / 2);
    }

    private static object MapStreak(LearnerStreak s, int? currentStreak = null, int? longestStreak = null, DateOnly? lastActiveDate = null) => new
    {
        currentStreak = currentStreak ?? s.CurrentStreak,
        longestStreak = longestStreak ?? s.LongestStreak,
        lastActiveDate = lastActiveDate ?? s.LastActiveDate,
        streakFreezesAvailable = s.StreakFreezeCount - s.StreakFreezeUsedCount
    };

    /// <summary>
    /// Criteria types this service can actually evaluate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="MeetsCriteria"/> receives only XP, streak, attempt count and vocabulary counts.
    /// The seed data also ships criteria that need data this service is never handed — grades per
    /// sub-test, mock-exam completions, spaced-review and pronunciation-drill sessions, forum
    /// posts, converted referrals, consecutive score improvement — and those hit the switch's
    /// <c>_ =&gt; false</c> arm. Ten seeded achievements could therefore never be awarded, and
    /// because <see cref="GetAchievementsAsync"/> reports every active achievement with
    /// <c>unlocked: false</c> and no other signal, a learner saw them permanently locked with
    /// nothing to explain why.
    /// </para>
    /// <para>
    /// This set is the honest description of what the evaluator covers. It exists so the API can
    /// say which achievements are measured rather than presenting an unevaluable one as merely
    /// unearned, and so an admin diagnosing "this achievement never fires" has the answer in code
    /// instead of having to read the switch. Adding a criterion type here is only correct once
    /// <see cref="MeetsCriteria"/> genuinely evaluates it AND the caller supplies its inputs.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> EvaluableCriteriaTypes = new(StringComparer.Ordinal)
    {
        "attempt_count",
        "streak_days",
        "total_xp",
        "vocab_added",
        "vocab_mastered",
        "forum_posts",
        "referrals_converted",
        "pronunciation_drills",
    };

    /// <summary>
    /// True when this achievement's criteria can actually be evaluated today. Unparseable or
    /// unrecognised criteria read as not evaluable, matching <see cref="MeetsCriteria"/>'s
    /// fail-closed behaviour.
    /// </summary>
    private static bool IsEvaluable(Achievement ach)
    {
        try
        {
            var type = System.Text.Json.JsonDocument.Parse(ach.CriteriaJson)
                .RootElement.GetProperty("type").GetString();
            return type is not null && EvaluableCriteriaTypes.Contains(type);
        }
        catch
        {
            return false;
        }
    }

    private static bool MeetsCriteria(
        Achievement ach,
        LearnerXP? xp,
        int currentStreak,
        int attemptCount,
        int vocabAdded,
        int vocabMastered,
        int forumPosts,
        int referralsConverted,
        int pronunciationDrills)
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
                "forum_posts" => forumPosts >= criteria.GetProperty("threshold").GetInt32(),
                "referrals_converted" => referralsConverted >= criteria.GetProperty("threshold").GetInt32(),
                "pronunciation_drills" => pronunciationDrills >= criteria.GetProperty("threshold").GetInt32(),
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
