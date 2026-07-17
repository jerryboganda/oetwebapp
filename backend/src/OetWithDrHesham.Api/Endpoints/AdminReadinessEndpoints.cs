using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Services.Readiness;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// Admin oversight endpoints for the Readiness module. Surfaces the
/// learners at risk for intervention, drill-down per learner with a
/// reasoning trace, manual recompute, and platform-wide aggregate metrics.
/// </summary>
public static class AdminReadinessEndpoints
{
    public static IEndpointRouteBuilder MapAdminReadinessEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin/readiness")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        admin.MapGet("/learners", async (
            string? risk,
            int? page,
            int? pageSize,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var p = Math.Max(1, page ?? 1);
            var ps = Math.Clamp(pageSize ?? 25, 1, 100);
            IQueryable<Domain.ReadinessSnapshot> baseQuery = db.ReadinessSnapshots.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(risk))
            {
                baseQuery = baseQuery.Where(s => s.OverallRisk == risk);
            }

            var total = await baseQuery.CountAsync(ct);
            var pageItems = await baseQuery
                .OrderByDescending(s => s.ComputedAt)
                .Skip((p - 1) * ps)
                .Take(ps)
                .ToListAsync(ct);

            var userIds = pageItems.Select(x => x.UserId).Distinct().ToList();
            var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
            var goals = await db.Goals.AsNoTracking().Where(g => userIds.Contains(g.UserId)).ToDictionaryAsync(g => g.UserId, ct);

            var rows = pageItems.Select(s => new AdminReadinessLearnerRow(
                UserId: s.UserId,
                DisplayName: users.TryGetValue(s.UserId, out var u) ? u.DisplayName ?? u.Id : s.UserId,
                TargetExamDate: goals.TryGetValue(s.UserId, out var g) ? g.TargetExamDate : null,
                OverallReadiness: s.OverallReadiness,
                OverallRisk: s.OverallRisk,
                WeakestSubtest: s.WeakestSubtest,
                TargetDateProbability: s.TargetDateProbability,
                ComputedAt: s.ComputedAt,
                ExpiresAt: s.ExpiresAt)).ToArray();

            return Results.Ok(new AdminReadinessLearnerListResponse(p, ps, total, rows));
        }).WithAdminRead("AdminLearnerRead");

        admin.MapGet("/learners/{userId}", async (
            string userId,
            ReadinessComputationService service,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var snapshot = await service.GetOrComputeAsync(userId, ct);
            var history = await db.ReadinessHistories.AsNoTracking()
                .Where(h => h.UserId == userId)
                .OrderBy(h => h.WeekStartDate)
                .ToListAsync(ct);
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
            var goal = await db.Goals.AsNoTracking().FirstOrDefaultAsync(g => g.UserId == userId, ct);

            return Results.Ok(new
            {
                userId,
                displayName = user?.DisplayName,
                targetExamDate = goal?.TargetExamDate,
                snapshot = ReadinessEndpoints.ToResponse(snapshot),
                history = history.Select(h => new ReadinessHistoryResponseDto(
                    h.WeekStartDate,
                    h.Overall,
                    h.Writing,
                    h.Speaking,
                    h.Reading,
                    h.Listening,
                    h.Vocabulary,
                    h.Risk,
                    h.TargetDateProbability)),
                reasoningTrace = BuildReasoningTrace(snapshot, history)
            });
        }).WithAdminRead("AdminLearnerRead");

        admin.MapPost("/learners/{userId}/recompute", async (
            string userId,
            ReadinessComputationService service,
            CancellationToken ct) =>
        {
            var snapshot = await service.ForceRefreshAsync(userId, ct);
            return Results.Ok(ReadinessEndpoints.ToResponse(snapshot));
        }).WithAdminWrite("AdminLearnerWrite");

        admin.MapGet("/metrics", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var staleCutoff = DateTimeOffset.UtcNow.AddHours(-48);
            var snapshots = await db.ReadinessSnapshots.AsNoTracking().ToListAsync(ct);
            var latestByUser = snapshots
                .GroupBy(s => s.UserId)
                .Select(g => g.OrderByDescending(s => s.ComputedAt).First())
                .ToList();

            decimal avg(Func<Domain.ReadinessSnapshot, decimal> selector)
                => latestByUser.Count == 0 ? 0m : Math.Round(latestByUser.Average(selector), 2);

            return Results.Ok(new AdminReadinessMetricsResponse(
                LearnersWithSnapshot: latestByUser.Count,
                HighRisk: latestByUser.Count(s => s.OverallRisk == "High"),
                ModerateRisk: latestByUser.Count(s => s.OverallRisk == "Moderate"),
                LowRisk: latestByUser.Count(s => s.OverallRisk == "Low"),
                UnknownRisk: latestByUser.Count(s => s.OverallRisk == "Unknown"),
                InterventionCandidates: latestByUser.Count(s => s.TargetDateProbability < 50m),
                StaleSnapshots: latestByUser.Count(s => s.ComputedAt < staleCutoff),
                AvgWriting: avg(s => s.WritingReadiness),
                AvgSpeaking: avg(s => s.SpeakingReadiness),
                AvgReading: avg(s => s.ReadingReadiness),
                AvgListening: avg(s => s.ListeningReadiness),
                AvgVocabulary: avg(s => s.VocabularyReadiness),
                AvgOverall: avg(s => s.OverallReadiness),
                GeneratedAt: DateTimeOffset.UtcNow));
        }).WithAdminRead("AdminLearnerRead");

        return app;
    }

    private static string BuildReasoningTrace(Domain.ReadinessSnapshot snapshot, IReadOnlyList<Domain.ReadinessHistory> history)
    {
        var parts = new List<string>
        {
            $"Overall readiness {snapshot.OverallReadiness} ({snapshot.OverallRisk} risk), confidence {snapshot.ConfidenceLevel}, {snapshot.DataPointCount} data points."
        };
        if (snapshot.WeakestSubtest is not null)
        {
            parts.Add($"Weakest sub-test: {snapshot.WeakestSubtest}.");
        }
        if (snapshot.TargetDateProbability.HasValue)
        {
            parts.Add($"Target-date probability: {snapshot.TargetDateProbability.Value:F1}%.");
        }
        if (history.Count >= 2)
        {
            var first = history.First().Overall;
            var last = history.Last().Overall;
            var delta = last - first;
            parts.Add($"History trend over {history.Count} weeks: delta {delta:+0.0;-0.0;0}.");
        }
        return string.Join(" ", parts);
    }
}
