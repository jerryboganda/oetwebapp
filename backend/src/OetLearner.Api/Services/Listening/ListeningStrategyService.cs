using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// ListeningStrategyService — Phase 3 strategy library (OET_LISTENING_MODULE_PATHWAY.md §16).
//
// Surfaces the per-skill strategy articles (note-taking, gist, inference,
// time-management, accent, exam-day). Each strategy renders as a markdown
// article with optional video / audio attachments + an optional linked drill
// CTA. Per-learner progress tracks:
//
//   • MarkedAsRead — one-way flag set when the learner taps "Mark as read".
//   • Favorited    — toggle for the strategy bookmark.
//   • ReadAt       — first-read timestamp; never overwritten.
//
// Mirrors Reading's StrategyService.cs in shape so analytics + retention
// behaviours are consistent between the two skills.
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningStrategyService
{
    /// <summary>Fetch all published strategies, optionally filtered by
    /// <paramref name="category"/>. Each row carries the learner's
    /// MarkedAsRead + Favorited flags.</summary>
    Task<IReadOnlyList<StrategyDto>> ListAsync(
        string userId,
        string? category,
        CancellationToken ct);

    /// <summary>Fetch a single strategy + the learner's progress row (or
    /// null when the learner has never engaged with it).</summary>
    Task<StrategyDetailDto?> GetBySlugAsync(
        string userId,
        string slug,
        CancellationToken ct);

    /// <summary>Stamp the strategy as read for this learner. Idempotent — the
    /// ReadAt timestamp is set only on the first call.</summary>
    Task<LearnerListeningStrategyProgress> MarkReadAsync(
        string userId,
        Guid strategyId,
        CancellationToken ct);

    /// <summary>Toggle the Favorited flag. Creates the progress row on first
    /// invocation so the toggle persists from the listing page.</summary>
    Task<LearnerListeningStrategyProgress> ToggleFavoriteAsync(
        string userId,
        Guid strategyId,
        CancellationToken ct);
}

/// <summary>Index projection — one row per strategy on the listing page.</summary>
public sealed record StrategyDto(
    Guid Id,
    string Slug,
    string Title,
    string Category,
    int EstimatedReadMinutes,
    int Difficulty,
    bool MarkedAsRead,
    bool Favorited);

/// <summary>Detail projection — full markdown body + applicable parts list
/// (derived from the JSON column) + per-learner progress envelope.</summary>
public sealed record StrategyDetailDto(
    Guid Id,
    string Slug,
    string Title,
    string Category,
    IReadOnlyList<string> ApplicableParts,
    int EstimatedReadMinutes,
    string BodyMarkdownEn,
    string BodyMarkdownAr,
    string? VideoUrl,
    string? AudioUrl,
    LearnerListeningStrategyProgress? Progress);

public sealed class ListeningStrategyService(LearnerDbContext db) : IListeningStrategyService
{
    // ── ListAsync ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<StrategyDto>> ListAsync(
        string userId,
        string? category,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }

        var query = db.ListeningStrategies
            .AsNoTracking()
            .Where(s => s.IsPublished);

        if (!string.IsNullOrWhiteSpace(category))
        {
            var normalisedCategory = category.Trim();
            query = query.Where(s => s.Category == normalisedCategory);
        }

        var strategies = await query
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Difficulty)
            .ThenBy(s => s.Title)
            .ToListAsync(ct);

        if (strategies.Count == 0)
        {
            return Array.Empty<StrategyDto>();
        }

        var ids = strategies.Select(s => s.Id).ToList();
        var progressByStrategyId = await db.LearnerListeningStrategyProgresses
            .AsNoTracking()
            .Where(p => p.UserId == userId && ids.Contains(p.StrategyId))
            .ToDictionaryAsync(p => p.StrategyId, ct);

        return strategies
            .Select(s =>
            {
                progressByStrategyId.TryGetValue(s.Id, out var progress);
                return new StrategyDto(
                    s.Id,
                    s.Slug,
                    s.Title,
                    s.Category,
                    s.EstimatedReadMinutes,
                    s.Difficulty,
                    MarkedAsRead: progress?.MarkedAsRead ?? false,
                    Favorited: progress?.Favorited ?? false);
            })
            .ToList();
    }

    // ── GetBySlugAsync ──────────────────────────────────────────────────────

    public async Task<StrategyDetailDto?> GetBySlugAsync(
        string userId,
        string slug,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("slug is required.", nameof(slug));
        }

        var normalisedSlug = slug.Trim();
        var strategy = await db.ListeningStrategies
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Slug == normalisedSlug && s.IsPublished, ct);

        if (strategy is null)
        {
            return null;
        }

        var progress = await db.LearnerListeningStrategyProgresses
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.StrategyId == strategy.Id, ct);

        var applicableParts = DeserialiseStringList(strategy.ApplicablePartsJson);

        return new StrategyDetailDto(
            strategy.Id,
            strategy.Slug,
            strategy.Title,
            strategy.Category,
            applicableParts,
            strategy.EstimatedReadMinutes,
            strategy.BodyMarkdownEn,
            strategy.BodyMarkdownAr,
            strategy.VideoUrl,
            strategy.AudioUrl,
            progress);
    }

    // ── MarkReadAsync ───────────────────────────────────────────────────────

    public async Task<LearnerListeningStrategyProgress> MarkReadAsync(
        string userId,
        Guid strategyId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }
        if (strategyId == Guid.Empty)
        {
            throw new ArgumentException("strategyId is required.", nameof(strategyId));
        }

        await EnsureStrategyExistsAsync(strategyId, ct);

        var progress = await GetOrCreateProgressAsync(userId, strategyId, ct);
        progress.MarkedAsRead = true;

        // Only stamp ReadAt the first time so analytics can measure "first
        // read latency". Subsequent re-saves are no-ops on the timestamp.
        if (progress.ReadAt == default)
        {
            progress.ReadAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return progress;
    }

    // ── ToggleFavoriteAsync ─────────────────────────────────────────────────

    public async Task<LearnerListeningStrategyProgress> ToggleFavoriteAsync(
        string userId,
        Guid strategyId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }
        if (strategyId == Guid.Empty)
        {
            throw new ArgumentException("strategyId is required.", nameof(strategyId));
        }

        await EnsureStrategyExistsAsync(strategyId, ct);

        var progress = await GetOrCreateProgressAsync(userId, strategyId, ct);
        progress.Favorited = !progress.Favorited;
        await db.SaveChangesAsync(ct);
        return progress;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task EnsureStrategyExistsAsync(Guid strategyId, CancellationToken ct)
    {
        var exists = await db.ListeningStrategies
            .AsNoTracking()
            .AnyAsync(s => s.Id == strategyId, ct);
        if (!exists)
        {
            throw new InvalidOperationException($"Listening strategy {strategyId} not found.");
        }
    }

    private async Task<LearnerListeningStrategyProgress> GetOrCreateProgressAsync(
        string userId,
        Guid strategyId,
        CancellationToken ct)
    {
        var progress = await db.LearnerListeningStrategyProgresses
            .FirstOrDefaultAsync(p => p.UserId == userId && p.StrategyId == strategyId, ct);

        if (progress is not null)
        {
            return progress;
        }

        progress = new LearnerListeningStrategyProgress
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StrategyId = strategyId,
            MarkedAsRead = false,
            Favorited = false,
            ReadAt = default,
        };
        db.LearnerListeningStrategyProgresses.Add(progress);
        return progress;
    }

    private static IReadOnlyList<string> DeserialiseStringList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }
        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json);
            return parsed ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
