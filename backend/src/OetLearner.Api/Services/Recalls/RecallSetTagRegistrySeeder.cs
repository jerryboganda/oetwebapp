using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Recalls;

/// <summary>
/// Seeds the <see cref="RecallSetTag"/> table with the 3 canonical codes from
/// <see cref="RecallSetCodes"/> on first boot. Idempotent: only inserts a row
/// when no row exists for that code. Existing rows' display data is left
/// alone so admin edits aren't clobbered.
/// </summary>
public static class RecallSetTagRegistrySeeder
{
    public static async Task EnsureAsync(LearnerDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await db.RecallSetTags.Select(x => x.Code).ToListAsync(ct);
        var existingSet = new HashSet<string>(existing, StringComparer.Ordinal);
        var added = 0;
        foreach (var meta in RecallSetCodes.Metadata)
        {
            if (existingSet.Contains(meta.Code)) continue;
            db.RecallSetTags.Add(new RecallSetTag
            {
                Code = meta.Code,
                DisplayName = meta.DisplayName,
                ShortLabel = meta.ShortLabel,
                Description = meta.Description,
                SortOrder = meta.SortOrder,
                IsActive = true,
                ExamTypeCode = "oet",
                CreatedByUserId = "system:seeder",
                CreatedAt = now,
                UpdatedAt = now,
            });
            added++;
        }
        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("RecallSetTagRegistrySeeder: seeded {Added} canonical recall set tag(s).", added);
        }
    }
}
