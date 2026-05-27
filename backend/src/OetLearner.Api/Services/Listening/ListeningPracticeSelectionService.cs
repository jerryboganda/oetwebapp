using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Practice Selection Service — Listening Module Pathway Phase 3 (§8.2)
//
// Adaptive Listening question selection for drill / accent_drill / dictation /
// mock modes. Mirrors the Reading PracticeSelectionService in shape, but with
// three Listening-specific behaviours:
//
//   1. SubSkillTagsCsv vs Reading.SkillTag — Listening questions can carry
//      *multiple* sub-skill codes (e.g. "L2,L8"), so the filter performs a
//      string-contains rather than equality. We post-filter on the CSR-loaded
//      list because EF Core can't index-prefix a substring match.
//
//   2. Accent column — drills can be narrowed to a specific accent so the
//      "weakest accent" pathway item ships an accent_drill that exclusively
//      uses, e.g., en-AU audio.
//
//   3. Difficulty band — uses the optional <see cref="ListeningQuestion.DifficultyLevel"/>
//      1–5 column. Learners with a CurrentScore around 6/10 see questions
//      at difficulty 3..4 (score/2 + 1, clamped). Missing scores default to
//      a band of 1..3.
//
// Type notes:
//   • <see cref="ListeningQuestion.Id"/> is a string PK (max 64 chars).
//   • <see cref="ListeningQuestionAttempt.ListeningQuestionId"/> mirrors that.
//   • <see cref="ListeningMockTemplate.QuestionIdsJson"/> stores an ordered
//     JSON array of those same string keys (42 entries for full mocks).
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Phase 3 listening adaptive practice selection (§8.2). Returns the
/// ordered question / drill IDs that the practice session orchestrator will
/// stamp into <c>ListeningPracticeSession.QuestionIdsJson</c>.</summary>
public interface IListeningPracticeSelectionService
{
    /// <summary>Spec §8.2 adaptive algorithm. Returns ordered Listening question IDs (strings).</summary>
    Task<IReadOnlyList<string>> SelectAudioForDrillAsync(
        string userId,
        string focusSkill,
        string? focusAccent,
        int targetMinutes,
        CancellationToken ct);

    /// <summary>Accent-targeted drill — narrows the pool to a single accent
    /// (e.g. <c>"australian"</c>) regardless of sub-skill.</summary>
    Task<IReadOnlyList<string>> SelectAccentDrillAsync(
        string userId,
        string accent,
        int targetMinutes,
        CancellationToken ct);

    /// <summary>Dictation drills (Guid IDs from <see cref="DictationDrill"/>),
    /// preferring published drills the learner has not seen in the last 14 days.</summary>
    Task<IReadOnlyList<Guid>> SelectDictationSetAsync(
        string userId,
        int targetCount,
        CancellationToken ct);

    /// <summary>Return the ordered string question list from the given mock template.</summary>
    Task<IReadOnlyList<string>> SelectMockQuestionsAsync(
        Guid mockTemplateId,
        CancellationToken ct);
}

