using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

[Collection("AuthFlows")]
public class AdminFlowsTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private const string VocabularyImportCsvHeader = "Term,Definition,ExampleSentence,Category,Difficulty,ProfessionId,ExamTypeCode,AmericanSpelling,AudioUrl,AudioSlowUrl,AudioSentenceUrl,AudioMediaAssetId,Collocations,RelatedTerms,SourceProvenance\n";

    private readonly FirstPartyAuthTestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminFlowsTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
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

    [Fact]
    public async Task AdminAIConfig_AllowsDedicatedAiConfigPermission()
    {
        var previous = Environment.GetEnvironmentVariable("Auth__UseDevelopmentAuth");
        Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", "true");
        try
        {
            using var factory = new TestWebApplicationFactory();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
            client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", AdminPermissions.AiConfig);

            var listResponse = await client.GetAsync("/v1/admin/ai-config");
            listResponse.EnsureSuccessStatusCode();

            var createResponse = await client.PostAsJsonAsync("/v1/admin/ai-config", new
            {
                model = "ai-config-rbac-model",
                provider = "digitalocean-serverless",
                taskType = "writing",
                accuracy = 88.0,
                confidenceThreshold = 0.75
            });
            createResponse.EnsureSuccessStatusCode();

            var writingOptionsResponse = await client.GetAsync("/v1/admin/writing/options");
            writingOptionsResponse.EnsureSuccessStatusCode();

            var updateWritingOptionsResponse = await client.PutAsJsonAsync("/v1/admin/writing/options", new
            {
                aiGradingEnabled = true,
                aiCoachEnabled = true,
                killSwitchReason = (string?)null,
                freeTierEnabled = false,
                freeTierLimit = 0,
                freeTierWindowDays = 7
            });
            updateWritingOptionsResponse.EnsureSuccessStatusCode();
        }
        finally
        {
            Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", previous);
        }
    }

    [Theory]
    [InlineData(AdminPermissions.ContentRead)]
    [InlineData(AdminPermissions.ContentWrite)]
    public async Task AdminAIConfig_DoesNotAllowContentPermissionsForCrudRoutes(string permission)
    {
        var previous = Environment.GetEnvironmentVariable("Auth__UseDevelopmentAuth");
        Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", "true");
        try
        {
            using var factory = new TestWebApplicationFactory();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
            client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", permission);

            var requests = new[]
            {
                new HttpRequestMessage(HttpMethod.Get, "/v1/admin/ai-config"),
                new HttpRequestMessage(HttpMethod.Get, "/v1/admin/writing/options"),
                new HttpRequestMessage(HttpMethod.Post, "/v1/admin/ai-config")
                {
                    Content = JsonContent.Create(new
                    {
                        model = "denied-ai-config-model",
                        provider = "digitalocean-serverless",
                        taskType = "writing",
                        accuracy = 88.0,
                        confidenceThreshold = 0.75
                    })
                },
                new HttpRequestMessage(HttpMethod.Put, "/v1/admin/ai-config/missing-config")
                {
                    Content = JsonContent.Create(new
                    {
                        status = "active",
                        accuracy = 91.0
                    })
                },
                new HttpRequestMessage(HttpMethod.Put, "/v1/admin/writing/options")
                {
                    Content = JsonContent.Create(new
                    {
                        aiGradingEnabled = true,
                        aiCoachEnabled = true,
                        killSwitchReason = (string?)null,
                        freeTierEnabled = false,
                        freeTierLimit = 0,
                        freeTierWindowDays = 7
                    })
                },
                new HttpRequestMessage(HttpMethod.Post, "/v1/admin/ai-config/missing-config/activate"),
                new HttpRequestMessage(HttpMethod.Delete, "/v1/admin/ai-config/missing-config")
            };

            foreach (var request in requests)
            {
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", previous);
        }
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
    public async Task AdminUsers_SetPassword_ReplacesCredentialsAndRevokesSessions()
    {
        var detailResponse = await _client.GetAsync("/v1/admin/users/mock-user-001");
        detailResponse.EnsureSuccessStatusCode();

        using var detailJson = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        var authAccountId = detailJson.RootElement.GetProperty("authAccountId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(authAccountId));

        var tokenId = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.RefreshTokenRecords.Add(new RefreshTokenRecord
            {
                Id = tokenId,
                ApplicationUserAccountId = authAccountId!,
                TokenHash = $"hash-{Guid.NewGuid():N}",
                FamilyId = Guid.NewGuid(),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var setPasswordResponse = await _client.PostAsJsonAsync("/v1/admin/users/mock-user-001/password", new
        {
            password = "BetterPassword123!"
        });
        setPasswordResponse.EnsureSuccessStatusCode();

        using var setPasswordJson = JsonDocument.Parse(await setPasswordResponse.Content.ReadAsStringAsync());
        Assert.Equal("mock-user-001", setPasswordJson.RootElement.GetProperty("userId").GetString());
        Assert.Equal(1, setPasswordJson.RootElement.GetProperty("revoked").GetInt32());

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var token = await db.RefreshTokenRecords.SingleAsync(x => x.ApplicationUserAccountId == authAccountId);
            Assert.NotNull(token.RevokedAt);
        }

        var resetResponse = await _client.PostAsJsonAsync("/v1/admin/users/mock-user-001/password", new
        {
            password = SeedData.LocalSeedPassword
        });
        resetResponse.EnsureSuccessStatusCode();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var token = await db.RefreshTokenRecords.SingleAsync(x => x.Id == tokenId);
            db.RefreshTokenRecords.Remove(token);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task AdminUsers_SetPassword_RejectsDeletedAndAuthlessAccounts()
    {
        var deleteResponse = await _client.PostAsJsonAsync("/v1/admin/users/mock-user-001/delete", new
        {
            reason = "testing password set guard"
        });
        deleteResponse.EnsureSuccessStatusCode();

        var deletedPasswordResponse = await _client.PostAsJsonAsync("/v1/admin/users/mock-user-001/password", new
        {
            password = "BetterPassword123!"
        });
        Assert.Equal(HttpStatusCode.BadRequest, deletedPasswordResponse.StatusCode);
        Assert.Equal("account_deleted", await ReadErrorCodeAsync(deletedPasswordResponse));

        var authlessUserId = $"authless-{Guid.NewGuid():N}";
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.Users.Add(new LearnerUser
            {
                Id = authlessUserId,
                Role = ApplicationUserRoles.Learner,
                DisplayName = "Authless User",
                Email = $"authless-{Guid.NewGuid():N}@example.test",
                Timezone = "UTC",
                Locale = "en-AU",
                ActiveProfessionId = "medicine",
                CreatedAt = DateTimeOffset.UtcNow,
                LastActiveAt = DateTimeOffset.UtcNow,
                AccountStatus = "active"
            });
            await db.SaveChangesAsync();
        }

        var authlessPasswordResponse = await _client.PostAsJsonAsync($"/v1/admin/users/{authlessUserId}/password", new
        {
            password = "BetterPassword123!"
        });
        Assert.Equal(HttpStatusCode.BadRequest, authlessPasswordResponse.StatusCode);
        Assert.Equal("password_set_unavailable", await ReadErrorCodeAsync(authlessPasswordResponse));

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var authlessUser = await db.Users.SingleAsync(x => x.Id == authlessUserId);
            db.Users.Remove(authlessUser);
            await db.SaveChangesAsync();
        }

        var restoreResponse = await _client.PostAsJsonAsync("/v1/admin/users/mock-user-001/restore", new
        {
            reason = "restore seeded learner after password-set guard test"
        });
        restoreResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AdminUsers_SetPassword_RejectsWeakPasswords()
    {
        var response = await _client.PostAsJsonAsync("/v1/admin/users/mock-user-001/password", new
        {
            password = "short"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("password_too_short", await ReadErrorCodeAsync(response));
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

    [Fact]
    public async Task AdminUsers_UpdateProfile_PersistsEditableFieldsAndKeepsEmailLocked()
    {
        var beforeResponse = await _client.GetAsync("/v1/admin/users/mock-user-001");
        beforeResponse.EnsureSuccessStatusCode();
        using var beforeJson = JsonDocument.Parse(await beforeResponse.Content.ReadAsStringAsync());
        var originalEmail = beforeJson.RootElement.GetProperty("email").GetString();

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var updateResponse = await _client.PutAsJsonAsync("/v1/admin/users/mock-user-001/profile", new
        {
            displayName = $"Edited Learner {suffix}",
            firstName = $"First{suffix}",
            lastName = $"Last{suffix}",
            mobileNumber = "+44 7700 900111",
            professionId = "medicine",
            examTypeId = "oet",
            countryTarget = "UK",
            timezone = "Europe/London",
            locale = "en-GB",
            marketingOptIn = true,
            agreeToTerms = true,
            agreeToPrivacy = true,
            email = "attacker@example.com", // must be ignored — email is immutable
            reason = "admin profile edit test"
        });
        updateResponse.EnsureSuccessStatusCode();

        using var updateJson = JsonDocument.Parse(await updateResponse.Content.ReadAsStringAsync());
        Assert.True(updateJson.RootElement.GetProperty("updated").GetBoolean());

        var afterResponse = await _client.GetAsync("/v1/admin/users/mock-user-001");
        afterResponse.EnsureSuccessStatusCode();
        using var afterJson = JsonDocument.Parse(await afterResponse.Content.ReadAsStringAsync());
        var after = afterJson.RootElement;

        Assert.Equal($"Edited Learner {suffix}", after.GetProperty("displayName").GetString());
        Assert.Equal($"First{suffix}", after.GetProperty("firstName").GetString());
        Assert.Equal($"Last{suffix}", after.GetProperty("lastName").GetString());
        Assert.Equal("+44 7700 900111", after.GetProperty("mobileNumber").GetString());
        Assert.Equal("medicine", after.GetProperty("professionId").GetString());
        Assert.Equal("oet", after.GetProperty("examTypeId").GetString());
        Assert.Equal("United Kingdom", after.GetProperty("countryTarget").GetString());
        Assert.Equal("Europe/London", after.GetProperty("timezone").GetString());
        Assert.Equal("en-GB", after.GetProperty("locale").GetString());
        Assert.True(after.GetProperty("marketingOptIn").GetBoolean());

        // Email is the immutable identity and must never change, even when supplied in the payload.
        Assert.Equal(originalEmail, after.GetProperty("email").GetString());
        Assert.NotEqual("attacker@example.com", after.GetProperty("email").GetString());
    }

    [Fact]
    public async Task AdminUsers_UpdateProfile_RejectsUnknownProfession()
    {
        var response = await _client.PutAsJsonAsync("/v1/admin/users/mock-user-001/profile", new
        {
            professionId = "not-a-real-profession"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_profession", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task AdminUsers_UpdateProfile_RejectsProfessionExamMismatch()
    {
        var response = await _client.PutAsJsonAsync("/v1/admin/users/mock-user-001/profile", new
        {
            professionId = "nursing",
            examTypeId = "ielts"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("profession_exam_mismatch", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task AdminUsers_UpdateProfile_RejectsProfessionOnlyCountryMismatch()
    {
        var countryResponse = await _client.PutAsJsonAsync("/v1/admin/users/mock-user-001/profile", new
        {
            countryTarget = "United Kingdom"
        });
        countryResponse.EnsureSuccessStatusCode();

        var professionId = $"restricted-{Guid.NewGuid():N}"[..24];
        var createProfessionResponse = await _client.PostAsJsonAsync("/v1/admin/signup-catalog/professions", new
        {
            id = professionId,
            label = "Restricted Country Profession",
            description = "Only available for Australia in this contract test.",
            examTypeIds = new[] { "oet" },
            countryTargets = new[] { "Australia" },
            sortOrder = 99,
            isActive = true
        });
        createProfessionResponse.EnsureSuccessStatusCode();

        var response = await _client.PutAsJsonAsync("/v1/admin/users/mock-user-001/profile", new
        {
            professionId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("profession_country_mismatch", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task AdminUsers_UpdateProfile_RejectsUnsupportedTargetCountry()
    {
        var response = await _client.PutAsJsonAsync("/v1/admin/users/mock-user-001/profile", new
        {
            countryTarget = "Atlantis"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("country_target_invalid", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task AdminUsers_UpdateProfile_RequiresTargetCountryBeforeCreatingRegistrationProfile()
    {
        var email = $"invited-learner-{Guid.NewGuid():N}@oet-prep.dev";
        var inviteResponse = await _client.PostAsJsonAsync("/v1/admin/users/invite", new
        {
            name = "Invited Learner",
            email,
            role = "learner",
            professionId = "nursing"
        });
        inviteResponse.EnsureSuccessStatusCode();

        using var inviteJson = JsonDocument.Parse(await inviteResponse.Content.ReadAsStringAsync());
        var userId = inviteJson.RootElement.GetProperty("id").GetString()!;

        var response = await _client.PutAsJsonAsync($"/v1/admin/users/{userId}/profile", new
        {
            firstName = "Invited"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("country_target_required", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task AdminUsers_UpdateProfile_ReturnsNotFoundForUnknownUser()
    {
        var response = await _client.PutAsJsonAsync($"/v1/admin/users/missing-{Guid.NewGuid():N}/profile", new
        {
            displayName = "Nobody"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
            var preview = await _client.PostAsync($"/v1/admin/vocabulary/import/preview?recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", previewContent);
            preview.EnsureSuccessStatusCode();
            using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
            Assert.Equal(batchId, previewJson.RootElement.GetProperty("importBatchId").GetString());
            Assert.Equal(1, previewJson.RootElement.GetProperty("validRows").GetInt32());
            Assert.Equal(0, previewJson.RootElement.GetProperty("invalidRows").GetInt32());
            Assert.Equal(0, previewJson.RootElement.GetProperty("duplicateRows").GetInt32());
        }

        using (var dryRunContent = CsvContent(csv, "recalls.csv"))
        {
            var dryRun = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=true&recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", dryRunContent);
            dryRun.EnsureSuccessStatusCode();
            using var dryRunJson = JsonDocument.Parse(await dryRun.Content.ReadAsStringAsync());
            Assert.Equal(batchId, dryRunJson.RootElement.GetProperty("importBatchId").GetString());
            Assert.Equal(1, dryRunJson.RootElement.GetProperty("imported").GetInt32());
            Assert.Equal(0, dryRunJson.RootElement.GetProperty("skipped").GetInt32());
            Assert.Equal(0, dryRunJson.RootElement.GetProperty("failedRows").GetInt32());
        }

        using (var importContent = CsvContent(csv, "recalls.csv"))
        {
            var import = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=false&recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", importContent);
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
            var preview = await _client.PostAsync("/v1/admin/vocabulary/import/preview?recallSetCode=old", conflictPreviewContent);
            preview.EnsureSuccessStatusCode();
            using var conflictJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
            Assert.Equal(0, conflictJson.RootElement.GetProperty("validRows").GetInt32());
            Assert.Equal(0, conflictJson.RootElement.GetProperty("invalidRows").GetInt32());
            Assert.Equal(1, conflictJson.RootElement.GetProperty("duplicateRows").GetInt32());
            Assert.Contains("duplicate-in-db", conflictJson.RootElement.GetProperty("rows")[0].GetProperty("error").GetString());
        }

        using (var conflictDryRunContent = CsvContent(conflictCsv, "recalls-conflict.csv"))
        {
            var dryRun = await _client.PostAsync("/v1/admin/vocabulary/import?dryRun=true&recallSetCode=old", conflictDryRunContent);
            dryRun.EnsureSuccessStatusCode();
            using var conflictDryRunJson = JsonDocument.Parse(await dryRun.Content.ReadAsStringAsync());
            Assert.Equal(0, conflictDryRunJson.RootElement.GetProperty("imported").GetInt32());
            Assert.Equal(1, conflictDryRunJson.RootElement.GetProperty("skipped").GetInt32());
            Assert.Equal(1, conflictDryRunJson.RootElement.GetProperty("duplicates").GetInt32());
        }
    }

    [Fact]
    public async Task AdminVocabularyImport_InCsvDuplicates_AccumulateExamFrequencyCountIdempotently()
    {
        // The same recall term repeated 3× in one CSV must surface as a single
        // term whose ExamFrequencyCount == 3 (drives the learner "×N" tag).
        var term = $"freq-recall-{Guid.NewGuid():N}";
        var batchId = $"recalls-freq-{Guid.NewGuid():N}"[..32];
        var csv = VocabularyImportCsvHeader
            + $"{term},\"Def 1\",\"Example one.\",medical,medium,medicine,oet,,,,,,,,\"src=unit-test;p=1;row=1\"\n"
            + $"{term},\"Def 2\",\"Example two.\",medical,medium,medicine,oet,,,,,,,,\"src=unit-test;p=1;row=2\"\n"
            + $"{term},\"Def 3\",\"Example three.\",medical,medium,medicine,oet,,,,,,,,\"src=unit-test;p=1;row=3\"\n";

        using (var previewContent = CsvContent(csv, "recalls-freq.csv"))
        {
            var preview = await _client.PostAsync($"/v1/admin/vocabulary/import/preview?recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", previewContent);
            preview.EnsureSuccessStatusCode();
            using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
            // One unique term, two in-CSV duplicates — duplicates never block.
            Assert.Equal(1, previewJson.RootElement.GetProperty("validRows").GetInt32());
            Assert.Equal(0, previewJson.RootElement.GetProperty("invalidRows").GetInt32());
            Assert.Equal(2, previewJson.RootElement.GetProperty("duplicateRows").GetInt32());
        }

        using (var dryRunContent = CsvContent(csv, "recalls-freq.csv"))
        {
            var dryRun = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=true&recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", dryRunContent);
            dryRun.EnsureSuccessStatusCode();
            using var dryRunJson = JsonDocument.Parse(await dryRun.Content.ReadAsStringAsync());
            Assert.Equal(1, dryRunJson.RootElement.GetProperty("imported").GetInt32());
            Assert.Equal(2, dryRunJson.RootElement.GetProperty("skipped").GetInt32());
            Assert.Equal(0, dryRunJson.RootElement.GetProperty("failedRows").GetInt32());
        }

        using (var importContent = CsvContent(csv, "recalls-freq.csv"))
        {
            var import = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=false&recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", importContent);
            import.EnsureSuccessStatusCode();
            using var importJson = JsonDocument.Parse(await import.Content.ReadAsStringAsync());
            Assert.Equal(1, importJson.RootElement.GetProperty("imported").GetInt32());
            Assert.Equal(2, importJson.RootElement.GetProperty("skipped").GetInt32());
        }

        async Task<int> ReadFrequencyAsync()
        {
            var list = await _client.GetAsync($"/v1/admin/vocabulary/items?search={Uri.EscapeDataString(term)}");
            list.EnsureSuccessStatusCode();
            using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            var item = listJson.RootElement.GetProperty("items").EnumerateArray()
                .Single(x => x.GetProperty("term").GetString() == term);
            return item.GetProperty("examFrequencyCount").GetInt32();
        }

        Assert.Equal(3, await ReadFrequencyAsync());

        // Re-committing the same batch id is rejected by the commit ledger, so
        // the frequency stays at 3 — the count is idempotent on retry.
        using (var replayContent = CsvContent(csv, "recalls-freq.csv"))
        {
            var replay = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=false&recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", replayContent);
            Assert.False(replay.IsSuccessStatusCode);
        }

        Assert.Equal(3, await ReadFrequencyAsync());
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
            var preview = await _client.PostAsync("/v1/admin/vocabulary/import/preview?recallSetCode=old", previewContent);
            preview.EnsureSuccessStatusCode();
            using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
            Assert.Equal(1, previewJson.RootElement.GetProperty("validRows").GetInt32());
            Assert.Equal(0, previewJson.RootElement.GetProperty("invalidRows").GetInt32());
            Assert.Equal(1, previewJson.RootElement.GetProperty("duplicateRows").GetInt32());
        }

        using (var dryRunContent = CsvContent(duplicateCsv, "recalls-duplicates.csv"))
        {
            var dryRun = await _client.PostAsync("/v1/admin/vocabulary/import?dryRun=true&recallSetCode=old", dryRunContent);
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
            var preview = await _client.PostAsync("/v1/admin/vocabulary/import/preview?recallSetCode=old", previewContent);
            preview.EnsureSuccessStatusCode();
            using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
            Assert.Equal(0, previewJson.RootElement.GetProperty("validRows").GetInt32());
            Assert.Equal(1, previewJson.RootElement.GetProperty("invalidRows").GetInt32());
            Assert.Contains("Category exceeds", previewJson.RootElement.GetProperty("rows")[0].GetProperty("error").GetString());
        }

        using (var dryRunContent = CsvContent(overLimitCsv, "recalls-over-limit.csv"))
        {
            var dryRun = await _client.PostAsync("/v1/admin/vocabulary/import?dryRun=true&recallSetCode=old", dryRunContent);
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
            var preview = await _client.PostAsync("/v1/admin/vocabulary/import/preview?recallSetCode=old", previewContent);
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
            var preview = await _client.PostAsync("/v1/admin/vocabulary/import/preview?recallSetCode=old", previewContent);
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
            var preview = await _client.PostAsync("/v1/admin/vocabulary/import/preview?recallSetCode=old", previewContent);
            preview.EnsureSuccessStatusCode();
            using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
            Assert.Equal(1, previewJson.RootElement.GetProperty("validRows").GetInt32());
            Assert.Equal(0, previewJson.RootElement.GetProperty("invalidRows").GetInt32());
        }

        var missingSourceCsv = VocabularyImportCsvHeader
            + $"source-recall-{Guid.NewGuid():N},\"Definition\",\"Example.\",medical,medium,medicine,oet,,,,,,,,\n";

        // SourceProvenance is now optional on import (Phase 1 of header-less
        // CSV support). Missing provenance must validate cleanly because the
        // import service auto-stamps a batch-derived source-pointer via
        // BuildBatchSourceProvenance(null, batchId).
        using (var previewContent = CsvContent(missingSourceCsv, "recalls-missing-source.csv"))
        {
            var preview = await _client.PostAsync("/v1/admin/vocabulary/import/preview?recallSetCode=old", previewContent);
            preview.EnsureSuccessStatusCode();
            using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
            Assert.Equal(1, previewJson.RootElement.GetProperty("validRows").GetInt32());
            Assert.Equal(0, previewJson.RootElement.GetProperty("invalidRows").GetInt32());
        }

        var batchOnlySourceCsv = VocabularyImportCsvHeader
            + $"source-batch-only-{Guid.NewGuid():N},\"Definition\",\"Example.\",medical,medium,medicine,oet,,,,,,,,\"batch=old-batch\"\n";

        using (var previewContent = CsvContent(batchOnlySourceCsv, "recalls-batch-only-source.csv"))
        {
            var preview = await _client.PostAsync("/v1/admin/vocabulary/import/preview?recallSetCode=old", previewContent);
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
            var preview = await _client.PostAsync("/v1/admin/vocabulary/import/preview?recallSetCode=old", previewContent);
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
            var dryRun = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=true&recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", dryRunContent);
            dryRun.EnsureSuccessStatusCode();
        }

        using (var importContent = CsvContent(csv, "recalls-batch.csv"))
        {
            var import = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=false&recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", importContent);
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
            var dryRun = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=true&recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", secondDryRunContent);
            dryRun.EnsureSuccessStatusCode();
        }

        using (var secondImportContent = CsvContent(secondCsv, "recalls-batch-reuse.csv"))
        {
            var secondImport = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=false&recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", secondImportContent);
            Assert.Equal(HttpStatusCode.BadRequest, secondImport.StatusCode);
        }
    }

    [Fact]
    public async Task AdminVocabularyImport_RecallAudioBackfillRequiresElevenLabsKeyAndCanCancelBatch()
    {
        var term = $"recall-audio-{Guid.NewGuid():N}";
        var batchId = $"recalls-audio-{Guid.NewGuid():N}"[..32];
        var csv = VocabularyImportCsvHeader
            + $"{term},\"Audio definition\",\"Audio example sentence.\",medical,medium,medicine,oet,,,,,,,,\"src=unit-test;p=3;row=1\"\n";

        using (var dryRunContent = CsvContent(csv, "recalls-audio.csv"))
        {
            var dryRun = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=true&recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", dryRunContent);
            dryRun.EnsureSuccessStatusCode();
        }

        using (var importContent = CsvContent(csv, "recalls-audio.csv"))
        {
            var import = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=false&recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", importContent);
            import.EnsureSuccessStatusCode();
        }

        var backfill = await _client.PostAsync($"/v1/admin/vocabulary/audio/backfill?batchId={Uri.EscapeDataString(batchId)}", null);
        Assert.Equal(HttpStatusCode.BadRequest, backfill.StatusCode);
        using (var errorJson = JsonDocument.Parse(await backfill.Content.ReadAsStringAsync()))
        {
            Assert.Equal("elevenlabs_api_key_required", errorJson.RootElement.GetProperty("code").GetString());
        }

        var audioBatchId = $"recall:{batchId}";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.AudioRegenerationBatches.Add(new AudioRegenerationBatch
            {
                Id = audioBatchId,
                AudioType = "recalls",
                Scope = "missing",
                Status = "running",
                TotalItems = 1,
                CompletedItems = 0,
                FailedItems = 0,
                VoiceId = "voice-test",
                ModelVariant = "eleven_multilingual_v2",
                ProviderName = "elevenlabs",
                RequestedBy = "unit-test",
                StartedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var cancel = await _client.PostAsync($"/v1/admin/vocabulary/import/batches/{Uri.EscapeDataString(batchId)}/audio/cancel", null);
        cancel.EnsureSuccessStatusCode();
        using (var cancelJson = JsonDocument.Parse(await cancel.Content.ReadAsStringAsync()))
        {
            Assert.True(cancelJson.RootElement.GetProperty("cancelled").GetBoolean());
            Assert.Equal(audioBatchId, cancelJson.RootElement.GetProperty("audioBatchId").GetString());
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var batch = await db.AudioRegenerationBatches.FindAsync(audioBatchId);
            Assert.NotNull(batch);
            Assert.Equal("cancelled", batch!.Status);
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
            var directCommit = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=false&recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", directCommitContent);
            Assert.Equal(HttpStatusCode.BadRequest, directCommit.StatusCode);
        }

        using (var omittedDryRunContent = CsvContent(csv, "recalls-default.csv"))
        {
            var defaultDryRun = await _client.PostAsync($"/v1/admin/vocabulary/import?recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", omittedDryRunContent);
            defaultDryRun.EnsureSuccessStatusCode();
            using var defaultJson = JsonDocument.Parse(await defaultDryRun.Content.ReadAsStringAsync());
            Assert.Equal(1, defaultJson.RootElement.GetProperty("imported").GetInt32());
        }

        var changedCsv = VocabularyImportCsvHeader
            + $"{term},\"Changed definition\",\"Example.\",medical,medium,medicine,oet,,,,,,,,\"src=gate;p=1;row=1\"\n";
        using (var changedCommitContent = CsvContent(changedCsv, "recalls-changed.csv"))
        {
            var changedCommit = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=false&recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", changedCommitContent);
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
            var dryRun = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=true&recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", dryRunContent);
            dryRun.EnsureSuccessStatusCode();
        }

        using (var importContent = CsvContent(csv, "recalls-active.csv"))
        {
            var import = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=false&recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", importContent);
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

    // ── Phase 1: flexible vocabulary CSV import ────────────────────────────

    [Fact]
    public async Task AdminVocabularyImport_HeaderlessTermOnlyCsv_PreviewSucceeds()
    {
        var t1 = $"aneurysm-{Guid.NewGuid():N}";
        var t2 = $"bank-manager-{Guid.NewGuid():N}";
        var t3 = $"co-codamol-{Guid.NewGuid():N}";
        var csv = $"{t1}\n{t2}\n{t3}\n";

        using var content = CsvContent(csv, "headerless.csv");
        var response = await _client.PostAsync("/v1/admin/vocabulary/import/preview?recallSetCode=old", content);
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(3, json.RootElement.GetProperty("validRows").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("invalidRows").GetInt32());

        var terms = json.RootElement.GetProperty("rows").EnumerateArray()
            .Select(r => r.GetProperty("term").GetString())
            .ToList();
        Assert.Contains(t1, terms);
        Assert.Contains(t2, terms);
        Assert.Contains(t3, terms);
    }

    [Fact]
    public async Task AdminVocabularyImport_HeaderedFullCsv_StillSucceeds()
    {
        var term = $"headered-regression-{Guid.NewGuid():N}";
        var csv = VocabularyImportCsvHeader
            + $"{term},\"Headered definition\",\"Headered example.\",medical,medium,medicine,oet,,,,,,,,\"src=verified-source;p=1;row=1\"\n";

        using var content = CsvContent(csv, "headered.csv");
        var response = await _client.PostAsync("/v1/admin/vocabulary/import/preview?recallSetCode=old", content);
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, json.RootElement.GetProperty("validRows").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("invalidRows").GetInt32());
    }

    [Fact]
    public async Task AdminVocabularyImport_HeaderlessCsv_StripsBomAndCrlf()
    {
        var t1 = $"bom-row-{Guid.NewGuid():N}";
        var t2 = $"bom-row-{Guid.NewGuid():N}";
        var t3 = $"bom-row-{Guid.NewGuid():N}";
        var csv = "\uFEFF" + $"{t1}\r\n{t2}\r\n{t3}\r\n";

        using var content = CsvContent(csv, "bom.csv");
        var response = await _client.PostAsync("/v1/admin/vocabulary/import/preview?recallSetCode=old", content);
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(3, json.RootElement.GetProperty("validRows").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("invalidRows").GetInt32());

        var terms = json.RootElement.GetProperty("rows").EnumerateArray()
            .Select(r => r.GetProperty("term").GetString())
            .ToList();
        Assert.DoesNotContain(terms, term => term is not null && term.StartsWith('\uFEFF'));
        Assert.Contains(t1, terms);
    }

    [Fact]
    public async Task AdminVocabularyImport_HeaderlessCsv_InCsvDuplicates_DedupedWithWarning()
    {
        var t1 = $"dup-a-{Guid.NewGuid():N}";
        var t2 = $"dup-b-{Guid.NewGuid():N}";
        var t3 = $"dup-c-{Guid.NewGuid():N}";
        // 5 rows: t1, t2, t1 (dup of line 1), t3, t2 (dup of line 2) → 3 unique, 2 in-csv dups
        var csv = $"{t1}\n{t2}\n{t1}\n{t3}\n{t2}\n";
        var batchId = Guid.NewGuid().ToString("N");

        using (var previewContent = CsvContent(csv, "dups.csv"))
        {
            var preview = await _client.PostAsync($"/v1/admin/vocabulary/import/preview?recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", previewContent);
            preview.EnsureSuccessStatusCode();
            using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
            Assert.Equal(3, previewJson.RootElement.GetProperty("validRows").GetInt32());
            // duplicateRows counts in-csv dups
            Assert.Equal(2, previewJson.RootElement.GetProperty("duplicateRows").GetInt32());
            var warnings = previewJson.RootElement.GetProperty("warnings").EnumerateArray()
                .Select(w => w.GetString() ?? "")
                .Where(w => w.Contains("duplicate-in-csv"))
                .ToList();
            Assert.Equal(2, warnings.Count);
        }

        using (var dryRunContent = CsvContent(csv, "dups.csv"))
        {
            var dryRun = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=true&recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", dryRunContent);
            dryRun.EnsureSuccessStatusCode();
            using var dryRunJson = JsonDocument.Parse(await dryRun.Content.ReadAsStringAsync());
            Assert.Equal(3, dryRunJson.RootElement.GetProperty("imported").GetInt32());
            Assert.Equal(2, dryRunJson.RootElement.GetProperty("duplicates").GetInt32());
            Assert.Equal(0, dryRunJson.RootElement.GetProperty("failedRows").GetInt32());
        }

        using (var commitContent = CsvContent(csv, "dups.csv"))
        {
            var commit = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=false&recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", commitContent);
            commit.EnsureSuccessStatusCode();
            using var commitJson = JsonDocument.Parse(await commit.Content.ReadAsStringAsync());
            Assert.Equal(3, commitJson.RootElement.GetProperty("imported").GetInt32());
            Assert.Equal(2, commitJson.RootElement.GetProperty("duplicates").GetInt32());
            var errors = commitJson.RootElement.GetProperty("errors").EnumerateArray()
                .Select(e => e.GetString() ?? "")
                .Where(e => e.Contains("duplicate-in-csv"))
                .ToList();
            Assert.Equal(2, errors.Count);
        }
    }

    [Fact]
    public async Task AdminVocabularyImport_ImportedTermsLandAsDraft_AndCanBePublishedWithoutOptionalFields()
    {
        var term = $"draft-gate-{Guid.NewGuid():N}";
        var csv = $"{term}\n";
        var batchId = Guid.NewGuid().ToString("N");

        using (var dryRunContent = CsvContent(csv, "draft.csv"))
        {
            var dryRun = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=true&recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", dryRunContent);
            dryRun.EnsureSuccessStatusCode();
        }

        using (var commitContent = CsvContent(csv, "draft.csv"))
        {
            var commit = await _client.PostAsync($"/v1/admin/vocabulary/import?dryRun=false&recallSetCode=old&importBatchId={Uri.EscapeDataString(batchId)}", commitContent);
            commit.EnsureSuccessStatusCode();
            using var commitJson = JsonDocument.Parse(await commit.Content.ReadAsStringAsync());
            Assert.Equal(1, commitJson.RootElement.GetProperty("imported").GetInt32());
        }

        var list = await _client.GetAsync($"/v1/admin/vocabulary/items?search={Uri.EscapeDataString(term)}");
        list.EnsureSuccessStatusCode();
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var item = listJson.RootElement.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("term").GetString() == term);
        Assert.Equal("draft", item.GetProperty("status").GetString());
        var id = item.GetProperty("id").GetString()!;

        // Publish (status=active) without supplying optional fields → now allowed.
        // All metadata fields are optional; only a non-empty term is required.
        var publishBody = JsonContent.Create(new { status = "active" });
        var publish = await _client.PutAsync($"/v1/admin/vocabulary/items/{id}", publishBody);
        publish.EnsureSuccessStatusCode();

        var listAfter = await _client.GetAsync($"/v1/admin/vocabulary/items?search={Uri.EscapeDataString(term)}");
        listAfter.EnsureSuccessStatusCode();
        using var listAfterJson = JsonDocument.Parse(await listAfter.Content.ReadAsStringAsync());
        var itemAfter = listAfterJson.RootElement.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("term").GetString() == term);
        Assert.Equal("active", itemAfter.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AdminVocabularyImport_TermOnlyRow_ValidatesAsDraft()
    {
        var term = $"validate-draft-{Guid.NewGuid():N}";
        var csv = $"{term}\n";

        using var content = CsvContent(csv, "single.csv");
        var response = await _client.PostAsync("/v1/admin/vocabulary/import/preview?recallSetCode=old", content);
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, json.RootElement.GetProperty("validRows").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("invalidRows").GetInt32());
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
