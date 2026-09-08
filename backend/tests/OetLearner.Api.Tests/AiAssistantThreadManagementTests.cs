using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Hubs;
using OetLearner.Api.Services.AiAssistant;

namespace OetLearner.Api.Tests;

/// <summary>
/// Conversation management + per-thread UBAG model override for the floating
/// assistant. Rename and model-set are ownership-checked (a foreign id reads
/// as 404, never 403), titles are length-guarded, and model ids are validated
/// against the UBAG allowlist (+ board composite) by the endpoint — here the
/// orchestrator contract is pinned: set, clear, and foreign-thread refusal.
/// Attachment guardrails (fail-closed caps) are pinned too: over-count,
/// over-size, wrong mime, and over-long document text all reject.
/// </summary>
public sealed class AiAssistantThreadManagementTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public AiAssistantThreadManagementTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task RenameThread_UpdatesTitle_ForOwnerOnly()
    {
        var orchestrator = BuildOrchestrator();
        var thread = await orchestrator.CreateThreadAsync("user-1", "admin", "Old", CancellationToken.None);

        Assert.True(await orchestrator.RenameThreadAsync(thread.Id, "user-1", "  New title  ", CancellationToken.None));

        await using var db = new LearnerDbContext(_options);
        var row = await db.AiAssistantThreads.FirstAsync(t => t.Id == thread.Id);
        Assert.Equal("New title", row.Title);
    }

    [Fact]
    public async Task RenameThread_RejectsForeignThread_AndBadTitles()
    {
        var orchestrator = BuildOrchestrator();
        var thread = await orchestrator.CreateThreadAsync("user-1", "admin", "Old", CancellationToken.None);

        Assert.False(await orchestrator.RenameThreadAsync(thread.Id, "user-2", "New", CancellationToken.None));
        Assert.False(await orchestrator.RenameThreadAsync(thread.Id, "user-1", "   ", CancellationToken.None));
        Assert.False(await orchestrator.RenameThreadAsync(thread.Id, "user-1", new string('x', 257), CancellationToken.None));
        Assert.False(await orchestrator.RenameThreadAsync("missing", "user-1", "New", CancellationToken.None));
    }

    [Fact]
    public async Task SetThreadModel_SetsAndClears_ForOwnerOnly()
    {
        var orchestrator = BuildOrchestrator();
        var thread = await orchestrator.CreateThreadAsync("user-1", "admin", null, CancellationToken.None);

        Assert.True(await orchestrator.SetThreadModelAsync(thread.Id, "user-1", "chatgpt_web", CancellationToken.None));
        await using (var db = new LearnerDbContext(_options))
        {
            Assert.Equal("chatgpt_web", (await db.AiAssistantThreads.FirstAsync(t => t.Id == thread.Id)).ModelOverride);
        }

        Assert.True(await orchestrator.SetThreadModelAsync(thread.Id, "user-1", null, CancellationToken.None));
        await using (var db = new LearnerDbContext(_options))
        {
            Assert.Null((await db.AiAssistantThreads.FirstAsync(t => t.Id == thread.Id)).ModelOverride);
        }

        Assert.False(await orchestrator.SetThreadModelAsync(thread.Id, "user-2", "chatgpt_web", CancellationToken.None));
        Assert.False(await orchestrator.SetThreadModelAsync(thread.Id, "user-1", new string('x', 129), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAndListThreads_CarryModelOverride_ToTheClient()
    {
        var orchestrator = BuildOrchestrator();
        var thread = await orchestrator.CreateThreadAsync("user-1", "admin", null, CancellationToken.None);
        Assert.Null(thread.ModelOverride);

        Assert.True(await orchestrator.SetThreadModelAsync(thread.Id, "user-1", "deepseek_web|Vision", CancellationToken.None));

        var rows = await orchestrator.ListThreadsAsync("user-1", 0, 20, CancellationToken.None);
        Assert.Equal("deepseek_web|Vision", Assert.Single(rows).ModelOverride);
    }

    [Fact]
    public void AttachmentGuard_RejectsOverCount_Oversize_WrongMime()
    {
        var tooMany = new[] { "data:image/png;base64,AA==", "data:image/png;base64,AA==", "data:image/png;base64,AA==", "data:image/png;base64,AA==" };
        Assert.Null(AiAssistantAttachmentGuard.ParseImages(tooMany, out var countError));
        Assert.NotNull(countError);

        Assert.Null(AiAssistantAttachmentGuard.ParseImages(["data:application/pdf;base64,AA=="], out var mimeError));
        Assert.NotNull(mimeError);

        Assert.Null(AiAssistantAttachmentGuard.ParseImages(["not-a-data-url"], out var shapeError));
        Assert.NotNull(shapeError);
    }

    [Fact]
    public void AttachmentGuard_AcceptsValidImage_AndRejectsOversizeDocument()
    {
        var images = AiAssistantAttachmentGuard.ParseImages(["data:image/png;base64,AA=="], out var imageError);
        Assert.Null(imageError);
        Assert.NotNull(images);
        Assert.Single(images!);
        Assert.Equal("image/png", images![0].MimeType);

        var big = new string('x', AiAssistantAttachmentGuard.MaxDocumentChars + 1);
        Assert.Null(AiAssistantAttachmentGuard.ParseDocument($"notes.pdf|application/pdf|{big}", out var docError));
        Assert.NotNull(docError);

        var document = AiAssistantAttachmentGuard.ParseDocument("notes.pdf|application/pdf|hello", out var okError);
        Assert.Null(okError);
        Assert.Equal("hello", document!.Text);
    }

    private AiAssistantOrchestrator BuildOrchestrator()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(_options);
        services.AddDbContext<LearnerDbContext>(o => o.UseSqlite(_connection));
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IServiceScopeFactory>();
        return new AiAssistantOrchestrator(
            factory,
            new NullAssistantGateway(),
            new NullToolRegistry(),
            new NullToolInvoker(),
            new NullPromptProvider(),
            new NullSettingsProvider(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AiAssistantOrchestrator>.Instance);
    }

    private sealed class NullAssistantGateway : IAiAssistantGateway
    {
        public async IAsyncEnumerable<LlmStreamChunk> StreamCompleteWithToolsAsync(
            string featureCode, string? userId, List<LlmMessage> messages,
            IReadOnlyList<OetLearner.Api.Services.AiTools.AiToolDefinition> tools,
            string? modelOverride,
            CancellationToken ct,
            IReadOnlyList<OetLearner.Api.Services.Rulebook.AiProviderImageAttachment>? imageAttachments = null,
            OetLearner.Api.Services.Rulebook.AiProviderDocumentAttachment? documentAttachment = null)
        {
            await Task.Yield();
            yield break;
        }
    }

    private sealed class NullToolRegistry : OetLearner.Api.Services.AiTools.IAiToolRegistry
    {
        public Task<IReadOnlyList<OetLearner.Api.Services.AiTools.AiToolDefinition>> ResolveForFeatureAsync(string featureCode, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<OetLearner.Api.Services.AiTools.AiToolDefinition>>(Array.Empty<OetLearner.Api.Services.AiTools.AiToolDefinition>());
        public bool IsKnownToolCode(string toolCode) => false;
        public void InvalidateFeature(string featureCode) { }
        public Task SeedCatalogAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NullToolInvoker : OetLearner.Api.Services.AiTools.IAiToolInvoker
    {
        public Task<OetLearner.Api.Services.AiTools.AiToolExecutionResult> InvokeAsync(string toolCode, System.Text.Json.JsonElement argsJson, OetLearner.Api.Services.AiTools.AiToolContext ctx, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<OetLearner.Api.Services.AiTools.AiToolExecutionResult> InvokeAsync(OetLearner.Api.Services.Rulebook.AiToolCall call, OetLearner.Api.Services.AiTools.AiToolContext ctx, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class NullPromptProvider : OetLearner.Api.Services.AiAssistant.SystemPrompts.ISystemPromptProvider
    {
        public string GetSystemPrompt(string role, string userId) => "prompt";
    }

    private sealed class NullSettingsProvider : OetLearner.Api.Services.Settings.IRuntimeSettingsProvider
    {
        public OetLearner.Api.Services.Settings.RuntimeSettingsSnapshot? CurrentSnapshot => null;
        public Task<OetLearner.Api.Services.Settings.RuntimeSettingsSnapshot> GetSnapshotAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<OetLearner.Api.Services.Settings.EffectiveSettings> GetAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<OetLearner.Api.Domain.RuntimeSettingsRow> GetRawAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public void Invalidate() { }
        public string Protect(string plain) => throw new NotSupportedException();
        public string? Unprotect(string? cipher) => null;
    }
}
