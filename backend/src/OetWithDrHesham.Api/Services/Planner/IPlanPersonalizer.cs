using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Planner;

/// <summary>
/// Premium-only seam that tweaks the generated plan after the rules engine has
/// produced it. v1 stub re-orders today's items by weight + biases weak-skill
/// items earlier in the day. v2 (future) will swap in an LLM that rewrites
/// rationale strings and re-balances slot minutes per learner narrative.
///
/// The seam is invoked only when:
///   • <see cref="StudyPlan.EntitlementTierAtGeneration"/> != "free"
///   • <see cref="StudyPlan.IsPremiumPersonalized"/> is requested true
///   • the global feature flag <c>study-plan-ai-personalize</c> is on
/// </summary>
public interface IPlanPersonalizer
{
    Task<int> ApplyAsync(
        StudyPlan plan,
        IReadOnlyList<StudyPlanItem> newItems,
        IReadOnlyDictionary<string, double> weights,
        IReadOnlyCollection<string> weakSubtests,
        CancellationToken cancellationToken);
}

/// <summary>
/// Rule-based personaliser used until the LLM-backed implementation lands.
/// Deterministic: stable order ensures the SkippedBecauseUnchanged check still works.
/// </summary>
public sealed class RuleBasedPlanPersonalizer : IPlanPersonalizer
{
    public Task<int> ApplyAsync(
        StudyPlan plan,
        IReadOnlyList<StudyPlanItem> newItems,
        IReadOnlyDictionary<string, double> weights,
        IReadOnlyCollection<string> weakSubtests,
        CancellationToken cancellationToken)
    {
        var weakSet = new HashSet<string>(weakSubtests, StringComparer.OrdinalIgnoreCase);

        // Re-sort today's items so weak-skill tasks bubble to the top.
        // Re-rank by:
        //   1. Section == today bubbles first
        //   2. Within today: weak-skill tasks before non-weak
        //   3. Within group: subtest weight desc
        //   4. PriorityScore asc as final tiebreak
        var ordered = newItems
            .OrderBy(i => i.Section == StudyPlanSections.Today ? 0 : 1)
            .ThenBy(i => weakSet.Contains(i.SubtestCode) ? 0 : 1)
            .ThenByDescending(i => weights.TryGetValue(i.SubtestCode, out var w) ? w : 0.0)
            .ThenBy(i => i.PriorityScore)
            .ToList();

        var changedCount = 0;
        for (var idx = 0; idx < ordered.Count; idx++)
        {
            var newScore = idx * 10;
            if (ordered[idx].PriorityScore != newScore)
            {
                ordered[idx].PriorityScore = newScore;
                changedCount++;
            }
        }

        plan.IsPremiumPersonalized = true;
        return Task.FromResult(changedCount);
    }
}
