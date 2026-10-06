using System.Text.Json;
using System.Text.RegularExpressions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>The part of a node row the placement planner needs (read under the node lock at claim).</summary>
public sealed record RemoteNodePolicyView(
    string Status,
    bool Paused,
    IReadOnlyList<string> AllowedKinds,
    int MaxConcurrency,
    IReadOnlyDictionary<string, int> PerKind,
    int CpuBudgetMilli,
    int MemBudgetMiB,
    int TmpBudgetMiB)
{
    public static RemoteNodePolicyView From(RemoteWorker node) => new(
        node.Status,
        node.Paused,
        node.AllowedKinds ?? Array.Empty<string>(),
        node.MaxConcurrency,
        RemotePolicyJson.ReadKindLimits(node.KindLimitsJson),
        node.CpuBudgetMilli,
        node.MemBudgetMiB,
        node.TmpBudgetMiB);
}

/// <summary>One (kind, schema version, engine version) the node may be handed right now.</summary>
public sealed record ClaimPlanEntry(
    string Kind,
    int SchemaVersion,
    string EngineVersion,
    int MaxWeight,
    IReadOnlyList<string> Purposes);

/// <summary>Outcome of eligibility planning: the entries to pick from, or the reason there are none.</summary>
public sealed record ClaimPlan(IReadOnlyList<ClaimPlanEntry> Entries, string? EmptyReason)
{
    public bool IsEmpty => Entries.Count == 0;
}

/// <summary>
/// Placement eligibility of a node (OET-RWP/1 section 3.8, items 1, 3 and 4). Pure: no database, no clock.
/// The SERVER decides what a node may run from <c>AllowedKinds</c>, the registry, the flags and the budgets;
/// the agent's self-declared <c>kinds[]</c> and capacity only NARROW the offer.
/// </summary>
public static class RemoteClaimPlanner
{
    public static ClaimPlan Plan(
        RemoteNodePolicyView node,
        IReadOnlyList<RemoteKindOfferDto> offers,
        RemoteCapacityDto capacity,
        RemoteFlagSnapshot flags,
        RemoteJobsOptions options,
        int leasedWeightTotal,
        IReadOnlyDictionary<string, int> leasedWeightByKind)
    {
        // A Probation node (and a Quarantined one proving itself) is only ever offered the known-answer canary.
        var canaryOnly = node.Status is RemoteNodeStatus.Probation or RemoteNodeStatus.Quarantined;
        if (!canaryOnly && node.Status != RemoteNodeStatus.Active)
        {
            return new ClaimPlan([], "kind_disabled");
        }

        // The policy is authoritative: agent-reported budgets are clamped to it.
        var memFree = Math.Min(capacity.MemBudgetFreeMiB ?? 0, node.MemBudgetMiB);
        var cpuFree = Math.Min(capacity.CpuBudgetFreeMilli ?? 0, node.CpuBudgetMilli);
        var tmpFree = Math.Min(capacity.TmpFreeMiB ?? 0, node.TmpBudgetMiB);
        var slots = Math.Min((long)node.MaxConcurrency, capacity.EffectiveConcurrency ?? 0);
        var overallFree = slots - leasedWeightTotal;
        var agentSlotsFree = capacity.HeavySlotsFree ?? 0;

        var entries = new List<ClaimPlanEntry>();
        var anyAllowed = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var offer in offers)
        {
            var kind = offer.Kind!;
            if (!seen.Add(kind)) continue;

            var spec = RemoteJobKinds.Find(kind);
            if (spec is null) continue;
            if (!node.AllowedKinds.Contains(kind, StringComparer.Ordinal)) continue;

            var engine = RemoteJobKinds.EngineVersion(kind, options);
            if (engine is null || !string.Equals(offer.EngineVersion, engine, StringComparison.Ordinal)) continue;
            if (offer.SchemaVersions is null || !offer.SchemaVersions.Contains(spec.SchemaVersion)) continue;

            var purposes = new List<string>();
            if (canaryOnly)
            {
                if (flags.Master) purposes.Add(RemoteJobPurpose.Canary);
            }
            else
            {
                if (flags.KindEnabled(kind, RemoteJobPurpose.Apply)) purposes.Add(RemoteJobPurpose.Apply);
                if (kind == RemoteJobKinds.PdfExtract && flags.KindEnabled(kind, RemoteJobPurpose.Shadow))
                {
                    purposes.Add(RemoteJobPurpose.Shadow);
                }

                if (flags.Master) purposes.Add(RemoteJobPurpose.Canary);
            }

            if (purposes.Count == 0) continue;
            anyAllowed = true;

            var limits = spec.Limits;
            var fits = memFree >= limits.MemMiB + limits.TmpMiB
                && cpuFree >= limits.CpuMilli
                && tmpFree >= limits.TmpMiB;

            var kindCap = node.PerKind.TryGetValue(kind, out var configuredCap)
                ? Math.Min(configuredCap, node.MaxConcurrency)
                : node.MaxConcurrency;
            leasedWeightByKind.TryGetValue(kind, out var leasedOfKind);
            var maxWeight = Math.Min(Math.Min(overallFree, kindCap - leasedOfKind), agentSlotsFree);

            if (!fits || maxWeight < limits.Weight) continue;

            entries.Add(new ClaimPlanEntry(kind, spec.SchemaVersion, engine, (int)Math.Min(maxWeight, 1000), purposes));
        }

