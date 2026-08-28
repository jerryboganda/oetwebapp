using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Tests.Infrastructure;
using Xunit;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// W3 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// real-PostgreSQL proof that the platform budget ceiling (owner directive
/// 2026-08-28 AI/Cloud API plan, points 4/5/9/10 — "budget reached -> no
/// calls above the approved limit", "hard and concurrency-safe") is enforced
/// atomically: N concurrent reservations against a shared, small limit can
/// NEVER collectively grant more than the limit allows, because the check and
/// the increment are the same SQL statement (<see cref="AiBudgetService"/>).
///
/// <para>
/// Companion to <see cref="AiExecutionCoordinatorPostgreSqlConcurrencyTests"/>,
/// which proves the "1 AI job / 1 provider call" half of the golden
/// invariant; this class proves the money half.
/// </para>
/// </summary>
public sealed class AiBudgetServicePostgreSqlConcurrencyTests
{
    private const string GlobalPolicyDdl =
        """
        CREATE TABLE "AiGlobalPolicies" (
            "Id" character varying(32) NOT NULL,
            "KillSwitchEnabled" boolean NOT NULL,
            "KillSwitchScope" integer NOT NULL,
            "KillSwitchReason" character varying(256) NULL,
            "DisabledFeaturesCsv" character varying(1024) NOT NULL,
            "MonthlyBudgetUsd" numeric NOT NULL,
            "SoftWarnPct" integer NOT NULL,
            "HardKillPct" integer NOT NULL,
            "CurrentSpendUsd" numeric NOT NULL,
            "AllowByokOnScoringFeatures" boolean NOT NULL,
            "AllowByokOnNonScoringFeatures" boolean NOT NULL,
            "DefaultPlatformProviderId" character varying(64) NOT NULL,
            "ByokErrorCooldownHours" integer NOT NULL,
            "ByokTransientRetryCount" integer NOT NULL,
            "AnomalyDetectionEnabled" boolean NOT NULL,
            "AnomalyMultiplierX" numeric NOT NULL,
            "RowVersion" integer NOT NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            "UpdatedByAdminId" character varying(64) NULL,
            CONSTRAINT "PK_AiGlobalPolicies" PRIMARY KEY ("Id")
        );
        """;

    private const string BudgetPeriodDdl =
        """
        CREATE TABLE "AiBudgetPeriods" (
            "Id" character varying(64) NOT NULL,
            "Scope" character varying(64) NOT NULL,
            "PeriodKey" character varying(16) NOT NULL,
            "LimitUsd" numeric NOT NULL,
            "ReservedUsd" numeric NOT NULL,
            "CommittedUsd" numeric NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            CONSTRAINT "PK_AiBudgetPeriods" PRIMARY KEY ("Id")
        );
        CREATE UNIQUE INDEX "UX_AiBudgetPeriods_Scope_PeriodKey" ON "AiBudgetPeriods" ("Scope", "PeriodKey");
        """;

    /// <summary>
    /// Owner acceptance scenario: "budget reached -> no calls above the
    /// approved limit". A $1.00 ceiling with a $0.10-per-call reservation can
    /// grant AT MOST 10 of 100 genuinely concurrent callers — never 11, never
    /// a partial/negative overshoot — because the WHERE clause and the
    /// increment are one atomic UPDATE, not a read followed by a write.
    /// </summary>
    [PostgreSqlFact]
    public async Task ConcurrentReservations_NeverGrantMoreThanTheLimitAllows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(GlobalPolicyDdl);
        await database.ExecuteAsync(BudgetPeriodDdl);

        const decimal limitUsd = 1.00m;
        const decimal perCallUsd = 0.10m;
        const int expectedGrants = 10; // limitUsd / perCallUsd
        // CI postgres:16-alpine defaults to max_connections=100, and the
        // harness already holds one session. 40 concurrent EF contexts is
        // enough to prove the atomic UPDATE never over-grants; 100-way
        // races starve the server and mis-report as budget_store_unavailable.
        const int totalCallers = 40;

        await SeedGlobalPolicyAsync(database, limitUsd);

        var services = new ServiceCollection()
            .AddDbContext<LearnerDbContext>(o => o.UseNpgsql(database.SchemaConnectionString, npgsql => npgsql.UseVector()))
            .AddSingleton(NullLogger<AiBudgetService>.Instance)
            .BuildServiceProvider();
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var budgetService = new AiBudgetService(scopeFactory, NullLogger<AiBudgetService>.Instance);

