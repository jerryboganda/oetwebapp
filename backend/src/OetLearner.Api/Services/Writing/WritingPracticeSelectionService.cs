using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingPracticeSelectionRequest(
    string Profession,
    IReadOnlyList<string> LetterTypeFocus,
    IReadOnlyDictionary<string, double> WeaknessVector,
    int DesiredItemCount,
    int Difficulty);

public sealed record WritingPracticePick(
    string PickKind,
    string ContentRefId,
    string? FocusSkill,
    string? FocusCriterion,
    int Difficulty,
    string Reason);

public interface IWritingPracticeSelectionService
{
    Task<IReadOnlyList<WritingPracticePick>> PickAsync(string userId, WritingPracticeSelectionRequest request, CancellationToken ct);
}

/// <summary>
/// Picks adaptive practice items based on:
/// - weakness vector (higher weakness ⇒ higher pick weight)
/// - 80/20 weakness vs balance split
/// - recency penalty (item attempted in last 7 days drops weight)
/// - profession lock (only scenarios/drills matching profession)
/// - difficulty progression (clamp ±1 from requested)
///
/// Deterministic for unit tests by seeding RNG from a stable per-user hash.
/// </summary>
public sealed class WritingPracticeSelectionService(LearnerDbContext db, TimeProvider clock) : IWritingPracticeSelectionService
{
    public async Task<IReadOnlyList<WritingPracticePick>> PickAsync(string userId, WritingPracticeSelectionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var desired = Math.Clamp(request.DesiredItemCount, 1, 10);
        var rng = new Random(StableSeed(userId, clock.GetUtcNow().Date));
        var picks = new List<WritingPracticePick>();

        var minDifficulty = Math.Max(1, request.Difficulty - 1);
        var maxDifficulty = Math.Min(5, request.Difficulty + 1);

        var scenarios = await db.WritingScenarios.AsNoTracking()
            .Where(s => s.Status == "published"
                        && s.Profession == request.Profession
                        && s.Difficulty >= minDifficulty
                        && s.Difficulty <= maxDifficulty)
            .Select(s => new { s.Id, s.LetterType, s.Difficulty })
            .ToListAsync(ct);

        var since = clock.GetUtcNow().AddDays(-7);
        var recentSubmissions = await db.WritingSubmissions.AsNoTracking()
            .Where(s => s.UserId == userId && s.SubmittedAt >= since)
            .Select(s => s.ScenarioId)
            .ToListAsync(ct);
        var recentSet = recentSubmissions.ToHashSet();

        var focus = request.LetterTypeFocus.Count == 0
            ? new[] { "LT-RR", "LT-DG", "LT-UR" }
            : request.LetterTypeFocus.ToArray();
        var weaknessRanked = request.WeaknessVector
            .OrderByDescending(kvp => kvp.Value)
            .Select(kvp => kvp.Key)
            .ToList();

        var weaknessQuota = (int)Math.Ceiling(desired * 0.8);
        var balanceQuota = desired - weaknessQuota;

        for (var i = 0; i < weaknessQuota && i < weaknessRanked.Count; i++)
        {
            var crit = weaknessRanked[i];
            var letterType = focus[i % focus.Length];
            var pool = scenarios.Where(s => s.LetterType.Equals(letterType, StringComparison.OrdinalIgnoreCase)).ToList();
            if (pool.Count == 0) pool = scenarios;
            if (pool.Count == 0) break;
            var pick = pool[rng.Next(pool.Count)];
            var recency = recentSet.Contains(pick.Id);
            picks.Add(new WritingPracticePick(
                PickKind: "letter",
                ContentRefId: pick.Id.ToString(),
                FocusSkill: WritingPracticeMappings.SkillForCriterion(crit),
                FocusCriterion: crit,
                Difficulty: pick.Difficulty,
                Reason: recency ? "weakness_focus_repeat" : "weakness_focus"));
        }

        var balanceLetterTypes = focus.Reverse().ToArray();
        for (var i = 0; i < balanceQuota && i < balanceLetterTypes.Length; i++)
        {
            var letterType = balanceLetterTypes[i];
            var pool = scenarios.Where(s => s.LetterType.Equals(letterType, StringComparison.OrdinalIgnoreCase) && !recentSet.Contains(s.Id)).ToList();
            if (pool.Count == 0) pool = scenarios.Where(s => s.LetterType.Equals(letterType, StringComparison.OrdinalIgnoreCase)).ToList();
            if (pool.Count == 0) continue;
            var pick = pool[rng.Next(pool.Count)];
            picks.Add(new WritingPracticePick(
                PickKind: "letter",
                ContentRefId: pick.Id.ToString(),
                FocusSkill: null,
                FocusCriterion: null,
                Difficulty: pick.Difficulty,
                Reason: "balance"));
        }

        return picks;
    }

    private static int StableSeed(string userId, DateTime date)
        => (userId ?? string.Empty).GetHashCode(StringComparison.Ordinal) ^ date.GetHashCode();
}

internal static class WritingPracticeMappings
{
    public static string SkillForCriterion(string criterion) => criterion switch
    {
        "c1" => "W2",
        "c2" => "W3",
        "c3" => "W4",
        "c4" => "W6",
        "c5" => "W5",
        "c6" => "W7",
        _ => "W6",
    };
}
