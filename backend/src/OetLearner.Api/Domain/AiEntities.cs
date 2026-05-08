using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Which credential funded an AI call.
/// <para>
/// - <c>Platform</c>: the platform's own provider key paid the call.<br/>
/// - <c>Byok</c>: the user's own key (OpenAI/Anthropic/OpenRouter/…) paid it.<br/>
/// - <c>PlatformFallback</c>: BYOK was attempted first but failed; platform
///   credits were used as fallback. Surfaced to the learner via a banner.<br/>
/// - <c>None</c>: the call was refused before any key was used
///   (kill-switch, quota-denied, ungrounded prompt, etc.).
/// </para>
/// See <c>docs/AI-USAGE-POLICY.md</c> §1 for the policy that decides this.
/// </summary>
public enum AiKeySource
{
    None = 0,
    Platform = 1,
    Byok = 2,
    PlatformFallback = 3,
}

/// <summary>
/// Final disposition of an AI call. Every call produces exactly one
/// <see cref="AiUsageRecord"/> regardless of outcome.
/// </summary>
public enum AiCallOutcome
{
    /// <summary>Provider returned a successful completion.</summary>
    Success = 0,
    /// <summary>Provider returned an error (4xx/5xx from the model vendor).</summary>
    ProviderError = 1,
    /// <summary>Call was refused by the gateway before any provider was contacted
    /// (quota, kill-switch, ungrounded prompt, disabled feature, etc.).</summary>
    GatewayRefused = 2,
    /// <summary>Call was cancelled by the caller before completion.</summary>
    Cancelled = 3,
    /// <summary>Call timed out waiting on the provider.</summary>
    Timeout = 4,
    /// <summary>Unexpected exception in platform code (bug). Alert-worthy.</summary>
    PlatformError = 5,
}

/// <summary>
/// Per-call audit + accounting row for the AI subsystem. One row per call,
/// regardless of whether the call succeeded, was denied, or errored.
///
/// <para>
/// This table is the foundation of:
/// <list type="bullet">
///   <item>Admin usage explorer (<c>/admin/ai-config/usage</c>).</item>
///   <item>Learner "AI credits used" dashboard widget.</item>
///   <item>Quota counters (aggregated per user per period).</item>
///   <item>Cost-vs-revenue analytics (aggregated per provider per day).</item>
///   <item>Anomaly detection (per-user daily vs trailing-7d median).</item>
/// </list>
/// </para>
///
/// <para>
/// Privacy: we do <b>not</b> persist full prompt or response bodies by default.
/// Hashes let us correlate retries and catch prompt-injection patterns without
/// retaining learner content. See <c>docs/AI-USAGE-POLICY.md</c> §8 for the
/// option that enables body retention with sampling + consent.
/// </para>
/// </summary>
[Index(nameof(UserId), nameof(CreatedAt))]
[Index(nameof(FeatureCode), nameof(CreatedAt))]
[Index(nameof(ProviderId), nameof(CreatedAt))]
[Index(nameof(AccountId), nameof(CreatedAt))]
[Index(nameof(CreatedAt))]
public class AiUsageRecord
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Learner / admin / system user this call was made on behalf of.
    /// Nullable because some pre-auth paths (e.g. trial content generation)
    /// run without a user context.</summary>
    [MaxLength(64)]
    public string? UserId { get; set; }

    /// <summary>Auth-account FK where available. Lets admin explorer join to
    /// <see cref="ApplicationUserAccount"/> without guessing.</summary>
    [MaxLength(64)]
    public string? AuthAccountId { get; set; }

    /// <summary>Sponsor / organisation scope, if the learner is under a sponsorship.
    /// Null for individual learners.</summary>
    [MaxLength(64)]
    public string? TenantId { get; set; }

    /// <summary>Stable feature identifier — e.g. <c>writing.grade</c>,
    /// <c>conversation.reply</c>. Matches the feature-eligibility matrix in
    /// <c>docs/AI-USAGE-POLICY.md</c> §5. Never free text at call sites;
    /// call sites pass a constant from <c>AiFeatureCodes</c>.</summary>
    [MaxLength(64)]
    public string FeatureCode { get; set; } = default!;

    /// <summary>Provider ID that actually served the call (e.g.
    /// <c>digitalocean-serverless</c>, <c>openai-platform</c>, <c>anthropic</c>,
    /// <c>openrouter</c>, <c>mock</c>). Null if the call was refused.</summary>
    [MaxLength(64)]
    public string? ProviderId { get; set; }

    /// <summary>For multi-account providers (e.g. Copilot multi-PAT pool),
    /// the <see cref="AiProviderAccount.Id"/> that ultimately served the
    /// call. Null when the provider is single-credential or when the call
    /// was refused before account selection. Per Phase 3 invariant: this
    /// is the LAST account tried — failover hops are recorded in
    /// <see cref="FailoverTrace"/>, not as separate rows.</summary>
    [MaxLength(64)]
    public string? AccountId { get; set; }

    /// <summary>Compact human-readable failover trail when the provider
    /// retried across multiple accounts. Format
    /// <c>"primary:429 → backup:success"</c>. Null when no failover
    /// happened (single-shot call). Capped at 1024 chars; longer trails
    /// are truncated by the recorder.</summary>
    [MaxLength(1024)]
    public string? FailoverTrace { get; set; }

    /// <summary>Model identifier sent to the provider (e.g. <c>gpt-4o</c>,
    /// <c>claude-3-5-sonnet-latest</c>). Null if the call was refused.</summary>
    [MaxLength(128)]
    public string? Model { get; set; }

    /// <summary>Which credential funded this call. See <see cref="AiKeySource"/>.</summary>
    public AiKeySource KeySource { get; set; } = AiKeySource.None;

    /// <summary>Rulebook version stamped on the grounded prompt. Mirrors
    /// <c>AiGroundedPromptMetadata.RulebookVersion</c>.</summary>
    [MaxLength(32)]
    public string? RulebookVersion { get; set; }

    /// <summary>Prompt-template version — lets us correlate quality shifts to
    /// prompt changes in A/B rollouts.</summary>
    [MaxLength(64)]
    public string? PromptTemplateId { get; set; }

    /// <summary>SHA-256 (hex) of the system prompt. Never the raw text. Lets
    /// us detect drift without retaining content.</summary>
    [MaxLength(64)]
    public string? SystemPromptHash { get; set; }

    /// <summary>SHA-256 (hex) of the user message. See note above.</summary>
    [MaxLength(64)]
    public string? UserPromptHash { get; set; }

    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens => PromptTokens + CompletionTokens;

    /// <summary>Estimated cost in USD based on provider rate card at call time.
    /// Stored at the moment of the call so changing rate cards later does not
    /// rewrite history.</summary>
    public decimal CostEstimateUsd { get; set; }

    /// <summary>Final outcome — see <see cref="AiCallOutcome"/>.</summary>
    public AiCallOutcome Outcome { get; set; } = AiCallOutcome.Success;

    /// <summary>Short machine error code when <see cref="Outcome"/> != Success
    /// (e.g. <c>quota_exhausted</c>, <c>provider_429</c>, <c>ungrounded</c>,
    /// <c>kill_switch</c>, <c>byok_invalid</c>). Compact so it indexes cheaply.</summary>
    [MaxLength(64)]
    public string? ErrorCode { get; set; }

    /// <summary>One-line human reason for the outcome. Never a stack trace.</summary>
    [MaxLength(512)]
    public string? ErrorMessage { get; set; }

    /// <summary>End-to-end latency including retries, measured by the gateway.</summary>
    public int LatencyMs { get; set; }

    /// <summary>Number of retry attempts Polly / the provider loop made.</summary>
    public int RetryCount { get; set; }

    /// <summary>Resolver decision trace — which policy rule decided the
    /// credential source. Human readable, short, e.g.
    /// <c>"scoring-critical → platform-only"</c> or
    /// <c>"user.auto + byok valid → byok"</c>. Lets the admin explorer answer
    /// "why did this call route this way?" without replaying logic.</summary>
    [MaxLength(256)]
    public string? PolicyTrace { get; set; }

    /// <summary>Wall-clock time the call started.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Period key used for monthly/daily quota aggregation, e.g.
    /// <c>2026-04</c> for monthly, <c>2026-04-18</c> for daily. Denormalised
    /// so counter queries don't need date functions.</summary>
    [MaxLength(16)]
    public string PeriodMonthKey { get; set; } = default!;

    [MaxLength(16)]
    public string PeriodDayKey { get; set; } = default!;

    // Optional FK for admin explorer joins. Not required, because usage
    // records can exist for users whose accounts were later deleted.
    public ApplicationUserAccount? AuthAccount { get; set; }
}

