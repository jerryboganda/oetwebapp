using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Services.Mocks.Results;

public interface IMockSectionResultAdapter
{
    bool Supports(string subtestCode);
    Task<MockSectionResolvedResult> ResolveAsync(MockSectionResultContext context, CancellationToken ct);
}

public interface IMockReportAggregationService
{
    Task GenerateAsync(BackgroundJobItem job, CancellationToken ct);

    /// <summary>
    /// P5 Speaking adapter — average the two <see cref="SpeakingAiAssessment"/>
    /// rows that belong to a <see cref="SpeakingMockSession"/> (one per
    /// role-play) and write the combined scaled score + readiness band back
    /// onto the session as a snapshot. Returns the resolved aggregate so
    /// callers can include it in their response payload without a second
    /// trip to the DB.
    /// </summary>
    Task<SpeakingMockAggregateResult> AggregateSpeakingMockSessionAsync(
        string mockSessionId,
        CancellationToken ct);
}

/// <summary>
/// Combined Speaking mock result. <see cref="CombinedScaledScore"/> is the
/// equally-weighted mean of both halves' scaled scores; null if either half
/// has no AI assessment yet.
/// </summary>
public sealed record SpeakingMockAggregateResult(
    string MockSessionId,
    int? CombinedScaledScore,
    string ReadinessBandCode,
    string ReadinessBandLabel,
    int PassThreshold,
    SpeakingMockAggregateCriterion[] PerCriterion,
    SpeakingMockAggregateHalf RolePlay1,
    SpeakingMockAggregateHalf RolePlay2);

public sealed record SpeakingMockAggregateCriterion(
    string Code,
    double Average,
    int Max,
    int Score1,
    int Score2);

