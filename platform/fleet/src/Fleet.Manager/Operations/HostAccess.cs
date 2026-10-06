using System.Collections.Concurrent;
using Fleet.Core.Domain;
using Fleet.Core.Ssh;
using Fleet.Manager.Api;
using Fleet.Manager.Infrastructure;
using Fleet.Manager.Persistence;
using Fleet.Manager.Provisioning;
using Fleet.Manager.Vault;

namespace Fleet.Manager.Operations;

/// <summary>
/// Everything needed to talk to a pinned helper as the manager's restricted fleet user: it builds the
/// <see cref="HostTarget"/> from the owner-verified pin (never from scanner output) and opens the
/// manager key from the vault for exactly one ctl call.
/// </summary>
public sealed class HostAccess
{
    private readonly CredentialStore _credentials;
    private readonly IProvisioner _provisioner;

    public HostAccess(CredentialStore credentials, IProvisioner provisioner)
    {
        _credentials = credentials;
        _provisioner = provisioner;
    }

    /// <summary>The pinned target. Throws when the host key has not been pinned yet (the pin is mandatory).</summary>
    public HostTarget TargetFor(HostEntity host)
    {
        if (host.HostKeyAlgo is null || host.HostKeyPublic is null)
        {
            throw new InvalidOperationException("The host key is not pinned.");
        }

        return new HostTarget(host.Address, host.SshPort, HostKeys.KnownHostsLine(host.Address, host.SshPort, host.HostKeyAlgo, host.HostKeyPublic));
    }

    public async Task<CtlResult> CtlAsync(
        HostEntity host,
        string verb,
        IReadOnlyList<string> args,
        string? stdin,
        CancellationToken cancellationToken)
    {
        using var key = await _credentials.OpenAsync(host.Id, CredentialPurposes.ManagerSsh, cancellationToken);
        if (key is null)
        {
            return new CtlResult(false, FailureReasons.AuthFailed, "the manager key is not available for this host", string.Empty, -1);
        }

        return await _provisioner.RunCtlAsync(new CtlRequest(TargetFor(host), key, verb, args, stdin), cancellationToken);
    }
}

/// <summary>
/// Host-key trust events (OET-RWP/1 section 8.6). A changed key is a HARD failure: the host is
/// disabled (in the manager and, best effort, in the API), a persistent alert is raised and only an
/// explicit owner re-pin with a fresh out-of-band fingerprint check clears it.
/// </summary>
public sealed class HostSecurityService
{
    private static readonly TimeSpan RepinWindow = TimeSpan.FromMinutes(10);

