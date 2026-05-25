using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using System.Text.Json;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Skill Scoring Service — Reading Module Pathway WS2
//
// Maintains per-user per-skill mastery scores (S1..S8) using a rolling
// weighted average.  Diagnostic baseline is snapshotted separately so the
// UI can display "progress since diagnostic" deltas.
//
// Design note: ReadingQuestion.Id is a string PK while ReadingQuestionAttempt
// .ReadingQuestionId is a Guid, so the two tables cannot be joined directly in
// EF Core.  SkillTag information is instead carried through the practice
// session's MetadataJson as a {stringQuestionId -> skillTag} map, written at
// session-creation time by ReadingLearnerPathwayService.
// ═════════════════════════════════════════════════════════════════════════════

public interface ISkillScoringService
{
    Task UpdateSkillScoresAsync(string userId, Guid practiceSessionId, CancellationToken ct);
    Task<Dictionary<string, decimal>> GetCurrentScoresAsync(string userId, CancellationToken ct);
    Task<string> GetWeakestSkillAsync(string userId, CancellationToken ct);
    Task RecalculateDiagnosticBaselineAsync(string userId, Guid sessionId, CancellationToken ct);
}

public sealed class SkillScoringService(LearnerDbContext db) : ISkillScoringService
{
    // The 8 OET Reading sub-skills — S1..S8
    private static readonly string[] AllSkillCodes = ["S1", "S2", "S3", "S4", "S5", "S6", "S7", "S8"];

    /// <summary>
    /// Update skill scores from a completed practice session.
    /// Reads the skill-tag map from session MetadataJson (key "skillTagMap"),
    /// then groups attempts by skill and applies a rolling weighted average.
    /// </summary>
    public async Task UpdateSkillScoresAsync(string userId, Guid practiceSessionId, CancellationToken ct)
    {
        var session = await db.ReadingPracticeSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == practiceSessionId && s.UserId == userId, ct);

        if (session is null) return;

        // Attempt rows for this session
        var attempts = await db.ReadingQuestionAttempts
            .AsNoTracking()
            .Where(a => a.PracticeSessionId == practiceSessionId && a.UserId == userId)
            .ToListAsync(ct);

        if (attempts.Count == 0) return;

        // Skill-tag map is stored in MetadataJson as {"skillTagMap": {"<attemptRowId>": "S1", ...}}
        // Keys are the ReadingQuestionAttempt.Id (Guid) serialised as strings.
        var skillTagMap = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(session.MetadataJson) && session.MetadataJson != "{}")
        {
            try
            {
                using var doc = JsonDocument.Parse(session.MetadataJson);
                if (doc.RootElement.TryGetProperty("skillTagMap", out var mapEl)
                    && mapEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in mapEl.EnumerateObject())
                        skillTagMap[prop.Name] = prop.Value.GetString() ?? "S1";
                }
            }
            catch (JsonException) { /* corrupt metadata — fall back to S1 for all */ }
        }

        // Group attempts by skill code
        var skillResults = attempts
            .GroupBy(a => skillTagMap.TryGetValue(a.Id.ToString(), out var tag) ? tag : "S1")
            .ToDictionary(
                g => g.Key,
                g => (
                    Accuracy: (decimal)g.Count(a => a.IsCorrect) / g.Count(),
                    Attempted: g.Count(),
                    Correct: g.Count(a => a.IsCorrect)
                ));

        var existingScores = await db.LearnerSkillScores
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var (skill, stats) in skillResults)
        {
            decimal newRawScore = stats.Accuracy * 10m; // 0–10 scale

            var existing = existingScores.FirstOrDefault(s => s.SkillCode == skill);
            if (existing is null)
            {
                db.LearnerSkillScores.Add(new LearnerSkillScore
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SkillCode = skill,
                    CurrentScore = newRawScore,
                    DiagnosticScore = 0m,
                    QuestionsAttempted = stats.Attempted,
                    QuestionsCorrect = stats.Correct,
                    LastPracticedAt = now,
                    UpdatedAt = now
                });
            }
            else
            {
                // Rolling weighted average: recent session counts 2x
                existing.CurrentScore = (existing.CurrentScore + newRawScore * 2m) / 3m;
                existing.QuestionsAttempted += stats.Attempted;
                existing.QuestionsCorrect += stats.Correct;
                existing.LastPracticedAt = now;
                existing.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<Dictionary<string, decimal>> GetCurrentScoresAsync(string userId, CancellationToken ct)
    {
        var scores = await db.LearnerSkillScores
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .ToDictionaryAsync(s => s.SkillCode, s => s.CurrentScore, ct);

        // Ensure all 8 skills are present; default 5.0 for untested
        foreach (var code in AllSkillCodes)
            scores.TryAdd(code, 5.0m);

        return scores;
    }

    public async Task<string> GetWeakestSkillAsync(string userId, CancellationToken ct)
    {
        var scores = await GetCurrentScoresAsync(userId, ct);
        return scores.MinBy(kvp => kvp.Value).Key ?? "S1";
    }

    /// <summary>
    /// Update scores from the diagnostic session, then copy the current
    /// scores into the DiagnosticScore baseline field.
    /// </summary>
    public async Task RecalculateDiagnosticBaselineAsync(string userId, Guid sessionId, CancellationToken ct)
    {
        await UpdateSkillScoresAsync(userId, sessionId, ct);

        var scores = await db.LearnerSkillScores
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);

        foreach (var score in scores)
            score.DiagnosticScore = score.CurrentScore;

        await db.SaveChangesAsync(ct);
    }
}
