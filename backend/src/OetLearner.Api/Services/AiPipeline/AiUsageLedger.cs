using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.AiPipeline;

/// <summary>
/// List prices (USD per 1,000 tokens) for the Claude models the platform routes grading through
/// (owner directive 2026-10-10, cost transparency). The provider row's flat price is ONE number per
/// provider, so it priced every <c>anthropic</c> call at the row's default model (Sonnet-class,
/// $3/$15 per million) even when the pipeline sent <c>claude-opus-5-5</c> ($4/$20), and it ignored
/// prompt-cache tokens entirely although the grading prompts are cached. This card is the single
/// model-aware price source of the AI Pipelines dashboard, the promotional-credit ledger and the
/// credit guard. It is a REPORTING rate card: it never feeds per-call budget enforcement.
/// Cache write is the 5-minute ephemeral rate (the only TTL the platform requests).
/// </summary>
public static class AiModelRateCard
{
    public sealed record Rate(decimal InputPer1k, decimal OutputPer1k, decimal CacheWritePer1k, decimal CacheReadPer1k);

    /// <summary>Date the card was last checked against Anthropic's published API prices.</summary>
    public const string VerifiedOn = "2026-10-10";

    private static readonly Dictionary<string, Rate> Rates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-opus-5-5"] = new(0.004m, 0.020m, 0.005m, 0.0002m),
        ["claude-opus-5"] = new(0.005m, 0.025m, 0.00625m, 0.0005m),
        ["claude-opus-4-8"] = new(0.005m, 0.025m, 0.00625m, 0.0005m),
        ["claude-opus-4-7"] = new(0.005m, 0.025m, 0.00625m, 0.0005m),
        ["claude-opus-4-6"] = new(0.005m, 0.025m, 0.00625m, 0.0005m),
        ["claude-sonnet-5-5"] = new(0.002m, 0.010m, 0.0025m, 0.0002m),
        ["claude-sonnet-5"] = new(0.002m, 0.010m, 0.0025m, 0.0002m),
        ["claude-sonnet-4-6"] = new(0.003m, 0.015m, 0.00375m, 0.0003m),
        ["claude-haiku-5-5"] = new(0.0001m, 0.0005m, 0.000125m, 0.00001m),
        ["claude-haiku-4-5"] = new(0.001m, 0.005m, 0.00125m, 0.0001m),
        ["claude-fable-5-1"] = new(0.010m, 0.050m, 0.0125m, 0.00025m),
        ["claude-fable-5"] = new(0.010m, 0.050m, 0.0125m, 0.00025m),
    };

    public static bool TryGet(string? model, out Rate rate)
    {
        if (!string.IsNullOrWhiteSpace(model) && Rates.TryGetValue(model.Trim(), out var found))
        {
            rate = found;
            return true;
        }

        rate = null!;
        return false;
    }
}

