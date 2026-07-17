using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Writing;

public interface IWritingCaseNoteHighlightService
{
    /// <summary>The learner's saved highlights JSON for a scenario, or "{}" when none.</summary>
    Task<string> GetAsync(string userId, Guid scenarioId, CancellationToken ct);

    /// <summary>Upserts the learner's highlights for a scenario; returns the stored JSON.</summary>
    Task<string> SaveAsync(string userId, Guid scenarioId, string highlightsJson, CancellationToken ct);
}

/// <summary>
/// Persists a learner's Case Notes PDF highlights per (UserId, ScenarioId). One
/// row per learner per scenario, upserted as marks change — so highlights pre-load
/// on every future attempt regardless of mode. The submission keeps its own
/// snapshot for results / tutor review (see <see cref="WritingSubmission.CaseNoteHighlightsJson"/>).
/// </summary>
public sealed class WritingCaseNoteHighlightService(LearnerDbContext db, TimeProvider clock) : IWritingCaseNoteHighlightService
{
    private const string Empty = "{}";

    public async Task<string> GetAsync(string userId, Guid scenarioId, CancellationToken ct)
    {
        var row = await db.WritingCaseNoteHighlights.AsNoTracking()
            .FirstOrDefaultAsync(h => h.UserId == userId && h.ScenarioId == scenarioId, ct);
        return string.IsNullOrWhiteSpace(row?.HighlightsJson) ? Empty : row!.HighlightsJson;
    }

    public async Task<string> SaveAsync(string userId, Guid scenarioId, string highlightsJson, CancellationToken ct)
    {
        var json = string.IsNullOrWhiteSpace(highlightsJson) ? Empty : highlightsJson;
        var now = clock.GetUtcNow();
        var row = await db.WritingCaseNoteHighlights
            .FirstOrDefaultAsync(h => h.UserId == userId && h.ScenarioId == scenarioId, ct);
        if (row is null)
        {
            row = new WritingCaseNoteHighlight
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ScenarioId = scenarioId,
                CreatedAt = now,
            };
            db.WritingCaseNoteHighlights.Add(row);
        }
        row.HighlightsJson = json;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return json;
    }
}
