using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
using Xunit;

namespace OetLearner.Api.Tests.Billing;

/// <summary>
/// Regression coverage for the invoice evidence-source model that closes the
/// "Paid invoice with no payment evidence" gap: <see cref="InvoiceEvidenceResolver"/>
/// must classify a subscription's real evidence as gateway / manual-proof / admin-grant
/// correctly, <see cref="InvoiceEvidenceReconciliationService"/> must backfill that
/// classification onto legacy invoice rows exactly once, and
/// <see cref="AdminService.GetBillingInvoiceEvidenceAsync"/> must only flag a genuine
/// gap (never an admin-grant invoice's by-design absence of a quote/payment) and must
/// still catch a "Paid" gateway invoice with zero payment evidence. Uses the lightweight
/// direct-<see cref="LearnerDbContext"/>-plus-manually-constructed-<see cref="AdminService"/>
/// harness (mirrors <c>AdminSubscriptionEntitlementAdjustTests</c>) rather than the full
/// <c>WebApplicationFactory</c>, since none of the covered code paths touch anything but
/// <c>db</c>.
/// </summary>
public sealed class InvoiceEvidenceSourceTests
{
    // ── InvoiceEvidenceResolver ─────────────────────────────────

    [Fact]
    public async Task ResolveAsync_CompletedGatewayPayment_ReturnsGatewaySourceWithQuoteAndPayment()
    {
        await using var db = NewDb();
        var userId = "usr-resolver-gateway";
        var subscription = NewSubscription(userId, price: 100m, currency: "GBP");
        db.Subscriptions.Add(subscription);

        var quote = NewQuote(userId, subscription.Id, amount: 100m, currency: "GBP");
        db.BillingQuotes.Add(quote);

        var payment = NewCompletedPayment(userId, quote.Id, amount: 100m, currency: "GBP");
        db.PaymentTransactions.Add(payment);
        await db.SaveChangesAsync();

        var result = await InvoiceEvidenceResolver.ResolveAsync(db, subscription, CancellationToken.None);

        Assert.Equal(InvoiceSources.Gateway, result.Source);
        Assert.NotNull(result.Quote);
        Assert.Equal(quote.Id, result.Quote!.Id);
        Assert.NotNull(result.Payment);
        Assert.Equal(payment.Id, result.Payment!.Id);
        Assert.Null(result.Proof);
    }

    [Fact]
    public async Task ResolveAsync_ApprovedManualProofOnly_ReturnsManualProofSourceWithProof()
    {
        await using var db = NewDb();
        var userId = "usr-resolver-manual-proof";
        var subscription = NewSubscription(userId, price: 75m, currency: "AUD");
        db.Subscriptions.Add(subscription);

        var proof = NewApprovedProof(userId, subscription.Id);
        db.ManualPaymentRequests.Add(proof);
        await db.SaveChangesAsync();

        var result = await InvoiceEvidenceResolver.ResolveAsync(db, subscription, CancellationToken.None);

        Assert.Equal(InvoiceSources.ManualProof, result.Source);
        Assert.NotNull(result.Proof);
        Assert.Equal(proof.Id, result.Proof!.Id);
        Assert.Null(result.Quote);
        Assert.Null(result.Payment);
    }

    [Fact]
    public async Task ResolveAsync_ApprovedManualProofWithLinkedQuoteAndManualTransaction_ReturnsManualProofNotGateway()
    {
        // Regression for the realistic proof-approved-subscription-purchase path:
        // ManualPaymentService.ApproveAsync mints a completed PaymentTransaction tagged
        // Gateway="manual" against the learner's real BillingQuote (see
        // ManualPaymentService.cs ApproveAsync / QuoteId linkage). That transaction is
        // "completed" and quote-linked exactly like a real gateway payment, so it must
        // NOT satisfy the gateway-evidence branch -- only a non-"manual" gateway tag may.
        await using var db = NewDb();
        var userId = "usr-resolver-manual-with-quote";
        var subscription = NewSubscription(userId, price: 100m, currency: "GBP");
        db.Subscriptions.Add(subscription);

        var quote = NewQuote(userId, subscription.Id, amount: 100m, currency: "GBP");
        db.BillingQuotes.Add(quote);

        var manualPayment = NewCompletedPayment(userId, quote.Id, amount: 100m, currency: "GBP");
        manualPayment.Gateway = "manual";
        manualPayment.GatewayTransactionId = "manual_proof-abc123";
        db.PaymentTransactions.Add(manualPayment);

        var proof = NewApprovedProof(userId, subscription.Id);
        proof.QuoteId = quote.Id;
        proof.PaymentTransactionId = manualPayment.Id;
        db.ManualPaymentRequests.Add(proof);
        await db.SaveChangesAsync();

        var result = await InvoiceEvidenceResolver.ResolveAsync(db, subscription, CancellationToken.None);

        Assert.Equal(InvoiceSources.ManualProof, result.Source);
        Assert.NotNull(result.Proof);
        Assert.Equal(proof.Id, result.Proof!.Id);
        Assert.Null(result.Quote);
        Assert.Null(result.Payment);
    }

