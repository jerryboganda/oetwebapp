using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Tests;

public class LearnerEntitlementResolverTests
{
    private static DbContextOptions<LearnerDbContext> NewInMemoryOptions()
        => new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    [Fact]
    public async Task ResolveAsync_Anonymous_ReturnsAnonymousTier()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var resolver = new LearnerEntitlementResolver(db);

        var result = await resolver.ResolveAsync(null, LearnerEntitlementResources.Grammar, default);

        Assert.Equal(LearnerEntitlementTier.Anonymous, result.Tier);
        Assert.True(result.IsAnonymous);
        Assert.Contains(LearnerEntitlementSource.Anonymous, result.Sources);
        Assert.False(result.HasPaidOrSponsoredAccess);
    }

    [Fact]
    public async Task ResolveAsync_NoSubscription_ReturnsFreeTier()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var resolver = new LearnerEntitlementResolver(db);

        var result = await resolver.ResolveAsync("learner-free", LearnerEntitlementResources.Grammar, default);

        Assert.Equal(LearnerEntitlementTier.Free, result.Tier);
        Assert.Contains(LearnerEntitlementSource.Free, result.Sources);
        Assert.False(result.HasPaidOrSponsoredAccess);
    }

    [Fact]
    public async Task ResolveAsync_DirectActiveSubscription_ReturnsPaidTier()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        db.Subscriptions.Add(Subscription("sub-active", "learner-paid", SubscriptionStatus.Active));
        await db.SaveChangesAsync();
        var resolver = new LearnerEntitlementResolver(db);

        var result = await resolver.ResolveAsync("learner-paid", LearnerEntitlementResources.Grammar, default);

        Assert.Equal(LearnerEntitlementTier.Paid, result.Tier);
        Assert.True(result.HasDirectActiveSubscription);
        Assert.True(result.HasPaidOrSponsoredAccess);
        Assert.Contains(LearnerEntitlementSource.DirectActiveSubscription, result.Sources);
    }

    [Fact]
    public async Task ResolveAsync_DirectTrialSubscription_ReturnsTrialTier()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        db.Subscriptions.Add(Subscription("sub-trial", "learner-trial", SubscriptionStatus.Trial));
        await db.SaveChangesAsync();
        var resolver = new LearnerEntitlementResolver(db);

        var result = await resolver.ResolveAsync("learner-trial", LearnerEntitlementResources.Grammar, default);

        Assert.Equal(LearnerEntitlementTier.Trial, result.Tier);
        Assert.True(result.HasDirectTrialSubscription);
        Assert.True(result.HasPaidOrSponsoredAccess);
        Assert.Contains(LearnerEntitlementSource.DirectTrialSubscription, result.Sources);
    }

    [Fact]
    public async Task ResolveAsync_ActiveAddOnAndCurrentFreeze_IncludeEvidence()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var now = DateTimeOffset.UtcNow;
        db.Subscriptions.Add(Subscription("sub-addon", "learner-addon", SubscriptionStatus.Active));
        db.SubscriptionItems.Add(new SubscriptionItem
        {
            Id = "sub-item-addon",
            SubscriptionId = "sub-addon",
            ItemCode = "priority-review",
            ItemType = "addon",
            Status = SubscriptionItemStatus.Active,
            StartsAt = now.AddDays(-1),
            CreatedAt = now.AddDays(-1),
            UpdatedAt = now.AddDays(-1)
        });
        db.AccountFreezeRecords.Add(new AccountFreezeRecord
        {
            Id = "freeze-active",
            UserId = "learner-addon",
            Status = FreezeStatus.Active,
            IsCurrent = true,
            RequestedAt = now.AddDays(-2),
            StartedAt = now.AddDays(-1),
            DurationDays = 7,
            UpdatedAt = now.AddDays(-1)
        });
        await db.SaveChangesAsync();
        var resolver = new LearnerEntitlementResolver(db);

        var result = await resolver.ResolveAsync("learner-addon", LearnerEntitlementResources.Grammar, default);

        Assert.True(result.HasActiveAddOn);
        Assert.True(result.HasCurrentFreeze);
        Assert.Contains(LearnerEntitlementSource.ActiveAddOn, result.Sources);
        Assert.Contains(LearnerEntitlementSource.CurrentFreeze, result.Sources);
        Assert.Contains(result.ActiveAddOns, addOn => addOn.ItemCode == "priority-review");
        Assert.Equal("freeze-active", result.CurrentFreeze?.Id);
    }

    [Fact]
    public async Task ResolveAsync_ActiveSponsorship_UnlocksSponsorSeat()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        db.Sponsorships.Add(new Sponsorship
        {
            Id = Guid.NewGuid(),
            SponsorUserId = "sponsor-user",
            LearnerUserId = "learner-sponsored",
            LearnerEmail = "learner@example.test",
            Status = "Active",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();
        var resolver = new LearnerEntitlementResolver(db);

        var result = await resolver.ResolveAsync("learner-sponsored", LearnerEntitlementResources.Grammar, default);

        Assert.Equal(LearnerEntitlementTier.SponsorSeat, result.Tier);
        Assert.True(result.HasSponsorSeat);
        Assert.True(result.HasPaidOrSponsoredAccess);
        Assert.Equal(LearnerEntitlementSource.ActiveSponsorship, result.SponsorSeat?.Source);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Revoked")]
    public async Task ResolveAsync_PendingOrRevokedSponsorship_DoesNotUnlockSponsorSeat(string status)
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        db.Sponsorships.Add(new Sponsorship
        {
            Id = Guid.NewGuid(),
            SponsorUserId = "sponsor-user",
            LearnerUserId = "learner-not-sponsored",
            LearnerEmail = "learner@example.test",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();
        var resolver = new LearnerEntitlementResolver(db);

        var result = await resolver.ResolveAsync("learner-not-sponsored", LearnerEntitlementResources.Grammar, default);

        Assert.Equal(LearnerEntitlementTier.Free, result.Tier);
        Assert.False(result.HasSponsorSeat);
        Assert.False(result.HasPaidOrSponsoredAccess);
    }

    [Fact]
    public async Task ResolveAsync_ConsentedLinkWithActiveSponsorAccount_UnlocksSponsorSeat()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var now = DateTimeOffset.UtcNow;
        db.SponsorAccounts.Add(SponsorAccount("sponsor-account", "active"));
        db.SponsorLearnerLinks.Add(new SponsorLearnerLink
        {
            Id = Guid.NewGuid(),
            SponsorId = "sponsor-account",
            LearnerId = "learner-linked",
            LearnerConsented = true,
            LinkedAt = now.AddDays(-2),
            ConsentedAt = now.AddDays(-1)
        });
        await db.SaveChangesAsync();
        var resolver = new LearnerEntitlementResolver(db);

        var result = await resolver.ResolveAsync("learner-linked", LearnerEntitlementResources.Grammar, default);

        Assert.Equal(LearnerEntitlementTier.SponsorSeat, result.Tier);
        Assert.True(result.HasSponsorSeat);
        Assert.Equal(LearnerEntitlementSource.ConsentedSponsorLink, result.SponsorSeat?.Source);
    }

    [Fact]
    public async Task ResolveAsync_UnconsentedLink_DoesNotUnlockSponsorSeat()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        db.SponsorAccounts.Add(SponsorAccount("sponsor-account", "active"));
        db.SponsorLearnerLinks.Add(new SponsorLearnerLink
        {
            Id = Guid.NewGuid(),
            SponsorId = "sponsor-account",
            LearnerId = "learner-unconsented",
            LearnerConsented = false,
            LinkedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();
        var resolver = new LearnerEntitlementResolver(db);

        var result = await resolver.ResolveAsync("learner-unconsented", LearnerEntitlementResources.Grammar, default);

        Assert.Equal(LearnerEntitlementTier.Free, result.Tier);
        Assert.False(result.HasSponsorSeat);
    }

    [Fact]
    public async Task ResolveAsync_ConsentedLinkWithInactiveSponsorAccount_DoesNotUnlockSponsorSeat()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        db.SponsorAccounts.Add(SponsorAccount("sponsor-account", "inactive"));
        db.SponsorLearnerLinks.Add(new SponsorLearnerLink
        {
            Id = Guid.NewGuid(),
            SponsorId = "sponsor-account",
            LearnerId = "learner-inactive-sponsor",
            LearnerConsented = true,
            LinkedAt = DateTimeOffset.UtcNow.AddDays(-2),
            ConsentedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();
        var resolver = new LearnerEntitlementResolver(db);

        var result = await resolver.ResolveAsync("learner-inactive-sponsor", LearnerEntitlementResources.Grammar, default);

        Assert.Equal(LearnerEntitlementTier.Free, result.Tier);
        Assert.False(result.HasSponsorSeat);
    }

    private static Subscription Subscription(string id, string userId, SubscriptionStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        return new Subscription
        {
            Id = id,
            UserId = userId,
            PlanId = "premium-monthly",
            Status = status,
            StartedAt = now.AddDays(-10),
            ChangedAt = now.AddDays(-10),
            NextRenewalAt = now.AddDays(20)
        };
    }

    private static SponsorAccount SponsorAccount(string id, string status)
        => new()
        {
            Id = id,
            AuthAccountId = $"auth-{id}",
            Name = "Sponsor Account",
            Type = "institution",
            ContactEmail = $"{id}@example.test",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };
}