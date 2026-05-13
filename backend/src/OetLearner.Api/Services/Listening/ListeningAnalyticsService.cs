using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Phase 6 + Phase 7 of LISTENING-MODULE-PLAN.md.
//
// One service, two views:
//   • GetMyAnalyticsAsync(userId)          — student-facing per-part breakdown,
//                                            top weakness, action plan.
//   • GetAdminAnalyticsAsync(days)         — class-wide signal: avg by part,
//                                            hardest items, distractor heat,
//                                            common misspellings, readiness.
//
// Both views merge legacy generic Listening `Attempt` rows and relational
// `ListeningAttempt` rows. Item-level correctness is re-derived against the
// authored 42-item map so analytics remain stable during migration.
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningAnalyticsService
{
    Task<ListeningStudentAnalyticsDto> GetMyAnalyticsAsync(string userId, CancellationToken ct);
    Task<ListeningAdminAnalyticsDto> GetAdminAnalyticsAsync(int days, CancellationToken ct);
    Task<ListeningClassAnalyticsDto> GetClassAnalyticsAsync(string ownerUserId, string classId, int days, CancellationToken ct);
    Task<ListeningAttemptExportDto> ExportAttemptAsync(string attemptId, CancellationToken ct);
}

public sealed record ListeningPartBreakdownDto(
    string PartCode,                  // "A" | "B" | "C"
    int Earned,
    int Max,
    double AccuracyPercent);

public sealed record ListeningTopWeaknessDto(
    string ErrorType,                 // distractor_confusion | spelling | paraphrase | …
    string Label,
    int Count);

public sealed record ListeningActionPlanItemDto(
    string Headline,
    string Detail,
    string? Route);

public sealed record ListeningStudentAnalyticsDto(
    int CompletedAttempts,
    int? BestScaledScore,
    int? AverageScaledScore,
    bool LikelyPassing,
    IReadOnlyList<ListeningPartBreakdownDto> PartBreakdown,
    IReadOnlyList<ListeningTopWeaknessDto> Weaknesses,
    IReadOnlyList<ListeningActionPlanItemDto> ActionPlan);

public sealed record ListeningHardestQuestionDto(
    string PaperId,
    string PaperTitle,
    int QuestionNumber,
    string PartCode,
    int AttemptCount,
    double AccuracyPercent);

public sealed record ListeningDistractorHeatDto(
    string PaperId,
    int QuestionNumber,
    string CorrectAnswer,
    IReadOnlyDictionary<string, int> WrongAnswerHistogram);

public sealed record ListeningCommonMisspellingDto(
    string CorrectAnswer,
    string WrongSpelling,
    int Count);

public sealed record ListeningAdminAnalyticsDto(
    int Days,
    int CompletedAttempts,
    int? AverageScaledScore,
    double PercentLikelyPassing,
    IReadOnlyList<ListeningPartBreakdownDto> ClassPartAverages,
    IReadOnlyList<ListeningHardestQuestionDto> HardestQuestions,
    IReadOnlyList<ListeningDistractorHeatDto> DistractorHeat,
    IReadOnlyList<ListeningCommonMisspellingDto> CommonMisspellings);

public sealed record ListeningClassAnalyticsDto(
    string ClassId,
    string ClassName,
    string? Description,
    int MemberCount,
    ListeningTeacherAnalyticsDto Analytics);

public sealed record ListeningTeacherDistractorHeatDto(
    string PaperId,
    int QuestionNumber,
    string CorrectAnswer,
    int WrongAnswerCount);

public sealed record ListeningTeacherAnalyticsDto(
    int Days,
    int CompletedAttempts,
    int? AverageScaledScore,
    double PercentLikelyPassing,
    IReadOnlyList<ListeningPartBreakdownDto> ClassPartAverages,
    IReadOnlyList<ListeningHardestQuestionDto> HardestQuestions,
    IReadOnlyList<ListeningTeacherDistractorHeatDto> DistractorHeat);

public sealed record ListeningAttemptExportDto(
    string Source,
    string AttemptId,
    string UserId,
    string PaperId,
    string Status,
    string Mode,
    DateTimeOffset StartedAt,
    DateTimeOffset? SubmittedAt,
    int? RawScore,
    int? ScaledScore,
    int? MaxRawScore,
    string? AnswersJson,
    string? DraftContent,
    string? Scratchpad,
    string? ChecklistJson,
    string? TranscriptJson,
    string? AnalysisJson,
    string? AudioMetadataJson,
    string? PolicySnapshotJson,
    string? ScopeJson,
    string? NavigationStateJson,
    string? AudioCueTimelineJson,
    string? TechReadinessJson,
    string? AnnotationsJson,
    string? HumanScoreOverridesJson,
    string? LastQuestionVersionMapJson,
    IReadOnlyList<ListeningAttemptAnswerExportDto> Answers,
    IReadOnlyList<ListeningAttemptEvaluationExportDto> Evaluations);

public sealed record ListeningAttemptAnswerExportDto(
    string AnswerId,
    string QuestionId,
    string UserAnswerJson,
    bool? IsCorrect,
    int PointsEarned,
    string? SelectedDistractorCategory,
    int? QuestionVersionSnapshot,
    int? OptionVersionSnapshot,
    DateTimeOffset AnsweredAt);