    [Fact]
    public async Task ResolveAsync_NoEvidenceAtAll_ReturnsAdminGrantSourceWithNulls()
    {
        await using var db = NewDb();
        var userId = "usr-resolver-admin-grant";
        var subscription = NewSubscription(userId, price: 0m, currency: "AUD");
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var result = await InvoiceEvidenceResolver.ResolveAsync(db, subscription, CancellationToken.None);

        Assert.Equal(InvoiceSources.AdminGrant, result.Source);
        Assert.Null(result.Quote);
        Assert.Null(result.Payment);
        Assert.Null(result.Proof);
    }

    // ── InvoiceEvidenceReconciliationService ─────────────────────

    [Fact]
    public async Task ReconcileAsync_BackfillsMatchedGatewayEvidence_FlagsOrphan_AndIsIdempotent()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;

        var matchedUserId = "usr-reconcile-matched";
        var subscription = NewSubscription(matchedUserId, price: 100m, currency: "GBP");
        subscription.StartedAt = now.AddDays(-5);
        db.Subscriptions.Add(subscription);

        var quote = NewQuote(matchedUserId, subscription.Id, amount: 100m, currency: "GBP");
        db.BillingQuotes.Add(quote);

        var payment = NewCompletedPayment(matchedUserId, quote.Id, amount: 100m, currency: "GBP");
        db.PaymentTransactions.Add(payment);

        var matchedInvoice = new Invoice
        {
            Id = "inv-legacy-matched-" + Guid.NewGuid().ToString("N")[..8],
            UserId = matchedUserId,
            IssuedAt = now,
            Amount = 100m,
            Currency = "GBP",
            Status = "Paid",
            Description = "Legacy invoice with a discoverable subscription",
        };
        db.Invoices.Add(matchedInvoice);

        var orphanInvoice = new Invoice
        {
            Id = "inv-legacy-orphan-" + Guid.NewGuid().ToString("N")[..8],
            UserId = "usr-reconcile-orphan-" + Guid.NewGuid().ToString("N")[..8],
            IssuedAt = now,
            Amount = 999.99m,
            Currency = "EGP",
            Status = "Paid",
            Description = "Legacy invoice with no matching subscription",
        };
        db.Invoices.Add(orphanInvoice);

        await db.SaveChangesAsync();

        var touched = await InvoiceEvidenceReconciliationService.ReconcileAsync(db, CancellationToken.None);
        Assert.Equal(2, touched);

