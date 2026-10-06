using Fleet.Core.Crypto;
using Fleet.Core.Domain;
using Fleet.Core.Ssh;

namespace Fleet.Manager.Provisioning;

/// <summary>Where and how to reach a helper. <see cref="KnownHostsLine"/> is the PINNED key (null only while scanning).</summary>
public sealed record HostTarget(string Address, int Port, string? KnownHostsLine);

public sealed record HostKeyScanResult(bool Success, IReadOnlyList<ScannedHostKey> Keys, string? Detail);

/// <summary>
/// One host-touching enrollment step. <see cref="OwnerKey"/>/<see cref="OwnerUser"/> are the temporary
/// owner credential (steps S1..S7 only); <see cref="ManagerKey"/> is the manager's own restricted key.
/// <see cref="Data"/> is plain DATA handed to Ansible as JSON (<c>-e @file</c>), never shell-interpolated.
/// </summary>
public sealed record ProvisionRequest(
    EnrollStep Step,
    HostTarget Target,
    string? OwnerUser,
    SecretBuffer? OwnerKey,
    SecretBuffer? ManagerKey,
    string? ManagerPublicKey,
    IReadOnlyDictionary<string, object?> Data);

public sealed record ProvisionResult(
    bool Success,
    bool Satisfied,
    string? FailureReason,
    string? Detail,
    string Summary,
    IReadOnlyDictionary<string, object?> Facts)
{
    private static readonly IReadOnlyDictionary<string, object?> NoFacts = new Dictionary<string, object?>();

    public static ProvisionResult Done(string summary, IReadOnlyDictionary<string, object?>? facts = null) =>
        new(true, false, null, null, summary, facts ?? NoFacts);

    /// <summary>The success predicate already holds; the step can be skipped.</summary>
    public static ProvisionResult AlreadySatisfied(string summary) =>
        new(true, true, null, null, summary, NoFacts);

    public static ProvisionResult NotSatisfied(string summary = "predicate not satisfied") =>
        new(true, false, null, null, summary, NoFacts);

    public static ProvisionResult Fail(string reason, string? detail, string summary) =>
        new(false, false, FailureReasons.Normalize(reason), detail, summary, NoFacts);
}

/// <summary>A restricted <c>oet-fleet-ctl</c> call over SSH (OET-RWP/1 section 7.4).</summary>
public sealed record CtlRequest(
    HostTarget Target,
    SecretBuffer ManagerKey,
    string Verb,
    IReadOnlyList<string> Args,
    string? StdinText);

/// <summary>
/// <see cref="FailureReason"/> is set only for transport-level failures (host key changed, auth
/// failed, unreachable). A ctl-level failure has <see cref="Success"/> false, a null reason and the
/// helper's JSON in <see cref="Stdout"/>; the calling step maps it to the right failure reason.
/// </summary>
public sealed record CtlResult(bool Success, string? FailureReason, string? Detail, string Stdout, int ExitCode);

/// <summary>
/// The seam between the manager's state machines and the real world (Ansible over OpenSSH on the
/// helper). Every method is idempotent and safe to repeat after a crash. Tests use a fake that models
/// a helper host.
/// </summary>
public interface IProvisioner
{
    /// <summary>Fetches host key fingerprints for DISPLAY only (they are not trusted until the owner confirms them out of band).</summary>
    Task<HostKeyScanResult> ScanHostKeyAsync(string address, int port, CancellationToken cancellationToken);

    /// <summary>The step's success predicate (read-only). Satisfied means the step can be skipped.</summary>
    Task<ProvisionResult> CheckAsync(ProvisionRequest request, CancellationToken cancellationToken);

    /// <summary>Performs the step. Safe to run again.</summary>
    Task<ProvisionResult> ApplyAsync(ProvisionRequest request, CancellationToken cancellationToken);

    /// <summary>Runs one allow-listed <c>oet-fleet-ctl</c> verb as the manager's restricted fleet user.</summary>
    Task<CtlResult> RunCtlAsync(CtlRequest request, CancellationToken cancellationToken);
}
