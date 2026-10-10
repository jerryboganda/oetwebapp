using Microsoft.EntityFrameworkCore;

namespace Fleet.Manager.Persistence;

/// <summary>Plain data access for the <c>hosts</c> table. Hosts are never deleted; a removed host stays as <c>Removed</c> history.</summary>
public sealed class HostStore
{
    private readonly IDbContextFactory<FleetDbContext> _factory;
    private readonly TimeProvider _time;

    public HostStore(IDbContextFactory<FleetDbContext> factory, TimeProvider time)
    {
        _factory = factory;
        _time = time;
    }

    public async Task<HostEntity?> GetAsync(string id, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Hosts.AsNoTracking().FirstOrDefaultAsync(h => h.Id == id, cancellationToken);
    }

    public async Task<HostEntity?> FindByNodeRefAsync(string nodeRef, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Hosts.AsNoTracking().FirstOrDefaultAsync(h => h.NodeRef == nodeRef, cancellationToken);
    }

    /// <summary>A host that has not been removed and uses this address and port.</summary>
    public async Task<HostEntity?> FindLiveByAddressAsync(string address, int port, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Hosts.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Address == address && h.SshPort == port && h.Lifecycle != "Removed", cancellationToken);
    }

    public async Task<IReadOnlyList<HostEntity>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Hosts.AsNoTracking().OrderBy(h => h.CreatedAt).ToListAsync(cancellationToken);
    }

    /// <summary>Hosts that have a node in the API and are not removed.</summary>
    public async Task<IReadOnlyList<HostEntity>> ListWithNodesAsync(CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Hosts.AsNoTracking()
            .Where(h => h.ApiNodeId != null && h.Lifecycle != "Removed")
            .OrderBy(h => h.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HostEntity>> ListByLifecycleAsync(string lifecycle, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Hosts.AsNoTracking().Where(h => h.Lifecycle == lifecycle).OrderBy(h => h.CreatedAt).ToListAsync(cancellationToken);
    }

    /// <summary>Loads, mutates and saves one host. Returns false when it does not exist.</summary>
    public async Task<bool> UpdateAsync(string id, Action<HostEntity> mutate, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var host = await db.Hosts.FirstOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (host is null)
        {
            return false;
        }

        mutate(host);
        host.UpdatedAt = _time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Atomic, durable grant revision. Renewals do not bump; every changed payload does.
    /// The first revision exceeds the legacy DesiredRevision so an existing UBAG grant is replaced.</summary>
    public async Task<long> AcceptUbagAllocationAsync(string id, string fingerprint, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE hosts SET
                ubag_allocation_revision = CASE
                    WHEN ubag_allocation_revision = 0 THEN desired_revision + 1
                    WHEN ubag_allocation_fingerprint = {fingerprint} THEN ubag_allocation_revision
                    ELSE ubag_allocation_revision + 1 END,
                ubag_allocation_fingerprint = {fingerprint}
            WHERE id = {id}", cancellationToken);
        if (changed != 1) throw new InvalidOperationException("UBAG allocation host no longer exists");
        var revision = await db.Hosts.AsNoTracking().Where(h => h.Id == id)
            .Select(h => h.UbagAllocationRevision).SingleAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return revision;
    }
}
