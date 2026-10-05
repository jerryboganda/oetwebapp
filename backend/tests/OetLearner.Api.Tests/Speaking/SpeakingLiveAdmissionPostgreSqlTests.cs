using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Data.Migrations;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Admin;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Tests.Infrastructure;
using Xunit;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// Live AI Speaking admission control on REAL PostgreSQL (owner decision 5 Oct 2026). The in-memory tests prove the
/// rules; these prove what only the real server can: the hand-authored migration SQL applies (and re-runs), the
/// Postgres advisory lock serialises genuinely concurrent callers so the cap is NEVER exceeded and tickets are
/// strictly FIFO, the subject-state EXISTS subqueries translate, and the ops snapshot's raw
/// <c>pg_stat_activity</c> query maps. Skips without <c>OET_TEST_POSTGRES_CONNECTION</c> (qa-smoke runs it).
/// </summary>
[Collection(PostgreSqlExclusiveCollection.Name)]
public sealed class SpeakingLiveAdmissionPostgreSqlTests
{
    [PostgreSqlFact]
    public async Task ConcurrentCallers_NeverAdmitMoreThanTheCap_AndTheLineIsStrictlyFifo()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await PrepareSchemaAsync(database);
        await using var services = BuildServices(database);
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var options = Options.Create(new SpeakingLiveAdmissionOptions { DefaultMaxConcurrent = 5 });
        const int callers = 24;

