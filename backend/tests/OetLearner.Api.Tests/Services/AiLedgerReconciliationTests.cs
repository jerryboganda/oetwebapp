using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;

namespace OetLearner.Api.Tests.Services;

public sealed class AiLedgerReconciliationTests
{
    [Fact]
    public async Task Report_does_not_mutate_balances()
    {
        var db = NewDb();
        db.AiCreditLedger.Add(new AiCreditLedgerEntry
        {
            Id = "led1",
            UserId = "u1",
            TokensDelta = 40,
            Source = AiCreditSource.Purchase,
            CreatedAt = DateTimeOffset.UtcNow,
            ReferenceId = "pay_1",
        });
        db.AiPackageCreditAccounts.Add(new AiPackageCreditAccount
        {
            Id = "acc1",
            UserId = "u1",
            SharedCredits = 10,
            FlexibleCredits = 5,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var report = await new AiLedgerReconciliationService(db).BuildReportAsync(default);
        Assert.False(report.MutatedBalances);
        Assert.Equal(1, report.DiscrepancyCount);
        Assert.Equal(40, await db.AiCreditLedger.SumAsync(e => e.TokensDelta));
        Assert.Equal(10, await db.AiPackageCreditAccounts.SumAsync(a => a.SharedCredits));
    }

    private static LearnerDbContext NewDb()
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
