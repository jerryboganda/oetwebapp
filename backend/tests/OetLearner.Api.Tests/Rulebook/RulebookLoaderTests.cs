using System.Text.Json;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Rulebooks;

namespace OetLearner.Api.Tests.Rulebook;

public class RulebookLoaderTests
{
    private readonly RulebookLoader _loader = new();

    [Fact]
    public void Loads_Writing_Medicine_Rulebook()
    {
        var book = _loader.Load(RuleKind.Writing, ExamProfession.Medicine);
        Assert.Equal(RuleKind.Writing, book.Kind);
        Assert.Equal(ExamProfession.Medicine, book.Profession);
        // Owner Rev8 (11 Sep 2026): canonical Writing rulebooks are regenerated
        // with the Rev8 registry additions (OWN-W-001..038) under this version.
        Assert.Equal("2.5.0-cross-model-audit", book.Version);
        // Sections grew as the global owner rows (OWN-W + OA/OA2/OA3/OA4)
        // joined the canonical pack; OA4 added "Layout" and "Identity" (84).
        // The Senior Assessor Release Audit rows (OA5-01..OA5-38) reuse
        // existing section titles, so the count stays 84.
        Assert.Equal(84, book.Sections.Count);
        Assert.True(book.Rules.Count >= 90);
    }

    [Fact]
    public void Loads_Speaking_Medicine_Rulebook()
    {
        var book = _loader.Load(RuleKind.Speaking, ExamProfession.Medicine);
        Assert.Equal(RuleKind.Speaking, book.Kind);
        Assert.Equal(ExamProfession.Medicine, book.Profession);
        Assert.Equal(8, book.Sections.Count);
        Assert.Equal(62, book.Rules.Count);
    }

    [Fact]
    public void Loads_Speaking_Nursing_Rulebook_ForSeededRolePlay()
    {
        var book = _loader.Load(RuleKind.Speaking, ExamProfession.Nursing);
        Assert.Equal(RuleKind.Speaking, book.Kind);
        Assert.Equal(ExamProfession.Nursing, book.Profession);
        Assert.Contains(book.Rules, rule => rule.Id == "RULE_56" && rule.AppliesTo.HasValue);
    }

    [Fact]
    public void Throws_For_Unregistered_Profession_Combination()
    {
        var registered = _loader.All()
            .Select(book => (book.Kind, book.Profession))
            .ToHashSet();

        var missing = Enum.GetValues<RuleKind>()
            .SelectMany(kind => Enum.GetValues<ExamProfession>().Select(profession => (kind, profession)))
            .FirstOrDefault(pair => !registered.Contains(pair)
                && !(OetLearner.Api.Services.Rulebook.RulebookLoader.IsAlliedHealth(pair.profession)
                    && registered.Contains((pair.kind, ExamProfession.OtherAlliedHealth))));

        Assert.DoesNotContain(registered, pair => pair == missing);
        Assert.Throws<OetLearner.Api.Services.Rulebook.RulebookNotFoundException>(() =>
            _loader.Load(missing.kind, missing.profession));
    }

    [Theory]
    [InlineData(ExamProfession.Dietetics)]
    [InlineData(ExamProfession.OccupationalTherapy)]
    [InlineData(ExamProfession.Optometry)]
    [InlineData(ExamProfession.Podiatry)]
    [InlineData(ExamProfession.SpeechPathology)]
    [InlineData(ExamProfession.Veterinary)]
    public void Speaking_Allied_Health_Professions_Use_The_Allied_Health_Book(ExamProfession profession)
    {
        // Production 23 Sep 2026: these six had no Speaking book, so every
        // Speaking grade for their learners threw RulebookNotFoundException.
        var book = _loader.Load(RuleKind.Speaking, profession);
        Assert.Same(_loader.Load(RuleKind.Speaking, ExamProfession.OtherAlliedHealth), book);
        Assert.NotEmpty(book.Rules);
    }

    // Canonical successors of the retired R-id criticals (rulebook
    // 2.0.0-canonical): R03.4 -> OW-007, R07.6 -> DH-W-009,
    // R09.2 -> DH-W-016, R13.10 -> OW-017, R14.6 -> DH-W-030,
    // R14.12 -> DH-W-036.
    [Theory]
    [InlineData("OW-007")]
    [InlineData("DH-W-009")]
    [InlineData("DH-W-016")]
    [InlineData("OW-017")]
    [InlineData("DH-W-030")]
    [InlineData("DH-W-036")]
    public void Critical_Writing_Rule_Found(string id)
    {
        var rule = _loader.FindRule(RuleKind.Writing, ExamProfession.Medicine, id);
        Assert.NotNull(rule);
        Assert.Equal(RuleSeverity.Critical, rule!.Severity);
    }

    [Theory]
    [InlineData("RULE_06")]
    [InlineData("RULE_22")]
    [InlineData("RULE_27")]
    [InlineData("RULE_32")]
    [InlineData("RULE_44")]
    public void Critical_Speaking_Rule_Found(string id)
    {
        var rule = _loader.FindRule(RuleKind.Speaking, ExamProfession.Medicine, id);
        Assert.NotNull(rule);
        Assert.Equal(RuleSeverity.Critical, rule!.Severity);
    }

    [Fact]
    public void Speaking_Has_13Stage_Consultation_StateMachine()
    {
        var book = _loader.Load(RuleKind.Speaking, ExamProfession.Medicine);
        Assert.NotNull(book.StateMachines);
        var sm = book.StateMachines!.Value;
        Assert.Equal(13, sm.GetProperty("consultationStages").GetArrayLength());
        Assert.Equal(7, sm.GetProperty("breakingBadNewsProtocol").GetArrayLength());
        Assert.Equal(3, sm.GetProperty("smokingLadder").GetArrayLength());
    }

    [Fact]
    public void Writing_Has_Letter_Skeleton_Table()
    {
        var book = _loader.Load(RuleKind.Writing, ExamProfession.Medicine);
        Assert.NotNull(book.Tables);
        var skeleton = book.Tables!.Value.GetProperty("letterSkeleton");
        Assert.True(skeleton.GetArrayLength() > 10);
    }

    [Fact]
    public void Assessment_Criteria_Is_Loaded_For_Both_Kinds()
    {
        Assert.Equal(JsonValueKind.Object, _loader.GetAssessmentCriteria(RuleKind.Writing).ValueKind);
        Assert.Equal(JsonValueKind.Object, _loader.GetAssessmentCriteria(RuleKind.Speaking).ValueKind);
    }
}
