using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services;

public partial class ExpertService
{
    public async Task<ExpertMetricsResponse> GetMetricsAsync(string reviewerId, int days, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var clampedDays = Math.Clamp(days, 1, 180);
        var windowStart = DateTimeOffset.UtcNow.Date.AddDays(-(clampedDays - 1));

        var assignments = await db.ExpertReviewAssignments
            .AsNoTracking()
            .Where(assignment => assignment.AssignedReviewerId == reviewerId)
            .ToListAsync(ct);

        var reviewIds = assignments.Select(assignment => assignment.ReviewRequestId).Distinct().ToList();
        var reviewRequests = reviewIds.Count == 0
            ? []
            : await db.ReviewRequests
                .AsNoTracking()
                .Where(reviewRequest => reviewIds.Contains(reviewRequest.Id))
                .ToListAsync(ct);

        var handledReviewIds = reviewRequests.Select(reviewRequest => reviewRequest.Id).Distinct().ToHashSet(StringComparer.Ordinal);
        var completedReviews = reviewRequests
            .Where(reviewRequest => reviewRequest.State == ReviewRequestState.Completed && reviewRequest.CompletedAt is not null && reviewRequest.CompletedAt >= windowStart)
            .ToList();

        var totalReviewsCompleted = completedReviews.Count;
        var draftReviews = await db.ExpertReviewDrafts
            .AsNoTracking()
            .CountAsync(draft => draft.ReviewerId == reviewerId && (draft.State == null || draft.State != "submitted"), ct);

        var slaHitRate = totalReviewsCompleted == 0
            ? 100.0
            : Math.Round(completedReviews.Count(reviewRequest => (reviewRequest.CompletedAt ?? reviewRequest.CreatedAt) <= CalculateSlaDueAt(reviewRequest)) * 100.0 / totalReviewsCompleted, 1);

        var avgTurnaroundHours = totalReviewsCompleted == 0
            ? 0.0
            : Math.Round(completedReviews.Average(reviewRequest => ((reviewRequest.CompletedAt ?? reviewRequest.CreatedAt) - reviewRequest.CreatedAt).TotalHours), 1);

        var completedCalibrationResults = await db.ExpertCalibrationResults
            .AsNoTracking()
            .Where(result => result.ReviewerId == reviewerId && !result.IsDraft)
            .ToListAsync(ct);
        var completedCalibrationCaseIds = completedCalibrationResults.Select(result => result.CalibrationCaseId).Distinct().ToArray();
        var completedCalibrationCases = await db.ExpertCalibrationCases
            .AsNoTracking()
            .Where(calibrationCase => completedCalibrationCaseIds.Contains(calibrationCase.Id))
            .ToDictionaryAsync(calibrationCase => calibrationCase.Id, ct);
        var calibrationAlignment = completedCalibrationResults.Count == 0
            ? 100.0
            : completedCalibrationResults
                .Select(result => completedCalibrationCases.TryGetValue(result.CalibrationCaseId, out var calibrationCase)
                    ? ResolveCalibrationAlignment(calibrationCase, result)
                    : result.AlignmentScore)
                .Average();

        var reworkCount = assignments.Count(assignment => !string.IsNullOrWhiteSpace(assignment.ReasonCode) && !string.Equals(assignment.ReasonCode, "submitted", StringComparison.OrdinalIgnoreCase));
        var reworkRate = handledReviewIds.Count == 0
            ? 0.0
            : Math.Round(reworkCount * 100.0 / handledReviewIds.Count, 1);

        var completionData = Enumerable.Range(0, clampedDays)
            .Select(offset =>
            {
                var day = windowStart.AddDays(offset);
                var count = completedReviews.Count(reviewRequest => (reviewRequest.CompletedAt ?? reviewRequest.CreatedAt).Date == day.Date);
                return new ExpertCompletionPointResponse(day.ToString("ddd", CultureInfo.InvariantCulture), count);
            })
            .ToList();

        return new ExpertMetricsResponse(
            new ExpertMetricsSummaryResponse(
                totalReviewsCompleted,
                draftReviews,
                slaHitRate,
                Math.Round(calibrationAlignment, 1),
                reworkRate,
                avgTurnaroundHours),
            completionData,
            clampedDays,
            DateTimeOffset.UtcNow);
    }

    // ══════════════════════════════════════════════════════
    // X3 · Expert Scoring Quality Metrics
    // ══════════════════════════════════════════════════════

