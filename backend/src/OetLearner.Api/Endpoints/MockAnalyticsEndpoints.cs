using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Mocks Module Phase 3.5 — admin business analytics specific to the Mocks
/// product. Routes live under <c>/v1/admin/analytics/mocks</c> and require the
/// <c>AdminContentRead</c> policy.
///
/// The endpoint aggregates three signals:
///   - revenue-by-package: SUM(price) from <see cref="PaymentTransaction"/>
///     completed rows whose <c>ProductType == "addon"</c> linked back to
///     a <see cref="BillingAddOnVersion"/> whose <c>GrantEntitlementsJson</c>
///     keys identify a mock package (mockFull / mockWriting / mockSpeaking).
///   - tutor-workload: open <see cref="MockBooking"/> count grouped by
///     assigned tutor id.
///   - low-quality-flags: count of <see cref="MockItemAnalysisSnapshot"/>
///     rows with a non-null <see cref="MockItemAnalysisSnapshot.Flag"/>,
///     grouped by flag value.
/// </summary>
public static class MockAnalyticsEndpoints
{
    /// <summary>Canonical mock-related entitlement keys we treat as mock revenue.</summary>
    private static readonly string[] MockEntitlementKeys =
    {
        "mockFull", "mock_full",
        "mockWriting", "mock_writing",
        "mockSpeaking", "mock_speaking",
        "mockSpeakingSession", "mock_speaking_session",
        "mockReading", "mock_reading",
        "mockListening", "mock_listening",
        "mockDiagnostic", "mock_diagnostic",
    };

    public static IEndpointRouteBuilder MapMockAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/analytics/mocks")
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUser")
            .WithTags("Admin Mock Analytics");

