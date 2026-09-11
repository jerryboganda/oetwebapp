using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Addendum Rev8 (11 Sep 2026) §7/§9/§14 — the Model Answer publish gate:
/// VERIFIED/CLEAN means zero unresolved violations of ANY severity under the
/// CURRENT validator; generation repairs only the failed rules and re-runs all
/// validators; the semantic validator's verdict is enforced; a stored verified
/// flag becomes invalid when the validator version changes; approval and the
/// candidate read path require verification under the running validator.
/// </summary>
public sealed class WritingRev8ModelAnswerGateTests
{
    private static LearnerDbContext NewDb()
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"writing-rev8-gate-{Guid.NewGuid()}")
            .Options);

    private static WritingTaskModelAnswerService Service(
        LearnerDbContext db, IAiGatewayService gateway, IWritingModelAnswerSemanticValidator? semantic = null)
        => new(db, gateway, new WritingRuleEngine(new RulebookLoader()), TimeProvider.System,
            NullLogger<WritingTaskModelAnswerService>.Instance, semantic);

    // The live Taylor defect's violations are mostly MAJOR (duplicated age,
    // repeated "urgent", "also", paragraph-start pronouns ...) — the pre-Rev8
    // gate held on Critical findings only, which is how such letters became
    // "Ready". Here the compliant exemplar's text is degraded with one
    // Major-only defect (a mid-sentence "also").
    private static string MajorOnlyDefect()
        => WritingModelAnswerBatchTests.ExemplarText()
            .Replace("He smokes and has been overweight long term.", "He smokes and has also been overweight long term.");

    [Fact]
    public async Task Import_Holds_A_Letter_With_Only_Major_Violations_And_Records_The_Report()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.", MajorOnlyDefect());
        var svc = Service(db, new ScriptedGateway());

        var dto = await svc.ImportAsync(scenarioId, MajorOnlyDefect(), "admin-1");

        Assert.Equal("HeldForReview", dto.Status);
        Assert.Equal("model_answer_rule_violations", dto.HoldReason);
        Assert.False(dto.IsCandidateVisible);
        var row = await db.WritingTaskModelAnswers.AsNoTracking().SingleAsync(a => a.ScenarioId == scenarioId);
        Assert.Null(row.ValidatorVersion);
        Assert.Contains("linker_avoid_words", row.ValidationReportJson);
    }

    [Fact]
    public async Task Import_Of_Compliant_Letter_Is_Verified_Under_Current_Validator_Then_Approvable()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        var svc = Service(db, new ScriptedGateway());

        var dto = await svc.ImportAsync(scenarioId, WritingModelAnswerBatchTests.ExemplarText(), "admin-1");

        Assert.Equal("Ready", dto.Status);
        Assert.False(dto.IsCandidateVisible);
        Assert.Equal(WritingRuleEngine.ValidatorVersion, dto.ValidatorVersion);
        Assert.StartsWith("rp-", dto.RulePackHash);
        Assert.InRange(dto.BodyWordCount ?? 0, 180, 200);
        Assert.Equal("verified_awaiting_approval", dto.VerificationStatus);

        var approved = await svc.ApproveAsync(scenarioId, "admin-1");
        Assert.True(approved!.IsCandidateVisible);
        var row = await db.WritingTaskModelAnswers.AsNoTracking().SingleAsync(a => a.ScenarioId == scenarioId);
        Assert.True(WritingTaskModelAnswerService.IsVerifiedForCandidates(row));
        Assert.Equal(1, await db.WritingTaskModelAnswers.CountAsync(WritingTaskModelAnswerService.CandidateVisibleVerified));
    }

    [Fact]
    public async Task Stored_Clean_Flag_From_An_Older_Validator_Is_Not_Candidate_Visible_And_Cannot_Be_Approved()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        db.WritingTaskModelAnswers.Add(new WritingTaskModelAnswer
        {
            Id = Guid.NewGuid(),
            ScenarioId = scenarioId,
            Status = WritingAssessmentModelAnswerStatus.Ready,
            IsCandidateVisible = true,
            ModelAnswerText = WritingModelAnswerBatchTests.ExemplarText(),
            ValidatorVersion = "writing-rules.rev5.2026-09-10",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var svc = Service(db, new ScriptedGateway());

        var row = await db.WritingTaskModelAnswers.AsNoTracking().SingleAsync(a => a.ScenarioId == scenarioId);
        Assert.False(WritingTaskModelAnswerService.IsVerifiedForCandidates(row));
        Assert.Equal(0, await db.WritingTaskModelAnswers.CountAsync(WritingTaskModelAnswerService.CandidateVisibleVerified));

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.ApproveAsync(scenarioId, "admin-1"));
        Assert.Equal("model_answer_not_verified", ex.ErrorCode);
    }

    [Fact]
    public async Task Revalidate_Apply_Stamps_Passing_Rows_And_Holds_Failing_Rows()
    {
        await using var db = NewDb();
        var good = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        var bad = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir again.", MajorOnlyDefect());
        foreach (var (id, text) in new[] { (good, WritingModelAnswerBatchTests.ExemplarText()), (bad, MajorOnlyDefect()) })
        {
            db.WritingTaskModelAnswers.Add(new WritingTaskModelAnswer
            {
                Id = Guid.NewGuid(),
                ScenarioId = id,
                Status = WritingAssessmentModelAnswerStatus.Ready,
                IsCandidateVisible = true,
                ModelAnswerText = text,
                ValidatorVersion = null,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        await db.SaveChangesAsync();
        var svc = Service(db, new ScriptedGateway());

        var report = await svc.RevalidateAsync(new WritingModelAnswerRevalidationRequest(
            Apply: false, IncludeSemantic: false, Profession: null, Offset: 0, Limit: 50, OnlyUnverified: false), "admin-1");
        Assert.Equal(2, report.Checked);
        Assert.Equal(1, report.Passed);
        Assert.Equal(1, report.Failed);
        Assert.Null((await db.WritingTaskModelAnswers.AsNoTracking().SingleAsync(a => a.ScenarioId == good)).ValidatorVersion);

        var applied = await svc.RevalidateAsync(new WritingModelAnswerRevalidationRequest(
            Apply: true, IncludeSemantic: false, Profession: null, Offset: 0, Limit: 50, OnlyUnverified: false), "admin-1");
        Assert.True(applied.Applied);
        var goodRow = await db.WritingTaskModelAnswers.AsNoTracking().SingleAsync(a => a.ScenarioId == good);
        var badRow = await db.WritingTaskModelAnswers.AsNoTracking().SingleAsync(a => a.ScenarioId == bad);
        Assert.Equal(WritingRuleEngine.ValidatorVersion, goodRow.ValidatorVersion);
        Assert.True(WritingTaskModelAnswerService.IsVerifiedForCandidates(goodRow));
        Assert.Equal(WritingAssessmentModelAnswerStatus.HeldForReview, badRow.Status);
        Assert.False(badRow.IsCandidateVisible);
        Assert.Equal("model_answer_revalidation_failed", badRow.HoldReason);
    }

    [Fact]
    public async Task Generate_Repairs_Only_Failed_Rules_Then_Stores_The_Passing_Text()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        var gateway = new ScriptedGateway(MajorOnlyDefect(), WritingModelAnswerBatchTests.ExemplarText());
        var svc = Service(db, gateway);

        var dto = await svc.GenerateAsync(scenarioId, "admin-1");

        Assert.Equal("Ready", dto.Status);
        Assert.Equal(2, gateway.Calls);
        Assert.Equal(1, dto.RepairCount);
        Assert.Contains("linker_avoid_words", gateway.LastUserInput);   // the repair prompt names the failed rule
        Assert.Contains("Previous draft", gateway.LastUserInput);
        Assert.Equal(WritingRuleEngine.ValidatorVersion, dto.ValidatorVersion);
    }

    [Fact]
    public async Task Generate_Never_Stores_When_Every_Attempt_Fails()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        var gateway = new ScriptedGateway(MajorOnlyDefect());
        var svc = Service(db, gateway);

        var dto = await svc.GenerateAsync(scenarioId, "admin-1");

        Assert.Equal("HeldForReview", dto.Status);
        Assert.Equal("model_answer_rule_violations", dto.HoldReason);
        Assert.Equal(4, gateway.Calls); // one generation + three targeted repairs, then stop
        Assert.False(dto.IsCandidateVisible);
    }

    [Fact]
    public async Task Semantic_Validator_Violations_Block_Storage_And_Unavailability_Is_Transient()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        var failing = new FixedSemantic(new WritingModelAnswerSemanticResult(false, false,
            [new WritingModelAnswerSemanticViolation("OWN-W-031", "Mr Weir has depression", "Background placed before the presenting complaint.")],
            "claude-sonnet-5", "2.1.0-canonical-rev8", null));
        var held = await Service(db, new ScriptedGateway(), failing)
            .ImportAsync(scenarioId, WritingModelAnswerBatchTests.ExemplarText(), "admin-1");
        Assert.Equal("model_answer_semantic_violations", held.HoldReason);
        Assert.Equal(1, failing.Calls);

        var unavailable = new FixedSemantic(new WritingModelAnswerSemanticResult(false, true, [], null, null, "semantic_validator_failed"));
        var transient = await Service(db, new ScriptedGateway(), unavailable)
            .ImportAsync(scenarioId, WritingModelAnswerBatchTests.ExemplarText(), "admin-1");
        Assert.Equal("model_answer_semantic_validator_unavailable", transient.HoldReason);
        Assert.True(WritingTaskModelAnswerService.IsTransientHold(transient.HoldReason));

        var passing = new FixedSemantic(new WritingModelAnswerSemanticResult(true, false, [], "claude-sonnet-5", "2.1.0-canonical-rev8", null));
        var ready = await Service(db, new ScriptedGateway(), passing)
            .ImportAsync(scenarioId, WritingModelAnswerBatchTests.ExemplarText(), "admin-1");
        Assert.Equal("Ready", ready.Status);
    }

    [Fact]
    public async Task Validate_Reports_Without_Storing_Anything()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        var svc = Service(db, new ScriptedGateway());

        var report = await svc.ValidateAsync(scenarioId, MajorOnlyDefect(), includeSemantic: false, "admin-1");

        Assert.False(report.Passed);
        Assert.Contains(report.DeterministicFindings, f => f.RuleId == "BUILTIN.linker_avoid_words");
        Assert.Equal(WritingRuleEngine.ValidatorVersion, report.ValidatorVersion);
        Assert.Equal(0, await db.WritingTaskModelAnswers.CountAsync());
    }

    [Fact]
    public void Contact_Offer_Courtesy_Sentence_Is_Not_An_Unmapped_Fact()
    {
        var letter = WritingModelAnswerBatchTests.ExemplarText();
        var facts = WritingModelAnswerBatchTests.CaseNoteSentencesFor(letter)
            .Where(s => !s.StartsWith("Should there be", StringComparison.Ordinal))
            .ToList();
        var grounding = WritingModelAnswerGroundingValidator.Validate(letter, facts);
        Assert.True(grounding.IsGrounded, string.Join(" | ", grounding.UnmappedSentences));
    }

    private sealed class FixedSemantic(WritingModelAnswerSemanticResult result) : IWritingModelAnswerSemanticValidator
    {
        public int Calls { get; private set; }

        public Task<WritingModelAnswerSemanticResult> ValidateAsync(WritingModelAnswerSemanticRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    /// <summary>Returns the scripted letters in order (the last one repeats).</summary>
    private sealed class ScriptedGateway(params string[] letters) : IAiGatewayService
    {
        public int Calls { get; private set; }
        public string LastUserInput { get; private set; } = "";

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "generate",
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            var letter = letters.Length == 0
                ? WritingModelAnswerBatchTests.ExemplarText()
                : letters[Math.Min(Calls, letters.Length - 1)];
            Calls++;
            LastUserInput = request.UserInput ?? "";
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                modelAnswerText = letter,
                whyThisWorks = new[] { "Grounded exemplar." },
                groundedFactReferences = new[] { "case-note-line:1" },
            });
            return Task.FromResult(new AiGatewayResult { Completion = json, ResolvedModel = "claude-sonnet-5" });
        }
    }
}
