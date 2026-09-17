using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Senior Assessor Release Audit (owner, 16 Sep 2026), group G4: request
/// function, closure request, discharge introduction, background placement,
/// letter-type function and unidiomatic request wording.
///
/// For each class: the REAL audited defect shape, injected into a canonical
/// clean fixture, is DETECTED in Model Answer mode; valid alternatives are NOT
/// flagged; the same defect in candidate mode is NOT flagged (every G4 branch
/// is Model-Answer-only); and the canonical clean fixtures stay clean. Every
/// injection asserts that its needle landed.
/// </summary>
public sealed class WritingSeniorAuditG4RegressionTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());

    private static List<LintFinding> Lint(string letter, string letterType,
        string? caseNotes = null, string? taskText = null, bool isModelAnswer = true,
        ExamProfession profession = ExamProfession.Medicine)
        => Engine.Lint(new WritingLintInput(
            LetterText: letter,
            LetterType: letterType,
            CaseNotesText: caseNotes,
            TaskText: taskText,
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

    private const string DuplicateRequest = "no_duplicated_request";
    private const string ClosureRequest = "closure_request_paragraph";
    private const string IntroPurpose = "intro_purpose_vague";
    private const string BackgroundPlacement = "background_paragraph_placement";
    private const string LetterTypeFunction = "letter_type_function_mismatch";

    // register_colloquial is also extended by another audit group, so the G4f
    // assertions look only at the unidiomatic-request branch's own findings.
    private static bool IsUnidiomaticRequestFinding(LintFinding f)
        => f.RuleId.EndsWith("register_colloquial", StringComparison.Ordinal)
           && f.Message.Contains("unidiomatic request wording", StringComparison.Ordinal);

    private const string Garcia = WritingRev8RegressionFixtureTests.GarciaUpdateLetter;        // LT-DG
    private const string Weston = WritingRev8RegressionFixtureTests.WestonReferralLetter;      // LT-NM
    private const string Weir = WritingRev8RegressionFixtureTests.WeirRoutineReferralLetter;   // LT-RR
    private const string McDonald = WritingRev8RegressionFixtureTests.McDonaldTransferLetter;  // LT-TR
    private const string Taylor = WritingRev8RegressionFixtureTests.TaylorUrgentReferralLetter;// LT-UR

    private static (string Letter, string LetterType, ExamProfession Profession) Fixture(string key) => key switch
    {
        "Garcia" => (Garcia, "LT-DG", ExamProfession.Medicine),
        "Weston" => (Weston, "LT-NM", ExamProfession.Medicine),
        "Weir" => (Weir, "LT-RR", ExamProfession.Medicine),
        "McDonald" => (McDonald, "LT-TR", ExamProfession.Medicine),
        "Taylor" => (Taylor, "LT-UR", ExamProfession.Medicine),
        "UltimateWeir" => (WritingUltimateFinalRegressionFixtureTests.WeirRoutineReferralLetter, "LT-RR", ExamProfession.Medicine),
        "UltimateGarcia" => (WritingUltimateFinalRegressionFixtureTests.GarciaDischargeLetter, "LT-DG", ExamProfession.Medicine),
        "UltimateRamsey" => (WritingUltimateFinalRegressionFixtureTests.RamseyPharmacyLetter, "LT-OT", ExamProfession.Pharmacy),
        "UltimateWright" => (WritingUltimateFinalRegressionFixtureTests.WrightKneeReferralLetter, "LT-RR", ExamProfession.Physiotherapy),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown fixture."),
    };

    private static List<LintFinding> LintFixture(string key)
    {
        var (letter, letterType, profession) = Fixture(key);
        return Lint(letter, letterType, profession: profession);
    }

    // ─────────────────────────────────────────────────────────────────
    // G4a — no_duplicated_request: semantic paraphrase of the request.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SA_G4_DuplicateRequest_Assess_And_Advise_Paraphrase_Fires()
    {
        // Audit #25 Michael Weir (live): intro "assessment and management",
        // closure "assess Mr Weir and advise on further management".
        var letter = Inject(Weir,
            "I would be grateful if you could consider MRI if clinically indicated.",
            "I would be grateful if you could assess Mr Weir and advise on further management, including possible MRI.");
        AssertRuleFires(Lint(letter, "LT-RR"), DuplicateRequest);
    }

    [Fact]
    public void SA_G4_DuplicateRequest_Admission_Requested_Twice_Fires()
    {
        // Audit #24 Julian McDonald (live): admission in intro AND closure.
        var letter = Inject(McDonald,
            "I am writing to transfer Mr McDonald to your rehabilitation service following elective left total knee replacement on 20 July 2018.",
            "I am writing to request Mr McDonald's admission for immediate rehabilitation following his elective left total knee replacement.");
        letter = Inject(letter,
            "I would be grateful if you could confirm Mr McDonald's admission for immediate rehabilitation.",
            "I would be grateful if you could confirm Mr McDonald's admission before his specialist appointment on 7 September 2018.");
        AssertRuleFires(Lint(letter, "LT-TR"), DuplicateRequest);
    }

    [Fact]
    public void SA_G4_DuplicateRequest_Same_Assessment_Object_Fires()
    {
        // Audit #13 Karen Smith (live): "neurological assessment" requested in
        // the intro and "provide the neurological assessment Mrs Smith
        // requires" in the closure.
        var letter = Inject(Taylor,
            "I would be grateful if you could consider tophus removal, if clinically indicated, at your earliest convenience.",
            "I would be grateful if you could provide the rheumatological assessment Mr Taylor requires at your earliest convenience.");
        AssertRuleFires(Lint(letter, "LT-UR"), DuplicateRequest);
    }

    [Fact]
    public void SA_G4_DuplicateRequest_Generic_Assessment_And_Opinion_Closure_Fires()
    {
        // Audit #26 Patrick Newton (live): intro "for assessment of ...",
        // closure "I would be grateful for your assessment and opinion."
        var letter = Inject(Weir,
            "I am writing to request your neurological assessment and management of Mr Michael Weir, who has presented with features suggestive of multiple sclerosis.",
            "I am writing to refer Mr Michael Weir for assessment of features suggestive of multiple sclerosis.");
        letter = Inject(letter,
            "I would be grateful if you could consider MRI if clinically indicated.",
            "I would be grateful for your assessment and opinion.");
        AssertRuleFires(Lint(letter, "LT-RR"), DuplicateRequest);
    }

    [Fact]
    public void SA_G4_DuplicateRequest_Transfer_Intro_With_Admission_Closure_Does_Not_Fire()
        => AssertRuleDoesNotFire(Lint(McDonald, "LT-TR"), DuplicateRequest);

    [Fact]
    public void SA_G4_DuplicateRequest_Distinct_Added_Action_Does_Not_Fire()
    {
        // Brian Morgan shape (not flagged by the audit): the closure adds a
        // distinct action after a shared assessment.
        var letter = Inject(Taylor,
            "I would be grateful if you could consider tophus removal, if clinically indicated, at your earliest convenience.",
            "Given this rapid deterioration, I would be grateful if you could assess Mr Taylor and arrange tophus removal should this prove necessary, at your earliest convenience.");
        AssertRuleDoesNotFire(Lint(letter, "LT-UR"), DuplicateRequest);
    }

    [Fact]
    public void SA_G4_DuplicateRequest_Compound_Medication_Review_And_Counselling_Does_Not_Fire()
    {
        // Alison Martin shape: "medication review" is a compound noun and
        // counselling is a distinct action. Cross-model audit (owner, 17 Sep
        // 2026): "confirm the diagnosis" after an assessment request IS a
        // duplicate (WritingCrossModelAuditRegressionTests), so the distinct
        // diagnostic objective here is "clarify the possible diagnosis".
        var letter = Inject(Weir,
            "I would be grateful if you could consider MRI if clinically indicated.",
            "I would be grateful if you could clarify the possible diagnosis and advise on ongoing management, including counselling and medication review.");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR"), DuplicateRequest);
    }

    [Fact]
    public void SA_G4_DuplicateRequest_Different_Assessment_Object_Does_Not_Fire()
        // Weston: "occupational therapy assessment" in the intro, "an
        // ergonomics assessment" as the distinct closure action.
        => AssertRuleDoesNotFire(Lint(Weston, "LT-NM"), DuplicateRequest);

    [Fact]
    public void SA_G4_DuplicateRequest_Candidate_Paraphrase_Is_Not_Flagged()
    {
        var letter = Inject(Weir,
            "I would be grateful if you could consider MRI if clinically indicated.",
            "I would be grateful if you could assess Mr Weir and advise on further management, including possible MRI.");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR", isModelAnswer: false), DuplicateRequest);
    }

    [Fact]
    public void SA_G4_DuplicateRequest_Verbatim_Repeat_Is_Reported_Once()
    {
        // The existing 4-gram branch owns the verbatim repeat (R2-09); the
        // paraphrase extension must not stack a second finding on it.
        var letter = Inject(McDonald,
            "I would be grateful if you could confirm Mr McDonald's admission for immediate rehabilitation.",
            "I would be grateful if you could transfer Mr McDonald to your rehabilitation service without delay.");
        Assert.Single(Lint(letter, "LT-TR"), f => f.RuleId.EndsWith(DuplicateRequest, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Garcia")]
    [InlineData("Weston")]
    [InlineData("Weir")]
    [InlineData("McDonald")]
    [InlineData("Taylor")]
    [InlineData("UltimateWeir")]
    [InlineData("UltimateGarcia")]
    [InlineData("UltimateRamsey")]
    [InlineData("UltimateWright")]
    public void SA_G4_DuplicateRequest_Clean_Fixtures_Do_Not_Fire(string fixture)
        => AssertRuleDoesNotFire(LintFixture(fixture), DuplicateRequest);

    // ─────────────────────────────────────────────────────────────────
    // G4b — closure_request_paragraph: canonical request paragraph absent.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SA_G4_ClosureRequest_Please_Monitor_Instruction_Fires()
    {
        // Audit #46 Ling Wu (live): "Please monitor for signs of ..." before
        // the contact sentence.
        var letter = Inject(Garcia,
            "I would be grateful if you could advise Ms Garcia's close contacts to seek prompt care if unwell and consider chemoprophylaxis.",
            "Please monitor Ms Garcia's close contacts for signs of meningitis and arrange chemoprophylaxis if required.");
        AssertRuleFires(Lint(letter, "LT-DG"), ClosureRequest);
    }

    [Fact]
    public void SA_G4_ClosureRequest_Please_Review_Instruction_Fires()
    {
        // Betty Johnson (live): "Please review Ms Johnson within one week ...".
        var letter = Inject(Weir,
            "I would be grateful if you could consider MRI if clinically indicated.",
            "Please review Mr Weir and consider MRI if clinically indicated.");
        AssertRuleFires(Lint(letter, "LT-RR"), ClosureRequest);
    }

    [Fact]
    public void SA_G4_ClosureRequest_Lead_In_Please_See_Him_Fires()
    {
        // Audit #51 Peter Sullivne (live): "Given ..., please see him for ...".
        var letter = Inject(Weir,
            "I would be grateful if you could consider MRI if clinically indicated.",
            "Given Mr Weir's new neurological signs, please see him for neurological assessment and consider MRI if clinically indicated.");
        AssertRuleFires(Lint(letter, "LT-RR"), ClosureRequest);
    }

    [Fact]
    public void SA_G4_ClosureRequest_Request_Merged_Into_Counselling_Sentence_Fires()
    {
        // Lucy Clarke (live, post-audit regression): "I counselled Mrs Clarke
        // ... and would be grateful for ...".
        var letter = Inject(Taylor,
            "I would be grateful if you could consider tophus removal, if clinically indicated, at your earliest convenience.",
            "I counselled Mr Taylor regarding the risk of further attacks and would be grateful for consideration of tophus removal at your earliest convenience.");
        AssertRuleFires(Lint(letter, "LT-UR"), ClosureRequest);
    }

    [Fact]
    public void SA_G4_ClosureRequest_No_Request_Paragraph_At_All_Fires()
    {
        // Audit #12 Janet Pristiely / #35 Sandra Peterson (live): the paragraph
        // before the contact sentence is clinical content, no request anywhere.
        var letter = Inject(Garcia,
            "\n\nI would be grateful if you could advise Ms Garcia's close contacts to seek prompt care if unwell and consider chemoprophylaxis.",
            string.Empty);
        AssertRuleFires(Lint(letter, "LT-DG"), ClosureRequest);
    }

    [Fact]
    public void SA_G4_ClosureRequest_Single_Lead_In_Before_Canonical_Request_Does_Not_Fire()
    {
        // Mathis/Woods/Foster shape left unflagged by the audit.
        var letter = Inject(Weir,
            "I would be grateful if you could consider MRI if clinically indicated.",
            "Given these neurological signs, I would be grateful if you could consider MRI if clinically indicated.");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR"), ClosureRequest);
    }

    [Fact]
    public void SA_G4_ClosureRequest_I_Would_Appreciate_Does_Not_Fire()
    {
        var letter = Inject(Weir,
            "I would be grateful if you could consider MRI if clinically indicated.",
            "I would appreciate your consideration of MRI if clinically indicated.");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR"), ClosureRequest);
    }

    [Fact]
    public void SA_G4_ClosureRequest_Enclosure_After_The_Request_Sentence_Does_Not_Fire()
    {
        // Katherine Topp shape: only the paragraph opener must be the request.
        var letter = Inject(Taylor,
            "I would be grateful if you could consider tophus removal, if clinically indicated, at your earliest convenience.",
            "I would be grateful if you could consider tophus removal, if clinically indicated, at your earliest convenience. Please find enclosed a copy of Mr Taylor's recent blood test results.");
        AssertRuleDoesNotFire(Lint(letter, "LT-UR"), ClosureRequest);
    }

    [Fact]
    public void SA_G4_ClosureRequest_Task_Requesting_No_Action_Does_Not_Fire()
    {
        // OA2-03: a pure information task never needs an invented request.
        var letter = Inject(Garcia,
            "\n\nI would be grateful if you could advise Ms Garcia's close contacts to seek prompt care if unwell and consider chemoprophylaxis.",
            string.Empty);
        const string task = "Using the information in the case notes, write a letter to Dr Bradbury for her information only; no further action is required.";
        AssertRuleDoesNotFire(Lint(letter, "LT-DG", taskText: task), ClosureRequest);
    }

    [Fact]
    public void SA_G4_ClosureRequest_Other_Letters_Are_Out_Of_Scope()
    {
        var letter = Inject(WritingUltimateFinalRegressionFixtureTests.RamseyPharmacyLetter,
            "I would be grateful if you could arrange weekly blister packs and a medication administration check.",
            "Please arrange weekly blister packs and a medication administration check.");
        AssertRuleDoesNotFire(Lint(letter, "LT-OT", profession: ExamProfession.Pharmacy), ClosureRequest);
    }

    [Fact]
    public void SA_G4_ClosureRequest_Candidate_Please_Instruction_Is_Not_Flagged()
    {
        var letter = Inject(Weir,
            "I would be grateful if you could consider MRI if clinically indicated.",
            "Please review Mr Weir and consider MRI if clinically indicated.");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR", isModelAnswer: false), ClosureRequest);
    }

    [Fact]
    public void SA_G4_ClosureRequest_Request_Merged_Into_Body_Is_Reported_Once()
    {
        // The existing branch owns this shape (R2-08); the new branch must not
        // stack a second finding on it.
        var letter = Inject(Garcia,
            "family immunisation was discussed.\n\nI would be grateful",
            "family immunisation was discussed. I would be grateful");
        Assert.Single(Lint(letter, "LT-DG"), f => f.RuleId.EndsWith(ClosureRequest, StringComparison.Ordinal));
    }

    // UltimateGarcia and UltimateWright were reworded from "Please monitor ..." /
    // "Please assess ..." to the canonical request family in this round.
    [Theory]
    [InlineData("Garcia")]
    [InlineData("Weston")]
    [InlineData("Weir")]
    [InlineData("McDonald")]
    [InlineData("Taylor")]
    [InlineData("UltimateWeir")]
    [InlineData("UltimateGarcia")]
    [InlineData("UltimateRamsey")]
    [InlineData("UltimateWright")]
    public void SA_G4_ClosureRequest_Clean_Fixtures_Do_Not_Fire(string fixture)
        => AssertRuleDoesNotFire(LintFixture(fixture), ClosureRequest);

    // ─────────────────────────────────────────────────────────────────
    // G4c — intro_purpose_vague: discharge introduction without an
    // ongoing-care request.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SA_G4_IntroPurpose_Discharge_Update_With_History_Noun_Admission_Fires()
    {
        // Audit #37 Betty Johnson (live): "discharged today after a right total
        // knee replacement and rehabilitation admission" — the history noun
        // "admission" is not a reader action.
        var letter = Inject(Garcia,
            "I am writing to update you regarding Ms Isabel Garcia's treatment for bacterial meningitis and request follow-up of close contacts.",
            "I am writing to update you on Ms Garcia, discharged today after her admission for bacterial meningitis.");
        AssertRuleFires(Lint(letter, "LT-DG"), IntroPurpose);
    }

    [Fact]
    public void SA_G4_IntroPurpose_Introduce_Opening_Without_Any_Request_Fires()
    {
        // Audit #12 Janet Pristiely / #35 Sandra Peterson (live): "I am writing
        // to introduce ..., admitted on ... and discharged today." and no
        // request anywhere in the letter.
        var letter = Inject(Garcia,
            "I am writing to update you regarding Ms Isabel Garcia's treatment for bacterial meningitis and request follow-up of close contacts.",
            "I am writing to introduce Ms Garcia, admitted on 20 May 2015 with bacterial meningitis and discharged today.");
        letter = Inject(letter,
            "\n\nI would be grateful if you could advise Ms Garcia's close contacts to seek prompt care if unwell and consider chemoprophylaxis.",
            string.Empty);
        AssertRuleFires(Lint(letter, "LT-DG"), IntroPurpose);
    }

    [Fact]
    public void SA_G4_IntroPurpose_Introduce_Opening_In_A_Referral_Fires()
    {
        var letter = Inject(Weir,
            "I am writing to request your neurological assessment and management of Mr Michael Weir, who has presented with features suggestive of multiple sclerosis.",
            "I am writing to introduce Mr Michael Weir, who has presented with features suggestive of multiple sclerosis after a recent admission.");
        AssertRuleFires(Lint(letter, "LT-RR"), IntroPurpose);
    }

    [Fact]
    public void SA_G4_IntroPurpose_Discharge_Intro_Requesting_Follow_Up_Does_Not_Fire()
        => AssertRuleDoesNotFire(Lint(Garcia, "LT-DG"), IntroPurpose);

    [Fact]
    public void SA_G4_IntroPurpose_Advise_You_Of_Further_Management_Does_Not_Fire()
    {
        // John Aloisius shape (not flagged by the audit).
        var letter = Inject(Garcia,
            "I am writing to update you regarding Ms Isabel Garcia's treatment for bacterial meningitis and request follow-up of close contacts.",
            "I am writing to update you on Ms Isabel Garcia's treatment for bacterial meningitis and to advise you of the further management of her close contacts.");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG"), IntroPurpose);
    }

    [Fact]
    public void SA_G4_IntroPurpose_Action_In_The_Second_Intro_Sentence_Does_Not_Fire()
    {
        // Sylvia Meadows shape: the whole introduction paragraph is tested.
        var letter = Inject(Garcia,
            "I am writing to update you regarding Ms Isabel Garcia's treatment for bacterial meningitis and request follow-up of close contacts.",
            "I am writing to update you on Ms Garcia, who was discharged today after her admission for bacterial meningitis. Her close contacts require follow-up.");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG"), IntroPurpose);
    }

    [Fact]
    public void SA_G4_IntroPurpose_Update_Referral_That_Is_Not_A_Discharge_Is_Out_Of_Scope()
    {
        var letter = Inject(Weir,
            "I am writing to request your neurological assessment and management of Mr Michael Weir, who has presented with features suggestive of multiple sclerosis.",
            "I am writing to update you on Mr Michael Weir after his recent admission.");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR"), IntroPurpose);
    }

    [Fact]
    public void SA_G4_IntroPurpose_Candidate_Discharge_Intro_Is_Not_Flagged()
    {
        var letter = Inject(Garcia,
            "I am writing to update you regarding Ms Isabel Garcia's treatment for bacterial meningitis and request follow-up of close contacts.",
            "I am writing to update you on Ms Garcia, discharged today after her admission for bacterial meningitis.");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG", isModelAnswer: false), IntroPurpose);
    }

    [Fact]
    public void SA_G4_IntroPurpose_Topic_Only_Intro_Is_Reported_Once()
    {
        // The existing topic-only branch owns this shape (R2-03).
        var letter = Inject(Garcia,
            "I am writing to update you regarding Ms Isabel Garcia's treatment for bacterial meningitis and request follow-up of close contacts.",
            "I am writing to update you regarding Ms Garcia's diagnosis and treatment for bacterial meningitis.");
        Assert.Single(Lint(letter, "LT-DG"), f => f.RuleId.EndsWith(IntroPurpose, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Garcia")]
    [InlineData("Weston")]
    [InlineData("Weir")]
    [InlineData("McDonald")]
    [InlineData("Taylor")]
    [InlineData("UltimateWeir")]
    [InlineData("UltimateGarcia")]
    [InlineData("UltimateRamsey")]
    [InlineData("UltimateWright")]
    public void SA_G4_IntroPurpose_Clean_Fixtures_Do_Not_Fire(string fixture)
        => AssertRuleDoesNotFire(LintFixture(fixture), IntroPurpose);

    // ─────────────────────────────────────────────────────────────────
    // G4d — background_paragraph_placement beyond the opening paragraph.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SA_G4_BackgroundPlacement_Remote_History_Opens_The_Body_Fires()
    {
        // Audit #28 Zach Foster (live): "diagnosed with asthma at the age of
        // three, with two previous hospital admissions" before the current
        // presentation.
        var letter = Inject(Weir,
            "On today's review, Mr Weir reported dizziness",
            "Mr Weir was diagnosed with asthma at the age of three, with two previous hospital admissions.\n\nOn today's review, Mr Weir reported dizziness");
        AssertRuleFires(Lint(letter, "LT-RR"), BackgroundPlacement);
    }

    [Fact]
    public void SA_G4_BackgroundPlacement_Habits_Mixed_Into_A_Later_Management_Paragraph_Fires()
    {
        // Audit #26 Patrick Newton (live): "smokes ten to fifteen cigarettes
        // daily, and his uncle has Crohn's disease" beside "I advised smoking
        // cessation".
        var letter = Inject(Weir,
            "He was assessed for hypercholesterolaemia, with repeat testing planned in three months.",
            "He was assessed for hypercholesterolaemia, with repeat testing planned in three months. Mr Weir smokes ten cigarettes daily, and his uncle has multiple sclerosis. I advised smoking cessation.");
        AssertRuleFires(Lint(letter, "LT-RR"), BackgroundPlacement);
    }

    [Fact]
    public void SA_G4_BackgroundPlacement_Allergy_Beside_The_Working_Diagnosis_Fires()
    {
        // Audit #36 Anne Hall / #20 George Poulos (live).
        var letter = Inject(Weir,
            "He was assessed for hypercholesterolaemia, with repeat testing planned in three months.",
            "He was assessed for hypercholesterolaemia, with repeat testing planned in three months. Mr Weir is allergic to penicillin. The working diagnosis is multiple sclerosis.");
        AssertRuleFires(Lint(letter, "LT-RR"), BackgroundPlacement);
    }

    [Fact]
    public void SA_G4_BackgroundPlacement_Urgent_Examination_Delayed_Behind_Background_Fires()
    {
        // Audit #32 Lucy Clarke (audited pack): chronic history paragraph
        // before the paragraph that carries today's examination.
        var letter = Inject(Taylor,
            "Mr Taylor has had gout since 2000, managed with allopurinol, paracetamol and colchicine.",
            "Mr Taylor has had type two diabetes mellitus since 2001, hyperlipidaemia since 2003 and hypertension since 2005.");
        letter = Inject(letter,
            "His brother has gout, and his father died of kidney failure.",
            "His brother has gout, and his father died of kidney failure. On examination today, his right first toe remained inflamed.");
        AssertRuleFires(Lint(letter, "LT-UR"), BackgroundPlacement);
    }

    [Fact]
    public void SA_G4_BackgroundPlacement_Dedicated_Background_Paragraph_Does_Not_Fire()
        => AssertRuleDoesNotFire(Lint(Weir, "LT-RR"), BackgroundPlacement);

    [Fact]
    public void SA_G4_BackgroundPlacement_Referred_Condition_History_In_The_Opening_Does_Not_Fire()
    {
        // James Seymour shape: "has had episodic ... since 2010, and the
        // current episode began ..." is the referred condition, not background.
        var letter = Inject(Taylor,
            "Today, Mr Taylor presented with pain in his right big toe",
            "Mr Taylor has had episodic gout since 2000, and the current flare began four days ago. Today, Mr Taylor presented with pain in his right big toe");
        AssertRuleDoesNotFire(Lint(letter, "LT-UR"), BackgroundPlacement);
    }

    [Fact]
    public void SA_G4_BackgroundPlacement_Condition_Named_In_The_Introduction_Is_Not_Background()
    {
        var letter = Inject(Weir,
            "who has presented with features suggestive of multiple sclerosis.",
            "who has presented with worsening depression and features suggestive of multiple sclerosis.");
        letter = Inject(letter,
            "On today's review, Mr Weir reported dizziness",
            "Mr Weir has depression, treated with sertraline hydrochloride.\n\nOn today's review, Mr Weir reported dizziness");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR"), BackgroundPlacement);
    }

    [Fact]
    public void SA_G4_BackgroundPlacement_Allergic_Reaction_Complaint_And_Recent_Infection_Do_Not_Fire()
    {
        // Karen Smith shape: a precipitating event and a presenting allergic
        // reaction are current-episode facts, not background.
        var letter = Inject(Weir,
            "His blood pressure was 88/70 mmHg.",
            "His blood pressure was 88/70 mmHg. He had an upper respiratory tract infection a week ago. He described an allergic reaction to a new soap last week.");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR"), BackgroundPlacement);
    }

    [Fact]
    public void SA_G4_BackgroundPlacement_Current_Medication_Statement_In_Background_Does_Not_Fire()
    {
        var letter = Inject(Weir,
            "He continues to smoke.",
            "He continues to smoke. He currently takes no other medication.");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR"), BackgroundPlacement);
    }

    [Theory]
    [InlineData("He continues to smoke. He is currently unemployed.")]
    [InlineData("He continues to smoke. He is now retired.")]
    public void SA_G4_BackgroundPlacement_Current_Social_Status_In_Background_Does_Not_Fire(string replacement)
        => AssertRuleDoesNotFire(Lint(Inject(Weir, "He continues to smoke.", replacement), "LT-RR"), BackgroundPlacement);

    [Fact]
    public void SA_G4_BackgroundPlacement_Candidate_Remote_History_First_Is_Not_Flagged()
    {
        var letter = Inject(Weir,
            "On today's review, Mr Weir reported dizziness",
            "Mr Weir was diagnosed with asthma at the age of three, with two previous hospital admissions.\n\nOn today's review, Mr Weir reported dizziness");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR", isModelAnswer: false), BackgroundPlacement);
    }

    [Fact]
    public void SA_G4_BackgroundPlacement_Opening_Paragraph_Habits_Are_Reported_Once()
    {
        // The existing opening-paragraph branch owns this shape (R2-10).
        var letter = Inject(McDonald,
            "A forty-eight-hour ketamine infusion was effective.",
            "He smokes twenty cigarettes daily and drinks six to ten standard drinks daily. A forty-eight-hour ketamine infusion was effective.");
        Assert.Single(Lint(letter, "LT-TR"), f => f.RuleId.EndsWith(BackgroundPlacement, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Garcia")]
    [InlineData("Weston")]
    [InlineData("Weir")]
    [InlineData("McDonald")]
    [InlineData("Taylor")]
    [InlineData("UltimateWeir")]
    [InlineData("UltimateGarcia")]
    [InlineData("UltimateRamsey")]
    [InlineData("UltimateWright")]
    public void SA_G4_BackgroundPlacement_Clean_Fixtures_Do_Not_Fire(string fixture)
        => AssertRuleDoesNotFire(LintFixture(fixture), BackgroundPlacement);

    // ─────────────────────────────────────────────────────────────────
    // G4e — letter_type_function_mismatch (new check id).
    // ─────────────────────────────────────────────────────────────────

    private const string CochraneNotes =
        "On 30/09/19 he presented with severe shortness of breath, chest pain and sweating for 2 hours.\n" +
        "He needs admission to the Cardiology Unit for stabilisation.";

    private const string CochraneTask =
        "Using the information in the case notes, write a letter of referral to the Emergency Registrar, Emergency\n" +
        "Department, QE11 Hospital, explaining the patient's current condition.";

    private const string MorganNotes =
        "Assessment: probable acute appendicitis\n" +
        "Plan: obtain urgent surgical opinion; send to hospital for surgical assessment and operation if necessary; request updates regarding progress";

    private const string MorganTask =
        "Using the information in the case notes, write a letter of referral to the Emergency Department Medical Officer.\n" +
        "Address the letter to:\nDr S Leyshon\nEmergency Department Medical Officer\nPA Hospital\nWooloongabba";

    [Fact]
    public void SA_G4_LetterTypeFunction_Admission_For_Stabilisation_Catalogued_Routine_Fires()
        // Audit #19 Dave Cochrane (production notes, LT-RR).
        => AssertRuleFires(Lint(Weir, "LT-RR", caseNotes: CochraneNotes), LetterTypeFunction);

    [Fact]
    public void SA_G4_LetterTypeFunction_Emergency_Registrar_Recipient_Fires()
        => AssertRuleFires(Lint(Weir, "LT-RR", taskText: CochraneTask), LetterTypeFunction);

    [Fact]
    public void SA_G4_LetterTypeFunction_Urgent_Surgical_Opinion_Plan_Fires()
        // Audit #47 Brian Morgan / OET test 14 (production notes, LT-RR).
        => AssertRuleFires(Lint(Weir, "LT-RR", caseNotes: MorganNotes), LetterTypeFunction);

    [Fact]
    public void SA_G4_LetterTypeFunction_Emergency_Department_Medical_Officer_Recipient_Fires()
        => AssertRuleFires(Lint(Weir, "LT-RR", taskText: MorganTask), LetterTypeFunction);

    [Theory]
    [InlineData("No urgent systemic signs.")]
    [InlineData("She required ICU admission for 3 days and needed magnesium sulphate.")]
    [InlineData("She asked about the possibility of immediate reconstructive surgery.")]
    [InlineData("His blood pressure stabilised by day 3.\nStabilised on Haldol 20mg and sodium valproate 125mg daily.")]
    [InlineData("10 Sep 2022: presented to hospital emergency department with shortness of breath, chest pain, sweating and fever.")]
    [InlineData("Plan: non-urgent referral to rheumatology.")]
    [InlineData("Assessment: suspected thyroid malignancy.\nPlan: refer for further assessment.")]
    public void SA_G4_LetterTypeFunction_Routine_Or_Past_Or_Negated_Notes_Do_Not_Fire(string notes)
        => AssertRuleDoesNotFire(Lint(Weir, "LT-RR", caseNotes: notes), LetterTypeFunction);

    [Fact]
    public void SA_G4_LetterTypeFunction_Suspected_Cancer_Referral_To_A_Surgical_Registrar_Does_Not_Fire()
    {
        // Sandra Marcus (LT-RR): suspected cancer alone is not urgent (house rule 0).
        const string task = "Using the information in the case notes, write a letter to Dr Phillip Wright, a surgical registrar, requesting further\nassessment.";
        AssertRuleDoesNotFire(Lint(Weir, "LT-RR", caseNotes: "Assessment: suspected thyroid malignancy.", taskText: task), LetterTypeFunction);
    }

    [Fact]
    public void SA_G4_LetterTypeFunction_Correctly_Catalogued_Urgent_Referral_Does_Not_Fire()
        => AssertRuleDoesNotFire(Lint(Taylor, "LT-UR", caseNotes: CochraneNotes, taskText: CochraneTask), LetterTypeFunction);

    [Fact]
    public void SA_G4_LetterTypeFunction_Candidate_Lane_Is_Not_Flagged()
        => AssertRuleDoesNotFire(Lint(Weir, "LT-RR", caseNotes: CochraneNotes, taskText: CochraneTask, isModelAnswer: false), LetterTypeFunction);

    [Theory]
    [InlineData("Garcia")]
    [InlineData("Weston")]
    [InlineData("Weir")]
    [InlineData("McDonald")]
    [InlineData("Taylor")]
    [InlineData("UltimateWeir")]
    [InlineData("UltimateGarcia")]
    [InlineData("UltimateRamsey")]
    [InlineData("UltimateWright")]
    public void SA_G4_LetterTypeFunction_Clean_Fixtures_Do_Not_Fire(string fixture)
        => AssertRuleDoesNotFire(LintFixture(fixture), LetterTypeFunction);

    [Fact]
    public void SA_G4_LetterTypeFunction_Clean_Fixtures_With_Their_Source_Do_Not_Fire()
    {
        const string weirTask = "Using the information given in the case notes, write a letter of referral to Dr M McLaren, Neurologist, Suite 3, 67 The Crescent, Newtown.";
        const string taylorNotes = "Plan: refer to a rheumatologist for urgent assessment.";
        AssertRuleDoesNotFire(Lint(Weir, "LT-RR", taskText: weirTask), LetterTypeFunction);
        AssertRuleDoesNotFire(Lint(Taylor, "LT-UR", caseNotes: taylorNotes), LetterTypeFunction);
    }

    // ─────────────────────────────────────────────────────────────────
    // G4f — register_colloquial: unidiomatic request wording.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SA_G4_UnidiomaticRequest_Per_This_Referral_Fires()
    {
        // Audit #8 Cathy Jones (live): "..., per this referral."
        var letter = Inject(Taylor,
            "I would be grateful if you could consider tophus removal, if clinically indicated, at your earliest convenience.",
            "I would be grateful if you could assess Mr Taylor at your earliest convenience, per this referral.");
        Assert.Contains(Lint(letter, "LT-UR"), f => IsUnidiomaticRequestFinding(f) && f.Quote == "per this referral");
    }

    [Fact]
    public void SA_G4_UnidiomaticRequest_Specialist_Noun_As_Modifier_Fires_With_Adjective_Fix()
    {
        // Audit #51 Peter Sullivne (live): "for dermatologist review".
        var letter = Inject(Taylor,
            "I would be grateful if you could consider tophus removal, if clinically indicated, at your earliest convenience.",
            "I would be grateful if you could see Mr Taylor for dermatologist review of the tophus at your earliest convenience.");
        Assert.Contains(Lint(letter, "LT-UR"), f => IsUnidiomaticRequestFinding(f)
            && f.Quote == "dermatologist review" && f.FixSuggestion == "dermatological review");
    }

    [Fact]
    public void SA_G4_UnidiomaticRequest_Outpatient_Specialist_Review_Fires()
    {
        // Sylvia Meadows (live): "She requires an outpatient endocrinologist review."
        var letter = Inject(Garcia,
            "The Department of Human Services was notified, and family immunisation was discussed.",
            "The Department of Human Services was notified, and family immunisation was discussed. Ms Garcia requires an outpatient neurologist review.");
        Assert.Contains(Lint(letter, "LT-DG"), f => IsUnidiomaticRequestFinding(f) && f.Quote == "neurologist review");
    }

    [Fact]
    public void SA_G4_UnidiomaticRequest_Adjective_Form_Does_Not_Fire()
    {
        var letter = Inject(Taylor,
            "I would be grateful if you could consider tophus removal, if clinically indicated, at your earliest convenience.",
            "I would be grateful if you could arrange dermatological assessment of the tophus at your earliest convenience.");
        Assert.DoesNotContain(Lint(letter, "LT-UR"), f => IsUnidiomaticRequestFinding(f));
    }

    [Fact]
    public void SA_G4_UnidiomaticRequest_Discipline_Noun_Does_Not_Fire()
        // Weston: "occupational therapy assessment".
        => Assert.DoesNotContain(Lint(Weston, "LT-NM"), f => IsUnidiomaticRequestFinding(f));

    [Fact]
    public void SA_G4_UnidiomaticRequest_Dietitian_Review_And_As_Per_Protocol_Do_Not_Fire()
    {
        var letter = Inject(McDonald,
            "Physiotherapy, an occupational therapy home visit, social work and drug and alcohol support are planned",
            "Physiotherapy as per the knee replacement protocol, dietitian review, social work and drug and alcohol support are planned");
        Assert.DoesNotContain(Lint(letter, "LT-TR"), f => IsUnidiomaticRequestFinding(f));
    }

    [Fact]
    public void SA_G4_UnidiomaticRequest_Specialist_Noun_As_Subject_Of_Review_Does_Not_Fire()
    {
        var letter = Inject(Weir,
            "A CT scan of the head and lumbar spine has therefore been ordered to investigate possible central or spinal causes.",
            "A CT scan of the head and lumbar spine has therefore been ordered, and I have asked that a neurologist review him.");
        Assert.DoesNotContain(Lint(letter, "LT-RR"), f => IsUnidiomaticRequestFinding(f));
    }

    [Fact]
    public void SA_G4_UnidiomaticRequest_Candidate_Wording_Is_Not_Flagged()
    {
        var letter = Inject(Taylor,
            "I would be grateful if you could consider tophus removal, if clinically indicated, at your earliest convenience.",
            "I would be grateful if you could assess Mr Taylor at your earliest convenience, per this referral.");
        Assert.DoesNotContain(Lint(letter, "LT-UR", isModelAnswer: false), f => IsUnidiomaticRequestFinding(f));
    }

    [Theory]
    [InlineData("Garcia")]
    [InlineData("Weston")]
    [InlineData("Weir")]
    [InlineData("McDonald")]
    [InlineData("Taylor")]
    [InlineData("UltimateWeir")]
    [InlineData("UltimateGarcia")]
    [InlineData("UltimateRamsey")]
    [InlineData("UltimateWright")]
    public void SA_G4_UnidiomaticRequest_Clean_Fixtures_Do_Not_Fire(string fixture)
        => Assert.DoesNotContain(LintFixture(fixture), f => IsUnidiomaticRequestFinding(f));
}
