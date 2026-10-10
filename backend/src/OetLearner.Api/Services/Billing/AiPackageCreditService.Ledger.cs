using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

public sealed partial class AiPackageCreditService
{
    /// <summary>
    /// Get-or-create is safe to call more than once per unit of work. Callers such as
    /// <see cref="ReverseGrantsAsync"/> (invoked twice back-to-back by
    /// UserAccessAllocationService.RemovePackageAsync, once per source reference) may each
    /// take the "no matching purchase, nothing to reverse" early-return path inside
    /// <see cref="ReverseOneGrantAsync"/> — which never calls SaveChangesAsync. A second
    /// <c>FirstOrDefaultAsync</c> against the database would not see the first call's
    /// still-pending Added entity and would track a SECOND account row for the same user;
    /// flushing both at the next SaveChangesAsync (e.g. RemovePackageAsync's own, after both
    /// reversal calls return) then violates the unique IX_AiPackageCreditAccounts_UserId
    /// index. Check the change tracker's local set FIRST — it includes not-yet-saved Added
    /// entities — before ever issuing a query or creating a new row.
    /// </summary>
    private async Task<AiPackageCreditAccount> GetOrCreateAccountAsync(string userId, CancellationToken ct)
    {
        var account = db.AiPackageCreditAccounts.Local.FirstOrDefault(row => row.UserId == userId)
            ?? await db.AiPackageCreditAccounts.FirstOrDefaultAsync(row => row.UserId == userId, ct);
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

        await EnsureLotsLoadedAsync(account, ct);
        var expiredLots = AccountLots(account)
            .Where(lot => !lot.Expired && lot.ExpiresAt is { } expires && expires <= now)
            .ToList();

        var accountExpired = account.ExpiresAt is { } accountExpiry && accountExpiry <= now;
        if (expiredLots.Count == 0 && accountExpired)
        {
            expiredLots = AccountLots(account).Where(lot => !lot.Expired).ToList();
        }

        if (expiredLots.Count == 0)
        {
            if (!accountExpired)
            {
                RebuildAccountFromLots(account);
                RepairNullSentinels(account);
            }

            return;
        }

        var preservedAccountExpiry = accountExpired ? account.ExpiresAt : null;

        foreach (var lot in expiredLots)
        {
            var referenceId = $"expiry:{lot.Id}:{lot.ExpiresAt:O}";
            if (await db.AiPackageCreditTransactions.AsNoTracking()
                .AnyAsync(row => row.UserId == account.UserId && row.Reason == AiPackageCreditReason.Expiry && row.ReferenceId == referenceId, ct))
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
                SourceReferenceId = lot.SourceReferenceId,
                Description = "AI package credits expired.",
                ValidFrom = lot.ValidFrom,
                ExpiresAt = lot.ExpiresAt,
                CreatedAt = now
            });
        }

        RebuildAccountFromLots(account);
        RepairNullSentinels(account);
        // Keep the lapsed date so gates answer "expired" - but only once nothing that
        // holds value is left (live, or scheduled and not yet started). The Max() in
        // RebuildAccountFromLots skips an open-ended lot (ExpiresAt == null), so
        // restoring the stale date unconditionally made every gate refuse with
        // ai_package_expired while that lot still held credits, and the next call
        // then expired the lot too.
        if (preservedAccountExpiry is { } kept
            && (account.ExpiresAt is null || account.ExpiresAt < kept)
            && !AccountLots(account).Any(lot => !lot.Expired && LotRetainsValue(lot)))
        {
            account.ExpiresAt = kept;
        }
    }

    private void AddTransaction(AiPackageCreditAccount account, AiPackageCreditTransaction row)
    {
        row.UserId = account.UserId;
        row.AccountId = account.Id;
        // Every ledger write routes through here, so the varchar(64) keys are
        // fitted in ONE place (Writing Addendum Rev8 §16, P0 task-load
        // failure): JobId used to receive the full Writing start reference
        // "writing-v2:{userId}:{scenarioId:D}:{n}" (86-90 chars for real
        // learner ids) → Postgres 22001 → generic 500 on every finite-balance
        // "Practice this". EF InMemory ignores MaxLength, so no test caught it.
        // ReferenceId (varchar 128) stays the full idempotency key — only the
        // 64-char columns are fitted, deterministically, so retries still map
        // to the same value.
        row.JobId = FitColumn(row.JobId, 64);
        row.PackageId = FitColumn(row.PackageId, 64);
        row.CreatedByAdminId = FitColumn(row.CreatedByAdminId, 64);
        db.AiPackageCreditTransactions.Add(row);
    }

    /// <summary>
    /// Deterministically fits <paramref name="value"/> to a column of
    /// <paramref name="maxLength"/> chars: unchanged when it fits, otherwise a
    /// readable prefix plus a 128-bit SHA-256 suffix.
    /// <see cref="AddonGrantProcessor.FitDatabaseKey"/> cannot be used for
    /// 64-char columns — its "-" + 64-hex suffix alone is 65 chars.
    /// </summary>
    private static string? FitColumn(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))[..32]
            .ToLowerInvariant();
        return value[..(maxLength - hash.Length - 1)] + "-" + hash;
    }

    private async Task<bool> ReverseOneGrantAsync(
        string userId, string sourceReferenceId, bool clearLapsedLots, CancellationToken ct)
    {
        return await InLedgerTransactionAsync<bool>(async () =>
        {
            var account = await GetOrCreateAccountAsync(userId, ct);
            await EnsureLotsLoadedAsync(account, ct);
            var purchases = await db.AiPackageCreditTransactions.AsNoTracking()
                .Where(row => row.UserId == userId
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
                SourceMatches(row, sourceReferenceId)
                && !reversedReferences.Contains(row.ReferenceId!)
                && !reversedReferences.Contains(AddonGrantProcessor.FitDatabaseKey($"grant-reverse:{row.ReferenceId}")));
            if (purchase is null)
            {
                return false;
            }

            await ExpireIfNeededAsync(account, DateTimeOffset.UtcNow, ct);
            Func<AiPackageCreditLot, bool> matchesPurchase = lot =>
                string.Equals(lot.SourceReferenceId, purchase.ReferenceId, StringComparison.OrdinalIgnoreCase)
                || (purchase.SourceReferenceId is not null
                    && string.Equals(lot.SourceReferenceId, purchase.SourceReferenceId, StringComparison.OrdinalIgnoreCase));
            var matchingLots = AccountLots(account)
                .Where(lot => !lot.Expired && LotHasRemaining(lot) && matchesPurchase(lot))
                .ToList();

            // Expired lots of this purchase that still carry value: parked ones come back on
            // unpark, lapsed ones (past their own validity end) never do.
            var lapsedCutoff = DateTimeOffset.UtcNow;
            var expiredHeldLots = AccountLots(account)
                .Where(lot => lot.Expired && LotRetainsValue(lot) && matchesPurchase(lot))
                .ToList();
            var lapsedLots = expiredHeldLots
                .Where(lot => lot.ExpiresAt is { } lapsedAt && lapsedAt <= lapsedCutoff)
                .ToList();

            // Expired lots that still hold value once this step is done. An explicit
            // revocation clears the lapsed ones below, so only parked lots are left; the
            // orphan sweep (clearLapsedLots == false) leaves every one of them in place.
            var heldAfterThisStep = clearLapsedLots
                ? expiredHeldLots.Count - lapsedLots.Count
                : expiredHeldLots.Count;

            if (matchingLots.Count == 0 && heldAfterThisStep > 0)
            {
                // Parked lots (suspended/cancelled-but-restorable sources) still
                // carry value behind the Expired flag. Do NOT mark the purchase
                // reversed: unpark (restore/reactivate/extend) must be able to
                // revive them, and the later removal must still find the purchase
                // unmarked so it reverses for real. Lots already contribute nothing
                // while flagged, so there is nothing to reverse right now.
                // The orphan sweep also lands here for a purchase whose only held
                // lots are lapsed: it does not clear them, so recording the reversal
                // would let an admin "extend" revive them (UpdateGrantWindowAsync
                // revives any lot that still has value) behind a purchase already
                // marked reversed, and a later removal/refund would never zero them.
                // An explicit revocation clears lapsed lots first (heldAfterThisStep
                // excludes them), so a refund of an expired purchase still records its
                // reversal and ReverseGrantsAsync reaches any later live purchase.
                return false;
            }

            // A lapsed lot of a purchase being revoked (refund, removal) must be dead too:
            // it keeps its balance behind Expired, and an admin date edit
            // (UpdateGrantWindowAsync revives any lot that still has value) would hand
            // the refunded purchase's credits back. Its loss is already booked by its
            // Expiry row, so it is cleared here but not added to the reversal deltas below.
            // The orphan sweep deliberately keeps them (and, with nothing live to
            // reverse, keeps the purchase unmarked above): "expire now" followed by
            // "extend" on an Expired package is a supported date override that revives
            // its unused credits, and the later removal must still find the purchase.
            if (clearLapsedLots)
            {
                foreach (var lapsed in lapsedLots)
                {
                    ClearLotValue(lapsed);
                }
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
                shared += -lot.SharedCredits;
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
                ClearLotValue(lot);
                lot.Expired = true;
                lot.ExpiredAt = DateTimeOffset.UtcNow;
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
                SourceReferenceId = purchase.SourceReferenceId ?? sourceReferenceId,
                Description = $"{purchase.PackageId} grant reversed",
                ValidFrom = purchase.ValidFrom,
                ExpiresAt = purchase.ExpiresAt,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    private static void ClearLotValue(AiPackageCreditLot lot)
    {
        lot.SharedCredits = 0;
        lot.FlexibleCredits = 0;
        lot.WritingOnlyCredits = 0;
        lot.SpeakingOnlyCredits = 0;
        lot.MockExamsRemaining = 0;
        // 0, not null: null only means "unlimited" while the Unlimited flag is
        // set, and the flags are cleared right below. A null left here made a
        // reversed lot read as still holding value (LotHasRemaining).
        lot.ListeningTestsRemaining = 0;
        lot.ReadingTestsRemaining = 0;
        lot.UnlimitedGrading = false;
        lot.UnlimitedListening = false;
        lot.UnlimitedReading = false;
    }

    private async Task ReverseOrphanedGrantsAsync(string userId, DateTimeOffset now, CancellationToken ct)
    {
        var ownedSources = await ComputeOwnedSourceReferencesAsync(userId, now, ct);

        var purchaseSources = await db.AiPackageCreditTransactions.AsNoTracking()
            .Where(row => row.UserId == userId
                          && row.Reason == AiPackageCreditReason.Purchase
                          && row.SourceReferenceId != null)
            .Select(row => row.SourceReferenceId!)
            .Distinct()
            .ToListAsync(ct);
        foreach (var sourceReference in purchaseSources)
        {
            if (ownedSources.Contains(sourceReference))
            {
                continue;
            }

            await ReverseGrantsCoreAsync(userId, sourceReference, clearLapsedLots: false, ct);
        }
    }

    /// <summary>
    /// Every grant source that still owns its entitlement: plan/course-gift
    /// sources of subscriptions that have not reached a terminal state, plus
    /// add-on sources whose subscription AND item are both currently live.
    /// Suspended/Frozen/Paused subs keep ownership (their lots are parked, not
    /// reversed) while Cancelled/Expired subs own nothing. Mirrors the
    /// eligibility gate used for the unlimited/allowance derivation so the two
    /// can never disagree about whether a source is active.
    /// </summary>
    private async Task<HashSet<string>> ComputeOwnedSourceReferencesAsync(
        string userId, DateTimeOffset now, CancellationToken ct)
    {
        var ownedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ownedSubscriptions = await db.Subscriptions.AsNoTracking()
            .Where(subscription => subscription.UserId == userId
                && subscription.Status != SubscriptionStatus.Cancelled
                && subscription.Status != SubscriptionStatus.Expired)
            .ToListAsync(ct);
        foreach (var subscription in ownedSubscriptions)
        {
            if (subscription.PlanId != Subscription.StandaloneAddonPlanId)
            {
                foreach (var source in AiPackageCreditSources.PlanKeys(subscription.Id, subscription.PlanId))
                {
                    ownedSources.Add(source);
                }
            }
        }

        var ownedItems = await (
            from item in db.SubscriptionItems.AsNoTracking()
            join subscription in db.Subscriptions.AsNoTracking()
                on item.SubscriptionId equals subscription.Id
            where subscription.UserId == userId
                  && (subscription.Status == SubscriptionStatus.Active
                      || subscription.Status == SubscriptionStatus.Trial
                      || subscription.Status == SubscriptionStatus.FreezeRequested)
                  && subscription.StartedAt <= now
                  && (subscription.ExpiresAt == null || subscription.ExpiresAt > now)
                  && item.Status == SubscriptionItemStatus.Active
                  && item.StartsAt <= now
                  && (item.EndsAt == null || item.EndsAt > now)
            select new { item.SubscriptionId, item.ItemCode })
            .ToListAsync(ct);
        foreach (var item in ownedItems)
        {
            ownedSources.Add(AiPackageCreditSources.Addon(item.SubscriptionId, item.ItemCode));
        }

        return ownedSources;
    }

    /// <summary>
    /// Currently-eligible ai_package grants with their purchased quantities.
    /// Single source of truth for "which objective allowances are active".
    /// </summary>
    private async Task<IReadOnlyList<EligibleAiPackageGrant>> LoadEligibleAiPackageGrantsAsync(
        string userId, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await (
            from item in db.SubscriptionItems.AsNoTracking()
            join subscription in db.Subscriptions.AsNoTracking()
                on item.SubscriptionId equals subscription.Id
            join addOn in db.BillingAddOns.AsNoTracking()
                on item.ItemCode equals addOn.Code
            where subscription.UserId == userId
                  && (subscription.Status == SubscriptionStatus.Active
                      || subscription.Status == SubscriptionStatus.Trial
                      || subscription.Status == SubscriptionStatus.FreezeRequested)
                  && subscription.StartedAt <= now
                  && (subscription.ExpiresAt == null || subscription.ExpiresAt > now)
                  && item.Status == SubscriptionItemStatus.Active
                  && item.StartsAt <= now
                  && (item.EndsAt == null || item.EndsAt > now)
                  && addOn.AddonKind == "ai_package"
            select new { item.SubscriptionId, item.ItemCode, item.Quantity, item.EndsAt, addOn.GrantEntitlementsJson }
        ).ToListAsync(ct);
        return rows
            .GroupBy(row => (row.SubscriptionId, row.ItemCode, row.GrantEntitlementsJson, row.EndsAt))
            .Select(group => new EligibleAiPackageGrant(
                group.Key.GrantEntitlementsJson,
                group.Sum(row => Math.Max(1, row.Quantity)),
                group.Key.SubscriptionId,
                group.Key.ItemCode,
                group.Key.EndsAt))
            .ToList();
    }

    private sealed record EligibleAiPackageGrant(
        string? Json,
        int Quantity,
        string SubscriptionId,
        string ItemCode,
        DateTimeOffset? EndsAt);

    /// <summary>
    /// Heal partial-failure grants: an eligible unlimited item with no purchase
    /// ledger row at all never granted its lot (the item row and the lot are
    /// written by separate steps). Materialize the missing unlimited lot so
    /// the active source actually contributes — unlimited has no consumption
    /// semantics, so this cannot over-grant. Skipped whenever a purchase row
    /// exists (the ledger then owns the outcome: live, consumed or reversed).
    /// Finite allowances are intentionally not materialized: consumption makes
    /// "missing row" ambiguous there.
    /// </summary>
    private async Task MaterializeMissingUnlimitedLotsAsync(
        AiPackageCreditAccount account,
        IReadOnlyList<EligibleAiPackageGrant> activeGrants,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var unlimitedGrants = new List<(EligibleAiPackageGrant Grant, AiPackageGrant Parsed)>();
        foreach (var grant in activeGrants)
        {
            var parsed = AiPackageGrant.FromAddOn(new BillingAddOn
            {
                Code = "pkg_recalc",
                AddonKind = "ai_package",
                GrantEntitlementsJson = grant.Json,
            }, grant.Quantity);
            if (parsed.ListeningTests is null || parsed.ReadingTests is null)
            {
                unlimitedGrants.Add((grant, parsed));
            }
        }

        if (unlimitedGrants.Count == 0)
        {
            return;
        }

        var purchaseSources = await db.AiPackageCreditTransactions.AsNoTracking()
            .Where(row => row.UserId == account.UserId
                          && row.Reason == AiPackageCreditReason.Purchase
                          && row.SourceReferenceId != null)
            .Select(row => row.SourceReferenceId!)
            .Distinct()
            .ToListAsync(ct);
        var purchaseSourceSet = purchaseSources.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var liveLots = LiveLots(account);
        var materialized = 0;

        foreach (var (grant, parsed) in unlimitedGrants)
        {
            var source = AiPackageCreditSources.Addon(grant.SubscriptionId, grant.ItemCode);
            if (purchaseSourceSet.Contains(source))
            {
                continue;
            }

            var needsListening = parsed.ListeningTests is null
                && !liveLots.Any(lot => lot.UnlimitedListening && LotSourceMatches(lot, source));
            var needsReading = parsed.ReadingTests is null
                && !liveLots.Any(lot => lot.UnlimitedReading && LotSourceMatches(lot, source));
            if (!needsListening && !needsReading)
            {
                continue;
            }

            var lot = new AiPackageCreditLot
            {
                Id = NewId("aipkg-lot"),
                PackageId = grant.ItemCode,
                PackageType = parsed.PackageType,
                ListeningTestsRemaining = parsed.ListeningTests is null ? null : 0,
                ReadingTestsRemaining = parsed.ReadingTests is null ? null : 0,
                UnlimitedListening = parsed.ListeningTests is null,
                UnlimitedReading = parsed.ReadingTests is null,
                ValidFrom = now,
                ExpiresAt = grant.EndsAt,
                SourceReferenceId = source,
                CreatedAt = now,
            };
            AddLot(account, lot);
            liveLots.Add(lot);
            AddTransaction(account, new AiPackageCreditTransaction
            {
                Id = NewId("aipkg-tx"),
                PackageId = grant.ItemCode,
                PackageType = parsed.PackageType,
                Reason = AiPackageCreditReason.Purchase,
                ReferenceId = AddonGrantProcessor.FitDatabaseKey($"heal:{grant.SubscriptionId}:{grant.ItemCode}"),
                SourceReferenceId = source,
                Description = $"{grant.ItemCode} reconciled missing unlimited grant",
                ValidFrom = now,
                ExpiresAt = grant.EndsAt,
                CreatedAt = now,
            });
            purchaseSourceSet.Add(source);
            materialized++;
        }

        if (materialized == 0)
        {
            return;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "AiPackageCreditService materialized {Materialized} missing unlimited lots for learner {UserId}.",
            materialized, account.UserId);
    }

    private static bool LotSourceMatches(AiPackageCreditLot lot, string source)
        => string.Equals(lot.SourceReferenceId, source, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Legacy/ghost repair: expire live unlimited lots that have no eligible
    /// unlimited source. This covers lots with missing source attribution
    /// (pre-attribution data, persisted synthetic legacy lots) that
    /// <see cref="ReverseOrphanedGrantsAsync"/> cannot match to a purchase.
    /// Finite manual (admin-adjust) lots are never unlimited, so they are
    /// inherently safe from this pass.
    /// </summary>
    private async Task ExpireOrphanedUnlimitedLotsAsync(
        AiPackageCreditAccount account,
        bool listeningUnlimited,
        bool readingUnlimited,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (listeningUnlimited && readingUnlimited)
        {
            return;
        }

        var ownedSources = await ComputeOwnedSourceReferencesAsync(account.UserId, now, ct);
        var expired = 0;
        foreach (var lot in AccountLots(account).Where(lot => !lot.Expired))
        {
            var needsListeningRepair = !listeningUnlimited && lot.UnlimitedListening;
            var needsReadingRepair = !readingUnlimited && lot.UnlimitedReading;
            if (!needsListeningRepair && !needsReadingRepair)
            {
                continue;
            }

            if (lot.SourceReferenceId is not null && ownedSources.Contains(lot.SourceReferenceId))
            {
                continue;
            }

            lot.UnlimitedGrading = false;
            lot.UnlimitedListening = false;
            lot.UnlimitedReading = false;
            if (lot.ListeningTestsRemaining is null) lot.ListeningTestsRemaining = 0;
            if (lot.ReadingTestsRemaining is null) lot.ReadingTestsRemaining = 0;
            if (!LotHasRemaining(lot))
            {
                lot.Expired = true;
                lot.ExpiredAt = now;
            }
            expired++;
        }

        if (expired > 0)
        {
            logger.LogWarning(
                "AiPackageCreditService expired {Expired} orphaned unlimited lots for learner {UserId}.",
                expired, account.UserId);
        }
    }

    /// <summary>
    /// All lot/transaction reference ids through which lots granted from
    /// <paramref name="subscriptionId"/> can be addressed, whatever grant path
    /// created them (admin add-on, checkout stripe/quote, course gift).
    /// </summary>
    private async Task<HashSet<string>> CollectSubscriptionLotReferencesAsync(
        string userId, string subscriptionId, CancellationToken ct)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Canonical prefixes cover lots/transactions written with the exact
            // source key even when no purchase transaction exists (course gift
            // lots carry the admin-package reference directly).
            AddonGrantProcessor.FitDatabaseKey($"admin-package:{subscriptionId}:"),
            AddonGrantProcessor.FitDatabaseKey($"plan:{subscriptionId}:"),
            AddonGrantProcessor.FitDatabaseKey($"addon:{subscriptionId}:"),
        };

        var rows = await db.AiPackageCreditTransactions.AsNoTracking()
            .Where(row => row.UserId == userId
                && (row.Reason == AiPackageCreditReason.Purchase || row.Reason == AiPackageCreditReason.AdminAdjustment))
            .Select(row => new { row.ReferenceId, row.SourceReferenceId })
            .ToListAsync(ct);
        foreach (var row in rows)
        {
            if (SourceBelongsToSubscription(row.SourceReferenceId, row.ReferenceId, subscriptionId))
            {
                if (row.ReferenceId is not null) references.Add(row.ReferenceId);
                if (row.SourceReferenceId is not null) references.Add(row.SourceReferenceId);
            }
        }

        return references;
    }

    private static bool LotSourceBelongsToSubscription(string sourceReferenceId, string subscriptionId)
        => sourceReferenceId.StartsWith($"admin-package:{subscriptionId}:", StringComparison.OrdinalIgnoreCase)
            || sourceReferenceId.StartsWith($"plan:{subscriptionId}:", StringComparison.OrdinalIgnoreCase)
            || sourceReferenceId.StartsWith($"addon:{subscriptionId}:", StringComparison.OrdinalIgnoreCase);

    private static bool SourceBelongsToSubscription(
        string? sourceReferenceId, string? referenceId, string subscriptionId)
    {
        if (!string.IsNullOrWhiteSpace(sourceReferenceId)
            && (sourceReferenceId.StartsWith($"admin-package:{subscriptionId}:", StringComparison.OrdinalIgnoreCase)
                || sourceReferenceId.StartsWith($"plan:{subscriptionId}:", StringComparison.OrdinalIgnoreCase)
                || sourceReferenceId.StartsWith($"addon:{subscriptionId}:", StringComparison.OrdinalIgnoreCase)
                || sourceReferenceId.Contains(subscriptionId, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(referenceId)
            && referenceId.Contains(subscriptionId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A lot that still carries value: any unlimited flag or any positive
    /// finite balance. Reversed/consumed lots fail this; parked lots pass it.
    /// </summary>
    private static bool LotRetainsValue(AiPackageCreditLot lot)
        => lot.UnlimitedGrading
            || lot.UnlimitedListening
            || lot.UnlimitedReading
            || lot.SharedCredits > 0
            || lot.FlexibleCredits > 0
            || lot.WritingOnlyCredits > 0
            || lot.SpeakingOnlyCredits > 0
            || lot.MockExamsRemaining > 0
            || (lot.ListeningTestsRemaining ?? 0) > 0
            || (lot.ReadingTestsRemaining ?? 0) > 0;

    /// <summary>Null-pool repair: a null Listening/Reading pool with no live
    /// unlimited lot is a ghost sentinel — drop it to zero.</summary>
    private void RepairNullSentinels(AiPackageCreditAccount account)
    {
        if (account.ListeningTestsRemaining is null
            && !LiveLots(account).Any(lot => lot.UnlimitedListening))
        {
            account.ListeningTestsRemaining = 0;
        }
        if (account.ReadingTestsRemaining is null
            && !LiveLots(account).Any(lot => lot.UnlimitedReading))
        {
            account.ReadingTestsRemaining = 0;
        }
    }

    private async Task<bool> TransactionExistsAsync(string userId, string referenceId, AiPackageCreditReason reason, CancellationToken ct)
        => await db.AiPackageCreditTransactions.AsNoTracking()
            .AnyAsync(row => row.UserId == userId && row.ReferenceId == referenceId && row.Reason == reason, ct);

    /// <summary>
    /// A plan's course gift is once per order, but checkout ("plan:{quote}:{code}"),
    /// manual approval ("manual:{request}:{code}") and Mark fulfilled
    /// ("plan:{subscription}:{code}") each build their own reference while sharing one
    /// <see cref="AiPackageCreditSources.Plan"/> source, so reference dedupe alone let a
    /// manually delivered order gift twice. Mark fulfilled is the only path whose
    /// reference IS the source key, so the same order is delivered twice exactly when
    /// one of the pair is the source-key reference and the other is not. Two checkouts or
    /// two manual approvals are separate orders reusing the subscription (a quote-less
    /// re-purchase resolves to the same one) and each keeps its gift. Only plan sources
    /// are guarded: add-on gifts ("addon:...") legitimately repeat under one source.
    /// </summary>
    private async Task<bool> PlanGiftAlreadyGrantedAsync(
        string userId, string source, string referenceId, CancellationToken ct)
    {
        if (!source.StartsWith("plan:", StringComparison.Ordinal))
        {
            return false;
        }

        var incomingIsSourceKey = string.Equals(referenceId, source, StringComparison.Ordinal);
        var grantedReferences = await db.AiPackageCreditTransactions.AsNoTracking()
            .Where(row => row.UserId == userId
                          && row.Reason == AiPackageCreditReason.Purchase
                          && row.SourceReferenceId == source
                          && row.ReferenceId != null)
            .Select(row => row.ReferenceId!)
            .ToListAsync(ct);
        foreach (var granted in grantedReferences)
        {
            // Neither or both being the source key is not the same-order pair.
            if (string.Equals(granted, source, StringComparison.Ordinal) == incomingIsSourceKey)
            {
                continue;
            }

            // Same reversal key ReverseOneGrantAsync writes: a refunded gift no longer counts.
            if (!await TransactionExistsAsync(
                    userId,
                    AddonGrantProcessor.FitDatabaseKey($"grant-reverse:{granted}"),
                    AiPackageCreditReason.GrantReversed,
                    ct))
            {
                return true;
            }
        }

        return false;
    }

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
                  && subscription.StartedAt <= now
                  && (subscription.ExpiresAt == null || subscription.ExpiresAt > now)
                  && item.ItemCode == "pkg_oet_mastery"
                  && item.Status == SubscriptionItemStatus.Active
                  && item.StartsAt <= now
            select item.EndsAt).ToListAsync(ct);

        // Evaluate the date window in memory. EF InMemory does not reliably
        // translate `EndsAt == null || EndsAt > now`, and standalone Mastery
        // grants can persist a null EndsAt when DurationDays was 0.
        //
        // Investigated for the Writing Rule Enforcement Addendum Rev5 (10 Sep
        // 2026, §12) credit contradiction and deliberately NOT changed to
        // also trust AiPackageCreditLot.UnlimitedGrading directly: every real
        // unlimited_grading grant in the catalog (see
        // Data/Migrations/20260820090000_AlignWebsiteCoursePackageDescriptions.cs
        // and .../20260905100000_AlignAiPackageSkillEntitlements.cs) is
        // package_type "full" (Mastery/full-course), and
        // OetMastery_BypassesWritingAndSpeakingOnlyWhilePurchaseItemIsActive
        // (AiPackageCreditServiceTests.cs) locks in that cancelling the
        // SubscriptionItem must immediately revoke access even while that
        // lot's own fixed-duration grant is still technically unexpired —
        // trusting the lot directly would silently re-open that revocation
        // hole. The real gap is more likely a desync between the AI-package
        // grant (GrantPackageAsync, fired on "ai_package" purchases) and the
        // Subscription/SubscriptionItem rows a full-course purchase should
        // also provision — that needs tracing through the actual purchase/
        // webhook pipeline against a real affected account, not a rule
        // change here.
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
            .AnyAsync(row => row.UserId == account.UserId
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
            .AnyAsync(row => row.UserId == account.UserId
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
            .AnyAsync(row => row.UserId == account.UserId && row.MockExamsDelta > 0, ct);
    }
}
