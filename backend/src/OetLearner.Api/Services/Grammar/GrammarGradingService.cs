using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Grammar;

// ═════════════════════════════════════════════════════════════════════════════
// Grammar Grading Service — MISSION CRITICAL
//
// Server-authoritative grading of a learner's exercise submission. The
// client never knows the correct answer until this service returns it.
//
// Pipeline:
//   1. Load lesson + exercises from db.
//   2. For each exercise, apply the per-type comparison strategy against
//      `CorrectAnswerJson` and `AcceptedAnswersJson`.
//   3. Write one `GrammarExerciseAttempt` row per exercise.
//   4. Update `LearnerGrammarProgress.MasteryScore` using EWMA.
//   5. Return a `GrammarGradingResult` with per-exercise detail.
//
// The caller (endpoint or orchestrator) is responsible for:
//   - Creating ReviewItem rows for wrong answers (spaced repetition).
//   - Awarding XP / checking achievements.
//   - Updating the topic mastery aggregate.
// ═════════════════════════════════════════════════════════════════════════════

public interface IGrammarGradingService
{
    Task<GrammarGradingResult> GradeAttemptAsync(
        string userId,
        string lessonId,
        IReadOnlyDictionary<string, JsonElement> answers,
        CancellationToken ct);
}

public sealed record GrammarGradingResult(
    string LessonId,
    int Score,                                          // 0-100 percentage
    int PointsEarned,
    int MaxPoints,
    int CorrectCount,
    int IncorrectCount,
    int MasteryScore,                                   // running EWMA after this attempt
    bool Mastered,                                      // MasteryScore >= masteryThreshold
    IReadOnlyList<GrammarExerciseResult> Exercises);

public sealed record GrammarExerciseResult(
    string ExerciseId,
    string Type,
    bool IsCorrect,
    int PointsEarned,
    int MaxPoints,
    JsonElement? UserAnswer,
    JsonElement? CorrectAnswer,
    string? ExplanationMarkdown);

