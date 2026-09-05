using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services.Companion;

/// <summary>
/// Independent kill switches for the AI Learning Companion, backed by the
/// existing <c>FeatureFlags</c> table so an operator can flip them from
/// <c>/admin/flags</c> <b>without a deploy</b> — which is exactly what the
/// source specification requires of an emergency control
/// (docs/ai-learning-companion/ARCHITECTURE_AND_INTEGRATION.md §14).
///
/// They are deliberately separate switches rather than one master flag: an
/// incident should be able to stop retrieval, stop actions, or freeze credit
/// consumption <i>without</i> taking the whole surface down, and knowledge
/// rollback should not require an application rollback.
///
/// <para>
/// Every flag here <b>fails closed</b>. If the row is missing, the database is
/// unreachable, or the value is ambiguous, the answer is <c>false</c>. This is
/// the opposite of <see cref="StrategyGuideService"/>, which fails open — that
/// is correct for a content catalogue and wrong for a surface that reaches a
/// learner's performance history, entitlements and credit balance.
/// </para>
/// </summary>
public interface ICompanionFeatureFlags
{
    /// <summary>Master switch for the learner-facing companion surface.</summary>
    Task<bool> IsEnabledAsync(CancellationToken ct);

    /// <summary>Knowledge retrieval. Off = answer without grounded sources rather than fail.</summary>
    Task<bool> IsRetrievalEnabledAsync(CancellationToken ct);

    /// <summary>Typed platform actions (open resource, add to plan, checkout).</summary>
    Task<bool> AreActionsEnabledAsync(CancellationToken ct);

    /// <summary>New AI Credit consumption. Off preserves balances and blocks only new charges.</summary>
    Task<bool> IsCreditConsumptionEnabledAsync(CancellationToken ct);

    /// <summary>
    /// GATED (TV-006 / TV-007). Whether the companion may state a numeric
    /// Writing/Speaking band estimate. Criterion-level feedback never depends on
    /// this flag; only a numeric claim does. Must stay off until approved
    /// calibration exists, and the UI must never call such a number official.
    /// </summary>
    Task<bool> IsScoreDisplayEnabledAsync(CancellationToken ct);

    /// <summary>
    /// Reads any other platform flag by key, with the same fail-closed contract.
    /// Used by <see cref="CompanionDestinationRegistry"/> so the companion never
    /// links to a surface the platform has switched off (e.g. <c>video_library</c>).
    /// </summary>
    Task<bool> IsPlatformFlagOnAsync(string key, CancellationToken ct);
}

public sealed class CompanionFeatureFlags(LearnerDbContext db, ILogger<CompanionFeatureFlags> logger)
    : ICompanionFeatureFlags
{
    internal const string CompanionKey = "ai_learning_companion";
    internal const string RetrievalKey = "companion_retrieval";
    internal const string ActionsKey = "companion_actions";
    internal const string CreditsKey = "companion_credits";
    internal const string ScoreDisplayKey = "companion_score_display";

    public Task<bool> IsEnabledAsync(CancellationToken ct) => IsFlagOnAsync(CompanionKey, ct);

    public Task<bool> IsRetrievalEnabledAsync(CancellationToken ct) => IsFlagOnAsync(RetrievalKey, ct);

    public Task<bool> AreActionsEnabledAsync(CancellationToken ct) => IsFlagOnAsync(ActionsKey, ct);

    public Task<bool> IsCreditConsumptionEnabledAsync(CancellationToken ct) => IsFlagOnAsync(CreditsKey, ct);

    public Task<bool> IsScoreDisplayEnabledAsync(CancellationToken ct) => IsFlagOnAsync(ScoreDisplayKey, ct);

    public Task<bool> IsPlatformFlagOnAsync(string key, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(key) ? Task.FromResult(false) : IsFlagOnAsync(key, ct);

    private async Task<bool> IsFlagOnAsync(string key, CancellationToken ct)
    {
        try
        {
            // Newest row wins if a key was ever duplicated, mirroring StrategyGuideService.
            var flag = await db.FeatureFlags
                .AsNoTracking()
                .Where(f => f.Key == key)
                .OrderByDescending(f => f.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            return flag?.Enabled ?? false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail closed. A companion that cannot read its own kill switch must
            // behave as if the switch is off.
            logger.LogWarning(ex, "Companion feature flag '{Key}' could not be read; failing closed.", key);
            return false;
        }
    }
}