        return entries.Count > 0
            ? new ClaimPlan(entries, null)
            : new ClaimPlan([], anyAllowed ? "no_capacity" : "kind_disabled");
    }
}

/// <summary>Reading and writing the JSON policy columns of a node.</summary>
public static class RemotePolicyJson
{
    public static IReadOnlyDictionary<string, int> ReadKindLimits(string? json)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var value))
                {
                    result[property.Name] = value;
                }
            }
        }
        catch (JsonException)
        {
            // A corrupt column behaves as "no per-kind caps" (the node-wide cap still applies).
        }

        return result;
    }

    /// <summary>Overlays the stored JSON object (when valid) on <paramref name="defaults"/>, member by member.</summary>
    public static Dictionary<string, object?> Overlay(string? json, Dictionary<string, object?> defaults)
    {
        if (string.IsNullOrWhiteSpace(json)) return defaults;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return defaults;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                defaults[property.Name] = property.Value.Clone();
            }
        }
        catch (JsonException)
        {
            // Fall back to the defaults.
        }

        return defaults;
    }
}

/// <summary>The <c>desired</c> state delivered to an agent in claim and heartbeat responses (OET-RWP/1 section 7.3).</summary>
public static class RemoteDesiredState
{
    public static Dictionary<string, object?> Build(RemoteWorker node)
    {
        var drain = node.Status is RemoteNodeStatus.Draining or RemoteNodeStatus.Disabled;

        var pressure = RemotePolicyJson.Overlay(node.PressureJson, new Dictionary<string, object?>
        {
            ["reduceCpuPct"] = 80,
            ["reduceMemFreePct"] = 20,
            ["restoreCpuPct"] = 60,
            ["restoreMemFreePct"] = 25,
            ["restoreAfterSeconds"] = 120,
        });
        var poll = RemotePolicyJson.Overlay(node.PollJson, new Dictionary<string, object?>
        {
            ["idle"] = 10,
            ["min"] = 5,
            ["max"] = 30,
        });
        var agentImage = RemotePolicyJson.Overlay(node.DesiredAgentJson, new Dictionary<string, object?>
        {
            ["approvedDigests"] = Array.Empty<string>(),
            ["target"] = null,
            ["minVersion"] = "0.0.0",
        });

        return new Dictionary<string, object?>
        {
            ["revision"] = node.PolicyRevision,
            ["drain"] = drain,
            ["paused"] = node.Paused,
            ["allowedKinds"] = node.AllowedKinds ?? Array.Empty<string>(),
            ["maxConcurrency"] = node.MaxConcurrency,
            ["perKind"] = RemotePolicyJson.ReadKindLimits(node.KindLimitsJson),
            ["budgets"] = new Dictionary<string, object?>
            {
                ["cpuMilli"] = node.CpuBudgetMilli,
                ["memMiB"] = node.MemBudgetMiB,
                ["tmpMiB"] = node.TmpBudgetMiB,
            },
            ["pressure"] = pressure,
            ["pollSeconds"] = poll,
            ["agentImage"] = agentImage,
        };
    }
}

/// <summary>A node-policy update (<c>PUT /nodes/{id}/policy</c>) and its range validation (OET-RWP/1 section 7.3).</summary>
public sealed class RemotePolicyUpdateDto
{
    public long? ExpectedRevision { get; set; }
    public List<string>? AllowedKinds { get; set; }
    public int? MaxConcurrency { get; set; }
    public Dictionary<string, int>? PerKind { get; set; }
    public RemoteBudgetsDto? Budgets { get; set; }
    public RemotePressureDto? Pressure { get; set; }
    public RemotePollDto? PollSeconds { get; set; }
    public RemoteAgentImageDto? AgentImage { get; set; }
    public bool? Paused { get; set; }
}

public sealed class RemoteBudgetsDto
{
    public int? CpuMilli { get; set; }
    public int? MemMiB { get; set; }
    public int? TmpMiB { get; set; }
}

