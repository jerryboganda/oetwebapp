using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant;
using OetLearner.Api.Services.Seeding;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// GET /v1/ai-assistant/models, PATCH /v1/ai-assistant/threads/{id}/model and the admin config save,
/// through the real pipeline, for the OpenCode catalog: it is a learner-only, per-thread pick that
/// exists only while an admin has the <c>opencode</c> provider row active, and it can never be saved
/// as a feature default.
/// </summary>
public sealed class AiAssistantModelEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AiAssistantModelEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── GET /models ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Models_ForALearner_AppendTheOpenCodeGroup_WhileTheRowIsActive()
    {
        await SetOpenCodeRowAsync(isActive: true);
        using var client = CreateClient(ApplicationUserRoles.Learner);

        var json = await client.GetFromJsonAsync<JsonElement>("/v1/ai-assistant/models");

        Assert.Equal(
            new[] { "anthropic", "ubag", "opencode" },
            json.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("provider").GetString()!).ToArray());
        var openCode = json.GetProperty("groups").EnumerateArray().Last();
        Assert.Equal("OpenCode", openCode.GetProperty("label").GetString());
        Assert.Equal(
            AssistantModelCatalog.OpenCodeModels.ToArray(),
            openCode.GetProperty("models").EnumerateArray().Select(m => m.GetString()!).ToArray());
        // The flat list stays the union of every group.
        Assert.Equal(
            AssistantModelCatalog.ClaudeApiModels.Concat(AssistantModelCatalog.UbagModels).Concat(AssistantModelCatalog.OpenCodeModels).ToArray(),
            json.GetProperty("models").EnumerateArray().Select(m => m.GetString()!).ToArray());
    }

    [Theory]
    [InlineData(false)] // row present, an admin switched it off
    [InlineData(null)]  // row absent
    public async Task Models_ForALearner_OmitTheOpenCodeGroup_WhenTheRowIsNotActive(bool? rowIsActive)
    {
        await SetOpenCodeRowAsync(rowIsActive);
        using var client = CreateClient(ApplicationUserRoles.Learner);

        var json = await client.GetFromJsonAsync<JsonElement>("/v1/ai-assistant/models");

        AssertNoOpenCode(json);
    }

    [Theory]
    [InlineData(ApplicationUserRoles.Admin)]
    [InlineData(ApplicationUserRoles.Expert)]
    public async Task Models_ForStaff_NeverIncludeOpenCode_EvenWhileTheRowIsActive(string role)
    {
        await SetOpenCodeRowAsync(isActive: true);
        using var client = CreateClient(role);

        var json = await client.GetFromJsonAsync<JsonElement>("/v1/ai-assistant/models");

        AssertNoOpenCode(json);
    }

    // ── PATCH /threads/{id}/model ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PatchModel_OpenCode_IsAccepted_ForALearner_WhileTheRowIsActive()
    {
        await SetOpenCodeRowAsync(isActive: true);
        var userId = NewUserId();
        var threadId = await SeedThreadAsync(userId, ApplicationUserRoles.Learner);
        using var client = CreateClient(ApplicationUserRoles.Learner, userId);

        var response = await client.PatchAsJsonAsync($"/v1/ai-assistant/threads/{threadId}/model", new { model = "glm-5.3-flash" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("glm-5.3-flash", await ReadThreadModelAsync(threadId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task PatchModel_OpenCode_IsRefused_WhenTheRowIsNotActive(bool? rowIsActive)
    {
        await SetOpenCodeRowAsync(rowIsActive);
        var userId = NewUserId();
        var threadId = await SeedThreadAsync(userId, ApplicationUserRoles.Learner);
        using var client = CreateClient(ApplicationUserRoles.Learner, userId);

        var response = await client.PatchAsJsonAsync($"/v1/ai-assistant/threads/{threadId}/model", new { model = "glm-5.3" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ai_assistant_model_unavailable", await ReadErrorCodeAsync(response));
        Assert.Null(await ReadThreadModelAsync(threadId));
    }

    [Theory]
    [InlineData(ApplicationUserRoles.Admin)]
    [InlineData(ApplicationUserRoles.Expert)]
    public async Task PatchModel_OpenCode_IsRefused_ForStaff_EvenWhileTheRowIsActive(string role)
    {
        await SetOpenCodeRowAsync(isActive: true);
        var userId = NewUserId();
        var threadId = await SeedThreadAsync(userId, role);
        using var client = CreateClient(role, userId);

        var response = await client.PatchAsJsonAsync($"/v1/ai-assistant/threads/{threadId}/model", new { model = "glm-5.3-flash" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ai_assistant_model_learner_only", await ReadErrorCodeAsync(response));
        Assert.Null(await ReadThreadModelAsync(threadId));
    }

    [Theory]
    [InlineData("gpt-4o")]
    [InlineData("GLM-5.3")] // OpenCode ids match exactly
    [InlineData("glm-5.3|x")]
    public async Task PatchModel_StillRejectsUnknownIds_WithTheUnknownCode(string model)
    {
        await SetOpenCodeRowAsync(isActive: true);
        var userId = NewUserId();
        var threadId = await SeedThreadAsync(userId, ApplicationUserRoles.Learner);
        using var client = CreateClient(ApplicationUserRoles.Learner, userId);

        var response = await client.PatchAsJsonAsync($"/v1/ai-assistant/threads/{threadId}/model", new { model });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ai_assistant_model_unknown", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task PatchModel_ClaudeIds_AreUnaffected_AndDoNotNeedTheOpenCodeRow()
    {
        await SetOpenCodeRowAsync(isActive: null);
        var userId = NewUserId();
        var threadId = await SeedThreadAsync(userId, ApplicationUserRoles.Learner);
        using var client = CreateClient(ApplicationUserRoles.Learner, userId);

        var response = await client.PatchAsJsonAsync($"/v1/ai-assistant/threads/{threadId}/model", new { model = "claude-sonnet-5" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("claude-sonnet-5", await ReadThreadModelAsync(threadId));
    }

    // ── POST /v1/admin/ai-assistant/config ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("glm-5.3-flash")]
    [InlineData("glm-5.3")]
    public async Task AdminConfigSave_RefusesAnOpenCodeModel_AsADefault_WithItsOwnCode(string model)
    {
        await SetOpenCodeRowAsync(isActive: true);
        using var client = CreateClient(ApplicationUserRoles.Admin, adminPermissions: AdminPermissions.AiConfig);

        var response = await client.PostAsJsonAsync(
            "/v1/admin/ai-assistant/config",
            new { roles = new[] { new { role = ApplicationUserRoles.Learner, model } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ai_assistant_model_not_defaultable", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task AdminConfigSave_StillRejectsAnUnknownModel_WithTheUnknownCode()
    {
        using var client = CreateClient(ApplicationUserRoles.Admin, adminPermissions: AdminPermissions.AiConfig);

        var response = await client.PostAsJsonAsync(
            "/v1/admin/ai-assistant/config",
            new { roles = new[] { new { role = ApplicationUserRoles.Learner, model = "gpt-4o" } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ai_assistant_model_unknown", await ReadErrorCodeAsync(response));
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static void AssertNoOpenCode(JsonElement models)
    {
        Assert.Equal(
            new[] { "anthropic", "ubag" },
            models.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("provider").GetString()!).ToArray());
        Assert.Equal(
            AssistantModelCatalog.ClaudeApiModels.Concat(AssistantModelCatalog.UbagModels).ToArray(),
            models.GetProperty("models").EnumerateArray().Select(m => m.GetString()!).ToArray());
    }

    private static string NewUserId() => $"assistant-models-{Guid.NewGuid():N}";

    private HttpClient CreateClient(string role, string? userId = null, string? adminPermissions = null)
    {
        userId ??= NewUserId();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", role);
        if (adminPermissions is not null)
        {
            client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", adminPermissions);
        }

        return client;
    }

    /// <summary>Creates or updates the opencode provider row; <c>null</c> deletes it.</summary>
    private async Task SetOpenCodeRowAsync(bool? isActive)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var row = await db.AiProviders.FirstOrDefaultAsync(p => p.Code == OpenCodeProviderDefaults.ProviderCode);
        if (isActive is null)
        {
            if (row is not null) db.AiProviders.Remove(row);
        }
        else
        {
            var now = DateTimeOffset.UtcNow;
            if (row is null)
            {
                row = new AiProvider
                {
                    Id = $"aip-test-{Guid.NewGuid():N}",
                    Code = OpenCodeProviderDefaults.ProviderCode,
                    Name = OpenCodeProviderDefaults.ProviderName,
                    Dialect = AiProviderDialect.OpenAiCompatible,
                    Category = AiProviderCategory.TextChat,
                    BaseUrl = OpenCodeProviderDefaults.ZenBaseUrl,
                    DefaultModel = OpenCodeProviderDefaults.DefaultModel,
                    FailoverPriority = OpenCodeProviderDefaults.FailoverPriority,
                    CreatedAt = now,
                };
                db.AiProviders.Add(row);
            }

            row.IsActive = isActive.Value;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync();
    }

    private async Task<string> SeedThreadAsync(string userId, string role)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var thread = new AiAssistantThread
        {
            Id = $"thr-{Guid.NewGuid():N}",
            UserId = userId,
            Role = role,
            Title = "Model picker",
        };
        db.AiAssistantThreads.Add(thread);
        await db.SaveChangesAsync();
        return thread.Id;
    }

    private async Task<string?> ReadThreadModelAsync(string threadId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        return (await db.AiAssistantThreads.AsNoTracking().FirstAsync(t => t.Id == threadId)).ModelOverride;
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("code").GetString();
    }
}
