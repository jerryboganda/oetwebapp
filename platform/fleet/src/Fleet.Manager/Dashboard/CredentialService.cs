using Fleet.Manager.Infrastructure;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Fleet.Manager.Vault;

namespace Fleet.Manager.Dashboard;

/// <summary>
/// The two credential actions the console adds to the manager's own: erasing a stored owner key before its 60 minutes are up, and
/// discarding the in-memory registry pull token. Storing keys, rotating node tokens and enrolling are the existing services' jobs.
/// Nothing here ever reads a secret: it only deletes, and it audits that it did.
/// </summary>
public sealed class CredentialService
{
    private readonly HostStore _hosts;
    private readonly CredentialStore _credentials;
    private readonly RolloutTokenHolder _pullToken;
    private readonly IAuditService _audit;
    private readonly IEventBus _events;

    public CredentialService(
        HostStore hosts,
        CredentialStore credentials,
        RolloutTokenHolder pullToken,
        IAuditService audit,
        IEventBus events)
    {
        _hosts = hosts;
        _credentials = credentials;
        _pullToken = pullToken;
        _audit = audit;
        _events = events;
    }

    /// <summary>
    /// Crypto-erases the temporary owner key of a host (row deleted with <c>secure_delete</c>, WAL checkpointed). An enrollment that was about to
    /// use it fails with <c>owner_credential_expired</c> and asks for it again. Returns false when there was none.
    /// </summary>
    public async Task<bool> RevokeOwnerKeyAsync(string hostId, string actor, CancellationToken cancellationToken)
    {
        var host = await _hosts.GetAsync(hostId, cancellationToken) ?? throw new FleetNotFoundException("The host");
        var removed = await _credentials.DestroyAsync(host.Id, CredentialPurposes.OwnerBootstrap, cancellationToken);
        await _audit.AppendAsync(
            actor,
            "credential.owner_key_revoked",
            host.NodeRef,
            new Dictionary<string, object?> { ["removed"] = removed },
            cancellationToken);
        _events.Publish("credential.updated", new { hostId = host.Id });
        return removed > 0;
    }

    /// <summary>Forgets the per-rollout registry pull token held in memory. The next CI sync supplies a new one.</summary>
    public async Task<bool> DiscardPullTokenAsync(string actor, CancellationToken cancellationToken)
    {
        var held = _pullToken.HasToken;
        _pullToken.Clear();
        await _audit.AppendAsync(actor, "release.pull_token_discarded", null, new Dictionary<string, object?> { ["held"] = held }, cancellationToken);
        _events.Publish("credential.updated", new { scope = "pull-token" });
        return held;
    }
}
