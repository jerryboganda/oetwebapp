using System.Text.RegularExpressions;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// The single sanitiser for every learner-facing Writing string (owner handoff, 6 Oct 2026: the
/// candidate sees only Severity, criterion name, the problematic wording, a Suggested fix and a
/// plain-English explanation; rule ids, validator labels and provider/model tags stay in backend
/// and admin records). Applied at the typed projection points (the v1.1 result projection, the
/// legacy grade mapper, the report builder) and NEVER to verbatim fields: the letter text,
/// <c>candidateWording</c>, quotes, snippets and the Model Answer are the candidate's own or the
/// approved words and must reach the page untouched.
///
/// Every regex runs under a timeout and FAILS CLOSED: a timeout returns the caller's fallback,
/// never the unscrubbed original (the Speaking precedent returns the original, which would leak).
/// Id families are case-sensitive and anchored, plus the closed registry sets of snake_case check
/// ids, so clinical text such as "G-6-PD", "COVID-19", "B-12" or "CT-12" is left alone.
/// </summary>
public static class WritingCandidateText
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private const RegexOptions Opts = RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private const RegexOptions OptsIgnoreCase = Opts | RegexOptions.IgnoreCase;

    // Every production Writing rule-id family: BUILTIN.<check>, the AI-grader tags (AI.<criterion>,
    // AI:<id>), G-W-#, DH-W-#, OWN-W-#, MED/NUR/DEN/PHA/PHY/RAD-W-#, OW-#, OA#-#, OA-#, canon SC-#,
    // three-part profession ids (PRD-OT-08), legacy R##.# and the LT-xx letter-type codes.
    // ponytail: a static family list (the production rulebooks hold exactly these). A new id family
    // needs one more alternative here; a dynamic set from the rulebook loader is the upgrade path.
    private const string IdToken =
        @"(?:BUILTIN\.[A-Za-z0-9_]+"
        + @"|AI[.:][A-Za-z][A-Za-z0-9_\-]*(?:\.[A-Za-z0-9_\-]+)*"
        + @"|(?:G|DH|OWN|[A-Z]{2,4})-W-\d{1,3}"
        + @"|OW-\d{1,3}"
        + @"|OA\d?-\d{1,3}"
        + @"|SC-\d{1,3}"
        + @"|(?!HLA-)[A-Z]{2,4}-[A-Z]{1,3}-\d{1,3}"
        + @"|R\d{1,2}\.\d{1,3}"
        + @"|LT-[A-Z]{2})";

    // Hyphens appear inside ids, so \b cannot delimit them: guard with explicit look-arounds.
    private const string IdGuarded = @"(?<![\w.\-])" + IdToken + @"(?![\w\-])";
    private const string IdList = IdGuarded + @"(?:\s*(?:[,;/&]|\band\b|\bor\b)\s*" + IdGuarded + ")*";

    // Words a grader message may append: "This affects Purpose and Content." / "Content criterion."
    private const string CriterionNames =
        "Purpose|Content|Conciseness(?: (?:and|&) Clarity)?|Clarity|Genre(?: (?:and|&) Style)?|Style|Organisation(?: (?:and|&) Layout)?|Layout|Language(?: Accuracy)?";

    private static readonly Regex Whitespace = new(@"\s+", Opts, RegexTimeout);
    private static readonly Regex ExemplarWord = new(@"\bexemplar(s)?\b", OptsIgnoreCase, RegexTimeout);

    // "(OW-005, DH-W-016)", "[see OA4-01; reason]", "[R12.1 · major]", "(R1)", "(R1-R5: reason)": a bracket
    // group that holds only ids or an R-label, optionally led by see/per/rule/ref and followed by free text.
    private static readonly Regex IdGroup = new(
        @"\s*[\(\[]\s*(?:(?i:see(?:\s+also)?|per|rules?|ref\.?|cf\.?)\s+)?(?:" + IdList
        + @"|R\d{1,2}(?:\s*[–\-]\s*R\d{1,2})?)"
        + @"(?:\s*[,;:·|\-–][^\)\]]*)?\s*[\)\]]",
        Opts, RegexTimeout);

    // A leading id or label: "G-W-117: ...", "Rule R08.14: ...", "R4 medication ...", "R1-R5: ...".
    private static readonly Regex LeadingId = new(
        @"^\s*(?:(?i:rules?)\s+)?(?:" + IdList + @"|R\d{1,2}(?:\.\d+)?(?:\s*[–\-]\s*R\d{1,2})?)"
        + @"\s*(?::|\.(?!\d)|\-|–|\s+(?=[a-z]))\s*",
        Opts, RegexTimeout);

    private static readonly Regex SeePerPhrase = new(
        @"\s*\b(?i:see(?:\s+also)?|per|ref\.?|cf\.?)\s+" + IdList, Opts, RegexTimeout);
    private static readonly Regex RuleRefPhrase = new(
        @"\b(?i:(?:the\s+)?rules?)\s+" + IdList, Opts, RegexTimeout);
    private static readonly Regex ViolatesPhrase = new(
        @"\b(?i:(violates?|violating|breach(?:es|ing)?|contrary\s+to|against|under))\s+" + IdList, Opts, RegexTimeout);
    private static readonly Regex BareIds = new(IdList, Opts, RegexTimeout);

    // Provider / model debug tags: the Max route, Claude, GPT, Codex, Jev, TypeSafe and the stored
    // model markers (deterministic-empty-vN, human_tutor).
    private static readonly Regex ProviderTags = new(
        @"\bwriting-claude-sub\b|\bclaude[\- ](?:opus|sonnet|haiku)[\w.\-]*|\bclaude-[\w.\-]+"
        + @"|\bgpt[\- ]?\d[\w.\-]*(?:\s+sol\b)?|\bgpt-[\w.\-]+|\bopenai\b|\banthropic\b|\bcodex\b|\bjev\w*|\btypesafe\b"
        + @"|\bdeterministic-empty-v\d+|\bhuman_tutor\b",
        OptsIgnoreCase, RegexTimeout);

    // Internal vocabulary: a sentence that still carries it after the id scrub is dropped whole.
    // "house rule" is deliberately NOT here: it is ordinary wording in the owner's own rules.
    private static readonly Regex InternalVocabulary = new(
        @"\bfirewall\b|\bvalidators?\b|\brules?[\- ]?engine\b|\brulebooks?\b|\bparser\b|\bdetectors?\b|\bdeterministic\b"
        + @"|\baddendum\s+\w+|\bowner\s+(?:clarification|directive)s?\b|\bsenior\s+assessor\s+audit\b"
        + @"|\bcross-model\s+audit\b|\bultimate\s+final\b|\bfact\s+map\b|\btask\s+snapshot\b|\bomission\s+map\b"
        + @"|\brule\s+pack\b|\bcanon\s+engine\b|\bprovenance\b|\bcalibration\s+set\b",
        OptsIgnoreCase, RegexTimeout);

    private static readonly Regex SentenceBreak = new(@"(?<=[.!?])\s+(?=[A-Z0-9“""'(])", Opts, RegexTimeout);
    private static readonly Regex AffectsSentence = new(
        $@"(?:(?<=[.!?])\s+|^\s*)(?:This affects (?=[^.]*\b(?:{CriterionNames})\b)[^.]*|(?:{CriterionNames}) criterion)\.(?=\s|$)",
        OptsIgnoreCase, RegexTimeout);
    private static readonly Regex LeadingLabelOnly = new(
        @"^\s*R\d{1,2}(?:\.\d+)?\s*(?::|\.|\-|–)", Opts, RegexTimeout);

    private static readonly Regex SpaceBeforePunctuation = new(@"\s+([,;:.!?])", Opts, RegexTimeout);
    private static readonly Regex EmptyBrackets = new(@"\(\s*\)|\[\s*\]", Opts, RegexTimeout);
    private static readonly Regex LeadingJunk = new(@"^[\s,;:.\-–]+", Opts, RegexTimeout);
    private static readonly Regex RepeatedComma = new(@",(?:\s*,)+", Opts, RegexTimeout);

    // Every snake_case check id the engine or the v1.1 adapter knows, longest first.
    private static readonly Regex CheckIds = BuildCheckIdRegex();

    private static Regex BuildCheckIdRegex()
    {
        var ids = WritingRuleEngine.SupportedCheckIds
            .Concat(WritingAssessmentV11RuleEngine.CheckIdCriteria.Keys)
            .Append("internal_detector_error")
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(x => x.Length)
            .Select(Regex.Escape);
        return new Regex(@"(?<![\w])(?:" + string.Join("|", ids) + @")(?![\w])", Opts, RegexTimeout);
    }

    private static readonly Dictionary<string, string> CriterionLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["purpose"] = "Purpose",
        ["content"] = "Content",
        ["conciseness_clarity"] = "Conciseness and Clarity",
        ["genre_style"] = "Genre and Style",
        ["organisation_layout"] = "Organisation and Layout",
        ["language"] = "Language",
    };

    /// <summary>
    /// Plain-English text for the candidate: ids, labels, provider tags and internal vocabulary removed.
    /// Returns <paramref name="fallback"/> when nothing meaningful is left, and when a regex times out.
    /// </summary>
    public static string Clean(string? text, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        try
        {
            var t = Whitespace.Replace(text, " ").Trim();
            var original = t;
            t = ExemplarWord.Replace(t, "model answer");
            t = IdGroup.Replace(t, string.Empty);
            t = LeadingId.Replace(t, string.Empty);
            t = SeePerPhrase.Replace(t, string.Empty);
            t = RuleRefPhrase.Replace(t, "the relevant guideline");
            t = ViolatesPhrase.Replace(t, "$1 the relevant guideline");
            t = BareIds.Replace(t, string.Empty);
            t = CheckIds.Replace(t, string.Empty);
            t = ProviderTags.Replace(t, string.Empty);
            t = DropInternalSentences(t);
            t = AffectsSentence.Replace(t, string.Empty);
            // Leading punctuation is only leftover junk when something was scrubbed away in front of it; a
            // suggested fix that legitimately starts with punctuation ("; however,") is kept as written.
            t = Tidy(t, stripLeadingPunctuation: !string.Equals(t, original, StringComparison.Ordinal));
            return t.Length == 0 || IsGraderPlaceholder(t) ? fallback : t;
        }
        catch (RegexMatchTimeoutException)
        {
            return fallback;
        }
    }

    // The text a grader finding carries when the model gave no message of its own ("AI grader finding.").
    // It names an internal process, so it is never shown: the caller's plain fallback stands in.
    private static bool IsGraderPlaceholder(string text)
        => string.Equals(
            text.TrimEnd('.', ' '),
            WritingReportDigest.PlaceholderMessage.TrimEnd('.'),
            StringComparison.OrdinalIgnoreCase);

    /// <summary><see cref="Clean"/>, or null when the input is empty or nothing is left of it.</summary>
    public static string? CleanOrNull(string? text)
    {
        var cleaned = Clean(text);
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>Each item cleaned; empty and repeated (case-insensitive) items are dropped.</summary>
    public static IReadOnlyList<string> CleanList(IEnumerable<string?>? items)
    {
        if (items is null) return [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var item in items)
        {
            var cleaned = Clean(item);
            if (cleaned.Length > 0 && seen.Add(cleaned)) result.Add(cleaned);
        }
        return result;
    }

    /// <summary>
    /// Detection only (guards, the secondary reviewer's pre-release check): true when the text still
    /// carries a rule id, label, check id, provider tag or internal vocabulary. A regex timeout counts
    /// as "yes" so a guard fails closed.
    /// </summary>
    public static bool ContainsInternalToken(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            return BareIds.IsMatch(text)
                || LeadingLabelOnly.IsMatch(text)
                || CheckIds.IsMatch(text)
                || ProviderTags.IsMatch(text)
                || InternalVocabulary.IsMatch(text)
                || AffectsSentence.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }

    /// <summary>
    /// The candidate severity word: critical | major | minor | advisory (lowercase on the wire; the
    /// UI capitalises). A stored <c>info</c> or any non-score-bearing finding is advisory, which
    /// carries no score effect.
    /// </summary>
    public static string PublicSeverity(string? severity, bool scoreBearing)
    {
        if (!scoreBearing) return "advisory";
        return severity?.Trim().ToLowerInvariant() switch
        {
            "critical" => "critical",
            "minor" => "minor",
            "info" or "advisory" => "advisory",
            _ => "major",
        };
    }

    /// <summary>The plain criterion name for a criterion code (purpose -> Purpose).</summary>
    public static string PublicCriterionLabel(string? criterionCode)
    {
        var code = (criterionCode ?? string.Empty).Trim();
        if (code.Length == 0) return "Language";
        return CriterionLabels.TryGetValue(code, out var label) ? label : PlainLabel(code);
    }

    /// <summary>"included_correctly" -> "Included correctly": a stored snake_case word shown as plain text.</summary>
    public static string PlainLabel(string? value)
    {
        var text = (value ?? string.Empty).Replace('_', ' ').Replace('-', ' ').Trim();
        if (text.Length == 0) return string.Empty;
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>The catalogue letter-type name (LT-RR -> Routine referral); never the LT-xx code.</summary>
    public static string PublicLetterTypeLabel(string? letterType)
        => WritingLetterTypeTaxonomy.NormalizeCatalogueLetterType(letterType) switch
        {
            WritingLetterTypeTaxonomy.RoutineReferral => "Routine referral",
            WritingLetterTypeTaxonomy.UrgentReferral => "Urgent referral",
            WritingLetterTypeTaxonomy.Discharge => "Discharge",
            WritingLetterTypeTaxonomy.Transfer => "Transfer",
            WritingLetterTypeTaxonomy.NonMedical => "Non-medical",
            _ => "Other letters",
        };

    private static string DropInternalSentences(string text)
    {
        if (!InternalVocabulary.IsMatch(text)) return text;
        var kept = SentenceBreak.Split(text).Where(sentence => !InternalVocabulary.IsMatch(sentence));
        return string.Join(" ", kept);
    }

    private static string Tidy(string text, bool stripLeadingPunctuation)
    {
        text = EmptyBrackets.Replace(text, string.Empty);
        text = RepeatedComma.Replace(text, ",");
        text = SpaceBeforePunctuation.Replace(text, "$1");
        text = Whitespace.Replace(text, " ");
        return (stripLeadingPunctuation ? LeadingJunk.Replace(text, string.Empty) : text).Trim();
    }
}
