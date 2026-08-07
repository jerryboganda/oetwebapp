using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.VideoLibrary;

/// <summary>
/// Resolves the per-user video scope consistently for both catalog listing and
/// playback. Explicitly allocated videos remain available, while videos first
/// published after the initial scope was created are automatically included.
/// This lets the catalog grow without requiring an admin to edit every learner.
/// </summary>
public sealed record UserVideoAccessScope(
    IReadOnlySet<string>? AllowedVideoIds,
    DateTimeOffset? AutomaticallyIncludedAfter)
{
    public bool Allows(LibraryVideo video)
    {
        var allowedVideoIds = AllowedVideoIds;
        if (allowedVideoIds is not { Count: > 0 }) return true;
        if (allowedVideoIds.Contains(video.Id)) return true;

        var availableAt = video.PublishedAt ?? video.CreatedAt;
        return AutomaticallyIncludedAfter is { } cutoff && availableAt > cutoff;
    }

    public static async Task<UserVideoAccessScope> LoadAsync(
        LearnerDbContext db,
        string userId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return new(null, null);

        var rows = await db.UserVideoAccesses.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new { x.VideoId, x.CreatedAt })
            .ToListAsync(ct);

        if (rows.Count == 0) return new(null, null);

        // Keep the first scope timestamp stable even when an admin later
        // replaces the selected ids. UserAccessAllocationService preserves the
        // same timestamp when it rewrites the rows.
        return new(
            rows.Select(x => x.VideoId).ToHashSet(StringComparer.Ordinal),
            rows.Min(x => x.CreatedAt));
    }
}
