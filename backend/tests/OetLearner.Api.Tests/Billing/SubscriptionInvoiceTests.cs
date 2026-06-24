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
/// Regression guard for the "subscribed but no invoice" gap: a paid subscription
/// created outside the checkout-webhook path (admin grant / complimentary) must
/// still get a downloadable invoice, the "downloads available" flag must reflect
/// reality, and the subscription status must never render as the literal
/// "unknown". Drives the real <see cref="LearnerService"/> from DI against the
/// in-memory database, the same way the fulfillment tests do.
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

    [Fact]
    public async Task PaidSubscriptionWithoutInvoice_GetInvoices_CreatesExactlyOnePaidInvoice_AndIsIdempotent()
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
    public async Task BillingSummary_PaidSub_FlagTrue_AndFrozenStatusIsNotUnknown()
    {
        string userId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();
            userId = SeedSubscriber(db, price: 100m, status: SubscriptionStatus.Frozen);
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
