namespace OetLearner.Api.Configuration;

/// <summary>
/// Live AI Speaking admission control (owner decision 5 Oct 2026). Bound from <c>Speaking:LiveAdmission</c>
/// (env <c>Speaking__LiveAdmission__*</c>). The owner-tunable cap and kill switch live in the
/// <c>SpeakingLiveAdmissionSettings</c> row (admin API, no restart); these values are the defaults when
/// that row is absent, plus the timing of the queue itself.
///
/// The gate counts live AI patient sessions (an AI exam or a standalone AI practice card) that were
/// ADMITTED and are still running. It never evicts a running session, never starts a clock and never
/// holds a credit for a waiting learner.
/// </summary>
public sealed class SpeakingLiveAdmissionOptions
{
    public const string SectionName = "Speaking:LiveAdmission";

    /// <summary>Hard environment kill switch, on top of the admin one. False = the gate never queues.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Concurrent live AI Speaking sessions when no admin setting exists (owner target: 100). A value
    /// below 1 is a misconfiguration and falls back to 100 (never to 1, which would throttle everyone); a value
    /// above 10000 is capped. To switch the gate off use <see cref="Enabled"/> or the admin kill switch.</summary>
    public int DefaultMaxConcurrent { get; set; } = 100;

    /// <summary>A waiter that has not polled for this long has left the line (clamped 15..600 s). The
    /// learner page polls every <see cref="PollAfterSeconds"/> while visible and every 20 s while hidden.</summary>
    public int WaiterHeartbeatSeconds { get; set; } = 90;

    /// <summary>Longest a place in the line is kept even for a learner who keeps polling (clamped 60..7200 s).
    /// Past it the learner is put at the back of the line on the next poll.</summary>
    public int MaxWaitSeconds { get; set; } = 1800;

    /// <summary>Largest line (clamped 1..100000). Beyond it a new waiter is refused with a retryable 503.</summary>
    public int MaxQueueLength { get; set; } = 1000;

    /// <summary>How long a freshly admitted session counts as holding a slot before its subject has
    /// started (clamped 30..900 s). It bridges the gap between the admission and the credit hold and
    /// state change that follow it, and frees a slot whose start failed without any compensation step.</summary>
    public int ClaimWindowSeconds { get; set; } = 120;

    /// <summary>Safety TTL of an admitted AI exam (clamped 10..240 minutes). Nominal length is 16 minutes.</summary>
    public int ExamAdmittedTtlMinutes { get; set; } = 45;

    /// <summary>Safety TTL of an admitted AI practice card (clamped 10..240 minutes). Nothing advances an
    /// abandoned practice card out of prep on the server, so this is what frees its place; a normal card
    /// (3 min prep, at most 10.5 min role-play) finishes well inside it.</summary>
    public int PracticeAdmittedTtlMinutes { get; set; } = 20;

    /// <summary>Average time a slot is held, used only to estimate a waiter's remaining wait (clamped 60..3600 s).</summary>
    public int AverageSessionSeconds { get; set; } = 900;

    /// <summary>Seconds the learner page waits between admission retries (clamped 2..30 s).</summary>
    public int PollAfterSeconds { get; set; } = 4;

    /// <summary>Days an ended admission row is kept before the sweeper purges it (clamped 1..90).</summary>
    public int RetentionDays { get; set; } = 7;

    public int DefaultMaxConcurrentResolved()
        => DefaultMaxConcurrent < MinConcurrent ? OwnerTargetConcurrent : Math.Min(DefaultMaxConcurrent, MaxConcurrent);
    public TimeSpan WaiterHeartbeat() => TimeSpan.FromSeconds(Math.Clamp(WaiterHeartbeatSeconds, 15, 600));
    public TimeSpan MaxWait() => TimeSpan.FromSeconds(Math.Clamp(MaxWaitSeconds, 60, 7200));
    public int MaxQueueLengthResolved() => Math.Clamp(MaxQueueLength, 1, 100000);
    public TimeSpan ClaimWindow() => TimeSpan.FromSeconds(Math.Clamp(ClaimWindowSeconds, 30, 900));
    public TimeSpan ExamAdmittedTtl() => TimeSpan.FromMinutes(Math.Clamp(ExamAdmittedTtlMinutes, 10, 240));
    public TimeSpan PracticeAdmittedTtl() => TimeSpan.FromMinutes(Math.Clamp(PracticeAdmittedTtlMinutes, 10, 240));
    public int AverageSessionSecondsResolved() => Math.Clamp(AverageSessionSeconds, 60, 3600);
    public int PollAfterSecondsResolved() => Math.Clamp(PollAfterSeconds, 2, 30);
    public TimeSpan Retention() => TimeSpan.FromDays(Math.Clamp(RetentionDays, 1, 90));

    /// <summary>Bounds of the admin-editable cap.</summary>
    public const int MinConcurrent = 1;
    public const int MaxConcurrent = 10000;

    /// <summary>The owner target (decision 5 Oct 2026): 100 concurrent live AI sessions.</summary>
    public const int OwnerTargetConcurrent = 100;
}
