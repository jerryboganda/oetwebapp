using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public sealed class MockService(LearnerDbContext db)
{
    private static readonly string[] FullMockOrder = ["listening", "reading", "writing", "speaking"];
    private static readonly HashSet<string> ProductiveSubtests = new(["writing", "speaking"], StringComparer.OrdinalIgnoreCase);

    // Privacy floor for anonymised cohort percentile signal (Phase C2). Below this threshold
    // a learner could infer a specific peer's score, so the API returns no percentile at all.
    private const int CohortPrivacyMinimum = 10;

    public async Task<object> GetMocksAsync(string userId, CancellationToken ct)
    {
        // Business rule: the Mock Center is a READ surface, not a mutation boundary, so it must
        // degrade gracefully for learners whose Identity account is valid but whose
        // `Users` profile row has not yet been bootstrapped. Throwing 404 here would turn
        // first-visit traffic into a dead page ("Failed to load mock center"). Instead we
        // render the canonical empty shape and point the learner at the dashboard bootstrap.
        var userExists = await db.Users.AsNoTracking().AnyAsync(x => x.Id == userId, ct);
        if (!userExists)
        {
            return new
            {
                reports = Array.Empty<object>(),
                learnerProfession = (string?)null,
                availableProfessions = Array.Empty<object>(),
                resumableAttempts = Array.Empty<object>(),
                recommendedNextMock = new
                {
                    id = "mock-center-bootstrap",
                    title = "Finish setting up your learner profile",
                    rationale = "Complete your dashboard bootstrap so we can tailor mocks to your profession and readiness.",
                    route = "/dashboard",
                    latestOverallScore = (string?)null,
                    latestOverallGrade = (string?)null,
                    trend = (string?)null,
                    readiness = (object?)null
                },
                purchasedMockReviews = new
                {
                    availableCredits = 0,
                    reservedCredits = 0,
                    consumedCredits = 0,
                    pendingReviews = 0,
                    completedReviews = 0,
                    reviewTurnaroundHours = 48,
                    reviewSlaLabel = "Expert review turnaround: within 48 hours"
                },
                collections = new
                {
                    fullMocks = Array.Empty<object>(),
                    subTestMocks = Array.Empty<object>()
                },
                emptyState = new
                {
                    title = "Your learner profile is not fully initialised yet",
                    description = "Open your dashboard once to finish setup, then come back here to pick a mock.",
                    route = "/dashboard"
                },
                scoreGuarantee = (object?)null,
                cohortPercentile = (object?)null
            };
        }

        var wallet = await EnsureWalletAsync(userId, ct);
        var learnerProfession = await db.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => x.ActiveProfessionId)
            .FirstOrDefaultAsync(ct);
        var bundles = await QueryPublishedBundles()
            .Include(x => x.Sections.OrderBy(s => s.SectionOrder))
                .ThenInclude(s => s.ContentPaper)
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Title)
            .ToListAsync(ct);

        var attempts = await db.MockAttempts.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.StartedAt)
            .Take(12)
            .ToListAsync(ct);

        var attemptIds = attempts.Select(x => x.Id).ToArray();

        // Pre-fetch section attempts so ProjectBundleCard can surface per-sub-test progress dots
        // on Full Mocks without N+1 queries per bundle.
        var sectionAttempts = attemptIds.Length == 0
            ? new List<MockSectionAttempt>()
            : await db.MockSectionAttempts.AsNoTracking()
                .Where(x => attemptIds.Contains(x.MockAttemptId))
                .ToListAsync(ct);
        var sectionAttemptsByAttempt = sectionAttempts
            .GroupBy(x => x.MockAttemptId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var reports = await db.MockReports.AsNoTracking()
            .Where(report => attemptIds.Contains(report.MockAttemptId))
            .OrderByDescending(report => report.GeneratedAt)
            .Take(6)
            .ToListAsync(ct);

        var reportItems = reports
            .Select(report =>
            {
                var payload = JsonSupport.Deserialize<Dictionary<string, object?>>(report.PayloadJson, new Dictionary<string, object?>());
                payload["id"] = report.Id;
                payload["reportId"] = report.Id;
                payload["state"] = ToAsyncState(report.State);
                payload["generatedAt"] = report.GeneratedAt;
                return payload;
            })
            .ToList();
        var latestReport = reportItems.FirstOrDefault();
        var activeReservations = await db.MockReviewReservations.AsNoTracking()
            .Where(x => x.UserId == userId && (x.State == MockReviewReservationState.Reserved || x.State == MockReviewReservationState.PartiallyConsumed))
            .ToListAsync(ct);

        var reviewAttempts = await db.Attempts.AsNoTracking()
            .Where(x => x.UserId == userId && (x.SubtestCode == "writing" || x.SubtestCode == "speaking"))
            .Select(x => x.Id)
            .ToListAsync(ct);
        var reviewStates = reviewAttempts.Count == 0
            ? []
            : await db.ReviewRequests.AsNoTracking()
                .Where(x => reviewAttempts.Contains(x.AttemptId))
                .Select(x => x.State)
                .ToListAsync(ct);

        var firstFullBundle = bundles.FirstOrDefault(x => x.MockType == "full");
        var firstFullRoute = firstFullBundle is null
            ? "/mocks/setup"
            : $"/mocks/setup?bundleId={Uri.EscapeDataString(firstFullBundle.Id)}&type=full";

        var fullMocks = bundles
            .Where(x => x.MockType == "full")
            .Select(bundle =>
            {
                var attempt = attempts.FirstOrDefault(a => a.MockBundleId == bundle.Id);
                var sections = attempt is not null && sectionAttemptsByAttempt.TryGetValue(attempt.Id, out var list)
                    ? list
                    : new List<MockSectionAttempt>();
                return ProjectBundleCard(bundle, attempt, latestReport, sections);
            })
            .ToArray();

        var subTestMocks = bundles
            .Where(x => x.MockType == "sub")
            .Select(bundle =>
            {
                var attempt = attempts.FirstOrDefault(a => a.MockBundleId == bundle.Id);
                var sections = attempt is not null && sectionAttemptsByAttempt.TryGetValue(attempt.Id, out var list)
                    ? list
                    : new List<MockSectionAttempt>();
                return ProjectBundleCard(bundle, attempt, latestReport, sections);
            })
            .ToArray();

        // Available profession filters are derived from the union of professions actually represented
        // in published bundles (plus the "all" sentinel). Presenting profession chips that have zero
        // bundles would be misleading.
        var bundleProfessionIds = bundles
            .Where(x => !x.AppliesToAllProfessions && !string.IsNullOrWhiteSpace(x.ProfessionId))
            .Select(x => x.ProfessionId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var professionRows = bundleProfessionIds.Length == 0
            ? new List<ProfessionReference>()
            : await db.Professions.AsNoTracking()
                .Where(x => bundleProfessionIds.Contains(x.Id))
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Label)
                .ToListAsync(ct);

        return new
        {
            reports = reportItems,
            learnerProfession,
            availableProfessions = professionRows
                .Select(x => new { id = x.Id, label = x.Label })
                .ToArray(),
            resumableAttempts = attempts
                .Where(x => x.State is AttemptState.InProgress or AttemptState.Paused or AttemptState.Evaluating)
                .Select(ProjectAttemptSummary)
                .ToArray(),
            recommendedNextMock = new
            {
                id = latestReport?.GetValueOrDefault("id")?.ToString() ?? firstFullBundle?.Id ?? "mock-center-empty",
                title = latestReport is null ? "Start a full OET mock" : "Review the next full mock",
                rationale = latestReport is null
                    ? "Choose a published bundle to capture a clean baseline across OET sections."
                    : $"Your latest report scored {latestReport.GetValueOrDefault("overallScore")?.ToString() ?? "an updated"} overall. Run another mock to confirm the gains.",
                route = firstFullRoute,
                latestOverallScore = latestReport?.GetValueOrDefault("overallScore")?.ToString(),
                latestOverallGrade = latestReport?.GetValueOrDefault("overallGrade")?.ToString(),
                trend = ExtractReportTrend(latestReport),
                readiness = BuildReadinessAdvisory(latestReport)
            },
            purchasedMockReviews = new
            {
                availableCredits = wallet.CreditBalance,
                reservedCredits = activeReservations.Sum(x => Math.Max(0, x.ReservedCredits - x.ConsumedCredits - x.ReleasedCredits)),
                consumedCredits = await db.MockReviewReservations.AsNoTracking().Where(x => x.UserId == userId).SumAsync(x => x.ConsumedCredits, ct),
                pendingReviews = reviewStates.Count(x => x is ReviewRequestState.Queued or ReviewRequestState.InReview or ReviewRequestState.AwaitingPayment),
                completedReviews = reviewStates.Count(x => x == ReviewRequestState.Completed),
                // Standard expert review SLA surfaced so learners know turnaround up-front (OET business req):
                // writing + speaking expert reviews are committed to 48h under current operations policy.
                reviewTurnaroundHours = 48,
                reviewSlaLabel = "Expert review turnaround: within 48 hours"
            },
            collections = new
            {
                fullMocks,
                subTestMocks
            },
            emptyState = bundles.Count == 0
                ? new
                {
                    title = "No mock bundles are published yet",
                    description = "Ask an admin to publish a full or sub-test mock bundle from the content mock bundle console.",
                    route = "/admin/content/mocks"
                }
                : null,
            // Phase C1: surface existing billing-module pledge so learners can see whether their
            // Score Guarantee is on track from the Mock Center. Read-only signal; refund + claim
            // flows remain owned by the billing module.
            scoreGuarantee = await BuildScoreGuaranteeSignalAsync(userId, latestReport, ct),
            // Phase C2: anonymised cohort percentile. Returns null when the cohort is too small
            // (< CohortPrivacyMinimum) to prevent re-identification, or when the learner has no
            // scored report yet.
            cohortPercentile = await BuildCohortPercentileSignalAsync(userId, latestReport, ct)
        };
    }

    public async Task<object> GetMockOptionsAsync(string userId, CancellationToken ct)
    {
        await EnsureUserAsync(userId, ct);
        var wallet = await EnsureWalletAsync(userId, ct);
        var bundles = await QueryPublishedBundles()
            .Include(x => x.Sections.OrderBy(s => s.SectionOrder))
                .ThenInclude(s => s.ContentPaper)
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Title)
            .ToListAsync(ct);

        var professions = await db.Professions.AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Label)
            .Select(x => new { id = x.Id, label = x.Label })
            .ToListAsync(ct);

        if (professions.Count == 0)
        {
            professions.AddRange([
                new { id = "medicine", label = "Medicine" },
                new { id = "nursing", label = "Nursing" },
                new { id = "pharmacy", label = "Pharmacy" },
                new { id = "dentistry", label = "Dentistry" }
            ]);
        }

        return new
        {
            mockTypes = new[]
            {
                new { id = "full", label = "Full Mock", description = "All four sub-tests in OET order." },
                new { id = "sub", label = "Single Sub-test", description = "Focus on one published sub-test bundle." }
            },
            subTypes = FullMockOrder.Select(x => new { id = x, label = ToDisplaySubtest(x) }),
            modes = new[]
            {
                new { id = "exam", label = "Exam" },
                new { id = "practice", label = "Practice" }
            },
            professions,
            reviewSelections = new[]
            {
                new { id = "none", label = "No Review", cost = 0 },
                new { id = "writing", label = "Writing Only", cost = 1 },
                new { id = "speaking", label = "Speaking Only", cost = 1 },
                new { id = "writing_and_speaking", label = "Writing + Speaking", cost = 2 },
                new { id = "current_subtest", label = "Current Sub-test", cost = 1 }
            },
            wallet = new { availableCredits = wallet.CreditBalance },
            availableBundles = bundles.Select(ProjectBundleOption).ToArray()
        };
    }

    public async Task<object> CreateMockAttemptAsync(string userId, MockAttemptCreateRequest request, CancellationToken ct)
    {
        await EnsureUserAsync(userId, ct);
        var now = DateTimeOffset.UtcNow;
        var mockType = NormalizeMockType(request.MockType);
        var subType = mockType == "sub" ? NormalizeSubtest(request.SubType) : null;
        var profession = NormalizeProfession(request.Profession);
        var reviewSelection = NormalizeMockReviewSelection(mockType, subType, request.IncludeReview, request.ReviewSelection);
        var reviewCost = ReviewCost(reviewSelection, mockType, subType);

        var bundle = await ResolvePublishedBundleAsync(request.BundleId, mockType, subType, profession, ct);
        var sectionDefinitions = bundle.Sections.OrderBy(x => x.SectionOrder).ToList();
        if (sectionDefinitions.Count == 0)
        {
            throw ApiException.Validation(
                "mock_bundle_empty",
                "This mock bundle has no published sections.",
                [new ApiFieldError("bundleId", "empty", "Choose a bundle with at least one section.")]);
        }

        var wallet = await EnsureWalletAsync(userId, ct);
        if (reviewCost > 0 && wallet.CreditBalance < reviewCost)
        {
            throw ApiException.PaymentRequired(
                "insufficient_review_credits",
                "You do not have enough review credits to reserve the selected expert review.");
        }

        var id = $"mock-attempt-{Guid.NewGuid():N}";
        var config = new
        {
            mockType,
            subType,
            mode = NormalizeMode(request.Mode),
            profession,
            includeReview = reviewCost > 0,
            strictTimer = request.StrictTimer,
            reviewSelection,
            bundleId = bundle.Id,
            bundleTitle = bundle.Title,
            targetCountry = request.TargetCountry
        };

        var attempt = new MockAttempt
        {
            Id = id,
            UserId = userId,
            MockBundleId = bundle.Id,
            MockType = mockType,
            SubtestCode = mockType == "sub" ? subType : null,
            Mode = config.mode,
            Profession = profession,
            ReviewSelection = reviewSelection,
            StrictTimer = request.StrictTimer,
            ReservedReviewCredits = reviewCost,
            ConfigJson = JsonSupport.Serialize(config),
            State = AttemptState.InProgress,
            StartedAt = now,
            ExamFamilyCode = bundle.ExamFamilyCode,
            ExamTypeCode = bundle.ExamTypeCode
        };

        db.MockAttempts.Add(attempt);

        foreach (var section in sectionDefinitions)
        {
            var sectionAttempt = new MockSectionAttempt
            {
                Id = $"mock-section-{Guid.NewGuid():N}",
                MockAttemptId = attempt.Id,
                MockBundleSectionId = section.Id,
                SubtestCode = section.SubtestCode,
                ContentPaperId = section.ContentPaperId,
                State = AttemptState.NotStarted,
                LaunchRoute = BuildLaunchRoute(attempt.Id, section, null)
            };
            sectionAttempt.LaunchRoute = BuildLaunchRoute(attempt.Id, section, sectionAttempt.Id);
            db.MockSectionAttempts.Add(sectionAttempt);
        }

        if (reviewCost > 0)
        {
            wallet.CreditBalance -= reviewCost;
            wallet.LastUpdatedAt = now;
            var txId = Guid.NewGuid();
            db.WalletTransactions.Add(new WalletTransaction
            {
                Id = txId,
                WalletId = wallet.Id,
                TransactionType = "mock_review_reservation",
                Amount = -reviewCost,
                BalanceAfter = wallet.CreditBalance,
                ReferenceType = "mock",
                ReferenceId = attempt.Id,
                Description = $"Reserved {reviewCost} expert review credit(s) for {bundle.Title}.",
                CreatedBy = userId,
                CreatedAt = now
            });
            db.MockReviewReservations.Add(new MockReviewReservation
            {
                Id = $"mock-reservation-{Guid.NewGuid():N}",
                UserId = userId,
                MockAttemptId = attempt.Id,
                WalletId = wallet.Id,
                State = MockReviewReservationState.Reserved,
                ReservedCredits = reviewCost,
                Selection = reviewSelection,
                ReservedAt = now,
                ExpiresAt = now.AddDays(7),
                DebitTransactionId = txId
            });
        }

        RecordEvent(userId, "mock_started", new { mockAttemptId = attempt.Id, bundleId = bundle.Id, mockType, subType, mode = config.mode, reviewSelection });
        await db.SaveChangesAsync(ct);
        return await GetMockAttemptAsync(userId, attempt.Id, ct);
    }

    public async Task<object> GetMockAttemptAsync(string userId, string mockAttemptId, CancellationToken ct)
    {
        var attempt = await GetMockAttemptOwnedByUserAsync(userId, mockAttemptId, ct);
        var sections = await db.MockSectionAttempts.AsNoTracking()
            .Where(x => x.MockAttemptId == attempt.Id)
            .Join(db.MockBundleSections.AsNoTracking().Include(x => x.ContentPaper),
                sectionAttempt => sectionAttempt.MockBundleSectionId,
                bundleSection => bundleSection.Id,
                (sectionAttempt, bundleSection) => new { sectionAttempt, bundleSection })
            .OrderBy(x => x.bundleSection.SectionOrder)
            .ToListAsync(ct);

        var reservation = await db.MockReviewReservations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.MockAttemptId == attempt.Id, ct);
        var config = JsonSupport.Deserialize<Dictionary<string, object?>>(attempt.ConfigJson, new Dictionary<string, object?>());

        return new
        {
            mockAttemptId = attempt.Id,
            state = ToApiState(attempt.State),
            startedAt = attempt.StartedAt,
            submittedAt = attempt.SubmittedAt,
            completedAt = attempt.CompletedAt,
            config,
            sectionStates = sections.Select(x => ProjectSectionAttempt(x.sectionAttempt, x.bundleSection, attempt)).ToArray(),
            reviewReservation = reservation is null ? null : ProjectReservation(reservation),
            resumeRoute = $"/mocks/player/{attempt.Id}",
            reportRoute = attempt.ReportId is null ? null : $"/mocks/report/{attempt.ReportId}",
            reportId = attempt.ReportId
        };
    }

    public async Task<object> StartMockSectionAsync(string userId, string mockAttemptId, string sectionId, MockSectionStartRequest request, CancellationToken ct)
    {
        var attempt = await GetMockAttemptOwnedByUserAsync(userId, mockAttemptId, ct);
        if (attempt.State is AttemptState.Completed or AttemptState.Abandoned)
        {
            throw ApiException.Conflict("mock_attempt_closed", "This mock attempt is already closed.");
        }

        var section = await db.MockSectionAttempts
            .FirstOrDefaultAsync(x => x.Id == sectionId && x.MockAttemptId == attempt.Id, ct)
            ?? throw ApiException.NotFound("mock_section_not_found", "Mock section not found.");
        var bundleSection = await db.MockBundleSections.AsNoTracking()
            .Include(x => x.ContentPaper)
            .FirstAsync(x => x.Id == section.MockBundleSectionId, ct);

        var now = DateTimeOffset.UtcNow;
        if (section.State == AttemptState.NotStarted)
        {
            section.State = AttemptState.InProgress;
            section.StartedAt = now;
            section.DeadlineAt = attempt.StrictTimer ? now.AddMinutes(bundleSection.TimeLimitMinutes) : null;
            RecordEvent(userId, "mock_section_started", new { mockAttemptId = attempt.Id, sectionId = section.Id, subtest = section.SubtestCode });
            await db.SaveChangesAsync(ct);
        }

        section.LaunchRoute = BuildLaunchRoute(attempt.Id, bundleSection, section.Id);
        return ProjectSectionAttempt(section, bundleSection, attempt);
    }

    public async Task<object> CompleteMockSectionAsync(string userId, string mockAttemptId, string sectionId, MockSectionCompleteRequest request, CancellationToken ct)
    {
        var attempt = await GetMockAttemptOwnedByUserAsync(userId, mockAttemptId, ct);
        if (attempt.State is AttemptState.Completed or AttemptState.Abandoned)
        {
            throw ApiException.Conflict("mock_attempt_closed", "This mock attempt is already closed.");
        }

        var section = await db.MockSectionAttempts
            .FirstOrDefaultAsync(x => x.Id == sectionId && x.MockAttemptId == attempt.Id, ct)
            ?? throw ApiException.NotFound("mock_section_not_found", "Mock section not found.");
        var bundleSection = await db.MockBundleSections.AsNoTracking()
            .Include(x => x.ContentPaper)
            .FirstAsync(x => x.Id == section.MockBundleSectionId, ct);

        var now = DateTimeOffset.UtcNow;
        section.State = AttemptState.Completed;
        section.SubmittedAt ??= now;
        section.CompletedAt = now;
        section.ContentAttemptId = string.IsNullOrWhiteSpace(request.ContentAttemptId) ? section.ContentAttemptId : request.ContentAttemptId;
        section.RawScore = request.RawScore ?? section.RawScore;
        section.RawScoreMax = request.RawScoreMax ?? section.RawScoreMax;
        section.ScaledScore = ResolveScaledScore(section.SubtestCode, request.RawScore, request.ScaledScore);
        section.Grade = string.IsNullOrWhiteSpace(request.Grade)
            ? section.ScaledScore is null ? section.Grade : OetScoring.OetGradeLetterFromScaled(section.ScaledScore.Value)
            : request.Grade;
        section.FeedbackJson = JsonSupport.Serialize(request.Evidence ?? new Dictionary<string, object?>());

        await ConsumeReservationForSectionAsync(userId, attempt, section, request.ReviewTurnaroundOption, now, ct);
        RecordEvent(userId, "mock_section_completed", new { mockAttemptId = attempt.Id, sectionId = section.Id, subtest = section.SubtestCode, section.ScaledScore });
        await db.SaveChangesAsync(ct);
        return ProjectSectionAttempt(section, bundleSection, attempt);
    }

    public async Task<object> SubmitMockAttemptAsync(string userId, string mockAttemptId, CancellationToken ct)
    {
        var attempt = await GetMockAttemptOwnedByUserAsync(userId, mockAttemptId, ct);
        if (attempt.State == AttemptState.Completed && attempt.ReportId is not null)
        {
            return new { mockAttemptId = attempt.Id, state = "completed", reportId = attempt.ReportId, reportRoute = $"/mocks/report/{attempt.ReportId}" };
        }

        var completedCount = await db.MockSectionAttempts.AsNoTracking()
            .CountAsync(x => x.MockAttemptId == attempt.Id && x.State == AttemptState.Completed, ct);
        if (completedCount == 0)
        {
            throw ApiException.Validation(
                "mock_no_completed_sections",
                "Complete at least one section before submitting the mock.",
                [new ApiFieldError("mockAttemptId", "no_completed_sections", "Start and complete a section first.")]);
        }

        var report = await db.MockReports.FirstOrDefaultAsync(x => x.MockAttemptId == attempt.Id, ct);
        if (report is null)
        {
            report = new MockReport
            {
                Id = $"mock-report-{Guid.NewGuid():N}",
                MockAttemptId = attempt.Id,
                State = AsyncState.Queued,
                PayloadJson = "{}"
            };
            db.MockReports.Add(report);
        }
        else
        {
            report.State = AsyncState.Queued;
        }

        attempt.ReportId = report.Id;
        attempt.State = AttemptState.Evaluating;
        attempt.SubmittedAt = DateTimeOffset.UtcNow;
        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"job-{Guid.NewGuid():N}",
            Type = JobType.MockReportGeneration,
            State = AsyncState.Queued,
            ResourceId = attempt.Id,
            PayloadJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            AvailableAt = DateTimeOffset.UtcNow,
            LastTransitionAt = DateTimeOffset.UtcNow,
            StatusReasonCode = "queued",
            StatusMessage = "Mock report generation queued."
        });
        await db.SaveChangesAsync(ct);
        return new { mockAttemptId = attempt.Id, state = "queued", reportId = report.Id, reportRoute = $"/mocks/report/{report.Id}", nextPollAfterMs = 2000 };
    }

    public async Task<object> CancelMockAttemptAsync(string userId, string mockAttemptId, CancellationToken ct)
    {
        var attempt = await GetMockAttemptOwnedByUserAsync(userId, mockAttemptId, ct);
        if (attempt.State is AttemptState.Completed or AttemptState.Abandoned)
        {
            return await GetMockAttemptAsync(userId, attempt.Id, ct);
        }

        attempt.State = AttemptState.Abandoned;
        attempt.CompletedAt = DateTimeOffset.UtcNow;
        await ReleaseReservationAsync(userId, attempt.Id, "Mock attempt cancelled before review consumption.", ct);
        RecordEvent(userId, "mock_cancelled", new { mockAttemptId = attempt.Id });
        await db.SaveChangesAsync(ct);
        return await GetMockAttemptAsync(userId, attempt.Id, ct);
    }

    public async Task<object> GetMockReportAsync(string userId, string reportId, CancellationToken ct)
    {
        var report = await db.MockReports.AsNoTracking()
            .Join(db.MockAttempts.AsNoTracking().Where(x => x.UserId == userId),
                report => report.MockAttemptId,
                attempt => attempt.Id,
                (report, attempt) => report)
            .FirstOrDefaultAsync(x => x.Id == reportId, ct)
            ?? throw ApiException.NotFound("mock_report_not_found", "Mock report not found.");

        var payload = JsonSupport.Deserialize<Dictionary<string, object?>>(report.PayloadJson, new Dictionary<string, object?>());
        payload["id"] = report.Id;
        payload["reportId"] = report.Id;
        payload["state"] = ToAsyncState(report.State);
        payload["generatedAt"] = report.GeneratedAt;
        payload["studyPlanUpdateCta"] = new { label = "Update study plan", route = "/study-plan" };
        return payload;
    }

    public async Task<object> ListBundlesAsync(string? status, string? mockType, string? subtest, CancellationToken ct)
    {
        var query = db.MockBundles.AsNoTracking()
            .Include(x => x.Sections.OrderBy(s => s.SectionOrder))
                .ThenInclude(s => s.ContentPaper)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ContentStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(x => x.Status == parsedStatus);
        }
        if (!string.IsNullOrWhiteSpace(mockType))
        {
            var normalizedType = NormalizeMockType(mockType);
            query = query.Where(x => x.MockType == normalizedType);
        }
        if (!string.IsNullOrWhiteSpace(subtest))
        {
            var normalizedSubtest = NormalizeSubtest(subtest);
            query = query.Where(x => x.SubtestCode == normalizedSubtest || x.Sections.Any(s => s.SubtestCode == normalizedSubtest));
        }

        var rows = await query
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.Title)
            .ToListAsync(ct);

        return new { items = rows.Select(ProjectBundleAdmin).ToArray() };
    }

    public async Task<object> GetBundleAsync(string id, CancellationToken ct)
    {
        var bundle = await GetBundleEntityAsync(id, track: false, ct);
        return ProjectBundleAdmin(bundle);
    }

    public async Task<object> CreateBundleAsync(AdminMockBundleCreateRequest request, string adminId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var mockType = NormalizeMockType(request.MockType);
        var subtest = mockType == "sub" ? NormalizeSubtest(request.SubtestCode) : null;
        var title = RequireText(request.Title, "title");
        var bundle = new MockBundle
        {
            Id = $"mock-bundle-{Guid.NewGuid():N}",
            Title = title,
            Slug = await UniqueSlugAsync(title, ct),
            MockType = mockType,
            SubtestCode = subtest,
            ProfessionId = string.IsNullOrWhiteSpace(request.ProfessionId) ? null : request.ProfessionId.Trim().ToLowerInvariant(),
            AppliesToAllProfessions = request.AppliesToAllProfessions,
            Status = ContentStatus.Draft,
            SourceProvenance = request.SourceProvenance,
            Priority = request.Priority ?? 0,
            TagsCsv = request.TagsCsv ?? string.Empty,
            EstimatedDurationMinutes = mockType == "full" ? FullMockOrder.Sum(DefaultTimeLimit) : DefaultTimeLimit(subtest ?? "reading"),
            CreatedByAdminId = adminId,
            UpdatedByAdminId = adminId,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.MockBundles.Add(bundle);
        LogAudit(adminId, "Created", "MockBundle", bundle.Id, $"Created mock bundle {bundle.Title}.");
        await db.SaveChangesAsync(ct);
        return ProjectBundleAdmin(bundle);
    }

    public async Task<object> UpdateBundleAsync(string id, AdminMockBundleUpdateRequest request, string adminId, CancellationToken ct)
    {
        var bundle = await GetBundleEntityAsync(id, track: true, ct);
        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            bundle.Title = request.Title.Trim();
        }
        if (!string.IsNullOrWhiteSpace(request.MockType))
        {
            bundle.MockType = NormalizeMockType(request.MockType);
        }
        if (request.SubtestCode is not null)
        {
            bundle.SubtestCode = bundle.MockType == "sub" ? NormalizeSubtest(request.SubtestCode) : null;
        }
        if (request.ProfessionId is not null)
        {
            bundle.ProfessionId = string.IsNullOrWhiteSpace(request.ProfessionId) ? null : request.ProfessionId.Trim().ToLowerInvariant();
        }
        if (request.AppliesToAllProfessions.HasValue)
        {
            bundle.AppliesToAllProfessions = request.AppliesToAllProfessions.Value;
        }
        if (request.SourceProvenance is not null) bundle.SourceProvenance = request.SourceProvenance;
        if (request.Priority.HasValue) bundle.Priority = request.Priority.Value;
        if (request.TagsCsv is not null) bundle.TagsCsv = request.TagsCsv;
        if (request.Status.HasValue && request.Status.Value != ContentStatus.Published)
        {
            bundle.Status = request.Status.Value;
        }
        bundle.UpdatedAt = DateTimeOffset.UtcNow;
        bundle.UpdatedByAdminId = adminId;
        LogAudit(adminId, "Updated", "MockBundle", bundle.Id, $"Updated mock bundle {bundle.Title}.");
        await db.SaveChangesAsync(ct);
        return await GetBundleAsync(bundle.Id, ct);
    }

    public async Task<object> ArchiveBundleAsync(string id, string adminId, CancellationToken ct)
    {
        var bundle = await GetBundleEntityAsync(id, track: true, ct);
        bundle.Status = ContentStatus.Archived;
        bundle.ArchivedAt = DateTimeOffset.UtcNow;
        bundle.UpdatedAt = DateTimeOffset.UtcNow;
        bundle.UpdatedByAdminId = adminId;
        LogAudit(adminId, "Archived", "MockBundle", bundle.Id, $"Archived mock bundle {bundle.Title}.");
        await db.SaveChangesAsync(ct);
        return new { id = bundle.Id, status = bundle.Status.ToString().ToLowerInvariant() };
    }

    public async Task<object> AddSectionAsync(string id, AdminMockBundleSectionRequest request, string adminId, CancellationToken ct)
    {
        var bundle = await GetBundleEntityAsync(id, track: true, ct);
        var paper = await db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.ContentPaperId, ct)
            ?? throw ApiException.NotFound("content_paper_not_found", "Content paper not found.");

        var subtest = NormalizeSubtest(paper.SubtestCode);
        if (bundle.MockType == "sub" && !string.Equals(bundle.SubtestCode, subtest, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "mock_section_subtest_mismatch",
                "A sub-test mock can only include sections from its selected sub-test.",
                [new ApiFieldError("contentPaperId", "subtest_mismatch", "Choose a paper from the same sub-test as the bundle.")]);
        }

        var nextOrder = request.SectionOrder ?? (await db.MockBundleSections.Where(x => x.MockBundleId == id).MaxAsync(x => (int?)x.SectionOrder, ct) ?? 0) + 1;
        var section = new MockBundleSection
        {
            Id = $"mock-bundle-section-{Guid.NewGuid():N}",
            MockBundleId = bundle.Id,
            SectionOrder = nextOrder,
            SubtestCode = subtest,
            ContentPaperId = paper.Id,
            TimeLimitMinutes = request.TimeLimitMinutes ?? DefaultTimeLimit(subtest),
            ReviewEligible = request.ReviewEligible ?? ProductiveSubtests.Contains(subtest),
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.MockBundleSections.Add(section);
        bundle.EstimatedDurationMinutes = await ComputeEstimatedDurationAsync(bundle.Id, section, ct);
        bundle.UpdatedAt = DateTimeOffset.UtcNow;
        bundle.UpdatedByAdminId = adminId;
        LogAudit(adminId, "AddedSection", "MockBundle", bundle.Id, $"Added {paper.Title} to {bundle.Title}.");
        await db.SaveChangesAsync(ct);
        return await GetBundleAsync(bundle.Id, ct);
    }

    public async Task<object> ReorderSectionsAsync(string id, AdminMockBundleReorderRequest request, string adminId, CancellationToken ct)
    {
        var bundle = await GetBundleEntityAsync(id, track: true, ct);
        var sections = await db.MockBundleSections.Where(x => x.MockBundleId == bundle.Id).ToListAsync(ct);
        var requested = request.SectionIds?.ToList() ?? [];
        if (requested.Count != sections.Count || requested.Except(sections.Select(x => x.Id)).Any())
        {
            throw ApiException.Validation(
                "mock_reorder_invalid",
                "The reorder request must include every section exactly once.",
                [new ApiFieldError("sectionIds", "invalid", "Submit all section ids in the desired order.")]);
        }

        for (var index = 0; index < requested.Count; index++)
        {
            sections.First(x => x.Id == requested[index]).SectionOrder = index + 1;
        }

        bundle.UpdatedAt = DateTimeOffset.UtcNow;
        bundle.UpdatedByAdminId = adminId;
        LogAudit(adminId, "ReorderedSections", "MockBundle", bundle.Id, $"Reordered sections for {bundle.Title}.");
        await db.SaveChangesAsync(ct);
        return await GetBundleAsync(bundle.Id, ct);
    }

    public async Task<object> PublishBundleAsync(string id, string adminId, CancellationToken ct)
    {
        var bundle = await GetBundleEntityAsync(id, track: true, ct);
        var sections = await db.MockBundleSections
            .Where(x => x.MockBundleId == bundle.Id)
            .Include(x => x.ContentPaper)
            .OrderBy(x => x.SectionOrder)
            .ToListAsync(ct);

        var errors = ValidatePublishGate(bundle, sections);
        if (errors.Count > 0)
        {
            throw ApiException.Validation("mock_publish_gate_failed", "The mock bundle is not ready to publish.", errors);
        }

        bundle.Status = ContentStatus.Published;
        bundle.PublishedAt = DateTimeOffset.UtcNow;
        bundle.UpdatedAt = DateTimeOffset.UtcNow;
        bundle.UpdatedByAdminId = adminId;
        bundle.EstimatedDurationMinutes = sections.Sum(x => x.TimeLimitMinutes);
        LogAudit(adminId, "Published", "MockBundle", bundle.Id, $"Published mock bundle {bundle.Title}.");
        await db.SaveChangesAsync(ct);
        return await GetBundleAsync(bundle.Id, ct);
    }

    private IQueryable<MockBundle> QueryPublishedBundles()
        => db.MockBundles.AsNoTracking().Where(x => x.Status == ContentStatus.Published);

    private async Task<MockBundle> ResolvePublishedBundleAsync(string? bundleId, string mockType, string? subtest, string profession, CancellationToken ct)
    {
        var query = db.MockBundles
            .Include(x => x.Sections.OrderBy(s => s.SectionOrder))
                .ThenInclude(s => s.ContentPaper)
            .Where(x => x.Status == ContentStatus.Published && x.MockType == mockType);

        if (!string.IsNullOrWhiteSpace(bundleId))
        {
            query = query.Where(x => x.Id == bundleId);
        }
        else if (mockType == "sub")
        {
            query = query.Where(x => x.SubtestCode == subtest);
        }

        var candidates = await query
            .OrderByDescending(x => x.AppliesToAllProfessions || x.ProfessionId == profession)
            .ThenByDescending(x => x.Priority)
            .ThenBy(x => x.Title)
            .ToListAsync(ct);

        var bundle = candidates.FirstOrDefault(x => x.AppliesToAllProfessions || x.ProfessionId == null || x.ProfessionId == profession)
            ?? candidates.FirstOrDefault();

        return bundle ?? throw ApiException.NotFound(
            "mock_bundle_not_found",
            mockType == "sub"
                ? $"No published {ToDisplaySubtest(subtest ?? "reading")} mock bundle is available yet."
                : "No published full mock bundle is available yet.");
    }

    private async Task<MockAttempt> GetMockAttemptOwnedByUserAsync(string userId, string mockAttemptId, CancellationToken ct)
        => await db.MockAttempts.FirstOrDefaultAsync(x => x.Id == mockAttemptId && x.UserId == userId, ct)
            ?? throw ApiException.NotFound("mock_attempt_not_found", "Mock attempt not found.");

    private async Task<MockBundle> GetBundleEntityAsync(string id, bool track, CancellationToken ct)
    {
        var query = track ? db.MockBundles : db.MockBundles.AsNoTracking();
        return await query.Include(x => x.Sections.OrderBy(s => s.SectionOrder)).ThenInclude(s => s.ContentPaper)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw ApiException.NotFound("mock_bundle_not_found", "Mock bundle not found.");
    }

    private async Task EnsureUserAsync(string userId, CancellationToken ct)
    {
        var exists = await db.Users.AsNoTracking().AnyAsync(x => x.Id == userId, ct);
        if (!exists)
        {
            throw ApiException.NotFound("learner_not_found", "Learner profile not found.");
        }
    }

    private async Task<Wallet> EnsureWalletAsync(string userId, CancellationToken ct)
    {
        var wallet = await db.Wallets.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (wallet is not null) return wallet;
        wallet = new Wallet
        {
            Id = $"wallet-{Guid.NewGuid():N}",
            UserId = userId,
            CreditBalance = 0,
            LedgerSummaryJson = "[]",
            LastUpdatedAt = DateTimeOffset.UtcNow
        };
        db.Wallets.Add(wallet);
        await db.SaveChangesAsync(ct);
        return wallet;
    }

    private async Task ConsumeReservationForSectionAsync(
        string userId,
        MockAttempt attempt,
        MockSectionAttempt section,
        string? turnaroundOption,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!SectionReviewSelected(attempt.ReviewSelection, attempt.MockType, attempt.SubtestCode, section.SubtestCode))
        {
            return;
        }

        var reservation = await db.MockReviewReservations
            .FirstOrDefaultAsync(x => x.MockAttemptId == attempt.Id && x.UserId == userId, ct);
        if (reservation is null || reservation.State is MockReviewReservationState.Consumed or MockReviewReservationState.Released or MockReviewReservationState.Expired)
        {
            return;
        }

        if (reservation.ConsumedCredits + reservation.ReleasedCredits >= reservation.ReservedCredits)
        {
            reservation.State = MockReviewReservationState.Consumed;
            return;
        }

        var contentAttemptId = section.ContentAttemptId;
        if (string.IsNullOrWhiteSpace(contentAttemptId))
        {
            contentAttemptId = $"attempt-mock-{Guid.NewGuid():N}";
            db.Attempts.Add(new Attempt
            {
                Id = contentAttemptId,
                UserId = userId,
                ContentId = section.ContentPaperId,
                SubtestCode = section.SubtestCode,
                Context = "mock",
                Mode = attempt.Mode,
                State = AttemptState.Completed,
                StartedAt = section.StartedAt ?? attempt.StartedAt,
                SubmittedAt = section.SubmittedAt ?? now,
                CompletedAt = now,
                ElapsedSeconds = section.StartedAt is null ? 0 : Math.Max(0, (int)(now - section.StartedAt.Value).TotalSeconds),
                ParentAttemptId = attempt.Id,
                AnswersJson = JsonSupport.Serialize(new { mockAttemptId = attempt.Id, mockSectionId = section.Id }),
                AnalysisJson = section.FeedbackJson,
                ExamFamilyCode = attempt.ExamFamilyCode,
                ExamTypeCode = attempt.ExamTypeCode
            });
            section.ContentAttemptId = contentAttemptId;
        }

        var existingReview = await db.ReviewRequests.AsNoTracking().AnyAsync(x => x.AttemptId == contentAttemptId, ct);
        if (existingReview)
        {
            return;
        }

        reservation.ConsumedCredits += 1;
        reservation.ConsumedAt = now;
        reservation.State = reservation.ConsumedCredits >= reservation.ReservedCredits
            ? MockReviewReservationState.Consumed
            : MockReviewReservationState.PartiallyConsumed;

        db.ReviewRequests.Add(new ReviewRequest
        {
            Id = $"review-{Guid.NewGuid():N}",
            AttemptId = contentAttemptId,
            SubtestCode = section.SubtestCode,
            State = ReviewRequestState.Queued,
            TurnaroundOption = string.IsNullOrWhiteSpace(turnaroundOption) ? "standard" : turnaroundOption,
            FocusAreasJson = "[]",
            LearnerNotes = $"Expert review requested from mock attempt {attempt.Id}.",
            PaymentSource = "mock_reserved_credits",
            PriceSnapshot = 1,
            CreatedAt = now,
            EligibilitySnapshotJson = JsonSupport.Serialize(new
            {
                source = "mock_review_reservation",
                mockAttemptId = attempt.Id,
                mockSectionId = section.Id,
                reservationId = reservation.Id
            })
        });
    }

    private async Task ReleaseReservationAsync(string userId, string mockAttemptId, string reason, CancellationToken ct)
    {
        var reservation = await db.MockReviewReservations
            .FirstOrDefaultAsync(x => x.UserId == userId && x.MockAttemptId == mockAttemptId, ct);
        if (reservation is null || reservation.State is MockReviewReservationState.Consumed or MockReviewReservationState.Released)
        {
            return;
        }

        var remaining = reservation.ReservedCredits - reservation.ConsumedCredits - reservation.ReleasedCredits;
        if (remaining <= 0)
        {
            reservation.State = MockReviewReservationState.Consumed;
            return;
        }

        var wallet = await EnsureWalletAsync(userId, ct);
        wallet.CreditBalance += remaining;
        wallet.LastUpdatedAt = DateTimeOffset.UtcNow;
        var txId = Guid.NewGuid();
        db.WalletTransactions.Add(new WalletTransaction
        {
            Id = txId,
            WalletId = wallet.Id,
            TransactionType = "refund",
            Amount = remaining,
            BalanceAfter = wallet.CreditBalance,
            ReferenceType = "mock",
            ReferenceId = mockAttemptId,
            Description = reason,
            CreatedBy = userId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        reservation.ReleasedCredits += remaining;
        reservation.ReleasedAt = DateTimeOffset.UtcNow;
        reservation.ReleaseTransactionId = txId;
        reservation.State = reservation.ConsumedCredits > 0
            ? MockReviewReservationState.PartiallyConsumed
            : MockReviewReservationState.Released;
    }

    private static int? ResolveScaledScore(string subtest, int? rawScore, int? scaledScore)
    {
        if (subtest is "reading" or "listening" && rawScore.HasValue)
        {
            return OetScoring.OetRawToScaled(rawScore.Value);
        }

        return scaledScore.HasValue
            ? Math.Clamp(scaledScore.Value, OetScoring.ScaledMin, OetScoring.ScaledMax)
            : null;
    }

    private static List<ApiFieldError> ValidatePublishGate(MockBundle bundle, IReadOnlyList<MockBundleSection> sections)
    {
        var errors = new List<ApiFieldError>();
        if (string.IsNullOrWhiteSpace(bundle.SourceProvenance))
        {
            errors.Add(new ApiFieldError("sourceProvenance", "required", "Mock bundle provenance is required before publish."));
        }

        if (bundle.MockType == "full")
        {
            var actual = sections.Select(x => x.SubtestCode).ToArray();
            if (!actual.SequenceEqual(FullMockOrder, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(new ApiFieldError("sections", "wrong_order", "Full mock bundles must contain Listening, Reading, Writing, Speaking in that order."));
            }
        }
        else
        {
            if (sections.Count != 1 || !string.Equals(sections[0].SubtestCode, bundle.SubtestCode, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ApiFieldError("sections", "subtest_required", "Sub-test mock bundles require exactly one section matching the selected sub-test."));
            }
        }

        foreach (var section in sections)
        {
            var paper = section.ContentPaper;
            if (paper is null)
            {
                errors.Add(new ApiFieldError("sections", "paper_missing", $"Section {section.Id} does not reference a valid content paper."));
                continue;
            }

            if (paper.Status != ContentStatus.Published)
            {
                errors.Add(new ApiFieldError("sections", "paper_unpublished", $"{paper.Title} must be published before this mock can be published."));
            }
            if (!string.Equals(paper.SubtestCode, section.SubtestCode, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ApiFieldError("sections", "paper_subtest_mismatch", $"{paper.Title} does not match the section sub-test."));
            }
            if (string.IsNullOrWhiteSpace(paper.SourceProvenance))
            {
                errors.Add(new ApiFieldError("sections", "paper_provenance_missing", $"{paper.Title} is missing content provenance."));
            }
            if (!bundle.AppliesToAllProfessions && !paper.AppliesToAllProfessions && !string.Equals(paper.ProfessionId, bundle.ProfessionId, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ApiFieldError("sections", "profession_mismatch", $"{paper.Title} does not match the bundle profession."));
            }
        }

        return errors;
    }

    private async Task<int> ComputeEstimatedDurationAsync(string bundleId, MockBundleSection newSection, CancellationToken ct)
    {
        var existing = await db.MockBundleSections.AsNoTracking()
            .Where(x => x.MockBundleId == bundleId)
            .SumAsync(x => x.TimeLimitMinutes, ct);
        return existing + newSection.TimeLimitMinutes;
    }

    private static object ProjectBundleCard(MockBundle bundle, MockAttempt? latestAttempt, Dictionary<string, object?>? latestReport, IReadOnlyList<MockSectionAttempt>? latestAttemptSections = null)
    {
        var completed = latestAttempt?.State == AttemptState.Completed;
        // Per-sub-test progress dot map for the Full Mock row. Only populated when the learner
        // has an existing attempt for this bundle \u2014 otherwise all dots default to "not started".
        var sectionProgress = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (latestAttemptSections is not null)
        {
            foreach (var section in latestAttemptSections)
            {
                if (string.IsNullOrWhiteSpace(section.SubtestCode)) continue;
                sectionProgress[section.SubtestCode] = section.State switch
                {
                    AttemptState.Completed => "completed",
                    AttemptState.InProgress => "in-progress",
                    AttemptState.Paused => "in-progress",
                    AttemptState.Evaluating => "in-progress",
                    AttemptState.Abandoned => "not-started",
                    _ => "not-started"
                };
            }
        }

        var includedSubtests = bundle.Sections
            .OrderBy(x => x.SectionOrder)
            .Select(x => x.SubtestCode)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new
        {
            id = bundle.Id,
            bundleId = bundle.Id,
            title = bundle.Title,
            mockType = bundle.MockType,
            subtest = bundle.SubtestCode,
            professionId = bundle.ProfessionId,
            appliesToAllProfessions = bundle.AppliesToAllProfessions,
            status = latestAttempt is null ? "available" : ToApiState(latestAttempt.State),
            score = completed ? latestReport?.GetValueOrDefault("overallScore")?.ToString() : null,
            date = latestAttempt?.CompletedAt?.ToString("MMM dd, yyyy"),
            duration = $"{bundle.EstimatedDurationMinutes}m",
            isRecommended = latestAttempt is null && bundle.MockType == "full",
            reason = bundle.Status == ContentStatus.Published ? null : "Not published",
            route = $"/mocks/setup?bundleId={Uri.EscapeDataString(bundle.Id)}&type={bundle.MockType}" + (bundle.SubtestCode is null ? string.Empty : $"&subtest={Uri.EscapeDataString(bundle.SubtestCode)}"),
            sectionCount = bundle.Sections.Count,
            reviewEligibleSections = bundle.Sections.Count(x => x.ReviewEligible),
            includedSubtests,
            sectionProgress
        };
    }

    private static object ProjectBundleOption(MockBundle bundle) => new
    {
        id = bundle.Id,
        bundleId = bundle.Id,
        title = bundle.Title,
        mockType = bundle.MockType,
        subtest = bundle.SubtestCode,
        professionId = bundle.ProfessionId,
        appliesToAllProfessions = bundle.AppliesToAllProfessions,
        estimatedDurationMinutes = bundle.EstimatedDurationMinutes,
        sections = bundle.Sections.OrderBy(x => x.SectionOrder).Select(x => new
        {
            id = x.Id,
            subtest = x.SubtestCode,
            title = x.ContentPaper?.Title ?? ToDisplaySubtest(x.SubtestCode),
            timeLimitMinutes = x.TimeLimitMinutes,
            reviewEligible = x.ReviewEligible,
            contentPaperId = x.ContentPaperId
        }).ToArray()
    };

    private static object ProjectBundleAdmin(MockBundle bundle) => new
    {
        id = bundle.Id,
        bundleId = bundle.Id,
        title = bundle.Title,
        slug = bundle.Slug,
        mockType = bundle.MockType,
        subtestCode = bundle.SubtestCode,
        professionId = bundle.ProfessionId,
        appliesToAllProfessions = bundle.AppliesToAllProfessions,
        status = bundle.Status.ToString().ToLowerInvariant(),
        estimatedDurationMinutes = bundle.EstimatedDurationMinutes,
        priority = bundle.Priority,
        tagsCsv = bundle.TagsCsv,
        sourceProvenance = bundle.SourceProvenance,
        createdAt = bundle.CreatedAt,
        updatedAt = bundle.UpdatedAt,
        publishedAt = bundle.PublishedAt,
        sections = bundle.Sections.OrderBy(x => x.SectionOrder).Select(x => new
        {
            id = x.Id,
            sectionOrder = x.SectionOrder,
            subtestCode = x.SubtestCode,
            contentPaperId = x.ContentPaperId,
            contentPaperTitle = x.ContentPaper?.Title,
            contentPaperStatus = x.ContentPaper is null ? null : x.ContentPaper.Status.ToString().ToLowerInvariant(),
            timeLimitMinutes = x.TimeLimitMinutes,
            reviewEligible = x.ReviewEligible
        }).ToArray()
    };

    private static object ProjectSectionAttempt(MockSectionAttempt section, MockBundleSection bundleSection, MockAttempt attempt) => new
    {
        id = section.Id,
        sectionAttemptId = section.Id,
        bundleSectionId = bundleSection.Id,
        title = $"{ToDisplaySubtest(section.SubtestCode)} section",
        subtest = section.SubtestCode,
        state = ToApiState(section.State),
        reviewAvailable = bundleSection.ReviewEligible,
        reviewSelected = SectionReviewSelected(attempt.ReviewSelection, attempt.MockType, attempt.SubtestCode, section.SubtestCode),
        launchRoute = section.LaunchRoute,
        contentPaperId = section.ContentPaperId,
        contentPaperTitle = bundleSection.ContentPaper?.Title,
        timeLimitMinutes = bundleSection.TimeLimitMinutes,
        startedAt = section.StartedAt,
        deadlineAt = section.DeadlineAt,
        submittedAt = section.SubmittedAt,
        completedAt = section.CompletedAt,
        rawScore = section.RawScore,
        rawScoreMax = section.RawScoreMax,
        scaledScore = section.ScaledScore,
        grade = section.Grade
    };

    private static object ProjectAttemptSummary(MockAttempt attempt) => new
    {
        mockAttemptId = attempt.Id,
        bundleId = attempt.MockBundleId,
        state = ToApiState(attempt.State),
        mockType = attempt.MockType,
        subtest = attempt.SubtestCode,
        startedAt = attempt.StartedAt,
        resumeRoute = $"/mocks/player/{attempt.Id}",
        reportRoute = attempt.ReportId is null ? null : $"/mocks/report/{attempt.ReportId}"
    };

    private static object ProjectReservation(MockReviewReservation reservation) => new
    {
        id = reservation.Id,
        state = reservation.State.ToString().ToLowerInvariant(),
        selection = reservation.Selection,
        reservedCredits = reservation.ReservedCredits,
        consumedCredits = reservation.ConsumedCredits,
        releasedCredits = reservation.ReleasedCredits,
        pendingCredits = Math.Max(0, reservation.ReservedCredits - reservation.ConsumedCredits - reservation.ReleasedCredits),
        reservedAt = reservation.ReservedAt,
        expiresAt = reservation.ExpiresAt
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
    /// Builds a lightweight readiness advisory object for the Mock Center based on the latest report's
    /// overall scaled score. Anchors on OET pass thresholds (350 = B grade) per docs/SCORING.md.
    /// Returns null when no scored report exists yet.
    /// </summary>
    private static object? BuildReadinessAdvisory(Dictionary<string, object?>? latestReport)
    {
        if (latestReport is null) return null;
        if (!latestReport.TryGetValue("overallScore", out var scoreRaw) || scoreRaw is null) return null;
        if (!int.TryParse(scoreRaw.ToString(), out var overall)) return null;

        // Canonical OET pass anchor = 350/500 (docs/SCORING.md). Tiering is advisory copy only.
        string tier;
        string message;
        if (overall >= 400)
        {
            tier = "strong";
            message = "You're comfortably above the OET pass line. Target consistency across all four sub-tests.";
        }
        else if (overall >= 350)
        {
            tier = "passing";
            message = "You're at or above the OET pass line. Another full mock will confirm the result is repeatable.";
        }
        else if (overall >= 300)
        {
            tier = "developing";
            message = "You're within striking distance. Practise the weakest sub-test before the next full mock.";
        }
        else
        {
            tier = "foundation";
            message = "Focus on sub-test drills first — a full mock will be more useful after targeted practice.";
        }

        return new
        {
            tier,
            message,
            passThreshold = 350,
            overallScore = overall
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


    private static string BuildLaunchRoute(string attemptId, MockBundleSection section, string? sectionAttemptId)
    {
        var query = $"mockAttemptId={Uri.EscapeDataString(attemptId)}";
        if (!string.IsNullOrWhiteSpace(sectionAttemptId))
        {
            query += $"&mockSectionId={Uri.EscapeDataString(sectionAttemptId)}";
        }
        query += $"&paperId={Uri.EscapeDataString(section.ContentPaperId)}";

        return section.SubtestCode switch
        {
            "reading" => $"/reading/paper/{Uri.EscapeDataString(section.ContentPaperId)}?{query}",
            "listening" => $"/listening/player/{Uri.EscapeDataString(section.ContentPaperId)}?{query}",
            "writing" => $"/writing/player?taskId={Uri.EscapeDataString(section.ContentPaperId)}&{query}",
            "speaking" => $"/speaking/task/{Uri.EscapeDataString(section.ContentPaperId)}?{query}",
            _ => $"/mocks/player/{Uri.EscapeDataString(attemptId)}"
        };
    }

    private static bool SectionReviewSelected(string selection, string mockType, string? subtest, string sectionSubtest)
        => selection == "writing_and_speaking" && ProductiveSubtests.Contains(sectionSubtest)
            || selection == "writing" && sectionSubtest == "writing"
            || selection == "speaking" && sectionSubtest == "speaking"
            || selection == "current_subtest" && mockType == "sub" && string.Equals(subtest, sectionSubtest, StringComparison.OrdinalIgnoreCase);

    private static int ReviewCost(string selection, string mockType, string? subType)
        => selection switch
        {
            "writing_and_speaking" => 2,
            "writing" or "speaking" => 1,
            "current_subtest" when mockType == "sub" && ProductiveSubtests.Contains(subType ?? string.Empty) => 1,
            _ => 0
        };

    private static string NormalizeMockReviewSelection(string mockType, string? subType, bool includeReview, string? reviewSelection)
    {
        var requestedSelection = (reviewSelection ?? string.Empty).Trim().ToLowerInvariant();
        if (mockType == "full")
        {
            var allowed = new HashSet<string>(["none", "writing", "speaking", "writing_and_speaking"], StringComparer.Ordinal);
            if (allowed.Contains(requestedSelection)) return requestedSelection;
            return includeReview ? "writing_and_speaking" : "none";
        }

        var productiveSubtest = ProductiveSubtests.Contains(subType ?? string.Empty);
        if (!productiveSubtest) return "none";
        return requestedSelection == "current_subtest"
            ? "current_subtest"
            : includeReview ? "current_subtest" : "none";
    }

    private static string NormalizeMockType(string? value)
    {
        var normalized = (value ?? "full").Trim().ToLowerInvariant();
        return normalized is "full" or "sub"
            ? normalized
            : throw ApiException.Validation("invalid_mock_type", "Mock type must be full or sub.", [new ApiFieldError("mockType", "invalid", "Use full or sub.")]);
    }

    private static string NormalizeSubtest(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return FullMockOrder.Contains(normalized)
            ? normalized
            : throw ApiException.Validation("invalid_subtest", "Choose a supported OET sub-test.", [new ApiFieldError("subtestCode", "invalid", "Use listening, reading, writing, or speaking.")]);
    }

    private static string NormalizeMode(string? value)
    {
        var normalized = (value ?? "exam").Trim().ToLowerInvariant();
        return normalized is "exam" or "practice" ? normalized : "exam";
    }

    private static string NormalizeProfession(string? value)
        => string.IsNullOrWhiteSpace(value) ? "medicine" : value.Trim().ToLowerInvariant();

    private static string RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw ApiException.Validation("required_field", $"{field} is required.", [new ApiFieldError(field, "required", "Enter a value.")]);
        }
        return value.Trim();
    }

    private async Task<string> UniqueSlugAsync(string title, CancellationToken ct)
    {
        var baseSlug = string.Join("-", title.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Replace("/", "-", StringComparison.Ordinal)
            .Replace("\\", "-", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(baseSlug)) baseSlug = $"mock-{Guid.NewGuid():N}";
        var slug = baseSlug;
        var suffix = 2;
        while (await db.MockBundles.AsNoTracking().AnyAsync(x => x.Slug == slug, ct))
        {
            slug = $"{baseSlug}-{suffix++}";
        }
        return slug;
    }

    private static int DefaultTimeLimit(string subtest) => subtest switch
    {
        "listening" => 42,
        "reading" => 60,
        "writing" => 45,
        "speaking" => 20,
        _ => 45
    };

    private static string ToDisplaySubtest(string subtest)
        => string.IsNullOrWhiteSpace(subtest) ? "Mock" : char.ToUpperInvariant(subtest[0]) + subtest[1..];

    private static string ToApiState(AttemptState state) => state switch
    {
        AttemptState.NotStarted => "ready",
        AttemptState.InProgress => "in_progress",
        AttemptState.Paused => "paused",
        AttemptState.Submitted => "submitted",
        AttemptState.Evaluating => "queued",
        AttemptState.Completed => "completed",
        AttemptState.Failed => "failed",
        AttemptState.Abandoned => "cancelled",
        _ => state.ToString().ToLowerInvariant()
    };

    private static string ToAsyncState(AsyncState state) => state switch
    {
        AsyncState.Idle => "idle",
        AsyncState.Queued => "queued",
        AsyncState.Processing => "processing",
        AsyncState.Completed => "completed",
        AsyncState.Failed => "failed",
        _ => state.ToString().ToLowerInvariant()
    };

    private void RecordEvent(string userId, string eventName, object payload)
    {
        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            Id = $"evt-{Guid.NewGuid():N}",
            UserId = userId,
            EventName = eventName,
            PayloadJson = JsonSupport.Serialize(payload),
            OccurredAt = DateTimeOffset.UtcNow
        });
    }

    private void LogAudit(string adminId, string action, string resourceType, string resourceId, string details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorName = adminId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details
        });
    }
}
