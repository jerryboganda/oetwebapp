using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>Elo-based adaptive difficulty engine for content selection and skill tracking.</summary>
public class AdaptiveDifficultyService(LearnerDbContext db)
{
    private const double KFactor = 32.0;
    private const double DefaultRating = 1500.0;

    // ── Skill profile ────────────────────────────────────────────────────

    public async Task<object> GetSkillProfileAsync(string userId, string? examTypeCode, CancellationToken ct)
    {
        var query = db.LearnerSkillProfiles.Where(p => p.UserId == userId);
        if (!string.IsNullOrEmpty(examTypeCode))
            query = query.Where(p => p.ExamTypeCode == examTypeCode);

        var profiles = await query.OrderBy(p => p.ExamTypeCode).ThenBy(p => p.SubtestCode).ToListAsync(ct);
        return profiles.Select(MapProfile);
    }

    public async Task UpdateSkillRatingAsync(string userId, string examTypeCode, string subtestCode, string? criterionCode, bool correct, CancellationToken ct)
    {
        var profile = await GetOrCreateProfileAsync(userId, examTypeCode, subtestCode, criterionCode, ct);

        var outcome = correct ? 1.0 : 0.0;
        var expected = 1.0 / (1 + Math.Pow(10, (DefaultRating - profile.CurrentRating) / 400.0));
        var delta = KFactor * (outcome - expected);

        var newRating = Math.Max(100, Math.Min(3000, profile.CurrentRating + delta));

        // Track recent scores for trend analysis
        var recentScores = TryDeserializeScores(profile.RecentScoresJson);
        recentScores.Add(correct ? 1 : 0);
        if (recentScores.Count > 20) recentScores.RemoveAt(0);

        profile.CurrentRating = newRating;
        profile.EvidenceCount++;
        profile.ConfidenceLevel = Math.Min(100, profile.EvidenceCount * 5);
        profile.RecentScoresJson = JsonSupport.Serialize(recentScores);
        profile.LastUpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    // ── Content selection ─────────────────────────────────────────────────

    public async Task<object> GetAdaptiveContentAsync(string userId, string examTypeCode, string subtestCode, int count, CancellationToken ct)
    {
        var profile = await db.LearnerSkillProfiles.FirstOrDefaultAsync(
            p => p.UserId == userId && p.ExamTypeCode == examTypeCode && p.SubtestCode == subtestCode && p.CriterionCode == null, ct);

        var targetRating = profile?.CurrentRating ?? DefaultRating;
        var tolerance = 200;

        var items = await db.ContentItems
            .Where(c => c.ExamTypeCode == examTypeCode && c.SubtestCode == subtestCode && c.Status == ContentStatus.Published)
            .Where(c => c.DifficultyRating >= targetRating - tolerance && c.DifficultyRating <= targetRating + tolerance)
            .OrderBy(_ => Guid.NewGuid())
            .Take(count)
            .ToListAsync(ct);

        // Fall back to any content if not enough in range
        if (items.Count < count)
        {
            var fallback = await db.ContentItems
                .Where(c => c.ExamTypeCode == examTypeCode && c.SubtestCode == subtestCode && c.Status == ContentStatus.Published)
                .OrderBy(_ => Guid.NewGuid())
                .Take(count - items.Count)
                .ToListAsync(ct);
            items.AddRange(fallback);
        }

        return items.Select(c => new
        {
            id = c.Id,
            subtestCode = c.SubtestCode,
            difficulty = c.DifficultyRating,
            title = c.Title,
            estimatedMinutes = c.EstimatedDurationMinutes
        });
    }

    // ── Update content difficulty after evaluation ─────────────────────────

    public async Task UpdateContentDifficultyAsync(string contentId, bool learnerSucceeded, double learnerRating, CancellationToken ct)
    {
        var content = await db.ContentItems.FindAsync([contentId], ct);
        if (content == null) return;

        var outcome = learnerSucceeded ? 0.0 : 1.0; // If learner succeeded, content is "weaker"
        var expected = 1.0 / (1 + Math.Pow(10, (learnerRating - content.DifficultyRating) / 400.0));
        var delta = KFactor * (outcome - expected);

        content.DifficultyRating = (int)Math.Max(100, Math.Min(3000, content.DifficultyRating + delta));
        await db.SaveChangesAsync(ct);
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private async Task<LearnerSkillProfile> GetOrCreateProfileAsync(string userId, string examTypeCode, string subtestCode, string? criterionCode, CancellationToken ct)
    {
        var criterionKey = criterionCode ?? string.Empty;
        var profile = await db.LearnerSkillProfiles.FirstOrDefaultAsync(
            p => p.UserId == userId && p.ExamTypeCode == examTypeCode && p.SubtestCode == subtestCode && p.CriterionCode == criterionKey, ct);

        if (profile != null) return profile;

        profile = new LearnerSkillProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ExamTypeCode = examTypeCode,
            SubtestCode = subtestCode,
            CriterionCode = criterionKey,
            CurrentRating = DefaultRating,
            ConfidenceLevel = 0,
            EvidenceCount = 0,
            RecentScoresJson = "[]",
            LastUpdatedAt = DateTimeOffset.UtcNow
        };
        db.LearnerSkillProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
        return profile;
    }

    private static List<int> TryDeserializeScores(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<List<int>>(json) ?? []; }
        catch { return []; }
    }

    private static object MapProfile(LearnerSkillProfile p) => new
    {
        examTypeCode = p.ExamTypeCode,
        subtestCode = p.SubtestCode,
        criterionCode = p.CriterionCode,
        currentRating = Math.Round(p.CurrentRating, 1),
        confidenceLevel = p.ConfidenceLevel,
        evidenceCount = p.EvidenceCount,
        lastUpdatedAt = p.LastUpdatedAt
    };
}
