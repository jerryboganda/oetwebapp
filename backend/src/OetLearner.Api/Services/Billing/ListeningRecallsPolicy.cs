namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Stable identity + rules for the standalone Listening Recalls product.
///
/// The standalone product is identified ONLY by its immutable catalog code
/// (<see cref="PlanCode"/>). Price, display name and marketing copy are
/// deliberately NOT consulted so a future £17 price change cannot break
/// automatic fulfilment.
///
/// Scope restriction: this applies ONLY when the purchased plan code IS
/// <see cref="PlanCode"/>. A bundle that merely CONTAINS recalls content
/// (e.g. Full Condensed Medicine, which also grants the Recalls module)
/// must NOT trigger this path — its own plan code governs it.
/// </summary>
public static class ListeningRecallsPolicy
{
    /// <summary>
    /// Canonical stable identifier for the standalone Listening Recalls
    /// product. Mirrors BillingPlan.Code / BillingPlanVersion.Code seeded by
    /// 20260824090000_SeedListeningRecallsPlan + oet-2026-catalog.json.
    /// </summary>
    public const string PlanCode = "listening-recalls";

    /// <summary>
    /// True only for the standalone Listening Recalls product purchase.
    /// Case-insensitive; null/whitespace never matches.
    /// </summary>
    public static bool IsStandaloneListeningRecalls(string? planCode)
        => !string.IsNullOrWhiteSpace(planCode)
            && string.Equals(planCode.Trim(), PlanCode, StringComparison.OrdinalIgnoreCase);
}
