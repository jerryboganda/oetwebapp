using System.Globalization;
using System.Text;

namespace OetLearner.Api.Services.Recalls;

/// <summary>
/// Pure-function spelling-error classifier for the Recalls "Listen &amp; type" feature.
/// See <c>docs/RECALLS-MODULE-PLAN.md</c> §6 for the canonical taxonomy.
/// All inputs are NFC-normalised and lowercased before classification.
/// </summary>
public static class SpellingDiff
{
    public const string Correct = "correct";
    public const string CaseOnly = "case_only";
    public const string BritishVariant = "british_variant";
    public const string MissingLetter = "missing_letter";
    public const string ExtraLetter = "extra_letter";
    public const string Transposition = "transposition";
    public const string DoubleLetter = "double_letter";
    public const string Hyphen = "hyphen";
    public const string Homophone = "homophone";
    public const string Unknown = "unknown";

    /// <summary>
    /// Classify <paramref name="typed"/> against the canonical British spelling
    /// <paramref name="canonical"/>. Returns a (code, distance, segments) triple.
    /// Segments encode the letter-level diff for the UI to render colour-coded
    /// feedback without re-classifying.
    /// </summary>
    public static SpellingDiffResult Classify(
        string canonical,
        string typed,
        string? americanSpelling = null,
        IReadOnlyCollection<string>? similarSounding = null)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        ArgumentNullException.ThrowIfNull(typed);

        var c = Normalise(canonical);
        var t = Normalise(typed);

        if (c.Length == 0)
            return new SpellingDiffResult(Unknown, 0, [], canonical, typed);

        // Exact match.
        if (string.Equals(c, t, StringComparison.Ordinal))
            return new SpellingDiffResult(Correct, 0, BuildEqualSegments(c), canonical, typed);

        // Case-only difference.
        if (string.Equals(c.ToLowerInvariant(), t.ToLowerInvariant(), StringComparison.Ordinal))
            return new SpellingDiffResult(CaseOnly, 0, BuildEqualSegments(t), canonical, typed);

        var lowerC = c.ToLowerInvariant();
        var lowerT = t.ToLowerInvariant();

        // British variant (typed matches the American form).
        if (!string.IsNullOrWhiteSpace(americanSpelling))
        {
            var lowerA = Normalise(americanSpelling!).ToLowerInvariant();
            if (string.Equals(lowerT, lowerA, StringComparison.Ordinal))
                return new SpellingDiffResult(BritishVariant, EditDistance(lowerC, lowerT), DiffSegments(lowerC, lowerT), canonical, typed);
        }

        // Homophone — typed matches a known similar-sounding word.
        if (similarSounding is { Count: > 0 })
        {
            foreach (var alt in similarSounding)
            {
                if (string.IsNullOrWhiteSpace(alt)) continue;
                if (string.Equals(Normalise(alt).ToLowerInvariant(), lowerT, StringComparison.Ordinal))
                    return new SpellingDiffResult(Homophone, EditDistance(lowerC, lowerT), DiffSegments(lowerC, lowerT), canonical, typed);
            }
        }

        // Hyphen mismatch (one form has '-', the other replaces it with '' or ' ').
        if (lowerC.Contains('-') != lowerT.Contains('-')
            && string.Equals(lowerC.Replace("-", "").Replace(" ", ""), lowerT.Replace("-", "").Replace(" ", ""), StringComparison.Ordinal))
        {
            return new SpellingDiffResult(Hyphen, 1, DiffSegments(lowerC, lowerT), canonical, typed);
        }

        var distance = EditDistance(lowerC, lowerT);
        var lenDelta = lowerC.Length - lowerT.Length;

        // Transposition: distance 2 with an adjacent swap detected.
        if (distance == 2 && lenDelta == 0 && HasAdjacentTransposition(lowerC, lowerT))
            return new SpellingDiffResult(Transposition, 2, DiffSegments(lowerC, lowerT), canonical, typed);

        // Double-letter rule (e.g. "inflamation" -> "inflammation"). Triggers when
        // the canonical has at least one doubled letter and removing one of the
        // doubles produces the typed form. Checked BEFORE the generic missing-letter
        // shortcut because it is more specific.
        if (TryDoubleLetterCollapse(lowerC, lowerT))
            return new SpellingDiffResult(DoubleLetter, Math.Max(1, distance), DiffSegments(lowerC, lowerT), canonical, typed);

