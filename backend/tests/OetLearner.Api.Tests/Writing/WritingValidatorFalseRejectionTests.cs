using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Three false rejections found while publishing the repaired non-Medicine Model Answers (22 Sep 2026).
/// Each blocked a source-faithful letter and left no wording that could pass:
/// - re_line_identity_unsupported rejected an age the notes state in apposition ("Mr Martin Wilson, 62,
///   was admitted") or in words ("a ten-year-old boy"), while re_line_age_when_no_dob demands that very
///   "aged N".
/// - discharge_plan_present (R14.7) demanded a medication dose when the notes record none (an undosed
///   lubricant), so the only way to pass was to invent a dose.
/// Every fix keeps the genuine error firing: a different age, and a missing dose the notes do record.
/// </summary>
public sealed class WritingValidatorFalseRejectionTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());

    private static List<LintFinding> Lint(string letter, string letterType, string caseNotes,
        ExamProfession profession = ExamProfession.Nursing)
        => Engine.Lint(new WritingLintInput(
            LetterText: letter,
            LetterType: letterType,
            CaseNotesText: caseNotes,
            TaskText: null,
            Profession: profession,
            IsModelAnswer: true)).ToList();

    private static bool Fires(List<LintFinding> findings, string checkId)
        => findings.Any(f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal) || f.RuleId == checkId);

    private static string Letter(string reLine, string body) => $"""
        Ms Jane Brown
        Community Nurse
        City Community Health Centre
        12 High Street
        Newtown

        15 September 2008

        Dear Ms Brown,
        Re: {reLine}

        {body}

        Should there be any queries, kindly do not hesitate to contact me.

        Yours sincerely,

        Nurse
        """;

    private const string IdentityCheck = "re_line_identity_unsupported";

    [Theory]
    [InlineData("Mr Martin Wilson, 62, was admitted with a fractured neck of femur.", "Mr Martin Wilson, aged 62")]
    [InlineData("Jonathon Apple is a ten-year-old boy with asthma.", "Jonathon Apple, aged 10")]
    [InlineData("Patient is a sixty-two year old retired teacher.", "Mr Martin Wilson, aged 62")]
    public void ReLineAge_SupportedByTheNotes_IsNotRejected(string notes, string reLine)
    {
        var findings = Lint(Letter(reLine, "I am writing regarding the ongoing care of this patient."), "discharge", notes);
        Assert.False(Fires(findings, IdentityCheck));
    }

    [Theory]
    [InlineData("Mr Martin Wilson, 62, was admitted with a fractured neck of femur.", "Mr Martin Wilson, aged 63")]
    [InlineData("Jonathon Apple is a ten-year-old boy with asthma.", "Jonathon Apple, aged 11")]
    [InlineData("Ward 5, bed 12. Admitted with pneumonia.", "Mr Martin Wilson, aged 12")]
    public void ReLineAge_TheNotesDoNotRecord_StillFires(string notes, string reLine)
    {
        var findings = Lint(Letter(reLine, "I am writing regarding the ongoing care of this patient."), "discharge", notes);
        Assert.True(Fires(findings, IdentityCheck));
    }

    private const string DischargeCheck = "R14.7";   // check id discharge_plan_present reports under its rulebook id

    private const string UndosedBody =
        "I am writing to request ongoing routine eye care for Mrs Morris following bilateral cataract surgery. "
        + "She should continue preservative-free lubricants up to four times daily as needed and seek urgent review "
        + "for sudden vision loss.";

    [Fact]
    public void DischargePlan_NotesRecordNoDose_DoesNotDemandOne()
    {
        const string notes = "Advised preservative-free lubricants up to four times daily as required. Continue lubricants as needed.";
        var findings = Lint(Letter("Mrs Helen Morris, DOB: 27 April 1956", UndosedBody), "discharge", notes, ExamProfession.Optometry);
        Assert.False(Fires(findings, DischargeCheck));
    }

    [Fact]
    public void DischargePlan_NotesRecordADose_TheLetterMustCarryIt()
    {
        const string notes = "Discharged on chloramphenicol 0.5% eye drops, 1 drop four times daily, and paracetamol 500 mg as needed.";
        var findings = Lint(Letter("Mrs Helen Morris, DOB: 27 April 1956",
            "I am writing to request ongoing routine eye care for Mrs Morris following bilateral cataract surgery. "
            + "She should seek review if her symptoms return."),
            "discharge", notes, ExamProfession.Optometry);
        Assert.True(Fires(findings, DischargeCheck));
    }

    [Fact]
    public void DischargePlan_NoInstructions_StillFiresWithoutADose()
    {
        const string notes = "Advised preservative-free lubricants as required.";
        var findings = Lint(Letter("Mrs Helen Morris, DOB: 27 April 1956",
            "I am writing about Mrs Morris, whose bilateral cataract surgery was uncomplicated."),
            "discharge", notes, ExamProfession.Optometry);
        Assert.True(Fires(findings, DischargeCheck));
    }
}
