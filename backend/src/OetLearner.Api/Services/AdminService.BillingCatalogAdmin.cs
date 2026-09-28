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

    // ════════════════════════════════════════════
    //  Billing Ops
    // ════════════════════════════════════════════

    public async Task<object> GetBillingPlansAsync(string? status, CancellationToken ct)
    {
        var query = db.BillingPlans.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            var parsedStatus = ParseBillingPlanStatus(status, BillingPlanStatus.Active);
            query = query.Where(p => p.Status == parsedStatus);
        }

        List<BillingPlan> plans;
        if (!db.Database.IsSqlite())
        {
            plans = await query
                .OrderBy(p => p.DisplayOrder)
                .ThenByDescending(p => p.UpdatedAt)
                .ToListAsync(ct);
        }
        else
        {
            plans = (await query.ToListAsync(ct))
                .OrderBy(p => p.DisplayOrder)
                .ThenByDescending(p => p.UpdatedAt)
                .ToList();
        }

        var versionMetadata = await GetBillingPlanVersionMetadataAsync(plans, ct);

        // Pre-fetch "what's included" bullet lists from the linked ContentPackages,
        // keyed by Code (same on both entities). Plans without a matching package
        // surface an empty list.
        var planCodes = plans.Select(p => p.Code).ToList();
        var packageFeatures = await db.ContentPackages.AsNoTracking()
            .Where(p => planCodes.Contains(p.Code))
            .ToDictionaryAsync(p => p.Code, p => p.ComparisonFeaturesJson, ct);

        return plans.Select(plan => MapBillingPlan(
            plan,
            versionMetadata.TryGetValue(plan.Id, out var metadata) ? metadata : EmptyBillingCatalogVersionMetadata,
            packageFeatures.TryGetValue(plan.Code, out var features) ? features : null));
    }

    public async Task<object> CreateBillingPlanAsync(string adminId, string adminName,
        AdminBillingPlanCreateRequest request, CancellationToken ct)
    {
        var validated = await ValidateBillingPlanCatalogAsync(ToBillingPlanCatalogInput(request), existingPlanId: null, BillingPlanStatus.Active, ct);

        var idValue = $"plan-{Guid.NewGuid():N}";
        var id = idValue[..Math.Min(64, idValue.Length)];
        var now = DateTimeOffset.UtcNow;

        var plan = new BillingPlan
        {
            Id = id,
            Code = validated.Code,
            Name = validated.Name,
            Description = validated.Description,
            Price = validated.Price,
            Currency = validated.Currency,
            Interval = validated.Interval,
            DurationMonths = validated.DurationMonths,
            IncludedCredits = validated.IncludedCredits,
            DisplayOrder = validated.DisplayOrder,
            IsVisible = validated.IsVisible,
            IsRenewable = validated.IsRenewable,
            TrialDays = validated.TrialDays,
            DiagnosticMockEntitlement = validated.DiagnosticMockEntitlement,
            IncludedSubtestsJson = validated.IncludedSubtestsJson,
            EntitlementsJson = validated.EntitlementsJson,
            ActiveSubscribers = 0,
            Status = validated.Status,
            CreatedAt = now,
            UpdatedAt = now
        };
        ApplyOet2026Fields(plan, validated.Oet2026);

        var version = CreateBillingPlanVersion(plan, 1, adminId, adminName, now);
        plan.ActiveVersionId = version.Id;
        plan.LatestVersionId = version.Id;

        db.BillingPlans.Add(plan);
        db.BillingPlanVersions.Add(version);
        await db.SaveChangesAsync(ct);

        await SyncContentPackageComparisonFeaturesAsync(plan.Code, validated.Oet2026.ComparisonFeaturesJson, now, ct);
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Created", "BillingPlan", id, $"Created plan: {validated.Name}", ct);
        var freshFeatures = await db.ContentPackages.AsNoTracking()
            .Where(p => p.Code == plan.Code)
            .Select(p => p.ComparisonFeaturesJson)
            .FirstOrDefaultAsync(ct);
        return MapBillingPlan(plan, null, freshFeatures);
    }

    public async Task<object> UpdateBillingPlanAsync(string adminId, string adminName, string planId, AdminBillingPlanUpdateRequest request, CancellationToken ct)
    {
        var plan = await db.BillingPlans.FirstOrDefaultAsync(p => p.Id == planId || p.Code == planId, ct)
            ?? throw ApiException.NotFound("billing_plan_not_found", "Billing plan not found.");

        await ThrowIfCatalogCodeChangedWithAuditAsync(adminId, adminName, "BillingPlan", plan.Id, request.Code, plan.Code, "billing_plan_invalid", "Billing plan catalog data is invalid.", ct);
        var validated = await ValidateBillingPlanCatalogAsync(ToBillingPlanCatalogInput(request), plan.Id, plan.Status, ct);
        var now = DateTimeOffset.UtcNow;
        var latestVersionNumber = await EnsureBillingPlanVersionBaselineAsync(plan, adminId, adminName, now, ct);
        plan.Code = validated.Code;
        plan.Name = validated.Name;
        plan.Description = validated.Description;
        plan.Price = validated.Price;
        plan.Currency = validated.Currency;
        plan.Interval = validated.Interval;
        plan.DurationMonths = validated.DurationMonths;
        plan.IncludedCredits = validated.IncludedCredits;
        plan.DisplayOrder = validated.DisplayOrder;
        plan.IsVisible = validated.IsVisible;
        plan.IsRenewable = validated.IsRenewable;
        plan.TrialDays = validated.TrialDays;
        plan.DiagnosticMockEntitlement = validated.DiagnosticMockEntitlement;
        plan.IncludedSubtestsJson = validated.IncludedSubtestsJson;
        plan.EntitlementsJson = validated.EntitlementsJson;
        plan.Status = validated.Status;
        plan.ArchivedAt = plan.Status == BillingPlanStatus.Archived ? now : plan.ArchivedAt;
        plan.UpdatedAt = now;
        ApplyOet2026Fields(plan, validated.Oet2026);

        var version = CreateBillingPlanVersion(plan, latestVersionNumber + 1, adminId, adminName, now);
        plan.ActiveVersionId = version.Id;
        plan.LatestVersionId = version.Id;
        db.BillingPlanVersions.Add(version);

        await db.SaveChangesAsync(ct);

        await SyncContentPackageComparisonFeaturesAsync(plan.Code, validated.Oet2026.ComparisonFeaturesJson, now, ct);
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Updated", "BillingPlan", plan.Id, $"Updated plan: {validated.Name}", ct);
        var freshFeatures = await db.ContentPackages.AsNoTracking()
            .Where(p => p.Code == plan.Code)
            .Select(p => p.ComparisonFeaturesJson)
            .FirstOrDefaultAsync(ct);
        return MapBillingPlan(plan, null, freshFeatures);
    }

    public async Task<object> GetBillingAddOnsAsync(string? status, CancellationToken ct)
    {
        var query = db.BillingAddOns.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            var parsedStatus = ParseBillingAddOnStatus(status, BillingAddOnStatus.Active);
            query = query.Where(addOn => addOn.Status == parsedStatus);
        }

        List<BillingAddOn> addOns;
        if (!db.Database.IsSqlite())
        {
            addOns = await query
                .OrderBy(addOn => addOn.DisplayOrder)
                .ThenByDescending(addOn => addOn.UpdatedAt)
                .ToListAsync(ct);
        }
        else
        {
            addOns = (await query.ToListAsync(ct))
                .OrderBy(addOn => addOn.DisplayOrder)
                .ThenByDescending(addOn => addOn.UpdatedAt)
                .ToList();
        }

        var versionMetadata = await GetBillingAddOnVersionMetadataAsync(addOns, ct);
        return addOns.Select(addOn => MapBillingAddOn(
            addOn,
            versionMetadata.TryGetValue(addOn.Id, out var metadata) ? metadata : EmptyBillingCatalogVersionMetadata));
    }

    /// <summary>Keys must look like <c>billing.section.name</c> — lowercase-rooted, dotted, alnum + . _ - only.</summary>
    private static bool IsValidBillingContentKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 128) return false;
        if (!key.StartsWith("billing.", StringComparison.Ordinal)) return false;
        foreach (var c in key)
        {
            var ok = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-';
            if (!ok) return false;
        }
        return true;
    }

    public async Task<object> GetBillingContentAsync(CancellationToken ct)
    {
        var rows = await db.BillingContentStrings.AsNoTracking()
            .OrderBy(x => x.Section).ThenBy(x => x.Key)
            .ToListAsync(ct);
        return new
        {
            entries = rows.Select(x => new
            {
                key = x.Key,
                section = x.Section,
                value = x.Value,
                description = x.Description,
                updatedAt = x.UpdatedAt,
                updatedByAdminName = x.UpdatedByAdminName,
            }).ToList()
        };
    }

    /// <summary>
    /// Atomic upsert of learner-billing copy overrides. Only stores overrides — the canonical
    /// defaults live in the frontend, so an absent key simply renders its default. Rejects
    /// malformed keys so junk never lands in the store.
    /// </summary>
    public async Task<object> ReplaceBillingContentAsync(string adminId, string adminName, AdminBillingContentReplaceRequest request, CancellationToken ct)
    {
        var entries = request.Entries ?? Array.Empty<AdminBillingContentEntry>();
        var normalized = new List<(string Key, string Value, string Section, string? Description)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var key = (entry.Key ?? string.Empty).Trim();
            if (!IsValidBillingContentKey(key))
            {
                throw ApiException.Validation("billing_content_invalid_key", $"Invalid billing content key: '{key}'.");
            }
            if (!seen.Add(key)) continue;
            var value = entry.Value ?? string.Empty;
            if (value.Length > 4000) value = value[..4000];
            var section = (entry.Section ?? string.Empty).Trim();
            if (section.Length > 64) section = section[..64];
            var description = entry.Description?.Trim();
            if (description is { Length: > 256 }) description = description[..256];
            normalized.Add((key, value, section, description));
        }

        var keys = normalized.Select(n => n.Key).ToList();
        var existing = await db.BillingContentStrings.Where(x => keys.Contains(x.Key)).ToListAsync(ct);
        var existingByKey = existing.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        var changed = new List<string>();
        foreach (var n in normalized)
        {
            if (existingByKey.TryGetValue(n.Key, out var row))
            {
                if (row.Value != n.Value || row.Section != n.Section || row.Description != n.Description)
                {
                    row.Value = n.Value;
                    row.Section = n.Section;
                    row.Description = n.Description;
                    row.UpdatedAt = now;
                    row.UpdatedByAdminId = adminId;
                    row.UpdatedByAdminName = adminName;
                    changed.Add(n.Key);
                }
            }
            else
            {
                db.BillingContentStrings.Add(new BillingContentString
                {
                    Key = n.Key,
                    Section = n.Section,
                    Value = n.Value,
                    Description = n.Description,
                    UpdatedAt = now,
                    UpdatedByAdminId = adminId,
                    UpdatedByAdminName = adminName,
                });
                changed.Add(n.Key);
            }
        }

        await db.SaveChangesAsync(ct);
        if (changed.Count > 0)
        {
            await LogAuditAsync(adminId, adminName, "Updated", "BillingContent", "billing-content",
                $"Updated {changed.Count} billing copy string(s): {string.Join(", ", changed.Take(20))}", ct);
        }
        return await GetBillingContentAsync(ct);
    }

    public async Task<object> DeleteBillingContentAsync(string adminId, string adminName, string key, CancellationToken ct)
    {
        var normalizedKey = (key ?? string.Empty).Trim();
        if (!IsValidBillingContentKey(normalizedKey))
        {
            throw ApiException.Validation("billing_content_invalid_key", $"Invalid billing content key: '{normalizedKey}'.");
        }

        var row = await db.BillingContentStrings.FirstOrDefaultAsync(x => x.Key == normalizedKey, ct);
        if (row is null)
        {
            return new { key = normalizedKey, deleted = false };
        }

        db.BillingContentStrings.Remove(row);
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Deleted", "BillingContent", normalizedKey,
            $"Deleted billing copy override: {normalizedKey}", ct);

        return new { key = normalizedKey, deleted = true };
    }

    public async Task<object> CreateBillingAddOnAsync(string adminId, string adminName, AdminBillingAddOnCreateRequest request, CancellationToken ct)
    {
        var validated = await ValidateBillingAddOnCatalogAsync(ToBillingAddOnCatalogInput(request), existingAddOnId: null, BillingAddOnStatus.Active, ct);

        var addOnIdValue = $"addon-{Guid.NewGuid():N}";
        var id = addOnIdValue[..Math.Min(64, addOnIdValue.Length)];
        var now = DateTimeOffset.UtcNow;
        var addOn = new BillingAddOn
        {
            Id = id,
            Code = validated.Code,
            Name = validated.Name,
            Description = validated.Description,
            Price = validated.Price,
            Currency = validated.Currency,
            Interval = validated.Interval,
            DurationDays = validated.DurationDays,
            GrantCredits = validated.GrantCredits,
            DisplayOrder = validated.DisplayOrder,
            IsRecurring = validated.IsRecurring,
            AppliesToAllPlans = validated.AppliesToAllPlans,
            IsStackable = validated.IsStackable,
            QuantityStep = validated.QuantityStep,
            MaxQuantity = validated.MaxQuantity,
            Status = validated.Status,
            CompatiblePlanCodesJson = validated.CompatiblePlanCodesJson,
            GrantEntitlementsJson = validated.GrantEntitlementsJson,
            CreatedAt = now,
            UpdatedAt = now
        };
        ApplyOet2026Fields(addOn, validated.Oet2026);

        var version = CreateBillingAddOnVersion(addOn, 1, adminId, adminName, now);
        addOn.ActiveVersionId = version.Id;
        addOn.LatestVersionId = version.Id;

        db.BillingAddOns.Add(addOn);
        db.BillingAddOnVersions.Add(version);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Created", "BillingAddOn", addOn.Id, $"Created add-on: {validated.Name}", ct);
        return MapBillingAddOn(addOn);
    }

    public async Task<object> UpdateBillingAddOnAsync(string adminId, string adminName, string addOnId, AdminBillingAddOnUpdateRequest request, CancellationToken ct)
    {
        var addOn = await db.BillingAddOns.FirstOrDefaultAsync(addOn => addOn.Id == addOnId || addOn.Code == addOnId, ct)
            ?? throw ApiException.NotFound("billing_addon_not_found", "Billing add-on not found.");

        await ThrowIfCatalogCodeChangedWithAuditAsync(adminId, adminName, "BillingAddOn", addOn.Id, request.Code, addOn.Code, "billing_addon_invalid", "Billing add-on catalog data is invalid.", ct);
        var validated = await ValidateBillingAddOnCatalogAsync(ToBillingAddOnCatalogInput(request), addOn.Id, addOn.Status, ct);
        var now = DateTimeOffset.UtcNow;
        var latestVersionNumber = await EnsureBillingAddOnVersionBaselineAsync(addOn, adminId, adminName, now, ct);
        addOn.Code = validated.Code;
        addOn.Name = validated.Name;
        addOn.Description = validated.Description;
        addOn.Price = validated.Price;
        addOn.Currency = validated.Currency;
        addOn.Interval = validated.Interval;
        addOn.DurationDays = validated.DurationDays;
        addOn.GrantCredits = validated.GrantCredits;
        addOn.DisplayOrder = validated.DisplayOrder;
        addOn.IsRecurring = validated.IsRecurring;
        addOn.AppliesToAllPlans = validated.AppliesToAllPlans;
        addOn.IsStackable = validated.IsStackable;
        addOn.QuantityStep = validated.QuantityStep;
        addOn.MaxQuantity = validated.MaxQuantity;
        addOn.Status = validated.Status;
        addOn.CompatiblePlanCodesJson = validated.CompatiblePlanCodesJson;
        addOn.GrantEntitlementsJson = validated.GrantEntitlementsJson;
        addOn.UpdatedAt = now;
        ApplyOet2026Fields(addOn, validated.Oet2026);

        var version = CreateBillingAddOnVersion(addOn, latestVersionNumber + 1, adminId, adminName, now);
        addOn.ActiveVersionId = version.Id;
        addOn.LatestVersionId = version.Id;
        db.BillingAddOnVersions.Add(version);

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "BillingAddOn", addOn.Id, $"Updated add-on: {validated.Name}", ct);
        return MapBillingAddOn(addOn);
    }

    /// <summary>
    /// Hard-deletes a billing plan after verifying no live references exist
    /// (active subscribers, historical subscriptions, billing quotes).
    /// Returns 409 (ApiException with status conflict) if references exist —
    /// callers are expected to archive instead.
    /// Also deletes the plan's BillingPlanVersion rows and the matching
    /// ContentPackage marketing row (so the row can be safely reseeded).
    /// </summary>
    public async Task<object> DeleteBillingPlanAsync(string adminId, string adminName, string planId, CancellationToken ct)
    {
        var plan = await db.BillingPlans.FirstOrDefaultAsync(p => p.Id == planId || p.Code == planId, ct)
            ?? throw ApiException.NotFound("billing_plan_not_found", "Billing plan not found.");

        if (plan.ActiveSubscribers > 0)
        {
            throw ApiException.Conflict(
                "billing_plan_in_use",
                $"Plan has {plan.ActiveSubscribers} active subscriber(s). Archive the plan instead of deleting it.");
        }

        var subscriptionExists = await db.Subscriptions.AsNoTracking()
            .AnyAsync(s => s.PlanId == plan.Id, ct);
        if (subscriptionExists)
        {
            throw ApiException.Conflict(
                "billing_plan_in_use",
                "Plan has historical subscription rows. Archive the plan instead of deleting it.");
        }

        var quoteExists = await db.BillingQuotes.AsNoTracking()
            .AnyAsync(q => q.PlanCode == plan.Code, ct);
        if (quoteExists)
        {
            throw ApiException.Conflict(
                "billing_plan_in_use",
                "Plan has historical billing quotes. Archive the plan instead of deleting it.");
        }

        // Detach the plan first so EF doesn't track-and-delete it twice when
        // ExecuteDeleteAsync runs (which is a tracker-bypassing SQL DELETE).
        var planName = plan.Name;
        var planCode = plan.Code;
        var resolvedPlanId = plan.Id;
        db.Entry(plan).State = EntityState.Detached;

        // BillingPlanVersion is guarded by an EF SaveChanges interceptor that
        // forbids Modified/Deleted on snapshot rows. ExecuteDeleteAsync issues
        // a single SQL DELETE without going through the ChangeTracker, so the
        // interceptor never sees it — which is what we want for hard-delete.
        await db.BillingPlanVersions.Where(v => v.PlanId == resolvedPlanId).ExecuteDeleteAsync(ct);
        await db.ContentPackages.Where(p => p.Code == planCode).ExecuteDeleteAsync(ct);
        await db.BillingPlans.Where(p => p.Id == resolvedPlanId).ExecuteDeleteAsync(ct);

        await LogAuditAsync(adminId, adminName, "Deleted", "BillingPlan", resolvedPlanId, $"Hard-deleted plan {planCode}: {planName}", ct);
        return new { id = resolvedPlanId, code = planCode, deleted = true };
    }

    /// <summary>
    /// Hard-deletes a billing add-on after verifying no live references exist
    /// (subscription items, billing quotes). Returns 409 when references
    /// exist — callers should archive instead.
    /// </summary>
    public async Task<object> DeleteBillingAddOnAsync(string adminId, string adminName, string addOnId, CancellationToken ct)
    {
        var addOn = await db.BillingAddOns.FirstOrDefaultAsync(a => a.Id == addOnId || a.Code == addOnId, ct)
            ?? throw ApiException.NotFound("billing_addon_not_found", "Billing add-on not found.");

        var itemExists = await db.SubscriptionItems.AsNoTracking()
            .AnyAsync(i => i.ItemCode == addOn.Code && i.ItemType == "addon", ct);
        if (itemExists)
        {
            throw ApiException.Conflict(
                "billing_addon_in_use",
                "Add-on has historical subscription items. Archive the add-on instead of deleting it.");
        }

        var quoteExists = await db.BillingQuotes.AsNoTracking()
            .AnyAsync(q => q.AddOnCodesJson.Contains(addOn.Code), ct);
        if (quoteExists)
        {
            throw ApiException.Conflict(
                "billing_addon_in_use",
                "Add-on has historical billing quotes. Archive the add-on instead of deleting it.");
        }

        var addOnName = addOn.Name;
        var addOnCode = addOn.Code;
        var resolvedAddOnId = addOn.Id;
        db.Entry(addOn).State = EntityState.Detached;

        // Same rationale as DeleteBillingPlanAsync — bypass the immutability
        // interceptor by issuing SQL DELETEs that don't touch the ChangeTracker.
        await db.BillingAddOnVersions.Where(v => v.AddOnId == resolvedAddOnId).ExecuteDeleteAsync(ct);
        await db.ContentPackages.Where(p => p.Code == addOnCode).ExecuteDeleteAsync(ct);
        await db.BillingAddOns.Where(a => a.Id == resolvedAddOnId).ExecuteDeleteAsync(ct);

        await LogAuditAsync(adminId, adminName, "Deleted", "BillingAddOn", resolvedAddOnId, $"Hard-deleted add-on {addOnCode}: {addOnName}", ct);
        return new { id = resolvedAddOnId, code = addOnCode, deleted = true };
    }

    public async Task<object> GetBillingCouponsAsync(string? status, CancellationToken ct)
    {
        var query = db.BillingCoupons.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            var parsedStatus = ParseBillingCouponStatus(status, BillingCouponStatus.Active);
            query = query.Where(coupon => coupon.Status == parsedStatus);
        }

        var coupons = await ToOrderedListDescendingAsync(query, coupon => coupon.CreatedAt, ct);

        var versionMetadata = await GetBillingCouponVersionMetadataAsync(coupons, ct);
        return coupons.Select(coupon => MapBillingCoupon(
            coupon,
            versionMetadata.TryGetValue(coupon.Id, out var metadata) ? metadata : EmptyBillingCatalogVersionMetadata));
    }

    public async Task<object> CreateBillingCouponAsync(string adminId, string adminName, AdminBillingCouponCreateRequest request, CancellationToken ct)
    {
        var validated = await ValidateBillingCouponCatalogAsync(ToBillingCouponCatalogInput(request), existingCouponId: null, BillingCouponStatus.Active, ct);

        var couponIdValue = $"coupon-{Guid.NewGuid():N}";
        var id = couponIdValue[..Math.Min(64, couponIdValue.Length)];
        var now = DateTimeOffset.UtcNow;
        var coupon = new BillingCoupon
        {
            Id = id,
            Code = validated.Code,
            Name = validated.Name,
            Description = validated.Description,
            DiscountType = validated.DiscountType,
            DiscountValue = validated.DiscountValue,
            Currency = validated.Currency,
            StartsAt = validated.StartsAt,
            EndsAt = validated.EndsAt,
            UsageLimitTotal = validated.UsageLimitTotal,
            UsageLimitPerUser = validated.UsageLimitPerUser,
            MinimumSubtotal = validated.MinimumSubtotal,
            IsStackable = validated.IsStackable,
            Status = validated.Status,
            ApplicablePlanCodesJson = validated.ApplicablePlanCodesJson,
            ApplicableAddOnCodesJson = validated.ApplicableAddOnCodesJson,
            Notes = validated.Notes,
            CreatedAt = now,
            UpdatedAt = now
        };

        var version = CreateBillingCouponVersion(coupon, 1, adminId, adminName, now);
        coupon.ActiveVersionId = version.Id;
        coupon.LatestVersionId = version.Id;

        db.BillingCoupons.Add(coupon);
        db.BillingCouponVersions.Add(version);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Created", "BillingCoupon", coupon.Id, $"Created coupon: {validated.Code}", ct);
        return MapBillingCoupon(coupon);
    }

    public async Task<object> UpdateBillingCouponAsync(string adminId, string adminName, string couponId, AdminBillingCouponUpdateRequest request, CancellationToken ct)
    {
        var coupon = await db.BillingCoupons.FirstOrDefaultAsync(coupon => coupon.Id == couponId || coupon.Code == couponId, ct)
            ?? throw ApiException.NotFound("billing_coupon_not_found", "Billing coupon not found.");

        await ThrowIfCatalogCodeChangedWithAuditAsync(adminId, adminName, "BillingCoupon", coupon.Id, request.Code, coupon.Code, "billing_coupon_invalid", "Billing coupon catalog data is invalid.", ct);
        var validated = await ValidateBillingCouponCatalogAsync(ToBillingCouponCatalogInput(request), coupon.Id, coupon.Status, ct);
        var now = DateTimeOffset.UtcNow;
        var latestVersionNumber = await EnsureBillingCouponVersionBaselineAsync(coupon, adminId, adminName, now, ct);
        coupon.Code = validated.Code;
        coupon.Name = validated.Name;
        coupon.Description = validated.Description;
        coupon.DiscountType = validated.DiscountType;
        coupon.DiscountValue = validated.DiscountValue;
        coupon.Currency = validated.Currency;
        coupon.StartsAt = validated.StartsAt;
        coupon.EndsAt = validated.EndsAt;
        coupon.UsageLimitTotal = validated.UsageLimitTotal;
        coupon.UsageLimitPerUser = validated.UsageLimitPerUser;
        coupon.MinimumSubtotal = validated.MinimumSubtotal;
        coupon.IsStackable = validated.IsStackable;
        coupon.Status = validated.Status;
        coupon.ApplicablePlanCodesJson = validated.ApplicablePlanCodesJson;
        coupon.ApplicableAddOnCodesJson = validated.ApplicableAddOnCodesJson;
        coupon.Notes = validated.Notes;
        coupon.UpdatedAt = now;

        var version = CreateBillingCouponVersion(coupon, latestVersionNumber + 1, adminId, adminName, now);
        coupon.ActiveVersionId = version.Id;
        coupon.LatestVersionId = version.Id;
        db.BillingCouponVersions.Add(version);

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "BillingCoupon", coupon.Id, $"Updated coupon: {validated.Code}", ct);
        return MapBillingCoupon(coupon);
    }

    public async Task<AdminBillingCatalogVersionHistoryResponse> GetBillingPlanVersionsAsync(string planId, CancellationToken ct)
    {
        var plan = await db.BillingPlans.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == planId || item.Code == planId, ct)
            ?? throw ApiException.NotFound("billing_plan_not_found", "Billing plan not found.");

        var versions = await db.BillingPlanVersions.AsNoTracking()
            .Where(version => version.PlanId == plan.Id)
            .OrderByDescending(version => version.VersionNumber)
            .ToListAsync(ct);

        var metadata = CreateCatalogVersionMetadata(
            versions.Select(version => (version.Id, version.VersionNumber)),
            plan.ActiveVersionId,
            plan.LatestVersionId);

        var subject = new AdminBillingCatalogSubjectResponse(
            "plan",
            plan.Id,
            plan.Code,
            plan.Name,
            plan.ActiveVersionId,
            metadata.ActiveVersionNumber,
            plan.LatestVersionId,
            metadata.LatestVersionNumber,
            metadata.VersionCount);

        return new AdminBillingCatalogVersionHistoryResponse(
            subject,
            versions.Select(version => MapBillingPlanVersion(plan, version)).ToList());
    }

    public async Task<AdminBillingCatalogVersionHistoryResponse> GetBillingAddOnVersionsAsync(string addOnId, CancellationToken ct)
    {
        var addOn = await db.BillingAddOns.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == addOnId || item.Code == addOnId, ct)
            ?? throw ApiException.NotFound("billing_addon_not_found", "Billing add-on not found.");

        var versions = await db.BillingAddOnVersions.AsNoTracking()
            .Where(version => version.AddOnId == addOn.Id)
            .OrderByDescending(version => version.VersionNumber)
            .ToListAsync(ct);

        var metadata = CreateCatalogVersionMetadata(
            versions.Select(version => (version.Id, version.VersionNumber)),
            addOn.ActiveVersionId,
            addOn.LatestVersionId);

        var subject = new AdminBillingCatalogSubjectResponse(
            "add_on",
            addOn.Id,
            addOn.Code,
            addOn.Name,
            addOn.ActiveVersionId,
            metadata.ActiveVersionNumber,
            addOn.LatestVersionId,
            metadata.LatestVersionNumber,
            metadata.VersionCount);

        return new AdminBillingCatalogVersionHistoryResponse(
            subject,
            versions.Select(version => MapBillingAddOnVersion(addOn, version)).ToList());
    }

    public async Task<AdminBillingCatalogVersionHistoryResponse> GetBillingCouponVersionsAsync(string couponId, CancellationToken ct)
    {
        var coupon = await db.BillingCoupons.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == couponId || item.Code == couponId, ct)
            ?? throw ApiException.NotFound("billing_coupon_not_found", "Billing coupon not found.");

        var versions = await db.BillingCouponVersions.AsNoTracking()
            .Where(version => version.CouponId == coupon.Id)
            .OrderByDescending(version => version.VersionNumber)
            .ToListAsync(ct);

        var metadata = CreateCatalogVersionMetadata(
            versions.Select(version => (version.Id, version.VersionNumber)),
            coupon.ActiveVersionId,
            coupon.LatestVersionId);

        var subject = new AdminBillingCatalogSubjectResponse(
            "coupon",
            coupon.Id,
            coupon.Code,
            coupon.Name,
            coupon.ActiveVersionId,
            metadata.ActiveVersionNumber,
            coupon.LatestVersionId,
            metadata.LatestVersionNumber,
            metadata.VersionCount);

        return new AdminBillingCatalogVersionHistoryResponse(
            subject,
            versions.Select(version => MapBillingCouponVersion(coupon, version)).ToList());
    }

    private static AdminBillingCatalogVersionResponse MapBillingPlanVersion(BillingPlan plan, BillingPlanVersion version) => new(
        version.Id,
        version.PlanId,
        version.VersionNumber,
        version.Code,
        version.Name,
        version.Description,
        version.Status.ToString().ToLowerInvariant(),
        string.Equals(version.Id, plan.ActiveVersionId, StringComparison.Ordinal),
        string.Equals(version.Id, plan.LatestVersionId, StringComparison.Ordinal),
        version.CreatedByAdminId,
        version.CreatedByAdminName,
        version.CreatedAt,
        new Dictionary<string, object?>
        {
            ["price"] = version.Price,
            ["currency"] = version.Currency,
            ["interval"] = version.Interval,
            ["durationMonths"] = version.DurationMonths,
            ["includedCredits"] = version.IncludedCredits,
            ["displayOrder"] = version.DisplayOrder,
            ["isVisible"] = version.IsVisible,
            ["isRenewable"] = version.IsRenewable,
            ["trialDays"] = version.TrialDays,
            ["includedSubtests"] = JsonSupport.Deserialize<List<string>>(version.IncludedSubtestsJson, []),
            ["entitlements"] = JsonSupport.Deserialize<Dictionary<string, object?>>(version.EntitlementsJson, new Dictionary<string, object?>()),
            ["archivedAt"] = version.ArchivedAt
        });

    private static AdminBillingCatalogVersionResponse MapBillingAddOnVersion(BillingAddOn addOn, BillingAddOnVersion version) => new(
        version.Id,
        version.AddOnId,
        version.VersionNumber,
        version.Code,
        version.Name,
        version.Description,
        version.Status.ToString().ToLowerInvariant(),
        string.Equals(version.Id, addOn.ActiveVersionId, StringComparison.Ordinal),
        string.Equals(version.Id, addOn.LatestVersionId, StringComparison.Ordinal),
        version.CreatedByAdminId,
        version.CreatedByAdminName,
        version.CreatedAt,
        new Dictionary<string, object?>
        {
            ["price"] = version.Price,
            ["currency"] = version.Currency,
            ["interval"] = version.Interval,
            ["durationDays"] = version.DurationDays,
            ["grantCredits"] = version.GrantCredits,
            ["displayOrder"] = version.DisplayOrder,
            ["isRecurring"] = version.IsRecurring,
            ["appliesToAllPlans"] = version.AppliesToAllPlans,
            ["isStackable"] = version.IsStackable,
            ["quantityStep"] = version.QuantityStep,
            ["maxQuantity"] = version.MaxQuantity,
            ["compatiblePlanCodes"] = JsonSupport.Deserialize<List<string>>(version.CompatiblePlanCodesJson, []),
            ["grantEntitlements"] = JsonSupport.Deserialize<Dictionary<string, object?>>(version.GrantEntitlementsJson, new Dictionary<string, object?>())
        });

    private static AdminBillingCatalogVersionResponse MapBillingCouponVersion(BillingCoupon coupon, BillingCouponVersion version) => new(
        version.Id,
        version.CouponId,
        version.VersionNumber,
        version.Code,
        version.Name,
        version.Description,
        version.Status.ToString().ToLowerInvariant(),
        string.Equals(version.Id, coupon.ActiveVersionId, StringComparison.Ordinal),
        string.Equals(version.Id, coupon.LatestVersionId, StringComparison.Ordinal),
        version.CreatedByAdminId,
        version.CreatedByAdminName,
        version.CreatedAt,
        new Dictionary<string, object?>
        {
            ["discountType"] = MapBillingDiscountType(version.DiscountType),
            ["discountValue"] = version.DiscountValue,
            ["currency"] = version.Currency,
            ["startsAt"] = version.StartsAt,
            ["endsAt"] = version.EndsAt,
            ["usageLimitTotal"] = version.UsageLimitTotal,
            ["usageLimitPerUser"] = version.UsageLimitPerUser,
            ["minimumSubtotal"] = version.MinimumSubtotal,
            ["isStackable"] = version.IsStackable,
            ["applicablePlanCodes"] = JsonSupport.Deserialize<List<string>>(version.ApplicablePlanCodesJson, []),
            ["applicableAddOnCodes"] = JsonSupport.Deserialize<List<string>>(version.ApplicableAddOnCodesJson, []),
            ["notes"] = version.Notes
        });
}
