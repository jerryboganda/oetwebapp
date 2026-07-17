using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Writing;

/// <summary>
/// Inputs for the deterministic pathway generator. Built by
/// <see cref="WritingPathwayServiceV2"/> from the learner's profile + last
/// diagnostic grade. Kept as a record so unit tests can construct inputs
/// directly with no DB access.
/// </summary>
public sealed record WritingPathwayGenerationInput(
    string Profession,
    string TargetBand,
    DateTimeOffset? ExamDate,
    DateTimeOffset Now,
    int DaysPerWeek,
    int MinutesPerDay,
    IReadOnlyList<string> LetterTypeFocus,
    WritingCriterionScores DiagnosticScores,
    IReadOnlyDictionary<string, int> SubSkillBaseline);

/// <summary>
/// 6 OET Writing criteria. C1 ranges 0-3; C2-C6 range 0-7. Used both for the
/// diagnostic seed and the pathway recompute trigger.
/// </summary>
public sealed record WritingCriterionScores(int C1, int C2, int C3, int C4, int C5, int C6)
{
    public static WritingCriterionScores Empty => new(0, 0, 0, 0, 0, 0);

    public int RawTotal => C1 + C2 + C3 + C4 + C5 + C6;

    public IEnumerable<(string Code, int Score, int Max)> Enumerate()
    {
        yield return ("c1", C1, 3);
        yield return ("c2", C2, 7);
        yield return ("c3", C3, 7);
        yield return ("c4", C4, 7);
        yield return ("c5", C5, 7);
        yield return ("c6", C6, 7);
    }
}

public sealed record WritingPathwayPlannedItem(
    int OrderIndex,
    string Stage,
    string Phase,
    int WeekNumber,
    string? FocusSkill,
    string? FocusCriterion,
    string ItemKind,
    string? ContentRefId,
    int EstimatedMinutes,
    string Title,
    string Description);

public interface IWritingPathwayGenerator
{
    IReadOnlyList<WritingPathwayPlannedItem> Generate(WritingPathwayGenerationInput input);
    IReadOnlyDictionary<string, double> ComputeWeaknessVector(WritingCriterionScores scores);
}

/// <summary>
/// Pure-function pathway generator. NO DB. NO DI.
///
/// Algorithm (spec §6.5):
/// 1. Derive weakness vector from the diagnostic (lower-than-target ⇒ higher weight).
/// 2. Allocate total minutes across phases (foundation 20%, practice 50%, mastery 30%).
/// 3. For each phase, emit items in this priority order:
///    - Sub-skill lessons (W1–W8) for the two weakest criteria
///    - Sentence + case-note drills (80% weakness focus, 20% balance)
///    - Full letters (one per letter-type focus, rotating)
///    - Mocks (mastery phase only)
///    - Canon refreshers (one every 3 items)
///    - Exemplar reviews (foundation phase only)
/// </summary>
public sealed class WritingPathwayGenerator : IWritingPathwayGenerator
{
    private static readonly IReadOnlyDictionary<string, string> CriterionToSkill = new Dictionary<string, string>
    {
        ["c1"] = "W2",
        ["c2"] = "W3",
        ["c3"] = "W4",
        ["c4"] = "W6",
        ["c5"] = "W5",
        ["c6"] = "W7",
    };

    private static readonly IReadOnlyDictionary<string, string> CriterionLabel = new Dictionary<string, string>
    {
        ["c1"] = "purpose",
        ["c2"] = "content",
        ["c3"] = "conciseness_clarity",
        ["c4"] = "genre_style",
        ["c5"] = "organisation_layout",
        ["c6"] = "language",
    };

