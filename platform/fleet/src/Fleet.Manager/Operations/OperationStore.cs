using Fleet.Core.Audit;
using Fleet.Core.Domain;
using Fleet.Manager.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fleet.Manager.Operations;

/// <summary>
/// Persistence of <c>operations</c> and <c>operation_steps</c>. An operation is a durable, resumable
/// unit of work: its steps are created up front as <c>pending</c>, each step records its own result,
/// and a crash at any point leaves enough in SQLite for the runner to continue. State changes use
/// optimistic concurrency on <c>state</c>, so the runner and an owner action can never both win.
/// Creation is hash-chained (<see cref="OperationSeal"/>).
/// </summary>
public sealed class OperationStore
{
    private static readonly List<string> RunnableStates = new()
    {
        nameof(EnrollmentState.Created),
        nameof(EnrollmentState.Bootstrapping),
        nameof(EnrollmentState.Provisioned),
        nameof(EnrollmentState.ImagePulling),
        nameof(EnrollmentState.AgentStarting),
        nameof(EnrollmentState.Verifying),
        nameof(EnrollmentState.Canary),
        nameof(GenericOperationState.Queued),
        nameof(GenericOperationState.Running),
    };

    private static readonly List<string> TerminalStates = new()
    {
        nameof(EnrollmentState.Active),
        nameof(EnrollmentState.Cancelled),
        nameof(GenericOperationState.Succeeded),
    };

    private readonly SemaphoreSlim _createGate = new(1, 1);
    private readonly IDbContextFactory<FleetDbContext> _factory;
    private readonly TimeProvider _time;

    public OperationStore(IDbContextFactory<FleetDbContext> factory, TimeProvider time)
    {
        _factory = factory;
        _time = time;
    }

