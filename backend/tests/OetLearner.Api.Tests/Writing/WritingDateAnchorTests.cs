using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Owner decisions of 19 Sep 2026 on scenarios whose SOURCE gives no day-level letter date.
///
/// Before this change a date line was mandatory, yet letter_date_unsupported had nothing to check a
/// date against when a scenario carried no date, so ANY date passed: "6 September 2026",
/// "1 January 2020" and "29 February 2044" all returned zero findings on one letter. That is how
/// fabricated dates reached the catalogue.
///
/// The tests pin what the owner asked for, and just as deliberately what must NOT change:
///  - None / MonthOnly: the letter date is optional, and a Model Answer may not invent one (or a day);
///  - a candidate is never penalised for omitting the date, nor for a reasonable assumed one;
///  - Unknown (a caller that did not classify) and Day behave exactly as before, so a caller that
///    lacks the data can never switch the date requirement off by accident.
/// </summary>
public sealed class WritingDateAnchorTests
{
    private const string Structure = "BUILTIN.letter_structure_order";
    private const string Layout = "BUILTIN.model_answer_layout";
    private const string DateRule = "BUILTIN.letter_date_unsupported";

    private static string Letter(string? dateLine, bool recipientBlock = true)
    {
        var head = recipientBlock ? "Dr Alice Warren\nCommunity Medical Centre\n14 Bridge Road\nNewtown\n\n" : string.Empty;
        var date = dateLine is null ? string.Empty : dateLine + "\n\n";
        return head + date
            + "Dear Dr Warren,\n"
            + "Re: Mr John Baker, DOB: 2 May 1948\n\n"
            + "I am writing to refer Mr Baker, who requires ongoing review following a fractured wrist.\n\n"
            + "Mr Baker was admitted with a fractured wrist and is now recovering well.\n\n"
            + "I would be grateful if you could review Mr Baker's wrist.\n\n"
            + "Should there be any queries, kindly do not hesitate to contact me.\n\n"
            + "Yours sincerely,\n\n"
            + "Registered Nurse";
    }

    private static IReadOnlyList<LintFinding> Lint(
        string letter, LetterDateAnchor anchor, bool modelAnswer = true, string? todayDate = null)
        => new WritingRuleEngine(new RulebookLoader()).Lint(new WritingLintInput(
            LetterText: letter,
            LetterType: "LT-RR",
            Profession: ExamProfession.Nursing,
            IsModelAnswer: modelAnswer,
            TodayDate: todayDate,
            DateAnchor: anchor));

    private static bool MissingDate(IReadOnlyList<LintFinding> f)
        => f.Any(x => x.RuleId == Structure && x.Message.Contains("Date", StringComparison.Ordinal));

    private static bool MissingRecipientBlock(IReadOnlyList<LintFinding> f)
        => f.Any(x => x.RuleId == Layout && x.Message.Contains("recipient's name/address block", StringComparison.Ordinal));

    // ---- a source with no date at all -------------------------------------------------------

    [Fact]
    public void NoDateInSource_ModelAnswerMayOmitTheDate()
    {
        var f = Lint(Letter(null), LetterDateAnchor.None);
        Assert.False(MissingDate(f));
        Assert.False(MissingRecipientBlock(f));
        Assert.DoesNotContain(f, x => x.RuleId == DateRule);
    }

    [Theory]
    [InlineData("3 September 2026")]
    [InlineData("1 January 2020")]
    [InlineData("29 February 2044")]
    public void NoDateInSource_ModelAnswerWithAnyDateIsUnverifiable(string dateLine)
    {
        // The three dates that all passed silently before.
        var f = Lint(Letter(dateLine), LetterDateAnchor.None);
        var hit = Assert.Single(f, x => x.RuleId == DateRule);
        Assert.Equal(RuleSeverity.Critical, hit.Severity);
        Assert.Contains("cannot be verified", hit.Message);
        Assert.Equal(dateLine, hit.Quote);
    }

    [Fact]
    public void NoDateInSource_MonthYearLineIsJustAsUnverifiable()
    {
        var f = Lint(Letter("February 2018"), LetterDateAnchor.None);
        var hit = Assert.Single(f, x => x.RuleId == DateRule);
        Assert.Contains("cannot be verified", hit.Message);
    }

    [Fact]
    public void NoDateInSource_TheRecipientBlockIsStillRequired()
    {
        var f = Lint(Letter(null, recipientBlock: false), LetterDateAnchor.None);
        Assert.True(MissingRecipientBlock(f));
    }

    // ---- a source that gives only a month and a year ----------------------------------------

