using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// Owner directive 2026-10-02: the platform $ caps are ONE admin switch
/// (<see cref="AiGlobalPolicy.EnforceSpendCaps"/>), off by default. Same fully
/// exhausted ledger both ways: off grants past every ceiling and still holds
/// every period (so Commit/Release settle them), on refuses as before.
/// </summary>
public sealed class AiBudgetSpendCapSwitchTests
{
    private static string MonthKey => $"month:{DateTimeOffset.UtcNow:yyyy-MM}";
    private static string DayKey => $"day:{DateTimeOffset.UtcNow:yyyy-MM-dd}";

    [Fact]
    public async Task CapsOff_ScoringIsGranted_PastEveryExhaustedCeiling_AndEveryPeriodIsHeld()
    {
        await using var services = BuildHost();
        await SeedExhaustedLedgerAsync(services, enforceSpendCaps: false);
        var budget = new AiBudgetService(
            services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiBudgetService>.Instance);

        var reservation = await budget.ReserveForCallAsync(
            AiOperationClass.ScoringCritical, AiBudgetService.DefaultReservationEstimateUsd, default);

        Assert.True(reservation.Granted);
        Assert.Null(reservation.DenyReason);
        Assert.Null(reservation.BorrowPeriodId);
        var expectedIds = new[]
        {
            $"{AiBudgetClasses.GlobalScope}:{MonthKey}",
            $"{AiBudgetClasses.GlobalScope}:{DayKey}",
            $"{AiBudgetClasses.ScoringCriticalScope}:{MonthKey}",
            $"{AiBudgetClasses.ScoringCriticalScope}:{DayKey}",
        };
        Assert.Equal(expectedIds, reservation.PeriodIdsToSettle());

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var periods = await db.AiBudgetPeriods.AsNoTracking().ToDictionaryAsync(p => p.Id);
        Assert.Equal(50.10m, periods[expectedIds[0]].ReservedUsd);
        Assert.Equal(50m, periods[expectedIds[0]].LimitUsd);
        Assert.Equal(AiBudgetClasses.PlatformDailyCapUsd + 0.10m, periods[expectedIds[1]].ReservedUsd);
        Assert.Equal(AiBudgetClasses.ScoringMonthlyLimitUsd + 0.10m, periods[expectedIds[2]].ReservedUsd);
        Assert.Equal(AiBudgetClasses.ScoringDailyLimitUsd + 0.10m, periods[expectedIds[3]].ReservedUsd);
    }

    [Fact]
    public async Task CapsOn_SameExhaustedLedger_IsRefused()
    {
        await using var services = BuildHost();
        await SeedExhaustedLedgerAsync(services, enforceSpendCaps: true);
        var budget = new AiBudgetService(
            services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiBudgetService>.Instance);

        var call = await budget.ReserveForCallAsync(
            AiOperationClass.ScoringCritical, AiBudgetService.DefaultReservationEstimateUsd, default);
        var monthly = await budget.ReserveAsync(
            AiBudgetClasses.GlobalScope, AiBudgetService.DefaultReservationEstimateUsd, default);

        Assert.False(call.Granted);
        Assert.Equal("global_budget_exhausted", call.DenyReason);
        Assert.False(monthly.Granted);
        Assert.Equal("global_budget_exhausted", monthly.DenyReason);
    }

    [Fact]
    public async Task CapsOff_SingleScopeReserve_IsGranted_PastTheMonthlyBudget()
    {
        await using var services = BuildHost();
        await SeedExhaustedLedgerAsync(services, enforceSpendCaps: false);
        var budget = new AiBudgetService(
            services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiBudgetService>.Instance);

        var reservation = await budget.ReserveAsync(
            AiBudgetClasses.GlobalScope, AiBudgetService.DefaultReservationEstimateUsd, default);

        Assert.True(reservation.Granted);
        Assert.Equal($"{AiBudgetClasses.GlobalScope}:{MonthKey}", reservation.PeriodId);
    }

    private static ServiceProvider BuildHost()
    {
        var dbName = Guid.NewGuid().ToString("N");
        return new ServiceCollection()
            .AddDbContext<LearnerDbContext>(o => o.UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)))
            .BuildServiceProvider();
    }

    /// <summary>$50 month budget, and every ceiling a scoring call reserves
    /// (or could borrow) already full.</summary>
    private static async Task SeedExhaustedLedgerAsync(ServiceProvider services, bool enforceSpendCaps)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;
        db.AiGlobalPolicies.Add(new AiGlobalPolicy
        {
            Id = "global",
            EnforceSpendCaps = enforceSpendCaps,
            MonthlyBudgetUsd = 50m,
            HardKillPct = 100,
            UpdatedAt = now,
        });

        void Full(string scopeName, string periodKey, decimal limitUsd) => db.AiBudgetPeriods.Add(new AiBudgetPeriod
        {
            Id = $"{scopeName}:{periodKey}",
            Scope = scopeName,
            PeriodKey = periodKey,
            LimitUsd = limitUsd,
            ReservedUsd = limitUsd,
            CommittedUsd = 0m,
            CreatedAt = now,
            UpdatedAt = now,
        });

        Full(AiBudgetClasses.GlobalScope, MonthKey, 50m);
        Full(AiBudgetClasses.GlobalScope, DayKey, AiBudgetClasses.PlatformDailyCapUsd);
        Full(AiBudgetClasses.ScoringCriticalScope, MonthKey, AiBudgetClasses.ScoringMonthlyLimitUsd);
        Full(AiBudgetClasses.ScoringCriticalScope, DayKey, AiBudgetClasses.ScoringDailyLimitUsd);
        Full(AiBudgetClasses.InteractiveLearningScope, MonthKey, AiBudgetClasses.InteractiveMonthlyLimitUsd);
        Full(AiBudgetClasses.InteractiveLearningScope, DayKey, AiBudgetClasses.InteractiveDailyLimitUsd);
        await db.SaveChangesAsync();
    }
}
