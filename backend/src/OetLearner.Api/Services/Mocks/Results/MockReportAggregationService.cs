using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Mocks.Results;

public interface IMockSectionResultAdapter
{
    bool Supports(string subtestCode);
    Task<MockSectionResolvedResult> ResolveAsync(MockSectionResultContext context, CancellationToken ct);
}

public interface IMockReportAggregationService
{
    Task GenerateAsync(BackgroundJobItem job, CancellationToken ct);
}

public sealed record MockSectionResultContext(
    LearnerDbContext Db,
    MockAttempt MockAttempt,
    MockSectionAttempt SectionAttempt,
    MockBundleSection BundleSection);

public sealed record MockSectionResolvedResult(
    string ResultStatus,
    int? RawScore,
    int? RawScoreMax,
    int? ScaledScore,
    string? Grade,
    string EvidenceSource,
    string? ReviewRequestId = null,
    string? ReviewState = null);

public sealed class ReadingMockSectionResultAdapter : IMockSectionResultAdapter
{
    public bool Supports(string subtestCode) => string.Equals(subtestCode, "reading", StringComparison.OrdinalIgnoreCase);

    public async Task<MockSectionResolvedResult> ResolveAsync(MockSectionResultContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(context.SectionAttempt.ContentAttemptId))
        {
            return LegacyMockSectionResultAdapter.ResolveLegacy(context.SectionAttempt, "missing_authoritative_attempt");
        }

        var attempt = await context.Db.ReadingAttempts.AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == context.SectionAttempt.ContentAttemptId &&
                x.UserId == context.MockAttempt.UserId &&
                x.PaperId == context.BundleSection.ContentPaperId,
                ct);

        if (attempt is null)
        {
            return LegacyMockSectionResultAdapter.ResolveLegacy(context.SectionAttempt, "authoritative_attempt_not_found");
        }

        var scaled = attempt.ScaledScore ?? (attempt.RawScore.HasValue ? OetScoring.OetRawToScaled(attempt.RawScore.Value) : null);
        return new MockSectionResolvedResult(
            attempt.SubmittedAt is null ? "in_progress" : scaled.HasValue ? "completed" : "pending_score",
            attempt.RawScore,
            attempt.MaxRawScore > 0 ? attempt.MaxRawScore : 42,
            scaled,
            scaled.HasValue ? OetScoring.OetGradeLetterFromScaled(scaled.Value) : null,
            "reading_attempt");
    }
}

public sealed class ListeningMockSectionResultAdapter : IMockSectionResultAdapter
{
    public bool Supports(string subtestCode) => string.Equals(subtestCode, "listening", StringComparison.OrdinalIgnoreCase);

    public async Task<MockSectionResolvedResult> ResolveAsync(MockSectionResultContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(context.SectionAttempt.ContentAttemptId))
        {
            return LegacyMockSectionResultAdapter.ResolveLegacy(context.SectionAttempt, "missing_authoritative_attempt");
        }

        var attempt = await context.Db.ListeningAttempts.AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == context.SectionAttempt.ContentAttemptId &&
                x.UserId == context.MockAttempt.UserId &&
                x.PaperId == context.BundleSection.ContentPaperId,
                ct);

        if (attempt is null)
        {
            return LegacyMockSectionResultAdapter.ResolveLegacy(context.SectionAttempt, "authoritative_attempt_not_found");
        }

        var scaled = attempt.ScaledScore ?? (attempt.RawScore.HasValue ? OetScoring.OetRawToScaled(attempt.RawScore.Value) : null);
        return new MockSectionResolvedResult(
            attempt.SubmittedAt is null ? "in_progress" : scaled.HasValue ? "completed" : "pending_score",
            attempt.RawScore,
            attempt.MaxRawScore > 0 ? attempt.MaxRawScore : 42,
            scaled,
            scaled.HasValue ? OetScoring.OetGradeLetterFromScaled(scaled.Value) : null,
            "listening_attempt");
    }
}

public sealed class LegacyMockSectionResultAdapter : IMockSectionResultAdapter
{
    public bool Supports(string subtestCode) => true;

    public Task<MockSectionResolvedResult> ResolveAsync(MockSectionResultContext context, CancellationToken ct)
        => Task.FromResult(ResolveLegacy(context.SectionAttempt, "mock_section_attempt"));

