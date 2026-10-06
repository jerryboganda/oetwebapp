using System.Text.Json;
using Fleet.Core.Domain;
using Fleet.Core.Policy;
using Fleet.Manager.Persistence;

namespace Fleet.Manager.Operations;

/// <summary>A host key offered during the scan. Public data; shown to the owner for out-of-band verification.</summary>
public sealed record HostKeyCandidate(string Algorithm, string PublicKeyBase64, string Fingerprint);

/// <summary>Mutable working data of an operation (<c>operations.data_json</c>). Never holds a secret.</summary>
public sealed class OperationData
{
    public List<HostKeyCandidate> HostKeyCandidates { get; set; } = new();

    public int HostKeyMismatches { get; set; }

    /// <summary>The agent image chosen at step S9 and reused by S10 (digest and the image id the pull reported).</summary>
    public string? ImageDigest { get; set; }

    public string? ImageId { get; set; }

    public DateTimeOffset? CanaryRequestedAt { get; set; }

    public DateTimeOffset? RestartRequestedAt { get; set; }

    /// <summary>
    /// The agent instance id the API reported before a token rotation restarted the agent. An agent generates a new id at every
    /// process start, so a changed id is the proof that a NEW process (the recreated container, started with the new token) is running.
    /// Kept across retries on purpose (never reset): a retried verification still has to see an id different from this one.
    /// </summary>
    public string? PreviousAgentInstanceId { get; set; }

    /// <summary>
    /// Set BEFORE the <c>uninstall</c> verb is sent. The verb closes the manager's own way in as its last act, so after a lost response
    /// or a crash a refused restricted login (<c>auth_failed</c>) means the uninstall already ran. Never reset by a retry.
    /// </summary>
    public bool UninstallRequested { get; set; }

    /// <summary>Fingerprint hint (not the token) of the token rendered by a rotation, recorded by its finalize step.</summary>
    public string? NewTokenFingerprint { get; set; }

    public static OperationData Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new OperationData();
        }

        try
        {
            return JsonSerializer.Deserialize<OperationData>(json, FleetJson.Options) ?? new OperationData();
        }
        catch (JsonException)
        {
            return new OperationData();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, FleetJson.Options);
}

/// <summary>The request is valid but not possible in the current state (HTTP 409).</summary>
public sealed class FleetOperationException : Exception
{
    public FleetOperationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>The addressed host or operation does not exist (HTTP 404).</summary>
public sealed class FleetNotFoundException : Exception
{
    public FleetNotFoundException(string what)
        : base(what + " was not found.")
    {
    }
}

public enum StepVerdict
{
    Done,
    Skipped,
    Failed,
}

public sealed record StepOutcome(StepVerdict Verdict, string Summary, string? FailureReason = null, string? FailureDetail = null)
{
    public static StepOutcome Done(string summary) => new(StepVerdict.Done, summary);

    public static StepOutcome Skipped(string summary) => new(StepVerdict.Skipped, summary);

    public static StepOutcome Fail(string reason, string? detail, string summary) =>
        new(StepVerdict.Failed, summary, FailureReasons.Normalize(reason), detail);
}

/// <summary>What a single runner tick achieved.</summary>
public enum Advance
{
    /// <summary>Progress was made; run another tick.</summary>
    Continue,

    /// <summary>Nothing more can happen until the owner (or a sync) acts.</summary>
    Blocked,

    /// <summary>The operation reached a terminal state.</summary>
    Done,
}

public sealed record NewOperation(
    OperationKind Kind,
    string? HostId,
    string InitialState,
    IReadOnlyList<string> StepNames,
    string ParamsJson,
    string Actor,
    string? RequestId);

/// <summary>Wakes the background worker when something it can act on appears (a new operation, a retry, a sync).</summary>
public sealed class OperationSignal
{
    private readonly SemaphoreSlim _semaphore = new(0, int.MaxValue);

    public void Kick() => _semaphore.Release();

    /// <summary>Returns when kicked or after <paramref name="timeout"/>.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(timeout, cancellationToken);
        while (_semaphore.CurrentCount > 0 && await _semaphore.WaitAsync(0, cancellationToken))
        {
            // Coalesce a burst of kicks into one wake-up.
        }
    }
}

// ---- Read models. They are what the JSON API and the UI see: no secrets, ever. ----

public sealed record HostView(
    string Id,
    string NodeRef,
    string DisplayName,
    string Address,
    int SshPort,
    string Lifecycle,
    string? Region,
    string? Provider,
    string? Os,
    string? Arch,
    int? CpuCores,
    int? MemMib,
    int? DiskGib,
    string? ApiNodeId,
    string? AgentDigest,
    int DesiredRevision,
    int AppliedRevision,
    DateTimeOffset? LastStatusAt,
    string? HostKeyAlgorithm,
    string? HostKeyFingerprint,
    DateTimeOffset? HostKeyPinnedAt,
    string? Alert,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static HostView From(HostEntity h) => new(
        h.Id, h.NodeRef, h.DisplayName, h.Address, h.SshPort, h.Lifecycle, h.Region, h.Provider, h.Os, h.Arch,
        h.CpuCores, h.MemMib, h.DiskGib, h.ApiNodeId, h.AgentDigest, h.DesiredRevision, h.AppliedRevision,
        h.LastStatusAt, h.HostKeyAlgo, h.HostKeySha256, h.HostKeyPinnedAt, h.Alert, h.CreatedAt, h.UpdatedAt);
}

public sealed record OperationStepView(
    int Seq,
    string Name,
    string State,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int? ExitCode,
    string? Summary);

public sealed record HostKeyCandidateView(string Algorithm, string Fingerprint);

public sealed record OperationView(
    string Id,
    string Kind,
    string? HostId,
    string State,
    string? ResumeState,
    string? CurrentStep,
    int StepAttempt,
    string? FailureReason,
    string? FailureDetail,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? FinishedAt,
    bool CancelRequested,
    bool AwaitingOwnerCredential,
    IReadOnlyList<HostKeyCandidateView> HostKeyCandidates,
    IReadOnlyList<OperationStepView> Steps)
{
    public static OperationView From(OperationEntity op, IReadOnlyList<OperationStepEntity> steps)
    {
        var data = OperationData.Parse(op.DataJson);
        var awaiting = op.State == nameof(EnrollmentState.HostKeyConfirmed)
            || op.State == nameof(GenericOperationState.AwaitingOwner)
            || (op.State == nameof(EnrollmentState.Failed)
                && (op.FailureReason == FailureReasons.OwnerCredentialExpired || op.FailureReason == FailureReasons.AuthFailed));
        return new OperationView(
            op.Id,
            op.Kind,
            op.HostId,
            op.State,
            op.ResumeState,
            op.CurrentStep,
            op.StepAttempt,
            op.FailureReason,
            op.FailureDetail,
            op.StartedAt,
            op.UpdatedAt,
            op.FinishedAt,
            op.CancelRequested,
            awaiting,
            data.HostKeyCandidates.Select(c => new HostKeyCandidateView(c.Algorithm, c.Fingerprint)).ToList(),
            steps.OrderBy(s => s.Seq)
                .Select(s => new OperationStepView(s.Seq, s.Name, s.State, s.StartedAt, s.FinishedAt, s.ExitCode, s.Summary))
                .ToList());
    }
}
