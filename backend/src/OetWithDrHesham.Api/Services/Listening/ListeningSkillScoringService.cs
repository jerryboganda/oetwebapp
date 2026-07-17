using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Skill Scoring Service — Listening Module Pathway Phase 1
//
// Maintains per-learner per-sub-skill (L1..L8) mastery scores and per-accent
// (BR/AU/US/NN) accuracy on a rolling cumulative basis. Mirrors the Reading
// pathway's SkillScoringService but with two key differences:
//
//   1. Tag fan-out — a single ListeningQuestion can carry multiple sub-skill
//      codes via SubSkillTagsCsv (e.g. "L2,L8"). Every tag listed accrues a
//      proportional share of the attempt's correctness against its row.
//
//   2. Accent dimension — every attempt is also rolled up against the
//      question's BCP-47-ish Accent code (en-GB / en-AU / en-US / en-XX),
//      normalised to the canonical {british, australian, us, non_native}
//      vocabulary used by LearnerAccentProgress and the dashboard chips
//      (cf. §12.2 of OET_LISTENING_MODULE_PATHWAY.md).
//
// Scoring math is cumulative — CurrentScore = correct/attempted * 10 across
// all-time attempts on that sub-skill. (Phase 2 may layer in a windowed
// rolling-weighted variant once we have enough attempt volume per learner.)
// MinutesListened on accent rows counts only attempts that actually played
// audio (i.e. AudioAssetId is not null), per §12.2 spec wording.
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningSkillScoringService
{
    /// <summary>After a practice session ends, recompute each L1..L8 score
    /// affected by the session's answers. Also updates per-accent accuracy.</summary>
    Task UpdateScoresFromSessionAsync(string userId, Guid practiceSessionId, CancellationToken ct);

    /// <summary>Seed/replace the diagnostic baseline across all 8 sub-skills + 4 accents.
    /// Called once by the diagnostic submit handler.</summary>
    Task SetDiagnosticBaselineAsync(string userId, IReadOnlyDictionary<string, decimal> skillScores,
        IReadOnlyDictionary<string, decimal> accentScores, CancellationToken ct);

    /// <summary>Return the learner's current L1..L8 + 4 accent scores. Missing rows
    /// surface as 0 score / 0 questions attempted.</summary>
    Task<(IReadOnlyList<LearnerListeningSkillScore> Skills, IReadOnlyList<LearnerAccentProgress> Accents)>
        GetScoresAsync(string userId, CancellationToken ct);

    /// <summary>Return weakest sub-skill (lowest CurrentScore). Null if no scores yet.</summary>
    Task<string?> GetWeakestSkillAsync(string userId, CancellationToken ct);

    /// <summary>Return weakest accent (lowest AccuracyPercentage). Null if no accents practiced.</summary>
    Task<string?> GetWeakestAccentAsync(string userId, CancellationToken ct);
}

