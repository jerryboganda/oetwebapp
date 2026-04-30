using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

// Wave 3 of docs/SPEAKING-MODULE-PLAN.md - integration tests for the
// speaking mock-set orchestrator endpoints and the rolling 7-day
// free-tier cap (Q1 of decisions §6, locked).
public class SpeakingMockSetTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public SpeakingMockSetTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListMockSets_ReturnsPublishedSetsAndEntitlement()
    {
        await EnsureSeedMockSetAsync();
        using var client = await CreateLearnerClientAsync("speaking-mocks-list");

        var response = await client.GetAsync("/v1/speaking/mock-sets");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("mockSets", out var sets));
        Assert.True(sets.GetArrayLength() >= 1);
        var first = sets[0];
        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("mockSetId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("rolePlay1ContentId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("rolePlay2ContentId").GetString()));

        Assert.True(json.RootElement.TryGetProperty("entitlement", out var ent));
        Assert.Equal(7, ent.GetProperty("windowDays").GetInt32());
        Assert.True(ent.GetProperty("cap").GetInt32() >= 0);
    }

    [Fact]
    public async Task StartMockSet_CreatesPairedAttempts_AndSessionTracksBothHalves()
    {
        var mockSetId = await EnsureSeedMockSetAsync();
        using var client = await CreateLearnerClientAsync("speaking-mocks-start");

        var startResponse = await client.PostAsJsonAsync(
            $"/v1/speaking/mock-sets/{mockSetId}/start",
            new { mode = "exam" });
        startResponse.EnsureSuccessStatusCode();

        using var startJson = JsonDocument.Parse(await startResponse.Content.ReadAsStringAsync());
        var sessionId = startJson.RootElement.GetProperty("mockSessionId").GetString()!;
        Assert.Equal("exam", startJson.RootElement.GetProperty("mode").GetString());
        Assert.Equal("inprogress", startJson.RootElement.GetProperty("state").GetString());

        var role1 = startJson.RootElement.GetProperty("rolePlay1");
        var role2 = startJson.RootElement.GetProperty("rolePlay2");
        var attempt1Id = role1.GetProperty("attemptId").GetString()!;
        var attempt2Id = role2.GetProperty("attemptId").GetString()!;
        Assert.NotEqual(attempt1Id, attempt2Id);

        // Both attempts must exist and share the session id as ComparisonGroupId.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var a1 = await db.Attempts.FirstAsync(x => x.Id == attempt1Id);
        var a2 = await db.Attempts.FirstAsync(x => x.Id == attempt2Id);
        Assert.Equal(sessionId, a1.ComparisonGroupId);
        Assert.Equal(sessionId, a2.ComparisonGroupId);
        Assert.Equal("speaking", a1.SubtestCode);
        Assert.Equal("mock_set", a1.Context);
        Assert.Equal("mock_set", a2.Context);

        // Combined block exists and reports nothing scored yet.
        var combined = startJson.RootElement.GetProperty("combined");
        Assert.False(combined.GetProperty("bothCompleted").GetBoolean());
        Assert.Equal(350, combined.GetProperty("passThreshold").GetInt32());

        // GET endpoint round-trips the same data.
        var getResponse = await client.GetAsync($"/v1/speaking/mock-sessions/{sessionId}");
        getResponse.EnsureSuccessStatusCode();
        using var getJson = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        Assert.Equal(sessionId, getJson.RootElement.GetProperty("mockSessionId").GetString());
    }

    [Fact]
    public async Task StartMockSet_FreeTier_SecondCallInWindow_Returns409()
    {
        var mockSetId = await EnsureSeedMockSetAsync();
        const string userId = "speaking-mocks-cap";
        using var client = await CreateLearnerClientAsync(userId);

        // Force this user onto the free plan AND ensure FreeTierConfig exists
        // with the default cap of 1 (the seed value).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var user = await db.Users.FirstAsync(x => x.Id == userId);
            user.CurrentPlanId = "free";

            var config = await db.FreeTierConfigs.FirstOrDefaultAsync();
            if (config is null)
            {
                db.FreeTierConfigs.Add(new FreeTierConfig
                {
                    Id = "FTC-test",
                    Enabled = true,
                    MaxSpeakingMockSets = 1,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
            }
            else
            {
                config.MaxSpeakingMockSets = 1;
            }
            await db.SaveChangesAsync();
        }

        var first = await client.PostAsJsonAsync(
            $"/v1/speaking/mock-sets/{mockSetId}/start",
            new { mode = "exam" });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"/v1/speaking/mock-sets/{mockSetId}/start",
            new { mode = "exam" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.Content.ReadAsStringAsync();
        Assert.Contains("free_tier_speaking_mock_sets_exceeded", problem);
    }

    [Fact]
    public async Task StartMockSet_PaidPlan_BypassesFreeTierCap()
    {
        var mockSetId = await EnsureSeedMockSetAsync();
        const string userId = "speaking-mocks-paid";
        using var client = await CreateLearnerClientAsync(userId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var user = await db.Users.FirstAsync(x => x.Id == userId);
            user.CurrentPlanId = "premium-monthly";

            var config = await db.FreeTierConfigs.FirstOrDefaultAsync();
            if (config is null)
            {
                db.FreeTierConfigs.Add(new FreeTierConfig
                {
                    Id = "FTC-test-paid",
                    Enabled = true,
                    MaxSpeakingMockSets = 1,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
            }
            else
            {
                config.MaxSpeakingMockSets = 1;
            }
            await db.SaveChangesAsync();
        }

        var first = await client.PostAsJsonAsync(
            $"/v1/speaking/mock-sets/{mockSetId}/start",
            new { mode = "exam" });
        first.EnsureSuccessStatusCode();
        var second = await client.PostAsJsonAsync(
            $"/v1/speaking/mock-sets/{mockSetId}/start",
            new { mode = "exam" });
        second.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task StartMockSet_UnpublishedSet_Returns409()
    {
        const string draftId = "sms-test-draft";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            if (!await db.SpeakingMockSets.AnyAsync(x => x.Id == draftId))
            {
                db.SpeakingMockSets.Add(new SpeakingMockSet
                {
                    Id = draftId,
                    Title = "Draft test set",
                    RolePlay1ContentId = "st-001",
                    RolePlay2ContentId = "st-002",
                    Status = SpeakingMockSetStatus.Draft,
                    Difficulty = "core",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
            }
        }

        using var client = await CreateLearnerClientAsync("speaking-mocks-draft");
        var response = await client.PostAsJsonAsync(
            $"/v1/speaking/mock-sets/{draftId}/start",
            new { mode = "exam" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
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

    private async Task<string> EnsureSeedMockSetAsync()
    {
        const string id = "sms-nursing-core-1";
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        if (!await db.SpeakingMockSets.AnyAsync(x => x.Id == id))
        {
            db.SpeakingMockSets.Add(new SpeakingMockSet
            {
                Id = id,
                ProfessionId = "nursing",
                Title = "Nursing Mock Set 1 - Core",
                Description = "Two paired role-plays.",
                RolePlay1ContentId = "st-001",
                RolePlay2ContentId = "st-002",
                Status = SpeakingMockSetStatus.Published,
                Difficulty = "core",
                CriteriaFocus = "informationGiving,relationshipBuilding",
                Tags = "nursing,core",
                SortOrder = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                PublishedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        return id;
    }
}
