using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts.Submissions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Progress;

namespace OetLearner.Api.Services.Submissions;

/// <summary>
/// Authoritative service for the learner-facing Submission History
/// experience (<c>/submissions</c>, <c>/submissions/{id}</c>,
/// <c>/submissions/compare</c>).
///
/// Responsibilities:
///   1. <b>Evidence-only listing</b>: never surface Abandoned / Paused /
///      NotStarted attempts — only Submitted / Evaluating / Completed /
///      Failed rows reach the UI.
///   2. <b>Keyset pagination</b> on <c>(SubmittedAtUtc, AttemptId)</c> so
///      the page scales linearly with history size (no Skip/Take cliff).
///   3. <b>Rich filtering &amp; facets</b>: sub-test, context, review
///      status, pass/fail, date range, free-text task search, include
///      hidden toggle.
///   4. <b>Canonical scoring</b>: every item carries a scaled 0–500 value
///      plus a country-aware Writing pass/fail flag via <see cref="OetScoring"/>.
///      The frontend never recomputes pass/fail from a raw percentage.
///   5. <b>Real comparison</b>: <see cref="CompareAsync"/> produces a
///      deterministic diff (scaled delta, criterion deltas, turnaround
///      delta) instead of the old hardcoded placeholder sentence.
///   6. <b>Detail composition</b>: a single <see cref="GetDetailAsync"/>
///      call assembles evidence, review lineage, revision lineage, and
///      next-action flags — replacing the N+1 frontend stitching in
///      <c>lib/api.ts</c>.
///   7. <b>Soft hide/unhide</b>: learner preference only — hidden attempts
///      remain in analytics, readiness, progress, and cohort aggregates.
/// </summary>
public sealed class SubmissionHistoryService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 50;
    private const int SparklineMaxPoints = 12;

    private readonly LearnerDbContext _db;

    public SubmissionHistoryService(LearnerDbContext db)
    {
        _db = db;
    }

    // -----------------------------------------------------------------------
    // List
    // -----------------------------------------------------------------------

    public async Task<SubmissionListResponse> ListAsync(
        string userId,
        SubmissionListQuery query,
        CancellationToken ct)
    {
        var goal = await _db.Goals.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
        var country = OetScoring.NormalizeWritingCountry(goal?.TargetCountry);

        // Build a simple filtered query of Attempt rows first (no cross-join
        // SQL translation headaches), then hydrate the related data in a
        // single extra query per table. Keeps the whole listing to 3
        // round-trips regardless of page size.
        var attemptsQuery = BuildAttemptsQuery(userId, query);

        // ── Facet counts (respect filters except include-hidden semantics).
        var facets = await ComputeFacetsAsync(userId, query, country, ct);

        // ── Total (current filter combination).
        var total = await attemptsQuery.CountAsync(ct);

        // ── Ordering + keyset cursor.
        var ordered = ApplyAttemptOrdering(attemptsQuery, query.Sort);

        var limit = Math.Clamp(query.Limit ?? DefaultPageSize, 1, MaxPageSize);
        var cursorFilter = DecodeCursor(query.Cursor, query.Sort);
        if (cursorFilter is { } cf)
        {
            ordered = ApplyAttemptCursorFilter(ordered, cf, query.Sort);
        }

        var attempts = await ordered.Take(limit + 1).ToListAsync(ct);

        string? nextCursor = null;
        if (attempts.Count > limit)
        {
            var cursorRow = attempts[limit - 1];
            nextCursor = EncodeCursor(cursorRow.SubmittedAt ?? cursorRow.StartedAt, cursorRow.Id, query.Sort);
            attempts = attempts.Take(limit).ToList();
        }

        var attemptIds = attempts.Select(a => a.Id).ToList();
        var contentIds = attempts.Select(a => a.ContentId).Distinct().ToList();

        var contents = await _db.ContentItems.AsNoTracking()
            .Where(c => contentIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Title })
            .ToListAsync(ct);
        var contentMap = contents.ToDictionary(c => c.Id, c => c.Title);

        var evaluations = await _db.Evaluations.AsNoTracking()
            .Where(e => attemptIds.Contains(e.AttemptId))
            .ToListAsync(ct);
        var evalByAttempt = evaluations
            .GroupBy(e => e.AttemptId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.GeneratedAt).First());

        var reviews = await _db.ReviewRequests.AsNoTracking()
            .Where(r => attemptIds.Contains(r.AttemptId))
            .ToListAsync(ct);
        var reviewByAttempt = reviews
            .GroupBy(r => r.AttemptId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CreatedAt).First());

        var items = attempts.Select(a =>
        {
            evalByAttempt.TryGetValue(a.Id, out var eval);
            reviewByAttempt.TryGetValue(a.Id, out var review);
            contentMap.TryGetValue(a.ContentId, out var title);
            return MapAttemptToListItem(a, eval, review, title ?? "Untitled paper", country);
        }).ToList();

        // Apply pass-only filter in-memory (country-aware Writing resolution
        // cannot be translated to SQL reliably).
        if (query.PassOnly)
        {
            items = items.Where(i => i.PassState == "pass").ToList();
        }

        var sparkline = await ComputeSparklineAsync(userId, query, ct);

        return new SubmissionListResponse(
            Items: items,
            NextCursor: nextCursor,
            Total: total,
            Facets: facets,
            Sparkline: sparkline);
    }

    // -----------------------------------------------------------------------
    // Detail (single call, replaces frontend N+1 composition)
    // -----------------------------------------------------------------------

    public async Task<SubmissionDetailResponse?> GetDetailAsync(
        string userId,
        string submissionId,
        CancellationToken ct)
    {
        var attempt = await _db.Attempts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Id == submissionId, ct);
        if (attempt is null) return null;

        var content = await _db.ContentItems.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == attempt.ContentId, ct);

        var evaluation = await _db.Evaluations.AsNoTracking()
            .Where(x => x.AttemptId == attempt.Id)
            .OrderByDescending(x => x.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        var reviewRequest = await _db.ReviewRequests.AsNoTracking()
            .Where(x => x.AttemptId == attempt.Id)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var goal = await _db.Goals.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
        var country = OetScoring.NormalizeWritingCountry(goal?.TargetCountry);

        var scaledScore = ProgressService.ParseScaledScore(evaluation?.ScoreRange);
        var pass = ResolvePassState(attempt.SubtestCode, scaledScore, country);

        var revisionLineage = await BuildRevisionLineageAsync(userId, attempt, ct);

        var strengths = ParseJsonArray(evaluation?.StrengthsJson);
        var issues = ParseJsonArray(evaluation?.IssuesJson);
        var criteria = ParseCriteria(evaluation?.CriterionScoresJson);

        var listItem = new SubmissionListItem(
            SubmissionId: attempt.Id,
            ContentId: attempt.ContentId,
            TaskName: content?.Title ?? "Untitled paper",
            Subtest: attempt.SubtestCode,
            Context: attempt.Context ?? "practice",
            AttemptDate: attempt.SubmittedAt ?? attempt.StartedAt,
            State: ToApiAttemptState(attempt.State),
            ReviewStatus: reviewRequest is null ? "not_requested" : ToReviewStatus(reviewRequest.State),
            EvaluationId: evaluation?.Id,
            ScaledScore: scaledScore,
            ScoreLabel: FormatScoreLabel(scaledScore),
            PassState: pass.State,
            PassLabel: pass.Label,
            RequiredScaled: pass.RequiredScaled,
            Grade: scaledScore is null ? null : OetScoring.OetGradeLetterFromScaled(scaledScore.Value),
            ComparisonGroupId: attempt.ComparisonGroupId,
            ParentAttemptId: attempt.ParentAttemptId,
            RevisionDepth: revisionLineage.Count - 1,
            CanRequestReview: CanRequestReview(attempt, reviewRequest),
            IsHidden: attempt.HiddenByUserAt.HasValue,
            Actions: BuildActions(attempt, reviewRequest));

        return new SubmissionDetailResponse(
            Submission: listItem,
            EvidenceSummary: new EvidenceSummary(
                Title: listItem.TaskName,
                ScoreLabel: listItem.ScoreLabel,
                StateLabel: TitleCase(listItem.State.Replace('_', ' ')),
                ReviewLabel: TitleCase(listItem.ReviewStatus.Replace('_', ' ')),
                NextActionLabel: listItem.CanRequestReview ? "Request review" : "Review current evidence"),
            Strengths: strengths,
            Issues: issues,
            Criteria: criteria,
            RevisionLineage: revisionLineage,
            ReviewLineage: reviewRequest is null ? null : BuildReviewLineage(reviewRequest));
    }

    // -----------------------------------------------------------------------
    // Compare (real diff, no hardcoded string)
    // -----------------------------------------------------------------------

    public async Task<SubmissionComparisonResponse> CompareAsync(
        string userId,
        string? leftId,
        string? rightId,
        CancellationToken ct)
    {
        var attempts = await _db.Attempts.AsNoTracking()
            .Where(x => x.UserId == userId && EvidenceStates.Contains(x.State))
            .OrderByDescending(x => x.SubmittedAt ?? x.StartedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);

        var left = leftId is null ? attempts.ElementAtOrDefault(0)
            : attempts.FirstOrDefault(a => a.Id == leftId);
        Attempt? right = null;
        if (rightId is not null)
        {
            right = attempts.FirstOrDefault(a => a.Id == rightId);
        }
        else if (left is not null)
        {
            right = PickComparisonPartner(attempts, left);
        }

        if (left is null || right is null)
        {
            return new SubmissionComparisonResponse(
                CanCompare: false,
                Reason: "need_two_attempts",
                ReasonLabel: "Need at least two related attempts to compare.",
                ComparisonGroupId: null,
                Left: null,
                Right: null,
                ScaledDelta: null,
                CriterionDeltas: Array.Empty<CriterionDelta>(),
                Summary: null);
        }

        if (left.SubtestCode != right.SubtestCode)
        {
            return new SubmissionComparisonResponse(
                CanCompare: false,
                Reason: "different_subtests",
                ReasonLabel: "Only attempts in the same sub-test can be compared.",
                ComparisonGroupId: null,
                Left: null,
                Right: null,
                ScaledDelta: null,
                CriterionDeltas: Array.Empty<CriterionDelta>(),
                Summary: null);
        }

        var leftEval = await _db.Evaluations.AsNoTracking()
            .Where(e => e.AttemptId == left.Id)
            .OrderByDescending(e => e.GeneratedAt)
            .FirstOrDefaultAsync(ct);
        var rightEval = await _db.Evaluations.AsNoTracking()
            .Where(e => e.AttemptId == right.Id)
            .OrderByDescending(e => e.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        var leftScaled = ProgressService.ParseScaledScore(leftEval?.ScoreRange);
        var rightScaled = ProgressService.ParseScaledScore(rightEval?.ScoreRange);

        var leftCriteria = ParseCriteria(leftEval?.CriterionScoresJson);
        var rightCriteria = ParseCriteria(rightEval?.CriterionScoresJson);

        var criterionDeltas = ComputeCriterionDeltas(leftCriteria, rightCriteria);

        int? scaledDelta = (leftScaled is null || rightScaled is null)
            ? null
            : rightScaled.Value - leftScaled.Value;

        var goal = await _db.Goals.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
        var country = OetScoring.NormalizeWritingCountry(goal?.TargetCountry);

        var summary = BuildComparisonSummary(left, right, leftScaled, rightScaled, criterionDeltas, country);

        return new SubmissionComparisonResponse(
            CanCompare: true,
            Reason: null,
            ReasonLabel: null,
            ComparisonGroupId: left.ComparisonGroupId ?? right.ComparisonGroupId,
            Left: BuildCompareSide(left, leftEval, leftScaled, country),
            Right: BuildCompareSide(right, rightEval, rightScaled, country),
            ScaledDelta: scaledDelta,
            CriterionDeltas: criterionDeltas,
            Summary: summary);
    }

    // -----------------------------------------------------------------------
    // Hide / unhide
    // -----------------------------------------------------------------------

    public async Task<bool> HideAsync(string userId, string submissionId, CancellationToken ct)
    {
        var attempt = await _db.Attempts.FirstOrDefaultAsync(x => x.UserId == userId && x.Id == submissionId, ct);
        if (attempt is null) return false;
        if (attempt.HiddenByUserAt is null)
        {
            attempt.HiddenByUserAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return true;
    }

    public async Task<bool> UnhideAsync(string userId, string submissionId, CancellationToken ct)
    {
        var attempt = await _db.Attempts.FirstOrDefaultAsync(x => x.UserId == userId && x.Id == submissionId, ct);
        if (attempt is null) return false;
        if (attempt.HiddenByUserAt is not null)
        {
            attempt.HiddenByUserAt = null;
            await _db.SaveChangesAsync(ct);
        }
        return true;
    }

    // -----------------------------------------------------------------------
    // CSV export (respects current filters)
    // -----------------------------------------------------------------------

    public async Task<byte[]> ExportCsvAsync(
        string userId,
        SubmissionListQuery query,
        CancellationToken ct)
    {
        var all = new List<SubmissionProjection>();
        string? cursor = null;
        var pagedQuery = query with { Limit = MaxPageSize };
        for (var safety = 0; safety < 200; safety++) // hard cap ~10k rows
        {
            var page = await ListAsync(userId, pagedQuery with { Cursor = cursor }, ct);
            foreach (var item in page.Items)
            {
                all.Add(new SubmissionProjection(
                    item.SubmissionId,
                    item.Subtest,
                    item.Context,
                    item.TaskName,
                    item.AttemptDate,
                    item.State,
                    item.ReviewStatus,
                    item.ScaledScore,
                    item.PassState,
                    item.Grade,
                    item.IsHidden));
            }
            if (page.NextCursor is null) break;
            cursor = page.NextCursor;
        }

        var sb = new StringBuilder();
        sb.AppendLine("submission_id,subtest,context,task,attempt_date,state,review_status,scaled_score,pass_state,grade,hidden");
        foreach (var r in all)
        {
            sb.Append(Csv(r.SubmissionId)).Append(',')
              .Append(Csv(r.Subtest)).Append(',')
              .Append(Csv(r.Context)).Append(',')
              .Append(Csv(r.Task)).Append(',')
              .Append(Csv(r.AttemptDate.ToString("O", CultureInfo.InvariantCulture))).Append(',')
              .Append(Csv(r.State)).Append(',')
              .Append(Csv(r.ReviewStatus)).Append(',')
              .Append(r.ScaledScore?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
              .Append(Csv(r.PassState)).Append(',')
              .Append(Csv(r.Grade ?? string.Empty)).Append(',')
              .Append(r.Hidden ? "true" : "false")
              .AppendLine();
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // -----------------------------------------------------------------------
    // Internals
    // -----------------------------------------------------------------------

    private static readonly AttemptState[] EvidenceStates =
    {
        AttemptState.Submitted,
        AttemptState.Evaluating,
        AttemptState.Completed,
        AttemptState.Failed,
    };

    private IQueryable<Attempt> BuildAttemptsQuery(string userId, SubmissionListQuery query)
    {
        var q = _db.Attempts.AsNoTracking()
            .Where(a => a.UserId == userId && EvidenceStates.Contains(a.State));

        if (!query.IncludeHidden)
        {
            q = q.Where(a => a.HiddenByUserAt == null);
        }

        if (!string.IsNullOrWhiteSpace(query.Subtest))
        {
            var subtest = query.Subtest.Trim().ToLowerInvariant();
            q = q.Where(a => a.SubtestCode.ToLower() == subtest);
        }
        if (!string.IsNullOrWhiteSpace(query.Context))
        {
            var context = query.Context.Trim().ToLowerInvariant();
            q = q.Where(a => a.Context.ToLower() == context);
        }
        if (query.From.HasValue)
        {
            q = q.Where(a => (a.SubmittedAt ?? a.StartedAt) >= query.From.Value);
        }
        if (query.To.HasValue)
        {
            q = q.Where(a => (a.SubmittedAt ?? a.StartedAt) <= query.To.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var needle = query.Q.Trim().ToLowerInvariant();
            // Task title lives on ContentItem — join via ContentId.
            var matchingIds = _db.ContentItems.AsNoTracking()
                .Where(c => c.Title.ToLower().Contains(needle))
                .Select(c => c.Id);
            q = q.Where(a => matchingIds.Contains(a.ContentId));
        }
        if (!string.IsNullOrWhiteSpace(query.ReviewStatus))
        {
            var rs = query.ReviewStatus.Trim().ToLowerInvariant();
            if (rs == "reviewed")
            {
                var reviewedIds = _db.ReviewRequests.AsNoTracking()
                    .Where(r => r.State == ReviewRequestState.Completed)
                    .Select(r => r.AttemptId);
                q = q.Where(a => reviewedIds.Contains(a.Id));
            }
            else if (rs == "pending")
            {
                var pendingIds = _db.ReviewRequests.AsNoTracking()
                    .Where(r => r.State == ReviewRequestState.Submitted
                              || r.State == ReviewRequestState.Queued
                              || r.State == ReviewRequestState.InReview
                              || r.State == ReviewRequestState.AwaitingPayment)
                    .Select(r => r.AttemptId);
                q = q.Where(a => pendingIds.Contains(a.Id));
            }
            else if (rs == "not_requested")
            {
                var reviewedAttempts = _db.ReviewRequests.AsNoTracking().Select(r => r.AttemptId);
                q = q.Where(a => !reviewedAttempts.Contains(a.Id));
            }
        }
        return q;
    }

    private static IQueryable<Attempt> ApplyAttemptOrdering(IQueryable<Attempt> q, string? sort)
    {
        return sort?.ToLowerInvariant() switch
        {
            "date-asc" => q.OrderBy(a => a.SubmittedAt ?? a.StartedAt).ThenBy(a => a.Id),
            _ => q.OrderByDescending(a => a.SubmittedAt ?? a.StartedAt).ThenByDescending(a => a.Id),
        };
    }

    private static IQueryable<Attempt> ApplyAttemptCursorFilter(
        IQueryable<Attempt> q,
        (DateTimeOffset SubmittedAt, string AttemptId) cf,
        string? sort)
    {
        return sort?.ToLowerInvariant() switch
        {
            "date-asc" => q.Where(a => (a.SubmittedAt ?? a.StartedAt) > cf.SubmittedAt
                                       || ((a.SubmittedAt ?? a.StartedAt) == cf.SubmittedAt && string.Compare(a.Id, cf.AttemptId, StringComparison.Ordinal) > 0)),
            _ => q.Where(a => (a.SubmittedAt ?? a.StartedAt) < cf.SubmittedAt
                               || ((a.SubmittedAt ?? a.StartedAt) == cf.SubmittedAt && string.Compare(a.Id, cf.AttemptId, StringComparison.Ordinal) < 0)),
        };
    }

    private static SubmissionListItem MapAttemptToListItem(
        Attempt a,
        Evaluation? eval,
        ReviewRequest? review,
        string title,
        string? country)
    {
        var scaled = ProgressService.ParseScaledScore(eval?.ScoreRange);
        var pass = ResolvePassState(a.SubtestCode, scaled, country);
        var canReview = a.State == AttemptState.Completed
                        && (a.SubtestCode == "writing" || a.SubtestCode == "speaking")
                        && review is null;
        return new SubmissionListItem(
            SubmissionId: a.Id,
            ContentId: a.ContentId,
            TaskName: title,
            Subtest: a.SubtestCode,
            Context: a.Context ?? "practice",
            AttemptDate: a.SubmittedAt ?? a.StartedAt,
            State: ToApiAttemptState(a.State),
            ReviewStatus: review is null ? "not_requested" : ToReviewStatus(review.State),
            EvaluationId: eval?.Id,
            ScaledScore: scaled,
            ScoreLabel: FormatScoreLabel(scaled),
            PassState: pass.State,
            PassLabel: pass.Label,
            RequiredScaled: pass.RequiredScaled,
            Grade: scaled is null ? null : OetScoring.OetGradeLetterFromScaled(scaled.Value),
            ComparisonGroupId: a.ComparisonGroupId,
            ParentAttemptId: a.ParentAttemptId,
            RevisionDepth: 0,
            CanRequestReview: canReview,
            IsHidden: a.HiddenByUserAt.HasValue,
            Actions: new SubmissionActions(
                ReopenFeedbackRoute: $"/submissions/{a.Id}",
                CompareRoute: $"/submissions/compare?leftId={a.Id}",
                RequestReviewRoute: canReview ? $"/submissions/{a.Id}?requestReview=1" : null));
    }

    private static string EncodeCursor(DateTimeOffset at, string id, string? sort)
    {
        var raw = $"{at.UtcDateTime:O}|{id}|{sort ?? "date-desc"}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    private static (DateTimeOffset, string)? DecodeCursor(string? cursor, string? sort)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|');
            if (parts.Length < 2) return null;
            if (!DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var ts))
                return null;
            return (ts, parts[1]);
        }
        catch
        {
            return null;
        }
    }

    private async Task<SubmissionFacets> ComputeFacetsAsync(
        string userId,
        SubmissionListQuery query,
        string? country,
        CancellationToken ct)
    {
        var baseQuery = _db.Attempts.AsNoTracking()
            .Where(a => a.UserId == userId && EvidenceStates.Contains(a.State));
        if (!query.IncludeHidden)
            baseQuery = baseQuery.Where(a => a.HiddenByUserAt == null);

        var subtestCounts = await baseQuery
            .GroupBy(x => x.SubtestCode)
            .Select(g => new { k = g.Key, n = g.Count() })
            .ToListAsync(ct);

        var contextCounts = await baseQuery
            .GroupBy(x => x.Context)
            .Select(g => new { k = g.Key, n = g.Count() })
            .ToListAsync(ct);

        // For review-status facets, fetch the attempt IDs + latest review state
        // per attempt with two small queries and aggregate in memory.
        var attemptIds = await baseQuery.Select(a => a.Id).ToListAsync(ct);
        var reviewsForAttempts = await _db.ReviewRequests.AsNoTracking()
            .Where(r => attemptIds.Contains(r.AttemptId))
            .Select(r => new { r.AttemptId, r.State, r.CreatedAt })
            .ToListAsync(ct);
        var latestByAttempt = reviewsForAttempts
            .GroupBy(r => r.AttemptId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt).First().State);

        var reviewDict = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var aid in attemptIds)
        {
            var key = latestByAttempt.TryGetValue(aid, out var state) ? ToReviewStatus(state) : "not_requested";
            reviewDict[key] = reviewDict.TryGetValue(key, out var n) ? n + 1 : 1;
        }

        return new SubmissionFacets(
            BySubtest: subtestCounts.ToDictionary(x => x.k, x => x.n),
            ByContext: contextCounts.ToDictionary(x => x.k, x => x.n),
            ByReviewStatus: reviewDict);
    }

    private async Task<Dictionary<string, List<SparklinePoint>>> ComputeSparklineAsync(
        string userId,
        SubmissionListQuery query,
        CancellationToken ct)
    {
        var attempts = _db.Attempts.AsNoTracking()
            .Where(a => a.UserId == userId && EvidenceStates.Contains(a.State));
        if (!query.IncludeHidden) attempts = attempts.Where(a => a.HiddenByUserAt == null);

        var attemptIds = await attempts
            .Select(a => new { a.Id, a.SubtestCode, a.SubmittedAt })
            .ToListAsync(ct);
        if (attemptIds.Count == 0) return new Dictionary<string, List<SparklinePoint>>(StringComparer.OrdinalIgnoreCase);

        var ids = attemptIds.Select(a => a.Id).ToList();
        var evals = await _db.Evaluations.AsNoTracking()
            .Where(e => ids.Contains(e.AttemptId) && e.ScoreRange != null)
            .Select(e => new { e.AttemptId, e.ScoreRange })
            .ToListAsync(ct);
        var evalByAttempt = evals.ToDictionary(e => e.AttemptId, e => e.ScoreRange);

        var result = new Dictionary<string, List<SparklinePoint>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in attemptIds.GroupBy(a => a.SubtestCode))
        {
            var points = group
                .OrderBy(a => a.SubmittedAt)
                .Select(a =>
                {
                    evalByAttempt.TryGetValue(a.Id, out var score);
                    return new SparklinePoint(
                        a.SubmittedAt ?? DateTimeOffset.MinValue,
                        ProgressService.ParseScaledScore(score));
                })
                .Where(p => p.Scaled.HasValue)
                .TakeLast(SparklineMaxPoints)
                .ToList();
            if (points.Count > 0) result[group.Key] = points;
        }
        return result;
    }

    private static SubmissionActions BuildActions(Attempt attempt, ReviewRequest? review)
    {
        var canReview = CanRequestReview(attempt, review);
        return new SubmissionActions(
            ReopenFeedbackRoute: $"/submissions/{attempt.Id}",
            CompareRoute: $"/submissions/compare?leftId={attempt.Id}",
            RequestReviewRoute: canReview ? $"/submissions/{attempt.Id}?requestReview=1" : null);
    }

    private static bool CanRequestReview(Attempt attempt, ReviewRequest? review)
    {
        return attempt.State == AttemptState.Completed
               && (attempt.SubtestCode == "writing" || attempt.SubtestCode == "speaking")
               && review is null;
    }

    private async Task<List<RevisionNode>> BuildRevisionLineageAsync(
        string userId,
        Attempt attempt,
        CancellationToken ct)
    {
        // Walk upward via ParentAttemptId until we hit a null, then include
        // all direct descendants of the root for this learner.
        var chain = new List<Attempt> { attempt };
        var seen = new HashSet<string> { attempt.Id };
        var cursor = attempt;
        while (!string.IsNullOrEmpty(cursor.ParentAttemptId))
        {
            var parent = await _db.Attempts.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId && x.Id == cursor.ParentAttemptId, ct);
            if (parent is null || !seen.Add(parent.Id)) break;
            chain.Insert(0, parent);
            cursor = parent;
        }

        // Fetch forward descendants whose ParentAttemptId matches something
        // in our chain. Flat traversal is sufficient given typical revision
        // depth <= 5.
        var rootId = chain[0].Id;
        var descendants = await _db.Attempts.AsNoTracking()
            .Where(x => x.UserId == userId && x.ParentAttemptId != null)
            .ToListAsync(ct);
        var descLookup = descendants.ToLookup(x => x.ParentAttemptId!);
        var frontier = new Queue<string>();
        frontier.Enqueue(rootId);
        while (frontier.Count > 0)
        {
            var pid = frontier.Dequeue();
            foreach (var child in descLookup[pid])
            {
                if (!seen.Add(child.Id)) continue;
                chain.Add(child);
                frontier.Enqueue(child.Id);
            }
        }

        chain = chain
            .OrderBy(x => x.SubmittedAt ?? x.StartedAt)
            .ThenBy(x => x.Id)
            .ToList();

        var chainEvals = await _db.Evaluations.AsNoTracking()
            .Where(e => chain.Select(a => a.Id).Contains(e.AttemptId))
            .ToListAsync(ct);

        var result = new List<RevisionNode>(chain.Count);
        for (var i = 0; i < chain.Count; i++)
        {
            var a = chain[i];
            var eval = chainEvals
                .Where(e => e.AttemptId == a.Id)
                .OrderByDescending(e => e.GeneratedAt)
                .FirstOrDefault();
            var scaled = ProgressService.ParseScaledScore(eval?.ScoreRange);
            result.Add(new RevisionNode(
                AttemptId: a.Id,
                Order: i,
                Label: i == 0 ? "Original" : $"Revision {i}",
                SubmittedAt: a.SubmittedAt ?? a.StartedAt,
                ScaledScore: scaled,
                IsCurrent: a.Id == attempt.Id));
        }
        return result;
    }

    private static ReviewLineage BuildReviewLineage(ReviewRequest review)
    {
        return new ReviewLineage(
            ReviewRequestId: review.Id,
            State: ToReviewStatus(review.State),
            StateLabel: TitleCase(ToReviewStatus(review.State).Replace('_', ' ')),
            TurnaroundOption: review.TurnaroundOption,
            CreditsCharged: (int)Math.Round(review.PriceSnapshot, MidpointRounding.AwayFromZero),
            RequestedAt: review.CreatedAt,
            CompletedAt: review.CompletedAt);
    }

    private static Attempt? PickComparisonPartner(List<Attempt> attempts, Attempt left)
    {
        return attempts
            .Where(c => c.Id != left.Id)
            .Where(c =>
                (!string.IsNullOrWhiteSpace(left.ComparisonGroupId) && string.Equals(c.ComparisonGroupId, left.ComparisonGroupId, StringComparison.Ordinal))
                || (c.SubtestCode == left.SubtestCode && c.ContentId == left.ContentId)
                || (!string.IsNullOrWhiteSpace(left.ParentAttemptId) && c.Id == left.ParentAttemptId)
                || (!string.IsNullOrWhiteSpace(c.ParentAttemptId) && c.ParentAttemptId == left.Id))
            .Where(c => c.SubtestCode == left.SubtestCode)
            .FirstOrDefault();
    }

    private static CompareSide BuildCompareSide(Attempt a, Evaluation? eval, int? scaled, string? country)
    {
        var pass = ResolvePassState(a.SubtestCode, scaled, country);
        return new CompareSide(
            AttemptId: a.Id,
            Subtest: a.SubtestCode,
            SubmittedAt: a.SubmittedAt ?? a.StartedAt,
            EvaluationId: eval?.Id,
            ScaledScore: scaled,
            ScoreLabel: FormatScoreLabel(scaled),
            PassState: pass.State,
            Grade: scaled is null ? null : OetScoring.OetGradeLetterFromScaled(scaled.Value));
    }

    private static List<CriterionDelta> ComputeCriterionDeltas(
        List<CriterionFeedback> leftCriteria,
        List<CriterionFeedback> rightCriteria)
    {
        var rightMap = rightCriteria.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var deltas = new List<CriterionDelta>();
        foreach (var l in leftCriteria)
        {
            if (!rightMap.TryGetValue(l.Name, out var r)) continue;
            var direction = r.Score > l.Score ? "up" : r.Score < l.Score ? "down" : "flat";
            deltas.Add(new CriterionDelta(
                Name: l.Name,
                LeftScore: l.Score,
                RightScore: r.Score,
                MaxScore: l.MaxScore,
                Direction: direction));
        }
        return deltas;
    }

    private static string? BuildComparisonSummary(
        Attempt left,
        Attempt right,
        int? leftScaled,
        int? rightScaled,
        List<CriterionDelta> deltas,
        string? country)
    {
        if (leftScaled is null || rightScaled is null)
        {
            return "One attempt is still pending evaluation — scores will appear here once grading is complete.";
        }
        var delta = rightScaled.Value - leftScaled.Value;
        var direction = delta > 0 ? "up" : delta < 0 ? "down" : "flat";
        var magnitude = Math.Abs(delta);
        var sb = new StringBuilder();
        sb.Append("Scaled score ")
          .Append(direction switch { "up" => "improved by", "down" => "dropped by", _ => "is unchanged" })
          .Append(direction == "flat" ? "" : $" {magnitude}")
          .Append(" between ")
          .Append(OetScoring.FormatScaledScore(leftScaled.Value))
          .Append(" and ")
          .Append(OetScoring.FormatScaledScore(rightScaled.Value))
          .Append('.');

        if (deltas.Count > 0)
        {
            var ups = deltas.Where(d => d.Direction == "up").ToList();
            var downs = deltas.Where(d => d.Direction == "down").ToList();
            if (ups.Count > 0)
                sb.Append(' ').Append("Improved: ").Append(string.Join(", ", ups.Select(d => d.Name))).Append('.');
            if (downs.Count > 0)
                sb.Append(' ').Append("Dropped: ").Append(string.Join(", ", downs.Select(d => d.Name))).Append('.');
        }

        // Pass/fail flip narration
        var leftPass = ResolvePassState(left.SubtestCode, leftScaled, country);
        var rightPass = ResolvePassState(right.SubtestCode, rightScaled, country);
        if (leftPass.State != rightPass.State)
        {
            sb.Append(' ').Append("Pass state changed from ").Append(leftPass.Label).Append(" to ").Append(rightPass.Label).Append('.');
        }

        return sb.ToString();
    }

    private static (string State, string Label, int? RequiredScaled) ResolvePassState(
        string? subtest,
        int? scaled,
        string? country)
    {
        if (scaled is null) return ("pending", "Pending", null);
        var s = scaled.Value;
        var code = (subtest ?? string.Empty).ToLowerInvariant();
        if (code == "writing")
        {
            var res = OetScoring.GradeWriting(s, country);
            if (res.Passed is null)
            {
                return (res.Reason ?? "country_required", res.Reason == "country_unsupported" ? "Unsupported country" : "Country required", null);
            }
            return (res.Passed.Value ? "pass" : "fail",
                res.Passed.Value ? $"Pass (Grade {res.RequiredGrade})" : $"Fail \u00b7 needs {res.RequiredScaled}",
                res.RequiredScaled);
        }
        if (code is "listening" or "reading" or "speaking")
        {
            var passed = s >= OetScoring.ScaledPassGradeB;
            return (passed ? "pass" : "fail",
                passed ? "Pass (Grade B)" : $"Fail \u00b7 needs {OetScoring.ScaledPassGradeB}",
                OetScoring.ScaledPassGradeB);
        }
        return ("pending", "Pending", null);
    }

    private static string FormatScoreLabel(int? scaled)
        => scaled.HasValue ? $"{scaled.Value} / {OetScoring.ScaledMax}" : "Pending";

    private static string ToApiAttemptState(AttemptState s) => s switch
    {
        AttemptState.Submitted => "submitted",
        AttemptState.Evaluating => "evaluating",
        AttemptState.Completed => "completed",
        AttemptState.Failed => "failed",
        _ => "unknown",
    };

    private static string ToReviewStatus(ReviewRequestState s) => s switch
    {
        ReviewRequestState.Completed => "reviewed",
        ReviewRequestState.Submitted or ReviewRequestState.Queued or ReviewRequestState.InReview or ReviewRequestState.AwaitingPayment => "pending",
        _ => "not_requested",
    };

    private static string TitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i][1..];
        }
        return string.Join(' ', parts);
    }

    private static List<string> ParseJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            var arr = JsonSerializer.Deserialize<List<string>>(json);
            return arr ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static List<CriterionFeedback> ParseCriteria(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<CriterionFeedback>();
        try
        {
            var parsed = JsonSerializer.Deserialize<List<CriterionFeedback>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return parsed ?? new List<CriterionFeedback>();
        }
        catch
        {
            return new List<CriterionFeedback>();
        }
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private sealed record SubmissionProjection(
        string SubmissionId,
        string Subtest,
        string Context,
        string Task,
        DateTimeOffset AttemptDate,
        string State,
        string ReviewStatus,
        int? ScaledScore,
        string PassState,
        string? Grade,
        bool Hidden);
}
