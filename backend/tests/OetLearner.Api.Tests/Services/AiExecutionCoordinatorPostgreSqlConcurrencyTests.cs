using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Tests.Infrastructure;
using Xunit;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// W2 of the AI cost/reliability remediation — real-PostgreSQL proof that the
/// exactly-once guarantee is enforced by the DATABASE, not by a read-then-check
/// in one process.
///
/// <para>
/// Two genuinely independent <see cref="AiExecutionCoordinator"/> instances,
/// each with its own <see cref="LearnerDbContext"/> on its own connection into
/// one isolated schema, race the same canonical action while sharing a single
/// call-counting <see cref="IAiGatewayCoreExecutor"/>. That shared counter is
/// the assertion that matters: if both coordinators believed they owned the
/// work, it would read 2 and the incident would repeat.
/// </para>
///
/// <para>
/// The lighter Sqlite coverage lives in
/// <c>AiExecutionCoordinatorObserveModeTests</c>; PostgreSQL is authoritative
/// because production classifies unique violations by SQLSTATE 23505 +
/// constraint name, which only a real server produces.
/// </para>
///
/// <see cref="PostgreSqlFactAttribute"/> skips (never fails) this class when
/// <c>OET_TEST_POSTGRES_CONNECTION</c> is unset.
/// </summary>
public sealed class AiExecutionCoordinatorPostgreSqlConcurrencyTests
{
    /// <summary>
    /// Production-faithful subset of the <c>AiOperations</c> DDL from
    /// <c>20261102090000_AddAiControlPlaneCore</c> +
    /// <c>20261105090000_AddAiOperationResourceSlot</c>. Hand-written rather
    /// than <c>EnsureCreated()</c> because the full model needs the pgvector
    /// extension, which is not required to exercise the control plane.
    /// <c>CHK_TestOnly_Module</c> exists only so a NON-unique constraint
    /// violation can be provoked deterministically.
    /// </summary>
    private const string OperationsDdl =
        """
        CREATE TABLE "AiOperations" (
            "Id" character varying(64) NOT NULL,
            "Module" character varying(32) NOT NULL,
            "FeatureCode" character varying(64) NOT NULL,
            "UserId" character varying(64) NULL,
            "TenantId" character varying(64) NULL,
            "ResourceId" character varying(64) NULL,
            "ResourceType" character varying(64) NULL,
            "ResourceVersion" integer NULL,
            "RequestHash" character varying(64) NULL,
            "PromptVersion" character varying(32) NULL,
            "RulebookVersion" character varying(32) NULL,
            "IdempotencyKey" character varying(256) NOT NULL,
            "ResourceSlotKey" character varying(64) NULL,
            "State" integer NOT NULL,
            "OperationClass" integer NOT NULL,
            "SelectedProviderId" character varying(64) NULL,
            "SelectedModel" character varying(128) NULL,
            "CreditReservationId" character varying(64) NULL,
            "BudgetReservationId" character varying(64) NULL,
            "AttemptLimit" integer NOT NULL,
            "NextAttemptAt" timestamp with time zone NULL,
            "LeaseOwner" character varying(128) NULL,
            "LeaseExpiresAt" timestamp with time zone NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            "ResultRef" character varying(128) NULL,
            CONSTRAINT "PK_AiOperations" PRIMARY KEY ("Id"),
            CONSTRAINT "CHK_TestOnly_Module" CHECK ("Module" <> 'forbidden-module')
        );
        CREATE UNIQUE INDEX "UX_AiOperations_IdempotencyKey" ON "AiOperations" ("IdempotencyKey");
        CREATE UNIQUE INDEX "UX_AiOperations_ResourceSlotKey"
            ON "AiOperations" ("ResourceSlotKey") WHERE "ResourceSlotKey" IS NOT NULL;
        """;

    /// <summary>
    /// B-4: two concurrent IDENTICAL requests ⇒ exactly ONE provider
    /// invocation and one operation row. The loser resolves to the winner's
    /// operation without ever calling the core.
    /// </summary>
    [PostgreSqlFact]
    public async Task TwoConcurrentIdenticalRequests_ProduceExactlyOneProviderInvocation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(OperationsDdl);

        var core = new CountingCoreExecutor(delay: TimeSpan.FromMilliseconds(250));
        var options = BuildOptions(database);

        await using var dbA = new LearnerDbContext(options);
        await using var dbB = new LearnerDbContext(options);
        var coordinatorA = NewCoordinator(dbA, core);
        var coordinatorB = NewCoordinator(dbB, core);

        var request = BuildRequest("sub-race", "hash-1");

