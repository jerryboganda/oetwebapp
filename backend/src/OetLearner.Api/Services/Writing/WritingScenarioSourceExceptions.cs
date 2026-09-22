using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Owner decisions (22 Sep 2026) for individual scenarios whose candidate-visible SOURCE cannot
/// support what the general rules demand. Each entry is deliberately task-specific: the general
/// extractors stay unchanged for every other scenario, and the case notes themselves are not edited.
/// <list type="bullet">
/// <item><b>Contradictory patient age</b> — Nursing, Mr Zhang Ming (WR-0023): the notes say both
/// "30-year-old male" and "age 24", with no DOB to settle it. No patient age is derived, so the
/// Re: line age is not required, no age is checked against the letter, and neither 24 nor 30 is
/// treated as unsupported. The Model Answer omits the age.</item>
/// <item><b>No letter date</b> — Nursing, Sophia Joe Patrick (WR-0189): the source has no
/// consultation date, only clinical history dates (the LMP). Those are never the letter date, so
/// the date anchor is None: the date line is optional and a Model Answer must not state one.</item>
/// </list>
/// </summary>
internal static class WritingScenarioSourceExceptions
{
    private static readonly HashSet<Guid> ContradictoryPatientAge = [Guid.Parse("eddf4519-8799-4f2e-b192-428bdcc06d6f")];

    private static readonly HashSet<Guid> NoLetterDate = [Guid.Parse("441cbddb-d720-4dd6-b528-ff2755c36a3a")];

    public static bool PatientAgeContradicted(Guid scenarioId) => ContradictoryPatientAge.Contains(scenarioId);

    public static int? PatientAge(Guid scenarioId, string caseNotes)
        => PatientAgeContradicted(scenarioId) ? null : WritingPatientAgeExtractor.Extract(caseNotes);

    public static LetterDateAnchor DateAnchor(Guid scenarioId, string? todayDate, string? caseNotes, string? taskText)
        => NoLetterDate.Contains(scenarioId)
            ? LetterDateAnchor.None
            : WritingRuleEngine.ClassifyDateAnchor(todayDate, caseNotes, taskText);
}
