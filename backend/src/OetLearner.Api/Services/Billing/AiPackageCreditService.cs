using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

file sealed record CreditUsage(int Shared, int Flexible, int Writing, int Speaking, int ListeningTests, int ReadingTests, int MockExams);

public interface IAiPackageCreditService
{
    Task<AiPackageCreditSnapshot> GetSnapshotAsync(string userId, int transactionLimit, CancellationToken ct);
    Task<AiPackageCreditSnapshot> GrantPackageAsync(string userId, BillingAddOn addOn, int quantity, string stripeSessionId, string? quoteId, CancellationToken ct);

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
        CancellationToken ct);
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
    Task<AiPackageDebitResult> DeductObjectivePracticeAsync(string userId, string subtest, string referenceId, CancellationToken ct);
    Task<AiPackageDebitResult> DeductMockAsync(string userId, string referenceId, CancellationToken ct);
    Task<bool> RefundAsync(string userId, string originalReferenceId, string refundReferenceId, string description, CancellationToken ct);
    Task<AiPackageCreditSnapshot> AdjustAsync(string userId, AiPackageCreditAdjustmentRequest request, string adminId, CancellationToken ct);
    Task<AiPackageCreditSnapshot> RecordExamOutcomeAsync(string userId, LearnerExamOutcomeRequest request, string adminId, string adminName, CancellationToken ct);

    /// <summary>
    /// Reverse unreversed Purchase rows for <paramref name="packageId"/> (AI
    /// add-on or Full Course gift). Remaining pools clamp at zero so spent
    /// credits are not restored as a negative balance.
    /// </summary>
    Task<int> ReverseGrantsAsync(string userId, string packageId, CancellationToken ct);

    /// <summary>
    /// If unlimited Listening/Reading was lost because the last unlimited
    /// add-on item was cancelled, drop the null sentinel back to a finite pool.
    /// </summary>
    Task RecalculateObjectiveAllowancesAsync(string userId, CancellationToken ct);

    /// <summary>
    /// Re-sync the valid-until of course-gifted AI credit lots that were granted
    /// from <paramref name="subscriptionId"/> (SourceReferenceId
    /// <c>admin-package:{subscriptionId}:{planCode}</c>) to the edited package
    /// expiry (admin date override). Only the linked lot's validity changes; Used
    /// history is untouched and unrelated credits are never modified.
    /// </summary>
    Task UpdateGrantExpiryAsync(string userId, string subscriptionId, DateTimeOffset? expiresAt, CancellationToken ct);

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
    public bool HasWritingActivity =>
        WritingUnlimited || WritingOnlyCredits >= 1 || FlexibleCredits >= 1 || SharedCredits >= AiGradingCreditCost.SharedWritingOrSpeaking;

    public bool HasSpeakingActivity =>
        SpeakingUnlimited || SpeakingOnlyCredits >= 1 || FlexibleCredits >= 1 || SharedCredits >= AiGradingCreditCost.SharedWritingOrSpeaking;

    public int AvailableWritingActivities =>
        WritingUnlimited ? int.MaxValue : WritingOnlyCredits + FlexibleCredits + (SharedCredits / AiGradingCreditCost.SharedWritingOrSpeaking);

    public int AvailableSpeakingActivities =>
        SpeakingUnlimited ? int.MaxValue : SpeakingOnlyCredits + FlexibleCredits + (SharedCredits / AiGradingCreditCost.SharedWritingOrSpeaking);
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
    DateTimeOffset? ExpiresAt);

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
    DateTimeOffset CreatedAt);

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

public sealed class AiPackageCreditService(LearnerDbContext db, ILogger<AiPackageCreditService> logger) : IAiPackageCreditService
{
    private const string NoCreditsMessage =
        "You do not have enough credits to start this activity. Please purchase another package or upgrade your plan.";

    public async Task<AiPackageCreditSnapshot> GetSnapshotAsync(string userId, int transactionLimit, CancellationToken ct)
    {
        var account = await GetOrCreateAccountAsync(userId, ct);
        await ExpireIfNeededAsync(account, DateTimeOffset.UtcNow, ct);
        await db.SaveChangesAsync(ct);
        return await ProjectSnapshotAsync(account.UserId, Math.Clamp(transactionLimit, 0, 200), ct);
    }

    public async Task<AiPackageCreditSnapshot> GrantPackageAsync(string userId, BillingAddOn addOn, int quantity, string stripeSessionId, string? quoteId, CancellationToken ct)
    {
        if (!string.Equals(addOn.AddonKind, "ai_package", StringComparison.OrdinalIgnoreCase))
        {
            return await GetSnapshotAsync(userId, 20, ct);
        }

        stripeSessionId = AddonGrantProcessor.FitDatabaseKey(stripeSessionId);
        quoteId = quoteId is null ? null : AddonGrantProcessor.FitDatabaseKey(quoteId);
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var account = await GetOrCreateAccountAsync(userId, ct);
        await ExpireIfNeededAsync(account, DateTimeOffset.UtcNow, ct);

        var existing = await db.AiPackageCreditTransactions.AsNoTracking()
            .AnyAsync(row => row.StripeSessionId == stripeSessionId, ct);
        if (existing)
        {
            return await ProjectSnapshotAsync(userId, 20, ct);
        }

        var grant = AiPackageGrant.FromAddOn(addOn, Math.Max(1, quantity));
        var now = DateTimeOffset.UtcNow;
        var newExpiry = now.AddDays(Math.Max(1, addOn.DurationDays));
        var referenceId = AddonGrantProcessor.FitDatabaseKey(
            quoteId is null ? $"stripe:{stripeSessionId}" : $"quote:{quoteId}:{addOn.Code}");

        AddLot(account, new AiPackageCreditLot
        {
            Id = NewId("aipkg-lot"),
            PackageId = addOn.Code,
            PackageType = grant.PackageType,
            SharedCredits = grant.SharedCredits,
            FlexibleCredits = grant.FlexibleCredits,
            WritingOnlyCredits = grant.WritingOnlyCredits,
            SpeakingOnlyCredits = grant.SpeakingOnlyCredits,
            ListeningTestsRemaining = grant.ListeningTests,
            ReadingTestsRemaining = grant.ReadingTests,
            MockExamsRemaining = grant.MockExams,
            UnlimitedGrading = grant.UnlimitedGrading,
            UnlimitedListening = grant.ListeningTests is null,
            UnlimitedReading = grant.ReadingTests is null,
            ExpiresAt = newExpiry,
            SourceReferenceId = referenceId,
            CreatedAt = now,
        });

        account.ExpiredBecausePassed = false;
        account.PassedAt = null;
        RebuildAccountFromLots(account);

        AddTransaction(account, new AiPackageCreditTransaction
        {
            Id = NewId("aipkg-tx"),
            StripeSessionId = stripeSessionId,
            PackageId = addOn.Code,
            PackageType = grant.PackageType,
            SharedCreditsDelta = grant.SharedCredits,
            FlexibleCreditsDelta = grant.FlexibleCredits,
            WritingOnlyCreditsDelta = grant.WritingOnlyCredits,
            SpeakingOnlyCreditsDelta = grant.SpeakingOnlyCredits,
            ListeningTestsDelta = grant.ListeningTests ?? 0,
            ReadingTestsDelta = grant.ReadingTests ?? 0,
            MockExamsDelta = grant.MockExams,
            Reason = AiPackageCreditReason.Purchase,
            ReferenceId = referenceId,
            Description = $"{addOn.Name} purchased",
            ExpiresAt = newExpiry,
            CreatedAt = now
        });

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await ProjectSnapshotAsync(userId, 20, ct);
    }

    public async Task<bool> GrantCourseGiftCreditsAsync(
        string userId,
        string planCode,
        string planName,
        int credits,
        string referenceId,
        DateTimeOffset? expiresAt,
        CancellationToken ct)
    {
        if (credits <= 0 || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(referenceId))
        {
            return false;
        }

        referenceId = AddonGrantProcessor.FitDatabaseKey(referenceId);
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var account = await GetOrCreateAccountAsync(userId, ct);
        var now = DateTimeOffset.UtcNow;
        await ExpireIfNeededAsync(account, now, ct);
        if (await TransactionExistsAsync(referenceId, AiPackageCreditReason.Purchase, ct))
        {
            return false;
        }

        account.ExpiredBecausePassed = false;
        account.PassedAt = null;

        AddLot(account, new AiPackageCreditLot
        {
            Id = NewId("aipkg-lot"),
            PackageId = planCode,
            PackageType = "full",
            SharedCredits = credits,
            ListeningTestsRemaining = 0,
            ReadingTestsRemaining = 0,
            ExpiresAt = expiresAt is { } expiry && expiry > now ? expiry : expiresAt,
            SourceReferenceId = referenceId,
            CreatedAt = now,
        });
        RebuildAccountFromLots(account);
        if (expiresAt is { } giftExpiry && giftExpiry > now)
        {
            account.ExpiresAt = Later(account.ExpiresAt, giftExpiry);
        }

        AddTransaction(account, new AiPackageCreditTransaction
        {
            Id = NewId("aipkg-tx"),
            PackageId = planCode,
            PackageType = "full",
            SharedCreditsDelta = credits,
            Reason = AiPackageCreditReason.Purchase,
            ReferenceId = referenceId,
            Description = $"{planName} gifted Shared AI practice credits",
            ExpiresAt = expiresAt,
            CreatedAt = now
        });

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return true;
    }

    public async Task<int> ReverseGrantsAsync(string userId, string packageId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(packageId))
        {
            return 0;
        }

        var reversed = 0;
        while (await ReverseOneGrantAsync(userId, packageId, ct))
        {
            reversed++;
        }

