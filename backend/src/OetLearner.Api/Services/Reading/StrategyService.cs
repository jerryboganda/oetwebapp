using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Strategy Service — Reading Module Pathway WS5
//
// Manages reading strategy articles authored by the content team.  Strategies
// are grouped by Category (distractor_recognition | time_management | scanning
// | inference | exam_day) and staged unlock (foundation | practice | mastery).
//
// Per-learner progress tracks: read status, favourite flag, and read timestamp.
// ═════════════════════════════════════════════════════════════════════════════

public interface IStrategyService
{
    /// <summary>Fetch all published strategies, optionally filtered by
    /// <paramref name="category"/> and/or <paramref name="stage"/>.</summary>
    Task<List<ReadingStrategy>> GetStrategiesAsync(
        string? category,
        string? stage,
        CancellationToken ct);

    /// <summary>Fetch a published strategy by slug, or null if not found / unpublished.</summary>
    Task<ReadingStrategy?> GetStrategyAsync(string slug, CancellationToken ct);

    /// <summary>Mark the strategy as read for this learner.  Stamps
    /// <see cref="ReadingStrategyProgress.ReadAt"/> to now if not already set.</summary>
    Task MarkReadAsync(string userId, Guid strategyId, CancellationToken ct);

    /// <summary>Toggle the favourite flag for this learner + strategy pair.</summary>
    Task ToggleFavoriteAsync(string userId, Guid strategyId, CancellationToken ct);

    /// <summary>Return the learner's progress row for a strategy, or null if
    /// they have never interacted with it.</summary>
    Task<ReadingStrategyProgress?> GetProgressAsync(
        string userId,
        Guid strategyId,
        CancellationToken ct);
}

public sealed class StrategyService(LearnerDbContext db) : IStrategyService
{
    // ── GetStrategiesAsync ────────────────────────────────────────────────────

    public async Task<List<ReadingStrategy>> GetStrategiesAsync(
        string? category,
        string? stage,
        CancellationToken ct)
    {
        var query = db.ReadingStrategies
            .AsNoTracking()
            .Where(s => s.IsPublished);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(s => s.Category == category);

        if (!string.IsNullOrWhiteSpace(stage))
            query = query.Where(s => s.UnlockStage == stage);

        return await query
            .OrderBy(s => s.Difficulty)
            .ThenBy(s => s.Title)
            .ToListAsync(ct);
    }

    // ── GetStrategyAsync ──────────────────────────────────────────────────────

    public async Task<ReadingStrategy?> GetStrategyAsync(string slug, CancellationToken ct)
        => await db.ReadingStrategies
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Slug == slug && s.IsPublished, ct);

    // ── MarkReadAsync ─────────────────────────────────────────────────────────

    public async Task MarkReadAsync(string userId, Guid strategyId, CancellationToken ct)
    {
        var progress = await GetOrCreateProgressAsync(userId, strategyId, ct);

        progress.MarkedAsRead = true;

        // Only stamp ReadAt the first time
        if (progress.ReadAt == default)
            progress.ReadAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    // ── ToggleFavoriteAsync ───────────────────────────────────────────────────

    public async Task ToggleFavoriteAsync(string userId, Guid strategyId, CancellationToken ct)
    {
        var progress = await GetOrCreateProgressAsync(userId, strategyId, ct);
        progress.Favorited = !progress.Favorited;
        await db.SaveChangesAsync(ct);
    }

    // ── GetProgressAsync ──────────────────────────────────────────────────────

    public async Task<ReadingStrategyProgress?> GetProgressAsync(
        string userId,
        Guid strategyId,
        CancellationToken ct)
        => await db.ReadingStrategyProgresses
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.StrategyId == strategyId, ct);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<ReadingStrategyProgress> GetOrCreateProgressAsync(
        string userId,
        Guid strategyId,
        CancellationToken ct)
    {
        var progress = await db.ReadingStrategyProgresses
            .FirstOrDefaultAsync(p => p.UserId == userId && p.StrategyId == strategyId, ct);

        if (progress is not null)
            return progress;

        progress = new ReadingStrategyProgress
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StrategyId = strategyId,
            MarkedAsRead = false,
            Favorited = false
        };
        db.ReadingStrategyProgresses.Add(progress);
        return progress;
    }
}
