namespace OetLearner.Api.Services.Seeding;

/// <summary>
/// Z.AI (Zhipu AI / BigModel) GLM provider registration values (owner directive 2026-10-09).
///
/// <para>
/// Sibling of <see cref="OpenCodeProviderDefaults"/>: the two vendors are both OpenAI-compatible
/// GLM hosts and are easy to confuse, so the distinguishing facts live here rather than being
/// re-derived at each call site.
/// </para>
///
/// <para>
/// <b>Key shape.</b> Z.AI issues <c>{32-hex}.{16-char}</c> keys. BOTH the international platform
/// (<c>api.z.ai</c>) and the China platform (<c>open.bigmodel.cn</c>) accept that shape and are
/// separate accounts with separate balances, so the base URL is an explicit owner decision rather
/// than a guess. This file pins the international platform (owner choice, 2026-10-09).
/// </para>
///
/// <para>
/// <b>Registered but not automatic.</b> The row ships with <c>ParticipatesInAutoSelection =
/// false</c>. Z.AI may be the chatbot's PRIMARY and a pipeline's LAST hop, but it is never the
/// implicit "first active credentialed row" for a feature nobody routed — see
/// <see cref="OetLearner.Api.Services.Rulebook.AiProviderDefaultEligibility"/>.
/// </para>
/// </summary>
/// <summary>Z.AI (Zhipu AI / BigModel) GLM provider registration values (owner directive 2026-10-09).</summary>
public static class ZaiProviderDefaults
{
    public const string ProviderCode = "z-ai";
    public const string ProviderName = "Z.AI (GLM)";

    /// <summary>
    /// The owner's GLM **Coding Plan** endpoint (owner decision 2026-10-09).
    ///
    /// <para>
    /// A Coding Plan is a separate entitlement from the pay-as-you-go balance, served on its own base
    /// URL. The general endpoint rejects plan traffic with <c>1113 "Insufficient balance or no
    /// resource package. Please recharge."</c> — which reads exactly like an empty wallet and is not
    /// one. Verified against the live endpoint with the owner's key: <c>/api/paas/v4</c> → 429/1113,
    /// <c>/api/coding/paas/v4</c> → 200.
    /// </para>
    ///
    /// <para>
    /// <b>Known licensing question, decided by the owner.</b> Z.AI's Coding Plan is documented as
    /// limited to officially supported tools (a named list of ~15 coding agents), with their FAQ
    /// directing self-built applications and bots to the standard API and advising billing
    /// accordingly, and their Usage Policy threatening rate limiting, account freezing or ban. The
    /// owner states they have Z.AI's permission to use the plan for this product, and this endpoint is
    /// configured on that basis. Recorded here so the basis of the decision travels with the code.
    /// </para>
    ///
    /// <para>
    /// Only <c>GLM-5.3</c> and <c>GLM-5.3-Flash</c> are plan-callable, so
    /// <see cref="CuratedChatModels"/> is restricted to exactly those two.
    /// </para>
    /// </summary>
    public const string BaseUrl = "https://api.z.ai/api/coding/paas/v4";

    /// <summary>The pay-as-you-go endpoint, kept for reference. Returns 429/1113 on a plan-only key.</summary>
    public const string PayAsYouGoBaseUrl = "https://api.z.ai/api/paas/v4";

        /// <summary>
    /// Owner-directed default (2026-10-09). <b>Measured on the Coding Plan endpoint, not assumed</b>:
    /// <c>glm-5.3-flash</c> passes chat, <c>response_format</c> (valid JSON out), tool calling,
    /// SSE streaming and an image content part. <c>glm-5.3</c> is text-only — an image turn returns
    /// <c>1210 messages.content.type is invalid</c> — so the Flash line is the only model here that
    /// can serve the assistants, which do accept attachments.
    /// </summary>
    public const string DefaultModel = "glm-5.3-flash";

    /// <summary>
    /// Reasoning level for the Coding Plan.
    ///
    /// <para>
    /// Earlier this was justified by Z.AI's documentation saying thinking is forced on and cannot be
    /// disabled, with <c>reasoning_effort</c> defaulting to <c>max</c>. <b>Measured on the coding
    /// endpoint that did not reproduce:</b> at <c>low</c> the model returns zero reasoning characters
    /// and answers directly. <c>low</c> is therefore kept as an explicit floor rather than a
    /// necessity, and the reasoning control is still per-model and probe-driven.
    /// </para>
    /// </summary>
    public const string DefaultReasoningEffort = "low";

    /// <summary>
    /// Behind <c>anthropic</c> (priority 20) so Z.AI is permitted as an implicit default but does
    /// NOT take over anything the owner never explicitly routed. Editable per row from
    /// <c>/admin/ai-providers</c>; this is only the seeded starting point.
    /// </summary>
    public const int FailoverPriority = 100;

