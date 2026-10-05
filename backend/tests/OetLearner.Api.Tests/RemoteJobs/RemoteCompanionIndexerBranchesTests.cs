using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>
/// The branches <see cref="CompanionDocumentIndexer"/> takes once the remote preparation answers (enqueue-and-poll, OET-RWP/1 section 6.2):
/// Pending leaves the file for the next reindex, Skip explains itself, Ready writes the helper's chunks WITHOUT extracting locally and
/// then consumes the parked result, and Local is the unchanged in-process path. A fake <see cref="IRemoteCompanionIndexPrep"/> drives
/// them, so no PostgreSQL is needed (the producer/consumer itself is covered by <c>RemoteCompanionPrepPostgresTests</c>).
/// </summary>
public sealed class RemoteCompanionIndexerBranchesTests
{
    private sealed class FakePrep(Func<MediaAsset, string, CompanionPrepPlan> decide) : IRemoteCompanionIndexPrep
    {
        public List<(string AssetId, string SourceKey)> Planned { get; } = [];

        public List<(CompanionPrepPlan Plan, bool FullyCommitted)> Completed { get; } = [];

        public Task<CompanionPrepPlan> PlanAsync(MediaAsset asset, string sourceKey, CancellationToken ct)
        {
            Planned.Add((asset.Id, sourceKey));
            return Task.FromResult(decide(asset, sourceKey));
        }

        public Task CompleteAsync(CompanionPrepPlan plan, bool fullyCommitted, CancellationToken ct)
        {
            Completed.Add((plan, fullyCommitted));
            return Task.CompletedTask;
        }
    }

    /// <summary>Counts calls so a test can prove the in-process extraction did (or did not) run.</summary>
    private sealed class CountingExtractor(string text) : IPdfTextExtractor
    {
        public int Calls { get; private set; }

        public Task<string> ExtractAsync(Stream pdfStream, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(text);
        }
    }

    private static readonly string LocalText = string.Join(
        ' ',
        Enumerable.Repeat("This handout explains how to structure the opening paragraph of a referral letter clearly.", 6));

    private static LearnerDbContext NewDb()
        => new(new DbContextOptionsBuilder<LearnerDbContext>().UseInMemoryDatabase("companion-indexer-" + Guid.NewGuid().ToString("N")).Options);

