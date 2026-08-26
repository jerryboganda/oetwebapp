using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Pipeline-level tests for <see cref="WritingEvaluationPipeline"/>.
///
/// Attempt-based AI Writing grading is disabled by the v1.1 specification
/// enforcement: every legacy queue job must fail closed with
/// <c>writing_v11_required</c> before any deterministic or AI score is
/// produced. These tests pin that gate so a future regression cannot
/// silently re-enable the legacy path.
///
/// Uses SQLite in-memory (per repo memory: EF SQLite catches translation
/// regressions that <c>UseInMemoryDatabase</c> hides).
/// </summary>
public sealed class WritingEvaluationPipelineTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;
    private readonly RulebookLoader _loader = new();
    private readonly WritingRuleEngine _engine;

    public WritingEvaluationPipelineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new LearnerDbContext(options);
        _db.Database.EnsureCreated();
        _engine = new WritingRuleEngine(_loader);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task CompleteEvaluationAsync_FailsClosedWithWritingV11Required()
    {
        var (attempt, evaluation, job) = SeedAttempt(profession: "medicine", country: "UK", letterType: "discharge");
        var gateway = new FakeAiGateway(BuildValidAiJson());
        var pipeline = new WritingEvaluationPipeline(_db, gateway, _engine, NullLogger<WritingEvaluationPipeline>.Instance);

        await pipeline.CompleteEvaluationAsync(job, default);
        await _db.SaveChangesAsync();

        var reloadedAttempt = await _db.Attempts.FirstAsync(x => x.Id == attempt.Id);
        var reloadedEval = await _db.Evaluations.FirstAsync(x => x.Id == evaluation.Id);

        Assert.Equal(AsyncState.Failed, reloadedEval.State);
        Assert.Equal("writing_v11_required", reloadedEval.StatusReasonCode);
        Assert.False(reloadedEval.Retryable);
        Assert.Null(reloadedEval.RetryAfterMs);

        // The attempt must stay Submitted: a blocked job never completes it.
        Assert.Equal(AttemptState.Submitted, reloadedAttempt.State);
        Assert.Null(reloadedAttempt.CompletedAt);

        // No AI call, no deterministic score: the gate runs before anything
        // else in the pipeline.
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public async Task CompleteEvaluationAsync_GateAppliesEvenWhenGatewayWouldSucceed()
    {
        // Even a fully valid AI scoring response must not resurrect legacy
        // grading — the fail-closed check precedes the gateway call entirely.
        var (_, evaluation, job) = SeedAttempt(profession: "nursing", country: null, letterType: "transfer_letter");
        var gateway = new FakeAiGateway(BuildValidAiJson(grade: "A", scaledScore: 420));
        var pipeline = new WritingEvaluationPipeline(_db, gateway, _engine, NullLogger<WritingEvaluationPipeline>.Instance);

        await pipeline.CompleteEvaluationAsync(job, default);
        await _db.SaveChangesAsync();

        var reloadedEval = await _db.Evaluations.FirstAsync(x => x.Id == evaluation.Id);
        Assert.Equal(AsyncState.Failed, reloadedEval.State);
        Assert.Equal("writing_v11_required", reloadedEval.StatusReasonCode);
        Assert.Equal(0, gateway.CallCount);
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private (Attempt attempt, Evaluation evaluation, BackgroundJobItem job) SeedAttempt(
        string profession,
        string? country,
        string letterType)
    {
        var userId = $"u-{Guid.NewGuid():N}";
        var contentId = $"c-{Guid.NewGuid():N}";
        var attemptId = $"a-{Guid.NewGuid():N}";

        _db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = "Test Learner",
            Email = $"{userId}@example.com",
            ActiveProfessionId = profession,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
        });

        if (!string.IsNullOrWhiteSpace(country))
        {
            _db.Set<LearnerGoal>().Add(new LearnerGoal
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ProfessionId = profession,
                TargetCountry = country,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        var attempt = new Attempt
        {
            Id = attemptId,
            UserId = userId,
            ContentId = contentId,
            SubtestCode = "writing",
            Context = JsonSerializer.Serialize(new { letterType }),
            Mode = "practice",
            State = AttemptState.Submitted,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            SubmittedAt = DateTimeOffset.UtcNow,
            DraftContent = BuildSimpleDraft(),
        };
        _db.Attempts.Add(attempt);

        var evaluation = new Evaluation
        {
            Id = $"eval-{Guid.NewGuid():N}",
            AttemptId = attemptId,
            SubtestCode = "writing",
            State = AsyncState.Queued,
            ScoreRange = "pending",
            ModelExplanationSafe = "pending",
            LearnerDisclaimer = "pending",
            LastTransitionAt = DateTimeOffset.UtcNow,
        };
        _db.Evaluations.Add(evaluation);

        var job = new BackgroundJobItem
        {
            Id = $"job-{Guid.NewGuid():N}",
            Type = JobType.WritingEvaluation,
            AttemptId = attemptId,
            State = AsyncState.Processing,
            CreatedAt = DateTimeOffset.UtcNow,
            AvailableAt = DateTimeOffset.UtcNow,
            LastTransitionAt = DateTimeOffset.UtcNow,
        };
        _db.BackgroundJobs.Add(job);

        _db.SaveChanges();
        return (attempt, evaluation, job);
    }

    private static string BuildSimpleDraft() =>
        "12 March 2026\n\nDr Brown\nGeneral Practice\n\nDear Dr Brown,\n\nRe: Mr Smith\n\n" +
        "I am writing to update you on Mr Smith following his discharge today after a routine procedure.\n\n" +
        "Yours sincerely,\nDr Jones\n";

    private static string BuildValidAiJson(string grade = "B", int scaledScore = 360) => JsonSerializer.Serialize(new
    {
        findings = Array.Empty<object>(),
        criteriaScores = new
        {
            purpose = 2,
            content = 5,
            conciseness_clarity = 5,
            genre_style = 5,
            organisation_layout = 5,
            language = 5,
        },
        estimatedScaledScore = scaledScore,
        estimatedGrade = grade,
        advisory = "Grounded scoring contract complete.",
        strengths = new[] { "Clear purpose." },
    });

    // -------------------------------------------------------------------
    // Fake gateway (test double)
    // -------------------------------------------------------------------

    private sealed class FakeAiGateway : IAiGatewayService
    {
        private readonly string _completion;
        public int CallCount { get; private set; }
        public AiGatewayRequest? LastRequest { get; private set; }

        public FakeAiGateway(string completion)
        {
            _completion = completion;
        }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
        {
            return new AiGroundedPrompt
            {
                SystemPrompt = "OET AI — Rulebook-Grounded System Prompt\n(test fixture)\n",
                TaskInstruction = "Score this writing attempt.",
                Metadata = new AiGroundedPromptMetadata
                {
                    RulebookVersion = "test-1.0.0",
                    RulebookKind = context.Kind,
                    Profession = context.Profession,
                    ScoringPassMark = 350,
                    ScoringGrade = "B",
                    AppliedRulesCount = 1,
                    AppliedRuleIds = new[] { "R09.2" },
                },
            };
        }

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(new AiGatewayResult
            {
                Completion = _completion,
                Metadata = request.Prompt!.Metadata,
                RulebookVersion = request.Prompt!.Metadata.RulebookVersion,
                AppliedRuleIds = request.Prompt!.Metadata.AppliedRuleIds,
            });
        }
    }
}
