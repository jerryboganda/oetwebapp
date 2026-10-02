using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

public static class AiPackageCreditSources
{
    public static string Addon(string subscriptionId, string addOnCode, string? sourceSuffix = null)
        => AddonGrantProcessor.FitDatabaseKey(
            string.IsNullOrWhiteSpace(sourceSuffix)
                ? $"addon:{subscriptionId}:{addOnCode}"
                : $"addon:{subscriptionId}:{addOnCode}:{sourceSuffix}");

    public static string Plan(string subscriptionId, string planCode)
        => AddonGrantProcessor.FitDatabaseKey($"plan:{subscriptionId}:{planCode}");

    public static string AdminPackage(string subscriptionId, string planCode)
        => AddonGrantProcessor.FitDatabaseKey($"admin-package:{subscriptionId}:{planCode}");

    /// <summary>
    /// Every plan-shaped source key owned by one subscription. Removal,
    /// refund, and orphan-sweep paths must use this set — never re-derive
    /// the pair — so a new key shape lands in all three at once.
    /// </summary>
    public static IReadOnlyList<string> PlanKeys(string subscriptionId, string planCode)
        => [Plan(subscriptionId, planCode), AdminPackage(subscriptionId, planCode)];

    /// <summary>
    /// Reversal key for an "addon:"/"plan:" AI-credit purchase reference, shared by
    /// the webhook and admin refund paths so both hit the same idempotency row.
    /// Inverse of the parser in AiCreditService; the format must not change.
    /// </summary>
    public static string? RefundReference(string purchaseReferenceId)
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
}

public interface IAiPackageCreditService
{
    Task<AiPackageCreditSnapshot> GetSnapshotAsync(string userId, int transactionLimit, CancellationToken ct);
    Task<AiPackageCreditSnapshot> GrantPackageAsync(
        string userId,
        BillingAddOn addOn,
        int quantity,
        string stripeSessionId,
        string? quoteId,
        CancellationToken ct,
        string? sourceReferenceId = null,
        DateTimeOffset? validFrom = null);

    /// <summary>
    /// Grant Full Course gifted AI credits into the Shared pool. Idempotent on
    /// <paramref name="referenceId"/>. Dedicated/Flexible W/S cost 1 activity;
    /// Shared Writing/Speaking cost 2; Listening/Reading cost 1 Shared.
    /// </summary>
    Task<bool> GrantCourseGiftCreditsAsync(
        string userId,
        string planCode,
        string planName,
        int credits,
        string referenceId,
        DateTimeOffset? expiresAt,
        CancellationToken ct,
        string? sourceReferenceId = null,
        DateTimeOffset? validFrom = null);
    Task<AiPackageDebitResult> DeductGradingCreditAsync(string userId, string subtest, string referenceId, CancellationToken ct);

    /// <summary>
    /// Consume <paramref name="quantity"/> Writing/Speaking activities in one
    /// atomic debit. Priority: dedicated ΓåÆ Flexible W/S ΓåÆ Shared last.
    /// One activity costs 1 dedicated/Flexible unit or 2 Shared units.
    /// </summary>
    Task<AiPackageDebitResult> DeductGradingCreditAsync(string userId, string subtest, string referenceId, int quantity, CancellationToken ct);

    /// <summary>
    /// Read-only mirror of <see cref="DeductGradingCreditAsync"/>. The debit
    /// itself happens once at attempt or session start.
    /// </summary>
    Task<AiPackageDebitResult> CheckGradingCreditAsync(string userId, string subtest, CancellationToken ct);

    /// <summary>
    /// Read-only mirror that verifies at least <paramref name="quantity"/>
    /// grading credits are available (used to gate multi-credit exams at start).
    /// </summary>
    Task<AiPackageDebitResult> CheckGradingCreditAsync(string userId, string subtest, int quantity, CancellationToken ct);

    /// <summary>
    /// The grading debit already booked under <paramref name="referenceId"/>, or null.
    /// Lets a Writing grade adopt the debit the start gate took when the task opened
    /// instead of charging the letter twice (WAI-01). Default = none, so ledger fakes
    /// keep their behaviour; the real service overrides it.
    /// </summary>
    Task<AiPackageCreditTransaction?> FindGradingDebitAsync(string userId, string referenceId, CancellationToken ct)
        => Task.FromResult<AiPackageCreditTransaction?>(null);

    Task<AiPackageDebitResult> DeductObjectivePracticeAsync(string userId, string subtest, string referenceId, CancellationToken ct);
    Task<AiPackageDebitResult> DeductMockAsync(string userId, string referenceId, CancellationToken ct);
    Task<bool> RefundAsync(string userId, string originalReferenceId, string refundReferenceId, string description, CancellationToken ct);
    Task<AiPackageCreditSnapshot> AdjustAsync(string userId, AiPackageCreditAdjustmentRequest request, string adminId, CancellationToken ct);
    Task<AiPackageCreditSnapshot> RecordExamOutcomeAsync(string userId, LearnerExamOutcomeRequest request, string adminId, string adminName, CancellationToken ct);

