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
            // Phase 2 closure — Reading-section cross-reference. Lets the
            // mocks dashboard show how learners are performing on the
            // Reading subtest WITHIN mock sessions, without forcing the
            // operator to drill into /admin/analytics/reading.
            var readingSection = await ComputeReadingSectionAsync(db, ct);

            return Results.Ok(new
            {
                revenueByPackage = revenue,
                tutorWorkload,
                lowQualityFlags,
                readingSection,
                generatedAt = DateTimeOffset.UtcNow,
            });
        });

        // ── Phase 8a sub-routes ─────────────────────────────────────────────
        // Four additional aggregations for the admin analytics dashboard.
        // Each is its own GET so the frontend can fetch them in parallel
        // without inflating the root payload.

        group.MapGet("/attempts-completion", async (
            int? days,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var window = ResolveWindow(days);
            var summary = await ComputeAttemptsCompletionAsync(db, window, ct);

            return Results.Ok(new
            {
                window = new { start = window.Start, end = window.End },
                started = summary.Started,
                completed = summary.Completed,
                completionRate = summary.CompletionRate,
                generatedAt = DateTimeOffset.UtcNow,
            });
        });

        group.MapGet("/average-readiness", async (
            int? days,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var window = ResolveWindow(days);
            var summary = await ComputeAverageReadinessAsync(db, window, ct);

            return Results.Ok(new
            {
                window = new { start = window.Start, end = window.End },
                sampleSize = summary.SampleSize,
                averageScore = summary.AverageScore,
                distribution = summary.Distribution,
                generatedAt = DateTimeOffset.UtcNow,
            });
        });

        group.MapGet("/pass-prediction", async (
            int? days,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var window = ResolveWindow(days);
            var summary = await ComputePassPredictionAsync(db, window, ct);

            return Results.Ok(new
            {
                window = new { start = window.Start, end = window.End },
                sampleSize = summary.SampleSize,
                predictedPassRate = summary.PredictedPassRate,
                byProfession = summary.ByProfession,
                generatedAt = DateTimeOffset.UtcNow,
            });
        });

        group.MapGet("/marking-delay", async (
            int? days,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var window = ResolveWindow(days);
            var perSubtest = await ComputeMarkingDelayAsync(db, window, ct);

            return Results.Ok(new
            {
                window = new { start = window.Start, end = window.End },
                perSubtest,
                generatedAt = DateTimeOffset.UtcNow,
            });
        });

        return app;
    }

    // -----------------------------------------------------------------------
    // Phase 8a — window helper
    // -----------------------------------------------------------------------

    private static AnalyticsWindow ResolveWindow(int? days)
    {
        var requested = days ?? 30;
        var clamped = Math.Clamp(requested, 1, 365);
        var end = DateTimeOffset.UtcNow;
        var start = end - TimeSpan.FromDays(clamped);
        return new AnalyticsWindow(start, end, clamped);
    }

    private readonly record struct AnalyticsWindow(
        DateTimeOffset Start,
        DateTimeOffset End,
        int Days);

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

    /// <summary>
    /// Phase 2 closure — aggregates Reading subtest performance across all
    /// mock sessions. Reads <see cref="MockSectionAttempt"/> rows with
    /// SubtestCode == "reading" and computes the mean raw, mean scaled,
    /// mean completion-time-in-seconds, and the section completion rate
    /// (Submitted+Graded / Started+InProgress+Submitted+Graded). All
    /// values are nullable because the underlying mock pipeline may not
    /// yet have written any Reading sections.
    /// </summary>
    private static async Task<ReadingSectionSummary> ComputeReadingSectionAsync(
        LearnerDbContext db,
        CancellationToken ct)
    {
        var rows = await db.Set<MockSectionAttempt>().AsNoTracking()
            .Where(s => s.SubtestCode == "reading")
            .Select(s => new
            {
                s.State,
                s.RawScore,
                s.ScaledScore,
                s.StartedAt,
                s.CompletedAt,
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return new ReadingSectionSummary(
                Started: 0,
                Submitted: 0,
                CompletionRatePercent: null,
                AverageRawScore: null,
                AverageScaledScore: null,
                AverageCompletionSeconds: null);
        }

        var started = rows.Count(r => r.State != AttemptState.NotStarted);
        // "Submitted" + "Completed" both count as a finished Reading section.
        // Evaluating is treated as not-yet-complete to avoid double-counting
        // mid-pipeline rows. Failed/Abandoned are excluded so the rate
        // reflects useful completions only.
        var submittedOrGraded = rows.Count(r => r.State == AttemptState.Submitted || r.State == AttemptState.Completed);
        var completionRate = started > 0
            ? Math.Round((double)submittedOrGraded / started * 100.0, 1)
            : (double?)null;

        var rawScores = rows.Where(r => r.RawScore.HasValue).Select(r => (double)r.RawScore!.Value).ToList();
        var scaledScores = rows.Where(r => r.ScaledScore.HasValue).Select(r => (double)r.ScaledScore!.Value).ToList();
        var completionSeconds = rows
            .Where(r => r.StartedAt.HasValue && r.CompletedAt.HasValue && r.CompletedAt.Value > r.StartedAt.Value)
            .Select(r => (r.CompletedAt!.Value - r.StartedAt!.Value).TotalSeconds)
            .ToList();

        return new ReadingSectionSummary(
            Started: started,
            Submitted: submittedOrGraded,
            CompletionRatePercent: completionRate,
            AverageRawScore: rawScores.Count > 0 ? Math.Round(rawScores.Average(), 1) : null,
            AverageScaledScore: scaledScores.Count > 0 ? Math.Round(scaledScores.Average(), 1) : null,
            AverageCompletionSeconds: completionSeconds.Count > 0 ? Math.Round(completionSeconds.Average(), 0) : null);
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

    // -----------------------------------------------------------------------
    // Phase 8a — attempts completion
    // -----------------------------------------------------------------------

    private static async Task<AttemptsCompletionSummary> ComputeAttemptsCompletionAsync(
        LearnerDbContext db,
        AnalyticsWindow window,
        CancellationToken ct)
    {
        // Only count attempts whose StartedAt falls inside the window.
        // We rely on the EF-mapped nullable so attempts with no StartedAt
        // are excluded from both numerator and denominator.
        var rows = await db.MockSectionAttempts.AsNoTracking()
            .Where(a => a.StartedAt != null
                     && a.StartedAt >= window.Start
                     && a.StartedAt <= window.End)
            .Select(a => new { a.State })
            .Take(50_000)
            .ToListAsync(ct);

        var started = rows.Count;
        var completed = rows.Count(r =>
            r.State == AttemptState.Submitted
            || r.State == AttemptState.Completed);

        var completionRate = started > 0
            ? Math.Round((double)completed / started, 4)
            : 0d;

        return new AttemptsCompletionSummary(started, completed, completionRate);
    }

    // -----------------------------------------------------------------------
    // Phase 8a — average readiness
    // -----------------------------------------------------------------------

    private static async Task<AverageReadinessSummary> ComputeAverageReadinessAsync(
        LearnerDbContext db,
        AnalyticsWindow window,
        CancellationToken ct)
    {
        // PayloadJson stores `overallScore` as a string. Pull the raw JSON,
        // parse client-side, and bucket by the configured thresholds.
        var rows = await db.MockReports.AsNoTracking()
            .Where(r => r.GeneratedAt != null
                     && r.GeneratedAt >= window.Start
                     && r.GeneratedAt <= window.End)
            .Select(r => new { r.PayloadJson })
            .Take(500)
            .ToListAsync(ct);

        var scores = new List<int>(rows.Count);
        foreach (var row in rows)
        {
            var parsed = TryReadOverallScore(row.PayloadJson);
            if (parsed.HasValue) scores.Add(parsed.Value);
        }

        if (scores.Count == 0)
        {
            return new AverageReadinessSummary(
                SampleSize: 0,
                AverageScore: null,
                Distribution: new ReadinessDistribution(0, 0, 0, 0));
        }

        var red = scores.Count(s => s < 320);
        var amber = scores.Count(s => s >= 320 && s < 350);
        var green = scores.Count(s => s >= 350 && s < 400);
        var darkGreen = scores.Count(s => s >= 400);

        var average = Math.Round(scores.Average(), 1);
        return new AverageReadinessSummary(
            SampleSize: scores.Count,
            AverageScore: average,
            Distribution: new ReadinessDistribution(red, amber, green, darkGreen));
    }

    private static int? TryReadOverallScore(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (!doc.RootElement.TryGetProperty("overallScore", out var prop)) return null;

            return prop.ValueKind switch
            {
                JsonValueKind.Number when prop.TryGetInt32(out var intVal) => intVal,
                JsonValueKind.String when int.TryParse(prop.GetString(), out var parsed) => parsed,
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // -----------------------------------------------------------------------
    // Phase 8a — pass prediction (recent-cohort aggregate)
    // -----------------------------------------------------------------------

    private static async Task<PassPredictionSummary> ComputePassPredictionAsync(
        LearnerDbContext db,
        AnalyticsWindow window,
        CancellationToken ct)
    {
        // Pull the MockReports written inside the window plus the matching
        // attempt rows for profession lookup. Capped at 500 reports to keep
        // the pass-prediction loop bounded.
        var reportRows = await db.MockReports.AsNoTracking()
            .Where(r => r.GeneratedAt != null
                     && r.GeneratedAt >= window.Start
                     && r.GeneratedAt <= window.End)
            .OrderByDescending(r => r.GeneratedAt)
            .Select(r => new { r.MockAttemptId, r.PayloadJson })
            .Take(500)
            .ToListAsync(ct);

        if (reportRows.Count == 0)
        {
            return new PassPredictionSummary(
                SampleSize: 0,
                PredictedPassRate: null,
                ByProfession: Array.Empty<PassPredictionProfessionRow>());
        }

        var attemptIds = reportRows.Select(r => r.MockAttemptId).Distinct().ToList();
        var professions = await db.MockAttempts.AsNoTracking()
            .Where(a => attemptIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Profession })
            .ToListAsync(ct);

        var professionByAttempt = professions
            .GroupBy(a => a.Id)
            .ToDictionary(g => g.Key, g => g.First().Profession ?? "unknown", StringComparer.Ordinal);

        // Tally pass/borderline-pass verdicts via the AdvisoryTier ladder.
        // We derive a numeric pass-rate in [0,1] from MockReport.overallScore
        // alone — the full MockPassPredictionService is not safe to invoke
        // 500× per request, but this matches the same Grade-B threshold the
        // service uses ("strong" / "passing" → counted as pass).
        var perProfession = new Dictionary<string, PassPredictionAccumulator>(StringComparer.Ordinal);
        var globalPass = 0;
        var globalTotal = 0;

        foreach (var row in reportRows)
        {
            var overall = TryReadOverallScore(row.PayloadJson);
            if (!overall.HasValue) continue;

            globalTotal++;
            var isPass = overall.Value >= 350; // OET Grade-B anchor.
            if (isPass) globalPass++;

            var profession = professionByAttempt.TryGetValue(row.MockAttemptId, out var p)
                ? p
                : "unknown";

            if (!perProfession.TryGetValue(profession, out var acc))
            {
                acc = new PassPredictionAccumulator(0, 0);
                perProfession[profession] = acc;
            }
            perProfession[profession] = acc with
            {
                Total = acc.Total + 1,
                Pass = acc.Pass + (isPass ? 1 : 0),
            };
        }

        var byProfession = perProfession
            .OrderByDescending(kvp => kvp.Value.Total)
            .Select(kvp => new PassPredictionProfessionRow(
                Profession: kvp.Key,
                SampleSize: kvp.Value.Total,
                PredictedPassRate: kvp.Value.Total > 0
                    ? Math.Round((double)kvp.Value.Pass / kvp.Value.Total, 4)
                    : 0d))
            .ToList();

        double? overallRate = globalTotal > 0
            ? Math.Round((double)globalPass / globalTotal, 4)
            : null;

        return new PassPredictionSummary(
            SampleSize: globalTotal,
            PredictedPassRate: overallRate,
            ByProfession: byProfession);
    }

    private sealed record PassPredictionAccumulator(int Total, int Pass);

    // -----------------------------------------------------------------------
    // Phase 8a — marking delay
    // -----------------------------------------------------------------------

    private static async Task<IReadOnlyList<MarkingDelayRow>> ComputeMarkingDelayAsync(
        LearnerDbContext db,
        AnalyticsWindow window,
        CancellationToken ct)
    {
        // Reservations transition from Reserved → Consumed when a reviewer
        // marks. The interval ReservedAt → ConsumedAt approximates the
        // marker turnaround. Selection encodes the subtest the credits paid
        // for: "writing", "speaking", or "writing_and_speaking".
        var reservations = await db.MockReviewReservations.AsNoTracking()
            .Where(r => r.ConsumedAt != null
                     && r.ConsumedAt >= window.Start
                     && r.ConsumedAt <= window.End)
            .Select(r => new
            {
                r.Selection,
                r.ReservedAt,
                r.ConsumedAt,
            })
            .Take(500)
            .ToListAsync(ct);

        if (reservations.Count == 0)
        {
            return Array.Empty<MarkingDelayRow>();
        }

        var byWriting = new List<double>();
        var bySpeaking = new List<double>();

        foreach (var row in reservations)
        {
            if (row.ConsumedAt is null) continue;
            var delayHours = (row.ConsumedAt.Value - row.ReservedAt).TotalHours;
            if (delayHours < 0) continue; // Skip clock-skew anomalies.

            var sel = (row.Selection ?? string.Empty).ToLowerInvariant();
            if (sel.Contains("writing"))
            {
                byWriting.Add(delayHours);
            }
            if (sel.Contains("speaking"))
            {
                bySpeaking.Add(delayHours);
            }
        }

        var output = new List<MarkingDelayRow>(2);
        if (byWriting.Count > 0) output.Add(BuildMarkingDelayRow("writing", byWriting));
        if (bySpeaking.Count > 0) output.Add(BuildMarkingDelayRow("speaking", bySpeaking));
        return output;
    }

    private static MarkingDelayRow BuildMarkingDelayRow(string subtest, List<double> hours)
    {
        var sorted = hours.OrderBy(h => h).ToList();
        var avg = Math.Round(sorted.Average(), 2);
        var p95 = Math.Round(Percentile(sorted, 0.95), 2);
        return new MarkingDelayRow(
            Subtest: subtest,
            SampleSize: sorted.Count,
            AvgDelayHours: avg,
            P95DelayHours: p95);
    }

    private static double Percentile(IReadOnlyList<double> sortedAscending, double percentile)
    {
        if (sortedAscending.Count == 0) return 0d;
        if (sortedAscending.Count == 1) return sortedAscending[0];

        var rank = percentile * (sortedAscending.Count - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper) return sortedAscending[lower];

        var weight = rank - lower;
        return sortedAscending[lower] + weight * (sortedAscending[upper] - sortedAscending[lower]);
    }
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

// ── Phase 8a wire contracts ────────────────────────────────────────────────

/// <summary>Mock attempt completion summary for the window.</summary>
public sealed record AttemptsCompletionSummary(
    int Started,
    int Completed,
    double CompletionRate);

/// <summary>Readiness score buckets within the window.</summary>
public sealed record ReadinessDistribution(
    int Red,
    int Amber,
    int Green,
    int DarkGreen);

/// <summary>Average readiness summary.</summary>
public sealed record AverageReadinessSummary(
    int SampleSize,
    double? AverageScore,
    ReadinessDistribution Distribution);

/// <summary>Pass-prediction tally for one profession.</summary>
public sealed record PassPredictionProfessionRow(
    string Profession,
    int SampleSize,
    double PredictedPassRate);

/// <summary>Aggregate pass-prediction summary.</summary>
public sealed record PassPredictionSummary(
    int SampleSize,
    double? PredictedPassRate,
    IReadOnlyList<PassPredictionProfessionRow> ByProfession);

/// <summary>Marking-delay row per subtest.</summary>
public sealed record MarkingDelayRow(
    string Subtest,
    int SampleSize,
    double AvgDelayHours,
    double P95DelayHours);

/// <summary>
/// Phase 2 closure — Reading subtest aggregate computed across mock
/// sessions. Surfaced in the root <c>/v1/admin/analytics/mocks</c>
/// payload so the mocks dashboard can show how mocks correlate with
/// Reading-paper performance without a second drill-down.
/// </summary>
public sealed record ReadingSectionSummary(
    int Started,
    int Submitted,
    double? CompletionRatePercent,
    double? AverageRawScore,
    double? AverageScaledScore,
    double? AverageCompletionSeconds);
