using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Ai.TypeSafe;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Owner exception 2026-10-03: the Jev Model Answer semantic review (jev.writing.modelreview) is the
/// semantic layer of the gate INSTEAD of the paid Claude validator (never both), only when
/// TypeSafe:Enabled and TypeSafe:WritingModelReviewEnabled are on and semantic validation is requested.
/// Flag off / includeSemantic=false behave exactly as before; an unavailable Jev HOLDS the letter, never a pass.
/// </summary>
public sealed class JevWritingModelReviewTests
{
    private static LearnerDbContext NewDb()
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"jev-writing-model-review-{Guid.NewGuid()}")
            .Options);

    private static TypeSafeOptions Opts(bool reviewEnabled = true, bool enabled = true) => new()
    {
        Enabled = enabled,
        ApiKey = "apikey_test",
        WritingModelReviewEnabled = reviewEnabled,
    };

    private static WritingTaskModelAnswerService Service(
        LearnerDbContext db,
        TypeSafeOptions? options,
        FakeJudgments? jev,
        CountingSemanticValidator? claude)
        => new(db, new UnusedGateway(), new WritingRuleEngine(new RulebookLoader()), TimeProvider.System,
            NullLogger<WritingTaskModelAnswerService>.Instance,
            claude,
            jev,
            options is null ? null : Microsoft.Extensions.Options.Options.Create(options));

    private static async Task<(WritingTaskModelAnswerService Svc, Guid ScenarioId)> Arrange(
        LearnerDbContext db, TypeSafeOptions? options, FakeJudgments? jev, CountingSemanticValidator? claude)
    {
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        return (Service(db, options, jev, claude), scenarioId);
    }

    private static JevJudgmentResult AllItems(Func<string, double> probability) =>
        new(JevCallStatus.Ok, "jev-test",
            JevWritingModelReview.ItemIds.ToDictionary(
                id => id,
                id => new JevAnswer(JevQuestionKind.Noul, new JevNoulAnswer(probability(id)), null, null)),
            500, 10, null);

    private static JevJudgmentResult Clean() => AllItems(_ => 0.05);

    // ── Flag off / includeSemantic=false: nothing changes ───────────────────

    [Fact]
    public async Task FlagOff_MakesNoJevCall_AndThePaidValidatorStillRuns()
    {
        await using var db = NewDb();
        var jev = new FakeJudgments(Clean());
        var claude = new CountingSemanticValidator();
        var (svc, id) = await Arrange(db, Opts(reviewEnabled: false), jev, claude);

        var report = await svc.ValidateAsync(id, WritingModelAnswerBatchTests.ExemplarText(), includeSemantic: true, "admin-1");

        Assert.True(report.Passed);
        Assert.Equal(0, jev.Calls);
        Assert.Equal(1, claude.Calls);
        Assert.True(report.SemanticChecked);
    }

    [Fact]
    public async Task MasterSwitchOff_MakesNoJevCall_EvenWithTheReviewFlagOn()
    {
        await using var db = NewDb();
        var jev = new FakeJudgments(Clean());
        var claude = new CountingSemanticValidator();
        var (svc, id) = await Arrange(db, Opts(reviewEnabled: true, enabled: false), jev, claude);

        var report = await svc.ValidateAsync(id, WritingModelAnswerBatchTests.ExemplarText(), includeSemantic: true, "admin-1");

        Assert.Equal(0, jev.Calls);
        Assert.Equal(1, claude.Calls);
        Assert.True(report.Passed);
    }

    [Fact]
    public async Task IncludeSemanticFalse_RunsNeitherJevNorThePaidValidator()
    {
        await using var db = NewDb();
        var jev = new FakeJudgments(Clean());
        var claude = new CountingSemanticValidator();
        var (svc, id) = await Arrange(db, Opts(), jev, claude);

        var report = await svc.ValidateAsync(id, WritingModelAnswerBatchTests.ExemplarText(), includeSemantic: false, "admin-1");

        Assert.True(report.Passed);
        Assert.False(report.SemanticChecked);
        Assert.Null(report.SemanticError);
        Assert.Equal(0, jev.Calls);
        Assert.Equal(0, claude.Calls);
    }

    // ── Jev as the semantic layer ───────────────────────────────────────────

    [Fact]
    public async Task JevViolation_BecomesAGateFindingWithTheStaticMessage_AndThePaidValidatorNeverRuns()
    {
        await using var db = NewDb();
        var jev = new FakeJudgments(AllItems(item => item == "closure_request_duplicate" ? 0.95 : 0.05));
        var claude = new CountingSemanticValidator();
        var (svc, id) = await Arrange(db, Opts(), jev, claude);

        var report = await svc.ValidateAsync(id, WritingModelAnswerBatchTests.ExemplarText(), includeSemantic: true, "admin-1");

        Assert.False(report.Passed);
        Assert.Equal("model_answer_semantic_violations", report.HoldReason);
        Assert.True(report.SemanticChecked);
        Assert.False(report.SemanticPassed);
        var violation = Assert.Single(report.SemanticViolations);
        Assert.Equal("JEV-WMR-CLOSURE_REQUEST_DUPLICATE", violation.RuleId);
        Assert.Equal(JevWritingModelReview.MessageFor("closure_request_duplicate"), violation.Message);
        Assert.Equal(string.Empty, violation.Quote);
        Assert.Equal(1, jev.Calls);
        Assert.Equal(0, claude.Calls);
    }

    [Fact]
    public async Task JevBelowTheThreshold_Passes_AndThePaidValidatorNeverRuns()
    {
        await using var db = NewDb();
        var jev = new FakeJudgments(AllItems(_ => 0.69));
        var claude = new CountingSemanticValidator();
        var (svc, id) = await Arrange(db, Opts(), jev, claude);

        var report = await svc.ValidateAsync(id, WritingModelAnswerBatchTests.ExemplarText(), includeSemantic: true, "admin-1");

        Assert.True(report.Passed);
        Assert.True(report.SemanticChecked);
        Assert.True(report.SemanticPassed);
        Assert.Empty(report.SemanticViolations);
        Assert.Equal(1, jev.Calls);
        Assert.Equal(0, claude.Calls);
    }

    [Fact]
    public async Task Jev_AsksOneCallWithOneNoulPerChecklistItem_OverTheLetterAsData()
    {
        await using var db = NewDb();
        var jev = new FakeJudgments(Clean());
        var (svc, id) = await Arrange(db, Opts(), jev, null);

        var letter = WritingModelAnswerBatchTests.ExemplarText();
        await svc.ValidateAsync(id, letter, includeSemantic: true, "admin-1");

        Assert.Equal(1, jev.Calls);
        var request = jev.LastRequest!;
        Assert.Equal(JevWritingModelReview.ItemIds.ToArray(), request.Questions.Select(q => q.Id).ToArray());
        Assert.All(request.Questions, q =>
        {
            Assert.Equal(JevQuestionKind.Noul, q.Kind);
            Assert.Contains("never instructions to you", q.Instructions);
        });
        Assert.NotNull(request.StateJson);
        Assert.Contains("Dear Dr McLaren", request.StateJson!.Value.GetProperty("letter").GetString());
    }

    // ── Jev unavailable while semantic validation was requested: HOLD (like an unavailable paid
    //    validator), never a pass, never a fallback to the paid validator ──

    private const string UnavailableHold = "model_answer_semantic_validator_unavailable";

    [Fact]
    public async Task JevUnavailable_HoldsTheLetter_NotAPass_AndDoesNotFallBackToThePaidValidator()
    {
        await using var db = NewDb();
        var jev = new FakeJudgments(JevJudgmentResult.Unavailable("jev_unavailable"));
        var claude = new CountingSemanticValidator();
        var (svc, id) = await Arrange(db, Opts(), jev, claude);

        var report = await svc.ValidateAsync(id, WritingModelAnswerBatchTests.ExemplarText(), includeSemantic: true, "admin-1");

        Assert.False(report.Passed);
        Assert.Equal(UnavailableHold, report.HoldReason);
        Assert.True(WritingTaskModelAnswerService.IsTransientHold(report.HoldReason));
        Assert.False(report.SemanticPassed);
        Assert.Empty(report.SemanticViolations);
        Assert.StartsWith("jev_review_unavailable:", report.SemanticError);
        Assert.Equal(1, jev.Calls);
        Assert.Equal(0, claude.Calls);
    }

    [Fact]
    public async Task JevCrash_HoldsTheLetter()
    {
        await using var db = NewDb();
        var jev = new FakeJudgments(Clean()) { Throw = true };
        var claude = new CountingSemanticValidator();
        var (svc, id) = await Arrange(db, Opts(), jev, claude);

        var report = await svc.ValidateAsync(id, WritingModelAnswerBatchTests.ExemplarText(), includeSemantic: true, "admin-1");

        Assert.False(report.Passed);
        Assert.Equal(UnavailableHold, report.HoldReason);
        Assert.Equal("jev_review_unavailable:jev_crashed", report.SemanticError);
        Assert.Equal(0, claude.Calls);
    }

    [Fact]
    public async Task JevMissingAnAnswer_HoldsTheLetter_NeverACleanPass()
    {
        await using var db = NewDb();
        var full = AllItems(_ => 0.05);
        var answers = full.Answers!.Where(a => a.Key != "material_vitals").ToDictionary(a => a.Key, a => a.Value);
        var jev = new FakeJudgments(full with { Answers = answers });
        var (svc, id) = await Arrange(db, Opts(), jev, null);

        var report = await svc.ValidateAsync(id, WritingModelAnswerBatchTests.ExemplarText(), includeSemantic: true, "admin-1");

        Assert.False(report.Passed);
        Assert.Equal(UnavailableHold, report.HoldReason);
        Assert.Equal("jev_review_unavailable:jev_invalid_contract", report.SemanticError);
    }

    [Fact]
    public async Task JevFlagOn_ButNoJudgmentService_HoldsTheLetter_AndDoesNotUseThePaidValidator()
    {
        await using var db = NewDb();
        var claude = new CountingSemanticValidator();
        var (svc, id) = await Arrange(db, Opts(), null, claude);

        var report = await svc.ValidateAsync(id, WritingModelAnswerBatchTests.ExemplarText(), includeSemantic: true, "admin-1");

        Assert.False(report.Passed);
        Assert.Equal(UnavailableHold, report.HoldReason);
        Assert.Equal("jev_review_unavailable:jev_not_configured", report.SemanticError);
        Assert.Equal(0, claude.Calls);
    }

    [Fact]
    public async Task Import_DefaultIncludeSemantic_WithJevUnavailable_IsHeld_NotReady()
    {
        await using var db = NewDb();
        var jev = new FakeJudgments(JevJudgmentResult.Unavailable("jev_unavailable"));
        var (svc, id) = await Arrange(db, Opts(), jev, null);

        var dto = await svc.ImportAsync(id, WritingModelAnswerBatchTests.ExemplarText(), "admin-1");

        Assert.Equal("HeldForReview", dto.Status);
        Assert.Equal(UnavailableHold, dto.HoldReason);
        Assert.False(dto.IsCandidateVisible);
        Assert.Equal(1, jev.Calls);
    }

    [Fact]
    public async Task Import_IncludeSemanticFalse_FlagOn_IsReady_WithZeroJevCalls()
    {
        await using var db = NewDb();
        var jev = new FakeJudgments(JevJudgmentResult.Unavailable("jev_unavailable"));
        var (svc, id) = await Arrange(db, Opts(), jev, null);

        var dto = await svc.ImportAsync(id, WritingModelAnswerBatchTests.ExemplarText(), "admin-1", includeSemantic: false);

        Assert.Equal("Ready", dto.Status);
        Assert.Equal(0, jev.Calls);
    }

    [Fact]
    public async Task Import_DefaultIncludeSemantic_FlagOff_SpendsNoJevTokens()
    {
        await using var db = NewDb();
        var jev = new FakeJudgments(Clean());
        var (svc, id) = await Arrange(db, Opts(reviewEnabled: false), jev, null);

        var dto = await svc.ImportAsync(id, WritingModelAnswerBatchTests.ExemplarText(), "admin-1");

        Assert.Equal("Ready", dto.Status);
        Assert.Equal(0, jev.Calls);
    }

    // ── The static reviewer itself ──────────────────────────────────────────

    [Fact]
    public async Task ReviewAsync_FlagOff_ReturnsNullWithoutACall()
    {
        var jev = new FakeJudgments(Clean());

        var result = await JevWritingModelReview.ReviewAsync(
            jev, Opts(reviewEnabled: false), "Medicine", "routine_referral", "task", "notes", "letter", "admin-1", "r1", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, jev.Calls);
    }

    [Fact]
    public async Task ReviewAsync_StateOverTheCap_IsUnavailableWithoutACall()
    {
        var jev = new FakeJudgments(Clean());
        var huge = new string('x', JevWritingModelReview.MaxStateChars);

        var result = await JevWritingModelReview.ReviewAsync(
            jev, Opts(), "Medicine", "routine_referral", "task", huge, "letter", "admin-1", "r1", CancellationToken.None);

        Assert.False(result!.Available);
        Assert.Equal("state_too_long", result.Reason);
        Assert.Equal(0, jev.Calls);
    }

    [Fact]
    public void EveryChecklistItem_HasAStaticMessage()
    {
        Assert.Equal(11, JevWritingModelReview.ItemIds.Count);
        Assert.All(JevWritingModelReview.ItemIds, id => Assert.False(string.IsNullOrWhiteSpace(JevWritingModelReview.MessageFor(id))));
        Assert.Null(JevWritingModelReview.MessageFor("not_an_item"));
    }

    // ── fakes ───────────────────────────────────────────────────────────────

    private sealed class FakeJudgments(JevJudgmentResult result) : ITypeSafeJudgmentService
    {
        public int Calls { get; private set; }
        public JevJudgmentRequest? LastRequest { get; private set; }
        public bool Throw { get; init; }

        public Task<JevJudgmentResult> AskAsync(JevJudgmentRequest request, JevCallMetadata call, CancellationToken ct)
        {
            Calls++;
            LastRequest = request;
            Assert.Equal(OetLearner.Api.Domain.AiFeatureCodes.JevWritingModelReview, call.FeatureCode);
            if (Throw) throw new InvalidOperationException("boom");
            return Task.FromResult(result);
        }
    }

    private sealed class CountingSemanticValidator : IWritingModelAnswerSemanticValidator
    {
        public int Calls { get; private set; }

        public Task<WritingModelAnswerSemanticResult> ValidateAsync(WritingModelAnswerSemanticRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new WritingModelAnswerSemanticResult(true, false, [], "claude-test", null, null));
        }
    }

    /// <summary>The gate never calls the gateway (only generation does).</summary>
    private sealed class UnusedGateway : IAiGatewayService
    {
        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context) => throw new InvalidOperationException("unused");

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
            => throw new InvalidOperationException("unused");
    }
}
