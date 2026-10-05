using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Fleet.Agent;

/// <summary>Monotonic millisecond clock: all lease arithmetic on the agent uses it, never wall time (section 4.2.3).</summary>
internal interface IMonotonicClock
{
    long NowMs { get; }
}

internal sealed class SystemMonotonicClock : IMonotonicClock
{
    public long NowMs => Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000L);
}

/// <summary>Hashing helpers. Hashes are lowercase hex SHA-256 everywhere (section 0.3).</summary>
internal static class Hashing
{
    public static string Sha256Hex(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static string Sha256Hex(string utf8Text) => Sha256Hex(Encoding.UTF8.GetBytes(utf8Text));

    public static async Task<string> Sha256FileAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan | FileOptions.Asynchronous);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Constant-time equality of two hex strings of any case.</summary>
    public static bool HexEquals(string a, string b)
    {
        var x = Encoding.ASCII.GetBytes(a.ToLowerInvariant());
        var y = Encoding.ASCII.GetBytes(b.ToLowerInvariant());
        return x.Length == y.Length && CryptographicOperations.FixedTimeEquals(x, y);
    }
}

internal static class TimeFormat
{
    /// <summary>RFC 3339 UTC with millisecond precision (section 0.3).</summary>
    public static string Rfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}

internal static class Backoff
{
    private static readonly int[] Steps = [1, 2, 4, 8, 16, 30];

    /// <summary>Exponential backoff 1,2,4,8,16,30 s capped at 30 s with +/-25% jitter (section 2.7).</summary>
    public static TimeSpan Compute(int attempt, Random? rng = null)
    {
        var seconds = Steps[Math.Clamp(attempt, 0, Steps.Length - 1)];
        var jitter = 0.75 + (rng ?? Random.Shared).NextDouble() * 0.5;
        return TimeSpan.FromSeconds(seconds * jitter);
    }

    /// <summary>Retry-After handling: wait at least the server's value plus up to 20% jitter (section 2.3).</summary>
    public static TimeSpan RetryAfterWithJitter(TimeSpan retryAfter, Random? rng = null) =>
        TimeSpan.FromMilliseconds(retryAfter.TotalMilliseconds * (1.0 + (rng ?? Random.Shared).NextDouble() * 0.2));
}

/// <summary>Builds fail.message values: fixed vocabulary only, never content, ids of learners or paths (H3, RW-154).</summary>
internal static class FailMessage
{
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var builder = new StringBuilder(Math.Min(text.Length, 200));
        foreach (var ch in text)
        {
            if (builder.Length >= 200) break;
            var allowed = ch is >= 'A' and <= 'Z' || ch is >= 'a' and <= 'z' || ch is >= '0' and <= '9'
                || ch is ' ' or '_' or '.' or ':' or '/' or '=' or ',' or '(' or ')' or '-';
            builder.Append(allowed ? ch : '_');
        }
        // A slash can only come from a sloppy caller; messages never carry paths.
        return builder.ToString().Replace('/', '_');
    }
}

/// <summary>Exception summaries for logs: type names only, never messages (messages can embed paths or content).</summary>
internal static class Redact
{
    public static string Exception(Exception ex)
    {
        var inner = ex.InnerException;
        return inner is null ? ex.GetType().Name : ex.GetType().Name + "/" + inner.GetType().Name;
    }
}

/// <summary>Semantic version comparison for desired.agentImage.minVersion (MAJOR.MINOR.PATCH, optional suffix ignored).</summary>
internal static class SemVer
{
    public static bool TryParse(string? text, out (int Major, int Minor, int Patch) version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var core = text.Trim();
        var cut = core.IndexOfAny(['-', '+']);
        if (cut >= 0) core = core[..cut];
        var parts = core.Split('.');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)) return false;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)) return false;
        if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch)) return false;
        version = (major, minor, patch);
        return true;
    }

    /// <summary>True when <paramref name="actual"/> is lower than <paramref name="minimum"/>. Unparseable values never block.</summary>
    public static bool IsBelow(string? actual, string? minimum) =>
        TryParse(actual, out var a) && TryParse(minimum, out var m) && a.CompareTo(m) < 0;
}