public sealed record SpeakingMockAggregateHalf(
    string AttemptId,
    string? SpeakingSessionId,
    string? AssessmentId,
    int? EstimatedScaledScore,
    string? ReadinessBand,
    string? OverallSummary,
    DateTimeOffset? GeneratedAt);

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
    MockSectionResultResolver sectionResolver,
    RemediationPlanService remediationPlan,
    ILogger<MockReportAggregationService> logger) : IMockReportAggregationService
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

        // Speaking & Writing are human-marked (never AI). While any productive
        // section is still awaiting an examiner, withhold the OVERALL band instead
        // of surfacing a misleading partial mean of only the auto-scored Reading/
        // Listening sections. R&L-only mocks are unaffected and release instantly.
        var hasProductiveSection = subTests.Any(x => x.id is "writing" or "speaking");
        var productivePending = subTests.Any(x => (x.id is "writing" or "speaking") && !x.scaledScore.HasValue);
        var availableScores = subTests.Where(x => x.scaledScore.HasValue).Select(x => x.scaledScore!.Value).ToList();
        var overall = (productivePending || availableScores.Count == 0)
            ? (int?)null
            : (int)Math.Round(availableScores.Average(), MidpointRounding.AwayFromZero);
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
        // V1 schema marker — consumers branch on payloadSchemaVersion. See MockReportPayloadV1.cs
        // and lib/mocks/report-payload.ts for the typed contract.
        report.PayloadSchemaVersion = "v1";
        report.PayloadJson = JsonSupport.Serialize(new
        {
            payloadSchemaVersion = "v1",
            id = report.Id,
            reportId = report.Id,
            mockAttemptId = mockAttempt.Id,
            title = mockAttempt.MockType == "sub" ? $"{ToDisplaySubtest(mockAttempt.SubtestCode ?? "mock")} Mock Report" : "Generated OET Mock Report",
            date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            profession = ReadString(config, "profession") ?? mockAttempt.Profession,
            targetCountry = ReadString(config, "targetCountry"),
            deliveryMode = ReadString(config, "deliveryMode") ?? mockAttempt.DeliveryMode,
            strictness = ReadString(config, "strictness") ?? mockAttempt.Strictness,
            // S/W-bearing mocks release only after a human examiner marks the
            // productive sections; R&L-only mocks keep their configured policy.
            releasePolicy = hasProductiveSection
                ? MockReleasePolicies.AfterTeacherMarking
                : ReadString(config, "releasePolicy"),
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

        // Seed the deterministic 7-day RemediationTask rows so the learner
        // dashboard can render the post-mock plan immediately, without the
        // learner needing to manually POST /v1/mocks/reports/{id}/remediation-plan/generate.
        // GenerateFromReportAsync is idempotent per (userId, reportId).
        try
        {
            await remediationPlan.GenerateFromReportAsync(mockAttempt.UserId, report.Id, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Auto-seed of remediation plan failed for report {ReportId}; learner can still POST to /remediation-plan/generate.", report.Id);
        }
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
            var s when s.Contains("writing", StringComparison.OrdinalIgnoreCase) => "/writing/practice/library",
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

    // ─────────────────────────────────────────────────────────────────────
    // P5 — Speaking mock-set aggregation
    // ─────────────────────────────────────────────────────────────────────
    //
    // A Speaking mock set is two role-plays. Each half has its own
    // `Attempt` row (linked via `SpeakingMockSession.Attempt1Id` /
    // `Attempt2Id`) and a `SpeakingSession` that wraps that `Attempt`. The
    // AI scorer writes a `SpeakingAiAssessment` keyed on the
    // `SpeakingSessionId`.
    //
    // Aggregation = equal-weight mean of both halves' criterion scores,
    // projected through `OetScoring.SpeakingProjectedScaled` (the SINGLE
    // canonical projection helper — never inline 70%/350 math here) into a
    // combined scaled score + readiness band. Persist on the session row so
    // a later AI re-score does not silently change the snapshot the learner
    // already saw.

    public async Task<SpeakingMockAggregateResult> AggregateSpeakingMockSessionAsync(
        string mockSessionId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mockSessionId))
            throw new ArgumentException("mockSessionId is required", nameof(mockSessionId));

        var session = await db.SpeakingMockSessions
            .FirstOrDefaultAsync(x => x.Id == mockSessionId, ct)
            ?? throw new InvalidOperationException($"SpeakingMockSession {mockSessionId} not found");

        // Look up the SpeakingSession that wraps each Attempt — the AI
        // assessment is keyed on SpeakingSession.Id, not Attempt.Id.
        var attemptIds = new[] { session.Attempt1Id, session.Attempt2Id };
        var speakingSessions = await db.SpeakingSessions.AsNoTracking()
            .Where(x => x.AttemptId != null && attemptIds.Contains(x.AttemptId))
            .ToListAsync(ct);

        var speaking1 = speakingSessions.FirstOrDefault(x => x.AttemptId == session.Attempt1Id);
        var speaking2 = speakingSessions.FirstOrDefault(x => x.AttemptId == session.Attempt2Id);

        var speakingIds = speakingSessions.Select(x => x.Id).ToList();
        // No AI for mock Speaking: the mock band is the HUMAN examiner's final
        // SpeakingTutorAssessment, never an AI assessment. Prefer the moderated
        // marker, then the primary marker, then any final row per session. Until a
        // tutor finalises a half, that half stays unscored → the session is pending.
        var assessments = speakingIds.Count == 0
            ? new List<SpeakingTutorAssessment>()
            : await db.SpeakingTutorAssessments.AsNoTracking()
                .Where(x => x.IsFinal && speakingIds.Contains(x.SpeakingSessionId))
                .OrderByDescending(x => x.SubmittedAt ?? x.UpdatedAt)
                .ToListAsync(ct);

        var ai1 = PickCanonicalTutorAssessment(assessments, speaking1?.Id);
        var ai2 = PickCanonicalTutorAssessment(assessments, speaking2?.Id);

        // Build the per-criterion average only when both halves are present.
        SpeakingMockAggregateCriterion[] perCriterion;
        int? combinedScaled = null;
        string bandCode;

        if (ai1 is not null && ai2 is not null)
        {
            perCriterion = new[]
            {
                Avg("intelligibility",       ai1.Intelligibility,        ai2.Intelligibility,        6),
                Avg("fluency",               ai1.Fluency,                ai2.Fluency,                6),
                Avg("appropriateness",       ai1.Appropriateness,        ai2.Appropriateness,        6),
                Avg("grammarExpression",     ai1.GrammarExpression,      ai2.GrammarExpression,      6),
                Avg("relationshipBuilding",  ai1.RelationshipBuilding,   ai2.RelationshipBuilding,   3),
                Avg("patientPerspective",    ai1.PatientPerspective,     ai2.PatientPerspective,     3),
                Avg("structure",             ai1.Structure,              ai2.Structure,              3),
                Avg("informationGathering",  ai1.InformationGathering,   ai2.InformationGathering,   3),
                Avg("informationGiving",     ai1.InformationGiving,      ai2.InformationGiving,      3),
            };

            // Use OetScoring.SpeakingProjectedScaled with averaged-then-rounded
            // criterion scores so the projection helper stays the single
            // source of truth for the rubric → scaled mapping.
            var averagedScores = new OetScoring.SpeakingCriterionScores(
                Intelligibility:      RoundHalfUp((ai1.Intelligibility      + ai2.Intelligibility)      / 2.0),
                Fluency:              RoundHalfUp((ai1.Fluency              + ai2.Fluency)              / 2.0),
                Appropriateness:      RoundHalfUp((ai1.Appropriateness      + ai2.Appropriateness)      / 2.0),
                GrammarExpression:    RoundHalfUp((ai1.GrammarExpression    + ai2.GrammarExpression)    / 2.0),
                RelationshipBuilding: RoundHalfUp((ai1.RelationshipBuilding + ai2.RelationshipBuilding) / 2.0),
                PatientPerspective:   RoundHalfUp((ai1.PatientPerspective   + ai2.PatientPerspective)   / 2.0),
                Structure:            RoundHalfUp((ai1.Structure            + ai2.Structure)            / 2.0),
                InformationGathering: RoundHalfUp((ai1.InformationGathering + ai2.InformationGathering) / 2.0),
                InformationGiving:    RoundHalfUp((ai1.InformationGiving    + ai2.InformationGiving)    / 2.0));

            // Two paths to the combined scaled score: (a) project the
            // averaged criterion scores via OetScoring; (b) average the two
            // already-projected scaled scores. We use (a) so the result
            // honours the canonical 70/30 anchor and stays stable even if a
            // criterion is later re-rounded.
            combinedScaled = OetScoring.SpeakingProjectedScaled(averagedScores);
            bandCode = OetScoring.SpeakingReadinessBandCode(
                OetScoring.SpeakingReadinessBandFromScaled(combinedScaled.Value));

            // Persist snapshot the FIRST time both halves complete. If we
            // already have a snapshot, leave it alone so a re-run after a
            // re-score does not silently change historical numbers.
            if (session.CombinedScaledSnapshot is null || string.IsNullOrEmpty(session.ReadinessBandSnapshot))
            {
                session.CombinedScaledSnapshot = combinedScaled;
                session.ReadinessBandSnapshot = bandCode;
                if (session.State != SpeakingMockSessionState.Completed)
                {
                    session.State = SpeakingMockSessionState.Completed;
                    session.CompletedAt = DateTimeOffset.UtcNow;
                }
                session.OrchestratorState = SpeakingMockOrchestratorStates.Aggregated;
                await db.SaveChangesAsync(ct);
            }
            else
            {
                // Prefer the persisted snapshot for display consistency.
                combinedScaled = session.CombinedScaledSnapshot ?? combinedScaled;
                bandCode = session.ReadinessBandSnapshot ?? bandCode;
            }
        }
        else
        {
            // Only one (or zero) halves scored — fall back to the existing
            // snapshot if one exists, otherwise NotReady.
            combinedScaled = session.CombinedScaledSnapshot;
            bandCode = string.IsNullOrEmpty(session.ReadinessBandSnapshot)
                ? OetScoring.SpeakingReadinessBandCode(OetScoring.SpeakingReadinessBand.NotReady)
                : session.ReadinessBandSnapshot;

            perCriterion = Array.Empty<SpeakingMockAggregateCriterion>();
        }

        return new SpeakingMockAggregateResult(
            MockSessionId: session.Id,
            CombinedScaledScore: combinedScaled,
            ReadinessBandCode: bandCode,
            ReadinessBandLabel: SpeakingBandLabel(bandCode),
            PassThreshold: OetScoring.ScaledPassGradeB,
            PerCriterion: perCriterion,
            RolePlay1: BuildHalf(session.Attempt1Id, speaking1, ai1),
            RolePlay2: BuildHalf(session.Attempt2Id, speaking2, ai2));

        static SpeakingMockAggregateCriterion Avg(string code, int a, int b, int max)
            => new(code, Math.Round((a + b) / 2.0, 2), max, a, b);

        static int RoundHalfUp(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

        static SpeakingTutorAssessment? PickCanonicalTutorAssessment(
            List<SpeakingTutorAssessment> all, string? speakingSessionId)
        {
            if (string.IsNullOrEmpty(speakingSessionId)) return null;
            var forSession = all.Where(a => a.SpeakingSessionId == speakingSessionId).ToList();
            return forSession.FirstOrDefault(a => a.MarkerRole == "moderated")
                ?? forSession.FirstOrDefault(a => a.MarkerRole == "primary")
                ?? forSession.FirstOrDefault();
        }

        static SpeakingMockAggregateHalf BuildHalf(string attemptId, SpeakingSession? sess, SpeakingTutorAssessment? ai)
            => new(
                AttemptId: attemptId,
                SpeakingSessionId: sess?.Id,
                AssessmentId: ai?.Id,
                EstimatedScaledScore: ai?.EstimatedScaledScore,
                ReadinessBand: ai?.ReadinessBand,
                OverallSummary: ai?.OverallFeedbackMarkdown,
                GeneratedAt: ai?.SubmittedAt);
    }

    private static string SpeakingBandLabel(string code) => code switch
    {
        "not_ready"  => "Not ready",
        "developing" => "Developing",
        "borderline" => "Borderline",
        "exam_ready" => "Exam-ready",
        "strong"     => "Strong",
        _             => "Not ready",
    };
}