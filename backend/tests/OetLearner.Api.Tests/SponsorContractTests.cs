using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// RW-010 — frontend/backend contract for the sponsor surface.
///
/// The frontend declares <c>SponsorBillingData</c> and <c>SponsorInvoice</c>
/// in <c>lib/api.ts</c>. This test asserts that every documented field on
/// those interfaces actually appears (with the expected runtime type) on the
/// service responses behind <c>GET /v1/sponsor/dashboard</c> and
/// <c>GET /v1/sponsor/billing</c>. Reflection-based shape check; value
/// semantics are locked down by <see cref="SponsorBillingReadModelTests"/>.
/// </summary>
public sealed class SponsorContractTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-05-10T12:00:00Z"));

    public SponsorContractTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Dashboard_ResponseMatchesFrontendSponsorDashboardDataShape()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedSponsorAccountAsync(db, "sponsor-c1");
        await SeedSponsorshipAsync(db, "sponsor-c1", "learner-c1", _clock.GetUtcNow().AddMonths(-1));
        await SeedTxAsync(db, "learner-c1", 49.99m, "completed", _clock.GetUtcNow().AddDays(-3));

        var service = new SponsorService(db, NullLogger<SponsorService>.Instance, _clock);
        var dashboard = await service.GetDashboardAsync("sponsor-c1", default);

        AssertProperty<string>(dashboard, "sponsorName");
        AssertProperty(dashboard, "organizationName", typeof(string), nullable: true);
        AssertProperty<int>(dashboard, "learnersSponsored");
        AssertProperty<int>(dashboard, "activeSponsorships");
        AssertProperty<int>(dashboard, "pendingSponsorships");
        AssertProperty<decimal>(dashboard, "totalSpend");
        AssertProperty(dashboard, "currency", typeof(string), nullable: true);
    }

    [Fact]
    public async Task Billing_ResponseMatchesFrontendSponsorBillingDataShape()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedSponsorAccountAsync(db, "sponsor-c2");
        await SeedSponsorshipAsync(db, "sponsor-c2", "learner-c2", _clock.GetUtcNow().AddMonths(-1));
        await SeedTxAsync(db, "learner-c2", 99.00m, "completed", _clock.GetUtcNow().AddDays(-1));

        var service = new SponsorService(db, NullLogger<SponsorService>.Instance, _clock);
        var billing = await service.GetBillingAsync("sponsor-c2", default);

        AssertProperty<string>(billing, "sponsorName");
        AssertProperty(billing, "organizationName", typeof(string), nullable: true);
        AssertProperty<int>(billing, "totalSponsorships");
        AssertProperty<decimal>(billing, "totalSpend");
        AssertProperty<decimal>(billing, "currentMonthSpend");
        AssertProperty(billing, "currency", typeof(string), nullable: true);
        AssertProperty<string>(billing, "billingCycle");

        var invoicesProp = billing!.GetType().GetProperty("invoices")
            ?? throw new Xunit.Sdk.XunitException("billing response missing 'invoices' property");
        var invoicesValue = invoicesProp.GetValue(billing);
        Assert.NotNull(invoicesValue);

        var invoiceList = ((System.Collections.IEnumerable)invoicesValue!).Cast<object>().ToList();
        Assert.NotEmpty(invoiceList);
        var first = invoiceList[0];

        AssertProperty<Guid>(first, "Id");
        AssertProperty<Guid>(first, "SponsorshipId");
        AssertProperty<string>(first, "LearnerUserId");
        AssertProperty<string>(first, "LearnerEmail");
        AssertProperty<string>(first, "Gateway");
        AssertProperty<string>(first, "GatewayTransactionId");
        AssertProperty<string>(first, "TransactionType");
        AssertProperty(first, "ProductType", typeof(string), nullable: true);
        AssertProperty(first, "ProductId", typeof(string), nullable: true);
        AssertProperty<decimal>(first, "Amount");
        AssertProperty<string>(first, "Currency");
        AssertProperty<string>(first, "Status");
        AssertProperty<DateTimeOffset>(first, "CreatedAt");
    }

    private static void AssertProperty<T>(object? source, string name)
        => AssertProperty(source, name, typeof(T), nullable: false);

    private static void AssertProperty(object? source, string name, Type expectedType, bool nullable)
    {
        Assert.NotNull(source);
        var prop = source!.GetType().GetProperty(name)
            ?? throw new Xunit.Sdk.XunitException($"response missing property '{name}' on {source.GetType().Name}");
        var value = prop.GetValue(source);
        if (!nullable)
        {
            Assert.NotNull(value);
        }
        if (value is not null)
        {
            var actual = value.GetType();
            var ok = expectedType.IsAssignableFrom(actual)
                || (Nullable.GetUnderlyingType(actual) ?? actual) == expectedType;
            if (!ok)
            {
                throw new Xunit.Sdk.XunitException(
                    $"property '{name}' has runtime type {actual.FullName}, expected {expectedType.FullName}");
            }
        }
    }

    private async Task SeedSponsorAccountAsync(LearnerDbContext db, string sponsorUserId)
    {
        db.SponsorAccounts.Add(new SponsorAccount
        {
            Id = Guid.NewGuid().ToString("N"),
            AuthAccountId = sponsorUserId,
            Name = "Contract Sponsor",
            Type = "institution",
            ContactEmail = "billing@example.test",
            OrganizationName = "Contract Org",
            Status = "active",
            CreatedAt = _clock.GetUtcNow().AddYears(-1),
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedSponsorshipAsync(
        LearnerDbContext db,
        string sponsorUserId,
        string learnerUserId,
        DateTimeOffset createdAt)
    {
        db.Sponsorships.Add(new Sponsorship
        {
            Id = Guid.NewGuid(),
            SponsorUserId = sponsorUserId,
            LearnerUserId = learnerUserId,
            LearnerEmail = $"{learnerUserId}@example.test",
            Status = "Active",
            CreatedAt = createdAt,
            RevokedAt = null,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedTxAsync(
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

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
