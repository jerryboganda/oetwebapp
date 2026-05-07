using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class MockLeakReportAdminEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public MockLeakReportAdminEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListLeakReports_ReturnsSeededRow_WithBundleTitleAndDisplayName()
    {
        var bundleId = $"leak-list-bundle-{Guid.NewGuid():N}";
        var reporterId = $"leak-list-user-{Guid.NewGuid():N}";
        await SeedLeakReportAsync(bundleId, reporterId, status: "open");

        using var client = CreateAdminClient(AdminPermissions.ContentRead);

        var response = await client.GetAsync("/v1/admin/mocks/leak-reports?status=open&limit=50");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);
        var ours = items.EnumerateArray().First(x => x.GetProperty("bundleId").GetString() == bundleId);
        Assert.Equal("Leak Report Test Bundle", ours.GetProperty("bundleTitle").GetString());
        Assert.Equal("open", ours.GetProperty("status").GetString());
        Assert.Equal("high", ours.GetProperty("severity").GetString());
        Assert.Equal(reporterId, ours.GetProperty("reportedByUserDisplayName").GetString());
        // Privacy floor: never serialise the learner email field.
        Assert.False(ours.TryGetProperty("reportedByUserEmail", out _));
    }

    [Fact]
    public async Task UpdateLeakReport_ResolvesAndWritesAuditEvent()
    {
        var bundleId = $"leak-update-bundle-{Guid.NewGuid():N}";
        var reporterId = $"leak-update-user-{Guid.NewGuid():N}";
        var reviewId = await SeedLeakReportAsync(bundleId, reporterId, status: "open");

        using var client = CreateAdminClient(AdminPermissions.ContentRead, AdminPermissions.ContentWrite);

        var response = await client.PatchAsJsonAsync($"/v1/admin/mocks/leak-reports/{reviewId}", new
        {
            status = "resolved",
            resolutionNote = "Confirmed false positive after editorial review."
        });
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("resolved", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "Confirmed false positive after editorial review.",
            json.RootElement.GetProperty("resolutionNote").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("resolvedAt").GetString()));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var review = await db.MockContentReviews.AsNoTracking().SingleAsync(x => x.Id == reviewId);
        Assert.Equal("resolved", review.Status);
        Assert.NotNull(review.ResolvedAt);

        var audit = await db.AuditEvents.AsNoTracking()
            .Where(x => x.ResourceType == "MockContentReview" && x.ResourceId == reviewId)
            .OrderByDescending(x => x.OccurredAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.Equal("MockLeakReport.Updated", audit!.Action);
    }

    [Fact]
    public async Task UpdateLeakReport_RejectsTransitionFromTerminalStatus()
    {
        var bundleId = $"leak-locked-bundle-{Guid.NewGuid():N}";
        var reporterId = $"leak-locked-user-{Guid.NewGuid():N}";
        var reviewId = await SeedLeakReportAsync(bundleId, reporterId, status: "dismissed");

        using var client = CreateAdminClient(AdminPermissions.ContentRead, AdminPermissions.ContentWrite);

        var response = await client.PatchAsJsonAsync($"/v1/admin/mocks/leak-reports/{reviewId}", new
        {
            status = "open",
            resolutionNote = (string?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("mock_leak_report_status_locked", error.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateLeakReport_RequiresContentWritePermission()
    {
        var bundleId = $"leak-perm-bundle-{Guid.NewGuid():N}";
        var reporterId = $"leak-perm-user-{Guid.NewGuid():N}";
        var reviewId = await SeedLeakReportAsync(bundleId, reporterId, status: "open");

        using var client = CreateAdminClient(AdminPermissions.ContentRead);

        var response = await client.PatchAsJsonAsync($"/v1/admin/mocks/leak-reports/{reviewId}", new
        {
            status = "investigating"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient CreateAdminClient(params string[] permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
        client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", string.Join(',', permissions));
        return client;
    }

    private async Task<string> SeedLeakReportAsync(string bundleId, string reporterId, string status)
    {
        await _factory.EnsureLearnerProfileAsync(reporterId, $"{reporterId}@example.test", reporterId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        if (!await db.MockBundles.AnyAsync(x => x.Id == bundleId))
        {
            db.MockBundles.Add(new MockBundle
            {
                Id = bundleId,
                Title = "Leak Report Test Bundle",
                Slug = bundleId,
                ExamFamilyCode = "oet",
                ExamTypeCode = "oet",
                MockType = MockTypes.Diagnostic,
                AppliesToAllProfessions = true,
                Status = ContentStatus.Published,
                EstimatedDurationMinutes = 60,
                ReleasePolicy = MockReleasePolicies.Instant,
                SourceStatus = MockSourceStatuses.Original,
                QualityStatus = MockQualityStatuses.Approved,
                SourceProvenance = "Admin-authored test bundle.",
                CreatedAt = now,
                UpdatedAt = now,
                PublishedAt = now,
            });
        }

        var reviewId = $"mock-review-{Guid.NewGuid():N}";
        db.MockContentReviews.Add(new MockContentReview
        {
            Id = reviewId,
            MockBundleId = bundleId,
            ReportedByUserId = reporterId,
            ReviewType = "leak_report",
            Severity = "high",
            Status = status,
            Notes = JsonSerializer.Serialize(new
            {
                reason = "leak_suspected",
                evidenceUrl = "https://example.com/evidence",
                pageOrQuestion = "Q3"
            }),
            CreatedAt = now,
        });
        await db.SaveChangesAsync();
        return reviewId;
    }
}
