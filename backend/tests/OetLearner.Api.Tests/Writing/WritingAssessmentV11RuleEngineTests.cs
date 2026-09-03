using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

public sealed class WritingAssessmentV11RuleEngineTests
{
    [Theory]
    [InlineData("content_requires_smoking_drinking", "content")]
    [InlineData("content_requires_allergy_for_atopic", "content")]
    [InlineData("urgent_intro_contains_urgent", "purpose")]
    [InlineData("discharge_omits_knownto_gp", "content")]
    [InlineData("non_medical_no_jargon", "genre_style")]
    [InlineData("linker_however_punctuation", "language")]
    public void Every_error_category_has_one_primary_criterion(string ruleId, string expected)
    {
        Assert.Equal(expected, WritingAssessmentV11RuleEngine.PrimaryCriterionFor(ruleId));
    }

    [Fact]
    public void House_style_accepts_semicolon_comma_before_and_after_however()
    {
        var findings = WritingAssessmentV11RuleEngine.EvaluateHouseStyle(
            "The patient improved; however, further review is required.");

        Assert.DoesNotContain(findings, finding => finding.RuleId == "R12.9");
    }

    [Fact]
    public void House_style_flags_missing_comma_after_however()
    {
        var findings = WritingAssessmentV11RuleEngine.EvaluateHouseStyle(
            "The patient improved; however further review is required.");

        var finding = Assert.Single(findings, item => item.RuleId == "R12.9");
        Assert.Equal("language", finding.PrimaryCriterionCode);
        Assert.Equal("major", finding.Severity);
    }

    [Fact]
    public void In_addition_to_is_not_treated_as_a_clause_joiner()
    {
        var findings = WritingAssessmentV11RuleEngine.EvaluateHouseStyle(
            "Please continue in addition to the current treatment.");

        Assert.DoesNotContain(findings, finding => finding.RuleId == "R12.11");
    }

    [Fact]
    public void Minor_re_line_rejects_titles_and_requires_first_name_in_each_paragraph()
    {
        var engine = new WritingAssessmentV11RuleEngine(new WritingRuleEngine(new RulebookLoader()));
        var findings = engine.Evaluate(
            new WritingLintInput("Re: Master John Smith\n\nThe patient has asthma.", "routine_referral", PatientAge: 17),
            "Patient: John Smith\nAge: 17\nDiagnosis: asthma.");

        Assert.Contains(findings, x => x.RuleId == "R06.10");
    }

    [Fact]
    public void Adult_re_line_requires_title_plus_last_name()
    {
        var engine = new WritingAssessmentV11RuleEngine(new WritingRuleEngine(new RulebookLoader()));
        var findings = engine.Evaluate(
            new WritingLintInput("Re: John Smith\n\nJohn Smith is stable.", "routine_referral", PatientAge: 18),
            "Patient: John Smith\nAge: 18\nDiagnosis: asthma.");

        Assert.Contains(findings, x => x.RuleId == "R06.11");
    }
}
