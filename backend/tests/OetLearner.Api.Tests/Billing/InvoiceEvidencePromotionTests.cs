using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests.Billing;

/// <summary>
/// Paid-only promotion vectors for the Fulfilment evidence slice (F3, #200).
/// Approving a proof promotes that subscription's Pending invoices to Paid;
/// the verdict routing (ManualProof/Gateway promote, AdminGrant does not)
/// lives in <see cref="InvoiceEvidenceResolver"/>.
/// </summary>
public sealed class InvoiceEvidencePromotionTests
{
    private static LearnerDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static void AddPlan(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan_basic",
            Code = "plan_basic",
            Name = "OET Premium",
            Price = 100m,
            Currency = "GBP",
            Interval = "one_time",
            DurationMonths = 6,
            AccessDurationDays = 180,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private static string SeedProofWithPendingInvoice(
        LearnerDbContext db, string userId, string subId, string proofId)
    {
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = "Evidence Learner",
            Email = $"{userId}@example.test",
            CreatedAt = now,
            LastActiveAt = now,
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = subId,
            UserId = userId,
            PlanId = "plan_basic",
            Status = SubscriptionStatus.Pending,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddMonths(6),
            PriceAmount = 100m,
            Currency = "GBP",
            Interval = "one_time",
        });
        db.ManualPaymentRequests.Add(new ManualPaymentRequest
        {
            Id = proofId,
            UserId = userId,
            AmountAmount = 100m,
            Currency = "GBP",
            Method = "uk_monzo_transfer",
            Reference = "REF-EV",
            CandidateFullName = "Candidate One",
            CandidateEmail = $"{userId}@example.test",
            CourseName = "OET Premium",
            CourseId = "plan_basic",
            Status = "pending",
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Invoices.Add(new Invoice
        {
            Id = $"inv-{proofId}",
            UserId = userId,
            Amount = 100m,
            Currency = "GBP",
            Status = "Pending",
            Description = "Manual order, awaiting approval",
            SubscriptionId = subId,
            Source = "manual_proof",
            IssuedAt = now,
        });
        return proofId;
    }

    [Fact]
    public async Task ApproveAsync_PromotesPendingInvoice_WhenProofApproved()
    {
        await using var db = NewContext();
        AddPlan(db);
        await db.SaveChangesAsync();
        SeedProofWithPendingInvoice(db, "u-ev-1", "sub-ev-1", "proof-ev-1");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var svc = new ManualPaymentService(db, new MemoryFileStorage());
        var approved = await svc.ApproveAsync("proof-ev-1", "admin_1", "Verified", CancellationToken.None);

        Assert.Equal("paid", approved.Status);
        Assert.Equal("Paid", (await db.Invoices.SingleAsync(i => i.Id == "inv-proof-ev-1")).Status);
    }

    [Fact]
    public async Task ApproveAsync_LeavesInvoicePending_WhenSubscriptionNotActive()
    {
        await using var db = NewContext();
        AddPlan(db);
        await db.SaveChangesAsync();
        SeedProofWithPendingInvoice(db, "u-ev-2", "sub-ev-2", "proof-ev-2");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // A plan requiring manual fulfilment stays Pending on approval, so
        // the invoice gate must not promote.
        var plan = await db.BillingPlans.SingleAsync(p => p.Code == "plan_basic");
        plan.DeliveryMethod = "manual_web";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var svc = new ManualPaymentService(db, new MemoryFileStorage());
        await svc.ApproveAsync("proof-ev-2", "admin_1", "Verified", CancellationToken.None);

        Assert.Equal("Pending", (await db.Invoices.SingleAsync(i => i.Id == "inv-proof-ev-2")).Status);
    }
}
