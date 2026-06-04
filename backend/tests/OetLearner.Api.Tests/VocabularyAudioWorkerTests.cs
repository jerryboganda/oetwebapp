using Microsoft.AspNetCore.Hosting;
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
                SourceProvenance = "batch=B1;source=admin-vocabulary-import",
            },
            new VocabularyTerm
            {
                Id = "VOC-B", Term = "beta", ExamTypeCode = "oet", Category = "c",
                Status = "active",
                SourceProvenance = "batch=B1;source=admin-vocabulary-import",
            },
            new VocabularyTerm
            {
                Id = "VOC-C", Term = "gamma", ExamTypeCode = "oet", Category = "c",
                Status = "active",
                SourceProvenance = "batch=B1;source=admin-vocabulary-import",
                AudioMediaAssetId = "MA-existing",
                AudioUrl = "/media/file/existing.mp3",
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

        await admin.EnqueueVocabularyAudioBackfillAsync("B1", CancellationToken.None);

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
        await using var fixture = await Fixture.CreateAsync(ttsProvider: "mock");
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

    // ─────────────────────────────────────────────────────────────────────

    private sealed class Fixture : IAsyncDisposable
    {
        public required LearnerDbContext Db { get; init; }
        public required IVocabularyAudioQueue Queue { get; init; }
        public required VocabularyAudioWorker Worker { get; init; }
        public required string StorageRoot { get; init; }
        public required IServiceProvider Root { get; init; }

        public static async Task<Fixture> CreateAsync(string ttsProvider)
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
                new StubOptionsProvider(new ConversationOptions { TtsProvider = ttsProvider }));
            if (string.Equals(ttsProvider, "elevenlabs", StringComparison.OrdinalIgnoreCase))
            {
                services.AddScoped<IConversationTtsProvider, TestElevenLabsTtsProvider>();
            }
            else
            {
                services.AddScoped<IConversationTtsProvider, MockConversationTtsProvider>();
            }
            services.AddScoped<IConversationTtsProviderSelector, ConversationTtsProviderSelector>();

            services.AddSingleton<IVocabularyAudioQueue, VocabularyAudioQueue>();
            services.AddSingleton<VocabularyAudioWorker>();

            var sp = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = false,
            });
            var assertionScope = sp.CreateAsyncScope();
            var db = assertionScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();
            var queue = sp.GetRequiredService<IVocabularyAudioQueue>();
            var worker = sp.GetRequiredService<VocabularyAudioWorker>();
            return new Fixture
            {
                Db = db,
                Queue = queue,
                Worker = worker,
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
}
