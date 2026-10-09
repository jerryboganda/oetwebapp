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
    /// <summary>
    /// Google Gemini native GenerateContent API. Supports multimodal request
    /// parts, including inline audio used by pronunciation linguistic scoring.
    /// </summary>
    GeminiNative = 4,
    /// <summary>Azure Cognitive Services Speech — TTS.</summary>
    AzureTts = 10,
    /// <summary>ElevenLabs synthesis API.</summary>
    ElevenLabsTts = 11,
    /// <summary>Azure Cognitive Services Speech — ASR (speech-to-text).</summary>
    AzureAsr = 12,
    /// <summary>OpenAI Whisper transcription (or compatible).</summary>
    WhisperAsr = 13,
    /// <summary>Azure Pronunciation Assessment (phoneme scoring).</summary>
    AzurePhoneme = 14,
    /// <summary>ElevenLabs Scribe realtime/batch speech-to-text.</summary>
    ElevenLabsStt = 15,
    /// <summary>
    /// TypeSafe System One ("Jev") typed-judgment API. NOT a chat endpoint: it
    /// has no <c>IAiModelProvider</c> adapter and must never be a gateway
    /// default or route target. The row exists so the admin can hold the
    /// Data-Protection-encrypted key (read via
    /// <c>IAiProviderRegistry.GetPlatformKeyAsync("typesafe-jev")</c>) and
    /// run the <c>GET /v1/models</c> connectivity probe.
    /// </summary>
    TypeSafeJev = 16,
    Mock = 99,
}

/// <summary>Capability category for an <see cref="AiProvider"/> row.
/// Drives the <c>/admin/ai-providers</c> tab filter and (Phase 6+) the
/// voice-selector registry. <c>TextChat</c> is the default for every row
/// pre-Phase 6.</summary>
public enum AiProviderCategory
{
    /// <summary>OpenAI-compatible / Anthropic / Copilot chat completions.</summary>
    TextChat = 0,
    /// <summary>Text-to-speech synthesis.</summary>
    Tts = 1,
    /// <summary>Automatic speech recognition (transcription).</summary>
    Asr = 2,
    /// <summary>Phoneme-level pronunciation scoring.</summary>
    Phoneme = 3,
    /// <summary>Optical character recognition (image / scanned PDF text extraction).</summary>
    Ocr = 4,
    /// <summary>Structured PDF extraction (tables, forms, native-text PDFs).</summary>
    PdfExtraction = 5,
    /// <summary>Typed-judgment providers (TypeSafe Jev). Never chat: excluded
    /// from every text-chat default / provider pick. The Postgres CHECK
    /// <c>CK_AiProviders_Category</c> must list every member (see migration
    /// <c>AllowAiProviderCategoryJudgment</c>).</summary>
    Judgment = 6,
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

    /// <summary>Capability category. Phase 6: lets the admin UI tab the
    /// providers list by capability and lets voice selectors find rows
    /// without mis-classifying a chat provider as a voice provider.
    /// Defaults to <c>TextChat</c> so all pre-Phase-6 rows behave
    /// identically.</summary>
    public AiProviderCategory Category { get; set; } = AiProviderCategory.TextChat;

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

    /// <summary>Comma-separated allow-list of permitted models. Empty = all.
    /// Sized generously (4096 chars) so providers exposing many models —
    /// e.g. DigitalOcean Serverless Inference (~64 model IDs ≈ 1.3 KB) —
    /// can be allow-listed in a single field.</summary>
    [MaxLength(4096)]
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

