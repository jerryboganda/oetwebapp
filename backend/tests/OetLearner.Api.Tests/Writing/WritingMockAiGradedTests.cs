using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Services.Writing.Configuration;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// W8: mock Writing is AI-graded. The pipeline must not park the submission
/// as awaiting human review.
/// </summary>
public sealed class WritingMockAiGradedTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;

    public WritingMockAiGradedTests()
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
    public async Task EvaluateAsync_MockSubmission_DoesNotParkAwaitingReview()
    {
        var submissionId = Guid.NewGuid();
        _db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = submissionId,
            UserId = "learner-1",
            ScenarioId = Guid.NewGuid(),
            Mode = "mock",
            LetterContent = "Dear Dr Smith, I am writing to refer Mr Jones for assessment and ongoing management.",
            LetterContentHash = "hash-mock-1",
            WordCount = 220,
            Status = "queued",
            GradingTier = "express",
            InputSource = "typed",
            StartedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var pipeline = new WritingSubmissionEvaluationPipeline(
            _db,
            new StubAiGateway(),
            canonEngine: null!,
            mistakeService: null!,
            events: null!,
            TimeProvider.System,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options()),
            NullLogger<WritingSubmissionEvaluationPipeline>.Instance);

        var ex = await Assert.ThrowsAsync<ApiException>(() => pipeline.EvaluateAsync(submissionId, default));
        Assert.Equal("writing_assessment_preflight_unavailable", ex.ErrorCode);

        var submission = await _db.WritingSubmissions.AsNoTracking().FirstAsync(s => s.Id == submissionId);
        Assert.NotEqual(WritingSubmissionStatuses.AwaitingReview, submission.Status);
    }
}
