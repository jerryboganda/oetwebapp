using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

public sealed partial class AiPackageCreditService
{
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
                     row.CreatedAt,
                     row.SourceReferenceId,
                     row.ValidFrom))
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
                 row.ValidFrom,
                 row.ExpiresAt,
                 row.CreatedAt,
                 row.ReferenceId,
                 row.SourceReferenceId))
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
        var liveLots = lots.Where(lot => IsLive(lot, now)).ToList();
        // Same exclusion the gates use (HasLiveRealUnlimited): the synthetic legacy lot
        // only mirrors a pre-lot balance, so it never makes the card say Unlimited.
        var listeningUnlimited = liveLots.Any(lot => IsRealLot(lot) && lot.UnlimitedListening);
        var readingUnlimited = liveLots.Any(lot => IsRealLot(lot) && lot.UnlimitedReading);
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
        var buckets = BuildBucketDtos(account, grantRows, usage, unlimitedGrading, listeningUnlimited, readingUnlimited, lots, ledgerRows);

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
            BuildBucket(liveLots, usage.Shared, "shared", unlimitedGrading, now),
            BuildBucket(liveLots, usage.Flexible, "flexible", unlimitedGrading, now),
            BuildBucket(liveLots, usage.Writing, "writing", unlimitedGrading, now),
            BuildBucket(liveLots, usage.Speaking, "speaking", unlimitedGrading, now),
            BuildBucket(liveLots, usage.Listening, "listening", unlimitedGrading, now),
            BuildBucket(liveLots, usage.Reading, "reading", unlimitedGrading, now),
            BuildBucket(liveLots, usage.Mocks, "mocks", unlimitedGrading, now),
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
        DateTimeOffset? ValidFrom,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset CreatedAt,
        string? ReferenceId,
        string? SourceReferenceId);

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
            NetConsumed(ledgerRows, row => row.SharedCreditsDelta),
            NetConsumed(ledgerRows, row => row.FlexibleCreditsDelta),
            NetConsumed(ledgerRows, row => row.WritingOnlyCreditsDelta),
            NetConsumed(ledgerRows, row => row.SpeakingOnlyCreditsDelta),
            NetConsumed(ledgerRows, row => row.ListeningTestsDelta),
            NetConsumed(ledgerRows, row => row.ReadingTestsDelta),
            NetConsumed(ledgerRows, row => row.MockExamsDelta));

    // Debits are negative and refunds positive, so summing the SIGNED deltas nets a
    // refunded activity to 0 used. Clamping each row at 0 first (the old code)
    // dropped every refund row, so Used stayed inflated after a refund while
    // Remaining was restored and Total = Remaining + Used overstated the package.
    private static int NetConsumed(IReadOnlyList<LedgerRow> ledgerRows, Func<LedgerRow, int> delta)
        => Math.Max(0, -ledgerRows.Where(row => IsConsumption(row.Reason)).Sum(delta));

    private static IReadOnlyList<AiPackageCreditBucketDto> BuildBucketDtos(
        AiPackageCreditAccount account,
        List<LedgerRow> grantRows,
        CreditUsage usage,
        bool writingSpeakingUnlimited,
        bool listeningUnlimited,
        bool readingUnlimited,
        IReadOnlyList<AiPackageCreditLot> lots,
        IReadOnlyList<LedgerRow> ledgerRows)
    {
        var now = DateTimeOffset.UtcNow;

        // A Purchase row is reversed once its GrantReversed row exists (same key
        // shape ReverseOneGrantAsync writes). Without this a refunded package kept
        // showing as a live grant, because the reversal row is negative and the
        // grant list only keeps positive deltas.
        var reversalReferences = grantRows
            .Where(row => row.Reason == AiPackageCreditReason.GrantReversed && row.ReferenceId is not null)
            .Select(row => row.ReferenceId!)
            .ToHashSet(StringComparer.Ordinal);

        bool IsReversedPurchase(LedgerRow row)
            => row.Reason == AiPackageCreditReason.Purchase
                && row.ReferenceId is { } reference
                && (reversalReferences.Contains(reference)
                    || reversalReferences.Contains(AddonGrantProcessor.FitDatabaseKey($"grant-reverse:{reference}")));

        // Source keys that belong ONLY to reversed purchases: a refunded purchase's
        // lapsed lot must not be reported as "expired unused" credits.
        var liveSources = grantRows
            .Where(row => row.Reason == AiPackageCreditReason.Purchase && !IsReversedPurchase(row))
            .SelectMany(row => new[] { row.ReferenceId, row.SourceReferenceId })
            .Where(value => value is not null)
            .Select(value => value!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var refundedOnlySources = grantRows
            .Where(IsReversedPurchase)
            .SelectMany(row => new[] { row.ReferenceId, row.SourceReferenceId })
            .Where(value => value is not null && !liveSources.Contains(value))
            .Select(value => value!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ExpireIfNeededAsync stamps "expiry:{lotId}:..." on every lot it lapses, which
        // also covers lots with no ExpiresAt of their own (wallet-level expiry).
        var expiryReferences = ledgerRows
            .Where(row => row.Reason == AiPackageCreditReason.Expiry && row.ReferenceId is not null)
            .Select(row => row.ReferenceId!)
            .ToList();

        // Exam-pass expiry zeroes lots and records the loss only as PassExpiry rows
        // (negative deltas), so it has to be read from the ledger, not the lots.
        var passExpiryRows = ledgerRows
            .Where(row => row.Reason == AiPackageCreditReason.PassExpiry)
            .ToList();
        DateTimeOffset? latestPassExpiry = passExpiryRows.Count > 0
            ? passExpiryRows.Max(row => row.CreatedAt)
            : null;

        // Credits sitting in a lot whose validity ended unused. Lots keep their
        // balance when they lapse (so an admin date extension can revive them),
        // which makes them the exact source for "expired". Parked (suspended)
        // lots are still inside their validity window and are not counted.
        bool LotExpiredByDate(AiPackageCreditLot lot)
            => lot.Expired
                && !(lot.SourceReferenceId is { } lotSource && refundedOnlySources.Contains(lotSource))
                && ((lot.ExpiresAt is { } end && end <= now)
                    || expiryReferences.Any(reference =>
                        reference.StartsWith($"expiry:{lot.Id}:", StringComparison.Ordinal)));

        int ExpiredCredits(Func<AiPackageCreditLot, int> lotSelector, Func<LedgerRow, int> passSelector)
            => lots.Where(LotExpiredByDate).Sum(lotSelector)
                + Math.Max(0, -passExpiryRows.Sum(passSelector));

        List<AiPackageCreditGrantSourceDto> GrantsFor(Func<LedgerRow, int> deltaSelector)
            => grantRows
                .Where(row => deltaSelector(row) > 0)
                .GroupBy(row => new { row.PackageId, row.SourceReferenceId, row.Description })
                .Select(group =>
                {
                    // Reversal is per purchase row, so a re-bought package can be
                    // partly refunded: total and state come from the rows still live.
                    var rows = group.ToList();
                    var liveRows = rows.Where(row => !IsReversedPurchase(row)).ToList();
                    var basis = liveRows.Count > 0 ? liveRows : rows;
                    var first = basis[0];
                    // A null ExpiresAt is open-ended; Max() alone would skip it and
                    // mislabel the whole line as expired.
                    var expiresAt = basis.Any(row => row.ExpiresAt is null)
                        ? (DateTimeOffset?)null
                        : basis.Max(row => row.ExpiresAt);
                    var validFrom = basis.Min(row => row.ValidFrom ?? row.CreatedAt);
                    var lapsedByPass = latestPassExpiry is { } passedAt
                        && liveRows.Count > 0
                        && liveRows.All(row => row.CreatedAt <= passedAt);
                    var status = liveRows.Count == 0
                        ? "reversed"
                        : ((expiresAt is { } end && end <= now) || lapsedByPass)
                            ? "expired"
                            : (validFrom > now ? "scheduled" : "active");
                    return new AiPackageCreditGrantSourceDto(
                        first.PackageId,
                        first.Description,
                        basis.Sum(row => deltaSelector(row)),
                        basis.Min(row => row.CreatedAt),
                        expiresAt,
                        first.SourceReferenceId,
                        validFrom,
                        DaysLeft(expiresAt, now),
                        status);
                })
                .OrderBy(source => source.GrantedAt)
                .ToList();

        AiPackageCreditBucketDto Bucket(string key, string label, int remaining, bool unlimited, int used, List<AiPackageCreditGrantSourceDto> grants, int expired = 0)
        {
            // Admin AI credits fix: Total = Used + Remaining is the invariant that the
            // UI reports. Admin ± / Set adjustments change `remaining` (the pool), so
            // Total moves but Used stays pinned to genuine learner consumption. Building
            // Total from the grant-reversal-free used figure keeps "Used" a read-only
            // learner-usage number (see ProjectSnapshotAsync.ComputeUsage).
            var effectiveUsed = unlimited ? 0 : Math.Clamp(used, 0, int.MaxValue);
            var totalGranted = remaining + effectiveUsed;
            // "active" already encodes started + not past its end + not refunded + not
            // wiped by an exam-pass expiry.
            var activeGrants = grants.Where(source => source.Status == "active").ToList();
            DateTimeOffset? validFrom = activeGrants.Count > 0
                ? activeGrants.Min(source => source.ValidFrom ?? source.GrantedAt)
                : null;
            DateTimeOffset? expiresAt = activeGrants.Any(source => source.ExpiresAt is null)
                ? null
                : activeGrants.Select(source => source.ExpiresAt).Max();
            // A refunded package no longer sources this balance; an expired one still
            // explains where the lapsed credits came from.
            var sourceGrants = grants.Where(source => source.Status != "reversed").ToList();
            return new(
                key,
                label,
                unlimited,
                totalGranted,
                effectiveUsed,
                remaining,
                sourceGrants.Count == 0 ? null : string.Join(", ", sourceGrants
                    .Select(source => HumanizePackageName(source.PackageId, source.Description))
                    .Distinct(StringComparer.OrdinalIgnoreCase)),
                validFrom,
                expiresAt ?? account.ExpiresAt,
                DaysLeft(expiresAt ?? account.ExpiresAt, now),
                grants,
                unlimited ? 0 : Math.Max(0, expired));
        }

        var buckets = new List<AiPackageCreditBucketDto>
        {
            // Unlimited only with a real live unlimited lot: a null pool alone is a ghost
            // sentinel (legacy lot / deleted source) that the gates refuse.
            Bucket("reading", "Reading Credits", account.ReadingTestsRemaining ?? 0, readingUnlimited, usage.Reading, GrantsFor(row => row.ReadingTestsDelta),
                ExpiredCredits(lot => lot.ReadingTestsRemaining ?? 0, row => row.ReadingTestsDelta)),
            Bucket("listening", "Listening Credits", account.ListeningTestsRemaining ?? 0, listeningUnlimited, usage.Listening, GrantsFor(row => row.ListeningTestsDelta),
                ExpiredCredits(lot => lot.ListeningTestsRemaining ?? 0, row => row.ListeningTestsDelta)),
            Bucket("writing", "Writing Credits",
                writingSpeakingUnlimited ? 0 : account.WritingOnlyCredits,
                writingSpeakingUnlimited,
                usage.Writing,
                GrantsFor(row => row.WritingOnlyCreditsDelta),
                ExpiredCredits(lot => lot.WritingOnlyCredits, row => row.WritingOnlyCreditsDelta)),
            Bucket("speaking", "Speaking Credits",
                writingSpeakingUnlimited ? 0 : account.SpeakingOnlyCredits,
                writingSpeakingUnlimited,
                usage.Speaking,
                GrantsFor(row => row.SpeakingOnlyCreditsDelta),
                ExpiredCredits(lot => lot.SpeakingOnlyCredits, row => row.SpeakingOnlyCreditsDelta)),
            Bucket("shared", "Shared Credits", account.SharedCredits, false, usage.Shared, GrantsFor(row => row.SharedCreditsDelta),
                ExpiredCredits(lot => lot.SharedCredits, row => row.SharedCreditsDelta)),
        };

        var flexibleWsGrants = GrantsFor(row => row.FlexibleCreditsDelta);
        if (account.FlexibleCredits > 0 || flexibleWsGrants.Count > 0)
        {
            buckets.Add(Bucket("flexible_ws", "Flexible W/S Credits", account.FlexibleCredits, false, usage.Flexible, flexibleWsGrants,
                ExpiredCredits(lot => lot.FlexibleCredits, row => row.FlexibleCreditsDelta)));
        }

        var mockGrants = GrantsFor(row => row.MockExamsDelta);
        if (account.MockExamsRemaining > 0 || mockGrants.Count > 0)
        {
            buckets.Add(Bucket("mock", "Full Mock Attempts", account.MockExamsRemaining, false, usage.Mocks, mockGrants,
                ExpiredCredits(lot => lot.MockExamsRemaining, row => row.MockExamsDelta)));
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

    private static bool IsLive(AiPackageCreditLot lot, DateTimeOffset now)
        => !lot.Expired
            && (lot.ValidFrom is null || lot.ValidFrom <= now)
            && (lot.ExpiresAt is null || lot.ExpiresAt > now);

    // Same definition as LotRetainsValue. A null Listening/Reading balance only means
    // "unlimited" together with its Unlimited flag (every creation path sets both), so
    // counting null as value made a reversed lot - whose flags were cleared but whose
    // pool was left null - look like it still held credits.
    private static bool LotHasRemaining(AiPackageCreditLot lot)
        => LotRetainsValue(lot);

    private static bool SourceMatches(AiPackageCreditTransaction row, string sourceReferenceId)
    {
        if (string.Equals(row.SourceReferenceId, sourceReferenceId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(row.SourceReferenceId))
        {
            return row.SourceReferenceId.StartsWith(sourceReferenceId + ":", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(row.ReferenceId, sourceReferenceId, StringComparison.OrdinalIgnoreCase)
            || row.ReferenceId?.StartsWith(sourceReferenceId + ":", StringComparison.OrdinalIgnoreCase) == true
            || row.ReferenceId?.StartsWith("stripe:" + sourceReferenceId + ":", StringComparison.OrdinalIgnoreCase) == true;
    }
}
