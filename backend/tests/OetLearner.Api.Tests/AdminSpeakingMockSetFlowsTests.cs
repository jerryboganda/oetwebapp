using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

// Wave 3 of docs/SPEAKING-MODULE-PLAN.md - admin CRUD round-trip for
// `SpeakingMockSet`. Mirrors `AdminFlowsTests.AdminContent_*` patterns.
[Collection("AuthFlows")]
public class AdminSpeakingMockSetFlowsTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminSpeakingMockSetFlowsTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");
    }

    [Fact]
    public async Task List_ReturnsSeededMockSet()
    {
        var response = await _client.GetAsync("/v1/admin/speaking/mock-sets");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var sets = json.RootElement.GetProperty("mockSets");
        Assert.True(sets.GetArrayLength() >= 1);
        var first = sets[0];
        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("mockSetId").GetString()));
        Assert.True(first.GetProperty("rolePlay1").GetProperty("isSpeaking").GetBoolean());
        Assert.True(first.GetProperty("rolePlay2").GetProperty("isSpeaking").GetBoolean());
    }

    [Fact]
    public async Task Create_ThenUpdate_ThenPublish_RoundTrips()
    {
        var createResponse = await _client.PostAsJsonAsync("/v1/admin/speaking/mock-sets", new
        {
            title = "Admin CRUD test mock set",
            rolePlay1ContentId = "st-001",
            rolePlay2ContentId = "st-002",
            professionId = "nursing",
            description = "Round-trip test",
            difficulty = "core",
            criteriaFocus = "informationGiving, relationshipBuilding",
            tags = "nursing, test",
            sortOrder = 99,
        });
        createResponse.EnsureSuccessStatusCode();
        using var createJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var mockSetId = createJson.RootElement.GetProperty("mockSetId").GetString()!;
        Assert.Equal("draft", createJson.RootElement.GetProperty("status").GetString());

        var updateResponse = await _client.PutAsJsonAsync($"/v1/admin/speaking/mock-sets/{mockSetId}", new
        {
            title = "Admin CRUD test mock set v2",
            description = "Updated",
            sortOrder = 5,
        });
        updateResponse.EnsureSuccessStatusCode();
        using var updateJson = JsonDocument.Parse(await updateResponse.Content.ReadAsStringAsync());
        Assert.Equal("Admin CRUD test mock set v2", updateJson.RootElement.GetProperty("title").GetString());
        Assert.Equal(5, updateJson.RootElement.GetProperty("sortOrder").GetInt32());

        var publishResponse = await _client.PostAsync($"/v1/admin/speaking/mock-sets/{mockSetId}/publish", null);
        publishResponse.EnsureSuccessStatusCode();
        using var publishJson = JsonDocument.Parse(await publishResponse.Content.ReadAsStringAsync());
        Assert.Equal("published", publishJson.RootElement.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(publishJson.RootElement.GetProperty("publishedAt").GetString()));
    }

    [Fact]
    public async Task Create_RejectsDuplicateRolePlays()
    {
        var response = await _client.PostAsJsonAsync("/v1/admin/speaking/mock-sets", new
        {
            title = "Duplicate role-plays",
            rolePlay1ContentId = "st-001",
            rolePlay2ContentId = "st-001",
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("SPEAKING_MOCK_SET_DUPLICATE_ROLE_PLAYS", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Create_RejectsNonSpeakingContent()
    {
        var nonSpeakingId = $"wt-speaking-mock-reject-{Guid.NewGuid():N}";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.ContentItems.Add(new ContentItem
            {
                Id = nonSpeakingId,
                ContentType = "writing_task",
                SubtestCode = "writing",
                ProfessionId = "nursing",
                Title = "Published writing task",
                Difficulty = "core",
                EstimatedDurationMinutes = 45,
                PublishedRevisionId = $"{nonSpeakingId}-r1",
                Status = ContentStatus.Published,
                CaseNotes = "A published writing item should still be rejected for speaking mock sets.",
                DetailJson = "{}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync("/v1/admin/speaking/mock-sets", new
        {
            title = "Mixed subtest mock set",
            rolePlay1ContentId = "st-001",
            rolePlay2ContentId = nonSpeakingId,
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("SPEAKING_MOCK_SET_NOT_SPEAKING", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Create_RejectsDraftSpeakingContent()
    {
        var draftId = $"st-draft-{Guid.NewGuid():N}";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.ContentItems.Add(new ContentItem
            {
                Id = draftId,
                ContentType = "speaking_task",
                SubtestCode = "speaking",
                ProfessionId = "nursing",
                Title = "Draft speaking card",
                Difficulty = "core",
                EstimatedDurationMinutes = 20,
                PublishedRevisionId = $"{draftId}-r1",
                Status = ContentStatus.Draft,
                CaseNotes = "Draft case notes should not be exposed to learners.",
                DetailJson = JsonSupport.Serialize(new
                {
                    setting = "Clinic",
                    patient = "Patient",
                    brief = "Explain the care plan.",
                    background = "Draft background"
                }),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync("/v1/admin/speaking/mock-sets", new
        {
            title = "Draft content mock set",
            rolePlay1ContentId = "st-001",
            rolePlay2ContentId = draftId,
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("SPEAKING_MOCK_SET_CONTENT_NOT_PUBLISHED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Archive_BlocksFurtherEdits()
    {
        var createResponse = await _client.PostAsJsonAsync("/v1/admin/speaking/mock-sets", new
        {
            title = "Archive test",
            rolePlay1ContentId = "st-001",
            rolePlay2ContentId = "st-002",
        });
        createResponse.EnsureSuccessStatusCode();
        using var createJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var mockSetId = createJson.RootElement.GetProperty("mockSetId").GetString()!;

        var archiveResponse = await _client.PostAsync($"/v1/admin/speaking/mock-sets/{mockSetId}/archive", null);
        archiveResponse.EnsureSuccessStatusCode();
        using var archiveJson = JsonDocument.Parse(await archiveResponse.Content.ReadAsStringAsync());
        Assert.Equal("archived", archiveJson.RootElement.GetProperty("status").GetString());

        var updateResponse = await _client.PutAsJsonAsync($"/v1/admin/speaking/mock-sets/{mockSetId}", new
        {
            title = "Should fail",
        });
        Assert.Equal(HttpStatusCode.Conflict, updateResponse.StatusCode);
    }
}
