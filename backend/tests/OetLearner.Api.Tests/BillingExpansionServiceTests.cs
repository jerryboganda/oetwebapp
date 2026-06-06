using System.Text;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests;

public class BillingExpansionServiceTests
{
    private static LearnerDbContext NewContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LearnerDbContext(options);
    }

    private static TaxRule UkVat() => new()
    {
        Id = "uk_vat",
        Country = "GB",
        Region = "UK",
        TaxType = "vat",
        DisplayName = "UK VAT",
        RatePercent = 20m,
        EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-30),
        ZeroRateForB2BReverseCharge = true,
        IsTaxInclusiveDisplay = true,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static TaxRule EgVat() => new()
    {
        Id = "eg_vat",
        Country = "EG",
        Region = "EGYPT",
        TaxType = "vat",
        DisplayName = "Egypt VAT",
        RatePercent = 14m,
        EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-30),
        ZeroRateForB2BReverseCharge = true,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task TaxResolver_ReturnsEmptyWhenCountryUnknown()
    {
        await using var db = NewContext(nameof(TaxResolver_ReturnsEmptyWhenCountryUnknown));
        var resolver = new TaxResolver(db);
        var breakdown = await resolver.ResolveAsync(new TaxResolutionRequest("US", null, 100m, "USD", "subscription", false), CancellationToken.None);
        Assert.True(breakdown.IsEmpty);
    }

    [Fact]
    public async Task TaxResolver_AppliesUkVatToB2C()
    {
        await using var db = NewContext(nameof(TaxResolver_AppliesUkVatToB2C));
        db.TaxRules.Add(UkVat());
        await db.SaveChangesAsync();

        var resolver = new TaxResolver(db);
        var breakdown = await resolver.ResolveAsync(new TaxResolutionRequest("GB", null, 100m, "GBP", "subscription", false), CancellationToken.None);

        Assert.Equal(20m, breakdown.TotalTaxAmount);
        Assert.Single(breakdown.Lines);
        Assert.Equal("vat", breakdown.Lines[0].TaxType);
        Assert.Equal(20m, breakdown.Lines[0].Amount);
    }

    [Fact]
    public async Task TaxResolver_ReverseChargesB2BWithForeignVatId()
    {
        await using var db = NewContext(nameof(TaxResolver_ReverseChargesB2BWithForeignVatId));
        db.TaxRules.Add(UkVat());
        await db.SaveChangesAsync();

        var resolver = new TaxResolver(db);
        var breakdown = await resolver.ResolveAsync(new TaxResolutionRequest("GB", "DE123456789", 100m, "GBP", "subscription", true), CancellationToken.None);

        Assert.Equal(0m, breakdown.TotalTaxAmount);
        Assert.Single(breakdown.Lines);
        Assert.Contains("reverse-charge", breakdown.Lines[0].Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TaxResolver_AppliesEgyptVat()
    {
        await using var db = NewContext(nameof(TaxResolver_AppliesEgyptVat));
        db.TaxRules.Add(EgVat());
        await db.SaveChangesAsync();

        var resolver = new TaxResolver(db);
        var breakdown = await resolver.ResolveAsync(new TaxResolutionRequest("EG", null, 100m, "EGP", "subscription", false), CancellationToken.None);

        Assert.Equal(14m, breakdown.TotalTaxAmount);
    }

    [Fact]
    public async Task ManualPaymentService_RejectsDuplicateProof()
    {
        await using var db = NewContext(nameof(ManualPaymentService_RejectsDuplicateProof));
        var svc = new ManualPaymentService(db);
        var proof = Encoding.UTF8.GetBytes("payment-receipt-content");

        await svc.SubmitAsync("user_a", new ManualPaymentSubmitRequest(null, 100m, "GBP", "bank_transfer", "REF-001", "https://example/proof.png"), proof, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SubmitAsync("user_b", new ManualPaymentSubmitRequest(null, 100m, "GBP", "bank_transfer", "REF-002", "https://example/proof.png"), proof, CancellationToken.None));
    }

    [Fact]
    public async Task ManualPaymentService_AllowsSameUserResubmittingSameProof()
    {
        await using var db = NewContext(nameof(ManualPaymentService_AllowsSameUserResubmittingSameProof));
        var svc = new ManualPaymentService(db);
        var proof = Encoding.UTF8.GetBytes("payment-receipt-content");

        await svc.SubmitAsync("user_a", new ManualPaymentSubmitRequest(null, 100m, "GBP", "bank_transfer", "REF-001", "https://example/proof.png"), proof, CancellationToken.None);
        var second = await svc.SubmitAsync("user_a", new ManualPaymentSubmitRequest(null, 100m, "GBP", "bank_transfer", "REF-002", "https://example/proof.png"), proof, CancellationToken.None);

        Assert.Equal("user_a", second.UserId);
    }

    [Fact]
    public async Task ManualPaymentService_ApproveTransitionsStatus()
    {
        await using var db = NewContext(nameof(ManualPaymentService_ApproveTransitionsStatus));
        var svc = new ManualPaymentService(db);
        var submitted = await svc.SubmitAsync("user_a", new ManualPaymentSubmitRequest(null, 100m, "GBP", "bank_transfer", "REF", "https://example/p.png"), Encoding.UTF8.GetBytes("x"), CancellationToken.None);

        var approved = await svc.ApproveAsync(submitted.Id, "admin_1", "Verified", CancellationToken.None);

        Assert.Equal("approved", approved.Status);
        Assert.Equal("admin_1", approved.ReviewedByAdminId);
    }

    [Fact]
    public async Task ManualPaymentService_RejectsAlreadyResolvedRequest()
    {
        await using var db = NewContext(nameof(ManualPaymentService_RejectsAlreadyResolvedRequest));
        var svc = new ManualPaymentService(db);
        var submitted = await svc.SubmitAsync("user_a", new ManualPaymentSubmitRequest(null, 100m, "GBP", "bank_transfer", "REF", "https://example/p.png"), Encoding.UTF8.GetBytes("y"), CancellationToken.None);

        await svc.ApproveAsync(submitted.Id, "admin", null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RejectAsync(submitted.Id, "admin", "late rejection", CancellationToken.None));
    }

    [Fact]
    public async Task DunningService_StartsActiveCampaign()
    {
        await using var db = NewContext(nameof(DunningService_StartsActiveCampaign));
        var svc = new DunningCampaignService(db);

        var campaign = await svc.StartAsync("sub_1", "user_a", "card_declined", "insufficient_funds", CancellationToken.None);

        Assert.Equal("active", campaign.Status);
        Assert.Equal("card_declined", campaign.LastFailureCode);
    }

    [Fact]
    public async Task DunningService_AdvanceMarksStepsCompleted()
    {
        await using var db = NewContext(nameof(DunningService_AdvanceMarksStepsCompleted));
        var svc = new DunningCampaignService(db);
        var campaign = await svc.StartAsync("sub_1", "user_a", null, null, CancellationToken.None);

        // Simulate the campaign started 4 days ago so day0/1/3 steps fire.
        campaign.StartedAt = DateTimeOffset.UtcNow.AddDays(-4);
        await db.SaveChangesAsync();

        await svc.AdvanceAsync(campaign, CancellationToken.None);

        Assert.Contains("day0_email", campaign.StepsCompletedCsv);
        Assert.Contains("day1_retry", campaign.StepsCompletedCsv);
        Assert.Contains("day3_retry_email", campaign.StepsCompletedCsv);
    }

    [Fact]
    public async Task DunningService_TerminalCancelAfter21Days()
    {
        await using var db = NewContext(nameof(DunningService_TerminalCancelAfter21Days));
        var svc = new DunningCampaignService(db);
        var campaign = await svc.StartAsync("sub_1", "user_a", null, null, CancellationToken.None);
        campaign.StartedAt = DateTimeOffset.UtcNow.AddDays(-22);
        await db.SaveChangesAsync();

        await svc.AdvanceAsync(campaign, CancellationToken.None);

        Assert.Equal("cancelled", campaign.Status);
        Assert.NotNull(campaign.CancelledAt);
    }

    [Fact]
    public async Task AffiliateService_AttributesFirstClickOnly()
    {
        await using var db = NewContext(nameof(AffiliateService_AttributesFirstClickOnly));
        db.Affiliates.AddRange(
            new Affiliate { Id = "a1", Code = "CODE1", OwnerName = "Ag1", ContactEmail = "a@b", CommissionPercent = 15m, PayoutThresholdAmount = 100m, Status = "active", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow },
            new Affiliate { Id = "a2", Code = "CODE2", OwnerName = "Ag2", ContactEmail = "a2@b", CommissionPercent = 25m, PayoutThresholdAmount = 100m, Status = "active", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var svc = new AffiliateService(db);
        await svc.AttributeUserAsync("u1", "CODE1", CancellationToken.None);
        await svc.AttributeUserAsync("u1", "CODE2", CancellationToken.None); // ignored — first-click wins

        var attribution = await db.AffiliateAttributions.FirstAsync(a => a.UserId == "u1");
        Assert.Equal("a1", attribution.AffiliateId);
    }

    [Fact]
    public async Task AffiliateService_AccruesAndReversesCommission()
    {
        await using var db = NewContext(nameof(AffiliateService_AccruesAndReversesCommission));
        db.Affiliates.Add(new Affiliate { Id = "a1", Code = "CODE", OwnerName = "Ag", ContactEmail = "a@b", CommissionPercent = 20m, PayoutThresholdAmount = 100m, Status = "active", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var svc = new AffiliateService(db);
        await svc.AttributeUserAsync("u1", "CODE", CancellationToken.None);
        var commission = await svc.AccrueCommissionAsync("u1", "pt_1", 100m, "GBP", CancellationToken.None);

        Assert.NotNull(commission);
        Assert.Equal(20m, commission!.AmountAmount);
        Assert.Equal("accrued", commission.Status);

        await svc.ReverseCommissionAsync("pt_1", CancellationToken.None);
        var after = await db.AffiliateCommissions.FirstAsync(c => c.PaymentTransactionId == "pt_1");
        Assert.Equal("reversed", after.Status);
        Assert.NotNull(after.ReversedAt);
    }
}
