using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts.Classes;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Classes;

namespace OetLearner.Api.Tests.Classes;

/// <summary>
/// Verifies ClassFeedbackService is idempotent on (sessionId, userId) and
/// aggregates ratings correctly. Re-submission updates rather than duplicates
/// per the unique index on (ClassSessionId, UserId).
/// </summary>
public sealed class ClassFeedbackServiceTests
{
    [Fact]
    public async Task SubmitAsync_RejectsRatingOutsideRange()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        SeedSessionAndEnrollment(db, "LCS-1", "learner-1", now);
        await db.SaveChangesAsync();
        var service = new ClassFeedbackService(db, new FixedTimeProvider(now));

        var tooLow = await Assert.ThrowsAsync<ApiException>(() => service.SubmitAsync(
            "LCS-1", "learner-1", new ClassFeedbackSubmitRequest(0, "comment", true), idempotencyKey: null, CancellationToken.None));
        Assert.Equal("class_feedback_rating_invalid", tooLow.ErrorCode);

        var tooHigh = await Assert.ThrowsAsync<ApiException>(() => service.SubmitAsync(
            "LCS-1", "learner-1", new ClassFeedbackSubmitRequest(6, "comment", true), idempotencyKey: null, CancellationToken.None));
        Assert.Equal("class_feedback_rating_invalid", tooHigh.ErrorCode);
    }

    [Fact]
    public async Task SubmitAsync_RequiresEnrollment()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        SeedSessionAndEnrollment(db, "LCS-1", "learner-1", now, enroll: false);
        await db.SaveChangesAsync();
        var service = new ClassFeedbackService(db, new FixedTimeProvider(now));

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.SubmitAsync(
            "LCS-1", "learner-1", new ClassFeedbackSubmitRequest(5, "great", true), idempotencyKey: null, CancellationToken.None));
        Assert.Equal("class_feedback_forbidden", exception.ErrorCode);
    }

    [Fact]
    public async Task SubmitAsync_IsIdempotentForRepeatSubmissions()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        SeedSessionAndEnrollment(db, "LCS-1", "learner-1", now);
        await db.SaveChangesAsync();
        var service = new ClassFeedbackService(db, new FixedTimeProvider(now));

        var first = await service.SubmitAsync(
            "LCS-1",
            "learner-1",
            new ClassFeedbackSubmitRequest(4, "Good", true),
            idempotencyKey: "key-1",
            CancellationToken.None);
        var second = await service.SubmitAsync(
            "LCS-1",
            "learner-1",
            new ClassFeedbackSubmitRequest(5, "Better", false),
            idempotencyKey: "key-2",
            CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(5, second.Rating);
        Assert.Equal("Better", second.Comment);
        Assert.False(second.RecommendToFriend);
        Assert.Equal(1, await db.ClassFeedbacks.CountAsync());
    }

    [Fact]
    public async Task GetForSessionAsync_AggregatesAverageAndRecommendPercent()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        SeedSessionAndEnrollment(db, "LCS-1", "learner-1", now);
        SeedSessionAndEnrollment(db, "LCS-1", "learner-2", now, seedSession: false);
        SeedSessionAndEnrollment(db, "LCS-1", "learner-3", now, seedSession: false);
        SeedSessionAndEnrollment(db, "LCS-1", "learner-4", now, seedSession: false);
        await db.SaveChangesAsync();
        var service = new ClassFeedbackService(db, new FixedTimeProvider(now));

        await service.SubmitAsync("LCS-1", "learner-1", new(5, "loved it", true), null, CancellationToken.None);
        await service.SubmitAsync("LCS-1", "learner-2", new(3, null, true), null, CancellationToken.None);
        await service.SubmitAsync("LCS-1", "learner-3", new(4, "ok", false), null, CancellationToken.None);
        await service.SubmitAsync("LCS-1", "learner-4", new(4, "fine", true), null, CancellationToken.None);

        var aggregate = await service.GetForSessionAsync("LCS-1", recentLimit: 10, CancellationToken.None);

        Assert.Equal(4, aggregate.Count);
        Assert.Equal(4d, aggregate.AverageRating);
        Assert.Equal(75d, aggregate.RecommendPercent);
        // Two comments (loved it, ok, fine — three actually — but learner-2 has no comment)
        Assert.Equal(3, aggregate.RecentComments.Count);
    }

    [Fact]
    public async Task GetForSessionAsync_ReturnsZeroAggregateWhenNoFeedback()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var service = new ClassFeedbackService(db, new FixedTimeProvider(now));

        var aggregate = await service.GetForSessionAsync("LCS-missing", recentLimit: 10, CancellationToken.None);

        Assert.Equal(0, aggregate.Count);
        Assert.Equal(0d, aggregate.AverageRating);
        Assert.Empty(aggregate.RecentComments);
    }

    // -- helpers -----------------------------------------------------------

    private static void SeedSessionAndEnrollment(
        LearnerDbContext db,
        string sessionId,
        string userId,
        DateTimeOffset now,
        bool seedSession = true,
        bool enroll = true)
    {
        if (seedSession)
        {
            var liveClass = new LiveClass
            {
                Id = "LC-1",
                Slug = "lc-1",
                Title = "Live class",
                Description = "Desc",
                Type = LiveClassType.GroupClass,
                Status = LiveClassStatus.Published,
                CreatedAt = now,
                UpdatedAt = now,
            };
            var session = new LiveClassSession
            {
                Id = sessionId,
                LiveClassId = liveClass.Id,
                ScheduledStartAt = now.AddHours(-2),
                ScheduledEndAt = now.AddHours(-1),
                Capacity = 10,
                Status = LiveClassSessionStatus.Completed,
                CreatedAt = now,
                UpdatedAt = now,
            };
            liveClass.Sessions.Add(session);
            db.LiveClasses.Add(liveClass);
        }

        if (enroll)
        {
            db.LiveClassEnrollments.Add(new LiveClassEnrollment
            {
                Id = $"LCE-{Guid.NewGuid():N}",
                ClassSessionId = sessionId,
                UserId = userId,
                EnrolledAt = now.AddDays(-1),
                IdempotencyKey = $"fb-{userId}",
                Status = LiveClassEnrollmentStatus.Attended,
            });
        }
    }

    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
