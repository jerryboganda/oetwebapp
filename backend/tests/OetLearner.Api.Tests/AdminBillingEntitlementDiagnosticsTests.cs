using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

[Collection("AuthFlows")]
public class AdminBillingEntitlementDiagnosticsTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public AdminBillingEntitlementDiagnosticsTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");
    }

    [Fact]
    public async Task AdminBillingEntitlementDiagnostics_RequiresBillingReadPermission()
    {
        var previous = Environment.GetEnvironmentVariable("Auth__UseDevelopmentAuth");
        Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", "true");
        try
        {
            using var factory = new TestWebApplicationFactory();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
            client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", AdminPermissions.ContentRead);

            var response = await client.GetAsync("/v1/admin/billing/entitlement-diagnostics");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", previous);
        }
    }

    [Fact]
    public async Task AdminBillingEntitlementDiagnostics_ReturnsSanitizedAggregateWarnings()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var invalidPlanCode = $"diag-ai-invalid-{suffix}";
        var legacyPlanCode = $"diag-content-legacy-{suffix}";
        var missingPlanCode = $"missing-plan-{suffix}";
        var missingQuotaCode = $"missing-quota-{suffix}";
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Users.Add(new LearnerUser
            {
                Id = $"diag-user-{suffix}",
                DisplayName = "Diagnostics Learner",
                Email = $"diag-{suffix}@example.com",
                CreatedAt = now,
                LastActiveAt = now
            });

            db.BillingPlans.AddRange(
                new BillingPlan
                {
                    Id = $"plan-{invalidPlanCode}",
                    Code = invalidPlanCode,
                    Name = "Diagnostics Invalid AI Plan",
                    Description = "Plan with explicit AI quota mapping to a missing quota plan.",
                    Price = 19m,
                    Currency = "AUD",
                    Interval = "monthly",
                    DurationMonths = 1,
                    IsVisible = true,
                    IsRenewable = true,
                    TrialDays = 0,
                    DisplayOrder = -21,
                    IncludedCredits = 0,
                    IncludedSubtestsJson = JsonSerializer.Serialize(new[] { "writing" }),
                    EntitlementsJson = JsonSerializer.Serialize(new { ai = new { quotaPlanCode = missingQuotaCode }, content = new { access = "explicit" } }),
                    Status = BillingPlanStatus.Active,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new BillingPlan
                {
                    Id = $"plan-{legacyPlanCode}",
                    Code = legacyPlanCode,
                    Name = "Diagnostics Legacy Content Plan",
                    Description = "Plan missing the content entitlement node.",
                    Price = 29m,
                    Currency = "AUD",
                    Interval = "monthly",
                    DurationMonths = 1,
                    IsVisible = true,
                    IsRenewable = true,
                    TrialDays = 0,
                    DisplayOrder = -20,
                    IncludedCredits = 0,
                    IncludedSubtestsJson = JsonSerializer.Serialize(new[] { "speaking" }),
                    EntitlementsJson = JsonSerializer.Serialize(new { ai = new { quotaPlanCode = "free" } }),
                    Status = BillingPlanStatus.Active,
                    CreatedAt = now,
                    UpdatedAt = now
                });

            db.Subscriptions.Add(new Subscription
            {
                Id = $"diag-subscription-{suffix}",
                UserId = $"diag-user-{suffix}",
                PlanId = missingPlanCode,
                Status = SubscriptionStatus.Active,
                NextRenewalAt = now.AddMonths(1),
                StartedAt = now.AddDays(-1),
                ChangedAt = now,
                PriceAmount = 49m,
                Currency = "AUD",
                Interval = "monthly"
            });

            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/v1/admin/billing/entitlement-diagnostics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("EntitlementsJson", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("includedSubtestsJson", body, StringComparison.OrdinalIgnoreCase);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.True(root.GetProperty("summary").GetProperty("totalWarnings").GetInt32() >= 3);

        var invalidAi = FindCheck(root, "invalid_ai_quota_mapping");
        Assert.True(invalidAi.GetProperty("count").GetInt32() >= 1);
        var invalidExample = FindExample(invalidAi, invalidPlanCode);
        Assert.Equal(missingQuotaCode, invalidExample.GetProperty("metadata").GetProperty("aiQuotaPlanCode").GetString());

        var legacyContent = FindCheck(root, "legacy_content_shape");
        Assert.True(legacyContent.GetProperty("count").GetInt32() >= 1);
        Assert.Equal(legacyPlanCode, FindExample(legacyContent, legacyPlanCode).GetProperty("subjectCode").GetString());

        var missingPlan = FindCheck(root, "missing_plan_subscriptions");
        Assert.True(missingPlan.GetProperty("count").GetInt32() >= 1);
        Assert.Equal(missingPlanCode, FindExample(missingPlan, missingPlanCode).GetProperty("subjectCode").GetString());
    }

    [Fact]
    public async Task AdminBillingEntitlementDiagnostics_DirectAiPlanCodeWinsBeforeSeededFallback()
    {
        const string billingPlanCode = "premium-monthly";
        var directAiPlanId = $"ai-plan-direct-{Guid.NewGuid():N}";
        string? originalEntitlementsJson = null;
        int originalDisplayOrder = 0;
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            var billingPlan = await db.BillingPlans.FirstAsync(plan => plan.Code == billingPlanCode);
            originalEntitlementsJson = billingPlan.EntitlementsJson;
            originalDisplayOrder = billingPlan.DisplayOrder;
            billingPlan.EntitlementsJson = JsonSerializer.Serialize(new { content = new { access = "explicit" } });
            billingPlan.DisplayOrder = -31;

            db.AiQuotaPlans.Add(new AiQuotaPlan
            {
                Id = directAiPlanId,
                Code = billingPlanCode,
                Name = "Premium Monthly Direct AI",
                Description = "Direct code match used by diagnostics precedence tests.",
                MonthlyTokenCap = 123,
                DailyTokenCap = 0,
                MaxConcurrentRequests = 0,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });

            await db.SaveChangesAsync();
        }

        try
        {
            var response = await _client.GetAsync("/v1/admin/billing/entitlement-diagnostics");
            response.EnsureSuccessStatusCode();

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            Assert.False(HasExample(FindCheck(root, "fallback_ai_quota_mapping"), billingPlanCode));
            Assert.False(HasExample(FindCheck(root, "invalid_ai_quota_mapping"), billingPlanCode));
        }
        finally
        {
            await using var cleanupScope = _factory.Services.CreateAsyncScope();
            var db = cleanupScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var billingPlan = await db.BillingPlans.FirstAsync(plan => plan.Code == billingPlanCode);
            billingPlan.EntitlementsJson = originalEntitlementsJson ?? billingPlan.EntitlementsJson;
            billingPlan.DisplayOrder = originalDisplayOrder;
            var directPlan = await db.AiQuotaPlans.FirstOrDefaultAsync(plan => plan.Id == directAiPlanId);
            if (directPlan is not null)
            {
                db.AiQuotaPlans.Remove(directPlan);
            }
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task AdminBillingEntitlementDiagnostics_FlagsInactiveSeededFallbackTargetAsInvalid()
    {
        const string billingPlanCode = "basic-monthly";
        const string fallbackAiPlanCode = "starter";
        string? originalEntitlementsJson = null;
        int originalDisplayOrder = 0;
        bool originalFallbackActive = true;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            var billingPlan = await db.BillingPlans.FirstAsync(plan => plan.Code == billingPlanCode);
            originalEntitlementsJson = billingPlan.EntitlementsJson;
            originalDisplayOrder = billingPlan.DisplayOrder;
            billingPlan.EntitlementsJson = JsonSerializer.Serialize(new { content = new { access = "explicit" } });
            billingPlan.DisplayOrder = -32;

            var fallbackPlan = await db.AiQuotaPlans.FirstAsync(plan => plan.Code == fallbackAiPlanCode);
            originalFallbackActive = fallbackPlan.IsActive;
            fallbackPlan.IsActive = false;

            await db.SaveChangesAsync();
        }

        try
        {
            var response = await _client.GetAsync("/v1/admin/billing/entitlement-diagnostics");
            response.EnsureSuccessStatusCode();

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var invalidAi = FindCheck(json.RootElement, "invalid_ai_quota_mapping");
            var example = FindExample(invalidAi, billingPlanCode);
            Assert.Equal(fallbackAiPlanCode, example.GetProperty("metadata").GetProperty("aiQuotaPlanCode").GetString());
            Assert.Equal("fallback", example.GetProperty("metadata").GetProperty("mappingSource").GetString());
        }
        finally
        {
            await using var cleanupScope = _factory.Services.CreateAsyncScope();
            var db = cleanupScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var billingPlan = await db.BillingPlans.FirstAsync(plan => plan.Code == billingPlanCode);
            billingPlan.EntitlementsJson = originalEntitlementsJson ?? billingPlan.EntitlementsJson;
            billingPlan.DisplayOrder = originalDisplayOrder;
            var fallbackPlan = await db.AiQuotaPlans.FirstAsync(plan => plan.Code == fallbackAiPlanCode);
            fallbackPlan.IsActive = originalFallbackActive;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task AdminBillingEntitlementDiagnostics_IncludesHiddenPlanReferencedByActiveSubscription()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var planCode = $"diag-hidden-plan-{suffix}";
        var missingQuotaCode = $"diag-hidden-quota-{suffix}";
        var userId = $"diag-hidden-user-{suffix}";
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Users.Add(new LearnerUser
            {
                Id = userId,
                DisplayName = "Hidden Plan Learner",
                Email = $"diag-hidden-{suffix}@example.com",
                CreatedAt = now,
                LastActiveAt = now
            });
            db.BillingPlans.Add(new BillingPlan
            {
                Id = $"plan-{planCode}",
                Code = planCode,
                Name = "Subscribed Hidden Plan",
                Description = "Hidden plan still referenced by an active subscription.",
                Price = 39m,
                Currency = "AUD",
                Interval = "monthly",
                DurationMonths = 1,
                IsVisible = false,
                IsRenewable = false,
                TrialDays = 0,
                DisplayOrder = -33,
                IncludedCredits = 0,
                IncludedSubtestsJson = JsonSerializer.Serialize(new[] { "writing" }),
                EntitlementsJson = JsonSerializer.Serialize(new { ai = new { quotaPlanCode = missingQuotaCode }, content = new { access = "explicit" } }),
                Status = BillingPlanStatus.Inactive,
                CreatedAt = now,
                UpdatedAt = now
            });
            db.Subscriptions.Add(new Subscription
            {
                Id = $"diag-hidden-subscription-{suffix}",
                UserId = userId,
                PlanId = $"  {planCode.ToUpperInvariant()}  ",
                Status = SubscriptionStatus.Active,
                NextRenewalAt = now.AddMonths(1),
                StartedAt = now.AddDays(-1),
                ChangedAt = now,
                PriceAmount = 39m,
                Currency = "AUD",
                Interval = "monthly"
            });

            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/v1/admin/billing/entitlement-diagnostics");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var invalidAi = FindCheck(json.RootElement, "invalid_ai_quota_mapping");
        var example = FindExample(invalidAi, planCode);
        Assert.Equal(missingQuotaCode, example.GetProperty("metadata").GetProperty("aiQuotaPlanCode").GetString());
    }

    private static JsonElement FindCheck(JsonElement root, string key)
    {
        foreach (var check in root.GetProperty("checks").EnumerateArray())
        {
            if (check.GetProperty("key").GetString() == key)
            {
                return check;
            }
        }

        throw new InvalidOperationException($"Check '{key}' was not returned.");
    }

    private static JsonElement FindExample(JsonElement check, string subjectCode)
    {
        foreach (var example in check.GetProperty("examples").EnumerateArray())
        {
            if (example.GetProperty("subjectCode").GetString() == subjectCode)
            {
                return example;
            }
        }

        throw new InvalidOperationException($"Example '{subjectCode}' was not returned.");
    }

    private static bool HasExample(JsonElement check, string subjectCode)
    {
        foreach (var example in check.GetProperty("examples").EnumerateArray())
        {
            if (example.GetProperty("subjectCode").GetString() == subjectCode)
            {
                return true;
            }
        }

        return false;
    }
}