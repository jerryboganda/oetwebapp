using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Cross-model audit remediation (owner FINAL decision, 17 Sep 2026).
/// - OA6-01 HARD GLOBAL RULE, no_duplicated_request (request-concept branch):
///   the same functional request never appears in both the introduction and
///   the closure, judged by concept, not wording (Garcia and Meadows hard
///   fails, Sorocco, OET test 10 / Ms Martin).
/// - OA6-02 request_action_unsupported (new id): a closure request to monitor,
///   check, repeat or test a clinical parameter needs a case-note line that
///   plans it (Meadows "monitor ... symptoms and electrolytes").
/// - medication_list_punctuation: "Glipizide, two 5 mg tablets each morning"
///   (the rules doc OA5 right form) is one item; "two" is never a drug.
/// Every class proves the audited defect fires for a Model Answer, the owner-
/// approved correction and valid alternatives stay silent, and the candidate
/// lane stays silent.
/// </summary>
public sealed class WritingCrossModelAuditRegressionTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());

    private const string DuplicateRequest = "no_duplicated_request";
    private const string Unsupported = "request_action_unsupported";
    private const string MedicationList = "medication_list_punctuation";

    private const string Garcia = WritingRev8RegressionFixtureTests.GarciaUpdateLetter;
    private const string Weir = WritingRev8RegressionFixtureTests.WeirRoutineReferralLetter;
    private const string McDonald = WritingRev8RegressionFixtureTests.McDonaldTransferLetter;
    private const string Taylor = WritingRev8RegressionFixtureTests.TaylorUrgentReferralLetter;
    private const string Weston = WritingRev8RegressionFixtureTests.WestonReferralLetter;

    private const string GarciaIntro =
        "I am writing to update you regarding Ms Isabel Garcia's treatment for bacterial meningitis and request follow-up of close contacts.";
    private const string GarciaClosure =
        "I would be grateful if you could advise Ms Garcia's close contacts to seek prompt care if unwell and consider chemoprophylaxis.";
    private const string WeirIntro =
        "I am writing to request your neurological assessment and management of Mr Michael Weir, who has presented with features suggestive of multiple sclerosis.";
    private const string WeirClosure = "I would be grateful if you could consider MRI if clinically indicated.";

    private static List<LintFinding> Lint(string letter, string letterType, string? caseNotes = null, bool isModelAnswer = true)
        => Engine.Lint(new WritingLintInput(
            LetterText: letter,
            LetterType: letterType,
            CaseNotesText: caseNotes,
            Profession: ExamProfession.Medicine,
            IsModelAnswer: isModelAnswer)).ToList();

    private static List<LintFinding> FindingsFor(List<LintFinding> findings, string checkId)
        => findings.Where(f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal)).ToList();

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

    private static string GarciaWith(string intro, string closure)
        => Inject(Inject(Garcia, GarciaIntro, intro), GarciaClosure, closure);

    // ─────────────────────────────────────────────────────────────────
    // OA6-01 — the same functional request in introduction and closure
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void CMA_Duplicate_Garcia_Contact_Follow_Up_Reworded_Fires()
    {
        // Live Garcia (both auditors, HARD FAIL): the intro requests follow-up of
        // close contacts; the closure asks to contact close family and friends.
        var letter = Inject(Garcia, GarciaClosure,
            "I would be grateful if you could contact close family and friends, advise prompt medical attention and observation, and consider chemoprophylaxis.");
        var finding = Assert.Single(FindingsFor(Lint(letter, "LT-DG"), DuplicateRequest));
        Assert.Contains("contact close family and friends", finding.Quote, StringComparison.Ordinal);
        Assert.Equal(RuleSeverity.Critical, finding.Severity);
    }

    [Theory]
    [InlineData("I would be grateful if you could trace Ms Garcia's household contacts and consider chemoprophylaxis.")]
    [InlineData("I would be grateful if you could screen Ms Garcia's close family for signs of unexplained illness.")]
    [InlineData("I would be grateful if you could follow up Ms Garcia's close contacts and consider chemoprophylaxis.")]
    public void CMA_Duplicate_Contact_Follow_Up_Semantic_Variants_Fire(string closure)
        => AssertRuleFires(Lint(Inject(Garcia, GarciaClosure, closure), "LT-DG"), DuplicateRequest);

    [Theory]
    [InlineData(GarciaClosure)]
    [InlineData("I would be grateful if you could advise Ms Garcia's close contacts on prompt medical attention and observation, and consider chemoprophylaxis.")]
    public void CMA_Duplicate_Advising_Contacts_Is_A_Distinct_Step(string closure)
        => AssertRuleDoesNotFire(Lint(GarciaClosure == closure ? Garcia : Inject(Garcia, GarciaClosure, closure), "LT-DG"), DuplicateRequest);

    [Fact]
    public void CMA_Duplicate_Meadows_Interim_Care_Until_The_Same_Review_Fires()
    {
        // Live Meadows (HARD FAIL): "ongoing care pending outpatient
        // endocrinological review" / "monitor ... until her endocrinologist's
        // appointment".
        var letter = GarciaWith(
            "I am writing to update you regarding Ms Isabel Garcia, discharged today, and request your ongoing care pending outpatient endocrinological review.",
            "I would be grateful if you could monitor Ms Garcia's symptoms and electrolytes until her endocrinologist's appointment.");
        AssertRuleFires(Lint(letter, "LT-DG"), DuplicateRequest);
    }

    [Fact]
    public void CMA_Duplicate_Interim_Care_Semantic_Variant_Fires()
    {
        var letter = GarciaWith(
            "I am writing to update you regarding Ms Isabel Garcia, discharged today, and request your continued management until her cardiology clinic.",
            "I would be grateful if you could look after Ms Garcia before her cardiology follow-up.");
        AssertRuleFires(Lint(letter, "LT-DG"), DuplicateRequest);
    }

    [Theory]
    // Owner-approved Meadows correction.
    [InlineData("I would be grateful if you could encourage Ms Garcia to attend her endocrinologist's appointment.")]
    // Pristiely shape: a specific monitoring step is distinct from general ongoing care.
    [InlineData("I would be grateful if you could monitor Ms Garcia's blood pressure and electrolytes for one week.")]
    public void CMA_Duplicate_Distinct_Step_After_Ongoing_Care_Does_Not_Fire(string closure)
    {
        var letter = GarciaWith(
            "I am writing to update you regarding Ms Isabel Garcia, discharged today, and request your ongoing care pending outpatient endocrinological review.",
            closure);
        AssertRuleDoesNotFire(Lint(letter, "LT-DG"), DuplicateRequest);
    }

    [Fact]
    public void CMA_Duplicate_Sorocco_Dressing_Request_Repeated_Fires_And_Correction_Passes()
    {
        const string intro = "I am writing to refer Ms Isabel Garcia, who has a deteriorating venous ulcer on her right leg, for regular wound dressing.";
        AssertRuleFires(Lint(GarciaWith(intro,
            "I would be grateful if you could visit Ms Garcia daily for the first week and then once or twice weekly for dressing changes."), "LT-NM"), DuplicateRequest);
        AssertRuleDoesNotFire(Lint(GarciaWith(intro,
            "I would be grateful if you could visit Ms Garcia daily for the first week and then once or twice weekly."), "LT-NM"), DuplicateRequest);
    }

    [Fact]
    public void CMA_Duplicate_Dressing_Named_Only_In_History_Is_Not_A_Request()
    {
        // Doris White shape: the intro asks for ongoing nursing care; dressings
        // appear only as history, so the closure's wound dressing is distinct.
        var letter = GarciaWith(
            "I am writing to request your ongoing nursing care for Ms Isabel Garcia, whose graft has healed with negative pressure dressings.",
            "I would be grateful if you could provide wound dressing for Ms Garcia's graft.");
        AssertRuleDoesNotFire(Lint(letter, "LT-NM"), DuplicateRequest);
    }

    [Theory]
    [InlineData("I would be grateful if you could confirm the diagnosis and provide further management.")]
    [InlineData("I would be grateful if you could confirm the diagnosis and advise on ongoing management, including counselling and medication review.")]
    [InlineData("I would be grateful if you could verify the provisional diagnosis.")]
    public void CMA_Duplicate_Confirming_The_Diagnosis_After_An_Assessment_Request_Fires(string closure)
        // OET test 10 / Ms Martin (MATERIAL): "request your psychiatric assessment"
        // then "confirm the diagnosis".
        => AssertRuleFires(Lint(Inject(Weir, WeirClosure, closure), "LT-RR"), DuplicateRequest);

    [Theory]
    [InlineData("I would be grateful if you could provide further management.")]
    [InlineData("I would be grateful if you could clarify the possible diagnosis.")]
    [InlineData("I would be grateful if you could arrange MRI to establish the diagnosis.")]
    public void CMA_Duplicate_Distinct_Diagnostic_Objectives_Do_Not_Fire(string closure)
        => AssertRuleDoesNotFire(Lint(Inject(Weir, WeirClosure, closure), "LT-RR"), DuplicateRequest);

    [Fact]
    public void CMA_Duplicate_Is_Reported_Once_When_The_Verbatim_Branch_Already_Fires()
    {
        var letter = Inject(Weir, WeirClosure,
            "I would be grateful if you could confirm the diagnosis after your neurological assessment and management of Mr Weir.");
        Assert.Single(FindingsFor(Lint(letter, "LT-RR"), DuplicateRequest));
    }

    [Fact]
    public void CMA_Duplicate_Candidate_Lane_Is_Never_Flagged_By_The_Concept_Branch()
    {
        var letter = Inject(Garcia, GarciaClosure,
            "I would be grateful if you could contact close family and friends, advise prompt medical attention and observation, and consider chemoprophylaxis.");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG", isModelAnswer: false), DuplicateRequest);
    }

    [Fact]
    public void CMA_Duplicate_Clean_Fixtures_Stay_Clean()
    {
        AssertRuleDoesNotFire(Lint(Garcia, "LT-DG"), DuplicateRequest);
        AssertRuleDoesNotFire(Lint(Weir, "LT-RR"), DuplicateRequest);
        AssertRuleDoesNotFire(Lint(McDonald, "LT-TR"), DuplicateRequest);
        AssertRuleDoesNotFire(Lint(Taylor, "LT-UR"), DuplicateRequest);
        AssertRuleDoesNotFire(Lint(Weston, "LT-NM"), DuplicateRequest);
    }

    // ─────────────────────────────────────────────────────────────────
    // OA6-02 — request_action_unsupported
    // ─────────────────────────────────────────────────────────────────

    private const string MeadowsPlanNotes =
        "Electrolytes: low sodium, high potassium\n" +
        "Admitted for overnight observation\n" +
        "Plan: inform the GP of the findings\n" +
        "Patient to be updated on the endocrinologist's appointment details\n" +
        "Referral for outpatient endocrinologist review";

    [Fact]
    public void CMA_Unsupported_Meadows_Invented_Monitoring_Request_Fires()
    {
        var letter = Inject(Garcia, GarciaClosure,
            "I would be grateful if you could monitor Ms Garcia's symptoms and electrolytes until her endocrinologist's appointment.");
        var finding = Assert.Single(FindingsFor(Lint(letter, "LT-DG", caseNotes: MeadowsPlanNotes), Unsupported));
        Assert.Contains("electrolytes", finding.Message, StringComparison.Ordinal);
        Assert.Contains("symptoms", finding.Message, StringComparison.Ordinal);
        Assert.Equal(RuleSeverity.Critical, finding.Severity);
    }

    [Theory]
    [InlineData("I would be grateful if you could check Ms Garcia's renal function in one week.", "renal function")]
    [InlineData("I would be grateful if you could repeat Ms Garcia's full blood count and review her medications.", "full blood count")]
    [InlineData("I would be grateful if you could arrange blood pressure monitoring for Ms Garcia.", "blood pressure")]
    public void CMA_Unsupported_Other_Unplanned_Monitoring_Requests_Fire(string closure, string parameter)
    {
        var finding = Assert.Single(FindingsFor(Lint(Inject(Garcia, GarciaClosure, closure), "LT-DG", caseNotes: MeadowsPlanNotes), Unsupported));
        Assert.Contains(parameter, finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CMA_Unsupported_A_Recorded_Result_Is_Not_A_Monitoring_Plan()
    {
        var letter = Inject(Garcia, GarciaClosure, "I would be grateful if you could monitor Ms Garcia's electrolytes.");
        AssertRuleFires(Lint(letter, "LT-DG", caseNotes: "Electrolytes: low sodium, high potassium"), Unsupported);
    }

    [Theory]
    // Yanlin Ma: the notes plan both.
    [InlineData("I would be grateful if you could arrange haemoglobin testing and INR monitoring through Queensland Medical Laboratory.",
        "Haemoglobin should be checked.\nRefer to Queensland Medical Laboratory for INR monitoring; maintain INR at 2.5-3.5.")]
    // Abbreviation in the notes, words in the letter.
    [InlineData("I would be grateful if you could monitor Ms Garcia's blood pressure for one week.",
        "BP should be monitored and maintained at 120/80 or below.")]
    [InlineData("I would be grateful if you could monitor and assist with Ms Garcia's medications.",
        "GP to monitor medications and educate daughter regarding polypharmacy.")]
    public void CMA_Unsupported_Planned_Monitoring_Passes(string closure, string notes)
        => AssertRuleDoesNotFire(Lint(Inject(Garcia, GarciaClosure, closure), "LT-DG", caseNotes: notes), Unsupported);

    [Theory]
    // Owner-approved Meadows correction: not a monitoring request.
    [InlineData("I would be grateful if you could encourage Ms Garcia to attend her endocrinologist's appointment.")]
    [InlineData(GarciaClosure)]
    public void CMA_Unsupported_Non_Monitoring_Requests_Do_Not_Fire(string closure)
        => AssertRuleDoesNotFire(Lint(closure == GarciaClosure ? Garcia : Inject(Garcia, GarciaClosure, closure), "LT-DG", caseNotes: MeadowsPlanNotes), Unsupported);

    [Fact]
    public void CMA_Unsupported_Is_Silent_Without_Case_Notes()
    {
        var letter = Inject(Garcia, GarciaClosure, "I would be grateful if you could monitor Ms Garcia's electrolytes.");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG"), Unsupported);
    }

    [Fact]
    public void CMA_Unsupported_Candidate_Lane_Is_Never_Flagged()
    {
        var letter = Inject(Garcia, GarciaClosure, "I would be grateful if you could monitor Ms Garcia's electrolytes.");
        AssertRuleDoesNotFire(Lint(letter, "LT-DG", caseNotes: MeadowsPlanNotes, isModelAnswer: false), Unsupported);
    }

    [Fact]
    public void CMA_Unsupported_Provenance_And_Criterion_Are_Registered()
    {
        Assert.Contains(Unsupported, WritingRuleEngine.SupportedCheckIds);
        Assert.NotEqual(WritingCandidateBehaviors.ScoreBearing, WritingRuleProvenance.For(Unsupported).CandidateBehavior);
    }

    // ─────────────────────────────────────────────────────────────────
    // medication_list_punctuation — tablet count before the strength
    // ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("He continues to smoke. Glipizide, two 5 mg tablets each morning, was continued.")]
    [InlineData("He continues to smoke. His medications are metformin, 500 mg twice daily; glipizide, two 5 mg tablets each morning and atorvastatin, 10 mg daily.")]
    public void CMA_Medication_Tablet_Count_Before_Strength_Is_One_Correct_Item(string replacement)
        => AssertRuleDoesNotFire(Lint(Inject(Weir, "He continues to smoke.", replacement), "LT-RR"), MedicationList);

    [Fact]
    public void CMA_Medication_Missing_Drug_Comma_Before_A_Tablet_Count_Still_Fires()
    {
        var letter = Inject(Weir, "He continues to smoke.", "He continues to smoke. Glipizide two 5 mg tablets each morning was continued.");
        var finding = Assert.Single(FindingsFor(Lint(letter, "LT-RR"), MedicationList));
        Assert.Equal("Glipizide, 5 mg", finding.FixSuggestion);
    }
}