        var taskA = RunAsync(coordinatorA, request);
        var taskB = RunAsync(coordinatorB, request);
        var outcomes = await Task.WhenAll(taskA, taskB);

        // The single load-bearing assertion: the provider was contacted once.
        Assert.Equal(1, core.Calls);

        // Exactly one caller owned the work; the other saw a duplicate.
        Assert.Equal(1, outcomes.Count(o => o.Owned));
        Assert.Equal(1, outcomes.Count(o => o.Duplicate || o.InFlight));

        await using var count = database.Command("""SELECT count(*) FROM "AiOperations";""");
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    /// <summary>
    /// Owner acceptance test (2026-08-28 AI/Cloud API plan, point 9): "100
    /// identical requests across both servers must result in 1 AI job, 1
    /// provider API call... Not 2, not 3, and definitely not repeated calls."
    /// 100 genuinely concurrent callers, split across TWO independent
    /// coordinator/DbContext/connection triples (modelling the blue and green
    /// API slots) racing the identical canonical action against one real
    /// PostgreSQL server ⇒ exactly one provider invocation, exactly one
    /// AiOperations row, and every other caller resolves to a duplicate/
    /// in-flight outcome with zero provider contact.
    /// </summary>
    [PostgreSqlFact]
    public async Task OneHundredConcurrentIdenticalRequestsAcrossTwoServers_ProduceExactlyOneProviderInvocation()
    {
        const int totalCallers = 100;

        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(OperationsDdl);

        var core = new CountingCoreExecutor(delay: TimeSpan.FromMilliseconds(300));
        var options = BuildOptions(database);
        var request = BuildRequest("sub-race-100", "hash-100");

        // Every caller gets its OWN LearnerDbContext/connection — exactly like
        // 100 independent HTTP requests each resolve their own scoped
        // DbContext via DI, whichever of the two hosted API slots (blue/green)
        // happens to handle them. Neither slot shares any in-process state
        // with the other in production, so "two servers" is modelled
        // correctly by N fully independent contexts racing through the
        // database alone — the only thing that can actually arbitrate them —
        // rather than by literally bucketing into two shared DbContext
        // instances (which would just recreate a DIFFERENT bug: EF Core's
        // DbContext is not safe for concurrent use by multiple callers).
        var contexts = new LearnerDbContext[totalCallers];
        try
        {
            var tasks = new Task<RaceOutcome>[totalCallers];
            for (var i = 0; i < totalCallers; i++)
            {
                contexts[i] = new LearnerDbContext(options);
                var coordinator = NewCoordinator(contexts[i], core);
                tasks[i] = RunAsync(coordinator, request);
            }
            var outcomes = await Task.WhenAll(tasks);

            // The single load-bearing assertion, at the owner's literal scale:
            // the provider was contacted exactly once, no matter how many
            // identical requests raced it or which server they landed on.
            Assert.Equal(1, core.Calls);
            Assert.Equal(1, outcomes.Count(o => o.Owned));
            Assert.Equal(totalCallers - 1, outcomes.Count(o => o.Duplicate || o.InFlight));
            Assert.Equal(0, outcomes.Count(o => o.Conflict));

            await using var count = database.Command("""SELECT count(*) FROM "AiOperations";""");
            Assert.Equal(1L, await count.ExecuteScalarAsync());
        }
        finally
        {
            foreach (var ctx in contexts)
            {
                if (ctx is not null) await ctx.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// B-3/B-4: two concurrent requests for the SAME resource slot but with
    /// DIFFERENT request hashes. The unique partial index — not a racy
    /// read-then-insert — guarantees one provider invocation and one
    /// controlled conflict.
    /// </summary>
    [PostgreSqlFact]
    public async Task TwoConcurrentSameSlotDifferentHash_ProduceOneInvocationAndOneConflict()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(OperationsDdl);

        var core = new CountingCoreExecutor(delay: TimeSpan.FromMilliseconds(250));
        var options = BuildOptions(database);

        await using var dbA = new LearnerDbContext(options);
        await using var dbB = new LearnerDbContext(options);
        var coordinatorA = NewCoordinator(dbA, core);
        var coordinatorB = NewCoordinator(dbB, core);

        // Same resource slot (same feature/module/user/resource/version/prompt/
        // rulebook) but different payloads, so the idempotency keys differ and
        // only the slot index can adjudicate.
        var taskA = RunAsync(coordinatorA, BuildRequest("sub-slot", "hash-a"));
        var taskB = RunAsync(coordinatorB, BuildRequest("sub-slot", "hash-b"));
        var outcomes = await Task.WhenAll(taskA, taskB);

        Assert.Equal(1, core.Calls);
        Assert.Equal(1, outcomes.Count(o => o.Owned));
        Assert.Equal(1, outcomes.Count(o => o.Conflict));

        await using var count = database.Command("""SELECT count(*) FROM "AiOperations";""");
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    /// <summary>
    /// B-2: a DbUpdateException that is NOT one of the two unique violations we
    /// own must propagate untouched. Laundering an FK/check/length failure into
    /// "someone else is in flight" would hide real corruption behind a retry
    /// loop — and, worse, could silently suppress a call the caller believes
    /// happened.
    /// </summary>
    [PostgreSqlFact]
    public async Task NonUniqueDbUpdateException_Propagates_AndNeverCallsProvider()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(OperationsDdl);

        var core = new CountingCoreExecutor();
        await using var db = new LearnerDbContext(BuildOptions(database));
        var coordinator = NewCoordinator(db, core);

        // `Module` is taken verbatim from the request, so this trips the
        // test-only CHECK constraint (SQLSTATE 23514) rather than a 23505.
        var request = BuildRequest("sub-check", "hash-1", module: "forbidden-module");

        var ex = await Assert.ThrowsAnyAsync<DbUpdateException>(
            () => coordinator.ExecuteAsync(request, default));

        var postgres = Assert.IsType<Npgsql.PostgresException>(ex.InnerException);
        Assert.Equal("23514", postgres.SqlState);
        Assert.Equal(0, core.Calls);
    }

    /// <summary>
    /// B-2 companion: proves the classification is by CONSTRAINT NAME against a
    /// real server, so the production duplicate/conflict split is not resting on
    /// an assumption about Npgsql's exception shape.
    /// </summary>
    [PostgreSqlFact]
    public async Task UniqueViolations_AreClassifiedByConstraintName()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(OperationsDdl);

        await using var db = new LearnerDbContext(BuildOptions(database));
        var store = new AiOperationStore(db);

        var first = NewOperation("op-1", key: "key-1", slot: "slot-1");
        Assert.Equal(AiOperationInsertOutcome.Inserted, (await store.TryInsertAsync(first, default)).Outcome);

        // Same idempotency key, different slot ⇒ duplicate.
        var duplicate = NewOperation("op-2", key: "key-1", slot: "slot-2");
        Assert.Equal(
            AiOperationInsertOutcome.DuplicateIdempotencyKey,
            (await store.TryInsertAsync(duplicate, default)).Outcome);

        // Different idempotency key, same slot ⇒ conflict.
        var conflict = NewOperation("op-3", key: "key-3", slot: "slot-1");
        Assert.Equal(
            AiOperationInsertOutcome.ResourceSlotConflict,
            (await store.TryInsertAsync(conflict, default)).Outcome);

        // A null slot never participates in the partial index.
        Assert.Equal(
            AiOperationInsertOutcome.Inserted,
            (await store.TryInsertAsync(NewOperation("op-4", key: "key-4", slot: null), default)).Outcome);
        Assert.Equal(
            AiOperationInsertOutcome.Inserted,
            (await store.TryInsertAsync(NewOperation("op-5", key: "key-5", slot: null), default)).Outcome);
    }

    /// <summary>
    /// W2 replay, on the authoritative engine. An identical replay of a
    /// resource-scoped action violates BOTH unique indexes at once, and which
    /// one PostgreSQL reports first is an index-check-ordering detail. This
    /// pins the production behaviour end to end: the violation is downgraded to
    /// a duplicate, the safe-failure predecessor is classified as replayable,
    /// and the bumped discriminator produces a genuinely new row on both
    /// dimensions — instead of the permanent "duplicate result unavailable"
    /// lockout the first version shipped with.
    /// </summary>
    [PostgreSqlFact]
    public async Task ReplayAfterSafeFailure_CreatesANewOperation_UnderRealUniqueViolations()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(OperationsDdl);

        var core = new CountingCoreExecutor();
        await using var db = new LearnerDbContext(BuildOptions(database));
        var coordinator = NewCoordinator(db, core);
        var request = BuildRequest("sub-replay-pg", "hash-1");

        var first = await coordinator.ExecuteAsync(request, default);
        Assert.False(first.WasDuplicate);

        // A recent completion is still a duplicate: no second provider call.
        Assert.True((await coordinator.ExecuteAsync(request, default)).WasDuplicate);
        Assert.Equal(1, core.Calls);

        // Reconcile the predecessor to a provably-safe failure, as a real
        // failed run (or a W4 sweeper) would.
        await database.ExecuteAsync(
            $"""UPDATE "AiOperations" SET "State" = {(int)AiOperationState.FailedTerminal};""");

        var replay = await coordinator.ExecuteAsync(request, default);

        Assert.False(replay.WasDuplicate);
        Assert.Equal(2, core.Calls);
        Assert.Equal(2, replay.Operation.ResourceVersion);
        Assert.NotEqual(first.Operation.IdempotencyKey, replay.Operation.IdempotencyKey);
        Assert.NotEqual(first.Operation.ResourceSlotKey, replay.Operation.ResourceSlotKey);

        await using var count = database.Command("""SELECT count(*) FROM "AiOperations";""");
        Assert.Equal(2L, await count.ExecuteScalarAsync());
    }