        // Single-edit shortcuts.
        if (distance == 1)
        {
            if (lenDelta == 1)
                return new SpellingDiffResult(MissingLetter, 1, DiffSegments(lowerC, lowerT), canonical, typed);
            if (lenDelta == -1)
                return new SpellingDiffResult(ExtraLetter, 1, DiffSegments(lowerC, lowerT), canonical, typed);
        }

        return new SpellingDiffResult(Unknown, distance, DiffSegments(lowerC, lowerT), canonical, typed);
    }

    private static string Normalise(string input)
        => (input ?? string.Empty).Normalize(NormalizationForm.FormC).Trim();

    /// <summary>Levenshtein distance, O(|a|*|b|) time and O(min(|a|,|b|)) space.</summary>
    public static int EditDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        // Ensure b is the shorter one for memory.
        if (a.Length < b.Length)
            (a, b) = (b, a);

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    private static bool HasAdjacentTransposition(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var swaps = 0;
        for (var i = 0; i < a.Length - 1; i++)
        {
            if (a[i] != b[i])
            {
                if (a[i] == b[i + 1] && a[i + 1] == b[i])
                {
                    swaps++;
                    i++; // skip the swapped pair
                }
                else
                {
                    return false;
                }
            }
        }
        return swaps == 1;
    }

    private static bool TryDoubleLetterCollapse(string canonical, string typed)
    {
        // For each doubled run in the canonical, removing one letter from that run
        // should produce the typed form (case-folded).
        for (var i = 0; i < canonical.Length - 1; i++)
        {
            if (canonical[i] == canonical[i + 1])
            {
                var collapsed = canonical.Remove(i, 1);
                if (string.Equals(collapsed, typed, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    private static List<DiffSegment> BuildEqualSegments(string s)
        => s.Length == 0 ? [] : [new DiffSegment("equal", s)];

    /// <summary>
    /// Produce a minimal diff segment list using the LCS table. Each segment is
    /// either <c>"equal"</c>, <c>"missing"</c> (in canonical, not typed), or
    /// <c>"extra"</c> (in typed, not canonical).
    /// </summary>
    public static List<DiffSegment> DiffSegments(string canonical, string typed)
    {
        var n = canonical.Length;
        var m = typed.Length;
        var dp = new int[n + 1, m + 1];
        for (var i = 1; i <= n; i++)
            for (var j = 1; j <= m; j++)
                dp[i, j] = canonical[i - 1] == typed[j - 1]
                    ? dp[i - 1, j - 1] + 1
                    : Math.Max(dp[i - 1, j], dp[i, j - 1]);

        var segments = new List<DiffSegment>();
        int x = n, y = m;
        while (x > 0 && y > 0)
        {
            if (canonical[x - 1] == typed[y - 1])
            {
                Prepend(segments, "equal", canonical[x - 1]);
                x--; y--;
            }
            else if (dp[x - 1, y] >= dp[x, y - 1])
            {
                Prepend(segments, "missing", canonical[x - 1]);
                x--;
            }
            else
            {
                Prepend(segments, "extra", typed[y - 1]);
                y--;
            }
        }
        while (x > 0) { Prepend(segments, "missing", canonical[x - 1]); x--; }
        while (y > 0) { Prepend(segments, "extra", typed[y - 1]); y--; }

        // Coalesce adjacent same-kind segments.
        var coalesced = new List<DiffSegment>();
        foreach (var seg in segments)
        {
            if (coalesced.Count > 0 && coalesced[^1].Kind == seg.Kind)
                coalesced[^1] = coalesced[^1] with { Text = coalesced[^1].Text + seg.Text };
            else
                coalesced.Add(seg);
        }
        return coalesced;
    }

    private static void Prepend(List<DiffSegment> segments, string kind, char ch)
        => segments.Insert(0, new DiffSegment(kind, ch.ToString()));
}

public record SpellingDiffResult(
    string Code,
    int Distance,
    List<DiffSegment> Segments,
    string Canonical,
    string Typed)
{
    public bool IsCorrect => Code is SpellingDiff.Correct or SpellingDiff.CaseOnly;
}

public record DiffSegment(string Kind, string Text);
