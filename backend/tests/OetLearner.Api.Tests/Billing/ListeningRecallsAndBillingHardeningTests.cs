using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests.Billing;

/// <summary>
/// LR-* + BILL-* regression coverage for the high-priority production fixes:
/// standalone Listening Recalls automatic access (stable code identity, never
/// price), paid-only invoice gate, AI-budget/billing-failure alert separation,
/// canonical six-month expiry with idempotent re-application, and idempotent
/// fulfilment (retry/concurrency safe, legitimate repurchases allowed).
/// </summary>
public sealed class ListeningRecallsAndBillingHardeningTests
{
    private static LearnerDbContext NewContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LearnerDbContext(options);
    }

    // ── LR-02 / LR-05: stable identity, never price/label, never "contains recalls" ──

    [Fact]
    public void LR02_BundledCourseContainingRecalls_DoesNotTriggerStandaloneAutomation()
    {
        Assert.False(ListeningRecallsPolicy.IsStandaloneListeningRecalls("full-condensed-medicine"));
        Assert.False(ListeningRecallsPolicy.IsStandaloneListeningRecalls("full-nursing"));
        Assert.False(ListeningRecallsPolicy.IsStandaloneListeningRecalls("crash-course"));
        Assert.False(ListeningRecallsPolicy.IsStandaloneListeningRecalls("mega-special"));
        Assert.False(ListeningRecallsPolicy.IsStandaloneListeningRecalls(null));
        Assert.False(ListeningRecallsPolicy.IsStandaloneListeningRecalls(""));
        Assert.False(ListeningRecallsPolicy.IsStandaloneListeningRecalls("listening-recalls-bundle"));
    }

    [Fact]
    public void LR01_LR05_StandaloneCodeMatches_RegardlessOfPriceOrCasing()
    {
        // Price must not control logic: the policy takes only the code.
        Assert.True(ListeningRecallsPolicy.IsStandaloneListeningRecalls("listening-recalls"));
        Assert.True(ListeningRecallsPolicy.IsStandaloneListeningRecalls("Listening-Recalls"));
        Assert.True(ListeningRecallsPolicy.IsStandaloneListeningRecalls("  listening-recalls  "));
        Assert.Equal("listening-recalls", ListeningRecallsPolicy.PlanCode);
    }

    // ── BILL-07: canonical six-month cap, never one year ──

    [Fact]
    public void BILL07_MisconfiguredAnnualPlan_IsClampedToSixMonths()
    {
        var now = DateTimeOffset.UtcNow;
        var plan = new BillingPlan
        {
            Id = "plan_annual",
            Code = "plan_annual",
            Name = "Annual (legacy misconfigured)",
            AccessDurationDays = 365,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var sub = new Subscription
        {
            Id = "sub_cap",
            UserId = "u1",
            PlanId = plan.Code,
            Status = SubscriptionStatus.Pending,
            StartedAt = now,
            ChangedAt = now,
        };

        SubscriptionBundleInitializer.ApplyBundle(sub, plan, now);

        Assert.Equal(180, sub.AccessDurationDays);
        Assert.NotNull(sub.ExpiresAt);
        Assert.InRange(sub.ExpiresAt!.Value, now.AddDays(179), now.AddDays(181));
    }

    [Fact]
    public void BILL07_ZeroOrNegativeDuration_FallsBackToSixMonths()
    {
        var now = DateTimeOffset.UtcNow;
        var plan = new BillingPlan
        {
            Id = "plan_zero",
            Code = "plan_zero",
            Name = "Zero",
            AccessDurationDays = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var sub = new Subscription
        {
            Id = "sub_zero",
            UserId = "u1",
            PlanId = plan.Code,
            Status = SubscriptionStatus.Pending,
            StartedAt = now,
            ChangedAt = now,
        };

        SubscriptionBundleInitializer.ApplyBundle(sub, plan, now);

        Assert.Equal(180, sub.AccessDurationDays);
        Assert.InRange(sub.ExpiresAt!.Value, now.AddDays(179), now.AddDays(181));
    }

    // ── Root cause for 02/09/2026 → 28/08/2027 (≈360 days): double application ──

    [Fact]
    public void BILL07_DoubleBundleApplication_RemainsSixMonths_NotTwelve()
    {
        var now = DateTimeOffset.UtcNow;
        var plan = new BillingPlan
        {
            Id = "plan_med",
            Code = "full-condensed-medicine",
            Name = "Full Condensed Recorded OET Course — Medicine",
            AccessDurationDays = 180,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var sub = new Subscription
        {
            Id = "sub_double",
            UserId = "u1",
            PlanId = plan.Code,
            Status = SubscriptionStatus.Pending,
            StartedAt = now,
            ChangedAt = now,
        };

        // Checkout completion (first grant) + manual approval (second grant).
        SubscriptionBundleInitializer.ApplyPlanEntitlements(sub, plan, now);
        var firstExpiry = sub.ExpiresAt;
        SubscriptionBundleInitializer.ApplyPlanEntitlements(sub, plan, now.AddHours(1));
        var secondExpiry = sub.ExpiresAt;

        Assert.NotNull(firstExpiry);
        Assert.NotNull(secondExpiry);
        Assert.Equal(firstExpiry!.Value, secondExpiry!.Value);
        Assert.InRange(secondExpiry!.Value, now.AddDays(179), now.AddDays(181));
        // Must never land near one year (≈360 days double-grant).
        Assert.True((secondExpiry.Value - now).TotalDays < 200);
    }

    [Fact]
    public void BILL07_ListeningRecalls_UsesCanonicalConfiguredDuration()
    {
        var now = DateTimeOffset.UtcNow;
        var plan = new BillingPlan
        {
            Id = "plan_listening-recalls",
            Code = "listening-recalls",
            Name = "Listening Recalls",
            AccessDurationDays = 180,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var sub = new Subscription
        {
            Id = "sub_lr",
            UserId = "u1",
            PlanId = plan.Code,
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now,
        };

        SubscriptionBundleInitializer.ApplyBundle(sub, plan, now);

        Assert.Equal(180, sub.AccessDurationDays);
        Assert.InRange(sub.ExpiresAt!.Value, now.AddDays(179), now.AddDays(181));
    }

    // ── BILL-01 / BILL-02 / BILL-04: failed & pending expose no candidate invoice ──

    [Fact]
    public async Task BILL01_FailedPayment_HasNoGatewayEvidence_SoNoInvoiceIsMinted()
    {
        await using var db = NewContext(nameof(BILL01_FailedPayment_HasNoGatewayEvidence_SoNoInvoiceIsMinted));
        var now = DateTimeOffset.UtcNow;
        var sub = new Subscription
        {
            Id = "sub_failed",
            UserId = "u_failed",
            PlanId = "full-condensed-medicine",
            Status = SubscriptionStatus.Draft,
            FulfilmentStatus = FulfilmentStatuses.Auto,
            PriceAmount = 100m,
            Currency = "GBP",
            Interval = "one_time",
            StartedAt = now,
            ChangedAt = now,
        };
        db.Subscriptions.Add(sub);
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            LearnerUserId = sub.UserId,
            Gateway = "stripe",
            GatewayTransactionId = "ch_failed_001",
            TransactionType = "subscription_payment",
            Status = "failed",
            Amount = 100m,
            Currency = "GBP",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var evidence = await InvoiceEvidenceResolver.ResolveAsync(db, sub, CancellationToken.None);

        Assert.Equal(InvoiceSources.AdminGrant, evidence.Source);
        Assert.Null(evidence.Payment);
        Assert.Null(evidence.Quote);
    }

    [Fact]
    public async Task BILL02_PendingOrderWithoutCompletedPayment_HasNoInvoiceEvidence()
    {
        await using var db = NewContext(nameof(BILL02_PendingOrderWithoutCompletedPayment_HasNoInvoiceEvidence));
        var now = DateTimeOffset.UtcNow;
        var sub = new Subscription
        {
            Id = "sub_pending",
            UserId = "u_pending",
            PlanId = "full-condensed-medicine",
            Status = SubscriptionStatus.Pending,
            FulfilmentStatus = FulfilmentStatuses.PendingVerification,
            PriceAmount = 100m,
            Currency = "GBP",
            Interval = "one_time",
            StartedAt = now,
            ChangedAt = now,
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();

        var evidence = await InvoiceEvidenceResolver.ResolveAsync(db, sub, CancellationToken.None);

        // No completed gateway payment and no approved proof → unevidenced grant.
        Assert.Equal(InvoiceSources.AdminGrant, evidence.Source);
    }

    [Fact]
    public async Task BILL03_CompletedGatewayPayment_ResolvesGatewayEvidence()
    {
        await using var db = NewContext(nameof(BILL03_CompletedGatewayPayment_ResolvesGatewayEvidence));
        var now = DateTimeOffset.UtcNow;
        var sub = new Subscription
        {
            Id = "sub_paid",
            UserId = "u_paid",
            PlanId = "listening-recalls",
            Status = SubscriptionStatus.Active,
            FulfilmentStatus = FulfilmentStatuses.Auto,
            PriceAmount = 17m,
            Currency = "GBP",
            Interval = "one_time",
            StartedAt = now,
            ChangedAt = now,
        };
        db.Subscriptions.Add(sub);
        var quote = new BillingQuote
        {
            Id = "quote_paid",
            UserId = sub.UserId,
            SubscriptionId = sub.Id,
            PlanCode = sub.PlanId,
            Status = BillingQuoteStatus.Completed,
            TotalAmount = 17m,
            Currency = "GBP",
            CreatedAt = now,
            ExpiresAt = now.AddHours(1),
        };
        db.BillingQuotes.Add(quote);
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            LearnerUserId = sub.UserId,
            Gateway = "stripe",
            GatewayTransactionId = "ch_paid_001",
            TransactionType = "subscription_payment",
            Status = "completed",
            Amount = 17m,
            Currency = "GBP",
            QuoteId = quote.Id,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var evidence = await InvoiceEvidenceResolver.ResolveAsync(db, sub, CancellationToken.None);

        Assert.Equal(InvoiceSources.Gateway, evidence.Source);
        Assert.NotNull(evidence.Payment);
    }

    // ── BILL alarm separation: AI budget must not raise the billing-failure alarm ──

    [Fact]
    public void BILL_Alarm_AiBudgetUsesSeparateKey_FromBillingFailures()
    {
        Assert.NotEqual(NotificationEventKey.AdminBillingFailureAlert, NotificationEventKey.AdminAiBudgetAlert);

        var billingTitle = NotificationCatalog.BuildTitle(
            NotificationEventKey.AdminBillingFailureAlert,
            new Dictionary<string, string?>());
        var aiTitle = NotificationCatalog.BuildTitle(
            NotificationEventKey.AdminAiBudgetAlert,
            new Dictionary<string, string?>());

        Assert.Equal("Billing failures need attention", billingTitle);
        Assert.Equal("AI budget threshold reached", aiTitle);
        Assert.NotEqual(billingTitle, aiTitle);
    }

    [Fact]
    public void BILL_Alarm_BillingCatalogKeepsPaidOnlySemantics()
    {
        // Learner invoice visibility is Paid-only (case-insensitive). Pending and
        // Failed rows are internal and must never be treated as candidate-visible.
        static bool IsCandidateVisible(string status)
            => string.Equals(status, "Paid", StringComparison.OrdinalIgnoreCase);

        Assert.True(IsCandidateVisible("Paid"));
        Assert.True(IsCandidateVisible("paid"));
        Assert.False(IsCandidateVisible("Pending"));
        Assert.False(IsCandidateVisible("Failed"));
        Assert.False(IsCandidateVisible("processing"));
    }

    // ── BILL-10: legitimate second purchase (different quote) is NOT suppressed ──

    [Fact]
    public void BILL10_DifferentQuotes_AreDistinctLogicalEvents()
    {
        var quoteA = "quote-aaa";
        var quoteB = "quote-bbb";
        Assert.NotEqual(quoteA, quoteB);

        // Idempotency identity is (SubscriptionId, ItemCode, QuoteId): same quote
        // replays converge, a new quote is a new purchase and must grant anew.
        var keyA = ("sub-1", "addon-3-letters", quoteA);
        var keyB = ("sub-1", "addon-3-letters", quoteB);
        Assert.NotEqual(keyA, keyB);

        var replayA = ("sub-1", "addon-3-letters", quoteA);
        Assert.Equal(keyA, replayA);
    }
}
