using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Hubs;
using OetLearner.Api.Services.AiAssistant;
using OetLearner.Api.Services.AiTools;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// First RunTurnAsync tests in the repo: the ReAct loop is scripted through a
/// fake <see cref="IAiAssistantGateway"/> and a real <see cref="AiToolInvoker"/>
/// over SQLite in-memory, so arg-parse guards, orphan tool rows, the learner
/// iteration cap and the turn/thread context contract are pinned end to end.
/// </summary>
public sealed class AiAssistantOrchestratorTurnTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public AiAssistantOrchestratorTurnTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task MalformedToolCallArgs_AreHandledAsArgsInvalid_WithoutCrashing()
    {
        var gateway = new ScriptedGateway(
            // Iteration 0: the model emits a tool call with unparsable arguments.
            [new LlmToolCallChunk("call-1", "echo_tool", "{not json")],
            // Iteration 1: the model recovers with a final answer.
            [new LlmTextChunk("done")]);
        var invoker = BuildInvoker(out var toolCtxCaptured);
        var orchestrator = BuildOrchestrator(gateway, invoker);

        var thread = await orchestrator.CreateThreadAsync("user-1", "learner", "t", CancellationToken.None);
        var events = new List<AssistantStreamEvent>();
        await foreach (var e in orchestrator.RunTurnAsync(thread.Id, "user-1", "learner", "hi", null, CancellationToken.None))
        {
            events.Add(e);
        }

        // The invoker rejects the malformed args before the tool runs (no crash, no INTERNAL_ERROR).
        Assert.Empty(toolCtxCaptured);

        // A tool-result message carrying the parse error (not a crash) was persisted for the model to recover from.
        await using var db = new LearnerDbContext(_options);
        var toolRow = await db.AiAssistantMessages.FirstOrDefaultAsync(m => m.Role == "tool");
        Assert.NotNull(toolRow);
        Assert.Contains("\"error\"", toolRow!.Content);

        // Final answer persisted.
        var final = await db.AiAssistantMessages.LastAsync(m => m.Role == "assistant");
        Assert.Equal("done", final.Content);
    }

    [Fact]
    public async Task AiToolContext_CarriesThreadId_AndUserMessageTurnId()
    {
        var gateway = new ScriptedGateway(
            [new LlmToolCallChunk("call-1", "echo_tool", "{}")],
            [new LlmTextChunk("ok")]);
        var invoker = BuildInvoker(out var toolCtxCaptured);
        var orchestrator = BuildOrchestrator(gateway, invoker);

        var thread = await orchestrator.CreateThreadAsync("user-1", "learner", "t", CancellationToken.None);
        await foreach (var _ in orchestrator.RunTurnAsync(thread.Id, "user-1", "learner", "hello", null, CancellationToken.None)) { }

        var toolCtx = Assert.Single(toolCtxCaptured);
        Assert.Equal(thread.Id, toolCtx.ThreadId);
        await using var db = new LearnerDbContext(_options);
        var userMsg = await db.AiAssistantMessages.FirstAsync(m => m.Role == "user");
        Assert.Equal(userMsg.Id, toolCtx.TurnId);
    }

    [Fact]
    public async Task LearnerIterationCap_IsSix_EvenWhenConfiguredHigher()
    {
        // A gateway that always asks for another tool call.
        var scenarios = Enumerable.Range(0, 25)
            .Select(_ => new List<LlmStreamChunk> { new LlmToolCallChunk("c", "echo_tool", "{}") })
            .ToArray();
        var gateway = new ScriptedGateway(scenarios);
        var orchestrator = BuildOrchestrator(gateway, BuildInvoker(out _));

        var thread = await orchestrator.CreateThreadAsync("user-1", "learner", "t", CancellationToken.None);
        await foreach (var _ in orchestrator.RunTurnAsync(thread.Id, "user-1", "learner", "hi", null, CancellationToken.None)) { }

        Assert.Equal(6, gateway.Calls.Count);
        await using var db = new LearnerDbContext(_options);
        var last = await db.AiAssistantMessages.Where(m => m.Role == "assistant").OrderByDescending(m => m.CreatedAt).FirstAsync();
        Assert.Contains("6 tool-call steps", last.Content);
    }

    [Fact]
    public async Task Continuation_IsMarkedOnIterationsAfterTheFirst()
    {
        var gateway = new ScriptedGateway(
            [new LlmToolCallChunk("c", "echo_tool", "{}")],
            [new LlmTextChunk("ok")]);
        var orchestrator = BuildOrchestrator(gateway, BuildInvoker(out _));

        var thread = await orchestrator.CreateThreadAsync("user-1", "learner", "t", CancellationToken.None);
        await foreach (var _ in orchestrator.RunTurnAsync(thread.Id, "user-1", "learner", "go", null, CancellationToken.None)) { }

        Assert.Equal(2, gateway.Calls.Count);
        Assert.False(gateway.Calls[0].IsContinuation);
        Assert.True(gateway.Calls[1].IsContinuation);
        Assert.Equal(thread.Id, gateway.Calls[0].ConversationKey);
    }

    [Fact]
    public async Task OrphanedToolRows_AtTheHistoryWindowBoundary_AreDropped()
    {
        // Seed: assistant-with-tools, tool result, then a fresh user message;
        // MaxContextMessages=2 keeps only [toolResult, user] at the boundary,
        // so the tool row is orphaned and must not reach the provider.
        var gateway = new ScriptedGateway([new LlmTextChunk("answer")]);
        var orchestrator = BuildOrchestrator(gateway, BuildInvoker(out _),
            TestRuntimeSettingsProvider.FromAiAssistantOptions(new AiAssistantOptions { MaxContextMessages = 2 }));

        var thread = await orchestrator.CreateThreadAsync("user-1", "learner", "t", CancellationToken.None);
        await using (var db = new LearnerDbContext(_options))
        {
            db.AiAssistantMessages.Add(new AiAssistantMessage
            {
                Id = "m-a", ThreadId = thread.Id, Role = "assistant", Content = null,
                ToolCallsJson = JsonSerializer.Serialize(new[] { new { id = "c1", name = "echo_tool", arguments = "{}" } }),
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
            });
            db.AiAssistantMessages.Add(new AiAssistantMessage
            {
                Id = "m-t", ThreadId = thread.Id, Role = "tool", Content = "{}",
                ToolCallId = "c1", ToolName = "echo_tool", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            });
            await db.SaveChangesAsync();
        }

        await foreach (var _ in orchestrator.RunTurnAsync(thread.Id, "user-1", "learner", "fresh", null, CancellationToken.None)) { }

        Assert.NotEmpty(gateway.Calls);
        var sent = gateway.Calls[0].Messages;
        Assert.DoesNotContain(sent, m => m.Role == "tool");
        Assert.Equal("user", sent[1].Role);
    }

    [Fact]
    public void BuildLlmMessages_KeepsToolRows_ThatBelongToThePrecedingAssistantCall()
    {
        var history = new List<AiAssistantMessage>
        {
            new() { Id = "1", ThreadId = "t", Role = "user", Content = "hi", CreatedAt = DateTimeOffset.UtcNow },
            new() { Id = "2", ThreadId = "t", Role = "assistant", Content = null, CreatedAt = DateTimeOffset.UtcNow.AddSeconds(1), ToolCallsJson = "[{\"id\":\"c\"}]" },
            new() { Id = "3", ThreadId = "t", Role = "tool", Content = "{}", ToolCallId = "c", ToolName = "x", CreatedAt = DateTimeOffset.UtcNow.AddSeconds(2) },
        };
        var messages = AiAssistantOrchestrator.BuildLlmMessages("sys", history);
        // system prompt + user + assistant tool call + its tool result
        Assert.Equal(4, messages.Count);
        Assert.Equal("tool", messages[3].Role);
    }

    private AiAssistantOrchestrator BuildOrchestrator(
        IAiAssistantGateway gateway,
        IAiToolInvoker invoker,
        OetLearner.Api.Services.Settings.IRuntimeSettingsProvider? settings = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_options);
        services.AddDbContext<LearnerDbContext>(o => o.UseSqlite(_connection));
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        return new AiAssistantOrchestrator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            gateway,
            new OneToolRegistry(),
            invoker,
            new NullPromptProvider(),
            settings ?? new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AiAssistantOrchestrator>.Instance);
    }

    private AiToolInvoker BuildInvoker(out List<AiToolContext> captured)
    {
        var list = new List<AiToolContext>();
        captured = list;
        var sp = new ServiceCollection()
            .AddSingleton(_options)
            .AddDbContext<LearnerDbContext>(o => o.UseSqlite(_connection))
            .AddLogging()
            .BuildServiceProvider();
        return new AiToolInvoker(
            new OneToolRegistry(),
            sp,
            new LearnerDbContext(_options),
            new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AiToolInvoker>.Instance,
            new IAiToolExecutor[] { new EchoTool(list) });
    }

    private sealed class ScriptedGateway : IAiAssistantGateway
    {
        private readonly List<List<LlmStreamChunk>> _scenarios;
        public List<CapturedCall> Calls { get; } = new();

        public ScriptedGateway(params List<LlmStreamChunk>[] scenarios)
            => _scenarios = scenarios.Select(s => s.ToList()).ToList();

        public async IAsyncEnumerable<LlmStreamChunk> StreamCompleteWithToolsAsync(
            string featureCode, string? userId, List<LlmMessage> messages,
            IReadOnlyList<AiToolDefinition> tools,
            string? modelOverride,
            CancellationToken ct,
            IReadOnlyList<OetLearner.Api.Services.Rulebook.AiProviderImageAttachment>? imageAttachments = null,
            OetLearner.Api.Services.Rulebook.AiProviderDocumentAttachment? documentAttachment = null,
            string? conversationKey = null,
            bool isContinuation = false)
        {
            Calls.Add(new CapturedCall(messages.ToList(), conversationKey, isContinuation));
            var next = _scenarios[0];
            if (_scenarios.Count > 1) _scenarios.RemoveAt(0);
            foreach (var chunk in next)
            {
                yield return chunk;
            }
            await Task.Yield();
        }

        public sealed record CapturedCall(List<LlmMessage> Messages, string? ConversationKey, bool IsContinuation);
    }

    private sealed class OneToolRegistry : IAiToolRegistry
    {
        public Task<IReadOnlyList<AiToolDefinition>> ResolveForFeatureAsync(string featureCode, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AiToolDefinition>>(new[]
            {
                new AiToolDefinition("echo_tool", "echo_tool", "Echoes input.", AiToolCategory.Read, "{\"type\":\"object\"}"),
            });
        public bool IsKnownToolCode(string toolCode) => string.Equals(toolCode, "echo_tool", StringComparison.OrdinalIgnoreCase);
        public void InvalidateFeature(string featureCode) { }
        public Task SeedCatalogAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class EchoTool : IAiToolExecutor
    {
        private readonly List<AiToolContext> _captured;
        public EchoTool(List<AiToolContext> captured) => _captured = captured;
        public string Code => "echo_tool";
        public AiToolCategory Category => AiToolCategory.Read;
        public string JsonSchemaArgs => "{\"type\":\"object\"}";
        public Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
        {
            _captured.Add(ctx);
            return Task.FromResult(new AiToolExecutionResult(AiToolOutcome.Success, JsonSerializer.Deserialize<JsonElement>("{}")));
        }
    }

    private sealed class NullPromptProvider : OetLearner.Api.Services.AiAssistant.SystemPrompts.ISystemPromptProvider
    {
        public string GetSystemPrompt(string role, string userId) => "prompt";
    }
}
