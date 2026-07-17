using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Writing;

// ─────────────────────────────────────────────────────────────────────────────
// Admin analytics + marking quality-control DTOs (spec §16). Mirrors
// lib/writing/types.ts WritingAdminAnalyticsDto / WritingMarkingQualityDto /
// WritingCriteriaScoresDto. PascalCase records serialise to camelCase JSON via
// the default ASP.NET Core (System.Text.Json Web) naming policy.
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WritingCriteriaScoresDto(
    double C1,
    double C2,
    double C3,
    double C4,
    double C5,
    double C6);

public sealed record WritingAnalyticsTotalsDto(
    int Tasks,
    int Submissions,
    int Reviewed,
    int Learners);

public sealed record WritingAverageBandByProfessionDto(
    string Profession,
    double AverageBand,
    int Attempts);

public sealed record WritingAverageBandByLetterTypeDto(
    string LetterType,
    double AverageBand,
    int Attempts);

public sealed record WritingHardestTaskDto(
    string TaskId,
    string Title,
    double AverageBand,
    int Attempts);

public sealed record WritingContentAggregateDto(
    string ItemText,
    int Count);

public sealed record WritingLanguageErrorDto(
    string RuleId,
    string RuleText,
    int Count,
    string? Criterion);

public sealed record WritingBucketCountDto(
    string BucketLabel,
    int Count);

public sealed record WritingPhaseSecondsDto(
    double Average,
    double Median);

public sealed record WritingAdminAnalyticsDto(
    WritingAnalyticsTotalsDto Totals,
    WritingCriteriaScoresDto AverageCriteria,
    IReadOnlyList<WritingAverageBandByProfessionDto> AverageBandByProfession,
    IReadOnlyList<WritingAverageBandByLetterTypeDto> AverageBandByLetterType,
    IReadOnlyList<WritingHardestTaskDto> HardestTasks,
    IReadOnlyList<WritingContentAggregateDto> CommonMissingContent,
    IReadOnlyList<WritingContentAggregateDto> CommonIrrelevantContent,
    IReadOnlyList<WritingLanguageErrorDto> CommonLanguageErrors,
    IReadOnlyList<WritingBucketCountDto> WordCountDistribution,
    WritingPhaseSecondsDto WritingPhaseSeconds,
    double AbandonmentRatePercent,
    double ResubmissionImprovementAverage);

public sealed record WritingTutorConsistencyDto(
    string TutorId,
    string? DisplayName,
    int Reviews,
    double AverageRawTotal,
    double LeniencyDelta,
    double AgreementCoefficient);

public sealed record WritingAiVsTutorVarianceDto(
    double MeanAbsoluteDelta,
    int Samples);

public sealed record WritingCriteriaDisagreementDto(
    string Criterion,
    double MeanAbsoluteDelta);

public sealed record WritingMarkingQualityDto(
    IReadOnlyList<WritingTutorConsistencyDto> TutorConsistency,
    WritingAiVsTutorVarianceDto AiVsTutorVariance,
    double AverageReviewTurnaroundHours,
    IReadOnlyList<WritingCriteriaDisagreementDto> CriteriaDisagreement,
    int ModerationsTriggered);

/// <summary>
/// Admin analytics + marking quality control for the Writing module (spec §16).
/// Aggregates submissions, AI grades, tutor reviews (score overrides + content
/// checklist verdicts), canon/rule violations, moderations, and tutor
/// calibrations. The "final" grade per submission is the tutor ScoreOverride
/// (merged over the AI grade) when a submitted review exists, else the AI grade.
/// </summary>
public interface IWritingAdminAnalyticsService
{
    Task<WritingAdminAnalyticsDto> GetOverviewAsync(
        string? profession,
        string? letterType,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken ct);

    Task<WritingMarkingQualityDto> GetMarkingQualityAsync(
        string? profession,
        string? letterType,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken ct);
}

