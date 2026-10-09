namespace OetLearner.Api.Services.Seeding;

// OpenCode inference-gateway provider (owner directive 2026-10-04, docs/AI-USAGE-POLICY.md §21).
//
// The learner chatbot reaches OpenCode through its OpenAI-compatible HTTP gateway (no CLI, no
// sidecar) using the platform's own paid gateway API key, which the owner pastes into
// /admin/ai-providers (encrypted, never returned). The row ships INACTIVE and KEYLESS: the
// OpenCode terms ("own internal use only") and the TV-029 provider inventory are owner gates.
//
// The provider is reachable ONLY through an explicit choice (the per-thread model picker). It is
// never an implicit default for any feature, whatever its failover priority — see
// AiProviderDefaultEligibility.

/// <summary>Canonical OpenCode provider registration values and routing constants.</summary>
public static class OpenCodeProviderDefaults
{
    public const string ProviderCode = "opencode";
    public const string ProviderName = "OpenCode (inference gateway)";

    /// <summary>Zen: pay-as-you-go credits.</summary>
    public const string ZenBaseUrl = "https://opencode.ai/zen/v1";

    /// <summary>Go: subscription. Same key shape; entitlement differs from Zen.</summary>
    public const string GoBaseUrl = "https://opencode.ai/zen/go/v1";

    /// <summary>Owner-directed learner default (DECISION_LOG D-005, 2026-10-07):
    /// deepseek-v4.1-flash with reasoning_effort=max on the chat-completions protocol.</summary>
    public const string DefaultModel = "deepseek-v4.1-flash";

    /// <summary>Valid for both curated model families (low / high / max). Bounds forced thinking
    /// so the answer is not starved of output tokens. "max" per owner directive D-005.</summary>
    public const string DefaultReasoningEffort = "max";

    /// <summary>Highest number = last. Combined with the explicit-only guard, so the row can never
    /// win an implicit "first active row" pick.</summary>
    public const int FailoverPriority = 900;

    /// <summary>Documented per-1M-token rates for <see cref="DefaultModel"/>
    /// (deepseek-v4.1-flash: $0.14 in / $0.28 out, checked 2026-10-07 — the gateway passes the
    /// native DeepSeek rates through at zero markup), expressed per 1K tokens for the pricing
    /// columns. Row-level columns: glm calls are costed at these rates unless an admin adjusts
    /// them; the own documented glm-5.3-flash rates ($0.15 in / $0.50 out per 1M) are near-identical.</summary>
    public const decimal DefaultPricePer1kPromptTokens = 0.00014m;
    public const decimal DefaultPricePer1kCompletionTokens = 0.00028m;

    /// <summary>Models served on the chat-completions protocol that this client speaks. Anything
    /// else on the gateway uses /responses, /messages or a Google protocol and would be refused.
    /// Free gateway models may train on submitted data and must never be listed here.</summary>
    public static readonly string[] CuratedChatModels = ["deepseek-v4.1-flash", "glm-5.3-flash", "glm-5.3"];

    public static string AllowedModelsCsv => string.Join(',', CuratedChatModels);

    /// <summary>Exactly what a learner sees when an OpenCode turn fails for any provider reason
    /// (owner wording). The specific failure class goes to the usage record, never to the learner.</summary>
    public const string LearnerBusyMessage =
        "The AI provider service is busy at the moment. Please try again later after a few minutes.";

/// <summary>RETIRED (2026-10-09): auto-selection eligibility is now per-row state —
/// <c>AiProvider.ParticipatesInAutoSelection</c>, owner-editable from /admin/ai-providers — so this
/// list is no longer read by any default-selection site. The one behaviour it could express that
/// stored state cannot, "a DELETED row must refuse rather than fall through to the mock provider",
/// is frozen as <c>AiAssistantGateway.RequiredActiveRowCodes</c>. Kept for reference only; do not
/// add codes here, add them there.</summary>
[Obsolete("Replaced by AiProvider.ParticipatesInAutoSelection; see AiAssistantGateway.RequiredActiveRowCodes for the absence case.")]
    public static readonly string[] ExplicitOnlyCodes = [ProviderCode];

    /// <summary>True for <c>opencode.ai</c> and any subdomain of it.</summary>
    public static bool IsOpenCodeHost(string? host)
        => !string.IsNullOrWhiteSpace(host)
           && (host.Equals("opencode.ai", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".opencode.ai", StringComparison.OrdinalIgnoreCase));

    /// <summary>True when <paramref name="baseUrl"/> is an absolute URL on an OpenCode host.</summary>
    public static bool IsOpenCodeBaseUrl(string? baseUrl)
        => Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && IsOpenCodeHost(uri.Host);

    public static bool IsDirectGatewayBaseUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https"
           && uri.Host == "opencode.ai" && uri.IsDefaultPort && uri.UserInfo.Length == 0
           && uri.Query.Length == 0 && uri.Fragment.Length == 0
           && uri.AbsolutePath.TrimEnd('/') is "/zen/go/v1" or "/zen/v1";
}
