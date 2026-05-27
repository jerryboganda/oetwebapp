using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests;

public class WritingLessonServiceTests
{
    private const string UserId = "learner-writing-lessons";

    private static (LearnerDbContext Db, WritingLessonService Service) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var clock = new FixedClock(new DateTimeOffset(2026, 5, 27, 8, 0, 0, TimeSpan.Zero));
        return (db, new WritingLessonService(db, clock));
    }

    [Fact]
    public async Task ListLessons_SeedsW1ToW8StarterLessonsWithSequentialUnlocks()
    {
        var (db, service) = Build();

        var lessons = await service.ListLessonsAsync(UserId, CancellationToken.None);

        Assert.Equal(8, lessons.Count);
        Assert.Equal("W1", lessons[0].SkillCode);
        Assert.True(lessons[0].IsUnlocked);
        Assert.False(lessons[1].IsUnlocked);
        Assert.Equal(8, await db.WritingLessons.CountAsync());

        await db.DisposeAsync();
    }

    [Fact]
    public async Task UpdateProgress_CompletesLessonAndUnlocksNextLesson()
    {
        var (db, service) = Build();
        var first = (await service.ListLessonsAsync(UserId, CancellationToken.None))[0];

        await service.UpdateProgressAsync(UserId, first.Slug, new WritingLessonProgressRequest(true, true, 4), CancellationToken.None);
        var lessons = await service.ListLessonsAsync(UserId, CancellationToken.None);

        Assert.NotNull(lessons[0].Progress?.CompletedAt);
        Assert.True(lessons[1].IsUnlocked);
        Assert.Equal(1, lessons[0].Progress?.QuizAttempts);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task UpdateProgress_RejectsOutOfRangeQuizScore()
    {
        var (db, service) = Build();
        var first = (await service.ListLessonsAsync(UserId, CancellationToken.None))[0];

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            service.UpdateProgressAsync(UserId, first.Slug, new WritingLessonProgressRequest(true, true, 999), CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("writing_lesson_quiz_score_invalid", ex.ErrorCode);

        await db.DisposeAsync();
    }

    private sealed class FixedClock(DateTimeOffset start) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => start;
    }
}