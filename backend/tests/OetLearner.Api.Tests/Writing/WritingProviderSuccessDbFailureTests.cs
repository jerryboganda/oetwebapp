using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Services.Writing.Configuration;
using OetLearner.Api.Services.Writing.Events;

namespace OetLearner.Api.Tests.Writing;

public sealed class WritingProviderSuccessDbFailureTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;

    public WritingProviderSuccessDbFailureTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task EvaluateAsync_PersistedProviderResult_DoesNotCallProviderAgain()
    {
        var id = Guid.NewGuid();
        _db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = id,
            UserId = "learner-1",
            ScenarioId = Guid.NewGuid(),
            Mode = "practice",
            LetterContent = "Dear Dr Smith, I am writing to refer Mr Jones. Yours sincerely, Doctor",
            LetterContentHash = "abc",
            WordCount = 20,
            Status = "queued",
            GradingTier = "express",
            InputSource = "typed",
            StartedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            ProviderResultJson = """
                {"C1":3,"C2":6,"C3":5,"C4":7,"C5":4,"C6":6,"EstimatedBand":31,"EstimatedScaledScore":380,"PerCriterionFeedbackJson":"{}","TopThreePrioritiesJson":"[]","ConfidenceFlag":"high","ModelUsed":"claude-sonnet-5"}
                """,
        });
        await _db.SaveChangesAsync();

        var gateway = new ThrowingGateway();
        var pipeline = new WritingSubmissionEvaluationPipeline(
            _db,
            gateway,
            new EmptyCanonEngine(),
            mistakeService: null!,
            events: new NoopWritingEventBus(),
            TimeProvider.System,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options()),
            NullLogger<WritingSubmissionEvaluationPipeline>.Instance,
            assessmentPreflight: new PassThroughPreflight());

        var outcome = await pipeline.EvaluateAsync(id, default);

        Assert.Equal(31, outcome.RawTotal);
        Assert.Equal("B", outcome.BandLabel);
        Assert.Equal(0, gateway.Calls);
        Assert.True(await _db.WritingGrades.AnyAsync(g => g.SubmissionId == id));
    }

    private sealed class ThrowingGateway : IAiGatewayService
    {
        public int Calls { get; private set; }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new() { SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**" };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            throw new InvalidOperationException("Provider must not be called when a result is already persisted.");
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
                true, WritingAssessmentV11Status.CandidateReady,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                "medicine", "routine_referral", "test", "task", "notes"));
    }
}