        group.MapGet("", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var revenue = await ComputeRevenueByPackageAsync(db, ct);
            var tutorWorkload = await ComputeTutorWorkloadAsync(db, ct);
            var lowQualityFlags = await ComputeLowQualityFlagsAsync(db, ct);

            return Results.Ok(new
            {
                revenueByPackage = revenue,
                tutorWorkload,
                lowQualityFlags,
                generatedAt = DateTimeOffset.UtcNow,
            });
        });

        return app;
    }

    // -----------------------------------------------------------------------

    private static async Task<IReadOnlyList<RevenueByPackageRow>> ComputeRevenueByPackageAsync(
        LearnerDbContext db,
        CancellationToken ct)
    {
        // 1. Pull every completed addon payment + its serialised addon-version id list.
        var completed = await db.PaymentTransactions.AsNoTracking()
            .Where(t => t.Status == "completed" && t.ProductType == "addon")
            .Select(t => new
            {
                t.Id,
                t.Amount,
                t.Currency,
                t.AddOnVersionIdsJson,
                t.CreatedAt,
            })
            .ToListAsync(ct);

        if (completed.Count == 0)
        {
            return Array.Empty<RevenueByPackageRow>();
        }

        // 2. Index all mock-tagged addon versions for fast lookup.
        var allVersions = await db.BillingAddOnVersions.AsNoTracking()
            .Select(v => new
            {
                v.Id,
                v.Code,
                v.Name,
                v.GrantEntitlementsJson,
            })
            .ToListAsync(ct);

        var mockVersionLookup = allVersions
            .Where(v => IsMockAddon(v.GrantEntitlementsJson))
            .ToDictionary(v => v.Id, v => (v.Code, v.Name), StringComparer.Ordinal);

        if (mockVersionLookup.Count == 0)
        {
            return Array.Empty<RevenueByPackageRow>();
        }

        // 3. Walk completed transactions; allocate revenue per linked mock version.
        var buckets = new Dictionary<string, RevenueBucket>(StringComparer.Ordinal);
        foreach (var txn in completed)
        {
            var versionIds = ExtractAddOnVersionIds(txn.AddOnVersionIdsJson);
            if (versionIds.Count == 0) continue;

            var mockMatches = versionIds
                .Where(mockVersionLookup.ContainsKey)
                .ToList();
            if (mockMatches.Count == 0) continue;

            // Split amount evenly across matched mock addons in the same txn —
            // this is the conservative attribution when a single payment
            // bundles multiple add-ons. Non-mock add-ons are excluded entirely.
            var share = txn.Amount / mockMatches.Count;
            foreach (var versionId in mockMatches)
            {
                var (code, name) = mockVersionLookup[versionId];
                if (!buckets.TryGetValue(code, out var bucket))
                {
                    bucket = new RevenueBucket(code, name, 0m, 0, txn.Currency);
                    buckets[code] = bucket;
                }

                buckets[code] = bucket with
                {
                    GrossRevenue = bucket.GrossRevenue + share,
                    PurchaseCount = bucket.PurchaseCount + 1,
                };
            }
        }

        return buckets.Values
            .OrderByDescending(b => b.GrossRevenue)
            .Select(b => new RevenueByPackageRow(
                AddOnCode: b.Code,
                AddOnName: b.Name,
                GrossRevenue: Math.Round(b.GrossRevenue, 2),
                PurchaseCount: b.PurchaseCount,
                Currency: b.Currency))
            .ToList();
    }

    private static async Task<IReadOnlyList<TutorWorkloadRow>> ComputeTutorWorkloadAsync(
        LearnerDbContext db,
        CancellationToken ct)
    {
        // "Open" bookings = anything that has not yet completed or cancelled.
        var openStatuses = new[]
        {
            MockBookingStatuses.Scheduled,
            MockBookingStatuses.Confirmed,
            MockBookingStatuses.InProgress,
        };

        var rows = await db.MockBookings.AsNoTracking()
            .Where(b => b.AssignedTutorId != null
                     && openStatuses.Contains(b.Status))
            .GroupBy(b => b.AssignedTutorId!)
            .Select(g => new TutorWorkloadRow(
                TutorId: g.Key,
                OpenBookings: g.Count(),
                NextScheduledAt: g.Min(x => x.ScheduledStartAt)))
            .OrderByDescending(r => r.OpenBookings)
            .ToListAsync(ct);

        return rows;
    }

    private static async Task<LowQualityFlagsSummary> ComputeLowQualityFlagsAsync(
        LearnerDbContext db,
        CancellationToken ct)
    {
        var flagged = await db.MockItemAnalysisSnapshots.AsNoTracking()
            .Where(s => s.Flag != null)
            .Select(s => new { s.Flag, s.MockBundleId, s.SubtestCode })
            .ToListAsync(ct);

        var total = flagged.Count;
        var byFlag = flagged
            .GroupBy(x => x.Flag!)
            .Select(g => new LowQualityFlagRow(g.Key, g.Count()))
            .OrderByDescending(r => r.Count)
            .ToList();

        var bySubtest = flagged
            .GroupBy(x => x.SubtestCode)
            .Select(g => new LowQualityFlagRow(g.Key, g.Count()))
            .OrderByDescending(r => r.Count)
            .ToList();

        return new LowQualityFlagsSummary(total, byFlag, bySubtest);
    }

    // -----------------------------------------------------------------------

    private static bool IsMockAddon(string? grantEntitlementsJson)
    {
        if (string.IsNullOrWhiteSpace(grantEntitlementsJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(grantEntitlementsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (MockEntitlementKeys.Contains(prop.Name, StringComparer.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string> ExtractAddOnVersionIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}" || json == "[]")
        {
            return Array.Empty<string>();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // The schema is "AddOnVersionIdsJson" — historically been written as
            // either an array of ids or an object {versionId: quantity}. Accept both.
            if (root.ValueKind == JsonValueKind.Array)
            {
                return root.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Cast<string>()
                    .ToList();
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                return root.EnumerateObject()
                    .Select(p => p.Name)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            }
        }
        catch (JsonException)
        {
            // Malformed legacy rows — fall through.
        }

        return Array.Empty<string>();
    }

    private sealed record RevenueBucket(
        string Code,
        string Name,
        decimal GrossRevenue,
        int PurchaseCount,
        string Currency);
}

/// <summary>Revenue attributed to a single mock add-on package, summed across completed payments.</summary>
public sealed record RevenueByPackageRow(
    string AddOnCode,
    string AddOnName,
    decimal GrossRevenue,
    int PurchaseCount,
    string Currency);

/// <summary>Open mock-booking workload for a single tutor.</summary>
public sealed record TutorWorkloadRow(
    string TutorId,
    int OpenBookings,
    DateTimeOffset? NextScheduledAt);

/// <summary>Single low-quality flag tally row.</summary>
public sealed record LowQualityFlagRow(string Key, int Count);

/// <summary>Aggregate low-quality-flag summary for the mocks product.</summary>
public sealed record LowQualityFlagsSummary(
    int TotalFlaggedItems,
    IReadOnlyList<LowQualityFlagRow> ByFlag,
    IReadOnlyList<LowQualityFlagRow> BySubtest);