    public async Task<object> GetScoringQualityMetricsAsync(string reviewerId, int days, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var windowStart = DateTimeOffset.UtcNow.Date.AddDays(-(Math.Clamp(days, 1, 180) - 1));

        // Get this expert's completed reviews within window
        var assignments = await db.ExpertReviewAssignments
            .Where(a => a.AssignedReviewerId == reviewerId && a.ClaimState == ExpertAssignmentState.Released
                        && a.ReleasedAt >= windowStart && a.ReasonCode == "submitted")
            .ToListAsync(ct);

        var reviewRequestIds = assignments.Select(a => a.ReviewRequestId).ToList();

        // Get all drafts for these reviews
        var drafts = await db.ExpertReviewDrafts
            .Where(d => d.ReviewerId == reviewerId && reviewRequestIds.Contains(d.ReviewRequestId) && d.State == "submitted")
            .ToListAsync(ct);

        // Get evaluations for these review requests (AI scores for comparison)
        var attemptIds = await db.ReviewRequests
            .Where(r => reviewRequestIds.Contains(r.Id))
            .Select(r => r.AttemptId)
            .ToListAsync(ct);

        var evaluations = await db.Evaluations
            .Where(e => attemptIds.Contains(e.AttemptId) && e.State == AsyncState.Completed)
            .ToListAsync(ct);

        // Calculate scoring distribution per criterion
        var scoringDistribution = new Dictionary<string, List<int>>();
        foreach (var draft in drafts)
        {
            var rubricEntries = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(draft.RubricEntriesJson ?? "[]", []);
            foreach (var entry in rubricEntries)
            {
                var criterion = entry.TryGetValue("criterionCode", out var cc) ? cc?.ToString() ?? "" : "";
                if (string.IsNullOrEmpty(criterion)) continue;

                if (!scoringDistribution.ContainsKey(criterion))
                    scoringDistribution[criterion] = [];

                if (entry.TryGetValue("score", out var sv) && sv is not null)
                {
                    if (sv is System.Text.Json.JsonElement je && je.TryGetInt32(out var intVal))
                        scoringDistribution[criterion].Add(intVal);
                    else if (int.TryParse(sv.ToString(), out var parsed))
                        scoringDistribution[criterion].Add(parsed);
                }
            }
        }

        // AI-Human agreement: compare expert scores to AI evaluation scores
        var aiHumanDifferences = new List<double>();
        foreach (var draft in drafts)
        {
            var rr = await db.ReviewRequests.FirstOrDefaultAsync(r => r.Id == draft.ReviewRequestId, ct);
            if (rr == null) continue;

            var eval = evaluations.FirstOrDefault(e => e.AttemptId == rr.AttemptId);
            if (eval == null) continue;

            var expertScores = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(draft.RubricEntriesJson ?? "[]", []);
            var aiScores = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(eval.CriterionScoresJson, []);

            var expertAvg = expertScores.Average(s =>
            {
                if (s.TryGetValue("score", out var sv) && sv is not null)
                {
                    if (sv is System.Text.Json.JsonElement je && je.TryGetDouble(out var d)) return d;
                    if (double.TryParse(sv.ToString(), out var p)) return p;
                }
                return 0.0;
            });
            var aiAvg = aiScores.Average(s =>
            {
                if (s.TryGetValue("score", out var sv) && sv is not null)
                {
                    if (sv is System.Text.Json.JsonElement je && je.TryGetDouble(out var d)) return d;
                    if (double.TryParse(sv.ToString(), out var p)) return p;
                }
                return 0.0;
            });

            aiHumanDifferences.Add(Math.Abs(expertAvg - aiAvg));
        }

        // Calibration drift — track average score over time
        var chronologicalDrafts = drafts.OrderBy(d => d.DraftSavedAt).ToList();
        var calibrationTrend = new List<object>();
        for (var i = 0; i < chronologicalDrafts.Count; i += Math.Max(1, chronologicalDrafts.Count / 10))
        {
            var d = chronologicalDrafts[i];
            var rubric = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(d.RubricEntriesJson ?? "[]", []);
            var avg = rubric.Count > 0 ? rubric.Average(r =>
            {
                if (r.TryGetValue("score", out var sv) && sv is not null)
                {
                    if (sv is System.Text.Json.JsonElement je && je.TryGetDouble(out var dd)) return dd;
                    if (double.TryParse(sv.ToString(), out var p)) return p;
                }
                return 0.0;
            }) : 0;

            calibrationTrend.Add(new { date = d.DraftSavedAt, averageScore = Math.Round(avg, 2) });
        }

        return new
        {
            totalReviewsInWindow = drafts.Count,
            days,
            scoringDistribution = scoringDistribution.Select(kv => new
            {
                criterion = kv.Key,
                mean = kv.Value.Count > 0 ? Math.Round(kv.Value.Average(), 2) : 0,
                stdDev = kv.Value.Count > 1 ? Math.Round(Math.Sqrt(kv.Value.Average(v => Math.Pow(v - kv.Value.Average(), 2))), 2) : 0,
                min = kv.Value.Count > 0 ? kv.Value.Min() : 0,
                max = kv.Value.Count > 0 ? kv.Value.Max() : 0,
                count = kv.Value.Count
            }).ToList(),
            aiHumanAgreement = new
            {
                comparisons = aiHumanDifferences.Count,
                averageDifference = aiHumanDifferences.Count > 0 ? Math.Round(aiHumanDifferences.Average(), 2) : 0,
                maxDifference = aiHumanDifferences.Count > 0 ? Math.Round(aiHumanDifferences.Max(), 2) : 0,
                agreementRate = aiHumanDifferences.Count > 0
                    ? Math.Round(aiHumanDifferences.Count(d => d <= 1.0) * 100.0 / aiHumanDifferences.Count, 1) : 0
            },
            calibrationTrend,
            reworkRate = assignments.Count > 0
                ? Math.Round(assignments.Count(a => a.ReasonCode != "submitted") * 100.0 / assignments.Count, 1) : 0
        };
    }
}
