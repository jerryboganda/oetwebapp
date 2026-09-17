using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Senior Assessor Release Audit (16 Sep 2026), group G7 — layout/identity
/// additions from the Model Answer render audit:
/// - re_line_age_when_no_dob (new): notes with an age and no DOB require the
///   Re: line to end ", aged N" (Mathis, Cathy Jones, Foster, Karen Jackson,
///   Betty Johnson, Geller, Perfect-Taylor).
/// - address_content_unsupported (new): every recipient line is supported by
///   the task or notes (Macalaque's invented "Dr Helena Rao"), and the task's
///   recipient name, post-nominals and postcode are never dropped (MacIntyre
///   "MBBS FRANZCOG", Erika Stone "NW1 2TG").
/// - role_salutation_matches_task (Model Answer extension): a bare role
///   recipient line ("Gynaecology Registrar") requires "Dear Gynaecology
///   Registrar," (OA2-17 / house rule 4a).
/// Every class proves the audited defect fires in Model Answer mode, valid
/// alternatives stay silent, the candidate lane stays silent and the clean
/// canonical fixtures stay clean.
/// </summary>
public sealed class WritingSeniorAuditG7RegressionTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());

    private const string AgeCheck = "re_line_age_when_no_dob";
    private const string AddressCheck = "address_content_unsupported";
    private const string RoleCheck = "role_salutation_matches_task";

    private static List<LintFinding> Lint(string letter, string letterType,
        string? caseNotes = null, string? taskText = null, bool isModelAnswer = true)
        => Engine.Lint(new WritingLintInput(
            LetterText: letter,
            LetterType: letterType,
            CaseNotesText: caseNotes,
            TaskText: taskText,
            Profession: ExamProfession.Medicine,
            IsModelAnswer: isModelAnswer)).ToList();

    private static void AssertRuleFires(List<LintFinding> findings, string checkId)
        => Assert.Contains(findings, f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal));

    private static void AssertRuleDoesNotFire(List<LintFinding> findings, string checkId)
        => Assert.DoesNotContain(findings, f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal));

    private static List<LintFinding> FindingsFor(List<LintFinding> findings, string checkId)
        => findings.Where(f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal)).ToList();

    private static string Inject(string letter, string find, string replace)
    {
        var injected = letter.Replace(find, replace);
        Assert.NotEqual(letter, injected);
        return injected;
    }

    private const string Garcia = WritingRev8RegressionFixtureTests.GarciaUpdateLetter;
    private const string Weston = WritingRev8RegressionFixtureTests.WestonReferralLetter;
    private const string Weir = WritingRev8RegressionFixtureTests.WeirRoutineReferralLetter;
    private const string McDonald = WritingRev8RegressionFixtureTests.McDonaldTransferLetter;
    private const string Taylor = WritingRev8RegressionFixtureTests.TaylorUrgentReferralLetter;

    // The canonical Weir letter carries the source DOB; the Re: line age rule
    // is proved on the SYNTHETIC DOB-less variant with DOB-less notes.
    private static readonly string WeirWithoutDob = WritingRev8RegressionFixtureTests.WeirRoutineReferralLetterWithoutDob;

    private const string WeirAddress = "Dr M McLaren\nNeurologist\nSuite 3\n67 The Crescent\nNewtown";
    private const string McDonaldAddress = "The Admissions Officer\nCabrini Hopetoun Rehabilitation\n2-6 Hopetoun Street\nElsternwick\nVic 3185";

    // ─── Canonical source texts (copies of the fixture-suite constants) ────

    private const string GarciaCaseNotes =
        "Patient was referred to the Emergency Department by her GP, Dr Bradbury. " +
        "Date of birth 01.01.1995 (age 20). " +
        "Presented on 23 May 2015 with painful, stiff joints for one week. " +
        "On examination: afebrile; bruising to left arm; petechial rash on abdomen and legs; unable to touch chin to chest when lying supine. " +
        "White cell count 14.0x10^9/L. C-reactive protein 150. " +
        "Lumbar puncture white cell count 1000 (elevated); glucose 10mg/dl (reduced); protein 70mg/dl (elevated). " +
        "Culture identified Neisseria meningitidis. Diagnosis: bacterial meningitis. " +
        "Treated with Dexamethasone 10mg IV before the first dose of antibiotics, then 10mg IV every 6 hours for 4 days. " +
        "Treated with Ceftriaxone 2g IV twice daily while awaiting lumbar puncture culture results. " +
        "Following lumbar puncture results, treated with benzylpenicillin 1.8g IV every 4 hours for 5 days. " +
        "Department of Human Services notified. Query chemoprophylaxis for close contacts.";

    private const string GarciaTaskText =
        "Using the information given in the case notes, write a letter to Dr Bradbury, the doctor who referred Ms Garcia, " +
        "to update her on the patient's status and follow-up treatment that may be required in the future. " +
        "Address the letter to Dr Lorna Bradbury, Stillwater Medical Clinic, 12 Main Street, Stillwater.";

    private const string TaylorCaseNotes =
        "Mr David Taylor, DOB 01/08/1965 (age 55). " +
        "History of gout since 2000, treated with colchicine (Lengout), allopurinol and paracetamol. " +
        "On 13/06/2020 presented with pain in his right big toe and swelling of the right foot. " +
        "Observations: BP 120/80, HR 90, RR 22, temperature 37.8°C. " +
        "Right first toe inflamed and red, with a tophus noted under the right big toe. " +
        "Plan: refer to a rheumatologist for urgent assessment.";

    private const string TaylorTaskText =
        "Using the information given in the case notes, write a letter of referral to Dr Still, a Rheumatologist at City Hospital, " +
        "for assessment of Mr Taylor. Address the letter to Dr Malcom Still, Rheumatologist, City Hospital, Suite 32, 55 Main Road, Newtown.";

    private const string WestonCaseNotes =
        "Patient is Betty Weston, DOB 12.2.64 (55 years). " +
        "Presenting problem: numbness/tingling in the thumb, index and middle finger of the right hand. " +
        "Diagnosis: carpal tunnel syndrome. " +
        "Discharge plan: refer to occupational therapy for a custom-made wrist splint in neutral position.";

    private const string WestonTaskText =
        "Using the information given in the case notes, write a referral letter to Ms Goody, a community occupational therapist, " +
        "for further management of Betty's condition. Address the letter to Ms Alison Goody, Occupational Therapist, " +
        "Northwood Community Health Centre, Northwood.";

    // Synthetic DOB-less notes (the real Weir source carries a DOB): only the
    // CHILDREN's ages, which never identify the patient.
    private const string WeirCaseNotes =
        "Mr Michael Weir is a patient in your general practice, height 183cm. " +
        "He is married with 3 children aged 13, 10 and 8. " +
        "He has depression, treated with sertraline hydrochloride (Zoloft) since September 2012. " +
        "On 09.08.14 he complained of dizziness and two recent blackouts lasting a few minutes each. " +
        "Examination on 09.08.14: BP 88/70, HR 76bpm, BMI 28 (93.7kg), chest clear.";

    private const string WeirTaskText =
        "Using the information given in the case notes, write a letter of referral to Dr M McLaren, Neurologist, Suite 3, 67 The Crescent, Newtown.";

    private const string McDonaldCaseNotes =
        "Patient is Julian McDonald (Mr), a 68-year-old male recovering from total left knee joint replacement. DOB: 12/1/50.";

    private const string McDonaldTaskText =
        "Mr McDonald was admitted 4 days ago for knee surgery at the Alfred Hospital where you work. " +
        "Using the information in the case notes, write a transfer letter to the Admissions Officer at Cabrini Hopetoun Rehabilitation, " +
        "2-6 Hopetoun Street, Elsternwick, Vic 3185, for Mr McDonald's immediate treatment.";

    // ─── Live audited sources (medicine55_inputs.json) ────

    private const string MathisAgeNotes = "Patient: Allen Mathis, 61 years old, retired cabinet maker";

    private const string MacalaqueTaskText =
        "Write a referral letter to a neuro-ophthalmologist regarding Mr Macalaque's new visual disturbances (blurred/double vision, " +
        "suspected diplopia with possible cranial nerve abnormality) that developed after his motor vehicle accident and head injury, " +
        "requesting assessment with a two-week review.";

    private const string MacalaqueCaseNotes =
        "Mr Brendan Macalaque, DOB 02/09/1973.\n" +
        "Works as a teacher, currently on leave.\n" +
        "Admitted to hospital on 23/09/18 following a motor vehicle accident (MVA).\n" +
        "Discharged from hospital on 30/10/18 and admitted to a rehabilitation centre.\n" +
        "Presented on 02/12/2018 with visual disturbances, complaining of blurred vision and double vision.\n" +
        "Eye test showed decreased vision of 6/9.\n" +
        "Diagnosis: diplopia, possible cranial nerve abnormality.\n" +
        "Plan: refer to a neuro-ophthalmologist and review in two weeks.";

    private const string MacalaqueInventedAddress = "Dr Helena Rao\nNeuro-Ophthalmologist\nCity Eye Centre\n45 Bridge Street\nNewtown";

    private const string MacIntyreTaskText =
        "Using the information in the case notes, write a referral letter to Dr Anne Childers MBBS FRANZCOG,\n" +
        "Consultant Obstetrician. Address the letter to:\n" +
        "Dr Anne Childers MBBS FRANZCOG\nConsultant Obstetrician\nSpirit Mother's Hospital\nStanley Street\nSouth Brisbane\n" +
        "In your answer:\n-\nExpand the relevant case notes into complete sentences.\n-\nDo not use note form.\n-\nUse letter format.\n" +
        "The body of your letter should be approximately 180-200 words.";

    private const string StoneTaskText =
        "Using the information in the case notes, write a letter of referral to Dr Maria Foreman, an endocrinologist at the " +
        "Endocrinology department of Royal Restricted Hospital, 11 Lake Street, London, NW1 2TG.\n\n" +
        "In your answer:\n- Expand the relevant notes into complete sentences.\n- Do not use note form.\n- Use letter format.\n\n" +
        "The body of the letter should be approximately 180-200 words.";

    private const string MorganTaskText =
        "Using the information in the case notes, write a letter of referral to the Emergency Department Medical Officer.\n" +
        "Address the letter to:\nDr S Leyshon\nEmergency Department Medical Officer\nPA Hospital\nWooloongabba\n" +
        "In your answer:\n-\nExpand the relevant case notes into complete sentences.";

    private const string MaTaskText =
        "Write a referral letter to Dr Janine McArdle, GP, University Medical Centre, St Lucia, summarising Mr Ma's\n" +
        "condition and the follow-up care he requires.\nIn your answer:\n-\nExpand the relevant case notes into complete sentences.";

    private const string GellerTaskText =
        "Using the information given in the case note, write a letter of referral to Dr Yates consultant Endocrinologist, " +
        "summarizing Ms Geller's relevant medical history, outlining your concerns, and requesting investigations and advice on " +
        "management address the letter to Dr Rania Yates , consultant Endocrinologist , Stanbridge Hospital , Moliers hill Stanbridge.";

    private const string CathyJonesTaskText =
        "Using the information in the case notes, write a referral letter to the Gynaecology Registrar, Spirit Hospital,\n" +
        "requesting urgent assessment. Address the letter to:\n" +
        "Gynaecology Registrar\nA&E Department\nSpirit Hospital\n111 Stanley St\nSouth Brisbane, QLD 4222\n" +
        "In your answer:\n-\nExpand the relevant case notes into complete sentences.";

    private const string CathyJonesAddress = "Gynaecology Registrar\nA&E Department\nSpirit Hospital\n111 Stanley St\nSouth Brisbane\nQLD 4222";

    private const string MarcusTaskText =
        "Using the information in the case notes, write a letter to Dr Phillip Wright, a surgical registrar, requesting further\n" +
        "assessment. Address the letter to:\n" +
        "Dr Phillip Wright\nSurgical Registrar\nBrisbane General Hospital\n765 Brunswick St\nValley, QLD 4444\n" +
        "In your answer:\n-\nExpand the relevant case notes into complete sentences.";

    private const string MathisTaskText =
        "No formal 'Writing Task' paragraph present; case notes end with a Plan-style line: 'Referral letter to Cardiologist, " +
        "Emergency department, Central hospital, Stillwater. For urgent assessment and advice on Mx.' Implied task: urgent referral " +
        "letter to the ED cardiologist at Central Hospital, Stillwater.";

    // ─────────────────────────────────────────────────────────────────
    // re_line_age_when_no_dob
    // ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Patient: Michael Weir, 61 years old, retired cabinet maker", "61")]
    [InlineData("Mr Michael Weir is 38 years old.", "38")]
    [InlineData("Mr Michael Weir is 25 years old, single, and works as a receptionist.", "25")]
    [InlineData("22-year-old man.\nMedical history: asthma since age 3, with 2 previous hospital admissions.\nFamily history: sister (age 18) has asthma.", "22")]
    [InlineData("Mr Michael Weir, 36 years old, has been a patient at the clinic for 10 years.\nHe is married with 1 child, a daughter aged 28 months.", "36")]
    [InlineData("Mr Michael Weir is an 81-year-old man who recently had a right total knee replacement on 25/02/2015 and is being discharged today", "81")]
    public void SA_G7_ReLineAgeWhenNoDob_Missing_Age_Fires_In_Model_Answer(string notes, string age)
    {
        // The live shape: "Re: Mr Allen Mathis" while the notes say
        // "61 years old" and record no DOB.
        var findings = FindingsFor(Lint(WeirWithoutDob, "LT-RR", caseNotes: notes), AgeCheck);
        var finding = Assert.Single(findings);
        Assert.Equal("Re: Mr Michael Weir, aged " + age, finding.FixSuggestion);
        Assert.Equal(RuleSeverity.Critical, finding.Severity);
    }

    [Fact]
    public void SA_G7_ReLineAgeWhenNoDob_Age_Without_The_Canonical_Comma_Form_Fires()
    {
        var letter = Inject(WeirWithoutDob, "Re: Mr Michael Weir", "Re: Mr Michael Weir aged 61");
        var finding = Assert.Single(FindingsFor(Lint(letter, "LT-RR", caseNotes: MathisAgeNotes), AgeCheck));
        Assert.Equal("Re: Mr Michael Weir, aged 61", finding.FixSuggestion);
    }

    [Fact]
    public void SA_G7_ReLineAgeWhenNoDob_Canonical_Aged_Form_Passes()
    {
        var letter = Inject(WeirWithoutDob, "Re: Mr Michael Weir", "Re: Mr Michael Weir, aged 61");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR", caseNotes: MathisAgeNotes), AgeCheck);
    }

    [Theory]
    [InlineData("Mr Michael Weir, DOB 20/09/1970, is 43 years old.")]
    [InlineData("Mr Michael Weir was born on 20 September 1970.\nHe is a 43-year-old teacher.")]
    [InlineData("Date of birth 20 September 1970 (age 43).")]
    public void SA_G7_ReLineAgeWhenNoDob_Is_Silent_When_The_Notes_Carry_A_Dob(string notes)
        // DOB has priority (OA3-03): the DOB rule owns these Re: lines, even
        // when the letter's Re: line (synthetic variant) carries no DOB.
        => AssertRuleDoesNotFire(Lint(WeirWithoutDob, "LT-RR", caseNotes: notes), AgeCheck);

    [Theory]
    [InlineData(WeirCaseNotes)]
    [InlineData("Mr Michael Weir had an appendectomy at age 15.")]
    [InlineData("Mr Michael Weir lives with his 12-year-old son.")]
    [InlineData("His father died aged 75. His mother, aged 76, is hypertensive.")]
    [InlineData("The patient is aged 18 months.")]
    public void SA_G7_ReLineAgeWhenNoDob_Relative_Past_Event_And_Month_Ages_Never_Count(string notes)
        => AssertRuleDoesNotFire(Lint(WeirWithoutDob, "LT-RR", caseNotes: notes), AgeCheck);

    [Fact]
    public void SA_G7_ReLineAgeWhenNoDob_A_Different_Stated_Age_Is_Left_To_The_Identity_Rule()
    {
        var letter = Inject(WeirWithoutDob, "Re: Mr Michael Weir", "Re: Mr Michael Weir, aged 60");
        var findings = Lint(letter, "LT-RR", caseNotes: MathisAgeNotes);
        AssertRuleDoesNotFire(findings, AgeCheck);
        AssertRuleFires(findings, "re_line_identity_unsupported");
    }

    [Fact]
    public void SA_G7_ReLineAgeWhenNoDob_Canonical_Weir_Re_Line_With_Dob_Is_Left_To_The_Dob_Rules()
        // The canonical Weir letter carries its source DOB; the age rule never
        // asks for "aged N" on a Re: line that already identifies by DOB.
        => AssertRuleDoesNotFire(Lint(Weir, "LT-RR", caseNotes: MathisAgeNotes), AgeCheck);

    [Fact]
    public void SA_G7_ReLineAgeWhenNoDob_Is_Silent_Without_Notes()
        => AssertRuleDoesNotFire(Lint(WeirWithoutDob, "LT-RR"), AgeCheck);

    [Fact]
    public void SA_G7_ReLineAgeWhenNoDob_Candidate_Lane_Is_Never_Flagged()
        => AssertRuleDoesNotFire(Lint(WeirWithoutDob, "LT-RR", caseNotes: MathisAgeNotes, isModelAnswer: false), AgeCheck);

    [Fact]
    public void SA_G7_ReLineAgeWhenNoDob_Clean_Fixtures_Stay_Clean()
    {
        AssertRuleDoesNotFire(Lint(Garcia, "LT-DG", caseNotes: GarciaCaseNotes), AgeCheck);
        AssertRuleDoesNotFire(Lint(Taylor, "LT-UR", caseNotes: TaylorCaseNotes), AgeCheck);
        AssertRuleDoesNotFire(Lint(Weston, "LT-NM", caseNotes: WestonCaseNotes), AgeCheck);
        AssertRuleDoesNotFire(Lint(Weir, "LT-RR", caseNotes: WeirCaseNotes), AgeCheck);
        AssertRuleDoesNotFire(Lint(McDonald, "LT-TR", caseNotes: McDonaldCaseNotes), AgeCheck);
    }

    // ─────────────────────────────────────────────────────────────────
    // address_content_unsupported
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SA_G7_AddressContentUnsupported_Invented_Recipient_Fires_In_Model_Answer()
    {
        // Brendan Macalaque: the task and notes only say "a
        // neuro-ophthalmologist"; the live letter invented the rest.
        var letter = Inject(Weir, WeirAddress, MacalaqueInventedAddress);
        var quotes = FindingsFor(Lint(letter, "LT-RR", caseNotes: MacalaqueCaseNotes, taskText: MacalaqueTaskText), AddressCheck)
            .Select(f => f.Quote).ToList();
        Assert.Contains("Dr Helena Rao", quotes);
        Assert.Contains("City Eye Centre", quotes);
        Assert.Contains("45 Bridge Street", quotes);
        Assert.DoesNotContain("Neuro-Ophthalmologist", quotes);
    }

    [Fact]
    public void SA_G7_AddressContentUnsupported_Dropped_Post_Nominals_Fire()
    {
        // Jane MacIntyre: task "Dr Anne Childers MBBS FRANZCOG", letter
        // "Dr Anne Childers".
        var letter = Inject(Weir, WeirAddress,
            "Dr Anne Childers\nConsultant Obstetrician\nSpirit Mother's Hospital\nStanley Street\nSouth Brisbane");
        var finding = Assert.Single(FindingsFor(Lint(letter, "LT-RR", taskText: MacIntyreTaskText), AddressCheck));
        Assert.Equal("Dr Anne Childers", finding.Quote);
        Assert.Contains("MBBS FRANZCOG", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SA_G7_AddressContentUnsupported_Dropped_Postcode_Fires()
    {
        // Erika Stone: task "11 Lake Street, London, NW1 2TG", letter stops at
        // "London".
        var letter = Inject(Weir, WeirAddress,
            "Dr Maria Foreman\nEndocrinologist\nEndocrinology Department\nRoyal Restricted Hospital\n11 Lake Street\nLondon");
        var finding = Assert.Single(FindingsFor(Lint(letter, "LT-RR", taskText: StoneTaskText), AddressCheck));
        Assert.Equal("London", finding.Quote);
        Assert.Contains("NW1 2TG", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SA_G7_AddressContentUnsupported_Respelled_Task_Component_Fires()
    {
        // Brian Morgan: the task spells "Wooloongabba"; source fidelity keeps
        // the task's spelling (DECISIONS §C16).
        var letter = Inject(Weir, WeirAddress, "Dr S Leyshon\nEmergency Department Medical Officer\nPA Hospital\nWoolloongabba");
        var finding = Assert.Single(FindingsFor(Lint(letter, "LT-RR", taskText: MorganTaskText), AddressCheck));
        Assert.Equal("Woolloongabba", finding.Quote);
    }

    [Fact]
    public void SA_G7_AddressContentUnsupported_Exact_Task_Recipients_Pass()
    {
        var childers = Inject(Weir, WeirAddress,
            "Dr Anne Childers MBBS FRANZCOG\nConsultant Obstetrician\nSpirit Mother's Hospital\nStanley Street\nSouth Brisbane");
        AssertRuleDoesNotFire(Lint(childers, "LT-RR", taskText: MacIntyreTaskText), AddressCheck);

        var foreman = Inject(Weir, WeirAddress,
            "Dr Maria Foreman\nEndocrinologist\nEndocrinology Department\nRoyal Restricted Hospital\n11 Lake Street\nLondon\nNW1 2TG");
        AssertRuleDoesNotFire(Lint(foreman, "LT-RR", taskText: StoneTaskText), AddressCheck);

        var leyshon = Inject(Weir, WeirAddress, "Dr S Leyshon\nEmergency Department Medical Officer\nPA Hospital\nWooloongabba");
        AssertRuleDoesNotFire(Lint(leyshon, "LT-RR", taskText: MorganTaskText), AddressCheck);
    }

    [Fact]
    public void SA_G7_AddressContentUnsupported_Role_Only_Task_Supports_A_Role_Line()
    {
        var letter = Inject(Weir, WeirAddress, "Neuro-Ophthalmologist");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR", caseNotes: MacalaqueCaseNotes, taskText: MacalaqueTaskText), AddressCheck);
    }

    [Fact]
    public void SA_G7_AddressContentUnsupported_Abbreviation_Expansion_Passes()
    {
        // Yanlin Ma: the task writes "GP"; the letter's "General Practitioner"
        // is the same component.
        var letter = Inject(Weir, WeirAddress, "Dr Janine McArdle\nGeneral Practitioner\nUniversity Medical Centre\nSt Lucia");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR", taskText: MaTaskText), AddressCheck);
    }

    [Fact]
    public void SA_G7_AddressContentUnsupported_Case_Punctuation_And_Line_Splits_Pass()
    {
        // Louise Geller: "Moliers hill Stanbridge." with stray spaces before
        // commas becomes "Moliers Hill" / "Stanbridge".
        var letter = Inject(Weir, WeirAddress, "Dr Rania Yates\nConsultant Endocrinologist\nStanbridge Hospital\nMoliers Hill\nStanbridge");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR", taskText: GellerTaskText), AddressCheck);
    }

    [Fact]
    public void SA_G7_AddressContentUnsupported_Is_Silent_Without_A_Recipient_Instruction()
    {
        var letter = Inject(Weir, WeirAddress, MacalaqueInventedAddress);
        AssertRuleDoesNotFire(Lint(letter, "LT-RR", taskText: "Refer Mr Weir."), AddressCheck);
        AssertRuleDoesNotFire(Lint(letter, "LT-RR", caseNotes: MacalaqueCaseNotes), AddressCheck);
    }

    [Fact]
    public void SA_G7_AddressContentUnsupported_Candidate_Lane_Is_Never_Flagged()
    {
        var letter = Inject(Weir, WeirAddress, MacalaqueInventedAddress);
        AssertRuleDoesNotFire(
            Lint(letter, "LT-RR", caseNotes: MacalaqueCaseNotes, taskText: MacalaqueTaskText, isModelAnswer: false),
            AddressCheck);
    }

    [Fact]
    public void SA_G7_AddressContentUnsupported_Clean_Fixtures_Stay_Clean()
    {
        AssertRuleDoesNotFire(Lint(Garcia, "LT-DG", caseNotes: GarciaCaseNotes, taskText: GarciaTaskText), AddressCheck);
        AssertRuleDoesNotFire(Lint(Taylor, "LT-UR", caseNotes: TaylorCaseNotes, taskText: TaylorTaskText), AddressCheck);
        AssertRuleDoesNotFire(Lint(Weston, "LT-NM", caseNotes: WestonCaseNotes, taskText: WestonTaskText), AddressCheck);
        AssertRuleDoesNotFire(Lint(Weir, "LT-RR", caseNotes: WeirCaseNotes, taskText: WeirTaskText), AddressCheck);
        AssertRuleDoesNotFire(Lint(McDonald, "LT-TR", caseNotes: McDonaldCaseNotes, taskText: McDonaldTaskText), AddressCheck);
    }

    // ─────────────────────────────────────────────────────────────────
    // role_salutation_matches_task — bare role recipient (Model Answer)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SA_G7_RoleSalutationBareRole_Dear_Doctor_For_A_Bare_Role_Fires_In_Model_Answer()
    {
        // Cathy Jones: recipient "Gynaecology Registrar" (no "The"), salutation
        // "Dear Doctor,".
        var letter = Inject(Inject(McDonald, McDonaldAddress, CathyJonesAddress), "Dear Admissions Officer,", "Dear Doctor,");
        var finding = Assert.Single(FindingsFor(Lint(letter, "LT-UR", taskText: CathyJonesTaskText), RoleCheck));
        Assert.Equal("Dear Gynaecology Registrar,", finding.FixSuggestion);
        Assert.Equal("Dear Doctor,", finding.Quote);
    }

    [Fact]
    public void SA_G7_RoleSalutationBareRole_Exact_Role_Salutation_Passes()
    {
        var letter = Inject(Inject(McDonald, McDonaldAddress, CathyJonesAddress), "Dear Admissions Officer,", "Dear Gynaecology Registrar,");
        AssertRuleDoesNotFire(Lint(letter, "LT-UR", taskText: CathyJonesTaskText), RoleCheck);
    }

    [Fact]
    public void SA_G7_RoleSalutationBareRole_Named_Recipient_Keeps_The_Personal_Salutation()
    {
        var named = Inject(Inject(McDonald, McDonaldAddress,
            "Dr Phillip Wright\nSurgical Registrar\nBrisbane General Hospital\n765 Brunswick St\nValley\nQLD 4444"),
            "Dear Admissions Officer,", "Dear Dr Wright,");
        AssertRuleDoesNotFire(Lint(named, "LT-UR", taskText: MarcusTaskText), RoleCheck);

        // The task names the person holding the role: the fix is the name
        // (address_content_unsupported's finding), never "Dear Surgical Registrar,".
        var nameDropped = Inject(Inject(McDonald, McDonaldAddress,
            "Surgical Registrar\nBrisbane General Hospital\n765 Brunswick St\nValley\nQLD 4444"),
            "Dear Admissions Officer,", "Dear Doctor,");
        var findings = Lint(nameDropped, "LT-UR", taskText: MarcusTaskText);
        AssertRuleDoesNotFire(findings, RoleCheck);
        AssertRuleFires(findings, AddressCheck);
    }

    [Fact]
    public void SA_G7_RoleSalutationBareRole_Unlisted_Or_Unsupported_Roles_Stay_Silent()
    {
        // Allen Mathis: "Cardiologist" is not a role-salutation form, so
        // "Dear Doctor," stays correct.
        var mathis = Inject(Inject(McDonald, McDonaldAddress, "Cardiologist\nEmergency Department\nCentral Hospital\nStillwater"),
            "Dear Admissions Officer,", "Dear Doctor,");
        AssertRuleDoesNotFire(Lint(mathis, "LT-UR", taskText: MathisTaskText), RoleCheck);

        // The role must be corroborated by the exact task.
        var uncorroborated = Inject(Inject(McDonald, McDonaldAddress, CathyJonesAddress), "Dear Admissions Officer,", "Dear Doctor,");
        AssertRuleDoesNotFire(Lint(uncorroborated, "LT-UR", taskText: WeirTaskText), RoleCheck);
    }

    [Fact]
    public void SA_G7_RoleSalutationBareRole_Never_Duplicates_The_The_Role_Finding()
    {
        // "The Admissions Officer" + "Dear Sir/Madam," is the existing
        // detector's shape: exactly one finding, never a second from the
        // bare-role branch.
        var letter = Inject(McDonald, "Dear Admissions Officer,", "Dear Sir/Madam,");
        Assert.Single(FindingsFor(Lint(letter, "LT-TR", taskText: McDonaldTaskText), RoleCheck));
    }

    [Fact]
    public void SA_G7_RoleSalutationBareRole_Candidate_Lane_Is_Never_Flagged()
    {
        var letter = Inject(Inject(McDonald, McDonaldAddress, CathyJonesAddress), "Dear Admissions Officer,", "Dear Doctor,");
        AssertRuleDoesNotFire(Lint(letter, "LT-UR", taskText: CathyJonesTaskText, isModelAnswer: false), RoleCheck);
    }

    [Fact]
    public void SA_G7_RoleSalutationBareRole_Clean_Fixtures_Stay_Clean()
    {
        AssertRuleDoesNotFire(Lint(Garcia, "LT-DG", caseNotes: GarciaCaseNotes, taskText: GarciaTaskText), RoleCheck);
        AssertRuleDoesNotFire(Lint(Taylor, "LT-UR", caseNotes: TaylorCaseNotes, taskText: TaylorTaskText), RoleCheck);
        AssertRuleDoesNotFire(Lint(Weston, "LT-NM", caseNotes: WestonCaseNotes, taskText: WestonTaskText), RoleCheck);
        AssertRuleDoesNotFire(Lint(Weir, "LT-RR", caseNotes: WeirCaseNotes, taskText: WeirTaskText), RoleCheck);
        AssertRuleDoesNotFire(Lint(McDonald, "LT-TR", caseNotes: McDonaldCaseNotes, taskText: McDonaldTaskText), RoleCheck);
    }
}
