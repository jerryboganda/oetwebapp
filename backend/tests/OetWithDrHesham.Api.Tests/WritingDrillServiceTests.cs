using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Services.Writing;

namespace OetWithDrHesham.Api.Tests;

public class WritingDrillServiceTests
{
    private const string UserId = "learner-writing-drills";

    private static (LearnerDbContext Db, WritingDrillService Service) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var clock = new FixedClock(new DateTimeOffset(2026, 5, 27, 8, 0, 0, TimeSpan.Zero));
        return (db, new WritingDrillService(db, clock));
    }

    [Fact]
    public async Task ListDrills_SeedsPublishedStarterDrillsAndFiltersBySkill()
    {
        var (db, service) = Build();

        var drills = await service.ListDrillsAsync(UserId, "W2", CancellationToken.None);

        var drill = Assert.Single(drills);
        Assert.Equal("W2", drill.TargetSubSkill);
        Assert.Equal(3, await db.WritingDrills.CountAsync());

        await db.DisposeAsync();
    }

    [Fact]
    public async Task SubmitDrill_ScoresExactAnswersDeterministically()
    {
        var (db, service) = Build();
        var drill = (await service.ListDrillsAsync(UserId, "W2", CancellationToken.None))[0];

        var result = await service.SubmitDrillAsync(UserId, drill.Id, new WritingDrillAttemptRequest("I am writing to refer.", 30), CancellationToken.None);

        Assert.True(result.IsCorrect);
        Assert.Equal(1, await db.WritingDrillAttempts.CountAsync());

        await db.DisposeAsync();
    }

    [Fact]
    public async Task SubmitCaseNoteDrill_ReturnsSentenceLevelFeedback()
    {
        var (db, service) = Build();
        var drill = (await service.ListCaseNoteDrillsAsync(UserId, CancellationToken.None))[0];
        var detail = await service.GetCaseNoteDrillAsync(UserId, drill.Id, CancellationToken.None);
        Assert.NotNull(detail);
        var responses = detail!.Sentences.ToDictionary(s => s.Id, s => s.Ordinal == 2 ? "omit" : "essential");

        var result = await service.SubmitCaseNoteDrillAsync(UserId, drill.Id, new OetWithDrHesham.Api.Services.Writing.WritingCaseNoteDrillAttemptRequest(responses, 45), CancellationToken.None);

        Assert.Equal(2, result.CorrectCount);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Feedback.Count);

        await db.DisposeAsync();
    }

    private sealed class FixedClock(DateTimeOffset start) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => start;
    }
}