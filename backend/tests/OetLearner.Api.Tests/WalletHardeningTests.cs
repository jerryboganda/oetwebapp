using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// Slice A — Wallet hardening regressions (May 2026 billing pass).
/// Exercises <see cref="WalletService"/> and <see cref="AdminWalletTierService"/>
/// directly against an in-memory <see cref="LearnerDbContext"/> so tests stay
/// fast and provider-agnostic.
/// </summary>
public class WalletHardeningTests
{
    // ── Test scaffolding ───────────────────────────────────────────────────

    private static DbContextOptions<LearnerDbContext> NewOptions()
        => new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static WalletService NewWalletService(LearnerDbContext db)
    {
        // The hardened CreditAsync/DebitAsync paths used in these tests do
        // not call into the payment gateway or platform-link helpers, so
        // null-typed stubs are sufficient. Cast through `default!` keeps
        // the signature happy without dragging in a full DI container.
        return new WalletService(
            db,
            paymentGateways: null!,
            platformLinks: null!,
            billingOptions: Options.Create(new BillingOptions
            {
                Wallet = new WalletBillingOptions { Currency = "AUD" }
            }));
    }

    private static async Task<Wallet> SeedWalletAsync(LearnerDbContext db, int balance)
    {
        var wallet = new Wallet
        {
            Id = $"wallet-{Guid.NewGuid():N}",
            UserId = $"user-{Guid.NewGuid():N}",
            CreditBalance = balance,
            LedgerSummaryJson = "[]",
            LastUpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Wallets.Add(wallet);
        await db.SaveChangesAsync();
        return wallet;
    }

    // ── 1. Atomic mutations + audit trail ──────────────────────────────────

    [Fact]
    public async Task CreditAsync_AppendsTransactionAndAuditEvent()
    {
        var options = NewOptions();
        await using var db = new LearnerDbContext(options);
        var wallet = await SeedWalletAsync(db, balance: 0);

        var service = NewWalletService(db);
        var tx = await service.CreditAsync(
            wallet.Id, amount: 50, transactionType: "top_up",
            referenceType: null, referenceId: null,
            description: null, createdBy: "system",
            ct: CancellationToken.None);

        Assert.Equal(50, tx.Amount);
        Assert.Equal(50, tx.BalanceAfter);

        var refreshed = await db.Wallets.AsNoTracking().FirstAsync(w => w.Id == wallet.Id);
        Assert.Equal(50, refreshed.CreditBalance);

        var audits = await db.AuditEvents
            .Where(e => e.ResourceType == "Wallet" && e.ResourceId == wallet.Id)
            .ToListAsync();
        Assert.Single(audits);
        Assert.Equal("wallet.credit", audits[0].Action);
        Assert.Contains("amount=50", audits[0].Details ?? "");
    }

    [Fact]
    public async Task DebitAsync_AppendsTransactionAndAuditEvent()
    {
        var options = NewOptions();
        await using var db = new LearnerDbContext(options);
        var wallet = await SeedWalletAsync(db, balance: 30);

        var service = NewWalletService(db);
        var tx = await service.DebitAsync(
            wallet.Id, amount: 10, transactionType: "review",
            referenceType: "review", referenceId: "rev-1",
            description: null, createdBy: "system",
            ct: CancellationToken.None);

        Assert.Equal(-10, tx.Amount);
        Assert.Equal(20, tx.BalanceAfter);

        var audit = await db.AuditEvents
            .SingleAsync(e => e.ResourceType == "Wallet" && e.ResourceId == wallet.Id);
        Assert.Equal("wallet.debit", audit.Action);
    }

    // ── 2. Negative-balance invariant ──────────────────────────────────────

    [Fact]
    public async Task DebitAsync_RejectsWhenBalanceInsufficient()
    {
        var options = NewOptions();
        await using var db = new LearnerDbContext(options);
        var wallet = await SeedWalletAsync(db, balance: 5);

        var service = NewWalletService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DebitAsync(
                wallet.Id, amount: 10, transactionType: "review",
                referenceType: null, referenceId: null,
                description: null, createdBy: "system",
                ct: CancellationToken.None));

        var refreshed = await db.Wallets.AsNoTracking().FirstAsync(w => w.Id == wallet.Id);
        Assert.Equal(5, refreshed.CreditBalance);

        // No transaction nor audit row should leak from a rejected debit.
        Assert.Empty(await db.WalletTransactions.Where(t => t.WalletId == wallet.Id).ToListAsync());
        Assert.Empty(await db.AuditEvents.Where(e => e.ResourceId == wallet.Id).ToListAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreditAsync_RejectsNonPositiveAmount(int amount)
    {
        var options = NewOptions();
        await using var db = new LearnerDbContext(options);
        var wallet = await SeedWalletAsync(db, balance: 10);
        var service = NewWalletService(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreditAsync(
                wallet.Id, amount, transactionType: "top_up",
                referenceType: null, referenceId: null,
                description: null, createdBy: "system",
                ct: CancellationToken.None));
    }

    // ── 3. Idempotency replay ──────────────────────────────────────────────

    [Fact]
    public async Task CreditAsync_WithSameIdempotencyKey_AppliesOnceAndReplaysSameTransaction()
    {
        var options = NewOptions();
        await using var db = new LearnerDbContext(options);
        var wallet = await SeedWalletAsync(db, balance: 0);
        var service = NewWalletService(db);

        var idem = $"k-{Guid.NewGuid():N}";

        var first = await service.CreditAsync(
            wallet.Id, amount: 25, transactionType: "top_up",
            referenceType: null, referenceId: null,
            description: null, createdBy: "system",
            idempotencyKey: idem, ct: CancellationToken.None);

        var second = await service.CreditAsync(
            wallet.Id, amount: 25, transactionType: "top_up",
            referenceType: null, referenceId: null,
            description: null, createdBy: "system",
            idempotencyKey: idem, ct: CancellationToken.None);

        Assert.Equal(first.Id, second.Id);

        var refreshed = await db.Wallets.AsNoTracking().FirstAsync(w => w.Id == wallet.Id);
        Assert.Equal(25, refreshed.CreditBalance); // applied exactly once

        var txCount = await db.WalletTransactions.CountAsync(t => t.WalletId == wallet.Id);
        Assert.Equal(1, txCount);
    }

    // ── 4. Concurrent mutation detection ───────────────────────────────────

    [Fact]
    public async Task ConcurrentDebit_OnSameWallet_SecondWriterDetectsConcurrencyViolation()
    {
        // EF's concurrency token on Wallet.LastUpdatedAt is configured via
        // OnModelCreating; this test confirms the wallet service participates
        // in that contract — two contexts cannot blindly debit in parallel.
        var options = NewOptions();
        string walletId;
        await using (var seedDb = new LearnerDbContext(options))
        {
            var w = await SeedWalletAsync(seedDb, balance: 100);
            walletId = w.Id;
        }

        await using var dbA = new LearnerDbContext(options);
        await using var dbB = new LearnerDbContext(options);
        var serviceA = NewWalletService(dbA);

        // Force dbB to load the wallet first so it has a stale token,
        // then serviceA commits, then serviceB attempts to commit.
        var stale = await dbB.Wallets.FirstAsync(w => w.Id == walletId);
        var staleToken = stale.LastUpdatedAt;

        await serviceA.DebitAsync(
            walletId, amount: 10, transactionType: "review",
            referenceType: null, referenceId: null,
            description: null, createdBy: "system",
            ct: CancellationToken.None);

        // Mutate the stale tracked entity directly to simulate a racing writer
        // that already had the row loaded before serviceA committed.
        stale.CreditBalance -= 10;
        stale.LastUpdatedAt = staleToken.AddMilliseconds(1);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());
    }

    // ── 5. Tier slug rules (kebab-case, immutability, uniqueness) ──────────

    [Fact]
    public async Task AdminWalletTierService_RejectsInvalidSlugFormat()
    {
        var options = NewOptions();
        await using var db = new LearnerDbContext(options);
        var service = NewAdminTierService(db);

        var ex = await Assert.ThrowsAsync<AdminWalletTierValidationException>(() =>
            service.ReplaceAsync("admin-1", "Admin", new AdminWalletTierReplaceRequest
            {
                Tiers = new()
                {
                    new AdminWalletTierInput
                    {
                        Slug = "Bad_Slug!", // not kebab-case
                        Amount = 10, Credits = 10, Bonus = 0, IsActive = true,
                        DisplayOrder = 0, Currency = "AUD",
                    }
                }
            }, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.Contains("slug", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AdminWalletTierService_RejectsDuplicateSlugWithinPayload()
    {
        var options = NewOptions();
        await using var db = new LearnerDbContext(options);
        var service = NewAdminTierService(db);

        var ex = await Assert.ThrowsAsync<AdminWalletTierValidationException>(() =>
            service.ReplaceAsync("admin-1", "Admin", new AdminWalletTierReplaceRequest
            {
                Tiers = new()
                {
                    new AdminWalletTierInput { Slug = "starter", Amount = 10, Credits = 10, Bonus = 0, IsActive = true, DisplayOrder = 0, Currency = "AUD" },
                    new AdminWalletTierInput { Slug = "starter", Amount = 25, Credits = 25, Bonus = 1, IsActive = true, DisplayOrder = 1, Currency = "AUD" },
                }
            }, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.Contains("duplicate slug", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AdminWalletTierService_RejectsSlugChangeOnExistingTier()
    {
        var options = NewOptions();
        await using var db = new LearnerDbContext(options);
        var service = NewAdminTierService(db);

        // Seed with one tier.
        await service.ReplaceAsync("admin-1", "Admin", new AdminWalletTierReplaceRequest
        {
            Tiers = new()
            {
                new AdminWalletTierInput { Slug = "starter", Amount = 10, Credits = 10, Bonus = 0, IsActive = true, DisplayOrder = 0, Currency = "AUD" },
            }
        }, CancellationToken.None);

        var seeded = await db.WalletTopUpTierConfigs.AsNoTracking().SingleAsync();
        Assert.Equal("starter", seeded.Slug);

        // Attempt to mutate slug on the same id.
        var ex = await Assert.ThrowsAsync<AdminWalletTierValidationException>(() =>
            service.ReplaceAsync("admin-1", "Admin", new AdminWalletTierReplaceRequest
            {
                Tiers = new()
                {
                    new AdminWalletTierInput
                    {
                        Id = seeded.Id,
                        Slug = "renamed",
                        Amount = 10, Credits = 10, Bonus = 0,
                        IsActive = true, DisplayOrder = 0, Currency = "AUD",
                    }
                }
            }, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.Contains("immutable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AdminWalletTierService_RejectsActiveTiersWithNonAscendingDisplayOrder()
    {
        var options = NewOptions();
        await using var db = new LearnerDbContext(options);
        var service = NewAdminTierService(db);

        var ex = await Assert.ThrowsAsync<AdminWalletTierValidationException>(() =>
            service.ReplaceAsync("admin-1", "Admin", new AdminWalletTierReplaceRequest
            {
                Tiers = new()
                {
                    // Lower amount with HIGHER displayOrder than the next tier
                    // would render the cheap tier below the expensive one — block it.
                    new AdminWalletTierInput { Slug = "a", Amount = 10, Credits = 10, Bonus = 0, IsActive = true, DisplayOrder = 5, Currency = "AUD" },
                    new AdminWalletTierInput { Slug = "b", Amount = 25, Credits = 25, Bonus = 1, IsActive = true, DisplayOrder = 1, Currency = "AUD" },
                }
            }, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.Contains("ascending", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AdminWalletTierService_RecordsAuditEventOnReplace()
    {
        var options = NewOptions();
        await using var db = new LearnerDbContext(options);
        var service = NewAdminTierService(db);

        await service.ReplaceAsync("admin-1", "Admin", new AdminWalletTierReplaceRequest
        {
            Tiers = new()
            {
                new AdminWalletTierInput { Slug = "starter", Amount = 10, Credits = 10, Bonus = 0, IsActive = true, DisplayOrder = 0, Currency = "AUD" },
            }
        }, CancellationToken.None);

        var audit = await db.AuditEvents.SingleAsync(e => e.Action == "wallet_tiers.replace");
        Assert.Equal("WalletTopUpTierConfig", audit.ResourceType);
    }

    private static AdminWalletTierService NewAdminTierService(LearnerDbContext db)
        => new(
            db,
            Options.Create(new BillingOptions
            {
                Wallet = new WalletBillingOptions { Currency = "AUD" }
            }),
            TimeProvider.System);
}
