using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiTools;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// Security boundary for the AI Learning Companion (docs/ai-learning-companion/).
///
/// <para>
/// The assistant orchestrator that serves learners shares its tool substrate with
/// the admin/developer assistant, whose tools include <c>run_command</c>,
/// <c>deploy</c>, <c>write_file</c>, <c>git_operations</c> and
/// <c>query_database</c>. Tool resolution is data-driven: an active
/// <see cref="AiFeatureToolGrant"/> row plus an active <see cref="AiTool"/> row is
/// all it takes. Deny-by-default protects the common case, but a single mistaken
/// admin grant row would otherwise expose a developer tool to every learner.
/// </para>
///
/// <para>
/// These tests assert the code-level guard in
/// <see cref="AiToolRegistry.ResolveForFeatureAsync"/>: for a learner-facing
/// feature code, only allowlisted tools resolve — <b>even when a grant row
/// explicitly says otherwise</b>.
/// </para>
/// </summary>
public sealed class CompanionLearnerToolBoundaryTests : IAsyncDisposable
{
    /// <summary>
    /// Privileged tools from <c>Services/AiAssistant/Tools/</c>. None of these may
    /// ever resolve for a learner-facing feature code.
    /// </summary>
    public static readonly string[] DeveloperToolCodes =
    [
        "run_command",
        "deploy",
        "write_file",
        "git_operations",
        "query_database",
        "read_file",
        "list_directory",
        "search_codebase",
        "retrieve_codebase",
        "web_search",
    ];

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public CompanionLearnerToolBoundaryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    public static TheoryData<string> LearnerFacingFeatures()
    {
        var data = new TheoryData<string>();
        foreach (var code in new[]
                 {
                     AiFeatureCodes.AiAssistantLearner,
                     AiFeatureCodes.CompanionChat,
                     AiFeatureCodes.CompanionRetrieval,
                     AiFeatureCodes.CompanionAction,
                 })
        {
            data.Add(code);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LearnerFacingFeatures))]
    public async Task DeveloperTools_NeverResolve_ForLearnerFacingFeature_EvenWhenGranted(string featureCode)
    {
        // Arrange: the worst case — someone actively grants every developer tool
        // to a learner-facing feature and marks both rows active.
        await SeedToolsAsync(DeveloperToolCodes);
        await GrantAsync(featureCode, DeveloperToolCodes);

        var registry = BuildRegistry(DeveloperToolCodes);

        // Act
        var resolved = await registry.ResolveForFeatureAsync(featureCode, CancellationToken.None);

        // Assert: the grant rows are ignored.
        Assert.Empty(resolved);
    }

    [Theory]
    [MemberData(nameof(LearnerFacingFeatures))]
    public async Task LearnerSafeTools_StillResolve_WhenGranted(string featureCode)
    {
        const string safeTool = "lookup_rulebook_rule";

        await SeedToolsAsync([safeTool]);
        await GrantAsync(featureCode, [safeTool]);

        var registry = BuildRegistry([safeTool]);

        var resolved = await registry.ResolveForFeatureAsync(featureCode, CancellationToken.None);

        Assert.Single(resolved);
        Assert.Equal(safeTool, resolved[0].Code);
    }

    [Theory]
    [MemberData(nameof(LearnerFacingFeatures))]
    public async Task MixedGrants_KeepSafeTool_DropDeveloperTool(string featureCode)
    {
        string[] tools = ["lookup_vocabulary_term", "run_command"];

        await SeedToolsAsync(tools);
        await GrantAsync(featureCode, tools);

        var registry = BuildRegistry(tools);

        var resolved = await registry.ResolveForFeatureAsync(featureCode, CancellationToken.None);

        Assert.Single(resolved);
        Assert.Equal("lookup_vocabulary_term", resolved[0].Code);
    }

    [Fact]
    public async Task AdminFeature_IsUnaffected_ByTheLearnerGuard()
    {
        // The guard must not regress the developer assistant: admin keeps its tools.
        await SeedToolsAsync(DeveloperToolCodes);
        await GrantAsync(AiFeatureCodes.AiAssistantAdmin, DeveloperToolCodes);

        var registry = BuildRegistry(DeveloperToolCodes);

        var resolved = await registry.ResolveForFeatureAsync(
            AiFeatureCodes.AiAssistantAdmin, CancellationToken.None);

        Assert.Equal(DeveloperToolCodes.Length, resolved.Count);
    }

    [Fact]
    public void LearnerSafeAllowlist_ContainsNoDeveloperTool()
    {
        // Guards against someone widening the allowlist by pasting in a dev tool.
        foreach (var devTool in DeveloperToolCodes)
        {
            Assert.False(
                AiToolRegistry.IsLearnerSafeTool(devTool),
                $"'{devTool}' is a developer tool and must never be on the learner-safe allowlist.");
        }
    }

    [Fact]
    public void CompanionFeatureCodes_AreTreatedAsLearnerFacing()
    {
        Assert.True(AiToolRegistry.IsLearnerFacingFeature(AiFeatureCodes.CompanionChat));
        Assert.True(AiToolRegistry.IsLearnerFacingFeature(AiFeatureCodes.CompanionRetrieval));
        Assert.True(AiToolRegistry.IsLearnerFacingFeature(AiFeatureCodes.CompanionAction));
        Assert.True(AiToolRegistry.IsLearnerFacingFeature(AiFeatureCodes.AiAssistantLearner));

        // Admin and expert are intentionally NOT learner-facing.
        Assert.False(AiToolRegistry.IsLearnerFacingFeature(AiFeatureCodes.AiAssistantAdmin));
        Assert.False(AiToolRegistry.IsLearnerFacingFeature(AiFeatureCodes.AiAssistantExpert));
    }

    // ---------------------------------------------------------------- helpers

    private async Task SeedToolsAsync(IEnumerable<string> toolCodes)
    {
        await using var db = new LearnerDbContext(_options);
        var now = DateTimeOffset.UtcNow;

        foreach (var code in toolCodes)
        {
            db.AiTools.Add(new AiTool
            {
                Id = $"tool-{code}",
                Code = code,
                Name = code,
                Description = $"Test tool {code}",
                Category = AiToolCategory.Read,
                JsonSchemaArgs = "{\"type\":\"object\",\"properties\":{}}",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task GrantAsync(string featureCode, IEnumerable<string> toolCodes)
    {
        await using var db = new LearnerDbContext(_options);
        var now = DateTimeOffset.UtcNow;

        foreach (var code in toolCodes)
        {
            db.AiFeatureToolGrants.Add(new AiFeatureToolGrant
            {
                Id = $"grant-{featureCode}-{code}",
                FeatureCode = featureCode,
                ToolCode = code,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync();
    }

    private AiToolRegistry BuildRegistry(IEnumerable<string> toolCodes)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_options);
        services.AddScoped(_ => new LearnerDbContext(_options));

        var executors = toolCodes.Select(code => (IAiToolExecutor)new StubExecutor(code)).ToList();

        // A fresh cache per registry keeps the grant lookup from leaking between cases.
        return new AiToolRegistry(
            services.BuildServiceProvider(),
            new MemoryCache(new MemoryCacheOptions()),
            new StaticOptionsMonitor<AiToolOptions>(new AiToolOptions()),
            NullLogger<AiToolRegistry>.Instance,
            executors);
    }

    private sealed class StubExecutor(string code) : IAiToolExecutor
    {
        public string Code { get; } = code;
        public AiToolCategory Category => AiToolCategory.Read;
        public string JsonSchemaArgs => "{\"type\":\"object\",\"properties\":{}}";

        public Task<AiToolExecutionResult> ExecuteAsync(
            System.Text.Json.JsonElement args, AiToolContext context, CancellationToken ct) =>
            throw new NotSupportedException("Boundary tests never execute a tool.");
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
