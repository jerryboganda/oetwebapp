using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Fleet.Core.Validation;

namespace Fleet.Core.Policy;

public sealed record Budgets(int CpuMilli, int MemMiB, int TmpMiB);

public sealed record PressureSettings(
    int ReduceCpuPct,
    int ReduceMemFreePct,
    int RestoreCpuPct,
    int RestoreMemFreePct,
    int RestoreAfterSeconds);

public sealed record PollSettings(int Idle, int Min, int Max);

public sealed record AgentImagePolicy(IReadOnlyList<string> ApprovedDigests, string? Target, string MinVersion);

/// <summary>
/// The desired state of one node (OET-RWP/1 section 7.3). The manager is its single writer;
/// the API validates the same ranges again and never clamps silently.
/// </summary>
public sealed record NodePolicy(
    IReadOnlyList<string> AllowedKinds,
    int MaxConcurrency,
    IReadOnlyDictionary<string, int> PerKind,
    Budgets Budgets,
    PressureSettings Pressure,
    PollSettings PollSeconds,
    AgentImagePolicy AgentImage);

public static class FleetJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}

public static class PolicyDefaults
{
    public const string DefaultMinAgentVersion = "1.0.0";
    public const string PdfExtractKind = "pdf.extract";

    public static readonly PressureSettings DefaultPressure = new(80, 20, 60, 25, 120);

    public static readonly PollSettings DefaultPoll = new(10, 5, 30);

    public static AgentImagePolicy EmptyAgentImage() =>
        new(Array.Empty<string>(), null, DefaultMinAgentVersion);

    /// <summary>The global default of section 8.4 for a 4 vCPU / 8 GiB node.</summary>
    public static NodePolicy Global() => new(
        new[] { PdfExtractKind },
        2,
        new Dictionary<string, int> { [PdfExtractKind] = 2 },
        new Budgets(3000, 5120, 3072),
        DefaultPressure,
        DefaultPoll,
        EmptyAgentImage());

    /// <summary>
    /// Scales the global default to the facts S1 recorded: all cores but one for compute, about
    /// 65 % of RAM (floor to 256 MiB) of which 60 % may be tmpfs, one slot per 1.5 cores.
    /// 4 cores / 7900 MiB gives exactly 3000 / 5120 / 3072 and 2 slots.
    /// </summary>
    public static NodePolicy ForHost(int cpuCores, int memTotalMiB, IReadOnlyList<string>? kinds = null)
    {
        var cpuMilli = Math.Clamp((Math.Max(cpuCores, 2) - 1) * 1000, 1000, 64000);
        var memMiB = Math.Clamp((int)(memTotalMiB * 0.65) / 256 * 256, 256, 262144);
        var tmpMiB = Math.Clamp((int)(memMiB * 0.6) / 256 * 256, 256, Math.Min(65536, memMiB));
        var concurrency = Math.Clamp(cpuMilli / 1500, 1, 8);
        var allowed = kinds is { Count: > 0 } ? kinds : new[] { PdfExtractKind };
        var perKind = allowed.ToDictionary(k => k, _ => concurrency, StringComparer.Ordinal);
        return new NodePolicy(
            allowed,
            concurrency,
            perKind,
            new Budgets(cpuMilli, memMiB, tmpMiB),
            DefaultPressure,
            DefaultPoll,
            EmptyAgentImage());
    }

    public static NodePolicy WithAgentImage(NodePolicy policy, AgentImagePolicy image) => policy with { AgentImage = image };
}

public static class PolicyResolver
{
    /// <summary>Effective policy = the host override when present, else the global policy (section 8.4).</summary>
    public static NodePolicy Effective(NodePolicy global, NodePolicy? hostOverride) => hostOverride ?? global;
}

public static class PolicyValidator
{
    private static readonly Regex KindPattern = new(@"\A[a-z][a-z0-9.-]{1,47}\z", RegexOptions.CultureInvariant);
    private static readonly Regex VersionPattern = new(@"\A\d{1,4}\.\d{1,4}\.\d{1,4}\z", RegexOptions.CultureInvariant);