public sealed record ListeningAttemptEvaluationExportDto(
    string EvaluationId,
    string SubtestCode,
    string State,
    string ScoreRange,
    string? GradeRange,
    string CriterionScoresJson,
    DateTimeOffset? GeneratedAt,
    string StatusReasonCode,
    string StatusMessage);

public sealed class ListeningAnalyticsService(LearnerDbContext db) : IListeningAnalyticsService
{
    private const string Subtest = "listening";
    private const int CanonicalRawMax = OetScoring.ListeningReadingRawMax;

    // ── Student view ─────────────────────────────────────────────────────

    public async Task<ListeningStudentAnalyticsDto> GetMyAnalyticsAsync(string userId, CancellationToken ct)
    {
        var attempts = await db.Attempts.AsNoTracking()
            .Where(a => a.UserId == userId
                && a.SubtestCode == Subtest
                && (a.State == AttemptState.Submitted || a.State == AttemptState.Completed))
            .OrderByDescending(a => a.SubmittedAt)
            .Take(20)
            .ToListAsync(ct);

        var relationalAttempts = await db.ListeningAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.Status == ListeningAttemptStatus.Submitted)
            .OrderByDescending(a => a.SubmittedAt)
            .Take(20)
            .ToListAsync(ct);

        if (attempts.Count == 0 && relationalAttempts.Count == 0)
        {
            return new ListeningStudentAnalyticsDto(
                CompletedAttempts: 0,
                BestScaledScore: null,
                AverageScaledScore: null,
                LikelyPassing: false,
                PartBreakdown: [],
                Weaknesses: [],
                ActionPlan: new[]
                {
                    new ListeningActionPlanItemDto(
                        "Start with a diagnostic",
                        "Take any published Listening paper to unlock per-part analytics, weakness detection, and a personalised action plan.",
                        "/listening"),
                });
        }

        var attemptIds = attempts.Select(a => a.Id).ToList();
        var relationalAttemptIds = relationalAttempts.Select(a => a.Id).ToList();
        var allAttemptIds = attemptIds.Concat(relationalAttemptIds).Distinct(StringComparer.Ordinal).ToList();
        var paperIds = attempts.Select(a => a.ContentId)
            .Concat(relationalAttempts.Select(a => a.PaperId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var papers = await db.ContentPapers.AsNoTracking()
            .Where(p => paperIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p, ct);
        var relationalAuthoredByPaper = await LoadRelationalAuthoredQuestionsAsync(
            relationalAttempts.Select(a => a.PaperId).Distinct(StringComparer.Ordinal).ToList(), ct);

        // Listening answers are stored as a JSON dictionary on the Attempt
        // itself (`AnswersJson`), not in a separate AttemptItem table —
        // see ListeningLearnerService.SaveAnswerAsync.
        var answersByAttempt = attempts.ToDictionary(
            a => a.Id,
            a => DeserializeAnswers(a.AnswersJson));

        var relationalAnswers = relationalAttemptIds.Count == 0
            ? new List<ListeningAnswer>()
            : await db.ListeningAnswers.AsNoTracking()
                .Where(a => relationalAttemptIds.Contains(a.ListeningAttemptId))
                .ToListAsync(ct);
        var relationalAnswersByAttempt = relationalAnswers
            .GroupBy(a => a.ListeningAttemptId)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(a => a.ListeningQuestionId, a => DeserializeRelationalAnswer(a.UserAnswerJson), StringComparer.Ordinal),
                StringComparer.Ordinal);

        var evals = await db.Evaluations.AsNoTracking()
            .Where(e => allAttemptIds.Contains(e.AttemptId))
            .ToListAsync(ct);

        var scaledByAttempt = evals
            .Select(e => (e.AttemptId, Scaled: TryReadScaled(e)))
            .Where(x => x.Scaled.HasValue)
            .ToDictionary(x => x.AttemptId, x => x.Scaled!.Value);

        foreach (var relationalAttempt in relationalAttempts)
        {
            if (relationalAttempt.ScaledScore is int scaled)
            {
                scaledByAttempt[relationalAttempt.Id] = scaled;
            }
        }

        // Per-part aggregates across recent attempts
        var partAgg = new Dictionary<string, (int earned, int max)>(StringComparer.OrdinalIgnoreCase);
        var weaknessAgg = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var attempt in attempts)
        {
            if (!papers.TryGetValue(attempt.ContentId, out var paper)) continue;
            var authored = ParseAuthoredQuestions(paper);
            if (authored.Count == 0) continue;

            var answers = answersByAttempt.TryGetValue(attempt.Id, out var a) ? a : new Dictionary<string, string?>();
            foreach (var q in authored)
            {
                var partKey = NormalizePartKey(q.PartCode);
                if (!partAgg.ContainsKey(partKey)) partAgg[partKey] = (0, 0);
                var prev = partAgg[partKey];
                partAgg[partKey] = (prev.earned, prev.max + q.Points);

                answers.TryGetValue(q.Id, out var raw);
                var learner = (raw ?? string.Empty).Trim();
                var correct = AnyAccepted(q, learner);
                if (correct)
                {
                    var p = partAgg[partKey];
                    partAgg[partKey] = (p.earned + q.Points, p.max);
                }
                else
                {
                    var et = ClassifyError(q, learner);
                    weaknessAgg.TryGetValue(et, out var c);
                    weaknessAgg[et] = c + 1;
                }
            }
        }

