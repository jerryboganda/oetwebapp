using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Shared freeze-entitlement consumption and freeze-record projection used by
/// the admin, learner and background-job freeze paths, so the entitlement
/// invariant lives in one place. Does not call SaveChanges; callers own the
/// transaction boundary.
/// </summary>
internal static class AccountFreezeEntitlements
{
    internal static async Task ConsumeSelfServiceAsync(LearnerDbContext db, AccountFreezeRecord record, DateTimeOffset consumedAt, CancellationToken ct)
    {
        if (!record.IsSelfService || record.EntitlementConsumed)
        {
            return;
        }

        var entitlement = await db.AccountFreezeEntitlements.FirstOrDefaultAsync(x => x.UserId == record.UserId, ct);
        if (entitlement is null)
        {
            db.AccountFreezeEntitlements.Add(new AccountFreezeEntitlement
            {
                Id = $"FZE-{Guid.NewGuid():N}",
                UserId = record.UserId,
                FreezeRecordId = record.Id,
                ConsumedAt = consumedAt,
                ResetAt = null
            });
        }
        else
        {
            entitlement.FreezeRecordId = record.Id;
            entitlement.ConsumedAt = consumedAt;
            entitlement.ResetAt = null;
            entitlement.ResetByAdminId = null;
            entitlement.ResetByAdminName = null;
            entitlement.ResetReason = null;
        }

        record.EntitlementConsumed = true;
    }

    internal static object MapRecord(AccountFreezeRecord record) => new
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
}
