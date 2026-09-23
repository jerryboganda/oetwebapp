using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// Submit→result must never dead-end (owner PDF §10A):
///   * a SpeakingEvaluation job that exhausts its retries / is orphaned by a
///     restart leaves its Evaluation Failed + Retryable (the learner gets a
///     "Try grading again" button) and the attempt back in Submitted;
///   * two racing submits cannot both win the attempt's state flip, so only
///     one Evaluation is queued.
/// </summary>
public sealed class SpeakingEvaluationFinalFailureTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public SpeakingEvaluationFinalFailureTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OrphanedSpeakingEvaluationJob_FailsItsEvaluationAsRetryable_AndReturnsAttemptToSubmitted()
    {
        var attemptId = $"att-final-{Guid.NewGuid():N}";
        var evaluationId = $"se-final-{Guid.NewGuid():N}";
        var stuckFor = TimeSpan.FromHours(48);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Attempts.Add(new Attempt
            {
                Id = attemptId,
                UserId = "final-failure-learner",
                ContentId = "content-final",
                SubtestCode = "speaking",
                Context = "practice",
                Mode = "self",
                State = AttemptState.Evaluating,
                StartedAt = DateTimeOffset.UtcNow - stuckFor,
            });
            db.Evaluations.Add(new Evaluation
            {
                Id = evaluationId,
                AttemptId = attemptId,
                SubtestCode = "speaking",
                State = AsyncState.Queued,
                ScoreRange = "pending",
                ModelExplanationSafe = "pending",
                LearnerDisclaimer = "pending",
                Retryable = true,
                LastTransitionAt = DateTimeOffset.UtcNow - stuckFor,
            });
            db.BackgroundJobs.Add(new BackgroundJobItem
            {
                Id = $"job-final-{Guid.NewGuid():N}",
                Type = JobType.SpeakingEvaluation,
                State = AsyncState.Processing,
                AttemptId = attemptId,
                ResourceId = evaluationId,
                CreatedAt = DateTimeOffset.UtcNow - stuckFor,
                AvailableAt = DateTimeOffset.UtcNow - stuckFor,
                LastTransitionAt = DateTimeOffset.UtcNow - stuckFor,
                StatusReasonCode = "processing",
                StatusMessage = "Job is processing.",
                Retryable = true,
            });
            await db.SaveChangesAsync();
        }

        var processor = _factory.Services.GetRequiredService<BackgroundJobProcessor>();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await processor.RecoverStuckJobsAsync(scope.ServiceProvider, db, CancellationToken.None);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var evaluation = await db.Evaluations.AsNoTracking().SingleAsync(x => x.Id == evaluationId);
            var attempt = await db.Attempts.AsNoTracking().SingleAsync(x => x.Id == attemptId);
            Assert.Equal(AsyncState.Failed, evaluation.State);
            Assert.True(evaluation.Retryable);
            Assert.Equal("speaking_evaluation_failed", evaluation.StatusReasonCode);
            Assert.Equal(AttemptState.Submitted, attempt.State);
        }
    }

    [Fact]
    public async Task TryClaimSpeakingSubmission_OnlyOneOfTwoRacingSubmitsWins()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys=OFF;";
            await pragma.ExecuteNonQueryAsync();
        }
        db.Attempts.Add(new Attempt
        {
            Id = "att-race",
            UserId = "race-learner",
            ContentId = "content-race",
            SubtestCode = "speaking",
            Context = "practice",
            Mode = "self",
            State = AttemptState.InProgress,
            StartedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        // Both racers read InProgress before either wrote.
        var first = await LearnerService.TryClaimSpeakingSubmissionAsync(db, "att-race", AttemptState.InProgress, default);
        var second = await LearnerService.TryClaimSpeakingSubmissionAsync(db, "att-race", AttemptState.InProgress, default);

        Assert.True(first);
        Assert.False(second);
        var stored = await db.Attempts.AsNoTracking().SingleAsync(x => x.Id == "att-race");
        Assert.Equal(AttemptState.Evaluating, stored.State);
    }
}
