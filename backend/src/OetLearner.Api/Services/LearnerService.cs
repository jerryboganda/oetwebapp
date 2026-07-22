using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Services;

public interface IWritingEntitlementService
{
    Task<WritingEntitlement> CheckAsync(string? userId, CancellationToken ct);
}

public sealed record WritingEntitlement(
    bool Allowed,
    string Tier,
    int Remaining,
    int LimitPerWindow,
    int WindowDays,
    DateTimeOffset? ResetAt,
    string Reason);

public sealed record GeneratedDownloadFile(Stream Stream, string ContentType, string FileName);

public sealed record PaymentWebhookRetryResult(
    string EventId,
    string Status,
    string ProcessingStatus,
    string? ErrorMessage,
    int AttemptCount,
    int RetryCount,
    string? GatewayTransactionId,
    string? NormalizedStatus);

/// <summary>
/// Outcome of a synchronous, server-side order capture (PayPal Expanded checkout).
/// <see cref="Status"/> is one of "completed" | "failed" | "pending". <see cref="RedirectTo"/>
/// is a best-effort post-purchase destination; the client may override it.
/// </summary>
public sealed record PaymentCaptureResult(
    string Status,
    string OrderId,
    string? CaptureId,
    string? RedirectTo,
    string? FailureReason);

public partial class LearnerService(
    LearnerDbContext db,
    IFileStorage fileStorage,
    IPdfTextExtractor pdfTextExtractor,
    PlatformLinkService platformLinks,
    NotificationService notifications,
    WalletService walletService,
    PaymentGatewayService paymentGateways,
    DisputeService? disputeService = null,
    IOptions<BillingOptions>? billingOptions = null,
    IOptions<StorageOptions>? storageOptions = null,
    global::OetLearner.Api.Services.IWritingEntitlementService? writingEntitlement = null,
    OetLearner.Api.Services.Mocks.Results.IMockReportAggregationService? mockReportAggregation = null,
    SpacedRepetitionService? spacedRepetition = null,
    GamificationService? gamification = null,
    OetLearner.Api.Services.Planner.ContentPicker? studyPlanContentPicker = null,
    OetLearner.Api.Services.Readiness.ReadinessComputationService? readinessComputation = null,
    OetLearner.Api.Services.Billing.ICouponVariantApplicator? couponVariantApplicator = null,
    PrivateSpeakingService? privateSpeakingService = null,
    IFulfillmentService? fulfillmentService = null,
    IAddonEligibilityService? addonEligibilityService = null,
    IAiPackageCreditService? aiPackageCreditService = null,
    IInvoicePdfService? invoicePdfService = null,
    IPlanContentAvailabilityService? planContentAvailability = null,
    IManualPaymentService? manualPaymentService = null,
    ILogger<LearnerService>? logger = null)
{
    private const string PaymentWebhookParserVersion = "payment-webhook-v1";
    private const int PaymentIdempotencyKeyMaxLength = 38;
    private const int WritingRevisionContentMaxLength = 30000;
    private const int WritingRevisionIdempotencyKeyMaxLength = 64;
    private const int ProgressHistoryLimit = 52;
    private static readonly TimeSpan PaymentWebhookProcessingLease = TimeSpan.FromMinutes(5);
    private static readonly Regex PaymentIdempotencyKeyRegex = new("^[A-Za-z0-9._:-]+$", RegexOptions.Compiled);
    private static readonly Regex WritingRevisionIdempotencyKeyRegex = new("^[A-Za-z0-9._:-]+$", RegexOptions.Compiled);
    private readonly StorageOptions storageSettings = storageOptions?.Value ?? new StorageOptions();

    private sealed record LearnerProfileState(
        LearnerUser User,
        LearnerGoal Goal,
        LearnerSettings Settings,
        Wallet Wallet);

    private sealed record DashboardPlanState(
        StudyPlan Plan,
        List<StudyPlanItem> Items);

    private sealed record DashboardEvidenceRow(
        int PendingReviews,
        string? EvaluationId,
        string? AttemptId,
        string? SubtestCode,
        string? ScoreRange,
        string? CriterionScoresJson);

    private sealed record ProgressEvaluationRow(
        string SubtestCode,
        string ScoreRange,
        string CriterionScoresJson,
        DateTimeOffset? GeneratedAt);

    private sealed record ProgressTotalsRow(
        string AccountStatus,
        int CompletedAttempts,
        int CompletedEvaluations);

    private sealed record ProgressReviewAggregateRow(
        int TotalRequests,
        int CompletedRequests,
        int CreditsConsumed);

    private sealed record ProgressReviewTurnaroundRow(
        DateTimeOffset CreatedAt,
        DateTimeOffset CompletedAt);

    private sealed record ComparisonAttemptRow(
        string Id,
        string ContentId,
        string SubtestCode,
        string? ComparisonGroupId,
        string? ParentAttemptId);

    private sealed record ComparisonEvaluationRow(
        string Id,
        string AttemptId,
        string ScoreRange);

    private sealed record WritingHomeEvaluationRow(
        Evaluation Evaluation,
        Attempt Attempt,
        ContentItem Content,
        bool IsLatest);

    private static List<DiagnosticSubtestStatus> ActiveDiagnosticSubtests(List<DiagnosticSubtestStatus> subtests)
        => subtests
            .Where(x => !string.Equals(x.SubtestCode, "reading", StringComparison.OrdinalIgnoreCase))
            .ToList();

    public async Task<object> GetMeAsync(string userId, CancellationToken cancellationToken)
    {
        var profile = await EnsureLearnerProfileStateAsync(userId, cancellationToken);
        var freeze = await GetFreezeStatusForLoadedUserAsync(profile.User, cancellationToken);
        return BuildMeDto(profile.User, profile.Goal, freeze);
    }

    public async Task<object> GetBootstrapAsync(string userId, CancellationToken cancellationToken)
    {
        var profile = await EnsureLearnerProfileStateAsync(userId, cancellationToken);
        var onboarding = BuildOnboardingStateDto(profile.User);
        var goals = GoalDto(profile.Goal);
        var readiness = await GetReadinessForLoadedProfileAsync(profile, cancellationToken);
        var freeze = await GetFreezeStatusForLoadedUserAsync(profile.User, cancellationToken);

        return new
        {
            user = BuildMeDto(profile.User, profile.Goal, freeze),
            onboarding,
            goals,
            readiness,
            freeze,
            permissions = new
            {
                canRequestReview = true,
                canViewTranscript = true,
                canPurchaseExtras = true,
                canResumeAttempt = true,
                eligibilityReasonCodes = Array.Empty<string>()
            },
            reference = new
            {
                professions = await GetProfessionsAsync(cancellationToken),
                subtests = await GetSubtestsAsync(cancellationToken)
            },
            links = new
            {
                dashboard = "/dashboard",
                studyPlan = "/study-plan",
                goals = "/goals"
            },
            lastUpdatedAt = profile.User.LastActiveAt
        };
    }

    private static object BuildMeDto(LearnerUser user, LearnerGoal goal, object freeze) => new
    {
        userId = user.Id,
        role = user.Role,
        displayName = user.DisplayName,
        email = user.Email,
        timezone = user.Timezone,
        locale = user.Locale,
        createdAt = user.CreatedAt,
        lastActiveAt = user.LastActiveAt,
        currentPlanId = user.CurrentPlanId,
        activeProfessionId = user.ActiveProfessionId,
        freeze,
        goals = new
        {
            examFamilyCode = goal.ExamFamilyCode,
            professionId = goal.ProfessionId,
            targetExamDate = goal.TargetExamDate,
            targetScoresBySubtest = new
            {
                writing = goal.TargetWritingScore,
                speaking = goal.TargetSpeakingScore,
                reading = goal.TargetReadingScore,
                listening = goal.TargetListeningScore
            }
        }
    };

    public async Task<IEnumerable<object>> GetProfessionsAsync(CancellationToken cancellationToken) =>
        await db.Professions
            .OrderBy(x => x.SortOrder)
            .Select(x => (object)new { professionId = x.Id, code = x.Code, label = x.Label, status = x.Status, sortOrder = x.SortOrder })
            .ToListAsync(cancellationToken);

    public async Task<IEnumerable<object>> GetSubtestsAsync(CancellationToken cancellationToken) =>
        await db.Subtests
            .OrderBy(x => x.Label)
            .Select(x => (object)new { subtestId = x.Id, code = x.Code, label = x.Label, supportsProfessionSpecificContent = x.SupportsProfessionSpecificContent })
            .ToListAsync(cancellationToken);

    public async Task<IEnumerable<object>> GetCriteriaAsync(string? subtest, CancellationToken cancellationToken)
    {
        var query = db.Criteria.AsQueryable();
        if (!string.IsNullOrWhiteSpace(subtest))
        {
            query = query.Where(x => x.SubtestCode == subtest);
        }

        return await query
            .OrderBy(x => x.SubtestCode)
            .ThenBy(x => x.SortOrder)
            .Select(x => (object)new
            {
                criterionId = x.Id,
                subtest = x.SubtestCode,
                code = x.Code,
                label = x.Label,
                description = x.Description,
                sortOrder = x.SortOrder
            })
            .ToListAsync(cancellationToken);
    }

    public object GetFilters(string surface)
    {
        return surface.ToLowerInvariant() switch
        {
            "writing" => new
            {
                surface,
                groups = new[]
                {
                    new { id = "profession", label = "Profession", options = new[] { new { id = "nursing", label = "Nursing" }, new { id = "medicine", label = "Medicine" } } },
                    new { id = "difficulty", label = "Difficulty", options = new[] { new { id = "easy", label = "Easy" }, new { id = "medium", label = "Medium" }, new { id = "hard", label = "Hard" } } }
                }
            },
            "speaking" => new
            {
                surface,
                groups = new[]
                {
                    new { id = "mode", label = "Mode", options = new[] { new { id = "ai", label = "AI Roleplay" }, new { id = "exam", label = "Exam Mode" }, new { id = "self", label = "Self Practice" } } },
                    new { id = "scenario", label = "Scenario", options = new[] { new { id = "handover", label = "Handover" }, new { id = "consultation", label = "Consultation" } } }
                }
            },
            _ => new
            {
                surface,
                groups = new[]
                {
                    new { id = "subtest", label = "Sub-test", options = new[] { new { id = "writing", label = "Writing" }, new { id = "speaking", label = "Speaking" }, new { id = "reading", label = "Reading" }, new { id = "listening", label = "Listening" } } }
                }
            }
        };
    }

    public async Task<object> GetOnboardingStateAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await EnsureUserAsync(userId, cancellationToken);
        return BuildOnboardingStateDto(user);
    }

    private static object BuildOnboardingStateDto(LearnerUser user) => new
    {
        completed = user.OnboardingCompleted,
        currentStep = user.OnboardingCurrentStep,
        stepCount = user.OnboardingStepCount,
        canSkip = false,
        startedAt = user.OnboardingStartedAt,
        completedAt = user.OnboardingCompletedAt,
        checkpoint = user.OnboardingCompleted ? "goals" : "welcome",
        resumeRoute = user.OnboardingCompleted ? "/dashboard" : "/onboarding"
    };

    public async Task<object> StartOnboardingAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        user.OnboardingStartedAt ??= DateTimeOffset.UtcNow;
        user.OnboardingCurrentStep = Math.Max(user.OnboardingCurrentStep, 1);
        user.LastActiveAt = DateTimeOffset.UtcNow;
        await RecordEventAsync(userId, "onboarding_started", new { userId }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetOnboardingStateAsync(userId, cancellationToken);
    }

    public async Task<object> CompleteOnboardingAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        user.OnboardingCompleted = true;
        user.OnboardingCurrentStep = user.OnboardingStepCount;
        user.OnboardingCompletedAt = DateTimeOffset.UtcNow;
        user.LastActiveAt = DateTimeOffset.UtcNow;
        await RecordEventAsync(userId, "onboarding_completed", new { userId }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetOnboardingStateAsync(userId, cancellationToken);
    }

    // ── Onboarding product tours ────────────────────────────────────────────
    // Per-user guided-tour completion state used by the client tour engine so a
    // tour is never auto-replayed once seen, and so tours can be re-surfaced after
    // a major content revision (LastSeenTourVersion < OnboardingTourVersion). The
    // rich tour_* analytics (with step/route detail) are emitted client-side; these
    // methods only persist durable completion/skip/dismiss state. Keyed by the auth
    // user id, so they also serve expert/tutor and admin workspaces.
    public const int OnboardingTourVersion = 1;

    public async Task<object> GetTourStateAsync(string userId, CancellationToken cancellationToken)
    {
        var row = await db.LearnerOnboardingTours.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        return TourStateDto(row);
    }

    public async Task<object> MarkTourAsync(string userId, MarkTourRequest request, CancellationToken cancellationToken)
    {
        var tourId = (request.TourId ?? string.Empty).Trim().ToLowerInvariant();
        var status = (request.Status ?? string.Empty).Trim().ToLowerInvariant();
        if (tourId.Length == 0)
            throw ApiException.Validation("invalid_tour", "A tour id is required.", [new ApiFieldError("tourId", "required", "Tour id is required.")]);
        if (status is not ("completed" or "skipped" or "dismissed"))
            throw ApiException.Validation("invalid_tour_status", "Status must be completed, skipped, or dismissed.", [new ApiFieldError("status", "invalid", "Use completed | skipped | dismissed.")]);

        var now = DateTimeOffset.UtcNow;
        var row = await db.LearnerOnboardingTours.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (row is null)
        {
            row = new LearnerOnboardingTour
            {
                UserId = userId,
                Role = NormalizeTourRole(request.Role),
                OnboardingVersion = OnboardingTourVersion,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.LearnerOnboardingTours.Add(row);
        }

        if (!string.IsNullOrWhiteSpace(request.Role)) row.Role = NormalizeTourRole(request.Role);

        if (status == "dismissed")
            row.DismissedTipsJson = AddToJsonSet(row.DismissedTipsJson, tourId);
        else if (status == "skipped")
            row.SkippedToursJson = AddToJsonSet(row.SkippedToursJson, tourId);
        else
            ApplyTourCompletion(row, tourId);

        row.LastSeenTourVersion = OnboardingTourVersion;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return TourStateDto(row);
    }

    private static void ApplyTourCompletion(LearnerOnboardingTour row, string tourId)
    {
        switch (tourId)
        {
            case "intro": row.CompletedIntro = true; break;
            case "dashboard":
            case "learner-dashboard": row.CompletedDashboardTour = true; break;
            case "listening": row.CompletedListeningTour = true; break;
            case "reading": row.CompletedReadingTour = true; break;
            case "writing": row.CompletedWritingTour = true; break;
            case "speaking": row.CompletedSpeakingTour = true; break;
            case "admin": row.CompletedAdminTour = true; break;
            case "expert":
            case "tutor": row.CompletedExpertTour = true; break;
            // Unknown ids: the closed column set stays stable; the client also caches
            // completion locally, so an unmapped id is simply not mirrored to a column.
        }
    }

    private static string AddToJsonSet(string json, string value)
    {
        var list = JsonSupport.Deserialize<List<string>>(json, []) ?? [];
        if (!list.Contains(value, StringComparer.OrdinalIgnoreCase)) list.Add(value);
        return JsonSupport.Serialize(list);
    }

    private static string NormalizeTourRole(string? role)
    {
        var r = (role ?? string.Empty).Trim().ToLowerInvariant();
        if (r == "tutor") return "expert";
        return r is "expert" or "admin" ? r : "learner";
    }

    private static string? NormalizeExamMode(string? mode)
    {
        var m = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return m.Length == 0 ? null : m;
    }

    private static string? NormalizeConfidence(string? level)
    {
        var l = (level ?? string.Empty).Trim().ToLowerInvariant();
        return l.Length == 0 ? null : l;
    }

    private static object TourStateDto(LearnerOnboardingTour? row) => new
    {
        onboardingVersion = row?.OnboardingVersion ?? OnboardingTourVersion,
        role = row?.Role ?? "learner",
        lastSeenTourVersion = row?.LastSeenTourVersion ?? 0,
        completed = new
        {
            intro = row?.CompletedIntro ?? false,
            dashboard = row?.CompletedDashboardTour ?? false,
            listening = row?.CompletedListeningTour ?? false,
            reading = row?.CompletedReadingTour ?? false,
            writing = row?.CompletedWritingTour ?? false,
            speaking = row?.CompletedSpeakingTour ?? false,
            admin = row?.CompletedAdminTour ?? false,
            expert = row?.CompletedExpertTour ?? false,
        },
        skippedTours = JsonSupport.Deserialize<List<string>>(row?.SkippedToursJson ?? "[]", []),
        dismissedTips = JsonSupport.Deserialize<List<string>>(row?.DismissedTipsJson ?? "[]", []),
    };

    public async Task<object> GetGoalsAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        await EnsureUserAsync(userId, cancellationToken);
        var goal = await db.Goals.FirstAsync(x => x.UserId == userId, cancellationToken);
        return GoalDto(goal);
    }

    public async Task<object> PatchGoalsAsync(string userId, PatchGoalsRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var goal = await db.Goals.FirstAsync(x => x.UserId == userId, cancellationToken);
        var examFamilyCode = NormalizeExamFamilyCode(request.ExamFamilyCode ?? goal.ExamFamilyCode);
        var examFamilyExists = await db.ExamFamilies.AsNoTracking()
            .AnyAsync(x => x.IsActive && x.Code == examFamilyCode, cancellationToken);
        if (!examFamilyExists)
        {
            throw ApiException.Validation(
                "invalid_exam_family",
                $"Exam family '{examFamilyCode}' is not supported.",
                [new ApiFieldError("examFamilyCode", "unsupported", "Choose a supported exam family.")]);
        }

        ValidateScoreRange(request.TargetWritingScore, nameof(request.TargetWritingScore), examFamilyCode);
        ValidateScoreRange(request.TargetSpeakingScore, nameof(request.TargetSpeakingScore), examFamilyCode);
        ValidateScoreRange(request.TargetReadingScore, nameof(request.TargetReadingScore), examFamilyCode);
        ValidateScoreRange(request.TargetListeningScore, nameof(request.TargetListeningScore), examFamilyCode);
        if (request.StudyHoursPerWeek.HasValue && (request.StudyHoursPerWeek.Value < 0 || request.StudyHoursPerWeek.Value > 168))
            throw ApiException.Validation("invalid_study_hours", "Study hours per week must be between 0 and 168.", [new ApiFieldError("studyHoursPerWeek", "out_of_range", "Must be 0–168.")]);
        if (request.PreviousAttempts.HasValue && request.PreviousAttempts.Value < 0)
            throw ApiException.Validation("invalid_previous_attempts", "Previous attempts cannot be negative.", [new ApiFieldError("previousAttempts", "out_of_range", "Must be 0 or greater.")]);

        goal.ExamFamilyCode = examFamilyCode;
        if (!string.IsNullOrWhiteSpace(request.ProfessionId)) goal.ProfessionId = request.ProfessionId;
        if (request.TargetExamDate.HasValue) goal.TargetExamDate = request.TargetExamDate;
        if (request.OverallGoal is not null) goal.OverallGoal = request.OverallGoal;
        if (request.TargetWritingScore.HasValue) goal.TargetWritingScore = request.TargetWritingScore;
        if (request.TargetSpeakingScore.HasValue) goal.TargetSpeakingScore = request.TargetSpeakingScore;
        if (request.TargetReadingScore.HasValue) goal.TargetReadingScore = request.TargetReadingScore;
        if (request.TargetListeningScore.HasValue) goal.TargetListeningScore = request.TargetListeningScore;
        if (request.PreviousAttempts.HasValue) goal.PreviousAttempts = request.PreviousAttempts.Value;
        if (request.WeakSubtests is not null) goal.WeakSubtestsJson = JsonSupport.Serialize(request.WeakSubtests);
        if (request.StudyHoursPerWeek.HasValue) goal.StudyHoursPerWeek = request.StudyHoursPerWeek.Value;
        if (request.TargetCountry is not null) goal.TargetCountry = TargetCountryOptions.Canonicalize(request.TargetCountry);
        if (request.TargetOrganization is not null) goal.TargetOrganization = request.TargetOrganization;
        if (request.DraftState is not null) goal.DraftStateJson = JsonSupport.Serialize(request.DraftState);
        if (request.TargetExamMode is not null) goal.TargetExamMode = NormalizeExamMode(request.TargetExamMode);
        if (request.ConfidenceLevel is not null) goal.ConfidenceLevel = NormalizeConfidence(request.ConfidenceLevel);

        goal.UpdatedAt = DateTimeOffset.UtcNow;
        await RecordEventAsync(userId, "goals_saved", new { userId, professionId = goal.ProfessionId, targetExamDate = goal.TargetExamDate }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return GoalDto(goal);
    }

    private static void ValidateScoreRange(int? score, string fieldName, string examFamilyCode)
    {
        if (!score.HasValue)
        {
            return;
        }

        var (minimum, maximum, label) = examFamilyCode switch
        {
            "ielts" => (0, 9, "0-9"),
            "pte" => (10, 90, "10-90"),
            _ => (0, 500, "0-500")
        };

        if (score.Value < minimum || score.Value > maximum)
        {
            throw ApiException.Validation(
                "invalid_score_range",
                $"Target score must be between {minimum} and {maximum} for {label} scoring.",
                [new ApiFieldError(fieldName, "out_of_range", $"Must be {minimum}–{maximum} for {label} scoring.")]);
        }
    }

    public async Task<object> SubmitGoalsAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var goal = await db.Goals.FirstAsync(x => x.UserId == userId, cancellationToken);
            goal.SubmittedAt = DateTimeOffset.UtcNow;
            goal.UpdatedAt = DateTimeOffset.UtcNow;

            var plan = await GetActiveStudyPlanEntityAsync(userId, cancellationToken);
            plan.State = AsyncState.Queued;
            await QueueJobAsync(JobType.StudyPlanRegeneration, resourceId: plan.Id, cancellationToken: cancellationToken);
            await RecordEventAsync(userId, "goals_saved", new { userId, professionId = goal.ProfessionId, submitted = true }, cancellationToken);
            LogAudit(userId, "Submitted", "Goals", goal.Id.ToString(), "Goals submitted, triggering study plan regeneration");
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new
            {
                goals = GoalDto(goal),
                studyPlanRegeneration = new
                {
                    state = ToAsyncState(plan.State),
                    nextPollAfterMs = 2000,
                    planId = plan.Id
                }
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<object> GetSettingsAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        await EnsureUserAsync(userId, cancellationToken);
        var settings = await db.Settings.FirstAsync(x => x.UserId == userId, cancellationToken);
        var goal = await db.Goals.FirstAsync(x => x.UserId == userId, cancellationToken);
        return SettingsDto(settings, goal);
    }

    public async Task<object> GetSettingsSectionAsync(string userId, string section, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        var user = await EnsureUserAsync(userId, cancellationToken);
        var settings = await db.Settings.FirstAsync(x => x.UserId == userId, cancellationToken);
        var goal = await db.Goals.FirstAsync(x => x.UserId == userId, cancellationToken);
        var profileValues = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.ProfileJson, new Dictionary<string, object?>());
        profileValues["displayName"] = user.DisplayName;
        profileValues["email"] = user.Email;
        profileValues["professionId"] = goal.ProfessionId ?? user.ActiveProfessionId;

        var studyValues = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.StudyJson, new Dictionary<string, object?>());
        studyValues["targetExamDate"] = goal.TargetExamDate;
        studyValues["studyHoursPerWeek"] = goal.StudyHoursPerWeek;
        studyValues["targetCountry"] = goal.TargetCountry;
        studyValues["professionId"] = goal.ProfessionId ?? user.ActiveProfessionId;
        studyValues["examFamilyCode"] = goal.ExamFamilyCode;

        return section.ToLowerInvariant() switch
        {
            "profile" => new { section = "profile", values = profileValues },
            "notifications" => new { section = "notifications", values = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.NotificationsJson, new Dictionary<string, object?>()) },
            "privacy" => new { section = "privacy", values = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.PrivacyJson, new Dictionary<string, object?>()) },
            "accessibility" => new { section = "accessibility", values = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.AccessibilityJson, new Dictionary<string, object?>()) },
            "audio" => new { section = "audio", values = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.AudioJson, new Dictionary<string, object?>()) },
            "study" => new { section = "study", values = studyValues },
            "goals" => new { section = "goals", values = GoalSettingsDto(await db.Goals.FirstAsync(x => x.UserId == userId, cancellationToken)) },
            _ => throw ApiException.Validation(
                "unknown_settings_section",
                $"Unknown settings section '{section}'.",
                [new ApiFieldError("section", "unknown_section", "Choose a supported settings section.")])
        };
    }

    public async Task<object> PatchSettingsSectionAsync(string userId, string section, PatchSectionRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        var user = await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var settings = await db.Settings.FirstAsync(x => x.UserId == userId, cancellationToken);
        var goal = await db.Goals.FirstAsync(x => x.UserId == userId, cancellationToken);
        if (section.Equals("goals", StringComparison.OrdinalIgnoreCase))
        {
            ApplyGoalSettingsPatch(goal, request.Values);
            goal.UpdatedAt = DateTimeOffset.UtcNow;
            await RecordEventAsync(userId, "settings_changed", new { userId, section = "goals" }, cancellationToken);
            LogAudit(userId, "Updated", "Settings", "goals", "Updated goal settings");
            await db.SaveChangesAsync(cancellationToken);
            return new { section = "goals", values = GoalSettingsDto(goal) };
        }

        if (section.Equals("profile", StringComparison.OrdinalIgnoreCase))
        {
            var mergedProfile = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.ProfileJson, new Dictionary<string, object?>());
            foreach (var (key, value) in request.Values)
            {
                mergedProfile[key] = value;
            }

            if (request.Values.TryGetValue("displayName", out var displayName))
            {
                user.DisplayName = ReadString(displayName) ?? user.DisplayName;
            }

            if (request.Values.TryGetValue("email", out var email))
            {
                user.Email = ReadString(email) ?? user.Email;
            }

            if (request.Values.TryGetValue("professionId", out var professionId))
            {
                var normalizedProfession = await ResolveProfessionChangeAsync(user, goal, professionId, "profile", cancellationToken);
                if (!string.IsNullOrWhiteSpace(normalizedProfession))
                {
                    goal.ProfessionId = normalizedProfession;
                    user.ActiveProfessionId = normalizedProfession;
                    mergedProfile["professionId"] = normalizedProfession;
                }
            }

            settings.ProfileJson = JsonSupport.Serialize(mergedProfile);
            goal.UpdatedAt = DateTimeOffset.UtcNow;
            await RecordEventAsync(userId, "settings_changed", new { userId, section = "profile" }, cancellationToken);
            LogAudit(userId, "Updated", "Settings", "profile", "Updated learner profile settings");
            await db.SaveChangesAsync(cancellationToken);
            return new
            {
                section = "profile",
                values = new Dictionary<string, object?>(mergedProfile)
                {
                    ["displayName"] = user.DisplayName,
                    ["email"] = user.Email,
                    ["professionId"] = goal.ProfessionId ?? user.ActiveProfessionId
                }
            };
        }

        if (section.Equals("study", StringComparison.OrdinalIgnoreCase))
        {
            var studyValues = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.StudyJson, new Dictionary<string, object?>());
            foreach (var (key, value) in request.Values)
            {
                studyValues[key] = value;
            }

            if (request.Values.TryGetValue("targetExamDate", out var targetExamDate))
            {
                goal.TargetExamDate = ReadDateOnly(targetExamDate) ?? goal.TargetExamDate;
            }

            if (request.Values.TryGetValue("studyHoursPerWeek", out var studyHoursPerWeek))
            {
                goal.StudyHoursPerWeek = ReadInt(studyHoursPerWeek) ?? goal.StudyHoursPerWeek;
            }

            if (request.Values.TryGetValue("targetCountry", out var targetCountry))
            {
                goal.TargetCountry = TargetCountryOptions.Canonicalize(ReadString(targetCountry));
            }

            if (request.Values.TryGetValue("professionId", out var studyProfessionId))
            {
                var normalizedProfession = await ResolveProfessionChangeAsync(user, goal, studyProfessionId, "study", cancellationToken);
                if (!string.IsNullOrWhiteSpace(normalizedProfession))
                {
                    goal.ProfessionId = normalizedProfession;
                    user.ActiveProfessionId = normalizedProfession;
                    studyValues["professionId"] = normalizedProfession;
                }
            }

            settings.StudyJson = JsonSupport.Serialize(studyValues);
            goal.UpdatedAt = DateTimeOffset.UtcNow;
            await RecordEventAsync(userId, "settings_changed", new { userId, section = "study" }, cancellationToken);
            LogAudit(userId, "Updated", "Settings", "study", "Updated study settings");
            await db.SaveChangesAsync(cancellationToken);
            studyValues["targetExamDate"] = goal.TargetExamDate;
            studyValues["studyHoursPerWeek"] = goal.StudyHoursPerWeek;
            studyValues["targetCountry"] = goal.TargetCountry;
            studyValues["professionId"] = goal.ProfessionId ?? user.ActiveProfessionId;
            return new { section = "study", values = studyValues };
        }

        var merged = section.ToLowerInvariant() switch
        {
            "notifications" => settings.NotificationsJson = MergeJsonSection(settings.NotificationsJson, request.Values),
            "privacy" => settings.PrivacyJson = MergeJsonSection(settings.PrivacyJson, request.Values),
            "accessibility" => settings.AccessibilityJson = MergeJsonSection(settings.AccessibilityJson, request.Values),
            "audio" => settings.AudioJson = MergeJsonSection(settings.AudioJson, request.Values),
            _ => throw ApiException.Validation(
                "unknown_settings_section",
                $"Unknown settings section '{section}'.",
                [new ApiFieldError("section", "unknown_section", "Choose a supported settings section.")])
        };

        await RecordEventAsync(userId, "settings_changed", new { userId, section = section.ToLowerInvariant() }, cancellationToken);
        LogAudit(userId, "Updated", "Settings", section.ToLowerInvariant(), $"Updated {section} settings");
        await db.SaveChangesAsync(cancellationToken);
        return new { section, values = JsonSupport.Deserialize<Dictionary<string, object?>>(merged, new Dictionary<string, object?>()) };
    }

    /// <summary>
    /// Resolve a learner-requested profession change into the canonical catalog id, or
    /// throw. Profession is one half of the subtest x profession axis that decides which
    /// content a package unlocks, so it is not free-text and it does not stay editable
    /// forever: once the learner has bought something, only an admin can move it.
    /// Returns the value to persist (unchanged when the PATCH is a no-op re-send, which
    /// the settings form does on every save).
    /// </summary>
    private async Task<string?> ResolveProfessionChangeAsync(
        LearnerUser user,
        LearnerGoal goal,
        object? requestedProfessionId,
        string section,
        CancellationToken cancellationToken)
    {
        var current = goal.ProfessionId ?? user.ActiveProfessionId;
        var requested = ReadString(requestedProfessionId)?.Trim();
        if (string.IsNullOrWhiteSpace(requested))
        {
            return current;
        }

        if (string.Equals(requested, current, StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        var normalized = requested.ToLowerInvariant();
        var catalogEntry = await db.SignupProfessionCatalog.AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Id.ToLower() == normalized && entry.IsActive, cancellationToken)
            ?? throw ApiException.Validation(
                "unknown_profession",
                $"Unknown profession '{requested}'.",
                [new ApiFieldError("professionId", "unknown", "Choose a profession from the registration list.")]);

        if (await HasCompletedPurchaseAsync(user.Id, cancellationToken))
        {
            throw ApiException.Validation(
                "profession_locked",
                "Your profession is locked because you have already purchased a package. Contact support to request a change.",
                [new ApiFieldError("professionId", "locked", "Contact support to change your profession.")]);
        }

        await RecordEventAsync(user.Id, "profession_changed", new
        {
            userId = user.Id,
            section,
            from = current,
            to = catalogEntry.Id
        }, cancellationToken);
        LogAudit(
            user.Id,
            "Updated",
            "Profession",
            user.Id,
            $"Profession changed from '{current ?? "(none)"}' to '{catalogEntry.Id}' via {section} settings");

        return catalogEntry.Id;
    }

    /// <summary>
    /// True once the learner owns anything they paid for. Reads the Subscriptions table
    /// rather than PaymentTransactions so admin-granted packages (which never produce a
    /// transaction) count too. The only subscription that is NOT a purchase is the
    /// pre-payment checkout scaffold — Pending with the default auto fulfilment — so a
    /// learner who merely opened checkout is not locked, while a paid manual-delivery
    /// order awaiting admin hand-over (Pending + pending_manual) is.
    /// </summary>
    private async Task<bool> HasCompletedPurchaseAsync(string userId, CancellationToken cancellationToken)
        => await db.Subscriptions.AsNoTracking().AnyAsync(subscription =>
            subscription.UserId == userId
            && (subscription.Status != SubscriptionStatus.Pending
                || subscription.FulfilmentStatus != FulfilmentStatuses.Auto), cancellationToken);

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
                    : new { evaluationId = evidence.EvaluationId, attemptId = evidence.AttemptId, subtest = evidence.SubtestCode, scoreRange = evidence.ScoreRange, route = AttemptFeedbackRoute(evidence.SubtestCode!, evidence.EvaluationId) },
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
                ORDER BY evaluation.""GeneratedAt"" DESC
                LIMIT 1")
            : db.Evaluations
                .Where(evaluation => db.Attempts.Any(attempt =>
                    attempt.Id == evaluation.AttemptId && attempt.UserId == userId))
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
                    evaluation == null ? null : evaluation.CriterionScoresJson))
            .SingleAsync(cancellationToken);
    }

    public async Task<object> GetStudyPlanAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        await EnsureUserAsync(userId, cancellationToken);
        var plan = await GetActiveStudyPlanEntityAsync(userId, cancellationToken);
        var items = await db.StudyPlanItems.Where(x => x.StudyPlanId == plan.Id).OrderBy(x => x.DueDate).ToListAsync(cancellationToken);
        var latestJob = await GetLatestStudyPlanRegenerationJobAsync(plan.Id, cancellationToken);

        return new
        {
            planId = plan.Id,
            version = plan.Version,
            generatedAt = plan.GeneratedAt,
            state = ToAsyncState(plan.State),
            checkpoint = plan.Checkpoint,
            weakSkillFocus = plan.WeakSkillFocus,
            items = items.Select(StudyPlanItemDto).ToList(),
            statusReasonCode = latestJob?.StatusReasonCode,
            statusMessage = latestJob?.StatusMessage,
            retryAfterMs = latestJob?.RetryAfterMs,
            lastTransitionAt = latestJob?.LastTransitionAt
        };
    }

    public async Task<object> RegenerateStudyPlanAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var plan = await GetActiveStudyPlanEntityAsync(userId, cancellationToken);
        plan.State = AsyncState.Queued;
        var payloadJson = JsonSupport.Serialize(new { userId, trigger = "Manual" });
        await QueueJobAsync(JobType.StudyPlanRegeneration, resourceId: plan.Id, payloadJson: payloadJson, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new { planId = plan.Id, state = "queued", nextPollAfterMs = 2000 };
    }

    public async Task<object> CompleteStudyPlanItemAsync(string userId, string itemId, CancellationToken cancellationToken)
    {
        return await CompleteStudyPlanItemInternalAsync(userId, itemId, feedbackRating: null, actualMinutesSpent: null, cancellationToken);
    }

    public async Task<object> CompleteStudyPlanItemAsync(string userId, string itemId, int? feedbackRating, int? actualMinutesSpent, CancellationToken cancellationToken)
    {
        return await CompleteStudyPlanItemInternalAsync(userId, itemId, feedbackRating, actualMinutesSpent, cancellationToken);
    }

    private async Task<object> CompleteStudyPlanItemInternalAsync(string userId, string itemId, int? feedbackRating, int? actualMinutesSpent, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var item = await GetStudyPlanItemOwnedByUserAsync(userId, itemId, cancellationToken);
        var alreadyCompleted = item.Status == StudyPlanItemStatus.Completed;
        item.Status = StudyPlanItemStatus.Completed;
        item.CompletedAt ??= DateTimeOffset.UtcNow;
        if (feedbackRating is not null) item.FeedbackRating = feedbackRating;
        if (actualMinutesSpent is not null) item.ActualMinutesSpent = actualMinutesSpent;
        var plan = await db.StudyPlans.FirstAsync(x => x.Id == item.StudyPlanId, cancellationToken);
        await RecordEventAsync(plan.UserId, "study_plan_item_completed", new { itemId = item.Id, planId = item.StudyPlanId, subtest = item.SubtestCode, feedbackRating, actualMinutesSpent }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        if (!alreadyCompleted)
        {
            await AwardCompletionRewardsAsync(plan.UserId, item, cancellationToken);
            await TryRefreshReadinessAsync(plan.UserId, cancellationToken);
        }

        return StudyPlanItemDto(item);
    }

    private async Task TryRefreshReadinessAsync(string userId, CancellationToken cancellationToken)
    {
        if (readinessComputation is null) return;
        try
        {
            await readinessComputation.ComputeAsync(userId, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[readiness] Compute failed for user={userId}: {ex.Message}");
        }
    }

    private async Task AwardCompletionRewardsAsync(string userId, StudyPlanItem item, CancellationToken cancellationToken)
    {
        // XP: scale modestly with task duration so a 5-min flashcard ≠ a 45-min mock.
        if (gamification is not null)
        {
            var xp = Math.Clamp(10 + (item.DurationMinutes / 5), 10, 60);
            try
            {
                await gamification.AwardXpAsync(userId, xp, $"study_plan:{item.SubtestCode}", cancellationToken);
            }
            catch (Exception ex)
            {
                // XP award is non-critical — log only.
                Console.Error.WriteLine($"[plan-complete] XP award failed for user={userId} item={item.Id}: {ex.Message}");
            }
        }

        // Spaced-repetition progression when this plan item linked back to a ReviewItem.
        if (!string.IsNullOrWhiteSpace(item.LinkedReviewItemId) && spacedRepetition is not null)
        {
            var quality = MapFeedbackToSm2Quality(item.FeedbackRating);
            try
            {
                await spacedRepetition.SubmitReviewAsync(userId, item.LinkedReviewItemId!, quality, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[plan-complete] SM-2 update failed for user={userId} review={item.LinkedReviewItemId}: {ex.Message}");
            }
        }
    }

    private static int MapFeedbackToSm2Quality(int? feedbackRating) => feedbackRating switch
    {
        1 => 5,   // "Too easy" → perfect recall
        2 => 4,   // "Just right" → good
        3 => 2,   // "Too hard" → hard
        _ => 4    // No feedback → assume good
    };

    public async Task<object> SkipStudyPlanItemAsync(string userId, string itemId, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var item = await GetStudyPlanItemOwnedByUserAsync(userId, itemId, cancellationToken);
        item.Status = StudyPlanItemStatus.Skipped;
        var plan = await db.StudyPlans.FirstAsync(x => x.Id == item.StudyPlanId, cancellationToken);
        await RecordEventAsync(plan.UserId, "study_plan_item_skipped", new { itemId = item.Id, planId = item.StudyPlanId, subtest = item.SubtestCode }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return StudyPlanItemDto(item);
    }

    public async Task<object> RescheduleStudyPlanItemAsync(string userId, string itemId, StudyPlanRescheduleRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var item = await GetStudyPlanItemOwnedByUserAsync(userId, itemId, cancellationToken);
        item.Status = StudyPlanItemStatus.Rescheduled;
        item.DueDate = request.DueDate ?? item.DueDate.AddDays(1);
        var plan = await db.StudyPlans.FirstAsync(x => x.Id == item.StudyPlanId, cancellationToken);
        await RecordEventAsync(plan.UserId, "study_plan_item_rescheduled", new { itemId = item.Id, planId = item.StudyPlanId, dueDate = item.DueDate, subtest = item.SubtestCode }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return StudyPlanItemDto(item);
    }

    public async Task<object> ResetStudyPlanItemAsync(string userId, string itemId, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var item = await GetStudyPlanItemOwnedByUserAsync(userId, itemId, cancellationToken);
        item.Status = StudyPlanItemStatus.NotStarted;
        await db.SaveChangesAsync(cancellationToken);
        return StudyPlanItemDto(item);
    }

    public async Task<object> SwapStudyPlanItemAsync(string userId, string itemId, StudyPlanSwapRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var item = await GetStudyPlanItemOwnedByUserAsync(userId, itemId, cancellationToken);

        // Mode 1: candidates-only — list 3 alternatives without mutating.
        if (string.IsNullOrWhiteSpace(request.ReplacementContentId))
        {
            if (studyPlanContentPicker is null)
            {
                return new { candidates = Array.Empty<object>(), note = "ContentPicker unavailable" };
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            var candidates = await studyPlanContentPicker.ResolveAlternativesAsync(item, user?.ActiveProfessionId, count: 3, cancellationToken);
            return new
            {
                itemId = item.Id,
                candidates = candidates.Select(c => new
                {
                    contentId = c.ContentId,
                    title = c.Title,
                    route = c.Route,
                    durationMinutes = c.DurationMinutes
                })
            };
        }

        // Mode 2: apply swap — mark original Replaced, insert new item.
        var replacement = await db.ContentItems
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ReplacementContentId, cancellationToken);

        var newItem = new StudyPlanItem
        {
            Id = $"plan-item-{Guid.NewGuid():N}",
            StudyPlanId = item.StudyPlanId,
            Title = replacement?.Title ?? item.Title,
            SubtestCode = item.SubtestCode,
            DurationMinutes = replacement?.EstimatedDurationMinutes > 0 ? replacement.EstimatedDurationMinutes : item.DurationMinutes,
            Rationale = $"Swapped from \"{item.Title}\" at your request. Comparable scope and duration.",
            DueDate = item.DueDate,
            Status = StudyPlanItemStatus.NotStarted,
            Section = item.Section,
            ContentId = request.ReplacementContentId,
            SourceContentId = request.ReplacementContentId,
            ContentRoute = BuildRouteForReplacement(item.SubtestCode, request.ReplacementContentId, item.ItemType),
            ItemType = item.ItemType,
            PriorityScore = item.PriorityScore,
            WeekIndex = item.WeekIndex,
            SlotKind = item.SlotKind
        };
        db.StudyPlanItems.Add(newItem);

        // Swap state: keep enum stable, mark via ReplacedById link. UI filters
        // out items where ReplacedById is non-null so they no longer surface.
        item.Status = StudyPlanItemStatus.Skipped;
        item.ReplacedById = newItem.Id;

        var plan = await db.StudyPlans.FirstAsync(x => x.Id == item.StudyPlanId, cancellationToken);
        await RecordEventAsync(plan.UserId, "study_plan_item_swapped", new
        {
            originalItemId = item.Id,
            newItemId = newItem.Id,
            planId = item.StudyPlanId,
            subtest = item.SubtestCode,
            replacementContentId = request.ReplacementContentId
        }, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return StudyPlanItemDto(newItem);
    }

    private static string BuildRouteForReplacement(string subtest, string contentId, string itemType)
    {
        var lower = (subtest ?? string.Empty).ToLowerInvariant();
        if (string.Equals(itemType, "mock", StringComparison.OrdinalIgnoreCase))
        {
            return $"/{lower}/mocks";
        }
        if (string.IsNullOrWhiteSpace(contentId))
        {
            return $"/{lower}";
        }
        return lower switch
        {
            "reading" => $"/reading/paper/{Uri.EscapeDataString(contentId)}",
            "listening" => $"/listening/player/{Uri.EscapeDataString(contentId)}",
            "writing" => "/writing/practice/library",
            "speaking" => $"/speaking/task/{Uri.EscapeDataString(contentId)}",
            _ => $"/{lower}"
        };
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
        await RecordEventAsync(profile.User.Id, "readiness_viewed", new { userId = profile.User.Id, snapshotId = snapshot.Id, computedAt = snapshot.ComputedAt }, cancellationToken);
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
                evaluation.CriterionScoresJson,
                evaluation.GeneratedAt))
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
                    score = ParseCriterionScore(criterion.GetValueOrDefault("scoreRange")?.ToString()),
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

        return new
        {
            trend = evaluations.Select((x, index) => new { week = $"Week {index + 1}", subtest = x.SubtestCode, scoreRange = x.ScoreRange, generatedAt = x.GeneratedAt }),
            subtestTrend = evaluations.Select((x, index) => new { week = $"Week {index + 1}", subtest = x.SubtestCode, scoreRange = x.ScoreRange, generatedAt = x.GeneratedAt }),
            criterionTrend,
            completion = new[]
            {
                new { day = "Mon", completed = 3 },
                new { day = "Tue", completed = 2 },
                new { day = "Wed", completed = 4 },
                new { day = "Thu", completed = 1 },
                new { day = "Fri", completed = 3 },
                new { day = "Sat", completed = 5 },
                new { day = "Sun", completed = 2 }
            },
            submissionVolume = new[]
            {
                new { week = "W1", submissions = 4 },
                new { week = "W2", submissions = 6 },
                new { week = "W3", submissions = 5 },
                new { week = "W4", submissions = 8 },
                new { week = "W5", submissions = 7 }
            },
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

    public Task<object> GetSubmissionsAsync(string userId, CancellationToken cancellationToken)
        => GetSubmissionsAsync(userId, cursor: null, limit: null, cancellationToken);

    public async Task<object> GetSubmissionsAsync(string userId, string? cursor, int? limit, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var pageSize = CursorPagination.NormalizeLimit(limit);
        if (IsSqliteProvider(db))
        {
            return await BuildSubmissionsResponseAsync(
                await GetSubmissionsSqlitePageAsync(userId, cursor, pageSize, cancellationToken),
                pageSize,
                cancellationToken);
        }

        var query = db.Attempts
            .AsNoTracking()
            .Where(x => x.UserId == userId);

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
                scoreEstimate = eval?.ScoreRange,
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
        CancellationToken cancellationToken)
    {
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
                ORDER BY COALESCE(""SubmittedAt"", ""StartedAt"") DESC, ""Id"" DESC
                LIMIT {pageSize + 1}");
        }
        else
        {
            query = db.Attempts.FromSqlInterpolated($@"
                SELECT *
                FROM ""Attempts""
                WHERE ""UserId"" = {userId}
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
                evaluation.ScoreRange))
            .ToListAsync(cancellationToken);
        var leftEval = evaluations.FirstOrDefault(evaluation => evaluation.AttemptId == left.Id);
        var rightEval = evaluations.FirstOrDefault(evaluation => evaluation.AttemptId == right.Id);

        return new
        {
            canCompare = true,
            left = new { attemptId = left.Id, evaluationId = leftEval?.Id, scoreRange = leftEval?.ScoreRange, subtest = left.SubtestCode },
            right = new { attemptId = right.Id, evaluationId = rightEval?.Id, scoreRange = rightEval?.ScoreRange, subtest = right.SubtestCode },
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

    public async Task<object> GetWritingHomeAsync(string userId, CancellationToken cancellationToken)
    {
        var profile = await EnsureLearnerProfileStateAsync(userId, cancellationToken);
        var examFamilyLabel = FormatExamFamilyLabel(profile.Goal.ExamFamilyCode);
        var tasks = await GetTasksBySubtestAsync("writing", cancellationToken);
        var attempts = await db.Attempts
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.SubtestCode == "writing")
            .OrderByDescending(x => x.SubmittedAt ?? x.StartedAt)
            .Take(4)
            .ToListAsync(cancellationToken);
        var draftAttempt = await db.Attempts
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.SubtestCode == "writing" && x.State == AttemptState.InProgress)
            .OrderByDescending(x => x.LastClientSyncAt ?? x.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var recentAttemptIds = attempts.Select(attempt => attempt.Id).ToArray();
        var latestEvaluationIdQuery = db.Evaluations
            .Where(evaluation =>
                evaluation.SubtestCode == "writing"
                && db.Attempts.Any(attempt =>
                    attempt.Id == evaluation.AttemptId
                    && attempt.UserId == userId
                    && attempt.SubtestCode == "writing"))
            .OrderByDescending(evaluation => evaluation.GeneratedAt)
            .Select(evaluation => evaluation.Id)
            .Take(1);
        var evaluationRows = await (
                from evaluation in db.Evaluations.AsNoTracking()
                join attempt in db.Attempts.AsNoTracking()
                    on evaluation.AttemptId equals attempt.Id
                join content in db.ContentItems.AsNoTracking()
                    on attempt.ContentId equals content.Id
                where attempt.UserId == userId
                      && attempt.SubtestCode == "writing"
                      && (recentAttemptIds.Contains(evaluation.AttemptId)
                          || latestEvaluationIdQuery.Contains(evaluation.Id))
                select new WritingHomeEvaluationRow(
                    evaluation,
                    attempt,
                    content,
                    latestEvaluationIdQuery.Contains(evaluation.Id)))
            .ToListAsync(cancellationToken);
        var latestEvaluationRow = evaluationRows.FirstOrDefault(row => row.IsLatest);
        var criterionDrillLibrary = latestEvaluationRow is not null
            ? JsonSupport.Deserialize<List<Dictionary<string, object?>>>(latestEvaluationRow.Evaluation.CriterionScoresJson, [])
                .OrderBy(x => ParseCriterionScore(x.GetValueOrDefault("scoreRange")?.ToString()))
                .Take(3)
                .Select(x => new
                {
                    criterionCode = x.GetValueOrDefault("criterionCode")?.ToString(),
                    criterionLabel = CriterionLabelFromCode(x.GetValueOrDefault("criterionCode")?.ToString()),
                    rationale = x.GetValueOrDefault("explanation")?.ToString() ?? "Target this criterion with a focused writing drill.",
                    route = $"/writing/tasks?criterion={x.GetValueOrDefault("criterionCode")}"
                })
                .ToList<object>()
            : tasks.Take(3).Select(task => task).ToList();
        var practiceLibrary = tasks.Take(4).ToList();
        var recommendedTask = practiceLibrary.FirstOrDefault();
        var evaluationByAttemptId = evaluationRows
            .Where(row => recentAttemptIds.Contains(row.Evaluation.AttemptId))
            .GroupBy(row => row.Evaluation.AttemptId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(row => row.Evaluation.GeneratedAt)
                    .First()
                    .Evaluation);
        object? latestEvaluationSummary = null;
        if (latestEvaluationRow is not null)
        {
            await RecordEventAsync(
                userId,
                "evaluation_viewed",
                new
                {
                    evaluationId = latestEvaluationRow.Evaluation.Id,
                    attemptId = latestEvaluationRow.Attempt.Id,
                    subtest = latestEvaluationRow.Evaluation.SubtestCode
                },
                cancellationToken);
            latestEvaluationSummary = BuildWritingEvaluationSummaryDto(
                latestEvaluationRow.Evaluation,
                latestEvaluationRow.Attempt,
                latestEvaluationRow.Content);
        }

        return new
        {
            recommendedTask,
            practiceLibrary,
            criterionDrillLibrary,
            pastSubmissions = attempts.Select(attempt =>
            {
                evaluationByAttemptId.TryGetValue(attempt.Id, out var evaluation);
                return new
                {
                    attemptId = attempt.Id,
                    contentId = attempt.ContentId,
                    state = ToApiState(attempt.State),
                    scoreEstimate = evaluation?.ScoreRange,
                    route = evaluation?.Id is null ? $"/writing/attempt/{attempt.Id}" : $"/writing/result?id={Uri.EscapeDataString(evaluation.Id)}"
                };
            }),
            reviewCredits = new
            {
                available = profile.Wallet.CreditBalance,
                route = "/reviews",
                billingRoute = "/billing"
            },
            fullMockEntry = new
            {
                title = $"{examFamilyLabel} Full Mock Test",
                route = "/mocks",
                rationale = $"Use a full mock to confirm whether your {examFamilyLabel} writing gains are transferring under timed conditions."
            },
            featuredTasks = tasks.Take(3),
            latestEvaluation = latestEvaluationSummary,
            actions = new[]
            {
                new { label = "Browse Writing Tasks", route = "/writing/tasks" },
                new { label = draftAttempt is null ? "Start Writing Task" : "Resume Draft", route = draftAttempt is null ? "/writing/tasks" : $"/writing/attempt/{draftAttempt.Id}" }
            }
        };
    }

    public async Task<List<object>> GetWritingTasksAsync(CancellationToken cancellationToken) => await GetTasksBySubtestAsync("writing", cancellationToken);

    public async Task<object> GetWritingTaskAsync(string contentId, CancellationToken cancellationToken)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId && x.SubtestCode == "writing" && x.Status == ContentStatus.Published, cancellationToken)
                   ?? throw ApiException.NotFound("content_not_found", "Writing task not found.");
        var detail = JsonSupport.Deserialize<Dictionary<string, object?>>(item.DetailJson, new Dictionary<string, object?>());
        return Merge(new Dictionary<string, object?>
        {
            ["contentId"] = item.Id,
            ["contentType"] = item.ContentType,
            ["subtest"] = item.SubtestCode,
            ["professionId"] = item.ProfessionId,
            ["title"] = item.Title,
            ["difficulty"] = item.Difficulty,
            ["estimatedDurationMinutes"] = item.EstimatedDurationMinutes,
            ["criteriaFocus"] = JsonSupport.Deserialize<List<string>>(item.CriteriaFocusJson, []),
            ["scenarioType"] = item.ScenarioType,
            ["modeSupport"] = JsonSupport.Deserialize<List<string>>(item.ModeSupportJson, []),
            ["publishedRevisionId"] = item.PublishedRevisionId,
            ["status"] = ToContentStatus(item.Status),
            ["caseNotes"] = item.CaseNotes
        }, detail);
    }

    public async Task<object> CreateWritingAttemptAsync(string userId, CreateAttemptRequest request, CancellationToken cancellationToken)
        => await CreateAttemptAsync(userId, request, "writing", cancellationToken);

    public async Task<object> GetWritingAttemptAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        var attempt = await GetWritingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        return await GetAttemptAsync(attempt.Id, cancellationToken);
    }

    public async Task<object> GetWritingPaperAssetsAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        _ = await GetWritingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        var assets = await LoadWritingPaperAssetResponsesAsync(attemptId, cancellationToken);
        return new
        {
            attemptId,
            assets,
            extractionState = SummarizeExtractionState(assets),
            extractedText = await BuildWritingPaperExtractedTextAsync(attemptId, cancellationToken)
        };
    }

    public async Task<object> AttachWritingPaperAssetsAsync(string userId, string attemptId, WritingPaperAssetAttachRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetWritingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        EnsureWritingDraftEditable(attempt);

        var mediaAssetIds = (request.MediaAssetIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        if (mediaAssetIds.Count == 0)
        {
            throw ApiException.Validation(
                "paper_assets_required",
                "Upload at least one handwritten writing page before continuing.",
                [new ApiFieldError("mediaAssetIds", "required", "Upload one or more image or PDF pages.")]);
        }

        if (request.ReplaceExisting == true)
        {
            var existingRows = await db.WritingAttemptAssets.Where(asset => asset.AttemptId == attempt.Id).ToListAsync(cancellationToken);
            db.WritingAttemptAssets.RemoveRange(existingRows);
        }

        var mediaAssets = await db.MediaAssets
            .Where(asset => mediaAssetIds.Contains(asset.Id))
            .ToListAsync(cancellationToken);
        if (mediaAssets.Count != mediaAssetIds.Count)
        {
            throw ApiException.Validation(
                "paper_asset_not_found",
                "One or more uploaded files could not be found.",
                [new ApiFieldError("mediaAssetIds", "not_found", "Re-upload the missing page and try again.")]);
        }

        var now = DateTimeOffset.UtcNow;
        var existingByMediaId = await db.WritingAttemptAssets
            .Where(asset => asset.AttemptId == attempt.Id && mediaAssetIds.Contains(asset.MediaAssetId))
            .ToDictionaryAsync(asset => asset.MediaAssetId, cancellationToken);
        var pageNumber = await db.WritingAttemptAssets
            .Where(asset => asset.AttemptId == attempt.Id)
            .Select(asset => (int?)asset.PageNumber)
            .MaxAsync(cancellationToken) ?? 0;

        foreach (var media in mediaAssets.OrderBy(asset => mediaAssetIds.IndexOf(asset.Id)))
        {
            ValidateWritingPaperMediaAsset(userId, media);
            if (!existingByMediaId.TryGetValue(media.Id, out var writingAsset))
            {
                writingAsset = new WritingAttemptAsset
                {
                    Id = $"wpa-{Guid.NewGuid():N}",
                    AttemptId = attempt.Id,
                    UserId = userId,
                    MediaAssetId = media.Id,
                    PageNumber = ++pageNumber,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.WritingAttemptAssets.Add(writingAsset);
            }

            if (!string.Equals(writingAsset.ExtractionState, "completed", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(writingAsset.ExtractedText))
            {
                await ExtractWritingPaperAssetAsync(writingAsset, media, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        var assets = await LoadWritingPaperAssetResponsesAsync(attempt.Id, cancellationToken);
        var extractedText = await BuildWritingPaperExtractedTextAsync(attempt.Id, cancellationToken);
        MergeWritingAttemptMetadata(attempt, "paper", "pending", assets.Select(asset => asset.MediaAssetId), extractedText.Length);
        await db.SaveChangesAsync(cancellationToken);

        return new
        {
            attemptId = attempt.Id,
            assets,
            extractionState = SummarizeExtractionState(assets),
            extractedText,
            extractedCharCount = extractedText.Length,
            wordCount = CountWords(extractedText)
        };
    }

    public async Task<object> UpdateWritingDraftAsync(string userId, string attemptId, DraftUpdateRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetWritingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        EnsureWritingDraftEditable(attempt);
        EnsureWritingReadOnlyPhaseAllowsDraftMutation(attempt, request);
        if (request.DraftVersion.HasValue && request.DraftVersion.Value != attempt.DraftVersion)
        {
            throw ApiException.Conflict(
                "draft_version_conflict",
                "This draft has changed since your last save. Refresh the latest server version before saving again.",
                [new ApiFieldError("draftVersion", "stale_value", "The draft version is stale.")]);
        }

        if (request.Content is not null) attempt.DraftContent = request.Content;
        if (request.Scratchpad is not null) attempt.Scratchpad = request.Scratchpad;
        if (request.Checklist is not null) attempt.ChecklistJson = JsonSupport.Serialize(request.Checklist);
        attempt.DraftVersion += 1;
        attempt.LastClientSyncAt = DateTimeOffset.UtcNow;
        attempt.State = AttemptState.InProgress;
        await db.SaveChangesAsync(cancellationToken);

        return new
        {
            attemptId = attempt.Id,
            saved = true,
            draftVersion = attempt.DraftVersion,
            lastSavedAt = attempt.LastClientSyncAt,
            state = ToApiState(attempt.State),
            saveState = new
            {
                state = "saved",
                message = "Draft saved.",
                lastSavedAt = attempt.LastClientSyncAt
            }
        };
    }

    private static void EnsureWritingDraftEditable(Attempt attempt)
    {
        if (attempt.State is AttemptState.NotStarted or AttemptState.InProgress or AttemptState.Paused)
        {
            return;
        }

        throw ApiException.Conflict(
            "writing_attempt_locked",
            "This writing attempt has already been submitted and cannot be edited.",
            [new ApiFieldError("attemptId", "locked", "Start a new writing attempt before editing another response.")]);
    }

    public async Task<object> HeartbeatAttemptAsync(string userId, string attemptId, HeartbeatRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        return await HeartbeatAttemptCoreAsync(attempt, request, cancellationToken);
    }

    public async Task<object> HeartbeatSpeakingAttemptAsync(string userId, string attemptId, HeartbeatRequest request, CancellationToken cancellationToken)
        => await HeartbeatSubtestAttemptAsync(userId, attemptId, request, "speaking", cancellationToken);

    public async Task<object> HeartbeatListeningAttemptAsync(string userId, string attemptId, HeartbeatRequest request, CancellationToken cancellationToken)
        => await HeartbeatSubtestAttemptAsync(userId, attemptId, request, "listening", cancellationToken);

    public async Task<object> HeartbeatWritingAttemptAsync(string userId, string attemptId, HeartbeatRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetWritingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        return await HeartbeatAttemptCoreAsync(attempt, request, cancellationToken);
    }

    private async Task<object> HeartbeatSubtestAttemptAsync(string userId, string attemptId, HeartbeatRequest request, string subtest, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetSubtestAttemptOwnedByUserAsync(userId, attemptId, subtest, $"{subtest}_attempt_not_found", $"{ToDisplaySubtest(subtest)} attempt not found.", cancellationToken);
        return await HeartbeatAttemptCoreAsync(attempt, request, cancellationToken);
    }

    private async Task<object> HeartbeatAttemptCoreAsync(Attempt attempt, HeartbeatRequest request, CancellationToken cancellationToken)
    {
        attempt.ElapsedSeconds = request.ElapsedSeconds;
        attempt.LastClientSyncAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.DeviceType)) attempt.DeviceType = request.DeviceType;
        await db.SaveChangesAsync(cancellationToken);
        return new { attemptId = attempt.Id, elapsedSeconds = attempt.ElapsedSeconds, lastClientSyncAt = attempt.LastClientSyncAt };
    }

    public async Task<object> SubmitWritingAttemptAsync(string userId, string attemptId, SubmitAttemptRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetWritingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        var examMode = NormalizeWritingExamMode(request.ExamMode);
        var assessorType = NormalizeWritingAssessorType(request.AssessorType);
        var idempotencyScope = $"writing-submit:{userId}:{attempt.Id}";
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var cached = await GetIdempotentResponseAsync(idempotencyScope, request.IdempotencyKey, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }
        }

        if (attempt.State is AttemptState.Submitted or AttemptState.Evaluating or AttemptState.Completed)
        {
            var existing = await db.Evaluations.FirstOrDefaultAsync(x => x.AttemptId == attemptId, cancellationToken);
            var existingReview = await db.ReviewRequests
                .Where(review => review.AttemptId == attemptId && review.State != ReviewRequestState.Cancelled && review.State != ReviewRequestState.Failed)
                .OrderByDescending(review => review.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (existingReview is not null && existing is null)
            {
                return new
                {
                    attemptId = attempt.Id,
                    reviewRequestId = existingReview.Id,
                    state = ToReviewRequestState(existingReview.State),
                    examMode,
                    assessorType = "instructor"
                };
            }

            return new { attemptId = attempt.Id, evaluationId = existing?.Id, state = existing is null ? "queued" : ToAsyncState(existing.State), examMode, assessorType };
        }

        var proposedContent = examMode == "paper" ? request.Content ?? await BuildWritingPaperExtractedTextAsync(attempt.Id, cancellationToken) : request.Content ?? attempt.DraftContent;
        if (examMode == "computer")
        {
            EnsureWritingReadOnlyPhaseAllowsContentMutation(attempt, proposedContent);
        }

        if (examMode == "paper")
        {
            if (request.PaperAssetIds?.Count > 0)
            {
                await AttachWritingPaperAssetsAsync(userId, attempt.Id, new WritingPaperAssetAttachRequest(request.PaperAssetIds, false), cancellationToken);
            }

            var extractedText = await BuildWritingPaperExtractedTextAsync(attempt.Id, cancellationToken);
            var incompletePaperAssets = await db.WritingAttemptAssets
                .CountAsync(asset => asset.AttemptId == attempt.Id && asset.ExtractionState != "completed", cancellationToken);
            if (incompletePaperAssets > 0)
            {
                throw ApiException.Validation(
                    "paper_ocr_incomplete",
                    "OCR must finish successfully for every handwritten page before submission.",
                    [new ApiFieldError("paperAssetIds", "ocr_incomplete", "Re-upload failed pages or wait for extraction before submitting.")]);
            }
            if (string.IsNullOrWhiteSpace(extractedText))
            {
                throw ApiException.Validation(
                    "paper_ocr_required",
                    "We could not extract enough text from the handwritten pages. Re-upload clearer pages before submitting.",
                    [new ApiFieldError("paperAssetIds", "ocr_required", "Paper-based assessment needs successful OCR before grading or tutor review.")]);
            }

            attempt.DraftContent = extractedText;
        }
        else if (request.Content is not null)
        {
            attempt.DraftContent = request.Content;
        }

        if (string.IsNullOrWhiteSpace(attempt.DraftContent))
        {
            throw ApiException.Validation(
                "writing_content_required",
                "Writing content is required before submission.",
                [new ApiFieldError("content", "required", "Enter your response before submitting.")]);
        }

        // Free-tier / kill-switch entitlement gate. Premium subscribers
        // pass through unconditionally; free tier respects the runtime
        // WritingOptions singleton (default: premium-only).
        if (assessorType == "ai" && writingEntitlement is not null)
        {
            var ent = await writingEntitlement.CheckAsync(userId, cancellationToken);
            if (!ent.Allowed)
            {
                var msg = ent.Reason switch
                {
                    "premium_required" => "Writing practice requires an active subscription.",
                    "quota_exceeded" => $"Free tier allows {ent.LimitPerWindow} writing attempts every {ent.WindowDays} days.",
                    _ => ent.Reason,
                };
                throw ApiException.PaymentRequired("writing_quota_exceeded", msg);
            }
        }

        List<string> paperAssetIds = examMode == "paper"
            ? await db.WritingAttemptAssets
                .Where(asset => asset.AttemptId == attempt.Id)
                .OrderBy(asset => asset.PageNumber)
                .Select(asset => asset.MediaAssetId)
                .ToListAsync(cancellationToken)
            : new List<string>();
        MergeWritingAttemptMetadata(attempt, examMode, assessorType, paperAssetIds, attempt.DraftContent.Length);

        if (assessorType == "instructor")
        {
            attempt.State = AttemptState.Completed;
            attempt.SubmittedAt = DateTimeOffset.UtcNow;
            attempt.CompletedAt = DateTimeOffset.UtcNow;
            attempt.LastClientSyncAt = DateTimeOffset.UtcNow;
            await LearnerWorkflowCoordinator.UpdateDiagnosticProgressAsync(db, attempt, AttemptState.Completed, cancellationToken);

            var reviewResponse = await CreateReviewRequestCoreAsync(userId, new ReviewRequestCreateRequest(
                attempt.Id,
                "writing",
                string.IsNullOrWhiteSpace(request.TurnaroundOption) ? "standard" : request.TurnaroundOption,
                request.FocusAreas ?? ["OET writing criteria", "voice-note feedback"],
                BuildInstructorLearnerNotes(request.LearnerNotes, examMode),
                "credits",
                request.IdempotencyKey is null ? null : $"writing-instructor-{request.IdempotencyKey}"), cancellationToken);

            var review = await db.ReviewRequests
                .Where(existingReview => existingReview.AttemptId == attempt.Id && existingReview.State != ReviewRequestState.Cancelled)
                .OrderByDescending(existingReview => existingReview.CreatedAt)
                .FirstAsync(cancellationToken);
            await TryAssignDrAhmedAsync(review.Id, cancellationToken);
            await RecordEventAsync(userId, "writing_instructor_review_requested", new { attemptId = attempt.Id, reviewRequestId = review.Id, examMode, contentId = attempt.ContentId }, cancellationToken);

            var response = new
            {
                attemptId = attempt.Id,
                reviewRequestId = review.Id,
                state = "queued_for_instructor_review",
                examMode,
                assessorType,
                review = reviewResponse
            };
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                await SaveIdempotentResponseAsync(idempotencyScope, request.IdempotencyKey, response, cancellationToken);
            }
            await db.SaveChangesAsync(cancellationToken);
            return response;
        }

        attempt.State = AttemptState.Evaluating;
        attempt.SubmittedAt = DateTimeOffset.UtcNow;
        attempt.LastClientSyncAt = DateTimeOffset.UtcNow;
        await LearnerWorkflowCoordinator.UpdateDiagnosticProgressAsync(db, attempt, AttemptState.Evaluating, cancellationToken);

        var evaluationId = $"we-{Guid.NewGuid():N}";
        if (aiPackageCreditService is not null)
        {
            // A Writing exam (one letter — no parts) costs two grading credits,
            // taken atomically in one debit so a failed grade refunds both.
            var debit = await aiPackageCreditService.DeductGradingCreditAsync(
                userId, "writing", evaluationId, AiGradingCreditCost.WritingExam, cancellationToken);
            if (!debit.Debited)
            {
                throw ApiException.PaymentRequired(
                    debit.ErrorCode ?? "no_ai_package_credits",
                    debit.ErrorMessage ?? "You have no credits remaining. Purchase a package to continue.");
            }
        }

        var evaluation = new Evaluation
        {
            Id = evaluationId,
            AttemptId = attempt.Id,
            SubtestCode = "writing",
            State = AsyncState.Queued,
            ScoreRange = "pending",
            ConfidenceBand = ConfidenceBand.Low,
            StrengthsJson = "[]",
            IssuesJson = "[]",
            CriterionScoresJson = "[]",
            FeedbackItemsJson = "[]",
            ModelExplanationSafe = "Evaluation queued.",
            LearnerDisclaimer = "Estimated training result pending.",
            StatusReasonCode = "queued",
            StatusMessage = "Writing evaluation queued.",
            Retryable = true,
            RetryAfterMs = 2000,
            LastTransitionAt = DateTimeOffset.UtcNow
        };
        db.Evaluations.Add(evaluation);
        await QueueJobAsync(JobType.WritingEvaluation, attemptId: attempt.Id, resourceId: evaluation.Id, cancellationToken: cancellationToken);
        var aiResponse = new { attemptId = attempt.Id, evaluationId = evaluation.Id, state = "queued", nextPollAfterMs = 2000, examMode, assessorType };
        await RecordEventAsync(attempt.UserId, "task_submitted", new { attemptId = attempt.Id, evaluationId = evaluation.Id, subtest = "writing", contentId = attempt.ContentId, examMode, assessorType }, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            await SaveIdempotentResponseAsync(idempotencyScope, request.IdempotencyKey, aiResponse, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        return aiResponse;
    }

    public async Task<object> GetReviewVoiceNotesAsync(string userId, string reviewRequestId, CancellationToken cancellationToken)
    {
        var review = await GetReviewRequestOwnedByUserAsync(userId, reviewRequestId, cancellationToken);
        if (review.State != ReviewRequestState.Completed)
        {
            return new { reviewRequestId, items = Array.Empty<ReviewVoiceNoteResponse>() };
        }

        var notes = await LoadReviewVoiceNoteResponsesAsync(reviewRequestId, readyOnly: true, cancellationToken);
        return new { reviewRequestId, items = notes };
    }

    public async Task<object> GetReviewResultAsync(string userId, string reviewRequestId, CancellationToken cancellationToken)
    {
        var review = await GetReviewRequestOwnedByUserAsync(userId, reviewRequestId, cancellationToken);
        if (review.State != ReviewRequestState.Completed)
        {
            throw ApiException.NotFound("review_result_not_ready", "This tutor review result is not ready yet.");
        }

        var submittedDraft = await db.ExpertReviewDrafts
            .AsNoTracking()
            .Where(draft => draft.ReviewRequestId == reviewRequestId && draft.State == "submitted")
            .OrderByDescending(draft => draft.DraftSavedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw ApiException.NotFound("review_result_not_found", "Tutor review result was not found.");

        var scores = JsonSupport.Deserialize(submittedDraft.RubricEntriesJson, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        var comments = JsonSupport.Deserialize(submittedDraft.CriterionCommentsJson, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var criteria = OrderReviewCriteria(review.SubtestCode, scores.Keys)
            .Select(code =>
            {
                var score = scores.GetValueOrDefault(code);
                var maxScore = ReviewCriterionMaxScore(review.SubtestCode, code);
                return new
                {
                    code,
                    name = CriterionLabelFromCode(code),
                    score,
                    maxScore,
                    explanation = comments.GetValueOrDefault(code) ?? string.Empty
                };
            })
            .ToList();
        var totalScore = criteria.Sum(criterion => criterion.score);
        var totalMaxScore = criteria.Sum(criterion => criterion.maxScore);

        return new
        {
            reviewRequestId = review.Id,
            attemptId = review.AttemptId,
            subtest = review.SubtestCode,
            state = ToReviewRequestState(review.State),
            completedAt = review.CompletedAt,
            submittedAt = submittedDraft.DraftSavedAt,
            finalComment = submittedDraft.FinalCommentDraft,
            scoreLabel = totalMaxScore > 0 ? $"Dr. Ahmed rubric {totalScore}/{totalMaxScore}" : "Dr. Ahmed reviewed",
            scores,
            criterionComments = comments,
            criteria
        };
    }

    private static string NormalizeWritingExamMode(string? examMode)
    {
        var normalized = (examMode ?? "computer").Trim().ToLowerInvariant().Replace("_", "-");
        return normalized switch
        {
            "paper" or "paper-based" or "handwritten" => "paper",
            "computer" or "computer-based" or "typed" => "computer",
            _ => throw ApiException.Validation(
                "invalid_writing_exam_mode",
                "Choose either computer-based or paper-based writing mode.",
                [new ApiFieldError("examMode", "invalid", "Use computer or paper.")])
        };
    }

    private static string NormalizeWritingAssessorType(string? assessorType)
    {
        var normalized = (assessorType ?? "ai").Trim().ToLowerInvariant().Replace("_", "-");
        return normalized switch
        {
            "ai" or "ai-assessment" or "automatic" => "ai",
            "instructor" or "dr-ahmed" or "doctor-ahmed" or "human" or "tutor" => "instructor",
            _ => throw ApiException.Validation(
                "invalid_writing_assessor",
                "Choose either AI assessment or Dr. Ahmed instructor assessment.",
                [new ApiFieldError("assessorType", "invalid", "Use ai or instructor.")])
        };
    }

    private static string BuildInstructorLearnerNotes(string? learnerNotes, string examMode)
    {
        var modeLabel = examMode == "paper" ? "Paper-based handwritten submission" : "Computer-based typed submission";
        return string.IsNullOrWhiteSpace(learnerNotes)
            ? $"Dr. Ahmed instructor assessment requested. {modeLabel}. Voice-note feedback expected."
            : $"Dr. Ahmed instructor assessment requested. {modeLabel}. Voice-note feedback expected.\n\nLearner notes: {learnerNotes.Trim()}";
    }

    private async Task TryAssignDrAhmedAsync(string reviewRequestId, CancellationToken cancellationToken)
    {
        var existingAssignment = await db.ExpertReviewAssignments
            .FirstOrDefaultAsync(assignment => assignment.ReviewRequestId == reviewRequestId
                && assignment.ClaimState != ExpertAssignmentState.Released, cancellationToken);
        if (existingAssignment is not null)
        {
            return;
        }

        var drAhmed = await db.ExpertUsers
            .Where(expert => expert.IsActive
                && (expert.DisplayName.ToLower().Contains("ahmed")
                    || expert.Email.ToLower().Contains("ahmed")))
            .OrderBy(expert => expert.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);
        if (drAhmed is null)
        {
            return;
        }

        db.ExpertReviewAssignments.Add(new ExpertReviewAssignment
        {
            Id = $"era-{Guid.NewGuid():N}",
            ReviewRequestId = reviewRequestId,
            AssignedReviewerId = drAhmed.Id,
            AssignedBy = "system-dr-ahmed-routing",
            AssignedAt = DateTimeOffset.UtcNow,
            ClaimState = ExpertAssignmentState.Assigned,
            ReasonCode = "dr-ahmed-writing-assessment"
        });
    }

    private static void ValidateWritingPaperMediaAsset(string userId, MediaAsset media)
    {
        if (!string.Equals(media.UploadedBy, userId, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Forbidden("paper_asset_forbidden", "You can only submit paper assets uploaded by your account.");
        }

        var isSupportedPaperFile = string.Equals(media.MimeType, "application/pdf", StringComparison.OrdinalIgnoreCase)
            || string.Equals(media.MimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(media.MimeType, "image/png", StringComparison.OrdinalIgnoreCase);
        if (!isSupportedPaperFile)
        {
            throw ApiException.Validation(
                "invalid_paper_asset_type",
                "Writing paper submissions must be JPG, PNG, or PDF pages.",
                [new ApiFieldError("mediaAssetIds", "invalid_type", "Upload JPG, PNG, or PDF pages.")]);
        }
    }

    private async Task ExtractWritingPaperAssetAsync(WritingAttemptAsset writingAsset, MediaAsset media, CancellationToken cancellationToken)
    {
        writingAsset.ExtractionState = "processing";
        writingAsset.UpdatedAt = DateTimeOffset.UtcNow;
        writingAsset.ExtractionProvider = "auto-docintel-pdfpig";
        writingAsset.ExtractionReasonCode = null;
        writingAsset.ExtractionMessage = null;

        try
        {
            await using var stream = await fileStorage.OpenReadAsync(media.StoragePath, cancellationToken);
            var text = (await pdfTextExtractor.ExtractAsync(stream, cancellationToken)).Trim();
            writingAsset.ExtractedText = text;
            writingAsset.ExtractedAt = DateTimeOffset.UtcNow;
            writingAsset.UpdatedAt = writingAsset.ExtractedAt.Value;

            if (text.Length < 20)
            {
                writingAsset.ExtractionState = "failed";
                writingAsset.ExtractionReasonCode = "ocr_no_text";
                writingAsset.ExtractionMessage = "OCR completed but did not find enough readable text.";
                return;
            }

            writingAsset.ExtractionState = "completed";
            writingAsset.ExtractionMessage = "Text extracted successfully.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            writingAsset.ExtractionState = "failed";
            writingAsset.ExtractedText = string.Empty;
            writingAsset.ExtractionReasonCode = "ocr_failed";
            writingAsset.ExtractionMessage = "OCR failed for this page. Re-upload a clearer scan or use computer-based entry.";
            writingAsset.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private async Task<IReadOnlyList<WritingPaperAssetResponse>> LoadWritingPaperAssetResponsesAsync(string attemptId, CancellationToken cancellationToken)
    {
        var rows = await db.WritingAttemptAssets
            .AsNoTracking()
            .Where(asset => asset.AttemptId == attemptId)
            .OrderBy(asset => asset.PageNumber)
            .Join(db.MediaAssets.AsNoTracking(), asset => asset.MediaAssetId, media => media.Id, (asset, media) => new { asset, media })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new WritingPaperAssetResponse(
            row.asset.Id,
            row.media.Id,
            row.media.OriginalFilename,
            row.media.MimeType,
            row.media.Format,
            row.media.SizeBytes,
            row.asset.PageNumber,
            row.asset.ExtractionState,
            row.asset.ExtractedText?.Length ?? 0,
            row.asset.ExtractionMessage,
            $"/v1/media/{row.media.Id}/content")).ToList();
    }

    private async Task<string> BuildWritingPaperExtractedTextAsync(string attemptId, CancellationToken cancellationToken)
    {
        var texts = await db.WritingAttemptAssets
            .AsNoTracking()
            .Where(asset => asset.AttemptId == attemptId && asset.ExtractionState == "completed")
            .OrderBy(asset => asset.PageNumber)
            .Select(asset => asset.ExtractedText)
            .ToListAsync(cancellationToken);
        return string.Join("\n\n", texts.Where(text => !string.IsNullOrWhiteSpace(text)).Select(text => text.Trim()));
    }

    private static string SummarizeExtractionState(IReadOnlyList<WritingPaperAssetResponse> assets)
    {
        if (assets.Count == 0) return "empty";
        if (assets.Any(asset => asset.ExtractionState == "processing" || asset.ExtractionState == "queued")) return "processing";
        if (assets.All(asset => asset.ExtractionState == "completed")) return "completed";
        if (assets.Any(asset => asset.ExtractionState == "completed")) return "partial";
        return "failed";
    }

    private static int CountWords(string text)
        => string.IsNullOrWhiteSpace(text) ? 0 : Regex.Matches(text, @"\b[\p{L}\p{N}']+\b").Count;

    private static void MergeWritingAttemptMetadata(Attempt attempt, string examMode, string assessorType, IEnumerable<string> paperAssetIds, int extractedCharCount)
    {
        var analysis = JsonSupport.Deserialize<Dictionary<string, object?>>(attempt.AnalysisJson, new Dictionary<string, object?>());
        analysis["writingSubmission"] = new
        {
            examMode,
            assessorType,
            submittedVia = examMode == "paper" ? "paper-upload" : "typed-editor",
            paperAssetIds = paperAssetIds.ToArray(),
            extractedCharCount,
            reviewer = assessorType == "instructor" ? "dr-ahmed" : null,
            updatedAt = DateTimeOffset.UtcNow
        };
        attempt.AnalysisJson = JsonSupport.Serialize(analysis);
    }

    private async Task<IReadOnlyList<ReviewVoiceNoteResponse>> LoadReviewVoiceNoteResponsesAsync(string reviewRequestId, bool readyOnly, CancellationToken cancellationToken)
    {
        var query = db.ReviewVoiceNotes
            .AsNoTracking()
            .Where(note => note.ReviewRequestId == reviewRequestId);
        if (readyOnly)
        {
            query = query.Where(note => note.Status == "ready");
        }

        var rows = await query
            .OrderByDescending(note => note.CreatedAt)
            .Join(db.MediaAssets.AsNoTracking(), note => note.MediaAssetId, media => media.Id, (note, media) => new { note, media })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new ReviewVoiceNoteResponse(
            row.note.Id,
            row.note.ReviewRequestId,
            row.media.Id,
            row.media.OriginalFilename,
            row.media.MimeType,
            row.note.DurationSeconds,
            row.note.TranscriptText,
            row.note.WrittenNotes,
            JsonSupport.Deserialize<Dictionary<string, int>>(row.note.RubricJson, new Dictionary<string, int>()),
            row.note.Status,
            row.note.CreatedAt,
            $"/v1/media/{row.media.Id}/content")).ToList();
    }

    private static void EnsureWritingReadOnlyPhaseAllowsDraftMutation(Attempt attempt, DraftUpdateRequest request)
    {
        var hasDraftMutation = request.Content is not null
            || request.Scratchpad is not null
            || request.Checklist is not null;
        EnsureWritingReadOnlyPhaseAllowsMutation(attempt, hasDraftMutation);
    }

    private static void EnsureWritingReadOnlyPhaseAllowsContentMutation(Attempt attempt, string? proposedContent)
        => EnsureWritingReadOnlyPhaseAllowsMutation(attempt, !string.IsNullOrWhiteSpace(proposedContent));

    private static void EnsureWritingReadOnlyPhaseAllowsMutation(Attempt attempt, bool hasWritingBearingMutation)
    {
        if (!string.Equals(attempt.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase)) return;
        if (!string.Equals(attempt.Mode, "exam", StringComparison.OrdinalIgnoreCase)) return;
        if (!hasWritingBearingMutation) return;

        var readingWindowEndsAt = attempt.StartedAt.AddMinutes(5);
        if (DateTimeOffset.UtcNow >= readingWindowEndsAt) return;

        throw ApiException.Conflict(
            "writing_reading_window_active",
            "The first 5 minutes of OET Writing exam mode are reading-only. Writing is accepted after the reading window ends.");
    }

    public async Task<object> GetWritingEvaluationSummaryAsync(string userId, string evaluationId, CancellationToken cancellationToken)
    {
        var evaluation = await GetEvaluationOwnedByUserAsync(userId, evaluationId, cancellationToken);
        var attempt = await db.Attempts.FirstAsync(x => x.Id == evaluation.AttemptId, cancellationToken);
        var content = await db.ContentItems.FirstAsync(x => x.Id == attempt.ContentId, cancellationToken);
        await RecordEventAsync(userId, "evaluation_viewed", new { evaluationId = evaluation.Id, attemptId = attempt.Id, subtest = evaluation.SubtestCode }, cancellationToken);
        return BuildWritingEvaluationSummaryDto(evaluation, attempt, content);
    }

    private static object BuildWritingEvaluationSummaryDto(
        Evaluation evaluation,
        Attempt attempt,
        ContentItem content)
    {
        var examFamilyCode = NormalizeExamFamilyCode(attempt.ExamFamilyCode);
        var examFamilyLabel = FormatExamFamilyLabel(examFamilyCode);
        return new
        {
            evaluationId = evaluation.Id,
            attemptId = attempt.Id,
            taskId = content.Id,
            taskTitle = content.Title,
            profession = content.ProfessionId ?? "medicine",
            examFamilyCode,
            examFamilyLabel,
            subtest = evaluation.SubtestCode,
            state = ToAsyncState(evaluation.State),
            scoreRange = evaluation.ScoreRange,
            gradeRange = evaluation.GradeRange,
            confidenceBand = evaluation.ConfidenceBand.ToString().ToLowerInvariant(),
            confidenceLabel = BuildConfidenceLabel(evaluation.ConfidenceBand),
            strengths = JsonSupport.Deserialize<List<string>>(evaluation.StrengthsJson, []),
            issues = JsonSupport.Deserialize<List<string>>(evaluation.IssuesJson, []),
            generatedAt = evaluation.GeneratedAt,
            recommendedDrills = new[]
            {
                new
                {
                    id = $"phrasing-{evaluation.Id}",
                    title = "Phrasing and transcript drill",
                    description = "Practise stronger patient-centred alternatives from the marked transcript.",
                    route = $"/speaking/phrasing/{evaluation.Id}"
                },
                new
                {
                    id = "ai-patient-practice",
                    title = "AI patient conversation practice",
                    description = "Launch the existing conversation module for a grounded patient practice session.",
                    route = "/conversation"
                }
            },
            modelExplanationSafe = evaluation.ModelExplanationSafe,
            learnerDisclaimer = evaluation.LearnerDisclaimer,
            isOfficialScore = false,
            methodLabel = BuildAiMethodLabel(evaluation.SubtestCode),
            provenanceLabel = $"{examFamilyLabel} practice estimate",
            humanReviewRecommended = ShouldRecommendHumanReview(evaluation.ConfidenceBand),
            escalationRecommended = ShouldRecommendHumanReview(evaluation.ConfidenceBand),
            statusReasonCode = evaluation.StatusReasonCode,
            retryable = evaluation.Retryable,
            retryAfterMs = evaluation.RetryAfterMs
        };
    }

    public async Task<object> GetWritingFeedbackAsync(string userId, string evaluationId, CancellationToken cancellationToken)
    {
        var summary = await GetWritingEvaluationSummaryAsync(userId, evaluationId, cancellationToken);
        var evaluation = await GetEvaluationOwnedByUserAsync(userId, evaluationId, cancellationToken);
        return new
        {
            summary,
            criterionScores = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.CriterionScoresJson, []),
            feedbackItems = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.FeedbackItemsJson, [])
        };
    }

    public async Task<object> GetWritingRevisionAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        var requestedAttempt = await GetWritingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        var attempt = requestedAttempt.ParentAttemptId is null
            ? requestedAttempt
            : await GetWritingAttemptOwnedByUserAsync(userId, requestedAttempt.ParentAttemptId, cancellationToken);
        var evaluation = await GetCompletedWritingEvaluationForRevisionAsync(attempt.Id, cancellationToken);
        var related = await db.Attempts.Where(x => x.ParentAttemptId == attempt.Id && x.UserId == userId && x.SubtestCode == "writing").OrderByDescending(x => x.StartedAt).ToListAsync(cancellationToken);
        var latestRevision = related.FirstOrDefault();
        var latestRevisionEvaluation = latestRevision is null
            ? null
            : await db.Evaluations.Where(x => x.AttemptId == latestRevision.Id).OrderByDescending(x => x.GeneratedAt).FirstOrDefaultAsync(cancellationToken);
        await RecordEventAsync(userId, "revision_started", new { attemptId = attempt.Id, subtest = attempt.SubtestCode }, cancellationToken);

        var baseCriterionScores = evaluation is null
            ? []
            : JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.CriterionScoresJson, []);
        var revisedCriterionScores = latestRevisionEvaluation is null
            ? baseCriterionScores
            : JsonSupport.Deserialize<List<Dictionary<string, object?>>>(latestRevisionEvaluation.CriterionScoresJson, []);

        var deltaSummary = baseCriterionScores.Select(baseScore =>
        {
            var code = baseScore.GetValueOrDefault("criterionCode")?.ToString();
            var revised = revisedCriterionScores.FirstOrDefault(x => x.GetValueOrDefault("criterionCode")?.ToString() == code);
            return new
            {
                name = CriterionLabelFromCode(code),
                original = ParseCriterionScore(baseScore.GetValueOrDefault("scoreRange")?.ToString()),
                revised = ParseCriterionScore(revised?.GetValueOrDefault("scoreRange")?.ToString() ?? baseScore.GetValueOrDefault("scoreRange")?.ToString()),
                max = 6
            };
        }).ToList();

        var unresolvedIssues = evaluation is null
            ? new List<string>()
            : JsonSupport.Deserialize<List<string>>(evaluation.IssuesJson, []);

        return new
        {
            baseAttempt = new { attemptId = attempt.Id, content = attempt.DraftContent, draftVersion = attempt.DraftVersion },
            revisionDraft = new { attemptId = latestRevision?.Id, content = latestRevision?.DraftContent ?? attempt.DraftContent },
            latestEvaluationId = evaluation?.Id,
            criterionScores = baseCriterionScores,
            deltaSummary,
            unresolvedIssues,
            priorRevisions = related.Select(x => new { attemptId = x.Id, submittedAt = x.SubmittedAt, state = ToApiState(x.State) }),
            actions = new[] { new { label = "Open writing", route = "/writing" } }
        };
    }

    public async Task<object> SubmitWritingRevisionAsync(string userId, string attemptId, RevisionSubmitRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var idempotencyKey = NormalizeWritingRevisionIdempotencyKey(request.IdempotencyKey);
        var idempotencyScope = $"writing-revision-submit:{userId}:{attemptId}";
        if (idempotencyKey is not null)
        {
            var cached = await GetIdempotentResponseAsync(idempotencyScope, idempotencyKey, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }
        }

        var baseAttempt = await GetWritingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        if (baseAttempt.State is not (AttemptState.Submitted or AttemptState.Evaluating or AttemptState.Completed))
        {
            throw ApiException.Validation(
                "writing_revision_base_not_submitted",
                "Submit the original Writing attempt before creating a revision.",
                [new ApiFieldError("attemptId", "not_submitted", "Revision requires a submitted Writing attempt.")]);
        }

        if (baseAttempt.ParentAttemptId is not null)
        {
            throw ApiException.Conflict(
                "writing_revision_base_is_revision",
                "Open the original Writing result before creating another revision.",
                [new ApiFieldError("attemptId", "revision_attempt", "Revision submission must target the original Writing attempt.")]);
        }

        _ = await GetCompletedWritingEvaluationForRevisionAsync(baseAttempt.Id, cancellationToken);

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            throw ApiException.Validation(
                "writing_revision_content_required",
                "Revised Writing content is required before submission.",
                [new ApiFieldError("content", "required", "Enter your revised response before submitting.")]);
        }

        if (request.Content.Length > WritingRevisionContentMaxLength)
        {
            throw ApiException.Validation(
                "writing_revision_content_too_long",
                "Revised Writing content is too long.",
                [new ApiFieldError("content", "too_long", $"Keep the revised response under {WritingRevisionContentMaxLength} characters.")]);
        }

        if (writingEntitlement is not null)
        {
            var ent = await writingEntitlement.CheckAsync(userId, cancellationToken);
            if (!ent.Allowed)
            {
                var msg = ent.Reason switch
                {
                    "premium_required" => "Writing practice requires an active subscription.",
                    "quota_exceeded" => $"Free tier allows {ent.LimitPerWindow} writing attempts every {ent.WindowDays} days.",
                    _ => ent.Reason,
                };
                var code = ent.Reason is "premium_required" or "quota_exceeded" ? ent.Reason : "writing_entitlement_blocked";
                throw ApiException.PaymentRequired(code, msg);
            }
        }

        var revision = new Attempt
        {
            Id = $"wa-{Guid.NewGuid():N}",
            UserId = baseAttempt.UserId,
            ContentId = baseAttempt.ContentId,
            SubtestCode = "writing",
            Context = "revision",
            Mode = baseAttempt.Mode,
            State = AttemptState.Evaluating,
            StartedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            DraftContent = request.Content,
            ParentAttemptId = baseAttempt.Id,
            ComparisonGroupId = baseAttempt.ComparisonGroupId,
            DeviceType = baseAttempt.DeviceType,
            DraftVersion = 1,
            LastClientSyncAt = DateTimeOffset.UtcNow
        };
        db.Attempts.Add(revision);
        var evaluation = new Evaluation
        {
            Id = $"we-{Guid.NewGuid():N}",
            AttemptId = revision.Id,
            SubtestCode = "writing",
            State = AsyncState.Queued,
            ScoreRange = "pending",
            ConfidenceBand = ConfidenceBand.Low,
            StrengthsJson = "[]",
            IssuesJson = "[]",
            CriterionScoresJson = "[]",
            FeedbackItemsJson = "[]",
            ModelExplanationSafe = "Revision evaluation queued.",
            LearnerDisclaimer = "Estimated training result pending.",
            StatusReasonCode = "queued",
            StatusMessage = "Revision queued.",
            Retryable = true,
            RetryAfterMs = 2000,
            LastTransitionAt = DateTimeOffset.UtcNow
        };
        db.Evaluations.Add(evaluation);
        await QueueJobAsync(JobType.WritingEvaluation, attemptId: revision.Id, resourceId: evaluation.Id, cancellationToken: cancellationToken);
        var response = new { attemptId = revision.Id, evaluationId = evaluation.Id, state = "queued" };
        await RecordEventAsync(baseAttempt.UserId, "revision_submitted", new { attemptId = revision.Id, parentAttemptId = baseAttempt.Id, evaluationId = evaluation.Id }, cancellationToken);
        if (idempotencyKey is not null)
        {
            await SaveIdempotentResponseAsync(idempotencyScope, idempotencyKey, response, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        return response;
    }

    private async Task<Evaluation> GetCompletedWritingEvaluationForRevisionAsync(string attemptId, CancellationToken cancellationToken)
    {
        return await db.Evaluations
            .Where(x => x.AttemptId == attemptId && x.SubtestCode == "writing" && x.State == AsyncState.Completed)
            .OrderByDescending(x => x.GeneratedAt ?? x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw ApiException.Conflict(
                "writing_revision_feedback_not_ready",
                "Complete the original Writing feedback before creating a revision.",
                [new ApiFieldError("attemptId", "feedback_not_ready", "Revision requires completed Writing feedback.")]);
    }

    private static string? NormalizeWritingRevisionIdempotencyKey(string? key)
    {
        var normalized = key?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Length > WritingRevisionIdempotencyKeyMaxLength || !WritingRevisionIdempotencyKeyRegex.IsMatch(normalized))
        {
            throw ApiException.Validation(
                "writing_revision_idempotency_key_invalid",
                "Revision idempotency key is invalid.",
                [new ApiFieldError("idempotencyKey", "invalid", "Use only letters, numbers, dots, underscores, colons, or hyphens, up to 64 characters.")]);
        }

        return normalized;
    }

    public async Task<object> GetWritingModelAnswerAsync(string userId, string contentId, CancellationToken cancellationToken)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId && x.SubtestCode == "writing" && x.Status == ContentStatus.Published, cancellationToken)
                   ?? throw ApiException.NotFound("content_not_found", "Writing model answer not found.");

        var hasSubmittedAttempt = await db.Attempts.AnyAsync(attempt =>
            attempt.UserId == userId &&
            attempt.ContentId == contentId &&
            attempt.SubtestCode == "writing" &&
            attempt.SubmittedAt != null &&
            (attempt.State == AttemptState.Submitted ||
             attempt.State == AttemptState.Evaluating ||
             attempt.State == AttemptState.Completed), cancellationToken);

        if (!hasSubmittedAttempt)
        {
            throw ApiException.Forbidden("writing_model_answer_locked", "Submit your Writing attempt before viewing the model answer.");
        }

        return new
        {
            contentId = item.Id,
            title = item.Title,
            professionId = item.ProfessionId,
            payload = JsonSupport.Deserialize<Dictionary<string, object?>>(item.ModelAnswerJson, new Dictionary<string, object?>())
        };
    }

    public async Task<object> GetSpeakingHomeAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        var tasks = await GetTasksBySubtestAsync("speaking", cancellationToken);
        var attemptIds = await db.Attempts.Where(x => x.UserId == userId && x.SubtestCode == "speaking").Select(x => x.Id).ToListAsync(cancellationToken);
        var wallet = await db.Wallets.FirstAsync(x => x.UserId == userId, cancellationToken);
        var attempts = (await db.Attempts
            .Where(x => x.UserId == userId && x.SubtestCode == "speaking")
            .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.SubmittedAt ?? x.StartedAt)
            .Take(4)
            .ToList();
        var latestEvaluation = (await db.Evaluations
            .Where(x => x.SubtestCode == "speaking" && attemptIds.Contains(x.AttemptId))
            .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.GeneratedAt)
            .FirstOrDefault();
        var commonIssues = latestEvaluation is null
            ? new[] { "Build smoother openings for role plays.", "Keep the professional tone consistent." }
            : JsonSupport.Deserialize<List<string>>(latestEvaluation.IssuesJson, [])
                .DefaultIfEmpty("Build smoother openings for role plays.")
                .ToArray();
        var evaluationByAttempt = (await db.Evaluations
            .Where(x => attemptIds.Contains(x.AttemptId))
            .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.GeneratedAt)
            .ToList();
        var evaluationLookup = evaluationByAttempt
            .GroupBy(x => x.AttemptId)
            .ToDictionary(group => group.Key, group => group.First());
        var phrasingRoute = latestEvaluation is null ? "/speaking/selection" : $"/speaking/phrasing/{latestEvaluation.Id}";
        return new
        {
            recommendedRolePlay = tasks.FirstOrDefault(),
            commonIssuesToImprove = commonIssues,
            drillGroups = new object[]
            {
                new
                {
                    id = "recalls-audio",
                    title = "Recalls audio drills",
                    items = new[]
                    {
                        new { id = "sp-drill-1", title = "Hear and type important treatment words", route = "/recalls/words" }
                    }
                },
                new
                {
                    id = "empathy_clarification",
                    title = "Empathy and clarification drills",
                    items = new[]
                    {
                        new { id = "sp-drill-2", title = "Clarify concerns without losing structure", route = phrasingRoute }
                    }
                }
            },
            pastAttempts = attempts.Select(attempt =>
            {
                evaluationLookup.TryGetValue(attempt.Id, out var evaluation);
                return new
                {
                    attemptId = attempt.Id,
                    state = ToApiState(attempt.State),
                    scoreEstimate = evaluation?.ScoreRange,
                    route = evaluation?.Id is null ? "/speaking/selection" : $"/speaking/results/{evaluation.Id}"
                };
            }),
            reviewCredits = new
            {
                available = wallet.CreditBalance,
                route = "/reviews",
                billingRoute = "/billing"
            },
            supportEntries = new[]
            {
                new { id = "recalls-audio", title = "Recalls Audio", description = "Click vocabulary words to hear British clinical pronunciation before the next role play.", route = "/recalls/words" },
                new { id = "conversation", title = "AI Conversation Practice", description = "Use the server-authoritative conversation module for interactive AI patient practice.", route = "/conversation" },
                new { id = "private-speaking", title = "Private Speaking Sessions", description = "Book human-led speaking support when you need live coaching.", route = "/private-speaking" }
            },
            featuredTasks = tasks.Take(3),
            latestEvaluation = latestEvaluation is null ? null : await GetSpeakingEvaluationSummaryAsync(userId, latestEvaluation.Id, cancellationToken),
            tips = new[]
            {
                "Use the mic check before longer speaking sessions.",
                "Prioritise professional tone and smooth transitions."
            }
        };
    }

    public async Task<List<object>> GetSpeakingTasksAsync(CancellationToken cancellationToken) => await GetTasksBySubtestAsync("speaking", cancellationToken);

    public async Task<object> GetSpeakingTaskAsync(string contentId, CancellationToken cancellationToken)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId && x.SubtestCode == "speaking" && x.Status == ContentStatus.Published, cancellationToken)
                   ?? throw ApiException.NotFound("content_not_found", "Speaking task not found.");
        return BuildLearnerSpeakingTaskPayload(item);
    }

    public async Task<object> CreateSpeakingAttemptAsync(string userId, CreateAttemptRequest request, CancellationToken cancellationToken)
        => await CreateAttemptAsync(userId, request, "speaking", cancellationToken);

    public async Task<object> GetSpeakingAttemptAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        var attempt = await GetSpeakingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        return await GetAttemptAsync(attempt.Id, cancellationToken);
    }

    public async Task<object> CreateSpeakingUploadSessionAsync(
        string userId,
        string attemptId,
        string? expectedContentId,
        string? expectedMockSessionId,
        CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetSpeakingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        EnsureSpeakingAttemptCanReceiveAudio(attempt);
        await EnsureSpeakingAttemptBindingAsync(attempt, expectedContentId, expectedMockSessionId, cancellationToken);
        var uploadId = $"upload-{Guid.NewGuid():N}";
        var upload = new UploadSession
        {
            Id = uploadId,
            AttemptId = attemptId,
            UploadUrl = platformLinks.BuildApiUrl($"/v1/speaking/upload-sessions/{uploadId}/content"),
            StorageKey = $"audio/{attemptId}/{uploadId}",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
            State = UploadState.Pending
        };
        db.UploadSessions.Add(upload);
        await db.SaveChangesAsync(cancellationToken);
        return new
        {
            uploadSessionId = upload.Id,
            uploadUrl = upload.UploadUrl,
            storageKey = upload.StorageKey,
            expiresAt = upload.ExpiresAt,
            httpMethod = "PUT",
            signed = false,
            requiresAuth = true
        };
    }

    public async Task<object> UploadSpeakingAudioAsync(
        string userId,
        string uploadSessionId,
        Stream content,
        string? contentType,
        CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var upload = await GetUploadSessionOwnedByUserAsync(userId, uploadSessionId, cancellationToken);
        var attempt = await GetSpeakingAttemptOwnedByUserAsync(userId, upload.AttemptId, cancellationToken);
        EnsureSpeakingAttemptCanReceiveAudio(attempt);
        await EnsureSpeakingAttemptBindingAsync(attempt, expectedContentId: null, expectedMockSessionId: null, cancellationToken);
        if (upload.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw ApiException.Conflict(
                "upload_session_expired",
                "This upload session has expired. Create a new upload session and try again.",
                [new ApiFieldError("uploadSessionId", "expired", "Request a new upload session before uploading audio.")]);
        }

        if (!MediaStoragePolicy.IsAllowedAudioContentType(contentType, storageSettings))
        {
            throw ApiException.Validation(
                "unsupported_audio_content_type",
                "Only supported audio formats can be uploaded.",
                [new ApiFieldError("audio", "unsupported_content_type", "Upload a browser-recorded WebM or another supported audio format.")]);
        }

        var bytesWritten = await WriteAudioToStorageAsync(upload.StorageKey, content, cancellationToken);
        if (bytesWritten == 0)
        {
            throw ApiException.Validation(
                "empty_audio_upload",
                "The uploaded audio file was empty.",
                [new ApiFieldError("audio", "empty", "Record some audio before uploading.")]);
        }

        upload.State = UploadState.Uploaded;
        await db.SaveChangesAsync(cancellationToken);

        return new
        {
            uploadSessionId = upload.Id,
            storageKey = upload.StorageKey,
            state = "uploaded",
            sizeBytes = bytesWritten,
            contentType = contentType
        };
    }

    public async Task<object> CompleteSpeakingUploadAsync(
        string userId,
        string attemptId,
        UploadCompleteRequest request,
        string? expectedContentId,
        string? expectedMockSessionId,
        CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetSpeakingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        EnsureSpeakingAttemptCanReceiveAudio(attempt);
        await EnsureSpeakingAttemptBindingAsync(attempt, expectedContentId, expectedMockSessionId, cancellationToken);
        var upload = await GetUploadSessionForCompletionAsync(userId, attemptId, request, cancellationToken);
        if (request.ConsentAccepted != true)
        {
            throw ApiException.Validation(
                "speaking_recording_consent_required",
                "Confirm recording consent before uploading speaking audio.",
                [new ApiFieldError("consent", "required", "Accept the speaking recording consent before submitting audio.")]);
        }
        if (upload.State != UploadState.Uploaded
            || !await fileStorage.ExistsAsync(upload.StorageKey, cancellationToken))
        {
            throw ApiException.Validation(
                "upload_binary_missing",
                "Upload the audio file before marking the speaking upload as complete.",
                [new ApiFieldError("audio", "missing", "Send the recording bytes to the upload URL first.")]);
        }

        var resolvedContentType = string.IsNullOrWhiteSpace(request.ContentType) ? null : request.ContentType;
        var storedLength = await fileStorage.LengthAsync(upload.StorageKey, cancellationToken);
        attempt.AudioUploadState = UploadState.Uploaded;
        attempt.AudioObjectKey = upload.StorageKey;
        attempt.AudioMetadataJson = JsonSupport.Serialize(new
        {
            fileName = request.FileName ?? $"{attemptId}.webm",
            sizeBytes = storedLength,
            reportedSizeBytes = request.SizeBytes,
            durationSeconds = request.DurationSeconds,
            captureMethod = request.CaptureMethod ?? "browser-recording",
            contentType = resolvedContentType,
            consent = new
            {
                accepted = true,
                acceptedAt = request.ConsentAcceptedAt ?? DateTimeOffset.UtcNow,
                consentText = request.ConsentText
            }
        });

        var existingTranscriptionJobs = await db.BackgroundJobs
            .Where(x => x.AttemptId == attemptId && x.Type == JobType.SpeakingTranscription && x.State != AsyncState.Failed)
            .ToListAsync(cancellationToken);
        var existingTranscriptionJob = existingTranscriptionJobs
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();
        if (existingTranscriptionJob is null)
        {
            await QueueJobAsync(JobType.SpeakingTranscription, attemptId: attemptId, resourceId: attemptId, cancellationToken: cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new { attemptId, audioUploadState = "uploaded", processingState = "queued", canSubmit = true };
    }

    public async Task<object> SubmitSpeakingAttemptAsync(string userId, string attemptId, CancellationToken cancellationToken)
        => await SubmitSpeakingAttemptAsync(userId, attemptId, expectedContentId: null, expectedMockSessionId: null, cancellationToken);

    public async Task<object> SubmitSpeakingAttemptAsync(
        string userId,
        string attemptId,
        string? expectedContentId,
        string? expectedMockSessionId,
        CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetSpeakingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        await EnsureSpeakingAttemptBindingAsync(attempt, expectedContentId, expectedMockSessionId, cancellationToken);
        if (attempt.State is AttemptState.Submitted or AttemptState.Evaluating or AttemptState.Completed)
        {
            var existing = await db.Evaluations.FirstOrDefaultAsync(x => x.AttemptId == attemptId, cancellationToken);
            if (existing is not null)
            {
                return new { attemptId, evaluationId = existing.Id, state = ToAsyncState(existing.State) };
            }

            await EnsureSpeakingAudioReadyForSubmissionAsync(attempt, cancellationToken);

            return await QueueSpeakingEvaluationAsync(attempt, cancellationToken);
        }

        await EnsureSpeakingAudioReadyForSubmissionAsync(attempt, cancellationToken);

        return await QueueSpeakingEvaluationAsync(attempt, cancellationToken);
    }

    private async Task<object> QueueSpeakingEvaluationAsync(Attempt attempt, CancellationToken cancellationToken)
    {
        attempt.State = AttemptState.Evaluating;
        attempt.SubmittedAt ??= DateTimeOffset.UtcNow;
        await LearnerWorkflowCoordinator.UpdateDiagnosticProgressAsync(db, attempt, AttemptState.Evaluating, cancellationToken);
        var evaluationId = $"se-{Guid.NewGuid():N}";
        // Speaking credits (2026-06-11 rebuild): the single speaking charge is
        // taken once per card at CARD REVEAL (prep start) — see
        // SpeakingSessionService.FinishWarmupAsync (practice) and
        // SpeakingExamService (exam). We deliberately do NOT debit again here at
        // grading time; doing so would double-charge the learner.

        var evaluation = new Evaluation
        {
            Id = evaluationId,
            AttemptId = attempt.Id,
            SubtestCode = "speaking",
            State = AsyncState.Queued,
            ScoreRange = "pending",
            ConfidenceBand = ConfidenceBand.Low,
            StrengthsJson = "[]",
            IssuesJson = "[]",
            CriterionScoresJson = "[]",
            FeedbackItemsJson = "[]",
            ModelExplanationSafe = "Speaking evaluation queued.",
            LearnerDisclaimer = "Training estimate pending.",
            StatusReasonCode = "queued",
            StatusMessage = "Speaking evaluation queued.",
            Retryable = true,
            RetryAfterMs = 2000,
            LastTransitionAt = DateTimeOffset.UtcNow
        };
        db.Evaluations.Add(evaluation);
        await QueueJobAsync(JobType.SpeakingEvaluation, attemptId: attempt.Id, resourceId: evaluation.Id, cancellationToken: cancellationToken);
        await RecordEventAsync(attempt.UserId, "task_submitted", new { attemptId = attempt.Id, evaluationId = evaluation.Id, subtest = "speaking", contentId = attempt.ContentId }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new { attemptId = attempt.Id, evaluationId = evaluation.Id, state = "queued", nextPollAfterMs = 2000 };
    }

    public async Task<object> GetSpeakingProcessingAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        await GetSpeakingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        var evaluations = await db.Evaluations.Where(x => x.AttemptId == attemptId).ToListAsync(cancellationToken);
        var evaluation = evaluations.OrderByDescending(x => x.LastTransitionAt).FirstOrDefault();
        var transcriptionJobs = await db.BackgroundJobs.Where(x => x.AttemptId == attemptId && x.Type == JobType.SpeakingTranscription).ToListAsync(cancellationToken);
        var transcriptionJob = transcriptionJobs.OrderByDescending(x => x.LastTransitionAt).FirstOrDefault();
        return new
        {
            attemptId,
            transcription = transcriptionJob is null
                ? (object)new Dictionary<string, object?> { ["state"] = "idle", ["statusReasonCode"] = null, ["retryAfterMs"] = null }
                : new Dictionary<string, object?> { ["state"] = ToAsyncState(transcriptionJob.State), ["statusReasonCode"] = transcriptionJob.StatusReasonCode, ["retryAfterMs"] = transcriptionJob.RetryAfterMs },
            evaluation = evaluation is null
                ? (object)new Dictionary<string, object?> { ["state"] = "idle", ["evaluationId"] = null, ["statusReasonCode"] = null, ["retryAfterMs"] = null }
                : new Dictionary<string, object?> { ["evaluationId"] = evaluation.Id, ["state"] = ToAsyncState(evaluation.State), ["statusReasonCode"] = evaluation.StatusReasonCode, ["retryAfterMs"] = evaluation.RetryAfterMs }
        };
    }

    public async Task<object> GetSpeakingEvaluationSummaryAsync(string userId, string evaluationId, CancellationToken cancellationToken)
    {
        var evaluation = await GetEvaluationOwnedByUserAsync(userId, evaluationId, cancellationToken);
        var attempt = await db.Attempts.FirstAsync(x => x.Id == evaluation.AttemptId, cancellationToken);
        var content = await db.ContentItems.FirstAsync(x => x.Id == attempt.ContentId, cancellationToken);
        var examFamilyCode = NormalizeExamFamilyCode(attempt.ExamFamilyCode);
        var examFamilyLabel = FormatExamFamilyLabel(examFamilyCode);
        await RecordEventAsync(userId, "evaluation_viewed", new { evaluationId = evaluation.Id, attemptId = attempt.Id, subtest = evaluation.SubtestCode }, cancellationToken);

        // Stable Wave 1 contract: criterion-keyed feedback + readiness band.
        // See docs/SPEAKING-MODULE-PLAN.md §3 Wave 1.
        var criteria = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.CriterionScoresJson, []);
        var (estimatedScaledScore, readinessBandCode, criteriaSource) = ReadSpeakingBandFromAnalysis(attempt.AnalysisJson);
        var roleCard = BuildLearnerSpeakingTaskPayload(content);
        var disclaimer = string.IsNullOrWhiteSpace(evaluation.LearnerDisclaimer)
            ? SpeakingContentStructure.PracticeDisclaimer
            : evaluation.LearnerDisclaimer;

        return new
        {
            evaluationId = evaluation.Id,
            attemptId = attempt.Id,
            taskId = content.Id,
            taskTitle = content.Title,
            examFamilyCode,
            examFamilyLabel,
            subtest = "speaking",
            state = ToAsyncState(evaluation.State),
            scoreRange = evaluation.ScoreRange,
            confidenceBand = evaluation.ConfidenceBand.ToString().ToLowerInvariant(),
            confidenceLabel = BuildConfidenceLabel(evaluation.ConfidenceBand),
            strengths = JsonSupport.Deserialize<List<string>>(evaluation.StrengthsJson, []),
            issues = JsonSupport.Deserialize<List<string>>(evaluation.IssuesJson, []),
            // Wave 1 contract additions ↓
            criteria,
            criterionScores = criteria,
            criteriaSource,
            readinessBand = readinessBandCode,
            readinessBandLabel = BuildSpeakingReadinessBandLabel(readinessBandCode),
            estimatedScaledScore,
            passThreshold = OetScoring.ScaledPassGradeB,
            rubricMax = OetScoring.SpeakingRubricMax,
            timing = new
            {
                prepTimeSeconds = roleCard.GetValueOrDefault("prepTimeSeconds"),
                roleplayTimeSeconds = roleCard.GetValueOrDefault("roleplayTimeSeconds"),
                recordedSeconds = attempt.ElapsedSeconds
            },
            workflow = new[]
            {
                "selection",
                "device_check",
                "prep",
                "roleplay_recording",
                "upload_submit",
                "processing_result",
                "transcript_review",
                "phrasing_drill",
                "expert_review_optional"
            },
            statusReasonCode = evaluation.StatusReasonCode,
            statusMessage = evaluation.StatusMessage,
            retryable = evaluation.Retryable,
            retryAfterMs = evaluation.RetryAfterMs,
            // Wave 1 contract additions ↑
            generatedAt = evaluation.GeneratedAt,
            nextDrill = new
            {
                id = evaluation.Id,
                title = "Phrasing and transcript drill",
                description = "Review the transcript markers and practise stronger alternatives from this attempt.",
                route = $"/speaking/phrasing/{evaluation.Id}"
            },
            recommendedDrills = new[]
            {
                new
                {
                    id = $"phrasing-{evaluation.Id}",
                    title = "Phrasing and transcript drill",
                    description = "Practise stronger patient-centred alternatives from the marked transcript.",
                    route = $"/speaking/phrasing/{evaluation.Id}"
                },
                new
                {
                    id = "ai-patient-practice",
                    title = "AI patient conversation practice",
                    description = "Launch the existing conversation module for a grounded patient practice session.",
                    route = "/conversation"
                }
            },
            modelExplanationSafe = evaluation.ModelExplanationSafe,
            learnerDisclaimer = disclaimer,
            disclaimer,
            isOfficialScore = false,
            methodLabel = BuildAiMethodLabel("speaking"),
            provenanceLabel = $"{examFamilyLabel} practice estimate",
            humanReviewRecommended = ShouldRecommendHumanReview(evaluation.ConfidenceBand),
            escalationRecommended = ShouldRecommendHumanReview(evaluation.ConfidenceBand)
        };
    }

    private static (int? estimatedScaledScore, string readinessBandCode, string? criteriaSource) ReadSpeakingBandFromAnalysis(string analysisJson, string examFamilyCode)
        => ReadSpeakingBandFromAnalysis(analysisJson);

    private static (int? estimatedScaledScore, string readinessBandCode, string? criteriaSource) ReadSpeakingBandFromAnalysis(string analysisJson)
    {
        if (string.IsNullOrWhiteSpace(analysisJson))
        {
            return (null, OetScoring.SpeakingReadinessBandCode(OetScoring.SpeakingReadinessBand.NotReady), null);
        }
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(analysisJson);
            if (!doc.RootElement.TryGetProperty("speakingBand", out var band)) goto fallback;
            int? scaled = band.TryGetProperty("scaledEstimate", out var s) && s.TryGetInt32(out var v) ? v : null;
            string? readiness = band.TryGetProperty("readinessBand", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.String ? r.GetString() : null;
            string? source = band.TryGetProperty("criteriaSource", out var src) && src.ValueKind == System.Text.Json.JsonValueKind.String ? src.GetString() : null;
            // If readinessBand was not yet persisted (legacy attempts before
            // Wave 1), derive it from the scaled estimate so the contract
            // is always populated.
            readiness ??= scaled is { } sv
                ? OetScoring.SpeakingReadinessBandCode(OetScoring.SpeakingReadinessBandFromScaled(sv))
                : OetScoring.SpeakingReadinessBandCode(OetScoring.SpeakingReadinessBand.NotReady);
            return (scaled, readiness, source);
        }
        catch
        {
        }
    fallback:
        return (null, OetScoring.SpeakingReadinessBandCode(OetScoring.SpeakingReadinessBand.NotReady), null);
    }

    private static string BuildSpeakingReadinessBandLabel(string code) => code switch
    {
        "not_ready"  => "Not ready",
        "developing" => "Developing",
        "borderline" => "Borderline",
        "exam_ready" => "Exam-ready",
        "strong"     => "Strong",
        _             => "Not ready",
    };

    public async Task<object> GetSpeakingReviewAsync(string userId, string evaluationId, CancellationToken cancellationToken)
    {
        var evaluation = await GetEvaluationOwnedByUserAsync(userId, evaluationId, cancellationToken);
        var attempt = await db.Attempts.FirstAsync(x => x.Id == evaluation.AttemptId, cancellationToken);
        var content = await db.ContentItems.FirstAsync(x => x.Id == attempt.ContentId, cancellationToken);
        var disclaimer = string.IsNullOrWhiteSpace(evaluation.LearnerDisclaimer)
            ? SpeakingContentStructure.PracticeDisclaimer
            : evaluation.LearnerDisclaimer;
        return new
        {
            summary = await GetSpeakingEvaluationSummaryAsync(userId, evaluationId, cancellationToken),
            roleCard = BuildLearnerSpeakingTaskPayload(content),
            disclaimer,
            transcript = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(attempt.TranscriptJson, []),
            analysis = JsonSupport.Deserialize<Dictionary<string, object?>>(attempt.AnalysisJson, new Dictionary<string, object?>()),
            feedbackItems = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.FeedbackItemsJson, []),
            audioAvailable = !string.IsNullOrWhiteSpace(attempt.AudioObjectKey),
            audioUrl = string.IsNullOrWhiteSpace(attempt.AudioObjectKey)
                ? null
                : platformLinks.BuildApiUrl($"/v1/speaking/evaluations/{Uri.EscapeDataString(evaluationId)}/audio")
        };
    }

    public async Task<StoredMediaFile> GetSpeakingEvaluationAudioAsync(string userId, string evaluationId, CancellationToken cancellationToken)
    {
        var evaluation = await GetEvaluationOwnedByUserAsync(userId, evaluationId, cancellationToken);
        var attempt = await db.Attempts.FirstAsync(x => x.Id == evaluation.AttemptId, cancellationToken);
        if (string.IsNullOrWhiteSpace(attempt.AudioObjectKey))
        {
            throw ApiException.NotFound("audio_not_found", "No uploaded audio is available for this speaking evaluation.");
        }

        var metadata = JsonSupport.Deserialize(attempt.AudioMetadataJson, new Dictionary<string, object?>());
        var contentType = metadata.TryGetValue("contentType", out var value) ? value?.ToString() : null;
        return await fileStorage.OpenStoredMediaFileAsync(attempt.AudioObjectKey, contentType, cancellationToken);
    }

    private async Task<long> WriteAudioToStorageAsync(string storageKey, Stream source, CancellationToken cancellationToken)
    {
        var maxBytes = storageSettings.MaxUploadBytes > 0 ? storageSettings.MaxUploadBytes : 25L * 1024 * 1024;
        var buffer = new byte[81920];
        long totalBytes = 0;

        try
        {
            await using var destination = await fileStorage.OpenWriteAsync(storageKey, cancellationToken);
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                totalBytes += read;
                if (totalBytes > maxBytes)
                {
                    throw ApiException.Validation(
                        "audio_file_too_large",
                        $"Audio uploads must be {maxBytes / (1024 * 1024)} MB or smaller.",
                        [new ApiFieldError("audio", "too_large", "Record a shorter file or increase the configured upload limit.")]);
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        catch
        {
            await fileStorage.DeleteAsync(storageKey, cancellationToken);
            throw;
        }

        return totalBytes;
    }

    public async Task<object> GetReadingHomeAsync(CancellationToken cancellationToken)
    {
        var tasks = await GetTasksBySubtestAsync("reading", cancellationToken);
        return new
        {
            featuredTasks = tasks,
            intro = "Reading practice focuses on rapid detail extraction and inference control.",
            entryPoints = new object[]
            {
                new { id = "part-a", title = "Part A", route = (string?)null, available = false },
                new { id = "part-b", title = "Part B", route = (string?)null, available = false },
                new { id = "part-c", title = "Part C", route = "/reading", available = true }
            },
            speedDrills = new[]
            {
                new { id = "reading-speed-1", title = "Timed detail scanning", route = "/reading" }
            },
            accuracyDrills = new[]
            {
                new { id = "reading-accuracy-1", title = "Named concept accuracy drill", route = "/reading" }
            },
            explanations = new[]
            {
                new { id = "reading-explanations", title = "Review answer explanations", route = "/history" }
            },
            mockSets = new[]
            {
                new { id = "reading-mock-set", title = "Reading mock set", route = "/mocks" }
            }
        };
    }

    public async Task<object> GetReadingTaskAsync(string contentId, CancellationToken cancellationToken) => await GetGenericTaskAsync(contentId, "reading", cancellationToken);
    public async Task<object> CreateReadingAttemptAsync(string userId, CreateAttemptRequest request, CancellationToken cancellationToken) => await CreateAttemptAsync(userId, request, "reading", cancellationToken);
    public async Task<object> GetReadingAttemptAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        var attempt = await GetSubtestAttemptOwnedByUserAsync(userId, attemptId, "reading", "reading_attempt_not_found", "Reading attempt not found.", cancellationToken);
        return await GetAttemptAsync(attempt.Id, cancellationToken);
    }
    public async Task<object> UpdateReadingAnswersAsync(string userId, string attemptId, AnswersUpdateRequest request, CancellationToken cancellationToken) => await UpdateAnswersAsync(userId, attemptId, request, "reading", cancellationToken);
    public async Task<object> SubmitReadingAttemptAsync(string userId, string attemptId, CancellationToken cancellationToken) => await SubmitObjectiveAttemptAsync(userId, attemptId, "reading", cancellationToken);
    public async Task<object> GetReadingEvaluationAsync(string userId, string evaluationId, CancellationToken cancellationToken) => await GetObjectiveEvaluationAsync(userId, evaluationId, "reading", cancellationToken);

    public async Task<object> GetListeningHomeAsync(CancellationToken cancellationToken)
    {
        var tasks = await GetTasksBySubtestAsync("listening", cancellationToken);
        return new
        {
            featuredTasks = tasks,
            intro = "Listening practice emphasises accurate capture of numbers, frequencies, and changes in plan.",
            partCollections = new[]
            {
                new { id = "listening-practice", title = "Practice sets", route = "/listening/player/lt-001" }
            },
            transcriptBackedReview = new
            {
                title = "Transcript-backed review",
                route = "/listening/review/lt-001",
                availableAfterAttempt = true
            },
            distractorDrills = new[]
            {
                new { id = "listening-drill-distractor_confusion", title = "Frequency distractor drill", route = "/listening/drills/listening-drill-distractor_confusion" }
            },
            accessPolicyHints = new
            {
                rationale = "Use transcript-backed review after an attempt so you can diagnose distractor patterns with real evidence instead of replaying blindly.",
                availableAfterAttempt = true
            },
            mockSets = new[]
            {
                new { id = "full-practice", title = "Full OET Mock", type = "full", subType = (string?)null, mode = "practice", includeReview = false, strictTimer = false, reviewSelection = "none" },
                new { id = "full-exam", title = "Full OET Mock", type = "full", subType = (string?)null, mode = "exam", includeReview = false, strictTimer = true, reviewSelection = "none" },
                new { id = "writing-only", title = "Writing-only Mock", type = "sub", subType = (string?)"writing", mode = "exam", includeReview = true, strictTimer = true, reviewSelection = "current_subtest" }
            }
        };
    }

    public async Task<object> GetListeningTaskAsync(string contentId, CancellationToken cancellationToken) => await GetGenericTaskAsync(contentId, "listening", cancellationToken);
    public async Task<object> CreateListeningAttemptAsync(string userId, CreateAttemptRequest request, CancellationToken cancellationToken) => await CreateAttemptAsync(userId, request, "listening", cancellationToken);
    public async Task<object> GetListeningAttemptAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        var attempt = await GetSubtestAttemptOwnedByUserAsync(userId, attemptId, "listening", "listening_attempt_not_found", "Listening attempt not found.", cancellationToken);
        return await GetAttemptAsync(attempt.Id, cancellationToken);
    }
    public async Task<object> UpdateListeningAnswersAsync(string userId, string attemptId, AnswersUpdateRequest request, CancellationToken cancellationToken) => await UpdateAnswersAsync(userId, attemptId, request, "listening", cancellationToken);
    public async Task<object> SubmitListeningAttemptAsync(string userId, string attemptId, CancellationToken cancellationToken) => await SubmitObjectiveAttemptAsync(userId, attemptId, "listening", cancellationToken);
    public async Task<object> GetListeningEvaluationAsync(string userId, string evaluationId, CancellationToken cancellationToken) => await GetObjectiveEvaluationAsync(userId, evaluationId, "listening", cancellationToken);
    public Task<object> GetListeningDrillAsync(string drillId, CancellationToken cancellationToken) => Task.FromResult(BuildListeningDrill(drillId));

    public async Task<object> GetBillingQuoteAsync(string userId, BillingQuoteRequest request, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        return await BuildBillingQuoteAsync(userId, request, cancellationToken, persistQuote: true);
    }

    public async Task<BillingPaymentStatusResponse> GetBillingPaymentStatusAsync(
        string userId,
        string? quoteId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);

        BillingQuote? quote = null;
        if (!string.IsNullOrWhiteSpace(quoteId))
        {
            quote = await db.BillingQuotes.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == quoteId && x.UserId == userId, cancellationToken);
        }

        if (quote is null && !string.IsNullOrWhiteSpace(sessionId))
        {
            quote = await db.BillingQuotes.AsNoTracking()
                .FirstOrDefaultAsync(x => x.CheckoutSessionId == sessionId && x.UserId == userId, cancellationToken);
        }

        PaymentTransaction? transaction = null;
        if (quote is not null)
        {
            transaction = await db.PaymentTransactions.AsNoTracking()
                .Where(x => x.LearnerUserId == userId && (x.QuoteId == quote.Id || x.GatewayTransactionId == quote.CheckoutSessionId))
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(sessionId))
        {
            transaction = await db.PaymentTransactions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.LearnerUserId == userId && x.GatewayTransactionId == sessionId, cancellationToken);
            if (transaction?.QuoteId is not null)
            {
                quote = await db.BillingQuotes.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == transaction.QuoteId && x.UserId == userId, cancellationToken);
            }
        }

        if (quote is null)
        {
            throw ApiException.NotFound("billing_payment_not_found", "Payment status was not found for this checkout.");
        }

        var quoteResponse = DeserializeQuoteResponse(quote);
        var invoice = await db.Invoices.AsNoTracking()
            .Where(x => x.UserId == userId && (x.QuoteId == quote.Id || x.CheckoutSessionId == quote.CheckoutSessionId))
            .OrderByDescending(x => x.IssuedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var subscriptionItem = await db.SubscriptionItems.AsNoTracking()
            .Where(x => x.QuoteId == quote.Id || x.CheckoutSessionId == quote.CheckoutSessionId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var status = NormalizeBillingPaymentStatus(quote, transaction, DateTimeOffset.UtcNow);
        var metadata = JsonSupport.Deserialize<Dictionary<string, object?>>(transaction?.MetadataJson ?? "{}", new Dictionary<string, object?>());
        var productType = ReadString(metadata.GetValueOrDefault("productType"))
            ?? ReadString(metadata.GetValueOrDefault("product_type"))
            ?? (quote.PlanCode is not null ? "plan_purchase" : quoteResponse.Items.FirstOrDefault()?.Kind);

        var resolvedSubscriptionId = subscriptionItem?.SubscriptionId ?? quote.SubscriptionId;
        if (string.IsNullOrWhiteSpace(resolvedSubscriptionId) && !string.IsNullOrWhiteSpace(quote.SubscriptionId))
        {
            resolvedSubscriptionId = quote.SubscriptionId;
        }

        return new BillingPaymentStatusResponse(
            status,
            quote.Id,
            quote.CheckoutSessionId,
            productType,
            quote.PlanCode,
            JsonSupport.Deserialize<List<string>>(quote.AddOnCodesJson, []),
            quoteResponse.Items,
            quote.TotalAmount,
            quote.Currency,
            invoice?.Id,
            resolvedSubscriptionId,
            FailureReasonForBillingPaymentStatus(status),
            status == "completed" ? transaction?.UpdatedAt ?? invoice?.IssuedAt : null,
            quote.ExpiresAt);
    }

    public async Task<object> GetBillingExtrasAsync()
    {
        var addOns = await db.BillingAddOns.AsNoTracking()
            .Where(x => x.Status == BillingAddOnStatus.Active && (x.IsRecurring || x.GrantCredits > 0 || x.AppliesToAllPlans))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Price)
            .ToListAsync();

        return new
        {
            items = addOns.Select(x => new
            {
                id = x.Code,
                code = x.Code,
                name = x.Name,
                productType = x.IsRecurring ? "addon_purchase" : "review_credits",
                quantity = x.GrantCredits > 0 ? x.GrantCredits : Math.Max(1, x.QuantityStep),
                price = x.Price,
                currency = x.Currency,
                interval = x.Interval,
                status = x.Status.ToString().ToLowerInvariant(),
                description = x.Description,
                grantCredits = x.GrantCredits,
                durationDays = x.DurationDays,
                isRecurring = x.IsRecurring,
                appliesToAllPlans = x.AppliesToAllPlans,
                quantityStep = x.QuantityStep,
                maxQuantity = x.MaxQuantity,
                compatiblePlanCodes = JsonSupport.Deserialize<List<string>>(x.CompatiblePlanCodesJson, [])
            })
        };
    }

    /// <summary>
    /// Returns the AI Credits storefront catalog (<c>addonKind="ai_package"</c>)
    /// grouped into Full / Separate (listening, reading, writing, speaking) / Mock
    /// families for the billing module's "AI Credits" tab. Grouping is derived from
    /// the canonical <c>pkg_*</c> code prefix so no extra catalog column is required.
    /// Purchases flow through the same quote → checkout-session → webhook pipeline
    /// as every other add-on (<see cref="CreateCheckoutSessionAsync"/>).
    /// </summary>
    /// <summary>
    /// Public learner-billing copy overrides as a flat <c>{ key: value }</c> map. The page
    /// merges these over its in-code defaults, so an empty map renders every default string.
    /// </summary>
    public async Task<object> GetBillingContentAsync(CancellationToken ct)
    {
        var rows = await db.BillingContentStrings.AsNoTracking()
            .Select(x => new { x.Key, x.Value })
            .ToListAsync(ct);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows) map[row.Key] = row.Value;
        return map;
    }

    public async Task<object> GetAiPackagesAsync()
    {
        var addOns = await db.BillingAddOns.AsNoTracking()
            .Where(x => x.Status == BillingAddOnStatus.Active && x.AddonKind == "ai_package")
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Price)
            .ToListAsync();

        var views = addOns.Select(ToAiPackageView).ToList();
        var currency = views.FirstOrDefault()?.Currency ?? "GBP";

        static object Project(AiPackageView v) => new
        {
            code = v.Code,
            name = v.Name,
            description = v.Description,
            price = v.Price,
            currency = v.Currency,
            credits = v.Credits,
            writingCredits = v.WritingCredits,
            speakingCredits = v.SpeakingCredits,
            mocks = v.Mocks,
            validityDays = v.ValidityDays,
            priorityQueue = v.PriorityQueue,
            group = v.Group,
            features = v.Features,
        };

        return new
        {
            currency,
            full = views.Where(v => v.Group == "full").Select(Project).ToList(),
            separate = new
            {
                listening = views.Where(v => v.Group == "listening").Select(Project).ToList(),
                reading = views.Where(v => v.Group == "reading").Select(Project).ToList(),
                writing = views.Where(v => v.Group == "writing").Select(Project).ToList(),
                speaking = views.Where(v => v.Group == "speaking").Select(Project).ToList(),
            },
            mock = views.Where(v => v.Group == "mock").Select(Project).ToList(),
        };
    }

    private static AiPackageView ToAiPackageView(BillingAddOn x)
    {
        var extras = ReadAiPackageExtras(x.GrantEntitlementsJson);
        // Prefer the admin-configured group; fall back to code-prefix derivation for legacy seeded rows.
        var group = string.IsNullOrWhiteSpace(x.AiPackageGroup)
            ? ResolveAiPackageGroup(x.Code)
            : x.AiPackageGroup.Trim().ToLowerInvariant();
        var credits = extras.FlexibleCredits ?? x.GrantCredits;
        var writingCredits = extras.WritingCredits ?? x.LettersGranted;
        var speakingCredits = extras.SpeakingCredits ?? x.SessionsGranted;
        if (group == "writing" && writingCredits == 0) writingCredits = credits;
        if (group == "speaking" && speakingCredits == 0) speakingCredits = credits;
        // Prefer admin-authored feature bullets; fall back to auto-generated copy when none stored.
        var features = ReadAiFeatures(x.AiFeaturesJson) ?? BuildAiPackageFeatures(
            group,
            credits,
            writingCredits,
            speakingCredits,
            extras.Mocks,
            x.DurationDays,
            extras.PriorityQueue,
            extras.ListeningTests,
            extras.ReadingTests);
        return new AiPackageView(
            x.Code, x.Name, x.Description ?? string.Empty,
            x.Price, x.Currency, credits, writingCredits,
            speakingCredits, extras.Mocks, x.DurationDays, extras.PriorityQueue, group, features);
    }

    private static string ResolveAiPackageGroup(string code)
    {
        if (code.StartsWith("pkg_listening", StringComparison.OrdinalIgnoreCase)) return "listening";
        if (code.StartsWith("pkg_reading", StringComparison.OrdinalIgnoreCase)) return "reading";
        if (code.StartsWith("pkg_writing", StringComparison.OrdinalIgnoreCase)) return "writing";
        if (code.StartsWith("pkg_speaking", StringComparison.OrdinalIgnoreCase)) return "speaking";
        if (code.StartsWith("pkg_mock", StringComparison.OrdinalIgnoreCase)) return "mock";
        return "full";
    }

    private static IReadOnlyList<string> BuildAiPackageFeatures(
        string group,
        int credits,
        int writingCredits,
        int speakingCredits,
        int mocks,
        int validityDays,
        bool priorityQueue,
        int? listeningTests,
        int? readingTests)
    {
        var features = new List<string>();
        var validity = validityDays >= 180 ? "6-month validity" : $"{validityDays}-day validity";
        switch (group)
        {
            case "full":
                features.Add($"{credits} flexible grading credits (Writing or Speaking)");
                if (mocks > 0) features.Add($"{mocks} full mock exam{(mocks == 1 ? string.Empty : "s")} included");
                // Only advertise unlimited L&R when the package actually grants it (both allowances null = unlimited).
                if (listeningTests is null && readingTests is null)
                    features.Add("Unlimited Listening & Reading practice");
                features.Add("AI feedback reports");
                if (priorityQueue) features.Add("Priority grading queue");
                features.Add(validity);
                break;
            case "writing":
                features.Add($"{writingCredits} AI-graded Writing letters");
                features.Add("Instant Claude feedback on every letter");
                features.Add("Detailed per-criterion feedback");
                features.Add(validity);
                break;
            case "speaking":
                features.Add($"{speakingCredits} AI-graded Speaking cards");
                features.Add("Whisper transcription + Claude assessment");
                features.Add("Rule-cited transcript markers");
                features.Add(validity);
                break;
            case "listening":
                features.Add(listeningTests is null ? "Unlimited Listening practice tests" : $"{listeningTests} Listening practice tests");
                features.Add("Deterministic answer-key marking");
                features.Add("Always free to grade - no AI credits used");
                features.Add(validity);
                break;
            case "reading":
                features.Add(readingTests is null ? "Unlimited Reading practice tests" : $"{readingTests} Reading practice tests");
                features.Add("Deterministic answer-key marking");
                features.Add("Always free to grade — no credits used");
                features.Add(validity);
                break;
            case "mock":
                features.Add($"{mocks} full mock exam{(mocks == 1 ? string.Empty : "s")} — all 4 subtests");
                features.Add("Writing + Speaking AI-graded");
                features.Add("Listening + Reading auto-marked");
                features.Add("Separate from AI credits");
                features.Add(validity);
                break;
        }
        return features;
    }

    /// <summary>
    /// Parses an admin-authored AI feature bullet list. Returns null (→ auto-generate fallback)
    /// when the JSON is empty, malformed, or contains no usable strings.
    /// </summary>
    private static IReadOnlyList<string>? ReadAiFeatures(string? aiFeaturesJson)
    {
        if (string.IsNullOrWhiteSpace(aiFeaturesJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(aiFeaturesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var features = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                var text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text)) features.Add(text.Trim());
            }
            return features.Count > 0 ? features : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AiPackageExtras ReadAiPackageExtras(string? grantEntitlementsJson)
    {
        if (string.IsNullOrWhiteSpace(grantEntitlementsJson)) return new(null, null, null, null, null, 0, false);
        try
        {
            using var doc = JsonDocument.Parse(grantEntitlementsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return new(null, null, null, null, null, 0, false);
            var root = doc.RootElement;
            var mocks = ReadInt(root, "mock_exams") ?? ReadInt(root, "mockFull") ?? 0;
            var pq = root.TryGetProperty("priority_queue", out var p) && p.ValueKind == JsonValueKind.True;
            return new(
                ReadInt(root, "flexible_credits"),
                ReadInt(root, "writing_only_credits"),
                ReadInt(root, "speaking_only_credits"),
                ReadNullableAllowance(root, "listening_tests"),
                ReadNullableAllowance(root, "reading_tests"),
                mocks,
                pq);
        }
        catch (JsonException)
        {
            return new(null, null, null, null, null, 0, false);
        }
    }

    private static int? ReadInt(JsonElement root, string name)
        => root.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var parsed)
            ? Math.Max(0, parsed)
            : null;

    private static int? ReadNullableAllowance(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Null) return null;
        return value.TryGetInt32(out var parsed) ? Math.Max(0, parsed) : null;
    }

    private sealed record AiPackageExtras(
        int? FlexibleCredits,
        int? WritingCredits,
        int? SpeakingCredits,
        int? ListeningTests,
        int? ReadingTests,
        int Mocks,
        bool PriorityQueue);

    private sealed record AiPackageView(
        string Code, string Name, string Description, decimal Price, string Currency,
        int Credits, int WritingCredits, int SpeakingCredits, int Mocks, int ValidityDays,
        bool PriorityQueue, string Group, IReadOnlyList<string> Features);

    public async Task<object> CreateCheckoutSessionAsync(string userId, CheckoutSessionCreateRequest request, CancellationToken cancellationToken)
    {
        var checkoutUser = await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);

        var normalizedProductType = (request.ProductType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedProductType is not ("review_credits" or "plan_purchase" or "plan_upgrade" or "plan_downgrade" or "addon_purchase"))
        {
            throw ApiException.Validation(
                "unsupported_checkout_product",
                $"Unsupported checkout product '{request.ProductType}'.",
                [new ApiFieldError("productType", "unsupported", "Only supported learner checkout products can be purchased.")]);
        }

        if (request.Quantity <= 0)
        {
            throw ApiException.Validation(
                "invalid_checkout_quantity",
                "Checkout quantity must be greater than zero.",
                [new ApiFieldError("quantity", "invalid", "Choose a checkout quantity greater than zero.")]);
        }

        if (normalizedProductType is "plan_purchase" or "plan_upgrade" or "plan_downgrade" or "addon_purchase"
            && string.IsNullOrWhiteSpace(request.PriceId))
        {
            throw ApiException.Validation(
                "target_item_required",
                "A target plan or add-on id is required for this checkout.",
                [new ApiFieldError("priceId", "required", "Choose the plan or add-on you want to purchase.")]);
        }

        var gatewayLabel = string.IsNullOrWhiteSpace(request.Gateway) ? "stripe" : request.Gateway.Trim().ToLowerInvariant();
        if (!paymentGateways.SupportedGateways.Contains(gatewayLabel, StringComparer.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "unsupported_gateway",
                $"Payment gateway '{gatewayLabel}' is not supported.",
                [new ApiFieldError("gateway", "unsupported", "Choose stripe or paypal.")]);
        }

        var normalizedAddOnCodes = NormalizeCodes(request.AddOnCodes);
        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);
        var idempotencyRequestHash = idempotencyKey is null
            ? null
            : ComputeIdempotencyRequestHash(new
            {
                userId,
                productType = normalizedProductType,
                request.Quantity,
                priceId = request.PriceId?.Trim(),
                couponCode = request.CouponCode?.Trim().ToUpperInvariant(),
                addOnCodes = normalizedAddOnCodes.OrderBy(code => code, StringComparer.OrdinalIgnoreCase).ToArray(),
                gateway = gatewayLabel,
                quoteId = request.QuoteId?.Trim(),
                parentSubscriptionId = request.ParentSubscriptionId?.Trim()
            });
        if (idempotencyKey is not null && idempotencyRequestHash is not null)
        {
            var reservation = await ReservePaymentIdempotencyAsync(
                "checkout-session",
                idempotencyKey,
                userId,
                idempotencyRequestHash,
                cancellationToken);
            if (!reservation.ShouldProcess)
            {
                return reservation.CachedResponse!;
            }
        }

        BillingQuoteResponse quoteResponse;
        BillingQuote quoteEntity;
        var providerRequestReturned = false;
        var idempotencyCompleted = false;
        object? idempotencyResponse = null;
        try
        {
        if (!string.IsNullOrWhiteSpace(request.QuoteId))
        {
            quoteEntity = await db.BillingQuotes.FirstOrDefaultAsync(x => x.Id == request.QuoteId && x.UserId == userId, cancellationToken)
                ?? throw ApiException.NotFound("billing_quote_not_found", "The requested billing quote could not be found.");

            var now = DateTimeOffset.UtcNow;
            if (quoteEntity.ExpiresAt < now)
            {
                var releasedCouponKeys = await ReleasePreCheckoutCouponReservationsForQuoteAsync(quoteEntity, now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await RefreshCouponRedemptionCountsAsync(releasedCouponKeys, now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                throw ApiException.Validation("billing_quote_expired", "This billing quote has expired.");
            }
            EnsureQuoteIsFulfillable(quoteEntity, now);
            if (quoteEntity.Status == BillingQuoteStatus.Applied && !string.IsNullOrWhiteSpace(quoteEntity.CheckoutSessionId))
            {
                throw ApiException.Conflict(
                    "billing_quote_already_applied",
                    "This billing quote is already attached to a checkout session. Refresh your cart before starting a new checkout.");
            }

            // Bind the quote snapshot to the inbound request so a stale or swapped
            // quoteId cannot be reused with a different product, plan, coupon, or add-on.
            var quoteAddOnCodes = JsonSupport.Deserialize<List<string>>(quoteEntity.AddOnCodesJson, []);
            if (!string.IsNullOrWhiteSpace(request.PriceId))
            {
                var matchesPlan = !string.IsNullOrWhiteSpace(quoteEntity.PlanCode)
                    && string.Equals(quoteEntity.PlanCode, request.PriceId, StringComparison.OrdinalIgnoreCase);
                var matchesAddOn = quoteAddOnCodes.Any(code => string.Equals(code, request.PriceId, StringComparison.OrdinalIgnoreCase));
                if (!matchesPlan && !matchesAddOn)
                {
                    throw ApiException.Validation(
                        "quote_mismatch",
                        "The supplied priceId does not match the saved quote.",
                        [new ApiFieldError("priceId", "mismatch", "Refresh your quote before checking out.")]);
                }
            }

            if (!string.IsNullOrWhiteSpace(request.CouponCode)
                && !string.Equals(request.CouponCode, quoteEntity.CouponCode, StringComparison.OrdinalIgnoreCase))
            {
                throw ApiException.Validation(
                    "quote_mismatch",
                    "The supplied couponCode does not match the saved quote.",
                    [new ApiFieldError("couponCode", "mismatch", "Refresh your quote before checking out.")]);
            }

            if (normalizedAddOnCodes.Count > 0)
            {
                var requested = normalizedAddOnCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var saved = new HashSet<string>(quoteAddOnCodes, StringComparer.OrdinalIgnoreCase);
                if (!requested.SetEquals(saved))
                {
                    throw ApiException.Validation(
                        "quote_mismatch",
                        "The supplied add-on codes do not match the saved quote.",
                        [new ApiFieldError("addOnCodes", "mismatch", "Refresh your quote before checking out.")]);
                }
            }

            // Re-run the profession / content-availability gate against the plan the
            // saved quote locked in. BuildBillingQuoteAsync already gates the fresh-quote
            // path below, but a stored quote skips it entirely — and the learner's
            // profession may have changed since the quote was built.
            if (normalizedProductType is "plan_purchase" or "plan_upgrade" or "plan_downgrade"
                && !string.IsNullOrWhiteSpace(quoteEntity.PlanCode))
            {
                var quotedPlan = await FindBillingPlanAsync(quoteEntity.PlanCode, cancellationToken);
                if (quotedPlan is not null)
                {
                    await EnsurePlanMatchesLearnerProfessionAsync(checkoutUser, quotedPlan, cancellationToken);
                }
            }

            quoteResponse = DeserializeQuoteResponse(quoteEntity);
        }
        else
        {
            quoteResponse = await BuildBillingQuoteAsync(userId, new BillingQuoteRequest(
                normalizedProductType,
                request.Quantity,
                request.PriceId,
                request.CouponCode,
                normalizedAddOnCodes,
                request.ParentSubscriptionId), cancellationToken, persistQuote: true);
            quoteEntity = await db.BillingQuotes.FirstAsync(x => x.Id == quoteResponse.QuoteId && x.UserId == userId, cancellationToken);
        }

        var purchaseTarget = quoteResponse.Items.FirstOrDefault()?.Code ?? quoteEntity.PlanCode ?? request.PriceId;
        PaymentIntentResult checkoutIntent;
        try
        {
            checkoutIntent = await paymentGateways.GetGateway(gatewayLabel).CreatePaymentIntentAsync(
            new CreatePaymentIntentRequest(
                UserId: userId,
                Amount: quoteEntity.TotalAmount,
                Currency: quoteEntity.Currency,
                ProductType: normalizedProductType,
                ProductId: quoteEntity.Id,
                Description: quoteResponse.Summary,
                                Metadata: new Dictionary<string, string>
                                {
                                        ["quote_id"] = quoteEntity.Id,
                                        ["product_type"] = normalizedProductType,
                                        ["purchase_target"] = purchaseTarget ?? string.Empty,
                                        ["user_id"] = userId,
                                        ["plan_code"] = quoteEntity.PlanCode ?? string.Empty,
                                        ["coupon_code"] = quoteEntity.CouponCode ?? string.Empty,
                                        ["add_on_codes"] = string.Join(',', JsonSupport.Deserialize<List<string>>(quoteEntity.AddOnCodesJson, [])),
                                        ["parent_subscription_id"] = request.ParentSubscriptionId ?? string.Empty,
                                        ["plan_version_id"] = quoteEntity.PlanVersionId ?? string.Empty,
                                        ["add_on_version_ids"] = quoteEntity.AddOnVersionIdsJson,
                                        ["coupon_version_id"] = quoteEntity.CouponVersionId ?? string.Empty
                                },
                                SuccessUrl: platformLinks.BuildWebUrl($"/billing/payment-return?status=success&gateway={Uri.EscapeDataString(gatewayLabel)}&quote={Uri.EscapeDataString(quoteEntity.Id)}&session={{CHECKOUT_SESSION_ID}}"),
                                    CancelUrl: platformLinks.BuildWebUrl($"/billing/payment-return?status=cancelled&gateway={Uri.EscapeDataString(gatewayLabel)}&quote={Uri.EscapeDataString(quoteEntity.Id)}"),
                                    IdempotencyKey: idempotencyKey),
                        cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not fully configured", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "gateway_unavailable",
                "This payment method is temporarily unavailable. Please pay by card instead.",
                [new ApiFieldError("gateway", "unavailable", "Choose a different payment method.")]);
        }
        catch (PaymentGatewayApiException)
        {
            // The payment provider's API rejected the request (e.g. invalid key, declined
            // params). Surface a clean, retryable 503 instead of an opaque 500. The real
            // provider detail is already logged inside the gateway and is never shown to
            // learners.
            throw ApiException.ServiceUnavailable(
                "payment_gateway_error",
                "We couldn't start your payment right now. Please try again in a moment or choose another payment method.",
                retryable: true);
        }
                        providerRequestReturned = true;

        quoteEntity.CheckoutSessionId = checkoutIntent.GatewayTransactionId;
        quoteEntity.Status = BillingQuoteStatus.Applied;

        var response = new
        {
            checkoutSessionId = checkoutIntent.GatewayTransactionId,
            quoteId = quoteEntity.Id,
            productType = normalizedProductType,
            quantity = request.Quantity,
            targetPlanId = quoteEntity.PlanCode,
            couponCode = quoteEntity.CouponCode,
            addOnCodes = JsonSupport.Deserialize<List<string>>(quoteEntity.AddOnCodesJson, []),
            subtotalAmount = quoteEntity.SubtotalAmount,
            discountAmount = quoteEntity.DiscountAmount,
            totalAmount = quoteEntity.TotalAmount,
            currency = quoteEntity.Currency,
            gateway = gatewayLabel,
            quote = quoteResponse,
            checkoutUrl = string.IsNullOrWhiteSpace(checkoutIntent.CheckoutUrl)
                ? platformLinks.BuildCheckoutUrl(
                    checkoutIntent.GatewayTransactionId,
                    normalizedProductType,
                    request.Quantity,
                    planId: quoteEntity.PlanCode,
                    couponCode: quoteEntity.CouponCode,
                    addOnCodes: JsonSupport.Deserialize<List<string>>(quoteEntity.AddOnCodesJson, []),
                    quoteId: quoteEntity.Id)
                : checkoutIntent.CheckoutUrl,
            state = checkoutIntent.Status
        };
        idempotencyResponse = response;

        var paymentTransaction = await db.PaymentTransactions.FirstOrDefaultAsync(
            transaction => transaction.GatewayTransactionId == checkoutIntent.GatewayTransactionId,
            cancellationToken);
        if (paymentTransaction is null)
        {
            var now = DateTimeOffset.UtcNow;
            paymentTransaction = new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                LearnerUserId = userId,
                Gateway = gatewayLabel,
                GatewayTransactionId = checkoutIntent.GatewayTransactionId,
                TransactionType = normalizedProductType is "plan_purchase" or "plan_upgrade" or "plan_downgrade"
                    ? "subscription_payment"
                    : "one_time_purchase",
                Status = "pending",
                Amount = quoteEntity.TotalAmount,
                Currency = quoteEntity.Currency,
                ProductType = normalizedProductType is "plan_purchase" or "plan_upgrade" or "plan_downgrade" ? "plan" : "addon",
                ProductId = purchaseTarget ?? quoteEntity.Id,
                QuoteId = quoteEntity.Id,
                PlanVersionId = quoteEntity.PlanVersionId,
                AddOnVersionIdsJson = quoteEntity.AddOnVersionIdsJson,
                CouponVersionId = quoteEntity.CouponVersionId,
                MetadataJson = JsonSupport.Serialize(new
                {
                    quoteId = quoteEntity.Id,
                    productType = normalizedProductType,
                    providerIntentId = checkoutIntent.ClientSecret,
                    purchaseTarget,
                    addOnCodes = JsonSupport.Deserialize<List<string>>(quoteEntity.AddOnCodesJson, []),
                    planCode = quoteEntity.PlanCode,
                    couponCode = quoteEntity.CouponCode,
                    planVersionId = quoteEntity.PlanVersionId,
                    addOnVersionIds = DeserializeAddOnVersionIds(quoteEntity),
                    couponVersionId = quoteEntity.CouponVersionId
                }),
                CreatedAt = now,
                UpdatedAt = now
            };
            db.PaymentTransactions.Add(paymentTransaction);
        }

        paymentTransaction.QuoteId = quoteEntity.Id;
        paymentTransaction.PlanVersionId = quoteEntity.PlanVersionId;
        paymentTransaction.AddOnVersionIdsJson = quoteEntity.AddOnVersionIdsJson;
        paymentTransaction.CouponVersionId = quoteEntity.CouponVersionId;
        paymentTransaction.MetadataJson = JsonSupport.Serialize(new
        {
            quoteId = quoteEntity.Id,
            productType = normalizedProductType,
            providerIntentId = checkoutIntent.ClientSecret,
            purchaseTarget,
            addOnCodes = JsonSupport.Deserialize<List<string>>(quoteEntity.AddOnCodesJson, []),
            planCode = quoteEntity.PlanCode,
            couponCode = quoteEntity.CouponCode,
            planVersionId = quoteEntity.PlanVersionId,
            addOnVersionIds = DeserializeAddOnVersionIds(quoteEntity),
            couponVersionId = quoteEntity.CouponVersionId
        });

        var reservedRedemptions = await db.BillingCouponRedemptions
            .Where(redemption => redemption.QuoteId == quoteEntity.Id && redemption.Status == BillingRedemptionStatus.Reserved)
            .ToListAsync(cancellationToken);
        foreach (var redemption in reservedRedemptions)
        {
            redemption.CheckoutSessionId = checkoutIntent.GatewayTransactionId;
        }

        db.BillingEvents.Add(new BillingEvent
        {
            Id = $"bill-evt-{Guid.NewGuid():N}",
            UserId = userId,
            QuoteId = quoteEntity.Id,
            EventType = "checkout_session_created",
            EntityType = "CheckoutSession",
            EntityId = checkoutIntent.GatewayTransactionId,
            PayloadJson = JsonSupport.Serialize(new
            {
                productType = normalizedProductType,
                quantity = request.Quantity,
                planCode = quoteEntity.PlanCode,
                couponCode = quoteEntity.CouponCode,
                addOnCodes = JsonSupport.Deserialize<List<string>>(quoteEntity.AddOnCodesJson, []),
                totalAmount = quoteEntity.TotalAmount,
                currency = quoteEntity.Currency,
                gateway = gatewayLabel,
                status = checkoutIntent.Status
            }),
            OccurredAt = DateTimeOffset.UtcNow
        });

        if (idempotencyKey is not null && idempotencyRequestHash is not null)
        {
            await CompletePaymentIdempotencyAsync("checkout-session", idempotencyKey, userId, idempotencyRequestHash, response, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        idempotencyCompleted = true;

        await RecordEventAsync(userId, "checkout_started", new
        {
            productType = normalizedProductType,
            quantity = request.Quantity,
            targetPlanId = quoteEntity.PlanCode,
            couponCode = quoteEntity.CouponCode,
            quoteId = quoteEntity.Id,
            totalAmount = quoteEntity.TotalAmount,
            gateway = gatewayLabel
        }, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return response;
        }
        catch
        {
            if (idempotencyKey is not null && idempotencyRequestHash is not null && !idempotencyCompleted)
            {
                if (idempotencyResponse is not null)
                {
                    await TryCompletePaymentIdempotencyAsync("checkout-session", idempotencyKey, userId, idempotencyRequestHash, idempotencyResponse, cancellationToken);
                }
                else if (!providerRequestReturned)
                {
                    await RemovePaymentIdempotencyReservationAsync("checkout-session", idempotencyKey, cancellationToken);
                }
            }
            throw;
        }
    }

    public async Task<object> CreateMockAttemptAsync(string userId, MockAttemptCreateRequest request, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var id = $"mock-attempt-{Guid.NewGuid():N}";
        var reviewSelection = NormalizeMockReviewSelection(request.MockType, request.SubType, request.IncludeReview, request.ReviewSelection);
        var config = new
        {
            mockType = request.MockType,
            subType = request.SubType,
            mode = request.Mode,
            profession = request.Profession,
            includeReview = request.IncludeReview,
            strictTimer = request.StrictTimer,
            reviewSelection
        };
        var attempt = new MockAttempt
        {
            Id = id,
            UserId = userId,
            ConfigJson = JsonSupport.Serialize(config),
            State = AttemptState.InProgress,
            StartedAt = DateTimeOffset.UtcNow
        };
        db.MockAttempts.Add(attempt);
        await RecordEventAsync(userId, "mock_started", new { mockAttemptId = attempt.Id, type = request.MockType, subType = request.SubType, mode = request.Mode, reviewSelection }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new
        {
            mockAttemptId = attempt.Id,
            state = ToApiState(attempt.State),
            config,
            sectionStates = MockAttemptSections(attempt.Id, config),
            resumeRoute = $"/mocks/player/{attempt.Id}",
            reportRoute = (string?)null
        };
    }

    public async Task<object> GetMockAttemptAsync(string userId, string mockAttemptId, CancellationToken cancellationToken)
    {
        var attempt = await GetMockAttemptOwnedByUserAsync(userId, mockAttemptId, cancellationToken);
        var config = JsonSupport.Deserialize<Dictionary<string, object?>>(attempt.ConfigJson, new Dictionary<string, object?>());
        return new
        {
            mockAttemptId = attempt.Id,
            state = ToApiState(attempt.State),
            startedAt = attempt.StartedAt,
            submittedAt = attempt.SubmittedAt,
            completedAt = attempt.CompletedAt,
            config,
            sectionStates = MockAttemptSections(attempt.Id, config),
            resumeRoute = $"/mocks/player/{attempt.Id}",
            reportRoute = attempt.ReportId is null ? null : $"/mocks/report/{attempt.ReportId}",
            reportId = attempt.ReportId
        };
    }

    public async Task<object> SubmitMockAttemptAsync(string userId, string mockAttemptId, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetMockAttemptOwnedByUserAsync(userId, mockAttemptId, cancellationToken);
        attempt.State = AttemptState.Evaluating;
        attempt.SubmittedAt = DateTimeOffset.UtcNow;
        await QueueJobAsync(JobType.MockReportGeneration, resourceId: attempt.Id, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new { mockAttemptId = attempt.Id, state = "queued", nextPollAfterMs = 2000 };
    }

    public async Task<object> GetMockReportAsync(string userId, string reportId, CancellationToken cancellationToken)
    {
        var report = await GetMockReportOwnedByUserAsync(userId, reportId, cancellationToken);
        var payload = JsonSupport.Deserialize<Dictionary<string, object?>>(report.PayloadJson, new Dictionary<string, object?>());
        payload["state"] = ToAsyncState(report.State);
        payload["generatedAt"] = report.GeneratedAt;
        payload["studyPlanUpdateCta"] = new
        {
            label = "Update study plan",
            route = "/study-plan"
        };
        return payload;
    }

    public async Task<object> GetReviewsAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var attemptIds = await db.Attempts.Where(x => x.UserId == userId).Select(x => x.Id).ToListAsync(cancellationToken);
        var reviews = await db.ReviewRequests.Where(x => attemptIds.Contains(x.AttemptId)).OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
        return new
        {
            items = reviews.Select(x => new
            {
                reviewRequestId = x.Id,
                attemptId = x.AttemptId,
                subtest = x.SubtestCode,
                state = ToReviewRequestState(x.State),
                turnaroundOption = x.TurnaroundOption,
                focusAreas = JsonSupport.Deserialize<List<string>>(x.FocusAreasJson, []),
                createdAt = x.CreatedAt,
                completedAt = x.CompletedAt
            })
        };
    }

    public async Task<object> GetReviewEligibilityAsync(string userId, string? attemptId, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        Attempt? attempt = null;
        if (!string.IsNullOrWhiteSpace(attemptId))
        {
            attempt = await db.Attempts.FirstOrDefaultAsync(x => x.Id == attemptId && x.UserId == userId, cancellationToken);
        }

        var wallet = await db.Wallets.FirstAsync(x => x.UserId == userId, cancellationToken);
        var canRequest = attempt is not null && attempt.State == AttemptState.Completed && attempt.SubtestCode is "writing" or "speaking";
        var reasons = new List<string>();
        if (attempt is null) reasons.Add("attempt_not_found");
        else if (attempt.State != AttemptState.Completed) reasons.Add("attempt_not_completed");
        else if (attempt.SubtestCode is not ("writing" or "speaking")) reasons.Add("unsupported_subtest");

        return new
        {
            attemptId,
            canRequestReview = canRequest,
            canPurchaseExtras = true,
            availableCredits = wallet.CreditBalance,
            turnaroundOptions = new[]
            {
                new { id = "standard", label = "Standard", time = "48-72 hours", cost = 1, description = "Detailed written feedback within three business days." },
                new { id = "express", label = "Express", time = "24 hours", cost = 2, description = "Priority turnaround within 24 hours." }
            },
            eligibilityReasonCodes = reasons
        };
    }

    public async Task<object> CreateReviewRequestAsync(string userId, ReviewRequestCreateRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        // Higher retry count tolerates cross-worker wallet contention during
        // parallel CI Playwright shards. The helper retries on the resulting
        // 409 wallet_update_conflict as well.
        for (var attemptNumber = 0; attemptNumber < 4; attemptNumber++)
        {
            try
            {
                return await CreateReviewRequestCoreAsync(userId, request, cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attemptNumber < 3)
            {
                db.ChangeTracker.Clear();
            }
        }

        throw ApiException.Conflict(
            "wallet_update_conflict",
            "Your review credits were updated at the same time as this request. Please try again.");
    }

    private async Task<object> CreateReviewRequestCoreAsync(string userId, ReviewRequestCreateRequest request, CancellationToken cancellationToken)
    {
        var user = await EnsureLearnerProfileAsync(userId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var cached = await GetIdempotentResponseAsync("review-request", request.IdempotencyKey, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }
        }

        var attempt = await GetAttemptOwnedByUserAsync(userId, request.AttemptId, cancellationToken);
        var turnaroundOption = (request.TurnaroundOption ?? string.Empty).Trim().ToLowerInvariant();
        if (turnaroundOption is not ("standard" or "express"))
        {
            throw ApiException.Validation(
                "invalid_turnaround_option",
                "Choose a valid tutor review turnaround option.",
                [new ApiFieldError("turnaroundOption", "invalid", "Turnaround must be standard or express.")]);
        }

        var paymentSource = (request.PaymentSource ?? string.Empty).Trim().ToLowerInvariant();
        if (paymentSource != "credits")
        {
            throw ApiException.Validation(
                "unsupported_payment_source",
                "Tutor review requests currently use review credits.",
                [new ApiFieldError("paymentSource", "unsupported", "Only review credits are supported for tutor review requests.")]);
        }

        var cost = turnaroundOption == "express" ? 2 : 1;

        if (attempt.SubtestCode is not ("writing" or "speaking") || attempt.State != AttemptState.Completed)
        {
            throw ApiException.Validation(
                "review_not_eligible",
                "This attempt is not eligible for tutor review.",
                [new ApiFieldError("attemptId", "not_eligible", "Only completed writing and speaking attempts can be sent for tutor review.")]);
        }

        if (!string.Equals(request.Subtest, attempt.SubtestCode, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "review_subtest_mismatch",
                "The review request subtest does not match the attempt.",
                [new ApiFieldError("subtest", "mismatch", "Use the same subtest as the selected attempt.")]);
        }

        var existingActiveReview = await db.ReviewRequests.AsNoTracking()
            .FirstOrDefaultAsync(x => x.AttemptId == request.AttemptId
                                      && x.State != ReviewRequestState.Completed
                                      && x.State != ReviewRequestState.Failed
                                      && x.State != ReviewRequestState.Cancelled,
                cancellationToken);
        if (existingActiveReview is not null)
        {
            throw ApiException.Conflict(
                "review_already_active",
                "This attempt already has an active tutor review request.");
        }

        var now = DateTimeOffset.UtcNow;
        var reviewId = $"review-{Guid.NewGuid():N}";

        // Spec ("Decrement writing_assessments_remaining on submission"): a learner with
        // bundled/add-on writing assessments consumes one (free) instead of paying when
        // submitting a WRITING letter for expert review. Resolve a tracked, eligible
        // subscription so the decrement participates in the same SaveChanges + outer
        // DbUpdateConcurrencyException retry that protects the wallet path from a racing
        // double-spend. The idempotency cache above already short-circuits a retried
        // submit before reaching this point, so a retry never double-decrements.
        var isWritingReview = string.Equals(attempt.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase);
        var prepaidSubscription = isWritingReview
            ? await ResolveEligibleWritingSubscriptionAsync(userId, now, cancellationToken)
            : null;

        string effectivePaymentSource;
        decimal priceSnapshot;
        object eligibilitySnapshot;

        if (prepaidSubscription is not null)
        {
            // Prepaid (entitlement) path: consume one bundled writing assessment and skip
            // the wallet load, wallet debit, and WalletTransaction entirely. PriceSnapshot
            // is 0 so downstream credits-only refund/reporting logic (which all gate on
            // PaymentSource == "credits" AND PriceSnapshot > 0) correctly treats this as a
            // non-credit, nothing-to-refund request.
            prepaidSubscription.WritingAssessmentsRemaining = Math.Max(0, prepaidSubscription.WritingAssessmentsRemaining - 1);
            effectivePaymentSource = "entitlement";
            priceSnapshot = 0m;
            eligibilitySnapshot = new
            {
                canRequestReview = true,
                paymentSource = "entitlement",
                writingAssessmentsRemaining = prepaidSubscription.WritingAssessmentsRemaining,
                entitlementSubscriptionId = prepaidSubscription.Id
            };
        }
        else
        {
            // Wallet (credits) path — unchanged.
            var wallet = await db.Wallets.FirstAsync(x => x.UserId == userId, cancellationToken);
            if (wallet.CreditBalance < cost)
            {
                throw ApiException.Validation(
                    "insufficient_credits",
                    "You do not have enough review credits for this request.",
                    [new ApiFieldError("paymentSource", "insufficient_credits", "Buy more credits or choose a different payment flow.")]);
            }

            wallet.CreditBalance -= cost;
            wallet.LastUpdatedAt = now;
            db.WalletTransactions.Add(new WalletTransaction
            {
                Id = Guid.NewGuid(),
                WalletId = wallet.Id,
                TransactionType = "review_deduction",
                Amount = -cost,
                BalanceAfter = wallet.CreditBalance,
                ReferenceType = "review",
                ReferenceId = reviewId,
                Description = $"Tutor review request for {attempt.SubtestCode} attempt {attempt.Id}.",
                CreatedBy = userId,
                CreatedAt = now
            });
            effectivePaymentSource = paymentSource;
            priceSnapshot = cost;
            eligibilitySnapshot = new { canRequestReview = true, availableCredits = wallet.CreditBalance };
        }

        db.Entry(user).Property(x => x.AccountStatus).IsModified = true;

        var review = new ReviewRequest
        {
            Id = reviewId,
            AttemptId = request.AttemptId,
            SubtestCode = request.Subtest,
            State = ReviewRequestState.Queued,
            TurnaroundOption = turnaroundOption,
            FocusAreasJson = JsonSupport.Serialize(request.FocusAreas),
            LearnerNotes = request.LearnerNotes ?? string.Empty,
            PaymentSource = effectivePaymentSource,
            PriceSnapshot = priceSnapshot,
            CreatedAt = now,
            EligibilitySnapshotJson = JsonSupport.Serialize(eligibilitySnapshot)
        };

        db.ReviewRequests.Add(review);

        await RecordEventAsync(userId, "review_requested", new { reviewRequestId = review.Id, attemptId = review.AttemptId, subtest = review.SubtestCode, turnaroundOption = review.TurnaroundOption }, cancellationToken);
        LogAudit(userId, "Created", "ReviewRequest", review.Id, prepaidSubscription is not null
            ? $"Tutor review requested for {review.SubtestCode} attempt {review.AttemptId}, funded by writing-assessment entitlement (subscription {prepaidSubscription.Id})"
            : $"Tutor review requested for {review.SubtestCode} attempt {review.AttemptId}, cost={cost} credits");
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            await SaveIdempotentResponseAsync("review-request", request.IdempotencyKey, new { reviewRequestId = review.Id }, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerReviewRequested,
            userId,
            "review_request",
            review.Id,
            review.CreatedAt.UtcDateTime.Ticks.ToString(),
            new Dictionary<string, object?>
            {
                ["attemptId"] = review.AttemptId,
                ["reviewRequestId"] = review.Id,
                ["subtest"] = review.SubtestCode,
                ["message"] = prepaidSubscription is not null
                    ? "Your tutor review request is queued. One bundled writing assessment was used."
                    : $"Your tutor review request is queued. {cost} review credit{(cost == 1 ? string.Empty : "s")} used."
            },
            cancellationToken);
        await notifications.CreateForAdminsAsync(
            NotificationEventKey.AdminReviewOpsAction,
            "review_request",
            review.Id,
            review.CreatedAt.UtcDateTime.Ticks.ToString(),
            new Dictionary<string, object?>
            {
                ["reviewRequestId"] = review.Id,
                ["attemptId"] = review.AttemptId,
                ["message"] = $"Learner {user.DisplayName} requested a {review.SubtestCode} tutor review with {review.TurnaroundOption} turnaround."
            },
            cancellationToken);
        return await GetReviewRequestAsync(userId, review.Id, cancellationToken);
    }

    /// <summary>
    /// Resolves a <b>tracked</b> <see cref="Subscription"/> for the learner that has at least
    /// one bundled/add-on writing assessment remaining and is currently usable, so a writing
    /// expert-review submission can consume it instead of charging wallet credits.
    /// Mirrors <c>PrivateSpeakingService.ResolveEligibleSpeakingSubscriptionAsync</c> but is
    /// intentionally self-contained (a direct query rather than a dependency on the entitlement
    /// resolver) to keep this change's footprint minimal. The result is tracked (NOT
    /// AsNoTracking) because the caller mutates <see cref="Subscription.WritingAssessmentsRemaining"/>.
    /// Eligibility: status Active or Trial, not expired (<see cref="Subscription.ExpiresAt"/>
    /// null or in the future), and <see cref="Subscription.WritingAssessmentsRemaining"/> &gt; 0.
    /// When several rows qualify, the one expiring soonest is consumed first (nulls last).
    /// </summary>
    private async Task<Subscription?> ResolveEligibleWritingSubscriptionAsync(
        string userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidates = await db.Subscriptions
            .Where(subscription => subscription.UserId == userId
                && subscription.WritingAssessmentsRemaining > 0)
            .ToListAsync(cancellationToken);

        return candidates
            .Where(subscription => subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.Trial)
            .Where(subscription => subscription.ExpiresAt is null || subscription.ExpiresAt > now)
            .OrderBy(subscription => subscription.ExpiresAt == null)
            .ThenBy(subscription => subscription.ExpiresAt)
            .FirstOrDefault();
    }

    public async Task<object> GetReviewRequestAsync(string userId, string reviewRequestId, CancellationToken cancellationToken)
    {
        var review = await GetReviewRequestOwnedByUserAsync(userId, reviewRequestId, cancellationToken);
        return new
        {
            reviewRequestId = review.Id,
            attemptId = review.AttemptId,
            subtest = review.SubtestCode,
            state = ToReviewRequestState(review.State),
            turnaroundOption = review.TurnaroundOption,
            focusAreas = JsonSupport.Deserialize<List<string>>(review.FocusAreasJson, []),
            learnerNotes = review.LearnerNotes,
            paymentSource = review.PaymentSource,
            priceSnapshot = review.PriceSnapshot,
            createdAt = review.CreatedAt,
            completedAt = review.CompletedAt,
            eligibilitySnapshot = JsonSupport.Deserialize<Dictionary<string, object?>>(review.EligibilitySnapshotJson, new Dictionary<string, object?>())
        };
    }

    public async Task<object> GetBillingSummaryAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        await EnsureSubscriptionInvoiceAsync(userId, cancellationToken);
        var hasInvoices = await db.Invoices.AnyAsync(x => x.UserId == userId, cancellationToken);
        // Fresh accounts have no Subscription/Wallet rows until their first purchase —
        // the summary must degrade to a well-formed "no subscription" payload, not 500.
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        var wallet = await db.Wallets.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var freeze = await GetFreezeStatusAsync(userId, cancellationToken);
        var walletPayload = new
        {
            walletId = wallet?.Id,
            creditBalance = wallet?.CreditBalance ?? 0,
            ledgerSummary = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(wallet?.LedgerSummaryJson ?? "[]", [])
        };
        if (subscription is null)
        {
            return new
            {
                subscriptionId = (string?)null,
                planId = (string?)null,
                planCode = "none",
                planName = "No subscription",
                planDescription = (string?)null,
                profession = "all",
                status = "none",
                nextRenewalAt = (DateTimeOffset?)null,
                startedAt = (DateTimeOffset?)null,
                changedAt = (DateTimeOffset?)null,
                freeze,
                price = new { amount = 0m, currency = "GBP", interval = "none" },
                wallet = walletPayload,
                activeAddOns = Enumerable.Empty<object>(),
                entitlements = new
                {
                    productiveSkillReviewsEnabled = false,
                    supportedReviewSubtests = new List<string> { "writing", "speaking" },
                    invoiceDownloadsAvailable = hasInvoices
                },
                plan = (object?)null
            };
        }
        var currentPlan = await FindBillingPlanAsync(subscription.PlanId, cancellationToken);
        var activeAddOns = await db.SubscriptionItems.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id && x.Status == SubscriptionItemStatus.Active)
            .ToListAsync(cancellationToken);
        var addOnCodes = activeAddOns.Select(x => x.ItemCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var addOnCatalog = addOnCodes.Count == 0
            ? []
            : await db.BillingAddOns.AsNoTracking()
                .Where(x => addOnCodes.Contains(x.Code))
                .ToListAsync(cancellationToken);
        return new
        {
            subscriptionId = subscription.Id,
            planId = subscription.PlanId,
            planCode = currentPlan?.Code ?? subscription.PlanId,
            planName = currentPlan?.Name ?? subscription.PlanId,
            planDescription = currentPlan?.Description,
            profession = currentPlan?.Profession ?? "all",
            status = ToSubscriptionState(subscription.Status),
            nextRenewalAt = subscription.NextRenewalAt,
            startedAt = subscription.StartedAt,
            changedAt = subscription.ChangedAt,
            freeze,
            price = new { amount = subscription.PriceAmount, currency = subscription.Currency, interval = subscription.Interval },
            wallet = walletPayload,
            activeAddOns = activeAddOns.Select(item =>
            {
                var addOn = addOnCatalog.FirstOrDefault(x => string.Equals(x.Code, item.ItemCode, StringComparison.OrdinalIgnoreCase));
                return new
                {
                    id = item.Id,
                    code = item.ItemCode,
                    name = addOn?.Name ?? item.ItemCode,
                    type = item.ItemType,
                    quantity = item.Quantity,
                    status = item.Status.ToString().ToLowerInvariant(),
                    startsAt = item.StartsAt,
                    endsAt = item.EndsAt,
                    quoteId = item.QuoteId,
                    checkoutSessionId = item.CheckoutSessionId,
                    grantCredits = addOn?.GrantCredits ?? 0,
                    price = addOn is null
                        ? null
                        : new { amount = addOn.Price, currency = addOn.Currency, interval = addOn.Interval },
                    description = addOn?.Description
                };
            }),
            entitlements = new
            {
                productiveSkillReviewsEnabled = subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.Trial,
                supportedReviewSubtests = currentPlan is null
                    ? new List<string> { "writing", "speaking" }
                    : JsonSupport.Deserialize<List<string>>(currentPlan.IncludedSubtestsJson, new List<string> { "writing", "speaking" }),
                invoiceDownloadsAvailable = hasInvoices
            },
            plan = currentPlan is null
                ? null
                : new
                {
                    code = currentPlan.Code,
                    name = currentPlan.Name,
                    description = currentPlan.Description,
                    includedCredits = currentPlan.IncludedCredits,
                    durationMonths = currentPlan.DurationMonths,
                    isRenewable = currentPlan.IsRenewable,
                    status = currentPlan.Status.ToString().ToLowerInvariant()
                }
        };
    }

    public async Task<object> GetBillingPlansAsync(string userId, CancellationToken cancellationToken)
    {
        var isPublic = string.IsNullOrWhiteSpace(userId);
        Subscription? subscription = null;
        if (!isPublic)
        {
            await EnsureUserAsync(userId, cancellationToken);
            // Null for accounts that never purchased — the plan list below already handles it.
            subscription = await db.Subscriptions.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        }

        var normalizedSubscriptionPlanId = subscription is not null ? NormalizeBillingCode(subscription.PlanId) : string.Empty;
        var plans = await db.BillingPlans.AsNoTracking()
            .Where(plan => plan.IsVisible || (subscription != null && plan.Code.ToLower() == normalizedSubscriptionPlanId))
            .OrderBy(plan => plan.DisplayOrder)
            .ThenBy(plan => plan.Price)
            .ToListAsync(cancellationToken);
        var currentPlan = subscription is not null
            ? (plans.FirstOrDefault(plan => string.Equals(plan.Code, subscription.PlanId, StringComparison.OrdinalIgnoreCase))
                ?? plans.FirstOrDefault(plan => plan.Status == BillingPlanStatus.Active)
                ?? plans.FirstOrDefault())
            : plans.FirstOrDefault(plan => plan.Status == BillingPlanStatus.Active) ?? plans.FirstOrDefault();
        return new
        {
            currentPlanId = subscription?.PlanId,
            currentPlanCode = currentPlan?.Code,
            items = plans.Select(plan => new
            {
                planId = plan.Code,
                code = plan.Code,
                label = plan.Name,
                tier = plan.Code,
                description = plan.Description,
                price = new { amount = plan.Price, currency = plan.Currency, interval = plan.Interval },
                reviewCredits = plan.IncludedCredits,
                mockReportsIncluded = true,
                canChangeTo = plan.Status == BillingPlanStatus.Active && (subscription is null || !string.Equals(plan.Code, subscription.PlanId, StringComparison.OrdinalIgnoreCase)),
                changeDirection = subscription is not null && string.Equals(plan.Code, subscription.PlanId, StringComparison.OrdinalIgnoreCase)
                    ? "current"
                    : plan.Price > (currentPlan?.Price ?? plan.Price)
                        ? "upgrade"
                        : plan.Price < (currentPlan?.Price ?? plan.Price)
                            ? "downgrade"
                            : "current",
                badge = plan.Status.ToString().ToLowerInvariant(),
                status = plan.Status.ToString().ToLowerInvariant(),
                displayOrder = plan.DisplayOrder,
                durationMonths = plan.DurationMonths,
                isVisible = plan.IsVisible,
                isRenewable = plan.IsRenewable,
                trialDays = plan.TrialDays,
                includedSubtests = JsonSupport.Deserialize<List<string>>(plan.IncludedSubtestsJson, []),
                entitlements = JsonSupport.Deserialize<Dictionary<string, object?>>(plan.EntitlementsJson, new Dictionary<string, object?>()
                )
            })
        };
    }

      public async Task<object> GetBillingChangePreviewAsync(string userId, string targetPlanId, CancellationToken cancellationToken)
      {
          await EnsureUserAsync(userId, cancellationToken);
          var subscription = await db.Subscriptions.FirstAsync(x => x.UserId == userId, cancellationToken);
          var currentPlan = await FindBillingPlanAsync(subscription.PlanId, cancellationToken)
              ?? throw ApiException.NotFound("billing_plan_not_found", "Your current billing plan could not be found.");
          var targetPlan = await FindPurchasableBillingPlanAsync(targetPlanId, cancellationToken)
              ?? throw ApiException.Validation(
                  "unknown_plan",
                  $"Unknown billing plan '{targetPlanId}'.",
                  [new ApiFieldError("targetPlanId", "unknown", "Choose a published billing plan.")]);

        var delta = targetPlan.Price - currentPlan.Price;
        var direction = delta >= 0 ? "upgrade" : "downgrade";
        return new
        {
            currentPlanId = currentPlan.Code,
            targetPlanId = targetPlan.Code,
            direction,
            proratedAmount = Math.Round(Math.Abs(delta) / 2m, 2),
            effectiveAt = subscription.NextRenewalAt,
            summary = direction == "upgrade"
                ? $"Switching to {targetPlan.Name} increases your billing amount by {Math.Abs(delta):0.00} {subscription.Currency}."
                : $"Switching to {targetPlan.Name} lowers your billing amount by {Math.Abs(delta):0.00} {subscription.Currency}.",
            currentCreditsIncluded = currentPlan.IncludedCredits,
            targetCreditsIncluded = targetPlan.IncludedCredits
        };
    }

    public async Task<object> CancelOwnSubscriptionAsync(string userId, bool immediate, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var subscription = await db.Subscriptions.FirstAsync(x => x.UserId == userId, cancellationToken);

        if (subscription.Status == SubscriptionStatus.Cancelled)
        {
            throw ApiException.Validation("subscription_already_cancelled", "Your subscription is already cancelled.");
        }

        if (subscription.Status == SubscriptionStatus.Expired)
        {
            throw ApiException.Validation("subscription_expired", "Your subscription has already expired.");
        }

        var now = DateTimeOffset.UtcNow;
        var previousStatus = subscription.Status;

        if (immediate)
        {
            SubscriptionStateMachine.Transition(subscription, SubscriptionStatus.Cancelled, "learner_self_cancel_immediate");
            subscription.NextRenewalAt = now;
        }
        subscription.ChangedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerSubscriptionCancelled,
            userId,
            "Subscription",
            subscription.Id,
            now.UtcDateTime.ToString("yyyy-MM-dd"),
            new Dictionary<string, object?>
            {
                ["message"] = immediate
                    ? "Your subscription has been cancelled immediately."
                    : $"Your subscription is scheduled to cancel at the end of your current billing period ({subscription.NextRenewalAt:yyyy-MM-dd}).",
                ["planName"] = subscription.PlanId,
                ["status"] = subscription.Status.ToString().ToLowerInvariant()
            },
            cancellationToken);

        return new
        {
            subscriptionId = subscription.Id,
            status = ToSubscriptionState(subscription.Status),
            cancelledAt = now,
            effectiveEndAt = subscription.NextRenewalAt,
            immediate
        };
    }

    public async Task<object> ReactivateOwnSubscriptionAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var subscription = await db.Subscriptions.FirstAsync(x => x.UserId == userId, cancellationToken);

        if (subscription.Status != SubscriptionStatus.Cancelled)
        {
            throw ApiException.Validation("subscription_not_cancelled", "Only cancelled subscriptions can be reactivated.");
        }

        var now = DateTimeOffset.UtcNow;
        SubscriptionStateMachine.ReactivateCancelled(subscription, "learner_self_reactivate", now);
        if (subscription.NextRenewalAt <= now)
        {
            subscription.NextRenewalAt = now.AddMonths(1);
        }

        await db.SaveChangesAsync(cancellationToken);

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerSubscriptionChanged,
            userId,
            "Subscription",
            subscription.Id,
            now.UtcDateTime.ToString("yyyy-MM-dd"),
            new Dictionary<string, object?>
            {
                ["message"] = "Your subscription has been reactivated.",
                ["planName"] = subscription.PlanId,
                ["status"] = "active"
            },
            cancellationToken);

        return new
        {
            subscriptionId = subscription.Id,
            status = ToSubscriptionState(subscription.Status),
            reactivatedAt = now,
            nextRenewalAt = subscription.NextRenewalAt
        };
    }

    public Task<object> GetInvoicesAsync(string userId, CancellationToken cancellationToken)
        => GetInvoicesAsync(userId, cursor: null, limit: null, cancellationToken);

    public async Task<object> GetInvoicesAsync(string userId, string? cursor, int? limit, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        await EnsureSubscriptionInvoiceAsync(userId, cancellationToken);
        var pageSize = CursorPagination.NormalizeLimit(limit);
        var invoices = (await db.Invoices
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.IssuedAt)
            .ThenByDescending(x => x.Id, StringComparer.Ordinal)
            .ToList();

        IEnumerable<Invoice> window = invoices;
        if (CursorPagination.TryDecode(cursor, out var decoded))
        {
            window = invoices.Where(x =>
            {
                if (x.IssuedAt < decoded.Timestamp) return true;
                if (x.IssuedAt == decoded.Timestamp) return string.CompareOrdinal(x.Id, decoded.Id) < 0;
                return false;
            });
        }

        var page = window.Take(pageSize + 1).ToList();
        var hasMore = page.Count > pageSize;
        var pageInvoices = hasMore ? page.Take(pageSize).ToList() : page;
        string? nextCursor = null;
        if (hasMore)
        {
            var last = pageInvoices[^1];
            nextCursor = CursorPagination.Encode(last.IssuedAt, last.Id);
        }

        return new
        {
            items = pageInvoices.Select(x => new
            {
                invoiceId = x.Id,
                date = x.IssuedAt,
                amount = x.Amount,
                currency = x.Currency,
                status = x.Status,
                description = x.Description,
                downloadUrl = platformLinks.BuildApiUrl($"/v1/billing/invoices/{Uri.EscapeDataString(x.Id)}/download")
            }),
            nextCursor
        };
    }

    public async Task<GeneratedDownloadFile> GetInvoiceDownloadAsync(string userId, string invoiceId, CancellationToken cancellationToken)
    {
        var user = await EnsureUserAsync(userId, cancellationToken);
        var invoice = await db.Invoices.FirstOrDefaultAsync(x => x.UserId == userId && x.Id == invoiceId, cancellationToken)
            ?? throw ApiException.NotFound("invoice_not_found", "Invoice not found.");

        var pdf = (invoicePdfService ?? new InvoicePdfService()).Generate(new InvoicePdfModel(
            InvoiceId: invoice.Id,
            Number: invoice.Number,
            IssuedAt: invoice.IssuedAt,
            Amount: invoice.Amount,
            Currency: invoice.Currency,
            Status: invoice.Status,
            Description: invoice.Description,
            BillToName: user.DisplayName,
            BillToEmail: user.Email));

        return new GeneratedDownloadFile(new MemoryStream(pdf.Bytes), "application/pdf", pdf.Filename);
    }

    /// <summary>
    /// Guarantees the learner's current paid subscription has a downloadable invoice.
    /// Invoices are normally created only inside payment-webhook fulfillment
    /// (<see cref="ApplyCheckoutCompletionAsync"/>); subscriptions created another way
    /// (admin grant, complimentary, or a purchase that never ran fulfillment) would
    /// otherwise have none. Idempotent and safe to call on every billing read:
    ///   • no-op for free plans (PriceAmount &lt;= 0),
    ///   • no-op when an invoice already covers the current purchase (matched by plan
    ///     version or by amount) so it never duplicates a real checkout invoice,
    ///   • deterministic per-purchase id so concurrent reads converge on one row.
    /// </summary>
    private async Task<bool> EnsureSubscriptionInvoiceAsync(string userId, CancellationToken cancellationToken)
    {
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (subscription is null || subscription.PriceAmount <= 0)
        {
            return false;
        }

        var alreadyCovered = await db.Invoices.AnyAsync(
            x => x.UserId == userId
                 && (x.Amount == subscription.PriceAmount
                     || (subscription.PlanVersionId != null && x.PlanVersionId == subscription.PlanVersionId)),
            cancellationToken);
        if (alreadyCovered)
        {
            return false;
        }

        // Compact, deterministic id keyed to this exact purchase (subscription + plan +
        // start instant). A later course purchase changes StartedAt and yields a new
        // invoice; repeat reads of the same purchase converge on this id.
        var compositeKey = $"{subscription.Id}|{subscription.PlanId}|{subscription.StartedAt.UtcTicks}";
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(compositeKey)))[..24]
            .ToLowerInvariant();
        var invoiceId = $"inv-sub-{hash}";

        if (await db.Invoices.AnyAsync(x => x.Id == invoiceId, cancellationToken))
        {
            return false;
        }

        var currentPlan = await FindBillingPlanAsync(subscription.PlanId, cancellationToken);
        var issuedAt = subscription.StartedAt != default
            ? subscription.StartedAt
            : (subscription.ChangedAt != default ? subscription.ChangedAt : DateTimeOffset.UtcNow);
        var planName = currentPlan?.Name ?? subscription.PlanId;
        var description = string.IsNullOrWhiteSpace(subscription.Interval)
            ? planName
            : $"{planName} ({subscription.Interval})";

        db.Invoices.Add(new Invoice
        {
            Id = invoiceId,
            UserId = userId,
            Number = await AllocateInvoiceNumberAsync(userId, invoiceId, cancellationToken),
            IssuedAt = issuedAt,
            Amount = subscription.PriceAmount,
            Currency = subscription.Currency,
            Status = "Paid",
            Description = description,
            PlanVersionId = subscription.PlanVersionId
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // A concurrent billing read created the same deterministic invoice first.
            // The invoice now exists either way — drop our pending insert and move on.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>
    /// Admin one-shot: ensures every learner on a paid subscription has a downloadable
    /// invoice. Idempotent — reuses <see cref="EnsureSubscriptionInvoiceAsync"/>, so it
    /// only creates the invoices that are genuinely missing. Returns how many users were
    /// scanned and how many invoices were created.
    /// </summary>
    public async Task<object> BackfillSubscriptionInvoicesAsync(CancellationToken cancellationToken)
    {
        var userIds = await db.Subscriptions
            .AsNoTracking()
            .Where(x => x.PriceAmount > 0)
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var created = 0;
        foreach (var userId in userIds)
        {
            if (await EnsureSubscriptionInvoiceAsync(userId, cancellationToken))
            {
                created++;
            }
        }

        return new { scanned = userIds.Count, created };
    }

    /// <summary>
    /// Renews the learner's one-time self-service freeze entitlement when they buy a
    /// new subscription/plan. The freeze rule is "once per subscription purchase": a
    /// fresh course purchase should grant a fresh freeze. We reuse the existing
    /// <see cref="AccountFreezeEntitlement.ResetAt"/> semantics — eligibility treats a
    /// reset entitlement as available, and the next freeze re-consumes it
    /// (clearing ResetAt). No-op if the entitlement was never consumed.
    /// </summary>
    private async Task ResetFreezeEntitlementForNewPurchaseAsync(string userId, CancellationToken cancellationToken)
    {
        var entitlement = await db.AccountFreezeEntitlements.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (entitlement is null || entitlement.ConsumedAt is null || entitlement.ResetAt is not null)
        {
            return;
        }

        entitlement.ResetAt = DateTimeOffset.UtcNow;
        entitlement.ResetReason = "new_subscription_purchase";
    }

    public object GetReviewOptions() => new
    {
        items = new[]
        {
            new { id = "standard", label = "Standard Review", turnaround = "48-72 hours", price = 1, currency = "credit", description = "Detailed tutor review with criterion-level notes." },
            new { id = "express", label = "Express Review", turnaround = "24 hours", price = 2, currency = "credit", description = "Priority tutor review returned within a day." }
        }
    };
    public async Task<object> GetMocksAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);

        var wallet = await db.Wallets.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        var attemptIds = await db.MockAttempts.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var reports = (await db.MockReports.AsNoTracking()
            .Where(report => attemptIds.Contains(report.MockAttemptId))
            .ToListAsync(cancellationToken))
            .OrderByDescending(report => report.GeneratedAt)
            .Take(6)
            .ToList();

        var reportItems = reports
            .Select(report => JsonSupport.Deserialize<Dictionary<string, object?>>(report.PayloadJson, new Dictionary<string, object?>()))
            .ToList();
        var latestReport = reportItems.FirstOrDefault();

        return new
        {
            reports = reportItems,
            recommendedNextMock = new
            {
                id = latestReport?.GetValueOrDefault("id")?.ToString() ?? "full-mock-next",
                title = latestReport is null ? "Full OET Mock Test" : "Review the next full mock",
                rationale = latestReport is null
                    ? "Start a full mock to capture a baseline performance snapshot."
                    : $"Your latest report scored {latestReport.GetValueOrDefault("overallScore")?.ToString() ?? "an updated"} overall. Run another full mock to confirm the gains.",
                route = "/mocks/setup"
            },
            purchasedMockReviews = new
            {
                availableCredits = wallet?.CreditBalance ?? 0
            },
            collections = new
            {
                fullMocks = new MockHistoryCard[]
                {
                    new(
                        "fm-1",
                        "Full Mock Test 1",
                        latestReport is null ? "available" : "completed",
                        latestReport?.GetValueOrDefault("overallScore")?.ToString() ?? "340",
                        latestReport?.GetValueOrDefault("date")?.ToString() ?? DateTimeOffset.UtcNow.ToString("MMM dd, yyyy"),
                        "3h 15m",
                        latestReport is null,
                        latestReport is null ? "Complete the first full mock to establish a baseline." : null),
                    new(
                        "fm-2",
                        "Full Mock Test 2",
                        "completed",
                        "B/B/B/B",
                        "Nov 05, 2023",
                        "3h 15m",
                        false,
                        null),
                    new(
                        "fm-3",
                        "Full Mock Test 3",
                        "available",
                        null,
                        null,
                        "3h 15m",
                        false,
                        null),
                    new(
                        "fm-4",
                        "Full Mock Test 4",
                        "available",
                        null,
                        null,
                        "3h 15m",
                        false,
                        null),
                    new(
                        "fm-5",
                        "Full Mock Test 5",
                        "locked",
                        null,
                        null,
                        null,
                        false,
                        "Complete Mock 4 first")
                },
                subTestMocks = new[]
                {
                    new { id = "read-mock", title = "Reading Mocks", subtest = "reading", route = "/mocks/setup?subtest=reading" },
                    new { id = "list-mock", title = "Listening Mocks", subtest = "listening", route = "/mocks/setup?subtest=listening" },
                    new { id = "speak-mock", title = "Speaking Mocks", subtest = "speaking", route = "/mocks/setup?subtest=speaking" },
                    new { id = "write-mock", title = "Writing Mocks", subtest = "writing", route = "/mocks/setup?subtest=writing" }
                }
            }
        };
    }

    public Task<object> GetMockOptionsAsync(CancellationToken cancellationToken)
        => Task.FromResult<object>(new
        {
            mockTypes = new[]
            {
                new { id = "full", label = "Full Mock", description = "All four sub-tests in sequence." },
                new { id = "sub", label = "Single Sub-test", description = "Focus on one specific skill area." }
            },
            subTypes = new[]
            {
                new { id = "reading", label = "Reading" },
                new { id = "listening", label = "Listening" },
                new { id = "writing", label = "Writing" },
                new { id = "speaking", label = "Speaking" }
            },
            modes = new[]
            {
                new { id = "practice", label = "Practice" },
                new { id = "exam", label = "Exam" }
            },
            professions = new[]
            {
                new { id = "medicine", label = "Medicine" },
                new { id = "nursing", label = "Nursing" },
                new { id = "pharmacy", label = "Pharmacy" },
                new { id = "dentistry", label = "Dentistry" },
                new { id = "dietetics", label = "Dietetics" },
                new { id = "optometry", label = "Optometry" },
                new { id = "physiotherapy", label = "Physiotherapy" },
                new { id = "podiatry", label = "Podiatry" },
                new { id = "radiography", label = "Radiography" },
                new { id = "speech_pathology", label = "Speech Pathology" },
                new { id = "veterinary_science", label = "Veterinary Science" }
            },
            reviewSelections = new[]
            {
                new { id = "none", label = "No Review" },
                new { id = "writing", label = "Writing Only" },
                new { id = "speaking", label = "Speaking Only" },
                new { id = "writing_and_speaking", label = "Writing + Speaking" },
                new { id = "current_subtest", label = "Current Sub-test" }
            }
        });

    private sealed record MockHistoryCard(
        string Id,
        string Title,
        string Status,
        string? Score,
        string? Date,
        string? Duration,
        bool IsRecommended,
        string? Reason);

    #if false
    public object GetExtras() => new
    {
        items = new[]
        {
            new { id = "credits-3", productType = "review_credits", quantity = 3, price = 29.99m, currency = "AUD", description = "Pack of 3 tutor review credits." },
            new { id = "credits-5", productType = "review_credits", quantity = 5, price = 44.99m, currency = "AUD", description = "Pack of 5 tutor review credits." }
        }
    };

                quoteResponse = await BuildBillingQuoteAsync(userId, new BillingQuoteRequest(
                    normalizedProductType,
                    request.Quantity,
                    request.PriceId,
                    request.CouponCode,
                    request.AddOnCodes), cancellationToken, persistQuote: true);
                quoteEntity = await db.BillingQuotes.FirstAsync(x => x.Id == quoteResponse.Id && x.UserId == userId, cancellationToken);
                return cached;
            }
        }

        var normalizedProductType = (request.ProductType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedProductType is not ("review_credits" or "plan_upgrade" or "plan_downgrade"))
        {
            throw ApiException.Validation(
                "unsupported_checkout_product",
                $"Unsupported checkout product '{request.ProductType}'.",
                [new ApiFieldError("productType", "unsupported", "Only supported learner checkout products can be purchased.")]);
        }

        if (request.Quantity <= 0)
        {
            throw ApiException.Validation(
                "invalid_checkout_quantity",
                "Checkout quantity must be greater than zero.",
                [new ApiFieldError("quantity", "invalid", "Choose a checkout quantity greater than zero.")]);
        }

        if (normalizedProductType is "plan_upgrade" or "plan_downgrade" && string.IsNullOrWhiteSpace(request.PriceId))
        {
            throw ApiException.Validation(
                "target_plan_required",
                "A target plan id is required for plan changes.",
                [new ApiFieldError("priceId", "required", "Choose the plan you want to switch to.")]);
        }

        var checkoutSessionId = $"checkout-{Guid.NewGuid():N}";
        object response = new
        {
            checkoutSessionId,
            productType = normalizedProductType,
            quantity = request.Quantity,
            targetPlanId = request.PriceId,
            checkoutUrl = platformLinks.BuildCheckoutUrl(checkoutSessionId, normalizedProductType, request.Quantity),
            state = "created"
        };
        await RecordEventAsync(userId, "subscription_changed", new { productType = normalizedProductType, quantity = request.Quantity, targetPlanId = request.PriceId }, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            await SaveIdempotentResponseAsync("checkout-session", request.IdempotencyKey, response, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        return response;
    }
    #endif

    private async Task<LearnerUser> EnsureUserAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
        {
            throw ApiException.Forbidden("learner_profile_not_found", "Learner profile not found.");
        }

        if (!string.Equals(user.AccountStatus, "active", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Forbidden("account_suspended", "This learner account is not available.");
        }

        return user;
    }

    private async Task<LearnerProfileState> EnsureLearnerProfileStateAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var loaded = await (
                from user in db.Users
                where user.Id == userId
                join registration in db.LearnerRegistrationProfiles
                    on user.Id equals registration.LearnerUserId into registrations
                from registration in registrations.DefaultIfEmpty()
                join loadedGoal in db.Goals
                    on user.Id equals loadedGoal.UserId into goals
                from loadedGoal in goals.DefaultIfEmpty()
                join loadedSettings in db.Settings
                    on user.Id equals loadedSettings.UserId into settingsRows
                from loadedSettings in settingsRows.DefaultIfEmpty()
                join loadedWallet in db.Wallets
                    on user.Id equals loadedWallet.UserId into wallets
                from loadedWallet in wallets.DefaultIfEmpty()
                select new
                {
                    User = user,
                    RegisteredTargetCountry = registration == null ? null : registration.CountryTarget,
                    Goal = loadedGoal,
                    Settings = loadedSettings,
                    Wallet = loadedWallet
                })
            .FirstOrDefaultAsync(cancellationToken);

        if (loaded is null)
        {
            throw ApiException.Forbidden("learner_profile_not_found", "Learner profile not found.");
        }

        if (!string.Equals(loaded.User.AccountStatus, "active", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Forbidden("account_suspended", "This learner account is not available.");
        }

        var now = DateTimeOffset.UtcNow;
        var registeredTargetCountry = TargetCountryOptions.TryCanonicalize(
            loaded.RegisteredTargetCountry,
            out var canonicalRegisteredTargetCountry)
            ? canonicalRegisteredTargetCountry
            : "Australia";
        var changed = false;
        var goal = loaded.Goal;
        if (goal is null)
        {
            goal = CreateDefaultGoal(
                loaded.User.Id,
                loaded.User.ActiveProfessionId,
                registeredTargetCountry,
                now);
            db.Goals.Add(goal);
            changed = true;
        }
        else if (ShouldRestoreRegisteredTargetCountry(goal, registeredTargetCountry))
        {
            goal.TargetCountry = registeredTargetCountry;
            goal.UpdatedAt = now;
            changed = true;
        }
        else if (TargetCountryOptions.TryCanonicalize(goal.TargetCountry, out var canonicalGoalTargetCountry)
                 && !string.Equals(goal.TargetCountry, canonicalGoalTargetCountry, StringComparison.Ordinal))
        {
            goal.TargetCountry = canonicalGoalTargetCountry;
            goal.UpdatedAt = now;
            changed = true;
        }

        var settings = loaded.Settings;
        if (settings is null)
        {
            settings = CreateDefaultSettings(loaded.User, goal);
            db.Settings.Add(settings);
            changed = true;
        }

        var wallet = loaded.Wallet;
        if (wallet is null)
        {
            wallet = CreateDefaultWallet(loaded.User.Id, now);
            db.Wallets.Add(wallet);
            changed = true;
        }

        if (changed)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                foreach (var entry in db.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Added).ToList())
                {
                    entry.State = EntityState.Detached;
                }

                var persistedGoal = await db.Goals.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
                var persistedSettings = await db.Settings.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
                var persistedWallet = await db.Wallets.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
                if (persistedGoal is null || persistedSettings is null || persistedWallet is null)
                {
                    throw;
                }

                goal = persistedGoal;
                settings = persistedSettings;
                wallet = persistedWallet;
            }
        }

        return new LearnerProfileState(loaded.User, goal, settings, wallet);
    }

    private async Task<object> GetFreezeStatusForLoadedUserAsync(
        LearnerUser user,
        CancellationToken cancellationToken)
    {
        var state = await (
                from owner in db.Users.AsNoTracking().Where(x => x.Id == user.Id)
                from loadedPolicy in db.AccountFreezePolicies
                    .AsNoTracking()
                    .OrderByDescending(x => x.Version)
                    .Take(1)
                    .DefaultIfEmpty()
                from currentFreeze in db.AccountFreezeRecords
                    .AsNoTracking()
                    .Where(x => x.UserId == owner.Id && x.IsCurrent)
                    .Take(1)
                    .DefaultIfEmpty()
                from entitlement in db.AccountFreezeEntitlements
                    .AsNoTracking()
                    .Where(x => x.UserId == owner.Id)
                    .Take(1)
                    .DefaultIfEmpty()
                from subscription in db.Subscriptions
                    .AsNoTracking()
                    .Where(x => x.UserId == owner.Id)
                    .Take(1)
                    .DefaultIfEmpty()
                select new
                {
                    Policy = loadedPolicy,
                    CurrentFreeze = currentFreeze,
                    Entitlement = entitlement,
                    Subscription = subscription
                })
            .SingleAsync(cancellationToken);
        var policy = state.Policy ?? CreateDefaultFreezeReadPolicy();
        var currentPlan = state.Subscription is null
            ? null
            : await db.BillingPlans
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    plan => plan.Code == state.Subscription.PlanId,
                    cancellationToken);
        var eligibility = BuildFreezeEligibilityFromLoadedState(
            policy,
            state.CurrentFreeze,
            state.Entitlement,
            state.Subscription,
            currentPlan);
        var history = await db.AccountFreezeRecords
            .AsNoTracking()
            .Where(record => record.UserId == user.Id)
            .OrderByDescending(record => record.RequestedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        return new
        {
            userId = user.Id,
            policy = MapFreezePolicy(policy),
            currentFreeze = state.CurrentFreeze is null ? null : MapFreezeRecord(state.CurrentFreeze),
            entitlement = state.Entitlement is null ? null : new
            {
                state.Entitlement.Id,
                state.Entitlement.UserId,
                state.Entitlement.FreezeRecordId,
                state.Entitlement.ConsumedAt,
                state.Entitlement.ResetAt,
                state.Entitlement.ResetByAdminId,
                state.Entitlement.ResetByAdminName,
                state.Entitlement.ResetReason,
                used = state.Entitlement.ConsumedAt is not null && state.Entitlement.ResetAt is null
            },
            eligibility,
            history = history.Select(MapFreezeRecord).ToList()
        };
    }

    private static AccountFreezePolicy CreateDefaultFreezeReadPolicy() => new()
    {
        Id = "global",
        IsEnabled = true,
        SelfServiceEnabled = true,
        ApprovalMode = FreezeApprovalMode.AutoApprove,
        MinDurationDays = 1,
        MaxDurationDays = 365,
        AllowScheduling = true,
        AccessMode = FreezeAccessMode.ReadOnly,
        EntitlementPauseMode = FreezeEntitlementPauseMode.InternalClock,
        RequireReason = true,
        RequireInternalNotes = false,
        AllowActivePaid = true,
        AllowGracePeriod = true,
        AllowTrial = false,
        AllowComplimentary = false,
        AllowCancelled = false,
        AllowExpired = false,
        AllowReviewOnly = false,
        AllowPastDue = true,
        AllowSuspended = false,
        PolicyNotes = "Default freeze policy",
        EligibilityReasonCodesJson = "[]",
        UpdatedAt = DateTimeOffset.UtcNow,
        Version = 1
    };

    private static FreezeEligibilityResult BuildFreezeEligibilityFromLoadedState(
        AccountFreezePolicy policy,
        AccountFreezeRecord? currentFreeze,
        AccountFreezeEntitlement? entitlement,
        Subscription? subscription,
        BillingPlan? currentPlan)
    {
        if (subscription is null)
        {
            return new FreezeEligibilityResult(
                false,
                false,
                false,
                policy.MaxDurationDays,
                policy.MinDurationDays,
                "unknown",
                ["subscription_missing"],
                policy.Version);
        }

        var reasonCodes = new List<string>();
        if (!policy.IsEnabled) reasonCodes.Add("policy_disabled");
        if (!policy.SelfServiceEnabled) reasonCodes.Add("self_service_disabled");
        if (currentFreeze is not null
            && currentFreeze.Status is FreezeStatus.Active or FreezeStatus.PendingApproval or FreezeStatus.Scheduled)
        {
            reasonCodes.Add("current_freeze_exists");
        }
        if (entitlement is not null && entitlement.ConsumedAt is not null && entitlement.ResetAt is null)
        {
            reasonCodes.Add("self_service_entitlement_used");
        }

        if (subscription.Status == SubscriptionStatus.Active && !policy.AllowActivePaid)
        {
            reasonCodes.Add("active_paid_excluded");
        }
        else if (subscription.Status == SubscriptionStatus.PastDue && !policy.AllowPastDue && !policy.AllowGracePeriod)
        {
            reasonCodes.Add("past_due_excluded");
        }
        else if (subscription.Status == SubscriptionStatus.Trial && !policy.AllowTrial)
        {
            reasonCodes.Add("trial_excluded");
        }
        else if (subscription.Status == SubscriptionStatus.Cancelled && !policy.AllowCancelled)
        {
            reasonCodes.Add("cancelled_excluded");
        }
        else if (subscription.Status == SubscriptionStatus.Expired && !policy.AllowExpired)
        {
            reasonCodes.Add("expired_excluded");
        }
        else if (subscription.Status == SubscriptionStatus.Suspended && !policy.AllowSuspended)
        {
            reasonCodes.Add("suspended_excluded");
        }

        if (!string.IsNullOrWhiteSpace(currentPlan?.Code)
            && currentPlan.Code.Contains("complimentary", StringComparison.OrdinalIgnoreCase)
            && !policy.AllowComplimentary)
        {
            reasonCodes.Add("complimentary_excluded");
        }

        var eligible = reasonCodes.Count == 0;
        return new FreezeEligibilityResult(
            eligible,
            eligible,
            policy.AllowScheduling,
            policy.MaxDurationDays,
            policy.MinDurationDays,
            ToSubscriptionState(subscription.Status),
            reasonCodes,
            policy.Version);
    }

    private async Task<LearnerUser> EnsureLearnerProfileAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await EnsureUserAsync(userId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var changed = false;
        var registeredTargetCountry = await ResolveRegisteredTargetCountryAsync(userId, cancellationToken);

        var goal = await db.Goals.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (goal is null)
        {
            goal = CreateDefaultGoal(userId, user.ActiveProfessionId, registeredTargetCountry, now);
            db.Goals.Add(goal);
            changed = true;
        }
        else if (ShouldRestoreRegisteredTargetCountry(goal, registeredTargetCountry))
        {
            goal.TargetCountry = registeredTargetCountry;
            goal.UpdatedAt = now;
            changed = true;
        }
        else if (TargetCountryOptions.TryCanonicalize(goal.TargetCountry, out var canonicalGoalTargetCountry)
            && !string.Equals(goal.TargetCountry, canonicalGoalTargetCountry, StringComparison.Ordinal))
        {
            goal.TargetCountry = canonicalGoalTargetCountry;
            goal.UpdatedAt = now;
            changed = true;
        }

        var settings = await db.Settings.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (settings is null)
        {
            settings = CreateDefaultSettings(user, goal);
            db.Settings.Add(settings);
            changed = true;
        }

        var wallet = await db.Wallets.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (wallet is null)
        {
            wallet = CreateDefaultWallet(userId, now);
            db.Wallets.Add(wallet);
            changed = true;
        }

        if (changed)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Concurrent first-visit requests race to create the same profile rows
                // (Wallets.UserId is unique). The loser drops its pending inserts — the
                // winner's rows already exist and every caller re-reads per request.
                foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                {
                    entry.State = EntityState.Detached;
                }
            }
        }

        return user;
    }

    private async Task<string> ResolveRegisteredTargetCountryAsync(string userId, CancellationToken cancellationToken)
    {
        var registeredTargetCountry = await db.LearnerRegistrationProfiles
            .AsNoTracking()
            .Where(x => x.LearnerUserId == userId)
            .Select(x => x.CountryTarget)
            .SingleOrDefaultAsync(cancellationToken);

        return TargetCountryOptions.TryCanonicalize(registeredTargetCountry, out var canonical)
            ? canonical
            : "Australia";
    }

    private static bool ShouldRestoreRegisteredTargetCountry(LearnerGoal goal, string registeredTargetCountry)
    {
        if (string.IsNullOrWhiteSpace(goal.TargetCountry)) return true;
        if (goal.SubmittedAt is not null) return false;
        if (string.Equals(registeredTargetCountry, "Australia", StringComparison.Ordinal)) return false;
        return string.Equals(goal.TargetCountry, "Australia", StringComparison.OrdinalIgnoreCase);
    }

    private static object GoalDto(LearnerGoal goal) => new
    {
        goalId = goal.Id,
        userId = goal.UserId,
        examFamilyCode = goal.ExamFamilyCode,
        professionId = goal.ProfessionId,
        targetExamDate = goal.TargetExamDate,
        overallGoal = goal.OverallGoal,
        targetScoresBySubtest = new
        {
            writing = goal.TargetWritingScore,
            speaking = goal.TargetSpeakingScore,
            reading = goal.TargetReadingScore,
            listening = goal.TargetListeningScore
        },
        previousAttemptSummary = goal.PreviousAttempts,
        weakSubtestSelfReport = JsonSupport.Deserialize<List<string>>(goal.WeakSubtestsJson, []),
        studyHoursPerWeek = goal.StudyHoursPerWeek,
        targetCountry = goal.TargetCountry,
        targetOrganization = goal.TargetOrganization,
        targetExamMode = goal.TargetExamMode,
        confidenceLevel = goal.ConfidenceLevel,
        draftState = JsonSupport.Deserialize<Dictionary<string, object?>>(goal.DraftStateJson, new Dictionary<string, object?>()),
        submittedAt = goal.SubmittedAt,
        updatedAt = goal.UpdatedAt
    };

    private static object GoalSettingsDto(LearnerGoal goal) => new
    {
        examFamilyCode = goal.ExamFamilyCode,
        professionId = goal.ProfessionId,
        targetExamDate = goal.TargetExamDate,
        overallGoal = goal.OverallGoal,
        targetScoresBySubtest = new
        {
            writing = goal.TargetWritingScore,
            speaking = goal.TargetSpeakingScore,
            reading = goal.TargetReadingScore,
            listening = goal.TargetListeningScore
        },
        previousAttempts = goal.PreviousAttempts,
        weakSubtests = JsonSupport.Deserialize<List<string>>(goal.WeakSubtestsJson, []),
        studyHoursPerWeek = goal.StudyHoursPerWeek,
        targetCountry = goal.TargetCountry,
        targetOrganization = goal.TargetOrganization,
        targetExamMode = goal.TargetExamMode,
        confidenceLevel = goal.ConfidenceLevel,
        draftState = JsonSupport.Deserialize<Dictionary<string, object?>>(goal.DraftStateJson, new Dictionary<string, object?>()),
        submittedAt = goal.SubmittedAt,
        updatedAt = goal.UpdatedAt
    };

    private async Task<UploadSession> GetUploadSessionOwnedByUserAsync(string userId, string uploadSessionId, CancellationToken cancellationToken)
    {
        var upload = await db.UploadSessions.FirstOrDefaultAsync(x => x.Id == uploadSessionId, cancellationToken)
            ?? throw ApiException.NotFound("upload_session_not_found", "The requested upload session was not found.");

        await GetAttemptOwnedByUserAsync(userId, upload.AttemptId, cancellationToken);
        return upload;
    }

    private async Task<UploadSession> GetUploadSessionForCompletionAsync(
        string userId,
        string attemptId,
        UploadCompleteRequest request,
        CancellationToken cancellationToken)
    {
        UploadSession? upload = null;
        if (!string.IsNullOrWhiteSpace(request.UploadSessionId))
        {
            upload = await GetUploadSessionOwnedByUserAsync(userId, request.UploadSessionId, cancellationToken);
            if (!string.Equals(upload.AttemptId, attemptId, StringComparison.Ordinal))
            {
                throw ApiException.Validation(
                    "upload_attempt_mismatch",
                    "The upload session does not belong to this speaking attempt.",
                    [new ApiFieldError("uploadSessionId", "mismatch", "Use the upload session created for this speaking attempt.")]);
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.StorageKey))
        {
            var uploadSessions = await db.UploadSessions
                .Where(x => x.AttemptId == attemptId && x.StorageKey == request.StorageKey)
                .ToListAsync(cancellationToken);
            upload = uploadSessions
                .OrderByDescending(x => x.ExpiresAt)
                .FirstOrDefault();
        }
        else
        {
            var uploadSessions = await db.UploadSessions
                .Where(x => x.AttemptId == attemptId)
                .ToListAsync(cancellationToken);
            upload = uploadSessions
                .OrderByDescending(x => x.ExpiresAt)
                .FirstOrDefault();
        }

        if (upload is null)
        {
            throw ApiException.Validation(
                "upload_session_required",
                "Create and use an upload session before completing the speaking upload.",
                [new ApiFieldError("uploadSessionId", "required", "Create a speaking upload session first.")]);
        }

        await GetAttemptOwnedByUserAsync(userId, upload.AttemptId, cancellationToken);
        return upload;
    }

    private static object SettingsDto(LearnerSettings settings, LearnerGoal goal)
    {
        var study = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.StudyJson, new Dictionary<string, object?>());
        study["targetExamDate"] = goal.TargetExamDate;
        study["studyHoursPerWeek"] = goal.StudyHoursPerWeek;
        study["targetCountry"] = goal.TargetCountry;
        study["professionId"] = goal.ProfessionId;
        study["examFamilyCode"] = goal.ExamFamilyCode;

        return new
        {
            profile = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.ProfileJson, new Dictionary<string, object?>()),
            goals = GoalSettingsDto(goal),
            notifications = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.NotificationsJson, new Dictionary<string, object?>()),
            privacy = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.PrivacyJson, new Dictionary<string, object?>()),
            accessibility = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.AccessibilityJson, new Dictionary<string, object?>()),
            audio = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.AudioJson, new Dictionary<string, object?>()),
            study
        };
    }

    private static LearnerGoal CreateDefaultGoal(string userId, string? professionId, string targetCountry, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProfessionId = string.IsNullOrWhiteSpace(professionId) ? "nursing" : professionId,
            TargetExamDate = DateOnly.FromDateTime(now.UtcDateTime.AddMonths(3)),
            OverallGoal = "Build a strong OET foundation and stay ready for exam day.",
            TargetWritingScore = 350,
            TargetSpeakingScore = 350,
            TargetReadingScore = 350,
            TargetListeningScore = 350,
            PreviousAttempts = 0,
            WeakSubtestsJson = JsonSupport.Serialize(new[] { "writing", "speaking" }),
            StudyHoursPerWeek = 10,
            TargetCountry = targetCountry,
            TargetOrganization = "AHPRA",
            DraftStateJson = JsonSupport.Serialize(new Dictionary<string, object?>()),
            UpdatedAt = now,
            ExamFamilyCode = "oet"
        };

    private static LearnerSettings CreateDefaultSettings(LearnerUser user, LearnerGoal goal)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ProfileJson = JsonSupport.Serialize(new Dictionary<string, object?>
            {
                ["displayName"] = user.DisplayName,
                ["email"] = user.Email,
                ["profession"] = goal.ProfessionId,
                ["timezone"] = user.Timezone,
                ["locale"] = user.Locale
            }),
            NotificationsJson = JsonSupport.Serialize(new Dictionary<string, object?>
            {
                ["emailReminders"] = true,
                ["reviewUpdates"] = true,
                ["billingAlerts"] = true
            }),
            PrivacyJson = JsonSupport.Serialize(new Dictionary<string, object?>
            {
                ["audioConsentAccepted"] = true,
                ["analyticsOptIn"] = true
            }),
            AccessibilityJson = JsonSupport.Serialize(new Dictionary<string, object?>
            {
                ["reducedMotion"] = false,
                ["highContrast"] = false,
                ["fontScale"] = 1.0
            }),
            AudioJson = JsonSupport.Serialize(new Dictionary<string, object?>
            {
                ["playbackSpeed"] = 1.0,
                ["autoAdvance"] = true
            }),
            StudyJson = JsonSupport.Serialize(new Dictionary<string, object?>
            {
                ["dailyGoalMinutes"] = 45,
                ["studyHoursPerWeek"] = goal.StudyHoursPerWeek,
                ["targetCountry"] = goal.TargetCountry
            })
        };

    private static Wallet CreateDefaultWallet(string userId, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            CreditBalance = 0,
            LastUpdatedAt = now,
            LedgerSummaryJson = JsonSupport.Serialize(Array.Empty<object>())
        };

    private static ReadinessSnapshot CreateDefaultReadinessSnapshot(string userId, LearnerGoal goal, DateTimeOffset now)
    {
        var targetDate = goal.TargetExamDate ?? DateOnly.FromDateTime(now.UtcDateTime.AddMonths(3));

        return new ReadinessSnapshot
        {
            Id = $"rs-{Guid.NewGuid():N}",
            UserId = userId,
            ComputedAt = now,
            Version = 1,
            PayloadJson = JsonSupport.Serialize(new
            {
                targetDate = targetDate.ToString("yyyy-MM-dd"),
                weeksRemaining = Math.Max(0, (int)Math.Ceiling((targetDate.ToDateTime(TimeOnly.MinValue) - now.UtcDateTime.Date).TotalDays / 7.0)),
                overallRisk = "unknown",
                recommendedStudyHours = goal.StudyHoursPerWeek,
                weakestLink = "No readiness evidence yet",
                subTests = Array.Empty<object>(),
                blockers = Array.Empty<object>(),
                evidence = new
                {
                    source = "no_evidence",
                    mocksCompleted = 0,
                    practiceQuestions = 0,
                    expertReviews = 0,
                    recentTrend = "Complete practice, mocks, or tutor reviews to unlock live readiness analytics.",
                    lastUpdated = now
                }
            })
        };
    }

    private static StudyPlan CreateDefaultStudyPlan(string userId, LearnerGoal goal, DateTimeOffset now)
        => new()
        {
            Id = $"plan-{Guid.NewGuid():N}",
            UserId = userId,
            Version = 1,
            GeneratedAt = now,
            State = AsyncState.Completed,
            Checkpoint = "Awaiting live study-plan evidence",
            WeakSkillFocus = "Awaiting learner evidence",
            ExamFamilyCode = goal.ExamFamilyCode
        };

    private static IEnumerable<StudyPlanItem> CreateDefaultStudyPlanItems(string planId)
        => Array.Empty<StudyPlanItem>();

    private static void ApplyGoalSettingsPatch(LearnerGoal goal, Dictionary<string, object?> values)
    {
        if (values.TryGetValue("examFamilyCode", out var examFamilyCode))
        {
            goal.ExamFamilyCode = NormalizeExamFamilyCode(ReadString(examFamilyCode) ?? goal.ExamFamilyCode);
        }

        if (values.TryGetValue("professionId", out var professionId))
        {
            goal.ProfessionId = ReadString(professionId) ?? goal.ProfessionId;
        }

        if (values.TryGetValue("targetExamDate", out var targetExamDate))
        {
            goal.TargetExamDate = ReadDateOnly(targetExamDate) ?? goal.TargetExamDate;
        }

        if (values.TryGetValue("overallGoal", out var overallGoal))
        {
            goal.OverallGoal = ReadString(overallGoal) ?? goal.OverallGoal;
        }

        if (values.TryGetValue("targetScoresBySubtest", out var targetScores))
        {
            var scores = ReadObject(targetScores);
            if (scores is not null)
            {
                goal.TargetWritingScore = ReadInt(scores.GetValueOrDefault("writing")) ?? goal.TargetWritingScore;
                goal.TargetSpeakingScore = ReadInt(scores.GetValueOrDefault("speaking")) ?? goal.TargetSpeakingScore;
                goal.TargetReadingScore = ReadInt(scores.GetValueOrDefault("reading")) ?? goal.TargetReadingScore;
                goal.TargetListeningScore = ReadInt(scores.GetValueOrDefault("listening")) ?? goal.TargetListeningScore;
            }
        }

        if (values.TryGetValue("previousAttempts", out var previousAttempts))
        {
            goal.PreviousAttempts = ReadInt(previousAttempts) ?? goal.PreviousAttempts;
        }

        if (values.TryGetValue("previousAttemptSummary", out var previousAttemptSummary))
        {
            goal.PreviousAttempts = ReadInt(previousAttemptSummary) ?? goal.PreviousAttempts;
        }

        if (values.TryGetValue("weakSubtests", out var weakSubtests))
        {
            var parsed = ReadStringList(weakSubtests);
            if (parsed is not null)
            {
                goal.WeakSubtestsJson = JsonSupport.Serialize(parsed);
            }
        }

        if (values.TryGetValue("weakSubtestSelfReport", out var weakSubtestSelfReport))
        {
            var parsed = ReadStringList(weakSubtestSelfReport);
            if (parsed is not null)
            {
                goal.WeakSubtestsJson = JsonSupport.Serialize(parsed);
            }
        }

        if (values.TryGetValue("studyHoursPerWeek", out var studyHoursPerWeek))
        {
            goal.StudyHoursPerWeek = ReadInt(studyHoursPerWeek) ?? goal.StudyHoursPerWeek;
        }

        if (values.TryGetValue("targetCountry", out var targetCountry))
        {
            goal.TargetCountry = TargetCountryOptions.Canonicalize(ReadString(targetCountry));
        }

        if (values.TryGetValue("targetOrganization", out var targetOrganization))
        {
            goal.TargetOrganization = ReadString(targetOrganization) ?? goal.TargetOrganization;
        }

        if (values.TryGetValue("draftState", out var draftState))
        {
            var draftStateObject = ReadObject(draftState);
            if (draftStateObject is not null)
            {
                goal.DraftStateJson = JsonSupport.Serialize(draftStateObject);
            }
        }
    }

    private async Task<Attempt> GetAttemptOwnedByUserAsync(string userId, string attemptId, CancellationToken cancellationToken)
        => await db.Attempts.FirstOrDefaultAsync(x => x.Id == attemptId && x.UserId == userId, cancellationToken)
           ?? throw ApiException.NotFound("attempt_not_found", "Attempt not found.");

    private async Task<Attempt> GetWritingAttemptOwnedByUserAsync(string userId, string attemptId, CancellationToken cancellationToken)
        => await GetSubtestAttemptOwnedByUserAsync(userId, attemptId, "writing", "writing_attempt_not_found", "Writing attempt not found.", cancellationToken);

    private async Task<Attempt> GetSubtestAttemptOwnedByUserAsync(
        string userId,
        string attemptId,
        string subtest,
        string notFoundCode,
        string notFoundMessage,
        CancellationToken cancellationToken)
    {
        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        if (!string.Equals(attempt.SubtestCode, subtest, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.NotFound(notFoundCode, notFoundMessage);
        }

        return attempt;
    }

    private async Task<Attempt> GetSpeakingAttemptOwnedByUserAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        if (!string.Equals(attempt.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.NotFound("speaking_attempt_not_found", "Speaking attempt not found.");
        }

        return attempt;
    }

    private static void EnsureSpeakingAttemptCanReceiveAudio(Attempt attempt)
    {
        if (attempt.State is AttemptState.Submitted or AttemptState.Evaluating or AttemptState.Completed)
        {
            throw ApiException.Conflict(
                "speaking_attempt_locked",
                "This speaking attempt has already been submitted and cannot receive another recording.",
                [new ApiFieldError("attemptId", "locked", "Start a new speaking attempt before uploading another recording.")]);
        }
    }

    private async Task EnsureSpeakingAudioReadyForSubmissionAsync(
        Attempt attempt,
        CancellationToken cancellationToken)
    {
        if (attempt.AudioUploadState != UploadState.Uploaded || string.IsNullOrWhiteSpace(attempt.AudioObjectKey))
        {
            throw ApiException.Validation(
                "speaking_audio_required",
                "Upload audio before submitting this speaking attempt.",
                [new ApiFieldError("audio", "required", "Complete the audio upload before submission.")]);
        }

        if (!await fileStorage.ExistsAsync(attempt.AudioObjectKey, cancellationToken)
            || await fileStorage.LengthAsync(attempt.AudioObjectKey, cancellationToken) <= 0)
        {
            throw ApiException.Validation(
                "speaking_audio_not_ready",
                "The speaking audio file is not ready for submission yet.",
                [new ApiFieldError("audio", "not_ready", "Upload the recording bytes again before submitting.")]);
        }
    }

    private async Task EnsureSpeakingAttemptBindingAsync(
        Attempt attempt,
        string? expectedContentId,
        string? expectedMockSessionId,
        CancellationToken cancellationToken)
    {
        var normalizedContentId = expectedContentId?.Trim();
        var normalizedMockSessionId = expectedMockSessionId?.Trim();

        if (!string.IsNullOrWhiteSpace(normalizedContentId)
            && !string.Equals(attempt.ContentId, normalizedContentId, StringComparison.Ordinal))
        {
            throw ApiException.Validation(
                "speaking_attempt_content_mismatch",
                "This speaking attempt does not belong to the requested task.",
                [new ApiFieldError("contentId", "mismatch", "Use the attempt created for this speaking task.")]);
        }

        if (!string.Equals(attempt.Context, "mock_set", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(normalizedMockSessionId))
            {
                throw ApiException.Validation(
                    "speaking_attempt_mock_session_mismatch",
                    "This speaking attempt does not belong to the requested mock session.",
                    [new ApiFieldError("mockSessionId", "mismatch", "Use the paired attempt created for this speaking mock session.")]);
            }

            return;
        }

        var sessionId = string.IsNullOrWhiteSpace(normalizedMockSessionId)
            ? attempt.ComparisonGroupId
            : normalizedMockSessionId;
        if (string.IsNullOrWhiteSpace(sessionId)
            || !string.Equals(attempt.ComparisonGroupId, sessionId, StringComparison.Ordinal))
        {
            throw ApiException.Validation(
                "speaking_attempt_mock_session_mismatch",
                "This speaking attempt does not belong to the requested mock session.",
                [new ApiFieldError("mockSessionId", "mismatch", "Use the paired attempt created for this speaking mock session.")]);
        }

        var session = await db.SpeakingMockSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == attempt.UserId, cancellationToken);
        if (session is null || (attempt.Id != session.Attempt1Id && attempt.Id != session.Attempt2Id))
        {
            throw ApiException.Validation(
                "speaking_attempt_mock_session_mismatch",
                "This speaking attempt does not belong to the requested mock session.",
                [new ApiFieldError("mockSessionId", "mismatch", "Use the paired attempt created for this speaking mock session.")]);
        }

    }

    private async Task<Evaluation> GetEvaluationOwnedByUserAsync(string userId, string evaluationId, CancellationToken cancellationToken)
    {
        var evaluation = await db.Evaluations.FirstOrDefaultAsync(x => x.Id == evaluationId, cancellationToken)
                         ?? throw ApiException.NotFound("evaluation_not_found", "Evaluation not found.");
        await GetAttemptOwnedByUserAsync(userId, evaluation.AttemptId, cancellationToken);
        return evaluation;
    }

    private async Task<StudyPlanItem> GetStudyPlanItemOwnedByUserAsync(string userId, string itemId, CancellationToken cancellationToken)
    {
        var item = await db.StudyPlanItems.FirstOrDefaultAsync(x => x.Id == itemId, cancellationToken)
                   ?? throw ApiException.NotFound("study_plan_item_not_found", "Study plan item not found.");
        var plan = await db.StudyPlans.FirstOrDefaultAsync(x => x.Id == item.StudyPlanId && x.UserId == userId, cancellationToken);
        if (plan is null)
        {
            throw ApiException.NotFound("study_plan_item_not_found", "Study plan item not found.");
        }

        return item;
    }

    private async Task<MockAttempt> GetMockAttemptOwnedByUserAsync(string userId, string mockAttemptId, CancellationToken cancellationToken)
        => await db.MockAttempts.FirstOrDefaultAsync(x => x.Id == mockAttemptId && x.UserId == userId, cancellationToken)
           ?? throw ApiException.NotFound("mock_attempt_not_found", "Mock attempt not found.");

    private async Task<MockReport> GetMockReportOwnedByUserAsync(string userId, string reportId, CancellationToken cancellationToken)
    {
        var report = await db.MockReports.FirstOrDefaultAsync(x => x.Id == reportId, cancellationToken)
                     ?? throw ApiException.NotFound("mock_report_not_found", "Mock report not found.");
        var mockAttempt = await db.MockAttempts.FirstOrDefaultAsync(x => x.Id == report.MockAttemptId && x.UserId == userId, cancellationToken);
        if (mockAttempt is null)
        {
            throw ApiException.NotFound("mock_report_not_found", "Mock report not found.");
        }

        return report;
    }

    private async Task<ReviewRequest> GetReviewRequestOwnedByUserAsync(string userId, string reviewRequestId, CancellationToken cancellationToken)
    {
        var review = await db.ReviewRequests.FirstOrDefaultAsync(x => x.Id == reviewRequestId, cancellationToken)
                     ?? throw ApiException.NotFound("review_request_not_found", "Review request not found.");
        await GetAttemptOwnedByUserAsync(userId, review.AttemptId, cancellationToken);
        return review;
    }

    private static string? ReadString(object? value) => value switch
    {
        null => null,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        JsonElement { ValueKind: JsonValueKind.Null } => null,
        _ => value.ToString()
    };

    private static int? ReadInt(object? value) => value switch
    {
        null => null,
        int number => number,
        long number => (int)number,
        JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var parsed) => parsed,
        JsonElement { ValueKind: JsonValueKind.String } element when int.TryParse(element.GetString(), out var parsed) => parsed,
        _ when int.TryParse(value.ToString(), out var parsed) => parsed,
        _ => null
    };

    private static DateOnly? ReadDateOnly(object? value)
    {
        var text = ReadString(value);
        return DateOnly.TryParse(text, out var parsed) ? parsed : null;
    }

    private static List<string>? ReadStringList(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonElement { ValueKind: JsonValueKind.Array } element)
        {
            return element.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToList();
        }

        if (value is IEnumerable<string> strings)
        {
            return strings.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
        }

        if (value is IEnumerable<object?> objects)
        {
            return objects.Select(ReadString).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToList();
        }

        return null;
    }

    private static Dictionary<string, object?>? ReadObject(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is Dictionary<string, object?> dictionary)
        {
            return dictionary;
        }

        if (value is JsonElement { ValueKind: JsonValueKind.Object } element)
        {
            return JsonSupport.Deserialize<Dictionary<string, object?>>(element.GetRawText(), new Dictionary<string, object?>());
        }

        return JsonSupport.Deserialize<Dictionary<string, object?>>(JsonSupport.Serialize(value), new Dictionary<string, object?>());
    }

    private static Dictionary<string, object?> BuildLearnerSpeakingTaskPayload(ContentItem item)
    {
        var detail = SpeakingContentStructure.ExtractStructure(item.DetailJson);
        var candidate = SpeakingContentStructure.ToDictionary(SpeakingContentStructure.ReadValue(detail, "candidateCard"));

        var role = SpeakingContentStructure.ReadString(candidate, "candidateRole", "role")
                   ?? SpeakingContentStructure.ReadString(detail, "candidateRole", "role")
                   ?? "Candidate";
        var setting = SpeakingContentStructure.ReadString(candidate, "setting")
                      ?? SpeakingContentStructure.ReadString(detail, "setting")
                      ?? "Clinical setting";
        var patient = SpeakingContentStructure.ReadString(candidate, "patientRole", "patient")
                      ?? SpeakingContentStructure.ReadString(detail, "patientRole", "patient")
                      ?? "Patient";
        var task = SpeakingContentStructure.ReadString(candidate, "task", "brief")
                   ?? SpeakingContentStructure.ReadString(detail, "task", "brief")
                   ?? "Complete the role play using patient-centred communication.";
        var background = SpeakingContentStructure.ReadString(candidate, "background")
                         ?? SpeakingContentStructure.ReadString(detail, "background", "caseNotes")
                         ?? item.CaseNotes
                         ?? string.Empty;
        var tasks = FirstNonEmptyList(
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(candidate, "tasks")),
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(detail, "tasks")),
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(detail, "roleObjectives")));
        var warmUps = SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(detail, "warmUpQuestions"));
        var criteriaFocus = FirstNonEmptyList(
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(detail, "criteriaFocus")),
            JsonSupport.Deserialize<List<string>>(item.CriteriaFocusJson, []));
        var prepSeconds = SpeakingContentStructure.ReadInt(detail, "prepTimeSeconds")
                          ?? SpeakingContentStructure.DefaultPrepTimeSeconds;
        var roleplaySeconds = SpeakingContentStructure.ReadInt(detail, "roleplayTimeSeconds")
                              ?? SpeakingContentStructure.DefaultRoleplayTimeSeconds;
        var disclaimer = SpeakingContentStructure.ReadString(detail, "disclaimer")
                         ?? SpeakingContentStructure.PracticeDisclaimer;

        var candidateCard = new Dictionary<string, object?>
        {
            ["role"] = role,
            ["candidateRole"] = role,
            ["setting"] = setting,
            ["patient"] = patient,
            ["patientRole"] = patient,
            ["task"] = task,
            ["brief"] = task,
            ["background"] = background,
            ["tasks"] = tasks
        };

        return new Dictionary<string, object?>
        {
            ["contentId"] = item.Id,
            ["contentType"] = item.ContentType,
            ["subtest"] = item.SubtestCode,
            ["title"] = item.Title,
            ["professionId"] = item.ProfessionId,
            ["difficulty"] = item.Difficulty,
            ["estimatedDurationMinutes"] = item.EstimatedDurationMinutes,
            ["criteriaFocus"] = criteriaFocus,
            ["criteriaFocusTags"] = criteriaFocus,
            ["scenarioType"] = item.ScenarioType,
            ["modeSupport"] = JsonSupport.Deserialize<List<string>>(item.ModeSupportJson, []),
            ["publishedRevisionId"] = item.PublishedRevisionId,
            ["status"] = ToContentStatus(item.Status),
            ["caseNotes"] = item.CaseNotes,
            ["candidateCard"] = candidateCard,
            ["role"] = role,
            ["setting"] = setting,
            ["patient"] = patient,
            ["task"] = task,
            ["brief"] = task,
            ["background"] = background,
            ["tasks"] = tasks,
            ["warmUpQuestions"] = warmUps,
            ["prepTimeSeconds"] = prepSeconds,
            ["roleplayTimeSeconds"] = roleplaySeconds,
            ["patientEmotion"] = SpeakingContentStructure.ReadString(detail, "patientEmotion") ?? "neutral",
            ["communicationGoal"] = SpeakingContentStructure.ReadString(detail, "communicationGoal", "purpose") ?? "Build rapport and complete the clinical task.",
            ["clinicalTopic"] = SpeakingContentStructure.ReadString(detail, "clinicalTopic") ?? item.ScenarioType ?? "roleplay",
            ["disclaimer"] = disclaimer,
            ["compliance"] = new
            {
                learnerSafe = true,
                // The interlocutor card is intentionally stripped from
                // every learner-facing payload (Wave 2 of
                // docs/SPEAKING-MODULE-PLAN.md). The card lives in
                // ContentPaper.ExtractedTextJson["interlocutorCard"] and
                // is only projected to expert/admin audiences.
                hiddenInterlocutorCard = true,
                sourceProvenanceAvailable = !string.IsNullOrWhiteSpace(item.SourceProvenance),
                officialScore = false
            }
        };
    }

    private static List<string> FirstNonEmptyList(params List<string>[] lists)
        => lists.FirstOrDefault(list => list.Count > 0) ?? [];

    private async Task<List<object>> GetTasksBySubtestAsync(string subtest, CancellationToken cancellationToken)
    {
        var items = await db.ContentItems
            .AsNoTracking()
            .Where(x => x.SubtestCode == subtest && x.Status == ContentStatus.Published)
            .OrderBy(x => x.Title)
            .ToListAsync(cancellationToken);
        if (string.Equals(subtest, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            return items.Select(item => (object)BuildLearnerSpeakingTaskPayload(item)).ToList();
        }

        return items.Select(item => (object)new
        {
            contentId = item.Id,
            contentType = item.ContentType,
            subtest = item.SubtestCode,
            professionId = item.ProfessionId,
            title = item.Title,
            difficulty = item.Difficulty,
            estimatedDurationMinutes = item.EstimatedDurationMinutes,
            criteriaFocus = JsonSupport.Deserialize<List<string>>(item.CriteriaFocusJson, []),
            scenarioType = item.ScenarioType,
            modeSupport = JsonSupport.Deserialize<List<string>>(item.ModeSupportJson, []),
            publishedRevisionId = item.PublishedRevisionId,
            status = ToContentStatus(item.Status)
        }).ToList();
    }

    private async Task<object> CreateAttemptAsync(string userId, CreateAttemptRequest request, string subtest, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var contentForAttempt = await db.ContentItems
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.ContentId, cancellationToken)
            ?? throw ApiException.NotFound("content_not_found", "Practice content not found.");
        if (!string.Equals(contentForAttempt.SubtestCode, subtest, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.NotFound("content_not_found", "Practice content not found.");
        }
        if (contentForAttempt.Status != ContentStatus.Published)
        {
            throw ApiException.Conflict("content_not_available", "This practice content is not currently available.");
        }
        var context = request.Context ?? "practice";
        var mode = request.Mode ?? (subtest is "reading" or "listening" ? "exam" : "practice");
        var existingAttempts = await db.Attempts
            .Where(x => x.UserId == userId
                        && x.ContentId == request.ContentId
                        && x.SubtestCode == subtest
                        && x.Context == context
                        && x.State == AttemptState.InProgress)
            .ToListAsync(cancellationToken);
        var existing = existingAttempts
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefault();
        if (existing is not null)
        {
            return await GetAttemptAsync(existing.Id, cancellationToken);
        }

        var attempt = new Attempt
        {
            Id = $"{subtest[..1]}a-{Guid.NewGuid():N}",
            UserId = userId,
            ContentId = request.ContentId,
            SubtestCode = subtest,
            Context = context,
            Mode = mode,
            State = AttemptState.InProgress,
            StartedAt = DateTimeOffset.UtcNow,
            DeviceType = request.DeviceType ?? "web",
            ParentAttemptId = request.ParentAttemptId,
            ComparisonGroupId = $"{subtest}-{request.ContentId}"
        };
        db.Attempts.Add(attempt);
        await LearnerWorkflowCoordinator.AttachAttemptToDiagnosticAsync(db, attempt, cancellationToken);
        await RecordEventAsync(userId, "task_started", new { attemptId = attempt.Id, contentId = attempt.ContentId, subtest = attempt.SubtestCode, mode = attempt.Mode, context = attempt.Context }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAttemptAsync(attempt.Id, cancellationToken);
    }

    private async Task<object> GetAttemptAsync(string attemptId, CancellationToken cancellationToken)
    {
        var attempt = await db.Attempts.FirstAsync(x => x.Id == attemptId, cancellationToken);
        var content = await db.ContentItems.FirstAsync(x => x.Id == attempt.ContentId, cancellationToken);
        var detail = JsonSupport.Deserialize<Dictionary<string, object?>>(content.DetailJson, new Dictionary<string, object?>());
        var contentPayload = string.Equals(content.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase)
            ? BuildLearnerSpeakingTaskPayload(content)
            : Merge(new Dictionary<string, object?>
            {
                ["contentId"] = content.Id,
                ["title"] = content.Title,
                ["subtest"] = content.SubtestCode,
                ["professionId"] = content.ProfessionId,
                ["difficulty"] = content.Difficulty,
                ["estimatedDurationMinutes"] = content.EstimatedDurationMinutes,
                ["caseNotes"] = content.CaseNotes,
                ["scenarioType"] = content.ScenarioType,
                ["criteriaFocus"] = JsonSupport.Deserialize<List<string>>(content.CriteriaFocusJson, [])
            }, detail);

        return new
        {
            attemptId = attempt.Id,
            userId = attempt.UserId,
            contentId = attempt.ContentId,
            subtest = attempt.SubtestCode,
            context = attempt.Context,
            mode = attempt.Mode,
            state = ToApiState(attempt.State),
            startedAt = attempt.StartedAt,
            submittedAt = attempt.SubmittedAt,
            completedAt = attempt.CompletedAt,
            elapsedSeconds = attempt.ElapsedSeconds,
            draftVersion = attempt.DraftVersion,
            parentAttemptId = attempt.ParentAttemptId,
            comparisonGroupId = attempt.ComparisonGroupId,
            deviceType = attempt.DeviceType,
            lastClientSyncAt = attempt.LastClientSyncAt,
            draftContent = attempt.DraftContent,
            scratchpad = attempt.Scratchpad,
            checklist = JsonSupport.Deserialize<Dictionary<string, bool>>(attempt.ChecklistJson, new Dictionary<string, bool>()),
            answers = JsonSupport.Deserialize<Dictionary<string, string?>>(attempt.AnswersJson, new Dictionary<string, string?>()),
            audioUploadState = ToUploadState(attempt.AudioUploadState),
            transcript = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(attempt.TranscriptJson, []),
            analysis = JsonSupport.Deserialize<Dictionary<string, object?>>(attempt.AnalysisJson, new Dictionary<string, object?>()),
            content = contentPayload
        };
    }

    private async Task<object> GetGenericTaskAsync(string contentId, string subtest, CancellationToken cancellationToken)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId && x.SubtestCode == subtest, cancellationToken)
                   ?? throw ApiException.NotFound("content_not_found", $"{ToDisplaySubtest(subtest)} task not found.");
        var detail = JsonSupport.Deserialize<Dictionary<string, object?>>(item.DetailJson, new Dictionary<string, object?>());
        if (string.Equals(subtest, "reading", StringComparison.OrdinalIgnoreCase))
        {
            detail = RedactLegacyReadingTask(detail);
        }
        else if (string.Equals(subtest, "listening", StringComparison.OrdinalIgnoreCase))
        {
            detail = RedactLegacyListeningTask(detail);
        }
        return Merge(new Dictionary<string, object?>
        {
            ["contentId"] = item.Id,
            ["title"] = item.Title,
            ["difficulty"] = item.Difficulty,
            ["estimatedDurationMinutes"] = item.EstimatedDurationMinutes,
            ["subtest"] = item.SubtestCode,
            ["scenarioType"] = item.ScenarioType
        }, detail);
    }

    private async Task<object> UpdateAnswersAsync(string userId, string attemptId, AnswersUpdateRequest request, string subtest, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetSubtestAttemptOwnedByUserAsync(userId, attemptId, subtest, $"{subtest}_attempt_not_found", $"{ToDisplaySubtest(subtest)} attempt not found.", cancellationToken);
        EnsureObjectiveAnswersEditable(attempt);
        var current = JsonSupport.Deserialize<Dictionary<string, string?>>(attempt.AnswersJson, new Dictionary<string, string?>());
        foreach (var (key, value) in request.Answers)
        {
            current[key] = value;
        }

        attempt.AnswersJson = JsonSupport.Serialize(current);
        attempt.LastClientSyncAt = DateTimeOffset.UtcNow;
        attempt.State = AttemptState.InProgress;
        await db.SaveChangesAsync(cancellationToken);
        return new { attemptId = attempt.Id, answers = current, lastClientSyncAt = attempt.LastClientSyncAt };
    }

    private static void EnsureObjectiveAnswersEditable(Attempt attempt)
    {
        if (attempt.State is AttemptState.NotStarted or AttemptState.InProgress or AttemptState.Paused)
        {
            return;
        }

        throw ApiException.Conflict(
            "objective_attempt_locked",
            "This attempt has already been submitted and cannot be edited.",
            [new ApiFieldError("attemptId", "locked", "Start a new attempt before editing another response.")]);
    }

    private async Task<object> SubmitObjectiveAttemptAsync(string userId, string attemptId, string subtest, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetSubtestAttemptOwnedByUserAsync(userId, attemptId, subtest, $"{subtest}_attempt_not_found", $"{ToDisplaySubtest(subtest)} attempt not found.", cancellationToken);
        if (attempt.State == AttemptState.Completed)
        {
            var existing = await db.Evaluations.FirstAsync(x => x.AttemptId == attempt.Id, cancellationToken);
            return new { attemptId = attempt.Id, evaluationId = existing.Id, state = "completed" };
        }

        if (aiPackageCreditService is not null)
        {
            var debit = await aiPackageCreditService.DeductObjectivePracticeAsync(userId, subtest, attempt.Id, cancellationToken);
            if (!debit.Debited)
            {
                throw ApiException.PaymentRequired(
                    debit.ErrorCode ?? $"no_{subtest}_tests",
                    debit.ErrorMessage ?? $"You have no {ToDisplaySubtest(subtest)} practice tests remaining. Purchase a package to continue.");
            }
        }

        attempt.State = AttemptState.Completed;
        attempt.SubmittedAt = DateTimeOffset.UtcNow;
        attempt.CompletedAt = DateTimeOffset.UtcNow;
        var content = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == attempt.ContentId && x.SubtestCode == subtest, cancellationToken)
            ?? throw ApiException.NotFound("content_not_found", $"{ToDisplaySubtest(subtest)} task not found.");
        var questions = ObjectiveQuestionsForContent(content);
        var answers = JsonSupport.Deserialize<Dictionary<string, string?>>(attempt.AnswersJson, new Dictionary<string, string?>());
        var rawScore = ObjectiveRawScore(questions, answers);
        var score = OetScoring.GradeListeningReading(subtest, rawScore);
        var scoreDisplay = $"{score.RawCorrect} / {score.RawMax} \u2022 {score.ScaledScore} / 500 \u2022 Grade {score.Grade}";
        var incorrectItems = questions
            .Where(question =>
            {
                var questionId = question.GetValueOrDefault("id")?.ToString() ?? string.Empty;
                return !MatchesObjectiveAnswer(answers.GetValueOrDefault(questionId), question.GetValueOrDefault("correctAnswer")?.ToString());
            })
            .ToList();

        var evaluation = new Evaluation
        {
            Id = $"{subtest[..1]}e-{Guid.NewGuid():N}",
            AttemptId = attempt.Id,
            SubtestCode = subtest,
            State = AsyncState.Completed,
            ScoreRange = scoreDisplay,
            GradeRange = $"Grade {score.Grade}",
            ConfidenceBand = ConfidenceBand.High,
            StrengthsJson = JsonSupport.Serialize(score.Passed
                ? new[] { $"Your {ToDisplaySubtest(subtest)} raw score is at or above the OET Grade B practice threshold.", "Your answer flow remained controlled under time pressure." }
                : new[] { $"You completed the {ToDisplaySubtest(subtest)} attempt and now have item-level evidence to review." }),
            IssuesJson = JsonSupport.Serialize(incorrectItems
                .Take(3)
                .Select(question => question.GetValueOrDefault("distractorExplanation")?.ToString()
                    ?? question.GetValueOrDefault("explanation")?.ToString()
                    ?? "Review exact detail evidence before your next attempt.")
                .DefaultIfEmpty("Keep using evidence-backed review to maintain objective accuracy.")),
            CriterionScoresJson = JsonSupport.Serialize(new[]
            {
                new
                {
                    criterionCode = $"{subtest}_accuracy",
                    rawScore = score.RawCorrect,
                    maxRawScore = score.RawMax,
                    scaledScore = score.ScaledScore,
                    grade = score.Grade,
                    passed = score.Passed,
                    scoreDisplay,
                    confidenceBand = "high",
                    explanation = "Objective score graded from the authored answer key."
                }
            }),
            FeedbackItemsJson = JsonSupport.Serialize(incorrectItems.Select(question =>
            {
                var questionId = question.GetValueOrDefault("id")?.ToString() ?? string.Empty;
                return new
                {
                    feedbackItemId = $"{attempt.Id}-{questionId}",
                    criterionCode = ObjectiveErrorType(subtest, question),
                    type = "answer_feedback",
                    anchor = new { questionId },
                    message = question.GetValueOrDefault("explanation")?.ToString() ?? "Review the source evidence for this answer.",
                    severity = "medium",
                    suggestedFix = question.GetValueOrDefault("distractorExplanation")?.ToString() ?? "Repeat a short focused drill for this error type."
                };
            })),
            GeneratedAt = DateTimeOffset.UtcNow,
            ModelExplanationSafe = "This objective result is based on answer accuracy only.",
            LearnerDisclaimer = "Practice estimate only.",
            StatusReasonCode = "completed",
            StatusMessage = "Result ready.",
            LastTransitionAt = DateTimeOffset.UtcNow
        };
        db.Evaluations.Add(evaluation);
        await RecordEventAsync(attempt.UserId, "task_submitted", new { attemptId = attempt.Id, evaluationId = evaluation.Id, subtest, contentId = attempt.ContentId }, cancellationToken);
        await LearnerWorkflowCoordinator.UpdateDiagnosticProgressAsync(db, attempt, AttemptState.Completed, cancellationToken);
        await LearnerWorkflowCoordinator.QueueStudyPlanRegenerationAsync(db, attempt.UserId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new { attemptId = attempt.Id, evaluationId = evaluation.Id, state = "completed" };
    }

    private async Task<object> GetObjectiveEvaluationAsync(string userId, string evaluationId, string subtest, CancellationToken cancellationToken)
    {
        var evaluation = await GetEvaluationOwnedByUserAsync(userId, evaluationId, cancellationToken);
        if (!string.Equals(evaluation.SubtestCode, subtest, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.NotFound($"{subtest}_evaluation_not_found", $"{ToDisplaySubtest(subtest)} evaluation not found.");
        }

        var attempt = await db.Attempts.FirstAsync(x => x.Id == evaluation.AttemptId, cancellationToken);
        if (!string.Equals(attempt.SubtestCode, subtest, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.NotFound($"{subtest}_evaluation_not_found", $"{ToDisplaySubtest(subtest)} evaluation not found.");
        }

        var content = await db.ContentItems.FirstAsync(x => x.Id == attempt.ContentId, cancellationToken);
        await RecordEventAsync(userId, "evaluation_viewed", new { evaluationId = evaluation.Id, attemptId = attempt.Id, subtest = evaluation.SubtestCode }, cancellationToken);
        var detail = JsonSupport.Deserialize<Dictionary<string, object?>>(content.DetailJson, new Dictionary<string, object?>());
        var questions = detail.TryGetValue("questions", out var questionsValue)
            ? JsonSupport.Deserialize<List<Dictionary<string, object?>>>(JsonSupport.Serialize(questionsValue), [])
            : [];
        var answers = JsonSupport.Deserialize<Dictionary<string, string?>>(attempt.AnswersJson, new Dictionary<string, string?>());
        var itemReview = questions
            .Select(question => ObjectiveItemReviewDto(content.SubtestCode, question, answers))
            .ToList();
        var rawScore = ObjectiveRawScore(questions, answers);
        var score = OetScoring.GradeListeningReading(content.SubtestCode, rawScore);
        var scoreDisplay = $"{score.RawCorrect} / {score.RawMax} \u2022 {score.ScaledScore} / 500 \u2022 Grade {score.Grade}";
        var errorClusters = ObjectiveErrorClusters(content.SubtestCode, itemReview);
        return new
        {
            evaluationId = evaluation.Id,
            attemptId = attempt.Id,
            taskId = content.Id,
            title = content.Title,
            subtest = content.SubtestCode,
            score = scoreDisplay,
            rawScore = score.RawCorrect,
            maxRawScore = score.RawMax,
            scaledScore = score.ScaledScore,
            grade = score.Grade,
            passed = score.Passed,
            gradeRange = $"Grade {score.Grade}",
            state = ToAsyncState(evaluation.State),
            strengths = JsonSupport.Deserialize<List<string>>(evaluation.StrengthsJson, []),
            issues = JsonSupport.Deserialize<List<string>>(evaluation.IssuesJson, []),
            feedbackItems = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.FeedbackItemsJson, []),
            itemReview,
            errorClusters,
            recommendedNextDrill = ObjectiveRecommendedDrill(content.SubtestCode, errorClusters),
            transcriptAccess = content.SubtestCode == "listening"
                ? ObjectiveTranscriptAccess(itemReview)
                : null,
            generatedAt = evaluation.GeneratedAt
        };
    }

    private async Task<StudyPlan> GetActiveStudyPlanEntityAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await EnsureLearnerProfileAsync(userId, cancellationToken);
        var goal = await db.Goals.FirstAsync(x => x.UserId == userId, cancellationToken);
        var query = db.StudyPlans.Where(x => x.UserId == userId);
        StudyPlan? plan;
        if (!db.Database.IsSqlite())
        {
            plan = await query.OrderByDescending(x => x.GeneratedAt).FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            var plans = await query.ToListAsync(cancellationToken);
            plan = plans.OrderByDescending(x => x.GeneratedAt).FirstOrDefault();
        }

        if (plan is not null)
        {
            if (!string.Equals(user.CurrentPlanId, plan.Id, StringComparison.Ordinal))
            {
                user.CurrentPlanId = plan.Id;
                await db.SaveChangesAsync(cancellationToken);
            }

            return plan;
        }

        var createdPlan = CreateDefaultStudyPlan(userId, goal, DateTimeOffset.UtcNow);
        db.StudyPlans.Add(createdPlan);
        db.StudyPlanItems.AddRange(CreateDefaultStudyPlanItems(createdPlan.Id));
        user.CurrentPlanId = createdPlan.Id;
        await db.SaveChangesAsync(cancellationToken);
        return createdPlan;
    }

    private async Task<ReadinessSnapshot> GetLatestReadinessSnapshotAsync(
        LearnerProfileState profile,
        CancellationToken cancellationToken)
    {
        // Prefer the unified ReadinessComputationService so callers always see
        // a computed snapshot (not the legacy default stub). Falls back to
        // direct DB read when DI isn't wired (e.g. legacy tests).
        if (readinessComputation is not null)
        {
            return await readinessComputation.GetOrComputeAsync(profile.User.Id, cancellationToken);
        }

        var snapshot = await db.ReadinessSnapshots
            .AsNoTracking()
            .Where(x => x.UserId == profile.User.Id)
            .OrderByDescending(x => x.ComputedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (snapshot is not null)
        {
            return snapshot;
        }

        snapshot = CreateDefaultReadinessSnapshot(profile.User.Id, profile.Goal, DateTimeOffset.UtcNow);
        db.ReadinessSnapshots.Add(snapshot);
        await db.SaveChangesAsync(cancellationToken);
        return snapshot;
    }

    private async Task<BackgroundJobItem?> GetLatestStudyPlanRegenerationJobAsync(string planId, CancellationToken cancellationToken)
    {
        var query = db.BackgroundJobs
            .Where(x => x.Type == JobType.StudyPlanRegeneration && x.ResourceId == planId);

        if (!db.Database.IsSqlite())
        {
            return await query
                .OrderByDescending(x => x.LastTransitionAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var jobs = await query.ToListAsync(cancellationToken);
        return jobs
            .OrderByDescending(x => x.LastTransitionAt)
            .FirstOrDefault();
    }

    private async Task<Evaluation?> GetLatestEvaluationForAttemptsAsync(IReadOnlyCollection<string> attemptIds, CancellationToken cancellationToken)
    {
        if (attemptIds.Count == 0)
        {
            return null;
        }

        var query = db.Evaluations.Where(x => attemptIds.Contains(x.AttemptId));
        if (!db.Database.IsSqlite())
        {
            return await query
                .OrderByDescending(x => x.GeneratedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var evaluations = await query.ToListAsync(cancellationToken);
        return evaluations
            .OrderByDescending(x => x.GeneratedAt)
            .FirstOrDefault();
    }

    private async Task QueueJobAsync(JobType type, string? attemptId = null, string? resourceId = null, string? payloadJson = null, CancellationToken cancellationToken = default)
    {
        var job = new BackgroundJobItem
        {
            Id = $"job-{Guid.NewGuid():N}",
            Type = type,
            State = AsyncState.Queued,
            AttemptId = attemptId,
            ResourceId = resourceId,
            CreatedAt = DateTimeOffset.UtcNow,
            AvailableAt = DateTimeOffset.UtcNow.AddSeconds(1),
            LastTransitionAt = DateTimeOffset.UtcNow,
            StatusReasonCode = "queued",
            StatusMessage = "Queued",
            Retryable = true,
            RetryAfterMs = 2000
        };
        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            job.PayloadJson = payloadJson;
        }
        db.BackgroundJobs.Add(job);
        await Task.CompletedTask;
    }

    private async Task RecordEventAsync(string userId, string eventName, object payload, CancellationToken cancellationToken)
    {
        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            Id = $"evt-{Guid.NewGuid():N}",
            UserId = userId,
            EventName = eventName,
            PayloadJson = JsonSupport.Serialize(payload),
            OccurredAt = DateTimeOffset.UtcNow
        });
        await Task.CompletedTask;
    }

    private void LogAudit(string userId, string action, string resourceType, string? resourceId, string? details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = userId,
            ActorName = $"learner:{userId}",
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details
        });
    }

    private async Task<Dictionary<string, object?>?> GetIdempotentResponseAsync(string scope, string key, CancellationToken cancellationToken)
    {
        var record = await db.IdempotencyRecords.FirstOrDefaultAsync(x => x.Scope == scope && x.Key == key, cancellationToken);
        return record is null
            ? null
            : JsonSupport.Deserialize<Dictionary<string, object?>>(record.ResponseJson, new Dictionary<string, object?>());
    }

    private sealed record PaymentIdempotencyReservation(bool ShouldProcess, Dictionary<string, object?>? CachedResponse);

    private static string? NormalizeIdempotencyKey(string? key)
    {
        var normalized = key?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Length > PaymentIdempotencyKeyMaxLength || !PaymentIdempotencyKeyRegex.IsMatch(normalized))
        {
            throw ApiException.Validation(
                "invalid_idempotency_key",
                $"Idempotency keys must be 1-{PaymentIdempotencyKeyMaxLength} ASCII token characters.",
                [new ApiFieldError("idempotencyKey", "invalid", "Use letters, numbers, dots, underscores, colons, or hyphens only.")]);
        }

        return normalized;
    }

    private static string ComputeIdempotencyRequestHash(object payload)
    {
        var json = JsonSupport.Serialize(payload);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task<PaymentIdempotencyReservation> ReservePaymentIdempotencyAsync(
        string scope,
        string key,
        string userId,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Scope == scope && x.Key == key, cancellationToken);
        if (existing is not null)
        {
            return ReadPaymentIdempotencyRecord(existing, userId, requestHash);
        }

        var record = new IdempotencyRecord
        {
            Id = $"idem-{Guid.NewGuid():N}",
            Scope = scope,
            Key = key,
            ResponseJson = CreatePaymentIdempotencyEnvelope(userId, requestHash, "processing", response: null),
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.IdempotencyRecords.Add(record);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new PaymentIdempotencyReservation(true, null);
        }
        catch (DbUpdateException)
        {
            db.Entry(record).State = EntityState.Detached;
            existing = await db.IdempotencyRecords.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Scope == scope && x.Key == key, cancellationToken);
            if (existing is not null)
            {
                return ReadPaymentIdempotencyRecord(existing, userId, requestHash);
            }

            throw;
        }
    }

    private PaymentIdempotencyReservation ReadPaymentIdempotencyRecord(
        IdempotencyRecord record,
        string userId,
        string requestHash)
    {
        using var document = JsonDocument.Parse(record.ResponseJson);
        var root = document.RootElement;

        if (!root.TryGetProperty("idempotency", out var idempotency))
        {
            return new PaymentIdempotencyReservation(
                false,
                JsonSupport.Deserialize<Dictionary<string, object?>>(record.ResponseJson, new Dictionary<string, object?>()));
        }

        var storedUserId = idempotency.TryGetProperty("userId", out var userIdElement) ? userIdElement.GetString() : null;
        var storedRequestHash = idempotency.TryGetProperty("requestHash", out var hashElement) ? hashElement.GetString() : null;
        if (!string.Equals(storedUserId, userId, StringComparison.Ordinal)
            || !string.Equals(storedRequestHash, requestHash, StringComparison.Ordinal))
        {
            throw ApiException.Conflict(
                "idempotency_key_reused",
                "This idempotency key was already used for a different billing request.",
                [new ApiFieldError("idempotencyKey", "reused", "Generate a new idempotency key for a changed request.")]);
        }

        var status = idempotency.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
        if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
            && root.TryGetProperty("response", out var response)
            && response.ValueKind != JsonValueKind.Null)
        {
            var cached = JsonSerializer.Deserialize<Dictionary<string, object?>>(response.GetRawText())
                ?? new Dictionary<string, object?>();
            return new PaymentIdempotencyReservation(false, cached);
        }

        throw ApiException.Conflict(
            "idempotency_in_progress",
            "A billing request with this idempotency key is still being prepared. Please retry shortly.",
            [new ApiFieldError("idempotencyKey", "in_progress", "Retry the same request in a few seconds.")]);
    }

    private async Task CompletePaymentIdempotencyAsync(
        string scope,
        string key,
        string userId,
        string requestHash,
        object response,
        CancellationToken cancellationToken)
    {
        var record = await db.IdempotencyRecords
            .FirstOrDefaultAsync(x => x.Scope == scope && x.Key == key, cancellationToken);
        if (record is null)
        {
            return;
        }

        ReadPaymentIdempotencyRecordForCompletion(record, userId, requestHash);
        record.ResponseJson = CreatePaymentIdempotencyEnvelope(userId, requestHash, "completed", response);
    }

    private async Task TryCompletePaymentIdempotencyAsync(
        string scope,
        string key,
        string userId,
        string requestHash,
        object response,
        CancellationToken cancellationToken)
    {
        try
        {
            db.ChangeTracker.Clear();
            await CompletePaymentIdempotencyAsync(scope, key, userId, requestHash, response, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Preserve the original billing error. A later retry with the same
            // provider idempotency key can still recover at the gateway layer.
        }
    }

    private static void ReadPaymentIdempotencyRecordForCompletion(
        IdempotencyRecord record,
        string userId,
        string requestHash)
    {
        using var document = JsonDocument.Parse(record.ResponseJson);
        if (!document.RootElement.TryGetProperty("idempotency", out var idempotency))
        {
            return;
        }

        var storedUserId = idempotency.TryGetProperty("userId", out var userIdElement) ? userIdElement.GetString() : null;
        var storedRequestHash = idempotency.TryGetProperty("requestHash", out var hashElement) ? hashElement.GetString() : null;
        if (!string.Equals(storedUserId, userId, StringComparison.Ordinal)
            || !string.Equals(storedRequestHash, requestHash, StringComparison.Ordinal))
        {
            throw ApiException.Conflict("idempotency_key_reused", "This idempotency key belongs to a different request.");
        }
    }

    private async Task RemovePaymentIdempotencyReservationAsync(string scope, string key, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var record = await db.IdempotencyRecords.FirstOrDefaultAsync(x => x.Scope == scope && x.Key == key, cancellationToken);
        if (record is null)
        {
            return;
        }

        db.IdempotencyRecords.Remove(record);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string CreatePaymentIdempotencyEnvelope(
        string userId,
        string requestHash,
        string status,
        object? response)
        => JsonSupport.Serialize(new
        {
            idempotency = new
            {
                version = "payment-v1",
                userId,
                requestHash,
                status
            },
            response
        });

    #if false
    private async Task<BillingPlan?> FindBillingPlanAsync(string planCode, CancellationToken cancellationToken)
    {
        var normalized = (planCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return await db.BillingPlans.AsNoTracking()
            .FirstOrDefaultAsync(plan => string.Equals(plan.Code, normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(plan.Id, normalized, StringComparison.OrdinalIgnoreCase), cancellationToken);
    }

    private async Task<BillingAddOn?> FindBillingAddOnAsync(string addOnCode, CancellationToken cancellationToken)
    {
        var normalized = (addOnCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return await db.BillingAddOns.AsNoTracking()
            .FirstOrDefaultAsync(addOn => string.Equals(addOn.Code, normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(addOn.Id, normalized, StringComparison.OrdinalIgnoreCase), cancellationToken);
    }

    private async Task<BillingCoupon?> FindBillingCouponAsync(string couponCode, CancellationToken cancellationToken)
    {
        var normalized = (couponCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return await db.BillingCoupons
            .FirstOrDefaultAsync(coupon => string.Equals(coupon.Code, normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(coupon.Id, normalized, StringComparison.OrdinalIgnoreCase), cancellationToken);
    }

    private static List<string> NormalizeCodes(IEnumerable<string>? codes)
        => (codes ?? Array.Empty<string>()).Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static Dictionary<string, object?> DeserializeQuoteValidation(string json)
        => JsonSupport.Deserialize<Dictionary<string, object?>>(json, new Dictionary<string, object?>());

    private static BillingQuoteResponse DeserializeQuoteResponse(BillingQuote quote)
    {
        var snapshot = JsonSupport.Deserialize<Dictionary<string, object?>>(quote.SnapshotJson, new Dictionary<string, object?>());
        var items = JsonSupport.Deserialize<List<BillingQuoteLineItem>>(JsonSupport.Serialize(snapshot.GetValueOrDefault("items") ?? []), []);
        var validation = DeserializeQuoteValidation(JsonSupport.Serialize(snapshot.GetValueOrDefault("validation") ?? new Dictionary<string, object?>()));
        return new BillingQuoteResponse(
            quote.Id,
            quote.Status.ToString().ToLowerInvariant(),
            quote.Currency,
            quote.SubtotalAmount,
            quote.DiscountAmount,
            quote.TotalAmount,
            quote.PlanCode,
            quote.CouponCode,
            JsonSupport.Deserialize<List<string>>(quote.AddOnCodesJson, []),
            items,
            quote.ExpiresAt,
            snapshot.TryGetValue("summary", out var summaryValue) ? summaryValue?.ToString() ?? string.Empty : string.Empty,
            validation);
    }

      private async Task<BillingQuoteResponse> BuildBillingQuoteAsync(
          string userId,
          BillingQuoteRequest request,
          CancellationToken cancellationToken,
          bool persistQuote)
    {
          var normalizedProductType = (request.ProductType ?? string.Empty).Trim().ToLowerInvariant();
          var now = DateTimeOffset.UtcNow;
          var subscription = await db.Subscriptions.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
          if (subscription is null)
          {
              var defaultPlan = await db.BillingPlans.AsNoTracking()
                  .Where(plan => plan.Status == BillingPlanStatus.Active && plan.IsVisible)
                  .OrderBy(plan => plan.DisplayOrder)
                  .ThenBy(plan => plan.Price)
                  .FirstOrDefaultAsync(cancellationToken)
                  ?? throw ApiException.NotFound(
                      "billing_plan_not_found",
                      "No published billing plan is available for checkout.");

            subscription = new Subscription
            {
                Id = TruncateIdentifier($"sub-{Guid.NewGuid():N}"),
                UserId = userId,
                PlanId = defaultPlan.Code,
                Status = SubscriptionStatus.Active,
                NextRenewalAt = now.AddMonths(Math.Max(defaultPlan.DurationMonths, 1)),
                StartedAt = now,
                ChangedAt = now,
                PriceAmount = defaultPlan.Price,
                Currency = defaultPlan.Currency,
                Interval = defaultPlan.Interval
            };

            db.Subscriptions.Add(subscription);
            await db.SaveChangesAsync(cancellationToken);
        }
        var currentPlan = await FindBillingPlanAsync(subscription.PlanId, cancellationToken);
        var addOnCodes = NormalizeCodes(request.AddOnCodes);
        var items = new List<BillingQuoteLineItem>();
        decimal subtotal;
        string? planCode = null;
        string summary;

          if (normalizedProductType is "plan_upgrade" or "plan_downgrade")
          {
              if (string.IsNullOrWhiteSpace(request.PriceId))
              {
                  throw ApiException.Validation(
                      "target_plan_required",
                      "A target plan id is required for plan changes.",
                      [new ApiFieldError("priceId", "required", "Choose the plan you want to switch to.")]);
              }

              var targetPlan = await FindPurchasableBillingPlanAsync(request.PriceId, cancellationToken)
                  ?? throw ApiException.Validation(
                      "unknown_plan",
                      $"Unknown billing plan '{request.PriceId}'.",
                      [new ApiFieldError("priceId", "unknown", "Choose a published billing plan.")]);

              planCode = targetPlan.Code;
              var referencePlan = currentPlan ?? targetPlan;
              var delta = targetPlan.Price - referencePlan.Price;
              subtotal = Math.Round(Math.Abs(delta) / 2m, 2, MidpointRounding.AwayFromZero);
            summary = normalizedProductType == "plan_upgrade"
                ? $"Switching to {targetPlan.Name} increases your billing amount by {Math.Abs(delta):0.00} {targetPlan.Currency}."
                : $"Switching to {targetPlan.Name} lowers your billing amount by {Math.Abs(delta):0.00} {targetPlan.Currency}.";
            items.Add(new BillingQuoteLineItem(
                "plan",
                targetPlan.Code,
                targetPlan.Name,
                subtotal,
                targetPlan.Currency,
                1,
                targetPlan.Description));
        }
          else if (normalizedProductType == "addon_purchase")
          {
              if (string.IsNullOrWhiteSpace(request.PriceId))
              {
                  throw ApiException.Validation(
                      "target_addon_required",
                      "A target add-on id is required for add-on purchases.",
                      [new ApiFieldError("priceId", "required", "Choose the add-on you want to purchase.")]);
              }

              var addOn = await FindPurchasableBillingAddOnAsync(request.PriceId, cancellationToken)
                  ?? throw ApiException.Validation(
                      "unknown_addon",
                      $"Unknown billing add-on '{request.PriceId}'.",
                      [new ApiFieldError("priceId", "unknown", "Choose a published add-on.")]);

              if (!IsAddOnCompatibleWithPlan(addOn, currentPlan))
              {
                  throw ApiException.Validation(
                      "addon_incompatible",
                      "The selected add-on is not available for your current plan.",
                      [new ApiFieldError("priceId", "incompatible", "Choose an add-on that works with your current plan.")]);
              }

              if (addOn.MaxQuantity is not null && request.Quantity > addOn.MaxQuantity.Value)
              {
                  throw ApiException.Validation(
                    "addon_quantity_exceeded",
                    "The requested add-on quantity exceeds the allowed maximum.",
                    [new ApiFieldError("quantity", "max_exceeded", "Reduce the quantity and try again.")]);
            }

            planCode = currentPlan?.Code;
            subtotal = Math.Round(addOn.Price * request.Quantity, 2, MidpointRounding.AwayFromZero);
            summary = $"{request.Quantity} x {addOn.Name}.";
            items.Add(new BillingQuoteLineItem(
                "addon",
                addOn.Code,
                addOn.Name,
                subtotal,
                addOn.Currency,
                request.Quantity,
                addOn.Description));
        }
          else
          {
              BillingAddOn? reviewPack = null;
              if (!string.IsNullOrWhiteSpace(request.PriceId))
              {
                  reviewPack = await FindPurchasableBillingAddOnAsync(request.PriceId, cancellationToken);
                  if (reviewPack is not null && !IsAddOnCompatibleWithPlan(reviewPack, currentPlan))
                  {
                      reviewPack = null;
                  }
              }

              reviewPack ??= (await db.BillingAddOns.AsNoTracking()
                  .Where(addOn => addOn.Status == BillingAddOnStatus.Active && addOn.GrantCredits == request.Quantity)
                  .OrderBy(addOn => addOn.DisplayOrder)
                  .ToListAsync(cancellationToken))
                  .FirstOrDefault(addOn => IsAddOnCompatibleWithPlan(addOn, currentPlan));

              reviewPack ??= (await db.BillingAddOns.AsNoTracking()
                  .Where(addOn => addOn.Status == BillingAddOnStatus.Active && addOn.GrantCredits > 0)
                  .OrderBy(addOn => addOn.DisplayOrder)
                  .ThenBy(addOn => addOn.Price)
                  .ToListAsync(cancellationToken))
                  .FirstOrDefault(addOn => IsAddOnCompatibleWithPlan(addOn, currentPlan))
                  ?? throw ApiException.Validation(
                      "review_pack_unavailable",
                      "No review credit pack is available for the requested quantity.",
                      [new ApiFieldError("quantity", "unsupported", "Choose one of the available review credit packs.")]);

            planCode = currentPlan?.Code;
            subtotal = Math.Round(reviewPack.Price, 2, MidpointRounding.AwayFromZero);
            summary = $"Review credit pack: {reviewPack.GrantCredits} credits.";
            items.Add(new BillingQuoteLineItem(
                "addon",
                reviewPack.Code,
                reviewPack.Name,
                subtotal,
                reviewPack.Currency,
                1,
                reviewPack.Description));
        }

        BillingCoupon? coupon = null;
        decimal discount = 0m;
        var validation = new Dictionary<string, object?>
        {
            ["productType"] = normalizedProductType,
            ["subtotal"] = subtotal,
            ["planCode"] = planCode,
            ["addOnCodes"] = addOnCodes
        };

        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            coupon = await FindBillingCouponAsync(request.CouponCode, cancellationToken);
            if (coupon is null)
            {
                throw ApiException.Validation(
                    "coupon_not_found",
                    "The coupon code could not be found.",
                    [new ApiFieldError("couponCode", "unknown", "Enter a valid coupon code.")]);
            }

            if (coupon.Status != BillingCouponStatus.Active)
            {
                throw ApiException.Validation(
                    "coupon_inactive",
                    "The coupon is not active.",
                    [new ApiFieldError("couponCode", "inactive", "Use a currently active coupon.")]);
            }

            if (coupon.StartsAt is not null && coupon.StartsAt > now)
            {
                throw ApiException.Validation(
                    "coupon_not_started",
                    "The coupon is not yet active.",
                    [new ApiFieldError("couponCode", "not_started", "Try again once the coupon start date has passed.")]);
            }

            if (coupon.EndsAt is not null && coupon.EndsAt < now)
            {
                throw ApiException.Validation(
                    "coupon_expired",
                    "The coupon has expired.",
                    [new ApiFieldError("couponCode", "expired", "Use a valid coupon code.")]);
            }

            if (coupon.MinimumSubtotal is not null && subtotal < coupon.MinimumSubtotal.Value)
            {
                throw ApiException.Validation(
                    "coupon_minimum_not_met",
                    "The coupon minimum purchase amount was not met.",
                    [new ApiFieldError("couponCode", "minimum_not_met", "Add more to your order or use another coupon.")]);
            }

            var planAllowList = JsonSupport.Deserialize<List<string>>(coupon.ApplicablePlanCodesJson, []);
            var addOnAllowList = JsonSupport.Deserialize<List<string>>(coupon.ApplicableAddOnCodesJson, []);
            if (planAllowList.Count > 0 && (planCode is null || !planAllowList.Any(code => string.Equals(code, planCode, StringComparison.OrdinalIgnoreCase))))
            {
                throw ApiException.Validation(
                    "coupon_not_applicable",
                    "The coupon does not apply to the selected plan.",
                    [new ApiFieldError("couponCode", "not_applicable", "Choose a coupon that matches the selected plan.")]);
            }

            if (addOnAllowList.Count > 0 && addOnCodes.Count > 0 && !addOnCodes.Any(code => addOnAllowList.Any(allowed => string.Equals(allowed, code, StringComparison.OrdinalIgnoreCase))))
            {
                throw ApiException.Validation(
                    "coupon_not_applicable",
                    "The coupon does not apply to the selected add-on.",
                    [new ApiFieldError("couponCode", "not_applicable", "Choose a coupon that matches the selected add-on.")]);
            }

            var couponRedemptionCount = await db.BillingCouponRedemptions.CountAsync(redemption => redemption.CouponCode == coupon.Code && redemption.Status != BillingRedemptionStatus.Voided, cancellationToken);
            if (coupon.UsageLimitTotal is not null && couponRedemptionCount >= coupon.UsageLimitTotal.Value)
            {
                throw ApiException.Validation(
                    "coupon_exhausted",
                    "The coupon usage limit has been reached.",
                    [new ApiFieldError("couponCode", "usage_limit", "Choose a different coupon.")]);
            }

            var perUserRedemptionCount = await db.BillingCouponRedemptions.CountAsync(redemption => redemption.CouponCode == coupon.Code && redemption.UserId == userId && redemption.Status != BillingRedemptionStatus.Voided, cancellationToken);
            if (coupon.UsageLimitPerUser is not null && perUserRedemptionCount >= coupon.UsageLimitPerUser.Value)
            {
                throw ApiException.Validation(
                    "coupon_user_limit",
                    "You have already used this coupon.",
                    [new ApiFieldError("couponCode", "user_limit", "This coupon can only be used once per user.")]);
            }

            discount = coupon.DiscountType == BillingDiscountType.Percentage
                ? Math.Round(subtotal * Math.Min(coupon.DiscountValue, 100m) / 100m, 2, MidpointRounding.AwayFromZero)
                : Math.Round(Math.Min(coupon.DiscountValue, subtotal), 2, MidpointRounding.AwayFromZero);

            if (discount > subtotal)
            {
                discount = subtotal;
            }

            validation["couponCode"] = coupon.Code;
            validation["couponStatus"] = coupon.Status.ToString().ToLowerInvariant();
            validation["discountType"] = coupon.DiscountType.ToString().ToLowerInvariant();
            validation["discountValue"] = coupon.DiscountValue;
            validation["discount"] = discount;
        }

        var total = Math.Max(0m, Math.Round(subtotal - discount, 2, MidpointRounding.AwayFromZero));
        var quoteIdValue = $"quote-{Guid.NewGuid():N}";
        var quoteId = quoteIdValue[..Math.Min(64, quoteIdValue.Length)];
        var quote = new BillingQuote
        {
            Id = quoteId,
            UserId = userId,
            SubscriptionId = subscription.Id,
            PlanCode = planCode,
            AddOnCodesJson = JsonSupport.Serialize(addOnCodes),
            CouponCode = coupon?.Code,
            Currency = items.FirstOrDefault()?.Currency ?? subscription.Currency,
            SubtotalAmount = subtotal,
            DiscountAmount = discount,
            TotalAmount = total,
            Status = BillingQuoteStatus.Created,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(30),
            SnapshotJson = JsonSupport.Serialize(new
            {
                items,
                validation,
                summary,
                subtotal,
                discount,
                total
            })
        };

        if (persistQuote)
        {
            db.BillingQuotes.Add(quote);
            db.BillingEvents.Add(new BillingEvent
            {
                Id = $"bill-evt-{Guid.NewGuid():N}",
                UserId = userId,
                SubscriptionId = subscription.Id,
                QuoteId = quote.Id,
                EventType = "billing_quote_created",
                EntityType = "BillingQuote",
                EntityId = quote.Id,
                PayloadJson = JsonSupport.Serialize(new { planCode, addOnCodes, couponCode = coupon?.Code, subtotal, discount, total }),
                OccurredAt = now
            });

            if (coupon is not null)
            {
                var redemptionIdValue = $"redemption-{Guid.NewGuid():N}";
                db.BillingCouponRedemptions.Add(new BillingCouponRedemption
                {
                    Id = redemptionIdValue[..Math.Min(64, redemptionIdValue.Length)],
                    CouponCode = coupon.Code,
                    UserId = userId,
                    QuoteId = quote.Id,
                    DiscountAmount = discount,
                    Currency = quote.Currency,
                    Status = BillingRedemptionStatus.Reserved,
                    RedeemedAt = now
                });

                coupon.RedemptionCount += 1;
                coupon.UpdatedAt = now;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return new BillingQuoteResponse(
            quote.Id,
            quote.Status.ToString().ToLowerInvariant(),
            quote.Currency,
            quote.SubtotalAmount,
            quote.DiscountAmount,
            quote.TotalAmount,
            quote.PlanCode,
            quote.CouponCode,
            addOnCodes,
            items,
            quote.ExpiresAt,
            summary,
            validation);
    }
    #endif

    private async Task SaveIdempotentResponseAsync(string scope, string key, object response, CancellationToken cancellationToken)
    {
        var exists = await db.IdempotencyRecords.AnyAsync(x => x.Scope == scope && x.Key == key, cancellationToken);
        if (exists)
        {
            return;
        }

        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = $"idem-{Guid.NewGuid():N}",
            Scope = scope,
            Key = key,
            ResponseJson = JsonSupport.Serialize(response),
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private sealed record ExperimentVariant(string Code, int Weight, decimal PriceMultiplier);

    /// <summary>
    /// Looks up any running pricing experiment for the given product, deterministically
    /// assigns the user to a variant via SHA-256 hash, persists the assignment, and
    /// returns the assignment id and priceMultiplier. Returns (null, 1.0) if no
    /// experiment applies or the user falls outside the rollout bucket.
    /// </summary>
    private async Task<(string? AssignmentId, decimal Multiplier)> TryApplyPricingExperimentAsync(
        string userId, string targetType, string targetId, CancellationToken ct)
    {
        var userRegion = await db.ApplicationUserAccounts
            .Where(u => u.Id == userId)
            .Select(u => u.PreferredRegion)
            .FirstOrDefaultAsync(ct);

        var experiments = await db.PricingExperiments
            .Where(e => e.Status == "running"
                && e.TargetType == targetType
                && e.TargetId == targetId
                && (e.Region == "*" || e.Region == userRegion))
            .ToListAsync(ct);

        foreach (var experiment in experiments)
        {
            // Deterministic rollout check: SHA-256(experimentId:userId) → bucket mod 100
            var rolloutHash = SHA256.HashData(Encoding.UTF8.GetBytes($"{experiment.Id}:{userId}"));
            var rolloutBucket = (int)(BinaryPrimitives.ReadUInt64BigEndian(rolloutHash) % 100);
            if (rolloutBucket >= experiment.RolloutPercent)
                continue;

            var variants = JsonSupport.Deserialize<List<ExperimentVariant>>(experiment.VariantsJson, []);
            if (variants.Count == 0)
                continue;

            // Deterministic variant pick: SHA-256(experimentId:userId:variant) → weighted selection
            var variantHash = SHA256.HashData(Encoding.UTF8.GetBytes($"{experiment.Id}:{userId}:variant"));
            var totalWeight = variants.Sum(v => v.Weight);
            if (totalWeight <= 0)
                continue;
            var variantBucket = (int)(BinaryPrimitives.ReadUInt64BigEndian(variantHash) % (ulong)totalWeight);
            string assignedCode = variants[^1].Code;
            int cumulative = 0;
            foreach (var v in variants)
            {
                cumulative += v.Weight;
                if (variantBucket < cumulative) { assignedCode = v.Code; break; }
            }

            // Upsert assignment (unique index on ExperimentId + UserId prevents duplicates)
            var existing = await db.PricingExperimentAssignments
                .FirstOrDefaultAsync(a => a.ExperimentId == experiment.Id && a.UserId == userId, ct);

            if (existing is not null)
            {
                // Respect the pre-existing assignment — don't re-randomise on re-quote
                var existingVariant = variants.FirstOrDefault(v => v.Code == existing.VariantCode);
                return (existing.Id, existingVariant?.PriceMultiplier ?? 1m);
            }

            var assignmentId = TruncateIdentifier($"pea-{Guid.NewGuid():N}");
            db.PricingExperimentAssignments.Add(new PricingExperimentAssignment
            {
                Id = assignmentId,
                ExperimentId = experiment.Id,
                UserId = userId,
                VariantCode = assignedCode,
                AssignedAt = DateTimeOffset.UtcNow,
            });

            var multiplier = variants.FirstOrDefault(v => v.Code == assignedCode)?.PriceMultiplier ?? 1m;
            return (assignmentId, multiplier);
        }

        return (null, 1m);
    }

    private static object StudyPlanItemDto(StudyPlanItem item) => new
    {
        itemId = item.Id,
        title = item.Title,
        subtest = item.SubtestCode,
        durationMinutes = item.DurationMinutes,
        rationale = item.Rationale,
        dueDate = item.DueDate,
        status = ToStudyPlanItemState(item.Status),
        section = item.Section,
        contentId = item.ContentId,
        itemType = item.ItemType,
        route = string.IsNullOrWhiteSpace(item.ContentRoute) ? StudyPlanRouteForItem(item) : item.ContentRoute,
        slotKind = item.SlotKind,
        weekIndex = item.WeekIndex,
        priorityScore = item.PriorityScore,
        linkedReviewItemId = item.LinkedReviewItemId,
        feedbackRating = item.FeedbackRating,
        completedAt = item.CompletedAt,
        actualMinutesSpent = item.ActualMinutesSpent,
        replacedById = item.ReplacedById
    };

    private static string StudyPlanRouteForItem(StudyPlanItem item)
    {
        if (string.Equals(item.ItemType, "mock", StringComparison.OrdinalIgnoreCase)) return "/mocks";
        if (string.Equals(item.SubtestCode, "vocabulary", StringComparison.OrdinalIgnoreCase)) return "/vocabulary";
        if (string.IsNullOrWhiteSpace(item.ContentId)) return $"/{item.SubtestCode.ToLowerInvariant()}";

        return item.SubtestCode.ToLowerInvariant() switch
        {
            "writing" => "/writing/practice/library",
            "speaking" => $"/speaking/task/{Uri.EscapeDataString(item.ContentId)}",
            "reading" => "/reading",
            "listening" => $"/listening/player/{Uri.EscapeDataString(item.ContentId)}",
            _ => $"/{item.SubtestCode.ToLowerInvariant()}"
        };
    }

    private static string MergeJsonSection(string currentJson, Dictionary<string, object?> values)
    {
        var current = JsonSupport.Deserialize<Dictionary<string, object?>>(currentJson, new Dictionary<string, object?>());
        foreach (var (key, value) in values)
        {
            current[key] = value;
        }

        return JsonSupport.Serialize(current);
    }

    private static Dictionary<string, object?> Merge(Dictionary<string, object?> baseValues, Dictionary<string, object?> extra)
    {
        foreach (var (key, value) in extra)
        {
            baseValues[key] = value;
        }

        return baseValues;
    }

    private static Dictionary<string, object?> RedactLegacyReadingTask(Dictionary<string, object?> detail)
    {
        var safe = new Dictionary<string, object?>(detail, StringComparer.OrdinalIgnoreCase);
        safe.Remove("correctAnswer");
        safe.Remove("explanation");
        safe.Remove("acceptedSynonyms");

        if (safe.TryGetValue("questions", out var questions))
        {
            safe["questions"] = RedactLegacyReadingQuestions(questions);
        }

        return safe;
    }

    private static object? RedactLegacyReadingQuestions(object? questions)
    {
        if (questions is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Select(RedactLegacyReadingQuestion).ToList();
        }

        return questions;
    }

    private static Dictionary<string, object?> RedactLegacyReadingQuestion(JsonElement question)
    {
        var safe = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in question.EnumerateObject())
        {
            if (property.NameEquals("correctAnswer")
                || property.NameEquals("explanation")
                || property.NameEquals("acceptedSynonyms"))
            {
                continue;
            }

            safe[property.Name] = property.Value.Clone();
        }

        return safe;
    }

    private static Dictionary<string, object?> RedactLegacyListeningTask(Dictionary<string, object?> detail)
    {
        var safe = new Dictionary<string, object?>(detail, StringComparer.OrdinalIgnoreCase);
        safe.Remove("correctAnswer");
        safe.Remove("explanation");
        safe.Remove("acceptedSynonyms");
        safe.Remove("transcriptExcerpt");
        safe.Remove("distractorExplanation");

        if (safe.TryGetValue("questions", out var questions))
        {
            safe["questions"] = RedactLegacyListeningQuestions(questions);
        }

        return safe;
    }

    private static object? RedactLegacyListeningQuestions(object? questions)
    {
        if (questions is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Select(RedactLegacyListeningQuestion).ToList();
        }

        return questions;
    }

    private static Dictionary<string, object?> RedactLegacyListeningQuestion(JsonElement question)
    {
        var safe = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in question.EnumerateObject())
        {
            if (property.NameEquals("correctAnswer")
                || property.NameEquals("explanation")
                || property.NameEquals("acceptedSynonyms")
                || property.NameEquals("transcriptExcerpt")
                || property.NameEquals("distractorExplanation"))
            {
                continue;
            }

            safe[property.Name] = property.Value.Clone();
        }

        return safe;
    }

    private static string ToApiState(AttemptState state) => state switch
    {
        AttemptState.NotStarted => "not_started",
        AttemptState.InProgress => "in_progress",
        AttemptState.Paused => "paused",
        AttemptState.Submitted => "submitted",
        AttemptState.Evaluating => "evaluating",
        AttemptState.Completed => "completed",
        AttemptState.Failed => "failed",
        AttemptState.Abandoned => "abandoned",
        _ => "unknown"
    };

    private static string ToDisplaySubtest(string code) => code.ToLowerInvariant() switch
    {
        "writing" => "Writing",
        "speaking" => "Speaking",
        "reading" => "Reading",
        "listening" => "Listening",
        _ => code
    };

    private static string NormalizeDiagnosticSubtest(string? code)
    {
        var value = code?.Trim();
        var normalized = StringComparer.OrdinalIgnoreCase.Equals(value, "writing") ? "writing"
            : StringComparer.OrdinalIgnoreCase.Equals(value, "speaking") ? "speaking"
            : StringComparer.OrdinalIgnoreCase.Equals(value, "reading") ? "reading"
            : StringComparer.OrdinalIgnoreCase.Equals(value, "listening") ? "listening"
            : null;

        return normalized ?? throw ApiException.Validation(
            "unsupported_diagnostic_subtest",
            "Choose a supported diagnostic subtest.",
            [new ApiFieldError("subtest", "unsupported", "Choose writing, speaking, reading, or listening.")]);
    }

    private static bool HasDiagnosticPaperTag(string? tagsCsv)
    {
        if (string.IsNullOrWhiteSpace(tagsCsv))
        {
            return false;
        }

        foreach (var tag in tagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(tag, "diagnostic"))
            {
                return true;
            }
        }

        return false;
    }


    private async Task<BillingPlan?> FindBillingPlanAsync(string planCode, CancellationToken cancellationToken)
    {
        var normalized = NormalizeBillingCode(planCode);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return await db.BillingPlans.AsNoTracking()
            .FirstOrDefaultAsync(plan => plan.Code.ToLower() == normalized
                || plan.Id.ToLower() == normalized, cancellationToken);
    }

    private async Task<BillingAddOn?> FindBillingAddOnAsync(string addOnCode, CancellationToken cancellationToken)
    {
        var normalized = NormalizeBillingCode(addOnCode);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return await db.BillingAddOns.AsNoTracking()
            .FirstOrDefaultAsync(addOn => addOn.Code.ToLower() == normalized
                || addOn.Id.ToLower() == normalized, cancellationToken);
    }

    private async Task<bool> IsAiPackageAddOnAsync(string addOnCode, CancellationToken cancellationToken)
    {
        var addOn = await FindBillingAddOnAsync(addOnCode, cancellationToken);
        return string.Equals(addOn?.AddonKind, "ai_package", StringComparison.OrdinalIgnoreCase);
    }

      private async Task<BillingCoupon?> FindBillingCouponAsync(string couponCode, CancellationToken cancellationToken)
      {
          var normalized = NormalizeBillingCode(couponCode);
          if (string.IsNullOrWhiteSpace(normalized))
          {
            return null;
        }

          return await db.BillingCoupons.AsNoTracking()
              .FirstOrDefaultAsync(coupon => coupon.Code.ToLower() == normalized
                  || coupon.Id.ToLower() == normalized, cancellationToken);
      }

      /// <summary>TutorBook packages ("tutor-book" plan, "tutor-book-addon" add-on) are
      /// manual-grant-only per owner directive 2026-07-22: no self-checkout, admins grant
      /// access per-user from the "Manage access" panel (<see cref="UserAccessAllocationService"/>).
      /// Every purchase entry point resolves its target through
      /// <see cref="FindPurchasableBillingPlanAsync"/>/<see cref="FindPurchasableBillingAddOnAsync"/>,
      /// so gating here blocks quote building, plan changes, and cart add-ons in one place.</summary>
      private static readonly HashSet<string> ManualGrantOnlyPlanCodes = new(StringComparer.OrdinalIgnoreCase) { "tutor-book" };
      private static readonly HashSet<string> ManualGrantOnlyAddOnCodes = new(StringComparer.OrdinalIgnoreCase) { "tutor-book-addon" };

      private static void EnsureNotManualGrantOnly(string code)
      {
          if (ManualGrantOnlyPlanCodes.Contains(code) || ManualGrantOnlyAddOnCodes.Contains(code))
          {
              throw ApiException.Validation(
                  "manual_grant_only",
                  "This package is granted manually by an admin — it cannot be purchased directly. Please contact support to enable it on your account.",
                  [new ApiFieldError("priceId", "manual_grant_only", "Contact support to enable this package.")]);
          }
      }

      private async Task<BillingPlan?> FindPurchasableBillingPlanAsync(string planCode, CancellationToken cancellationToken)
      {
          if (!string.IsNullOrWhiteSpace(planCode)) EnsureNotManualGrantOnly(planCode.Trim());
          var plan = await FindBillingPlanAsync(planCode, cancellationToken);
          return plan is not null && plan.Status == BillingPlanStatus.Active && plan.IsVisible && !plan.IsDraft
              ? plan
              : null;
      }

      /// <summary>
      /// Gate a plan purchase on the buyer's registered profession. A plan tagged
      /// <c>all</c> (or untagged) is sold to everyone; anything else must match the
      /// buyer's ActiveProfessionId, because the package's content is resolved on the
      /// subtest x profession axis and a mismatched buyer would pay for an empty course.
      /// Runs on every checkout entry point (fresh quote and saved quote) so a profession
      /// changed after the quote was built cannot slip through.
      /// </summary>
      private async Task EnsurePlanMatchesLearnerProfessionAsync(
          LearnerUser user,
          BillingPlan plan,
          CancellationToken cancellationToken)
      {
          var planProfession = plan.Profession?.Trim();
          if (string.IsNullOrWhiteSpace(planProfession)
              || string.Equals(planProfession, "all", StringComparison.OrdinalIgnoreCase))
          {
              return;
          }

          if (!string.Equals(planProfession, user.ActiveProfessionId, StringComparison.OrdinalIgnoreCase))
          {
              throw ApiException.Validation(
                  "profession_mismatch",
                  $"{plan.Name} is only available to learners registered under a different profession.",
                  [new ApiFieldError("priceId", "profession_mismatch", "Choose a package that matches your registered profession.")]);
          }

          // A package whose enabled content modules resolve to zero items for this
          // profession is unsellable — block it here rather than let the learner pay for
          // an empty course (e.g. Physiotherapy packages while no physiotherapy video
          // exists yet). Fails open when the service is unregistered.
          if (planContentAvailability is null)
          {
              return;
          }

          var availability = await planContentAvailability.ResolveAsync(plan, user.ActiveProfessionId, cancellationToken);
          if (availability.EmptyEnabledModules.Count > 0)
          {
              throw ApiException.Validation(
                  "content_unavailable_for_profession",
                  $"{plan.Name} has no {string.Join(", ", availability.EmptyEnabledModules)} content for your profession yet. Please contact support.",
                  [new ApiFieldError("priceId", "content_unavailable", "Choose a package that has content for your profession.")]);
          }
      }

      private async Task<bool> UserOwnsTutorBookAsync(string userId, CancellationToken cancellationToken)
          => await db.Subscriptions.AsNoTracking().AnyAsync(s =>
              s.UserId == userId
              && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial)
              && s.TutorBookUnlocked, cancellationToken);

      private async Task<BillingAddOn?> FindPurchasableBillingAddOnAsync(string addOnCode, CancellationToken cancellationToken)
      {
          if (!string.IsNullOrWhiteSpace(addOnCode)) EnsureNotManualGrantOnly(addOnCode.Trim());
          var addOn = await FindBillingAddOnAsync(addOnCode, cancellationToken);
          return addOn is not null && addOn.Status == BillingAddOnStatus.Active
              ? addOn
              : null;
      }

      private static bool IsAddOnCompatibleWithPlan(BillingAddOn addOn, BillingPlan? plan)
      {
          var compatiblePlanCodes = JsonSupport.Deserialize<List<string>>(addOn.CompatiblePlanCodesJson, []);

          if (addOn.AppliesToAllPlans)
          {
              return true;
          }

          if (!addOn.RequiresEligibleParent && compatiblePlanCodes.Count == 0)
          {
              return true;
          }

          if (compatiblePlanCodes.Count == 0 || plan is null)
          {
              return false;
          }

          return compatiblePlanCodes.Any(code =>
              string.Equals(code, plan.Code, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(code, plan.Id, StringComparison.OrdinalIgnoreCase));
      }

      private async Task<BillingCatalogVersionRef?> ResolvePlanVersionRefAsync(BillingPlan plan, CancellationToken cancellationToken)
      {
          BillingPlanVersion? version = null;
          if (!string.IsNullOrWhiteSpace(plan.ActiveVersionId))
          {
              version = await db.BillingPlanVersions.AsNoTracking()
                  .FirstOrDefaultAsync(item => item.PlanId == plan.Id && item.Id == plan.ActiveVersionId, cancellationToken);
          }

          if (version is null && !string.IsNullOrWhiteSpace(plan.LatestVersionId))
          {
              version = await db.BillingPlanVersions.AsNoTracking()
                  .FirstOrDefaultAsync(item => item.PlanId == plan.Id && item.Id == plan.LatestVersionId, cancellationToken);
          }

          version ??= await db.BillingPlanVersions.AsNoTracking()
              .Where(item => item.PlanId == plan.Id)
              .OrderByDescending(item => item.VersionNumber)
              .FirstOrDefaultAsync(cancellationToken);

          return version is not null && BillingPlanMatchesVersion(plan, version)
              ? new BillingCatalogVersionRef(version.Id, version.VersionNumber)
              : null;
      }

      private async Task<BillingCatalogVersionRef?> ResolveAddOnVersionRefAsync(BillingAddOn addOn, CancellationToken cancellationToken)
      {
          BillingAddOnVersion? version = null;
          if (!string.IsNullOrWhiteSpace(addOn.ActiveVersionId))
          {
              version = await db.BillingAddOnVersions.AsNoTracking()
                  .FirstOrDefaultAsync(item => item.AddOnId == addOn.Id && item.Id == addOn.ActiveVersionId, cancellationToken);
          }

          if (version is null && !string.IsNullOrWhiteSpace(addOn.LatestVersionId))
          {
              version = await db.BillingAddOnVersions.AsNoTracking()
                  .FirstOrDefaultAsync(item => item.AddOnId == addOn.Id && item.Id == addOn.LatestVersionId, cancellationToken);
          }

          version ??= await db.BillingAddOnVersions.AsNoTracking()
              .Where(item => item.AddOnId == addOn.Id)
              .OrderByDescending(item => item.VersionNumber)
              .FirstOrDefaultAsync(cancellationToken);

          return version is not null && BillingAddOnMatchesVersion(addOn, version)
              ? new BillingCatalogVersionRef(version.Id, version.VersionNumber)
              : null;
      }

      private async Task<BillingCatalogVersionRef?> ResolveCouponVersionRefAsync(BillingCoupon coupon, CancellationToken cancellationToken)
      {
          BillingCouponVersion? version = null;
          if (!string.IsNullOrWhiteSpace(coupon.ActiveVersionId))
          {
              version = await db.BillingCouponVersions.AsNoTracking()
                  .FirstOrDefaultAsync(item => item.CouponId == coupon.Id && item.Id == coupon.ActiveVersionId, cancellationToken);
          }

          if (version is null && !string.IsNullOrWhiteSpace(coupon.LatestVersionId))
          {
              version = await db.BillingCouponVersions.AsNoTracking()
                  .FirstOrDefaultAsync(item => item.CouponId == coupon.Id && item.Id == coupon.LatestVersionId, cancellationToken);
          }

          version ??= await db.BillingCouponVersions.AsNoTracking()
              .Where(item => item.CouponId == coupon.Id)
              .OrderByDescending(item => item.VersionNumber)
              .FirstOrDefaultAsync(cancellationToken);

          return version is not null && BillingCouponMatchesVersion(coupon, version)
              ? new BillingCatalogVersionRef(version.Id, version.VersionNumber)
              : null;
      }

      private static bool BillingPlanMatchesVersion(BillingPlan plan, BillingPlanVersion version)
          => string.Equals(plan.Code, version.Code, StringComparison.Ordinal)
             && string.Equals(plan.Name, version.Name, StringComparison.Ordinal)
             && string.Equals(plan.Description, version.Description, StringComparison.Ordinal)
             && plan.Price == version.Price
             && string.Equals(plan.Currency, version.Currency, StringComparison.Ordinal)
             && string.Equals(plan.Interval, version.Interval, StringComparison.Ordinal)
             && plan.DurationMonths == version.DurationMonths
             && plan.IsVisible == version.IsVisible
             && plan.IsRenewable == version.IsRenewable
             && plan.TrialDays == version.TrialDays
             && plan.DisplayOrder == version.DisplayOrder
             && plan.IncludedCredits == version.IncludedCredits
             && string.Equals(plan.IncludedSubtestsJson, version.IncludedSubtestsJson, StringComparison.Ordinal)
             && string.Equals(plan.EntitlementsJson, version.EntitlementsJson, StringComparison.Ordinal)
             && plan.Status == version.Status
             && plan.ArchivedAt == version.ArchivedAt;

      private static bool BillingAddOnMatchesVersion(BillingAddOn addOn, BillingAddOnVersion version)
          => string.Equals(addOn.Code, version.Code, StringComparison.Ordinal)
             && string.Equals(addOn.Name, version.Name, StringComparison.Ordinal)
             && string.Equals(addOn.Description, version.Description, StringComparison.Ordinal)
             && addOn.Price == version.Price
             && string.Equals(addOn.Currency, version.Currency, StringComparison.Ordinal)
             && string.Equals(addOn.Interval, version.Interval, StringComparison.Ordinal)
             && addOn.Status == version.Status
             && addOn.IsRecurring == version.IsRecurring
             && addOn.DurationDays == version.DurationDays
             && addOn.GrantCredits == version.GrantCredits
             && string.Equals(addOn.GrantEntitlementsJson, version.GrantEntitlementsJson, StringComparison.Ordinal)
             && string.Equals(addOn.CompatiblePlanCodesJson, version.CompatiblePlanCodesJson, StringComparison.Ordinal)
             && addOn.AppliesToAllPlans == version.AppliesToAllPlans
             && addOn.IsStackable == version.IsStackable
             && addOn.QuantityStep == version.QuantityStep
             && addOn.MaxQuantity == version.MaxQuantity
             && addOn.DisplayOrder == version.DisplayOrder;

      private static bool BillingCouponMatchesVersion(BillingCoupon coupon, BillingCouponVersion version)
          => string.Equals(coupon.Code, version.Code, StringComparison.Ordinal)
             && string.Equals(coupon.Name, version.Name, StringComparison.Ordinal)
             && string.Equals(coupon.Description, version.Description, StringComparison.Ordinal)
             && coupon.DiscountType == version.DiscountType
             && coupon.DiscountValue == version.DiscountValue
             && string.Equals(coupon.Currency, version.Currency, StringComparison.Ordinal)
             && coupon.Status == version.Status
             && coupon.StartsAt == version.StartsAt
             && coupon.EndsAt == version.EndsAt
             && coupon.UsageLimitTotal == version.UsageLimitTotal
             && coupon.UsageLimitPerUser == version.UsageLimitPerUser
             && coupon.MinimumSubtotal == version.MinimumSubtotal
             && string.Equals(coupon.ApplicablePlanCodesJson, version.ApplicablePlanCodesJson, StringComparison.Ordinal)
             && string.Equals(coupon.ApplicableAddOnCodesJson, version.ApplicableAddOnCodesJson, StringComparison.Ordinal)
             && coupon.IsStackable == version.IsStackable
             && string.Equals(coupon.Notes, version.Notes, StringComparison.Ordinal);

      private async Task<int> CountCouponRedemptionsAsync(BillingCoupon coupon, string? userId, CancellationToken cancellationToken)
      {
          var normalizedCouponCode = NormalizeBillingCode(coupon.Code);
          var query = db.BillingCouponRedemptions.Where(redemption =>
              redemption.Status != BillingRedemptionStatus.Voided
              && (redemption.CouponId == coupon.Id
                  || (redemption.CouponId == null && redemption.CouponCode.ToLower() == normalizedCouponCode)));

          if (!string.IsNullOrWhiteSpace(userId))
          {
              query = query.Where(redemption => redemption.UserId == userId);
          }

          return await query.CountAsync(cancellationToken);
      }

      private static Dictionary<string, string> DeserializeAddOnVersionIds(BillingQuote quote)
          => new(JsonSupport.Deserialize<Dictionary<string, string>>(quote.AddOnVersionIdsJson, new Dictionary<string, string>()), StringComparer.OrdinalIgnoreCase);

      private static string NormalizeBillingCode(string? value)
          => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static List<string> NormalizeCodes(IEnumerable<string>? codes)
        => (codes ?? Array.Empty<string>()).Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static BillingQuoteResponse DeserializeQuoteResponse(BillingQuote quote)
    {
        var snapshot = JsonSupport.Deserialize<Dictionary<string, object?>>(quote.SnapshotJson, new Dictionary<string, object?>());
        var items = snapshot.TryGetValue("items", out var itemsValue)
            ? JsonSupport.Deserialize<List<BillingQuoteLineItem>>(JsonSupport.Serialize(itemsValue), [])
            : [];
        var validation = snapshot.TryGetValue("validation", out var validationValue)
            ? JsonSupport.Deserialize<Dictionary<string, object?>>(JsonSupport.Serialize(validationValue), new Dictionary<string, object?>())
            : new Dictionary<string, object?>();

        return new BillingQuoteResponse(
            quote.Id,
            quote.Status.ToString().ToLowerInvariant(),
            quote.Currency,
            quote.SubtotalAmount,
            quote.DiscountAmount,
            quote.TotalAmount,
            quote.PlanCode,
            quote.CouponCode,
            JsonSupport.Deserialize<List<string>>(quote.AddOnCodesJson, []),
            items,
            quote.ExpiresAt,
            snapshot.TryGetValue("summary", out var summaryValue) ? summaryValue?.ToString() ?? string.Empty : string.Empty,
            validation);
    }

    private sealed record BillingCatalogVersionRef(string Id, int VersionNumber);

    private sealed class BillingQuoteCatalogSnapshot
    {
        public int SchemaVersion { get; set; } = 2;
        public DateTimeOffset CapturedAt { get; set; }
        public BillingQuotePlanSnapshot? Plan { get; set; }
        public List<BillingQuoteAddOnSnapshot> AddOns { get; set; } = [];
        public BillingQuoteCouponSnapshot? Coupon { get; set; }
    }

    private sealed class BillingQuotePlanSnapshot
    {
        public string Code { get; set; } = string.Empty;
        public string? VersionId { get; set; }
        public int? VersionNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Currency { get; set; } = "AUD";
        public string Interval { get; set; } = "month";
        public int DurationMonths { get; set; }
        public int IncludedCredits { get; set; }
        public int BundledAiCredits { get; set; }
    }

    private sealed class BillingQuoteAddOnSnapshot
    {
        public string Code { get; set; } = string.Empty;
        public string? VersionId { get; set; }
        public int? VersionNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Currency { get; set; } = "AUD";
        public string Interval { get; set; } = "one_time";
        public bool IsRecurring { get; set; }
        public int DurationDays { get; set; }
        public int GrantCredits { get; set; }
        public string GrantEntitlementsJson { get; set; } = "{}";
    }

    private sealed class BillingQuoteCouponSnapshot
    {
        public string Code { get; set; } = string.Empty;
        public string? VersionId { get; set; }
        public int? VersionNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DiscountType { get; set; } = string.Empty;
        public decimal DiscountValue { get; set; }
        public decimal DiscountAmount { get; set; }
        public string Currency { get; set; } = "AUD";
    }

    private static BillingQuoteCatalogSnapshot BuildQuoteCatalogSnapshot(
        DateTimeOffset capturedAt,
        BillingPlan? plan,
        BillingCatalogVersionRef? planVersion,
        IEnumerable<BillingAddOn> addOns,
        IReadOnlyDictionary<string, BillingCatalogVersionRef> addOnVersions,
        BillingCoupon? coupon,
        BillingCatalogVersionRef? couponVersion,
        decimal discountAmount)
        => new()
        {
            SchemaVersion = 2,
            CapturedAt = capturedAt,
            Plan = plan is null
                ? null
                : new BillingQuotePlanSnapshot
                {
                    Code = plan.Code,
                    VersionId = planVersion?.Id,
                    VersionNumber = planVersion?.VersionNumber,
                    Name = plan.Name,
                    Price = plan.Price,
                    Currency = plan.Currency,
                    Interval = plan.Interval,
                    DurationMonths = plan.DurationMonths,
                    IncludedCredits = plan.IncludedCredits,
                    BundledAiCredits = plan.BundledAiCredits
                },
            AddOns = addOns.Select(addOn => new BillingQuoteAddOnSnapshot
            {
                Code = addOn.Code,
                VersionId = addOnVersions.TryGetValue(addOn.Code, out var addOnVersion) ? addOnVersion.Id : null,
                VersionNumber = addOnVersions.TryGetValue(addOn.Code, out addOnVersion) ? addOnVersion.VersionNumber : null,
                Name = addOn.Name,
                Price = addOn.Price,
                Currency = addOn.Currency,
                Interval = addOn.Interval,
                IsRecurring = addOn.IsRecurring,
                DurationDays = addOn.DurationDays,
                GrantCredits = addOn.GrantCredits,
                GrantEntitlementsJson = addOn.GrantEntitlementsJson
            }).ToList(),
            Coupon = coupon is null
                ? null
                : new BillingQuoteCouponSnapshot
                {
                    Code = coupon.Code,
                    VersionId = couponVersion?.Id,
                    VersionNumber = couponVersion?.VersionNumber,
                    Name = coupon.Name,
                    DiscountType = coupon.DiscountType.ToString(),
                    DiscountValue = coupon.DiscountValue,
                    DiscountAmount = discountAmount,
                    Currency = coupon.Currency
                }
        };

    private static BillingQuoteCatalogSnapshot? DeserializeQuoteCatalogSnapshot(BillingQuote quote)
    {
        var snapshot = JsonSupport.Deserialize<Dictionary<string, object?>>(quote.SnapshotJson, new Dictionary<string, object?>());
        if (!snapshot.TryGetValue("catalog", out var catalogValue) || catalogValue is null)
        {
            return null;
        }

        var catalog = JsonSupport.Deserialize<BillingQuoteCatalogSnapshot?>(JsonSupport.Serialize(catalogValue), null);
        return catalog is null || (catalog.Plan is null && catalog.AddOns.Count == 0 && catalog.Coupon is null)
            ? null
            : catalog;
    }

    private async Task<BillingQuoteResponse> BuildBillingQuoteAsync(
        string userId,
        BillingQuoteRequest request,
        CancellationToken cancellationToken,
        bool persistQuote)
    {
        var normalizedProductType = (request.ProductType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedProductType is not ("review_credits" or "plan_purchase" or "plan_upgrade" or "plan_downgrade" or "addon_purchase"))
        {
            throw ApiException.Validation(
                "unsupported_checkout_product",
                $"Unsupported checkout product '{request.ProductType}'.",
                [new ApiFieldError("productType", "unsupported", "Only supported learner checkout products can be quoted.")]);
        }

        if (request.Quantity <= 0)
        {
            throw ApiException.Validation(
                "invalid_checkout_quantity",
                "Checkout quantity must be greater than zero.",
                [new ApiFieldError("quantity", "invalid", "Choose a checkout quantity greater than zero.")]);
        }

        if (normalizedProductType is "plan_purchase" or "plan_upgrade" or "plan_downgrade" or "addon_purchase"
            && string.IsNullOrWhiteSpace(request.PriceId))
        {
            throw ApiException.Validation(
                "target_item_required",
                "A target plan or add-on id is required for this checkout.",
                [new ApiFieldError("priceId", "required", "Choose the plan or add-on you want to purchase.")]);
        }

        var now = DateTimeOffset.UtcNow;
        var parentSubscriptionId = request.ParentSubscriptionId?.Trim();
        if (normalizedProductType == "addon_purchase")
        {
            if (string.IsNullOrWhiteSpace(request.PriceId))
            {
                throw ApiException.Validation(
                    "target_addon_required",
                    "A target add-on id is required for add-on purchases.",
                    [new ApiFieldError("priceId", "required", "Choose the add-on you want to purchase.")]);
            }

            var preflightAddOn = await FindPurchasableBillingAddOnAsync(request.PriceId, cancellationToken)
                ?? throw ApiException.Validation(
                    "unknown_addon",
                    $"Unknown billing add-on '{request.PriceId}'.",
                    [new ApiFieldError("priceId", "unknown", "Choose a published add-on.")]);

            if (preflightAddOn.RequiresEligibleParent)
            {
                var eligibility = addonEligibilityService ?? new AddonEligibilityService(db);
                var eligibilityResult = await eligibility.ResolveAsync(userId, preflightAddOn.Code, cancellationToken);
                if (!eligibilityResult.Eligible)
                {
                    throw ApiException.Validation(
                        eligibilityResult.Reason ?? "addon_ineligible",
                        "The selected add-on requires an eligible parent enrolment.",
                        [new ApiFieldError("priceId", "ineligible", "Choose an add-on that works with one of your active enrolments.")]);
                }

                if (string.IsNullOrWhiteSpace(parentSubscriptionId))
                {
                    if (eligibilityResult.EligibleParents.Count == 1)
                    {
                        parentSubscriptionId = eligibilityResult.EligibleParents[0].SubscriptionId;
                    }
                    else
                    {
                        throw ApiException.Validation(
                            "parent_subscription_required",
                            "Choose which eligible enrolment this add-on should apply to.",
                            [new ApiFieldError("parentSubscriptionId", "required", "Choose an eligible parent enrolment.")]);
                    }
                }
                else if (!eligibilityResult.EligibleParents.Any(parent =>
                    string.Equals(parent.SubscriptionId, parentSubscriptionId, StringComparison.OrdinalIgnoreCase)))
                {
                    throw ApiException.Validation(
                        "parent_subscription_not_eligible",
                        "The selected parent enrolment is not eligible for this add-on.",
                        [new ApiFieldError("parentSubscriptionId", "ineligible", "Choose an eligible parent enrolment.")]);
                }
            }
        }

        var subscriptionQuery = db.Subscriptions.Where(x => x.UserId == userId);
        var subscription = string.IsNullOrWhiteSpace(parentSubscriptionId)
            ? await subscriptionQuery.FirstOrDefaultAsync(cancellationToken)
            : await subscriptionQuery.FirstOrDefaultAsync(x => x.Id == parentSubscriptionId, cancellationToken);
        if (subscription is null && normalizedProductType == "addon_purchase" && !string.IsNullOrWhiteSpace(parentSubscriptionId))
        {
            throw ApiException.Validation(
                "parent_subscription_not_found",
                "The selected parent enrolment could not be found.",
                [new ApiFieldError("parentSubscriptionId", "unknown", "Choose an eligible parent enrolment.")]);
        }
        if (subscription is null)
        {
            var defaultPlan = await db.BillingPlans.AsNoTracking()
                .Where(plan => plan.Status == BillingPlanStatus.Active)
                .OrderBy(plan => plan.DisplayOrder)
                .ThenBy(plan => plan.Price)
                .FirstOrDefaultAsync(cancellationToken)
                ?? await db.BillingPlans.AsNoTracking()
                    .OrderBy(plan => plan.DisplayOrder)
                    .ThenBy(plan => plan.Price)
                    .FirstOrDefaultAsync(cancellationToken)
                ?? throw ApiException.NotFound(
                    "billing_plan_not_found",
                    "No billing plan is available for checkout.");

            // A brand-new learner with no prior subscription needs a parent row so
            // this quote (and any add-on items) have something to hang off. It is a
            // pre-payment SCAFFOLD only — it MUST NOT confer entitlements until the
            // payment actually completes, otherwise merely opening checkout would
            // light up "Active on your account" and unlock gated modules for free.
            // So it starts Pending (fails-low to FREE in EffectiveEntitlementResolver)
            // and is flipped to Active in ApplyCheckoutCompletionAsync once paid.
            subscription = new Subscription
            {
                Id = TruncateIdentifier($"sub-{Guid.NewGuid():N}"),
                UserId = userId,
                PlanId = defaultPlan.Code,
                Status = SubscriptionStatus.Pending,
                NextRenewalAt = now.AddMonths(Math.Max(defaultPlan.DurationMonths, 1)),
                StartedAt = now,
                ChangedAt = now,
                PriceAmount = defaultPlan.Price,
                Currency = defaultPlan.Currency,
                Interval = defaultPlan.Interval
            };

            db.Subscriptions.Add(subscription);
            await db.SaveChangesAsync(cancellationToken);
        }
        var currentPlan = await FindBillingPlanAsync(subscription.PlanId, cancellationToken);
        var addOnCodes = NormalizeCodes(request.AddOnCodes);
        var items = new List<BillingQuoteLineItem>();
        BillingPlan? snapshotPlan = null;
        var snapshotAddOns = new List<BillingAddOn>();
        decimal subtotal;
        string? planCode = null;
        string summary;

        if (normalizedProductType is "plan_purchase" or "plan_upgrade" or "plan_downgrade")
        {
            if (string.IsNullOrWhiteSpace(request.PriceId))
            {
                throw ApiException.Validation(
                    "target_plan_required",
                    "A target plan id is required for plan changes.",
                    [new ApiFieldError("priceId", "required", "Choose the plan you want to switch to.")]);
            }

            var targetPlan = await FindPurchasableBillingPlanAsync(request.PriceId, cancellationToken)
                ?? throw ApiException.Validation(
                    "unknown_plan",
                    $"Unknown billing plan '{request.PriceId}'.",
                    [new ApiFieldError("priceId", "unknown", "Choose a published billing plan.")]);

            AdminService.EnsurePlanCanStartNewSubscription(targetPlan);
            await EnsurePlanMatchesLearnerProfessionAsync(
                await EnsureUserAsync(userId, cancellationToken),
                targetPlan,
                cancellationToken);
            if (targetPlan.BundledTutorBook && await UserOwnsTutorBookAsync(userId, cancellationToken))
            {
                throw ApiException.Validation(
                    "tutor_book_already_owned",
                    "Your account already has Tutor Book access.",
                    [new ApiFieldError("priceId", "already_owned", "Choose the course-only option or contact support.")]);
            }

            planCode = targetPlan.Code;
            snapshotPlan = targetPlan;
            if (normalizedProductType == "plan_purchase")
            {
                subtotal = Math.Round(targetPlan.Price * Math.Max(1, request.Quantity), 2, MidpointRounding.AwayFromZero);
                summary = $"{request.Quantity} x {targetPlan.Name}.";
            }
            else
            {
                var referencePlan = currentPlan ?? targetPlan;
                var delta = targetPlan.Price - referencePlan.Price;
                subtotal = Math.Round(Math.Abs(delta) / 2m, 2, MidpointRounding.AwayFromZero);
                summary = normalizedProductType == "plan_upgrade"
                    ? $"Switching to {targetPlan.Name} increases your billing amount by {Math.Abs(delta):0.00} {targetPlan.Currency}."
                    : $"Switching to {targetPlan.Name} lowers your billing amount by {Math.Abs(delta):0.00} {targetPlan.Currency}.";
            }
            items.Add(new BillingQuoteLineItem(
                "plan",
                targetPlan.Code,
                targetPlan.Name,
                subtotal,
                targetPlan.Currency,
                Math.Max(1, request.Quantity),
                targetPlan.Description));

            // Cart (multi-item) checkout: a plan bought together with add-ons/AI packages.
            // Compose the AddOnCodes as one-each add-on line items in the same quote so the
            // whole cart is one payment, fulfilled by the ApplyCheckoutCompletionAsync loop
            // (plan block + add-on items). Only for a fresh plan purchase — upgrades and
            // downgrades price on a plan-price delta and never carry cart add-ons.
            if (normalizedProductType == "plan_purchase")
            {
                subtotal += await AppendCartAddOnItemsAsync(request.AddOnCodes, planCode, currentPlan, items, snapshotAddOns, cancellationToken);
                addOnCodes = NormalizeCodes(items.Where(line => line.Kind == "addon").Select(line => line.Code).ToList());
                if (items.Count(line => line.Kind == "addon") > 0)
                {
                    summary = $"{items.Count} items in your cart.";
                }
            }
        }
        else if (normalizedProductType == "addon_purchase")
        {
            if (string.IsNullOrWhiteSpace(request.PriceId))
            {
                throw ApiException.Validation(
                    "target_addon_required",
                    "A target add-on id is required for add-on purchases.",
                    [new ApiFieldError("priceId", "required", "Choose the add-on you want to purchase.")]);
            }

            var addOn = await FindPurchasableBillingAddOnAsync(request.PriceId, cancellationToken)
                ?? throw ApiException.Validation(
                    "unknown_addon",
                    $"Unknown billing add-on '{request.PriceId}'.",
                    [new ApiFieldError("priceId", "unknown", "Choose a published add-on.")]);

            if (!IsAddOnCompatibleWithPlan(addOn, currentPlan))
            {
                throw ApiException.Validation(
                    "addon_incompatible",
                    "The selected add-on is not available for your current plan.",
                    [new ApiFieldError("priceId", "incompatible", "Choose an add-on that works with your current plan.")]);
            }

            if (addOn.MaxQuantity is not null && request.Quantity > addOn.MaxQuantity.Value)
            {
                throw ApiException.Validation(
                    "addon_quantity_exceeded",
                    "The requested add-on quantity exceeds the allowed maximum.",
                    [new ApiFieldError("quantity", "max_exceeded", "Reduce the quantity and try again.")]);
            }

            snapshotAddOns.Add(addOn);
            planCode = currentPlan?.Code;
            var primaryLineAmount = Math.Round(addOn.Price * request.Quantity, 2, MidpointRounding.AwayFromZero);
            subtotal = primaryLineAmount;
            items.Add(new BillingQuoteLineItem(
                "addon",
                addOn.Code,
                addOn.Name,
                primaryLineAmount,
                addOn.Currency,
                request.Quantity,
                addOn.Description));

            // Cart (multi-item) checkout: any AddOnCodes beyond the primary priceId are
            // composed as additional one-each add-on line items in the SAME quote, so the
            // whole cart is charged in one payment and fulfilled by the existing
            // ApplyCheckoutCompletionAsync loop over the quote items. An empty AddOnCodes
            // list keeps the single add-on behaviour byte-for-byte.
            subtotal += await AppendCartAddOnItemsAsync(request.AddOnCodes, addOn.Code, currentPlan, items, snapshotAddOns, cancellationToken);
            addOnCodes = NormalizeCodes(items.Select(line => line.Code).ToList());
            summary = items.Count == 1
                ? $"{request.Quantity} x {addOn.Name}."
                : $"{items.Count} items in your cart.";
        }
        else
        {
            BillingAddOn? reviewPack = null;
            if (!string.IsNullOrWhiteSpace(request.PriceId))
            {
                reviewPack = await FindPurchasableBillingAddOnAsync(request.PriceId, cancellationToken)
                    ?? throw ApiException.Validation(
                        "unknown_addon",
                        $"Unknown billing add-on '{request.PriceId}'.",
                        [new ApiFieldError("priceId", "unknown", "Choose a published review credit pack.")]);

                if (!IsAddOnCompatibleWithPlan(reviewPack, currentPlan))
                {
                    throw ApiException.Validation(
                        "addon_incompatible",
                        "The selected review credit pack is not available for your current plan.",
                        [new ApiFieldError("priceId", "incompatible", "Choose a review credit pack that works with your current plan.")]);
                }
            }

            reviewPack ??= (await db.BillingAddOns.AsNoTracking()
                .Where(addOn => addOn.Status == BillingAddOnStatus.Active && addOn.GrantCredits == request.Quantity)
                .OrderBy(addOn => addOn.DisplayOrder)
                .ToListAsync(cancellationToken))
                .FirstOrDefault(addOn => IsAddOnCompatibleWithPlan(addOn, currentPlan));

            reviewPack ??= (await db.BillingAddOns.AsNoTracking()
                .Where(addOn => addOn.Status == BillingAddOnStatus.Active && addOn.GrantCredits > 0)
                .OrderBy(addOn => addOn.DisplayOrder)
                .ThenBy(addOn => addOn.Price)
                .ToListAsync(cancellationToken))
                .FirstOrDefault(addOn => IsAddOnCompatibleWithPlan(addOn, currentPlan))
                ?? throw ApiException.Validation(
                    "review_pack_unavailable",
                    "No review credit pack is available for the requested quantity.",
                    [new ApiFieldError("quantity", "unsupported", "Choose one of the available review credit packs.")]);

            addOnCodes = NormalizeCodes([reviewPack.Code]);
            snapshotAddOns.Add(reviewPack);
            planCode = currentPlan?.Code;
            subtotal = Math.Round(reviewPack.Price, 2, MidpointRounding.AwayFromZero);
            summary = $"Review credit pack: {reviewPack.GrantCredits} credits.";
            items.Add(new BillingQuoteLineItem(
                "addon",
                reviewPack.Code,
                reviewPack.Name,
                subtotal,
                reviewPack.Currency,
                1,
                reviewPack.Description));
        }

        // Apply any running pricing experiment for this product.
        var expTargetType = normalizedProductType is "plan_upgrade" or "plan_downgrade" ? "plan" : "addon";
        var expTargetId = normalizedProductType is "plan_upgrade" or "plan_downgrade"
            ? (planCode ?? string.Empty)
            : (items.Count > 0 ? items[0].Code : string.Empty);
        var (experimentAssignmentId, priceMultiplier) = string.IsNullOrEmpty(expTargetId)
            ? (null, 1m)
            : await TryApplyPricingExperimentAsync(userId, expTargetType, expTargetId, cancellationToken);
        if (priceMultiplier != 1m && items.Count > 0)
        {
            subtotal = Math.Round(subtotal * priceMultiplier, 2, MidpointRounding.AwayFromZero);
            // Reflect adjusted price in the line item so the quote response is accurate.
            var item = items[0];
            items[0] = item with { Amount = subtotal };
        }

        BillingCoupon? coupon = null;
        decimal discount = 0m;
        var validation = new Dictionary<string, object?>
        {
            ["productType"] = normalizedProductType,
            ["subtotal"] = subtotal,
            ["planCode"] = planCode,
            ["addOnCodes"] = addOnCodes
        };

        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            coupon = await FindBillingCouponAsync(request.CouponCode, cancellationToken);
            if (coupon is null)
            {
                throw ApiException.Validation(
                    "coupon_not_found",
                    "The coupon code could not be found.",
                    [new ApiFieldError("couponCode", "unknown", "Enter a valid coupon code.")]);
            }

            if (coupon.Status != BillingCouponStatus.Active)
            {
                throw ApiException.Validation(
                    "coupon_inactive",
                    "The coupon is not active.",
                    [new ApiFieldError("couponCode", "inactive", "Use a currently active coupon.")]);
            }

            if (coupon.StartsAt is not null && coupon.StartsAt > now)
            {
                throw ApiException.Validation(
                    "coupon_not_started",
                    "The coupon is not yet active.",
                    [new ApiFieldError("couponCode", "not_started", "Try again once the coupon start date has passed.")]);
            }

            if (coupon.EndsAt is not null && coupon.EndsAt < now)
            {
                throw ApiException.Validation(
                    "coupon_expired",
                    "The coupon has expired.",
                    [new ApiFieldError("couponCode", "expired", "Use a valid coupon code.")]);
            }

            if (coupon.MinimumSubtotal is not null && subtotal < coupon.MinimumSubtotal.Value)
            {
                throw ApiException.Validation(
                    "coupon_minimum_not_met",
                    "The coupon minimum purchase amount was not met.",
                    [new ApiFieldError("couponCode", "minimum_not_met", "Add more to your order or use another coupon.")]);
            }

            var planAllowList = JsonSupport.Deserialize<List<string>>(coupon.ApplicablePlanCodesJson, []);
            var addOnAllowList = JsonSupport.Deserialize<List<string>>(coupon.ApplicableAddOnCodesJson, []);
            if (planAllowList.Count > 0 && (planCode is null || !planAllowList.Any(code => string.Equals(code, planCode, StringComparison.OrdinalIgnoreCase))))
            {
                throw ApiException.Validation(
                    "coupon_not_applicable",
                    "The coupon does not apply to the selected plan.",
                    [new ApiFieldError("couponCode", "not_applicable", "Choose a coupon that matches the selected plan.")]);
            }

            if (addOnAllowList.Count > 0 && !addOnCodes.Any(code => addOnAllowList.Any(allowed => string.Equals(allowed, code, StringComparison.OrdinalIgnoreCase))))
            {
                throw ApiException.Validation(
                    "coupon_not_applicable",
                    "The coupon does not apply to the selected add-on.",
                    [new ApiFieldError("couponCode", "not_applicable", "Choose a coupon that matches the selected add-on.")]);
            }

            await ReleaseExpiredCouponReservationsAsync(coupon, now, cancellationToken);

            var couponRedemptionCount = await CountCouponRedemptionsAsync(coupon, userId: null, cancellationToken);
            if (coupon.UsageLimitTotal is not null && couponRedemptionCount >= coupon.UsageLimitTotal.Value)
            {
                throw ApiException.Validation(
                    "coupon_exhausted",
                    "The coupon usage limit has been reached.",
                    [new ApiFieldError("couponCode", "usage_limit", "Choose a different coupon.")]);
            }

            var perUserRedemptionCount = await CountCouponRedemptionsAsync(coupon, userId, cancellationToken);
            if (coupon.UsageLimitPerUser is not null && perUserRedemptionCount >= coupon.UsageLimitPerUser.Value)
            {
                throw ApiException.Validation(
                    "coupon_user_limit",
                    "You have already used this coupon.",
                    [new ApiFieldError("couponCode", "user_limit", "This coupon can only be used once per user.")]);
            }

            // trial_extension_days and free_months shift subscription dates post-checkout;
            // they carry no price discount at quote time.
            if (coupon.CouponVariant is "trial_extension_days" or "free_months")
            {
                discount = 0m;
            }
            else
            {
                discount = coupon.DiscountType == BillingDiscountType.Percentage
                    ? Math.Round(subtotal * Math.Min(coupon.DiscountValue, 100m) / 100m, 2, MidpointRounding.AwayFromZero)
                    : Math.Round(Math.Min(coupon.DiscountValue, subtotal), 2, MidpointRounding.AwayFromZero);

                if (discount > subtotal)
                {
                    discount = subtotal;
                }
            }

            validation["couponCode"] = coupon.Code;
            validation["couponStatus"] = coupon.Status.ToString().ToLowerInvariant();
            validation["discountType"] = coupon.DiscountType.ToString().ToLowerInvariant();
            validation["discountValue"] = coupon.DiscountValue;
            validation["discount"] = discount;
        }

        var total = Math.Max(0m, Math.Round(subtotal - discount, 2, MidpointRounding.AwayFromZero));
        var planVersion = snapshotPlan is null ? null : await ResolvePlanVersionRefAsync(snapshotPlan, cancellationToken);

        // How the plan being bought is handed over, so checkout can tell a Telegram/manual
        // buyer their order sits Pending Manual Fulfilment rather than implying access is
        // live the moment they pay (spec 2026-07-15 §2/§6.6). Read from the version locked
        // to this quote so a mid-flight admin edit cannot change an in-flight order. An
        // add-on/credit purchase buys no plan, so it is always automatic_web.
        validation["deliveryMethod"] = snapshotPlan is null
            ? DeliveryMethods.AutomaticWeb
            : await ResolvePlanDeliveryMethodAsync(planVersion?.Id, snapshotPlan.Code, cancellationToken);

        var addOnVersions = new Dictionary<string, BillingCatalogVersionRef>(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshotAddOn in snapshotAddOns)
        {
            var addOnVersion = await ResolveAddOnVersionRefAsync(snapshotAddOn, cancellationToken);
            if (addOnVersion is not null)
            {
                addOnVersions[snapshotAddOn.Code] = addOnVersion;
            }
        }

        var couponVersion = coupon is null ? null : await ResolveCouponVersionRefAsync(coupon, cancellationToken);
        var addOnVersionIdsJson = JsonSupport.Serialize(addOnVersions.ToDictionary(item => item.Key, item => item.Value.Id, StringComparer.OrdinalIgnoreCase));
        var quoteIdValue = $"quote-{Guid.NewGuid():N}";
        var quoteId = quoteIdValue[..Math.Min(64, quoteIdValue.Length)];
        var quote = new BillingQuote
        {
            Id = quoteId,
            UserId = userId,
            SubscriptionId = subscription.Id,
            PlanCode = planCode,
            PlanVersionId = planVersion?.Id,
            AddOnCodesJson = JsonSupport.Serialize(addOnCodes),
            AddOnVersionIdsJson = addOnVersionIdsJson,
            CouponCode = coupon?.Code,
            CouponVersionId = couponVersion?.Id,
            Currency = items.FirstOrDefault()?.Currency ?? subscription.Currency,
            SubtotalAmount = subtotal,
            DiscountAmount = discount,
            TotalAmount = total,
            Status = BillingQuoteStatus.Created,
            CreatedAt = now,
            ExpiresAt = now.Add(BillingQuoteDefaultLifetime),
            ExperimentAssignmentId = experimentAssignmentId,
            SnapshotJson = JsonSupport.Serialize(new
            {
                items,
                catalog = BuildQuoteCatalogSnapshot(now, snapshotPlan, planVersion, snapshotAddOns, addOnVersions, coupon, couponVersion, discount),
                validation,
                summary,
                subtotal,
                discount,
                total
            })
        };

        if (persistQuote)
        {
            IDbContextTransaction? couponReservationTransaction = null;
            try
            {
                if (coupon is not null)
                {
                    couponReservationTransaction = await BeginTransactionIfNeededAsync(cancellationToken);
                    await LockBillingCouponForReservationAsync(coupon, now, cancellationToken);
                    await ReleaseExpiredCouponReservationsAsync(coupon, now, cancellationToken);

                    var lockedPerUserRedemptionCount = await CountCouponRedemptionsAsync(coupon, userId, cancellationToken);
                    if (coupon.UsageLimitPerUser is not null && lockedPerUserRedemptionCount >= coupon.UsageLimitPerUser.Value)
                    {
                        throw ApiException.Validation(
                            "coupon_user_limit",
                            "You have already used this coupon.",
                            [new ApiFieldError("couponCode", "user_limit", "This coupon can only be used once per user.")]);
                    }

                    var couponReservation = await BillingCouponRedemptionAtomic.TryReserveAsync(db, coupon.Id, now, cancellationToken);
                    if (!couponReservation.Reserved)
                    {
                        throw ApiException.Validation(
                            couponReservation.RejectionCode ?? "coupon_exhausted",
                            "The coupon could not be reserved.",
                            [new ApiFieldError("couponCode", "usage_limit", "Choose a different coupon.")]);
                    }
                }

                db.BillingQuotes.Add(quote);
                db.BillingEvents.Add(new BillingEvent
                {
                    Id = $"bill-evt-{Guid.NewGuid():N}",
                    UserId = userId,
                    SubscriptionId = subscription.Id,
                    QuoteId = quote.Id,
                    EventType = "billing_quote_created",
                    EntityType = "BillingQuote",
                    EntityId = quote.Id,
                    PayloadJson = JsonSupport.Serialize(new { planCode, addOnCodes, couponCode = coupon?.Code, subtotal, discount, total }),
                    OccurredAt = now
                });

                if (coupon is not null)
                {
                    var redemptionIdValue = $"redemption-{Guid.NewGuid():N}";
                    db.BillingCouponRedemptions.Add(new BillingCouponRedemption
                    {
                        Id = redemptionIdValue[..Math.Min(64, redemptionIdValue.Length)],
                        CouponCode = coupon.Code,
                        CouponId = coupon.Id,
                        CouponVersionId = couponVersion?.Id,
                        UserId = userId,
                        QuoteId = quote.Id,
                        DiscountAmount = discount,
                        Currency = quote.Currency,
                        Status = BillingRedemptionStatus.Reserved,
                        RedeemedAt = now
                    });

                }

                await db.SaveChangesAsync(cancellationToken);
                await CommitIfOwnedAsync(couponReservationTransaction, cancellationToken);
            }
            finally
            {
                if (couponReservationTransaction is not null)
                {
                    await couponReservationTransaction.DisposeAsync();
                }
            }
        }

        return new BillingQuoteResponse(
            quote.Id,
            quote.Status.ToString().ToLowerInvariant(),
            quote.Currency,
            quote.SubtotalAmount,
            quote.DiscountAmount,
            quote.TotalAmount,
            quote.PlanCode,
            quote.CouponCode,
            addOnCodes,
            items,
            quote.ExpiresAt,
            summary,
            validation);
    }

    /// <summary>
    /// Composes the "extra" cart add-ons (every code in <paramref name="cartAddOnCodes"/>
    /// except the primary <paramref name="primaryCode"/> and anything already staged in
    /// <paramref name="snapshotAddOns"/>) into the shared quote as one-each add-on line
    /// items, returning the summed price of the appended lines so the caller can fold it
    /// into the quote subtotal. Standalone-only: an add-on that requires an eligible parent
    /// is rejected here (must be purchased on its own) rather than silently attached to the
    /// wrong enrolment. An empty/absent list is a no-op, preserving single-item behaviour.
    /// </summary>
    private async Task<decimal> AppendCartAddOnItemsAsync(
        List<string>? cartAddOnCodes,
        string? primaryCode,
        BillingPlan? currentPlan,
        List<BillingQuoteLineItem> items,
        List<BillingAddOn> snapshotAddOns,
        CancellationToken cancellationToken)
    {
        var appendedTotal = 0m;
        foreach (var extraCode in NormalizeCodes(cartAddOnCodes))
        {
            if (!string.IsNullOrWhiteSpace(primaryCode)
                && string.Equals(extraCode, primaryCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (snapshotAddOns.Any(existing => string.Equals(existing.Code, extraCode, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var extraAddOn = await FindPurchasableBillingAddOnAsync(extraCode, cancellationToken)
                ?? throw ApiException.Validation(
                    "unknown_addon",
                    $"Unknown billing add-on '{extraCode}'.",
                    [new ApiFieldError("addOnCodes", "unknown", "Remove the unavailable item from your cart.")]);

            if (!IsAddOnCompatibleWithPlan(extraAddOn, currentPlan))
            {
                throw ApiException.Validation(
                    "addon_incompatible",
                    $"'{extraAddOn.Name}' is not available for your current plan.",
                    [new ApiFieldError("addOnCodes", "incompatible", "Remove the incompatible item from your cart.")]);
            }

            if (extraAddOn.RequiresEligibleParent)
            {
                throw ApiException.Validation(
                    "addon_requires_parent",
                    $"'{extraAddOn.Name}' must be purchased on its own.",
                    [new ApiFieldError("addOnCodes", "requires_parent", "Check this item out separately.")]);
            }

            var lineAmount = Math.Round(extraAddOn.Price, 2, MidpointRounding.AwayFromZero);
            appendedTotal += lineAmount;
            snapshotAddOns.Add(extraAddOn);
            items.Add(new BillingQuoteLineItem(
                "addon",
                extraAddOn.Code,
                extraAddOn.Name,
                lineAmount,
                extraAddOn.Currency,
                1,
                extraAddOn.Description));
        }

        return appendedTotal;
    }

    private async Task ReleaseExpiredCouponReservationsAsync(BillingCoupon coupon, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var normalizedCouponCode = NormalizeBillingCode(coupon.Code);
        if (string.IsNullOrWhiteSpace(normalizedCouponCode))
        {
            return;
        }

        var expiredReservations = await db.BillingCouponRedemptions
            .Where(redemption => (redemption.CouponId == coupon.Id || (redemption.CouponId == null && redemption.CouponCode.ToLower() == normalizedCouponCode))
                && redemption.Status == BillingRedemptionStatus.Reserved
                && redemption.CheckoutSessionId == null)
            .Join(db.BillingQuotes,
                redemption => redemption.QuoteId,
                quote => quote.Id,
                (redemption, quote) => new { Redemption = redemption, Quote = quote })
            .Where(row => row.Quote.ExpiresAt < now
                && row.Quote.CheckoutSessionId == null
                && (row.Quote.Status == BillingQuoteStatus.Created || row.Quote.Status == BillingQuoteStatus.Expired))
            .ToListAsync(cancellationToken);

        if (expiredReservations.Count == 0)
        {
            return;
        }

        foreach (var row in expiredReservations)
        {
            row.Redemption.Status = BillingRedemptionStatus.Voided;
            row.Quote.Status = BillingQuoteStatus.Expired;
        }

        var releasedCouponKeys = GetCouponRedemptionCountKeys(expiredReservations.Select(row => row.Redemption));
        await db.SaveChangesAsync(cancellationToken);
        await RefreshCouponRedemptionCountsAsync(releasedCouponKeys, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<(string? CouponId, string CouponCode)>> ReleasePreCheckoutCouponReservationsForQuoteAsync(BillingQuote quote, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (quote.ExpiresAt >= now
            || quote.Status is not (BillingQuoteStatus.Created or BillingQuoteStatus.Expired)
            || !string.IsNullOrWhiteSpace(quote.CheckoutSessionId))
        {
            return [];
        }

        quote.Status = BillingQuoteStatus.Expired;

        var redemptions = await db.BillingCouponRedemptions
            .Where(redemption => redemption.QuoteId == quote.Id
                && redemption.Status == BillingRedemptionStatus.Reserved
                && redemption.CheckoutSessionId == null)
            .ToListAsync(cancellationToken);

        if (redemptions.Count == 0)
        {
            return [];
        }

        var releasedCouponKeys = GetCouponRedemptionCountKeys(redemptions);
        foreach (var redemption in redemptions)
        {
            redemption.Status = BillingRedemptionStatus.Voided;
        }

        return releasedCouponKeys;
    }

    private static List<(string? CouponId, string CouponCode)> GetCouponRedemptionCountKeys(IEnumerable<BillingCouponRedemption> releasedRedemptions)
        => releasedRedemptions
            .Select(redemption => (redemption.CouponId, CouponCode: NormalizeBillingCode(redemption.CouponCode)))
            .Where(key => !string.IsNullOrWhiteSpace(key.CouponId) || !string.IsNullOrWhiteSpace(key.CouponCode))
            .Distinct()
            .ToList();

    private async Task LockBillingCouponForReservationAsync(BillingCoupon coupon, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (db.Database.IsInMemory())
        {
            return;
        }

        var lockedCount = await db.BillingCoupons
            .Where(item => item.Id == coupon.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.UpdatedAt, now), cancellationToken);

        if (lockedCount == 0)
        {
            throw ApiException.Validation(
                "coupon_not_found",
                "The coupon code could not be found.",
                [new ApiFieldError("couponCode", "unknown", "Enter a valid coupon code.")]);
        }
    }

    private async Task RefreshCouponRedemptionCountsAsync(IReadOnlyCollection<(string? CouponId, string CouponCode)> releasedCouponKeys, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (releasedCouponKeys.Count == 0)
        {
            return;
        }

        var couponIds = releasedCouponKeys
            .Where(key => !string.IsNullOrWhiteSpace(key.CouponId))
            .Select(key => key.CouponId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var legacyCouponCodes = releasedCouponKeys
            .Where(key => string.IsNullOrWhiteSpace(key.CouponId))
            .Select(key => key.CouponCode)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (couponIds.Count == 0 && legacyCouponCodes.Count == 0)
        {
            return;
        }

        var coupons = await db.BillingCoupons
            .Where(coupon => couponIds.Contains(coupon.Id) || legacyCouponCodes.Contains(coupon.Code.ToLower()))
            .ToListAsync(cancellationToken);

        foreach (var coupon in coupons)
        {
            coupon.RedemptionCount = await CountCouponRedemptionsAsync(coupon, userId: null, cancellationToken);
            coupon.UpdatedAt = now;
        }
    }

    private static string NormalizeMockReviewSelection(string mockType, string? subType, bool includeReview, string? reviewSelection)
    {
        var normalizedMockType = (mockType ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedSubType = (subType ?? string.Empty).Trim().ToLowerInvariant();
        var requestedSelection = (reviewSelection ?? string.Empty).Trim().ToLowerInvariant();

        if (normalizedMockType == "full")
        {
            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "none",
                "writing",
                "speaking",
                "writing_and_speaking"
            };

            if (allowed.Contains(requestedSelection))
            {
                return requestedSelection;
            }

            return includeReview ? "writing_and_speaking" : "none";
        }

        var productiveSubtest = normalizedSubType is "writing" or "speaking";
        if (!productiveSubtest)
        {
            return "none";
        }

        return requestedSelection == "current_subtest"
            ? "current_subtest"
            : includeReview ? "current_subtest" : "none";
    }

    private static object[] MockAttemptSections(string attemptId, object configSource)
    {
        var config = JsonSupport.Deserialize<Dictionary<string, object?>>(JsonSupport.Serialize(configSource), new Dictionary<string, object?>());
        var mockType = ReadString(config.GetValueOrDefault("mockType")) ?? "full";
        var subType = ReadString(config.GetValueOrDefault("subType"));
        var reviewSelection = ReadString(config.GetValueOrDefault("reviewSelection")) ?? "none";

        IEnumerable<string> subtests = string.Equals(mockType, "full", StringComparison.OrdinalIgnoreCase)
            ? ["reading", "listening", "writing", "speaking"]
            : string.IsNullOrWhiteSpace(subType) ? ["reading"] : [subType];

        return subtests.Select(subtest => new
        {
            id = subtest,
            title = $"{ToDisplaySubtest(subtest)} section",
            state = "ready",
            reviewAvailable = subtest is "writing" or "speaking",
            reviewSelected = reviewSelection == "writing_and_speaking"
                || (reviewSelection == "writing" && subtest == "writing")
                || (reviewSelection == "speaking" && subtest == "speaking")
                || (reviewSelection == "current_subtest" && string.Equals(subtest, subType, StringComparison.OrdinalIgnoreCase)),
            launchRoute = $"/mocks/player/{attemptId}?section={Uri.EscapeDataString(subtest)}"
        }).ToArray<object>();
    }

    private static object BuildListeningDrill(string drillId)
    {
        var normalizedId = (drillId ?? string.Empty).Trim().ToLowerInvariant();
        var detail = normalizedId switch
        {
            "listening-drill-distractor_confusion" => new
            {
                drillId = normalizedId,
                title = "Distractor Control Drill",
                focusLabel = "Speaker intent and change-of-plan control",
                description = "Use short consultation clips to separate what was suggested first from what was finally agreed.",
                errorType = "distractor_confusion",
                estimatedMinutes = 12,
                highlights = new[]
                {
                    "Track corrected instructions instead of the first option you hear.",
                    "Notice when a clinician rules out a medication or follow-up plan.",
                    "Review transcript evidence only after you commit to an answer."
                }
            },
            "listening-drill-numbers_and_frequencies" => new
            {
                drillId = normalizedId,
                title = "Numbers and Frequencies Drill",
                focusLabel = "Medication, dosage, and appointment precision",
                description = "Practise capturing exact numbers, timings, and dosage language in fast clinical audio.",
                errorType = "numbers_and_frequencies",
                estimatedMinutes = 10,
                highlights = new[]
                {
                    "Distinguish similar-sounding numbers before replaying.",
                    "Lock onto frequency phrases such as once daily and every second day.",
                    "Use replay snippets to verify quantities, not whole conversations."
                }
            },
            _ => new
            {
                drillId = string.IsNullOrWhiteSpace(normalizedId) ? "listening-drill-detail_capture" : normalizedId,
                title = "Exact Detail Capture Drill",
                focusLabel = "Referral detail and key-clue accuracy",
                description = "Rebuild listening accuracy by isolating the exact clinical detail that changed the answer.",
                errorType = "detail_capture",
                estimatedMinutes = 11,
                highlights = new[]
                {
                    "Identify which detail actually answers the question.",
                    "Separate symptoms, plans, and history without blending them.",
                    "Review the transcript clue that justified the correct answer."
                }
            }
        };

        return new
        {
            detail.drillId,
            detail.title,
            detail.focusLabel,
            detail.description,
            detail.errorType,
            detail.estimatedMinutes,
            detail.highlights,
            launchRoute = $"/listening/player/lt-001?drill={Uri.EscapeDataString(detail.drillId)}",
            reviewRoute = $"/listening/review/lt-001?drill={Uri.EscapeDataString(detail.drillId)}"
        };
    }

    private static int DiagnosticSubtestOrder(string? code) => code?.ToLowerInvariant() switch
    {
        "writing" => 0,
        "speaking" => 1,
        "reading" => 2,
        "listening" => 3,
        _ => 99
    };

    private static object ObjectiveItemReviewDto(string subtest, Dictionary<string, object?> question, Dictionary<string, string?> answers)
    {
        var questionId = question.GetValueOrDefault("id")?.ToString() ?? string.Empty;
        var learnerAnswer = answers.GetValueOrDefault(questionId);
        var correctAnswer = question.GetValueOrDefault("correctAnswer")?.ToString();
        var isCorrect = MatchesObjectiveAnswer(learnerAnswer, correctAnswer);
        var transcriptAllowed = question.TryGetValue("allowTranscriptReveal", out var allowTranscriptRevealValue) && ReadBool(allowTranscriptRevealValue) == true;
        return new
        {
            questionId,
            number = ReadInt(question.GetValueOrDefault("number")) ?? 0,
            prompt = question.GetValueOrDefault("text")?.ToString(),
            type = question.GetValueOrDefault("type")?.ToString(),
            learnerAnswer,
            correctAnswer,
            isCorrect,
            explanation = question.GetValueOrDefault("explanation")?.ToString(),
            errorType = isCorrect ? (string?)null : ObjectiveErrorType(subtest, question),
            options = ReadStringList(question.GetValueOrDefault("options")) ?? [],
            transcript = subtest == "listening" && transcriptAllowed
                ? new
                {
                    allowed = true,
                    excerpt = question.GetValueOrDefault("transcriptExcerpt")?.ToString(),
                    distractorExplanation = question.GetValueOrDefault("distractorExplanation")?.ToString()
                }
                : null
        };
    }

    private static List<Dictionary<string, object?>> ObjectiveQuestionsForContent(ContentItem content)
    {
        var detail = JsonSupport.Deserialize<Dictionary<string, object?>>(content.DetailJson, new Dictionary<string, object?>());
        return detail.TryGetValue("questions", out var questionsValue)
            ? JsonSupport.Deserialize<List<Dictionary<string, object?>>>(JsonSupport.Serialize(questionsValue), [])
            : [];
    }

    private static int ObjectiveRawScore(IEnumerable<Dictionary<string, object?>> questions, Dictionary<string, string?> answers)
    {
        var raw = questions.Count(question =>
        {
            var questionId = question.GetValueOrDefault("id")?.ToString() ?? string.Empty;
            var correctAnswer = question.GetValueOrDefault("correctAnswer")?.ToString();
            return MatchesObjectiveAnswer(answers.GetValueOrDefault(questionId), correctAnswer);
        });

        return Math.Clamp(raw, 0, OetScoring.ListeningReadingRawMax);
    }

    private static IEnumerable<object> ObjectiveErrorClusters(string subtest, IEnumerable<object> itemReview)
    {
        var clusterSeed = itemReview
            .Select(item => JsonSupport.Deserialize<Dictionary<string, object?>>(JsonSupport.Serialize(item), new Dictionary<string, object?>()))
            .Where(item => item.GetValueOrDefault("isCorrect")?.ToString() == bool.FalseString || string.Equals(item.GetValueOrDefault("isCorrect")?.ToString(), "false", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.GetValueOrDefault("errorType")?.ToString() ?? "accuracy")
            .Select(group => new
            {
                errorType = group.Key,
                label = ObjectiveErrorTypeLabel(group.Key),
                count = group.Count(),
                subtest,
                affectedQuestionIds = group.Select(item => item.GetValueOrDefault("questionId")?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x))
            });

        return clusterSeed.Any()
            ? clusterSeed
            : new object[]
            {
                new
                {
                    errorType = subtest == "listening" ? "distractor_confusion" : "detail_capture",
                    label = ObjectiveErrorTypeLabel(subtest == "listening" ? "distractor_confusion" : "detail_capture"),
                    count = 0,
                    subtest,
                    affectedQuestionIds = Array.Empty<string>()
                }
            };
    }

    private static object ObjectiveRecommendedDrill(string subtest, IEnumerable<object> errorClusters)
    {
        var firstCluster = errorClusters
            .Select(cluster => JsonSupport.Deserialize<Dictionary<string, object?>>(JsonSupport.Serialize(cluster), new Dictionary<string, object?>()))
            .FirstOrDefault();
        var errorType = firstCluster?.GetValueOrDefault("errorType")?.ToString() ?? (subtest == "listening" ? "distractor_confusion" : "detail_capture");
        var listeningDrillId = $"listening-drill-{errorType}";
        return new
        {
            id = subtest == "listening" ? listeningDrillId : $"{subtest}-drill-{errorType}",
            title = subtest == "listening" ? "Listening distractor drill" : "Reading exact-detail drill",
            rationale = $"Focus next on {ObjectiveErrorTypeLabel(errorType).ToLowerInvariant()} to strengthen your {ToDisplaySubtest(subtest)} accuracy.",
            route = subtest == "listening"
                ? $"/listening/drills/{Uri.EscapeDataString(listeningDrillId)}"
                : "/reading"
        };
    }

    private static object ObjectiveTranscriptAccess(IEnumerable<object> itemReview)
    {
        var items = itemReview
            .Select(item => JsonSupport.Deserialize<Dictionary<string, object?>>(JsonSupport.Serialize(item), new Dictionary<string, object?>()))
            .ToList();
        var allowedQuestionIds = items
            .Where(item =>
            {
                var transcript = ReadObject(item.GetValueOrDefault("transcript"));
                return transcript?.GetValueOrDefault("allowed")?.ToString() == bool.TrueString || string.Equals(transcript?.GetValueOrDefault("allowed")?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
            })
            .Select(item => item.GetValueOrDefault("questionId")?.ToString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();

        return new
        {
            policy = "per_item_post_attempt",
            state = allowedQuestionIds.Count == 0 ? "restricted" : "partial",
            allowedQuestionIds,
            reason = "Transcript snippets are revealed only on items that explicitly allow post-attempt transcript support."
        };
    }

    private static bool MatchesObjectiveAnswer(string? learnerAnswer, string? correctAnswer)
        => string.Equals(NormalizeObjectiveAnswer(learnerAnswer), NormalizeObjectiveAnswer(correctAnswer), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeObjectiveAnswer(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Trim().ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)).ToArray());

    private static string ObjectiveErrorType(string subtest, Dictionary<string, object?> question)
    {
        var type = question.GetValueOrDefault("type")?.ToString();
        if (subtest == "listening" && !string.IsNullOrWhiteSpace(question.GetValueOrDefault("distractorExplanation")?.ToString()))
        {
            return "distractor_confusion";
        }

        return (subtest, type) switch
        {
            ("reading", "mcq") => "named_concept_miss",
            ("reading", _) => "detail_capture",
            ("listening", _) => "detail_capture",
            _ => "accuracy"
        };
    }

    private static string ObjectiveErrorTypeLabel(string errorType) => errorType switch
    {
        "named_concept_miss" => "Named concept recognition",
        "distractor_confusion" => "Distractor control",
        "detail_capture" => "Exact detail capture",
        _ => "Accuracy"
    };

    private static string CriterionLabelFromCode(string? code) => code switch
    {
        "purpose" => "Purpose",
        "content" => "Content",
        "conciseness" => "Conciseness & Clarity",
        "conciseness_clarity" => "Conciseness & Clarity",
        "genre" => "Genre & Style",
        "genre_style" => "Genre & Style",
        "organization" => "Organisation & Layout",
        "organisation_layout" => "Organisation & Layout",
        "language" => "Language",
        "intelligibility" => "Intelligibility",
        "fluency" => "Fluency",
        "appropriateness" => "Appropriateness of Language",
        "grammar" => "Resources of Grammar & Expression",
        "grammarExpression" => "Resources of Grammar & Expression",
        "grammar_expression" => "Resources of Grammar & Expression",
        "relationshipBuilding" => "Relationship Building",
        "patientPerspective" => "Understanding & Incorporating Patient's Perspective",
        "providingStructure" => "Providing Structure",
        "structure" => "Providing Structure",
        "informationGathering" => "Information Gathering",
        "informationGiving" => "Information Giving",
        _ => code ?? "Criterion"
    };

    private static readonly string[] WritingReviewCriteria = ["purpose", "content", "conciseness", "genre", "organization", "language"];
    private static readonly string[] SpeakingReviewCriteria = ["intelligibility", "fluency", "appropriateness", "grammar_expression", "relationshipBuilding", "patientPerspective", "providingStructure", "informationGathering", "informationGiving"];

    private static IEnumerable<string> OrderReviewCriteria(string subtestCode, IEnumerable<string> availableCodes)
    {
        var available = new HashSet<string>(availableCodes, StringComparer.OrdinalIgnoreCase);
        var canonical = string.Equals(subtestCode, "writing", StringComparison.OrdinalIgnoreCase)
            ? WritingReviewCriteria
            : SpeakingReviewCriteria;
        foreach (var code in canonical)
        {
            if (available.Remove(code))
            {
                yield return code;
            }
        }

        foreach (var code in available.OrderBy(code => code, StringComparer.OrdinalIgnoreCase))
        {
            yield return code;
        }
    }

    private static int ReviewCriterionMaxScore(string subtestCode, string criterionCode)
    {
        if (string.Equals(subtestCode, "writing", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(criterionCode, "purpose", StringComparison.OrdinalIgnoreCase) ? 3 : 7;
        }

        return criterionCode is "relationshipBuilding" or "patientPerspective" or "providingStructure" or "informationGathering" or "informationGiving" ? 3 : 6;
    }

    private static int ParseCriterionScore(string? scoreRange)
    {
        if (string.IsNullOrWhiteSpace(scoreRange)) return 0;
        var digits = new string(scoreRange.TakeWhile(ch => char.IsDigit(ch) || ch == '-').ToArray());
        if (digits.Contains('-'))
        {
            var parts = digits.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var a) && int.TryParse(parts[1], out var b))
            {
                return (a + b) / 2;
            }
        }

        return int.TryParse(digits, out var value) ? value : 0;
    }

    private static string ScoreRangeToGrade(string? scoreRange)
    {
        var score = ParseCriterionScore(scoreRange);
        return score switch
        {
            >= 5 => "B+",
            4 => "B",
            3 => "C+",
            2 => "C",
            _ => "D"
        };
    }

    private static string ToAsyncState(AsyncState state) => state switch
    {
        AsyncState.Idle => "idle",
        AsyncState.Queued => "queued",
        AsyncState.Processing => "processing",
        AsyncState.Completed => "completed",
        AsyncState.Failed => "failed",
        _ => "unknown"
    };

    private static string ToStudyPlanItemState(StudyPlanItemStatus status) => status switch
    {
        StudyPlanItemStatus.NotStarted => "not_started",
        StudyPlanItemStatus.InProgress => "in_progress",
        StudyPlanItemStatus.Completed => "completed",
        StudyPlanItemStatus.Skipped => "skipped",
        StudyPlanItemStatus.Rescheduled => "rescheduled",
        _ => "unknown"
    };

    private static string ToReviewRequestState(ReviewRequestState status) => status switch
    {
        ReviewRequestState.Draft => "draft",
        ReviewRequestState.Submitted => "submitted",
        ReviewRequestState.AwaitingPayment => "awaiting_payment",
        ReviewRequestState.Queued => "queued",
        ReviewRequestState.InReview => "in_review",
        ReviewRequestState.Completed => "completed",
        ReviewRequestState.Failed => "failed",
        ReviewRequestState.Cancelled => "cancelled",
        _ => "unknown"
    };

    private static string ToSubscriptionState(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.Trial => "trial",
        SubscriptionStatus.Pending => "pending",
        SubscriptionStatus.Active => "active",
        SubscriptionStatus.PastDue => "past_due",
        SubscriptionStatus.Suspended => "suspended",
        SubscriptionStatus.Cancelled => "cancelled",
        SubscriptionStatus.Expired => "expired",
        SubscriptionStatus.Paused => "paused",
        SubscriptionStatus.FreezeRequested => "freeze_requested",
        SubscriptionStatus.Frozen => "frozen",
        // Never surface a literal "unknown" — fall back to the enum name so a future
        // status renders sensibly instead of looking broken on the billing page.
        _ => status.ToString().ToLowerInvariant()
    };

    private static string ToUploadState(UploadState status) => status switch
    {
        UploadState.Pending => "pending",
        UploadState.InProgress => "in_progress",
        UploadState.Uploaded => "uploaded",
        UploadState.Failed => "failed",
        _ => "unknown"
    };

    private static string ToContentStatus(ContentStatus status) => status switch
    {
        ContentStatus.Draft => "draft",
        ContentStatus.Published => "published",
        ContentStatus.Archived => "archived",
        _ => "unknown"
    };

    private static bool? ReadBool(object? value) => value switch
    {
        null => null,
        bool boolean => boolean,
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.False } => false,
        JsonElement { ValueKind: JsonValueKind.String } element when bool.TryParse(element.GetString(), out var parsed) => parsed,
        _ when bool.TryParse(value.ToString(), out var parsed) => parsed,
        _ => null
    };

    private static string AttemptFeedbackRoute(string subtest, string evaluationId) => subtest switch
    {
        "writing" => $"/writing/result?id={Uri.EscapeDataString(evaluationId)}",
        "speaking" => $"/speaking/result/{evaluationId}",
        "reading" => "/reading",
        "listening" => $"/listening/task/{evaluationId}",
        _ => $"/history/{evaluationId}"
    };

    // ── Engagement ──

    public async Task<object> GetEngagementAsync(string userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw ApiException.NotFound("user_not_found", "User not found.");

        var weeklyActivity = JsonSupport.Deserialize<bool[]>(user.WeeklyActivityJson ?? "[]", []);
        var daysLabels = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        return new
        {
            currentStreak = user.CurrentStreak,
            longestStreak = user.LongestStreak,
            lastPracticeDate = user.LastPracticeDate,
            totalPracticeMinutes = user.TotalPracticeMinutes,
            totalPracticeSessions = user.TotalPracticeSessions,
            avgSessionMinutes = user.TotalPracticeSessions > 0 ? user.TotalPracticeMinutes / user.TotalPracticeSessions : 0,
            weeklyActivity = weeklyActivity.Select((active, index) => new
            {
                day = index < daysLabels.Length ? daysLabels[index] : $"Day {index + 1}",
                active
            }),
            streakFreezeAvailable = true,
            streakFreezeUsedThisWeek = false
        };
    }

    // ── Wallet Transactions ──

    public object GetWalletTopUpTiers()
    {
        var tiers = walletService.GetConfiguredTopUpTiers();
        return new
        {
            currency = walletService.GetWalletCurrency(),
            tiers = tiers.Select(t => new
            {
                amount = t.Amount,
                credits = t.Credits,
                bonus = t.Bonus,
                totalCredits = t.Credits + t.Bonus,
                label = string.IsNullOrWhiteSpace(t.Label) ? $"${t.Amount}" : t.Label,
                isPopular = t.IsPopular,
            }).ToList(),
        };
    }

    public async Task<object> GetWalletTransactionsAsync(string userId, int limit, CancellationToken ct)
    {
        var wallet = await db.Wallets.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (wallet is null)
        {
            return new { balance = 0, transactions = Array.Empty<object>() };
        }

        var maxTransactions = Math.Min(limit, 100);
        var walletTransactions = await db.WalletTransactions.AsNoTracking()
            .Where(x => x.WalletId == wallet.Id)
            .ToListAsync(ct);

        var transactions = walletTransactions
            .OrderByDescending(x => x.CreatedAt)
            .Take(maxTransactions)
            .Select(x => new
            {
                id = x.Id,
                type = x.TransactionType,
                amount = x.Amount,
                balanceAfter = x.BalanceAfter,
                referenceType = x.ReferenceType,
                referenceId = x.ReferenceId,
                description = x.Description,
                createdAt = x.CreatedAt
            })
            .ToList();

        return new
        {
            balance = wallet.CreditBalance,
            lastUpdatedAt = wallet.LastUpdatedAt,
            transactions
        };
    }

    public async Task<object> CreateWalletTopUpAsync(string userId, WalletTopUpRequest request, CancellationToken ct)
    {
        await EnsureUserAsync(userId, ct);
        await EnsureLearnerMutationAllowedAsync(userId, ct);

        var gateway = (request.Gateway ?? string.Empty).Trim().ToLowerInvariant();
        if (gateway is not "stripe" and not "paypal")
        {
            throw ApiException.Validation(
                "invalid_gateway",
                "Choose stripe or paypal as payment gateway.",
                [new ApiFieldError("gateway", "invalid", "Select a supported payment method.")]);
        }

        var configuredTiers = walletService.GetConfiguredTopUpTiers();
        if (!configuredTiers.Any(t => t.Amount == request.Amount))
        {
            var validList = configuredTiers.Count == 0
                ? "currently unavailable"
                : string.Join(", ", configuredTiers.Select(t => $"${t.Amount}"));
            throw ApiException.Validation(
                "invalid_amount",
                $"Choose a valid top-up amount ({validList}).",
                [new ApiFieldError("amount", "invalid", "Select one of the available top-up amounts.")]);
        }

        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);
        var idempotencyRequestHash = idempotencyKey is null
            ? null
            : ComputeIdempotencyRequestHash(new { userId, request.Amount, gateway });
        if (idempotencyKey is not null && idempotencyRequestHash is not null)
        {
            var reservation = await ReservePaymentIdempotencyAsync("wallet-top-up", idempotencyKey, userId, idempotencyRequestHash, ct);
            if (!reservation.ShouldProcess)
            {
                return reservation.CachedResponse!;
            }
        }

        var providerRequestReturned = false;
        var idempotencyCompleted = false;
        object? idempotencyResponse = null;
        try
        {
        var session = await walletService.CreateTopUpSessionAsync(userId, request.Amount, gateway, idempotencyKey, ct);
        providerRequestReturned = true;
        var response = new
        {
            sessionId = session.SessionId,
            gateway = session.Gateway,
            amountAud = session.AmountDollars,
            creditsGranted = session.CreditsGranted,
            bonusCredits = session.BonusCredits,
            totalCredits = session.TotalCredits,
            checkoutUrl = session.CheckoutUrl,
            status = "pending_payment",
            expiresAt = session.ExpiresAt
        };

        idempotencyResponse = response;

        if (idempotencyKey is not null && idempotencyRequestHash is not null)
        {
            await CompletePaymentIdempotencyAsync("wallet-top-up", idempotencyKey, userId, idempotencyRequestHash, response, ct);
        }

        await db.SaveChangesAsync(ct);
        idempotencyCompleted = true;

        // Audit the successful top-up session creation so billing operators can
        // reconcile against payment-gateway events later.
        db.BillingEvents.Add(new BillingEvent
        {
            Id = $"bill-evt-{Guid.NewGuid():N}",
            UserId = userId,
            EventType = "wallet_top_up_session_created",
            EntityType = "WalletTopUpSession",
            EntityId = session.SessionId,
            PayloadJson = JsonSupport.Serialize(new
            {
                gateway = session.Gateway,
                amountDollars = session.AmountDollars,
                creditsGranted = session.CreditsGranted,
                bonusCredits = session.BonusCredits,
                totalCredits = session.TotalCredits
            }),
            OccurredAt = DateTimeOffset.UtcNow
        });
        await RecordEventAsync(userId, "wallet_top_up_started", new
        {
            sessionId = session.SessionId,
            gateway = session.Gateway,
            amountDollars = session.AmountDollars,
            totalCredits = session.TotalCredits
        }, ct);

        await db.SaveChangesAsync(ct);

        // Learners observe refund/payment status through billing invoices; wallet
        // top-up session creation intentionally returns only the checkout contract.
        return response;
        }
        catch
        {
            if (idempotencyKey is not null && idempotencyRequestHash is not null && !idempotencyCompleted)
            {
                if (idempotencyResponse is not null)
                {
                    await TryCompletePaymentIdempotencyAsync("wallet-top-up", idempotencyKey, userId, idempotencyRequestHash, idempotencyResponse, ct);
                }
                else if (!providerRequestReturned)
                {
                    await RemovePaymentIdempotencyReservationAsync("wallet-top-up", idempotencyKey, ct);
                }
            }

            throw;
        }
    }

    // ── Payment Webhooks ──

    public Task<object> HandleStripeWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        => HandlePaymentWebhookAsync("stripe", payload, headers, ct);

    public Task<object> HandlePayPalWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        => HandlePaymentWebhookAsync("paypal", payload, headers, ct);

    // Regional gateways. Each gateway's HandleWebhookAsync verifies its own
    // signature/HMAC; HandlePaymentWebhookAsync then runs the same idempotent
    // dedup + fulfillment as Stripe/PayPal. These only receive traffic once an
    // admin configures the gateway's credentials in Runtime Settings.
    public Task<object> HandleCheckoutComWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        => HandlePaymentWebhookAsync("checkoutcom", payload, headers, ct);

    public Task<object> HandlePaymobWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        => HandlePaymentWebhookAsync("paymob", payload, headers, ct);

    public Task<object> HandlePayTabsWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        => HandlePaymentWebhookAsync("paytabs", payload, headers, ct);

    public Task<object> HandleEasyKashWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        => HandlePaymentWebhookAsync("easykash", payload, headers, ct);

    public static bool IsRejectedWebhookOutcome(object outcome)
        => outcome.GetType().GetProperty("received")?.GetValue(outcome) is false;

    public static string? GetPaymentWebhookRetryBlockedReason(PaymentWebhookEvent evt)
    {
        if (!string.Equals(evt.ProcessingStatus, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return "Only failed local webhook processing attempts can be retried.";
        }

        if (!string.Equals(evt.VerificationStatus, "verified", StringComparison.OrdinalIgnoreCase) || evt.VerifiedAt is null)
        {
            return "This webhook was not signature-verified at ingestion. Ask the payment provider to redeliver it through the live webhook endpoint.";
        }

        if (string.IsNullOrWhiteSpace(evt.GatewayTransactionId))
        {
            return "This webhook does not have a trusted parsed payment transaction id.";
        }

        if (string.IsNullOrWhiteSpace(evt.NormalizedStatus))
        {
            return "This webhook does not have a trusted parsed payment status.";
        }

        if (!string.Equals(evt.ParserVersion, PaymentWebhookParserVersion, StringComparison.Ordinal))
        {
            return "This webhook was parsed by an unsupported parser version. Ask the payment provider to redeliver it through the live webhook endpoint.";
        }

        if (!IsValidSha256(evt.PayloadSha256))
        {
            return "This webhook does not have durable payload hash evidence from ingestion.";
        }

        if (!string.Equals(evt.NormalizedStatus, "completed", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(evt.NormalizedStatus, "failed", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(evt.NormalizedStatus, "refunded", StringComparison.OrdinalIgnoreCase))
            {
                return "Only completed, failed, or refunded payment status webhooks can be retried by admin.";
            }
        }

        return null;
    }

    public async Task<PaymentWebhookRetryResult> RetryVerifiedPaymentWebhookAsync(
        Guid eventId,
        string actorId,
        string actorName,
        CancellationToken ct)
    {
        var existing = await db.PaymentWebhookEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == eventId, ct)
            ?? throw ApiException.NotFound("webhook_not_found", "Webhook event not found.");

        var blockedReason = GetPaymentWebhookRetryBlockedReason(existing);
        if (blockedReason is not null)
        {
            throw ApiException.Conflict("webhook_not_retryable", blockedReason);
        }

        var now = DateTimeOffset.UtcNow;
        var adminId = TruncateForColumn(actorId, 64);
        var adminName = TruncateForColumn(actorName, 128);

        if (db.Database.IsInMemory())
        {
            var tracked = await db.PaymentWebhookEvents.FirstAsync(e => e.Id == eventId, ct);
            if (!string.Equals(tracked.ProcessingStatus, "failed", StringComparison.OrdinalIgnoreCase))
            {
                throw ApiException.Conflict("webhook_already_processing", "This webhook is no longer in a failed retryable state.");
            }

            tracked.ProcessingStatus = "processing";
            tracked.ErrorMessage = null;
            tracked.ProcessedAt = null;
            tracked.AttemptCount += 1;
            tracked.RetryCount += 1;
            tracked.LastAttemptedAt = now;
            tracked.LastRetriedAt = now;
            tracked.LastRetriedByAdminId = adminId;
            tracked.LastRetriedByAdminName = adminName;
            await db.SaveChangesAsync(ct);
        }
        else
        {
            var claimed = await db.PaymentWebhookEvents
                .Where(e => e.Id == eventId && e.ProcessingStatus == "failed")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(e => e.ProcessingStatus, "processing")
                    .SetProperty(e => e.ErrorMessage, (string?)null)
                    .SetProperty(e => e.ProcessedAt, (DateTimeOffset?)null)
                    .SetProperty(e => e.AttemptCount, e => e.AttemptCount + 1)
                    .SetProperty(e => e.RetryCount, e => e.RetryCount + 1)
                    .SetProperty(e => e.LastAttemptedAt, now)
                    .SetProperty(e => e.LastRetriedAt, now)
                    .SetProperty(e => e.LastRetriedByAdminId, adminId)
                    .SetProperty(e => e.LastRetriedByAdminName, adminName), ct);

            if (claimed == 0)
            {
                throw ApiException.Conflict("webhook_already_processing", "This webhook is no longer in a failed retryable state.");
            }

            db.ChangeTracker.Clear();
        }

        var retryEvent = await db.PaymentWebhookEvents.AsNoTracking().FirstAsync(e => e.Id == eventId, ct);
        var result = await ApplyVerifiedPaymentWebhookEventAsync(
            retryEvent.Id,
            retryEvent.GatewayTransactionId,
            retryEvent.NormalizedStatus,
            InferWebhookCategory(retryEvent.EventType),
            retryEvent.GatewayEventId,
            ct);

        return new PaymentWebhookRetryResult(
            retryEvent.Id.ToString(),
            BuildWebhookRetryStatus(result.ProcessingStatus),
            result.ProcessingStatus,
            result.ErrorMessage,
            result.AttemptCount,
            result.RetryCount,
            result.GatewayTransactionId,
            result.NormalizedStatus);
    }

    private async Task<object> HandlePaymentWebhookAsync(
        string gatewayName,
        string payload,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct)
    {
        var receivedAt = DateTimeOffset.UtcNow;
        WebhookProcessResult result;
        try
        {
            result = await paymentGateways.GetGateway(gatewayName).HandleWebhookAsync(payload, headers, ct);
        }
        catch (Exception ex)
        {
            result = new WebhookProcessResult(
                EventId: $"{gatewayName}-error-{Guid.NewGuid():N}",
                EventType: "handler_exception",
                Processed: false,
                Error: ex.Message);
        }

        if (!result.Processed)
        {
            return new
            {
                received = false,
                gateway = gatewayName,
                eventId = result.EventId,
                eventType = result.EventType,
                error = result.Error ?? "Webhook verification failed.",
                state = "rejected"
            };
        }

        var webhookEvent = await db.PaymentWebhookEvents
            .FirstOrDefaultAsync(x => x.Gateway == gatewayName && x.GatewayEventId == result.EventId, ct);

        if (webhookEvent is not null && webhookEvent.ProcessingStatus is "completed" or "ignored")
        {
            return new
            {
                received = true,
                duplicate = true,
                gateway = gatewayName,
                eventId = webhookEvent.GatewayEventId,
                eventType = webhookEvent.EventType,
                state = webhookEvent.ProcessingStatus
            };
        }

        if (webhookEvent is not null && webhookEvent.ProcessingStatus == "processing")
        {
            var lastAttemptedAt = webhookEvent.LastAttemptedAt ?? webhookEvent.ReceivedAt;
            if (lastAttemptedAt > receivedAt.Subtract(PaymentWebhookProcessingLease))
            {
                return new
                {
                    received = true,
                    duplicate = true,
                    gateway = gatewayName,
                    eventId = webhookEvent.GatewayEventId,
                    eventType = webhookEvent.EventType,
                    state = webhookEvent.ProcessingStatus
                };
            }
        }

        var isNewWebhookEvent = webhookEvent is null;
        webhookEvent ??= new PaymentWebhookEvent
        {
            Id = Guid.NewGuid(),
            Gateway = gatewayName,
            GatewayEventId = result.EventId,
            ReceivedAt = receivedAt
        };

        webhookEvent.EventType = result.EventType;
        webhookEvent.PayloadJson = result.Processed ? result.SafePayloadJson ?? "{}" : "{}";
        webhookEvent.PayloadSha256 = ComputePayloadSha256(payload);
        webhookEvent.ParserVersion = PaymentWebhookParserVersion;
        webhookEvent.VerificationStatus = result.Processed ? "verified" : "failed";
        webhookEvent.VerifiedAt = result.Processed ? receivedAt : null;
        webhookEvent.GatewayTransactionId = result.GatewayTransactionId;
        webhookEvent.NormalizedStatus = result.NormalizedStatus;
        webhookEvent.AttemptCount += 1;
        webhookEvent.LastAttemptedAt = receivedAt;
        webhookEvent.ErrorMessage = result.Processed ? null : result.Error ?? "Webhook verification failed.";
        webhookEvent.ProcessingStatus = result.Processed ? "processing" : ResolveWebhookFailureStatus(webhookEvent.AttemptCount);
        webhookEvent.ProcessedAt = result.Processed ? null : DateTimeOffset.UtcNow;
        if (db.Entry(webhookEvent).State == EntityState.Detached)
        {
            db.PaymentWebhookEvents.Add(webhookEvent);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (isNewWebhookEvent)
        {
            db.ChangeTracker.Clear();
            var existingWebhookEvent = await db.PaymentWebhookEvents
                .FirstOrDefaultAsync(x => x.Gateway == gatewayName && x.GatewayEventId == result.EventId, ct);
            if (existingWebhookEvent is null)
            {
                throw;
            }

            return new
            {
                received = true,
                duplicate = true,
                gateway = gatewayName,
                eventId = existingWebhookEvent.GatewayEventId,
                eventType = existingWebhookEvent.EventType,
                state = existingWebhookEvent.ProcessingStatus
            };
        }

        var applied = await ApplyVerifiedPaymentWebhookEventAsync(
            webhookEvent.Id,
            result.GatewayTransactionId,
            result.NormalizedStatus,
            result.EventCategory,
            result.GatewayObjectId,
            ct);

        return new
        {
            received = true,
            gateway = gatewayName,
            eventId = result.EventId,
            eventType = result.EventType,
            gatewayTransactionId = applied.GatewayTransactionId,
            normalizedStatus = applied.NormalizedStatus,
            error = applied.ErrorMessage,
            state = applied.ProcessingStatus
        };
    }

    internal async Task<PaymentWebhookRetryResult> ApplyVerifiedPaymentWebhookEventAsync(
        Guid eventId,
        string? gatewayTransactionId,
        string? normalizedStatus,
        string? eventCategory,
        string? gatewayObjectId,
        CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        try
        {
            var webhookEvent = await db.PaymentWebhookEvents.FirstAsync(e => e.Id == eventId, ct);
            var now = DateTimeOffset.UtcNow;

            if (string.IsNullOrWhiteSpace(gatewayTransactionId))
            {
                webhookEvent.ProcessingStatus = "ignored";
                webhookEvent.ErrorMessage = "No checkout or payment transaction id was included in the webhook payload.";
                webhookEvent.ProcessedAt = now;
                await db.SaveChangesAsync(ct);
                await CommitIfOwnedAsync(tx, ct);
                return MapWebhookRetryResult(webhookEvent);
            }

            var privateSpeakingTargetStatus = string.IsNullOrWhiteSpace(normalizedStatus)
                ? "pending"
                : normalizedStatus.Trim().ToLowerInvariant();
            if (await ApplyPrivateSpeakingWebhookIfMatchedAsync(webhookEvent, gatewayTransactionId, privateSpeakingTargetStatus, ct))
            {
                await CommitIfOwnedAsync(tx, ct);
                return MapWebhookRetryResult(webhookEvent);
            }

            if (await ApplyLegacyBillingWebhookIfMatchedAsync(webhookEvent, gatewayTransactionId, privateSpeakingTargetStatus, ct))
            {
                await CommitIfOwnedAsync(tx, ct);
                return MapWebhookRetryResult(webhookEvent);
            }

            if (await ApplyCartCheckoutWebhookIfMatchedAsync(webhookEvent, gatewayTransactionId, privateSpeakingTargetStatus, ct))
            {
                await CommitIfOwnedAsync(tx, ct);
                return MapWebhookRetryResult(webhookEvent);
            }

            var paymentTransaction = await db.PaymentTransactions
                .FirstOrDefaultAsync(x => x.GatewayTransactionId == gatewayTransactionId
                    || (x.MetadataJson != null && x.MetadataJson.Contains(gatewayTransactionId)), ct);

            if (paymentTransaction is null)
            {
                webhookEvent.ProcessingStatus = "ignored";
                webhookEvent.ErrorMessage = $"Payment transaction '{gatewayTransactionId}' was not found.";
                webhookEvent.ProcessedAt = now;
                await db.SaveChangesAsync(ct);
                await CommitIfOwnedAsync(tx, ct);
                return MapWebhookRetryResult(webhookEvent);
            }

            var targetStatus = string.IsNullOrWhiteSpace(normalizedStatus)
                ? paymentTransaction.Status
                : normalizedStatus.Trim().ToLowerInvariant();

            if (string.Equals(eventCategory, PaymentWebhookCategories.Refund, StringComparison.OrdinalIgnoreCase)
                || string.Equals(targetStatus, "refunded", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(targetStatus, "refunded", StringComparison.OrdinalIgnoreCase)
                    && IsFullRefundWebhook(webhookEvent.PayloadJson, paymentTransaction.Amount))
                {
                    await ApplyCheckoutRefundAsync(paymentTransaction, webhookEvent.Id.ToString("N"), ct);
                    paymentTransaction.Status = "refunded";
                    paymentTransaction.UpdatedAt = now;
                }
                else
                {
                    await ApplyPartialCheckoutRefundAsync(paymentTransaction, webhookEvent, gatewayObjectId, ct);
                }

                webhookEvent.ProcessingStatus = "completed";
                webhookEvent.ErrorMessage = null;
                webhookEvent.ProcessedAt = now;
                await db.SaveChangesAsync(ct);
                await CommitIfOwnedAsync(tx, ct);
                return MapWebhookRetryResult(webhookEvent);
            }

            if (string.Equals(eventCategory, PaymentWebhookCategories.Dispute, StringComparison.OrdinalIgnoreCase)
                && disputeService is not null
                && targetStatus.StartsWith("dispute_", StringComparison.OrdinalIgnoreCase))
            {
                await disputeService.RecordSignalAsync(new DisputeWebhookSignal(
                    paymentTransaction.Gateway,
                    string.IsNullOrWhiteSpace(gatewayObjectId) ? webhookEvent.GatewayEventId : gatewayObjectId,
                    paymentTransaction.GatewayTransactionId,
                    targetStatus,
                    paymentTransaction.Amount,
                    paymentTransaction.Currency,
                    webhookEvent.EventType), ct);
                webhookEvent.ProcessingStatus = "completed";
                webhookEvent.ErrorMessage = null;
                webhookEvent.ProcessedAt = now;
                await db.SaveChangesAsync(ct);
                await CommitIfOwnedAsync(tx, ct);
                return MapWebhookRetryResult(webhookEvent);
            }

            if (string.Equals(paymentTransaction.Status, "completed", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(targetStatus, "completed", StringComparison.OrdinalIgnoreCase))
            {
                webhookEvent.ProcessingStatus = "ignored";
                webhookEvent.ErrorMessage = "Payment transaction is already completed; webhook status was not downgraded.";
                webhookEvent.ProcessedAt = now;
                await db.SaveChangesAsync(ct);
                await CommitIfOwnedAsync(tx, ct);
                return MapWebhookRetryResult(webhookEvent);
            }

            paymentTransaction.Status = targetStatus;
            paymentTransaction.UpdatedAt = now;

            switch (targetStatus)
            {
                case "completed" when string.Equals(paymentTransaction.TransactionType, "wallet_top_up", StringComparison.OrdinalIgnoreCase):
                    await ApplyWalletTopUpCompletionAsync(paymentTransaction, ct);
                    webhookEvent.ProcessingStatus = "completed";
                    break;

                case "completed":
                    await ApplyCheckoutCompletionAsync(paymentTransaction, ct);
                    webhookEvent.ProcessingStatus = "completed";
                    break;

                case "failed":
                    await MarkCheckoutFailedAsync(paymentTransaction, ct);
                    webhookEvent.ProcessingStatus = "completed";
                    break;

                default:
                    webhookEvent.ProcessingStatus = "completed";
                    break;
            }

            webhookEvent.ErrorMessage = null;
            webhookEvent.ProcessedAt = now;
            await db.SaveChangesAsync(ct);
            await CommitIfOwnedAsync(tx, ct);
            return MapWebhookRetryResult(webhookEvent);
        }
        catch (Exception ex)
        {
            if (tx is not null)
            {
                await tx.RollbackAsync(ct);
            }

            db.ChangeTracker.Clear();
            var webhookEvent = await db.PaymentWebhookEvents.FirstAsync(e => e.Id == eventId, ct);
            webhookEvent.ProcessingStatus = ResolveWebhookFailureStatus(webhookEvent.AttemptCount);
            webhookEvent.ErrorMessage = ex.Message;
            webhookEvent.ProcessedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return MapWebhookRetryResult(webhookEvent);
        }
    }

    /// <summary>
    /// Synchronous fulfilment entry for PayPal Expanded (embedded) checkout. The browser
    /// SDK approves an order and posts its id here; we capture server-side and run the SAME
    /// idempotent grant logic the webhook uses, so the capture and a later
    /// PAYMENT.CAPTURE.COMPLETED webhook converge without double-granting. Owner-scoped:
    /// the caller must own the underlying payment transaction or speaking booking. Cart
    /// checkouts capture through the cart service, not here.
    /// </summary>
    internal async Task<PaymentCaptureResult> FulfillCapturedOrderAsync(
        string gateway,
        string userId,
        string orderId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            throw ApiException.Validation("order_required", "An order id is required to complete the payment.");
        }

        var transaction = await db.PaymentTransactions.FirstOrDefaultAsync(
            x => x.LearnerUserId == userId
                && (x.GatewayTransactionId == orderId || (x.MetadataJson != null && x.MetadataJson.Contains(orderId))),
            ct);

        var speakingBooking = transaction is null
            ? await db.PrivateSpeakingBookings.FirstOrDefaultAsync(
                b => (b.StripeCheckoutSessionId == orderId || b.PaymentGatewayOrderId == orderId)
                    && b.LearnerUserId == userId, ct)
            : null;

        // Cart (embedded PayPal) checkout: a paypal-gateway CheckoutSession keyed on the
        // approved order id. Same owner scope so a guessed order id can't be confirmed
        // against another learner's cart.
        var cartSession = (transaction is null && speakingBooking is null)
            ? await db.CheckoutSessions.FirstOrDefaultAsync(
                s => s.GatewayOrderId == orderId && s.UserId == userId && s.Gateway == "paypal", ct)
            : null;

        if (transaction is null && speakingBooking is null && cartSession is null)
        {
            // Unknown order, or it belongs to another learner — same opaque 404 either way
            // so a guessed order id can never be confirmed against someone else's purchase.
            throw ApiException.NotFound("order_not_found", "We couldn't find that payment to complete.");
        }

        // Idempotent short-circuit: capture or the webhook already fulfilled this order.
        if (transaction is not null && string.Equals(transaction.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return new PaymentCaptureResult("completed", orderId, transaction.CaptureId, ComputeCaptureRedirect(transaction), null);
        }
        if (speakingBooking is not null && speakingBooking.PaymentStatus == PrivateSpeakingPaymentStatus.Succeeded)
        {
            return new PaymentCaptureResult("completed", orderId, speakingBooking.StripePaymentIntentId, "/speaking/private", null);
        }
        if (cartSession is not null && string.Equals(cartSession.Status, "fulfilled", StringComparison.OrdinalIgnoreCase))
        {
            return new PaymentCaptureResult("completed", orderId, null, "/dashboard?purchase=success", null);
        }

        // Capture against the gateway OUTSIDE any DB transaction (never hold a db lock across
        // a network round-trip). PayPal-Request-Id keyed on the order id makes this idempotent,
        // so a retried onApprove or a webhook racing the capture cannot double-charge.
        CaptureResult capture;
        try
        {
            capture = await paymentGateways.GetGateway(gateway).CaptureOrderAsync(orderId, $"capture-{orderId}", ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "gateway_unavailable",
                "This payment method is temporarily unavailable. Please pay by card instead.",
                [new ApiFieldError("gateway", "unavailable", "Choose a different payment method.")]);
        }
        catch (PaymentGatewayApiException)
        {
            throw ApiException.ServiceUnavailable(
                "payment_gateway_error",
                "We couldn't complete your payment right now. Please try again in a moment or choose another payment method.",
                retryable: true);
        }

        var now = DateTimeOffset.UtcNow;
        if (!string.Equals(capture.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            // Declined / not-yet-settled capture. Mark the txn failed so the learner can retry,
            // and surface a clean, retryable error to the embedded UI.
            if (transaction is not null)
            {
                transaction.Status = "failed";
                transaction.UpdatedAt = now;
                await MarkCheckoutFailedAsync(transaction, ct);
                await db.SaveChangesAsync(ct);
            }
            else if (cartSession is not null && !string.Equals(cartSession.Status, "fulfilled", StringComparison.OrdinalIgnoreCase))
            {
                cartSession.Status = "failed";
                cartSession.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
            }

            return new PaymentCaptureResult("failed", orderId, capture.CaptureId, null, "payment_not_completed");
        }

        // Capture succeeded — run the idempotent grant inside a transaction.
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        try
        {
            if (speakingBooking is not null)
            {
                var confirmed = await privateSpeakingService!.ConfirmBookingPaymentAsync(orderId, capture.CaptureId, ct);
                if (!confirmed)
                {
                    throw new InvalidOperationException($"No private-speaking booking found for order {orderId}.");
                }

                await CommitIfOwnedAsync(tx, ct);
                return new PaymentCaptureResult("completed", orderId, capture.CaptureId, "/speaking/private", null);
            }

            if (cartSession is not null)
            {
                if (fulfillmentService is null)
                {
                    throw new InvalidOperationException("Billing fulfillment service is not available.");
                }

                // Same idempotent grant the PAYMENT.CAPTURE.COMPLETED webhook runs, keyed on
                // the order id, so capture and the webhook converge without double-granting.
                await fulfillmentService.FulfillCartByGatewayOrderAsync(orderId, ct);
                await CommitIfOwnedAsync(tx, ct);
                return new PaymentCaptureResult("completed", orderId, capture.CaptureId, "/dashboard?purchase=success", null);
            }

            transaction!.Status = "completed";
            transaction.CaptureId = capture.CaptureId;
            transaction.UpdatedAt = now;

            if (string.Equals(transaction.TransactionType, "wallet_top_up", StringComparison.OrdinalIgnoreCase))
            {
                await ApplyWalletTopUpCompletionAsync(transaction, ct);
            }
            else
            {
                await ApplyCheckoutCompletionAsync(transaction, ct);
            }

            await db.SaveChangesAsync(ct);
            await CommitIfOwnedAsync(tx, ct);
            return new PaymentCaptureResult("completed", orderId, capture.CaptureId, ComputeCaptureRedirect(transaction), null);
        }
        catch
        {
            if (tx is not null)
            {
                await tx.RollbackAsync(ct);
            }
            throw;
        }
    }

    /// <summary>Best-effort post-purchase destination mirroring the payment-return routing.</summary>
    private static string ComputeCaptureRedirect(PaymentTransaction transaction)
    {
        if (string.Equals(transaction.TransactionType, "wallet_top_up", StringComparison.OrdinalIgnoreCase))
        {
            return "/billing?tab=credits";
        }

        if (string.Equals(transaction.ProductType, "addon", StringComparison.OrdinalIgnoreCase))
        {
            // pkg_* add-ons are AI-credit bundles; route to that tab.
            var isAiPackage = transaction.MetadataJson?.Contains("pkg_", StringComparison.OrdinalIgnoreCase) ?? false;
            return isAiPackage ? "/billing?tab=ai-credits" : "/billing?tab=credits";
        }

        return "/dashboard?purchase=success";
    }

    private async Task<bool> ApplyPrivateSpeakingWebhookIfMatchedAsync(
        PaymentWebhookEvent webhookEvent,
        string checkoutSessionId,
        string targetStatus,
        CancellationToken ct)
    {
        // Match a Stripe checkout session (hosted) or a PayPal order id (embedded) — both
        // uniquely identify one booking and route through the same ConfirmBookingPaymentAsync.
        var bookingExists = await db.PrivateSpeakingBookings
            .AnyAsync(booking => booking.StripeCheckoutSessionId == checkoutSessionId
                || booking.PaymentGatewayOrderId == checkoutSessionId, ct);
        if (!bookingExists)
        {
            return false;
        }

        if (privateSpeakingService is null)
        {
            throw new InvalidOperationException("Private speaking payment service is not available.");
        }

        var now = DateTimeOffset.UtcNow;
        switch (targetStatus)
        {
            case "completed":
            {
                var confirmed = await privateSpeakingService.ConfirmBookingPaymentAsync(
                    checkoutSessionId,
                    ExtractStripePaymentIntentId(webhookEvent.PayloadJson),
                    ct);
                if (!confirmed)
                {
                    throw new InvalidOperationException($"No private-speaking booking found for Stripe checkout session {checkoutSessionId}.");
                }

                webhookEvent.ProcessingStatus = "completed";
                webhookEvent.ErrorMessage = null;
                webhookEvent.ProcessedAt = now;
                await db.SaveChangesAsync(ct);
                return true;
            }

            case "failed":
                if (string.Equals(webhookEvent.EventType, "checkout.session.expired", StringComparison.OrdinalIgnoreCase))
                {
                    await privateSpeakingService.HandleCheckoutExpiredAsync(checkoutSessionId, ct);
                }
                else
                {
                    await privateSpeakingService.HandlePaymentFailureAsync(checkoutSessionId, ct);
                }

                webhookEvent.ProcessingStatus = "completed";
                webhookEvent.ErrorMessage = null;
                webhookEvent.ProcessedAt = now;
                await db.SaveChangesAsync(ct);
                return true;

            default:
                webhookEvent.ProcessingStatus = "ignored";
                webhookEvent.ErrorMessage = "Private speaking checkout payment is not settled yet; waiting for a terminal Stripe event.";
                webhookEvent.ProcessedAt = now;
                await db.SaveChangesAsync(ct);
                return true;
        }
    }

    private async Task<bool> ApplyLegacyBillingWebhookIfMatchedAsync(
        PaymentWebhookEvent webhookEvent,
        string gatewayObjectId,
        string targetStatus,
        CancellationToken ct)
    {
        if (IsStripeInvoiceWebhook(webhookEvent.EventType))
        {
            if (targetStatus == "completed")
            {
                if (fulfillmentService is null)
                {
                    throw new InvalidOperationException("Billing fulfillment service is not available.");
                }

                await fulfillmentService.FulfillRenewalAsync(gatewayObjectId, ct);
                webhookEvent.ProcessingStatus = "completed";
                webhookEvent.ErrorMessage = null;
            }
            else
            {
                webhookEvent.ProcessingStatus = "ignored";
                webhookEvent.ErrorMessage = "Stripe invoice webhook is not a paid renewal event.";
            }

            webhookEvent.ProcessedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return true;
        }

        if (!IsStripeCheckoutSessionWebhook(webhookEvent.EventType))
        {
            return false;
        }

        var checkoutSession = await db.CheckoutSessions
            .FirstOrDefaultAsync(session => session.StripeSessionId == gatewayObjectId, ct);
        if (checkoutSession is null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        switch (targetStatus)
        {
            case "completed":
                if (fulfillmentService is null)
                {
                    throw new InvalidOperationException("Billing fulfillment service is not available.");
                }

                await fulfillmentService.FulfillCheckoutAsync(gatewayObjectId, ct);
                webhookEvent.ProcessingStatus = "completed";
                webhookEvent.ErrorMessage = null;
                break;

            case "failed":
                if (!string.Equals(checkoutSession.Status, "fulfilled", StringComparison.OrdinalIgnoreCase))
                {
                    checkoutSession.Status = string.Equals(webhookEvent.EventType, "checkout.session.expired", StringComparison.OrdinalIgnoreCase)
                        ? "expired"
                        : "failed";
                    checkoutSession.UpdatedAt = now;
                    checkoutSession.ExpiresAt ??= now;
                }

                webhookEvent.ProcessingStatus = "completed";
                webhookEvent.ErrorMessage = null;
                break;

            default:
                webhookEvent.ProcessingStatus = "ignored";
                webhookEvent.ErrorMessage = "Stripe checkout session payment is not settled yet; waiting for a terminal Stripe event.";
                break;
        }

        webhookEvent.ProcessedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Webhook backstop for PayPal (embedded) cart checkouts. A PAYMENT.CAPTURE.COMPLETED
    /// event carries the order id as the transaction id; we map it to the paypal-gateway
    /// CheckoutSession and run the SAME idempotent fulfilment the capture endpoint runs, so
    /// the two converge without double-granting. Returns false when no cart session matches
    /// (so the caller falls through to the PaymentTransaction path).
    /// </summary>
    private async Task<bool> ApplyCartCheckoutWebhookIfMatchedAsync(
        PaymentWebhookEvent webhookEvent,
        string gatewayOrderId,
        string targetStatus,
        CancellationToken ct)
    {
        var cartSession = await db.CheckoutSessions
            .FirstOrDefaultAsync(s => s.GatewayOrderId == gatewayOrderId && s.Gateway == "paypal", ct);
        if (cartSession is null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        switch (targetStatus)
        {
            case "completed":
                if (fulfillmentService is null)
                {
                    throw new InvalidOperationException("Billing fulfillment service is not available.");
                }

                await fulfillmentService.FulfillCartByGatewayOrderAsync(gatewayOrderId, ct);
                webhookEvent.ProcessingStatus = "completed";
                webhookEvent.ErrorMessage = null;
                break;

            case "failed":
                if (!string.Equals(cartSession.Status, "fulfilled", StringComparison.OrdinalIgnoreCase))
                {
                    cartSession.Status = "failed";
                    cartSession.UpdatedAt = now;
                }

                webhookEvent.ProcessingStatus = "completed";
                webhookEvent.ErrorMessage = null;
                break;

            default:
                webhookEvent.ProcessingStatus = "ignored";
                webhookEvent.ErrorMessage = "PayPal cart order is not settled yet; waiting for a capture event.";
                break;
        }

        webhookEvent.ProcessedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static bool IsStripeCheckoutSessionWebhook(string? eventType)
        => eventType is not null
           && eventType.StartsWith("checkout.session.", StringComparison.OrdinalIgnoreCase);

    private static bool IsStripeInvoiceWebhook(string? eventType)
        => eventType is not null
           && eventType.StartsWith("invoice.", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractStripePaymentIntentId(string? safePayloadJson)
    {
        if (string.IsNullOrWhiteSpace(safePayloadJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(safePayloadJson);
            if (doc.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("object", out var obj))
            {
                return PaymentGatewayJson.GetString(obj, "payment_intent");
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfNeededAsync(CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return null;
        if (db.Database.IsInMemory()) return null;
        return await db.Database.BeginTransactionAsync(ct);
    }

    private static async Task CommitIfOwnedAsync(IDbContextTransaction? tx, CancellationToken ct)
    {
        if (tx is not null) await tx.CommitAsync(ct);
    }

    private static PaymentWebhookRetryResult MapWebhookRetryResult(PaymentWebhookEvent evt)
        => new(
            evt.Id.ToString(),
            BuildWebhookRetryStatus(evt.ProcessingStatus),
            evt.ProcessingStatus,
            evt.ErrorMessage,
            evt.AttemptCount,
            evt.RetryCount,
            evt.GatewayTransactionId,
            evt.NormalizedStatus);

    private static string BuildWebhookRetryStatus(string processingStatus)
        => processingStatus switch
        {
            "completed" => "reprocessed",
            "ignored" => "no_effect",
            "failed" => "still_failed",
            _ => processingStatus
        };

    private string ResolveWebhookFailureStatus(int attemptCount)
        => attemptCount >= Math.Max(1, billingOptions?.Value.WebhookMaxAttempts ?? 5)
            ? "dead_letter"
            : "failed";

    private static string InferWebhookCategory(string eventType)
    {
        if (eventType.Contains("dispute", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentWebhookCategories.Dispute;
        }

        if (eventType.Contains("refund", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentWebhookCategories.Refund;
        }

        return PaymentWebhookCategories.Payment;
    }

    private static string ComputePayloadSha256(string payload)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

    private static bool IsValidSha256(string? value)
        => value?.Length == 64 && value.All(Uri.IsHexDigit);

    private async Task ApplyWalletTopUpCompletionAsync(PaymentTransaction transaction, CancellationToken ct)
    {
        var metadata = ReadObject(JsonSupport.Deserialize<object?>(transaction.MetadataJson ?? "{}", null));
        var credits = ReadInt(metadata?.GetValueOrDefault("credits")) ?? 0;
        var bonus = ReadInt(metadata?.GetValueOrDefault("bonus")) ?? 0;
        var totalCredits = ReadInt(metadata?.GetValueOrDefault("totalCredits")) ?? credits + bonus;
        if (totalCredits <= 0)
        {
            return;
        }

        await CreditWalletForPaymentAsync(
            transaction.LearnerUserId,
            totalCredits,
            "top_up",
            "payment",
            transaction.GatewayTransactionId,
            $"Wallet top-up: {totalCredits} credits",
            ct);

        var invoiceId = TruncateIdentifier($"inv-topup-{transaction.GatewayTransactionId}");
        var existingInvoice = await db.Invoices.FirstOrDefaultAsync(x => x.Id == invoiceId, ct);
        if (existingInvoice is null)
        {
            db.Invoices.Add(new Invoice
            {
                Id = invoiceId,
                UserId = transaction.LearnerUserId,
                IssuedAt = DateTimeOffset.UtcNow,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Status = "Paid",
                Description = $"Wallet top-up: {credits} credits + {bonus} bonus credits",
                Number = await AllocateInvoiceNumberAsync(transaction.LearnerUserId, invoiceId, ct),
                CheckoutSessionId = transaction.GatewayTransactionId
            });
        }
        else
        {
            existingInvoice.CheckoutSessionId ??= transaction.GatewayTransactionId;
        }

        await AddBillingEventIfMissingAsync(new BillingEvent
        {
            Id = $"bill-evt-{Guid.NewGuid():N}",
            UserId = transaction.LearnerUserId,
            EventType = "wallet_top_up_completed",
            EntityType = "PaymentTransaction",
            EntityId = transaction.GatewayTransactionId,
            PayloadJson = JsonSupport.Serialize(new
            {
                totalCredits,
                credits,
                bonus,
                amount = transaction.Amount,
                currency = transaction.Currency,
                gateway = transaction.Gateway
            }),
            OccurredAt = DateTimeOffset.UtcNow
        }, ct);
    }

    /// <summary>
    /// Delivery method for a purchased plan, read from the immutable BillingPlanVersion
    /// locked to the quote so a mid-flight admin edit cannot change how an already-paid
    /// order is delivered. Falls back to the live plan, then to automatic_web.
    /// </summary>
    private async Task<string> ResolvePlanDeliveryMethodAsync(string? planVersionId, string? planCode, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(planVersionId))
        {
            var version = await db.BillingPlanVersions.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == planVersionId, ct);
            if (version is not null)
            {
                return DeliveryMethods.IsValid(version.DeliveryMethod)
                    ? version.DeliveryMethod
                    : DeliveryMethods.AutomaticWeb;
            }
        }

        if (!string.IsNullOrWhiteSpace(planCode))
        {
            var livePlan = await FindBillingPlanAsync(planCode, ct);
            if (livePlan is not null)
            {
                return DeliveryMethods.IsValid(livePlan.DeliveryMethod)
                    ? livePlan.DeliveryMethod
                    : DeliveryMethods.AutomaticWeb;
            }
        }

        return DeliveryMethods.AutomaticWeb;
    }

    /// <summary>
    /// Mint the system receipt that stands in for a learner-uploaded proof file on a card
    /// gateway order, so every order carries exactly one proof row for the admin dashboard.
    /// Idempotent on PaymentTransactionId (completion fires from both the webhook and the
    /// synchronous capture path). Best-effort by design: the learner has already paid, so a
    /// bookkeeping failure must never block activation.
    /// </summary>
    private async Task TryWriteGatewayReceiptAsync(
        PaymentTransaction transaction,
        BillingQuote quote,
        string courseName,
        CancellationToken ct)
    {
        if (manualPaymentService is null)
        {
            return;
        }

        try
        {
            await manualPaymentService.CreateGatewayReceiptAsync(
                transaction.LearnerUserId,
                transaction,
                courseName,
                quote.PlanCode,
                ct);
        }
        catch (Exception ex)
        {
            logger?.LogError(
                ex,
                "Failed to write gateway payment receipt for transaction {GatewayTransactionId} (quote {QuoteId}). Access was granted regardless.",
                transaction.GatewayTransactionId,
                quote.Id);
        }
    }

    private async Task ApplyCheckoutCompletionAsync(PaymentTransaction transaction, CancellationToken ct)
    {
        var quote = await GetQuoteForTransactionAsync(transaction, ct);
        if (quote is null || quote.Status == BillingQuoteStatus.Completed)
        {
            return;
        }

        var user = await EnsureUserAsync(transaction.LearnerUserId, ct);
        var subscription = !string.IsNullOrWhiteSpace(quote.SubscriptionId)
            ? await db.Subscriptions.FirstOrDefaultAsync(x => x.UserId == transaction.LearnerUserId && x.Id == quote.SubscriptionId, ct)
            : await db.Subscriptions.FirstOrDefaultAsync(x => x.UserId == transaction.LearnerUserId, ct);
        if (subscription is null)
        {
            throw ApiException.Validation(
                "billing_quote_parent_missing",
                "The enrolment selected for this checkout could not be found.");
        }
        var quoteResponse = DeserializeQuoteResponse(quote);
        var catalogSnapshot = DeserializeQuoteCatalogSnapshot(quote);
        var addOnVersionIds = DeserializeAddOnVersionIds(quote);
        var now = DateTimeOffset.UtcNow;
        EnsureQuoteIsFulfillable(quote, now);
        await EnsureQuoteSnapshotMatchesCurrentCatalogAsync(quote, ct);

        if (!string.IsNullOrWhiteSpace(quote.PlanCode) && string.Equals(transaction.TransactionType, "subscription_payment", StringComparison.OrdinalIgnoreCase))
        {
            var targetPlan = catalogSnapshot?.Plan;
            if (targetPlan is null)
            {
                var livePlan = await FindBillingPlanAsync(quote.PlanCode, ct);
                if (livePlan is not null)
                {
                    targetPlan = new BillingQuotePlanSnapshot
                    {
                        Code = livePlan.Code,
                        Name = livePlan.Name,
                        Price = livePlan.Price,
                        Currency = livePlan.Currency,
                        Interval = livePlan.Interval,
                        DurationMonths = livePlan.DurationMonths,
                        IncludedCredits = livePlan.IncludedCredits,
                        BundledAiCredits = livePlan.BundledAiCredits
                    };
                }
            }

            if (targetPlan is not null)
            {
                subscription.PlanId = targetPlan.Code;
                subscription.PlanVersionId = quote.PlanVersionId;

                // Delivery method decides whether payment alone releases access. The quote
                // snapshot carries pricing only, so read it from the immutable version row
                // locked to this purchase, falling back to the live plan.
                var deliveryMethod = await ResolvePlanDeliveryMethodAsync(quote.PlanVersionId, quote.PlanCode, ct);
                if (DeliveryMethods.RequiresManualFulfilment(deliveryMethod))
                {
                    // Telegram / manual-web / manual-material: the learner has paid, but an
                    // admin still has to hand the package over. Park it at pending_manual and
                    // leave Status alone — a scaffold subscription stays Pending (which the
                    // entitlement resolver treats as FREE, so this grants nothing), and an
                    // existing Active subscription is never downgraded out from under a
                    // learner who already has access. Admin "Mark Fulfilled" flips both.
                    subscription.FulfilmentStatus = FulfilmentStatuses.PendingManual;
                }
                else
                {
                    subscription.FulfilmentStatus = FulfilmentStatuses.Auto;
                    SubscriptionStateMachine.Transition(subscription, SubscriptionStatus.Active, "checkout_completed");
                }

                subscription.PriceAmount = targetPlan.Price;
                subscription.Currency = targetPlan.Currency;
                subscription.Interval = targetPlan.Interval;
                subscription.ChangedAt = now;
                if (subscription.StartedAt == default)
                {
                    subscription.StartedAt = now;
                }

                if (subscription.NextRenewalAt <= now)
                {
                    subscription.NextRenewalAt = now.AddMonths(Math.Max(1, targetPlan.DurationMonths));
                }

                user.CurrentPlanId = targetPlan.Code;

                if (targetPlan.IncludedCredits > 0)
                {
                    await CreditWalletForPaymentAsync(
                        transaction.LearnerUserId,
                        targetPlan.IncludedCredits,
                        "plan_grant",
                        "subscription",
                        quote.Id,
                        $"Included credits for {targetPlan.Name}",
                        ct);
                }

                if (targetPlan.BundledAiCredits > 0)
                {
                    var inserted = await CreditAiLedgerForPlanPaymentAsync(
                        transaction.LearnerUserId,
                        targetPlan,
                        targetPlan.BundledAiCredits,
                        quote.Id,
                        now,
                        ct);
                    if (inserted)
                    {
                        subscription.AiCreditsRemaining = checked(subscription.AiCreditsRemaining + targetPlan.BundledAiCredits);
                    }
                }

                // Provision the rest of the bundled entitlement template (writing
                // assessments, speaking sessions, Tutor Book / Basic English
                // unlocks, and the access-duration expiry). The plan snapshot the
                // method already has (`targetPlan`) only carries AI credits, so we
                // read these fields from the immutable BillingPlanVersion locked to
                // this purchase (`quote.PlanVersionId`) and fall back to the live
                // BillingPlan. AI credits are intentionally left untouched here —
                // they are granted exactly once above via the AI-credit ledger, so
                // ApplyPlanEntitlements must NOT re-touch AiCreditsRemaining. This
                // runs once per completion (the whole method early-returns when the
                // quote is already Completed), so replays never re-stamp.
                var planVersion = string.IsNullOrWhiteSpace(quote.PlanVersionId)
                    ? null
                    : await db.BillingPlanVersions.AsNoTracking()
                        .FirstOrDefaultAsync(v => v.Id == quote.PlanVersionId, ct);
                if (planVersion is not null)
                {
                    SubscriptionBundleInitializer.ApplyPlanEntitlements(subscription, planVersion, now);
                }
                else
                {
                    var livePlanForBundle = await FindBillingPlanAsync(quote.PlanCode, ct);
                    if (livePlanForBundle is not null)
                    {
                        SubscriptionBundleInitializer.ApplyPlanEntitlements(subscription, livePlanForBundle, now);
                    }
                }

                // Freeze rule is "once per subscription purchase" — buying a new plan
                // renews the learner's one-time self-service freeze entitlement.
                await ResetFreezeEntitlementForNewPurchaseAsync(transaction.LearnerUserId, ct);
            }
        }

        // Activate the pre-payment scaffold subscription now that we are on the paid
        // completion path. An automatic_web plan purchase has already transitioned it to
        // Active above; this covers add-on / credit / review-pack purchases by a
        // first-time learner whose scaffold subscription was created Pending in
        // BuildBillingQuoteAsync and never runs the plan block. Guarded on Pending so it
        // is a strict no-op for a returning learner's existing Active/Cancelled/Frozen
        // subscription. Pending now has TWO producers — the checkout scaffold, and a
        // manual-delivery plan purchase parked above — so the pending_manual conjunct is
        // load-bearing: without it this would activate a Telegram/manual order that an
        // admin has not yet fulfilled.
        if (subscription.Status == SubscriptionStatus.Pending
            && !string.Equals(subscription.FulfilmentStatus, FulfilmentStatuses.PendingManual, StringComparison.OrdinalIgnoreCase))
        {
            SubscriptionStateMachine.Transition(subscription, SubscriptionStatus.Active, "checkout_completed");
        }

        foreach (var item in quoteResponse.Items.Where(x => string.Equals(x.Kind, "addon", StringComparison.OrdinalIgnoreCase)))
        {
            var addOn = catalogSnapshot?.AddOns.FirstOrDefault(snapshot => string.Equals(snapshot.Code, item.Code, StringComparison.OrdinalIgnoreCase));
            if (addOn is null)
            {
                var liveAddOn = await FindBillingAddOnAsync(item.Code, ct);
                if (liveAddOn is null)
                {
                    continue;
                }

                addOn = new BillingQuoteAddOnSnapshot
                {
                    Code = liveAddOn.Code,
                    Name = liveAddOn.Name,
                    Price = liveAddOn.Price,
                    Currency = liveAddOn.Currency,
                    Interval = liveAddOn.Interval,
                    IsRecurring = liveAddOn.IsRecurring,
                    DurationDays = liveAddOn.DurationDays,
                    GrantCredits = liveAddOn.GrantCredits,
                    GrantEntitlementsJson = liveAddOn.GrantEntitlementsJson
                };
            }

            var existingItem = await db.SubscriptionItems.FirstOrDefaultAsync(
                x => x.SubscriptionId == subscription.Id
                     && x.ItemCode == addOn.Code
                     && x.QuoteId == quote.Id,
                ct);

            if (existingItem is null)
            {
                // Resolve the immutable add-on version id once (snapshot id, else the
                // quote's add-on version map) — reused for the SubscriptionItem row
                // and the entitlement grant below.
                var resolvedAddOnVersionId = addOn.VersionId
                    ?? (addOnVersionIds.TryGetValue(addOn.Code, out var mappedAddOnVersionId) ? mappedAddOnVersionId : null);

                db.SubscriptionItems.Add(new SubscriptionItem
                {
                    Id = TruncateIdentifier($"subitem-{Guid.NewGuid():N}"),
                    SubscriptionId = subscription.Id,
                    ItemCode = addOn.Code,
                    ItemType = addOn.IsRecurring ? "recurring_addon" : "addon",
                    AddOnVersionId = resolvedAddOnVersionId,
                    Quantity = Math.Max(1, item.Quantity),
                    Status = SubscriptionItemStatus.Active,
                    StartsAt = now,
                    EndsAt = addOn.DurationDays > 0 ? now.AddDays(addOn.DurationDays) : null,
                    QuoteId = quote.Id,
                    CheckoutSessionId = transaction.GatewayTransactionId,
                    CreatedAt = now,
                    UpdatedAt = now
                });

                // Mocks Module Phase 8b — record an audit + billing-event trail
                // when an add-on whose GrantEntitlementsJson contains mock-type
                // entitlements is first applied to the subscription. The existing
                // `MockEntitlementService` already derives "granted" totals
                // dynamically from active SubscriptionItems joined to the
                // BillingAddOn catalog, so no separate ledger insert is needed
                // for grants. The audit/event emission here gives ops a
                // searchable trail of "mock_entitlements_granted" signals and is
                // idempotent on (BillingAddOnId, UserId, paymentTransactionId)
                // by virtue of running only inside this `existingItem == null`
                // branch.
                await RecordMockEntitlementGrantsAsync(
                    transaction,
                    quote,
                    subscription.Id,
                    addOn.Code,
                    Math.Max(1, item.Quantity),
                    now,
                    ct);

                // Provision the non-AI add-on entitlements (writing assessments,
                // speaking sessions, Tutor Book unlock). The add-on snapshot
                // (`addOn`) only carries AI/credit fields, so read the granted
                // letters/sessions/kind from the immutable BillingAddOnVersion
                // locked to this purchase and fall back to the live BillingAddOn.
                // AI credits are intentionally NOT touched here — they are granted
                // separately below via the AI-credit ledger, so
                // ApplyAddOnEntitlements must NOT re-touch AiCreditsRemaining. This
                // sits strictly inside the `existingItem is null` branch so a
                // replayed/duplicate completion never double-grants.
                var addOnVersion = string.IsNullOrWhiteSpace(resolvedAddOnVersionId)
                    ? null
                    : await db.BillingAddOnVersions.AsNoTracking()
                        .FirstOrDefaultAsync(v => v.Id == resolvedAddOnVersionId, ct);
                // Phase 6a — resolve the add-on kind + extension days from the same
                // version/live row used for ApplyAddOnEntitlements, so the guarded
                // access-extension branch below can advance ExpiresAt.
                string resolvedAddonKind = string.Empty;
                int resolvedExtensionDays = 0;
                if (addOnVersion is not null)
                {
                    SubscriptionBundleInitializer.ApplyAddOnEntitlements(subscription, addOnVersion);
                    resolvedAddonKind = addOnVersion.AddonKind;
                    resolvedExtensionDays = addOnVersion.ExtensionDays;
                }
                else
                {
                    var liveAddOnForEntitlements = await FindBillingAddOnAsync(addOn.Code, ct);
                    if (liveAddOnForEntitlements is not null)
                    {
                        SubscriptionBundleInitializer.ApplyAddOnEntitlements(subscription, liveAddOnForEntitlements);
                        resolvedAddonKind = liveAddOnForEntitlements.AddonKind;
                        resolvedExtensionDays = liveAddOnForEntitlements.ExtensionDays;
                    }
                }

                // Phase 6a — Extend Access add-on. Strictly additive and guarded on
                // the new `access_extension` kind: a no-op for every other add-on /
                // plan purchase. Sits inside the once-only `existingItem is null`
                // guard so a replayed completion never double-extends. Pushes the
                // course expiry out from the later of (now, current expiry).
                if (string.Equals(resolvedAddonKind, "access_extension", StringComparison.Ordinal)
                    && resolvedExtensionDays > 0)
                {
                    var extensionUnits = Math.Max(1, item.Quantity);
                    var totalExtensionDays = resolvedExtensionDays * extensionUnits;
                    var baseline = subscription.ExpiresAt is { } currentExpiry && currentExpiry > now
                        ? currentExpiry
                        : now;
                    subscription.ExpiresAt = baseline.AddDays(totalExtensionDays);
                }

                if (string.Equals(resolvedAddonKind, "ai_package", StringComparison.Ordinal)
                    && aiPackageCreditService is not null)
                {
                    var liveAddOnForPackage = await FindBillingAddOnAsync(addOn.Code, ct);
                    if (liveAddOnForPackage is not null)
                    {
                        await aiPackageCreditService.GrantPackageAsync(
                            transaction.LearnerUserId,
                            liveAddOnForPackage,
                            Math.Max(1, item.Quantity),
                            transaction.GatewayTransactionId,
                            quote.Id,
                            ct);
                    }
                }
            }

            var isAiPackageAddOn = await IsAiPackageAddOnAsync(addOn.Code, ct);
            if (addOn.GrantCredits > 0 && !isAiPackageAddOn)
            {
                var creditAmount = addOn.GrantCredits * Math.Max(1, item.Quantity);
                if (AddOnGrantsAiCredits(addOn.GrantEntitlementsJson))
                {
                    if (existingItem is null)
                    {
                        subscription.AiCreditsRemaining = checked(subscription.AiCreditsRemaining + creditAmount);
                    }

                    await CreditAiLedgerForAddOnPaymentAsync(
                        transaction.LearnerUserId,
                        addOn,
                        creditAmount,
                        quote.Id,
                        now,
                        ct);
                }
                else
                {
                    await CreditWalletForPaymentAsync(
                        transaction.LearnerUserId,
                        creditAmount,
                        "credit_purchase",
                        "addon",
                        $"{quote.Id}:{addOn.Code}",
                        $"{addOn.Name} credits",
                        ct);
                }
            }
        }

        var redemptions = await db.BillingCouponRedemptions
            .Where(x => x.QuoteId == quote.Id && x.Status == BillingRedemptionStatus.Reserved)
            .ToListAsync(ct);
        foreach (var redemption in redemptions)
        {
            redemption.Status = BillingRedemptionStatus.Applied;
            redemption.CheckoutSessionId = transaction.GatewayTransactionId;
            redemption.SubscriptionId = subscription.Id;
            redemption.CouponVersionId ??= quote.CouponVersionId;
            if (string.IsNullOrWhiteSpace(redemption.CouponId) && !string.IsNullOrWhiteSpace(quote.CouponCode))
            {
                var coupon = await FindBillingCouponAsync(quote.CouponCode, ct);
                redemption.CouponId = coupon?.Id;
            }
        }

        // Apply coupon variant side effects (trial extension, free months date shifts)
        if (couponVariantApplicator is not null && !string.IsNullOrWhiteSpace(quote.CouponCode))
        {
            var variantCoupon = await FindBillingCouponAsync(quote.CouponCode, ct);
            if (variantCoupon is not null)
            {
                couponVariantApplicator.Apply(variantCoupon, subscription);
            }
        }

        var invoiceId = TruncateIdentifier($"inv-{quote.Id}");
        var existingInvoice = await db.Invoices.FirstOrDefaultAsync(x => x.Id == invoiceId, ct);
        if (existingInvoice is null)
        {
            db.Invoices.Add(new Invoice
            {
                Id = invoiceId,
                UserId = transaction.LearnerUserId,
                Number = await AllocateInvoiceNumberAsync(transaction.LearnerUserId, invoiceId, ct),
                IssuedAt = now,
                Amount = quote.TotalAmount,
                Currency = quote.Currency,
                Status = "Paid",
                Description = quoteResponse.Summary,
                PlanVersionId = quote.PlanVersionId,
                AddOnVersionIdsJson = quote.AddOnVersionIdsJson,
                CouponVersionId = quote.CouponVersionId,
                QuoteId = quote.Id,
                CheckoutSessionId = transaction.GatewayTransactionId
            });
        }
        else
        {
            existingInvoice.PlanVersionId ??= quote.PlanVersionId;
            if (string.IsNullOrWhiteSpace(existingInvoice.AddOnVersionIdsJson) || existingInvoice.AddOnVersionIdsJson == "{}")
            {
                existingInvoice.AddOnVersionIdsJson = quote.AddOnVersionIdsJson;
            }
            existingInvoice.CouponVersionId ??= quote.CouponVersionId;
            existingInvoice.QuoteId ??= quote.Id;
            existingInvoice.CheckoutSessionId ??= transaction.GatewayTransactionId;
            existingInvoice.Number ??= await AllocateInvoiceNumberAsync(transaction.LearnerUserId, invoiceId, ct);
        }

        quote.Status = BillingQuoteStatus.Completed;
        quote.CheckoutSessionId = transaction.GatewayTransactionId;

        await TryWriteGatewayReceiptAsync(transaction, quote, quoteResponse.Summary, ct);

        // Mark pricing-experiment conversion if this quote was assigned to a variant.
        if (!string.IsNullOrEmpty(quote.ExperimentAssignmentId))
        {
            var assignment = await db.PricingExperimentAssignments
                .FirstOrDefaultAsync(a => a.Id == quote.ExperimentAssignmentId, ct);
            if (assignment is not null && !assignment.Converted)
            {
                assignment.Converted = true;
                assignment.ConvertedAt = now;
                assignment.ConvertedAmount = quote.TotalAmount;
            }
        }

        await AddBillingEventIfMissingAsync(new BillingEvent
        {
            Id = $"bill-evt-{Guid.NewGuid():N}",
            UserId = transaction.LearnerUserId,
            SubscriptionId = subscription.Id,
            QuoteId = quote.Id,
            EventType = "checkout_completed",
            EntityType = "PaymentTransaction",
            EntityId = transaction.GatewayTransactionId,
            PayloadJson = JsonSupport.Serialize(new
            {
                quoteId = quote.Id,
                planCode = quote.PlanCode,
                items = quoteResponse.Items,
                totalAmount = quote.TotalAmount,
                currency = quote.Currency,
                gateway = transaction.Gateway
            }),
            OccurredAt = now
        }, ct);

        await RecordEventAsync(transaction.LearnerUserId, "checkout_completed", new
        {
            quoteId = quote.Id,
            gateway = transaction.Gateway,
            planCode = quote.PlanCode,
            totalAmount = quote.TotalAmount
        }, ct);

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerPaymentSucceeded,
            transaction.LearnerUserId,
            "PaymentTransaction",
            transaction.GatewayTransactionId,
            now.UtcDateTime.ToString("yyyy-MM-dd"),
            new Dictionary<string, object?>
            {
                ["amount"] = quote.TotalAmount,
                ["currency"] = quote.Currency,
                ["planName"] = quote.PlanCode
            },
            ct);

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerSubscriptionChanged,
            transaction.LearnerUserId,
            "Subscription",
            subscription.Id,
            now.UtcDateTime.ToString("yyyy-MM-dd"),
            new Dictionary<string, object?>
            {
                ["message"] = $"Your subscription to {quote.PlanCode} is now active.",
                ["planName"] = quote.PlanCode,
                ["status"] = "active"
            },
            ct);
    }

    private async Task MarkCheckoutFailedAsync(PaymentTransaction transaction, CancellationToken ct)
    {
        var quote = await GetQuoteForTransactionAsync(transaction, ct);
        if (quote is null || quote.Status is BillingQuoteStatus.Completed or BillingQuoteStatus.Cancelled)
        {
            return;
        }

        quote.Status = BillingQuoteStatus.Cancelled;
        quote.CheckoutSessionId ??= transaction.GatewayTransactionId;

        var redemptions = await db.BillingCouponRedemptions
            .Where(x => x.QuoteId == quote.Id && x.Status == BillingRedemptionStatus.Reserved)
            .ToListAsync(ct);

        foreach (var redemption in redemptions)
        {
            redemption.Status = BillingRedemptionStatus.Voided;
            redemption.CheckoutSessionId = transaction.GatewayTransactionId;

            var coupon = !string.IsNullOrWhiteSpace(redemption.CouponId)
                ? await db.BillingCoupons.FirstOrDefaultAsync(x => x.Id == redemption.CouponId, ct)
                : await db.BillingCoupons.FirstOrDefaultAsync(x => x.Code == redemption.CouponCode, ct);
            if (coupon is not null && coupon.RedemptionCount > 0)
            {
                coupon.RedemptionCount -= 1;
                coupon.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await AddBillingEventIfMissingAsync(new BillingEvent
        {
            Id = $"bill-evt-{Guid.NewGuid():N}",
            UserId = transaction.LearnerUserId,
            QuoteId = quote.Id,
            EventType = "checkout_failed",
            EntityType = "PaymentTransaction",
            EntityId = transaction.GatewayTransactionId,
            PayloadJson = JsonSupport.Serialize(new
            {
                quoteId = quote.Id,
                gateway = transaction.Gateway,
                totalAmount = quote.TotalAmount,
                currency = quote.Currency
            }),
            OccurredAt = DateTimeOffset.UtcNow
        }, ct);

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerPaymentFailed,
            transaction.LearnerUserId,
            "PaymentTransaction",
            transaction.GatewayTransactionId,
            DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-dd"),
            new Dictionary<string, object?>
            {
                ["amount"] = quote.TotalAmount,
                ["currency"] = quote.Currency,
                ["message"] = "Your payment could not be processed. Update your billing details to avoid subscription interruption."
            },
            ct);
    }

    private async Task ApplyCheckoutRefundAsync(PaymentTransaction transaction, string refundEventId, CancellationToken ct)
    {
        var quote = await GetQuoteForTransactionAsync(transaction, ct);
        if (quote is null)
        {
            return;
        }

        var refundIdempotencyKey = $"webhook-refund:{refundEventId}";
        var existingRefund = await db.Set<OrderRefund>().AsNoTracking()
            .AnyAsync(refund => refund.IdempotencyKey == refundIdempotencyKey
                                || (refund.Gateway == transaction.Gateway
                                    && refund.GatewayRefundId == refundEventId)
                                || (refund.PaymentTransactionId == transaction.GatewayTransactionId
                                    && refund.Status == "succeeded"
                                    && refund.RefundType == "full"),
                ct);
        if (existingRefund)
        {
            return;
        }

        var reversedWalletCredits = await ReverseCheckoutWalletCreditsAsync(transaction, ct);
        var reversedEntitlements = await ReverseCheckoutEntitlementsAsync(transaction, ct);

        var subscription = await db.Subscriptions.FirstOrDefaultAsync(x => x.UserId == transaction.LearnerUserId, ct);
        var reversedAiCredits = false;

        if (subscription is not null)
        {
            var purchaseEntries = await db.AiCreditLedger.AsNoTracking()
                .Where(entry => entry.UserId == transaction.LearnerUserId
                                && entry.Source == AiCreditSource.Purchase
                                && entry.TokensDelta > 0
                                && entry.ReferenceId != null
                                && (entry.ReferenceId.StartsWith("addon:" + quote.Id + ":")
                                    || entry.ReferenceId.StartsWith("plan:" + quote.Id + ":")))
                .ToListAsync(ct);

            foreach (var purchase in purchaseEntries)
            {
                var reversalReferenceId = BuildAiCreditRefundReference(purchase.ReferenceId!);
                if (reversalReferenceId is null) continue;
                var alreadyReversed = await db.AiCreditLedger.AsNoTracking()
                    .AnyAsync(entry => entry.UserId == transaction.LearnerUserId
                                       && entry.Source == AiCreditSource.AdminAdjustment
                                       && entry.ReferenceId == reversalReferenceId,
                        ct);
                if (alreadyReversed) continue;

                subscription.AiCreditsRemaining = Math.Max(0, subscription.AiCreditsRemaining - purchase.TokensDelta);
                db.AiCreditLedger.Add(new AiCreditLedgerEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = transaction.LearnerUserId,
                    TokensDelta = -purchase.TokensDelta,
                    CostDeltaUsd = 0m,
                    Source = AiCreditSource.AdminAdjustment,
                    Description = "Refund reversal for AI grading credits",
                    ReferenceId = reversalReferenceId,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                reversedAiCredits = true;

                await AddBillingEventIfMissingAsync(new BillingEvent
                {
                    Id = $"bill-evt-{Guid.NewGuid():N}",
                    UserId = transaction.LearnerUserId,
                    SubscriptionId = subscription.Id,
                    QuoteId = quote.Id,
                    EventType = "ai_package_credits_refunded",
                    EntityType = "PaymentWebhookEvent",
                    EntityId = $"{refundEventId}:{reversalReferenceId}",
                    PayloadJson = JsonSupport.Serialize(new
                    {
                        paymentTransactionId = transaction.GatewayTransactionId,
                        sourceReferenceId = purchase.ReferenceId,
                        creditsReversed = purchase.TokensDelta,
                        referenceId = reversalReferenceId
                    }),
                    OccurredAt = DateTimeOffset.UtcNow,
                }, ct);
            }
        }

        db.Set<OrderRefund>().Add(new OrderRefund
        {
            Id = Guid.NewGuid(),
            PaymentTransactionId = transaction.GatewayTransactionId,
            LearnerUserId = transaction.LearnerUserId,
            Gateway = transaction.Gateway,
            GatewayRefundId = refundEventId,
            IdempotencyKey = refundIdempotencyKey,
            RefundType = "full",
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            Status = "succeeded",
            Reason = "gateway_webhook",
            RequestedByAdminId = null,
            RequestedByAdminName = null,
            ReversedWalletCredits = reversedWalletCredits,
            ReversedEntitlements = reversedEntitlements || reversedAiCredits,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        await AddBillingEventIfMissingAsync(new BillingEvent
        {
            Id = $"bill-evt-refund-{Guid.NewGuid():N}",
            UserId = transaction.LearnerUserId,
            SubscriptionId = subscription?.Id,
            QuoteId = quote.Id,
            EventType = "refund_full_received",
            EntityType = nameof(OrderRefund),
            EntityId = refundIdempotencyKey,
            PayloadJson = JsonSupport.Serialize(new
            {
                paymentTransactionId = transaction.GatewayTransactionId,
                amount = transaction.Amount,
                currency = transaction.Currency,
                reversedWalletCredits,
                reversedEntitlements,
                reversedAiCredits
            }),
            OccurredAt = DateTimeOffset.UtcNow,
        }, ct);
    }

    private async Task ApplyPartialCheckoutRefundAsync(PaymentTransaction transaction, PaymentWebhookEvent webhookEvent, string? gatewayObjectId, CancellationToken ct)
    {
        var parsedAmount = ReadRefundAmountFromWebhook(webhookEvent.PayloadJson);
        if (parsedAmount is null || parsedAmount.Value.Amount <= 0m || parsedAmount.Value.Amount >= transaction.Amount)
        {
            return;
        }

        var refundIdempotencyKey = $"webhook-refund:{webhookEvent.Id:N}";
        var gatewayRefundId = webhookEvent.GatewayEventId;

        var existingRefund = await db.Set<OrderRefund>().AsNoTracking()
            .AnyAsync(refund => refund.IdempotencyKey == refundIdempotencyKey
                                || (refund.Gateway == transaction.Gateway && refund.GatewayRefundId == gatewayRefundId),
                ct);
        if (existingRefund)
        {
            return;
        }

        var alreadyRefunded = await db.Set<OrderRefund>().AsNoTracking()
            .Where(refund => refund.PaymentTransactionId == transaction.GatewayTransactionId
                             && refund.Status != "failed"
                             && refund.Status != "reversed")
            .SumAsync(refund => (decimal?)refund.Amount, ct) ?? 0m;
        var remaining = Math.Max(0m, transaction.Amount - alreadyRefunded);
        var refundAmount = parsedAmount.Value.IsCumulative
            ? parsedAmount.Value.Amount - alreadyRefunded
            : parsedAmount.Value.Amount;
        refundAmount = Math.Min(refundAmount, remaining);
        if (refundAmount <= 0m)
        {
            return;
        }

        db.Set<OrderRefund>().Add(new OrderRefund
        {
            Id = Guid.NewGuid(),
            PaymentTransactionId = transaction.GatewayTransactionId,
            LearnerUserId = transaction.LearnerUserId,
            Gateway = transaction.Gateway,
            GatewayRefundId = gatewayRefundId,
            IdempotencyKey = refundIdempotencyKey,
            RefundType = "partial",
            Amount = refundAmount,
            Currency = transaction.Currency,
            Status = "succeeded",
            Reason = "gateway_webhook",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        await AddBillingEventIfMissingAsync(new BillingEvent
        {
            Id = $"bill-evt-refund-partial-{Guid.NewGuid():N}",
            UserId = transaction.LearnerUserId,
            QuoteId = transaction.QuoteId,
            EventType = "refund_partial_received",
            EntityType = nameof(OrderRefund),
            EntityId = refundIdempotencyKey,
            PayloadJson = JsonSupport.Serialize(new
            {
                paymentTransactionId = transaction.GatewayTransactionId,
                amount = refundAmount,
                currency = transaction.Currency,
                gatewayRefundId
            }),
            OccurredAt = DateTimeOffset.UtcNow,
        }, ct);
    }

    private async Task<bool> ReverseCheckoutWalletCreditsAsync(PaymentTransaction transaction, CancellationToken ct)
    {
        var entries = await db.WalletTransactions
            .Where(w => w.Amount > 0
                        && ((w.ReferenceType == "payment" && w.ReferenceId == transaction.GatewayTransactionId)
                            || (transaction.QuoteId != null
                                && ((w.ReferenceType == "subscription" && w.ReferenceId == transaction.QuoteId)
                                    || (w.ReferenceType == "addon" && w.ReferenceId != null && w.ReferenceId.StartsWith(transaction.QuoteId + ":"))))))
            .ToListAsync(ct);
        if (entries.Count == 0) return false;

        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == transaction.LearnerUserId, ct);
        if (wallet is null) return false;

        var totalReverse = entries.Sum(e => e.Amount);
        if (totalReverse <= 0) return false;
        if (wallet.CreditBalance < totalReverse)
        {
            throw new InvalidOperationException(
                $"Cannot reverse {totalReverse} wallet credits because only {wallet.CreditBalance} credits remain.");
        }

        wallet.CreditBalance -= totalReverse;
        wallet.LastUpdatedAt = DateTimeOffset.UtcNow;
        db.WalletTransactions.Add(new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            TransactionType = "refund",
            Amount = -totalReverse,
            BalanceAfter = wallet.CreditBalance,
            ReferenceType = "payment",
            ReferenceId = transaction.GatewayTransactionId,
            Description = $"Reversed by refund of payment {transaction.GatewayTransactionId}",
            CreatedBy = "system",
            CreatedAt = DateTimeOffset.UtcNow
        });
        return true;
    }

    private async Task<bool> ReverseCheckoutEntitlementsAsync(PaymentTransaction transaction, CancellationToken ct)
    {
        var changed = false;
        var items = await db.SubscriptionItems
            .Where(i => i.Status == SubscriptionItemStatus.Active
                        && (i.CheckoutSessionId == transaction.GatewayTransactionId
                            || (transaction.QuoteId != null && i.QuoteId == transaction.QuoteId)))
            .ToListAsync(ct);
        foreach (var item in items)
        {
            item.Status = SubscriptionItemStatus.Cancelled;
            item.EndsAt = DateTimeOffset.UtcNow;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            changed = true;

            // Phase 6a — symmetric reversal of an Extend Access add-on. Guarded on
            // the `access_extension` kind, so it is a no-op for every other add-on.
            // Subtracts the granted days from ExpiresAt but never pulls it below
            // now. The grant flow has no other symmetric entitlement reversal, so
            // this is the only place the extension is undone.
            var reverseExtensionDays = 0;
            if (!string.IsNullOrWhiteSpace(item.AddOnVersionId))
            {
                var ver = await db.BillingAddOnVersions.AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Id == item.AddOnVersionId, ct);
                if (ver is not null && string.Equals(ver.AddonKind, "access_extension", StringComparison.Ordinal))
                {
                    reverseExtensionDays = ver.ExtensionDays;
                }
            }
            if (reverseExtensionDays <= 0)
            {
                var liveAddOn = await FindBillingAddOnAsync(item.ItemCode, ct);
                if (liveAddOn is not null && string.Equals(liveAddOn.AddonKind, "access_extension", StringComparison.Ordinal))
                {
                    reverseExtensionDays = liveAddOn.ExtensionDays;
                }
            }
            if (reverseExtensionDays > 0)
            {
                var now = DateTimeOffset.UtcNow;
                var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == item.SubscriptionId, ct);
                if (sub is not null && sub.ExpiresAt is { } currentExpiry)
                {
                    var totalDays = reverseExtensionDays * Math.Max(1, item.Quantity);
                    var reduced = currentExpiry.AddDays(-totalDays);
                    sub.ExpiresAt = reduced < now ? now : reduced;
                }
            }
        }

        if (string.Equals(transaction.TransactionType, "subscription_payment", StringComparison.OrdinalIgnoreCase))
        {
            var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == transaction.LearnerUserId, ct);
            if (sub is not null && sub.Status == SubscriptionStatus.Active)
            {
                SubscriptionStateMachine.Transition(sub, SubscriptionStatus.Cancelled, "payment_refund_full");
                changed = true;
            }
        }

        return changed;
    }

    private static string? BuildAiCreditRefundReference(string purchaseReferenceId)
    {
        if (purchaseReferenceId.StartsWith("addon:", StringComparison.Ordinal))
        {
            return "addon-refund:" + purchaseReferenceId["addon:".Length..];
        }

        if (purchaseReferenceId.StartsWith("plan:", StringComparison.Ordinal))
        {
            return "plan-refund:" + purchaseReferenceId["plan:".Length..];
        }

        return null;
    }

    /// <summary>
    /// Mocks Module Phase 8b — when a paid <see cref="BillingAddOn"/> grants
    /// per-mock-type credits via its <see cref="BillingAddOn.GrantEntitlementsJson"/>
    /// catalog field, emit one <see cref="AuditEvent"/> per mock-type bucket
    /// (action <c>mock_entitlements_granted</c>) and one
    /// <see cref="BillingEvent"/> aggregating all granted buckets. Both writes
    /// are idempotent on (BillingAddOnId, UserId, paymentTransactionId) — the
    /// audit guard checks for an existing row with the same resource id and
    /// payload, and the billing-event guard piggybacks on
    /// <see cref="AddBillingEventIfMissingAsync"/>.
    /// </summary>
    private async Task RecordMockEntitlementGrantsAsync(
        PaymentTransaction transaction,
        BillingQuote quote,
        string subscriptionId,
        string addOnCode,
        int quantity,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var liveAddOn = await FindBillingAddOnAsync(addOnCode, ct);
        if (liveAddOn is null || string.IsNullOrWhiteSpace(liveAddOn.GrantEntitlementsJson))
        {
            return;
        }

        Dictionary<string, JsonElement>? grantMap;
        try
        {
            grantMap = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                liveAddOn.GrantEntitlementsJson, JsonSupport.Options);
        }
        catch (JsonException)
        {
            return;
        }
        if (grantMap is null || grantMap.Count == 0)
        {
            return;
        }

        var grantedBuckets = new List<(string MockType, int Count)>();
        foreach (var (key, value) in grantMap)
        {
            var ledgerType = MockEntitlementKeys.LedgerTypeForGrantKey(key);
            if (ledgerType is null) continue;
            var perUnit = ReadGrantCount(value);
            if (perUnit <= 0) continue;
            grantedBuckets.Add((ledgerType, perUnit * Math.Max(1, quantity)));
        }

        if (grantedBuckets.Count == 0)
        {
            return;
        }

        // Idempotent audit emission — guard by checking for an existing
        // (Action, ResourceId, ActorId) tuple recorded for this txn.
        var paymentTxnId = transaction.GatewayTransactionId;
        var alreadyAudited = await db.AuditEvents.AnyAsync(a =>
            a.Action == "mock_entitlements_granted"
            && a.ActorId == transaction.LearnerUserId
            && a.ResourceType == "BillingAddOn"
            && a.ResourceId == liveAddOn.Id
            && a.Details != null
            && a.Details.Contains(paymentTxnId), ct);

        if (!alreadyAudited)
        {
            foreach (var bucket in grantedBuckets)
            {
                db.AuditEvents.Add(new AuditEvent
                {
                    Id = $"AUD-{Guid.NewGuid():N}",
                    OccurredAt = occurredAt,
                    ActorId = transaction.LearnerUserId,
                    ActorName = $"system:billing:{transaction.Gateway}",
                    Action = "mock_entitlements_granted",
                    ResourceType = "BillingAddOn",
                    ResourceId = liveAddOn.Id,
                    Details = JsonSupport.Serialize(new
                    {
                        paymentTransactionId = paymentTxnId,
                        mockType = bucket.MockType,
                        count = bucket.Count,
                        addOnId = liveAddOn.Id,
                        addOnCode = liveAddOn.Code,
                        quantity,
                        subscriptionId,
                        quoteId = quote.Id,
                        gateway = transaction.Gateway
                    })
                });
            }
        }

        await AddBillingEventIfMissingAsync(new BillingEvent
        {
            Id = $"bill-evt-{Guid.NewGuid():N}",
            UserId = transaction.LearnerUserId,
            SubscriptionId = subscriptionId,
            QuoteId = quote.Id,
            EventType = "mock_entitlements_granted",
            EntityType = "BillingAddOn",
            EntityId = liveAddOn.Id,
            PayloadJson = JsonSupport.Serialize(new
            {
                paymentTransactionId = paymentTxnId,
                addOnId = liveAddOn.Id,
                addOnCode = liveAddOn.Code,
                quantity,
                buckets = grantedBuckets.Select(b => new { mockType = b.MockType, count = b.Count }).ToArray()
            }),
            OccurredAt = occurredAt
        }, ct);
    }

    /// <summary>
    /// Reads a non-negative int count from a JSON element that may be a
    /// number, numeric string, or boolean (true=1). Used by the Phase 8b
    /// audit pipeline so it stays tolerant of the same loose JSON shapes
    /// already accepted by <see cref="MockEntitlementService"/>.
    /// </summary>
    private static int ReadGrantCount(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var i) => Math.Max(0, i),
            JsonValueKind.String when int.TryParse(element.GetString(), out var s) => Math.Max(0, s),
            JsonValueKind.True => 1,
            _ => 0,
        };
    }

    private async Task AddBillingEventIfMissingAsync(BillingEvent billingEvent, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(billingEvent.EntityId))
        {
            var exists = await db.BillingEvents.AnyAsync(x =>
                x.EventType == billingEvent.EventType
                && x.EntityType == billingEvent.EntityType
                && x.EntityId == billingEvent.EntityId
                && x.UserId == billingEvent.UserId
                && x.QuoteId == billingEvent.QuoteId,
                ct);

            if (exists)
            {
                return;
            }
        }

        db.BillingEvents.Add(billingEvent);
    }

    private async Task CreditWalletForPaymentAsync(
        string userId,
        int amount,
        string transactionType,
        string referenceType,
        string referenceId,
        string description,
        CancellationToken ct)
    {
        if (amount <= 0)
        {
            return;
        }

        var wallet = await db.Wallets.FirstAsync(x => x.UserId == userId, ct);
        var existing = await db.WalletTransactions.FirstOrDefaultAsync(
            x => x.WalletId == wallet.Id
                 && x.TransactionType == transactionType
                 && x.ReferenceType == referenceType
                 && x.ReferenceId == referenceId,
            ct);

        if (existing is not null)
        {
            return;
        }

        wallet.CreditBalance += amount;
        wallet.LastUpdatedAt = DateTimeOffset.UtcNow;

        db.WalletTransactions.Add(new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            TransactionType = transactionType,
            Amount = amount,
            BalanceAfter = wallet.CreditBalance,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Description = description,
            CreatedBy = "system",
            CreatedAt = wallet.LastUpdatedAt
        });
    }

    private async Task CreditAiLedgerForAddOnPaymentAsync(
        string userId,
        BillingQuoteAddOnSnapshot addOn,
        int amount,
        string quoteId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (amount <= 0 || !AddOnGrantsAiCredits(addOn.GrantEntitlementsJson))
        {
            return;
        }

        var referenceId = $"addon:{quoteId}:{addOn.Code}";
        var existing = await db.AiCreditLedger.AsNoTracking().AnyAsync(
            entry => entry.UserId == userId
                     && entry.Source == AiCreditSource.Purchase
                     && entry.ReferenceId == referenceId,
            ct);
        if (existing)
        {
            return;
        }

        db.AiCreditLedger.Add(new AiCreditLedgerEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            TokensDelta = amount,
            CostDeltaUsd = 0m,
            Source = AiCreditSource.Purchase,
            Description = $"{addOn.Name} AI grading credits",
            ReferenceId = referenceId,
            ExpiresAt = addOn.DurationDays > 0 ? now.AddDays(addOn.DurationDays) : null,
            CreatedAt = now,
        });
    }

    private async Task<bool> CreditAiLedgerForPlanPaymentAsync(
        string userId,
        BillingQuotePlanSnapshot plan,
        int amount,
        string quoteId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (amount <= 0) return false;

        var referenceId = $"plan:{quoteId}:{plan.Code}";
        var existing = await db.AiCreditLedger.AsNoTracking().AnyAsync(
            entry => entry.UserId == userId
                     && entry.Source == AiCreditSource.Purchase
                     && entry.ReferenceId == referenceId,
            ct);
        if (existing) return false;

        db.AiCreditLedger.Add(new AiCreditLedgerEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            TokensDelta = amount,
            CostDeltaUsd = 0m,
            Source = AiCreditSource.Purchase,
            Description = $"{plan.Name} bundled AI grading credits",
            ReferenceId = referenceId,
            ExpiresAt = plan.DurationMonths > 0 ? now.AddMonths(plan.DurationMonths) : null,
            CreatedAt = now,
        });

        return true;
    }

    private static bool IsFullRefundWebhook(string? safePayloadJson, decimal transactionAmount)
    {
        if (transactionAmount <= 0m || string.IsNullOrWhiteSpace(safePayloadJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(safePayloadJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("object", out var obj)
                && obj.ValueKind == JsonValueKind.Object)
            {
                var refundedMinor = ReadLong(obj, "amount_refunded");
                var amountMinor = ReadLong(obj, "amount") ?? ReadLong(obj, "amount_total");
                if (refundedMinor is not null && amountMinor is not null && amountMinor > 0)
                {
                    return refundedMinor >= amountMinor;
                }
            }

            if (root.TryGetProperty("resource", out var resource)
                && resource.ValueKind == JsonValueKind.Object
                && resource.TryGetProperty("amount", out var amount)
                && amount.ValueKind == JsonValueKind.Object
                && amount.TryGetProperty("value", out var valueElement)
                && decimal.TryParse(valueElement.GetString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var refundedMajor))
            {
                return refundedMajor >= transactionAmount;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;

        static long? ReadLong(JsonElement obj, string propertyName)
            => obj.TryGetProperty(propertyName, out var value)
               && value.ValueKind == JsonValueKind.Number
               && value.TryGetInt64(out var number)
                ? number
                : null;
    }

    private static RefundWebhookAmount? ReadRefundAmountFromWebhook(string? safePayloadJson)
    {
        if (string.IsNullOrWhiteSpace(safePayloadJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(safePayloadJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("object", out var obj)
                && obj.ValueKind == JsonValueKind.Object)
            {
                var minorAmount = ReadLong(obj, "amount_refunded") ?? ReadLong(obj, "amount");
                if (minorAmount is not null)
                {
                    return new RefundWebhookAmount(
                        decimal.Round(minorAmount.Value / 100m, 2, MidpointRounding.AwayFromZero),
                        obj.TryGetProperty("amount_refunded", out _));
                }
            }

            if (root.TryGetProperty("resource", out var resource)
                && resource.ValueKind == JsonValueKind.Object
                && resource.TryGetProperty("amount", out var amount)
                && amount.ValueKind == JsonValueKind.Object
                && amount.TryGetProperty("value", out var valueElement)
                && decimal.TryParse(valueElement.GetString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var majorAmount))
            {
                return new RefundWebhookAmount(majorAmount, IsCumulative: false);
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;

        static long? ReadLong(JsonElement obj, string propertyName)
            => obj.TryGetProperty(propertyName, out var value)
               && value.ValueKind == JsonValueKind.Number
               && value.TryGetInt64(out var number)
                ? number
                : null;
    }

    private readonly record struct RefundWebhookAmount(decimal Amount, bool IsCumulative);

    private static bool AddOnGrantsAiCredits(string? grantEntitlementsJson)
    {
        if (string.IsNullOrWhiteSpace(grantEntitlementsJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(grantEntitlementsJson);
                 return doc.RootElement.ValueKind == JsonValueKind.Object
                     && TryReadAiCreditGrantValue(doc.RootElement, out var value)
                     && value > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int ResolveAiCreditGrant(string? grantEntitlementsJson, int fallbackGrantCredits)
    {
        if (!string.IsNullOrWhiteSpace(grantEntitlementsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(grantEntitlementsJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && TryReadAiCreditGrantValue(doc.RootElement, out var value))
                {
                    return Math.Max(0, value);
                }
            }
            catch (JsonException)
            {
                return 0;
            }
        }

        return fallbackGrantCredits > 0 && string.IsNullOrWhiteSpace(grantEntitlementsJson)
            ? fallbackGrantCredits
            : 0;
    }

    private static bool TryReadAiCreditGrantValue(JsonElement root, out int value)
    {
        if (root.TryGetProperty("ai_credits", out var aiCredits) && aiCredits.TryGetInt32(out value))
        {
            return true;
        }

        if (root.TryGetProperty("reviewCredits", out var reviewCredits) && reviewCredits.TryGetInt32(out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private async Task<BillingQuote?> GetQuoteForTransactionAsync(PaymentTransaction transaction, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(transaction.QuoteId))
        {
            var quoteByTransactionRef = await db.BillingQuotes.FirstOrDefaultAsync(x => x.Id == transaction.QuoteId, ct);
            if (quoteByTransactionRef is not null)
            {
                return quoteByTransactionRef;
            }
        }

        var metadata = ReadObject(JsonSupport.Deserialize<object?>(transaction.MetadataJson ?? "{}", null));
        var quoteId = ReadString(metadata?.GetValueOrDefault("quoteId"))
            ?? ReadString(metadata?.GetValueOrDefault("quote_id"));

        if (!string.IsNullOrWhiteSpace(quoteId))
        {
            var quoteById = await db.BillingQuotes.FirstOrDefaultAsync(x => x.Id == quoteId, ct);
            if (quoteById is not null)
            {
                return quoteById;
            }
        }

        return await db.BillingQuotes.FirstOrDefaultAsync(x => x.CheckoutSessionId == transaction.GatewayTransactionId, ct);
    }

    private static string NormalizeBillingPaymentStatus(BillingQuote quote, PaymentTransaction? transaction, DateTimeOffset now)
    {
        if (quote.Status == BillingQuoteStatus.Completed
            || string.Equals(transaction?.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return "completed";
        }

        if (string.Equals(transaction?.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return "failed";
        }

        if (quote.Status == BillingQuoteStatus.Cancelled)
        {
            return "cancelled";
        }

        if (quote.Status == BillingQuoteStatus.Expired || quote.ExpiresAt <= now)
        {
            return "expired";
        }

        return "pending";
    }

    private static string? FailureReasonForBillingPaymentStatus(string status)
        => status switch
        {
            "cancelled" => "Checkout was cancelled before payment completed.",
            "failed" => "Payment did not complete.",
            "expired" => "Checkout expired before payment completed.",
            _ => null
        };

    private static string NormalizeExamFamilyCode(string? value)
        => (value ?? "oet").Trim().ToLowerInvariant() switch
        {
            "" => "oet",
            "oet" => "oet",
            "ielts" => "ielts",
            "pte" => "pte",
            var other => other
        };

    private static string FormatExamFamilyLabel(string? value)
        => NormalizeExamFamilyCode(value) switch
        {
            "oet" => "OET",
            "ielts" => "IELTS",
            "pte" => "PTE",
            var other => other.ToUpperInvariant()
        };

    private static string BuildConfidenceLabel(ConfidenceBand band) => band switch
    {
        ConfidenceBand.High => "High confidence practice estimate",
        ConfidenceBand.Medium => "Medium confidence practice estimate",
        _ => "Low confidence practice estimate"
    };

    private static string BuildAiMethodLabel(string subtestCode)
        => (subtestCode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "writing" => "AI-assisted writing evaluation",
            "speaking" => "AI-assisted speaking evaluation",
            "reading" => "Auto-scored reading evaluation",
            "listening" => "Auto-scored listening evaluation",
            _ => "AI-assisted practice evaluation"
        };

    private static bool ShouldRecommendHumanReview(ConfidenceBand band)
        => band is ConfidenceBand.Low or ConfidenceBand.Medium;

    private static string TruncateIdentifier(string value)
        => value.Length <= 64 ? value : value[..64];

    private static string TruncateForColumn(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    // ── Exam Family Reference ──

    public async Task<object> GetExamFamiliesAsync(CancellationToken ct)
    {
        var families = await db.ExamFamilies.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Select(x => new
            {
                code = x.Code,
                label = x.Label,
                scoringModel = x.ScoringModel,
                description = x.Description,
                subtests = x.SubtestConfigJson,
                criteria = x.CriteriaConfigJson,
                isActive = x.IsActive
            })
            .ToListAsync(ct);

        return new { examFamilies = families };
    }

    // ════════════════════════════════════════════
    //  Score Guarantee
    // ════════════════════════════════════════════

    public async Task<object> GetScoreGuaranteeAsync(string userId, CancellationToken ct)
    {
        var pledge = await db.ScoreGuaranteePledges
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.ActivatedAt)
            .FirstOrDefaultAsync(ct);

        if (pledge is null)
            return new { active = false, eligible = true };

        return new
        {
            active = pledge.Status == "active",
            id = pledge.Id,
            pledgeId = pledge.Id,
            userId = pledge.UserId,
            subscriptionId = pledge.SubscriptionId,
            baselineScore = pledge.BaselineScore,
            guaranteedImprovement = pledge.GuaranteedImprovement,
            status = pledge.Status,
            proofDocumentUrl = pledge.ProofDocumentUrl,
            claimNote = pledge.ClaimNote,
            reviewNote = pledge.ReviewNote,
            activatedAt = pledge.ActivatedAt,
            expiresAt = pledge.ExpiresAt,
            actualScore = pledge.ActualScore,
            claimSubmittedAt = pledge.ClaimSubmittedAt,
            reviewedAt = pledge.ReviewedAt
        };
    }

    public async Task<object> ActivateScoreGuaranteeAsync(string userId, ScoreGuaranteeActivateRequest request, CancellationToken ct)
    {
        await EnsureLearnerMutationAllowedAsync(userId, ct);
        var existing = await db.ScoreGuaranteePledges
            .AnyAsync(p => p.UserId == userId && p.Status == "active", ct);
        if (existing)
            throw ApiException.Conflict("already_active", "Score guarantee is already active.");

        if (request.BaselineScore < 0 || request.BaselineScore > 500)
            throw ApiException.Validation("invalid_score", "Baseline score must be between 0 and 500.");

        var sub = await db.Subscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == SubscriptionStatus.Active, ct);
        if (sub is null)
            throw ApiException.Validation("no_subscription", "Active subscription required for score guarantee.");

        var pledge = new ScoreGuaranteePledge
        {
            Id = $"SGP-{Guid.NewGuid():N}",
            UserId = userId,
            SubscriptionId = sub.Id,
            BaselineScore = request.BaselineScore,
            GuaranteedImprovement = 50,
            Status = "active",
            ActivatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(180)
        };

        db.ScoreGuaranteePledges.Add(pledge);
        await db.SaveChangesAsync(ct);

        return new { id = pledge.Id, pledgeId = pledge.Id, status = "active", expiresAt = pledge.ExpiresAt };
    }

    public async Task<object> SubmitScoreGuaranteeClaimAsync(string userId, ScoreGuaranteeClaimRequest request, CancellationToken ct)
    {
        await EnsureLearnerMutationAllowedAsync(userId, ct);
        var pledge = await db.ScoreGuaranteePledges
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "active", ct)
            ?? throw ApiException.NotFound("no_pledge", "No active score guarantee found.");

        if (request.ActualScore < 0 || request.ActualScore > 500)
            throw ApiException.Validation("invalid_score", "Actual score must be between 0 and 500.");

        pledge.ActualScore = request.ActualScore;
        pledge.ProofDocumentUrl = request.ProofDocumentUrl;
        pledge.ClaimNote = request.Note;
        pledge.Status = "claim_submitted";
        pledge.ClaimSubmittedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return new { id = pledge.Id, pledgeId = pledge.Id, status = "claim_submitted" };
    }

    // ════════════════════════════════════════════
    //  Score Cross-Reference Calculator
    // ════════════════════════════════════════════

    public Task<object> GetScoreEquivalencesAsync(CancellationToken ct)
    {
        // Official OET equivalence table (publicly available from OET website)
        var equivalences = new[]
        {
            new { oetGrade = "A",   oetScoreMin = 450, oetScoreMax = 500, ielts = 9.0,  pte = 88,  cefr = "C2" },
            new { oetGrade = "A",   oetScoreMin = 400, oetScoreMax = 449, ielts = 8.5,  pte = 83,  cefr = "C2" },
            new { oetGrade = "B+",  oetScoreMin = 370, oetScoreMax = 399, ielts = 8.0,  pte = 79,  cefr = "C1+" },
            new { oetGrade = "B",   oetScoreMin = 350, oetScoreMax = 369, ielts = 7.5,  pte = 73,  cefr = "C1" },
            new { oetGrade = "B",   oetScoreMin = 300, oetScoreMax = 349, ielts = 7.0,  pte = 65,  cefr = "B2+" },
            new { oetGrade = "C+",  oetScoreMin = 250, oetScoreMax = 299, ielts = 6.5,  pte = 58,  cefr = "B2" },
            new { oetGrade = "C",   oetScoreMin = 200, oetScoreMax = 249, ielts = 6.0,  pte = 50,  cefr = "B1+" },
            new { oetGrade = "C",   oetScoreMin = 150, oetScoreMax = 199, ielts = 5.5,  pte = 43,  cefr = "B1" },
            new { oetGrade = "D",   oetScoreMin = 100, oetScoreMax = 149, ielts = 5.0,  pte = 36,  cefr = "A2+" },
            new { oetGrade = "E",   oetScoreMin = 0,   oetScoreMax = 99,  ielts = 4.5,  pte = 30,  cefr = "A2" }
        };

        var commonRequirements = new[]
        {
            new { country = "Australia", body = "AHPRA (Nursing)", oetMinGrade = "B", oetMinScore = 350, ieltsMin = 7.0 },
            new { country = "Australia", body = "AHPRA (Medicine)", oetMinGrade = "B", oetMinScore = 350, ieltsMin = 7.0 },
            new { country = "UK", body = "NMC (Nursing)", oetMinGrade = "C+", oetMinScore = 300, ieltsMin = 7.0 },
            new { country = "UK", body = "GMC (Medicine)", oetMinGrade = "B", oetMinScore = 350, ieltsMin = 7.5 },
            new { country = "New Zealand", body = "NCNZ (Nursing)", oetMinGrade = "B", oetMinScore = 350, ieltsMin = 7.0 },
            new { country = "Ireland", body = "NMBI (Nursing)", oetMinGrade = "C+", oetMinScore = 300, ieltsMin = 6.5 },
            new { country = "Singapore", body = "SNB (Nursing)", oetMinGrade = "C+", oetMinScore = 300, ieltsMin = 6.5 },
            new { country = "USA", body = "Various State Boards", oetMinGrade = "C+", oetMinScore = 300, ieltsMin = 6.5 }
        };

        return Task.FromResult<object>(new { equivalences, commonRequirements });
    }

    // ════════════════════════════════════════════
    //  Referral Program
    // ════════════════════════════════════════════

    public async Task<object> GetReferralInfoAsync(string userId, CancellationToken ct)
    {
        var myCode = await db.ReferralRecords
            .AsNoTracking()
            .Where(r => r.ReferrerUserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var referralsMade = await db.ReferralRecords
            .AsNoTracking()
            .CountAsync(r => r.ReferrerUserId == userId && r.Status != "pending", ct);

        var creditsEarned = await db.ReferralRecords
            .AsNoTracking()
            .Where(r => r.ReferrerUserId == userId && r.Status == "rewarded")
            .SumAsync(r => r.ReferrerCreditAmount, ct);

        return new
        {
            referralCode = myCode?.ReferralCode,
            referralsMade,
            creditsEarned,
            referrerCreditAmount = 10m,
            referredDiscountPercent = 10m
        };
    }

    public async Task<object> GenerateReferralCodeAsync(string userId, CancellationToken ct)
    {
        await EnsureLearnerMutationAllowedAsync(userId, ct);
        var existing = await db.ReferralRecords
            .FirstOrDefaultAsync(r => r.ReferrerUserId == userId && r.Status == "pending", ct);

        if (existing is not null)
            return new { referralCode = existing.ReferralCode };

        var code = $"REF-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var record = new ReferralRecord
        {
            Id = $"RR-{Guid.NewGuid():N}",
            ReferrerUserId = userId,
            ReferralCode = code,
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.ReferralRecords.Add(record);
        await db.SaveChangesAsync(ct);

        return new { referralCode = code };
    }

    // ══════════════════════════════════════════════════════
    // L3 · Profession-Specific Learning Paths
    // ══════════════════════════════════════════════════════

    public async Task<object> GetLearningPathAsync(string userId, string? professionId, string examTypeCode, CancellationToken ct)
    {
        var goal = await db.Goals.FirstOrDefaultAsync(g => g.UserId == userId, ct);
        var effectiveProfession = professionId ?? goal?.ProfessionId ?? "nursing";

        var profession = await db.Professions
            .FirstOrDefaultAsync(p => p.Code == effectiveProfession || p.Id == effectiveProfession, ct);

        // Get content items filtered by profession
        var contentItems = await db.ContentItems
            .Where(c => c.Status == ContentStatus.Published && c.ExamTypeCode == examTypeCode &&
                        (c.ProfessionId == effectiveProfession || c.ProfessionId == null))
            .OrderBy(c => c.SubtestCode)
            .ThenBy(c => c.Difficulty == "easy" ? 0 : c.Difficulty == "medium" ? 1 : 2)
            .Take(60)
            .ToListAsync(ct);

        // Get user's completed attempts for progress tracking
        var completedAttemptContentIds = await db.Attempts
            .Where(a => a.UserId == userId && a.State == AttemptState.Completed)
            .Select(a => a.ContentId)
            .Distinct()
            .ToListAsync(ct);

        var subtestGroups = contentItems
            .GroupBy(c => c.SubtestCode)
            .Select(g => new
            {
                subtestCode = g.Key,
                totalItems = g.Count(),
                completedItems = g.Count(c => completedAttemptContentIds.Contains(c.Id)),
                progressPercent = g.Count() > 0 ? Math.Round(g.Count(c => completedAttemptContentIds.Contains(c.Id)) * 100.0 / g.Count(), 1) : 0,
                items = g.Take(15).Select(c => new
                {
                    id = c.Id,
                    title = c.Title,
                    difficulty = c.Difficulty,
                    durationMinutes = c.EstimatedDurationMinutes,
                    completed = completedAttemptContentIds.Contains(c.Id),
                    scenarioType = c.ScenarioType
                }).ToList()
            }).ToList();

        return new
        {
            professionCode = effectiveProfession,
            professionLabel = profession?.Label ?? effectiveProfession,
            examTypeCode,
            subtestPaths = subtestGroups,
            overallProgress = subtestGroups.Count > 0
                ? Math.Round(subtestGroups.Average(g => g.progressPercent), 1) : 0,
            totalContent = contentItems.Count,
            nextRecommended = contentItems
                .Where(c => !completedAttemptContentIds.Contains(c.Id))
                .Take(3)
                .Select(c => new { id = c.Id, title = c.Title, subtestCode = c.SubtestCode, difficulty = c.Difficulty })
                .ToList()
        };
    }

    // ══════════════════════════════════════════════════════
    // L5 · Adaptive Weak-Area Remediation
    // ══════════════════════════════════════════════════════

    public async Task<object> GetRemediationProfileAsync(string userId, CancellationToken ct)
    {
        // Analyze criterion scores across recent evaluations to find weak areas
        var userAttemptIds = await db.Attempts
            .Where(a => a.UserId == userId && a.State == AttemptState.Completed)
            .Select(a => a.Id)
            .ToListAsync(ct);

        var evaluations = await db.Evaluations
            .Where(e => userAttemptIds.Contains(e.AttemptId) && e.State == AsyncState.Completed)
            .OrderByDescending(e => e.GeneratedAt)
            .Take(20)
            .ToListAsync(ct);

        var weakAreas = new List<object>();
        var criterionAggregates = new Dictionary<string, List<double>>();

        foreach (var eval in evaluations)
        {
            var criterionScores = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(eval.CriterionScoresJson, []);
            foreach (var cs in criterionScores)
            {
                var code = cs.TryGetValue("code", out var c) ? c?.ToString() ?? "" : "";
                if (string.IsNullOrEmpty(code)) continue;

                var key = $"{eval.SubtestCode}:{code}";
                if (!criterionAggregates.ContainsKey(key))
                    criterionAggregates[key] = [];

                if (cs.TryGetValue("score", out var s) && s is not null)
                {
                    if (s is System.Text.Json.JsonElement je && je.TryGetDouble(out var jd))
                        criterionAggregates[key].Add(jd);
                    else if (double.TryParse(s.ToString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                        criterionAggregates[key].Add(parsed);
                }
            }
        }

        // Identify criteria scoring below threshold
        foreach (var (key, scores) in criterionAggregates.OrderBy(kv => kv.Value.Average()))
        {
            var avg = scores.Average();
            if (avg >= 4.0) continue; // Only flag weak criteria (below 4 out of 6)

            var parts = key.Split(':');
            weakAreas.Add(new
            {
                subtestCode = parts[0],
                criterionCode = parts.Length > 1 ? parts[1] : "",
                averageScore = Math.Round(avg, 2),
                evaluationCount = scores.Count,
                trend = scores.Count >= 3
                    ? (scores.TakeLast(3).Average() > scores.Take(3).Average() ? "improving" : "declining")
                    : "insufficient_data"
            });
        }

        // Get available foundation resources for remediation
        var resources = await db.FoundationResources
            .Where(r => r.Status == ContentStatus.Published)
            .OrderBy(r => r.DisplayOrder)
            .Take(20)
            .ToListAsync(ct);

        return new
        {
            evaluationsAnalyzed = evaluations.Count,
            weakAreas = weakAreas.Take(10).ToList(),
            availableResources = resources.Select(r => new
            {
                id = r.Id,
                title = r.Title,
                resourceType = r.ResourceType,
                difficulty = r.Difficulty,
                displayOrder = r.DisplayOrder
            }).ToList(),
            recommendations = weakAreas.Take(3).Select(wa => new
            {
                area = wa,
                suggestedResources = resources
                    .Where(r => r.Difficulty == "easy" || r.Difficulty == "medium")
                    .Take(3)
                    .Select(r => new { id = r.Id, title = r.Title })
                    .ToList()
            }).ToList()
        };
    }

    public async Task<object> StartRemediationSessionAsync(string userId, string subtestCode, string? criterionCode, CancellationToken ct)
    {
        // Find relevant foundation resources for the weak area
        var resources = await db.FoundationResources
            .Where(r => r.Status == ContentStatus.Published)
            .OrderBy(r => r.Difficulty == "easy" ? 0 : r.Difficulty == "medium" ? 1 : 2)
            .ThenBy(r => r.DisplayOrder)
            .Take(5)
            .ToListAsync(ct);

        var sessionId = $"rem-{Guid.NewGuid():N}";

        return new
        {
            sessionId,
            subtestCode,
            criterionCode,
            resources = resources.Select(r => new
            {
                id = r.Id,
                title = r.Title,
                resourceType = r.ResourceType,
                difficulty = r.Difficulty,
                contentBody = r.ContentBody
            }).ToList(),
            startedAt = DateTimeOffset.UtcNow
        };
    }

    // ══════════════════════════════════════════════════════
    // E1 · Smart Next Best Action
    // ══════════════════════════════════════════════════════

    public async Task<object> GetNextBestActionsAsync(string userId, CancellationToken ct)
    {
        var actions = new List<object>();
        var now = DateTimeOffset.UtcNow;

        // 1. Check for incomplete study plan items due today/overdue
        var studyPlan = await db.StudyPlans
            .Where(p => p.UserId == userId && p.State == AsyncState.Completed)
            .OrderByDescending(p => p.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        if (studyPlan is not null)
        {
            var overdueItems = await db.StudyPlanItems
                .Where(i => i.StudyPlanId == studyPlan.Id && i.Status == StudyPlanItemStatus.NotStarted &&
                            i.DueDate <= DateOnly.FromDateTime(now.UtcDateTime))
                .OrderBy(i => i.DueDate)
                .Take(3)
                .ToListAsync(ct);

            foreach (var item in overdueItems)
            {
                actions.Add(new
                {
                    type = "overdue_task",
                    priority = "high",
                    title = item.Title,
                    subtitle = $"Due {item.DueDate:MMM dd} · {item.DurationMinutes}min",
                    actionUrl = $"/study-plan?highlight={item.Id}",
                    subtestCode = item.SubtestCode
                });
            }
        }

        // 2. Check for pending reviews (tutor feedback ready)
        var pendingReviews = await db.ReviewRequests
            .Where(r => r.State == ReviewRequestState.Completed)
            .Join(db.Attempts, r => r.AttemptId, a => a.Id, (r, a) => new { r, a })
            .Where(x => x.a.UserId == userId)
            .OrderByDescending(x => x.r.CompletedAt)
            .Take(2)
            .ToListAsync(ct);

        foreach (var pr in pendingReviews)
        {
            actions.Add(new
            {
                type = "review_ready",
                priority = "medium",
                title = $"Tutor review ready: {pr.r.SubtestCode}",
                subtitle = "Review your personalized feedback",
                actionUrl = $"/feedback/{pr.r.Id}",
                subtestCode = pr.r.SubtestCode
            });
        }

        // 3. Check weak areas that need attention
        var recentAttemptIds = await db.Attempts
            .Where(a => a.UserId == userId && a.State == AttemptState.Completed)
            .OrderByDescending(a => a.CompletedAt)
            .Take(5)
            .Select(a => a.Id)
            .ToListAsync(ct);

        var recentEvals = await db.Evaluations
            .Where(e => recentAttemptIds.Contains(e.AttemptId) && e.State == AsyncState.Completed)
            .ToListAsync(ct);

        if (recentEvals.Count > 0)
        {
            var weakSubtest = recentEvals
                .GroupBy(e => e.SubtestCode)
                .Select(g => new
                {
                    SubtestCode = g.Key,
                    AvgMid = g.Average(e =>
                    {
                        var parts = e.ScoreRange?.Split('-');
                        return parts?.Length == 2 && double.TryParse(parts[0], out var lo) && double.TryParse(parts[1], out var hi)
                            ? (lo + hi) / 2.0 : 0;
                    })
                })
                .OrderBy(g => g.AvgMid)
                .FirstOrDefault();

            if (weakSubtest is not null && weakSubtest.AvgMid < OetScoring.ScaledPassGradeB)
            {
                actions.Add(new
                {
                    type = "weak_area_practice",
                    priority = "medium",
                    title = $"Practice {weakSubtest.SubtestCode} — your weakest area",
                    subtitle = $"Average score: {weakSubtest.AvgMid:F0}/500",
                    actionUrl = $"/practice/{weakSubtest.SubtestCode}",
                    subtestCode = weakSubtest.SubtestCode
                });
            }
        }

        // 4. Goal-based recommendation
        var goal = await db.Goals.FirstOrDefaultAsync(g => g.UserId == userId, ct);
        if (goal?.TargetExamDate is not null)
        {
            var daysUntilExam = (goal.TargetExamDate.Value.ToDateTime(TimeOnly.MinValue) - now.UtcDateTime).TotalDays;
            if (daysUntilExam is > 0 and <= 30)
            {
                actions.Add(new
                {
                    type = "exam_approaching",
                    priority = "high",
                    title = $"Exam in {daysUntilExam:F0} days — intensify practice",
                    subtitle = "Focus on mock exams and timed practice",
                    actionUrl = "/test-day",
                    subtestCode = (string?)null
                });
            }
        }

        // 5. Streak continuation
        actions.Add(new
        {
            type = "daily_goal",
            priority = "low",
            title = "Complete today's study goal",
            subtitle = "Keep your streak going",
            actionUrl = "/dashboard",
            subtestCode = (string?)null
        });

        return new
        {
            actions = actions.Take(5).ToList(),
            generatedAt = now
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // E4: Speaking Fluency Timeline
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetFluencyTimelineAsync(string userId, string attemptId, CancellationToken ct)
    {
        var attempt = await db.Attempts
            .FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId && a.SubtestCode == "speaking", ct)
            ?? throw ApiException.NotFound("ATTEMPT_NOT_FOUND", "Speaking attempt not found.");

        // Parse transcript segments for timing data
        var segments = new List<object>();
        try
        {
            var transcriptItems = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, System.Text.Json.JsonElement>>>(attempt.TranscriptJson);
            if (transcriptItems is not null)
            {
                double lastEnd = 0;
                int segIdx = 0;
                foreach (var item in transcriptItems)
                {
                    var startSec = item.TryGetValue("startTime", out var st) ? st.GetDouble() : lastEnd;
                    var endSec = item.TryGetValue("endTime", out var et) ? et.GetDouble() : startSec + 1;
                    var text = item.TryGetValue("text", out var tx) ? tx.GetString() ?? "" : "";
                    var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                    var duration = endSec - startSec;
                    var wordsPerMinute = duration > 0 ? wordCount / duration * 60.0 : 0;
                    var gap = startSec - lastEnd;

                    // Detect filler words
                    var fillerWords = new[] { "um", "uh", "er", "ah", "like", "you know", "sort of", "kind of" };
                    var fillerCount = fillerWords.Sum(f => System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), @"\b" + f + @"\b").Count);

                    segments.Add(new
                    {
                        index = segIdx++,
                        startTime = Math.Round(startSec, 2),
                        endTime = Math.Round(endSec, 2),
                        text,
                        wordCount,
                        wordsPerMinute = Math.Round(wordsPerMinute, 1),
                        pauseBefore = Math.Round(gap, 2),
                        isPause = gap > 1.5,
                        fillerCount,
                        fluencyRating = fillerCount == 0 && wordsPerMinute >= 100 && wordsPerMinute <= 170 ? "good"
                            : fillerCount > 2 || wordsPerMinute < 80 || wordsPerMinute > 200 ? "poor" : "fair"
                    });
                    lastEnd = endSec;
                }
            }
        }
        catch { /* Transcript parsing failed — return empty timeline */ }

        // Parse analysis JSON for overall fluency metrics
        var analysisData = new Dictionary<string, object>();
        try
        {
            var analysis = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(attempt.AnalysisJson);
            if (analysis is not null)
            {
                if (analysis.TryGetValue("speechRate", out var sr)) analysisData["speechRate"] = sr.GetDouble();
                if (analysis.TryGetValue("pauseCount", out var pc)) analysisData["pauseCount"] = pc.GetInt32();
                if (analysis.TryGetValue("averagePauseDuration", out var apd)) analysisData["averagePauseDuration"] = apd.GetDouble();
            }
        }
        catch { }

        var totalWords = segments.Sum(s => (int)((dynamic)s).wordCount);
        var totalFillers = segments.Sum(s => (int)((dynamic)s).fillerCount);
        var totalDuration = segments.Count > 0 ? (double)((dynamic)segments.Last()).endTime : 0;

        return new
        {
            attemptId,
            totalDurationSeconds = Math.Round(totalDuration, 1),
            totalWords,
            totalFillerWords = totalFillers,
            fillerRatio = totalWords > 0 ? Math.Round(totalFillers * 100.0 / totalWords, 1) : 0,
            averageWordsPerMinute = totalDuration > 0 ? Math.Round(totalWords / totalDuration * 60, 1) : 0,
            pauseCount = segments.Count(s => (bool)((dynamic)s).isPause),
            timeline = segments,
            overallAnalysis = analysisData,
            benchmarks = new
            {
                idealWordsPerMinute = new { min = 120, max = 160 },
                maxAcceptableFillerRatio = 3.0,
                maxAcceptablePauseSeconds = 2.0
            }
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // E5: Progress — Comparative Analytics & Percentile Ranking
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetComparativeAnalyticsAsync(string userId, CancellationToken ct)
    {
        var subtests = new[] { "writing", "speaking", "reading", "listening" };
        var results = new List<object>();

        foreach (var subtest in subtests)
        {
            // Get user's latest evaluation scores
            var userEvals = await db.Evaluations
                .Where(e => db.Attempts.Any(a => a.Id == e.AttemptId && a.UserId == userId && a.SubtestCode == subtest))
                .OrderByDescending(e => e.GeneratedAt)
                .Take(5)
                .ToListAsync(ct);

            if (userEvals.Count == 0) continue;

            var userAvg = userEvals
                .Select(e =>
                {
                    var parts = e.ScoreRange?.Split('-');
                    return parts?.Length == 2 && int.TryParse(parts[0], out var lo) && int.TryParse(parts[1], out var hi)
                        ? (lo + hi) / 2.0 : (double?)null;
                })
                .Where(s => s.HasValue)
                .Select(s => s!.Value)
                .ToList();

            if (userAvg.Count == 0) continue;
            var userScore = userAvg.Average();

            // Get all users' average scores for this subtest (cohort comparison)
            var allScores = await db.Evaluations
                .Where(e => e.SubtestCode == subtest && e.GeneratedAt >= DateTimeOffset.UtcNow.AddDays(-90))
                .Select(e => e.ScoreRange)
                .ToListAsync(ct);

            var allAverages = allScores
                .Select(sr =>
                {
                    var parts = sr?.Split('-');
                    return parts?.Length == 2 && int.TryParse(parts[0], out var lo) && int.TryParse(parts[1], out var hi)
                        ? (lo + hi) / 2.0 : (double?)null;
                })
                .Where(s => s.HasValue)
                .Select(s => s!.Value)
                .OrderBy(s => s)
                .ToList();

            var percentile = allAverages.Count > 0
                ? Math.Round(allAverages.Count(s => s <= userScore) * 100.0 / allAverages.Count, 1)
                : 50.0;

            var cohortAvg = allAverages.Count > 0 ? Math.Round(allAverages.Average(), 1) : 0;
            var cohortMedian = allAverages.Count > 0 ? allAverages[allAverages.Count / 2] : 0;

            // Score gap to target
            var goal = await db.Goals.FirstOrDefaultAsync(g => g.UserId == userId, ct);
            double? targetScore = null;
            if (goal is not null)
            {
                targetScore = subtest switch
                {
                    "writing" => goal.TargetWritingScore,
                    "speaking" => goal.TargetSpeakingScore,
                    "reading" => goal.TargetReadingScore,
                    "listening" => goal.TargetListeningScore,
                    _ => null
                };
            }

            results.Add(new
            {
                subtestCode = subtest,
                yourScore = Math.Round(userScore, 1),
                percentile,
                cohortAverage = cohortAvg,
                cohortMedian,
                cohortSize = allAverages.Count,
                targetScore,
                gapToTarget = targetScore.HasValue ? Math.Round(targetScore.Value - userScore, 1) : (double?)null,
                tier = percentile >= 90 ? "top10" : percentile >= 75 ? "top25" : percentile >= 50 ? "aboveMedian" : "belowMedian"
            });
        }

        return new { subtests = results, generatedAt = DateTimeOffset.UtcNow };
    }

    // ═══════════════════════════════════════════════════════════════
    // E6: Mock — Exam Simulation Configuration
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetExamSimulationConfigAsync(string userId, CancellationToken ct)
    {
        var goal = await db.Goals.FirstOrDefaultAsync(g => g.UserId == userId, ct);
        var examType = OetLearner.Api.Services.Common.ExamCodes.NormalizeOrNull(goal?.ExamTypeCode) ?? OetLearner.Api.Services.Common.ExamCodes.DefaultCode;

        // Count user's completed mocks to determine readiness for simulation
        var completedMocks = await db.Attempts
            .CountAsync(a => a.UserId == userId && a.Context == "mock" && a.State == AttemptState.Completed, ct);

        return new
        {
            examType,
            simulationMode = new
            {
                strictTiming = true,
                noPause = true,
                sequentialSubtests = true,
                noBackNavigation = true,
                showCountdown = true,
                stressIndicators = completedMocks < 3
            },
            subtestTimings = new
            {
                listening = new { durationMinutes = 42, sections = 2 },
                reading = new { durationMinutes = 60, sections = 3 },
                writing = new { durationMinutes = 45, sections = 1 },
                speaking = new { durationMinutes = 20, sections = 2 }
            },
            totalDurationMinutes = 167,
            completedSimulations = completedMocks,
            recommendation = completedMocks < 3
                ? "Complete at least 3 practice mocks before attempting full simulation."
                : completedMocks < 6
                    ? "Good practice base. Try simulation mode to build test-day confidence."
                    : "Strong preparation. Use simulation mode for final exam readiness check.",
            unlocked = completedMocks >= 2
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // E9: Study Plan — Auto-Regeneration on Drift Detection
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> DetectStudyPlanDriftAsync(string userId, CancellationToken ct)
    {
        var plan = await db.StudyPlans.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (plan is null) return new { hasPlan = false, drift = (object?)null };

        var items = await db.StudyPlanItems
            .Where(i => i.StudyPlanId == plan.Id)
            .OrderBy(i => i.DueDate)
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var overdue = items.Where(i => i.DueDate < today && i.Status == StudyPlanItemStatus.NotStarted).ToList();
        var completed = items.Where(i => i.Status == StudyPlanItemStatus.Completed).ToList();
        var total = items.Count;
        var expectedCompleted = items.Count(i => i.DueDate <= today);
        var actualCompleted = completed.Count;
        var completionRate = expectedCompleted > 0 ? Math.Round(actualCompleted * 100.0 / expectedCompleted, 1) : 100;
        var driftDays = overdue.Count > 0 ? (today.ToDateTime(TimeOnly.MinValue) - overdue.First().DueDate.ToDateTime(TimeOnly.MinValue)).Days : 0;

        var driftLevel = driftDays > 14 ? "severe" : driftDays > 7 ? "moderate" : driftDays > 3 ? "mild" : "on-track";
        var shouldRegenerate = driftLevel is "severe" or "moderate";

        // Auto-enqueue a regen job when drift is moderate+ AND the last
        // regen was more than 24h ago. Cooldown prevents whiplash while
        // still rescuing learners who fall behind for days. (Phase 3b)
        var autoRegenQueued = false;
        if (shouldRegenerate)
        {
            var hoursSinceLastRegen = (DateTimeOffset.UtcNow - plan.GeneratedAt).TotalHours;
            if (hoursSinceLastRegen >= 24)
            {
                var existingQueued = await db.BackgroundJobs.AnyAsync(
                    j => j.Type == JobType.StudyPlanRegeneration
                        && j.ResourceId == plan.Id
                        && (j.State == AsyncState.Queued || j.State == AsyncState.Processing),
                    ct);

                if (!existingQueued)
                {
                    var payloadJson = JsonSupport.Serialize(new { userId, trigger = "DriftRecovery" });
                    await QueueJobAsync(JobType.StudyPlanRegeneration, resourceId: plan.Id, payloadJson: payloadJson, cancellationToken: ct);
                    plan.State = AsyncState.Queued;
                    await db.SaveChangesAsync(ct);
                    autoRegenQueued = true;
                }
            }
        }

        // Breakdown by subtest
        var subtestDrift = items
            .GroupBy(i => i.SubtestCode)
            .Select(g => new
            {
                subtestCode = g.Key,
                total = g.Count(),
                completed = g.Count(i => i.Status == StudyPlanItemStatus.Completed),
                overdue = g.Count(i => i.DueDate < today && i.Status == StudyPlanItemStatus.NotStarted),
                completionRate = g.Count(i => i.DueDate <= today) > 0
                    ? Math.Round(g.Count(i => i.Status == StudyPlanItemStatus.Completed) * 100.0 / g.Count(i => i.DueDate <= today), 1) : 100
            })
            .ToList();

        return new
        {
            hasPlan = true,
            planId = plan.Id,
            drift = new
            {
                level = driftLevel,
                overdueItems = overdue.Count,
                oldestOverdueDays = driftDays,
                completionRate,
                expectedCompleted,
                actualCompleted,
                totalItems = total,
                shouldRegenerate,
                autoRegenQueued,
                recommendation = driftLevel switch
                {
                    "severe" => "You're significantly behind schedule. We strongly recommend regenerating your study plan to align with your current pace.",
                    "moderate" => "You're falling behind. Consider regenerating your plan or catching up on priority items.",
                    "mild" => "Slightly behind but manageable. Focus on overdue items this week.",
                    _ => "Great job! You're on track with your study plan."
                }
            },
            subtestDrift,
            overdueItems = overdue.Select(i => new { i.Id, i.Title, i.SubtestCode, i.DueDate, daysOverdue = (today.ToDateTime(TimeOnly.MinValue) - i.DueDate.ToDateTime(TimeOnly.MinValue)).Days }).Take(10)
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // E10: Billing Upgrade Path Visibility
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetBillingUpgradePathAsync(string userId, CancellationToken ct)
    {
        // Get user's current subscription
        var subscription = await db.Subscriptions
            .Where(s => s.UserId == userId && s.Status == SubscriptionStatus.Active)
            .FirstOrDefaultAsync(ct);

        var currentPlanId = subscription?.PlanId;
        var allPlans = await db.BillingPlans
            .Where(p => p.IsVisible && p.Status == BillingPlanStatus.Active)
            .OrderBy(p => p.DisplayOrder)
            .ToListAsync(ct);

        var currentPlan = currentPlanId is not null ? allPlans.FirstOrDefault(p => p.Id == currentPlanId) : null;

        // Get usage stats
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        var reviewsUsedThisMonth = await db.ReviewRequests
            .CountAsync(r => db.Attempts.Any(a => a.Id == r.AttemptId && a.UserId == userId)
                && r.CreatedAt >= DateTimeOffset.UtcNow.AddDays(-30), ct);

        var planComparison = allPlans.Select(p =>
        {
            Dictionary<string, object>? entitlements = null;
            try { entitlements = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(p.EntitlementsJson); } catch { }

            return new
            {
                planId = p.Id,
                planCode = p.Code,
                planName = p.Name,
                description = p.Description,
                price = p.Price,
                currency = p.Currency,
                interval = p.Interval,
                includedCredits = p.IncludedCredits,
                trialDays = p.TrialDays,
                isCurrent = p.Id == currentPlanId,
                isUpgrade = currentPlan is not null && p.Price > currentPlan.Price,
                isDowngrade = currentPlan is not null && p.Price < currentPlan.Price,
                entitlements = entitlements ?? new Dictionary<string, object>()
            };
        }).ToList();

        return new
        {
            currentPlan = currentPlan is not null ? new
            {
                planId = currentPlan.Id,
                planName = currentPlan.Name,
                price = currentPlan.Price,
                includedCredits = currentPlan.IncludedCredits
            } : null,
            usage = new
            {
                reviewsUsedThisMonth,
                creditsRemaining = wallet?.CreditBalance ?? 0,
                subscriptionStarted = subscription?.StartedAt,
                subscriptionEnds = subscription?.NextRenewalAt
            },
            plans = planComparison,
            recommendation = currentPlan is null
                ? "Start with a plan to unlock tutor reviews, mock exams, and AI-powered feedback."
                : reviewsUsedThisMonth >= (currentPlan.IncludedCredits * 0.8)
                    ? "You've used most of your included reviews. Consider upgrading for more credits."
                    : "Your current plan is meeting your usage needs."
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // L9: Interleaved Practice Mode
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetInterleavedPracticeSessionAsync(string userId, int durationMinutes, CancellationToken ct)
    {
        var targetMinutes = durationMinutes > 0 ? Math.Min(durationMinutes, 60) : 20;
        var subtests = new[] { "reading", "listening", "writing", "speaking" };
        var sessionItems = new List<object>();
        var allocatedMinutes = 0;

        // Get user's weakest areas to weight task selection
        var recentEvals = await db.Evaluations
            .AsNoTracking()
            .Where(e => db.Attempts.Any(a => a.Id == e.AttemptId && a.UserId == userId)
                && e.GeneratedAt >= DateTimeOffset.UtcNow.AddDays(-30))
            .Select(e => new { e.SubtestCode, e.ScoreRange })
            .ToListAsync(ct);

        var subtestScores = recentEvals
            .GroupBy(e => e.SubtestCode)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var scores = g.Select(e =>
                    {
                        var parts = e.ScoreRange?.Split('-');
                        return parts?.Length == 2 && int.TryParse(parts[0], out var lo) ? lo : 300;
                    }).ToList();
                    return scores.Count > 0 ? scores.Average() : 300.0;
                });

        // Prioritize weaker subtests
        var orderedSubtests = subtests
            .OrderBy(s => subtestScores.GetValueOrDefault(s, 300.0))
            .ToList();
        var availableContentCounts = await db.ContentItems
            .AsNoTracking()
            .Where(content =>
                subtests.Contains(content.SubtestCode)
                && content.Status == ContentStatus.Published)
            .GroupBy(content => content.SubtestCode)
            .Select(group => new { Subtest = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Subtest, row => row.Count, ct);

        int taskIndex = 0;
        while (allocatedMinutes < targetMinutes)
        {
            var subtest = orderedSubtests[taskIndex % orderedSubtests.Count];
            var taskDuration = subtest switch
            {
                "reading" => 8,
                "listening" => 7,
                "writing" => 10,
                "speaking" => 5,
                _ => 7
            };

            if (allocatedMinutes + taskDuration > targetMinutes + 3) break;

            // Get adaptive content for this subtest
            var contentCount = availableContentCounts.GetValueOrDefault(subtest);
            var randomOffset = contentCount > 0
                ? RandomNumberGenerator.GetInt32(contentCount)
                : 0;
            var content = contentCount == 0
                ? null
                : await db.ContentItems
                    .AsNoTracking()
                    .Where(c => c.SubtestCode == subtest && c.Status == ContentStatus.Published)
                    .OrderBy(c => c.Id)
                    .Skip(randomOffset)
                    .Select(c => new { c.Id, c.Title, c.SubtestCode, c.EstimatedDurationMinutes, c.Difficulty })
                    .FirstOrDefaultAsync(ct);

            if (content is not null)
            {
                sessionItems.Add(new
                {
                    order = taskIndex + 1,
                    contentId = content.Id,
                    title = content.Title,
                    subtestCode = content.SubtestCode,
                    taskType = subtest switch
                    {
                        "reading" => "passage-comprehension",
                        "listening" => "audio-exercise",
                        "writing" => "short-response",
                        "speaking" => "pronunciation-drill",
                        _ => "practice"
                    },
                    durationMinutes = taskDuration,
                    difficulty = content.Difficulty,
                    isWeakArea = subtestScores.GetValueOrDefault(subtest, (double)OetScoring.ScaledPassGradeCPlus) < OetScoring.ScaledPassGradeB
                });
                allocatedMinutes += taskDuration;
            }
            taskIndex++;
            if (taskIndex > 20) break; // Safety cap
        }

        return new
        {
            sessionId = Guid.NewGuid().ToString(),
            targetDurationMinutes = targetMinutes,
            actualDurationMinutes = allocatedMinutes,
            taskCount = sessionItems.Count,
            tasks = sessionItems,
            scienceBasis = "Interleaving different skill types in a single session improves long-term retention (Rohrer & Taylor, 2007).",
            tips = new[]
            {
                "Don't skip tasks — the variety is intentional for better learning.",
                "Take 30-second breaks between tasks to reset your focus.",
                "Review any mistakes immediately after each task."
            }
        };
    }

    // ── L12: Peer Review Exchange ────────────────────────────

    public async Task<object> GetPeerReviewPoolAsync(string userId, CancellationToken ct)
    {
        // Available peer review requests from other learners (not mine, not claimed)
        var available = await db.PeerReviewRequests
            .Where(r => r.SubmitterUserId != userId && r.Status == "open")
            .OrderByDescending(r => r.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        // My submissions
        var mySubmissions = await db.PeerReviewRequests
            .Where(r => r.SubmitterUserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(10)
            .ToListAsync(ct);

        // My reviews (claimed by me)
        var myReviews = await db.PeerReviewRequests
            .Where(r => r.ReviewerUserId == userId)
            .OrderByDescending(r => r.ClaimedAt)
            .Take(10)
            .ToListAsync(ct);

        // Get feedback for my submissions
        var mySubmissionIds = mySubmissions.Select(s => s.Id).ToList();
        var feedbackForMe = await db.PeerReviewFeedbacks
            .Where(f => mySubmissionIds.Contains(f.PeerReviewRequestId))
            .ToListAsync(ct);

        return new
        {
            availableToReview = available.Select(r => new { id = r.Id, subtestCode = r.SubtestCode, attemptId = r.AttemptId, createdAt = r.CreatedAt }),
            mySubmissions = mySubmissions.Select(s => new { id = s.Id, subtestCode = s.SubtestCode, status = s.Status, createdAt = s.CreatedAt, feedback = feedbackForMe.Where(f => f.PeerReviewRequestId == s.Id).Select(f => new { rating = f.OverallRating, comments = f.Comments, strengths = f.StrengthNotes, improvements = f.ImprovementNotes }) }),
            myReviews = myReviews.Select(r => new { id = r.Id, subtestCode = r.SubtestCode, status = r.Status, claimedAt = r.ClaimedAt, completedAt = r.CompletedAt }),
            stats = new
            {
                reviewsGiven = myReviews.Count(r => r.Status == "completed"),
                reviewsReceived = feedbackForMe.Count,
                averageHelpfulness = feedbackForMe.Where(f => f.HelpfulnessRating > 0).Select(f => (double)f.HelpfulnessRating).DefaultIfEmpty(0).Average()
            }
        };
    }

    public async Task<object> SubmitForPeerReviewAsync(string userId, string attemptId, string subtestCode, CancellationToken ct)
    {
        var attempt = await db.Attempts.FindAsync([attemptId], ct)
            ?? throw new InvalidOperationException("Attempt not found.");

        if (attempt.UserId != userId) throw new InvalidOperationException("Not your attempt.");

        var existing = await db.PeerReviewRequests.AnyAsync(r => r.AttemptId == attemptId && r.Status != "expired", ct);
        if (existing) throw new InvalidOperationException("Already submitted for peer review.");

        var request = new PeerReviewRequest
        {
            Id = $"pr-{Guid.NewGuid():N}",
            SubmitterUserId = userId,
            AttemptId = attemptId,
            SubtestCode = subtestCode.ToLowerInvariant(),
            Status = "open",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.PeerReviewRequests.Add(request);
        await db.SaveChangesAsync(ct);
        return new { id = request.Id, status = "open" };
    }

    public async Task<object> ClaimPeerReviewAsync(string userId, string peerReviewId, CancellationToken ct)
    {
        var request = await db.PeerReviewRequests.FindAsync([peerReviewId], ct)
            ?? throw new InvalidOperationException("Peer review request not found.");

        if (request.SubmitterUserId == userId) throw new InvalidOperationException("Cannot review your own submission.");
        if (request.Status != "open") throw new InvalidOperationException("Already claimed or completed.");

        request.ReviewerUserId = userId;
        request.Status = "claimed";
        request.ClaimedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return new { id = request.Id, status = "claimed" };
    }

    public async Task<object> SubmitPeerFeedbackAsync(string userId, string peerReviewId, int overallRating, string comments, string? strengths, string? improvements, CancellationToken ct)
    {
        var request = await db.PeerReviewRequests.FindAsync([peerReviewId], ct)
            ?? throw new InvalidOperationException("Peer review request not found.");

        if (request.ReviewerUserId != userId) throw new InvalidOperationException("Not assigned to you.");
        if (request.Status != "claimed") throw new InvalidOperationException("Not in claimable state.");

        var feedback = new PeerReviewFeedback
        {
            Id = $"prf-{Guid.NewGuid():N}",
            PeerReviewRequestId = peerReviewId,
            ReviewerUserId = userId,
            OverallRating = Math.Clamp(overallRating, 1, 5),
            Comments = comments.Trim(),
            StrengthNotes = strengths?.Trim(),
            ImprovementNotes = improvements?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.PeerReviewFeedbacks.Add(feedback);
        request.Status = "completed";
        request.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return new { feedbackId = feedback.Id, status = "completed" };
    }

    // ── Learner Escalation / Dispute ────────────────────────────

    public async Task<object> SubmitEscalationAsync(
        string userId, string submissionId, string reason, string details, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(submissionId))
            throw new InvalidOperationException("Submission ID is required.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Reason is required.");
        if (string.IsNullOrWhiteSpace(details))
            throw new InvalidOperationException("Details are required.");

        var existing = await db.LearnerEscalations
            .AnyAsync(e => e.UserId == userId && e.SubmissionId == submissionId && e.Status == "Pending", ct);
        if (existing)
            throw new InvalidOperationException("An escalation for this submission is already pending.");

        var escalation = new LearnerEscalation
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            SubmissionId = submissionId,
            Reason = reason,
            Details = details,
            Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.LearnerEscalations.Add(escalation);
        await db.SaveChangesAsync(ct);
        return new { escalationId = escalation.Id, status = escalation.Status };
    }

    public async Task<object> GetMyEscalationsAsync(string userId, CancellationToken ct)
    {
        var items = await db.LearnerEscalations
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(200)
            .Select(e => new
            {
                id = e.Id,
                submissionId = e.SubmissionId,
                reason = e.Reason,
                status = e.Status,
                createdAt = e.CreatedAt,
                updatedAt = e.UpdatedAt
            })
            .ToListAsync(ct);

        return new { items, total = items.Count };
    }

    public async Task<object> GetEscalationDetailsAsync(string userId, string escalationId, CancellationToken ct)
    {
        var esc = await db.LearnerEscalations
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == escalationId && e.UserId == userId, ct)
            ?? throw new InvalidOperationException("Escalation not found.");

        return new
        {
            id = esc.Id,
            submissionId = esc.SubmissionId,
            reason = esc.Reason,
            details = esc.Details,
            status = esc.Status,
            createdAt = esc.CreatedAt,
            updatedAt = esc.UpdatedAt
        };
    }
}
