using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Cross-profession repair, 18 Sep 2026 — letter_date_unsupported floor.
///
/// Rule 39: "appointments, planned or due dates and the LMP never count" for dating a letter. A
/// date introduced by "from" was still counted as a documented encounter, so Mrs Marjorie
/// Mellors's note "Physiotherapy outpatients three times weekly from 15 May 2018" set the floor
/// and rejected her letter dated 14 May 2018 — the transfer day her notes and her task both
/// describe — forcing the physiotherapy start to be written as "from today" instead.
/// </summary>
public sealed class WritingPlannedStartDateFloorTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());
    private const string Check = "letter_date_unsupported";

    private const string Notes =
        "Admitted 10 May 2018 following an accident. | Discharged 14 May 2018. | "
        + "On 11 May 2018, she underwent open reduction and internal fixation of the distal right radius. | "
        + "Earlier transfer to Primrose Retirement Home has been arranged by the social worker for 14 May 2018. | "
        + "Physiotherapy outpatients three times weekly from 15 May 2018. | "
        + "Stitches to be removed on 20 May 2018 at the Orthopaedic Outpatients Department.";

    private static string Letter(string date) => $"""
        Ms Jackson
        Resident Warden
        Primrose Retirement Home

        {date}

        Dear Ms Jackson,
        Re: Mrs Marjorie Mellors, aged 77

        I am writing to request your ongoing nursing care for Mrs Marjorie Mellors, who transferred to your home today.

        Mrs Mellors was admitted on 10 May 2018 after being knocked down by a mobility scooter.

        Mrs Mellors lives alone and has osteoporosis, treated with calcitonin.

        I would be grateful if you could arrange for her son or your staff to accompany her to the Orthopaedic Outpatients Department.

        Should there be any queries, kindly do not hesitate to contact me.

        Yours sincerely,

        Charge Nurse
        """;

    private static IReadOnlyList<LintFinding> Lint(string date) =>
        Engine.Lint(new WritingLintInput(
            LetterText: Letter(date),
            LetterType: "routine_referral",
            CaseNotesText: Notes,
            IsModelAnswer: true));

    [Fact]
    public void A_letter_dated_on_the_documented_transfer_day_is_accepted()
    {
        Assert.DoesNotContain(Lint("14 May 2018"), f => f.RuleId.Contains(Check));
    }

    [Theory]
    [InlineData("Physiotherapy three times weekly from 15 May 2018.")]
    [InlineData("Home oxygen commencing 15 May 2018.")]
    [InlineData("Compression therapy starting 15 May 2018.")]
    [InlineData("Stitches to be removed on 20 May 2018.")]
    [InlineData("Follow-up appointment 20 May 2018.")]
    public void A_planned_start_or_booking_never_sets_the_floor(string plan)
    {
        var findings = Engine.Lint(new WritingLintInput(
            LetterText: Letter("14 May 2018"),
            LetterType: "routine_referral",
            CaseNotesText: "Admitted 10 May 2018. | Discharged 14 May 2018. | " + plan,
            IsModelAnswer: true));
        Assert.DoesNotContain(findings, f => f.RuleId.Contains(Check));
    }

    [Fact]
    public void A_real_later_encounter_still_sets_the_floor()
    {
        var findings = Engine.Lint(new WritingLintInput(
            LetterText: Letter("14 May 2018"),
            LetterType: "routine_referral",
            CaseNotesText: "Admitted 10 May 2018. | On 16 May 2018 the wound was reviewed and redressed.",
            IsModelAnswer: true));
        Assert.Contains(findings, f => f.RuleId.Contains(Check));
    }
}
