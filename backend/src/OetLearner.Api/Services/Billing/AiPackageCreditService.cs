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
    /// Grant the Full Course gifted AI credits into the wallet exams actually
    /// debit (flexible pool). Idempotent on <paramref name="referenceId"/>.
    /// Writing letter = 2, Speaking exam = 2 (1/card), Listening/Reading = 1.
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
    bool SpeakingUnlimited = false);

public sealed record AiPackageCreditTransactionDto(
    string Id,
    string? PackageId,
    string? PackageType,
    string Reason,
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

public sealed record AiPackageDebitResult(bool Debited, string? ErrorCode, string? ErrorMessage, string? DebitReferenceId, bool Bypassed = false);

public sealed record AiPackageCreditAdjustmentRequest(
    int FlexibleCreditsDelta,
    int WritingOnlyCreditsDelta,
    int SpeakingOnlyCreditsDelta,
    int ListeningTestsDelta,
    int ReadingTestsDelta,
    int MockExamsDelta,
    DateTimeOffset? ExpiresAt,
    string? Reason);

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

        account.FlexibleCredits += credits;
        account.ExpiredBecausePassed = false;
        account.PassedAt = null;
        if (expiresAt is { } expiry && expiry > now)
        {
            account.ExpiresAt = Later(account.ExpiresAt, expiry);
        }

        // Null L/R means unlimited on paid AI packages. A course gift must not
        // inherit that: empty finite pools so Listening/Reading spend flexible.
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
            FlexibleCreditsDelta = credits,
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

        // The dedicated subtest pool (Writing-only / Speaking-only) is drawn
        // down first, then the flexible pool covers the remainder. All-or-
        // nothing: if the combined balance cannot fund the full quantity we
        // debit nothing and report insufficient credits, so a multi-credit
        // exam (e.g. a 2-credit Writing letter) never leaves a learner
        // half-charged. Debiting quantity units in one ledger row keeps the
        // charge atomic and refundable in a single RefundAsync call.
        var dedicated = normalized == "writing" ? account.WritingOnlyCredits : account.SpeakingOnlyCredits;
        if (dedicated + account.FlexibleCredits < quantity)
        {
            return new(false, "no_ai_package_credits", NoCreditsMessage, null);
        }

        var fromDedicated = Math.Min(dedicated, quantity);
        var fromFlexible = quantity - fromDedicated;
        var writingDelta = 0;
        var speakingDelta = 0;
        if (normalized == "writing")
        {
            account.WritingOnlyCredits -= fromDedicated;
            writingDelta = -fromDedicated;
        }
        else
        {
            account.SpeakingOnlyCredits -= fromDedicated;
            speakingDelta = -fromDedicated;
        }
        account.FlexibleCredits -= fromFlexible;
        var flexibleDelta = -fromFlexible;

        account.UpdatedAt = DateTimeOffset.UtcNow;
        AddTransaction(account, new AiPackageCreditTransaction
        {
            Id = NewId("aipkg-tx"),
            PackageType = normalized,
            FlexibleCreditsDelta = flexibleDelta,
            WritingOnlyCreditsDelta = writingDelta,
            SpeakingOnlyCreditsDelta = speakingDelta,
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
        return new(true, null, null, referenceId);
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

        // Mirror DeductGradingCreditAsync's pool sizing so the start-of-exam
        // gate blocks a learner who cannot cover the full quantity (e.g. a
        // 2-credit Writing exam) rather than letting them start and fail at
        // submit.
        var dedicated = normalized == "writing" ? account.WritingOnlyCredits : account.SpeakingOnlyCredits;
        var hasCredit = dedicated + account.FlexibleCredits >= quantity;

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

        var listeningDelta = 0;
        var readingDelta = 0;
        var flexibleDelta = 0;
        if (normalized == "listening" && account.ListeningTestsRemaining is not null)
        {
            if (account.ListeningTestsRemaining > 0)
            {
                account.ListeningTestsRemaining--;
                listeningDelta = -AiGradingCreditCost.ListeningExam;
            }
            else if (account.FlexibleCredits >= AiGradingCreditCost.ListeningExam)
            {
                account.FlexibleCredits -= AiGradingCreditCost.ListeningExam;
                flexibleDelta = -AiGradingCreditCost.ListeningExam;
            }
            else
            {
                return new(false, "no_listening_tests", "You have no Listening practice tests remaining. Purchase a package to continue.", null);
            }
        }
        if (normalized == "reading" && account.ReadingTestsRemaining is not null)
        {
            if (account.ReadingTestsRemaining > 0)
            {
                account.ReadingTestsRemaining--;
                readingDelta = -AiGradingCreditCost.ReadingExam;
            }
            else if (account.FlexibleCredits >= AiGradingCreditCost.ReadingExam)
            {
                account.FlexibleCredits -= AiGradingCreditCost.ReadingExam;
                flexibleDelta = -AiGradingCreditCost.ReadingExam;
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
            FlexibleCreditsDelta = flexibleDelta,
            ListeningTestsDelta = listeningDelta,
            ReadingTestsDelta = readingDelta,
            Reason = AiPackageCreditReason.ObjectivePracticeDeduct,
            ReferenceId = referenceId,
            Description = flexibleDelta < 0
                ? $"{normalized} exam used {Math.Abs(flexibleDelta)} gifted AI credit"
                : $"{normalized} deterministic practice allowance used",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new(true, null, null, referenceId);
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
        return new(true, null, null, referenceId);
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
        account.FlexibleCredits += Math.Abs(debit.FlexibleCreditsDelta);
        account.WritingOnlyCredits += Math.Abs(debit.WritingOnlyCreditsDelta);
        account.SpeakingOnlyCredits += Math.Abs(debit.SpeakingOnlyCreditsDelta);
        account.MockExamsRemaining += Math.Abs(debit.MockExamsDelta);
        account.UpdatedAt = DateTimeOffset.UtcNow;

        AddTransaction(account, new AiPackageCreditTransaction
        {
            Id = NewId("aipkg-tx"),
            PackageType = debit.PackageType,
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
            var flexible = -account.FlexibleCredits;
            var writing = -account.WritingOnlyCredits;
            var speaking = -account.SpeakingOnlyCredits;
            var listening = -(account.ListeningTestsRemaining ?? 0);
            var reading = -(account.ReadingTestsRemaining ?? 0);
            var mocks = -account.MockExamsRemaining;
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

        var flexible = -account.FlexibleCredits;
        var writing = -account.WritingOnlyCredits;
        var speaking = -account.SpeakingOnlyCredits;
        var listening = -(account.ListeningTestsRemaining ?? 0);
        var reading = -(account.ReadingTestsRemaining ?? 0);
        var mocks = -account.MockExamsRemaining;
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
        if (account.FlexibleCredits != 0
            || account.WritingOnlyCredits != 0
            || account.SpeakingOnlyCredits != 0)
        {
            return false;
        }

        return !await db.AiPackageCreditTransactions.AsNoTracking()
            .AnyAsync(row => row.AccountId == account.Id
                             && (row.Reason == AiPackageCreditReason.Purchase
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

        var practiceRows = await db.AiPackageCreditTransactions.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => new { row.Reason, row.FlexibleCreditsDelta, row.WritingOnlyCreditsDelta, row.SpeakingOnlyCreditsDelta })
            .ToListAsync(ct);
        var creditsGranted = Math.Max(0, practiceRows
            .Where(row => row.Reason is AiPackageCreditReason.Purchase
                or AiPackageCreditReason.AdminAdjustment
                or AiPackageCreditReason.GrantReversed)
            .Sum(row => row.FlexibleCreditsDelta + row.WritingOnlyCreditsDelta + row.SpeakingOnlyCreditsDelta));
        var creditsUsed = practiceRows
            .Where(row => row.Reason is AiPackageCreditReason.GradingDeduct or AiPackageCreditReason.ObjectivePracticeDeduct)
            .Sum(row => Math.Max(0, -row.FlexibleCreditsDelta) + Math.Max(0, -row.WritingOnlyCreditsDelta) + Math.Max(0, -row.SpeakingOnlyCreditsDelta));
        var creditsRemaining = account.FlexibleCredits + account.WritingOnlyCredits + account.SpeakingOnlyCredits;
        var unlimitedGrading = await HasActiveUnlimitedGradingAsync(userId, DateTimeOffset.UtcNow, ct);

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
            unlimitedGrading);
    }

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
