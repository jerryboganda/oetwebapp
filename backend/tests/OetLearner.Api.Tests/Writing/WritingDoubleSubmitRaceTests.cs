using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Services.Writing.Configuration;
using OetLearner.Api.Services.Writing.Events;

namespace OetLearner.Api.Tests.Writing;

public sealed class WritingDoubleSubmitRaceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;

    public WritingDoubleSubmitRaceTests()
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
    public async Task CreateSubmissionAsync_SameIdempotencyKey_ReturnsSingleRow()
    {
        var pipeline = BuildPipeline(new CountingGateway(CanonicalCompletion), credits: new CountingReservations());
        var context = SampleContext("key-1");

        var first = await pipeline.CreateSubmissionAsync(context, default);
        var second = await pipeline.CreateSubmissionAsync(context, default);

        Assert.Equal(first, second);
        Assert.Equal(1, await _db.WritingSubmissions.CountAsync());
    }

    [Fact]
    public async Task EvaluateAsync_SecondClaim_DoesNotOpenSecondProviderCall()
    {
        var gateway = new CountingGateway(CanonicalCompletion);
        var credits = new CountingReservations();
        var pipeline = BuildPipeline(gateway, credits);
        var id = await pipeline.CreateSubmissionAsync(SampleContext("eval-1"), default);

        var first = await pipeline.EvaluateAsync(id, default);
        var second = await pipeline.EvaluateAsync(id, default);

        Assert.Equal(first.GradeId, second.GradeId);
        Assert.Equal(1, gateway.Calls);
        Assert.Equal(1, credits.ReserveCalls);
        Assert.Equal(1, credits.CommitCalls);
        Assert.Equal(0, credits.ReleaseCalls);
        Assert.Equal(1, await _db.WritingGrades.CountAsync());
    }

    private WritingSubmissionEvaluationPipeline BuildPipeline(IAiGatewayService gateway, CountingReservations credits)
        => new(
            _db,
            gateway,
            new EmptyCanonEngine(),
            mistakeService: null!,
            events: new NoopWritingEventBus(),
            TimeProvider.System,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options()),
            NullLogger<WritingSubmissionEvaluationPipeline>.Instance,
            assessmentPreflight: new PassThroughPreflight(),
            creditReservations: credits);

    private static WritingSubmissionGradeContext SampleContext(string key)
        => new(
            UserId: "learner-1",
            ScenarioId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Mode: "practice",
            GradingTier: "express",
            InputSource: "typed",
            LetterContent: "Dear Dr Smith,\nRe: Mr Jones\n\nI am writing to refer Mr Jones.\n\nYours sincerely,\nDoctor",
            TimeSpentSeconds: 40,
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            IsRevision: false,
            OriginalSubmissionId: null,
            IdempotencyKey: key);

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
            => new()
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "score",
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new AiGatewayResult
            {
                Completion = completion,
                ResolvedModel = "claude-sonnet-5",
            });
        }
    }

    private sealed class CountingReservations : IAiCreditReservationService
    {
        public int ReserveCalls { get; private set; }
        public int CommitCalls { get; private set; }
        public int ReleaseCalls { get; private set; }
        public string LastId { get; } = "res-1";

        public Task<AiCreditReservationTicket> ReserveWritingAsync(
            string userId, string operationId, string businessReference, CancellationToken ct)
        {
            ReserveCalls++;
            return Task.FromResult(new AiCreditReservationTicket(
                LastId, operationId, "writing", 1, AiCreditReservationState.Reserved, ReserveCalls > 1));
        }

        public Task<AiCreditReservationTicket> ReserveSpeakingAsync(
            string userId, string operationId, string businessReference, CancellationToken ct)
            => ReserveWritingAsync(userId, operationId, businessReference, ct);

        public Task CommitAsync(string reservationId, CancellationToken ct)
        {
            CommitCalls++;
            return Task.CompletedTask;
        }

        public Task CommitByBusinessReferenceAsync(string businessReference, CancellationToken ct)
            => CommitAsync(LastId, ct);

        public Task ReleaseAsync(string reservationId, CancellationToken ct)
        {
            ReleaseCalls++;
            return Task.CompletedTask;
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
                "medicine", "routine_referral", "test", "task", "Patient name: John Jones\nAge: 54"));
    }
}