    /// <summary>
    /// Reverse unreversed Purchase rows for the exact grant source. Product
    /// codes are not sufficient because a learner may own the same package twice.
    /// </summary>
    Task<int> ReverseGrantsAsync(string userId, string sourceReferenceId, CancellationToken ct);

    /// <summary>
    /// If unlimited Listening/Reading was lost because the last unlimited
    /// add-on item was cancelled, drop the null sentinel back to a finite pool.
    /// Also expires orphaned unlimited lots (including legacy lots with no
    /// source attribution) and repairs stuck null pools, so a deleted/revoked
    /// source can never keep contributing Unlimited. Finite manual
    /// (admin-adjust) lots are independent sources and are never touched.
    /// </summary>
    Task RecalculateObjectiveAllowancesAsync(string userId, CancellationToken ct);

    /// <summary>
    /// Reversibly park every AI-credit lot granted from
    /// <paramref name="subscriptionId"/> (course-gift lots, plan lots and
    /// add-on lots attached to it): balances and unlimited flags are preserved
    /// but flagged <c>Expired</c> so the effective entitlement drops to zero
    /// immediately. Paired with <see cref="UnparkSubscriptionLotsAsync"/>.
    /// Used by suspend/cancel/expire flows; idempotent.
    /// </summary>
    Task ParkSubscriptionLotsAsync(string userId, string subscriptionId, CancellationToken ct);

    /// <summary>
    /// Undo <see cref="ParkSubscriptionLotsAsync"/>: revive parked lots of
    /// <paramref name="subscriptionId"/> whose validity window is still open.
    /// Lots zeroed by a real reversal and lots past their validity end are
    /// never revived. Idempotent.
    /// </summary>
    Task UnparkSubscriptionLotsAsync(string userId, string subscriptionId, CancellationToken ct);

    /// <summary>
    /// Re-sync the valid-until of course-gifted AI credit lots that were granted
    /// from <paramref name="subscriptionId"/> (SourceReferenceId
    /// <c>admin-package:{subscriptionId}:{planCode}</c>) to the edited package
    /// expiry (admin date override). Only the linked lot's validity changes; Used
    /// history is untouched and unrelated credits are never modified.
    /// </summary>
    Task UpdateGrantExpiryAsync(string userId, string subscriptionId, DateTimeOffset? expiresAt, CancellationToken ct);
    Task UpdateGrantWindowAsync(
        string userId,
        string subscriptionId,
        DateTimeOffset? validFrom,
        DateTimeOffset? expiresAt,
        CancellationToken ct);

    /// <summary>
    /// Read-only check whether the learner holds an active objective-practice
    /// allowance for <paramref name="subtest"/> (listening/reading). True when
    /// an unexpired lot grants unlimited OR remaining dedicated tests &gt; 0 OR
    /// shared credits &gt;= 1. Mirrors <see cref="DeductObjectivePracticeAsync"/>
    /// without consuming. Used by <c>ContentEntitlementService</c> so a
    /// standalone Reading/Listening Pro purchase can open papers even without
    /// an eligible course subscription.
    /// </summary>
    Task<bool> HasObjectivePracticeAllowanceAsync(string userId, string subtest, CancellationToken ct);
}

public sealed record AiPackageCreditBucketSnapshot(
    int TotalGranted,
    int Used,
    int Remaining,
    bool Unlimited,
    IReadOnlyList<string> SourcePackages,
    DateTimeOffset? ExpiresAt,
    int? DaysLeft);

public sealed record AiPackageOpenedActivityDto(
    string Id,
    string Title,
    string Subtest,
    string Status,
    DateTimeOffset StartedAt,
    string? AuthorizingPackage,
    int CreditsUsed,
    int RemainingAfterStart);

