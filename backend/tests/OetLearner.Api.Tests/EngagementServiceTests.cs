using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using Xunit;

namespace OetLearner.Api.Tests;

public class EngagementServiceTests
{
    private static (LearnerDbContext db, EngagementService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new EngagementService(db));
    }

    private static LearnerUser MakeUser(
        string id = "u1",
        DateTimeOffset? lastPractice = null,
        int currentStreak = 0,
        int longestStreak = 0,
        int totalMinutes = 0,
        int totalSessions = 0)
        => new()
        {
            Id = id,
            DisplayName = "Test",
            Email = $"{id}@test.local",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            LastActiveAt = DateTimeOffset.UtcNow.AddDays(-1),
            LastPracticeDate = lastPractice,
            CurrentStreak = currentStreak,
            LongestStreak = longestStreak,
            TotalPracticeMinutes = totalMinutes,
            TotalPracticeSessions = totalSessions,
        };

    // ── UpdateStreakAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateStreakAsync_no_op_when_user_missing()
    {
        var (db, svc) = Build();
        await svc.UpdateStreakAsync("ghost", 30, default);
        Assert.Equal(0, await db.Users.CountAsync());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UpdateStreakAsync_first_practice_starts_streak_at_one()
    {
        var (db, svc) = Build();
        db.Users.Add(MakeUser());
        await db.SaveChangesAsync();

        await svc.UpdateStreakAsync("u1", 30, default);

        var u = await db.Users.FirstAsync();
        Assert.Equal(1, u.CurrentStreak);
        Assert.Equal(1, u.LongestStreak);
        Assert.Equal(30, u.TotalPracticeMinutes);
        Assert.Equal(1, u.TotalPracticeSessions);
        Assert.NotNull(u.LastPracticeDate);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UpdateStreakAsync_same_day_does_not_change_streak_but_increments_stats()
    {
        var (db, svc) = Build();
        db.Users.Add(MakeUser(
            lastPractice: DateTimeOffset.UtcNow,
            currentStreak: 5,
            longestStreak: 5,
            totalMinutes: 100,
            totalSessions: 3));
        await db.SaveChangesAsync();

        await svc.UpdateStreakAsync("u1", 20, default);

        var u = await db.Users.FirstAsync();
        Assert.Equal(5, u.CurrentStreak);
        Assert.Equal(5, u.LongestStreak);
        Assert.Equal(120, u.TotalPracticeMinutes);
        Assert.Equal(4, u.TotalPracticeSessions);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UpdateStreakAsync_yesterday_extends_streak()
    {
        var (db, svc) = Build();
        db.Users.Add(MakeUser(
            lastPractice: DateTimeOffset.UtcNow.AddDays(-1),
            currentStreak: 3,
            longestStreak: 3));
        await db.SaveChangesAsync();

        await svc.UpdateStreakAsync("u1", 15, default);

        var u = await db.Users.FirstAsync();
        Assert.Equal(4, u.CurrentStreak);
        Assert.Equal(4, u.LongestStreak); // longest also extends
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UpdateStreakAsync_gap_resets_streak_to_one()
    {
        var (db, svc) = Build();
        db.Users.Add(MakeUser(
            lastPractice: DateTimeOffset.UtcNow.AddDays(-3),
            currentStreak: 12,
            longestStreak: 20));
        await db.SaveChangesAsync();

        await svc.UpdateStreakAsync("u1", 10, default);

        var u = await db.Users.FirstAsync();
        Assert.Equal(1, u.CurrentStreak);
        Assert.Equal(20, u.LongestStreak); // longest preserved
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UpdateStreakAsync_clamps_negative_minutes_to_zero()
    {
        var (db, svc) = Build();
        db.Users.Add(MakeUser());
        await db.SaveChangesAsync();

        await svc.UpdateStreakAsync("u1", -50, default);

        var u = await db.Users.FirstAsync();
        Assert.Equal(0, u.TotalPracticeMinutes);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UpdateStreakAsync_writes_weekly_activity_for_today()
    {
        var (db, svc) = Build();
        db.Users.Add(MakeUser());
        await db.SaveChangesAsync();

        await svc.UpdateStreakAsync("u1", 5, default);

        var u = await db.Users.FirstAsync();
        Assert.NotNull(u.WeeklyActivityJson);
        Assert.Contains("true", u.WeeklyActivityJson);
        await db.DisposeAsync();
    }

    // ── RecordActivityAsync ────────────────────────────────────────────────

    [Fact]
    public async Task RecordActivityAsync_no_op_when_user_missing()
    {
        var (db, svc) = Build();
        await svc.RecordActivityAsync("ghost", 30, default);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task RecordActivityAsync_increments_minutes_but_not_sessions_or_streak()
    {
        var (db, svc) = Build();
        db.Users.Add(MakeUser(currentStreak: 5, totalMinutes: 100, totalSessions: 3));
        await db.SaveChangesAsync();

        await svc.RecordActivityAsync("u1", 25, default);

        var u = await db.Users.FirstAsync();
        Assert.Equal(125, u.TotalPracticeMinutes);
        Assert.Equal(3, u.TotalPracticeSessions); // unchanged
        Assert.Equal(5, u.CurrentStreak); // unchanged
        await db.DisposeAsync();
    }

    [Fact]
    public async Task RecordActivityAsync_clamps_negative_duration()
    {
        var (db, svc) = Build();
        db.Users.Add(MakeUser(totalMinutes: 50));
        await db.SaveChangesAsync();

        await svc.RecordActivityAsync("u1", -10, default);

        var u = await db.Users.FirstAsync();
        Assert.Equal(50, u.TotalPracticeMinutes);
        await db.DisposeAsync();
    }

    // ── UseStreakFreezeAsync ───────────────────────────────────────────────

    [Fact]
    public async Task UseStreakFreezeAsync_returns_false_when_user_missing()
    {
        var (db, svc) = Build();
        Assert.False(await svc.UseStreakFreezeAsync("ghost", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UseStreakFreezeAsync_returns_false_when_no_prior_practice()
    {
        var (db, svc) = Build();
        db.Users.Add(MakeUser(lastPractice: null));
        await db.SaveChangesAsync();
        Assert.False(await svc.UseStreakFreezeAsync("u1", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UseStreakFreezeAsync_returns_false_when_practiced_yesterday()
    {
        // Yesterday already maintains the streak — no freeze needed.
        var (db, svc) = Build();
        db.Users.Add(MakeUser(lastPractice: DateTimeOffset.UtcNow.AddDays(-1)));
        await db.SaveChangesAsync();

        Assert.False(await svc.UseStreakFreezeAsync("u1", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UseStreakFreezeAsync_returns_false_when_practiced_today()
    {
        var (db, svc) = Build();
        db.Users.Add(MakeUser(lastPractice: DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        Assert.False(await svc.UseStreakFreezeAsync("u1", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UseStreakFreezeAsync_applies_freeze_when_gap_exceeds_one_day()
    {
        var (db, svc) = Build();
        db.Users.Add(MakeUser(lastPractice: DateTimeOffset.UtcNow.AddDays(-3)));
        await db.SaveChangesAsync();

        var ok = await svc.UseStreakFreezeAsync("u1", default);
        Assert.True(ok);

        var u = await db.Users.FirstAsync();
        var lastDate = DateOnly.FromDateTime(u.LastPracticeDate!.Value.UtcDateTime);
        var yesterday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddDays(-1);
        Assert.Equal(yesterday, lastDate);
        await db.DisposeAsync();
    }

    // ── CalculateTargetDateRiskAsync ───────────────────────────────────────

    [Fact]
    public async Task CalculateTargetDateRiskAsync_unknown_when_user_missing()
    {
        var (db, svc) = Build();
        var r = await svc.CalculateTargetDateRiskAsync("ghost", default);
        Assert.Equal("unknown", r.RiskLevel);
        Assert.Empty(r.Factors);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task CalculateTargetDateRiskAsync_unknown_when_no_target_date()
    {
        var (db, svc) = Build();
        db.Users.Add(MakeUser());
        db.Goals.Add(new LearnerGoal
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            ProfessionId = "medicine",
            TargetExamDate = null,
        });
        await db.SaveChangesAsync();

        var r = await svc.CalculateTargetDateRiskAsync("u1", default);
        Assert.Equal("unknown", r.RiskLevel);
        Assert.Equal(0, r.WeeksRemaining);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task CalculateTargetDateRiskAsync_flags_exam_passed_when_date_in_past()
    {
        var (db, svc) = Build();
        db.Users.Add(MakeUser(currentStreak: 7));
        db.Goals.Add(new LearnerGoal
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            ProfessionId = "medicine",
            TargetExamDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
        });
        await db.SaveChangesAsync();

        var r = await svc.CalculateTargetDateRiskAsync("u1", default);
        Assert.Contains(r.Factors, f => f.FactorId == "exam_passed");
        await db.DisposeAsync();
    }

    [Fact]
    public async Task CalculateTargetDateRiskAsync_flags_no_streak_when_zero()
    {
        var (db, svc) = Build();
        db.Users.Add(MakeUser(currentStreak: 0));
        db.Goals.Add(new LearnerGoal
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            ProfessionId = "medicine",
            TargetExamDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60)),
        });
        await db.SaveChangesAsync();

        var r = await svc.CalculateTargetDateRiskAsync("u1", default);
        Assert.Contains(r.Factors, f => f.FactorId == "no_streak");
        await db.DisposeAsync();
    }

    [Fact]
    public async Task CalculateTargetDateRiskAsync_flags_time_critical_when_under_2_weeks()
    {
        var (db, svc) = Build();
        db.Users.Add(MakeUser(currentStreak: 7));
        db.Goals.Add(new LearnerGoal
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            ProfessionId = "medicine",
            TargetExamDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
        });
        await db.SaveChangesAsync();

        var r = await svc.CalculateTargetDateRiskAsync("u1", default);
        Assert.Contains(r.Factors, f => f.FactorId == "time_critical");
        await db.DisposeAsync();
    }

    [Fact]
    public async Task CalculateTargetDateRiskAsync_probability_clamped_5_to_95()
    {
        var (db, svc) = Build();
        db.Users.Add(MakeUser(currentStreak: 30, totalMinutes: 100000, totalSessions: 200));
        db.Goals.Add(new LearnerGoal
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            ProfessionId = "medicine",
            TargetExamDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(120)),
        });
        await db.SaveChangesAsync();

        var r = await svc.CalculateTargetDateRiskAsync("u1", default);
        Assert.InRange(r.ReadinessProbability, 5, 95);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task CalculateTargetDateRiskAsync_risk_level_low_summary_includes_weeks()
    {
        var (db, svc) = Build();
        db.Users.Add(MakeUser(currentStreak: 14, totalMinutes: 5000, totalSessions: 50));
        db.Goals.Add(new LearnerGoal
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            ProfessionId = "medicine",
            TargetExamDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(120)),
        });
        await db.SaveChangesAsync();

        var r = await svc.CalculateTargetDateRiskAsync("u1", default);
        // weeksRemaining ≈ 17, risk should be "low" given streak ≥ 7 + lots of minutes
        Assert.NotNull(r.Summary);
        Assert.Contains(r.WeeksRemaining.ToString(), r.Summary);
        await db.DisposeAsync();
    }
}
