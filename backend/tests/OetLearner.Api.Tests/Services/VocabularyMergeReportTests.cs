using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Tests.Services;

public sealed class VocabularyMergeReportTests
{
    [Fact]
    public async Task Reports_clusters_without_merging()
    {
        var db = NewDb();
        db.VocabularyWords.AddRange(
            new VocabularyWord { Id = Guid.NewGuid(), Word = "Pulse", NormalizedWord = "pulse", CreatedAt = DateTimeOffset.Parse("2026-01-01Z") },
            new VocabularyWord { Id = Guid.NewGuid(), Word = "Fever", NormalizedWord = "fever", CreatedAt = DateTimeOffset.Parse("2026-02-01Z") });
        await db.SaveChangesAsync();

        var report = await new VocabularyMergeReportService(db).BuildReportAsync(default);
        Assert.False(report.MutatedRows);
        Assert.Equal(0, report.ClusterCount);
        Assert.Equal(2, await db.VocabularyWords.CountAsync());
    }

    private static LearnerDbContext NewDb()
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
