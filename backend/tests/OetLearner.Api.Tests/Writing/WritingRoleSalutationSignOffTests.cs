using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Cross-profession repair, 18 Sep 2026 — SalutationIsUnnamedRecipient.
///
/// The role-noun pattern required the role to be the salutation's last word, so
/// "Dear Emergency Department Consultant on Duty," fell out of it, was read as a personal name,
/// and yours_sincerely_vs_faithfully demanded "Yours sincerely" — the opposite of owner decision
/// 15 / OA2-17, and the opposite of what the official OET sample response for that very case
/// (Ms Patricia Styles's urgent referral) signs. A duty qualifier names no person.
/// </summary>
public sealed class WritingRoleSalutationSignOffTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());
    private const string Check = "yours_sincerely_vs_faithfully";

    [Theory]
    [InlineData("Dear Emergency Department Consultant on Duty,")]
    [InlineData("Dear Consultant on Duty,")]
    [InlineData("Dear Registrar on Call,")]
    [InlineData("Dear Nurse in Charge,")]
    [InlineData("Dear Nurse on-call,")]
    [InlineData("Dear Emergency Department Consultant,")]
    [InlineData("Dear Neuro-Ophthalmologist,")]
    [InlineData("Dear Sir/Madam,")]
    // 18 Sep 2026: "Leader" was missing from the role list (Lead cannot match it) and a
    // service suffix pushed "Dear Director of Nursing," out, though "Dear Director," was fine.
    [InlineData("Dear Team Leader,")]
    [InlineData("Dear Director of Nursing,")]
    [InlineData("Dear Head of Midwifery,")]
    [InlineData("Dear Charge Nurse,")]
    // Hyphenated roles were still read as personal names.
    [InlineData("Dear Nurse-in-Charge,")]
    [InlineData("Dear Registrar-on-Call,")]
    // More bare roles the OET tasks actually address.
    [InlineData("Dear Supervisor,")]
    [InlineData("Dear Resident Warden,")]
    [InlineData("Dear Social Worker,")]
    [InlineData("Dear Community Social Worker,")]
    public void A_role_or_duty_salutation_names_no_person(string salutation)
    {
        Assert.True(WritingRuleEngine.SalutationIsUnnamedRecipient(salutation));
    }

    [Theory]
    [InlineData("Dear Dr Macnor,")]
    [InlineData("Dear Ms Welsh,")]
    [InlineData("Dear Dr Priya Shah,")]
    public void A_titled_person_is_still_a_named_recipient(string salutation)
    {
        Assert.False(WritingRuleEngine.SalutationIsUnnamedRecipient(salutation));
    }

    private static string Letter(string salutation, string signOff) => $"""
        Emergency Department Consultant on Duty
        Newtown Hospital
        100 Main Street
        Newtown

        30 August 2019

        {salutation}
        Re: Ms Patricia Styles, DOB: 27 April 1957

        I am writing to refer Ms Patricia Styles for urgent assessment of a possible relapse of pericarditis.

        Ms Styles reported chest pain on exertion, with a temperature of 38.2 °C.

        I would be grateful if you could accept Ms Styles's transfer at your earliest convenience.

        Should there be any queries, kindly do not hesitate to contact me.

        {signOff}

        Registered Nurse
        """;

    private static IReadOnlyList<LintFinding> Lint(string salutation, string signOff) =>
        Engine.Lint(new WritingLintInput(
            LetterText: Letter(salutation, signOff),
            LetterType: "urgent_referral",
            IsModelAnswer: true));

    [Fact]
    public void Faithfully_is_accepted_after_a_duty_role_salutation()
    {
        Assert.DoesNotContain(Lint("Dear Emergency Department Consultant on Duty,", "Yours faithfully,"),
            f => f.RuleId.Contains(Check));
    }

    [Fact]
    public void Sincerely_after_a_duty_role_salutation_is_the_defect()
    {
        // This is what the letter was forced into before the fix.
        Assert.Contains(Lint("Dear Emergency Department Consultant on Duty,", "Yours sincerely,"),
            f => f.RuleId.Contains(Check));
    }

    [Fact]
    public void A_named_recipient_still_has_to_close_sincerely()
    {
        Assert.Contains(Lint("Dear Dr Macnor,", "Yours faithfully,"), f => f.RuleId.Contains(Check));
        Assert.DoesNotContain(Lint("Dear Dr Macnor,", "Yours sincerely,"), f => f.RuleId.Contains(Check));
    }
}
