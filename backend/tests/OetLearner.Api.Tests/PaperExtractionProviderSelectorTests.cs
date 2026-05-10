using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests;

/// <summary>
/// RW-012 — admin-managed PDF / OCR provider rotation.
///
/// Asserts that <see cref="PaperExtractionProviderSelector"/> resolves the
/// active <see cref="AiProvider"/> row by category (Ocr / PdfExtraction)
/// and ranks by <see cref="AiProvider.FailoverPriority"/> the same way
/// <c>ConversationAsrProviderSelector</c> does for ASR. This is the
/// admin-rotatable hook that lets operators swap OCR / PDF providers
/// from the /admin/ai-providers console without a redeploy.
/// </summary>
public sealed class PaperExtractionProviderSelectorTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public PaperExtractionProviderSelectorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task SelectAsync_ReturnsHighestPriorityActiveOcrProvider()
    {
        await using var db = new LearnerDbContext(_options);
        SeedProvider(db, "tesseract", AiProviderCategory.Ocr, priority: 50, active: true);
        SeedProvider(db, "azure-doc-ai", AiProviderCategory.Ocr, priority: 10, active: true);
        SeedProvider(db, "tesseract-stale", AiProviderCategory.Ocr, priority: 1, active: false);
        await db.SaveChangesAsync();

        var providers = new IPaperExtractionProvider[]
        {
            new FakePaperProvider("tesseract"),
            new FakePaperProvider("azure-doc-ai"),
            new FakePaperProvider("tesseract-stale"),
        };
        var selector = new PaperExtractionProviderSelector(db, providers);

        var resolved = await selector.SelectAsync(AiProviderCategory.Ocr, default);
        Assert.NotNull(resolved);
        Assert.Equal("azure-doc-ai", resolved!.Code);
    }

    [Fact]
    public async Task SelectAsync_ReturnsNullWhenNoActiveProviderForCategory()
    {
        await using var db = new LearnerDbContext(_options);
        SeedProvider(db, "tesseract", AiProviderCategory.Ocr, priority: 10, active: false);
        await db.SaveChangesAsync();

        var providers = new IPaperExtractionProvider[] { new FakePaperProvider("tesseract") };
        var selector = new PaperExtractionProviderSelector(db, providers);

        var resolved = await selector.SelectAsync(AiProviderCategory.PdfExtraction, default);
        Assert.Null(resolved);
    }

    [Fact]
    public async Task SelectAsync_RejectsNonExtractionCategory()
    {
        await using var db = new LearnerDbContext(_options);
        var selector = new PaperExtractionProviderSelector(db, Array.Empty<IPaperExtractionProvider>());
        await Assert.ThrowsAsync<ArgumentException>(
            () => selector.SelectAsync(AiProviderCategory.TextChat, default));
    }

    private static void SeedProvider(
        LearnerDbContext db,
        string code,
        AiProviderCategory category,
        int priority,
        bool active)
    {
        db.AiProviders.Add(new AiProvider
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = code,
            Name = code,
            Dialect = AiProviderDialect.OpenAiCompatible,
            Category = category,
            BaseUrl = "https://example.test",
            EncryptedApiKey = string.Empty,
            ApiKeyHint = "",
            DefaultModel = "",
            FailoverPriority = priority,
            IsActive = active,
        });
    }

    private sealed class FakePaperProvider(string code) : IPaperExtractionProvider
    {
        public string Code { get; } = code;
        public Task<PaperExtractionResult> ExtractAsync(
            ReadOnlyMemory<byte> bytes,
            string fileName,
            string? mimeType,
            CancellationToken ct)
            => Task.FromResult(new PaperExtractionResult(
                Text: "ok",
                StructuredJson: null,
                PageCount: 1,
                ProviderCode: Code,
                DurationMs: 1));
    }
}
