using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Services.Reading;

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

        // Load all questions for the paper (single round-trip)
        var partIds = await db.ReadingParts
            .Where(p => p.PaperId == attempt.PaperId)
            .Select(p => p.Id)
            .ToListAsync(ct);
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

            var (isCorrect, pts) = GradeOne(q, answer, policy);
            answer.IsCorrect = isCorrect;
            answer.PointsEarned = pts;
            answer.SelectedDistractorCategory = isCorrect
                ? null
                : ResolveSelectedDistractor(q, answer);
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

        await UpdateErrorBankAsync(attempt, questionById, ct);

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = attempt.UserId,
            ActorName = attempt.UserId,
            Action = "ReadingAttemptGraded",
            ResourceType = "ReadingAttempt",
            ResourceId = attempt.Id,
            Details = $"raw={raw}/{attempt.MaxRawScore} scaled={attempt.ScaledScore?.ToString() ?? "n/a"} mode={attempt.Mode}",
        });
        await db.SaveChangesAsync(ct);

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

    private (bool isCorrect, int points) GradeOne(ReadingQuestion q, ReadingAnswer a, ReadingResolvedPolicy policy)
    {
        try
        {
            return q.QuestionType switch
            {
                ReadingQuestionType.MultipleChoice3 or
                ReadingQuestionType.MultipleChoice4 => GradeMcq(q, a),

                ReadingQuestionType.MatchingTextReference => GradeMatching(q, a, policy),

                ReadingQuestionType.ShortAnswer or
                ReadingQuestionType.SentenceCompletion => GradeShortAnswer(q, a, policy),

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
        var ok = !string.IsNullOrEmpty(correct) && correct == user;
        return (ok, ok ? q.Points : 0);
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
            && q.QuestionType != ReadingQuestionType.MultipleChoice4)
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
            if (StringsMatch(user, c, q.CaseSensitive, policy.ShortAnswerNormalisation))
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

        if (string.Equals(normalisation, "fuzzy_levenshtein_1", StringComparison.OrdinalIgnoreCase))
        {
            if (!caseSensitive)
            {
                na = na.ToUpperInvariant();
                nb = nb.ToUpperInvariant();
            }

            return LevenshteinDistanceAtMostOne(na, nb);
        }

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
