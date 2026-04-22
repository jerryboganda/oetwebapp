using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests;

/// <summary>
/// Unit tests for <see cref="ListeningStructureService"/> — the publish-gate
/// validator that enforces the canonical OET Listening shape
/// (Part A = 24, Part B = 6, Part C = 12 → 42 items).
/// </summary>
public class ListeningStructureServiceTests
{
    private static (LearnerDbContext db, ListeningStructureService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new ListeningStructureService(db));
    }

    private static async Task<ContentPaper> AddPaperAsync(LearnerDbContext db, string? extractedTextJson)
    {
        var now = DateTimeOffset.UtcNow;
        var paper = new ContentPaper
        {
            Id = Guid.NewGuid().ToString("N"),
            SubtestCode = "listening",
            Title = "Listening Paper Test",
            Slug = $"listening-{Guid.NewGuid():N}",
            Status = ContentStatus.Draft,
            ExtractedTextJson = extractedTextJson ?? string.Empty,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ContentPapers.Add(paper);
        await db.SaveChangesAsync();
        return paper;
    }

    private static string BuildQuestionsJson(int partA, int partB, int partC, int partBOptionCount = 3)
    {
        var list = new List<object>();
        var num = 1;
        for (var i = 0; i < partA; i++)
            list.Add(new { id = $"a-{i}", number = num++, partCode = i < partA / 2 ? "A1" : "A2", text = "q", correctAnswer = "x" });
        for (var i = 0; i < partB; i++)
            list.Add(new { id = $"b-{i}", number = num++, partCode = "B", text = "q",
                options = Enumerable.Range(0, partBOptionCount).Select(x => $"opt-{x}").ToArray(),
                correctAnswer = "opt-0" });
        for (var i = 0; i < partC; i++)
            list.Add(new { id = $"c-{i}", number = num++, partCode = i < partC / 2 ? "C1" : "C2", text = "q", correctAnswer = "x" });
        return JsonSerializer.Serialize(new { listeningQuestions = list });
    }

    [Fact]
    public async Task CanonicalShape_24_6_12_IsPublishReady()
    {
        var (db, svc) = Build();
        var paper = await AddPaperAsync(db, BuildQuestionsJson(24, 6, 12));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.True(report.IsPublishReady);
        Assert.Equal(24, report.Counts.PartACount);
        Assert.Equal(6, report.Counts.PartBCount);
        Assert.Equal(12, report.Counts.PartCCount);
        Assert.Equal(42, report.Counts.TotalItems);
        Assert.Empty(report.Issues);
    }

    [Theory]
    [InlineData(23, 6, 12, "listening_part_a_count")]
    [InlineData(24, 5, 12, "listening_part_b_count")]
    [InlineData(24, 6, 11, "listening_part_c_count")]
    [InlineData(25, 6, 12, "listening_part_a_count")]
    public async Task NonCanonicalCounts_BlockPublish(int a, int b, int c, string expectedCode)
    {
        var (db, svc) = Build();
        var paper = await AddPaperAsync(db, BuildQuestionsJson(a, b, c));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == expectedCode && i.Severity == "error");
    }

    [Fact]
    public async Task EmptyExtractedText_BlocksPublish()
    {
        var (db, svc) = Build();
        var paper = await AddPaperAsync(db, null);

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "listening_no_items" && i.Severity == "error");
        Assert.Equal(0, report.Counts.TotalItems);
    }

    [Fact]
    public async Task MalformedJson_BlocksPublish_WithExplicitIssue()
    {
        var (db, svc) = Build();
        var paper = await AddPaperAsync(db, "{ not valid json ");

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "listening_invalid_json" && i.Severity == "error");
    }

    [Fact]
    public async Task PartB_WithWrongOptionCount_BlocksPublish()
    {
        var (db, svc) = Build();
        // Part B items have 2 options instead of the required 3.
        var paper = await AddPaperAsync(db, BuildQuestionsJson(24, 6, 12, partBOptionCount: 2));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "listening_part_b_mcq_shape" && i.Severity == "error");
    }

    [Fact]
    public async Task LegacyPartCodes_A_And_C_AreCountedCorrectly()
    {
        var (db, svc) = Build();
        // Legacy flat "A" / "C" codes (no A1/A2 granularity) still count.
        var list = new List<object>();
        var num = 1;
        for (var i = 0; i < 24; i++)
            list.Add(new { id = $"a-{i}", number = num++, partCode = "A", text = "q", correctAnswer = "x" });
        for (var i = 0; i < 6; i++)
            list.Add(new { id = $"b-{i}", number = num++, partCode = "B", text = "q",
                options = new[] { "1", "2", "3" }, correctAnswer = "1" });
        for (var i = 0; i < 12; i++)
            list.Add(new { id = $"c-{i}", number = num++, partCode = "C", text = "q", correctAnswer = "x" });
        var json = JsonSerializer.Serialize(new { listeningQuestions = list });
        var paper = await AddPaperAsync(db, json);

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.True(report.IsPublishReady);
        Assert.Equal(24, report.Counts.PartACount);
        Assert.Equal(6, report.Counts.PartBCount);
        Assert.Equal(12, report.Counts.PartCCount);
    }

    [Fact]
    public async Task ValidatePaperAsync_Throws_WhenPaperMissing()
    {
        var (_, svc) = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ValidatePaperAsync("does-not-exist", default));
    }
}
