using OetLearner.Api.Domain;
using OetLearner.Api.Services.Seeding;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Whether a provider row may be picked IMPLICITLY ("the first active credentialed row") by any
/// default-selection site. Rows that must be chosen explicitly are excluded:
/// <list type="bullet">
/// <item>keyless subscription sidecar rows (marker key) — the owner's Claude/Codex subscriptions;</item>
/// <item>codes listed in <see cref="OpenCodeProviderDefaults.ExplicitOnlyCodes"/> — real-key rows such
/// as OpenCode that learner traffic may only reach through an explicit pick.</item>
/// </list>
/// Every default-selection site must use this predicate so a sixth site cannot quietly route
/// traffic to a provider nobody chose.
/// </summary>
public static class AiProviderDefaultEligibility
{
    public static bool IsDefaultEligible(AiProvider row)
        => !WritingSubscriptionProviderDefaults.IsMarkerKey(row.EncryptedApiKey)
           && !IsExplicitOnlyCode(row.Code);

    public static bool IsExplicitOnlyCode(string? code)
        => !string.IsNullOrWhiteSpace(code)
           && OpenCodeProviderDefaults.ExplicitOnlyCodes.Contains(code.Trim().ToLowerInvariant());
}
