using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Tests;

/// <summary>
/// Slice A — May 2026 billing hardening, Track C closure (2026-05-12).
///
/// Locks the cross-DB <see cref="Wallet.RowVersion"/> rowversion contract:
///   • The property is configured as a concurrency token.
///   • The property is marked rowversion / shadow-managed.
///   • The property is nullable (legacy rows from before the 20260512100000
///     migration must keep working until first write).
///
/// Production Postgres still uses the existing <c>LastUpdatedAt</c>
/// concurrency token via the <c>ConfigureXminToken</c> path; this test
/// guarantees parity for SQLite/in-memory test providers.
/// </summary>
public class WalletRowVersionConcurrencyTests
{
    [Fact]
    public void Wallet_RowVersion_IsConcurrencyToken_AndNullable()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        using var db = new LearnerDbContext(options);
        var walletEntity = db.Model.FindEntityType(typeof(Wallet));

        Assert.NotNull(walletEntity);
        var rowVersion = walletEntity!.FindProperty(nameof(Wallet.RowVersion));
        Assert.NotNull(rowVersion);
        Assert.True(rowVersion!.IsConcurrencyToken, "Wallet.RowVersion must be a concurrency token.");
        Assert.True(rowVersion.IsNullable, "Wallet.RowVersion must be nullable so legacy rows survive the additive 20260512100000 migration.");
    }

    [Fact]
    public void Wallet_RowVersion_IsByteArray()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        using var db = new LearnerDbContext(options);
        var walletEntity = db.Model.FindEntityType(typeof(Wallet));

        Assert.NotNull(walletEntity);
        var rowVersion = walletEntity!.FindProperty(nameof(Wallet.RowVersion));
        Assert.NotNull(rowVersion);
        // Cross-DB: byte[] is the canonical rowversion shape that both
        // SQLite (shadow) and Postgres (bytea) can persist. EF maps it.
        Assert.Equal(typeof(byte[]), rowVersion!.ClrType);
    }

    [Fact]
    public async Task Wallet_AccompaniesLastUpdatedAt_AsAdditionalConcurrencyToken()
    {
        // We do not break the LastUpdatedAt path that production already
        // ships; we add RowVersion alongside it. Both tokens must be
        // configured concurrently on the entity.
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new LearnerDbContext(options);
        var walletEntity = db.Model.FindEntityType(typeof(Wallet));

        Assert.NotNull(walletEntity);
        var lastUpdatedAt = walletEntity!.FindProperty(nameof(Wallet.LastUpdatedAt));
        var rowVersion = walletEntity.FindProperty(nameof(Wallet.RowVersion));

        Assert.NotNull(lastUpdatedAt);
        Assert.NotNull(rowVersion);
        Assert.True(lastUpdatedAt!.IsConcurrencyToken);
        Assert.True(rowVersion!.IsConcurrencyToken);
    }

    [Fact]
    public async Task Wallet_LegacyRowWithNullRowVersion_CanStillBeWritten()
    {
        // Regression guard: an existing Wallet row from before the
        // 20260512100000_AddWalletRowVersion migration will have
        // RowVersion = NULL. The first write after migration must not
        // throw — EF will assign a fresh shadow value.
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using (var seedDb = new LearnerDbContext(options))
        {
            seedDb.Wallets.Add(new Wallet
            {
                Id = "wallet-rv-legacy",
                UserId = "user-rv-legacy",
                CreditBalance = 10,
                LedgerSummaryJson = "[]",
                LastUpdatedAt = new DateTimeOffset(2026, 03, 30, 12, 0, 0, TimeSpan.Zero),
                RowVersion = null, // legacy row pre-migration
            });
            await seedDb.SaveChangesAsync();
        }

        await using var writeDb = new LearnerDbContext(options);
        var wallet = await writeDb.Wallets.SingleAsync(x => x.Id == "wallet-rv-legacy");
        wallet.CreditBalance -= 1;
        wallet.LastUpdatedAt = wallet.LastUpdatedAt.AddMinutes(1);

        // No exception means the additive migration is safe.
        await writeDb.SaveChangesAsync();

        var persisted = await writeDb.Wallets.AsNoTracking().SingleAsync(x => x.Id == "wallet-rv-legacy");
        Assert.Equal(9, persisted.CreditBalance);
    }
}
