using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class MockV2EndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public MockV2EndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateBooking_ReturnsLearnerSafeDto()
    {
        var bundleId = await EnsurePublishedBundleAsync("mock-v2-booking-bundle");
        using var client = await CreateLearnerClientAsync("mock-v2-booking-user");

        var response = await client.PostAsJsonAsync("/v1/mock-bookings", new
        {
            mockBundleId = bundleId,
            scheduledStartAt = DateTimeOffset.UtcNow.AddDays(2),
            timezoneIana = "Asia/Karachi",
            deliveryMode = MockDeliveryModes.OetHome,
            consentToRecording = true,
            learnerNotes = "Need a final readiness slot."
        });
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("id").GetString()));
        Assert.Equal(bundleId, root.GetProperty("mockBundleId").GetString());
        Assert.Equal("OET@Home Readiness Bundle", root.GetProperty("title").GetString());
        Assert.Equal(MockDeliveryModes.OetHome, root.GetProperty("deliveryMode").GetString());
        Assert.True(root.GetProperty("candidateCardVisible").GetBoolean());
        Assert.False(root.GetProperty("interlocutorCardVisible").GetBoolean());
        Assert.False(root.TryGetProperty("zoomStartUrl", out _));
    }

    [Fact]
    public async Task DiagnosticEntitlement_UsesBillingPlanConfiguration()
    {
        const string userId = "mock-v2-diagnostic-disabled";
        using var client = await CreateLearnerClientAsync(userId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var now = DateTimeOffset.UtcNow;
            var planId = $"plan-{Guid.NewGuid():N}";
            db.BillingPlans.Add(new BillingPlan
            {
                Id = planId,
                Code = planId,
                Name = "Diagnostic Disabled Plan",
                Description = "Test plan",
                Price = 0,
                Currency = "AUD",
                Interval = "month",
                DurationMonths = 1,
                IsVisible = true,
                IsRenewable = true,
                DiagnosticMockEntitlement = MockDiagnosticEntitlementService.Disabled,
                IncludedSubtestsJson = "[]",
                EntitlementsJson = "{}",
                Status = BillingPlanStatus.Active,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Subscriptions.Add(new Subscription
            {
                Id = $"sub-{Guid.NewGuid():N}",
                UserId = userId,
                PlanId = planId,
                Status = SubscriptionStatus.Active,
                StartedAt = now.AddDays(-1),
                ChangedAt = now,
                NextRenewalAt = now.AddMonths(1),
                PriceAmount = 0,
                Currency = "AUD",
                Interval = "monthly",
            });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/v1/mocks/diagnostic/entitlement");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("allowed").GetBoolean());
        Assert.Equal(MockDiagnosticEntitlementService.Disabled, json.RootElement.GetProperty("entitlement").GetString());
        Assert.Equal("diagnostic_disabled_for_plan", json.RootElement.GetProperty("reason").GetString());
    }

    private async Task<HttpClient> CreateLearnerClientAsync(string userId)
    {
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }

    private async Task<string> EnsurePublishedBundleAsync(string id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var existing = await db.MockBundles.FirstOrDefaultAsync(x => x.Id == id);
        if (existing is null)
        {
            db.MockBundles.Add(new MockBundle
            {
                Id = id,
                Title = "OET@Home Readiness Bundle",
                Slug = id,
                ExamFamilyCode = "oet",
                ExamTypeCode = "oet",
                MockType = MockTypes.FinalReadiness,
                AppliesToAllProfessions = true,
                Status = ContentStatus.Published,
                EstimatedDurationMinutes = 180,
                ReleasePolicy = MockReleasePolicies.AfterTeacherMarking,
                SourceStatus = MockSourceStatuses.Original,
                QualityStatus = MockQualityStatuses.Approved,
                SourceProvenance = "Admin-authored test bundle.",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                PublishedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.Status = ContentStatus.Published;
            existing.ReleasePolicy = MockReleasePolicies.AfterTeacherMarking;
            existing.QualityStatus = MockQualityStatuses.Approved;
            existing.SourceStatus = MockSourceStatuses.Original;
        }
        await db.SaveChangesAsync();
        return id;
    }
}
