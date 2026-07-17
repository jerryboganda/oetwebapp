namespace OetWithDrHesham.Api.Services.AiAssistant;

/// <summary>
/// Static configuration helpers defining per-role defaults for the AI Assistant.
/// These are compile-time defaults; runtime overrides come from the feature-route
/// resolver and admin UI.
/// </summary>
public static class AiAssistantConfiguration
{
    public static readonly Dictionary<string, RoleConfig> RoleDefaults = new()
    {
        ["admin"] = new("glm-5", 4096, 10, true),
        ["expert"] = new("glm-5", 2048, 5, false),
        ["learner"] = new("glm-5", 1024, 3, false),
    };

    /// <summary>
    /// Per-role configuration for the AI Assistant.
    /// </summary>
    /// <param name="DefaultModel">Default model when no feature-route override exists.</param>
    /// <param name="MaxTokens">Maximum completion tokens per turn.</param>
    /// <param name="MaxIterations">Maximum tool-call iterations per request.</param>
    /// <param name="AllowMutations">Whether this role can invoke mutation tools.</param>
    public record RoleConfig(string DefaultModel, int MaxTokens, int MaxIterations, bool AllowMutations);
}
