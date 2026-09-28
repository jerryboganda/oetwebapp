using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public partial class BackgroundJobProcessor
{
    /// <summary>
    /// T5 — enqueue a single <see cref="JobType.PrivateSpeakingNoShowSweep"/> job,
    /// skipping if one is already queued/processing. The BackgroundJobs queue is
    /// the source of truth, so this stays idempotent across multiple API replicas.
    /// </summary>
    private static async Task EnqueuePrivateSpeakingNoShowSweepJobAsync(
        LearnerDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var alreadyQueued = await db.BackgroundJobs
            .AsNoTracking()
            .AnyAsync(j => j.Type == JobType.PrivateSpeakingNoShowSweep
                && (j.State == AsyncState.Queued || j.State == AsyncState.Processing),
                cancellationToken);
        if (alreadyQueued) return;

        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"jb-ps-noshow-sweep-{Guid.NewGuid():N}",
            Type = JobType.PrivateSpeakingNoShowSweep,
            State = AsyncState.Queued,
            StatusReasonCode = "queued",
            StatusMessage = "Private Speaking no-show sweep queued.",
            CreatedAt = now,
            AvailableAt = now,
            LastTransitionAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Enqueue a single <see cref="JobType.PrivateSpeakingReminder"/> job, skipping
    /// if one is already queued/processing. Idempotent across multiple API replicas,
    /// mirroring <see cref="EnqueuePrivateSpeakingNoShowSweepJobAsync"/>.
    /// </summary>
    private static async Task EnqueuePrivateSpeakingReminderJobAsync(
        LearnerDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var alreadyQueued = await db.BackgroundJobs
            .AsNoTracking()
            .AnyAsync(j => j.Type == JobType.PrivateSpeakingReminder
                && (j.State == AsyncState.Queued || j.State == AsyncState.Processing),
                cancellationToken);
        if (alreadyQueued) return;

        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"jb-ps-reminder-{Guid.NewGuid():N}",
            Type = JobType.PrivateSpeakingReminder,
            State = AsyncState.Queued,
            StatusReasonCode = "queued",
            StatusMessage = "Private Speaking reminder sweep queued.",
            CreatedAt = now,
            AvailableAt = now,
            LastTransitionAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Enqueue a single <see cref="JobType.PrivateSpeakingReservationExpiry"/> job,
    /// skipping if one is already queued/processing. Idempotent across multiple API
    /// replicas, mirroring <see cref="EnqueuePrivateSpeakingNoShowSweepJobAsync"/>.
    /// </summary>
    private static async Task EnqueuePrivateSpeakingReservationExpiryJobAsync(
        LearnerDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var alreadyQueued = await db.BackgroundJobs
            .AsNoTracking()
            .AnyAsync(j => j.Type == JobType.PrivateSpeakingReservationExpiry
                && (j.State == AsyncState.Queued || j.State == AsyncState.Processing),
                cancellationToken);
        if (alreadyQueued) return;

        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"jb-ps-reservation-expiry-{Guid.NewGuid():N}",
            Type = JobType.PrivateSpeakingReservationExpiry,
            State = AsyncState.Queued,
            StatusReasonCode = "queued",
            StatusMessage = "Private Speaking reservation-expiry sweep queued.",
            CreatedAt = now,
            AvailableAt = now,
            LastTransitionAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task RunSpeakingTranscriptionQueueAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var pipeline = services.GetRequiredService<OetLearner.Api.Services.Speaking.SpeakingTranscriptionPipeline>();
        for (var processed = 0; processed < 5; processed += 1)
        {
            if (!await pipeline.ProcessNextAsync(cancellationToken))
            {
                break;
            }
        }
    }

    /// <summary>
    /// Wave A5 — claim pending dunning attempts whose <c>ScheduledAt</c> is in
    /// the past and enqueue one <see cref="JobType.BillingDunningRetry"/> per
    /// row. Uses a dedup guard on (ResourceId == DunningAttempt.Id) to avoid
    /// double-enqueueing if multiple API replicas tick at the same moment.
    /// </summary>
    private static async Task EnqueueDueBillingDunningRetriesAsync(
        LearnerDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = await db.DunningAttempts
            .Where(a => a.Outcome == OetLearner.Api.Domain.DunningAttemptOutcome.Pending
                     && a.ScheduledAt <= now
                     && a.ExecutedAt == null)
            .OrderBy(a => a.ScheduledAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (due.Count == 0) return;

        var pendingAttemptIds = due.Select(a => a.Id).ToList();
        var alreadyQueued = await db.BackgroundJobs
            .Where(j => j.Type == JobType.BillingDunningRetry
                     && pendingAttemptIds.Contains(j.ResourceId!)
                     && (j.State == AsyncState.Queued || j.State == AsyncState.Processing))
            .Select(j => j.ResourceId)
            .ToListAsync(cancellationToken);
        var alreadyQueuedSet = new HashSet<string?>(alreadyQueued);

        foreach (var attempt in due)
        {
            if (alreadyQueuedSet.Contains(attempt.Id)) continue;
            db.BackgroundJobs.Add(new BackgroundJobItem
            {
                Id = $"jb-dunning-retry-{Guid.NewGuid():N}",
                Type = JobType.BillingDunningRetry,
                ResourceId = attempt.Id,
                State = AsyncState.Queued,
                StatusReasonCode = "queued",
                StatusMessage = "Billing dunning retry queued.",
                CreatedAt = now,
                AvailableAt = now,
                LastTransitionAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Wave A5 — schedule a single <see cref="JobType.BillingAbandonedCartEmail"/>
    /// sweep at 03:00 UTC each day. The "last enqueued" timestamp guards
    /// against duplicate sweeps within the same UTC date when the worker
    /// reschedules between ticks.
    /// </summary>
    private static async Task EnqueueAbandonedCartSweepJobAsync(
        LearnerDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = now.UtcDateTime.ToString("yyyy-MM-dd");
        var jobId = $"jb-cart-sweep-{today}";
        var existing = await db.BackgroundJobs
            .AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => j.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null) return;

        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = jobId,
            Type = JobType.BillingAbandonedCartEmail,
            State = AsyncState.Queued,
            StatusReasonCode = "queued",
            StatusMessage = "Billing abandoned-cart sweep queued.",
            CreatedAt = now,
            AvailableAt = now,
            LastTransitionAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// True when "now" is at or past 03:00 UTC on a UTC date we have not yet
    /// emitted a sweep job for. Encapsulated so unit tests can target it
    /// without booting the full processor.
    /// </summary>
    internal static bool ShouldEnqueueDailyAbandonedCartSweep(DateTimeOffset now, DateTimeOffset lastEnqueuedAt)
    {
        if (now.UtcDateTime.Hour < 3) return false;
        if (lastEnqueuedAt == DateTimeOffset.MinValue) return true;
        return lastEnqueuedAt.UtcDateTime.Date < now.UtcDateTime.Date;
    }

    internal static async Task RunReadinessRolloverAsync(IServiceProvider services, LearnerDbContext db, CancellationToken cancellationToken)
    {
        var staleCutoff = DateTimeOffset.UtcNow.AddHours(-24);
        var recentActivityCutoff = DateTimeOffset.UtcNow.AddDays(-7);
        var staleUserIds = await db.ReadinessSnapshots
            .Where(s => s.ComputedAt < staleCutoff)
            .Select(s => s.UserId)
            .Distinct()
            .Take(100)
            .ToListAsync(cancellationToken);

        if (staleUserIds.Count > 0)
        {
            var activeUserIds = await db.Attempts
                .Where(a => staleUserIds.Contains(a.UserId) && a.CompletedAt >= recentActivityCutoff)
                .Select(a => a.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var computation = services.GetRequiredService<OetLearner.Api.Services.Readiness.ReadinessComputationService>();
            foreach (var userId in activeUserIds)
            {
                try
                {
                    await computation.ComputeAsync(userId, cancellationToken);
                }
                catch (Exception)
                {
                    // swallow per-user failures so rollover continues
                }
            }
        }

        // Prune history beyond 26 weeks
        var pruneCutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-26 * 7));
        await PruneReadinessHistoryAsync(db, pruneCutoff, cancellationToken);
    }

    internal static async Task<int> PruneReadinessHistoryAsync(
        LearnerDbContext db,
        DateOnly pruneCutoff,
        CancellationToken cancellationToken)
    {
        var query = db.ReadinessHistories
            .Where(history => history.WeekStartDate < pruneCutoff);

        if (db.Database.IsRelational())
        {
            return await query.ExecuteDeleteAsync(cancellationToken);
        }

        // EF's in-memory provider does not support ExecuteDeleteAsync.
        var oldRows = await query.ToListAsync(cancellationToken);
        if (oldRows.Count == 0) return 0;

        db.ReadinessHistories.RemoveRange(oldRows);
        await db.SaveChangesAsync(cancellationToken);
        return oldRows.Count;
    }
}
