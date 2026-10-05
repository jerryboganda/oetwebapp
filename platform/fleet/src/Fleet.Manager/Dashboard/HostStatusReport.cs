using System.Globalization;
using System.Text.Json;

namespace Fleet.Manager.Dashboard;

/// <summary>
/// What the manager last learned about a helper: the API node summary the monitor mirrors onto the host row and the restricted
/// <c>oet-fleet-ctl status</c> report (OET-RWP/1 section 7.4). EVERY value is helper-originated and therefore untrusted: strings are
/// capped, and the views render them through Razor's encoder.
/// </summary>
public sealed record HostStatusReport(
    string? NodeStatus,
    string? NodeHealth,
    string? AgentVersion,
    int? IntegrityStrikes,
    DateTimeOffset? CtlPolledAt,
    int? CtlLatencyMs,
    bool CtlReported,
    string? CtlFailure,
    string? Os,
    string? Arch,
    int? CpuCores,
    int? MemTotalMiB,
    int? MemAvailableMiB,
    int? DiskFreeGiB,
    long? UptimeSeconds,
    int? SwapMiB,
    bool? TimeSyncOk,
    string? DockerVersion,
    bool? DockerRunning,
    bool? AgentPresent,
    string? AgentState,
    string? AgentImageDigest,
    DateTimeOffset? AgentStartedAt,
    int? AgentRestartCount,
    bool? AgentOomKilled,
    int? CtlVersion);

public static class HostStatusParser
{
    private const int MaxText = 120;

    /// <summary>Parses <c>hosts.last_status_json</c>. Null when there is none or it is not readable (it is capped at 4000 characters when stored).</summary>
    public static HostStatusReport? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var host = Obj(root, "host");
            var ctl = host is { } h ? Obj(h, "status") : null;
            var ctlHost = ctl is { } c1 ? Obj(c1, "host") : null;
            var docker = ctl is { } c2 ? Obj(c2, "docker") : null;
            var agent = ctl is { } c3 ? Obj(c3, "agent") : null;
            var ctlMeta = ctl is { } c4 ? Obj(c4, "ctl") : null;

            // A ctl report that failed is {"ok":false,"reason":"..."}; a good one carries a "schema".
            var reported = ctl is { } c5 && c5.TryGetProperty("schema", out _);
            string? failure = null;
            if (ctl is { } c6 && !reported)
            {
                failure = Str(c6, "reason") ?? Str(c6, "error") ?? "ctl_failed";
            }

            return new HostStatusReport(
                Str(root, "status"),
                Str(root, "health"),
                Str(root, "agentVersion"),
                Int(root, "integrityStrikes"),
                host is { } h2 ? Time(h2, "polledAt") : null,
                host is { } h3 ? Int(h3, "latencyMs") : null,
                reported,
                failure,
                ctlHost is { } e1 ? Str(e1, "os") : null,
                ctlHost is { } e2 ? Str(e2, "arch") : null,
                ctlHost is { } e3 ? Int(e3, "cpuCores") : null,
                ctlHost is { } e4 ? Int(e4, "memTotalMiB") : null,
                ctlHost is { } e5 ? Int(e5, "memAvailableMiB") : null,
                ctlHost is { } e6 ? Int(e6, "diskFreeGiB") : null,
                ctlHost is { } e7 ? Long(e7, "uptimeSeconds") : null,
                ctlHost is { } e8 ? Int(e8, "swapMiB") : null,
                ctlHost is { } e9 ? Bool(e9, "timeSyncOk") : null,
                docker is { } d1 ? Str(d1, "version") : null,
                docker is { } d2 ? Bool(d2, "running") : null,
                agent is { } a1 ? Bool(a1, "present") : null,
                agent is { } a2 ? Str(a2, "state") : null,
                agent is { } a3 ? Str(a3, "imageDigest") : null,
                agent is { } a4 ? Time(a4, "startedAt") : null,
                agent is { } a5 ? Int(a5, "restartCount") : null,
                agent is { } a6 ? Bool(a6, "oomKilled") : null,
                ctlMeta is { } m1 ? Int(m1, "version") : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? Obj(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static string? Str(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return text is null ? null : (text.Length > MaxText ? text[..MaxText] : text);
    }

    private static int? Int(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetInt32(out var number) ? number : null;
    }

    private static long? Long(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetInt64(out var number) ? number : null;
    }

    private static bool? Bool(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static DateTimeOffset? Time(JsonElement parent, string name)
    {
        var text = Str(parent, name);
        return text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;
    }
}
