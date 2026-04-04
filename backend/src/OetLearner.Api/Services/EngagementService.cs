using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Service for managing learner engagement signals: streaks, practice tracking,
/// and activity logging. Called after any practice completion event.
/// </summary>
public class EngagementService(LearnerDbContext db)
{
    /// <summary>
    /// Updates the learner's streak and practice statistics after a practice session.
    /// Logic:
    /// - If LastPracticeDate is today → no streak change, only increment stats
    /// - If LastPracticeDate is yesterday → CurrentStreak++
    /// - If LastPracticeDate is older → CurrentStreak = 1 (streak broken)
    /// - Always update LongestStreak if CurrentStreak exceeds it
    /// </summary>
    public async Task UpdateStreakAsync(string userId, int practiceMinutes, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return;

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var lastPractice = user.LastPracticeDate.HasValue
            ? DateOnly.FromDateTime(user.LastPracticeDate.Value.UtcDateTime)
            : (DateOnly?)null;

        if (lastPractice == today)
        {
            // Same day — no streak change, just increment stats
        }
        else if (lastPractice == today.AddDays(-1))
        {
            // Yesterday — extend streak
            user.CurrentStreak++;
        }
        else
        {
            // Gap — streak broken, start fresh
            user.CurrentStreak = 1;
        }

        if (user.CurrentStreak > user.LongestStreak)
        {
            user.LongestStreak = user.CurrentStreak;
        }

        user.LastPracticeDate = now;
        user.TotalPracticeMinutes += Math.Max(0, practiceMinutes);
        user.TotalPracticeSessions++;

        // Update weekly activity for today
        UpdateWeeklyActivity(user, today);

        user.LastActiveAt = now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Records a practice session without updating the streak (e.g., for passive activities).
    /// </summary>
    public async Task RecordActivityAsync(string userId, int durationMinutes, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return;

        user.TotalPracticeMinutes += Math.Max(0, durationMinutes);
        user.LastActiveAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Uses a streak freeze to prevent losing a streak for one day.
    /// Returns true if the freeze was applied, false if no freeze was available.
    /// </summary>
    public async Task<bool> UseStreakFreezeAsync(string userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return false;

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var lastPractice = user.LastPracticeDate.HasValue
            ? DateOnly.FromDateTime(user.LastPracticeDate.Value.UtcDateTime)
            : (DateOnly?)null;

        // Only applicable if the last practice was 2+ days ago (yesterday would still maintain streak)
        if (lastPractice is null || lastPractice >= today.AddDays(-1))
            return false;

        // Freeze: set LastPracticeDate to yesterday to maintain streak continuity
        user.LastPracticeDate = now.AddDays(-1);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Gets a target-date risk assessment for the learner based on
    /// current readiness, weeks remaining, and study velocity.
    /// </summary>
    public async Task<TargetDateRisk> CalculateTargetDateRiskAsync(string userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return new TargetDateRisk("unknown", 0, 0, "No user found.", []);

        var goal = await db.Goals.AsNoTracking()
            .FirstOrDefaultAsync(g => g.UserId == userId, ct);

        if (goal?.TargetExamDate is null)
            return new TargetDateRisk("unknown", 0, 0, "No target exam date set.", []);

        var examDate = goal.TargetExamDate.Value;
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var daysRemaining = examDate.DayNumber - today.DayNumber;
        var weeksRemaining = Math.Max(0, daysRemaining / 7);

        // Calculate study velocity (minutes per week over last 4 weeks)
        var fourWeeksAgo = DateTimeOffset.UtcNow.AddDays(-28);
        var recentAttempts = await db.Attempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.SubmittedAt >= fourWeeksAgo)
            .CountAsync(ct);

        var weeklyPaceMinutes = user.TotalPracticeSessions > 0
            ? user.TotalPracticeMinutes / Math.Max(1, (int)Math.Ceiling((DateTimeOffset.UtcNow - (user.LastPracticeDate ?? DateTimeOffset.UtcNow)).TotalDays / 7.0))
            : 0;

        // Risk factors
        var factors = new List<RiskFactor>();

        if (daysRemaining < 0)
        {
            factors.Add(new RiskFactor("exam_passed", "critical", "Your exam date has already passed."));
        }
        else if (weeksRemaining <= 2)
        {
            factors.Add(new RiskFactor("time_critical", "high", $"Only {daysRemaining} days remaining until your exam."));
        }
        else if (weeksRemaining <= 4)
        {
            factors.Add(new RiskFactor("time_limited", "moderate", $"{weeksRemaining} weeks remaining — focus on weakest areas."));
        }

        if (user.CurrentStreak == 0)
        {
            factors.Add(new RiskFactor("no_streak", "moderate", "No active practice streak. Consistency improves outcomes."));
        }

        if (weeklyPaceMinutes < 120 && weeksRemaining > 0) // Less than 2 hours per week
        {
            factors.Add(new RiskFactor("low_study_pace", "moderate", $"Current pace is ~{weeklyPaceMinutes} min/week. Aim for 5+ hours/week."));
        }

        if (recentAttempts < 3)
        {
            factors.Add(new RiskFactor("low_recent_activity", "moderate", $"Only {recentAttempts} attempts in the last 4 weeks."));
        }

        // Calculate probability (simplified model)
        var baseScore = 60;
        if (weeksRemaining >= 12) baseScore += 15;
        else if (weeksRemaining >= 8) baseScore += 10;
        else if (weeksRemaining >= 4) baseScore += 5;
        else if (weeksRemaining <= 1) baseScore -= 20;

        if (user.CurrentStreak >= 7) baseScore += 10;
        else if (user.CurrentStreak >= 3) baseScore += 5;

        if (weeklyPaceMinutes >= 300) baseScore += 10;
        else if (weeklyPaceMinutes >= 120) baseScore += 5;
        else if (weeklyPaceMinutes < 60) baseScore -= 10;

        if (recentAttempts >= 10) baseScore += 5;

        var probability = Math.Clamp(baseScore, 5, 95);

        var riskLevel = probability switch
        {
            >= 70 => "low",
            >= 45 => "moderate",
            _ => "high"
        };

        var summary = riskLevel switch
        {
            "low" => $"On track with {weeksRemaining} weeks to go. Maintain your current pace.",
            "moderate" => $"Some risk areas to address. {weeksRemaining} weeks remaining — increase study intensity.",
            "high" => $"Significant risk of not meeting targets. {weeksRemaining} weeks left — urgent action needed.",
            _ => "Unable to assess risk."
        };

        return new TargetDateRisk(riskLevel, probability, weeksRemaining, summary, factors);
    }

    private static void UpdateWeeklyActivity(LearnerUser user, DateOnly today)
    {
        var dayOfWeek = (int)today.DayOfWeek;
        // Convert Sunday=0 to Monday-first: Mon=0, Tue=1, ..., Sun=6
        var mondayIndex = dayOfWeek == 0 ? 6 : dayOfWeek - 1;

        var activity = JsonSupport.Deserialize<bool[]>(user.WeeklyActivityJson ?? "[]", new bool[7]);
        if (activity.Length < 7)
        {
            var expanded = new bool[7];
            Array.Copy(activity, expanded, Math.Min(activity.Length, 7));
            activity = expanded;
        }

        activity[mondayIndex] = true;
        user.WeeklyActivityJson = JsonSupport.Serialize(activity);
    }
}

public record TargetDateRisk(
    string RiskLevel,
    int ReadinessProbability,
    int WeeksRemaining,
    string Summary,
    List<RiskFactor> Factors);

public record RiskFactor(
    string FactorId,
    string Severity,
    string Description);
