using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Senior Assessor Release Audit (owner, 16 Sep 2026), group G5 regression
/// classes, proved on the canonical Medicine fixtures:
/// - A1: emotional/affective observations (register_colloquial, DECISIONS §C.12),
///   with the mental-health-letter exemption.
/// - A2: judgmental behaviour wording — compliance, defaulted, bizarre
///   behaviour, drinks heavily (judgmental_labels).
/// - B: note-style "query X" / "?X" (register_colloquial).
/// - C: "tiredness" -> "fatigue" (register_colloquial).
/// - E: weight-based doses and alphanumeric brands in medication lists
///   (medication_list_punctuation).
/// - F: contradictory medication frequency (medication_frequency_conflict).
/// - G: stranded formulation "Salbutamol, 5 mg, nebules" (medication_list_punctuation).
/// - H: "confirm the working diagnosis of possible X" / "A viral infection was
///   assessed" (register_colloquial).
/// Every new branch is Model-Answer-only: each defect is injected and must
/// fire for a Model Answer, stay silent for a candidate, and valid
/// alternatives must never fire.
/// </summary>
public sealed class WritingSeniorAuditG5RegressionTests
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

    private static void AssertRuleDoesNotFire(List<LintFinding> findings, string checkId)
        => Assert.DoesNotContain(findings, f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal));

    private static void AssertQuoteFires(List<LintFinding> findings, string checkId, string quoteFragment)
        => Assert.True(
            findings.Any(f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal)
                              && (f.Quote ?? string.Empty).Contains(quoteFragment, StringComparison.OrdinalIgnoreCase)),
            "Expected " + checkId + " with a quote containing \"" + quoteFragment + "\". Actual: "
            + string.Join(" | ", findings.Select(f => f.RuleId + " [" + f.Quote + "]")));

    private static void AssertQuoteDoesNotFire(List<LintFinding> findings, string checkId, string quoteFragment)
        => Assert.DoesNotContain(findings, f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal)
                                                && (f.Quote ?? string.Empty).Contains(quoteFragment, StringComparison.OrdinalIgnoreCase));

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

    private const string TaylorBreathSentence = "He reported shortness of breath.";
    private const string McDonaldCaravanSentence = "He lives alone in a caravan.";
    private const string McDonaldAnalgesiaSentence =
        "Analgesia comprises paracetamol, 1 g four times daily; ibuprofen, 400 mg three times daily; Targin, 20/10 twice daily and oxycodone, 5-10 mg four-hourly as needed.";
    private const string AloisiusCommaChain =
        "Antituberculous therapy comprises isoniazid, 5 mg/kg daily, rifampin, 10 mg/kg daily, pyrazinamide, 15-30 mg/kg daily, and ethambutol, 2.8 g twice weekly.";
    private const string AloisiusOwnerForm =
        "Antituberculous therapy comprises isoniazid, 5 mg/kg daily; rifampin, 10 mg/kg daily; pyrazinamide, 15-30 mg/kg daily and ethambutol, 2.8 g twice weekly.";
    private const string WeirClosureSentence = "I would be grateful if you could consider MRI if clinically indicated.";
    private const string WeirAssessedSentence = "He was assessed for hypercholesterolaemia, with repeat testing planned in three months.";

    // ─── A1 — emotional/affective observations ────

    [Theory]
    [InlineData("He is anxious and dyspnoeic, with bilateral ankle oedema.", "is anxious")]
    [InlineData("He was anxious, and the toe was hot and swollen.", "was anxious")]
    [InlineData("He was anxious and worried about the results.", "anxious and worried")]
    [InlineData("He is quite worried about his toe.", "quite worried")]
    [InlineData("He presented in distress, with severe pain.", "in distress")]
    [InlineData("He reported no chest pain and no obvious anxiety.", "no obvious anxiety")]
    [InlineData("He now reports feeling bored, discouraged and restless.", "bored")]
    [InlineData("He finds changing the dressing embarrassing.", "embarrassing")]
    [InlineData("He feels discouraged by the recurrence.", "discouraged")]
    public void SA_G5_A1_Emotional_Observation_Fires_In_Model_Answer_And_Not_For_Candidates(string sentence, string quote)
    {
        var letter = Inject(Taylor, TaylorBreathSentence, sentence);
        AssertQuoteFires(Lint(letter, "LT-UR"), "register_colloquial", quote);
        AssertRuleDoesNotFire(Lint(letter, "LT-UR", isModelAnswer: false), "register_colloquial");
    }

    [Theory]
    [InlineData("He is anxious about the flare.")]
    [InlineData("He remains in respiratory distress.")]
    [InlineData("He has a history of an anxiety disorder.")]
    [InlineData("His wife is concerned about his mobility.")]
    [InlineData("He became anxious on entering the scanner.")]
    [InlineData("Heavy lifting was discouraged.")]
    public void SA_G5_A1_Valid_Alternatives_Do_Not_Fire(string sentence)
        => AssertRuleDoesNotFire(Lint(Inject(Taylor, TaylorBreathSentence, sentence), "LT-UR"), "register_colloquial");

    [Fact]
    public void SA_G5_A1_Mental_State_Finding_Is_Exempt_Only_In_A_Mental_Health_Letter()
    {
        var neurology = Inject(Weir, "He continues to smoke.", "He was anxious, with poor sleep and headaches. He continues to smoke.");
        AssertQuoteFires(Lint(neurology, "LT-RR"), "register_colloquial", "was anxious");

        var psychiatric = Inject(neurology,
            "your neurological assessment and management of Mr Michael Weir",
            "your psychiatric assessment and management of Mr Michael Weir");
        AssertQuoteDoesNotFire(Lint(psychiatric, "LT-RR"), "register_colloquial", "anxious");
    }

    // ─── A2 — judgmental behaviour wording ────

    [Theory]
    [InlineData("He lives alone in a caravan and has a history of medication non-compliance.", "non-compliance")]
    [InlineData("He received supportive counselling on medication compliance.", "compliance")]
    [InlineData("He defaulted on his follow-up appointment.", "defaulted")]
    [InlineData("On examination, he showed bizarre behaviour.", "bizarre behaviour")]
    [InlineData("He drinks heavily and lives alone in a caravan.", "drinks heavily")]
    public void SA_G5_A2_Judgmental_Behaviour_Wording_Fires_In_Model_Answer_And_Not_For_Candidates(string sentence, string quote)
    {
        var letter = Inject(McDonald, McDonaldCaravanSentence, sentence);
        AssertQuoteFires(Lint(letter, "LT-TR"), "judgmental_labels", quote);
        AssertRuleDoesNotFire(Lint(letter, "LT-TR", isModelAnswer: false), "judgmental_labels");
    }

    [Theory]
    [InlineData("A Webster pack has been agreed to support adherence.")]
    [InlineData("He has not attended his follow-up appointments.")]
    [InlineData("He was inconsistent with his medication, often forgetting doses.")]
    [InlineData("Lung compliance was reduced.")]
    [InlineData("He rarely drinks alcohol.")]
    public void SA_G5_A2_Valid_Alternatives_Do_Not_Fire(string sentence)
        => AssertRuleDoesNotFire(Lint(Inject(McDonald, McDonaldCaravanSentence, sentence), "LT-TR"), "judgmental_labels");

    [Fact]
    public void SA_G5_A2_Existing_Non_Compliant_Label_Is_Not_Reported_Twice()
    {
        var letter = Inject(McDonald, McDonaldCaravanSentence, "He has been non-compliant with his medication.");
        var findings = Lint(letter, "LT-TR").Where(f => f.RuleId.EndsWith("judgmental_labels", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(findings);
        Assert.DoesNotContain(findings, f => string.Equals(f.Quote, "compliant", StringComparison.OrdinalIgnoreCase));
    }

    // ─── B — note-style "query X" / "?X" ────

    [Theory]
    [InlineData("The working assessment is query septic arthritis.", "query septic")]
    [InlineData("The assessment is acute gout with ?septic arthritis.", "?septic")]
    [InlineData("He has a query of renal calculi.", "query of renal")]
    public void SA_G5_B_Note_Style_Query_Fires_In_Model_Answer_And_Not_For_Candidates(string sentence, string quote)
    {
        var letter = Inject(Taylor, TaylorBreathSentence, sentence);
        AssertQuoteFires(Lint(letter, "LT-UR"), "register_colloquial", quote);
        AssertRuleDoesNotFire(Lint(letter, "LT-UR", isModelAnswer: false), "register_colloquial");
    }

    [Theory]
    [InlineData("Septic arthritis was suspected.")]
    [InlineData("The working diagnosis is gout with possible septic arthritis.")]
    [InlineData("He raised a query regarding his medication.")]
    [InlineData("Should he have any query about his colchicine, he will contact the clinic.")]
    public void SA_G5_B_Valid_Alternatives_Do_Not_Fire(string sentence)
        => AssertRuleDoesNotFire(Lint(Inject(Taylor, TaylorBreathSentence, sentence), "LT-UR"), "register_colloquial");

    // ─── C — "tiredness" -> "fatigue" ────

    [Fact]
    public void SA_G5_C_Tiredness_Fires_In_Model_Answer_And_Not_For_Candidates()
    {
        var letter = Inject(Weir, "with fatigue, stress and lethargy", "with tiredness, stress and lethargy");
        AssertQuoteFires(Lint(letter, "LT-RR"), "register_colloquial", "tiredness");
        AssertRuleDoesNotFire(Lint(letter, "LT-RR", isModelAnswer: false), "register_colloquial");
    }

    [Theory]
    [InlineData("with profound fatigue, stress and lethargy")]
    [InlineData("with lassitude, stress and lethargy")]
    [InlineData("with low energy, stress and lethargy")]
    public void SA_G5_C_Valid_Alternatives_Do_Not_Fire(string replacement)
        => AssertRuleDoesNotFire(Lint(Inject(Weir, "with fatigue, stress and lethargy", replacement), "LT-RR"), "register_colloquial");

    // ─── E — weight-based doses and alphanumeric brands in medication lists ────

    [Fact]
    public void SA_G5_E_Weight_Based_Comma_Chain_Fires_In_Model_Answer_And_Not_For_Candidates()
    {
        var letter = Inject(McDonald, McDonaldAnalgesiaSentence, AloisiusCommaChain);
        Assert.Contains(Lint(letter, "LT-TR"), f => f.RuleId.EndsWith("medication_list_punctuation", StringComparison.Ordinal)
                                                   && f.Message.StartsWith("Three or more medicines", StringComparison.Ordinal));
        AssertRuleDoesNotFire(Lint(letter, "LT-TR", isModelAnswer: false), "medication_list_punctuation");
    }

    [Fact]
    public void SA_G5_E_Alphanumeric_Brand_Without_Dose_Comma_Fires_In_Model_Answer_And_Not_For_Candidates()
    {
        var letter = Inject(McDonald, "Lipitor, 20 mg at night", "NovoMix30 25 units twice daily");
        Assert.Contains(Lint(letter, "LT-TR"), f => f.RuleId.EndsWith("medication_list_punctuation", StringComparison.Ordinal)
                                                   && f.FixSuggestion == "NovoMix30, 25 units");
        AssertRuleDoesNotFire(Lint(letter, "LT-TR", isModelAnswer: false), "medication_list_punctuation");
    }

    [Theory]
    [InlineData(McDonaldAnalgesiaSentence, AloisiusOwnerForm)]
    [InlineData(McDonaldCaravanSentence, "Blood tests showed haemoglobin 110 g/L.")]
    [InlineData(McDonaldCaravanSentence, "Urine output was 0.5 mL/kg/h.")]
    [InlineData("Lipitor, 20 mg at night", "NovoMix30, 25 units twice daily")]
    public void SA_G5_E_Valid_Alternatives_Do_Not_Fire(string find, string replace)
        => AssertRuleDoesNotFire(Lint(Inject(McDonald, find, replace), "LT-TR"), "medication_list_punctuation");

    // Needs the shared edit that lets SemicolonsAreMedicationListSeparators use
    // SaG5MedicationItemRe: the owner-form weight-based list is a medication
    // list, not a narrative semicolon.
    [Fact]
    public void SA_G5_E_Owner_Form_Weight_Based_List_Is_Exempt_From_Semicolon_Overuse()
        => AssertRuleDoesNotFire(Lint(Inject(McDonald, McDonaldAnalgesiaSentence, AloisiusOwnerForm), "LT-TR"), "semicolon_overuse");

    // ─── F — contradictory medication frequency ────

    [Theory]
    [InlineData("Karvina, 300 mg daily", "Karvina, 300 mg twice daily each morning", "twice daily each morning")]
    [InlineData("Lipitor, 20 mg at night", "Lipitor, 20 mg twice at night", "twice at night")]
    [InlineData("Lipitor, 20 mg at night", "Lipitor, 20 mg three times a day in the evening", "three times a day in the evening")]
    public void SA_G5_F_Frequency_Conflict_Fires_In_Model_Answer_And_Not_For_Candidates(string find, string replace, string quote)
    {
        var letter = Inject(McDonald, find, replace);
        AssertQuoteFires(Lint(letter, "LT-TR"), "medication_frequency_conflict", quote);
        AssertRuleDoesNotFire(Lint(letter, "LT-TR", isModelAnswer: false), "medication_frequency_conflict");
    }

    [Theory]
    [InlineData("Lipitor, 20 mg at night", "Lipitor, 10 mg twice daily in the morning and at night")]
    [InlineData("Lipitor, 20 mg at night", "Lipitor, 20 mg once daily at night")]
    [InlineData("Lipitor, 20 mg at night", "Lipitor, 20 mg twice daily")]
    [InlineData(McDonaldCaravanSentence, "He lives alone in a caravan and wakes twice at night.")]
    public void SA_G5_F_Valid_Alternatives_Do_Not_Fire(string find, string replace)
        => AssertRuleDoesNotFire(Lint(Inject(McDonald, find, replace), "LT-TR"), "medication_frequency_conflict");

    // ─── G — stranded formulation ────

    [Fact]
    public void SA_G5_G_Stranded_Formulation_Fires_In_Model_Answer_And_Not_For_Candidates()
    {
        var letter = Inject(Taylor, TaylorBreathSentence, "Salbutamol, 5 mg, nebules were given without improvement.");
        Assert.Contains(Lint(letter, "LT-UR"), f => f.RuleId.EndsWith("medication_list_punctuation", StringComparison.Ordinal)
                                                   && f.FixSuggestion == "Salbutamol nebules, 5 mg");
        AssertRuleDoesNotFire(Lint(letter, "LT-UR", isModelAnswer: false), "medication_list_punctuation");
    }

    [Theory]
    [InlineData("Salbutamol nebules, 5 mg, were given without improvement.")]
    [InlineData("Metoclopramide, 10 mg, was prescribed for nausea.")]
    [InlineData("Atenolol, 50 mg, half a tablet each morning, was commenced.")]
    public void SA_G5_G_Valid_Alternatives_Do_Not_Fire(string sentence)
        => AssertRuleDoesNotFire(Lint(Inject(Taylor, TaylorBreathSentence, sentence), "LT-UR"), "medication_list_punctuation");

    // ─── H — lexical misuse ────

    [Theory]
    [InlineData(WeirClosureSentence, "I would be grateful if you could confirm the working diagnosis of possible multiple sclerosis.", "confirm the working diagnosis of possible")]
    [InlineData(WeirAssessedSentence, "A viral infection was assessed, and repeat testing is planned in three months.", "was assessed")]
    public void SA_G5_H_Lexical_Misuse_Fires_In_Model_Answer_And_Not_For_Candidates(string find, string replace, string quote)
    {
        var letter = Inject(Weir, find, replace);
        AssertQuoteFires(Lint(letter, "LT-RR"), "register_colloquial", quote);
        AssertRuleDoesNotFire(Lint(letter, "LT-RR", isModelAnswer: false), "register_colloquial");
    }

    [Theory]
    [InlineData(WeirClosureSentence, "I would be grateful if you could confirm the diagnosis and advise on management.")]
    [InlineData(WeirClosureSentence, "I would be grateful if you could consider MRI to confirm the suspected diagnosis.")]
    [InlineData(WeirAssessedSentence, "He was assessed as having hypercholesterolaemia, with repeat testing planned in three months.")]
    [InlineData("He continues to smoke.", "A working diagnosis of possible multiple sclerosis has been made. He continues to smoke.")]
    public void SA_G5_H_Valid_Alternatives_Do_Not_Fire(string find, string replace)
        => AssertRuleDoesNotFire(Lint(Inject(Weir, find, replace), "LT-RR"), "register_colloquial");

    // ─── Clean canonical fixtures stay clean for every G5 check id ────

    [Fact]
    public void SA_G5_Clean_Fixture_Letters_Produce_No_G5_Findings()
    {
        var fixtures = new (string Letter, string LetterType, ExamProfession Profession)[]
        {
            (Garcia, "LT-DG", ExamProfession.Medicine),
            (Weston, "LT-NM", ExamProfession.Medicine),
            (Weir, "LT-RR", ExamProfession.Medicine),
            (McDonald, "LT-TR", ExamProfession.Medicine),
            (Taylor, "LT-UR", ExamProfession.Medicine),
            (WritingUltimateFinalRegressionFixtureTests.WeirRoutineReferralLetter, "LT-RR", ExamProfession.Medicine),
            (WritingUltimateFinalRegressionFixtureTests.GarciaDischargeLetter, "LT-DG", ExamProfession.Medicine),
            (WritingUltimateFinalRegressionFixtureTests.RamseyPharmacyLetter, "LT-OT", ExamProfession.Pharmacy),
            (WritingUltimateFinalRegressionFixtureTests.WrightKneeReferralLetter, "LT-RR", ExamProfession.Physiotherapy),
        };
        foreach (var (letter, letterType, profession) in fixtures)
        {
            var findings = Lint(letter, letterType, profession: profession);
            AssertRuleDoesNotFire(findings, "register_colloquial");
            AssertRuleDoesNotFire(findings, "judgmental_labels");
            AssertRuleDoesNotFire(findings, "medication_list_punctuation");
            AssertRuleDoesNotFire(findings, "medication_frequency_conflict");
        }
    }
}
