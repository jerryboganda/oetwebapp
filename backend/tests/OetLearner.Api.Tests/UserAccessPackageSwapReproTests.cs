using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Tests;

/// <summary>
/// Scratch reproduction for the reported "Advanced (full manual control)" bug:
/// admin swaps a learner's package (grants a new one, removes the old one) and the
/// override does not take effect correctly. Mirrors the package transition sequence
/// used by the admin access editor.
/// </summary>
public class UserAccessPackageSwapReproTests
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

    [Fact]
    public async Task SwapPackage_GrantNewThenRemoveOld_ResolverReflectsNewPackage()
    {
        await using var db = CreateDb();
        db.Users.Add(new LearnerUser
        {
            Id = "learner-swap",
            Role = ApplicationUserRoles.Learner,
            DisplayName = "Test",
            Email = "learner-swap@t.dev",
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            AccountStatus = "active",
        });
        // Realistic full-course plans: non-empty DashboardModulesJson, like real seeded plans.
        db.BillingPlans.AddRange(
            new BillingPlan
            {
                Id = "plan-med", Code = "med", Name = "Medicine", DurationMonths = 6, AccessDurationDays = 180,
                DashboardModulesJson = "[\"Recalls\",\"MaterialsLibrary\",\"VideoLibrary\",\"Mocks\"]",
            },
            new BillingPlan
            {
                Id = "plan-physio", Code = "physio", Name = "Physio", DurationMonths = 6, AccessDurationDays = 180,
                DashboardModulesJson = "[\"Recalls\",\"MaterialsLibrary\",\"VideoLibrary\",\"Mocks\"]",
            });
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        // Admin grants the original package A ("med"), primary.
        await svc.GrantPackageAsync("admin", "Admin", "learner-swap",
            new AdminUserAccessPackageRequest("med", null, null, MakePrimary: true, GrantIncludedCredits: false, OverrideProfessionMismatch: false),
            default);

        var beforeSwap = await svc.GetAccessAsync("learner-swap", default);
        var packageAId = beforeSwap.Subscriptions.Single().Id;

        // Admin "changes the subscription package" via the Advanced panel: picks a new
        // plan B ("physio") and deletes the old row A in the UI, then clicks Save.
        // The service remains safe even when callers grant the replacement before removing
        // the old package.
        await svc.GrantPackageAsync("admin", "Admin", "learner-swap",
            new AdminUserAccessPackageRequest("physio", null, null, MakePrimary: false, GrantIncludedCredits: false, OverrideProfessionMismatch: false),
            default);
        var beforePrimaryChange = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-swap", default);
        Assert.Equal("med", beforePrimaryChange.PlanCode);
        var physioId = (await svc.GetAccessAsync("learner-swap", default)).Subscriptions.Single(s => s.PlanCode == "physio").Id;
        await svc.SetPrimaryPackageAsync("admin", "Admin", "learner-swap", physioId, default);
        var afterPrimaryChange = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-swap", default);
        Assert.Equal("physio", afterPrimaryChange.PlanCode);
        Assert.True(afterPrimaryChange.HasEligibleSubscription);
        await svc.RemovePackageAsync("admin", "Admin", "learner-swap", packageAId, default);

        var afterSwap = await svc.GetAccessAsync("learner-swap", default);
        var learner = await db.Users.AsNoTracking().FirstAsync(u => u.Id == "learner-swap");

        // ── What the admin sees when the panel re-fetches after Save ──
        Assert.Single(afterSwap.Subscriptions);
        var physio = afterSwap.Subscriptions.Single(s => s.PlanCode == "physio");
        Assert.Equal("Active", physio.Status);
        Assert.DoesNotContain(afterSwap.Subscriptions, s => s.PlanCode == "med");
        Assert.Equal(SubscriptionStatus.Cancelled,
            (await db.Subscriptions.SingleAsync(s => s.Id == packageAId)).Status);
        Assert.Contains(await db.AuditEvents.ToListAsync(),
            audit => audit.Action == "Package Removed" && audit.ResourceId == packageAId);

        // ── What actually gates the learner's platform access ──
        var snapshot = await new EffectiveEntitlementResolver(db).ResolveAsync("learner-swap", default);
        Assert.True(snapshot.HasEligibleSubscription);
        Assert.Equal("physio", snapshot.PlanCode);
        Assert.Contains("MaterialsLibrary", snapshot.EnabledModules);

        // ── Did CurrentPlanId end up pointing at the new package? ──
        Assert.Equal("physio", learner.CurrentPlanId);

        // ── What the admin's "Subscription" summary card (top of the user detail
        // page, sourced from AdminService.GetUserDetailAsync, NOT the Access &
        // Allocation panel) shows after the swap ──
        var detail = await NewAdminService(db).GetUserDetailAsync("learner-swap", default);
        var summary = GetProp(detail, "subscription");
        Assert.NotNull(summary);
        Assert.Equal("physio", GetProp(summary!, "planCode"));
        Assert.Equal("active", GetProp(summary!, "status"));
    }

    private static object? GetProp(object source, string name)
        => source.GetType().GetProperty(name)?.GetValue(source);

    private static AdminService NewAdminService(LearnerDbContext db)
        => new(
            db,
            emailOtpService: null!,
            passwordHasher: null!,
            passwordPolicyService: null!,
            timeProvider: TimeProvider.System,
            notifications: null!,
            learnerService: null!);
}
