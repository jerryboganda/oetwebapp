using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using Xunit;

namespace OetLearner.Api.Tests;

public class WalletServiceTests
{
    private static (LearnerDbContext db, WalletService svc) Build()
    {
        var opts = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(opts);
        var svc = new WalletService(db, paymentGateways: null!, platformLinks: null!);
        return (db, svc);
    }

    private static Wallet AddWallet(LearnerDbContext db, string userId, int balance = 0)
    {
        var w = new Wallet
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            CreditBalance = balance,
            LastUpdatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        db.Wallets.Add(w);
        db.SaveChanges();
        return w;
    }

    // ── GetBalanceAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetBalanceAsync_returns_zero_when_wallet_not_found()
    {
        var (_, svc) = Build();
        var bal = await svc.GetBalanceAsync("no-such-user", CancellationToken.None);
        Assert.Equal(0, bal.CreditBalance);
    }

    [Fact]
    public async Task GetBalanceAsync_returns_current_balance_when_wallet_exists()
    {
        var (db, svc) = Build();
        AddWallet(db, "u1", balance: 42);
        var bal = await svc.GetBalanceAsync("u1", CancellationToken.None);
        Assert.Equal(42, bal.CreditBalance);
    }

    // ── CreditAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreditAsync_increases_balance_and_records_positive_transaction()
    {
        var (db, svc) = Build();
        var w = AddWallet(db, "u1", balance: 5);
        var txn = await svc.CreditAsync(w.Id, 10, "top_up", null, null, null, null, CancellationToken.None);

        Assert.Equal(10, txn.Amount);
        Assert.Equal(15, txn.BalanceAfter);
        Assert.Equal("top_up", txn.TransactionType);
        Assert.Equal("system", txn.CreatedBy);
        Assert.NotNull(txn.Description);
        Assert.Contains("+10", txn.Description!);

        var refreshed = await db.Wallets.AsNoTracking().FirstAsync(x => x.Id == w.Id);
        Assert.Equal(15, refreshed.CreditBalance);
    }

