using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OetWithDrHesham.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Practice Selection Service — WS3
//
// Adaptive question selection for drill, wrong-answer review, and mock modes
// (spec §8.2).
//
// TYPE NOTE: ReadingQuestion.Id is a string PK (max 64 chars).
// ReadingQuestionAttempt.ReadingQuestionId is Guid.  Pathway-generation code
// carries the mapping in ReadingPracticeSession.MetadataJson.
//
// SelectQuestionsForDrillAsync and SelectWrongAnswerReviewQueueAsync return
// List<string> (string question IDs from ReadingQuestion.Id) because the drill
// pipeline stores those string IDs in ReadingPracticeSession.QuestionIdsJson.
// SelectMockQuestionsAsync still deals with Guid-domain IDs from mock templates.
//
// Difficulty filtering: ReadingQuestion has no numeric DifficultyScore column
// in the current schema — the difficulty-band logic is annotated as a no-op
// and will be activated once the column is added via migration.
// ═════════════════════════════════════════════════════════════════════════════

public interface IPracticeSelectionService
{
    /// <summary>
    /// Select up to <paramref name="targetCount"/> published questions for a
    /// targeted drill on <paramref name="focusSkill"/>.  Questions seen in the
    /// last 14 days are excluded; previously-wrong questions are weighted 3×.
    /// Returns string IDs from <see cref="ReadingQuestion.Id"/>.
    /// </summary>
    Task<List<string>> SelectQuestionsForDrillAsync(
        string userId,
        string focusSkill,
        int targetCount,
        CancellationToken ct);

    /// <summary>
    /// Return unresolved wrong-answer review question IDs.
    /// Ordered by most recently missed, then highest miss count.
    /// </summary>
    Task<List<string>> SelectWrongAnswerReviewQueueAsync(
        string userId,
        int targetCount,
        CancellationToken ct);

    /// <summary>
    /// Return the ordered Guid question list from the given mock template.
    /// </summary>
    Task<List<Guid>> SelectMockQuestionsAsync(
        string userId,
        Guid mockTemplateId,
        CancellationToken ct);
}

public sealed class PracticeSelectionService(LearnerDbContext db) : IPracticeSelectionService
{
    private const int SeenRecentlyDays = 14;

    // ═══════════════════════════════════════════════════════════════════════
    // SelectQuestionsForDrillAsync  — spec §8.2 adaptive algorithm
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<List<string>> SelectQuestionsForDrillAsync(
        string userId,
        string focusSkill,
        int targetCount,
        CancellationToken ct)
    {
        // ── 1. Get current skill score (0-10 scale) for difficulty banding ─
        var skillScore = await db.LearnerSkillScores
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.SkillCode == focusSkill)
            .Select(s => (decimal?)s.CurrentScore)
            .FirstOrDefaultAsync(ct) ?? 5m;

        // Target difficulty band: [score-1, score+1].
        // NOTE: ReadingQuestion has no DifficultyScore column in the current
        // schema — difficulty filtering is a planned extension.  The query
        // currently selects all published questions for the skill.
        var diffLow  = Math.Max(0m, skillScore - 1);
        var diffHigh = Math.Min(10m, skillScore + 1);
        _ = (diffLow, diffHigh); // suppress unused-var warning until column is added

        // ── 2. Exclude questions seen in the last 14 days ─────────────────
        var recentCutoff = DateTimeOffset.UtcNow.AddDays(-SeenRecentlyDays);

        // ReadingQuestionAttempt uses the stable Guid bridge for ReadingQuestion.Id.
        var recentQuestionIds = await db.ReadingQuestionAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.AttemptedAt >= recentCutoff)
            .Select(a => a.ReadingQuestionId)
            .Distinct()
            .ToListAsync(ct);
        var recentSet = recentQuestionIds.ToHashSet();

        // ── 3. Fetch candidate questions ──────────────────────────────────
        var candidates = await db.ReadingQuestions
            .AsNoTracking()
            .Where(q => q.SkillTag == focusSkill
                    && q.ReviewState == ReadingReviewState.Published)
            .Select(q => q.Id)
            .ToListAsync(ct);
        candidates = candidates
            .Where(id => !recentSet.Contains(StableGuidFromQuestionId(id)))
            .ToList();

        // Fallback: relax recency constraint if pool is empty
        if (candidates.Count == 0)
        {
            candidates = await db.ReadingQuestions
                .AsNoTracking()
                .Where(q => q.SkillTag == focusSkill
                    && q.ReviewState == ReadingReviewState.Published)
                .Select(q => q.Id)
                .ToListAsync(ct);
        }

        if (candidates.Count == 0) return [];

        // ── 4. Identify previously-wrong questions for 3× weighting ──────
        var wrongIdsList = await db.ReadingErrorBankEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId && !e.IsResolved)
            .Select(e => e.ReadingQuestionId)  // string
            .ToListAsync(ct);
        var wrongIds = wrongIdsList.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ── 5. Build weighted pool (wrong-answer questions appear 3×) ─────
        var pool = new List<string>(candidates.Count * 2);
        foreach (var id in candidates)
        {
            pool.Add(id);
            if (wrongIds.Contains(id))
            {
                pool.Add(id);
                pool.Add(id);
            }
        }

        Shuffle(pool);

        // De-duplicate while preserving weighted-first ordering
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(targetCount);
        foreach (var id in pool)
        {
            if (seen.Add(id))
            {
                result.Add(id);
                if (result.Count == targetCount) break;
            }
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SelectWrongAnswerReviewQueueAsync
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<List<string>> SelectWrongAnswerReviewQueueAsync(
        string userId,
        int targetCount,
        CancellationToken ct)
    {
        return await db.ReadingErrorBankEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId && !e.IsResolved)
            .OrderByDescending(e => e.LastSeenWrongAt)
            .ThenByDescending(e => e.TimesWrong)
            .Take(targetCount)
            .Select(e => e.ReadingQuestionId)
            .ToListAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SelectMockQuestionsAsync
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<List<Guid>> SelectMockQuestionsAsync(
        string userId,
        Guid mockTemplateId,
        CancellationToken ct)
    {
        var template = await db.ReadingMockTemplates
            .AsNoTracking()
            .Where(t => t.Id == mockTemplateId && t.IsPublished)
            .Select(t => t.QuestionIdsJson)
            .FirstOrDefaultAsync(ct);

        if (template is null) return [];

        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(template) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void Shuffle<T>(List<T> list)
    {
        var rng = Random.Shared;
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static Guid StableGuidFromQuestionId(string questionId)
    {
        if (Guid.TryParse(questionId, out var parsed)) return parsed;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(questionId));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }
}