        foreach (var attempt in relationalAttempts)
        {
            if (!papers.TryGetValue(attempt.PaperId, out var paper)) continue;
            var authored = relationalAuthoredByPaper.TryGetValue(attempt.PaperId, out var relationalAuthored)
                ? relationalAuthored
                : ParseAuthoredQuestions(paper);
            if (authored.Count == 0) continue;

            var answers = relationalAnswersByAttempt.TryGetValue(attempt.Id, out var a) ? a : new Dictionary<string, string?>();
            foreach (var q in authored)
            {
                var partKey = NormalizePartKey(q.PartCode);
                if (!partAgg.ContainsKey(partKey)) partAgg[partKey] = (0, 0);
                var prev = partAgg[partKey];
                partAgg[partKey] = (prev.earned, prev.max + q.Points);

                answers.TryGetValue(q.Id, out var raw);
                var learner = (raw ?? string.Empty).Trim();
                var correct = AnyAccepted(q, learner);
                if (correct)
                {
                    var p = partAgg[partKey];
                    partAgg[partKey] = (p.earned + q.Points, p.max);
                }
                else
                {
                    var et = ClassifyError(q, learner);
                    weaknessAgg.TryGetValue(et, out var c);
                    weaknessAgg[et] = c + 1;
                }
            }
        }

        var partBreakdown = partAgg
            .Select(kv => new ListeningPartBreakdownDto(
                PartCode: kv.Key,
                Earned: kv.Value.earned,
                Max: kv.Value.max,
                AccuracyPercent: kv.Value.max == 0 ? 0 : Math.Round(100.0 * kv.Value.earned / kv.Value.max, 1)))
            .OrderBy(p => p.PartCode)
            .ToList();

        var weaknesses = weaknessAgg
            .OrderByDescending(kv => kv.Value)
            .Take(3)
            .Select(kv => new ListeningTopWeaknessDto(kv.Key, ErrorTypeLabel(kv.Key), kv.Value))
            .ToList();

        int? best = scaledByAttempt.Count == 0 ? null : scaledByAttempt.Values.Max();
        int? avg = scaledByAttempt.Count == 0 ? null : (int)Math.Round(scaledByAttempt.Values.Average());
        var passing = (best ?? 0) >= OetScoring.ScaledPassGradeB;

        var plan = BuildStudentActionPlan(weaknesses, partBreakdown, passing);

