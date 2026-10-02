using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// WAI-06b. "Practice this again" is a real new attempt: the terminal submit lock holds for the CURRENT
/// attempt only (scoped by the draft row's <c>AttemptStartedAt</c>), while a learner with no draft row keeps
/// the task-wide lock. A FAILED letter is never orphaned: resubmitting the same text resolves to that
/// record at any age, so there is never a duplicate record for one failed letter.
/// </summary>
public sealed class WritingSubmitAttemptLockTests : IAsyncDisposable
{
    private const string User = "learner-lock-1";
    private static readonly Guid ScenarioId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;

    public WritingSubmitAttemptLockTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.WritingScenarios.Add(new WritingScenario
        {
            Id = ScenarioId,
            Title = "Lock harness task",
            Profession = "medicine",
            LetterType = "routine_referral",
            Status = "published",
            AuthorId = "admin-1",
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        _db.SaveChanges();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task PracticeAgain_NewAttemptCanBeSubmitted_AfterAGradedLetter()
    {
        var pipeline = BuildPipeline();
        var first = await pipeline.SubmitAsync(Attempt("k1", LetterA), default);
        await Backdate(first.SubmissionId, "graded", hoursAgo: 3);
        // The learner pressed "Practice this again": a new attempt started after the graded letter.
        AddDraft(attemptStartedAt: Now.AddHours(-1));

        var second = await pipeline.SubmitAsync(Attempt("k2", LetterB), default);

        Assert.True(second.IsNew);
        Assert.NotEqual(first.SubmissionId, second.SubmissionId);
        Assert.Equal(2, await _db.WritingSubmissions.CountAsync());
    }

    [Fact]
    public async Task SameAttempt_ASecondDifferentSubmit_StaysLocked()
    {
        var pipeline = BuildPipeline();
        var first = await pipeline.SubmitAsync(Attempt("k1", LetterA), default);
        await Backdate(first.SubmissionId, "graded", hoursAgo: 1);
        // The current attempt started BEFORE the graded letter: that letter belongs to it.
        AddDraft(attemptStartedAt: Now.AddHours(-3));

        var ex = await Assert.ThrowsAsync<ApiException>(() => pipeline.SubmitAsync(Attempt("k2", LetterB), default));

        Assert.Equal("writing_submission_locked", ex.Code);
        Assert.Equal(1, await _db.WritingSubmissions.CountAsync());
    }

    [Fact]
    public async Task NoDraftRow_KeepsTheTaskWideLock()
    {
        var pipeline = BuildPipeline();
        var first = await pipeline.SubmitAsync(Attempt("k1", LetterA), default);
        await Backdate(first.SubmissionId, "graded", hoursAgo: 3);

        var ex = await Assert.ThrowsAsync<ApiException>(() => pipeline.SubmitAsync(Attempt("k2", LetterB), default));

        Assert.Equal("writing_submission_locked", ex.Code);
    }

    [Fact]
    public async Task LegacyDraftWithoutAttemptStart_KeepsTheTaskWideLock()
    {
        var pipeline = BuildPipeline();
        var first = await pipeline.SubmitAsync(Attempt("k1", LetterA), default);
        await Backdate(first.SubmissionId, "graded", hoursAgo: 3);
        AddDraft(attemptStartedAt: null);

        var ex = await Assert.ThrowsAsync<ApiException>(() => pipeline.SubmitAsync(Attempt("k2", LetterB), default));

        Assert.Equal("writing_submission_locked", ex.Code);
    }

    [Fact]
    public async Task FailedLetter_SameText_ResolvesToTheFailedRecord_AtAnyAge()
    {
        var pipeline = BuildPipeline();
        var failed = await pipeline.SubmitAsync(Attempt("k1", LetterA), default);
        await Backdate(failed.SubmissionId, "failed", hoursAgo: 72);

        var again = await pipeline.SubmitAsync(Attempt("k2-new-key", LetterA), default);

        Assert.False(again.IsNew);
        Assert.Equal(failed.SubmissionId, again.SubmissionId);
        Assert.Equal(1, await _db.WritingSubmissions.CountAsync());
    }

    [Fact]
    public async Task GradedLetter_SameText_OutsideTheRaceWindow_IsNotReused()
    {
        // Only FAILED letters resolve at any age; a graded one past the window is a different attempt
        // (and the task-wide lock decides whether it may be submitted at all).
        var pipeline = BuildPipeline();
        var graded = await pipeline.SubmitAsync(Attempt("k1", LetterA), default);
        await Backdate(graded.SubmissionId, "graded", hoursAgo: 72);
        AddDraft(attemptStartedAt: Now.AddHours(-1));

        var again = await pipeline.SubmitAsync(Attempt("k2", LetterA), default);

        Assert.True(again.IsNew);
        Assert.NotEqual(graded.SubmissionId, again.SubmissionId);
    }

    [Fact]
    public async Task FailedLetter_DifferentText_IsANewRecord_AndFailedRowsNeverLock()
    {
        var pipeline = BuildPipeline();
        var failed = await pipeline.SubmitAsync(Attempt("k1", LetterA), default);
        await Backdate(failed.SubmissionId, "failed", hoursAgo: 72);

        var other = await pipeline.SubmitAsync(Attempt("k2", LetterB), default);

        Assert.True(other.IsNew);
        Assert.Equal(2, await _db.WritingSubmissions.CountAsync());
    }

    private async Task Backdate(Guid submissionId, string status, int hoursAgo)
    {
        var row = await _db.WritingSubmissions.SingleAsync(s => s.Id == submissionId);
        row.Status = status;
        row.CreatedAt = Now.AddHours(-hoursAgo);
        row.SubmittedAt = Now.AddHours(-hoursAgo);
        await _db.SaveChangesAsync();
    }

    private void AddDraft(DateTimeOffset? attemptStartedAt)
    {
        _db.WritingDraftsV2.Add(new WritingDraftV2
        {
            Id = Guid.NewGuid(),
            UserId = User,
            ScenarioId = ScenarioId,
            Mode = "practice",
            Content = "draft text",
            WordCount = 2,
            Status = WritingDraftStatuses.Active,
            Version = 1,
            LastSavedAt = Now,
            CreatedAt = Now,
            AttemptStartedAt = attemptStartedAt,
        });
        _db.SaveChanges();
    }

    private WritingSubmissionEvaluationPipeline BuildPipeline()
        => new(
            _db,
            aiGateway: null!,
            canonEngine: null!,
            mistakeService: null!,
            events: new NoopWritingEventBus(),
            TimeProvider.System,
            settingsProvider: null!,
            NullLogger<WritingSubmissionEvaluationPipeline>.Instance);

    private static WritingSubmitAttempt Attempt(string key, string letter)
        => new(
            UserId: User,
            ScenarioId: ScenarioId,
            Mode: "practice",
            GradingTier: "express",
            InputSource: "typed",
            LetterContent: letter,
            TimeSpentSeconds: 40,
            StartedAt: Now.AddMinutes(-5),
            IsRevision: false,
            OriginalSubmissionId: null,
            IdempotencyKey: key);

    private const string LetterA =
        "Dear Dr Smith,\nRe: Mr Jones\n\nI am writing to refer Mr Jones.\n\nYours sincerely,\nDoctor";

    private const string LetterB =
        "Dear Dr Smith,\nRe: Mrs Jones\n\nI am writing to refer Mrs Jones urgently.\n\nYours sincerely,\nDoctor";
}