public sealed class RemotePressureDto
{
    public int? ReduceCpuPct { get; set; }
    public int? ReduceMemFreePct { get; set; }
    public int? RestoreCpuPct { get; set; }
    public int? RestoreMemFreePct { get; set; }
    public int? RestoreAfterSeconds { get; set; }
}

public sealed class RemotePollDto
{
    public int? Idle { get; set; }
    public int? Min { get; set; }
    public int? Max { get; set; }
}

public sealed class RemoteAgentImageDto
{
    public List<string>? ApprovedDigests { get; set; }
    public string? Target { get; set; }
    public string? MinVersion { get; set; }
}

public static partial class RemotePolicyValidation
{
    [GeneratedRegex("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();

    [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SemVerPattern();

    /// <summary>The kind names a policy may allow: exactly the registry.</summary>
    public static bool IsRegistryKind(string? kind) => RemoteJobKinds.Find(kind) is not null;

    /// <summary>
    /// Validates the supplied members of a policy update. Returns null when valid, else the first problem
    /// (the caller answers <c>422 policy_invalid</c>). Members left out are left unchanged.
    /// </summary>
    public static string? Validate(RemotePolicyUpdateDto policy, int currentMaxConcurrency, int currentMemMiB, int currentTmpMiB)
    {
        var maxConcurrency = policy.MaxConcurrency ?? currentMaxConcurrency;
        if (policy.MaxConcurrency is < 0 or > 8) return "maxConcurrency must be 0..8.";

        if (policy.AllowedKinds is not null)
        {
            if (policy.AllowedKinds.Count > RemoteJobKinds.All.Count) return "allowedKinds has too many entries.";
            foreach (var kind in policy.AllowedKinds)
            {
                if (!IsRegistryKind(kind)) return "allowedKinds contains a kind that is not in the registry.";
            }
        }

        if (policy.PerKind is not null)
        {
            foreach (var (kind, cap) in policy.PerKind)
            {
                if (!IsRegistryKind(kind)) return "perKind contains a kind that is not in the registry.";
                if (cap < 0 || cap > maxConcurrency) return "perKind values must be 0..maxConcurrency.";
            }
        }

        // Lowering the node-wide cap below an existing per-kind cap is fine: claim clamps to min(perKind, maxConcurrency).

        if (policy.Budgets is { } budgets)
        {
            if (budgets.CpuMilli is < 0 or > 64000) return "budgets.cpuMilli must be 0..64000.";
            if (budgets.MemMiB is < 0 or > 262144) return "budgets.memMiB must be 0..262144.";
            if (budgets.TmpMiB is < 0 or > 65536) return "budgets.tmpMiB must be 0..65536.";
            var mem = budgets.MemMiB ?? currentMemMiB;
            var tmp = budgets.TmpMiB ?? currentTmpMiB;
            if (tmp > mem) return "budgets.tmpMiB must not exceed budgets.memMiB.";
        }

        if (policy.Pressure is { } pressure)
        {
            foreach (var pct in new[] { pressure.ReduceCpuPct, pressure.ReduceMemFreePct, pressure.RestoreCpuPct, pressure.RestoreMemFreePct })
            {
                if (pct is null or < 1 or > 100) return "pressure percentages must be 1..100.";
            }

            if (pressure.RestoreCpuPct >= pressure.ReduceCpuPct) return "pressure.restoreCpuPct must be below reduceCpuPct.";
            if (pressure.RestoreMemFreePct <= pressure.ReduceMemFreePct) return "pressure.restoreMemFreePct must be above reduceMemFreePct.";
            if (pressure.RestoreAfterSeconds is null or < 30 or > 3600) return "pressure.restoreAfterSeconds must be 30..3600.";
        }

        if (policy.PollSeconds is { } poll)
        {
            if (poll.Min is null or < 2 or > 60) return "pollSeconds.min must be 2..60.";
            if (poll.Idle is null || poll.Idle < poll.Min || poll.Idle > 60) return "pollSeconds.idle must be min..60.";
            if (poll.Max is null || poll.Max < poll.Idle || poll.Max > 120) return "pollSeconds.max must be idle..120.";
        }

        if (policy.AgentImage is { } image)
        {
            var approved = image.ApprovedDigests ?? new List<string>();
            if (approved.Count > 8) return "agentImage.approvedDigests holds at most 8 digests.";
            if (approved.Any(d => !DigestPattern().IsMatch(d ?? string.Empty))) return "agentImage.approvedDigests must be sha256:<64 hex>.";
            if (image.Target is not null && !approved.Contains(image.Target, StringComparer.Ordinal))
            {
                return "agentImage.target must be one of approvedDigests.";
            }

            if (image.MinVersion is not null && !SemVerPattern().IsMatch(image.MinVersion))
            {
                return "agentImage.minVersion must be MAJOR.MINOR.PATCH.";
            }
        }

        return null;
    }
}
