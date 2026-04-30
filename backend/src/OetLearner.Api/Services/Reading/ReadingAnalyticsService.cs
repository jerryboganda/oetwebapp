using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Reading Analytics Service — Phase 5
//
// Aggregates ReadingAttempt + ReadingAnswer rows into per-paper insights:
//   • Hardest questions (lowest correct rate, min sample size enforced)
//   • Distractor histogram per question (which trap traps the most learners)
//   • Risk labels (high error rate / low discrimination) — heuristic, not
//     diagnostic. Admins use these to triage the review queue.
//
// All endpoints are admin-only; learner DTOs never expose these aggregations.
// ═════════════════════════════════════════════════════════════════════════════

public interface IReadingAnalyticsService
{
    Task<ReadingPaperAnalytics> GetPaperAnalyticsAsync(string paperId, CancellationToken ct);
}

public sealed record ReadingPaperAnalytics(
    string PaperId,
    int TotalAttempts,
    int SubmittedAttempts,
    double MeanRawScore,
    double MeanScaledScore,
    IReadOnlyList<ReadingHardestQuestion> HardestQuestions,
    IReadOnlyList<ReadingDistractorHistogramRow> DistractorHistogram,
    IReadOnlyList<ReadingRiskLabel> RiskLabels);

public sealed record ReadingHardestQuestion(
    string QuestionId,
    string PartCode,
    int DisplayOrder,
    string Stem,
    int AnswerCount,
    int CorrectCount,
    double CorrectRate);

public sealed record ReadingDistractorHistogramRow(
    string QuestionId,
    ReadingDistractorCategory Category,
    string OptionKey,
    int SelectedCount);

public sealed record ReadingRiskLabel(
    string QuestionId,
    string Code,           // "too_hard" | "too_easy" | "low_discrimination"
    string Description);

public sealed class ReadingAnalyticsService(LearnerDbContext db) : IReadingAnalyticsService
{
    /// <summary>Min answers per question before we report a stat. Stops a
    /// single learner's bad day from labelling a question "too hard".</summary>
    private const int MinSampleSize = 5;

    /// <summary>Correct-rate ≤ this is flagged "too hard".</summary>
    private const double TooHardThreshold = 0.20;

    /// <summary>Correct-rate ≥ this is flagged "too easy".</summary>
    private const double TooEasyThreshold = 0.95;

    public async Task<ReadingPaperAnalytics> GetPaperAnalyticsAsync(string paperId, CancellationToken ct)
    {
        // Submitted attempts only — in-progress data is noise.
        var attempts = await db.ReadingAttempts.AsNoTracking()
            .Where(a => a.PaperId == paperId)
            .ToListAsync(ct);
        var submitted = attempts.Where(a => a.Status == ReadingAttemptStatus.Submitted).ToList();
        var submittedIds = submitted.Select(a => a.Id).ToHashSet();

        var meanRaw = submitted.Count == 0
            ? 0.0
            : submitted.Where(a => a.RawScore.HasValue).Select(a => (double)a.RawScore!.Value).DefaultIfEmpty(0).Average();
        var meanScaled = submitted.Count == 0
            ? 0.0
            : submitted.Where(a => a.ScaledScore.HasValue).Select(a => (double)a.ScaledScore!.Value).DefaultIfEmpty(0).Average();

        // Question lookup (with part code for nicer labels)
        var parts = await db.ReadingParts.AsNoTracking()
            .Where(p => p.PaperId == paperId)
            .ToListAsync(ct);
        var partIds = parts.Select(p => p.Id).ToList();
        var questions = await db.ReadingQuestions.AsNoTracking()
            .Where(q => partIds.Contains(q.ReadingPartId))
            .ToListAsync(ct);
        var partCodeById = parts.ToDictionary(p => p.Id, p => p.PartCode.ToString());

        // Per-question stats
        var answers = await db.ReadingAnswers.AsNoTracking()
            .Where(a => submittedIds.Contains(a.ReadingAttemptId))
            .ToListAsync(ct);
        var answersByQuestion = answers.GroupBy(a => a.ReadingQuestionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var hardest = new List<ReadingHardestQuestion>();
        var risks = new List<ReadingRiskLabel>();
        foreach (var q in questions)
        {
            if (!answersByQuestion.TryGetValue(q.Id, out var rows) || rows.Count < MinSampleSize)
                continue;
            var correct = rows.Count(r => r.IsCorrect == true);
            var rate = (double)correct / rows.Count;
            hardest.Add(new ReadingHardestQuestion(
                q.Id,
                partCodeById.TryGetValue(q.ReadingPartId, out var pc) ? pc : "?",
                q.DisplayOrder,
                Truncate(q.Stem, 200),
                rows.Count, correct, rate));

            if (rate <= TooHardThreshold)
                risks.Add(new ReadingRiskLabel(q.Id, "too_hard",
                    $"Only {correct}/{rows.Count} learners answered correctly ({rate:P0})."));
            else if (rate >= TooEasyThreshold)
                risks.Add(new ReadingRiskLabel(q.Id, "too_easy",
                    $"{correct}/{rows.Count} learners ({rate:P0}) answered correctly — consider raising difficulty."));
        }

        // Hardest-first sort, take top 10
        var hardestTop = hardest.OrderBy(h => h.CorrectRate).Take(10).ToList();

        // Distractor histogram — only wrong answers with a category set
        var histogram = answers
            .Where(a => a.IsCorrect != true && a.SelectedDistractorCategory.HasValue)
            .GroupBy(a => new { a.ReadingQuestionId, Category = a.SelectedDistractorCategory!.Value })
            .Select(g => new ReadingDistractorHistogramRow(
                g.Key.ReadingQuestionId,
                g.Key.Category,
                ResolveOptionKey(questions.FirstOrDefault(q => q.Id == g.Key.ReadingQuestionId), g.Key.Category),
                g.Count()))
            .OrderByDescending(r => r.SelectedCount)
            .Take(50)
            .ToList();

        return new ReadingPaperAnalytics(
            PaperId: paperId,
            TotalAttempts: attempts.Count,
            SubmittedAttempts: submitted.Count,
            MeanRawScore: Math.Round(meanRaw, 2),
            MeanScaledScore: Math.Round(meanScaled, 2),
            HardestQuestions: hardestTop,
            DistractorHistogram: histogram,
            RiskLabels: risks);
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");

    /// <summary>Best-effort lookup: which option key carries a given
    /// distractor category for this question? Returns "?" if the question
    /// or its metadata can't be resolved.</summary>
    private static string ResolveOptionKey(ReadingQuestion? q, ReadingDistractorCategory cat)
    {
        if (q is null || string.IsNullOrWhiteSpace(q.OptionDistractorsJson)) return "?";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(q.OptionDistractorsJson);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return "?";
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String
                    && Enum.TryParse<ReadingDistractorCategory>(prop.Value.GetString(), ignoreCase: true, out var c)
                    && c == cat)
                {
                    return prop.Name;
                }
            }
        }
        catch (System.Text.Json.JsonException) { }
        return "?";
    }
}
