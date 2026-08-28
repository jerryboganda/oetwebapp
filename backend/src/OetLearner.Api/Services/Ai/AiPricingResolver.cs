using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// resolves the single effective <see cref="AiModelPrice"/> row for a
/// provider/model at a given instant and computes cache-aware cost.
///
/// <para>
/// Deliberately does not touch <c>AiGatewayService.ComputeCostEstimateAsync</c>
/// (the existing flat <see cref="AiProvider"/> rate card) — this resolver is
/// the pricing source for W2-migrated paths only (currently the Listening
/// Part A Anthropic direct-call recorder; see
/// <c>Services/Listening/ListeningPartAAiScoringService.Anthropic.cs</c>).
/// Later waves migrate the gateway itself.
/// </para>
/// </summary>
public interface IAiPricingResolver
{
    Task<AiPricingResolution?> ResolveAsync(string providerId, string model, DateTimeOffset instant, CancellationToken ct);
}

/// <summary>Resolved rate-card row for one provider/model at one instant.</summary>
public sealed record AiPricingResolution(
    string ProviderId,
    string Model,
    string PricingVersion,
    decimal InputPer1k,
    decimal OutputPer1k,
    decimal? CacheWritePer1k,
    decimal? CacheReadPer1k)
{
    /// <summary>
    /// Normal (non-cached) input/output tokens bill at the base rate; cache
    /// write/read tokens bill at their own rate only when the provider row
    /// carries one (<see cref="CacheWritePer1k"/>/<see cref="CacheReadPer1k"/>
    /// null ⇒ that tier costs nothing extra beyond the base rate already
    /// charged, matching e.g. OpenAI's automatic, un-metered cache writes).
    /// No token is ever counted twice: normal and cache tokens are disjoint
    /// buckets reported separately by the provider.
    /// </summary>
    public decimal ComputeCostUsd(int normalInputTokens, int normalOutputTokens, int cacheWriteTokens, int cacheReadTokens)
    {
        var cost = normalInputTokens / 1000m * InputPer1k
            + normalOutputTokens / 1000m * OutputPer1k;

        if (cacheWriteTokens > 0 && CacheWritePer1k is { } writeRate)
        {
            cost += cacheWriteTokens / 1000m * writeRate;
        }

        if (cacheReadTokens > 0 && CacheReadPer1k is { } readRate)
        {
            cost += cacheReadTokens / 1000m * readRate;
        }

        return cost;
    }
}

public sealed class AiPricingResolver(LearnerDbContext db) : IAiPricingResolver
{
    public async Task<AiPricingResolution?> ResolveAsync(string providerId, string model, DateTimeOffset instant, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        var row = await db.AiModelPrices
            .AsNoTracking()
            .Where(p => p.ProviderId == providerId
                && p.Model == model
                && p.EffectiveFrom <= instant
                && (p.EffectiveTo == null || p.EffectiveTo > instant))
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

        if (row is null)
        {
            return null;
        }

        return new AiPricingResolution(
            row.ProviderId,
            row.Model,
            row.PricingVersion,
            row.InputPer1k,
            row.OutputPer1k,
            row.CacheWritePer1k,
            row.CacheReadPer1k);
    }
}
