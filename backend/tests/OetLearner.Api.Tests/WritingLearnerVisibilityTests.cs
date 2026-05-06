using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public class WritingLearnerVisibilityTests
{
    [Fact]
    public async Task GetWritingTaskAsync_requires_published_content()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        db.ContentItems.Add(BuildWritingContent("writing-1", ContentStatus.Archived));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            service.GetWritingTaskAsync("writing-1", CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task GetWritingModelAnswerAsync_requires_owned_submitted_attempt()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var now = DateTimeOffset.UtcNow;

        db.ContentItems.Add(BuildWritingContent("writing-1", ContentStatus.Published));
        db.Attempts.Add(BuildAttempt("other-learner", "writing-1", AttemptState.Completed, now));
        db.Attempts.Add(BuildAttempt("learner-1", "writing-1", AttemptState.InProgress, null));
        db.Attempts.Add(BuildAttempt("learner-1", "writing-1", AttemptState.Failed, now));
        await db.SaveChangesAsync();

        var locked = await Assert.ThrowsAsync<ApiException>(() =>
            service.GetWritingModelAnswerAsync("learner-1", "writing-1", CancellationToken.None));
        Assert.Equal(StatusCodes.Status403Forbidden, locked.StatusCode);

        db.Attempts.Add(BuildAttempt("learner-1", "writing-1", AttemptState.Evaluating, now));
        await db.SaveChangesAsync();

        var answer = await service.GetWritingModelAnswerAsync("learner-1", "writing-1", CancellationToken.None);
        Assert.Contains("Dear Dr Smith", JsonSupport.Serialize(answer));
    }

    [Fact]
    public async Task GetWritingModelAnswerAsync_requires_published_content()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var now = DateTimeOffset.UtcNow;

        db.ContentItems.Add(BuildWritingContent("writing-1", ContentStatus.Archived));
        db.Attempts.Add(BuildAttempt("learner-1", "writing-1", AttemptState.Completed, now));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            service.GetWritingModelAnswerAsync("learner-1", "writing-1", CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
    }

    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static LearnerService CreateService(LearnerDbContext db)
        => new(db, null!, null!, null!, null!, null!, null!);

    private static ContentItem BuildWritingContent(string id, ContentStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        return new ContentItem
        {
            Id = id,
            ContentType = "writing_task",
            SubtestCode = "writing",
            ProfessionId = "medicine",
            Title = "Writing task",
            Difficulty = "B",
            EstimatedDurationMinutes = 45,
            CriteriaFocusJson = "[\"purpose\"]",
            ScenarioType = "routine_referral",
            ModeSupportJson = "[\"learning\",\"exam\"]",
            PublishedRevisionId = "rev-1",
            Status = status,
            CaseNotes = "Patient notes",
            DetailJson = "{\"letterType\":\"routine_referral\",\"caseNotes\":\"Patient notes\"}",
            ModelAnswerJson = "{\"paragraphs\":[{\"id\":\"p1\",\"text\":\"Dear Dr Smith\"}]}",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = status == ContentStatus.Published ? now : null
        };
    }

    private static Attempt BuildAttempt(string userId, string contentId, AttemptState state, DateTimeOffset? submittedAt)
        => new()
        {
            Id = $"wa-{Guid.NewGuid():N}",
            UserId = userId,
            ContentId = contentId,
            SubtestCode = "writing",
            Context = "practice",
            Mode = "learning",
            State = state,
            StartedAt = DateTimeOffset.UtcNow,
            SubmittedAt = submittedAt,
            DraftContent = "Dear Dr Smith"
        };
}