using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;

namespace OetLearner.Api.Tests.Billing;

public class AffiliatePortalEndpointsTests
{
    [Fact]
    public async Task SelfStatsFindTheAffiliateBehindALearnerToken()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(nameof(SelfStatsFindTheAffiliateBehindALearnerToken))
            .Options;
        await using var db = new LearnerDbContext(options);
        var now = DateTimeOffset.UtcNow;
        // Registration's shape: separate ids, linked by LearnerUser.AuthAccountId.
        db.ApplicationUserAccounts.Add(new ApplicationUserAccount
        {
            Id = "auth_1", Email = "Agent@Example.test", NormalizedEmail = "AGENT@EXAMPLE.TEST", PasswordHash = "x", CreatedAt = now,
        });
        db.Users.Add(new LearnerUser { Id = "learner_1", AuthAccountId = "auth_1", DisplayName = "Agent", Email = "agent@example.test", CreatedAt = now, LastActiveAt = now });
        db.Affiliates.Add(new Affiliate
        {
            Id = "a1", Code = "AGENT01", OwnerName = "Agent", ContactEmail = "agent@example.test", CommissionPercent = 20m,
            PayoutThresholdAmount = 100m, Status = "active", CreatedAt = now, UpdatedAt = now,
        });
        db.AffiliateAttributions.Add(new AffiliateAttribution { Id = "att1", UserId = "learner_2", AffiliateId = "a1", ClickedAt = now, AttributedAt = now });
        await db.SaveChangesAsync();

        // The JWT's NameIdentifier for a learner is the learner id, never the account id.
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "learner_1")], "test")),
        };
        var method = typeof(AffiliatePortalEndpoints).GetMethod("GetSelfStats", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = await (Task<Results<Ok<AffiliateStatsResponse>, NotFound>>)method.Invoke(null, [http, db, CancellationToken.None])!;

        var ok = Assert.IsType<Ok<AffiliateStatsResponse>>(result.Result);
        Assert.Equal("AGENT01", ok.Value!.AffiliateCode);
        Assert.Equal(1, ok.Value.TotalSignups);
    }
}
