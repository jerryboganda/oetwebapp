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
                    reviewSlaLabel = "Tutor review turnaround: within 48 hours"
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

        var firstFullBundle = bundles.FirstOrDefault(x => MockTypes.IsFullShape(x.MockType));
        var firstFullRoute = firstFullBundle is null
            ? "/mocks/setup"
            : $"/mocks/setup?bundleId={Uri.EscapeDataString(firstFullBundle.Id)}&type={firstFullBundle.MockType}";

        var fullMocks = bundles
            .Where(x => MockTypes.IsFullShape(x.MockType))
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
            .Where(x => MockTypes.IsSubShape(x.MockType))
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
                // Standard tutor review SLA surfaced so learners know turnaround up-front (OET business req):
                // writing + speaking tutor reviews are committed to 48h under current operations policy.
                reviewTurnaroundHours = 48,
                reviewSlaLabel = "Tutor review turnaround: within 48 hours"
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
                new { id = MockTypes.Full, label = MockTypes.Label(MockTypes.Full), description = "All four sub-tests in OET order." },
                new { id = MockTypes.Lrw, label = MockTypes.Label(MockTypes.Lrw), description = "Listening, Reading and Writing in one sitting (Speaking scheduled separately)." },
                new { id = MockTypes.Sub, label = MockTypes.Label(MockTypes.Sub), description = "Focus on one published sub-test bundle." },
                new { id = MockTypes.Part, label = MockTypes.Label(MockTypes.Part), description = "Single part within a sub-test (e.g. Reading Part A only)." },
                new { id = MockTypes.Diagnostic, label = MockTypes.Label(MockTypes.Diagnostic), description = "Establish your baseline and unlock a personalised study path." },
                new { id = MockTypes.FinalReadiness, label = MockTypes.Label(MockTypes.FinalReadiness), description = "Strict full mock taken before booking the real exam." },
                new { id = MockTypes.Remedial, label = MockTypes.Label(MockTypes.Remedial), description = "Targeted mock generated from your weak-area analysis." },
            },
            subTypes = FullMockOrder.Select(x => new { id = x, label = ToDisplaySubtest(x) }),
            modes = new[]
            {
                new { id = "exam", label = "Exam" },
                new { id = "practice", label = "Practice" }
            },
            deliveryModes = new[]
            {
                new { id = MockDeliveryModes.Computer, label = "On-screen (computer)" },
                new { id = MockDeliveryModes.OetHome, label = "OET@Home (remote)" },
                new { id = MockDeliveryModes.Paper, label = "Paper-based" },
            },
            strictnessOptions = new[]
            {
                new { id = MockStrictness.Learning, label = "Learning", description = "Pause, replay, and hints allowed." },
                new { id = MockStrictness.Exam, label = "Exam", description = "Strict timers, one-play audio, no hints." },
                new { id = MockStrictness.FinalReadiness, label = "Final readiness", description = "Strictest preset \u2014 used right before the real exam." },
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
        var subType = MockTypes.IsSubShape(mockType) ? NormalizeSubtest(request.SubType) : null;
        var profession = NormalizeProfession(request.Profession);
        var deliveryMode = NormalizeDeliveryMode(request.DeliveryMode);
        var strictness = NormalizeStrictness(request.Strictness, mockType);
        var effectiveStrictTimer = request.StrictTimer || strictness is MockStrictness.Exam or MockStrictness.FinalReadiness;
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
                "You do not have enough review credits to reserve the selected tutor review.");
        }

        var id = $"mock-attempt-{Guid.NewGuid():N}";
        var config = new
        {
            mockType,
            mockTypeLabel = MockTypes.Label(mockType),
            subType,
            mode = NormalizeMode(request.Mode),
            profession,
            deliveryMode,
            strictness,
            includeReview = reviewCost > 0,
            strictTimer = effectiveStrictTimer,
            reviewSelection,
            bundleId = bundle.Id,
            bundleTitle = bundle.Title,
            targetCountry = request.TargetCountry,
            releasePolicy = bundle.ReleasePolicy,
            sourceStatus = bundle.SourceStatus,
            watermarkEnabled = bundle.WatermarkEnabled
        };

        var attempt = new MockAttempt
        {
            Id = id,
            UserId = userId,
            MockBundleId = bundle.Id,
            MockType = mockType,
            SubtestCode = MockTypes.IsSubShape(mockType) ? subType : null,
            Mode = config.mode,
            Profession = profession,
            ReviewSelection = reviewSelection,
            StrictTimer = effectiveStrictTimer,
            DeliveryMode = deliveryMode,
            Strictness = strictness,
            RandomisationSeed = bundle.RandomiseQuestions ? Random.Shared.NextInt64(1, uint.MaxValue) : null,
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
                LaunchRoute = BuildLaunchRoute(attempt, section, null)
            };
            sectionAttempt.LaunchRoute = BuildLaunchRoute(attempt, section, sectionAttempt.Id);
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
                Description = $"Reserved {reviewCost} tutor review credit(s) for {bundle.Title}.",
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

        section.LaunchRoute = BuildLaunchRoute(attempt, bundleSection, section.Id, section.ContentAttemptId);
        return ProjectSectionAttempt(section, bundleSection, attempt);
    }

    public async Task BindSectionContentAttemptIfRequestedAsync(
        string userId,
        string? mockAttemptId,
        string? sectionId,
        string contentAttemptId,
        string subtestCode,
        string paperId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mockAttemptId) && string.IsNullOrWhiteSpace(sectionId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(mockAttemptId) || string.IsNullOrWhiteSpace(sectionId))
        {
            throw ApiException.Validation("mock_section_binding_incomplete", "Mock section binding requires both mockAttemptId and mockSectionId.");
        }

        var attempt = await GetMockAttemptOwnedByUserAsync(userId, mockAttemptId.Trim(), ct);
        if (attempt.State is AttemptState.Completed or AttemptState.Abandoned)
        {
            throw ApiException.Conflict("mock_attempt_closed", "This mock attempt is already closed.");
        }

        var section = await db.MockSectionAttempts
            .FirstOrDefaultAsync(x => x.Id == sectionId.Trim() && x.MockAttemptId == attempt.Id, ct)
            ?? throw ApiException.NotFound("mock_section_not_found", "Mock section not found.");
        var bundleSection = await db.MockBundleSections.AsNoTracking()
            .FirstAsync(x => x.Id == section.MockBundleSectionId, ct);
        var normalizedSubtest = NormalizeSubtest(subtestCode);
        if (!string.Equals(section.SubtestCode, normalizedSubtest, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(bundleSection.SubtestCode, normalizedSubtest, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("mock_section_subtest_mismatch", "The content attempt does not match this mock section subtest.");
        }
        if (!string.Equals(bundleSection.ContentPaperId, paperId, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("mock_section_paper_mismatch", "The content attempt does not match this mock section paper.");
        }
        if (string.Equals(normalizedSubtest, "listening", StringComparison.OrdinalIgnoreCase))
        {
            await RequireRelationalListeningStructureForMockAsync(paperId, ct);
        }
        if (section.State != AttemptState.InProgress)
        {
            throw ApiException.Conflict("mock_section_not_in_progress", "Start the mock section before binding its content attempt.");
        }

        var trimmedContentAttemptId = contentAttemptId.Trim();
        var contentAttemptExists = normalizedSubtest switch
        {
            "reading" => await db.ReadingAttempts.AsNoTracking().AnyAsync(x =>
                x.Id == trimmedContentAttemptId && x.UserId == userId && x.PaperId == paperId,
                ct),
            "listening" => await db.ListeningAttempts.AsNoTracking().AnyAsync(x =>
                x.Id == trimmedContentAttemptId && x.UserId == userId && x.PaperId == paperId,
                ct),
            _ => false,
        };
        if (!contentAttemptExists)
        {
            throw ApiException.NotFound("content_attempt_not_found", "The content attempt was not found for this learner and paper.");
        }

        if (!string.IsNullOrWhiteSpace(section.ContentAttemptId)
            && !string.Equals(section.ContentAttemptId, trimmedContentAttemptId, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Conflict("mock_section_content_attempt_mismatch", "This mock section is already bound to another content attempt.");
        }

        section.ContentAttemptId = trimmedContentAttemptId;
        section.LaunchRoute = BuildLaunchRoute(attempt, bundleSection, section.Id, section.ContentAttemptId);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> ValidateSectionContentAttemptBindingTargetIfRequestedAsync(
        string userId,
        string? mockAttemptId,
        string? sectionId,
        string subtestCode,
        string paperId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mockAttemptId) && string.IsNullOrWhiteSpace(sectionId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(mockAttemptId) || string.IsNullOrWhiteSpace(sectionId))
        {
            throw ApiException.Validation("mock_section_binding_incomplete", "Mock section binding requires both mockAttemptId and mockSectionId.");
        }

        var attempt = await GetMockAttemptOwnedByUserAsync(userId, mockAttemptId.Trim(), ct);
        if (attempt.State is AttemptState.Completed or AttemptState.Abandoned)
        {
            throw ApiException.Conflict("mock_attempt_closed", "This mock attempt is already closed.");
        }

        var section = await db.MockSectionAttempts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sectionId.Trim() && x.MockAttemptId == attempt.Id, ct)
            ?? throw ApiException.NotFound("mock_section_not_found", "Mock section not found.");
        var bundleSection = await db.MockBundleSections.AsNoTracking()
            .FirstAsync(x => x.Id == section.MockBundleSectionId, ct);
        var normalizedSubtest = NormalizeSubtest(subtestCode);
        if (!string.Equals(section.SubtestCode, normalizedSubtest, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(bundleSection.SubtestCode, normalizedSubtest, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("mock_section_subtest_mismatch", "The content attempt does not match this mock section subtest.");
        }
        if (!string.Equals(bundleSection.ContentPaperId, paperId, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("mock_section_paper_mismatch", "The content attempt does not match this mock section paper.");
        }
        if (string.Equals(normalizedSubtest, "listening", StringComparison.OrdinalIgnoreCase))
        {
            await RequireRelationalListeningStructureForMockAsync(paperId, ct);
        }
        if (section.State != AttemptState.InProgress)
        {
            throw ApiException.Conflict("mock_section_not_in_progress", "Start the mock section before binding its content attempt.");
        }
        if (!string.IsNullOrWhiteSpace(section.ContentAttemptId))
        {
            throw ApiException.Conflict("mock_section_content_attempt_already_bound", "This mock section is already bound to a content attempt.");
        }

        return true;
    }

    private async Task RequireRelationalListeningStructureForMockAsync(string paperId, CancellationToken ct)
    {
        var hasRelationalQuestions = await db.ListeningQuestions.AsNoTracking()
            .AnyAsync(question => question.PaperId == paperId, ct);
        if (!hasRelationalQuestions)
        {
            throw ApiException.Validation(
                "mock_listening_structure_required",
                "Listening mock sections require structured Listening questions before they can start.");
        }
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

        var canonicalEvidence = await ResolveCanonicalSectionEvidenceAsync(userId, request.ContentAttemptId, section, bundleSection, ct);
        if (canonicalEvidence is null && !string.IsNullOrWhiteSpace(request.ContentAttemptId))
        {
            var ownsContentAttempt = await OwnsLegacySectionEvidenceAsync(userId, request.ContentAttemptId, section, bundleSection, ct);
            if (!ownsContentAttempt)
            {
                throw ApiException.NotFound("content_attempt_not_found", "The submitted section evidence was not found for this learner and paper.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        section.State = AttemptState.Completed;
        section.SubmittedAt ??= now;
        section.CompletedAt = now;
        section.ContentAttemptId = canonicalEvidence?.ContentAttemptId
            ?? (string.IsNullOrWhiteSpace(request.ContentAttemptId) ? section.ContentAttemptId : request.ContentAttemptId.Trim());
        section.RawScore = canonicalEvidence?.RawScore ?? request.RawScore ?? section.RawScore;
        section.RawScoreMax = canonicalEvidence?.RawScoreMax ?? request.RawScoreMax ?? section.RawScoreMax;
        section.ScaledScore = canonicalEvidence?.ScaledScore ?? ResolveScaledScore(section.SubtestCode, request.RawScore, request.ScaledScore);
        section.Grade = canonicalEvidence?.Grade ?? (string.IsNullOrWhiteSpace(request.Grade)
            ? section.ScaledScore is null ? section.Grade : OetScoring.OetGradeLetterFromScaled(section.ScaledScore.Value)
            : request.Grade);
        section.FeedbackJson = JsonSupport.Serialize(BuildSectionEvidencePayload(request.Evidence, canonicalEvidence));

        await ConsumeReservationForSectionAsync(userId, attempt, section, request.ReviewTurnaroundOption, now, ct);
        RecordEvent(userId, "mock_section_completed", new { mockAttemptId = attempt.Id, sectionId = section.Id, subtest = section.SubtestCode, section.ScaledScore });
        await db.SaveChangesAsync(ct);
        return ProjectSectionAttempt(section, bundleSection, attempt);
    }

    private async Task<CanonicalSectionEvidence?> ResolveCanonicalSectionEvidenceAsync(
        string userId,
        string? contentAttemptId,
        MockSectionAttempt section,
        MockBundleSection bundleSection,
        CancellationToken ct)
    {
        return section.SubtestCode.Trim().ToLowerInvariant() switch
        {
            "reading" => await ResolveReadingEvidenceAsync(userId, contentAttemptId, section, bundleSection.ContentPaperId, ct),
            "listening" => await ResolveListeningEvidenceAsync(userId, contentAttemptId, section, bundleSection.ContentPaperId, ct),
            _ => null,
        };
    }

    private async Task<CanonicalSectionEvidence> ResolveReadingEvidenceAsync(
        string userId,
        string? contentAttemptId,
        MockSectionAttempt section,
        string paperId,
        CancellationToken ct)
    {
        var attemptId = RequireCanonicalContentAttemptId(contentAttemptId);
        RequireBoundContentAttempt(section, attemptId);
        var attempt = await db.ReadingAttempts.AsNoTracking().FirstOrDefaultAsync(x =>
            x.Id == attemptId && x.UserId == userId && x.PaperId == paperId && x.Status == ReadingAttemptStatus.Submitted,
            ct) ?? throw ApiException.NotFound("content_attempt_not_found", "The submitted section evidence was not found for this learner and paper.");
        return BuildCanonicalEvidence(attempt.Id, attempt.RawScore, attempt.MaxRawScore, attempt.ScaledScore, "reading_attempt");
    }

    private async Task<CanonicalSectionEvidence> ResolveListeningEvidenceAsync(
        string userId,
        string? contentAttemptId,
        MockSectionAttempt section,
        string paperId,
        CancellationToken ct)
    {
        var attemptId = RequireCanonicalContentAttemptId(contentAttemptId);
        RequireBoundContentAttempt(section, attemptId);
        var attempt = await db.ListeningAttempts.AsNoTracking().FirstOrDefaultAsync(x =>
            x.Id == attemptId && x.UserId == userId && x.PaperId == paperId && x.Status == ListeningAttemptStatus.Submitted,
            ct) ?? throw ApiException.NotFound("content_attempt_not_found", "The submitted section evidence was not found for this learner and paper.");
        return BuildCanonicalEvidence(attempt.Id, attempt.RawScore, attempt.MaxRawScore, attempt.ScaledScore, "listening_attempt");
    }

    private async Task<bool> OwnsLegacySectionEvidenceAsync(
        string userId,
        string contentAttemptId,
        MockSectionAttempt section,
        MockBundleSection bundleSection,
        CancellationToken ct)
    {
        var attemptId = contentAttemptId.Trim();
        return await db.Attempts.AsNoTracking().AnyAsync(x =>
                x.Id == attemptId
                && x.UserId == userId
                && x.ContentId == bundleSection.ContentPaperId
                && x.SubtestCode == section.SubtestCode
                && x.State == AttemptState.Completed,
                ct);
    }

    private static string RequireCanonicalContentAttemptId(string? contentAttemptId)
        => string.IsNullOrWhiteSpace(contentAttemptId)
            ? throw ApiException.Validation("content_attempt_required", "Reading and Listening mock sections require submitted section evidence.")
            : contentAttemptId.Trim();

    private static void RequireBoundContentAttempt(MockSectionAttempt section, string contentAttemptId)
    {
        if (string.IsNullOrWhiteSpace(section.ContentAttemptId))
        {
            throw ApiException.Conflict("content_attempt_not_bound", "This mock section has not been bound to a Reading or Listening attempt.");
        }

        if (!string.Equals(section.ContentAttemptId, contentAttemptId, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Conflict("content_attempt_mismatch", "The submitted section evidence does not match the attempt started for this mock section.");
        }
    }

    private static CanonicalSectionEvidence BuildCanonicalEvidence(
        string contentAttemptId,
        int? rawScore,
        int rawScoreMax,
        int? scaledScore,
        string evidenceSource)
    {
        if (!rawScore.HasValue)
        {
            throw ApiException.Conflict("content_attempt_not_graded", "The submitted section evidence has not been graded yet.");
        }

        var scaled = scaledScore ?? OetScoring.OetRawToScaled(rawScore.Value);
        var max = rawScoreMax > 0 ? rawScoreMax : 42;
        return new CanonicalSectionEvidence(contentAttemptId, rawScore.Value, max, scaled, OetScoring.OetGradeLetterFromScaled(scaled), evidenceSource);
    }

    private static Dictionary<string, object?> BuildSectionEvidencePayload(
        Dictionary<string, object?>? requestEvidence,
        CanonicalSectionEvidence? canonicalEvidence)
    {
        var evidence = requestEvidence is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(requestEvidence);
        if (canonicalEvidence is not null)
        {
            evidence["evidenceSource"] = canonicalEvidence.EvidenceSource;
            evidence["contentAttemptId"] = canonicalEvidence.ContentAttemptId;
        }

        return evidence;
    }

    private sealed record CanonicalSectionEvidence(
        string ContentAttemptId,
        int RawScore,
        int RawScoreMax,
        int ScaledScore,
        string Grade,
        string EvidenceSource);

    public async Task<object> SubmitMockAttemptAsync(string userId, string mockAttemptId, CancellationToken ct)
    {
        var attempt = await GetMockAttemptOwnedByUserAsync(userId, mockAttemptId, ct);
        if (attempt.State == AttemptState.Completed && attempt.ReportId is not null)
        {
            return new { mockAttemptId = attempt.Id, state = "completed", reportId = attempt.ReportId, reportRoute = $"/mocks/report/{attempt.ReportId}" };
        }

        var sectionStates = await db.MockSectionAttempts.AsNoTracking()
            .Where(x => x.MockAttemptId == attempt.Id)
            .Join(db.MockBundleSections.AsNoTracking(),
                sectionAttempt => sectionAttempt.MockBundleSectionId,
                bundleSection => bundleSection.Id,
                (sectionAttempt, bundleSection) => new
                {
                    sectionAttempt.Id,
                    sectionAttempt.SubtestCode,
                    sectionAttempt.State,
                    bundleSection.IsRequired
                })
            .ToListAsync(ct);
        var completedCount = sectionStates.Count(x => x.State == AttemptState.Completed);
        if (completedCount == 0)
        {
            throw ApiException.Validation(
                "mock_no_completed_sections",
                "Complete at least one section before submitting the mock.",
                [new ApiFieldError("mockAttemptId", "no_completed_sections", "Start and complete a section first.")]);
        }

        var incompleteRequiredSections = sectionStates
            .Where(x => x.IsRequired && x.State != AttemptState.Completed)
            .ToList();
        if (incompleteRequiredSections.Count > 0)
        {
            var missing = incompleteRequiredSections
                .Select(x => x.SubtestCode)
                .Distinct()
                .ToArray();
            throw ApiException.Validation(
                "mock_sections_incomplete",
                "Complete every required mock section before submitting the report.",
                [new ApiFieldError("sections", "incomplete", $"Still pending: {string.Join(", ", missing)}.")]);
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

    /// <summary>
    /// Mocks V2 Wave 2 — record a batch of proctoring events for an attempt.
    /// Rate-limited at <see cref="ProctoringEventCap"/> rows per attempt to prevent abuse.
    /// </summary>
    public const int ProctoringEventCap = 250;
    public const int ProctoringBatchMax = 50;

    public async Task<object> RecordProctoringEventsAsync(
        string userId,
        string mockAttemptId,
        MockProctoringEventBatchRequest request,
        CancellationToken ct)
    {
        if (request is null || request.Events is null || request.Events.Count == 0)
        {
            throw ApiException.Validation("invalid_request", "events array is required.");
        }
        if (request.Events.Count > ProctoringBatchMax)
        {
            throw ApiException.Validation("batch_too_large", $"Up to {ProctoringBatchMax} events per request.");
        }

        var attempt = await GetMockAttemptOwnedByUserAsync(userId, mockAttemptId, ct);

        var existing = await db.MockProctoringEvents.CountAsync(x => x.MockAttemptId == attempt.Id, ct);
        var capacity = ProctoringEventCap - existing;
        if (capacity <= 0)
        {
            return new { ok = true, accepted = 0, dropped = request.Events.Count, reason = "cap_reached" };
        }

        var validSectionIds = await db.MockSectionAttempts
            .Where(x => x.MockAttemptId == attempt.Id)
            .Select(x => x.Id)
            .ToListAsync(ct);
        var sectionIdSet = validSectionIds.ToHashSet(StringComparer.Ordinal);

        var now = DateTimeOffset.UtcNow;
        var accepted = 0;
        var dropped = 0;
        foreach (var ev in request.Events)
        {
            if (accepted >= capacity) { dropped++; continue; }
            if (string.IsNullOrWhiteSpace(ev.Kind) || !MockProctoringKinds.All.Contains(ev.Kind))
            {
                dropped++;
                continue;
            }
            var severity = !string.IsNullOrWhiteSpace(ev.Severity) && MockProctoringKinds.Severities.Contains(ev.Severity)
                ? ev.Severity!
                : MockProctoringKinds.DefaultSeverity(ev.Kind);
            var sectionId = !string.IsNullOrWhiteSpace(ev.MockSectionAttemptId) && sectionIdSet.Contains(ev.MockSectionAttemptId!)
                ? ev.MockSectionAttemptId
                : null;
            var occurredAt = ev.OccurredAt == default ? now : ev.OccurredAt;
            // Guard against client clock skew: reject far-future timestamps.
            if (occurredAt > now.AddMinutes(5)) occurredAt = now;
            var metadata = ev.Metadata is { Count: > 0 }
                ? JsonSupport.Serialize(ev.Metadata)
                : "{}";

            db.MockProctoringEvents.Add(new MockProctoringEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                MockAttemptId = attempt.Id,
                MockSectionAttemptId = sectionId,
                Kind = ev.Kind,
                Severity = severity,
                OccurredAt = occurredAt,
                MetadataJson = metadata,
            });
            accepted++;
        }

        if (accepted > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        return new { ok = true, accepted, dropped, capacityRemaining = capacity - accepted };
    }

    public async Task<object> GetMockReportAsync(string userId, string reportId, CancellationToken ct)
    {
        var row = await db.MockReports.AsNoTracking()
            .Join(db.MockAttempts.AsNoTracking().Where(x => x.UserId == userId),
                report => report.MockAttemptId,
                attempt => attempt.Id,
                (report, attempt) => new { report, attempt })
            .FirstOrDefaultAsync(x => x.report.Id == reportId, ct)
            ?? throw ApiException.NotFound("mock_report_not_found", "Mock report not found.");

        var payload = JsonSupport.Deserialize<Dictionary<string, object?>>(row.report.PayloadJson, new Dictionary<string, object?>());
        payload["id"] = row.report.Id;
        payload["reportId"] = row.report.Id;
        payload["state"] = ToAsyncState(row.report.State);
        payload["generatedAt"] = row.report.GeneratedAt;
        payload["studyPlanUpdateCta"] = new { label = "Update study plan", route = "/study-plan" };
        payload["isOfficialScore"] = false;
        payload["aiTrustBoundary"] = new
        {
            disclaimer = "Mock exam scores are generated by AI and should be treated as practice guidance, not official exam results.",
            provenanceLabel = "AI-assisted mock estimate",
            methodLabel = "Auto-scored mock exam"
        };
        await SeedMockRemediationStudyPlanAsync(userId, row.attempt, row.report, payload, ct);
        await EnrichMockReportPayloadAsync(row.attempt, row.report, payload, ct);
        return payload;
    }

    public async Task<object> ListMockBookingsAsync(string userId, CancellationToken ct)
    {
        await EnsureUserAsync(userId, ct);
        var rows = await db.MockBookings.AsNoTracking()
            .Include(x => x.MockBundle)
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.ScheduledStartAt)
            .Take(50)
            .ToListAsync(ct);
        return new { items = rows.Select(ProjectBookingLearner).ToArray() };
    }

    public async Task<object> CreateMockBookingAsync(string userId, MockBookingCreateRequest request, CancellationToken ct)
    {
        await EnsureUserAsync(userId, ct);
        var bundle = await db.MockBundles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.MockBundleId && x.Status == ContentStatus.Published, ct)
            ?? throw ApiException.NotFound("mock_bundle_not_found", "Choose a published mock bundle before booking.");

        if (request.ScheduledStartAt < DateTimeOffset.UtcNow.AddMinutes(15))
        {
            throw ApiException.Validation(
                "mock_booking_too_soon",
                "Schedule at least 15 minutes ahead so checks and reminders can run.",
                [new ApiFieldError("scheduledStartAt", "too_soon", "Choose a later time.")]);
        }

        var now = DateTimeOffset.UtcNow;
        var deliveryMode = NormalizeDeliveryMode(request.DeliveryMode);
        var booking = new MockBooking
        {
            Id = $"mock-booking-{Guid.NewGuid():N}",
            UserId = userId,
            MockBundleId = bundle.Id,
            ScheduledStartAt = request.ScheduledStartAt.ToUniversalTime(),
            TimezoneIana = string.IsNullOrWhiteSpace(request.TimezoneIana) ? "UTC" : request.TimezoneIana.Trim(),
            Status = MockBookingStatuses.Scheduled,
            ConsentToRecording = request.ConsentToRecording,
            DeliveryMode = deliveryMode,
            LearnerNotes = string.IsNullOrWhiteSpace(request.LearnerNotes) ? null : request.LearnerNotes.Trim(),
            LiveRoomState = MockLiveRoomStates.Waiting,
            ZoomMeetingId = $"sandbox-{Guid.NewGuid():N}"[..24],
            ZoomJoinUrl = $"/mocks/speaking-room/{{bookingId}}",
            CreatedAt = now,
            UpdatedAt = now
        };
        booking.ZoomJoinUrl = $"/mocks/speaking-room/{Uri.EscapeDataString(booking.Id)}";
        db.MockBookings.Add(booking);
        RecordEvent(userId, "mock_booking_created", new { bookingId = booking.Id, bundleId = bundle.Id, deliveryMode, booking.ScheduledStartAt });
        await db.SaveChangesAsync(ct);
        return ProjectBookingLearner(booking, bundle);
    }

    public async Task<object> UpdateMockBookingAsync(string userId, string bookingId, MockBookingUpdateRequest request, CancellationToken ct)
    {
        var booking = await db.MockBookings
            .Include(x => x.MockBundle)
            .FirstOrDefaultAsync(x => x.Id == bookingId && x.UserId == userId, ct)
            ?? throw ApiException.NotFound("mock_booking_not_found", "Mock booking not found.");

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            throw ApiException.Validation(
                "booking_status_readonly",
                "Learners cannot change booking status through this endpoint. Use reschedule or cancel actions instead.",
                [new ApiFieldError("status", "readonly", "Booking status is controlled by the booking lifecycle service.")]);
        }
        if (booking.Status is MockBookingStatuses.Completed or MockBookingStatuses.Cancelled or MockBookingStatuses.LearnerNoShow or MockBookingStatuses.TutorNoShow)
        {
            throw ApiException.Validation("booking_finalized", "This booking can no longer be changed.");
        }
        if (request.ScheduledStartAt.HasValue && request.ScheduledStartAt.Value != booking.ScheduledStartAt)
        {
            if (booking.RescheduleCount >= MockBookingService.MaxReschedulesPerBooking)
            {
                throw ApiException.Validation("reschedule_cap_reached",
                    $"Bookings can be rescheduled up to {MockBookingService.MaxReschedulesPerBooking} times.");
            }
            if (request.ScheduledStartAt.Value <= DateTimeOffset.UtcNow.AddHours(MockBookingService.MinLeadTimeHours))
            {
                throw ApiException.Validation("lead_time_too_short",
                    $"Reschedule must land at least {MockBookingService.MinLeadTimeHours} hours in the future.");
            }
            booking.ScheduledStartAt = request.ScheduledStartAt.Value.ToUniversalTime();
            booking.RescheduleCount += 1;
        }
        if (request.TimezoneIana is not null)
        {
            booking.TimezoneIana = string.IsNullOrWhiteSpace(request.TimezoneIana) ? booking.TimezoneIana : request.TimezoneIana.Trim();
        }
        if (request.ConsentToRecording.HasValue) booking.ConsentToRecording = request.ConsentToRecording.Value;
        if (request.LearnerNotes is not null) booking.LearnerNotes = request.LearnerNotes;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        RecordEvent(userId, "mock_booking_updated", new { bookingId = booking.Id, booking.Status, booking.ScheduledStartAt });
        await db.SaveChangesAsync(ct);
        return ProjectBookingLearner(booking, booking.MockBundle);
    }

    public async Task<object> ListAdminMockBookingsAsync(string? status, CancellationToken ct)
    {
        var query = db.MockBookings.AsNoTracking()
            .Include(x => x.MockBundle)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = NormalizeBookingStatus(status);
            query = query.Where(x => x.Status == normalized);
        }
        var rows = await query.OrderBy(x => x.ScheduledStartAt).Take(200).ToListAsync(ct);
        return new { items = rows.Select(ProjectBookingAdmin).ToArray() };
    }

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
        var scored = reports.Select(x => ParsePayloadOverallScore(x.PayloadJson)).Where(x => x.HasValue).Select(x => x!.Value).ToList();
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
                score = ParsePayloadOverallScore(x.report.PayloadJson),
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

    public async Task<object> ListExpertMockBookingsAsync(string expertId, CancellationToken ct)
    {
        var rows = await db.MockBookings.AsNoTracking()
            .Include(x => x.MockBundle)
            .Where(x => x.AssignedTutorId == null || x.AssignedTutorId == expertId || x.AssignedInterlocutorId == expertId)
            .OrderBy(x => x.ScheduledStartAt)
            .Take(100)
            .ToListAsync(ct);
        return new { items = rows.Select(ProjectBookingExpert).ToArray() };
    }

    public async Task<object> ReportMockLeakAsync(string userId, MockLeakReportRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.MockBundleId) && string.IsNullOrWhiteSpace(request.MockAttemptId))
        {
            throw ApiException.Validation(
                "mock_leak_report_target_required",
                "Select the mock bundle or attempt you are reporting.",
                [new ApiFieldError("mockBundleId", "required", "Provide a bundle or attempt id.")]);
        }

        MockAttempt? attempt = null;
        string? bundleId = string.IsNullOrWhiteSpace(request.MockBundleId) ? null : request.MockBundleId.Trim();
        if (!string.IsNullOrWhiteSpace(request.MockAttemptId))
        {
            attempt = await db.MockAttempts.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.MockAttemptId && x.UserId == userId, ct)
                ?? throw ApiException.NotFound("mock_attempt_not_found", "Mock attempt not found.");
            bundleId ??= attempt.MockBundleId;
        }

        var notes = JsonSupport.Serialize(new
        {
            reason = request.Reason?.Trim(),
            evidenceUrl = request.EvidenceUrl?.Trim(),
            pageOrQuestion = request.PageOrQuestion?.Trim()
        });
        var review = new MockContentReview
        {
            Id = $"mock-review-{Guid.NewGuid():N}",
            MockBundleId = bundleId,
            MockAttemptId = attempt?.Id ?? request.MockAttemptId,
            ReportedByUserId = userId,
            ReviewType = "leak_report",
            Severity = "high",
            Status = "open",
            Notes = notes,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.MockContentReviews.Add(review);
        RecordEvent(userId, "mock_leak_reported", new { reviewId = review.Id, bundleId, request.MockAttemptId });
        await db.SaveChangesAsync(ct);
        return new { id = review.Id, status = review.Status, severity = review.Severity };
    }

    // Mocks Wave 8 — admin leak-report queue.
    private static readonly HashSet<string> LeakReportStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "open", "investigating", "resolved", "dismissed" };

    private static readonly HashSet<string> TerminalLeakReportStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "resolved", "dismissed" };

    public async Task<IReadOnlyList<MockLeakReportSummary>> ListLeakReportsAsync(
        string? status, int limit, CancellationToken ct)
    {
        var clamped = limit <= 0 ? 50 : Math.Min(limit, 200);
        var query = db.MockContentReviews.AsNoTracking()
            .Include(x => x.MockBundle)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalised = status.Trim().ToLowerInvariant();
            if (!LeakReportStatuses.Contains(normalised))
            {
                throw ApiException.Validation(
                    "mock_leak_report_status_invalid",
                    "Status must be one of open, investigating, resolved, dismissed.");
            }
            query = query.Where(x => x.Status == normalised);
        }

        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(clamped)
            .ToListAsync(ct);

        var reporterIds = rows
            .Select(x => x.ReportedByUserId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToArray();
        var displayNames = reporterIds.Length == 0
            ? new Dictionary<string, string>(0)
            : await db.Users.AsNoTracking()
                .Where(u => reporterIds.Contains(u.Id))
                .Select(u => new { u.Id, u.DisplayName })
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return rows.Select(row => ToLeakReportSummary(row, displayNames)).ToArray();
    }

    public async Task<MockLeakReportSummary> UpdateLeakReportAsync(
        string adminId,
        string id,
        MockLeakReportUpdateRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Status)
            || !LeakReportStatuses.Contains(request.Status.Trim().ToLowerInvariant()))
        {
            throw ApiException.Validation(
                "mock_leak_report_status_invalid",
                "Status must be one of open, investigating, resolved, dismissed.");
        }
        var nextStatus = request.Status.Trim().ToLowerInvariant();

        var review = await db.MockContentReviews
            .Include(x => x.MockBundle)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw ApiException.NotFound("mock_leak_report_not_found", "Leak report not found.");

        if (TerminalLeakReportStatuses.Contains(review.Status)
            && !string.Equals(review.Status, nextStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "mock_leak_report_status_locked",
                $"Report is already {review.Status} and cannot transition to {nextStatus}.");
        }

        var now = DateTimeOffset.UtcNow;
        var previousStatus = review.Status;
        var note = string.IsNullOrWhiteSpace(request.ResolutionNote) ? null : request.ResolutionNote.Trim();

        // The notes column already stores the original learner-submitted JSON
        // payload (reason, evidenceUrl, pageOrQuestion). To preserve that
        // payload while recording the admin resolution note we merge under a
        // dedicated key so the original report is never overwritten.
        if (note is not null)
        {
            var payload = string.IsNullOrWhiteSpace(review.Notes)
                ? new Dictionary<string, object?>()
                : JsonSupport.Deserialize<Dictionary<string, object?>>(review.Notes, new Dictionary<string, object?>());
            payload["adminResolutionNote"] = note;
            payload["adminResolutionAt"] = now;
            payload["adminResolutionBy"] = adminId;
            review.Notes = JsonSupport.Serialize(payload);
        }

        review.Status = nextStatus;
        if (TerminalLeakReportStatuses.Contains(nextStatus))
        {
            review.ResolvedAt = now;
            review.ResolvedByAdminId = adminId;
        }
        else
        {
            review.ResolvedAt = null;
            review.ResolvedByAdminId = null;
        }

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            OccurredAt = now,
            ActorId = adminId,
            ActorName = adminId,
            Action = "MockLeakReport.Updated",
            ResourceType = "MockContentReview",
            ResourceId = review.Id,
            Details = JsonSupport.Serialize(new
            {
                previousStatus,
                nextStatus,
                hasResolutionNote = note is not null
            })
        });

        await db.SaveChangesAsync(ct);

        var displayNames = string.IsNullOrWhiteSpace(review.ReportedByUserId)
            ? new Dictionary<string, string>(0)
            : await db.Users.AsNoTracking()
                .Where(u => u.Id == review.ReportedByUserId)
                .Select(u => new { u.Id, u.DisplayName })
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return ToLeakReportSummary(review, displayNames);
    }

    private static MockLeakReportSummary ToLeakReportSummary(
        MockContentReview row,
        IReadOnlyDictionary<string, string> displayNames)
    {
        string? reasonCode = null;
        string? details = null;
        string? evidenceUrl = null;
        string? pageOrQuestion = null;
        string? resolutionNote = null;
        if (!string.IsNullOrWhiteSpace(row.Notes))
        {
            var payload = JsonSupport.Deserialize<Dictionary<string, object?>>(
                row.Notes, new Dictionary<string, object?>());
            reasonCode = payload.TryGetValue("reason", out var reason) ? reason?.ToString() : null;
            evidenceUrl = payload.TryGetValue("evidenceUrl", out var url) ? url?.ToString() : null;
            pageOrQuestion = payload.TryGetValue("pageOrQuestion", out var pq) ? pq?.ToString() : null;
            resolutionNote = payload.TryGetValue("adminResolutionNote", out var rn) ? rn?.ToString() : null;
            details = reasonCode;
        }

        string? displayName = null;
        if (!string.IsNullOrWhiteSpace(row.ReportedByUserId)
            && displayNames.TryGetValue(row.ReportedByUserId!, out var dn))
        {
            displayName = dn;
        }

        return new MockLeakReportSummary(
            Id: row.Id,
            BundleId: row.MockBundleId,
            BundleTitle: row.MockBundle?.Title,
            AttemptId: row.MockAttemptId,
            Severity: row.Severity,
            Status: row.Status,
            ReasonCode: reasonCode,
            Details: details,
            EvidenceUrl: evidenceUrl,
            PageOrQuestion: pageOrQuestion,
            ReportedByUserId: row.ReportedByUserId,
            ReportedByUserDisplayName: displayName,
            CreatedAt: row.CreatedAt,
            ResolvedAt: row.ResolvedAt,
            ResolvedByAdminId: row.ResolvedByAdminId,
            ResolutionNote: resolutionNote);
    }

    public async Task<object> GetDiagnosticStudyPathAsync(string userId, CancellationToken ct)
    {
        await EnsureUserAsync(userId, ct);
        var diagnosticAttemptIds = db.MockAttempts.AsNoTracking()
            .Where(x => x.UserId == userId && x.MockType == MockTypes.Diagnostic)
            .Select(x => x.Id);
        var report = await db.MockReports.AsNoTracking()
            .Where(x => diagnosticAttemptIds.Contains(x.MockAttemptId) && x.State == AsyncState.Completed)
            .OrderByDescending(x => x.GeneratedAt)
            .FirstOrDefaultAsync(ct);
        var payload = report is null
            ? new Dictionary<string, object?>()
            : JsonSupport.Deserialize<Dictionary<string, object?>>(report.PayloadJson, new Dictionary<string, object?>());
        var weakness = ReadWeakestCriterion(payload);
        var items = await db.StudyPlanItems.AsNoTracking()
            .Join(db.StudyPlans.AsNoTracking().Where(x => x.UserId == userId),
                item => item.StudyPlanId,
                plan => plan.Id,
                (item, plan) => item)
            .Where(x => x.ContentId != null && x.ContentId.StartsWith("mock-remediation:"))
            .OrderBy(x => x.DueDate)
            .Take(7)
            .ToListAsync(ct);

        return new
        {
            diagnosticCompleted = report is not null,
            reportId = report?.Id,
            weakness,
            generatedAt = report?.GeneratedAt,
            items = items.Select(x => new
            {
                id = x.Id,
                title = x.Title,
                subtest = x.SubtestCode,
                dueDate = x.DueDate,
                durationMinutes = x.DurationMinutes,
                rationale = x.Rationale,
                route = RouteForSubtest(x.SubtestCode)
            }).ToArray(),
            fallback = report is null
                ? new
                {
                    title = "Start a diagnostic mock first",
                    route = "/mocks/setup?type=diagnostic",
                    description = "Diagnostic access is plan-configurable. Start from the mock setup page when your plan includes it."
                }
                : null
        };
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
        var subtest = MockTypes.IsSubShape(mockType) ? NormalizeSubtest(request.SubtestCode) : null;
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
            Difficulty = NormalizeDifficulty(request.Difficulty),
            SourceStatus = NormalizeSourceStatus(request.SourceStatus),
            QualityStatus = NormalizeQualityStatus(request.QualityStatus),
            ReleasePolicy = NormalizeReleasePolicy(request.ReleasePolicy),
            TopicTagsCsv = request.TopicTagsCsv ?? string.Empty,
            SkillTagsCsv = request.SkillTagsCsv ?? string.Empty,
            WatermarkEnabled = request.WatermarkEnabled ?? true,
            RandomiseQuestions = request.RandomiseQuestions ?? false,
            EstimatedDurationMinutes = ComputeBundleDefaultDuration(mockType, subtest),
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
            bundle.SubtestCode = MockTypes.IsSubShape(bundle.MockType) ? NormalizeSubtest(request.SubtestCode) : null;
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
        if (request.Difficulty is not null) bundle.Difficulty = NormalizeDifficulty(request.Difficulty);
        if (request.SourceStatus is not null) bundle.SourceStatus = NormalizeSourceStatus(request.SourceStatus);
        if (request.QualityStatus is not null) bundle.QualityStatus = NormalizeQualityStatus(request.QualityStatus);
        if (request.ReleasePolicy is not null) bundle.ReleasePolicy = NormalizeReleasePolicy(request.ReleasePolicy);
        if (request.TopicTagsCsv is not null) bundle.TopicTagsCsv = request.TopicTagsCsv;
        if (request.SkillTagsCsv is not null) bundle.SkillTagsCsv = request.SkillTagsCsv;
        if (request.WatermarkEnabled.HasValue) bundle.WatermarkEnabled = request.WatermarkEnabled.Value;
        if (request.RandomiseQuestions.HasValue) bundle.RandomiseQuestions = request.RandomiseQuestions.Value;
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
        if (MockTypes.IsSubShape(bundle.MockType) && !string.Equals(bundle.SubtestCode, subtest, StringComparison.OrdinalIgnoreCase))
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
        else if (MockTypes.IsSubShape(mockType))
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
            MockTypes.IsSubShape(mockType)
                ? $"No published {ToDisplaySubtest(subtest ?? "reading")} mock bundle is available yet."
                : $"No published {MockTypes.Label(mockType).ToLowerInvariant()} bundle is available yet.");
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
            LearnerNotes = $"Tutor review requested from mock attempt {attempt.Id}.",
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

        var requiredSequence = RequiredSubtestSequence(bundle.MockType);
        if (requiredSequence is not null)
        {
            var actual = sections.Select(x => x.SubtestCode).ToArray();
            if (!actual.SequenceEqual(requiredSequence, StringComparer.OrdinalIgnoreCase))
            {
                var label = MockTypes.Label(bundle.MockType);
                var expected = string.Join(", ", requiredSequence.Select(s => char.ToUpperInvariant(s[0]) + s[1..]));
                errors.Add(new ApiFieldError("sections", "wrong_order", $"{label} bundles must contain {expected} in that order."));
            }
        }
        else if (MockTypes.IsSubShape(bundle.MockType))
        {
            if (sections.Count != 1 || !string.Equals(sections[0].SubtestCode, bundle.SubtestCode, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ApiFieldError("sections", "subtest_required", "Sub-test mock bundles require exactly one section matching the selected sub-test."));
            }
        }
        else
        {
            // Diagnostic / Remedial / other flexible-shape bundles: require at least one section.
            if (sections.Count == 0)
            {
                errors.Add(new ApiFieldError("sections", "sections_required", "This mock requires at least one section before publishing."));
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
            difficulty = bundle.Difficulty,
            sourceStatus = bundle.SourceStatus,
            qualityStatus = bundle.QualityStatus,
            releasePolicy = bundle.ReleasePolicy,
            topicTags = SplitCsv(bundle.TopicTagsCsv),
            skillTags = SplitCsv(bundle.SkillTagsCsv),
            watermarkEnabled = bundle.WatermarkEnabled,
            randomiseQuestions = bundle.RandomiseQuestions,
            isRecommended = latestAttempt is null && MockTypes.IsFullShape(bundle.MockType),
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
        difficulty = bundle.Difficulty,
        sourceStatus = bundle.SourceStatus,
        qualityStatus = bundle.QualityStatus,
        releasePolicy = bundle.ReleasePolicy,
        topicTags = SplitCsv(bundle.TopicTagsCsv),
        skillTags = SplitCsv(bundle.SkillTagsCsv),
        watermarkEnabled = bundle.WatermarkEnabled,
        randomiseQuestions = bundle.RandomiseQuestions,
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
        difficulty = bundle.Difficulty,
        sourceStatus = bundle.SourceStatus,
        qualityStatus = bundle.QualityStatus,
        releasePolicy = bundle.ReleasePolicy,
        topicTagsCsv = bundle.TopicTagsCsv,
        skillTagsCsv = bundle.SkillTagsCsv,
        watermarkEnabled = bundle.WatermarkEnabled,
        randomiseQuestions = bundle.RandomiseQuestions,
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

    private static object ProjectBookingLearner(MockBooking booking)
        => ProjectBookingLearner(booking, booking.MockBundle);

    private static object ProjectBookingLearner(MockBooking booking, MockBundle? bundle) => new
    {
        id = booking.Id,
        bookingId = booking.Id,
        mockBundleId = booking.MockBundleId,
        mockAttemptId = booking.MockAttemptId,
        title = bundle?.Title ?? "Scheduled mock",
        scheduledStartAt = booking.ScheduledStartAt,
        timezoneIana = booking.TimezoneIana,
        status = booking.Status,
        deliveryMode = booking.DeliveryMode,
        liveRoomState = booking.LiveRoomState,
        consentToRecording = booking.ConsentToRecording,
        rescheduleCount = booking.RescheduleCount,
        joinUrl = booking.ZoomJoinUrl,
        learnerNotes = booking.LearnerNotes,
        releasePolicy = bundle?.ReleasePolicy ?? MockReleasePolicies.Instant,
        candidateCardVisible = true,
        interlocutorCardVisible = false
    };

    private static object ProjectBookingAdmin(MockBooking booking) => new
    {
        id = booking.Id,
        bookingId = booking.Id,
        userId = booking.UserId,
        mockBundleId = booking.MockBundleId,
        mockBundleTitle = booking.MockBundle?.Title,
        scheduledStartAt = booking.ScheduledStartAt,
        timezoneIana = booking.TimezoneIana,
        status = booking.Status,
        assignedTutorId = booking.AssignedTutorId,
        assignedInterlocutorId = booking.AssignedInterlocutorId,
        deliveryMode = booking.DeliveryMode,
        liveRoomState = booking.LiveRoomState,
        consentToRecording = booking.ConsentToRecording,
        rescheduleCount = booking.RescheduleCount,
        zoomMeetingId = booking.ZoomMeetingId,
        learnerNotes = booking.LearnerNotes,
        releasePolicy = booking.MockBundle?.ReleasePolicy ?? MockReleasePolicies.Instant
    };

    private static object ProjectBookingExpert(MockBooking booking) => new
    {
        id = booking.Id,
        bookingId = booking.Id,
        learnerId = booking.UserId,
        mockBundleId = booking.MockBundleId,
        mockBundleTitle = booking.MockBundle?.Title,
        scheduledStartAt = booking.ScheduledStartAt,
        timezoneIana = booking.TimezoneIana,
        status = booking.Status,
        liveRoomState = booking.LiveRoomState,
        startUrl = booking.ZoomStartUrl,
        joinUrl = booking.ZoomJoinUrl,
        consentToRecording = booking.ConsentToRecording,
        candidateCardVisible = true,
        interlocutorCardVisible = true,
        learnerNotes = booking.LearnerNotes
    };

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
            var score = IntValue(st, "scaledScore") ?? ParseScore(StringValue(st, "score"));
            var advisory = score.HasValue
                ? OetScoring.AdvisoryTier(score.Value)
                : null;
            return new
            {
                subtest = name,
                scaledScore = score,
                grade = score.HasValue ? OetScoring.OetGradeLetterFromScaled(score.Value) : null,
                rag = advisory?.Tier ?? "pending",
                message = advisory?.Message ?? "Awaiting scored evidence or teacher review.",
                passThreshold = advisory?.PassThreshold
            };
        }).ToArray();

        payload["perModuleReadiness"] = perModuleReadiness;
        payload["partScores"] = subTests.Select(st => new
        {
            subtest = StringValue(st, "name") ?? StringValue(st, "subtest") ?? "Mock",
            rawScore = StringValue(st, "rawScore") ?? "N/A",
            scaledScore = IntValue(st, "scaledScore"),
            grade = StringValue(st, "grade"),
            state = StringValue(st, "state") ?? "completed"
        }).ToArray();
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

    private static string NormalizeSubtestOrDefault(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return FullMockOrder.Contains(normalized ?? string.Empty) ? normalized! : "reading";
    }

    private static string RouteForSubtest(string? subtest) => NormalizeSubtestOrDefault(subtest) switch
    {
        "listening" => "/listening",
        "reading" => "/reading/practice",
        "writing" => "/writing/library",
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
    /// Builds a lightweight readiness advisory object for the Mock Center based on the latest report's
    /// overall scaled score. Anchors on OET pass thresholds (350 = B grade) per docs/SCORING.md.
    /// Returns null when no scored report exists yet.
    /// </summary>
    private static object? BuildReadinessAdvisory(Dictionary<string, object?>? latestReport)
    {
        if (latestReport is null) return null;
        if (!latestReport.TryGetValue("overallScore", out var scoreRaw) || scoreRaw is null) return null;
        if (!int.TryParse(scoreRaw.ToString(), out var overall)) return null;

        // Canonical OET pass anchor = 350/500 (docs/SCORING.md). Tiering is advisory copy only and is
        // centralised in OetScoring.AdvisoryTier — never compare scaled scores to 350/300/400 inline.
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


    private static string BuildLaunchRoute(MockAttempt attempt, MockBundleSection section, string? sectionAttemptId, string? contentAttemptId = null)
    {
        var attemptId = attempt.Id;
        var query = $"mockAttemptId={Uri.EscapeDataString(attemptId)}";
        if (!string.IsNullOrWhiteSpace(sectionAttemptId))
        {
            query += $"&mockSectionId={Uri.EscapeDataString(sectionAttemptId)}";
        }
        if (!string.IsNullOrWhiteSpace(contentAttemptId))
        {
            query += $"&attemptId={Uri.EscapeDataString(contentAttemptId)}";
        }
        query += $"&paperId={Uri.EscapeDataString(section.ContentPaperId)}";
        query += $"&mockMode={Uri.EscapeDataString(attempt.Mode)}";
        query += $"&strictness={Uri.EscapeDataString(attempt.Strictness)}";
        query += $"&deliveryMode={Uri.EscapeDataString(attempt.DeliveryMode)}";
        query += $"&strictTimer={(attempt.StrictTimer ? "true" : "false")}";

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
            || selection == "current_subtest" && MockTypes.IsSubShape(mockType) && string.Equals(subtest, sectionSubtest, StringComparison.OrdinalIgnoreCase);

    private static int ReviewCost(string selection, string mockType, string? subType)
        => selection switch
        {
            "writing_and_speaking" => 2,
            "writing" or "speaking" => 1,
            "current_subtest" when MockTypes.IsSubShape(mockType) && ProductiveSubtests.Contains(subType ?? string.Empty) => 1,
            _ => 0
        };

    private static string NormalizeMockReviewSelection(string mockType, string? subType, bool includeReview, string? reviewSelection)
    {
        var requestedSelection = (reviewSelection ?? string.Empty).Trim().ToLowerInvariant();
        if (MockTypes.IsFullShape(mockType))
        {
            // LRW excludes Speaking entirely; restrict review selection to writing-only options.
            var allowed = MockTypes.ExcludesSpeaking(mockType)
                ? new HashSet<string>(["none", "writing"], StringComparer.Ordinal)
                : new HashSet<string>(["none", "writing", "speaking", "writing_and_speaking"], StringComparer.Ordinal);
            if (allowed.Contains(requestedSelection)) return requestedSelection;
            return includeReview
                ? (MockTypes.ExcludesSpeaking(mockType) ? "writing" : "writing_and_speaking")
                : "none";
        }

        var productiveSubtest = ProductiveSubtests.Contains(subType ?? string.Empty);
        if (!productiveSubtest) return "none";
        return requestedSelection == "current_subtest"
            ? "current_subtest"
            : includeReview ? "current_subtest" : "none";
    }

    /// <summary>
    /// Default total duration when an admin creates a bundle but has not yet authored sections.
    /// Wave 1: full + final-readiness sum all four subtests, LRW sums the first three (no speaking),
    /// sub-shape uses the single sub-test’s default. Diagnostic / Remedial default to 60 min.
    /// </summary>
    private static int ComputeBundleDefaultDuration(string mockType, string? subtest) => mockType switch
    {
        MockTypes.Full or MockTypes.FinalReadiness => FullMockOrder.Sum(DefaultTimeLimit),
        MockTypes.Lrw => new[] { "listening", "reading", "writing" }.Sum(DefaultTimeLimit),
        MockTypes.Sub or MockTypes.Part or MockTypes.Remedial => DefaultTimeLimit(subtest ?? "reading"),
        MockTypes.Diagnostic => 60,
        _ => 60,
    };

    private static string NormalizeMockType(string? value)
    {
        var normalized = (value ?? MockTypes.Full).Trim().ToLowerInvariant();
        if (MockTypes.IsValid(normalized)) return normalized;
        // Tolerate the historical alias "subtest" returned by some legacy clients.
        if (string.Equals(normalized, "subtest", StringComparison.Ordinal)) return MockTypes.Sub;
        throw ApiException.Validation(
            "invalid_mock_type",
            $"Mock type must be one of: {string.Join(", ", MockTypes.All)}.",
            [new ApiFieldError("mockType", "invalid", "Use a supported OET mock-type token.")]);
    }

    private static string NormalizeDeliveryMode(string? value)
    {
        var normalized = (value ?? MockDeliveryModes.Computer).Trim().ToLowerInvariant();
        return MockDeliveryModes.IsValid(normalized) ? normalized : MockDeliveryModes.Computer;
    }

    private static string NormalizeStrictness(string? value, string mockType)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return MockStrictness.IsValid(normalized) ? normalized : MockTypes.DefaultStrictness(mockType);
    }

    private static string NormalizeReleasePolicy(string? value)
    {
        var normalized = (value ?? MockReleasePolicies.Instant).Trim().ToLowerInvariant();
        return MockReleasePolicies.IsValid(normalized) ? normalized : MockReleasePolicies.Instant;
    }

    private static string NormalizeSourceStatus(string? value)
    {
        var normalized = (value ?? MockSourceStatuses.NeedsReview).Trim().ToLowerInvariant();
        return MockSourceStatuses.IsValid(normalized) ? normalized : MockSourceStatuses.NeedsReview;
    }

    private static string NormalizeQualityStatus(string? value)
    {
        var normalized = (value ?? MockQualityStatuses.Draft).Trim().ToLowerInvariant();
        return MockQualityStatuses.IsValid(normalized) ? normalized : MockQualityStatuses.Draft;
    }

    private static string NormalizeBookingStatus(string? value)
    {
        var normalized = (value ?? MockBookingStatuses.Scheduled).Trim().ToLowerInvariant();
        return MockBookingStatuses.IsValid(normalized) ? normalized : MockBookingStatuses.Scheduled;
    }

    private static string NormalizeDifficulty(string? value)
    {
        var normalized = (value ?? "exam_ready").Trim().ToLowerInvariant().Replace(" ", "_", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(normalized) ? "exam_ready" : normalized[..Math.Min(normalized.Length, 32)];
    }

    private static string[] SplitCsv(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Sub-test sequence enforced for the given mock-type at publish-gate time.
    /// Full + Final-readiness require all four. LRW excludes Speaking. Diagnostic /
    /// Remedial / Sub / Part have flexible content shapes validated separately.
    /// </summary>
    private static IReadOnlyList<string>? RequiredSubtestSequence(string mockType) => mockType switch
    {
        MockTypes.Full or MockTypes.FinalReadiness => FullMockOrder,
        MockTypes.Lrw => ["listening", "reading", "writing"],
        _ => null,
    };

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
