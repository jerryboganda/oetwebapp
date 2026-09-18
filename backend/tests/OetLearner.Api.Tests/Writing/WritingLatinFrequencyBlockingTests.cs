using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Owner hard rule (18 Sep 2026): a medication-frequency abbreviation must be written in plain
/// English in a candidate-facing Model Answer, and an answer carrying one must not reach
/// READY/PUBLISHED. The detector previously emitted RuleSeverity.Minor with "Consider
/// translating ..." — advisory — so "PRN" could publish despite the check being registered Critical.
///
/// These tests pin the three properties the rule now needs: it BLOCKS a Model Answer, it covers the
/// q_h shorthand the map cannot enumerate, and it does NOT fire on optometry's OD/OS/OU, which
/// denote the right eye, left eye and both eyes rather than a dosing frequency.
/// </summary>
public class WritingLatinFrequencyBlockingTests
{
    private const string Rule = "BUILTIN.latin_abbreviations_translated";

    private static string Letter(string medicationSentence) => $"""
        Dr Alice Warren
        Community Medical Centre
        14 Bridge Road
        Newtown

        3 November 2019

        Dear Dr Warren,

        Re: Mr John Baker, DOB: 2 May 1948

        I am writing to refer Mr Baker, who requires ongoing review of his analgesia following a
        recent admission for a fractured wrist.

        {medicationSentence}

        Mr Baker has a history of hypertension and osteoarthritis, and he lives alone.

        I would be grateful if you could review his analgesia at his next appointment.

        Should there be any queries, kindly do not hesitate to contact me.

        Yours sincerely,

        Registered Nurse
        """;

    private static IReadOnlyList<LintFinding> Lint(string sentence, bool isModelAnswer,
        ExamProfession profession = ExamProfession.Nursing) =>
        new WritingRuleEngine(new RulebookLoader()).Lint(new WritingLintInput(
            LetterText: Letter(sentence),
            LetterType: "LT-RR",
            Profession: profession,
            IsModelAnswer: isModelAnswer));

    [Theory]
    [InlineData("He was prescribed paracetamol, 1 g PRN for pain.")]
    [InlineData("He was prescribed amoxicillin, 500 mg TDS for seven days.")]
    [InlineData("He was prescribed ramipril, 5 mg mane.")]
    [InlineData("He was prescribed temazepam, 10 mg nocte.")]
    [InlineData("He was prescribed metformin, 500 mg BD with meals.")]
    [InlineData("He was prescribed hydrocortisone cream QDS to the affected area.")]
    [InlineData("Adrenaline, 0.5 mg was given stat on arrival.")]
    public void ModelAnswer_WithUntranslatedFrequencyAbbreviation_IsBlocked(string sentence)
    {
        var findings = Lint(sentence, isModelAnswer: true);
        var hit = Assert.Single(findings, f => f.RuleId == Rule);
        Assert.Equal(RuleSeverity.Critical, hit.Severity);
    }

    [Theory]
    [InlineData("He was prescribed paracetamol, 1 g q6h for pain.", "every six hours")]
    [InlineData("He was prescribed paracetamol, 1 g q4h for pain.", "every four hours")]
    [InlineData("He was prescribed cefalexin, 500 mg q8h for five days.", "every eight hours")]
    [InlineData("He was prescribed enoxaparin, 40 mg q12h.", "every twelve hours")]
    public void ModelAnswer_WithEveryNHoursShorthand_IsBlockedAndSuggestsEnglish(string sentence, string expected)
    {
        var findings = Lint(sentence, isModelAnswer: true);
        var hit = Assert.Single(findings, f => f.RuleId == Rule);
        Assert.Equal(RuleSeverity.Critical, hit.Severity);
        Assert.Equal(expected, hit.FixSuggestion);
    }

    [Fact]
    public void ModelAnswer_WithPlainEnglishFrequency_IsClean()
    {
        var findings = Lint(
            "He was prescribed paracetamol, 1 g as needed, and ramipril, 5 mg in the morning.",
            isModelAnswer: true);
        Assert.DoesNotContain(findings, f => f.RuleId == Rule);
    }

    /// <summary>
    /// Accepted clinical abbreviations are a different category and must not be expanded — the rule
    /// covers dosing frequency only.
    /// </summary>
    [Fact]
    public void ModelAnswer_WithAcceptedClinicalAbbreviations_IsClean()
    {
        var findings = Lint(
            "An MRI confirmed the fracture, and IV antibiotics were commenced before the CT review.",
            isModelAnswer: true);
        Assert.DoesNotContain(findings, f => f.RuleId == Rule);
    }

    /// <summary>
    /// Optometry writes OD for the right eye (oculus dexter), which is also the prescription
    /// abbreviation for "once a day". Reading it as a frequency would fail a correct optometry
    /// letter — the exact "do not break correct writing to satisfy a regex" failure mode.
    /// OS and OU are eye terms only and are not in the frequency map in any profession.
    /// </summary>
    [Theory]
    [InlineData("Visual acuity was 6/9 OD and 6/12 OS on examination.")]
    [InlineData("Intraocular pressure was 15 mmHg OU at today's review.")]
    public void Optometry_EyeLateralityTokens_AreNotTreatedAsFrequency(string sentence)
    {
        var findings = Lint(sentence, isModelAnswer: true, profession: ExamProfession.Optometry);
        Assert.DoesNotContain(findings, f => f.RuleId == Rule);
    }

    /// <summary>
    /// OS / OU are never a dosing frequency, so they must not fire outside optometry either.
    /// </summary>
    [Fact]
    public void NonOptometry_OsAndOuTokens_AreNotFrequencyViolations()
    {
        var findings = Lint("The wound over the os calcis was reviewed today.", isModelAnswer: true);
        Assert.DoesNotContain(findings, f => f.RuleId == Rule);
    }

    /// <summary>
    /// Outside optometry the same token really is a dosing frequency, so the rule still applies.
    /// </summary>
    [Fact]
    public void NonOptometry_OdToken_IsStillBlocked()
    {
        var findings = Lint("He was prescribed ramipril, 5 mg od.", isModelAnswer: true);
        Assert.Contains(findings, f => f.RuleId == Rule && f.Severity == RuleSeverity.Critical);
    }

    /// <summary>
    /// A CANDIDATE writing "BD" is a style note to feed back, not a publication gate, so the
    /// candidate path keeps the advisory severity it has always had.
    /// </summary>
    [Fact]
    public void CandidateLetter_WithAbbreviation_StaysAdvisory()
    {
        var findings = Lint("He was prescribed metformin, 500 mg BD with meals.", isModelAnswer: false);
        var hit = Assert.Single(findings, f => f.RuleId == Rule);
        Assert.Equal(RuleSeverity.Minor, hit.Severity);
    }
}
