using Fleet.Core.Policy;
using Fleet.Manager.Api;
using Fleet.Manager.Monitoring;
using Fleet.Manager.Operations;

namespace Fleet.Manager.Dashboard;

// Read models of the owner console. They hold nothing secret (a credential appears only as the fingerprint hint and its timestamps)
// and every string that came from a helper, the API or a child process has already been through Fmt.Untrusted or is rendered through it.

/// <summary>The primary VPS as the manager can see it: the host-wide pressure its placement rules use, and the console's own footprint.</summary>
public sealed record PrimaryView(
    bool PressureReadable,
    double? CpuSomeAvg10,
    double? MemorySomeAvg10,
    double? MemoryWorkingSetPct,
    bool HasHeadroom,
    long ConsoleWorkingSetMiB,
    TimeSpan ConsoleUptime);

/// <summary>One operation in a list: no steps, no secrets, a failure detail that is already display-safe.</summary>
public sealed record OpSummary(
    string Id,
    string Kind,
    string? HostId,
    string HostName,
    string State,
    string? ResumeState,
    string? CurrentStep,
    string? FailureReason,
    string? FailureDetail,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? FinishedAt,
    string Actor,
    string NextAction,
    bool NeedsOwner,
    bool IsOpen);

/// <summary>A helper row: the manager's host record, what the OET API says about the node, the last SSH status report and the derived health.</summary>
public sealed record HelperLine(
    HostView Host,
    ApiNode? Node,
    HostStatusReport? Report,
    string Presence,
    double? Utilisation,
    TimeSpan? HeartbeatAge,
    OpSummary? Operation);

public sealed record FleetTotals(
    int Helpers,
    int Active,
    int Draining,
    int Disabled,
    int Enrolling,
    int Failed,
    int Offline,
    int SlotsTotal,
    int SlotsUsed,
    double? Utilisation,
    int CpuBudgetFreeMilli,
    int MemBudgetFreeMiB);

/// <summary>Something the owner should look at. <see cref="Severity"/> is <c>bad</c>, <c>warn</c> or <c>info</c>.</summary>
public sealed record AttentionItem(string Severity, string Text, string? Href);

public sealed record FleetOverview(
    DateTimeOffset Now,
    PrimaryView Primary,
    IReadOnlyList<HelperLine> Helpers,
    FleetTotals Totals,
    IReadOnlyList<AttentionItem> Attention,
    IReadOnlyList<OpSummary> InFlight,
    bool ApiReachable,
    string? ApiError,
    DateTimeOffset? LastPollAt,
    bool IntegrityOk,
    bool PullTokenHeld);

public sealed record HostActions(
    bool CanDrain,
    bool CanDisable,
    bool CanResume,
    bool CanRotateToken,
    bool CanRepair,
    bool CanRemove,
    bool NeedsRepin);

public sealed record PolicyView(
    NodePolicy Effective,
    bool IsOverride,
    NodePolicy Global,
    int DesiredRevision,
    int AppliedRevision);

/// <summary>A stored credential as the UI may show it: its purpose, a fingerprint hint and timestamps. Never a value.</summary>
public sealed record CredentialLine(
    string HostId,
    string NodeRef,
    string HostName,
    string Purpose,
    string Hint,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RotatedAt);

public sealed record AuditLine(long Id, DateTimeOffset At, string Actor, string Action, string? Target, string Details);

public sealed record HostPageView(
    DateTimeOffset Now,
    HelperLine Line,
    HostActions Actions,
    PolicyView Policy,
    IReadOnlyList<string> RegistryKinds,
    IReadOnlyList<HealthTransition> Health,
    IReadOnlyList<OpSummary> Operations,
    IReadOnlyList<AuditLine> Audit,
    IReadOnlyList<CredentialLine> Credentials,
    OpSummary? EnrollOperation);

public sealed record OperationPageView(
    DateTimeOffset Now,
    OperationView Operation,
    HostView? Host,
    string HostName,
    OwnerKeyAcceptance KeyAcceptance,
    CredentialLine? SavedKey,
    bool CanRetry,
    bool CanCancel,
    bool PullTokenHeld);

public sealed record OperationsPageView(
    DateTimeOffset Now,
    IReadOnlyList<OpSummary> Operations,
    IReadOnlyList<string> Kinds,
    string? KindFilter,
    string? StateFilter,
    int OpenCount);

