using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Senior Assessor Release Audit (owner, 16 Sep 2026), round 2. Revalidating
/// the 55 repaired Medicine Model Answers exposed four validator false
/// positives on correct, owner-mandated wording. Per the owner's workflow
/// principle the validator is fixed, never the correct letter; each class
/// proves the false positive is gone AND that the genuine defect still fires.
/// </summary>
public sealed class WritingSeniorAuditRound2RegressionTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());

    private static List<LintFinding> Lint(string letter, bool isModelAnswer = true)
        => Engine.Lint(new WritingLintInput(
            LetterText: letter,
            LetterType: "LT-RR",
            Profession: ExamProfession.Medicine,
            IsModelAnswer: isModelAnswer)).ToList();

    private static bool Fires(List<LintFinding> findings, string checkId)
        => findings.Any(f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal));

    private static string Inject(string letter, string find, string replace)
    {
        var injected = letter.Replace(find, replace);
        Assert.NotEqual(letter, injected);
        return injected;
    }

    private const string Weir = WritingRev8RegressionFixtureTests.WeirRoutineReferralLetter;
    private const string WeirBp = "His blood pressure was 88/70 mmHg.";

    // Mrs Mary Clarke: "follow-up investigations" is a planned test, not a review.
    [Fact]
    public void SA_R2_FollowUp_Investigations_Are_Not_A_Review_Appointment()
    {
        var markers = WritingCaseNotesMarkerExtractor.Derive(
            "Plan: refer to thoracic surgeon for follow-up investigations (bronchoscopy, biopsy) and assessment.");
        Assert.Null(markers.FollowUpDate);
    }

    [Theory]
    [InlineData("Follow-up in 2 weeks.", "2 weeks")]
    [InlineData("Review in 3 months to assess weight and fitness.", "3 months")]
    public void SA_R2_A_Real_Follow_Up_Instruction_Still_Sets_The_Marker(string notes, string expected)
    {
        var markers = WritingCaseNotesMarkerExtractor.Derive(notes);
        Assert.NotNull(markers.FollowUpDate);
        Assert.Contains(expected, markers.FollowUpDate!, StringComparison.OrdinalIgnoreCase);
    }

    // Sylvia Meadows: HbA1c is a result name, not a unitless value.
    [Fact]
    public void SA_R2_HbA1c_Without_A_Value_Is_Not_A_Unitless_Result()
    {
        var letter = Inject(Weir, WeirBp, "His blood pressure and HbA1c level were normal.");
        Assert.False(Fires(Lint(letter), "numerical_values_have_units"));
        Assert.False(Fires(Lint(letter, isModelAnswer: false), "numerical_values_have_units"));
    }

    [Fact]
    public void SA_R2_A_Real_Unitless_HbA1c_Value_Still_Fires()
    {
        var letter = Inject(Weir, WeirBp, "His HbA1c level was 9.");
        Assert.True(Fires(Lint(letter), "numerical_values_have_units"));
    }

    // Sylvia Meadows: tablet counts and "as required" still form an OA2-16 list.
    [Fact]
    public void SA_R2_Word_Dose_Medication_List_Semicolons_Are_List_Separators()
    {
        var letter = Inject(Weir, WeirBp,
            "His medications are ramipril, two tablets; Ventolin, as required; amlodipine, two tablets and metformin, 500 mg twice daily.");
        Assert.False(Fires(Lint(letter), "semicolon_overuse"));
    }

    [Fact]
    public void SA_R2_A_Narrative_Semicolon_Still_Fires()
    {
        var letter = Inject(Weir, WeirBp, "He takes two tablets each morning; he feels better.");
        Assert.True(Fires(Lint(letter), "semicolon_overuse"));
    }

    // Sandra Marcus / Lucy Clarke: the owner-mandated neutral forms must ground.
    [Fact]
    public void SA_R2_Did_Not_Attend_Grounds_On_Defaulted()
    {
        var result = WritingModelAnswerGroundingValidator.Validate(
            "Dear Doctor,\nRe: Ms Sandra Marcus\n\nShe did not attend her follow-up appointment.\n\nYours sincerely,\n\nDoctor",
            ["Patient defaulted her appointment, and presented today (06/10/19)."]);
        Assert.True(result.IsGrounded, string.Join(" | ", result.UnmappedSentences));
    }

    [Fact]
    public void SA_R2_Neutral_Smoking_And_Drinking_Forms_Ground_On_Label_Nouns()
    {
        var result = WritingModelAnswerGroundingValidator.Validate(
            "Dear Doctor,\nRe: Mrs Lucy Clarke\n\nShe does not smoke and drinks alcohol socially.\n\nYours sincerely,\n\nDoctor",
            ["Non-smoker.", "Social drinker."]);
        Assert.True(result.IsGrounded, string.Join(" | ", result.UnmappedSentences));
    }

    // Round 3 (final letter round): a specialty-only recipient is a role.
    [Theory]
    [InlineData("Dear Neuro-Ophthalmologist,", true)]
    [InlineData("Dear Cardiologist,", true)]
    [InlineData("Dear Consultant Paediatrician,", true)]
    [InlineData("Dear Gynaecology Registrar,", true)]
    [InlineData("Dear Doctor,", true)]
    [InlineData("Dear Dr Still,", false)]
    [InlineData("Dear Mr Kist,", false)]
    [InlineData("Dear Ms Goody,", false)]
    public void SA_R3_Specialty_Role_Salutations_Are_Unnamed_Recipients(string salutation, bool unnamed)
        => Assert.Equal(unnamed, WritingRuleEngine.SalutationIsUnnamedRecipient(salutation));

    [Fact]
    public void SA_R3_Specialty_Role_Salutation_Closes_Yours_Faithfully()
    {
        var letter = Inject(
            Inject(WritingRev8RegressionFixtureTests.WestonReferralLetter, "Dear Ms Goody,", "Dear Occupational Therapist,"),
            "Yours sincerely,", "Yours faithfully,");
        Assert.False(Fires(Lint(letter), "yours_sincerely_vs_faithfully"));
    }

    // Round 3: "at 9 and 10 weeks" is gestation; a real past-event age still fires.
    [Fact]
    public void SA_R3_Coordinated_Gestation_Is_Not_A_Past_Event_Age()
    {
        var letter = Inject(Weir, WeirBp, "His wife had two miscarriages in 2008, at 9 and 10 weeks.");
        Assert.False(Fires(Lint(letter), "number_style_words_vs_digits"));
    }

    [Fact]
    public void SA_R3_A_Past_Event_Age_In_Digits_Still_Fires()
    {
        var letter = Inject(Weir, WeirBp, "He had an appendectomy at 15 and recovered well.");
        Assert.True(Fires(Lint(letter), "number_style_words_vs_digits"));
    }

    [Fact]
    public void SA_R2_An_Invented_Heavy_Drinking_Claim_Still_Does_Not_Ground()
    {
        var result = WritingModelAnswerGroundingValidator.Validate(
            "Dear Doctor,\nRe: Mrs Lucy Clarke\n\nShe drinks alcohol heavily every evening.\n\nYours sincerely,\n\nDoctor",
            ["Non-smoker."]);
        Assert.False(result.IsGrounded);
    }
}
