using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Security;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services;

public partial class AdminService
{

    private static void ApplyAdminSubscriptionStatus(Subscription subscription, SubscriptionStatus target, string reason, DateTimeOffset now)
    {
        if (SubscriptionStateMachine.IsLegal(subscription.Status, target))
        {
            SubscriptionStateMachine.Transition(subscription, target, reason);
            return;
        }

        subscription.Status = target;
        subscription.ChangedAt = now;
    }

    public async Task<object> GetBillingSubscriptionsAsync(string? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Subscriptions.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            if (Enum.TryParse<SubscriptionStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(subscription => subscription.Status == parsedStatus);
            }
        }
        else
        {
            // Normal admin view hides Draft scaffolds (pre-payment only, never Pending,
            // no entitlements) and phantom Pending+auto rows that never saw a successful
            // payment (cart-only). Explicit status=draft or status=pending still shows
            // them when filtered.
            query = query.Where(subscription =>
                subscription.Status != SubscriptionStatus.Draft
                && !(subscription.Status == SubscriptionStatus.Pending && subscription.FulfilmentStatus == FulfilmentStatuses.Auto));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Trim();
            query = query.Where(subscription => subscription.UserId.Contains(normalized) || subscription.PlanId.Contains(normalized));
        }

        var total = await query.CountAsync(ct);
        var subscriptions = await ToOrderedListDescendingAsync(
            query,
            subscription => subscription.ChangedAt,
            ct,
            skip: (page - 1) * pageSize,
            take: pageSize);

