namespace OetLearner.Api.Contracts;

public sealed record SpeakingSimulationV11RubricCriterion(
    string CriterionCode,
    string Label,
    int Weight,
    string[] EnabledRuleIds);

public sealed record SpeakingSimulationV11RubricCriteria(
    IReadOnlyList<SpeakingSimulationV11RubricCriterion> Criteria);

public sealed record SpeakingSimulationV11GateResult(
    bool IsReleased,
    IReadOnlyList<string> BlockingReasons,
    string[] EnabledRuleIds,
    string SpecVersion,
    string RubricVersion,
    IReadOnlyList<SpeakingSimulationV11RubricCriterion>? RubricCriteria = null,
    int SilencePromptThresholdMs = 12_000,
    string? AudioAssessmentProvider = null,
    string CalibrationVersion = "speaking-simulation-v1.1-calibration");

public static class SpeakingSimulationV11Contracts
{
    public const string SpecVersion = "speaking-simulation-v1.1";
    public const string RubricVersion = "speaking-simulation-v1.1-rubric";
    public const string CalibrationVersion = "speaking-simulation-v1.1-calibration";
    public const int DefaultSilencePromptThresholdMs = 12_000;
    public const string GraphDisclaimer = "AI Estimated Practice Score — not an official OET result";

    public static readonly SpeakingSimulationV11RubricCriteria RubricCriteria = new(
    [
        new("intelligibility_pronunciation", "Intelligibility & pronunciation", 10, ["R49", "R50"]),
        new("fluency_continuity", "Fluency & continuity", 12, ["R49", "R51"]),
        new("grammar_vocabulary", "Grammar & vocabulary", 8, ["R06", "R07"]),
        new("appropriateness_plain_language", "Appropriateness / plain language", 10, ["R06", "R08", "R12"]),
        new("relationship_building_empathy", "Relationship building & empathy", 14, ["R13", "R15", "R16"]),
        new("patient_perspective", "Patient perspective", 10, ["R17", "R18"]),
        new("information_gathering", "Information gathering", 10, ["R09", "R10", "R14"]),
        new("information_giving_checking", "Information giving & checking", 12, ["R11", "R18", "R40"]),
        new("structure_task_management", "Structure & task management", 9, ["R20", "R21", "R22"]),
        new("closure_time_management", "Closure & time management", 5, ["R20", "R22", "R49"]),
    ]);

    public static bool IsValidRubric(IReadOnlyList<SpeakingSimulationV11RubricCriterion>? criteria)
    {
        if (criteria is null || criteria.Count != RubricCriteria.Criteria.Count
            || criteria.Any(item => item is null
                || string.IsNullOrWhiteSpace(item.CriterionCode)
                || string.IsNullOrWhiteSpace(item.Label)))
        {
            return false;
        }

        if (!criteria.Select(item => item.CriterionCode)
            .SequenceEqual(RubricCriteria.Criteria.Select(item => item.CriterionCode), StringComparer.Ordinal))
        {
            return false;
        }

        var expected = RubricCriteria.Criteria.ToDictionary(x => x.CriterionCode, StringComparer.Ordinal);
        if (criteria.GroupBy(x => x.CriterionCode, StringComparer.Ordinal).Any(group => group.Count() != 1))
        {
            return false;
        }

        var actual = criteria.ToDictionary(x => x.CriterionCode, StringComparer.Ordinal);
        return actual.Count == expected.Count
            && actual.Values.Sum(item => item.Weight) == 100
            && expected.All(pair => actual.TryGetValue(pair.Key, out var item)
                && item.Weight == pair.Value.Weight
                && string.Equals(item.Label?.Trim(), pair.Value.Label, StringComparison.Ordinal)
                && item.EnabledRuleIds is not null
                && item.EnabledRuleIds.Length == pair.Value.EnabledRuleIds.Length
                && item.EnabledRuleIds.SequenceEqual(pair.Value.EnabledRuleIds, StringComparer.OrdinalIgnoreCase)
                && item.EnabledRuleIds.All(ruleId =>
                    !string.Equals(ruleId, "rule55", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(ruleId, "R55", StringComparison.OrdinalIgnoreCase)));
    }
}
