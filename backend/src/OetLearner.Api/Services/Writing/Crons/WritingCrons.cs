using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing.Configuration;

namespace OetLearner.Api.Services.Writing.Crons;

/// <summary>Daily 02:00 UTC: generate next-day plan for every active learner.
/// Idempotent on (LearnerId, Date).</summary>
public sealed class WritingDailyPlanCron(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<WritingV2Options> options,
    ILogger<WritingDailyPlanCron> logger) : WritingCronBase(scopeFactory, clock, options, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        var now = Clock.GetUtcNow();
        if (now.Hour != 2) return;
        using var scope = ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var planner = scope.ServiceProvider.GetRequiredService<IWritingDailyPlanServiceV2>();
        var cutoff = now.AddDays(-7);
        var activeUserIds = await db.LearnerWritingProfiles.AsNoTracking()
            .Where(p => p.UpdatedAt >= cutoff)
            .Select(p => p.UserId)
            .ToListAsync(ct);
        foreach (var userId in activeUserIds)
        {
            try { await planner.GetTodayAsync(userId, ct); }
            catch (Exception ex) { Logger.LogWarning(ex, "Daily plan ensure failed for user {UserId}", userId); }
        }
    }
}

/// <summary>Daily 03:00 UTC: compute readiness per active learner.</summary>
public sealed class WritingReadinessCron(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<WritingV2Options> options,
    ILogger<WritingReadinessCron> logger) : WritingCronBase(scopeFactory, clock, options, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        var now = Clock.GetUtcNow();
        if (now.Hour != 3) return;
        using var scope = ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IWritingReadinessService>();
        var cutoff = now.AddDays(-30);
        var userIds = await db.WritingSubmissions.AsNoTracking()
            .Where(s => s.SubmittedAt >= cutoff)
            .Select(s => s.UserId)
            .Distinct()
            .ToListAsync(ct);
        foreach (var userId in userIds)
        {
            try { await service.ComputeForUserAsync(userId, ct); }
            catch (Exception ex) { Logger.LogWarning(ex, "Readiness compute failed for user {UserId}", userId); }
        }
    }
}

/// <summary>Every 5 minutes: drain queued+batched submissions through the V2
/// evaluation pipeline. Uses express-tier path if batch endpoint isn't wired.</summary>
public sealed class WritingBatchGradingCron(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<WritingV2Options> options,
    ILogger<WritingBatchGradingCron> logger) : WritingCronBase(scopeFactory, clock, options, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);

    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var pipeline = scope.ServiceProvider.GetRequiredService<IWritingSubmissionEvaluationPipeline>();
        var ids = await db.WritingSubmissions.AsNoTracking()
            .Where(s => s.Status == "queued" && s.GradingTier == "batched")
            .OrderBy(s => s.SubmittedAt)
            .Select(s => s.Id)
            .Take(25)
            .ToListAsync(ct);
        foreach (var id in ids)
        {
            try { await pipeline.EvaluateAsync(id, ct); }
            catch (Exception ex) { Logger.LogWarning(ex, "Batch grading failed for submission {Id}", id); }
        }
    }
}

/// <summary>Daily 04:00 UTC: pre-compute analytics aggregates per learner.
/// Stores nothing extra — calls ComputeAsync to warm any caches the analytics
/// service builds inside the same scope.</summary>
public sealed class WritingAnalyticsAggregationCron(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<WritingV2Options> options,
    ILogger<WritingAnalyticsAggregationCron> logger) : WritingCronBase(scopeFactory, clock, options, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        var now = Clock.GetUtcNow();
        if (now.Hour != 4) return;
        using var scope = ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var analytics = scope.ServiceProvider.GetRequiredService<IWritingAnalyticsServiceV2>();
        var cutoff = now.AddDays(-30);
        var userIds = await db.WritingSubmissions.AsNoTracking()
            .Where(s => s.SubmittedAt >= cutoff)
            .Select(s => s.UserId)
            .Distinct()
            .ToListAsync(ct);
        foreach (var userId in userIds)
        {
            try { _ = await analytics.ComputeAsync(userId, ct); }
            catch (Exception ex) { Logger.LogWarning(ex, "Analytics aggregation failed for user {UserId}", userId); }
        }
    }
}

/// <summary>Hourly: alert tutors if queue depth or oldest-wait exceed
/// configured thresholds. Auto-pause logic is enforced server-side; this
/// service just logs (and could fan-out via INotificationService when wired).</summary>
public sealed class WritingTutorQueueAlertCron(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<WritingV2Options> options,
    ILogger<WritingTutorQueueAlertCron> logger) : WritingCronBase(scopeFactory, clock, options, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = ScopeFactory.CreateScope();
        var tutorService = scope.ServiceProvider.GetRequiredService<IWritingTutorReviewService>();
        var status = await tutorService.GetQueueStatusAsync(ct);
        if (status.Paused)
        {
            Logger.LogWarning("Writing tutor queue auto-paused: {Reason} depth={Depth} oldest={Oldest}h",
                status.Reason, status.CurrentDepth, status.OldestWaitHours);
        }
    }
}

/// <summary>Daily 04:30 UTC: delete WritingDraftsV2 rows older than 30 days
/// and abandoned (un-submitted, expired) WritingDiagnosticSessions. Submitted
/// diagnostic sessions are retained for audit.</summary>
public sealed class WritingDraftCleanupCron(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<WritingV2Options> options,
    ILogger<WritingDraftCleanupCron> logger) : WritingCronBase(scopeFactory, clock, options, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        var now = Clock.GetUtcNow();
        if (now.Hour != 4 || now.Minute > 35) return;
        using var scope = ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var cutoff = now.AddDays(-30);
        var stale = await db.WritingDraftsV2.Where(d => d.LastSavedAt < cutoff).ToListAsync(ct);
        if (stale.Count > 0)
        {
            db.WritingDraftsV2.RemoveRange(stale);
            await db.SaveChangesAsync(ct);
            Logger.LogInformation("WritingDraftCleanupCron deleted {Count} drafts older than 30 days.", stale.Count);
        }

        // Abandoned diagnostic sessions: ExpiresAt past + never submitted.
        // Submitted ones stay around for audit/forensics.
        var abandonedSessions = await db.WritingDiagnosticSessions
            .Where(s => s.ExpiresAt < now && s.SubmissionId == null)
            .ToListAsync(ct);
        if (abandonedSessions.Count > 0)
        {
            db.WritingDiagnosticSessions.RemoveRange(abandonedSessions);
            await db.SaveChangesAsync(ct);
            Logger.LogInformation("WritingDraftCleanupCron deleted {Count} abandoned diagnostic sessions.", abandonedSessions.Count);
        }
    }
}

/// <summary>Hourly: scan recently-modified Writing content for invalid
/// state transitions. Logs offenders so admins can act.</summary>
public sealed class WritingContentAuditCron(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<WritingV2Options> options,
    ILogger<WritingContentAuditCron> logger) : WritingCronBase(scopeFactory, clock, options, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var since = Clock.GetUtcNow().AddHours(-1);
        var scenarioCount = await db.WritingScenarios.AsNoTracking().CountAsync(s => s.CreatedAt >= since, ct);
        var canonCount = await db.WritingCanonRules.AsNoTracking().CountAsync(r => r.UpdatedAt >= since, ct);
        if (scenarioCount + canonCount > 0)
        {
            Logger.LogInformation("Writing content audit tick: scenarios={Scenarios} canon={Canon}",
                scenarioCount, canonCount);
        }
    }
}
