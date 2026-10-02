using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Services.Writing.Configuration;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// WAI-05 — the QA-only per-learner fault switch: off for everyone unless an admin flags one
/// learner; a faulted hop fails before any provider call; the learner's failed run is a real
/// <c>failed</c> row (no auto re-queue) whose Retry grades even with the flag still on.
/// </summary>
public sealed class WritingQaFaultTests : IAsyncDisposable
{
    private const string QaLearner = "qa-learner";
    private const string OtherLearner = "real-learner";
    private static readonly Guid ScenarioId = Guid.Parse("c0ffee00-0000-4000-8000-0000000000e5");

    private const string CanonicalCompletion = """
        {
          "findings": [],
          "criteriaScores": { "purpose": 3, "content": 6, "conciseness_clarity": 5, "genre_style": 7, "organisation_layout": 4, "language": 6 },
          "estimatedScaledScore": 380,
          "estimatedGrade": "B"
        }
        """;

    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;

    public WritingQaFaultTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.WritingScenarios.Add(new WritingScenario
        {
            Id = ScenarioId,
            Title = "QA fault task",
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
    public async Task NoFlag_IsANoOp_TheRunStartsOnMax()
    {
        var gateway = new RecordingGateway();
        var id = await SeedAsync(QaLearner);

        await Pipeline(gateway).EvaluateAsync(id, default);

        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(id));
        Assert.Equal(new[] { WritingSubscriptionProviders.Claude }, gateway.Providers);
        Assert.Null(await Fault().ReadAsync(QaLearner, default));
    }

    [Fact]
    public async Task AFlagForOneLearner_NeverAffectsAnother()
    {
        await FlagAsync(WritingQaFault.AllHopsKey(QaLearner));
        var gateway = new RecordingGateway();
        var id = await SeedAsync(OtherLearner);

        await Pipeline(gateway).EvaluateAsync(id, default);

        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(id));
        Assert.Equal(new[] { WritingSubscriptionProviders.Claude }, gateway.Providers);
        Assert.Null(await Fault().ReadAsync(OtherLearner, default));
    }