    public static MockSectionResolvedResult ResolveLegacy(MockSectionAttempt section, string source)
    {
        var scaled = section.ScaledScore;
        if (scaled is null && section.SubtestCode is "reading" or "listening" && section.RawScore.HasValue)
        {
            scaled = OetScoring.OetRawToScaled(section.RawScore.Value);
        }

        var status = section.State == AttemptState.Completed
            ? scaled.HasValue ? "completed" : "pending_score"
            : "not_completed";

        return new MockSectionResolvedResult(
            status,
            section.RawScore,
            section.RawScoreMax,
            scaled,
            scaled.HasValue ? OetScoring.OetGradeLetterFromScaled(scaled.Value) : section.Grade,
            source);
    }
}

public sealed class MockSectionResultResolver(IEnumerable<IMockSectionResultAdapter> adapters)
{
    public async Task<MockSectionResolvedResult> ResolveAsync(MockSectionResultContext context, CancellationToken ct)
    {
        var adapter = adapters.First(adapter => adapter.Supports(context.SectionAttempt.SubtestCode));
        var resolved = await adapter.ResolveAsync(context, ct);

        context.SectionAttempt.RawScore = resolved.RawScore;
        context.SectionAttempt.RawScoreMax = resolved.RawScoreMax;
        context.SectionAttempt.ScaledScore = resolved.ScaledScore;
        context.SectionAttempt.Grade = resolved.Grade;
        context.SectionAttempt.FeedbackJson = JsonSupport.Serialize(new
        {
            resultStatus = resolved.ResultStatus,
            evidenceSource = resolved.EvidenceSource,
            reviewRequestId = resolved.ReviewRequestId,
            reviewState = resolved.ReviewState
        });

        return resolved;
    }
}