        var userIds = subscriptions.Select(subscription => subscription.UserId).Distinct().ToList();
        var userNames = await db.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, ct);

        var planCodes = subscriptions.Select(subscription => subscription.PlanId).Distinct().ToList();
        var planNames = await db.BillingPlans.AsNoTracking()
            .Where(plan => planCodes.Contains(plan.Code) || planCodes.Contains(plan.Id))
            .ToDictionaryAsync(plan => plan.Code, plan => plan.Name, ct);

        var items = subscriptions.Select(subscription => new
        {
            subscription.Id,
            subscription.UserId,
            userName = userNames.TryGetValue(subscription.UserId, out var name) ? name : subscription.UserId,
            planId = subscription.PlanId,
            planName = planNames.TryGetValue(subscription.PlanId, out var planName) ? planName : subscription.PlanId,
            status = NormalizeSubscriptionStatus(subscription, DateTimeOffset.UtcNow),
            subscription.NextRenewalAt,
            subscription.ExpiresAt,
            startDate = subscription.StartedAt == default ? (DateTimeOffset?)null : subscription.StartedAt,
            endDate = subscription.ExpiresAt,
            durationDays = Math.Max(1, subscription.AccessDurationDays),
            remainingDays = CalculateSubscriptionRemainingDays(subscription, DateTimeOffset.UtcNow),
            expiringSoon = IsSubscriptionExpiringSoon(subscription, DateTimeOffset.UtcNow),
            subscription.TotalFreezeDaysUsed,
            maxFreezeDays = subscription.MaxFreezeDaysAllowed,
            freezeAllowanceRemaining = Math.Max(0, subscription.MaxFreezeDaysAllowed - subscription.TotalFreezeDaysUsed),
            subscription.PreservedRemainingDays,
            subscription.PendingFreezeRequestDate,
            subscription.FrozenSince,
            subscription.StartedAt,
            subscription.ChangedAt,
            price = subscription.PriceAmount,
            subscription.Currency,
            subscription.Interval,
            addOnCount = 0
        }).ToList();

        return new { total, page, pageSize, items };
    }

    // ════════════════════════════════════════════
    //  Subscription lifecycle (admin manual actions)
    // ════════════════════════════════════════════
    //
    // These endpoints expose the same Subscription state transitions that normally
    // happen through the checkout / webhook pipeline (see LearnerService.ApplyCheckoutCompletionAsync)
    // but driven explicitly by an admin operator. Every mutation:
    //   • Loads the Subscription row by Id (404 if missing).
    //   • Applies the state change in-process under a single SaveChanges call.
    //   • Writes an AuditEvent with actor + action + reason for every transition.
    //   • Returns the canonical projection so the UI can hot-swap the row without a refetch.
    //
    // Concurrency: callers retry once on DbUpdateConcurrencyException, mirroring the
    // pattern used by AdjustUserCreditsAsync.

    // Shared 2-attempt retry wrapper for admin subscription mutations. The first
    // DbUpdateConcurrencyException clears the change tracker and retries; the second
    // surface as a 409 so the operator can refresh and try again. All lifecycle
    // mutations use this so concurrent admin/checkout flows do not silently lose
    // a write or commit a torn state.
    private async Task<T> WithSubscriptionConcurrencyRetryAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        for (var attemptNumber = 0; attemptNumber < 2; attemptNumber++)
        {
            try
            {
                return await action(ct);
            }
            catch (DbUpdateConcurrencyException) when (attemptNumber == 0)
            {
                db.ChangeTracker.Clear();
            }
        }

        throw ApiException.Conflict(
            "subscription_update_conflict",
            "The subscription changed while the update was being applied. Please retry.");
    }

    public Task<object> ChangeSubscriptionPlanAsync(string adminId, string adminName,
        string subscriptionId, AdminSubscriptionChangePlanRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PlanCode))
        {
            throw ApiException.Validation("plan_required", "A target plan code is required.");
        }

        return WithSubscriptionConcurrencyRetryAsync(
            inner => ChangeSubscriptionPlanCoreAsync(adminId, adminName, subscriptionId, request, inner),
            ct);
    }

    private async Task<object> ChangeSubscriptionPlanCoreAsync(string adminId, string adminName,
        string subscriptionId, AdminSubscriptionChangePlanRequest request, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct)
            ?? throw ApiException.NotFound("subscription_not_found", "Subscription not found.");

        var planCode = request.PlanCode.Trim();
        var targetPlan = await db.BillingPlans.FirstOrDefaultAsync(p => p.Code == planCode || p.Id == planCode, ct)
            ?? throw ApiException.Validation("plan_not_found", $"Billing plan '{planCode}' was not found.");

        var now = DateTimeOffset.UtcNow;
        var previousPlanId = subscription.PlanId;
        var previousStatus = subscription.Status;

        subscription.PlanId = targetPlan.Code;
        subscription.PlanVersionId = null; // admin override is not bound to a payment-time snapshot
        subscription.PriceAmount = targetPlan.Price;
        subscription.Currency = targetPlan.Currency;
        subscription.Interval = targetPlan.Interval;
        if (subscription.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Expired or SubscriptionStatus.Suspended)
        {
            ApplyAdminSubscriptionStatus(subscription, SubscriptionStatus.Active, "admin_change_plan", now);
        }
        subscription.ChangedAt = now;
        if (subscription.StartedAt == default)
        {
            subscription.StartedAt = now;
        }

        if (request.ResetRenewalDate || subscription.NextRenewalAt <= now)
        {
            subscription.NextRenewalAt = now.AddMonths(Math.Max(1, targetPlan.DurationMonths));
        }

        var learner = await db.Users.FirstOrDefaultAsync(u => u.Id == subscription.UserId, ct);
        if (learner is not null)
        {
            learner.CurrentPlanId = targetPlan.Code;
        }

        var creditedAmount = 0;
        if (request.GrantIncludedCredits && targetPlan.IncludedCredits > 0)
        {
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == subscription.UserId, ct);
            if (wallet is null)
            {
                wallet = new Wallet
                {
                    Id = $"wallet-{Guid.NewGuid():N}",
                    UserId = subscription.UserId,
                    CreditBalance = 0,
                    LedgerSummaryJson = "[]",
                    LastUpdatedAt = now
                };
                db.Wallets.Add(wallet);
            }
            wallet.CreditBalance += targetPlan.IncludedCredits;
            wallet.LastUpdatedAt = now;
            // Append-only ledger entry so reconciliation matches the credit-balance
            // delta. Source = admin override, attributed to the operator.
            db.WalletTransactions.Add(new WalletTransaction
            {
                Id = Guid.NewGuid(),
                WalletId = wallet.Id,
                TransactionType = "admin_grant",
                Amount = targetPlan.IncludedCredits,
                BalanceAfter = wallet.CreditBalance,
                ReferenceType = "subscription",
                ReferenceId = subscription.Id,
                Description = $"Plan change to {targetPlan.Code}: granted {targetPlan.IncludedCredits} credits",
                CreatedBy = adminId,
                CreatedAt = now,
            });
            creditedAmount = targetPlan.IncludedCredits;
        }

        await db.SaveChangesAsync(ct);

        var details = $"Changed plan {previousPlanId} → {targetPlan.Code}"
            + $"; status {previousStatus} → {subscription.Status}"
            + (request.ResetRenewalDate ? "; renewal reset" : "")
            + (creditedAmount > 0 ? $"; granted {creditedAmount} credits" : "")
            + (string.IsNullOrWhiteSpace(request.Reason) ? "" : $"; reason: {request.Reason}");
        await LogAuditAsync(adminId, adminName, "Subscription Plan Change", "Subscription", subscriptionId, details, ct);

        return ProjectSubscription(subscription, targetPlan.Name, learner?.DisplayName);
    }

    public Task<object> ExtendSubscriptionAsync(string adminId, string adminName,
        string subscriptionId, AdminSubscriptionExtendRequest request, CancellationToken ct)
    {
        var providedAxes = (request.AddDays.HasValue ? 1 : 0)
            + (request.AddMonths.HasValue ? 1 : 0)
            + (request.NewRenewalAt.HasValue ? 1 : 0);
        if (providedAxes != 1)
        {
            throw ApiException.Validation(
                "extend_input_invalid",
                "Provide exactly one of addDays, addMonths or newRenewalAt.");
        }

        return WithSubscriptionConcurrencyRetryAsync(
            inner => ExtendSubscriptionCoreAsync(adminId, adminName, subscriptionId, request, inner),
            ct);
    }

    private async Task<object> ExtendSubscriptionCoreAsync(string adminId, string adminName,
        string subscriptionId, AdminSubscriptionExtendRequest request, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct)
            ?? throw ApiException.NotFound("subscription_not_found", "Subscription not found.");

        var now = DateTimeOffset.UtcNow;
        var previousRenewal = subscription.NextRenewalAt;
        var previousExpiry = subscription.ExpiresAt;
        var anchor = subscription.NextRenewalAt > now ? subscription.NextRenewalAt : now;
        var expiryAnchor = subscription.ExpiresAt is { } exp && exp > now ? exp : now;

        DateTimeOffset newRenewal;
        if (request.NewRenewalAt.HasValue)
        {
            newRenewal = request.NewRenewalAt.Value;
        }
        else if (request.AddDays.HasValue)
        {
            newRenewal = anchor.AddDays(request.AddDays.Value);
        }
        else
        {
            newRenewal = anchor.AddMonths(request.AddMonths!.Value);
        }

        if (newRenewal <= now.AddYears(-1) || newRenewal >= now.AddYears(50))
        {
            throw ApiException.Validation("extend_renewal_out_of_range",
                "Renewal date must be within a reasonable window (-1y to +50y from now).");
        }

        subscription.NextRenewalAt = newRenewal;
        if (subscription.Status == SubscriptionStatus.Frozen)
        {
            var deltaDays = Math.Max(0, (int)Math.Ceiling((newRenewal - previousRenewal).TotalDays));
            subscription.PreservedRemainingDays = Math.Max(0, (subscription.PreservedRemainingDays ?? 0) + deltaDays);
        }
        else
        {
            var deltaDays = Math.Max(0, (int)Math.Ceiling((newRenewal - previousRenewal).TotalDays));
            subscription.ExpiresAt = request.NewRenewalAt ?? expiryAnchor.AddDays(deltaDays);
        }
        subscription.ChangedAt = now;
        if (subscription.Status is SubscriptionStatus.Expired or SubscriptionStatus.PastDue && newRenewal > now)
        {
            ApplyAdminSubscriptionStatus(subscription, SubscriptionStatus.Active, "admin_extend_subscription", now);
        }

        await db.SaveChangesAsync(ct);

        var details = $"Renewal {previousRenewal:o} → {newRenewal:o}"
            + (string.IsNullOrWhiteSpace(request.Reason) ? "" : $"; reason: {request.Reason}");
        await LogAuditAsync(adminId, adminName, "Subscription Extension", "Subscription", subscriptionId, details, ct);

        var planName = await ResolvePlanNameAsync(subscription.PlanId, ct);
        var learnerName = await ResolveUserDisplayNameAsync(subscription.UserId, ct);
        return ProjectSubscription(subscription, planName, learnerName);
    }

    public Task<object> CancelSubscriptionAsync(string adminId, string adminName,
        string subscriptionId, AdminSubscriptionCancelRequest request, CancellationToken ct)
        => WithSubscriptionConcurrencyRetryAsync(
            inner => CancelSubscriptionCoreAsync(adminId, adminName, subscriptionId, request, inner),
            ct);

    private async Task<object> CancelSubscriptionCoreAsync(string adminId, string adminName,
        string subscriptionId, AdminSubscriptionCancelRequest request, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct)
            ?? throw ApiException.NotFound("subscription_not_found", "Subscription not found.");

        if (subscription.Status == SubscriptionStatus.Cancelled)
        {
            throw ApiException.Validation("subscription_already_cancelled", "This subscription is already cancelled.");
        }

        var now = DateTimeOffset.UtcNow;
        var previousStatus = subscription.Status;
        // Immediate cancellation revokes entitlement now (status flip + renewal pulled
        // forward). Non-immediate ("end-of-period") cancellation preserves the existing
        // status and renewal anchor so entitlement continues until the natural renewal
        // date — the scheduled cancellation is recorded in the audit log only. This
        // matches the contract documented on AdminSubscriptionCancelRequest.
        if (request.Immediate)
        {
            CloseOpenSubscriptionFreezeRows(subscription, now, "admin_cancel_subscription");
            ApplyAdminSubscriptionStatus(subscription, SubscriptionStatus.Cancelled, "admin_cancel_subscription", now);
            subscription.NextRenewalAt = now;
            subscription.ExpiresAt = now;
        }
        subscription.ChangedAt = now;

        await db.SaveChangesAsync(ct);

        if (request.Immediate && aiPackageCredits is not null)
        {
            // Immediate cancellation is a termination event: park this
            // subscription's AI lots (balances preserved for a later
            // reactivate) and reconcile orphaned allowances immediately.
            await aiPackageCredits.ParkSubscriptionLotsAsync(subscription.UserId, subscription.Id, ct);
            await aiPackageCredits.RecalculateObjectiveAllowancesAsync(subscription.UserId, ct);
        }

        var details = request.Immediate
            ? $"Cancelled (status {previousStatus} → cancelled, immediate)"
              + (string.IsNullOrWhiteSpace(request.Reason) ? "" : $"; reason: {request.Reason}")
            : $"Scheduled cancellation at end-of-period {subscription.NextRenewalAt:o} (status remains {previousStatus})"
              + (string.IsNullOrWhiteSpace(request.Reason) ? "" : $"; reason: {request.Reason}");
        await LogAuditAsync(adminId, adminName, "Subscription Cancellation", "Subscription", subscriptionId, details, ct);

        if (notifications is not null)
        {
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerSubscriptionCancelled,
                subscription.UserId,
                "Subscription",
                subscription.Id,
                now.UtcDateTime.ToString("yyyy-MM-dd"),
                new Dictionary<string, object?>
                {
                    ["message"] = request.Immediate
                        ? "Your subscription has been cancelled immediately."
                        : $"Your subscription is scheduled to cancel at the end of your current billing period ({subscription.NextRenewalAt:yyyy-MM-dd}).",
                    ["planName"] = subscription.PlanId,
                    ["status"] = subscription.Status.ToString().ToLowerInvariant()
                },
                ct);
        }

        var planName = await ResolvePlanNameAsync(subscription.PlanId, ct);
        var learnerName = await ResolveUserDisplayNameAsync(subscription.UserId, ct);
        return ProjectSubscription(subscription, planName, learnerName);
    }

    public Task<object> ReactivateSubscriptionAsync(string adminId, string adminName,
        string subscriptionId, AdminSubscriptionReactivateRequest request, CancellationToken ct)
        => WithSubscriptionConcurrencyRetryAsync(
            inner => ReactivateSubscriptionCoreAsync(adminId, adminName, subscriptionId, request, inner),
            ct);

    private async Task<object> ReactivateSubscriptionCoreAsync(string adminId, string adminName,
        string subscriptionId, AdminSubscriptionReactivateRequest request, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct)
            ?? throw ApiException.NotFound("subscription_not_found", "Subscription not found.");

        if (subscription.Status == SubscriptionStatus.Active)
        {
            throw ApiException.Validation("subscription_already_active", "This subscription is already active.");
        }

        var now = DateTimeOffset.UtcNow;
        var previousStatus = subscription.Status;
        ApplyAdminSubscriptionStatus(subscription, SubscriptionStatus.Active, "admin_reactivate_subscription", now);

        if (request.ResetRenewalDate || subscription.NextRenewalAt <= now)
        {
            var plan = await db.BillingPlans.FirstOrDefaultAsync(p => p.Code == subscription.PlanId || p.Id == subscription.PlanId, ct);
            var months = Math.Max(1, plan?.DurationMonths ?? 1);
            subscription.NextRenewalAt = now.AddMonths(months);
            var days = Math.Max(1, plan?.AccessDurationDays ?? subscription.AccessDurationDays);
            subscription.AccessDurationDays = days;
            subscription.ExpiresAt = now.AddDays(days);
        }

        await db.SaveChangesAsync(ct);

        var details = $"Reactivated (status {previousStatus} → active; renewal {subscription.NextRenewalAt:o})"
            + (string.IsNullOrWhiteSpace(request.Reason) ? "" : $"; reason: {request.Reason}");
        await LogAuditAsync(adminId, adminName, "Subscription Reactivation", "Subscription", subscriptionId, details, ct);

        if (aiPackageCredits is not null)
        {
            // Revive this subscription's parked lots whose validity is still
            // open, then reconcile. Reversed/consumed lots never come back.
            await aiPackageCredits.UnparkSubscriptionLotsAsync(subscription.UserId, subscription.Id, ct);
            await aiPackageCredits.RecalculateObjectiveAllowancesAsync(subscription.UserId, ct);
        }

        var planName = await ResolvePlanNameAsync(subscription.PlanId, ct);
        var learnerName = await ResolveUserDisplayNameAsync(subscription.UserId, ct);
        return ProjectSubscription(subscription, planName, learnerName);
    }

    public Task<object> SetSubscriptionStatusAsync(string adminId, string adminName,
        string subscriptionId, AdminSubscriptionStatusRequest request, CancellationToken ct)
    {
        // Normalize wire-format status tokens (e.g. "past_due") into the PascalCase enum names
        // ("PastDue") that Enum.TryParse expects. Without this strip, valid UI tokens were
        // rejected as invalid because the underscore form does not exist in the enum.
        var normalizedStatus = (request.Status ?? string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
        var statusName = Enum.GetNames<SubscriptionStatus>()
            .FirstOrDefault(name => string.Equals(name, normalizedStatus, StringComparison.OrdinalIgnoreCase));
        if (statusName is null)
        {
            throw ApiException.Validation("subscription_status_invalid",
                "Status must be one of: trial, pending, active, past_due, suspended, cancelled, expired.");
        }
        var parsedStatus = Enum.Parse<SubscriptionStatus>(statusName);

        return WithSubscriptionConcurrencyRetryAsync(
            inner => SetSubscriptionStatusCoreAsync(adminId, adminName, subscriptionId, request, parsedStatus, inner),
            ct);
    }

    private async Task<object> SetSubscriptionStatusCoreAsync(string adminId, string adminName,
        string subscriptionId, AdminSubscriptionStatusRequest request, SubscriptionStatus parsedStatus, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct)
            ?? throw ApiException.NotFound("subscription_not_found", "Subscription not found.");

        var now = DateTimeOffset.UtcNow;
        var previousStatus = subscription.Status;
        SubscriptionStateMachine.Transition(subscription, parsedStatus, "admin_set_subscription_status");

        await db.SaveChangesAsync(ct);

        if (aiPackageCredits is not null)
        {
            // Manual status override is a termination/restore event for AI
            // lots: non-granting terminal states park, granting states unpark.
            // Other states (freeze-dunning nuances) are left to their own flows.
            if (parsedStatus is SubscriptionStatus.Cancelled or SubscriptionStatus.Expired or SubscriptionStatus.Suspended)
            {
                await aiPackageCredits.ParkSubscriptionLotsAsync(subscription.UserId, subscription.Id, ct);
                await aiPackageCredits.RecalculateObjectiveAllowancesAsync(subscription.UserId, ct);
            }
            else if (parsedStatus is SubscriptionStatus.Active or SubscriptionStatus.Trial or SubscriptionStatus.FreezeRequested)
            {
                await aiPackageCredits.UnparkSubscriptionLotsAsync(subscription.UserId, subscription.Id, ct);
                await aiPackageCredits.RecalculateObjectiveAllowancesAsync(subscription.UserId, ct);
            }
        }

        var details = $"Status {previousStatus} → {parsedStatus}"
            + (string.IsNullOrWhiteSpace(request.Reason) ? "" : $"; reason: {request.Reason}");
        await LogAuditAsync(adminId, adminName, "Subscription Status Change", "Subscription", subscriptionId, details, ct);

        var planName = await ResolvePlanNameAsync(subscription.PlanId, ct);
        var learnerName = await ResolveUserDisplayNameAsync(subscription.UserId, ct);
        return ProjectSubscription(subscription, planName, learnerName);
    }

    public Task<object> ApproveSubscriptionFreezeAsync(string adminId, string adminName,
        string subscriptionId, FreezeActionRequest request, CancellationToken ct)
        => WithSubscriptionConcurrencyRetryAsync(
            inner => ApproveSubscriptionFreezeCoreAsync(adminId, adminName, subscriptionId, request, inner),
            ct);

    private async Task<object> ApproveSubscriptionFreezeCoreAsync(string adminId, string adminName,
        string subscriptionId, FreezeActionRequest request, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct)
            ?? throw ApiException.NotFound("subscription_not_found", "Subscription not found.");
        var freeze = await db.SubscriptionFreezes
            .Where(f => f.SubscriptionId == subscriptionId && f.RequestStatus == "pending")
            .OrderByDescending(f => f.FreezeRequestDate)
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.Conflict("freeze_request_missing", "No pending freeze request exists for this subscription.");

        var now = DateTimeOffset.UtcNow;
        if (subscription.ExpiresAt is { } expires && expires <= now)
        {
            ApplyAdminSubscriptionStatus(subscription, SubscriptionStatus.Expired, "admin_approve_freeze_expired", now);
            freeze.RequestStatus = "rejected";
            freeze.RejectionReason = "Subscription expired before approval.";
            freeze.AdminDecisionById = adminId;
            freeze.AdminDecisionDate = now;
            freeze.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            throw ApiException.Validation("subscription_expired", "The subscription expired before the freeze could be approved.");
        }

        StartSubscriptionFreeze(subscription, freeze, now, adminId, request.Reason ?? request.InternalNotes ?? "Approved by admin.");
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Subscription Freeze Approved", "Subscription", subscriptionId, request.Reason ?? "Approved freeze request.", ct);
        return await ProjectSubscriptionForAdminAsync(subscription, ct);
    }

    public Task<object> RejectSubscriptionFreezeAsync(string adminId, string adminName,
        string subscriptionId, FreezeActionRequest request, CancellationToken ct)
        => WithSubscriptionConcurrencyRetryAsync(
            inner => RejectSubscriptionFreezeCoreAsync(adminId, adminName, subscriptionId, request, inner),
            ct);

    private async Task<object> RejectSubscriptionFreezeCoreAsync(string adminId, string adminName,
        string subscriptionId, FreezeActionRequest request, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct)
            ?? throw ApiException.NotFound("subscription_not_found", "Subscription not found.");
        var freeze = await db.SubscriptionFreezes
            .Where(f => f.SubscriptionId == subscriptionId && f.RequestStatus == "pending")
            .OrderByDescending(f => f.FreezeRequestDate)
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.Conflict("freeze_request_missing", "No pending freeze request exists for this subscription.");

        var now = DateTimeOffset.UtcNow;
        freeze.RequestStatus = "rejected";
        freeze.RejectionReason = request.Reason ?? request.InternalNotes ?? "Rejected by admin.";
        freeze.AdminNotes = request.InternalNotes;
        freeze.AdminDecisionById = adminId;
        freeze.AdminDecisionDate = now;
        freeze.UpdatedAt = now;
        subscription.PendingFreezeRequestDate = null;
        ApplyAdminSubscriptionStatus(subscription, SubscriptionStatus.Active, "admin_reject_freeze", now);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Subscription Freeze Rejected", "Subscription", subscriptionId, freeze.RejectionReason, ct);
        return await ProjectSubscriptionForAdminAsync(subscription, ct);
    }

    public Task<object> AdminFreezeSubscriptionAsync(string adminId, string adminName,
        string subscriptionId, FreezeActionRequest request, CancellationToken ct)
        => WithSubscriptionConcurrencyRetryAsync(
            inner => AdminFreezeSubscriptionCoreAsync(adminId, adminName, subscriptionId, request, inner),
            ct);

    private async Task<object> AdminFreezeSubscriptionCoreAsync(string adminId, string adminName,
        string subscriptionId, FreezeActionRequest request, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct)
            ?? throw ApiException.NotFound("subscription_not_found", "Subscription not found.");

        var now = DateTimeOffset.UtcNow;
        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Trial or SubscriptionStatus.FreezeRequested))
        {
            throw ApiException.Conflict("subscription_freeze_invalid_state", "Only active or freeze-requested subscriptions can be frozen.");
        }
        if (CalculateSubscriptionRemainingDays(subscription, now) <= 0)
        {
            throw ApiException.Validation("subscription_no_remaining_days", "A subscription with no remaining days cannot be frozen.");
        }
        if (subscription.TotalFreezeDaysUsed >= subscription.MaxFreezeDaysAllowed)
        {
            throw ApiException.Validation("freeze_allowance_used", "Freeze allowance has already been used.");
        }

        var freeze = await db.SubscriptionFreezes
            .Where(f => f.SubscriptionId == subscriptionId && f.RequestStatus == "pending")
            .OrderByDescending(f => f.FreezeRequestDate)
            .FirstOrDefaultAsync(ct);
        if (freeze is null)
        {
            freeze = new SubscriptionFreeze
            {
                Id = $"subfreeze-{Guid.NewGuid():N}",
                SubscriptionId = subscription.Id,
                UserId = subscription.UserId,
                RequestedBy = "admin",
                RequestStatus = "approved",
                FreezeRequestDate = now,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.SubscriptionFreezes.Add(freeze);
        }
        StartSubscriptionFreeze(subscription, freeze, now, adminId, request.Reason ?? request.InternalNotes ?? "Admin direct freeze.");
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Subscription Frozen", "Subscription", subscriptionId, request.Reason ?? "Admin direct freeze.", ct);
        return await ProjectSubscriptionForAdminAsync(subscription, ct);
    }

    public Task<object> AdminResumeSubscriptionAsync(string adminId, string adminName,
        string subscriptionId, FreezeActionRequest request, CancellationToken ct)
        => WithSubscriptionConcurrencyRetryAsync(
            inner => AdminResumeSubscriptionCoreAsync(adminId, adminName, subscriptionId, request, inner),
            ct);

    private async Task<object> AdminResumeSubscriptionCoreAsync(string adminId, string adminName,
        string subscriptionId, FreezeActionRequest request, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct)
            ?? throw ApiException.NotFound("subscription_not_found", "Subscription not found.");
        if (subscription.Status != SubscriptionStatus.Frozen)
        {
            throw ApiException.Conflict("subscription_resume_invalid_state", "Only frozen subscriptions can be resumed.");
        }

        var freeze = await db.SubscriptionFreezes
            .Where(f => f.SubscriptionId == subscriptionId && f.RequestStatus == "approved" && f.FreezeEndDate == null)
            .OrderByDescending(f => f.FreezeStartDate)
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.Conflict("freeze_record_missing", "Open freeze record was not found.");

        var now = DateTimeOffset.UtcNow;
        ResumeSubscriptionFreeze(subscription, freeze, now);
        freeze.AdminNotes = request.InternalNotes;
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Subscription Resumed", "Subscription", subscriptionId, request.Reason ?? "Admin resumed subscription.", ct);
        return await ProjectSubscriptionForAdminAsync(subscription, ct);
    }

    // Inspect-and-correct the OET 2026 entitlement counters / unlock flags directly on a
    // Subscription. Each request field is an absolute SET (null = leave unchanged); the two
    // counters are clamped to >= 0 so the operator cannot drive entitlement negative. Wrapped
    // in the shared concurrency retry and audited with a before/after JSON snapshot.
    public Task<object> AdjustSubscriptionEntitlementsAsync(string adminId, string adminName,
        string subscriptionId, AdminSubscriptionEntitlementAdjustRequest request, CancellationToken ct)
    {
        return WithSubscriptionConcurrencyRetryAsync(
            inner => AdjustSubscriptionEntitlementsCoreAsync(adminId, adminName, subscriptionId, request, inner),
            ct);
    }

    private async Task<object> AdjustSubscriptionEntitlementsCoreAsync(string adminId, string adminName,
        string subscriptionId, AdminSubscriptionEntitlementAdjustRequest request, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct)
            ?? throw ApiException.NotFound("subscription_not_found", "Subscription not found.");

        var now = DateTimeOffset.UtcNow;
        var before = new
        {
            subscription.WritingAssessmentsRemaining,
            subscription.SpeakingSessionsRemaining,
            subscription.AiCreditsRemaining,
            subscription.TutorBookUnlocked,
            subscription.BasicEnglishUnlocked,
        };

        if (request.WritingAssessmentsRemaining.HasValue)
        {
            subscription.WritingAssessmentsRemaining = Math.Max(0, request.WritingAssessmentsRemaining.Value);
        }
        if (request.SpeakingSessionsRemaining.HasValue)
        {
            subscription.SpeakingSessionsRemaining = Math.Max(0, request.SpeakingSessionsRemaining.Value);
        }
        if (request.AiCreditsRemaining.HasValue)
        {
            subscription.AiCreditsRemaining = Math.Max(0, request.AiCreditsRemaining.Value);
        }
        if (request.TutorBookUnlocked.HasValue)
        {
            subscription.TutorBookUnlocked = request.TutorBookUnlocked.Value;
        }
        if (request.BasicEnglishUnlocked.HasValue)
        {
            subscription.BasicEnglishUnlocked = request.BasicEnglishUnlocked.Value;
        }

        subscription.ChangedAt = now;

        await db.SaveChangesAsync(ct);

        var after = new
        {
            subscription.WritingAssessmentsRemaining,
            subscription.SpeakingSessionsRemaining,
            subscription.AiCreditsRemaining,
            subscription.TutorBookUnlocked,
            subscription.BasicEnglishUnlocked,
        };
        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? "Admin entitlement adjustment"
            : request.Reason.Trim();
        var details = $"reason: {reason}"
            + $"; before: {JsonSerializer.Serialize(before)}"
            + $"; after: {JsonSerializer.Serialize(after)}";
        await LogAuditAsync(adminId, adminName, "SubscriptionEntitlementsAdjusted", "Subscription", subscriptionId, details, ct);

        return new
        {
            subscription.Id,
            subscription.WritingAssessmentsRemaining,
            subscription.SpeakingSessionsRemaining,
            subscription.AiCreditsRemaining,
            subscription.TutorBookUnlocked,
            subscription.BasicEnglishUnlocked,
            subscription.ChangedAt,
        };
    }

    public Task<object> CreateSubscriptionAsync(string adminId, string adminName,
        AdminSubscriptionCreateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            throw ApiException.Validation("user_required", "A learner user id is required.");
        }
        if (string.IsNullOrWhiteSpace(request.PlanCode))
        {
            throw ApiException.Validation("plan_required", "A billing plan code is required.");
        }

        return WithSubscriptionConcurrencyRetryAsync(
            inner => CreateSubscriptionCoreAsync(adminId, adminName, request, inner),
            ct);
    }

    private async Task<object> CreateSubscriptionCoreAsync(string adminId, string adminName,
        AdminSubscriptionCreateRequest request, CancellationToken ct)
    {
        var learner = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, ct)
            ?? throw ApiException.NotFound("user_not_found", "User not found.");

        // Multiple subscriptions per learner are intentional — EffectiveEntitlementResolver
        // aggregates packages additively, so a new grant must never depend on the learner
        // having none.
        var planCode = request.PlanCode.Trim();
        var plan = await db.BillingPlans.FirstOrDefaultAsync(p => p.Code == planCode || p.Id == planCode, ct)
            ?? throw ApiException.Validation("plan_not_found", $"Billing plan '{planCode}' was not found.");

        var now = DateTimeOffset.UtcNow;
        var months = Math.Max(1, plan.DurationMonths);
        var subscription = new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = request.UserId,
            PlanId = plan.Code,
            PlanVersionId = null,
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddMonths(months),
            PriceAmount = plan.Price,
            Currency = plan.Currency,
            Interval = plan.Interval,
        };
        // Canonical six-month rule: stamp bundled counters + ExpiresAt from the
        // plan's AccessDurationDays clamped to the 180-day ceiling. Previously
        // ExpiresAt was left null here, leaving expiry to fall back on
        // NextRenewalAt / AccessDurationDays defaults downstream.
        SubscriptionBundleInitializer.ApplyBundle(subscription, plan, now);
        // One-time packages have no billing renewal: mirror the real access end
        // into NextRenewalAt so lifecycle surfaces never contradict expiry.
        if (!plan.IsRenewable || plan.DurationMonths <= 0)
        {
            subscription.NextRenewalAt = subscription.ExpiresAt ?? subscription.NextRenewalAt;
        }
        db.Subscriptions.Add(subscription);
        learner.CurrentPlanId = plan.Code;

        var creditedAmount = 0;
        if (request.GrantIncludedCredits && plan.IncludedCredits > 0)
        {
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == request.UserId, ct);
            if (wallet is null)
            {
                wallet = new Wallet
                {
                    Id = $"wallet-{Guid.NewGuid():N}",
                    UserId = request.UserId,
                    CreditBalance = 0,
                    LedgerSummaryJson = "[]",
                    LastUpdatedAt = now
                };
                db.Wallets.Add(wallet);
            }
            wallet.CreditBalance += plan.IncludedCredits;
            wallet.LastUpdatedAt = now;
            db.WalletTransactions.Add(new WalletTransaction
            {
                Id = Guid.NewGuid(),
                WalletId = wallet.Id,
                TransactionType = "admin_grant",
                Amount = plan.IncludedCredits,
                BalanceAfter = wallet.CreditBalance,
                ReferenceType = "subscription",
                ReferenceId = subscription.Id,
                Description = $"Subscription created on {plan.Code}: granted {plan.IncludedCredits} credits",
                CreatedBy = adminId,
                CreatedAt = now,
            });
            creditedAmount = plan.IncludedCredits;
        }

        await db.SaveChangesAsync(ct);

        var details = $"Created subscription on plan {plan.Code} for user {request.UserId}"
            + (creditedAmount > 0 ? $"; granted {creditedAmount} credits" : "")
            + (string.IsNullOrWhiteSpace(request.Reason) ? "" : $"; reason: {request.Reason}");
        await LogAuditAsync(adminId, adminName, "Subscription Created", "Subscription", subscription.Id, details, ct);

        return ProjectSubscription(subscription, plan.Name, learner.DisplayName);
    }

    private async Task<string> ResolvePlanNameAsync(string planRef, CancellationToken ct)
    {
        var plan = await db.BillingPlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == planRef || p.Id == planRef, ct);
        return plan?.Name ?? planRef;
    }

    private async Task<string> ResolveUserDisplayNameAsync(string userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.DisplayName })
            .FirstOrDefaultAsync(ct);
        return user?.DisplayName ?? userId;
    }

    private async Task<object> ProjectSubscriptionForAdminAsync(Subscription subscription, CancellationToken ct)
    {
        var planName = await ResolvePlanNameAsync(subscription.PlanId, ct);
        var learnerName = await ResolveUserDisplayNameAsync(subscription.UserId, ct);
        return ProjectSubscription(subscription, planName, learnerName);
    }

    private static int CalculateSubscriptionRemainingDays(Subscription subscription, DateTimeOffset now)
    {
        if (subscription.Status == SubscriptionStatus.Frozen)
        {
            return Math.Max(0, subscription.PreservedRemainingDays ?? 0);
        }

        if (subscription.ExpiresAt is null)
        {
            return Math.Max(1, subscription.AccessDurationDays);
        }

        return Math.Max(0, (int)Math.Ceiling((subscription.ExpiresAt.Value - now).TotalDays));
    }

    private static bool IsSubscriptionExpiringSoon(Subscription subscription, DateTimeOffset now)
    {
        var status = NormalizeSubscriptionStatus(subscription, now);
        return status is "active" or "trial" or "freeze_requested"
            && CalculateSubscriptionRemainingDays(subscription, now) < 14;
    }

    private static string NormalizeSubscriptionStatus(Subscription subscription, DateTimeOffset now)
    {
        if (subscription.Status != SubscriptionStatus.Frozen
            && subscription.ExpiresAt is { } expires
            && expires <= now)
        {
            return "expired";
        }

        return subscription.Status switch
        {
            SubscriptionStatus.PastDue => "past_due",
            SubscriptionStatus.FreezeRequested => "freeze_requested",
            _ => subscription.Status.ToString().ToLowerInvariant(),
        };
    }

    private void CloseOpenSubscriptionFreezeRows(Subscription subscription, DateTimeOffset now, string reason)
    {
        foreach (var freeze in db.SubscriptionFreezes.Where(f => f.SubscriptionId == subscription.Id && f.FreezeEndDate == null))
        {
            freeze.FreezeEndDate = now;
            freeze.RequestStatus = "completed";
            freeze.AdminNotes = reason;
            freeze.UpdatedAt = now;
        }

        subscription.PendingFreezeRequestDate = null;
        subscription.FrozenSince = null;
        subscription.PreservedRemainingDays = null;
    }

    private static void StartSubscriptionFreeze(Subscription subscription, SubscriptionFreeze freeze, DateTimeOffset now, string adminId, string reason)
    {
        var remaining = CalculateSubscriptionRemainingDays(subscription, now);
        if (remaining <= 0)
        {
            throw ApiException.Validation("subscription_no_remaining_days", "A subscription with no remaining days cannot be frozen.");
        }

        subscription.PreservedRemainingDays = remaining;
        subscription.PendingFreezeRequestDate = null;
        subscription.FrozenSince = now;
        ApplyAdminSubscriptionStatus(subscription, SubscriptionStatus.Frozen, "subscription_freeze", now);

        freeze.RequestStatus = "approved";
        freeze.FreezeStartDate = now;
        freeze.PreservedRemainingDaysAtFreeze = remaining;
        freeze.FrozenBy = "admin";
        freeze.FreezeReason = reason;
        freeze.AdminDecisionById = adminId;
        freeze.AdminDecisionDate = now;
        freeze.UpdatedAt = now;
    }

    private static void ResumeSubscriptionFreeze(Subscription subscription, SubscriptionFreeze freeze, DateTimeOffset now)
    {
        if (freeze.FreezeStartDate is null)
        {
            throw ApiException.Conflict("freeze_start_missing", "Open freeze record has no start date.");
        }

        var used = Math.Max(1, (int)Math.Ceiling((now - freeze.FreezeStartDate.Value).TotalDays));
        var allowanceRemaining = Math.Max(0, subscription.MaxFreezeDaysAllowed - subscription.TotalFreezeDaysUsed);
        used = Math.Min(used, allowanceRemaining);
        var preserved = Math.Max(0, subscription.PreservedRemainingDays ?? freeze.PreservedRemainingDaysAtFreeze ?? 0);

        freeze.FreezeEndDate = now;
        freeze.FreezeDaysUsed = used;
        freeze.UpdatedAt = now;

        subscription.TotalFreezeDaysUsed += used;
        subscription.ExpiresAt = now.AddDays(preserved);
        subscription.PreservedRemainingDays = null;
        subscription.FrozenSince = null;
        subscription.PendingFreezeRequestDate = null;
        ApplyAdminSubscriptionStatus(subscription, SubscriptionStatus.Active, "subscription_resume", now);
    }

    private static object ProjectSubscription(Subscription subscription, string? planName, string? userName)
        => new
        {
            subscription.Id,
            subscription.UserId,
            userName = userName ?? subscription.UserId,
            planId = subscription.PlanId,
            planName = planName ?? subscription.PlanId,
            status = NormalizeSubscriptionStatus(subscription, DateTimeOffset.UtcNow),
            subscription.NextRenewalAt,
            subscription.ExpiresAt,
            startDate = subscription.StartedAt == default ? (DateTimeOffset?)null : subscription.StartedAt,
            endDate = subscription.ExpiresAt,
            durationDays = Math.Max(1, subscription.AccessDurationDays),
            remainingDays = CalculateSubscriptionRemainingDays(subscription, DateTimeOffset.UtcNow),
            expiringSoon = IsSubscriptionExpiringSoon(subscription, DateTimeOffset.UtcNow),
            subscription.TotalFreezeDaysUsed,
            maxFreezeDays = subscription.MaxFreezeDaysAllowed,
            freezeAllowanceRemaining = Math.Max(0, subscription.MaxFreezeDaysAllowed - subscription.TotalFreezeDaysUsed),
            subscription.PreservedRemainingDays,
            subscription.PendingFreezeRequestDate,
            subscription.FrozenSince,
            subscription.StartedAt,
            subscription.ChangedAt,
            price = subscription.PriceAmount,
            subscription.Currency,
            subscription.Interval,
            addOnCount = 0
        };
}
