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

    Task<ReadingCohortAnalytics> GetCohortAnalyticsAsync(
        string paperId, IReadOnlyCollection<string> userIds, CancellationToken ct);
}

public sealed record ReadingPaperAnalytics(
    string PaperId,
    int TotalAttempts,
    int SubmittedAttempts,
    double MeanRawScore,
    double MeanScaledScore,
    double CompletionRate,
    double AbandonmentRate,
    double AverageTimePerQuestionMs,
    int ManualOverrideCount,
    double ManualOverrideRate,
    IReadOnlyList<ReadingHardestQuestion> HardestQuestions,
    IReadOnlyList<ReadingDistractorHistogramRow> DistractorHistogram,
    IReadOnlyList<ReadingQuestionDiscrimination> Discrimination,
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

/// <summary>Per-question discrimination via the upper/lower 27% method:
/// rank submitted attempts by total raw score, then D = (correct-rate in the
/// top 27%) − (correct-rate in the bottom 27%). Values near 0 (or negative)
/// flag questions that fail to separate strong from weak learners.</summary>
public sealed record ReadingQuestionDiscrimination(
    string QuestionId,
    string PartCode,
    int DisplayOrder,
    double DiscriminationIndex,
    double UpperGroupCorrectRate,
    double LowerGroupCorrectRate,
    int SampleSize);

public sealed record ReadingRiskLabel(
    string QuestionId,
    string Code,           // "too_hard" | "too_easy" | "low_discrimination"
    string Description);

// ── Cohort analytics ────────────────────────────────────────────────────────

public sealed record ReadingCohortAnalytics(
    string PaperId,
    int StudentCount,
    IReadOnlyList<ReadingCohortPartAverage> PartAverages,
    IReadOnlyList<ReadingCohortSkillAverage> SkillAverages,
    IReadOnlyList<ReadingHardestQuestion> HardestQuestions,
    IReadOnlyList<ReadingDistractorHistogramRow> TopDistractors,
    IReadOnlyList<ReadingCohortStudent> Students);

public sealed record ReadingCohortPartAverage(
    string PartCode,
    double AverageRawScore,
    double AverageAccuracyPercent,
    int MaxRawScore);

public sealed record ReadingCohortSkillAverage(
    string Skill,
    double AverageAccuracyPercent,
    int QuestionCount);

public sealed record ReadingCohortStudent(
    string UserId,
    bool HasAttempt,
    int? RawScore,
    int? ScaledScore,
    string GradeLetter,
    string Rag,            // "green" | "amber" | "red" | "none"
    int AssignmentsAssigned,
    int AssignmentsCompleted);

public sealed class ReadingAnalyticsService(LearnerDbContext db) : IReadingAnalyticsService
{
    /// <summary>Min answers per question before we report a stat. Stops a
    /// single learner's bad day from labelling a question "too hard".</summary>
    private const int MinSampleSize = 5;

    /// <summary>Correct-rate ≤ this is flagged "too hard".</summary>
    private const double TooHardThreshold = 0.20;

    /// <summary>Correct-rate ≥ this is flagged "too easy".</summary>
    private const double TooEasyThreshold = 0.95;

    /// <summary>Discrimination index ≤ this is flagged "low_discrimination".</summary>
    private const double LowDiscriminationThreshold = 0.20;

    /// <summary>Fraction of ranked attempts taken as the upper / lower group
    /// for the discrimination index (classic upper-lower 27% rule).</summary>
    private const double DiscriminationGroupFraction = 0.27;

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

        // Discrimination (upper/lower 27%): rank submitted attempts by their
        // total raw score, then per question compare correct-rate between the
        // strongest and weakest groups. Questions that barely separate the two
        // (low / negative index) are added to the risk list.
        var discrimination = ComputeDiscrimination(
            submitted, answersByQuestion, questions, partCodeById);
        foreach (var d in discrimination)
        {
            if (d.SampleSize >= MinSampleSize && d.DiscriminationIndex <= LowDiscriminationThreshold)
            {
                risks.Add(new ReadingRiskLabel(d.QuestionId, "low_discrimination",
                    $"Discrimination index {d.DiscriminationIndex:F2} — weak separation between strong and weak learners."));
            }
        }

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

        // Completion / abandonment.
        var completionRate = attempts.Count == 0
            ? 0.0
            : (double)submitted.Count / attempts.Count;
        var abandonedCount = attempts.Count(a =>
            a.Status is ReadingAttemptStatus.Expired or ReadingAttemptStatus.Abandoned);
        var abandonmentRate = attempts.Count == 0
            ? 0.0
            : (double)abandonedCount / attempts.Count;

        // Average time per question, derived from TotalElapsedMs: per attempt
        // take the furthest cumulative timestamp and divide by answered count,
        // then average across submitted attempts.
        var perAttemptTimes = new List<double>();
        foreach (var grp in answers.GroupBy(a => a.ReadingAttemptId))
        {
            var answered = grp.Count(a => a.TotalElapsedMs.HasValue);
            if (answered == 0) continue;
            var maxTotal = grp.Where(a => a.TotalElapsedMs.HasValue).Max(a => a.TotalElapsedMs!.Value);
            perAttemptTimes.Add((double)maxTotal / answered);
        }
        var avgTimePerQuestion = perAttemptTimes.Count == 0 ? 0.0 : perAttemptTimes.Average();

        // Manual-override frequency among submitted attempts.
        var overrideCount = submitted.Count(a => a.ScoreOverrideRaw.HasValue || a.ScoreOverrideScaled.HasValue);
        var overrideRate = submitted.Count == 0 ? 0.0 : (double)overrideCount / submitted.Count;

        return new ReadingPaperAnalytics(
            PaperId: paperId,
            TotalAttempts: attempts.Count,
            SubmittedAttempts: submitted.Count,
            MeanRawScore: Math.Round(meanRaw, 2),
            MeanScaledScore: Math.Round(meanScaled, 2),
            CompletionRate: Math.Round(completionRate, 4),
            AbandonmentRate: Math.Round(abandonmentRate, 4),
            AverageTimePerQuestionMs: Math.Round(avgTimePerQuestion, 1),
            ManualOverrideCount: overrideCount,
            ManualOverrideRate: Math.Round(overrideRate, 4),
            HardestQuestions: hardestTop,
            DistractorHistogram: histogram,
            Discrimination: discrimination,
            RiskLabels: risks);
    }

    /// <summary>Compute per-question discrimination via the upper/lower 27%
    /// method. Returns an empty list when there are too few submitted attempts
    /// to form meaningful groups.</summary>
    internal static IReadOnlyList<ReadingQuestionDiscrimination> ComputeDiscrimination(
        IReadOnlyList<ReadingAttempt> submitted,
        IReadOnlyDictionary<string, List<ReadingAnswer>> answersByQuestion,
        IReadOnlyList<ReadingQuestion> questions,
        IReadOnlyDictionary<string, string> partCodeById)
    {
        var ranked = submitted
            .Where(a => a.RawScore.HasValue)
            .OrderByDescending(a => a.RawScore!.Value)
            .Select(a => a.Id)
            .ToList();
        var groupSize = (int)Math.Floor(ranked.Count * DiscriminationGroupFraction);
        if (groupSize < 1) return Array.Empty<ReadingQuestionDiscrimination>();

        var upperIds = ranked.Take(groupSize).ToHashSet(StringComparer.Ordinal);
        var lowerIds = ranked.Skip(ranked.Count - groupSize).ToHashSet(StringComparer.Ordinal);

        var result = new List<ReadingQuestionDiscrimination>();
        foreach (var q in questions)
        {
            if (!answersByQuestion.TryGetValue(q.Id, out var rows)) continue;
            var upper = rows.Where(r => upperIds.Contains(r.ReadingAttemptId)).ToList();
            var lower = rows.Where(r => lowerIds.Contains(r.ReadingAttemptId)).ToList();
            if (upper.Count == 0 || lower.Count == 0) continue;

            var upperRate = (double)upper.Count(r => r.IsCorrect == true) / upper.Count;
            var lowerRate = (double)lower.Count(r => r.IsCorrect == true) / lower.Count;
            result.Add(new ReadingQuestionDiscrimination(
                QuestionId: q.Id,
                PartCode: partCodeById.TryGetValue(q.ReadingPartId, out var pc) ? pc : "?",
                DisplayOrder: q.DisplayOrder,
                DiscriminationIndex: Math.Round(upperRate - lowerRate, 3),
                UpperGroupCorrectRate: Math.Round(upperRate, 3),
                LowerGroupCorrectRate: Math.Round(lowerRate, 3),
                SampleSize: upper.Count + lower.Count));
        }
        return result.OrderBy(d => d.DiscriminationIndex).ToList();
    }

    public async Task<ReadingCohortAnalytics> GetCohortAnalyticsAsync(
        string paperId, IReadOnlyCollection<string> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.Ordinal).ToList();

        // Each student's best submitted attempt (highest EFFECTIVE scaled
        // score, override wins) is the canonical record for cohort stats.
        var attempts = ids.Count == 0
            ? new List<ReadingAttempt>()
            : await db.ReadingAttempts.AsNoTracking()
                .Where(a => a.PaperId == paperId
                    && a.Status == ReadingAttemptStatus.Submitted
                    && ids.Contains(a.UserId))
                .ToListAsync(ct);

        var bestByUser = attempts
            .GroupBy(a => a.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(a => EffectiveScaled(a) ?? int.MinValue)
                      .ThenByDescending(a => a.SubmittedAt)
                      .First(),
                StringComparer.Ordinal);

        var bestAttemptIds = bestByUser.Values.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

        var parts = await db.ReadingParts.AsNoTracking()
            .Where(p => p.PaperId == paperId)
            .ToListAsync(ct);
        var partIds = parts.Select(p => p.Id).ToList();
        var questions = await db.ReadingQuestions.AsNoTracking()
            .Where(q => partIds.Contains(q.ReadingPartId))
            .ToListAsync(ct);
        var partCodeById = parts.ToDictionary(p => p.Id, p => p.PartCode.ToString());
        var questionById = questions.ToDictionary(q => q.Id, StringComparer.Ordinal);

        var answers = bestAttemptIds.Count == 0
            ? new List<ReadingAnswer>()
            : await db.ReadingAnswers.AsNoTracking()
                .Where(a => bestAttemptIds.Contains(a.ReadingAttemptId))
                .ToListAsync(ct);

        // Part averages across the cohort's best attempts.
        var partAverages = parts
            .OrderBy(p => p.PartCode)
            .Select(part =>
            {
                var partCode = part.PartCode.ToString();
                var partQuestionIds = questions
                    .Where(q => q.ReadingPartId == part.Id)
                    .Select(q => q.Id)
                    .ToHashSet(StringComparer.Ordinal);
                var maxRaw = questions.Where(q => q.ReadingPartId == part.Id).Sum(q => q.Points);
                var perAttempt = answers
                    .Where(a => partQuestionIds.Contains(a.ReadingQuestionId))
                    .GroupBy(a => a.ReadingAttemptId)
                    .Select(g => new
                    {
                        Raw = g.Sum(a => a.PointsEarned),
                        Accuracy = g.Any() ? 100.0 * g.Count(a => a.IsCorrect == true) / g.Count() : 0.0,
                    })
                    .ToList();
                return new ReadingCohortPartAverage(
                    PartCode: partCode,
                    AverageRawScore: perAttempt.Count == 0 ? 0.0 : Math.Round(perAttempt.Average(x => x.Raw), 2),
                    AverageAccuracyPercent: perAttempt.Count == 0 ? 0.0 : Math.Round(perAttempt.Average(x => x.Accuracy), 1),
                    MaxRawScore: maxRaw);
            })
            .ToList();

        // Skill averages across the cohort.
        var skillAverages = answers
            .Select(a => new
            {
                Answer = a,
                Skill = questionById.TryGetValue(a.ReadingQuestionId, out var q) && !string.IsNullOrWhiteSpace(q.SkillTag)
                    ? q.SkillTag!
                    : (questionById.TryGetValue(a.ReadingQuestionId, out var q2) ? q2.QuestionType.ToString() : "unknown"),
            })
            .GroupBy(x => x.Skill)
            .Select(g => new ReadingCohortSkillAverage(
                Skill: g.Key,
                AverageAccuracyPercent: Math.Round(100.0 * g.Count(x => x.Answer.IsCorrect == true) / g.Count(), 1),
                QuestionCount: g.Count()))
            .OrderByDescending(s => s.QuestionCount)
            .ThenBy(s => s.Skill)
            .ToList();

        // Cohort hardest questions + top distractors (best attempts only).
        var answersByQuestion = answers.GroupBy(a => a.ReadingQuestionId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var hardest = new List<ReadingHardestQuestion>();
        foreach (var q in questions)
        {
            if (!answersByQuestion.TryGetValue(q.Id, out var rows) || rows.Count == 0) continue;
            var correct = rows.Count(r => r.IsCorrect == true);
            hardest.Add(new ReadingHardestQuestion(
                q.Id,
                partCodeById.TryGetValue(q.ReadingPartId, out var pc) ? pc : "?",
                q.DisplayOrder,
                Truncate(q.Stem, 200),
                rows.Count, correct, (double)correct / rows.Count));
        }
        var hardestTop = hardest.OrderBy(h => h.CorrectRate).Take(10).ToList();

        var topDistractors = answers
            .Where(a => a.IsCorrect != true && a.SelectedDistractorCategory.HasValue)
            .GroupBy(a => new { a.ReadingQuestionId, Category = a.SelectedDistractorCategory!.Value })
            .Select(g => new ReadingDistractorHistogramRow(
                g.Key.ReadingQuestionId,
                g.Key.Category,
                ResolveOptionKey(questionById.GetValueOrDefault(g.Key.ReadingQuestionId), g.Key.Category),
                g.Count()))
            .OrderByDescending(r => r.SelectedCount)
            .Take(20)
            .ToList();

        // Assignment completion per student (this paper).
        var assignments = ids.Count == 0
            ? new List<ReadingAssignment>()
            : await db.ReadingAssignments.AsNoTracking()
                .Where(x => x.PaperId == paperId && ids.Contains(x.AssignedToUserId))
                .ToListAsync(ct);
        var assignmentsByUser = assignments
            .GroupBy(x => x.AssignedToUserId)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // Per-student RAG (Green = pass band, Amber = one band below pass,
        // Red = below) — derived purely from OetScoring grade letters so no
        // scaled threshold is ever inlined here.
        var students = ids
            .Select(uid =>
            {
                bestByUser.TryGetValue(uid, out var best);
                var scaled = best is null ? null : EffectiveScaled(best);
                var raw = best is null ? null : EffectiveRaw(best);
                var letter = scaled is int s ? OetScoring.OetGradeLetterFromScaled(s) : "—";
                var rag = ResolveRag(scaled);
                assignmentsByUser.TryGetValue(uid, out var userAssignments);
                return new ReadingCohortStudent(
                    UserId: uid,
                    HasAttempt: best is not null,
                    RawScore: raw,
                    ScaledScore: scaled,
                    GradeLetter: letter,
                    Rag: rag,
                    AssignmentsAssigned: userAssignments?.Count ?? 0,
                    AssignmentsCompleted: userAssignments?.Count(a => a.Status == "completed") ?? 0);
            })
            .ToList();

        return new ReadingCohortAnalytics(
            PaperId: paperId,
            StudentCount: ids.Count,
            PartAverages: partAverages,
            SkillAverages: skillAverages,
            HardestQuestions: hardestTop,
            TopDistractors: topDistractors,
            Students: students);
    }

    private static int? EffectiveScaled(ReadingAttempt a)
        => (a.ScoreOverrideRaw.HasValue || a.ScoreOverrideScaled.HasValue) ? a.ScoreOverrideScaled : a.ScaledScore;

    private static int? EffectiveRaw(ReadingAttempt a)
        => (a.ScoreOverrideRaw.HasValue || a.ScoreOverrideScaled.HasValue) ? a.ScoreOverrideRaw : a.RawScore;

    /// <summary>Green when the effective scaled score is a pass-band grade
    /// (A/B), Amber when it is the band immediately below pass (C+), Red for
    /// anything lower, and "none" when the student has no graded attempt. All
    /// boundaries come from OetScoring grade letters — never a literal score.</summary>
    private static string ResolveRag(int? scaled)
    {
        if (scaled is not int s) return "none";
        var letter = OetScoring.OetGradeLetterFromScaled(s);
        if (OetScoring.IsListeningReadingPassByScaled(s)) return "green"; // A / B
        return letter == "C+" ? "amber" : "red";
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
