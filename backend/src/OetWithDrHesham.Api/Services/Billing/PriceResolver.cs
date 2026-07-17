using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Domain.ValueObjects;

namespace OetWithDrHesham.Api.Services.Billing;

/// <summary>
/// Resolves the final Money to charge for a billing target in a region.
/// Falls back to the target's own Currency/Price columns when no override exists.
/// </summary>
public interface IPriceResolver
{
    Task<ResolvedPrice> ResolvePlanAsync(string planId, string region, CancellationToken ct);
    Task<ResolvedPrice> ResolveAddOnAsync(string addOnId, string region, CancellationToken ct);
}

public sealed record ResolvedPrice(Money Money, string Source);

public sealed class PriceResolver : IPriceResolver
{
    private readonly LearnerDbContext _db;

    public PriceResolver(LearnerDbContext db) => _db = db;

    public async Task<ResolvedPrice> ResolvePlanAsync(string planId, string region, CancellationToken ct)
    {
        var normalizedRegion = string.IsNullOrWhiteSpace(region) ? BillingRegions.RestOfWorld : region.ToUpperInvariant();

        var override_ = await _db.RegionPricings
            .Where(r => r.TargetType == RegionPricingTargetTypes.Plan
                && r.TargetId == planId
                && r.Region == normalizedRegion
                && r.IsActive)
            .Select(r => new { r.PriceAmount, r.Currency })
            .FirstOrDefaultAsync(ct);

        if (override_ is not null)
        {
            return new ResolvedPrice(Money.FromMajor(override_.PriceAmount, override_.Currency), "region_override");
        }

        var plan = await _db.BillingPlans
            .Where(p => p.Id == planId)
            .Select(p => new { p.Price, p.Currency })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Plan not found: {planId}");

        return new ResolvedPrice(Money.FromMajor(plan.Price, plan.Currency), "plan_default");
    }

    public async Task<ResolvedPrice> ResolveAddOnAsync(string addOnId, string region, CancellationToken ct)
    {
        var normalizedRegion = string.IsNullOrWhiteSpace(region) ? BillingRegions.RestOfWorld : region.ToUpperInvariant();

        var override_ = await _db.RegionPricings
            .Where(r => r.TargetType == RegionPricingTargetTypes.AddOn
                && r.TargetId == addOnId
                && r.Region == normalizedRegion
                && r.IsActive)
            .Select(r => new { r.PriceAmount, r.Currency })
            .FirstOrDefaultAsync(ct);

        if (override_ is not null)
        {
            return new ResolvedPrice(Money.FromMajor(override_.PriceAmount, override_.Currency), "region_override");
        }

        var addon = await _db.BillingAddOns
            .Where(a => a.Id == addOnId)
            .Select(a => new { a.Price, a.Currency })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"AddOn not found: {addOnId}");

        return new ResolvedPrice(Money.FromMajor(addon.Price, addon.Currency), "addon_default");
    }
}
