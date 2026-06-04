using System.Text.Json;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Module Pathway — Phase 1
//
// Deterministic per-question grader for the new pathway flow
// (ListeningQuestionAttempt). This is SEPARATE from
// ListeningGradingService.cs which serves the V2 paper-level attempt path
// and must remain untouched (the sealed class is referenced by audit tests).
//
// Reference: OET_LISTENING_MODULE_PATHWAY.md §26.5 — grading is 100%
// deterministic, no AI calls. Part A gap-fill uses spelling tolerance via
// Levenshtein ≤ 1 against canonical / accepted variants. Part B/C MCQ uses
// exact key match. Aggregate scoring rolls up to L1..L8 sub-skill scores
// (0–10) and 4 accent bucket scores (0–100).
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningLearnerGradingService
{
    /// <summary>Grade a single ListeningQuestionAttempt against the question's
    /// canonical answer. Sets IsCorrect, IsSpellingCorrectMeaningWrong,
    /// IsMeaningCorrectSpellingWrong on the attempt entity. Does not persist.</summary>
    Task<GradingResult> GradeAttemptAsync(ListeningQuestionAttempt attempt,
        ListeningQuestion question, CancellationToken ct);

    /// <summary>Grade an entire diagnostic session — iterates all attempts,
    /// returns aggregate score + per-sub-skill + per-accent breakdowns.</summary>
    Task<DiagnosticGradingResult> GradeSessionAsync(
        IReadOnlyList<ListeningQuestionAttempt> attempts,
        IReadOnlyDictionary<string, ListeningQuestion> questionsById,
        CancellationToken ct);
}

public sealed record GradingResult(
    bool IsCorrect,
    bool IsSpellingCorrectMeaningWrong,
    bool IsMeaningCorrectSpellingWrong,
    string? CanonicalAnswer);

public sealed record DiagnosticGradingResult(
    int TotalQuestions,
    int CorrectCount,
    int UnknownCount,
    IReadOnlyDictionary<string, decimal> SkillScores0to10,    // L1..L8
    IReadOnlyDictionary<string, decimal> AccentScores0to100,  // british/australian/us/non_native
    IReadOnlyList<SpellingMissTuple> SpellingMisses);

public sealed record SpellingMissTuple(string QuestionId, string LearnerAnswer, string CorrectAnswer);

public sealed class ListeningLearnerGradingService : IListeningLearnerGradingService
{
    // Canonical L1..L8 sub-skill codes (LearnerListeningSkillScore.SkillCode).
    // Sub-skills not exercised in a session default to 5.0 (mid-range neutral
    // baseline) so downstream pathway generators don't extrapolate from zero.
    private static readonly string[] AllSkillCodes =
        { "L1", "L2", "L3", "L4", "L5", "L6", "L7", "L8" };

    private const decimal MissingSkillNeutralBaseline = 5.0m;

    // Levenshtein cap — protects against pathological inputs in the DP table.
    // Real Part A answers fit comfortably inside 64 chars; anything longer is
    // either junk or a paste error and is truncated before comparison.
    private const int MaxLevenshteinLength = 64;

    private readonly ILogger<ListeningLearnerGradingService> _logger;

    public ListeningLearnerGradingService(ILogger<ListeningLearnerGradingService> logger)
    {
        _logger = logger;
    }

