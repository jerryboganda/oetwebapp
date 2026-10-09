using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.AiPipeline;

public enum AiCreditGuardMode
{
    /// <summary>No active grant or remaining credit above the reserve: the saved order stands.</summary>
    None = 0,
    /// <summary>Remaining credit at or below the reserve: the API hop runs after the Max hop.</summary>
    Demoted = 1,
    /// <summary>Remaining credit at or below the floor: the API hop is left out of the plan.</summary>
    Skipped = 2,
}

/// <summary>Runtime snapshot of the promotional-credit guard for one provider grant.</summary>
public sealed record AiCreditGuardState(
    bool Active,
    AiCreditGuardMode Mode,
    string ProviderCode,
    string? GrantId,
    decimal GrantUsd,
    decimal SpentUsd,
    decimal RemainingUsd,
    decimal ReserveUsd,
    decimal FloorUsd,
    DateTimeOffset? GrantStartsAt,
    DateTimeOffset CheckedAt);

/// <summary>
/// Promotional-credit protection (owner directive 2026-10-09): while an operator-entered credit grant
/// (Credits & Grants, the $200 Anthropic promotional balance) still has meaningful remaining credit,
/// grading runs the saved order with the paid API first, burning the promo credits before the Claude Max
/// subscription. As the balance runs low the guard demotes the API hop behind Max, and at the floor it
/// drops the hop from the plan entirely — without writing <see cref="AiPipelineStages"/> (the saved order
/// is never touched; this is a runtime protection layer the dashboard surfaces).
///
/// The balance is the internal ledger (grant minus usage-row spend), cached for
/// <see cref="AiPipelineCreditGuardOptions.CacheSeconds"/>. It is deliberately NOT the only protection:
/// an actually-exhausted Anthropic account fails with a typed insufficient-credit error, which every
/// grading chain already fails over, so a ledger miss can never block a candidate or cause an unexpected
/// charge (the promotional balance is prepaid and cannot draw a card). Spending alerts reuse the AI-budget
/// alert ladder (50/75/90/100 %) with their own scope, so billing alerts stay separate.
/// </summary>
public interface IAiCreditGuard
{
    Task<AiCreditGuardState> GetStateAsync(CancellationToken ct);
    Task<AiPipelinePlan> ApplyAsync(AiPipelinePlan plan, ILogger logger, CancellationToken ct);
}

public sealed class AiPipelineCreditGuardOptions
{
    public const string SectionName = "AiPipeline:CreditGuard";

    /// <summary>Provider whose grant is guarded.</summary>
    public string ProviderCode { get; set; } = "anthropic";

    /// <summary>Subscription hop the API hop is demoted behind (the Claude Max group's primary).</summary>
    public string PromoteProviderCode { get; set; } = "writing-claude-sub";

    /// <summary>Remaining USD at or below which the API hop is demoted behind Max. Default keeps the
    /// last ~$20 of promo credits unspent by grading (a ledger-estimate safety margin).</summary>
    public decimal ReserveUsd { get; set; } = 20m;

    /// <summary>Remaining USD at or below which the API hop is left out of the plan entirely.</summary>
    public decimal FloorUsd { get; set; } = 5m;

    public int CacheSeconds { get; set; } = 60;

    /// <summary>Alert ladder on the grant's consumption (percent). Same values as the AI-budget ladder.</summary>
    public int[] AlertLadderPct { get; set; } = [50, 75, 90, 100];
}

