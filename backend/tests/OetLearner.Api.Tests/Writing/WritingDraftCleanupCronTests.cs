using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing.Crons;

namespace OetLearner.Api.Tests.Writing;

/// <summary>WAI-06 retention: consumed and empty legacy drafts go after 30 days;
/// an active draft with text is resumable for 180 days.</summary>
public sealed class WritingDraftCleanupCronTests
{
    [Fact]
    public async Task Purge_KeepsResumableWork_AndDropsOnlyExpiredRows()
    {
        await using var db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var now = new DateTimeOffset(2026, 10, 2, 4, 30, 0, TimeSpan.Zero);
        void Add(string key, int ageDays, string status, string content, string? phase) => db.WritingDraftsV2.Add(new WritingDraftV2
        {
            Id = Guid.NewGuid(), UserId = key, ScenarioId = Guid.NewGuid(), Mode = "practice", Content = content,
            Status = status, Phase = phase, LastSavedAt = now.AddDays(-ageDays), CreatedAt = now.AddDays(-ageDays),
        });
        Add("submitted-31d", 31, WritingDraftStatuses.Submitted, "letter", "writing");
        Add("submitted-29d", 29, WritingDraftStatuses.Submitted, "letter", "writing");
        Add("empty-legacy-31d", 31, WritingDraftStatuses.Active, "  ", null);
        Add("empty-clock-31d", 31, WritingDraftStatuses.Active, "", "reading");
        Add("text-31d", 31, WritingDraftStatuses.Active, "Dear Dr Green", null);
        Add("text-179d", 179, WritingDraftStatuses.Active, "Dear Dr Green", "writing");
        Add("text-181d", 181, WritingDraftStatuses.Active, "Dear Dr Green", "writing");
        await db.SaveChangesAsync();

        Assert.Equal(3, await WritingDraftCleanupCron.PurgeExpiredAsync(db, now, default));

        Assert.Equal(
            new[] { "empty-clock-31d", "submitted-29d", "text-179d", "text-31d" },
            (await db.WritingDraftsV2.AsNoTracking().Select(d => d.UserId).ToListAsync()).Order(StringComparer.Ordinal));
    }
}
