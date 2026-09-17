using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Owner Senior Assessor Release Audit (16 Sep 2026), group G2 regression
/// classes. Every class proves four things on the canonical fixture letters:
/// (a) the real audited defect shape, injected, fires in Model Answer mode;
/// (b) valid alternatives never fire; (c) the same defective letter in the
/// candidate lane does not fire the new branch (implementation decisions §B:
/// every G2 detector is Model Answer only); (d) the clean fixtures produce no
/// finding for any G2 check id. Every injection asserts its needle landed.
/// </summary>
public sealed class WritingSeniorAuditG2RegressionTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());

    private const string MalformedToday = "malformed_today_phrase";
    private const string MissingPossessive = "missing_possessive_name";
    private const string TypographicCorruption = "typographic_corruption";
    private const string IntroAdverbialComma = "intro_adverbial_comma";
    private const string NumberStyle = "number_style_words_vs_digits";
    private const string ValueUnitSpacing = "value_unit_spacing";
    private const string VitalUnits = "numerical_values_have_units";
    private const string ConditionsLowercase = "conditions_lowercase";

    private static readonly string[] G2CheckIds =
    {
        MalformedToday, MissingPossessive, TypographicCorruption, IntroAdverbialComma,
        NumberStyle, ValueUnitSpacing, VitalUnits, ConditionsLowercase,
    };

    private static List<LintFinding> Lint(string letter, string letterType, bool isModelAnswer = true,
        ExamProfession profession = ExamProfession.Medicine)
        => Engine.Lint(new WritingLintInput(
            LetterText: letter,
            LetterType: letterType,
            Profession: profession,
            IsModelAnswer: isModelAnswer)).ToList();

    private static void AssertRuleFires(List<LintFinding> findings, string checkId)
        => Assert.Contains(findings, f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal));

    private static void AssertRuleDoesNotFire(List<LintFinding> findings, string checkId)
        => Assert.DoesNotContain(findings, f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal));

    private static string Inject(string letter, string find, string replace)
    {
        var injected = letter.Replace(find, replace);
        Assert.NotEqual(letter, injected);
        return injected;
    }

    private static (string Letter, string LetterType, ExamProfession Profession) Fixture(string key) => key switch
    {
        "garcia" => (WritingRev8RegressionFixtureTests.GarciaUpdateLetter, "LT-DG", ExamProfession.Medicine),
        "weston" => (WritingRev8RegressionFixtureTests.WestonReferralLetter, "LT-NM", ExamProfession.Medicine),
        "weir" => (WritingRev8RegressionFixtureTests.WeirRoutineReferralLetter, "LT-RR", ExamProfession.Medicine),
        "mcdonald" => (WritingRev8RegressionFixtureTests.McDonaldTransferLetter, "LT-TR", ExamProfession.Medicine),
        "taylor" => (WritingRev8RegressionFixtureTests.TaylorUrgentReferralLetter, "LT-UR", ExamProfession.Medicine),
        "uf-weir" => (WritingUltimateFinalRegressionFixtureTests.WeirRoutineReferralLetter, "LT-RR", ExamProfession.Medicine),
        "uf-garcia" => (WritingUltimateFinalRegressionFixtureTests.GarciaDischargeLetter, "LT-DG", ExamProfession.Medicine),
        "uf-ramsey" => (WritingUltimateFinalRegressionFixtureTests.RamseyPharmacyLetter, "LT-OT", ExamProfession.Pharmacy),
        "uf-wright" => (WritingUltimateFinalRegressionFixtureTests.WrightKneeReferralLetter, "LT-RR", ExamProfession.Physiotherapy),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown G2 fixture key."),
    };

    private static List<LintFinding> LintInjected(string key, string find, string replace, bool isModelAnswer = true)
    {
        var (letter, letterType, profession) = Fixture(key);
        return Lint(Inject(letter, find, replace), letterType, isModelAnswer, profession);
    }

    // Stable needles in the canonical fixtures.
    private const string TaylorBreath = "He reported shortness of breath.";
    private const string WeirSmoke = "He continues to smoke.";

    // ─── (d) clean fixtures stay clean for every G2 check id ────

    public static TheoryData<string> CleanFixtures() => new()
    {
        "garcia", "weston", "weir", "mcdonald", "taylor", "uf-weir", "uf-garcia", "uf-ramsey", "uf-wright",
    };

    [Theory]
    [MemberData(nameof(CleanFixtures))]
    public void SA_G2_Clean_Fixtures_Produce_No_G2_Finding(string key)
    {
        // uf-garcia was repaired in this round: "On examination she was febrile"
        // (the audited missing-comma shape) now reads "On examination, she ...".
        var (letter, letterType, profession) = Fixture(key);
        var findings = Lint(letter, letterType, profession: profession);
        foreach (var checkId in G2CheckIds)
            AssertRuleDoesNotFire(findings, checkId);
    }

    // ─── G2-a — malformed "today" phrases (malformed_today_phrase) ────

    public static TheoryData<string, string, string> MalformedTodayDefects() => new()
    {
        { "taylor", "Today, Mr Taylor presented with pain", "Mr Taylor presented on today with pain" },
        { "taylor", TaylorBreath, "At review on today, he reported shortness of breath." },
        { "taylor", TaylorBreath, "On today, he reported shortness of breath." },
        { "taylor", TaylorBreath, "His symptoms, confirmed on today, included shortness of breath." },
        { "taylor", TaylorBreath, "When reviewed alone on today, he reported shortness of breath." },
        { "taylor", TaylorBreath, "By today, his breathing had become laboured." },
        { "taylor", TaylorBreath, "He has been mobilising in a wheelchair since today, with shortness of breath." },
        { "taylor", TaylorBreath, "He reported shortness of breath at the visit of today." },
    };

    [Theory]
    [MemberData(nameof(MalformedTodayDefects))]
    public void SA_G2_MalformedToday_Audited_Shapes_Fire_In_Model_Answer(string key, string find, string replace)
        => AssertRuleFires(LintInjected(key, find, replace), MalformedToday);

    [Theory]
    [MemberData(nameof(MalformedTodayDefects))]
    public void SA_G2_MalformedToday_Is_Silent_In_The_Candidate_Lane(string key, string find, string replace)
        => AssertRuleDoesNotFire(LintInjected(key, find, replace, isModelAnswer: false), MalformedToday);

    [Theory]
    [InlineData("At today's review, he reported shortness of breath.")]
    [InlineData("At review today, he reported shortness of breath.")]
    [InlineData("On examination today, he had shortness of breath.")]
    [InlineData("As of today, he reports shortness of breath.")]
    [InlineData("He is to continue salbutamol from today for five days.")]
    [InlineData("He will be reviewed later on today.")]
    public void SA_G2_MalformedToday_Valid_Today_Forms_Do_Not_Fire(string replace)
        => AssertRuleDoesNotFire(LintInjected("taylor", TaylorBreath, replace), MalformedToday);

    // ─── G2-b — missing possessive after a patient name (missing_possessive_name) ────

    public static TheoryData<string, string, string> MissingPossessiveDefects() => new()
    {
        { "weston", "She is aware that her weight, with a body mass index", "She is aware that Mrs Weston weight, with a body mass index" },
        { "weir", "Mr Weir has depression and has taken sertraline hydrochloride since September 2012.", "Mr Weir depression has been treated with sertraline hydrochloride since September 2012." },
        { "taylor", "His brother has gout, and his father died of kidney failure.", "Mr Taylor brother has gout, and his father died of kidney failure." },
        { "weston", "Mrs Weston's carpal tunnel syndrome has been managed conservatively", "Mrs Weston right wrist symptoms have been managed conservatively" },
        { "garcia", "Ms Garcia received dexamethasone", "In view of Ms Garcia history, she received dexamethasone" },
        // First-name branch (audited "Erika fasting sugars"): Re: Mr Michael Weir.
        { "weir", WeirSmoke, "Michael fasting sugars remain above 16." },
    };

    [Theory]
    [MemberData(nameof(MissingPossessiveDefects))]
    public void SA_G2_MissingPossessive_Audited_Shapes_Fire_In_Model_Answer(string key, string find, string replace)
        => AssertRuleFires(LintInjected(key, find, replace), MissingPossessive);

    [Theory]
    [MemberData(nameof(MissingPossessiveDefects))]
    public void SA_G2_MissingPossessive_Is_Silent_In_The_Candidate_Lane(string key, string find, string replace)
        => AssertRuleDoesNotFire(LintInjected(key, find, replace, isModelAnswer: false), MissingPossessive);

    [Theory]
    [InlineData("Mr Weir's wife reports that he continues to smoke.")]
    [InlineData("Mr Weir smokes and is allergic to penicillin.")]
    [InlineData("Mr Weir lives at home with his wife.")]
    [InlineData("His wife helps Mr Weir care for the garden.")]
    [InlineData("Mr Weir left work early to attend the review.")]
    [InlineData("Mr Weir first noticed the weakness in June 2014.")]
    [InlineData("Please review Mr Weir within one week.")]
    public void SA_G2_MissingPossessive_Valid_Name_Constructions_Do_Not_Fire(string replace)
        => AssertRuleDoesNotFire(LintInjected("weir", WeirSmoke, replace), MissingPossessive);

    // ─── G2-c — introductory adverbial comma (intro_adverbial_comma extension) ────

    public static TheoryData<string, string, string> IntroAdverbialDefects() => new()
    {
        { "taylor", TaylorBreath, "On examination he reported shortness of breath." },
        { "taylor", TaylorBreath, "On admission Mr Taylor reported shortness of breath." },
        { "taylor", TaylorBreath, "The following morning Mr Taylor reported shortness of breath." },
        { "taylor", TaylorBreath, "On 12 June 2020 Mr Taylor reported shortness of breath." },
        { "taylor", TaylorBreath, "In June 2020 he reported shortness of breath." },
        { "taylor", TaylorBreath, "Over the past three weeks he has reported shortness of breath." },
        { "taylor", TaylorBreath, "Overnight he reported shortness of breath." },
        { "taylor", TaylorBreath, "At review on 12 June 2020 he reported shortness of breath." },
        { "taylor", TaylorBreath, "Today he reported shortness of breath." },
    };

    [Theory]
    [MemberData(nameof(IntroAdverbialDefects))]
    public void SA_G2_IntroAdverbialComma_Audited_Shapes_Fire_In_Model_Answer(string key, string find, string replace)
        => AssertRuleFires(LintInjected(key, find, replace), IntroAdverbialComma);

    [Theory]
    [MemberData(nameof(IntroAdverbialDefects))]
    public void SA_G2_IntroAdverbialComma_Is_Silent_In_The_Candidate_Lane(string key, string find, string replace)
        => AssertRuleDoesNotFire(LintInjected(key, find, replace, isModelAnswer: false), IntroAdverbialComma);

    [Theory]
    [InlineData("On examination, he reported shortness of breath.")]
    [InlineData("On examination today, he reported shortness of breath.")]
    [InlineData("On review in June 2020, he reported shortness of breath.")]
    [InlineData("His shortness of breath initially improved.")]
    [InlineData("Initial treatment did not relieve his shortness of breath.")]
    [InlineData("In June 2020, he reported shortness of breath.")]
    [InlineData("Over the following months, he reported shortness of breath.")]
    [InlineData("In 2011 and 2012 he reported shortness of breath.")]
    public void SA_G2_IntroAdverbialComma_Valid_Forms_Do_Not_Fire(string replace)
        => AssertRuleDoesNotFire(LintInjected("taylor", TaylorBreath, replace), IntroAdverbialComma);

    [Fact]
    public void SA_G2_IntroAdverbialComma_Legacy_Capitalised_Shape_Is_Reported_Once()
    {
        var findings = LintInjected("taylor", "Today, Mr Taylor presented", "Today Mr Taylor presented");
        Assert.Single(findings, f => f.RuleId.EndsWith(IntroAdverbialComma, StringComparison.Ordinal));
    }

    // ─── G2-d — descriptive number style (number_style_words_vs_digits extension) ────

    private const string McDonaldHabits = "He smokes twenty cigarettes daily and drinks six to ten standard drinks daily.";
    private const string WestonSleep = "Her sleep was disturbed by pain, which was relieved by moving her fingers.";

    public static TheoryData<string, string, string> NumberStyleDefects() => new()
    {
        { "mcdonald", McDonaldHabits, "He smokes 30 to thirty-five cigarettes daily and drinks six to ten standard drinks daily." },
        { "mcdonald", McDonaldHabits, "He smokes 5-six cigarettes daily and drinks six to ten standard drinks daily." },
        { "mcdonald", McDonaldHabits, "He smokes twenty cigarettes daily and drinks 40 units of alcohol weekly." },
        { "mcdonald", McDonaldHabits, "He has a smoking history of 35 years and drinks six to ten standard drinks daily." },
        { "weston", WestonSleep, "Her sleep was disturbed by pain lasting less than 15 minutes." },
        { "weston", WestonSleep, "Her sleep was disturbed by pain after 20-30 minutes of lying down." },
        { "taylor", TaylorBreath, "He reported shortness of breath and was given 30 days off work." },
        { "taylor", "His brother has gout, and his father died of kidney failure.", "His brother has gout, and his father died of kidney failure at 53." },
        { "taylor", TaylorBreath, "He had an appendectomy at 15." },
    };

    [Theory]
    [MemberData(nameof(NumberStyleDefects))]
    public void SA_G2_NumberStyle_Audited_Shapes_Fire_In_Model_Answer(string key, string find, string replace)
        => AssertRuleFires(LintInjected(key, find, replace), NumberStyle);

    [Theory]
    [MemberData(nameof(NumberStyleDefects))]
    public void SA_G2_NumberStyle_New_Shapes_Are_Silent_In_The_Candidate_Lane(string key, string find, string replace)
        => AssertRuleDoesNotFire(LintInjected(key, find, replace, isModelAnswer: false), NumberStyle);

    [Theory]
    [InlineData("mcdonald", McDonaldHabits, "He smokes twelve to fourteen cigarettes daily and drinks fourteen units of alcohol weekly.")]
    [InlineData("taylor", "His brother has gout, and his father died of kidney failure.", "His brother has gout, and his father died of kidney failure at age fifty-three.")]
    [InlineData("weston", "She has a background of", "She had an emergency Caesarean section at 32 weeks and has a background of")]
    [InlineData("weir", WeirSmoke, "He is married with three children aged 13, 10 and 8.")]
    [InlineData("weir", WeirSmoke, "He takes insulin, 50 units twice daily.")]
    [InlineData("weston", WestonSleep, "She reported weight loss of 1 to 2 kg.")]
    public void SA_G2_NumberStyle_Valid_Numbers_Do_Not_Fire(string key, string find, string replace)
        => AssertRuleDoesNotFire(LintInjected(key, find, replace), NumberStyle);

    // ─── G2-e + G2-f2 — time and exponent notation (value_unit_spacing extension) ────

    public static TheoryData<string, string, string> ValueUnitSpacingDefects() => new()
    {
        { "taylor", TaylorBreath, "He reported shortness of breath and was reviewed again at 8am." },
        { "taylor", TaylorBreath, "He reported shortness of breath and was reviewed again at 3pm." },
        { "taylor", TaylorBreath, "He reported shortness of breath and was reviewed again at 1.30pm." },
        { "weston", "32 kg/m²", "32 kg/m2" },
        { "taylor", TaylorBreath, "He reported shortness of breath. The white cell count was 18,000 cells per mm3." },
    };

    [Theory]
    [MemberData(nameof(ValueUnitSpacingDefects))]
    public void SA_G2_ValueUnitSpacing_Audited_Shapes_Fire_In_Model_Answer(string key, string find, string replace)
        => AssertRuleFires(LintInjected(key, find, replace), ValueUnitSpacing);

    [Theory]
    [MemberData(nameof(ValueUnitSpacingDefects))]
    public void SA_G2_ValueUnitSpacing_New_Shapes_Are_Silent_In_The_Candidate_Lane(string key, string find, string replace)
        => AssertRuleDoesNotFire(LintInjected(key, find, replace, isModelAnswer: false), ValueUnitSpacing);

    [Theory]
    [InlineData("He reported shortness of breath and was reviewed again at 8 am.")]
    [InlineData("Ultrasound demonstrated an 18 x 16 mm nodule.")]
    [InlineData("A peak flow of 500 L/min was recorded.")]
    [InlineData("He takes ramipril, 2 mg daily and amlodipine, 5 mg daily.")]
    public void SA_G2_ValueUnitSpacing_Valid_Notation_Does_Not_Fire(string replace)
        => AssertRuleDoesNotFire(LintInjected("taylor", TaylorBreath, replace), ValueUnitSpacing);

    // ─── G2-f1 — vital-sign units (numerical_values_have_units extension) ────

    public static TheoryData<string, string, string> VitalUnitDefects() => new()
    {
        // Audited bare blood-pressure ratio ("blood pressure 148/98").
        { "taylor", "blood pressure 120/80 mmHg, heart rate 90 bpm and respiratory rate 22 breaths/min", "blood pressure 120/80, heart rate 90 bpm and respiratory rate 22 breaths/min" },
        // Audited "Pulse 96, blood pressure 110/70" — each borrowed the next vital's ratio.
        { "taylor", TaylorBreath, "He reported shortness of breath. Pulse 96, blood pressure 110/70." },
        // Audited "respiratory rate 16, pulse 80 bpm".
        { "taylor", "heart rate 90 bpm and respiratory rate 22 breaths/min", "respiratory rate 22 and heart rate 90 bpm" },
    };

    [Theory]
    [MemberData(nameof(VitalUnitDefects))]
    public void SA_G2_VitalUnits_Audited_Shapes_Fire_In_Model_Answer(string key, string find, string replace)
        => AssertRuleFires(LintInjected(key, find, replace), VitalUnits);

    [Theory]
    [MemberData(nameof(VitalUnitDefects))]
    public void SA_G2_VitalUnits_New_Shapes_Are_Silent_In_The_Candidate_Lane(string key, string find, string replace)
        => AssertRuleDoesNotFire(LintInjected(key, find, replace, isModelAnswer: false), VitalUnits);

    [Theory]
    [InlineData("His temperature was 38 degrees Celsius and his pulse was 88 bpm.")]
    [InlineData("His blood pressure has since returned to normal range at home.")]
    [InlineData("His blood pressure and electrolytes are to be monitored for one week.")]
    [InlineData("Examination showed visual acuity reduced to 6/9.")]
    [InlineData("His blood pressure was 88/70 mmHg.")]
    public void SA_G2_VitalUnits_Valid_Vital_Wording_Does_Not_Fire(string replace)
        => AssertRuleDoesNotFire(LintInjected("taylor", TaylorBreath, replace), VitalUnits);

    // ─── G2-g — generic drug capitalised mid-sentence (conditions_lowercase extension) ────

    public static TheoryData<string, string, string> GenericDrugDefects() => new()
    {
        { "taylor", "treated with colchicine, 1 mg, and NSAIDs", "treated with Colchicine, 1 mg, and NSAIDs" },
        { "mcdonald", "Analgesia comprises paracetamol, 1 g four times daily", "Analgesia comprises Paracetamol, 1 g four times daily" },
        { "weir", "has taken sertraline hydrochloride", "has taken Sertraline hydrochloride" },
        { "garcia", "before ceftriaxone, 2 g IV twice daily", "before Ceftriaxone, 2 g IV twice daily" },
    };

    [Theory]
    [MemberData(nameof(GenericDrugDefects))]
    public void SA_G2_GenericDrug_Audited_Shapes_Fire_In_Model_Answer(string key, string find, string replace)
        => AssertRuleFires(LintInjected(key, find, replace), ConditionsLowercase);

    [Theory]
    [MemberData(nameof(GenericDrugDefects))]
    public void SA_G2_GenericDrug_Is_Silent_In_The_Candidate_Lane(string key, string find, string replace)
        => AssertRuleDoesNotFire(LintInjected(key, find, replace, isModelAnswer: false), ConditionsLowercase);

    [Theory]
    [InlineData("mcdonald", "Analgesia comprises paracetamol, 1 g four times daily", "Analgesia comprises Paracetamol Osteo, 665 mg every eight hours")]
    [InlineData("taylor", TaylorBreath, "Metoclopramide, 10 mg, was prescribed for nausea.")]
    [InlineData("taylor", "treated with colchicine, 1 mg, and NSAIDs", "treated with Voltaren, 50 mg, and NSAIDs")]
    public void SA_G2_GenericDrug_Sentence_Initial_And_Trade_Names_Do_Not_Fire(string key, string find, string replace)
        => AssertRuleDoesNotFire(LintInjected(key, find, replace), ConditionsLowercase);

    // ─── G2-h — typographic corruption (typographic_corruption) ────

    public static TheoryData<string, string, string> TypographicDefects() => new()
    {
        // Audited "medial aspect . However" / "hypertensive ."
        { "taylor", TaylorBreath, "He reported shortness of breath ." },
        { "taylor", TaylorBreath, "He reported shortness of of breath." },
        // Audited "diabetes mellitus mellitus".
        { "weston", "type two diabetes mellitus", "type two diabetes mellitus mellitus" },
        { "taylor", TaylorBreath, "He reported shortness of breath and and chest pain." },
        // Audited "symptoms. today, Ms Day presented".
        { "taylor", TaylorBreath, "he reported shortness of breath." },
    };

    [Theory]
    [MemberData(nameof(TypographicDefects))]
    public void SA_G2_TypographicCorruption_Audited_Shapes_Fire_In_Model_Answer(string key, string find, string replace)
        => AssertRuleFires(LintInjected(key, find, replace), TypographicCorruption);

    [Theory]
    [MemberData(nameof(TypographicDefects))]
    public void SA_G2_TypographicCorruption_Is_Silent_In_The_Candidate_Lane(string key, string find, string replace)
        => AssertRuleDoesNotFire(LintInjected(key, find, replace, isModelAnswer: false), TypographicCorruption);

    [Theory]
    [InlineData("weston", "She has a background of", "She had had a hysterectomy and has a background of")]
    [InlineData("taylor", TaylorBreath, "He reported symptoms, e.g. shortness of breath.")]
    [InlineData("taylor", TaylorBreath, "He reported shortness of breath. eGFR was normal.")]
    [InlineData("taylor", TaylorBreath, "His temperature was 37.8 °C and the ratio was 2.5.")]
    [InlineData("taylor", TaylorBreath, "He was previously treated at Wagga Wagga Base Hospital.")]
    public void SA_G2_TypographicCorruption_Valid_Text_Does_Not_Fire(string key, string find, string replace)
        => AssertRuleDoesNotFire(LintInjected(key, find, replace), TypographicCorruption);
}
