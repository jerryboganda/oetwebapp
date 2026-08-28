using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Rulebook;
using Xunit;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// the control plane must never decide when a business caller's domain writes
/// land.
///
/// <para>
/// The defect: <see cref="AiOperationStore"/> took the request-scoped
/// <see cref="LearnerDbContext"/> and called <c>SaveChanges</c> on it. Any
/// unrelated entity the caller had staged but not yet saved — a submission
/// mutation, a state transition, a ledger row it intended to commit only AFTER
/// a successful AI call — was silently flushed by control-plane bookkeeping,
/// BEFORE the provider was even contacted. If the call then failed, the caller's
/// domain change was already committed with no way to know.
/// </para>
///
/// <para>
/// The fix mirrors <see cref="DirectAiCallRecorder"/>: every store method opens
/// its own <see cref="IServiceScope"/> and its own short-lived context.
/// </para>
/// </summary>
public sealed class AiOperationStoreIsolationTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly ServiceProvider _provider;

    public AiOperationStoreIsolationTests()
    {
        // Shared-cache in-memory Sqlite: several connections (one per scope)
        // see the SAME database, which is exactly what this test needs to prove.
        var connectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<LearnerDbContext>(o => o.UseSqlite(connectionString));
        _provider = services.BuildServiceProvider(validateScopes: true);

        using var seedScope = _provider.CreateScope();
        seedScope.ServiceProvider.GetRequiredService<LearnerDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _keepAlive.Dispose();
    }

    /// <summary>
    /// Item 6: creating an operation must not flush the caller's unrelated
    /// tracked changes. The operation row commits (through the store's own
    /// scope) while the caller's staged entity stays exactly where the caller
    /// left it — pending, and still the caller's to commit or discard.
    /// </summary>
    [Fact]
    public async Task CoordinatorOperationCreation_DoesNotFlushCallerTrackedChanges()
    {
        using var callerScope = _provider.CreateScope();
        var callerDb = callerScope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var staged = NewUnrelatedDomainChange();
        callerDb.AuditEvents.Add(staged); // deliberately NOT saved

        var core = new NoopCore();
        var coordinator = new AiExecutionCoordinator(
            new AiOperationStore(_provider.GetRequiredService<IServiceScopeFactory>()),
            core,
            featurePolicyRegistry: null,
            pricingResolver: null,
            hostEnvironment: null,
            logger: NullLogger<AiExecutionCoordinator>.Instance);

        var result = await coordinator.ExecuteAsync(BuildRequest(), default);
        Assert.False(result.WasDuplicate);
        Assert.Equal(1, core.Calls);

        using var verifyScope = _provider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        // The control plane persisted its own row through its own scope...
        Assert.Equal(1, await verifyDb.AiOperations.AsNoTracking().CountAsync());
        Assert.Equal(AiOperationState.Completed,
            (await verifyDb.AiOperations.AsNoTracking().SingleAsync()).State);

        // ...and did NOT commit the caller's unrelated domain change.
        Assert.Equal(0, await verifyDb.AuditEvents.AsNoTracking().CountAsync());
        Assert.Equal(EntityState.Added, callerDb.Entry(staged).State);
    }

    /// <summary>
    /// Regression anchor: the SHARED-context shape really did flush the caller's
    /// work, so the test above is proving something. Uses the deliberately
    /// <c>internal</c> test seam — DI can never select it, because
    /// <c>ServiceProvider</c> only considers public constructors.
    /// </summary>
    [Fact]
    public async Task SharedCallerContext_WouldHaveFlushedCallerChanges_WhichIsWhyTheStoreOpensItsOwnScope()
    {
        using var callerScope = _provider.CreateScope();
        var callerDb = callerScope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        callerDb.AuditEvents.Add(NewUnrelatedDomainChange());

        var store = new AiOperationStore(callerDb); // the pre-fix shape
        await store.TryInsertAsync(NewOperation(), default);

        using var verifyScope = _provider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(1, await verifyDb.AuditEvents.AsNoTracking().CountAsync());
    }

    /// <summary>The production constructor is the only one DI can see, so the
    /// shared-context seam can never be re-introduced by a registration.</summary>
    [Fact]
    public void OnlyTheScopeFactoryConstructorIsPublic()
    {
        var ctor = Assert.Single(typeof(AiOperationStore).GetConstructors());
        var parameter = Assert.Single(ctor.GetParameters());
        Assert.Equal(typeof(IServiceScopeFactory), parameter.ParameterType);
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────

    private static AuditEvent NewUnrelatedDomainChange() => new()
    {
        Id = $"audit_{Guid.NewGuid():N}",
        OccurredAt = DateTimeOffset.UtcNow,
        ActorId = "admin-1",
        ActorName = "admin-1",
        Action = "UnrelatedBusinessChange",
        ResourceType = "ContentPaper",
        ResourceId = "paper-1",
    };

    private static AiOperation NewOperation() => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Module = "writing",
        FeatureCode = AiFeatureCodes.WritingGrade,
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        State = AiOperationState.Queued,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static AiOperationRequest BuildRequest() => new()
    {
        Module = "writing",
        ResourceId = "sub-isolation",
        ResourceType = "writing_submission",
        ResourceVersion = 1,
        RequestHash = "hash-isolation",
        PromptVersion = "pv1",
        RulebookVersion = "rb1",
        GatewayRequest = new AiGatewayRequest
        {
            FeatureCode = AiFeatureCodes.WritingGrade,
            UserId = "user-isolation",
            Prompt = new AiGroundedPrompt { SystemPrompt = "system", TaskInstruction = "grade" },
        },
    };

    private sealed class NoopCore : IAiGatewayCoreExecutor
    {
        public int Calls { get; private set; }

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new AiGatewayResult
            {
                Completion = "ok",
                ResolvedProvider = "anthropic",
                ResolvedModel = "claude-sonnet-5",
                UsageRecordId = "usage-1",
                UsagePersisted = true,
            });
        }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context) => new();
    }
}
