using OetLearner.Api.Domain;
using OetLearner.Api.Services.Seeding;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Whether a provider row may be picked IMPLICITLY ("the first active credentialed row") by any
/// default-selection site. Rows that must be chosen explicitly are excluded:
/// <list type="bullet">
/// <item>keyless subscription sidecar rows (marker key) — the owner's Claude/Codex subscriptions;</item>
/// <item>rows whose <see cref="AiProvider.ParticipatesInAutoSelection"/> is <see langword="false"/> —
/// <em>including every row created after the column was introduced</em>, so a newly added vendor is
/// never auto-selected until an admin opts it in from <c>/admin/ai-providers</c>.</item>
/// </list>
/// Every default-selection site must use this predicate so a sixth site cannot quietly route
/// traffic to a provider nobody chose.
///
/// <para>
/// This replaced a hardcoded code list, which made "configurable from the admin panel" impossible:
/// a vendor's reachability was fixed in C# and only a deploy could change it. The migration that
/// introduced the column backfills <see langword="true"/> for every pre-existing row except
/// <c>opencode</c>, which is exactly the set the old rule admitted, so no live routing changes.
/// </para>
/// </summary>
public static class AiProviderDefaultEligibility
{
    /// <summary>The single rule every implicit-default site must call.</summary>
    public static bool IsDefaultEligible(AiProvider row)
        => !WritingSubscriptionProviderDefaults.IsMarkerKey(row.EncryptedApiKey)
           && row.ParticipatesInAutoSelection;

    /// <summary>
    /// Whether a bare code is known to be opted out, when the row itself is not in hand.
    ///
    /// <para>
    /// Always <see langword="false"/>. Eligibility is now per-row state, so answering "yes" from a
    /// code alone would mean guessing — and "unknown must not read as yes" is exactly the failure
    /// this class exists to prevent. Callers holding only a code must load the row and call
    /// <see cref="IsDefaultEligible"/>.
    /// </para>
    /// </summary>
    public static bool IsExplicitOnlyCode(string? code) => false;

    /// <summary>
    /// Human-readable reason a row is not eligible, for the "why is my route not being used"
    /// readout on <c>/admin/ai-providers</c>.
    /// </summary>
    public static string? IneligibilityReason(AiProvider row)
    {
        if (WritingSubscriptionProviderDefaults.IsMarkerKey(row.EncryptedApiKey))
            return "Keyless subscription sidecar row (marker key) — reachable only through an explicit pipeline hop.";
        if (!row.IsActive)
            return "Provider is switched off.";
        if (string.IsNullOrWhiteSpace(row.EncryptedApiKey))
            return "No API key on the row.";
        if (!row.ParticipatesInAutoSelection)
            return "Participates in automatic selection is off — reachable only where a route points at it.";
        return null;
    }
}