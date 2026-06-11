using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Services.Writing.Configuration;
using OetLearner.Api.Services.Writing.Events;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Proves the zero-AI invariant for mock Writing: a submission with
/// <c>Mode == "mock"</c> must NEVER reach the AI rubric. The pipeline must park
/// it as "awaiting human review" and write no <see cref="WritingGrade"/>. Every
/// collaborator that would only run on the AI grading path is a throwing stub,
/// so the test fails loudly if any AI-path code is reached.
///
/// Uses SQLite in-memory (per repo convention — catches EF translation issues
/// that <c>UseInMemoryDatabase</c> hides).
/// </summary>
public sealed class WritingMockNoAiTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;

    public WritingMockNoAiTests()
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
    public async Task EvaluateAsync_MockSubmission_SkipsAi_ParksAwaitingReview_AndWritesNoGrade()
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

        // The mock guard returns before the canon engine / mistake service / event
        // bus are ever touched, so they are passed as null! — if the guard ever
        // regresses and the AI grading path runs, the StubAiGateway throws (and the
        // null collaborators would NRE), failing the test loudly either way.
        var pipeline = new WritingSubmissionEvaluationPipeline(
            _db,
            new StubAiGateway(),              // throws if any AI call is attempted
            canonEngine: null!,               // AI-path only — must not be reached
            mistakeService: null!,            // AI-path only — must not be reached
            events: null!,                    // AI-path only — must not be reached
            TimeProvider.System,
            Options.Create(new WritingV2Options()),
            NullLogger<WritingSubmissionEvaluationPipeline>.Instance);

        var outcome = await pipeline.EvaluateAsync(submissionId, default);

        var submission = await _db.WritingSubmissions.AsNoTracking().FirstAsync(s => s.Id == submissionId);
        Assert.Equal(WritingSubmissionStatuses.AwaitingReview, submission.Status);
        Assert.False(await _db.WritingGrades.AnyAsync(g => g.SubmissionId == submissionId));
        Assert.Equal(Guid.Empty, outcome.GradeId);
    }
}
