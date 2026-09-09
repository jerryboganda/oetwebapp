using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.AiAssistant;
using OetLearner.Api.Services.Seeding;

namespace OetLearner.Api.Tests;

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
}
