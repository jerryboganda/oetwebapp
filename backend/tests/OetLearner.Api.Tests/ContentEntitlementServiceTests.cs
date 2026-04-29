using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Tests;

public class ContentEntitlementServiceTests
{
    [Fact]
    public async Task AllowAccessAsync_FreePaper_AllowsAnonymousEquivalentCaller()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var service = BuildService(db);
        var paper = Paper(tagsCsv: "access:free");

        var result = await service.AllowAccessAsync(null, paper, default);

        Assert.True(result.Allowed);
        Assert.Equal("free_paper", result.Reason);
        Assert.Equal("free", result.CurrentTier);
    }

    [Fact]
    public async Task AllowAccessAsync_AnonymousPremiumPaper_Denies()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var service = BuildService(db);

        var result = await service.AllowAccessAsync(null, Paper(), default);

        Assert.False(result.Allowed);
        Assert.Equal("no_active_subscription", result.Reason);
        Assert.Null(result.CurrentTier);
        Assert.Equal("subtest:reading", result.RequiredScope);
    }

    [Fact]
    public async Task AllowAccessAsync_ActiveSubscriptionWithPlanGrant_Allows()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var learnerId = "learner-plan-grant";
        var paper = Paper();
        db.BillingPlans.Add(Plan("reading-plan", "{\"content\":{\"tier\":\"free\",\"subtests\":[\"reading\"],\"papers\":[]}}"));
        db.Subscriptions.Add(Subscription("sub-plan-grant", learnerId, "reading-plan", SubscriptionStatus.Active));
        await db.SaveChangesAsync();
        var service = BuildService(db);

        var result = await service.AllowAccessAsync(learnerId, paper, default);

        Assert.True(result.Allowed);
        Assert.Equal("plan_grants_subtest", result.Reason);
        Assert.Equal("free", result.CurrentTier);
    }

    [Fact]
    public async Task AllowAccessAsync_ActiveSubscriptionWithoutMatchingPlanGrant_Denies()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var learnerId = "learner-plan-denied";
        db.BillingPlans.Add(Plan("listening-plan", "{\"content\":{\"tier\":\"free\",\"subtests\":[\"listening\"],\"papers\":[]}}"));
        db.Subscriptions.Add(Subscription("sub-plan-denied", learnerId, "listening-plan", SubscriptionStatus.Active));
        await db.SaveChangesAsync();
        var service = BuildService(db);

        var result = await service.AllowAccessAsync(learnerId, Paper(), default);

        Assert.False(result.Allowed);
        Assert.Equal("plan_does_not_grant", result.Reason);
        Assert.Equal("free", result.CurrentTier);
        Assert.Equal("subtest:reading", result.RequiredScope);
    }

    [Fact]
    public async Task AllowAccessAsync_ActiveSponsorship_AllowsPremiumPaperAsSponsorSeat()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var learnerId = "learner-sponsored";
        db.Sponsorships.Add(new Sponsorship
        {
            Id = Guid.NewGuid(),
            SponsorUserId = "sponsor-user",
            LearnerUserId = learnerId,
            LearnerEmail = "learner-sponsored@example.test",
            Status = "Active",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();
        var service = BuildService(db);

        var result = await service.AllowAccessAsync(learnerId, Paper(), default);

        Assert.True(result.Allowed);
        Assert.Equal("sponsor_seat", result.Reason);
        Assert.Equal("sponsor", result.CurrentTier);
    }

    [Fact]
    public async Task AllowAccessAsync_ConsentedSponsorLink_AllowsPremiumPaperAsSponsorSeat()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var learnerId = "learner-linked";
        db.SponsorAccounts.Add(SponsorAccount("sponsor-account", "active"));
        db.SponsorLearnerLinks.Add(new SponsorLearnerLink
        {
            Id = Guid.NewGuid(),
            SponsorId = "sponsor-account",
            LearnerId = learnerId,
            LearnerConsented = true,
            LinkedAt = DateTimeOffset.UtcNow.AddDays(-2),
            ConsentedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();
        var service = BuildService(db);

        var result = await service.AllowAccessAsync(learnerId, Paper(), default);

        Assert.True(result.Allowed);
        Assert.Equal("sponsor_seat", result.Reason);
        Assert.Equal("sponsor", result.CurrentTier);
    }

    [Theory]
    [InlineData("unconsented_link")]
    [InlineData("inactive_sponsor_account")]
    [InlineData("revoked_sponsorship")]
    public async Task AllowAccessAsync_InvalidSponsorEvidence_DeniesPremiumPaper(string evidenceCase)
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var learnerId = $"learner-{evidenceCase}";
        SeedInvalidSponsorEvidence(db, learnerId, evidenceCase);
        await db.SaveChangesAsync();
        var service = BuildService(db);

        var result = await service.AllowAccessAsync(learnerId, Paper(), default);

        Assert.False(result.Allowed);
        Assert.Equal("no_active_subscription", result.Reason);
        Assert.Equal("free", result.CurrentTier);
        Assert.Equal("subtest:reading", result.RequiredScope);
    }

    private static DbContextOptions<LearnerDbContext> NewInMemoryOptions()
        => new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static ContentEntitlementService BuildService(LearnerDbContext db)
        => new(db, new LearnerEntitlementResolver(db));

    private static ContentPaper Paper(string id = "paper-reading", string? tagsCsv = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new ContentPaper
        {
            Id = id,
            SubtestCode = "reading",
            Title = "Reading entitlement paper",
            Slug = $"reading-entitlement-{Guid.NewGuid():N}",
            AppliesToAllProfessions = true,
            Difficulty = "standard",
            EstimatedDurationMinutes = 60,
            Status = ContentStatus.Published,
            SourceProvenance = ContentDefaults.DefaultSourceProvenance,
            TagsCsv = tagsCsv ?? string.Empty,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now
        };
    }

    private static BillingPlan Plan(string id, string entitlementsJson)
        => new()
        {
            Id = id,
            Code = id,
            Name = id,
            Description = id,
            Price = 49.99m,
            Currency = "AUD",
            Interval = "monthly",
            DurationMonths = 1,
            IncludedSubtestsJson = "[]",
            EntitlementsJson = entitlementsJson
        };

    private static Subscription Subscription(string id, string userId, string planId, SubscriptionStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        return new Subscription
        {
            Id = id,
            UserId = userId,
            PlanId = planId,
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

    private static void SeedInvalidSponsorEvidence(LearnerDbContext db, string learnerId, string evidenceCase)
    {
        switch (evidenceCase)
        {
            case "unconsented_link":
                db.SponsorAccounts.Add(SponsorAccount("sponsor-unconsented", "active"));
                db.SponsorLearnerLinks.Add(new SponsorLearnerLink
                {
                    Id = Guid.NewGuid(),
                    SponsorId = "sponsor-unconsented",
                    LearnerId = learnerId,
                    LearnerConsented = false,
                    LinkedAt = DateTimeOffset.UtcNow.AddDays(-1)
                });
                break;
            case "inactive_sponsor_account":
                db.SponsorAccounts.Add(SponsorAccount("sponsor-inactive", "inactive"));
                db.SponsorLearnerLinks.Add(new SponsorLearnerLink
                {
                    Id = Guid.NewGuid(),
                    SponsorId = "sponsor-inactive",
                    LearnerId = learnerId,
                    LearnerConsented = true,
                    LinkedAt = DateTimeOffset.UtcNow.AddDays(-2),
                    ConsentedAt = DateTimeOffset.UtcNow.AddDays(-1)
                });
                break;
            case "revoked_sponsorship":
                db.Sponsorships.Add(new Sponsorship
                {
                    Id = Guid.NewGuid(),
                    SponsorUserId = "sponsor-revoked",
                    LearnerUserId = learnerId,
                    LearnerEmail = $"{learnerId}@example.test",
                    Status = "Revoked",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
                    RevokedAt = DateTimeOffset.UtcNow.AddDays(-1)
                });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(evidenceCase), evidenceCase, null);
        }
    }
}