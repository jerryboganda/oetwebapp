using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Rulebook;
using Xunit;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// W2 of the AI cost/reliability remediation — OBSERVE-MODE coverage for
/// <see cref="AiExecutionCoordinator"/>: one <see cref="AiOperation"/> row
/// persisted before the core executor is ever called, exactly one physical
/// invocation per canonical action, duplicate-request replay without a second
/// call, and a genuine conflict on a reused resource slot.
///
/// <para>
/// Uses a fake <see cref="IAiGatewayCoreExecutor"/> (never
/// <see cref="IAiGatewayService"/> — that direction is what caused the
/// recursion defect) against a Sqlite in-memory <see cref="LearnerDbContext"/>.
/// See <c>AiExecutionCoordinatorPostgreSqlConcurrencyTests</c> for the
/// real-PostgreSQL proof that a genuinely concurrent race across two
/// connections cannot escape the unique-index guard.
/// </para>
/// </summary>
public sealed class AiExecutionCoordinatorObserveModeTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public AiExecutionCoordinatorObserveModeTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task ExecuteAsync_FirstCall_PersistsOperationBeforeCallingCore_AndCompletes()
    {
        await using var db = new LearnerDbContext(_options);
        var core = new FakeCoreExecutor();
        var coordinator = NewCoordinator(db, core);

        var result = await coordinator.ExecuteAsync(BuildRequest("sub-1", "hash-1"), default);

        Assert.False(result.WasDuplicate);
        Assert.NotNull(result.GatewayResult);
        Assert.Equal(1, core.CallCount);
        Assert.Equal(AiOperationState.Completed, result.Operation.State);

        // The core saw the operation id — that is the recursion fence's key.
        Assert.Equal(result.Operation.Id, core.LastRequest!.OperationId);

        var persisted = await db.AiOperations.AsNoTracking().SingleAsync();
        Assert.Equal(AiOperationState.Completed, persisted.State);
        Assert.Equal("usage-record-1", persisted.ResultRef);
        Assert.NotNull(persisted.ResourceSlotKey);
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateSameHash_ReturnsExistingOperation_NoSecondCoreCall()
    {
        await using var db = new LearnerDbContext(_options);
        var core = new FakeCoreExecutor();
        var coordinator = NewCoordinator(db, core);

        var first = await coordinator.ExecuteAsync(BuildRequest("sub-1", "hash-1"), default);
        var second = await coordinator.ExecuteAsync(BuildRequest("sub-1", "hash-1"), default);

        Assert.False(first.WasDuplicate);
        Assert.True(second.WasDuplicate);
        Assert.Null(second.GatewayResult);
        Assert.Equal(1, core.CallCount);
        Assert.Equal(first.Operation.Id, second.Operation.Id);

        // D-1: the duplicate carries a truthful state + result pointer.
        Assert.Equal(AiOperationState.Completed, second.State);
        Assert.Equal("usage-record-1", second.ResultRef);
    }

    /// <summary>
    /// B-3: the same resource slot with a DIFFERENT request hash is a real
    /// conflict enforced by the UNIQUE partial index, not by a racy
    /// read-then-insert. Exactly one provider invocation total.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SameResourceSlotDifferentRequestHash_ThrowsConflict()
    {
        await using var db = new LearnerDbContext(_options);
        var core = new FakeCoreExecutor();
        var coordinator = NewCoordinator(db, core);

        await coordinator.ExecuteAsync(BuildRequest("sub-1", "hash-1"), default);

        await Assert.ThrowsAsync<AiOperationConflictException>(
            () => coordinator.ExecuteAsync(BuildRequest("sub-1", "hash-2"), default));

        Assert.Equal(1, core.CallCount);
        Assert.Equal(1, await db.AiOperations.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_DifferentResourceId_CreatesDistinctOperations_AndCallsCoreTwice()
    {
        await using var db = new LearnerDbContext(_options);
        var core = new FakeCoreExecutor();
        var coordinator = NewCoordinator(db, core);

        var first = await coordinator.ExecuteAsync(BuildRequest("sub-1", "hash-1"), default);
        var second = await coordinator.ExecuteAsync(BuildRequest("sub-2", "hash-1"), default);

        Assert.False(first.WasDuplicate);
        Assert.False(second.WasDuplicate);
        Assert.NotEqual(first.Operation.Id, second.Operation.Id);
        Assert.NotEqual(first.Operation.ResourceSlotKey, second.Operation.ResourceSlotKey);
        Assert.Equal(2, core.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_CoreThrows_MarksOperationFailedTerminal_AndPropagates()
    {
        await using var db = new LearnerDbContext(_options);
        var core = new FakeCoreExecutor { ThrowOnComplete = true };
        var coordinator = NewCoordinator(db, core);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ExecuteAsync(BuildRequest("sub-1", "hash-1"), default));

        var persisted = await db.AiOperations.AsNoTracking().SingleAsync();
        Assert.Equal(AiOperationState.FailedTerminal, persisted.State);
        Assert.Null(persisted.ResultRef);
    }

    /// <summary>
    /// F-1: an ambiguous core failure (client-side timeout, mid-flight
    /// transport loss) may already have been accepted and billed by the
    /// provider. It must be persisted as <see cref="AiOperationState.Indeterminate"/>
    /// — never <see cref="AiOperationState.FailedTerminal"/> — because
    /// FailedTerminal is a replayable state: classifying it terminal would let
    /// the identical follow-up request pay a second time for the same work.
    /// </summary>
    [Theory]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(TaskCanceledException))] // HttpClient timeout surfaces as this with a non-cancelled caller token
    public async Task ExecuteAsync_CoreThrowsAmbiguousFailure_MarksIndeterminate_AndDuplicateNeverReplays(Type exceptionType)
    {
        await using var db = new LearnerDbContext(_options);
        var core = new FakeCoreExecutor
        {
            ExceptionToThrow = (Exception)Activator.CreateInstance(exceptionType)!,
        };
        var coordinator = NewCoordinator(db, core);

        await Assert.ThrowsAsync(exceptionType,
            () => coordinator.ExecuteAsync(BuildRequest("sub-1", "hash-1"), CancellationToken.None));

        var persisted = await db.AiOperations.AsNoTracking().SingleAsync();
        Assert.Equal(AiOperationState.Indeterminate, persisted.State);
        Assert.Null(persisted.ResultRef);
        Assert.Equal(1, core.CallCount);

        // The identical re-request is answered from the existing operation:
        // no second physical provider call, ever.
        core.ExceptionToThrow = null;
        var replay = await coordinator.ExecuteAsync(BuildRequest("sub-1", "hash-1"), CancellationToken.None);
        Assert.True(replay.WasDuplicate);
        Assert.Equal(AiOperationState.Indeterminate, replay.State);
        Assert.Equal(1, core.CallCount);
    }

    /// <summary>
    /// D-2: <see cref="AiOperation.ResultRef"/> is gated on the recorder having
    /// actually committed a usage row. A generated-but-unpersisted id must
    /// never be stamped, or the control plane points at a row that does not
    /// exist.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenUsageDidNotPersist_LeavesResultRefNull()
    {
        await using var db = new LearnerDbContext(_options);
        var core = new FakeCoreExecutor { UsagePersisted = false };
        var coordinator = NewCoordinator(db, core);

        var result = await coordinator.ExecuteAsync(BuildRequest("sub-1", "hash-1"), default);

        Assert.Equal(AiOperationState.Completed, result.Operation.State);
        Assert.Null(result.ResultRef);
        Assert.Null((await db.AiOperations.AsNoTracking().SingleAsync()).ResultRef);
    }

    /// <summary>
    /// A-4: an interactive call with no stable caller resource leaves
    /// <see cref="AiOperation.ResourceSlotKey"/> null, so it never participates
    /// in the unique partial index and unrelated conversations are never
    /// conflated with one another.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WithNoResourceId_LeavesResourceSlotNull_AndDoesNotConflate()
    {
        await using var db = new LearnerDbContext(_options);
        var core = new FakeCoreExecutor();
        var coordinator = NewCoordinator(db, core);

        var first = await coordinator.ExecuteAsync(BuildRequest(resourceId: null, "hash-a"), default);
        var second = await coordinator.ExecuteAsync(BuildRequest(resourceId: null, "hash-b"), default);

        Assert.Null(first.Operation.ResourceSlotKey);
        Assert.Null(second.Operation.ResourceSlotKey);
        Assert.NotEqual(first.Operation.Id, second.Operation.Id);
        Assert.Equal(2, core.CallCount);
    }

    /// <summary>E-2: Production refuses an explicitly disabled feature BEFORE
    /// the operation row and with zero core invocations.</summary>
    [Fact]
    public async Task ExecuteAsync_InProduction_WithDisabledPolicy_RefusesWithoutCallingCore()
    {
        await using var db = new LearnerDbContext(_options);
        db.AiFeaturePolicies.Add(new AiFeaturePolicy
        {
            Id = Guid.NewGuid().ToString("N"),
            FeatureCode = AiFeatureCodes.WritingGrade,
            Module = "writing",
            OperationClass = AiOperationClass.ScoringCritical,
            IsActive = false,
            PolicyVersion = 1,
            RequiresGrounding = true,
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var core = new FakeCoreExecutor();
        var coordinator = new AiExecutionCoordinator(
            new AiOperationStore(db),
            core,
            featurePolicyRegistry: new AiFeaturePolicyRegistry(db),
            pricingResolver: null,
            hostEnvironment: new CoordinatorHostEnvironment("Production"),
            logger: NullLogger<AiExecutionCoordinator>.Instance);

        var ex = await Assert.ThrowsAsync<AiFeaturePolicyRefusedException>(
            () => coordinator.ExecuteAsync(BuildRequest("sub-1", "hash-1"), default));

        Assert.Equal("policy_disabled", ex.Reason);
        Assert.Equal(0, core.CallCount);
        Assert.Equal(0, await db.AiOperations.AsNoTracking().CountAsync());
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────

    private static AiExecutionCoordinator NewCoordinator(LearnerDbContext db, FakeCoreExecutor core)
        => new(
            new AiOperationStore(db),
            core,
            featurePolicyRegistry: null,
            pricingResolver: null,
            hostEnvironment: null,
            logger: NullLogger<AiExecutionCoordinator>.Instance);

    private static AiOperationRequest BuildRequest(string? resourceId, string requestHash) => new()
    {
        Module = "writing",
        ResourceId = resourceId,
        ResourceType = resourceId is null ? null : "writing_submission",
        ResourceVersion = resourceId is null ? null : 1,
        RequestHash = requestHash,
        PromptVersion = "pv1",
        RulebookVersion = "rb1",
        GatewayRequest = new AiGatewayRequest
        {
            FeatureCode = AiFeatureCodes.WritingGrade,
            UserId = "user-1",
            Prompt = new AiGroundedPrompt { SystemPrompt = "OET AI — Rulebook-Grounded System Prompt\n...", TaskInstruction = "grade" },
        },
    };

    private sealed class FakeCoreExecutor : IAiGatewayCoreExecutor
    {
        public int CallCount { get; private set; }
        public bool ThrowOnComplete { get; set; }
        public Exception? ExceptionToThrow { get; set; }
        public bool UsagePersisted { get; set; } = true;
        public AiGatewayRequest? LastRequest { get; private set; }

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            CallCount++;
            LastRequest = request;
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            if (ThrowOnComplete)
            {
                throw new InvalidOperationException("simulated provider failure");
            }

            return Task.FromResult(new AiGatewayResult
            {
                Completion = "ok",
                ResolvedModel = "claude-sonnet-5",
                ResolvedProvider = "anthropic",
                UsageRecordId = "usage-record-1",
                UsagePersisted = UsagePersisted,
            });
        }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new() { SystemPrompt = "OET AI — Rulebook-Grounded System Prompt", TaskInstruction = "grade" };
    }

    private sealed class CoordinatorHostEnvironment(string environmentName) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
