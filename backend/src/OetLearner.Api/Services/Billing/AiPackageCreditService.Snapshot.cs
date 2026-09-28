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
                .GroupBy(row => new { row.PackageId, row.SourceReferenceId, row.Description })
                .Select(group =>
                {
                    var first = group.First();
                    return new AiPackageCreditGrantSourceDto(
                        first.PackageId,
                        first.Description,
                        group.Sum(row => deltaSelector(row)),
                        group.Min(row => row.CreatedAt),
                        group.Max(row => row.ExpiresAt),
                        first.SourceReferenceId,
                        group.Min(row => row.ValidFrom ?? row.CreatedAt),
                        DaysLeft(group.Max(row => row.ExpiresAt), now));
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
            var activeGrants = grants.Where(source =>
                (source.ValidFrom is null || source.ValidFrom <= now)
                && (source.ExpiresAt is null || source.ExpiresAt > now)).ToList();
            DateTimeOffset? validFrom = activeGrants.Count > 0
                ? activeGrants.Min(source => source.ValidFrom ?? source.GrantedAt)
                : null;
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

    private static bool IsLive(AiPackageCreditLot lot, DateTimeOffset now)
        => !lot.Expired
            && (lot.ValidFrom is null || lot.ValidFrom <= now)
            && (lot.ExpiresAt is null || lot.ExpiresAt > now);

    private static bool LotHasRemaining(AiPackageCreditLot lot)
        => lot.SharedCredits > 0
            || lot.FlexibleCredits > 0
            || lot.WritingOnlyCredits > 0
            || lot.SpeakingOnlyCredits > 0
            || lot.MockExamsRemaining > 0
            || lot.ListeningTestsRemaining is null
            || lot.ListeningTestsRemaining > 0
            || lot.ReadingTestsRemaining is null
            || lot.ReadingTestsRemaining > 0
            || lot.UnlimitedGrading
            || lot.UnlimitedListening
            || lot.UnlimitedReading;

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
