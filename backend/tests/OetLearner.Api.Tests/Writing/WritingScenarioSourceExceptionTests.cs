using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Owner decisions of 22 Sep 2026 for two scenarios whose source cannot support what the general
/// rules demand (WritingScenarioSourceExceptions). Each exception is pinned to its own scenario;
/// the controls prove every other scenario keeps the general rule.
/// </summary>
public sealed class WritingScenarioSourceExceptionTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());

    private static readonly Guid Zhang = Guid.Parse("eddf4519-8799-4f2e-b192-428bdcc06d6f");
    private static readonly Guid Sophia = Guid.Parse("441cbddb-d720-4dd6-b528-ff2755c36a3a");
    private static readonly Guid Other = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string ZhangNotes =
        "Described as a 30-year-old male patient on the mental health ward.\nCase notes record name as Ming Zhang, age 24.\nAdmitted 5 April 2011.";

    private const string SophiaNotes =
        "Sophia Joe Patrick is 15 years old.\nHer last menstrual period was 4/2/13, with no cycles for the past 2 months.";

    private static List<LintFinding> Lint(Guid scenarioId, string letter, string notes)
        => Engine.Lint(new WritingLintInput(
            LetterText: letter,
            LetterType: "referral",
            PatientAge: WritingScenarioSourceExceptions.PatientAge(scenarioId, notes),
            CaseNotesText: notes,
            Profession: ExamProfession.Nursing,
            IsModelAnswer: true,
            DateAnchor: WritingScenarioSourceExceptions.DateAnchor(scenarioId, null, notes, null),
            PatientAgeContradicted: WritingScenarioSourceExceptions.PatientAgeContradicted(scenarioId))).ToList();

    private static bool Fires(List<LintFinding> findings, string checkId)
        => findings.Any(f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal));

    private static string Letter(string dateLine, string reLine) => $"""
        The Team Leader
        Ryde Community Mental Health Team
        {dateLine}
        Dear Team Leader,
        Re: {reLine}

        I am writing to refer this patient for ongoing community care and support.

        Should you have any questions, please do not hesitate to contact me.

        Yours sincerely,

        Charge Nurse
        """;

    [Fact]
    public void ContradictoryAge_NoAgeIsDerivedOrDemanded_ForZhangOnly()
    {
        Assert.Null(WritingScenarioSourceExceptions.PatientAge(Zhang, ZhangNotes));
        Assert.NotNull(WritingScenarioSourceExceptions.PatientAge(Other, ZhangNotes));

        var letter = Letter("\n26 April 2011\n", "Mr Ming Zhang");
        Assert.False(Fires(Lint(Zhang, letter, ZhangNotes), "re_line_age_when_no_dob"));
        Assert.True(Fires(Lint(Other, letter, ZhangNotes), "re_line_age_when_no_dob"));
    }

    [Fact]
    public void NoLetterDate_ClinicalHistoryDateIsNotALetterDate_ForSophiaOnly()
    {
        Assert.Equal(LetterDateAnchor.None, WritingScenarioSourceExceptions.DateAnchor(Sophia, null, SophiaNotes, null));
        Assert.Equal(LetterDateAnchor.Day, WritingScenarioSourceExceptions.DateAnchor(Other, null, SophiaNotes, null));

        var undated = Letter("", "Sophia Joe Patrick, aged 15");
        var sophia = Lint(Sophia, undated, SophiaNotes);
        Assert.False(Fires(sophia, "model_answer_layout"));
        Assert.False(Fires(sophia, "letter_structure_order"));
        Assert.True(Fires(Lint(Other, undated, SophiaNotes), "model_answer_layout"));

        // With no source date, a stated date is unverifiable and must be omitted.
        Assert.True(Fires(Lint(Sophia, Letter("\n4 February 2013\n", "Sophia Joe Patrick, aged 15"), SophiaNotes), "letter_date_unsupported"));
    }
}