public sealed class GrammarGradingService(
    LearnerDbContext db,
    IGrammarPolicyService policy,
    ILogger<GrammarGradingService> logger) : IGrammarGradingService
{
    public async Task<GrammarGradingResult> GradeAttemptAsync(
        string userId,
        string lessonId,
        IReadOnlyDictionary<string, JsonElement> answers,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentException.ThrowIfNullOrEmpty(lessonId);
        ArgumentNullException.ThrowIfNull(answers);

        var lesson = await db.GrammarLessons
            .FirstOrDefaultAsync(l => l.Id == lessonId, ct)
            ?? throw new InvalidOperationException($"Grammar lesson '{lessonId}' not found.");

        var exercises = await db.GrammarExercises
            .Where(e => e.LessonId == lessonId)
            .OrderBy(e => e.SortOrder)
            .ToListAsync(ct);

        if (exercises.Count == 0)
        {
            // Legacy lesson with only ExercisesJson — return zero exercises
            // so the attempt still records "completed" without errors.
            logger.LogInformation("Grammar lesson {LessonId} has no typed exercises; recording empty attempt.", lessonId);
        }

        var policyConfig = await policy.GetEffectiveAsync(lesson.ExamTypeCode, ct);
        var results = new List<GrammarExerciseResult>(exercises.Count);
        int points = 0, maxPoints = 0, correct = 0, incorrect = 0;

        // Compute attempt index for the idempotent batch (shared across all exercises).
        var previousAttempts = await db.GrammarExerciseAttempts
            .Where(a => a.UserId == userId && a.LessonId == lessonId)
            .Select(a => a.AttemptIndex)
            .ToListAsync(ct);
        var attemptIndex = (previousAttempts.Count == 0 ? 0 : previousAttempts.Max()) + 1;

        var now = DateTimeOffset.UtcNow;

        foreach (var ex in exercises)
        {
            maxPoints += Math.Max(1, ex.Points);

            answers.TryGetValue(ex.Id, out var userAnswer);
            var (isCorrect, earned, correctAnswerProjection) = GradeOne(ex, userAnswer);
            points += earned;
            if (isCorrect) correct++; else incorrect++;

            // Persist an attempt row for analytics / review queue.
            db.GrammarExerciseAttempts.Add(new GrammarExerciseAttempt
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                LessonId = lessonId,
                ExerciseId = ex.Id,
                UserAnswerJson = userAnswer.ValueKind == JsonValueKind.Undefined
                    ? "null"
                    : userAnswer.GetRawText(),
                IsCorrect = isCorrect,
                PointsEarned = earned,
                AttemptIndex = attemptIndex,
                CreatedAt = now,
            });

            results.Add(new GrammarExerciseResult(
                ExerciseId: ex.Id,
                Type: ex.Type,
                IsCorrect: isCorrect,
                PointsEarned: earned,
                MaxPoints: Math.Max(1, ex.Points),
                UserAnswer: userAnswer.ValueKind == JsonValueKind.Undefined ? null : userAnswer,
                CorrectAnswer: correctAnswerProjection,
                ExplanationMarkdown: string.IsNullOrWhiteSpace(ex.ExplanationMarkdown) ? null : ex.ExplanationMarkdown));
        }

        var percent = maxPoints > 0 ? (int)Math.Round(points * 100.0 / maxPoints) : 100;

        // ── Update progress (EWMA) ────────────────────────────────────────
        var progress = await db.LearnerGrammarProgress
            .FirstOrDefaultAsync(p => p.UserId == userId && p.LessonId == lessonId, ct);
        if (progress is null)
        {
            progress = new LearnerGrammarProgress
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                LessonId = lessonId,
                Status = "in_progress",
                StartedAt = now,
                AnswersJson = "{}",
            };
            db.LearnerGrammarProgress.Add(progress);
        }

        var w = Math.Clamp(policyConfig.EwmaWeight, 0.0, 1.0);
        var priorMastery = progress.MasteryScore;
        var newMastery = progress.AttemptCount == 0
            ? percent
            : (int)Math.Round(priorMastery * (1 - w) + percent * w);
        progress.MasteryScore = newMastery;
        progress.ExerciseScore = percent;
        progress.AttemptCount += 1;
        progress.LastAttemptedAt = now;
        progress.Status = "completed";
        progress.CompletedAt ??= now;

        var answersBlob = new Dictionary<string, JsonElement>();
        foreach (var kv in answers) answersBlob[kv.Key] = kv.Value;
        progress.AnswersJson = JsonSerializer.Serialize(answersBlob);

        await db.SaveChangesAsync(ct);

        var mastered = newMastery >= policyConfig.MasteryThreshold;

        return new GrammarGradingResult(
            LessonId: lessonId,
            Score: percent,
            PointsEarned: points,
            MaxPoints: Math.Max(1, maxPoints),
            CorrectCount: correct,
            IncorrectCount: incorrect,
            MasteryScore: newMastery,
            Mastered: mastered,
            Exercises: results);
    }

    // ── Per-type grading strategies ─────────────────────────────────────────

    private static (bool isCorrect, int points, JsonElement? correctAnswer) GradeOne(
        GrammarExercise ex, JsonElement userAnswer)
    {
        JsonElement? correctProjection = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(ex.CorrectAnswerJson) ? "[]" : ex.CorrectAnswerJson);
            correctProjection = doc.RootElement.Clone();
        }
        catch
        {
            correctProjection = null;
        }

        var accepted = GrammarCanonicaliser.ReadStringArray(ex.AcceptedAnswersJson);

        return ex.Type switch
        {
            "mcq" => GradeMcq(ex, userAnswer, correctProjection),
            "fill_blank" => GradeFillBlank(ex, userAnswer, accepted, correctProjection),
            "error_correction" => GradeFillBlank(ex, userAnswer, accepted, correctProjection),
            "sentence_transformation" => GradeFillBlank(ex, userAnswer, accepted, correctProjection),
            "matching" => GradeMatching(ex, userAnswer, correctProjection),
            _ => GradeFillBlank(ex, userAnswer, accepted, correctProjection),
        };
    }

    private static (bool, int, JsonElement?) GradeMcq(GrammarExercise ex, JsonElement userAnswer, JsonElement? correct)
    {
        var userId = ExtractString(userAnswer);
        var correctId = ExtractFirstString(correct);
        var isCorrect = !string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(correctId) && string.Equals(userId, correctId, StringComparison.OrdinalIgnoreCase);
        return (isCorrect, isCorrect ? Math.Max(1, ex.Points) : 0, correct);
    }

    private static (bool, int, JsonElement?) GradeFillBlank(
        GrammarExercise ex, JsonElement userAnswer, IReadOnlyList<string> accepted, JsonElement? correct)
    {
        var userText = ExtractString(userAnswer);
        var expecteds = ReadAllStrings(correct);
        if (accepted.Count > 0) expecteds.AddRange(accepted);
        if (expecteds.Count == 0) return (false, 0, correct);
        var ok = !string.IsNullOrEmpty(userText) && GrammarCanonicaliser.MatchesAny(userText, expecteds);
        return (ok, ok ? Math.Max(1, ex.Points) : 0, correct);
    }

    private static (bool, int, JsonElement?) GradeMatching(GrammarExercise ex, JsonElement userAnswer, JsonElement? correct)
    {
        // Shape:
        //   correct: [{ "left":"l1","right":"r1" }, ...]
        //   user:    { "l1":"r1", "l2":"r2" }  OR  [{ "left":"l1","right":"r1" }, ...]
        if (correct is null || correct.Value.ValueKind != JsonValueKind.Array)
            return (false, 0, correct);

        var pairs = new List<(string Left, string Right)>();
        foreach (var el in correct.Value.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var left = el.TryGetProperty("left", out var l) ? l.GetString() : null;
            var right = el.TryGetProperty("right", out var r) ? r.GetString() : null;
            if (!string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right))
                pairs.Add((left, right));
        }
        if (pairs.Count == 0) return (false, 0, correct);

        var userMap = NormaliseMatchingAnswer(userAnswer);
        int matched = 0;
        foreach (var (left, right) in pairs)
        {
            if (userMap.TryGetValue(GrammarCanonicaliser.Canonicalise(left), out var userRight)
                && GrammarCanonicaliser.Matches(userRight, right))
                matched++;
        }

        var ratio = (double)matched / pairs.Count;
        var earned = (int)Math.Round(ratio * Math.Max(1, ex.Points));
        // Full credit only when every pair matches.
        var fullyCorrect = matched == pairs.Count;
        return (fullyCorrect, earned, correct);
    }

    private static Dictionary<string, string> NormaliseMatchingAnswer(JsonElement userAnswer)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (userAnswer.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in userAnswer.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                    result[GrammarCanonicaliser.Canonicalise(prop.Name)] = prop.Value.GetString() ?? "";
            }
        }
        else if (userAnswer.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in userAnswer.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var left = el.TryGetProperty("left", out var l) ? l.GetString() : null;
                var right = el.TryGetProperty("right", out var r) ? r.GetString() : null;
                if (!string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right))
                    result[GrammarCanonicaliser.Canonicalise(left)] = right;
            }
        }
        return result;
    }

    private static string? ExtractString(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Undefined || el.ValueKind == JsonValueKind.Null) return null;
        if (el.ValueKind == JsonValueKind.String) return el.GetString();
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString();
        return null;
    }

    private static string? ExtractFirstString(JsonElement? el)
    {
        if (el is null || el.Value.ValueKind == JsonValueKind.Null) return null;
        if (el.Value.ValueKind == JsonValueKind.String) return el.Value.GetString();
        if (el.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.Value.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String) return item.GetString();
        }
        return null;
    }

    private static List<string> ReadAllStrings(JsonElement? el)
    {
        var list = new List<string>();
        if (el is null) return list;
        if (el.Value.ValueKind == JsonValueKind.String)
        {
            var s = el.Value.GetString();
            if (!string.IsNullOrEmpty(s)) list.Add(s);
        }
        else if (el.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.Value.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrEmpty(s)) list.Add(s);
                }
        }
        return list;
    }
}
