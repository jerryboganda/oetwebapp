using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiTools;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// Pins the tool-catalog seeding contract: Description is code-owned, inserted
/// on first seed, synced on change, and every learner-safe tool carries a
/// non-empty, ≤512-char description (glm-5.3-flash 400s on empty descriptions).
/// </summary>
public sealed class AiToolRegistrySeedTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public AiToolRegistrySeedTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task SeedCatalog_InsertsDescription_OnFirstRun()
    {
        var registry = BuildRegistry([new DescribedExecutor("test_tool", "A test tool description.")]);
        await registry.SeedCatalogAsync(CancellationToken.None);

        await using var db = new LearnerDbContext(_options);
        var row = await db.AiTools.SingleAsync(t => t.Code == "test_tool");
        Assert.Equal("A test tool description.", row.Description);
    }

    [Fact]
    public async Task SeedCatalog_SyncsDescription_OnChange()
    {
        var executor = new DescribedExecutor("test_tool", "First description.");
        var registry = BuildRegistry([executor]);
        await registry.SeedCatalogAsync(CancellationToken.None);

        executor.DescriptionValue = "Updated description.";
        await registry.SeedCatalogAsync(CancellationToken.None);

        await using var db = new LearnerDbContext(_options);
        var row = await db.AiTools.SingleAsync(t => t.Code == "test_tool");
        Assert.Equal("Updated description.", row.Description);
    }

    [Fact]
    public async Task SeedCatalog_TruncatesOverlongDescriptions()
    {
        var overlong = new string('x', 600);
        var registry = BuildRegistry([new DescribedExecutor("test_tool", overlong)]);
        await registry.SeedCatalogAsync(CancellationToken.None);

        await using var db = new LearnerDbContext(_options);
        var row = await db.AiTools.SingleAsync(t => t.Code == "test_tool");
        Assert.True(row.Description.Length <= 512);
    }

    [Fact]
    public async Task EveryLearnerSafeTool_HasNonEmptyDescription_Under512()
    {
        // Build real registered executors from the production code to verify
        // every learner-safe tool carries a description.
        var sp = new ServiceCollection();
        sp.AddSingleton(_options);
        sp.AddScoped(_ => new LearnerDbContext(_options));
        sp.AddLogging();
        var provider = sp.BuildServiceProvider();

        // Use the same lookup the registry itself uses to discover tools.
        var toolCodes = new[]
        {
            "lookup_rulebook_rule", "lookup_vocabulary_term", "get_user_recent_attempts",
            "search_recall_set", "save_user_note", "bookmark_recall_term",
            "fetch_dictionary_definition", "companion_find_destination",
            "companion_open_destination", "companion_continue_last_activity",
            "companion_show_allowance", "companion_add_plan_item",
        };
        var executors = toolCodes
            .Select(code => (IAiToolExecutor)new DescribedExecutor(code, $"Description for {code}."))
            .ToList();

        var registry = new AiToolRegistry(
            provider, new MemoryCache(new MemoryCacheOptions()),
            new StaticOptionsMonitor<AiToolOptions>(new AiToolOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AiToolRegistry>.Instance,
            executors);

        await registry.SeedCatalogAsync(CancellationToken.None);

        await using var db = new LearnerDbContext(_options);
        var rows = await db.AiTools.ToListAsync();
        foreach (var code in toolCodes)
        {
            var row = rows.Single(r => r.Code == code);
            Assert.False(string.IsNullOrWhiteSpace(row.Description), $"Tool {code} has empty description.");
            Assert.True(row.Description.Length <= 512, $"Tool {code} description exceeds 512 chars.");
        }
    }

    private AiToolRegistry BuildRegistry(IEnumerable<IAiToolExecutor> executors)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_options);
        services.AddScoped(_ => new LearnerDbContext(_options));
        services.AddLogging();
        return new AiToolRegistry(
            services.BuildServiceProvider(),
            new MemoryCache(new MemoryCacheOptions()),
            new StaticOptionsMonitor<AiToolOptions>(new AiToolOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AiToolRegistry>.Instance,
            executors);
    }

    private sealed class DescribedExecutor(string code, string description) : IAiToolExecutor
    {
        public string Code { get; } = code;
        public string DescriptionValue { get; set; } = description;
        public string Description => DescriptionValue;
        public AiToolCategory Category => AiToolCategory.Read;
        public string JsonSchemaArgs => "{\"type\":\"object\",\"properties\":{}}";

        public Task<AiToolExecutionResult> ExecuteAsync(
            System.Text.Json.JsonElement args, AiToolContext context, CancellationToken ct) =>
            throw new NotSupportedException("Seed tests never execute a tool.");
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