public sealed class ListeningPracticeSelectionService(LearnerDbContext db)
    : IListeningPracticeSelectionService
{
    private const int SeenRecentlyDays = 14;
    private const decimal DefaultSkillScore = 5m;

    // Canonical accent code mapping — pathway side uses {british,australian,us,
    // non_native}; the ListeningQuestion.Accent column carries BCP-47-ish codes
    // (en-GB / en-AU / en-US / en-XX). We translate both ways so callers can
    // pass whichever vocabulary they have on hand.
    private static readonly IReadOnlyDictionary<string, string> AccentCodeToBcp =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["british"]    = "en-GB",
            ["australian"] = "en-AU",
            ["us"]         = "en-US",
            ["american"]   = "en-US",
            ["non_native"] = "en-XX",
        };

    // ═══════════════════════════════════════════════════════════════════════
    // SelectAudioForDrillAsync — spec §8.2 adaptive algorithm
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<string>> SelectAudioForDrillAsync(
        string userId,
        string focusSkill,
        string? focusAccent,
        int targetMinutes,
        CancellationToken ct)
    {
        // ~2 minutes per Listening question on average (audio + reflection).
        var targetCount = Math.Max(3, targetMinutes / 2);

        // ── 1. Current skill score → difficulty band ─────────────────────
        var currentScore = await db.LearnerListeningSkillScores
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.SkillCode == focusSkill)
            .Select(s => (decimal?)s.CurrentScore)
            .FirstOrDefaultAsync(ct) ?? DefaultSkillScore;

        // Translate 0–10 score → 1–5 difficulty band with ±1 tolerance.
        // floor((score + 1) / 2)) keeps the band roughly aligned with the
        // 1–5 author-rated difficulty scale (a learner scoring 6/10 targets
        // difficulty 3, with 2..4 acceptable).
        var centre = Math.Clamp((int)Math.Floor((currentScore + 1m) / 2m), 1, 5);
        var bandLow = Math.Max(1, centre - 1);
        var bandHigh = Math.Min(5, centre + 1);

        // ── 2. Exclude questions attempted in the last 14 days ───────────
        var recentCutoff = DateTimeOffset.UtcNow.AddDays(-SeenRecentlyDays);
        var recentIds = await db.ListeningQuestionAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.AttemptedAt >= recentCutoff)
            .Select(a => a.ListeningQuestionId)
            .Distinct()
            .ToListAsync(ct);
        var recentSet = new HashSet<string>(recentIds, StringComparer.OrdinalIgnoreCase);

        // ── 3. Optional accent filter — translate to BCP-47-ish code ─────
        string? accentFilter = ResolveAccentCode(focusAccent);

        // ── 4. Fetch candidate questions (SubSkillTagsCsv contains focus) ─
        // We over-fetch the sub-skill-tag matches client-side because
        // SubSkillTagsCsv is a CSV column — EF can't index-prefix a contains
        // for the comma boundary, so we narrow with EF.Functions.Like first
        // then post-filter on the parsed CSV.
        var candidatePool = await db.ListeningQuestions
            .AsNoTracking()
            .Where(q => q.SubSkillTagsCsv != null
                && EF.Functions.Like(q.SubSkillTagsCsv!, $"%{focusSkill}%"))
            .Where(q => accentFilter == null || q.Accent == accentFilter)
            .Where(q => q.DifficultyLevel == null
                || (q.DifficultyLevel >= bandLow && q.DifficultyLevel <= bandHigh))
            .Select(q => new { q.Id, q.SubSkillTagsCsv, q.Accent })
            .ToListAsync(ct);

        var candidates = candidatePool
            .Where(q => HasSubSkillTag(q.SubSkillTagsCsv, focusSkill))
            .Where(q => !recentSet.Contains(q.Id))
            .Select(q => q.Id)
            .ToList();

        // ── 5. Fallback: relax difficulty band, then relax recency ──────
        if (candidates.Count < targetCount)
        {
            var widerPool = await db.ListeningQuestions
                .AsNoTracking()
                .Where(q => q.SubSkillTagsCsv != null
                    && EF.Functions.Like(q.SubSkillTagsCsv!, $"%{focusSkill}%"))
                .Where(q => accentFilter == null || q.Accent == accentFilter)
                .Select(q => new { q.Id, q.SubSkillTagsCsv })
                .ToListAsync(ct);

            var widerIds = widerPool
                .Where(q => HasSubSkillTag(q.SubSkillTagsCsv, focusSkill))
                .Where(q => !recentSet.Contains(q.Id))
                .Select(q => q.Id)
                .ToList();

            // Union; preserve initial ordering.
            foreach (var id in widerIds)
            {
                if (!candidates.Contains(id, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(id);
            }
        }

        if (candidates.Count == 0)
        {
            // Last-ditch: any question tagged with the focus sub-skill,
            // ignoring recency entirely. Permits repeats so a brand-new
            // learner still receives a non-empty drill.
            var fallback = await db.ListeningQuestions
                .AsNoTracking()
                .Where(q => q.SubSkillTagsCsv != null
                    && EF.Functions.Like(q.SubSkillTagsCsv!, $"%{focusSkill}%"))
                .Select(q => new { q.Id, q.SubSkillTagsCsv })
                .ToListAsync(ct);

            candidates = fallback
                .Where(q => HasSubSkillTag(q.SubSkillTagsCsv, focusSkill))
                .Select(q => q.Id)
                .ToList();
        }

        if (candidates.Count == 0) return [];

        // ── 6. Weighted sampling: 3× previously-wrong, 1.5× weakest-accent ─
        return await WeightedSampleAsync(userId, candidates, focusAccent, targetCount, ct);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SelectAccentDrillAsync
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<string>> SelectAccentDrillAsync(
        string userId,
        string accent,
        int targetMinutes,
        CancellationToken ct)
    {
        var targetCount = Math.Max(3, targetMinutes / 2);
        var bcp = ResolveAccentCode(accent);
        if (bcp is null) return [];

        var recentCutoff = DateTimeOffset.UtcNow.AddDays(-SeenRecentlyDays);
        var recentIds = await db.ListeningQuestionAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.AttemptedAt >= recentCutoff)
            .Select(a => a.ListeningQuestionId)
            .Distinct()
            .ToListAsync(ct);
        var recentSet = new HashSet<string>(recentIds, StringComparer.OrdinalIgnoreCase);

        var candidates = await db.ListeningQuestions
            .AsNoTracking()
            .Where(q => q.Accent == bcp)
            .Select(q => q.Id)
            .ToListAsync(ct);

        var fresh = candidates.Where(id => !recentSet.Contains(id)).ToList();
        var pool = fresh.Count > 0 ? fresh : candidates;
        if (pool.Count == 0) return [];

        return await WeightedSampleAsync(userId, pool, accent, targetCount, ct);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SelectDictationSetAsync
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<Guid>> SelectDictationSetAsync(
        string userId,
        int targetCount,
        CancellationToken ct)
    {
        if (targetCount <= 0) return [];

        var recentCutoff = DateTimeOffset.UtcNow.AddDays(-SeenRecentlyDays);

        // Prefer drills the learner has either never attempted or hasn't seen
        // in the last 14 days. We carry an explicit "not attempted" subset to
        // preserve the spec's "freshness over repetition" preference.
        var attemptedRecently = await db.LearnerDictationProgresses
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.LastAttemptedAt >= recentCutoff)
            .Select(p => p.DictationDrillId)
            .ToListAsync(ct);
        var recentSet = attemptedRecently.ToHashSet();

        var allPublished = await db.DictationDrills
            .AsNoTracking()
            .Where(d => d.IsPublished)
            .Select(d => d.Id)
            .ToListAsync(ct);

        var fresh = allPublished.Where(id => !recentSet.Contains(id)).ToList();
        var pool = fresh.Count > 0 ? fresh : allPublished;
        if (pool.Count == 0) return [];

        Shuffle(pool);
        return pool.Take(targetCount).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SelectMockQuestionsAsync
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<string>> SelectMockQuestionsAsync(
        Guid mockTemplateId,
        CancellationToken ct)
    {
        var json = await db.ListeningMockTemplates
            .AsNoTracking()
            .Where(t => t.Id == mockTemplateId && t.IsPublished)
            .Select(t => t.QuestionIdsJson)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Weighting + sampling
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a weighted bag from <paramref name="candidates"/>:
    ///   • 3× for questions the user has previously answered incorrectly
    ///     (still in the wrong-review queue).
    ///   • 1.5× when the question's accent matches <paramref name="focusAccent"/>
    ///     — implemented as 2× on half the matching rows via a flip-flop.
    /// Then shuffles + de-duplicates, returning up to <paramref name="targetCount"/>
    /// distinct IDs.
    /// </summary>
    private async Task<IReadOnlyList<string>> WeightedSampleAsync(
        string userId,
        List<string> candidates,
        string? focusAccent,
        int targetCount,
        CancellationToken ct)
    {
        // Wrong-review IDs — previously-wrong, still in the review queue.
        var wrongIds = await db.ListeningQuestionAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId
                && !a.IsCorrect
                && a.InReviewQueue)
            .Select(a => a.ListeningQuestionId)
            .Distinct()
            .ToListAsync(ct);
        var wrongSet = new HashSet<string>(wrongIds, StringComparer.OrdinalIgnoreCase);

        // Accent code for the 1.5× boost — translated to BCP-47.
        var accentBcp = ResolveAccentCode(focusAccent);
        HashSet<string>? accentMatchingIds = null;
        if (accentBcp is not null)
        {
            var idsForAccent = await db.ListeningQuestions
                .AsNoTracking()
                .Where(q => q.Accent == accentBcp && candidates.Contains(q.Id))
                .Select(q => q.Id)
                .ToListAsync(ct);
            accentMatchingIds = new HashSet<string>(idsForAccent, StringComparer.OrdinalIgnoreCase);
        }

        var pool = new List<string>(candidates.Count * 3);
        var flipFlop = false;
        foreach (var id in candidates)
        {
            pool.Add(id);
            if (wrongSet.Contains(id))
            {
                pool.Add(id);
                pool.Add(id);
            }
            if (accentMatchingIds is not null && accentMatchingIds.Contains(id))
            {
                // 1.5× implemented as alternating 2× on every other match,
                // which averages out to 1.5× across the candidate set.
                if (flipFlop) pool.Add(id);
                flipFlop = !flipFlop;
            }
        }

        Shuffle(pool);

        // De-duplicate, preserving the weighted-first ordering.
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

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Translate either the canonical accent vocabulary
    /// (<c>british / australian / us / non_native</c>) OR a BCP-47-ish code
    /// (<c>en-GB / en-AU / en-US / en-XX</c>) into the BCP-47-ish form stored
    /// on <see cref="ListeningQuestion.Accent"/>. Returns <c>null</c> for
    /// <c>null</c>/whitespace input.</summary>
    private static string? ResolveAccentCode(string? accent)
    {
        if (string.IsNullOrWhiteSpace(accent)) return null;
        if (AccentCodeToBcp.TryGetValue(accent, out var bcp)) return bcp;
        // If the caller already passed a BCP-47-ish code (e.g. "en-GB"),
        // pass it through unchanged. Anything else is a typo / new accent
        // and we fail open with the raw value to surface in logs.
        return accent;
    }

    /// <summary>Check whether the comma-separated sub-skill CSV contains the
    /// exact code (case-insensitive, whole-token match). Avoids partial
    /// substring matches like "L1" matching "L10".</summary>
    private static bool HasSubSkillTag(string? csv, string skillCode)
    {
        if (string.IsNullOrWhiteSpace(csv)) return false;
        foreach (var token in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(token, skillCode, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void Shuffle<T>(List<T> list)
    {
        var rng = Random.Shared;
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
