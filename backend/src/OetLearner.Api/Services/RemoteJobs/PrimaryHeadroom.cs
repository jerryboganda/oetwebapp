using System.Globalization;
using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>Whether the primary can afford to run extraction itself right now (OET-RWP/1 section 3.8).</summary>
public interface IPrimaryHeadroom
{
    bool HasHeadroom();
}

/// <summary>
/// Pure decision over cgroup v2 readings. Headroom needs ALL of: CPU pressure <c>some avg10 &lt; 25</c>, memory pressure
/// <c>some avg10 &lt; 5</c>, and the container's memory working set under 75% of its limit. A reading that is missing or
/// unparseable means NO headroom (fail closed). A memory limit of <c>max</c> (unlimited) skips only the working-set test.
/// </summary>
public static partial class PrimaryHeadroomEvaluator
{
    public const double CpuPressureLimit = 25;
    public const double MemoryPressureLimit = 5;
    public const double MemoryUsageLimit = 0.75;

    [GeneratedRegex(@"^some\s+avg10=(?<v>[0-9]+(\.[0-9]+)?)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex SomeAvg10();

    /// <summary>Extracts <c>some avg10</c> from a PSI file, or null when absent.</summary>
    public static double? ParseSomeAvg10(string? pressureText)
    {
        if (string.IsNullOrWhiteSpace(pressureText)) return null;
        var match = SomeAvg10().Match(pressureText);
        return match.Success && double.TryParse(match.Groups["v"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    public static bool Evaluate(string? cpuPressure, string? memoryPressure, string? memoryCurrent, string? memoryMax)
    {
        var cpu = ParseSomeAvg10(cpuPressure);
        var memory = ParseSomeAvg10(memoryPressure);
        if (cpu is null || memory is null) return false;
        if (cpu >= CpuPressureLimit || memory >= MemoryPressureLimit) return false;

        if (!long.TryParse(memoryCurrent?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var current)) return false;
        var max = memoryMax?.Trim();
        if (string.Equals(max, "max", StringComparison.Ordinal)) return true;
        if (!long.TryParse(max, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) || limit <= 0) return false;
        return current < limit * MemoryUsageLimit;
    }
}

/// <summary>
/// Reads this process's cgroup v2 files (<c>/sys/fs/cgroup</c>) at most once per ten seconds. Any I/O failure (not Linux,
/// no cgroup v2, no permission) answers "no headroom": the hard-after rule of the placement code keeps work flowing.
/// (These are kernel pseudo-files, not media or user data, so they do not go through <c>IFileStorage</c>.)
/// </summary>
public sealed class CgroupPrimaryHeadroom(TimeProvider timeProvider) : IPrimaryHeadroom
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private DateTimeOffset _checkedAt = DateTimeOffset.MinValue;
    private bool _value;

    public bool HasHeadroom()
    {
        var now = timeProvider.GetUtcNow();
        lock (_gate)
        {
            if (now - _checkedAt < CacheFor) return _value;
            _value = Read();
            _checkedAt = now;
            return _value;
        }
    }

    private static bool Read()
    {
        try
        {
            const string root = "/sys/fs/cgroup/";
            return PrimaryHeadroomEvaluator.Evaluate(
                File.ReadAllText(root + "cpu.pressure"),
                File.ReadAllText(root + "memory.pressure"),
                File.ReadAllText(root + "memory.current"),
                File.ReadAllText(root + "memory.max"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
