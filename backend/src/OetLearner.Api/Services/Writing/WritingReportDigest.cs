using System.Text.RegularExpressions;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// One graded finding as the candidate report digest sees it. <paramref name="Category"/> is the
/// stored finding category (for example <c>punctuation</c>); it only informs the priority impact tier.
/// </summary>
public readonly record struct WritingDigestFinding(
    string? RuleId,
    string Severity,
    string? Message,
    string? Quote,
    string? Fix,
    string Criterion,
    int? Offset,
    bool ScoreBearing,
    string? Category = null);

/// <summary>
/// A chosen Top Priority: the clean candidate text plus the identifiers that stay in backend and admin
/// records (never sent to a candidate).
/// </summary>
public sealed record WritingPriority(string Text, string? RuleId, string Criterion, string Severity);

/// <summary>
/// Candidate-facing digest of a Writing grade: up to three genuinely different priorities and a short
/// per-criterion summary, built from the full finding list. Pure post-processing (no prompt change), so
/// it also repairs reports stored before it existed when it runs at read time.
///
/// Top Priorities (owner handoff, 6 Oct 2026, section 2.4): only score-bearing, non-advisory findings
/// take a slot; they are ordered by severity and then by impact (clinical safety, task fulfilment,
/// communication, polish) rather than by whichever validator rule fired first; repeats of one problem
/// are collapsed; fewer than three (even none) is returned when fewer meaningful distinct problems
/// exist. The text is clean and label-free: rule ids never leave the server.
/// </summary>
public static class WritingReportDigest
{
    public const int PriorityMaxChars = 200;
    public const int SummaryMaxChars = 240;

    /// <summary>The text a grader finding carries when the model gave no message; never a candidate priority.</summary>
    public const string PlaceholderMessage = "AI grader finding.";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private const RegexOptions Opts = RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly Regex AffectsPurpose = new(@"This affects[^.]*\bPurpose\b", Opts | RegexOptions.IgnoreCase, RegexTimeout);
    private static readonly Regex SentenceBreak = new(@"(?<=[.!?])\s+(?=[A-Z0-9“""'(])", Opts, RegexTimeout);
    private static readonly Regex Word = new(@"[a-z]{4,}", Opts, RegexTimeout);
    private static readonly Regex AlphaNumerics = new(@"[^a-z0-9]", Opts, RegexTimeout);

    // A legacy stored priority starts with "<rule id>: ". Only label SHAPES (an id with a digit, hyphen or
    // underscore) are stripped, so a plain lead-in such as "Purpose: state it first." survives.
    private static readonly Regex StoredRuleLabel = new(
        @"^\s*(?:AI(?:[.:][\w.\-]*)?|BUILTIN\.[\w.]+|[A-Z]{1,4}\d?(?:-[A-Z]{1,4})*-\d{1,3}|R\d{1,2}(?:\.\d+)?|[a-z][a-z0-9]*(?:_[a-z0-9]+)+):\s+",
        Opts, RegexTimeout);

    // ponytail: keyword heuristic for "clinical safety". Ceiling: a finding about an omitted allergy that
    // the grader words without any of these cues is ranked as task fulfilment. Upgrade path: the grader
    // stamps a category on each finding.
    private static readonly Regex SafetyCue = new(
        @"\b(?:allerg\w*|dose|dosage|contraindicat\w*|anticoag\w*|interaction|invented|fabricat\w*|unsupported|contradict\w*|unsafe|suspected|confirmed)\b",
        Opts | RegexOptions.IgnoreCase, RegexTimeout);

    private static readonly HashSet<string> CommonWords = new(StringComparer.Ordinal)
    {
        "letter", "candidate", "should", "would", "which", "there", "their", "these", "those",
        "about", "where", "while", "other", "being", "without", "affects", "criterion", "notes",
        "missing", "omitted", "include", "included", "includes", "mention", "mentioned", "information",
        "details", "detail", "relevant", "needs", "need", "with", "that", "this", "from", "have", "been",
        "were", "when", "also", "into", "more", "than", "such", "some", "only", "very",
    };

