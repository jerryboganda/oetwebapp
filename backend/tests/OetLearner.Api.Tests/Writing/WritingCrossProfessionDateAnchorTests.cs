using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Cross-profession repair, 18 Sep 2026 — letter_date_unsupported branch 3.
///
/// Mr Satchell's stored nursing answer was dated "6 September 2026" with no support anywhere: the
/// task had no today's date and his notes' latest entry is a bare month ("February 2018"). Both
/// existing branches need an anchor to compare against, so an invented date passed silently. The
/// new branch fires only when BOTH anchors are missing, and only for Model Answers.
/// </summary>
public sealed class WritingCrossProfessionDateAnchorTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());
    private const string Check = "letter_date_unsupported";

    private const string Letter = """
        The Community Nurse
        Community Health Centre
        Maroubra

        6 September 2026

        Dear Sir/Madam,
        Re: Mr Phillip Satchell, aged 73

        I am writing to transfer the care of Mr Satchell, who requires continued community nursing.

        Mr Satchell has chronic bilateral leg ulcers with recurrent infections.

        I would be grateful if you could continue his twice-daily dressings.

        Should there be any queries, kindly do not hesitate to contact me.

        Yours faithfully,

        Registered Nurse
        """;

    private static IReadOnlyList<LintFinding> Lint(string notes, string? todayDate, bool modelAnswer = true) =>
        Engine.Lint(new WritingLintInput(
            LetterText: Letter,
            LetterType: "transfer",
            CaseNotesText: notes,
            TodayDate: todayDate,
            IsModelAnswer: modelAnswer));

    private const string NotesNoDate =
        "Patient: Phillip Satchell, aged 73 | Chronic bilateral leg ulcers | Last visit to community centre: February 2018";

    [Fact]
    public void An_invented_date_is_flagged_when_the_task_has_no_today_date_and_the_notes_have_none()
    {
        Assert.Contains(Lint(NotesNoDate, todayDate: null), f => f.RuleId.Contains(Check));
    }

    [Fact]
    public void Setting_the_scenarios_today_date_is_what_clears_it()
    {
        // The repair is a data fix, not a rewrite: the same letter passes once the task carries a date.
        Assert.DoesNotContain(Lint(NotesNoDate, todayDate: "6 September 2026"), f => f.RuleId.Contains(Check));
    }

    [Theory]
    [InlineData("Initial review 28 February 2018: ulcer unchanged")]
    [InlineData("28/02/2018 review — ulcer unchanged")]
    [InlineData("28.02.18 review — ulcer unchanged")]
    public void A_dated_note_leaves_this_branch_to_the_existing_two(string dated)
    {
        // The other branches own the comparison; this one must not double-report.
        var findings = Lint(NotesNoDate + " | " + dated, todayDate: null);
        Assert.DoesNotContain(findings, f => f.RuleId.Contains(Check) && (f.Message ?? "").Contains("no date at all"));
    }

    [Fact]
    public void A_date_of_birth_alone_does_not_anchor_the_letter_date()
    {
        var findings = Lint("Patient: Phillip Satchell | DOB: 12 March 1953 | Chronic bilateral leg ulcers", todayDate: null);
        Assert.Contains(findings, f => f.RuleId.Contains(Check));
    }

    [Fact]
    public void The_candidate_lane_never_sees_it()
    {
        Assert.DoesNotContain(Lint(NotesNoDate, todayDate: null, modelAnswer: false), f => f.RuleId.Contains(Check));
    }

    [Fact]
    public void Without_canonical_case_notes_it_stays_silent()
    {
        var findings = Engine.Lint(new WritingLintInput(LetterText: Letter,
            LetterType: "transfer", IsModelAnswer: true));
        Assert.DoesNotContain(findings, f => f.RuleId.Contains(Check));
    }

    // ---- Randhawa: a stated today's date outranks the latest-note ceiling ----
    //
    // His task states 31 January 2017; his newest note is 29 January. With both branches live no
    // date could pass at all, so the scenario was unpassable by writing.

    private const string RandhawaNotes =
        "Patient: Tej Singh Randhawa | DOB: 9 September 1976 | 10/01/2017 first presentation "
        + "| 24/01/2017 review | 29/01/2017 referral encounter, for orthopaedic assessment";

    private static string DatedLetter(string date) => Letter.Replace("6 September 2026", date);

    [Fact]
    public void The_tasks_stated_today_date_is_accepted_even_when_it_post_dates_every_note()
    {
        var findings = Engine.Lint(new WritingLintInput(
            LetterType: "transfer",
            LetterText: DatedLetter("31 January 2017"),
            CaseNotesText: RandhawaNotes,
            TodayDate: "31 January 2017",
            IsModelAnswer: true));
        Assert.DoesNotContain(findings, f => f.RuleId.Contains(Check));
    }

    [Fact]
    public void A_date_that_is_not_the_stated_today_date_still_fails()
    {
        var findings = Engine.Lint(new WritingLintInput(
            LetterType: "transfer",
            LetterText: DatedLetter("29 January 2017"),
            CaseNotesText: RandhawaNotes,
            TodayDate: "31 January 2017",
            IsModelAnswer: true));
        Assert.Contains(findings, f => f.RuleId.Contains(Check));
    }

    [Fact]
    public void Without_a_stated_today_date_the_note_ceiling_still_catches_an_invented_date()
    {
        var findings = Engine.Lint(new WritingLintInput(
            LetterType: "transfer",
            LetterText: DatedLetter("6 September 2026"),
            CaseNotesText: RandhawaNotes,
            IsModelAnswer: true));
        Assert.Contains(findings, f => f.RuleId.Contains(Check));
    }
}
