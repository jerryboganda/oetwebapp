using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests.Content;

/// <summary>
/// ContentTextExtractionService hardening: a thrown extraction is never cached as
/// empty text, failures back off and cap (a paid OCR tier is not re-billed every
/// tick), a successful empty result is cached once, a pass that changes nothing
/// writes nothing, and the write re-reads the row and merges only its own keys
/// (so a save that landed during the minutes-long extraction survives, whether or
/// not that writer bumps RowVersion) and fences on the RowVersion it read. A pass
/// that is cancelled keeps what it already extracted, and an asset whose attempts
/// are used up leaves the worker's candidate set. SQLite twin: concurrency tokens
/// and the UPDATE ... WHERE RowVersion = @old shape are real here.
/// </summary>
public sealed class ContentTextExtractionServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private DbContextOptions<LearnerDbContext> _options = default!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        await using var db = new LearnerDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task ThrownExtraction_IsNotCachedAsEmptyText_AndBacksOff()
    {
        await SeedPaperAsync("p1", "a1");
        var storage = new FakeStorage { FailingKeys = { "papers/a1.pdf" } };
        var extractor = new StubExtractor("never used");
        var before = await ReadPaperAsync("p1");

        Assert.Equal(0, await ExtractAsync("p1", storage, extractor));

        var after = await ReadPaperAsync("p1");
        var root = JsonDocument.Parse(after.ExtractedTextJson).RootElement;
        Assert.False(root.TryGetProperty("a1", out _), "a failure must not be cached as an empty text entry");
        var marker = MarkerFor(root, "a1");
        Assert.Equal(1, marker.GetProperty("attempts").GetInt32());
        Assert.True(marker.GetProperty("retryAfter").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        // The worker's SQL pre-filter looks for the asset id as a JSON KEY. A marker must not
        // contain it that way, or a failed asset would look cached and never be retried.
        Assert.DoesNotContain("\"a1\":", after.ExtractedTextJson);
        Assert.Equal(before.RowVersion + 1, after.RowVersion);
        // The marker is bookkeeping, not a content change.
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);

        // Inside the back-off window the asset is not touched again: no storage
        // read, no write.
        Assert.Equal(0, await ExtractAsync("p1", storage, extractor));
        Assert.Equal(1, storage.OpenCalls);
        Assert.Equal(after.RowVersion, (await ReadPaperAsync("p1")).RowVersion);
        Assert.Equal(0, extractor.Calls);
    }

    [Fact]
    public async Task AfterBackOff_TheAssetIsRetried_AndSuccessClearsTheMarker()
    {
        var expiredMarker = FailureJson("a1", attempts: 2, retryAfter: DateTimeOffset.UtcNow.AddMinutes(-1));
        await SeedPaperAsync("p1", "a1", extractedTextJson: expiredMarker);
        var extractor = new StubExtractor("recovered text");

        Assert.Equal(1, await ExtractAsync("p1", new FakeStorage(), extractor));

        var root = JsonDocument.Parse((await ReadPaperAsync("p1")).ExtractedTextJson).RootElement;
        Assert.Equal("recovered text", root.GetProperty("a1").GetString());
        Assert.False(root.TryGetProperty(ContentTextExtractionService.FailuresKey, out _));
        Assert.Equal(1, extractor.Calls);
    }

    [Fact]
    public async Task AttemptsAreCounted_AndAnExhaustedAssetIsLeftToAForcedRun()
    {
        var exhausted = FailureJson(
            "a1",
            attempts: ContentTextExtractionService.MaxAutomaticAttempts,
            retryAfter: DateTimeOffset.UtcNow.AddHours(-1));
        await SeedPaperAsync("p1", "a1", extractedTextJson: exhausted);
        var extractor = new StubExtractor("forced text");

        // The normal pass leaves it alone however old the marker is.
        Assert.Equal(0, await ExtractAsync("p1", new FakeStorage(), extractor));
        Assert.Equal(0, extractor.Calls);

        // A forced run ignores back-off, caches the text and clears the marker.
        Assert.Equal(1, await ExtractAsync("p1", new FakeStorage(), extractor, force: true));
        var root = JsonDocument.Parse((await ReadPaperAsync("p1")).ExtractedTextJson).RootElement;
        Assert.Equal("forced text", root.GetProperty("a1").GetString());
        Assert.False(root.TryGetProperty(ContentTextExtractionService.FailuresKey, out _));
    }

    [Fact]
    public async Task SuccessiveFailures_RaiseTheAttemptCount_AndStretchTheBackOff()
    {
        await SeedPaperAsync("p1", "a1", extractedTextJson: FailureJson("a1", attempts: 1, retryAfter: DateTimeOffset.UtcNow.AddMinutes(-1)));
        var storage = new FakeStorage { FailingKeys = { "papers/a1.pdf" } };

        Assert.Equal(0, await ExtractAsync("p1", storage, new StubExtractor("x")));

        var marker = MarkerFor(JsonDocument.Parse((await ReadPaperAsync("p1")).ExtractedTextJson).RootElement, "a1");
        Assert.Equal(2, marker.GetProperty("attempts").GetInt32());
        var retryAfter = marker.GetProperty("retryAfter").GetDateTimeOffset();
        Assert.True(retryAfter > DateTimeOffset.UtcNow.AddMinutes(30), "the second back-off is an hour, not the first 15 minutes");
        Assert.Equal(TimeSpan.FromHours(1), ContentTextExtractionService.RetryDelay(2));
    }

    [Fact]
    public async Task SuccessfulEmptyResult_IsCachedOnce()
    {
        await SeedPaperAsync("p1", "a1");
        var extractor = new StubExtractor(string.Empty);

        Assert.Equal(1, await ExtractAsync("p1", new FakeStorage(), extractor));
        var root = JsonDocument.Parse((await ReadPaperAsync("p1")).ExtractedTextJson).RootElement;
        Assert.Equal(string.Empty, root.GetProperty("a1").GetString());

        Assert.Equal(0, await ExtractAsync("p1", new FakeStorage(), extractor));
        Assert.Equal(1, extractor.Calls);
    }

    [Fact]
    public async Task APassThatChangesNothing_WritesNothingAndLeavesUpdatedAtAlone()
    {
        await SeedPaperAsync("p1", "a1", extractedTextJson: """{"a1":"already extracted","listeningQuestions":[]}""");
        var before = await ReadPaperAsync("p1");
        var extractor = new StubExtractor("unused");

        Assert.Equal(0, await ExtractAsync("p1", new FakeStorage(), extractor));

        var after = await ReadPaperAsync("p1");
        Assert.Equal(before.ExtractedTextJson, after.ExtractedTextJson);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(before.RowVersion, after.RowVersion);
        Assert.Equal(0, extractor.Calls);
    }

    [Fact]
    public async Task ASuccessfulPass_PreservesAuthoringKeys_TouchesUpdatedAt_AndBumpsRowVersion()
    {
        await SeedPaperAsync("p1", "a1", extractedTextJson: """{"listeningQuestions":[{"number":1}]}""");
        var before = await ReadPaperAsync("p1");

        Assert.Equal(1, await ExtractAsync("p1", new FakeStorage(), new StubExtractor("text")));

        var after = await ReadPaperAsync("p1");
        var root = JsonDocument.Parse(after.ExtractedTextJson).RootElement;
        Assert.Equal("text", root.GetProperty("a1").GetString());
        Assert.Equal(1, root.GetProperty("listeningQuestions").GetArrayLength());
        Assert.True(after.UpdatedAt > before.UpdatedAt);
        Assert.Equal(before.RowVersion + 1, after.RowVersion);
    }

    [Fact]
    public async Task AForcedRunThatFails_KeepsTheTextThatWasAlreadyCached()
    {
        await SeedPaperAsync("p1", "a1", extractedTextJson: """{"a1":"good cached text"}""");
        var storage = new FakeStorage { FailingKeys = { "papers/a1.pdf" } };

        Assert.Equal(0, await ExtractAsync("p1", storage, new StubExtractor("x"), force: true));

        var root = JsonDocument.Parse((await ReadPaperAsync("p1")).ExtractedTextJson).RootElement;
        Assert.Equal("good cached text", root.GetProperty("a1").GetString());
        Assert.Equal(1, MarkerFor(root, "a1").GetProperty("attempts").GetInt32());
    }

    [Fact]
    public async Task ACancelledExtraction_IsNeitherCachedNorRecordedAsAFailure()
    {
        await SeedPaperAsync("p1", "a1");
        using var cts = new CancellationTokenSource();
        var extractor = new StubExtractor("x") { OnExtract = () => { cts.Cancel(); cts.Token.ThrowIfCancellationRequested(); return Task.CompletedTask; } };

        await using (var db = new LearnerDbContext(_options))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Service(db, new FakeStorage(), extractor).ExtractForPaperAsync("p1", cts.Token));
        }

        var after = await ReadPaperAsync("p1");
        Assert.Equal("{}", after.ExtractedTextJson);
    }

    // A pass runs for minutes (OCR), so what it read at the start is stale by the time it writes.
    // The Speaking and Writing structure saves rewrite ExtractedTextJson without bumping RowVersion
    // on their own, which a RowVersion fence alone cannot see: the write re-reads the row and merges
    // only its own keys, so the other writer's keys survive either way.

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ASaveThatCommitsDuringExtraction_Survives_WhetherOrNotItBumpsRowVersion(bool bumpRowVersion)
    {
        await SeedPaperAsync("p1", "a1");
        var extractor = new StubExtractor("extracted") { OnExtract = () => ConcurrentAdminSaveAsync(bumpRowVersion) };

        Assert.Equal(1, await ExtractAsync("p1", new FakeStorage(), extractor));

        var after = await ReadPaperAsync("p1");
        var root = JsonDocument.Parse(after.ExtractedTextJson).RootElement;
        Assert.Equal("extracted", root.GetProperty("a1").GetString());
        Assert.Equal(2, root.GetProperty("speakingStructure").GetProperty("cards").GetInt32());
        Assert.Equal("edited by admin", after.Title);
        Assert.Equal(bumpRowVersion ? 2 : 1, after.RowVersion);
    }

    [Fact]
    public async Task ATextCachedByAnotherWriterDuringTheExtraction_IsNotOverwrittenByANormalPass()
    {
        await SeedPaperAsync("p1", "a1");
        var extractor = new StubExtractor("mine") { OnExtract = () => ConcurrentWriteAsync("""{"a1":"someone else got there first"}""") };

        // Nothing of this pass was written, so nothing counts as extracted.
        Assert.Equal(0, await ExtractAsync("p1", new FakeStorage(), extractor));

        var root = JsonDocument.Parse((await ReadPaperAsync("p1")).ExtractedTextJson).RootElement;
        Assert.Equal("someone else got there first", root.GetProperty("a1").GetString());
    }

    [Fact]
    public async Task AWriterThatSlipsInBetweenTheReReadAndTheSave_CostsAReMerge_NotTheExtraction()
    {
        await SeedPaperAsync("p1", "a1");
        var slipIn = new InjectOnFirstSaveInterceptor(
            () => ConcurrentWriteAsync("""{"listeningQuestions":[{"number":1}]}"""));
        var options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).AddInterceptors(slipIn).Options;

        await using (var db = new LearnerDbContext(options))
        {
            Assert.Equal(1, await Service(db, new FakeStorage(), new StubExtractor("paid for")).ExtractForPaperAsync("p1", CancellationToken.None));
        }

        // The first save lost the compare; the retry merged onto the newer row and won.
        Assert.Equal(2, slipIn.Saves);
        var after = await ReadPaperAsync("p1");
        var root = JsonDocument.Parse(after.ExtractedTextJson).RootElement;
        Assert.Equal("paid for", root.GetProperty("a1").GetString());
        Assert.Equal(1, root.GetProperty("listeningQuestions").GetArrayLength());
        // The slipped-in writer's bump, then this pass's bump on the second attempt.
        Assert.Equal(2, after.RowVersion);
    }

    [Fact]
    public async Task ACancelledPass_KeepsTheAssetsItAlreadyExtracted_ThenStillCancels()
    {
        await SeedPaperAsync("p1", "a1");
        await AddPdfAssetAsync("p1", "a2");
        using var cts = new CancellationTokenSource();
        var calls = 0;
        var extractor = new StubExtractor("paid for text")
        {
            OnExtract = () =>
            {
                // The second asset is the one the host stops during.
                if (++calls == 2)
                {
                    cts.Cancel();
                    cts.Token.ThrowIfCancellationRequested();
                }

                return Task.CompletedTask;
            },
        };

        await using (var db = new LearnerDbContext(_options))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Service(db, new FakeStorage(), extractor).ExtractForPaperAsync("p1", cts.Token));
        }

        Assert.Equal(2, extractor.Calls);
        var root = JsonDocument.Parse((await ReadPaperAsync("p1")).ExtractedTextJson).RootElement;
        var texts = root.EnumerateObject().Where(property => property.Value.ValueKind == JsonValueKind.String).ToList();
        Assert.Single(texts);
        Assert.Equal("paid for text", texts[0].Value.GetString());
    }

    [Fact]
    public async Task AFailureThatUsesUpTheAttempts_ListsTheAssetAsExhausted_UntilAForcedRunSucceeds()
    {
        await SeedPaperAsync(
            "p1",
            "a1",
            extractedTextJson: FailureJson("a1", ContentTextExtractionService.MaxAutomaticAttempts - 1, DateTimeOffset.UtcNow.AddMinutes(-1)));
        var storage = new FakeStorage { FailingKeys = { "papers/a1.pdf" } };

        Assert.Equal(0, await ExtractAsync("p1", storage, new StubExtractor("x")));

        var exhaustedBlob = (await ReadPaperAsync("p1")).ExtractedTextJson;
        var exhaustedRoot = JsonDocument.Parse(exhaustedBlob).RootElement;
        Assert.Equal(
            ContentTextExtractionService.MaxAutomaticAttempts,
            exhaustedRoot.GetProperty(ContentTextExtractionService.ExhaustedKey).GetProperty("a1").GetInt32());
        // This is what the worker's pre-filter reads as "nothing left to do for a1".
        Assert.Contains("\"a1\":", exhaustedBlob);

        Assert.Equal(1, await ExtractAsync("p1", new FakeStorage(), new StubExtractor("recovered"), force: true));
        var recovered = JsonDocument.Parse((await ReadPaperAsync("p1")).ExtractedTextJson).RootElement;
        Assert.Equal("recovered", recovered.GetProperty("a1").GetString());
        Assert.False(recovered.TryGetProperty(ContentTextExtractionService.ExhaustedKey, out _));
        Assert.False(recovered.TryGetProperty(ContentTextExtractionService.FailuresKey, out _));
    }

    [Fact]
    public void FactsRecordedByTheEmbeddedTier_FlowBackToTheCaller()
    {
        var facts = PdfExtractionFacts.Begin();
        try
        {
            PdfExtractionFacts.RecordEmbedded(3, 1200);
            Assert.Equal(3, facts.Pages);
            Assert.Equal(1200, facts.EmbeddedChars);
        }
        finally
        {
            PdfExtractionFacts.End();
        }

        // With no scope open, recording is a harmless no-op.
        PdfExtractionFacts.RecordEmbedded(9, 9);
        Assert.Equal(3, facts.Pages);
    }

    [Fact]
    public async Task FactsRecordedInsideAnAwaitedCall_AreVisibleThroughTheSameHolder()
    {
        var facts = PdfExtractionFacts.Begin();
        try
        {
            await Task.Run(async () =>
            {
                await Task.Yield();
                PdfExtractionFacts.RecordEmbedded(2, 77);
            });
            Assert.Equal(2, facts.Pages);
            Assert.Equal(77, facts.EmbeddedChars);
        }
        finally
        {
            PdfExtractionFacts.End();
        }
    }

    [Theory]
    [InlineData(0, 0, "none")]
    [InlineData(0, 40, "none")]
    [InlineData(40, 40, "embedded")]
    [InlineData(40, 0, "ocr")]
    [InlineData(80, 40, "ocr")]
    public void TierLabel_IsInferredFromWhatTheCallProduced(int chars, int embeddedChars, string expected)
        => Assert.Equal(expected, PdfExtractionFacts.DescribeTier(chars, embeddedChars));

    // ── helpers ─────────────────────────────────────────────────────────

    /// <summary>An admin structure save committing while an extraction is in flight; only some writers bump RowVersion.</summary>
    private async Task ConcurrentAdminSaveAsync(bool bumpRowVersion)
    {
        await using var other = new LearnerDbContext(_options);
        var paper = await other.ContentPapers.SingleAsync(p => p.Id == "p1");
        paper.Title = "edited by admin";
        paper.ExtractedTextJson = """{"speakingStructure":{"cards":2}}""";
        if (bumpRowVersion)
        {
            paper.RowVersion++;
        }

        await other.SaveChangesAsync();
    }

    /// <summary>Replaces the blob from another context and bumps RowVersion, like Listening authoring does.</summary>
    private async Task ConcurrentWriteAsync(string extractedTextJson)
    {
        await using var other = new LearnerDbContext(_options);
        var paper = await other.ContentPapers.SingleAsync(p => p.Id == "p1");
        paper.ExtractedTextJson = extractedTextJson;
        paper.RowVersion++;
        await other.SaveChangesAsync();
    }

    private async Task AddPdfAssetAsync(string paperId, string assetId)
    {
        await using var db = new LearnerDbContext(_options);
        var now = DateTimeOffset.UtcNow.AddMinutes(-5);
        db.MediaAssets.Add(new MediaAsset
        {
            Id = $"m-{assetId}",
            OriginalFilename = $"{assetId}.pdf",
            MimeType = "application/pdf",
            Format = "pdf",
            StoragePath = $"papers/{assetId}.pdf",
            Status = MediaAssetStatus.Ready,
            UploadedAt = now,
        });
        db.ContentPaperAssets.Add(new ContentPaperAsset
        {
            Id = assetId,
            PaperId = paperId,
            Role = PaperAssetRole.AnswerKey,
            MediaAssetId = $"m-{assetId}",
            IsPrimary = true,
            CreatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Runs <c>inject</c> once, just before the first SaveChanges executes: the writer that
    /// "slips in" after the service's re-read.</summary>
    private sealed class InjectOnFirstSaveInterceptor(Func<Task> inject) : SaveChangesInterceptor
    {
        private int _saves;

        /// <summary>Every SaveChanges the intercepted context attempted (the injected writer uses another context).</summary>
        public int Saves => _saves;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _saves) == 1)
            {
                await inject();
            }

            return result;
        }
    }

    private async Task<int> ExtractAsync(string paperId, FakeStorage storage, StubExtractor extractor, bool force = false)
    {
        await using var db = new LearnerDbContext(_options);
        return await Service(db, storage, extractor).ExtractForPaperAsync(paperId, CancellationToken.None, force);
    }

    private static ContentTextExtractionService Service(LearnerDbContext db, FakeStorage storage, StubExtractor extractor)
        => new(db, storage, extractor, NullLogger<ContentTextExtractionService>.Instance);

    private async Task<ContentPaper> ReadPaperAsync(string id)
    {
        await using var db = new LearnerDbContext(_options);
        return await db.ContentPapers.AsNoTracking().SingleAsync(p => p.Id == id);
    }

    /// <summary>The stored failure marker for one asset (the key holds an array of markers).</summary>
    private static JsonElement MarkerFor(JsonElement root, string assetId)
        => root.GetProperty(ContentTextExtractionService.FailuresKey)
            .EnumerateArray()
            .Single(marker => marker.GetProperty("assetId").GetString() == assetId);

    internal static string FailureJson(string assetId, int attempts, DateTimeOffset retryAfter)
        => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [ContentTextExtractionService.FailuresKey] = new[]
            {
                new Dictionary<string, object>
                {
                    ["assetId"] = assetId,
                    ["attempts"] = attempts,
                    ["lastAttemptAt"] = retryAfter.AddHours(-1),
                    ["retryAfter"] = retryAfter,
                    ["error"] = "IOException",
                },
            },
        });

    private async Task SeedPaperAsync(string paperId, string assetId, string? extractedTextJson = null)
    {
        await using var db = new LearnerDbContext(_options);
        var now = DateTimeOffset.UtcNow.AddMinutes(-5);
        db.MediaAssets.Add(new MediaAsset
        {
            Id = $"m-{assetId}",
            OriginalFilename = $"{assetId}.pdf",
            MimeType = "application/pdf",
            Format = "pdf",
            StoragePath = $"papers/{assetId}.pdf",
            Status = MediaAssetStatus.Ready,
            UploadedAt = now,
        });
        db.ContentPapers.Add(new ContentPaper
        {
            Id = paperId,
            SubtestCode = "reading",
            Title = paperId,
            Slug = paperId,
            Status = ContentStatus.Published,
            CreatedAt = now,
            UpdatedAt = now,
            ExtractedTextJson = extractedTextJson ?? "{}",
        });
        db.ContentPaperAssets.Add(new ContentPaperAsset
        {
            Id = assetId,
            PaperId = paperId,
            Role = PaperAssetRole.QuestionPaper,
            MediaAssetId = $"m-{assetId}",
            IsPrimary = true,
            CreatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private sealed class StubExtractor(string text) : IPdfTextExtractor
    {
        public int Calls { get; private set; }

        /// <summary>Runs inside the extraction call, before it returns.</summary>
        public Func<Task>? OnExtract { get; init; }

        public async Task<string> ExtractAsync(Stream pdfStream, CancellationToken ct)
        {
            Calls++;
            if (OnExtract is not null) await OnExtract();
            return text;
        }
    }

    private sealed class FakeStorage : IFileStorage
    {
        public HashSet<string> FailingKeys { get; } = [];

        public int OpenCalls { get; private set; }

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
        {
            OpenCalls++;
            if (FailingKeys.Contains(key))
            {
                throw new FileNotFoundException($"blob {key} is missing");
            }

            return Task.FromResult<Stream>(new MemoryStream([0x25, 0x50, 0x44, 0x46, 0x2D]));
        }

        public Task<long> WriteAsync(string key, Stream source, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenWriteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string key, CancellationToken ct) => Task.FromResult(!FailingKeys.Contains(key));
        public Task<bool> DeleteAsync(string key, CancellationToken ct) => Task.FromResult(false);
        public Task<long> LengthAsync(string key, CancellationToken ct) => Task.FromResult(5L);
        public Task MoveAsync(string sourceKey, string destKey, bool overwrite, CancellationToken ct) => Task.CompletedTask;
        public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct) => Task.FromResult(0);
        public string? TryResolveLocalPath(string key) => null;
        public Uri? ResolveReadUrl(string key, TimeSpan ttl) => null;
    }
}

/// <summary>
/// ContentTextExtractionWorker selection. The old window (oldest 20 by UpdatedAt)
/// rotated only because every visit bumped UpdatedAt; with the bump gone it would
/// have frozen on the same 20 papers. Selection is now by predicate plus an id
/// cursor, so every paper with uncached work is reached. In-memory provider for the
/// behaviour; the Npgsql translation is checked separately via ToQueryString.
/// </summary>
public sealed class ContentTextExtractionWorkerSelectionTests
{
    [Fact]
    public async Task Selects_OnlyNonArchivedPapersWithAnUncachedPdfAsset_InIdOrder()
    {
        var dbName = $"extraction-select-{Guid.NewGuid():N}";
        await using (var db = NewContext(dbName))
        {
            Add(db, "p-a", "a-a", cachedJson: """{"a-a":"cached"}""");
            Add(db, "p-b", "a-b");
            Add(db, "p-c", "a-c", status: ContentStatus.Archived);
            Add(db, "p-d", "a-d", format: "mp3");
            Add(db, "p-e", "a-e");
            // A paper with one cached and one uncached PDF still has work.
            Add(db, "p-f", "a-f1", cachedJson: """{"a-f1":"cached"}""");
            AddSecondPdf(db, "p-f", "a-f2");
            // A failed asset (marker present, no text) must stay a candidate, or its
            // back-off could never end in a retry.
            Add(
                db,
                "p-g",
                "a-g",
                cachedJson: ContentTextExtractionServiceTests.FailureJson("a-g", attempts: 1, retryAfter: DateTimeOffset.UtcNow.AddMinutes(-1)));
            // An exhausted asset (attempts used up) is listed with its id as a key, so it
            // leaves the candidate set instead of being reloaded every cycle for nothing.
            Add(db, "p-h", "a-h", cachedJson: """{"extractionExhausted":{"a-h":5}}""");
            // ... but a paper with one exhausted and one untouched PDF still has work.
            Add(db, "p-i", "a-i1", cachedJson: """{"extractionExhausted":{"a-i1":5}}""");
            AddSecondPdf(db, "p-i", "a-i2");
            await db.SaveChangesAsync();
        }

        var extraction = new RecordingExtraction();
        using var provider = Provider(dbName, extraction);

        var processed = await Worker(provider).RunOnceAsync(CancellationToken.None);

        Assert.Equal(5, processed);
        Assert.Equal(new[] { "p-b", "p-e", "p-f", "p-g", "p-i" }, extraction.PaperIds);
    }

    [Fact]
    public async Task ReachesPapersBeyondTheFirstTwenty_AcrossTicks()
    {
        var dbName = $"extraction-cursor-{Guid.NewGuid():N}";
        await using (var db = NewContext(dbName))
        {
            for (var index = 0; index < 25; index++)
            {
                Add(db, $"paper-{index:D2}", $"asset-{index:D2}");
            }

            await db.SaveChangesAsync();
        }

        // The recording service never caches anything, so every paper stays a
        // candidate forever (exactly like a paper whose asset is in failure
        // back-off). Only the cursor can get past the first 20.
        var extraction = new RecordingExtraction();
        using var provider = Provider(dbName, extraction);
        var worker = Worker(provider);

        await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(20, extraction.PaperIds.Count);
        Assert.Equal("paper-19", extraction.PaperIds[^1]);

        await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(25, extraction.PaperIds.Distinct().Count());
        Assert.Contains("paper-24", extraction.PaperIds);

        // A short batch means the end of the list: the next tick starts over.
        extraction.PaperIds.Clear();
        await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal("paper-00", extraction.PaperIds[0]);
    }

    [Fact]
    public async Task AConflictOnOnePaper_DoesNotAbortTheBatch_OrCountAsWork()
    {
        var dbName = $"extraction-conflict-{Guid.NewGuid():N}";
        await using (var db = NewContext(dbName))
        {
            Add(db, "p-1", "a-1");
            Add(db, "p-2", "a-2");
            Add(db, "p-3", "a-3");
            await db.SaveChangesAsync();
        }

        var extraction = new RecordingExtraction
        {
            ThrowFor = { ["p-2"] = new DbUpdateConcurrencyException("admin saved first") },
        };
        using var provider = Provider(dbName, extraction);

        var processed = await Worker(provider).RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, processed);
        Assert.Equal(new[] { "p-1", "p-2", "p-3" }, extraction.PaperIds);
    }

    [Fact]
    public async Task AnUnexpectedFailureOnOnePaper_DoesNotAbortTheBatchEither()
    {
        var dbName = $"extraction-failure-{Guid.NewGuid():N}";
        await using (var db = NewContext(dbName))
        {
            Add(db, "p-1", "a-1");
            Add(db, "p-2", "a-2");
            await db.SaveChangesAsync();
        }

        var extraction = new RecordingExtraction
        {
            ThrowFor = { ["p-1"] = new InvalidOperationException("storage exploded") },
        };
        using var provider = Provider(dbName, extraction);

        Assert.Equal(1, await Worker(provider).RunOnceAsync(CancellationToken.None));
        Assert.Equal(new[] { "p-1", "p-2" }, extraction.PaperIds);
    }

    [Fact]
    public async Task ShutdownCancellation_StopsTheBatch()
    {
        var dbName = $"extraction-cancel-{Guid.NewGuid():N}";
        await using (var db = NewContext(dbName))
        {
            Add(db, "p-1", "a-1");
            await db.SaveChangesAsync();
        }

        using var provider = Provider(dbName, new RecordingExtraction());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Worker(provider).RunOnceAsync(cts.Token));
    }

    [Fact]
    public void CandidateQuery_TranslatesForPostgres_WithAnIdCursorOrderAndLimit()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=translation_only;Username=none;Password=none",
                npgsql => npgsql.UseVector())
            .Options;
        using var db = new LearnerDbContext(options);

        var sql = ContentTextExtractionWorker.CandidatePaperIds(db, "paper-10").ToQueryString();

        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lower(", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static ContentTextExtractionWorker Worker(ServiceProvider provider)
        => new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ContentTextExtractionWorker>.Instance);

    private static LearnerDbContext NewContext(string dbName)
        => new(new DbContextOptionsBuilder<LearnerDbContext>().UseInMemoryDatabase(dbName).Options);

    private static ServiceProvider Provider(string dbName, IContentTextExtractionService extraction)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext(dbName));
        services.AddSingleton<IContentTextExtractionService>(extraction);
        return services.BuildServiceProvider();
    }

    private static void Add(
        LearnerDbContext db,
        string paperId,
        string assetId,
        string? cachedJson = null,
        ContentStatus status = ContentStatus.Published,
        string format = "pdf")
    {
        var now = DateTimeOffset.UtcNow;
        db.MediaAssets.Add(new MediaAsset
        {
            Id = $"m-{assetId}",
            OriginalFilename = $"{assetId}.{format}",
            MimeType = $"application/{format}",
            Format = format,
            StoragePath = $"papers/{assetId}.{format}",
            Status = MediaAssetStatus.Ready,
            UploadedAt = now,
        });
        db.ContentPapers.Add(new ContentPaper
        {
            Id = paperId,
            SubtestCode = "reading",
            Title = paperId,
            Slug = paperId,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
            ExtractedTextJson = cachedJson ?? "{}",
        });
        db.ContentPaperAssets.Add(new ContentPaperAsset
        {
            Id = assetId,
            PaperId = paperId,
            Role = PaperAssetRole.QuestionPaper,
            MediaAssetId = $"m-{assetId}",
            IsPrimary = true,
            CreatedAt = now,
        });
    }

    private static void AddSecondPdf(LearnerDbContext db, string paperId, string assetId)
    {
        var now = DateTimeOffset.UtcNow;
        db.MediaAssets.Add(new MediaAsset
        {
            Id = $"m-{assetId}",
            OriginalFilename = $"{assetId}.pdf",
            MimeType = "application/pdf",
            Format = "pdf",
            StoragePath = $"papers/{assetId}.pdf",
            Status = MediaAssetStatus.Ready,
            UploadedAt = now,
        });
        db.ContentPaperAssets.Add(new ContentPaperAsset
        {
            Id = assetId,
            PaperId = paperId,
            Role = PaperAssetRole.AnswerKey,
            MediaAssetId = $"m-{assetId}",
            IsPrimary = true,
            CreatedAt = now,
        });
    }

    private sealed class RecordingExtraction : IContentTextExtractionService
    {
        public List<string> PaperIds { get; } = [];

        public Dictionary<string, Exception> ThrowFor { get; } = new();

        public Task<int> ExtractForPaperAsync(string paperId, CancellationToken ct, bool force = false)
        {
            PaperIds.Add(paperId);
            if (ThrowFor.TryGetValue(paperId, out var failure))
            {
                throw failure;
            }

            return Task.FromResult(1);
        }
    }
}
