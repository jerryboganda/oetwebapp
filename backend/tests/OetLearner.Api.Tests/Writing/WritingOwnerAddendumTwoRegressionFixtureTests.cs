using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Permanent regression classes R2-01..R2-18 for the FINAL WRITING OWNER
/// CLARIFICATIONS ADDENDUM TWO (14 Sep 2026, OA2-01..OA2-20), plus the three
/// supplementary classes this round added (unsupported Re:-line identity,
/// adverbial "also", role salutation with "Yours faithfully,").
///
/// OA2-01 is the governing principle: "If a visible defect remains while the
/// validator says PASS / 0 findings, treat that as a validator failure, not a
/// clean letter." So every class below proves BOTH halves — the injected
/// defect is DETECTED, and the deliberately valid alternative PASSES with no
/// false positive. The fixtures are the same five canonical Medicine letters
/// used by <see cref="WritingRev8RegressionFixtureTests"/> so the two
/// batteries can never drift apart, and every injection asserts that its
/// needle actually changed the letter — a stale needle fails loudly instead of
/// silently linting a pristine fixture.
/// </summary>
public sealed class WritingOwnerAddendumTwoRegressionFixtureTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());

    private static List<LintFinding> Lint(string letter, string letterType,
        string? caseNotes = null, string? taskText = null, WritingCaseNotesMarkers? markers = null,
        bool isModelAnswer = true)
        => Engine.Lint(new WritingLintInput(
            LetterText: letter,
            LetterType: letterType,
            CaseNotesMarkers: markers,
            CaseNotesText: caseNotes,
            TaskText: taskText,
            Profession: ExamProfession.Medicine,
            IsModelAnswer: isModelAnswer)).ToList();

    private static void AssertRuleFires(List<LintFinding> findings, string checkId)
        => Assert.Contains(findings, f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal));

    private static void AssertRuleDoesNotFire(List<LintFinding> findings, string checkId)
        => Assert.DoesNotContain(findings, f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal));

    /// <summary>
    /// Applies an injection and PROVES it landed. Without this a fixture
    /// rewrite turns Replace(...) into a silent no-op and the test starts
    /// linting the pristine letter — the exact failure mode that let defective
    /// letters read as clean.
    /// </summary>
    private static string Inject(string letter, string find, string replace)
    {
        var injected = letter.Replace(find, replace);
        Assert.NotEqual(letter, injected);
        return injected;
    }

    // Canonical clean baselines — single source of truth.
    private const string Garcia = WritingRev8RegressionFixtureTests.GarciaUpdateLetter;        // LT-DG
    private const string Weston = WritingRev8RegressionFixtureTests.WestonReferralLetter;      // LT-NM
    private const string Weir = WritingRev8RegressionFixtureTests.WeirRoutineReferralLetter;   // LT-RR
    private const string McDonald = WritingRev8RegressionFixtureTests.McDonaldTransferLetter;  // LT-TR
    private const string Taylor = WritingRev8RegressionFixtureTests.TaylorUrgentReferralLetter;// LT-UR

    // The canonical Garcia notes document NO admission and NO discharge — the
    // Addendum Two source resolution for the LT-DG cell.
    private const string GarciaCaseNotes =
        "Patient was referred to the Emergency Department by her GP, Dr Bradbury. " +
        "Date of birth 01.01.1995 (age 20). " +
        "Presented on 23 May 2015 with painful, stiff joints for one week. " +
        "White cell count 14.0x10^9/L. C-reactive protein 150. " +
        "Culture identified Neisseria meningitidis. Diagnosis: bacterial meningitis.";

    // The canonical Weir notes, reconciled against the SOURCE PDF (Senior
    // Assessor Release Audit, 16 Sep 2026, DECISIONS §C.1): the source records
    // "Mr Michael Weir (DOB: 20 Sep 1970)" — the production notes lost the DOB
    // in extraction — and no age for the patient, only the CHILDREN's ages.
    // They carry the 9 August 2014 blood pressure that OA2-14 requires the
    // letter to preserve.
    private const string WeirCaseNotes =
        "Mr Michael Weir (DOB: 20 Sep 1970) is a patient in your general practice, height 183cm. " +
        "He is married with 3 children aged 13, 10 and 8. " +
        "He has depression, treated with sertraline hydrochloride (Zoloft) since September 2012. " +
        "On 09.08.14 he complained of dizziness and two recent blackouts lasting a few minutes each. " +
        "Examination on 09.08.14: BP 88/70, HR 76bpm, BMI 28 (93.7kg), chest clear. " +
        "Cholesterol was 6.37mmol/L. He is a smoker. He has been overweight long term.";

    // SYNTHETIC notes that deliberately record NO date of birth (the real Weir
    // source has one). They exist only to prove the rule "never invent a DOB
    // the notes lack" and are paired with the synthetic DOB-less letter.
    private const string WeirSyntheticNotesWithoutDob =
        "Mr Michael Weir is a patient in your general practice, height 183cm. " +
        "He is married with 3 children aged 13, 10 and 8. " +
        "He has depression, treated with sertraline hydrochloride (Zoloft) since September 2012. " +
        "On 09.08.14 he complained of dizziness and two recent blackouts lasting a few minutes each. " +
        "Examination on 09.08.14: BP 88/70, HR 76bpm, BMI 28 (93.7kg), chest clear. " +
        "Cholesterol was 6.37mmol/L. He is a smoker. He has been overweight long term.";

    private static readonly string WeirWithoutDob = WritingRev8RegressionFixtureTests.WeirRoutineReferralLetterWithoutDob;

    private const string TaylorTaskText =
        "Using the information given in the case notes, write a letter of referral to Dr Still, a Rheumatologist at City Hospital, " +
        "for assessment of Mr Taylor. Address the letter to Dr Malcom Still, Rheumatologist, City Hospital, Suite 32, 55 Main Road, Newtown.";

    private const string McDonaldTaskText =
        "Mr McDonald was admitted 4 days ago for knee surgery at the Alfred Hospital where you work. " +
        "Using the information in the case notes, write a transfer letter to the Admissions Officer at Cabrini Hopetoun Rehabilitation, " +
        "2-6 Hopetoun Street, Elsternwick, Vic 3185, for Mr McDonald's immediate treatment.";

    private static readonly WritingCaseNotesMarkers NoAdmissionOrDischarge = new();

    private static readonly WritingCaseNotesMarkers AdmissionAndDischargeProven =
        new(AdmissionDocumented: true, DischargeDocumented: true);

    // ─────────────────────────────────────────────────────────────────
    // Baselines: the five canonical letters stay clean under the OA2 pack.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Garcia_Update_Lints_Clean_Under_Addendum_Two()
        => Assert.Empty(Lint(Garcia, "LT-DG", caseNotes: GarciaCaseNotes));

    [Fact]
    public void Weston_Referral_Lints_Clean_Under_Addendum_Two()
        => Assert.Empty(Lint(Weston, "LT-NM"));

    [Fact]
    public void Weir_Routine_Referral_Lints_Clean_Under_Addendum_Two()
        => Assert.Empty(Lint(Weir, "LT-RR", caseNotes: WeirCaseNotes));

    [Fact]
    public void McDonald_Transfer_Lints_Clean_Under_Addendum_Two()
        => Assert.Empty(Lint(McDonald, "LT-TR", taskText: McDonaldTaskText));

    [Fact]
    public void Taylor_Urgent_Referral_Lints_Clean_Under_Addendum_Two()
        => Assert.Empty(Lint(Taylor, "LT-UR", taskText: TaylorTaskText));

    // ─────────────────────────────────────────────────────────────────
    // R2-01 — admission/discharge hallucination.  discharge_language_unsupported
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_01_Injected_Discharge_Language_Without_Admission_Evidence_Is_Flagged()
    {
        var letter = Inject(Taylor, "He reported shortness of breath.",
            "He reported shortness of breath and will be discharged into your care.");
        AssertRuleFires(Lint(letter, "LT-UR", markers: NoAdmissionOrDischarge),
            "discharge_language_unsupported");
    }

    [Fact]
    public void R2_01_Simple_Update_Wording_Passes_When_No_Admission_Is_Documented()
        => AssertRuleDoesNotFire(Lint(Taylor, "LT-UR", markers: NoAdmissionOrDischarge),
            "discharge_language_unsupported");

    // ─────────────────────────────────────────────────────────────────
    // R2-02 — missed discharge function.  discharge_function_missed
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_02_Vague_Simple_Update_Fails_When_Admission_And_Discharge_Are_Proven()
        => AssertRuleFires(Lint(Garcia, "LT-DG", markers: AdmissionAndDischargeProven),
            "discharge_function_missed");

    [Fact]
    public void R2_02_Update_On_Discharge_Wording_Passes_When_Admission_Is_Proven()
    {
        var letter = Inject(Garcia,
            "I am writing to update you regarding Ms Isabel Garcia's treatment for bacterial meningitis",
            "I am writing to update you regarding Ms Isabel Garcia, who was admitted with bacterial meningitis and is now discharged into your care");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG", markers: AdmissionAndDischargeProven),
            "discharge_function_missed");
    }

    // R2-02c — "under your care" is an equally valid discharge-function
    // phrase to "into your care"; DischargeFunctionMarkerRe used to accept
    // only the latter, so a source-faithful Model Answer using "under" was
    // wrongly told it had not stated the discharge function.
    [Fact]
    public void R2_02c_Under_Your_Care_Phrasing_Also_Satisfies_The_Discharge_Function()
    {
        var letter = Inject(Garcia,
            "I am writing to update you regarding Ms Isabel Garcia's treatment for bacterial meningitis",
            "I am writing to update you regarding Ms Isabel Garcia, who was admitted with bacterial meningitis and is now home under your care");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG", markers: AdmissionAndDischargeProven),
            "discharge_function_missed");
    }

    [Fact]
    public void R2_02_Is_Silent_When_The_Notes_Do_Not_Prove_Admission_And_Discharge()
        => AssertRuleDoesNotFire(Lint(Garcia, "LT-DG", markers: NoAdmissionOrDischarge),
            "discharge_function_missed");

    // R2-02b — the extractor itself, not a hand-built marker fixture. Every
    // R2-02 test above passes hand-constructed WritingCaseNotesMarkers, so
    // none of them exercise WritingCaseNotesMarkerExtractor.Derive() at all.
    // The extractor used to match bare venue nouns ("hospital", "ward") and
    // any "discharg\w*" substring, so an outpatient referral whose notes
    // merely name a hospital and hand over a discharge leaflet armed BOTH
    // markers with no admission or discharge episode ever having happened —
    // exactly the OA2-01 "visible defect + PASS" failure the addendum exists
    // to close, one layer below the detectors themselves.
    [Fact]
    public void R2_02b_Extractor_Does_Not_Infer_Admission_Or_Discharge_From_Outpatient_Wording()
    {
        var markers = WritingCaseNotesMarkerExtractor.Derive(
            "GP referred her to the outpatient clinic at City Hospital. Discharge advice leaflet given.");
        Assert.False(markers.AdmissionDocumented);
        Assert.False(markers.DischargeDocumented);
    }

    [Fact]
    public void R2_02b_Extractor_Still_Proves_A_Genuine_Admission_And_Discharge()
    {
        var markers = WritingCaseNotesMarkerExtractor.Derive(
            "Mrs Jones was admitted with pneumonia and treated with IV antibiotics. " +
            "She is now fit for discharge and will return home today under your ongoing care.");
        Assert.True(markers.AdmissionDocumented);
        Assert.True(markers.DischargeDocumented);
    }

    [Fact]
    public void R2_02b_Extractor_Recognises_The_Noun_Form_Admission()
        => Assert.True(WritingCaseNotesMarkerExtractor.Derive("Admission date 20/7/18 for elective knee replacement.").AdmissionDocumented);

    [Fact]
    public void R2_02b_Extractor_Does_Not_Infer_Admission_From_Transfer_Or_Post_Op_History_Alone()
    {
        var markers = WritingCaseNotesMarkerExtractor.Derive(
            "Transfer date 24/7/18. Reason for transfer: rehabilitation care. Post-operative review planned.");
        Assert.False(markers.AdmissionDocumented);
    }

    // ─────────────────────────────────────────────────────────────────
    // R2-03 — vague purpose / buried action.  intro_purpose_vague
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_03_Topic_Only_Introduction_Is_Flagged()
    {
        var letter = Inject(Garcia,
            "I am writing to update you regarding Ms Isabel Garcia's treatment for bacterial meningitis and request follow-up of close contacts.",
            "I am writing to update you regarding Ms Isabel Garcia's diagnosis and treatment for bacterial meningitis.");
        AssertRuleFires(Lint(letter, "LT-DG"), "intro_purpose_vague");
    }

    [Fact]
    public void R2_03_Introduction_Naming_The_Reader_Action_Passes()
        => AssertRuleDoesNotFire(Lint(Garcia, "LT-DG"), "intro_purpose_vague");

    // ─────────────────────────────────────────────────────────────────
    // R2-04 — narrative semicolon.  semicolon_overuse
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_04_Injected_Narrative_Semicolon_Is_Flagged()
    {
        var letter = Inject(Garcia,
            "twice daily. Dexamethasone was continued six-hourly",
            "twice daily; dexamethasone was continued six-hourly");
        AssertRuleFires(Lint(letter, "LT-DG"), "semicolon_overuse");
    }

    [Fact]
    public void R2_04_A_Single_Prose_Semicolon_Is_Enough_To_Fail()
    {
        var letter = Inject(Taylor,
            "in September 2010. Kidney stones were also noted that year.",
            "in September 2010; kidney stones were noted that year.");
        AssertRuleFires(Lint(letter, "LT-UR"), "semicolon_overuse");
    }

    [Fact]
    public void R2_04_Medication_List_Semicolons_Are_Not_Narrative_Semicolons()
        => AssertRuleDoesNotFire(Lint(McDonald, "LT-TR"), "semicolon_overuse");

    [Fact]
    public void R2_04_Candidate_Semicolon_Control_Is_Model_Answer_Only()
    {
        var letter = Inject(Taylor,
            "in September 2010. Kidney stones were also noted that year.",
            "in September 2010; kidney stones were noted that year.");
        AssertRuleDoesNotFire(Lint(letter, "LT-UR", isModelAnswer: false), "semicolon_overuse");
    }

    // ─────────────────────────────────────────────────────────────────
    // R2-05 — lab/result noun failure.  result_noun_fragment
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_05_Injected_Bare_White_Cells_Is_Flagged()
    {
        var letter = Inject(Garcia, "a white cell count at 1000 with", "1000 white cells with");
        AssertRuleFires(Lint(letter, "LT-DG"), "result_noun_fragment");
    }

    [Fact]
    public void R2_05_Count_With_Its_Measurement_Noun_Passes()
        => AssertRuleDoesNotFire(Lint(Garcia, "LT-DG"), "result_noun_fragment");

    // ─────────────────────────────────────────────────────────────────
    // R2-06 — the validator must NOT force "at" or "of" by regex.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_06_Was_Form_Of_A_Result_Passes()
    {
        // "the white cell count was ..." — a complete result noun with a
        // finite verb stays correct under OA3-04.
        AssertRuleDoesNotFire(Lint(Garcia, "LT-DG"), "result_head_noun");
        AssertRuleDoesNotFire(Lint(Garcia, "LT-DG"), "result_noun_fragment");
        AssertRuleDoesNotFire(Lint(Garcia, "LT-DG"), "result_at_wording");
    }

    [Fact]
    public void R2_06_Of_Form_Is_A_Candidate_Alternative_But_Fails_The_Model_Answer()
    {
        // OA3-04 (15 Sep 2026) supersedes the old "never force at" scope: the
        // of-form is a CANDIDATE alternative, but the canonical Model Answer
        // uses the owner's at-construction on a complete result noun.
        var letter = Inject(Garcia,
            "The white cell count was 14.0x10^9/L",
            "Blood tests showed a white cell count of 14.0x10^9/L");
        AssertRuleFires(Lint(letter, "LT-DG"), "result_at_wording");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG", isModelAnswer: false), "result_at_wording");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG"), "result_head_noun");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG"), "result_noun_fragment");
    }

    [Fact]
    public void R2_06_Both_Vital_Sign_Prepositions_Pass()
    {
        AssertRuleDoesNotFire(Lint(Taylor, "LT-UR"), "result_head_noun");
        var letter = Inject(Taylor, "a temperature of 37.8 °C", "the temperature was 37.8 °C");
        AssertRuleDoesNotFire(Lint(letter, "LT-UR"), "result_head_noun");
    }

    [Fact]
    public void R2_06_Headless_Result_Noun_Is_Still_Flagged()
    {
        var letter = Inject(Weir,
            "The cholesterol level was 6.37 mmol/L",
            "Investigations showed a cholesterol of 6.37 mmol/L");
        AssertRuleFires(Lint(letter, "LT-RR"), "result_head_noun");
    }

    // ─────────────────────────────────────────────────────────────────
    // R2-07 — supine phrasing.  supine_position_wording
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_07_Injected_On_Supine_Position_Is_Flagged()
    {
        var letter = Inject(Garcia, "to her chest when supine", "to her chest on supine position");
        AssertRuleFires(Lint(letter, "LT-DG"), "supine_position_wording");
    }

    [Fact]
    public void R2_07_When_Supine_Passes()
        => AssertRuleDoesNotFire(Lint(Garcia, "LT-DG"), "supine_position_wording");

    [Fact]
    public void R2_07_In_The_Supine_Position_Passes()
    {
        var letter = Inject(Garcia, "to her chest when supine", "to her chest in the supine position");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG"), "supine_position_wording");
    }

    [Fact]
    public void R2_07_While_Lying_Supine_Passes()
    {
        var letter = Inject(Garcia, "to her chest when supine", "to her chest when lying supine");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG"), "supine_position_wording");
    }

    // ─────────────────────────────────────────────────────────────────
    // R2-08 — the specific request buried in a body paragraph.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_08_Request_Merged_Into_A_Body_Paragraph_Is_Flagged()
    {
        var letter = Inject(Garcia,
            "family immunisation was discussed.\n\nI would be grateful",
            "family immunisation was discussed. I would be grateful");
        AssertRuleFires(Lint(letter, "LT-DG"), "closure_request_paragraph");
    }

    [Fact]
    public void R2_08_Request_In_Its_Own_Closure_Paragraph_Passes()
        => AssertRuleDoesNotFire(Lint(Garcia, "LT-DG"), "closure_request_paragraph");

    // R2-08b — the request paragraph's POSITION, not just its own contents.
    // The checks in DetectClosureRequestParagraph only ever examined
    // BodyParagraphs[^1], so a request paragraph that IS well-formed but sits
    // two or more paragraphs before the end — with real clinical content
    // stranded after it — linted 100% clean. Swaps the request paragraph and
    // the public-health paragraph so the request becomes the THIRD-from-last
    // body paragraph while the letter otherwise stays word-for-word
    // identical and every other rule (naming, grammar, semicolons,
    // medication syntax) still passes.
    [Fact]
    public void R2_08b_Request_Paragraph_Stranded_Mid_Letter_Is_Flagged()
    {
        var letter = Inject(Garcia,
            "The Department of Human Services was notified, and family immunisation was discussed.\n\n"
            + "I would be grateful if you could contact Ms Garcia's close contacts, advise them to seek prompt care if unwell and consider chemoprophylaxis.",
            "I would be grateful if you could contact Ms Garcia's close contacts, advise them to seek prompt care if unwell and consider chemoprophylaxis.\n\n"
            + "The Department of Human Services was notified, and family immunisation was discussed.");
        AssertRuleFires(Lint(letter, "LT-DG"), "closure_request_paragraph");
    }

    // ─────────────────────────────────────────────────────────────────
    // R2-09 — duplicate request.  no_duplicated_request
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_09_Closure_Repeating_The_Introduction_Is_Flagged()
    {
        var letter = Inject(McDonald,
            "I would be grateful if you could confirm Mr McDonald's admission for immediate rehabilitation.",
            "I would be grateful if you could transfer Mr McDonald to your rehabilitation service without delay.");
        AssertRuleFires(Lint(letter, "LT-TR"), "no_duplicated_request");
    }

    [Fact]
    public void R2_09_A_Distinct_Closure_Action_Passes()
        => AssertRuleDoesNotFire(Lint(McDonald, "LT-TR"), "no_duplicated_request");

    // ─────────────────────────────────────────────────────────────────
    // R2-10 — background too early.  background_paragraph_placement
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_10_Habits_In_The_Current_Problem_Paragraph_Are_Flagged()
    {
        var letter = Inject(McDonald,
            "A forty-eight-hour ketamine infusion was effective.",
            "He smokes twenty cigarettes daily and drinks six to ten standard drinks daily. A forty-eight-hour ketamine infusion was effective.");
        AssertRuleFires(Lint(letter, "LT-TR"), "background_paragraph_placement");
    }

    [Fact]
    public void R2_10_Medical_History_Label_In_The_Current_Problem_Paragraph_Is_Flagged()
    {
        var letter = Inject(Weir,
            "Examination revealed sensory loss",
            "His past medical history includes depression. Examination revealed sensory loss");
        AssertRuleFires(Lint(letter, "LT-RR"), "background_paragraph_placement");
    }

    [Fact]
    public void R2_10_Dedicated_Background_Paragraph_Before_The_Closure_Passes()
    {
        AssertRuleDoesNotFire(Lint(McDonald, "LT-TR"), "background_paragraph_placement");
        AssertRuleDoesNotFire(Lint(Weir, "LT-RR"), "background_paragraph_placement");
        AssertRuleDoesNotFire(Lint(Weston, "LT-NM"), "background_paragraph_placement");
    }

    [Fact]
    public void R2_10_A_Presenting_Complaint_History_Is_Not_Background()
        // "a three-week history of numbness" in Weston's current-problem
        // paragraph must never read as medical-history background.
        => AssertRuleDoesNotFire(Lint(Weston, "LT-NM"), "background_paragraph_placement");

    [Fact]
    public void R2_10_Candidate_Background_Placement_Is_Model_Answer_Only()
    {
        var letter = Inject(McDonald,
            "A forty-eight-hour ketamine infusion was effective.",
            "He smokes twenty cigarettes daily. A forty-eight-hour ketamine infusion was effective.");
        AssertRuleDoesNotFire(Lint(letter, "LT-TR", isModelAnswer: false), "background_paragraph_placement");
    }

    // ─────────────────────────────────────────────────────────────────
    // R2-11 — descriptive number as a digit.  number_style_words_vs_digits
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_11_Injected_Cigarette_Digit_Is_Flagged()
    {
        var letter = Inject(McDonald, "He smokes twenty cigarettes daily", "He smokes 20 cigarettes daily");
        AssertRuleFires(Lint(letter, "LT-TR"), "number_style_words_vs_digits");
    }

    [Fact]
    public void R2_11_Injected_Digit_Duration_Before_A_Procedure_Noun_Is_Flagged()
    {
        var letter = Inject(McDonald, "A forty-eight-hour ketamine infusion", "A 48-hour ketamine infusion");
        AssertRuleFires(Lint(letter, "LT-TR"), "number_style_words_vs_digits");
    }

    [Fact]
    public void R2_11_Words_For_Descriptive_Numbers_Pass()
        => AssertRuleDoesNotFire(Lint(McDonald, "LT-TR"), "number_style_words_vs_digits");

    [Fact]
    public void R2_11_Spelled_Out_Count_Still_Carries_Its_Frequency_Check()
    {
        // OA2-15 must not silently kill lifestyle_frequency_precision: the
        // frequency is still mandatory once the number is a word.
        var letter = Inject(McDonald, "He smokes twenty cigarettes daily", "He smokes twenty cigarettes");
        AssertRuleFires(Lint(letter, "LT-TR"), "lifestyle_frequency_precision");
        AssertRuleDoesNotFire(Lint(McDonald, "LT-TR"), "lifestyle_frequency_precision");
    }

    [Fact]
    public void R2_11_Candidate_Digit_Form_Is_Never_An_Automatic_Penalty()
        => Assert.Equal(WritingCandidateBehaviors.CoachingOnly,
            WritingRuleProvenance.For("number_style_words_vs_digits").CandidateBehavior);

    // ─────────────────────────────────────────────────────────────────
    // R2-12 — medication final separator.  medication_list_punctuation
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_12_Semicolon_Before_The_Final_And_Is_Flagged()
    {
        var letter = Inject(McDonald,
            "Karvina, 300 mg daily and Nicabate patch, 21 mg.",
            "Karvina, 300 mg daily; and Nicabate patch, 21 mg.");
        AssertRuleFires(Lint(letter, "LT-TR"), "medication_list_punctuation");
    }

    [Fact]
    public void R2_12_No_Semicolon_Before_The_Final_And_Passes()
        => AssertRuleDoesNotFire(Lint(McDonald, "LT-TR"), "medication_list_punctuation");

    // ─────────────────────────────────────────────────────────────────
    // R2-13 — role-based salutation.  role_salutation_matches_task
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_13_Dear_Sir_Madam_For_A_Named_Role_Is_Flagged()
    {
        var letter = Inject(McDonald, "Dear Admissions Officer,", "Dear Sir/Madam,");
        AssertRuleFires(Lint(letter, "LT-TR", taskText: McDonaldTaskText), "role_salutation_matches_task");
    }

    [Fact]
    public void R2_13_Invented_Role_Synonym_Is_Flagged()
    {
        var letter = Inject(McDonald, "Dear Admissions Officer,", "Dear Admitting Officer,");
        AssertRuleFires(Lint(letter, "LT-TR", taskText: McDonaldTaskText), "role_salutation_matches_task");
    }

    [Fact]
    public void R2_13_Exact_Role_Salutation_Passes()
        => AssertRuleDoesNotFire(Lint(McDonald, "LT-TR", taskText: McDonaldTaskText),
            "role_salutation_matches_task");

    [Fact]
    public void R2_13_A_Role_Salutation_Still_Closes_Yours_Faithfully()
        // A role is not a personal name: reading it as one used to fire
        // yours_sincerely_vs_faithfully (Critical) on a correct transfer letter.
        => AssertRuleDoesNotFire(Lint(McDonald, "LT-TR", taskText: McDonaldTaskText),
            "yours_sincerely_vs_faithfully");

    [Fact]
    public void R2_13_Is_Silent_Without_The_Exact_Task()
    {
        // The rule can only prove a role mismatch against the exact task, so
        // it must stay silent without one — exactly like
        // letter_date_unsupported / recipient_name_mismatch /
        // re_line_identity_unsupported. Missing this gate let the rule fire
        // on every real CANDIDATE submission: WritingEvaluationPipeline's
        // production Lint() call never supplies TaskText, so a candidate who
        // correctly wrote "Dear Sir/Madam," to a recipient block that happens
        // to contain a role line was flagged regardless — exactly what
        // OA2-20's firewall (item 23) exists to prevent.
        var letter = McDonald.Replace("Dear Admissions Officer,", "Dear Sir/Madam,");
        Assert.NotEqual(McDonald, letter);
        AssertRuleDoesNotFire(Lint(letter, "LT-TR", taskText: null), "role_salutation_matches_task");
    }

    [Fact]
    public void R2_13_Candidate_Without_Task_Text_Is_Never_Penalised_For_A_Generic_Salutation()
    {
        // The exact scenario the live candidate evaluation pipeline produces:
        // IsModelAnswer=false, TaskText=null.
        var letter = McDonald.Replace("Dear Admissions Officer,", "Dear Sir/Madam,");
        AssertRuleDoesNotFire(Lint(letter, "LT-TR", taskText: null, isModelAnswer: false),
            "role_salutation_matches_task");
    }

    // ─────────────────────────────────────────────────────────────────
    // R2-14 — relevant current vital sign, no invented interpretation.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_14_Weir_Fixture_Preserves_The_Current_Blood_Pressure()
        => Assert.Contains("88/70 mmHg", Weir, StringComparison.Ordinal);

    [Fact]
    public void R2_14_Injected_Hypotension_Label_Is_Flagged()
    {
        var letter = Inject(Weir,
            "His blood pressure was 88/70 mmHg.",
            "He was hypotensive.");
        AssertRuleFires(Lint(letter, "LT-RR", caseNotes: WeirCaseNotes),
            "vital_sign_interpretation_unsupported");
    }

    [Fact]
    public void R2_14_Raw_Value_Without_Interpretation_Passes()
        => AssertRuleDoesNotFire(Lint(Weir, "LT-RR", caseNotes: WeirCaseNotes),
            "vital_sign_interpretation_unsupported");

    [Fact]
    public void R2_14_A_Documented_Condition_Is_Not_An_Invented_Interpretation()
    {
        // McDonald's notes DO record hypertension, so naming it is source
        // fidelity, not an inferred interpretation of a vital sign.
        var notes = "Patient history: hypertension. Patient history: obesity, BMI 35.";
        AssertRuleDoesNotFire(Lint(McDonald, "LT-TR", caseNotes: notes),
            "vital_sign_interpretation_unsupported");
    }

    [Fact]
    public void R2_14_Is_Silent_Without_The_Canonical_Notes()
    {
        var letter = Inject(Weir, "His blood pressure was 88/70 mmHg.", "He was hypotensive.");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR"), "vital_sign_interpretation_unsupported");
    }

    // ─────────────────────────────────────────────────────────────────
    // R2-15 — allied-health readers are healthcare professionals.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_15_Common_Diagnoses_To_An_Occupational_Therapist_Are_Not_Jargon()
    {
        // Weston's letter carries type two diabetes mellitus, hypothyroidism
        // and arthrosis to an occupational therapist and must lint clean.
        Assert.Contains("type two diabetes mellitus", Weston, StringComparison.Ordinal);
        Assert.Contains("hypothyroidism and arthrosis", Weston, StringComparison.Ordinal);
        AssertRuleDoesNotFire(Lint(Weston, "LT-NM"), "non_medical_no_jargon");
        AssertRuleDoesNotFire(Lint(Weston, "LT-NM", isModelAnswer: false), "non_medical_no_jargon");
    }

    // ─────────────────────────────────────────────────────────────────
    // R2-16 — canonical contact template vs candidate equivalents.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_16_Appended_With_Any_Queries_Is_Flagged()
    {
        var letter = Inject(Garcia,
            "Should there be any queries, kindly do not hesitate to contact me.",
            "Please do not hesitate to contact me with any queries.");
        AssertRuleFires(Lint(letter, "LT-DG"), "canonical_contact_template");
    }

    [Fact]
    public void R2_16_Canonical_Template_Passes()
        => AssertRuleDoesNotFire(Lint(Garcia, "LT-DG"), "canonical_contact_template");

    [Fact]
    public void R2_16_Please_Variant_Of_The_House_Template_Passes()
    {
        var letter = Inject(Garcia,
            "Should there be any queries, kindly do not hesitate to contact me.",
            "Should there be any queries, please do not hesitate to contact me.");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG"), "canonical_contact_template");
    }

    [Fact]
    public void R2_16_Candidate_Semantic_Equivalent_Is_Not_Penalised()
    {
        var letter = Inject(Garcia,
            "Should there be any queries, kindly do not hesitate to contact me.",
            "Please contact me if you require any further information.");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG", isModelAnswer: false), "canonical_contact_template");
        Assert.Equal(WritingCandidateBehaviors.AcceptAlternative,
            WritingRuleProvenance.For("canonical_contact_template").CandidateBehavior);
    }

    // ─────────────────────────────────────────────────────────────────
    // R2-17 — recipient spelling against the exact task.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_17_Recipient_Spelling_Mismatch_Is_Flagged()
        => AssertRuleFires(
            Lint(Taylor, "LT-UR", taskText: TaylorTaskText.Replace("Malcom", "Malcolm")),
            "recipient_name_mismatch");

    [Fact]
    public void R2_17_The_Exact_Task_Spelling_Passes()
        // The production Writing Task spells the recipient "Dr Malcom Still";
        // source spelling controls, so the letter must keep it.
        => AssertRuleDoesNotFire(Lint(Taylor, "LT-UR", taskText: TaylorTaskText), "recipient_name_mismatch");

    [Fact]
    public void R2_17_Is_Silent_Without_The_Exact_Task()
        => AssertRuleDoesNotFire(Lint(Taylor, "LT-UR"), "recipient_name_mismatch");

    // ─────────────────────────────────────────────────────────────────
    // R2-18 — brand/generic duplication.  brand_generic_duplication
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_18_Repeated_Generic_Brand_Pairing_Is_Flagged()
    {
        var letter = Taylor
            .Replace("treated with colchicine, 1 mg, and NSAIDs",
                "treated with colchicine, also known as Lengout, 1 mg, and NSAIDs")
            .Replace("managed with allopurinol, paracetamol and colchicine.",
                "managed with allopurinol, paracetamol and colchicine, also known as Lengout.");
        Assert.NotEqual(Taylor, letter);
        AssertRuleFires(Lint(letter, "LT-UR"), "brand_generic_duplication");
    }

    [Fact]
    public void R2_18_One_Clear_Medication_Identity_Passes()
        => AssertRuleDoesNotFire(Lint(Taylor, "LT-UR"), "brand_generic_duplication");

    [Fact]
    public void R2_18_A_Single_Brand_Appositive_Is_Allowed()
    {
        var letter = Inject(Taylor,
            "treated with colchicine, 1 mg, and NSAIDs",
            "treated with colchicine, also known as Lengout, 1 mg, and NSAIDs");
        AssertRuleDoesNotFire(Lint(letter, "LT-UR"), "brand_generic_duplication");
    }

    // ─────────────────────────────────────────────────────────────────
    // R2-19 — medication_passive_grammar must not flag correct clinical
    // English. Found during the 224-catalogue revalidation (14 Sep 2026):
    // "I would be grateful for your continued care." (possessive determiner
    // "your" parsed as a drug subject) and "He has become socially
    // withdrawn." (adverb "socially" parsed as a drug subject) were both
    // reported as note-form defects. Possessives and "-ly" adverbs can
    // never be medication subjects.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_19_Possessive_Determiner_Before_A_Participle_Is_Not_Note_Form()
    {
        var letter = Inject(McDonald,
            "I would be grateful if you could confirm Mr McDonald's admission for immediate rehabilitation.",
            "I would be grateful for your continued care of Mr McDonald during his rehabilitation.");
        AssertRuleDoesNotFire(Lint(letter, "LT-TR"), "medication_passive_grammar");
    }

    [Fact]
    public void R2_19_Adverb_Before_A_Participle_Is_Not_Note_Form()
    {
        var letter = Inject(Garcia,
            "The Department of Human Services was notified, and family immunisation was discussed.",
            "The Department of Human Services was notified, and her mother has become socially withdrawn since the admission.");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG", caseNotes: GarciaCaseNotes), "medication_passive_grammar");
    }

    [Fact]
    public void R2_19_Person_Subject_With_A_Participle_Is_Not_Note_Form()
    {
        // "She commenced smoking" and "the school doctor commenced him on
        // doxycycline" are correct active-voice clinical English.
        var letter = Inject(Garcia,
            "She responded well to the treatment.",
            "She commenced smoking in 2013, and the school doctor commenced her on iron infusions.");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG", caseNotes: GarciaCaseNotes), "medication_passive_grammar");
    }

    [Fact]
    public void R2_19_Dob_Is_Not_A_Treatment_Date_Ceiling()
    {
        // A birth date is an identity fact, not a documented treatment date.
        // With ONLY a DOB in the notes there is no treatment-date ceiling at
        // all, so no letter date can be "later than every documented date".
        const string dobOnlyNotes = "Patient: Mrs Maeve Greerson. DOB 09.10.1951.";
        var dobOnlyLetter = Weir.Replace("9 August 2014", "15 October 1951");
        AssertRuleDoesNotFire(Lint(dobOnlyLetter, "LT-RR", caseNotes: dobOnlyNotes), "letter_date_unsupported");

        // A DOB label must not swallow a LATER admission date: the label
        // scopes only the date it introduces, so the admission remains the
        // ceiling and a letter dated on the admission day passes (regression
        // for the fixed-width look-back window that hid this).
        const string notes = "Patient: Mrs Maeve Greerson. DOB 09.10.1951. " +
            "Admitted 24 July 1951 with dehydration. Review in 2/52.";
        var letter = Weir.Replace("9 August 2014", "24 July 1951");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR", caseNotes: notes), "letter_date_unsupported");

        // Structured extraction can isolate the birth date into its own bare
        // fragment with no label at all; a bare date line is still not a
        // treatment-date ceiling.
        const string bareNotes = "Greerson.\n09.10.1951";
        var letter2 = Weir.Replace("9 August 2014", "15 October 1951");
        AssertRuleDoesNotFire(Lint(letter2, "LT-RR", caseNotes: bareNotes), "letter_date_unsupported");
    }

    [Fact]
    public void R2_19_Letter_Date_Still_Fires_When_Truly_Unsupported()
    {
        const string notes = "Patient: Mrs Maeve Greerson. DOB 09.10.1951. " +
            "Admitted 24 July 1951 with dehydration. Review in 2/52.";
        var letter = Weir.Replace("9 August 2014", "15 October 2009");
        AssertRuleFires(Lint(letter, "LT-RR", caseNotes: notes), "letter_date_unsupported");
    }

    [Fact]
    public void R2_19_Genuine_Note_Form_Drug_Voice_Still_Fires()
    {
        var letter = Inject(McDonald,
            "I would be grateful if you could confirm Mr McDonald's admission for immediate rehabilitation.",
            "Colchicine ceased on discharge.");
        AssertRuleFires(Lint(letter, "LT-TR"), "medication_passive_grammar");
    }

    // ─────────────────────────────────────────────────────────────────
    // Supplementary: the Re:-line identity the owner review missed entirely.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Injected_Unsupported_Re_Line_Dob_Is_Flagged()
    {
        // A Re: line DOB that the notes do not record is a source-fidelity
        // defect (OA2-01): proved with SYNTHETIC DOB-less notes. (The real Weir
        // source records DOB 20 Sep 1970, so the canonical Weir letter's DOB
        // is supported — see Weir_Re_Line_Dob_Is_Supported_By_The_Source_Notes.)
        AssertRuleFires(Lint(Weir, "LT-RR", caseNotes: WeirSyntheticNotesWithoutDob), "re_line_identity_unsupported");
    }

    [Fact]
    public void Weir_Re_Line_Dob_Is_Supported_By_The_Source_Notes()
        => AssertRuleDoesNotFire(Lint(Weir, "LT-RR", caseNotes: WeirCaseNotes),
            "re_line_identity_unsupported");

    [Fact]
    public void Source_Supported_Re_Line_Dob_Passes()
        // Garcia's notes DO record "Date of birth 01.01.1995"; the Re: line
        // writes the same date in the canonical long form.
        => AssertRuleDoesNotFire(Lint(Garcia, "LT-DG", caseNotes: GarciaCaseNotes),
            "re_line_identity_unsupported");

    [Fact]
    public void Re_Line_Without_A_Date_Of_Birth_Passes()
        // Synthetic DOB-less source + DOB-less Re: line: nothing is invented.
        => AssertRuleDoesNotFire(Lint(WeirWithoutDob, "LT-RR", caseNotes: WeirSyntheticNotesWithoutDob),
            "re_line_identity_unsupported");

    [Fact]
    public void Re_Line_Identity_Is_Silent_Without_The_Canonical_Notes()
        => AssertRuleDoesNotFire(Lint(Weir, "LT-RR"), "re_line_identity_unsupported");

    // ─────────────────────────────────────────────────────────────────
    // Supplementary: "also" is banned as a LINKER, never as an adverb.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Adverbial_Also_Inside_A_Clause_Passes()
        // The owner's own canonical Taylor sentence is "Kidney stones were
        // also noted that year."
        => AssertRuleDoesNotFire(Lint(Taylor, "LT-UR"), "linker_avoid_words");

    [Fact]
    public void Connective_Also_Is_Still_Flagged()
    {
        var letter = Inject(Taylor,
            "His brother has gout, and his father died of kidney failure.",
            "His brother has gout, and also his father died of kidney failure.");
        AssertRuleFires(Lint(letter, "LT-UR"), "linker_avoid_words");
    }

    [Fact]
    public void Sentence_Initial_Also_Is_Still_Flagged()
    {
        var letter = Inject(Taylor,
            "Kidney stones were also noted that year.",
            "Also, kidney stones were noted that year.");
        AssertRuleFires(Lint(letter, "LT-UR"), "linker_avoid_words");
    }

    // ─────────────────────────────────────────────────────────────────
    // OA2-20: the new house rules must not become candidate penalties.
    // ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("background_paragraph_placement")]
    [InlineData("canonical_contact_template")]
    [InlineData("result_head_noun")]
    [InlineData("semicolon_overuse")]
    [InlineData("brand_generic_duplication")]
    [InlineData("number_style_words_vs_digits")]
    [InlineData("medication_list_punctuation")]
    // Senior Assessor Release Audit (16 Sep 2026, OA5): Model-Answer-only ids.
    [InlineData("sentence_fragment")]
    [InlineData("malformed_word_form")]
    [InlineData("malformed_today_phrase")]
    [InlineData("missing_possessive_name")]
    [InlineData("typographic_corruption")]
    [InlineData("age_dob_inconsistent")]
    [InlineData("letter_type_function_mismatch")]
    [InlineData("medication_frequency_conflict")]
    [InlineData("narrated_chronology_contradiction")]
    [InlineData("owner_required_fact_missing")]
    [InlineData("re_line_age_when_no_dob")]
    [InlineData("address_content_unsupported")]
    public void House_Style_Rules_Are_Never_Score_Bearing_For_Candidates(string checkId)
    {
        var behavior = WritingRuleProvenance.For(checkId).CandidateBehavior;
        Assert.True(
            behavior is WritingCandidateBehaviors.CoachingOnly
                or WritingCandidateBehaviors.AcceptAlternative
                or WritingCandidateBehaviors.NotApplicable,
            $"House-style rule '{checkId}' is {behavior}; Addendum Two OA2-20 forbids it from scoring a candidate.");
    }

    // ─────────────────────────────────────────────────────────────────
    // Supplementary: a PAST appointment is not a follow-up instruction.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void A_Past_Dated_Appointment_Does_Not_Set_The_Follow_Up_Marker()
    {
        // Weston's notes record the consultation the letter is written about.
        // Reading it as a future follow-up forced
        // closure_mentions_review_if_required onto a letter whose task asks for
        // no review — Addendum Two Weston E says add review/follow-up ONLY when
        // the Writing Task supports it.
        var markers = WritingCaseNotesMarkerExtractor.Derive(
            "Outpatient clinic appointment on 10.06.2018. " +
            "17.06.2018 review of symptoms: fatigue, cold intolerance, constipation. " +
            "Discharge plan: refer to occupational therapy for a custom-made wrist splint in neutral position.");
        Assert.Null(markers.FollowUpDate);
    }

    [Fact]
    public void A_Genuine_Future_Appointment_Still_Sets_The_Follow_Up_Marker()
    {
        var markers = WritingCaseNotesMarkerExtractor.Derive(
            "Treatment plan: specialist appointment at 6 weeks, made for 7/9/18.");
        Assert.NotNull(markers.FollowUpDate);
    }

    [Fact]
    public void An_Explicit_Review_Instruction_Still_Sets_The_Follow_Up_Marker()
    {
        var markers = WritingCaseNotesMarkerExtractor.Derive(
            "He was assessed for hypercholesterolaemia with repeat testing planned, review in 3 months.");
        Assert.NotNull(markers.FollowUpDate);
    }

    [Fact]
    public void Weston_Closure_Without_A_Review_Is_Not_Flagged()
        => AssertRuleDoesNotFire(Lint(Weston, "LT-NM"), "closure_mentions_review_if_required");

    // ─────────────────────────────────────────────────────────────────
    // Platform guard: the Model Answer generation constants.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Model_Answer_Generation_Uses_High_Effort_And_The_Raised_Token_Cap()
    {
        // Commit b0082f199 dropped the thinking effort from "max" to "high"
        // and raised the completion cap to 64000 after "max" produced a 0%
        // real generation success rate across dozens of PAID attempts:
        // Anthropic adaptive thinking shares ONE max_tokens budget between
        // reasoning and the visible completion, so at "max" the model spent
        // the entire budget reasoning (exactly 32000/32000, then exactly
        // 64000/64000, with zero variance) and never emitted the JSON answer.
        // Commit 31765d735 silently reverted both. This test exists so a merge
        // can never reintroduce that failure mode unnoticed.
        Assert.Equal("high", OetLearner.Api.Services.Writing.WritingTaskModelAnswerService.ThinkingEffort);
        Assert.Equal(64000, OetLearner.Api.Services.Writing.WritingTaskModelAnswerService.MaxCompletionTokens);
    }

    [Theory]
    [InlineData("result_noun_fragment")]
    [InlineData("supine_position_wording")]
    [InlineData("vital_sign_interpretation_unsupported")]
    [InlineData("role_salutation_matches_task")]
    [InlineData("re_line_identity_unsupported")]
    public void Genuine_Source_And_English_Failures_Stay_Score_Bearing(string checkId)
        => Assert.Equal(WritingCandidateBehaviors.ScoreBearing,
            WritingRuleProvenance.For(checkId).CandidateBehavior);

    // ─────────────────────────────────────────────────────────────────
    // R2-21 — patient_name_spelling must not flag correct names. Found
    // during the 224-catalogue revalidation (15 Sep 2026): the canonical
    // name extraction took the FIRST titled name in the notes (often the
    // recipient, a relative, or a PDF fragment like "Ms Osbur is"), and
    // the near-match scan flagged a relative sharing the patient's
    // surname ("Mr Krishnan Ramamurthy") as the patient's misspelling.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void R2_21_Canonical_Name_Comes_From_The_Name_Line()
    {
        // The notes name the patient on a "Name:" line and also contain a
        // fragment "Mrs Osburn is ..." whose last token is a stopword; the
        // correct Re: line must pass.
        const string notes = "Patient: Mrs Weir. Name: Michael Weir. Mrs Weir is 69 years old. " +
            "Admitted 24 July 1951 with dehydration.";
        AssertRuleDoesNotFire(Lint(Weir, "LT-RR", caseNotes: notes), "patient_name_spelling");
    }

    [Fact]
    public void R2_21_Relative_With_The_Same_Surname_Is_Not_A_Spelling_Error()
    {
        // "Mr Robert Weir" is the patient's father, not a misspelling of
        // "Michael"; the near-match scan must skip a far first token.
        const string notes = "Name: Michael Weir. Admitted 24 July 1951. Review in 2/52.";
        var letter = Inject(Weir,
            "I am writing to request your neurological assessment",
            "Mr Robert Weir attended with his son. I am writing to request your neurological assessment");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR", caseNotes: notes), "patient_name_spelling");
    }

    [Fact]
    public void R2_21_True_Spelling_Error_Still_Fires()
    {
        const string notes = "Name: Michael Weir. Admitted 24 July 1951. Review in 2/52.";
        var letter = Inject(Weir,
            "Re: Mr Michael Weir",
            "Re: Mr Michacl Weir");
        AssertRuleFires(Lint(letter, "LT-RR", caseNotes: notes), "patient_name_spelling");
    }
}
