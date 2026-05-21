namespace OetLearner.Api.Configuration;

/// <summary>
/// Phase 12 (P12) of the OET Speaking module plan — release gating.
///
/// Strongly-typed binder for the <c>Features</c> configuration section.
/// The <see cref="SpeakingV2"/> flag is the master kill-switch for the
/// rebuilt Speaking academy (warm-up + AI patient turn loop + LiveKit
/// Cloud + dual scoring + calibration drift + admin analytics + content
/// library). Default is <c>false</c> so production deploys must opt-in
/// explicitly — usually via <c>Features__SpeakingV2 = true</c> env var or
/// the matching JSON key.
///
/// Rollout cadence (per plan section 12.5):
///   1. Enable in staging via env var.
///   2. Pilot 5 % cohort in production.
///   3. Monitor LiveKit egress + Anthropic cache-hit rate + tutor drift
///      for 7 days.
///   4. Flip globally.
///
/// TODO Agent I: register in Program.cs via
/// <c>builder.Services.Configure&lt;FeatureFlagOptions&gt;(builder.Configuration.GetSection("Features"));</c>
/// once the orchestrator can land the DI line. This file is intentionally
/// the only production source the test/runbook phase touches.
/// </summary>
public sealed class FeatureFlagOptions
{
    /// <summary>Configuration section name. Matches the existing
    /// <c>"Features"</c> object in <c>appsettings.json</c> so the new
    /// flag co-exists with <c>EnableSwagger</c>.</summary>
    public const string SectionName = "Features";

    /// <summary>
    /// Master gate for the Phase 1–11 Speaking module rebuild. When
    /// <c>false</c>, every new Speaking surface (warm-up, AI patient turn
    /// loop, dual scoring UI, calibration drift dashboard, admin analytics
    /// speaking sub-page) hides itself, and the legacy
    /// <c>app/speaking/task/[id]</c> flow keeps serving the previous UX.
    /// </summary>
    public bool SpeakingV2 { get; set; } = false;

    /// <summary>
    /// Preserved from the pre-Phase-12 configuration so existing callers
    /// (Swagger UI exposure) keep binding through the same section.
    /// </summary>
    public bool EnableSwagger { get; set; } = false;
}
