using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

public interface IAiPackageCreditService
{
    Task<AiPackageCreditSnapshot> GetSnapshotAsync(string userId, int transactionLimit, CancellationToken ct);
    Task<AiPackageCreditSnapshot> GrantPackageAsync(string userId, BillingAddOn addOn, int quantity, string stripeSessionId, string? quoteId, CancellationToken ct);

    /// <summary>
    /// Grant the Full Course gifted AI credits into the universal Shared
    /// wallet (Reading 1, Listening 1, Writing 2, Speaking 2). Idempotent on
    /// <paramref name="referenceId"/>.
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
    /// Consume <paramref name="quantity"/> grading credits in one atomic,
    /// all-or-nothing debit (dedicated subtest pool first, then flexible).
    /// Used where a single exam costs more than one credit — e.g. a Writing
    /// exam costs <see cref="AiGradingCreditCost.WritingExam"/>.
    /// </summary>
    Task<AiPackageDebitResult> DeductGradingCreditAsync(string userId, string subtest, string referenceId, int quantity, CancellationToken ct);

    /// <summary>
    /// Read-only mirror of <see cref="DeductGradingCreditAsync"/> — reports
    /// whether a grading debit would succeed right now, without consuming a
    /// credit or writing a ledger transaction. Used to gate entry into an
    /// AI-graded practice session at attempt-start time; the actual credit is
    /// still consumed once, at submit, via <see cref="DeductGradingCreditAsync"/>.
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
}

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
    IReadOnlyList<AiPackageCreditBucketDto>? Buckets = null);

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
    DateTimeOffset? ExpiresAt = null,
    string? Reason = null,
    int SharedCreditsDelta = 0);

public sealed record LearnerExamOutcomeRequest(bool Passed, DateTimeOffset ExamDate, string? EvidenceNote);

public sealed class AiPackageCreditService(LearnerDbContext db, ILogger<AiPackageCreditService> logger) : IAiPackageCreditService
{
    private const string NoCreditsMessage = "You have no credits remaining. Purchase a package to continue.";

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
        account.FlexibleCredits += grant.FlexibleCredits;
        account.WritingOnlyCredits += grant.WritingOnlyCredits;
        account.SpeakingOnlyCredits += grant.SpeakingOnlyCredits;
        account.MockExamsRemaining += grant.MockExams;
        account.ListeningTestsRemaining = MergeObjectiveAllowance(account.ListeningTestsRemaining, grant.ListeningTests);
        account.ReadingTestsRemaining = MergeObjectiveAllowance(account.ReadingTestsRemaining, grant.ReadingTests);
        account.ExpiresAt = Later(account.ExpiresAt, newExpiry);
        account.ExpiredBecausePassed = false;
        account.PassedAt = null;
        account.UpdatedAt = now;

