using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// Spec: Recalls "locked_until_enrolled". Learner-facing CONTENT routes
/// (today/queue/library/report/revision-plan/etc.) must return 402
/// <c>enrolment_required</c> until the learner is enrolled (active, non-frozen
/// subscription). Mirrors <see cref="RecallsAudioEntitlementTests"/>.
/// </summary>
public class RecallsContentEntitlementTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    [Theory]
    [InlineData("/v1/recalls/library")]
    [InlineData("/v1/recalls/today")]
    [InlineData("/v1/recalls/queue?limit=10")]
    public async Task Content_returns_402_for_learner_without_active_subscription(string route)
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: false);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("enrolment_required", payload, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/v1/recalls/library")]
    [InlineData("/v1/recalls/today")]
    public async Task Content_returns_402_for_frozen_active_subscriber(string route)
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: true, isFrozen: true);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("enrolment_required", payload, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/v1/recalls/library")]
    [InlineData("/v1/recalls/today")]
    public async Task Content_passes_enrolment_gate_for_active_subscriber(string route)
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: true);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync(route);

        // Gate cleared: any non-402 is acceptable here (expected 200 OK).
        Assert.NotEqual(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Theory]
    [InlineData("/v1/recalls/library")]
    [InlineData("/v1/recalls/today")]
    public async Task Content_bypasses_enrolment_gate_for_admin(string route)
    {
        // Admins bypass the enrolment gate (mirrors the audio endpoint). The
        // contract is "never 402 enrolment_required" for an admin principal.
        var adminId = $"admin-{Guid.NewGuid():N}";
        await SeedLearnerAsync(adminId, hasActiveSubscription: false);

        using var client = CreateAdminClient(adminId);
        var response = await client.GetAsync(route);

        Assert.NotEqual(HttpStatusCode.PaymentRequired, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("enrolment_required", payload, StringComparison.Ordinal);
    }

    private HttpClient CreateLearnerClient(string learnerId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", learnerId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{learnerId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", learnerId);
        client.DefaultRequestHeaders.Add("X-Debug-Profession", "medicine");
        return client;
    }

    private HttpClient CreateAdminClient(string adminId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", adminId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{adminId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", adminId);
        client.DefaultRequestHeaders.Add("X-Debug-Profession", "medicine");
        return client;
    }

    private async Task SeedLearnerAsync(string learnerId, bool hasActiveSubscription, bool isFrozen = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;

        db.Users.Add(new LearnerUser
        {
            Id = learnerId,
            DisplayName = learnerId,
            Email = $"{learnerId}@example.test",
            ActiveProfessionId = "medicine",
            AccountStatus = "active",
            Timezone = "UTC",
            Locale = "en-AU",
            CreatedAt = now,
            LastActiveAt = now
        });

        if (hasActiveSubscription)
        {
            const string planCode = "premium-monthly-recalls-content-test";
            if (!db.BillingPlans.Any(plan => plan.Id == planCode || plan.Code == planCode))
            {
                db.BillingPlans.Add(new BillingPlan
                {
                    Id = planCode,
                    Code = planCode,
                    Name = "Premium monthly (recalls content test)",
                    EntitlementsJson = "{}",
                    IncludedSubtestsJson = "[]",
                    Status = BillingPlanStatus.Active,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            db.Subscriptions.Add(new Subscription
            {
                Id = $"sub-{Guid.NewGuid():N}",
                UserId = learnerId,
                PlanId = planCode,
                Status = SubscriptionStatus.Active,
                StartedAt = now,
                ChangedAt = now,
                NextRenewalAt = now.AddDays(30),
                PriceAmount = 49.99m,
                Currency = "AUD",
                Interval = "monthly"
            });
        }

        if (isFrozen)
        {
            db.AccountFreezeRecords.Add(new AccountFreezeRecord
            {
                Id = $"freeze-{Guid.NewGuid():N}",
                UserId = learnerId,
                Status = FreezeStatus.Active,
                IsCurrent = true,
                RequestedAt = now,
                StartedAt = now,
                DurationDays = 30,
                Reason = "Test freeze",
                PolicySnapshotJson = "{}",
                EligibilitySnapshotJson = "{}"
            });
        }

        await db.SaveChangesAsync();
    }
}