    /// <summary>
    /// Models offered in the admin/thread pickers — <b>exactly the two the Coding Plan can call</b>.
    ///
    /// <para>
    /// Z.AI documents that only GLM-5.3 and GLM-5.3-Flash are directly callable on the plan. Older
    /// names are silently aliased onto one of those two, so offering them would be offering a
    /// different model under a misleading label.
    /// </para>
    ///
    /// <para>
    /// The pay-as-you-go free tiers (<c>glm-4.5-flash</c>, <c>glm-4.7-flash</c>) are deliberately NOT
    /// here. They are not in the plan, so pinning one would fail on this base URL, and the default
    /// model is Flash regardless.
    /// </para>
    /// </summary>
    public static readonly string[] CuratedChatModels =
    [
        "glm-5.3-flash",  // owner default; measured: tools, JSON mode, SSE, vision
        "glm-5.3",        // measured: tools, JSON mode, SSE — TEXT ONLY (no image content part)
    ];

    /// <summary>
    /// Nothing is billed per token on this row: the Coding Plan is a flat subscription drawn down in
    /// credits, so a per-1K price would double-count spend already covered by the subscription. The
    /// usage dashboards therefore show zero direct cost for <c>z-ai</c>, which is accurate — the cost
    /// is the monthly plan, not the traffic.
    /// </summary>
    public static readonly Dictionary<string, (decimal PromptPer1k, decimal CompletionPer1k)> RatesPer1k =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["glm-5.3"] = (0m, 0m),
            ["glm-5.3-flash"] = (0m, 0m),
        };

    /// <summary>
    /// Pay-as-you-go rates per 1K, kept only as a reference for what the same models would cost
    /// outside the plan. Not applied to the row.
    /// </summary>
    public static readonly Dictionary<string, (decimal PromptPer1k, decimal CompletionPer1k)> PayAsYouGoRatesPer1k =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["glm-5.3"] = (0.0014m, 0.0044m),      // $1.40 / $4.40 per 1M
            ["glm-5.3-flash"] = (0.00015m, 0.0005m), // $0.15 / $0.50 per 1M
        };

    /// <summary>Zero on a subscription row; see <see cref="RatesPer1k"/>.</summary>
    public static (decimal PromptPer1k, decimal CompletionPer1k) RatesFor(string? model)
        => (0m, 0m);

    public static bool IsFreeModel(string? model) => false;

    public static string AllowedModelsCsv => string.Join(',', CuratedChatModels);

    /// <summary>True for <c>z.ai</c> and any subdomain of it (the international platform).</summary>
    public static bool IsZaiHost(string? host)
        => !string.IsNullOrWhiteSpace(host)
           && (host.Equals("z.ai", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".z.ai", StringComparison.OrdinalIgnoreCase));

    public static bool IsZaiBaseUrl(string? baseUrl)
        => Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && IsZaiHost(uri.Host);

    /// <summary>True for a Z.AI GLM model id. GLM-4.5 and later all expose <c>reasoning_effort</c>;
    /// earlier GLM generations did not, and sending the parameter to one would be a 400.</summary>
    public static bool IsZaiModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return false;
        var m = model.Trim().ToLowerInvariant();
        if (!m.StartsWith("glm-", StringComparison.Ordinal)) return false;
        // Version gate: glm-4.5 is the first generation with the reasoning parameters.
        if (m.StartsWith("glm-4.5", StringComparison.Ordinal)) return true;
        if (m.StartsWith("glm-4.6", StringComparison.Ordinal)) return true;
        if (m.StartsWith("glm-4.7", StringComparison.Ordinal)) return true;
        if (m.StartsWith("glm-5", StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// Request parameters Z.AI rejects, which this codebase would otherwise send.
    ///
    /// <para>
    /// Every item here is a <b>whole-request 400</b> on Z.AI, not a warning, so they are enforced
    /// in the payload builder rather than left to per-call-site discipline. Error code <c>1214</c>.
    /// </para>
    /// </summary>
    public static class RequestLimits
    {
        /// <summary>Z.AI's documented range is (0, 1]. <c>temperature: 0</c> is rejected outright
        /// ("do_sample = False … is not applicable in OpenAI calls"), and a temperature sent for a
        /// thinking model must be positive.</summary>
        public const double MinTemperature = 0.01d;

        /// <summary><c>tool_choice</c> accepts the literal <c>"auto"</c> and nothing else: no
        /// <c>"none"</c>, no <c>"required"</c>, no forced-function object.</summary>
        public const string OnlyToolChoice = "auto";

        /// <summary><c>max_tokens</c> hard ceiling (Z.AI: 128K max output). Not
        /// <c>max_completion_tokens</c>, which Z.AI does not implement.</summary>
        public const int MaxTokensCeiling = 131072;

        /// <summary><c>stream_options</c> / <c>include_usage</c> do not exist on Z.AI — usage
        /// arrives unconditionally on the final SSE chunk. Sending it is at best ignored and at
        /// worst a 400.</summary>
        public const bool SupportsStreamOptions = false;
    }
}