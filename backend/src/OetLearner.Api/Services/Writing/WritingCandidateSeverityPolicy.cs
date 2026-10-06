using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// One finding as the severity policy sees it. <paramref name="CheckId"/> is the RESOLVED check id
/// (see <see cref="WritingCandidateSeverityPolicy.Resolve"/>), <paramref name="FromGrader"/> marks an
/// AI-grader finding, and <paramref name="Trusted"/> marks a deterministic finding whose id this process
/// cannot resolve (a legacy-profession rule id): its stored severity was already calibrated when the
/// report was written, so it passes through unchanged.
/// </summary>
public readonly record struct WritingSeverityInput(string? CheckId, string? Severity, bool FromGrader, bool Trusted = false);

/// <summary>
/// The candidate severity doctrine (owner handoff, 6 Oct 2026, section 3). Severity words in the stored
/// vocabulary are critical | major | minor | info, where <c>info</c> IS the candidate-facing "Advisory":
/// coaching only, zero score effect, never a Top Priority.
/// <list type="bullet">
/// <item>Critical is reserved for source-fact / task proofs and for AI findings that are safety or task
/// failures. Surface grammar, style and layout checks are never Critical.</item>
/// <item>General-English checks are Minor (Major only when the same check repeats three or more times);
/// official-OET and owner-canonical checks are capped at Major.</item>
/// <item>Coaching-only and not-applicable checks are Advisory; accept-alternative checks are at most Minor.</item>
/// <item>An AI finding that cites a rule id absent from the check registry is a detected mistake, so it
/// stays score-bearing at the severity the grader gave it.</item>
/// </list>
/// Pure and deterministic: it runs when a report is written AND again when it is read, so reports stored
/// (or cloned by grade reuse) before the policy are repaired too. It is idempotent.
/// </summary>
public static class WritingCandidateSeverityPolicy
{
    public const string Critical = "critical";
    public const string Major = "major";
    public const string Minor = "minor";
    public const string Info = "info";

    // Owner doctrine: a paragraph that opens with He/She is a house-rule error (flag it, Minor, Major only
    // when the detector's ambiguity guard says clarity suffers) even though the registry row is
    // coaching-only. The detector already emits Minor or Major for candidates, so only the
    // score-bearing status is overridden here (the registry table itself is not edited).
    private static readonly HashSet<string> ScoredByDoctrine = new(StringComparer.OrdinalIgnoreCase)
    {
        "paragraph_start_patient_name",
    };

