using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Phase 3 production tax resolver. Looks up <see cref="TaxRule"/> by buyer country
/// and applies reverse-charge for B2B with valid foreign VAT IDs.
/// </summary>
public sealed class TaxResolver : IRegionTaxResolver
{
    private readonly LearnerDbContext _db;

    public TaxResolver(LearnerDbContext db) => _db = db;

    public async Task<TaxBreakdown> ResolveAsync(TaxResolutionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerCountry))
        {
            return TaxBreakdown.Empty;
        }

        var country = request.BuyerCountry.ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;

        var rules = await _db.TaxRules
            .Where(r => r.IsActive
                && r.Country == country
                && r.EffectiveFrom <= now
                && (r.EffectiveTo == null || r.EffectiveTo > now))
            .ToListAsync(ct);

        if (rules.Count == 0)
        {
            return TaxBreakdown.Empty;
        }

        var lines = new List<TaxLine>();
        decimal total = 0m;

        foreach (var rule in rules)
        {
            // B2B reverse-charge: zero-rate when buyer has a valid VAT ID from a different country.
            if (request.TreatAsB2B && rule.ZeroRateForB2BReverseCharge
                && !string.IsNullOrWhiteSpace(request.BuyerVatId)
                && IsValidVatIdShape(request.BuyerVatId)
                && !request.BuyerVatId.StartsWith(country, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add(new TaxLine(rule.TaxType, $"{rule.DisplayName} (reverse-charge)", 0m, 0m));
                continue;
            }

            var taxAmount = decimal.Round(request.SubtotalAmount * rule.RatePercent / 100m, 2, MidpointRounding.AwayFromZero);
            lines.Add(new TaxLine(rule.TaxType, rule.DisplayName, rule.RatePercent, taxAmount));
            total += taxAmount;
        }

        return new TaxBreakdown(lines, total);
    }

    /// <summary>Minimal shape check; production should call VIES (EU) / GCC tax authority APIs.</summary>
    private static bool IsValidVatIdShape(string vatId)
    {
        if (vatId.Length < 4) return false;
        return char.IsLetter(vatId[0]) && char.IsLetter(vatId[1]) && vatId.Skip(2).Any(char.IsDigit);
    }
}