        AddTransaction(account, new AiPackageCreditTransaction
        {
            Id = NewId("aipkg-tx"),
            StripeSessionId = stripeSessionId,
            PackageId = addOn.Code,
            PackageType = grant.PackageType,
            FlexibleCreditsDelta = grant.FlexibleCredits,
            WritingOnlyCreditsDelta = grant.WritingOnlyCredits,
            SpeakingOnlyCreditsDelta = grant.SpeakingOnlyCredits,
            ListeningTestsDelta = grant.ListeningTests ?? 0,
            ReadingTestsDelta = grant.ReadingTests ?? 0,
            MockExamsDelta = grant.MockExams,
            Reason = AiPackageCreditReason.Purchase,
            ReferenceId = AddonGrantProcessor.FitDatabaseKey(
                quoteId is null ? $"stripe:{stripeSessionId}" : $"quote:{quoteId}:{addOn.Code}"),
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

        account.SharedCredits += credits;
        account.ExpiredBecausePassed = false;
        account.PassedAt = null;
        if (expiresAt is { } expiry && expiry > now)
        {
            account.ExpiresAt = Later(account.ExpiresAt, expiry);
        }

        // Null L/R means unlimited on paid AI packages. A course gift must not
        // inherit that: empty finite pools so Listening/Reading spend Shared.
        // Leave an existing paid unlimited allowance (pkg_*) intact.
        if (account.ListeningTestsRemaining is null || account.ReadingTestsRemaining is null)
        {
            var hasPaidAiPackage = await db.AiPackageCreditTransactions.AnyAsync(
                row => row.UserId == userId
                    && row.Reason == AiPackageCreditReason.Purchase
                    && row.PackageId != null
                    && row.PackageId.StartsWith("pkg_"),
                ct);
            if (!hasPaidAiPackage)
            {
                account.ListeningTestsRemaining ??= 0;
                account.ReadingTestsRemaining ??= 0;
            }
        }

        account.UpdatedAt = now;

        AddTransaction(account, new AiPackageCreditTransaction
        {
            Id = NewId("aipkg-tx"),
            PackageId = planCode,
            PackageType = "full",
            SharedCreditsDelta = credits,
            Reason = AiPackageCreditReason.Purchase,
            ReferenceId = referenceId,
            Description = $"{planName} gifted AI practice credits",
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

        if (listeningUnlimited)
        {
            account.ListeningTestsRemaining = null;
        }
        else
        {
            account.ListeningTestsRemaining = account.ListeningTestsRemaining is int listeningRemaining
                ? Math.Min(listeningRemaining, listeningSum)
                : listeningSum;
        }

        if (readingUnlimited)
        {
            account.ReadingTestsRemaining = null;
        }
        else
        {
            account.ReadingTestsRemaining = account.ReadingTestsRemaining is int readingRemaining
                ? Math.Min(readingRemaining, readingSum)
                : readingSum;
        }

        account.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await ReverseOrphanedGrantsAsync(userId, now, ct);
    }

    public Task<AiPackageDebitResult> DeductGradingCreditAsync(string userId, string subtest, string referenceId, CancellationToken ct)
        => DeductGradingCreditAsync(userId, subtest, referenceId, 1, ct);

    public async Task<AiPackageDebitResult> DeductGradingCreditAsync(string userId, string subtest, string referenceId, int quantity, CancellationToken ct)
    {
        quantity = Math.Max(1, quantity);
        var normalized = NormalizeSubtest(subtest);
        if (normalized is not ("writing" or "speaking"))
        {
            return new(false, "unsupported_subtest", "Only Writing and Speaking consume AI grading credits.", null);
        }

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var account = await GetOrCreateAccountAsync(userId, ct);
        var now = DateTimeOffset.UtcNow;
        await ExpireIfNeededAsync(account, now, ct);
        if (await TransactionExistsAsync(referenceId, AiPackageCreditReason.GradingDeduct, ct))
        {
            return new(false, "already_debited", "This grading job has already consumed a credit.", referenceId);
        }

        if (account.ExpiredBecausePassed || (account.ExpiresAt is not null && account.ExpiresAt <= now))
        {
            return new(false, "ai_package_expired", "Your AI package has expired. Purchase a package to continue.", null);
        }

        if (await HasActiveUnlimitedGradingAsync(userId, now, ct))
        {
            // OET Mastery is unlimited for Writing and Speaking for the life of
            // its purchased subscription item. No finite wallet unit or ledger
            // debit is created; cancellation/refund revokes the active item.
            return new(true, null, null, referenceId);
        }

        if (await ShouldBypassGradingDebitForLegacyAccountAsync(account, ct))
        {
            return new(true, null, null, referenceId);
        }

        // Master Catalogue §1 credit-cost matrix for ONE graded submission:
        //   dedicated Writing-only / Speaking-only pool → 1 credit,
        //   restricted Flexible W/S pool               → 1 credit,
        //   universal Shared Credits                   → 2 credits.
        // Priority follows §2: dedicated first, then Flexible W/S, then
        // Shared. All-or-nothing: if no single source can fund the full
        // activity we debit nothing and report insufficient credits, keeping
        // the charge atomic and refundable in one RefundAsync call.
        // <paramref name="quantity"/> is the submission count (callers pass 1).
        quantity = 1;
        var label = normalized == "writing" ? "Writing" : "Speaking";
        var dedicatedAvailable = normalized == "writing"
            ? account.WritingOnlyCredits
            : account.SpeakingOnlyCredits;

        int usedDedicated = 0, usedFlexible = 0, usedShared = 0;
        string? balanceSource;
        string feedbackMessage;

        if (dedicatedAvailable >= 1)
        {
            usedDedicated = 1;
            balanceSource = "dedicated";
            if (normalized == "writing")
            {
                account.WritingOnlyCredits -= 1;
                feedbackMessage = $"1 Writing Credit used. {account.WritingOnlyCredits} Writing Credits remaining.";
            }
            else
            {
                account.SpeakingOnlyCredits -= 1;
                feedbackMessage = $"1 Speaking Credit used. {account.SpeakingOnlyCredits} Speaking Credits remaining.";
            }
        }
        else if (account.FlexibleCredits >= 1)
        {
            usedFlexible = 1;
            balanceSource = "flexible_ws";
            account.FlexibleCredits -= 1;
            feedbackMessage = $"1 Flexible W/S Credit used for {label}. {account.FlexibleCredits} Flexible W/S Credits remaining.";
        }
        else if (account.SharedCredits >= AiGradingCreditCost.WritingExam)
        {
            usedShared = AiGradingCreditCost.WritingExam;
            balanceSource = "shared";
            account.SharedCredits -= usedShared;
            feedbackMessage = $"{usedShared} Shared Credits used for {label}. {account.SharedCredits} Shared Credits remaining.";
        }
        else
        {
            return new(false, "no_ai_package_credits", NoCreditsMessage, null);
        }

        var writingDelta = -usedDedicated * (normalized == "writing" ? 1 : 0);
        var speakingDelta = -usedDedicated * (normalized == "speaking" ? 1 : 0);

        account.UpdatedAt = DateTimeOffset.UtcNow;
        AddTransaction(account, new AiPackageCreditTransaction
        {
            Id = NewId("aipkg-tx"),
            PackageType = normalized,
            SharedCreditsDelta = -usedShared,
            FlexibleCreditsDelta = -usedFlexible,
            WritingOnlyCreditsDelta = writingDelta,
            SpeakingOnlyCreditsDelta = speakingDelta,
            Reason = AiPackageCreditReason.GradingDeduct,
            ReferenceId = referenceId,
            JobId = referenceId,
            Description = $"{label} AI grading credit deducted",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);

        return new(true, null, null, referenceId, Bypassed: false,
            BalanceSource: balanceSource,
            CreditsUsed: usedDedicated + usedFlexible + usedShared,
            RemainingAfter: account.WritingOnlyCredits + account.SpeakingOnlyCredits + account.FlexibleCredits + account.SharedCredits,
            FeedbackMessage: feedbackMessage);
    }

    public Task<AiPackageDebitResult> CheckGradingCreditAsync(string userId, string subtest, CancellationToken ct)
        => CheckGradingCreditAsync(userId, subtest, 1, ct);

    public async Task<AiPackageDebitResult> CheckGradingCreditAsync(string userId, string subtest, int quantity, CancellationToken ct)
    {
        quantity = Math.Max(1, quantity);
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
            return new(true, null, null, null);
        }

        if (await ShouldBypassGradingDebitForLegacyAccountAsync(account, ct))
        {
            return new(true, null, null, null);
        }

        // Mirror DeductGradingCreditAsync's eligibility rule so the
        // start-of-exam gate blocks a learner who cannot fund one submission:
        // dedicated pool ≥ 1, or Flexible W/S ≥ 1, or Shared ≥ 2.
        var dedicatedAvailable = normalized == "writing" ? account.WritingOnlyCredits : account.SpeakingOnlyCredits;
        var hasCredit = dedicatedAvailable >= 1
            || account.FlexibleCredits >= 1
            || account.SharedCredits >= AiGradingCreditCost.WritingExam;

        return hasCredit
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
        if (account.ExpiresAt is null
            && account.SharedCredits <= 0
            && account.FlexibleCredits <= 0
            && account.ListeningTestsRemaining.GetValueOrDefault() == 0
            && account.ReadingTestsRemaining.GetValueOrDefault() == 0)
        {
            return new(true, null, null, referenceId);
        }

        if (await TransactionExistsAsync(referenceId, AiPackageCreditReason.ObjectivePracticeDeduct, ct))
        {
            // Paper is the billing unit: this learner has already unlocked this
            // paper (referenceId is per-(user, subtest, paper) via
            // CreditGateExtensions.ObjectivePaperReference), so every other part
            // and every re-attempt of the same paper is free — allow, do not
            // charge again and do not block.
            return new(true, null, null, referenceId);
        }

        if (account.ExpiredBecausePassed || (account.ExpiresAt is not null && account.ExpiresAt <= DateTimeOffset.UtcNow))
        {
            return new(false, "ai_package_expired", "Your AI package has expired. Purchase a package to continue.", null);
        }

        // Master Catalogue priority for deterministic subtests: the
        // subtest-specific allowance first, then universal Shared Credits at
        // cost 1. The restricted Flexible W/S pool is NEVER consumed by
        // Reading/Listening.
        var listeningDelta = 0;
        var readingDelta = 0;
        var sharedDelta = 0;
        string? balanceSource = null;
        string? feedbackMessage = null;
        var label = normalized == "listening" ? "Listening" : "Reading";
        if (normalized == "listening")
        {
            if (account.ListeningTestsRemaining is null)
            {
                // Unlimited Listening for package validity: no debit.
            }
            else if (account.ListeningTestsRemaining > 0)
            {
                account.ListeningTestsRemaining--;
                listeningDelta = -AiGradingCreditCost.ListeningExam;
                balanceSource = "listening";
                feedbackMessage = $"1 Listening Credit used. {account.ListeningTestsRemaining} Listening Credits remaining.";
            }
            else if (account.SharedCredits >= AiGradingCreditCost.ListeningExam)
            {
                account.SharedCredits -= AiGradingCreditCost.ListeningExam;
                sharedDelta = -AiGradingCreditCost.ListeningExam;
                balanceSource = "shared";
                feedbackMessage = $"{AiGradingCreditCost.ListeningExam} Shared Credit used for Listening. {account.SharedCredits} Shared Credits remaining.";
            }
            else
            {
                return new(false, "no_listening_tests", "You have no Listening practice tests remaining. Purchase a package to continue.", null);
            }
        }
        if (normalized == "reading")
        {
            if (account.ReadingTestsRemaining is null)
            {
                // Unlimited Reading for package validity: no debit.
            }
            else if (account.ReadingTestsRemaining > 0)
            {
                account.ReadingTestsRemaining--;
                readingDelta = -AiGradingCreditCost.ReadingExam;
                balanceSource = "reading";
                feedbackMessage = $"1 Reading Credit used. {account.ReadingTestsRemaining} Reading Credits remaining.";
            }
            else if (account.SharedCredits >= AiGradingCreditCost.ReadingExam)
            {
                account.SharedCredits -= AiGradingCreditCost.ReadingExam;
                sharedDelta = -AiGradingCreditCost.ReadingExam;
                balanceSource = "shared";
                feedbackMessage = $"{AiGradingCreditCost.ReadingExam} Shared Credit used for Reading. {account.SharedCredits} Shared Credits remaining.";
            }
            else
            {
                return new(false, "no_reading_tests", "You have no Reading practice tests remaining. Purchase a package to continue.", null);
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
            Reason = AiPackageCreditReason.ObjectivePracticeDeduct,
            ReferenceId = referenceId,
            Description = sharedDelta < 0
                ? $"{normalized} exam used {Math.Abs(sharedDelta)} Shared credit"
                : $"{normalized} deterministic practice allowance used",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new(true, null, null, referenceId, Bypassed: false,
            BalanceSource: balanceSource,
            CreditsUsed: Math.Abs(listeningDelta + readingDelta + sharedDelta),
            RemainingAfter: (account.ListeningTestsRemaining ?? 0) + (account.ReadingTestsRemaining ?? 0) + account.SharedCredits,
            FeedbackMessage: feedbackMessage ?? $"Unlimited {label} practice — no credits consumed.");
    }

    public async Task<AiPackageDebitResult> DeductMockAsync(string userId, string referenceId, CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var account = await GetOrCreateAccountAsync(userId, ct);
        await ExpireIfNeededAsync(account, DateTimeOffset.UtcNow, ct);
        if (await TransactionExistsAsync(referenceId, AiPackageCreditReason.MockDeduct, ct))
        {
            return new(false, "already_debited", "This mock has already consumed allowance.", referenceId);
        }

        if (await ShouldBypassMockDebitForLegacyAccountAsync(account, ct))
        {
            // Not an AI-package customer — signal the bypass so the caller can
            // charge the add-on mock-credit ledger instead of skipping billing.
            return new(true, null, null, referenceId, Bypassed: true);
        }

        if (account.ExpiredBecausePassed || (account.ExpiresAt is not null && account.ExpiresAt <= DateTimeOffset.UtcNow))
        {
            return new(false, "ai_package_expired", "Your AI package has expired. Purchase a package to continue.", null);
        }
        if (account.MockExamsRemaining <= 0)
        {
            return new(false, "no_mock_exams", "You have no mock exams remaining. Purchase a package to continue.", null);
        }

        account.MockExamsRemaining--;
        account.UpdatedAt = DateTimeOffset.UtcNow;
        AddTransaction(account, new AiPackageCreditTransaction
        {
            Id = NewId("aipkg-tx"),
            PackageType = "mock",
            MockExamsDelta = -1,
            Reason = AiPackageCreditReason.MockDeduct,
            ReferenceId = referenceId,
            Description = "Mock exam allowance used",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new(true, null, null, referenceId, Bypassed: false,
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
                          && (row.Reason == AiPackageCreditReason.GradingDeduct || row.Reason == AiPackageCreditReason.MockDeduct))
            .OrderByDescending(row => row.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (debit is null)
        {
            return false;
        }

        var reason = debit.Reason == AiPackageCreditReason.MockDeduct
            ? AiPackageCreditReason.MockRefundOnFailure
            : AiPackageCreditReason.RefundOnFailure;
        account.SharedCredits += Math.Abs(debit.SharedCreditsDelta);
        account.FlexibleCredits += Math.Abs(debit.FlexibleCreditsDelta);
        account.WritingOnlyCredits += Math.Abs(debit.WritingOnlyCreditsDelta);
        account.SpeakingOnlyCredits += Math.Abs(debit.SpeakingOnlyCreditsDelta);
        account.MockExamsRemaining += Math.Abs(debit.MockExamsDelta);
        account.UpdatedAt = DateTimeOffset.UtcNow;

        AddTransaction(account, new AiPackageCreditTransaction
        {
            Id = NewId("aipkg-tx"),
            PackageType = debit.PackageType,
            SharedCreditsDelta = Math.Abs(debit.SharedCreditsDelta),
            FlexibleCreditsDelta = Math.Abs(debit.FlexibleCreditsDelta),
            WritingOnlyCreditsDelta = Math.Abs(debit.WritingOnlyCreditsDelta),
            SpeakingOnlyCreditsDelta = Math.Abs(debit.SpeakingOnlyCreditsDelta),
            MockExamsDelta = Math.Abs(debit.MockExamsDelta),
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
        account.SharedCredits = Math.Max(0, account.SharedCredits + request.SharedCreditsDelta);
        account.FlexibleCredits = Math.Max(0, account.FlexibleCredits + request.FlexibleCreditsDelta);
        account.WritingOnlyCredits = Math.Max(0, account.WritingOnlyCredits + request.WritingOnlyCreditsDelta);
        account.SpeakingOnlyCredits = Math.Max(0, account.SpeakingOnlyCredits + request.SpeakingOnlyCreditsDelta);
        account.MockExamsRemaining = Math.Max(0, account.MockExamsRemaining + request.MockExamsDelta);
        account.ListeningTestsRemaining = AdjustNullableAllowance(account.ListeningTestsRemaining, request.ListeningTestsDelta);
        account.ReadingTestsRemaining = AdjustNullableAllowance(account.ReadingTestsRemaining, request.ReadingTestsDelta);
        account.ExpiresAt = request.ExpiresAt ?? account.ExpiresAt;
        account.UpdatedAt = DateTimeOffset.UtcNow;

        AddTransaction(account, new AiPackageCreditTransaction
        {
            Id = NewId("aipkg-tx"),
            SharedCreditsDelta = request.SharedCreditsDelta,
            FlexibleCreditsDelta = request.FlexibleCreditsDelta,
            WritingOnlyCreditsDelta = request.WritingOnlyCreditsDelta,
            SpeakingOnlyCreditsDelta = request.SpeakingOnlyCreditsDelta,
            ListeningTestsDelta = request.ListeningTestsDelta,
            ReadingTestsDelta = request.ReadingTestsDelta,
            MockExamsDelta = request.MockExamsDelta,
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
            account.SharedCredits = 0;
            account.FlexibleCredits = 0;
            account.WritingOnlyCredits = 0;
            account.SpeakingOnlyCredits = 0;
            account.ListeningTestsRemaining = 0;
            account.ReadingTestsRemaining = 0;
            account.MockExamsRemaining = 0;
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
        if (account is not null) return account;

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
        if (account.ExpiresAt is null || account.ExpiresAt > now || account.ExpiredBecausePassed)
        {
            return;
        }

        if (await db.AiPackageCreditTransactions.AsNoTracking()
            .AnyAsync(row => row.AccountId == account.Id && row.Reason == AiPackageCreditReason.Expiry && row.ReferenceId == $"expiry:{account.ExpiresAt:O}", ct))
        {
            return;
        }

        var shared = -account.SharedCredits;
        var flexible = -account.FlexibleCredits;
        var writing = -account.WritingOnlyCredits;
        var speaking = -account.SpeakingOnlyCredits;
        var listening = -(account.ListeningTestsRemaining ?? 0);
        var reading = -(account.ReadingTestsRemaining ?? 0);
        var mocks = -account.MockExamsRemaining;
        account.SharedCredits = 0;
        account.FlexibleCredits = 0;
        account.WritingOnlyCredits = 0;
        account.SpeakingOnlyCredits = 0;
        account.ListeningTestsRemaining = 0;
        account.ReadingTestsRemaining = 0;
        account.MockExamsRemaining = 0;
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
            Reason = AiPackageCreditReason.Expiry,
            ReferenceId = $"expiry:{account.ExpiresAt:O}",
            Description = "AI package credits expired.",
            CreatedAt = now
        });
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

        var shared = -Math.Min(account.SharedCredits, Math.Max(0, purchase.SharedCreditsDelta));
        var flexible = -Math.Min(account.FlexibleCredits, Math.Max(0, purchase.FlexibleCreditsDelta));
        var writing = -Math.Min(account.WritingOnlyCredits, Math.Max(0, purchase.WritingOnlyCreditsDelta));
        var speaking = -Math.Min(account.SpeakingOnlyCredits, Math.Max(0, purchase.SpeakingOnlyCreditsDelta));
        var mocks = -Math.Min(account.MockExamsRemaining, Math.Max(0, purchase.MockExamsDelta));
        var listening = 0;
        var reading = 0;
        if (account.ListeningTestsRemaining is int listeningRemaining && purchase.ListeningTestsDelta > 0)
        {
            listening = -Math.Min(listeningRemaining, purchase.ListeningTestsDelta);
            account.ListeningTestsRemaining = listeningRemaining + listening;
        }
        if (account.ReadingTestsRemaining is int readingRemaining && purchase.ReadingTestsDelta > 0)
        {
            reading = -Math.Min(readingRemaining, purchase.ReadingTestsDelta);
            account.ReadingTestsRemaining = readingRemaining + reading;
        }

        account.SharedCredits += shared;
        account.FlexibleCredits += flexible;
        account.WritingOnlyCredits += writing;
        account.SpeakingOnlyCredits += speaking;
        account.MockExamsRemaining += mocks;
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
                row.CreatedAt))
            .ToListAsync(ct);

        static bool IsGrantReason(AiPackageCreditReason reason)
            => reason is AiPackageCreditReason.Purchase
                or AiPackageCreditReason.AdminAdjustment
                or AiPackageCreditReason.GrantReversed;

        static bool IsDebitReason(AiPackageCreditReason reason)
            => reason is AiPackageCreditReason.GradingDeduct
                or AiPackageCreditReason.ObjectivePracticeDeduct
                or AiPackageCreditReason.MockDeduct;

        var grantRows = ledgerRows.Where(row => IsGrantReason(row.Reason)).ToList();
        var debitRows = ledgerRows.Where(row => IsDebitReason(row.Reason)).ToList();

        var creditsGranted = grantRows.Sum(row =>
            Math.Max(0, row.SharedCreditsDelta) + Math.Max(0, row.FlexibleCreditsDelta)
            + Math.Max(0, row.WritingOnlyCreditsDelta) + Math.Max(0, row.SpeakingOnlyCreditsDelta));
        var creditsUsed = debitRows.Sum(row =>
            Math.Max(0, -row.SharedCreditsDelta) + Math.Max(0, -row.FlexibleCreditsDelta)
            + Math.Max(0, -row.WritingOnlyCreditsDelta) + Math.Max(0, -row.SpeakingOnlyCreditsDelta));
        var sharedGranted = grantRows.Sum(row => Math.Max(0, row.SharedCreditsDelta));
        var creditsRemaining = account.SharedCredits + account.FlexibleCredits
            + account.WritingOnlyCredits + account.SpeakingOnlyCredits;
        var unlimitedGrading = await HasActiveUnlimitedGradingAsync(userId, DateTimeOffset.UtcNow, ct);

        var buckets = BuildBucketDtos(account, grantRows, unlimitedGrading);

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
            debitRows.Sum(row => Math.Max(0, -row.SharedCreditsDelta)),
            buckets);
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
        DateTimeOffset CreatedAt);

    private static IReadOnlyList<AiPackageCreditBucketDto> BuildBucketDtos(
        AiPackageCreditAccount account,
        List<LedgerRow> grantRows,
        bool writingSpeakingUnlimited)
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

        AiPackageCreditBucketDto Bucket(string key, string label, int remaining, bool unlimited, List<AiPackageCreditGrantSourceDto> grants)
        {
            var totalGranted = grants.Sum(source => source.TotalGranted);
            var activeGrants = grants.Where(source => source.ExpiresAt is null || source.ExpiresAt > now).ToList();
            var validFrom = activeGrants.Count > 0 ? activeGrants.Min(source => source.GrantedAt) : null;
            DateTimeOffset? expiresAt = activeGrants.Any(source => source.ExpiresAt is null)
                ? null
                : activeGrants.Select(source => source.ExpiresAt).Max();
            var used = unlimited ? 0 : Math.Clamp(totalGranted - remaining, 0, int.MaxValue);
            return new(
                key,
                label,
                unlimited,
                totalGranted,
                used,
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
            Bucket("reading", "Reading Credits", account.ReadingTestsRemaining ?? 0, account.ReadingTestsRemaining is null, GrantsFor(row => row.ReadingTestsDelta)),
            Bucket("listening", "Listening Credits", account.ListeningTestsRemaining ?? 0, account.ListeningTestsRemaining is null, GrantsFor(row => row.ListeningTestsDelta)),
            Bucket("writing", "Writing Credits",
                writingSpeakingUnlimited ? 0 : account.WritingOnlyCredits,
                writingSpeakingUnlimited,
                MergeGradingGrants(grantRows, "writing")),
            Bucket("speaking", "Speaking Credits",
                writingSpeakingUnlimited ? 0 : account.SpeakingOnlyCredits,
                writingSpeakingUnlimited,
                MergeGradingGrants(grantRows, "speaking")),
            Bucket("shared", "Shared Credits", account.SharedCredits, false, GrantsFor(row => row.SharedCreditsDelta)),
        };

        var flexibleWsGrants = GrantsFor(row => row.FlexibleCreditsDelta);
        if (account.FlexibleCredits > 0 || flexibleWsGrants.Count > 0)
        {
            buckets.Add(Bucket("flexible_ws", "Flexible W/S Credits", account.FlexibleCredits, false, flexibleWsGrants));
        }

        var mockGrants = GrantsFor(row => row.MockExamsDelta);
        if (account.MockExamsRemaining > 0 || mockGrants.Count > 0)
        {
            buckets.Add(Bucket("mock", "Full Mock Attempts", account.MockExamsRemaining, false, mockGrants));
        }

        return buckets;
    }

    /// <summary>
    /// Writing/Speaking dedicated-pool grants come from writing_only_credits /
    /// speaking_only_credits ledger deltas; legacy purchases that granted the
    /// same pool through the old flexible column are folded in so the bucket
    /// still shows a truthful Total.
    /// </summary>
    private static List<AiPackageCreditGrantSourceDto> MergeGradingGrants(List<LedgerRow> grantRows, string subtest)
    {
        Func<LedgerRow, int> dedicated = subtest == "writing"
            ? (Func<LedgerRow, int>)(row => row.WritingOnlyCreditsDelta)
            : row => row.SpeakingOnlyCreditsDelta;
        return grantRows
            .Where(row => dedicated(row) > 0)
            .GroupBy(row => row.PackageId ?? row.Description)
            .Select(group =>
            {
                var first = group.First();
                return new AiPackageCreditGrantSourceDto(
                    first.PackageId,
                    first.Description,
                    group.Sum(row => dedicated(row)),
                    group.Min(row => row.CreatedAt),
                    group.Max(row => row.ExpiresAt));
            })
            .OrderBy(source => source.GrantedAt)
            .ToList();
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

    private sealed record AiPackageGrant(
        string PackageType,
        int FlexibleCredits,
        int WritingOnlyCredits,
        int SpeakingOnlyCredits,
        int? ListeningTests,
        int? ReadingTests,
        int MockExams)
    {
        public static AiPackageGrant FromAddOn(BillingAddOn addOn, int quantity)
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(addOn.GrantEntitlementsJson) ? "{}" : addOn.GrantEntitlementsJson);
            var root = doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement : default;
            var packageType = ReadString(root, "package_type") ?? ResolvePackageType(addOn.Code);
            var unlimitedGrading = ReadBool(root, "unlimited_grading");
            var flexible = unlimitedGrading
                ? 0
                : ReadInt(root, "flexible_credits") ?? (packageType == "full" ? addOn.GrantCredits : 0);
            var writing = unlimitedGrading
                ? 0
                : ReadInt(root, "writing_only_credits") ?? (packageType == "writing" ? addOn.GrantCredits : 0);
            var speaking = unlimitedGrading
                ? 0
                : ReadInt(root, "speaking_only_credits") ?? (packageType == "speaking" ? addOn.GrantCredits : 0);
            var listening = ReadNullableAllowance(root, "listening_tests");
            var reading = ReadNullableAllowance(root, "reading_tests");
            var mocks = ReadInt(root, "mock_exams") ?? ReadInt(root, "mockFull") ?? 0;

            return new(
                packageType,
                flexible * quantity,
                writing * quantity,
                speaking * quantity,
                listening is null ? null : listening * quantity,
                reading is null ? null : reading * quantity,
                mocks * quantity);
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
