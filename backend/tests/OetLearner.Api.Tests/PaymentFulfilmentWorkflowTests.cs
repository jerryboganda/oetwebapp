using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public sealed class PaymentFulfilmentWorkflowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public PaymentFulfilmentWorkflowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task VerifiedPayment_QueuesLatestOrder_AndFulfilmentIsAtomicAndIdempotent()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"fulfil-user-{suffix}";
        var planCode = $"fulfil-plan-{suffix}";
        var subscriptionId = $"fulfil-sub-{suffix}";
        var quoteId = $"fulfil-quote-{suffix}";
        var paymentId = Guid.NewGuid();
        var receiptId = $"fulfil-receipt-{suffix}";
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", "Paid Candidate");

        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.BillingPlans.Add(new BillingPlan
            {
                Id = planCode,
                Code = planCode,
                Name = "Verified OET Package",
                Price = 120m,
                Currency = "GBP",
                Interval = "one_time",
                DurationMonths = 6,
                AccessDurationDays = 180,
                IncludedCredits = 4,
                DeliveryMethod = DeliveryMethods.AutomaticWeb,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Subscriptions.AddRange(
                new Subscription
                {
                    Id = subscriptionId,
                    UserId = userId,
                    PlanId = planCode,
                    Status = SubscriptionStatus.Active,
                    FulfilmentStatus = FulfilmentStatuses.PendingVerification,
                    StartedAt = now.AddDays(-1),
                    ChangedAt = now,
                    NextRenewalAt = now.AddMonths(6),
                    PriceAmount = 120m,
                    Currency = "GBP",
                    Interval = "one_time",
                },
                new Subscription
                {
                    Id = $"abandoned-sub-{suffix}",
                    UserId = userId,
                    PlanId = planCode,
                    Status = SubscriptionStatus.Draft,
                    FulfilmentStatus = FulfilmentStatuses.PendingVerification,
                    StartedAt = now,
                    ChangedAt = now,
                    NextRenewalAt = now.AddMonths(6),
                    PriceAmount = 120m,
                    Currency = "GBP",
                    Interval = "one_time",
                },
                new Subscription
                {
                    Id = $"failed-sub-{suffix}",
                    UserId = userId,
                    PlanId = planCode,
                    Status = SubscriptionStatus.Active,
                    FulfilmentStatus = FulfilmentStatuses.PendingVerification,
                    StartedAt = now,
                    ChangedAt = now,
                    NextRenewalAt = now.AddMonths(6),
                    PriceAmount = 120m,
                    Currency = "GBP",
                    Interval = "one_time",
                });
            db.BillingQuotes.AddRange(
                Quote($"old-quote-{suffix}", subscriptionId, userId, planCode, 10m, now.AddMinutes(-10)),
                Quote(quoteId, subscriptionId, userId, planCode, 120m, now),
                Quote($"failed-quote-{suffix}", $"failed-sub-{suffix}", userId, planCode, 120m, now));
            db.PaymentTransactions.AddRange(
                new PaymentTransaction
                {
                    Id = paymentId,
                    LearnerUserId = userId,
                    Gateway = "stripe",
                    GatewayTransactionId = $"pi_{suffix}",
                    TransactionType = "subscription_payment",
                    Status = "completed",
                    Amount = 120m,
                    Currency = "GBP",
                    ProductType = "plan",
                    ProductId = planCode,
                    QuoteId = quoteId,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new PaymentTransaction
                {
                    Id = Guid.NewGuid(),
                    LearnerUserId = userId,
                    Gateway = "stripe",
                    GatewayTransactionId = $"pi_failed_{suffix}",
                    TransactionType = "subscription_payment",
                    Status = "failed",
                    Amount = 120m,
                    Currency = "GBP",
                    ProductType = "plan",
                    ProductId = planCode,
                    QuoteId = $"failed-quote-{suffix}",
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            db.ManualPaymentRequests.Add(new ManualPaymentRequest
            {
                Id = receiptId,
                UserId = userId,
                QuoteId = quoteId,
                AmountAmount = 120m,
                Currency = "GBP",
                Method = "stripe",
                Kind = PaymentProofKinds.GatewayReceipt,
                Gateway = "stripe",
                PaymentTransactionId = paymentId,
                Reference = $"pi_{suffix}",
                CandidateFullName = "Paid Candidate",
                CandidateEmail = $"{userId}@example.test",
                CourseId = planCode,
                PaymentCategory = "international",
                Status = "paid",
                AccessGrantedSubscriptionId = subscriptionId,
                SubmittedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        using var admin = _factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Debug-Role", ApplicationUserRoles.Admin);
        admin.DefaultRequestHeaders.Add("X-Debug-UserId", $"admin-{suffix}");
        admin.DefaultRequestHeaders.Add("X-Debug-Email", $"admin-{suffix}@example.test");
        admin.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", AdminPermissions.SystemAdmin);

        var queue = await admin.GetFromJsonAsync<List<PendingFulfilmentDto>>("/v1/admin/billing/fulfilment/");
        var order = Assert.Single(queue!, item => item.SubscriptionId == subscriptionId);
        Assert.Equal(quoteId, order.OrderId);
        Assert.Equal(120m, order.Amount);
        Assert.Equal("GBP", order.Currency);
        Assert.Equal("stripe", order.Gateway);
        Assert.Equal($"pi_{suffix}", order.TransactionId);
        Assert.Equal("completed", order.PaymentStatus);
        Assert.DoesNotContain(queue!, item => item.SubscriptionId == $"abandoned-sub-{suffix}");
        Assert.DoesNotContain(queue!, item => item.SubscriptionId == $"failed-sub-{suffix}");

        using var firstResponse = await admin.PostAsJsonAsync(
            $"/v1/admin/billing/fulfilment/subscriptions/{subscriptionId}/mark-fulfilled",
            new { notes = "Gateway payment verified." });
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        using var retryResponse = await admin.PostAsJsonAsync(
            $"/v1/admin/billing/fulfilment/subscriptions/{subscriptionId}/mark-fulfilled",
            new { notes = "Duplicate admin retry." });
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);

        var remainingQueue = await admin.GetFromJsonAsync<List<PendingFulfilmentDto>>("/v1/admin/billing/fulfilment/");
        Assert.DoesNotContain(remainingQueue!, item => item.SubscriptionId == subscriptionId);

        await using var assertScope = _factory.Services.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var subscription = await assertDb.Subscriptions.SingleAsync(item => item.Id == subscriptionId);
        var wallet = await assertDb.Wallets.SingleAsync(item => item.UserId == userId);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(FulfilmentStatuses.Fulfilled, subscription.FulfilmentStatus);
        Assert.Equal(4, wallet.CreditBalance);
        // Included-credit grants are idempotent per subscription (not per receipt):
        // ApproveAsync records ReferenceId = subscription id (ff2b61407 scheme).
        Assert.Single(await assertDb.WalletTransactions
            .Where(item => item.WalletId == wallet.Id && item.ReferenceId == subscriptionId)
            .ToListAsync());
        Assert.Single(await assertDb.AuditEvents
            .Where(item => item.ResourceId == subscriptionId && item.Action == "subscription.mark_fulfilled")
            .ToListAsync());
        Assert.True(await assertDb.PaymentTransactions.AnyAsync(item => item.Id == paymentId));
        Assert.True(await assertDb.ManualPaymentRequests.AnyAsync(item => item.Id == receiptId));
    }

    [Fact]
    public async Task MarkFulfilled_WithGatewayEvidence_PromotesPendingInvoice()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"fulfil-gw-{suffix}";
        var planCode = $"fulfil-gw-plan-{suffix}";
        var subscriptionId = $"fulfil-gw-sub-{suffix}";
        var quoteId = $"fulfil-gw-quote-{suffix}";
        var paymentId = Guid.NewGuid();
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", "Gateway Candidate");

        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.BillingPlans.Add(new BillingPlan
            {
                Id = planCode,
                Code = planCode,
                Name = "Verified OET Package",
                Price = 120m,
                Currency = "GBP",
                Interval = "one_time",
                DurationMonths = 6,
                AccessDurationDays = 180,
                IncludedCredits = 4,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Subscriptions.Add(new Subscription
            {
                Id = subscriptionId,
                UserId = userId,
                PlanId = planCode,
                Status = SubscriptionStatus.Active,
                FulfilmentStatus = FulfilmentStatuses.PendingVerification,
                StartedAt = now.AddDays(-1),
                ChangedAt = now,
                NextRenewalAt = now.AddMonths(6),
                PriceAmount = 120m,
                Currency = "GBP",
                Interval = "one_time",
            });
            db.BillingQuotes.Add(new BillingQuote
            {
                Id = quoteId,
                UserId = userId,
                SubscriptionId = subscriptionId,
                PlanCode = planCode,
                Currency = "GBP",
                SubtotalAmount = 120m,
                TotalAmount = 120m,
                Status = BillingQuoteStatus.Applied,
                CreatedAt = now,
                ExpiresAt = now.AddHours(1),
                SnapshotJson = "{}",
            });
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = paymentId,
                LearnerUserId = userId,
                Gateway = "stripe",
                GatewayTransactionId = $"pi_{suffix}",
                TransactionType = "subscription_payment",
                Status = "completed",
                Amount = 120m,
                Currency = "GBP",
                ProductType = "plan",
                ProductId = planCode,
                QuoteId = quoteId,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Invoices.Add(new Invoice
            {
                Id = $"inv-gw-{suffix}",
                UserId = userId,
                Amount = 120m,
                Currency = "GBP",
                Status = "Pending",
                Description = "Gateway order, awaiting fulfilment",
                SubscriptionId = subscriptionId,
                QuoteId = quoteId,
                IssuedAt = now,
            });
            await db.SaveChangesAsync();
        }

        using var admin = _factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Debug-Role", ApplicationUserRoles.Admin);
        admin.DefaultRequestHeaders.Add("X-Debug-UserId", $"admin-{suffix}");
        admin.DefaultRequestHeaders.Add("X-Debug-Email", $"admin-{suffix}@example.test");
        admin.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", AdminPermissions.SystemAdmin);

        using var response = await admin.PostAsJsonAsync(
            $"/v1/admin/billing/fulfilment/subscriptions/{subscriptionId}/mark-fulfilled",
            new { notes = "Gateway payment verified." });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var assertScope = _factory.Services.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal("Paid", (await assertDb.Invoices.SingleAsync(i => i.Id == $"inv-gw-{suffix}")).Status);
    }

    [Fact]
    public async Task MarkFulfilled_PureGrantWithoutEvidence_LeavesInvoicePending()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"fulfil-grant-{suffix}";
        var planCode = $"fulfil-grant-plan-{suffix}";
        var subscriptionId = $"fulfil-grant-sub-{suffix}";
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", "Grant Candidate");

        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.BillingPlans.Add(new BillingPlan
            {
                Id = planCode,
                Code = planCode,
                Name = "Grant Package",
                Price = 120m,
                Currency = "GBP",
                Interval = "one_time",
                DurationMonths = 6,
                AccessDurationDays = 180,
                DeliveryMethod = DeliveryMethods.AutomaticWeb,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Subscriptions.Add(new Subscription
            {
                Id = subscriptionId,
                UserId = userId,
                PlanId = planCode,
                Status = SubscriptionStatus.Active,
                FulfilmentStatus = FulfilmentStatuses.PendingVerification,
                StartedAt = now.AddDays(-1),
                ChangedAt = now,
                NextRenewalAt = now.AddMonths(6),
                PriceAmount = 120m,
                Currency = "GBP",
                Interval = "one_time",
            });
            db.Invoices.Add(new Invoice
            {
                Id = $"inv-grant-{suffix}",
                UserId = userId,
                Amount = 120m,
                Currency = "GBP",
                Status = "Pending",
                Description = "Admin grant, no payment evidence",
                SubscriptionId = subscriptionId,
                IssuedAt = now,
            });
            await db.SaveChangesAsync();
        }

        using var admin = _factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Debug-Role", ApplicationUserRoles.Admin);
        admin.DefaultRequestHeaders.Add("X-Debug-UserId", $"admin-{suffix}");
        admin.DefaultRequestHeaders.Add("X-Debug-Email", $"admin-{suffix}@example.test");
        admin.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", AdminPermissions.SystemAdmin);

        using var response = await admin.PostAsJsonAsync(
            $"/v1/admin/billing/fulfilment/subscriptions/{subscriptionId}/mark-fulfilled",
            new { notes = "Admin grant fulfilment." });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var assertScope = _factory.Services.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(SubscriptionStatus.Active, (await assertDb.Subscriptions.SingleAsync(s => s.Id == subscriptionId)).Status);
        Assert.Equal("Pending", (await assertDb.Invoices.SingleAsync(i => i.Id == $"inv-grant-{suffix}")).Status);
    }

    private static BillingQuote Quote(
        string id,
        string subscriptionId,
        string userId,
        string planCode,
        decimal total,
        DateTimeOffset createdAt)
        => new()
        {
            Id = id,
            UserId = userId,
            SubscriptionId = subscriptionId,
            PlanCode = planCode,
            Currency = "GBP",
            SubtotalAmount = total,
            TotalAmount = total,
            Status = BillingQuoteStatus.Applied,
            CreatedAt = createdAt,
            ExpiresAt = createdAt.AddHours(1),
            SnapshotJson = "{}",
        };
}
