using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Permanent regression fixtures (owner directives 13-14 Sep 2026): the five
/// finished Medicine Model Answers — Garcia (update), Weston (non-medical
/// referral), Weir (routine referral), McDonald (transfer) and Taylor
/// (urgent referral) — must lint with ZERO findings under the Owner
/// Clarifications Addendum (OA-01..OA-15) battery, and every defect the
/// owner listed must be DETECTED when deliberately injected, with zero
/// false positives on deliberately valid alternatives. The medication
/// parser must recognise legitimate dose formats (ranges, combination
/// strengths, frequency-only doses) so correct clinical wording is never
/// distorted to satisfy — or evade — the regex: "if correct professional
/// wording exposes a weakness in the validator, FIX THE VALIDATOR."
/// </summary>
public sealed class WritingRev8RegressionFixtureTests
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

    private const string TaylorTaskText =
        "Using the information given in the case notes, write a letter of referral to Dr Still, a Rheumatologist at City Hospital, " +
        "for assessment of Mr Taylor. Address the letter to Dr Malcom Still, Rheumatologist, City Hospital, Suite 32, 55 Main Road, Newtown.";

    // ─── A. Ms Isabel Garcia — update (LT-DG) ────

    internal const string GarciaUpdateLetter = """
        Dr Lorna Bradbury
        Stillwater Medical Clinic
        12 Main Street
        Stillwater

        23 May 2015

        Dear Dr Bradbury,
        Re: Ms Isabel Garcia, DOB: 1 January 1995

        I am writing to update you regarding Ms Isabel Garcia's treatment for bacterial meningitis and request follow-up of close contacts.

        Ms Garcia presented with a one-week history of painful, stiff joints, headache and photophobia. On examination, she was afebrile, with a petechial rash on the abdomen and legs, bruising on the left arm and inability to touch chin to chest when supine. The white cell count was 14.0x10^9/L and the C-reactive protein level was 150. Lumbar puncture showed a white cell count of 1000 with polymorphonuclear predominance, reduced glucose 10 mg/dL and elevated protein 70 mg/dL. Culture confirmed Neisseria meningitidis.

        Ms Garcia received dexamethasone, 10 mg IV, before ceftriaxone, 2 g IV twice daily. Dexamethasone was continued six-hourly for four days. Following lumbar puncture results, treatment was changed to benzylpenicillin, 1.8 g IV four-hourly for five days. She responded well to treatment.

        The Department of Human Services was notified of Ms Garcia's case, and family immunisation was discussed.

        I would be grateful if you could contact Ms Garcia's close contacts, advise them to seek prompt attention for unexplained illness and consider chemoprophylaxis.

        Should there be any queries, kindly do not hesitate to contact me.

        Yours sincerely,

        Doctor
""";

    [Fact]
    public void Garcia_Update_Fixture_Lints_Clean_Against_Source()
    {
        var findings = Lint(GarciaUpdateLetter, "LT-DG", caseNotes: GarciaCaseNotes, taskText: GarciaTaskText);
        Assert.Empty(findings);
    }

    // §7 injection 1 (Garcia-style): the letter date is invented — later than
    // every date documented in the canonical notes (23 May 2015).
    [Fact]
    public void Injected_Invented_Letter_Date_Is_Flagged()
    {
        var letter = GarciaUpdateLetter.Replace("23 May 2015", "30 May 2015");
        AssertRuleFires(Lint(letter, "LT-DG", caseNotes: GarciaCaseNotes), "letter_date_unsupported");
    }

    [Fact]
    public void Source_Supported_Letter_Date_Passes()
    {
        var findings = Lint(GarciaUpdateLetter, "LT-DG", caseNotes: GarciaCaseNotes);
        Assert.DoesNotContain(findings, f => f.RuleId.EndsWith("letter_date_unsupported", StringComparison.Ordinal));
    }

    // §7 injection 2: Re: line identifies the patient by surname only.
    [Fact]
    public void Injected_Re_Line_Surname_Only_Is_Flagged()
    {
        var letter = GarciaUpdateLetter.Replace("Re: Ms Isabel Garcia, DOB: 1 January 1995", "Re: Ms Garcia, DOB: 1 January 1995");
        AssertRuleFires(Lint(letter, "LT-DG"), "re_line_full_name");
    }

    // §7 injection 3: the introduction states only the topic while the letter
    // carries a request; and the vague working-assessment hand-off.
    [Fact]
    public void Injected_Vague_Intro_Without_Request_Is_Flagged()
    {
        var letter = GarciaUpdateLetter.Replace(
            "I am writing to update you regarding Ms Isabel Garcia's treatment for bacterial meningitis and request follow-up of close contacts.",
            "I am writing to update you regarding Ms Garcia's diagnosis and treatment for bacterial meningitis.");
        AssertRuleFires(Lint(letter, "LT-DG"), "intro_purpose_vague");
    }

    [Fact]
    public void Injected_Working_Assessment_Handoff_Is_Flagged()
    {
        var letter = WeirRoutineReferralLetter.Replace(
            "I am writing to request your neurological assessment and management of Mr Michael Weir, who has presented with features suggestive of multiple sclerosis.",
            "I am writing to update the practice regarding Mr Weir, given a working assessment of possible multiple sclerosis.");
        AssertRuleFires(Lint(letter, "LT-RR"), "intro_purpose_vague");
    }

    // §7 injection 4: discharge/transfer-of-care wording invented in a simple
    // update whose notes document no admission or discharge. (For an LT-DG
    // task the classification itself is the semantic validator's decision,
    // so the deterministic detector scopes to non-discharge letters.)
    [Fact]
    public void Injected_Unsupported_Discharge_Wording_Is_Flagged()
    {
        var letter = TaylorUrgentReferralLetter.Replace(
            "He had shortness of breath.",
            "He had shortness of breath and is ready for discharge.");
        AssertRuleFires(
            Lint(letter, "LT-UR", markers: new WritingCaseNotesMarkers()),
            "discharge_language_unsupported");
    }

    // §7 injection 5: the false-READY fixture — "Examination showed afebrile".
    [Fact]
    public void Injected_Examination_Showed_Afebrile_Is_Flagged()
    {
        var letter = GarciaUpdateLetter.Replace(
            "On examination, she was afebrile, with a petechial rash",
            "Examination showed afebrile, petechial rash");
        AssertRuleFires(Lint(letter, "LT-DG"), "incomplete_clinical_construction");
    }

    // §7 injection 6: missing passive auxiliaries.
    [Fact]
    public void Injected_Medication_Without_Auxiliary_Is_Flagged()
    {
        var letter = GarciaUpdateLetter.Replace(
            "Dexamethasone was continued six-hourly",
            "Dexamethasone continued six-hourly");
        AssertRuleFires(Lint(letter, "LT-DG"), "medication_passive_grammar");
    }

    [Fact]
    public void Injected_Treatment_Changed_Without_Auxiliary_Is_Flagged()
    {
        var letter = GarciaUpdateLetter.Replace(
            "treatment was changed to benzylpenicillin",
            "treatment changed to benzylpenicillin");
        AssertRuleFires(Lint(letter, "LT-DG"), "treatment_change_grammar");
    }

    // §7 injection 7: the request merged into the preceding body paragraph.
    [Fact]
    public void Injected_Request_Merged_Into_Body_Paragraph_Is_Flagged()
    {
        var letter = GarciaUpdateLetter.Replace(
            "The Department of Human Services was notified of Ms Garcia's case, and family immunisation was discussed.\n\nI would be grateful",
            "The Department of Human Services was notified of Ms Garcia's case, and family immunisation was discussed. I would be grateful");
        AssertRuleFires(Lint(letter, "LT-DG"), "closure_request_paragraph");
    }

    // §7 injection 8: the universal contact sentence merged into the request
    // paragraph.
    [Fact]
    public void Injected_Contact_Offer_Merged_Into_Request_Paragraph_Is_Flagged()
    {
        var letter = GarciaUpdateLetter.Replace(
            "I would be grateful if you could contact Ms Garcia's close contacts, advise them to seek prompt attention for unexplained illness and consider chemoprophylaxis.\n\nShould there be any queries, kindly do not hesitate to contact me.",
            "I would be grateful if you could contact Ms Garcia's close contacts, advise them to seek prompt attention for unexplained illness and consider chemoprophylaxis. Should there be any queries, kindly do not hesitate to contact me.");
        AssertRuleFires(Lint(letter, "LT-DG"), "closure_request_paragraph");
    }

    // OA-03 owner override: the introduction MAY use the full name once as
    // part of the purpose clause — it must not be penalised.
    [Fact]
    public void Full_Name_In_Intro_Purpose_Clause_Is_Accepted()
    {
        var findings = Lint(GarciaUpdateLetter, "LT-DG");
        Assert.DoesNotContain(findings, f => f.RuleId.EndsWith("body_uses_last_name_only", StringComparison.Ordinal));
    }

    [Fact]
    public void Injected_Bruising_To_Left_Arm_Is_Flagged()
    {
        var letter = GarciaUpdateLetter.Replace(
            "bruising on the left arm",
            "bruising to the left arm");
        AssertRuleFires(Lint(letter, "LT-DG"), "register_colloquial");
    }

    // ─── B. Mrs Betty Weston — non-medical referral (LT-NM) ────

    internal const string WestonReferralLetter = """
        Ms Alison Goody
        Occupational Therapist
        Northwood Community Health Centre
        Northwood

        20 June 2018

        Dear Ms Goody,
        Re: Mrs Betty Weston, DOB: 12 February 1964

        I am writing to request your occupational therapy assessment and management of Mrs Betty Weston, who has been diagnosed with carpal tunnel syndrome.

        Mrs Weston presented on 10 June 2018 with a three-week history of numbness and tingling in the right thumb, index and middle fingers. Her sleep was disturbed by pain, which was relieved by moving her fingers. She reported difficulty unscrewing jar tops and gripping a glass or cup, with objects slipping from her fingers. Examination showed decreased grip strength without swelling, with positive Phalen's and Tinel's signs, confirming carpal tunnel syndrome.

        Mrs Weston's carpal tunnel syndrome has been managed conservatively with night-time wrist splinting, alongside electromyography and nerve conduction studies to exclude other neurological causes.

        Mrs Weston works as a supermarket manager, which requires long hours on her feet. She is aware that her weight, with a body mass index of 32 kg/m², is an aggravating factor. She has a background of type two diabetes mellitus, hypothyroidism and arthrosis.

        I would be grateful if you could provide a custom-made wrist splint in a neutral position and undertake an ergonomics assessment.

        Should there be any queries, kindly do not hesitate to contact me.

        Yours sincerely,

        Doctor
""";

    [Fact]
    public void Weston_Referral_Fixture_Lints_Clean()
    {
        Assert.Empty(Lint(WestonReferralLetter, "LT-NM"));
    }

    // OA-12: canonical Model Answers write "type two diabetes mellitus" in
    // words; digits are a Model Answer house-form violation but a candidate
    // alternative that must stay unpenalised.
    [Fact]
    public void Injected_Type_2_Diabetes_In_Model_Answer_Is_Flagged()
    {
        var letter = WestonReferralLetter.Replace("type two diabetes mellitus", "type 2 diabetes mellitus");
        AssertRuleFires(Lint(letter, "LT-NM"), "diabetes_type_words");
    }

    [Fact]
    public void Candidate_Type_2_Diabetes_Is_Not_Penalised()
    {
        var letter = WestonReferralLetter.Replace("type two diabetes mellitus", "type 2 diabetes mellitus");
        Assert.DoesNotContain(Lint(letter, "LT-NM", isModelAnswer: false),
            f => f.RuleId.EndsWith("diabetes_type_words", StringComparison.Ordinal));
    }

    // ─── C. Mr Michael Weir — routine referral (LT-RR) ────

    internal const string WeirRoutineReferralLetter = """
        Dr M McLaren
        Neurologist
        Suite 3
        67 The Crescent
        Newtown

        9 August 2014

        Dear Dr McLaren,
        Re: Mr Michael Weir

        I am writing to request your neurological assessment and management of Mr Michael Weir, who has presented with features suggestive of multiple sclerosis.

        On today's review, Mr Weir reported dizziness, two recent blackouts, tingling in both hands, persistent left leg weakness, breathlessness, occasional constipation and low energy. His blood pressure was 88/70 mmHg. Examination revealed sensory loss to sharp and blunt stimuli in both hands and a diminished left patellar reflex. A CT scan of the head and lumbar spine has therefore been ordered to investigate possible central or spinal causes.

        Mr Weir presented in June 2014 with fatigue, stress and lethargy, returning one week later with left leg weakness. The cholesterol level was 6.37 mmol/L, and the full blood count showed low white and red cell counts, haemoglobin and haematocrit. He was assessed for hypercholesterolaemia, with repeat testing planned in three months.

        Mr Weir has depression and has taken sertraline hydrochloride since September 2012. He continues to smoke. He has been overweight for many years.

        I would be grateful if you could consider MRI if clinically indicated.

        Should there be any queries, kindly do not hesitate to contact me.

        Yours sincerely,

        Doctor
""";

    private const string WeirTaskText =
        "Using the information given in the case notes, write a letter of referral to Dr M McLaren, Neurologist, Suite 3, 67 The Crescent, Newtown.";

    [Fact]
    public void Weir_Routine_Referral_Fixture_Lints_Clean_Against_Task()
    {
        Assert.Empty(Lint(WeirRoutineReferralLetter, "LT-RR", taskText: WeirTaskText));
    }

    // §7 injection 11: "has been overweight long term" is ungrammatical
    // register; the notes may say it — a Model Answer still must not.
    [Fact]
    public void Injected_Overweight_Long_Term_Is_Flagged()
    {
        var letter = WeirRoutineReferralLetter.Replace(
            "has been overweight for many years",
            "has been overweight long term");
        AssertRuleFires(Lint(letter, "LT-RR"), "register_colloquial");
    }

    // Owner ruling (13 Sep 2026): the case notes say "tired, stressed and
    // sluggish", but a Model Answer must render that as premium clinical
    // wording — the colloquial register rule fires EVEN THOUGH the words
    // appear verbatim in the source notes. (The former case-notes exemption
    // was a validator bypass and was removed.)
    [Fact]
    public void Injected_Colloquial_Source_Wording_Is_Flagged_Even_When_In_The_Notes()
    {
        var letter = WeirRoutineReferralLetter.Replace(
            "with fatigue, stress and lethargy",
            "feeling tired, stressed and sluggish");
        AssertRuleFires(Lint(letter, "LT-RR"), "register_colloquial");
    }

    [Fact]
    public void Injected_Vague_Duration_Is_Flagged()
    {
        var letter = WeirRoutineReferralLetter.Replace(
            "has been overweight for many years",
            "has been overweight for a long time");
        AssertRuleFires(Lint(letter, "LT-RR"), "register_colloquial");
    }

    // ─── D. Mr Julian McDonald — transfer (LT-TR) ────

    internal const string McDonaldTransferLetter = """
        The Admissions Officer
        Cabrini Hopetoun Rehabilitation
        2-6 Hopetoun Street
        Elsternwick
        Vic 3185

        24 July 2018

        Dear Admissions Officer,
        Re: Mr Julian McDonald, DOB: 12 January 1950

        I am writing to transfer Mr McDonald to your rehabilitation service following his elective left total knee replacement on 20 July 2018 under Mr Mossley.

        Post-operative pain despite morphine patient-controlled analgesia slowed mobilisation. A forty-eight-hour ketamine infusion was effective. Amitriptyline was discontinued because of difficulty urinating. Somnolence and snoring prompted sleep studies for possible obstructive sleep apnoea, and a catheter urine culture grew Staphylococcus saprophyticus, treated with five days of Keflex.

        Discharge medications are Zyloric, 300 mg daily; Lipitor, 20 mg at night; Karvina, 300 mg daily and Nicabate patch, 21 mg. Analgesia comprises paracetamol, 1 g four times daily; ibuprofen, 400 mg three times daily; Targin, 20/10 twice daily and oxycodone, 5-10 mg four-hourly as needed. Physiotherapy, an occupational therapy home visit, social work and drug and alcohol support are planned, with a specialist appointment on 7 September 2018.

        Mr McDonald has hypertension, osteoarthritis, gout, post-traumatic stress disorder and childhood penicillin allergy. He smokes twenty cigarettes daily and drinks six to ten standard drinks daily. He lives alone in a caravan.

        I would be grateful if you could confirm Mr McDonald's admission for immediate rehabilitation.

        Should there be any queries, kindly do not hesitate to contact me.

        Yours faithfully,

        Doctor
""";

    [Fact]
    public void McDonald_Transfer_Fixture_Lints_Clean()
    {
        Assert.Empty(Lint(McDonaldTransferLetter, "LT-TR"));
    }

    // The parser must natively recognise a dose range ("5-10 mg") and a
    // combination strength ("20/10" with no unit but a frequency), so the
    // natural 4-item analgesia list passes list-punctuation AS WRITTEN —
    // the pre-fix parser saw only 2 items and forced unnatural rewrites.
    [Fact]
    public void Medication_Parser_Recognises_Range_And_Combination_Doses_As_List_Items()
    {
        var findings = Lint(McDonaldTransferLetter, "LT-TR");
        Assert.DoesNotContain(findings, f => f.RuleId.EndsWith("medication_list_punctuation", StringComparison.Ordinal));
    }

    // §7 injection 9: address components joined by commas instead of line
    // breaks (OA-11).
    [Fact]
    public void Injected_Comma_Joined_Address_Is_Flagged()
    {
        var letter = McDonaldTransferLetter.Replace(
            "Elsternwick\nVic 3185",
            "Elsternwick, Vic 3185");
        AssertRuleFires(Lint(letter, "LT-TR"), "address_punctuation");
    }

    // §7 injection 10: the impossible quantity "over six to ten".
    [Fact]
    public void Injected_Over_Six_To_Ten_Is_Flagged()
    {
        var letter = McDonaldTransferLetter.Replace(
            "drinks six to ten standard drinks daily",
            "drinks over six to ten standard drinks daily");
        AssertRuleFires(Lint(letter, "LT-TR"), "illogical_quantity_range");
    }

    [Fact]
    public void Injected_Note_Form_Partitive_Is_Flagged()
    {
        var letter = McDonaldTransferLetter.Replace(
            "Amitriptyline was discontinued because of difficulty urinating",
            "Amitriptyline ceased because of difficulty urinating");
        AssertRuleFires(Lint(letter, "LT-TR"), "medication_passive_grammar");
    }

    [Fact]
    public void Injected_Latin_Frequency_Nocte_Is_Flagged()
    {
        var letter = McDonaldTransferLetter.Replace(
            "Lipitor, 20 mg at night",
            "Lipitor, 20 mg nocte");
        AssertRuleFires(Lint(letter, "LT-TR"), "latin_abbreviations_translated");
    }

    [Fact]
    public void Injected_Missing_Dose_Comma_Is_Flagged()
    {
        var letter = McDonaldTransferLetter.Replace(
            "Lipitor, 20 mg at night",
            "Lipitor 20 mg at night");
        AssertRuleFires(Lint(letter, "LT-TR"), "medication_list_punctuation");
    }

    [Fact]
    public void Injected_Comma_Only_Medication_List_Is_Flagged()
    {
        var letter = McDonaldTransferLetter.Replace(
            "Analgesia comprises paracetamol, 1 g four times daily; ibuprofen, 400 mg three times daily; Targin, 20/10 twice daily and oxycodone, 5-10 mg four-hourly as needed.",
            "Analgesia comprises paracetamol, 1 g four times daily, ibuprofen, 400 mg three times daily, Targin, 20/10 twice daily and oxycodone, 5-10 mg four-hourly as needed.");
        AssertRuleFires(Lint(letter, "LT-TR"), "medication_list_punctuation");
    }

    [Fact]
    public void Injected_Stripped_Cigarette_Frequency_Is_Flagged()
    {
        var letter = McDonaldTransferLetter.Replace(
            "He smokes twenty cigarettes daily",
            "He smokes twenty cigarettes");
        AssertRuleFires(Lint(letter, "LT-TR"), "lifestyle_frequency_precision");
    }

    // §7 injection 15: the closure mechanically repeats the introduction's
    // request.
    [Fact]
    public void Injected_Duplicated_Request_Is_Flagged()
    {
        var letter = McDonaldTransferLetter.Replace(
            "I would be grateful if you could confirm Mr McDonald's admission for immediate rehabilitation.",
            "I would be grateful if you could transfer Mr McDonald to your rehabilitation service without delay.");
        AssertRuleFires(Lint(letter, "LT-TR"), "no_duplicated_request");
    }

    // ─── E. Mr David Taylor — urgent referral (LT-UR) ────

    internal const string TaylorUrgentReferralLetter = """
        Dr Malcom Still
        Rheumatologist
        City Hospital
        Suite 32
        55 Main Road
        Newtown

        13 June 2020

        Dear Dr Still,
        Re: Mr David Taylor, DOB: 1 August 1965

        I am writing to request your urgent rheumatological assessment and management of Mr Taylor, who has presented with a gout flare and an associated tophus.

        Today, Mr Taylor presented with pain in his right big toe, swelling of the toe and foot, right flank pain and red-coloured urine. Observations recorded a temperature of 37.8 °C, blood pressure 120/80 mmHg, heart rate 90 bpm and respiratory rate 22 breaths/min. He had shortness of breath. Examination revealed an inflamed, red right first toe with an underlying tophus, treated with colchicine, 1 mg, and NSAIDs.

        Mr Taylor has had gout since 2000, managed with allopurinol, paracetamol and colchicine. He experienced a severe attack in June 2010 and a further attack in September 2010. Kidney stones were also noted that year. He remained free of attacks from 2011 to 2020.

        Mr Taylor was diagnosed with depression in 2011, possibly related to gout, and has taken fluoxetine since then. His brother has gout, and his father died of kidney failure.

        I would be grateful if you could consider tophus removal, if clinically indicated, at your earliest convenience.

        Should there be any queries, kindly do not hesitate to contact me.

        Yours sincerely,

        Doctor
""";

    [Fact]
    public void Taylor_Urgent_Referral_Fixture_Lints_Clean_Against_Task()
    {
        Assert.Empty(Lint(TaylorUrgentReferralLetter, "LT-UR", taskText: TaylorTaskText));
    }

    // §7 injection 13: recipient spelling must copy the Writing Task exactly.
    [Fact]
    public void Injected_Recipient_Spelling_Mismatch_Is_Flagged()
    {
        AssertRuleFires(Lint(TaylorUrgentReferralLetter, "LT-UR", taskText: TaylorTaskText.Replace("Malcom", "Malcolm")),
            "recipient_name_mismatch");
    }

    // §7 injection 12: missing/weak vital-sign units and wrong unit spacing.
    [Fact]
    public void Injected_Bare_Per_Minute_Respiratory_Rate_Is_Flagged()
    {
        var letter = TaylorUrgentReferralLetter.Replace(
            "respiratory rate 22 breaths/min",
            "respiratory rate 22 /min");
        AssertRuleFires(Lint(letter, "LT-UR"), "respiratory_rate_unit_style");
    }

    [Fact]
    public void Injected_Compressed_Temperature_Unit_Is_Flagged()
    {
        var letter = TaylorUrgentReferralLetter.Replace("37.8 °C", "37.8°C");
        AssertRuleFires(Lint(letter, "LT-UR"), "value_unit_spacing");
    }

    [Fact]
    public void Injected_Unitless_Heart_And_Respiratory_Rates_Are_Flagged()
    {
        var letter = TaylorUrgentReferralLetter.Replace(
            "heart rate 90 bpm and respiratory rate 22 breaths/min",
            "heart rate 90 and respiratory rate 22");
        AssertRuleFires(Lint(letter, "LT-UR"), "numerical_values_have_units");
    }

    // §7 injection 14: the vague object — "possible removal" without its
    // clinical object.
    [Fact]
    public void Injected_Vague_Object_Is_Flagged()
    {
        var letter = TaylorUrgentReferralLetter.Replace(
            "consider tophus removal, if clinically indicated",
            "consider possible removal, if clinically indicated");
        AssertRuleFires(Lint(letter, "LT-UR"), "vague_clinical_object");
    }

    [Fact]
    public void Vague_Object_With_Named_Object_Passes()
    {
        var letter = TaylorUrgentReferralLetter.Replace(
            "consider tophus removal, if clinically indicated",
            "consider possible removal of the tophus, if clinically indicated");
        Assert.DoesNotContain(Lint(letter, "LT-UR"),
            f => f.RuleId.EndsWith("vague_clinical_object", StringComparison.Ordinal));
    }

    // §7 injection: judgmental observation (Taylor defect "appeared anxious").
    [Fact]
    public void Injected_Judgmental_Observation_Is_Flagged()
    {
        var letter = TaylorUrgentReferralLetter.Replace(
            "He had shortness of breath.",
            "He appeared anxious with shortness of breath.");
        AssertRuleFires(Lint(letter, "LT-UR"), "register_colloquial");
    }

    // ─── F. Candidate-mode false-positive firewall (addendum §6) ────

    // House paragraphing is a MODEL ANSWER rule: a candidate letter with the
    // request and contact offer merged into one closure paragraph is not a
    // template violation.
    [Fact]
    public void Candidate_Merged_Closure_Paragraphs_Are_Not_Penalised()
    {
        var letter = GarciaUpdateLetter.Replace(
            "I would be grateful if you could contact Ms Garcia's close contacts, advise them to seek prompt attention for unexplained illness and consider chemoprophylaxis.\n\nShould there be any queries, kindly do not hesitate to contact me.",
            "I would be grateful if you could contact Ms Garcia's close contacts, advise them to seek prompt attention for unexplained illness and consider chemoprophylaxis. Should there be any queries, kindly do not hesitate to contact me.");
        Assert.DoesNotContain(Lint(letter, "LT-DG", isModelAnswer: false),
            f => f.RuleId.EndsWith("closure_request_paragraph", StringComparison.Ordinal));
    }

    // Duplicate-request detection is phrase-based and Model Answer only —
    // candidates are assessed semantically, never by phrase matching.
    [Fact]
    public void Candidate_Duplicate_Request_Is_Not_Phrase_Matched()
    {
        var letter = McDonaldTransferLetter.Replace(
            "I would be grateful if you could confirm Mr McDonald's admission for immediate rehabilitation.",
            "I would be grateful if you could transfer Mr McDonald to your rehabilitation service without delay.");
        Assert.DoesNotContain(Lint(letter, "LT-TR", isModelAnswer: false),
            f => f.RuleId.EndsWith("no_duplicated_request", StringComparison.Ordinal));
    }

    // Results written as coordinated noun phrases (no comma splice) pass.
    [Fact]
    public void Coordinated_Result_Clauses_Pass()
    {
        Assert.DoesNotContain(Lint(GarciaUpdateLetter, "LT-DG"),
            f => f.RuleId.EndsWith("results_comma_splice", StringComparison.Ordinal));
    }

    // A genuine comma splice of two finite result clauses is detected.
    [Fact]
    public void Injected_Result_Comma_Splice_Is_Flagged()
    {
        var letter = GarciaUpdateLetter.Replace(
            "The white cell count was 14.0x10^9/L and the C-reactive protein level was 150.",
            "The white cell count was 14.0x10^9/L, the C-reactive protein level was 150.");
        AssertRuleFires(Lint(letter, "LT-DG"), "results_comma_splice");
    }
}
