using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Services;

public sealed class SpeakingDuplicateMarkerTests
{
    [Fact]
    public async Task Marks_later_identical_rows_and_keeps_earliest()
    {
        var db = NewDb();
        db.SpeakingAiAssessments.AddRange(
            Row("a1", "sess", "tr1", DateTimeOffset.Parse("2026-01-01Z")),
            Row("a2", "sess", "tr1", DateTimeOffset.Parse("2026-01-02Z")));
        await db.SaveChangesAsync();

        var marked = await new SpeakingDuplicateMarker(db).MarkDuplicatesAsync(default);
        Assert.Equal(1, marked);
        Assert.False(db.SpeakingAiAssessments.Single(a => a.Id == "a1").IsDuplicate);
        Assert.True(db.SpeakingAiAssessments.Single(a => a.Id == "a2").IsDuplicate);
        Assert.Equal(2, await db.SpeakingAiAssessments.CountAsync());
    }

    private static SpeakingAiAssessment Row(string id, string session, string transcript, DateTimeOffset at)
        => new()
        {
            Id = id,
            SpeakingSessionId = session,
            TranscriptId = transcript,
            Provider = "anthropic",
            ModelId = "claude",
            PromptTemplateId = "speaking.score.v2",
            ReadinessBand = "C",
            GeneratedAt = at,
        };

    private static LearnerDbContext NewDb()
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
