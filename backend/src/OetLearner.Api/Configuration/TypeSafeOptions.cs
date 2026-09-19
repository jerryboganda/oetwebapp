namespace OetLearner.Api.Configuration;

/// <summary>
/// TypeSafe SystemOne (model "Jev") judgment calls — the direct, non-gateway
/// AI surface that returns typed judgments (Choice / Noul / Score) instead of
/// generated text. See <c>Services/Ai/TypeSafe/</c>.
///
/// <para>
/// Jev is a judgment layer, never a grader: it never overrides the
/// rulebook-grounded gateway verdicts, the deterministic WritingRuleEngine,
/// or the GEPA placement engine. Code owns every weight, threshold, and
/// pass/fail decision — the model only supplies probabilities.
/// </para>
///
/// <para>
/// The API key is server-side only and is read from configuration
/// (<c>TypeSafe__ApiKey</c> in the VPS <c>.env.production</c>, or the local
/// dev env). It must never appear in a client bundle, a log line, or a
/// tracked file. <see cref="Enabled"/> is the master kill-switch; every
/// judgment caller must treat a disabled/unavailable result as "carry on
/// without the judgment" — never as a learner-visible failure.
/// </para>
/// </summary>
public sealed class TypeSafeOptions
{
    public const string SectionName = "TypeSafe";

    /// <summary>Provider code used on AiUsageRecord rows (analytics grouping).</summary>
    public const string ProviderCode = "typesafe-jev";

    /// <summary>Master kill-switch. Default false — production must opt in
    /// explicitly via <c>TypeSafe__Enabled=true</c> after calibration.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Bearer key for <c>https://api.typesafe.ai</c>. Empty in the
    /// committed appsettings; the live value exists only in the VPS env.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Pinned model id, deliberately NOT the <c>jev-latest</c>
    /// alias: judgment thresholds are tuned against a fixed version, and the
    /// alias can silently move under them. Bump deliberately.</summary>
    public string Model { get; set; } = "jev-1.13.0";

    /// <summary>API base URL. Overridable for tests.</summary>
    public string BaseUrl { get; set; } = "https://api.typesafe.ai";

    /// <summary>Per-attempt HTTP timeout. Jev judgments are latency-sensitive
    /// guard/route calls; a slow answer is worthless, so this is tight.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Extra attempts after the first on 429/529 only (exponential
    /// backoff). 401/422 are configuration bugs — never retried.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>TypeSafe price per input token in USD (2026-09 rate card:
    /// $42 per billion input tokens; output tokens are free). Used only to
    /// meter the budget hold on AiUsageRecord rows.</summary>
    public decimal CostPerInputTokenUsd { get; set; } = 0.000000042m;
}
