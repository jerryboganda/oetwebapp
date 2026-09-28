using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Services;

public sealed partial class MockService
{
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
        var latestReportHasGovernedScore = latestReport is not null && ContainsGovernedScore(latestReport);
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
                    : latestReportHasGovernedScore
                        ? "Your latest report contains Reading/Listening evidence. Review owner-approved per-assessment results before planning the next mock."
                        : $"Your latest report scored {latestReport.GetValueOrDefault("overallScore")?.ToString() ?? "an updated"} overall. Run another mock to confirm the gains.",
                route = firstFullRoute,
                latestOverallScore = latestReportHasGovernedScore ? null : latestReport?.GetValueOrDefault("overallScore")?.ToString(),
                latestOverallGrade = latestReportHasGovernedScore ? null : latestReport?.GetValueOrDefault("overallGrade")?.ToString(),
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
        // The learner's own profession so the setup page can preselect it
        // instead of defaulting to whichever profession sorts first.
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
            learnerProfession,
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

        // Billing: an attempt must consume exactly one allowance. AI-package
        // customers spend from their package's mock_exams pool; everyone else
        // spends a BillingAddOn mock credit (the same bucket the setup page
        // displays and gates on). Before this, the add-on ledger was never
        // debited at all — credits gated the UI but were never consumed.
        var aiPackageCovered = false;
        if (aiPackageCreditService is not null
            && (string.Equals(mockType, MockTypes.Full, StringComparison.OrdinalIgnoreCase)
                || string.Equals(mockType, MockTypes.FinalReadiness, StringComparison.OrdinalIgnoreCase)))
        {
            var debit = await aiPackageCreditService.DeductMockAsync(userId, id, ct);
            if (!debit.Debited)
            {
                throw ApiException.PaymentRequired(
                    debit.ErrorCode ?? "no_mock_exams",
                    debit.ErrorMessage ?? "You have no mock exams remaining. Purchase a package to continue.");
            }
            aiPackageCovered = !debit.Bypassed;
        }

        if (!aiPackageCovered && mockEntitlementService is not null)
        {
            // Premium/trial subscribers no-op inside DebitAsync (unlimited).
            var creditDebit = await mockEntitlementService.DebitAsync(userId, mockType, id, ct);
            if (!creditDebit.Success)
            {
                throw ApiException.PaymentRequired(
                    creditDebit.Reason,
                    creditDebit.Message);
            }
        }

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
            if (mockEntitlementService is not null)
            {
                await mockEntitlementService.CommitAsync(userId, attempt.MockType, attempt.Id, ct);
            }

