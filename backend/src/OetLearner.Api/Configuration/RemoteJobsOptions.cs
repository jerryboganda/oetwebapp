namespace OetLearner.Api.Configuration;

/// <summary>
/// <c>RemoteJobs</c> configuration section (environment <c>RemoteJobs__&lt;Name&gt;</c>) for the
/// remote-worker boundary (OET-RWP/1 section 9.3). Everything is OFF by default: the master
/// switch and every per-kind switch are <c>FeatureFlags</c> rows that must exist and be enabled.
/// Secrets are never configuration (node and fleet tokens live hashed in the database).
/// </summary>
public sealed class RemoteJobsOptions
{
    public const string SectionName = "RemoteJobs";

    /// <summary>Protocol number the API speaks (N).</summary>
    public int CurrentProtocol { get; set; } = 1;

    /// <summary>Lowest protocol number the API still accepts (N-1, floor 1).</summary>
    public int MinProtocol { get; set; } = 1;

    public int LeaseSeconds { get; set; } = 120;
    public int HeartbeatEverySeconds { get; set; } = 20;
    public int NodeHeartbeatSeconds { get; set; } = 15;
    public int NodeStaleAfterSeconds { get; set; } = 45;
    public int NodeOfflineAfterSeconds { get; set; } = 600;
    public int MaxAttempts { get; set; } = 3;
    public int ReleaseLimit { get; set; } = 5;
    public int BackoffBaseSeconds { get; set; } = 5;
    public int BackoffMaxSeconds { get; set; } = 300;
    public int BackoffJitterPercent { get; set; } = 20;
    public int ReaperIntervalSeconds { get; set; } = 15;
    public int ReaperBatch { get; set; } = 100;
    public int FallbackAfterMinutes { get; set; } = 10;
    public int FallbackHardAfterMinutes { get; set; } = 60;
    public int JobRetentionDays { get; set; } = 30;
    public int DeferredResultRetentionDays { get; set; } = 7;

    /// <summary>
    /// Hours a prepared Speaking audio join (<c>media.speaking-join</c>, learner audio) may wait for its grade before the reaper deletes it.
    /// The grade deletes it the moment it uses it, and retention or an erasure of the clips deletes it too, so this is only the backstop.
    /// </summary>
    public int SpeakingJoinOutputTtlHours { get; set; } = 24;

    public int IntegrityStrikeLimit { get; set; } = 3;
    public int StrikeWindowMinutes { get; set; } = 60;

    /// <summary>Placement fair-share gate (enable at two or more active nodes).</summary>
    public bool FairShareGate { get; set; }

    public int ClaimRatePerMinute { get; set; } = 60;
    public int TokenTtlDays { get; set; } = 30;
    public int FleetTokenTtlDays { get; set; } = 90;
    public int TokenRotationGraceSeconds { get; set; } = 3600;

    /// <summary>Optional CIDR allow-list for the fleet service plane (empty = no restriction).</summary>
    public string[] FleetAllowedCidrs { get; set; } = [];

    /// <summary>
    /// Fraction (0..1) of applied jobs re-checked in-process by the ai-worker after cutover
    /// (recommended 0.05). 0 disables sampling.
    /// </summary>
    public double VerifySampleRate { get; set; }

    /// <summary>Per-kind overrides, e.g. <c>RemoteJobs:Kinds:media.audio-extract:EngineVersion</c>.</summary>
    public Dictionary<string, RemoteKindOptions> Kinds { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Pinned engine string for a kind, or null when none is configured.</summary>
    public string? EngineVersionOverride(string kind)
        => Kinds.TryGetValue(kind, out var options) && !string.IsNullOrWhiteSpace(options.EngineVersion)
            ? options.EngineVersion.Trim()
            : null;

    /// <summary>Values clamped to ranges the state machine can honour (a typo must not brick leasing).</summary>
    public RemoteJobsOptions Normalized()
    {
        var current = Math.Max(1, CurrentProtocol);
        return new RemoteJobsOptions
        {
            CurrentProtocol = current,
            MinProtocol = Math.Clamp(MinProtocol, 1, current),
            LeaseSeconds = Math.Clamp(LeaseSeconds, 30, 900),
            HeartbeatEverySeconds = Math.Clamp(HeartbeatEverySeconds, 5, 300),
            NodeHeartbeatSeconds = Math.Clamp(NodeHeartbeatSeconds, 5, 300),
            NodeStaleAfterSeconds = Math.Clamp(NodeStaleAfterSeconds, 15, 3600),
            NodeOfflineAfterSeconds = Math.Clamp(NodeOfflineAfterSeconds, 60, 86400),
            MaxAttempts = Math.Clamp(MaxAttempts, 1, 10),
            ReleaseLimit = Math.Clamp(ReleaseLimit, 0, 50),
            BackoffBaseSeconds = Math.Clamp(BackoffBaseSeconds, 1, 600),
            BackoffMaxSeconds = Math.Clamp(BackoffMaxSeconds, 1, 3600),
            BackoffJitterPercent = Math.Clamp(BackoffJitterPercent, 0, 100),
            ReaperIntervalSeconds = Math.Clamp(ReaperIntervalSeconds, 5, 600),
            ReaperBatch = Math.Clamp(ReaperBatch, 1, 1000),
            FallbackAfterMinutes = Math.Clamp(FallbackAfterMinutes, 1, 1440),
            FallbackHardAfterMinutes = Math.Clamp(FallbackHardAfterMinutes, 1, 10080),
            JobRetentionDays = Math.Clamp(JobRetentionDays, 1, 3650),
            DeferredResultRetentionDays = Math.Clamp(DeferredResultRetentionDays, 1, 365),
            SpeakingJoinOutputTtlHours = Math.Clamp(SpeakingJoinOutputTtlHours, 1, 168),
            IntegrityStrikeLimit = Math.Clamp(IntegrityStrikeLimit, 1, 100),
            StrikeWindowMinutes = Math.Clamp(StrikeWindowMinutes, 1, 1440),
            FairShareGate = FairShareGate,
            ClaimRatePerMinute = Math.Clamp(ClaimRatePerMinute, 1, 6000),
            TokenTtlDays = Math.Clamp(TokenTtlDays, 1, 365),
            FleetTokenTtlDays = Math.Clamp(FleetTokenTtlDays, 1, 365),
            TokenRotationGraceSeconds = Math.Clamp(TokenRotationGraceSeconds, 0, 86400),
            FleetAllowedCidrs = FleetAllowedCidrs ?? [],
            VerifySampleRate = double.IsNaN(VerifySampleRate) ? 0d : Math.Clamp(VerifySampleRate, 0d, 1d),
            Kinds = Kinds ?? new Dictionary<string, RemoteKindOptions>(StringComparer.Ordinal),
        };
    }
}

public sealed class RemoteKindOptions
{
    /// <summary>Pinned engine version offered at claim (required for media kinds).</summary>
    public string? EngineVersion { get; set; }
}
