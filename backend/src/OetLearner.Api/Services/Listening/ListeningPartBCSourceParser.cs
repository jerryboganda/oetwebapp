using System.Text;
using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Part B / Part C — source-text question recovery.
//
// The candidate-facing stem for a Part B/C item is printed on the paper's own
// question-paper PDF. That PDF's extracted text is already cached on
// ContentPaper.ExtractedTextJson (keyed by asset id) by
// ContentTextExtractionService, so a stem that was lost or overwritten can be
// re-derived from the paper's own source instead of being re-typed or invented.
//
// This parser is deliberately CONSERVATIVE and FAILS CLOSED. PDF text
// extraction routinely interleaves two questions when a watermark or a second
// column sits between them, and a plausible-looking but mis-attributed stem is
// far worse for a candidate than a reported gap. An item is only recovered when
// the slice for its printed number yields a stem plus exactly one A/B/C option
// set, in order. Anything ambiguous is reported as unrecoverable so an operator
// can enter it from the source paper by hand.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>One Part B/C item recovered verbatim from the question-paper text.</summary>
public sealed record ListeningPartBCSourceItem(
    int Number,
    string Stem,
    string OptionA,
    string OptionB,
    string OptionC)
{
    public IReadOnlyList<string> Options => [OptionA, OptionB, OptionC];
}

/// <summary>Why a printed number could not be recovered from the source text.</summary>
public enum ListeningPartBCSourceSkipReason
{
    /// <summary>No `25.`-style anchor for this number anywhere in the text.</summary>
    NoQuestionAnchor = 0,
    /// <summary>The slice held no complete, correctly ordered A/B/C option set.</summary>
    OptionSetIncomplete = 1,
    /// <summary>Two or more option sets in one slice — the source text interleaves
    /// this item with its neighbour, so attribution is ambiguous.</summary>
    AmbiguousInterleavedText = 2,
    /// <summary>An option's text was empty or implausibly long after cleaning.</summary>
    OptionTextRejected = 3,
    /// <summary>The stem was empty, too long, or is a sentinel/heading rather than
    /// a real question.</summary>
    StemRejected = 4,
}

public sealed record ListeningPartBCSourceSkip(int Number, ListeningPartBCSourceSkipReason Reason, string Detail);

public sealed record ListeningPartBCSourceParseResult(
    IReadOnlyList<ListeningPartBCSourceItem> Items,
    IReadOnlyList<ListeningPartBCSourceSkip> Skipped)
{
    public static ListeningPartBCSourceParseResult Empty { get; } = new([], []);
}

public static class ListeningPartBCSourceParser
{
    /// <summary>First printed number carrying an A/B/C multiple-choice item.</summary>
    public const int FirstNumber = 25;

    /// <summary>Last printed number on an OET Listening paper.</summary>
    public const int LastNumber = 42;

    // Stems on a real paper run from a short "What does X say?" to a two-sentence
    // context + question. Anything outside this band is a parse that swallowed
    // surrounding page furniture.
    private const int MinStemLength = 8;
    private const int MaxStemLength = 600;
    private const int MinOptionLength = 2;
    private const int MaxOptionLength = 400;

    private static readonly RegexOptions Std =
        RegexOptions.Compiled | RegexOptions.CultureInvariant;

    // ── Page furniture that appears between or inside items ──────────────────
    // Each pattern is anchored to a whole line or to an unambiguous phrase, so
    // stripping one can never remove candidate-facing question content.
    private static readonly Regex[] NoiseLinePatterns =
    [
        new(@"^\s*\[CANDIDATE\s+NO\.?\].*$", Std | RegexOptions.Multiline | RegexOptions.IgnoreCase),
        new(@"^\s*LISTENING\s+(SUB-TEST\s+)?QUESTION\s+PAPER\s+\d+\s*/\s*\d+\s*$", Std | RegexOptions.Multiline | RegexOptions.IgnoreCase),
        new(@"^\s*[-=]{2,}\s*PAGE\s*\d+\s*[-=]{2,}\s*$", Std | RegexOptions.Multiline | RegexOptions.IgnoreCase),
        new(@"^\s*PAGE\s*\d+\s*$", Std | RegexOptions.Multiline | RegexOptions.IgnoreCase),
        new(@"^\s*Practice\s+Test\s*\d+\s*:?\s*$", Std | RegexOptions.Multiline | RegexOptions.IgnoreCase),
        new(@"^\s*BLANK\s*$", Std | RegexOptions.Multiline | RegexOptions.IgnoreCase),
        new(@"^\s*www\.[^\s]+\s*$", Std | RegexOptions.Multiline | RegexOptions.IgnoreCase),
        new(@"^\s*©?\s*Cambridge\s+Boxhill\s+Language\s+Assessment.*$", Std | RegexOptions.Multiline | RegexOptions.IgnoreCase),
    ];

