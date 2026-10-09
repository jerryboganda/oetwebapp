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

    // 9 Oct 2026 final source audit (owner delegated the decision): the source gives no usable writing day, so the date
    // line is optional and a Model Answer states none - Casey (discharge "day 7", undated), O'Riley (discharge undated),
    // Hawthorne (today's visit undated; 06.04.19 is the previous review), Collins (age 78 contradicts the dated visit).
    private static readonly HashSet<Guid> NoLetterDate =
    [
        Guid.Parse("441cbddb-d720-4dd6-b528-ff2755c36a3a"),
        Guid.Parse("2dc96911-e1eb-4963-a156-1a6465be3be1"),
        Guid.Parse("066ecfa1-c02e-4b71-9d9c-6e5393bb6da8"),
        Guid.Parse("0fc02e37-24b0-47cb-a408-5a090aa587ac"),
        Guid.Parse("c110e41b-a05f-4c1a-8500-af7a9dc71b74"),
    ];

    // The writing day IS derivable from the source: Brew (task: "admitted 5 days ago", admitted 6 July 2017), Davies
    // (note: "2 July: ... patient ready for discharge", the transfer is written then), Norris (discharge 19 April 2015;
    // the 22 April re-dressing is a PLANNED date and never the letter date). Used by the Model Answer gate only: the
    // candidate-facing task is not changed.
    private static readonly Dictionary<Guid, string> TodayDateOverrides = new()
    {
        [Guid.Parse("75134963-0c27-4481-b9f5-2b1786421781")] = "11 July 2017",
        [Guid.Parse("f08ba66d-735a-4509-85f6-59954cf029f6")] = "2 July 2017",
        [Guid.Parse("a3d1b730-604c-4af7-82ef-cf1c40015bac")] = "19 April 2015",
    };

    public static string? TodayDate(Guid scenarioId, string? todayDate)
        => TodayDateOverrides.TryGetValue(scenarioId, out var overridden) ? overridden : todayDate;

    public static bool PatientAgeContradicted(Guid scenarioId) => ContradictoryPatientAge.Contains(scenarioId);

    public static int? PatientAge(Guid scenarioId, string caseNotes)
        => PatientAgeContradicted(scenarioId) ? null : WritingPatientAgeExtractor.Extract(caseNotes);

    public static LetterDateAnchor DateAnchor(Guid scenarioId, string? todayDate, string? caseNotes, string? taskText)
        => NoLetterDate.Contains(scenarioId)
            ? LetterDateAnchor.None
            : WritingRuleEngine.ClassifyDateAnchor(TodayDate(scenarioId, todayDate), caseNotes, taskText);
}
