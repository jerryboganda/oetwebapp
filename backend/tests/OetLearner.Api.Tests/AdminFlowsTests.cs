using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class AdminFlowsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AdminFlowsTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Debug-UserId", "admin-user-001");
        _client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
        _client.DefaultRequestHeaders.Add("X-Debug-Name", "Admin User");
    }

    // ── Content ────────────────────────────────

    [Fact]
    public async Task AdminContent_ListReturnsSeededItems()
    {
        var response = await _client.GetAsync("/v1/admin/content");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("total").GetInt32() >= 1);
        Assert.True(json.RootElement.GetProperty("items").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task AdminContent_CreateAndPublish()
    {
        var createResponse = await _client.PostAsJsonAsync("/v1/admin/content", new
        {
            contentType = "writing_task",
            subtestCode = "writing",
            professionId = "nursing",
            title = "Test Referral Letter - Admin",
            difficulty = "medium"
        });
        createResponse.EnsureSuccessStatusCode();

        using var createJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var contentId = createJson.RootElement.GetProperty("id").GetString()!;
        Assert.Equal("draft", createJson.RootElement.GetProperty("status").GetString());

        var publishResponse = await _client.PostAsync($"/v1/admin/content/{contentId}/publish", null);
        publishResponse.EnsureSuccessStatusCode();

        using var publishJson = JsonDocument.Parse(await publishResponse.Content.ReadAsStringAsync());
        Assert.Equal("published", publishJson.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AdminContent_UpdateCreatesRevision()
    {
        var createResponse = await _client.PostAsJsonAsync("/v1/admin/content", new
        {
            contentType = "speaking_task",
            subtestCode = "speaking",
            professionId = "nursing",
            title = "Test Speaking Task - Revision",
            difficulty = "easy"
        });
        createResponse.EnsureSuccessStatusCode();
        using var createJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var contentId = createJson.RootElement.GetProperty("id").GetString()!;

        var updateResponse = await _client.PutAsJsonAsync($"/v1/admin/content/{contentId}", new
        {
            title = "Updated Speaking Task",
            changeNote = "Updated title for testing"
        });
        updateResponse.EnsureSuccessStatusCode();

        var revisionsResponse = await _client.GetAsync($"/v1/admin/content/{contentId}/revisions");
        revisionsResponse.EnsureSuccessStatusCode();
        using var revisionsJson = JsonDocument.Parse(await revisionsResponse.Content.ReadAsStringAsync());
        Assert.True(revisionsJson.RootElement.GetArrayLength() >= 2);
    }

    // ── Taxonomy ───────────────────────────────

    [Fact]
    public async Task AdminTaxonomy_ListReturnsSeededProfessions()
    {
        var response = await _client.GetAsync("/v1/admin/taxonomy");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetArrayLength() >= 1);
    }

    // ── Criteria ───────────────────────────────

    [Fact]
    public async Task AdminCriteria_ListAndCreate()
    {
        var listResponse = await _client.GetAsync("/v1/admin/criteria");
        listResponse.EnsureSuccessStatusCode();

        var createResponse = await _client.PostAsJsonAsync("/v1/admin/criteria", new
        {
            name = "Test Criterion",
            subtestCode = "writing",
            description = "Criterion created by admin test"
        });
        createResponse.EnsureSuccessStatusCode();

        using var createJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(createJson.RootElement.GetProperty("id").GetString()));
    }

    // ── AI Config ──────────────────────────────

    [Fact]
    public async Task AdminAIConfig_ListReturnsSeeded()
    {
        var response = await _client.GetAsync("/v1/admin/ai-config");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetArrayLength() >= 4);
    }

    [Fact]
    public async Task AdminAIConfig_CreateAndUpdate()
    {
        var createResponse = await _client.PostAsJsonAsync("/v1/admin/ai-config", new
        {
            model = "test-model",
            provider = "TestProvider",
            taskType = "writing",
            accuracy = 90.0,
            confidenceThreshold = 0.80
        });
        createResponse.EnsureSuccessStatusCode();

        using var createJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var configId = createJson.RootElement.GetProperty("id").GetString()!;

        var updateResponse = await _client.PutAsJsonAsync($"/v1/admin/ai-config/{configId}", new
        {
            status = "active",
            accuracy = 95.5
        });
        updateResponse.EnsureSuccessStatusCode();

        using var updateJson = JsonDocument.Parse(await updateResponse.Content.ReadAsStringAsync());
        Assert.Equal("active", updateJson.RootElement.GetProperty("status").GetString());
    }

    // ── Feature Flags ──────────────────────────

    [Fact]
    public async Task AdminFlags_ListReturnsSeeded()
    {
        var response = await _client.GetAsync("/v1/admin/flags");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetArrayLength() >= 4);
    }

    [Fact]
    public async Task AdminFlags_CreateToggleUpdate()
    {
        var createResponse = await _client.PostAsJsonAsync("/v1/admin/flags", new
        {
            name = "Test Flag",
            key = $"test_flag_{Guid.NewGuid():N}"[..24],
            enabled = false,
            flagType = "release",
            description = "Test flag from integration tests"
        });
        createResponse.EnsureSuccessStatusCode();

        using var createJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var flagId = createJson.RootElement.GetProperty("id").GetString()!;
        Assert.False(createJson.RootElement.GetProperty("enabled").GetBoolean());

        var toggleResponse = await _client.PutAsJsonAsync($"/v1/admin/flags/{flagId}", new { enabled = true });
        toggleResponse.EnsureSuccessStatusCode();

        using var toggleJson = JsonDocument.Parse(await toggleResponse.Content.ReadAsStringAsync());
        Assert.True(toggleJson.RootElement.GetProperty("enabled").GetBoolean());
    }

    // ── Audit Logs ─────────────────────────────

    [Fact]
    public async Task AdminAuditLogs_ReturnsSeeded()
    {
        var response = await _client.GetAsync("/v1/admin/audit-logs");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("total").GetInt32() >= 4);
    }

    // ── Users ──────────────────────────────────

    [Fact]
    public async Task AdminUsers_ListAndDetail()
    {
        var listResponse = await _client.GetAsync("/v1/admin/users");
        listResponse.EnsureSuccessStatusCode();

        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.True(listJson.RootElement.GetProperty("total").GetInt32() >= 1);

        var detailResponse = await _client.GetAsync("/v1/admin/users/mock-user-001");
        detailResponse.EnsureSuccessStatusCode();

        using var detailJson = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal("mock-user-001", detailJson.RootElement.GetProperty("id").GetString());
    }

    // ── Billing ────────────────────────────────

    [Fact]
    public async Task AdminBilling_PlansReturnsSeeded()
    {
        var response = await _client.GetAsync("/v1/admin/billing/plans");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetArrayLength() >= 4);
    }

    // ── Review Ops ─────────────────────────────

    [Fact]
    public async Task AdminReviewOps_SummaryReturnsShape()
    {
        var response = await _client.GetAsync("/v1/admin/review-ops/summary");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("backlog", out _));
        Assert.True(json.RootElement.TryGetProperty("statusDistribution", out _));
    }

    // ── Quality Analytics ──────────────────────

    [Fact]
    public async Task AdminQualityAnalytics_ReturnsShape()
    {
        var response = await _client.GetAsync("/v1/admin/quality-analytics");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("aiHumanAgreement", out _));
        Assert.True(json.RootElement.TryGetProperty("reviewSLA", out _));
        Assert.True(json.RootElement.TryGetProperty("featureAdoption", out _));
    }

    // ── Auth Guard ─────────────────────────────

    [Fact]
    public async Task AdminEndpoints_RejectNonAdminRole()
    {
        var client = new TestWebApplicationFactory().CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", "mock-user-001");
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");

        var response = await client.GetAsync("/v1/admin/content");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
