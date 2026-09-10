using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Recalls;

/// <summary>
/// Recall vocabulary spelling — Practice Spelling (§3B) and the mini Spelling
/// Test (§3C), plus the per-learner "Review Mistakes" list.
///
/// Design constraints from the developer brief (§3D):
/// <list type="bullet">
/// <item><b>No AI/LLM tokens.</b> Pass/fail is a plain string comparison against
/// the canonical spelling already stored on <see cref="VocabularyTerm"/>.</item>
/// <item><b>No audio regeneration.</b> This service never touches TTS; the client
/// replays the existing audio through the existing
/// <c>GET /v1/recalls/audio/{termId}</c> route.</item>
/// <item><b>No credit deduction.</b> Nothing here routes through the AI gateway
/// or the credit ledger, so spelling practice cannot consume AI credits.</item>
/// </list>
///
/// Mistakes are stored server-side (one row per learner + word) so the list
/// survives logout, app restart and switching device. A row is created on the
/// first miss, its counter incremented on every later miss, and deleted as soon
/// as the learner spells that word correctly.
/// </summary>
public sealed class RecallSpellingService(LearnerDbContext db)
{
    /// <summary>Hard ceiling on words returned by one set request.</summary>
    private const int MaxSetSize = 500;

    /// <summary>
    /// How many candidate rows are pulled before shuffling in memory. Comfortably
    /// larger than any realistic recall bank, so a 10/20/30-word set is drawn from
    /// the whole catalogue rather than from its first page.
    /// </summary>
    /// <remarks>
    /// ponytail: in-memory shuffle, ceiling = <see cref="CandidatePool"/> rows.
    /// Chosen because <c>ORDER BY random()</c> has no provider-portable EF Core
    /// translation (the test suite runs on SQLite, production on Postgres).
    /// Upgrade path: once a provider-specific expression is verified for both, move
    /// the ordering into SQL and drop the pool.
    /// </remarks>
    private const int CandidatePool = 2000;

