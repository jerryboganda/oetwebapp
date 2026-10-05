using System.Text.RegularExpressions;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

/// <summary>One graded finding as the candidate report digest sees it.</summary>
public readonly record struct WritingDigestFinding(
    string? RuleId,
    string Severity,
    string? Message,
    string? Quote,
    string? Fix,
    string Criterion,
    int? Offset,
    bool ScoreBearing);

/// <summary>
/// Candidate-facing digest of a Writing grade: three genuinely different
/// priorities and a short per-criterion summary, built from the full finding
/// list. Pure post-processing (no prompt change), so it also repairs reports
/// stored before it existed when it runs at read time.
/// </summary>
public static class WritingReportDigest
{
    public const int PriorityMaxChars = 200;
    public const int SummaryMaxChars = 240;

    // Criterion names a grader message may append ("This affects Purpose and Content.").
    private const string CriterionNames =
        "Purpose|Content|Conciseness(?: (?:and|&) Clarity)?|Clarity|Genre(?: (?:and|&) Style)?|Style|Organisation(?: (?:and|&) Layout)?|Layout|Language(?: Accuracy)?";

    // "R4 medication ..." / "R1: ..." at the start; ids such as OWN-W-034, OA4-01, PRD-OT-08, R12.4.
    private const string RuleId = @"(?:[A-Z]{1,4}\d?(?:-[A-Z]{1,3})?-\d{1,3}|R\d{1,2}(?:\.\d+)?)";
    private static readonly Regex LeadingRuleLabel = new(@"^\s*R\d{1,2}(?:\.\d+)?(?:\s*[:.\-–]\s*|\s+(?=[a-z]))", RegexOptions.Compiled);
    private static readonly Regex RuleIdParenthetical = new(
        $@"\s*\((?:see(?: also)?\s+)?{RuleId}(?:\s*(?:[,;/&]|\b(?:and|or)\b)\s*{RuleId})*(?:\s*[,;:]\s*[^)]*)?\)", RegexOptions.Compiled);
    // A whole sentence naming the criteria a finding touches ("This affects Purpose and
    // Content." / "Content criterion."), wherever it sits in the message.
    private static readonly Regex AffectsSentence = new(
        $@"(?:(?<=[.!?])\s+|^\s*)(?:This affects (?=[^.]*\b(?:{CriterionNames})\b)[^.]*|(?:{CriterionNames}) criterion)\.(?=\s|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AffectsPurpose = new(@"This affects[^.]*\bPurpose\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ExemplarWord = new(@"\bexemplar(s)?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SentenceBreak = new(@"(?<=[.!?])\s+(?=[A-Z0-9“""'(])", RegexOptions.Compiled);
    private static readonly Regex Word = new(@"[a-z]{5,}", RegexOptions.Compiled);

    private static readonly HashSet<string> CommonWords = new(StringComparer.Ordinal)
    {
        "letter", "candidate", "should", "would", "which", "there", "their", "these", "those",
        "about", "where", "while", "other", "being", "without", "affects", "criterion", "notes",
    };

    /// <summary>A grader message without internal labels, rule ids or criterion tails.</summary>
    public static string Clean(string? message)
    {
        var text = Regex.Replace(message ?? string.Empty, @"\s+", " ").Trim();
        text = ExemplarWord.Replace(text, "model answer");
        text = LeadingRuleLabel.Replace(text, string.Empty);
        text = RuleIdParenthetical.Replace(text, string.Empty);
        return AffectsSentence.Replace(text, string.Empty).Trim();
    }

    /// <summary><see cref="Clean"/> for a full correction row: the cleaned text, or the original when nothing is left of it.</summary>
    public static string Tidy(string? text)
    {
        var cleaned = Clean(text);
        return cleaned.Length > 0 ? cleaned : text ?? string.Empty;
    }

    /// <summary>Whole sentences of <paramref name="message"/> up to <paramref name="maxChars"/>; the first sentence is word-clipped if it alone is too long.</summary>
    public static string Clip(string? message, int maxChars)
        => Pack(Sentences(Clean(message)), maxChars);

    /// <summary>
    /// The three most important, mutually distinct problems, as
    /// <c>"{ruleId}: {message}"</c> (the UI strips the label). Fewer than
    /// <paramref name="take"/> when fewer distinct problems exist.
    /// </summary>
    public static IReadOnlyList<string> ComposePriorities(IEnumerable<WritingDigestFinding> findings, int take = 3)
    {
        var chosen = new List<(WritingDigestFinding Finding, string Text, HashSet<string> Tokens, bool Purpose)>();
        foreach (var finding in Ordered(findings))
        {
            if (chosen.Count >= take) break;
            var text = Clip(finding.Message, PriorityMaxChars);
            if (text.Length == 0) continue;
            var tokens = Tokens(text);
            var purpose = InvolvesPurpose(finding);
            if (chosen.Any(c => IsSameProblem(c.Finding, c.Tokens, c.Purpose, finding, tokens, purpose))) continue;
            chosen.Add((finding, text, tokens, purpose));
        }
        return chosen
            .Select(c => string.IsNullOrWhiteSpace(c.Finding.RuleId) ? c.Text : $"{c.Finding.RuleId}: {c.Text}")
            .ToList();
    }

    /// <summary>
    /// One or two short sentences for a criterion card, taken from its most
    /// important findings (already in impact order). Null when there are none.
    /// </summary>
    public static string? CriterionSummary(IEnumerable<WritingDigestFinding> criterionFindings)
    {
        var sentences = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var messages = 0;
        foreach (var finding in criterionFindings)
        {
            var cleaned = Clean(finding.Message);
            if (cleaned.Length == 0 || !seen.Add(cleaned)) continue;
            sentences.AddRange(Sentences(cleaned).Take(2));
            if (++messages == 2) break;
        }
        var summary = Pack(sentences.Distinct(StringComparer.OrdinalIgnoreCase), SummaryMaxChars);
        return summary.Length == 0 ? null : summary;
    }

    /// <summary>Impact order: severity, then score-bearing before coaching-only, then position in the letter.</summary>
    public static IEnumerable<WritingDigestFinding> Ordered(IEnumerable<WritingDigestFinding> findings)
        => findings
            .Select((f, i) => (f, i))
            .OrderBy(x => SeverityRank(x.f.Severity))
            .ThenBy(x => x.f.ScoreBearing ? 0 : 1)
            .ThenBy(x => x.f.Offset ?? int.MaxValue)
            .ThenBy(x => x.i)
            .Select(x => x.f);

    /// <summary>Stored AI findings carry <c>AI.&lt;criterion&gt;</c> / <c>AI:&lt;rule id&gt;</c> ids; everything else resolves through the rule registry.</summary>
    public static bool IsScoreBearing(string? ruleSource)
    {
        var id = (ruleSource ?? string.Empty).Trim();
        if (id.StartsWith("AI.", StringComparison.OrdinalIgnoreCase)) return true;
        if (id.StartsWith("AI:", StringComparison.OrdinalIgnoreCase)) id = id[3..];
        return WritingRuleProvenance.For(WritingAssessmentV11RuleEngine.ResolveCheckId(id)).CandidateBehavior
            == WritingCandidateBehaviors.ScoreBearing;
    }

    private static int SeverityRank(string? severity) => severity?.Trim().ToLowerInvariant() switch
    {
        "critical" => 0,
        "major" => 1,
        "moderate" => 2,
        "minor" => 3,
        _ => 4,
    };

    private static bool InvolvesPurpose(WritingDigestFinding f)
        => string.Equals(f.Criterion, "purpose", StringComparison.OrdinalIgnoreCase)
            || (f.Message is not null && AffectsPurpose.IsMatch(f.Message));

    // At most one priority may be about Purpose; otherwise a repeat is the same
    // rule, the same wording in the letter, or an obvious paraphrase.
    private static bool IsSameProblem(
        WritingDigestFinding a, HashSet<string> aTokens, bool aPurpose,
        WritingDigestFinding b, HashSet<string> bTokens, bool bPurpose)
    {
        if (aPurpose && bPurpose) return true;
        if (!string.IsNullOrWhiteSpace(a.RuleId)
            && string.Equals(a.RuleId, b.RuleId, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrWhiteSpace(a.Quote) && !string.IsNullOrWhiteSpace(b.Quote)
            && (a.Quote!.Contains(b.Quote!, StringComparison.OrdinalIgnoreCase)
                || b.Quote!.Contains(a.Quote!, StringComparison.OrdinalIgnoreCase))) return true;
        var smaller = Math.Min(aTokens.Count, bTokens.Count);
        return smaller >= 4 && aTokens.Intersect(bTokens).Count() / (double)smaller >= 0.6;
    }

    private static HashSet<string> Tokens(string text)
        => Word.Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => !CommonWords.Contains(w))
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> Sentences(string text)
        => text.Length == 0 ? [] : SentenceBreak.Split(text).Select(s => s.Trim()).Where(s => s.Length > 0);

    private static string Pack(IEnumerable<string> sentences, int maxChars)
    {
        var result = string.Empty;
        foreach (var sentence in sentences)
        {
            var next = result.Length == 0 ? sentence : $"{result} {sentence}";
            if (next.Length <= maxChars) { result = next; continue; }
            return result.Length > 0 ? result : WordClip(sentence, maxChars);
        }
        return result;
    }

    private static string WordClip(string text, int maxChars)
    {
        var cut = text[..Math.Max(0, maxChars - 1)];
        var space = cut.LastIndexOf(' ');
        if (space > maxChars / 2) cut = cut[..space];
        return cut.TrimEnd(' ', ',', ';', ':', '-') + "…";
    }
}