    public Task<GradingResult> GradeAttemptAsync(
        ListeningQuestionAttempt attempt,
        ListeningQuestion question,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(question);

        // "I don't know" short-circuits — no spelling logic, no credit.
        // This matches §26.5: unknowns must not soak the spelling-miss
        // bucket because they signal comprehension failure, not orthography.
        if (attempt.IsUnknown)
        {
            attempt.IsCorrect = false;
            attempt.IsSpellingCorrectMeaningWrong = false;
            attempt.IsMeaningCorrectSpellingWrong = false;
            var canonicalForUnknown = TryReadString(question.CorrectAnswerJson);
            return Task.FromResult(new GradingResult(
                IsCorrect: false,
                IsSpellingCorrectMeaningWrong: false,
                IsMeaningCorrectSpellingWrong: false,
                CanonicalAnswer: canonicalForUnknown));
        }

        return question.QuestionType switch
        {
            ListeningQuestionType.MultipleChoice3 => Task.FromResult(GradeMcq(attempt, question)),
            // FillInBlank grades identically to ShortAnswer (spelling-tolerant
            // canonical + accepted-variants compare).
            ListeningQuestionType.ShortAnswer => Task.FromResult(GradeShortAnswer(attempt, question)),
            ListeningQuestionType.FillInBlank => Task.FromResult(GradeShortAnswer(attempt, question)),
            _ => Task.FromResult(GradeUnsupported(attempt, question)),
        };
    }

    public Task<DiagnosticGradingResult> GradeSessionAsync(
        IReadOnlyList<ListeningQuestionAttempt> attempts,
        IReadOnlyDictionary<string, ListeningQuestion> questionsById,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(questionsById);

        var totalQuestions = attempts.Count;
        var correctCount = 0;
        var unknownCount = 0;

        // Per-bucket tallies — accumulate (attempted, correct) and divide at
        // the end so we don't have to do floating-point reduction mid-loop.
        var skillTally = new Dictionary<string, (int Attempted, int Correct)>(StringComparer.OrdinalIgnoreCase);
        var accentTally = new Dictionary<string, (int Attempted, int Correct)>(StringComparer.OrdinalIgnoreCase);
        var spellingMisses = new List<SpellingMissTuple>();

        foreach (var attempt in attempts)
        {
            ct.ThrowIfCancellationRequested();
            if (attempt is null) continue;

            if (!questionsById.TryGetValue(attempt.ListeningQuestionId, out var question)
                || question is null)
            {
                _logger.LogWarning(
                    "Listening session grade skipped attempt {AttemptId}: question {QuestionId} not in lookup",
                    attempt.Id, attempt.ListeningQuestionId);
                continue;
            }

            if (attempt.IsUnknown)
            {
                unknownCount++;
            }
            else if (attempt.IsCorrect)
            {
                correctCount++;
            }

            // L1..L8 sub-skill rollup — a single question may carry multiple
            // sub-skill tags (CSV in SubSkillTagsCsv), and each tag picks up
            // the same correct/attempted count. This biases multi-tagged
            // items toward over-weighting, but the alternative (splitting
            // 1/n credit per tag) hides genuine multi-skill performance.
            foreach (var skill in ParseSubSkillCodes(question.SubSkillTagsCsv))
            {
                var current = skillTally.TryGetValue(skill, out var s) ? s : (0, 0);
                skillTally[skill] = (current.Item1 + 1, current.Item2 + (attempt.IsCorrect ? 1 : 0));
            }

            // Accent rollup — map BCP-47ish codes ("en-GB", "en-AU", etc.) to
            // pathway bucket names ("british", "australian", …). Unmappable
            // / null accents fall into a generic "other" bucket which the
            // pathway generator currently ignores.
            var accentBucket = MapAccentToBucket(question.Accent);
            if (accentBucket is not null)
            {
                var current = accentTally.TryGetValue(accentBucket, out var a) ? a : (0, 0);
                accentTally[accentBucket] = (current.Item1 + 1, current.Item2 + (attempt.IsCorrect ? 1 : 0));
            }

            // Spelling-miss roll-up — we only collect "meaning correct,
            // spelling wrong" because that is the actionable diagnostic
            // signal (the candidate heard the word but spelled it wrong, so
            // drill the spelling pattern). The opposite case
            // (IsSpellingCorrectMeaningWrong) is rare and indicates
            // section-crossing confusion, surfaced via wrong-review queue.
            if (attempt.IsMeaningCorrectSpellingWrong)
            {
                var canonical = TryReadString(question.CorrectAnswerJson) ?? string.Empty;
                spellingMisses.Add(new SpellingMissTuple(
                    QuestionId: question.Id,
                    LearnerAnswer: attempt.LearnerAnswer ?? attempt.SelectedOption ?? string.Empty,
                    CorrectAnswer: canonical));
            }
        }

        // Skill scores 0–10. Missing sub-skills default to the neutral 5.0
        // baseline rather than 0 so a learner who has not yet touched L7
        // doesn't show as a catastrophic gap on day 1.
        var skillScores = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in AllSkillCodes)
        {
            if (skillTally.TryGetValue(code, out var tally) && tally.Attempted > 0)
            {
                var raw = (decimal)tally.Correct / tally.Attempted * 10m;
                skillScores[code] = Clamp(raw, 0m, 10m);
            }
            else
            {
                skillScores[code] = MissingSkillNeutralBaseline;
            }
        }

