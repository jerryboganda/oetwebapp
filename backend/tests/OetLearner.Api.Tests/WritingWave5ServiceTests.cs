using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Services.Writing.Events;

namespace OetLearner.Api.Tests;

public class WritingWave5ServiceTests
{
    private const string UserId = "learner-writing-wave5";
    private const string OtherUserId = "other-writing-wave5";

    [Fact]
    public async Task GetLetterTypesAsync_ScopesRowsToCurrentLearner()
    {
        var db = BuildDb();
        var clock = new FixedClock();
        var scenarioId = Guid.NewGuid();
        db.WritingScenarios.Add(Scenario(scenarioId, "LT-RR"));
        var mine = Submission(Guid.NewGuid(), UserId, scenarioId, clock.GetUtcNow().AddDays(-1));
        var theirs = Submission(Guid.NewGuid(), OtherUserId, scenarioId, clock.GetUtcNow().AddDays(-1));
        db.WritingSubmissions.AddRange(mine, theirs);
        db.WritingGrades.AddRange(Grade(Guid.NewGuid(), mine.Id, 32, clock.GetUtcNow()), Grade(Guid.NewGuid(), theirs.Id, 18, clock.GetUtcNow()));
        await db.SaveChangesAsync();
        var service = new WritingAnalyticsServiceV2(db, clock);

        var result = await service.GetLetterTypesAsync(UserId, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal("LT-RR", row.LetterType);
        Assert.Equal(1, row.Attempts);
        Assert.Equal(32, row.AverageBand);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task BeginMockWritingAsync_PersistsPhaseBoundary()
    {
        var db = BuildDb();
        var clock = new FixedClock();
        var mockId = await SeedPublishedMockAsync(db, clock);
        var service = BuildMockService(db, clock, new StubSubmissionPipeline());
        var started = await service.StartMockAsync(UserId, new WritingMockStartRequest(mockId), CancellationToken.None);
        await MoveSessionToWritingWindowAsync(db, started.Id, clock);

        var updated = await service.BeginMockWritingAsync(UserId, started.Id, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("writing", updated.Status);
        Assert.NotNull(updated.ReadingPhaseEndedAt);
        Assert.Equal(0, updated.ReadingSecondsRemaining);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task BeginMockWritingAsync_RejectsEarlyPhaseChange()
    {
        var db = BuildDb();
        var clock = new FixedClock();
        var mockId = await SeedPublishedMockAsync(db, clock);
        var service = BuildMockService(db, clock, new StubSubmissionPipeline());
        var started = await service.StartMockAsync(UserId, new WritingMockStartRequest(mockId), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.BeginMockWritingAsync(UserId, started.Id, CancellationToken.None));

        Assert.Equal("writing_mock_reading_window_active", ex.ErrorCode);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task SubmitMockAsync_RejectsSubmissionBeforeWritingPhase()
    {
        var db = BuildDb();
        var clock = new FixedClock();
        var mockId = await SeedPublishedMockAsync(db, clock);
        var pipeline = new StubSubmissionPipeline();
        var service = BuildMockService(db, clock, pipeline);
        var started = await service.StartMockAsync(UserId, new WritingMockStartRequest(mockId), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.SubmitMockAsync(
            UserId,
            started.Id,
            new WritingMockSubmitRequest("Dear Doctor, this is a mock letter.", 7, 60),
            CancellationToken.None));

        Assert.Equal("writing_mock_not_in_writing_phase", ex.ErrorCode);
        Assert.Equal(0, pipeline.CreateCalls);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task SubmitMockAsync_RejectsShortSubmissionBeforeCreatingSubmission()
    {
        var db = BuildDb();
        var clock = new FixedClock();
        var mockId = await SeedPublishedMockAsync(db, clock);
        var pipeline = new StubSubmissionPipeline(db);
        var service = BuildMockService(db, clock, pipeline);
        var started = await service.StartMockAsync(UserId, new WritingMockStartRequest(mockId), CancellationToken.None);
        await MoveSessionToWritingWindowAsync(db, started.Id, clock);
        await service.BeginMockWritingAsync(UserId, started.Id, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.SubmitMockAsync(
            UserId,
            started.Id,
            new WritingMockSubmitRequest("Dear Doctor, this is too short.", 6, 60),
            CancellationToken.None));

        Assert.Equal("writing_mock_word_count_too_low", ex.ErrorCode);
        Assert.Equal(0, pipeline.CreateCalls);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task SubmitMockAsync_ReturnsExistingSubmittedResultWithoutCreatingDuplicate()
    {
        var db = BuildDb();
        var clock = new FixedClock();
        var mockId = await SeedPublishedMockAsync(db, clock);
        var pipeline = new StubSubmissionPipeline(db);
        var service = BuildMockService(db, clock, pipeline);
        var started = await service.StartMockAsync(UserId, new WritingMockStartRequest(mockId), CancellationToken.None);
        await MoveSessionToWritingWindowAsync(db, started.Id, clock);
        await service.BeginMockWritingAsync(UserId, started.Id, CancellationToken.None);
        var request = new WritingMockSubmitRequest(new string('w', 500), 120, 120);

        var first = await service.SubmitMockAsync(UserId, started.Id, request, CancellationToken.None);
        var second = await service.SubmitMockAsync(UserId, started.Id, request, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, pipeline.CreateCalls);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task GetMockResultsAsync_ReturnsGradeWhenPipelineReusesExistingGrade()
    {
        var db = BuildDb();
        var clock = new FixedClock();
        var mockId = await SeedPublishedMockAsync(db, clock);
        var scenarioId = await db.WritingMocks.AsNoTracking().Where(m => m.Id == mockId).Select(m => m.ScenarioId).SingleAsync();
        var priorSubmission = Submission(Guid.NewGuid(), UserId, scenarioId, clock.GetUtcNow().AddMinutes(-10));
        var reusedGradeId = Guid.NewGuid();
        db.WritingSubmissions.Add(priorSubmission);
        db.WritingGrades.Add(Grade(reusedGradeId, priorSubmission.Id, 31, clock.GetUtcNow().AddMinutes(-5)));
        await db.SaveChangesAsync();
        var pipeline = new StubSubmissionPipeline(db, reusedGradeId);
        var service = BuildMockService(db, clock, pipeline);
        var started = await service.StartMockAsync(UserId, new WritingMockStartRequest(mockId), CancellationToken.None);
        await MoveSessionToWritingWindowAsync(db, started.Id, clock);
        await service.BeginMockWritingAsync(UserId, started.Id, CancellationToken.None);
        var request = new WritingMockSubmitRequest(new string('w', 500), 120, 120);

        await service.SubmitMockAsync(UserId, started.Id, request, CancellationToken.None);
        var results = await service.GetMockResultsAsync(UserId, started.Id, CancellationToken.None);

        Assert.NotNull(results);
        Assert.Equal(31, results.Grade.RawTotal);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task GetBandsAsync_ReturnsRawTargetForChartAxis()
    {
        var db = BuildDb();
        var clock = new FixedClock();
        var scenarioId = Guid.NewGuid();
        db.LearnerWritingProfiles.Add(new LearnerWritingProfile
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            TargetBand = "B+",
            UpdatedAt = clock.GetUtcNow(),
        });
        db.WritingScenarios.Add(Scenario(scenarioId, "LT-RR"));
        var mine = Submission(Guid.NewGuid(), UserId, scenarioId, clock.GetUtcNow().AddDays(-1));
        db.WritingSubmissions.Add(mine);
        db.WritingGrades.Add(Grade(Guid.NewGuid(), mine.Id, 32, clock.GetUtcNow()));
        await db.SaveChangesAsync();
        var service = new WritingAnalyticsServiceV2(db, clock);

        var result = await service.GetBandsAsync(UserId, CancellationToken.None);

        Assert.Equal(34, result.TargetBand);

        await db.DisposeAsync();
    }

    private static LearnerDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static WritingMockService BuildMockService(LearnerDbContext db, TimeProvider clock, StubSubmissionPipeline pipeline)
        => new(db, clock, new NoopWritingEventBus(), pipeline, NullLogger<WritingMockService>.Instance);

    private static async Task MoveSessionToWritingWindowAsync(LearnerDbContext db, Guid sessionId, TimeProvider clock)
    {
        var session = await db.WritingMockSessions.SingleAsync(s => s.Id == sessionId);
        session.StartedAt = clock.GetUtcNow().AddMinutes(-5);
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedPublishedMockAsync(LearnerDbContext db, TimeProvider clock)
    {
        var scenarioId = Guid.NewGuid();
        var mockId = Guid.NewGuid();
        db.WritingScenarios.Add(Scenario(scenarioId, "LT-RR"));
        db.WritingMocks.Add(new WritingMock
        {
            Id = mockId,
            ScenarioId = scenarioId,
            Title = "Mock A",
            Difficulty = 4,
            Status = "published",
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync();
        return mockId;
    }

    private static WritingScenario Scenario(Guid id, string letterType)
        => new()
        {
            Id = id,
            Title = "Referral scenario",
            LetterType = letterType,
            Profession = "medicine",
            Difficulty = 3,
            CaseNotesMarkdown = "Patient requires referral.",
            IsDiagnostic = false,
            Status = "published",
            AuthorId = "admin",
            PublishedAt = new DateTimeOffset(2026, 5, 27, 8, 0, 0, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(2026, 5, 27, 8, 0, 0, TimeSpan.Zero),
        };

    private static WritingSubmission Submission(Guid id, string userId, Guid scenarioId, DateTimeOffset submittedAt)
        => new()
        {
            Id = id,
            UserId = userId,
            ScenarioId = scenarioId,
            Mode = "practice",
            LetterContent = "Dear Doctor, this is a writing submission.",
            LetterContentHash = id.ToString("N"),
            WordCount = 180,
            TimeSpentSeconds = 2100,
            StartedAt = submittedAt.AddMinutes(-35),
            SubmittedAt = submittedAt,
            Status = "graded",
            GradingTier = "express",
            InputSource = "typed",
            CreatedAt = submittedAt,
        };

    private static WritingGrade Grade(Guid id, Guid submissionId, short rawTotal, DateTimeOffset gradedAt)
        => new()
        {
            Id = id,
            SubmissionId = submissionId,
            C1Purpose = 3,
            C2Content = 6,
            C3Conciseness = 5,
            C4Genre = 6,
            C5Organisation = 6,
            C6Language = 6,
            RawTotal = rawTotal,
            EstimatedBand = 350,
            BandLabel = "B",
            ModelUsed = "test",
            CanonVersion = "v1",
            GradedAt = gradedAt,
            CreatedAt = gradedAt,
        };

    private sealed class StubSubmissionPipeline : IWritingSubmissionEvaluationPipeline
    {
        private readonly LearnerDbContext? db;
        private readonly Guid? reusedGradeId;

        public StubSubmissionPipeline(LearnerDbContext? db = null, Guid? reusedGradeId = null)
        {
            this.db = db;
            this.reusedGradeId = reusedGradeId;
        }

        public int CreateCalls { get; private set; }

        public async Task<Guid> CreateSubmissionAsync(WritingSubmissionGradeContext context, CancellationToken ct)
        {
            CreateCalls++;
            var id = Guid.NewGuid();
            if (db is not null)
            {
                db.WritingSubmissions.Add(new WritingSubmission
                {
                    Id = id,
                    UserId = context.UserId,
                    ScenarioId = context.ScenarioId,
                    Mode = context.Mode,
                    LetterContent = context.LetterContent,
                    LetterContentHash = id.ToString("N"),
                    WordCount = context.LetterContent.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
                    TimeSpentSeconds = context.TimeSpentSeconds,
                    StartedAt = context.StartedAt,
                    SubmittedAt = context.StartedAt.AddSeconds(context.TimeSpentSeconds),
                    Status = "submitted",
                    GradingTier = context.GradingTier,
                    InputSource = context.InputSource,
                    CreatedAt = context.StartedAt,
                });
                await db.SaveChangesAsync(ct);
            }
            return id;
        }

        public async Task<WritingSubmissionGradeOutcome> EvaluateAsync(Guid submissionId, CancellationToken ct)
        {
            if (reusedGradeId is { } existingGradeId)
            {
                var existing = await db!.WritingGrades.AsNoTracking().SingleAsync(g => g.Id == existingGradeId, ct);
                return new WritingSubmissionGradeOutcome(submissionId, existing.Id, existing.RawTotal, existing.BandLabel, true);
            }
            var gradeId = Guid.NewGuid();
            if (db is not null)
            {
                db.WritingGrades.Add(Grade(gradeId, submissionId, 32, DateTimeOffset.UtcNow));
                await db.SaveChangesAsync(ct);
            }
            return new WritingSubmissionGradeOutcome(submissionId, gradeId, 32, "B", false);
        }
    }

    private sealed class NoopWritingEventBus : IWritingEventBus
    {
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
            where TEvent : WritingEvent
            => Task.CompletedTask;
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset now = new(2026, 5, 27, 8, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;
    }
}