using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Writing;

namespace OetWithDrHesham.Api.Tests;

public class WritingWave4ServiceTests
{
    private const string UserId = "learner-writing-wave4";

    [Fact]
    public async Task ListMyMistakesAsync_IncludesRuleViolationAnalyticsWhenNoPersistedStatExists()
    {
        var db = BuildDb();
        var clock = new FixedClock();
        var mistakeId = Guid.NewGuid();
        db.WritingCommonMistakes.Add(new WritingCommonMistake
        {
            Id = mistakeId,
            Category = "content",
            Summary = "Missing key clinical content",
            ExampleWrong = "The discharge diagnosis is omitted.",
            ExampleRight = "The discharge diagnosis is stated clearly.",
            CanonRuleId = "missing_key_content",
            RelatedSubSkill = "W3",
            CreatedAt = clock.GetUtcNow().AddDays(-4),
        });
        db.WritingRuleViolations.AddRange(
            RuleViolation("rv-1", "missing_key_content", clock.GetUtcNow().AddDays(-2)),
            RuleViolation("rv-2", "missing_key_content", clock.GetUtcNow().AddDays(-1)));
        await db.SaveChangesAsync();
        var service = new WritingMistakeService(db, clock);

        var result = await service.ListMyMistakesAsync(UserId, CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Equal(mistakeId, row.Id);
        Assert.Equal(2, row.Stat.OccurrenceCount);

        await db.DisposeAsync();
    }

    private static LearnerDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static WritingRuleViolation RuleViolation(string id, string ruleId, DateTimeOffset generatedAt)
        => new()
        {
            Id = id,
            AttemptId = $"attempt-{id}",
            EvaluationId = $"evaluation-{id}",
            UserId = UserId,
            Profession = "medicine",
            LetterType = "LT-RR",
            RuleId = ruleId,
            Severity = "major",
            Source = "rulebook",
            Message = "Missing key content.",
            GeneratedAt = generatedAt,
        };

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset now = new(2026, 5, 27, 8, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;
    }
}
