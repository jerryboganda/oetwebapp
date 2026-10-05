using System.Text.Json;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>Aggregates shown next to a node that are not columns of the node row.</summary>
public sealed record RemoteNodeAggregates(int LeasedCount, int LeasedWeight, int ActiveTokens, DateTimeOffset? NextTokenExpiry)
{
    public static readonly RemoteNodeAggregates None = new(0, 0, 0, null);
}

/// <summary>
/// The node object of OET-RWP/1 section 7.1.2 (never any secret, storage key or token). Health is derived from the last
/// heartbeat and never stored.
/// </summary>
public static class RemoteNodeView
{
    public const string Online = "Online";
    public const string Stale = "Stale";
    public const string Offline = "Offline";
    public const string Unseen = "Unseen";

    public static string Health(DateTimeOffset? lastHeartbeat, DateTimeOffset now, RemoteJobsOptions options)
    {
        if (lastHeartbeat is null) return Unseen;
        var age = now - lastHeartbeat.Value;
        if (age <= TimeSpan.FromSeconds(options.NodeStaleAfterSeconds)) return Online;
        return age <= TimeSpan.FromSeconds(options.NodeOfflineAfterSeconds) ? Stale : Offline;
    }

    public static Dictionary<string, object?> Build(
        RemoteWorker node,
        RemoteNodeAggregates aggregates,
        DateTimeOffset now,
        RemoteJobsOptions options)
    {
        var capacity = ParseObject(node.LastCapacityJson);
        var load = ParseObject(node.LastLoadJson);
        var policy = RemoteDesiredState.Build(node);

        return new Dictionary<string, object?>
        {
            ["id"] = node.Id,
            ["nodeRef"] = node.NodeRef,
            ["displayName"] = node.DisplayName,
            ["status"] = node.Status,
            ["statusReason"] = node.StatusReason,
            ["statusChangedAt"] = RemoteIds.FormatTime(node.StatusChangedAt),
            ["health"] = Health(node.LastHeartbeatAt, now, options),
            ["region"] = node.Region,
            ["provider"] = node.Provider,
            ["lastSeenAt"] = RemoteIds.FormatTime(node.LastSeenAt),
            ["lastHeartbeatAt"] = RemoteIds.FormatTime(node.LastHeartbeatAt),
            ["lastClaimAt"] = RemoteIds.FormatTime(node.LastClaimAt),
            ["agent"] = new Dictionary<string, object?>
            {
                ["version"] = node.AgentVersion,
                ["imageDigest"] = node.AgentImageDigest,
                ["protocol"] = node.ProtocolVersion,
                ["instanceId"] = node.CurrentInstanceId,
                ["kinds"] = ParseElement(node.KindsJson) ?? (object)Array.Empty<object>(),
            },
            ["capacity"] = Pick(capacity, "cpuBudgetFreeMilli", "memBudgetFreeMiB", "tmpFreeMiB", "heavySlotsFree", "effectiveConcurrency"),
            ["load"] = Pick(load, "cpuPct", "memFreePct", "pressure"),
            ["leases"] = new Dictionary<string, object?>
            {
                ["count"] = aggregates.LeasedCount,
                ["weight"] = aggregates.LeasedWeight,
                ["max"] = node.MaxConcurrency,
            },
            ["policy"] = policy,
            ["appliedRevision"] = node.AppliedRevision,
            ["integrityStrikes"] = node.IntegrityStrikes,
            ["lastCanary"] = node.LastCanaryAt is null
                ? null
                : new Dictionary<string, object?>
                {
                    ["at"] = RemoteIds.FormatTime(node.LastCanaryAt),
                    ["ok"] = node.LastCanaryOk,
                },
            ["tokens"] = new Dictionary<string, object?>
            {
                ["activeCount"] = aggregates.ActiveTokens,
                ["nextExpiryAt"] = RemoteIds.FormatTime(aggregates.NextTokenExpiry),
            },
        };
    }

    private static Dictionary<string, JsonElement>? ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? ParseElement(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, object?>? Pick(Dictionary<string, JsonElement>? source, params string[] names)
    {
        if (source is null) return null;
        var picked = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (source.TryGetValue(name, out var value)) picked[name] = value;
        }

        return picked;
    }
}
