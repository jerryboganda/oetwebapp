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
    private static (LearnerDbContext db, RefundService refundService, DisputeService disputeService) Build(
        IPaymentGatewayProvider? gatewayProvider = null)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var billingOptions = new BillingOptions { AllowSandboxFallbacks = true };
        var billingOpts = Options.Create(billingOptions);
        var gateways = new PaymentGatewayService(
            new StripeGateway(new HttpClient(), billingOpts, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions)),
            new PayPalGateway(new HttpClient(), billingOpts),
            new OetLearner.Api.Services.Billing.Gateways.PayTabsGateway(new HttpClient(), billingOpts, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions)),
            new OetLearner.Api.Services.Billing.Gateways.PaymobGateway(new HttpClient(), billingOpts, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions)),
            new OetLearner.Api.Services.Billing.Gateways.CheckoutComGateway(new HttpClient(), billingOpts, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions)));
        return (db, new RefundService(db, gatewayProvider ?? gateways), new DisputeService(db));
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
    public async Task PendingIdempotentFullRefund_ReplaysProviderAndCompletesLocalReversals()
    {
        var (db, refundService, _) = Build();
        await SeedCompletedSubscriptionPaymentAsync(db, "u-resume", "tx_resume_refund", 100m, includedCredits: 20);
        var now = DateTimeOffset.UtcNow;
        db.OrderRefunds.Add(new OrderRefund
        {
            Id = Guid.NewGuid(),
            PaymentTransactionId = "tx_resume_refund",
            LearnerUserId = "u-resume",
            Gateway = "stripe",
            GatewayRefundId = "pending:idem-resume-refund",
            IdempotencyKey = "idem-resume-refund",
            RefundType = "full",
            Amount = 100m,
            Currency = "AUD",
            Status = "pending",
            Reason = "requested_by_customer",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var result = await refundService.IssueRefundAsync(
            new RefundRequest("tx_resume_refund", 100m, "requested_by_customer", "idem-resume-refund"),
            default);

        Assert.True(result.Idempotent);
        Assert.Equal("succeeded", result.Status);
        Assert.True(result.ReversedWalletCredits);
        Assert.True(result.ReversedEntitlements);

        var refund = await db.OrderRefunds.SingleAsync();
        Assert.Equal("succeeded", refund.Status);
        Assert.False(refund.GatewayRefundId.StartsWith("pending:", StringComparison.Ordinal));

        var tx = await db.PaymentTransactions.SingleAsync();
        Assert.Equal("refunded", tx.Status);

        var sub = await db.Subscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatus.Cancelled, sub.Status);

        var wallet = await db.Wallets.SingleAsync();
        Assert.Equal(0, wallet.CreditBalance);
    }

    [Fact]
    public async Task PendingProviderRefund_DoesNotWriteIssuedEventUntilReplaySucceeds()
    {
        var gateway = new ScriptedRefundGateway(
            new RefundResult("re-pending-first", "pending", 100m),
            new RefundResult("re-succeeded-replay", "succeeded", 100m));
        var (db, refundService, _) = Build(new SingleGatewayProvider(gateway));
        await SeedCompletedSubscriptionPaymentAsync(db, "u-pending-provider", "tx_provider_pending", 100m, includedCredits: 20);

        var first = await refundService.IssueRefundAsync(
            new RefundRequest("tx_provider_pending", 100m, "requested_by_customer", "idem-provider-pending"),
            default);

        Assert.Equal("pending", first.Status);
        Assert.Empty(await db.BillingEvents.Where(e => e.EventType == "refund_full_issued").ToListAsync());

        var replay = await refundService.IssueRefundAsync(
            new RefundRequest("tx_provider_pending", 100m, "requested_by_customer", "idem-provider-pending"),
            default);

        Assert.True(replay.Idempotent);
        Assert.Equal("succeeded", replay.Status);
        Assert.True(replay.ReversedWalletCredits);
        Assert.True(replay.ReversedEntitlements);
        Assert.Equal(new[] { "idem-provider-pending", "idem-provider-pending" }, gateway.IdempotencyKeys);
        var refundEvent = await db.BillingEvents.SingleAsync(e => e.EventType == "refund_full_issued");
        Assert.Contains("\"reversedWalletCredits\":true", refundEvent.PayloadJson);
        Assert.Contains("\"reversedEntitlements\":true", refundEvent.PayloadJson);
    }

    [Fact]
    public async Task GatewayException_KeepsRefundPendingAndReplayableWithoutAllowingSecondKey()
    {
        var gateway = new ScriptedRefundGateway(
            new InvalidOperationException("gateway timeout after request accepted"),
            new RefundResult("re-timeout-replay", "succeeded", 100m));
        var (db, refundService, _) = Build(new SingleGatewayProvider(gateway));
        await SeedCompletedSubscriptionPaymentAsync(db, "u-timeout", "tx_timeout_refund", 100m, includedCredits: 20);

        await Assert.ThrowsAsync<InvalidOperationException>(() => refundService.IssueRefundAsync(
            new RefundRequest("tx_timeout_refund", 100m, "requested_by_customer", "idem-timeout"),
            default));

        var pending = await db.OrderRefunds.SingleAsync();
        Assert.Equal("pending", pending.Status);

        await Assert.ThrowsAsync<InvalidOperationException>(() => refundService.IssueRefundAsync(
            new RefundRequest("tx_timeout_refund", 100m, "requested_by_customer", "idem-second-key"),
            default));

        var replay = await refundService.IssueRefundAsync(
            new RefundRequest("tx_timeout_refund", 100m, "requested_by_customer", "idem-timeout"),
            default);

        Assert.True(replay.Idempotent);
        Assert.Equal("succeeded", replay.Status);
        Assert.True(replay.ReversedWalletCredits);
        Assert.True(replay.ReversedEntitlements);
        Assert.Equal(new[] { "idem-timeout", "idem-timeout" }, gateway.IdempotencyKeys);
        Assert.Equal(1, await db.OrderRefunds.CountAsync());
    }

    [Fact]
    public async Task FullRefund_ReversesAiPackageCreditsAndLedgerBalance()
    {
        var (db, refundService, _) = Build();
        var now = DateTimeOffset.UtcNow;
        const string userId = "u-ai-refund";
        const string quoteId = "quote-ai-refund";
        const string txId = "tx_ai_refund";

        db.Users.Add(new LearnerUser { Id = userId, DisplayName = "AI", Email = "ai-refund@x.test", CreatedAt = now, LastActiveAt = now });
        db.Wallets.Add(new Wallet { Id = $"w-{userId}", UserId = userId, CreditBalance = 0, LastUpdatedAt = now });
        db.Subscriptions.Add(new Subscription
        {
            Id = $"sub-{userId}",
            UserId = userId,
            PlanId = "basic",
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddMonths(1),
            AiCreditsRemaining = 12,
        });
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            LearnerUserId = userId,
            Gateway = "stripe",
            GatewayTransactionId = txId,
            TransactionType = "subscription_payment",
            Status = "completed",
            Amount = 19m,
            Currency = "AUD",
            ProductType = "addon",
            ProductId = "pkg_quick_check",
            QuoteId = quoteId,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.SubscriptionItems.Add(new SubscriptionItem
        {
            Id = "subitem-ai-refund",
            SubscriptionId = $"sub-{userId}",
            ItemCode = "pkg_quick_check",
            ItemType = "addon",
            Quantity = 1,
            Status = SubscriptionItemStatus.Active,
            StartsAt = now,
            QuoteId = quoteId,
            CheckoutSessionId = txId,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AiCreditLedger.Add(new AiCreditLedgerEntry
        {
            Id = "ledger-ai-package-purchase",
            UserId = userId,
            TokensDelta = 5,
            CostDeltaUsd = 0m,
            Source = AiCreditSource.Purchase,
            Description = "Quick Check AI grading credits",
            ReferenceId = "addon:quote-ai-refund:pkg_quick_check",
            CreatedAt = now,
        });
        db.AiCreditLedger.Add(new AiCreditLedgerEntry
        {
            Id = "ledger-ai-plan-purchase",
            UserId = userId,
            TokensDelta = 7,
            CostDeltaUsd = 0m,
            Source = AiCreditSource.Purchase,
            Description = "Course bundled AI grading credits",
            ReferenceId = "plan:quote-ai-refund:pkg_course",
            CreatedAt = now,
        });
        await db.SaveChangesAsync();

        var result = await refundService.IssueRefundAsync(
            new RefundRequest(txId, 19m, "requested_by_customer", "idem-ai-refund"),
            default);

        var subscription = await db.Subscriptions.SingleAsync();
        var balance = await new OetLearner.Api.Services.AiManagement.AiCreditService(db).GetBalanceAsync(userId, default);

        Assert.True(result.ReversedEntitlements);
        Assert.Equal(0, subscription.AiCreditsRemaining);
        Assert.Equal(0, balance.TokensAvailable);
        Assert.True(await db.AiCreditLedger.AnyAsync(entry => entry.Source == AiCreditSource.AdminAdjustment
                                                             && entry.TokensDelta == -5
                                                             && entry.ReferenceId == "addon-refund:quote-ai-refund:pkg_quick_check"));
        Assert.True(await db.AiCreditLedger.AnyAsync(entry => entry.Source == AiCreditSource.AdminAdjustment
                                     && entry.TokensDelta == -7
                                     && entry.ReferenceId == "plan-refund:quote-ai-refund:pkg_course"));
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

    private sealed class SingleGatewayProvider(IPaymentGateway gateway) : IPaymentGatewayProvider
    {
        public IPaymentGateway GetGateway(string name) => gateway;

        public IReadOnlyList<string> SupportedGateways { get; } = ["stripe"];
    }

    private sealed class ScriptedRefundGateway(params object[] refundResults) : IPaymentGateway
    {
        private readonly Queue<object> _refundResults = new(refundResults);
        private readonly List<string> _idempotencyKeys = [];

        public string GatewayName => "stripe";

        public IReadOnlyList<string> IdempotencyKeys => _idempotencyKeys;

        public Task<PaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<WebhookProcessResult> HandleWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RefundResult> ProcessRefundAsync(
            string transactionId,
            decimal amount,
            string currency,
            string reason,
            string idempotencyKey,
            CancellationToken ct)
        {
            _idempotencyKeys.Add(idempotencyKey);
            var next = _refundResults.Dequeue();
            if (next is Exception exception)
            {
                return Task.FromException<RefundResult>(exception);
            }

            return Task.FromResult((RefundResult)next);
        }
    }
}