    [Fact]
    public void MonthOnly_ModelAnswerMayStateMonthAndYear()
    {
        var f = Lint(Letter("February 2018"), LetterDateAnchor.MonthOnly);
        Assert.DoesNotContain(f, x => x.RuleId == DateRule);
        Assert.False(MissingDate(f));
        Assert.False(MissingRecipientBlock(f));
    }

    [Fact]
    public void MonthOnly_ModelAnswerMayOmitTheDate()
    {
        var f = Lint(Letter(null), LetterDateAnchor.MonthOnly);
        Assert.DoesNotContain(f, x => x.RuleId == DateRule);
        Assert.False(MissingDate(f));
        Assert.False(MissingRecipientBlock(f));
    }

    [Fact]
    public void MonthOnly_ModelAnswerMustNotInventTheDay()
    {
        var f = Lint(Letter("28 February 2018"), LetterDateAnchor.MonthOnly);
        var hit = Assert.Single(f, x => x.RuleId == DateRule);
        Assert.Equal(RuleSeverity.Critical, hit.Severity);
        Assert.Contains("cannot state a day", hit.Message);
    }

    [Fact]
    public void MonthOnly_AMonthYearLineIsTheDateNotTheAddressBlock()
    {
        // No recipient block, only a Month-Year line before the salutation: still a missing block.
        var f = Lint("February 2018\n\n" + Letter(null, recipientBlock: false), LetterDateAnchor.MonthOnly);
        Assert.True(MissingRecipientBlock(f));
    }

    // ---- what must NOT change ----------------------------------------------------------------

    [Fact]
    public void UnknownAnchor_KeepsTheDateRequired_ExactlyAsBefore()
    {
        var f = Lint(Letter(null), LetterDateAnchor.Unknown);
        Assert.True(MissingDate(f));
        Assert.True(MissingRecipientBlock(f));
    }

    [Fact]
    public void DayAnchor_KeepsTheDateRequired()
    {
        var f = Lint(Letter(null), LetterDateAnchor.Day, todayDate: "3 November 2019");
        Assert.True(MissingDate(f));
    }

    [Fact]
    public void DayAnchor_ADatedLetterIsNotFlaggedUnverifiable()
    {
        var f = Lint(Letter("3 November 2019"), LetterDateAnchor.Day, todayDate: "3 November 2019");
        Assert.DoesNotContain(f, x => x.RuleId == DateRule);
    }

    // ---- candidates ---------------------------------------------------------------------------

    [Fact]
    public void Candidate_IsNotPenalisedForOmittingADateTheSourceCannotSupport()
    {
        Assert.False(MissingDate(Lint(Letter(null), LetterDateAnchor.None, modelAnswer: false)));
        Assert.False(MissingDate(Lint(Letter(null), LetterDateAnchor.MonthOnly, modelAnswer: false)));
    }

    [Theory]
    [InlineData("3 September 2026", LetterDateAnchor.None)]
    [InlineData("28 February 2018", LetterDateAnchor.MonthOnly)]
    public void Candidate_IsNotPenalisedForAReasonableAssumedDate(string dateLine, LetterDateAnchor anchor)
    {
        var f = Lint(Letter(dateLine), anchor, modelAnswer: false);
        Assert.DoesNotContain(f, x => x.RuleId == DateRule);
    }

    [Fact]
    public void Candidate_WithAnUnclassifiedTask_StillNeedsTheDate()
    {
        // A caller that could not classify must never switch the requirement off.
        Assert.True(MissingDate(Lint(Letter(null), LetterDateAnchor.Unknown, modelAnswer: false)));
    }

    // ---- the classifier ------------------------------------------------------------------------

    [Fact]
    public void Classify_WithNoInformationAtAll_IsUnknownNeverNone()
        => Assert.Equal(LetterDateAnchor.Unknown, WritingRuleEngine.ClassifyDateAnchor(null, null, null));

    [Fact]
    public void Classify_ATodayDateIsADayAnchor()
        => Assert.Equal(LetterDateAnchor.Day, WritingRuleEngine.ClassifyDateAnchor("1 March 2010", "", null));

    [Theory]
    [InlineData("Ms Stokes presented at the pharmacy today. DOB: 24.09.1937 (Age 77).", LetterDateAnchor.None)]
    [InlineData("Mr Baker was born in March 1990 and lives alone.", LetterDateAnchor.None)]
    [InlineData("First attended centre: March 2015. Last visit to community centre: February 2018.", LetterDateAnchor.MonthOnly)]
    [InlineData("She was discharged on 28 April 2011.", LetterDateAnchor.Day)]
    [InlineData("Assume that today's date is 15 May 2021.", LetterDateAnchor.Day)]
    public void Classify_ReadsHowMuchDateTheNotesGive(string notes, LetterDateAnchor expected)
        => Assert.Equal(expected, WritingRuleEngine.ClassifyDateAnchor(null, notes, null));
}
