using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Services.Writing.Configuration;
using OetLearner.Api.Services.Writing.Events;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Fidelity guarantees for the live Writing grading pipeline
/// (<see cref="WritingSubmissionEvaluationPipeline"/>):
///   1. It parses the canonical grounded scoring contract (criteriaScores-based)
///      into the right C1..C6 columns, raw total, band label, per-criterion rich
///      object (keyed c1..c6) and honest model provenance.
///   2. It NEVER fabricates a grade: an unreadable / incomplete AI contract
///      fails loud and retryable, persisting no <see cref="WritingGrade"/>.
///
/// Uses SQLite in-memory (repo convention) and a controllable fake gateway so we
/// exercise the parser/persistence exactly.
/// </summary>
public sealed class WritingSubmissionEvaluationPipelineTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;

    public WritingSubmissionEvaluationPipelineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new LearnerDbContext(options);
        _db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task EvaluateAsync_CanonicalCriteriaScores_MapsGradeWithoutFabrication()
    {
        var submissionId = await SeedPracticeSubmissionAsync();

        // Canonical grounded reply contract (the single source of truth) — NOT
        // the old {c1..c6} shape. purpose=3 (max 3), others 0..7. raw total =
        // 3+6+5+7+4+6 = 31 → OET band "B".
        const string completion = """
            {
              "findings": [
                { "ruleId": "R03.4", "severity": "critical", "quote": "no allergy line",
                  "message": "State the penicillin allergy in the opening.",
                  "fixSuggestion": "Add the allergy to the first paragraph.", "criterionCode": "content" }
              ],
              "criteriaScores": { "purpose": 3, "content": 6, "conciseness_clarity": 5, "genre_style": 7, "organisation_layout": 4, "language": 6 },
              "estimatedScaledScore": 380,
              "estimatedGrade": "B",
              "passed": true,
              "passRequires": { "scaled": 350, "grade": "B" },
              "advisory": "AI-generated — pending tutor review"
            }
            """;
        var gateway = new FakeAiGateway(completion, "claude-sonnet-4-6", new[] { "R03.4" });
        var pipeline = BuildPipeline(gateway, aiPathReached: true);

        var outcome = await pipeline.EvaluateAsync(submissionId, default);

        var grade = await _db.WritingGrades.AsNoTracking().SingleAsync(g => g.SubmissionId == submissionId);
        Assert.Equal(3, grade.C1Purpose);
        Assert.Equal(6, grade.C2Content);
        Assert.Equal(5, grade.C3Conciseness);
        Assert.Equal(7, grade.C4Genre);
        Assert.Equal(4, grade.C5Organisation);
        Assert.Equal(6, grade.C6Language);
        Assert.Equal(31, grade.RawTotal);
        // EstimatedBand is stored in raw-total units (0..38) so OetBandLabel is
        // correct — the previous code stored a scaled/200 value and mislabelled.
        Assert.Equal(31, grade.EstimatedBand);
        Assert.Equal("B", grade.BandLabel);
        Assert.Equal("high", grade.ConfidenceFlag);
        // Honest model provenance — not the prompt-template id.
        Assert.Equal("claude-sonnet-4-6", grade.ModelUsed);
        Assert.Equal(31, outcome.RawTotal);
        Assert.Equal("B", outcome.BandLabel);
        Assert.False(outcome.IdempotentReuse);

        // Per-criterion feedback is the rich object keyed c1..c6 that the
        // response mapper renders; the content finding lands on c2.
        using var doc = JsonDocument.Parse(grade.PerCriterionFeedbackJson);
        var c2 = doc.RootElement.GetProperty("c2");
        Assert.Equal(6, c2.GetProperty("score").GetInt32());
        var cited = c2.GetProperty("citedRuleIds").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("R03.4", cited);
        Assert.Contains("penicillin allergy", c2.GetProperty("feedback").GetString());
    }

    [Theory]
    // Not JSON at all.
    [InlineData("I am unable to grade this letter.")]
    // Only two of six criteria — an incomplete scoring contract.
    [InlineData("{\"criteriaScores\":{\"purpose\":3,\"content\":6},\"estimatedScaledScore\":380}")]
    // All six criteria but no estimatedScaledScore — still incomplete.
    [InlineData("{\"criteriaScores\":{\"purpose\":3,\"content\":6,\"conciseness_clarity\":5,\"genre_style\":7,\"organisation_layout\":4,\"language\":6}}")]
    public async Task EvaluateAsync_UnreadableOrIncompleteContract_FailsLoudAndWritesNoGrade(string completion)
    {
        var submissionId = await SeedPracticeSubmissionAsync();
        // The fail-loud branch fires inside CallRubricAsync, before the canon
        // engine / event bus are reached — so they are not needed here.
        var pipeline = BuildPipeline(new FakeAiGateway(completion), aiPathReached: false);

        var ex = await Assert.ThrowsAsync<ApiException>(() => pipeline.EvaluateAsync(submissionId, default));
        Assert.Equal("writing_rubric_failed", ex.ErrorCode);

        // Crucially: NO fabricated "all 3s" grade is persisted.
        Assert.False(await _db.WritingGrades.AnyAsync(g => g.SubmissionId == submissionId));
        var submission = await _db.WritingSubmissions.AsNoTracking().FirstAsync(s => s.Id == submissionId);
        Assert.Equal("failed", submission.Status);
    }

    // -----------------------------------------------------------------
    // Harness
    // -----------------------------------------------------------------

    private async Task<Guid> SeedPracticeSubmissionAsync()
    {
        var id = Guid.NewGuid();
        _db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = id,
            UserId = "learner-1",
            ScenarioId = Guid.NewGuid(),
            Mode = "practice",
            LetterContent = "Dear Dr Smith,\nRe: Mr Jones\n\nI am writing to refer Mr Jones for assessment and ongoing management of his condition.\n\nYours sincerely,\nDoctor",
            LetterContentHash = $"hash-{id:N}",
            WordCount = 220,
            Status = "queued",
            GradingTier = "express",
            InputSource = "typed",
            StartedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
        return id;
    }

    private WritingSubmissionEvaluationPipeline BuildPipeline(IAiGatewayService gateway, bool aiPathReached)
        => new(
            _db,
            gateway,
            // Canon engine + event bus run only after a successful parse.
            canonEngine: aiPathReached ? new EmptyCanonEngine() : null!,
            // Mistake-stat update is wrapped in a fail-soft try/catch in the
            // pipeline, so a null is tolerated on the success path and the
            // failure path never reaches it.
            mistakeService: null!,
            events: aiPathReached ? new NoopWritingEventBus() : null!,
            TimeProvider.System,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options()),
            NullLogger<WritingSubmissionEvaluationPipeline>.Instance);

    private sealed class FakeAiGateway : IAiGatewayService
    {
        private readonly string _completion;
        private readonly string _model;
        private readonly IReadOnlyList<string> _appliedRuleIds;

        public FakeAiGateway(string completion, string model = "claude-sonnet-4-6", IReadOnlyList<string>? appliedRuleIds = null)
        {
            _completion = completion;
            _model = model;
            _appliedRuleIds = appliedRuleIds ?? Array.Empty<string>();
        }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "score",
                Metadata = new AiGroundedPromptMetadata { AppliedRuleIds = _appliedRuleIds },
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
            => Task.FromResult(new AiGatewayResult
            {
                Completion = _completion,
                ResolvedModel = _model,
                AppliedRuleIds = _appliedRuleIds,
                Metadata = new AiGroundedPromptMetadata { AppliedRuleIds = _appliedRuleIds },
            });
    }

    private sealed class EmptyCanonEngine : IWritingCanonEngine
    {
        public Task<WritingCanonDetectionResult> DetectViolationsAsync(WritingCanonDetectionRequest request, CancellationToken ct)
            => Task.FromResult(new WritingCanonDetectionResult(request.SubmissionId, Array.Empty<WritingCanonViolation>()));

        public Task<WritingCanonRuleTestResponse?> TestRuleAsync(string adminUserId, string ruleId, WritingCanonRuleTestRequest request, CancellationToken ct)
            => throw new NotImplementedException();
    }

    private sealed class NoopWritingEventBus : IWritingEventBus
    {
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
            where TEvent : WritingEvent
            => Task.CompletedTask;
    }
}
