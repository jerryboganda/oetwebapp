namespace Fleet.Agent;

internal enum AgentState
{
    Starting,
    Ready,
    Degraded,
    Draining,
    Paused,
    ProtocolMismatch,
    ApiUnsupported,
    AuthFailed,
    Superseded,
    Stopping,
}

/// <summary>Every input of the agent state machine (protocol 5.2). Pure data so the machine is testable without threads.</summary>
internal readonly record struct AgentFacts(
    bool Stopping = false,
    bool Superseded = false,
    bool AuthFailed = false,
    bool ApiUnsupported = false,
    bool ProtocolMismatch = false,
    bool HeartbeatOk = false,
    bool SelfCheckDone = false,
    bool SelfCheckOk = false,
    bool DesiredDrain = false,
    bool DesiredPaused = false,
    bool ServerDraining = false,
    string? NodeForbidden = null,
    bool UnapprovedDigest = false,
    bool BelowMinVersion = false,
    bool PressureReduced = false,
    int ConsecutiveHeartbeatFailures = 0);

internal readonly record struct AgentDecision(AgentState State, bool MayClaim, string? Reason);

internal static class AgentStateMachine
{
    /// <summary>After this many consecutive node-heartbeat failures the agent stops claiming (section 4.7).</summary>
    public const int HeartbeatFailureLimit = 3;

    public static AgentDecision Derive(AgentFacts f)
    {
        if (f.Stopping) return new(AgentState.Stopping, false, "stopping");
        if (f.Superseded) return new(AgentState.Superseded, false, "instance_superseded");
        if (f.AuthFailed) return new(AgentState.AuthFailed, false, "unauthorized");
        if (f.ApiUnsupported) return new(AgentState.ApiUnsupported, false, "api_unsupported");
        if (f.ProtocolMismatch) return new(AgentState.ProtocolMismatch, false, "protocol_mismatch");
        if (!f.HeartbeatOk || !f.SelfCheckDone) return new(AgentState.Starting, false, "starting");
        if (f.NodeForbidden is not null) return new(AgentState.Draining, false, f.NodeForbidden);
        if (f.DesiredDrain || f.ServerDraining) return new(AgentState.Draining, false, "draining");
        if (f.DesiredPaused) return new(AgentState.Paused, false, "paused");
        if (f.UnapprovedDigest) return new(AgentState.Degraded, false, "unapproved_digest");
        if (f.BelowMinVersion) return new(AgentState.Degraded, false, "below_min_version");
        if (!f.SelfCheckOk) return new(AgentState.Degraded, false, "self_check_failed");
        if (f.ConsecutiveHeartbeatFailures >= HeartbeatFailureLimit) return new(AgentState.Ready, false, "heartbeat_failures");
        if (f.PressureReduced) return new(AgentState.Degraded, true, "pressure");
        return new(AgentState.Ready, true, null);
    }

    /// <summary>The value of the heartbeat request's state member (section 4.7.1).</summary>
    public static string WireState(AgentState state) => state switch
    {
        AgentState.Starting => "starting",
        AgentState.Ready => "ready",
        AgentState.Degraded => "degraded",
        AgentState.Draining => "draining",
        AgentState.Paused => "paused",
        AgentState.ProtocolMismatch => "protocol_mismatch",
        AgentState.Stopping => "stopping",
        AgentState.Superseded => "stopping",
        _ => "degraded",
    };
}
