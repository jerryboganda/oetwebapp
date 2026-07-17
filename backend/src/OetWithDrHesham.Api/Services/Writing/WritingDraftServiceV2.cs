using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Writing;

public sealed record WritingDraftV2View(
    string UserId,
    Guid ScenarioId,
    string Mode,
    string Content,
    int WordCount,
    int TimeSpentSeconds,
    DateTimeOffset LastSavedAt);

public sealed record WritingDraftV2SaveRequest(string Content, int WordCount, int TimeSpentSeconds);

public interface IWritingDraftServiceV2
{
    Task<WritingDraftV2View?> GetAsync(string userId, Guid scenarioId, string mode, CancellationToken ct);
    Task<WritingDraftV2View> SaveAsync(string userId, Guid scenarioId, string mode, WritingDraftV2SaveRequest request, CancellationToken ct);
    Task DeleteAsync(string userId, Guid scenarioId, string mode, CancellationToken ct);
}

/// <summary>
/// Port of WritingDraftService onto the V2 (UserId, ScenarioId, Mode) unique
/// table. Writes go ONLY to V2. Reads search V2 first, then optionally fall
/// back to the legacy table — kept here for the grace-period window. The
/// legacy table is read-only at this layer (any save promotes the row to V2).
/// </summary>
public sealed class WritingDraftServiceV2(LearnerDbContext db, TimeProvider clock) : IWritingDraftServiceV2
{
    public async Task<WritingDraftV2View?> GetAsync(string userId, Guid scenarioId, string mode, CancellationToken ct)
    {
        var normalizedMode = NormaliseMode(mode);
        var draft = await db.WritingDraftsV2.AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == userId && d.ScenarioId == scenarioId && d.Mode == normalizedMode, ct);
        if (draft is not null) return ToView(draft);
        return null;
    }

    public async Task<WritingDraftV2View> SaveAsync(string userId, Guid scenarioId, string mode, WritingDraftV2SaveRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedMode = NormaliseMode(mode);
        var now = clock.GetUtcNow();
        var entity = await db.WritingDraftsV2.FirstOrDefaultAsync(d => d.UserId == userId && d.ScenarioId == scenarioId && d.Mode == normalizedMode, ct);
        if (entity is null)
        {
            entity = new WritingDraftV2
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ScenarioId = scenarioId,
                Mode = normalizedMode,
                CreatedAt = now,
            };
            db.WritingDraftsV2.Add(entity);
        }
        entity.Content = request.Content ?? string.Empty;
        entity.WordCount = Math.Max(0, request.WordCount);
        entity.TimeSpentSeconds = Math.Max(0, request.TimeSpentSeconds);
        entity.LastSavedAt = now;
        await db.SaveChangesAsync(ct);
        return ToView(entity);
    }

    public async Task DeleteAsync(string userId, Guid scenarioId, string mode, CancellationToken ct)
    {
        var normalizedMode = NormaliseMode(mode);
        var entity = await db.WritingDraftsV2.FirstOrDefaultAsync(d => d.UserId == userId && d.ScenarioId == scenarioId && d.Mode == normalizedMode, ct);
        if (entity is null) return;
        db.WritingDraftsV2.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    private static WritingDraftV2View ToView(WritingDraftV2 entity)
        => new(entity.UserId, entity.ScenarioId, entity.Mode, entity.Content, entity.WordCount, entity.TimeSpentSeconds, entity.LastSavedAt);

    private static string NormaliseMode(string? mode)
        => string.IsNullOrWhiteSpace(mode) ? "practice" : mode.Trim().ToLowerInvariant();
}
