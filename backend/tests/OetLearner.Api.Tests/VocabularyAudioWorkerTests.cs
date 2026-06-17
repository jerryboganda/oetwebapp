using Microsoft.AspNetCore.Hosting;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Conversation.Tts;
using OetLearner.Api.Services.Vocabulary;
using OetLearner.Api.Services.VoiceDesign;
using Xunit;

namespace OetLearner.Api.Tests;

public sealed class VocabularyAudioWorkerTests
{
    [Fact]
    public async Task EnqueueOnImport_StoresAudioAndUpdatesTerm()
    {
        await using var fixture = await Fixture.CreateAsync(ttsProvider: "auto");
        fixture.Db.VocabularyTerms.Add(new VocabularyTerm
        {
            Id = "VOC-001",
            Term = "haemorrhage",
            ExamTypeCode = "oet",
            Category = "clinical",
            Status = "active",
        });
        await fixture.Db.SaveChangesAsync();

        await fixture.Worker.ProcessOneAsync(
            new VocabularyAudioJob("VOC-001", "haemorrhage", null, "en-GB", "batch-1"),
            CancellationToken.None);

        var term = await fixture.Db.VocabularyTerms.AsNoTracking().FirstAsync(t => t.Id == "VOC-001");
        Assert.False(string.IsNullOrWhiteSpace(term.AudioMediaAssetId));
        Assert.False(string.IsNullOrWhiteSpace(term.AudioUrl));

        var asset = await fixture.Db.MediaAssets.AsNoTracking().FirstAsync(a => a.Id == term.AudioMediaAssetId);
        Assert.Equal("mp3", asset.Format);
        Assert.Equal("audio", asset.MediaKind);
        Assert.False(string.IsNullOrWhiteSpace(asset.Sha256));
        Assert.Equal(MediaAssetStatus.Ready, asset.Status);

        Assert.Contains(fixture.Db.AuditEvents.AsNoTracking().ToList(),
            e => e.Action == "VocabAudioGenerated" && e.ResourceId == "VOC-001");
    }

    [Fact]
    public async Task Backfill_PicksUpTermsWithoutAudio()
    {
        await using var fixture = await Fixture.CreateAsync(ttsProvider: "auto");
        fixture.Db.VocabularyTerms.AddRange(
            new VocabularyTerm
            {
                Id = "VOC-A", Term = "alpha", ExamTypeCode = "oet", Category = "c",
                Status = "active",
                SourceProvenance = "batch=B01;source=admin-vocabulary-import",
            },
            new VocabularyTerm
            {
                Id = "VOC-B", Term = "beta", ExamTypeCode = "oet", Category = "c",
                Status = "active",
                SourceProvenance = "batch=B01;source=admin-vocabulary-import",
            },
            new VocabularyTerm
            {
                Id = "VOC-C", Term = "gamma", ExamTypeCode = "oet", Category = "c",
                Status = "active",
                SourceProvenance = "batch=B01;source=admin-vocabulary-import",
                AudioMediaAssetId = "MA-existing",
                AudioUrl = "/media/file/existing.mp3",
            });
        fixture.Db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = "vocab-commit:B01",
            Scope = "admin-vocabulary-import",
            Key = "B01",
            ResponseJson = JsonSerializer.Serialize(new
            {
                importBatchId = "B01",
                fileSha256 = "test",
                termIds = new[] { "VOC-A", "VOC-B", "VOC-C" },
                committedAt = DateTimeOffset.UtcNow
            }),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await fixture.Db.SaveChangesAsync();

        var admin = new OetLearner.Api.Services.AdminService(
            fixture.Db,
            emailOtpService: null!,
            passwordHasher: null!,
            passwordPolicyService: null!,
            timeProvider: TimeProvider.System,
            notifications: null!,
            learnerService: null!,
            vocabularyAudioQueue: fixture.Queue);

        await admin.EnqueueVocabularyAudioBackfillAsync("B01", CancellationToken.None);

        Assert.Equal(2, fixture.Queue.PendingCount);
    }

    [Fact]
    public async Task WorkerSkipsWhenTtsOff()
    {
        await using var fixture = await Fixture.CreateAsync(ttsProvider: "off");
        fixture.Db.VocabularyTerms.Add(new VocabularyTerm
        {
            Id = "VOC-OFF",
            Term = "stethoscope",
            ExamTypeCode = "oet",
            Category = "clinical",
            Status = "active",
        });
        await fixture.Db.SaveChangesAsync();

        await fixture.Worker.ProcessOneAsync(
            new VocabularyAudioJob("VOC-OFF", "stethoscope", null, "en-GB", "batch-x"),
            CancellationToken.None);

        var term = await fixture.Db.VocabularyTerms.AsNoTracking().FirstAsync(t => t.Id == "VOC-OFF");
        Assert.Null(term.AudioMediaAssetId);
        Assert.Null(term.AudioUrl);
        Assert.Empty(fixture.Db.MediaAssets.AsNoTracking().ToList());
    }

