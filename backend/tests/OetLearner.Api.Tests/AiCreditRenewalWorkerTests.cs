using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;

namespace OetLearner.Api.Tests;

/// <summary>
/// Regression: documents and verifies that <see cref="AiCreditRenewalWorker"/>
/// renews AI credits ONLY for direct, paid, ACTIVE subscriptions. Trial,
/// cancelled, past-due, and sponsor-only learners are intentionally excluded —
/// see the divergence comment in the worker.
/// </summary>
public sealed class AiCreditRenewalWorkerTests
{
    [Fact]
    public async Task ActivePaidSubscription_Renews()
    {
        var (services, db) = BuildScope();
        SeedPlan(db);
        db.Subscriptions.Add(NewSubscription("sub-active", "learner-active", SubscriptionStatus.Active));
        await db.SaveChangesAsync();

        var worker = new AiCreditRenewalWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AiCreditRenewalWorker>.Instance);
        var (renewed, _) = await worker.RunOnceAsync(default);

        Assert.Equal(1, renewed);
        Assert.Single(db.AiCreditLedger, e => e.UserId == "learner-active" && e.Source == AiCreditSource.PlanRenewal);
    }

    [Fact]
    public async Task TrialSubscription_DoesNotRenew()
    {
        var (services, db) = BuildScope();
        SeedPlan(db);
        db.Subscriptions.Add(NewSubscription("sub-trial", "learner-trial", SubscriptionStatus.Trial));
        await db.SaveChangesAsync();

        var worker = new AiCreditRenewalWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AiCreditRenewalWorker>.Instance);
        var (renewed, _) = await worker.RunOnceAsync(default);

        Assert.Equal(0, renewed);
        Assert.Empty(db.AiCreditLedger.Where(e => e.UserId == "learner-trial"));
    }

    [Fact]
    public async Task CancelledOrPastDueSubscription_DoesNotRenew()
    {
        var (services, db) = BuildScope();
        SeedPlan(db);
        db.Subscriptions.Add(NewSubscription("sub-cancelled", "learner-cancelled", SubscriptionStatus.Cancelled));
        db.Subscriptions.Add(NewSubscription("sub-pastdue", "learner-pastdue", SubscriptionStatus.PastDue));
        await db.SaveChangesAsync();

        var worker = new AiCreditRenewalWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AiCreditRenewalWorker>.Instance);
        var (renewed, _) = await worker.RunOnceAsync(default);

        Assert.Equal(0, renewed);
        Assert.Empty(db.AiCreditLedger);
    }

    [Fact]
    public async Task SponsorOnlyLearner_DoesNotRenew()
    {
        var (services, db) = BuildScope();
        SeedPlan(db);
        // No direct subscription; sponsorship grants seat-based access but
        // does NOT trigger monthly AI credit renewals here.
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

        var worker = new AiCreditRenewalWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AiCreditRenewalWorker>.Instance);
        var (renewed, _) = await worker.RunOnceAsync(default);

        Assert.Equal(0, renewed);
        Assert.Empty(db.AiCreditLedger);
    }

    [Fact]
    public async Task RunningTwice_InSamePeriod_IsIdempotent()
    {
        var (services, db) = BuildScope();
        SeedPlan(db);
        db.Subscriptions.Add(NewSubscription("sub-active-2", "learner-active-2", SubscriptionStatus.Active));
        await db.SaveChangesAsync();

        var worker = new AiCreditRenewalWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AiCreditRenewalWorker>.Instance);
        var first = await worker.RunOnceAsync(default);
        var second = await worker.RunOnceAsync(default);

        Assert.Equal(1, first.renewed);
        Assert.Equal(0, second.renewed);
        Assert.Single(db.AiCreditLedger, e => e.UserId == "learner-active-2" && e.Source == AiCreditSource.PlanRenewal);
    }

    private static (IServiceProvider Services, LearnerDbContext Db) BuildScope()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<LearnerDbContext>(opts => opts.UseInMemoryDatabase(dbName));
        services.AddScoped<IAiCreditService, AiCreditService>();
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        return (provider, db);
    }

    private static void SeedPlan(LearnerDbContext db)
    {
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "billing-premium",
            Code = "premium",
            Name = "Premium",
            Price = 49m,
            Currency = "USD",
            Interval = "month"
        });
        db.AiQuotaPlans.Add(new AiQuotaPlan
        {
            Id = "ai-premium",
            Code = "premium",
            Name = "Premium AI",
            Period = AiQuotaPeriod.Monthly,
            MonthlyTokenCap = 100_000,
            IsActive = true,
            RolloverPolicy = AiQuotaRolloverPolicy.Expire,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private static Subscription NewSubscription(string id, string userId, SubscriptionStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        return new Subscription
        {
            Id = id,
            UserId = userId,
            PlanId = "billing-premium",
            Status = status,
            StartedAt = now.AddDays(-10),
            ChangedAt = now.AddDays(-10),
            NextRenewalAt = now.AddDays(20)
        };
    }
}