        // Accent scores 0–100. Unlike skills, we OMIT accents the candidate
        // hasn't been tested on — the pathway generator's diagnostic
        // sequence is responsible for ensuring all four accents get
        // exercised before the first plan is built.
        var accentScores = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (accent, tally) in accentTally)
        {
            if (tally.Attempted <= 0) continue;
            var raw = (decimal)tally.Correct / tally.Attempted * 100m;
            accentScores[accent] = Clamp(raw, 0m, 100m);
        }

        return Task.FromResult(new DiagnosticGradingResult(
            TotalQuestions: totalQuestions,
            CorrectCount: correctCount,
            UnknownCount: unknownCount,
            SkillScores0to10: skillScores,
            AccentScores0to100: accentScores,
            SpellingMisses: spellingMisses));
    }

    // ─────────────────────────────────────────────────────────────────────
    // MCQ grader (Part B / Part C) — exact OptionKey match
    // ─────────────────────────────────────────────────────────────────────

    private static GradingResult GradeMcq(ListeningQuestionAttempt attempt, ListeningQuestion question)
    {
        var canonical = TryReadString(question.CorrectAnswerJson);
        var canonicalNorm = (canonical ?? string.Empty).Trim().ToUpperInvariant();
        var selected = (attempt.SelectedOption ?? string.Empty).Trim().ToUpperInvariant();

        var isCorrect = !string.IsNullOrEmpty(canonicalNorm)
            && string.Equals(selected, canonicalNorm, StringComparison.Ordinal);

        attempt.IsCorrect = isCorrect;
        attempt.IsSpellingCorrectMeaningWrong = false;
        attempt.IsMeaningCorrectSpellingWrong = false;

        return new GradingResult(
            IsCorrect: isCorrect,
            IsSpellingCorrectMeaningWrong: false,
            IsMeaningCorrectSpellingWrong: false,
            CanonicalAnswer: canonical);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Short-answer grader (Part A) — spelling-tolerant
    // ─────────────────────────────────────────────────────────────────────

    private static GradingResult GradeShortAnswer(ListeningQuestionAttempt attempt, ListeningQuestion question)
    {
        var canonical = TryReadString(question.CorrectAnswerJson);
        var synonyms = ParseSynonyms(question.AcceptedSynonymsJson);

        // Prefer LearnerAnswer (verbatim free-text). Fall back to
        // SelectedOption because the legacy MCQ field is reused for Part A
        // by some learner clients.
        var raw = attempt.LearnerAnswer ?? attempt.SelectedOption ?? string.Empty;
        var caseSensitive = question.CaseSensitive;

        var userNorm = Normalize(raw, caseSensitive);
        var candidates = BuildCandidates(canonical, synonyms);
        var candidateNorms = candidates
            .Select(c => Normalize(c, caseSensitive))
            .Where(s => s.Length > 0)
            .ToList();

        // 1) Exact match against any candidate → fully correct.
        if (candidateNorms.Any(c => string.Equals(userNorm, c, StringComparison.Ordinal)))
        {
            attempt.IsCorrect = true;
            attempt.IsSpellingCorrectMeaningWrong = false;
            attempt.IsMeaningCorrectSpellingWrong = false;
            return new GradingResult(true, false, false, canonical);
        }

        // 2) Empty input is wrong, but never a "spelling-wrong" classification
        // — that bucket should only fire when the candidate actually wrote
        // something close.
        if (string.IsNullOrWhiteSpace(userNorm))
        {
            attempt.IsCorrect = false;
            attempt.IsSpellingCorrectMeaningWrong = false;
            attempt.IsMeaningCorrectSpellingWrong = false;
            return new GradingResult(false, false, false, canonical);
        }

        // 3) Off-by-1 Levenshtein against canonical → spelling-wrong but
        // meaning-correct. We test the canonical only (not synonyms) because
        // synonyms are already accepted variants, so an off-by-1 against
        // them would either be a degenerate near-duplicate or junk.
        if (canonical is not null)
        {
            var canonicalNorm = Normalize(canonical, caseSensitive);
            if (canonicalNorm.Length > 0 && Levenshtein(userNorm, canonicalNorm) <= 1)
            {
                attempt.IsCorrect = false;
                attempt.IsSpellingCorrectMeaningWrong = false;
                attempt.IsMeaningCorrectSpellingWrong = true;
                return new GradingResult(false, false, true, canonical);
            }
        }

        // 4) "Sound-alike confusion" — defined in §26.5 as the candidate
        // writing a phonetically close but wrong word from ANOTHER
        // question's accepted variants in the same session. That requires
        // session-scope context we don't have here (only the single
        // question's data). The session-level grader could re-classify
        // these post-hoc; for the per-attempt path we leave the flag false.
        attempt.IsCorrect = false;
        attempt.IsSpellingCorrectMeaningWrong = false;
        attempt.IsMeaningCorrectSpellingWrong = false;
        return new GradingResult(false, false, false, canonical);
    }

    private static GradingResult GradeUnsupported(ListeningQuestionAttempt attempt, ListeningQuestion question)
    {
        // Defensive: ListeningQuestionType is a closed enum today
        // (ShortAnswer | MultipleChoice3) but if it ever expands without a
        // corresponding grader branch, mark as wrong rather than throwing —
        // a runtime exception inside a learner-facing endpoint would block
        // the entire session.
        attempt.IsCorrect = false;
        attempt.IsSpellingCorrectMeaningWrong = false;
        attempt.IsMeaningCorrectSpellingWrong = false;
        return new GradingResult(false, false, false, TryReadString(question.CorrectAnswerJson));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Normalisation helpers — mirror ListeningGradingService.NormaliseFor
    // for the trim+collapse+case-fold strategy (the Listening V2 default).
    // ─────────────────────────────────────────────────────────────────────

    private static string Normalize(string? value, bool caseSensitive)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        // Trim leading/trailing whitespace.
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return string.Empty;

        // Collapse internal runs of whitespace to a single space.
        Span<char> buffer = trimmed.Length <= 256
            ? stackalloc char[trimmed.Length]
            : new char[trimmed.Length];
        var idx = 0;
        var prevSpace = false;
        foreach (var ch in trimmed)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace) { buffer[idx++] = ' '; prevSpace = true; }
            }
            else
            {
                buffer[idx++] = ch;
                prevSpace = false;
            }
        }
        var collapsed = new string(buffer[..idx]);
        return caseSensitive ? collapsed : collapsed.ToLowerInvariant();
    }

    private static IReadOnlyList<string> BuildCandidates(string? canonical, IReadOnlyList<string> synonyms)
    {
        if (string.IsNullOrWhiteSpace(canonical) && synonyms.Count == 0)
            return Array.Empty<string>();

        var list = new List<string>(1 + synonyms.Count);
        if (!string.IsNullOrWhiteSpace(canonical)) list.Add(canonical);
        foreach (var s in synonyms)
        {
            if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
        }
        return list;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Sub-skill / accent parsing
    // ─────────────────────────────────────────────────────────────────────

    private static IEnumerable<string> ParseSubSkillCodes(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) yield break;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var code = raw.ToUpperInvariant();
            // Only return canonical L1..L8 codes — author typos like "L9" or
            // free-form tags get silently dropped rather than skewing the
            // skill score map.
            if (code.Length == 2 && code[0] == 'L' && code[1] >= '1' && code[1] <= '8' && seen.Add(code))
            {
                yield return code;
            }
        }
    }

    /// <summary>Maps an authored accent code (BCP-47ish: en-GB, en-AU, en-US,
    /// en-XX, en-IE) to the pathway's 4-bucket vocabulary used by
    /// <see cref="LearnerAccentProgress.Accent"/>. Returns null for unknown
    /// values so they're dropped from the rollup rather than corrupting the
    /// dictionary.</summary>
    private static string? MapAccentToBucket(string? accentCode)
    {
        if (string.IsNullOrWhiteSpace(accentCode)) return null;
        var c = accentCode.Trim();
        // Case-insensitive single switch — keeps the mapping co-located
        // with the field's authoring contract on ListeningQuestion.Accent.
        if (c.Equals("en-GB", StringComparison.OrdinalIgnoreCase)
            || c.Equals("en-IE", StringComparison.OrdinalIgnoreCase)
            || c.Equals("british", StringComparison.OrdinalIgnoreCase))
            return "british";
        if (c.Equals("en-AU", StringComparison.OrdinalIgnoreCase)
            || c.Equals("australian", StringComparison.OrdinalIgnoreCase))
            return "australian";
        if (c.Equals("en-US", StringComparison.OrdinalIgnoreCase)
            || c.Equals("us", StringComparison.OrdinalIgnoreCase))
            return "us";
        if (c.Equals("en-XX", StringComparison.OrdinalIgnoreCase)
            || c.Equals("non_native", StringComparison.OrdinalIgnoreCase)
            || c.Equals("non-native", StringComparison.OrdinalIgnoreCase))
            return "non_native";
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────
    // JSON parsing — tolerant of malformed authoring payloads. We never
    // throw out of the grader for parse errors because that would block
    // grading of the entire session over a single corrupt row.
    // ─────────────────────────────────────────────────────────────────────

    private static string? TryReadString(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            // CorrectAnswerJson is JSON-encoded — e.g. "\"42 mg\"" — so a
            // direct Deserialize<string> handles both quoted strings and
            // JSON nulls. We fall back to the raw string for legacy rows
            // that may have been written un-encoded.
            var parsed = JsonSerializer.Deserialize<string>(json);
            return parsed;
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static IReadOnlyList<string> ParseSynonyms(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json);
            if (parsed is null || parsed.Count == 0) return Array.Empty<string>();
            return parsed
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Levenshtein DP — two-row variant with length-difference early exit.
    // We cap input length at 64 chars; longer strings are truncated
    // because real Part A answers fit comfortably in that envelope and
    // the DP table is O(m*n) memory in the worst case.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Edit distance between two strings (insertions, deletions,
    /// substitutions; cost 1 each). Truncates to 64 chars to keep the DP
    /// table bounded.</summary>
    private static int Levenshtein(string a, string b)
    {
        if (a is null && b is null) return 0;
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;

        // Truncate pathological inputs — see class-level comment.
        var sa = a.Length > MaxLevenshteinLength ? a[..MaxLevenshteinLength] : a;
        var sb = b.Length > MaxLevenshteinLength ? b[..MaxLevenshteinLength] : b;

        if (string.Equals(sa, sb, StringComparison.Ordinal)) return 0;

        // Standard two-row dynamic-programming Levenshtein.
        var n = sa.Length;
        var m = sb.Length;
        var prev = new int[m + 1];
        var curr = new int[m + 1];
        for (var j = 0; j <= m; j++) prev[j] = j;

        for (var i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var cost = sa[i - 1] == sb[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[m];
    }

    private static decimal Clamp(decimal value, decimal min, decimal max)
        => value < min ? min : value > max ? max : value;
}
