using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// Owner directive 2026-09-23 — admin-side AI (AdminBatch) carries no day or
/// class-month ceilings. These tests count ledger rows, not intent:
///
/// <para>
/// an AdminBatch call must be granted even when the global DAY period and its
/// own class periods are exhausted, must create zero class period rows, and
/// must still be gated by the global MONTH reserve. Student-facing classes
/// keep every ceiling, and scoring may borrow the exempt Admin donor when its
/// own and Interactive buckets are exhausted.
/// </para>
/// </summary>
public sealed class AiBudgetAdminExemptionTests
{
    private static string MonthKey => $"month:{DateTimeOffset.UtcNow:yyyy-MM}";
    private static string DayKey => $"day:{DateTimeOffset.UtcNow:yyyy-MM-dd}";

    private static (ServiceProvider Provider, Func<LearnerDbContext> DbFactory) BuildHost()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection()
            .AddDbContext<LearnerDbContext>(o => o.UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)))
            .AddSingleton(NullLogger<AiBudgetService>.Instance)
            .BuildServiceProvider();

        return (services, () => services.GetRequiredService<LearnerDbContext>());
    }

    private static async Task SeedGlobalPolicyAsync(LearnerDbContext db, decimal monthlyBudgetUsd = 50m)
    {
        db.AiGlobalPolicies.Add(new AiGlobalPolicy
        {
            Id = "global",
            // These tests pin cap behaviour, which only refuses calls while
            // the admin switch is on (owner directive 2026-10-02, default off).
            EnforceSpendCaps = true,
            KillSwitchEnabled = false,
            KillSwitchScope = AiKillSwitchScope.PlatformKeysOnly,
            DisabledFeaturesCsv = string.Empty,
            MonthlyBudgetUsd = monthlyBudgetUsd,
            SoftWarnPct = 80,
            HardKillPct = 100,
            CurrentSpendUsd = 0m,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedPeriodAsync(
        LearnerDbContext db, string scope, string periodKey, decimal limitUsd, decimal usedUsd)
    {
        db.AiBudgetPeriods.Add(new AiBudgetPeriod
        {
            Id = $"{scope}:{periodKey}",
            Scope = scope,
            PeriodKey = periodKey,
            LimitUsd = limitUsd,
            ReservedUsd = usedUsd,
            CommittedUsd = 0m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AdminBatch_IsGranted_EvenWhenGlobalDayIsExhausted_AndCreatesNoClassPeriods()
    {
        var (services, dbFactory) = BuildHost();
        await using var servicesOwner = services;
        using var db = dbFactory();
        await SeedGlobalPolicyAsync(db);
        // The platform-wide $5 daily ceiling is fully spent.
        await SeedPeriodAsync(db, AiBudgetClasses.GlobalScope, DayKey, limitUsd: AiBudgetClasses.PlatformDailyCapUsd, usedUsd: AiBudgetClasses.PlatformDailyCapUsd);

        var budget = new AiBudgetService(
            services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiBudgetService>.Instance);

        var reservation = await budget.ReserveForCallAsync(
            AiOperationClass.AdminBatch, AiBudgetService.DefaultReservationEstimateUsd, default);

        Assert.True(reservation.Granted);

        // Only the global month period was touched; no class:AdminBatch rows.
        Assert.Equal(
            AiBudgetService.DefaultReservationEstimateUsd,
            await db.AiBudgetPeriods.Where(p => p.Scope == AiBudgetClasses.GlobalScope && p.PeriodKey == MonthKey)
                .SumAsync(p => p.ReservedUsd));
        Assert.False(await db.AiBudgetPeriods.AnyAsync(p => p.Scope == AiBudgetClasses.AdminBatchScope));
    }

    [Fact]
    public async Task AdminBatch_IsStillGated_ByTheGlobalMonthReserve()
    {
        var (services, dbFactory) = BuildHost();
        await using var servicesOwner = services;
        using var db = dbFactory();
        await SeedGlobalPolicyAsync(db, monthlyBudgetUsd: 50m);
        await SeedPeriodAsync(db, AiBudgetClasses.GlobalScope, MonthKey, limitUsd: 50m, usedUsd: 50m);

        var budget = new AiBudgetService(
            services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiBudgetService>.Instance);

        var reservation = await budget.ReserveForCallAsync(
            AiOperationClass.AdminBatch, AiBudgetService.DefaultReservationEstimateUsd, default);

        Assert.False(reservation.Granted);
        Assert.Equal("global_budget_exhausted", reservation.DenyReason);
    }

    [Fact]
    public async Task Interactive_StillDenied_WhenGlobalDayIsExhausted()
    {
        var (services, dbFactory) = BuildHost();
        await using var servicesOwner = services;
        using var db = dbFactory();
        await SeedGlobalPolicyAsync(db);
        await SeedPeriodAsync(db, AiBudgetClasses.GlobalScope, DayKey, limitUsd: AiBudgetClasses.PlatformDailyCapUsd, usedUsd: AiBudgetClasses.PlatformDailyCapUsd);

        var budget = new AiBudgetService(
            services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiBudgetService>.Instance);

        var reservation = await budget.ReserveForCallAsync(
            AiOperationClass.InteractiveLearning, AiBudgetService.DefaultReservationEstimateUsd, default);

        Assert.False(reservation.Granted);
        Assert.Equal("global_daily_budget_exhausted", reservation.DenyReason);
    }

    [Fact]
    public async Task Interactive_StillDenied_WhenItsClassDayIsExhausted()
    {
        var (services, dbFactory) = BuildHost();
        await using var servicesOwner = services;
        using var db = dbFactory();
        await SeedGlobalPolicyAsync(db);
        await SeedPeriodAsync(db, AiBudgetClasses.InteractiveLearningScope, DayKey, limitUsd: 1m, usedUsd: 1m);

        var budget = new AiBudgetService(
            services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiBudgetService>.Instance);

        var reservation = await budget.ReserveForCallAsync(
            AiOperationClass.InteractiveLearning, AiBudgetService.DefaultReservationEstimateUsd, default);

        Assert.False(reservation.Granted);
        Assert.Equal("class_budget_exhausted", reservation.DenyReason);
    }

    [Fact]
    public async Task AdminBatch_IsGranted_BeyondItsRetiredClassMonthCeiling()
    {
        var (services, dbFactory) = BuildHost();
        await using var servicesOwner = services;
        using var db = dbFactory();
        await SeedGlobalPolicyAsync(db);
        // The retired $5 Admin month ceiling, pre-spent by legacy rows.
        await SeedPeriodAsync(db, AiBudgetClasses.AdminBatchScope, MonthKey, limitUsd: 5m, usedUsd: 5m);
        await SeedPeriodAsync(db, AiBudgetClasses.AdminBatchScope, DayKey, limitUsd: 0.5m, usedUsd: 0.5m);

        var budget = new AiBudgetService(
            services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiBudgetService>.Instance);

        var reservation = await budget.ReserveForCallAsync(
            AiOperationClass.AdminBatch, AiBudgetService.DefaultReservationEstimateUsd, default);

        Assert.True(reservation.Granted);
        Assert.Null(reservation.ClassPeriodId);
        Assert.Null(reservation.BorrowPeriodId);
    }

    [Fact]
    public async Task Scoring_CanBorrow_TheExemptAdminDonor_WhenOwnAndInteractiveBucketsAreExhausted()
    {
        var (services, dbFactory) = BuildHost();
        await using var servicesOwner = services;
        using var db = dbFactory();
        await SeedGlobalPolicyAsync(db, monthlyBudgetUsd: 50m);
        await SeedPeriodAsync(db, AiBudgetClasses.ScoringCriticalScope, MonthKey, limitUsd: AiBudgetClasses.ScoringMonthlyLimitUsd, usedUsd: AiBudgetClasses.ScoringMonthlyLimitUsd);
        await SeedPeriodAsync(db, AiBudgetClasses.ScoringCriticalScope, DayKey, limitUsd: AiBudgetClasses.ScoringDailyLimitUsd, usedUsd: AiBudgetClasses.ScoringDailyLimitUsd);
        await SeedPeriodAsync(db, AiBudgetClasses.InteractiveLearningScope, MonthKey, limitUsd: 10m, usedUsd: 10m);
        await SeedPeriodAsync(db, AiBudgetClasses.InteractiveLearningScope, DayKey, limitUsd: 1m, usedUsd: 1m);

        var budget = new AiBudgetService(
            services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiBudgetService>.Instance);

        var reservation = await budget.ReserveForCallAsync(
            AiOperationClass.ScoringCritical, AiBudgetService.DefaultReservationEstimateUsd, default);

        // The exempt Admin donor grants headroom without holding any period.
        Assert.True(reservation.Granted);
        Assert.Null(reservation.BorrowPeriodId);
        Assert.Equal(0.10m,
            await db.AiBudgetPeriods.Where(p => p.Scope == AiBudgetClasses.GlobalScope && p.PeriodKey == MonthKey)
                .SumAsync(p => p.ReservedUsd));
    }
}
