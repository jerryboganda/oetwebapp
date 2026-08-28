using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// What a caller that lost the idempotency-key insert is allowed to do next.
/// </summary>
public enum AiOperationReplayDecision
{
    /// <summary>The existing operation is still running. Keep waiting — this is
    /// the concurrent/immediate duplicate case and it must NEVER call a
    /// provider a second time.</summary>
    WaitInFlight = 0,

    /// <summary>The existing operation is terminal and repeating it is not
    /// safe (a recent completion, or an outcome we cannot prove was never
    /// billed). The caller gets the truthful duplicate outcome, never a second
    /// provider call.</summary>
    Duplicate = 1,

    /// <summary>The existing operation is terminal AND provably safe to redo
    /// (a clean failure/cancellation, or a completion old enough to be outside
    /// the replay window). The caller may open a NEW operation by bumping the
    /// replay discriminator.</summary>
    CreateNewAttempt = 2,
}

/// <summary>
/// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// state- and time-bounded replay rules for
/// <see cref="AiExecutionCoordinator"/>.
///
/// <para>
/// The defect this fixes: an idempotency key is derived from stable request
/// content, so once ANY operation existed for a given canonical action the key
/// was taken forever. A terminal failure, or a completion from last month,
/// permanently refused every future identical request — the caller could only
/// ever be told "duplicate result unavailable". That is a lockout, not
/// idempotency.
/// </para>
///
/// <para>
/// The rules below keep the property that actually protects money — a
/// concurrent or immediate duplicate never reaches a provider twice — while
/// letting genuinely re-runnable work through:
/// <list type="bullet">
///   <item>non-terminal ⇒ <see cref="AiOperationReplayDecision.WaitInFlight"/>;
///   the winner is still working, so the loser waits and never calls;</item>
///   <item><see cref="AiOperationState.Completed"/> INSIDE the replay window
///   ⇒ <see cref="AiOperationReplayDecision.Duplicate"/>; this is the
///   double-submit / retry-storm case the incident was about;</item>
///   <item><see cref="AiOperationState.Completed"/> OUTSIDE the window ⇒
///   <see cref="AiOperationReplayDecision.CreateNewAttempt"/>; a learner
///   legitimately re-requesting the same action tomorrow is new work;</item>
///   <item><see cref="AiOperationState.FailedTerminal"/> /
///   <see cref="AiOperationState.Cancelled"/> ⇒
///   <see cref="AiOperationReplayDecision.CreateNewAttempt"/>; these are the
///   two states that prove no usable result exists AND no ambiguity;</item>
///   <item><see cref="AiOperationState.Indeterminate"/> ⇒
///   <see cref="AiOperationReplayDecision.Duplicate"/>, ALWAYS. An ambiguous
///   outcome may already have been billed, so it is never auto-retried; a
///   human/ops reconciliation must move it to a decided state first.</item>
/// </list>
/// </para>
/// </summary>
public static class AiOperationReplayPolicy
{
    /// <summary>Default "same action, right now" window. Short on purpose: it
    /// exists to absorb double-clicks, client retries and worker overlap, not
    /// to cache results.</summary>
    public static readonly TimeSpan DefaultReplayWindow = TimeSpan.FromMinutes(5);

    /// <summary>Hard ceiling on the configurable window so a mis-set
    /// configuration value cannot recreate the permanent lockout.</summary>
    public static readonly TimeSpan MaxReplayWindow = TimeSpan.FromHours(24);

    /// <summary>Default number of replay discriminator bumps attempted before
    /// giving up. Mirrors <c>DirectAiCallRecorder.MaxRetryAfterFailureRounds</c>
    /// so both control-plane entry points behave the same way.</summary>
    public const int DefaultMaxReplayRounds = 5;

    /// <summary>Hard ceiling on the configurable round count — this is a
    /// bounded walk over stale rows, never a retry loop.</summary>
    public const int MaxReplayRoundsCeiling = 10;

    public static bool IsTerminal(AiOperationState state) => state is
        AiOperationState.Completed or
        AiOperationState.FailedTerminal or
        AiOperationState.BlockedBudget or
        AiOperationState.Indeterminate or
        AiOperationState.SkippedNoEvidence or
        AiOperationState.Cancelled;

    /// <summary>
    /// Safe-failure states: the operation is closed, produced no durable
    /// result, and — critically — we can prove it is not an ambiguous
    /// possibly-billed call. Only these may be replaced by a new attempt.
    /// </summary>
    public static bool IsSafeFailure(AiOperationState state) => state is
        AiOperationState.FailedTerminal or
        AiOperationState.Cancelled;

    public static AiOperationReplayDecision Decide(
        AiOperationState state,
        DateTimeOffset terminalAt,
        DateTimeOffset now,
        TimeSpan replayWindow)
    {
        if (!IsTerminal(state)) return AiOperationReplayDecision.WaitInFlight;
        if (IsSafeFailure(state)) return AiOperationReplayDecision.CreateNewAttempt;

        // Completed is the only state where age matters. Everything else that
        // reaches here (Indeterminate / BlockedBudget / SkippedNoEvidence) is
        // deliberately non-replayable: either it may already have been billed,
        // or a different layer (W3 budget, evidence gate) owns re-arming it.
        if (state != AiOperationState.Completed) return AiOperationReplayDecision.Duplicate;

        var window = Clamp(replayWindow);
        // Guard against clock skew making a fresh completion look ancient.
        var age = now - terminalAt;
        return age > window ? AiOperationReplayDecision.CreateNewAttempt : AiOperationReplayDecision.Duplicate;
    }

    /// <summary>Bound a configured window into [0, <see cref="MaxReplayWindow"/>].</summary>
    public static TimeSpan Clamp(TimeSpan replayWindow)
        => replayWindow < TimeSpan.Zero ? TimeSpan.Zero
            : replayWindow > MaxReplayWindow ? MaxReplayWindow
            : replayWindow;

    /// <summary>Bound a configured round count into [1, <see cref="MaxReplayRoundsCeiling"/>].</summary>
    public static int ClampRounds(int rounds)
        => rounds < 1 ? 1 : rounds > MaxReplayRoundsCeiling ? MaxReplayRoundsCeiling : rounds;

    /// <summary>
    /// Deterministic replay discriminator bump. Identical to the proven walk in
    /// <c>DirectAiCallRecorder.BeginOperationAsync</c>: the value participates
    /// through the existing <c>ResourceVersion</c> dimension, so it changes BOTH
    /// the idempotency key and the resource slot and therefore cannot collide
    /// with the row it is replacing.
    /// </summary>
    public static int NextVersion(int? current) => (current ?? 1) + 1;
}
