using Fleet.Core.Audit;
using Fleet.Manager.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fleet.Manager.Persistence;

public interface IAuditService
{
    /// <summary>Appends one hash-chained row. Details are scrubbed and capped (see <see cref="AuditDetails"/>).</summary>
    Task<AuditRecord> AppendAsync(
        string actor,
        string action,
        string? target,
        IReadOnlyDictionary<string, object?>? details = null,
        CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<AuditRecord>> ListAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>Verifies the whole chain, oldest to newest (RW-145).</summary>
    Task<ChainVerification> VerifyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Hash-chained audit log. Writes are serialised in-process (the manager is a single instance
/// owning its SQLite file), so a row's <c>prev_hash</c> is always the true head.
/// </summary>
public sealed class AuditService : IAuditService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IDbContextFactory<FleetDbContext> _factory;
    private readonly TimeProvider _time;
    private readonly IEventBus _events;

    public AuditService(IDbContextFactory<FleetDbContext> factory, TimeProvider time, IEventBus events)
    {
        _factory = factory;
        _time = time;
        _events = events;
    }

    public async Task<AuditRecord> AppendAsync(
        string actor,
        string action,
        string? target,
        IReadOnlyDictionary<string, object?>? details = null,
        CancellationToken cancellationToken = default)
    {
        var safeActor = Cap(LogScrubber.Scrub(actor, 64), 64);
        var safeAction = Cap(action, 64);
        var safeTarget = target is null ? null : Cap(LogScrubber.Scrub(target, 128), 128);
        var detailsJson = AuditDetails.ToCanonicalJson(details);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await _factory.CreateDbContextAsync(cancellationToken);
            var head = await db.Audit
                .AsNoTracking()
                .OrderByDescending(a => a.Id)
                .Select(a => new { a.Id, a.Hash })
                .FirstOrDefaultAsync(cancellationToken);

            var id = (head?.Id ?? 0) + 1;
            var prevHash = head?.Hash ?? AuditChain.Genesis;
            var at = DateTimeOffset.FromUnixTimeMilliseconds(_time.GetUtcNow().ToUnixTimeMilliseconds());
            var hash = AuditChain.ComputeHash(prevHash, id, at, safeActor, safeAction, safeTarget, detailsJson);

            db.Audit.Add(new AuditEntity
            {
                Id = id,
                At = at,
                Actor = safeActor,
                Action = safeAction,
                Target = safeTarget,
                DetailsJson = detailsJson,
                PrevHash = prevHash,
                Hash = hash,
            });
            await db.SaveChangesAsync(cancellationToken);

            var entry = new AuditRecord(id, at, safeActor, safeAction, safeTarget, detailsJson, prevHash, hash);
            _events.Publish("audit.appended", new { id, at, action = safeAction, target = safeTarget });
            return entry;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AuditRecord>> ListAsync(int take, CancellationToken cancellationToken = default)
    {
        var bounded = Math.Clamp(take, 1, 500);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Audit
            .AsNoTracking()
            .OrderByDescending(a => a.Id)
            .Take(bounded)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToList();
    }

    public async Task<ChainVerification> VerifyAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Audit.AsNoTracking().OrderBy(a => a.Id).ToListAsync(cancellationToken);
        return AuditChain.Verify(rows.Select(ToRecord));
    }

    private static AuditRecord ToRecord(AuditEntity e) =>
        new(e.Id, e.At, e.Actor, e.Action, e.Target, e.DetailsJson, e.PrevHash, e.Hash);

    private static string Cap(string value, int max) => value.Length > max ? value[..max] : value;
}