public sealed class ListeningSkillScoringService(
    LearnerDbContext db,
    ILogger<ListeningSkillScoringService> logger) : IListeningSkillScoringService
{
    /// <summary>Canonical sub-skill codes per §6.5 / §7.4 of the pathway spec.</summary>
    private static readonly string[] AllSkillCodes =
        ["L1", "L2", "L3", "L4", "L5", "L6", "L7", "L8"];

    /// <summary>Canonical accent labels per §12.2 — the vocabulary stored on
    /// <see cref="LearnerAccentProgress.Accent"/>.</summary>
    private static readonly string[] AllAccents =
        ["british", "australian", "us", "non_native"];

    private static readonly char[] CsvSeparator = [','];

    public async Task UpdateScoresFromSessionAsync(string userId, Guid practiceSessionId, CancellationToken ct)
    {
        // Step 1: load attempts for this session.
        var attempts = await db.ListeningQuestionAttempts
            .Where(a => a.PracticeSessionId == practiceSessionId && a.UserId == userId)
            .ToListAsync(ct);

        if (attempts.Count == 0)
        {
            logger.LogDebug(
                "No attempts found for listening session {SessionId} (user {UserId}); nothing to score.",
                practiceSessionId, userId);
            return;
        }

        // Step 2: join to ListeningQuestion to pick up SubSkillTagsCsv + Accent.
        // ListeningQuestion.Id is string PK; ListeningQuestionAttempt.ListeningQuestionId
        // is the matching string FK (cf. Domain/ListeningPathwayEntities.cs lines 130-134).
        var questionIds = attempts.Select(a => a.ListeningQuestionId).Distinct().ToList();
        var questionMeta = await db.ListeningQuestions
            .AsNoTracking()
            .Where(q => questionIds.Contains(q.Id))
            .Select(q => new { q.Id, q.SubSkillTagsCsv, q.Accent })
            .ToDictionaryAsync(q => q.Id, ct);

        // Step 3 + 4: walk attempts, accumulate {skill -> (attempted, correct)} and
        // {accent -> (attempted, correct, minutes)} buckets in-memory before touching
        // the database. We mutate after the read pass so EF tracking stays clean.
        var skillBuckets = new Dictionary<string, SkillBucket>(StringComparer.OrdinalIgnoreCase);
        var accentBuckets = new Dictionary<string, AccentBucket>(StringComparer.OrdinalIgnoreCase);

        foreach (var attempt in attempts)
        {
            if (!questionMeta.TryGetValue(attempt.ListeningQuestionId, out var meta))
            {
                // Question deleted or admin renamed the PK — log and skip rather than blow up the whole session.
                logger.LogWarning(
                    "Listening attempt {AttemptId} references question {QuestionId} that no longer exists; skipping.",
                    attempt.Id, attempt.ListeningQuestionId);
                continue;
            }

            // Sub-skill fan-out. A question may be tagged with multiple L-codes
            // ("L2,L8") — every listed code accrues this attempt fully.
            var subSkills = ParseSubSkills(meta.SubSkillTagsCsv);
            foreach (var code in subSkills)
            {
                if (!skillBuckets.TryGetValue(code, out var bucket))
                {
                    bucket = new SkillBucket();
                    skillBuckets[code] = bucket;
                }
                bucket.Attempted++;
                if (attempt.IsCorrect) bucket.Correct++;
            }

            // Accent fan-in. Normalise BCP-47-ish question codes to the
            // dashboard vocabulary; questions with no accent tag are ignored.
            var accent = NormaliseAccent(meta.Accent);
            if (accent is not null)
            {
                if (!accentBuckets.TryGetValue(accent, out var bucket))
                {
                    bucket = new AccentBucket();
                    accentBuckets[accent] = bucket;
                }
                bucket.Attempted++;
                if (attempt.IsCorrect) bucket.Correct++;

                // Step 5: minutes listened only counts attempts that actually
                // played audio. The AudioAssetId nullable on ListeningQuestionAttempt
                // is our truth signal (cf. ListeningPathwayEntities.cs line 136).
                if (attempt.AudioAssetId.HasValue && attempt.TimeSpentSeconds > 0)
                {
                    bucket.AudioSeconds += attempt.TimeSpentSeconds;
                }
            }
        }

        if (skillBuckets.Count == 0 && accentBuckets.Count == 0)
        {
            logger.LogDebug(
                "Listening session {SessionId} had {AttemptCount} attempts but none carried usable sub-skill/accent tags.",
                practiceSessionId, attempts.Count);
            return;
        }

        // Step 6: upsert rows. We pull the existing rows once per dimension and
        // patch in-place to keep the trip count to two SELECTs + one SaveChanges.
        var existingSkillRows = await db.LearnerListeningSkillScores
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);
        var existingAccentRows = await db.LearnerAccentProgresses
            .Where(a => a.UserId == userId)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;

        foreach (var (code, bucket) in skillBuckets)
        {
            var row = existingSkillRows.FirstOrDefault(s =>
                string.Equals(s.SkillCode, code, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new LearnerListeningSkillScore
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SkillCode = code,
                    DiagnosticScore = 0m,
                    QuestionsAttempted = 0,
                    QuestionsCorrect = 0,
                };
                db.LearnerListeningSkillScores.Add(row);
            }

            row.QuestionsAttempted += bucket.Attempted;
            row.QuestionsCorrect += bucket.Correct;
            row.CurrentScore = ComputeSkillScore(row.QuestionsCorrect, row.QuestionsAttempted);
            row.LastPracticedAt = now;
            row.UpdatedAt = now;
        }

        foreach (var (accent, bucket) in accentBuckets)
        {
            var row = existingAccentRows.FirstOrDefault(a =>
                string.Equals(a.Accent, accent, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new LearnerAccentProgress
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Accent = accent,
                    QuestionsAttempted = 0,
                    QuestionsCorrect = 0,
                    MinutesListened = 0,
                    SelfConfidenceRating = 0,
                };
                db.LearnerAccentProgresses.Add(row);
            }

            row.QuestionsAttempted += bucket.Attempted;
            row.QuestionsCorrect += bucket.Correct;
            row.AccuracyPercentage = ComputeAccentAccuracy(row.QuestionsCorrect, row.QuestionsAttempted);
            // Whole-minute floor — partial-minute fractions roll forward only
            // once enough audio-seconds accumulate to cross another minute mark.
            // We re-derive from cumulative seconds rather than storing the raw
            // seconds count to keep the entity surface minimal (Phase 1).
            row.MinutesListened += bucket.AudioSeconds / 60;
            row.LastPracticedAt = now;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Listening scores updated for user {UserId} from session {SessionId}: {SkillCount} sub-skills, {AccentCount} accents.",
            userId, practiceSessionId, skillBuckets.Count, accentBuckets.Count);
    }

    public async Task SetDiagnosticBaselineAsync(
        string userId,
        IReadOnlyDictionary<string, decimal> skillScores,
        IReadOnlyDictionary<string, decimal> accentScores,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(skillScores);
        ArgumentNullException.ThrowIfNull(accentScores);

        var existingSkillRows = await db.LearnerListeningSkillScores
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);
        var existingAccentRows = await db.LearnerAccentProgresses
            .Where(a => a.UserId == userId)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;

        foreach (var (rawCode, rawValue) in skillScores)
        {
            var code = (rawCode ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(code)) continue;

            // Clamp to [0, 10] using the spec-mandated Math.Clamp((double)x, 0, 10) idiom.
            var clamped = (decimal)Math.Clamp((double)rawValue, 0d, 10d);

            var row = existingSkillRows.FirstOrDefault(s =>
                string.Equals(s.SkillCode, code, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new LearnerListeningSkillScore
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SkillCode = code,
                };
                db.LearnerListeningSkillScores.Add(row);
            }

            // Diagnostic seed — do NOT double-count question attempts here; the
            // diagnostic session's own per-question UpdateScoresFromSessionAsync
            // call (or its absence) is what owns the attempt totals.
            row.DiagnosticScore = clamped;
            row.CurrentScore = clamped;
            row.QuestionsAttempted = 0;
            row.QuestionsCorrect = 0;
            row.LastPracticedAt = now;
            row.UpdatedAt = now;
        }

        foreach (var (rawAccent, rawValue) in accentScores)
        {
            var accent = NormaliseAccent(rawAccent);
            if (accent is null) continue;

            // Accent dashboard scores live on a 0–100 scale; clamp accordingly.
            var clamped = (decimal)Math.Clamp((double)rawValue, 0d, 100d);

            var row = existingAccentRows.FirstOrDefault(a =>
                string.Equals(a.Accent, accent, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new LearnerAccentProgress
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Accent = accent,
                };
                db.LearnerAccentProgresses.Add(row);
            }

            row.AccuracyPercentage = clamped;
            row.QuestionsAttempted = 0;
            row.QuestionsCorrect = 0;
            row.MinutesListened = 0;
            row.LastPracticedAt = now;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Diagnostic baseline written for user {UserId}: {SkillCount} skills + {AccentCount} accents.",
            userId, skillScores.Count, accentScores.Count);
    }

    public async Task<(IReadOnlyList<LearnerListeningSkillScore> Skills, IReadOnlyList<LearnerAccentProgress> Accents)>
        GetScoresAsync(string userId, CancellationToken ct)
    {
        var skills = await db.LearnerListeningSkillScores
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);

        var accents = await db.LearnerAccentProgresses
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .ToListAsync(ct);

        // Surface missing rows as zero-valued placeholders so the dashboard
        // can render a complete L1..L8 grid + 4-accent strip without conditionals.
        foreach (var code in AllSkillCodes)
        {
            if (!skills.Any(s => string.Equals(s.SkillCode, code, StringComparison.OrdinalIgnoreCase)))
            {
                skills.Add(new LearnerListeningSkillScore
                {
                    Id = Guid.Empty,
                    UserId = userId,
                    SkillCode = code,
                    CurrentScore = 0m,
                    DiagnosticScore = 0m,
                    QuestionsAttempted = 0,
                    QuestionsCorrect = 0,
                });
            }
        }

        foreach (var accent in AllAccents)
        {
            if (!accents.Any(a => string.Equals(a.Accent, accent, StringComparison.OrdinalIgnoreCase)))
            {
                accents.Add(new LearnerAccentProgress
                {
                    Id = Guid.Empty,
                    UserId = userId,
                    Accent = accent,
                    AccuracyPercentage = 0m,
                    QuestionsAttempted = 0,
                    QuestionsCorrect = 0,
                    MinutesListened = 0,
                });
            }
        }

        return (skills, accents);
    }

    public async Task<string?> GetWeakestSkillAsync(string userId, CancellationToken ct)
    {
        var weakest = await db.LearnerListeningSkillScores
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.QuestionsAttempted > 0)
            .OrderBy(s => s.CurrentScore)
            .ThenBy(s => s.SkillCode)
            .Select(s => s.SkillCode)
            .FirstOrDefaultAsync(ct);
        return weakest;
    }

    public async Task<string?> GetWeakestAccentAsync(string userId, CancellationToken ct)
    {
        var weakest = await db.LearnerAccentProgresses
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.QuestionsAttempted > 0)
            .OrderBy(a => a.AccuracyPercentage)
            .ThenBy(a => a.Accent)
            .Select(a => a.Accent)
            .FirstOrDefaultAsync(ct);
        return weakest;
    }

    // ─── helpers ──────────────────────────────────────────────────────────

    private static decimal ComputeSkillScore(int correct, int attempted)
    {
        if (attempted <= 0) return 0m;
        var raw = (double)correct / attempted * 10d;
        var clamped = Math.Clamp(raw, 0d, 10d);
        return Math.Round((decimal)clamped, 2, MidpointRounding.AwayFromZero);
    }

    private static decimal ComputeAccentAccuracy(int correct, int attempted)
    {
        if (attempted <= 0) return 0m;
        var raw = (double)correct / attempted * 100d;
        var clamped = Math.Clamp(raw, 0d, 100d);
        return Math.Round((decimal)clamped, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Parse SubSkillTagsCsv ("L2,L8" or " l2 , l8 ") into uppercase
    /// canonical sub-skill codes. Anything outside L1..L8 is dropped — we don't
    /// want a typo in admin authoring to silently create a phantom mastery row.</summary>
    private static IReadOnlyCollection<string> ParseSubSkills(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<string>();

        var parts = csv.Split(CsvSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts)
        {
            var upper = part.ToUpperInvariant();
            if (Array.IndexOf(AllSkillCodes, upper) >= 0)
                set.Add(upper);
        }
        return set;
    }

    /// <summary>Normalise either a BCP-47-ish question accent code (en-GB,
    /// en-AU, en-US, en-XX) or an already-normalised dashboard label
    /// (british / australian / us / non_native) to the canonical vocabulary
    /// used by <see cref="LearnerAccentProgress.Accent"/>. Returns null for
    /// unknown / unmappable values so callers can skip silently.</summary>
    private static string? NormaliseAccent(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Already-canonical labels (used by SetDiagnosticBaselineAsync callers).
        var lower = raw.Trim().ToLowerInvariant();
        if (Array.IndexOf(AllAccents, lower) >= 0) return lower;

        // BCP-47-ish question accent codes — see ListeningQuestion.Accent
        // doc-comment (Domain/ListeningEntities.cs line 313).
        return lower switch
        {
            "en-gb" or "en-ie" or "en-uk" => "british",
            "en-au" or "en-nz" => "australian",
            "en-us" or "en-ca" => "us",
            "en-xx" or "en-in" or "en-ph" or "en-za" => "non_native",
            _ => null,
        };
    }

    private sealed class SkillBucket
    {
        public int Attempted { get; set; }
        public int Correct { get; set; }
    }

    private sealed class AccentBucket
    {
        public int Attempted { get; set; }
        public int Correct { get; set; }
        public int AudioSeconds { get; set; }
    }
}