    [Fact]
    public async Task RecallTermsRejectNonElevenLabsJobs()
    {
        // A recall job whose ProviderName is not "elevenlabs" (here: the default
        // null) must be rejected by the worker before synthesis.
        await using var fixture = await Fixture.CreateAsync(ttsProvider: "elevenlabs");
        fixture.Db.VocabularyTerms.Add(new VocabularyTerm
        {
            Id = "VOC-RECALL-BLOCKED",
            Term = "anaemia",
            ExamTypeCode = "oet",
            Category = "recall",
            Status = "active",
            RecallSetCodesJson = "[\"2026\"]",
        });
        await fixture.Db.SaveChangesAsync();

        await fixture.Worker.ProcessOneAsync(
            new VocabularyAudioJob("VOC-RECALL-BLOCKED", "anaemia", null, "en-GB", "batch-2"),
            CancellationToken.None);

        var term = await fixture.Db.VocabularyTerms.AsNoTracking().FirstAsync(t => t.Id == "VOC-RECALL-BLOCKED");
        Assert.Null(term.AudioMediaAssetId);
        Assert.Null(term.AudioUrl);
    }

    [Fact]
    public async Task RecallTermsUseElevenLabsStorageKeyAndProvenance()
    {
        await using var fixture = await Fixture.CreateAsync(ttsProvider: "elevenlabs");
        fixture.Db.VocabularyTerms.Add(new VocabularyTerm
        {
            Id = "VOC-RECALL-001",
            Term = "haemorrhage",
            ExamTypeCode = "oet",
            Category = "recall",
            Status = "active",
            RecallSetCodesJson = "[\"2026\"]",
        });
        await fixture.Db.SaveChangesAsync();

        await fixture.Worker.ProcessOneAsync(
            new VocabularyAudioJob(
                TermId: "VOC-RECALL-001",
                Text: "haemorrhage",
                Voice: "voice-abc",
                Locale: "en-GB",
                BatchId: "batch-3",
                ModelVariant: "eleven_multilingual_v2",
                ProviderName: "elevenlabs"),
            CancellationToken.None);

        var term = await fixture.Db.VocabularyTerms.AsNoTracking().FirstAsync(t => t.Id == "VOC-RECALL-001");
        Assert.Equal("elevenlabs", term.AudioProvider);
        Assert.Equal("voice-abc", term.AudioVoice);
        Assert.StartsWith("recalls/audio/2026-voc-recall-001-", term.AudioUrl, StringComparison.Ordinal);
        Assert.NotNull(term.AudioMediaAssetId);
    }

    [Fact]
    public async Task RecallTermWithNonElevenLabsExistingAudioRegenerates()
    {
        await using var fixture = await Fixture.CreateAsync(ttsProvider: "elevenlabs");
        var storage = fixture.Root.GetRequiredService<IFileStorage>();
        const string existingKey = "recalls/audio/old-voc-recall-002.wav";
        await using (var stream = new MemoryStream(new byte[] { 1, 2, 3 }))
        {
            await storage.WriteAsync(existingKey, stream, CancellationToken.None);
        }

        fixture.Db.VocabularyTerms.Add(new VocabularyTerm
        {
            Id = "VOC-RECALL-002",
            Term = "tachycardia",
            ExamTypeCode = "oet",
            Category = "recall",
            Status = "active",
            RecallSetCodesJson = "[\"old\"]",
            AudioMediaAssetId = "media-old",
            AudioUrl = existingKey,
            AudioProvider = "mock",
            AudioVoice = "old-voice",
        });
        fixture.Db.MediaAssets.Add(new MediaAsset
        {
            Id = "media-old",
            OriginalFilename = "old.wav",
            MimeType = "audio/wav",
            Format = "wav",
            SizeBytes = 3,
            StoragePath = existingKey,
            Status = MediaAssetStatus.Ready,
            Sha256 = "oldsha",
            MediaKind = "audio",
            UploadedBy = "test",
            UploadedAt = DateTimeOffset.UtcNow,
            ProcessedAt = DateTimeOffset.UtcNow,
        });
        await fixture.Db.SaveChangesAsync();

        await fixture.Worker.ProcessOneAsync(
            new VocabularyAudioJob(
                TermId: "VOC-RECALL-002",
                Text: "tachycardia",
                Voice: "voice-new",
                Locale: "en-GB",
                BatchId: "batch-4",
                ModelVariant: "eleven_multilingual_v2",
                ProviderName: "elevenlabs"),
            CancellationToken.None);

        var term = await fixture.Db.VocabularyTerms.AsNoTracking().FirstAsync(t => t.Id == "VOC-RECALL-002");
        Assert.Equal("elevenlabs", term.AudioProvider);
        Assert.Equal("voice-new", term.AudioVoice);
        Assert.NotEqual(existingKey, term.AudioUrl);
    }

