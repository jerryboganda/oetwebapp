using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public partial class LearnerService
{
    public async Task<object> GetFreezeStatusAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        var user = await EnsureUserAsync(userId, cancellationToken);
        var policy = await GetCurrentFreezePolicyAsync(cancellationToken);
        var currentFreeze = await GetCurrentFreezeRecordAsync(userId, cancellationToken);
        var entitlement = await GetFreezeEntitlementAsync(userId, cancellationToken);
        var eligibility = await BuildFreezeEligibilityAsync(userId, policy, currentFreeze, entitlement, null, cancellationToken);
        var historyQuery = db.AccountFreezeRecords.AsNoTracking()
            .Where(x => x.UserId == userId);
        var history = db.Database.IsSqlite()
            ? (await historyQuery.ToListAsync(cancellationToken))
                .OrderByDescending(x => x.RequestedAt)
                .Take(10)
                .ToList()
            : await historyQuery
                .OrderByDescending(x => x.RequestedAt)
                .Take(10)
                .ToListAsync(cancellationToken);

        return new
        {
            userId = user.Id,
            policy = MapFreezePolicy(policy),
            currentFreeze = currentFreeze is null ? null : MapFreezeRecord(currentFreeze),
            entitlement = entitlement is null ? null : new
            {
                entitlement.Id,
                entitlement.UserId,
                entitlement.FreezeRecordId,
                entitlement.ConsumedAt,
                entitlement.ResetAt,
                entitlement.ResetByAdminId,
                entitlement.ResetByAdminName,
                entitlement.ResetReason,
                used = entitlement.ConsumedAt is not null && entitlement.ResetAt is null
            },
            eligibility = eligibility,
            history = history.Select(MapFreezeRecord).ToList()
        };
    }

    public async Task<object> RequestFreezeAsync(string userId, FreezeRequestRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        var user = await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);

        var policy = await GetCurrentFreezePolicyAsync(cancellationToken);
        if (!policy.IsEnabled || !policy.SelfServiceEnabled)
        {
            throw ApiException.Forbidden("freeze_unavailable", "Freezes are not available for self-service right now.");
        }

        var startAt = request.StartAt ?? DateTimeOffset.UtcNow;
        var endAt = request.EndAt ?? startAt.AddDays(policy.MinDurationDays);
        var durationDays = Math.Max(1, (int)Math.Ceiling((endAt - startAt).TotalDays));
        if (durationDays < policy.MinDurationDays || durationDays > policy.MaxDurationDays || durationDays > 365)
        {
            throw ApiException.Validation("freeze_duration_invalid", $"Freeze duration must be between {policy.MinDurationDays} and {Math.Min(policy.MaxDurationDays, 365)} days.");
        }

        var currentFreeze = await GetCurrentFreezeRecordAsync(userId, cancellationToken);
        if (currentFreeze is not null && currentFreeze.Status is FreezeStatus.Active or FreezeStatus.PendingApproval or FreezeStatus.Scheduled)
        {
            throw ApiException.Conflict("freeze_exists", "There is already an active or pending freeze for this learner.");
        }

        var entitlement = await GetFreezeEntitlementAsync(userId, cancellationToken);
        if (entitlement is not null && entitlement.ConsumedAt is not null && entitlement.ResetAt is null)
        {
            throw ApiException.Conflict("freeze_limit_reached", "Self-service freeze entitlement has already been used for this learner.");
        }

        var eligibility = await BuildFreezeEligibilityAsync(userId, policy, currentFreeze, entitlement, request, cancellationToken);
        if (!eligibility.Eligible)
        {
            throw ApiException.Forbidden("freeze_ineligible", "This learner is not eligible for a self-service freeze.");
        }

        var now = DateTimeOffset.UtcNow;
        var status = policy.ApprovalMode == FreezeApprovalMode.AdminApprovalRequired
            ? FreezeStatus.PendingApproval
            : startAt > now
                ? FreezeStatus.Scheduled
                : FreezeStatus.Active;

        var record = new AccountFreezeRecord
        {
            Id = $"FRZ-{Guid.NewGuid():N}",
            UserId = userId,
            RequestedByLearnerId = userId,
            Status = status,
            IsCurrent = true,
            IsSelfService = true,
            EntitlementConsumed = true,
            EntitlementReset = request.PauseEntitlementClock ?? (policy.EntitlementPauseMode == FreezeEntitlementPauseMode.InternalClock),
            IsOverride = false,
            RequestedAt = now,
            ScheduledStartAt = startAt,
            StartedAt = status == FreezeStatus.Active ? now : null,
            EndedAt = endAt,
            DurationDays = durationDays,
            Reason = (request.Reason ?? string.Empty).Trim(),
            InternalNotes = null,
            PolicySnapshotJson = JsonSupport.Serialize(MapFreezePolicy(policy)),
            PolicyVersionSnapshot = policy.Version,
            EligibilitySnapshotJson = JsonSupport.Serialize(eligibility),
            RejectionReason = null,
            EndReason = null,
            CancellationReason = null,
            UpdatedAt = now
        };

        db.AccountFreezeRecords.Add(record);
        db.AccountFreezeEntitlements.Add(new AccountFreezeEntitlement
        {
            Id = $"FZE-{Guid.NewGuid():N}",
            UserId = userId,
            FreezeRecordId = record.Id,
            ConsumedAt = now,
            ResetAt = null
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw ApiException.Conflict("freeze_limit_reached", "A self-service freeze has already been claimed for this learner.");
        }

        if (record.Status is FreezeStatus.Active or FreezeStatus.Scheduled)
        {
            await QueueFreezeLifecycleJobsAsync(record, cancellationToken);
        }

        await RecordEventAsync(userId, "freeze_requested", new
        {
            freezeId = record.Id,
            record.Status,
            record.ScheduledStartAt,
            record.EndedAt,
            record.DurationDays
        }, cancellationToken);

        if (status == FreezeStatus.Active)
        {
            await RecordEventAsync(userId, "freeze_started", new { freezeId = record.Id, startedAt = now }, cancellationToken);
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerFreezeStarted,
                userId,
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
        else if (status == FreezeStatus.PendingApproval)
        {
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerFreezeRequested,
                userId,
                nameof(AccountFreezeRecord),
                record.Id,
                record.PolicyVersionSnapshot.ToString(),
                new Dictionary<string, object?>
                {
                    ["freezeId"] = record.Id,
                    ["message"] = "Your freeze request is waiting for approval."
                },
                cancellationToken);
            await notifications.CreateForAdminsAsync(
                NotificationEventKey.AdminFreezeLifecycleAction,
                nameof(AccountFreezeRecord),
                record.Id,
                record.PolicyVersionSnapshot.ToString(),
                new Dictionary<string, object?>
                {
                    ["message"] = $"Learner {user.DisplayName} requested a freeze."
                },
                cancellationToken);
        }

        return await GetFreezeStatusAsync(userId, cancellationToken);
    }

    public async Task<object> ConfirmFreezeAsync(string userId, string freezeId, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        await EnsureUserAsync(userId, cancellationToken);

        var record = await db.AccountFreezeRecords.FirstOrDefaultAsync(x => x.Id == freezeId && x.UserId == userId, cancellationToken)
            ?? throw ApiException.NotFound("freeze_not_found", "Freeze request not found.");

        if (record.Status is FreezeStatus.Cancelled or FreezeStatus.Rejected or FreezeStatus.Completed or FreezeStatus.ForceEnded)
        {
            throw ApiException.Conflict("freeze_finalized", "This freeze request can no longer be confirmed.");
        }

        var now = DateTimeOffset.UtcNow;
        record.Status = record.ScheduledStartAt is not null && record.ScheduledStartAt > now
            ? FreezeStatus.Scheduled
            : FreezeStatus.Active;
        record.IsCurrent = true;
        record.StartedAt ??= record.Status == FreezeStatus.Active ? now : null;
        record.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);
        if (record.Status is FreezeStatus.Active or FreezeStatus.Scheduled)
        {
            await QueueFreezeLifecycleJobsAsync(record, cancellationToken);
        }
        await RecordEventAsync(userId, "freeze_confirmed", new { freezeId = record.Id, status = record.Status }, cancellationToken);
        return await GetFreezeStatusAsync(userId, cancellationToken);
    }

    public async Task<object> CancelFreezeAsync(string userId, string freezeId, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        await EnsureUserAsync(userId, cancellationToken);

        var record = await db.AccountFreezeRecords.FirstOrDefaultAsync(x => x.Id == freezeId && x.UserId == userId, cancellationToken)
            ?? throw ApiException.NotFound("freeze_not_found", "Freeze request not found.");

        if (record.Status is FreezeStatus.Active)
        {
            throw ApiException.Conflict("freeze_active", "An active freeze cannot be cancelled; end it instead.");
        }

        if (record.Status is FreezeStatus.Cancelled or FreezeStatus.Rejected or FreezeStatus.Completed or FreezeStatus.ForceEnded)
        {
            return await GetFreezeStatusAsync(userId, cancellationToken);
        }

        record.Status = FreezeStatus.Cancelled;
        record.IsCurrent = false;
        record.CancellationReason = string.IsNullOrWhiteSpace(record.CancellationReason)
            ? "Cancelled by learner"
            : record.CancellationReason;
        record.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        await RecordEventAsync(userId, "freeze_cancelled", new { freezeId = record.Id }, cancellationToken);
        return await GetFreezeStatusAsync(userId, cancellationToken);
    }

    private async Task QueueFreezeLifecycleJobsAsync(AccountFreezeRecord record, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object?>
        {
            ["freezeRecordId"] = record.Id,
            ["userId"] = record.UserId,
            ["status"] = record.Status.ToString(),
            ["scheduledStartAt"] = record.ScheduledStartAt,
            ["endedAt"] = record.EndedAt
        };

        if (record.Status == FreezeStatus.Scheduled && record.ScheduledStartAt is not null)
        {
            await UpsertFreezeJobAsync(
                JobType.FreezeStart,
                $"freeze-start:{record.Id}",
                record.ScheduledStartAt.Value,
                payload,
                cancellationToken);
        }

        if (record.EndedAt is not null)
        {
            await UpsertFreezeJobAsync(
                JobType.FreezeEnd,
                $"freeze-end:{record.Id}",
                record.EndedAt.Value,
                payload,
                cancellationToken);
        }
    }

    private async Task UpsertFreezeJobAsync(
        JobType type,
        string jobId,
        DateTimeOffset availableAt,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var job = await db.BackgroundJobs.FirstOrDefaultAsync(x => x.Id == jobId, cancellationToken);
        if (job is null)
        {
            db.BackgroundJobs.Add(new BackgroundJobItem
            {
                Id = jobId,
                Type = type,
                State = AsyncState.Queued,
                ResourceId = payload.TryGetValue("freezeRecordId", out var resourceId) ? resourceId?.ToString() : null,
                PayloadJson = JsonSupport.Serialize(payload),
                CreatedAt = now,
                AvailableAt = availableAt,
                LastTransitionAt = now,
                StatusReasonCode = "queued",
                StatusMessage = "Freeze lifecycle job queued.",
                Retryable = true,
                RetryCount = 0,
                RetryAfterMs = null
            });
            return;
        }

        job.Type = type;
        job.State = AsyncState.Queued;
        job.ResourceId = payload.TryGetValue("freezeRecordId", out var jobResourceId) ? jobResourceId?.ToString() : job.ResourceId;
        job.PayloadJson = JsonSupport.Serialize(payload);
        job.AvailableAt = availableAt;
        job.LastTransitionAt = now;
        job.StatusReasonCode = "queued";
        job.StatusMessage = "Freeze lifecycle job queued.";
        job.Retryable = true;
        job.RetryCount = 0;
        job.RetryAfterMs = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureLearnerMutationAllowedAsync(string userId, CancellationToken cancellationToken)
    {
        var currentFreeze = await GetCurrentFreezeRecordAsync(userId, cancellationToken);
        if (currentFreeze is null)
        {
            return;
        }

        if (currentFreeze.Status is FreezeStatus.Active or FreezeStatus.Scheduled or FreezeStatus.PendingApproval)
        {
            throw ApiException.Forbidden("account_frozen", "This learner account is frozen and read-only.");
        }
    }

    private async Task<AccountFreezePolicy> GetCurrentFreezePolicyAsync(CancellationToken cancellationToken)
    {
        var policy = await db.AccountFreezePolicies.AsNoTracking()
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(cancellationToken);

        return policy ?? new AccountFreezePolicy
        {
            Id = "global",
            IsEnabled = true,
            SelfServiceEnabled = true,
            ApprovalMode = FreezeApprovalMode.AutoApprove,
            MinDurationDays = 1,
            MaxDurationDays = 365,
            AllowScheduling = true,
            AccessMode = FreezeAccessMode.ReadOnly,
            EntitlementPauseMode = FreezeEntitlementPauseMode.InternalClock,
            RequireReason = true,
            RequireInternalNotes = false,
            AllowActivePaid = true,
            AllowGracePeriod = true,
            AllowTrial = false,
            AllowComplimentary = false,
            AllowCancelled = false,
            AllowExpired = false,
            AllowReviewOnly = false,
            AllowPastDue = true,
            AllowSuspended = false,
            PolicyNotes = "Default freeze policy",
            EligibilityReasonCodesJson = "[]",
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };
    }

    private Task<AccountFreezeRecord?> GetCurrentFreezeRecordAsync(string userId, CancellationToken cancellationToken)
        => db.Database.IsSqlite()
            ? GetCurrentFreezeRecordSqliteAsync(userId, cancellationToken)
            : db.AccountFreezeRecords.AsNoTracking()
                .Where(x => x.UserId == userId && x.IsCurrent)
                .OrderByDescending(x => x.RequestedAt)
                .FirstOrDefaultAsync(cancellationToken);

    private async Task<AccountFreezeRecord?> GetCurrentFreezeRecordSqliteAsync(string userId, CancellationToken cancellationToken)
    {
        var records = await db.AccountFreezeRecords.AsNoTracking()
            .Where(x => x.UserId == userId && x.IsCurrent)
            .ToListAsync(cancellationToken);

        return records
            .OrderByDescending(x => x.RequestedAt)
            .FirstOrDefault();
    }

    private Task<AccountFreezeEntitlement?> GetFreezeEntitlementAsync(string userId, CancellationToken cancellationToken)
        => db.AccountFreezeEntitlements.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

    private async Task<FreezeEligibilityResult> BuildFreezeEligibilityAsync(
        string userId,
        AccountFreezePolicy policy,
        AccountFreezeRecord? currentFreeze,
        AccountFreezeEntitlement? entitlement,
        FreezeRequestRequest? request,
        CancellationToken cancellationToken)
    {
        var subscription = await db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (subscription is null)
        {
            return new FreezeEligibilityResult(
                false,
                false,
                false,
                policy.MaxDurationDays,
                policy.MinDurationDays,
                "unknown",
                ["subscription_missing"],
                policy.Version);
        }

        var currentPlan = await db.BillingPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Code == subscription.PlanId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var reasonCodes = new List<string>();
        var subscriptionState = ToSubscriptionState(subscription.Status);

        if (!policy.IsEnabled)
        {
            reasonCodes.Add("policy_disabled");
        }

        if (!policy.SelfServiceEnabled)
        {
            reasonCodes.Add("self_service_disabled");
        }

        if (currentFreeze is not null && currentFreeze.Status is FreezeStatus.Active or FreezeStatus.PendingApproval or FreezeStatus.Scheduled)
        {
            reasonCodes.Add("current_freeze_exists");
        }

        if (entitlement is not null && entitlement.ConsumedAt is not null && entitlement.ResetAt is null)
        {
            reasonCodes.Add("self_service_entitlement_used");
        }

        if (request is not null)
        {
            if (policy.RequireReason && string.IsNullOrWhiteSpace(request.Reason))
            {
                reasonCodes.Add("reason_required");
            }

            var startAt = request.StartAt ?? now;
            var endAt = request.EndAt ?? startAt.AddDays(policy.MinDurationDays);
            var durationDays = Math.Max(1, (int)Math.Ceiling((endAt - startAt).TotalDays));
            if (durationDays < policy.MinDurationDays || durationDays > policy.MaxDurationDays || durationDays > 365)
            {
                reasonCodes.Add("duration_invalid");
            }
        }

        if (subscription.Status == SubscriptionStatus.Active && !policy.AllowActivePaid)
        {
            reasonCodes.Add("active_paid_excluded");
        }
        else if (subscription.Status == SubscriptionStatus.PastDue && !policy.AllowPastDue && !policy.AllowGracePeriod)
        {
            reasonCodes.Add("past_due_excluded");
        }
        else if (subscription.Status == SubscriptionStatus.Trial && !policy.AllowTrial)
        {
            reasonCodes.Add("trial_excluded");
        }
        else if (subscription.Status == SubscriptionStatus.Cancelled && !policy.AllowCancelled)
        {
            reasonCodes.Add("cancelled_excluded");
        }
        else if (subscription.Status == SubscriptionStatus.Expired && !policy.AllowExpired)
        {
            reasonCodes.Add("expired_excluded");
        }
        else if (subscription.Status == SubscriptionStatus.Suspended && !policy.AllowSuspended)
        {
            reasonCodes.Add("suspended_excluded");
        }

        if (!string.IsNullOrWhiteSpace(currentPlan?.Code) && currentPlan.Code.Contains("complimentary", StringComparison.OrdinalIgnoreCase) && !policy.AllowComplimentary)
        {
            reasonCodes.Add("complimentary_excluded");
        }

        var eligible = reasonCodes.Count == 0;
        return new FreezeEligibilityResult(
            eligible,
            eligible,
            policy.AllowScheduling,
            policy.MaxDurationDays,
            policy.MinDurationDays,
            subscriptionState,
            reasonCodes,
            policy.Version);
    }

    private static object MapFreezePolicy(AccountFreezePolicy policy) => new
    {
        policy.Id,
        policy.IsEnabled,
        policy.SelfServiceEnabled,
        approvalMode = policy.ApprovalMode.ToString(),
        policy.MinDurationDays,
        policy.MaxDurationDays,
        policy.AllowScheduling,
        accessMode = policy.AccessMode.ToString(),
        entitlementPauseMode = policy.EntitlementPauseMode.ToString(),
        policy.RequireReason,
        policy.RequireInternalNotes,
        policy.AllowActivePaid,
        policy.AllowGracePeriod,
        policy.AllowTrial,
        policy.AllowComplimentary,
        policy.AllowCancelled,
        policy.AllowExpired,
        policy.AllowReviewOnly,
        policy.AllowPastDue,
        policy.AllowSuspended,
        policy.PolicyNotes,
        policy.EligibilityReasonCodesJson,
        policy.UpdatedAt,
        policy.Version
    };

    private static object MapFreezeRecord(AccountFreezeRecord record) => new
    {
        record.Id,
        record.UserId,
        record.RequestedByLearnerId,
        record.RequestedByAdminId,
        record.RequestedByAdminName,
        record.ApprovedByAdminId,
        record.ApprovedByAdminName,
        record.RejectedByAdminId,
        record.RejectedByAdminName,
        record.EndedByAdminId,
        record.EndedByAdminName,
        status = record.Status.ToString(),
        record.IsCurrent,
        record.IsSelfService,
        record.EntitlementConsumed,
        record.EntitlementReset,
        record.IsOverride,
        record.RequestedAt,
        record.ScheduledStartAt,
        record.StartedAt,
        record.EndedAt,
        record.DurationDays,
        record.Reason,
        record.InternalNotes,
        record.PolicySnapshotJson,
        record.PolicyVersionSnapshot,
        record.EligibilitySnapshotJson,
        record.RejectionReason,
        record.EndReason,
        record.CancellationReason,
        record.UpdatedAt
    };

    private sealed record FreezeEligibilityResult(
        bool Eligible,
        bool CanRequest,
        bool CanSchedule,
        int MaxDurationDays,
        int MinDurationDays,
        string SubscriptionState,
        IReadOnlyCollection<string> ReasonCodes,
        int PolicyVersion);
}