    /// <summary>An ambiguous predecessor must survive the same round trip
    /// unchanged: no bump, no new row, no second call — ever.</summary>
    [PostgreSqlFact]
    public async Task ReplayAfterIndeterminate_IsRefused_WithNoSecondProviderCall()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(OperationsDdl);

        var core = new CountingCoreExecutor();
        await using var db = new LearnerDbContext(BuildOptions(database));
        var coordinator = NewCoordinator(db, core);
        var request = BuildRequest("sub-indeterminate-pg", "hash-1");

        await coordinator.ExecuteAsync(request, default);
        await database.ExecuteAsync(
            $"""UPDATE "AiOperations" SET "State" = {(int)AiOperationState.Indeterminate}, "UpdatedAt" = now() - interval '400 days';""");

        var replay = await coordinator.ExecuteAsync(request, default);

        Assert.True(replay.WasDuplicate);
        Assert.Equal(AiOperationState.Indeterminate, replay.Operation.State);
        Assert.Equal(1, core.Calls);

        await using var count = database.Command("""SELECT count(*) FROM "AiOperations";""");
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────

    private static DbContextOptions<LearnerDbContext> BuildOptions(PostgreSqlTestDatabase database)
        => new DbContextOptionsBuilder<LearnerDbContext>()
            .UseNpgsql(database.SchemaConnectionString, npgsql => npgsql.UseVector())
            .Options;

