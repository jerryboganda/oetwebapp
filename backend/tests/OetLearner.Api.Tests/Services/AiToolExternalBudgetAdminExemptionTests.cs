using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiTools;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// Owner directive 2026-09-23 — the per-user daily external-network tool
/// budget still bounds learner features, but admin-side features (the
/// admin/expert assistants, admin.* drafts) are exempt and run past it.
/// </summary>
public sealed class AiToolExternalBudgetAdminExemptionTests
{
    private const string ToolCode = "web.fetch";

    private static (LearnerDbContext Db, AiToolInvoker Invoker) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new LearnerDbContext(options);

        var settings = TestRuntimeSettingsProvider.Base()
            with
            {
                AiGateway = TestRuntimeSettingsProvider.DefaultAiGateway()
                    with { ExternalNetworkPerUserDailyCalls = 1 },
            };

        var invoker = new AiToolInvoker(
            new SingleToolRegistry(),
            new ServiceCollection().BuildServiceProvider(),
            db,
            new TestRuntimeSettingsProvider(settings),
            NullLogger<AiToolInvoker>.Instance,
            [new StubExternalExecutor()]);

        return (db, invoker);
    }

    private static async Task SeedSuccessfulInvocationAsync(LearnerDbContext db, string userId)
    {
        db.AiToolInvocations.Add(new AiToolInvocation
        {
            Id = Guid.NewGuid().ToString("N"),
            AiUsageRecordId = "usage-seed",
            FeatureCode = AiFeatureCodes.ReadingExplanation,
            ToolCode = ToolCode,
            Category = AiToolCategory.ExternalNetwork,
            UserId = userId,
            TurnIndex = 0,
            ArgsHash = "seed",
            ResultHash = "seed",
            Outcome = AiToolOutcome.Success,
            LatencyMs = 5,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task LearnerFeature_IsDenied_AfterTheDailyExternalBudgetIsSpent()
    {
        var (db, invoker) = Build();
        await SeedSuccessfulInvocationAsync(db, "user-001");

        var result = await invoker.InvokeAsync(
            ToolCode,
            JsonDocument.Parse("{}").RootElement.Clone(),
            new AiToolContext(AiFeatureCodes.ReadingExplanation, "user-001", null, "usage-1", 0),
            default);

        Assert.Equal(AiToolOutcome.BudgetExceeded, result.Outcome);
        Assert.Equal("external_budget", result.ErrorCode);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task AdminFeature_RunsPastTheDailyExternalBudget()
    {
        var (db, invoker) = Build();
        await SeedSuccessfulInvocationAsync(db, "user-001");

        var result = await invoker.InvokeAsync(
            ToolCode,
            JsonDocument.Parse("{}").RootElement.Clone(),
            new AiToolContext(AiFeatureCodes.AdminWritingDraft, "user-001", null, "usage-2", 0),
            default);

        Assert.Equal(AiToolOutcome.Success, result.Outcome);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task ExpertAssistant_RunsPastTheDailyExternalBudget()
    {
        var (db, invoker) = Build();
        await SeedSuccessfulInvocationAsync(db, "user-001");

        var result = await invoker.InvokeAsync(
            ToolCode,
            JsonDocument.Parse("{}").RootElement.Clone(),
            new AiToolContext(AiFeatureCodes.AiAssistantExpert, "user-001", null, "usage-3", 0),
            default);

        Assert.Equal(AiToolOutcome.Success, result.Outcome);
        await db.DisposeAsync();
    }

    private sealed class SingleToolRegistry : IAiToolRegistry
    {
        public Task<IReadOnlyList<AiToolDefinition>> ResolveForFeatureAsync(string featureCode, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AiToolDefinition>>(
            [
                new AiToolDefinition(
                    ToolCode, "Web Fetch", "Fetches a page.", AiToolCategory.ExternalNetwork, "{}"),
            ]);

        public bool IsKnownToolCode(string toolCode) => toolCode == ToolCode;
        public void InvalidateFeature(string featureCode) { }
        public Task SeedCatalogAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubExternalExecutor : IAiToolExecutor
    {
        public string Code => ToolCode;
        public AiToolCategory Category => AiToolCategory.ExternalNetwork;
        public string JsonSchemaArgs => "{}";

        public Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
            => Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.Success, JsonDocument.Parse("{\"ok\":true}").RootElement.Clone()));
    }
}