public sealed class WritingAdminAnalyticsService : IWritingAdminAnalyticsService
{
    private readonly LearnerDbContext _db;
    private readonly ILogger<WritingAdminAnalyticsService> _logger;

    public WritingAdminAnalyticsService(LearnerDbContext db, ILogger<WritingAdminAnalyticsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Criterion keys in the ScoreOverride / checklist-verdict JSON maps (c1..c6).
    private static readonly string[] CriterionKeys = { "c1", "c2", "c3", "c4", "c5", "c6" };

    // Tutor content-checklist verdicts that count as "missing" content.
    private static readonly HashSet<string> MissingVerdicts =
        new(StringComparer.OrdinalIgnoreCase) { "missing", "inaccurate" };

    private const int TopN = 10;
    // Word count below which an attempt is treated as abandoned (proxy — there is
    // no dedicated abandoned status on WritingSubmission; see report notes).
    private const int AbandonmentWordThreshold = 20;

    public async Task<WritingAdminAnalyticsDto> GetOverviewAsync(
        string? profession,
        string? letterType,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken ct)
    {
        // 1) Resolve the in-scope scenarios (profession / letter-type filters) so we
        //    can both filter submissions and report task titles.
        var scenarioQuery = _db.WritingScenarios.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(profession))
            scenarioQuery = scenarioQuery.Where(s => s.Profession == profession);
        if (!string.IsNullOrWhiteSpace(letterType))
            scenarioQuery = scenarioQuery.Where(s => s.LetterType == letterType);

        var scenarios = await scenarioQuery
            .Select(s => new ScenarioMeta(s.Id, s.Title, s.Profession, s.LetterType, s.Status))
            .ToListAsync(ct);

        var scenarioById = scenarios.ToDictionary(s => s.Id);
        var inScopeScenarioIds = scenarioById.Keys.ToHashSet();

        // tasks total = published scenarios (within filter scope).
        var tasksTotal = scenarios.Count(s => s.Status == "published");

        // 2) Load in-scope submissions (date filter on SubmittedAt + scenario scope).
        var submissionQuery = _db.WritingSubmissions.AsNoTracking()
            .Where(s => inScopeScenarioIds.Contains(s.ScenarioId));
        if (fromDate.HasValue)
            submissionQuery = submissionQuery.Where(s => s.SubmittedAt >= fromDate.Value);
        if (toDate.HasValue)
            submissionQuery = submissionQuery.Where(s => s.SubmittedAt <= toDate.Value);

        var submissions = await submissionQuery
            .Select(s => new SubmissionMeta(
                s.Id,
                s.UserId,
                s.ScenarioId,
                s.WordCount,
                s.TimeSpentSeconds,
                s.Status,
                s.IsRevision,
                s.OriginalSubmissionId))
            .ToListAsync(ct);

        if (submissions.Count == 0)
        {
            // Empty cohort — return a fully-zeroed payload without further queries.
            return EmptyOverview(tasksTotal);
        }

        var submissionIds = submissions.Select(s => s.Id).ToHashSet();
        var submissionById = submissions.ToDictionary(s => s.Id);

        // 3) AI grades for the in-scope submissions.
        var grades = await _db.WritingGrades.AsNoTracking()
            .Where(g => submissionIds.Contains(g.SubmissionId))
            .Select(g => new GradeMeta(
                g.SubmissionId,
                g.C1Purpose, g.C2Content, g.C3Conciseness,
                g.C4Genre, g.C5Organisation, g.C6Language,
                g.RawTotal, g.EstimatedBand))
            .ToListAsync(ct);
        var gradeBySubmission = grades
            .GroupBy(g => g.SubmissionId)
            .ToDictionary(g => g.Key, g => g.First());

        // 4) Submitted tutor reviews (latest per submission) for score overrides +
        //    content-checklist verdicts.
        var reviews = await _db.WritingTutorReviews.AsNoTracking()
            .Where(r => r.Status == "submitted" && submissionIds.Contains(r.SubmissionId))
            .Select(r => new ReviewMeta(
                r.SubmissionId,
                r.TutorId,
                r.ScoreOverrideJson,
                r.ContentChecklistVerdictJson,
                r.SubmittedAt))
            .ToListAsync(ct);
        var reviewBySubmission = reviews
            .GroupBy(r => r.SubmissionId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.SubmittedAt ?? DateTimeOffset.MinValue).First());

