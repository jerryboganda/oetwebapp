using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Planner;

/// <summary>
/// Background service that fires three planner reminder events at the right
/// local-time slots for each learner. Modelled on
/// <c>MockBookingReminderWorker</c> — uses the existing
/// <see cref="NotificationService"/> dedupe-key contract so the same reminder
/// never fires twice for the same learner on the same day.
///
///   • <c>LearnerDailyStudyReminder</c> — when local morning hour reached AND
///     today still has uncompleted plan items.
///   • <c>LearnerWeakSkillReminder</c>  — when local evening hour reached AND
///     today's weak-skill item is still NotStarted/InProgress.
///   • <c>LearnerStudyPlanDueReminder</c> — when a plan item's DueDate is
///     today AND now is past mid-afternoon AND it has not been completed.
///
/// Bucket strings ensure dedupe scopes are stable across restarts.
/// </summary>
public sealed class StudyPlanReminderWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<StudyPlanReminderWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private const int MaxLearnersPerTick = 500;
    private const int MorningLocalHour = 8;     // local 08:00–09:00 window
    private const int EveningLocalHour = 20;    // local 20:00–21:00 window
    private const int DueRemindLocalHour = 16;  // local 16:00 reminder if today's items still open

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(5, 30)), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var sent = await RunOnceAsync(stoppingToken);
                if (sent > 0)
                {
                    logger.LogInformation("StudyPlanReminderWorker dispatched {Count} reminders.", sent);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "StudyPlanReminderWorker tick failed.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var nowUtc = clock.GetUtcNow();

        // Pull only learners with an active plan and at least one open item today.
        var todayUtc = DateOnly.FromDateTime(nowUtc.UtcDateTime);
        var candidatePlanIds = await db.StudyPlans
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.GeneratedAt)
            .Take(MaxLearnersPerTick)
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (candidatePlanIds.Count == 0) return 0;

        var plans = await db.StudyPlans
            .AsNoTracking()
            .Where(p => candidatePlanIds.Contains(p.Id))
            .ToListAsync(ct);

        var userIds = plans.Select(p => p.UserId).Distinct().ToList();
        var users = await db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Timezone, ct);

        var dispatched = 0;

        foreach (var plan in plans)
        {
            if (!users.TryGetValue(plan.UserId, out var tz)) tz = "UTC";
            var localNow = ConvertToLocal(nowUtc, tz);
            var localHour = localNow.Hour;
            var localDate = DateOnly.FromDateTime(localNow.DateTime);

            // Fetch items in three windows in one shot to limit round trips.
            var items = await db.StudyPlanItems
                .AsNoTracking()
                .Where(i => i.StudyPlanId == plan.Id)
                .Where(i => i.DueDate == localDate)
                .Where(i => i.Status == StudyPlanItemStatus.NotStarted ||
                            i.Status == StudyPlanItemStatus.InProgress)
                .ToListAsync(ct);
            if (items.Count == 0) continue;

            // Morning daily reminder.
            if (localHour == MorningLocalHour)
            {
                dispatched += await TryFireAsync(
                    notifications,
                    NotificationEventKey.LearnerDailyStudyReminder,
                    plan.UserId,
                    plan.Id,
                    bucket: $"daily-{localDate:yyyyMMdd}",
                    title: "Today's plan is ready",
                    message: $"You have {items.Count} task(s) on your study plan today.",
                    ct);
            }

            // Per-item due-reminder for items still open at 16:00 local.
            if (localHour == DueRemindLocalHour)
            {
                foreach (var item in items)
                {
                    dispatched += await TryFireAsync(
                        notifications,
                        NotificationEventKey.LearnerStudyPlanDueReminder,
                        plan.UserId,
                        item.Id,
                        bucket: $"due-{localDate:yyyyMMdd}-{item.Id}",
                        title: "Plan task still open",
                        message: $"\"{item.Title}\" is due today.",
                        ct);
                }
            }

            // Evening weak-skill nudge.
            if (localHour == EveningLocalHour)
            {
                var weakItem = items.FirstOrDefault(i =>
                    !string.IsNullOrWhiteSpace(plan.WeakSkillFocus) &&
                    plan.WeakSkillFocus.Contains(i.SubtestCode, StringComparison.OrdinalIgnoreCase));
                if (weakItem is not null)
                {
                    dispatched += await TryFireAsync(
                        notifications,
                        NotificationEventKey.LearnerWeakSkillReminder,
                        plan.UserId,
                        weakItem.Id,
                        bucket: $"weak-{localDate:yyyyMMdd}",
                        title: "Don't forget your weak-skill focus",
                        message: $"\"{weakItem.Title}\" targets your highest-gap area.",
                        ct);
                }
            }
        }

        return dispatched;
    }

    private static async Task<int> TryFireAsync(
        NotificationService notifications,
        NotificationEventKey key,
        string userId,
        string resourceId,
        string bucket,
        string title,
        string message,
        CancellationToken ct)
    {
        try
        {
            await notifications.CreateForLearnerAsync(
                key,
                userId,
                "study_plan",
                resourceId,
                bucket,
                new Dictionary<string, object?>
                {
                    ["title"] = title,
                    ["message"] = message
                },
                ct);
            return 1;
        }
        catch (Exception)
        {
            // Dedupe collision or transient error — non-fatal, skip.
            return 0;
        }
    }

    private static DateTimeOffset ConvertToLocal(DateTimeOffset utc, string timezoneId)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            return TimeZoneInfo.ConvertTime(utc, tz);
        }
        catch
        {
            return utc;
        }
    }
}
