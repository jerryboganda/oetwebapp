using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Pronunciation Library Service — Listening Phase 4 (OET_LISTENING_MODULE_PATHWAY §15)
//
// Manages per-learner spaced-repetition pronunciation cards for the Listening
// Module pathway. SM-2 scheduling is implemented inline matching the spec
// (mirrors Services/Reading/VocabularyService.cs — kept independent so the
// listening surface evolves without coupling to the reading vocabulary deck).
//
// Composes the master PronunciationCard table (curated healthcare vocabulary
// with IPA + audio assets) with per-learner LearnerPronunciationCard rows
// holding the SM-2 state machine (easiness, intervalDays, repetitions,
// retentionScore, nextReviewAt).
//
// Differences from the Reading equivalent:
//   • Master cards are pronunciation-focused (IPA, audio per accent) rather
//     than meaning-focused (definition, example). When a learner adds an
//     unknown word the service creates a stub master card synchronously
//     rather than calling out to the AI gateway — audio assets will be
//     backfilled by an authoring workflow.
//   • New cards become due immediately (NextReviewAt = now) so the learner
//     can practice them in the same session they were added.
//   • RetentionScore evolves additively (clamped 0..100) rather than being
//     recomputed from `quality / 5 * 100` each review.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Listening pronunciation library — manages master pronunciation cards and
/// per-learner SM-2 review state.
/// </summary>
public interface IPronunciationService
{
    /// <summary>Add a word to the learner's pronunciation deck. Creates the
    /// master <see cref="PronunciationCard"/> if it does not yet exist.</summary>
    Task<LearnerPronunciationCard> AddCardAsync(string userId, string word, string source, CancellationToken ct);

    /// <summary>Return every card the learner has subscribed to, joined to
    /// the master row and projected to <see cref="PronunciationCardDto"/>.</summary>
    Task<IReadOnlyList<PronunciationCardDto>> GetUserCardsAsync(string userId, CancellationToken ct);

    /// <summary>Return up to <paramref name="max"/> cards due for review
    /// (NextReviewAt &lt;= now), ordered by NextReviewAt ascending.</summary>
    Task<IReadOnlyList<PronunciationCardDto>> GetDueForReviewAsync(string userId, int max, CancellationToken ct);

    /// <summary>Record a review outcome and advance the SM-2 schedule.
    /// Quality is in the range 0..5.</summary>
    Task<PronunciationReviewResult> SubmitReviewAsync(string userId, Guid cardId, int quality, CancellationToken ct);

    /// <summary>Aggregate retention counters for the learner's deck.</summary>
    Task<PronunciationStatsDto> GetStatsAsync(string userId, CancellationToken ct);

    /// <summary>Idempotently return the master <see cref="PronunciationCard"/>
    /// for the given word; creates a stub row if no matching record exists.</summary>
    Task<PronunciationCard> EnsureCardExistsAsync(string word, CancellationToken ct);

    /// <summary>Delete the learner's subscription to a pronunciation card.
    /// Leaves the master row in place.</summary>
    Task RemoveCardAsync(string userId, Guid cardId, CancellationToken ct);
}

// ─────────────────────────────────────────────────────────────────────────────
// DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Flattened projection of a learner's pronunciation card joined to
/// its master row. Sent over the wire by the GET endpoints.</summary>
public sealed record PronunciationCardDto(
    Guid Id,
    Guid PronunciationCardId,
    string Word,
    string PronunciationIpa,
    string? AudioBritishUrl,
    string? AudioAustralianUrl,
    string DefinitionEn,
    decimal Easiness,
    int IntervalDays,
    int Repetitions,
    int RetentionScore,
    DateTimeOffset NextReviewAt,
    DateTimeOffset? LastReviewedAt,
    DateTimeOffset AddedAt);

/// <summary>Returned by <see cref="IPronunciationService.SubmitReviewAsync"/>
/// — the minimum the frontend needs to render the post-review confirmation.</summary>
public sealed record PronunciationReviewResult(
    Guid CardId,
    int IntervalDays,
    DateTimeOffset NextReviewAt,
    int RetentionScore);

/// <summary>Aggregate stats for the learner's deck.
/// Mastered: Repetitions >= 4. Learning: 1..3. Struggling: 0.
/// DueToday: rows with NextReviewAt &lt;= now.</summary>
public sealed record PronunciationStatsDto(
    int Total,
    int Mastered,
    int Learning,
    int Struggling,
    int DueToday);

