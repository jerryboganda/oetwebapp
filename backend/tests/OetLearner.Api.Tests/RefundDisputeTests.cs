using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests;

/// <summary>
/// Slice B (billing-hardening) — refund and dispute lifecycle services.
/// Uses an in-memory <see cref="LearnerDbContext"/> seeded with a completed
/// payment + wallet ledger, then exercises partial-refund math, full-refund
/// reversal, idempotency, and dispute-driven entitlement freezes.
/// </summary>
public class RefundDisputeTests
{
    private static (LearnerDbContext db, RefundService refundService, DisputeService disputeService) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var billingOpts = Options.Create(new BillingOptions { AllowSandboxFallbacks = true });
        var gateways = new PaymentGatewayService(
            new StripeGateway(new HttpClient(), billingOpts),
            new PayPalGateway(new HttpClient(), billingOpts));
        return (db, new RefundService(db, gateways), new DisputeService(db));
    }

    private static async Task SeedCompletedSubscriptionPaymentAsync(LearnerDbContext db, string userId, string txId, decimal amount, int includedCredits = 0)
    {
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new LearnerUser { Id = userId, DisplayName = "T", Email = $"{userId}@x.test", CreatedAt = now, LastActiveAt = now });
        db.Wallets.Add(new Wallet { Id = $"w-{userId}", UserId = userId, CreditBalance = includedCredits, LastUpdatedAt = now });
        db.Subscriptions.Add(new Subscription
        {
            Id = $"sub-{userId}",
            UserId = userId,
            PlanId = "pro",
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddMonths(1),
            PriceAmount = amount,
            Currency = "AUD",
            Interval = "monthly"
        });
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            LearnerUserId = userId,
            Gateway = "stripe",
            GatewayTransactionId = txId,
            TransactionType = "subscription_payment",
            Status = "completed",
            Amount = amount,
            Currency = "AUD",
            ProductType = "plan",
            ProductId = "pro",
            CreatedAt = now,
            UpdatedAt = now
        });
        if (includedCredits > 0)
        {
            db.WalletTransactions.Add(new WalletTransaction
            {
                Id = Guid.NewGuid(),
                WalletId = $"w-{userId}",
                TransactionType = "plan_grant",
                Amount = includedCredits,
                BalanceAfter = includedCredits,
                ReferenceType = "payment",
                ReferenceId = txId,
                Description = "Plan grant",
                CreatedBy = "system",
                CreatedAt = now
            });
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PartialRefund_ReducesRemainingAuthorised_DoesNotReverseEntitlements()
    {
        var (db, refundService, _) = Build();
        await SeedCompletedSubscriptionPaymentAsync(db, "u1", "tx_partial", 100m, includedCredits: 10);

        var result = await refundService.IssueRefundAsync(
            new RefundRequest("tx_partial", 25m, "requested_by_customer", "idem-1", AdminId: "admin-1"),
            default);

        Assert.Equal("partial", result.RefundType);
        Assert.Equal(75m, result.RemainingAuthorisedAmount);
        Assert.False(result.ReversedEntitlements);
        Assert.False(result.ReversedWalletCredits);

        var sub = await db.Subscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatus.Active, sub.Status);

        var wallet = await db.Wallets.SingleAsync();
        Assert.Equal(10, wallet.CreditBalance);
    }

    [Fact]
    public async Task FullRefund_ReversesWalletCredits_CancelsSubscription_AndMarksTransactionRefunded()
    {
        var (db, refundService, _) = Build();
        await SeedCompletedSubscriptionPaymentAsync(db, "u2", "tx_full", 100m, includedCredits: 20);

        var result = await refundService.IssueRefundAsync(
            new RefundRequest("tx_full", 100m, "requested_by_customer", "idem-2"),
            default);

        Assert.Equal("full", result.RefundType);
        Assert.True(result.ReversedWalletCredits);
        Assert.True(result.ReversedEntitlements);

        var sub = await db.Subscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatus.Cancelled, sub.Status);

        var wallet = await db.Wallets.SingleAsync();
        Assert.Equal(0, wallet.CreditBalance);

        var tx = await db.PaymentTransactions.SingleAsync();
        Assert.Equal("refunded", tx.Status);

        // Audit ledger entry was written.
        Assert.True(await db.BillingEvents.AnyAsync(e => e.EventType == "refund_full_issued"));
    }

    [Fact]
    public async Task Refund_IsIdempotentOnKey()
    {
        var (db, refundService, _) = Build();
        await SeedCompletedSubscriptionPaymentAsync(db, "u3", "tx_idem", 100m, includedCredits: 10);

        var first = await refundService.IssueRefundAsync(
            new RefundRequest("tx_idem", 30m, "duplicate_test", "idem-key-shared"),
            default);
        var second = await refundService.IssueRefundAsync(
            new RefundRequest("tx_idem", 30m, "duplicate_test", "idem-key-shared"),
            default);

        Assert.False(first.Idempotent);
        Assert.True(second.Idempotent);
        Assert.Equal(first.RefundId, second.RefundId);
        Assert.Equal(1, await db.OrderRefunds.CountAsync());
    }

    [Fact]
    public async Task Refund_RejectsOverRefund()
    {
        var (db, refundService, _) = Build();
        await SeedCompletedSubscriptionPaymentAsync(db, "u4", "tx_over", 50m);

        await refundService.IssueRefundAsync(new RefundRequest("tx_over", 30m, "x", "idem-a"), default);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await refundService.IssueRefundAsync(new RefundRequest("tx_over", 25m, "x", "idem-b"), default));
    }

    [Fact]
    public async Task DisputeOpened_FreezesActiveSubscription()
    {
        var (db, _, disputeService) = Build();
        await SeedCompletedSubscriptionPaymentAsync(db, "u5", "tx_dispute", 100m);

        var response = await disputeService.RecordSignalAsync(
            new DisputeWebhookSignal("stripe", "dp_1", "tx_dispute", "dispute_opened", 100m, "AUD", "fraudulent"),
            default);

        Assert.True(response.EntitlementsFrozen);
        var sub = await db.Subscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatus.Suspended, sub.Status);
    }

    [Fact]
    public async Task DisputeWon_ReinstatesEntitlements()
    {
        var (db, _, disputeService) = Build();
        await SeedCompletedSubscriptionPaymentAsync(db, "u6", "tx_dispute_won", 100m);

        await disputeService.RecordSignalAsync(
            new DisputeWebhookSignal("stripe", "dp_2", "tx_dispute_won", "dispute_opened", 100m, "AUD", null),
            default);
        Assert.Equal(SubscriptionStatus.Suspended, (await db.Subscriptions.SingleAsync()).Status);

        await disputeService.RecordSignalAsync(
            new DisputeWebhookSignal("stripe", "dp_2", "tx_dispute_won", "dispute_won", 100m, "AUD", null),
            default);

        var sub = await db.Subscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatus.Active, sub.Status);
        var dispute = await db.PaymentDisputes.SingleAsync();
        Assert.Equal("closed_won", dispute.Status);
        Assert.False(dispute.EntitlementsFrozen);
    }

    [Fact]
    public async Task DisputeLost_CancelsSubscription()
    {
        var (db, _, disputeService) = Build();
        await SeedCompletedSubscriptionPaymentAsync(db, "u7", "tx_dispute_lost", 100m);

        await disputeService.RecordSignalAsync(
            new DisputeWebhookSignal("stripe", "dp_3", "tx_dispute_lost", "dispute_lost", 100m, "AUD", "fraud"),
            default);

        Assert.Equal(SubscriptionStatus.Cancelled, (await db.Subscriptions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Dispute_IsIdempotentOnGatewayDisputeId()
    {
        var (db, _, disputeService) = Build();
        await SeedCompletedSubscriptionPaymentAsync(db, "u8", "tx_dispute_dup", 100m);

        await disputeService.RecordSignalAsync(
            new DisputeWebhookSignal("stripe", "dp_dup", "tx_dispute_dup", "dispute_opened", 100m, "AUD", null), default);
        await disputeService.RecordSignalAsync(
            new DisputeWebhookSignal("stripe", "dp_dup", "tx_dispute_dup", "dispute_funds_withdrawn", 100m, "AUD", null), default);

        Assert.Equal(1, await db.PaymentDisputes.CountAsync());
        var dispute = await db.PaymentDisputes.SingleAsync();
        Assert.Equal("funds_withdrawn", dispute.Status);
        Assert.NotNull(dispute.FundsWithdrawnAt);
    }
}