    /// <summary>Ranges of OET-RWP/1 section 7.3. <paramref name="registryKinds"/> is the API kind registry when known.</summary>
    public static IReadOnlyList<ValidationIssue> Validate(NodePolicy policy, IReadOnlyCollection<string>? registryKinds = null)
    {
        var issues = new List<ValidationIssue>();
        void Add(string field, string message) => issues.Add(new ValidationIssue(field, "policy_invalid", message));

        if (policy.AllowedKinds is null || policy.AllowedKinds.Count > 16)
        {
            Add("allowedKinds", "allowedKinds must list at most 16 kinds.");
        }
        else
        {
            if (policy.AllowedKinds.Distinct(StringComparer.Ordinal).Count() != policy.AllowedKinds.Count)
            {
                Add("allowedKinds", "allowedKinds must not contain duplicates.");
            }

            foreach (var kind in policy.AllowedKinds)
            {
                if (!KindPattern.IsMatch(kind))
                {
                    Add("allowedKinds", "unsupported kind name.");
                }
                else if (registryKinds is not null && !registryKinds.Contains(kind, StringComparer.Ordinal))
                {
                    Add("allowedKinds", "kind '" + kind + "' is not in the API registry.");
                }
            }
        }

        if (policy.MaxConcurrency is < 0 or > 8)
        {
            Add("maxConcurrency", "maxConcurrency must be 0..8.");
        }

        if (policy.PerKind is null)
        {
            Add("perKind", "perKind is required.");
        }
        else
        {
            foreach (var (kind, limit) in policy.PerKind)
            {
                if (limit < 0 || limit > policy.MaxConcurrency)
                {
                    Add("perKind", "perKind[" + kind + "] must be 0..maxConcurrency.");
                }

                if (policy.AllowedKinds is not null && !policy.AllowedKinds.Contains(kind, StringComparer.Ordinal))
                {
                    Add("perKind", "perKind[" + kind + "] is not an allowed kind.");
                }
            }
        }

        var b = policy.Budgets;
        if (b is null)
        {
            Add("budgets", "budgets are required.");
        }
        else
        {
            if (b.CpuMilli is < 0 or > 64000)
            {
                Add("budgets.cpuMilli", "cpuMilli must be 0..64000.");
            }

            if (b.MemMiB is < 0 or > 262144)
            {
                Add("budgets.memMiB", "memMiB must be 0..262144.");
            }

            if (b.TmpMiB is < 0 or > 65536)
            {
                Add("budgets.tmpMiB", "tmpMiB must be 0..65536.");
            }

            if (b.TmpMiB > b.MemMiB)
            {
                Add("budgets.tmpMiB", "tmpMiB must not exceed memMiB (tmpfs is RAM).");
            }
        }

        var p = policy.Pressure;
        if (p is null)
        {
            Add("pressure", "pressure is required.");
        }
        else
        {
            foreach (var (name, value) in new[]
                     {
                         ("reduceCpuPct", p.ReduceCpuPct), ("reduceMemFreePct", p.ReduceMemFreePct),
                         ("restoreCpuPct", p.RestoreCpuPct), ("restoreMemFreePct", p.RestoreMemFreePct),
                     })
            {
                if (value is < 1 or > 100)
                {
                    Add("pressure." + name, name + " must be 1..100.");
                }
            }

            if (p.RestoreCpuPct >= p.ReduceCpuPct)
            {
                Add("pressure.restoreCpuPct", "restoreCpuPct must be below reduceCpuPct (hysteresis).");
            }

            if (p.RestoreMemFreePct <= p.ReduceMemFreePct)
            {
                Add("pressure.restoreMemFreePct", "restoreMemFreePct must be above reduceMemFreePct (hysteresis).");
            }

            if (p.RestoreAfterSeconds is < 30 or > 3600)
            {
                Add("pressure.restoreAfterSeconds", "restoreAfterSeconds must be 30..3600.");
            }
        }

        var poll = policy.PollSeconds;
        if (poll is null)
        {
            Add("pollSeconds", "pollSeconds is required.");
        }
        else
        {
            if (poll.Min is < 2 or > 60)
            {
                Add("pollSeconds.min", "min must be 2..60.");
            }

            if (poll.Idle < poll.Min || poll.Idle > 60)
            {
                Add("pollSeconds.idle", "idle must be min..60.");
            }

            if (poll.Max < poll.Idle || poll.Max > 120)
            {
                Add("pollSeconds.max", "max must be idle..120.");
            }
        }

        var image = policy.AgentImage;
        if (image is null)
        {
            Add("agentImage", "agentImage is required.");
        }
        else
        {
            if (image.ApprovedDigests is null || image.ApprovedDigests.Count > 8)
            {
                Add("agentImage.approvedDigests", "at most 8 approved digests.");
            }
            else
            {
                foreach (var digest in image.ApprovedDigests)
                {
                    if (!InputValidator.IsValidImageDigest(digest))
                    {
                        Add("agentImage.approvedDigests", "digest must match sha256:<64 hex>.");
                    }
                }

                if (image.Target is not null && !image.ApprovedDigests.Contains(image.Target, StringComparer.Ordinal))
                {
                    Add("agentImage.target", "target must be one of approvedDigests.");
                }
            }

            if (image.MinVersion is null || !VersionPattern.IsMatch(image.MinVersion))
            {
                Add("agentImage.minVersion", "minVersion must be MAJOR.MINOR.PATCH.");
            }
        }

        return issues;
    }

    public static void EnsureValid(NodePolicy policy, IReadOnlyCollection<string>? registryKinds = null)
    {
        var issues = Validate(policy, registryKinds);
        if (issues.Count > 0)
        {
            throw new FleetValidationException(issues);
        }
    }
}
