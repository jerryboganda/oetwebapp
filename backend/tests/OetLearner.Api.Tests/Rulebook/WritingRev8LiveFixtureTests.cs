using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Rulebook;

/// <summary>
/// PERMANENT regression fixtures (Writing Rule Enforcement &amp; Model Answer
/// Validation Addendum Rev8, 11 Sep 2026, §9.4, §14, §15, §18): the live
/// candidate-facing Model Answers that were reported "clean" while still
/// breaking known owner rules. Each defect letter must keep failing the
/// Model-Answer-mode validator with every documented defect class; each
/// owner-compliant correction must pass with ZERO blocking findings (proving
/// the stricter canonical house style is satisfiable, not a blanket block).
/// A future change must not be releasable if any of these defect classes
/// reappears.
/// </summary>
public sealed class WritingRev8LiveFixtureTests
{
    private readonly WritingRuleEngine _engine = new(new RulebookLoader());

    private IReadOnlyList<LintFinding> Model(string letter, string letterType, ExamProfession profession)
        => _engine.Lint(new WritingLintInput(letter, letterType, Profession: profession, IsModelAnswer: true));

    private IReadOnlyList<LintFinding> Candidate(string letter, string letterType, ExamProfession profession)
        => _engine.Lint(new WritingLintInput(letter, letterType, Profession: profession));

    private static void AssertFires(IReadOnlyList<LintFinding> findings, params string[] checkIds)
    {
        foreach (var id in checkIds)
            Assert.True(findings.Any(f => f.RuleId == $"BUILTIN.{id}"),
                $"Expected BUILTIN.{id} to fire. Actual: {string.Join(", ", findings.Select(f => f.RuleId).Distinct())}");
    }

    private static void AssertClean(IReadOnlyList<LintFinding> findings)
    {
        var blocking = WritingRuleEngine.ModelAnswerBlockingFindings(findings);
        Assert.True(blocking.Count == 0,
            "Owner-compliant Model Answer must have zero blocking findings. Actual: "
            + string.Join(" | ", blocking.Select(f => $"{f.RuleId}: {f.Message} [{f.Quote}]")));
    }

    // ── Live defect: Medicine urgent rheumatology referral, Mr David Taylor
    // (task 07d56634-dc0f-4afc-9b3d-ae1d527f1314, LT-UR) — Addendum §1-§2 screenshot.
    private const string TaylorLiveDefect = """
Dr Malcom Still
Rheumatologist
City Hospital
Suite 32
55 Main Road
Newtown

13 June 2020

Dear Dr Still,
Re: Mr David Taylor, aged 55
I am writing to refer Mr David Taylor, aged 55, for urgent rheumatological assessment of an acute gout flare with an associated tophus.

He was diagnosed with gout in 2000, treated with colchicine, allopurinol and paracetamol, and his brother also has gout. He suffered attacks on 1 June and 1 September 2010, treated with a steroid injection, colchicine and allopurinol, and was advised to stop smoking, reduce alcohol and red meat, and increase fluids. He adhered well and remained attack-free from 2011 until now.

He presented on 13 June 2020 with pain and swelling of his right big toe and foot, right flank pain and red-coloured urine. Observations were blood pressure 120/80, heart rate 90, respiratory rate 22 and temperature 37.8°C. His right first toe was inflamed and red, with a tophus beneath it, and he was commenced on colchicine 1 mg and NSAIDs.

Given his flank pain, red urine, previous kidney stones and a family history of renal failure, he was referred to a urologist.

I would be grateful if you could urgently assess Mr Taylor's gout, with a view to possible removal of the tophus.

Yours sincerely,

Doctor
""";

    [Fact]
    public void Taylor_LiveDefect_ModelAnswer_Fails_Every_Documented_Rule()
    {
        var findings = Model(TaylorLiveDefect, "LT-UR", ExamProfession.Medicine);
        AssertFires(findings,
            "blank_line_after_re_line",        // no blank line between Re: and introduction
            "age_not_duplicated_in_intro",     // "aged 55" in Re: AND introduction
            "body_uses_last_name_only",        // full name repeated after the Re: line
            "urgent_body_starts_today",        // urgent body starts with 2000 history
            "body_no_todays_date",             // "13 June 2020" repeated in the body
            "urgent_closure_phrase",           // no "at your earliest convenience"
            "urgent_token_not_repeated",       // "urgently" in the closure
            "closure_contact_offer",           // no universal contact-offer sentence
            "paragraph_start_patient_name",    // body paragraphs open with "He"
            "linker_avoid_words",              // "his brother also has gout"
            "emotional_wording",               // "He suffered attacks"
            "medication_list_punctuation");    // "colchicine 1 mg" without comma
        Assert.NotEmpty(WritingRuleEngine.ModelAnswerBlockingFindings(findings));
    }

