using OetLearner.Api.Services.Seeding;

namespace OetLearner.Api.Services.AiAssistant;

/// <summary>
/// Chatbot model picker catalogs. Claude (Anthropic API), UBAG (browser), Z.AI (GLM) and
/// OpenCode (inference gateway) are separate on purpose — a UBAG id must never be sent to
/// Anthropic, and a GLM id must never be sent to either. <c>claude_web</c> is a UBAG browser target,
/// not an Anthropic model.
/// <para>
/// OpenCode is per-thread only: <see cref="IsThreadSelectable"/> accepts it,
/// <see cref="IsSelectable"/> (also the admin default-model check) does not, so an OpenCode id can
/// never be saved as a feature-route default. Z.AI is in BOTH — it is the owner's directed default
/// for the learner and staff assistants, so it must be saveable as a default, and unlike OpenCode it
/// has no terms gate that argues for hiding it behind a per-thread pick.
/// </para>
/// </summary>
public static class AssistantModelCatalog
{
    public const string AnthropicProviderCode = "anthropic";
    public const string LearnerAssistantLabel = "OET Personal Ai Assistant";

    /// <summary>
    /// The learner's model when no admin default is saved.
    ///
    /// <para>
    /// <b>This used to be a hardcoded pin.</b> Both the model and its provider were pinned in code
    /// (<c>deepseek-v4.1-flash</c> on <c>opencode</c>), so nothing in the database, the environment
    /// or any admin screen could change the chatbot every real learner uses. It is now a DEFAULT
    /// rather than a pin: an admin default saved through <c>/admin/ai-assistant/config</c> wins, and
    /// this is only the fallback.
    /// </para>
    ///
    /// <para>
    /// The value moved to <c>glm-5.3-flash</c> on Z.AI (owner directive 2026-10-09). It is the
    /// multimodal model: the assistant sends image and document attachments and several tools emit
    /// strict JSON, and the text-only <c>glm-5.3</c> cannot serve an image-bearing turn.
    /// </para>
    /// </summary>
    public const string LearnerDefaultModel = ZaiProviderDefaults.DefaultModel;

    /// <summary>
    /// The learner's provider when no admin default is saved. Derived from the model rather than
    /// written out, so the two cannot drift.
    /// </summary>
    public static string LearnerDefaultProviderCode
        => ProviderCodeForModel(LearnerDefaultModel) ?? ZaiProviderDefaults.ProviderCode;

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

    /// <summary>
    /// Z.AI models offered to the assistants. Free-to-call ids are labelled in the picker
    /// (<see cref="ZaiProviderDefaults.IsFreeModel"/>) but never made a default: a vendor's free
    /// tier is the first thing it changes, and the learner chatbot must not depend on that.
    /// </summary>
    public static IReadOnlyList<string> ZaiModels { get; } = ZaiProviderDefaults.CuratedChatModels;

    private static readonly HashSet<string> ZaiModelSet = new(ZaiProviderDefaults.CuratedChatModels, StringComparer.OrdinalIgnoreCase);

    /// <summary>Models the LEARNER surface may be offered. Narrower than the staff list on purpose:
    /// the learner entitlement is one curated personal assistant, not the whole catalogue.</summary>
    public static IReadOnlyList<string> LearnerModels { get; } = ZaiProviderDefaults.CuratedChatModels;

    public static bool IsClaudeApiModel(string? model) =>
        !string.IsNullOrWhiteSpace(model) && ClaudeApiModelSet.Contains(model.Trim());

    public static bool IsUbagModel(string? model) =>
        !string.IsNullOrWhiteSpace(model) && UbagModelSet.Contains(model.Trim());

    public static bool IsOpenCodeModel(string? model) =>
        !string.IsNullOrWhiteSpace(model) && OpenCodeModelSet.Contains(model.Trim());

    public static bool IsZaiModel(string? model) =>
        !string.IsNullOrWhiteSpace(model) && ZaiModelSet.Contains(model.Trim());

    /// <summary>Claude + UBAG + Z.AI. Used by the admin default-model save.</summary>
    public static bool IsSelectable(string? model) =>
        IsClaudeApiModel(model) || IsUbagModel(model) || IsZaiModel(model);

/// <summary>
    /// Models any staff or learner conversation may be pinned to: everything the picker offers
    /// (Claude API, UBAG, Z.AI) plus the OpenCode gateway.
    ///
    /// <para>
    /// <b>This MUST stay the union of what <c>GET /models</c> offers.</b> A previous version listed
    /// only Z.AI and OpenCode while the picker still listed Claude and UBAG, so selecting
    /// <c>claude-sonnet-5</c> or any <c>chatgpt_web|…</c> model failed with
    /// <c>ai_assistant_model_unknown</c> and the UI showed "Failed to change model" — a validator that
    /// disagrees with the control above it. Deriving this from <see cref="IsSelectable"/> is what
    /// keeps the two in step: the picker can only offer a subset of
    /// <see cref="IsSelectable"/> plus OpenCode.
    /// </para>
    /// </summary>
    public static bool IsThreadSelectable(string? model)
        => IsSelectable(model) || IsOpenCodeModel(model);

    /// <summary>What a LEARNER may be offered. Kept separate from <see cref="IsThreadSelectable"/>
    /// so widening the staff picker can never quietly widen the learner's entitlement.</summary>
    public static bool IsLearnerSelectable(string? model)
        => !string.IsNullOrWhiteSpace(model) && LearnerModels.Contains(model.Trim(), StringComparer.OrdinalIgnoreCase);

    public static string? ProviderCodeForModel(string? model)
    {
        if (IsClaudeApiModel(model)) return AnthropicProviderCode;
        if (IsUbagModel(model)) return UbagProviderRouteDefaults.ProviderCode;
        // Z.AI before OpenCode: several GLM ids are offered by BOTH vendors, and Z.AI is the owner's
        // directed provider, so it wins the mapping and the OpenCode gateway keeps only its
        // deepseek id in practice.
        if (IsZaiModel(model)) return ZaiProviderDefaults.ProviderCode;
        if (IsOpenCodeModel(model)) return OpenCodeProviderDefaults.ProviderCode;
        return null;
    }

    /// <summary>Display label for a picker group.</summary>
    public static string ProviderLabelFor(string? providerCode) => providerCode switch
    {
        AnthropicProviderCode => "Claude (API)",
        ZaiProviderDefaults.ProviderCode => "Z.AI (GLM)",
        OpenCodeProviderDefaults.ProviderCode => "Direct OpenCode gateway",
        _ when string.Equals(providerCode, UbagProviderRouteDefaults.ProviderCode, StringComparison.OrdinalIgnoreCase) => "UBAG (browser)",
        _ => providerCode ?? "Unknown",
    };
}
