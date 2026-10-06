using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.RemoteJobs;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>
/// The only writer of <c>ContentPaper.ExtractedTextJson</c> for the remote path (OET-RWP/1 section 6.1.5): merge ONE key,
/// never rewrite authored members, bump <c>RowVersion</c>, touch <c>UpdatedAt</c> only on a real change (RW-082, RW-083, RW-084).
/// Runs on the InMemory provider; the Postgres compare-and-swap is covered by the PostgreSQL tests.
/// </summary>
public sealed class RemoteContentApplierTests
{
    private const string AuthoredJson = "{\"listeningQuestions\":[{\"n\":1,\"text\":\"Q\"}],\"writingStructure\":{\"a\":1}}";

    private static DbContextOptions<LearnerDbContext> Options(string name)
        => new DbContextOptionsBuilder<LearnerDbContext>().UseInMemoryDatabase(name).Options;

    private static async Task SeedPaperAsync(string dbName, string paperId, string json, int rowVersion, DateTimeOffset updatedAt)
    {
        await using var db = new LearnerDbContext(Options(dbName));
        db.ContentPapers.Add(new ContentPaper
        {
            Id = paperId,
            SubtestCode = "listening",
            Title = "Paper " + paperId,
            Slug = "paper-" + paperId,
            ExtractedTextJson = json,
            RowVersion = rowVersion,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<(string Json, int Version, DateTimeOffset UpdatedAt)> ReadPaperAsync(string dbName, string paperId)
    {
        await using var db = new LearnerDbContext(Options(dbName));
        var paper = await db.ContentPapers.AsNoTracking().SingleAsync(p => p.Id == paperId);
        return (paper.ExtractedTextJson, paper.RowVersion, paper.UpdatedAt);
    }

    // ── merger (pure) ────────────────────────────────────────────────────────

    [Fact]
    public void Merge_AddsOneKey_AndCarriesEveryOtherMemberAcross()
    {
        var merged = ExtractedTextMerger.Merge(AuthoredJson, "asset-1", "extracted text", replaceExisting: false);

        Assert.Equal(ExtractedTextMergeStatus.Written, merged.Status);
        using var document = JsonDocument.Parse(merged.Json!);
        Assert.Equal("extracted text", document.RootElement.GetProperty("asset-1").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("listeningQuestions")[0].GetProperty("n").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("writingStructure").GetProperty("a").GetInt32());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    public void Merge_ABlankOrEmptyColumn_BecomesAnObjectWithTheKey(string existing)
    {
        var merged = ExtractedTextMerger.Merge(existing, "asset-1", "t", false);

        Assert.Equal(ExtractedTextMergeStatus.Written, merged.Status);
        Assert.Equal("{\"asset-1\":\"t\"}", merged.Json);
    }

    [Fact]
    public void Merge_AnExistingKey_StandsUnlessReplacementIsAskedFor_AndAnIdenticalTextIsNeverRewritten()
    {
        var existing = "{\"asset-1\":\"old\"}";

        Assert.Equal(ExtractedTextMergeStatus.AlreadyCached, ExtractedTextMerger.Merge(existing, "asset-1", "new", false).Status);
        Assert.Equal(ExtractedTextMergeStatus.AlreadyCached, ExtractedTextMerger.Merge(existing, "asset-1", "old", true).Status);

        var replaced = ExtractedTextMerger.Merge(existing, "asset-1", "new", true);
        Assert.Equal(ExtractedTextMergeStatus.Written, replaced.Status);
        Assert.Equal("{\"asset-1\":\"new\"}", replaced.Json);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("\"a string\"")]
    public void Merge_AnUnreadableColumn_IsNeverOverwrittenBlindly(string existing)
    {
        var merged = ExtractedTextMerger.Merge(existing, "asset-1", "t", true);

        Assert.Equal(ExtractedTextMergeStatus.Unreadable, merged.Status);
        Assert.Null(merged.Json);
    }

    [Fact]
    public void ReadKeys_ListsMembers_AndReturnsNullForANonObject()
    {
        Assert.Equal(new[] { "a", "b" }, ExtractedTextMerger.ReadKeys("{\"a\":\"1\",\"b\":\"2\"}")!.OrderBy(k => k).ToArray());
        Assert.Empty(ExtractedTextMerger.ReadKeys("")!);
        Assert.Empty(ExtractedTextMerger.ReadKeys("null")!);
        Assert.Null(ExtractedTextMerger.ReadKeys("[1]"));
        Assert.Null(ExtractedTextMerger.ReadKeys("not json"));
    }

    [Fact]
    public void ReadKeys_AnAssetWhoseAutomaticAttemptsAreUsedUp_CountsAsNeedingNoWork()
    {
        // layer 02's reserved key: { "<assetId>": attempts }; the worker's SQL pre-filter reads the id as a key too
        var keys = ExtractedTextMerger.ReadKeys("{\"a\":\"1\",\"extractionExhausted\":{\"b\":5},\"extractionFailures\":[{\"assetId\":\"c\",\"attempts\":1}]}")!;

        Assert.Contains("a", keys);
        Assert.Contains("b", keys);
        Assert.DoesNotContain("c", keys); // a failure in back-off is still a candidate: a bounded wait, then a retry
    }

    [Fact]
    public void Merge_AnAssetThatNowHasText_DropsItsFailureMarkerAndExhaustedEntry_AndNoOneElses()
    {
        const string existing = "{\"listeningQuestions\":[],\"extractionExhausted\":{\"b\":5,\"c\":5},"
            + "\"extractionFailures\":[{\"assetId\":\"b\",\"attempts\":5},{\"assetId\":\"c\",\"attempts\":5}]}";

        var merged = ExtractedTextMerger.Merge(existing, "b", "text", replaceExisting: false);

        Assert.Equal(ExtractedTextMergeStatus.Written, merged.Status);
        using var document = JsonDocument.Parse(merged.Json!);
        var root = document.RootElement;
        Assert.Equal("text", root.GetProperty("b").GetString());
        Assert.False(root.GetProperty("extractionExhausted").TryGetProperty("b", out _));
        Assert.Equal(5, root.GetProperty("extractionExhausted").GetProperty("c").GetInt32());
        var remaining = root.GetProperty("extractionFailures").EnumerateArray().Select(f => f.GetProperty("assetId").GetString()).ToArray();
        Assert.Equal(new[] { "c" }, remaining);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("listeningQuestions").ValueKind);

        var last = ExtractedTextMerger.Merge(merged.Json, "c", "more", replaceExisting: false);
        using var cleared = JsonDocument.Parse(last.Json!);
        Assert.False(cleared.RootElement.TryGetProperty("extractionExhausted", out _));
        Assert.False(cleared.RootElement.TryGetProperty("extractionFailures", out _));
    }

    // ── single-key commit ────────────────────────────────────────────────────

    [Fact]
    public async Task Commit_WritesOneKey_PreservesAuthoredMembers_BumpsRowVersion_AndTouchesUpdatedAt()
    {
        var dbName = "commit-" + Guid.NewGuid().ToString("N");
        var before = DateTimeOffset.UtcNow.AddDays(-3);
        await SeedPaperAsync(dbName, "p1", AuthoredJson, rowVersion: 5, before);
        var now = DateTimeOffset.UtcNow;

        await using (var db = new LearnerDbContext(Options(dbName)))
        {
            var status = await PaperExtractedTextCommit.CommitAsync(db, "p1", "asset-1", "hello world", false, now, CancellationToken.None);
            Assert.Equal(PaperTextCommitStatus.Written, status);
        }

        var after = await ReadPaperAsync(dbName, "p1");
        using var document = JsonDocument.Parse(after.Json);
        Assert.Equal("hello world", document.RootElement.GetProperty("asset-1").GetString());
        Assert.True(document.RootElement.TryGetProperty("listeningQuestions", out _));
        Assert.True(document.RootElement.TryGetProperty("writingStructure", out _));
        Assert.Equal(6, after.Version);
        Assert.Equal(now, after.UpdatedAt);
    }

    [Fact]
    public async Task Commit_AnAlreadyCachedKey_ChangesNothing()
    {
        var dbName = "commit-" + Guid.NewGuid().ToString("N");
        var before = DateTimeOffset.UtcNow.AddDays(-3);
        await SeedPaperAsync(dbName, "p1", "{\"asset-1\":\"cached\"}", rowVersion: 2, before);

        await using (var db = new LearnerDbContext(Options(dbName)))
        {
            var status = await PaperExtractedTextCommit.CommitAsync(db, "p1", "asset-1", "different", false, DateTimeOffset.UtcNow, CancellationToken.None);
            Assert.Equal(PaperTextCommitStatus.AlreadyCached, status);
        }

        var after = await ReadPaperAsync(dbName, "p1");
        Assert.Equal("{\"asset-1\":\"cached\"}", after.Json);
        Assert.Equal(2, after.Version);
        Assert.Equal(before, after.UpdatedAt);
    }

    [Fact]
    public async Task Commit_ReportsAMissingPaper_AndAnUnreadableColumn_WithoutWriting()
    {
        var dbName = "commit-" + Guid.NewGuid().ToString("N");
        await SeedPaperAsync(dbName, "broken", "not json", rowVersion: 1, DateTimeOffset.UtcNow.AddDays(-1));

        await using var db = new LearnerDbContext(Options(dbName));

        Assert.Equal(PaperTextCommitStatus.PaperGone, await PaperExtractedTextCommit.CommitAsync(db, "missing", "a", "t", false, DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Equal(PaperTextCommitStatus.Unreadable, await PaperExtractedTextCommit.CommitAsync(db, "broken", "a", "t", true, DateTimeOffset.UtcNow, CancellationToken.None));

        var after = await ReadPaperAsync(dbName, "broken");
        Assert.Equal("not json", after.Json);
        Assert.Equal(1, after.Version);
    }

    [Fact]
    public async Task Commit_Replacement_OverwritesOnlyWhenAskedFor()
    {
        var dbName = "commit-" + Guid.NewGuid().ToString("N");
        await SeedPaperAsync(dbName, "p1", "{\"asset-1\":\"old\"}", rowVersion: 1, DateTimeOffset.UtcNow.AddDays(-1));

        await using (var db = new LearnerDbContext(Options(dbName)))
        {
            Assert.Equal(PaperTextCommitStatus.Written,
                await PaperExtractedTextCommit.CommitAsync(db, "p1", "asset-1", "new", true, DateTimeOffset.UtcNow, CancellationToken.None));
        }

        var after = await ReadPaperAsync(dbName, "p1");
        Assert.Equal("{\"asset-1\":\"new\"}", after.Json);
        Assert.Equal(2, after.Version);
    }

    // ── pdf.extract applier ──────────────────────────────────────────────────

    private static readonly string[] Pages =
    [
        "Applier page one is long enough to be real extracted text for the paper.",
        "Applier page two adds a second paragraph of extracted text for the paper.",
    ];

    private static PdfExtractResult ResultOf(bool needsOcr = false)
    {
        var flat = string.Join("\n\n", Pages).Trim();
        var pageHashes = Pages.Select(page => RemoteIds.Sha256Hex(page)).ToList();
        return new PdfExtractResult(
            "pdf.extract.result/1",
            RemoteTestData.Engine(),
            RemoteTestData.Sha,
            "flat",
            needsOcr,
            needsOcr ? "below_min_text" : null,
            Pages.Length,
            needsOcr ? 10 : flat.Length,
            needsOcr ? null : RemoteIds.Sha256Hex(flat),
            needsOcr ? null : RemoteIds.Sha256Hex(string.Join("\n", pageHashes)),
            needsOcr ? new List<string>() : pageHashes,
            needsOcr ? null : Pages);
    }

    private static string Manifest(long size)
        => JsonSerializer.Serialize(new[]
        {
            new Dictionary<string, object>
            {
                ["name"] = "pdf",
                ["sizeBytes"] = size,
                ["sha256"] = RemoteTestData.Sha,
                ["contentType"] = "application/pdf",
                ["storageKey"] = "media/sample.pdf",
            },
        });

    private static RemoteJobRow JobFor(long manifestSize = 1234, string? settingsHash = null)
        => RemoteTestData.Row(
            RemoteJobState.Succeeded,
            inputsJson: Manifest(manifestSize),
            settingsHash: settingsHash ?? PdfExtractSettings.Hash("flat", "auto", 50, false));

    private static async Task SeedAssetAsync(string dbName, string mediaId, string? sha, long size, params (string AssetId, string PaperId)[] links)
    {
        await using var db = new LearnerDbContext(Options(dbName));
        db.MediaAssets.Add(new MediaAsset
        {
            Id = mediaId,
            OriginalFilename = "sample.pdf",
            MimeType = "application/pdf",
            Format = "pdf",
            SizeBytes = size,
            StoragePath = "media/sample.pdf",
            Sha256 = sha,
            UploadedAt = DateTimeOffset.UtcNow,
        });
        foreach (var (assetId, paperId) in links)
        {
            db.ContentPaperAssets.Add(new ContentPaperAsset
            {
                Id = assetId,
                PaperId = paperId,
                MediaAssetId = mediaId,
                Role = PaperAssetRole.QuestionPaper,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<RemoteApplyOutcome> ApplyAsync(string dbName, RemoteJobRow job, PdfExtractResult result)
    {
        await using var db = new LearnerDbContext(Options(dbName));
        var handler = new PdfExtractKindHandler(
            new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base()),
            NullLogger<PdfExtractKindHandler>.Instance);
        var context = new RemoteApplyContext(
            db, job, result, "{}", "rw_00000000000000000000000001", 1, [], new RemoteJobsOptions(), TimeProvider.System);
        return await handler.ApplyAsync(context, CancellationToken.None);
    }

    [Fact]
    public async Task Apply_MergesTheAssetTextIntoEveryPaperThatReferencesIt()
    {
        var dbName = "apply-" + Guid.NewGuid().ToString("N");
        await SeedPaperAsync(dbName, "p1", AuthoredJson, 3, DateTimeOffset.UtcNow.AddDays(-2));
        await SeedPaperAsync(dbName, "p2", "{}", 0, DateTimeOffset.UtcNow.AddDays(-2));
        await SeedAssetAsync(dbName, "media-1", RemoteTestData.Sha, 1234, ("asset-a", "p1"), ("asset-b", "p2"));

        var outcome = await ApplyAsync(dbName, JobFor(), ResultOf());

        Assert.Equal(RemoteApplyKind.Applied, outcome.Kind);
        var first = await ReadPaperAsync(dbName, "p1");
        var second = await ReadPaperAsync(dbName, "p2");
        using var firstDoc = JsonDocument.Parse(first.Json);
        using var secondDoc = JsonDocument.Parse(second.Json);
        var expected = string.Join("\n\n", Pages).Trim();
        Assert.Equal(expected, firstDoc.RootElement.GetProperty("asset-a").GetString());
        Assert.True(firstDoc.RootElement.TryGetProperty("listeningQuestions", out _));
        Assert.Equal(4, first.Version);
        Assert.Equal(expected, secondDoc.RootElement.GetProperty("asset-b").GetString());
        Assert.Equal(1, second.Version);
    }

    [Fact]
    public async Task Apply_AnAssetAlreadyCached_IsANoOp()
    {
        var dbName = "apply-" + Guid.NewGuid().ToString("N");
        await SeedPaperAsync(dbName, "p1", "{\"asset-a\":\"already\"}", 2, DateTimeOffset.UtcNow.AddDays(-2));
        await SeedAssetAsync(dbName, "media-1", RemoteTestData.Sha, 1234, ("asset-a", "p1"));

        var outcome = await ApplyAsync(dbName, JobFor(), ResultOf());

        Assert.Equal(RemoteApplyKind.NoOp, outcome.Kind);
        Assert.Equal("already_cached", outcome.Reason);
        Assert.Equal(2, (await ReadPaperAsync(dbName, "p1")).Version);
    }

    [Fact]
    public async Task Apply_NeedsOcr_WritesNothingAndHandsTheAssetBackToTheLocalPath()
    {
        var dbName = "apply-" + Guid.NewGuid().ToString("N");
        await SeedPaperAsync(dbName, "p1", "{}", 0, DateTimeOffset.UtcNow.AddDays(-2));
        await SeedAssetAsync(dbName, "media-1", RemoteTestData.Sha, 1234, ("asset-a", "p1"));

        var outcome = await ApplyAsync(dbName, JobFor(), ResultOf(needsOcr: true));

        Assert.Equal(RemoteApplyKind.NoOp, outcome.Kind);
        Assert.Equal("needs_ocr", outcome.Reason);
        Assert.Equal("{}", (await ReadPaperAsync(dbName, "p1")).Json);
    }

    [Fact]
    public async Task Apply_ADeletedAssetOrOneNoPaperReferences_IsDiscardedAsGone()
    {
        var dbName = "apply-" + Guid.NewGuid().ToString("N");
        await SeedPaperAsync(dbName, "p1", "{}", 0, DateTimeOffset.UtcNow);

        var noMedia = await ApplyAsync(dbName, JobFor(), ResultOf());
        Assert.Equal(RemoteApplyKind.Discarded, noMedia.Kind);
        Assert.Equal("resource_gone", noMedia.Reason);

        await SeedAssetAsync(dbName, "media-1", RemoteTestData.Sha, 1234); // no links
        var noLinks = await ApplyAsync(dbName, JobFor(), ResultOf());
        Assert.Equal("resource_gone", noLinks.Reason);
    }

    [Fact]
    public async Task Apply_AChangedAsset_IsDiscardedAsStale_BeforeAnythingIsWritten()
    {
        var dbName = "apply-" + Guid.NewGuid().ToString("N");
        await SeedPaperAsync(dbName, "p1", "{}", 0, DateTimeOffset.UtcNow);
        await SeedAssetAsync(dbName, "media-1", new string('9', 64), 1234, ("asset-a", "p1"));

        var changedHash = await ApplyAsync(dbName, JobFor(), ResultOf());
        Assert.Equal(RemoteApplyKind.Discarded, changedHash.Kind);
        Assert.Equal("stale_input", changedHash.Reason);
        Assert.Equal("{}", (await ReadPaperAsync(dbName, "p1")).Json);
    }

    [Fact]
    public async Task Apply_AManifestSizeThatNoLongerMatchesTheAsset_IsStale()
    {
        var dbName = "apply-" + Guid.NewGuid().ToString("N");
        await SeedPaperAsync(dbName, "p1", "{}", 0, DateTimeOffset.UtcNow);
        await SeedAssetAsync(dbName, "media-1", RemoteTestData.Sha, 1234, ("asset-a", "p1"));

        var outcome = await ApplyAsync(dbName, JobFor(manifestSize: 999), ResultOf());

        Assert.Equal(RemoteApplyKind.Discarded, outcome.Kind);
        Assert.Equal("stale_input", outcome.Reason);
    }

    [Fact]
    public async Task Apply_ChangedExtractionSettings_AreDiscardedAsStale()
    {
        var dbName = "apply-" + Guid.NewGuid().ToString("N");
        await SeedPaperAsync(dbName, "p1", "{}", 0, DateTimeOffset.UtcNow);
        await SeedAssetAsync(dbName, "media-1", RemoteTestData.Sha, 1234, ("asset-a", "p1"));

        var outcome = await ApplyAsync(dbName, JobFor(settingsHash: PdfExtractSettings.Hash("flat", "azure", 50, false)), ResultOf());

        Assert.Equal(RemoteApplyKind.Discarded, outcome.Kind);
        Assert.Equal("stale_settings", outcome.Reason);
        Assert.Equal("{}", (await ReadPaperAsync(dbName, "p1")).Json);
    }

    [Fact]
    public async Task Apply_AShadowResult_NeverTouchesTheDomain()
    {
        var dbName = "apply-" + Guid.NewGuid().ToString("N");
        await SeedPaperAsync(dbName, "p1", "{}", 0, DateTimeOffset.UtcNow);
        await SeedAssetAsync(dbName, "media-1", RemoteTestData.Sha, 1234, ("asset-a", "p1"));
        var shadow = RemoteTestData.Row(RemoteJobState.Succeeded, purpose: RemoteJobPurpose.Shadow, inputsJson: Manifest(1234));

        var outcome = await ApplyAsync(dbName, shadow, ResultOf());

        Assert.Equal(RemoteApplyKind.Shadow, outcome.Kind);
        Assert.Equal("{}", (await ReadPaperAsync(dbName, "p1")).Json);
    }

    [Fact]
    public async Task CompanionApplier_ParksAValidResult_AndNeverEmbedsAnything()
    {
        var dbName = "apply-" + Guid.NewGuid().ToString("N");
        await SeedAssetAsync(dbName, "media-1", RemoteTestData.Sha, 1234);
        var job = RemoteTestData.Row(
            RemoteJobState.Succeeded,
            kind: RemoteJobKinds.CompanionIndexPrep,
            settingsHash: CompanionIndexPrepSettings.Hash(50));
        var result = new CompanionIndexPrepResult(
            "companion.index-prep.result/1", job.EngineVersion, job.InputSha256, false, null, 1, 300, "0123456789abcdef", 1,
            new List<CompanionPrepChunk> { new("Page 1", 1, "chunk") }, new string('a', 64));

        await using var db = new LearnerDbContext(Options(dbName));
        var handler = new CompanionIndexPrepKindHandler(new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base()));
        var outcome = await handler.ApplyAsync(
            new RemoteApplyContext(db, job, result, "{}", "rw_00000000000000000000000001", 1, [], new RemoteJobsOptions(), TimeProvider.System),
            CancellationToken.None);

        Assert.Equal(RemoteApplyKind.Deferred, outcome.Kind);

        var stale = await handler.ApplyAsync(
            new RemoteApplyContext(db, RemoteTestData.Row(kind: RemoteJobKinds.CompanionIndexPrep, settingsHash: new string('1', 64)), result, "{}", "rw_x", 1, [], new RemoteJobsOptions(), TimeProvider.System),
            CancellationToken.None);
        Assert.Equal("stale_settings", stale.Reason);
    }
}
