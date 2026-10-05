using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Assessment;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Services;

public partial class LearnerService
{

    public async Task<object> GetDashboardAsync(string userId, CancellationToken cancellationToken)
    {
        var profile = await EnsureLearnerProfileStateAsync(userId, cancellationToken);
        var freeze = await GetFreezeStatusForLoadedUserAsync(profile.User, cancellationToken);
        var readiness = await GetReadinessForLoadedProfileAsync(profile, cancellationToken);
        var dashboardPlan = await GetDashboardPlanAsync(profile, cancellationToken);
        var activePlan = dashboardPlan.Plan;
        var planItems = dashboardPlan.Items;
        var evidence = await GetDashboardEvidenceAsync(userId, cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var todaysTasks = planItems
            .Where(x => string.Equals(x.Section, "today", StringComparison.OrdinalIgnoreCase) || x.DueDate <= today)
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.DurationMinutes)
            .Take(5)
            .ToList();
        var dueItems = planItems.Where(x => x.DueDate <= today).ToList();
        var completedDueItems = dueItems.Count(x => x.Status == StudyPlanItemStatus.Completed);
        var completionRate = dueItems.Count > 0
            ? Math.Round(completedDueItems / (double)dueItems.Count, 2)
            : (double?)null;
        var nextPlanItem = planItems
            .Where(x => x.Status is not StudyPlanItemStatus.Completed and not StudyPlanItemStatus.Skipped)
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.DurationMinutes)
            .FirstOrDefault();
        var nextMockItem = planItems
            .Where(x => string.Equals(x.ItemType, "mock", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.DueDate)
            .FirstOrDefault();
        var primaryActions = new List<object>
        {
            new { id = "resume-study-plan", label = "Resume Study Plan", route = "/study-plan" }
        };
        if (nextPlanItem is not null)
        {
            primaryActions.Add(new { id = "start-next-task", label = "Start Next Task", route = StudyPlanRouteForItem(nextPlanItem) });
        }
        if (evidence.EvaluationId is not null)
        {
            primaryActions.Add(new { id = "view-latest-feedback", label = "View Latest Feedback", route = AttemptFeedbackRoute(evidence.SubtestCode!, evidence.EvaluationId) });
        }

        return new
        {
            cards = new
            {
                readiness,
                examDate = new { value = profile.Goal.TargetExamDate, route = "/goals" },
                todaysTasks = todaysTasks.Select(StudyPlanItemDto),
                latestEvaluatedSubmission = evidence.EvaluationId is null || evidence.AttemptId is null
                    ? null
                    : new { evaluationId = evidence.EvaluationId, attemptId = evidence.AttemptId, subtest = evidence.SubtestCode, scoreRange = GovernedScoreRange(evidence.SubtestCode, evidence.ScoreRange, evidence.ScaledScore, evidence.ScoreConversionTableVersionKey, evidence.ScoreConversionPassed), route = AttemptFeedbackRoute(evidence.SubtestCode!, evidence.EvaluationId) },
                weakCriteria = evidence.EvaluationId is null
                    ? new List<Dictionary<string, object?>>()
                    : JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evidence.CriterionScoresJson, []),
                momentum = new { streakDays = profile.User.CurrentStreak, completionRate, dueItems = dueItems.Count, completedDueItems },
                nextMockRecommendation = nextMockItem is null
                    ? null
                    : new { title = nextMockItem.Title, route = StudyPlanRouteForItem(nextMockItem), rationale = nextMockItem.Rationale },
                pendingExpertReviews = new { count = evidence.PendingReviews, route = "/reviews" }
            },
            engagement = new
            {
                currentStreak = profile.User.CurrentStreak,
                longestStreak = profile.User.LongestStreak,
                lastPracticeDate = profile.User.LastPracticeDate,
                totalPracticeMinutes = profile.User.TotalPracticeMinutes,
                totalPracticeSessions = profile.User.TotalPracticeSessions
            },
            freeze,
            primaryActions,
            partialData = evidence.EvaluationId is null,
            lastUpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<DashboardPlanState> GetDashboardPlanAsync(
        LearnerProfileState profile,
        CancellationToken cancellationToken)
    {
        IQueryable<StudyPlan> latestPlanQuery = IsSqliteProvider(db)
            ? db.StudyPlans.FromSqlInterpolated($@"
                SELECT *
                FROM ""StudyPlans""
                WHERE ""UserId"" = {profile.User.Id}
                ORDER BY ""GeneratedAt"" DESC
                LIMIT 1")
            : db.StudyPlans
                .Where(x => x.UserId == profile.User.Id)
                .OrderByDescending(x => x.GeneratedAt)
                .Take(1);

        var rows = await (
                from loadedPlan in latestPlanQuery.AsNoTracking()
                join item in db.StudyPlanItems.AsNoTracking()
                    on loadedPlan.Id equals item.StudyPlanId into items
                from item in items.DefaultIfEmpty()
                select new { Plan = loadedPlan, Item = item })
            .ToListAsync(cancellationToken);

        StudyPlan plan;
        List<StudyPlanItem> planItems;
        var changed = false;
        if (rows.Count == 0)
        {
            plan = CreateDefaultStudyPlan(profile.User.Id, profile.Goal, DateTimeOffset.UtcNow);
            planItems = CreateDefaultStudyPlanItems(plan.Id).ToList();
            db.StudyPlans.Add(plan);
            db.StudyPlanItems.AddRange(planItems);
            changed = true;
        }
        else
        {
            plan = rows[0].Plan;
            planItems = rows
                .Where(x => x.Item is not null)
                .Select(x => x.Item!)
                .ToList();
        }

        if (!string.Equals(profile.User.CurrentPlanId, plan.Id, StringComparison.Ordinal))
        {
            profile.User.CurrentPlanId = plan.Id;
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new DashboardPlanState(plan, planItems);
    }

    private async Task<DashboardEvidenceRow> GetDashboardEvidenceAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        IQueryable<Evaluation> latestEvaluationQuery = IsSqliteProvider(db)
            ? db.Evaluations.FromSqlInterpolated($@"
                SELECT evaluation.*
                FROM ""Evaluations"" AS evaluation
                INNER JOIN ""Attempts"" AS attempt ON attempt.""Id"" = evaluation.""AttemptId""
                WHERE attempt.""UserId"" = {userId}
                  AND evaluation.""SubtestCode"" <> 'writing'
                ORDER BY evaluation.""GeneratedAt"" DESC
                LIMIT 1")
            : db.Evaluations
                .Where(evaluation => db.Attempts.Any(attempt =>
                    attempt.Id == evaluation.AttemptId
                    && attempt.UserId == userId
                    && evaluation.SubtestCode != "writing"))
                .OrderByDescending(evaluation => evaluation.GeneratedAt)
                .Take(1);

        return await (
                from owner in db.Users.AsNoTracking().Where(x => x.Id == userId)
                from evaluation in latestEvaluationQuery.AsNoTracking().DefaultIfEmpty()
                select new DashboardEvidenceRow(
                    db.ReviewRequests.Count(review =>
                        (review.State == ReviewRequestState.Submitted
                         || review.State == ReviewRequestState.Queued
                         || review.State == ReviewRequestState.InReview)
                        && db.Attempts.Any(attempt =>
                            attempt.Id == review.AttemptId && attempt.UserId == owner.Id)),
                    evaluation == null ? null : evaluation.Id,
                    evaluation == null ? null : evaluation.AttemptId,
                    evaluation == null ? null : evaluation.SubtestCode,
                    evaluation == null ? null : evaluation.ScoreRange,
                    evaluation == null ? null : evaluation.ScaledScore,
                    evaluation == null ? null : evaluation.CriterionScoresJson,
                    evaluation == null ? null : evaluation.ScoreConversionTableVersionKey,
                    evaluation == null ? null : evaluation.ScoreConversionPassed))
            .SingleAsync(cancellationToken);
    }

    public async Task<object> GetReadinessAsync(string userId, CancellationToken cancellationToken)
    {
        var profile = await EnsureLearnerProfileStateAsync(userId, cancellationToken);
        return await GetReadinessForLoadedProfileAsync(profile, cancellationToken);
    }

    private async Task<object> GetReadinessForLoadedProfileAsync(
        LearnerProfileState profile,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetLatestReadinessSnapshotAsync(profile, cancellationToken);
        var payload = JsonSupport.Deserialize<Dictionary<string, object?>>(snapshot.PayloadJson, new Dictionary<string, object?>());
        payload["snapshotId"] = snapshot.Id;
        payload["computedAt"] = snapshot.ComputedAt;
        payload["snapshotVersion"] = snapshot.Version;
        // No server-side "readiness_viewed" write on this read path. RecordEventAsync only
        // adds the row to the change tracker (a GET almost never saves, so it was normally
        // discarded), the client already tracks the view (lib/hooks/use-dashboard-home.ts),
        // and a pending tracked change stops EffectiveEntitlementResolver from memoizing
        // inside the request.
        return payload;
    }

    public async Task<object> GetProgressAsync(string userId, CancellationToken cancellationToken)
    {
        var totals = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new ProgressTotalsRow(
                user.AccountStatus,
                db.Attempts.Count(attempt =>
                    attempt.UserId == user.Id && attempt.State == AttemptState.Completed),
                db.Evaluations.Count(evaluation =>
                    evaluation.State == AsyncState.Completed
                    && db.Attempts.Any(attempt =>
                        attempt.Id == evaluation.AttemptId
                        && attempt.UserId == user.Id
                        && attempt.State == AttemptState.Completed))))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw ApiException.Forbidden("learner_profile_not_found", "Learner profile not found.");

        if (!string.Equals(totals.AccountStatus, "active", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Forbidden("account_suspended", "This learner account is not available.");
        }

        var recentEvaluations = await db.Evaluations
            .AsNoTracking()
            .Where(evaluation =>
                evaluation.State == AsyncState.Completed
                && db.Attempts.Any(attempt =>
                    attempt.Id == evaluation.AttemptId
                    && attempt.UserId == userId
                    && attempt.State == AttemptState.Completed))
            .OrderByDescending(evaluation => evaluation.GeneratedAt)
            .ThenByDescending(evaluation => evaluation.Id)
            .Take(ProgressHistoryLimit)
            .Select(evaluation => new ProgressEvaluationRow(
                evaluation.SubtestCode,
                evaluation.ScoreRange,
                evaluation.ScaledScore,
                evaluation.CriterionScoresJson,
                evaluation.GeneratedAt,
                evaluation.ScoreConversionTableVersionKey,
                evaluation.ScoreConversionPassed))
            .ToListAsync(cancellationToken);

        var evaluations = recentEvaluations
            .OrderBy(evaluation => evaluation.GeneratedAt)
            .ToList();
        var parsedEvaluations = evaluations
            .Select(evaluation => new
            {
                Evaluation = evaluation,
                Criteria = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.CriterionScoresJson, [])
            })
            .ToList();
        var criterionTrend = parsedEvaluations
            .SelectMany(parsed => parsed.Criteria.Select(criterion =>
            {
                var criterionCode = criterion.GetValueOrDefault("criterionCode")?.ToString();
                return new
                {
                    criterionCode,
                    criterionLabel = CriterionLabelFromCode(criterionCode),
                    score = IsGovernedScoreAvailable(parsed.Evaluation.SubtestCode, parsed.Evaluation.ScoreRange, parsed.Evaluation.ScaledScore, parsed.Evaluation.ScoreConversionTableVersionKey, parsed.Evaluation.ScoreConversionPassed)
                        ? ParseCriterionScore(criterion.GetValueOrDefault("scoreRange")?.ToString())
                        : (int?)null,
                    generatedAt = parsed.Evaluation.GeneratedAt,
                    subtest = parsed.Evaluation.SubtestCode
                };
            }))
            .ToList();

        var reviewQuery = db.ReviewRequests
            .AsNoTracking()
            .Where(review => db.Attempts.Any(attempt =>
                attempt.Id == review.AttemptId
                && attempt.UserId == userId
                && attempt.State == AttemptState.Completed));
        var reviewUsage = await reviewQuery
            .GroupBy(_ => 1)
            .Select(group => new ProgressReviewAggregateRow(
                group.Count(),
                group.Count(review => review.CompletedAt.HasValue),
                group.Count(review => review.PaymentSource.ToLower() == "credits")))
            .SingleOrDefaultAsync(cancellationToken)
            ?? new ProgressReviewAggregateRow(0, 0, 0);
        var completedReviewTurnarounds = await reviewQuery
            .Where(review => review.CompletedAt.HasValue)
            .Select(review => new ProgressReviewTurnaroundRow(
                review.CreatedAt,
                review.CompletedAt!.Value))
            .ToListAsync(cancellationToken);
        var averageTurnaroundHours = completedReviewTurnarounds.Count == 0
            ? (double?)null
            : Math.Round(completedReviewTurnarounds.Average(x => (x.CompletedAt - x.CreatedAt).TotalHours), 1);

        // The completion and volume charts show this learner's real activity.
        // ponytail: loads the learner's attempt timestamps and buckets them in
        // memory, which keeps the query provider-neutral; window it in SQL if a
        // learner ever has thousands of attempts.
        var activity = await db.Attempts
            .AsNoTracking()
            .Where(attempt => attempt.UserId == userId)
            .Select(attempt => new { attempt.State, attempt.SubmittedAt, attempt.CompletedAt })
            .ToListAsync(cancellationToken);
        var (completionSeries, volumeSeries) = BuildProgressActivitySeries(
            DateTimeOffset.UtcNow,
            activity.Select(row => row.State == AttemptState.Completed ? row.CompletedAt : null),
            activity.Select(row => row.SubmittedAt));

        return new
        {
            trend = evaluations.Select((x, index) => new { week = $"Week {index + 1}", subtest = x.SubtestCode, scoreRange = GovernedScoreRange(x.SubtestCode, x.ScoreRange, x.ScaledScore, x.ScoreConversionTableVersionKey, x.ScoreConversionPassed), generatedAt = x.GeneratedAt }),
            subtestTrend = evaluations.Select((x, index) => new { week = $"Week {index + 1}", subtest = x.SubtestCode, scoreRange = GovernedScoreRange(x.SubtestCode, x.ScoreRange, x.ScaledScore, x.ScoreConversionTableVersionKey, x.ScoreConversionPassed), generatedAt = x.GeneratedAt }),
            criterionTrend,
            completion = completionSeries.Select(point => new { day = point.Day, completed = point.Completed }),
            submissionVolume = volumeSeries.Select(point => new { week = point.Week, submissions = point.Submissions }),
            reviewUsage = new
            {
                totalRequests = reviewUsage.TotalRequests,
                completedRequests = reviewUsage.CompletedRequests,
                averageTurnaroundHours,
                creditsConsumed = reviewUsage.CreditsConsumed
            },
            totals = new { completedAttempts = totals.CompletedAttempts, completedEvaluations = totals.CompletedEvaluations },
            freshness = new
            {
                generatedAt = DateTimeOffset.UtcNow,
                usesFallbackSeries = evaluations.Count == 0
            }
        };
    }

    /// <summary>
    /// Completed attempts per UTC day over the last 7 days, and submitted attempts per
    /// week (weeks start on Monday) over the last 5 weeks, oldest first and ending with
    /// the current day and week.
    /// </summary>
    internal static (IReadOnlyList<(string Day, int Completed)> Completion, IReadOnlyList<(string Week, int Submissions)> SubmissionVolume)
        BuildProgressActivitySeries(DateTimeOffset now, IEnumerable<DateTimeOffset?> completedAt, IEnumerable<DateTimeOffset?> submittedAt)
    {
        var today = now.UtcDateTime.Date;
        var completedDays = completedAt.Where(at => at.HasValue).Select(at => at!.Value.UtcDateTime.Date).ToList();
        var completion = Enumerable.Range(0, 7)
            .Select(offset => today.AddDays(offset - 6))
            .Select(day => (day.ToString("ddd", CultureInfo.InvariantCulture), completedDays.Count(completedDay => completedDay == day)))
            .ToList();

        var thisWeek = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var submittedDays = submittedAt.Where(at => at.HasValue).Select(at => at!.Value.UtcDateTime.Date).ToList();
        var submissionVolume = Enumerable.Range(0, 5)
            .Select(offset => thisWeek.AddDays(7 * (offset - 4)))
            .Select(weekStart => (
                weekStart.ToString("d MMM", CultureInfo.InvariantCulture),
                submittedDays.Count(submittedDay => submittedDay >= weekStart && submittedDay < weekStart.AddDays(7))))
            .ToList();

        return (completion, submissionVolume);
    }

    public Task<object> GetSubmissionsAsync(string userId, CancellationToken cancellationToken)
        => GetSubmissionsAsync(userId, cursor: null, limit: null, subtest: null, cancellationToken);

    public async Task<object> GetSubmissionsAsync(string userId, string? cursor, int? limit, string? subtest, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var pageSize = CursorPagination.NormalizeLimit(limit);
        // Applied before the cursor/limit page is taken, so "subtest=writing,
        // limit=100" returns the 100 most recent WRITING attempts rather than
        // the 100 most recent attempts of any subtest filtered down afterwards.
        var subtestFilter = string.IsNullOrWhiteSpace(subtest) ? null : subtest.Trim().ToLowerInvariant();
        if (IsSqliteProvider(db))
        {
            return await BuildSubmissionsResponseAsync(
                await GetSubmissionsSqlitePageAsync(userId, cursor, pageSize, subtestFilter, cancellationToken),
                pageSize,
                cancellationToken);
        }

        // A Speaking card that belongs to a session or an exam is listed, with its result, under
        // "Attempt activity" (GET /v1/me/attempts). It never gets an Evaluation row, so here it showed
        // "Pending" forever. Left out in the page query (not after it) so cursor and limit stay exact.
        // A legacy recorder submission stays: it has no session, or a bridge session (see
        // SpeakingEvaluationPipeline) next to the Evaluation that carries its score.
        var query = db.Attempts
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Where(x => !db.SpeakingSessions.Any(s => s.UserId == userId && s.AttemptId == x.Id)
                || db.Evaluations.Any(e => e.AttemptId == x.Id));

        if (subtestFilter is not null)
        {
            query = query.Where(x => x.SubtestCode.ToLower() == subtestFilter);
        }

        if (CursorPagination.TryDecode(cursor, out var decoded))
        {
            query = query.Where(x =>
                (x.SubmittedAt ?? x.StartedAt) < decoded.Timestamp
                || ((x.SubmittedAt ?? x.StartedAt) == decoded.Timestamp && x.Id.CompareTo(decoded.Id) < 0));
        }

        var page = await query
            .OrderByDescending(x => x.SubmittedAt ?? x.StartedAt)
            .ThenByDescending(x => x.Id)
            .Take(pageSize + 1)
            .Select(x => new AttemptSubmissionRow(
                x.Id,
                x.ContentId,
                x.SubtestCode,
                x.State,
                x.StartedAt,
                x.SubmittedAt,
                x.AnalysisJson,
                x.ComparisonGroupId))
            .ToListAsync(cancellationToken);

        return await BuildSubmissionsResponseAsync(page, pageSize, cancellationToken);
    }

    private async Task<object> BuildSubmissionsResponseAsync(
        List<AttemptSubmissionRow> page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var hasMore = page.Count > pageSize;
        var pageAttempts = hasMore ? page.Take(pageSize).ToList() : page;
        var attemptIds = pageAttempts.Select(x => x.Id).ToArray();
        var contentIds = pageAttempts.Select(x => x.ContentId).Distinct().ToArray();

        var contentsById = await db.ContentItems
            .AsNoTracking()
            .Where(x => contentIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var evaluationsByAttemptId = (await db.Evaluations
                .AsNoTracking()
                .Where(x => attemptIds.Contains(x.AttemptId))
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.AttemptId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(evaluation => evaluation.GeneratedAt).First());

        var reviewsByAttemptId = (await db.ReviewRequests
                .AsNoTracking()
                .Where(x => attemptIds.Contains(x.AttemptId))
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.AttemptId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(review => review.CreatedAt).First());

        var completedReviewIds = reviewsByAttemptId.Values
            .Where(x => x.State == ReviewRequestState.Completed)
            .Select(x => x.Id)
            .ToArray();
        var voiceNoteCountsByReviewId = completedReviewIds.Length == 0
            ? new Dictionary<string, int>()
            : await db.ReviewVoiceNotes
                .AsNoTracking()
                .Where(note => completedReviewIds.Contains(note.ReviewRequestId) && note.Status == "ready")
                .GroupBy(note => note.ReviewRequestId)
                .Select(group => new { ReviewRequestId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(x => x.ReviewRequestId, x => x.Count, cancellationToken);

        var items = new List<object>();

        foreach (var attempt in pageAttempts)
        {
            // Content may be missing if it was force-deleted after the attempt was
            // recorded (orphaned attempt). Render a fallback row so the learner's
            // evidence stays visible and the whole list does not 500. Rendering
            // (rather than skipping) keeps items.Count == pageAttempts.Count, so the
            // cursor/hasMore pagination math below stays exact.
            if (!contentsById.TryGetValue(attempt.ContentId, out var content))
            {
                logger?.LogWarning(
                    "Submissions: orphaned attempt {AttemptId} references missing content {ContentId}; rendering fallback row.",
                    attempt.Id,
                    attempt.ContentId);
            }

            evaluationsByAttemptId.TryGetValue(attempt.Id, out var eval);
            reviewsByAttemptId.TryGetValue(attempt.Id, out var review);
            var voiceNoteCount = review is not null
                                 && voiceNoteCountsByReviewId.TryGetValue(review.Id, out var count)
                ? count
                : 0;
            var canRequestReview = attempt.State == AttemptState.Completed && attempt.SubtestCode is "writing" or "speaking";
            var metadata = ReadWritingSubmissionMetadata(attempt.SubtestCode, attempt.AnalysisJson);
            items.Add(new
            {
                submissionId = attempt.Id,
                reviewRequestId = review?.Id,
                contentId = content?.Id ?? attempt.ContentId,
                taskName = content?.Title ?? "Removed practice item",
                subtest = content?.SubtestCode ?? attempt.SubtestCode,
                attemptDate = attempt.SubmittedAt ?? attempt.StartedAt,
                scoreEstimate = GovernedScoreRange(
                    eval?.SubtestCode ?? attempt.SubtestCode,
                    eval?.ScoreRange,
                    eval?.ScaledScore,
                    eval?.ScoreConversionTableVersionKey,
                    eval?.ScoreConversionPassed),
                reviewStatus = review is null ? "not_requested" : ToReviewRequestState(review.State),
                evaluationId = eval?.Id,
                state = ToApiState(attempt.State),
                submissionMode = metadata.ExamMode,
                assessorType = metadata.AssessorType,
                voiceNoteCount,
                comparisonGroupId = attempt.ComparisonGroupId,
                canRequestReview,
                actions = new
                {
                    reopenFeedbackRoute = $"/submissions/{attempt.Id}",
                    compareRoute = $"/submissions/compare?leftId={attempt.Id}",
                    requestReviewRoute = canRequestReview ? $"/submissions/{attempt.Id}?requestReview=1" : null
                }
            });
        }

        string? nextCursor = null;
        if (hasMore)
        {
            var last = pageAttempts[^1];
            var ts = last.SubmittedAt ?? last.StartedAt;
            nextCursor = CursorPagination.Encode(ts, last.Id);
        }

        return new { items, nextCursor };
    }

    private sealed record AttemptSubmissionRow(
        string Id,
        string ContentId,
        string SubtestCode,
        AttemptState State,
        DateTimeOffset StartedAt,
        DateTimeOffset? SubmittedAt,
        string AnalysisJson,
        string? ComparisonGroupId);

    private async Task<List<AttemptSubmissionRow>> GetSubmissionsSqlitePageAsync(
        string userId,
        string? cursor,
        int pageSize,
        string? subtestFilter,
        CancellationToken cancellationToken)
    {
        // Raw SQL (see cursor-ticks note below) can't reuse the LINQ .Where()
        // the non-SQLite path uses, so the subtest filter is applied here as an
        // extra AND clause — still ahead of the LIMIT, same as the other path.
        // subtestFilter is bound as a real parameter (not string-concatenated),
        // so a null value simply makes the "IS NULL" arm true rather than
        // opening a SQL-injection hole.
        IQueryable<Attempt> query;
        if (CursorPagination.TryDecode(cursor, out var decoded))
        {
            // SQLite persists DateTimeOffset as UTC ticks (INTEGER) via
            // SqliteUtcTicksConverter. Raw SQL bypasses that converter, so the
            // cursor timestamp must be bound as ticks too — otherwise it binds as
            // TEXT and, under SQLite type affinity, every INTEGER column value
            // sorts below any TEXT parameter, making the predicate always true and
            // returning the first page forever.
            var cursorTicks = decoded.Timestamp.UtcTicks;
            query = db.Attempts.FromSqlInterpolated($@"
                SELECT *
                FROM ""Attempts""
                WHERE ""UserId"" = {userId}
                  AND (
                    COALESCE(""SubmittedAt"", ""StartedAt"") < {cursorTicks}
                    OR (COALESCE(""SubmittedAt"", ""StartedAt"") = {cursorTicks} AND ""Id"" < {decoded.Id})
                  )
                  AND ({subtestFilter} IS NULL OR LOWER(""SubtestCode"") = {subtestFilter})
                  AND (NOT EXISTS (SELECT 1 FROM ""SpeakingSessions"" AS ""ss"" WHERE ""ss"".""UserId"" = {userId} AND ""ss"".""AttemptId"" = ""Attempts"".""Id"")
                    OR EXISTS (SELECT 1 FROM ""Evaluations"" AS ""ev"" WHERE ""ev"".""AttemptId"" = ""Attempts"".""Id""))
                ORDER BY COALESCE(""SubmittedAt"", ""StartedAt"") DESC, ""Id"" DESC
                LIMIT {pageSize + 1}");
        }
        else
        {
            query = db.Attempts.FromSqlInterpolated($@"
                SELECT *
                FROM ""Attempts""
                WHERE ""UserId"" = {userId}
                  AND ({subtestFilter} IS NULL OR LOWER(""SubtestCode"") = {subtestFilter})
                  AND (NOT EXISTS (SELECT 1 FROM ""SpeakingSessions"" AS ""ss"" WHERE ""ss"".""UserId"" = {userId} AND ""ss"".""AttemptId"" = ""Attempts"".""Id"")
                    OR EXISTS (SELECT 1 FROM ""Evaluations"" AS ""ev"" WHERE ""ev"".""AttemptId"" = ""Attempts"".""Id""))
                ORDER BY COALESCE(""SubmittedAt"", ""StartedAt"") DESC, ""Id"" DESC
                LIMIT {pageSize + 1}");
        }

        return await query
            .AsNoTracking()
            .Select(x => new AttemptSubmissionRow(
                x.Id,
                x.ContentId,
                x.SubtestCode,
                x.State,
                x.StartedAt,
                x.SubmittedAt,
                x.AnalysisJson,
                x.ComparisonGroupId))
            .ToListAsync(cancellationToken);
    }

    private static bool IsSqliteProvider(LearnerDbContext context)
        => context.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private static (string ExamMode, string AssessorType) ReadWritingSubmissionMetadata(Attempt attempt)
        => ReadWritingSubmissionMetadata(attempt.SubtestCode, attempt.AnalysisJson);

    private static (string ExamMode, string AssessorType) ReadWritingSubmissionMetadata(string subtestCode, string? analysisJson)
    {
        if (!string.Equals(subtestCode, "writing", StringComparison.OrdinalIgnoreCase))
        {
            return ("computer", "ai");
        }

        try
        {
            using var doc = JsonDocument.Parse(analysisJson ?? "{}");
            if (doc.RootElement.TryGetProperty("writingSubmission", out var submission)
                && submission.ValueKind == JsonValueKind.Object)
            {
                var examMode = submission.TryGetProperty("examMode", out var examModeNode) && examModeNode.ValueKind == JsonValueKind.String
                    ? examModeNode.GetString() ?? "computer"
                    : "computer";
                var assessorType = submission.TryGetProperty("assessorType", out var assessorNode) && assessorNode.ValueKind == JsonValueKind.String
                    ? assessorNode.GetString() ?? "ai"
                    : "ai";
                return (examMode, assessorType);
            }
        }
        catch (JsonException)
        {
            return ("computer", "ai");
        }

        return ("computer", "ai");
    }

    public async Task<object> CompareSubmissionsAsync(string userId, string? leftId, string? rightId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var left = leftId is not null
            ? await GetComparisonAttemptOwnedByUserAsync(userId, leftId, cancellationToken)
            : await db.Attempts
                .AsNoTracking()
                .Where(attempt => attempt.UserId == userId)
                .OrderByDescending(attempt => attempt.SubmittedAt)
                .Select(attempt => new ComparisonAttemptRow(
                    attempt.Id,
                    attempt.ContentId,
                    attempt.SubtestCode,
                    attempt.ComparisonGroupId,
                    attempt.ParentAttemptId))
                .FirstOrDefaultAsync(cancellationToken);
        ComparisonAttemptRow? right = null;
        if (rightId is not null)
        {
            right = await GetComparisonAttemptOwnedByUserAsync(userId, rightId, cancellationToken);
        }
        else if (left is not null)
        {
            var comparisonGroupId = string.IsNullOrWhiteSpace(left.ComparisonGroupId)
                ? null
                : left.ComparisonGroupId;
            var parentAttemptId = string.IsNullOrWhiteSpace(left.ParentAttemptId)
                ? null
                : left.ParentAttemptId;
            right = await db.Attempts
                .AsNoTracking()
                .Where(candidate => candidate.UserId == userId && candidate.Id != left.Id)
                .Where(candidate =>
                    (comparisonGroupId != null && candidate.ComparisonGroupId == comparisonGroupId)
                    || (candidate.SubtestCode == left.SubtestCode && candidate.ContentId == left.ContentId)
                    || (parentAttemptId != null && candidate.Id == parentAttemptId)
                    || (candidate.ParentAttemptId != null && candidate.ParentAttemptId == left.Id))
                .OrderByDescending(candidate => candidate.SubmittedAt ?? candidate.StartedAt)
                .Select(candidate => new ComparisonAttemptRow(
                    candidate.Id,
                    candidate.ContentId,
                    candidate.SubtestCode,
                    candidate.ComparisonGroupId,
                    candidate.ParentAttemptId))
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (left is null || right is null)
        {
            return new { canCompare = false, reason = "Need at least two submissions to compare." };
        }

        var comparisonAttemptIds = new[] { left.Id, right.Id };
        var evaluations = await db.Evaluations
            .AsNoTracking()
            .Where(evaluation => comparisonAttemptIds.Contains(evaluation.AttemptId))
            .Select(evaluation => new ComparisonEvaluationRow(
                evaluation.Id,
                evaluation.AttemptId,
                evaluation.SubtestCode,
                evaluation.ScoreRange,
                evaluation.ScaledScore,
                evaluation.ScoreConversionTableVersionKey,
                evaluation.ScoreConversionPassed))
            .ToListAsync(cancellationToken);
        var leftEval = evaluations.FirstOrDefault(evaluation => evaluation.AttemptId == left.Id);
        var rightEval = evaluations.FirstOrDefault(evaluation => evaluation.AttemptId == right.Id);

        return new
        {
            canCompare = true,
            left = new { attemptId = left.Id, evaluationId = leftEval?.Id, scoreRange = GovernedScoreRange(left.SubtestCode, leftEval?.ScoreRange, leftEval?.ScaledScore, leftEval?.ScoreConversionTableVersionKey, leftEval?.ScoreConversionPassed), subtest = left.SubtestCode },
            right = new { attemptId = right.Id, evaluationId = rightEval?.Id, scoreRange = GovernedScoreRange(right.SubtestCode, rightEval?.ScoreRange, rightEval?.ScaledScore, rightEval?.ScoreConversionTableVersionKey, rightEval?.ScoreConversionPassed), subtest = right.SubtestCode },
            summary = "The more recent submission shows stronger structure and slightly improved score confidence.",
            comparisonGroupId = left.ComparisonGroupId ?? right.ComparisonGroupId
        };
    }

    private async Task<ComparisonAttemptRow> GetComparisonAttemptOwnedByUserAsync(
        string userId,
        string attemptId,
        CancellationToken cancellationToken)
        => await db.Attempts
               .AsNoTracking()
               .Where(attempt => attempt.Id == attemptId && attempt.UserId == userId)
               .Select(attempt => new ComparisonAttemptRow(
                   attempt.Id,
                   attempt.ContentId,
                   attempt.SubtestCode,
                   attempt.ComparisonGroupId,
                   attempt.ParentAttemptId))
               .FirstOrDefaultAsync(cancellationToken)
           ?? throw ApiException.NotFound("attempt_not_found", "Attempt not found.");
}
