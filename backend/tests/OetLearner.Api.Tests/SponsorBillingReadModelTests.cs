using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// RW-013 — sponsor billing read model. Locks down the contract that
/// dashboard `totalSpend`, billing `totalSpend`/`currentMonthSpend`, and
/// the invoice list are computed from real <see cref="PaymentTransaction"/>
/// rows belonging to learners linked through a non-revoked
/// <see cref="Sponsorship"/>, restricted to the sponsorship's active
/// window, and only count transactions with status="completed".
/// </summary>
public sealed class SponsorBillingReadModelTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-05-09T12:00:00Z"));

    public SponsorBillingReadModelTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Dashboard_ReturnsZeroSpend_WhenNoLinkedTransactions()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedSponsorAsync(db, sponsorUserId: "sponsor-1");
        var service = new SponsorService(db, NullLogger<SponsorService>.Instance, _clock);

        var dashboard = await service.GetDashboardAsync("sponsor-1", default);

        Assert.Equal(0m, GetProperty<decimal>(dashboard, "totalSpend"));
        Assert.Null(GetProperty<string?>(dashboard, "currency"));
    }

    [Fact]
    public async Task Billing_SumsCompletedTransactionsForLinkedLearner_InsideActiveWindow()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedSponsorAsync(db, sponsorUserId: "sponsor-1");
        var sponsorshipId = await SeedSponsorshipAsync(db,
            sponsorUserId: "sponsor-1",
            learnerUserId: "learner-1",
            createdAt: _clock.GetUtcNow().AddMonths(-2));

        // Three completed transactions inside the window — should all count.
        await SeedTxAsync(db, learnerUserId: "learner-1", amount: 49.99m, status: "completed",
            createdAt: _clock.GetUtcNow().AddDays(-40));
        await SeedTxAsync(db, learnerUserId: "learner-1", amount: 49.99m, status: "completed",
            createdAt: _clock.GetUtcNow().AddDays(-10));
        await SeedTxAsync(db, learnerUserId: "learner-1", amount: 99.99m, status: "completed",
            createdAt: _clock.GetUtcNow().AddDays(-2));

        // Decoy: completed transaction BEFORE the sponsorship existed — must not count.
        await SeedTxAsync(db, learnerUserId: "learner-1", amount: 1000m, status: "completed",
            createdAt: _clock.GetUtcNow().AddMonths(-6));

        // Decoy: pending transaction (status != completed) — must not count.
        await SeedTxAsync(db, learnerUserId: "learner-1", amount: 500m, status: "pending",
            createdAt: _clock.GetUtcNow().AddDays(-1));

        // Decoy: a different learner not linked to this sponsor — must not count.
        await SeedTxAsync(db, learnerUserId: "stranger", amount: 777m, status: "completed",
            createdAt: _clock.GetUtcNow().AddDays(-1));

        var service = new SponsorService(db, NullLogger<SponsorService>.Instance, _clock);
        var billing = await service.GetBillingAsync("sponsor-1", default);

        Assert.Equal(199.97m, GetProperty<decimal>(billing, "totalSpend"));
        // Clock is 2026-05-09; only the tx at -2d (2026-05-07) falls inside
        // the current calendar month. The -10d tx is 2026-04-29.
        Assert.Equal(99.99m, GetProperty<decimal>(billing, "currentMonthSpend"));
        Assert.Equal("GBP", GetProperty<string?>(billing, "currency"));
        Assert.Equal("monthly", GetProperty<string>(billing, "billingCycle"));

        var invoices = GetInvoiceList(billing);
        Assert.Equal(3, invoices.Count);
        // Most recent first.
        Assert.Equal(99.99m, invoices[0].Amount);
        Assert.Equal(sponsorshipId, invoices[0].SponsorshipId);
        Assert.Equal("learner-1", invoices[0].LearnerUserId);
        Assert.Equal("completed", invoices[0].Status);
    }

    [Fact]
    public async Task Billing_RespectsRevocationCutoff()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedSponsorAsync(db, sponsorUserId: "sponsor-1");
        var revokedAt = _clock.GetUtcNow().AddDays(-20);
        await SeedSponsorshipAsync(db,
            sponsorUserId: "sponsor-1",
            learnerUserId: "learner-1",
            createdAt: _clock.GetUtcNow().AddMonths(-3),
            revokedAt: revokedAt);

        // Inside revoked window — counts.
        await SeedTxAsync(db, learnerUserId: "learner-1", amount: 30m, status: "completed",
            createdAt: _clock.GetUtcNow().AddDays(-30));
        // After revocation — must not count.
        await SeedTxAsync(db, learnerUserId: "learner-1", amount: 500m, status: "completed",
            createdAt: _clock.GetUtcNow().AddDays(-5));

        var service = new SponsorService(db, NullLogger<SponsorService>.Instance, _clock);
        var billing = await service.GetBillingAsync("sponsor-1", default);

        Assert.Equal(30m, GetProperty<decimal>(billing, "totalSpend"));
    }

    [Fact]
    public async Task Billing_ReturnsNullCurrency_WhenInvoicesMixCurrencies()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedSponsorAsync(db, sponsorUserId: "sponsor-1");
        await SeedSponsorshipAsync(db,
            sponsorUserId: "sponsor-1",
            learnerUserId: "learner-1",
            createdAt: _clock.GetUtcNow().AddMonths(-2));

        await SeedTxAsync(db, learnerUserId: "learner-1", amount: 10m, status: "completed",
            createdAt: _clock.GetUtcNow().AddDays(-15), currency: "GBP");
        await SeedTxAsync(db, learnerUserId: "learner-1", amount: 20m, status: "completed",
            createdAt: _clock.GetUtcNow().AddDays(-10), currency: "AUD");

        var service = new SponsorService(db, NullLogger<SponsorService>.Instance, _clock);
        var billing = await service.GetBillingAsync("sponsor-1", default);

        Assert.Equal(30m, GetProperty<decimal>(billing, "totalSpend"));
        Assert.Null(GetProperty<string?>(billing, "currency"));
    }

    [Fact]
    public async Task Billing_IgnoresSponsorshipsWithoutLearnerUserId()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedSponsorAsync(db, sponsorUserId: "sponsor-1");
        // Pending invitation — no LearnerUserId yet.
        db.Sponsorships.Add(new Sponsorship
        {
            Id = Guid.NewGuid(),
            SponsorUserId = "sponsor-1",
            LearnerUserId = null,
            LearnerEmail = "pending@example.test",
            Status = "Pending",
            CreatedAt = _clock.GetUtcNow().AddDays(-2),
        });
        await db.SaveChangesAsync();

        var service = new SponsorService(db, NullLogger<SponsorService>.Instance, _clock);
        var billing = await service.GetBillingAsync("sponsor-1", default);

        Assert.Equal(0m, GetProperty<decimal>(billing, "totalSpend"));
        Assert.Empty(GetInvoiceList(billing));
    }

    // ── Helpers ────────────────────────────────────────────────────

    private async Task SeedSponsorAsync(LearnerDbContext db, string sponsorUserId)
    {
        db.SponsorAccounts.Add(new SponsorAccount
        {
            Id = Guid.NewGuid().ToString("N"),
            AuthAccountId = sponsorUserId,
            Name = "Test Sponsor",
            Type = "institution",
            ContactEmail = "billing@example.test",
            OrganizationName = "Test Org",
            Status = "active",
            CreatedAt = _clock.GetUtcNow().AddYears(-1),
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedSponsorshipAsync(
        LearnerDbContext db,
        string sponsorUserId,
        string learnerUserId,
        DateTimeOffset createdAt,
        DateTimeOffset? revokedAt = null)
    {
        var sponsorship = new Sponsorship
        {
            Id = Guid.NewGuid(),
            SponsorUserId = sponsorUserId,
            LearnerUserId = learnerUserId,
            LearnerEmail = $"{learnerUserId}@example.test",
            Status = revokedAt is null ? "Active" : "Revoked",
            CreatedAt = createdAt,
            RevokedAt = revokedAt,
        };
        db.Sponsorships.Add(sponsorship);
        await db.SaveChangesAsync();
        return sponsorship.Id;
    }

    private async Task SeedTxAsync(
        LearnerDbContext db,
        string learnerUserId,
        decimal amount,
        string status,
        DateTimeOffset createdAt,
        string currency = "GBP")
    {
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            LearnerUserId = learnerUserId,
            Gateway = "stripe",
            GatewayTransactionId = $"pi_{Guid.NewGuid():N}",
            TransactionType = "subscription_payment",
            Status = status,
            Amount = amount,
            Currency = currency,
            ProductType = "plan",
            ProductId = "plan-pro",
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        });
        await db.SaveChangesAsync();
    }

    private static T GetProperty<T>(object source, string name)
    {
        var prop = source.GetType().GetProperty(name)
            ?? throw new InvalidOperationException($"Property '{name}' not found on {source.GetType().Name}.");
        return (T)prop.GetValue(source)!;
    }

    private static IReadOnlyList<SponsorInvoice> GetInvoiceList(object billing)
    {
        var raw = billing.GetType().GetProperty("invoices")!.GetValue(billing);
        return raw switch
        {
            IReadOnlyList<SponsorInvoice> list => list,
            IEnumerable<SponsorInvoice> seq => seq.ToList(),
            null => Array.Empty<SponsorInvoice>(),
            _ => throw new InvalidOperationException($"Unexpected invoices payload type: {raw.GetType().FullName}")
        };
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