    /// <summary>
    /// Grade one typed answer.
    ///
    /// Comparison rule (§3B): ignore letter case and leading/trailing spaces, but
    /// internal spaces and hyphens must match exactly — "advance care plan" and
    /// "advancecareplan" are different, and so are "well-being" and "wellbeing".
    /// </summary>
    public async Task<RecallSpellingCheckResponse> CheckAsync(
        string userId,
        RecallSpellingCheckRequest request,
        bool isPremium,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TermId))
            throw ApiException.Validation("TERM_ID_REQUIRED", "Term id is required.");
        if (request.Typed is null)
            throw ApiException.Validation("TYPED_REQUIRED", "A typed answer is required.");

        var term = await ResolveTermAsync(request.TermId.Trim(), isPremium, ct);
        var correct = IsCorrectSpelling(term.Term, request.Typed);

        var existing = await db.RecallSpellingMistakes
            .FirstOrDefaultAsync(m => m.UserId == userId && m.VocabularyTermId == term.Id, ct);

        var now = DateTimeOffset.UtcNow;
        var added = false;
        var removed = false;

        if (correct)
        {
            // Answered correctly — the word leaves Review Mistakes entirely.
            if (existing is not null)
            {
                db.RecallSpellingMistakes.Remove(existing);
                removed = true;
                await db.SaveChangesAsync(ct);
            }
        }
        else if (existing is null)
        {
            db.RecallSpellingMistakes.Add(new RecallSpellingMistake
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                VocabularyTermId = term.Id,
                WrongAttemptCount = 1,
                LastWrongAt = now,
                CreatedAt = now,
            });
            added = true;
            await db.SaveChangesAsync(ct);
        }
        else
        {
            // Spelled wrong again — keep it and bump the count.
            existing.WrongAttemptCount += 1;
            existing.LastWrongAt = now;
            await db.SaveChangesAsync(ct);
        }

        return new RecallSpellingCheckResponse(
            Correct: correct,
            Canonical: term.Term,
            WrongAttemptCount: correct ? 0 : (existing?.WrongAttemptCount ?? 1),
            AddedToMistakes: added,
            RemovedFromMistakes: removed);
    }

    /// <summary>
    /// The learner's active Review Mistakes list — most recently missed first.
    /// Only references the existing recall word; nothing is duplicated.
    /// </summary>
    public async Task<RecallSpellingMistakesResponse> GetMistakesAsync(
        string userId, bool isPremium, CancellationToken ct)
    {
        var items = await db.RecallSpellingMistakes
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Join(RecallTermScope(isPremium), m => m.VocabularyTermId, t => t.Id, (m, t) => new { m, t })
            .OrderByDescending(x => x.m.LastWrongAt)
            .Take(MaxSetSize)
            .Select(x => new RecallSpellingMistakeItem(
                x.t.Id,
                x.t.Term,
                x.t.Category,
                x.t.Definition,
                x.t.IpaPronunciation,
                x.t.AudioMediaAssetId != null && x.t.AudioMediaAssetId != "",
                x.m.WrongAttemptCount,
                x.m.LastWrongAt))
            .ToListAsync(ct);

        return new RecallSpellingMistakesResponse(items, items.Count);
    }

    /// <summary>
    /// Build the word set for a spelling test.
    ///
    /// <paramref name="source"/>: <c>all</c> (default) | <c>favorites</c> |
    /// <c>mistakes</c>. <paramref name="size"/>: <c>10</c> | <c>20</c> | <c>30</c>
    /// | <c>all</c>.
    ///
    /// The canonical spelling is deliberately <b>not</b> returned — the answer stays
    /// hidden until the learner presses Check, which grades server-side. Words are
    /// restricted to those that already have pronunciation audio, because the test
    /// is audio-driven (§3D: reuse the existing audio, never regenerate).
    /// </summary>
    public async Task<RecallSpellingSetResponse> GetSetAsync(
        string userId, string? size, string? source, bool isPremium, CancellationToken ct)
    {
        var take = ParseSize(size);
        var normalisedSource = (source ?? "all").Trim().ToLowerInvariant();
        if (normalisedSource is not ("all" or "favorites" or "mistakes")) normalisedSource = "all";

        if (normalisedSource == "mistakes")
        {
            // Most recently missed first — this doubles as the review order.
            var items = await db.RecallSpellingMistakes
                .AsNoTracking()
                .Where(m => m.UserId == userId)
                .Join(RecallTermScope(isPremium), m => m.VocabularyTermId, t => t.Id, (m, t) => new { m, t })
                .Where(x => x.t.AudioMediaAssetId != null && x.t.AudioMediaAssetId != "")
                .OrderByDescending(x => x.m.LastWrongAt)
                .Take(take ?? MaxSetSize)
                .Select(x => new RecallSpellingSetItem(
                    x.t.Id, x.t.Category, x.t.ExamFrequencyCount, true))
                .ToListAsync(ct);

            return new RecallSpellingSetResponse(items, items.Count, normalisedSource, take?.ToString() ?? "all");
        }

        var candidatesQuery = normalisedSource == "favorites"
            ? db.RecallBookmarks
                .AsNoTracking()
                .Where(b => b.UserId == userId)
                .Join(RecallTermScope(isPremium), b => b.VocabularyTermId, t => t.Id, (b, t) => t)
            : RecallTermScope(isPremium);

        var candidates = await candidatesQuery
            .Where(t => t.AudioMediaAssetId != null && t.AudioMediaAssetId != "")
            .OrderBy(t => t.Term)
            .Take(CandidatePool)
            .Select(t => new RecallSpellingSetItem(
                t.Id, t.Category, t.ExamFrequencyCount, false))
            .ToListAsync(ct);

        // "All Words" means all of them, so the stored order is kept. A sized set is
        // shuffled so repeat tests are not the same ten words in the same order.
        var items = take is { } n
            ? Shuffle(candidates).Take(n).ToList()
            : candidates.Take(MaxSetSize).ToList();

        return new RecallSpellingSetResponse(items, items.Count, normalisedSource, take?.ToString() ?? "all");
    }

    /// <summary>
    /// Case-insensitive comparison of the trimmed strings. Internal whitespace and
    /// hyphens are significant, so the comparison is ordinal — no collapsing, no
    /// punctuation stripping, no fuzzy matching.
    /// </summary>
    public static bool IsCorrectSpelling(string canonical, string? typed)
        => string.Equals(
            (typed ?? string.Empty).Trim(),
            (canonical ?? string.Empty).Trim(),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The single definition of "a recall word we may spell-test": active, part of
    /// at least one recall set (same scope as <c>RecallsService.EnsureAudioAsync</c>),
    /// and — for non-premium learners — a curated free-preview term. Locked content
    /// must never leak through the spelling surface.
    /// </summary>
    private IQueryable<VocabularyTerm> RecallTermScope(bool isPremium)
    {
        var query = db.VocabularyTerms
            .AsNoTracking()
            .Where(t => t.Status == "active"
                        && t.RecallSetCodesJson != null
                        && t.RecallSetCodesJson != ""
                        && t.RecallSetCodesJson != "[]");

        if (!isPremium) query = query.Where(t => t.IsFreePreview);
        return query;
    }

    private async Task<VocabularyTerm> ResolveTermAsync(string termId, bool isPremium, CancellationToken ct)
    {
        var term = await db.VocabularyTerms
            .FirstOrDefaultAsync(t => t.Id == termId
                                      && t.Status == "active"
                                      && t.RecallSetCodesJson != null
                                      && t.RecallSetCodesJson != ""
                                      && t.RecallSetCodesJson != "[]", ct)
            ?? throw ApiException.NotFound("TERM_NOT_FOUND", "Term not found.");

        if (!isPremium && !term.IsFreePreview)
        {
            throw ApiException.PaymentRequired(
                "subscription_required",
                "Spelling practice is available for paid candidates on this word.");
        }

        return term;
    }

    private static IReadOnlyList<T> Shuffle<T>(IReadOnlyList<T> source)
    {
        var copy = source.ToArray();
        Random.Shared.Shuffle(copy);
        return copy;
    }

    /// <summary><c>null</c> means "all words".</summary>
    private static int? ParseSize(string? size)
    {
        if (string.IsNullOrWhiteSpace(size)) return 10;
        var trimmed = size.Trim().ToLowerInvariant();
        if (trimmed is "all" or "allwords" or "all-words") return null;
        return int.TryParse(trimmed, out var n) && n > 0 ? Math.Min(n, MaxSetSize) : 10;
    }
}