        // 5) Compose the FINAL grade per submission (tutor override merged over AI).
        var finalGrades = new Dictionary<Guid, FinalGrade>();
        foreach (var sub in submissions)
        {
            if (!gradeBySubmission.TryGetValue(sub.Id, out var ai)) continue;

            var overrides = reviewBySubmission.TryGetValue(sub.Id, out var rev)
                ? WritingDualAssessmentService.ParseOverride(rev.ScoreOverrideJson)
                : new Dictionary<string, int>();

            var criteria = new double[6]
            {
                Pick(overrides, "c1", ai.C1),
                Pick(overrides, "c2", ai.C2),
                Pick(overrides, "c3", ai.C3),
                Pick(overrides, "c4", ai.C4),
                Pick(overrides, "c5", ai.C5),
                Pick(overrides, "c6", ai.C6),
            };
            var rawTotal = overrides.Count > 0 ? criteria.Sum() : ai.RawTotal;
            // Tutor reviews carry no band; the band aggregations use the AI grade's
            // EstimatedBand (see report notes).
            finalGrades[sub.Id] = new FinalGrade(criteria, rawTotal, ai.EstimatedBand);
        }

        var gradedSubmissions = submissions
            .Where(s => finalGrades.ContainsKey(s.Id))
            .ToList();

        // ── totals ──
        var totals = new WritingAnalyticsTotalsDto(
            Tasks: tasksTotal,
            Submissions: submissions.Count,
            Reviewed: submissions.Count(s => reviewBySubmission.ContainsKey(s.Id)),
            Learners: submissions.Select(s => s.UserId).Distinct().Count());

        // ── averageCriteria (mean c1..c6 across graded submissions) ──
        var averageCriteria = gradedSubmissions.Count == 0
            ? new WritingCriteriaScoresDto(0, 0, 0, 0, 0, 0)
            : new WritingCriteriaScoresDto(
                Round2(gradedSubmissions.Average(s => finalGrades[s.Id].Criteria[0])),
                Round2(gradedSubmissions.Average(s => finalGrades[s.Id].Criteria[1])),
                Round2(gradedSubmissions.Average(s => finalGrades[s.Id].Criteria[2])),
                Round2(gradedSubmissions.Average(s => finalGrades[s.Id].Criteria[3])),
                Round2(gradedSubmissions.Average(s => finalGrades[s.Id].Criteria[4])),
                Round2(gradedSubmissions.Average(s => finalGrades[s.Id].Criteria[5])));

        // ── averageBandByProfession / averageBandByLetterType ──
        var byProfession = gradedSubmissions
            .Where(s => scenarioById.ContainsKey(s.ScenarioId))
            .GroupBy(s => scenarioById[s.ScenarioId].Profession)
            .Select(g => new WritingAverageBandByProfessionDto(
                Profession: g.Key,
                AverageBand: RoundBand(g.Average(s => finalGrades[s.Id].EstimatedBand)),
                Attempts: g.Count()))
            .OrderByDescending(x => x.Attempts)
            .ToList();

        var byLetterType = gradedSubmissions
            .Where(s => scenarioById.ContainsKey(s.ScenarioId))
            .GroupBy(s => scenarioById[s.ScenarioId].LetterType)
            .Select(g => new WritingAverageBandByLetterTypeDto(
                LetterType: g.Key,
                AverageBand: RoundBand(g.Average(s => finalGrades[s.Id].EstimatedBand)),
                Attempts: g.Count()))
            .OrderByDescending(x => x.Attempts)
            .ToList();