/// <summary>One (provider, model, feature) aggregate of <see cref="AiUsageRecord"/> rows.</summary>
public sealed record AiUsageGroup(
    string ProviderId,
    string? Model,
    string FeatureCode,
    long Calls,
    long Successes,
    long Retries,
    long PromptTokens,
    long CompletionTokens,
    long CacheWriteTokens,
    long CacheReadTokens,
    decimal StoredCostUsd)
{
    public long Failures => Calls - Successes;

    /// <summary>
    /// What these calls cost at API list prices. A Claude model on the paid API, or on a subscription route
    /// (where it is the API-EQUIVALENT value, never a charge), is priced from <see cref="AiModelRateCard"/>
    /// over provider-reported tokens including the cache buckets. Any other provider keeps the stored
    /// per-call estimate, which is the best number the platform has for it.
    /// </summary>
    public decimal PricedUsd
    {
        get
        {
            if (IsClaudeRoute(ProviderId) && AiModelRateCard.TryGet(Model, out var r))
            {
                return (PromptTokens * r.InputPer1k
                    + CompletionTokens * r.OutputPer1k
                    + CacheWriteTokens * r.CacheWritePer1k
                    + CacheReadTokens * r.CacheReadPer1k) / 1000m;
            }

            return StoredCostUsd;
        }
    }

    /// <summary>True when <see cref="PricedUsd"/> came from the model rate card, not a stored estimate.</summary>
    public bool FromRateCard => IsClaudeRoute(ProviderId) && AiModelRateCard.TryGet(Model, out _);

    public bool IsSubscription => SubscriptionAccountGroups.GroupOf(ProviderId) is not null;

    /// <summary>Money actually incurred per call: zero on a subscription route, the priced amount otherwise.</summary>
    public decimal IncrementalApiUsd => IsSubscription ? 0m : PricedUsd;

    private static bool IsClaudeRoute(string providerId) =>
        string.Equals(providerId, AiPipelineDefaults.ClaudeApiProvider, StringComparison.OrdinalIgnoreCase)
        || SubscriptionAccountGroups.GroupOf(providerId) == SubscriptionAccountGroups.ClaudeMax;
}

/// <summary>
/// The one place usage rows are aggregated and priced for the AI Pipelines page, the promotional-credit
/// ledger and the credit guard, so the three can never disagree about what a call cost.
/// </summary>
public static class AiUsageLedger
{
    public static async Task<List<AiUsageGroup>> LoadGroupsAsync(
        LearnerDbContext db,
        DateTimeOffset? since,
        DateTimeOffset? until,
        string? providerCode,
        CancellationToken ct,
        Expression<Func<AiUsageRecord, bool>>? extraFilter = null)
    {
        var q = db.AiUsageRecords.AsNoTracking().Where(r => r.ProviderId != null);
        if (since is { } sinceAt) q = q.Where(r => r.CreatedAt >= sinceAt);
        if (until is { } untilAt) q = q.Where(r => r.CreatedAt < untilAt);
        if (!string.IsNullOrEmpty(providerCode)) q = q.Where(r => r.ProviderId == providerCode);
        if (extraFilter is not null) q = q.Where(extraFilter);

        var rows = await q
            .GroupBy(r => new { r.ProviderId, r.Model, r.FeatureCode })
            .Select(g => new
            {
                ProviderId = g.Key.ProviderId!,
                g.Key.Model,
                g.Key.FeatureCode,
                Calls = (long)g.Count(),
                Successes = (long)g.Count(x => x.Outcome == AiCallOutcome.Success),
                Retried = (long)g.Count(x => x.AttemptNumber > 1),
                RetryCount = g.Sum(x => (long)x.RetryCount),
                PromptTokens = g.Sum(x => (long)x.PromptTokens),
                CompletionTokens = g.Sum(x => (long)x.CompletionTokens),
                CacheWrite = g.Sum(x => (long)(x.CacheWriteTokens ?? 0)),
                CacheRead = g.Sum(x => (long)(x.CacheReadTokens ?? 0)),
                Cost = g.Sum(x => x.CalculatedCostUsd ?? x.CostEstimateUsd),
            })
            .ToListAsync(ct);

        return rows.Select(r => new AiUsageGroup(
            r.ProviderId, r.Model, r.FeatureCode, r.Calls, r.Successes, r.Retried + r.RetryCount,
            r.PromptTokens, r.CompletionTokens, r.CacheWrite, r.CacheRead, r.Cost)).ToList();
    }

    /// <summary>Total priced spend of one provider between two instants (null = open ended).</summary>
    public static async Task<decimal> SpendUsdAsync(
        LearnerDbContext db,
        string providerCode,
        DateTimeOffset? since,
        DateTimeOffset? until,
        CancellationToken ct)
    {
        var groups = await LoadGroupsAsync(db, since, until, providerCode, ct);
        return groups.Sum(g => g.PricedUsd);
    }
}