public sealed record AiPackageCreditSnapshot(
    string UserId,
    int FlexibleCredits,
    int WritingOnlyCredits,
    int SpeakingOnlyCredits,
    int? ListeningTestsRemaining,
    int? ReadingTestsRemaining,
    int MockExamsRemaining,
    DateTimeOffset? ExpiresAt,
    bool ExpiredBecausePassed,
    DateTimeOffset? PassedAt,
    IReadOnlyList<AiPackageCreditTransactionDto> Transactions,
    int CreditsGranted = 0,
    int CreditsUsed = 0,
    int CreditsRemaining = 0,
    bool WritingUnlimited = false,
    bool SpeakingUnlimited = false,
    int SharedCredits = 0,
    int SharedCreditsGranted = 0,
    int SharedCreditsUsed = 0,
    IReadOnlyList<AiPackageCreditBucketDto>? Buckets = null,
    bool ListeningUnlimited = false,
    bool ReadingUnlimited = false,
    AiPackageCreditBucketSnapshot? Shared = null,
    AiPackageCreditBucketSnapshot? Flexible = null,
    AiPackageCreditBucketSnapshot? Writing = null,
    AiPackageCreditBucketSnapshot? Speaking = null,
    AiPackageCreditBucketSnapshot? Listening = null,
    AiPackageCreditBucketSnapshot? Reading = null,
    AiPackageCreditBucketSnapshot? Mocks = null,
    IReadOnlyList<AiPackageOpenedActivityDto>? Activities = null)
{
    // FINAL 2026-09-06: one Writing letter / one Speaking card costs 2 AI
    // credits from ANY pool. A lone single credit can never fund an
    // activity on its own (shared funds whole activities only), so
    // fundability is simulated with the same greedy order the debit uses:
    // dedicated first, then Flexible W/S, then Shared.
    public bool HasWritingActivity =>
        WritingUnlimited || FundableWritingOrSpeakingActivities(WritingOnlyCredits, FlexibleCredits, SharedCredits) >= 1;

    public bool HasSpeakingActivity =>
        SpeakingUnlimited || FundableWritingOrSpeakingActivities(SpeakingOnlyCredits, FlexibleCredits, SharedCredits) >= 1;

    public int AvailableWritingActivities =>
        WritingUnlimited ? int.MaxValue : FundableWritingOrSpeakingActivities(WritingOnlyCredits, FlexibleCredits, SharedCredits);

    public int AvailableSpeakingActivities =>
        SpeakingUnlimited ? int.MaxValue : FundableWritingOrSpeakingActivities(SpeakingOnlyCredits, FlexibleCredits, SharedCredits);

    /// <summary>
    /// Complete Writing/Speaking activities fundable from raw pool balances.
    /// Mirrors <c>SpendWritingOrSpeaking</c> exactly (dedicated, then
    /// Flexible W/S, then whole Shared activities) so gates and debits agree.
    /// </summary>
    public static int FundableWritingOrSpeakingActivities(int dedicated, int flexible, int shared)
    {
        var units = AiGradingCreditCost.CreditsPerWritingOrSpeakingActivity;
        var remainingDedicated = dedicated;
        var remainingFlexible = flexible;
        var remainingShared = shared;
        var activities = 0;
        while (true)
        {
            var need = units;
            var takeDedicated = Math.Min(remainingDedicated, need);
            need -= takeDedicated;
            var takeFlexible = Math.Min(remainingFlexible, need);
            need -= takeFlexible;
            if (need == 0)
            {
                remainingDedicated -= takeDedicated;
                remainingFlexible -= takeFlexible;
                activities++;
                continue;
            }
            if (need == units && remainingShared >= units)
            {
                remainingShared -= units;
                activities++;
                continue;
            }
            return activities;
        }
    }
}

/// <summary>
/// Candidate/admin-visible per-bucket balance conforming to the Master
/// Catalogue dashboard card: Total granted/purchased, Used, Remaining (or
/// Unlimited), source package(s), validity window and days left.
/// </summary>
public sealed record AiPackageCreditBucketDto(
    string Key,
    string Label,
    bool Unlimited,
    int TotalGranted,
    int Used,
    int Remaining,
    string? SourcePackages,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ExpiresAt,
    int DaysLeft,
    IReadOnlyList<AiPackageCreditGrantSourceDto> Grants);

public sealed record AiPackageCreditGrantSourceDto(
    string? PackageId,
    string Description,
    int TotalGranted,
    DateTimeOffset GrantedAt,
    DateTimeOffset? ExpiresAt,
    string? SourceReferenceId = null,
    DateTimeOffset? ValidFrom = null,
    int? DaysLeft = null);

public sealed record AiPackageCreditTransactionDto(
    string Id,
    string? PackageId,
    string? PackageType,
    string Reason,
    int SharedCreditsDelta,
    int FlexibleCreditsDelta,
    int WritingOnlyCreditsDelta,
    int SpeakingOnlyCreditsDelta,
    int ListeningTestsDelta,
    int ReadingTestsDelta,
    int MockExamsDelta,
    string? ReferenceId,
    string Description,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    string? SourceReferenceId = null,
    DateTimeOffset? ValidFrom = null);

public sealed record AiPackageDebitResult(
    bool Debited,
    string? ErrorCode,
    string? ErrorMessage,
    string? DebitReferenceId,
    bool Bypassed = false,
    string? BalanceSource = null,
    int CreditsUsed = 0,
    int? RemainingAfter = null,
    string? FeedbackMessage = null);

public sealed record AiPackageCreditAdjustmentRequest(
    int FlexibleCreditsDelta,
    int WritingOnlyCreditsDelta,
    int SpeakingOnlyCreditsDelta,
    int ListeningTestsDelta,
    int ReadingTestsDelta,
    int MockExamsDelta,
    DateTimeOffset? ExpiresAt,
    string? Reason,
    int SharedCreditsDelta = 0,
    int? SharedCreditsSet = null,
    int? FlexibleCreditsSet = null,
    int? WritingOnlyCreditsSet = null,
    int? SpeakingOnlyCreditsSet = null,
    int? ListeningTestsSet = null,
    int? ReadingTestsSet = null,
    int? MockExamsSet = null);

public sealed record LearnerExamOutcomeRequest(bool Passed, DateTimeOffset ExamDate, string? EvidenceNote);
