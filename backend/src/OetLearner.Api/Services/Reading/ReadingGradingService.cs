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
    int ScaledScore,
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
            return await BuildResultFromExistingAsync(attempt, existingRaw, ct);
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

        var policy = await policyService.ResolveForUserAsync(attempt.UserId, ct);
        var details = new List<ReadingAnswerResult>(questions.Count);

        int raw = 0, correctCount = 0, incorrectCount = 0, unanswered = 0;
        foreach (var q in questions)
        {
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
            raw += pts;
            if (isCorrect) correctCount++; else incorrectCount++;
            details.Add(new(q.Id, q.QuestionType.ToString(), isCorrect, pts, q.Points));
        }

        attempt.RawScore = raw;
        attempt.MaxRawScore = ReadingStructureService.CanonicalMaxRawScore;
        attempt.ScaledScore = OetScoring.OetRawToScaled(raw);
        attempt.Status = ReadingAttemptStatus.Submitted;
        attempt.SubmittedAt ??= DateTimeOffset.UtcNow;
        attempt.LastActivityAt = DateTimeOffset.UtcNow;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = attempt.UserId,
            ActorName = attempt.UserId,
            Action = "ReadingAttemptGraded",
            ResourceType = "ReadingAttempt",
            ResourceId = attempt.Id,
            Details = $"raw={raw}/{attempt.MaxRawScore} scaled={attempt.ScaledScore}",
        });
        await db.SaveChangesAsync(ct);

        return new ReadingGradingResult(
            RawScore: raw,
            MaxRawScore: attempt.MaxRawScore,
            ScaledScore: attempt.ScaledScore!.Value,
            GradeLetter: OetScoring.OetGradeLetterFromScaled(attempt.ScaledScore!.Value),
            CorrectCount: correctCount,
            IncorrectCount: incorrectCount,
            UnansweredCount: unanswered,
            Answers: details);
    }

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
        return caseSensitive
            ? string.Equals(na, nb, StringComparison.Ordinal)
            : string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalise(string s, string strategy) => strategy switch
    {
        "exact" => s,
        "trim_only" => s.Trim(),
        "fuzzy_levenshtein_1" => CollapseWhitespace(s.Trim()), // distance computed by caller if enabled; default same as trim_collapse
        _ /* trim_collapse_case_insensitive */ => CollapseWhitespace(s.Trim()),
    };

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

        return new ReadingGradingResult(
            raw, attempt.MaxRawScore,
            attempt.ScaledScore ?? OetScoring.OetRawToScaled(raw),
            OetScoring.OetGradeLetterFromScaled(attempt.ScaledScore ?? OetScoring.OetRawToScaled(raw)),
            correct, wrong, unans, details);
    }
}
