namespace Fleet.Agent;

/// <summary>
/// Thread-safe holder of the agent's facts, the desired state delivered by the API and the derived state decision.
/// Every mutator recomputes the decision through <see cref="AgentStateMachine"/> and raises <see cref="Changed"/>
/// when the state or the claim permission moved.
/// </summary>
internal sealed class AgentStatus
{
    private readonly object _gate = new();
    private readonly ILogger _log;
    private readonly string _imageDigest;
    private readonly string _version;
    private AgentFacts _facts = new();
    private DesiredState _desired = new();
    private long _appliedRevision;
    private AgentDecision _decision = new(AgentState.Starting, false, "starting");
    private string? _nodeStatus;

    public AgentStatus(ILogger log, string imageDigest, string version)
    {
        _log = log;
        _imageDigest = imageDigest;
        _version = version;
    }

    /// <summary>Raised outside the lock whenever the derived decision changes.</summary>
    public event Action<AgentDecision>? Changed;

    /// <summary>Raised when the API advertises a desired revision the agent has not applied (X-Remote-Desired-Revision).</summary>
    public event Action? DesiredRevisionStale;

    public AgentDecision Decision
    {
        get { lock (_gate) return _decision; }
    }

    public AgentFacts Facts
    {
        get { lock (_gate) return _facts; }
    }

    public DesiredState Desired
    {
        get { lock (_gate) return _desired; }
    }

    public long AppliedRevision
    {
        get { lock (_gate) return _appliedRevision; }
    }

    public string? NodeStatus
    {
        get { lock (_gate) return _nodeStatus; }
    }

    public bool MayClaim => Decision.MayClaim;

    /// <summary>Job heartbeats stop while the API cannot be spoken to (rollback skew, protocol mismatch, auth failure).</summary>
    public bool JobHeartbeatsAllowed
    {
        get
        {
            var state = Decision.State;
            return state is not (AgentState.ProtocolMismatch or AgentState.ApiUnsupported or AgentState.AuthFailed or AgentState.Superseded);
        }
    }

    public int ConsecutiveHeartbeatFailures
    {
        get { lock (_gate) return _facts.ConsecutiveHeartbeatFailures; }
    }

    // ---- mutators ------------------------------------------------------------------------------------------

    public void OnNodeHeartbeatOk(NodeHeartbeatResponse response)
    {
        Mutate(f =>
        {
            f = f with
            {
                HeartbeatOk = true,
                ConsecutiveHeartbeatFailures = 0,
                AuthFailed = false,
                ApiUnsupported = false,
                ProtocolMismatch = false,
                NodeForbidden = null,
            };

            var desired = response.Desired;
            if (desired is not null)
            {
                _desired = Normalise(desired);
                _appliedRevision = desired.Revision;
                f = f with { DesiredDrain = desired.Drain, DesiredPaused = desired.Paused };
                var image = desired.AgentImage;
                var unapproved = image is not null && image.ApprovedDigests.Count > 0
                    && !image.ApprovedDigests.Contains(_imageDigest, StringComparer.Ordinal);
                var below = image is not null && SemVer.IsBelow(_version, image.MinVersion);
                f = f with { UnapprovedDigest = unapproved, BelowMinVersion = below };
            }

            var status = response.Node?.Status;
            if (status is not null)
            {
                _nodeStatus = status;
                f = f with
                {
                    ServerDraining = status is "Draining" or "Disabled",
                    NodeForbidden = status switch
                    {
                        "Quarantined" => "node_quarantined",
                        "Pending" => "node_not_active",
                        _ => null,
                    },
                };
            }

            return f;
        });
    }

    public void OnNodeHeartbeatFailure() =>
        Mutate(f => f with { ConsecutiveHeartbeatFailures = Math.Min(f.ConsecutiveHeartbeatFailures + 1, 1000) });

    public void OnAuthFailed() => Mutate(f => f with { AuthFailed = true });

    public void OnApiUnsupported() => Mutate(f => f with { ApiUnsupported = true });

    public void OnProtocolMismatch() => Mutate(f => f with { ProtocolMismatch = true });

    public void OnSuperseded() => Mutate(f => f with { Superseded = true });

    public void OnNodeForbidden(string code) => Mutate(f => f with { NodeForbidden = code });

    public void OnServerDraining() => Mutate(f => f with { ServerDraining = true });

    public void SetSelfCheck(bool ok) => Mutate(f => f with { SelfCheckDone = true, SelfCheckOk = ok });

    public void SetPressureReduced(bool reduced) => Mutate(f => f with { PressureReduced = reduced });

    public void BeginStopping() => Mutate(f => f with { Stopping = true });

    /// <summary>Called with the X-Remote-Desired-Revision header or a body field; a mismatch triggers an immediate heartbeat.</summary>
    public void NoteDesiredRevision(long revision)
    {
        bool stale;
        lock (_gate) stale = revision != _appliedRevision;
        if (stale) DesiredRevisionStale?.Invoke();
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    private void Mutate(Func<AgentFacts, AgentFacts> change)
    {
        AgentDecision before;
        AgentDecision after;
        lock (_gate)
        {
            before = _decision;
            _facts = change(_facts);
            after = AgentStateMachine.Derive(_facts);
            _decision = after;
        }

        if (before != after)
        {
            _log.LogInformation("agent state {From} -> {To} claim={MayClaim} reason={Reason}",
                before.State, after.State, after.MayClaim, after.Reason ?? "none");
            Changed?.Invoke(after);
        }
    }

    private static DesiredState Normalise(DesiredState desired)
    {
        desired.MaxConcurrency = Math.Clamp(desired.MaxConcurrency, 0, 8);
        desired.Pressure ??= new PressureSettings();
        desired.PollSeconds ??= new PollSettings();
        desired.AllowedKinds ??= [];
        desired.PerKind ??= new Dictionary<string, int>();
        return desired;
    }
}