    private readonly HostStore _hosts;
    private readonly IFleetApi _api;
    private readonly IProvisioner _provisioner;
    private readonly IAuditService _audit;
    private readonly IEventBus _events;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, IReadOnlyList<ScannedHostKey> Keys)> _repin = new();

    public HostSecurityService(
        HostStore hosts,
        IFleetApi api,
        IProvisioner provisioner,
        IAuditService audit,
        IEventBus events,
        TimeProvider time)
    {
        _hosts = hosts;
        _api = api;
        _provisioner = provisioner;
        _audit = audit;
        _events = events;
        _time = time;
    }

    public async Task OnHostKeyChangedAsync(string hostId, string actor, CancellationToken cancellationToken)
    {
        var host = await _hosts.GetAsync(hostId, cancellationToken);
        if (host is null)
        {
            return;
        }

        await _hosts.UpdateAsync(
            hostId,
            h =>
            {
                h.Lifecycle = nameof(HostLifecycle.Disabled);
                h.Alert = FailureReasons.HostKeyChanged;
            },
            cancellationToken);

        if (host.ApiNodeId is not null)
        {
            try
            {
                await _api.DisableAsync(host.ApiNodeId, cancellationToken);
            }
            catch (FleetApiException)
            {
                // Best effort: the node may already be disabled or unreachable; the alert and the manager-side Disabled stand regardless.
            }
        }

        await _audit.AppendAsync(
            actor,
            "host.host_key_changed",
            host.NodeRef,
            new Dictionary<string, object?> { ["address"] = host.Address, ["pinned"] = host.HostKeySha256 },
            cancellationToken);
        _events.Publish("alert.raised", new { hostId, nodeRef = host.NodeRef, alert = FailureReasons.HostKeyChanged });
    }

    /// <summary>Step 1 of a re-pin: fetch the fingerprints the host offers now (display only).</summary>
    public async Task<IReadOnlyList<HostKeyCandidateView>> BeginRepinAsync(string hostId, string actor, CancellationToken cancellationToken)
    {
        var host = await _hosts.GetAsync(hostId, cancellationToken)
            ?? throw new Fleet.Core.Validation.FleetValidationException(new Fleet.Core.Validation.ValidationIssue("hostId", "host_not_found", "No such host."));
        var scan = await _provisioner.ScanHostKeyAsync(host.Address, host.SshPort, cancellationToken);
        if (!scan.Success)
        {
            throw new Fleet.Core.Validation.FleetValidationException(new Fleet.Core.Validation.ValidationIssue("address", FailureReasons.HostKeyUnreachable, "the host did not offer a host key."));
        }

        _repin[hostId] = (_time.GetUtcNow(), scan.Keys);
        await _audit.AppendAsync(actor, "host.repin_started", host.NodeRef, null, cancellationToken);
        return scan.Keys.Select(k => new HostKeyCandidateView(k.Algorithm, k.Fingerprint)).ToList();
    }

    /// <summary>Step 2: the owner compared the fingerprint with the provider console and typed its first 8 characters.</summary>
    public async Task ConfirmRepinAsync(string hostId, string typedPrefix, string actor, CancellationToken cancellationToken)
    {
        var host = await _hosts.GetAsync(hostId, cancellationToken)
            ?? throw new Fleet.Core.Validation.FleetValidationException(new Fleet.Core.Validation.ValidationIssue("hostId", "host_not_found", "No such host."));
        if (!_repin.TryGetValue(hostId, out var pending) || _time.GetUtcNow() - pending.At > RepinWindow)
        {
            throw new Fleet.Core.Validation.FleetValidationException(new Fleet.Core.Validation.ValidationIssue("hostId", "repin_not_started", "Start the re-pin again; the scan is older than 10 minutes."));
        }

        var candidate = HostKeys.Preferred(pending.Keys)
            ?? throw new Fleet.Core.Validation.FleetValidationException(new Fleet.Core.Validation.ValidationIssue("hostId", "repin_not_started", "No key to pin."));
        if (!HostKeys.PrefixMatches(candidate.Fingerprint, typedPrefix))
        {
            await _audit.AppendAsync(actor, "host.repin_mismatch", host.NodeRef, null, cancellationToken);
            throw new Fleet.Core.Validation.FleetValidationException(new Fleet.Core.Validation.ValidationIssue("fingerprint", FailureReasons.HostKeyMismatch, "The typed characters do not match the fingerprint."));
        }

        var now = _time.GetUtcNow();
        await _hosts.UpdateAsync(
            hostId,
            h =>
            {
                h.HostKeyAlgo = candidate.Algorithm;
                h.HostKeySha256 = candidate.Fingerprint;
                h.HostKeyPublic = candidate.PublicKeyBase64;
                h.HostKeyPinnedAt = now;
                h.Alert = null;
            },
            cancellationToken);
        _repin.TryRemove(hostId, out _);
        await _audit.AppendAsync(
            actor,
            "host.repinned",
            host.NodeRef,
            new Dictionary<string, object?> { ["fingerprint"] = candidate.Fingerprint },
            cancellationToken);
        _events.Publish("alert.cleared", new { hostId, nodeRef = host.NodeRef });
    }
}
