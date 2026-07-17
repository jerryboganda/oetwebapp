using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;

namespace OetWithDrHesham.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Reading Grading Service — Slice R5
//
// Single grading pipeline:
//   1. Load attempt + answers + questions
//   2. Grade each answer via type-specific strategy
//   3. Sum PointsEarned → RawScore
//   4. Scaled = OetScoring.OetRawToScaled(raw)
//   5. Persist on the attempt
//
// MISSION CRITICAL: raw→scaled conversion happens ONLY through OetScoring.
// No inline threshold comparisons anywhere in the grading path.
// ═════════════════════════════════════════════════════════════════════════════

public interface IReadingGradingService
{
    Task<ReadingGradingResult> GradeAttemptAsync(string attemptId, CancellationToken ct);

    /// <summary>
    /// Wave 2 — re-grade an already-Submitted attempt in place from its
    /// stored answers (used by accepted-answer recalculation after an admin
    /// edits a question's correct answer / accepted synonyms). Re-derives
    /// raw + scaled via <see cref="OetScoring"/>, persists, and bumps
    /// RowVersion WITHOUT changing <see cref="ReadingAttempt.SubmittedAt"/>.
    /// Returns null when the attempt is missing or not Submitted.
    /// </summary>
    Task<ReadingGradingResult?> RegradeSubmittedAsync(string attemptId, CancellationToken ct);
}

public sealed record ReadingGradingResult(
    int RawScore,
    int MaxRawScore,
    int? ScaledScore,
    string GradeLetter,
    int CorrectCount,
    int IncorrectCount,
    int UnansweredCount,
    IReadOnlyList<ReadingAnswerResult> Answers);

public sealed record ReadingAnswerResult(
    string QuestionId,
    string QuestionType,
    bool IsCorrect,
    int PointsEarned,
    int MaxPoints);

