using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Senior Assessor Release Audit (owner, 16 Sep 2026) — group G1 grammar
/// regression battery. Every class proves four things on the canonical
/// Medicine fixtures: (a) the REAL audited defective shape fires in Model
/// Answer mode; (b) valid alternatives never fire; (c) the same defect in
/// candidate mode does not fire (DECISIONS §B — Model Answer only); (d) the
/// clean fixture letters stay clean.
/// - G1a sentence_fragment: verbless / subjectless sentences.
/// - G1b incomplete_clinical_construction: gapped passive auxiliary.
/// - G1c incomplete_clinical_construction: adjective in a "with" noun list.
/// - G1d malformed_word_form: "ex-smokes", "has been continued smoking",
///   "social drinks alcohol" (doubled words are typographic_corruption, G2).
/// </summary>
public sealed class WritingSeniorAuditG1RegressionTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());

    private const string SentenceFragment = "sentence_fragment";
    private const string IncompleteConstruction = "incomplete_clinical_construction";
    private const string MalformedWordForm = "malformed_word_form";

    private const string Garcia = WritingRev8RegressionFixtureTests.GarciaUpdateLetter;          // LT-DG
    private const string Taylor = WritingRev8RegressionFixtureTests.TaylorUrgentReferralLetter;  // LT-UR
    private const string McDonald = WritingRev8RegressionFixtureTests.McDonaldTransferLetter;    // LT-TR

    // Stable body sentences used as injection anchors.
    private const string TaylorAnchor = "He reported shortness of breath.";
    private const string McDonaldAnchor = "He lives alone in a caravan.";

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
        Assert.Contains(find, letter, StringComparison.Ordinal);
        var injected = letter.Replace(find, replace);
        Assert.NotEqual(letter, injected);
        return injected;
    }

    private static string AppendToTaylor(string sentence)
        => Inject(Taylor, TaylorAnchor, TaylorAnchor + " " + sentence);

    private static string AppendToMcDonald(string sentence)
        => Inject(McDonald, McDonaldAnchor, McDonaldAnchor + " " + sentence);

    // ─── G1a — sentence_fragment ────

    [Theory]
    [InlineData("With poorly controlled diabetes, mild-to-moderate hypertension, and ischaemic heart disease following coronary angioplasty in 2011 after a myocardial infarction.")]
    [InlineData("However, no effusion.")]
    [InlineData("However, a HbA1c level at 10%, indicating very poor control, with elevated cholesterol of 6.2 and triglycerides of 2.4.")]
    [InlineData("Lives with her long-term boyfriend, for assessment and management of reactive depression and anxiety.")]
    [InlineData("However, was initially resistant to a psychiatric referral and has now agreed to be seen.")]
    [InlineData("Loss of appetite, together with low libido, poor memory and concentration, loss of pleasure, loss of confidence and a low tolerance for alcohol.")]
    [InlineData("And prescribed eflornithine cream for the facial hair alongside Ms Geller existing over-the-counter hair removal cream.")]
    [InlineData("At your earliest convenience.")]
    [InlineData("Pulse 96, blood pressure 110/70.")]
    [InlineData("Oxycodone, 5-10 mg as required, with home nursing support for personal care.")]
    [InlineData("Exercise tolerance review for referral to Toowong Cardiac Rehabilitation.")]
    [InlineData("Temporal headache extending to the vertex, with dizziness, nausea, anxiety and visual disturbance, eased by lying down.")]
    [InlineData("Disclosed a previously undisclosed whiplash injury from a car accident in June 2017, for which she had not sought treatment.")]
    [InlineData("However, has returned to her usual mild level, and she is now settled.")]
    [InlineData("With insomnia, fatigue, joint pain and headaches, and was advised on rest and moderate exercise.")]
    [InlineData("Attended the pharmacy today requesting an over-the-counter sleeping tablet, which I declined to dispense pending your review.")]
    [InlineData("And Atacand, 4 mg daily for hypertension.")]
    public void SA_G1_SentenceFragment_Audited_Fragment_Fires_In_Model_Answer(string fragment)
        => AssertRuleFires(Lint(AppendToTaylor(fragment), "LT-UR"), SentenceFragment);

    [Theory]
    [InlineData("With poorly controlled diabetes, mild-to-moderate hypertension, and ischaemic heart disease following coronary angioplasty in 2011 after a myocardial infarction.")]
    [InlineData("However, no effusion.")]
    [InlineData("Lives with her long-term boyfriend, for assessment and management of reactive depression and anxiety.")]
    [InlineData("However, was initially resistant to a psychiatric referral and has now agreed to be seen.")]
    [InlineData("And prescribed eflornithine cream for the facial hair alongside Ms Geller existing over-the-counter hair removal cream.")]
    [InlineData("At your earliest convenience.")]
    [InlineData("Exercise tolerance review for referral to Toowong Cardiac Rehabilitation.")]
    public void SA_G1_SentenceFragment_Candidate_Mode_Does_Not_Fire(string fragment)
        => AssertRuleDoesNotFire(Lint(AppendToTaylor(fragment), "LT-UR", isModelAnswer: false), SentenceFragment);

    // Cathy Jones: the urgent phrase split off the request sentence satisfied
    // urgent_closure_phrase while leaving a verbless fragment behind.
    [Fact]
    public void SA_G1_SentenceFragment_Split_Urgent_Closure_Phrase_Fires()
    {
        var letter = Inject(Taylor,
            "if clinically indicated, at your earliest convenience.",
            "if clinically indicated. At your earliest convenience.");
        var findings = Lint(letter, "LT-UR");
        Assert.Contains(findings, f => f.RuleId.EndsWith(SentenceFragment, StringComparison.Ordinal)
                                       && f.Quote == "At your earliest convenience.");
    }

    [Theory]
    [InlineData("However, chest X-ray and CT demonstrated right middle lobe atelectasis and an enlarged right hilum.")]
    [InlineData("Both respond well to Panadol and Prozac.")]
    [InlineData("Her fasting sugars remain often above 16, with other readings around 7 to 8.")]
    [InlineData("Recent investigations show a raised serum urate level at 0.48 mmol/L and a raised CRP level at 6.0 mg/L.")]
    [InlineData("Pyrazinamide and ethambutol then cease, with isoniazid and rifampin continuing for a further four months.")]
    [InlineData("Exercise tolerance remains good.")]
    [InlineData("A home-care worker visits every two days to assist with bathing.")]
    [InlineData("Please find enclosed copies of the blood test results from 2014, 2017 and 2019.")]
    [InlineData("Her second pregnancy, with normal blood pressure throughout, ended in a full-term delivery.")]
    [InlineData("With hyperbaric oxygen therapy and negative pressure dressings, the graft is now 70% healed, and her ankle brachial index has improved to 70.")]
    [InlineData("Owing to a fear of needles, no bloods have yet been taken.")]
    [InlineData("However, an echocardiogram confirmed pericarditis.")]
    [InlineData("However, there has been no giving way.")]
    [InlineData("His mother died of an acute myocardial infarction at 52.")]
    [InlineData("This followed a right lower leg injury from a fall at the beach on 2 April.")]
    [InlineData("Smoking, high blood pressure, obesity, diabetes and high cholesterol increase the risk of angina.")]
    [InlineData("His long-term steroid use and prior fragility fracture place him at high fracture risk.")]
    [InlineData("His family shares most meals.")]
    [InlineData("Avoid repetitive gripping for longer than twenty minutes without a break.")]
    [InlineData("If conscious, give oral glucose.")]
    [InlineData("Stop smoking and limit alcohol to two standard drinks daily.")]
    [InlineData("Headaches, relieved by rest, occur daily.")]
    public void SA_G1_SentenceFragment_Valid_Sentence_Does_Not_Fire(string sentence)
        => AssertRuleDoesNotFire(Lint(AppendToTaylor(sentence), "LT-UR"), SentenceFragment);

    // ─── G1b — incomplete_clinical_construction: gapped passive auxiliary ────

    [Fact]
    public void SA_G1_ElidedPassiveAuxiliary_Garcia_Family_Immunisation_Fires_In_Model_Answer()
    {
        var letter = Inject(Garcia, "and family immunisation was discussed.", "and family immunisation discussed.");
        var findings = Lint(letter, "LT-DG");
        Assert.Contains(findings, f => f.RuleId.EndsWith(IncompleteConstruction, StringComparison.Ordinal)
                                       && f.Quote == "family immunisation discussed");
    }

    [Theory]
    [InlineData("Metformin was increased to 750 mg twice daily and atorvastatin, 20 mg daily added.", "atorvastatin, 20 mg daily added")]
    [InlineData("Folic acid and tinzaparine, 3,500 units daily were commenced, routine antenatal bloods arranged, and Mrs MacIntyre has elected to have a nuchal translucency scan.", "routine antenatal bloods arranged")]
    [InlineData("Benzoyl peroxide was stopped and topical clindamycin, 1%, commenced twice daily.", "topical clindamycin, 1%, commenced")]
    [InlineData("A protective dressing was applied, debridement withheld, and Mrs Chen was advised against unnecessary walking.", "debridement withheld")]
    public void SA_G1_ElidedPassiveAuxiliary_Audited_Shape_Fires_In_Model_Answer(string sentence, string quote)
    {
        var findings = Lint(AppendToTaylor(sentence), "LT-UR");
        Assert.Contains(findings, f => f.RuleId.EndsWith(IncompleteConstruction, StringComparison.Ordinal) && f.Quote == quote);
    }

    [Fact]
    public void SA_G1_ElidedPassiveAuxiliary_Candidate_Mode_Does_Not_Fire()
    {
        var letter = Inject(Garcia, "and family immunisation was discussed.", "and family immunisation discussed.");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG", isModelAnswer: false), IncompleteConstruction);
    }

    [Fact]
    public void SA_G1_ElidedPassiveAuxiliary_Garcia_Fixture_With_Auxiliary_Does_Not_Fire()
        => AssertRuleDoesNotFire(Lint(Garcia, "LT-DG"), IncompleteConstruction);

    [Theory]
    [InlineData("Metformin was increased to 750 mg twice daily, and atorvastatin, 20 mg daily, was added.")]
    [InlineData("His metformin regimen was changed to twice daily and atorvastatin, 10 mg each morning was added.")]
    [InlineData("His history includes an inguinal hernia in 2008 and a skin cancer removed in 2016.")]
    [InlineData("He was counselled regarding treatment options, and a core biopsy performed today confirmed a moderately differentiated carcinoma.")]
    [InlineData("He was discharged on 2 November 2022, with twice-weekly carer visits arranged to support him and his wife at home.")]
    [InlineData("Tramadol replaced Panadeine Forte, with pelvic floor and transversus abdominis exercises added.")]
    [InlineData("He commenced smoking in 2013, and the school doctor commenced him on iron infusions.")]
    [InlineData("Nicotine patches, 25 mg, were added, and citalopram was increased to 20 mg.")]
    [InlineData("Metformin was stopped and replaced with insulin, and haemodialysis was commenced, three times weekly for four hours, with no improvement in renal function.")]
    [InlineData("He was reviewed by the physiotherapist, and Dr Smith advised on home exercises.")]
    public void SA_G1_ElidedPassiveAuxiliary_Valid_Alternative_Does_Not_Fire(string sentence)
        => AssertRuleDoesNotFire(Lint(AppendToTaylor(sentence), "LT-UR"), IncompleteConstruction);

    // ─── G1c — incomplete_clinical_construction: faulty "with" list parallelism ────

    [Fact]
    public void SA_G1_FaultyParallelism_Garcia_Unable_In_With_List_Fires_In_Model_Answer()
    {
        var letter = Inject(Garcia, "bruising on the left arm and inability to touch", "bruising on the left arm and unable to touch");
        var findings = Lint(letter, "LT-DG");
        Assert.Contains(findings, f => f.RuleId.EndsWith(IncompleteConstruction, StringComparison.Ordinal)
                                       && f.Message.Contains("\"unable\"", StringComparison.Ordinal));
    }

    [Fact]
    public void SA_G1_FaultyParallelism_Candidate_Mode_Does_Not_Fire()
    {
        var letter = Inject(Garcia, "bruising on the left arm and inability to touch", "bruising on the left arm and unable to touch");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG", isModelAnswer: false), IncompleteConstruction);
    }

    [Theory]
    [InlineData("He was afebrile, with a petechial rash on the abdomen and legs and bruising on the left arm, and was unable to touch his chin to his chest when supine.")]
    [InlineData("He is jaundiced and dehydrated, with a temperature of 39 °C.")]
    [InlineData("Examination shows marked tenderness, with a positive pregnancy test and clear urine dipstick.")]
    [InlineData("He is unable to move his right eye, with painful movement of both eyes, worse on the right, and double vision.")]
    [InlineData("He was comfortable with minimal pain and able to mobilise independently.")]
    [InlineData("He remained stable, with good oxygen saturation, and alert throughout.")]
    public void SA_G1_FaultyParallelism_Valid_Alternative_Does_Not_Fire(string sentence)
        => AssertRuleDoesNotFire(Lint(AppendToTaylor(sentence), "LT-UR"), IncompleteConstruction);

    // ─── G1d — malformed_word_form ────

    [Theory]
    [InlineData("Mr McDonald is an ex-smokes of 35 years, having quit seven years ago.", "an ex-smokes")]
    [InlineData("He has been continued smoking 10-twenty cigarettes daily for four years despite counselling on smoking cessation.", "has been continued smoking")]
    [InlineData("He does not smoke and social drinks alcohol.", "social drinks alcohol")]
    [InlineData("He is a former smokes of 35 pack-years.", "a former smokes")]
    [InlineData("He does not smoke, non-drinks alcohol, and has no known allergies.", "non-drinks")]
    public void SA_G1_MalformedWordForm_Audited_Shape_Fires_In_Model_Answer(string sentence, string quote)
    {
        var findings = Lint(AppendToMcDonald(sentence), "LT-TR");
        Assert.Contains(findings, f => f.RuleId.EndsWith(MalformedWordForm, StringComparison.Ordinal) && f.Quote == quote);
    }

    [Theory]
    [InlineData("Mr McDonald is an ex-smokes of 35 years, having quit seven years ago.")]
    [InlineData("He has been continued smoking 10-twenty cigarettes daily for four years despite counselling on smoking cessation.")]
    [InlineData("He does not smoke and social drinks alcohol.")]
    public void SA_G1_MalformedWordForm_Candidate_Mode_Does_Not_Fire(string sentence)
        => AssertRuleDoesNotFire(Lint(AppendToMcDonald(sentence), "LT-TR", isModelAnswer: false), MalformedWordForm);

    [Theory]
    [InlineData("He smoked for thirty-five years and quit seven years ago.")]
    [InlineData("He has smoked ten to twenty cigarettes daily for four years despite counselling on smoking cessation.")]
    [InlineData("Physiotherapy has been stopped pending medical clearance.")]
    [InlineData("He has never smoked and rarely drinks alcohol.")]
    [InlineData("By 2010, he had had three attacks of gout.")]
    [InlineData("He was previously treated at Wagga Wagga Base Hospital.")]
    [InlineData("He drinks two social drinks weekly.")]
    public void SA_G1_MalformedWordForm_Valid_Alternative_Does_Not_Fire(string sentence)
        => AssertRuleDoesNotFire(Lint(AppendToMcDonald(sentence), "LT-TR"), MalformedWordForm);

    // ─── Clean fixtures stay clean for every G1 check id ────

    [Fact]
    public void SA_G1_Clean_Fixture_Letters_Produce_No_G1_Findings()
    {
        var fixtures = new (string Name, string Letter, string LetterType, ExamProfession Profession)[]
        {
            ("Rev8.Garcia", WritingRev8RegressionFixtureTests.GarciaUpdateLetter, "LT-DG", ExamProfession.Medicine),
            ("Rev8.Weston", WritingRev8RegressionFixtureTests.WestonReferralLetter, "LT-NM", ExamProfession.Medicine),
            ("Rev8.Weir", WritingRev8RegressionFixtureTests.WeirRoutineReferralLetter, "LT-RR", ExamProfession.Medicine),
            ("Rev8.McDonald", WritingRev8RegressionFixtureTests.McDonaldTransferLetter, "LT-TR", ExamProfession.Medicine),
            ("Rev8.Taylor", WritingRev8RegressionFixtureTests.TaylorUrgentReferralLetter, "LT-UR", ExamProfession.Medicine),
            ("UltimateFinal.Weir", WritingUltimateFinalRegressionFixtureTests.WeirRoutineReferralLetter, "LT-RR", ExamProfession.Medicine),
            ("UltimateFinal.Garcia", WritingUltimateFinalRegressionFixtureTests.GarciaDischargeLetter, "LT-DG", ExamProfession.Medicine),
            ("UltimateFinal.Ramsey", WritingUltimateFinalRegressionFixtureTests.RamseyPharmacyLetter, "LT-OT", ExamProfession.Pharmacy),
            ("UltimateFinal.Wright", WritingUltimateFinalRegressionFixtureTests.WrightKneeReferralLetter, "LT-RR", ExamProfession.Physiotherapy),
            ("Batch.Exemplar", WritingModelAnswerBatchTests.ExemplarText(), "LT-RR", ExamProfession.Medicine),
        };
        var checkIds = new[] { SentenceFragment, IncompleteConstruction, MalformedWordForm };
        foreach (var fixture in fixtures)
        {
            var offenders = Lint(fixture.Letter, fixture.LetterType, profession: fixture.Profession)
                .Where(f => checkIds.Any(id => f.RuleId.EndsWith(id, StringComparison.Ordinal)))
                .ToList();
            Assert.True(offenders.Count == 0,
                fixture.Name + " produced G1 findings: " + string.Join(" | ", offenders.Select(o => o.RuleId + ": " + o.Quote)));
        }
    }
}
