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
        var gateway = new FakeAiGateway(completion, "claude-sonnet-5", new[] { "R03.4" });
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
        // EstimatedBand is stored in raw-total units (0..38, analytics only).
        // BandLabel is the candidate-facing grade letter and is derived from
        // the canonical 0-500 estimatedScaledScore (380 here) via
        // OetScoring.OetGradeLetterFromScaled — never from the raw total.
        Assert.Equal(31, grade.EstimatedBand);
        Assert.Equal("B", grade.BandLabel);
        Assert.Equal("high", grade.ConfidenceFlag);
        // Honest model provenance — not the prompt-template id.
        Assert.Equal("claude-sonnet-5", grade.ModelUsed);
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

    [Fact]
    public async Task EvaluateAsync_BlankLetter_GradesZeroWithNoProviderCall()
    {
        var id = Guid.NewGuid();
        _db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = id,
            UserId = "learner-1",
            ScenarioId = Guid.NewGuid(),
            Mode = "practice",
            LetterContent = "   ",
            LetterContentHash = $"hash-{id:N}",
            WordCount = 0,
            Status = "queued",
            GradingTier = "express",
            InputSource = "typed",
            StartedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var gateway = new CountingGateway();
        var pipeline = BuildPipeline(gateway, aiPathReached: true);

        var outcome = await pipeline.EvaluateAsync(id, default);

        var grade = await _db.WritingGrades.AsNoTracking().SingleAsync(g => g.SubmissionId == id);
        Assert.Equal(0, grade.RawTotal);
        Assert.Equal(0, grade.C1Purpose);
        Assert.Equal(0, grade.C6Language);
        Assert.Equal("E", grade.BandLabel);
        Assert.Equal("deterministic-empty-v1", grade.ModelUsed);
        Assert.Equal("graded", (await _db.WritingSubmissions.AsNoTracking().FirstAsync(s => s.Id == id)).Status);
        Assert.False(outcome.IdempotentReuse);
        // No provider call and therefore no grading charge for blank content.
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task CreateSubmissionAsync_DifferentKeysSameContent_ReturnsSingleRow()
    {
        // Double-tap guard: two rapid sends of the same logical attempt carry
        // different random client keys but identical content — they must
        // collapse into ONE submission row (and therefore ONE grading job).
        var gateway = new CountingGateway();
        var pipeline = BuildPipeline(gateway, aiPathReached: true);
        var scenarioId = Guid.NewGuid();
        const string letter = "Dear Dr Smith,\nRe: Mr Jones\n\nI am writing to refer Mr Jones.\n\nYours sincerely,\nDoctor";

        var first = await pipeline.CreateSubmissionAsync(new WritingSubmissionGradeContext(
            "learner-1", scenarioId, "practice", "express", "typed", letter, 40,
            DateTimeOffset.UtcNow.AddMinutes(-5), false, null, "random-key-1"), default);
        var second = await pipeline.CreateSubmissionAsync(new WritingSubmissionGradeContext(
            "learner-1", scenarioId, "practice", "express", "typed", letter, 40,
            DateTimeOffset.UtcNow.AddMinutes(-5), false, null, "random-key-2"), default);

        Assert.Equal(first, second);
        Assert.Equal(1, await _db.WritingSubmissions.CountAsync());
    }

    [Fact]
    public async Task EvaluateAsync_GradingInput_NeverContainsModelAnswerText()
    {
        // Separation proof: even when an approved pre-generated Model Answer
        // exists for the task, the rubric prompt is built ONLY from canonical
        // case notes + exact task + candidate letter. No similarity,
        // phrase-match or embedding comparison against the exemplar exists in
        // the grading path — the answer is display-only.
        const string canary = "CANARY-MODEL-ANSWER-PHRASE-ZEBRA-42";
        var scenarioId = Guid.NewGuid();
        _db.WritingScenarios.Add(new WritingScenario
        {
            Id = scenarioId,
            Title = "Separation fixture",
            Profession = "medicine",
            LetterType = "LT-RR",
            TaskPromptMarkdown = "Write a routine referral.",
            Status = "published",
            AuthorId = "admin-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.WritingTaskModelAnswers.Add(new WritingTaskModelAnswer
        {
            Id = Guid.NewGuid(),
            ScenarioId = scenarioId,
            Status = WritingAssessmentModelAnswerStatus.Ready,
            IsCandidateVisible = true,
            ModelAnswerText = $"Dear Doctor, {canary} kindly review this patient. Yours sincerely, Doctor",
            // Verified under the running validator, i.e. exactly the answer a
            // candidate result would display — and still never a grading input.
            ValidatorVersion = WritingRuleEngine.ValidatorVersion,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        var submissionId = Guid.NewGuid();
        _db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = submissionId,
            UserId = "learner-1",
            ScenarioId = scenarioId,
            Mode = "practice",
            LetterContent = "Dear Dr Smith,\nRe: Mr Jones\n\nI am writing to refer Mr Jones for assessment.\n\nYours sincerely,\nDoctor",
            LetterContentHash = $"hash-{submissionId:N}",
            WordCount = 120,
            Status = "queued",
            GradingTier = "express",
            InputSource = "typed",
            StartedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        const string completion = """
            {
              "findings": [],
              "criteriaScores": { "purpose": 2, "content": 5, "conciseness_clarity": 5, "genre_style": 5, "organisation_layout": 5, "language": 5 },
              "estimatedScaledScore": 340,
              "estimatedGrade": "C+"
            }
            """;
        var gateway = new CapturingGateway(completion);
        var pipeline = BuildPipeline(gateway, aiPathReached: true);

        await pipeline.EvaluateAsync(submissionId, default);

        Assert.NotNull(gateway.LastUserInput);
        Assert.DoesNotContain(canary, gateway.LastUserInput);
    }

    [Fact]
    public async Task CreateSubmissionAsync_EmptyLetter_IsAcceptedForGrading()
    {
        var pipeline = BuildPipeline(new CountingGateway(), aiPathReached: true);

        var id = await pipeline.CreateSubmissionAsync(new WritingSubmissionGradeContext(
            "learner-1", Guid.NewGuid(), "practice", "express", "typed", string.Empty, 0,
            DateTimeOffset.UtcNow, false, null, "empty-key-1"), default);

        Assert.NotEqual(Guid.Empty, id);
    }

    // -----------------------------------------------------------------
    // Harness
    // -----------------------------------------------------------------

    private async Task<Guid> SeedPracticeSubmissionAsync()
    {
        // Grading resolves profession/letter-type from the owning scenario
        // (fail-closed when absent), so the harness seeds a real scenario row.
        var scenarioId = Guid.NewGuid();
        _db.WritingScenarios.Add(new WritingScenario
        {
            Id = scenarioId,
            Title = "Harness task",
            Profession = "medicine",
            LetterType = "routine_referral",
            Status = "published",
            AuthorId = "admin-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        var id = Guid.NewGuid();
        _db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = id,
            UserId = "learner-1",
            ScenarioId = scenarioId,
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
            NullLogger<WritingSubmissionEvaluationPipeline>.Instance,
            assessmentPreflight: new PassThroughPreflight());

    private sealed class FakeAiGateway : IAiGatewayService
    {
        private readonly string _completion;
        private readonly string _model;
        private readonly IReadOnlyList<string> _appliedRuleIds;

        public FakeAiGateway(string completion, string model = "claude-sonnet-5", IReadOnlyList<string>? appliedRuleIds = null)
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

    private sealed class CapturingGateway(string completion) : IAiGatewayService
    {
        public string? LastUserInput { get; private set; }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "score",
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            LastUserInput = request.UserInput;
            return Task.FromResult(new AiGatewayResult { Completion = completion });
        }
    }

    private sealed class CountingGateway : IAiGatewayService
    {
        public int Calls { get; private set; }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "score",
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new AiGatewayResult { Completion = "{}" });
        }
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

    private sealed class PassThroughPreflight : IWritingAssessmentPreflightService
    {
        public Task<WritingAssessmentPreflightResult> ValidateAsync(WritingSubmission submission, CancellationToken ct)
            => Task.FromResult(new WritingAssessmentPreflightResult(
                CanScore: true,
                Status: WritingAssessmentV11Status.CandidateReady,
                MissingInputCodes: Array.Empty<string>(),
                ReleaseBlockCodes: Array.Empty<string>(),
                AppliedRulePacks: Array.Empty<string>(),
                Profession: "medicine",
                LetterType: "routine_referral",
                RulePackVersion: "test",
                TaskSnapshot: "Refer the patient.",
                CaseNotesSnapshot: "Patient name: John Jones\nAge: 54"));
    }
}
