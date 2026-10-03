using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// The gamification read paths merge the parallel stores learners actually
/// write to (Reading pathway `LearnerXp`/`StreakRecord` + the engagement
/// mirror on Users) into the grammar/study-plan `LearnerXP`/`LearnerStreak`
/// rows — so the header badges, /achievements and the leaderboard reflect all
/// learner activity, not just grammar awards.
/// </summary>
public class GamificationServiceTests
{
    private static LearnerDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static JsonElement AsJson(object payload) =>
        JsonSerializer.SerializeToElement(payload);

    private static DateOnly CurrentWeekStart()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return today.AddDays(-(int)today.DayOfWeek);
    }

    [Fact]
    public async Task GetXpAsync_merges_reading_xp_into_the_total_and_recomputes_level()
    {
        var db = BuildDb();
        db.LearnerXPs.Add(new LearnerXP
        {
            UserId = "u1",
            TotalXP = 700,
            WeeklyXP = 100,
            MonthlyXP = 700,
            Level = 4,
            WeekStartDate = CurrentWeekStart(),
            MonthStartDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });
        // Reading pathway store (separate table: ReadingLearnerXps).
        db.LearnerXps.Add(new LearnerXp
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            TotalXp = 300,
            CurrentLevel = 2,
            XpToNextLevel = 200,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var svc = new GamificationService(db);

        var json = AsJson(await svc.GetXpAsync("u1", CancellationToken.None));

        Assert.Equal(1000, json.GetProperty("totalXP").GetInt64());
        // Level thresholds: 5 = 1000 XP.
        Assert.Equal(5, json.GetProperty("level").GetInt32());
        Assert.Equal(1000, json.GetProperty("currentLevelXP").GetInt64());
        Assert.Equal(1500, json.GetProperty("nextLevelXP").GetInt64());
        // Period buckets stay grammar-scoped: reading XP has no weekly row.
        Assert.Equal(100, json.GetProperty("weeklyXP").GetInt64());
    }

    [Fact]
    public async Task GetStreakAsync_merges_the_streak_record_and_engagement_sources()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var db = BuildDb();
        db.LearnerStreaks.Add(new LearnerStreak
        {
            UserId = "u1",
            CurrentStreak = 3,
            LongestStreak = 10,
            LastActiveDate = today.AddDays(-1),
        });
        db.StreakRecords.Add(new StreakRecord
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            Date = today,
            HasActivity = true,
            QuestionsAnsweredToday = 9,
            CurrentStreak = 8,
            LongestStreak = 12,
        });
        db.Users.Add(new LearnerUser
        {
            Id = "u1",
            DisplayName = "Aisha",
            Email = "aisha@example.com",
            CurrentStreak = 5,
            LongestStreak = 7,
            LastPracticeDate = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var svc = new GamificationService(db);

        var json = AsJson(await svc.GetStreakAsync("u1", CancellationToken.None));

        Assert.Equal(8, json.GetProperty("currentStreak").GetInt32());
        Assert.Equal(12, json.GetProperty("longestStreak").GetInt32());
        Assert.Equal(today, DateOnly.Parse(json.GetProperty("lastActiveDate").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task GetStreakAsync_reports_the_zeroed_row_when_no_source_has_activity()
    {
        var db = BuildDb();
        var svc = new GamificationService(db);

        var json = AsJson(await svc.GetStreakAsync("nobody", CancellationToken.None));

        Assert.Equal(0, json.GetProperty("currentStreak").GetInt32());
        Assert.Equal(0, json.GetProperty("longestStreak").GetInt32());
    }

    [Fact]
    public async Task CheckAndAwardAchievementsAsync_unlocks_a_streak_earned_outside_the_grammar_store()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var db = BuildDb();
        db.Achievements.Add(new Achievement
        {
            Id = "ach-streak7",
            Code = "streak-7",
            Label = "Week-long flame",
            Description = "Reach a 7 day streak.",
            Category = "streak",
            XPReward = 0,
            CriteriaJson = "{\"type\":\"streak_days\",\"threshold\":7}",
            SortOrder = 1,
            Status = "active",
        });
        // Grammar streak never reached the threshold — the reading pathway did.
        db.LearnerStreaks.Add(new LearnerStreak
        {
            UserId = "u1",
            CurrentStreak = 2,
            LongestStreak = 2,
            LastActiveDate = today.AddDays(-1),
        });
        db.StreakRecords.Add(new StreakRecord
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            Date = today,
            HasActivity = true,
            QuestionsAnsweredToday = 9,
            CurrentStreak = 8,
            LongestStreak = 8,
        });
        await db.SaveChangesAsync();
        var svc = new GamificationService(db);

        await svc.CheckAndAwardAchievementsAsync("u1", "reading_activity", CancellationToken.None);

        Assert.True(await db.LearnerAchievements.AnyAsync(
            la => la.UserId == "u1" && la.AchievementId == "ach-streak7"));
    }

    [Fact]
    public async Task GetLeaderboardAsync_alltime_adds_reading_xp_but_period_buckets_stay_grammar_only()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var db = BuildDb();
        db.LeaderboardEntries.Add(new LeaderboardEntry
        {
            Id = Guid.NewGuid(),
            UserId = "learner-a",
            DisplayName = "Aisha",
            ExamTypeCode = "OET",
            Period = "weekly",
            PeriodStart = CurrentWeekStart(),
            XP = 0,
            Rank = 0,
            OptedIn = true,
        });
        db.LeaderboardEntries.Add(new LeaderboardEntry
        {
            Id = Guid.NewGuid(),
            UserId = "learner-b",
            DisplayName = "Bilal",
            ExamTypeCode = "OET",
            Period = "weekly",
            PeriodStart = CurrentWeekStart(),
            XP = 0,
            Rank = 0,
            OptedIn = true,
        });
        db.LearnerXPs.Add(new LearnerXP
        {
            UserId = "learner-a",
            TotalXP = 500,
            WeeklyXP = 500,
            MonthlyXP = 500,
            Level = 3,
            WeekStartDate = CurrentWeekStart(),
            MonthStartDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });
        db.LearnerXPs.Add(new LearnerXP
        {
            UserId = "learner-b",
            TotalXP = 600,
            WeeklyXP = 600,
            MonthlyXP = 600,
            Level = 3,
            WeekStartDate = CurrentWeekStart(),
            MonthStartDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });
        db.LearnerXps.Add(new LearnerXp
        {
            Id = Guid.NewGuid(),
            UserId = "learner-a",
            TotalXp = 200,
            CurrentLevel = 2,
            XpToNextLevel = 100,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var svc = new GamificationService(db);

        var allTime = JsonSerializer.SerializeToElement(
            await svc.GetLeaderboardAsync("OET", "alltime", CancellationToken.None));
        Assert.Equal("Aisha", allTime[0].GetProperty("displayName").GetString());
        Assert.Equal(700, allTime[0].GetProperty("xp").GetInt64());
        Assert.Equal("Bilal", allTime[1].GetProperty("displayName").GetString());
        Assert.Equal(600, allTime[1].GetProperty("xp").GetInt64());

        // Weekly/monthly buckets only track grammar awards — reading XP must not
        // leak into a period the reading store does not maintain.
        var weekly = JsonSerializer.SerializeToElement(
            await svc.GetLeaderboardAsync("OET", "weekly", CancellationToken.None));
        Assert.Equal("learner-b", weekly[0].GetProperty("displayName").GetString());
        Assert.Equal("learner-a", weekly[1].GetProperty("displayName").GetString());
    }
}
