using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>Dialect the provider speaks. Determines which concrete
/// <c>IAiModelProvider</c> the gateway dispatches to.</summary>
public enum AiProviderDialect
{
    OpenAiCompatible = 0,
    Anthropic = 1,
    /// <summary>
    /// Cloudflare Workers AI native API. Uses POST {BaseUrl}/run/{model}
    /// with a CF-specific request/response shape; auth is Bearer token.
    /// BaseUrl format: https://api.cloudflare.com/client/v4/accounts/{ACCOUNT_ID}/ai
    /// </summary>
    Cloudflare = 2,
    /// <summary>
    /// GitHub Copilot / GitHub Models. OpenAI-compatible chat-completions
    /// API at {BaseUrl}/chat/completions with two GitHub-specific headers
    /// (<c>X-GitHub-Api-Version</c>, <c>User-Agent</c>) and a GitHub PAT
    /// (scope <c>models:read</c>) as the bearer token. Model ids use the
    /// <c>{publisher}/{model}</c> form (e.g. <c>openai/gpt-4o-mini</c>).
    /// BaseUrl default: <c>https://models.github.ai/inference</c>.
    /// </summary>
    Copilot = 3,
    Mock = 99,
}

/// <summary>
/// DB-backed provider registry (Slice 5). Replaces config-only registration
/// so admins can add/rotate providers without a redeploy. The concrete
/// <c>IAiModelProvider</c> implementations resolve the active row at call
/// time via <see cref="Code"/>.
///
/// Platform keys stored here are encrypted via ASP.NET Data Protection,
/// exactly like BYOK keys in <c>UserAiCredential</c>.
/// </summary>
[Index(nameof(Code), IsUnique = true)]
public class AiProvider
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Stable code: <c>digitalocean-serverless</c>,
    /// <c>openai-platform</c>, <c>anthropic</c>, <c>openrouter</c>, …</summary>
    [MaxLength(64)]
    public string Code { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    public AiProviderDialect Dialect { get; set; } = AiProviderDialect.OpenAiCompatible;

    [MaxLength(512)]
    public string BaseUrl { get; set; } = default!;

    /// <summary>Encrypted platform API key (via purpose-scoped Data Protection).</summary>
    [MaxLength(4096)]
    public string EncryptedApiKey { get; set; } = string.Empty;

    [MaxLength(16)]
    public string ApiKeyHint { get; set; } = string.Empty;

    [MaxLength(128)]
    public string DefaultModel { get; set; } = "";

    /// <summary>
    /// Reasoning effort hint sent to reasoning-capable models
    /// (Claude 4+, OpenAI o-series, GPT-5). Values: <c>low</c>, <c>medium</c>,
    /// <c>high</c>. Null = fall back to <c>AiProviderOptions.ReasoningEffort</c>.
    /// </summary>
    [MaxLength(16)]
    public string? ReasoningEffort { get; set; }

    /// <summary>Comma-separated allow-list of permitted models. Empty = all.</summary>
    [MaxLength(1024)]
    public string AllowedModelsCsv { get; set; } = string.Empty;

    /// <summary>Price per 1,000 prompt tokens, USD. Stored at provider level
    /// so cost estimates are consistent across features.</summary>
    public decimal PricePer1kPromptTokens { get; set; }

    /// <summary>Price per 1,000 completion tokens, USD.</summary>
    public decimal PricePer1kCompletionTokens { get; set; }

    /// <summary>Polly retry count for transient failures.</summary>
    public int RetryCount { get; set; } = 2;

    /// <summary>Polly circuit-breaker threshold (consecutive failures).</summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>Circuit-breaker rolling window.</summary>
    public int CircuitBreakerWindowSeconds { get; set; } = 30;

    /// <summary>Display order in failover routing. Lower = tried first.</summary>
    public int FailoverPriority { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Phase 4: timestamp of the most recent admin-initiated
    /// connectivity probe via <c>POST /v1/admin/ai/providers/{code}/test</c>.
    /// Null = never tested.</summary>
    public DateTimeOffset? LastTestedAt { get; set; }

    /// <summary>Phase 4: classifier outcome from the last connectivity
    /// probe — one of <c>ok</c>, <c>auth</c>, <c>rate_limited</c>,
    /// <c>network</c>, <c>ungrounded</c>, <c>unknown</c>. Null = never
    /// tested. Short, indexable; the human message is kept in
    /// <see cref="LastTestError"/>.</summary>
    [MaxLength(32)]
    public string? LastTestStatus { get; set; }

    /// <summary>Phase 4: one-line error message from the last failed
    /// probe. Null on success or when never tested. Capped at 512 to
    /// avoid leaking stack traces into the UI.</summary>
    [MaxLength(512)]
    public string? LastTestError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }
}