    public async Task<OperationEntity> CreateAsync(NewOperation spec, CancellationToken cancellationToken)
    {
        await _createGate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await _factory.CreateDbContextAsync(cancellationToken);
            var head = await db.Operations
                .AsNoTracking()
                .OrderByDescending(o => o.Seq)
                .Select(o => new { o.Seq, o.Hash })
                .FirstOrDefaultAsync(cancellationToken);

            var now = DateTimeOffset.FromUnixTimeMilliseconds(_time.GetUtcNow().ToUnixTimeMilliseconds());
            var id = "op_" + Guid.NewGuid().ToString("N");
            var seq = (head?.Seq ?? 0) + 1;
            var prev = head?.Hash ?? AuditChain.Genesis;
            var kind = OperationKinds.ToWire(spec.Kind);
            var actor = LogScrubber.Scrub(spec.Actor, 64);
            var entity = new OperationEntity
            {
                Id = id,
                Seq = seq,
                Kind = kind,
                HostId = spec.HostId,
                State = spec.InitialState,
                ParamsJson = spec.ParamsJson,
                DataJson = "{}",
                Actor = actor,
                RequestId = spec.RequestId,
                StartedAt = now,
                UpdatedAt = now,
                PrevHash = prev,
                Hash = OperationSeal.ComputeHash(prev, seq, id, kind, spec.HostId, spec.ParamsJson, actor, spec.RequestId, now),
            };
            db.Operations.Add(entity);
            for (var i = 0; i < spec.StepNames.Count; i++)
            {
                db.OperationSteps.Add(new OperationStepEntity { OperationId = id, Seq = i + 1, Name = spec.StepNames[i], State = "pending" });
            }

            await db.SaveChangesAsync(cancellationToken);
            return entity;
        }
        finally
        {
            _createGate.Release();
        }
    }

    public async Task<OperationEntity?> GetAsync(string id, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Operations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<OperationStepEntity>> GetStepsAsync(string id, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.OperationSteps.AsNoTracking().Where(s => s.OperationId == id).OrderBy(s => s.Seq).ToListAsync(cancellationToken);
    }

    /// <summary>Newest first.</summary>
    public async Task<IReadOnlyList<OperationEntity>> ListAsync(int take, string? hostId, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var query = db.Operations.AsNoTracking();
        if (hostId is not null)
        {
            query = query.Where(o => o.HostId == hostId);
        }

        return await query.OrderByDescending(o => o.Seq).Take(Math.Clamp(take, 1, 500)).ToListAsync(cancellationToken);
    }

    /// <summary>The newest operation of this kind for the host that has not reached a terminal state (Failed counts as open: it can be retried).</summary>
    public async Task<OperationEntity?> FindOpenAsync(OperationKind kind, string hostId, CancellationToken cancellationToken)
    {
        var wire = OperationKinds.ToWire(kind);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Operations
            .AsNoTracking()
            .Where(o => o.HostId == hostId && o.Kind == wire && !TerminalStates.Contains(o.State))
            .OrderByDescending(o => o.Seq)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>The newest rollout that has not reached a terminal state. Only one rolling update may run at a time.</summary>
    public async Task<OperationEntity?> FindOpenRolloutAsync(CancellationToken cancellationToken)
    {
        var wire = OperationKinds.ToWire(OperationKind.Rollout);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Operations
            .AsNoTracking()
            .Where(o => o.Kind == wire && !TerminalStates.Contains(o.State))
            .OrderByDescending(o => o.Seq)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>The enroll operation of a host (there is at most one).</summary>
    public async Task<OperationEntity?> FindEnrollAsync(string hostId, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Operations.AsNoTracking().FirstOrDefaultAsync(o => o.HostId == hostId && o.Kind == "enroll", cancellationToken);
    }

    /// <summary>Ids of operations the worker can advance now, oldest first.</summary>
    public async Task<IReadOnlyList<string>> ListRunnableIdsAsync(bool includeAwaitingSync, CancellationToken cancellationToken)
    {
        var states = new List<string>(RunnableStates);
        if (includeAwaitingSync)
        {
            states.Add(nameof(EnrollmentState.ImageAwaitingSync));
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Operations
            .AsNoTracking()
            .Where(o => states.Contains(o.State))
            .OrderBy(o => o.Seq)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Loads, mutates and saves one operation. When <paramref name="expectedState"/> is given and the
    /// operation is in another state, or a concurrent writer changed the state, nothing is written and false is returned.
    /// </summary>
    public async Task<bool> UpdateAsync(string id, string? expectedState, Action<OperationEntity> mutate, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var op = await db.Operations.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (op is null || (expectedState is not null && !string.Equals(op.State, expectedState, StringComparison.Ordinal)))
        {
            return false;
        }

        mutate(op);
        op.UpdatedAt = _time.GetUtcNow();
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public async Task SaveDataAsync(string id, OperationData data, CancellationToken cancellationToken)
    {
        var json = data.ToJson();
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var now = _time.GetUtcNow();
        await db.Operations.Where(o => o.Id == id).ExecuteUpdateAsync(
            s => s.SetProperty(o => o.DataJson, json).SetProperty(o => o.UpdatedAt, now),
            cancellationToken);
    }

    public async Task<bool> RequestCancelAsync(string id, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var changed = await db.Operations.Where(o => o.Id == id).ExecuteUpdateAsync(
            s => s.SetProperty(o => o.CancelRequested, true),
            cancellationToken);
        return changed > 0;
    }

    public async Task UpdateStepAsync(string operationId, int seq, Action<OperationStepEntity> mutate, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var step = await db.OperationSteps.FirstOrDefaultAsync(s => s.OperationId == operationId && s.Seq == seq, cancellationToken);
        if (step is null)
        {
            return;
        }

        mutate(step);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Crash recovery: a step left <c>running</c> by a dead process goes back to <c>pending</c> (steps are idempotent).</summary>
    public async Task<int> ResetRunningStepsAsync(CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.OperationSteps.Where(s => s.State == "running").ExecuteUpdateAsync(
            s => s.SetProperty(x => x.State, "pending"),
            cancellationToken);
    }

    /// <summary>Verifies the creation seals of all operations in chain order.</summary>
    public async Task<ChainVerification> VerifyChainAsync(CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Operations.AsNoTracking().OrderBy(o => o.Seq).ToListAsync(cancellationToken);
        var expectedPrev = AuditChain.Genesis;
        var count = 0;
        foreach (var op in rows)
        {
            count++;
            if (!string.Equals(op.PrevHash, expectedPrev, StringComparison.Ordinal))
            {
                return new ChainVerification(false, count, op.Seq, "previous hash does not match the preceding operation");
            }

            var recomputed = OperationSeal.ComputeHash(op.PrevHash, op.Seq, op.Id, op.Kind, op.HostId, op.ParamsJson, op.Actor, op.RequestId, op.StartedAt);
            if (!string.Equals(recomputed, op.Hash, StringComparison.Ordinal))
            {
                return new ChainVerification(false, count, op.Seq, "operation seal does not match its creation fields");
            }

            expectedPrev = op.Hash;
        }

        return ChainVerification.Ok(count);
    }
}
