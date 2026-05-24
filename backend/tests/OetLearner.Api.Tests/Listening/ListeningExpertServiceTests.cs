using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

public class ListeningExpertServiceTests
{
    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ListeningExpertService CreateService(LearnerDbContext db) =>
        new(db, NullLogger<ListeningExpertService>.Instance);

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static ContentPaper SeedPaper(string id = "paper-1") => new()
    {
        Id = id,
        SubtestCode = "listening",
        Title = "Listening Test Paper",
        Slug = $"listening-{id}",
        Status = ContentStatus.Published,
        Difficulty = "standard",
        CreatedAt = Now,
        UpdatedAt = Now,
        ExtractedTextJson = "{}",
    };

    private static LearnerUser SeedUser(string id = "user-1") => new()
    {
        Id = id,
        DisplayName = "Test Learner",
        Email = "learner@test.com",
        CreatedAt = Now,
        LastActiveAt = Now,
    };

    private static ListeningAttempt SeedAttempt(
        string id = "attempt-1",
        string paperId = "paper-1",
        string userId = "user-1",
        int rawScore = 30,
        int scaledScore = 350) => new()
    {
        Id = id,
        PaperId = paperId,
        UserId = userId,
        Status = ListeningAttemptStatus.Submitted,
        StartedAt = Now.AddMinutes(-40),
        SubmittedAt = Now,
        LastActivityAt = Now,
        RawScore = rawScore,
        MaxRawScore = 42,
        ScaledScore = scaledScore,
        Mode = ListeningAttemptMode.Exam,
    };

    [Fact]
    public async Task SubmitFeedbackAsync_WithRawScoreOverride_RecalculatesScaledViaOetScoring()
    {
        // Arrange
        await using var db = NewDb();
        db.Set<ContentPaper>().Add(SeedPaper());
        db.Users.Add(SeedUser());
        db.ListeningAttempts.Add(SeedAttempt());
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var req = new ListeningExpertFeedbackRequest(
            OverallFeedback: "Good attempt, adjusted score.",
            PerQuestionFeedback: null,
            RecommendedAreas: null,
            RawScoreOverride: 35,
            ScoreOverrideReason: "Expert reviewed and adjusted");

        // Act
        await svc.SubmitFeedbackAsync("expert-1", "attempt-1", req, CancellationToken.None);

        // Assert
        var attempt = await db.ListeningAttempts.FindAsync("attempt-1");
        Assert.NotNull(attempt);
        Assert.Equal(35, attempt.RawScore);
        Assert.Equal(OetScoring.OetRawToScaled(35), attempt.ScaledScore);
    }

    [Fact]
    public async Task SubmitFeedbackAsync_WithoutRawScoreOverride_DoesNotChangeExistingScores()
    {
        // Arrange
        await using var db = NewDb();
        db.Set<ContentPaper>().Add(SeedPaper());
        db.Users.Add(SeedUser());
        db.ListeningAttempts.Add(SeedAttempt(rawScore: 30, scaledScore: 350));
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var req = new ListeningExpertFeedbackRequest(
            OverallFeedback: "Feedback without override.",
            PerQuestionFeedback: null,
            RecommendedAreas: null,
            RawScoreOverride: null,
            ScoreOverrideReason: null);

        // Act
        await svc.SubmitFeedbackAsync("expert-1", "attempt-1", req, CancellationToken.None);

        // Assert
        var attempt = await db.ListeningAttempts.FindAsync("attempt-1");
        Assert.NotNull(attempt);
        Assert.Equal(30, attempt.RawScore);
        Assert.Equal(350, attempt.ScaledScore);
    }

    [Fact]
    public async Task GetAttemptsPagedAsync_ReturnsCorrectPagingMetadata()
    {
        // Arrange
        await using var db = NewDb();
        var paper = SeedPaper();
        var user = SeedUser();
        db.Set<ContentPaper>().Add(paper);
        db.Users.Add(user);

        // Seed 5 submitted attempts
        for (var i = 1; i <= 5; i++)
        {
            db.ListeningAttempts.Add(SeedAttempt(
                id: $"attempt-{i}",
                paperId: paper.Id,
                userId: user.Id,
                rawScore: 20 + i,
                scaledScore: OetScoring.OetRawToScaled(20 + i)));
        }
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        // Act — page 1, size 2
        var result = await svc.GetAttemptsPagedAsync(
            "expert-1", page: 1, pageSize: 2, learnerId: null, paperId: null, CancellationToken.None);

        // Assert
        Assert.Equal(5, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task SubmitFeedbackAsync_WithRawScoreOverride_Zero_ScaledIsZero()
    {
        // Arrange — edge case: expert overrides to 0
        await using var db = NewDb();
        db.Set<ContentPaper>().Add(SeedPaper());
        db.Users.Add(SeedUser());
        db.ListeningAttempts.Add(SeedAttempt());
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var req = new ListeningExpertFeedbackRequest(
            OverallFeedback: "Override to zero.",
            PerQuestionFeedback: null,
            RecommendedAreas: null,
            RawScoreOverride: 0,
            ScoreOverrideReason: "Complete re-evaluation");

        // Act
        await svc.SubmitFeedbackAsync("expert-1", "attempt-1", req, CancellationToken.None);

        // Assert
        var attempt = await db.ListeningAttempts.FindAsync("attempt-1");
        Assert.NotNull(attempt);
        Assert.Equal(0, attempt.RawScore);
        Assert.Equal(OetScoring.OetRawToScaled(0), attempt.ScaledScore);
        Assert.Equal(0, attempt.ScaledScore);
    }
}
