using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// W4 — two workers cannot lease the same operation; an expired lease is
/// reclaimed once; ProviderSucceeded resumes with zero extra provider calls.
/// </summary>
[Collection(PostgreSqlExclusiveCollection.Name)]
public sealed class AiOperationLeaseTests
{
    private const string OperationsDdl = """
        CREATE TABLE "AiOperations" (
            "Id" varchar(64) PRIMARY KEY,
            "Module" varchar(32) NOT NULL DEFAULT '',
            "FeatureCode" varchar(64) NOT NULL DEFAULT '',
            "IdempotencyKey" varchar(256) NOT NULL DEFAULT '',
            "State" integer NOT NULL,
            "NextAttemptAt" timestamptz,
            "LeaseOwner" varchar(128),
            "LeaseExpiresAt" timestamptz,
            "CreatedAt" timestamptz NOT NULL,
            "UpdatedAt" timestamptz NOT NULL,
            "ResultRef" varchar(128)
        );
        """;

    [PostgreSqlFact]
    public async Task TwoWorkers_CannotLeaseTheSameOperation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(OperationsDdl);
        await InsertAsync(database, "op-1", (int)AiOperationState.Queued, leaseOwner: null, leaseExpiresAt: null);

        var now = DateTimeOffset.UtcNow;
        await using var db1 = CreateContext(database);
        await using var db2 = CreateContext(database);
        var claimer = new AiOperationLeaseClaimer();

        var first = claimer.ClaimAsync(db1, "worker-a", now, TimeSpan.FromMinutes(30), 10, CancellationToken.None);
        var second = claimer.ClaimAsync(db2, "worker-b", now, TimeSpan.FromMinutes(30), 10, CancellationToken.None);
        await Task.WhenAll(first, second);

        var total = first.Result.Count + second.Result.Count;
        Assert.Equal(1, total);
        var winner = first.Result.Concat(second.Result).Single();
        Assert.Equal("op-1", winner.Id);
        Assert.Equal(AiOperationState.Leased, winner.State);
    }

    [PostgreSqlFact]
    public async Task ExpiredLease_IsReclaimedExactlyOnce()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(OperationsDdl);
        var past = DateTimeOffset.UtcNow.AddMinutes(-5);
        await InsertAsync(database, "op-expired", (int)AiOperationState.Leased, "dead-worker", past);

        var now = DateTimeOffset.UtcNow;
        await using var db1 = CreateContext(database);
        await using var db2 = CreateContext(database);
        var claimer = new AiOperationLeaseClaimer();

        var first = await claimer.ClaimAsync(db1, "worker-a", now, TimeSpan.FromMinutes(30), 10, CancellationToken.None);
        var second = await claimer.ClaimAsync(db2, "worker-b", now, TimeSpan.FromMinutes(30), 10, CancellationToken.None);

        Assert.Single(first);
        Assert.Empty(second);
        Assert.Equal("op-expired", first[0].Id);
    }

    [Fact]
    public async Task ProviderSucceeded_ResumesFromPersistedResult_WithZeroProviderCalls()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<LearnerDbContext>(o => o.UseInMemoryDatabase(dbName));
        await using var sp = services.BuildServiceProvider();

        await using (var seedScope = sp.CreateAsyncScope())
        {
            var seed = seedScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            seed.AiOperations.Add(new AiOperation
            {
                Id = "op-resume",
                Module = "listening",
                FeatureCode = "listening.part_a.score",
                IdempotencyKey = "key-resume",
                State = AiOperationState.ProviderSucceeded,
                ResultRef = "usage-1",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        var handler = new AiLeasedOperationHandler(sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiLeasedOperationHandler>.Instance);
        await handler.HandleAsync(new AiLeasedOperation("op-resume", AiOperationState.ProviderSucceeded, "usage-1"), CancellationToken.None);

        Assert.Equal(0, handler.ProviderInvocationCount);
        await using var verifyScope = sp.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var row = await verify.AiOperations.SingleAsync(o => o.Id == "op-resume");
        Assert.Equal(AiOperationState.Completed, row.State);
        Assert.Null(row.LeaseOwner);
        Assert.Equal("usage-1", row.ResultRef);
    }

    private static LearnerDbContext CreateContext(PostgreSqlTestDatabase database)
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseNpgsql(database.SchemaConnectionString)
            .Options);

    private static async Task InsertAsync(
        PostgreSqlTestDatabase database,
        string id,
        int state,
        string? leaseOwner,
        DateTimeOffset? leaseExpiresAt)
    {
        var owner = leaseOwner is null ? "NULL" : $"'{leaseOwner.Replace("'", "''")}'";
        var expires = leaseExpiresAt is null ? "NULL" : $"TIMESTAMPTZ '{leaseExpiresAt.Value.UtcDateTime:yyyy-MM-dd HH:mm:ss}+00'";
        await database.ExecuteAsync($"""
            INSERT INTO "AiOperations" ("Id", "State", "LeaseOwner", "LeaseExpiresAt", "CreatedAt", "UpdatedAt")
            VALUES ('{id}', {state}, {owner}, {expires}, NOW(), NOW());
            """);
    }
}
