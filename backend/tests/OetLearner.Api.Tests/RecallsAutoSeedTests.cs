using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Recalls;

namespace OetLearner.Api.Tests;

public class RecallsAutoSeedTests
{
    private static LearnerDbContext NewDb() =>
        new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task Seeds_starred_review_items_for_wrong_free_text_only()
    {
        await using var db = NewDb();
        var seed = new RecallsAutoSeed(db);

        var items = new[]
        {
            new RecallsListeningSeedItem("q1", "short_answer", "What did the patient say?", "thigh road", "thyroid"),
            new RecallsListeningSeedItem("q2", "mcq", "Which of the following…", "A", "B"),     // mcq → skipped
            new RecallsListeningSeedItem("q3", "fill_blank", "_____ pressure", "", "blood"),   // free text → seeded
        };

        await seed.SeedFromListeningAsync("user-1", "att-1", items, default);

        var seeded = db.ReviewItems.ToList();
        Assert.Equal(2, seeded.Count);
        Assert.All(seeded, r => Assert.True(r.Starred));
        Assert.All(seeded, r => Assert.Equal("hearing", r.StarReason));
        Assert.All(seeded, r => Assert.Equal("listening", r.SourceType));
        Assert.All(seeded, r => Assert.StartsWith("listening:att-1:", r.SourceId));
        Assert.Equal("active", seeded[0].Status);
    }

    [Fact]
    public async Task Re_seeding_same_attempt_does_not_duplicate()
    {
        await using var db = NewDb();
        var seed = new RecallsAutoSeed(db);

        var items = new[]
        {
            new RecallsListeningSeedItem("q1", "short_answer", "P", "x", "y"),
        };
        await seed.SeedFromListeningAsync("user-1", "att-1", items, default);
        await seed.SeedFromListeningAsync("user-1", "att-1", items, default);

        Assert.Equal(1, db.ReviewItems.Count());
    }

    [Fact]
    public async Task Skips_when_correct_answer_is_blank()
    {
        await using var db = NewDb();
        var seed = new RecallsAutoSeed(db);

        var items = new[]
        {
            new RecallsListeningSeedItem("q1", "short_answer", "P", "x", ""),
        };
        await seed.SeedFromListeningAsync("user-1", "att-1", items, default);

        Assert.Empty(db.ReviewItems);
    }
}
