using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;
using Xunit;

namespace OetLearner.Api.Tests.Billing;

/// <summary>
/// Paid-only invoice policy guards: a subscription with confirmed payment
/// evidence (gateway payment or approved proof) gets a downloadable invoice;
/// an unevidenced grant does not. Drives the real <see cref="LearnerService"/>
/// from DI against the in-memory database, the same way the fulfillment
/// tests do. (Since the #171 evidence requirement, seeds carry explicit
/// payment evidence — a priced subscription alone is an admin grant, not
/// proof of payment.)
/// </summary>
public sealed class SubscriptionInvoiceTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public SubscriptionInvoiceTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string SeedSubscriber(LearnerDbContext db, decimal price, SubscriptionStatus status)
    {
        var userId = $"usr-inv-{Guid.NewGuid():N}"[..32];
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = "Invoice Learner",
            Email = $"{userId}@example.test",
            CreatedAt = now,
            LastActiveAt = now,
            AccountStatus = "active",
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}"[..32],
            UserId = userId,
            PlanId = "full-condensed-medicine",
            Status = status,
            StartedAt = now.AddDays(-3),
            ChangedAt = now,
            NextRenewalAt = now.AddMonths(6),
            PriceAmount = price,
            Currency = "GBP",
            Interval = "one_time",
        });
        return userId;
    }

    private static void SeedGatewayEvidence(LearnerDbContext db, string userId, decimal price)
    {
        // NOTE: Local (tracker), not a store query — InMemory does not return
        // unsaved rows from queries, and seeds save once at the end.
        var subId = db.Subscriptions.Local.Single(s => s.UserId == userId).Id;
        var now = DateTimeOffset.UtcNow;
        var quoteId = $"q-{Guid.NewGuid():N}"[..32];
        db.BillingQuotes.Add(new BillingQuote
        {
            Id = quoteId,
            UserId = userId,
            SubscriptionId = subId,
            PlanCode = "full-condensed-medicine",
            Currency = "GBP",
            SubtotalAmount = price,
            TotalAmount = price,
            Status = BillingQuoteStatus.Applied,
            CreatedAt = now,
            ExpiresAt = now.AddHours(1),
            SnapshotJson = "{}",
        });
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            LearnerUserId = userId,
            Gateway = "stripe",
            GatewayTransactionId = $"pi-{Guid.NewGuid():N}"[..32],
            TransactionType = "subscription_payment",
            Status = "completed",
            Amount = price,
            Currency = "GBP",
            ProductType = "plan",
            ProductId = "full-condensed-medicine",
            QuoteId = quoteId,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    [Fact]
    public async Task PaidSubscriptionWithoutInvoice_GetInvoices_CreatesExactlyOnePaidInvoice_AndIsIdempotent()
    {
        string userId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();
            userId = SeedSubscriber(db, price: 100m, status: SubscriptionStatus.Active);
            SeedGatewayEvidence(db, userId, price: 100m);
            await db.SaveChangesAsync();
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<LearnerService>();
            await service.GetInvoicesAsync(userId, CancellationToken.None);
            // A second read must not create a duplicate.
            await service.GetInvoicesAsync(userId, CancellationToken.None);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var invoices = await db.Invoices.Where(x => x.UserId == userId).ToListAsync();
            Assert.Single(invoices);
            Assert.Equal("Paid", invoices[0].Status);
            Assert.Equal(100m, invoices[0].Amount);
            Assert.Equal("GBP", invoices[0].Currency);
        }
    }

    [Fact]
    public async Task FreeSubscription_GetInvoices_CreatesNoInvoice()
    {
        string userId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();
            userId = SeedSubscriber(db, price: 0m, status: SubscriptionStatus.Active);
            await db.SaveChangesAsync();
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<LearnerService>();
            await service.GetInvoicesAsync(userId, CancellationToken.None);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            Assert.Equal(0, await db.Invoices.CountAsync(x => x.UserId == userId));
        }
    }

    [Fact]
    public async Task GrantWithoutEvidence_GetInvoices_CreatesNoInvoice()
    {
        string userId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();
            userId = SeedSubscriber(db, price: 100m, status: SubscriptionStatus.Active);
            await db.SaveChangesAsync();
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<LearnerService>();
            await service.GetInvoicesAsync(userId, CancellationToken.None);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            Assert.Equal(0, await db.Invoices.CountAsync(x => x.UserId == userId));
        }
    }

    [Fact]
    public async Task BillingSummary_PaidSub_FlagTrue_AndFrozenStatusIsNotUnknown()
    {
        string userId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();
            userId = SeedSubscriber(db, price: 100m, status: SubscriptionStatus.Frozen);
            SeedGatewayEvidence(db, userId, price: 100m);
            await db.SaveChangesAsync();
        }

        string json;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<LearnerService>();
            var summary = await service.GetBillingSummaryAsync(userId, CancellationToken.None);
            json = JsonSerializer.Serialize(summary);
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("frozen", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("entitlements").GetProperty("invoiceDownloadsAvailable").GetBoolean());
    }

    [Fact]
    public async Task BillingSummary_FreeSub_FlagFalse()
    {
        string userId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();
            userId = SeedSubscriber(db, price: 0m, status: SubscriptionStatus.Active);
            await db.SaveChangesAsync();
        }

        string json;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<LearnerService>();
            var summary = await service.GetBillingSummaryAsync(userId, CancellationToken.None);
            json = JsonSerializer.Serialize(summary);
        }

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("entitlements").GetProperty("invoiceDownloadsAvailable").GetBoolean());
    }

    [Fact]
    public async Task Backfill_CreatesInvoiceForPaidSubscriptionLackingOne()
    {
        string userId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();
            userId = SeedSubscriber(db, price: 75m, status: SubscriptionStatus.Active);
            SeedGatewayEvidence(db, userId, price: 75m);
            await db.SaveChangesAsync();
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<LearnerService>();
            await service.BackfillSubscriptionInvoicesAsync(CancellationToken.None);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            Assert.Equal(1, await db.Invoices.CountAsync(x => x.UserId == userId));
        }
    }
}