    [Fact]
    public async Task CreditAsync_persists_provided_metadata_fields()
    {
        var (db, svc) = Build();
        var w = AddWallet(db, "u1");
        var txn = await svc.CreditAsync(
            w.Id, 5, "manual_adjustment",
            referenceType: "manual", referenceId: "ref-1",
            description: "custom note", createdBy: "admin-7",
            ct: CancellationToken.None);

        Assert.Equal("manual", txn.ReferenceType);
        Assert.Equal("ref-1", txn.ReferenceId);
        Assert.Equal("custom note", txn.Description);
        Assert.Equal("admin-7", txn.CreatedBy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task CreditAsync_throws_for_non_positive_amount(int amount)
    {
        var (db, svc) = Build();
        var w = AddWallet(db, "u1");
        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CreditAsync(w.Id, amount, "x", null, null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task CreditAsync_throws_when_wallet_does_not_exist()
    {
        var (_, svc) = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreditAsync("missing-wallet", 5, "x", null, null, null, null, CancellationToken.None));
    }

    // ── DebitAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task DebitAsync_decreases_balance_and_records_negative_transaction()
    {
        var (db, svc) = Build();
        var w = AddWallet(db, "u1", balance: 20);
        var txn = await svc.DebitAsync(w.Id, 7, "review_deduction", null, null, null, null, CancellationToken.None);

        Assert.Equal(-7, txn.Amount);
        Assert.Equal(13, txn.BalanceAfter);
        Assert.Contains("-7", txn.Description!);

        var refreshed = await db.Wallets.AsNoTracking().FirstAsync(x => x.Id == w.Id);
        Assert.Equal(13, refreshed.CreditBalance);
    }

    [Fact]
    public async Task DebitAsync_throws_when_balance_insufficient()
    {
        var (db, svc) = Build();
        var w = AddWallet(db, "u1", balance: 5);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.DebitAsync(w.Id, 10, "x", null, null, null, null, CancellationToken.None));
        Assert.Contains("Insufficient", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DebitAsync_allows_debit_equal_to_balance()
    {
        var (db, svc) = Build();
        var w = AddWallet(db, "u1", balance: 10);
        var txn = await svc.DebitAsync(w.Id, 10, "x", null, null, null, null, CancellationToken.None);
        Assert.Equal(0, txn.BalanceAfter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task DebitAsync_throws_for_non_positive_amount(int amount)
    {
        var (db, svc) = Build();
        var w = AddWallet(db, "u1", balance: 100);
        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.DebitAsync(w.Id, amount, "x", null, null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task DebitAsync_throws_when_wallet_does_not_exist()
    {
        var (_, svc) = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.DebitAsync("missing", 1, "x", null, null, null, null, CancellationToken.None));
    }

    // ── GetTransactionHistoryAsync ───────────────────────────────────────

    [Fact]
    public async Task GetTransactionHistoryAsync_returns_only_transactions_for_wallet()
    {
        var (db, svc) = Build();
        var w1 = AddWallet(db, "u1");
        var w2 = AddWallet(db, "u2");

        await svc.CreditAsync(w1.Id, 5, "x", null, null, null, null, CancellationToken.None);
        await svc.CreditAsync(w2.Id, 3, "x", null, null, null, null, CancellationToken.None);

        var history = await svc.GetTransactionHistoryAsync(w1.Id, 10, CancellationToken.None);
        Assert.Single(history);
        Assert.Equal(w1.Id, history[0].WalletId);
    }

    [Fact]
    public async Task GetTransactionHistoryAsync_orders_newest_first()
    {
        var (db, svc) = Build();
        var w = AddWallet(db, "u1", balance: 100);
        await svc.CreditAsync(w.Id, 1, "first", null, null, null, null, CancellationToken.None);
        await Task.Delay(10);
        await svc.CreditAsync(w.Id, 2, "second", null, null, null, null, CancellationToken.None);
        await Task.Delay(10);
        await svc.CreditAsync(w.Id, 3, "third", null, null, null, null, CancellationToken.None);

        var history = await svc.GetTransactionHistoryAsync(w.Id, 10, CancellationToken.None);
        Assert.Equal(3, history.Count);
        Assert.Equal("third", history[0].TransactionType);
        Assert.Equal("first", history[2].TransactionType);
    }

    [Fact]
    public async Task GetTransactionHistoryAsync_caps_results_at_100_even_when_higher_limit_requested()
    {
        var (db, svc) = Build();
        var w = AddWallet(db, "u1", balance: 1000);
        for (var i = 0; i < 150; i++)
        {
            await svc.CreditAsync(w.Id, 1, "x", null, null, null, null, CancellationToken.None);
        }

        var history = await svc.GetTransactionHistoryAsync(w.Id, limit: 1000, CancellationToken.None);
        Assert.Equal(100, history.Count);
    }

    [Fact]
    public async Task GetTransactionHistoryAsync_respects_smaller_limit()
    {
        var (db, svc) = Build();
        var w = AddWallet(db, "u1", balance: 100);
        for (var i = 0; i < 5; i++)
        {
            await svc.CreditAsync(w.Id, 1, "x", null, null, null, null, CancellationToken.None);
        }

        var history = await svc.GetTransactionHistoryAsync(w.Id, limit: 3, CancellationToken.None);
        Assert.Equal(3, history.Count);
    }

    // ── CreateTopUpSessionAsync (validation paths only) ──────────────────

    [Fact]
    public async Task CreateTopUpSessionAsync_throws_when_wallet_not_found()
    {
        var (_, svc) = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateTopUpSessionAsync("missing-user", 25, "stripe", CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(200)]
    [InlineData(-10)]
    public async Task CreateTopUpSessionAsync_throws_for_invalid_amount(int amount)
    {
        var (db, svc) = Build();
        AddWallet(db, "u1");
        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CreateTopUpSessionAsync("u1", amount, "stripe", CancellationToken.None));
    }
}
