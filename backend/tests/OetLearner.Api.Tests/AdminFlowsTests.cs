using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

[Collection("AuthFlows")]
public class AdminFlowsTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AdminFlowsTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _client = factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");
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
    public async Task AdminContent_ListReturnsSeededItems_WithSeededJwtSignIn()
    {
        using var factory = new FirstPartyAuthTestWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");

        var response = await client.GetAsync("/v1/admin/content");
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

    [Fact]
    public async Task AdminDashboard_ReturnsOperationalSummary()
    {
        var response = await _client.GetAsync("/v1/admin/dashboard");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("generatedAt", out _));
        Assert.True(json.RootElement.TryGetProperty("contentHealth", out _));
        Assert.True(json.RootElement.TryGetProperty("reviewOps", out _));
        Assert.True(json.RootElement.TryGetProperty("billingRisk", out _));
        Assert.True(json.RootElement.TryGetProperty("quality", out _));
    }

    [Fact]
    public async Task PublishRequests_AllowsEditorReviewPermissionToReadQueue()
    {
        var previous = Environment.GetEnvironmentVariable("Auth__UseDevelopmentAuth");
        Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", "true");
        try
        {
            using var factory = new TestWebApplicationFactory();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
            client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", AdminPermissions.ContentEditorReview);

            var response = await client.GetAsync("/v1/admin/publish-requests");

            response.EnsureSuccessStatusCode();
        }
        finally
        {
            Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", previous);
        }
    }

    [Fact]
    public async Task PublishRequests_DoesNotAllowContentReadOnlyPermissionToReadQueue()
    {
        var previous = Environment.GetEnvironmentVariable("Auth__UseDevelopmentAuth");
        Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", "true");
        try
        {
            using var factory = new TestWebApplicationFactory();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
            client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", AdminPermissions.ContentRead);

            var response = await client.GetAsync("/v1/admin/publish-requests");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", previous);
        }
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("unpublish")]
    public async Task GrammarLessonPublishing_DoesNotAllowContentWriteOnlyPermission(string action)
    {
        var previous = Environment.GetEnvironmentVariable("Auth__UseDevelopmentAuth");
        Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", "true");
        try
        {
            using var factory = new TestWebApplicationFactory();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
            client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", AdminPermissions.ContentWrite);

            var response = await client.PostAsync($"/v1/admin/grammar/lessons/missing-lesson/{action}", null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", previous);
        }
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("unpublish")]
    public async Task GrammarLessonPublishing_AllowsContentPublishPermissionToReachHandler(string action)
    {
        var previous = Environment.GetEnvironmentVariable("Auth__UseDevelopmentAuth");
        Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", "true");
        try
        {
            using var factory = new TestWebApplicationFactory();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
            client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", AdminPermissions.ContentPublish);

            var response = await client.PostAsync($"/v1/admin/grammar/lessons/missing-lesson/{action}", null);

            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", previous);
        }
    }

    [Fact]
    public async Task AdminDashboard_AndContentList_RemainQueryable_WhenSqliteBacksDesktopRuntime()
    {
        var sqlitePath = Path.Combine(Path.GetTempPath(), $"oet-admin-dashboard-{Guid.NewGuid():N}.db");

        try
        {
            await using var factory = new SqliteAdminWebApplicationFactory(sqlitePath);
            using var client = factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");

            var dashboardResponse = await client.GetAsync("/v1/admin/dashboard");
            var dashboardPayload = await dashboardResponse.Content.ReadAsStringAsync();
            Assert.True(dashboardResponse.IsSuccessStatusCode, dashboardPayload);

            using var dashboardJson = JsonDocument.Parse(dashboardPayload);
            Assert.True(dashboardJson.RootElement.TryGetProperty("generatedAt", out _));
            Assert.True(dashboardJson.RootElement.TryGetProperty("contentHealth", out _));

            var contentResponse = await client.GetAsync("/v1/admin/content");
            var contentPayload = await contentResponse.Content.ReadAsStringAsync();
            Assert.True(contentResponse.IsSuccessStatusCode, contentPayload);

            using var contentJson = JsonDocument.Parse(contentPayload);
            Assert.True(contentJson.RootElement.GetProperty("total").GetInt32() >= 1);
            Assert.True(contentJson.RootElement.GetProperty("items").GetArrayLength() >= 1);
        }
        finally
        {
            foreach (var path in new[] { sqlitePath, $"{sqlitePath}-wal", $"{sqlitePath}-shm" })
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // Windows can briefly retain SQLite file handles after host disposal.
                }
            }
        }
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

    [Fact]
    public async Task AdminCriteria_StatusIsPersistedAndFilterable()
    {
        var createResponse = await _client.PostAsJsonAsync("/v1/admin/criteria", new
        {
            name = "Archived Criterion",
            subtestCode = "writing",
            weight = 9,
            description = "Archive me after creation"
        });
        createResponse.EnsureSuccessStatusCode();

        using var createJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var criterionId = createJson.RootElement.GetProperty("id").GetString()!;

        var updateResponse = await _client.PutAsJsonAsync($"/v1/admin/criteria/{criterionId}", new
        {
            status = "archived"
        });
        updateResponse.EnsureSuccessStatusCode();

        var archivedResponse = await _client.GetAsync("/v1/admin/criteria?status=archived");
        archivedResponse.EnsureSuccessStatusCode();

        using var archivedJson = JsonDocument.Parse(await archivedResponse.Content.ReadAsStringAsync());
        Assert.Contains(archivedJson.RootElement.EnumerateArray(), item =>
            item.GetProperty("id").GetString() == criterionId &&
            item.GetProperty("status").GetString() == "archived");
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

    [Fact]
    public async Task AdminAuditLogs_ExportReturnsCsv()
    {
        var response = await _client.GetAsync("/v1/admin/audit-logs/export");
        response.EnsureSuccessStatusCode();

        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("Id,Timestamp,Actor,Action,ResourceType,ResourceId,Details", csv);
        Assert.Contains("aud-", csv, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task AdminUsers_InviteAndTriggerPasswordReset()
    {
        var email = $"invited-admin-{Guid.NewGuid():N}@oet-prep.dev";
        var inviteResponse = await _client.PostAsJsonAsync("/v1/admin/users/invite", new
        {
            name = "Invited Admin",
            email,
            role = "admin"
        });
        inviteResponse.EnsureSuccessStatusCode();

        using var inviteJson = JsonDocument.Parse(await inviteResponse.Content.ReadAsStringAsync());
        var userId = inviteJson.RootElement.GetProperty("id").GetString()!;
        Assert.Equal("admin", inviteJson.RootElement.GetProperty("role").GetString());
        Assert.Equal(email, inviteJson.RootElement.GetProperty("email").GetString());
        Assert.True(inviteJson.RootElement.TryGetProperty("invitation", out _));

        var resetResponse = await _client.PostAsync($"/v1/admin/users/{userId}/password-reset", null);
        resetResponse.EnsureSuccessStatusCode();

        using var resetJson = JsonDocument.Parse(await resetResponse.Content.ReadAsStringAsync());
        Assert.Equal("reset_password", resetJson.RootElement.GetProperty("purpose").GetString());
        Assert.Equal(userId, resetJson.RootElement.GetProperty("userId").GetString());
    }

    [Fact]
    public async Task AdminUsers_DeleteAndRestoreLearnerAccount()
    {
        var deleteResponse = await _client.PostAsJsonAsync("/v1/admin/users/mock-user-001/delete", new
        {
            reason = "testing delete lifecycle"
        });
        deleteResponse.EnsureSuccessStatusCode();

        using var deleteJson = JsonDocument.Parse(await deleteResponse.Content.ReadAsStringAsync());
        Assert.Equal("deleted", deleteJson.RootElement.GetProperty("status").GetString());

        var deletedDetailResponse = await _client.GetAsync("/v1/admin/users/mock-user-001");
        deletedDetailResponse.EnsureSuccessStatusCode();
        using var deletedDetailJson = JsonDocument.Parse(await deletedDetailResponse.Content.ReadAsStringAsync());
        Assert.Equal("deleted", deletedDetailJson.RootElement.GetProperty("status").GetString());
        Assert.True(deletedDetailJson.RootElement.GetProperty("availableActions").GetProperty("canRestore").GetBoolean());

        var restoreResponse = await _client.PostAsJsonAsync("/v1/admin/users/mock-user-001/restore", new
        {
            reason = "testing restore lifecycle"
        });
        restoreResponse.EnsureSuccessStatusCode();

        using var restoreJson = JsonDocument.Parse(await restoreResponse.Content.ReadAsStringAsync());
        Assert.Equal("active", restoreJson.RootElement.GetProperty("status").GetString());

        var listResponse = await _client.GetAsync("/v1/admin/users?status=active");
        listResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AdminUsers_StatusUpdateRejectsUnknownValues()
    {
        var response = await _client.PutAsJsonAsync("/v1/admin/users/mock-user-001/status", new
        {
            status = "paused",
            reason = "testing"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_user_status", await ReadErrorCodeAsync(response));
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
        Assert.True(json.RootElement.TryGetProperty("filters", out _));
        Assert.True(json.RootElement.TryGetProperty("freshness", out _));
        Assert.True(json.RootElement.TryGetProperty("trendSeries", out _));
    }

    // ── Auth Guard ─────────────────────────────

    [Fact]
    public async Task AdminEndpoints_RejectNonAdminRole()
    {
        using var factory = new FirstPartyAuthTestWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");

        var response = await client.GetAsync("/v1/admin/content");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Bulk User Import ───────────────────────

    [Fact]
    public async Task AdminUsers_BulkImport_CreatesUsersFromCsv()
    {
        var csv = "email,firstName,lastName,role,profession\n"
                + $"import-learner-{Guid.NewGuid():N}@oet-prep.dev,Jane,Smith,learner,nursing\n"
                + $"import-expert-{Guid.NewGuid():N}@oet-prep.dev,John,Doe,expert,medicine\n";

        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(csv)), "file", "users.csv");

        var response = await _client.PostAsync("/v1/admin/users/import", content);
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, json.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("created").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("skipped").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("errors").GetArrayLength());
    }

    [Fact]
    public async Task AdminUsers_BulkImport_SkipsDuplicateEmails()
    {
        var email = $"dup-import-{Guid.NewGuid():N}@oet-prep.dev";
        var csv = "email,firstName,lastName,role,profession\n"
                + $"{email},First,User,learner,nursing\n"
                + $"{email},Duplicate,User,learner,nursing\n";

        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(csv)), "file", "users.csv");

        var response = await _client.PostAsync("/v1/admin/users/import", content);
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, json.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("created").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("skipped").GetInt32());
    }

    [Fact]
    public async Task AdminUsers_BulkImport_ReportsInvalidRows()
    {
        var csv = "email,firstName,lastName,role,profession\n"
                + "not-an-email,Bad,User,learner,nursing\n"
                + $"valid-{Guid.NewGuid():N}@oet-prep.dev,Good,User,invalidrole,nursing\n";

        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(csv)), "file", "users.csv");

        var response = await _client.PostAsync("/v1/admin/users/import", content);
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, json.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("created").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("errors").GetArrayLength());
    }

    [Fact]
    public async Task AdminUsers_BulkImport_RejectsEmptyCsv()
    {
        var csv = "email,firstName,lastName,role,profession\n";

        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(csv)), "file", "users.csv");

        var response = await _client.PostAsync("/v1/admin/users/import", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var codeElement)
            ? codeElement.GetString()
            : null;
    }

    private sealed class SqliteAdminWebApplicationFactory(string sqlitePath) : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = $"Data Source={sqlitePath}"
                });
            });
        }
    }

}
