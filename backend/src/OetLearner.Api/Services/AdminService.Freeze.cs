using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public partial class AdminService
{
    public async Task<object> GetFreezeOverviewAsync(CancellationToken ct)
    {
        var policy = await GetCurrentFreezePolicyAsync(ct);
        var records = await db.AccountFreezeRecords.AsNoTracking()
            .OrderByDescending(x => x.RequestedAt)
            .Take(100)
            .ToListAsync(ct);

        return new
        {
            generatedAt = timeProvider.GetUtcNow(),
            policy = MapFreezePolicy(policy),
            counts = new
            {
                active = records.Count(x => x.Status == FreezeStatus.Active),
                pending = records.Count(x => x.Status == FreezeStatus.PendingApproval),
                scheduled = records.Count(x => x.Status == FreezeStatus.Scheduled),
                cancelled = records.Count(x => x.Status == FreezeStatus.Cancelled),
                rejected = records.Count(x => x.Status == FreezeStatus.Rejected),
                ended = records.Count(x => x.Status is FreezeStatus.Completed or FreezeStatus.ForceEnded)
            },
            records = records.Select(MapFreezeRecord).ToList()
        };
    }

    public async Task<object> UpdateFreezePolicyAsync(string adminId, string adminName, FreezePolicyRequest request, CancellationToken ct)
    {
        var currentPolicy = await GetCurrentFreezePolicyAsync(ct);
        var trackedPolicy = await db.AccountFreezePolicies.FirstOrDefaultAsync(x => x.Id == currentPolicy.Id, ct);
        var now = timeProvider.GetUtcNow();

        if (!Enum.TryParse<FreezeApprovalMode>(request.ApprovalMode, true, out var approvalMode))
        {
            throw ApiException.Validation("freeze_policy_invalid", "Unsupported freeze approval mode.");
        }

        if (!Enum.TryParse<FreezeAccessMode>(request.AccessMode, true, out var accessMode))
        {
            throw ApiException.Validation("freeze_policy_invalid", "Unsupported freeze access mode.");
        }

        if (!Enum.TryParse<FreezeEntitlementPauseMode>(request.EntitlementPauseMode, true, out var entitlementPauseMode))
        {
            throw ApiException.Validation("freeze_policy_invalid", "Unsupported freeze entitlement pause mode.");
        }

        if (request.MinDurationDays <= 0 || request.MaxDurationDays < request.MinDurationDays || request.MaxDurationDays > 365)
        {
            throw ApiException.Validation("freeze_policy_invalid", "Freeze duration limits must stay within 1 to 365 days.");
        }

        var nextPolicy = trackedPolicy ?? new AccountFreezePolicy { Id = currentPolicy.Id };
        nextPolicy.IsEnabled = request.IsEnabled;
        nextPolicy.SelfServiceEnabled = request.SelfServiceEnabled;
        nextPolicy.ApprovalMode = approvalMode;
        nextPolicy.MinDurationDays = request.MinDurationDays;
        nextPolicy.MaxDurationDays = request.MaxDurationDays;
        nextPolicy.AllowScheduling = request.AllowScheduling;
        nextPolicy.AccessMode = accessMode;
        nextPolicy.EntitlementPauseMode = entitlementPauseMode;
        nextPolicy.RequireReason = request.RequireReason;
        nextPolicy.RequireInternalNotes = request.RequireInternalNotes;
        nextPolicy.AllowActivePaid = request.AllowActivePaid;
        nextPolicy.AllowGracePeriod = request.AllowGracePeriod;
        nextPolicy.AllowTrial = request.AllowTrial;
        nextPolicy.AllowComplimentary = request.AllowComplimentary;
        nextPolicy.AllowCancelled = request.AllowCancelled;
        nextPolicy.AllowExpired = request.AllowExpired;
        nextPolicy.AllowReviewOnly = request.AllowReviewOnly;
        nextPolicy.AllowPastDue = request.AllowPastDue;
        nextPolicy.AllowSuspended = request.AllowSuspended;
        nextPolicy.PolicyNotes = request.PolicyNotes ?? string.Empty;
        nextPolicy.EligibilityReasonCodesJson = request.EligibilityReasonCodesJson ?? "[]";
        nextPolicy.UpdatedByAdminId = adminId;
        nextPolicy.UpdatedByAdminName = adminName;
        nextPolicy.UpdatedAt = now;
        nextPolicy.Version = currentPolicy.Version + 1;

        if (trackedPolicy is null)
        {
            db.AccountFreezePolicies.Add(nextPolicy);
        }
        else
        {
            db.AccountFreezePolicies.Update(nextPolicy);
        }
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(
            adminId,
            adminName,
            "Updated Freeze Policy",
            nameof(AccountFreezePolicy),
            nextPolicy.Id,
            $"Freeze policy updated to version {nextPolicy.Version}.",
            ct);

        await NotifyAdminsAsync(
            NotificationEventKey.AdminFreezePolicyChanged,
            nameof(AccountFreezePolicy),
            nextPolicy.Id,
            nextPolicy.Version.ToString(),
            $"Freeze policy updated to version {nextPolicy.Version}.",
            ct);

        return await GetFreezeOverviewAsync(ct);
    }

    public async Task<object> CreateManualFreezeAsync(string adminId, string adminName, FreezeManualCreateRequest request, CancellationToken ct)
    {
        var target = await ResolveUserTargetAsync(request.UserId, ct);
        var policy = await GetCurrentFreezePolicyAsync(ct);
        var currentFreeze = await db.AccountFreezeRecords.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == request.UserId && x.IsCurrent, ct);
        var entitlement = await db.AccountFreezeEntitlements.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == request.UserId, ct);

        if (currentFreeze is not null && currentFreeze.Status is FreezeStatus.Active or FreezeStatus.PendingApproval or FreezeStatus.Scheduled)
        {
            throw ApiException.Conflict("freeze_exists", "This learner already has a current freeze record.");
        }

        var startAt = request.StartAt ?? timeProvider.GetUtcNow();
        var endAt = request.EndAt ?? startAt.AddDays(policy.MinDurationDays);
        var durationDays = Math.Max(1, (int)Math.Ceiling((endAt - startAt).TotalDays));
        if (durationDays < policy.MinDurationDays || durationDays > policy.MaxDurationDays || durationDays > 365)
        {
            throw ApiException.Validation("freeze_duration_invalid", $"Freeze duration must be between {policy.MinDurationDays} and {Math.Min(policy.MaxDurationDays, 365)} days.");
        }

        var eligibility = await BuildFreezeEligibilityAsync(
            request.UserId,
            policy,
            currentFreeze,
            entitlement,
            new FreezeRequestRequest(startAt, endAt, request.Reason, request.PauseEntitlementClock),
            ct);

        if (!request.OverrideEligibility.GetValueOrDefault() && !eligibility.Eligible)
        {
            throw ApiException.Forbidden("freeze_ineligible", "This learner is not eligible for a freeze under the current policy.");
        }

        var now = timeProvider.GetUtcNow();
        var status = startAt > now ? FreezeStatus.Scheduled : FreezeStatus.Active;
        var record = new AccountFreezeRecord
        {
            Id = $"FRZ-{Guid.NewGuid():N}",
            UserId = request.UserId,
            RequestedByAdminId = adminId,
            RequestedByAdminName = adminName,
            ApprovedByAdminId = adminId,
            ApprovedByAdminName = adminName,
            Status = status,
            IsCurrent = true,
            IsSelfService = false,
            EntitlementConsumed = false,
            EntitlementReset = request.PauseEntitlementClock ?? (policy.EntitlementPauseMode == FreezeEntitlementPauseMode.InternalClock),
            IsOverride = request.OverrideEligibility.GetValueOrDefault(),
            RequestedAt = now,
            ScheduledStartAt = startAt,
            StartedAt = status == FreezeStatus.Active ? now : null,
            EndedAt = endAt,
            DurationDays = durationDays,
            Reason = request.Reason?.Trim() ?? string.Empty,
            InternalNotes = request.InternalNotes?.Trim(),
            PolicySnapshotJson = JsonSupport.Serialize(MapFreezePolicy(policy)),
            PolicyVersionSnapshot = policy.Version,
            EligibilitySnapshotJson = JsonSupport.Serialize(eligibility),
            UpdatedAt = now
        };

        db.AccountFreezeRecords.Add(record);
        await db.SaveChangesAsync(ct);
        await QueueFreezeLifecycleJobsAsync(record, ct);

        await LogAuditAsync(
            adminId,
            adminName,
            "Created Freeze",
            nameof(AccountFreezeRecord),
            record.Id,
            $"Created freeze for {target.Name} ({request.UserId}).",
            ct);

        await notifications.CreateForLearnerAsync(
            status == FreezeStatus.Active ? NotificationEventKey.LearnerFreezeStarted : NotificationEventKey.LearnerFreezeApproved,
            request.UserId,
            nameof(AccountFreezeRecord),
            record.Id,
            record.PolicyVersionSnapshot.ToString(),
            new Dictionary<string, object?>
            {
                ["freezeId"] = record.Id,
                ["message"] = status == FreezeStatus.Active ? "Your freeze is now active." : "Your freeze has been approved."
            },
            ct);

        await notifications.CreateForAdminsAsync(
            NotificationEventKey.AdminFreezeLifecycleAction,
            nameof(AccountFreezeRecord),
            record.Id,
            record.PolicyVersionSnapshot.ToString(),
            new Dictionary<string, object?>
            {
                ["message"] = $"Admin freeze created for {target.Name}.",
                ["userId"] = target.Id
            },
            ct);

        return await GetFreezeOverviewAsync(ct);
    }

    public async Task<object> ApproveFreezeAsync(string adminId, string adminName, string freezeId, FreezeActionRequest request, CancellationToken ct)
    {
        var record = await db.AccountFreezeRecords.FirstOrDefaultAsync(x => x.Id == freezeId, ct)
            ?? throw ApiException.NotFound("freeze_not_found", "Freeze record not found.");

        if (record.Status is not FreezeStatus.PendingApproval)
        {
            throw ApiException.Conflict("freeze_not_pending", "Only pending freeze requests can be approved.");
        }

        var now = timeProvider.GetUtcNow();
        record.ApprovedByAdminId = adminId;
        record.ApprovedByAdminName = adminName;
        record.Status = record.ScheduledStartAt is not null && record.ScheduledStartAt > now
            ? FreezeStatus.Scheduled
            : FreezeStatus.Active;
        record.StartedAt ??= record.Status == FreezeStatus.Active ? now : null;
        record.IsCurrent = true;
        record.UpdatedAt = now;
        if (!string.IsNullOrWhiteSpace(request.InternalNotes))
        {
            record.InternalNotes = request.InternalNotes.Trim();
        }

        await db.SaveChangesAsync(ct);
        await QueueFreezeLifecycleJobsAsync(record, ct);

        await LogAuditAsync(adminId, adminName, "Approved Freeze", nameof(AccountFreezeRecord), record.Id, $"Approved freeze for user {record.UserId}.", ct);

        await notifications.CreateForLearnerAsync(
            record.Status == FreezeStatus.Active ? NotificationEventKey.LearnerFreezeStarted : NotificationEventKey.LearnerFreezeApproved,
            record.UserId,
            nameof(AccountFreezeRecord),
            record.Id,
            record.PolicyVersionSnapshot.ToString(),
            new Dictionary<string, object?>
            {
                ["freezeId"] = record.Id,
                ["message"] = record.Status == FreezeStatus.Active ? "Your freeze is now active." : "Your freeze has been approved."
            },
            ct);

        await notifications.CreateForAdminsAsync(
            NotificationEventKey.AdminFreezeLifecycleAction,
            nameof(AccountFreezeRecord),
            record.Id,
            record.PolicyVersionSnapshot.ToString(),
            new Dictionary<string, object?>
            {
                ["message"] = $"Freeze approved for user {record.UserId}.",
                ["userId"] = record.UserId
            },
            ct);

        return await GetFreezeOverviewAsync(ct);
    }

    public async Task<object> RejectFreezeAsync(string adminId, string adminName, string freezeId, FreezeActionRequest request, CancellationToken ct)
    {
        var record = await db.AccountFreezeRecords.FirstOrDefaultAsync(x => x.Id == freezeId, ct)
            ?? throw ApiException.NotFound("freeze_not_found", "Freeze record not found.");

        if (record.Status is FreezeStatus.Completed or FreezeStatus.ForceEnded)
        {
            throw ApiException.Conflict("freeze_finalized", "This freeze has already ended.");
        }

        record.RejectedByAdminId = adminId;
        record.RejectedByAdminName = adminName;
        record.RejectionReason = request.Reason?.Trim();
        record.Status = FreezeStatus.Rejected;
        record.IsCurrent = false;
        record.UpdatedAt = timeProvider.GetUtcNow();
        if (!string.IsNullOrWhiteSpace(request.InternalNotes))
        {
            record.InternalNotes = request.InternalNotes.Trim();
        }

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Rejected Freeze", nameof(AccountFreezeRecord), record.Id, $"Rejected freeze for user {record.UserId}.", ct);

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerFreezeRejected,
            record.UserId,
            nameof(AccountFreezeRecord),
            record.Id,
            record.PolicyVersionSnapshot.ToString(),
            new Dictionary<string, object?>
            {
                ["freezeId"] = record.Id,
                ["message"] = "Your freeze request was rejected."
            },
            ct);

        await notifications.CreateForAdminsAsync(
            NotificationEventKey.AdminFreezeLifecycleAction,
            nameof(AccountFreezeRecord),
            record.Id,
            record.PolicyVersionSnapshot.ToString(),
            new Dictionary<string, object?>
            {
                ["message"] = $"Freeze rejected for user {record.UserId}.",
                ["userId"] = record.UserId
            },
            ct);

        return await GetFreezeOverviewAsync(ct);
    }

    public async Task<object> EndFreezeAsync(string adminId, string adminName, string freezeId, FreezeActionRequest request, CancellationToken ct)
    {
        return await EndFreezeCoreAsync(adminId, adminName, freezeId, request, FreezeStatus.Completed, ct);
    }

    public async Task<object> ForceEndFreezeAsync(string adminId, string adminName, string freezeId, FreezeActionRequest request, CancellationToken ct)
    {
        return await EndFreezeCoreAsync(adminId, adminName, freezeId, request, FreezeStatus.ForceEnded, ct);
    }

    private async Task<object> EndFreezeCoreAsync(string adminId, string adminName, string freezeId, FreezeActionRequest request, FreezeStatus finalStatus, CancellationToken ct)
    {
        var record = await db.AccountFreezeRecords.FirstOrDefaultAsync(x => x.Id == freezeId, ct)
            ?? throw ApiException.NotFound("freeze_not_found", "Freeze record not found.");

        if (record.Status is FreezeStatus.Cancelled or FreezeStatus.Rejected or FreezeStatus.Completed or FreezeStatus.ForceEnded)
        {
            return await GetFreezeOverviewAsync(ct);
        }

        record.EndedByAdminId = adminId;
        record.EndedByAdminName = adminName;
        record.EndReason = request.Reason?.Trim();
        record.Status = finalStatus;
        record.IsCurrent = false;
        record.EndedAt = timeProvider.GetUtcNow();
        record.UpdatedAt = record.EndedAt ?? timeProvider.GetUtcNow();
        if (!string.IsNullOrWhiteSpace(request.InternalNotes))
        {
            record.InternalNotes = request.InternalNotes.Trim();
        }

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, finalStatus == FreezeStatus.ForceEnded ? "Force End Freeze" : "End Freeze", nameof(AccountFreezeRecord), record.Id, $"Ended freeze for user {record.UserId}.", ct);

        await notifications.CreateForLearnerAsync(
            finalStatus == FreezeStatus.ForceEnded ? NotificationEventKey.LearnerFreezeEnded : NotificationEventKey.LearnerFreezeEnded,
            record.UserId,
            nameof(AccountFreezeRecord),
            record.Id,
            record.PolicyVersionSnapshot.ToString(),
            new Dictionary<string, object?>
            {
                ["freezeId"] = record.Id,
                ["message"] = finalStatus == FreezeStatus.ForceEnded ? "Your freeze was force-ended by an administrator." : "Your freeze has ended."
            },
            ct);

        await notifications.CreateForAdminsAsync(
            NotificationEventKey.AdminFreezeLifecycleAction,
            nameof(AccountFreezeRecord),
            record.Id,
            record.PolicyVersionSnapshot.ToString(),
            new Dictionary<string, object?>
            {
                ["message"] = finalStatus == FreezeStatus.ForceEnded
                    ? $"Freeze force-ended for user {record.UserId}."
                    : $"Freeze ended for user {record.UserId}.",
                ["userId"] = record.UserId
            },
            ct);

        return await GetFreezeOverviewAsync(ct);
    }

    private async Task QueueFreezeLifecycleJobsAsync(AccountFreezeRecord record, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
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
            await UpsertFreezeJobAsync(JobType.FreezeStart, $"freeze-start:{record.Id}", record.ScheduledStartAt.Value, payload, ct);
        }

        if (record.EndedAt is not null)
        {
            await UpsertFreezeJobAsync(JobType.FreezeEnd, $"freeze-end:{record.Id}", record.EndedAt.Value, payload, ct);
        }
    }

    private async Task UpsertFreezeJobAsync(JobType type, string jobId, DateTimeOffset availableAt, IReadOnlyDictionary<string, object?> payload, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var job = await db.BackgroundJobs.FirstOrDefaultAsync(x => x.Id == jobId, ct);
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
        }
        else
        {
            job.Type = type;
            job.State = AsyncState.Queued;
            job.ResourceId = payload.TryGetValue("freezeRecordId", out var resourceId) ? resourceId?.ToString() : job.ResourceId;
            job.PayloadJson = JsonSupport.Serialize(payload);
            job.AvailableAt = availableAt;
            job.LastTransitionAt = now;
            job.StatusReasonCode = "queued";
            job.StatusMessage = "Freeze lifecycle job queued.";
            job.Retryable = true;
            job.RetryCount = 0;
            job.RetryAfterMs = null;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<AccountFreezePolicy> GetCurrentFreezePolicyAsync(CancellationToken ct)
    {
        var policy = await db.AccountFreezePolicies.AsNoTracking()
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);

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
            UpdatedAt = timeProvider.GetUtcNow(),
            Version = 1
        };
    }

    private async Task<FreezeEligibilityResult> BuildFreezeEligibilityAsync(
        string userId,
        AccountFreezePolicy policy,
        AccountFreezeRecord? currentFreeze,
        AccountFreezeEntitlement? entitlement,
        FreezeRequestRequest? request,
        CancellationToken ct)
    {
        var subscription = await db.Subscriptions.AsNoTracking().FirstAsync(x => x.UserId == userId, ct);
        var currentPlan = await db.BillingPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Code == subscription.PlanId, ct);
        var reasonCodes = new List<string>();

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

        if (request is not null && policy.RequireReason && string.IsNullOrWhiteSpace(request.Reason))
        {
            reasonCodes.Add("reason_required");
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

        return new FreezeEligibilityResult(
            reasonCodes.Count == 0,
            policy.AllowScheduling,
            policy.MaxDurationDays,
            policy.MinDurationDays,
            subscription.Status.ToString().ToLowerInvariant(),
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
        policy.UpdatedByAdminId,
        policy.UpdatedByAdminName,
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
        bool CanSchedule,
        int MaxDurationDays,
        int MinDurationDays,
        string SubscriptionState,
        IReadOnlyCollection<string> ReasonCodes,
        int PolicyVersion);
}