    [Fact]
    public async Task ResumeVocabularyAudio_RecallTerms_EnqueuesElevenLabsJobsAndCreatesRunningBatch()
    {
        // Regression: the Resume button used to enqueue recall jobs with
        // ProviderName=null, which the worker rejects, so audio never generated.
        // It must now route recall terms through the recalls batch path
        // (ProviderName=elevenlabs + a running AudioRegenerationBatch).
        await using var fixture = await Fixture.CreateAsync(ttsProvider: "elevenlabs");
        fixture.Db.VocabularyTerms.AddRange(
            new VocabularyTerm
            {
                Id = "VOC-R1", Term = "amlodipine", ExamTypeCode = "oet",
                Category = "recall", Status = "draft", RecallSetCodesJson = "[\"2026\"]",
            },
            new VocabularyTerm
            {
                Id = "VOC-R2", Term = "cholecystitis", ExamTypeCode = "oet",
                Category = "recall", Status = "draft", RecallSetCodesJson = "[\"2026\"]",
            });
        await fixture.Db.SaveChangesAsync();

        var admin = new OetLearner.Api.Services.AdminService(
            fixture.Db,
            emailOtpService: null!,
            passwordHasher: null!,
            passwordPolicyService: null!,
            timeProvider: TimeProvider.System,
            notifications: null!,
            learnerService: null!,
            vocabularyAudioQueue: fixture.Queue,
            voiceDesignRegeneration: fixture.Regen);

        var result = await admin.ResumeVocabularyAudioAsync(CancellationToken.None);

        var enqueued = (int)result.GetType().GetProperty("enqueued")!.GetValue(result)!;
        Assert.Equal(2, enqueued);
        Assert.Equal(2, fixture.Queue.PendingCount);

        var batch = await fixture.Db.AudioRegenerationBatches.AsNoTracking()
            .SingleAsync(b => b.AudioType == "recalls");
        Assert.Equal("running", batch.Status);
        Assert.Equal("elevenlabs", batch.ProviderName);
        Assert.Equal(2, batch.TotalItems);
    }

