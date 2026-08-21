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
            FlexibleCredits = 5,
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
        Assert.Equal("ai_package_grants", result.Reason);
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