    private static readonly Regex[] NoisePhrasePatterns =
    [
        // Watermark stamped across the page; it lands mid-sentence in extracted text.
        new(@"(?<![A-Za-z])SAMPLE(?![A-Za-z])", Std),
        new(@"Fill\s+the\s+circle\s+in\s+completely\.?\s*Example\s*:?\s*[ABC]?", Std | RegexOptions.IgnoreCase),
        new(@"You\s+now\s+have\s+\w+(\s+\w+)?\s+seconds?\s+to\s+(read|look\s+at)[^.]*\.", Std | RegexOptions.IgnoreCase),
        new(@"Now\s+look\s+at\s+(question\s+\d+|extract\s+\w+|Part\s+[ABC])\s*\.", Std | RegexOptions.IgnoreCase),
        new(@"That\s+is\s+the\s+end\s+of\s+Part\s+[ABC]\s*\.", Std | RegexOptions.IgnoreCase),
        new(@"Extract\s+\d+\s*:\s*Questions\s+\d+\s*[-–]\s*\d+", Std | RegexOptions.IgnoreCase),
        new(@"For\s+questions\s+\d+\s*[-–]\s*\d+\s*,\s*choose\s+the\s+answer[^.]*\.", Std | RegexOptions.IgnoreCase),
        new(@"Complete\s+your\s+answers\s+as\s+you\s+listen\s*\.", Std | RegexOptions.IgnoreCase),
    ];

    // A printed question number: `31.` / `31)` / a bare `31` opening a line.
    // Matched anywhere so an item that follows the previous one on the same
    // extracted line is still found.
    private static readonly Regex QuestionAnchorPattern =
        new(@"(?<=^|[\s>\]])(?<number>\d{1,2})\s*[.)]\s+", Std | RegexOptions.Multiline);

    // An A/B/C option marker. Requires trailing text on the same run so a bare
    // answer-grid letter column ("A\nB\nC") is not mistaken for an option set.
    private static readonly Regex OptionMarkerPattern =
        new(@"(?<=^|[\s>\]])(?<key>[ABC])\s*[.)]?\s+(?=\S)", Std | RegexOptions.Multiline);

    private static readonly Regex WhitespacePattern = new(@"\s+", Std);