        // ── hardestTasks (lowest mean band; min 1 attempt; top 10) ──
        var hardestTasks = gradedSubmissions
            .Where(s => scenarioById.ContainsKey(s.ScenarioId))
            .GroupBy(s => s.ScenarioId)
            .Where(g => g.Count() >= 1)
            .Select(g => new WritingHardestTaskDto(
                TaskId: g.Key.ToString(),
                Title: scenarioById[g.Key].Title,
                AverageBand: RoundBand(g.Average(s => finalGrades[s.Id].EstimatedBand)),
                Attempts: g.Count()))
            .OrderBy(x => x.AverageBand)
            .ThenByDescending(x => x.Attempts)
            .Take(TopN)
            .ToList();

        // ── commonMissingContent / commonIrrelevantContent (tutor verdicts) ──
        var (missingContent, irrelevantContent) =
            await AggregateContentVerdictsAsync(reviewBySubmission.Values, ct);

        // ── commonLanguageErrors (canon + rule violations) ──
        var languageErrors = await AggregateLanguageErrorsAsync(submissionIds, ct);

        // ── wordCountDistribution ──
        var wordCountDistribution = BuildWordCountDistribution(submissions);

        // ── writingPhaseSeconds (avg + median TimeSpentSeconds) ──
        var times = submissions.Select(s => (double)s.TimeSpentSeconds).ToList();
        var writingPhase = new WritingPhaseSecondsDto(
            Average: times.Count == 0 ? 0 : Round2(times.Average()),
            Median: Round2(Median(times)));

        // ── abandonmentRatePercent ──
        // No dedicated abandoned status exists; treat Status=="cancelled" OR a
        // word count below the threshold as abandoned (proxy — see report notes).
        var abandoned = submissions.Count(s =>
            string.Equals(s.Status, "cancelled", StringComparison.OrdinalIgnoreCase)
            || s.WordCount < AbandonmentWordThreshold);
        var abandonmentRate = submissions.Count == 0
            ? 0
            : Round2(100.0 * abandoned / submissions.Count);

        // ── resubmissionImprovementAverage (revision rawTotal − original) ──
        var resubmissionImprovement = ComputeResubmissionImprovement(
            submissions, submissionById, finalGrades);

