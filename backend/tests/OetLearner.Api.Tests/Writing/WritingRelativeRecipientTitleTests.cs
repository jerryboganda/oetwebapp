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

    // ---- Mrs Jane Brown: the relative is named with no first name at all ----
    //
    // Her task says "help Mr Brown care for his wife at home", so the body's "Mr Brown" is the
    // source's own wording for her husband. The letter had to write "Mrs Brown's husband" instead.

    private static string BrownLetter(string bodyExtra) => $"""
        Ms Angela Hope
        Community Care Manager
        Government Community Services

        7 September 2026

        Dear Ms Hope,
        Re: Mrs Jane Brown, DOB: 14 February 1940

        I am writing to refer Mrs Jane Brown for an urgent social-worker assessment.

        Mrs Brown has Alzheimer's disease and cannot remember basic information or routines.{bodyExtra}

        Mrs Brown had a stroke four years ago with right-sided weakness.

        I would be grateful if you could determine the community services that would best help her husband care for her at home.

        Should there be any queries, kindly do not hesitate to contact me.

        Yours sincerely,

        Registered Nurse
        """;

    [Fact]
    public void A_relative_the_task_itself_names_with_no_first_name_is_not_a_title_switch()
    {
        var findings = Engine.Lint(new WritingLintInput(
            LetterText: BrownLetter(" Mr Brown reports finding her care increasingly difficult."),
            LetterType: "routine_referral",
            TaskText: "Write a letter to Angela Hope, Community Care Manager, requesting services to help Mr Brown care for his wife at home.",
            IsModelAnswer: true));
        Assert.DoesNotContain(findings, f => f.RuleId.Contains(Check));
    }

    [Fact]
    public void Without_that_support_a_bare_wrong_title_still_fires()
    {
        var findings = Engine.Lint(new WritingLintInput(
            LetterText: BrownLetter(" Mr Brown was reviewed on the ward and remains comfortable."),
            LetterType: "routine_referral",
            TaskText: "Write a letter to Angela Hope, Community Care Manager, requesting a social-worker assessment.",
            IsModelAnswer: true));
        Assert.Contains(findings, f => f.RuleId.Contains(Check));
    }
}