    [Fact]
    public void Taylor_LiveDefect_As_A_Candidate_Letter_Accepts_Professional_Urgent_Wording()
    {
        // Addendum §11: a candidate is never penalised for "urgently" in the
        // closure or for not copying "at your earliest convenience"; the
        // factual "suffered attacks" is not emotional editorialising.
        var findings = Candidate(TaylorLiveDefect, "LT-UR", ExamProfession.Medicine);
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.urgent_closure_phrase");
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.urgent_token_not_repeated");
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.emotional_wording");
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.intro_opens_i_am_writing_to");
        // ...while the same hard rules still apply to the candidate.
        AssertFires(findings, "blank_line_after_re_line", "age_not_duplicated_in_intro",
            "body_uses_last_name_only", "urgent_body_starts_today", "paragraph_start_patient_name",
            "closure_contact_offer");
    }

    private const string TaylorCorrected = """
Dr Malcom Still
Rheumatologist
City Hospital
Suite 32
55 Main Road
Newtown

13 June 2020

Dear Dr Still,
Re: Mr David Taylor, aged 55

I am writing to urgently refer Mr Taylor for rheumatological assessment of an acute gout flare with an associated tophus.

On today's visit, Mr Taylor presented with a painful, swollen right big toe and foot, right flank pain and red-coloured urine. His blood pressure was 120/80 mmHg, heart rate 90 per minute and temperature 37.8°C. His right first toe was inflamed with an underlying tophus, and he was commenced on colchicine, 1 mg, and anti-inflammatory medication.

Mr Taylor was diagnosed with gout in 2000 and treated with colchicine, allopurinol and paracetamol. Further attacks on 1 June 2010 and 1 September 2010 were treated with a steroid injection, colchicine and allopurinol. He was advised to stop smoking, reduce alcohol and red meat, and increase fluids; subsequently, he remained attack-free from 2011.

Mr Taylor has previous kidney stones, a family history of renal failure and a brother with gout. Given his flank pain and red urine, he was referred to a urologist.

I would be grateful if you could assess Mr Taylor at your earliest convenience, with a view to possible removal of the tophus. Should there be any queries, kindly do not hesitate to contact me.

Yours sincerely,

Doctor
""";

    [Fact]
    public void Taylor_OwnerCompliant_Correction_Passes_With_Zero_Blocking_Findings()
        => AssertClean(Model(TaylorCorrected, "LT-UR", ExamProfession.Medicine));

    // ── Live defect §15.1: Medicine neurology referral, Mr Michael Weir
    // (task 211cc065-4efb-49f6-9fd8-2526c9fcb18b, LT-RR).
    private const string WeirLiveDefect = """
Dr M McLaren
Neurologist
Suite 3
67 The Crescent
Newtown

11.08.14

Dear Dr McLaren,
Re: Mr Michael Weir

I am writing to refer Mr Michael Weir, a patient at this practice, for a full neurological assessment; the working assessment is possible multiple sclerosis.

Mr Weir is a smoker and has been overweight long term. He has depression, treated with sertraline hydrochloride, also known as Zoloft, since September 2012.

He first presented on 29.06.14 feeling tired, stressed and sluggish, and cholesterol and full blood count testing were arranged. On review on 07.07.14, he remained tired and low in mood and had developed left leg weakness. His cholesterol was 6.37mmol/L, and his blood count showed low white cell count, red cell count, haemoglobin and haematocrit. He was assessed for hypercholesterolaemia and advised on lifestyle changes.

On 09.08.14, he reported dizziness, two blackouts lasting a few minutes each, tingling in his hands, ongoing left leg weakness, breathlessness and occasional constipation, and was still smoking. Examination revealed bilateral sensory loss in his hands and a diminished left patellar reflex. A CT scan of the head and spine has been arranged to exclude central causes.

I would be grateful if you could assess him further, with MRI imaging if indicated.

Yours sincerely,
Doctor
""";

    [Fact]
    public void Weir_LiveDefect_ModelAnswer_Fails_Every_Documented_Rule()
    {
        var findings = Model(WeirLiveDefect, "LT-RR", ExamProfession.Medicine);
        AssertFires(findings,
            "year_not_abbreviated",                // "11.08.14", "29.06.14" ...
            "body_uses_last_name_only",            // "Mr Michael Weir" in the introduction
            "body_forbidden_phrase_the_patient",   // "a patient at this practice"
            "judgmental_labels",                   // "is a smoker"
            "paragraph_start_patient_name",        // "He first presented" / "On 09.08.14, he"
            "register_colloquial",                 // "tired", "sluggish", "MRI imaging"
            "value_unit_spacing",                  // "6.37mmol/L"
            "closure_contact_offer",
            "linker_avoid_words",                  // "also known as"
            "signoff_designation_present");        // no blank line before the designation
    }

