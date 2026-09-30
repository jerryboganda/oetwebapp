using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Regression guard for the production submit timeout (30 Sep 2026): grading
/// must run OFF the HTTP request path. CreateSubmissionAsync persists the
/// letter and returns immediately (status queued) while grading continues on a
/// detached task with a non-cancellable token — a client timeout/abort must
/// never kill a paid grade mid-flight. RetryGradeAsync behaves the same way.
/// </summary>
public sealed class WritingDetachedGradingTests
{
    private static readonly Guid ScenarioId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
    private static readonly TimeSpan GradeDelay = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task CreateSubmission_ReturnsImmediately_WhileGradingRunsDetached()
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = Guid.NewGuid().ToString("N");
        await using var db = NewDb(databaseName, root);
        var pipeline = new SlowPipeline(db, GradeDelay);
        await using var provider = BuildProvider(databaseName, root, pipeline);
        var service = BuildService(db, pipeline, provider.GetRequiredService<IServiceScopeFactory>());

        var stopwatch = Stopwatch.StartNew();
        var response = await service.CreateSubmissionAsync("learner-1", SampleRequest(), default);
        stopwatch.Stop();

        // The slow grade must NOT block the response; the letter lands queued.
        Assert.True(
            stopwatch.Elapsed < GradeDelay,
            $"submit took {stopwatch.ElapsedMilliseconds}ms — grading is blocking the request path");
        Assert.Equal("queued", response.Status);

        // The detached grade actually starts, on a token a client abort cannot cancel.
        var started = await Task.WhenAny(pipeline.EvaluateStarted.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(pipeline.EvaluateStarted.Task, started);
        Assert.False(pipeline.LastEvaluateToken.CanBeCanceled);
    }

    [Fact]
    public async Task RetryGrade_FailedSubmission_ReturnsQueuedImmediately()
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = Guid.NewGuid().ToString("N");
        await using var db = NewDb(databaseName, root);
        var submissionId = Guid.NewGuid();
        db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = submissionId,
            UserId = "learner-1",
            ScenarioId = ScenarioId,
            Mode = "practice",
            LetterContent = "Dear Dr Smith, I am writing to refer Mr Jones for assessment.",
            LetterContentHash = "hash-detached-retry",
            WordCount = 20,
            Status = WritingSubmissionStatuses.Failed,
            GradingTier = "express",
            InputSource = "typed",
            StartedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var pipeline = new SlowPipeline(db, GradeDelay);
        await using var provider = BuildProvider(databaseName, root, pipeline);
        var service = BuildService(db, pipeline, provider.GetRequiredService<IServiceScopeFactory>());

        var stopwatch = Stopwatch.StartNew();
        var response = await service.RetryGradeAsync("learner-1", submissionId, default);
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < GradeDelay,
            $"retry took {stopwatch.ElapsedMilliseconds}ms — grading is blocking the request path");
        Assert.Equal(submissionId, response.Id);
        Assert.Equal("queued", response.Status);

        var started = await Task.WhenAny(pipeline.EvaluateStarted.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(pipeline.EvaluateStarted.Task, started);
        Assert.False(pipeline.LastEvaluateToken.CanBeCanceled);
    }

    private static ServiceProvider BuildProvider(
        string databaseName, InMemoryDatabaseRoot root, IWritingSubmissionEvaluationPipeline pipeline)
    {
        // Shares the InMemory store with the request context via the explicit
        // root, so the detached scope sees the persisted submission.
        var services = new ServiceCollection();
        services.AddScoped(_ => new LearnerDbContext(Options(databaseName, root)));
        services.AddSingleton(pipeline);
        return services.BuildServiceProvider();
    }

    private static WritingSubmissionService BuildService(
        LearnerDbContext db, IWritingSubmissionEvaluationPipeline pipeline, IServiceScopeFactory scopeFactory)
        => new(
            db,
            pipeline,
            NullLogger<WritingSubmissionService>.Instance,
            new EmptyHighlightStore(),
            scopeFactory: scopeFactory);

    private static DbContextOptions<LearnerDbContext> Options(string databaseName, InMemoryDatabaseRoot root)
        => new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(databaseName, root)
            .Options;

    private static LearnerDbContext NewDb(string databaseName, InMemoryDatabaseRoot root)
    {
        var db = new LearnerDbContext(Options(databaseName, root));
        db.Users.Add(new LearnerUser
        {
            Id = "learner-1",
            DisplayName = "Detached Learner",
            Email = "detached-learner@example.test",
            ActiveProfessionId = "medicine",
            AccountStatus = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
        });
        db.WritingScenarios.Add(new WritingScenario
        {
            Id = ScenarioId,
            Title = "Harness task",
            Profession = "medicine",
            LetterType = "routine_referral",
            Status = "published",
            AuthorId = "admin-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
        return db;
    }

    private static WritingSubmissionCreateRequest SampleRequest() => new(
        ScenarioId: ScenarioId,
        Mode: "practice",
        LetterContent: "Dear Dr Smith, I am writing to refer Mr Jones for assessment and ongoing management.",
        WordCount: 15,
        TimeSpentSeconds: 120,
        InputSource: "typed",
        SimulationMode: null,
        CaseNoteHighlightsJson: null,
        IdempotencyKey: "detached-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Submit seam persists a real queued row; grading takes <c>GradeDelay</c>
    /// so a synchronous (broken) implementation measurably blocks the caller.
    /// </summary>
    private sealed class SlowPipeline(LearnerDbContext db, TimeSpan delay) : IWritingSubmissionEvaluationPipeline
    {
        public TaskCompletionSource EvaluateStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken LastEvaluateToken { get; private set; }

        public Task<Guid> CreateSubmissionAsync(WritingSubmissionGradeContext context, CancellationToken ct)
            => throw new NotSupportedException();

        public async Task<WritingSubmitOutcome> SubmitAsync(WritingSubmitAttempt attempt, CancellationToken ct)
        {
            var id = Guid.NewGuid();
            db.WritingSubmissions.Add(new WritingSubmission
            {
                Id = id,
                UserId = attempt.UserId,
                ScenarioId = attempt.ScenarioId,
                Mode = attempt.Mode,
                LetterContent = attempt.LetterContent ?? string.Empty,
                LetterContentHash = id.ToString("N"),
                WordCount = 50,
                TimeSpentSeconds = attempt.TimeSpentSeconds,
                Status = WritingSubmissionStatuses.Queued,
                GradingTier = attempt.GradingTier,
                InputSource = attempt.InputSource,
                StartedAt = attempt.StartedAt,
                SubmittedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            return new WritingSubmitOutcome(id, true);
        }

        public async Task<WritingSubmissionGradeOutcome> EvaluateAsync(Guid submissionId, CancellationToken ct)
        {
            LastEvaluateToken = ct;
            EvaluateStarted.TrySetResult();
            await Task.Delay(delay);
            return new WritingSubmissionGradeOutcome(submissionId, Guid.NewGuid(), 30, "B", false);
        }
    }

    private sealed class EmptyHighlightStore : IWritingCaseNoteHighlightService
    {
        public Task<string> GetAsync(string userId, Guid scenarioId, CancellationToken ct) => Task.FromResult("{}");
        public Task<string> SaveAsync(string userId, Guid scenarioId, string highlightsJson, CancellationToken ct) => Task.FromResult(highlightsJson);
    }
}
