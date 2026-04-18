using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;

namespace OetLearner.Api.Tests;

public class AiCreditServiceTests
{
    private static (LearnerDbContext db, AiCreditService credits) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new AiCreditService(db));
    }

    [Fact]
    public async Task Grant_IncreasesBalance()
    {
        var (db, credits) = Build();
        await credits.GrantAsync("u1", 1000, 0.05m, AiCreditSource.PlanRenewal, "March", null, null, null, default);
        var balance = await credits.GetBalanceAsync("u1", default);
        Assert.Equal(1000, balance.TokensAvailable);
        Assert.Equal(1000, balance.TokensGrantedLifetime);
        Assert.Equal(0, balance.TokensConsumedLifetime);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SweepExpired_ZeroesOutExpiredGrants()
    {
        var (db, credits) = Build();
        await credits.GrantAsync("u1", 500, 0m, AiCreditSource.Promo, "trial",
            null, DateTimeOffset.UtcNow.AddDays(-1), null, default);
        await credits.GrantAsync("u1", 200, 0m, AiCreditSource.PlanRenewal, "Apr",
            null, DateTimeOffset.UtcNow.AddDays(30), null, default);

        // Balance filter already excludes past-expiry grants even before sweep.
        var beforeSweep = await credits.GetBalanceAsync("u1", default);
        Assert.Equal(200, beforeSweep.TokensAvailable);

        var expired = await credits.SweepExpiredAsync(DateTimeOffset.UtcNow, default);
        Assert.Equal(1, expired);

        // After sweep, the ExpiredByEntryId flag is set, so subsequent sweeps
        // won't re-run against this grant (idempotent).
        var afterSweep = await credits.GetBalanceAsync("u1", default);
        Assert.Equal(200, afterSweep.TokensAvailable);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SweepExpired_IsIdempotent()
    {
        var (db, credits) = Build();
        await credits.GrantAsync("u1", 100, 0m, AiCreditSource.Promo, null, null,
            DateTimeOffset.UtcNow.AddDays(-1), null, default);
        var first = await credits.SweepExpiredAsync(DateTimeOffset.UtcNow, default);
        var second = await credits.SweepExpiredAsync(DateTimeOffset.UtcNow, default);
        Assert.Equal(1, first);
        Assert.Equal(0, second);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Grant_RejectsNonPositive()
    {
        var (db, credits) = Build();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            credits.GrantAsync("u1", 0, 0m, AiCreditSource.Promo, null, null, null, null, default));
        await db.DisposeAsync();
    }
}
