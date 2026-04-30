using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin analytics for the Reading authoring and learner attempt subsystem.
/// This endpoint is read-only and deliberately derives pass/fail from the
/// canonical OET scoring service rather than from inline thresholds.
/// </summary>
public static class ReadingAnalyticsAdminEndpoints
{
    public static IEndpointRouteBuilder MapReadingAnalyticsAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        admin.MapGet("/reading/analytics", async (
            [FromQuery] int? days,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var analytics = await BuildAnalyticsAsync(db, days, ct);
            return Results.Ok(analytics);
        }).WithAdminRead("AdminContentRead");

        return app;
    }

    public static async Task<ReadingAdminAnalyticsDto> BuildAnalyticsAsync(
        LearnerDbContext db,
        int? days,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var windowDays = Math.Clamp(days ?? 30, 7, 365);
        var since = now.AddDays(-windowDays);

        var papers = await db.ContentPapers.AsNoTracking()
            .Where(p => p.SubtestCode == "reading")
            .ToListAsync(ct);

        if (papers.Count == 0)
        {
            return new ReadingAdminAnalyticsDto(
                now,
                windowDays,
                new ReadingAdminAnalyticsSummaryDto(0, 0, 0, 0, 0, 0, 0, null, null, null, null),
                Array.Empty<ReadingPaperAnalyticsDto>(),
                EmptyPartBreakdown(),
                Array.Empty<ReadingSkillAnalyticsDto>(),
                Array.Empty<ReadingQuestionAnalyticsDto>(),
                Array.Empty<ReadingModeAnalyticsDto>(),
                new[]
                {
                    new ReadingActionInsightDto(
                        "no_reading_papers",
                        "Create the first Reading paper",
                        "No Reading content papers exist yet, so analytics cannot accumulate evidence.",
                        "warning"),
                });
        }

        var paperIds = papers.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var parts = await db.ReadingParts.AsNoTracking()
            .Where(p => paperIds.Contains(p.PaperId))
            .ToListAsync(ct);
        var partIds = parts.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var questions = partIds.Count == 0
            ? new List<ReadingQuestion>()
            : await db.ReadingQuestions.AsNoTracking()
                .Where(q => partIds.Contains(q.ReadingPartId))
                .ToListAsync(ct);
        var attempts = await db.ReadingAttempts.AsNoTracking()
            .Where(a => paperIds.Contains(a.PaperId)
                && ((a.Status == ReadingAttemptStatus.Submitted && a.SubmittedAt.HasValue && a.SubmittedAt.Value >= since)
                    || (a.Status != ReadingAttemptStatus.Submitted && a.StartedAt >= since)))
            .ToListAsync(ct);

        var submittedAttempts = attempts
            .Where(a => a.Status == ReadingAttemptStatus.Submitted && a.SubmittedAt.HasValue && a.SubmittedAt.Value >= since)
            .ToList();
        var activeAttempts = attempts
            .Where(a => a.Status == ReadingAttemptStatus.InProgress && a.StartedAt >= since)
            .ToList();
        var submittedAttemptIds = submittedAttempts.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var answers = submittedAttemptIds.Count == 0
            ? new List<ReadingAnswer>()
            : await db.ReadingAnswers.AsNoTracking()
                .Where(a => submittedAttemptIds.Contains(a.ReadingAttemptId))
                .ToListAsync(ct);

        var partsByPaper = parts
            .GroupBy(p => p.PaperId)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var partById = parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var questionsByPart = questions
            .GroupBy(q => q.ReadingPartId)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var knownQuestionIds = questions.Select(q => q.Id).ToHashSet(StringComparer.Ordinal);
        var analyticAnswers = answers
            .Where(a => knownQuestionIds.Contains(a.ReadingQuestionId))
            .ToList();
        var questionOpportunityMetrics = BuildQuestionOpportunityMetrics(submittedAttempts, questions, partById, analyticAnswers);
        var answersByQuestion = questionOpportunityMetrics.InScopeAnswers
            .GroupBy(a => a.ReadingQuestionId)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var submittedAttemptsByPaper = submittedAttempts
            .GroupBy(a => a.PaperId)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var paperAnalytics = papers
            .OrderBy(p => p.Title)
            .Select(paper => BuildPaperAnalytics(
                paper,
                partsByPaper.GetValueOrDefault(paper.Id) ?? new List<ReadingPart>(),
                questionsByPart,
                attempts.Where(a => a.PaperId == paper.Id).ToList(),
                submittedAttemptsByPaper.GetValueOrDefault(paper.Id) ?? new List<ReadingAttempt>()))
            .ToList();

        var partBreakdown = BuildPartBreakdown(parts, questions, questionOpportunityMetrics.OpportunityCountsByQuestion, answersByQuestion);
        var skillBreakdown = BuildSkillBreakdown(questions, questionOpportunityMetrics.OpportunityCountsByQuestion, answersByQuestion);
        var hardestQuestions = BuildHardestQuestions(papers, questions, partById, questionOpportunityMetrics.OpportunityCountsByQuestion, answersByQuestion);
        var modeBreakdown = BuildModeBreakdown(attempts);

        var scaledScores = submittedAttempts
            .Select(GetScaledScore)
            .Where(score => score.HasValue)
            .Select(score => (double)score!.Value)
            .ToList();
        var passedCount = submittedAttempts.Count(a =>
        {
            var scaled = GetScaledScore(a);
            return scaled.HasValue && OetScoring.IsListeningReadingPassByScaled(scaled.Value);
        });
        var passEligibleCount = submittedAttempts.Count(a => GetScaledScore(a).HasValue);
        var totalOpportunities = questions.Sum(q => OpportunityCount(q, questionOpportunityMetrics.OpportunityCountsByQuestion));
        var totalAnswered = questionOpportunityMetrics.InScopeAnswers.Count;

        var summary = new ReadingAdminAnalyticsSummaryDto(
            TotalPapers: papers.Count,
            PublishedPapers: papers.Count(p => p.Status == ContentStatus.Published),
            ExamReadyPapers: paperAnalytics.Count(p => p.IsExamReady),
            AuthoredQuestions: questions.Count,
            TotalAttempts: attempts.Count,
            SubmittedAttempts: submittedAttempts.Count,
            ActiveAttempts: activeAttempts.Count,
            AverageRawScore: AverageOrNull(submittedAttempts.Where(IsCanonicalScoreEligible).Where(a => a.RawScore.HasValue).Select(a => (double)a.RawScore!.Value)),
            AverageScaledScore: AverageOrNull(scaledScores),
            PassRatePercent: Percent(passedCount, passEligibleCount),
            UnansweredRatePercent: Percent(Math.Max(0, totalOpportunities - totalAnswered), totalOpportunities));

        return new ReadingAdminAnalyticsDto(
            now,
            windowDays,
            summary,
            paperAnalytics,
            partBreakdown,
            skillBreakdown,
            hardestQuestions,
            modeBreakdown,
            BuildActionInsights(paperAnalytics, partBreakdown, skillBreakdown, hardestQuestions, summary));
    }

    private static IReadOnlyList<ReadingPartAnalyticsDto> EmptyPartBreakdown() => new[]
    {
        new ReadingPartAnalyticsDto("A", 0, 0, 0, 0, null),
        new ReadingPartAnalyticsDto("B", 0, 0, 0, 0, null),
        new ReadingPartAnalyticsDto("C", 0, 0, 0, 0, null),
    };

    private static ReadingPaperAnalyticsDto BuildPaperAnalytics(
        ContentPaper paper,
        IReadOnlyList<ReadingPart> paperParts,
        IReadOnlyDictionary<string, List<ReadingQuestion>> questionsByPart,
        IReadOnlyList<ReadingAttempt> paperAttempts,
        IReadOnlyList<ReadingAttempt> submittedAttempts)
    {
        var questions = paperParts
            .SelectMany(part => questionsByPart.GetValueOrDefault(part.Id) ?? new List<ReadingQuestion>())
            .ToList();
        var partACount = questions.Count(q => paperParts.Any(p => p.Id == q.ReadingPartId && p.PartCode == ReadingPartCode.A));
        var partBCount = questions.Count(q => paperParts.Any(p => p.Id == q.ReadingPartId && p.PartCode == ReadingPartCode.B));
        var partCCount = questions.Count(q => paperParts.Any(p => p.Id == q.ReadingPartId && p.PartCode == ReadingPartCode.C));
        var totalPoints = questions.Sum(q => q.Points);
        var isCanonicalShape = partACount == 20 && partBCount == 6 && partCCount == 16 && totalPoints == OetScoring.ListeningReadingRawMax;
        var scaledScores = submittedAttempts
            .Select(GetScaledScore)
            .Where(score => score.HasValue)
            .Select(score => score!.Value)
            .ToList();
        var passedCount = scaledScores.Count(OetScoring.IsListeningReadingPassByScaled);
        var completionSeconds = submittedAttempts
            .Where(a => a.SubmittedAt.HasValue)
            .Select(a => Math.Max(0, (a.SubmittedAt!.Value - a.StartedAt).TotalSeconds));

        return new ReadingPaperAnalyticsDto(
            paper.Id,
            paper.Title,
            paper.Status.ToString(),
            paper.Difficulty,
            questions.Count,
            totalPoints,
            partACount,
            partBCount,
            partCCount,
            isCanonicalShape,
            isCanonicalShape && paper.Status == ContentStatus.Published,
            paperAttempts.Count,
            submittedAttempts.Count,
            AverageOrNull(submittedAttempts.Where(IsCanonicalScoreEligible).Where(a => a.RawScore.HasValue).Select(a => (double)a.RawScore!.Value)),
            AverageOrNull(scaledScores.Select(score => (double)score)),
            Percent(passedCount, scaledScores.Count),
            AverageOrNull(completionSeconds));
    }

    private static IReadOnlyList<ReadingPartAnalyticsDto> BuildPartBreakdown(
        IReadOnlyList<ReadingPart> parts,
        IReadOnlyList<ReadingQuestion> questions,
        IReadOnlyDictionary<string, int> opportunityCountsByQuestion,
        IReadOnlyDictionary<string, List<ReadingAnswer>> answersByQuestion)
    {
        return new[] { ReadingPartCode.A, ReadingPartCode.B, ReadingPartCode.C }
            .Select(code =>
            {
                var partIds = parts.Where(p => p.PartCode == code).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
                var partQuestions = questions.Where(q => partIds.Contains(q.ReadingPartId)).ToList();
                var opportunities = partQuestions.Sum(q => OpportunityCount(q, opportunityCountsByQuestion));
                var partAnswers = partQuestions
                    .SelectMany(q => answersByQuestion.GetValueOrDefault(q.Id) ?? new List<ReadingAnswer>())
                    .ToList();
                var correct = partAnswers.Count(a => a.IsCorrect == true);
                var unanswered = Math.Max(0, opportunities - partAnswers.Count);
                return new ReadingPartAnalyticsDto(
                    code.ToString(),
                    partQuestions.Count,
                    opportunities,
                    correct,
                    unanswered,
                    Percent(correct, opportunities));
            })
            .ToList();
    }

    private static IReadOnlyList<ReadingSkillAnalyticsDto> BuildSkillBreakdown(
        IReadOnlyList<ReadingQuestion> questions,
        IReadOnlyDictionary<string, int> opportunityCountsByQuestion,
        IReadOnlyDictionary<string, List<ReadingAnswer>> answersByQuestion)
    {
        return questions
            .GroupBy(q => string.IsNullOrWhiteSpace(q.SkillTag) ? "Unspecified" : q.SkillTag.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var skillQuestions = group.ToList();
                var opportunities = skillQuestions.Sum(q => OpportunityCount(q, opportunityCountsByQuestion));
                var skillAnswers = skillQuestions
                    .SelectMany(q => answersByQuestion.GetValueOrDefault(q.Id) ?? new List<ReadingAnswer>())
                    .ToList();
                var correct = skillAnswers.Count(a => a.IsCorrect == true);
                var unanswered = Math.Max(0, opportunities - skillAnswers.Count);
                return new ReadingSkillAnalyticsDto(
                    group.Key,
                    skillQuestions.Count,
                    opportunities,
                    correct,
                    unanswered,
                    Percent(correct, opportunities));
            })
            .OrderBy(s => s.AccuracyPercent ?? double.MaxValue)
            .ThenByDescending(s => s.Opportunities)
            .ThenBy(s => s.Label)
            .ToList();
    }

    private static IReadOnlyList<ReadingQuestionAnalyticsDto> BuildHardestQuestions(
        IReadOnlyList<ContentPaper> papers,
        IReadOnlyList<ReadingQuestion> questions,
        IReadOnlyDictionary<string, ReadingPart> partById,
        IReadOnlyDictionary<string, int> opportunityCountsByQuestion,
        IReadOnlyDictionary<string, List<ReadingAnswer>> answersByQuestion)
    {
        var paperById = papers.ToDictionary(p => p.Id, StringComparer.Ordinal);
        return questions
            .Select(q =>
            {
                var part = partById.GetValueOrDefault(q.ReadingPartId);
                var paper = part is null ? null : paperById.GetValueOrDefault(part.PaperId);
                var opportunities = OpportunityCount(q, opportunityCountsByQuestion);
                var questionAnswers = answersByQuestion.GetValueOrDefault(q.Id) ?? new List<ReadingAnswer>();
                var correct = questionAnswers.Count(a => a.IsCorrect == true);
                var unanswered = Math.Max(0, opportunities - questionAnswers.Count);
                return new ReadingQuestionAnalyticsDto(
                    paper?.Id ?? string.Empty,
                    paper?.Title ?? "Unknown paper",
                    q.Id,
                    part?.PartCode.ToString() ?? "?",
                    $"Part {part?.PartCode.ToString() ?? "?"} Q{q.DisplayOrder}",
                    q.DisplayOrder,
                    q.QuestionType.ToString(),
                    string.IsNullOrWhiteSpace(q.SkillTag) ? "Unspecified" : q.SkillTag.Trim(),
                    q.Stem,
                    opportunities,
                    questionAnswers.Count,
                    correct,
                    unanswered,
                    Percent(correct, opportunities));
            })
            .Where(q => q.Opportunities > 0)
            .OrderBy(q => q.AccuracyPercent ?? double.MaxValue)
            .ThenByDescending(q => q.AnsweredCount)
            .ThenByDescending(q => q.Opportunities)
            .ThenByDescending(q => q.UnansweredCount)
            .ThenBy(q => q.PaperTitle)
            .ThenBy(q => q.DisplayOrder)
            .Take(12)
            .ToList();
    }

    private static IReadOnlyList<ReadingModeAnalyticsDto> BuildModeBreakdown(IReadOnlyList<ReadingAttempt> attempts)
    {
        return attempts
            .GroupBy(a => a.Mode)
            .Select(group =>
            {
                var modeAttempts = group.ToList();
                var submitted = modeAttempts.Where(a => a.Status == ReadingAttemptStatus.Submitted).ToList();
                var scaledScores = submitted
                    .Select(GetScaledScore)
                    .Where(score => score.HasValue)
                    .Select(score => score!.Value)
                    .ToList();
                return new ReadingModeAnalyticsDto(
                    group.Key.ToString(),
                    modeAttempts.Count,
                    submitted.Count,
                    AverageOrNull(submitted.Where(IsCanonicalScoreEligible).Where(a => a.RawScore.HasValue).Select(a => (double)a.RawScore!.Value)),
                    AverageOrNull(scaledScores.Select(score => (double)score)),
                    Percent(scaledScores.Count(OetScoring.IsListeningReadingPassByScaled), scaledScores.Count));
            })
            .OrderByDescending(m => m.AttemptCount)
            .ThenBy(m => m.Mode)
            .ToList();
    }

    private static IReadOnlyList<ReadingActionInsightDto> BuildActionInsights(
        IReadOnlyList<ReadingPaperAnalyticsDto> papers,
        IReadOnlyList<ReadingPartAnalyticsDto> parts,
        IReadOnlyList<ReadingSkillAnalyticsDto> skills,
        IReadOnlyList<ReadingQuestionAnalyticsDto> hardestQuestions,
        ReadingAdminAnalyticsSummaryDto summary)
    {
        var insights = new List<ReadingActionInsightDto>();
        var incompletePapers = papers.Count(p => !p.IsCanonicalShapeComplete);
        if (incompletePapers > 0)
        {
            insights.Add(new ReadingActionInsightDto(
                "content_shape",
                "Finish canonical Reading structures",
                $"{incompletePapers} Reading paper(s) do not yet match the 20 + 6 + 16 item structure.",
                "warning"));
        }

            if (summary.SubmittedAttempts == 0)
            {
                insights.Add(new ReadingActionInsightDto(
                "no_submitted_attempts",
                "Collect submitted Reading attempts",
                "Published Reading content exists, but this window has no submitted attempts to analyse yet.",
                "warning"));
            }

        var weakestPart = parts
            .Where(p => p.Opportunities > 0 && p.AccuracyPercent.HasValue)
            .OrderBy(p => p.AccuracyPercent)
            .FirstOrDefault();
        if (weakestPart is not null)
        {
            insights.Add(new ReadingActionInsightDto(
                $"part_{weakestPart.PartCode.ToLowerInvariant()}",
                $"Review Part {weakestPart.PartCode} performance",
                $"Part {weakestPart.PartCode} is currently the lowest accuracy section at {weakestPart.AccuracyPercent}%.",
                weakestPart.AccuracyPercent < 55 ? "danger" : "warning"));
        }

        var weakestSkill = skills
            .Where(s => s.Opportunities > 0 && s.AccuracyPercent.HasValue)
            .OrderBy(s => s.AccuracyPercent)
            .FirstOrDefault();
        if (weakestSkill is not null)
        {
            insights.Add(new ReadingActionInsightDto(
                $"skill_{SanitiseId(weakestSkill.Label)}",
                $"Target {weakestSkill.Label}",
                $"{weakestSkill.UnansweredCount} unanswered or missed opportunity signal(s) are concentrated around this skill.",
                weakestSkill.AccuracyPercent < 55 ? "danger" : "warning"));
        }

        var hardest = hardestQuestions.FirstOrDefault(q => q.AccuracyPercent.HasValue);
        if (hardest is not null)
        {
            insights.Add(new ReadingActionInsightDto(
                $"question_{SanitiseId(hardest.QuestionId)}",
                "Audit the hardest item",
                $"{hardest.Label} in {hardest.PaperTitle} has {hardest.AccuracyPercent}% accuracy across {hardest.Opportunities} opportunity/opportunities.",
                "warning"));
        }

        if (summary.PassRatePercent.HasValue && summary.PassRatePercent < 50)
        {
            insights.Add(new ReadingActionInsightDto(
                "pass_rate",
                "Investigate score readiness",
                $"The current window pass rate is {summary.PassRatePercent}%, so learner readiness needs closer review.",
                "danger"));
        }

        if (insights.Count == 0)
        {
            insights.Add(new ReadingActionInsightDto(
                "healthy_window",
                "Reading analytics look stable",
                "No urgent content shape or performance risks were detected in this evidence window.",
                "success"));
        }

        return insights.Take(5).ToList();
    }

    private static int OpportunityCount(
        ReadingQuestion question,
        IReadOnlyDictionary<string, int> opportunityCountsByQuestion)
    {
        return opportunityCountsByQuestion.TryGetValue(question.Id, out var count) ? count : 0;
    }

    private static int? GetScaledScore(ReadingAttempt attempt)
    {
        if (!IsCanonicalScoreEligible(attempt)) return null;
        if (attempt.RawScore.HasValue) return OetScoring.OetRawToScaled(attempt.RawScore.Value);
        return attempt.ScaledScore;
    }

    private static bool IsCanonicalScoreEligible(ReadingAttempt attempt)
    {
        return attempt.Mode is ReadingAttemptMode.Exam or ReadingAttemptMode.Learning
            && attempt.MaxRawScore == OetScoring.ListeningReadingRawMax;
    }

    private static ReadingQuestionOpportunityMetrics BuildQuestionOpportunityMetrics(
        IReadOnlyList<ReadingAttempt> submittedAttempts,
        IReadOnlyList<ReadingQuestion> questions,
        IReadOnlyDictionary<string, ReadingPart> partById,
        IReadOnlyList<ReadingAnswer> answers)
    {
        var opportunityCountsByQuestion = questions.ToDictionary(q => q.Id, _ => 0, StringComparer.Ordinal);
        var questionIdsByPaper = questions
            .GroupBy(q => partById.GetValueOrDefault(q.ReadingPartId)?.PaperId)
            .Where(g => g.Key is not null)
            .ToDictionary(
                g => g.Key!,
                g => g.Select(q => q.Id).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
        var answersByAttempt = answers
            .GroupBy(a => a.ReadingAttemptId)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var inScopeAnswers = new List<ReadingAnswer>();

        foreach (var attempt in submittedAttempts)
        {
            if (!questionIdsByPaper.TryGetValue(attempt.PaperId, out var paperQuestionIds)) continue;

            var attemptAnswers = answersByAttempt.GetValueOrDefault(attempt.Id) ?? new List<ReadingAnswer>();
            var inScopeQuestionIds = ResolveQuestionScope(attempt, paperQuestionIds, attemptAnswers);
            foreach (var questionId in inScopeQuestionIds)
            {
                if (opportunityCountsByQuestion.ContainsKey(questionId)) opportunityCountsByQuestion[questionId]++;
            }

            inScopeAnswers.AddRange(attemptAnswers.Where(answer => inScopeQuestionIds.Contains(answer.ReadingQuestionId)));
        }

        return new ReadingQuestionOpportunityMetrics(opportunityCountsByQuestion, inScopeAnswers);
    }

    private static HashSet<string> ResolveQuestionScope(
        ReadingAttempt attempt,
        HashSet<string> paperQuestionIds,
        IReadOnlyList<ReadingAnswer> attemptAnswers)
    {
        if (attempt.Mode is ReadingAttemptMode.Exam or ReadingAttemptMode.Learning
            || string.IsNullOrWhiteSpace(attempt.ScopeJson))
        {
            return new HashSet<string>(paperQuestionIds, StringComparer.Ordinal);
        }

        var scopedQuestionIds = TryReadQuestionIdsFromScope(attempt.ScopeJson, paperQuestionIds);
        return scopedQuestionIds.Count > 0
            ? scopedQuestionIds
            : attemptAnswers.Select(a => a.ReadingQuestionId)
                .Where(paperQuestionIds.Contains)
                .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> TryReadQuestionIdsFromScope(string scopeJson, HashSet<string> paperQuestionIds)
    {
        try
        {
            using var doc = JsonDocument.Parse(scopeJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("questionIds", out var questionIds)
                || questionIds.ValueKind != JsonValueKind.Array)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            return questionIds.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(id => !string.IsNullOrWhiteSpace(id) && paperQuestionIds.Contains(id))
                .Select(id => id!)
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static double? AverageOrNull(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : Math.Round(list.Average(), 1, MidpointRounding.AwayFromZero);
    }

    private static double? Percent(int numerator, int denominator)
    {
        return denominator <= 0
            ? null
            : Math.Round((double)numerator * 100 / denominator, 1, MidpointRounding.AwayFromZero);
    }

    private static string SanitiseId(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        return new string(chars).Trim('-');
    }
}

public sealed record ReadingAdminAnalyticsDto(
    DateTimeOffset GeneratedAt,
    int WindowDays,
    ReadingAdminAnalyticsSummaryDto Summary,
    IReadOnlyList<ReadingPaperAnalyticsDto> Papers,
    IReadOnlyList<ReadingPartAnalyticsDto> PartBreakdown,
    IReadOnlyList<ReadingSkillAnalyticsDto> SkillBreakdown,
    IReadOnlyList<ReadingQuestionAnalyticsDto> HardestQuestions,
    IReadOnlyList<ReadingModeAnalyticsDto> ModeBreakdown,
    IReadOnlyList<ReadingActionInsightDto> ActionInsights);

public sealed record ReadingAdminAnalyticsSummaryDto(
    int TotalPapers,
    int PublishedPapers,
    int ExamReadyPapers,
    int AuthoredQuestions,
    int TotalAttempts,
    int SubmittedAttempts,
    int ActiveAttempts,
    double? AverageRawScore,
    double? AverageScaledScore,
    double? PassRatePercent,
    double? UnansweredRatePercent);

public sealed record ReadingPaperAnalyticsDto(
    string PaperId,
    string Title,
    string Status,
    string Difficulty,
    int QuestionCount,
    int TotalPoints,
    int PartACount,
    int PartBCount,
    int PartCCount,
    bool IsCanonicalShapeComplete,
    bool IsExamReady,
    int AttemptCount,
    int SubmittedCount,
    double? AverageRawScore,
    double? AverageScaledScore,
    double? PassRatePercent,
    double? AverageCompletionSeconds);

public sealed record ReadingPartAnalyticsDto(
    string PartCode,
    int QuestionCount,
    int Opportunities,
    int CorrectCount,
    int UnansweredCount,
    double? AccuracyPercent);

public sealed record ReadingSkillAnalyticsDto(
    string Label,
    int QuestionCount,
    int Opportunities,
    int CorrectCount,
    int UnansweredCount,
    double? AccuracyPercent);

public sealed record ReadingQuestionAnalyticsDto(
    string PaperId,
    string PaperTitle,
    string QuestionId,
    string PartCode,
    string Label,
    int DisplayOrder,
    string QuestionType,
    string SkillTag,
    string Stem,
    int Opportunities,
    int AnsweredCount,
    int CorrectCount,
    int UnansweredCount,
    double? AccuracyPercent);

public sealed record ReadingModeAnalyticsDto(
    string Mode,
    int AttemptCount,
    int SubmittedCount,
    double? AverageRawScore,
    double? AverageScaledScore,
    double? PassRatePercent);

public sealed record ReadingActionInsightDto(
    string Id,
    string Title,
    string Description,
    string Tone);

internal sealed record ReadingQuestionOpportunityMetrics(
    IReadOnlyDictionary<string, int> OpportunityCountsByQuestion,
    IReadOnlyList<ReadingAnswer> InScopeAnswers);