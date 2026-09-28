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
    IPasswordHasher<ApplicationUserAccount>? passwordHasher = null,
    ILogger<LearnerService>? logger = null,
    IAssessmentScoreConversionService? scoreConversionService = null,
    IAssessmentMarkingPolicyService? markingPolicyService = null,
    IPaymentGatewayCatalog? paymentGatewayCatalog = null,
    global::OetLearner.Api.Services.Settings.IRuntimeSettingsProvider? runtimeSettings = null,
    OetLearner.Api.Services.Billing.BillingReconciliationWorker? billingReconciliation = null,
    IFreeTierContentResolver? freeTierContentResolver = null)
{
    private const string PaymentWebhookParserVersion = "payment-webhook-v1";

    /// <summary>
    /// How many signature/verification REJECTIONS per gateway we are willing to
    /// record per hour. Payment webhook endpoints are unauthenticated and
    /// deliberately unthrottled, so the audit trail for rejected deliveries needs
    /// its own bound; past this we log and drop rather than let varied junk
    /// payloads grow the table. Identical replays collapse onto one row via the
    /// unique (Gateway, GatewayEventId) index and never reach this cap.
    /// </summary>
    private const int RejectedWebhookAuditCapPerHour = 50;
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
        int? ScaledScore,
        string? CriterionScoresJson,
        string? ScoreConversionTableVersionKey,
        bool? ScoreConversionPassed);

    private sealed record ProgressEvaluationRow(
        string SubtestCode,
        string? ScoreRange,
        int? ScaledScore,
        string CriterionScoresJson,
        DateTimeOffset? GeneratedAt,
        string? ScoreConversionTableVersionKey,
        bool? ScoreConversionPassed);

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
        string SubtestCode,
        string? ScoreRange,
        int? ScaledScore,
        string? ScoreConversionTableVersionKey,
        bool? ScoreConversionPassed);

    private sealed record WritingHomeEvaluationRow(
        Evaluation Evaluation,
        Attempt Attempt,
        ContentItem Content,
        bool IsLatest);

    // ── Onboarding product tours ────────────────────────────────────────────
    // Per-user guided-tour completion state used by the client tour engine so a
    // tour is never auto-replayed once seen, and so tours can be re-surfaced after
    // a major content revision (LastSeenTourVersion < OnboardingTourVersion). The
    // rich tour_* analytics (with step/route detail) are emitted client-side; these
    // methods only persist durable completion/skip/dismiss state. Keyed by the auth
    // user id, so they also serve expert/tutor and admin workspaces.
    public const int OnboardingTourVersion = 1;

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
                    RegisteredTargetExamDate = registration == null ? (DateOnly?)null : registration.TargetExamDate,
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
                loaded.RegisteredTargetExamDate,
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
        var registeredTargetExamDate = await ResolveRegisteredTargetExamDateAsync(userId, cancellationToken);

        var goal = await db.Goals.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (goal is null)
        {
            goal = CreateDefaultGoal(userId, user.ActiveProfessionId, registeredTargetCountry, registeredTargetExamDate, now);
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

    private async Task<DateOnly?> ResolveRegisteredTargetExamDateAsync(string userId, CancellationToken cancellationToken)
    {
        return await db.LearnerRegistrationProfiles
            .AsNoTracking()
            .Where(x => x.LearnerUserId == userId)
            .Select(x => x.TargetExamDate)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static bool ShouldRestoreRegisteredTargetCountry(LearnerGoal goal, string registeredTargetCountry)
    {
        if (string.IsNullOrWhiteSpace(goal.TargetCountry)) return true;
        if (goal.SubmittedAt is not null) return false;
        if (string.Equals(registeredTargetCountry, "Australia", StringComparison.Ordinal)) return false;
        return string.Equals(goal.TargetCountry, "Australia", StringComparison.OrdinalIgnoreCase);
    }

    private static LearnerGoal CreateDefaultGoal(string userId, string? professionId, string targetCountry, DateOnly? registeredTargetExamDate, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProfessionId = string.IsNullOrWhiteSpace(professionId) ? "nursing" : professionId,
            TargetExamDate = registeredTargetExamDate ?? DateOnly.FromDateTime(now.UtcDateTime.AddMonths(3)),
            TargetExamDateSetByUser = registeredTargetExamDate.HasValue,
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

    private static readonly System.Text.RegularExpressions.Regex ProvenanceToken =
        new(@"\s*\[[^\]]*\]\s*$", System.Text.RegularExpressions.RegexOptions.Compiled);

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

    private static double? ParseScoreRangeMid(string? scoreRange)
    {
        if (string.IsNullOrWhiteSpace(scoreRange)) return null;
        var parts = scoreRange.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2
            && double.TryParse(parts[0], out var low)
            && double.TryParse(parts[1], out var high))
        {
            return (low + high) / 2.0;
        }

        return double.TryParse(scoreRange.Trim(), out var single) ? single : null;
    }

    private static bool IsGovernedScoreAvailable(
        string? subtestCode,
        string? scoreRange,
        int? scaledScore,
        string? scoreConversionTableVersionKey,
        bool? scoreConversionPassed)
        => !IsListeningOrReading(subtestCode)
            || (HasCanonicalListeningReadingRawMaximum(scoreRange)
                && scaledScore.HasValue
                && !string.IsNullOrWhiteSpace(scoreConversionTableVersionKey)
                && scoreConversionPassed.HasValue);

    private static string? GovernedScoreRange(
        string? subtestCode,
        string? scoreRange,
        int? scaledScore,
        string? scoreConversionTableVersionKey,
        bool? scoreConversionPassed)
        => IsGovernedScoreAvailable(subtestCode, scoreRange, scaledScore, scoreConversionTableVersionKey, scoreConversionPassed)
            ? scoreRange
            : null;

    private static bool HasCanonicalListeningReadingRawMaximum(string? scoreRange)
    {
        if (string.IsNullOrWhiteSpace(scoreRange)) return false;
        var rawSegment = scoreRange.Split('•', 2, StringSplitOptions.TrimEntries)[0];
        return rawSegment.EndsWith("/ 42", StringComparison.Ordinal)
            || rawSegment.EndsWith("/42", StringComparison.Ordinal);
    }

    private static bool IsListeningOrReading(string? subtestCode)
        => subtestCode?.Trim().ToLowerInvariant() is "listening" or "reading";

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

    /// <summary>Gateways with a server-to-server payment status lookup that
    /// <see cref="OetLearner.Api.Services.Billing.BillingReconciliationWorker.RecoverPaymentAsync"/>
    /// can query. Stripe/PayPal are excluded here — their webhook path is fast enough that
    /// this live safety net was never built for them, and they expose no such lookup on
    /// the shared gateway contract.</summary>
    private static readonly string[] LivePollReconciliationGateways =
    [
        PaymentGatewayNames.Whop,
        PaymentGatewayNames.Paymob,
        PaymentGatewayNames.PayTabs,
        PaymentGatewayNames.CheckoutCom,
        PaymentGatewayNames.EasyKash,
    ];

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
            "expired" => "Your checkout session has expired. Please start a new checkout.",
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
}