    private const string WeirCorrected = """
Dr M McLaren
Neurologist
Suite 3
67 The Crescent
Newtown

11 August 2014

Dear Dr McLaren,
Re: Mr Michael Weir

I am writing to refer Mr Weir, who is presenting with features suggestive of multiple sclerosis, for a full neurological assessment.

On 9 August 2014, Mr Weir reported dizziness, two blackouts lasting a few minutes each, tingling in his hands, ongoing left leg weakness, breathlessness and occasional constipation. Examination revealed bilateral sensory loss in his hands and a diminished left patellar reflex. A CT scan of the head and spine has been arranged to exclude central causes.

Mr Weir first presented on 29 June 2014 with fatigue and stress, and blood tests were arranged. On review on 7 July 2014, he reported persistent fatigue and low mood and had developed left leg weakness. His cholesterol was 6.37 mmol/L, and his blood count showed a low white cell count, red cell count, haemoglobin and haematocrit. He was assessed for hypercholesterolaemia and advised on lifestyle changes.

Mr Weir has depression, treated with sertraline hydrochloride, known as Zoloft, since September 2012. He smokes and has been overweight long term.

I would be grateful if you could assess Mr Weir, including MRI if indicated. Should there be any queries, kindly do not hesitate to contact me.

Yours sincerely,

Doctor
""";

    [Fact]
    public void Weir_OwnerCompliant_Correction_Passes_With_Zero_Blocking_Findings()
        => AssertClean(Model(WeirCorrected, "LT-RR", ExamProfession.Medicine));

    // ── Live defect §15.2: Medicine meningitis update, Ms Garcia
    // (task bb8982f4-9ee6-43cf-814e-7d876ab11168, LT-DG).
    private const string GarciaLiveDefect = """
Dr Lorna Bradbury
Stillwater Medical Clinic
12 Main Street
Stillwater

23 May 2015


Dear Dr Bradbury,
Re: Ms Garcia, DOB 01.01.1995

I am writing to update you on Ms Garcia, a university student referred to the Emergency Department today with suspected meningitis, following a one-week history of painful, stiff joints, headache, neck stiffness, photophobia, bruising and a rash.

On examination, she was afebrile, with bruising to her left arm, a petechial rash on her abdomen and legs, and was unable to touch her chin to her chest when supine. Blood tests showed a raised white cell count and C-reactive protein, and lumbar puncture findings were consistent with bacterial meningitis; subsequent culture confirmed Neisseria meningitidis.

She was commenced on intravenous ceftriaxone 2g twice daily, together with dexamethasone 10mg before the first dose and then every 6 hours for 4 days. Once culture results were confirmed, treatment changed to intravenous benzylpenicillin 1.8g every 4 hours for 5 days. She has responded well to treatment.

The Department of Human Services has been notified, and family members were advised to be immunised. Could you please contact her close family and friends, advise them to seek prompt medical attention and observe for signs of unexplained illness, and consider whether chemoprophylaxis is indicated for recent close contacts.

Yours sincerely,
Doctor
""";

    [Fact]
    public void Garcia_LiveDefect_ModelAnswer_Fails_Every_Documented_Rule()
    {
        var findings = Model(GarciaLiveDefect, "LT-DG", ExamProfession.Medicine);
        AssertFires(findings,
            "dob_colon_format",                // "DOB 01.01.1995" (no colon)
            "date_format_consistent",          // "23 May 2015" + "01.01.1995"
            "paragraph_start_patient_name",    // "On examination, she ..." / "She was commenced ..."
            "value_unit_spacing",              // "2g", "10mg", "1.8g"
            "medication_list_punctuation",     // "ceftriaxone 2g" without comma
            "number_style_words_vs_digits",    // "every 6 hours for 4 days"
            "closure_contact_offer",
            "signoff_designation_present");
    }

