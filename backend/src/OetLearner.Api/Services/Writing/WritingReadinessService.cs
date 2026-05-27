using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing.Events;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingReadinessSnapshot(
    string UserId,
    DateOnly Date,
    int Score,
    decimal MockAverageBand,
    decimal TrajectorySlope,
    decimal CanonCleanRate,
    int TimeMgmtScore,
    int TypeConsistency,
    string? PredictedBandLabel,
    DateTimeOffset ComputedAt);

public interface IWritingReadinessService
{
    Task<WritingReadinessSnapshot> ComputeForUserAsync(string userId, CancellationToken ct);
    Task<WritingReadinessSnapshot?> GetLatestAsync(string userId, CancellationToken ct);
    Task<IReadOnlyList<WritingReadinessSnapshot>> GetHistoryAsync(string userId, int days, CancellationToken ct);

    Task<WritingReadinessResponseV2> GetReadinessAsync(string userId, CancellationToken ct);
}

public sealed class WritingReadinessService(
    LearnerDbContext db,
    TimeProvider clock,
    IWritingEventBus events,
    ILogger<WritingReadinessService> logger) : IWritingReadinessService
{
    public async Task<WritingReadinessSnapshot> ComputeForUserAsync(string userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var date = DateOnly.FromDateTime(now.UtcDateTime);

        var lastMocks = await db.WritingMockSessions.AsNoTracking()
            .Where(m => m.UserId == userId && m.Status == "submitted" && m.SubmissionId != null)
            .Join(db.WritingSubmissions.AsNoTracking(), m => m.SubmissionId!.Value, s => s.Id, (m, s) => new { m, s })
            .Where(x => x.s.UserId == userId && x.s.Mode == "mock")
            .OrderByDescending(x => x.m.SubmittedAt)
            .Take(3)
            .Select(x => new { x.m.SubmissionId, x.m.SubmittedAt, x.m.ReadingPhaseEndedAt, x.m.StartedAt })
            .ToListAsync(ct);

        var submissionIds = lastMocks.Select(m => m.SubmissionId!.Value).ToList();
        var grades = submissionIds.Count == 0
            ? new List<WritingGrade>()
            : await db.WritingGrades.AsNoTracking()
                .Where(g => submissionIds.Contains(g.SubmissionId))
                .ToListAsync(ct);

        var mockAvg = grades.Count == 0 ? 0m : (decimal)Math.Round(grades.Average(g => g.RawTotal), 2);
        var trajectory = CalculateTrajectory(grades);
        var canonClean = await CalculateCanonCleanRateAsync(userId, ct);
        var timeMgmt = CalculateTimeMgmt(lastMocks.Select(m => new MockTime(m.StartedAt, m.ReadingPhaseEndedAt, m.SubmittedAt)).ToList());
        var typeConsistency = await CalculateTypeConsistencyAsync(userId, ct);

        var score = ComputeBlendedScore(mockAvg, trajectory, canonClean, timeMgmt, typeConsistency);
        var predicted = PredictBandLabel(mockAvg);

        var entity = await db.WritingReadinessScores.FirstOrDefaultAsync(s => s.UserId == userId && s.Date == date, ct);
        if (entity is null)
        {
            entity = new WritingReadinessScore
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Date = date,
            };
            db.WritingReadinessScores.Add(entity);
        }
        var previousScore = entity.Score;
        entity.Score = score;
        entity.MockAverageBand = mockAvg;
        entity.TrajectorySlope = trajectory;
        entity.CanonCleanRate = canonClean;
        entity.TimeMgmtScore = timeMgmt;
        entity.TypeConsistency = typeConsistency;
        entity.PredictedBandLabel = predicted;
        entity.ComputedAt = now;

        var profile = await db.LearnerWritingProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is not null)
        {
            profile.CurrentReadinessScore = score;
            profile.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);

        if (score >= 80 && previousScore < 80)
        {
            try
            {
                await events.PublishAsync(new WritingReadinessGreenLight(userId, score, predicted, now), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Writing readiness green-light event failed for user {UserId}.", userId);
            }
        }

        return new WritingReadinessSnapshot(userId, date, score, mockAvg, trajectory, canonClean, timeMgmt, typeConsistency, predicted, now);
    }

    public async Task<WritingReadinessSnapshot?> GetLatestAsync(string userId, CancellationToken ct)
    {
        var entity = await db.WritingReadinessScores.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.Date)
            .FirstOrDefaultAsync(ct);
        return entity is null ? null : ToSnapshot(entity);
    }

    public async Task<IReadOnlyList<WritingReadinessSnapshot>> GetHistoryAsync(string userId, int days, CancellationToken ct)
    {
        var cutoff = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddDays(-Math.Clamp(days, 1, 365)));
        var rows = await db.WritingReadinessScores.AsNoTracking()
            .Where(s => s.UserId == userId && s.Date >= cutoff)
            .OrderBy(s => s.Date)
            .ToListAsync(ct);
        return rows.Select(ToSnapshot).ToList();
    }

    private static WritingReadinessSnapshot ToSnapshot(WritingReadinessScore entity)
        => new(entity.UserId, entity.Date, entity.Score,
            entity.MockAverageBand ?? 0m,
            entity.TrajectorySlope ?? 0m,
            entity.CanonCleanRate ?? 0m,
            entity.TimeMgmtScore ?? 0,
            entity.TypeConsistency ?? 0,
            entity.PredictedBandLabel,
            entity.ComputedAt);

    private static int ComputeBlendedScore(decimal mockAvg, decimal trajectory, decimal canonClean, int timeMgmt, int typeConsistency)
    {
        var mockComponent = Math.Clamp((double)mockAvg / 38.0 * 100, 0, 100) * 0.5;
        var trajectoryComponent = Math.Clamp(((double)trajectory + 1.0) / 2.0 * 100, 0, 100) * 0.2;
        var canonComponent = Math.Clamp((double)canonClean * 100.0, 0, 100) * 0.15;
        var timeComponent = Math.Clamp(timeMgmt, 0, 100) * 0.1;
        var typeComponent = Math.Clamp(typeConsistency, 0, 100) * 0.05;
        var raw = mockComponent + trajectoryComponent + canonComponent + timeComponent + typeComponent;
        return Math.Clamp((int)Math.Round(raw), 0, 100);
    }

    private static decimal CalculateTrajectory(IReadOnlyList<WritingGrade> grades)
    {
        if (grades.Count < 2) return 0m;
        var ordered = grades.OrderBy(g => g.GradedAt).ToList();
        var n = ordered.Count;
        var sumX = 0.0;
        var sumY = 0.0;
        var sumXY = 0.0;
        var sumXX = 0.0;
        for (var i = 0; i < n; i++)
        {
            sumX += i;
            sumY += ordered[i].RawTotal;
            sumXY += i * ordered[i].RawTotal;
            sumXX += i * i;
        }
        var denom = n * sumXX - sumX * sumX;
        if (denom == 0) return 0m;
        var slope = (n * sumXY - sumX * sumY) / denom;
        return (decimal)Math.Round(Math.Clamp(slope / 5.0, -1.0, 1.0), 2);
    }

    private async Task<decimal> CalculateCanonCleanRateAsync(string userId, CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddDays(-30);
        var recent = await db.WritingSubmissions.AsNoTracking()
            .Where(s => s.UserId == userId && s.SubmittedAt >= since && s.Status == "graded")
            .OrderByDescending(s => s.SubmittedAt)
            .Take(10)
            .Select(s => s.Id)
            .ToListAsync(ct);
        if (recent.Count == 0) return 0m;
        var clean = 0;
        foreach (var id in recent)
        {
            var count = await db.WritingCanonViolations.AsNoTracking().CountAsync(v => v.SubmissionId == id, ct);
            if (count <= 2) clean++;
        }
        return (decimal)Math.Round(clean / (double)recent.Count, 2);
    }

    private static int CalculateTimeMgmt(IReadOnlyList<MockTime> mocks)
    {
        if (mocks.Count == 0) return 0;
        var withinBudget = 0;
        foreach (var m in mocks)
        {
            if (m.SubmittedAt is null) continue;
            var writingPhase = m.ReadingPhaseEndedAt is { } reading ? (m.SubmittedAt.Value - reading).TotalMinutes : (m.SubmittedAt.Value - m.StartedAt).TotalMinutes;
            if (writingPhase <= 40) withinBudget++;
        }
        return (int)Math.Round(100.0 * withinBudget / mocks.Count);
    }

    private async Task<int> CalculateTypeConsistencyAsync(string userId, CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddDays(-30);
        var grades = await db.WritingGrades.AsNoTracking()
            .Join(db.WritingSubmissions.AsNoTracking(), g => g.SubmissionId, s => s.Id, (g, s) => new { g, s })
            .Join(db.WritingScenarios.AsNoTracking(), x => x.s.ScenarioId, s => s.Id, (x, scenario) => new { x.g.RawTotal, scenario.LetterType, x.s.SubmittedAt, x.s.UserId })
            .Where(x => x.UserId == userId && x.SubmittedAt >= since)
            .ToListAsync(ct);
        if (grades.Count < 2) return 50;
        var byType = grades.GroupBy(g => g.LetterType).Select(g => g.Average(x => (double)x.RawTotal)).ToList();
        if (byType.Count < 2) return 80;
        var mean = byType.Average();
        var variance = byType.Sum(b => Math.Pow(b - mean, 2)) / byType.Count;
        var penalty = Math.Min(50, variance);
        return Math.Clamp(100 - (int)Math.Round(penalty * 2), 0, 100);
    }

    private static string? PredictBandLabel(decimal mockAverage)
    {
        if (mockAverage >= 38) return "A";
        if (mockAverage >= 34) return "B+";
        if (mockAverage >= 30) return "B";
        if (mockAverage >= 24) return "C+";
        if (mockAverage >= 18) return "C";
        return null;
    }

    private readonly record struct MockTime(DateTimeOffset StartedAt, DateTimeOffset? ReadingPhaseEndedAt, DateTimeOffset? SubmittedAt);

    public async Task<WritingReadinessResponseV2> GetReadinessAsync(string userId, CancellationToken ct)
    {
        var snap = await GetLatestAsync(userId, ct) ?? await ComputeForUserAsync(userId, ct);
        var history = await GetHistoryAsync(userId, 14, ct);
        int? delta = null;
        if (history.Count >= 2)
        {
            var prior = history[history.Count - 2].Score;
            delta = snap.Score - prior;
        }
        var subScores = new WritingReadinessSubScoresResponse(
            MockAverage: (int)Math.Round((double)snap.MockAverageBand),
            Trajectory: (int)Math.Round((double)snap.TrajectorySlope * 100),
            CanonCleanRate: (int)Math.Round((double)snap.CanonCleanRate * 100),
            TimeMgmt: snap.TimeMgmtScore,
            TypeConsistency: snap.TypeConsistency);
        return new WritingReadinessResponseV2(
            Date: snap.Date.ToString("O"),
            Score: snap.Score,
            SubScores: subScores,
            PredictedBand: snap.PredictedBandLabel,
            DeltaVsLastWeek: delta,
            ComputedAt: snap.ComputedAt);
    }
}
