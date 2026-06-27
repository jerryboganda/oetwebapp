using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Guards the learner-facing scenario (writing letter) library against the bug
/// where students could not see all available letters: profession filtering was
/// case-sensitive (dropdown sends "medicine", data stored "Medicine") and the
/// list must paginate completely via a stable total.
/// </summary>
public class WritingScenarioListTests
{
    private const string UserId = "learner-scenario-list";

    [Fact]
    public async Task ListScenariosAsync_MatchesProfessionCaseInsensitively()
    {
        await using var db = BuildDb();
        // Stored capitalised, exactly as the seed data ("Medicine"/"Nursing").
        db.WritingScenarios.AddRange(
            Scenario("Routine referral A", "Medicine"),
            Scenario("Routine referral B", "Medicine"),
            Scenario("Discharge note", "Nursing"));
        await db.SaveChangesAsync();
        var service = new WritingScenarioService(db, new FixedClock());

        // Dropdown sends the lowercase profession id.
        var result = await service.ListScenariosAsync(
            UserId, profession: "medicine", letterType: null, difficulty: null,
            isDiagnostic: null, search: null, page: 1, pageSize: 50, CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal("Medicine", item.Profession));
    }

    [Fact]
    public async Task ListScenariosAsync_PaginatesWithStableTotal()
    {
        await using var db = BuildDb();
        db.WritingScenarios.AddRange(
            Scenario("Alpha", "Medicine"),
            Scenario("Bravo", "Medicine"),
            Scenario("Charlie", "Medicine"));
        await db.SaveChangesAsync();
        var service = new WritingScenarioService(db, new FixedClock());

        var page1 = await service.ListScenariosAsync(
            UserId, null, null, null, null, null, page: 1, pageSize: 2, CancellationToken.None);
        var page2 = await service.ListScenariosAsync(
            UserId, null, null, null, null, null, page: 2, pageSize: 2, CancellationToken.None);

        Assert.Equal(3, page1.Total);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(3, page2.Total);                 // total is stable across pages
        Assert.Single(page2.Items);                   // remaining tail letter is reachable
        Assert.Empty(page1.Items.Select(i => i.Id).Intersect(page2.Items.Select(i => i.Id)));
    }

    [Fact]
    public async Task ListScenariosAsync_ExcludesDraftLetters()
    {
        await using var db = BuildDb();
        var published = Scenario("Published", "Medicine");
        var draft = Scenario("Draft", "Medicine");
        draft.Status = "draft";
        db.WritingScenarios.AddRange(published, draft);
        await db.SaveChangesAsync();
        var service = new WritingScenarioService(db, new FixedClock());

        var result = await service.ListScenariosAsync(
            UserId, null, null, null, null, null, page: 1, pageSize: 50, CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal("Published", Assert.Single(result.Items).Title);
    }

    private static LearnerDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static WritingScenario Scenario(string title, string profession)
        => new()
        {
            Id = Guid.NewGuid(),
            Title = title,
            LetterType = "LT-RR",
            Profession = profession,
            Difficulty = 3,
            IsDiagnostic = false,
            Status = "published",
            AuthorId = "admin",
            PublishedAt = new DateTimeOffset(2026, 5, 27, 8, 0, 0, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(2026, 5, 27, 8, 0, 0, TimeSpan.Zero),
        };

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset now = new(2026, 5, 27, 8, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;
    }
}
