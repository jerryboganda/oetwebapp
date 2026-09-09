using OetLearner.Api.Services.Seeding;

namespace OetLearner.Api.Services.AiAssistant;

/// <summary>
/// Chatbot model picker catalogs. Claude (Anthropic API) and UBAG (browser)
/// are separate on purpose — a UBAG id must never be sent to Anthropic.
/// <c>claude_web</c> is a UBAG browser target, not an Anthropic model.
/// </summary>
public static class AssistantModelCatalog
{
    public const string AnthropicProviderCode = "anthropic";
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

    public static bool IsClaudeApiModel(string? model) =>
        !string.IsNullOrWhiteSpace(model) && ClaudeApiModelSet.Contains(model.Trim());

    public static bool IsUbagModel(string? model) =>
        !string.IsNullOrWhiteSpace(model) && UbagModelSet.Contains(model.Trim());

    public static bool IsSelectable(string? model) =>
        IsClaudeApiModel(model) || IsUbagModel(model);

    public static string? ProviderCodeForModel(string? model)
    {
        if (IsClaudeApiModel(model)) return AnthropicProviderCode;
        if (IsUbagModel(model)) return UbagProviderRouteDefaults.ProviderCode;
        return null;
    }
}