public sealed class AiCreditGuard(
    LearnerDbContext db,
    IOptions<AiPipelineCreditGuardOptions> options,
    TimeProvider clock,
    ILogger<AiCreditGuard> logger,
    OetLearner.Api.Services.NotificationService? notifications = null) : IAiCreditGuard
{
    public const string AlertScope = "ai_credit_grant";

    private static readonly object CacheGate = new();
    private static AiCreditGuardState? Cached;
    private static DateTimeOffset CachedAt;

    internal static AiCreditGuardMode ResolveMode(decimal remainingUsd, decimal reserveUsd, decimal floorUsd)
        => remainingUsd <= floorUsd ? AiCreditGuardMode.Skipped
           : remainingUsd <= reserveUsd ? AiCreditGuardMode.Demoted
           : AiCreditGuardMode.None;

    public async Task<AiCreditGuardState> GetStateAsync(CancellationToken ct)
    {
        var ttl = TimeSpan.FromSeconds(Math.Max(5, options.Value.CacheSeconds));
        lock (CacheGate)
        {
            if (Cached is { } hit && clock.GetUtcNow() - CachedAt < ttl) return hit;
        }

        var state = await ComputeStateAsync(ct);
        lock (CacheGate)
        {
            Cached = state;
            CachedAt = clock.GetUtcNow();
        }
        return state;
    }

    /// <summary>Drops the cached snapshot (used after an admin edits grants).</summary>
    internal static void InvalidateCache()
    {
        lock (CacheGate) Cached = null;
    }

    private async Task<AiCreditGuardState> ComputeStateAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var o = options.Value;
        var grant = await db.AiCreditGrants.AsNoTracking()
            .Where(g => g.ProviderCode == o.ProviderCode && g.StartsAt <= now)
            .OrderByDescending(g => g.StartsAt)
            .FirstOrDefaultAsync(ct);
        if (grant is null)
        {
            return new AiCreditGuardState(false, AiCreditGuardMode.None, o.ProviderCode,
                null, 0m, 0m, 0m, o.ReserveUsd, o.FloorUsd, null, now);
        }

        // Model-aware and cache-aware (AiUsageLedger), so the promotional balance falls at the rate Anthropic
        // really bills: claude-opus-5-5 at $4/$20 per million plus the prompt-cache buckets, not the provider
        // row's flat Sonnet-class price over non-cached tokens only.
        var spent = await AiUsageLedger.SpendUsdAsync(db, o.ProviderCode, grant.StartsAt, null, ct);
        var remaining = grant.GrantUsd - spent;
        var mode = ResolveMode(remaining, o.ReserveUsd, o.FloorUsd);

        await EvaluateAlertsAsync(grant, spent, ct);
        return new AiCreditGuardState(mode != AiCreditGuardMode.None, mode, o.ProviderCode,
            grant.Id, grant.GrantUsd, spent, remaining, o.ReserveUsd, o.FloorUsd, grant.StartsAt, now);
    }

    /// <summary>One indexed pass, then the same deduplicated ladder the budget alerts use:
    /// an AiBudgetAlerts row per (scope, grant, threshold) plus one admin notification per new row.</summary>
    private async Task EvaluateAlertsAsync(AiCreditGrant grant, decimal spent, CancellationToken ct)
    {
        try
        {
            if (grant.GrantUsd <= 0m) return;
            var pct = decimal.Round(spent / grant.GrantUsd * 100m, 2, MidpointRounding.AwayFromZero);
            foreach (var threshold in options.Value.AlertLadderPct)
            {
                if (pct < threshold) continue;
                var inserted = await InsertAlertIfNewAsync(grant.Id, threshold, ct);
                if (!inserted) continue;
                logger.LogWarning(
                    "Promotional credit {Pct}% threshold fired for grant {GrantId} (spent {Spent} of {Grant} USD).",
                    threshold, grant.Id, spent, grant.GrantUsd);
                if (notifications is not null)
                {
                    await notifications.CreateForAdminsAsync(
                        NotificationEventKey.AdminAiBudgetAlert,
                        AlertScope,
                        grant.Id,
                        $"{threshold}",
                        new Dictionary<string, object?>
                        {
                            ["scope"] = AlertScope,
                            ["periodKey"] = grant.Id,
                            ["thresholdPct"] = threshold,
                            ["occupancyUsd"] = spent,
                            ["limitUsd"] = grant.GrantUsd,
                        },
                        ct);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Credit-guard alert evaluation failed for grant {GrantId}; alerts are best-effort.", grant.Id);
        }
    }

    private async Task<bool> InsertAlertIfNewAsync(string grantId, int thresholdPct, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        var affected = await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "AiBudgetAlerts"
                ("Id", "Scope", "PeriodKey", "ThresholdPct", "FiredAt")
            VALUES
                ({0}, {1}, {2}, {3}, NOW())
            ON CONFLICT ("Scope", "PeriodKey", "ThresholdPct") DO NOTHING
            """,
            id, AlertScope, grantId, thresholdPct);
        if (affected > 0) return true;
        if (affected == 0) return false;
        return !await db.AiBudgetAlerts.AsNoTracking()
            .AnyAsync(a => a.Scope == AlertScope && a.PeriodKey == grantId && a.ThresholdPct == thresholdPct, ct);
    }

    public async Task<AiPipelinePlan> ApplyAsync(AiPipelinePlan plan, ILogger runLogger, CancellationToken ct)
    {
        if (!AiPipelineStageKeys.IsGrading(plan.StageKey)) return plan;
        var state = await GetStateAsync(ct);
        if (state.Mode == AiCreditGuardMode.None) return plan;

        var o = options.Value;
        if (state.Mode == AiCreditGuardMode.Skipped)
        {
            var kept = plan.Hops.Where(h => !string.Equals(h.Provider, o.ProviderCode, StringComparison.OrdinalIgnoreCase)).ToList();
            if (kept.Count == plan.Hops.Count) return plan;
            runLogger.LogWarning(
                "Credit guard: promotional balance at/below the floor (remaining {Remaining} USD) — {Provider} hops left out of {Stage} this run.",
                state.RemainingUsd, o.ProviderCode, plan.StageKey);
            return plan with
            {
                Hops = kept,
                Skipped = [.. plan.Skipped, $"{o.ProviderCode}: promotional credits exhausted (credit guard)"],
            };
        }

        // Demoted: move the API hop behind the last Max hop, keeping every other hop in saved order.
        var apiHops = plan.Hops.Where(h => string.Equals(h.Provider, o.ProviderCode, StringComparison.OrdinalIgnoreCase)).ToList();
        if (apiHops.Count == 0) return plan;
        var hasPromote = false;
        foreach (var hop in plan.Hops)
        {
            if (string.Equals(hop.Provider, o.PromoteProviderCode, StringComparison.OrdinalIgnoreCase)) hasPromote = true;
        }
        if (!hasPromote) return plan; // nothing to demote behind; the saved order stands

        var without = plan.Hops.Where(h => !apiHops.Contains(h)).ToList();
        var insertAt = 0;
        for (var i = 0; i < without.Count; i++)
        {
            if (string.Equals(without[i].Provider, o.PromoteProviderCode, StringComparison.OrdinalIgnoreCase)) insertAt = i + 1;
        }
        var result = without.GetRange(0, insertAt);
        result.AddRange(apiHops);
        result.AddRange(without.Skip(insertAt));
        runLogger.LogWarning(
            "Credit guard: promotional balance at/below the reserve (remaining {Remaining} USD) — {Provider} demoted behind {Promote} for {Stage} this run.",
            state.RemainingUsd, o.ProviderCode, o.PromoteProviderCode, plan.StageKey);
        return result.SequenceEqual(plan.Hops) ? plan : plan with { Hops = result };
    }
}
