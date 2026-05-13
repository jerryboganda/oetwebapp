using System.Collections.Immutable;

namespace OetLearner.Api.Services.Listening;

/// <summary>
/// Canonical per-window timing defaults for the Listening V2 session FSM
/// (CBT-strict mode). Reserved by <c>PRD-LISTENING-V2.md</c> §5.2 so that
/// the ListeningPolicy migration in WS-A can seed these values into the
/// new policy columns, and the FSM service in WS-B can fall back to them
/// when an authored ListeningPolicy row is missing.
///
/// All values are milliseconds. They mirror the R-code rulebook:
/// <list type="bullet">
///   <item>R05 — preview / playback / review window durations.</item>
///   <item>R06 — confirm-token TTL, between-section transition.</item>
///   <item>R07 — paper-mode final all-parts review window.</item>
///   <item>R10 — tech-readiness probe TTL.</item>
/// </list>
///
/// IMPORTANT: This file holds defaults ONLY. It does NOT replace the
/// ListeningPolicy entity. The runtime FSM must read effective values from
/// ListeningPolicy + ListeningUserPolicyOverride first, then fall back here.
/// </summary>
internal static class ListeningPolicyDefaults
{
    // R05 — Preview window (caller silence period before audio starts).
    public const int PreviewMsA1 = 30_000;
    public const int PreviewMsA2 = 30_000;
    public const int PreviewMsC1 = 90_000;
    public const int PreviewMsC2 = 60_000;

    // R05 — Review window (final answer-edit period inside the section
    // before locking the section per R06.1).
    public const int ReviewMsA1 = 75_000;
    public const int ReviewMsA2 = 75_000;
    public const int ReviewMsC1 = 30_000;
    public const int ReviewMsC2FinalCbt = 120_000;
    public const int ReviewMsC2FinalPaper = 120_000;

    // R06 — Inter-section transition window (cool-down + intro audio cue).
    public const int BetweenSectionTransitionMs = 40_000;

    // R04.6 — Part B per-question prompt window (10–15 s pause cue).
    public const int PartBQuestionWindowMs = 15_000;

    // R06.10 — Confirm-token TTL for the two-step section advance protocol.
    // First /advance call returns 412 + signed token; second call must echo
    // the token within this window.
    public const int ConfirmTokenTtlMs = 30_000;

    // R10 — Tech-readiness probe result freshness window (15 min).
    public const int TechReadinessTtlMs = 900_000;

    // R07.3 — Paper-mode all-parts final review window override (sticky red
    // banner kicks in when remaining time drops below this).
    public const int FinalReviewAllPartsMsPaper = 120_000;

    /// <summary>Per-Part (preview, review) tuple lookup keyed by part code.
    /// Frozen at static-ctor time so FSM transitions never allocate.</summary>
    public static readonly ImmutableDictionary<string, (int PreviewMs, int ReviewMs)> PerPart =
        ImmutableDictionary.CreateRange(new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase)
        {
            ["A1"] = (PreviewMsA1, ReviewMsA1),
            ["A2"] = (PreviewMsA2, ReviewMsA2),
            ["C1"] = (PreviewMsC1, ReviewMsC1),
            ["C2"] = (PreviewMsC2, ReviewMsC2FinalCbt),
        });
}
