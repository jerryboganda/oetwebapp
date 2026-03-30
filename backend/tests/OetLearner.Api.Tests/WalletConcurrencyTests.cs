using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Tests;

public class WalletConcurrencyTests
{
    [Fact]
    public void Wallet_LastUpdatedAt_IsConfiguredAsConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        using var db = new LearnerDbContext(options);
        var walletEntity = db.Model.FindEntityType(typeof(Wallet));

        Assert.NotNull(walletEntity);
        var lastUpdatedAt = walletEntity!.FindProperty(nameof(Wallet.LastUpdatedAt));
        Assert.NotNull(lastUpdatedAt);
        Assert.True(lastUpdatedAt!.IsConcurrencyToken);
    }

    [Fact]
    public async Task Wallet_ConcurrentUpdates_ThrowConcurrencyException()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var initialTimestamp = new DateTimeOffset(2026, 03, 30, 12, 0, 0, TimeSpan.Zero);
        await using (var seedDb = new LearnerDbContext(options))
        {
            seedDb.Wallets.Add(new Wallet
            {
                Id = "wallet-001",
                UserId = "user-001",
                CreditBalance = 5,
                LedgerSummaryJson = "[]",
                LastUpdatedAt = initialTimestamp,
            });
            await seedDb.SaveChangesAsync();
        }

        await using var firstDb = new LearnerDbContext(options);
        await using var secondDb = new LearnerDbContext(options);

        var firstWallet = await firstDb.Wallets.SingleAsync(x => x.UserId == "user-001");
        var secondWallet = await secondDb.Wallets.SingleAsync(x => x.UserId == "user-001");

        firstWallet.CreditBalance -= 2;
        firstWallet.LastUpdatedAt = initialTimestamp.AddMinutes(1);
        await firstDb.SaveChangesAsync();

        secondWallet.CreditBalance -= 2;
        secondWallet.LastUpdatedAt = initialTimestamp.AddMinutes(2);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondDb.SaveChangesAsync());
    }
}
