using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public class ContentDeduplicationService(LearnerDbContext db)
{
    /// <summary>
    /// Compute a content fingerprint for dedup detection.
    /// Uses title (normalized) + subtest + profession + scenario type.
    /// </summary>
    public static string ComputeFingerprint(ContentItem item)
    {
        var normalized = NormalizeTitle(item.Title);
        var input = $"{normalized}|{item.SubtestCode}|{item.ProfessionId ?? "general"}|{item.ScenarioType ?? "none"}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash)[..16];
    }

    private static string NormalizeTitle(string title)
    {
        // Lowercase, remove extra spaces, trim, strip common prefixes
        var t = title.Trim().ToLowerInvariant();
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ");
        // Remove batch identifiers like "(batch 3)", "(march 2026)"
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\(batch\s*\d+\)", "");
        t = System.Text.RegularExpressions.Regex.Replace(
            t, @"\((jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)\w*\s*\d{4}\)", "");
        return t.Trim();
    }

    /// <summary>
    /// Scan all content items and assign DuplicateGroupId to items that share the same fingerprint.
    /// Returns the number of duplicate groups found.
    /// </summary>
    public async Task<DedupScanResult> ScanForDuplicatesAsync(CancellationToken ct)
    {
        var items = await db.ContentItems
            .Where(c => c.Status != ContentStatus.Archived && c.FreshnessConfidence != "superseded")
            .ToListAsync(ct);

        var fingerprintGroups = items
            .GroupBy(ComputeFingerprint)
            .Where(g => g.Count() > 1)
            .ToList();

        var groupsCreated = 0;
        var itemsTagged = 0;

        foreach (var group in fingerprintGroups)
        {
            var groupId = $"dup-{group.Key}";
            foreach (var item in group)
            {
                if (item.DuplicateGroupId != groupId)
                {
                    item.DuplicateGroupId = groupId;
                    itemsTagged++;
                }
            }
            groupsCreated++;
        }

        await db.SaveChangesAsync(ct);
        return new DedupScanResult(groupsCreated, itemsTagged);
    }

    /// <summary>
    /// Get all duplicate groups with their items.
    /// </summary>
    public async Task<object> GetDuplicateGroupsAsync(int page, int pageSize, CancellationToken ct)
    {
        var groups = await db.ContentItems
            .Where(c => c.DuplicateGroupId != null)
            .GroupBy(c => c.DuplicateGroupId!)
            .Select(g => new
            {
                DuplicateGroupId = g.Key,
                Count = g.Count(),
                Items = g.OrderByDescending(c => c.QualityScore)
                         .ThenByDescending(c => c.UpdatedAt)
                         .Select(c => new
                         {
                             c.Id,
                             c.Title,
                             c.SubtestCode,
                             c.ProfessionId,
                             c.ScenarioType,
                             c.InstructionLanguage,
                             c.SourceProvenance,
                             c.FreshnessConfidence,
                             c.QualityScore,
                             c.Status,
                             c.SupersededById,
                             c.CanonicalSourcePath,
                             c.CreatedAt,
                             c.UpdatedAt
                         })
                         .ToList()
            })
            .ToListAsync(ct);

        var total = groups.Count;
        var paged = groups
            .OrderByDescending(g => g.Count)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new { items = paged, total, page, pageSize };
    }

    /// <summary>
    /// Get a single duplicate group by ID with full items.
    /// </summary>
    public async Task<object?> GetDuplicateGroupAsync(string groupId, CancellationToken ct)
    {
        var items = await db.ContentItems
            .Where(c => c.DuplicateGroupId == groupId)
            .OrderByDescending(c => c.QualityScore)
            .ThenByDescending(c => c.UpdatedAt)
            .ToListAsync(ct);

        if (items.Count == 0) return null;
        return new { DuplicateGroupId = groupId, Count = items.Count, Items = items };
    }

    /// <summary>
    /// Designate one item in a duplicate group as the canonical version.
    /// All other items in the group get marked as superseded and point to the canonical item.
    /// </summary>
    public async Task<bool> DesignateCanonicalAsync(string groupId, string canonicalItemId, CancellationToken ct)
    {
        var items = await db.ContentItems
            .Where(c => c.DuplicateGroupId == groupId)
            .ToListAsync(ct);

        if (items.Count == 0) return false;
        var canonical = items.FirstOrDefault(c => c.Id == canonicalItemId);
        if (canonical is null) return false;

        foreach (var item in items)
        {
            if (item.Id == canonicalItemId)
            {
                // Canonical item keeps Published status, clears superseded reference
                item.SupersededById = null;
                item.FreshnessConfidence = "current";
            }
            else
            {
                // Non-canonical items get superseded
                item.SupersededById = canonicalItemId;
                item.FreshnessConfidence = "superseded";
                item.Status = ContentStatus.Archived;
                item.ArchivedAt = DateTimeOffset.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Remove an item from its duplicate group (admin decides it's not actually a duplicate).
    /// </summary>
    public async Task<bool> RemoveFromGroupAsync(string itemId, CancellationToken ct)
    {
        var item = await db.ContentItems.FindAsync([itemId], ct);
        if (item is null || item.DuplicateGroupId is null) return false;

        var groupId = item.DuplicateGroupId;
        item.DuplicateGroupId = null;
        item.SupersededById = null;

        // If only 1 item remains in the group, remove that one too
        var remaining = await db.ContentItems
            .Where(c => c.DuplicateGroupId == groupId && c.Id != itemId)
            .ToListAsync(ct);

        if (remaining.Count == 1)
        {
            remaining[0].DuplicateGroupId = null;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }
}

public record DedupScanResult(int GroupsFound, int ItemsTagged);
