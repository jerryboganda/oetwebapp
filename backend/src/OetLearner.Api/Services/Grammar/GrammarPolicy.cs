using System.Text.Json;

namespace OetLearner.Api.Services.Grammar;

/// <summary>
/// Runtime-configurable knobs for the Grammar module. See docs/GRAMMAR-POLICY.md.
/// Values are resolved as: per-exam override → default → hard-coded fallback.
/// </summary>
public sealed class GrammarPolicy
{
    public int RetryLimitPerExercise { get; init; } = Defaults.RetryLimitPerExercise;
    public int AttemptCooldownSeconds { get; init; } = Defaults.AttemptCooldownSeconds;
    public string ExplanationVisibility { get; init; } = Defaults.ExplanationVisibility;
    public int MasteryThreshold { get; init; } = Defaults.MasteryThreshold;
    public double EwmaWeight { get; init; } = Defaults.EwmaWeight;
    public bool AiDraftEnabled { get; init; } = Defaults.AiDraftEnabled;
    public string? AiDraftModel { get; init; }
    public int PaywallFreeLessonsCap { get; init; } = Defaults.PaywallFreeLessonsCap;
    public bool ReviewQueueEnabled { get; init; } = Defaults.ReviewQueueEnabled;
    public bool WritingLinkEnabled { get; init; } = Defaults.WritingLinkEnabled;
    public bool SpeakingLinkEnabled { get; init; } = Defaults.SpeakingLinkEnabled;
    public double ReadinessWeight { get; init; } = Defaults.ReadinessWeight;
    public int XpLessonComplete { get; init; } = Defaults.XpLessonComplete;
    public int XpLessonMastered { get; init; } = Defaults.XpLessonMastered;
    public int XpTopicMastered { get; init; } = Defaults.XpTopicMastered;
    public int RetentionDaysAttempts { get; init; } = Defaults.RetentionDaysAttempts;
    public int RetentionDaysRecommendations { get; init; } = Defaults.RetentionDaysRecommendations;
    public bool DiagnosticEnabled { get; init; } = Defaults.DiagnosticEnabled;
    public bool OfflineCacheEnabled { get; init; } = Defaults.OfflineCacheEnabled;

    public static class Defaults
    {
        public const int RetryLimitPerExercise = 3;
        public const int AttemptCooldownSeconds = 0;
        public const string ExplanationVisibility = "afterSubmit";
        public const int MasteryThreshold = 80;
        public const double EwmaWeight = 0.4;
        public const bool AiDraftEnabled = true;
        public const int PaywallFreeLessonsCap = 5;
        public const bool ReviewQueueEnabled = true;
        public const bool WritingLinkEnabled = true;
        public const bool SpeakingLinkEnabled = true;
        public const double ReadinessWeight = 0.10;
        public const int XpLessonComplete = 10;
        public const int XpLessonMastered = 15;
        public const int XpTopicMastered = 50;
        public const int RetentionDaysAttempts = 365;
        public const int RetentionDaysRecommendations = 180;
        public const bool DiagnosticEnabled = false;
        public const bool OfflineCacheEnabled = true;
    }
}

public interface IGrammarPolicyService
{
    Task<GrammarPolicy> GetEffectiveAsync(string examTypeCode, CancellationToken ct);
}

/// <summary>
/// Default implementation — returns the built-in defaults. A future
/// slice will read from <c>AdminSetting</c> / <c>FeatureFlag</c> records
/// and merge per-exam overrides here. The interface is stable so the
/// engine callers don't change when that slice lands.
/// </summary>
public sealed class GrammarPolicyService : IGrammarPolicyService
{
    public Task<GrammarPolicy> GetEffectiveAsync(string examTypeCode, CancellationToken ct)
        => Task.FromResult(new GrammarPolicy());
}