    public IReadOnlyDictionary<string, double> ComputeWeaknessVector(WritingCriterionScores scores)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, score, max) in scores.Enumerate())
        {
            var normalized = max <= 0 ? 0 : score / (double)max;
            result[code] = Math.Round(Math.Clamp(1.0 - normalized, 0.0, 1.0), 4);
        }
        return result;
    }

    public IReadOnlyList<WritingPathwayPlannedItem> Generate(WritingPathwayGenerationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var totalWeeks = CalculateTotalWeeks(input.ExamDate, input.Now);
        var weaknessVector = ComputeWeaknessVector(input.DiagnosticScores);
        var orderedWeak = weaknessVector
            .OrderByDescending(kvp => kvp.Value)
            .Select(kvp => kvp.Key)
            .ToList();
        var focus = NormalizeFocus(input.LetterTypeFocus, input.Profession);

        var items = new List<WritingPathwayPlannedItem>();
        var order = 1;

        var foundationWeeks = Math.Max(1, (int)Math.Ceiling(totalWeeks * 0.2));
        var practiceWeeks = Math.Max(1, (int)Math.Ceiling(totalWeeks * 0.5));
        var masteryWeeks = Math.Max(1, totalWeeks - foundationWeeks - practiceWeeks);

        order = AppendStage(items, order, "foundation", foundationWeeks, focus, orderedWeak, includeExemplars: true, includeMocks: false);
        order = AppendStage(items, order, "practice", practiceWeeks, focus, orderedWeak, includeExemplars: false, includeMocks: false);
        AppendStage(items, order, "mastery", masteryWeeks, focus, orderedWeak, includeExemplars: false, includeMocks: true);

        return items;
    }

    private static int AppendStage(
        List<WritingPathwayPlannedItem> items,
        int order,
        string stage,
        int weeks,
        IReadOnlyList<string> focus,
        IReadOnlyList<string> orderedWeak,
        bool includeExemplars,
        bool includeMocks)
    {
        var letterCursor = 0;
        for (var week = 1; week <= weeks; week++)
        {
            var phase = ResolvePhase(stage, week, weeks);
            var weakIndex = (week - 1) % Math.Max(1, orderedWeak.Count);
            var focusCriterion = orderedWeak.Count == 0 ? "c5" : orderedWeak[weakIndex];
            var focusSkill = CriterionToSkill.GetValueOrDefault(focusCriterion, "W6");

            items.Add(new WritingPathwayPlannedItem(order++, stage, phase, week,
                focusSkill, CriterionLabel.GetValueOrDefault(focusCriterion, focusCriterion),
                ItemKind: "lesson", ContentRefId: null,
                EstimatedMinutes: 10,
                Title: $"Lesson — focus {focusSkill}",
                Description: $"Targeted lesson for weakest criterion {focusCriterion}."));

            items.Add(new WritingPathwayPlannedItem(order++, stage, phase, week,
                focusSkill, CriterionLabel.GetValueOrDefault(focusCriterion, focusCriterion),
                ItemKind: "drill", ContentRefId: null,
                EstimatedMinutes: 8,
                Title: "Sentence drill",
                Description: "Sentence-level rehearsal of the focus pattern."));

            if (week % 2 == 1)
            {
                items.Add(new WritingPathwayPlannedItem(order++, stage, phase, week,
                    focusSkill, CriterionLabel.GetValueOrDefault(focusCriterion, focusCriterion),
                    ItemKind: "drill", ContentRefId: null,
                    EstimatedMinutes: 10,
                    Title: "Case-note relevance drill",
                    Description: "Practice deciding what to include from the case notes."));
            }

            var letterType = focus.Count == 0 ? "LT-RR" : focus[letterCursor % focus.Count];
            letterCursor++;
            items.Add(new WritingPathwayPlannedItem(order++, stage, phase, week,
                focusSkill, CriterionLabel.GetValueOrDefault(focusCriterion, focusCriterion),
                ItemKind: "letter", ContentRefId: letterType,
                EstimatedMinutes: 45,
                Title: $"Full letter — {letterType}",
                Description: "Write a complete letter and submit for grading."));

            if (includeMocks && week == weeks)
            {
                items.Add(new WritingPathwayPlannedItem(order++, stage, phase, week,
                    null, null, ItemKind: "mock", ContentRefId: null,
                    EstimatedMinutes: 50,
                    Title: "Mock exam",
                    Description: "Strict 5+40 minute mock under exam conditions."));
            }

            if (includeExemplars && week == 1)
            {
                items.Add(new WritingPathwayPlannedItem(order++, stage, phase, week,
                    focusSkill, CriterionLabel.GetValueOrDefault(focusCriterion, focusCriterion),
                    ItemKind: "exemplar-review", ContentRefId: letterType,
                    EstimatedMinutes: 12,
                    Title: "Exemplar review",
                    Description: "Study a gold-standard letter for this letter type."));
            }

            if (week % 3 == 0)
            {
                items.Add(new WritingPathwayPlannedItem(order++, stage, phase, week,
                    "W6", "genre_style", ItemKind: "canon-refresher", ContentRefId: null,
                    EstimatedMinutes: 5,
                    Title: "Canon refresher",
                    Description: "Skim Dr Ahmed's style canon for one rule cluster."));
            }
        }
        return order;
    }

    private static string ResolvePhase(string stage, int week, int totalWeeks)
        => stage switch
        {
            "foundation" => "ramp-up",
            "mastery" => week == totalWeeks ? "exam-sim" : "consolidate",
            _ => week <= Math.Max(1, totalWeeks / 2) ? "build" : "stretch",
        };

    private static int CalculateTotalWeeks(DateTimeOffset? examDate, DateTimeOffset now)
    {
        if (examDate is null) return 10;
        var weeks = (int)Math.Ceiling((examDate.Value - now).TotalDays / 7.0);
        return Math.Clamp(weeks, 4, 12);
    }

    private static IReadOnlyList<string> NormalizeFocus(IReadOnlyList<string> focus, string profession)
    {
        if (focus is { Count: > 0 }) return focus;
        return profession switch
        {
            "pharmacy" => new[] { "LT-RR", "LT-RP", "LT-NM" },
            "nursing" => new[] { "LT-DG", "LT-TR", "LT-NM" },
            _ => new[] { "LT-RR", "LT-DG", "LT-UR" },
        };
    }
}