    [Fact]
    public async Task L1L2Fault_MaxAndApiFailBeforeAnyCall_AndCodexServesForReal()
    {
        await FlagAsync(WritingQaFault.L1L2Key(QaLearner));
        var gateway = new RecordingGateway();
        var id = await SeedAsync(QaLearner);

        await Pipeline(gateway).EvaluateAsync(id, default);

        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(id));
        Assert.Equal(new[] { WritingSubscriptionProviders.Codex }, gateway.Providers);
    }

    [Fact]
    public async Task AllHopsFault_LeavesARealFailedRow_AndTheRetryGradesWithTheFlagStillOn()
    {
        await FlagAsync(WritingQaFault.AllHopsKey(QaLearner));
        var gateway = new RecordingGateway();
        var pipeline = Pipeline(gateway);
        var id = await SeedAsync(QaLearner);

        await Assert.ThrowsAsync<ApiException>(() => pipeline.EvaluateAsync(id, default));

        var failed = await _db.WritingSubmissions.AsNoTracking().SingleAsync(s => s.Id == id);
        Assert.Equal(WritingSubmissionStatuses.Failed, failed.Status); // no automatic re-queue for a QA learner
        Assert.Equal(0, failed.AutoRetryCount);
        Assert.True(WritingV2ResponseMapper.ToSubmissionResponse(failed).CanRetry);
        Assert.Empty(gateway.Providers); // no provider call, so no usage row and no circuit/marker effect

        await new WritingSubmissionService(_db, pipeline, NullLogger<WritingSubmissionService>.Instance, new EmptyHighlightStore())
            .RetryGradeAsync(QaLearner, id, default);

        Assert.Equal(WritingSubmissionStatuses.Graded, await StatusAsync(id));
        Assert.Equal(new[] { WritingSubscriptionProviders.Claude }, gateway.Providers);
    }

    [Fact]
    public async Task AStaleOrDisabledFlag_IsIgnored()
    {
        await FlagAsync(WritingQaFault.AllHopsKey(QaLearner), updatedAt: DateTimeOffset.UtcNow.AddHours(-25));
        await FlagAsync(WritingQaFault.L1L2Key(QaLearner), enabled: false);

        Assert.Null(await Fault().ReadAsync(QaLearner, default));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(3, 3)]
    [InlineData(99, WritingQaFault.MaxRuns)]
    public async Task RolloutPercentage_IsTheNumberOfFailedRuns_ZeroMeansOne_CappedAtFive(int rollout, int runs)
    {
        await FlagAsync(WritingQaFault.L1L2Key(QaLearner), rollout: rollout);

        var fault = await Fault().ReadAsync(QaLearner, default);

        Assert.Equal(new WritingQaFault.Fault(AllHops: false, Runs: runs), fault);
        Assert.True(fault!.ShouldFailHop(WritingGradeHop.ClaudeMax, runs));
        Assert.False(fault.ShouldFailHop(WritingGradeHop.ClaudeMax, runs + 1));
        Assert.False(fault.ShouldFailHop(WritingGradeHop.Codex, 1));
    }

    [Fact]
    public async Task AnUnreadableFlag_FailsClosed()
    {
        await FlagAsync(WritingQaFault.AllHopsKey(QaLearner));
        var broken = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options);
        await broken.DisposeAsync();

        var fault = await new WritingQaFault(broken, TimeProvider.System, NullLogger<WritingQaFault>.Instance)
            .ReadAsync(QaLearner, default);

        Assert.Null(fault);
    }

    // ── Harness ────────────────────────────────────────────────────────────────

    private WritingQaFault Fault() => new(_db, TimeProvider.System, NullLogger<WritingQaFault>.Instance);

    private WritingSubmissionEvaluationPipeline Pipeline(IAiGatewayService gateway)
        => new(
            _db,
            gateway,
            new EmptyCanonEngine(),
            mistakeService: null!,
            events: new NoopWritingEventBus(),
            TimeProvider.System,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options()),
            NullLogger<WritingSubmissionEvaluationPipeline>.Instance,
            subscriptionSelector: new WritingSubscriptionSelector(),
            assessmentPreflight: new PassThroughPreflight(),
            qaFault: Fault());

    private async Task FlagAsync(string key, bool enabled = true, int rollout = 1, DateTimeOffset? updatedAt = null)
    {
        _db.FeatureFlags.Add(new FeatureFlag
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = key,
            Key = key,
            Enabled = enabled,
            RolloutPercentage = rollout,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    private async Task<Guid> SeedAsync(string userId)
    {
        var id = Guid.NewGuid();
        _db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = id,
            UserId = userId,
            ScenarioId = ScenarioId,
            Mode = "practice",
            LetterContent = $"Dear Dr Green,\n\nI am writing to refer Mr Lee ({userId}).\n\nYours sincerely,\nDoctor",
            LetterContentHash = $"hash-{id:N}",
            WordCount = 14,
            Status = WritingSubmissionStatuses.Queued,
            GradingTier = "express",
            InputSource = "typed",
            StartedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
        return id;
    }

    private async Task<string> StatusAsync(Guid id)
        => (await _db.WritingSubmissions.AsNoTracking().SingleAsync(s => s.Id == id)).Status;

    private sealed class RecordingGateway : IAiGatewayService
    {
        public List<string> Providers { get; } = new();

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "score",
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Providers.Add(request.Provider);
            return Task.FromResult(new AiGatewayResult { Completion = CanonicalCompletion, ResolvedModel = request.Model });
        }
    }

    private sealed class PassThroughPreflight : IWritingAssessmentPreflightService
    {
        public Task<WritingAssessmentPreflightResult> ValidateAsync(WritingSubmission submission, CancellationToken ct)
            => Task.FromResult(new WritingAssessmentPreflightResult(
                true, WritingAssessmentV11Status.CandidateReady,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                "medicine", "routine_referral", "test", "task", "Patient name: Adam Lee\nAge: 54"));
    }

    private sealed class EmptyHighlightStore : IWritingCaseNoteHighlightService
    {
        public Task<string> GetAsync(string userId, Guid scenarioId, CancellationToken ct) => Task.FromResult("{}");
        public Task<string> SaveAsync(string userId, Guid scenarioId, string highlightsJson, CancellationToken ct) => Task.FromResult(highlightsJson);
    }
}