public sealed record ReleaseLine(ReleaseView Release, bool IsCurrent, bool InWindow);

public sealed record ReleasesView(
    DateTimeOffset Now,
    IReadOnlyList<ReleaseLine> Releases,
    bool PullTokenHeld,
    int AwaitingSync,
    OpSummary? OpenRollout,
    int RolloutHosts);

public sealed record AuditPageView(
    IReadOnlyList<AuditLine> Lines,
    bool? ChainIntact,
    string? ChainProblem,
    int ChainChecked);

/// <summary>Job counts of one kind, from the OET API's <c>GET /stats</c>.</summary>
public sealed record KindQueue(
    string Kind,
    int Queued,
    int Leased,
    int Succeeded,
    int Failed,
    int Quarantined,
    int FallbackLocal,
    int Cancelled)
{
    /// <summary>Succeeded among jobs that ended for good (succeeded, failed, quarantined); null when none has ended.</summary>
    public double? CompletionRate
    {
        get
        {
            var ended = Succeeded + Failed + Quarantined;
            return ended == 0 ? null : (double)Succeeded / ended;
        }
    }
}

public sealed record QueueStats(
    IReadOnlyList<KindQueue> Kinds,
    long? OldestQueuedAgeSeconds,
    int LeasedCount,
    IReadOnlyDictionary<string, int> LeasedWeightByNode);

public sealed record KindWorkload(
    string Kind,
    string Project,
    KindQueue Queue,
    int AllowedNodes,
    IReadOnlyList<string> EligibleNodes,
    string Placement,
    string PlacementReason,
    IReadOnlyList<string> Rejections);

public sealed record NodeLoadLine(
    string HostId,
    string NodeRef,
    string Status,
    string Health,
    int Leases,
    int LeasedWeight,
    int SlotCap,
    string Kinds);

public sealed record WorkloadsView(
    DateTimeOffset Now,
    bool StatsAvailable,
    string? StatsError,
    long? OldestQueuedAgeSeconds,
    int LeasedCount,
    IReadOnlyList<KindWorkload> Kinds,
    IReadOnlyList<NodeLoadLine> Nodes,
    PrimaryView Primary);

public sealed record PolicyHostLine(
    string HostId,
    string NodeRef,
    string HostName,
    string Lifecycle,
    bool HasOverride,
    int DesiredRevision,
    int AppliedRevision,
    int MaxConcurrency,
    string Kinds);

public sealed record PoliciesView(
    DateTimeOffset Now,
    NodePolicy Global,
    bool GlobalIsDefault,
    IReadOnlyList<string> RegistryKinds,
    bool RegistryKnown,
    IReadOnlyList<PolicyHostLine> Hosts);

public sealed record TokenLine(
    string HostId,
    string NodeRef,
    string HostName,
    string? Hint,
    DateTimeOffset? RenderedAt,
    int? ActiveTokens,
    DateTimeOffset? NextExpiryAt,
    bool CanRotate);

public sealed record ServiceCredentialLine(string Name, bool Present, string Detail);

public sealed record CredentialsView(
    DateTimeOffset Now,
    IReadOnlyList<CredentialLine> OwnerKeys,
    IReadOnlyList<CredentialLine> ManagerKeys,
    IReadOnlyList<TokenLine> NodeTokens,
    IReadOnlyList<ServiceCredentialLine> Services,
    IReadOnlyList<OpSummary> AwaitingKey,
    string MasterKeyId,
    bool PreviousKeyLoaded,
    bool PullTokenHeld);

public sealed record OetIntegration(
    bool ApiReachable,
    string? ApiError,
    DateTimeOffset? LastPollAt,
    bool CredentialProvisioned,
    bool StatusFetched,
    string? StatusError,
    int? ProtocolCurrent,
    int? ProtocolMinimum,
    IReadOnlyList<string> Kinds,
    int NodesRegistered,
    int NodesActive,
    int NodesOnline,
    bool SyncEndpointEnabled,
    bool MetricsTokenPresent,
    string? CurrentRelease,
    int UnapprovedReleases);

public sealed record ProjectLine(string Name, string Role, string Status, string Detail);

public sealed record ProjectsView(
    DateTimeOffset Now,
    PrimaryView Primary,
    OetIntegration Oet,
    IReadOnlyList<ProjectLine> Projects);