    private static async Task SeedPdfAsync(LearnerDbContext db, InMemoryFileStorage storage, string fileId, string title)
    {
        var now = DateTimeOffset.UtcNow;
        var assetId = "asset-" + fileId;
        db.MediaAssets.Add(new MediaAsset
        {
            Id = assetId,
            OriginalFilename = fileId + ".pdf",
            MimeType = "application/pdf",
            Format = "pdf",
            SizeBytes = 100,
            StoragePath = "materials/" + fileId + ".pdf",
            Status = MediaAssetStatus.Ready,
            UploadedAt = now,
        });
        db.MaterialFiles.Add(new MaterialFile
        {
            Id = fileId,
            MediaAssetId = assetId,
            SubtestCode = "writing",
            Kind = "pdf",
            Title = title,
            Status = ContentStatus.Published,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        await storage.WriteAsync("materials/" + fileId + ".pdf", new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4")), CancellationToken.None);
    }

    private static CompanionDocumentIndexer NewIndexer(LearnerDbContext db, InMemoryFileStorage storage, CountingExtractor extractor, IRemoteCompanionIndexPrep? prep)
        => new(db, new UnusedEmbeddings(), extractor, storage, NullLogger<CompanionDocumentIndexer>.Instance, prep);

    private static CompanionPrepPlan ReadyPlan(string jobId, string version)
        => new(
            CompanionPrepAction.Ready,
            null,
            jobId,
            version,
            [
                new CompanionChunkDraft("Opening paragraph", "State the exact request in the first sentence of the letter.", 1),
                new CompanionChunkDraft("Closing paragraph", "End with a request that differs from the one in the introduction.", 2),
            ]);

    [Fact]
    public async Task Pending_LeavesTheFileForTheNextReindex_AndSaysSo_WithoutExtractingOrWriting()
    {
        await using var db = NewDb();
        var storage = new InMemoryFileStorage();
        await SeedPdfAsync(db, storage, "mf-pending", "Pending handout");
        var extractor = new CountingExtractor(LocalText);
        var prep = new FakePrep((_, _) => new CompanionPrepPlan(CompanionPrepAction.Pending, "queued for remote extraction"));

        var result = await NewIndexer(db, storage, extractor, prep).IndexAsync(embed: false, CancellationToken.None);

        Assert.Equal(0, result.SourcesWritten);
        Assert.Equal(0, result.ChunksWritten);
        Assert.Equal(0, extractor.Calls);
        Assert.Empty(prep.Completed);
        Assert.Equal(0, await db.CompanionSources.CountAsync());
        Assert.Contains(result.Warnings, warning => warning.Contains("being prepared on a helper", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Skip_ExplainsItself_AndDoesNotExtractOrWrite()
    {
        await using var db = NewDb();
        var storage = new InMemoryFileStorage();
        await SeedPdfAsync(db, storage, "mf-skip", "Quarantined handout");
        var extractor = new CountingExtractor(LocalText);
        var prep = new FakePrep((_, _) => new CompanionPrepPlan(CompanionPrepAction.Skip, "remote extraction is quarantined; an admin must requeue or force it local"));

        var result = await NewIndexer(db, storage, extractor, prep).IndexAsync(embed: false, CancellationToken.None);

        Assert.Equal(0, result.SourcesWritten);
        Assert.Equal(0, extractor.Calls);
        Assert.Equal(0, await db.CompanionSources.CountAsync());
        Assert.Contains(result.Warnings, warning => warning.Contains("Quarantined handout", StringComparison.Ordinal) && warning.Contains("quarantined", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("being prepared on a helper", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ready_WritesTheHelpersChunks_WithoutExtractingLocally_ThenConsumesTheParkedResult()
    {
        await using var db = NewDb();
        var storage = new InMemoryFileStorage();
        await SeedPdfAsync(db, storage, "mf-ready", "Ready handout");
        var extractor = new CountingExtractor(LocalText);
        var plan = ReadyPlan("rj_ready_job", "remote-v1");
        var prep = new FakePrep((_, _) => plan);

        var result = await NewIndexer(db, storage, extractor, prep).IndexAsync(embed: false, CancellationToken.None);

        Assert.Equal(1, result.SourcesWritten);
        Assert.Equal(2, result.ChunksWritten);
        Assert.Equal(0, extractor.Calls); // the helper already did the extraction and the chunking
        Assert.Empty(result.Warnings);

        var source = await db.CompanionSources.SingleAsync();
        Assert.Equal("material:mf-ready", source.SourceKey);
        Assert.Equal("remote-v1", source.Version);
        Assert.Equal("Ready handout", source.Title);
        Assert.Equal("materials/mf-ready.pdf", source.StorageLocator);
        var chunks = await db.CompanionChunks.Where(chunk => chunk.SourceId == source.Id).OrderBy(chunk => chunk.Ordinal).ToListAsync();
        Assert.Equal(new int?[] { 1, 2 }, chunks.Select(chunk => chunk.PageNumber).ToArray());
        Assert.Equal("Opening paragraph", chunks[0].Heading);

        // A clean write consumes the parked remote result exactly once.
        var completed = Assert.Single(prep.Completed);
        Assert.Equal("rj_ready_job", completed.Plan.JobId);
        Assert.True(completed.FullyCommitted);
    }

    [Fact]
    public async Task Ready_AWriteThatCarriesWarnings_LeavesTheParkedResultForARetry()
    {
        await using var db = NewDb();
        var storage = new InMemoryFileStorage();
        await SeedPdfAsync(db, storage, "mf-warn", "Warning handout");
        var extractor = new CountingExtractor(LocalText);
        var prep = new FakePrep((_, _) => ReadyPlan("rj_warn_job", "remote-v1"));
        var indexer = NewIndexer(db, storage, extractor, prep);
        await indexer.IndexAsync(embed: false, CancellationToken.None);
        prep.Completed.Clear();

        // A new version supersedes the old one, which the writer reports as a warning: the result is not treated as fully committed.
        var next = new FakePrep((_, _) => ReadyPlan("rj_warn_job_2", "remote-v2"));
        var result = await NewIndexer(db, storage, extractor, next).IndexAsync(embed: false, CancellationToken.None);

        Assert.Contains(result.Warnings, warning => warning.Contains("superseded", StringComparison.Ordinal));
        var completed = Assert.Single(next.Completed);
        Assert.Equal("rj_warn_job_2", completed.Plan.JobId);
        Assert.False(completed.FullyCommitted);
        Assert.Equal(0, extractor.Calls);
    }

    [Fact]
    public async Task Local_IsTheUnchangedInProcessPath_AndNeverCompletesAnything()
    {
        await using var db = NewDb();
        var storage = new InMemoryFileStorage();
        await SeedPdfAsync(db, storage, "mf-local", "Local handout");
        var extractor = new CountingExtractor(LocalText);
        var prep = new FakePrep((_, _) => CompanionPrepPlan.UseLocal);

        var result = await NewIndexer(db, storage, extractor, prep).IndexAsync(embed: false, CancellationToken.None);

        Assert.Equal(1, extractor.Calls);
        Assert.Equal(1, result.SourcesWritten);
        Assert.True(result.ChunksWritten >= 1);
        Assert.Single(prep.Planned);
        Assert.Empty(prep.Completed);
        var source = await db.CompanionSources.SingleAsync();
        Assert.Equal("material:mf-local", source.SourceKey);
    }

    [Fact]
    public async Task WithoutARemotePrep_TheIndexerExtractsLocally_ExactlyAsBefore()
    {
        await using var db = NewDb();
        var storage = new InMemoryFileStorage();
        await SeedPdfAsync(db, storage, "mf-none", "Unassisted handout");
        var extractor = new CountingExtractor(LocalText);

        var result = await NewIndexer(db, storage, extractor, prep: null).IndexAsync(embed: false, CancellationToken.None);

        Assert.Equal(1, extractor.Calls);
        Assert.Equal(1, result.SourcesWritten);
    }

    [Fact]
    public async Task APendingFile_DoesNotBlockTheOthers_AndTheWarningCountsHowManyWait()
    {
        await using var db = NewDb();
        var storage = new InMemoryFileStorage();
        await SeedPdfAsync(db, storage, "mf-wait-a", "Waiting handout A");
        await SeedPdfAsync(db, storage, "mf-wait-b", "Waiting handout B");
        await SeedPdfAsync(db, storage, "mf-wait-c", "Finished handout C");
        var extractor = new CountingExtractor(LocalText);
        var prep = new FakePrep((_, sourceKey) => sourceKey == "material:mf-wait-c"
            ? ReadyPlan("rj_c", "remote-v1")
            : new CompanionPrepPlan(CompanionPrepAction.Pending, "remote extraction in progress"));

        var result = await NewIndexer(db, storage, extractor, prep).IndexAsync(embed: false, CancellationToken.None);

        Assert.Equal(1, result.SourcesWritten);
        Assert.Equal("material:mf-wait-c", (await db.CompanionSources.SingleAsync()).SourceKey);
        Assert.Contains(result.Warnings, warning => warning.StartsWith("2 material PDF(s) are being prepared on a helper", StringComparison.Ordinal));
        Assert.Equal(3, prep.Planned.Count);
        Assert.Equal("rj_c", Assert.Single(prep.Completed).Plan.JobId);
    }
}