    /// <summary>
    /// May this row be picked IMPLICITLY — "the first active credentialed row" — by any
    /// default-selection site (<c>AiProviderDefaultEligibility</c>)?
    ///
    /// <para>
    /// <see cref="IsActive"/> and this flag are deliberately independent, giving three states:
    /// an <b>off</b> row (unreachable), a row that is active and keyed but
    /// <see langword="false"/> here (registered and testable, reachable only where an admin
    /// routes it explicitly), and a row that participates in automatic selection.
    /// </para>
    ///
    /// <para>
    /// This replaced a hardcoded code list (<c>OpenCodeProviderDefaults.ExplicitOnlyCodes</c>):
    /// a new vendor row must not silently become the provider for every feature nobody routed.
    /// Owner-editable from <c>/admin/ai-providers</c>, default <see langword="false"/> so a
    /// freshly seeded row is never auto-selected until it is asked for.
    /// </para>
    /// </summary>
    public bool ParticipatesInAutoSelection { get; set; }

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
/// Observed capabilities of ONE model on ONE provider row. Z.AI ships models with genuinely
/// different abilities: <c>glm-5.3-flash</c> is multimodal with a 1M context, <c>glm-5.3</c> is
/// text-only, <c>glm-4.6v</c> has vision but no JSON mode.
///
/// <para>
/// Capabilities therefore cannot live on <see cref="AiProvider"/>: one row serves many models,
/// and a single truthy flag would be a lie for at least one of them.
/// </para>
///
/// <para>
/// Every value here comes from a LIVE probe against the real endpoint with the real key
/// (<c>POST /v1/admin/ai/providers/{code}/probe-capabilities</c>), never copied from vendor
/// documentation — for Z.AI the published schema and the published model page contradict each
/// other about <c>response_format</c>, so only an observed call settles it. A missing row means
/// <b>unknown</b>, and every consumer treats unknown as <b>unsupported</b> (fail closed); see
/// <c>AiFeatureCapabilityRequirements</c>.
/// </para>
/// </summary>
[Index(nameof(ProviderCode), nameof(Model), IsUnique = true)]
public class AiProviderModelCapability
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Provider code (not the surrogate key) so a capability row survives a provider
    /// re-create and so eligibility lookups avoid a join.</summary>
    [MaxLength(64)]
    public string ProviderCode { get; set; } = default!;

    /// <summary>Exact model id as sent in the request body.</summary>
    [MaxLength(128)]
    public string Model { get; set; } = default!;

    /// <summary>Accepts <c>tools</c> and returns <c>tool_calls</c>.</summary>
    public bool SupportsTools { get; set; }

    /// <summary>Accepts <c>{"type":"image_url"}</c> content parts (base64 data URL).</summary>
    public bool SupportsVision { get; set; }

    /// <summary>Accepts a document/file content part. Distinct from vision: a provider may take
    /// images but not documents, and this codebase folds documents to text
    /// (<c>AiProviderPayloadBuilder.BuildDocumentFold</c>) rather than sending bytes.</summary>
    public bool SupportsDocuments { get; set; }

    /// <summary>Honours <c>response_format</c> (json_object / json_schema).</summary>
    public bool SupportsJsonMode { get; set; }

    /// <summary>Serves a vector-embedding endpoint. Deliberately a probed flag rather than a
    /// hardcoded vendor exception: <c>embeddings.generate</c> is a routable feature code, and
    /// several chat vendors (Z.AI among them) expose no <c>/embeddings</c> route at all.</summary>
    public bool SupportsEmbeddings { get; set; }

    /// <summary>Streams via SSE. When <see langword="false"/> the caller must take the buffered
    /// path — the assistant's fake-stream burst slicing is the visible symptom of getting this
    /// wrong.</summary>
    public bool SupportsStreaming { get; set; }

    /// <summary>Understands the <c>thinking</c> parameter and emits <c>reasoning_content</c>.</summary>
    public bool SupportsThinking { get; set; }

    /// <summary>Can thinking actually be switched OFF. Z.AI documents GLM-5.3 and GLM-5.3-FLASH
    /// as forced-on and not disableable, so a control offering "off" for those would be lying —
    /// the UI renders them locked and this flag is the reason it shows.</summary>
    public bool ThinkingCanBeDisabled { get; set; }

    /// <summary>Comma-separated <c>reasoning_effort</c> values the model accepts, e.g.
    /// <c>max,xhigh,high,medium,low,minimal,none</c>. Empty = the model has no reasoning dial.</summary>
    [MaxLength(256)]
    public string AllowedReasoningEffortsCsv { get; set; } = string.Empty;

    /// <summary>Observed hard ceiling on <c>max_tokens</c>, or 0 when unknown.</summary>
    public int MaxTokensCeiling { get; set; }

    /// <summary>Documented context window, or 0 when unknown.</summary>
    public int ContextTokens { get; set; }

    /// <summary>Probe outcome: <c>ok</c>, <c>partial</c>, <c>auth</c>, <c>rate_limited</c>,
    /// <c>network</c> or <c>unknown</c>. Only <c>ok</c> and <c>partial</c> carry usable
    /// capabilities; anything else leaves every flag false so callers fail closed.</summary>
    [MaxLength(32)]
    public string ProbeStatus { get; set; } = "unknown";

    /// <summary>Human-readable probe notes — which sub-probe failed and why. Capped at 512 so a
    /// stack trace never reaches the UI.</summary>
    [MaxLength(512)]
    public string? ProbeDetail { get; set; }

    public DateTimeOffset? ProbedAtUtc { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
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
