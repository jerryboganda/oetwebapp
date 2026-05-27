using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Recalls;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// OET Listening Module Pathway — Phase 4 (§14): Dictation Drill subsystem.
//
// A dictation drill plays a short healthcare audio clip and asks the learner
// to transcribe it. Grading is *spelling-tolerant within reason* — common
// healthcare misspellings (off-by-one typos) are flagged with a "did you mean"
// hint rather than counted as a hard fail, but anything further from the
// canonical transcript is treated as a wrong answer with the correct form
// surfaced for study.
//
// Scheduling follows a simplified spaced-repetition model:
//   • First correct (Attempts becomes 1): NextReviewAt = now + 3 days.
//   • Second consecutive correct (Attempts >= 2 and previously correct):
//     NextReviewAt = now + 7 days, considered "mastered" for stats.
//   • Wrong answer: NextReviewAt = now + 1 day, Attempts increments.
//
// The set selector blends (a) due review items from the learner's history with
// (b) new published drills the learner has not yet seen, and (c) varies the
// difficulty distribution so the set is not all hard or all easy.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Lightweight projection of a dictation drill for learner playback.</summary>
public sealed record DictationDrillDto(
    Guid Id,
    string DrillType,
    string? AudioAssetUrl,
    int DurationSeconds,
    string Accent,
    int Difficulty);

/// <summary>Per-attempt grading result returned to the learner UI.</summary>
public sealed record DictationResultDto(
    Guid DrillId,
    bool IsCorrect,
    bool OffByOneTypo,
    string CorrectAnswer,
    string LearnerAnswer,
    DateTimeOffset? NextReviewAt);

/// <summary>Aggregate dictation progress stats for the dashboard header.</summary>
public sealed record DictationStatsDto(
    int TotalAttempted,
    int Mastered,
    int Struggling,
    decimal AccuracyPercentage);

/// <summary>
/// Dictation drill orchestration: picks a mixed set, grades answers with
/// healthcare-spelling tolerance, and rolls forward the spaced-repetition
/// schedule. See <see cref="DictationService"/>.
/// </summary>
public interface IDictationService
{
    /// <summary>
    /// Compose a set of <paramref name="targetCount"/> drills mixing due review
    /// items with fresh published drills the learner has not yet attempted.
    /// Difficulty is varied so a set is never all-easy or all-hard.
    /// </summary>
    Task<IReadOnlyList<DictationDrillDto>> SelectDrillSetAsync(string userId, int targetCount, CancellationToken ct);

    /// <summary>
    /// Grade <paramref name="learnerAnswer"/> against the drill's transcript
    /// and accepted variants, upsert <see cref="LearnerDictationProgress"/>,
    /// and return the per-attempt result envelope.
    /// </summary>
    Task<DictationResultDto> SubmitAnswerAsync(string userId, Guid drillId, string learnerAnswer, CancellationToken ct);

    /// <summary>Aggregate stats: total attempted, mastered, struggling, overall accuracy.</summary>
    Task<DictationStatsDto> GetStatsAsync(string userId, CancellationToken ct);
}

/// <summary>
/// EF Core-backed implementation of <see cref="IDictationService"/>.
/// </summary>
public sealed class DictationService : IDictationService
{
    private readonly LearnerDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<DictationService> _log;

    public DictationService(LearnerDbContext db, TimeProvider clock, ILogger<DictationService> log)
    {
        _db = db;
        _clock = clock;
        _log = log;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Set selection
    // ─────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<DictationDrillDto>> SelectDrillSetAsync(
        string userId,
        int targetCount,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }

        // Cap target count to prevent runaway query sizes; the UI requests 8
        // by default but be defensive against future callers.
        targetCount = Math.Clamp(targetCount, 1, 25);

        var now = _clock.GetUtcNow();

        // (a) Due review items — drills where NextReviewAt has elapsed.
        var dueProgress = await _db.LearnerDictationProgresses
            .Where(p => p.UserId == userId
                && p.NextReviewAt != null
                && p.NextReviewAt <= now)
            .OrderBy(p => p.NextReviewAt)
            .Take(targetCount)
            .ToListAsync(ct);

        var dueDrillIds = dueProgress.Select(p => p.DictationDrillId).ToList();

        var dueDrills = dueDrillIds.Count == 0
            ? new List<DictationDrill>()
            : await _db.DictationDrills
                .Where(d => dueDrillIds.Contains(d.Id) && d.IsPublished)
                .ToListAsync(ct);

