using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Mocks;

/// <summary>
/// Aggregates trend signals across a learner's most-recent completed MockReports.
///
/// Phase 3 of the OET Mocks Module: trend-based readiness.
///
/// "Consistent green" semantics (canonical OET Grade B threshold):
///   - At least the last 2 completed mocks reach scaled overall &gt;= 350.
///   - Two consecutive Grade-B+ overalls = exam-ready signal.
///   - Anything mixed = remediation before booking.
///
/// This service is intentionally read-only and side-effect free. The endpoint
/// at <c>GET /v1/learner/me/readiness/trend</c> consumes it directly.
/// </summary>
public sealed class MockReadinessTrendService(LearnerDbContext db)
{
    /// <summary>
    /// Canonical Grade-B scaled threshold per <see cref="OetScoring"/>.
    /// Mirrors the same constant used in <c>lib/mocks/workflow.ts</c>.
    /// </summary>
    public const int GradeBScaledThreshold = 350;

    /// <summary>
    /// Default number of most-recent completed mocks examined for trend.
    /// </summary>
    public const int DefaultAttemptsConsidered = 5;

    /// <summary>
    /// Minimum attempts required to declare a non-flat trend or a
    /// "consistent green" signal. With a single attempt we can only
    /// answer "pending more evidence".
    /// </summary>
    public const int MinAttemptsForTrend = 2;

    public Task<MockReadinessTrendResult> ComputeAsync(string userId, CancellationToken ct)
        => ComputeAsync(userId, DefaultAttemptsConsidered, ct);

    public async Task<MockReadinessTrendResult> ComputeAsync(string userId, int maxAttempts, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required for trend computation.", nameof(userId));
        }

        var limit = maxAttempts <= 0 ? DefaultAttemptsConsidered : maxAttempts;

        // Join MockReports → MockAttempts so we can filter by the owning user
        // and only consider Completed reports with a stable GeneratedAt timestamp.
        var reports = await db.MockReports.AsNoTracking()
            .Join(
                db.MockAttempts.AsNoTracking().Where(a => a.UserId == userId),
                report => report.MockAttemptId,
                attempt => attempt.Id,
                (report, _) => report)
            .Where(r => r.State == AsyncState.Completed && r.GeneratedAt != null)
            .OrderByDescending(r => r.GeneratedAt)
            .Take(limit)
            .ToListAsync(ct);

        var scores = reports
            .Select(r => TryReadOverallScore(r.PayloadJson))
            .Where(s => s.HasValue)
            .Select(s => s!.Value)
            .ToList();

        var attemptsConsidered = scores.Count;

        if (attemptsConsidered == 0)
        {
            return new MockReadinessTrendResult(
                AttemptsConsidered: 0,
                OverallTrend: "flat",
                ConsistentGreen: false,
                Message: "No completed mocks yet — finish a full mock to start a trend.");
        }

        if (attemptsConsidered < MinAttemptsForTrend)
        {
            return new MockReadinessTrendResult(
                AttemptsConsidered: attemptsConsidered,
                OverallTrend: "flat",
                ConsistentGreen: false,
                Message: "One mock on record — complete another full mock to confirm the trend.");
        }

        // `scores` is ordered most-recent first. Compare the most-recent two
        // (the canonical "consistency" check) and the linear delta from
        // earliest-considered to most-recent for the overall direction.
        var newest = scores[0];
        var secondNewest = scores[1];
        var oldestInWindow = scores[^1];

        var consistentGreen = newest >= GradeBScaledThreshold && secondNewest >= GradeBScaledThreshold;

        string overallTrend;
        var delta = newest - oldestInWindow;
        if (delta >= 10)
        {
            overallTrend = "up";
        }
        else if (delta <= -10)
        {
            overallTrend = "down";
        }
        else
        {
            overallTrend = "flat";
        }

        var message = BuildMessage(attemptsConsidered, consistentGreen, overallTrend);

        return new MockReadinessTrendResult(
            AttemptsConsidered: attemptsConsidered,
            OverallTrend: overallTrend,
            ConsistentGreen: consistentGreen,
            Message: message);
    }

    private static string BuildMessage(int attemptsConsidered, bool consistentGreen, string overallTrend)
    {
        if (consistentGreen)
        {
            return $"Consistent green across last {attemptsConsidered} mocks — exam-ready signal.";
        }

        return overallTrend switch
        {
            "up" => "Trend is improving across recent mocks — keep practising before booking the official OET.",
            "down" => "Recent mocks are trending down — complete remediation before booking.",
            _ => "Mixed results — complete remediation before booking."
        };
    }

    private static int? TryReadOverallScore(string payloadJson)
    {
        var payload = JsonSupport.Deserialize<Dictionary<string, object?>>(payloadJson, new Dictionary<string, object?>());
        return payload.TryGetValue("overallScore", out var value) && value is not null && int.TryParse(value.ToString(), out var parsed)
            ? parsed
            : null;
    }
}

/// <summary>
/// Trend signal returned by <see cref="MockReadinessTrendService"/>.
/// Mirrors the V1 schema record <c>MockReportTrendV1</c>.
/// </summary>
public sealed record MockReadinessTrendResult(
    int AttemptsConsidered,
    string OverallTrend,
    bool ConsistentGreen,
    string Message);
