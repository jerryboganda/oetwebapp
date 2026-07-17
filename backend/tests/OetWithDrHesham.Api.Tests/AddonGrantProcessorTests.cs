using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.AiManagement;
using OetWithDrHesham.Api.Services.Billing;

namespace OetWithDrHesham.Api.Tests;

public sealed class AddonGrantProcessorTests
{
    [Fact]
    public async Task ApplyAsync_GrantsAiPackageCreditsToSubscriptionAndLedger_Idempotently()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new LearnerDbContext(options);
        var now = DateTimeOffset.UtcNow;
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-quick-check",
            UserId = "user-quick-check",
            PlanId = "plan-basic",
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddMonths(1),
        });
        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = "addon_pkg_quick_check",
            Code = "pkg_quick_check",
            Name = "Quick Check",
            Status = BillingAddOnStatus.Active,
            GrantCredits = 5,
            GrantEntitlementsJson = "{\"ai_credits\":5}",
            DurationDays = 30,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var processor = new AddonGrantProcessor(db, NullLogger<AddonGrantProcessor>.Instance);
        var first = await processor.ApplyAsync("evt-1", "sub-quick-check", "pkg_quick_check");
        var second = await processor.ApplyAsync("evt-1", "sub-quick-check", "pkg_quick_check");

        var subscription = await db.Subscriptions.SingleAsync();
        var ledger = Assert.Single(await db.AiCreditLedger.ToListAsync());
        Assert.True(first.Applied);
        Assert.True(second.DuplicateSkipped);
        Assert.Equal(5, subscription.AiCreditsRemaining);
        Assert.Equal(AiCreditSource.Purchase, ledger.Source);
        Assert.Equal(5, ledger.TokensDelta);
        Assert.Equal("addon:sub-quick-check:pkg_quick_check:evt-1", ledger.ReferenceId);
        Assert.NotNull(ledger.ExpiresAt);
        Assert.InRange(ledger.ExpiresAt.Value, now.AddDays(29), now.AddDays(31));
    }

    [Fact]
    public async Task ApplyAsync_MapsLegacyReviewCreditsToAiLedgerGrant()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new LearnerDbContext(options);
        var now = DateTimeOffset.UtcNow;
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-legacy-credits",
            UserId = "user-legacy-credits",
            PlanId = "plan-basic",
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddMonths(1),
        });
        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = "addon-credits-3",
            Code = "credits-3",
            Name = "3 Review Credits",
            Status = BillingAddOnStatus.Active,
            GrantCredits = 3,
            GrantEntitlementsJson = "{\"reviewCredits\":3}",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var processor = new AddonGrantProcessor(db, NullLogger<AddonGrantProcessor>.Instance);
        var result = await processor.ApplyAsync("evt-legacy-credits", "sub-legacy-credits", "credits-3");

        var subscription = await db.Subscriptions.SingleAsync();
        var ledger = Assert.Single(await db.AiCreditLedger.ToListAsync());
        Assert.True(result.Applied);
        Assert.Equal(3, subscription.AiCreditsRemaining);
        Assert.Equal(AiCreditSource.Purchase, ledger.Source);
        Assert.Equal(3, ledger.TokensDelta);
        Assert.Equal("addon:sub-legacy-credits:credits-3:evt-legacy-credits", ledger.ReferenceId);
    }

    [Fact]
    public async Task ReverseAsync_ReversesAiPackageCreditsInSubscriptionAndLedger_Idempotently()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new LearnerDbContext(options);
        var now = DateTimeOffset.UtcNow;
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-mastery",
            UserId = "user-mastery",
            PlanId = "plan-basic",
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddMonths(1),
            AiCreditsRemaining = 30,
        });
        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = "addon_pkg_oet_mastery",
            Code = "pkg_oet_mastery",
            Name = "OET Mastery",
            Status = BillingAddOnStatus.Active,
            GrantCredits = 30,
            GrantEntitlementsJson = "{\"ai_credits\":30,\"mockFull\":5,\"priority_queue\":true}",
            DurationDays = 180,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AiCreditLedger.Add(new AiCreditLedgerEntry
        {
            Id = "ledger-purchase-mastery",
            UserId = "user-mastery",
            TokensDelta = 30,
            CostDeltaUsd = 0m,
            Source = AiCreditSource.Purchase,
            Description = "OET Mastery AI grading credits",
            ReferenceId = "addon:sub-mastery:pkg_oet_mastery:evt-purchase-1",
            CreatedAt = now,
        });
        await db.SaveChangesAsync();

        var processor = new AddonGrantProcessor(db, NullLogger<AddonGrantProcessor>.Instance);
        var first = await processor.ReverseAsync("evt-refund-1", "sub-mastery", "pkg_oet_mastery");
        var second = await processor.ReverseAsync("evt-refund-1", "sub-mastery", "pkg_oet_mastery");

        var subscription = await db.Subscriptions.SingleAsync();
        var ledger = Assert.Single(await db.AiCreditLedger.Where(entry => entry.Source == AiCreditSource.AdminAdjustment).ToListAsync());
        var balance = await new AiCreditService(db).GetBalanceAsync("user-mastery", default);
        Assert.True(first.Applied);
        Assert.True(second.DuplicateSkipped);
        Assert.Equal(0, subscription.AiCreditsRemaining);
        Assert.Equal(0, balance.TokensAvailable);
        Assert.Equal(AiCreditSource.AdminAdjustment, ledger.Source);
        Assert.Equal(-30, ledger.TokensDelta);
        Assert.Equal("addon-refund:sub-mastery:pkg_oet_mastery:evt-purchase-1", ledger.ReferenceId);
    }

    [Fact]
    public async Task ReverseAsync_BindsAiCreditReversalToOneUnreversedPurchaseReference()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new LearnerDbContext(options);
        var now = DateTimeOffset.UtcNow;
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-mastery",
            UserId = "user-mastery",
            PlanId = "plan-basic",
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddMonths(1),
            AiCreditsRemaining = 60,
        });
        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = "addon_pkg_oet_mastery",
            Code = "pkg_oet_mastery",
            Name = "OET Mastery",
            Status = BillingAddOnStatus.Active,
            GrantCredits = 30,
            GrantEntitlementsJson = "{\"ai_credits\":30}",
            DurationDays = 180,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AiCreditLedger.AddRange(
            new AiCreditLedgerEntry
            {
                Id = "ledger-purchase-mastery-1",
                UserId = "user-mastery",
                TokensDelta = 30,
                CostDeltaUsd = 0m,
                Source = AiCreditSource.Purchase,
                Description = "OET Mastery AI grading credits",
                ReferenceId = "addon:sub-mastery:pkg_oet_mastery:evt-purchase-1",
                CreatedAt = now,
            },
            new AiCreditLedgerEntry
            {
                Id = "ledger-purchase-mastery-2",
                UserId = "user-mastery",
                TokensDelta = 30,
                CostDeltaUsd = 0m,
                Source = AiCreditSource.Purchase,
                Description = "OET Mastery AI grading credits",
                ReferenceId = "addon:sub-mastery:pkg_oet_mastery:evt-purchase-2",
                CreatedAt = now.AddMinutes(1),
            });
        await db.SaveChangesAsync();

        var processor = new AddonGrantProcessor(db, NullLogger<AddonGrantProcessor>.Instance);
        var result = await processor.ReverseAsync("evt-refund-1", "sub-mastery", "pkg_oet_mastery");

        var ledger = Assert.Single(await db.AiCreditLedger.Where(entry => entry.Source == AiCreditSource.AdminAdjustment).ToListAsync());
        var balance = await new AiCreditService(db).GetBalanceAsync("user-mastery", default);
        var subscription = await db.Subscriptions.SingleAsync();
        Assert.True(result.Applied);
        Assert.Equal("addon-refund:sub-mastery:pkg_oet_mastery:evt-purchase-1", ledger.ReferenceId);
        Assert.Equal(30, balance.TokensAvailable);
        Assert.Equal(30, subscription.AiCreditsRemaining);
    }

    [Fact]
    public async Task ReverseAsync_UsesPurchaseLedgerAmount_WhenLiveAddOnGrantChanged()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new LearnerDbContext(options);
        var now = DateTimeOffset.UtcNow;
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-mastery",
            UserId = "user-mastery",
            PlanId = "plan-basic",
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddMonths(1),
            AiCreditsRemaining = 80,
        });
        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = "addon_pkg_oet_mastery",
            Code = "pkg_oet_mastery",
            Name = "OET Mastery",
            Status = BillingAddOnStatus.Active,
            GrantCredits = 80,
            GrantEntitlementsJson = "{\"ai_credits\":80}",
            DurationDays = 180,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AiCreditLedger.AddRange(
            new AiCreditLedgerEntry
            {
                Id = "ledger-purchase-mastery-old",
                UserId = "user-mastery",
                TokensDelta = 30,
                CostDeltaUsd = 0m,
                Source = AiCreditSource.Purchase,
                Description = "OET Mastery AI grading credits",
                ReferenceId = "addon:sub-mastery:pkg_oet_mastery:evt-purchase-old",
                CreatedAt = now,
            },
            new AiCreditLedgerEntry
            {
                Id = "ledger-purchase-other",
                UserId = "user-mastery",
                TokensDelta = 50,
                CostDeltaUsd = 0m,
                Source = AiCreditSource.Purchase,
                Description = "Other AI grading credits",
                ReferenceId = "addon:sub-other:pkg_other:evt-purchase-other",
                CreatedAt = now.AddMinutes(1),
            });
        await db.SaveChangesAsync();

        var processor = new AddonGrantProcessor(db, NullLogger<AddonGrantProcessor>.Instance);
        var result = await processor.ReverseAsync("evt-refund-old", "sub-mastery", "pkg_oet_mastery");

        var subscription = await db.Subscriptions.SingleAsync();
        var ledger = Assert.Single(await db.AiCreditLedger.Where(entry => entry.Source == AiCreditSource.AdminAdjustment).ToListAsync());
        var balance = await new AiCreditService(db).GetBalanceAsync("user-mastery", default);
        Assert.True(result.Applied);
        Assert.Equal(-30, ledger.TokensDelta);
        Assert.Equal("addon-refund:sub-mastery:pkg_oet_mastery:evt-purchase-old", ledger.ReferenceId);
        Assert.Equal(50, subscription.AiCreditsRemaining);
        Assert.Equal(50, balance.TokensAvailable);
    }

    [Fact]
    public async Task ReverseAsync_SkipsAlreadyReversedPurchase_AndReversesLaterMatchingPurchase()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new LearnerDbContext(options);
        var now = DateTimeOffset.UtcNow;
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-mastery",
            UserId = "user-mastery",
            PlanId = "plan-basic",
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddMonths(1),
            AiCreditsRemaining = 30,
        });
        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = "addon_pkg_oet_mastery",
            Code = "pkg_oet_mastery",
            Name = "OET Mastery",
            Status = BillingAddOnStatus.Active,
            GrantCredits = 30,
            GrantEntitlementsJson = "{\"ai_credits\":30}",
            DurationDays = 180,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AiCreditLedger.AddRange(
            new AiCreditLedgerEntry
            {
                Id = "ledger-purchase-mastery-1",
                UserId = "user-mastery",
                TokensDelta = 30,
                CostDeltaUsd = 0m,
                Source = AiCreditSource.Purchase,
                Description = "OET Mastery AI grading credits",
                ReferenceId = "addon:sub-mastery:pkg_oet_mastery:evt-purchase-1",
                CreatedAt = now,
            },
            new AiCreditLedgerEntry
            {
                Id = "ledger-refund-mastery-1",
                UserId = "user-mastery",
                TokensDelta = -30,
                CostDeltaUsd = 0m,
                Source = AiCreditSource.AdminAdjustment,
                Description = "Refund reversal for OET Mastery AI grading credits",
                ReferenceId = "addon-refund:sub-mastery:pkg_oet_mastery:evt-purchase-1",
                CreatedAt = now.AddMinutes(1),
            },
            new AiCreditLedgerEntry
            {
                Id = "ledger-purchase-other",
                UserId = "user-mastery",
                TokensDelta = 30,
                CostDeltaUsd = 0m,
                Source = AiCreditSource.Purchase,
                Description = "Later OET Mastery AI grading credits",
                ReferenceId = "addon:sub-mastery:pkg_oet_mastery:evt-purchase-2",
                CreatedAt = now.AddMinutes(2),
            });
        await db.SaveChangesAsync();

        var processor = new AddonGrantProcessor(db, NullLogger<AddonGrantProcessor>.Instance);
        var result = await processor.ReverseAsync("evt-refund-2", "sub-mastery", "pkg_oet_mastery");

        var subscription = await db.Subscriptions.SingleAsync();
        Assert.True(result.Applied);
        Assert.Null(result.Reason);
        Assert.Equal(0, subscription.AiCreditsRemaining);
        Assert.Equal(2, await db.AiCreditLedger.CountAsync(entry => entry.Source == AiCreditSource.AdminAdjustment));
        Assert.True(await db.AiCreditLedger.AnyAsync(entry =>
            entry.Source == AiCreditSource.AdminAdjustment &&
            entry.ReferenceId == "addon-refund:sub-mastery:pkg_oet_mastery:evt-purchase-2"));
        Assert.True(await db.IdempotencyRecords.AnyAsync(record => record.Scope == "addon_refund" && record.Key.Contains("evt-refund-2")));
    }

    [Fact]
    public async Task ReverseAsync_DoesNotMutateSubscription_WhenAiPurchaseLedgerIsMissing()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new LearnerDbContext(options);
        var now = DateTimeOffset.UtcNow;
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-mastery",
            UserId = "user-mastery",
            PlanId = "plan-basic",
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddMonths(1),
            AiCreditsRemaining = 30,
        });
        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = "addon_pkg_oet_mastery",
            Code = "pkg_oet_mastery",
            Name = "OET Mastery",
            Status = BillingAddOnStatus.Active,
            GrantCredits = 30,
            GrantEntitlementsJson = "{\"ai_credits\":30}",
            DurationDays = 180,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var processor = new AddonGrantProcessor(db, NullLogger<AddonGrantProcessor>.Instance);
        var result = await processor.ReverseAsync("evt-refund-missing", "sub-mastery", "pkg_oet_mastery");

        var subscription = await db.Subscriptions.SingleAsync();
        Assert.False(result.Applied);
        Assert.Equal("ai_credit_purchase_missing", result.Reason);
        Assert.Equal(30, subscription.AiCreditsRemaining);
        Assert.Empty(await db.AiCreditLedger.ToListAsync());
        Assert.False(await db.IdempotencyRecords.AnyAsync(record => record.Scope == "addon_refund" && record.Key.Contains("evt-refund-missing")));
    }

    [Fact]
    public async Task ReverseAsync_ReversesNonAiAddOnWithoutAiPurchaseLedger()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new LearnerDbContext(options);
        var now = DateTimeOffset.UtcNow;
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-writing-pack",
            UserId = "user-writing-pack",
            PlanId = "plan-basic",
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddMonths(1),
            WritingAssessmentsRemaining = 4,
            SpeakingSessionsRemaining = 2,
        });
        db.BillingAddOns.Add(new BillingAddOn
        {
            Id = "addon_pkg_writing_pack",
            Code = "pkg_writing_pack",
            Name = "Writing Pack",
            Status = BillingAddOnStatus.Active,
            LettersGranted = 2,
            SessionsGranted = 1,
            GrantCredits = 0,
            GrantEntitlementsJson = "{}",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var processor = new AddonGrantProcessor(db, NullLogger<AddonGrantProcessor>.Instance);
        var result = await processor.ReverseAsync("evt-refund-writing", "sub-writing-pack", "pkg_writing_pack");

        var subscription = await db.Subscriptions.SingleAsync();
        Assert.True(result.Applied);
        Assert.Equal(2, subscription.WritingAssessmentsRemaining);
        Assert.Equal(1, subscription.SpeakingSessionsRemaining);
        Assert.Empty(await db.AiCreditLedger.ToListAsync());
        Assert.True(await db.IdempotencyRecords.AnyAsync(record => record.Scope == "addon_refund" && record.Key.Contains("evt-refund-writing")));
    }
}