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
public static class ZaiProviderDefaults
{
    public const string ProviderCode = "z-ai";
    public const string ProviderName = "Z.AI (GLM)";

    /// <summary>International Z.AI OpenAI-compatible surface. Global pay-as-you-go; the only
    /// documented general (non-Coding-Plan) endpoint. The China platform would be
    /// <c>https://open.bigmodel.cn/api/paas/v4</c> with its own balance.</summary>
    public const string BaseUrl = "https://api.z.ai/api/paas/v4";

    /// <summary>
    /// Owner-directed default (2026-10-09). Chosen over the text-only <c>glm-5.3</c> because the
    /// assistant sends image and document attachments and several call sites emit strict JSON:
    /// only the Flash line is multimodal AND JSON-capable at $0.15/$0.50 per 1M.
    /// </summary>
    public const string DefaultModel = "glm-5.3-flash";

    /// <summary>
    /// Thinking is FORCED and cannot be disabled on GLM-5.3 / GLM-5.3-FLASH, and
    /// <c>reasoning_effort</c> defaults to <c>max</c>. Reasoning output is billed inside
    /// <c>completion_tokens</c> at the model's normal output rate (Z.AI's <c>usage</c> object has
    /// no separate reasoning field), so <c>max</c> on a high-traffic surface is expensive.
    /// <c>low</c> is the only lever — the model will not go below it and will not go off.
    /// Owner decision, 2026-10-09, with alert-only spend monitoring (never a hard stop).
    /// </summary>
    public const string DefaultReasoningEffort = "low";

    /// <summary>
    /// Behind <c>anthropic</c> (priority 20) so Z.AI is permitted as an implicit default but does
    /// NOT take over anything the owner never explicitly routed. Editable per row from
    /// <c>/admin/ai-providers</c>; this is only the seeded starting point.
    /// </summary>
    public const int FailoverPriority = 100;

    /// <summary>
    /// Models offered in the admin/thread pickers. <b>Free</b> entries are surfaced with an honest
    /// "free" label and are never made a default: a vendor's free tier is the first thing it
    /// changes, and the learner chatbot must not depend on that.
    ///
    /// <para>
    /// Do NOT extend this list with a vision model lacking JSON mode (<c>glm-4.6v</c>,
    /// <c>glm-4.5v</c>): those bind to a request schema with no <c>response_format</c> at all, so
    /// they would silently break every strict-JSON call site. The capability probe is the authority
    /// on what any given model can actually do, not this list.
    /// </para>
    /// </summary>
    public static readonly string[] CuratedChatModels =
    [
        "glm-5.3-flash",  // owner default; multimodal, tools, JSON mode, 1M context
        "glm-5.3",        // flagship text, tools, JSON mode — no vision
        "glm-5.2",
        "glm-5.1",
        "glm-4.7",
        "glm-4.6",
        "glm-4.7-flash",  // free to call
        "glm-4.5-flash",  // free to call
    ];

    /// <summary>Models Z.AI bills nothing for (all four price columns free, no expiry published).</summary>
    public static readonly HashSet<string> FreeModels =
        new(["glm-4.7-flash", "glm-4.5-flash"], StringComparer.OrdinalIgnoreCase);

    public static string AllowedModelsCsv => string.Join(',', CuratedChatModels);

    /// <summary>
    /// Published rates, USD per 1M tokens, read from the Z.AI pricing page on 2026-10-09 and
    /// converted to the per-1K columns <c>AiProviders</c> stores.
    ///
    /// <para>
    /// <b>Why this table exists.</b> The env bootstrapper used to hardcode 0.015 / 0.075 per 1K,
    /// which is $15 / $75 per 1M — <b>100x too high in both directions</b> for every current Z.AI
    /// model. A cost screen reading those numbers is not merely imprecise, it is actively
    /// misleading, and it is what you would use to judge whether the last-resort pipeline hops are
    /// firing more than expected.
    /// </para>
    ///
    /// <para>
    /// Reasoning/thinking tokens are metered inside <c>completion_tokens</c>, so a forced-thinking
    /// model costs more than its table row suggests — that is what the spend alert is for.
    /// </para>
    /// </summary>
    public static readonly Dictionary<string, (decimal PromptPer1k, decimal CompletionPer1k)> RatesPer1k =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // $1.40 / $4.40 per 1M
            ["glm-5.3"] = (0.0014m, 0.0044m),
            ["glm-5.3-flash"] = (0.00015m, 0.0005m),   // $0.15 / $0.50
            ["glm-5.3-flashx"] = (0.00037m, 0.00125m),
            ["glm-5.2"] = (0.0014m, 0.0044m),
            ["glm-5.1"] = (0.0014m, 0.0044m),
            ["glm-5"] = (0.0010m, 0.0032m),
            ["glm-4.7"] = (0.0006m, 0.0022m),
            ["glm-4.7-flash"] = (0m, 0m),               // free to call
            ["glm-4.7-flashx"] = (0.00007m, 0.0004m),
            ["glm-4.6"] = (0.0006m, 0.0022m),
            ["glm-4.6v"] = (0.0003m, 0.0009m),
            ["glm-4.6v-flash"] = (0m, 0m),
            ["glm-4.5"] = (0.0006m, 0.0022m),
            ["glm-4.5-air"] = (0.0002m, 0.0011m),
            ["glm-4.5-flash"] = (0m, 0m),               // free to call
            ["glm-4.5v"] = (0.0006m, 0.0018m),
        };

    /// <summary>Rates for <paramref name="model"/>, falling back to the owner's default model's
    /// rates rather than to zero — an unknown model must not look free.</summary>
    public static (decimal PromptPer1k, decimal CompletionPer1k) RatesFor(string? model)
        => !string.IsNullOrWhiteSpace(model) && RatesPer1k.TryGetValue(model.Trim(), out var rates)
            ? rates
            : RatesPer1k[DefaultModel];

    public static bool IsFreeModel(string? model)
        => !string.IsNullOrWhiteSpace(model) && FreeModels.Contains(model.Trim());

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