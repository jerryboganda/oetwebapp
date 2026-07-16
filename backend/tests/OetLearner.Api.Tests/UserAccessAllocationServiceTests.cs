using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests;

public class UserAccessAllocationServiceTests
{
    private static LearnerDbContext CreateDb()
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class NoopAddonProcessor : IAddonGrantProcessor
    {
        public Task<AddonGrantResult> ApplyAsync(string eventId, string subscriptionId, string addOnCode, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<AddonGrantResult> ReverseAsync(string eventId, string subscriptionId, string addOnCode, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private static UserAccessAllocationService CreateService(LearnerDbContext db)
        => new(db, new NoopAddonProcessor(), TimeProvider.System);

    private static async Task SeedLearnerAsync(LearnerDbContext db, string userId, string? authId = null)
    {
        db.Users.Add(new LearnerUser
        {
            Id = userId,
            AuthAccountId = authId,
            Role = ApplicationUserRoles.Learner,
            DisplayName = "Test",
            Email = $"{userId}@t.dev",
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            AccountStatus = "active",
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GrantPackage_CreatesSubscription_WithCustomExpiry_AndSetsPrimary()
    {
        await using var db = CreateDb();
        await SeedLearnerAsync(db, "learner-1");
        db.BillingPlans.Add(new BillingPlan { Id = "plan-med", Code = "med", Name = "Medicine", DurationMonths = 6, AccessDurationDays = 180 });
        await db.SaveChangesAsync();
        var customExpiry = DateTimeOffset.UtcNow.AddDays(90);

        var access = await CreateService(db).GrantPackageAsync("admin", "Admin", "learner-1",
            new AdminUserAccessPackageRequest("med", StartsAt: null, ExpiresAt: customExpiry,
                MakePrimary: true, GrantIncludedCredits: false, OverrideProfessionMismatch: false), default);

        Assert.Single(access.Subscriptions);
        var sub = access.Subscriptions[0];
        Assert.Equal("med", sub.PlanCode);
        Assert.True(sub.IsPrimary);
        Assert.Equal(customExpiry, sub.ExpiresAt);
        Assert.Equal("med", (await db.Users.FirstAsync(u => u.Id == "learner-1")).CurrentPlanId);
    }

    [Fact]
    public async Task GrantPackage_SamePlanTwice_IsIdempotent_NoDuplicateRow()
    {
        await using var db = CreateDb();
        await SeedLearnerAsync(db, "learner-2");
        db.BillingPlans.Add(new BillingPlan { Id = "plan-med", Code = "med", Name = "Medicine", DurationMonths = 6, AccessDurationDays = 180 });
        await db.SaveChangesAsync();
        var svc = CreateService(db);
        var req = new AdminUserAccessPackageRequest("med", StartsAt: null, ExpiresAt: null,
            MakePrimary: true, GrantIncludedCredits: false, OverrideProfessionMismatch: false);

        await svc.GrantPackageAsync("admin", "Admin", "learner-2", req, default);
        var access = await svc.GrantPackageAsync("admin", "Admin", "learner-2", req, default);

        Assert.Single(access.Subscriptions);
        Assert.Equal(1, await db.Subscriptions.CountAsync(s => s.UserId == "learner-2"));
    }

    [Fact]
    public async Task GrantPackage_TwoDifferentPlans_ProducesTwoSubscriptions()
    {
        await using var db = CreateDb();
        await SeedLearnerAsync(db, "learner-3");
        db.BillingPlans.AddRange(
            new BillingPlan { Id = "plan-med", Code = "med", Name = "Medicine", DurationMonths = 6, AccessDurationDays = 180 },
            new BillingPlan { Id = "plan-physio", Code = "physio", Name = "Physio", DurationMonths = 6, AccessDurationDays = 180 });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        await svc.GrantPackageAsync("admin", "Admin", "learner-3", new AdminUserAccessPackageRequest("med", null, null, true, false, false), default);
        var access = await svc.GrantPackageAsync("admin", "Admin", "learner-3", new AdminUserAccessPackageRequest("physio", null, null, false, false, false), default);

        Assert.Equal(2, access.Subscriptions.Count);
    }

    [Fact]
    public async Task PutScope_ReplacesModuleOverrides_AndSetsMasterExpiry()
    {
        await using var db = CreateDb();
        await SeedLearnerAsync(db, "learner-4");
        var expiry = DateTimeOffset.UtcNow.AddDays(30);

        var access = await CreateService(db).PutScopeAsync("admin", "Admin", "learner-4",
            new AdminUserAccessScopeRequest(
                Modules: new List<AdminModuleOverrideDto> { new("Mocks", false), new("VideoLibrary", true) },
                MaterialFolderIds: new List<string> { "mfd_a", "mfd_b" },
                RecallSetCodes: new List<string> { "2026" },
                AccessExpiresAt: expiry,
                ClearAccessExpiry: false), default);

        Assert.Equal(2, access.ModuleOverrides.Count);
        Assert.Contains(access.ModuleOverrides, m => m.ModuleKey == "Mocks" && !m.Enabled);
        Assert.Equal(2, access.MaterialFolderIds.Count);
        Assert.Equal(new[] { "2026" }, access.RecallSetCodes);
        Assert.Equal(expiry, access.AccessExpiresAt);
    }

    [Fact]
    public async Task PutScope_PastExpiry_RevokesActiveRefreshTokens()
    {
        await using var db = CreateDb();
        await SeedLearnerAsync(db, "learner-5", authId: "auth-5");
        var tokenId = Guid.NewGuid();
        db.RefreshTokenRecords.Add(new RefreshTokenRecord
        {
            Id = tokenId,
            ApplicationUserAccountId = "auth-5",
            TokenHash = "hash",
            FamilyId = Guid.NewGuid(),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            RevokedAt = null,
        });
        await db.SaveChangesAsync();

        await CreateService(db).PutScopeAsync("admin", "Admin", "learner-5",
            new AdminUserAccessScopeRequest(null, null, null,
                AccessExpiresAt: DateTimeOffset.UtcNow.AddDays(-1), ClearAccessExpiry: false), default);

        var token = await db.RefreshTokenRecords.FirstAsync(t => t.Id == tokenId);
        Assert.NotNull(token.RevokedAt);
    }
}