        // 24 genuinely concurrent requests, one DbContext (one connection) each, like 24 learners pressing Begin.
        var results = await Task.WhenAll(Enumerable.Range(0, callers).Select(i => Task.Run(async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = new SpeakingLiveAdmissionService(
                scope.ServiceProvider.GetRequiredService<LearnerDbContext>(), options, clock);
            return await service.AdmitOrQueueAsync(
                $"user-{i}", SpeakingLiveAdmissionKinds.Exam, $"exam-{i}", true, CancellationToken.None);
        })));

        // The load-bearing assertion: never more than the cap, however many raced.
        Assert.Equal(5, results.Count(r => r.Outcome == SpeakingLiveAdmissionOutcome.Admitted));
        Assert.Equal(callers - 5, results.Count(r => r.MustWait));
        Assert.DoesNotContain(results, r => r.Outcome == SpeakingLiveAdmissionOutcome.Bypassed);

        await using var readScope = scopeFactory.CreateAsyncScope();
        var db = readScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var rows = await db.SpeakingLiveAdmissions.AsNoTracking().OrderBy(a => a.Seq).ToListAsync();
        Assert.Equal(callers, rows.Count);
        // Tickets are contiguous and unique (the unique index would also have refused a duplicate).
        Assert.Equal(Enumerable.Range(1, callers).Select(n => (long)n), rows.Select(a => a.Seq));
        // Strict FIFO: the five admitted are the five earliest tickets, everyone else waits in order.
        Assert.All(rows.Take(5), a => Assert.Equal(SpeakingLiveAdmissionState.Admitted, a.State));
        Assert.All(rows.Skip(5), a => Assert.Equal(SpeakingLiveAdmissionState.Waiting, a.State));
    }

    [PostgreSqlFact]
    public async Task APlaceFreesWhenItsSubjectEnds_AndTheEarliestWaiterTakesIt()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await PrepareSchemaAsync(database);
        await database.ExecuteAsync(
            $"""
            INSERT INTO "SpeakingExamSessions" ("Id", "State")
            VALUES ('exam-a', {(int)SpeakingExamState.ActiveA}), ('exam-b', {(int)SpeakingExamState.ActiveA});
            """);
        await using var services = BuildServices(database);
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var options = Options.Create(new SpeakingLiveAdmissionOptions { DefaultMaxConcurrent = 1, ClaimWindowSeconds = 30 });

        async Task<SpeakingLiveAdmissionResult> CallAsync(string n)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = new SpeakingLiveAdmissionService(
                scope.ServiceProvider.GetRequiredService<LearnerDbContext>(), options, clock);
            return await service.AdmitOrQueueAsync(
                $"user-{n}", SpeakingLiveAdmissionKinds.Exam, $"exam-{n}", true, CancellationToken.None);
        }

        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, (await CallAsync("a")).Outcome);
        Assert.True((await CallAsync("b")).MustWait);
        Assert.True((await CallAsync("c")).MustWait);

        // Past the claim window A holds its place only because its exam is running (the EXISTS subquery).
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.True((await CallAsync("b")).MustWait);

        // A's exam completes. C is second in line: B, the earliest waiter, takes the one free place first.
        await database.ExecuteAsync(
            $"""UPDATE "SpeakingExamSessions" SET "State" = {(int)SpeakingExamState.Completed} WHERE "Id" = 'exam-a';""");
        var cEarly = await CallAsync("c");
        Assert.True(cEarly.MustWait);
        Assert.Equal(2, cEarly.Waiting!.Position);
        Assert.Equal(SpeakingLiveAdmissionOutcome.Admitted, (await CallAsync("b")).Outcome);

        var last = await CallAsync("c");
        Assert.True(last.MustWait);
        Assert.Equal(1, last.Waiting!.Position);
    }

    [PostgreSqlFact]
    public async Task TheMigrationIsRerunnable_AndTheOpsSnapshotReadsRealPostgres()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await PrepareSchemaAsync(database);
        // Idempotent: a second apply (blue/green overlap, a replayed script) changes nothing and does not fail.
        foreach (var sql in MigrationUpSql())
        {
            await database.ExecuteAsync(sql);
        }

        await database.ExecuteAsync(
            $"""
            INSERT INTO "BackgroundJobs" ("Id", "Type", "State", "AvailableAt", "LastTransitionAt") VALUES
              ('j1', {(int)JobType.WritingEvaluation}, {(int)AsyncState.Queued}, now() - interval '2 minutes', now() - interval '2 minutes'),
              ('j2', {(int)JobType.WritingEvaluation}, {(int)AsyncState.Processing}, now() - interval '20 minutes', now() - interval '15 minutes'),
              ('j3', {(int)JobType.StudyPlanRegeneration}, {(int)AsyncState.Queued}, now() + interval '10 minutes', now());
            """);
        await using var services = BuildServices(database);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var admission = new SpeakingLiveAdmissionService(db, Options.Create(new SpeakingLiveAdmissionOptions()));

        var snapshot = await new AdminOpsSnapshotService(db, admission).GetAsync(CancellationToken.None);

        Assert.Equal(1, snapshot.Jobs.TotalQueued);
        Assert.Equal(1, snapshot.Jobs.TotalProcessing);
        Assert.Equal(1, snapshot.Jobs.Scheduled);
        var writing = Assert.Single(snapshot.Jobs.ByType);
        Assert.Equal(nameof(JobType.WritingEvaluation), writing.Type);
        Assert.Equal(1, writing.StuckProcessing);
        Assert.True(writing.OldestQueuedAgeSeconds >= 110);

        Assert.True(snapshot.Connections.Available, snapshot.Connections.Note);
        Assert.True(snapshot.Connections.MaxConnections > 0);
        // The harness tags every session of this test schema with the schema name as its application_name.
        Assert.Contains(snapshot.Connections.ByApplicationName, row => row.ApplicationName == database.Schema);
        Assert.Equal(100, snapshot.Speaking.Admission!.MaxConcurrent);
        Assert.Equal(0, snapshot.Speaking.Admission.Admitted);
    }

    private static async Task PrepareSchemaAsync(PostgreSqlTestDatabase database)
    {
        foreach (var sql in MigrationUpSql())
        {
            await database.ExecuteAsync(sql);
        }

        // Only the columns the admission and snapshot queries read; EF does not validate the rest of the table.
        await database.ExecuteAsync(
            """
            CREATE TABLE "SpeakingExamSessions" ("Id" character varying(64) PRIMARY KEY, "State" integer NOT NULL);
            CREATE TABLE "SpeakingSessions" ("Id" character varying(64) PRIMARY KEY, "State" integer NOT NULL);
            CREATE TABLE "BackgroundJobs" (
                "Id" character varying(64) PRIMARY KEY,
                "Type" integer NOT NULL,
                "State" integer NOT NULL,
                "AvailableAt" timestamp with time zone NOT NULL,
                "LastTransitionAt" timestamp with time zone NOT NULL);
            """);
    }

    private static ServiceProvider BuildServices(PostgreSqlTestDatabase database)
        => new ServiceCollection()
            .AddDbContext<LearnerDbContext>(o => o.UseNpgsql(database.SchemaConnectionString, npgsql => npgsql.UseVector()))
            .BuildServiceProvider();

    private static string[] MigrationUpSql()
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var up = typeof(AddSpeakingLiveAdmission).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AddSpeakingLiveAdmission.Up was not found.");
        up.Invoke(new AddSpeakingLiveAdmission(), [builder]);
        return builder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql).ToArray();
    }
}