            // The webcam/environment preflight only ran client-side, so an API
            // caller could start a strict section with no check at all and
            // nothing was recorded. A hard server block is not possible (the
            // browser owns the camera), but the absence is now a durable
            // proctoring event tutors/admins see on the integrity summary.
            var strictAttempt = attempt.Strictness is MockStrictness.Exam or MockStrictness.FinalReadiness;
            var preflightConfirmed = request.ClientState is not null
                && request.ClientState.TryGetValue("preflight", out var preflight)
                && string.Equals(preflight?.ToString(), "passed", StringComparison.OrdinalIgnoreCase);
            if (strictAttempt && !preflightConfirmed)
            {
                db.MockProctoringEvents.Add(new MockProctoringEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    MockAttemptId = attempt.Id,
                    MockSectionAttemptId = section.Id,
                    Kind = MockProctoringKinds.SectionStartedWithoutPreflight,
                    Severity = "warning",
                    MetadataJson = JsonSupport.Serialize(new { subtest = section.SubtestCode }),
                    OccurredAt = now,
                });
            }

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
        // Phase 6 closure — fast-path: a previous successful complete
        // already wrote an IdempotencyRecord. Return its cached projection
        // verbatim so the second click / second-tab path is a no-op.
        var idempotencyKey = $"{userId}:{mockAttemptId}:{sectionId}";
        var existingRecord = await db.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Scope == CompleteSectionIdempotencyScope && r.Key == idempotencyKey, ct);
        if (existingRecord is not null)
        {
            var cached = TryDeserializeCompleteSectionResult(existingRecord.ResponseJson);
            if (cached is not null) return cached;
            // Fall through to recompute when cached JSON is unreadable.
        }

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

        // Productive sections must also show work. Reading/Listening already
        // require a submitted content attempt above; before this check a
        // learner could mark Writing and Speaking "complete" from the mock
        // dashboard without ever opening the workspace (proctoring logged it
        // as advisory but nothing blocked).
        await RequireProductiveSectionEvidenceAsync(userId, attempt, section, request, ct);

        var now = DateTimeOffset.UtcNow;
        section.State = AttemptState.Completed;
        section.SubmittedAt ??= now;
        section.CompletedAt = now;
        section.ContentAttemptId = canonicalEvidence?.ContentAttemptId
            ?? (string.IsNullOrWhiteSpace(request.ContentAttemptId) ? section.ContentAttemptId : request.ContentAttemptId.Trim());
        var governedScore = section.SubtestCode.Trim().ToLowerInvariant() is "reading" or "listening";
        section.RawScore = canonicalEvidence?.RawScore
            ?? (governedScore ? null : request.RawScore ?? section.RawScore);
        section.RawScoreMax = canonicalEvidence?.RawScoreMax
            ?? (governedScore ? null : request.RawScoreMax ?? section.RawScoreMax);
        section.ScaledScore = canonicalEvidence?.ScaledScore
            ?? (governedScore ? null : ResolveScaledScore(request.ScaledScore));
        section.Grade = canonicalEvidence is not null
            ? canonicalEvidence.Grade
            : governedScore
                ? null
                : string.IsNullOrWhiteSpace(request.Grade) ? section.Grade : request.Grade;
        section.FeedbackJson = JsonSupport.Serialize(BuildSectionEvidencePayload(request.Evidence, canonicalEvidence));

        await ConsumeReservationForSectionAsync(userId, attempt, section, request.ReviewTurnaroundOption, now, ct);
        RecordEvent(userId, "mock_section_completed", new { mockAttemptId = attempt.Id, sectionId = section.Id, subtest = section.SubtestCode, section.ScaledScore });
        await db.SaveChangesAsync(ct);
        var projection = ProjectSectionAttempt(section, bundleSection, attempt);

        // Cache the projection so a concurrent retry from another tab
        // returns the same payload without re-running the write path.
        var winner = await TryPersistCompleteSectionIdempotencyAsync(idempotencyKey, projection, ct);
        return winner ?? projection;
    }

    private async Task<object?> TryPersistCompleteSectionIdempotencyAsync(
        string key,
        object projection,
        CancellationToken ct)
    {
        var record = new IdempotencyRecord
        {
            Id = $"idem-{Guid.NewGuid():N}",
            Scope = CompleteSectionIdempotencyScope,
            Key = key,
            ResponseJson = JsonSupport.Serialize(projection),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.IdempotencyRecords.Add(record);
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException)
        {
            db.Entry(record).State = EntityState.Detached;
            var winner = await db.IdempotencyRecords.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Scope == CompleteSectionIdempotencyScope && r.Key == key, ct);
            return winner is null ? null : TryDeserializeCompleteSectionResult(winner.ResponseJson);
        }
    }

    private static object? TryDeserializeCompleteSectionResult(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            // The projection is an anonymous object on write; we return it
            // as a Dictionary<string, object?> on read so the JSON shape
            // is preserved verbatim by the JSON serializer at the API
            // boundary.
            return JsonSupport.Deserialize<Dictionary<string, object?>>(json, new Dictionary<string, object?>());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Evidence gate for the human-marked (productive) sections. Writing
    /// completions must come from the mock writing workspace: the section has
    /// to be started and the request must carry a substantive writing payload
    /// (word count &gt; 0). Speaking completions require a live-tutor booking
    /// on this attempt (mocks are human-marked — no AI path exists to grade
    /// them) or, failing that, a started section with an evidence payload.
    /// </summary>
    private async Task RequireProductiveSectionEvidenceAsync(
        string userId,
        MockAttempt attempt,
        MockSectionAttempt section,
        MockSectionCompleteRequest request,
        CancellationToken ct)
    {
        var subtest = section.SubtestCode.Trim().ToLowerInvariant();
        if (subtest is not ("writing" or "speaking")) return;

        var sectionStarted = section.State is AttemptState.InProgress or AttemptState.Paused or AttemptState.Evaluating
            || section.StartedAt is not null;

        if (subtest == "writing")
        {
            var hasWritingPayload = request.Evidence is not null
                && request.Evidence.TryGetValue("wordCount", out var wc)
                && int.TryParse(wc?.ToString(), out var words)
                && words > 0;
            if (!sectionStarted || !hasWritingPayload)
            {
                throw ApiException.Validation(
                    "writing_evidence_required",
                    "Writing mock sections require a submitted response from the writing workspace.");
            }
            return;
        }

        // Section-scoped when the booking carries a MockSectionId (set by the
        // Speaking Gateway since 2026-07-27); attempt-scoped for older rows
        // and standalone bookings that predate section forwarding.
        var hasBooking = await db.MockBookings.AsNoTracking().AnyAsync(b =>
            b.UserId == userId
            && b.MockAttemptId == attempt.Id
            && (b.MockSectionId == null || b.MockSectionId == section.Id)
            && b.Status != MockBookingStatuses.Cancelled,
            ct);
        var hasSpeakingPayload = sectionStarted && request.Evidence is { Count: > 0 };
        if (!hasBooking && !hasSpeakingPayload)
        {
            throw ApiException.Validation(
                "speaking_evidence_required",
                "Speaking mock sections require a live-tutor booking or a completed speaking session.");
        }
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
        return BuildCanonicalEvidence(
            attempt.Id,
            attempt.RawScore,
            attempt.MaxRawScore,
            attempt.ScaledScore,
            attempt.ScoreConversionTableVersionKey,
            attempt.ScoreConversionGrade,
            attempt.ScoreConversionPassed,
            "reading_attempt");
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
        return BuildCanonicalEvidence(
            attempt.Id,
            attempt.RawScore,
            attempt.MaxRawScore,
            attempt.ScaledScore,
            attempt.ScoreConversionTableVersionKey,
            attempt.ScoreConversionGrade,
            attempt.ScoreConversionPassed,
            "listening_attempt");
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
        string? scoreConversionTableVersionKey,
        string? scoreConversionGrade,
        bool? scoreConversionPassed,
        string evidenceSource)
    {
        if (!rawScore.HasValue)
        {
            throw ApiException.Conflict("content_attempt_not_graded", "The submitted section evidence has not been graded yet.");
        }

        if (!scaledScore.HasValue
            || string.IsNullOrWhiteSpace(scoreConversionTableVersionKey)
            || !scoreConversionPassed.HasValue)
        {
            throw ApiException.Conflict(
                "content_attempt_scaled_unavailable",
                "The submitted section has a raw score but no owner-approved scaled conversion yet.");
        }

        if (rawScoreMax != OetScoring.ListeningReadingRawMax)
        {
            throw ApiException.Conflict(
                "content_attempt_scaled_unavailable",
                "Only a complete 42-question Listening or Reading attempt can provide owner-approved scaled conversion.");
        }

        var scaled = scaledScore.Value;
        return new CanonicalSectionEvidence(
            contentAttemptId,
            rawScore.Value,
            rawScoreMax,
            scaled,
            scoreConversionGrade,
            scoreConversionTableVersionKey.Trim(),
            scoreConversionPassed.Value,
            evidenceSource);
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
            evidence["scoreConversionTableVersionKey"] = canonicalEvidence.ScoreConversionTableVersionKey;
            evidence["scoreConversionPassed"] = canonicalEvidence.ScoreConversionPassed;
        }

        return evidence;
    }

    private sealed record CanonicalSectionEvidence(
        string ContentAttemptId,
        int RawScore,
        int RawScoreMax,
        int ScaledScore,
        string? Grade,
        string ScoreConversionTableVersionKey,
        bool ScoreConversionPassed,
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
        // Writing mock sections are always graded by a HUMAN examiner — never by
        // AI. Speaking used to be human-marked-only too, but the 2026-07-22
        // 7-day AI/tutor gate lets a mock's Speaking section be completed by
        // the AI exam instead (forced when the candidate's exam is under 7
        // days away, optional otherwise). Tailor the trust-boundary copy so it
        // never claims AI-graded Speaking was "marked by a human examiner".
        var hasWritingSection = await db.MockSectionAttempts.AsNoTracking()
            .AnyAsync(s => s.MockAttemptId == row.attempt.Id && s.SubtestCode == "writing", ct);
        var hasSpeakingSection = await db.MockSectionAttempts.AsNoTracking()
            .AnyAsync(s => s.MockAttemptId == row.attempt.Id && s.SubtestCode == "speaking", ct);
        var speakingWasAiGraded = hasSpeakingSection && await db.SpeakingExamSessions.AsNoTracking()
            .AnyAsync(e => e.MockAttemptId == row.attempt.Id
                && e.Mode == SpeakingExamMode.Ai
                && e.State == SpeakingExamState.Completed, ct);

        if (hasWritingSection || (hasSpeakingSection && !speakingWasAiGraded))
        {
            payload["aiTrustBoundary"] = new
            {
                disclaimer = "Reading & Listening are auto-scored against the official answer key; Speaking & Writing are marked by a human examiner, not AI. Treat this as practice guidance, not an official exam result.",
                provenanceLabel = "Human-marked Speaking/Writing · auto-scored Reading/Listening",
                methodLabel = "Examiner-marked mock exam"
            };
        }
        else if (hasSpeakingSection && speakingWasAiGraded)
        {
            payload["aiTrustBoundary"] = new
            {
                disclaimer = "Reading & Listening are auto-scored against the official answer key; Speaking was scored by AI (your exam was inside the 7-day window, or you chose AI). Treat this as practice guidance, not an official exam result.",
                provenanceLabel = "AI-scored Speaking · auto-scored Reading/Listening",
                methodLabel = "AI-assisted mock exam"
            };
        }
        else
        {
            payload["aiTrustBoundary"] = new
            {
                disclaimer = "Reading & Listening mock scores are auto-scored against the official answer key and should be treated as practice guidance, not official exam results.",
                provenanceLabel = "Auto-scored mock estimate",
                methodLabel = "Auto-scored mock exam"
            };
        }
        await SeedMockRemediationStudyPlanAsync(userId, row.attempt, row.report, payload, ct);
        await EnrichMockReportPayloadAsync(row.attempt, row.report, payload, ct);
        return payload;
    }
}
