using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Services;

public sealed partial class MockService
{
    public async Task<object> GetAdminMockAnalyticsAsync(CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-30);
        var attempts = await db.MockAttempts.AsNoTracking()
            .Where(x => x.StartedAt >= since)
            .ToListAsync(ct);
        var reports = await db.MockReports.AsNoTracking()
            .Where(x => x.GeneratedAt != null && x.GeneratedAt >= since)
            .ToListAsync(ct);
        var reviewAttemptIds = await db.Attempts.AsNoTracking()
            .Where(x => x.Context == "mock")
            .Select(x => x.Id)
            .ToListAsync(ct);
        List<ReviewRequest> reviewRequests = reviewAttemptIds.Count == 0
            ? []
            : await db.ReviewRequests.AsNoTracking()
                .Where(x => reviewAttemptIds.Contains(x.AttemptId))
                .ToListAsync(ct);
        var scored = reports
            .Where(x => !ContainsGovernedScore(x.PayloadJson))
            .Select(x => ParsePayloadOverallScore(x.PayloadJson))
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();
        var average = scored.Count == 0 ? (double?)null : Math.Round(scored.Average(), 1);

        return new
        {
            windowDays = 30,
            attemptsStarted = attempts.Count,
            attemptsCompleted = attempts.Count(x => x.State == AttemptState.Completed),
            completionRate = attempts.Count == 0 ? 0 : Math.Round(attempts.Count(x => x.State == AttemptState.Completed) * 100.0 / attempts.Count, 1),
            reportsGenerated = reports.Count,
            averageReadinessScore = average,
            greenReadinessCount = scored.Count(x => OetScoring.AdvisoryTier(x).Tier is "green" or "dark-green"),
            markingDelayMetrics = new
            {
                queued = reviewRequests.Count(x => x.State == ReviewRequestState.Queued),
                inReview = reviewRequests.Count(x => x.State == ReviewRequestState.InReview),
                completed = reviewRequests.Count(x => x.State == ReviewRequestState.Completed),
                averageTurnaroundHours = reviewRequests
                    .Where(x => x.CompletedAt.HasValue)
                    .Select(x => (x.CompletedAt!.Value - x.CreatedAt).TotalHours)
                    .DefaultIfEmpty(0)
                    .Average()
            },
            learnerRiskListRoute = "/v1/admin/mocks/risk-list"
        };
    }

    public async Task<object> GetAdminMockRiskListAsync(CancellationToken ct)
    {
        var reports = await db.MockReports.AsNoTracking()
            .Join(db.MockAttempts.AsNoTracking(),
                report => report.MockAttemptId,
                attempt => attempt.Id,
                (report, attempt) => new { report, attempt })
            .Where(x => x.report.State == AsyncState.Completed && x.report.GeneratedAt != null)
            .OrderByDescending(x => x.report.GeneratedAt)
            .Take(200)
            .ToListAsync(ct);

        var items = reports
            .Select(x => new
            {
                learnerId = x.attempt.UserId,
                mockAttemptId = x.attempt.Id,
                reportId = x.report.Id,
                generatedAt = x.report.GeneratedAt,
                score = ContainsGovernedScore(x.report.PayloadJson)
                    ? null
                    : ParsePayloadOverallScore(x.report.PayloadJson),
                weakest = ReadWeakestCriterion(JsonSupport.Deserialize(x.report.PayloadJson, new Dictionary<string, object?>()))
            })
            .Where(x => !x.score.HasValue || OetScoring.AdvisoryTier(x.score.Value).Tier is "red" or "amber")
            .Select(x => new
            {
                x.learnerId,
                x.mockAttemptId,
                x.reportId,
                x.generatedAt,
                overallScore = x.score,
                risk = x.score.HasValue ? OetScoring.AdvisoryTier(x.score.Value).Tier : "pending",
                weakness = new { x.weakest.Subtest, x.weakest.Criterion, x.weakest.Description },
                action = "Assign remediation or teacher follow-up"
            })
            .Take(50)
            .ToArray();

        return new { items };
    }

    private async Task EnrichMockReportPayloadAsync(
        MockAttempt attempt,
        MockReport report,
        Dictionary<string, object?> payload,
        CancellationToken ct)
    {
        var subTests = ReadSubTests(payload);
        var sections = await db.MockSectionAttempts.AsNoTracking()
            .Where(x => x.MockAttemptId == attempt.Id)
            .OrderBy(x => x.StartedAt)
            .ToListAsync(ct);
        var proctoringEvents = await db.MockProctoringEvents.AsNoTracking()
            .Where(x => x.MockAttemptId == attempt.Id)
            .ToListAsync(ct);

        var perModuleReadiness = subTests.Select(st =>
        {
            var name = StringValue(st, "name") ?? StringValue(st, "subtest") ?? "Mock";
            var governedScore = name.Trim().ToLowerInvariant() is "reading" or "listening";
            var section = governedScore
                ? sections.FirstOrDefault(x => string.Equals(x.SubtestCode, name, StringComparison.OrdinalIgnoreCase))
                : null;
            var score = governedScore
                ? section is not null && IsOwnerConvertedSection(section) ? section.ScaledScore : null
                : IntValue(st, "scaledScore") ?? ParseScore(StringValue(st, "score"));
            var advisory = !governedScore && score.HasValue
                ? OetScoring.AdvisoryTier(score.Value)
                : null;
            return new
            {
                subtest = name,
                scaledScore = score,
                grade = governedScore ? score.HasValue ? section!.Grade : null : score.HasValue ? OetScoring.OetGradeLetterFromScaled(score.Value) : null,
                rag = governedScore ? (score.HasValue ? "owner-converted" : "pending") : advisory?.Tier ?? "pending",
                message = governedScore
                    ? (score.HasValue
                        ? "Owner-approved conversion is available; no mock-wide pass label is inferred."
                        : "Awaiting owner-approved score conversion or teacher review.")
                    : advisory?.Message ?? "Awaiting scored evidence or teacher review.",
                passThreshold = governedScore ? null : advisory?.PassThreshold
            };
        }).ToArray();

        payload["perModuleReadiness"] = perModuleReadiness;
        payload["partScores"] = subTests.Select(st => new
        {
            subtest = StringValue(st, "name") ?? StringValue(st, "subtest") ?? "Mock",
            rawScore = StringValue(st, "rawScore") ?? "N/A",
            scaledScore = IsGovernedSubtest(st)
                ? sections.FirstOrDefault(x => string.Equals(x.SubtestCode, StringValue(st, "name") ?? StringValue(st, "subtest"), StringComparison.OrdinalIgnoreCase)) is { } scaledSection && IsOwnerConvertedSection(scaledSection)
                    ? scaledSection.ScaledScore
                    : null
                : IntValue(st, "scaledScore"),
            grade = IsGovernedSubtest(st)
                ? sections.FirstOrDefault(x => string.Equals(x.SubtestCode, StringValue(st, "name") ?? StringValue(st, "subtest"), StringComparison.OrdinalIgnoreCase)) is { } gradeSection && IsOwnerConvertedSection(gradeSection)
                    ? gradeSection.Grade
                    : null
                : StringValue(st, "grade"),
            state = StringValue(st, "state") ?? "completed"
        }).ToArray();
        var governedConversionPending = subTests.Any(st =>
        {
            if (!IsGovernedSubtest(st)) return false;
            var subtest = StringValue(st, "name") ?? StringValue(st, "subtest") ?? string.Empty;
            var section = sections.FirstOrDefault(x => string.Equals(x.SubtestCode, subtest, StringComparison.OrdinalIgnoreCase));
            return section is null || !IsOwnerConvertedSection(section);
        });
        if (governedConversionPending)
        {
            payload["overallScore"] = "Pending";
            payload["overallGrade"] = null;
            payload["summary"] = "The mock-wide score is waiting for owner-approved Reading/Listening conversion evidence.";
        }
        payload["timingAnalysis"] = sections.Select(section => new
        {
            sectionId = section.Id,
            subtest = section.SubtestCode,
            startedAt = section.StartedAt,
            submittedAt = section.SubmittedAt,
            completedAt = section.CompletedAt,
            secondsUsed = section.StartedAt is not null && (section.CompletedAt ?? section.SubmittedAt) is not null
                ? (int?)Math.Max(0, (int)((section.CompletedAt ?? section.SubmittedAt)!.Value - section.StartedAt.Value).TotalSeconds)
                : null,
            deadlineAt = section.DeadlineAt
        }).ToArray();
        payload["errorCategories"] = BuildReportErrorCategories(payload);
        payload["teacherReviewState"] = payload.TryGetValue("reviewSummary", out var reviewSummary) ? reviewSummary : new
        {
            queued = 0,
            inReview = 0,
            completed = 0,
            pending = 0
        };
        payload["bookingAdvice"] = BuildBookingAdvice(payload);
        payload["retakeAdvice"] = BuildRetakeAdvice(payload);
        payload["proctoringSummary"] = new
        {
            totalEvents = proctoringEvents.Count,
            advisoryOnly = true,
            criticalEvents = proctoringEvents.Count(x => x.Severity == "critical"),
            warningEvents = proctoringEvents.Count(x => x.Severity == "warning"),
            byKind = proctoringEvents
                .GroupBy(x => x.Kind)
                .OrderByDescending(g => g.Count())
                .Select(g => new { kind = g.Key, count = g.Count() })
                .ToArray(),
            message = proctoringEvents.Count == 0
                ? "No integrity events were recorded. Proctoring is advisory and never blocks submission automatically."
                : "Integrity events were recorded for teacher/admin review. They are advisory and did not block submission."
        };
        payload["releasePolicy"] = ReadConfigValue(attempt.ConfigJson, "releasePolicy") ?? MockReleasePolicies.Instant;
        payload["remediationPlan"] = BuildServerRemediationPlan(payload, report.Id);
    }

    private async Task SeedMockRemediationStudyPlanAsync(
        string userId,
        MockAttempt attempt,
        MockReport report,
        Dictionary<string, object?> payload,
        CancellationToken ct)
    {
        if (report.State != AsyncState.Completed) return;
        var contentPrefix = $"mock-remediation:{attempt.Id}:";
        var alreadySeeded = await db.StudyPlanItems.AsNoTracking()
            .Join(db.StudyPlans.AsNoTracking().Where(x => x.UserId == userId),
                item => item.StudyPlanId,
                plan => plan.Id,
                (item, plan) => item)
            .AnyAsync(x => x.ContentId != null && x.ContentId.StartsWith(contentPrefix), ct);
        if (alreadySeeded) return;

        var plan = await db.StudyPlans
            .Where(x => x.UserId == userId && x.State == AsyncState.Completed)
            .OrderByDescending(x => x.GeneratedAt)
            .FirstOrDefaultAsync(ct);
        var weakness = ReadWeakestCriterion(payload);
        var weakSubtest = NormalizeSubtestOrDefault(weakness.Subtest);
        var now = DateTimeOffset.UtcNow;
        if (plan is null)
        {
            plan = new StudyPlan
            {
                Id = $"plan-{Guid.NewGuid():N}",
                UserId = userId,
                Version = 1,
                GeneratedAt = now,
                State = AsyncState.Completed,
                Checkpoint = "Created from your latest mock report.",
                WeakSkillFocus = $"{weakness.Subtest}: {weakness.Criterion}",
                ExamFamilyCode = attempt.ExamFamilyCode,
                ExamTypeCode = attempt.ExamTypeCode
            };
            db.StudyPlans.Add(plan);
        }
        else
        {
            plan.Version += 1;
            plan.GeneratedAt = now;
            plan.Checkpoint = "Updated from your latest mock report.";
            plan.WeakSkillFocus = $"{weakness.Subtest}: {weakness.Criterion}";
        }

        var actions = BuildServerRemediationPlan(payload, report.Id).Take(7).ToArray();
        for (var i = 0; i < actions.Length; i++)
        {
            db.StudyPlanItems.Add(new StudyPlanItem
            {
                Id = $"study-item-{Guid.NewGuid():N}",
                StudyPlanId = plan.Id,
                Title = actions[i].Title,
                SubtestCode = weakSubtest,
                DurationMinutes = 30,
                Rationale = actions[i].Description,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(i + 1)),
                Status = StudyPlanItemStatus.NotStarted,
                Section = i == 0 ? "today" : "thisWeek",
                ContentId = $"{contentPrefix}{i + 1}",
                ItemType = "mock_remediation"
            });
        }
        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            Id = $"evt-{Guid.NewGuid():N}",
            UserId = userId,
            EventName = "mock_remediation_plan_seeded",
            PayloadJson = JsonSupport.Serialize(new { mockAttemptId = attempt.Id, reportId = report.Id, itemCount = actions.Length }),
            OccurredAt = now
        });
        await db.SaveChangesAsync(ct);
    }

    private static IReadOnlyList<Dictionary<string, object?>> ReadSubTests(Dictionary<string, object?> payload)
    {
        if (!payload.TryGetValue("subTests", out var raw) || raw is null) return [];
        return JsonSupport.Deserialize(JsonSupport.Serialize(raw), new List<Dictionary<string, object?>>());
    }

    private static bool IsGovernedSubtest(Dictionary<string, object?> subtest)
        => (StringValue(subtest, "name") ?? StringValue(subtest, "subtest") ?? string.Empty)
            .Trim().ToLowerInvariant() is "reading" or "listening";

    private static (string Subtest, string Criterion, string Description) ReadWeakestCriterion(Dictionary<string, object?> payload)
    {
        if (!payload.TryGetValue("weakestCriterion", out var raw) || raw is null)
        {
            return ("Reading", "Awaiting evidence", "Complete a mock to generate personalised remediation.");
        }
        var dict = JsonSupport.Deserialize(JsonSupport.Serialize(raw), new Dictionary<string, object?>());
        return (
            StringValue(dict, "subtest") ?? "Reading",
            StringValue(dict, "criterion") ?? "Awaiting evidence",
            StringValue(dict, "description") ?? "Complete a mock to generate personalised remediation.");
    }

    private static IEnumerable<(string Day, string Title, string Description, string Route)> BuildServerRemediationPlan(
        Dictionary<string, object?> payload,
        string reportId)
    {
        var weakness = ReadWeakestCriterion(payload);
        var subtest = weakness.Subtest.ToLowerInvariant();
        var route = RouteForSubtest(subtest);
        return
        [
            ("Day 1", "Review every lost mark", "Compare answer review, timing notes, and teacher comments before attempting new work.", $"/mocks/report/{Uri.EscapeDataString(reportId)}"),
            ("Day 2", $"Repair {weakness.Criterion}", weakness.Description, route),
            ("Day 3", "Complete a targeted micro-drill", $"Focus on {weakness.Subtest} without full-exam pressure first.", route),
            ("Day 4", "Attempt a sectional mock", "Check whether the repair transfers under timed conditions.", $"/mocks/setup?type=sub&subtest={Uri.EscapeDataString(NormalizeSubtestOrDefault(subtest))}"),
            ("Day 5-7", "Book tutor review or retake", "If Writing or Speaking is involved, request tutor feedback before another readiness mock.", "/mocks/setup")
        ];
    }

    private static object BuildReportErrorCategories(Dictionary<string, object?> payload)
    {
        var weakness = ReadWeakestCriterion(payload);
        return new[]
        {
            new
            {
                category = weakness.Criterion,
                subtest = weakness.Subtest,
                severity = "priority",
                description = weakness.Description
            }
        };
    }

    private static object BuildBookingAdvice(Dictionary<string, object?> payload)
    {
        var score = ParseScore(payload.TryGetValue("overallScore", out var raw) ? raw?.ToString() : null);
        if (!score.HasValue)
        {
            return new { status = "pending", message = "Wait for scored sections and teacher review before booking the official OET.", route = "/mocks/setup" };
        }
        if (ReadSubTests(payload).Any(st => (StringValue(st, "name") ?? StringValue(st, "subtest") ?? string.Empty)
            .Trim().ToLowerInvariant() is "reading" or "listening"))
        {
            return new
            {
                status = "pending",
                score,
                message = "Use the owner-approved per-assessment pass labels before booking the official OET; no mock-wide pass claim is inferred.",
                route = "/billing/exam-booking"
            };
        }
        var advisory = OetScoring.AdvisoryTier(score.Value);
        return new
        {
            status = advisory.Tier,
            score,
            message = advisory.Tier is "green" or "dark-green"
                ? "Use at least two consistent green mocks before booking the official OET."
                : "Complete remediation and retake a strict mock before booking.",
            route = "/billing/exam-booking"
        };
    }

    private static object BuildRetakeAdvice(Dictionary<string, object?> payload)
    {
        var weakness = ReadWeakestCriterion(payload);
        return new
        {
            recommendedWindowDays = 7,
            nextMockType = "sub",
            subtest = NormalizeSubtestOrDefault(weakness.Subtest),
            message = $"Retake a targeted {weakness.Subtest} mock after completing the 7-day remediation plan."
        };
    }

    private static string? ReadConfigValue(string configJson, string key)
    {
        var config = JsonSupport.Deserialize(configJson, new Dictionary<string, object?>());
        return config.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static string? StringValue(Dictionary<string, object?> dict, string key)
        => dict.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static int? IntValue(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value) || value is null) return null;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static int? ParseScore(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains("pending", StringComparison.OrdinalIgnoreCase)) return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (!int.TryParse(digits, out var parsed)) return null;
        return value.Contains('%', StringComparison.Ordinal) ? Math.Clamp(parsed * 5, OetScoring.ScaledMin, OetScoring.ScaledMax) : parsed;
    }

    private static int? ParsePayloadOverallScore(string payloadJson)
    {
        var payload = JsonSupport.Deserialize(payloadJson, new Dictionary<string, object?>());
        return ParseScore(payload.TryGetValue("overallScore", out var raw) ? raw?.ToString() : null);
    }

    private static bool ContainsGovernedScore(string payloadJson)
        => ContainsGovernedScore(JsonSupport.Deserialize(payloadJson, new Dictionary<string, object?>()));

    private static bool ContainsGovernedScore(Dictionary<string, object?> payload)
        => ReadSubTests(payload).Any(st => (StringValue(st, "name") ?? StringValue(st, "subtest") ?? string.Empty)
            .Trim().ToLowerInvariant() is "reading" or "listening");

    private static string NormalizeSubtestOrDefault(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return FullMockOrder.Contains(normalized ?? string.Empty) ? normalized! : "reading";
    }

    private static string RouteForSubtest(string? subtest) => NormalizeSubtestOrDefault(subtest) switch
    {
        "listening" => "/listening",
        "reading" => "/reading/practice",
        "writing" => "/writing/practice/library",
        "speaking" => "/speaking/selection",
        _ => "/practice"
    };

    /// <summary>
    /// Extracts the trend direction ("up" | "down" | "flat" | null) from the latest report payload's
    /// priorComparison block. Returns null when no comparison is available (first report).
    /// Used by the Mock Center "Recommended next step" card to visualise momentum.
    /// </summary>
    private static string? ExtractReportTrend(Dictionary<string, object?>? latestReport)
    {
        if (latestReport is null) return null;
        if (!latestReport.TryGetValue("priorComparison", out var priorRaw) || priorRaw is null) return null;
        try
        {
            var priorJson = JsonSupport.Serialize(priorRaw);
            var prior = JsonSupport.Deserialize<Dictionary<string, object?>>(priorJson, new Dictionary<string, object?>());
            if (prior.TryGetValue("exists", out var exists) && exists is bool b && !b) return null;
            return prior.TryGetValue("overallTrend", out var trend) ? trend?.ToString() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Builds a lightweight readiness advisory object for the Mock Center.
    /// Reading/Listening reports remain pending here because a mock-wide score
    /// cannot inherit an owner pass label from a shared numeric threshold.
    /// </summary>
    private static object? BuildReadinessAdvisory(Dictionary<string, object?>? latestReport)
    {
        if (latestReport is null) return null;
        if (!latestReport.TryGetValue("overallScore", out var scoreRaw) || scoreRaw is null) return null;
        if (!int.TryParse(scoreRaw.ToString(), out var overall)) return null;

        if (ReadSubTests(latestReport).Any(st => (StringValue(st, "name") ?? StringValue(st, "subtest") ?? string.Empty)
            .Trim().ToLowerInvariant() is "reading" or "listening"))
        {
            return new
            {
                tier = "pending",
                message = "Owner-approved per-assessment pass labels are required; no mock-wide pass claim is inferred.",
                passThreshold = (int?)null,
                overallScore = overall,
            };
        }

        var advisory = OetScoring.AdvisoryTier(overall);
        return new
        {
            tier = advisory.Tier,
            message = advisory.Message,
            passThreshold = advisory.PassThreshold,
            overallScore = advisory.OverallScore,
        };
    }

    /// <summary>
    /// Phase C1 — read-only Score Guarantee signal for the Mock Center. Surfaces the active pledge's
    /// state without duplicating billing logic; refund / claim flows remain owned by the billing module.
    /// Returns null when the learner has no pledge on file, so the UI can hide the card entirely.
    /// </summary>
    private async Task<object?> BuildScoreGuaranteeSignalAsync(
        string userId,
        Dictionary<string, object?>? latestReport,
        CancellationToken ct)
    {
        // Most-recent pledge (covers both "active" and recently terminal states so the UI can show a
        // closed result briefly before it ages out). Only surface the newest one.
        var pledge = await db.ScoreGuaranteePledges.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.ActivatedAt)
            .FirstOrDefaultAsync(ct);
        if (pledge is null) return null;

        var guaranteedScore = pledge.BaselineScore + pledge.GuaranteedImprovement;
        int? latestScore = null;
        if (latestReport is not null
            && latestReport.TryGetValue("overallScore", out var scoreRaw)
            && scoreRaw is not null
            && int.TryParse(scoreRaw.ToString(), out var parsed))
        {
            latestScore = parsed;
        }

        var now = DateTimeOffset.UtcNow;
        var daysRemaining = pledge.ExpiresAt > now
            ? (int)Math.Ceiling((pledge.ExpiresAt - now).TotalDays)
            : 0;
        var isActive = string.Equals(pledge.Status, "active", StringComparison.OrdinalIgnoreCase) && pledge.ExpiresAt > now;
        var onTrack = latestScore.HasValue && latestScore.Value >= guaranteedScore;

        // Gentle advisory copy keyed to the status + progression so the Mock Center card reads as
        // guidance, not a verdict. Refund eligibility language is NOT made here — that belongs to
        // /billing/score-guarantee.
        string message;
        if (!isActive)
        {
            message = pledge.Status switch
            {
                "claim_approved" => "Your Score Guarantee claim was approved — check billing for the refund status.",
                "claim_rejected" => "Your Score Guarantee claim was reviewed. Visit billing for details.",
                "claim_submitted" => "Your Score Guarantee claim is under review by the admin team.",
                "expired" => "Your Score Guarantee window has closed.",
                _ => "Your Score Guarantee is no longer active."
            };
        }
        else if (!latestScore.HasValue)
        {
            message = $"Score Guarantee is active. Complete a full mock to check progress toward {guaranteedScore}/500.";
        }
        else if (onTrack)
        {
            message = $"You're on track — latest mock ({latestScore}) is at or above the guaranteed {guaranteedScore}/500.";
        }
        else
        {
            var gap = guaranteedScore - latestScore.Value;
            message = $"Latest mock is {latestScore}/500 — {gap} points under the guaranteed {guaranteedScore}. Keep practising.";
        }

        return new
        {
            status = pledge.Status,
            isActive,
            baselineScore = pledge.BaselineScore,
            guaranteedScore,
            guaranteedImprovement = pledge.GuaranteedImprovement,
            latestOverallScore = latestScore,
            onTrack,
            daysRemaining,
            expiresAt = pledge.ExpiresAt,
            message,
            route = "/billing/score-guarantee"
        };
    }

    /// <summary>
    /// Phase C2 — anonymised cohort percentile. Computes the learner's percentile against the
    /// cohort of all mock reports generated in the last 90 days. Returns null when fewer than
    /// <see cref="CohortPrivacyMinimum"/> peer reports exist to prevent re-identification; returns
    /// a banded percentile (rounded to the nearest 5) rather than a precise rank.
    /// </summary>
    private async Task<object?> BuildCohortPercentileSignalAsync(
        string userId,
        Dictionary<string, object?>? latestReport,
        CancellationToken ct)
    {
        if (latestReport is null) return null;
        if (!latestReport.TryGetValue("overallScore", out var scoreRaw) || scoreRaw is null) return null;
        if (!int.TryParse(scoreRaw.ToString(), out var learnerScore)) return null;

        var since = DateTimeOffset.UtcNow.AddDays(-90);

        // Pull the scored payloads and extract the overall score on the CLR side; reports store
        // JSON so we cannot LINQ-translate the score extraction. We exclude the learner's own
        // reports so the learner is ranked against peers only.
        // MockReport has no UserId column — we join via MockAttempt to scope the cohort to peers
        // (learners other than the current one) whose reports landed in the retention window.
        var peerAttemptIds = db.MockAttempts.AsNoTracking()
            .Where(a => a.UserId != userId)
            .Select(a => a.Id);

        var peerPayloads = await db.MockReports.AsNoTracking()
            .Where(x => x.State == AsyncState.Completed
                && x.GeneratedAt != null
                && x.GeneratedAt >= since
                && peerAttemptIds.Contains(x.MockAttemptId))
            .OrderByDescending(x => x.GeneratedAt)
            .Select(x => x.PayloadJson)
            .Take(1000)
            .ToListAsync(ct);

        var peerScores = new List<int>(peerPayloads.Count);
        foreach (var payload in peerPayloads)
        {
            var dict = JsonSupport.Deserialize<Dictionary<string, object?>>(payload, new Dictionary<string, object?>());
            if (dict.TryGetValue("overallScore", out var peerRaw)
                && peerRaw is not null
                && int.TryParse(peerRaw.ToString(), out var peerScore))
            {
                peerScores.Add(peerScore);
            }
        }

        if (peerScores.Count < CohortPrivacyMinimum) return null;

        // Percentile = share of peers at or below the learner's score (standard convention).
        var atOrBelow = peerScores.Count(s => s <= learnerScore);
        var rawPercentile = (int)Math.Round(atOrBelow * 100.0 / peerScores.Count);
        // Band to the nearest 5 to further blunt re-identification risk.
        var bandedPercentile = Math.Clamp((int)Math.Round(rawPercentile / 5.0) * 5, 5, 95);

        string label = bandedPercentile switch
        {
            >= 90 => "Top 10% of recent mocks",
            >= 75 => "Top 25% of recent mocks",
            >= 50 => "Above the recent cohort median",
            >= 25 => "Below the recent cohort median",
            _ => "Focus on fundamentals to climb the cohort"
        };

        return new
        {
            percentile = bandedPercentile,
            cohortSize = peerScores.Count,
            windowDays = 90,
            learnerScore,
            label
        };
    }
}
