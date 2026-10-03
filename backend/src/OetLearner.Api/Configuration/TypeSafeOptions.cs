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

    // ── Writing-pilot per-surface switches (all default OFF) ────────────────
    // Each Writing-pilot call site checks ITS OWN flag before spending any
    // jev tokens; flipping <see cref="Enabled"/> alone does nothing. Every
    // flag only ever turns on an advisory/guard surface — never a grader.

    /// <summary>Pre-gateway submission guard (parallel Nouls: injection /
    /// rule-evasion / abuse / gibberish). Negative gate only — it may block
    /// or flag for tutor review, never auto-pass anything.</summary>
    public bool WritingGuardEnabled { get; set; } = false;

    /// <summary>Confidence-gated request routing on <c>/v1/ai/complete</c>
    /// for Writing kinds. When on AND the router answers with confidence at
    /// or above <see cref="RouteConfidenceThreshold"/>, the grounded prompt
    /// task and feature code are realigned to the routed target; otherwise
    /// the caller's explicit request stands untouched.</summary>
    public bool WritingRouteEnabled { get; set; } = false;

    /// <summary>Post-grading citation verification (per AI finding: supported
    /// / contradicted / not-in-evidence). Contradicted or low-confidence
    /// findings flag the grade for tutor review via
    /// <c>WritingGrade.ConfidenceFlag</c> + a pending tutor assignment.</summary>
    public bool WritingVerifyEnabled { get; set; } = false;

    /// <summary>Advisory per-criterion jev Scores merged into the grade's
    /// per-criterion feedback JSON as an extra <c>jevAdvisory</c> field per
    /// criterion. Display-only; the mapper ignores unknown fields, and no
    /// scoring path reads it.</summary>
    public bool WritingCriteriaEnabled { get; set; } = false;

    /// <summary>Companion retrieval rerank: one jev Score per hybrid-search
    /// candidate (downstream of the entitlement prefilter) reorders the
    /// evidence by semantic relevance to the query. Ordering-only — never a
    /// filter of record, never touches grading, and a rerank outage keeps
    /// the original hybrid ordering.</summary>
    public bool CompanionRerankEnabled { get; set; } = false;

    /// <summary>Advisory conversation-turn judgment (stays-in-role /
    /// clinically-appropriate / unsafe Nouls) run alongside AI-patient reply
    /// generation. Informational only — never gates, scores, or ends a
    /// session.</summary>
    public bool ConversationAdvisoryEnabled { get; set; } = false;

    /// <summary>Owner decision 2 (Max-always-on): when false (default) a guard
    /// Block verdict is only recorded and flagged to a tutor while the letter is
    /// STILL graded by Max. Set true only with explicit owner approval, which
    /// restores the old behaviour of skipping the paid grade for that letter.</summary>
    public bool WritingGuardEnforced { get; set; } = false;

    /// <summary>Writing pass/fail Noul cross-check beside the grader's verdict
    /// (<c>jev.writing.outcome</c>). Advisory: a flip only flags tutor review.</summary>
    public bool WritingOutcomeEnabled { get; set; } = false;

    /// <summary>Writing finding classification (<c>jev.writing.findings</c>):
    /// criterion Choice and valid-alternative Noul per AI finding.</summary>
    public bool WritingFindingsEnabled { get; set; } = false;

    /// <summary>Speaking pre-grade readiness Nouls (<c>jev.speaking.readiness</c>).
    /// Negative flag only; never skips the pinned Max grade.</summary>
    public bool SpeakingReadinessEnabled { get; set; } = false;

    /// <summary>Speaking post-grade criterion cross-check
    /// (<c>jev.speaking.crosscheck</c>). Never changes a number.</summary>
    public bool SpeakingCrosscheckEnabled { get; set; } = false;

    /// <summary>Outcome cross-check: the Noul answer must be at least this far
    /// from 0.5 (i.e. confident either way) before a flip counts.</summary>
    public double OutcomeConfidenceThreshold { get; set; } = 0.70;

    /// <summary>Cross-checks: normalised distance (0..1 of the criterion scale)
    /// between the grader's score and Jev's position that counts as divergence.</summary>
    public double CrosscheckDivergenceThreshold { get; set; } = 0.34;

    /// <summary>Cross-checks: minimum Jev confidence for a Score/Choice to be
    /// used at all; lower confidence is treated as no signal.</summary>
    public double CrosscheckConfidenceThreshold { get; set; } = 0.60;

    /// <summary>Speaking readiness: any single readiness Noul at or above this
    /// flags the submission for tutor attention.</summary>
    public double ReadinessFlagThreshold { get; set; } = 0.80;

    public bool ResponseVerifyEnabled { get; set; } = false;
    public double ResponseConfidenceThreshold { get; set; } = 0.80;
    public bool DevelopmentTriageEnabled { get; set; } = false;
    public double DevelopmentConfidenceThreshold { get; set; } = 0.80;

    /// <summary>Guard: any single guard Noul at or above this blocks the
    /// submission from reaching the paid AI grade (flagged to a human).</summary>
    public double GuardBlockThreshold { get; set; } = 0.80;

    /// <summary>Guard: at or above this (but below the block threshold) the
    /// submission proceeds but is logged for review calibration.</summary>
    public double GuardReviewThreshold { get; set; } = 0.50;

    /// <summary>Route: the router's Choice confidence must reach this before
    /// the routed target may override the caller's explicit request.</summary>
    public double RouteConfidenceThreshold { get; set; } = 0.70;

    /// <summary>Verify: a "supported" verdict below this confidence counts
    /// as unproven and flags the grade for tutor review, same as a
    /// contradicted verdict.</summary>
    public double VerifyConfidenceThreshold { get; set; } = 0.60;

    /// <summary>Verify: maximum findings verified in one fan-out call. The
    /// state carries every finding, so this bounds the context; findings
    /// beyond the cap are left unverified rather than verified blind.</summary>
    public int VerifyMaxFindingsPerCall { get; set; } = 12;

    /// <summary>Circuit breaker: consecutive failed sends (after in-call
    /// retries) before sends short-circuit platform-wide. Any success resets
    /// the streak. Callers already treat failures as fail-soft Unavailable,
    /// so an open breaker only saves the timeout wait.</summary>
    public int BreakerFailureThreshold { get; set; } = 5;

    /// <summary>Circuit breaker: how long sends stay short-circuited after
    /// the failure threshold trips.</summary>
    public int BreakerCooldownSeconds { get; set; } = 30;
}