        return new WritingAdminAnalyticsDto(
            Totals: totals,
            AverageCriteria: averageCriteria,
            AverageBandByProfession: byProfession,
            AverageBandByLetterType: byLetterType,
            HardestTasks: hardestTasks,
            CommonMissingContent: missingContent,
            CommonIrrelevantContent: irrelevantContent,
            CommonLanguageErrors: languageErrors,
            WordCountDistribution: wordCountDistribution,
            WritingPhaseSeconds: writingPhase,
            AbandonmentRatePercent: abandonmentRate,
            ResubmissionImprovementAverage: resubmissionImprovement);
    }

    public async Task<WritingMarkingQualityDto> GetMarkingQualityAsync(
        string? profession,
        string? letterType,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken ct)
    {
        // Scope by scenario filters, then by submissions within the date window.
        var scenarioQuery = _db.WritingScenarios.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(profession))
            scenarioQuery = scenarioQuery.Where(s => s.Profession == profession);
        if (!string.IsNullOrWhiteSpace(letterType))
            scenarioQuery = scenarioQuery.Where(s => s.LetterType == letterType);
        var inScopeScenarioIds = (await scenarioQuery.Select(s => s.Id).ToListAsync(ct)).ToHashSet();

        var submissionQuery = _db.WritingSubmissions.AsNoTracking()
            .Where(s => inScopeScenarioIds.Contains(s.ScenarioId));
        if (fromDate.HasValue)
            submissionQuery = submissionQuery.Where(s => s.SubmittedAt >= fromDate.Value);
        if (toDate.HasValue)
            submissionQuery = submissionQuery.Where(s => s.SubmittedAt <= toDate.Value);

        var submissions = await submissionQuery
            .Select(s => new { s.Id, s.SubmittedAt })
            .ToListAsync(ct);

        if (submissions.Count == 0)
            return EmptyMarkingQuality();

        var submissionIds = submissions.Select(s => s.Id).ToHashSet();
        var submittedAtById = submissions.ToDictionary(s => s.Id, s => s.SubmittedAt);

        // AI grades (per-criterion + raw total) for variance/disagreement.
        var grades = await _db.WritingGrades.AsNoTracking()
            .Where(g => submissionIds.Contains(g.SubmissionId))
            .Select(g => new GradeMeta(
                g.SubmissionId,
                g.C1Purpose, g.C2Content, g.C3Conciseness,
                g.C4Genre, g.C5Organisation, g.C6Language,
                g.RawTotal, g.EstimatedBand))
            .ToListAsync(ct);
        var gradeBySubmission = grades
            .GroupBy(g => g.SubmissionId)
            .ToDictionary(g => g.Key, g => g.First());

        // Submitted tutor reviews in scope.
        var reviews = await _db.WritingTutorReviews.AsNoTracking()
            .Where(r => r.Status == "submitted" && submissionIds.Contains(r.SubmissionId))
            .Select(r => new ReviewMeta(
                r.SubmissionId,
                r.TutorId,
                r.ScoreOverrideJson,
                r.ContentChecklistVerdictJson,
                r.SubmittedAt))
            .ToListAsync(ct);

        // ── cohort mean raw total (across all submitted reviews) ──
        var reviewRawTotals = reviews
            .Select(r => (double)WritingDualAssessmentService.SumOverride(r.ScoreOverrideJson))
            .ToList();
        var cohortMean = reviewRawTotals.Count == 0 ? 0 : reviewRawTotals.Average();

        // ── tutorConsistency (per tutor) ──
        var tutorIds = reviews.Select(r => r.TutorId).Distinct().ToList();
        var tutors = await _db.Users.AsNoTracking()
            .Where(u => tutorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToListAsync(ct);
        var displayNameById = tutors.ToDictionary(u => u.Id, u => u.DisplayName);

        // Latest calibration per tutor (agreement coefficient).
        var calibrations = await _db.WritingTutorCalibrations.AsNoTracking()
            .Where(c => tutorIds.Contains(c.TutorId))
            .Select(c => new { c.TutorId, c.AgreementCoefficient, c.LastCalibratedAt })
            .ToListAsync(ct);
        var coefficientByTutor = calibrations
            .GroupBy(c => c.TutorId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(c => c.LastCalibratedAt).First().AgreementCoefficient);

        var tutorConsistency = reviews
            .GroupBy(r => r.TutorId)
            .Select(g =>
            {
                var avg = g.Average(r => (double)WritingDualAssessmentService.SumOverride(r.ScoreOverrideJson));
                return new WritingTutorConsistencyDto(
                    TutorId: g.Key.ToString(),
                    DisplayName: displayNameById.TryGetValue(g.Key, out var dn) ? dn : null,
                    Reviews: g.Count(),
                    AverageRawTotal: Round2(avg),
                    LeniencyDelta: Round2(avg - cohortMean),
                    AgreementCoefficient: coefficientByTutor.TryGetValue(g.Key, out var coeff)
                        ? Round2((double)coeff)
                        : 0);
            })
            .OrderByDescending(x => x.Reviews)
            .ToList();

        // ── aiVsTutorVariance (submissions having both AI grade + tutor override) ──
        var bothDeltas = new List<double>();
        // ── criteriaDisagreement accumulators (per criterion) ──
        var critDeltaSums = new double[6];
        var critDeltaCounts = new int[6];
        // ── turnaround accumulator ──
        var turnaroundHours = new List<double>();

        foreach (var rev in reviews)
        {
            // turnaround (independent of AI grade presence)
            if (rev.SubmittedAt.HasValue
                && submittedAtById.TryGetValue(rev.SubmissionId, out var subAt))
            {
                var hours = (rev.SubmittedAt.Value - subAt).TotalHours;
                if (hours >= 0) turnaroundHours.Add(hours);
            }

            if (!gradeBySubmission.TryGetValue(rev.SubmissionId, out var ai)) continue;
            var overrides = WritingDualAssessmentService.ParseOverride(rev.ScoreOverrideJson);
            if (overrides.Count == 0) continue;

            var tutorTotal = overrides.Values.Sum();
            bothDeltas.Add(Math.Abs(tutorTotal - ai.RawTotal));

            var aiCriteria = new[] { ai.C1, ai.C2, ai.C3, ai.C4, ai.C5, ai.C6 };
            for (var i = 0; i < CriterionKeys.Length; i++)
            {
                if (overrides.TryGetValue(CriterionKeys[i], out var tutorScore))
                {
                    critDeltaSums[i] += Math.Abs(tutorScore - aiCriteria[i]);
                    critDeltaCounts[i] += 1;
                }
            }
        }

        var aiVsTutor = new WritingAiVsTutorVarianceDto(
            MeanAbsoluteDelta: bothDeltas.Count == 0 ? 0 : Round2(bothDeltas.Average()),
            Samples: bothDeltas.Count);

        var criteriaDisagreement = CriterionKeys
            .Select((key, i) => new WritingCriteriaDisagreementDto(
                Criterion: key,
                MeanAbsoluteDelta: critDeltaCounts[i] == 0
                    ? 0
                    : Round2(critDeltaSums[i] / critDeltaCounts[i])))
            .ToList();

        var avgTurnaround = turnaroundHours.Count == 0 ? 0 : Round2(turnaroundHours.Average());

        // ── moderationsTriggered (in-scope submissions) ──
        var moderationsTriggered = await _db.WritingModerations.AsNoTracking()
            .Where(m => submissionIds.Contains(m.SubmissionId))
            .CountAsync(ct);

        return new WritingMarkingQualityDto(
            TutorConsistency: tutorConsistency,
            AiVsTutorVariance: aiVsTutor,
            AverageReviewTurnaroundHours: avgTurnaround,
            CriteriaDisagreement: criteriaDisagreement,
            ModerationsTriggered: moderationsTriggered);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<(IReadOnlyList<WritingContentAggregateDto> Missing,
                        IReadOnlyList<WritingContentAggregateDto> Irrelevant)>
        AggregateContentVerdictsAsync(IEnumerable<ReviewMeta> reviews, CancellationToken ct)
    {
        // itemId → count, split by missing vs irrelevant.
        await Task.CompletedTask;
        var missingCounts = new Dictionary<string, int>();
        var irrelevantCounts = new Dictionary<string, int>();

        foreach (var rev in reviews)
        {
            if (string.IsNullOrWhiteSpace(rev.ContentChecklistVerdictJson)) continue;
            Dictionary<string, string>? verdicts;
            try
            {
                verdicts = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    rev.ContentChecklistVerdictJson, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }
            if (verdicts is null) continue;

            foreach (var (itemId, verdict) in verdicts)
            {
                if (string.IsNullOrWhiteSpace(verdict)) continue;
                if (MissingVerdicts.Contains(verdict))
                    missingCounts[itemId] = missingCounts.GetValueOrDefault(itemId) + 1;
                else if (string.Equals(verdict, "irrelevant", StringComparison.OrdinalIgnoreCase))
                    irrelevantCounts[itemId] = irrelevantCounts.GetValueOrDefault(itemId) + 1;
            }
        }

        if (missingCounts.Count == 0 && irrelevantCounts.Count == 0)
        {
            return (Array.Empty<WritingContentAggregateDto>(),
                    Array.Empty<WritingContentAggregateDto>());
        }

        // Content checklists were removed from the writing task, so there is no
        // item-text table to resolve against. Tutor content-checklist verdicts are
        // no longer captured, so these aggregates are effectively empty; fall back
        // to the raw verdict id as the display text.
        var textByItemId = new Dictionary<string, string>();

        var missing = missingCounts
            .Select(kv => new WritingContentAggregateDto(
                ItemText: textByItemId.TryGetValue(kv.Key, out var t) ? t : kv.Key,
                Count: kv.Value))
            .OrderByDescending(x => x.Count)
            .Take(TopN)
            .ToList();

        var irrelevant = irrelevantCounts
            .Select(kv => new WritingContentAggregateDto(
                ItemText: textByItemId.TryGetValue(kv.Key, out var t) ? t : kv.Key,
                Count: kv.Value))
            .OrderByDescending(x => x.Count)
            .Take(TopN)
            .ToList();

        return (missing, irrelevant);
    }

    private async Task<IReadOnlyList<WritingLanguageErrorDto>> AggregateLanguageErrorsAsync(
        HashSet<Guid> submissionIds, CancellationToken ct)
    {
        // Aggregate canon violations + rule violations by ruleId. Canon violations
        // key directly to WritingSubmission.Id and carry no descriptive rule text
        // (only a snippet), so their RuleText is left null and the RuleId speaks
        // for itself. Rule violations live in the coach subsystem keyed by
        // AttemptId (string), carry a learner-facing Message, and have no pinned
        // criterion — language errors default to criterion c6 (Language).
        var canon = await _db.WritingCanonViolations.AsNoTracking()
            .Where(v => submissionIds.Contains(v.SubmissionId))
            .Select(v => new ViolationMeta(v.RuleId, null, null))
            .ToListAsync(ct);

        // Rule violations are keyed by AttemptId (string), not by the Guid
        // submission id. Match on the id string form so the source degrades
        // gracefully to empty when the two id spaces do not overlap.
        var submissionIdStrings = submissionIds.Select(id => id.ToString()).ToHashSet();
        var rule = await _db.WritingRuleViolations.AsNoTracking()
            .Where(v => submissionIdStrings.Contains(v.AttemptId))
            .Select(v => new ViolationMeta(v.RuleId, v.Message, null))
            .ToListAsync(ct);

        var combined = canon.Concat(rule)
            .Where(v => !string.IsNullOrWhiteSpace(v.RuleId))
            .GroupBy(v => v.RuleId)
            .Select(g => new WritingLanguageErrorDto(
                RuleId: g.Key,
                RuleText: g.Select(x => x.RuleText)
                           .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? string.Empty,
                Count: g.Count(),
                // Prefer an explicit criterion mapping from a rule violation; default c6 (Language).
                Criterion: g.Select(x => x.CriterionCode)
                            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? "c6"))
            .OrderByDescending(x => x.Count)
            .Take(TopN)
            .ToList();

        return combined;
    }

    private static IReadOnlyList<WritingBucketCountDto> BuildWordCountDistribution(
        IReadOnlyCollection<SubmissionMeta> submissions)
    {
        // Buckets: <150, 150-179, 180-200, 201-230, >230.
        int b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0;
        foreach (var s in submissions)
        {
            var w = s.WordCount;
            if (w < 150) b1++;
            else if (w < 180) b2++;
            else if (w <= 200) b3++;
            else if (w <= 230) b4++;
            else b5++;
        }
        return new List<WritingBucketCountDto>
        {
            new("<150", b1),
            new("150-179", b2),
            new("180-200", b3),
            new("201-230", b4),
            new(">230", b5),
        };
    }

    private static double ComputeResubmissionImprovement(
        IReadOnlyList<SubmissionMeta> submissions,
        IReadOnlyDictionary<Guid, SubmissionMeta> submissionById,
        IReadOnlyDictionary<Guid, FinalGrade> finalGrades)
    {
        var deltas = new List<double>();
        foreach (var s in submissions)
        {
            if (!s.IsRevision || s.OriginalSubmissionId is null) continue;
            if (!finalGrades.TryGetValue(s.Id, out var revisionGrade)) continue;
            // Original may fall outside the in-scope set if it predates the date
            // window; skip when its final grade is unavailable.
            if (!finalGrades.TryGetValue(s.OriginalSubmissionId.Value, out var originalGrade)) continue;
            deltas.Add(revisionGrade.RawTotal - originalGrade.RawTotal);
        }
        return deltas.Count == 0 ? 0 : Round2(deltas.Average());
    }

    private static double Pick(IReadOnlyDictionary<string, int> overrides, string key, int aiValue)
        => overrides.TryGetValue(key, out var v) ? v : aiValue;

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var ordered = values.OrderBy(v => v).ToList();
        var mid = ordered.Count / 2;
        return ordered.Count % 2 == 1
            ? ordered[mid]
            : (ordered[mid - 1] + ordered[mid]) / 2.0;
    }

    private static double Round2(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static double RoundBand(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private static WritingAdminAnalyticsDto EmptyOverview(int tasksTotal) => new(
        Totals: new WritingAnalyticsTotalsDto(tasksTotal, 0, 0, 0),
        AverageCriteria: new WritingCriteriaScoresDto(0, 0, 0, 0, 0, 0),
        AverageBandByProfession: Array.Empty<WritingAverageBandByProfessionDto>(),
        AverageBandByLetterType: Array.Empty<WritingAverageBandByLetterTypeDto>(),
        HardestTasks: Array.Empty<WritingHardestTaskDto>(),
        CommonMissingContent: Array.Empty<WritingContentAggregateDto>(),
        CommonIrrelevantContent: Array.Empty<WritingContentAggregateDto>(),
        CommonLanguageErrors: Array.Empty<WritingLanguageErrorDto>(),
        WordCountDistribution: BuildWordCountDistribution(Array.Empty<SubmissionMeta>()),
        WritingPhaseSeconds: new WritingPhaseSecondsDto(0, 0),
        AbandonmentRatePercent: 0,
        ResubmissionImprovementAverage: 0);

    private static WritingMarkingQualityDto EmptyMarkingQuality() => new(
        TutorConsistency: Array.Empty<WritingTutorConsistencyDto>(),
        AiVsTutorVariance: new WritingAiVsTutorVarianceDto(0, 0),
        AverageReviewTurnaroundHours: 0,
        CriteriaDisagreement: CriterionKeys
            .Select(k => new WritingCriteriaDisagreementDto(k, 0))
            .ToList(),
        ModerationsTriggered: 0);

    // ── projection records (kept private to this service) ──
    private sealed record ScenarioMeta(Guid Id, string Title, string Profession, string LetterType, string Status);

    private sealed record SubmissionMeta(
        Guid Id,
        string UserId,
        Guid ScenarioId,
        int WordCount,
        int TimeSpentSeconds,
        string Status,
        bool IsRevision,
        Guid? OriginalSubmissionId);

    private sealed record GradeMeta(
        Guid SubmissionId,
        int C1, int C2, int C3, int C4, int C5, int C6,
        int RawTotal,
        double EstimatedBand);

    private sealed record ReviewMeta(
        Guid SubmissionId,
        string TutorId,
        string? ScoreOverrideJson,
        string? ContentChecklistVerdictJson,
        DateTimeOffset? SubmittedAt);

    private sealed record FinalGrade(double[] Criteria, double RawTotal, double EstimatedBand);

    private sealed record ViolationMeta(string RuleId, string? RuleText, string? CriterionCode);
}
