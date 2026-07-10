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
    Task<AiPackageDebitResult> DeductGradingCreditAsync(string userId, string subtest, string referenceId, CancellationToken ct);

    /// <summary>
    /// Read-only mirror of <see cref="DeductGradingCreditAsync"/> — reports
    /// whether a grading debit would succeed right now, without consuming a
    /// credit or writing a ledger transaction. Used to gate entry into an
    /// AI-graded practice session at attempt-start time; the actual credit is
    /// still consumed once, at submit, via <see cref="DeductGradingCreditAsync"/>.
    /// </summary>
    Task<AiPackageDebitResult> CheckGradingCreditAsync(string userId, string subtest, CancellationToken ct);
    Task<AiPackageDebitResult> DeductObjectivePracticeAsync(string userId, string subtest, string referenceId, CancellationToken ct);
    Task<AiPackageDebitResult> DeductMockAsync(string userId, string referenceId, CancellationToken ct);
    Task<bool> RefundAsync(string userId, string originalReferenceId, string refundReferenceId, string description, CancellationToken ct);
    Task<AiPackageCreditSnapshot> AdjustAsync(string userId, AiPackageCreditAdjustmentRequest request, string adminId, CancellationToken ct);
    Task<AiPackageCreditSnapshot> RecordExamOutcomeAsync(string userId, LearnerExamOutcomeRequest request, string adminId, string adminName, CancellationToken ct);
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
    IReadOnlyList<AiPackageCreditTransactionDto> Transactions);

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
            ReferenceId = quoteId is null ? $"stripe:{stripeSessionId}" : $"quote:{quoteId}:{addOn.Code}",
            Description = $"{addOn.Name} purchased",
            ExpiresAt = newExpiry,
            CreatedAt = now
        });

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await ProjectSnapshotAsync(userId, 20, ct);
    }

    public async Task<AiPackageDebitResult> DeductGradingCreditAsync(string userId, string subtest, string referenceId, CancellationToken ct)
    {
        var normalized = NormalizeSubtest(subtest);
        if (normalized is not ("writing" or "speaking"))
        {
            return new(false, "unsupported_subtest", "Only Writing and Speaking consume AI grading credits.", null);
        }

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var account = await GetOrCreateAccountAsync(userId, ct);
        await ExpireIfNeededAsync(account, DateTimeOffset.UtcNow, ct);
        if (await TransactionExistsAsync(referenceId, AiPackageCreditReason.GradingDeduct, ct))
        {
            return new(false, "already_debited", "This grading job has already consumed a credit.", referenceId);
        }

        if (await ShouldBypassGradingDebitForLegacyAccountAsync(account, ct))
        {
            return new(true, null, null, referenceId);
        }

        if (account.ExpiredBecausePassed || (account.ExpiresAt is not null && account.ExpiresAt <= DateTimeOffset.UtcNow))
        {
            return new(false, "ai_package_expired", "Your AI package has expired. Purchase a package to continue.", null);
        }

        var writingDelta = 0;
        var speakingDelta = 0;
        var flexibleDelta = 0;
        if (normalized == "writing" && account.WritingOnlyCredits > 0)
        {
            account.WritingOnlyCredits--;
            writingDelta = -1;
        }
        else if (normalized == "speaking" && account.SpeakingOnlyCredits > 0)
        {
            account.SpeakingOnlyCredits--;
            speakingDelta = -1;
        }
        else if (account.FlexibleCredits > 0)
        {
            account.FlexibleCredits--;
            flexibleDelta = -1;
        }
        else
        {
            return new(false, "no_ai_package_credits", NoCreditsMessage, null);
        }

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
            Description = $"{normalized} AI grading credit deducted",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new(true, null, null, referenceId);
    }

    public async Task<AiPackageDebitResult> CheckGradingCreditAsync(string userId, string subtest, CancellationToken ct)
    {
        var normalized = NormalizeSubtest(subtest);
        if (normalized is not ("writing" or "speaking"))
        {
            return new(false, "unsupported_subtest", "Only Writing and Speaking consume AI grading credits.", null);
        }

        var account = await GetOrCreateAccountAsync(userId, ct);
        await ExpireIfNeededAsync(account, DateTimeOffset.UtcNow, ct);
        await db.SaveChangesAsync(ct);

        if (await ShouldBypassGradingDebitForLegacyAccountAsync(account, ct))
        {
            return new(true, null, null, null);
        }

        if (account.ExpiredBecausePassed || (account.ExpiresAt is not null && account.ExpiresAt <= DateTimeOffset.UtcNow))
        {
            return new(false, "ai_package_expired", "Your AI package has expired. Purchase a package to continue.", null);
        }

        var hasCredit = (normalized == "writing" && account.WritingOnlyCredits > 0)
            || (normalized == "speaking" && account.SpeakingOnlyCredits > 0)
            || account.FlexibleCredits > 0;

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
        if (normalized == "listening" && account.ListeningTestsRemaining is not null)
        {
            if (account.ListeningTestsRemaining <= 0) return new(false, "no_listening_tests", "You have no Listening practice tests remaining. Purchase a package to continue.", null);
            account.ListeningTestsRemaining--;
            listeningDelta = -1;
        }
        if (normalized == "reading" && account.ReadingTestsRemaining is not null)
        {
            if (account.ReadingTestsRemaining <= 0) return new(false, "no_reading_tests", "You have no Reading practice tests remaining. Purchase a package to continue.", null);
            account.ReadingTestsRemaining--;
            readingDelta = -1;
        }

        account.UpdatedAt = DateTimeOffset.UtcNow;
        AddTransaction(account, new AiPackageCreditTransaction
        {
            Id = NewId("aipkg-tx"),
            PackageType = normalized,
            ListeningTestsDelta = listeningDelta,
            ReadingTestsDelta = readingDelta,
            Reason = AiPackageCreditReason.ObjectivePracticeDeduct,
            ReferenceId = referenceId,
            Description = $"{normalized} deterministic practice allowance used",
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

    private async Task<bool> TransactionExistsAsync(string referenceId, AiPackageCreditReason reason, CancellationToken ct)
        => await db.AiPackageCreditTransactions.AsNoTracking()
            .AnyAsync(row => row.ReferenceId == referenceId && row.Reason == reason, ct);

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
                             && (row.FlexibleCreditsDelta > 0
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
            transactions);
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
            var flexible = ReadInt(root, "flexible_credits") ?? (packageType == "full" ? addOn.GrantCredits : 0);
            var writing = ReadInt(root, "writing_only_credits") ?? (packageType == "writing" ? addOn.GrantCredits : 0);
            var speaking = ReadInt(root, "speaking_only_credits") ?? (packageType == "speaking" ? addOn.GrantCredits : 0);
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