        // (b) Fresh drills — published but never attempted by this learner.
        var remaining = targetCount - dueDrills.Count;
        var freshDrills = new List<DictationDrill>();
        if (remaining > 0)
        {
            var attemptedIds = await _db.LearnerDictationProgresses
                .Where(p => p.UserId == userId)
                .Select(p => p.DictationDrillId)
                .ToListAsync(ct);

            // We over-fetch slightly so we can shuffle the difficulty distribution
            // below rather than always returning the same N rows in CreatedAt order.
            var fetchCount = Math.Max(remaining * 3, remaining + 4);

            freshDrills = await _db.DictationDrills
                .Where(d => d.IsPublished && !attemptedIds.Contains(d.Id))
                .OrderBy(d => d.CreatedAt)
                .Take(fetchCount)
                .ToListAsync(ct);

            // Mix difficulties: bucket by Difficulty, take an even slice of each.
            freshDrills = SpreadByDifficulty(freshDrills, remaining);
        }

        var combined = new List<DictationDrill>(dueDrills.Count + freshDrills.Count);
        combined.AddRange(dueDrills);
        combined.AddRange(freshDrills);

        // Deterministic but interleaved ordering: review → new alternation so
        // the learner doesn't see all reviews up front.
        var ordered = InterleaveReviewWithNew(dueDrills, freshDrills);

