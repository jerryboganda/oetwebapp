using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public sealed class AiAssistantAdminEndpointStubTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AiAssistantAdminEndpointStubTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");
    }

    public static TheoryData<string, Func<HttpClient, string, Task<HttpResponseMessage>>> StubbedAdminMutations => new()
    {
        { "/v1/admin/ai-assistant/settings", (client, path) => client.PutAsJsonAsync(path, new { globalEnabled = true }) },
        { "/v1/admin/ai-assistant/providers", (client, path) => client.PostAsJsonAsync(path, new { displayName = "Local test provider" }) },
        { $"/v1/admin/ai-assistant/providers/{Guid.NewGuid()}", (client, path) => client.PutAsJsonAsync(path, new { displayName = "Local test provider" }) },
        { $"/v1/admin/ai-assistant/providers/{Guid.NewGuid()}", (client, path) => client.DeleteAsync(path) },
        { "/v1/admin/ai-assistant/indexing/reindex", (client, path) => client.PostAsJsonAsync(path, new { scope = "all" }) },
    };

    [Theory]
    [MemberData(nameof(StubbedAdminMutations))]
    public async Task V1WriteStubs_RemainNotImplemented(string path, Func<HttpClient, string, Task<HttpResponseMessage>> send)
    {
        using var response = await send(_client, path);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public async Task KillSwitch_ReturnsFullSettingsDto()
    {
        using var response = await _client.PostAsJsonAsync("/v1/admin/ai-assistant/kill-switch", new { enabled = true });

        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        Assert.True(root.GetProperty("globalEnabled").GetBoolean());
        Assert.True(root.TryGetProperty("requireApprovalAlways", out _));
        Assert.True(root.TryGetProperty("defaultProvider", out _));
        Assert.True(root.TryGetProperty("defaultModel", out _));
        Assert.True(root.TryGetProperty("lastKillSwitchAt", out _));
        Assert.True(root.TryGetProperty("lastKillSwitchActor", out _));
    }

    [Fact]
    public async Task V1WriteStubs_RequireAdminPermissionBeforeStub()
    {
        using var learner = _factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");

        using var response = await learner.PutAsJsonAsync("/v1/admin/ai-assistant/settings", new { globalEnabled = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task V1Chatbot_RejectsAiToolGrants()
    {
        using var response = await _client.PostAsJsonAsync("/v1/admin/ai-tools/grants", new
        {
            featureCode = AiFeatureCodes.AdminAiChatbot,
            toolCode = "lookup_rulebook_rule",
            isActive = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(AiFeatureCodes.AdminAiChatbot, body);
    }
}