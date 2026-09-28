using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

public sealed partial class AiPackageCreditService(LearnerDbContext db, ILogger<AiPackageCreditService> logger) : IAiPackageCreditService
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

    public async Task<AiPackageCreditSnapshot> GrantPackageAsync(
        string userId,
        BillingAddOn addOn,
        int quantity,
        string stripeSessionId,
        string? quoteId,
        CancellationToken ct,
        string? sourceReferenceId = null,
        DateTimeOffset? validFrom = null)
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
        var grantValidFrom = validFrom ?? now;
        DateTimeOffset? newExpiry = addOn.DurationDays > 0
            ? grantValidFrom.AddDays(addOn.DurationDays)
            : null;
        var referenceId = AddonGrantProcessor.FitDatabaseKey(
            quoteId is null ? $"stripe:{stripeSessionId}" : $"quote:{quoteId}:{addOn.Code}");
        sourceReferenceId = AddonGrantProcessor.FitDatabaseKey(sourceReferenceId ?? referenceId);

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
            ValidFrom = grantValidFrom,
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
            SourceReferenceId = sourceReferenceId,
            Description = $"{addOn.Name} purchased",
            ValidFrom = grantValidFrom,
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
        CancellationToken ct,
        string? sourceReferenceId = null,
        DateTimeOffset? validFrom = null)
    {
        if (credits <= 0 || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(referenceId))
        {
            return false;
        }

        referenceId = AddonGrantProcessor.FitDatabaseKey(referenceId);
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var account = await GetOrCreateAccountAsync(userId, ct);
        var now = DateTimeOffset.UtcNow;
        var giftValidFrom = validFrom ?? now;
        sourceReferenceId = AddonGrantProcessor.FitDatabaseKey(sourceReferenceId ?? referenceId);
        await ExpireIfNeededAsync(account, now, ct);
        if (await TransactionExistsAsync(userId, referenceId, AiPackageCreditReason.Purchase, ct))
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
            ValidFrom = giftValidFrom,
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
            SourceReferenceId = sourceReferenceId,
            Description = $"{planName} gifted Shared AI practice credits",
            ValidFrom = giftValidFrom,
            ExpiresAt = expiresAt,
            CreatedAt = now
        });

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return true;
    }

    public async Task<int> ReverseGrantsAsync(string userId, string sourceReferenceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sourceReferenceId))
        {
            return 0;
        }

        var reversed = 0;
        while (await ReverseOneGrantAsync(userId, sourceReferenceId, ct))
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
        // Quantity-aware parse of every currently-eligible ai_package grant. A
        // grant only counts while its subscription AND its item are both live
        // (Active/Trial/FreezeRequested + within StartedAt/EndsAt window).
        var activeGrants = await LoadEligibleAiPackageGrantsAsync(userId, now, ct);

        var listeningUnlimited = false;
        var readingUnlimited = false;
        foreach (var active in activeGrants)
        {
            var grant = AiPackageGrant.FromAddOn(new BillingAddOn
            {
                Code = "pkg_recalc",
                AddonKind = "ai_package",
                GrantEntitlementsJson = active.Json,
            }, active.Quantity);
            if (grant.ListeningTests is null) listeningUnlimited = true;
            if (grant.ReadingTests is null) readingUnlimited = true;
        }

        account.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await ReverseOrphanedGrantsAsync(userId, now, ct);
        var refreshed = await db.AiPackageCreditAccounts.FirstOrDefaultAsync(row => row.UserId == userId, ct);
        if (refreshed is not null)
        {
            await EnsureLotsLoadedAsync(refreshed, ct);
            await MaterializeMissingUnlimitedLotsAsync(refreshed, activeGrants, now, ct);
            await ExpireOrphanedUnlimitedLotsAsync(refreshed, listeningUnlimited, readingUnlimited, now, ct);
            RebuildAccountFromLots(refreshed);
            // Stuck-sentinel repair: a null pool with no live unlimited lot is a
            // ghost (legacy account or orphaned lot). Finite manual lots are
            // independent sources and are deliberately left untouched — there is
            // intentionally no finite clamp here.
            if (refreshed.ListeningTestsRemaining is null
                && !LiveLots(refreshed).Any(lot => lot.UnlimitedListening))
            {
                refreshed.ListeningTestsRemaining = 0;
            }
            if (refreshed.ReadingTestsRemaining is null
                && !LiveLots(refreshed).Any(lot => lot.UnlimitedReading))
            {
                refreshed.ReadingTestsRemaining = 0;
            }
            refreshed.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task ParkSubscriptionLotsAsync(string userId, string subscriptionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(subscriptionId))
        {
            return;
        }

        var account = await db.AiPackageCreditAccounts.FirstOrDefaultAsync(row => row.UserId == userId, ct);
        if (account is null)
        {
            return;
        }

        await EnsureLotsLoadedAsync(account, ct);
        var now = DateTimeOffset.UtcNow;
        var ownedReferences = await CollectSubscriptionLotReferencesAsync(userId, subscriptionId, ct);
        var parked = 0;
        foreach (var lot in AccountLots(account).Where(lot => !lot.Expired && LotRetainsValue(lot)))
        {
            if (lot.SourceReferenceId is null
                || (!ownedReferences.Contains(lot.SourceReferenceId)
                    && !LotSourceBelongsToSubscription(lot.SourceReferenceId, subscriptionId)))
            {
                continue;
            }

            lot.Expired = true;
            lot.ExpiredAt = now;
            parked++;
        }

        if (parked == 0)
        {
            return;
        }

        RebuildAccountFromLots(account);
        RepairNullSentinels(account);
        account.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "AiPackageCreditService parked {Parked} lots for learner {UserId} on subscription {SubscriptionId}.",
            parked, userId, subscriptionId);
    }

    public async Task UnparkSubscriptionLotsAsync(string userId, string subscriptionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(subscriptionId))
        {
            return;
        }

        var account = await db.AiPackageCreditAccounts.FirstOrDefaultAsync(row => row.UserId == userId, ct);
        if (account is null)
        {
            return;
        }

        await EnsureLotsLoadedAsync(account, ct);
        var now = DateTimeOffset.UtcNow;
        var ownedReferences = await CollectSubscriptionLotReferencesAsync(userId, subscriptionId, ct);
        var revived = 0;
        foreach (var lot in AccountLots(account).Where(lot => lot.Expired))
        {
            if (lot.SourceReferenceId is null
                || (!ownedReferences.Contains(lot.SourceReferenceId)
                    && !LotSourceBelongsToSubscription(lot.SourceReferenceId, subscriptionId)))
            {
                continue;
            }

            // Only parked lots come back: reversed/consumed lots carry no value
            // and lots past their validity end stay expired.
            if (!LotRetainsValue(lot))
            {
                continue;
            }
            if (lot.ExpiresAt is { } endsAt && endsAt <= now)
            {
                continue;
            }

            lot.Expired = false;
            lot.ExpiredAt = null;
            revived++;
        }

        if (revived == 0)
        {
            return;
        }

        RebuildAccountFromLots(account);
        account.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "AiPackageCreditService unparked {Revived} lots for learner {UserId} on subscription {SubscriptionId}.",
            revived, userId, subscriptionId);
    }

    /// <summary>
    /// Admin date override sync: move the valid-until of course-gifted AI credit
    /// lots granted from this exact subscription to the edited package expiry.
    /// Used history is untouched; unrelated lots/packages are never modified.
    /// </summary>
    public async Task UpdateGrantExpiryAsync(string userId, string subscriptionId, DateTimeOffset? expiresAt, CancellationToken ct)
        => await UpdateGrantWindowAsync(userId, subscriptionId, null, expiresAt, ct);

    public async Task UpdateGrantWindowAsync(
        string userId,
        string subscriptionId,
        DateTimeOffset? validFrom,
        DateTimeOffset? expiresAt,
        CancellationToken ct)
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
                && source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && LotHasRemaining(lot))
            .ToList();
        if (linkedLots.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var linkedSources = linkedLots
            .Select(lot => lot.SourceReferenceId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var linkedTransactions = await db.AiPackageCreditTransactions
            .Where(row => row.UserId == userId
                && row.Reason == AiPackageCreditReason.Purchase
                && row.SourceReferenceId != null
                && row.SourceReferenceId.StartsWith(prefix))
            .ToListAsync(ct);
        foreach (var lot in linkedLots)
        {
            if (validFrom is not null)
            {
                lot.ValidFrom = validFrom;
            }
            lot.ExpiresAt = expiresAt;
            var temporallyExpired = expiresAt is { } end && end <= now;
            // A temporal expiry may be extended later. A reversed lot has no
            // remaining balance and is excluded above, so removal/refund cannot revive it.
            lot.Expired = temporallyExpired;
            lot.ExpiredAt = temporallyExpired ? now : null;
        }

        foreach (var transaction in linkedTransactions.Where(row =>
                     row.SourceReferenceId is not null && linkedSources.Contains(row.SourceReferenceId)))
        {
            if (validFrom is not null)
            {
                transaction.ValidFrom = validFrom;
            }
            transaction.ExpiresAt = expiresAt;
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
        if (await TransactionExistsAsync(userId, referenceId, AiPackageCreditReason.GradingDeduct, ct))
        {
            return new(true, "already_debited", "This grading job has already consumed a credit.", referenceId);
        }

        // Unlimited checked before the account-level expiry/package-expired
        // throw (Writing Rule Enforcement Addendum Rev5, 10 Sep 2026, §12.1:
        // "the unlimited entitlement itself is sufficient; a zero balance in
        // another pool must not block the attempt"). As of §12's fix, both
        // Writing "Practice this" surfaces (WritingScenarioEndpoints
        // eligibility, LearnerService.CreateWritingAttemptAsync) reach this
        // only via WritingEntitlementService.AuthorizeStartAsync — never
        // directly — precisely so it stays in lockstep with the reservation
        // service and the dashboard's free-tier fallback.
        if (await HasActiveUnlimitedGradingAsync(userId, now, ct))
        {
            return new(true, null, null, referenceId, BalanceSource: "unlimited");
        }

        if (account.ExpiredBecausePassed || (account.ExpiresAt is not null && account.ExpiresAt <= now))
        {
            return new(false, "ai_package_expired", "Your AI package has expired. Purchase a package to continue.", null);
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
        // FINAL 2026-09-06: one letter/card costs exactly 2 AI credits. If the
        // spend could not fund every requested activity in full (e.g. a lone
        // stranded credit), refuse BEFORE persisting anything so the candidate
        // is never partially charged. No SaveChanges has run yet, so the
        // tracked lot mutations are discarded with the transaction.
        var expectedUnits = quantity * AiGradingCreditCost.CreditsPerWritingOrSpeakingActivity;
        if (spend.CreditsUsed != expectedUnits)
        {
            return new(false, "no_ai_package_credits", NoCreditsMessage, null);
        }

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
            Description = $"{normalized} AI grading credits deducted ({spend.CreditsUsed})",
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

        // Same reorder as DeductGradingCreditAsync above — keep the two in
        // lockstep so a check-only call and the debit it precedes never
        // disagree.
        if (await HasActiveUnlimitedGradingAsync(userId, now, ct))
        {
            return new(true, null, null, null, BalanceSource: "unlimited");
        }

        if (account.ExpiredBecausePassed || (account.ExpiresAt is not null && account.ExpiresAt <= now))
        {
            return new(false, "ai_package_expired", "Your AI package has expired. Purchase a package to continue.", null);
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

        if (await TransactionExistsAsync(userId, referenceId, AiPackageCreditReason.ObjectivePracticeDeduct, ct))
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
            if (HasLiveRealUnlimited(account, "listening"))
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
            if (HasLiveRealUnlimited(account, "reading"))
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
        // A null pool WITHOUT a live, real (non-synthetic) unlimited lot is a ghost
        // sentinel from a deleted source — never an allowance.
        if (normalized == "listening")
            return HasLiveRealUnlimited(account, "listening")
                || (account.ListeningTestsRemaining ?? 0) > 0
                || account.SharedCredits >= AiGradingCreditCost.ListeningExam;

        return HasLiveRealUnlimited(account, "reading")
            || (account.ReadingTestsRemaining ?? 0) > 0
            || account.SharedCredits >= AiGradingCreditCost.ReadingExam;
    }

    public async Task<AiPackageDebitResult> DeductMockAsync(string userId, string referenceId, CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var account = await GetOrCreateAccountAsync(userId, ct);
        await ExpireIfNeededAsync(account, DateTimeOffset.UtcNow, ct);
        if (await TransactionExistsAsync(userId, referenceId, AiPackageCreditReason.MockDeduct, ct))
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
        if (await TransactionExistsAsync(userId, refundReferenceId, AiPackageCreditReason.RefundOnFailure, ct)
            || await TransactionExistsAsync(userId, refundReferenceId, AiPackageCreditReason.MockRefundOnFailure, ct))
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
                 row.ValidFrom,
                 row.ExpiresAt,
                 row.CreatedAt,
                 row.ReferenceId,
                 row.SourceReferenceId))
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
        // An explicit admin set-to-finite must be able to clear a stuck null
        // sentinel left by a deleted source: with no live unlimited lot the
        // null pool is a ghost, never an entitlement.
        RepairNullSentinels(account);

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

    private const string LegacySyntheticPackageId = "legacy";

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