        var reloadedMatched = await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == matchedInvoice.Id);
        Assert.Equal(InvoiceSources.Gateway, reloadedMatched.Source);
        Assert.Equal(subscription.Id, reloadedMatched.SubscriptionId);
        Assert.Equal(quote.Id, reloadedMatched.QuoteId);
        Assert.Equal(quote.CheckoutSessionId, reloadedMatched.CheckoutSessionId);
        Assert.NotNull(reloadedMatched.ReconciledAt);

        var reloadedOrphan = await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == orphanInvoice.Id);
        Assert.NotNull(reloadedOrphan.ReconciledAt);
        Assert.Null(reloadedOrphan.SubscriptionId);

        var orphanAudits = await db.AuditEvents
            .Where(a => a.Action == "invoice.reconciliation.orphan_flagged" && a.ResourceId == orphanInvoice.Id)
            .ToListAsync();
        Assert.Single(orphanAudits);

        // Second run must be a no-op: both rows already carry ReconciledAt.
        var touchedAgain = await InvoiceEvidenceReconciliationService.ReconcileAsync(db, CancellationToken.None);
        Assert.Equal(0, touchedAgain);

        var orphanAuditCountAfterSecondRun = await db.AuditEvents
            .CountAsync(a => a.Action == "invoice.reconciliation.orphan_flagged" && a.ResourceId == orphanInvoice.Id);
        Assert.Equal(1, orphanAuditCountAfterSecondRun);
    }

    // ── AdminService.GetBillingInvoiceEvidenceAsync ───────────────

    [Fact]
    public async Task GetBillingInvoiceEvidenceAsync_AdminGrantInvoice_DoesNotFlagMissingQuoteOrPayment()
    {
        await using var db = NewDb();
        var invoice = new Invoice
        {
            Id = "inv-admin-grant-" + Guid.NewGuid().ToString("N")[..8],
            UserId = "usr-admin-grant",
            IssuedAt = DateTimeOffset.UtcNow,
            Amount = 0m,
            Currency = "GBP",
            Status = "Paid",
            Description = "Admin-granted access with no payment evidence",
            Source = InvoiceSources.AdminGrant,
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var service = NewAdminService(db);
        var evidence = await service.GetBillingInvoiceEvidenceAsync(invoice.Id, CancellationToken.None);

        Assert.DoesNotContain("quote", evidence.NotRecorded);
        Assert.DoesNotContain("payment", evidence.NotRecorded);
    }

    [Fact]
    public async Task GetBillingInvoiceEvidenceAsync_PaidGatewayInvoiceWithNoPaymentRows_FlagsPaidWithoutEvidence()
    {
        await using var db = NewDb();
        var invoice = new Invoice
        {
            Id = "inv-gateway-no-payment-" + Guid.NewGuid().ToString("N")[..8],
            UserId = "usr-gateway-no-payment",
            IssuedAt = DateTimeOffset.UtcNow,
            Amount = 100m,
            Currency = "GBP",
            Status = "Paid",
            Description = "Gateway-sourced invoice missing its payment evidence",
            Source = InvoiceSources.Gateway,
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var service = NewAdminService(db);
        var evidence = await service.GetBillingInvoiceEvidenceAsync(invoice.Id, CancellationToken.None);

        Assert.Contains("paid_status_without_gateway_evidence", evidence.IntegrityFlags);
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static LearnerDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LearnerDbContext(options);
    }

    private static AdminService NewAdminService(LearnerDbContext db)
    {
        // Every code path exercised here (GetBillingInvoiceEvidenceAsync) only touches
        // `db` -- the other primary-constructor parameters are never invoked, so passing
        // null! is safe and keeps these tests narrowly scoped (mirrors
        // AdminSubscriptionEntitlementAdjustTests.NewAdminService).
        return new AdminService(
            db,
            emailOtpService: null!,
            passwordHasher: null!,
            passwordPolicyService: null!,
            timeProvider: TimeProvider.System,
            notifications: null!,
            learnerService: null!);
    }

    private static Subscription NewSubscription(string userId, decimal price, string currency)
    {
        var now = DateTimeOffset.UtcNow;
        return new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = userId,
            PlanId = "full-condensed-medicine",
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-3),
            ChangedAt = now,
            NextRenewalAt = now.AddMonths(6),
            PriceAmount = price,
            Currency = currency,
            Interval = "one_time",
        };
    }

    private static BillingQuote NewQuote(string userId, string subscriptionId, decimal amount, string currency)
    {
        var now = DateTimeOffset.UtcNow;
        return new BillingQuote
        {
            Id = $"quote-{Guid.NewGuid():N}",
            UserId = userId,
            SubscriptionId = subscriptionId,
            PlanCode = "full-condensed-medicine",
            Currency = currency,
            SubtotalAmount = amount,
            DiscountAmount = 0m,
            TotalAmount = amount,
            Status = BillingQuoteStatus.Completed,
            CreatedAt = now.AddMinutes(-10),
            ExpiresAt = now.AddDays(1),
            CheckoutSessionId = $"checkout-{Guid.NewGuid():N}",
        };
    }

    private static PaymentTransaction NewCompletedPayment(string userId, string quoteId, decimal amount, string currency)
    {
        var now = DateTimeOffset.UtcNow;
        return new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            LearnerUserId = userId,
            Gateway = "stripe",
            GatewayTransactionId = $"txn-{Guid.NewGuid():N}",
            TransactionType = "subscription_payment",
            Status = "completed",
            Amount = amount,
            Currency = currency,
            ProductType = "plan",
            ProductId = "full-condensed-medicine",
            QuoteId = quoteId,
            CreatedAt = now.AddMinutes(-9),
            UpdatedAt = now.AddMinutes(-1),
        };
    }

    private static ManualPaymentRequest NewApprovedProof(string userId, string subscriptionId)
    {
        var now = DateTimeOffset.UtcNow;
        return new ManualPaymentRequest
        {
            Id = $"proof-{Guid.NewGuid():N}",
            UserId = userId,
            Method = "bank_transfer",
            Kind = PaymentProofKinds.LearnerUpload,
            Status = "approved",
            SubmittedAt = now.AddMinutes(-30),
            ReviewedAt = now.AddMinutes(-5),
            AccessGrantedSubscriptionId = subscriptionId,
            CreatedAt = now.AddMinutes(-30),
            UpdatedAt = now.AddMinutes(-5),
        };
    }
}
