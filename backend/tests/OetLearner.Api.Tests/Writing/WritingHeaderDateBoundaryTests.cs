using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Cross-profession repair, 18 Sep 2026 — body_no_todays_date.
///
/// The header date was matched in the body as a bare substring, so a letter dated "5 May 2017"
/// was flagged for "his discharge on 15 May 2017": the header date sits inside "1|5 May 2017".
/// Mr Joe Black's letter could only pass by dropping the year off that other, legitimate date.
/// </summary>
public sealed class WritingHeaderDateBoundaryTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());
    private const string Check = "body_no_todays_date";

    private static string Letter(string bodySentence) => $"""
        The Community Nurse
        Community Health Centre
        Newtown

        5 May 2017

        Dear Sir/Madam,
        Re: Mr Joe Black, aged 24

        I am writing to refer Mr Joe Black for assessment of his disability level and therapy needs.

        {bodySentence}

        Mr Black lives with his parents and is allergic to penicillin.

        I would be grateful if you could arrange daily home visits for the first two weeks after discharge.

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
    [InlineData("His home needs modification before his discharge on 15 May 2017.")]
    [InlineData("He was transferred to this ward on 25 May 2017 for rehabilitation.")]
    public void A_different_date_that_merely_ends_with_the_header_date_is_not_a_repeat(string sentence)
    {
        Assert.DoesNotContain(Lint(sentence), f => f.RuleId.Contains(Check));
    }

    [Theory]
    [InlineData("Mr Black was reviewed on 5 May 2017 and remains comfortable.")]
    [InlineData("On 5 May 2017, he began mobilising in a wheelchair.")]
    public void Repeating_the_header_date_itself_still_fires(string sentence)
    {
        Assert.Contains(Lint(sentence), f => f.RuleId.Contains(Check));
    }
}