        return ordered
            .Take(targetCount)
            .Select(d => new DictationDrillDto(
                d.Id,
                d.DrillType,
                d.AudioAssetUrl,
                d.DurationSeconds,
                d.Accent,
                d.Difficulty))
            .ToList();
    }

    /// <summary>
    /// Round-robin pick across Difficulty buckets so a set is never all-easy
    /// or all-hard. Preserves stable ordering within each bucket.
    /// </summary>
    private static List<DictationDrill> SpreadByDifficulty(
        IReadOnlyList<DictationDrill> source,
        int count)
    {
        if (source.Count == 0 || count <= 0) return new List<DictationDrill>();

        // Group by difficulty band — 1/2 = easy, 3 = medium, 4/5 = hard.
        var easy = source.Where(d => d.Difficulty <= 2).ToList();
        var medium = source.Where(d => d.Difficulty == 3).ToList();
        var hard = source.Where(d => d.Difficulty >= 4).ToList();

        var picked = new List<DictationDrill>(count);
        int easyIdx = 0, medIdx = 0, hardIdx = 0;

        // Round-robin: easy → medium → hard, skipping empty buckets.
        while (picked.Count < count
            && (easyIdx < easy.Count || medIdx < medium.Count || hardIdx < hard.Count))
        {
            if (easyIdx < easy.Count) picked.Add(easy[easyIdx++]);
            if (picked.Count >= count) break;
            if (medIdx < medium.Count) picked.Add(medium[medIdx++]);
            if (picked.Count >= count) break;
            if (hardIdx < hard.Count) picked.Add(hard[hardIdx++]);
        }

        return picked;
    }

    /// <summary>Interleave review items with fresh items so the learner doesn't see one block then the other.</summary>
    private static List<DictationDrill> InterleaveReviewWithNew(
        IReadOnlyList<DictationDrill> review,
        IReadOnlyList<DictationDrill> fresh)
    {
        var combined = new List<DictationDrill>(review.Count + fresh.Count);
        int r = 0, f = 0;
        while (r < review.Count || f < fresh.Count)
        {
            if (r < review.Count) combined.Add(review[r++]);
            if (f < fresh.Count) combined.Add(fresh[f++]);
        }
        return combined;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Grading + SR scheduling
    // ─────────────────────────────────────────────────────────────────────

    public async Task<DictationResultDto> SubmitAnswerAsync(
        string userId,
        Guid drillId,
        string learnerAnswer,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }

        var drill = await _db.DictationDrills
            .FirstOrDefaultAsync(d => d.Id == drillId && d.IsPublished, ct);
        if (drill is null)
        {
            throw new InvalidOperationException($"Dictation drill {drillId} not found.");
        }

        // Treat null/whitespace as an explicit wrong answer rather than an exception
        // — the UI should still record the attempt so it can show "Tap to skip".
        var typedRaw = learnerAnswer ?? string.Empty;
        var typedNormalised = Normalise(typedRaw);
        var canonical = Normalise(drill.TranscriptText);

        var (isCorrect, offByOneTypo) = Grade(typedNormalised, canonical, drill.AcceptableVariantsJson);

        var now = _clock.GetUtcNow();

        // Upsert progress row.
        var progress = await _db.LearnerDictationProgresses
            .FirstOrDefaultAsync(p => p.UserId == userId && p.DictationDrillId == drillId, ct);

        var previouslyCorrect = progress?.IsCorrect == true;
        if (progress is null)
        {
            progress = new LearnerDictationProgress
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                DictationDrillId = drillId,
            };
            _db.LearnerDictationProgresses.Add(progress);
        }

        progress.LearnerAnswer = typedRaw.Length > 1024 ? typedRaw[..1024] : typedRaw;
        progress.IsCorrect = isCorrect;
        progress.Attempts += 1;
        progress.LastAttemptedAt = now;
        progress.NextReviewAt = ComputeNextReviewAt(now, isCorrect, previouslyCorrect);

        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Dictation drill {DrillId} attempted by {UserId}: correct={IsCorrect} typo={OffByOneTypo} attempts={Attempts}",
            drillId, userId, isCorrect, offByOneTypo, progress.Attempts);

        return new DictationResultDto(
            drill.Id,
            isCorrect,
            offByOneTypo,
            drill.TranscriptText,
            typedRaw,
            progress.NextReviewAt);
    }

    /// <summary>
    /// Returns <c>(isCorrect, offByOneTypo)</c>. A direct match (after
    /// normalisation) against the canonical transcript or any accepted variant
    /// is correct. A Levenshtein distance of 1 against the canonical or any
    /// variant is treated as "wrong but close" so the UI can show a "Did you
    /// mean…?" hint. Anything larger is a hard miss.
    /// </summary>
    private static (bool isCorrect, bool offByOneTypo) Grade(
        string typed,
        string canonical,
        string variantsJson)
    {
        if (typed.Length == 0)
        {
            return (false, false);
        }

        if (string.Equals(typed, canonical, StringComparison.Ordinal))
        {
            return (true, false);
        }

        var variants = ParseVariants(variantsJson)
            .Select(Normalise)
            .Where(v => v.Length > 0)
            .ToList();

        foreach (var variant in variants)
        {
            if (string.Equals(typed, variant, StringComparison.Ordinal))
            {
                return (true, false);
            }
        }

        // Off-by-one typo against the canonical or any variant — flag as a
        // soft miss so the UI can show a "did you mean" prompt.
        var canonicalDistance = SpellingDiff.EditDistance(typed, canonical);
        if (canonicalDistance <= 1)
        {
            return (false, true);
        }

        foreach (var variant in variants)
        {
            if (SpellingDiff.EditDistance(typed, variant) <= 1)
            {
                return (false, true);
            }
        }

        return (false, false);
    }

    /// <summary>
    /// Simplified spaced-repetition scheduler — not SM-2, but conceptually
    /// similar: streaked correctness extends the next interval, a miss pulls
    /// it back to 1 day.
    /// </summary>
    private static DateTimeOffset ComputeNextReviewAt(
        DateTimeOffset now,
        bool isCorrect,
        bool previouslyCorrect)
    {
        if (!isCorrect)
        {
            return now.AddDays(1);
        }
        // Two-in-a-row → space out further; first correct → modest interval.
        return previouslyCorrect ? now.AddDays(7) : now.AddDays(3);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Stats
    // ─────────────────────────────────────────────────────────────────────

    public async Task<DictationStatsDto> GetStatsAsync(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }

        var rows = await _db.LearnerDictationProgresses
            .Where(p => p.UserId == userId)
            .Select(p => new { p.IsCorrect, p.Attempts })
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return new DictationStatsDto(0, 0, 0, 0m);
        }

        var totalAttempted = rows.Count;
        var mastered = rows.Count(r => r.IsCorrect && r.Attempts >= 2);
        var struggling = rows.Count(r => r.Attempts > 2 && !r.IsCorrect);

        var correctCount = rows.Count(r => r.IsCorrect);
        var accuracy = totalAttempted > 0
            ? Math.Round((decimal)correctCount * 100m / totalAttempted, 1, MidpointRounding.AwayFromZero)
            : 0m;

        return new DictationStatsDto(totalAttempted, mastered, struggling, accuracy);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Normalise an answer for tolerant comparison: NFC, trim, collapse
    /// runs of whitespace, lowercase. Punctuation is preserved so that a
    /// learner who omits a trailing full stop is still graded as correct
    /// against a transcript stored without one (the canonical form is
    /// stripped the same way).
    /// </summary>
    private static string Normalise(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var s = input.Normalize(NormalizationForm.FormC).Trim();
        // Lowercase + collapse internal whitespace.
        var sb = new StringBuilder(s.Length);
        var inWhitespace = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!inWhitespace)
                {
                    sb.Append(' ');
                    inWhitespace = true;
                }
            }
            else
            {
                sb.Append(char.ToLowerInvariant(ch));
                inWhitespace = false;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Parse the JSON-encoded acceptable-variants array on the drill record.
    /// Defensive — malformed JSON returns an empty list rather than throwing
    /// so a single bad seed row can't break grading for everyone.
    /// </summary>
    private static IReadOnlyList<string> ParseVariants(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(json);
            return values ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