public sealed class MockReportAggregationService(
    LearnerDbContext db,
    NotificationService notifications,
    MockSectionResultResolver sectionResolver) : IMockReportAggregationService
{
    public async Task GenerateAsync(BackgroundJobItem job, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId)) return;

        var mockAttempt = await db.MockAttempts.FirstAsync(x => x.Id == job.ResourceId, ct);
        var report = await db.MockReports.FirstOrDefaultAsync(x => x.MockAttemptId == mockAttempt.Id, ct);
        if (report is null)
        {
            report = new MockReport
            {
                Id = $"mock-report-{Guid.NewGuid():N}",
                MockAttemptId = mockAttempt.Id
            };
            db.MockReports.Add(report);
        }

        var sections = await db.MockSectionAttempts
            .Where(x => x.MockAttemptId == mockAttempt.Id)
            .Join(db.MockBundleSections.AsNoTracking().Include(x => x.ContentPaper),
                sectionAttempt => sectionAttempt.MockBundleSectionId,
                bundleSection => bundleSection.Id,
                (sectionAttempt, bundleSection) => new { sectionAttempt, bundleSection })
            .OrderBy(x => x.bundleSection.SectionOrder)
            .ToListAsync(ct);

        var resolvedBySectionId = new Dictionary<string, MockSectionResolvedResult>(StringComparer.Ordinal);
        foreach (var row in sections)
        {
            var resolved = await sectionResolver.ResolveAsync(new MockSectionResultContext(db, mockAttempt, row.sectionAttempt, row.bundleSection), ct);
            resolvedBySectionId[row.sectionAttempt.Id] = resolved;
        }

        var reviewAttemptIds = sections
            .Select(x => x.sectionAttempt.ContentAttemptId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();
        List<ReviewRequest> reviewRequests = reviewAttemptIds.Count == 0
            ? []
            : await db.ReviewRequests.AsNoTracking()
                .Where(x => reviewAttemptIds.Contains(x.AttemptId))
                .ToListAsync(ct);

        var subTests = sections.Select(row =>
        {
            var section = row.sectionAttempt;
            var subtest = section.SubtestCode;
            var resolved = resolvedBySectionId[section.Id];
            var review = reviewRequests.FirstOrDefault(x => x.AttemptId == section.ContentAttemptId);
            var state = review is null ? resolved.ResultStatus : ReviewStateForReport(review.State);
            var scaled = resolved.ScaledScore;
            return new
            {
                id = subtest.Trim().ToLowerInvariant(),
                name = ToDisplaySubtest(subtest),
                score = scaled?.ToString() ?? (state is "queued" or "in_review" ? "Pending review" : "Pending"),
                rawScore = FormatMockRawScore(resolved, subtest),
                scaledScore = scaled,
                grade = scaled is null ? null : OetScoring.OetGradeLetterFromScaled(scaled.Value),
                state,
                evidenceSource = resolved.EvidenceSource,
                contentPaperTitle = row.bundleSection.ContentPaper?.Title,
                reviewRequestId = review?.Id,
                reviewState = review is null ? null : ReviewStateForReport(review.State)
            };
        }).ToList();

        var availableScores = subTests.Where(x => x.scaledScore.HasValue).Select(x => x.scaledScore!.Value).ToList();
        var overall = availableScores.Count == 0 ? (int?)null : (int)Math.Round(availableScores.Average(), MidpointRounding.AwayFromZero);
        var weakest = subTests.Where(x => x.scaledScore.HasValue).OrderBy(x => x.scaledScore).FirstOrDefault();
        var previousReports = await db.MockReports.AsNoTracking()
            .Join(db.MockAttempts.AsNoTracking().Where(x => x.UserId == mockAttempt.UserId && x.Id != mockAttempt.Id),
                r => r.MockAttemptId,
                a => a.Id,
                (r, _) => r)
            .Where(x => x.State == AsyncState.Completed && x.GeneratedAt != null)
            .OrderByDescending(x => x.GeneratedAt)
            .Take(1)
            .ToListAsync(ct);
        var priorOverall = previousReports.Select(x => TryReadOverallScore(x.PayloadJson)).FirstOrDefault(x => x.HasValue);
        var proctoringEvents = await db.MockProctoringEvents.AsNoTracking().Where(x => x.MockAttemptId == mockAttempt.Id).ToListAsync(ct);
        var perModuleReadiness = subTests.Select(x =>
        {
            var advisory = x.scaledScore.HasValue ? OetScoring.AdvisoryTier(x.scaledScore.Value) : null;
            return new
            {
                subtest = x.name,
                scaledScore = x.scaledScore,
                grade = x.grade,
                rag = advisory?.Tier ?? "pending",
                message = advisory?.Message ?? "Awaiting scored evidence or teacher review.",
                passThreshold = advisory?.PassThreshold
            };
        }).ToArray();
        var timingAnalysis = sections.Select(x => new
        {
            sectionId = x.sectionAttempt.Id,
            subtest = x.sectionAttempt.SubtestCode,
            startedAt = x.sectionAttempt.StartedAt,
            submittedAt = x.sectionAttempt.SubmittedAt,
            completedAt = x.sectionAttempt.CompletedAt,
            deadlineAt = x.sectionAttempt.DeadlineAt,
            secondsUsed = x.sectionAttempt.StartedAt is not null && (x.sectionAttempt.CompletedAt ?? x.sectionAttempt.SubmittedAt) is not null
                ? (int?)Math.Max(0, (int)((x.sectionAttempt.CompletedAt ?? x.sectionAttempt.SubmittedAt)!.Value - x.sectionAttempt.StartedAt.Value).TotalSeconds)
                : null
        }).ToArray();
        var weakestCriterion = weakest is null
            ? new { subtest = "Pending", criterion = "Awaiting evidence", description = "Complete scored sections or wait for expert-reviewed productive sections." }
            : new { subtest = weakest.name, criterion = "Lowest scaled sub-test", description = $"Prioritise {weakest.name} next; current scaled score is {weakest.scaledScore}/500." };
        var config = JsonSupport.Deserialize<Dictionary<string, object?>>(mockAttempt.ConfigJson, new Dictionary<string, object?>());

        report.State = AsyncState.Completed;
        report.GeneratedAt = DateTimeOffset.UtcNow;
        report.PayloadJson = JsonSupport.Serialize(new
        {
            id = report.Id,
            reportId = report.Id,
            mockAttemptId = mockAttempt.Id,
            title = mockAttempt.MockType == "sub" ? $"{ToDisplaySubtest(mockAttempt.SubtestCode ?? "mock")} Mock Report" : "Generated OET Mock Report",
            date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            profession = ReadString(config, "profession") ?? mockAttempt.Profession,
            targetCountry = ReadString(config, "targetCountry"),
            deliveryMode = ReadString(config, "deliveryMode") ?? mockAttempt.DeliveryMode,
            strictness = ReadString(config, "strictness") ?? mockAttempt.Strictness,
            releasePolicy = ReadString(config, "releasePolicy"),
            overallScore = overall?.ToString() ?? "Pending",
            overallGrade = overall is null ? null : OetScoring.OetGradeLetterFromScaled(overall.Value),
            summary = BuildMockReportSummary(overall, subTests.Count(x => x.state is "queued" or "in_review")),
            subTests,
            weakestCriterion,
            reviewSummary = new
            {
                queued = reviewRequests.Count(x => x.State == ReviewRequestState.Queued),
                inReview = reviewRequests.Count(x => x.State == ReviewRequestState.InReview),
                completed = reviewRequests.Count(x => x.State == ReviewRequestState.Completed),
                pending = reviewRequests.Count(x => x.State is ReviewRequestState.Queued or ReviewRequestState.InReview or ReviewRequestState.AwaitingPayment)
            },
            perModuleReadiness,
            partScores = subTests.Select(x => new { subtest = x.name, x.rawScore, x.scaledScore, x.grade, x.state, x.evidenceSource }).ToArray(),
            timingAnalysis,
            errorCategories = new[]
            {
                new { category = weakestCriterion.criterion, subtest = weakestCriterion.subtest, severity = "priority", description = weakestCriterion.description }
            },
            teacherReviewState = new
            {
                queued = reviewRequests.Count(x => x.State == ReviewRequestState.Queued),
                inReview = reviewRequests.Count(x => x.State == ReviewRequestState.InReview),
                completed = reviewRequests.Count(x => x.State == ReviewRequestState.Completed),
                pending = reviewRequests.Count(x => x.State is ReviewRequestState.Queued or ReviewRequestState.InReview or ReviewRequestState.AwaitingPayment)
            },
            bookingAdvice = BuildMockBookingAdvice(overall),
            retakeAdvice = new
            {
                recommendedWindowDays = 7,
                nextMockType = "sub",
                subtest = weakestCriterion.subtest,
                message = $"Retake a targeted {weakestCriterion.subtest} mock after completing the 7-day remediation plan."
            },
            proctoringSummary = new
            {
                totalEvents = proctoringEvents.Count,
                advisoryOnly = true,
                criticalEvents = proctoringEvents.Count(x => x.Severity == "critical"),
                warningEvents = proctoringEvents.Count(x => x.Severity == "warning"),
                byKind = proctoringEvents.GroupBy(x => x.Kind).Select(g => new { kind = g.Key, count = g.Count() }).ToArray(),
                message = proctoringEvents.Count == 0
                    ? "No integrity events were recorded. Proctoring is advisory and never blocks submission automatically."
                    : "Integrity events were recorded for teacher/admin review. They are advisory and did not block submission."
            },
            remediationPlan = BuildMockRemediationPlan(report.Id, weakestCriterion.subtest, weakestCriterion.criterion, weakestCriterion.description),
            priorComparison = priorOverall.HasValue && overall.HasValue
                ? new
                {
                    exists = true,
                    priorMockName = "Previous mock",
                    overallTrend = overall.Value > priorOverall.Value ? "up" : overall.Value < priorOverall.Value ? "down" : "flat",
                    details = $"Overall score changed by {overall.Value - priorOverall.Value:+#;-#;0} points since the previous mock."
                }
                : new { exists = false, priorMockName = string.Empty, overallTrend = "flat", details = "No earlier generated mock report is available for comparison." }
        });

        mockAttempt.ReportId = report.Id;
        mockAttempt.State = AttemptState.Completed;
        mockAttempt.CompletedAt = DateTimeOffset.UtcNow;

        var hasCompletionEvent = await db.AnalyticsEvents.AsNoTracking().AnyAsync(x =>
            x.UserId == mockAttempt.UserId &&
            x.EventName == "mock_completed" &&
            x.PayloadJson.Contains(mockAttempt.Id), ct);
        if (!hasCompletionEvent)
        {
            db.AnalyticsEvents.Add(new AnalyticsEventRecord
            {
                Id = $"evt-{Guid.NewGuid():N}",
                UserId = mockAttempt.UserId,
                EventName = "mock_completed",
                PayloadJson = JsonSupport.Serialize(new { mockAttemptId = mockAttempt.Id, reportId = report.Id, overallScore = overall }),
                OccurredAt = DateTimeOffset.UtcNow
            });
        }

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerMockReportReady,
            mockAttempt.UserId,
            "mock_attempt",
            mockAttempt.Id,
            report.Id,
            new Dictionary<string, object?> { ["mockAttemptId"] = mockAttempt.Id },
            ct);
    }

    private static string? ReadString(Dictionary<string, object?> values, string key)
        => values.TryGetValue(key, out var value) && value is not null && !string.IsNullOrWhiteSpace(value.ToString())
            ? value.ToString()
            : null;

    private static string FormatMockRawScore(MockSectionResolvedResult resolved, string subtestCode)
    {
        if (resolved.RawScore.HasValue && resolved.RawScoreMax.HasValue) return $"{resolved.RawScore}/{resolved.RawScoreMax}";
        if (resolved.RawScore.HasValue && (subtestCode is "reading" or "listening")) return $"{resolved.RawScore}/42";
        return "N/A";
    }

    private static string ReviewStateForReport(ReviewRequestState state) => state switch
    {
        ReviewRequestState.Queued => "queued",
        ReviewRequestState.InReview => "in_review",
        ReviewRequestState.Completed => "completed",
        ReviewRequestState.Failed => "failed",
        ReviewRequestState.Cancelled => "cancelled",
        ReviewRequestState.AwaitingPayment => "awaiting_payment",
        _ => state.ToString().ToLowerInvariant()
    };

    private static int? TryReadOverallScore(string payloadJson)
    {
        var payload = JsonSupport.Deserialize<Dictionary<string, object?>>(payloadJson, new Dictionary<string, object?>());
        return payload.TryGetValue("overallScore", out var value) && value is not null && int.TryParse(value.ToString(), out var parsed)
            ? parsed
            : null;
    }

    private static string BuildMockReportSummary(int? overall, int pendingReviews)
    {
        if (pendingReviews > 0) return $"The report is generated from available section evidence with {pendingReviews} expert-reviewed section(s) still pending.";
        return overall.HasValue
            ? $"The advisory overall mock score is {overall}/500, calculated as the rounded mean of available sub-test scaled scores."
            : "The report is waiting for scored section evidence before calculating an advisory overall score.";
    }

    private static object BuildMockBookingAdvice(int? overall)
    {
        if (!overall.HasValue) return new { status = "pending", message = "Wait for scored sections and teacher review before booking the official OET.", route = "/mocks/setup" };
        var advisory = OetScoring.AdvisoryTier(overall.Value);
        return new
        {
            status = advisory.Tier,
            score = overall.Value,
            message = advisory.Tier is "green" or "dark-green"
                ? "Use at least two consistent green mocks before booking the official OET."
                : "Complete remediation and retake a strict mock before booking.",
            route = "/exam-booking"
        };
    }

    private static object[] BuildMockRemediationPlan(string reportId, string subtest, string criterion, string description)
    {
        var normalized = string.IsNullOrWhiteSpace(subtest) ? "reading" : subtest.ToLowerInvariant();
        var route = normalized switch
        {
            var s when s.Contains("listening", StringComparison.OrdinalIgnoreCase) => "/listening",
            var s when s.Contains("reading", StringComparison.OrdinalIgnoreCase) => "/reading/practice",
            var s when s.Contains("writing", StringComparison.OrdinalIgnoreCase) => "/writing/library",
            var s when s.Contains("speaking", StringComparison.OrdinalIgnoreCase) => "/speaking/selection",
            _ => "/practice"
        };

        return
        [
            new { day = "Day 1", title = "Review every lost mark", description = "Compare answer review, timing notes, and teacher comments before attempting new work.", route = $"/mocks/report/{reportId}" },
            new { day = "Day 2", title = $"Repair {criterion}", description, route },
            new { day = "Day 3", title = "Complete a targeted micro-drill", description = $"Focus on {subtest} without full-exam pressure first.", route },
            new { day = "Day 4", title = "Attempt a sectional mock", description = "Check whether the repair transfers under timed conditions.", route = $"/mocks/setup?type=sub&subtest={Uri.EscapeDataString(normalized)}" },
            new { day = "Day 5-7", title = "Book tutor review or retake", description = "If Writing or Speaking is involved, request tutor feedback before another readiness mock.", route = "/mocks/setup" }
        ];
    }

    private static string ToDisplaySubtest(string subtest)
        => string.IsNullOrWhiteSpace(subtest) ? "Mock" : char.ToUpperInvariant(subtest[0]) + subtest[1..];
}