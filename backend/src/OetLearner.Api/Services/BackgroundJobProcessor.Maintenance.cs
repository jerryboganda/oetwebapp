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
    private static async Task RunSubscriptionLifecycleCheckAsync(LearnerDbContext db, NotificationService notifications, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var renewalWindow = now.AddDays(7);

        // "Renews on …" is a billing event. One-time packages (plan not renewable)
        // must never receive it — their end date is access expiry, not a renewal.
        var renewablePlanCodes = (await db.BillingPlans.AsNoTracking()
            .Where(plan => plan.IsRenewable && plan.Code != null)
            .Select(plan => plan.Code)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<Subscription> renewingSoon;
        List<Subscription> expired;
        if (db.Database.IsSqlite())
        {
            var activeSubscriptions = await db.Subscriptions
                .AsNoTracking()
                .Where(subscription => subscription.Status == SubscriptionStatus.Active)
                .ToListAsync(cancellationToken);

            renewingSoon = activeSubscriptions
                .Where(subscription => subscription.NextRenewalAt > now && subscription.NextRenewalAt <= renewalWindow)
                .Where(subscription => renewablePlanCodes.Contains(subscription.PlanId))
                .ToList();
            expired = activeSubscriptions
                .Where(subscription => subscription.NextRenewalAt <= now)
                .ToList();
        }
        else
        {
            // Renewal reminders: Active subscriptions renewing within 7 days
            renewingSoon = await db.Subscriptions
                .AsNoTracking()
                .Where(subscription => subscription.Status == SubscriptionStatus.Active
                    && subscription.NextRenewalAt > now
                    && subscription.NextRenewalAt <= renewalWindow)
                .ToListAsync(cancellationToken);
            renewingSoon = renewingSoon
                .Where(subscription => renewablePlanCodes.Contains(subscription.PlanId))
                .ToList();

            expired = await db.Subscriptions
                .AsNoTracking()
                .Where(subscription => subscription.Status == SubscriptionStatus.Active
                    && subscription.NextRenewalAt <= now)
                .ToListAsync(cancellationToken);
        }

        foreach (var sub in renewingSoon)
        {
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerRenewalComing,
                sub.UserId,
                "Subscription",
                sub.Id,
                now.UtcDateTime.ToString("yyyy-MM-dd"),
                new Dictionary<string, object?>
                {
                    ["message"] = $"Your subscription renews on {sub.NextRenewalAt:yyyy-MM-dd}.",
                    ["planName"] = sub.PlanId,
                    ["renewalDate"] = sub.NextRenewalAt.ToString("O")
                },
                cancellationToken);
        }

        foreach (var sub in expired)
        {
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerSubscriptionChanged,
                sub.UserId,
                "Subscription",
                sub.Id,
                now.UtcDateTime.ToString("yyyy-MM-dd"),
                new Dictionary<string, object?>
                {
                    ["message"] = "Your subscription has expired. Renew to keep your study plan and premium features.",
                    ["planName"] = sub.PlanId,
                    ["status"] = "expired"
                },
                cancellationToken);
        }
    }

    private static async Task RunSlaAlertCheckAsync(LearnerDbContext db, NotificationService notifications, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        // Alert experts 24h before SLA deadline
        var alertThreshold = now.AddHours(24);

        // Map turnaround options to hours for SLA calculation
        static int TurnaroundHours(string option) => option?.ToLowerInvariant() switch
        {
            "24h" or "24hour" or "24-hour" or "1day" => 24,
            "48h" or "48hour" or "48-hour" or "2day" => 48,
            "72h" or "72hour" or "72-hour" or "3day" => 72,
            "7day" or "7-day" or "1week" => 168,
            _ => 72
        };

        var approachingDeadline = db.Database.IsSqlite()
            ? (await db.ReviewRequests
                    .AsNoTracking()
                    .Where(request => request.State == ReviewRequestState.InReview)
                    .ToListAsync(cancellationToken))
                .Join(
                    (await db.ExpertReviewAssignments
                            .AsNoTracking()
                            .Where(assignment => assignment.ClaimState == ExpertAssignmentState.Claimed
                                && assignment.AssignedReviewerId != null)
                            .ToListAsync(cancellationToken))
                        .Where(assignment => !string.IsNullOrWhiteSpace(assignment.AssignedReviewerId)),
                    request => request.Id,
                    assignment => assignment.ReviewRequestId,
                    (request, assignment) => new { Request = request, Assignment = assignment })
                .ToList()
            : await db.ReviewRequests
                .AsNoTracking()
                .Where(request => request.State == ReviewRequestState.InReview)
                .Join(
                    db.ExpertReviewAssignments.AsNoTracking().Where(assignment => assignment.ClaimState == ExpertAssignmentState.Claimed && assignment.AssignedReviewerId != null),
                    request => request.Id,
                    assignment => assignment.ReviewRequestId,
                    (request, assignment) => new { Request = request, Assignment = assignment })
                .ToListAsync(cancellationToken);

        foreach (var item in approachingDeadline)
        {
            var slaDeadline = item.Request.CreatedAt.AddHours(TurnaroundHours(item.Request.TurnaroundOption));
            if (slaDeadline > now && slaDeadline <= alertThreshold && !string.IsNullOrWhiteSpace(item.Assignment.AssignedReviewerId))
            {
                await notifications.CreateForExpertAsync(
                    NotificationEventKey.ExpertReviewOverdue,
                    item.Assignment.AssignedReviewerId!,
                    "ReviewRequest",
                    item.Request.Id,
                    now.UtcDateTime.ToString("yyyy-MM-dd"),
                    new Dictionary<string, object?>
                    {
                        ["reviewRequestId"] = item.Request.Id,
                        ["message"] = $"Review {item.Request.Id} is approaching its SLA deadline ({slaDeadline:yyyy-MM-dd HH:mm})."
                    },
                    cancellationToken);
            }
        }
    }

    private static async Task RunDripCampaignDispatchAsync(LearnerDbContext db, NotificationService notifications, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var today = now.UtcDateTime.ToString("yyyy-MM-dd");

        // Credit depletion nudge: learners with low credits
        var lowCreditThreshold = 3;
        var lowCreditLearners = await db.Wallets
            .AsNoTracking()
            .Where(w => w.CreditBalance < lowCreditThreshold)
            .ToListAsync(cancellationToken);

        foreach (var wallet in lowCreditLearners)
        {
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerCreditsLow,
                wallet.UserId,
                "Wallet",
                wallet.Id,
                today,
                new Dictionary<string, object?>
                {
                    ["message"] = $"You have {wallet.CreditBalance} review credits left. Top up to keep submitting for expert feedback."
                },
                cancellationToken);
        }

        // Inactive learner nudge: no activity in 7 days
        var inactiveThreshold = now.AddDays(-7);
        var inactiveLearners = db.Database.IsSqlite()
            ? (await db.Users
                .AsNoTracking()
                .Where(user => user.Role == ApplicationUserRoles.Learner)
                .ToListAsync(cancellationToken))
                .Where(user => user.LastActiveAt < inactiveThreshold)
                .ToList()
            : await db.Users
                .AsNoTracking()
                .Where(user => user.LastActiveAt < inactiveThreshold
                    && user.Role == ApplicationUserRoles.Learner)
                .ToListAsync(cancellationToken);

        foreach (var user in inactiveLearners)
        {
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerInactiveNudge,
                user.Id,
                "User",
                user.Id,
                today,
                new Dictionary<string, object?>
                {
                    ["message"] = "We miss you! Come back to continue your OET preparation."
                },
                cancellationToken);
        }
    }
}
