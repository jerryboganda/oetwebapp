using Fleet.Core.Policy;
using Fleet.Core.Validation;

namespace Fleet.Manager.Dashboard;

/// <summary>One job kind row of the policy form.</summary>
public sealed class KindInput
{
    public string? Kind { get; set; }

    public bool Allowed { get; set; }

    /// <summary>The per-kind concurrency cap; blank means "the helper's maximum concurrency".</summary>
    public int? Max { get; set; }
}

/// <summary>
/// The policy form as it is posted: flat numbers and a list of kinds. <see cref="TryBuild"/> turns it into a <see cref="NodePolicy"/> on top of the
/// current one (the approved-image window is never edited here); the ranges themselves are checked by <see cref="PolicyValidator"/>, the single place
/// that knows them, when the policy service stores it.
/// </summary>
public sealed class PolicyInput
{
    /// <summary>More kind rows than this are ignored (the registry is small; a tampered form must not grow without bound).</summary>
    public const int MaxKinds = 32;

    public int? MaxConcurrency { get; set; }

    public int? CpuMilli { get; set; }

    public int? MemMiB { get; set; }

    public int? TmpMiB { get; set; }

    public int? ReduceCpuPct { get; set; }

    public int? ReduceMemFreePct { get; set; }

    public int? RestoreCpuPct { get; set; }

    public int? RestoreMemFreePct { get; set; }

    public int? RestoreAfterSeconds { get; set; }

    public int? PollIdle { get; set; }

    public int? PollMin { get; set; }

    public int? PollMax { get; set; }

    public List<KindInput> Kinds { get; set; } = new();

    /// <summary>Fills the form from a policy. <paramref name="knownKinds"/> are the kinds to offer (the API registry and whatever is already allowed).</summary>
    public static PolicyInput From(NodePolicy policy, IEnumerable<string> knownKinds)
    {
        var names = new SortedSet<string>(knownKinds, StringComparer.Ordinal);
        foreach (var kind in policy.AllowedKinds)
        {
            names.Add(kind);
        }

        return new PolicyInput
        {
            MaxConcurrency = policy.MaxConcurrency,
            CpuMilli = policy.Budgets.CpuMilli,
            MemMiB = policy.Budgets.MemMiB,
            TmpMiB = policy.Budgets.TmpMiB,
            ReduceCpuPct = policy.Pressure.ReduceCpuPct,
            ReduceMemFreePct = policy.Pressure.ReduceMemFreePct,
            RestoreCpuPct = policy.Pressure.RestoreCpuPct,
            RestoreMemFreePct = policy.Pressure.RestoreMemFreePct,
            RestoreAfterSeconds = policy.Pressure.RestoreAfterSeconds,
            PollIdle = policy.PollSeconds.Idle,
            PollMin = policy.PollSeconds.Min,
            PollMax = policy.PollSeconds.Max,
            Kinds = names
                .Select(name => new KindInput
                {
                    Kind = name,
                    Allowed = policy.AllowedKinds.Contains(name, StringComparer.Ordinal),
                    Max = policy.PerKind.TryGetValue(name, out var cap) ? cap : policy.MaxConcurrency,
                })
                .ToList(),
        };
    }

    /// <summary>
    /// The policy this form describes, or null with the problems when a number is missing. Out-of-range numbers are NOT judged here:
    /// <see cref="PolicyValidator"/> does that when the policy is stored, with the ranges of OET-RWP/1 section 7.3.
    /// </summary>
    public NodePolicy? TryBuild(NodePolicy baseline, out IReadOnlyList<ValidationIssue> issues)
    {
        var problems = new List<ValidationIssue>();

        int Need(int? value, string field)
        {
            if (value is null)
            {
                problems.Add(new ValidationIssue(field, "policy_invalid", field + " is required: enter a whole number."));
                return 0;
            }

            return value.Value;
        }

        var maxConcurrency = Need(MaxConcurrency, "maxConcurrency");
        var cpu = Need(CpuMilli, "budgets.cpuMilli");
        var mem = Need(MemMiB, "budgets.memMiB");
        var tmp = Need(TmpMiB, "budgets.tmpMiB");
        var pressure = new PressureSettings(
            Need(ReduceCpuPct, "pressure.reduceCpuPct"),
            Need(ReduceMemFreePct, "pressure.reduceMemFreePct"),
            Need(RestoreCpuPct, "pressure.restoreCpuPct"),
            Need(RestoreMemFreePct, "pressure.restoreMemFreePct"),
            Need(RestoreAfterSeconds, "pressure.restoreAfterSeconds"));
        var poll = new PollSettings(Need(PollIdle, "pollSeconds.idle"), Need(PollMin, "pollSeconds.min"), Need(PollMax, "pollSeconds.max"));

        var allowed = new List<string>();
        var perKind = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in Kinds.Take(MaxKinds))
        {
            var name = row.Kind?.Trim();
            if (string.IsNullOrEmpty(name) || !row.Allowed || perKind.ContainsKey(name))
            {
                continue;
            }

            allowed.Add(name);
            perKind[name] = row.Max ?? maxConcurrency;
        }

        issues = problems;
        return problems.Count > 0
            ? null
            : baseline with
            {
                AllowedKinds = allowed,
                MaxConcurrency = maxConcurrency,
                PerKind = perKind,
                Budgets = new Budgets(cpu, mem, tmp),
                Pressure = pressure,
                PollSeconds = poll,
            };
    }
}
