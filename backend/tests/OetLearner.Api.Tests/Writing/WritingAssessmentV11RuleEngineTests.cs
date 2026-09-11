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
    // BUILTIN.* ids the old substring heuristic sent to "content" or the
    // wrong criterion by accident (D5).
    [InlineData("BUILTIN.no_contractions", "genre_style")]
    [InlineData("BUILTIN.year_not_abbreviated", "organisation_layout")]
    [InlineData("BUILTIN.paragraph_start_patient_name", "genre_style")]
    [InlineData("BUILTIN.discharge_intro_template", "purpose")]
    [InlineData("BUILTIN.letter_body_length", "conciseness_clarity")]
    [InlineData("BUILTIN.linker_comma_and_case", "language")]
    [InlineData("BUILTIN.closure_contact_offer", "organisation_layout")]
    // Legacy profession books report R-code rule ids: mapped by section.
    [InlineData("R03.8", "content")]
    [InlineData("R06.11", "organisation_layout")]
    [InlineData("R07.3", "purpose")]
    [InlineData("R10.5", "language")]
    [InlineData("R12.9", "language")]
    [InlineData("R13.2", "purpose")]
    [InlineData("R14.4", "content")]
    [InlineData("R15.2", "genre_style")]
    // Owner registry rows share their first check_id's criterion.
    [InlineData("OWN-W-022", "language")]
    [InlineData("OWN-W-013", "purpose")]
    [InlineData("OWN-W-037", "content")]
    [InlineData("G-W-112", "content")]
    [InlineData(null, "content")]
    public void Every_error_category_has_one_primary_criterion(string? ruleId, string expected)
    {
        Assert.Equal(expected, WritingAssessmentV11RuleEngine.PrimaryCriterionFor(ruleId));
    }

    [Fact]
    public void Every_supported_check_id_has_an_explicit_primary_criterion()
    {
        Assert.All(WritingRuleEngine.SupportedCheckIds, checkId =>
        {
            Assert.True(
                WritingAssessmentV11RuleEngine.CheckIdCriteria.TryGetValue(checkId, out var mapped),
                $"No criterion mapping for check id '{checkId}'.");
            Assert.True(OetLearner.Api.Domain.WritingAssessmentV11Invariants.IsPrimaryCriterion(mapped.Criterion));
        });
    }

    // Owner Rev8 linker rule (OWN-W-022): sentence-initial "However, ..." and
    // "...; however, ..." between complete clauses are CORRECT. The removed
    // v1.1 house style (semicolon-only R12.9-R12.11, comma before "for
    // which"/causal "as" R12.16/R12.17) flagged both.
    [Theory]
    [InlineData(ExamProfession.Medicine)]
    [InlineData(ExamProfession.Dietetics)]
    public void Correct_owner_linker_punctuation_is_not_flagged(ExamProfession profession)
    {
        const string letter =
            "Dr Paul Brown\nCity Hospital\n\n1 June 2026\n\nDear Dr Brown,\nRe: Mr David Taylor, aged 55\n\n" +
            "I am writing to refer Mr Taylor for assessment of his right knee pain.\n\n" +
            "Mr Taylor's pain settled after physiotherapy. However, he reported new swelling in May 2026. " +
            "His mobility improved; however, the swelling has persisted, for which he takes paracetamol.\n\n" +
            "Should you have any queries, please do not hesitate to contact me.\n\nYours sincerely,\n\nDoctor";
        var engine = new WritingAssessmentV11RuleEngine(new WritingRuleEngine(new RulebookLoader()));

        var findings = engine.Evaluate(new WritingLintInput(letter, "LT-RR", PatientAge: 55, Profession: profession));

        string[] linkerRules =
        [
            "R12.9", "R12.10", "R12.11", "R12.16", "R12.17",
            "BUILTIN.linker_comma_and_case", "BUILTIN.linker_however_punctuation",
        ];
        Assert.DoesNotContain(findings, f => linkerRules.Contains(f.RuleId));
        Assert.DoesNotContain(findings, f => f.Quote is { } q && q.Contains("owever", StringComparison.Ordinal)
            && f.Category == "punctuation");
    }

    [Fact]
    public void Linker_joining_two_clauses_with_a_comma_is_still_flagged_under_language()
    {
        const string letter =
            "Dear Dr Brown,\nRe: Mr David Taylor, aged 55\n\n" +
            "I am writing to refer Mr Taylor for assessment of his right knee pain.\n\n" +
            "Mr Taylor's pain settled, however he reported new swelling in May 2026.\n\n" +
            "Should you have any queries, please do not hesitate to contact me.\n\nYours sincerely,\n\nDoctor";
        var engine = new WritingAssessmentV11RuleEngine(new WritingRuleEngine(new RulebookLoader()));

        var findings = engine.Evaluate(new WritingLintInput(letter, "LT-RR", PatientAge: 55));

        var finding = Assert.Single(findings, f => f.RuleId == "BUILTIN.linker_however_punctuation");
        Assert.Equal("language", finding.PrimaryCriterionCode);
        Assert.Equal("punctuation", finding.Category);
    }

    // Rev8 Re: form "Mr David Taylor, aged 55" with the title + surname at
    // the first mention of each body paragraph is correct: the removed
    // EvaluateNaming (mis-labelled R06.10/R06.11) no longer fires on it.
    [Fact]
    public void Rev8_adult_re_line_and_paragraph_naming_are_not_flagged()
    {
        const string letter =
            "Dear Dr Brown,\nRe: Mr David Taylor, aged 55\n\n" +
            "I am writing to refer Mr Taylor for assessment of his right knee pain.\n\n" +
            "Mr Taylor is stable. He reports no further pain and he is keen to return to work.\n\n" +
            "Should you have any queries, please do not hesitate to contact me.\n\nYours sincerely,\n\nDoctor";
        var engine = new WritingAssessmentV11RuleEngine(new WritingRuleEngine(new RulebookLoader()));

        var findings = engine.Evaluate(
            new WritingLintInput(letter, "routine_referral", PatientAge: 55),
            "Patient: David Taylor\nAge: 55\nDiagnosis: osteoarthritis.");

        string[] namingRules =
        [
            "R06.10", "R06.11",
            "BUILTIN.minor_naming_convention", "BUILTIN.paragraph_start_patient_name", "BUILTIN.body_uses_last_name_only",
        ];
        Assert.DoesNotContain(findings, f => namingRules.Contains(f.RuleId));
    }

    [Fact]
    public void Minor_re_line_with_a_title_is_flagged_by_the_engine_naming_rule()
    {
        var engine = new WritingAssessmentV11RuleEngine(new WritingRuleEngine(new RulebookLoader()));
        var findings = engine.Evaluate(new WritingLintInput(
            "Dear Dr Brown,\nRe: Master John Smith\n\nI am writing to refer John for review of his asthma.\n\nYours sincerely,\n\nDoctor",
            "routine_referral",
            PatientAge: 12,
            PatientIsMinor: true));

        var finding = Assert.Single(findings, x => x.RuleId == "BUILTIN.minor_naming_convention");
        Assert.Equal("genre_style", finding.PrimaryCriterionCode);
    }
}
