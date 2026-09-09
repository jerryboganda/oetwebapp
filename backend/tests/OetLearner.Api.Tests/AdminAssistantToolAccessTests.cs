using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Safety;
using OetLearner.Api.Services.AiTools;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// The admin chatbot (`ai_assistant.admin`) is read-only. These tests pin
/// both halves of that contract: (1) the seeder grants only read/search/list,
/// SELECT-only DB, and web search — never write/run/git/deploy; (2)
/// SafetyGuard denies mutation tools on chatbot feature codes even when
/// `AiToolContext.IsAdmin` is true.
/// </summary>
public sealed class AdminAssistantToolAccessTests
{
    private static readonly string[] ExpectedAdminTools =
    [
        "read_file",
        "search_codebase",
        "retrieve_codebase",
        "list_directory",
        "query_database",
        "web_search",
    ];

    [Fact]
    public void AdminAssistantToolset_ContainsFullCodebaseAccess()
    {
        var field = typeof(AiToolCatalogSeederHostedService)
            .GetField("AdminAssistantToolCodes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        var codes = (string[])field.GetValue(null)!;

        Assert.Equal(ExpectedAdminTools.OrderBy(c => c), codes.OrderBy(c => c));
    }

    [Fact]
    public void AdminToolset_DoesNotWeakenLearnerBoundary()
    {
        foreach (var tool in ExpectedAdminTools.Where(c => c is "write_file" or "run_command" or "git_operations" or "deploy" or "query_database"))
        {
            Assert.False(
                AiToolRegistry.IsLearnerSafeTool(tool),
                $"'{tool}' must never be on the learner-safe allowlist.");
        }

        Assert.False(AiToolRegistry.IsLearnerFacingFeature(AiFeatureCodes.AiAssistantAdmin));
    }

    [Fact]
    public async Task SafetyGuard_DeniesMutation_ForAdminChatbot()
    {
        var guard = new SafetyGuard(new SecretScanner(), NullLogger<SafetyGuard>.Instance);
        using var doc = System.Text.Json.JsonDocument.Parse("{\"path\":\"app/page.tsx\"}");
        var ctx = new AiToolContext("ai_assistant.admin", "admin-user-1", null, "usage-1", 0, IsAdmin: true);

        var result = await guard.CheckAsync("write_file", doc.RootElement, ctx, CancellationToken.None);

        Assert.False(result.IsAllowed);
        Assert.Contains("read-only", result.DenialReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SafetyGuard_AllowsMutation_ForNonChatbotAdminSurface()
    {
        var guard = new SafetyGuard(new SecretScanner(), NullLogger<SafetyGuard>.Instance);
        using var doc = System.Text.Json.JsonDocument.Parse("{\"path\":\"app/page.tsx\"}");
        var ctx = new AiToolContext("admin.listening.skill_tag", "admin-user-1", null, "usage-1", 0, IsAdmin: true);

        var result = await guard.CheckAsync("write_file", doc.RootElement, ctx, CancellationToken.None);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task SafetyGuard_DeniesMutation_ForNonAdmin_EvenWithUserId()
    {
        var guard = new SafetyGuard(new SecretScanner(), NullLogger<SafetyGuard>.Instance);
        using var doc = System.Text.Json.JsonDocument.Parse("{\"path\":\"app/page.tsx\"}");
        var ctx = new AiToolContext("ai_assistant.learner", "learner-user-1", null, "usage-1", 0, IsAdmin: false);

        var result = await guard.CheckAsync("write_file", doc.RootElement, ctx, CancellationToken.None);

        Assert.False(result.IsAllowed);
        Assert.Contains("read-only", result.DenialReason, StringComparison.OrdinalIgnoreCase);
    }
}