public sealed class ReadingGradingService(
    LearnerDbContext db,
    IReadingPolicyService policyService,
    ILogger<ReadingGradingService> logger) : IReadingGradingService
{
    public async Task<ReadingGradingResult> GradeAttemptAsync(string attemptId, CancellationToken ct)
    {
        var attempt = await db.ReadingAttempts
            .Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct)
            ?? throw new InvalidOperationException("Attempt not found.");

        // Idempotent: if already graded, return existing result.
        if (attempt.Status == ReadingAttemptStatus.Submitted && attempt.RawScore is int existingRaw)
        {
            var shouldRegradeSubmittedSubset = IsSubsetPracticeMode(attempt.Mode)
                && (attempt.ScaledScore is not null
                    || attempt.MaxRawScore == ReadingStructureService.CanonicalMaxRawScore);
            if (!shouldRegradeSubmittedSubset)
            {
                return await BuildResultFromExistingAsync(attempt, existingRaw, ct);
            }
        }

        return await GradeAndPersistAsync(attempt, "ReadingAttemptGraded", ct);
    }

    /// <summary>
    /// Wave 2 — force a re-grade of an already-Submitted attempt in place.
    /// Bypasses the idempotent short-circuit in <see cref="GradeAttemptAsync"/>
    /// so an accepted-answer edit can be propagated to historical attempts.
    /// Writes no audit row of its own — the caller (ReadingTutorService)
    /// records the recalculation audit so the action is logged once with
    /// full context.
    /// </summary>
    public async Task<ReadingGradingResult?> RegradeSubmittedAsync(string attemptId, CancellationToken ct)
    {
        var attempt = await db.ReadingAttempts
            .Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null || attempt.Status != ReadingAttemptStatus.Submitted)
            return null;
        return await GradeAndPersistAsync(attempt, auditAction: null, ct);
    }

    private async Task<ReadingGradingResult> GradeAndPersistAsync(
        ReadingAttempt attempt, string? auditAction, CancellationToken ct)
    {
        // Load all questions for the paper (single round-trip)
        var partIds = await db.ReadingParts
            .Where(p => p.PaperId == attempt.PaperId)
            .Select(p => p.Id)
            .ToListAsync(ct);
        var partCodeByPartId = await db.ReadingParts.AsNoTracking()
            .Where(p => p.PaperId == attempt.PaperId)
            .ToDictionaryAsync(p => p.Id, p => p.PartCode, ct);
        var questions = await db.ReadingQuestions
            .Where(q => partIds.Contains(q.ReadingPartId))
            .ToListAsync(ct);

        var questionById = questions.ToDictionary(q => q.Id);
        var answersByQuestionId = attempt.Answers.ToDictionary(a => a.ReadingQuestionId);

        // Phase 3b: subset modes (Drill / MiniTest / ErrorBank) only score
        // their in-scope questions; full-paper modes (Exam / Learning) keep
        // the canonical 42-question denominator and OET 0-500 conversion.
        var isSubsetPracticeMode = IsSubsetPracticeMode(attempt.Mode);
        var scopeQuestionIds = ParseScopeQuestionIds(attempt);
        var gradedQuestions = isSubsetPracticeMode
            ? questions.Where(q => scopeQuestionIds?.Contains(q.Id) == true).ToList()
            : questions;

        var policy = await ResolvePolicyForAttemptAsync(attempt, ct);
        var details = new List<ReadingAnswerResult>(gradedQuestions.Count);

        int raw = 0, correctCount = 0, incorrectCount = 0, unanswered = 0, maxRaw = 0;
        foreach (var q in gradedQuestions)
        {
            maxRaw += q.Points;
            ReadingAnswer? answer = answersByQuestionId.GetValueOrDefault(q.Id);
            if (answer is null)
            {
                unanswered++;
                details.Add(new(q.Id, q.QuestionType.ToString(), false, 0, q.Points));
                continue;
            }

            var partCode = partCodeByPartId.GetValueOrDefault(q.ReadingPartId, ReadingPartCode.A);
            var (isCorrect, pts) = GradeOne(q, answer, policy, attempt.Mode, partCode);
            answer.IsCorrect = isCorrect;
            answer.PointsEarned = pts;
            answer.SelectedDistractorCategory = isCorrect
                ? null
                : ResolveSelectedDistractor(q, answer);
            answer.MissReason = ClassifyMiss(q, answer, policy, partCode, isCorrect);
            raw += pts;
            if (isCorrect) correctCount++; else incorrectCount++;
            details.Add(new(q.Id, q.QuestionType.ToString(), isCorrect, pts, q.Points));
        }

        attempt.RawScore = raw;
        attempt.MaxRawScore = isSubsetPracticeMode ? maxRaw : ReadingStructureService.CanonicalMaxRawScore;

        // Subset attempts skip OET 0-500 conversion: the 30/42 anchor only
        // applies to the canonical full paper. Store null so the result
        // surface treats it as practice-only.
        attempt.ScaledScore = isSubsetPracticeMode ? null : OetScoring.OetRawToScaled(raw);
        attempt.Status = ReadingAttemptStatus.Submitted;
        attempt.SubmittedAt ??= DateTimeOffset.UtcNow;
        attempt.LastActivityAt = DateTimeOffset.UtcNow;

        // P0-F 2026-05 hardening: bump the optimistic-concurrency token so
        // that a concurrent grader running against the same row hits a
        // DbUpdateConcurrencyException on save instead of silently
        // overwriting our final score.
        attempt.RowVersion++;

        await UpdateErrorBankAsync(attempt, questionById, ct);

        if (auditAction is not null)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = DateTimeOffset.UtcNow,
                ActorId = attempt.UserId,
                ActorName = attempt.UserId,
                Action = auditAction,
                ResourceType = "ReadingAttempt",
                ResourceId = attempt.Id,
                Details = $"raw={raw}/{attempt.MaxRawScore} scaled={attempt.ScaledScore?.ToString() ?? "n/a"} mode={attempt.Mode}",
            });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another submit raced us to graded state. Reload the canonical
            // result the winner persisted instead of overwriting it.
            logger.LogInformation(
                "Reading attempt {AttemptId} grade lost concurrency race; returning winner's stored result.",
                attempt.Id);
            db.ChangeTracker.Clear();
            var winner = await db.ReadingAttempts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == attempt.Id, ct)
                ?? throw new InvalidOperationException("Attempt vanished after concurrency conflict.");
            if (winner.RawScore is int winnerRaw)
            {
                return await BuildResultFromExistingAsync(winner, winnerRaw, ct);
            }
            // Winner not yet committed - rare. Fall through with our values.
        }

        return new ReadingGradingResult(
            RawScore: raw,
            MaxRawScore: attempt.MaxRawScore,
            ScaledScore: attempt.ScaledScore,
            GradeLetter: attempt.ScaledScore is int scaledForReturn
                ? OetScoring.OetGradeLetterFromScaled(scaledForReturn)
                : "—",
            CorrectCount: correctCount,
            IncorrectCount: incorrectCount,
            UnansweredCount: unanswered,
            Answers: details);
    }

    private static HashSet<string>? ParseScopeQuestionIds(ReadingAttempt attempt)
    {
        if (!IsSubsetPracticeMode(attempt.Mode))
            return null;
        if (string.IsNullOrWhiteSpace(attempt.ScopeJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(attempt.ScopeJson);
            if (!doc.RootElement.TryGetProperty("questionIds", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                return null;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 } s)
                    ids.Add(s);
            }
            return ids.Count > 0 ? ids : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool IsSubsetPracticeMode(ReadingAttemptMode mode)
        => mode is ReadingAttemptMode.Drill or ReadingAttemptMode.MiniTest or ReadingAttemptMode.ErrorBank;

    // ── Grader strategies ────────────────────────────────────────────────

    private (bool isCorrect, int points) GradeOne(
        ReadingQuestion q,
        ReadingAnswer a,
        ReadingResolvedPolicy policy,
        ReadingAttemptMode mode,
        ReadingPartCode partCode)
    {
        try
        {
            var strictPartA = partCode == ReadingPartCode.A;
            return q.QuestionType switch
            {
                ReadingQuestionType.MultipleChoice3 or
                ReadingQuestionType.MultipleChoice4 or
                ReadingQuestionType.MultipleChoiceFlexible => GradeMcq(q, a),

                ReadingQuestionType.MatchingTextReference => strictPartA
                    ? GradeStrictSingleLetter(q, a)
                    : GradeMatching(q, a, policy),

                ReadingQuestionType.ShortAnswer or
                ReadingQuestionType.SentenceCompletion => strictPartA
                    ? GradeStrictTextAnswer(q, a, policy)
                    : GradeShortAnswer(q, a, policy),

                ReadingQuestionType.FillInBlank => GradeShortAnswer(q, a, policy),

                ReadingQuestionType.ShortAnswerLabeled => GradeLabeledShortAnswer(q, a, policy),

                _ => ApplyUnknownFallback(q, a, policy),
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Grading exception for question {QuestionId} type {Type} — applying fallback policy.",
                q.Id, q.QuestionType);
            return ApplyUnknownFallback(q, a, policy);
        }
    }

    private static (bool, int) GradeMcq(ReadingQuestion q, ReadingAnswer a)
    {
        var correct = JsonSerializer.Deserialize<string>(q.CorrectAnswerJson)?.Trim().ToUpperInvariant();
        string user;
        try { user = JsonSerializer.Deserialize<string>(a.UserAnswerJson)?.Trim().ToUpperInvariant() ?? ""; }
        catch (JsonException) { user = (a.UserAnswerJson ?? "").Trim().ToUpperInvariant(); }

        // Wave 1.1.1 dual-read: if user answer looks like an option ID, resolve to letter
        if (user.StartsWith("OPT-", StringComparison.OrdinalIgnoreCase))
            user = ResolveOptionIdToLetter(q.OptionsJson, user) ?? user;

        var ok = !string.IsNullOrEmpty(correct) && correct == user;
        return (ok, ok ? q.Points : 0);
    }

    private static (bool, int) GradeLabeledShortAnswer(ReadingQuestion q, ReadingAnswer a, ReadingResolvedPolicy policy)
    {
        if (!TryParseStringMap(q.CorrectAnswerJson, out var correctAnswers)
            || !TryParseStringMap(a.UserAnswerJson, out var userAnswers))
        {
            return GradeShortAnswer(q, a, policy);
        }

        if (correctAnswers.Count == 0 || userAnswers.Count != correctAnswers.Count)
            return (false, 0);

        ParseLabeledSynonyms(q.AcceptedSynonymsJson, out var globalSynonyms, out var labeledSynonyms);

        foreach (var (label, correctValue) in correctAnswers)
        {
            if (!userAnswers.TryGetValue(label, out var userValue))
                return (false, 0);

            var candidates = new List<string> { correctValue };
            if (labeledSynonyms.TryGetValue(label, out var labelSyns))
            {
                candidates.AddRange(labelSyns);
            }
            else if (globalSynonyms is not null)
            {
                candidates.AddRange(globalSynonyms);
            }

            var matched = candidates.Any(candidate =>
                StringsMatch(
                    ApplyTextNormalization(userValue, policy),
                    ApplyTextNormalization(candidate, policy),
                    q.CaseSensitive,
                    policy.ShortAnswerNormalisation));
            if (!matched)
                return (false, 0);
        }

        return (true, q.Points);
    }

    /// <summary>
    /// Resolves an option ID (e.g. "opt-abc123def456") to its letter (e.g. "A")
    /// by searching the enriched OptionsJson. Returns null if not found.
    /// </summary>
    private static string? ResolveOptionIdToLetter(string optionsJson, string optionId)
    {
        try
        {
            using var doc = JsonDocument.Parse(optionsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var opt in doc.RootElement.EnumerateArray())
            {
                if (opt.ValueKind != JsonValueKind.Object) continue;
                if (opt.TryGetProperty("id", out var idProp)
                    && string.Equals(idProp.GetString(), optionId, StringComparison.OrdinalIgnoreCase)
                    && opt.TryGetProperty("letter", out var letterProp))
                {
                    return letterProp.GetString()?.Trim().ToUpperInvariant();
                }
            }
        }
        catch (JsonException) { /* malformed options — cannot resolve */ }
        return null;
    }

    /// <summary>
    /// Phase 4 — when a learner picks a wrong MCQ option that the author
    /// tagged with a distractor category, cache it on the answer for fast
    /// analytics. Silent on parse errors / missing metadata: this is purely
    /// informational and must never block grading.
    /// </summary>
    private static ReadingDistractorCategory? ResolveSelectedDistractor(ReadingQuestion q, ReadingAnswer a)
    {
        if (q.QuestionType != ReadingQuestionType.MultipleChoice3
            && q.QuestionType != ReadingQuestionType.MultipleChoice4
            && q.QuestionType != ReadingQuestionType.MultipleChoiceFlexible)
            return null;
        if (string.IsNullOrWhiteSpace(q.OptionDistractorsJson)) return null;

        string user;
        try { user = JsonSerializer.Deserialize<string>(a.UserAnswerJson)?.Trim().ToUpperInvariant() ?? ""; }
        catch (JsonException) { user = (a.UserAnswerJson ?? "").Trim().ToUpperInvariant(); }
        if (user.Length == 0) return null;

        try
        {
            using var doc = JsonDocument.Parse(q.OptionDistractorsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name.Trim(), user, StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.String
                    && Enum.TryParse<ReadingDistractorCategory>(prop.Value.GetString(), ignoreCase: true, out var cat))
                {
                    return cat;
                }
            }
        }
        catch (JsonException) { /* malformed metadata — drop silently */ }
        return null;
    }

    private static (bool, int) GradeMatching(ReadingQuestion q, ReadingAnswer a, ReadingResolvedPolicy policy)
    {
        var correct = ParseStringSet(q.CorrectAnswerJson);
        var user = ParseStringSet(a.UserAnswerJson);
        if (correct.Count == 0) return (false, 0);

        if (policy.MatchingAllowPartialCredit)
        {
            // Partial: each correct element contributes proportionally.
            var hits = correct.Count(c => user.Contains(c));
            if (hits == 0) return (false, 0);
            var pts = (int)Math.Floor((double)q.Points * hits / correct.Count);
            return (hits == correct.Count, Math.Max(0, pts));
        }

        // All-or-nothing
        var allRight = correct.SetEquals(user);
        return (allRight, allRight ? q.Points : 0);
    }

    private static (bool, int) GradeStrictSingleLetter(ReadingQuestion q, ReadingAnswer a)
    {
        var correct = ParseJsonString(q.CorrectAnswerJson)?.Trim();
        var user = ParseJsonString(a.UserAnswerJson)?.Trim();
        var ok = correct is "A" or "B" or "C" or "D"
            && string.Equals(user, correct, StringComparison.Ordinal);
        return (ok, ok ? q.Points : 0);
    }

    private static (bool, int) GradeStrictTextAnswer(ReadingQuestion q, ReadingAnswer a, ReadingResolvedPolicy policy)
    {
        var correct = ParseJsonString(q.CorrectAnswerJson);
        var user = ParseJsonString(a.UserAnswerJson);
        if (correct is null || user is null) return (false, 0);

        // STRICT spelling: normalise + collapse whitespace + (optional)
        // smart-quote / hyphen / unit folding, then compare. NO Levenshtein
        // and NO synonyms for Part A — real OET answers are copied
        // word-for-word from the text. Case-insensitivity is policy-gated.
        var nc = CollapseWhitespace(ApplyTextNormalization(correct.Trim(), policy));
        var nu = CollapseWhitespace(ApplyTextNormalization(user.Trim(), policy));

        var ok = policy.PartACaseInsensitive
            ? string.Equals(nu, nc, StringComparison.OrdinalIgnoreCase)
            : string.Equals(nu, nc, StringComparison.Ordinal);
        return (ok, ok ? q.Points : 0);
    }

    private static (bool, int) GradeShortAnswer(ReadingQuestion q, ReadingAnswer a, ReadingResolvedPolicy policy)
    {
        string correct;
        try { correct = JsonSerializer.Deserialize<string>(q.CorrectAnswerJson) ?? ""; }
        catch (JsonException) { correct = q.CorrectAnswerJson; }

        string user;
        try { user = JsonSerializer.Deserialize<string>(a.UserAnswerJson) ?? ""; }
        catch (JsonException) { user = a.UserAnswerJson ?? ""; }

        var candidates = new List<string> { correct };
        if (policy.ShortAnswerAcceptSynonyms && !string.IsNullOrWhiteSpace(q.AcceptedSynonymsJson))
        {
            try
            {
                var syns = JsonSerializer.Deserialize<string[]>(q.AcceptedSynonymsJson!);
                if (syns is not null) candidates.AddRange(syns);
            }
            catch (JsonException) { /* malformed — fall through with primary only */ }
        }

        foreach (var c in candidates)
        {
            if (StringsMatch(
                    ApplyTextNormalization(user, policy),
                    ApplyTextNormalization(c, policy),
                    q.CaseSensitive,
                    policy.ShortAnswerNormalisation))
                return (true, q.Points);
        }
        return (false, 0);
    }

    private static (bool, int) ApplyUnknownFallback(ReadingQuestion q, ReadingAnswer a, ReadingResolvedPolicy policy)
    {
        return policy.UnknownTypeFallbackPolicy switch
        {
            "grade_as_correct" => (true, q.Points),
            "fail_grading" => throw new InvalidOperationException(
                $"Grader refused for question {q.Id} type {q.QuestionType} per fallback policy."),
            _ /* skip_with_zero */ => (false, 0),
        };
    }

    private static bool StringsMatch(string a, string b, bool caseSensitive, string normalisation)
    {
        if (a is null || b is null) return false;
        var na = Normalise(a, normalisation);
        var nb = Normalise(b, normalisation);

        // Decision 4 — hard-lock strictness everywhere: NO fuzzy/Levenshtein
        // ACCEPTANCE in any mode/policy. A stale `fuzzy_levenshtein_1`
        // normalisation degrades to exact match. (Levenshtein survives only in
        // ClassifyMiss for analytics labelling, never for grading.)
        return caseSensitive
            ? string.Equals(na, nb, StringComparison.Ordinal)
            : string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalise(string s, string strategy) => strategy switch
    {
        "exact" => s,
        "trim_only" => s.Trim(),
        "fuzzy_levenshtein_1" => CollapseWhitespace(s.Trim()),
        _ /* trim_collapse_case_insensitive */ => CollapseWhitespace(s.Trim()),
    };

    // R04.2: true when the two normalised answers differ ONLY by a
    // singular/plural inflection (trailing s/es, or y -> ies). Analytics
    // labelling only — such an answer is still graded WRONG.
    private static bool IsNumberFormDifference(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        var x = a.ToUpperInvariant();
        var y = b.ToUpperInvariant();
        if (string.Equals(x, y, StringComparison.Ordinal)) return false;
        foreach (var (s, p) in new[] { (x, y), (y, x) })
        {
            if (p == s + "S" || p == s + "ES") return true;
            if (s.Length > 1 && s.EndsWith("Y", StringComparison.Ordinal) && p == s[..^1] + "IES") return true;
        }
        return false;
    }

    private static bool LevenshteinDistanceAtMostOne(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal)) return true;
        if (Math.Abs(a.Length - b.Length) > 1) return false;

        var edits = 0;
        var i = 0;
        var j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (a[i] == b[j])
            {
                i++;
                j++;
                continue;
            }

            edits++;
            if (edits > 1) return false;

            if (a.Length == b.Length)
            {
                i++;
                j++;
            }
            else if (a.Length > b.Length)
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        if (i < a.Length || j < b.Length) edits++;
        return edits <= 1;
    }

    private static string CollapseWhitespace(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length);
        var prevSpace = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace) sb.Append(' ');
                prevSpace = true;
            }
            else { sb.Append(ch); prevSpace = false; }
        }
        return sb.ToString();
    }

    // ── Wave 1 — text normalisation shared by Part A and B/C grading ─────

    /// <summary>
    /// Applies the policy-gated text-normalisation toggles (smart quotes,
    /// hyphen spacing, unit spacing) to a single string. Used by BOTH the
    /// Part A strict-text path and the B/C short-answer path so the same
    /// rules govern every typed comparison.
    /// </summary>
    private static string ApplyTextNormalization(string s, ReadingResolvedPolicy policy)
    {
        if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
        var result = s;
        if (policy.NormalizeSmartQuotes) result = FoldSmartQuotes(result);
        if (policy.NormalizeHyphenSpacing) result = FoldHyphenSpacing(result);
        if (policy.NormalizeUnitSpacing) result = FoldUnitSpacing(result);
        return result;
    }

    /// <summary>Maps curly/typographic apostrophes and quotes to their ASCII
    /// equivalents: ’ ‘ ‛ ` ´ → ' and “ ” „ → ". En/em dashes are left
    /// untouched (hyphen normalisation handles spacing only).</summary>
    private static string FoldSmartQuotes(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\u2019': // ’ right single quote
                case '\u2018': // ‘ left single quote
                case '\u201B': // ‛ single high-reversed-9
                case '`':      // grave accent
                case '\u00B4': // ´ acute accent
                    sb.Append('\'');
                    break;
                case '\u201C': // “ left double quote
                case '\u201D': // ” right double quote
                case '\u201E': // „ low double quote
                    sb.Append('"');
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Collapses whitespace around a hyphen: <c>"x - y"</c> →
    /// <c>"x-y"</c>.</summary>
    private static string FoldHyphenSpacing(string s)
        => System.Text.RegularExpressions.Regex.Replace(s, @"\s*-\s*", "-");

    /// <summary>Removes the space between a number and a following unit /
    /// percent token: <c>"500 mg"</c> → <c>"500mg"</c>.</summary>
    private static string FoldUnitSpacing(string s)
        => System.Text.RegularExpressions.Regex.Replace(s, @"(?<=\d)\s+(?=[A-Za-z%])", "");

    /// <summary>
    /// Wave 1 — classify why a graded answer was missed. Returns null for a
    /// correct answer. Priority order mirrors the documented decision list:
    /// blank → spelling → distractor → wrong_text → incomplete → wrong.
    /// </summary>
    private static string? ClassifyMiss(
        ReadingQuestion q,
        ReadingAnswer a,
        ReadingResolvedPolicy policy,
        ReadingPartCode partCode,
        bool isCorrect)
    {
        if (isCorrect) return null;

        var userRaw = ParseJsonString(a.UserAnswerJson) ?? a.UserAnswerJson ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userRaw)) return "blank";

        var isPartATyped = partCode == ReadingPartCode.A
            && q.QuestionType is ReadingQuestionType.ShortAnswer
                or ReadingQuestionType.SentenceCompletion;

        string? nu = null, nc = null;
        if (isPartATyped)
        {
            var correctRaw = ParseJsonString(q.CorrectAnswerJson) ?? string.Empty;
            nu = CollapseWhitespace(ApplyTextNormalization(userRaw.Trim(), policy));
            nc = CollapseWhitespace(ApplyTextNormalization(correctRaw.Trim(), policy));

            // R04.2 singular/plural — still WRONG, but labelled distinctly for
            // analytics. Checked before "spelling" so a plural inflection is not
            // mislabelled as a one-edit typo.
            if (!string.IsNullOrEmpty(nc) && IsNumberFormDifference(nu, nc))
            {
                return "number_form";
            }

            // "spelling": would have been right but for case/spelling — a
            // case-insensitive normalised match or a single-edit typo.
            if (!string.IsNullOrEmpty(nc)
                && (string.Equals(nu, nc, StringComparison.OrdinalIgnoreCase)
                    || LevenshteinDistanceAtMostOne(nu.ToUpperInvariant(), nc.ToUpperInvariant())))
            {
                return "spelling";
            }
        }

        if ((q.QuestionType is ReadingQuestionType.MultipleChoice3
                or ReadingQuestionType.MultipleChoice4
                or ReadingQuestionType.MultipleChoiceFlexible)
            && a.SelectedDistractorCategory is not null)
        {
            return "distractor";
        }

        if (q.QuestionType == ReadingQuestionType.MatchingTextReference)
        {
            return "wrong_text";
        }

        if (isPartATyped && nu is not null && nc is not null
            && nu.Length > 0 && nc.Length > nu.Length
            && nc.Contains(nu, StringComparison.OrdinalIgnoreCase))
        {
            return "incomplete";
        }

        return "wrong";
    }

    private static HashSet<string> ParseStringSet(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var root = JsonDocument.Parse(json).RootElement;
            return root.ValueKind switch
            {
                JsonValueKind.Array => root.EnumerateArray()
                    .Select(e => (e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText()) ?? "")
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim().ToUpperInvariant())
                    .ToHashSet(),
                JsonValueKind.String => new HashSet<string> { root.GetString()!.Trim().ToUpperInvariant() },
                _ => new()
            };
        }
        catch (JsonException) { return new(); }
    }

    private static string? ParseJsonString(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<string>(json); }
        catch (JsonException) { return null; }
    }

    private static bool TryParseStringMap(string? json, out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String)
                    return false;

                var key = prop.Name.Trim();
                if (key.Length == 0)
                    return false;

                values[key] = prop.Value.GetString() ?? string.Empty;
            }

            return values.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void ParseLabeledSynonyms(
        string? json,
        out string[]? globalSynonyms,
        out Dictionary<string, string[]> labeledSynonyms)
    {
        globalSynonyms = null;
        labeledSynonyms = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var global = new List<string>();
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                        global.Add(s);
                }

                if (global.Count > 0)
                    globalSynonyms = global.ToArray();
                return;
            }

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(prop.Name) || prop.Value.ValueKind != JsonValueKind.Array)
                    continue;

                var list = new List<string>();
                foreach (var item in prop.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                        list.Add(s);
                }

                if (list.Count > 0)
                    labeledSynonyms[prop.Name.Trim()] = list.ToArray();
            }
        }
        catch (JsonException)
        {
            // Malformed synonyms are ignored; grading still falls back to the primary answer.
        }
    }

    /// <summary>
    /// Phase 3 Error Bank — keep <see cref="ReadingErrorBankEntry"/> rows
    /// in sync with this attempt's grading. Wrong answers upsert an entry
    /// (incrementing TimesWrong + LastSeenWrongAt). Correct answers on a
    /// previously-missed question resolve the entry.
    /// </summary>
    private async Task UpdateErrorBankAsync(
        ReadingAttempt attempt,
        IReadOnlyDictionary<string, ReadingQuestion> questionById,
        CancellationToken ct)
    {
        if (attempt.Answers.Count == 0) return;

        var questionIds = attempt.Answers.Select(a => a.ReadingQuestionId).ToList();
        var existing = await db.ReadingErrorBankEntries
            .Where(e => e.UserId == attempt.UserId && questionIds.Contains(e.ReadingQuestionId))
            .ToListAsync(ct);
        var byQ = existing.ToDictionary(e => e.ReadingQuestionId);
        var now = DateTimeOffset.UtcNow;

        foreach (var ans in attempt.Answers)
        {
            if (!questionById.TryGetValue(ans.ReadingQuestionId, out var q)) continue;
            var partCode = await ResolvePartCodeAsync(q, ct);
            byQ.TryGetValue(ans.ReadingQuestionId, out var entry);

            if (ans.IsCorrect == true)
            {
                if (entry is { IsResolved: false })
                {
                    entry.IsResolved = true;
                    entry.ResolvedAt = now;
                    entry.ResolvedReason = "answered_correctly";
                }
                continue;
            }

            if (entry is null)
            {
                entry = new ReadingErrorBankEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = attempt.UserId,
                    ReadingQuestionId = ans.ReadingQuestionId,
                    PaperId = attempt.PaperId,
                    PartCode = partCode,
                    LastWrongAttemptId = attempt.Id,
                    FirstSeenWrongAt = now,
                    LastSeenWrongAt = now,
                    TimesWrong = 1,
                    IsResolved = false,
                };
                db.ReadingErrorBankEntries.Add(entry);
                byQ[ans.ReadingQuestionId] = entry;
            }
            else
            {
                entry.LastWrongAttemptId = attempt.Id;
                entry.LastSeenWrongAt = now;
                entry.TimesWrong += 1;
                entry.PartCode = partCode;
                entry.PaperId = attempt.PaperId;
                if (entry.IsResolved)
                {
                    entry.IsResolved = false;
                    entry.ResolvedAt = null;
                    entry.ResolvedReason = null;
                }
            }
        }
    }

    private async Task<ReadingPartCode> ResolvePartCodeAsync(ReadingQuestion q, CancellationToken ct)
    {
        if (q.Part is not null) return q.Part.PartCode;
        var code = await db.ReadingParts.AsNoTracking()
            .Where(p => p.Id == q.ReadingPartId)
            .Select(p => (ReadingPartCode?)p.PartCode)
            .FirstOrDefaultAsync(ct);
        return code ?? ReadingPartCode.A;
    }

    private async Task<ReadingGradingResult> BuildResultFromExistingAsync(
        ReadingAttempt attempt, int raw, CancellationToken ct)
    {
        var ansMap = attempt.Answers.ToDictionary(x => x.ReadingQuestionId);
        var partIds = await db.ReadingParts
            .Where(p => p.PaperId == attempt.PaperId)
            .Select(p => p.Id)
            .ToListAsync(ct);
        var questions = await db.ReadingQuestions
            .Where(q => partIds.Contains(q.ReadingPartId))
            .ToListAsync(ct);

        // Subset modes only include their in-scope questions in the
        // returned details + counts.
        var isSubsetPracticeMode = IsSubsetPracticeMode(attempt.Mode);
        var scopeIds = ParseScopeQuestionIds(attempt);
        if (isSubsetPracticeMode)
        {
            questions = questions.Where(q => scopeIds?.Contains(q.Id) == true).ToList();
        }

        int correct = 0, wrong = 0, unans = 0;
        var details = new List<ReadingAnswerResult>(questions.Count);
        foreach (var q in questions)
        {
            if (!ansMap.TryGetValue(q.Id, out var a))
            {
                unans++;
                details.Add(new(q.Id, q.QuestionType.ToString(), false, 0, q.Points));
                continue;
            }
            var ok = a.IsCorrect ?? false;
            if (ok) correct++; else wrong++;
            details.Add(new(q.Id, q.QuestionType.ToString(), ok, a.PointsEarned, q.Points));
        }

        var scaled = IsSubsetPracticeMode(attempt.Mode) ? null : attempt.ScaledScore;
        var grade = scaled is null ? "—" : OetScoring.OetGradeLetterFromScaled(scaled.Value);
        return new ReadingGradingResult(
            raw, attempt.MaxRawScore, scaled, grade,
            correct, wrong, unans, details);
    }

    private async Task<ReadingResolvedPolicy> ResolvePolicyForAttemptAsync(ReadingAttempt attempt, CancellationToken ct)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<ReadingResolvedPolicy>(attempt.PolicySnapshotJson);
            if (snapshot is not null
                && !string.IsNullOrWhiteSpace(snapshot.PartATimerStrictness)
                && !string.IsNullOrWhiteSpace(snapshot.ShortAnswerNormalisation)
                && !string.IsNullOrWhiteSpace(snapshot.UnknownTypeFallbackPolicy))
            {
                return snapshot;
            }
        }
        catch (JsonException)
        {
            // Older or malformed attempts fall back to the current resolver.
        }

        return await policyService.ResolveForUserAsync(attempt.UserId, ct);
    }
}
