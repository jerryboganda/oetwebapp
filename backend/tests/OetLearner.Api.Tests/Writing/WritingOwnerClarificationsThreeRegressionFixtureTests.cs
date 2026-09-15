using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Permanent regression classes R3-01..R3-05 for the OWNER CLARIFICATIONS
/// ROUND 3 (15 Sep 2026, OA3-01..OA3-05), proved on the same five canonical
/// Medicine fixtures as the R2 battery:
/// - R3-01 (OA3-01): a full patient name in the INTRODUCTION is valid with or
///   without a purpose clause; a surname-only introduction is equally valid;
///   a full name recurring in a LATER body paragraph fails.
/// - R3-02 (OA3-02): patient-name spelling is hard source fidelity — the
///   canonical notes are the spelling authority; a one-letter change fails.
/// - R3-03 (OA3-03): DOB has priority over age in the Re: line when the
///   notes supply a DOB; "aged X" is only for a DOB-less source.
/// - R3-04 (OA3-04): canonical "at" result wording; headless result values
///   fail the Model Answer; candidates keep grammatical alternatives.
/// - R3-05 (OA3-05): a treatment clause may not dangle on a specimen.
/// </summary>
public sealed class WritingOwnerClarificationsThreeRegressionFixtureTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());

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

    // Notes that NAME the patient — the spelling authority for OA3-02.
    private const string TaylorCaseNotes =
        "Mr David Taylor, DOB 01/08/1965 (age 55). " +
        "History of gout since 2000, treated with colchicine (Lengout), allopurinol and paracetamol. " +
        "On 13/06/2020 presented with pain in his right big toe and swelling of the right foot. " +
        "Observations: BP 120/80, HR 90, RR 22, temperature 37.8°C. " +
        "Right first toe inflamed and red, with a tophus noted under the right big toe. " +
        "Plan: refer to a rheumatologist for urgent assessment.";

    private const string WestonCaseNotes =
        "Patient is Betty Weston, DOB 12.2.64 (55 years). " +
        "Presenting problem: numbness/tingling in the thumb, index and middle finger of the right hand. " +
        "Diagnosis: carpal tunnel syndrome. " +
        "Discharge plan: refer to occupational therapy for a custom-made wrist splint in neutral position.";

    private const string GarciaNamedNotes =
        "Patient is Isabel Garcia, DOB 01.01.1995 (age 20). " +
        "Diagnosis: bacterial meningitis. Culture identified Neisseria meningitidis.";

    // ─── R3-01 — introduction full-name freedom ────

    [Fact]
    public void R3_01_Full_Name_Introduction_Without_A_Purpose_Clause_Is_Valid()
    {
        // The Weir fixture opens "…management of Mr Michael Weir, who has
        // presented…" — a full name NOT governed by regarding/for/of. It must
        // never be flagged merely because the full name is in the Re: line.
        AssertRuleDoesNotFire(Lint(Weir, "LT-RR"), "body_uses_last_name_only");
        AssertRuleDoesNotFire(Lint(Weston, "LT-NM"), "body_uses_last_name_only");
        AssertRuleDoesNotFire(Lint(Garcia, "LT-DG"), "body_uses_last_name_only");
    }

    [Fact]
    public void R3_01_Surname_Only_Introduction_Is_Equally_Valid()
    {
        var letter = Inject(Weir,
            "management of Mr Michael Weir, who has presented",
            "management of Mr Weir, who has presented");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR"), "body_uses_last_name_only");
    }

    [Fact]
    public void R3_01_Full_Name_Recurring_In_A_Later_Body_Paragraph_Is_Flagged()
    {
        var letter = Inject(Weir,
            "Mr Weir presented in June 2014 with fatigue",
            "Mr Michael Weir presented in June 2014 with fatigue");
        AssertRuleFires(Lint(letter, "LT-RR"), "body_uses_last_name_only");
    }

    [Fact]
    public void R3_01_Both_Introduction_Forms_Are_Accepted_For_Candidates()
    {
        var full = Lint(Weir, "LT-RR", isModelAnswer: false);
        var surnameOnly = Inject(Weir,
            "management of Mr Michael Weir, who has presented",
            "management of Mr Weir, who has presented");
        AssertRuleDoesNotFire(full, "body_uses_last_name_only");
        AssertRuleDoesNotFire(Lint(surnameOnly, "LT-RR", isModelAnswer: false), "body_uses_last_name_only");
    }

    // ─── R3-02 — patient-name spelling fidelity ────

    [Fact]
    public void R3_02_Exactly_Spelled_Patient_Name_Passes()
    {
        var findings = Lint(Taylor, "LT-UR", caseNotes: TaylorCaseNotes);
        AssertRuleDoesNotFire(findings, "patient_name_spelling");
    }

    [Fact]
    public void R3_02_One_Letter_Missing_In_The_Surname_Is_Flagged()
    {
        var letter = Inject(Taylor, "Re: Mr David Taylor, DOB: 1 August 1965", "Re: Mr David Taylr, DOB: 1 August 1965");
        AssertRuleFires(Lint(letter, "LT-UR", caseNotes: TaylorCaseNotes), "patient_name_spelling");
    }

    [Fact]
    public void R3_02_One_Letter_Changed_In_The_First_Name_Is_Flagged()
    {
        var letter = Inject(Taylor,
            "management of Mr Taylor, who has presented with a gout flare",
            "management of Mr David Taylor, who has presented with a gout flare")
            .Replace("management of Mr David Taylor", "management of Mr Davod Taylor");
        AssertRuleFires(Lint(letter, "LT-UR", caseNotes: TaylorCaseNotes), "patient_name_spelling");
    }

    [Fact]
    public void R3_02_Re_Line_Surname_Mismatch_Is_Flagged()
    {
        var letter = Inject(Taylor, "Re: Mr David Taylor, DOB: 1 August 1965", "Re: Mr David Tailor, DOB: 1 August 1965");
        AssertRuleFires(Lint(letter, "LT-UR", caseNotes: TaylorCaseNotes), "patient_name_spelling");
    }

    [Fact]
    public void R3_02_Is_Silent_When_The_Notes_Never_Name_The_Patient()
    {
        // Garcia's canonical notes never state her first name; nothing may be
        // invented to compare against, so the check stays inert.
        AssertRuleDoesNotFire(Lint(Garcia, "LT-DG"), "patient_name_spelling");
    }

    [Fact]
    public void R3_02_Another_Persons_Full_Name_Is_Not_A_Spelling_Error()
    {
        // The notes name the patient AND a second man (the specialist). A
        // full name matching NEITHER patient token is a different person and
        // must never trip the patient check; only a near miss to the
        // patient's own name is a spelling error.
        const string notes = "Mr Michael Weir is a patient in your general practice, height 183cm. " +
            "Mr B Mossley is the specialist who performed the surgery.";
        AssertRuleDoesNotFire(Lint(Weir, "LT-RR", caseNotes: notes), "patient_name_spelling");

        // A one-letter change to the PATIENT's name still fails on the same
        // notes.
        var letter = Inject(Weir,
            "management of Mr Michael Weir, who has presented",
            "management of Mr Michael Wier, who has presented");
        AssertRuleFires(Lint(letter, "LT-RR", caseNotes: notes), "patient_name_spelling");
    }

    // ─── R3-03 — DOB priority over age ────

    [Fact]
    public void R3_03_Re_Line_With_The_Source_Dob_Passes()
        => AssertRuleDoesNotFire(Lint(Taylor, "LT-UR", caseNotes: TaylorCaseNotes), "re_line_dob_priority");

    [Fact]
    public void R3_03_Age_Used_While_The_Notes_Have_A_Dob_Is_Flagged()
    {
        var letter = Inject(Taylor, "Re: Mr David Taylor, DOB: 1 August 1965", "Re: Mr David Taylor, aged 55");
        AssertRuleFires(Lint(letter, "LT-UR", caseNotes: TaylorCaseNotes), "re_line_dob_priority");
    }

    [Fact]
    public void R3_03_Dob_Omitted_Altogether_While_The_Notes_Have_One_Is_Flagged()
    {
        var letter = Inject(Taylor, "Re: Mr David Taylor, DOB: 1 August 1965", "Re: Mr David Taylor");
        AssertRuleFires(Lint(letter, "LT-UR", caseNotes: TaylorCaseNotes), "re_line_dob_priority");
    }

    [Fact]
    public void R3_03_Age_Form_Passes_When_The_Notes_Have_No_Dob()
    {
        // The canonical Weir notes record NO date of birth (verified against
        // production 15 Sep 2026), so an age-based Re: line is correct there.
        const string notes = "Mr Michael Weir is a patient in your general practice, height 183cm. " +
            "He is married with 3 children aged 13, 10 and 8. " +
            "On 09.08.14 he complained of dizziness and two recent blackouts. " +
            "Cholesterol was 6.37mmol/L.";
        var letter = Inject(Weir, "Re: Mr Michael Weir", "Re: Mr Michael Weir, aged 55");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR", caseNotes: notes), "re_line_dob_priority");
        // And the DOB-less Re: line stays correct for the real fixture.
        AssertRuleDoesNotFire(Lint(Weir, "LT-RR", caseNotes: notes), "re_line_dob_priority");
    }

    [Fact]
    public void R3_03_Dob_And_Age_Both_In_The_Notes_Resolve_To_The_Dob()
    {
        // Weston's notes carry BOTH ("DOB 12.2.64 (55 years)"); the Re: line
        // must carry the DOB, never the age.
        AssertRuleDoesNotFire(Lint(Weston, "LT-NM", caseNotes: WestonCaseNotes), "re_line_dob_priority");
        var letter = Inject(Weston, "Re: Mrs Betty Weston, DOB: 12 February 1964", "Re: Mrs Betty Weston, aged 55");
        AssertRuleFires(Lint(letter, "LT-NM", caseNotes: WestonCaseNotes), "re_line_dob_priority");
    }

    [Fact]
    public void R3_03_Abbreviated_Month_Dob_In_Notes_Grounds_The_Re_Line()
    {
        // Physiotherapy - Sophie Bennett: the canonical notes write
        // "DOB 14 Nov 1969" while the letter writes "DOB: 14 November 1969".
        // The abbreviated month must parse, or a source-supported DOB reads
        // as invented (re_line_identity_unsupported) and the DOB-priority
        // rule goes blind.
        const string notes = "Patient: Ms Sophie Bennett, DOB 14 Nov 1969 (49 years old). " +
            "Occupation: nurse. 28 Aug 2019 initial assessment: constant right buttock pain.";
        AssertRuleDoesNotFire(Lint(Taylor.Replace("Re: Mr David Taylor, DOB: 1 August 1965", "Re: Mr David Taylor, DOB: 14 November 1969"),
            "LT-UR", caseNotes: notes), "re_line_identity_unsupported");
        AssertRuleFires(Lint(Taylor.Replace("Re: Mr David Taylor, DOB: 1 August 1965", "Re: Mr David Taylor, aged 49"),
            "LT-UR", caseNotes: notes), "re_line_dob_priority");
    }

    [Fact]
    public void R3_03_Letter_Date_Within_A_Forward_Relative_Review_Window_Passes()
    {
        // Physiotherapy - Sophie Bennett pattern: the notes end with a
        // relative forward reference ("review in 2 days") and no documented
        // transfer date, so a letter written inside the continuing timeline
        // (14 days after the last absolute date) is not provably invented.
        const string notes = "Patient: Ms Sophie Bennett, DOB 14 Nov 1969. " +
            "28 Aug 2019 initial assessment: constant right buttock pain. " +
            "To see the doctor to change medication to tramadol; review in 2 days.";
        var letter = Weir.Replace("9 August 2014", "11 September 2019");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR", caseNotes: notes), "letter_date_unsupported");
        // Without a forward reference the provable-invention rule stays armed.
        const string closedNotes = "Patient: Ms Sophie Bennett, DOB 14 Nov 1969. " +
            "28 Aug 2019 initial assessment: constant right buttock pain.";
        AssertRuleFires(Lint(letter, "LT-RR", caseNotes: closedNotes), "letter_date_unsupported");
    }

    // ─── R3-04 — canonical "at" result wording ────

    [Fact]
    public void R3_04_Canonical_At_Wording_Is_Accepted()
    {
        // The repaired Garcia fixture states the LP results exactly in the
        // owner's at-construction — no result-wording finding may fire.
        var findings = Lint(Garcia, "LT-DG");
        AssertRuleDoesNotFire(findings, "result_at_wording");
        AssertRuleDoesNotFire(findings, "result_noun_fragment");
        AssertRuleDoesNotFire(findings, "result_head_noun");
    }

    [Fact]
    public void R3_04_Headless_Result_Value_Is_Flagged()
    {
        var letter = Inject(Garcia,
            "The white cell count was 14.0x10^9/L and the C-reactive protein level was 150.",
            "Blood tests showed white cell count 14.0x10^9/L and C-reactive protein level 150.");
        AssertRuleFires(Lint(letter, "LT-DG"), "result_at_wording");
    }

    [Fact]
    public void R3_04_Of_Form_Fails_The_Model_Answer_Only()
    {
        var letter = Inject(Garcia,
            "a reduced glucose level at 10 mg/dL",
            "a reduced glucose of 10 mg/dL");
        AssertRuleFires(Lint(letter, "LT-DG"), "result_at_wording");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG", isModelAnswer: false), "result_at_wording");
    }

    [Fact]
    public void R3_04_Was_Form_On_A_Complete_Result_Noun_Passes()
    {
        var letter = Inject(Garcia,
            "a reduced glucose level at 10 mg/dL",
            "a glucose level that was reduced to 10 mg/dL");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG"), "result_at_wording");
    }

    [Fact]
    public void R3_04_Provenance_Is_AcceptAlternative()
        => Assert.Equal(WritingCandidateBehaviors.AcceptAlternative,
            WritingRuleProvenance.For("result_at_wording").CandidateBehavior);

    // ─── R3-05 — dangling treatment modifier ────

    [Fact]
    public void R3_05_Dangling_Treatment_On_A_Culture_Is_Flagged()
    {
        var letter = Inject(McDonald,
            "A catheter urine culture grew Staphylococcus saprophyticus, and Mr McDonald was treated with Keflex for five days.",
            "A catheter urine culture grew Staphylococcus saprophyticus, treated with five days of Keflex.");
        AssertRuleFires(Lint(letter, "LT-TR"), "dangling_treatment_modifier");
        // Genuine grammar: the defect is score-bearing for candidates too.
        AssertRuleFires(Lint(letter, "LT-TR", isModelAnswer: false), "dangling_treatment_modifier");
    }

    [Fact]
    public void R3_05_Patient_Subject_Repair_Passes()
        => AssertRuleDoesNotFire(Lint(McDonald, "LT-TR"), "dangling_treatment_modifier");

    [Fact]
    public void R3_05_Unrelated_Treated_With_Clause_Is_Not_Flagged()
    {
        // "presented with ... , treated with ..." names no specimen subject —
        // outside the dangling-specimen pattern.
        var letter = Inject(Garcia,
            "She responded well to the treatment.",
            "She presented with photophobia, treated with simple analgesia at home, and responded well to the treatment.");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG"), "dangling_treatment_modifier");
    }
}
