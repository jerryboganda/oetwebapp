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
    private const string VocabularyImportCsvHeader = "Term,Definition,ExampleSentence,Category,Difficulty,ProfessionId,ExamTypeCode,AmericanSpelling,AudioUrl,AudioSlowUrl,AudioSentenceUrl,AudioMediaAssetId,Collocations,RelatedTerms,SourceProvenance\n";

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
    public async Task AdminContent_SubtestFilter_ReturnsOnlyMatchingSubtest()
    {
        var response = await _client.GetAsync("/v1/admin/content?subtest=speaking&pageSize=50");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);

        foreach (var item in items.EnumerateArray())
        {
            Assert.Equal("speaking", item.GetProperty("subtestCode").GetString());
        }
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

    [Theory]
    [InlineData(AdminPermissions.ContentEditorReview)]
    [InlineData(AdminPermissions.ContentPublisherApproval)]
    [InlineData(AdminPermissions.ContentPublish)]
    [InlineData(AdminPermissions.SystemAdmin)]
    public async Task PublishRequests_AllowsWorkflowPermissionsToReadQueue(string permission)
    {
        var previous = Environment.GetEnvironmentVariable("Auth__UseDevelopmentAuth");
        Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", "true");
        try
        {
            using var factory = new TestWebApplicationFactory();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
            client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", permission);

            var response = await client.GetAsync("/v1/admin/publish-requests");

            response.EnsureSuccessStatusCode();
        }
        finally
        {
            Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", previous);
        }
    }

    [Theory]
    [InlineData(AdminPermissions.ContentRead)]
    [InlineData(AdminPermissions.ContentWrite)]
    [InlineData(AdminPermissions.BillingRead)]
    [InlineData(AdminPermissions.AiConfig)]
    public async Task PublishRequests_DoesNotAllowNonWorkflowPermissionsToReadQueue(string permission)
    {
        var previous = Environment.GetEnvironmentVariable("Auth__UseDevelopmentAuth");
        Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", "true");
        try
        {
            using var factory = new TestWebApplicationFactory();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
            client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", permission);

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
        // Provider must already exist in AiProviders (registry-backed). The seeded
        // stub is "digitalocean-serverless" — using an unknown code triggers the
        // invalid_provider validation added in the AI provider registry pass.
        var createResponse = await _client.PostAsJsonAsync("/v1/admin/ai-config", new
        {
            model = "test-model",
            provider = "digitalocean-serverless",
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

    // ── Vocabulary Import ─────────────────────

    [Fact]
    public async Task AdminVocabularyImport_DryRunCommitAndConflictPreview_PreservesRecallFields()
    {
        var term = $"recall-import-{Guid.NewGuid():N}";
        var batchId = $"recalls-preserve-{Guid.NewGuid():N}"[..32];
        var csv = VocabularyImportCsvHeader
            + $"{term},\"Recall definition\",\"Recall example sentence.\",medical,medium,medicine,oet,{term}-us,https://cdn.example/audio.mp3,https://cdn.example/audio-slow.mp3,https://cdn.example/audio-sentence.mp3,media-recall-001,\"recall phrase;clinical recall\",\"related one|related two\",\"src=verified-source;p=1;row=1\"\n";

        using (var previewContent = CsvContent(csv, "recalls.csv"))
        {
            var preview = await _client.PostAsync($"/v1/admin/vocabulary/import/preview?importBatchId={Uri.EscapeDataString(batchId)}", previewContent);
            preview.EnsureSuccessStatusCode();
            using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
            Assert.Equal(batchId, previewJson.RootElement.GetProperty("importBatchId").GetString());
            Assert.Equal(1, previewJson.RootElement.GetProperty("validRows").GetInt32());
            Assert.Equal(0, previewJson.RootElement.GetProperty("invalidRows").GetInt32());
            Assert.Equal(0, previewJson.RootElement.GetProperty("duplicateRows").GetInt32());
        }

        using (var dryRunContent = CsvContent(csv, "recalls.csv"))
        {
            var dryRun = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=true&importBatchId={Uri.EscapeDataString(batchId)}", dryRunContent);
            dryRun.EnsureSuccessStatusCode();
            using var dryRunJson = JsonDocument.Parse(await dryRun.Content.ReadAsStringAsync());
            Assert.Equal(batchId, dryRunJson.RootElement.GetProperty("importBatchId").GetString());
            Assert.Equal(1, dryRunJson.RootElement.GetProperty("imported").GetInt32());
            Assert.Equal(0, dryRunJson.RootElement.GetProperty("skipped").GetInt32());
            Assert.Equal(0, dryRunJson.RootElement.GetProperty("failedRows").GetInt32());
        }

        using (var importContent = CsvContent(csv, "recalls.csv"))
        {
            var import = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=false&importBatchId={Uri.EscapeDataString(batchId)}", importContent);
            import.EnsureSuccessStatusCode();
            using var importJson = JsonDocument.Parse(await import.Content.ReadAsStringAsync());
            Assert.Equal(batchId, importJson.RootElement.GetProperty("importBatchId").GetString());
            Assert.Equal(1, importJson.RootElement.GetProperty("imported").GetInt32());
            Assert.Equal(0, importJson.RootElement.GetProperty("skipped").GetInt32());
        }

        var list = await _client.GetAsync($"/v1/admin/vocabulary/items?search={Uri.EscapeDataString(term)}");
        list.EnsureSuccessStatusCode();
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var item = listJson.RootElement.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("term").GetString() == term);
        var id = item.GetProperty("id").GetString()!;

        var detail = await _client.GetAsync($"/v1/admin/vocabulary/items/{id}");
        detail.EnsureSuccessStatusCode();
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        var root = detailJson.RootElement;
        Assert.Equal($"{term}-us", root.GetProperty("americanSpelling").GetString());
        Assert.Equal("https://cdn.example/audio-slow.mp3", root.GetProperty("audioSlowUrl").GetString());
        Assert.Equal("https://cdn.example/audio-sentence.mp3", root.GetProperty("audioSentenceUrl").GetString());
        Assert.Equal("media-recall-001", root.GetProperty("audioMediaAssetId").GetString());
        Assert.Contains("clinical recall", root.GetProperty("collocationsJson").GetString());
        Assert.Contains("related two", root.GetProperty("relatedTermsJson").GetString());

        var conflictCsv = VocabularyImportCsvHeader
            + $" {term.ToUpperInvariant()} ,\"Recall definition\",\"Recall example sentence.\",medical,medium,Medicine,OET,{term}-us,https://cdn.example/audio.mp3,https://cdn.example/audio-slow.mp3,https://cdn.example/audio-sentence.mp3,media-recall-001,\"recall phrase;clinical recall\",\"related one|related two\",\"src=verified-source;p=1;row=1\"\n";

        using (var conflictPreviewContent = CsvContent(conflictCsv, "recalls-conflict.csv"))
        {
            var preview = await _client.PostAsync("/v1/admin/vocabulary/import/preview", conflictPreviewContent);
            preview.EnsureSuccessStatusCode();
            using var conflictJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
            Assert.Equal(0, conflictJson.RootElement.GetProperty("validRows").GetInt32());
            Assert.Equal(1, conflictJson.RootElement.GetProperty("invalidRows").GetInt32());
            Assert.Equal(1, conflictJson.RootElement.GetProperty("duplicateRows").GetInt32());
            Assert.Contains("Existing vocabulary term", conflictJson.RootElement.GetProperty("rows")[0].GetProperty("error").GetString());
        }

        using (var conflictDryRunContent = CsvContent(conflictCsv, "recalls-conflict.csv"))
        {
            var dryRun = await _client.PostAsync("/v1/admin/vocabulary/import?dryRun=true", conflictDryRunContent);
            dryRun.EnsureSuccessStatusCode();
            using var conflictDryRunJson = JsonDocument.Parse(await dryRun.Content.ReadAsStringAsync());
            Assert.Equal(0, conflictDryRunJson.RootElement.GetProperty("imported").GetInt32());
            Assert.Equal(1, conflictDryRunJson.RootElement.GetProperty("skipped").GetInt32());
            Assert.Equal(1, conflictDryRunJson.RootElement.GetProperty("duplicates").GetInt32());
        }
    }

    [Fact]
    public async Task AdminVocabularyImport_PreviewAndDryRun_BlockSameFileDuplicatesAndOverLimitFields()
    {
        var duplicateTerm = $"duplicate-recall-{Guid.NewGuid():N}";
        var duplicateCsv = VocabularyImportCsvHeader
            + $"{duplicateTerm},\"Definition\",\"Example.\",medical,medium,medicine,oet,,,,,,,,\"src=unit-test;p=1;row=1\"\n"
            + $"{duplicateTerm.ToUpperInvariant()},\"Definition 2\",\"Example 2.\",medical,medium,Medicine,OET,,,,,,,,\"src=unit-test;p=1;row=2\"\n";

        using (var previewContent = CsvContent(duplicateCsv, "recalls-duplicates.csv"))
        {
            var preview = await _client.PostAsync("/v1/admin/vocabulary/import/preview", previewContent);
            preview.EnsureSuccessStatusCode();
            using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
            Assert.Equal(1, previewJson.RootElement.GetProperty("validRows").GetInt32());
            Assert.Equal(1, previewJson.RootElement.GetProperty("invalidRows").GetInt32());
            Assert.Equal(1, previewJson.RootElement.GetProperty("duplicateRows").GetInt32());
        }

        using (var dryRunContent = CsvContent(duplicateCsv, "recalls-duplicates.csv"))
        {
            var dryRun = await _client.PostAsync("/v1/admin/vocabulary/import?dryRun=true", dryRunContent);
            dryRun.EnsureSuccessStatusCode();
            using var dryRunJson = JsonDocument.Parse(await dryRun.Content.ReadAsStringAsync());
            Assert.Equal(1, dryRunJson.RootElement.GetProperty("imported").GetInt32());
            Assert.Equal(1, dryRunJson.RootElement.GetProperty("skipped").GetInt32());
            Assert.Equal(1, dryRunJson.RootElement.GetProperty("duplicates").GetInt32());
        }

        var overLimitCategory = new string('c', 65);
        var overLimitCsv = VocabularyImportCsvHeader
            + $"limit-recall-{Guid.NewGuid():N},\"Definition\",\"Example.\",{overLimitCategory},medium,medicine,oet,,,,,,,,\"src=unit-test;p=2;row=1\"\n";

        using (var previewContent = CsvContent(overLimitCsv, "recalls-over-limit.csv"))
        {
            var preview = await _client.PostAsync("/v1/admin/vocabulary/import/preview", previewContent);
            preview.EnsureSuccessStatusCode();
            using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
            Assert.Equal(0, previewJson.RootElement.GetProperty("validRows").GetInt32());
            Assert.Equal(1, previewJson.RootElement.GetProperty("invalidRows").GetInt32());
            Assert.Contains("Category exceeds", previewJson.RootElement.GetProperty("rows")[0].GetProperty("error").GetString());
        }

        using (var dryRunContent = CsvContent(overLimitCsv, "recalls-over-limit.csv"))
        {
            var dryRun = await _client.PostAsync("/v1/admin/vocabulary/import?dryRun=true", dryRunContent);
            dryRun.EnsureSuccessStatusCode();
            using var dryRunJson = JsonDocument.Parse(await dryRun.Content.ReadAsStringAsync());
            Assert.Equal(0, dryRunJson.RootElement.GetProperty("imported").GetInt32());
            Assert.Equal(1, dryRunJson.RootElement.GetProperty("failedRows").GetInt32());
        }
    }

    [Fact]
    public async Task AdminVocabularyImport_PreviewSupportsMultilineCsvAndBlocksUnknownTaxonomy()
    {
        var multilineTerm = $"multiline-recall-{Guid.NewGuid():N}";
        var multilineCsv = VocabularyImportCsvHeader
            + $"{multilineTerm},\"Definition line one\r\nline two with \"\"quoted\"\" detail\",\"Example sentence with, comma.\",medical,medium,medicine,oet,,,,,,\"follow-up call\",\"related A|related B\",\"src=multiline;p=1;row=1\"\n";

        using (var previewContent = CsvContent(multilineCsv, "recalls-multiline.csv"))
        {
            var preview = await _client.PostAsync("/v1/admin/vocabulary/import/preview", previewContent);
            preview.EnsureSuccessStatusCode();
            using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
            Assert.Equal(1, previewJson.RootElement.GetProperty("validRows").GetInt32());
            Assert.Equal(0, previewJson.RootElement.GetProperty("invalidRows").GetInt32());
            Assert.Contains("line two", previewJson.RootElement.GetProperty("rows")[0].GetProperty("definition").GetString());
            Assert.Contains("quoted", previewJson.RootElement.GetProperty("rows")[0].GetProperty("definition").GetString());
        }

        var unknownTaxonomyCsv = VocabularyImportCsvHeader
            + $"taxonomy-recall-{Guid.NewGuid():N},\"Definition\",\"Example.\",unapproved_bucket,medium,medicine,oet,,,,,,,,\"src=taxonomy;p=1;row=1\"\n";

        using (var previewContent = CsvContent(unknownTaxonomyCsv, "recalls-taxonomy.csv"))
        {
            var preview = await _client.PostAsync("/v1/admin/vocabulary/import/preview", previewContent);
            preview.EnsureSuccessStatusCode();
            using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
            Assert.Equal(0, previewJson.RootElement.GetProperty("validRows").GetInt32());
            Assert.Equal(1, previewJson.RootElement.GetProperty("invalidRows").GetInt32());
            Assert.Contains("Unknown category", previewJson.RootElement.GetProperty("rows")[0].GetProperty("error").GetString());
        }

        var unknownDifficultyCsv = VocabularyImportCsvHeader
            + $"difficulty-recall-{Guid.NewGuid():N},\"Definition\",\"Example.\",medical,experimental,medicine,oet,,,,,,,,\"src=difficulty;p=1;row=1\"\n";

        using (var previewContent = CsvContent(unknownDifficultyCsv, "recalls-difficulty.csv"))
        {
            var preview = await _client.PostAsync("/v1/admin/vocabulary/import/preview", previewContent);
            preview.EnsureSuccessStatusCode();
            using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
            Assert.Equal(0, previewJson.RootElement.GetProperty("validRows").GetInt32());
            Assert.Equal(1, previewJson.RootElement.GetProperty("invalidRows").GetInt32());
            Assert.Contains("Unknown difficulty", previewJson.RootElement.GetProperty("rows")[0].GetProperty("error").GetString());
        }

        var missingSourceCsv = VocabularyImportCsvHeader
            + $"source-recall-{Guid.NewGuid():N},\"Definition\",\"Example.\",medical,medium,medicine,oet,,,,,,,,\n";

        using (var previewContent = CsvContent(missingSourceCsv, "recalls-missing-source.csv"))
        {
            var preview = await _client.PostAsync("/v1/admin/vocabulary/import/preview", previewContent);
            preview.EnsureSuccessStatusCode();
            using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
            Assert.Equal(0, previewJson.RootElement.GetProperty("validRows").GetInt32());
            Assert.Equal(1, previewJson.RootElement.GetProperty("invalidRows").GetInt32());
            Assert.Contains("sourceProvenance", previewJson.RootElement.GetProperty("rows")[0].GetProperty("error").GetString());
        }

        var batchOnlySourceCsv = VocabularyImportCsvHeader
            + $"source-batch-only-{Guid.NewGuid():N},\"Definition\",\"Example.\",medical,medium,medicine,oet,,,,,,,,\"batch=old-batch\"\n";

        using (var previewContent = CsvContent(batchOnlySourceCsv, "recalls-batch-only-source.csv"))
        {
            var preview = await _client.PostAsync("/v1/admin/vocabulary/import/preview", previewContent);
            preview.EnsureSuccessStatusCode();
            using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
            Assert.Equal(0, previewJson.RootElement.GetProperty("validRows").GetInt32());
            Assert.Equal(1, previewJson.RootElement.GetProperty("invalidRows").GetInt32());
            Assert.Contains("compact source pointer", previewJson.RootElement.GetProperty("rows")[0].GetProperty("error").GetString());
        }

        foreach (var genericSource in new[] { "source=admin-vocabulary-import", "source=unknown", "not-source=recalls-src-001", "batch=old-batch;source=admin-vocabulary-import" })
        {
            var genericSourceCsv = VocabularyImportCsvHeader
                + $"generic-source-{Guid.NewGuid():N},\"Definition\",\"Example.\",medical,medium,medicine,oet,,,,,,,,\"{genericSource}\"\n";

            using var previewContent = CsvContent(genericSourceCsv, "recalls-generic-source.csv");
            var preview = await _client.PostAsync("/v1/admin/vocabulary/import/preview", previewContent);
            preview.EnsureSuccessStatusCode();
            using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
            Assert.Equal(0, previewJson.RootElement.GetProperty("validRows").GetInt32());
            Assert.Equal(1, previewJson.RootElement.GetProperty("invalidRows").GetInt32());
            Assert.Contains("compact source pointer", previewJson.RootElement.GetProperty("rows")[0].GetProperty("error").GetString());
        }
    }

    [Fact]
    public async Task AdminVocabularyImport_BatchExportAndRollback_ArchivesDraftRows()
    {
        var term = $"batch-recall-{Guid.NewGuid():N}";
        var batchId = $"recalls-test-{Guid.NewGuid():N}"[..32];
        var csv = "Term,Definition,ExampleSentence,Category,Difficulty,ProfessionId,ExamTypeCode,AmericanSpelling,AudioUrl,AudioSlowUrl,AudioSentenceUrl,AudioMediaAssetId,SynonymsCsv,Collocations,RelatedTerms,SourceProvenance\n"
            + $"{term},\"Batch definition\",\"Batch example sentence.\",medical,medium,medicine,oet,{term}-us,,,,,\"batch synonym one|batch synonym two\",\"batch collocation\",\"batch related\",\"src=unit-test;p=1;row=1\"\n";

        using (var dryRunContent = CsvContent(csv, "recalls-batch.csv"))
        {
            var dryRun = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=true&importBatchId={Uri.EscapeDataString(batchId)}", dryRunContent);
            dryRun.EnsureSuccessStatusCode();
        }

        using (var importContent = CsvContent(csv, "recalls-batch.csv"))
        {
            var import = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=false&importBatchId={Uri.EscapeDataString(batchId)}", importContent);
            import.EnsureSuccessStatusCode();
            using var importJson = JsonDocument.Parse(await import.Content.ReadAsStringAsync());
            Assert.Equal(batchId, importJson.RootElement.GetProperty("importBatchId").GetString());
            Assert.Equal(1, importJson.RootElement.GetProperty("imported").GetInt32());
        }

        var summary = await _client.GetAsync($"/v1/admin/vocabulary/import/batches/{Uri.EscapeDataString(batchId)}");
        summary.EnsureSuccessStatusCode();
        using (var summaryJson = JsonDocument.Parse(await summary.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, summaryJson.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(1, summaryJson.RootElement.GetProperty("draft").GetInt32());
            Assert.Equal(0, summaryJson.RootElement.GetProperty("active").GetInt32());
            Assert.StartsWith($"batch={batchId};", summaryJson.RootElement.GetProperty("rows")[0].GetProperty("sourceProvenance").GetString());
        }

        var export = await _client.GetAsync($"/v1/admin/vocabulary/import/batches/{Uri.EscapeDataString(batchId)}/export");
        export.EnsureSuccessStatusCode();
        var csvExport = await export.Content.ReadAsStringAsync();
        Assert.Contains(batchId, csvExport);
        Assert.Contains(term, csvExport);
        Assert.Contains("batch synonym two", csvExport);
        Assert.Contains("batch collocation", csvExport);

        using (var reconcileContent = CsvContent(csv, "recalls-batch-manifest.csv"))
        {
            var reconcile = await _client.PostAsync($"/v1/admin/vocabulary/import/batches/{Uri.EscapeDataString(batchId)}/reconcile", reconcileContent);
            reconcile.EnsureSuccessStatusCode();
            using var reconcileJson = JsonDocument.Parse(await reconcile.Content.ReadAsStringAsync());
            Assert.True(reconcileJson.RootElement.GetProperty("clean").GetBoolean());
            Assert.Equal(1, reconcileJson.RootElement.GetProperty("matchedRows").GetInt32());
            Assert.Equal(0, reconcileJson.RootElement.GetProperty("mismatchedRows").GetInt32());
        }

        var mismatchedCsv = csv.Replace("Batch definition", "Changed batch definition", StringComparison.Ordinal);
        using (var reconcileContent = CsvContent(mismatchedCsv, "recalls-batch-mismatch.csv"))
        {
            var reconcile = await _client.PostAsync($"/v1/admin/vocabulary/import/batches/{Uri.EscapeDataString(batchId)}/reconcile", reconcileContent);
            reconcile.EnsureSuccessStatusCode();
            using var reconcileJson = JsonDocument.Parse(await reconcile.Content.ReadAsStringAsync());
            Assert.False(reconcileJson.RootElement.GetProperty("clean").GetBoolean());
            Assert.Equal(1, reconcileJson.RootElement.GetProperty("mismatchedRows").GetInt32());
            Assert.Contains(reconcileJson.RootElement.GetProperty("rows").EnumerateArray(), row =>
                row.GetProperty("status").GetString() == "mismatched"
                && row.GetProperty("mismatches").EnumerateArray().Any(m => m.GetProperty("field").GetString() == "definition"));
        }

        var rollback = await _client.PostAsJsonAsync($"/v1/admin/vocabulary/import/batches/{Uri.EscapeDataString(batchId)}/rollback", new { deleteDraftRows = false });
        rollback.EnsureSuccessStatusCode();
        using (var rollbackJson = JsonDocument.Parse(await rollback.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, rollbackJson.RootElement.GetProperty("archived").GetInt32());
            Assert.Equal(0, rollbackJson.RootElement.GetProperty("blocked").GetInt32());
        }

        var afterRollback = await _client.GetAsync($"/v1/admin/vocabulary/import/batches/{Uri.EscapeDataString(batchId)}");
        afterRollback.EnsureSuccessStatusCode();
        using var afterJson = JsonDocument.Parse(await afterRollback.Content.ReadAsStringAsync());
        Assert.Equal(0, afterJson.RootElement.GetProperty("draft").GetInt32());
        Assert.Equal(1, afterJson.RootElement.GetProperty("archived").GetInt32());

        var secondTerm = $"batch-recall-reuse-{Guid.NewGuid():N}";
        var secondCsv = VocabularyImportCsvHeader
            + $"{secondTerm},\"Second definition\",\"Second example.\",medical,medium,medicine,oet,,,,,,,,\"src=unit-test;p=2;row=1\"\n";

        using (var secondDryRunContent = CsvContent(secondCsv, "recalls-batch-reuse.csv"))
        {
            var dryRun = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=true&importBatchId={Uri.EscapeDataString(batchId)}", secondDryRunContent);
            dryRun.EnsureSuccessStatusCode();
        }

        using (var secondImportContent = CsvContent(secondCsv, "recalls-batch-reuse.csv"))
        {
            var secondImport = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=false&importBatchId={Uri.EscapeDataString(batchId)}", secondImportContent);
            Assert.Equal(HttpStatusCode.BadRequest, secondImport.StatusCode);
        }
    }

    [Fact]
    public async Task AdminVocabularyImport_CommitRequiresMatchingDryRun()
    {
        var term = $"commit-gate-{Guid.NewGuid():N}";
        var batchId = $"recalls-gate-{Guid.NewGuid():N}"[..32];
        var csv = VocabularyImportCsvHeader
            + $"{term},\"Definition\",\"Example.\",medical,medium,medicine,oet,,,,,,,,\"src=gate;p=1;row=1\"\n";

        using (var directCommitContent = CsvContent(csv, "recalls-direct.csv"))
        {
            var directCommit = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=false&importBatchId={Uri.EscapeDataString(batchId)}", directCommitContent);
            Assert.Equal(HttpStatusCode.BadRequest, directCommit.StatusCode);
        }

        using (var omittedDryRunContent = CsvContent(csv, "recalls-default.csv"))
        {
            var defaultDryRun = await _client.PostAsync($"/v1/admin/vocabulary/import?importBatchId={Uri.EscapeDataString(batchId)}", omittedDryRunContent);
            defaultDryRun.EnsureSuccessStatusCode();
            using var defaultJson = JsonDocument.Parse(await defaultDryRun.Content.ReadAsStringAsync());
            Assert.Equal(1, defaultJson.RootElement.GetProperty("imported").GetInt32());
        }

        var changedCsv = VocabularyImportCsvHeader
            + $"{term},\"Changed definition\",\"Example.\",medical,medium,medicine,oet,,,,,,,,\"src=gate;p=1;row=1\"\n";
        using (var changedCommitContent = CsvContent(changedCsv, "recalls-changed.csv"))
        {
            var changedCommit = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=false&importBatchId={Uri.EscapeDataString(batchId)}", changedCommitContent);
            Assert.Equal(HttpStatusCode.BadRequest, changedCommit.StatusCode);
        }

        var list = await _client.GetAsync($"/v1/admin/vocabulary/items?search={Uri.EscapeDataString(term)}");
        list.EnsureSuccessStatusCode();
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.DoesNotContain(listJson.RootElement.GetProperty("items").EnumerateArray(), x => x.GetProperty("term").GetString() == term);
    }

    [Fact]
    public async Task AdminVocabularyImport_RollbackBlocksActiveRowsAndNeverDeletes()
    {
        var term = $"active-batch-recall-{Guid.NewGuid():N}";
        var batchId = $"recalls-active-{Guid.NewGuid():N}"[..32];
        var csv = VocabularyImportCsvHeader
            + $"{term},\"Active rollback definition\",\"Active example.\",communication,medium,medicine,oet,,,,,,,,\"src=active;p=1;row=1\"\n";

        using (var dryRunContent = CsvContent(csv, "recalls-active.csv"))
        {
            var dryRun = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=true&importBatchId={Uri.EscapeDataString(batchId)}", dryRunContent);
            dryRun.EnsureSuccessStatusCode();
        }

        using (var importContent = CsvContent(csv, "recalls-active.csv"))
        {
            var import = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=false&importBatchId={Uri.EscapeDataString(batchId)}", importContent);
            import.EnsureSuccessStatusCode();
        }

        var list = await _client.GetAsync($"/v1/admin/vocabulary/items?search={Uri.EscapeDataString(term)}");
        list.EnsureSuccessStatusCode();
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var id = listJson.RootElement.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("term").GetString() == term)
            .GetProperty("id").GetString()!;

        var activate = await _client.PutAsJsonAsync($"/v1/admin/vocabulary/items/{id}", new { status = "active" });
        activate.EnsureSuccessStatusCode();

        var rollback = await _client.PostAsJsonAsync($"/v1/admin/vocabulary/import/batches/{Uri.EscapeDataString(batchId)}/rollback", new { deleteDraftRows = true });
        rollback.EnsureSuccessStatusCode();
        using (var rollbackJson = JsonDocument.Parse(await rollback.Content.ReadAsStringAsync()))
        {
            Assert.Equal(0, rollbackJson.RootElement.GetProperty("deleted").GetInt32());
            Assert.Equal(0, rollbackJson.RootElement.GetProperty("archived").GetInt32());
            Assert.Equal(1, rollbackJson.RootElement.GetProperty("blocked").GetInt32());
        }

        var summary = await _client.GetAsync($"/v1/admin/vocabulary/import/batches/{Uri.EscapeDataString(batchId)}");
        summary.EnsureSuccessStatusCode();
        using var summaryJson = JsonDocument.Parse(await summary.Content.ReadAsStringAsync());
        Assert.Equal(1, summaryJson.RootElement.GetProperty("active").GetInt32());
        Assert.Equal(0, summaryJson.RootElement.GetProperty("archived").GetInt32());
    }

    [Fact]
    public async Task AdminRecallsLegacyBulkUpload_IsDisabledForProductionSafety()
    {
        var response = await _client.PostAsJsonAsync("/v1/admin/recalls/bulk-upload", Array.Empty<object>());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("legacy_recalls_import_disabled", json.RootElement.GetProperty("code").GetString());
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

    private static MultipartFormDataContent CsvContent(string csv, string fileName)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(csv)), "file", fileName);
        return content;
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
