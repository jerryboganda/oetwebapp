using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Readiness;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Learner-facing readiness endpoints. Replaces the original
/// <c>GET /v1/readiness</c> implementation in <c>LearnerEndpoints</c> with
/// a fully computed snapshot from <see cref="ReadinessComputationService"/>,
/// and adds new endpoints for history, blockers, forecast scenarios, and
/// manual refresh.
/// </summary>
public static class ReadinessEndpoints
{
    public static IEndpointRouteBuilder MapReadinessEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/readiness")
            .RequireAuthorization("LearnerOnly")
            .WithTags("Learner Readiness");

        group.MapGet(string.Empty, async (
            HttpContext http,
            ReadinessComputationService service,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var userId = UserId(http);
            var snapshot = await service.GetOrComputeAsync(userId, ct);
            db.AnalyticsEvents.Add(new AnalyticsEventRecord
            {
                Id = $"evt-{Guid.NewGuid():N}",
                UserId = userId,
                EventName = "readiness_viewed",
                PayloadJson = JsonSupport.Serialize(new { userId, snapshotId = snapshot.Id, computedAt = snapshot.ComputedAt }),
                OccurredAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToResponse(snapshot));
        });

        group.MapGet("/history", async (
            HttpContext http,
            int? weeks,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var userId = UserId(http);
            var w = Math.Clamp(weeks ?? 12, 1, 26);
            var since = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-w * 7));
            var rows = await db.ReadinessHistories.AsNoTracking()
                .Where(h => h.UserId == userId && h.WeekStartDate >= since)
                .OrderBy(h => h.WeekStartDate)
                .Select(h => new ReadinessHistoryResponseDto(
                    h.WeekStartDate,
                    h.Overall,
                    h.Writing,
                    h.Speaking,
                    h.Reading,
                    h.Listening,
                    h.Vocabulary,
                    h.Risk,
                    h.TargetDateProbability))
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        group.MapGet("/blockers", async (
            HttpContext http,
            ReadinessComputationService service,
            CancellationToken ct) =>
        {
            var snapshot = await service.GetOrComputeAsync(UserId(http), ct);
            var payload = JsonSupport.Deserialize<JsonElement>(snapshot.PayloadJson, default);
            var blockers = payload.TryGetProperty("blockers", out var el) && el.ValueKind == JsonValueKind.Array
                ? el.Deserialize<BlockerDto[]>() ?? []
                : [];
            return Results.Ok(blockers);
        });

        group.MapGet("/forecast", async (
            HttpContext http,
            int? hoursPerWeek,
            ReadinessComputationService service,
            ReadinessForecastCalculator forecast,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var userId = UserId(http);
            var snapshot = await service.GetOrComputeAsync(userId, ct);
            var goal = await db.Goals.AsNoTracking().FirstOrDefaultAsync(g => g.UserId == userId, ct);
            var targetDate = goal?.TargetExamDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3));
            var weeksRemaining = Math.Max(0, (int)Math.Ceiling((targetDate.ToDateTime(TimeOnly.MinValue) - DateTime.UtcNow.Date).TotalDays / 7.0));
            var target = ResolveTarget(goal);

            if (hoursPerWeek.HasValue)
            {
                var scenario = forecast.ComputeScenarioOverride(snapshot.OverallReadiness, target, weeksRemaining, hoursPerWeek.Value);
                return Results.Ok(ToForecastResponse(scenario));
            }

            var history = await db.ReadinessHistories.AsNoTracking()
                .Where(h => h.UserId == userId)
                .OrderBy(h => h.WeekStartDate)
                .ToListAsync(ct);
            var result = forecast.Compute(snapshot.OverallReadiness, target, weeksRemaining, history);
            return result is null
                ? Results.Ok(new ReadinessForecastResponse(0m, 0m, weeksRemaining, 0m, 0m, []))
                : Results.Ok(ToForecastResponse(result));
        });

        group.MapPost("/refresh", async (
            HttpContext http,
            ReadinessComputationService service,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var userId = UserId(http);
            var latest = await db.ReadinessSnapshots.AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.ComputedAt)
                .FirstOrDefaultAsync(ct);
            if (latest is not null && (DateTimeOffset.UtcNow - latest.ComputedAt) < ReadinessComputationService.DebounceWindow)
            {
                return Results.StatusCode(429);
            }
            var refreshed = await service.ForceRefreshAsync(userId, ct);
            return Results.Ok(ToResponse(refreshed));
        }).RequireRateLimiting("PerUserWrite");

        group.MapGet("/risk", async (
            HttpContext http,
            ReadinessComputationService service,
            CancellationToken ct) =>
        {
            var snapshot = await service.GetOrComputeAsync(UserId(http), ct);
            var payload = JsonSupport.Deserialize<JsonElement>(snapshot.PayloadJson, default);
            var factors = payload.TryGetProperty("riskFactors", out var el) && el.ValueKind == JsonValueKind.Array
                ? el.Deserialize<RiskFactorResponseDto[]>() ?? []
                : [];
            return Results.Ok(new
            {
                riskProbability = snapshot.TargetDateProbability ?? 0m,
                riskLevel = snapshot.OverallRisk,
                factors,
                recommendation = $"Focus on {snapshot.WeakestSubtest ?? "your weakest sub-test"} and aim for {snapshot.RecommendedStudyHoursPerWeek} hours of study per week."
            });
        });

        return app;
    }

    internal static ReadinessResponse ToResponse(ReadinessSnapshot snapshot)
    {
        var payload = JsonSupport.Deserialize<JsonElement>(snapshot.PayloadJson, default);
        var targetDate = TryGetString(payload, "targetDate") ?? DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var weeksRemaining = TryGetInt(payload, "weeksRemaining") ?? 0;
        var rationale = TryGetString(payload, "recommendedStudyHoursRationale") ?? string.Empty;

        SubtestReadinessDto[] subtests = [];
        if (payload.TryGetProperty("subTests", out var subtestsEl) && subtestsEl.ValueKind == JsonValueKind.Array)
        {
            subtests = subtestsEl.EnumerateArray().Select(el => new SubtestReadinessDto(
                Code: TryGetString(el, "code") ?? TryGetString(el, "id") ?? string.Empty,
                Name: TryGetString(el, "name") ?? string.Empty,
                Current: TryGetDecimal(el, "readiness") ?? 0,
                Target: TryGetDecimal(el, "target") ?? 70,
                Status: TryGetString(el, "status") ?? "Unknown",
                IsWeakest: el.TryGetProperty("isWeakest", out var iw) && iw.ValueKind == JsonValueKind.True,
                ConfidenceBand: TryGetString(el, "confidenceBand") ?? "Low",
                DataPoints: TryGetInt(el, "dataPoints") ?? 0)).ToArray();
        }

        VocabularyReadinessDto vocab = new(0, 100, 0, 600, 0, 0);
        if (payload.TryGetProperty("vocabulary", out var vEl) && vEl.ValueKind == JsonValueKind.Object)
        {
            vocab = new VocabularyReadinessDto(
                Readiness: TryGetDecimal(vEl, "readiness") ?? 0,
                Target: TryGetDecimal(vEl, "target") ?? 100,
                Mastered: TryGetInt(vEl, "mastered") ?? 0,
                MasteryTarget: TryGetInt(vEl, "masteryTarget") ?? 600,
                Accuracy30d: TryGetDecimal(vEl, "accuracy30d") ?? 0,
                DataPoints: TryGetInt(vEl, "dataPoints") ?? 0);
        }

        BlockerDto[] blockers = [];
        if (payload.TryGetProperty("blockers", out var bEl) && bEl.ValueKind == JsonValueKind.Array)
        {
            blockers = bEl.Deserialize<BlockerDto[]>() ?? [];
        }

        RiskFactorResponseDto[] riskFactors = [];
        if (payload.TryGetProperty("riskFactors", out var rfEl) && rfEl.ValueKind == JsonValueKind.Array)
        {
            riskFactors = rfEl.Deserialize<RiskFactorResponseDto[]>() ?? [];
        }

        EvidenceDto evidence = new("no_evidence", 0, 0, 0, 0, string.Empty, snapshot.ComputedAt);
        if (payload.TryGetProperty("evidence", out var eEl) && eEl.ValueKind == JsonValueKind.Object)
        {
            evidence = new EvidenceDto(
                Source: TryGetString(eEl, "source") ?? "no_evidence",
                MocksCompleted: TryGetInt(eEl, "mocksCompleted") ?? 0,
                PracticeQuestions: TryGetInt(eEl, "practiceQuestions") ?? 0,
                ExpertReviews: TryGetInt(eEl, "expertReviews") ?? 0,
                VocabReviewed30d: TryGetInt(eEl, "vocabReviewed30d") ?? 0,
                RecentTrend: TryGetString(eEl, "recentTrend") ?? string.Empty,
                LastUpdated: snapshot.ComputedAt);
        }

        return new ReadinessResponse(
            TargetDate: targetDate,
            WeeksRemaining: weeksRemaining,
            OverallReadiness: snapshot.OverallReadiness,
            OverallRisk: snapshot.OverallRisk,
            TargetDateProbability: snapshot.TargetDateProbability,
            WeakestSubtest: snapshot.WeakestSubtest,
            RecommendedStudyHoursPerWeek: snapshot.RecommendedStudyHoursPerWeek,
            RecommendedStudyHoursRationale: rationale,
            ConfidenceLevel: snapshot.ConfidenceLevel,
            DataPointCount: snapshot.DataPointCount,
            SubTests: subtests,
            Vocabulary: vocab,
            Blockers: blockers,
            RiskFactors: riskFactors,
            Evidence: evidence,
            ComputedAt: snapshot.ComputedAt,
            ExpiresAt: snapshot.ExpiresAt,
            Version: snapshot.Version);
    }

    internal static decimal ResolveTarget(LearnerGoal? goal)
    {
        if (goal is null) return 70m;
        var explicitTargets = new[] { goal.TargetWritingScore, goal.TargetSpeakingScore, goal.TargetReadingScore, goal.TargetListeningScore }
            .Where(x => x.HasValue && x.Value > 0)
            .Select(x => (decimal)x!.Value)
            .ToList();
        if (explicitTargets.Count == 0) return 70m;
        return Math.Clamp(explicitTargets.Average() / 5m, 0m, 100m);
    }

    private static ReadinessForecastResponse ToForecastResponse(ForecastResult result)
        => new(
            Probability: result.Probability,
            WeeksNeeded: result.WeeksNeeded,
            WeeksAvailable: result.WeeksAvailable,
            RequiredImprovement: result.RequiredImprovement,
            SlopePerWeek: result.SlopePerWeek,
            Scenarios: result.Scenarios.Select(s => new ForecastScenarioResponseDto(
                Label: s.Label,
                HoursPerWeek: s.HoursPerWeek,
                ProjectedReadinessAtTarget: s.ProjectedReadinessAtTarget,
                Probability: s.Probability)).ToArray());

    private static string? TryGetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? TryGetInt(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

    private static decimal? TryGetDecimal(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;

    private static string UserId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