    // Findings that are the same problem class for a reader. ponytail: a hand-written table of the
    // engine's sibling check ids. Ceiling: two different tense errors in one letter collapse into one
    // priority (the corrections list still shows every one); upgrade path is a per-finding problem type.
    private static readonly Dictionary<string, string> CheckFamily = BuildCheckFamily();

    // Message classes for findings that carry no registry check (rule-less or unregistered AI findings),
    // tried in order. Only used for the language-side criteria, never for purpose or content omissions.
    private static readonly Regex[] MessageClassPatterns =
    {
        new(@"\b(?:spell\w*|misspel\w*|typo\w*)\b", Opts | RegexOptions.IgnoreCase, RegexTimeout),
        new(@"\b(?:tense|past simple|present perfect|past perfect|present simple)\b", Opts | RegexOptions.IgnoreCase, RegexTimeout),
        new(@"\b(?:comma|full stop|semicolon|colon|apostrophe|punctuat\w*|capital letter|capitalis\w*)\b", Opts | RegexOptions.IgnoreCase, RegexTimeout),
        new(@"\b(?:article|preposition)s?\b", Opts | RegexOptions.IgnoreCase, RegexTimeout),
        new(@"\b(?:agreement|singular|plural)\b", Opts | RegexOptions.IgnoreCase, RegexTimeout),
        new(@"\b(?:contraction\w*|colloquial|informal|register)\b", Opts | RegexOptions.IgnoreCase, RegexTimeout),
        new(@"\b(?:abbreviat\w*|jargon|acronym\w*)\b", Opts | RegexOptions.IgnoreCase, RegexTimeout),
        new(@"\b(?:unit|units|mmhg|mg|kg)\b", Opts | RegexOptions.IgnoreCase, RegexTimeout),
    };

    private static readonly string[] MessageClassNames =
    {
        "spelling", "tense", "punctuation", "articles", "agreement", "register", "abbreviation", "units",
    };

    private enum Impact
    {
        Safety = 0,
        TaskFulfilment = 1,
        Communication = 2,
        Polish = 3,
    }

