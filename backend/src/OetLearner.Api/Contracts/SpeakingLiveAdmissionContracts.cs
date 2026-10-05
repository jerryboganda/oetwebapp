namespace OetLearner.Api.Contracts;

// Live AI Speaking admission control (owner decision 5 Oct 2026).
//
// A learner who asks to start an AI exam (finish-intro) or an AI practice card (finish-warmup) while the
// live-session cap is full is NOT started: the response keeps the unscored state (exam "intro", session
// "warmup") and carries `admission` so the page can show the line. Nothing is timed and no credit is held
// while waiting. The page retries the same call; the call that finds a free place is the one that holds the
// credit and starts the clock.

/// <summary>Where a learner stands in the live-session line. Only ever sent while <c>Status</c> is
/// <c>waiting</c>; an admitted learner gets the normal next state and no <c>admission</c> at all.</summary>
public sealed record SpeakingLiveAdmissionView(
    string Status,
    int Position,
    int QueueLength,
    int EstimatedWaitSeconds,
    int PollAfterSeconds)
{
    public const string WaitingStatus = "waiting";
}

/// <summary>Admin view of the gate: the effective cap and what is in it right now. Counts come from the
/// database (all API slots and the worker), never from process memory.</summary>
public sealed record SpeakingLiveAdmissionCounts(
    bool Enabled,
    int MaxConcurrent,
    string Source,
    int Admitted,
    int Waiting,
    int Free,
    DateTimeOffset? OldestWaitingSince,
    int? OldestWaitSeconds);

/// <summary><c>PUT /v1/admin/ai/live-voice/admission</c>. Either field may be omitted to leave it unchanged.</summary>
public sealed record UpdateSpeakingLiveAdmissionRequest(bool? Enabled, int? MaxConcurrent);
