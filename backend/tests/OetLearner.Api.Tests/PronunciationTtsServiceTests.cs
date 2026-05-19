using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Conversation.Tts;
using OetLearner.Api.Services.Pronunciation;

namespace OetLearner.Api.Tests;

public sealed class PronunciationTtsServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;
    private readonly InMemoryFileStorage _storage;

    public PronunciationTtsServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new LearnerDbContext(options);
        _db.Database.EnsureCreated();
        _storage = new InMemoryFileStorage();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task GenerateModelAudioAsync_HappyPath_PersistsMediaAndLinksDrill()
    {
        var drill = SeedDrill();
        var audio = System.Text.Encoding.ASCII.GetBytes("FAKE_MP3_BYTES_v1");
        var provider = new FakeTtsProvider("azure", configured: true,
            new ConversationTtsResult(audio, "audio/mpeg", DurationMs: 2400, "azure", "ok"));
        var selector = new FakeSelector(provider);
        var svc = new PronunciationTtsService(
            selector, _storage, _db,
            new FakeEnv(Environments.Development),
            NullLogger<PronunciationTtsService>.Instance);

        var result = await svc.GenerateModelAudioAsync(drill.Id, "Please breathe deeply.", null, CancellationToken.None);

        Assert.Equal("azure", result.ProviderName);
        Assert.Equal(audio.Length, result.Bytes);
        Assert.Equal(2400, result.DurationMs);
        Assert.StartsWith("pronunciation/model-audio/", result.StorageKey);
        Assert.EndsWith(".mp3", result.StorageKey);
        Assert.True(_storage.Exists(result.StorageKey));

        var media = await _db.MediaAssets.SingleAsync();
        Assert.Equal(result.MediaAssetId, media.Id);
        Assert.Equal(result.Sha256, media.Sha256);
        Assert.Equal("audio", media.MediaKind);
        Assert.Equal(MediaAssetStatus.Ready, media.Status);

        var updated = await _db.PronunciationDrills.SingleAsync(d => d.Id == drill.Id);
        Assert.Equal(media.Id, updated.AudioModelAssetId);
    }

    [Fact]
    public async Task GenerateModelAudioAsync_SameInputs_DedupsByHash()
    {
        var drill = SeedDrill();
        var audio = System.Text.Encoding.ASCII.GetBytes("FAKE_MP3_BYTES_v1");
        var provider = new FakeTtsProvider("azure", configured: true,
            new ConversationTtsResult(audio, "audio/mpeg", 2400, "azure", null));
        var svc = new PronunciationTtsService(
            new FakeSelector(provider), _storage, _db,
            new FakeEnv(Environments.Development),
            NullLogger<PronunciationTtsService>.Instance);

        var first = await svc.GenerateModelAudioAsync(drill.Id, "Same text", null, default);
        var second = await svc.GenerateModelAudioAsync(drill.Id, "Different drill same audio", null, default);

        Assert.Equal(first.MediaAssetId, second.MediaAssetId);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(1, await _db.MediaAssets.CountAsync());
    }

    [Fact]
    public async Task GenerateModelAudioAsync_NoProvider_Throws()
    {
        var drill = SeedDrill();
        var svc = new PronunciationTtsService(
            new FakeSelector(null), _storage, _db,
            new FakeEnv(Environments.Development),
            NullLogger<PronunciationTtsService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.GenerateModelAudioAsync(drill.Id, "anything", null, default));
        Assert.Contains("real TTS provider", ex.Message);
    }

    [Fact]
    public async Task GenerateModelAudioAsync_MockProvider_RefusesEvenInDevelopment()
    {
        var drill = SeedDrill();
        var mock = new FakeTtsProvider("mock", configured: true,
            new ConversationTtsResult([1, 2, 3], "audio/wav", 100, "mock", null));
        var svc = new PronunciationTtsService(
            new FakeSelector(mock), _storage, _db,
            new FakeEnv(Environments.Development),
            NullLogger<PronunciationTtsService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.GenerateModelAudioAsync(drill.Id, "anything", null, default));
        Assert.Contains("real TTS provider", ex.Message);
    }

    [Fact]
    public async Task GenerateModelAudioAsync_EmptyText_Throws()
    {
        var drill = SeedDrill();
        var provider = new FakeTtsProvider("azure", true,
            new ConversationTtsResult([1, 2], "audio/mpeg", 100, "azure", null));
        var svc = new PronunciationTtsService(
            new FakeSelector(provider), _storage, _db,
            new FakeEnv(Environments.Development),
            NullLogger<PronunciationTtsService>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.GenerateModelAudioAsync(drill.Id, "   ", null, default));
    }

    [Fact]
    public async Task GenerateModelAudioAsync_UnknownDrillId_Throws()
    {
        var provider = new FakeTtsProvider("azure", true,
            new ConversationTtsResult([1, 2], "audio/mpeg", 100, "azure", null));
        var svc = new PronunciationTtsService(
            new FakeSelector(provider), _storage, _db,
            new FakeEnv(Environments.Development),
            NullLogger<PronunciationTtsService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.GenerateModelAudioAsync("PRN-missing", "hello", null, default));
    }

    [Fact]
    public async Task GenerateModelAudioAsync_UnsupportedMime_Throws()
    {
        var drill = SeedDrill();
        var provider = new FakeTtsProvider("azure", true,
            new ConversationTtsResult([1, 2], "audio/flac", 100, "azure", null));
        var svc = new PronunciationTtsService(
            new FakeSelector(provider), _storage, _db,
            new FakeEnv(Environments.Development),
            NullLogger<PronunciationTtsService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.GenerateModelAudioAsync(drill.Id, "hello", null, default));
        Assert.Contains("Unsupported", ex.Message);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private PronunciationDrill SeedDrill()
    {
        var d = new PronunciationDrill
        {
            Id = "PRN-test-001",
            Label = "th (voiceless)",
            TargetPhoneme = "theta",
            Profession = "medicine",
            Focus = "phoneme",
            Difficulty = "medium",
            Status = "draft",
            ExampleWordsJson = "[]",
            MinimalPairsJson = "[]",
            SentencesJson = "[]",
            TipsHtml = "",
            OrderIndex = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.PronunciationDrills.Add(d);
        _db.SaveChanges();
        return d;
    }

    private sealed class FakeSelector(IConversationTtsProvider? provider) : IConversationTtsProviderSelector
    {
        public Task<IConversationTtsProvider?> TrySelectAsync(CancellationToken ct = default)
            => Task.FromResult(provider);
        public Task<bool> IsTtsDisabledAsync(CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private sealed class FakeTtsProvider(string name, bool configured, ConversationTtsResult result)
        : IConversationTtsProvider
    {
        public string Name { get; } = name;
        public bool IsConfigured { get; } = configured;
        public Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct)
            => Task.FromResult(result);
    }

    private sealed class FakeEnv(string envName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = envName;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
