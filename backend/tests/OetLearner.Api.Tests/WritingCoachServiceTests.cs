using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public class WritingCoachServiceTests
{
    private static (LearnerDbContext Db, WritingCoachService Service) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new WritingCoachService(db));
    }

    [Fact]
    public async Task CheckTextAsync_DoesNotGenerateLengthOnlySuggestions()
    {
        var (db, service) = Build();
        var longText = string.Join(" ", Enumerable.Range(0, 220).Select(_ => "clinically"));

        await service.CheckTextAsync(
            userId: "learner-1",
            attemptId: "attempt-1",
            request: new WritingCoachCheckRequest(longText, CursorPosition: null),
            ct: CancellationToken.None);

        Assert.Empty(await db.WritingCoachSuggestions.ToListAsync());
        var session = await db.WritingCoachSessions.SingleAsync();
        Assert.Equal(0, session.SuggestionsGenerated);

        await db.DisposeAsync();
    }
}