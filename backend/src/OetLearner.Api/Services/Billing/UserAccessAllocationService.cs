using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Admin per-user access allocation for the manual "Add User" feature: grant/remove
/// packages (multiple subscriptions per user), grant add-ons, and declaratively set the
/// per-user scope (module overrides, Materials-folder + Recall-set allow-lists, and the
/// master login-expiry gate). Grants reuse <see cref="SubscriptionBundleInitializer"/> and
/// <see cref="IAddonGrantProcessor"/> so entitlements/AI credits stay consistent and idempotent.
/// </summary>
public sealed class UserAccessAllocationService(
    LearnerDbContext db,
    IAddonGrantProcessor addonGrantProcessor,
    TimeProvider timeProvider)
{
    private static readonly SubscriptionStatus[] LiveStatuses =
    {
        SubscriptionStatus.Active,
        SubscriptionStatus.Trial,
        SubscriptionStatus.FreezeRequested,
        SubscriptionStatus.Frozen,
    };

    public async Task<UserAccessDto> GetAccessAsync(string userId, CancellationToken ct)
    {
        var learner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw ApiException.NotFound("user_not_found", "User not found.");

        var subs = await db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.ChangedAt)
            .ToListAsync(ct);

        var planCodes = subs.Select(s => s.PlanId).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();
        var planNames = await db.BillingPlans.AsNoTracking()
            .Where(p => planCodes.Contains(p.Code))
            .ToDictionaryAsync(p => p.Code, p => p.Name, ct);

        var subIds = subs.Select(s => s.Id).ToList();
        var now = timeProvider.GetUtcNow();
        var addOns = await db.SubscriptionItems.AsNoTracking()
            .Where(i => subIds.Contains(i.SubscriptionId)
                && i.Status == SubscriptionItemStatus.Active
                && i.StartsAt <= now
                && (i.EndsAt == null || i.EndsAt > now))
            .Select(i => new UserAccessAddonDto(i.ItemCode, i.SubscriptionId))
            .ToListAsync(ct);

        var moduleOverrides = await db.UserModuleOverrides.AsNoTracking()
            .Where(o => o.UserId == userId)
            .Select(o => new UserAccessModuleDto(o.ModuleKey, o.Enabled))
            .ToListAsync(ct);

        var folderIds = await db.UserMaterialFolderAccesses.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.FolderId).ToListAsync(ct);

        var recallSetCodes = await db.UserRecallSetAccesses.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.RecallSetCode).ToListAsync(ct);

        var subscriptionDtos = subs.Select(s => new UserAccessSubscriptionDto(
            s.Id,
            s.PlanId,
            planNames.TryGetValue(s.PlanId, out var name) ? name : s.PlanId,
            s.Status.ToString(),
            s.ExpiresAt,
            string.Equals(s.PlanId, learner.CurrentPlanId, StringComparison.OrdinalIgnoreCase))).ToList();

        return new UserAccessDto(
            subscriptionDtos,
            addOns,
            moduleOverrides,
            folderIds,
            recallSetCodes,
            learner.AccessExpiresAt);
    }

    public async Task<UserAccessDto> GrantPackageAsync(
        string adminId, string adminName, string userId, AdminUserAccessPackageRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PlanCode))
        {
            throw ApiException.Validation("plan_required", "A billing plan code is required.");
        }

        var learner = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw ApiException.NotFound("user_not_found", "User not found.");

        var planCode = request.PlanCode.Trim();
        var plan = await db.BillingPlans.FirstOrDefaultAsync(p => p.Code == planCode || p.Id == planCode, ct)
            ?? throw ApiException.Validation("plan_not_found", $"Billing plan '{planCode}' was not found.");

        var now = timeProvider.GetUtcNow();

        // Idempotent: if a live subscription for this plan already exists, adjust it in
        // place (expiry / primary) rather than creating a duplicate row.
        var existing = await db.Subscriptions.FirstOrDefaultAsync(
            s => s.UserId == userId && s.PlanId == plan.Code && LiveStatuses.Contains(s.Status), ct);

        if (existing is not null)
        {
            if (request.ExpiresAt.HasValue) existing.ExpiresAt = request.ExpiresAt;
            existing.ChangedAt = now;
            if (request.MakePrimary) learner.CurrentPlanId = plan.Code;
            await db.SaveChangesAsync(ct);
            await AuditAsync(adminId, adminName, "Package Re-granted", existing.Id,
                $"Adjusted package {plan.Code} for {userId}", ct);
            return await GetAccessAsync(userId, ct);
        }

        var subscription = new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = userId,
            PlanId = plan.Code,
            PlanVersionId = null,
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddMonths(Math.Max(1, plan.DurationMonths)),
            PriceAmount = plan.Price,
            Currency = plan.Currency,
            Interval = plan.Interval,
        };
        // Bundled entitlements + access-duration expiry; then honour a custom expiry.
        SubscriptionBundleInitializer.ApplyBundle(subscription, plan, now);
        if (request.ExpiresAt.HasValue) subscription.ExpiresAt = request.ExpiresAt;
        db.Subscriptions.Add(subscription);

        if (request.MakePrimary || string.IsNullOrWhiteSpace(learner.CurrentPlanId))
        {
            learner.CurrentPlanId = plan.Code;
        }

        if (request.GrantIncludedCredits && plan.IncludedCredits > 0)
        {
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
            if (wallet is null)
            {
                wallet = new Wallet
                {
                    Id = $"wallet-{Guid.NewGuid():N}",
                    UserId = userId,
                    CreditBalance = 0,
                    LedgerSummaryJson = "[]",
                    LastUpdatedAt = now,
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
                Description = $"Admin allocated {plan.Code}: granted {plan.IncludedCredits} credits",
                CreatedBy = adminId,
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
        await AuditAsync(adminId, adminName, "Package Granted", subscription.Id,
            $"Granted package {plan.Code} to {userId}", ct);
        return await GetAccessAsync(userId, ct);
    }

    public async Task<UserAccessDto> RemovePackageAsync(
        string adminId, string adminName, string userId, string subscriptionId, CancellationToken ct)
    {
        var learner = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw ApiException.NotFound("user_not_found", "User not found.");

        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == userId, ct)
            ?? throw ApiException.NotFound("subscription_not_found", "Subscription not found for this user.");

        var now = timeProvider.GetUtcNow();
        sub.Status = SubscriptionStatus.Cancelled;
        sub.ChangedAt = now;

        // If this was the primary plan, repoint to any other still-live subscription.
        if (string.Equals(learner.CurrentPlanId, sub.PlanId, StringComparison.OrdinalIgnoreCase))
        {
            var replacement = await db.Subscriptions
                .Where(s => s.UserId == userId && s.Id != sub.Id && LiveStatuses.Contains(s.Status))
                .OrderByDescending(s => s.ChangedAt)
                .FirstOrDefaultAsync(ct);
            learner.CurrentPlanId = replacement?.PlanId;
        }

        await db.SaveChangesAsync(ct);
        await AuditAsync(adminId, adminName, "Package Removed", sub.Id,
            $"Cancelled package {sub.PlanId} for {userId}", ct);
        return await GetAccessAsync(userId, ct);
    }

    public async Task<UserAccessDto> GrantAddonAsync(
        string adminId, string adminName, string userId, AdminUserAccessAddonRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.AddonCode))
        {
            throw ApiException.Validation("addon_required", "An add-on code is required.");
        }

        var addonCode = request.AddonCode.Trim();
        var addonExists = await db.BillingAddOns.AsNoTracking().AnyAsync(a => a.Code == addonCode, ct);
        if (!addonExists)
        {
            throw ApiException.Validation("addon_not_found", $"Add-on '{addonCode}' was not found.");
        }

        // Resolve target subscription: explicit id, else the user's primary/latest live sub.
        string targetSubId;
        if (!string.IsNullOrWhiteSpace(request.SubscriptionId))
        {
            targetSubId = request.SubscriptionId.Trim();
            var owned = await db.Subscriptions.AsNoTracking()
                .AnyAsync(s => s.Id == targetSubId && s.UserId == userId, ct);
            if (!owned) throw ApiException.Validation("subscription_not_found", "Target subscription not found for this user.");
        }
        else
        {
            var primary = await db.Subscriptions.AsNoTracking()
                .Where(s => s.UserId == userId && LiveStatuses.Contains(s.Status))
                .OrderByDescending(s => s.ChangedAt)
                .FirstOrDefaultAsync(ct)
                ?? throw ApiException.Validation("no_subscription", "The user has no active subscription to attach the add-on to.");
            targetSubId = primary.Id;
        }

        var quantity = Math.Max(1, request.Quantity);
        for (var unit = 0; unit < quantity; unit++)
        {
            // Idempotent per unit: re-submitting the same quantity replays the same eventIds.
            var eventId = $"admin_alloc:{userId}:{addonCode}:{targetSubId}:{unit}";
            await addonGrantProcessor.ApplyAsync(eventId, targetSubId, addonCode, ct);
        }

        await AuditAsync(adminId, adminName, "Add-on Granted", targetSubId,
            $"Granted add-on {addonCode} x{quantity} to {userId}", ct);
        return await GetAccessAsync(userId, ct);
    }

    public async Task<UserAccessDto> PutScopeAsync(
        string adminId, string adminName, string userId, AdminUserAccessScopeRequest request, CancellationToken ct)
    {
        var learner = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw ApiException.NotFound("user_not_found", "User not found.");

        var now = timeProvider.GetUtcNow();

        if (request.Modules is not null)
        {
            var existing = await db.UserModuleOverrides.Where(o => o.UserId == userId).ToListAsync(ct);
            db.UserModuleOverrides.RemoveRange(existing);
            foreach (var m in request.Modules)
            {
                if (string.IsNullOrWhiteSpace(m.ModuleKey)) continue;
                db.UserModuleOverrides.Add(new UserModuleOverride
                {
                    Id = $"umo_{Guid.NewGuid():N}",
                    UserId = userId,
                    ModuleKey = m.ModuleKey.Trim(),
                    Enabled = m.Enabled,
                    UpdatedAt = now,
                });
            }
        }

        if (request.MaterialFolderIds is not null)
        {
            var existing = await db.UserMaterialFolderAccesses.Where(x => x.UserId == userId).ToListAsync(ct);
            db.UserMaterialFolderAccesses.RemoveRange(existing);
            foreach (var folderId in request.MaterialFolderIds.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct())
            {
                db.UserMaterialFolderAccesses.Add(new UserMaterialFolderAccess
                {
                    Id = $"ufa_{Guid.NewGuid():N}",
                    UserId = userId,
                    FolderId = folderId.Trim(),
                    CreatedAt = now,
                });
            }
        }

        if (request.RecallSetCodes is not null)
        {
            var existing = await db.UserRecallSetAccesses.Where(x => x.UserId == userId).ToListAsync(ct);
            db.UserRecallSetAccesses.RemoveRange(existing);
            foreach (var code in request.RecallSetCodes.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct())
            {
                db.UserRecallSetAccesses.Add(new UserRecallSetAccess
                {
                    Id = $"ursa_{Guid.NewGuid():N}",
                    UserId = userId,
                    RecallSetCode = code.Trim().ToLowerInvariant(),
                    CreatedAt = now,
                });
            }
        }

        if (request.ClearAccessExpiry)
        {
            learner.AccessExpiresAt = null;
        }
        else if (request.AccessExpiresAt.HasValue)
        {
            learner.AccessExpiresAt = request.AccessExpiresAt;
        }

        await db.SaveChangesAsync(ct);

        // If the master expiry is now in the past, kill live sessions immediately
        // (mirrors suspend) so the learner is bounced to the renew popup at next request.
        if (learner.AccessExpiresAt is { } exp && exp <= now && !string.IsNullOrWhiteSpace(learner.AuthAccountId))
        {
            var activeTokens = await db.RefreshTokenRecords
                .Where(t => t.ApplicationUserAccountId == learner.AuthAccountId && t.RevokedAt == null)
                .ToListAsync(ct);
            foreach (var token in activeTokens) token.RevokedAt = now;
            if (activeTokens.Count > 0) await db.SaveChangesAsync(ct);
        }

        await AuditAsync(adminId, adminName, "Access Scope Updated", userId,
            $"Updated per-user module/content scope + expiry for {userId}", ct);
        return await GetAccessAsync(userId, ct);
    }

    private async Task AuditAsync(string adminId, string adminName, string action, string resourceId, string details, CancellationToken ct)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = timeProvider.GetUtcNow(),
            ActorId = adminId,
            ActorName = adminName,
            Action = action,
            ResourceType = "UserAccess",
            ResourceId = resourceId,
            Details = details,
        });
        await db.SaveChangesAsync(ct);
    }
}

// ── Response DTOs ──

public record UserAccessDto(
    IReadOnlyList<UserAccessSubscriptionDto> Subscriptions,
    IReadOnlyList<UserAccessAddonDto> AddOns,
    IReadOnlyList<UserAccessModuleDto> ModuleOverrides,
    IReadOnlyList<string> MaterialFolderIds,
    IReadOnlyList<string> RecallSetCodes,
    DateTimeOffset? AccessExpiresAt);

public record UserAccessSubscriptionDto(
    string Id,
    string PlanCode,
    string PlanName,
    string Status,
    DateTimeOffset? ExpiresAt,
    bool IsPrimary);

public record UserAccessAddonDto(string Code, string SubscriptionId);

public record UserAccessModuleDto(string ModuleKey, bool Enabled);
