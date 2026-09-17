using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Cross-profession repair, 18 Sep 2026 — patient_title_mismatch.
///
/// The check scanned the WHOLE letter for "Mr/Mrs/Ms/Miss &lt;patient surname&gt;", so a recipient who
/// shares the patient's surname was read as the patient carrying the wrong title. Mrs Anita
/// Ramamurthy's nursing task addresses the letter to her husband, "Mr. Krishnan Ramamurthy", so a
/// correct letter must contain both titles — and the only variants that passed were ones that
/// stripped the husband's title or surname, i.e. defective letters. The patient's title is a body
/// concern, and a different first name marks a different person.
/// </summary>
public sealed class WritingRelativeRecipientTitleTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());
    private const string Check = "patient_title_mismatch";

    private static string Letter(string block, string salutation, string bodyExtra = "") => $$"""
        {{block}}
        #648 Bourke Street
        Melbourne

        23 June 2017

        {{salutation}}
        Re: Mrs Anita Ramamurthy, aged 59

        I am writing to ask for your support with Mrs Ramamurthy's insulin injections following her discharge today.

        Mrs Ramamurthy was admitted with poorly controlled diabetes and an interval appendectomy.{{bodyExtra}}

        Mrs Ramamurthy lives with her family and manages her own medications.

        I would be grateful if you could chart her fasting blood sugars before the follow-up consultation.

        Should there be any queries, kindly do not hesitate to contact me.

        Yours sincerely,

        Registered Nurse
        """;

    private static IReadOnlyList<LintFinding> Lint(string letter) =>
        Engine.Lint(new WritingLintInput(LetterText: letter, LetterType: "discharge", IsModelAnswer: true));

    [Fact]
    public void A_husband_recipient_sharing_the_surname_is_not_the_patient()
    {
        Assert.DoesNotContain(Lint(Letter("Mr Krishnan Ramamurthy", "Dear Mr Ramamurthy,")),
            f => f.RuleId.Contains(Check));
    }

    [Fact]
    public void A_relative_named_in_the_body_with_the_same_surname_is_allowed()
    {
        Assert.DoesNotContain(
            Lint(Letter("Mr Krishnan Ramamurthy", "Dear Mr Ramamurthy,",
                " Mr Krishnan Ramamurthy has been administering her injections.")),
            f => f.RuleId.Contains(Check));
    }

    [Fact]
    public void The_patients_own_title_is_still_pinned_in_the_body()
    {
        // Same first name as the Re: line, wrong title — the defect the rule exists for.
        Assert.Contains(
            Lint(Letter("Mr Krishnan Ramamurthy", "Dear Mr Ramamurthy,",
                " Mr Anita Ramamurthy was reviewed on the ward.")),
            f => f.RuleId.Contains(Check));
    }

    [Fact]
    public void A_bare_wrong_title_on_the_patient_surname_in_the_body_still_fires()
    {
        Assert.Contains(
            Lint(Letter("Mr Krishnan Ramamurthy", "Dear Mr Ramamurthy,",
                " Mr Ramamurthy's blood sugars remained elevated throughout the admission.")),
            f => f.RuleId.Contains(Check));
    }
}
