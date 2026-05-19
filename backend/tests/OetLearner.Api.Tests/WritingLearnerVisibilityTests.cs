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

    [Fact]
    public async Task GetWritingWeaknessAnalyticsAsync_returns_owned_recent_rule_violations()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var now = DateTimeOffset.UtcNow;

        db.Users.Add(BuildUser("learner-1"));
        db.Users.Add(BuildUser("other-learner"));
        db.WritingRuleViolations.AddRange(
            BuildViolation("learner-1", "layout_blank_line", "Paragraph layout needs attention.", now.AddDays(-2)),
            BuildViolation("learner-1", "grammar_articles", "Article accuracy is inconsistent.", now.AddDays(-1)),
            BuildViolation("learner-1", "custom_rule", "A rulebook finding without a known bucket.", now.AddHours(-2)),
            BuildViolation("other-learner", "irrelevant_content", "Should not leak across learners.", now.AddHours(-1)),
            BuildViolation("learner-1", "unclear_purpose", "Too old for the clamped 365-day window.", now.AddDays(-370)));
        await db.SaveChangesAsync();

        var result = await service.GetWritingWeaknessAnalyticsAsync("learner-1", 500, CancellationToken.None);

        Assert.Equal(365, result.WindowDays);
        Assert.Equal(3, result.Points.Count);
        Assert.Contains(result.Points, point => point.Tag == "poor_paragraphing" && point.Criterion == "organization");
        Assert.Contains(result.Points, point => point.Tag == "grammar_articles" && point.Criterion == "language");
        Assert.Contains(result.Points, point => point.Tag == "other_rulebook_issue" && point.Criterion is null);
        Assert.DoesNotContain(result.Points, point => point.Tag == "irrelevant_content");
        Assert.DoesNotContain(result.Points, point => point.Tag == "unclear_purpose");
    }

    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static LearnerService CreateService(LearnerDbContext db)
        => new(db, null!, null!, null!, null!, null!, null!, null!);

    private static LearnerUser BuildUser(string userId)
    {
        var now = DateTimeOffset.UtcNow;
        return new LearnerUser
        {
            Id = userId,
            DisplayName = userId,
            Email = $"{userId}@example.test",
            CreatedAt = now,
            LastActiveAt = now,
        };
    }

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

    private static WritingRuleViolation BuildViolation(string userId, string ruleId, string message, DateTimeOffset generatedAt)
        => new()
        {
            Id = $"wrv-{Guid.NewGuid():N}",
            AttemptId = $"wa-{Guid.NewGuid():N}",
            UserId = userId,
            Profession = "medicine",
            LetterType = "routine_referral",
            RuleId = ruleId,
            Severity = "major",
            Source = "rulebook",
            Message = message,
            GeneratedAt = generatedAt,
        };
}