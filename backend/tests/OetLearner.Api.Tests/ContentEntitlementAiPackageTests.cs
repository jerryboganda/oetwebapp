using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Entitlements;
using Xunit;

namespace OetLearner.Api.Tests;

public class ContentEntitlementAiPackageTests
{
    [Fact]
    public async Task ListeningPaper_Allowed_WhenStandaloneAiPackageHasRemainingTests()
    {
        await using var db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        var now = DateTimeOffset.UtcNow;
        db.AiPackageCreditAccounts.Add(new AiPackageCreditAccount
        {
            Id = "acct-l",
            UserId = "user-ai",
            SharedCredits = 5,
            ListeningTestsRemaining = 3,
            ReadingTestsRemaining = 3,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddDays(30)
        });
        await db.SaveChangesAsync();

        var credits = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        var svc = new ContentEntitlementService(db, new EffectiveEntitlementResolver(db), credits);
        var paper = new ContentPaper
        {
            Id = "paper-l1",
            SubtestCode = "listening",
            Title = "Listening 1",
            Slug = "listening-1",
            TagsCsv = "access:premium"
        };

        var result = await svc.AllowAccessAsync("user-ai", paper, default);

        Assert.True(result.Allowed);
        Assert.Equal("ai_package_grants_subtest", result.Reason);
    }

    [Fact]
    public async Task ReadingPaper_Allowed_WhenStandaloneReadingProUnlimited()
    {
        await using var db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        var now = DateTimeOffset.UtcNow;
        var acct = new AiPackageCreditAccount
        {
            Id = "acct-r-pro",
            UserId = "user-reading-pro",
            SharedCredits = 0,
            ListeningTestsRemaining = 0,
            ReadingTestsRemaining = null, // unlimited sentinel
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddDays(180)
        };
        db.AiPackageCreditAccounts.Add(acct);
        await db.SaveChangesAsync();
        // Seed live unlimited Reading lot (the HasObjectivePracticeAllowance code checks lots,
        // not just the null sentinel — mirror the real Grant path that creates a lot).
        db.AiPackageCreditLots.Add(new AiPackageCreditLot
        {
            Id = "lot-r-pro",
            UserId = "user-reading-pro",
            AccountId = acct.Id,
            PackageId = "pkg_reading_pro",
            PackageType = "reading",
            ReadingTestsRemaining = null,
            UnlimitedReading = true,
            ExpiresAt = now.AddDays(180),
            CreatedAt = now,
        });
        await db.SaveChangesAsync();

        var credits = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        var svc = new ContentEntitlementService(db, new EffectiveEntitlementResolver(db), credits);
        var paper = new ContentPaper
        {
            Id = "paper-r1",
            SubtestCode = "reading",
            Title = "Reading 1",
            Slug = "reading-1",
            TagsCsv = "access:premium"
        };

        var result = await svc.AllowAccessAsync("user-reading-pro", paper, default);

        Assert.True(result.Allowed);
        Assert.Equal("ai_package_grants_subtest", result.Reason);
    }

    [Fact]
    public async Task ReadingPaper_Allowed_ViaSharedCreditsFallback()
    {
        await using var db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        var now = DateTimeOffset.UtcNow;
        db.AiPackageCreditAccounts.Add(new AiPackageCreditAccount
        {
            Id = "acct-shared",
            UserId = "user-shared",
            SharedCredits = 1,
            ListeningTestsRemaining = 0,
            ReadingTestsRemaining = 0,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddDays(30)
        });
        await db.SaveChangesAsync();

        var credits = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        var svc = new ContentEntitlementService(db, new EffectiveEntitlementResolver(db), credits);
        var paper = new ContentPaper
        {
            Id = "paper-r2",
            SubtestCode = "reading",
            Title = "Reading 2",
            Slug = "reading-2",
            TagsCsv = "access:premium"
        };

        var result = await svc.AllowAccessAsync("user-shared", paper, default);

        Assert.True(result.Allowed);
        Assert.Equal("ai_package_grants_subtest", result.Reason);
    }

    [Fact]
    public async Task ReadingPaper_Blocked_WhenAiPackageExpired()
    {
        await using var db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        var now = DateTimeOffset.UtcNow;
        db.AiPackageCreditAccounts.Add(new AiPackageCreditAccount
        {
            Id = "acct-exp",
            UserId = "user-exp",
            SharedCredits = 5,
            ListeningTestsRemaining = 3,
            ReadingTestsRemaining = 3,
            CreatedAt = now.AddDays(-40),
            UpdatedAt = now.AddDays(-40),
            ExpiresAt = now.AddDays(-1) // expired
        });
        await db.SaveChangesAsync();

        var credits = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        var svc = new ContentEntitlementService(db, new EffectiveEntitlementResolver(db), credits);
        var paper = new ContentPaper
        {
            Id = "paper-r3",
            SubtestCode = "reading",
            Title = "Reading 3",
            Slug = "reading-3",
            TagsCsv = "access:premium"
        };

        var result = await svc.AllowAccessAsync("user-exp", paper, default);

        Assert.False(result.Allowed);
        Assert.Equal("no_active_subscription", result.Reason);
    }

    [Fact]
    public async Task ListeningPaper_Blocked_WithoutCourseOrAiAllowance()
    {
        await using var db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        var credits = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        var svc = new ContentEntitlementService(db, new EffectiveEntitlementResolver(db), credits);
        var paper = new ContentPaper
        {
            Id = "paper-l2",
            SubtestCode = "listening",
            Title = "Listening 2",
            Slug = "listening-2",
            TagsCsv = "access:premium"
        };

        var result = await svc.AllowAccessAsync("user-free", paper, default);

        Assert.False(result.Allowed);
        Assert.Equal("no_active_subscription", result.Reason);
    }
}
