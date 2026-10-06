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
    /// Wave A5 — execute a single pending dunning attempt. The job payload
    /// carries the <c>DunningAttempt.Id</c> (also stored in
    /// <see cref="BackgroundJobItem.ResourceId"/> by the enqueuer).
    /// </summary>
    private static async Task ExecuteBillingDunningRetryAsync(
        IServiceProvider services, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId)) return;
        var dunning = services.GetRequiredService<OetLearner.Api.Services.Billing.IDunningService>();
        await dunning.ExecutePendingRetryAsync(job.ResourceId, cancellationToken);
    }

    /// <summary>
    /// Wave A5 — daily 03:00 UTC sweep that emails carts idle &gt; 24h.
    /// </summary>
    private static async Task ExecuteBillingAbandonedCartEmailAsync(
        IServiceProvider services, CancellationToken cancellationToken)
    {
        var svc = services.GetRequiredService<OetLearner.Api.Services.Billing.IAbandonedCartRecoveryService>();
        await svc.SweepAsync(cancellationToken);
    }

    /// <summary>
    /// Wave A5 — 3-day renewal reminder. Wave A4's <c>invoice.upcoming</c>
    /// webhook enqueues the job with payload
    /// <c>{ userId, subscriptionId, renewsAt, amount, currency }</c>.
    /// </summary>
    private static async Task ExecuteBillingRenewalReminderAsync(
        IServiceProvider services, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.PayloadJson)) return;
        BillingRenewalReminderPayload? payload;
        try
        {
            payload = System.Text.Json.JsonSerializer.Deserialize<BillingRenewalReminderPayload>(job.PayloadJson);
        }
        catch
        {
            return;
        }
        if (payload is null || string.IsNullOrWhiteSpace(payload.UserId) || string.IsNullOrWhiteSpace(payload.SubscriptionId))
            return;

        var dispatcher = services.GetRequiredService<OetLearner.Api.Services.Billing.IBillingNotificationDispatcher>();
        await OetLearner.Api.Services.Billing.BillingDunningNotifications.SendRenewalReminderAsync(
            dispatcher,
            userId: payload.UserId,
            stripeSubscriptionId: payload.SubscriptionId,
            renewsAt: payload.RenewsAt,
            amount: payload.Amount ?? string.Empty,
            currency: payload.Currency ?? string.Empty,
            cancellationToken);
    }

    private sealed class BillingRenewalReminderPayload
    {
        public string? UserId { get; set; }
        public string? SubscriptionId { get; set; }
        public DateTimeOffset RenewsAt { get; set; }
        public string? Amount { get; set; }
        public string? Currency { get; set; }
    }

    private static async Task CompleteFreezeStartAsync(LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId))
        {
            return;
        }

        var record = await db.AccountFreezeRecords.FirstOrDefaultAsync(x => x.Id == job.ResourceId, cancellationToken);
        if (record is null)
        {
            return;
        }

        if (record.Status is FreezeStatus.Active or FreezeStatus.Completed or FreezeStatus.ForceEnded or FreezeStatus.Cancelled or FreezeStatus.Rejected)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (record.ScheduledStartAt is not null && record.ScheduledStartAt > now)
        {
            return;
        }

        record.Status = FreezeStatus.Active;
        record.IsCurrent = true;
        record.StartedAt ??= record.ScheduledStartAt ?? now;
        record.UpdatedAt = now;
        await AccountFreezeEntitlements.ConsumeSelfServiceAsync(db, record, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerFreezeStarted,
            record.UserId,
            nameof(AccountFreezeRecord),
            record.Id,
            record.PolicyVersionSnapshot.ToString(),
            new Dictionary<string, object?>
            {
                ["freezeId"] = record.Id,
                ["message"] = "Your freeze is now active."
            },
            cancellationToken);
    }

    private static async Task CompleteFreezeEndAsync(LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId))
        {
            return;
        }

        var record = await db.AccountFreezeRecords.FirstOrDefaultAsync(x => x.Id == job.ResourceId, cancellationToken);
        if (record is null)
        {
            return;
        }

        if (record.Status is FreezeStatus.Completed or FreezeStatus.ForceEnded or FreezeStatus.Cancelled or FreezeStatus.Rejected)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (record.EndedAt is not null && record.EndedAt > now)
        {
            return;
        }

        record.Status = FreezeStatus.Completed;
        record.IsCurrent = false;
        record.StartedAt ??= record.ScheduledStartAt ?? now;
        record.EndedAt ??= now;
        record.EndReason ??= "Freeze period ended";
        record.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerFreezeEnded,
            record.UserId,
            nameof(AccountFreezeRecord),
            record.Id,
            record.PolicyVersionSnapshot.ToString(),
            new Dictionary<string, object?>
            {
                ["freezeId"] = record.Id,
                ["message"] = "Your freeze period has ended."
            },
            cancellationToken);
    }

    /// <summary>
    /// Freeze records that are due a lifecycle transition right now: a Scheduled one
    /// whose start has passed, or an Active one whose end has passed. These are
    /// exactly the two conditions the loop in <see cref="ReconcileFreezeLifecycleAsync"/>
    /// acts on, evaluated in SQL (indexed on Status + ScheduledStartAt / EndedAt) so
    /// the common case (nothing due) loads no rows. The sweep used to load every
    /// Scheduled and Active record, tracked, and filter in memory.
    /// </summary>
    internal static IQueryable<AccountFreezeRecord> DueFreezeRecords(LearnerDbContext db, DateTimeOffset now)
        => db.AccountFreezeRecords.Where(x =>
            (x.Status == FreezeStatus.Scheduled && x.ScheduledStartAt != null && x.ScheduledStartAt <= now)
            || (x.Status == FreezeStatus.Active && x.EndedAt != null && x.EndedAt <= now));

    private static async Task ReconcileFreezeLifecycleAsync(IServiceProvider services, LearnerDbContext db, CancellationToken cancellationToken)
    {
        var notifications = services.GetRequiredService<NotificationService>();
        var now = DateTimeOffset.UtcNow;

        // SQLite cannot translate DateTimeOffset comparisons, so it keeps the old
        // load-then-filter shape (the loop below re-checks both conditions anyway).
        var records = db.Database.IsSqlite()
            ? (await db.AccountFreezeRecords
                    .Where(x => x.Status == FreezeStatus.Scheduled || x.Status == FreezeStatus.Active)
                    .ToListAsync(cancellationToken))
                .OrderBy(x => x.ScheduledStartAt)
                .ToList()
            : await DueFreezeRecords(db, now)
                .OrderBy(x => x.ScheduledStartAt)
                .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var record in records)
        {
            if (record.Status == FreezeStatus.Scheduled && record.ScheduledStartAt is not null && record.ScheduledStartAt <= now)
            {
                record.Status = FreezeStatus.Active;
                record.StartedAt ??= record.ScheduledStartAt ?? now;
                record.UpdatedAt = now;
                changed = true;
                await AccountFreezeEntitlements.ConsumeSelfServiceAsync(db, record, now, cancellationToken);

                await notifications.CreateForLearnerAsync(
                    NotificationEventKey.LearnerFreezeStarted,
                    record.UserId,
                    nameof(AccountFreezeRecord),
                    record.Id,
                    record.PolicyVersionSnapshot.ToString(),
                    new Dictionary<string, object?>
                    {
                        ["freezeId"] = record.Id,
                        ["message"] = "Your freeze is now active."
                    },
                    cancellationToken);
            }

            if (record.Status == FreezeStatus.Active && record.EndedAt is not null && record.EndedAt <= now)
            {
                record.Status = FreezeStatus.Completed;
                record.IsCurrent = false;
                record.StartedAt ??= record.ScheduledStartAt ?? now;
                record.EndedAt ??= now;
                record.EndReason ??= "Freeze period ended";
                record.UpdatedAt = now;
                changed = true;

                await notifications.CreateForLearnerAsync(
                    NotificationEventKey.LearnerFreezeEnded,
                    record.UserId,
                    nameof(AccountFreezeRecord),
                    record.Id,
                    record.PolicyVersionSnapshot.ToString(),
                    new Dictionary<string, object?>
                    {
                        ["freezeId"] = record.Id,
                        ["message"] = "Your freeze period has ended."
                    },
                    cancellationToken);
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
