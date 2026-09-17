using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// ULTIMATE FINAL master-specification regression fixtures (owner handoff,
/// 13 Sep 2026): the permanent Section 11 production-failure fixtures —
/// Weir (Medicine routine referral), Garcia (Medicine update-on-discharge,
/// including the false-READY §11.5 defects), Ramsey (Pharmacy) and Wright
/// (Physiotherapy) — plus the Section 17 candidate false-positive firewall
/// and the Section 15.1 provenance-completeness contract.
/// "A green validator cannot override a visible source/rule/render
/// violation": every defect the handoff lists is injected deliberately and
/// must be caught by the deterministic gate.
/// </summary>
public sealed class WritingUltimateFinalRegressionFixtureTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());
    private static readonly WritingAssessmentV11RuleEngine AssessmentEngine = new(Engine);

    private static List<LintFinding> LintModel(string letter, string letterType, ExamProfession profession = ExamProfession.Medicine,
        WritingCaseNotesMarkers? markers = null, bool patientIsMinor = false)
        => Engine.Lint(new WritingLintInput(
            LetterText: letter, LetterType: letterType, Profession: profession,
            CaseNotesMarkers: markers, PatientIsMinor: patientIsMinor, IsModelAnswer: true)).ToList();

    private static List<LintFinding> LintCandidate(string letter, string letterType, ExamProfession profession = ExamProfession.Medicine,
        WritingCaseNotesMarkers? markers = null)
        => Engine.Lint(new WritingLintInput(
            LetterText: letter, LetterType: letterType, Profession: profession,
            CaseNotesMarkers: markers, IsModelAnswer: false)).ToList();

    private static void AssertRuleFires(List<LintFinding> findings, string checkId)
        => Assert.Contains(findings, f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal));

    private static void AssertRuleDoesNotFire(List<LintFinding> findings, string checkId)
        => Assert.DoesNotContain(findings, f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal));

    // ─────────────────────────────────────────────────────────────────
    // §15.1 — provenance completeness: every deterministic check id MUST
    // declare its authority tag, its OET criterion and its candidate
    // behaviour. A finding without explainable provenance is not safe for
    // automatic candidate scoring.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Every_Supported_CheckId_Has_Provenance_And_Valid_Candidate_Behavior()
    {
        var missing = WritingRuleEngine.SupportedCheckIds
            .Where(id => !WritingRuleProvenance.ByCheckId.ContainsKey(id))
            .ToList();
        Assert.True(missing.Count == 0,
            $"Check ids without a provenance record: {string.Join(", ", missing)}. " +
            "Ultimate Final §15.1 requires every machine rule to declare its authority tag and candidate behaviour.");

        var invalid = WritingRuleProvenance.ByCheckId.Values
            .Where(p => p.CandidateBehavior is not (WritingCandidateBehaviors.ScoreBearing
                or WritingCandidateBehaviors.CoachingOnly
                or WritingCandidateBehaviors.AcceptAlternative
                or WritingCandidateBehaviors.NotApplicable))
            .Select(p => p.CandidateBehavior)
            .ToList();
        Assert.True(invalid.Count == 0, $"Invalid candidate behaviour values: {string.Join(", ", invalid)}");
    }

    [Fact]
    public void Provenance_Tags_Use_Only_The_Ultimate_Final_Vocabulary()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            WritingProvenanceTags.SourceFactTask,
            WritingProvenanceTags.OetOfficial,
            WritingProvenanceTags.GeneralEnglishValidated,
            WritingProvenanceTags.OwnerModelAnswerCanonical,
            WritingProvenanceTags.ProfessionResource,
            WritingProvenanceTags.PreferredStyle,
        };
        var offenders = WritingRuleProvenance.ByCheckId
            .Where(kv => !allowed.Contains(kv.Value.Tag))
            .Select(kv => $"{kv.Key}={kv.Value.Tag}")
            .ToList();
        Assert.True(offenders.Count == 0, $"Unknown provenance tags: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void House_Style_And_Descriptor_Engine_Are_Stamped_Ultimate_Final()
    {
        Assert.Equal("cross-model-audit-2026-09-17", WritingRev8HouseStyle.Version);
        Assert.Contains("CANDIDATE FALSE-POSITIVE FIREWALL", WritingRev8HouseStyle.CandidateGradingRules);
        Assert.Contains("never manufacture a finding", WritingRev8HouseStyle.CandidateGradingRules);
        Assert.Contains("450-500", WritingOetDescriptors.DescriptorEngine);
        Assert.Contains("never double-penalise", WritingOetDescriptors.DescriptorEngine);
    }

    // ─────────────────────────────────────────────────────────────────
    // §11.1 — Medicine, Mr Michael Weir, routine neurology referral.
    // ─────────────────────────────────────────────────────────────────

    internal const string WeirRoutineReferralLetter = """
Dr Anne Ferguson
Neurologist
Riverton Specialist Centre
22 Collins Street
Riverton

18 September 2026

Dear Dr Ferguson,
Re: Mr Michael Weir, DOB: 4 June 1978

I am writing to refer Mr Weir, who has experienced recurrent headaches with visual disturbance over the past two months despite simple analgesia.

Mr Weir reports bilateral throbbing headaches occurring four times weekly, each lasting up to six hours, accompanied by nausea, photophobia and vomiting. He describes flashing lights in both visual fields before the onset of pain, and the headaches have recently begun to wake him at night. Paracetamol, 1 g, no longer relieves his symptoms.

Examination today was normal, with blood pressure 128/78 mmHg and no focal neurological signs. Fundoscopy was unremarkable, and cranial nerve and upper limb examination was normal. A random glucose measured 6.37 mmol/L two weeks ago.

Mr Weir has no significant past medical history and takes no regular medication. He smokes ten cigarettes daily and drinks alcohol occasionally, and his sleep is consistently interrupted. His mother has a history of migraine. He works as a long-distance driver and is concerned about his vision.

I would be grateful if you could assess Mr Weir for a possible migraine variant and exclude other causes.

Should there be any queries, kindly do not hesitate to contact me.

Yours sincerely,

Doctor
""";

    [Fact]
    public void Weir_Routine_Referral_Fixture_Lints_Clean()
        => Assert.Empty(LintModel(WeirRoutineReferralLetter, "LT-RR"));

    [Fact]
    public void Weir_Injected_Abbreviated_Year_Is_Flagged()
    {
        var letter = WeirRoutineReferralLetter.Replace("DOB: 4 June 1978", "DOB: 4 June '78");
        AssertRuleFires(LintModel(letter, "LT-RR"), "year_not_abbreviated");
    }

    [Fact]
    public void Weir_Injected_Full_Name_Repeated_In_Body_Is_Flagged()
    {
        var letter = WeirRoutineReferralLetter.Replace(
            "Mr Weir reports bilateral throbbing headaches",
            "Mr Michael Weir reports bilateral throbbing headaches");
        AssertRuleFires(LintModel(letter, "LT-RR"), "body_uses_last_name_only");
    }

    [Fact]
    public void Weir_Injected_The_Patient_Is_Flagged()
    {
        var letter = WeirRoutineReferralLetter.Replace(
            "Examination today was normal",
            "Examination of the patient today was normal");
        AssertRuleFires(LintModel(letter, "LT-RR"), "body_forbidden_phrase_the_patient");
    }

    [Fact]
    public void Weir_Injected_Smoker_Label_Is_Flagged()
    {
        var letter = WeirRoutineReferralLetter.Replace(
            "He smokes ten cigarettes daily",
            "He is a smoker with ten cigarettes daily");
        AssertRuleFires(LintModel(letter, "LT-RR"), "judgmental_labels");
    }

    [Fact]
    public void Weir_Injected_Pronoun_First_Paragraph_Is_Flagged()
    {
        var letter = WeirRoutineReferralLetter.Replace(
            "Mr Weir has no significant past medical history and takes no regular medication.",
            "He has no significant past medical history and takes no regular medication.");
        AssertRuleFires(LintModel(letter, "LT-RR"), "paragraph_start_patient_name");
    }

    [Fact]
    public void Weir_Injected_Colloquial_Tired_Sluggish_Is_Flagged()
    {
        var letter = WeirRoutineReferralLetter.Replace(
            "each lasting up to six hours, accompanied by nausea, photophobia and vomiting",
            "each lasting up to six hours, leaving him tired and sluggish with nausea, photophobia and vomiting");
        AssertRuleFires(LintModel(letter, "LT-RR"), "register_colloquial");
    }

    [Fact]
    public void Weir_Injected_MRI_Imaging_Redundancy_Is_Flagged()
    {
        var letter = WeirRoutineReferralLetter.Replace(
            "Fundoscopy was unremarkable,",
            "Fundoscopy was unremarkable and an MRI imaging scan was arranged,");
        AssertRuleFires(LintModel(letter, "LT-RR"), "register_colloquial");
    }

    [Fact]
    public void Weir_Injected_Missing_Unit_Space_Is_Flagged()
    {
        var letter = WeirRoutineReferralLetter.Replace("6.37 mmol/L", "6.37mmol/L");
        AssertRuleFires(LintModel(letter, "LT-RR"), "value_unit_spacing");
    }

    [Fact]
    public void Weir_Injected_Missing_Contact_Offer_Is_Flagged()
    {
        var letter = WeirRoutineReferralLetter.Replace("\n\nShould there be any queries, kindly do not hesitate to contact me.", string.Empty);
        AssertRuleFires(LintModel(letter, "LT-RR"), "closure_contact_offer");
    }

    // ─────────────────────────────────────────────────────────────────
    // §11.2 + §11.5 — Medicine, Ms Isabel Garcia, update-on-discharge,
    // including the false-READY defects that once passed as "zero findings".
    // ─────────────────────────────────────────────────────────────────

    internal const string GarciaDischargeLetter = """
Dr Paul Simpson
Simpson Family Medical Centre
14 Main Street
Riverton

3 March 2025

Dear Dr Simpson,
Re: Ms Isabel Garcia, DOB: 3 March 1950

I am writing to update you regarding Ms Garcia, who was discharged home today after treatment for bacterial meningitis, and to request your ongoing monitoring during her recovery.

Ms Garcia presented on 24 February with headache, fever, photophobia and neck stiffness. On examination, she was febrile at 38.2 degrees C, with positive Kernig's sign and no focal neurological deficit. Blood cultures grew Neisseria meningitidis, confirming bacterial meningitis.

Ms Garcia was admitted to the acute medical unit on the day of presentation. She received ceftriaxone, 2 g four times daily and dexamethasone, 10 mg four times daily, for seven days. Her fever and headache settled within two days, and repeat blood cultures on day five were negative. A hearing assessment before discharge showed no deficit. She has returned to her baseline neurological state, mobilises independently and was advised to rest for a further week.

Ms Garcia has been discharged home today with ciprofloxacin, 500 mg twice daily for three further days.

I would be grateful if you could monitor Ms Garcia's temperature, headache and rash, arrange a hearing review, and review her in one week.

Should there be any queries, kindly do not hesitate to contact me.

Yours sincerely,

Doctor
""";

    [Fact]
    public void Garcia_Discharge_Fixture_Lints_Clean()
        => Assert.Empty(LintModel(GarciaDischargeLetter, "LT-DG"));

    [Fact]
    public void Garcia_FalseReady_Surname_Only_Re_Line_Is_Flagged()
    {
        // §11.5: "Re: Ms Garcia instead of full Re: Ms Isabel Garcia must fail"
        // — this defect previously passed with "zero findings".
        var letter = GarciaDischargeLetter.Replace(
            "Re: Ms Isabel Garcia, DOB: 3 March 1950",
            "Re: Ms Garcia, DOB: 3 March 1950");
        AssertRuleFires(LintModel(letter, "LT-DG"), "re_line_full_name");
    }

    [Fact]
    public void Garcia_Adult_Untitled_Re_Line_Is_Flagged()
    {
        var letter = GarciaDischargeLetter.Replace(
            "Re: Ms Isabel Garcia, DOB: 3 March 1950",
            "Re: Isabel Garcia, DOB: 3 March 1950");
        AssertRuleFires(LintModel(letter, "LT-DG"), "re_line_full_name");
    }

    [Fact]
    public void Garcia_Child_Re_Line_Without_Title_Is_Accepted()
    {
        var childLetter = GarciaDischargeLetter.Replace(
            "Re: Ms Isabel Garcia, DOB: 3 March 1950",
            "Re: Tomas Garcia, DOB: 3 March 2019")
            .Replace("Ms Garcia", "Tomas").Replace("she was", "he was")
            .Replace("Her symptoms", "His symptoms").Replace("She has returned", "He has returned")
            .Replace("her temperature", "his temperature").Replace("her hearing", "his hearing")
            .Replace("review her", "review him").Replace("her baseline", "his baseline")
            .Replace("Ms Isabel Garcia, who", "Tomas Garcia, who").Replace("Dear Dr Simpson", "Dear Dr Simpson");
        var findings = LintModel(childLetter, "LT-DG", patientIsMinor: true);
        AssertRuleDoesNotFire(findings, "re_line_full_name");
    }

    [Fact]
    public void Garcia_FalseReady_Incomplete_Construction_Is_Flagged()
    {
        // §11.5: "grammatically incomplete constructions such as
        // 'Examination showed afebrile...' must fail Language".
        var letter = GarciaDischargeLetter.Replace(
            "On examination, she was febrile at 38.2 degrees C, with positive Kernig's sign",
            "Examination showed afebrile despite a temperature of 38.2 degrees C, with positive Kernig's sign");
        Assert.NotEqual(GarciaDischargeLetter, letter);
        AssertRuleFires(LintModel(letter, "LT-DG"), "incomplete_clinical_construction");
    }

    [Fact]
    public void Garcia_Mixed_Date_Families_Are_Flagged()
    {
        var letter = GarciaDischargeLetter.Replace("presented on 24 February", "presented on 24/02/2025");
        AssertRuleFires(LintModel(letter, "LT-DG"), "date_format_consistent");
    }

    [Fact]
    public void Garcia_Dob_Without_Colon_Is_Flagged()
    {
        var letter = GarciaDischargeLetter.Replace("DOB: 3 March 1950", "DOB 3 March 1950");
        AssertRuleFires(LintModel(letter, "LT-DG"), "dob_colon_format");
    }

    [Fact]
    public void Garcia_Dotted_Dob_Is_Flagged()
    {
        var letter = GarciaDischargeLetter.Replace("DOB: 3 March 1950", "D.O.B: 3 March 1950");
        AssertRuleFires(LintModel(letter, "LT-DG"), "dob_colon_format");
    }

    [Fact]
    public void Garcia_Missing_Dose_Unit_Space_Is_Flagged()
    {
        var letter = GarciaDischargeLetter.Replace("ceftriaxone, 2 g four times daily", "ceftriaxone, 2g four times daily");
        AssertRuleFires(LintModel(letter, "LT-DG"), "value_unit_spacing");
    }

    [Fact]
    public void Garcia_Narrative_Duration_As_Digits_Is_Flagged()
    {
        var letter = GarciaDischargeLetter.Replace("for seven days", "for 7 days");
        AssertRuleFires(LintModel(letter, "LT-DG"), "number_style_words_vs_digits");
    }

    [Fact]
    public void Garcia_Pronoun_First_Body_Paragraph_Is_Flagged()
    {
        var letter = GarciaDischargeLetter.Replace(
            "Ms Garcia was admitted to the acute medical unit",
            "She was admitted to the acute medical unit");
        AssertRuleFires(LintModel(letter, "LT-DG"), "paragraph_start_patient_name");
    }

    [Fact]
    public void Garcia_Missing_Universal_Contact_Sentence_Is_Flagged()
    {
        var letter = GarciaDischargeLetter.Replace("\n\nShould there be any queries, kindly do not hesitate to contact me.", string.Empty);
        AssertRuleFires(LintModel(letter, "LT-DG"), "closure_contact_offer");
    }

    // §3.3 — discharge vs simple update: a letter whose notes do NOT
    // document an admission must never claim one.
    [Fact]
    public void Simple_Update_Inventing_Discharge_Is_Flagged()
    {
        var markers = new WritingCaseNotesMarkers(AdmissionDocumented: false, DischargeDocumented: false);
        var letter = GarciaDischargeLetter.Replace(
            "who was discharged home today after treatment for bacterial meningitis",
            "who is ready for discharge following treatment of her symptoms");
        AssertRuleFires(LintModel(letter, "LT-RR", markers: markers), "discharge_language_unsupported");
    }

    [Fact]
    public void Discharge_Language_With_Documented_Admission_Is_Accepted()
    {
        var markers = new WritingCaseNotesMarkers(AdmissionDocumented: true, DischargeDocumented: true);
        var findings = LintModel(GarciaDischargeLetter, "LT-DG", markers: markers);
        AssertRuleDoesNotFire(findings, "discharge_language_unsupported");
    }

    [Fact]
    public void Discharge_Source_Check_Is_Skipped_Without_Notes_Snapshot()
    {
        // No CaseNotesMarkers (e.g. lint without a source snapshot): the
        // source-fidelity direction cannot be proven, so it must not fire.
        var findings = LintModel(GarciaDischargeLetter, "LT-DG", markers: null);
        AssertRuleDoesNotFire(findings, "discharge_language_unsupported");
    }

    [Fact]
    public void Vaginal_Discharge_Is_Not_Conflated_With_Hospital_Discharge()
    {
        var markers = new WritingCaseNotesMarkers(AdmissionDocumented: false, DischargeDocumented: false);
        var letter = WeirRoutineReferralLetter.Replace(
            "He smokes ten cigarettes daily and drinks alcohol occasionally.",
            "He smokes ten cigarettes daily and reported a vaginal discharge assessment last year for completeness.");
        var findings = LintModel(letter, "LT-RR", markers: markers);
        AssertRuleDoesNotFire(findings, "discharge_language_unsupported");
    }

    // ─────────────────────────────────────────────────────────────────
    // §11.3 — Pharmacy, Mrs Alice Ramsey, medication-regimen letter.
    // ─────────────────────────────────────────────────────────────────

    internal const string RamseyPharmacyLetter = """
Ms Dana Woolf
Care Manager
Rosewood Supported Living
8 Orchard Lane
Riverton

12 August 2026

Dear Ms Woolf,
Re: Mrs Alice Ramsey, DOB: 17 November 1941

I am writing to request medication support for Mrs Ramsey, who has had difficulty managing her prescribed regimen since her discharge from hospital.

Mrs Ramsey takes metformin, 1 g twice daily; perindopril, 5 mg in the morning and aspirin, 100 mg daily. She has missed doses on occasion because her current blister packs are difficult to open, and her daughter reports finding tablets loose in her handbag over the past month. Mrs Ramsey has atrial fibrillation alongside type two diabetes and hypertension. Consistent daily administration is clinically important for stroke risk reduction, and missed doses would be a significant safety concern for the care team supporting her at home.

Mrs Ramsey's general practitioner reviewed the regimen this week, confirmed its appropriateness and asked for pharmacy support with blister-pack dispensing. Mrs Ramsey is willing to accept help with administration at breakfast and at bedtime, and her daughter visits each weekend to reinforce the routine. No dose changes are required at present, and her most recent blood results were stable.

I would be grateful if you could arrange weekly blister packs and a medication administration check.

Should there be any queries, kindly do not hesitate to contact me.

Yours sincerely,

Pharmacist
""";

    [Fact]
    public void Ramsey_Pharmacy_Fixture_Lints_Clean()
        => Assert.Empty(LintModel(RamseyPharmacyLetter, "LT-OT", ExamProfession.Pharmacy));

    [Fact]
    public void Ramsey_Injected_Your_Mother_Opening_Is_Flagged()
    {
        var letter = RamseyPharmacyLetter.Replace(
            "I am writing to request medication support for Mrs Ramsey, who has had difficulty",
            "Your mother Mrs Ramsey has had difficulty");
        AssertRuleFires(LintModel(letter, "LT-OT", ExamProfession.Pharmacy), "intro_opens_i_am_writing_to");
    }

    [Fact]
    public void Ramsey_Injected_Relationship_Label_Is_Flagged()
    {
        var letter = RamseyPharmacyLetter.Replace(
            "Mrs Ramsey takes metformin, 1 g twice daily",
            "Your mother takes metformin, 1 g twice daily");
        AssertRuleFires(LintModel(letter, "LT-OT", ExamProfession.Pharmacy), "relationship_label_patient_reference");
    }

    [Fact]
    public void Ramsey_Injected_Comma_Only_Medication_List_Is_Flagged()
    {
        var letter = RamseyPharmacyLetter.Replace(
            "metformin, 1 g twice daily; perindopril, 5 mg in the morning and aspirin, 100 mg daily",
            "metformin, 1 g twice daily, perindopril, 5 mg in the morning, aspirin, 100 mg daily");
        AssertRuleFires(LintModel(letter, "LT-OT", ExamProfession.Pharmacy), "medication_list_punctuation");
    }

    [Fact]
    public void Ramsey_Injected_Also_Connective_Is_Flagged()
    {
        var letter = RamseyPharmacyLetter.Replace(
            "confirmed its appropriateness and asked for pharmacy support",
            "confirmed its appropriateness and also asked for pharmacy support");
        AssertRuleFires(LintModel(letter, "LT-OT", ExamProfession.Pharmacy), "linker_avoid_words");
    }

    [Fact]
    public void Ramsey_Injected_Missing_Contact_Sentence_Is_Flagged()
    {
        var letter = RamseyPharmacyLetter.Replace("\n\nShould there be any queries, kindly do not hesitate to contact me.", string.Empty);
        AssertRuleFires(LintModel(letter, "LT-OT", ExamProfession.Pharmacy), "closure_contact_offer");
    }

    // ─────────────────────────────────────────────────────────────────
    // §11.4 — Physiotherapy, Mrs Melanie Wright, knee referral.
    // ─────────────────────────────────────────────────────────────────

    internal const string WrightKneeReferralLetter = """
Mr James Okafor
Physiotherapist
Riverton Sports Medicine
40 Bank Place
Riverton

29 April 2026

Dear Mr Okafor,
Re: Mrs Melanie Wright, DOB: 8 June 1973

I am writing to refer Mrs Wright for physiotherapy following an acute left knee injury sustained while netball training two days ago.

Mrs Wright felt a popping sensation in her left knee during a sudden change of direction, with immediate swelling and an inability to bear weight. The knee is now bruised over the medial joint line, with a moderate effusion and restricted flexion to ninety degrees. She is unable to straighten the knee fully and reports a feeling of instability on stairs. She does not currently have a physiotherapist and manages her fitness independently.

An X-ray today showed no fracture. Mrs Wright has been using crutches, ice and elevation at home, with paracetamol, 1 g four times daily, for pain, and the swelling has gradually decreased since the injury. She is eager to return to competitive netball within the season and understands the need for supervised rehabilitation first.

I would be grateful if you could assess Mrs Wright, establish a quadriceps strengthening programme with progression milestones and advise on bracing together with a safe return-to-sport timeline.

Should there be any queries, kindly do not hesitate to contact me.

Yours sincerely,

Doctor
""";

    [Fact]
    public void Wright_Knee_Referral_Fixture_Lints_Clean()
        => Assert.Empty(LintModel(WrightKneeReferralLetter, "LT-RR", ExamProfession.Physiotherapy));

    [Fact]
    public void Wright_Injected_Dotted_Dob_Is_Flagged()
    {
        var letter = WrightKneeReferralLetter.Replace("DOB: 8 June 1973", "D.O.B: 8 June 1973");
        AssertRuleFires(LintModel(letter, "LT-RR", ExamProfession.Physiotherapy), "dob_colon_format");
    }

    [Fact]
    public void Wright_Injected_Repeated_But_Is_Flagged()
    {
        var letter = WrightKneeReferralLetter.Replace(
            "The knee is now bruised over the medial joint line, with a moderate effusion and restricted flexion to ninety degrees.",
            "The knee is bruised but swollen, but she can partially weight-bear, but flexion is restricted to ninety degrees.");
        AssertRuleFires(LintModel(letter, "LT-RR", ExamProfession.Physiotherapy), "linker_avoid_words");
    }

    [Fact]
    public void Wright_Injected_Casual_Pop_Is_Flagged()
    {
        var letter = WrightKneeReferralLetter.Replace(
            "Mrs Wright felt a popping sensation in her left knee",
            "Mrs Wright felt something pop in her left knee");
        AssertRuleFires(LintModel(letter, "LT-RR", ExamProfession.Physiotherapy), "register_colloquial");
    }

    [Fact]
    public void Wright_Injected_Management_After_Closure_Request_Is_Flagged()
    {
        var letter = WrightKneeReferralLetter.Replace(
            "advise on bracing together with a safe return-to-sport timeline.",
            "advise on bracing together with a safe return-to-sport timeline. She was advised to ice the knee hourly and to elevate the limb.");
        var findings = LintModel(letter, "LT-RR", ExamProfession.Physiotherapy);
        Assert.True(
            findings.Any(f => f.RuleId.EndsWith("closure_contains_management", StringComparison.Ordinal)
                           || f.RuleId.EndsWith("closure_request_paragraph", StringComparison.Ordinal)),
            "Management advice after the closing request must be flagged by the closure battery.");
    }

    // ─────────────────────────────────────────────────────────────────
    // §17 — candidate false-positive firewall: house-style deviations
    // may be REPORTED for learning, but every one of them is
    // coaching-only (or an accepted alternative), never score-bearing.
    // ─────────────────────────────────────────────────────────────────

    private static List<WritingAssessmentRuleFinding> AssessCandidate(string letter, string letterType = "LT-RR")
        => AssessmentEngine.Evaluate(new WritingLintInput(
            LetterText: letter, LetterType: letterType, Profession: ExamProfession.Medicine,
            IsModelAnswer: false)).ToList();

    [Fact]
    public void Candidate_Professional_Opening_Without_Canonical_Phrase_Never_Fires()
    {
        var letter = WeirRoutineReferralLetter.Replace(
            "I am writing to refer Mr Weir, who has experienced",
            "Please accept this referral of Mr Weir, who has experienced");
        var findings = AssessCandidate(letter);
        Assert.DoesNotContain(findings, f => f.RuleId.EndsWith("intro_opens_i_am_writing_to", StringComparison.Ordinal));
    }

    [Fact]
    public void Candidate_The_Patient_Is_Coaching_Only()
    {
        var letter = WeirRoutineReferralLetter.Replace(
            "Examination today was normal",
            "Examination of the patient today was normal");
        var finding = AssessCandidate(letter)
            .Single(f => f.RuleId.EndsWith("body_forbidden_phrase_the_patient", StringComparison.Ordinal));
        Assert.Equal(WritingCandidateBehaviors.CoachingOnly, finding.CandidateBehavior);
        Assert.Equal(WritingProvenanceTags.OwnerModelAnswerCanonical, finding.ProvenanceTag);
    }

    [Fact]
    public void Candidate_House_Linkers_Are_Coaching_Only()
    {
        var letter = WeirRoutineReferralLetter.Replace(
            "Paracetamol, 1 g, no longer relieves his symptoms.",
            "Paracetamol, 1 g, no longer relieves his symptoms, so he has been using rest in a dark room.");
        var finding = AssessCandidate(letter)
            .Single(f => f.RuleId.EndsWith("linker_avoid_words", StringComparison.Ordinal));
        Assert.Equal(WritingCandidateBehaviors.CoachingOnly, finding.CandidateBehavior);
    }

    [Fact]
    public void Candidate_Descriptive_Digit_Is_Coaching_Only()
    {
        var letter = GarciaDischargeLetter.Replace("for seven days", "for 7 days");
        var finding = AssessCandidate(letter, "LT-DG")
            .Single(f => f.RuleId.EndsWith("number_style_words_vs_digits", StringComparison.Ordinal));
        Assert.Equal(WritingCandidateBehaviors.CoachingOnly, finding.CandidateBehavior);
    }

    [Fact]
    public void Candidate_Alternate_Medication_List_Punctuation_Is_Coaching_Only()
    {
        var letter = RamseyPharmacyLetter.Replace(
            "metformin, 1 g twice daily; perindopril, 5 mg in the morning and aspirin, 100 mg daily",
            "metformin 1 g twice daily, perindopril 5 mg in the morning and aspirin 100 mg daily");
        var finding = AssessmentEngine.Evaluate(new WritingLintInput(
            LetterText: letter, LetterType: "LT-OT", Profession: ExamProfession.Pharmacy,
            IsModelAnswer: false)).Where(f => f.RuleId.EndsWith("medication_list_punctuation", StringComparison.Ordinal)).First();
        Assert.Equal(WritingCandidateBehaviors.CoachingOnly, finding.CandidateBehavior);
    }

    [Fact]
    public void Candidate_Five_Body_Paragraphs_Are_Coaching_Only()
    {
        var letter = WeirRoutineReferralLetter.Replace(
            "His mother has a history of migraine.",
            "His mother has a history of migraine.\n\nMr Weir holds a commercial driver licence, which requires annual medical review.");
        var finding = AssessCandidate(letter)
            .Single(f => f.RuleId.EndsWith("letter_paragraph_count", StringComparison.Ordinal));
        Assert.Equal(WritingCandidateBehaviors.CoachingOnly, finding.CandidateBehavior);
    }

    [Fact]
    public void Candidate_Missing_Contact_Offer_Is_Coaching_Only()
    {
        var letter = WeirRoutineReferralLetter.Replace(
            "Should there be any queries, kindly do not hesitate to contact me.",
            "Thank you for your continued care of Mr Weir.");
        var finding = AssessCandidate(letter)
            .Single(f => f.RuleId.EndsWith("closure_contact_offer", StringComparison.Ordinal));
        Assert.Equal(WritingCandidateBehaviors.CoachingOnly, finding.CandidateBehavior);
    }

    [Fact]
    public void Candidate_Brackets_Are_Coaching_Only()
    {
        var letter = WeirRoutineReferralLetter.Replace(
            "blood pressure 128/78 mmHg",
            "blood pressure 128/78 mmHg (measured seated)");
        var finding = AssessCandidate(letter)
            .Where(f => f.RuleId.EndsWith("no_brackets_in_letter", StringComparison.Ordinal)).First();
        Assert.Equal(WritingCandidateBehaviors.CoachingOnly, finding.CandidateBehavior);
    }

    [Fact]
    public void Candidate_Dob_Without_Colon_Is_Coaching_Only()
    {
        var letter = GarciaDischargeLetter.Replace("DOB: 3 March 1950", "DOB 3 March 1950");
        var finding = AssessCandidate(letter, "LT-DG")
            .Single(f => f.RuleId.EndsWith("dob_colon_format", StringComparison.Ordinal));
        Assert.Equal(WritingCandidateBehaviors.CoachingOnly, finding.CandidateBehavior);
    }

    [Fact]
    public void Clean_Professional_Candidate_Letter_Has_Zero_Score_Bearing_Findings()
    {
        // §17 "clean answer": a candidate letter that differs from the Model
        // Answer in wording and organisation but is professionally correct
        // must produce no score-bearing deterministic finding. §9.1: "Do not
        // generate mistakes just to fill feedback."
        var letter = WeirRoutineReferralLetter.Replace(
            "I am writing to refer Mr Weir, who has experienced recurrent headaches with visual disturbance over the past two months.",
            "Please accept this referral of Mr Weir. He has had recurrent headaches with visual disturbance for the past two months.");
        var scoreBearing = AssessCandidate(letter)
            .Where(f => f.CandidateBehavior == WritingCandidateBehaviors.ScoreBearing)
            .ToList();
        Assert.True(scoreBearing.Count == 0,
            $"Protected-alternative letter produced score-bearing findings: {string.Join("; ", scoreBearing.Select(f => $"{f.RuleId}: {f.Message}"))}");
    }

    [Fact]
    public void Genuine_Candidate_Errors_Remain_Score_Bearing()
    {
        // The firewall never protects actual errors: invented facts stay
        // score-bearing in candidate mode too (§9 source-fidelity rows).
        var markers = new WritingCaseNotesMarkers(AdmissionDocumented: false, DischargeDocumented: false);
        var letter = GarciaDischargeLetter.Replace(
            "who was discharged home today after treatment for bacterial meningitis",
            "who is ready for discharge following treatment of her symptoms");
        var finding = AssessmentEngine.Evaluate(new WritingLintInput(
            LetterText: letter, LetterType: "LT-RR", Profession: ExamProfession.Medicine,
            CaseNotesMarkers: markers, IsModelAnswer: false))
            .Single(f => f.RuleId.EndsWith("discharge_language_unsupported", StringComparison.Ordinal));
        Assert.Equal(WritingCandidateBehaviors.ScoreBearing, finding.CandidateBehavior);
        Assert.Equal(WritingProvenanceTags.SourceFactTask, finding.ProvenanceTag);
        Assert.Equal("content", finding.PrimaryCriterionCode);
    }

    [Fact]
    public void Every_Finding_Carries_Provenance_Criterion_And_Correction_Contract()
    {
        // §15.1: evidence + rule id + provenance + criterion + severity +
        // score-bearing status + correction are all present on the finding.
        var letter = GarciaDischargeLetter.Replace("DOB: 3 March 1950", "DOB 3 March 1950");
        var findings = AssessCandidate(letter, "LT-DG");
        Assert.Contains(findings, f =>
            f.RuleId.EndsWith("dob_colon_format", StringComparison.Ordinal)
            && !string.IsNullOrEmpty(f.ProvenanceTag)
            && !string.IsNullOrEmpty(f.CandidateBehavior)
            && !string.IsNullOrEmpty(f.PrimaryCriterionCode)
            && !string.IsNullOrEmpty(f.Severity)
            && !string.IsNullOrEmpty(f.Message));
    }
}