// ─────────────────────────────────────────────────────────────────────────────
// Service implementation
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PronunciationService(
    LearnerDbContext db,
    ILogger<PronunciationService>? logger = null)
    : IPronunciationService
{
    private const int MaxReviewCap = 100;

    // ── AddCardAsync ────────────────────────────────────────────────────────

    public async Task<LearnerPronunciationCard> AddCardAsync(
        string userId, string word, string source, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID must not be empty.", nameof(userId));
        if (string.IsNullOrWhiteSpace(word))
            throw new ArgumentException("Word must not be empty.", nameof(word));

        var masterCard = await EnsureCardExistsAsync(word, ct);

        // Idempotent — return the existing subscription if the learner has
        // already added this card.
        var existing = await db.LearnerPronunciationCards
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.PronunciationCardId == masterCard.Id, ct);
        if (existing is not null)
            return existing;

        var now = DateTimeOffset.UtcNow;
        var card = new LearnerPronunciationCard
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PronunciationCardId = masterCard.Id,
            Source = string.IsNullOrWhiteSpace(source) ? "manual" : source.Trim(),
            Easiness = 2.5m,
            IntervalDays = 1,
            Repetitions = 0,
            RetentionScore = 0,
            // Due immediately so the learner can practise the new word in the
            // same session they added it.
            NextReviewAt = now,
            LastReviewedAt = null,
            AddedAt = now,
        };

        db.LearnerPronunciationCards.Add(card);
        await db.SaveChangesAsync(ct);
        return card;
    }

    // ── GetUserCardsAsync ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<PronunciationCardDto>> GetUserCardsAsync(
        string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID must not be empty.", nameof(userId));

        // Project via a join so we surface the master Word / IPA / audio URLs
        // alongside the per-learner SM-2 state.
        var rows = await (
            from lpc in db.LearnerPronunciationCards.AsNoTracking()
            join pc in db.PronunciationCards.AsNoTracking()
                on lpc.PronunciationCardId equals pc.Id
            where lpc.UserId == userId
            orderby lpc.AddedAt descending
            select new PronunciationCardDto(
                lpc.Id,
                pc.Id,
                pc.Word,
                pc.PronunciationIpa,
                pc.AudioBritishUrl,
                pc.AudioAustralianUrl,
                pc.DefinitionEn,
                lpc.Easiness,
                lpc.IntervalDays,
                lpc.Repetitions,
                lpc.RetentionScore,
                lpc.NextReviewAt,
                lpc.LastReviewedAt,
                lpc.AddedAt))
            .ToListAsync(ct);

        return rows;
    }

    // ── GetDueForReviewAsync ────────────────────────────────────────────────

    public async Task<IReadOnlyList<PronunciationCardDto>> GetDueForReviewAsync(
        string userId, int max, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID must not be empty.", nameof(userId));

        var take = max <= 0 ? 25 : Math.Min(max, MaxReviewCap);
        var now = DateTimeOffset.UtcNow;

        var rows = await (
            from lpc in db.LearnerPronunciationCards.AsNoTracking()
            join pc in db.PronunciationCards.AsNoTracking()
                on lpc.PronunciationCardId equals pc.Id
            where lpc.UserId == userId && lpc.NextReviewAt <= now
            orderby lpc.NextReviewAt
            select new PronunciationCardDto(
                lpc.Id,
                pc.Id,
                pc.Word,
                pc.PronunciationIpa,
                pc.AudioBritishUrl,
                pc.AudioAustralianUrl,
                pc.DefinitionEn,
                lpc.Easiness,
                lpc.IntervalDays,
                lpc.Repetitions,
                lpc.RetentionScore,
                lpc.NextReviewAt,
                lpc.LastReviewedAt,
                lpc.AddedAt))
            .Take(take)
            .ToListAsync(ct);

        return rows;
    }

    // ── SubmitReviewAsync ───────────────────────────────────────────────────

    public async Task<PronunciationReviewResult> SubmitReviewAsync(
        string userId, Guid cardId, int quality, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID must not be empty.", nameof(userId));
        if (quality < 0 || quality > 5)
            throw new ArgumentOutOfRangeException(nameof(quality), "Quality must be in the range 0..5.");

        var card = await db.LearnerPronunciationCards
            .FirstOrDefaultAsync(x => x.Id == cardId && x.UserId == userId, ct)
            ?? throw new KeyNotFoundException(
                $"LearnerPronunciationCard {cardId} not found for user {userId}.");

        var now = DateTimeOffset.UtcNow;

        // SM-2 algorithm — spec variant:
        //   quality < 3  → reset repetitions, interval = 1 day.
        //   quality >= 3 → advance repetitions; interval grows 1d → 6d →
        //                  round(intervalDays * easiness) for subsequent reps.
        //   easiness     → clamped to 1.3, updated by the SM-2 formula.
        //   retentionScore → clamped 0..100, additively adjusted by
        //                    (quality-3)*10 so 5 → +20, 4 → +10, 3 → 0,
        //                    2 → -10, 1 → -20, 0 → -30.
        if (quality < 3)
        {
            card.Repetitions = 0;
            card.IntervalDays = 1;
        }
        else
        {
            card.Repetitions += 1;
            card.IntervalDays = card.Repetitions switch
            {
                1 => 1,
                2 => 6,
                _ => Math.Max(1, (int)Math.Round(card.IntervalDays * (double)card.Easiness)),
            };
        }

        var delta = 0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02);
        card.Easiness = Math.Max(1.3m, card.Easiness + (decimal)delta);

        var retentionDelta = (quality - 3) * 10;
        var nextRetention = card.RetentionScore + retentionDelta;
        card.RetentionScore = Math.Clamp(nextRetention, 0, 100);

        card.NextReviewAt = now.AddDays(card.IntervalDays);
        card.LastReviewedAt = now;

        await db.SaveChangesAsync(ct);

        logger?.LogDebug(
            "SubmitReviewAsync — user={UserId} card={CardId} quality={Quality} interval={Interval} reps={Reps} retention={Retention}",
            userId, cardId, quality, card.IntervalDays, card.Repetitions, card.RetentionScore);

        return new PronunciationReviewResult(
            CardId: card.Id,
            IntervalDays: card.IntervalDays,
            NextReviewAt: card.NextReviewAt,
            RetentionScore: card.RetentionScore);
    }

    // ── GetStatsAsync ───────────────────────────────────────────────────────

    public async Task<PronunciationStatsDto> GetStatsAsync(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID must not be empty.", nameof(userId));

        var rows = await db.LearnerPronunciationCards
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new { x.Repetitions, x.NextReviewAt })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return new PronunciationStatsDto(0, 0, 0, 0, 0);

        var now = DateTimeOffset.UtcNow;
        var mastered = rows.Count(r => r.Repetitions >= 4);
        var learning = rows.Count(r => r.Repetitions is >= 1 and <= 3);
        var struggling = rows.Count(r => r.Repetitions == 0);
        var dueToday = rows.Count(r => r.NextReviewAt <= now);

        return new PronunciationStatsDto(
            Total: rows.Count,
            Mastered: mastered,
            Learning: learning,
            Struggling: struggling,
            DueToday: dueToday);
    }

    // ── EnsureCardExistsAsync ───────────────────────────────────────────────

    public async Task<PronunciationCard> EnsureCardExistsAsync(string word, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(word))
            throw new ArgumentException("Word must not be empty.", nameof(word));

        var normalised = word.Trim();
        var lower = normalised.ToLowerInvariant();

        // Case-insensitive lookup against the master table.
        var existing = await db.PronunciationCards
            .FirstOrDefaultAsync(c => c.Word.ToLower() == lower, ct);
        if (existing is not null)
            return existing;

        // No master row — create a stub. Audio assets and IPA fields will be
        // populated by an authoring workflow; until then the learner gets the
        // SM-2 scheduling without the audio playback.
        var now = DateTimeOffset.UtcNow;
        var card = new PronunciationCard
        {
            Id = Guid.NewGuid(),
            Word = normalised,
            PronunciationIpa = string.Empty,
            BritishIpa = string.Empty,
            AustralianIpa = string.Empty,
            AmericanIpa = string.Empty,
            AudioBritishUrl = null,
            AudioAustralianUrl = null,
            AudioAmericanUrl = null,
            DefinitionEn = string.Empty,
            DefinitionAr = string.Empty,
            SyllableCount = 0,
            StressPattern = string.Empty,
            CommonMispronunciationsJson = "[]",
            SimilarSoundingTrapsJson = "[]",
            Difficulty = 5,
            ProfessionRelevanceJson = "[]",
            CreatedAt = now,
        };

        db.PronunciationCards.Add(card);
        await db.SaveChangesAsync(ct);
        logger?.LogInformation("EnsureCardExistsAsync — created stub master card for '{Word}' ({Id}).", normalised, card.Id);
        return card;
    }

    // ── RemoveCardAsync ─────────────────────────────────────────────────────

    public async Task RemoveCardAsync(string userId, Guid cardId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID must not be empty.", nameof(userId));

        var card = await db.LearnerPronunciationCards
            .FirstOrDefaultAsync(x => x.Id == cardId && x.UserId == userId, ct);
        if (card is null)
            return; // idempotent — already absent.

        db.LearnerPronunciationCards.Remove(card);
        await db.SaveChangesAsync(ct);
    }
}
