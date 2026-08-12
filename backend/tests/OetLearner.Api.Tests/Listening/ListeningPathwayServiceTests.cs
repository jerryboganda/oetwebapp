using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

public class ListeningPathwayServiceTests
{
    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task GetPathwayAsync_excludes_admin_review_score_from_progression()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = "admin-review-pathway",
            UserId = "learner-admin-review-pathway",
            PaperId = "paper-admin-review-pathway",
            StartedAt = now.AddMinutes(-30),
            LastActivityAt = now,
            SubmittedAt = now,
            Status = ListeningAttemptStatus.Submitted,
            Mode = ListeningAttemptMode.Learning,
            RawScore = 42,
            MaxRawScore = 42,
            ScaledScore = 500,
            ScoreConversionTableVersionKey = "test-listening-v1",
            ScoreConversionPassed = true,
            RequiresAdminReview = true,
            AdminReviewReason = "audio_playback_error",
        });
        await db.SaveChangesAsync();

        var snapshot = await new ListeningPathwayService(db)
            .GetPathwayAsync("learner-admin-review-pathway", CancellationToken.None);

        Assert.Equal("foundation", snapshot.Stage);
        Assert.Null(snapshot.BestScaledScore);
        Assert.Contains(snapshot.Milestones, milestone => milestone.Code == "scaled_300" && !milestone.Achieved);
    }
}