    private static AiExecutionCoordinator NewCoordinator(LearnerDbContext db, CountingCoreExecutor core)
        => new(
            new AiOperationStore(db),
            core,
            featurePolicyRegistry: null,
            pricingResolver: null,
            hostEnvironment: null,
            logger: NullLogger<AiExecutionCoordinator>.Instance);

    private static async Task<RaceOutcome> RunAsync(AiExecutionCoordinator coordinator, AiOperationRequest request)
    {
        try
        {
            var result = await coordinator.ExecuteAsync(request, default);
            return new RaceOutcome(Owned: !result.WasDuplicate, Duplicate: result.WasDuplicate, Conflict: false, InFlight: false);
        }
        catch (AiOperationConflictException)
        {
            return new RaceOutcome(false, false, Conflict: true, InFlight: false);
        }
        catch (AiOperationInFlightException)
        {
            return new RaceOutcome(false, false, false, InFlight: true);
        }
    }

    private sealed record RaceOutcome(bool Owned, bool Duplicate, bool Conflict, bool InFlight);

    private static AiOperationRequest BuildRequest(string resourceId, string requestHash, string module = "writing") => new()
    {
        Module = module,
        ResourceId = resourceId,
        ResourceType = "writing_submission",
        ResourceVersion = 1,
        RequestHash = requestHash,
        PromptVersion = "pv1",
        RulebookVersion = "rb1",
        GatewayRequest = new AiGatewayRequest
        {
            FeatureCode = AiFeatureCodes.WritingGrade,
            UserId = "user-1",
            Prompt = new AiGroundedPrompt { SystemPrompt = "OET AI — Rulebook-Grounded System Prompt", TaskInstruction = "grade" },
        },
    };

    private static AiOperation NewOperation(string id, string key, string? slot) => new()
    {
        Id = id,
        Module = "writing",
        FeatureCode = AiFeatureCodes.WritingGrade,
        UserId = "user-1",
        IdempotencyKey = key,
        ResourceSlotKey = slot,
        State = AiOperationState.Queued,
        OperationClass = AiOperationClass.ScoringCritical,
        AttemptLimit = 1,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Shared across both racing coordinators. The delay widens the
    /// race window so the losing caller is genuinely mid-flight rather than
    /// arriving after the winner already finished.</summary>
    private sealed class CountingCoreExecutor(TimeSpan delay = default) : IAiGatewayCoreExecutor
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public async Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);

            return new AiGatewayResult
            {
                Completion = "ok",
                ResolvedProvider = "anthropic",
                ResolvedModel = "claude-sonnet-5",
                UsageRecordId = "usage-record-1",
                UsagePersisted = true,
            };
        }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context) => new();
    }
}
