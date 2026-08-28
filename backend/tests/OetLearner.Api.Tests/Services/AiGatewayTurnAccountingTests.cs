using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiTools;
using OetLearner.Api.Services.Rulebook;
using Xunit;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// the load-bearing accounting invariant: <b>one physical provider invocation
/// is one <see cref="AiUsageRecord"/> row</b>.
///
/// <para>
/// The reviewed defect: the tool loop invoked the provider up to N times but
/// wrote a single aggregated usage row at the end. Every intermediate turn was
/// real money the ledger never saw, and a late failure re-counted earlier
/// turns' tokens onto the failure row. These tests drive the REAL
/// <see cref="AiUsageRecorder"/> against a real <see cref="LearnerDbContext"/>
/// and count rows — they cannot pass by inspection of intent.
/// </para>
/// </summary>
public sealed class AiGatewayTurnAccountingTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public AiGatewayTurnAccountingTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    /// <summary>C-5: three physical turns ⇒ three usage rows AND three attempt
    /// rows with monotonic attempt numbers, each carrying only its own tokens.</summary>
    [Fact]
    public async Task ThreeProviderTurns_Write_ThreeUsageRows_AndThreeAttemptRows()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedOperationAsync(db, "op-3turn");

        var provider = new ScriptedMultiTurnProvider(
            Turn.WithTool(promptTokens: 100, completionTokens: 10),
            Turn.WithTool(promptTokens: 200, completionTokens: 20),
            Turn.Final("done", promptTokens: 300, completionTokens: 30));

        var gateway = BuildGateway(db, provider);

        var result = await gateway.CompleteAsync(NewRequest(gateway, operationId: "op-3turn"));

        Assert.Equal("done", result.Completion);
        Assert.Equal(3, provider.Calls);

        var usage = await db.AiUsageRecords.AsNoTracking().OrderBy(u => u.AttemptNumber).ToListAsync();
        Assert.Equal(3, usage.Count);
        Assert.Equal(new[] { 100, 200, 300 }, usage.Select(u => u.PromptTokens));
        Assert.Equal(new[] { 10, 20, 30 }, usage.Select(u => u.CompletionTokens));
        Assert.All(usage, u => Assert.Equal(AiCallOutcome.Success, u.Outcome));

        var attempts = await db.AiOperationAttempts.AsNoTracking().OrderBy(a => a.AttemptNumber).ToListAsync();
        Assert.Equal(3, attempts.Count);
        Assert.Equal(new[] { 1, 2, 3 }, attempts.Select(a => a.AttemptNumber));
        Assert.All(attempts, a => Assert.Equal("op-3turn", a.OperationId));

        // The in-memory aggregate the caller/admin surfaces see is preserved.
        Assert.Equal(600, result.Usage!.PromptTokens);
        Assert.Equal(60, result.Usage.CompletionTokens);
        Assert.True(result.UsagePersisted);
    }

    /// <summary>C-3: truncation after the max turn budget must NOT add an N+1
    /// row. The last physical turn carries the truncation outcome.</summary>
    [Fact]
    public async Task ToolLoopTruncation_WritesExactlyOneRowPerTurn_AndNoExtraRow()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedOperationAsync(db, "op-trunc");

        // Every turn keeps asking for a tool, so the loop hits its cap.
        var provider = new ScriptedMultiTurnProvider(
            Turn.WithTool(promptTokens: 100, completionTokens: 10),
            Turn.WithTool(promptTokens: 200, completionTokens: 20));

        var gateway = BuildGateway(db, provider, maxToolCalls: 2);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.CompleteAsync(NewRequest(gateway, operationId: "op-trunc")));

        Assert.Equal(2, provider.Calls);

        var usage = await db.AiUsageRecords.AsNoTracking().OrderBy(u => u.AttemptNumber).ToListAsync();
        Assert.Equal(2, usage.Count); // N turns ⇒ N rows, never N+1.
        Assert.Equal(AiCallOutcome.Success, usage[0].Outcome);

        // The truncating invocation owns the outcome on its OWN row…
        Assert.Equal(AiCallOutcome.ProviderError, usage[1].Outcome);
        Assert.Equal("tool_loop_truncated", usage[1].ErrorCode);
        // …and carries only its own tokens — turn 1's 100 are not re-counted.
        Assert.Equal(200, usage[1].PromptTokens);

        var attempts = await db.AiOperationAttempts.AsNoTracking().ToListAsync();
        Assert.Equal(2, attempts.Count);
    }

    /// <summary>C-2: a provider failure on turn 2 produces one SUCCESS row for
    /// turn 1 and one zero-cost FAILURE row for turn 2 — the failure row must
    /// not inherit turn 1's tokens or cost.</summary>
    [Fact]
    public async Task ProviderFailureOnSecondTurn_WritesOneSuccess_AndOneZeroCostFailure()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedOperationAsync(db, "op-fail2");

        var provider = new ScriptedMultiTurnProvider(
            Turn.WithTool(promptTokens: 100, completionTokens: 10),
            Turn.Throws(new HttpRequestException("upstream exploded")));

        var gateway = BuildGateway(db, provider);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => gateway.CompleteAsync(NewRequest(gateway, operationId: "op-fail2")));

        Assert.Equal(2, provider.Calls);

        var usage = await db.AiUsageRecords.AsNoTracking().OrderBy(u => u.AttemptNumber).ToListAsync();
        Assert.Equal(2, usage.Count);

        Assert.Equal(AiCallOutcome.Success, usage[0].Outcome);
        Assert.Equal(100, usage[0].PromptTokens);

        var failure = usage[1];
        Assert.NotEqual(AiCallOutcome.Success, failure.Outcome);
        Assert.Equal(0, failure.PromptTokens);      // no double-count of turn 1
        Assert.Equal(0, failure.CompletionTokens);
        Assert.Equal(0m, failure.CostEstimateUsd);  // provider produced nothing

        var attempts = await db.AiOperationAttempts.AsNoTracking().ToListAsync();
        Assert.Equal(2, attempts.Count);
    }

    /// <summary>C-2: a single successful call writes exactly ONE row — no extra
    /// failure row is appended after a successful final response.</summary>
    [Fact]
    public async Task SingleSuccessfulTurn_WritesExactlyOneSuccessRow()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedOperationAsync(db, "op-single");

        var provider = new ScriptedMultiTurnProvider(Turn.Final("hello", 50, 5));
        var gateway = BuildGateway(db, provider);

        await gateway.CompleteAsync(NewRequest(gateway, operationId: "op-single"));

        var usage = await db.AiUsageRecords.AsNoTracking().ToListAsync();
        var single = Assert.Single(usage);
        Assert.Equal(AiCallOutcome.Success, single.Outcome);
        Assert.Single(await db.AiOperationAttempts.AsNoTracking().ToListAsync());
    }

    /// <summary>C-1: a direct core call with NO OperationId still writes one
    /// usage row per turn — and zero attempt rows, because there is no
    /// operation to attribute them to. Pre-W2 call sites are unaffected.</summary>
    [Fact]
    public async Task WithoutOperationId_WritesOneUsageRowPerTurn_AndNoAttemptRows()
    {
        await using var db = new LearnerDbContext(_options);

        var provider = new ScriptedMultiTurnProvider(
            Turn.WithTool(promptTokens: 100, completionTokens: 10),
            Turn.Final("done", promptTokens: 200, completionTokens: 20));

        var gateway = BuildGateway(db, provider);

        await gateway.CompleteAsync(NewRequest(gateway, operationId: null));

        Assert.Equal(2, provider.Calls);
        Assert.Equal(2, await db.AiUsageRecords.AsNoTracking().CountAsync());
        Assert.Equal(0, await db.AiOperationAttempts.AsNoTracking().CountAsync());
        Assert.All(
            await db.AiUsageRecords.AsNoTracking().ToListAsync(),
            u => Assert.Null(u.OperationId));
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────

    private readonly RulebookLoader _loader = new();

    private AiGatewayService BuildGateway(LearnerDbContext db, ScriptedMultiTurnProvider provider, int maxToolCalls = 4)
        => new(
            _loader,
            new IAiModelProvider[] { provider },
            usageRecorder: new AiUsageRecorder(db, NullLogger<AiUsageRecorder>.Instance),
            toolRegistry: new SingleToolRegistry(),
            toolInvoker: new EchoToolInvoker(),
            toolOptions: Options.Create(new AiToolOptions { MaxToolCallsPerCompletion = maxToolCalls }),
            logger: NullLogger<AiGatewayService>.Instance);

    private static AiGatewayRequest NewRequest(AiGatewayService gateway, string? operationId) => new()
    {
        FeatureCode = AiFeatureCodes.AdminWritingDraft,
        UserId = "user-1",
        OperationId = operationId,
        UserInput = "draft me a letter",
        Prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.GenerateContent,
            LetterType = "routine_referral",
        }),
    };

    private static async Task SeedOperationAsync(LearnerDbContext db, string operationId)
    {
        db.AiOperations.Add(new AiOperation
        {
            Id = operationId,
            Module = "admin",
            FeatureCode = AiFeatureCodes.AdminWritingDraft,
            UserId = "user-1",
            IdempotencyKey = $"key-{operationId}",
            OperationClass = AiOperationClass.AdminBatch,
            State = AiOperationState.Leased,
            AttemptLimit = 4,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>One scripted physical provider turn.</summary>
    private sealed record Turn(string Text, int PromptTokens, int CompletionTokens, bool RequestsTool, Exception? Failure)
    {
        public static Turn WithTool(int promptTokens, int completionTokens)
            => new("", promptTokens, completionTokens, RequestsTool: true, Failure: null);

        public static Turn Final(string text, int promptTokens, int completionTokens)
            => new(text, promptTokens, completionTokens, RequestsTool: false, Failure: null);

        public static Turn Throws(Exception ex) => new("", 0, 0, false, ex);
    }

    /// <summary>A provider that replays a fixed script of physical turns and
    /// counts how many times it was actually invoked. Running out of script is
    /// an assertion failure, not a silent extra turn.</summary>
    private sealed class ScriptedMultiTurnProvider(params Turn[] turns) : IAiModelProvider
    {
        public int Calls { get; private set; }
        public string Name => "scripted";

        public Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
        {
            if (Calls >= turns.Length)
            {
                throw new InvalidOperationException(
                    $"Provider invoked {Calls + 1} times but the script only defines {turns.Length} turns.");
            }

            var turn = turns[Calls++];
            if (turn.Failure is not null) throw turn.Failure;

            return Task.FromResult(new AiProviderCompletion
            {
                Text = turn.Text,
                Usage = new AiUsage
                {
                    PromptTokens = turn.PromptTokens,
                    CompletionTokens = turn.CompletionTokens,
                },
                ToolCalls = turn.RequestsTool
                    ? new List<AiToolCall> { new() { Id = $"call-{Calls}", ToolCode = "echo", ArgsJson = "{}" } }
                    : null,
            });
        }
    }

    private sealed class SingleToolRegistry : IAiToolRegistry
    {
        public Task<IReadOnlyList<AiToolDefinition>> ResolveForFeatureAsync(string featureCode, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AiToolDefinition>>(new[]
            {
                new AiToolDefinition("echo", "echo", "Echoes its arguments.", AiToolCategory.Read, "{}"),
            });

        public bool IsKnownToolCode(string toolCode) => true;
        public void InvalidateFeature(string featureCode) { }
        public Task SeedCatalogAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class EchoToolInvoker : IAiToolInvoker
    {
        public Task<AiToolExecutionResult> InvokeAsync(string toolCode, JsonElement argsJson, AiToolContext ctx, CancellationToken ct)
            => Task.FromResult(new AiToolExecutionResult(AiToolOutcome.Success, JsonDocument.Parse("{\"ok\":true}").RootElement));

        public Task<AiToolExecutionResult> InvokeAsync(AiToolCall call, AiToolContext ctx, CancellationToken ct)
            => InvokeAsync(call.ToolCode, default, ctx, ct);
    }
}