/// <summary>
/// Canonical feature codes. Call sites pass one of these constants into the
/// gateway. The set matches the feature-eligibility matrix in
/// <c>docs/AI-USAGE-POLICY.md</c> §5. Adding a new code here without updating
/// that matrix is a bug caught by <c>AiFeatureEligibilityTests</c>.
/// </summary>
public static class AiFeatureCodes
{
    // Scoring-critical (platform-only by default)
    public const string WritingGrade = "writing.grade";
    public const string WritingSampleScore = "writing.sample_score";
    public const string SpeakingGrade = "speaking.grade";
    public const string MockFullGrade = "mock.full_grade";

    // Non-scoring (BYOK-eligible)
    public const string WritingCoachSuggest = "writing.coach.suggest";
    public const string WritingCoachExplain = "writing.coach.explain";
    public const string ConversationReply = "conversation.reply";
    public const string ConversationOpening = "conversation.opening";
    public const string PronunciationTip = "pronunciation.tip";
    public const string SummarisePassage = "summarise.passage";
    public const string VocabularyGloss = "vocabulary.gloss";

    // Recalls (vocabulary + spaced-repetition unified surface).
    // BYOK-eligible — non-scoring explanatory feedback.
    public const string RecallsMistakeExplain = "recalls.mistake_explain";
    public const string RecallsRevisionPlan = "recalls.revision_plan";

    // Mocks V2 — Wave 5 remediation plan AI personalisation.
    // Optional enrichment over the deterministic 7-day plan; classified as
    // non-scoring + BYOK-eligible (no candidate score is produced — the AI
    // only generates a personalised intro/summary string for the plan).
    public const string MockRemediationDraft = "mock.remediation_draft";

    // Pronunciation analysis (platform-only by default — scoring-critical)
    public const string PronunciationScore = "pronunciation.score";
    public const string PronunciationFeedback = "pronunciation.feedback";

    // Conversation evaluation (platform-only — scoring-critical)
    public const string ConversationEvaluation = "conversation.evaluation";

    // Admin tooling (platform-only always)
    public const string AdminContentGeneration = "admin.content_generation";
    public const string AdminGrammarDraft = "admin.grammar_draft";
    public const string AdminPronunciationDraft = "admin.pronunciation_draft";
    public const string AdminVocabularyDraft = "admin.vocabulary_draft";
    public const string AdminConversationDraft = "admin.conversation_draft";
    public const string AdminListeningDraft = "admin.listening_draft";

    // Catch-all for calls that pre-date feature classification. Tolerated only
    // during the Slice 1 rollout; future slices will validate against this set.
    public const string Unclassified = "unclassified";
}
