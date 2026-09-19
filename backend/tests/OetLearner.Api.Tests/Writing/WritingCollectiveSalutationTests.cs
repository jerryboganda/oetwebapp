using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Owner decision 15 (19 Sep 2026): a salutation to a GROUP names no individual, so it closes
/// "Yours faithfully," exactly like a role. The headlice advisory's own official sample answer opens
/// "Dear Parent," and signs "Yours faithfully,", yet the validator read "Parent" as a personal name
/// and rejected the correct sign-off at Critical severity — and the same rule runs on candidate
/// letters, so a candidate who wrote it correctly was marked wrong.
/// </summary>
public sealed class WritingCollectiveSalutationTests
{
    private const string Rule = "BUILTIN.yours_sincerely_vs_faithfully";

    [Theory]
    [InlineData("Dear Parent,")]
    [InlineData("Dear Parents and Guardians,")]
    [InlineData("Dear Families with young children,")]
    [InlineData("Dear Residents of Maple House,")]
    [InlineData("Dear Colleagues,")]
    [InlineData("Dear Staff,")]
    public void A_group_salutation_is_an_unnamed_recipient(string salutation)
        => Assert.True(WritingRuleEngine.SalutationIsUnnamedRecipient(salutation));

    [Theory]
    [InlineData("Dear Mrs Burnley,")]
    [InlineData("Dear Dr Warren,")]
    [InlineData("Dear Mr Bennet,")]
    // A bare surname is a named person: an open "any plural noun" test would get this one wrong,
    // which is why the audience list is closed.
    [InlineData("Dear Williams,")]
    [InlineData("Dear Jones,")]
    [InlineData("Dear John,")]
    public void A_named_person_is_not_an_unnamed_recipient(string salutation)
        => Assert.False(WritingRuleEngine.SalutationIsUnnamedRecipient(salutation));

    [Theory]
    [InlineData("Dear Registrar,")]
    [InlineData("Dear Emergency Department Consultant on Duty,")]
    [InlineData("Dear Sir/Madam,")]
    public void The_existing_role_salutations_are_unchanged(string salutation)
        => Assert.True(WritingRuleEngine.SalutationIsUnnamedRecipient(salutation));

    private static string Letter(string salutation, string signOff) =>
        "Riverside Primary School\nRiverside\n\n14 July 2012\n\n"
        + salutation + "\nRe: Headlice\n\n"
        + "I am writing to advise you about headlice, following an outbreak at the school.\n\n"
        + "Should there be any queries, kindly do not hesitate to contact me.\n\n"
        + signOff + "\n\nPharmacist";

    private static IReadOnlyList<LintFinding> Lint(string letter, bool modelAnswer)
        => new WritingRuleEngine(new RulebookLoader()).Lint(new WritingLintInput(
            LetterText: letter, LetterType: "LT-OT", Profession: ExamProfession.Pharmacy, IsModelAnswer: modelAnswer));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_group_salutation_accepts_yours_faithfully_for_a_model_answer_and_a_candidate(bool modelAnswer)
        => Assert.DoesNotContain(Lint(Letter("Dear Parent,", "Yours faithfully,"), modelAnswer), f => f.RuleId == Rule);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_group_salutation_rejects_yours_sincerely(bool modelAnswer)
        => Assert.Contains(Lint(Letter("Dear Parent,", "Yours sincerely,"), modelAnswer), f => f.RuleId == Rule);

    [Fact]
    public void A_named_recipient_still_requires_yours_sincerely()
    {
        Assert.Contains(Lint(Letter("Dear Mrs Burnley,", "Yours faithfully,"), true), f => f.RuleId == Rule);
        Assert.DoesNotContain(Lint(Letter("Dear Mrs Burnley,", "Yours sincerely,"), true), f => f.RuleId == Rule);
    }
}
