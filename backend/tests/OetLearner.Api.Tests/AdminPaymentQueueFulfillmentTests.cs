using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Domain.Billing;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests;

public class AdminPaymentQueueFulfillmentTests
{
    private static LearnerDbContext NewContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LearnerDbContext(options);
    }

    private static HttpContext CreateAdminHttpContext(string adminId = "admin_123", string adminName = "Dr Admin")
    {
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, adminId),
            new Claim(ClaimTypes.Name, adminName),
            new Claim(ClaimTypes.Role, "Admin")
        }, "TestAuth");
        context.User = new ClaimsPrincipal(identity);
        return context;
    }

    private static void SeedPlan(LearnerDbContext db, string planCode = "plan_oet_premium", int includedCredits = 5, int bundledAiCredits = 10)
    {
        var now = DateTimeOffset.UtcNow;
        if (!db.BillingPlans.Any(p => p.Code == planCode))
        {
            db.BillingPlans.Add(new BillingPlan
            {
                Id = planCode,
                Code = planCode,
                Name = "OET Master Class Premium",
                Price = 150m,
                Currency = "GBP",
                Interval = "one_time",
                DurationMonths = 6,
                AccessDurationDays = 180,
                IncludedCredits = includedCredits,
                BundledAiCredits = bundledAiCredits,
                DeliveryMethod = DeliveryMethods.AutomaticWeb,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
    }

    private static void SeedUser(LearnerDbContext db, string userId = "user_cand_1", string email = "candidate@example.com", string name = "John Doe")
    {
        var now = DateTimeOffset.UtcNow;
        if (!db.Users.Any(u => u.Id == userId))
        {
            db.Users.Add(new LearnerUser
            {
                Id = userId,
                Email = email,
                DisplayName = name,
                Role = "learner",
                CreatedAt = now,
            });
        }
    }

    [Fact]
    public async Task TestA_SuccessfulOnlinePayment_AppearsInPendingFulfilmentQueue()
    {
        await using var db = NewContext(nameof(TestA_SuccessfulOnlinePayment_AppearsInPendingFulfilmentQueue));
        var userId = "cand_test_a";
        var planCode = "plan_oet_premium";
        SeedUser(db, userId, "candidate_a@example.com", "Alice Smith");
        SeedPlan(db, planCode, includedCredits: 5, bundledAiCredits: 10);

        var now = DateTimeOffset.UtcNow;
        var subscription = new Subscription
        {
            Id = "sub_test_a",
            UserId = userId,
            PlanId = planCode,
            Status = SubscriptionStatus.Pending,
            FulfilmentStatus = FulfilmentStatuses.PendingVerification,
            PriceAmount = 150m,
            Currency = "GBP",
            Interval = "one_time",
            AccessDurationDays = 180,
            StartedAt = now,
            ChangedAt = now,
        };
        db.Subscriptions.Add(subscription);

        var quote = new BillingQuote
        {
            Id = "quote_test_a",
            UserId = userId,
            PlanCode = planCode,
            SubscriptionId = subscription.Id,
            Status = BillingQuoteStatus.Completed,
            TotalAmount = 150m,
            Currency = "GBP",
            CreatedAt = now,
        };
        db.BillingQuotes.Add(quote);

        var tx = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            LearnerUserId = userId,
            QuoteId = quote.Id,
            Gateway = "stripe",
            GatewayTransactionId = "ch_test_123456",
            Status = "completed",
            Amount = 150m,
            Currency = "GBP",
            TransactionType = "subscription_payment",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.PaymentTransactions.Add(tx);

        var manualSvc = new ManualPaymentService(db, new MemoryFileStorage());
        await manualSvc.CreateGatewayReceiptAsync(userId, tx, "OET Master Class Premium", planCode, subscription.Id, CancellationToken.None);
        await db.SaveChangesAsync();

        // Query the Pending Fulfilment Queue endpoint method
        var result = await BillingExpansionEndpointsAccessor.InvokeListPendingFulfilment(db, CancellationToken.None);
        var queue = result.Value;

        Assert.NotNull(queue);
        Assert.Single(queue);
        var item = queue[0];
        Assert.Equal(subscription.Id, item.SubscriptionId);
        Assert.Equal(userId, item.UserId);
        Assert.Equal("Alice Smith", item.DisplayName);
        Assert.Equal("candidate_a@example.com", item.Email);
        Assert.Equal(150m, item.Amount);
        Assert.Equal("GBP", item.Currency);
        Assert.Equal("stripe", item.Gateway);
        Assert.Equal("ch_test_123456", item.TransactionId);
        Assert.Equal(FulfilmentStatuses.PendingVerification, item.FulfilmentStatus);
    }

    [Fact]
    public async Task TestB_AdminFulfillment_GrantsAccessAndCredits_AndRemovesFromQueue()
    {
        await using var db = NewContext(nameof(TestB_AdminFulfillment_GrantsAccessAndCredits_AndRemovesFromQueue));
        var userId = "cand_test_b";
        var planCode = "plan_oet_premium";
        SeedUser(db, userId, "candidate_b@example.com", "Bob Jones");
        SeedPlan(db, planCode, includedCredits: 5, bundledAiCredits: 10);

        var now = DateTimeOffset.UtcNow;
        var subscription = new Subscription
        {
            Id = "sub_test_b",
            UserId = userId,
            PlanId = planCode,
            Status = SubscriptionStatus.Pending,
            FulfilmentStatus = FulfilmentStatuses.PendingVerification,
            PriceAmount = 150m,
            Currency = "GBP",
            Interval = "one_time",
            AccessDurationDays = 180,
            StartedAt = now,
            ChangedAt = now,
        };
        db.Subscriptions.Add(subscription);

        var receipt = new ManualPaymentRequest
        {
            Id = "mpr_test_b",
            UserId = userId,
            Kind = PaymentProofKinds.GatewayReceipt,
            Gateway = "stripe",
            AmountAmount = 150m,
            Currency = "GBP",
            Method = "stripe",
            Reference = "ch_stripe_b",
            Status = "pending",
            AccessGrantedSubscriptionId = subscription.Id,
            CandidateFullName = "Bob Jones",
            CandidateEmail = "candidate_b@example.com",
            CourseName = "OET Master Class Premium",
            CourseId = planCode,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ManualPaymentRequests.Add(receipt);
        await db.SaveChangesAsync();

        var httpContext = CreateAdminHttpContext("admin_999", "Super Admin");
        var aiCreditService = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);

        // Execute Admin Fulfillment
        var result = await BillingExpansionEndpointsAccessor.InvokeMarkSubscriptionFulfilled(
            subscription.Id,
            httpContext,
            new ApproveRejectRequest(Notes: "Payment verified by admin"),
            db,
            aiCreditService,
            CancellationToken.None);

        Assert.True(result.Result is Ok<PendingFulfilmentDto>);
        var fulfilledDto = ((Ok<PendingFulfilmentDto>)result.Result).Value!;
        Assert.Equal(FulfilmentStatuses.Fulfilled, fulfilledDto.FulfilmentStatus);

        // Verify Database Invariants
        var updatedSub = await db.Subscriptions.FindAsync(subscription.Id);
        Assert.NotNull(updatedSub);
        Assert.Equal(SubscriptionStatus.Active, updatedSub!.Status);
        Assert.Equal(FulfilmentStatuses.Fulfilled, updatedSub.FulfilmentStatus);

        var updatedUser = await db.Users.FindAsync(userId);
        Assert.NotNull(updatedUser);
        Assert.Equal(planCode, updatedUser!.CurrentPlanId);

        // Verify Wallet Credits were granted
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
        Assert.NotNull(wallet);
        Assert.Equal(5, wallet!.CreditBalance);

        // Verify AI Package Gift Credits were granted in the AI account
        var aiAccount = await db.AiPackageCreditAccounts.FirstOrDefaultAsync(a => a.UserId == userId);
        Assert.NotNull(aiAccount);
        Assert.Equal(10, aiAccount!.SharedCredits);

        // Verify receipt status is updated to paid
        var updatedReceipt = await db.ManualPaymentRequests.FindAsync(receipt.Id);
        Assert.NotNull(updatedReceipt);
        Assert.Equal("paid", updatedReceipt!.Status);

        // Verify it is removed from active Pending Fulfilment queue
        var queueResult = await BillingExpansionEndpointsAccessor.InvokeListPendingFulfilment(db, CancellationToken.None);
        Assert.Empty(queueResult.Value!);
    }

    [Fact]
    public async Task TestC_FailedPayment_DoesNotAppearInPendingFulfilmentQueue()
    {
        await using var db = NewContext(nameof(TestC_FailedPayment_DoesNotAppearInPendingFulfilmentQueue));
        var userId = "cand_test_c";
        var planCode = "plan_oet_premium";
        SeedUser(db, userId, "candidate_c@example.com", "Charlie Brown");
        SeedPlan(db, planCode);

        var now = DateTimeOffset.UtcNow;
        var subscription = new Subscription
        {
            Id = "sub_test_c",
            UserId = userId,
            PlanId = planCode,
            Status = SubscriptionStatus.Draft, // Unpaid
            FulfilmentStatus = FulfilmentStatuses.Auto,
            PriceAmount = 150m,
            Currency = "GBP",
            StartedAt = now,
            ChangedAt = now,
        };
        db.Subscriptions.Add(subscription);

        var tx = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            LearnerUserId = userId,
            Gateway = "stripe",
            GatewayTransactionId = "ch_failed_123",
            Status = "failed",
            Amount = 150m,
            Currency = "GBP",
            TransactionType = "subscription_payment",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.PaymentTransactions.Add(tx);
        await db.SaveChangesAsync();

        var queueResult = await BillingExpansionEndpointsAccessor.InvokeListPendingFulfilment(db, CancellationToken.None);
        Assert.Empty(queueResult.Value!);
    }

    [Fact]
    public async Task TestD_CartOnly_DoesNotAppearInPendingFulfilmentQueue()
    {
        await using var db = NewContext(nameof(TestD_CartOnly_DoesNotAppearInPendingFulfilmentQueue));
        var userId = "cand_test_d";
        SeedUser(db, userId);

        var cart = new Cart
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Carts.Add(cart);
        await db.SaveChangesAsync();

        var queueResult = await BillingExpansionEndpointsAccessor.InvokeListPendingFulfilment(db, CancellationToken.None);
        Assert.Empty(queueResult.Value!);
    }

    [Fact]
    public async Task TestE_AbandonedCheckout_DoesNotAppearInPendingFulfilmentQueue()
    {
        await using var db = NewContext(nameof(TestE_AbandonedCheckout_DoesNotAppearInPendingFulfilmentQueue));
        var userId = "cand_test_e";
        var planCode = "plan_oet_premium";
        SeedUser(db, userId);
        SeedPlan(db, planCode);

        var quote = new BillingQuote
        {
            Id = "quote_abandoned",
            UserId = userId,
            PlanCode = planCode,
            Status = BillingQuoteStatus.Created, // Abandoned before payment
            TotalAmount = 150m,
            Currency = "GBP",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.BillingQuotes.Add(quote);
        await db.SaveChangesAsync();

        var queueResult = await BillingExpansionEndpointsAccessor.InvokeListPendingFulfilment(db, CancellationToken.None);
        Assert.Empty(queueResult.Value!);
    }

    [Fact]
    public async Task TestF_DuplicatePaymentReceiptCreation_IsIdempotent()
    {
        await using var db = NewContext(nameof(TestF_DuplicatePaymentReceiptCreation_IsIdempotent));
        var userId = "cand_test_f";
        var planCode = "plan_oet_premium";
        SeedUser(db, userId);
        SeedPlan(db, planCode);

        var now = DateTimeOffset.UtcNow;
        var tx = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            LearnerUserId = userId,
            Gateway = "stripe",
            GatewayTransactionId = "ch_duplicate_test",
            Status = "completed",
            Amount = 150m,
            Currency = "GBP",
            TransactionType = "subscription_payment",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.PaymentTransactions.Add(tx);
        await db.SaveChangesAsync();

        var manualSvc = new ManualPaymentService(db, new MemoryFileStorage());
        var r1 = await manualSvc.CreateGatewayReceiptAsync(userId, tx, "OET Premium", planCode, "sub_1", CancellationToken.None);
        await db.SaveChangesAsync();

        var r2 = await manualSvc.CreateGatewayReceiptAsync(userId, tx, "OET Premium", planCode, "sub_1", CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(r1.Id, r2.Id);
        var totalReceipts = await db.ManualPaymentRequests.CountAsync(r => r.PaymentTransactionId == tx.Id);
        Assert.Equal(1, totalReceipts);
    }

    [Fact]
    public async Task TestG_DuplicateAdminFulfillment_IsIdempotentAndDoesNotDoubleGrantCredits()
    {
        await using var db = NewContext(nameof(TestG_DuplicateAdminFulfillment_IsIdempotentAndDoesNotDoubleGrantCredits));
        var userId = "cand_test_g";
        var planCode = "plan_oet_premium";
        SeedUser(db, userId, "candidate_g@example.com", "Grace Hopper");
        SeedPlan(db, planCode, includedCredits: 5, bundledAiCredits: 10);

        var now = DateTimeOffset.UtcNow;
        var subscription = new Subscription
        {
            Id = "sub_test_g",
            UserId = userId,
            PlanId = planCode,
            Status = SubscriptionStatus.Pending,
            FulfilmentStatus = FulfilmentStatuses.PendingVerification,
            PriceAmount = 150m,
            Currency = "GBP",
            Interval = "one_time",
            AccessDurationDays = 180,
            StartedAt = now,
            ChangedAt = now,
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var httpContext = CreateAdminHttpContext("admin_111", "Admin One");
        var aiCreditService = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);

        // 1st fulfillment
        var firstResult = await BillingExpansionEndpointsAccessor.InvokeMarkSubscriptionFulfilled(
            subscription.Id,
            httpContext,
            new ApproveRejectRequest(Notes: "First fulfillment"),
            db,
            aiCreditService,
            CancellationToken.None);
        Assert.True(firstResult.Result is Ok<PendingFulfilmentDto>);

        // 2nd fulfillment attempt (duplicate/retry/concurrency): idempotent
        // success reusing the same entitlement — never a second grant and never
        // a second subscription row. BILL-08.
        var secondResult = await BillingExpansionEndpointsAccessor.InvokeMarkSubscriptionFulfilled(
            subscription.Id,
            httpContext,
            new ApproveRejectRequest(Notes: "Second duplicate fulfillment"),
            db,
            aiCreditService,
            CancellationToken.None);
        Assert.True(secondResult.Result is Ok<PendingFulfilmentDto>);
        var secondDto = ((Ok<PendingFulfilmentDto>)secondResult.Result).Value!;
        Assert.Equal(FulfilmentStatuses.Fulfilled, secondDto.FulfilmentStatus);

        // Still exactly one subscription row for this order.
        Assert.Equal(1, await db.Subscriptions.CountAsync(s => s.Id == subscription.Id));

        // Assert wallet was NOT double credited
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
        Assert.NotNull(wallet);
        Assert.Equal(5, wallet!.CreditBalance);

        // Assert AI credits were NOT double credited
        var aiAccount = await db.AiPackageCreditAccounts.FirstOrDefaultAsync(a => a.UserId == userId);
        Assert.NotNull(aiAccount);
        Assert.Equal(10, aiAccount!.SharedCredits);
    }

    [Fact]
    public async Task TestH_QueueFilters_ReturnCorrectRecords()
    {
        await using var db = NewContext(nameof(TestH_QueueFilters_ReturnCorrectRecords));
        var user1 = "cand_filter_1";
        var user2 = "cand_filter_2";
        SeedUser(db, user1, "user1@example.com", "User One");
        SeedUser(db, user2, "user2@example.com", "User Two");
        SeedPlan(db, "plan_1");
        SeedPlan(db, "plan_2");

        var now = DateTimeOffset.UtcNow;
        // Sub 1: Pending verification WITH completed gateway payment evidence —
        // the queue lists paid-but-awaiting-fulfilment orders only.
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub_pending_1",
            UserId = user1,
            PlanId = "plan_1",
            Status = SubscriptionStatus.Pending,
            FulfilmentStatus = FulfilmentStatuses.PendingVerification,
            StartedAt = now,
            ChangedAt = now,
        });
        db.BillingQuotes.Add(new BillingQuote
        {
            Id = "quote_pending_1",
            UserId = user1,
            SubscriptionId = "sub_pending_1",
            PlanCode = "plan_1",
            Status = BillingQuoteStatus.Completed,
            TotalAmount = 100m,
            Currency = "GBP",
            CreatedAt = now,
            ExpiresAt = now.AddHours(1),
        });
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            LearnerUserId = user1,
            QuoteId = "quote_pending_1",
            Gateway = "stripe",
            GatewayTransactionId = "ch_pending_1",
            Status = "completed",
            Amount = 100m,
            Currency = "GBP",
            TransactionType = "subscription_payment",
            CreatedAt = now,
            UpdatedAt = now,
        });

        // Sub 2: Fulfilled
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub_fulfilled_2",
            UserId = user2,
            PlanId = "plan_2",
            Status = SubscriptionStatus.Active,
            FulfilmentStatus = FulfilmentStatuses.Fulfilled,
            StartedAt = now,
            ChangedAt = now,
        });

        await db.SaveChangesAsync();

        var queueResult = await BillingExpansionEndpointsAccessor.InvokeListPendingFulfilment(db, CancellationToken.None);
        var items = queueResult.Value;

        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal("sub_pending_1", items[0].SubscriptionId);
    }

    /// <summary>Strict rule (owner directive, 2026-08-31): "Automatic Access on website
    /// after clicking Mark Fulfilled must be only for 6 months not more than that ... for
    /// any profession or for any package." A manual-delivery plan misconfigured with more
    /// than 180 days of access must still land in the Pending Fulfilment queue already
    /// capped, and clicking Mark Fulfilled must never grant more than the cap.</summary>
    [Fact]
    public async Task TestI_ManualFulfillment_CapsExcessiveAccessDurationAtSixMonths_ForAnyPackage()
    {
        await using var db = NewContext(nameof(TestI_ManualFulfillment_CapsExcessiveAccessDurationAtSixMonths_ForAnyPackage));
        var userId = "cand_test_i";
        var planCode = "plan_oet_annual_nursing";
        SeedUser(db, userId, "candidate_i@example.com", "Ivy Chan");

        var now = DateTimeOffset.UtcNow;
        // A misconfigured/legacy plan requiring manual (WhatsApp/Telegram) hand-over,
        // set up for a full year of access — the exact loophole the cap must close,
        // regardless of profession or package.
        db.BillingPlans.Add(new BillingPlan
        {
            Id = planCode,
            Code = planCode,
            Name = "Annual Nursing Premium (legacy)",
            Price = 150m,
            Currency = "GBP",
            Interval = "one_time",
            DurationMonths = 12,
            AccessDurationDays = 365,
            DeliveryMethod = DeliveryMethods.ManualWeb,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var proof = new ManualPaymentRequest
        {
            Id = "mpr_test_i",
            UserId = userId,
            Kind = PaymentProofKinds.LearnerUpload,
            Method = "bank_transfer",
            Reference = "txn_i_001",
            AmountAmount = 150m,
            Currency = "GBP",
            Status = "pending",
            CandidateFullName = "Ivy Chan",
            CandidateEmail = "candidate_i@example.com",
            CourseName = "Annual Nursing Premium (legacy)",
            CourseId = planCode,
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ManualPaymentRequests.Add(proof);
        await db.SaveChangesAsync();

        var manualSvc = new ManualPaymentService(db, new MemoryFileStorage());
        var approved = await manualSvc.ApproveAsync(proof.Id, "admin_999", "Proof verified.", CancellationToken.None);
        var subscriptionId = approved.AccessGrantedSubscriptionId;
        Assert.NotNull(subscriptionId);

        // The cap already applies the moment the order is approved into the queue —
        // long before an admin ever clicks Mark Fulfilled.
        var queuedSub = await db.Subscriptions.SingleAsync(s => s.Id == subscriptionId);
        Assert.Equal(SubscriptionStatus.Pending, queuedSub.Status);
        Assert.Equal(FulfilmentStatuses.PendingManual, queuedSub.FulfilmentStatus);
        Assert.NotNull(queuedSub.ExpiresAt);
        Assert.InRange(queuedSub.ExpiresAt!.Value, now.AddDays(179), now.AddDays(181));
        Assert.NotEqual(now.AddDays(365), queuedSub.ExpiresAt);

        var httpContext = CreateAdminHttpContext("admin_999", "Super Admin");
        var aiCreditService = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        var result = await BillingExpansionEndpointsAccessor.InvokeMarkSubscriptionFulfilled(
            subscriptionId!,
            httpContext,
            new ApproveRejectRequest(Notes: "Handed over via WhatsApp."),
            db,
            aiCreditService,
            CancellationToken.None);

        Assert.True(result.Result is Ok<PendingFulfilmentDto>);

        // Automatic web access is now released — but still capped at 6 months, never
        // the plan's misconfigured 365 days.
        var fulfilledSub = await db.Subscriptions.SingleAsync(s => s.Id == subscriptionId);
        Assert.Equal(SubscriptionStatus.Active, fulfilledSub.Status);
        Assert.Equal(FulfilmentStatuses.Fulfilled, fulfilledSub.FulfilmentStatus);
        Assert.NotNull(fulfilledSub.ExpiresAt);
        Assert.InRange(fulfilledSub.ExpiresAt!.Value, now.AddDays(179), now.AddDays(181));
        Assert.NotEqual(now.AddDays(365), fulfilledSub.ExpiresAt);
    }
}

/// <summary>
/// Helper accessor to invoke internal static methods of BillingExpansionEndpoints for tests.
/// </summary>
public static class BillingExpansionEndpointsAccessor
{
    public static async Task<Ok<List<PendingFulfilmentDto>>> InvokeListPendingFulfilment(LearnerDbContext db, CancellationToken ct)
    {
        var method = typeof(BillingExpansionEndpoints).GetMethod(
            "ListPendingFulfilment",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        return (Ok<List<PendingFulfilmentDto>>)await (Task<Ok<List<PendingFulfilmentDto>>>)method!.Invoke(null, new object[] { db, ct })!;
    }

    public static async Task<Results<Ok<PendingFulfilmentDto>, NotFound, BadRequest<string>>> InvokeMarkSubscriptionFulfilled(
        string id,
        HttpContext http,
        ApproveRejectRequest request,
        LearnerDbContext db,
        IAiPackageCreditService? aiPackageCredits,
        CancellationToken ct,
        IManualPaymentService? manualPayments = null,
        LearnerService? learnerService = null)
    {
        var method = typeof(BillingExpansionEndpoints).GetMethod(
            "MarkSubscriptionFulfilled",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        manualPayments ??= new ManualPaymentService(db, new MemoryFileStorage());
        // learnerService is only touched by the best-effort invoice-minting call at the
        // tail of MarkSubscriptionFulfilled, which is wrapped in a try/catch that must
        // never fail the fulfilment itself — null is safe here and gets swallowed.
        return (Results<Ok<PendingFulfilmentDto>, NotFound, BadRequest<string>>)await (Task<Results<Ok<PendingFulfilmentDto>, NotFound, BadRequest<string>>>)method!.Invoke(null, new object?[] { id, http, request, db, aiPackageCredits, manualPayments, learnerService, ct })!;
    }
}
