using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

public sealed partial class AiPackageCreditService
{
    internal const int MaxLedgerAttempts = 4;

    /// <summary>
    /// Runs one ledger unit of work in its own SERIALIZABLE transaction and re-runs the
    /// WHOLE unit on a PostgreSQL serialization failure (40001) or deadlock (40P01) —
    /// production saw concurrent learners get HTTP 500 "likely due to a transient failure"
    /// from task-open debits and admin adjusts because nothing retried. Each attempt
    /// re-reads the ledger from scratch (tracked ledger rows are detached between attempts)
    /// and the per-reference idempotency checks run again inside the new transaction, so a
    /// retry can never double-debit: the failed attempt rolled back completely.
    /// Not retried here: a caller-owned ambient transaction (that caller owns atomicity and
    /// retry) or a context that already had unsaved changes on entry (a rollback could not
    /// restore them faithfully) — both keep the old single-attempt behaviour.
    /// </summary>
    private async Task<T> InLedgerTransactionAsync<T>(Func<Task<T>> unit, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null || db.Database.IsInMemory())
        {
            return await unit();
        }

        var retryable = !db.ChangeTracker.HasChanges();
        var trackedBefore = new HashSet<object>(
            db.ChangeTracker.Entries().Select(entry => entry.Entity), ReferenceEqualityComparer.Instance);
        for (var attempt = 1; ; attempt++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                if (db.Database.IsNpgsql())
                {
                    // SSI takes a RELATION-level predicate lock for every sequential scan, so on
                    // the small ledger tables (where the planner prefers seq scans) any two
                    // learners' debits conflicted. Index scans lock only the rows/pages read.
                    await db.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off", ct);
                }

                var result = await unit();
                await tx.CommitAsync(ct);
                return result;
            }
            catch (Exception ex) when (retryable && attempt < MaxLedgerAttempts && IsSerializationConflict(ex))
            {
                try { await tx.RollbackAsync(CancellationToken.None); }
                catch (Exception rollbackEx) { logger.LogDebug(rollbackEx, "Ledger rollback after serialization conflict failed."); }

                foreach (var entry in db.ChangeTracker.Entries().ToList())
                {
                    if (!trackedBefore.Contains(entry.Entity)
                        || entry.Entity is AiPackageCreditAccount or AiPackageCreditLot or AiPackageCreditTransaction)
                    {
                        entry.State = EntityState.Detached;
                    }
                }

                logger.LogWarning(
                    "AiPackageCreditService ledger serialization conflict (attempt {Attempt}/{Max}); retrying the unit of work.",
                    attempt, MaxLedgerAttempts);
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(25, 100) * attempt), ct);
            }
        }
    }

    internal static bool IsSerializationConflict(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is Npgsql.PostgresException { SqlState: "40001" or "40P01" })
            {
                return true;
            }
        }

        return false;
    }

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
        if (AccountLots(account).Count > 0)
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
            PackageId = LegacySyntheticPackageId,
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
            ValidFrom = DateTimeOffset.UtcNow,
            ExpiresAt = account.ExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private bool HasUnlimitedObjective(AiPackageCreditAccount account, string subtest)
        => subtest == "listening"
            ? LiveLots(account).Any(lot => lot.UnlimitedListening)
            : LiveLots(account).Any(lot => lot.UnlimitedReading);

    /// <summary>
    /// Authorization-grade unlimited check: synthetic <c>"legacy"</c> lots only
    /// mirror pre-lot account balances and carry no grant provenance, so they
    /// can never authorize unlimited practice on their own. A stuck null pool
    /// from a deleted source therefore denies instead of granting.
    /// </summary>
    private bool HasLiveRealUnlimited(AiPackageCreditAccount account, string subtest)
        => LiveLots(account).Any(lot =>
            IsRealLot(lot)
            && (subtest == "listening" ? lot.UnlimitedListening : lot.UnlimitedReading));

    /// <summary>
    /// The one exclusion every "is this unlimited" answer shares - gates and the
    /// snapshot card alike - so the card can never promise what the gate refuses.
    /// </summary>
    private static bool IsRealLot(AiPackageCreditLot lot)
        => !string.Equals(lot.PackageId, LegacySyntheticPackageId, StringComparison.OrdinalIgnoreCase);

    private List<AiPackageCreditLot> LiveLots(AiPackageCreditAccount account)
        => db.AiPackageCreditLots.Local
            .Where(lot => lot.AccountId == account.Id && IsLive(lot, DateTimeOffset.UtcNow))
            .OrderBy(lot => lot.ExpiresAt ?? DateTimeOffset.MaxValue)
            .ThenBy(lot => lot.CreatedAt)
            .ThenBy(lot => lot.Id)
            .ToList();

    private List<AiPackageCreditLot> AccountLots(AiPackageCreditAccount account)
        => db.AiPackageCreditLots.Local
            .Where(lot => lot.AccountId == account.Id)
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
        // FINAL 2026-09-06: quantity is always a count of complete activities
        // (1 letter / 1 card). Every pool costs 2 AI credits per activity, so
        // no denomination conversion happens here — just a floor of 1.
        _ = account;
        _ = subtest;
        return Math.Max(1, quantity);
    }

    /// <summary>
    /// Account-level (fungible across lots) pool balances that can fund
    /// <paramref name="subtest"/> activities. Read by the gate and the debit
    /// alike, so they cannot be fed different numbers.
    /// </summary>
    private (int Dedicated, int Flexible, int Shared) WritingOrSpeakingPools(AiPackageCreditAccount account, string subtest)
    {
        var lots = LiveLots(account);
        return (
            lots.Sum(lot => subtest == "writing" ? lot.WritingOnlyCredits : lot.SpeakingOnlyCredits),
            lots.Sum(lot => lot.FlexibleCredits),
            lots.Sum(lot => lot.SharedCredits));
    }

    private bool CanFundWritingOrSpeaking(AiPackageCreditAccount account, string subtest, int quantity)
    {
        // FINAL 2026-09-06: 2 AI credits per letter/card from any pool, by the
        // same account-level rule the debit applies, so gates and debits can
        // never disagree.
        var pools = WritingOrSpeakingPools(account, subtest);
        return AiPackageCreditSnapshot.FundableWritingOrSpeakingActivities(pools.Dedicated, pools.Flexible, pools.Shared) >= quantity;
    }

    private SpendResult SpendWritingOrSpeaking(AiPackageCreditAccount account, string subtest, int quantity)
    {
        // FINAL 2026-09-06: every pool is denominated in AI credits and one
        // activity (one Writing letter / one Speaking card) costs exactly 2.
        // Priority: dedicated, then Flexible W/S (together they fund an
        // activity, so 1 + 1 is a letter), then Shared for whole activities.
        // Every pool is fungible ACROSS lots: a Shared activity may be funded
        // by one credit from each of two lots (Full Course gifts are odd).
        var unitsPerActivity = AiGradingCreditCost.CreditsPerWritingOrSpeakingActivity;
        var lots = LiveLots(account);
        var pools = WritingOrSpeakingPools(account, subtest);
        if (AiPackageCreditSnapshot.FundableWritingOrSpeakingActivities(pools.Dedicated, pools.Flexible, pools.Shared) < quantity)
        {
            // Refuse BEFORE touching any lot: the caller sees CreditsUsed != cost.
            return new SpendResult(0, 0, 0, 0, SerializeAllocations([]), string.Empty, "none", 0);
        }

        var poolActivities = AiPackageCreditSnapshot.PoolFundedWritingOrSpeakingActivities(pools.Dedicated, pools.Flexible, quantity);
        var remainingUnits = poolActivities * unitsPerActivity;
        var remainingSharedUnits = (quantity - poolActivities) * unitsPerActivity;
        var writing = 0;
        var speaking = 0;
        var flexible = 0;
        var shared = 0;
        var allocations = new Dictionary<string, LotAllocation>(StringComparer.Ordinal);

        LotAllocation Track(AiPackageCreditLot lot) =>
            allocations.TryGetValue(lot.Id, out var existing)
                ? existing
                : allocations[lot.Id] = new LotAllocation(lot.Id, 0, 0, 0, 0, 0, 0, 0);

        foreach (var lot in lots)
        {
            if (remainingUnits <= 0) break;
            var dedicated = subtest == "writing" ? lot.WritingOnlyCredits : lot.SpeakingOnlyCredits;
            var takeDedicated = Math.Min(dedicated, remainingUnits);
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
            remainingUnits -= takeDedicated;
        }

        foreach (var lot in lots)
        {
            if (remainingUnits <= 0) break;
            var takeFlexible = Math.Min(lot.FlexibleCredits, remainingUnits);
            if (takeFlexible <= 0) continue;
            lot.FlexibleCredits -= takeFlexible;
            remainingUnits -= takeFlexible;
            flexible += takeFlexible;
            allocations[lot.Id] = Track(lot) with { Flexible = Track(lot).Flexible + takeFlexible };
        }

        foreach (var lot in lots)
        {
            if (remainingSharedUnits <= 0) break;
            var takeShared = Math.Min(lot.SharedCredits, remainingSharedUnits);
            if (takeShared <= 0) continue;
            lot.SharedCredits -= takeShared;
            remainingSharedUnits -= takeShared;
            shared += takeShared;
            allocations[lot.Id] = Track(lot) with { Shared = Track(lot).Shared + takeShared };
        }

        RebuildAccountFromLots(account);
        var label = subtest == "writing" ? "Writing" : "Speaking";
        var dedicatedRemaining = subtest == "writing" ? account.WritingOnlyCredits : account.SpeakingOnlyCredits;
        string spent;
        string remaining;
        string balanceSource;
        int remainingActivities;
        if (shared > 0 && writing + speaking + flexible == 0)
        {
            spent = CountText(shared, "Shared Credit");
            remaining = $"{CountText(account.SharedCredits, "Shared Credit")} remaining";
            remainingActivities = account.SharedCredits / unitsPerActivity;
            balanceSource = "shared";
        }
        else if (writing + speaking > 0 && flexible == 0 && shared == 0)
        {
            spent = CountText(writing + speaking, $"{label} Credit");
            remaining = $"{CountText(dedicatedRemaining, $"{label} Credit")} remaining";
            remainingActivities = dedicatedRemaining / unitsPerActivity;
            balanceSource = "dedicated";
        }
        else if (flexible > 0 && writing + speaking == 0 && shared == 0)
        {
            spent = CountText(flexible, "Flexible Writing/Speaking Credit");
            remaining = $"{CountText(account.FlexibleCredits, "Flexible Writing/Speaking Credit")} remaining";
            remainingActivities = account.FlexibleCredits / unitsPerActivity;
            balanceSource = "flexible_ws";
        }
        else
        {
            var parts = new List<string>();
            if (writing + speaking > 0) parts.Add(CountText(writing + speaking, $"{label} Credit"));
            if (flexible > 0) parts.Add(CountText(flexible, "Flexible Writing/Speaking Credit"));
            if (shared > 0) parts.Add(CountText(shared, "Shared Credit"));
            spent = string.Join(" + ", parts);
            // Credits across every pool that can still fund this subtest, with the
            // whole activities the same rule makes of them (same as RemainingAfter).
            var remainingCredits = dedicatedRemaining + account.FlexibleCredits + account.SharedCredits;
            remaining = $"{CountText(remainingCredits, "credit")} remaining for {label}";
            remainingActivities = RemainingAfterSpend(account, subtest);
            balanceSource = "mixed";
        }

        var noun = subtest == "writing" ? "letter" : "card";
        var feedback =
            $"{quantity} {Pluralize(noun, quantity)} used ({spent}). " +
            $"{remaining} ({remainingActivities} {Pluralize(noun, remainingActivities)}).";

        return new SpendResult(
            -shared,
            -flexible,
            -writing,
            -speaking,
            SerializeAllocations(allocations.Values),
            feedback,
            balanceSource,
            writing + speaking + flexible + shared);
    }

    private static string Pluralize(string noun, int count)
        => count == 1 ? noun : noun + "s";

    private static string CountText(int count, string unit)
        => $"{count} {Pluralize(unit, count)}";

    private static int RemainingAfterSpend(AiPackageCreditAccount account, string subtest)
        // Complete activities still fundable — same simulation as the gates.
        => subtest == "writing"
            ? AiPackageCreditSnapshot.FundableWritingOrSpeakingActivities(account.WritingOnlyCredits, account.FlexibleCredits, account.SharedCredits)
            : AiPackageCreditSnapshot.FundableWritingOrSpeakingActivities(account.SpeakingOnlyCredits, account.FlexibleCredits, account.SharedCredits);

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

            // The credit goes back exactly where it came from and the lot's own
            // state is left alone. This used to clear Expired, which revived the
            // WHOLE lot (its remaining balance and unlimited flags) whenever it
            // was parked (suspended/cancelled), reversed (refunded purchase) or
            // past its validity end. A refund restores the status quo ante: a
            // live lot gets the credit back, a parked lot returns it on unpark,
            // a lapsed or reversed lot keeps it inert like the rest of its balance.
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

    // Refund fallback only (RestoreAllocation). The account expiry is inherited even
    // when it is already past: the refunded credit must stay as inert as the package
    // it came from, never become an open-ended top-up. Admin top-ups go through
    // ApplyAdminAdjustmentToLots, which never inherits a past date.
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

    /// <summary>Returns the top-up lot it created, or null when nothing was added.</summary>
    private AiPackageCreditLot? ApplyAdminAdjustmentToLots(
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
        AiPackageCreditLot? topUpLot = null;
        if (sharedDelta > 0 || flexibleDelta > 0 || writingDelta > 0 || speakingDelta > 0 || listeningDelta > 0 || readingDelta > 0 || mockDelta > 0)
        {
            // A top-up inherits the account expiry only while it is still ahead.
            // After everything lapsed that date is past, and the new lot would be
            // born expired: the admin granted credits the learner can never use.
            var inheritedExpiry = account.ExpiresAt is { } accountExpiry && accountExpiry > DateTimeOffset.UtcNow
                ? accountExpiry
                : (DateTimeOffset?)null;
            topUpLot = new AiPackageCreditLot
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
                ExpiresAt = expiresAt ?? inheritedExpiry,
                SourceReferenceId = $"admin-adjust:{account.Id}",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            AddLot(account, topUpLot);
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
        return topUpLot;
    }

    private void ZeroAllLots(AiPackageCreditAccount account, DateTimeOffset now)
    {
        foreach (var lot in AccountLots(account).Where(lot => !lot.Expired))
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
        int usedCredits,
        string kind,
        bool unlimitedGrading,
        DateTimeOffset now)
    {
        var unlimited = kind switch
        {
            "writing" or "speaking" or "flexible" => unlimitedGrading || liveLots.Any(lot => lot.UnlimitedGrading),
            "listening" => liveLots.Any(lot => IsRealLot(lot) && lot.UnlimitedListening),
            "reading" => liveLots.Any(lot => IsRealLot(lot) && lot.UnlimitedReading),
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
        // Total = Remaining + genuine learner usage (ledger consumption), the same
        // invariant the bucket cards report. Used used to be derived as
        // all-lots minus live-lots, which reported credits that merely EXPIRED or
        // were parked as "used".
        var used = unlimited ? 0 : Math.Max(0, usedCredits);
        var granted = remaining + used;
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
}