    /// <summary>
    /// Recover every Part B/C item the question-paper text unambiguously
    /// supports. Numbers outside <paramref name="wantedNumbers"/> are ignored;
    /// pass null for the full 25–42 range.
    /// </summary>
    public static ListeningPartBCSourceParseResult Parse(
        string? questionPaperText,
        IReadOnlyCollection<int>? wantedNumbers = null)
    {
        if (string.IsNullOrWhiteSpace(questionPaperText)) return ListeningPartBCSourceParseResult.Empty;

        var wanted = wantedNumbers is { Count: > 0 }
            ? wantedNumbers.Where(n => n is >= FirstNumber and <= LastNumber).ToHashSet()
            : Enumerable.Range(FirstNumber, LastNumber - FirstNumber + 1).ToHashSet();
        if (wanted.Count == 0) return ListeningPartBCSourceParseResult.Empty;

        var text = Denoise(questionPaperText);

        // Anchors for the Part B/C range only, in document order. A number that
        // is printed more than once (e.g. an answer grid repeating it) yields
        // several anchors; each slice is evaluated and the first that parses
        // cleanly wins, so a stray grid reference cannot mask the real item.
        var anchors = QuestionAnchorPattern.Matches(text)
            .Select(match => (
                Number: int.TryParse(match.Groups["number"].Value, out var n) ? n : -1,
                Start: match.Index,
                End: match.Index + match.Length))
            .Where(anchor => anchor.Number is >= FirstNumber and <= LastNumber)
            .OrderBy(anchor => anchor.Start)
            .ToList();

        var items = new List<ListeningPartBCSourceItem>();
        var skipped = new List<ListeningPartBCSourceSkip>();

        foreach (var number in wanted.OrderBy(n => n))
        {
            var candidates = anchors
                .Select((anchor, index) => (anchor, index))
                .Where(entry => entry.anchor.Number == number)
                .ToList();

            if (candidates.Count == 0)
            {
                skipped.Add(new(number, ListeningPartBCSourceSkipReason.NoQuestionAnchor,
                    $"No printed \"{number}.\" anchor was found in the question-paper text."));
                continue;
            }

            ListeningPartBCSourceSkip? lastFailure = null;
            ListeningPartBCSourceItem? recovered = null;

            foreach (var (anchor, index) in candidates)
            {
                // The slice ends at the next anchor for ANY Part B/C number, so a
                // neighbouring item's text can never be absorbed into this stem.
                var sliceEnd = index + 1 < anchors.Count ? anchors[index + 1].Start : text.Length;
                if (sliceEnd <= anchor.End) continue;

                var outcome = ParseSlice(number, text[anchor.End..sliceEnd]);
                if (outcome.Item is not null)
                {
                    recovered = outcome.Item;
                    break;
                }
                lastFailure = outcome.Skip;
            }

            if (recovered is not null) items.Add(recovered);
            else if (lastFailure is not null) skipped.Add(lastFailure);
            else
            {
                skipped.Add(new(number, ListeningPartBCSourceSkipReason.OptionSetIncomplete,
                    $"The text after \"{number}.\" was empty."));
            }
        }

        return new ListeningPartBCSourceParseResult(items, skipped);
    }

    private static (ListeningPartBCSourceItem? Item, ListeningPartBCSourceSkip? Skip) ParseSlice(int number, string slice)
    {
        var markers = OptionMarkerPattern.Matches(slice)
            .Select(match => (Key: match.Groups["key"].Value, Start: match.Index, End: match.Index + match.Length))
            .ToList();

        // Locate the FIRST correctly ordered A→B→C run. A second complete run in
        // the same slice means the extractor interleaved two printed items and
        // neither can be attributed safely.
        var runs = new List<(int A, int B, int C)>();
        for (var i = 0; i < markers.Count; i++)
        {
            if (markers[i].Key != "A") continue;
            var b = -1;
            var c = -1;
            for (var j = i + 1; j < markers.Count; j++)
            {
                if (b < 0 && markers[j].Key == "B") { b = j; continue; }
                if (b >= 0 && markers[j].Key == "C") { c = j; break; }
                // Another "A" before B closes this candidate run.
                if (b < 0 && markers[j].Key == "A") break;
            }
            if (b >= 0 && c >= 0)
            {
                runs.Add((i, b, c));
                i = c;
            }
        }

        if (runs.Count == 0)
        {
            return (null, new(number, ListeningPartBCSourceSkipReason.OptionSetIncomplete,
                $"Q{number}: the question-paper text after the number does not contain a complete A/B/C option set."));
        }

        if (runs.Count > 1)
        {
            return (null, new(number, ListeningPartBCSourceSkipReason.AmbiguousInterleavedText,
                $"Q{number}: the extracted text holds {runs.Count} option sets between this number and the next, so the printed stem cannot be attributed unambiguously. Enter this item from the source paper."));
        }

        var (aIndex, bIndex, cIndex) = runs[0];
        var stem = Collapse(slice[..markers[aIndex].Start]);
        var optionA = Collapse(slice[markers[aIndex].End..markers[bIndex].Start]);
        var optionB = Collapse(slice[markers[bIndex].End..markers[cIndex].Start]);
        var optionC = Collapse(slice[markers[cIndex].End..]);

        foreach (var (key, value) in new[] { ("A", optionA), ("B", optionB), ("C", optionC) })
        {
            if (value.Length < MinOptionLength || value.Length > MaxOptionLength)
            {
                return (null, new(number, ListeningPartBCSourceSkipReason.OptionTextRejected,
                    $"Q{number}: option {key} recovered as {value.Length} characters, outside the accepted {MinOptionLength}-{MaxOptionLength} range."));
            }

            // A standalone A/B/C left INSIDE a recovered option means the split
            // letter was part of the prose ("Hepatitis B vaccination", "vitamin
            // C levels") rather than an option marker, so the boundaries cannot
            // be trusted. Clinical option text makes this a real case, not a
            // theoretical one.
            if (OptionMarkerPattern.IsMatch(value))
            {
                return (null, new(number, ListeningPartBCSourceSkipReason.AmbiguousInterleavedText,
                    $"Q{number}: option {key} still contains a standalone A/B/C marker after splitting, so the option boundaries in the extracted text are ambiguous. Enter this item from the source paper."));
            }
        }

        // The same reasoning applies to the stem: a marker still inside it means
        // the run started at a letter that belonged to the printed question.
        if (OptionMarkerPattern.IsMatch(stem))
        {
            return (null, new(number, ListeningPartBCSourceSkipReason.AmbiguousInterleavedText,
                $"Q{number}: the recovered stem still contains a standalone A/B/C marker, so the boundary between the question and its options is ambiguous. Enter this item from the source paper."));
        }

        if (stem.Length < MinStemLength || stem.Length > MaxStemLength)
        {
            return (null, new(number, ListeningPartBCSourceSkipReason.StemRejected,
                $"Q{number}: the recovered stem is {stem.Length} characters, outside the accepted {MinStemLength}-{MaxStemLength} range."));
        }

        if (!ListeningLearnerService.IsUsablePartBCStem(stem))
        {
            return (null, new(number, ListeningPartBCSourceSkipReason.StemRejected,
                $"Q{number}: the recovered stem is a sentinel, a section heading, or a generic placeholder rather than the printed question."));
        }

        return (new ListeningPartBCSourceItem(number, stem, optionA, optionB, optionC), null);
    }

