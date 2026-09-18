using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Cross-profession repair, 18 Sep 2026 — sentence_fragment.
///
/// The subject-verb adjacency branch allowed only an "-ly" adverb between a bare-name subject and
/// its verb, although the titled-subject branch already allowed "now", "still", "again" and the
/// rest. So "Harry now cries and panics whenever his mother leaves his sight." was reported as
/// having "no subject and finite verb", and deleting the single word "now" made it pass.
/// </summary>
public sealed class WritingFragmentAdverbTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());
    private const string Check = "sentence_fragment";

    private static string Letter(string sentence) => $"""
        The Child and Family Health Nurse
        Community Health Centre
        Bankstown

        16 May 2011

        Dear Sir/Madam,
        Re: Harry Kovacs, DOB: 15 April 2006

        I am writing to refer Harry Kovacs, who has developed separation anxiety, for assessment and management.

        {sentence} He has become socially withdrawn and is refusing to attend kindergarten.

        Harry lives with his mother, Elizabeth, who has a history of depression.

        I would be grateful if you could arrange a joint meeting with Elizabeth and me.

        Should there be any queries, kindly do not hesitate to contact me.

        Yours faithfully,

        Registered Nurse
        """;

    private static IReadOnlyList<LintFinding> Lint(string sentence) =>
        Engine.Lint(new WritingLintInput(
            LetterText: Letter(sentence),
            LetterType: "routine_referral",
            IsModelAnswer: true));

    [Theory]
    [InlineData("Harry now cries and panics whenever his mother leaves his sight.")]
    [InlineData("Harry still cries whenever his mother leaves the room.")]
    [InlineData("Harry again refuses to attend kindergarten.")]
    [InlineData("Harry currently sleeps in his mother's bed.")]
    [InlineData("Harry frequently cries at drop-off.")]
    [InlineData("Harry cries whenever his mother leaves his sight.")]
    public void An_adverb_between_a_bare_name_subject_and_its_verb_is_not_a_fragment(string sentence)
    {
        Assert.DoesNotContain(Lint(sentence), f => f.RuleId.Contains(Check));
    }

    [Theory]
    [InlineData("Now crying and panicking whenever his mother leaves.")]
    [InlineData("Because of the separation anxiety.")]
    public void A_genuine_fragment_still_fires(string sentence)
    {
        Assert.Contains(Lint(sentence), f => f.RuleId.Contains(Check));
    }
}
