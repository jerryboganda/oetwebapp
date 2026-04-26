using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using Xunit;

namespace OetLearner.Api.Tests;

public class ContentImportServiceTests
{
    private static (LearnerDbContext db, ContentImportService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new ContentImportService(db));
    }

    private static ContentImportRow Row(int idx, string title = "Sample task", string subtest = "writing")
        => new()
        {
            RowIndex = idx,
            Title = title,
            SubtestCode = subtest,
        };

    // ── Success path ───────────────────────────────────────────────────────

    [Fact]
    public async Task BulkImportAsync_creates_batch_and_items_for_valid_rows()
    {
        var (db, svc) = Build();
        var rows = new List<ContentImportRow>
        {
            Row(1, "Letter A", "writing"),
            Row(2, "Listening Part A", "listening"),
        };

        var result = await svc.BulkImportAsync("admin-1", "Spring intake", rows, default);

        Assert.Equal(2, result.Created);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.CreatedIds.Count);

        var batch = await db.ContentImportBatches.SingleAsync();
        Assert.Equal("Spring intake", batch.Title);
        Assert.Equal("completed", batch.Status);
        Assert.Equal(2, batch.TotalItems);
        Assert.Equal(2, batch.ProcessedItems);
        Assert.Equal(0, batch.FailedItems);
        Assert.NotNull(batch.CompletedAt);
        Assert.Null(batch.ErrorLogJson);

        var items = await db.ContentItems.ToListAsync();
        Assert.Equal(2, items.Count);
        Assert.All(items, i =>
        {
            Assert.Equal(ContentStatus.Draft, i.Status);
            Assert.Equal(batch.Id, i.ImportBatchId);
            Assert.Equal("admin-1", i.CreatedBy);
        });
        await db.DisposeAsync();
    }

    [Fact]
    public async Task BulkImportAsync_applies_row_defaults_when_optional_fields_omitted()
    {
        var (db, svc) = Build();
        var rows = new List<ContentImportRow> { Row(1) };

        await svc.BulkImportAsync("admin-1", "batch", rows, default);

        var item = await db.ContentItems.SingleAsync();
        Assert.Equal("task", item.ContentType);
        Assert.Equal("intermediate", item.Difficulty);
        Assert.Equal(15, item.EstimatedDurationMinutes);
        Assert.Equal("en", item.InstructionLanguage);
        Assert.Equal("en", item.ContentLanguage);
        Assert.Equal("original", item.SourceProvenance);
        Assert.Equal("owned", item.RightsStatus);
        Assert.Equal("current", item.FreshnessConfidence);
        Assert.Equal("{}", item.DetailJson);
        Assert.Equal("{}", item.ModelAnswerJson);
        Assert.Equal("[]", item.CriteriaFocusJson);
        Assert.Equal("[\"practice\"]", item.ModeSupportJson);
        Assert.Equal(0, item.QualityScore);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task BulkImportAsync_respects_caller_provided_optional_fields()
    {
        var (db, svc) = Build();
        var rows = new List<ContentImportRow>
        {
            new()
            {
                RowIndex = 1,
                Title = "Custom",
                SubtestCode = "writing",
                ContentType = "scenario",
                Difficulty = "advanced",
                EstimatedDurationMinutes = 45,
                InstructionLanguage = "ar+en",
                ContentLanguage = "ar",
                SourceProvenance = "official_sample",
                RightsStatus = "licensed",
                FreshnessConfidence = "likely_current",
                QualityScore = 4,
            },
        };

        await svc.BulkImportAsync("admin-1", "batch", rows, default);
        var item = await db.ContentItems.SingleAsync();
        Assert.Equal("scenario", item.ContentType);
        Assert.Equal("advanced", item.Difficulty);
        Assert.Equal(45, item.EstimatedDurationMinutes);
        Assert.Equal("ar+en", item.InstructionLanguage);
        Assert.Equal("ar", item.ContentLanguage);
        Assert.Equal("official_sample", item.SourceProvenance);
        Assert.Equal("licensed", item.RightsStatus);
        Assert.Equal("likely_current", item.FreshnessConfidence);
        Assert.Equal(4, item.QualityScore);
        await db.DisposeAsync();
    }

    // ── Validation rejections ──────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BulkImportAsync_rejects_blank_title(string? title)
    {
        var (db, svc) = Build();
        var rows = new List<ContentImportRow>
        {
            new() { RowIndex = 7, Title = title!, SubtestCode = "writing" },
        };

        var result = await svc.BulkImportAsync("admin-1", "b", rows, default);

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Failed);
        Assert.Single(result.Errors);
        Assert.Equal(7, result.Errors[0].RowIndex);
        Assert.Contains("Title", result.Errors[0].Message);
        Assert.Equal(0, await db.ContentItems.CountAsync());
        await db.DisposeAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BulkImportAsync_rejects_blank_subtest_code(string? code)
    {
        var (db, svc) = Build();
        var rows = new List<ContentImportRow>
        {
            new() { RowIndex = 3, Title = "x", SubtestCode = code! },
        };

        var result = await svc.BulkImportAsync("admin-1", "b", rows, default);

        Assert.Equal(1, result.Failed);
        Assert.Contains("SubtestCode", result.Errors[0].Message);
        Assert.Equal(0, await db.ContentItems.CountAsync());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task BulkImportAsync_rejects_invalid_DetailJson()
    {
        var (db, svc) = Build();
        var rows = new List<ContentImportRow>
        {
            new()
            {
                RowIndex = 5,
                Title = "x",
                SubtestCode = "writing",
                DetailJson = "{not valid json", // malformed
            },
        };

        var result = await svc.BulkImportAsync("admin-1", "b", rows, default);

        Assert.Equal(1, result.Failed);
        Assert.Contains("DetailJson invalid", result.Errors[0].Message);
        Assert.Equal(0, await db.ContentItems.CountAsync());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task BulkImportAsync_mixed_outcome_records_partial_success()
    {
        var (db, svc) = Build();
        var rows = new List<ContentImportRow>
        {
            Row(1, "ok", "writing"),
            new() { RowIndex = 2, Title = "", SubtestCode = "writing" }, // bad
            Row(3, "also-ok", "speaking"),
        };

        var result = await svc.BulkImportAsync("admin-1", "mixed", rows, default);

        Assert.Equal(2, result.Created);
        Assert.Equal(1, result.Failed);

        var batch = await db.ContentImportBatches.SingleAsync();
        Assert.Equal("completed_with_errors", batch.Status);
        Assert.Equal(2, batch.ProcessedItems);
        Assert.Equal(1, batch.FailedItems);
        Assert.NotNull(batch.ErrorLogJson);
        Assert.Contains("Title", batch.ErrorLogJson);
        Assert.Equal(2, await db.ContentItems.CountAsync());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task BulkImportAsync_empty_rows_yields_completed_batch_no_items()
    {
        var (db, svc) = Build();
        var result = await svc.BulkImportAsync("admin-1", "empty", new List<ContentImportRow>(), default);

        Assert.Equal(0, result.Created);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.CreatedIds);

        var batch = await db.ContentImportBatches.SingleAsync();
        Assert.Equal(0, batch.TotalItems);
        Assert.Equal("completed", batch.Status);
        Assert.Null(batch.ErrorLogJson);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task BulkImportAsync_generates_unique_item_ids_with_subtest_prefix()
    {
        var (db, svc) = Build();
        var rows = new List<ContentImportRow>
        {
            Row(1, "a", "writing"),
            Row(2, "b", "writing"),
            Row(3, "c", "listening"),
        };

        await svc.BulkImportAsync("admin-1", "b", rows, default);

        var ids = await db.ContentItems.Select(i => new { i.Id, i.SubtestCode }).ToListAsync();
        Assert.Equal(3, ids.Select(i => i.Id).Distinct().Count());
        // Implementation uses `subtest[..2]` as ID prefix: "wr-…" / "li-…".
        Assert.All(ids.Where(i => i.SubtestCode == "writing"), i => Assert.StartsWith("wr-", i.Id));
        Assert.All(ids.Where(i => i.SubtestCode == "listening"), i => Assert.StartsWith("li-", i.Id));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task BulkImportAsync_imported_items_are_always_Draft_status_even_with_official_provenance()
    {
        var (db, svc) = Build();
        var rows = new List<ContentImportRow>
        {
            new() { RowIndex = 1, Title = "x", SubtestCode = "writing", SourceProvenance = "official_sample" },
        };

        await svc.BulkImportAsync("admin-1", "b", rows, default);

        var item = await db.ContentItems.SingleAsync();
        Assert.Equal(ContentStatus.Draft, item.Status);
        Assert.Equal("official_sample", item.SourceProvenance);
        await db.DisposeAsync();
    }
}