        return reversed;
    }

    public async Task RecalculateObjectiveAllowancesAsync(string userId, CancellationToken ct)
    {
        var account = await db.AiPackageCreditAccounts.FirstOrDefaultAsync(row => row.UserId == userId, ct);
        if (account is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var entitlementJson = await (
            from item in db.SubscriptionItems.AsNoTracking()
            join subscription in db.Subscriptions.AsNoTracking()
                on item.SubscriptionId equals subscription.Id
            join addOn in db.BillingAddOns.AsNoTracking()
                on item.ItemCode equals addOn.Code
            where subscription.UserId == userId
                  && (subscription.Status == SubscriptionStatus.Active
                      || subscription.Status == SubscriptionStatus.Trial
                      || subscription.Status == SubscriptionStatus.FreezeRequested)
                  && item.Status == SubscriptionItemStatus.Active
                  && item.StartsAt <= now
                  && (item.EndsAt == null || item.EndsAt > now)
                  && addOn.AddonKind == "ai_package"
            select addOn.GrantEntitlementsJson
        ).ToListAsync(ct);

        var listeningUnlimited = false;
        var readingUnlimited = false;
        var listeningSum = 0;
        var readingSum = 0;
        foreach (var json in entitlementJson)
        {
            var grant = AiPackageGrant.FromAddOn(new BillingAddOn
            {
                Code = "pkg_recalc",
                AddonKind = "ai_package",
                GrantEntitlementsJson = json,
            }, 1);
            if (grant.ListeningTests is null) listeningUnlimited = true;
            else listeningSum += grant.ListeningTests.Value;
            if (grant.ReadingTests is null) readingUnlimited = true;
            else readingSum += grant.ReadingTests.Value;
        }

        account.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await ReverseOrphanedGrantsAsync(userId, now, ct);
        var refreshed = await db.AiPackageCreditAccounts.FirstOrDefaultAsync(row => row.UserId == userId, ct);
        if (refreshed is not null)
        {
            await EnsureLotsLoadedAsync(refreshed, ct);
            RebuildAccountFromLots(refreshed);
            if (listeningUnlimited)
            {
                refreshed.ListeningTestsRemaining = null;
            }
            else if (refreshed.ListeningTestsRemaining is int listeningRemaining)
            {
                refreshed.ListeningTestsRemaining = Math.Min(listeningRemaining, listeningSum);
            }
            if (readingUnlimited)
            {
                refreshed.ReadingTestsRemaining = null;
            }
            else if (refreshed.ReadingTestsRemaining is int readingRemaining)
            {
                refreshed.ReadingTestsRemaining = Math.Min(readingRemaining, readingSum);
            }
            refreshed.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Admin date override sync: move the valid-until of course-gifted AI credit
    /// lots granted from this exact subscription to the edited package expiry.
    /// Used history is untouched; unrelated lots/packages are never modified.
    /// </summary>
    public async Task UpdateGrantExpiryAsync(string userId, string subscriptionId, DateTimeOffset? expiresAt, CancellationToken ct)
    {
        var prefix = AddonGrantProcessor.FitDatabaseKey($"admin-package:{subscriptionId}:");
        var account = await db.AiPackageCreditAccounts.FirstOrDefaultAsync(row => row.UserId == userId, ct);
        if (account is null)
        {
            return;
        }

        await EnsureLotsLoadedAsync(account, ct);
        var linkedLots = db.AiPackageCreditLots.Local
            .Where(lot => lot.AccountId == account.Id
                && lot.SourceReferenceId is { } source
                && source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (linkedLots.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var lot in linkedLots)
        {
            lot.ExpiresAt = expiresAt;
            // A past end expires the linked (unused) credits immediately, exactly as
            // the package itself expired — spending any remaining balance is blocked.
            if (expiresAt is { } exp && exp <= now && !lot.Expired)
            {
                lot.Expired = true;
                lot.ExpiredAt = now;
            }
        }

        RebuildAccountFromLots(account);
        account.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    public Task<AiPackageDebitResult> DeductGradingCreditAsync(string userId, string subtest, string referenceId, CancellationToken ct)
        => DeductGradingCreditAsync(userId, subtest, referenceId, 1, ct);

    public async Task<AiPackageDebitResult> DeductGradingCreditAsync(string userId, string subtest, string referenceId, int quantity, CancellationToken ct)
    {
        var normalized = NormalizeSubtest(subtest);
        if (normalized is not ("writing" or "speaking"))
        {
            return new(false, "unsupported_subtest", "Only Writing and Speaking consume AI grading credits.", null);
        }

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var account = await GetOrCreateAccountAsync(userId, ct);
        var now = DateTimeOffset.UtcNow;
        await ExpireIfNeededAsync(account, now, ct);
        quantity = ResolveGradingActivities(account, normalized, quantity);
        if (await TransactionExistsAsync(referenceId, AiPackageCreditReason.GradingDeduct, ct))
        {
            return new(true, "already_debited", "This grading job has already consumed a credit.", referenceId);
        }

        if (account.ExpiredBecausePassed || (account.ExpiresAt is not null && account.ExpiresAt <= now))
        {
            return new(false, "ai_package_expired", "Your AI package has expired. Purchase a package to continue.", null);
        }

        if (await HasActiveUnlimitedGradingAsync(userId, now, ct))
        {
            return new(true, null, null, referenceId, BalanceSource: "unlimited");
        }

        if (await ShouldBypassGradingDebitForLegacyAccountAsync(account, ct))
        {
            return new(true, null, null, referenceId);
        }

        if (!CanFundWritingOrSpeaking(account, normalized, quantity))
        {
            return new(false, "no_ai_package_credits", NoCreditsMessage, null);
        }

        var spend = SpendWritingOrSpeaking(account, normalized, quantity);
        account.UpdatedAt = DateTimeOffset.UtcNow;
        AddTransaction(account, new AiPackageCreditTransaction
        {
            Id = NewId("aipkg-tx"),
            PackageType = normalized,
            SharedCreditsDelta = spend.SharedDelta,
            FlexibleCreditsDelta = spend.FlexibleDelta,
            WritingOnlyCreditsDelta = spend.WritingDelta,
            SpeakingOnlyCreditsDelta = spend.SpeakingDelta,
            AllocationJson = spend.AllocationJson,
            Reason = AiPackageCreditReason.GradingDeduct,
            ReferenceId = referenceId,
            JobId = referenceId,
            Description = quantity == 1
                ? $"{normalized} AI grading credit deducted"
                : $"{normalized} AI grading credits deducted ({quantity})",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new(
            true,
            null,
            null,
            referenceId,
            Bypassed: false,
            BalanceSource: spend.BalanceSource,
            CreditsUsed: spend.CreditsUsed,
            RemainingAfter: RemainingAfterSpend(account, normalized),
            FeedbackMessage: spend.FeedbackMessage);
    }

    public Task<AiPackageDebitResult> CheckGradingCreditAsync(string userId, string subtest, CancellationToken ct)
        => CheckGradingCreditAsync(userId, subtest, 1, ct);

    public async Task<AiPackageDebitResult> CheckGradingCreditAsync(string userId, string subtest, int quantity, CancellationToken ct)
    {
        var normalized = NormalizeSubtest(subtest);
        if (normalized is not ("writing" or "speaking"))
        {
            return new(false, "unsupported_subtest", "Only Writing and Speaking consume AI grading credits.", null);
        }

        var account = await GetOrCreateAccountAsync(userId, ct);
        var now = DateTimeOffset.UtcNow;
        await ExpireIfNeededAsync(account, now, ct);
        await db.SaveChangesAsync(ct);

        if (account.ExpiredBecausePassed || (account.ExpiresAt is not null && account.ExpiresAt <= now))
        {
            return new(false, "ai_package_expired", "Your AI package has expired. Purchase a package to continue.", null);
        }

        if (await HasActiveUnlimitedGradingAsync(userId, now, ct))
        {
            return new(true, null, null, null, BalanceSource: "unlimited");
        }

        if (await ShouldBypassGradingDebitForLegacyAccountAsync(account, ct))
        {
            return new(true, null, null, null);
        }

        quantity = ResolveGradingActivities(account, normalized, quantity);
        return CanFundWritingOrSpeaking(account, normalized, quantity)
            ? new(true, null, null, null)
            : new(false, "no_ai_package_credits", NoCreditsMessage, null);
    }

    public async Task<AiPackageDebitResult> DeductObjectivePracticeAsync(string userId, string subtest, string referenceId, CancellationToken ct)
    {
        var normalized = NormalizeSubtest(subtest);
        if (normalized is not ("listening" or "reading"))
        {
            return new(false, "unsupported_subtest", "Only Listening and Reading use deterministic practice allowances.", null);
        }

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var account = await GetOrCreateAccountAsync(userId, ct);
        await ExpireIfNeededAsync(account, DateTimeOffset.UtcNow, ct);
        if (await ShouldBypassObjectiveDebitForLegacyAccountAsync(account, ct))
        {
            return new(true, null, null, referenceId, Bypassed: true);
        }

        if (await TransactionExistsAsync(referenceId, AiPackageCreditReason.ObjectivePracticeDeduct, ct))
        {
            return new(true, null, null, referenceId);
        }

        if (account.ExpiredBecausePassed || (account.ExpiresAt is not null && account.ExpiresAt <= DateTimeOffset.UtcNow))
        {
            return new(false, "ai_package_expired", "Your AI package has expired. Purchase a package to continue.", null);
        }

        var listeningDelta = 0;
        var readingDelta = 0;
        var sharedDelta = 0;
        string? feedback = null;
        string? balanceSource = null;
        var allocations = new List<LotAllocation>();
        if (normalized == "listening")
        {
            if (HasUnlimitedObjective(account, "listening"))
            {
                return new(true, null, null, referenceId, BalanceSource: "listening", FeedbackMessage: "Unlimited Listening practice — no credits consumed.");
            }

            if ((account.ListeningTestsRemaining ?? 0) > 0)
            {
                allocations.AddRange(SpendDedicatedObjective(account, "listening", 1));
                listeningDelta = -AiGradingCreditCost.ListeningExam;
                balanceSource = "listening";
                feedback = FormatUsedRemaining("Listening Credit", 1, account.ListeningTestsRemaining ?? 0);
            }
            else if (account.SharedCredits >= AiGradingCreditCost.ListeningExam)
            {
                allocations.AddRange(SpendSharedFromLots(account, AiGradingCreditCost.ListeningExam));
                sharedDelta = -AiGradingCreditCost.ListeningExam;
                balanceSource = "shared";
                feedback = FormatSharedUsed(normalized, 1, account.SharedCredits);
            }
            else
            {
                return new(false, "no_listening_tests", NoCreditsMessage, null);
            }
        }
        else
        {
            if (HasUnlimitedObjective(account, "reading"))
            {
                return new(true, null, null, referenceId, BalanceSource: "reading", FeedbackMessage: "Unlimited Reading practice — no credits consumed.");
            }

            if ((account.ReadingTestsRemaining ?? 0) > 0)
            {
                allocations.AddRange(SpendDedicatedObjective(account, "reading", 1));
                readingDelta = -AiGradingCreditCost.ReadingExam;
                balanceSource = "reading";
                feedback = FormatUsedRemaining("Reading Credit", 1, account.ReadingTestsRemaining ?? 0);
            }
            else if (account.SharedCredits >= AiGradingCreditCost.ReadingExam)
            {
                allocations.AddRange(SpendSharedFromLots(account, AiGradingCreditCost.ReadingExam));
                sharedDelta = -AiGradingCreditCost.ReadingExam;
                balanceSource = "shared";
                feedback = FormatSharedUsed(normalized, 1, account.SharedCredits);
            }
            else
            {
                return new(false, "no_reading_tests", NoCreditsMessage, null);
            }
        }

        account.UpdatedAt = DateTimeOffset.UtcNow;
        AddTransaction(account, new AiPackageCreditTransaction
        {
            Id = NewId("aipkg-tx"),
            PackageType = normalized,
            SharedCreditsDelta = sharedDelta,
            ListeningTestsDelta = listeningDelta,
            ReadingTestsDelta = readingDelta,
            AllocationJson = SerializeAllocations(allocations),
            Reason = AiPackageCreditReason.ObjectivePracticeDeduct,
            ReferenceId = referenceId,
            Description = sharedDelta < 0
                ? $"{normalized} exam used {Math.Abs(sharedDelta)} Shared AI credit"
                : $"{normalized} deterministic practice allowance used",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new(
            true,
            null,
            null,
            referenceId,
            Bypassed: false,
            BalanceSource: balanceSource,
            CreditsUsed: Math.Abs(listeningDelta + readingDelta + sharedDelta),
            RemainingAfter: (account.ListeningTestsRemaining ?? 0) + (account.ReadingTestsRemaining ?? 0) + account.SharedCredits,
            FeedbackMessage: feedback);
    }

    public async Task<bool> HasObjectivePracticeAllowanceAsync(string userId, string subtest, CancellationToken ct)
    {
        var normalized = NormalizeSubtest(subtest);
        if (normalized is not ("listening" or "reading")) return false;

        var account = await db.AiPackageCreditAccounts.FirstOrDefaultAsync(row => row.UserId == userId, ct);
        if (account is null) return false;

        await EnsureLotsLoadedAsync(account, ct);
        EnsureSyntheticLotIfNeeded(account);
        await ExpireIfNeededAsync(account, DateTimeOffset.UtcNow, ct);

        // Expired accounts cannot fund objective practice, same gate as DeductObjectivePracticeAsync.
        if (account.ExpiredBecausePassed || (account.ExpiresAt is not null && account.ExpiresAt <= DateTimeOffset.UtcNow))
            return false;

        // Legacy bypass is NOT treated as an allowance — it is a debit-only shortcut
        // for pre-package accounts. Content visibility still requires a real purchase.
        // Also treat the account null sentinel (ReadingTestsRemaining == null) as unlimited.
        if (normalized == "listening")
            return HasUnlimitedObjective(account, "listening")
                || account.ListeningTestsRemaining is null
                || (account.ListeningTestsRemaining ?? 0) > 0
                || account.SharedCredits >= AiGradingCreditCost.ListeningExam;

        return HasUnlimitedObjective(account, "reading")
            || account.ReadingTestsRemaining is null
            || (account.ReadingTestsRemaining ?? 0) > 0
            || account.SharedCredits >= AiGradingCreditCost.ReadingExam;
    }

    public async Task<AiPackageDebitResult> DeductMockAsync(string userId, string referenceId, CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var account = await GetOrCreateAccountAsync(userId, ct);
        await ExpireIfNeededAsync(account, DateTimeOffset.UtcNow, ct);
        if (await TransactionExistsAsync(referenceId, AiPackageCreditReason.MockDeduct, ct))
        {
            return new(true, "already_debited", "This mock has already consumed allowance.", referenceId);
        }

        if (await ShouldBypassMockDebitForLegacyAccountAsync(account, ct))
        {
            return new(true, null, null, referenceId, Bypassed: true);
        }

        if (account.ExpiredBecausePassed || (account.ExpiresAt is not null && account.ExpiresAt <= DateTimeOffset.UtcNow))
        {
            return new(false, "ai_package_expired", "Your AI package has expired. Purchase a package to continue.", null);
        }
        if (account.MockExamsRemaining <= 0)
        {
            return new(false, "no_mock_exams", NoCreditsMessage, null);
        }

        var mockAllocations = SpendMockFromLots(account, 1);
        account.UpdatedAt = DateTimeOffset.UtcNow;
        AddTransaction(account, new AiPackageCreditTransaction
        {
            Id = NewId("aipkg-tx"),
            PackageType = "mock",
            MockExamsDelta = -1,
            AllocationJson = SerializeAllocations(mockAllocations),
            Reason = AiPackageCreditReason.MockDeduct,
            ReferenceId = referenceId,
            Description = "Mock exam allowance used",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new(
            true,
            null,
            null,
            referenceId,
            Bypassed: false,
            BalanceSource: "mock",
            CreditsUsed: 1,
            RemainingAfter: account.MockExamsRemaining,
            FeedbackMessage: $"1 Full Mock attempt used. {account.MockExamsRemaining} Mock Attempts remaining.");
    }

    public async Task<bool> RefundAsync(string userId, string originalReferenceId, string refundReferenceId, string description, CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var account = await GetOrCreateAccountAsync(userId, ct);
        if (await TransactionExistsAsync(refundReferenceId, AiPackageCreditReason.RefundOnFailure, ct)
            || await TransactionExistsAsync(refundReferenceId, AiPackageCreditReason.MockRefundOnFailure, ct))
        {
            return false;
        }

        var debit = await db.AiPackageCreditTransactions.AsNoTracking()
            .Where(row => row.UserId == userId
                          && row.ReferenceId == originalReferenceId
                          && (row.Reason == AiPackageCreditReason.GradingDeduct
                              || row.Reason == AiPackageCreditReason.MockDeduct
                              || row.Reason == AiPackageCreditReason.ObjectivePracticeDeduct))
            .OrderByDescending(row => row.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (debit is null)
        {
            return false;
        }

        var reason = debit.Reason == AiPackageCreditReason.MockDeduct
            ? AiPackageCreditReason.MockRefundOnFailure
            : AiPackageCreditReason.RefundOnFailure;
        RestoreAllocation(account, debit);
        account.UpdatedAt = DateTimeOffset.UtcNow;

        AddTransaction(account, new AiPackageCreditTransaction
        {
            Id = NewId("aipkg-tx"),
            PackageType = debit.PackageType,
            SharedCreditsDelta = Math.Abs(debit.SharedCreditsDelta),
            FlexibleCreditsDelta = Math.Abs(debit.FlexibleCreditsDelta),
            WritingOnlyCreditsDelta = Math.Abs(debit.WritingOnlyCreditsDelta),
            SpeakingOnlyCreditsDelta = Math.Abs(debit.SpeakingOnlyCreditsDelta),
            ListeningTestsDelta = Math.Abs(debit.ListeningTestsDelta),
            ReadingTestsDelta = Math.Abs(debit.ReadingTestsDelta),
            MockExamsDelta = Math.Abs(debit.MockExamsDelta),
            AllocationJson = debit.AllocationJson,
            Reason = reason,
            ReferenceId = refundReferenceId,
            JobId = debit.JobId,
            Description = description,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return true;
    }

    public async Task<AiPackageCreditSnapshot> AdjustAsync(string userId, AiPackageCreditAdjustmentRequest request, string adminId, CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var account = await GetOrCreateAccountAsync(userId, ct);
        await ExpireIfNeededAsync(account, DateTimeOffset.UtcNow, ct);

        // Genuine used-per-bucket so admin adjustments never mutate "Used" and the
        // Total >= Used invariant can be validated. SetExact targets TOTAL:
        // delta = (setTotal - used) - currentRemaining, i.e. Remaining = setTotal - Used.
        var ledgerRows = await db.AiPackageCreditTransactions.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => new LedgerRow(
                row.PackageId,
                row.Description,
                row.Reason,
                row.SharedCreditsDelta,
                row.FlexibleCreditsDelta,
                row.WritingOnlyCreditsDelta,
                row.SpeakingOnlyCreditsDelta,
                row.ListeningTestsDelta,
                row.ReadingTestsDelta,
                row.MockExamsDelta,
                row.ExpiresAt,
                row.CreatedAt,
                row.ReferenceId))
            .ToListAsync(ct);
        var usage = ComputeUsage(ledgerRows);

        var sharedDelta = ResolveAdminDelta(account.SharedCredits, usage.Shared, request.SharedCreditsDelta, request.SharedCreditsSet, "Shared Credits");
        var flexibleDelta = ResolveAdminDelta(account.FlexibleCredits, usage.Flexible, request.FlexibleCreditsDelta, request.FlexibleCreditsSet, "Flexible W/S Credits");
        var writingDelta = ResolveAdminDelta(account.WritingOnlyCredits, usage.Writing, request.WritingOnlyCreditsDelta, request.WritingOnlyCreditsSet, "Writing Credits");
        var speakingDelta = ResolveAdminDelta(account.SpeakingOnlyCredits, usage.Speaking, request.SpeakingOnlyCreditsDelta, request.SpeakingOnlyCreditsSet, "Speaking Credits");
        var mockDelta = ResolveAdminDelta(account.MockExamsRemaining, usage.Mocks, request.MockExamsDelta, request.MockExamsSet, "Mock Attempts");
        var listeningDelta = ResolveAdminDelta(account.ListeningTestsRemaining ?? 0, usage.Listening, request.ListeningTestsDelta, request.ListeningTestsSet, "Listening Credits");
        var readingDelta = ResolveAdminDelta(account.ReadingTestsRemaining ?? 0, usage.Reading, request.ReadingTestsDelta, request.ReadingTestsSet, "Reading Credits");

        ApplyAdminAdjustmentToLots(
            account,
            sharedDelta,
            flexibleDelta,
            writingDelta,
            speakingDelta,
            listeningDelta,
            readingDelta,
            mockDelta,
            request.ExpiresAt);

        // "Set exact" also replaces the expiry when explicitly provided; otherwise the
        // pool expiry is retained. This is the only admin mutation that can rewrite
        // ExpiresAt, and it is the "Edit dates" source of truth for linked credits.
        account.ExpiresAt = request.ExpiresAt ?? account.ExpiresAt;
        account.UpdatedAt = DateTimeOffset.UtcNow;
        RebuildAccountFromLots(account);

        AddTransaction(account, new AiPackageCreditTransaction
        {
            Id = NewId("aipkg-tx"),
            SharedCreditsDelta = sharedDelta,
            FlexibleCreditsDelta = flexibleDelta,
            WritingOnlyCreditsDelta = writingDelta,
            SpeakingOnlyCreditsDelta = speakingDelta,
            ListeningTestsDelta = listeningDelta,
            ReadingTestsDelta = readingDelta,
            MockExamsDelta = mockDelta,
            Reason = AiPackageCreditReason.AdminAdjustment,
            ReferenceId = $"admin:{adminId}:{Guid.NewGuid():N}",
            Description = string.IsNullOrWhiteSpace(request.Reason) ? "Admin AI package credit adjustment" : request.Reason,
            ExpiresAt = request.ExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByAdminId = adminId
        });

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await ProjectSnapshotAsync(userId, 50, ct);
    }

    public async Task<AiPackageCreditSnapshot> RecordExamOutcomeAsync(string userId, LearnerExamOutcomeRequest request, string adminId, string adminName, CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var now = DateTimeOffset.UtcNow;
        db.LearnerExamOutcomes.Add(new LearnerExamOutcome
        {
            Id = NewId("exam-outcome"),
            UserId = userId,
            Passed = request.Passed,
            ExamDate = request.ExamDate,
            RecordedByAdminId = adminId,
            RecordedByAdminName = string.IsNullOrWhiteSpace(adminName) ? adminId : adminName,
            EvidenceNote = request.EvidenceNote,
            RecordedAt = now
        });

        var account = await GetOrCreateAccountAsync(userId, ct);
        if (request.Passed)
        {
            var shared = -account.SharedCredits;
            var flexible = -account.FlexibleCredits;
            var writing = -account.WritingOnlyCredits;
            var speaking = -account.SpeakingOnlyCredits;
            var listening = -(account.ListeningTestsRemaining ?? 0);
            var reading = -(account.ReadingTestsRemaining ?? 0);
            var mocks = -account.MockExamsRemaining;
            ZeroAllLots(account, now);
            account.ExpiredBecausePassed = true;
            account.PassedAt = request.ExamDate;
            account.ExpiresAt = now;
            account.UpdatedAt = now;

            AddTransaction(account, new AiPackageCreditTransaction
            {
                Id = NewId("aipkg-tx"),
                SharedCreditsDelta = shared,
                FlexibleCreditsDelta = flexible,
                WritingOnlyCreditsDelta = writing,
                SpeakingOnlyCreditsDelta = speaking,
                ListeningTestsDelta = listening,
                ReadingTestsDelta = reading,
                MockExamsDelta = mocks,
                Reason = AiPackageCreditReason.PassExpiry,
                ReferenceId = $"exam-pass:{request.ExamDate:yyyyMMdd}:{Guid.NewGuid():N}",
                Description = "AI package expired because candidate passed OET.",
                CreatedAt = now,
                CreatedByAdminId = adminId
            });
        }

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        logger.LogInformation("Admin {AdminId} recorded OET exam outcome for learner {UserId}; passed={Passed}.", adminId, userId, request.Passed);
        return await ProjectSnapshotAsync(userId, 50, ct);
    }

    private async Task<AiPackageCreditAccount> GetOrCreateAccountAsync(string userId, CancellationToken ct)
    {
        var account = await db.AiPackageCreditAccounts.FirstOrDefaultAsync(row => row.UserId == userId, ct);
        if (account is not null)
        {
            await EnsureLotsLoadedAsync(account, ct);
            EnsureSyntheticLotIfNeeded(account);
            return account;
        }

        var now = DateTimeOffset.UtcNow;
        account = new AiPackageCreditAccount
        {
            Id = NewId("aipkg-acct"),
            UserId = userId,
            ListeningTestsRemaining = 0,
            ReadingTestsRemaining = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.AiPackageCreditAccounts.Add(account);
        return account;
    }

    private async Task ExpireIfNeededAsync(AiPackageCreditAccount account, DateTimeOffset now, CancellationToken ct)
    {
        if (account.ExpiredBecausePassed)
        {
            return;
        }

        var lots = LiveLots(account);
        var expiredLots = lots
            .Where(lot => !lot.Expired && lot.ExpiresAt is { } expires && expires <= now)
            .ToList();
        if (expiredLots.Count == 0)
        {
            if (!db.AiPackageCreditLots.Local.Any(lot => lot.AccountId == account.Id))
            {
                await EnsureLotsLoadedAsync(account, ct);
                expiredLots = LiveLots(account)
                    .Where(lot => !lot.Expired && lot.ExpiresAt is { } expires && expires <= now)
                    .ToList();
            }
        }

        var accountExpired = account.ExpiresAt is { } accountExpiry && accountExpiry <= now;
        if (expiredLots.Count == 0 && accountExpired)
        {
            expiredLots = LiveLots(account).ToList();
        }

        if (expiredLots.Count == 0)
        {
            if (!accountExpired)
            {
                RebuildAccountFromLots(account);
            }

            return;
        }

        var preservedAccountExpiry = accountExpired ? account.ExpiresAt : null;

        foreach (var lot in expiredLots)
        {
            var referenceId = $"expiry:{lot.Id}:{lot.ExpiresAt:O}";
            if (await db.AiPackageCreditTransactions.AsNoTracking()
                .AnyAsync(row => row.AccountId == account.Id && row.Reason == AiPackageCreditReason.Expiry && row.ReferenceId == referenceId, ct))
            {
                lot.Expired = true;
                lot.ExpiredAt ??= now;
                continue;
            }

            lot.Expired = true;
            lot.ExpiredAt = now;
            AddTransaction(account, new AiPackageCreditTransaction
            {
                Id = NewId("aipkg-tx"),
                PackageId = lot.PackageId,
                PackageType = lot.PackageType,
                SharedCreditsDelta = -lot.SharedCredits,
                FlexibleCreditsDelta = -lot.FlexibleCredits,
                WritingOnlyCreditsDelta = -lot.WritingOnlyCredits,
                SpeakingOnlyCreditsDelta = -lot.SpeakingOnlyCredits,
                ListeningTestsDelta = -(lot.ListeningTestsRemaining ?? 0),
                ReadingTestsDelta = -(lot.ReadingTestsRemaining ?? 0),
                MockExamsDelta = -lot.MockExamsRemaining,
                Reason = AiPackageCreditReason.Expiry,
                ReferenceId = referenceId,
                Description = "AI package credits expired.",
                ExpiresAt = lot.ExpiresAt,
                CreatedAt = now
            });
            lot.SharedCredits = 0;
            lot.FlexibleCredits = 0;
            lot.WritingOnlyCredits = 0;
            lot.SpeakingOnlyCredits = 0;
            lot.ListeningTestsRemaining = lot.UnlimitedListening ? lot.ListeningTestsRemaining : 0;
            lot.ReadingTestsRemaining = lot.UnlimitedReading ? lot.ReadingTestsRemaining : 0;
            lot.MockExamsRemaining = 0;
        }

        RebuildAccountFromLots(account);
        if (preservedAccountExpiry is { } kept && (account.ExpiresAt is null || account.ExpiresAt < kept))
        {
            account.ExpiresAt = kept;
        }
    }

    private void AddTransaction(AiPackageCreditAccount account, AiPackageCreditTransaction row)
    {
        row.UserId = account.UserId;
        row.AccountId = account.Id;
        db.AiPackageCreditTransactions.Add(row);
    }

    private async Task<bool> ReverseOneGrantAsync(string userId, string packageId, CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var account = await GetOrCreateAccountAsync(userId, ct);
        await EnsureLotsLoadedAsync(account, ct);
        var purchases = await db.AiPackageCreditTransactions.AsNoTracking()
            .Where(row => row.UserId == userId
                          && row.PackageId == packageId
                          && row.Reason == AiPackageCreditReason.Purchase
                          && row.ReferenceId != null)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .ToListAsync(ct);
        var reversedReferences = (await db.AiPackageCreditTransactions.AsNoTracking()
            .Where(row => row.UserId == userId
                          && row.Reason == AiPackageCreditReason.GrantReversed
                          && row.ReferenceId != null)
            .Select(row => row.ReferenceId!)
            .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var purchase = purchases.FirstOrDefault(row =>
        {
            var reverseReference = AddonGrantProcessor.FitDatabaseKey($"grant-reverse:{row.ReferenceId}");
            return !reversedReferences.Contains(row.ReferenceId!)
                   && !reversedReferences.Contains(reverseReference);
        });
        if (purchase is null)
        {
            return false;
        }

        await ExpireIfNeededAsync(account, DateTimeOffset.UtcNow, ct);
        var lots = LiveLots(account)
            .Where(lot => string.Equals(lot.SourceReferenceId, purchase.ReferenceId, StringComparison.Ordinal)
                          || string.Equals(lot.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var matchingLots = lots
            .Where(lot => string.Equals(lot.SourceReferenceId, purchase.ReferenceId, StringComparison.Ordinal))
            .ToList();
        if (matchingLots.Count == 0)
        {
            matchingLots = lots
                .Where(lot => string.Equals(lot.PackageId, packageId, StringComparison.OrdinalIgnoreCase) && !lot.Expired)
                .Take(1)
                .ToList();
        }

        var shared = 0;
        var flexible = 0;
        var writing = 0;
        var speaking = 0;
        var mocks = 0;
        var listening = 0;
        var reading = 0;
        foreach (var lot in matchingLots)
        {
            shared += -Math.Min(lot.SharedCredits, Math.Max(0, purchase.SharedCreditsDelta > 0 ? purchase.SharedCreditsDelta : lot.SharedCredits));
            flexible += -lot.FlexibleCredits;
            writing += -lot.WritingOnlyCredits;
            speaking += -lot.SpeakingOnlyCredits;
            mocks += -lot.MockExamsRemaining;
            if (lot.ListeningTestsRemaining is int listeningRemaining)
            {
                listening += -listeningRemaining;
            }
            if (lot.ReadingTestsRemaining is int readingRemaining)
            {
                reading += -readingRemaining;
            }
            lot.SharedCredits = 0;
            lot.FlexibleCredits = 0;
            lot.WritingOnlyCredits = 0;
            lot.SpeakingOnlyCredits = 0;
            lot.MockExamsRemaining = 0;
            lot.ListeningTestsRemaining = lot.UnlimitedListening ? null : 0;
            lot.ReadingTestsRemaining = lot.UnlimitedReading ? null : 0;
            lot.UnlimitedGrading = false;
            lot.UnlimitedListening = false;
            lot.UnlimitedReading = false;
            lot.Expired = true;
            lot.ExpiredAt = DateTimeOffset.UtcNow;
        }

        if (matchingLots.Count == 0)
        {
            shared = -Math.Min(account.SharedCredits, Math.Max(0, purchase.SharedCreditsDelta));
            flexible = -Math.Min(account.FlexibleCredits, Math.Max(0, purchase.FlexibleCreditsDelta));
            writing = -Math.Min(account.WritingOnlyCredits, Math.Max(0, purchase.WritingOnlyCreditsDelta));
            speaking = -Math.Min(account.SpeakingOnlyCredits, Math.Max(0, purchase.SpeakingOnlyCreditsDelta));
            mocks = -Math.Min(account.MockExamsRemaining, Math.Max(0, purchase.MockExamsDelta));
            if (account.ListeningTestsRemaining is int listeningRemaining && purchase.ListeningTestsDelta > 0)
            {
                listening = -Math.Min(listeningRemaining, purchase.ListeningTestsDelta);
            }
            if (account.ReadingTestsRemaining is int readingRemaining && purchase.ReadingTestsDelta > 0)
            {
                reading = -Math.Min(readingRemaining, purchase.ReadingTestsDelta);
            }
        }

        RebuildAccountFromLots(account);
        account.UpdatedAt = DateTimeOffset.UtcNow;
        var reverseReference = AddonGrantProcessor.FitDatabaseKey($"grant-reverse:{purchase.ReferenceId}");
        AddTransaction(account, new AiPackageCreditTransaction
        {
            Id = NewId("aipkg-tx"),
            PackageId = purchase.PackageId,
            PackageType = purchase.PackageType,
            SharedCreditsDelta = shared,
            FlexibleCreditsDelta = flexible,
            WritingOnlyCreditsDelta = writing,
            SpeakingOnlyCreditsDelta = speaking,
            ListeningTestsDelta = listening,
            ReadingTestsDelta = reading,
            MockExamsDelta = mocks,
            Reason = AiPackageCreditReason.GrantReversed,
            ReferenceId = reverseReference,
            Description = $"{purchase.PackageId} grant reversed",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return true;
    }

    private async Task ReverseOrphanedGrantsAsync(string userId, DateTimeOffset now, CancellationToken ct)
    {
        var livePlanIds = (await db.Subscriptions.AsNoTracking()
            .Where(subscription => subscription.UserId == userId
                                   && subscription.PlanId != Subscription.StandaloneAddonPlanId
                                   && (subscription.Status == SubscriptionStatus.Active
                                       || subscription.Status == SubscriptionStatus.Trial
                                       || subscription.Status == SubscriptionStatus.FreezeRequested))
            .Select(subscription => subscription.PlanId)
            .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var liveItemCodes = (await (
            from item in db.SubscriptionItems.AsNoTracking()
            join subscription in db.Subscriptions.AsNoTracking()
                on item.SubscriptionId equals subscription.Id
            where subscription.UserId == userId
                  && (subscription.Status == SubscriptionStatus.Active
                      || subscription.Status == SubscriptionStatus.Trial
                      || subscription.Status == SubscriptionStatus.FreezeRequested)
                  && item.Status == SubscriptionItemStatus.Active
                  && item.StartsAt <= now
                  && (item.EndsAt == null || item.EndsAt > now)
            select item.ItemCode).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var packageIds = (await db.AiPackageCreditTransactions.AsNoTracking()
            .Where(row => row.UserId == userId
                          && row.Reason == AiPackageCreditReason.Purchase
                          && row.PackageId != null)
            .Select(row => row.PackageId!)
            .Distinct()
            .ToListAsync(ct));

        foreach (var packageId in packageIds)
        {
            if (livePlanIds.Contains(packageId) || liveItemCodes.Contains(packageId))
            {
                continue;
            }

            await ReverseGrantsAsync(userId, packageId, ct);
        }
    }

    private async Task<bool> TransactionExistsAsync(string referenceId, AiPackageCreditReason reason, CancellationToken ct)
        => await db.AiPackageCreditTransactions.AsNoTracking()
            .AnyAsync(row => row.ReferenceId == referenceId && row.Reason == reason, ct);

    private async Task<bool> HasActiveUnlimitedGradingAsync(string userId, DateTimeOffset now, CancellationToken ct)
    {
        var endsAtValues = await (
            from item in db.SubscriptionItems.AsNoTracking()
            join subscription in db.Subscriptions.AsNoTracking()
                on item.SubscriptionId equals subscription.Id
            where subscription.UserId == userId
                  && (subscription.Status == SubscriptionStatus.Active
                      || subscription.Status == SubscriptionStatus.Trial
                      || subscription.Status == SubscriptionStatus.FreezeRequested)
                  && item.ItemCode == "pkg_oet_mastery"
                  && item.Status == SubscriptionItemStatus.Active
                  && item.StartsAt <= now
            select item.EndsAt).ToListAsync(ct);

        // Evaluate the date window in memory. EF InMemory does not reliably
        // translate `EndsAt == null || EndsAt > now`, and standalone Mastery
        // grants can persist a null EndsAt when DurationDays was 0.
        return endsAtValues.Any(endsAt => endsAt is null || endsAt > now);
    }

    private async Task<bool> ShouldBypassGradingDebitForLegacyAccountAsync(AiPackageCreditAccount account, CancellationToken ct)
    {
        if (account.SharedCredits != 0
            || account.FlexibleCredits != 0
            || account.WritingOnlyCredits != 0
            || account.SpeakingOnlyCredits != 0)
        {
            return false;
        }

        return !await db.AiPackageCreditTransactions.AsNoTracking()
            .AnyAsync(row => row.AccountId == account.Id
                             && (row.Reason == AiPackageCreditReason.Purchase
                                 || row.SharedCreditsDelta > 0
                                 || row.FlexibleCreditsDelta > 0
                                 || row.WritingOnlyCreditsDelta > 0
                                 || row.SpeakingOnlyCreditsDelta > 0), ct);
    }

    private async Task<bool> ShouldBypassObjectiveDebitForLegacyAccountAsync(AiPackageCreditAccount account, CancellationToken ct)
    {
        if (HasUnlimitedObjective(account, "listening")
            || HasUnlimitedObjective(account, "reading")
            || (account.ListeningTestsRemaining ?? 0) > 0
            || (account.ReadingTestsRemaining ?? 0) > 0
            || account.SharedCredits > 0)
        {
            return false;
        }

        return !await db.AiPackageCreditTransactions.AsNoTracking()
            .AnyAsync(row => row.AccountId == account.Id
                             && (row.Reason == AiPackageCreditReason.Purchase
                                 || row.SharedCreditsDelta > 0
                                 || row.ListeningTestsDelta > 0
                                 || row.ReadingTestsDelta > 0), ct);
    }

    private async Task<bool> ShouldBypassMockDebitForLegacyAccountAsync(AiPackageCreditAccount account, CancellationToken ct)
    {
        if (account.MockExamsRemaining > 0)
        {
            return false;
        }

        return !await db.AiPackageCreditTransactions.AsNoTracking()
            .AnyAsync(row => row.AccountId == account.Id && row.MockExamsDelta > 0, ct);
    }

    private async Task<AiPackageCreditSnapshot> ProjectSnapshotAsync(string userId, int transactionLimit, CancellationToken ct)
    {
        var account = await db.AiPackageCreditAccounts.AsNoTracking().FirstAsync(row => row.UserId == userId, ct);
        var transactions = transactionLimit <= 0
            ? []
            : await db.AiPackageCreditTransactions.AsNoTracking()
                .Where(row => row.UserId == userId)
                .OrderByDescending(row => row.CreatedAt)
                .ThenByDescending(row => row.Id)
                .Take(transactionLimit)
                .Select(row => new AiPackageCreditTransactionDto(
                    row.Id,
                    row.PackageId,
                    row.PackageType,
                    row.Reason.ToString(),
                    row.SharedCreditsDelta,
                    row.FlexibleCreditsDelta,
                    row.WritingOnlyCreditsDelta,
                    row.SpeakingOnlyCreditsDelta,
                    row.ListeningTestsDelta,
                    row.ReadingTestsDelta,
                    row.MockExamsDelta,
                    row.ReferenceId,
                    row.Description,
                    row.ExpiresAt,
                    row.CreatedAt))
                .ToListAsync(ct);

        var ledgerRows = await db.AiPackageCreditTransactions.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => new LedgerRow(
                row.PackageId,
                row.Description,
                row.Reason,
                row.SharedCreditsDelta,
                row.FlexibleCreditsDelta,
                row.WritingOnlyCreditsDelta,
                row.SpeakingOnlyCreditsDelta,
                row.ListeningTestsDelta,
                row.ReadingTestsDelta,
                row.MockExamsDelta,
                row.ExpiresAt,
                row.CreatedAt,
                row.ReferenceId))
            .ToListAsync(ct);

        var grantRows = ledgerRows.Where(row => IsGrantReason(row.Reason)).ToList();
        var debitRows = ledgerRows.Where(row => IsDebitReason(row.Reason)).ToList();
        var usage = ComputeUsage(ledgerRows);
        var creditsGranted = Math.Max(0, grantRows.Sum(row =>
            row.SharedCreditsDelta + row.FlexibleCreditsDelta + row.WritingOnlyCreditsDelta + row.SpeakingOnlyCreditsDelta));
        var creditsUsed = usage.Shared + usage.Flexible + usage.Writing + usage.Speaking;
        var sharedGranted = Math.Max(0, grantRows.Sum(row => row.SharedCreditsDelta));
        var sharedUsed = usage.Shared;
        var creditsRemaining = account.SharedCredits + account.FlexibleCredits + account.WritingOnlyCredits + account.SpeakingOnlyCredits;
        var unlimitedGrading = await HasActiveUnlimitedGradingAsync(userId, DateTimeOffset.UtcNow, ct);
        var now = DateTimeOffset.UtcNow;
        var lots = await db.AiPackageCreditLots.AsNoTracking()
            .Where(lot => lot.UserId == userId)
            .ToListAsync(ct);
        var liveLots = lots.Where(lot => !lot.Expired && (lot.ExpiresAt is null || lot.ExpiresAt > now)).ToList();
        var listeningUnlimited = liveLots.Any(lot => lot.UnlimitedListening);
        var readingUnlimited = liveLots.Any(lot => lot.UnlimitedReading);
        var activities = debitRows
            .OrderByDescending(row => row.CreatedAt)
            .Take(20)
            .Select(row => new AiPackageOpenedActivityDto(
                row.ReferenceId ?? row.CreatedAt.ToString("O"),
                row.Description,
                row.Reason == AiPackageCreditReason.MockDeduct ? "mock" : InferActivitySubtest(row.Description),
                "opened",
                row.CreatedAt,
                row.PackageId,
                Math.Max(0, -row.SharedCreditsDelta) + Math.Max(0, -row.FlexibleCreditsDelta) + Math.Max(0, -row.WritingOnlyCreditsDelta) + Math.Max(0, -row.SpeakingOnlyCreditsDelta),
                creditsRemaining))
            .ToList();
        var buckets = BuildBucketDtos(account, grantRows, usage, unlimitedGrading, listeningUnlimited, readingUnlimited);

        return new AiPackageCreditSnapshot(
            account.UserId,
            account.FlexibleCredits,
            account.WritingOnlyCredits,
            account.SpeakingOnlyCredits,
            account.ListeningTestsRemaining,
            account.ReadingTestsRemaining,
            account.MockExamsRemaining,
            account.ExpiresAt,
            account.ExpiredBecausePassed,
            account.PassedAt,
            transactions,
            creditsGranted,
            creditsUsed,
            creditsRemaining,
            unlimitedGrading,
            unlimitedGrading,
            account.SharedCredits,
            sharedGranted,
            sharedUsed,
            buckets,
            listeningUnlimited,
            readingUnlimited,
            BuildBucket(liveLots, lots, "shared", unlimitedGrading, now),
            BuildBucket(liveLots, lots, "flexible", unlimitedGrading, now),
            BuildBucket(liveLots, lots, "writing", unlimitedGrading, now),
            BuildBucket(liveLots, lots, "speaking", unlimitedGrading, now),
            BuildBucket(liveLots, lots, "listening", unlimitedGrading, now),
            BuildBucket(liveLots, lots, "reading", unlimitedGrading, now),
            BuildBucket(liveLots, lots, "mocks", unlimitedGrading, now),
            activities);
    }

    private sealed record LedgerRow(
        string? PackageId,
        string Description,
        AiPackageCreditReason Reason,
        int SharedCreditsDelta,
        int FlexibleCreditsDelta,
        int WritingOnlyCreditsDelta,
        int SpeakingOnlyCreditsDelta,
        int ListeningTestsDelta,
        int ReadingTestsDelta,
        int MockExamsDelta,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset CreatedAt,
        string? ReferenceId);

    /// <summary>Genuine learner consumption (never admin adjustments / reversals /
    /// expiry). Per-bucket usage used to project Total = Used + Remaining.</summary>
    private sealed record CreditUsage(
        int Shared,
        int Flexible,
        int Writing,
        int Speaking,
        int Listening,
        int Reading,
        int Mocks);

    private static bool IsGrantReason(AiPackageCreditReason reason)
        => reason is AiPackageCreditReason.Purchase
            or AiPackageCreditReason.AdminAdjustment
            or AiPackageCreditReason.GrantReversed;

    private static bool IsDebitReason(AiPackageCreditReason reason)
        => reason is AiPackageCreditReason.GradingDeduct
            or AiPackageCreditReason.ObjectivePracticeDeduct
            or AiPackageCreditReason.MockDeduct;

    private static bool IsConsumption(AiPackageCreditReason reason)
        => IsDebitReason(reason)
            || reason is AiPackageCreditReason.RefundOnFailure
                or AiPackageCreditReason.MockRefundOnFailure;

    /// <summary>
    /// Genuine learner usage per bucket. Refund rows restore a debit, so they are
    /// included in the netting: a consumed-then-refunded credit nets to 0 used.
    /// Admin adjustments, grant reversals and expiry rows are allocation changes
    /// and never count toward Used.
    /// </summary>
    private static CreditUsage ComputeUsage(IReadOnlyList<LedgerRow> ledgerRows)
        => new(
            Math.Max(0, -ledgerRows.Where(row => IsConsumption(row.Reason)).Sum(row => Math.Min(0, row.SharedCreditsDelta))),
            Math.Max(0, -ledgerRows.Where(row => IsConsumption(row.Reason)).Sum(row => Math.Min(0, row.FlexibleCreditsDelta))),
            Math.Max(0, -ledgerRows.Where(row => IsConsumption(row.Reason)).Sum(row => Math.Min(0, row.WritingOnlyCreditsDelta))),
            Math.Max(0, -ledgerRows.Where(row => IsConsumption(row.Reason)).Sum(row => Math.Min(0, row.SpeakingOnlyCreditsDelta))),
            Math.Max(0, -ledgerRows.Where(row => IsConsumption(row.Reason)).Sum(row => Math.Min(0, row.ListeningTestsDelta))),
            Math.Max(0, -ledgerRows.Where(row => IsConsumption(row.Reason)).Sum(row => Math.Min(0, row.ReadingTestsDelta))),
            Math.Max(0, -ledgerRows.Where(row => IsConsumption(row.Reason)).Sum(row => Math.Min(0, row.MockExamsDelta))));

    private static IReadOnlyList<AiPackageCreditBucketDto> BuildBucketDtos(
        AiPackageCreditAccount account,
        List<LedgerRow> grantRows,
        CreditUsage usage,
        bool writingSpeakingUnlimited,
        bool listeningUnlimited,
        bool readingUnlimited)
    {
        var now = DateTimeOffset.UtcNow;

        List<AiPackageCreditGrantSourceDto> GrantsFor(Func<LedgerRow, int> deltaSelector)
            => grantRows
                .Where(row => deltaSelector(row) > 0)
                .GroupBy(row => row.PackageId ?? row.Description)
                .Select(group =>
                {
                    var first = group.First();
                    return new AiPackageCreditGrantSourceDto(
                        first.PackageId,
                        first.Description,
                        group.Sum(row => deltaSelector(row)),
                        group.Min(row => row.CreatedAt),
                        group.Max(row => row.ExpiresAt));
                })
                .OrderBy(source => source.GrantedAt)
                .ToList();

        AiPackageCreditBucketDto Bucket(string key, string label, int remaining, bool unlimited, int used, List<AiPackageCreditGrantSourceDto> grants)
        {
            // Admin AI credits fix: Total = Used + Remaining is the invariant that the
            // UI reports. Admin ± / Set adjustments change `remaining` (the pool), so
            // Total moves but Used stays pinned to genuine learner consumption. Building
            // Total from the grant-reversal-free used figure keeps "Used" a read-only
            // learner-usage number (see ProjectSnapshotAsync.ComputeUsage).
            var effectiveUsed = unlimited ? 0 : Math.Clamp(used, 0, int.MaxValue);
            var totalGranted = remaining + effectiveUsed;
            var activeGrants = grants.Where(source => source.ExpiresAt is null || source.ExpiresAt > now).ToList();
            DateTimeOffset? validFrom = activeGrants.Count > 0 ? activeGrants.Min(source => source.GrantedAt) : null;
            DateTimeOffset? expiresAt = activeGrants.Any(source => source.ExpiresAt is null)
                ? null
                : activeGrants.Select(source => source.ExpiresAt).Max();
            return new(
                key,
                label,
                unlimited,
                totalGranted,
                effectiveUsed,
                remaining,
                grants.Count == 0 ? null : string.Join(", ", grants
                    .Select(source => HumanizePackageName(source.PackageId, source.Description))
                    .Distinct(StringComparer.OrdinalIgnoreCase)),
                validFrom,
                expiresAt ?? account.ExpiresAt,
                DaysLeft(expiresAt ?? account.ExpiresAt, now),
                grants);
        }

        var buckets = new List<AiPackageCreditBucketDto>
        {
            Bucket("reading", "Reading Credits", account.ReadingTestsRemaining ?? 0, readingUnlimited || account.ReadingTestsRemaining is null, usage.Reading, GrantsFor(row => row.ReadingTestsDelta)),
            Bucket("listening", "Listening Credits", account.ListeningTestsRemaining ?? 0, listeningUnlimited || account.ListeningTestsRemaining is null, usage.Listening, GrantsFor(row => row.ListeningTestsDelta)),
            Bucket("writing", "Writing Credits",
                writingSpeakingUnlimited ? 0 : account.WritingOnlyCredits,
                writingSpeakingUnlimited,
                usage.Writing,
                GrantsFor(row => row.WritingOnlyCreditsDelta)),
            Bucket("speaking", "Speaking Credits",
                writingSpeakingUnlimited ? 0 : account.SpeakingOnlyCredits,
                writingSpeakingUnlimited,
                usage.Speaking,
                GrantsFor(row => row.SpeakingOnlyCreditsDelta)),
            Bucket("shared", "Shared Credits", account.SharedCredits, false, usage.Shared, GrantsFor(row => row.SharedCreditsDelta)),
        };

        var flexibleWsGrants = GrantsFor(row => row.FlexibleCreditsDelta);
        if (account.FlexibleCredits > 0 || flexibleWsGrants.Count > 0)
        {
            buckets.Add(Bucket("flexible_ws", "Flexible W/S Credits", account.FlexibleCredits, false, usage.Flexible, flexibleWsGrants));
        }

        var mockGrants = GrantsFor(row => row.MockExamsDelta);
        if (account.MockExamsRemaining > 0 || mockGrants.Count > 0)
        {
            buckets.Add(Bucket("mock", "Full Mock Attempts", account.MockExamsRemaining, false, usage.Mocks, mockGrants));
        }

        return buckets;
    }

    private static string HumanizePackageName(string? packageId, string description)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return description;
        }

        if (packageId.StartsWith("pkg_", StringComparison.OrdinalIgnoreCase))
        {
            packageId = packageId["pkg_".Length..];
        }

        return packageId.Replace('_', ' ').Replace('-', ' ');
    }

    private static int DaysLeft(DateTimeOffset? expiresAt, DateTimeOffset now)
        => expiresAt is null ? -1 : Math.Max(0, (int)Math.Ceiling((expiresAt.Value - now).TotalDays));

    private async Task<IDbContextTransaction?> BeginTransactionIfNeededAsync(CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null || db.Database.IsInMemory())
        {
            return null;
        }

        return await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    }

    private static int? MergeObjectiveAllowance(int? current, int? grant)
    {
        if (grant is null) return null;
        if (current is null) return null;
        return Math.Max(0, current.Value) + Math.Max(0, grant.Value);
    }

    private static int? AdjustNullableAllowance(int? current, int delta)
        => current is null ? null : Math.Max(0, current.Value + delta);

    private static DateTimeOffset? Later(DateTimeOffset? current, DateTimeOffset next)
        => current is null || next > current ? next : current;

    private static string NormalizeSubtest(string value)
        => value.Trim().ToLowerInvariant();

    private static string NewId(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(64, prefix.Length + 33)];

    private void AddLot(AiPackageCreditAccount account, AiPackageCreditLot lot)
    {
        lot.UserId = account.UserId;
        lot.AccountId = account.Id;
        db.AiPackageCreditLots.Add(lot);
    }

    private async Task EnsureLotsLoadedAsync(AiPackageCreditAccount account, CancellationToken ct)
    {
        if (db.AiPackageCreditLots.Local.Any(lot => lot.AccountId == account.Id))
        {
            return;
        }

        await db.AiPackageCreditLots
            .Where(lot => lot.AccountId == account.Id)
            .LoadAsync(ct);
    }

    private void EnsureSyntheticLotIfNeeded(AiPackageCreditAccount account)
    {
        if (LiveLots(account).Count > 0)
        {
            return;
        }

        if (account.SharedCredits <= 0
            && account.FlexibleCredits <= 0
            && account.WritingOnlyCredits <= 0
            && account.SpeakingOnlyCredits <= 0
            && (account.ListeningTestsRemaining ?? 0) <= 0
            && (account.ReadingTestsRemaining ?? 0) <= 0
            && account.MockExamsRemaining <= 0
            && account.ListeningTestsRemaining is not null
            && account.ReadingTestsRemaining is not null)
        {
            return;
        }

        AddLot(account, new AiPackageCreditLot
        {
            Id = NewId("aipkg-lot"),
            PackageId = "legacy",
            PackageType = "legacy",
            SharedCredits = Math.Max(0, account.SharedCredits),
            FlexibleCredits = Math.Max(0, account.FlexibleCredits),
            WritingOnlyCredits = Math.Max(0, account.WritingOnlyCredits),
            SpeakingOnlyCredits = Math.Max(0, account.SpeakingOnlyCredits),
            ListeningTestsRemaining = account.ListeningTestsRemaining,
            ReadingTestsRemaining = account.ReadingTestsRemaining,
            MockExamsRemaining = Math.Max(0, account.MockExamsRemaining),
            UnlimitedListening = account.ListeningTestsRemaining is null,
            UnlimitedReading = account.ReadingTestsRemaining is null,
            ExpiresAt = account.ExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private bool HasUnlimitedObjective(AiPackageCreditAccount account, string subtest)
        => subtest == "listening"
            ? LiveLots(account).Any(lot => lot.UnlimitedListening)
            : LiveLots(account).Any(lot => lot.UnlimitedReading);

    private List<AiPackageCreditLot> LiveLots(AiPackageCreditAccount account)
        => db.AiPackageCreditLots.Local
            .Where(lot => lot.AccountId == account.Id && !lot.Expired)
            .OrderBy(lot => lot.ExpiresAt ?? DateTimeOffset.MaxValue)
            .ThenBy(lot => lot.CreatedAt)
            .ThenBy(lot => lot.Id)
            .ToList();

    private void RebuildAccountFromLots(AiPackageCreditAccount account)
    {
        if (!db.AiPackageCreditLots.Local.Any(lot => lot.AccountId == account.Id))
        {
            return;
        }

        var lots = LiveLots(account);
        account.SharedCredits = Math.Max(0, lots.Sum(lot => lot.SharedCredits));
        account.FlexibleCredits = Math.Max(0, lots.Sum(lot => lot.FlexibleCredits));
        account.WritingOnlyCredits = Math.Max(0, lots.Sum(lot => lot.WritingOnlyCredits));
        account.SpeakingOnlyCredits = Math.Max(0, lots.Sum(lot => lot.SpeakingOnlyCredits));
        account.MockExamsRemaining = Math.Max(0, lots.Sum(lot => lot.MockExamsRemaining));
        account.ListeningTestsRemaining = lots.Any(lot => lot.UnlimitedListening)
            ? null
            : lots.Sum(lot => lot.ListeningTestsRemaining ?? 0);
        account.ReadingTestsRemaining = lots.Any(lot => lot.UnlimitedReading)
            ? null
            : lots.Sum(lot => lot.ReadingTestsRemaining ?? 0);
        account.ExpiresAt = lots
            .Select(lot => lot.ExpiresAt)
            .Where(expires => expires is not null)
            .DefaultIfEmpty()
            .Max();
        account.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static int ResolveGradingActivities(AiPackageCreditAccount account, string subtest, int quantity)
    {
        var requested = Math.Max(1, quantity);
        var dedicated = subtest == "writing" ? account.WritingOnlyCredits : account.SpeakingOnlyCredits;
        // Callers historically pass the Shared W/S rate (2) for one activity
        // when only Shared can fund. Dedicated/Flexible quantity stays as
        // activity count so two-credit dedicated checks still work.
        if (dedicated + account.FlexibleCredits == 0 && requested == AiGradingCreditCost.SharedWritingOrSpeaking)
        {
            return 1;
        }

        return requested;
    }

    private bool CanFundWritingOrSpeaking(AiPackageCreditAccount account, string subtest, int quantity)
    {
        var dedicated = subtest == "writing" ? account.WritingOnlyCredits : account.SpeakingOnlyCredits;
        var remaining = quantity - dedicated - account.FlexibleCredits;
        if (remaining <= 0) return true;
        return account.SharedCredits >= remaining * AiGradingCreditCost.SharedWritingOrSpeaking;
    }

    private SpendResult SpendWritingOrSpeaking(AiPackageCreditAccount account, string subtest, int quantity)
    {
        var remaining = quantity;
        var writing = 0;
        var speaking = 0;
        var flexible = 0;
        var sharedActivities = 0;
        var allocations = new Dictionary<string, LotAllocation>(StringComparer.Ordinal);

        LotAllocation Track(AiPackageCreditLot lot) =>
            allocations.TryGetValue(lot.Id, out var existing)
                ? existing
                : allocations[lot.Id] = new LotAllocation(lot.Id, 0, 0, 0, 0, 0, 0, 0);

        foreach (var lot in LiveLots(account))
        {
            if (remaining <= 0) break;
            var dedicated = subtest == "writing" ? lot.WritingOnlyCredits : lot.SpeakingOnlyCredits;
            var takeDedicated = Math.Min(dedicated, remaining);
            if (takeDedicated <= 0) continue;
            if (subtest == "writing")
            {
                lot.WritingOnlyCredits -= takeDedicated;
                writing += takeDedicated;
                allocations[lot.Id] = Track(lot) with { Writing = Track(lot).Writing + takeDedicated };
            }
            else
            {
                lot.SpeakingOnlyCredits -= takeDedicated;
                speaking += takeDedicated;
                allocations[lot.Id] = Track(lot) with { Speaking = Track(lot).Speaking + takeDedicated };
            }
            remaining -= takeDedicated;
        }

        foreach (var lot in LiveLots(account))
        {
            if (remaining <= 0) break;
            var takeFlexible = Math.Min(lot.FlexibleCredits, remaining);
            if (takeFlexible <= 0) continue;
            lot.FlexibleCredits -= takeFlexible;
            remaining -= takeFlexible;
            flexible += takeFlexible;
            allocations[lot.Id] = Track(lot) with { Flexible = Track(lot).Flexible + takeFlexible };
        }

        foreach (var lot in LiveLots(account))
        {
            if (remaining <= 0) break;
            var takeSharedActivities = Math.Min(remaining, lot.SharedCredits / AiGradingCreditCost.SharedWritingOrSpeaking);
            if (takeSharedActivities <= 0) continue;
            var sharedUnitsTaken = takeSharedActivities * AiGradingCreditCost.SharedWritingOrSpeaking;
            lot.SharedCredits -= sharedUnitsTaken;
            remaining -= takeSharedActivities;
            sharedActivities += takeSharedActivities;
            allocations[lot.Id] = Track(lot) with { Shared = Track(lot).Shared + sharedUnitsTaken };
        }

        RebuildAccountFromLots(account);
        var sharedUnits = sharedActivities * AiGradingCreditCost.SharedWritingOrSpeaking;
        var label = subtest == "writing" ? "Writing" : "Speaking";
        string feedback;
        string balanceSource;
        if (sharedActivities > 0 && writing + speaking + flexible == 0)
        {
            feedback = FormatSharedUsed(label, sharedUnits, account.SharedCredits);
            balanceSource = "shared";
        }
        else if (writing + speaking > 0 && flexible == 0 && sharedActivities == 0)
        {
            feedback = FormatUsedRemaining($"{label} Credit", writing + speaking, subtest == "writing" ? account.WritingOnlyCredits : account.SpeakingOnlyCredits);
            balanceSource = "dedicated";
        }
        else if (flexible > 0 && writing + speaking == 0 && sharedActivities == 0)
        {
            feedback = FormatUsedRemaining("Flexible Writing/Speaking Credit", flexible, account.FlexibleCredits);
            balanceSource = "flexible_ws";
        }
        else
        {
            feedback = FormatUsedRemaining($"{label} Credit", quantity, RemainingAfterSpend(account, subtest));
            balanceSource = "mixed";
        }

        return new SpendResult(
            -sharedUnits,
            -flexible,
            -writing,
            -speaking,
            SerializeAllocations(allocations.Values),
            feedback,
            balanceSource,
            writing + speaking + flexible + sharedUnits);
    }

    private static int RemainingAfterSpend(AiPackageCreditAccount account, string subtest)
        => subtest == "writing"
            ? account.WritingOnlyCredits + account.FlexibleCredits + (account.SharedCredits / AiGradingCreditCost.SharedWritingOrSpeaking)
            : account.SpeakingOnlyCredits + account.FlexibleCredits + (account.SharedCredits / AiGradingCreditCost.SharedWritingOrSpeaking);

    private List<LotAllocation> SpendDedicatedObjective(AiPackageCreditAccount account, string subtest, int quantity)
    {
        var remaining = quantity;
        var allocations = new List<LotAllocation>();
        foreach (var lot in LiveLots(account))
        {
            if (remaining <= 0) break;
            if (subtest == "listening")
            {
                if (lot.UnlimitedListening || lot.ListeningTestsRemaining is null) continue;
                var take = Math.Min(lot.ListeningTestsRemaining.Value, remaining);
                if (take <= 0) continue;
                lot.ListeningTestsRemaining -= take;
                remaining -= take;
                allocations.Add(new LotAllocation(lot.Id, 0, 0, 0, 0, take, 0, 0));
            }
            else
            {
                if (lot.UnlimitedReading || lot.ReadingTestsRemaining is null) continue;
                var take = Math.Min(lot.ReadingTestsRemaining.Value, remaining);
                if (take <= 0) continue;
                lot.ReadingTestsRemaining -= take;
                remaining -= take;
                allocations.Add(new LotAllocation(lot.Id, 0, 0, 0, 0, 0, take, 0));
            }
        }

        RebuildAccountFromLots(account);
        return allocations;
    }

    private List<LotAllocation> SpendSharedFromLots(AiPackageCreditAccount account, int units)
    {
        var remaining = units;
        var allocations = new List<LotAllocation>();
        foreach (var lot in LiveLots(account))
        {
            if (remaining <= 0) break;
            var take = Math.Min(lot.SharedCredits, remaining);
            if (take <= 0) continue;
            lot.SharedCredits -= take;
            remaining -= take;
            allocations.Add(new LotAllocation(lot.Id, take, 0, 0, 0, 0, 0, 0));
        }

        RebuildAccountFromLots(account);
        return allocations;
    }

    private List<LotAllocation> SpendMockFromLots(AiPackageCreditAccount account, int units)
    {
        var remaining = units;
        var allocations = new List<LotAllocation>();
        foreach (var lot in LiveLots(account))
        {
            if (remaining <= 0) break;
            var take = Math.Min(lot.MockExamsRemaining, remaining);
            if (take <= 0) continue;
            lot.MockExamsRemaining -= take;
            remaining -= take;
            allocations.Add(new LotAllocation(lot.Id, 0, 0, 0, 0, 0, 0, take));
        }

        RebuildAccountFromLots(account);
        return allocations;
    }

    private void RestoreAllocation(AiPackageCreditAccount account, AiPackageCreditTransaction debit)
    {
        var allocations = ParseAllocations(debit.AllocationJson);
        if (allocations.Count == 0)
        {
            var fallbackLot = LiveLots(account).FirstOrDefault() ?? CreateAdminLot(account, debit.ExpiresAt);
            fallbackLot.SharedCredits += Math.Abs(debit.SharedCreditsDelta);
            fallbackLot.FlexibleCredits += Math.Abs(debit.FlexibleCreditsDelta);
            fallbackLot.WritingOnlyCredits += Math.Abs(debit.WritingOnlyCreditsDelta);
            fallbackLot.SpeakingOnlyCredits += Math.Abs(debit.SpeakingOnlyCreditsDelta);
            fallbackLot.MockExamsRemaining += Math.Abs(debit.MockExamsDelta);
            if (debit.ListeningTestsDelta != 0)
            {
                fallbackLot.ListeningTestsRemaining = (fallbackLot.ListeningTestsRemaining ?? 0) + Math.Abs(debit.ListeningTestsDelta);
            }
            if (debit.ReadingTestsDelta != 0)
            {
                fallbackLot.ReadingTestsRemaining = (fallbackLot.ReadingTestsRemaining ?? 0) + Math.Abs(debit.ReadingTestsDelta);
            }
            RebuildAccountFromLots(account);
            return;
        }

        foreach (var allocation in allocations)
        {
            var lot = db.AiPackageCreditLots.Local.FirstOrDefault(row => row.Id == allocation.LotId)
                      ?? LiveLots(account).FirstOrDefault();
            if (lot is null)
            {
                lot = CreateAdminLot(account, debit.ExpiresAt);
            }

            lot.Expired = false;
            lot.ExpiredAt = null;
            lot.SharedCredits += allocation.Shared;
            lot.FlexibleCredits += allocation.Flexible;
            lot.WritingOnlyCredits += allocation.Writing;
            lot.SpeakingOnlyCredits += allocation.Speaking;
            lot.MockExamsRemaining += allocation.Mocks;
            if (allocation.Listening != 0)
            {
                lot.ListeningTestsRemaining = (lot.ListeningTestsRemaining ?? 0) + allocation.Listening;
            }
            if (allocation.Reading != 0)
            {
                lot.ReadingTestsRemaining = (lot.ReadingTestsRemaining ?? 0) + allocation.Reading;
            }
        }

        RebuildAccountFromLots(account);
    }

    private AiPackageCreditLot CreateAdminLot(AiPackageCreditAccount account, DateTimeOffset? expiresAt)
    {
        var lot = new AiPackageCreditLot
        {
            Id = NewId("aipkg-lot"),
            PackageId = "admin",
            PackageType = "admin",
            ExpiresAt = expiresAt ?? account.ExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        AddLot(account, lot);
        return lot;
    }

    private void ApplyAdminAdjustmentToLots(
        AiPackageCreditAccount account,
        int sharedDelta,
        int flexibleDelta,
        int writingDelta,
        int speakingDelta,
        int listeningDelta,
        int readingDelta,
        int mockDelta,
        DateTimeOffset? expiresAt)
    {
        if (sharedDelta > 0 || flexibleDelta > 0 || writingDelta > 0 || speakingDelta > 0 || listeningDelta > 0 || readingDelta > 0 || mockDelta > 0)
        {
            AddLot(account, new AiPackageCreditLot
            {
                Id = NewId("aipkg-lot"),
                PackageId = "admin",
                PackageType = "admin",
                SharedCredits = Math.Max(0, sharedDelta),
                FlexibleCredits = Math.Max(0, flexibleDelta),
                WritingOnlyCredits = Math.Max(0, writingDelta),
                SpeakingOnlyCredits = Math.Max(0, speakingDelta),
                ListeningTestsRemaining = listeningDelta > 0 ? listeningDelta : 0,
                ReadingTestsRemaining = readingDelta > 0 ? readingDelta : 0,
                MockExamsRemaining = Math.Max(0, mockDelta),
                ExpiresAt = expiresAt ?? account.ExpiresAt,
                SourceReferenceId = $"admin-adjust:{account.Id}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        if (sharedDelta < 0)
        {
            SpendSharedFromLots(account, -sharedDelta);
        }
        if (flexibleDelta < 0)
        {
            var remaining = -flexibleDelta;
            foreach (var lot in LiveLots(account))
            {
                if (remaining <= 0) break;
                var take = Math.Min(lot.FlexibleCredits, remaining);
                lot.FlexibleCredits -= take;
                remaining -= take;
            }
        }
        if (writingDelta < 0)
        {
            var remaining = -writingDelta;
            foreach (var lot in LiveLots(account))
            {
                if (remaining <= 0) break;
                var take = Math.Min(lot.WritingOnlyCredits, remaining);
                lot.WritingOnlyCredits -= take;
                remaining -= take;
            }
        }
        if (speakingDelta < 0)
        {
            var remaining = -speakingDelta;
            foreach (var lot in LiveLots(account))
            {
                if (remaining <= 0) break;
                var take = Math.Min(lot.SpeakingOnlyCredits, remaining);
                lot.SpeakingOnlyCredits -= take;
                remaining -= take;
            }
        }
        if (listeningDelta < 0) SpendDedicatedObjective(account, "listening", -listeningDelta);
        if (readingDelta < 0) SpendDedicatedObjective(account, "reading", -readingDelta);
        if (mockDelta < 0) SpendMockFromLots(account, -mockDelta);
    }

    private void ZeroAllLots(AiPackageCreditAccount account, DateTimeOffset now)
    {
        foreach (var lot in LiveLots(account))
        {
            lot.SharedCredits = 0;
            lot.FlexibleCredits = 0;
            lot.WritingOnlyCredits = 0;
            lot.SpeakingOnlyCredits = 0;
            lot.ListeningTestsRemaining = 0;
            lot.ReadingTestsRemaining = 0;
            lot.MockExamsRemaining = 0;
            lot.UnlimitedGrading = false;
            lot.UnlimitedListening = false;
            lot.UnlimitedReading = false;
            lot.Expired = true;
            lot.ExpiredAt = now;
        }

        RebuildAccountFromLots(account);
    }

    private static int ResolveAdminDelta(int currentRemaining, int used, int delta, int? set, string label)
    {
        if (set is int targetTotal)
        {
            // SetExact targets TOTAL. Validate Total must never be lower than Used.
            if (targetTotal < used)
            {
                throw ApiException.Validation(
                    "credits_total_below_used",
                    $"Cannot set {label} to {targetTotal}: the learner has already used {used}, " +
                    $"and Total can never be lower than Used");
            }
            // Remaining = Total - Used. Delta moves currentRemaining to (targetTotal - used).
            var targetRemaining = targetTotal - used;
            return targetRemaining - currentRemaining;
        }

        // Delta mode: validation applies to negative admin adjustments too — the
        // resulting Remaining (and therefore Total) must never dip below Used.
        if (delta < 0 && currentRemaining + delta < 0)
        {
            throw ApiException.Validation(
                "credits_total_below_used",
                $"Cannot remove {Math.Abs(delta)} {label}: only {currentRemaining} remaining ({used} used), " +
                "and Total can never fall below Used");
        }

        return delta;
    }

    private static string FormatUsedRemaining(string unit, int used, int remaining)
        => $"{used} {unit}{(used == 1 ? "" : "s")} used. {remaining} {unit}{(remaining == 1 ? "" : "s")} remaining.";

    private static string FormatSharedUsed(string activity, int used, int remaining)
        => $"{used} Shared Credit{(used == 1 ? "" : "s")} used for {ToTitle(activity)}. {remaining} Shared Credit{(remaining == 1 ? "" : "s")} remaining.";

    private static string ToTitle(string value)
        => string.IsNullOrWhiteSpace(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..];

    private static string InferActivitySubtest(string description)
    {
        var lower = description.ToLowerInvariant();
        if (lower.Contains("writing")) return "writing";
        if (lower.Contains("speaking")) return "speaking";
        if (lower.Contains("listening")) return "listening";
        if (lower.Contains("reading")) return "reading";
        return "shared";
    }

    private static string SerializeAllocations(IEnumerable<LotAllocation> allocations)
        => JsonSerializer.Serialize(allocations.Select(item => new
        {
            lotId = item.LotId,
            shared = item.Shared,
            flexible = item.Flexible,
            writing = item.Writing,
            speaking = item.Speaking,
            listening = item.Listening,
            reading = item.Reading,
            mocks = item.Mocks,
        }));

    private static List<LotAllocation> ParseAllocations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return doc.RootElement.EnumerateArray()
                    .Select(ParseAllocation)
                    .Where(item => item is not null)
                    .Cast<LotAllocation>()
                    .ToList();
            }

            var single = ParseAllocation(doc.RootElement);
            return single is null ? [] : [single];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static LotAllocation? ParseAllocation(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        return new LotAllocation(
            element.TryGetProperty("lotId", out var lotId) ? lotId.GetString() : null,
            ReadAbs(element, "shared"),
            ReadAbs(element, "flexible"),
            ReadAbs(element, "writing"),
            ReadAbs(element, "speaking"),
            ReadAbs(element, "listening"),
            ReadAbs(element, "reading"),
            ReadAbs(element, "mocks"));
    }

    private static int ReadAbs(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed)
            ? Math.Abs(parsed)
            : 0;

    private static AiPackageCreditBucketSnapshot BuildBucket(
        IReadOnlyList<AiPackageCreditLot> liveLots,
        IReadOnlyList<AiPackageCreditLot> allLots,
        string kind,
        bool unlimitedGrading,
        DateTimeOffset now)
    {
        var unlimited = kind switch
        {
            "writing" or "speaking" or "flexible" => unlimitedGrading || liveLots.Any(lot => lot.UnlimitedGrading),
            "listening" => liveLots.Any(lot => lot.UnlimitedListening),
            "reading" => liveLots.Any(lot => lot.UnlimitedReading),
            _ => false,
        };
        int Remaining(AiPackageCreditLot lot) => kind switch
        {
            "shared" => lot.SharedCredits,
            "flexible" => lot.FlexibleCredits,
            "writing" => lot.WritingOnlyCredits,
            "speaking" => lot.SpeakingOnlyCredits,
            "listening" => lot.ListeningTestsRemaining ?? 0,
            "reading" => lot.ReadingTestsRemaining ?? 0,
            "mocks" => lot.MockExamsRemaining,
            _ => 0,
        };
        var remaining = unlimited && kind is "writing" or "speaking" or "flexible" ? 0 : liveLots.Sum(Remaining);
        var granted = allLots.Sum(Remaining);
        var used = Math.Max(0, granted - remaining);
        var expires = liveLots
            .Where(lot => Remaining(lot) > 0 || (unlimited && kind is "writing" or "speaking" or "listening" or "reading"))
            .Select(lot => lot.ExpiresAt)
            .Where(value => value is not null)
            .OrderBy(value => value)
            .FirstOrDefault();
        var sources = liveLots
            .Where(lot => Remaining(lot) > 0 || lot.UnlimitedGrading || lot.UnlimitedListening || lot.UnlimitedReading)
            .Select(lot => lot.PackageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();
        int? daysLeft = expires is { } expiry ? Math.Max(0, (int)Math.Ceiling((expiry - now).TotalDays)) : null;
        return new AiPackageCreditBucketSnapshot(granted, used, remaining, unlimited, sources, expires, daysLeft);
    }

    private sealed record SpendResult(
        int SharedDelta,
        int FlexibleDelta,
        int WritingDelta,
        int SpeakingDelta,
        string AllocationJson,
        string FeedbackMessage,
        string BalanceSource,
        int CreditsUsed);

    private sealed record LotAllocation(
        string? LotId,
        int Shared,
        int Flexible,
        int Writing,
        int Speaking,
        int Listening,
        int Reading,
        int Mocks);

    private sealed record AiPackageGrant(
        string PackageType,
        int SharedCredits,
        int FlexibleCredits,
        int WritingOnlyCredits,
        int SpeakingOnlyCredits,
        int? ListeningTests,
        int? ReadingTests,
        int MockExams,
        bool UnlimitedGrading)
    {
        public static AiPackageGrant FromAddOn(BillingAddOn addOn, int quantity)
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(addOn.GrantEntitlementsJson) ? "{}" : addOn.GrantEntitlementsJson);
            var root = doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement : default;
            var packageType = ReadString(root, "package_type") ?? ResolvePackageType(addOn.Code);
            var unlimitedGrading = ReadBool(root, "unlimited_grading");
            var hasFlexibleKey = HasProperty(root, "flexible_credits");
            var flexible = unlimitedGrading
                ? 0
                : ReadInt(root, "flexible_credits") ?? 0;
            var shared = unlimitedGrading
                ? 0
                : ReadInt(root, "shared_credits") ?? 0;
            var writing = unlimitedGrading
                ? 0
                : ReadInt(root, "writing_only_credits") ?? (packageType == "writing" ? addOn.GrantCredits : 0);
            var speaking = unlimitedGrading
                ? 0
                : ReadInt(root, "speaking_only_credits") ?? (packageType == "speaking" ? addOn.GrantCredits : 0);
            if (!unlimitedGrading && shared == 0 && flexible == 0)
            {
                var leftover = ReadInt(root, "ai_credits") ?? 0;
                if (leftover == 0
                    && addOn.GrantCredits > 0
                    && writing == 0
                    && speaking == 0
                    && !hasFlexibleKey)
                {
                    leftover = addOn.GrantCredits;
                }
                shared = leftover;
            }

            var listening = ReadNullableAllowance(root, "listening_tests");
            var reading = ReadNullableAllowance(root, "reading_tests");
            var mocks = ReadInt(root, "mock_exams") ?? ReadInt(root, "mockFull") ?? 0;

            return new(
                packageType,
                shared * quantity,
                flexible * quantity,
                writing * quantity,
                speaking * quantity,
                listening is null ? null : listening * quantity,
                reading is null ? null : reading * quantity,
                mocks * quantity,
                unlimitedGrading);
        }

        private static string ResolvePackageType(string code)
        {
            if (code.StartsWith("pkg_listening", StringComparison.OrdinalIgnoreCase)) return "listening";
            if (code.StartsWith("pkg_reading", StringComparison.OrdinalIgnoreCase)) return "reading";
            if (code.StartsWith("pkg_writing", StringComparison.OrdinalIgnoreCase)) return "writing";
            if (code.StartsWith("pkg_speaking", StringComparison.OrdinalIgnoreCase)) return "speaking";
            if (code.StartsWith("pkg_mock", StringComparison.OrdinalIgnoreCase)) return "mock";
            return "full";
        }

        private static bool HasProperty(JsonElement root, string name)
            => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out _);

        private static int? ReadInt(JsonElement root, string name)
            => root.ValueKind == JsonValueKind.Object
               && root.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.Number
               && value.TryGetInt32(out var parsed)
                ? Math.Max(0, parsed)
                : null;

        private static string? ReadString(JsonElement root, string name)
            => root.ValueKind == JsonValueKind.Object
               && root.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static bool ReadBool(JsonElement root, string name)
            => root.ValueKind == JsonValueKind.Object
               && root.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.True;

        private static int? ReadNullableAllowance(JsonElement root, string name)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value))
            {
                return 0;
            }

            return value.ValueKind == JsonValueKind.Null
                ? null
                : value.TryGetInt32(out var parsed) ? Math.Max(0, parsed) : 0;
        }
    }
}
