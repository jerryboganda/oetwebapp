using System.Text.Json;
using System.Text.Json.Serialization;

namespace OetLearner.Api.Services.AiPipeline;

/// <summary>Stage keys of the AI Pipeline Control Center. The first four are also AI feature codes.</summary>
public static class AiPipelineStageKeys
{
    public const string WritingGrade = "writing.grade";
    public const string WritingReview = "writing.grade.review";
    public const string SpeakingGrade = "speaking.grade";
    public const string SpeakingReview = "speaking.grade.review";
    public const string LiveVoice = "speaking.live_voice";

    public static readonly IReadOnlyList<string> All =
        [WritingGrade, WritingReview, SpeakingGrade, SpeakingReview, LiveVoice];

    public static bool IsKnown(string? key) => key is not null && All.Contains(key);

    /// <summary>Grading stages can never be switched off as a whole; reviewers can.</summary>
    public static bool IsGrading(string key) => key is WritingGrade or SpeakingGrade;

    public static bool IsReviewer(string key) => key is WritingReview or SpeakingReview;

    /// <summary>Stages whose provider choices are real AiProvider rows (live voice uses its own provider keys).</summary>
    public static bool UsesProviderRegistry(string key) => key != LiveVoice;

    public static string Label(string key) => key switch
    {
        WritingGrade => "Writing grading",
        WritingReview => "Writing reviewer",
        SpeakingGrade => "Speaking grading",
        SpeakingReview => "Speaking reviewer",
        LiveVoice => "Speaking live voice",
        _ => key,
    };
}

/// <summary>One step of a stage, in priority order. Provider is an AiProvider.Code (live voice: openai | gemini).</summary>
public sealed record AiPipelineHop(
    string Provider,
    string? Model,
    bool Enabled,
    int Attempts,
    int BudgetSeconds);

/// <summary>Where a resolved configuration came from. Only the first setup may come from the built-in default.</summary>
public enum AiPipelineSource
{
    /// <summary>The saved row (the normal case).</summary>
    Saved,
    /// <summary>The last version this process read successfully (the database could not be read now).</summary>
    LastKnownGood,
    /// <summary>No row has ever been saved and nothing was read before: first setup.</summary>
    InitialDefault,
}

public sealed record AiPipelineStageConfig(
    string StageKey,
    bool StageEnabled,
    int Version,
    IReadOnlyList<AiPipelineHop> Hops,
    AiPipelineSource Source);

/// <summary>A hop that passed the run-start checks (enabled, provider row active and credentialed).</summary>
public sealed record AiPipelineResolvedHop(
    int Index,
    string Provider,
    string Model,
    int Attempts,
    int BudgetSeconds);

/// <summary>The plan of one run. <see cref="Skipped"/> lists hops left out and why, for logs and the self-check.</summary>
public sealed record AiPipelinePlan(
    string StageKey,
    bool StageEnabled,
    int Version,
    AiPipelineSource Source,
    IReadOnlyList<AiPipelineResolvedHop> Hops,
    IReadOnlyList<string> Skipped);

public static class AiPipelineJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Serialize(IReadOnlyList<AiPipelineHop> hops) => JsonSerializer.Serialize(hops, Options);

    public static IReadOnlyList<AiPipelineHop> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<List<AiPipelineHop>>(json, Options) ?? [];
    }
}

/// <summary>
/// The built-in order of each stage. Used ONLY to create the saved row the first time (insert-only) and when
/// nothing has ever been read; it never overrides a saved decision.
/// </summary>
public static class AiPipelineDefaults
{
    public const string MaxProvider = "writing-claude-sub";
    public const string ClaudeApiProvider = "anthropic";
    public const string CodexProvider = "writing-codex-sub";
    public const string ClaudeModel = "claude-opus-5-5";
    public const string CodexModel = "gpt-6.1-sol";

    public static IReadOnlyList<AiPipelineHop> For(string stageKey) => stageKey switch
    {
        AiPipelineStageKeys.WritingGrade =>
        [
            new(MaxProvider, ClaudeModel, true, 2, 420),
            new(ClaudeApiProvider, ClaudeModel, true, 1, 150),
            new(CodexProvider, CodexModel, true, 2, 420),
        ],
        AiPipelineStageKeys.SpeakingGrade =>
        [
            new(MaxProvider, ClaudeModel, true, 1, 900),
            // Owner correction 9 Oct 2026: the API hop is the approved claude-opus-5-5, never sonnet-5
            // (the initial seed predated the pipeline API-first flip).
            new(ClaudeApiProvider, ClaudeModel, true, 1, 900),
        ],
        AiPipelineStageKeys.WritingReview or AiPipelineStageKeys.SpeakingReview =>
        [
            new(CodexProvider, CodexModel, true, 2, 240),
            new(ClaudeApiProvider, ClaudeModel, true, 1, 240),
        ],
        AiPipelineStageKeys.LiveVoice =>
        [
            new("openai", null, true, 1, 0),
            new("gemini", null, true, 1, 0),
        ],
        _ => [],
    };
}