    private const string GarciaCorrected = """
Dr Lorna Bradbury
Stillwater Medical Clinic
12 Main Street
Stillwater

23 May 2015

Dear Dr Bradbury,
Re: Ms Isabel Garcia, DOB: 1 January 1995

I am writing to update you regarding Ms Garcia, who was admitted today with bacterial meningitis, and to request follow-up of her close contacts.

On examination, Ms Garcia was afebrile, with bruising to her left arm and a petechial rash on her abdomen and legs. She was unable to touch her chin to her chest when supine. Blood tests showed raised white cell count and C-reactive protein, lumbar puncture findings were consistent with bacterial meningitis, and culture confirmed Neisseria meningitidis.

Ms Garcia was commenced on intravenous ceftriaxone, 2 g twice daily, and dexamethasone, 10 mg before the first dose and then every six hours for four days. After culture confirmation, treatment was changed to intravenous benzylpenicillin, 1.8 g every four hours for five days. She has responded well to treatment.

Ms Garcia's case has been notified to the Department of Human Services, and her family members were advised to be immunised.

I would be grateful if you could advise Ms Garcia's family and friends to seek prompt medical attention if unexplained illness develops and consider chemoprophylaxis for her recent close contacts. Should there be any queries, kindly do not hesitate to contact me.

Yours sincerely,

Doctor
""";

    [Fact]
    public void Garcia_OwnerCompliant_Correction_Passes_With_Zero_Blocking_Findings()
        => AssertClean(Model(GarciaCorrected, "LT-DG", ExamProfession.Medicine));

    // ── Live defect §15.3: Pharmacy medication-regimen letter, Mrs Alice Ramsey
    // (task 0d0e6b51-cc89-441d-8cac-baa44f756681, LT-OT; recipient = daughter).
    private const string RamseyLiveDefect = """
Mrs Holly Kerr
3 Rose Avenue
Springbank

8 August 2018

Dear Mrs Kerr,
Re: Medication regime for your mother, Mrs Alice Ramsey

Your mother, Mrs Alice Ramsey, is being discharged today from Newtown Hospital following surgery for a fractured femur, and will be staying with you during her recovery. I am writing to outline her medication regime and possible side effects.

Your mother will continue ranitidine 150mg twice daily for reflux and atorvastatin 20mg each morning on an empty stomach for high cholesterol, both taken for several years. Four new medications have also been added. Dalteparin 2500IU by injection under the skin will prevent blood clots until she is walking independently; she has been shown how to give this herself. Panadeine Forte 500mg, containing paracetamol and codeine, may be taken up to four times a day as needed for pain. Durolax 10mg at night will prevent constipation, and metoclopramide 10mg up to three times a day can be taken for nausea caused by the codeine.

Please watch for easy bruising or bleeding from the dalteparin, unusual muscle pain from the atorvastatin, and constipation, stomach upset or severe drowsiness from the pain relief, and contact her doctor promptly if any of these occur or if bleeding does not stop. She has an appointment on 22 August 2018 to have her cast removed.

Yours sincerely,

Pharmacist
""";

    [Fact]
    public void Ramsey_LiveDefect_ModelAnswer_Fails_Every_Documented_Rule()
    {
        var findings = Model(RamseyLiveDefect, "LT-OT", ExamProfession.Pharmacy);
        AssertFires(findings,
            "intro_opens_i_am_writing_to",           // opens "Your mother, ..."
            "body_uses_last_name_only",              // "Mrs Alice Ramsey" repeated in the letter
            "relationship_label_patient_reference",  // "Your mother will continue ..."
            "paragraph_start_patient_name",          // final paragraph "... contact her doctor"
            "medication_list_punctuation",           // "ranitidine 150mg"
            "value_unit_spacing",                    // "150mg", "2500IU", "500mg", "10mg"
            "linker_avoid_words",                    // "have also been added"
            "closure_contact_offer");
    }

    private const string RamseyCorrected = """
Mrs Holly Kerr
3 Rose Avenue
Springbank

8 August 2018

Dear Mrs Kerr,
Re: Mrs Alice Ramsey

I am writing to outline the medication regime and possible side effects for Mrs Ramsey, your mother, who is being discharged today following surgery for a fractured femur.

Mrs Ramsey will continue ranitidine, 150 mg twice daily for reflux, and atorvastatin, 20 mg each morning on an empty stomach for high cholesterol. Dalteparin, 2500 IU by injection under the skin, helps prevent blood clots until she walks independently. She has been shown how to self-inject. Panadeine Forte, 500 mg, containing paracetamol and codeine, may be taken up to four times a day as needed for pain. Durolax, 10 mg at night, helps prevent constipation. Metoclopramide, 10 mg up to three times a day, may be taken for nausea caused by the codeine.

Mrs Ramsey should be monitored for easy bruising or bleeding from the dalteparin, unusual muscle pain from the atorvastatin, and constipation, stomach upset or severe drowsiness from the pain relief. If any of these occur, or if bleeding does not stop, please contact her doctor promptly.

Mrs Ramsey has an appointment on 22 August 2018 to have her cast removed. Should there be any queries, kindly do not hesitate to contact me.

Yours sincerely,

Pharmacist
""";

