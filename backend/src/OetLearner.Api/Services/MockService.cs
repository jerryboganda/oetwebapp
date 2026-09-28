using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Services;

public sealed partial class MockService(
    LearnerDbContext db,
    IAiPackageCreditService? aiPackageCreditService = null,
    IMockEntitlementService? mockEntitlementService = null)
{
    private static readonly string[] FullMockOrder = ["listening", "reading", "writing", "speaking"];
    private static readonly HashSet<string> ProductiveSubtests = new(["writing", "speaking"], StringComparer.OrdinalIgnoreCase);

    // Privacy floor for anonymised cohort percentile signal (Phase C2). Below this threshold
    // a learner could infer a specific peer's score, so the API returns no percentile at all.
    private const int CohortPrivacyMinimum = 10;

    /// <summary>Phase 6 closure — kebab-case scope for the
    /// IdempotencyRecord rows that cover mock-section completion. The
    /// row prevents concurrent completes from two tabs double-writing
    /// the analytics event or re-resolving canonical evidence.</summary>
    private const string CompleteSectionIdempotencyScope = "mock-section-complete";

    /// <summary>
    /// Mocks V2 Wave 2 — record a batch of proctoring events for an attempt.
    /// Rate-limited at <see cref="ProctoringEventCap"/> rows per attempt to prevent abuse.
    /// </summary>
    public const int ProctoringEventCap = 250;
    public const int ProctoringBatchMax = 50;

    // Mocks Wave 8 — admin leak-report queue.
    private static readonly HashSet<string> LeakReportStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "open", "investigating", "resolved", "dismissed" };

    private static readonly HashSet<string> TerminalLeakReportStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "resolved", "dismissed" };

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

        // Master Catalogue §7 profession isolation: never fall back to another
        // profession's bundle. Only universal bundles (all-professions or
        // unscoped) may serve a learner whose exact profession has no bundle.
        var bundle = candidates.FirstOrDefault(x => x.AppliesToAllProfessions || x.ProfessionId == null || x.ProfessionId == profession);

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

    private static int? ResolveScaledScore(int? scaledScore)
    {
        return scaledScore.HasValue
            ? Math.Clamp(scaledScore.Value, OetScoring.ScaledMin, OetScoring.ScaledMax)
            : null;
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
        scaledScore = IsOwnerConvertedSection(section) ? section.ScaledScore : section.SubtestCode.Trim().ToLowerInvariant() is "reading" or "listening" ? null : section.ScaledScore,
        grade = IsOwnerConvertedSection(section) ? section.Grade : section.SubtestCode.Trim().ToLowerInvariant() is "reading" or "listening" ? null : section.Grade
    };

    private static bool IsOwnerConvertedSection(MockSectionAttempt section)
    {
        var governedScore = section.SubtestCode.Trim().ToLowerInvariant() is "reading" or "listening";
        if (!governedScore) return true;
        var evidence = JsonSupport.Deserialize<Dictionary<string, object?>>(
            section.FeedbackJson,
            new Dictionary<string, object?>());
        return evidence.TryGetValue("scoreConversionTableVersionKey", out var key)
            && !string.IsNullOrWhiteSpace(key?.ToString())
            && evidence.TryGetValue("scoreConversionPassed", out var passed)
            && bool.TryParse(passed?.ToString(), out _);
    }

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
            // Listening exam mocks launch the new strict one-way sub-section
            // player (A1..B1-B6..C2, per-section audio + countdown + auto-advance).
            // It reads mockAttemptId/mockSectionId for score write-back and
            // defaults to exam mode (normalizeExamMode) for a launch with no
            // explicit ?mode=. The legacy /listening/player/{id} stays for
            // diagnostic / practice / direct review.
            "listening" => $"/listening/paper/{Uri.EscapeDataString(section.ContentPaperId)}?{query}",
            "writing" => $"/mocks/writing/{Uri.EscapeDataString(sectionAttemptId ?? section.ContentPaperId)}?{query}",
            // Speaking routes through the AI/tutor gateway (2026-07-22 owner
            // rule) instead of straight to the self-record task player — the
            // gateway decides AI-only vs AI-or-tutor from the candidate's
            // TargetExamDate and forwards this same query string onward.
            "speaking" => $"/mocks/speaking/{Uri.EscapeDataString(section.ContentPaperId)}?{query}",
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
