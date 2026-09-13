using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Permanent regression fixtures (owner directive, 13 Sep 2026): the three
/// finished Medicine Model Answers — McDonald (transfer), Weir (routine
/// referral) and Taylor (urgent referral) — must lint with ZERO findings,
/// and every defect the owner listed must be DETECTED when deliberately
/// injected. The medication parser must recognise legitimate dose formats
/// (ranges, combination strengths, frequency-only doses) so correct
/// clinical wording is never distorted to satisfy — or evade — the regex:
/// "if correct professional wording exposes a weakness in the validator,
/// FIX THE VALIDATOR."
/// </summary>
public sealed class WritingRev8RegressionFixtureTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());

    private static List<LintFinding> Lint(string letter, string letterType)
        => Engine.Lint(new WritingLintInput(
            LetterText: letter,
            LetterType: letterType,
            Profession: ExamProfession.Medicine,
            IsModelAnswer: true)).ToList();

    private static void AssertRuleFires(List<LintFinding> findings, string checkId)
        => Assert.Contains(findings, f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal));

    // ─── A. Mr Julian McDonald — Transfer (LT-TR) ────

    internal const string McDonaldTransferLetter = """
The Admissions Officer
Cabrini Hopetoun Rehabilitation
2-6 Hopetoun Street
Elsternwick, Vic 3185

24 July 2018

Dear Sir/Madam,
Re: Mr Julian McDonald, DOB: 12 January 1950

I am writing to transfer Mr McDonald for immediate rehabilitation.

Mr McDonald underwent elective left total knee replacement on 20 July 2018 under Mr Mossley. His history includes hypertension, post-traumatic stress disorder, childhood penicillin allergy, smoking 20 cigarettes daily and drinking over six to ten standard drinks daily. He lives alone in a caravan.

Post-operative pain despite morphine patient-controlled analgesia has slowed Mr McDonald's mobilisation. A 48-hour ketamine infusion was effective; amitriptyline was discontinued for difficulty urinating. Significant somnolence and snoring prompted sleep studies for possible obstructive sleep apnoea. Catheter urine culture grew Staphylococcus saprophyticus, treated with five days of Keflex.

Discharge medications are Zyloric, 300 mg daily; Lipitor, 20 mg at night; Karvina, 300 mg daily; and Nicabate patch, 21 mg. Analgesia comprises paracetamol, 1 g four times daily; ibuprofen, 400 mg three times daily; Targin, 20/10 twice daily; and oxycodone, 5-10 mg four-hourly as needed.

Physiotherapy, occupational therapy home visit to assess suitability to return home, social work and drug and alcohol input are planned. I would be grateful if you could admit Mr McDonald before his specialist appointment on 7 September 2018. Should there be any queries, please do not hesitate to contact me.

Yours faithfully,

Doctor
""";

    [Fact]
    public void McDonald_Transfer_Fixture_Lints_Clean()
    {
        var findings = Lint(McDonaldTransferLetter, "LT-TR");
        Assert.Empty(findings);
    }

    // The parser must natively recognise a dose range ("5-10 mg") and a
    // combination strength ("20/10" with no unit but a frequency), so the
    // natural 4-item analgesia list passes list-punctuation AS WRITTEN —
    // the pre-fix parser saw only 2 items and forced unnatural rewrites.
    [Fact]
    public void Medication_Parser_Recognises_Range_And_Combination_Doses_As_List_Items()
    {
        var sentence = "Analgesia comprises paracetamol, 1 g four times daily; ibuprofen, 400 mg three times daily; Targin, 20/10 twice daily; and oxycodone, 5-10 mg four-hourly as needed.";
        var letter = McDonaldTransferLetter.Replace(
            "Analgesia comprises paracetamol, 1 g four times daily; ibuprofen, 400 mg three times daily; Targin, 20/10 twice daily; and oxycodone, 5-10 mg four-hourly as needed.",
            sentence);
        var findings = Lint(letter, "LT-TR");
        Assert.DoesNotContain(findings, f => f.RuleId.EndsWith("medication_list_punctuation", StringComparison.Ordinal));
    }

    [Fact]
    public void Injected_Note_Form_Partitive_Is_Flagged()
    {
        var letter = McDonaldTransferLetter.Replace(
            "amitriptyline was discontinued for difficulty urinating",
            "amitriptyline ceased for difficulty urinating");
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
            "Analgesia comprises paracetamol, 1 g four times daily; ibuprofen, 400 mg three times daily; Targin, 20/10 twice daily; and oxycodone, 5-10 mg four-hourly as needed.",
            "Analgesia comprises paracetamol, 1 g four times daily, ibuprofen, 400 mg three times daily, Targin, 20/10 twice daily and oxycodone, 5-10 mg four-hourly as needed.");
        AssertRuleFires(Lint(letter, "LT-TR"), "medication_list_punctuation");
    }

    [Fact]
    public void Injected_Stripped_Cigarette_Frequency_Is_Flagged()
    {
        var letter = McDonaldTransferLetter.Replace(
            "smoking 20 cigarettes daily",
            "smoking 20 cigarettes");
        AssertRuleFires(Lint(letter, "LT-TR"), "lifestyle_frequency_precision");
    }

    // ─── B. Mr Michael Weir — Routine Referral (LT-RR) ────

    internal const string WeirRoutineReferralLetter = """
Dr M McLaren
Neurologist
Suite 3
67 The Crescent
Newtown

9 August 2014

Dear Dr McLaren,
Re: Mr Michael Weir

I am writing to refer Mr Weir for a full neurological assessment, given a working assessment of possible multiple sclerosis.

On today's review, Mr Weir reported dizziness, two recent blackouts, tingling in both hands, persistent left leg weakness, breathlessness, occasional constipation and low energy. Examination revealed sensory loss to sharp and blunt testing in both hands and a diminished left patellar reflex. A CT scan of the head and lumbar spine has therefore been ordered to investigate possible central or spinal causes of the weakness and hyporeflexia.

Mr Weir presented in June 2014 with fatigue, stress and lethargy, returning one week later with left leg weakness. Investigations showed a cholesterol of 6.37 mmol/L and a full blood count with low white and red cell counts, haemoglobin and haematocrit. He was assessed for hypercholesterolaemia, with repeat testing planned in three months.

Mr Weir has depression, treated with sertraline hydrochloride since September 2012. He continues to smoke and has been overweight long term.

I would be grateful if you could assess Mr Weir and advise on further management, including possible magnetic resonance imaging. Should there be any queries, kindly do not hesitate to contact me.

Yours sincerely,

Doctor
""";

    [Fact]
    public void Weir_Routine_Referral_Fixture_Lints_Clean()
    {
        var findings = Lint(WeirRoutineReferralLetter, "LT-RR");
        Assert.Empty(findings);
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
            "has been overweight long term",
            "has been overweight for a long time");
        AssertRuleFires(Lint(letter, "LT-RR"), "register_colloquial");
    }

    // ─── C. Mr David Taylor — Urgent Referral (LT-UR) ────

    internal const string TaylorUrgentReferralLetter = """
Dr Malcom Still
Rheumatologist
City Hospital
Suite 32
55 Main Road
Newtown

13 June 2020

Dear Dr Still,
Re: Mr David Taylor, aged 55

I am writing to urgently refer Mr Taylor for rheumatological assessment of a gout flare with an associated tophus, and possible tophus removal.

Today, Mr Taylor presented with pain in his right big toe, swelling of the toe and foot, right flank pain and red-coloured urine. Observations recorded a temperature of 37.8 °C, blood pressure of 120/80 mmHg, heart rate of 90 bpm and respiratory rate of 22 /min. He had shortness of breath. Examination revealed an inflamed, red right first toe with an underlying tophus, treated with colchicine, also known as Lengout, 1 mg, and NSAIDs.

Mr Taylor has had gout since 2000, managed with allopurinol, paracetamol and colchicine, also known as Lengout. He experienced a severe attack and a further attack in 2010; kidney stones were noted that year. He remained free of attacks from 2011 to 2020. He was diagnosed with depression in 2011, possibly gout-related, treated with fluoxetine since. His brother has gout, and his father died of kidney failure.

I would be grateful if you could assess Mr Taylor's tophus and advise on management, including possible removal, at your earliest convenience. Should there be any queries, do not hesitate to contact me.

Yours sincerely,

Doctor
""";

    [Fact]
    public void Taylor_Urgent_Referral_Fixture_Lints_Clean()
    {
        var findings = Lint(TaylorUrgentReferralLetter, "LT-UR");
        Assert.Empty(findings);
    }

    [Fact]
    public void Injected_Judgmental_Observation_Is_Flagged()
    {
        var letter = TaylorUrgentReferralLetter.Replace(
            "He had shortness of breath.",
            "He appeared anxious with shortness of breath.");
        AssertRuleFires(Lint(letter, "LT-UR"), "register_colloquial");
    }

    [Fact]
    public void Injected_Compressed_Temperature_Unit_Is_Flagged()
    {
        var letter = TaylorUrgentReferralLetter.Replace(
            "37.8 °C",
            "37.8°C");
        AssertRuleFires(Lint(letter, "LT-UR"), "value_unit_spacing");
    }

    [Fact]
    public void Injected_Unitless_Heart_And_Respiratory_Rates_Are_Flagged()
    {
        var letter = TaylorUrgentReferralLetter.Replace(
            "heart rate of 90 bpm and respiratory rate of 22 /min",
            "heart rate of 90 and respiratory rate of 22");
        AssertRuleFires(Lint(letter, "LT-UR"), "numerical_values_have_units");
    }
}
