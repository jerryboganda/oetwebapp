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

public sealed class WritingGradeReuseKeyTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;

    public WritingGradeReuseKeyTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        // Grading resolves profession/letter-type from the owning scenario
        // (fail-closed when absent), so the harness seeds a real scenario row.
        _db.WritingScenarios.Add(new WritingScenario
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Title = "Harness task",
            Profession = "medicine",
            LetterType = "routine_referral",
            Status = "published",
            AuthorId = "admin-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task EvaluateAsync_SameReuseIdentity_ReusesGradeWithinTtl()
    {
        // Double-tap contract: two sends of the same logical attempt carry
        // different random client keys but identical content — they collapse
        // into ONE submission row and ONE provider grading workflow.
        const string letter = "Dear Dr Smith,\nRe: Mr Jones\n\nI am writing to refer Mr Jones for review.\n\nYours sincerely,\nDoctor";
        var gateway = new CountingGateway(CanonicalCompletion);
        var pipeline = BuildPipeline(gateway);

        var firstId = await pipeline.CreateSubmissionAsync(Context("k1", letter), default);
        var first = await pipeline.EvaluateAsync(firstId, default);

        var secondId = await pipeline.CreateSubmissionAsync(Context("k2", letter), default);
        var second = await pipeline.EvaluateAsync(secondId, default);

        Assert.Equal(firstId, secondId);
        Assert.True(second.IdempotentReuse);
        Assert.Equal(first.RawTotal, second.RawTotal);
        Assert.Equal(first.GradeId, second.GradeId);
        Assert.Equal(1, gateway.Calls);
        Assert.Equal(1, await _db.WritingSubmissions.CountAsync());
        Assert.Equal(1, await _db.WritingGrades.CountAsync());
    }

    [Fact]
    public async Task EvaluateAsync_DifferentContent_CreatesSeparateSubmission()
    {
        // The dedupe guard must not swallow a legitimate new attempt with
        // different content: it creates its own submission row.
        var gateway = new CountingGateway(CanonicalCompletion);
        var pipeline = BuildPipeline(gateway);

        var firstId = await pipeline.CreateSubmissionAsync(
            Context("k1", "Dear Dr Smith,\nRe: Mr Jones\n\nFirst letter.\n\nYours sincerely,\nDoctor"), default);
        await pipeline.EvaluateAsync(firstId, default);

        var secondId = await pipeline.CreateSubmissionAsync(
            Context("k2", "Dear Dr Smith,\nRe: Mr Jones\n\nSecond letter with different content.\n\nYours sincerely,\nDoctor"), default);

        Assert.NotEqual(firstId, secondId);
        Assert.Equal(2, await _db.WritingSubmissions.CountAsync());
    }

    private WritingSubmissionEvaluationPipeline BuildPipeline(IAiGatewayService gateway)
        => new(
            _db,
            gateway,
            new EmptyCanonEngine(),
            mistakeService: null!,
            events: new NoopWritingEventBus(),
            TimeProvider.System,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options()),
            NullLogger<WritingSubmissionEvaluationPipeline>.Instance,
            assessmentPreflight: new PassThroughPreflight());

    private static WritingSubmissionGradeContext Context(string key, string letter)
        => new("learner-1", Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), "practice", "express", "typed",
            letter, 30, DateTimeOffset.UtcNow.AddMinutes(-4), false, null, key);

    private const string CanonicalCompletion = """
        {
          "findings": [],
          "criteriaScores": { "purpose": 3, "content": 6, "conciseness_clarity": 5, "genre_style": 7, "organisation_layout": 4, "language": 6 },
          "estimatedScaledScore": 380,
          "estimatedGrade": "B"
        }
        """;

    private sealed class CountingGateway(string completion) : IAiGatewayService
    {
        public int Calls { get; private set; }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new() { SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**" };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new AiGatewayResult { Completion = completion, ResolvedModel = "claude-sonnet-5" });
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