    [Fact]
    public void Ramsey_OwnerCompliant_Correction_Passes_With_Zero_Blocking_Findings()
        => AssertClean(Model(RamseyCorrected, "LT-OT", ExamProfession.Pharmacy));

    // ── Live defect §15.4: Physiotherapy left-knee referral, Mrs Melanie Wright
    // (task b1ad86cd-26a4-409f-9553-8ed134b6eb24, LT-RR).
    private const string WrightLiveDefect = """
Dr David Delbridge
Underhill Medical Centre
71 Jewel Crescent
Underhill

02 September 2026

Dear Dr Delbridge,
Re: Mrs Melanie Wright D.O.B: 08 June 1973

I am writing to refer Mrs Wright regarding left knee pain. She has recently moved to the area with no GP and requests a recommendation.

Mrs Wright walks four times weekly and attends a circuit class twice weekly, having increased her gym exercise in the three weeks before injury. Two weeks ago, she turned suddenly while walking and felt something 'pop' in her left knee, with delayed swelling; she iced, rested and took paracetamol. She had no giving way, but intermittent locking, inability to fully straighten the knee, and disturbed sleep. Mornings are better, but afternoons bring more swelling, with no prior treatment or investigation. She underwent partial meniscectomy of the right knee in 1990 and the left knee in 2000, and takes glucosamine and felodipine for hypertension.

On examination, she was limping, unable to straighten the knee, with difficulty ascending stairs. Active range lacked full extension and flexion; passive extension increased pain, with hamstring tightness. Ligament testing was normal, with tenderness on the anteromedial joint line. My impression is further left medial meniscal damage.

I would be grateful if you could arrange investigation, including X-ray, MRI or arthroscopy. She was advised to avoid weight-bearing and swim for fitness.

Yours sincerely,

Physiotherapist
""";

    [Fact]
    public void Wright_LiveDefect_ModelAnswer_Fails_Every_Documented_Rule()
    {
        var findings = Model(WrightLiveDefect, "LT-RR", ExamProfession.Physiotherapy);
        AssertFires(findings,
            "dob_colon_format",                // "D.O.B:"
            "register_colloquial",             // "with no GP", "felt something 'pop'"
            "linker_avoid_words",              // "but"
            "paragraph_start_patient_name",    // "On examination, she ..."
            "closure_contains_management",     // "She was advised ..." after the request
            "closure_contact_offer");
    }

    private const string WrightCorrected = """
Dr David Delbridge
Underhill Medical Centre
71 Jewel Crescent
Underhill

2 September 2026

Dear Dr Delbridge,
Re: Mrs Melanie Wright, DOB: 8 June 1973

I am writing to refer Mrs Wright, who does not currently have a GP, for investigation of suspected further left medial meniscal damage.

Two weeks ago, Mrs Wright turned suddenly while walking and felt a popping sensation in her left knee, followed by delayed swelling. She iced and rested the knee and took paracetamol. She has had intermittent locking, inability to fully straighten the knee and disturbed sleep; however, there has been no giving way.

On examination, Mrs Wright was limping and unable to straighten the knee, with difficulty ascending stairs. Active range lacked full extension and flexion, and passive extension increased pain, with hamstring tightness. Ligament testing was normal, with tenderness on the anteromedial joint line. She was advised to avoid weight-bearing and to swim for fitness.

Mrs Wright walks four times weekly and attends a circuit class twice weekly, having increased her exercise before the injury. She had partial meniscectomies of the right knee in 1990 and the left knee in 2000. She takes glucosamine and felodipine for hypertension.

I would be grateful if you could arrange an X-ray, MRI or arthroscopy. Should there be any queries, kindly do not hesitate to contact me.

Yours sincerely,

Physiotherapist
""";

    [Fact]
    public void Wright_OwnerCompliant_Correction_Passes_With_Zero_Blocking_Findings()
        => AssertClean(Model(WrightCorrected, "LT-RR", ExamProfession.Physiotherapy));

    [Fact]
    public void Validator_Version_Is_Stamped_And_RulePack_Fingerprint_Is_Stable()
    {
        Assert.Equal("writing-rules.rev8.2026-09-11.1", WritingRuleEngine.ValidatorVersion);
        var a = _engine.RulePackFingerprint(ExamProfession.Medicine);
        var b = _engine.RulePackFingerprint(ExamProfession.Medicine);
        Assert.StartsWith("rp-", a);
        Assert.Equal(a, b);
        Assert.NotEqual(a, _engine.RulePackFingerprint(ExamProfession.Pharmacy));
    }
}