        var tasks = new Task<AiBudgetReservation>[totalCallers];
        for (var i = 0; i < totalCallers; i++)
        {
            tasks[i] = budgetService.ReserveAsync("global", perCallUsd, default);
        }
        var results = await Task.WhenAll(tasks);

        var granted = results.Count(r => r.Granted);
        var denied = results.Count(r => !r.Granted);

        // The single load-bearing assertion: never more than the limit allows,
        // no matter how many concurrent callers raced it.
        Assert.Equal(expectedGrants, granted);
        Assert.Equal(totalCallers - expectedGrants, denied);
        Assert.All(results.Where(r => !r.Granted), r => Assert.Equal("global_budget_exhausted", r.DenyReason));

        // The ledger itself must agree: exactly the granted amount reserved,
        // nothing committed yet (no CommitAsync was called), never negative,
        // never over the limit.
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var period = await db.AiBudgetPeriods.AsNoTracking().SingleAsync();
        Assert.Equal(expectedGrants * perCallUsd, period.ReservedUsd);
        Assert.Equal(0m, period.CommittedUsd);
        Assert.True(period.ReservedUsd + period.CommittedUsd <= period.LimitUsd);

        await services.DisposeAsync();
    }

    /// <summary>Reserve → Commit → Release round-trips never drive the ledger
    /// negative or leave a phantom reservation behind, even when many callers
    /// commit/release concurrently against the same period row.</summary>
    [PostgreSqlFact]
    public async Task ConcurrentCommitAndRelease_NeverDriveTheLedgerNegative()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(GlobalPolicyDdl);
        await database.ExecuteAsync(BudgetPeriodDdl);

        const decimal limitUsd = 100.00m;
        const decimal perCallUsd = 0.05m;
        const int totalCallers = 40;

        await SeedGlobalPolicyAsync(database, limitUsd);

        var services = new ServiceCollection()
            .AddDbContext<LearnerDbContext>(o => o.UseNpgsql(database.SchemaConnectionString, npgsql => npgsql.UseVector()))
            .BuildServiceProvider();
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var budgetService = new AiBudgetService(scopeFactory, NullLogger<AiBudgetService>.Instance);

        var reservations = await Task.WhenAll(
            Enumerable.Range(0, totalCallers).Select(_ => budgetService.ReserveAsync("global", perCallUsd, default)));
        Assert.All(reservations, r => Assert.True(r.Granted));

        // Half commit (real spend), half release (call never happened) — both
        // concurrently, against the same period row.
        var settleTasks = reservations.Select((r, i) => i % 2 == 0
            ? budgetService.CommitAsync(r, perCallUsd, default)
            : budgetService.ReleaseAsync(r, default));
        await Task.WhenAll(settleTasks);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var period = await db.AiBudgetPeriods.AsNoTracking().SingleAsync();

        Assert.Equal(0m, period.ReservedUsd); // every reservation settled one way or the other
        Assert.Equal((totalCallers / 2) * perCallUsd, period.CommittedUsd);
        Assert.True(period.ReservedUsd >= 0m);
        Assert.True(period.CommittedUsd >= 0m);

        await services.DisposeAsync();
    }

    private static async Task SeedGlobalPolicyAsync(PostgreSqlTestDatabase database, decimal monthlyBudgetUsd)
    {
        await using var cmd = database.Command(
            """
            INSERT INTO "AiGlobalPolicies" (
                "Id", "KillSwitchEnabled", "KillSwitchScope", "DisabledFeaturesCsv",
                "MonthlyBudgetUsd", "SoftWarnPct", "HardKillPct", "CurrentSpendUsd",
                "AllowByokOnScoringFeatures", "AllowByokOnNonScoringFeatures",
                "DefaultPlatformProviderId", "ByokErrorCooldownHours", "ByokTransientRetryCount",
                "AnomalyDetectionEnabled", "AnomalyMultiplierX", "RowVersion", "UpdatedAt"
            ) VALUES (
                'global', false, 0, '',
                @monthlyBudgetUsd, 80, 100, 0,
                false, true,
                'digitalocean-serverless', 24, 2,
                true, 10, 0, now()
            );
            """);
        cmd.Parameters.AddWithValue("monthlyBudgetUsd", monthlyBudgetUsd);
        await cmd.ExecuteNonQueryAsync();
    }
}