    private static Dictionary<string, string> BuildCheckFamily()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string family, params string[] checkIds)
        {
            foreach (var id in checkIds) map[id] = family;
        }

        Add("linker", "linker_comma_and_case", "linker_however_punctuation", "linker_in_addition_punctuation", "linker_therefore_punctuation", "linker_avoid_words", "linker_density", "intro_adverbial_comma");
        Add("request", "no_duplicated_request", "closure_request_paragraph", "closure_mentions_patient_request_if_flagged", "request_action_unsupported", "canonical_contact_template", "closure_contact_offer", "closure_contains_management");
        Add("purpose", "intro_contains_purpose", "intro_opens_i_am_writing_to", "intro_purpose_vague", "urgent_intro_contains_urgent", "discharge_intro_template", "discharge_function_missed", "letter_type_function_mismatch", "cancer_suspected_flagged_urgent", "closure_mentions_review_if_required");
        Add("patient_naming", "body_forbidden_phrase_the_patient", "body_uses_last_name_only", "paragraph_start_patient_name", "relationship_label_patient_reference", "minor_naming_convention", "patient_title_mismatch");
        Add("re_line", "re_line_age_dob", "re_line_full_name", "re_line_dob_priority", "re_line_age_when_no_dob", "re_line_identity_unsupported", "dob_colon_format", "dob_age_forbidden_phrase", "age_not_duplicated_in_intro", "age_dob_inconsistent");
        Add("units", "numerical_values_have_units", "value_unit_spacing", "respiratory_rate_unit_style");
        Add("tense", "ago_requires_past_simple", "since_requires_present_perfect", "for_duration_requires_present_perfect", "surgery_past_simple", "visit_content_tense_basic_check", "discharge_admitted_with_past_simple", "treatment_change_grammar");
        Add("results", "result_at_wording", "result_head_noun", "result_noun_fragment", "results_comma_splice", "supine_position_wording", "dangling_treatment_modifier", "incomplete_clinical_construction");
        Add("sign_off", "signoff_designation_present", "signoff_no_invented_name", "yours_sincerely_capitalisation", "yours_sincerely_vs_faithfully", "role_salutation_matches_task", "salutation_last_name_only", "salutation_re_adjacent", "salutation_re_same_line");
        Add("spacing", "blank_line_after_re_line", "blank_line_between_paragraphs", "blank_before_closing_phrase", "date_blank_line_sandwich", "letter_paragraph_count", "min_body_paragraphs", "visit_paragraphization_check", "background_paragraph_placement", "letter_structure_order");
        Add("register", "emotional_wording", "judgmental_labels", "register_colloquial", "no_contractions", "no_asap_in_letter", "non_medical_no_jargon");
        Add("medication", "medication_list_punctuation", "medication_passive_grammar", "medication_frequency_conflict");
        return map;
    }

    /// <summary>A grader message without internal labels, rule ids or criterion tails.</summary>
    public static string Clean(string? message) => WritingCandidateText.Clean(message);

    /// <summary>
    /// <see cref="Clean"/> for a full correction row: the cleaned text, or <paramref name="fallback"/> when
    /// nothing is left of it. It never returns the original text, which may still carry a label or id.
    /// </summary>
    public static string Tidy(string? text, string fallback = "") => WritingCandidateText.Clean(text, fallback);

    /// <summary>Whole sentences of <paramref name="message"/> up to <paramref name="maxChars"/>; the first sentence is word-clipped if it alone is too long.</summary>
    public static string Clip(string? message, int maxChars)
        => Pack(Sentences(Clean(message)), maxChars);

    /// <summary>
    /// The most important, mutually distinct problems as clean text only (never a rule label). Fewer than
    /// <paramref name="take"/> when fewer distinct problems exist, down to none.
    /// </summary>
    public static IReadOnlyList<string> ComposePriorities(IEnumerable<WritingDigestFinding> findings, int take = 3)
        => ComposePriorityItems(findings, take).Select(p => p.Text).ToList();

    /// <summary>
    /// <see cref="ComposePriorities"/> with the identifiers kept alongside each text, for the admin-only
    /// audit record. Eligible findings are score-bearing and not advisory; they are ordered by severity,
    /// then impact tier, then Purpose first, then position in the letter.
    /// </summary>
    public static IReadOnlyList<WritingPriority> ComposePriorityItems(IEnumerable<WritingDigestFinding> findings, int take = 3)
    {
        if (take <= 0) return [];
        try
        {
            return ChoosePriorities(findings, take);
        }
        catch (RegexMatchTimeoutException)
        {
            // Fail closed: no priority is better than a mis-ranked or repeated one, and the read still succeeds.
            return [];
        }
    }

    private static IReadOnlyList<WritingPriority> ChoosePriorities(IEnumerable<WritingDigestFinding> findings, int take)
    {
        var eligible = findings
            .Select((f, i) => (Finding: f, Index: i))
            .Where(x => x.Finding.ScoreBearing && !IsAdvisorySeverity(x.Finding.Severity))
            .OrderBy(x => SeverityRank(x.Finding.Severity))
            .ThenBy(x => (int)ImpactOf(x.Finding))
            .ThenBy(x => string.Equals(x.Finding.Criterion, "purpose", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x.Finding.Offset ?? int.MaxValue)
            .ThenBy(x => x.Index)
            .Select(x => x.Finding);

        var chosen = new List<Candidate>();
        foreach (var finding in eligible)
        {
            if (chosen.Count >= take) break;
            var text = Capitalise(Clip(finding.Message, PriorityMaxChars));
            if (!IsMeaningful(text)) continue;
            var candidate = new Candidate(finding, text, Tokens(text), InvolvesPurpose(finding));
            if (chosen.Any(c => IsSameProblem(c, candidate))) continue;
            chosen.Add(candidate);
        }
        return chosen
            .Select(c => new WritingPriority(c.Text, c.Finding.RuleId, c.Finding.Criterion, c.Finding.Severity))
            .ToList();
    }

    /// <summary>
    /// Priorities stored by an earlier version (<c>"{rule id}: text"</c>, parentheticals, "This affects" tails,
    /// duplicates) made clean, distinct and at most <paramref name="take"/>. Used when only the stored list
    /// exists; nothing is added and nothing is recomputed.
    /// </summary>
    public static IReadOnlyList<string> CleanStoredPriorities(IEnumerable<string?> stored, int take = 3)
    {
        var kept = new List<(string Text, HashSet<string> Tokens)>();
        try
        {
            foreach (var raw in stored)
            {
                if (kept.Count >= take) break;
                var text = Capitalise(Clip(StripStoredLabel(raw), PriorityMaxChars));
                if (!IsMeaningful(text)) continue;
                var tokens = Tokens(text);
                if (kept.Any(k => SameNormalisedText(k.Text, text) || NearDuplicate(k.Tokens, tokens))) continue;
                kept.Add((text, tokens));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Fail closed: keep only what was already cleaned and distinct.
        }
        return kept.Select(k => k.Text).ToList();
    }

    /// <summary>
    /// One or two short sentences for a criterion card, taken from its most important findings (already
    /// in impact order). Advisory findings are skipped. Null when there are none.
    /// </summary>
    public static string? CriterionSummary(IEnumerable<WritingDigestFinding> criterionFindings)
    {
        var sentences = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var messages = 0;
        foreach (var finding in criterionFindings)
        {
            if (!finding.ScoreBearing || IsAdvisorySeverity(finding.Severity)) continue;
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

    /// <summary>
    /// Whether a stored finding may take a Top Priority slot or lower a score. An info/advisory severity is
    /// never score-bearing. Stored AI findings carry <c>AI.&lt;criterion&gt;</c> (rule-less, a detected
    /// mistake) or <c>AI:&lt;rule id&gt;</c>: a cited id missing from the check registry is a detected mistake
    /// too, so only a registry-known coaching-only check is advisory. Everything else resolves through the
    /// registry (see <see cref="WritingCandidateSeverityPolicy"/>).
    /// </summary>
    public static bool IsScoreBearing(string? ruleSource, string? severity = null)
    {
        if (IsAdvisorySeverity(severity)) return false;
        return WritingCandidateSeverityPolicy.IsScoreBearing(WritingCandidateSeverityPolicy.InputFor(ruleSource, severity));
    }

    private sealed record Candidate(WritingDigestFinding Finding, string Text, HashSet<string> Tokens, bool Purpose);

    private static bool IsAdvisorySeverity(string? severity)
    {
        var value = severity?.Trim();
        return string.Equals(value, "info", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "advisory", StringComparison.OrdinalIgnoreCase);
    }

    private static int SeverityRank(string? severity) => severity?.Trim().ToLowerInvariant() switch
    {
        "critical" => 0,
        "major" => 1,
        "moderate" => 2,
        "minor" => 3,
        _ => 4,
    };

    private static Impact ImpactOf(WritingDigestFinding f)
    {
        var rank = SeverityRank(f.Severity);
        var criterion = (f.Criterion ?? string.Empty).Trim().ToLowerInvariant();
        // The grader reserves Critical for invented, wrong or unsafe content.
        if (rank == 0) return Impact.Safety;
        if (criterion == "content" && (IsSourceFactCheck(f.RuleId) || SafetyCue.IsMatch(f.Message ?? string.Empty)))
            return Impact.Safety;
        if (criterion is "purpose" or "content") return Impact.TaskFulfilment;
        if (string.Equals(f.Category, "punctuation", StringComparison.OrdinalIgnoreCase)
            || (rank >= 3 && criterion is "language" or "organisation_layout"))
            return Impact.Polish;
        return Impact.Communication;
    }

    private static bool IsSourceFactCheck(string? ruleId)
        => WritingRuleProvenance.TryGet(WritingAssessmentV11RuleEngine.ResolveCheckId(ruleId), out var provenance)
            && provenance.Tag == WritingProvenanceTags.SourceFactTask;

    private static bool InvolvesPurpose(WritingDigestFinding f)
        => string.Equals(f.Criterion, "purpose", StringComparison.OrdinalIgnoreCase)
            || (f.Message is not null && AffectsPurpose.IsMatch(f.Message));

    // At most one priority may be about Purpose; otherwise a repeat is the same rule, the same family of
    // checks, the same wording in the letter, or an obvious paraphrase.
    private static bool IsSameProblem(Candidate a, Candidate b)
    {
        if (a.Purpose && b.Purpose) return true;
        var identityA = Identity(a.Finding.RuleId);
        if (identityA is not null && identityA == Identity(b.Finding.RuleId)) return true;
        var familyA = FamilyOf(a.Finding);
        if (familyA is not null && familyA == FamilyOf(b.Finding)) return true;
        if (SameWording(a.Finding.Quote, b.Finding.Quote)) return true;
        return NearDuplicate(a.Tokens, b.Tokens);
    }

    // The rule identity, or null for a rule-less finding ("AI.<criterion>" is the same pseudo id for every
    // rule-less finding of a criterion, so it says nothing about whether two problems match).
    private static string? Identity(string? ruleId)
    {
        var id = (ruleId ?? string.Empty).Trim();
        if (id.Length == 0 || id.StartsWith("AI.", StringComparison.OrdinalIgnoreCase)) return null;
        if (id.StartsWith("AI:", StringComparison.OrdinalIgnoreCase)) id = id["AI:".Length..];
        if (id.StartsWith("BUILTIN.", StringComparison.OrdinalIgnoreCase)) id = id["BUILTIN.".Length..];
        return id.ToUpperInvariant();
    }

    private static string? FamilyOf(WritingDigestFinding f)
    {
        var checkId = WritingAssessmentV11RuleEngine.ResolveCheckId(f.RuleId);
        if (checkId.Length > 0)
        {
            if (CheckFamily.TryGetValue(checkId, out var family)) return family;
            if (WritingRuleProvenance.TryGet(checkId, out _)) return "check:" + checkId.ToLowerInvariant();
        }
        var criterion = (f.Criterion ?? string.Empty).Trim().ToLowerInvariant();
        if (criterion is "purpose" or "content") return null;
        var message = f.Message ?? string.Empty;
        for (var i = 0; i < MessageClassPatterns.Length; i++)
        {
            if (MessageClassPatterns[i].IsMatch(message)) return "class:" + MessageClassNames[i];
        }
        return null;
    }

    private static bool SameWording(string? a, string? b)
    {
        var x = Normalised(a);
        var y = Normalised(b);
        return x.Length >= 4 && y.Length >= 4 && (x.Contains(y, StringComparison.Ordinal) || y.Contains(x, StringComparison.Ordinal));
    }

    private static string Normalised(string? text)
        => AlphaNumerics.Replace((text ?? string.Empty).ToLowerInvariant(), string.Empty);

    private static bool SameNormalisedText(string a, string b)
        => string.Equals(Normalised(a), Normalised(b), StringComparison.Ordinal);

    // A paraphrase shares most of its content words; at least three shared words, so two different
    // omissions that merely both say "missing ... information" are never merged.
    private static bool NearDuplicate(HashSet<string> a, HashSet<string> b)
    {
        var smaller = Math.Min(a.Count, b.Count);
        if (smaller < 3) return false;
        var shared = a.Intersect(b).Count();
        return shared >= 3 && shared / (double)smaller >= 0.6;
    }

    private static bool IsMeaningful(string text)
    {
        if (text.Length < 15) return false;
        if (text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 3) return false;
        return !string.Equals(text.TrimEnd('.', ' '), PlaceholderMessage.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
    }

    private static string StripStoredLabel(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        try
        {
            return StoredRuleLabel.Replace(text, string.Empty);
        }
        catch (RegexMatchTimeoutException)
        {
            return string.Empty;
        }
    }

    private static string Capitalise(string text)
        => text.Length > 0 && char.IsLower(text[0]) ? char.ToUpperInvariant(text[0]) + text[1..] : text;

    // Content words of a text: 4+ letters, no filler, lightly stemmed so "omitted" and "omission" line up.
    private static HashSet<string> Tokens(string text)
        => Word.Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => !CommonWords.Contains(w))
            .Select(Stem)
            .ToHashSet(StringComparer.Ordinal);

    private static string Stem(string word)
    {
        foreach (var suffix in new[] { "ing", "es", "ed", "ly", "s" })
        {
            if (word.Length > suffix.Length + 3 && word.EndsWith(suffix, StringComparison.Ordinal))
                return word[..^suffix.Length];
        }
        return word;
    }

    private static IEnumerable<string> Sentences(string text)
    {
        if (text.Length == 0) return [];
        try
        {
            return SentenceBreak.Split(text).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        }
        catch (RegexMatchTimeoutException)
        {
            return [text];
        }
    }

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
