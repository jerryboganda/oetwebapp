using System.Globalization;

namespace OetLearner.Api.Services.RemoteJobs;

public enum RemoteRangeKind
{
    /// <summary>No (usable) Range header: serve the whole object with 200.</summary>
    Full,

    /// <summary>A single satisfiable range: serve 206 with <c>Content-Range</c>.</summary>
    Partial,

    /// <summary>Multiple ranges, a malformed header or an unsatisfiable range: 416.</summary>
    Unsatisfiable,
}

public readonly record struct RemoteRange(RemoteRangeKind Kind, long Start, long End)
{
    public long Length => Kind == RemoteRangeKind.Partial ? End - Start + 1 : 0;

    public static readonly RemoteRange Whole = new(RemoteRangeKind.Full, 0, 0);

    public static readonly RemoteRange Rejected = new(RemoteRangeKind.Unsatisfiable, 0, 0);
}

/// <summary>
/// Single-range <c>Range</c> parsing for the inputs route (OET-RWP/1 section 4.3): <c>bytes=a-b</c>, <c>bytes=a-</c>,
/// <c>bytes=-n</c>. Anything else that is present (several ranges, other units, garbage, a start beyond the object) is
/// unsatisfiable, so an agent that resumes a download never silently receives the wrong bytes.
/// </summary>
public static class RemoteRangeParser
{
    public static RemoteRange Parse(string? header, long length)
    {
        if (string.IsNullOrWhiteSpace(header)) return RemoteRange.Whole;

        const string unit = "bytes=";
        var value = header.Trim();
        if (!value.StartsWith(unit, StringComparison.OrdinalIgnoreCase)) return RemoteRange.Rejected;

        var spec = value[unit.Length..].Trim();
        if (spec.Length == 0 || spec.Contains(',')) return RemoteRange.Rejected;

        var dash = spec.IndexOf('-');
        if (dash < 0) return RemoteRange.Rejected;

        var first = spec[..dash].Trim();
        var last = spec[(dash + 1)..].Trim();

        if (first.Length == 0)
        {
            // Suffix range: the last n bytes.
            if (!TryParse(last, out var suffix) || suffix <= 0 || length == 0) return RemoteRange.Rejected;
            var start = Math.Max(0, length - suffix);
            return new RemoteRange(RemoteRangeKind.Partial, start, length - 1);
        }

        if (!TryParse(first, out var startOffset) || startOffset >= length) return RemoteRange.Rejected;

        long to;
        if (last.Length == 0)
        {
            to = length - 1;
        }
        else
        {
            if (!TryParse(last, out to) || to < startOffset) return RemoteRange.Rejected;
            to = Math.Min(to, length - 1);
        }

        return new RemoteRange(RemoteRangeKind.Partial, startOffset, to);
    }

    private static bool TryParse(string text, out long value)
        => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;
}
