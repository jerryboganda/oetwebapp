using OetLearner.Api.Services.Seeding;

namespace OetLearner.Api.Services.AiAssistant;

/// <summary>
/// Chatbot model picker catalogs. Claude (Anthropic API), UBAG (browser) and
/// OpenCode (inference gateway) are separate on purpose — a UBAG id must never
/// be sent to Anthropic. <c>claude_web</c> is a UBAG browser target, not an
/// Anthropic model.
/// <para>
/// OpenCode is per-thread only: <see cref="IsThreadSelectable"/> accepts it,
/// <see cref="IsSelectable"/> (also the admin default-model check) does not, so
/// an OpenCode id can never be saved as a feature-route default.
/// </para>
/// </summary>
public static class AssistantModelCatalog
{
    public const string AnthropicProviderCode = "anthropic";
    public const string LearnerAssistantLabel = "OET Personal Ai Assistant";
    public const string LearnerModel = "deepseek-v4.1-flash";
    public const string UbagCompositeModel = "chatgpt_web|GPT-5.6 Sol + Medium";

    public static readonly string[] ClaudeApiModels =
    [
        "claude-sonnet-5",
        "claude-haiku-5",
        "claude-fable-5",
        "claude-opus-4-8",
        "claude-haiku-4-5-20251001",
    ];

    private static readonly HashSet<string> ClaudeApiModelSet = new(ClaudeApiModels, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> UbagModelSet = new(
        UbagProviderRouteDefaults.AllowedModelsCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(UbagCompositeModel),
        StringComparer.Ordinal);

    public static IReadOnlyList<string> UbagModels { get; } = UbagModelSet
        .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static readonly HashSet<string> OpenCodeModelSet = new(
        OpenCodeProviderDefaults.CuratedChatModels,
        StringComparer.Ordinal);

    public static IReadOnlyList<string> OpenCodeModels { get; } = OpenCodeProviderDefaults.CuratedChatModels;

    public static bool IsClaudeApiModel(string? model) =>
        !string.IsNullOrWhiteSpace(model) && ClaudeApiModelSet.Contains(model.Trim());

    public static bool IsUbagModel(string? model) =>
        !string.IsNullOrWhiteSpace(model) && UbagModelSet.Contains(model.Trim());

    public static bool IsOpenCodeModel(string? model) =>
        !string.IsNullOrWhiteSpace(model) && OpenCodeModelSet.Contains(model.Trim());

    /// <summary>Claude + UBAG only. Used by the admin default-model save, so it must NOT
    /// accept OpenCode: the catalog's provider overrides the route's, which would silently
    /// make OpenCode a feature default without the route-approval gate.</summary>
    public static bool IsSelectable(string? model) =>
        IsClaudeApiModel(model) || IsUbagModel(model);

    /// <summary>Models a learner may pin to a single conversation (adds OpenCode).</summary>
    public static bool IsThreadSelectable(string? model) =>
        IsSelectable(model) || IsOpenCodeModel(model);

    public static string? ProviderCodeForModel(string? model)
    {
        if (IsClaudeApiModel(model)) return AnthropicProviderCode;
        if (IsUbagModel(model)) return UbagProviderRouteDefaults.ProviderCode;
        if (IsOpenCodeModel(model)) return OpenCodeProviderDefaults.ProviderCode;
        return null;
    }
}
