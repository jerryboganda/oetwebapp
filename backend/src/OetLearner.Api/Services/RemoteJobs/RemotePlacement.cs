using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.RemoteJobs;

public enum RemotePlacementDecision
{
    /// <summary>Enqueue a remote job: a healthy, allowed, capable node with free capacity exists.</summary>
    Remote,

    /// <summary>No node can take it, but the primary has headroom: run it in-process.</summary>
    Local,

    /// <summary>Neither: leave the work pending (no job row) and re-evaluate on the next tick.</summary>
    Wait,
}

/// <summary>
/// Producer-side placement decision (OET-RWP/1 section 3.8): <c>Remote</c> when at least one node is Active, Online, allowed for
/// the kind, offers the exact kind/schema/engine version, and has reported free weight for it; else <c>Local</c> while the
/// primary has headroom; else <c>Wait</c>. Pull-based placement itself happens at claim time; this only decides whether a job
/// row is worth creating.
/// </summary>
public sealed class RemotePlacement(
    LearnerDbContext db,
    RemoteJobsSettings settings,
    IPrimaryHeadroom headroom,
    TimeProvider timeProvider)
{
    public async Task<RemotePlacementDecision> DecideAsync(string kind, int weight, CancellationToken ct)
    {
        if (await HasEligibleNodeAsync(kind, weight, ct)) return RemotePlacementDecision.Remote;
        return headroom.HasHeadroom() ? RemotePlacementDecision.Local : RemotePlacementDecision.Wait;
    }

    public async Task<bool> HasEligibleNodeAsync(string kind, int weight, CancellationToken ct)
    {
        var spec = RemoteJobKinds.Find(kind);
        var options = settings.Current;
        var engine = RemoteJobKinds.EngineVersion(kind, options);
        if (spec is null || engine is null) return false;

        var now = timeProvider.GetUtcNow();
        var cutoff = now - TimeSpan.FromSeconds(options.NodeStaleAfterSeconds);
        var nodes = await db.RemoteWorkers.AsNoTracking()
            .Where(w => w.Status == RemoteNodeStatus.Active && !w.Paused && w.LastHeartbeatAt != null && w.LastHeartbeatAt >= cutoff)
            .ToListAsync(ct);
        if (nodes.Count == 0) return false;

        var leased = await LeasedWeightsAsync(nodes.Select(n => n.Id).ToArray(), ct);
        foreach (var node in nodes)
        {
            if (!node.AllowedKinds.Contains(kind, StringComparer.Ordinal)) continue;
            if (!OffersKind(node.KindsJson, kind, spec.SchemaVersion, engine)) continue;

            var effective = EffectiveConcurrency(node.LastCapacityJson) ?? node.MaxConcurrency;
            var perKind = RemotePolicyJson.ReadKindLimits(node.KindLimitsJson);
            var kindCap = perKind.TryGetValue(kind, out var cap) ? Math.Min(cap, node.MaxConcurrency) : node.MaxConcurrency;
            var used = leased.TryGetValue(node.Id, out var found) ? found : new LeasedWeight();
            var free = Math.Min(Math.Min(node.MaxConcurrency, effective) - used.Total, kindCap - used.ByKind(kind));
            if (free >= weight) return true;
        }

        return false;
    }

    /// <summary>True when the node's reported kinds contain exactly (kind, schemaVersion, engineVersion).</summary>
    public static bool OffersKind(string? kindsJson, string kind, int schemaVersion, string engineVersion)
    {
        if (string.IsNullOrWhiteSpace(kindsJson)) return false;
        try
        {
            using var document = JsonDocument.Parse(kindsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return false;
            foreach (var offer in document.RootElement.EnumerateArray())
            {
                if (offer.ValueKind != JsonValueKind.Object) continue;
                if (!offer.TryGetProperty("kind", out var k) || k.GetString() != kind) continue;
                if (!offer.TryGetProperty("engineVersion", out var e) || e.GetString() != engineVersion) continue;
                if (!offer.TryGetProperty("schemaVersions", out var s) || s.ValueKind != JsonValueKind.Array) continue;
                if (s.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var parsed) && parsed == schemaVersion))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            // An unreadable report offers nothing.
        }

        return false;
    }

    private static int? EffectiveConcurrency(string? capacityJson)
    {
        if (string.IsNullOrWhiteSpace(capacityJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(capacityJson);
            return document.RootElement.TryGetProperty("effectiveConcurrency", out var value) && value.TryGetInt32(out var parsed)
                ? parsed
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class LeasedWeight
    {
        public int Total { get; set; }

        public Dictionary<string, int> PerKind { get; } = new(StringComparer.Ordinal);

        public int ByKind(string kind) => PerKind.TryGetValue(kind, out var weight) ? weight : 0;
    }

    private async Task<Dictionary<string, LeasedWeight>> LeasedWeightsAsync(string[] nodeIds, CancellationToken ct)
    {
        var result = new Dictionary<string, LeasedWeight>(StringComparer.Ordinal);
        var rows = await RemoteDb.QueryAsync(
            db,
            """
            SELECT "LeaseOwner", "Kind", COALESCE(SUM("Weight"), 0)::int AS "Weight"
            FROM "RemoteJobs" WHERE "State" = 'Leased' AND "LeaseOwner" = ANY(@ids) GROUP BY "LeaseOwner", "Kind";
            """,
            parameters => parameters.AddWithValue("ids", nodeIds),
            reader => (Node: RemoteDb.Str(reader, "LeaseOwner"), Kind: RemoteDb.Str(reader, "Kind"), Weight: RemoteDb.Int(reader, "Weight")),
            ct);

        foreach (var row in rows)
        {
            if (!result.TryGetValue(row.Node, out var entry))
            {
                entry = new LeasedWeight();
                result[row.Node] = entry;
            }

            entry.Total += row.Weight;
            entry.PerKind[row.Kind] = row.Weight;
        }

        return result;
    }
}

/// <summary>
/// Remembers, per unit of work, when it first had to wait because neither a remote node nor primary headroom was available
/// (OET-RWP/1 section 3.7: after <c>FallbackHardAfterMinutes</c> it runs locally regardless, so liveness beats purity).
/// Per process and best-effort: a restart only restarts the clock.
/// </summary>
public sealed class RemoteLocalWaitTracker(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _firstWait = new(StringComparer.Ordinal);

    /// <summary>True when <paramref name="key"/> has now waited at least <paramref name="hardAfter"/> (the clock starts on the first call).</summary>
    public bool HardDeadlinePassed(string key, TimeSpan hardAfter)
    {
        var now = timeProvider.GetUtcNow();
        var since = _firstWait.GetOrAdd(key, now);
        if (_firstWait.Count > 5000) Prune(now, hardAfter);
        return now - since >= hardAfter;
    }

    public void Clear(string key) => _firstWait.TryRemove(key, out _);

    private void Prune(DateTimeOffset now, TimeSpan hardAfter)
    {
        foreach (var (key, since) in _firstWait)
        {
            if (now - since > hardAfter + hardAfter) _firstWait.TryRemove(key, out _);
        }
    }
}