        return new ListeningStudentAnalyticsDto(
            CompletedAttempts: attempts.Count + relationalAttempts.Count,
            BestScaledScore: best,
            AverageScaledScore: avg,
            LikelyPassing: passing,
            PartBreakdown: partBreakdown,
            Weaknesses: weaknesses,
            ActionPlan: plan);
    }

    private static IReadOnlyList<ListeningActionPlanItemDto> BuildStudentActionPlan(
        IReadOnlyList<ListeningTopWeaknessDto> weaknesses,
        IReadOnlyList<ListeningPartBreakdownDto> parts,
        bool passing)
    {
        var plan = new List<ListeningActionPlanItemDto>();
        if (weaknesses.Count > 0)
        {
            var top = weaknesses[0];
            plan.Add(new ListeningActionPlanItemDto(
                Headline: $"Target: {top.Label}",
                Detail: $"This pattern accounts for {top.Count} of your recent missed items. Focus your next 30 minutes on a targeted drill before another mock attempt.",
                Route: "/listening/drills"));
        }

        var weakestPart = parts
            .Where(p => p.Max > 0)
            .OrderBy(p => p.AccuracyPercent)
            .FirstOrDefault();
        if (weakestPart is not null)
        {
            plan.Add(new ListeningActionPlanItemDto(
                Headline: $"Part {weakestPart.PartCode} is your weakest part",
                Detail: $"Recent accuracy is {weakestPart.AccuracyPercent}%. Re-listen to the audio and rebuild it from the transcript before the next attempt.",
                Route: null));
        }

        plan.Add(new ListeningActionPlanItemDto(
            Headline: passing ? "Maintain exam readiness" : "Take one more full mock",
            Detail: passing
                ? "Your best scaled score is at or above 350. Keep one full mock per week to maintain stamina under one-play conditions."
                : "Score consistency comes from repetition under exam constraints. Schedule one full Listening mock per week.",
            Route: "/mocks"));

        return plan;
    }

    // ── Admin view ───────────────────────────────────────────────────────

    public async Task<ListeningAdminAnalyticsDto> GetAdminAnalyticsAsync(int days, CancellationToken ct)
        => await BuildAggregateAnalyticsAsync(days, scopedTeacherClassId: null, ct);

    public async Task<ListeningAttemptExportDto> ExportAttemptAsync(string attemptId, CancellationToken ct)
    {
        var relationalAttempt = await db.ListeningAttempts.AsNoTracking()
            .FirstOrDefaultAsync(attempt => attempt.Id == attemptId, ct);
        if (relationalAttempt is not null)
        {
            var answers = await db.ListeningAnswers.AsNoTracking()
                .Where(answer => answer.ListeningAttemptId == attemptId)
                .OrderBy(answer => answer.ListeningQuestionId)
                .Select(answer => new ListeningAttemptAnswerExportDto(
                    answer.Id,
                    answer.ListeningQuestionId,
                    answer.UserAnswerJson,
                    answer.IsCorrect,
                    answer.PointsEarned,
                    answer.SelectedDistractorCategory == null ? null : answer.SelectedDistractorCategory.ToString(),
                    answer.QuestionVersionSnapshot,
                    answer.OptionVersionSnapshot,
                    answer.AnsweredAt))
                .ToListAsync(ct);
            var evaluations = await LoadEvaluationExportsAsync(attemptId, ct);

            return new ListeningAttemptExportDto(
                Source: "listening-v2",
                AttemptId: relationalAttempt.Id,
                UserId: relationalAttempt.UserId,
                PaperId: relationalAttempt.PaperId,
                Status: relationalAttempt.Status.ToString(),
                Mode: relationalAttempt.Mode.ToString(),
                StartedAt: relationalAttempt.StartedAt,
                SubmittedAt: relationalAttempt.SubmittedAt,
                RawScore: relationalAttempt.RawScore,
                ScaledScore: relationalAttempt.ScaledScore,
                MaxRawScore: relationalAttempt.MaxRawScore,
                AnswersJson: null,
                DraftContent: null,
                Scratchpad: null,
                ChecklistJson: null,
                TranscriptJson: null,
                AnalysisJson: null,
                AudioMetadataJson: null,
                PolicySnapshotJson: relationalAttempt.PolicySnapshotJson,
                ScopeJson: relationalAttempt.ScopeJson,
                NavigationStateJson: relationalAttempt.NavigationStateJson,
                AudioCueTimelineJson: relationalAttempt.AudioCueTimelineJson,
                TechReadinessJson: relationalAttempt.TechReadinessJson,
                AnnotationsJson: relationalAttempt.AnnotationsJson,
                HumanScoreOverridesJson: relationalAttempt.HumanScoreOverridesJson,
                LastQuestionVersionMapJson: relationalAttempt.LastQuestionVersionMapJson,
                Answers: answers,
                Evaluations: evaluations);
        }

        var legacyAttempt = await db.Attempts.AsNoTracking()
            .FirstOrDefaultAsync(attempt => attempt.Id == attemptId && attempt.SubtestCode == Subtest, ct)
            ?? throw new KeyNotFoundException($"Listening attempt {attemptId} not found.");
        var legacyEvaluations = await LoadEvaluationExportsAsync(attemptId, ct);
        var scaledScore = legacyEvaluations
            .Select(evaluation => new
            {
                evaluation.GeneratedAt,
                Scaled = TryReadScaled(evaluation.CriterionScoresJson),
            })
            .Where(item => item.Scaled.HasValue)
            .OrderByDescending(item => item.GeneratedAt)
            .FirstOrDefault()
            ?.Scaled;

        return new ListeningAttemptExportDto(
            Source: "legacy",
            AttemptId: legacyAttempt.Id,
            UserId: legacyAttempt.UserId,
            PaperId: legacyAttempt.ContentId,
            Status: legacyAttempt.State.ToString(),
            Mode: legacyAttempt.Mode,
            StartedAt: legacyAttempt.StartedAt,
            SubmittedAt: legacyAttempt.SubmittedAt,
            RawScore: null,
            ScaledScore: scaledScore,
            MaxRawScore: null,
            AnswersJson: legacyAttempt.AnswersJson,
            DraftContent: legacyAttempt.DraftContent,
            Scratchpad: legacyAttempt.Scratchpad,
            ChecklistJson: legacyAttempt.ChecklistJson,
            TranscriptJson: legacyAttempt.TranscriptJson,
            AnalysisJson: legacyAttempt.AnalysisJson,
            AudioMetadataJson: legacyAttempt.AudioMetadataJson,
            PolicySnapshotJson: null,
            ScopeJson: null,
            NavigationStateJson: null,
            AudioCueTimelineJson: null,
            TechReadinessJson: null,
            AnnotationsJson: null,
            HumanScoreOverridesJson: null,
            LastQuestionVersionMapJson: null,
            Answers: [],
            Evaluations: legacyEvaluations);
    }

    public async Task<ListeningClassAnalyticsDto> GetClassAnalyticsAsync(
        string ownerUserId,
        string classId,
        int days,
        CancellationToken ct)
    {
        var teacherClass = await db.TeacherClasses.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == classId && row.OwnerUserId == ownerUserId, ct)
            ?? throw new KeyNotFoundException($"Class {classId} not found.");

        var memberCount = await db.TeacherClassMembers.AsNoTracking()
            .Where(member => member.TeacherClassId == classId)
            .Select(member => member.UserId)
            .Distinct()
            .CountAsync(ct);

        var analytics = await BuildAggregateAnalyticsAsync(days, scopedTeacherClassId: classId, ct);
        return new ListeningClassAnalyticsDto(
            teacherClass.Id,
            teacherClass.Name,
            teacherClass.Description,
            memberCount,
            ToTeacherSafeAnalytics(analytics));
    }

    private static ListeningTeacherAnalyticsDto ToTeacherSafeAnalytics(ListeningAdminAnalyticsDto analytics)
        => new(
            Days: analytics.Days,
            CompletedAttempts: analytics.CompletedAttempts,
            AverageScaledScore: analytics.AverageScaledScore,
            PercentLikelyPassing: analytics.PercentLikelyPassing,
            ClassPartAverages: analytics.ClassPartAverages,
            HardestQuestions: analytics.HardestQuestions,
            DistractorHeat: analytics.DistractorHeat
                .Select(item => new ListeningTeacherDistractorHeatDto(
                    PaperId: item.PaperId,
                    QuestionNumber: item.QuestionNumber,
                    CorrectAnswer: item.CorrectAnswer,
                    WrongAnswerCount: item.WrongAnswerHistogram.Values.Sum()))
                .ToList());

    private async Task<List<ListeningAttemptEvaluationExportDto>> LoadEvaluationExportsAsync(
        string attemptId,
        CancellationToken ct)
        => await db.Evaluations.AsNoTracking()
            .Where(evaluation => evaluation.AttemptId == attemptId && evaluation.SubtestCode == Subtest)
            .OrderBy(evaluation => evaluation.GeneratedAt)
            .Select(evaluation => new ListeningAttemptEvaluationExportDto(
                evaluation.Id,
                evaluation.SubtestCode,
                evaluation.State.ToString(),
                evaluation.ScoreRange,
                evaluation.GradeRange,
                evaluation.CriterionScoresJson,
                evaluation.GeneratedAt,
                evaluation.StatusReasonCode,
                evaluation.StatusMessage))
            .ToListAsync(ct);

    private async Task<ListeningAdminAnalyticsDto> BuildAggregateAnalyticsAsync(
        int days,
        string? scopedTeacherClassId,
        CancellationToken ct)
    {
        if (days <= 0) days = 30;
        if (days > 365) days = 365;

        var since = DateTimeOffset.UtcNow.AddDays(-days);
        var clientSideSubmittedAtFilter = db.Database.IsSqlite();
        var attemptsQuery = db.Attempts.AsNoTracking()
            .Where(a => a.SubtestCode == Subtest
                && (a.State == AttemptState.Submitted || a.State == AttemptState.Completed));
        if (!clientSideSubmittedAtFilter)
        {
            attemptsQuery = attemptsQuery.Where(a => a.SubmittedAt >= since);
        }
        if (!string.IsNullOrWhiteSpace(scopedTeacherClassId))
        {
            var members = db.TeacherClassMembers.AsNoTracking()
                .Where(member => member.TeacherClassId == scopedTeacherClassId);
            attemptsQuery = attemptsQuery
                .Join(members, attempt => attempt.UserId, member => member.UserId, (attempt, _) => attempt)
                .Distinct();
        }

        var attempts = await attemptsQuery
            .ToListAsync(ct);
        if (clientSideSubmittedAtFilter)
        {
            attempts = attempts
                .Where(a => a.SubmittedAt is not null && a.SubmittedAt.Value >= since)
                .ToList();
        }

        var relationalAttemptsQuery = db.ListeningAttempts.AsNoTracking()
            .Where(a => a.Status == ListeningAttemptStatus.Submitted);
        if (!clientSideSubmittedAtFilter)
        {
            relationalAttemptsQuery = relationalAttemptsQuery.Where(a => a.SubmittedAt >= since);
        }
        if (!string.IsNullOrWhiteSpace(scopedTeacherClassId))
        {
            var members = db.TeacherClassMembers.AsNoTracking()
                .Where(member => member.TeacherClassId == scopedTeacherClassId);
            relationalAttemptsQuery = relationalAttemptsQuery
                .Join(members, attempt => attempt.UserId, member => member.UserId, (attempt, _) => attempt)
                .Distinct();
        }

        var relationalAttempts = await relationalAttemptsQuery
            .ToListAsync(ct);
        if (clientSideSubmittedAtFilter)
        {
            relationalAttempts = relationalAttempts
                .Where(a => a.SubmittedAt is not null && a.SubmittedAt.Value >= since)
                .ToList();
        }

        if (attempts.Count == 0 && relationalAttempts.Count == 0)
        {
            return EmptyAdminAnalytics(days);
        }

        var attemptIds = attempts.Select(a => a.Id).ToList();
        var relationalAttemptIds = relationalAttempts.Select(a => a.Id).ToList();
        var allAttemptIds = attemptIds.Concat(relationalAttemptIds).Distinct(StringComparer.Ordinal).ToList();
        var paperIds = attempts.Select(a => a.ContentId)
            .Concat(relationalAttempts.Select(a => a.PaperId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var papers = await db.ContentPapers.AsNoTracking()
            .Where(p => paperIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p, ct);
        var relationalAuthoredByPaper = await LoadRelationalAuthoredQuestionsAsync(
            relationalAttempts.Select(a => a.PaperId).Distinct(StringComparer.Ordinal).ToList(), ct);

        var answersByAttempt = attempts.ToDictionary(
            a => a.Id,
            a => DeserializeAnswers(a.AnswersJson));

        var relationalAnswers = relationalAttemptIds.Count == 0
            ? new List<ListeningAnswer>()
            : await db.ListeningAnswers.AsNoTracking()
                .Where(a => relationalAttemptIds.Contains(a.ListeningAttemptId))
                .ToListAsync(ct);
        var relationalAnswersByAttempt = relationalAnswers
            .GroupBy(a => a.ListeningAttemptId)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(a => a.ListeningQuestionId, a => DeserializeRelationalAnswer(a.UserAnswerJson), StringComparer.Ordinal),
                StringComparer.Ordinal);

        var evals = await db.Evaluations.AsNoTracking()
            .Where(e => allAttemptIds.Contains(e.AttemptId))
            .ToListAsync(ct);

        var scaledByAttempt = evals
            .Select(e => new { e.AttemptId, e.GeneratedAt, Scaled = TryReadScaled(e) })
            .Where(x => x.Scaled.HasValue)
            .GroupBy(x => x.AttemptId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(x => x.GeneratedAt).First().Scaled!.Value,
                StringComparer.Ordinal);
        foreach (var relationalAttempt in relationalAttempts)
        {
            if (relationalAttempt.ScaledScore is int scaledScore)
            {
                // Relational Listening V2 attempts are the source of truth when both legacy
                // Evaluation rows and the normalized attempt row have a scaled score.
                scaledByAttempt[relationalAttempt.Id] = scaledScore;
            }
        }

        var scaled = scaledByAttempt.Values.ToList();
        int? avgScaled = scaled.Count == 0 ? null : (int)Math.Round(scaled.Average());
        var percentPassing = scaled.Count == 0 ? 0 : Math.Round(100.0 * scaled.Count(OetScoring.IsListeningReadingPassByScaled) / scaled.Count, 1);

        // Per-part class averages
        var partAgg = new Dictionary<string, (int earned, int max)>(StringComparer.OrdinalIgnoreCase);

        // Per-question (paperId, qNum) tally for hardest questions + distractor heat
        var qTally = new Dictionary<(string paperId, int qNum), (int correct, int total, string partCode, string correct_answer, Dictionary<string, int> wrongs)>();

        // Common misspellings
        var spellingTally = new Dictionary<(string correct, string wrong), int>();

        foreach (var attempt in attempts)
        {
            if (!papers.TryGetValue(attempt.ContentId, out var paper)) continue;
            var authored = ParseAuthoredQuestions(paper);
            if (authored.Count == 0) continue;

            var answers = answersByAttempt.TryGetValue(attempt.Id, out var a) ? a : new Dictionary<string, string?>();

            foreach (var q in authored)
            {
                var partKey = NormalizePartKey(q.PartCode);
                if (!partAgg.ContainsKey(partKey)) partAgg[partKey] = (0, 0);
                var ag = partAgg[partKey];
                partAgg[partKey] = (ag.earned, ag.max + q.Points);

                answers.TryGetValue(q.Id, out var raw);
                var learner = (raw ?? string.Empty).Trim();
                var isCorrect = AnyAccepted(q, learner);

                if (isCorrect)
                {
                    var p = partAgg[partKey];
                    partAgg[partKey] = (p.earned + q.Points, p.max);
                }

                var key = (paper.Id, q.Number);
                if (!qTally.TryGetValue(key, out var t))
                {
                    t = (0, 0, partKey, q.CorrectAnswer ?? string.Empty, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
                }
                t.total += 1;
                if (isCorrect) t.correct += 1;
                else if (!string.IsNullOrWhiteSpace(learner))
                {
                    t.wrongs.TryGetValue(learner, out var c);
                    t.wrongs[learner] = c + 1;

                    // Cheap spelling detector for Part A short_answer items.
                    if (partKey == "A"
                        && !string.IsNullOrWhiteSpace(q.CorrectAnswer)
                        && learner.Length >= 3
                        && q.CorrectAnswer.Length >= 3
                        && !string.Equals(learner, q.CorrectAnswer, StringComparison.OrdinalIgnoreCase)
                        && Levenshtein(learner.ToLowerInvariant(), q.CorrectAnswer.ToLowerInvariant())
                            <= Math.Max(1, q.CorrectAnswer.Length / 4))
                    {
                        var k = (q.CorrectAnswer.Trim(), learner.Trim());
                        spellingTally.TryGetValue(k, out var sc);
                        spellingTally[k] = sc + 1;
                    }
                }
                qTally[key] = t;
            }
        }

        foreach (var attempt in relationalAttempts)
        {
            if (!papers.TryGetValue(attempt.PaperId, out var paper)) continue;
            var authored = relationalAuthoredByPaper.TryGetValue(attempt.PaperId, out var relationalAuthored)
                ? relationalAuthored
                : ParseAuthoredQuestions(paper);
            if (authored.Count == 0) continue;

            var answers = relationalAnswersByAttempt.TryGetValue(attempt.Id, out var a) ? a : new Dictionary<string, string?>();

            foreach (var q in authored)
            {
                var partKey = NormalizePartKey(q.PartCode);
                if (!partAgg.ContainsKey(partKey)) partAgg[partKey] = (0, 0);
                var ag = partAgg[partKey];
                partAgg[partKey] = (ag.earned, ag.max + q.Points);

                answers.TryGetValue(q.Id, out var raw);
                var learner = (raw ?? string.Empty).Trim();
                var isCorrect = AnyAccepted(q, learner);

                if (isCorrect)
                {
                    var p = partAgg[partKey];
                    partAgg[partKey] = (p.earned + q.Points, p.max);
                }

                var key = (paper.Id, q.Number);
                if (!qTally.TryGetValue(key, out var t))
                {
                    t = (0, 0, partKey, q.CorrectAnswer ?? string.Empty, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
                }
                t.total += 1;
                if (isCorrect) t.correct += 1;
                else if (!string.IsNullOrWhiteSpace(learner))
                {
                    t.wrongs.TryGetValue(learner, out var c);
                    t.wrongs[learner] = c + 1;

                    if (partKey == "A"
                        && !string.IsNullOrWhiteSpace(q.CorrectAnswer)
                        && learner.Length >= 3
                        && q.CorrectAnswer.Length >= 3
                        && !string.Equals(learner, q.CorrectAnswer, StringComparison.OrdinalIgnoreCase)
                        && Levenshtein(learner.ToLowerInvariant(), q.CorrectAnswer.ToLowerInvariant())
                            <= Math.Max(1, q.CorrectAnswer.Length / 4))
                    {
                        var k = (q.CorrectAnswer.Trim(), learner.Trim());
                        spellingTally.TryGetValue(k, out var sc);
                        spellingTally[k] = sc + 1;
                    }
                }
                qTally[key] = t;
            }
        }

        var classParts = partAgg
            .Select(kv => new ListeningPartBreakdownDto(
                PartCode: kv.Key,
                Earned: kv.Value.earned,
                Max: kv.Value.max,
                AccuracyPercent: kv.Value.max == 0 ? 0 : Math.Round(100.0 * kv.Value.earned / kv.Value.max, 1)))
            .OrderBy(p => p.PartCode)
            .ToList();

        var hardest = qTally
            .Where(kv => kv.Value.total >= 3)
            .OrderBy(kv => kv.Value.total == 0 ? 1 : (double)kv.Value.correct / kv.Value.total)
            .Take(10)
            .Select(kv =>
            {
                var pTitle = papers.TryGetValue(kv.Key.paperId, out var pp) ? pp.Title : kv.Key.paperId;
                return new ListeningHardestQuestionDto(
                    PaperId: kv.Key.paperId,
                    PaperTitle: pTitle ?? kv.Key.paperId,
                    QuestionNumber: kv.Key.qNum,
                    PartCode: kv.Value.partCode,
                    AttemptCount: kv.Value.total,
                    AccuracyPercent: kv.Value.total == 0 ? 0 : Math.Round(100.0 * kv.Value.correct / kv.Value.total, 1));
            })
            .ToList();

        var heat = qTally
            .Where(kv => kv.Value.partCode != "A" && kv.Value.wrongs.Count > 0)
            .OrderByDescending(kv => kv.Value.wrongs.Values.Sum())
            .Take(10)
            .Select(kv => new ListeningDistractorHeatDto(
                PaperId: kv.Key.paperId,
                QuestionNumber: kv.Key.qNum,
                CorrectAnswer: kv.Value.correct_answer,
                WrongAnswerHistogram: kv.Value.wrongs))
            .ToList();

        var spelling = spellingTally
            .OrderByDescending(kv => kv.Value)
            .Take(10)
            .Select(kv => new ListeningCommonMisspellingDto(kv.Key.correct, kv.Key.wrong, kv.Value))
            .ToList();

        return new ListeningAdminAnalyticsDto(
            Days: days,
            CompletedAttempts: attempts.Count + relationalAttempts.Count,
            AverageScaledScore: avgScaled,
            PercentLikelyPassing: percentPassing,
            ClassPartAverages: classParts,
            HardestQuestions: hardest,
            DistractorHeat: heat,
            CommonMisspellings: spelling);
    }

    private static ListeningAdminAnalyticsDto EmptyAdminAnalytics(int days)
        => new(
            Days: days,
            CompletedAttempts: 0,
            AverageScaledScore: null,
            PercentLikelyPassing: 0,
            ClassPartAverages: [],
            HardestQuestions: [],
            DistractorHeat: [],
            CommonMisspellings: []);

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string NormalizePartKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "?";
        var first = char.ToUpperInvariant(raw[0]);
        return first is 'A' or 'B' or 'C' ? first.ToString() : "?";
    }

    private sealed record AuthoredQ(
        string Id, int Number, string PartCode, string Type,
        IReadOnlyList<string> Options, string CorrectAnswer,
        IReadOnlyList<string> AcceptedAnswers, int Points);

    private async Task<Dictionary<string, IReadOnlyList<AuthoredQ>>> LoadRelationalAuthoredQuestionsAsync(
        IReadOnlyCollection<string> paperIds,
        CancellationToken ct)
    {
        if (paperIds.Count == 0) return new Dictionary<string, IReadOnlyList<AuthoredQ>>(StringComparer.Ordinal);

        var questions = await db.ListeningQuestions.AsNoTracking()
            .Include(q => q.Part)
            .Include(q => q.Options)
            .Where(q => paperIds.Contains(q.PaperId))
            .OrderBy(q => q.QuestionNumber)
            .ToListAsync(ct);

        return questions
            .GroupBy(q => q.PaperId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AuthoredQ>)group.Select(MapRelationalAuthoredQuestion).ToList(),
                StringComparer.Ordinal);
    }

    private static AuthoredQ MapRelationalAuthoredQuestion(OetLearner.Api.Domain.ListeningQuestion question)
    {
        var options = question.Options.OrderBy(option => option.DisplayOrder).ToList();
        var correctOption = options.FirstOrDefault(option => option.IsCorrect);
        var rawCorrect = DeserializeRelationalAnswer(question.CorrectAnswerJson) ?? string.Empty;
        var correctAnswer = correctOption?.Text ?? rawCorrect;
        var accepted = ReadStringListJson(question.AcceptedSynonymsJson).ToList();
        AddAccepted(accepted, rawCorrect);
        if (correctOption is not null)
        {
            AddAccepted(accepted, correctOption.OptionKey);
            AddAccepted(accepted, correctOption.Text);
        }

        return new AuthoredQ(
            Id: question.Id,
            Number: question.QuestionNumber,
            PartCode: PartCodeLabel(question.Part?.PartCode ?? ListeningPartCode.A1),
            Type: question.QuestionType == ListeningQuestionType.MultipleChoice3 ? "multiple_choice_3" : "short_answer",
            Options: options.Select(option => option.Text).ToList(),
            CorrectAnswer: correctAnswer,
            AcceptedAnswers: accepted,
            Points: Math.Max(1, question.Points));
    }

    private static void AddAccepted(List<string> accepted, string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return;
        if (!accepted.Any(existing => string.Equals(existing.Trim(), answer.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            accepted.Add(answer.Trim());
        }
    }

    private static string PartCodeLabel(ListeningPartCode partCode) => partCode switch
    {
        ListeningPartCode.A1 => "A1",
        ListeningPartCode.A2 => "A2",
        ListeningPartCode.B => "B",
        ListeningPartCode.C1 => "C1",
        ListeningPartCode.C2 => "C2",
        _ => partCode.ToString(),
    };

    private static IReadOnlyList<string> ReadStringListJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            return doc.RootElement.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<AuthoredQ> ParseAuthoredQuestions(ContentPaper paper)
    {
        if (string.IsNullOrWhiteSpace(paper.ExtractedTextJson)) return [];
        try
        {
            var root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(paper.ExtractedTextJson);
            if (root is null || !root.TryGetValue("listeningQuestions", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return [];

            var list = new List<AuthoredQ>(arr.GetArrayLength());
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                string getStr(string key) => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
                int getInt(string key, int dflt = 0) => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : dflt;
                List<string> getList(string key)
                {
                    if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array) return new();
                    var items = new List<string>(v.GetArrayLength());
                    foreach (var item in v.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.String) items.Add(item.GetString() ?? string.Empty);
                    return items;
                }

                var num = getInt("number", list.Count + 1);
                var correct = getStr("correctAnswer");
                var accepted = getList("acceptedAnswers");
                if (!string.IsNullOrWhiteSpace(correct)) accepted.Insert(0, correct);

                list.Add(new AuthoredQ(
                    Id: getStr("id") is { Length: > 0 } id ? id : $"lq-{num}",
                    Number: num,
                    PartCode: getStr("partCode") is { Length: > 0 } pc ? pc : (getStr("part") is { Length: > 0 } p2 ? p2 : "A"),
                    Type: getStr("type") is { Length: > 0 } t ? t : "short_answer",
                    Options: getList("options"),
                    CorrectAnswer: correct,
                    AcceptedAnswers: accepted.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    Points: Math.Max(1, getInt("points", 1))));
            }
            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool AnyAccepted(AuthoredQ q, string learner)
    {
        if (string.IsNullOrWhiteSpace(learner)) return false;
        var l = learner.Trim();
        return q.AcceptedAnswers.Any(a =>
            !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), l, StringComparison.OrdinalIgnoreCase));
    }

    private static string ClassifyError(AuthoredQ q, string learner)
    {
        if (string.IsNullOrWhiteSpace(learner)) return "empty";
        if (q.Options.Count > 0) return "distractor_confusion";
        if (string.IsNullOrWhiteSpace(q.CorrectAnswer)) return "detail_capture";
        var ca = q.CorrectAnswer.Trim().ToLowerInvariant();
        var la = learner.Trim().ToLowerInvariant();
        if (la == ca) return "detail_capture";
        if (Levenshtein(la, ca) <= Math.Max(2, ca.Length / 4)) return "spelling";
        var caTokens = ca.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var laTokens = la.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (laTokens.Length > 0 && !caTokens.Any(t => laTokens.Contains(t))) return "paraphrase";
        return "detail_capture";
    }

    private static string ErrorTypeLabel(string code) => code switch
    {
        "distractor_confusion" => "MCQ distractor confusion",
        "spelling" => "Spelling under pressure",
        "paraphrase" => "Used your words, not the speaker's",
        "grammar_number" => "Number / article mismatch",
        "wrong_section" => "Right answer, wrong gap",
        "extra_info" => "Extra words in short answer",
        "empty" => "Items left blank",
        "detail_capture" => "Missed the exact detail",
        _ => code,
    };

    private static int? TryReadScaled(Evaluation evaluation)
        => TryReadScaled(evaluation.CriterionScoresJson);

    private static int? TryReadScaled(string? criterionScoresJson)
    {
        if (string.IsNullOrWhiteSpace(criterionScoresJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(criterionScoresJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                if (el.TryGetProperty("scaledScore", out var s) && s.ValueKind == JsonValueKind.Number)
                    return s.GetInt32();
            }
            return null;
        }
        catch (JsonException) { return null; }
    }

    private static Dictionary<string, string?> DeserializeAnswers(string? json)
        => JsonSupport.Deserialize<Dictionary<string, string?>>(json ?? string.Empty, new Dictionary<string, string?>());

    private static string? DeserializeRelationalAnswer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.String => doc.RootElement.GetString(),
                JsonValueKind.Number => doc.RootElement.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => doc.RootElement.ToString(),
            };
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static int Levenshtein(string a, string b)
    {
        if (a == b) return 0;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        if (a.Length > 64) a = a[..64];
        if (b.Length > 64) b = b[..64];
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }
}
