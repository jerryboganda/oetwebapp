using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Domain.ValueObjects;

namespace OetWithDrHesham.Api.Services.Billing;

/// <summary>
/// Resolves the active pricing experiment for a (user, target) and applies
/// the variant's price multiplier on top of the base / region price. Records
/// the assignment so analytics can compute uplift per variant.
/// </summary>
public interface IPricingExperimentService
{
    Task<ExperimentResult?> ResolveAsync(string userId, string targetType, string targetId, string region, CancellationToken ct);
    Task<Money> ApplyAsync(Money basePrice, string userId, string targetType, string targetId, string region, CancellationToken ct);
    Task RecordConversionAsync(string userId, string targetType, string targetId, decimal convertedAmount, CancellationToken ct);
}

public sealed record ExperimentVariantSpec(string Code, decimal Weight, decimal PriceMultiplier, string? Currency);
public sealed record ExperimentResult(string ExperimentId, string VariantCode, decimal PriceMultiplier, string? Currency);

public sealed class PricingExperimentService : IPricingExperimentService
{
    private readonly LearnerDbContext _db;
    private readonly IFxRateService _fx;
    private readonly ILogger<PricingExperimentService> _logger;

    public PricingExperimentService(LearnerDbContext db, IFxRateService fx, ILogger<PricingExperimentService> logger)
    {
        _db = db;
        _fx = fx;
        _logger = logger;
    }

    public async Task<ExperimentResult?> ResolveAsync(string userId, string targetType, string targetId, string region, CancellationToken ct)
    {
        var experiment = await _db.PricingExperiments
            .Where(e => e.Status == "running"
                && e.TargetType == targetType
                && e.TargetId == targetId
                && (e.Region == "*" || e.Region == region))
            .OrderByDescending(e => e.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (experiment is null) return null;

        // Existing assignment wins.
        var existing = await _db.PricingExperimentAssignments
            .FirstOrDefaultAsync(a => a.ExperimentId == experiment.Id && a.UserId == userId, ct);
        if (existing is not null)
        {
            var variants = ParseVariants(experiment.VariantsJson);
            var existingSpec = variants.FirstOrDefault(v => v.Code == existing.VariantCode);
            if (existingSpec is null) return null;
            return new ExperimentResult(experiment.Id, existing.VariantCode, existingSpec.PriceMultiplier, existingSpec.Currency);
        }

        // Deterministic per-user hash → percent.
        var userHashPct = HashToPercent(userId, experiment.Id);
        if (userHashPct > experiment.RolloutPercent)
        {
            return null; // user not enrolled (sees control / default price)
        }

        var variantSpecs = ParseVariants(experiment.VariantsJson);
        if (variantSpecs.Count == 0) return null;

        // Weighted draw using the same hash to keep assignment stable across calls.
        var pick = PickVariant(variantSpecs, userHashPct);

        var assignment = new PricingExperimentAssignment
        {
            Id = Guid.NewGuid().ToString("N"),
            ExperimentId = experiment.Id,
            UserId = userId,
            VariantCode = pick.Code,
            Converted = false,
            AssignedAt = DateTimeOffset.UtcNow,
        };
        _db.PricingExperimentAssignments.Add(assignment);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Race condition — another request just assigned the same user.
            existing = await _db.PricingExperimentAssignments
                .FirstOrDefaultAsync(a => a.ExperimentId == experiment.Id && a.UserId == userId, ct);
            if (existing is not null)
            {
                var existingSpec = variantSpecs.FirstOrDefault(v => v.Code == existing.VariantCode);
                if (existingSpec is null) return null;
                return new ExperimentResult(experiment.Id, existing.VariantCode, existingSpec.PriceMultiplier, existingSpec.Currency);
            }
            throw;
        }

        return new ExperimentResult(experiment.Id, pick.Code, pick.PriceMultiplier, pick.Currency);
    }

    public async Task<Money> ApplyAsync(Money basePrice, string userId, string targetType, string targetId, string region, CancellationToken ct)
    {
        var result = await ResolveAsync(userId, targetType, targetId, region, ct);
        if (result is null) return basePrice;

        var multiplied = decimal.Round(basePrice.ToMajor() * result.PriceMultiplier, 4);

        // If variant pins a currency, FX-convert.
        if (!string.IsNullOrWhiteSpace(result.Currency) && !string.Equals(result.Currency, basePrice.Currency, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                multiplied = await _fx.ConvertAsync(multiplied, basePrice.Currency, result.Currency, ct);
                return Money.FromMajor(multiplied, result.Currency);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pricing experiment FX conversion failed; returning unmodified currency.");
            }
        }
        return Money.FromMajor(multiplied, basePrice.Currency);
    }

    public async Task RecordConversionAsync(string userId, string targetType, string targetId, decimal convertedAmount, CancellationToken ct)
    {
        var experiment = await _db.PricingExperiments
            .Where(e => e.Status == "running" && e.TargetType == targetType && e.TargetId == targetId)
            .OrderByDescending(e => e.StartedAt)
            .FirstOrDefaultAsync(ct);
        if (experiment is null) return;

        var assignment = await _db.PricingExperimentAssignments
            .FirstOrDefaultAsync(a => a.ExperimentId == experiment.Id && a.UserId == userId, ct);
        if (assignment is null || assignment.Converted) return;

        assignment.Converted = true;
        assignment.ConvertedAt = DateTimeOffset.UtcNow;
        assignment.ConvertedAmount = convertedAmount;
        await _db.SaveChangesAsync(ct);
    }

    private static List<ExperimentVariantSpec> ParseVariants(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return new();
            var result = new List<ExperimentVariantSpec>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var code = item.TryGetProperty("code", out var c) ? c.GetString() : null;
                if (string.IsNullOrEmpty(code)) continue;
                var weight = item.TryGetProperty("weight", out var w) && w.TryGetDecimal(out var wd) ? wd : 1m;
                var mult = item.TryGetProperty("priceMultiplier", out var p) && p.TryGetDecimal(out var pd) ? pd : 1m;
                var currency = item.TryGetProperty("currency", out var cu) ? cu.GetString() : null;
                result.Add(new ExperimentVariantSpec(code, weight, mult, currency));
            }
            return result;
        }
        catch
        {
            return new();
        }
    }

    private static ExperimentVariantSpec PickVariant(IReadOnlyList<ExperimentVariantSpec> variants, decimal userHashPct)
    {
        var totalWeight = variants.Sum(v => v.Weight);
        if (totalWeight <= 0m) return variants[0];
        var threshold = (userHashPct / 100m) * totalWeight;
        decimal cumulative = 0m;
        foreach (var v in variants)
        {
            cumulative += v.Weight;
            if (threshold <= cumulative) return v;
        }
        return variants[^1];
    }

    private static decimal HashToPercent(string userId, string experimentId)
    {
        var bytes = Encoding.UTF8.GetBytes(experimentId + "::" + userId);
        var hash = SHA256.HashData(bytes);
        var n = BitConverter.ToUInt32(hash, 0);
        return (decimal)(n % 10000) / 100m;
    }
}