    /// <summary>True for a grader-stored rule source: <c>AI.&lt;criterion&gt;</c> or <c>AI:&lt;rule id&gt;</c>.</summary>
    public static bool IsGraderRuleId(string? ruleSource)
    {
        var id = (ruleSource ?? string.Empty).TrimStart();
        return id.StartsWith("AI.", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("AI:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The resolved check id of a stored rule source (BUILTIN.*, OWN-W-*, AI:*, a bare check id) and whether
    /// the grader produced it. <c>AI.&lt;criterion&gt;</c> resolves to itself, which is not in the registry.
    /// </summary>
    public static (string? CheckId, bool FromGrader) Resolve(string? ruleSource)
        => (WritingAssessmentV11RuleEngine.ResolveCheckId(ruleSource), IsGraderRuleId(ruleSource));

    /// <summary>
    /// Policy input for a stored rule source. A deterministic id that is neither registered nor BUILTIN
    /// (a legacy-profession rule id) is trusted as stored: it was resolved through the rulebook and
    /// calibrated when the report was written, which the static registry cannot repeat at read time.
    /// </summary>
    public static WritingSeverityInput InputFor(string? ruleSource, string? severity)
    {
        var (checkId, fromGrader) = Resolve(ruleSource);
        var trusted = !fromGrader
            && !string.IsNullOrWhiteSpace(ruleSource)
            && !ruleSource!.TrimStart().StartsWith("BUILTIN.", StringComparison.OrdinalIgnoreCase)
            && !WritingRuleProvenance.TryGet(checkId, out _);
        return new WritingSeverityInput(checkId, severity, fromGrader, trusted);
    }

    /// <summary>
    /// Whether a finding may lower a criterion score or take a Top Priority slot. Unregistered grader ids
    /// are detected mistakes (true); unregistered deterministic ids keep the registry's conservative
    /// coaching default (false).
    /// </summary>
    public static bool IsScoreBearing(string? resolvedCheckId, bool fromGrader)
    {
        if (!string.IsNullOrWhiteSpace(resolvedCheckId) && ScoredByDoctrine.Contains(resolvedCheckId.Trim())) return true;
        if (WritingRuleProvenance.TryGet(resolvedCheckId, out var provenance))
            return provenance.CandidateBehavior == WritingCandidateBehaviors.ScoreBearing;
        return fromGrader;
    }

    public static bool IsScoreBearing(WritingSeverityInput input)
        => input.Trusted || IsScoreBearing(input.CheckId, input.FromGrader);

    /// <summary>
    /// The calibrated stored-vocabulary severity for one finding. Repeats are not visible here: use
    /// <see cref="CalibrateAll"/> when several findings share a check so three-or-more repeats of a
    /// general-English check can be raised to Major.
    /// </summary>
    public static string Calibrate(string? resolvedCheckId, string? severity, bool fromGrader)
        => CalibrateOne(new WritingSeverityInput(resolvedCheckId, severity, fromGrader), repeated: false);

    /// <summary>Calibrates a whole report's findings together; the result lines up with <paramref name="items"/>.</summary>
    public static IReadOnlyList<string> CalibrateAll(IReadOnlyList<WritingSeverityInput> items)
    {
        var repeats = items
            .Where(IsGeneralEnglishScored)
            .GroupBy(x => x.CheckId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= 3)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new string[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var repeated = IsGeneralEnglishScored(items[i]) && repeats.Contains(items[i].CheckId!.Trim());
            result[i] = CalibrateOne(items[i], repeated);
        }
        return result;
    }

    private static bool IsGeneralEnglishScored(WritingSeverityInput input)
        => !input.Trusted
            && !string.IsNullOrWhiteSpace(input.CheckId)
            && WritingRuleProvenance.TryGet(input.CheckId, out var provenance)
            && provenance.CandidateBehavior == WritingCandidateBehaviors.ScoreBearing
            && provenance.Tag == WritingProvenanceTags.GeneralEnglishValidated;

    private static string CalibrateOne(WritingSeverityInput input, bool repeated)
    {
        var severity = Normalise(input.Severity);
        if (severity == Info) return Info;
        if (input.Trusted) return severity;

        if (!WritingRuleProvenance.TryGet(input.CheckId, out var provenance))
        {
            // Unregistered: a grader-cited id is a detected mistake and keeps the grader's severity;
            // an unregistered deterministic id keeps the registry's conservative coaching default.
            return input.FromGrader ? severity : Info;
        }

        if (ScoredByDoctrine.Contains((input.CheckId ?? string.Empty).Trim()))
            return severity == Critical ? Major : severity;

        switch (provenance.CandidateBehavior)
        {
            case WritingCandidateBehaviors.CoachingOnly:
            case WritingCandidateBehaviors.NotApplicable:
                return Info;
            case WritingCandidateBehaviors.AcceptAlternative:
                return Minor;
        }

        return provenance.Tag switch
        {
            // Source-fact and task proofs may stay Critical.
            WritingProvenanceTags.SourceFactTask => severity,
            // General English: Minor, Major only for a repeated pattern that really hurts the reader.
            WritingProvenanceTags.GeneralEnglishValidated => repeated && severity != Minor ? Major : Minor,
            // Official-OET, owner-canonical, profession-resource and preferred-style checks: never above Major.
            _ => severity == Critical ? Major : severity,
        };
    }

    // The grader and the engine may send critical | major | minor | info | advisory; anything else
    // (moderate, blank, a typo) is read as Major, the way the grading pipeline has always coerced it.
    private static string Normalise(string? severity) => severity?.Trim().ToLowerInvariant() switch
    {
        "critical" => Critical,
        "minor" => Minor,
        "info" or "advisory" => Info,
        _ => Major,
    };
}
