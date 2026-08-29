using System.Text;
using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Part B / Part C — source-text question recovery.
//
// The candidate-facing stem for a Part B/C item is printed on the paper's own
// question-paper PDF, whose extracted text is cached on
// ContentPaper.ExtractedTextJson by ContentTextExtractionService. A stem that
// was lost or overwritten can therefore be re-derived from the paper's own
// source instead of being re-typed or invented.
//
// The published Atlas/Nova catalogue uses FOUR different printed layouts, all
// verified against the real production question papers:
//
//   1. Canonical      "25. You hear ...  A first  B second  C third"
//   2. SAMPLE-watermark interleave — the watermark splits the page so the
//      extractor emits item N's context line, then a bare "N+1.", then N's
//      question + N's options followed by N+1's question + N+1's options.
//   3. Nova bullet    " 25 You hear ...  o AIt is a routine ...  o BIt is ..."
//      (no full stop after the number; the option letter is glued to its text)
//   4. Kaplan paren   "25. You hear ...  (A) be transferred ...  (B) receive ..."
//   plus papers that restart numbering per section (Part B 1-6, Part C 1-6 per
//   extract) rather than printing the canonical 25-42.
//
// Layouts 3-5 are normalised into layout 1 up front, so one parser handles the
// whole catalogue. Layout 2 gets a dedicated decoder.
//
// The parser is deliberately PRECISION-FIRST and FAILS CLOSED: a
// plausible-looking but mis-attributed stem is far worse for a candidate than a
// reported gap, so anything ambiguous is reported as unrecoverable for an
// operator to enter by hand.
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
    /// <summary>Two or more option sets in one slice that the interleave decoder
    /// could not resolve — attribution would be a guess.</summary>
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

    private const int MinStemLength = 8;
    private const int MaxStemLength = 600;
    private const int MinOptionLength = 2;
    private const int MaxOptionLength = 400;

    private static readonly RegexOptions Std = RegexOptions.Compiled | RegexOptions.CultureInvariant;
    private static readonly RegexOptions Multi = Std | RegexOptions.Multiline;
    private static readonly RegexOptions MultiI = Multi | RegexOptions.IgnoreCase;

    // ── Page furniture ───────────────────────────────────────────────────────
    private static readonly Regex[] NoiseLinePatterns =
    [
        new(@"^\s*\[CANDIDATE\s+NO\.?\].*$", MultiI),
        new(@"^\s*LISTENING\s+(SUB-TEST\s+)?QUESTION\s+PAPER\s+\d+\s*/\s*\d+\s*$", MultiI),
        new(@"^\s*[-=]{2,}\s*PAGE\s*\d+\s*[-=]{2,}\s*$", MultiI),
        new(@"^\s*PAGE\s*\d+\s*$", MultiI),
        new(@"^\s*Practice\s+Test\s*\d+\s*:?\s*$", MultiI),
        new(@"^\s*BLANK\s*$", MultiI),
        new(@"^\s*www\.[^\s]+\s*$", MultiI),
        new(@"^\s*©?\s*Cambridge\s+Boxhill\s+Language\s+Assessment.*$", MultiI),
    ];

    private static readonly Regex[] NoisePhrasePatterns =
    [
        new(@"(?<![A-Za-z])SAMPLE(?![A-Za-z])", Std),
        new(@"Fill\s+the\s+circle\s+in\s+completely\.?\s*Example\s*:?\s*[ABC]?", Std | RegexOptions.IgnoreCase),
        new(@"You\s+now\s+have\s+[\w\-]+(\s+\w+)?\s+seconds?\s+to\s+(read|look\s+at)[^.]*\.", Std | RegexOptions.IgnoreCase),
        new(@"You\s+now\s+have\s+two\s+minutes\s+to[^.]*\.", Std | RegexOptions.IgnoreCase),
        new(@"Now\s+look\s+at\s+(question\s+\d+|extract\s+\w+|Part\s+[ABC])\s*\.", Std | RegexOptions.IgnoreCase),
        new(@"That\s+is\s+the\s+end\s+of\s+Part\s+[ABC]\s*\.", Std | RegexOptions.IgnoreCase),
        new(@"Extract\s+\d+\s*:\s*Questions\s+\d+\s*[-–]\s*\d+", Std | RegexOptions.IgnoreCase),
        new(@"For\s+questions\s+\d+\s*(?:[-–]|to)\s*\d+\s*,\s*choose\s+the\s+answer[^.]*\.", Std | RegexOptions.IgnoreCase),
        new(@"Complete\s+your\s+answers\s+as\s+you\s+listen\s*\.", Std | RegexOptions.IgnoreCase),
    ];

    // ── Layout normalisation ─────────────────────────────────────────────────
    /// <summary>Kaplan papers parenthesise the marker: "(A) be transferred...".</summary>
    private static readonly Regex ParenMarkerPattern = new(@"(^|[\s>\]])\(\s*([ABC])\s*\)\s*", Multi);

    /// <summary>Nova papers glue a Word bullet to the letter and run straight into
    /// the text: "o AIt is a routine announcement...". The uppercase+lowercase
    /// lookahead leaves ordinary prose ("result from A interruptions") alone.</summary>
    private static readonly Regex BulletMarkerPattern = new(@"(^|[\s>\]])[o○◦]?\s*([ABC])(?=[A-Z][a-z])", Multi);

    /// <summary>Nova also prints the number with no full stop: " 25 You hear...".</summary>
    private static readonly Regex BareNumberPattern = new(@"(^|[\s>\]])(\d{1,2})\s+(?=[A-Z])", Multi);

    private static readonly Regex PartBHeadPattern = new(@"Part\s*B\b", Std | RegexOptions.IgnoreCase);
    private static readonly Regex PartCHeadPattern = new(@"Part\s*C\b", Std | RegexOptions.IgnoreCase);
    private static readonly Regex ExtractHeadPattern = new(@"Extract\s*(?:1|2|one|two)\b", Std | RegexOptions.IgnoreCase);
    private static readonly Regex SectionRelativeAnchorPattern = new(@"(^|[\s>\]])([1-9]|1[0-2])\s*[.)]\s+(?=\S)", Multi);

    // ── Structure ────────────────────────────────────────────────────────────
    private static readonly Regex QuestionAnchorPattern = new(@"(?<=^|[\s>\]])(?<number>\d{1,2})\s*[.)]\s+", Multi);
    private static readonly Regex OptionMarkerPattern = new(@"(?<=^|[\s>\]])(?<key>[ABC])\s*[.)]?\s+(?=\S)", Multi);
    private static readonly Regex MarkerA = new(@"(?<=^|[\s>\]])A\s*[.)]?\s+(?=\S)", Multi);
    private static readonly Regex MarkerB = new(@"(?<=^|[\s>\]])B\s*[.)]?\s+(?=\S)", Multi);
    private static readonly Regex MarkerC = new(@"(?<=^|[\s>\]])C\s*[.)]?\s+(?=\S)", Multi);
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
        var slices = BuildSlices(text);

        var found = new Dictionary<int, ListeningPartBCSourceItem>();
        var failure = new Dictionary<int, ListeningPartBCSourceSkip>();

        // Pass 1 — canonical layout: exactly one option set inside the slice.
        foreach (var (number, slice) in slices)
        {
            if (found.ContainsKey(number)) continue;
            var outcome = ParsePlainSlice(number, slice);
            if (outcome.Item is not null) found[number] = outcome.Item;
            else if (outcome.Skip is not null && !failure.ContainsKey(number)) failure[number] = outcome.Skip;
        }

        // Pass 2 — the SAMPLE-watermark interleave.
        for (var i = 0; i < slices.Count - 1; i++)
        {
            var (low, lowSlice) = slices[i];
            var (high, highSlice) = slices[i + 1];
            if (high != low + 1) continue;
            if (found.ContainsKey(low) && found.ContainsKey(high)) continue;

            var pair = TryDecodeInterleavedPair(low, lowSlice, high, highSlice);
            if (pair is null) continue;

            found.TryAdd(low, pair.Value.Low);
            found.TryAdd(high, pair.Value.High);
            failure.Remove(low);
            failure.Remove(high);
        }

        var items = new List<ListeningPartBCSourceItem>();
        var skipped = new List<ListeningPartBCSourceSkip>();
        foreach (var number in wanted.OrderBy(n => n))
        {
            if (found.TryGetValue(number, out var item)) { items.Add(item); continue; }
            skipped.Add(failure.TryGetValue(number, out var skip)
                ? skip
                : new(number, ListeningPartBCSourceSkipReason.NoQuestionAnchor,
                    $"No printed \"{number}.\" item was found in the question-paper text."));
        }

        return new ListeningPartBCSourceParseResult(items, skipped);
    }

    private static List<(int Number, string Slice)> BuildSlices(string text)
    {
        var anchors = QuestionAnchorPattern.Matches(text)
            .Select(match => (
                Number: int.TryParse(match.Groups["number"].Value, out var n) ? n : -1,
                Start: match.Index,
                End: match.Index + match.Length))
            .Where(a => a.Number is >= FirstNumber and <= LastNumber)
            .OrderBy(a => a.Start)
            .ToList();

        var slices = new List<(int, string)>(anchors.Count);
        for (var i = 0; i < anchors.Count; i++)
        {
            var end = i + 1 < anchors.Count ? anchors[i + 1].Start : text.Length;
            if (end > anchors[i].End) slices.Add((anchors[i].Number, text[anchors[i].End..end]));
        }
        return slices;
    }

    private readonly record struct Marker(string Key, int Start, int End);

    private static (List<Marker> Marks, List<(int A, int B, int C)> Runs) OptionRuns(string slice)
    {
        var marks = OptionMarkerPattern.Matches(slice)
            .Select(m => new Marker(m.Groups["key"].Value, m.Index, m.Index + m.Length))
            .ToList();

        var runs = new List<(int, int, int)>();
        for (var i = 0; i < marks.Count; i++)
        {
            if (marks[i].Key != "A") continue;
            int b = -1, c = -1;
            for (var j = i + 1; j < marks.Count; j++)
            {
                if (b < 0 && marks[j].Key == "B") { b = j; continue; }
                if (b < 0 && marks[j].Key == "A") break;
                if (b >= 0 && marks[j].Key == "C") { c = j; break; }
            }
            if (b >= 0 && c >= 0) { runs.Add((i, b, c)); i = c; }
        }
        return (marks, runs);
    }

    private static (ListeningPartBCSourceItem? Item, ListeningPartBCSourceSkip? Skip) ParsePlainSlice(int number, string slice)
    {
        var (marks, runs) = OptionRuns(slice);
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

        var (a, b, c) = runs[0];
        var stem = Collapse(slice[..marks[a].Start]);
        var optionA = Collapse(slice[marks[a].End..marks[b].Start]);
        var optionB = Collapse(slice[marks[b].End..marks[c].Start]);
        var optionC = OptionCText(slice, marks[c].End);

        return Validate(number, stem, optionA, optionB, optionC);
    }

    /// <summary>
    /// The SAMPLE watermark splits the page so the extractor emits item N's
    /// context line, a bare "N+1.", then N's question + N's options followed by
    /// N+1's question + N+1's options. The first option set therefore belongs to
    /// the LOWER number — its stem runs into the block — and the second to the
    /// higher one. Requires the low item's option C to end at a line break, which
    /// is where the high item's stem starts; without that the layout is something
    /// else and is left alone.
    /// </summary>
    private static (ListeningPartBCSourceItem Low, ListeningPartBCSourceItem High)? TryDecodeInterleavedPair(
        int lowNumber, string lowSlice, int highNumber, string highSlice)
    {
        var (_, lowRuns) = OptionRuns(lowSlice);
        if (lowRuns.Count != 0) return null;

        var (marks, highRuns) = OptionRuns(highSlice);
        if (highRuns.Count != 2) return null;

        var (a1, b1, c1) = highRuns[0];
        var (a2, b2, c2) = highRuns[1];

        var lineEnd = highSlice.IndexOf('\n', marks[c1].End);
        if (lineEnd < 0 || lineEnd > marks[a2].Start) return null;

        var lowStem = Collapse($"{Collapse(lowSlice)} {Collapse(highSlice[..marks[a1].Start])}");
        var low = Validate(
            lowNumber,
            lowStem,
            Collapse(highSlice[marks[a1].End..marks[b1].Start]),
            Collapse(highSlice[marks[b1].End..marks[c1].Start]),
            Collapse(highSlice[marks[c1].End..lineEnd]));
        if (low.Item is null) return null;

        var high = Validate(
            highNumber,
            Collapse(highSlice[lineEnd..marks[a2].Start]),
            Collapse(highSlice[marks[a2].End..marks[b2].Start]),
            Collapse(highSlice[marks[b2].End..marks[c2].Start]),
            OptionCText(highSlice, marks[c2].End));
        if (high.Item is null) return null;

        return (low.Item, high.Item);
    }

    /// <summary>
    /// Option C runs to the end of its printed line. Options are either printed
    /// one per line or run together on one line; in both layouts C is the tail of
    /// its own line, so this bounds it without swallowing the next block.
    /// </summary>
    private static string OptionCText(string slice, int start)
    {
        var lineEnd = slice.IndexOf('\n', start);
        return Collapse(lineEnd < 0 ? slice[start..] : slice[start..lineEnd]);
    }

    private static (ListeningPartBCSourceItem? Item, ListeningPartBCSourceSkip? Skip) Validate(
        int number, string stem, string optionA, string optionB, string optionC)
    {
        foreach (var (key, value) in new[] { ("A", optionA), ("B", optionB), ("C", optionC) })
        {
            if (value.Length < MinOptionLength || value.Length > MaxOptionLength)
            {
                return (null, new(number, ListeningPartBCSourceSkipReason.OptionTextRejected,
                    $"Q{number}: option {key} recovered as {value.Length} characters, outside the accepted {MinOptionLength}-{MaxOptionLength} range."));
            }
        }

        // A standalone letter only signals a bad split when it is a marker we
        // still expect to find further on. Nothing follows option C, so real
        // prose like "C A social worker will come..." is fine; and a stem
        // legitimately opens "A patient called Marisol...", so a leading A there
        // is not evidence of anything.
        if (MarkerB.IsMatch(stem) || MarkerC.IsMatch(stem)
            || MarkerB.IsMatch(optionA) || MarkerC.IsMatch(optionA)
            || MarkerB.IsMatch(optionB) || MarkerC.IsMatch(optionB))
        {
            return (null, new(number, ListeningPartBCSourceSkipReason.AmbiguousInterleavedText,
                $"Q{number}: a later option marker is still embedded in the recovered text, so the boundaries in the extracted text are ambiguous. Enter this item from the source paper."));
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
        text = text.Replace('', ' ').Replace('•', ' ');
        foreach (var pattern in NoiseLinePatterns) text = pattern.Replace(text, string.Empty);
        foreach (var pattern in NoisePhrasePatterns) text = pattern.Replace(text, " ");

        text = ParenMarkerPattern.Replace(text, m => $"{m.Groups[1].Value}{m.Groups[2].Value} ");
        text = BulletMarkerPattern.Replace(text, m => $"{m.Groups[1].Value}{m.Groups[2].Value} ");
        text = BareNumberPattern.Replace(text, m =>
            int.TryParse(m.Groups[2].Value, out var n) && n is >= FirstNumber and <= LastNumber
                ? $"{m.Groups[1].Value}{n}. "
                : m.Value);

        return RenumberSectionRelative(text);
    }

    /// <summary>
    /// Some papers restart numbering per section — Part B 1-6, then Part C 1-6
    /// per extract — instead of printing the canonical 25-42. Map those onto the
    /// numbers the platform stores (B=25-30, C1=31-36, C2=37-42). Only runs when
    /// the text does NOT already carry canonical numbering, so an ordinary paper
    /// is never touched.
    /// </summary>
    private static string RenumberSectionRelative(string text)
    {
        var canonical = QuestionAnchorPattern.Matches(text)
            .Count(m => int.TryParse(m.Groups["number"].Value, out var n) && n is >= FirstNumber and <= LastNumber);
        if (canonical >= 6) return text;

        var partB = PartBHeadPattern.Match(text);
        if (!partB.Success) return text;
        var partC = PartCHeadPattern.Match(text, partB.Index + partB.Length);
        if (!partC.Success) return text;

        var extracts = ExtractHeadPattern.Matches(text[(partC.Index + partC.Length)..])
            .Select(m => m.Index + partC.Index + partC.Length)
            .ToList();
        int? secondExtractStart = extracts.Count >= 2 ? extracts[1] : null;

        return SectionRelativeAnchorPattern.Replace(text, match =>
        {
            if (!int.TryParse(match.Groups[2].Value, out var n) || n is < 1 or > 6) return match.Value;
            if (match.Index < partB.Index + partB.Length) return match.Value;
            var offset = match.Index < partC.Index
                ? 24
                : secondExtractStart is null || match.Index < secondExtractStart ? 30 : 36;
            return $"{match.Groups[1].Value}{n + offset}. ";
        });
    }

    private static string Collapse(string raw)
    {
        var text = WhitespacePattern.Replace(raw, " ").Trim();
        text = text.Trim(' ', '\t', '·', '-', '–', '—', ':', ';');
        return text.Trim();
    }

    /// <summary>
    /// Choose the cached asset text most likely to be the question paper, by
    /// actually PARSING each candidate and keeping whichever yields the most
    /// items. Scoring on anchors alone let an audio-script transcript — which
    /// also numbers its extracts — outrank the real question paper.
    /// </summary>
    public static string? SelectQuestionPaperText(
        IEnumerable<string?> candidateTexts,
        IReadOnlyCollection<int>? wantedNumbers = null)
    {
        string? best = null;
        var bestScore = 0;
        foreach (var candidate in candidateTexts)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var score = Parse(candidate, wantedNumbers).Items.Count;
            if (score > bestScore) { bestScore = score; best = candidate; }
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