    private static string Denoise(string raw)
    {
        var text = raw.Replace("\r\n", "\n").Replace('\r', '\n');
        // Word bullet glyph and the private-use characters PDF extraction emits.
        text = text.Replace('', ' ').Replace('•', ' ');
        foreach (var pattern in NoiseLinePatterns) text = pattern.Replace(text, string.Empty);
        foreach (var pattern in NoisePhrasePatterns) text = pattern.Replace(text, " ");
        return text;
    }

    private static string Collapse(string raw)
    {
        var text = WhitespacePattern.Replace(raw, " ").Trim();
        // Curly punctuation is kept verbatim — it is the printed wording — but a
        // leading list glyph or stray separator left by extraction is not.
        text = text.Trim(' ', '\t', '·', '-', '–', '—', ':', ';');
        return text.Trim();
    }

    /// <summary>
    /// Pick the question-paper text most likely to hold Part B/C, from the
    /// per-asset entries cached on <c>ContentPaper.ExtractedTextJson</c>.
    /// Papers are usually ingested as ONE whole-booklet PDF, so the longest
    /// entry that actually contains Part B/C anchors wins.
    /// </summary>
    public static string? SelectQuestionPaperText(IEnumerable<string?> candidateTexts)
    {
        string? best = null;
        var bestScore = 0;
        foreach (var candidate in candidateTexts)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var denoised = Denoise(candidate);
            var score = QuestionAnchorPattern.Matches(denoised)
                .Select(match => int.TryParse(match.Groups["number"].Value, out var n) ? n : -1)
                .Count(n => n is >= FirstNumber and <= LastNumber);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }
        return bestScore > 0 ? best : null;
    }

    /// <summary>Human-readable one-line summary for admin reports and audit rows.</summary>
    public static string Describe(ListeningPartBCSourceParseResult result)
    {
        var builder = new StringBuilder();
        builder.Append(result.Items.Count).Append(" recovered");
        if (result.Skipped.Count > 0)
        {
            builder.Append(", ").Append(result.Skipped.Count).Append(" unrecoverable (");
            builder.Append(string.Join(", ", result.Skipped
                .GroupBy(skip => skip.Reason)
                .OrderBy(group => group.Key)
                .Select(group => $"{group.Key}: {group.Count()}")));
            builder.Append(')');
        }
        return builder.ToString();
    }
}
