using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.AiAssistant;
using OetLearner.Api.Services.Seeding;

namespace OetLearner.Api.Tests.Services;

public sealed class AssistantModelCatalogTests
{
    [Fact]
    public void ClaudeAndUbagCatalogs_AreDisjoint()
    {
        Assert.Empty(AssistantModelCatalog.ClaudeApiModels.Intersect(AssistantModelCatalog.UbagModels, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void ClaudeWeb_IsUbagBrowserTarget_NotAnthropicApi()
    {
        Assert.True(AssistantModelCatalog.IsUbagModel("claude_web"));
        Assert.False(AssistantModelCatalog.IsClaudeApiModel("claude_web"));
        Assert.Equal(UbagProviderRouteDefaults.ProviderCode, AssistantModelCatalog.ProviderCodeForModel("claude_web"));
    }

    [Fact]
    public void DuckAiLuna_RoutesToUbag_NotAnthropic()
    {
        Assert.Equal(UbagProviderRouteDefaults.ProviderCode, AssistantModelCatalog.ProviderCodeForModel("duckai_web|GPT-5.6 Luna"));
        Assert.False(AssistantModelCatalog.IsClaudeApiModel("duckai_web|GPT-5.6 Luna"));
    }

    [Fact]
    public void ClaudeSonnet_RoutesToAnthropic()
    {
        Assert.Equal(AssistantModelCatalog.AnthropicProviderCode, AssistantModelCatalog.ProviderCodeForModel("claude-sonnet-5"));
        Assert.False(AssistantModelCatalog.IsUbagModel("claude-sonnet-5"));
    }

    [Fact]
    public void ClaudeApiModels_MatchAnthropicSeederCsv()
    {
        Assert.Equal(
            CoreAiProviderSeeder.AnthropicAllowedModelsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            AssistantModelCatalog.ClaudeApiModels);
    }

    [Fact]
    public void OpenCodeCatalog_IsTheCuratedList_AndDisjointFromClaudeAndUbag()
    {
        Assert.Equal(OpenCodeProviderDefaults.CuratedChatModels, AssistantModelCatalog.OpenCodeModels.ToArray());
        Assert.NotEmpty(AssistantModelCatalog.OpenCodeModels);
        Assert.Empty(AssistantModelCatalog.OpenCodeModels.Intersect(AssistantModelCatalog.ClaudeApiModels, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(AssistantModelCatalog.OpenCodeModels.Intersect(AssistantModelCatalog.UbagModels, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void OpenCodeModels_AreThreadSelectable_ButNeverDefaultSelectable()
    {
        foreach (var model in AssistantModelCatalog.OpenCodeModels)
        {
            Assert.True(AssistantModelCatalog.IsOpenCodeModel(model));
            Assert.True(AssistantModelCatalog.IsThreadSelectable(model));
            // IsSelectable also guards the admin default-model save: an OpenCode id there would
            // silently make OpenCode a feature default (the catalog's provider overrides the route's).
            Assert.False(AssistantModelCatalog.IsSelectable(model));
        }
    }

    [Fact]
    public void ThreadSelectable_StillCoversClaudeAndUbag_AndRejectsUnknownIds()
    {
        Assert.True(AssistantModelCatalog.IsThreadSelectable("claude-sonnet-5"));
        Assert.True(AssistantModelCatalog.IsThreadSelectable("claude_web"));
        Assert.False(AssistantModelCatalog.IsThreadSelectable("gpt-4o"));
        Assert.False(AssistantModelCatalog.IsThreadSelectable("qwen3.8-flash")); // gateway model on a protocol this client does not speak
        Assert.False(AssistantModelCatalog.IsThreadSelectable(null));
        Assert.False(AssistantModelCatalog.IsThreadSelectable("  "));
        Assert.False(AssistantModelCatalog.IsOpenCodeModel("claude-sonnet-5"));
        Assert.False(AssistantModelCatalog.IsOpenCodeModel("claude_web"));
    }

    [Fact]
    public void IsOpenCodeModel_IsStrictOrdinal()
    {
        // Claude ids match case-insensitively; OpenCode ids must match exactly.
        Assert.False(AssistantModelCatalog.IsOpenCodeModel("GLM-5.3-FLASH"));
        Assert.False(AssistantModelCatalog.IsOpenCodeModel("glm-5.3-flash|extra"));
        Assert.False(AssistantModelCatalog.IsOpenCodeModel("glm-5"));
        Assert.False(AssistantModelCatalog.IsOpenCodeModel(null));
        Assert.False(AssistantModelCatalog.IsOpenCodeModel(""));
    }

    [Fact]
    public void OpenCodeModel_RoutesToOpenCode_NotAnthropicOrUbag()
    {
        Assert.Equal(OpenCodeProviderDefaults.ProviderCode, AssistantModelCatalog.ProviderCodeForModel("glm-5.3-flash"));
        Assert.Equal(OpenCodeProviderDefaults.ProviderCode, AssistantModelCatalog.ProviderCodeForModel("glm-5.3"));
        Assert.False(AssistantModelCatalog.IsClaudeApiModel("glm-5.3"));
        Assert.False(AssistantModelCatalog.IsUbagModel("glm-5.3"));
        Assert.Null(AssistantModelCatalog.ProviderCodeForModel("GLM-5.3"));
    }
}