/// <summary>
/// One credential / quota slot belonging to an <see cref="AiProvider"/>
/// row. Phase 2 of the GitHub Copilot integration: lets a single provider
/// (e.g. <c>copilot</c>) hold many PATs / accounts so that when the first
/// account hits its monthly cap or returns 429, the gateway transparently
/// fails over to the next account by ascending <see cref="Priority"/>.
///
/// One <c>AiUsageRecord</c> is still written per turn — failover retries
/// roll up into <see cref="AiUsageRecord.RetryCount"/> +
/// <see cref="AiUsageRecord.FailoverTraceJson"/>. See the audit invariants
/// in <c>docs/AI-COPILOT-PROGRESS.md</c> Phase 2.
///
/// Concurrency contract: account selection is performed via an atomic
/// <c>UPDATE ... SET requests_used_this_month = requests_used_this_month + 1
/// WHERE id = @id AND is_active AND (exhausted_until IS NULL OR exhausted_until &lt; now())
/// AND (monthly_request_cap IS NULL OR requests_used_this_month &lt; monthly_request_cap)</c>.
/// Under Postgres <c>READ COMMITTED</c> the WHERE is re-evaluated when a
/// concurrent UPDATE releases its row lock, so two callers cannot both
/// win the last slot in the cap.
/// </summary>
[Index(nameof(ProviderId), nameof(Priority))]
[Index(nameof(ProviderId), nameof(IsActive))]
public class AiProviderAccount
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>FK to <see cref="AiProvider.Id"/>. Multi-account semantics
    /// only meaningful for providers that support per-PAT quota
    /// (today: <see cref="AiProviderDialect.Copilot"/>); other dialects
    /// keep their single-row registry.</summary>
    [MaxLength(64)]
    public string ProviderId { get; set; } = default!;

    /// <summary>Operator-friendly label, e.g. <c>"primary-org"</c>,
    /// <c>"backup-personal"</c>. Shown in the admin UI.</summary>
    [MaxLength(128)]
    public string Label { get; set; } = default!;

    /// <summary>Encrypted PAT (or other API key), via
    /// <c>IDataProtectionProvider</c> purpose <c>AiProvider.PlatformKey.v1</c>.
    /// Same purpose as <see cref="AiProvider.EncryptedApiKey"/> so the
    /// existing protector implementation works unchanged.</summary>
    [MaxLength(4096)]
    public string EncryptedApiKey { get; set; } = string.Empty;

    /// <summary>Last 4 chars of the PAT for the admin UI. Never the key.</summary>
    [MaxLength(16)]
    public string ApiKeyHint { get; set; } = string.Empty;

    /// <summary>Monthly request cap. <c>null</c> = unlimited (no quota
    /// checked locally; rely on the provider returning 429 to detect
    /// exhaustion). When non-null, the atomic pick increments
    /// <see cref="RequestsUsedThisMonth"/> and skips this row once the
    /// cap is reached.</summary>
    public int? MonthlyRequestCap { get; set; }

    /// <summary>Counter incremented atomically on every successful
    /// reservation. Reset by <c>AiAccountQuotaResetWorker</c> at first-of-
    /// month UTC. Reflects requests, NOT tokens.</summary>
    public int RequestsUsedThisMonth { get; set; }

    /// <summary>Lower = tried first. Ties broken by ascending
    /// <see cref="RequestsUsedThisMonth"/> to spread load.</summary>
    public int Priority { get; set; }

    /// <summary>When non-null and in the future, the account is in
    /// quarantine (e.g. provider returned 429 with Retry-After). Reset
    /// to <c>null</c> once the quarantine window passes or admin
    /// explicitly clears it.</summary>
    public DateTimeOffset? ExhaustedUntil { get; set; }

    /// <summary>Set to false on hard-auth failures (401/403) until an
    /// admin re-enables. Soft errors only set <see cref="ExhaustedUntil"/>.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Phase 4: timestamp of the most recent admin-initiated
    /// per-account probe via
    /// <c>POST /v1/admin/ai/providers/{providerId}/accounts/{accountId}/test</c>.
    /// Null = never tested.</summary>
    public DateTimeOffset? LastTestedAt { get; set; }

    /// <summary>Phase 4: classifier outcome from the last per-account
    /// probe. Same vocabulary as <see cref="AiProvider.LastTestStatus"/>.</summary>
    [MaxLength(32)]
    public string? LastTestStatus { get; set; }

    /// <summary>Phase 4: one-line error message from the last failed
    /// per-account probe.</summary>
    [MaxLength(512)]
    public string? LastTestError { get; set; }

    /// <summary>Period key, e.g. <c>2026-05</c>. Used by the monthly
    /// reset worker to detect rows whose counter belongs to a previous
    /// month and zero them on first observation if the worker missed
    /// the boundary.</summary>
    [MaxLength(8)]
    public string PeriodMonthKey { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }
}

/// <summary>
/// Phase 7 — per-feature provider routing override. When a row exists for a
/// given <see cref="FeatureCode"/> with <see cref="IsActive"/>=true, the
/// gateway routes that feature to <see cref="ProviderCode"/> regardless of
/// failover priority. Missing or inactive rows fall through to the global
/// registry default (highest-priority active <see cref="AiProvider"/>).
///
/// <para>
/// Platform-only feature codes (writing.coach.*, summarise.passage,
/// conversation.reply, …) are filtered server-side from the allowed
/// <see cref="ProviderCode"/> set so admins cannot accidentally point them
/// at a BYOK-only dialect.
/// </para>
/// </summary>
[Index(nameof(FeatureCode), IsUnique = true)]
public class AiFeatureRoute
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Canonical feature code from <c>AiFeatureCodes</c>.</summary>
    [MaxLength(64)]
    public string FeatureCode { get; set; } = default!;

    /// <summary>Provider <see cref="AiProvider.Code"/> to route to.</summary>
    [MaxLength(64)]
    public string ProviderCode { get; set; } = default!;

    /// <summary>
    /// Optional model override. Null = use the provider's
    /// <see cref="AiProvider.DefaultModel"/>.
    /// </summary>
    [MaxLength(128)]
    public string? Model { get; set; }

    /// <summary>When false, the route is ignored and the gateway falls
    /// through to the global default. Lets admins disable a route without
    /// losing its configuration.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }
}
