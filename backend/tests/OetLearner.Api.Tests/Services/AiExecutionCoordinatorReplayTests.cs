using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Rulebook;
using Xunit;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// the PERMANENT REPLAY LOCKOUT fix.
///
/// <para>
/// The defect: <see cref="AiIdempotencyKeyBuilder"/> derives its key from stable
/// request content, so the FIRST operation ever created for a canonical action
/// owned that key for ever. A terminal failure, or a completion from last month,
/// then refused every future identical request — the caller could only be told
/// "duplicate result unavailable", for ever. That is a lockout, not idempotency.
/// </para>
///
/// <para>
/// These tests pin the repaired contract on BOTH sides of the line:
/// <list type="bullet">
///   <item>an immediate/concurrent duplicate STILL never calls the provider
///   twice (the anti-double-charge property must not be weakened);</item>
///   <item>a completion inside the replay window is STILL a duplicate;</item>
///   <item><see cref="AiOperationState.Indeterminate"/> is NEVER auto-retried,
///   at any age — an ambiguous outcome may already have been billed;</item>
///   <item>a safe failure (FailedTerminal / Cancelled) CAN be redone;</item>
///   <item>a completion older than the window CAN be redone;</item>
///   <item>the replay walk itself is bounded and degrades to a truthful
///   duplicate rather than looping.</item>
/// </list>
/// </para>
/// </summary>
public sealed class AiExecutionCoordinatorReplayTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public AiExecutionCoordinatorReplayTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    // ── The property that protects money ────────────────────────────────────────

    /// <summary>
    /// Item 1: the immediate same-action duplicate. Bounding replay must not buy
    /// a second provider call for a twin submitted right now.
    /// </summary>
    [Fact]
    public async Task ImmediateDuplicate_ReturnsExistingOperation_AndNeverCallsProviderTwice()
    {
        await using var db = new LearnerDbContext(_options);
        var core = new CountingCore();
        var coordinator = NewCoordinator(db, core);

        var first = await coordinator.ExecuteAsync(BuildRequest(), default);
        var second = await coordinator.ExecuteAsync(BuildRequest(), default);

        Assert.False(first.WasDuplicate);
        Assert.True(second.WasDuplicate);
        Assert.Null(second.GatewayResult);
        Assert.Equal(first.Operation.Id, second.Operation.Id);
        Assert.Equal(1, core.Calls);
        Assert.Equal(1, await db.AiOperations.AsNoTracking().CountAsync());
    }

    /// <summary>
    /// Item 1: a COMPLETED operation inside the (short, configurable) replay
    /// window is still the duplicate case — this is the double-submit /
    /// retry-storm shape the incident was actually about.
    /// </summary>
    [Fact]
    public async Task CompletedInsideReplayWindow_IsStillADuplicate_WithNoSecondCall()
    {
        await using var db = new LearnerDbContext(_options);
        var core = new CountingCore();
        var coordinator = NewCoordinator(db, core, replayWindowSeconds: 3600);

        await coordinator.ExecuteAsync(BuildRequest(), default);
        var second = await coordinator.ExecuteAsync(BuildRequest(), default);

        Assert.True(second.WasDuplicate);
        Assert.Equal(AiOperationState.Completed, second.State);
        Assert.Equal(1, core.Calls);
    }

    /// <summary>
    /// Item 1: an ambiguous predecessor is never auto-replayed at ANY age. The
    /// provider may already have accepted and charged for it, so only an
    /// explicit reconciliation (moving it to a decided state) may unblock it.
    /// </summary>
    [Fact]
    public async Task Indeterminate_IsNeverAutoRetried_EvenLongAfterTheReplayWindow()
    {
        await using var db = new LearnerDbContext(_options);
        var core = new CountingCore();
        var coordinator = NewCoordinator(db, core);

        await coordinator.ExecuteAsync(BuildRequest(), default);
        await AgeOperationAsync(db, AiOperationState.Indeterminate, TimeSpan.FromDays(30));

        var second = await coordinator.ExecuteAsync(BuildRequest(), default);

        Assert.True(second.WasDuplicate);
        Assert.Equal(AiOperationState.Indeterminate, second.Operation.State);
        Assert.Equal(1, core.Calls);
        Assert.Equal(1, await db.AiOperations.AsNoTracking().CountAsync());
    }

    // ── The lockout fix ─────────────────────────────────────────────────────────

    /// <summary>
    /// Item 1: an identical request whose predecessor ended in a proven-safe
    /// failure opens a NEW operation instead of being refused for ever. The new
    /// row carries a bumped replay discriminator on the existing
    /// <see cref="AiOperation.ResourceVersion"/> dimension, so both the
    /// idempotency key and the resource slot differ and neither unique index is
    /// weakened.
    /// </summary>
    [Theory]
    [InlineData(AiOperationState.FailedTerminal)]
    [InlineData(AiOperationState.Cancelled)]
    public async Task SafeFailurePredecessor_AllowsAnIdenticalRequestToRunAgain(AiOperationState predecessor)
    {
        await using var db = new LearnerDbContext(_options);
        var core = new CountingCore { ThrowOnCall = true };
        var coordinator = NewCoordinator(db, core);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ExecuteAsync(BuildRequest(), default));

        var failed = await db.AiOperations.AsNoTracking().SingleAsync();
        Assert.Equal(AiOperationState.FailedTerminal, failed.State);
        await AgeOperationAsync(db, predecessor, TimeSpan.Zero);

        core.ThrowOnCall = false;
        var retry = await coordinator.ExecuteAsync(BuildRequest(), default);

        Assert.False(retry.WasDuplicate);
        Assert.NotNull(retry.GatewayResult);
        Assert.Equal(2, core.Calls);
        Assert.Equal(AiOperationState.Completed, retry.Operation.State);

        // A genuinely NEW row, at a bumped replay version, with distinct keys.
        Assert.NotEqual(failed.Id, retry.Operation.Id);
        Assert.Equal(1, failed.ResourceVersion);
        Assert.Equal(2, retry.Operation.ResourceVersion);
        Assert.NotEqual(failed.IdempotencyKey, retry.Operation.IdempotencyKey);
        Assert.NotEqual(failed.ResourceSlotKey, retry.Operation.ResourceSlotKey);
        Assert.Equal(2, await db.AiOperations.AsNoTracking().CountAsync());
    }

    /// <summary>
    /// Item 1: a learner legitimately re-requesting the same action tomorrow is
    /// new work, not a replay. Proven by ageing a real completion past the
    /// default window rather than by shrinking the window to zero.
    /// </summary>
    [Fact]
    public async Task CompletedOutsideReplayWindow_ExecutesAnew()
    {
        await using var db = new LearnerDbContext(_options);
        var core = new CountingCore();
        var coordinator = NewCoordinator(db, core); // default 5-minute window

        var first = await coordinator.ExecuteAsync(BuildRequest(), default);
        await AgeOperationAsync(db, AiOperationState.Completed, TimeSpan.FromHours(6));

        var second = await coordinator.ExecuteAsync(BuildRequest(), default);

        Assert.False(second.WasDuplicate);
        Assert.NotEqual(first.Operation.Id, second.Operation.Id);
        Assert.Equal(2, core.Calls);
        Assert.Equal(2, await db.AiOperations.AsNoTracking().CountAsync());
    }

    /// <summary>
    /// Item 1: the replay walk is BOUNDED. With one round available a
    /// replayable predecessor still cannot loop — the caller gets the truthful
    /// duplicate outcome and, critically, no provider call.
    /// </summary>
    [Fact]
    public async Task ReplayRounds_AreBounded_AndDegradeToATruthfulDuplicate()
    {
        await using var db = new LearnerDbContext(_options);
        var core = new CountingCore { ThrowOnCall = true };
        var coordinator = NewCoordinator(db, core, maxReplayRounds: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ExecuteAsync(BuildRequest(), default));

        core.ThrowOnCall = false;
        var second = await coordinator.ExecuteAsync(BuildRequest(), default);

        Assert.True(second.WasDuplicate);
        Assert.Null(second.GatewayResult);
        Assert.Equal(1, core.Calls);
        Assert.Equal(1, await db.AiOperations.AsNoTracking().CountAsync());
    }

    // ── Decision table ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AiOperationState.Queued, AiOperationReplayDecision.WaitInFlight)]
    [InlineData(AiOperationState.Leased, AiOperationReplayDecision.WaitInFlight)]
    [InlineData(AiOperationState.ProviderSucceeded, AiOperationReplayDecision.WaitInFlight)]
    [InlineData(AiOperationState.RetryScheduled, AiOperationReplayDecision.WaitInFlight)]
    [InlineData(AiOperationState.Completed, AiOperationReplayDecision.Duplicate)]
    [InlineData(AiOperationState.Indeterminate, AiOperationReplayDecision.Duplicate)]
    [InlineData(AiOperationState.BlockedBudget, AiOperationReplayDecision.Duplicate)]
    [InlineData(AiOperationState.SkippedNoEvidence, AiOperationReplayDecision.Duplicate)]
    [InlineData(AiOperationState.FailedTerminal, AiOperationReplayDecision.CreateNewAttempt)]
    [InlineData(AiOperationState.Cancelled, AiOperationReplayDecision.CreateNewAttempt)]
    public void ReplayPolicy_FreshPredecessor_DecidesByState(
        AiOperationState state, AiOperationReplayDecision expected)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(expected, AiOperationReplayPolicy.Decide(
            state, terminalAt: now, now: now, replayWindow: AiOperationReplayPolicy.DefaultReplayWindow));
    }

    [Fact]
    public void ReplayPolicy_OnlyCompletedIsAgeSensitive()
    {
        var now = DateTimeOffset.UtcNow;
        var window = TimeSpan.FromMinutes(5);
        var ancient = now - TimeSpan.FromDays(400);

        Assert.Equal(AiOperationReplayDecision.CreateNewAttempt,
            AiOperationReplayPolicy.Decide(AiOperationState.Completed, ancient, now, window));

        // Age must NEVER unlock an ambiguous or externally-owned terminal state.
        Assert.Equal(AiOperationReplayDecision.Duplicate,
            AiOperationReplayPolicy.Decide(AiOperationState.Indeterminate, ancient, now, window));
        Assert.Equal(AiOperationReplayDecision.Duplicate,
            AiOperationReplayPolicy.Decide(AiOperationState.BlockedBudget, ancient, now, window));
    }

    /// <summary>A mis-set configuration value must not be able to recreate the
    /// permanent lockout (huge window) or an unbounded walk.</summary>
    [Fact]
    public void ReplayPolicy_ClampsConfiguration()
    {
        Assert.Equal(TimeSpan.Zero, AiOperationReplayPolicy.Clamp(TimeSpan.FromSeconds(-1)));
        Assert.Equal(AiOperationReplayPolicy.MaxReplayWindow,
            AiOperationReplayPolicy.Clamp(TimeSpan.FromDays(3650)));
        Assert.Equal(TimeSpan.FromMinutes(5), AiOperationReplayPolicy.Clamp(TimeSpan.FromMinutes(5)));

        Assert.Equal(1, AiOperationReplayPolicy.ClampRounds(0));
        Assert.Equal(1, AiOperationReplayPolicy.ClampRounds(-9));
        Assert.Equal(AiOperationReplayPolicy.MaxReplayRoundsCeiling,
            AiOperationReplayPolicy.ClampRounds(int.MaxValue));

        Assert.Equal(2, AiOperationReplayPolicy.NextVersion(1));
        Assert.Equal(2, AiOperationReplayPolicy.NextVersion(null));
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────

    /// <summary>Rewrites the single operation row to a chosen terminal state and
    /// age. Models a reconciled predecessor without waiting in wall-clock time.</summary>
    private static async Task AgeOperationAsync(LearnerDbContext db, AiOperationState state, TimeSpan age)
    {
        var row = await db.AiOperations.SingleAsync();
        row.State = state;
        row.UpdatedAt = DateTimeOffset.UtcNow - age;
        await db.SaveChangesAsync();
        db.Entry(row).State = EntityState.Detached;
    }

    private static AiExecutionCoordinator NewCoordinator(
        LearnerDbContext db,
        CountingCore core,
        int? replayWindowSeconds = null,
        int? maxReplayRounds = null)
    {
        IOptions<AiExecutionCoordinationOptions>? options = replayWindowSeconds is null && maxReplayRounds is null
            ? null
            : Options.Create(new AiExecutionCoordinationOptions
            {
                ReplayWindowSeconds = replayWindowSeconds ?? (int)AiOperationReplayPolicy.DefaultReplayWindow.TotalSeconds,
                MaxReplayRounds = maxReplayRounds ?? AiOperationReplayPolicy.DefaultMaxReplayRounds,
            });

        return new AiExecutionCoordinator(
            new AiOperationStore(db),
            core,
            featurePolicyRegistry: null,
            pricingResolver: null,
            hostEnvironment: null,
            logger: NullLogger<AiExecutionCoordinator>.Instance,
            coordinationOptions: options);
    }

    private static AiOperationRequest BuildRequest() => new()
    {
        Module = "writing",
        ResourceId = "sub-replay",
        ResourceType = "writing_submission",
        ResourceVersion = 1,
        RequestHash = "hash-stable",
        PromptVersion = "pv1",
        RulebookVersion = "rb1",
        GatewayRequest = new AiGatewayRequest
        {
            FeatureCode = AiFeatureCodes.WritingGrade,
            UserId = "user-replay",
            Prompt = new AiGroundedPrompt { SystemPrompt = "system", TaskInstruction = "grade" },
        },
    };

    private sealed class CountingCore : IAiGatewayCoreExecutor
    {
        public int Calls { get; private set; }
        public bool ThrowOnCall { get; set; }

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            if (ThrowOnCall) throw new InvalidOperationException("simulated provider failure");

            return Task.FromResult(new AiGatewayResult
            {
                Completion = "ok",
                ResolvedProvider = "anthropic",
                ResolvedModel = "claude-sonnet-5",
                UsageRecordId = $"usage-{Calls}",
                UsagePersisted = true,
            });
        }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context) => new();
    }
}