// ── DTOs ────────────────────────────────────────────────────────────────

public record RecallSpellingCheckRequest(string TermId, string? Typed);

/// <summary>
/// Result of grading one typed answer.
/// <c>Canonical</c> is the stored canonical spelling, revealed after Check.
/// <c>WrongAttemptCount</c> is 0 once the word is answered correctly (the row is
/// removed), otherwise the number of times this learner has missed it.
/// </summary>
public record RecallSpellingCheckResponse(
    bool Correct,
    string Canonical,
    int WrongAttemptCount,
    bool AddedToMistakes,
    bool RemovedFromMistakes);

public record RecallSpellingMistakeItem(
    string TermId,
    string Term,
    string Category,
    string Definition,
    string? Ipa,
    bool HasAudio,
    int WrongAttemptCount,
    DateTimeOffset LastWrongAt);

public record RecallSpellingMistakesResponse(IReadOnlyList<RecallSpellingMistakeItem> Items, int Total);

/// <summary>
/// One word in a spelling test. Deliberately omits the canonical spelling so the
/// answer is not present in the payload before Check. <c>FromMistakes</c> is true
/// when the word came from the learner's Review Mistakes list.
/// </summary>
public record RecallSpellingSetItem(
    string TermId,
    string Category,
    int ExamFrequencyCount,
    bool FromMistakes);

public record RecallSpellingSetResponse(
    IReadOnlyList<RecallSpellingSetItem> Items,
    int Total,
    string Source,
    string Size);