    [Fact]
    public async Task RecallRegeneration_SameIdentity_UsesStableKey_NoChurn()
    {
        // Regression for the production regeneration-loop / orphan-accumulation bug:
        // regenerating the same recall term with the same voice+model must resolve
        // to the SAME storage key even though ElevenLabs returns different bytes,
        // so it overwrites in place instead of spawning a new object each time.
        await using var fixture = await Fixture.CreateAsync(ttsProvider: "elevenlabs", varyingBytes: true);
        fixture.Db.VocabularyTerms.Add(new VocabularyTerm
        {
            Id = "VOC-IDEMP", Term = "sepsis", ExamTypeCode = "oet",
            Category = "recall", Status = "active", RecallSetCodesJson = "[\"2026\"]",
        });
        await fixture.Db.SaveChangesAsync();

        var job = new VocabularyAudioJob(
            TermId: "VOC-IDEMP", Text: "sepsis", Voice: "voice-abc", Locale: "en-GB",
            BatchId: "batch-1", ModelVariant: "eleven_multilingual_v2", ProviderName: "elevenlabs");

        await fixture.Worker.ProcessOneAsync(job, CancellationToken.None);
        var firstUrl = (await fixture.Db.VocabularyTerms.AsNoTracking().FirstAsync(t => t.Id == "VOC-IDEMP")).AudioUrl;

        // Force a real re-synthesis (different bytes) with the SAME identity.
        await fixture.Worker.ProcessOneAsync(job with { ForceRegenerate = true }, CancellationToken.None);
        var secondUrl = (await fixture.Db.VocabularyTerms.AsNoTracking().FirstAsync(t => t.Id == "VOC-IDEMP")).AudioUrl;

        // Stable, deterministic key across regenerations — the core of the fix.
        Assert.Equal(firstUrl, secondUrl);

        // And exactly one audio object on disk for this term (no orphan pile-up).
        var dir = Path.Combine(fixture.StorageRoot, "storage", "recalls", "audio");
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir) : Array.Empty<string>();
        Assert.Single(files.Where(f => Path.GetFileName(f).Contains("voc-idemp", StringComparison.Ordinal)));
    }

    // ─────────────────────────────────────────────────────────────────────

    private sealed class Fixture : IAsyncDisposable
    {
        public required LearnerDbContext Db { get; init; }
        public required IVocabularyAudioQueue Queue { get; init; }
        public required VocabularyAudioWorker Worker { get; init; }
        public required IVoiceDesignRegenerationService Regen { get; init; }
        public required string StorageRoot { get; init; }
        public required IServiceProvider Root { get; init; }

        public static async Task<Fixture> CreateAsync(string ttsProvider, bool varyingBytes = false)
        {
            var storageRoot = Path.Combine(Path.GetTempPath(), $"vocab-audio-{Guid.NewGuid():N}");
            Directory.CreateDirectory(storageRoot);
            var dbName = $"vocab-audio-{Guid.NewGuid():N}";

            var services = new ServiceCollection();
            services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
            services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment(storageRoot));
            services.AddSingleton<IOptions<StorageOptions>>(
                Options.Create(new StorageOptions { LocalRootPath = "storage" }));
            services.AddScoped<IFileStorage, LocalFileStorage>();

            services.AddDbContext<LearnerDbContext>(o => o.UseInMemoryDatabase(dbName));

            services.AddSingleton<IConversationOptionsProvider>(
                new StubOptionsProvider(new ConversationOptions
                {
                    TtsProvider = ttsProvider,
                    // Present so the recalls regeneration path passes its API-key
                    // guard in tests; the stub TTS providers ignore it.
                    ElevenLabsApiKey = "test-elevenlabs-key",
                    ElevenLabsDefaultVoiceId = "voice-default",
                    ElevenLabsModel = "eleven_multilingual_v2",
                }));
            // ElevenLabs is the only TTS provider. Register the test double for
            // any non-"off" setting (the selector resolves "auto"/"elevenlabs"
            // to it); "off" registers nothing so the selector returns null.
            if (varyingBytes)
            {
                // Singleton so its call-counter persists across the worker's per-job
                // scopes — each synthesis returns DIFFERENT bytes, simulating
                // ElevenLabs' non-determinism.
                services.AddSingleton<IConversationTtsProvider, VaryingElevenLabsTtsProvider>();
            }
            else if (!string.Equals(ttsProvider, "off", StringComparison.OrdinalIgnoreCase))
            {
                services.AddScoped<IConversationTtsProvider, TestElevenLabsTtsProvider>();
            }
            services.AddScoped<IConversationTtsProviderSelector, ConversationTtsProviderSelector>();

            services.AddSingleton<IVocabularyAudioQueue, VocabularyAudioQueue>();
            services.AddSingleton<VocabularyAudioWorker>();
            services.AddScoped<IVoiceDesignRegenerationService, VoiceDesignRegenerationService>();

            var sp = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = false,
            });
            var assertionScope = sp.CreateAsyncScope();
            var db = assertionScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();
            var queue = sp.GetRequiredService<IVocabularyAudioQueue>();
            var worker = sp.GetRequiredService<VocabularyAudioWorker>();
            // Resolve from the same scope as Db so they share one DbContext.
            var regen = assertionScope.ServiceProvider.GetRequiredService<IVoiceDesignRegenerationService>();
            return new Fixture
            {
                Db = db,
                Queue = queue,
                Worker = worker,
                Regen = regen,
                StorageRoot = storageRoot,
                Root = sp,
                Scope = assertionScope,
            };
        }

        public required AsyncServiceScope Scope { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Scope.DisposeAsync();
            if (Root is IAsyncDisposable ad) await ad.DisposeAsync();
            else if (Root is IDisposable d) d.Dispose();
            try { if (Directory.Exists(StorageRoot)) Directory.Delete(StorageRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    private sealed class StubOptionsProvider(ConversationOptions opts) : IConversationOptionsProvider
    {
        public Task<ConversationOptions> GetAsync(CancellationToken ct = default) => Task.FromResult(opts);
        public void Invalidate() { }
    }

    private sealed class TestWebHostEnvironment(string root) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestElevenLabsTtsProvider : IConversationTtsProvider
    {
        public string Name => "elevenlabs";
        public bool IsConfigured => true;

        public Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct)
            => Task.FromResult(new ConversationTtsResult(
                new byte[] { 0x49, 0x44, 0x33, 1, 2, 3 },
                "audio/mpeg",
                500,
                Name,
                "test elevenlabs"));
    }

    private sealed class VaryingElevenLabsTtsProvider : IConversationTtsProvider
    {
        private int _counter;
        public string Name => "elevenlabs";
        public bool IsConfigured => true;

        public Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct)
        {
            var n = Interlocked.Increment(ref _counter);
            // Different bytes per call → different sha. Under the old byte-sha key
            // scheme this produced a brand-new storage object every regeneration.
            var bytes = new byte[] { 0x49, 0x44, 0x33, (byte)n, (byte)(n * 7), (byte)(n * 13) };
            return Task.FromResult(new ConversationTtsResult(bytes, "audio/mpeg", 500, Name, "varying"));
        }
    }
}
