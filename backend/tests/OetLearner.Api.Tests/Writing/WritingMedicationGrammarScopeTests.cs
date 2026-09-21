using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Cross-profession repair, 18 Sep 2026 — medication_passive_grammar.
///
/// The rule reads the token before a participle as the medication, so sentences with no drug in
/// them were reported as note-form English: "Alison is also becoming withdrawn at home" was
/// flagged on "becoming withdrawn" and told to write "becoming was discontinued", and
/// "she had ceased reporting hallucinations" was flagged on "had ceased". A gerund and a bare
/// auxiliary are never drug names.
/// </summary>
public sealed class WritingMedicationGrammarScopeTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());
    private const string Check = "medication_passive_grammar";

    private static string Letter(string sentence) => $"""
        Dr Priya Shah
        Community Health Centre
        Newtown

        7 March 2012

        Dear Dr Shah,
        Re: Alison Cooper, DOB: 14 June 2002

        I am writing to refer Alison Cooper for psychological assessment following repeated school absences.

        {sentence} Her attendance has fallen in each of the last two school years.

        Alison lives with her mother and has mild asthma.

        I would be grateful if you could determine whether she has underlying grief-related problems.

        Should there be any queries, kindly do not hesitate to contact me.

        Yours sincerely,

        Registered Nurse
        """;

    private static IReadOnlyList<LintFinding> Lint(string sentence) =>
        Engine.Lint(new WritingLintInput(
            LetterText: Letter(sentence),
            LetterType: "routine_referral",
            IsModelAnswer: true));

    [Theory]
    [InlineData("Alison is also becoming withdrawn at home.")]
    [InlineData("She had ceased reporting auditory hallucinations.")]
    [InlineData("Alison has been feeling withdrawn since her father's death.")]
    public void A_sentence_with_no_medication_in_it_is_not_medication_grammar(string sentence)
    {
        Assert.DoesNotContain(Lint(sentence), f => f.RuleId.Contains(Check));
    }

    [Fact]
    public void A_real_note_form_medication_subject_still_fires()
    {
        Assert.Contains(Lint("Metformin discontinued."), f => f.RuleId.Contains(Check));
    }
}
