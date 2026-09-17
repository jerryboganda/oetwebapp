using System.Text.RegularExpressions;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Senior Assessor Release Audit (16 Sep 2026), group G6: chronology and
/// owner-required facts, proved on the canonical fixture letters with
/// production-shaped case notes.
/// - G6a: the letter date must equal an explicit today's date (notes/task
///   "Today's Date", scenario TodayDate); numeric note dates are read.
/// - G6b/G6d: the letter date may not pre-date the latest documented
///   encounter (Betty Johnson 25 February vs 21/03/15; Weston 10 June vs
///   17.06.2018; Sarah Day 4 January vs 24.02.18), and the numeric ceiling
///   catches an invented later date (Weston 20 June 2018; the canonical
///   fixture now carries the source date 17 June 2018).
/// - G6b-body/G6c (narrated_chronology_contradiction): a past event dated after
///   the letter date, "on today and presented four days later", "her last
///   period of today".
/// - G6e (owner_required_fact_missing): Weir must keep 88/70 mmHg.
/// Every new branch is Model Answer only; candidate letters are never flagged.
/// </summary>
public sealed class WritingSeniorAuditG6RegressionTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());

    private const string LetterDate = "letter_date_unsupported";
    private const string Chronology = "narrated_chronology_contradiction";
    private const string OwnerFact = "owner_required_fact_missing";

    private static List<LintFinding> Lint(string letter, string letterType,
        string? caseNotes = null, string? taskText = null, bool isModelAnswer = true, string? todayDate = null,
        ExamProfession profession = ExamProfession.Medicine)
        => Engine.Lint(new WritingLintInput(
            LetterText: letter,
            LetterType: letterType,
            Profession: profession,
            IsModelAnswer: isModelAnswer,
            CaseNotesText: caseNotes,
            TaskText: taskText,
            TodayDate: todayDate)).ToList();

    private static void AssertRuleFires(List<LintFinding> findings, string checkId)
        => Assert.Contains(findings, f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal));

    private static void AssertRuleDoesNotFire(List<LintFinding> findings, string checkId)
        => Assert.DoesNotContain(findings, f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal));

    private static void AssertRuleFiresWithFix(List<LintFinding> findings, string checkId, string fix)
        => Assert.Contains(findings, f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal) && f.FixSuggestion == fix);

    private static string Inject(string letter, string find, string replace)
    {
        var injected = letter.Replace(find, replace);
        Assert.NotEqual(letter, injected);
        return injected;
    }

    // Re-dates a fixture through its whole-line date, whatever date the
    // fixture currently carries (the Weston fixture date is being reconciled
    // to the source), so these tests never depend on the fixture's own date.
    private static readonly Regex DateLineRe = new(@"^\d{1,2} [A-Z][a-z]+ \d{4}(?=\r?$)", RegexOptions.Multiline);

    private static string ReDate(string letter, string date)
    {
        Assert.Matches(DateLineRe, letter);
        return DateLineRe.Replace(letter, date, 1);
    }

    private const string Garcia = WritingRev8RegressionFixtureTests.GarciaUpdateLetter;        // LT-DG
    private const string Weston = WritingRev8RegressionFixtureTests.WestonReferralLetter;      // LT-NM
    private const string Weir = WritingRev8RegressionFixtureTests.WeirRoutineReferralLetter;   // LT-RR
    private const string McDonald = WritingRev8RegressionFixtureTests.McDonaldTransferLetter;  // LT-TR
    private const string Taylor = WritingRev8RegressionFixtureTests.TaylorUrgentReferralLetter;// LT-UR

    // ─── Production-shaped case notes (one structured sentence per line, as
    // WritingTaskModelAnswerService joins them) ────

    private const string JohnsonNotes =
        "Ms Betty Johnson is an 81-year-old woman who recently had a right total knee replacement on 25/02/2015 and is being discharged today\n" +
        "Warfarin 5mg was restarted on 26/02/15 for ongoing anticoagulation, alongside s/c Clexane 80mg\n" +
        "Her wound remained clean between 03-05/03/15 with good mobility, and clips were gradually removed\n" +
        "Remaining clips were removed and pathology was clear on 06/03/15, and she was transferred to rehabilitation\n" +
        "Between 15-19/03/15 her progress was uneventful, with gradually increasing independence\n" +
        "On 21/03/15 there were no cardiac issues noted\n" +
        "A rehabilitation appointment has been arranged in 2 weeks";

    private const string WestonProductionNotes =
        "Patient is Betty Weston, DOB 12.2.64 (55 years)\n" +
        "Outpatient clinic appointment on 10.06.2018\n" +
        "Presenting problem: numbness/tingling in the thumb, index and middle finger of the right hand, of 3 weeks' duration\n" +
        "17.06.2018 review of symptoms: fatigue, cold intolerance, constipation\n" +
        "Discharge plan: conservative management with night-time wrist splinting for more than 3 weeks";

    private const string SarahDayNotes =
        "Patient is Ms Sarah Day, DOB 29.07.1997, 20 years old\n" +
        "06.12.17: patient presented with her mother, complaining of unilateral headache\n" +
        "04.01.18: attacks more frequent\n" +
        "31.01.18: patient complained of drowsiness and diarrhoea since starting eletriptan\n" +
        "24.02.18: patient presented alone";

    private const string MacIntyreReconciledNotes =
        "Today's Date: 24/08/19\n" +
        "Mrs Jane MacIntyre, DOB 01/03/80, has a history of pre-eclampsia.\n" +
        "Her last menstrual period was 26/06/19.\n" +
        "She has a positive home pregnancy test for her fifth pregnancy and thinks she is 8 weeks pregnant.";

    private const string WeirNotes =
        "Mr Michael Weir is a patient in your general practice, height 183cm.\n" +
        "On 29.06.14 he presented for a general check-up, reporting feeling run down: tired, stressed and sluggish.\n" +
        "On 09.08.14 he complained of dizziness and two recent blackouts lasting a few minutes each.\n" +
        "Examination on 09.08.14: BP 88/70, HR 76bpm, BMI 28 (93.7kg), chest clear.";

    private const string GarciaNotes =
        "Patient was referred to the Emergency Department by her GP, Dr Bradbury. " +
        "Date of birth 01.01.1995 (age 20). " +
        "Presented on 23 May 2015 with painful, stiff joints for one week.";

    private const string TaylorNotes =
        "Mr David Taylor, DOB 01/08/1965 (age 55).\n" +
        "Severe gout attack on 01/06/2010, treated with steroid injection, colchicine and allopurinol.\n" +
        "On 13/06/2020 presented with pain in his right big toe and swelling of the right foot and big toe.";

    private const string McDonaldNotes =
        "DOB: 12/1/50\n" +
        "Admission date 20/7/18 for elective left total knee joint replacement (TKJR)\n" +
        "Transfer date 24/7/18\n" +
        "Treatment plan: specialist appointment at 6 weeks, made for 7/9/18";

    // ─── G6a — letter date vs an explicit today's date ────

    [Fact]
    public void SA_G6_ExplicitToday_Letter_Dated_On_The_Lmp_Fires_Against_The_Notes_Todays_Date()
    {
        // Mrs Jane MacIntyre: the letter was dated on the LMP (26 June 2019)
        // while the source states Today's Date 24/08/19.
        var letter = ReDate(Weston, "26 June 2019");
        AssertRuleFiresWithFix(Lint(letter, "LT-RR", caseNotes: MacIntyreReconciledNotes), LetterDate, "24 August 2019");
    }

    [Fact]
    public void SA_G6_ExplicitToday_Scenario_TodayDate_Mismatch_Fires()
    {
        // Ms Louise Geller: TodayDate 6 January 2024, letter 6 September 2026.
        var letter = ReDate(Weir, "6 September 2026");
        AssertRuleFiresWithFix(Lint(letter, "LT-RR", todayDate: "6 January 2024"), LetterDate, "6 January 2024");
    }

    [Fact]
    public void SA_G6_ExplicitToday_Task_Assume_Todays_Date_Mismatch_Fires()
    {
        const string task = "Assume that today's date is 7 January 2023.\nUsing the information in the case notes, write a letter to Dr Jason Imre.";
        AssertRuleFiresWithFix(Lint(ReDate(Weir, "8 January 2023"), "LT-RR", taskText: task), LetterDate, "7 January 2023");
        AssertRuleDoesNotFire(Lint(ReDate(Weir, "7 January 2023"), "LT-RR", taskText: task), LetterDate);
    }

    [Fact]
    public void SA_G6_ExplicitToday_Letter_On_The_Stated_Day_Passes()
    {
        AssertRuleDoesNotFire(Lint(ReDate(Weston, "24 August 2019"), "LT-RR", caseNotes: MacIntyreReconciledNotes), LetterDate);
        AssertRuleDoesNotFire(Lint(ReDate(Weir, "6 January 2024"), "LT-RR", todayDate: "6 January 2024"), LetterDate);
    }

    [Fact]
    public void SA_G6_ExplicitToday_Inline_Today_Visit_Marker_Is_Not_A_Label()
    {
        // Mr George Poulos: "Today (21/06/14) ..." is a visit marker, and the
        // latest encounter 05/07/14 equals the letter date.
        const string notes =
            "21/06/14: severe lower back pain of 2 days' duration.\n" +
            "Today (21/06/14) pain is again less severe.\n" +
            "05/07/14: pain is worse; patient almost immobile with severe pain down the right leg.";
        AssertRuleDoesNotFire(Lint(ReDate(Weir, "5 July 2014"), "LT-RR", caseNotes: notes), LetterDate);
    }

    [Fact]
    public void SA_G6_ExplicitToday_Time_Beside_A_Date_Is_Not_A_Date()
    {
        // Ms Sally McConville: "13/9/14, 10.30am" — the time never parses.
        const string notes = "13/9/14, 10.30am: more short of breath despite prednisolone and antibiotics; feeling feverish and unwell.";
        AssertRuleDoesNotFire(Lint(ReDate(Weir, "13 September 2014"), "LT-RR", caseNotes: notes), LetterDate);
    }

    [Fact]
    public void SA_G6_ExplicitToday_Conflicting_Stated_Dates_Prove_Nothing()
    {
        var letter = ReDate(Weston, "26 June 2019");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR", caseNotes: MacIntyreReconciledNotes, todayDate: "25 August 2019"), LetterDate);
    }

    [Fact]
    public void SA_G6_ExplicitToday_Is_Model_Answer_Only()
    {
        AssertRuleDoesNotFire(Lint(ReDate(Weston, "26 June 2019"), "LT-RR", caseNotes: MacIntyreReconciledNotes, isModelAnswer: false), LetterDate);
        AssertRuleDoesNotFire(Lint(ReDate(Weir, "6 September 2026"), "LT-RR", todayDate: "6 January 2024", isModelAnswer: false), LetterDate);
    }

    // ─── G6b — letter date before a documented encounter (floor) ────

    [Fact]
    public void SA_G6_EncounterFloor_Betty_Johnson_Letter_Before_Discharge_From_Rehab_Fires()
    {
        var letter = ReDate(Garcia, "25 February 2015");
        AssertRuleFiresWithFix(Lint(letter, "LT-DG", caseNotes: JohnsonNotes), LetterDate, "21 March 2015");
    }

    [Fact]
    public void SA_G6_EncounterFloor_Letter_On_The_Latest_Encounter_Passes()
        => AssertRuleDoesNotFire(Lint(ReDate(Garcia, "21 March 2015"), "LT-DG", caseNotes: JohnsonNotes), LetterDate);

    [Fact]
    public void SA_G6_EncounterFloor_Out_Of_Sequence_Source_Typo_Never_Sets_The_Floor()
    {
        // Maria Sorocco: "17/08/2019" precedes "25/02/2019" in the notes.
        const string notes =
            "Patient is Ms Maria Sorocco, DOB 15/08/1943\n" +
            "17/08/2019: recurrence of the right leg ulcer\n" +
            "25/02/2019: ulcer healing, no swelling, no redness\n" +
            "04/06/2019: mild swelling and redness of the ulcer\n" +
            "15/06/2019: increased redness and swelling";
        AssertRuleDoesNotFire(Lint(ReDate(Weston, "15 June 2019"), "LT-NM", caseNotes: notes), LetterDate);
    }

    [Fact]
    public void SA_G6_EncounterFloor_Planned_Discharge_And_Due_Dose_Are_Not_Encounters()
    {
        // OET test 4 (Jack Mills).
        const string notes =
            "Patient is Jack Mills, DOB 01/09/1999, single.\n" +
            "23/11/19: presented irritable and suspicious.\n" +
            "Discharge planned for 27/12/19; father to collect him at 11am; will live with father in Canberra.\n" +
            "To continue sodium valproate 250mg twice daily and Navane 1.5mg IM every 4 weeks, next dose due 16/01/20.";
        AssertRuleDoesNotFire(Lint(ReDate(Weir, "27 December 2019"), "LT-RR", caseNotes: notes), LetterDate);
    }

    [Fact]
    public void SA_G6_EncounterFloor_Arranged_Appointment_Is_Not_An_Encounter()
    {
        // OET test 15 (Dulcie Woods).
        const string notes =
            "On 08/08/19 reported initial improvement, but then increased palpitations.\n" +
            "An appointment was arranged with Dr Raymond's receptionist for 8am on 14/08/19.";
        AssertRuleDoesNotFire(Lint(ReDate(Weston, "8 August 2019"), "LT-RR", caseNotes: notes), LetterDate);
    }

    [Fact]
    public void SA_G6_EncounterFloor_Transfer_Letter_With_Labelled_And_Future_Dates_Passes()
        => AssertRuleDoesNotFire(Lint(McDonald, "LT-TR", caseNotes: McDonaldNotes), LetterDate);

    [Fact]
    public void SA_G6_EncounterFloor_Is_Model_Answer_Only()
        => AssertRuleDoesNotFire(Lint(ReDate(Garcia, "25 February 2015"), "LT-DG", caseNotes: JohnsonNotes, isModelAnswer: false), LetterDate);

    // ─── G6d — the live post-audit date changes ────

    [Fact]
    public void SA_G6_PostAuditDates_Weston_Proven_Against_Production_Notes()
    {
        AssertRuleFiresWithFix(Lint(ReDate(Weston, "10 June 2018"), "LT-NM", caseNotes: WestonProductionNotes), LetterDate, "17 June 2018");
        Assert.Contains(Lint(ReDate(Weston, "20 June 2018"), "LT-NM", caseNotes: WestonProductionNotes),
            f => f.RuleId.EndsWith(LetterDate, StringComparison.Ordinal) && f.Message.Contains("later than every date", StringComparison.Ordinal));
        AssertRuleDoesNotFire(Lint(ReDate(Weston, "17 June 2018"), "LT-NM", caseNotes: WestonProductionNotes), LetterDate);
    }

    [Fact]
    public void SA_G6_PostAuditDates_Sarah_Day_Proven_Against_Production_Notes()
    {
        AssertRuleFiresWithFix(Lint(ReDate(Weston, "4 January 2018"), "LT-RR", caseNotes: SarahDayNotes), LetterDate, "24 February 2018");
        AssertRuleDoesNotFire(Lint(ReDate(Weston, "24 February 2018"), "LT-RR", caseNotes: SarahDayNotes), LetterDate);
    }

    [Fact]
    public void SA_G6_PostAuditDates_Candidate_Letter_Is_Not_Flagged()
        => AssertRuleDoesNotFire(Lint(ReDate(Weston, "20 June 2018"), "LT-NM", caseNotes: WestonProductionNotes, isModelAnswer: false), LetterDate);

    // ─── G6b-body — a past event dated after the letter date ────

    [Fact]
    public void SA_G6_BodyDateAfterLetter_Transfer_On_6_March_In_A_Letter_Dated_25_February_Fires()
    {
        var letter = Inject(ReDate(Garcia, "25 February 2015"),
            "She responded well to the treatment.",
            "She was transferred to rehabilitation on 6 March with a clean wound, progressing well with a frame and stick.");
        AssertRuleFires(Lint(letter, "LT-DG"), Chronology);
    }

    [Fact]
    public void SA_G6_BodyDateAfterLetter_Warfarin_Recommenced_On_26_February_Fires()
    {
        var letter = Inject(ReDate(Garcia, "25 February 2015"),
            "She responded well to the treatment.",
            "Post-operatively, her haemoglobin fell to 80 g/l, requiring a blood transfusion, and warfarin was recommenced on 26 February.");
        AssertRuleFires(Lint(letter, "LT-DG"), Chronology);
    }

    [Fact]
    public void SA_G6_BodyDateAfterLetter_Dated_Finding_With_The_Verb_After_The_Date_Fires()
    {
        // Evelyn Parker (Dietetics, wider corpus): "Dietetic assessment on 14
        // August found ..." in a letter dated 12 August 2026.
        var letter = Inject(ReDate(Garcia, "25 February 2015"),
            "She responded well to the treatment.",
            "Dietetic assessment on 27 February found a weight of 48.0 kg with a BMI of 18.3.");
        AssertRuleFires(Lint(letter, "LT-DG"), Chronology);
    }

    [Theory]
    [InlineData("25 February 2015", "Her review on 3 March was cancelled.")]
    [InlineData("25 February 2015", "Dietetic assessment on 20 February found a weight of 48.0 kg.")]
    [InlineData("25 February 2015", "She was advised to return for review on 3 March.")]
    [InlineData("25 February 2015", "She is due for review at the clinic on 3 March.")]
    [InlineData("25 February 2015", "She was discharged on 20 February 2015.")]
    [InlineData("25 February 2015", "She was transferred to rehabilitation on 6 March 2014 with a clean wound.")]
    [InlineData("1 January 2024", "She was reviewed on 20 November with minimal improvement.")]
    public void SA_G6_BodyDateAfterLetter_Valid_Alternatives_Pass(string letterDate, string sentence)
    {
        var letter = Inject(ReDate(Garcia, letterDate), "She responded well to the treatment.", sentence);
        AssertRuleDoesNotFire(Lint(letter, "LT-DG"), Chronology);
    }

    [Fact]
    public void SA_G6_BodyDateAfterLetter_Is_Model_Answer_Only()
    {
        var letter = Inject(ReDate(Garcia, "25 February 2015"),
            "She responded well to the treatment.",
            "She was transferred to rehabilitation on 6 March with a clean wound, progressing well with a frame and stick.");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG", isModelAnswer: false), Chronology);
    }

    // ─── G6c — contradictions anchored on "today" ────

    [Fact]
    public void SA_G6_TodayOffset_On_Today_And_Four_Days_Later_Fires()
    {
        // Mr Barry Jones, live and audited.
        var letter = Inject(Weir,
            "He continues to smoke.",
            "He hurt his back lifting a heavy box off the floor at work on today and presented four days later with worsening pain.");
        AssertRuleFires(Lint(letter, "LT-RR"), Chronology);
    }

    [Theory]
    [InlineData("He hurt his back lifting a heavy box at work on 17 March and presented today, four days later, with worsening pain.")]
    [InlineData("On today's review, he reported pain that began at work and worsened two days later.")]
    [InlineData("He presented today with pain that started at work and worsened two days later.")]
    public void SA_G6_TodayOffset_Valid_Alternatives_Pass(string sentence)
    {
        var letter = Inject(Weir, "He continues to smoke.", sentence);
        AssertRuleDoesNotFire(Lint(letter, "LT-RR"), Chronology);
    }

    [Fact]
    public void SA_G6_TodayOffset_Last_Period_Of_Today_Fires()
    {
        // Mrs Jane MacIntyre, live and audited.
        var letter = Inject(Garcia,
            "She responded well to the treatment.",
            "She now has a positive test for her fifth pregnancy, estimated at eight weeks by her last period of today.");
        AssertRuleFires(Lint(letter, "LT-DG"), Chronology);
    }

    [Theory]
    [InlineData("Her last menstrual period was on 26 June 2019, and she is estimated to be eight weeks pregnant.")]
    [InlineData("She now has a positive pregnancy test and is estimated to be eight weeks pregnant.")]
    [InlineData("Her last menstrual period was six weeks ago.")]
    public void SA_G6_TodayOffset_Lmp_Valid_Alternatives_Pass(string sentence)
    {
        var letter = Inject(Garcia, "She responded well to the treatment.", sentence);
        AssertRuleDoesNotFire(Lint(letter, "LT-DG"), Chronology);
    }

    [Fact]
    public void SA_G6_TodayOffset_Is_Model_Answer_Only()
    {
        var barry = Inject(Weir,
            "He continues to smoke.",
            "He hurt his back lifting a heavy box off the floor at work on today and presented four days later with worsening pain.");
        var macIntyre = Inject(Garcia,
            "She responded well to the treatment.",
            "She now has a positive test for her fifth pregnancy, estimated at eight weeks by her last period of today.");
        AssertRuleDoesNotFire(Lint(barry, "LT-RR", isModelAnswer: false), Chronology);
        AssertRuleDoesNotFire(Lint(macIntyre, "LT-DG", isModelAnswer: false), Chronology);
    }

    // ─── G6e — owner-required fact (OA2-14 Weir 88/70 mmHg) ────

    [Fact]
    public void SA_G6_OwnerRequiredFact_Weir_Letter_Without_88_70_mmHg_Fires()
    {
        var letter = Inject(Weir, "His blood pressure was 88/70 mmHg. ", "");
        AssertRuleFiresWithFix(Lint(letter, "LT-RR", caseNotes: WeirNotes), OwnerFact, "His blood pressure was 88/70 mmHg.");
    }

    [Fact]
    public void SA_G6_OwnerRequiredFact_Valid_Alternatives_Pass()
    {
        // The fixture sentence.
        AssertRuleDoesNotFire(Lint(Weir, "LT-RR", caseNotes: WeirNotes), OwnerFact);
        // Another head noun and sentence position.
        var variant = Inject(Weir,
            "His blood pressure was 88/70 mmHg.",
            "Mr Weir's blood pressure was 88/70 mmHg, and his heart rate was 76 bpm.");
        AssertRuleDoesNotFire(Lint(variant, "LT-RR", caseNotes: WeirNotes), OwnerFact);
        // Notes that do not carry the fact require nothing.
        var withoutBp = Inject(Weir, "His blood pressure was 88/70 mmHg. ", "");
        AssertRuleDoesNotFire(Lint(withoutBp, "LT-RR", caseNotes: "Mr Michael Weir is a patient in your general practice, height 183cm."), OwnerFact);
        // Another patient with the same reading has no owner requirement.
        const string otherPatient = "Mr David Taylor, DOB 01/08/1965 (age 55).\nOn 13/06/2020 presented with pain in his right big toe.\nObservations: BP 88/70, HR 90.";
        AssertRuleDoesNotFire(Lint(Taylor, "LT-UR", caseNotes: otherPatient), OwnerFact);
    }

    [Fact]
    public void SA_G6_OwnerRequiredFact_Is_Model_Answer_Only_And_Source_Gated()
    {
        var letter = Inject(Weir, "His blood pressure was 88/70 mmHg. ", "");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR", caseNotes: WeirNotes, isModelAnswer: false), OwnerFact);
        AssertRuleDoesNotFire(Lint(letter, "LT-RR"), OwnerFact);
    }

    // ─── Clean fixtures stay clean for every G6 check ────

    [Fact]
    public void SA_G6_CleanFixtures_Canonical_Medicine_Letters_Have_No_G6_Findings()
    {
        var runs = new List<List<LintFinding>>
        {
            Lint(Garcia, "LT-DG", caseNotes: GarciaNotes),
            Lint(Weston, "LT-NM"),
            Lint(ReDate(Weston, "17 June 2018"), "LT-NM", caseNotes: WestonProductionNotes),
            Lint(Weir, "LT-RR", caseNotes: WeirNotes),
            Lint(McDonald, "LT-TR", caseNotes: McDonaldNotes),
            Lint(Taylor, "LT-UR", caseNotes: TaylorNotes),
        };
        foreach (var findings in runs)
        {
            AssertRuleDoesNotFire(findings, LetterDate);
            AssertRuleDoesNotFire(findings, Chronology);
            AssertRuleDoesNotFire(findings, OwnerFact);
        }
    }

    [Fact]
    public void SA_G6_CleanFixtures_Ultimate_Final_And_Exemplar_Letters_Have_No_G6_Findings()
    {
        var exemplar = WritingModelAnswerBatchTests.ExemplarText();
        var exemplarNotes = string.Join("\n", WritingModelAnswerBatchTests.CaseNoteSentencesFor(exemplar));
        var runs = new List<List<LintFinding>>
        {
            Lint(WritingUltimateFinalRegressionFixtureTests.WeirRoutineReferralLetter, "LT-RR"),
            Lint(WritingUltimateFinalRegressionFixtureTests.GarciaDischargeLetter, "LT-DG"),
            Lint(WritingUltimateFinalRegressionFixtureTests.RamseyPharmacyLetter, "LT-OT", profession: ExamProfession.Pharmacy),
            Lint(WritingUltimateFinalRegressionFixtureTests.WrightKneeReferralLetter, "LT-RR", profession: ExamProfession.Physiotherapy),
            Lint(exemplar, "LT-RR", caseNotes: exemplarNotes),
        };
        foreach (var findings in runs)
        {
            AssertRuleDoesNotFire(findings, LetterDate);
            AssertRuleDoesNotFire(findings, Chronology);
            AssertRuleDoesNotFire(findings, OwnerFact);
        }
    }
}
