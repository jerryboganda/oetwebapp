using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

// Phase 5 (G) of the OET Speaking module roadmap.
//
// Admin CRUD for `SpeakingDrillItem`. Mirrors the existing
// `AdminService.SpeakingMockSets.cs` style: each operation is audit-
// logged + transactional, and the publish gate flips both the
// underlying `ContentItem.Status` and surfaces the row to learner
// listings.
//
// Each drill is backed by a `ContentItem` (ContentType="speaking_drill",
// SubtestCode="speaking") whose Title/DetailJson hold the learner-facing
// prompt + instruction text. The `SpeakingDrillItem` row holds the
// drill kind, criteria mapping, and recommendation threshold.
//
// Permissions reuse `AdminContentRead` / `AdminContentWrite` /
// `AdminContentPublish` so admins authoring role-play cards already
// have the right grants.
public partial class AdminService
{
    private static readonly HashSet<string> ValidDrillKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Opening","Empathy","Ice","OpenQuestion","LayLanguage","Signposting",
        "CheckingUnderstanding","Reassurance","Closing","Pronunciation","Fluency","Grammar",
    };

    public async Task<object> ListSpeakingDrillsAsync(
        string? drillKind,
        string? professionId,
        string? status,
        CancellationToken ct)
    {
        var q = from drill in db.SpeakingDrillItems.AsNoTracking()
                join content in db.ContentItems.AsNoTracking()
                    on drill.ContentItemId equals content.Id
                select new { drill, content };

        if (!string.IsNullOrWhiteSpace(drillKind)
            && Enum.TryParse<SpeakingDrillKind>(drillKind.Trim(), ignoreCase: true, out var kind))
        {
            q = q.Where(x => x.drill.DrillKind == kind);
        }
        if (!string.IsNullOrWhiteSpace(professionId))
        {
            var pid = professionId.Trim();
            q = q.Where(x => x.content.ProfessionId == pid);
        }
        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<ContentStatus>(status.Trim(), ignoreCase: true, out var st))
        {
            q = q.Where(x => x.content.Status == st);
        }

        var rows = await q
            .OrderBy(x => x.drill.DrillKind)
            .ThenBy(x => x.content.Title)
            .Take(500)
            .ToListAsync(ct);

        return new
        {
            drills = rows.Select(r => ProjectSummaryRow(r.drill, r.content)).ToArray(),
        };
    }

    public async Task<object> GetSpeakingDrillAsync(string drillId, CancellationToken ct)
    {
        var drill = await db.SpeakingDrillItems.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == drillId, ct)
            ?? throw ApiException.NotFound("speaking_drill_not_found", "That speaking drill does not exist.");
        var content = await db.ContentItems.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == drill.ContentItemId, ct)
            ?? throw ApiException.NotFound("speaking_drill_content_missing",
                "The underlying content item for this drill is missing.");
        return ProjectDetail(drill, content);
    }

    public async Task<object> CreateSpeakingDrillAsync(
        string adminId,
        string adminName,
        AdminDrillCreateRequest request,
        CancellationToken ct)
    {
        ValidateDrillRequest(request.DrillKind, request.Title, request.InstructionText, request.TargetCriteria);
        var kind = ParseDrillKind(request.DrillKind);

        var now = DateTimeOffset.UtcNow;
        var contentId = $"ci-drill-{Guid.NewGuid():N}";
        var drillId = $"sdi-{Guid.NewGuid():N}";

        var content = new ContentItem
        {
            Id = contentId,
            ContentType = "speaking_drill",
            SubtestCode = "speaking",
            ProfessionId = string.IsNullOrWhiteSpace(request.ProfessionId) ? null : request.ProfessionId.Trim(),
            Title = request.Title.Trim(),
            Difficulty = "core",
            EstimatedDurationMinutes = 1,
            CriteriaFocusJson = JsonSupport.Serialize(request.TargetCriteria ?? Array.Empty<string>()),
            ScenarioType = kind.ToString().ToLowerInvariant(),
            ModeSupportJson = JsonSupport.Serialize(new[] { "learning" }),
            PublishedRevisionId = $"rev-{drillId}",
            Status = ContentStatus.Draft,
            CaseNotes = null,
            DetailJson = JsonSupport.Serialize(new
            {
                instructionText = request.InstructionText.Trim(),
                drillKind = kind.ToString(),
                targetCriteria = request.TargetCriteria ?? Array.Empty<string>(),
            }),
            ModelAnswerJson = "{}",
            ExamFamilyCode = "oet",
            ExamTypeCode = "oet",
            DifficultyRating = 1500,
            SourceType = "manual",
            SourceProvenance = "original",
            RightsStatus = "owned",
            QaStatus = "approved",
            FreshnessConfidence = "current",
            InstructionLanguage = "en",
            ContentLanguage = "en",
            CreatedBy = adminId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var drill = new SpeakingDrillItem
        {
            Id = drillId,
            ContentItemId = contentId,
            DrillKind = kind,
            TargetCriteriaJson = JsonSupport.Serialize(request.TargetCriteria ?? Array.Empty<string>()),
            RecommendedAfterSessionScoreBelow = request.RecommendedAfterSessionScoreBelow,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        db.ContentItems.Add(content);
        db.SpeakingDrillItems.Add(drill);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Created", "SpeakingDrill", drillId,
            $"Created drill: {content.Title} ({kind})", ct);
        await CommitIfOwnedAsync(tx, ct);

        return ProjectDetail(drill, content);
    }

    public async Task<object> UpdateSpeakingDrillAsync(
        string adminId,
        string adminName,
        string drillId,
        AdminDrillUpdateRequest request,
        CancellationToken ct)
    {
        var drill = await db.SpeakingDrillItems.FirstOrDefaultAsync(x => x.Id == drillId, ct)
            ?? throw ApiException.NotFound("speaking_drill_not_found", "That speaking drill does not exist.");
        var content = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == drill.ContentItemId, ct)
            ?? throw ApiException.NotFound("speaking_drill_content_missing",
                "The underlying content item for this drill is missing.");

        if (content.Status == ContentStatus.Archived)
        {
            throw ApiException.Conflict("speaking_drill_archived",
                "Archived drills are read-only.");
        }

        if (request.DrillKind is not null)
        {
            var kind = ParseDrillKind(request.DrillKind);
            drill.DrillKind = kind;
            content.ScenarioType = kind.ToString().ToLowerInvariant();
        }
        if (request.ProfessionId is not null)
        {
            content.ProfessionId = string.IsNullOrWhiteSpace(request.ProfessionId) ? null : request.ProfessionId.Trim();
        }
        if (request.Title is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                throw ApiException.Validation("SPEAKING_DRILL_TITLE_REQUIRED", "Title is required.");
            }
            content.Title = request.Title.Trim();
        }
        if (request.InstructionText is not null)
        {
            if (string.IsNullOrWhiteSpace(request.InstructionText))
            {
                throw ApiException.Validation("SPEAKING_DRILL_INSTRUCTION_REQUIRED", "Instruction text is required.");
            }
            content.DetailJson = JsonSupport.Serialize(new
            {
                instructionText = request.InstructionText.Trim(),
                drillKind = drill.DrillKind.ToString(),
                targetCriteria = request.TargetCriteria ?? ParseTargetCriteria(drill.TargetCriteriaJson),
            });
        }
        if (request.TargetCriteria is not null)
        {
            drill.TargetCriteriaJson = JsonSupport.Serialize(request.TargetCriteria);
            content.CriteriaFocusJson = JsonSupport.Serialize(request.TargetCriteria);
            // Keep DetailJson aligned with the new criteria.
            content.DetailJson = JsonSupport.Serialize(new
            {
                instructionText = ParseInstructionText(content.DetailJson),
                drillKind = drill.DrillKind.ToString(),
                targetCriteria = request.TargetCriteria,
            });
        }
        if (request.RecommendedAfterSessionScoreBelow.HasValue)
        {
            drill.RecommendedAfterSessionScoreBelow = request.RecommendedAfterSessionScoreBelow.Value;
        }

        var now = DateTimeOffset.UtcNow;
        drill.UpdatedAt = now;
        content.UpdatedAt = now;

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "SpeakingDrill", drillId,
            $"Updated drill: {content.Title}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return ProjectDetail(drill, content);
    }

    public async Task<object> PublishSpeakingDrillAsync(
        string adminId,
        string adminName,
        string drillId,
        CancellationToken ct)
    {
        var drill = await db.SpeakingDrillItems.FirstOrDefaultAsync(x => x.Id == drillId, ct)
            ?? throw ApiException.NotFound("speaking_drill_not_found", "That speaking drill does not exist.");
        var content = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == drill.ContentItemId, ct)
            ?? throw ApiException.NotFound("speaking_drill_content_missing",
                "The underlying content item for this drill is missing.");

        if (content.Status == ContentStatus.Archived)
        {
            throw ApiException.Conflict("speaking_drill_archived",
                "Archived drills cannot be published.");
        }

        var now = DateTimeOffset.UtcNow;
        content.Status = ContentStatus.Published;
        content.PublishedAt = now;
        content.UpdatedAt = now;
        drill.UpdatedAt = now;

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Published", "SpeakingDrill", drillId,
            $"Published drill: {content.Title}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return ProjectDetail(drill, content);
    }

    public async Task<object> ArchiveSpeakingDrillAsync(
        string adminId,
        string adminName,
        string drillId,
        CancellationToken ct)
    {
        var drill = await db.SpeakingDrillItems.FirstOrDefaultAsync(x => x.Id == drillId, ct)
            ?? throw ApiException.NotFound("speaking_drill_not_found", "That speaking drill does not exist.");
        var content = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == drill.ContentItemId, ct)
            ?? throw ApiException.NotFound("speaking_drill_content_missing",
                "The underlying content item for this drill is missing.");

        if (content.Status == ContentStatus.Archived)
        {
            return ProjectDetail(drill, content);
        }

        var now = DateTimeOffset.UtcNow;
        content.Status = ContentStatus.Archived;
        content.ArchivedAt = now;
        content.UpdatedAt = now;
        drill.UpdatedAt = now;

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Archived", "SpeakingDrill", drillId,
            $"Archived drill: {content.Title}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return ProjectDetail(drill, content);
    }

    public async Task<object> DeleteSpeakingDrillAsync(
        string adminId,
        string adminName,
        string drillId,
        CancellationToken ct)
    {
        // Soft-delete via Archive — keeps audit + analytics intact.
        return await ArchiveSpeakingDrillAsync(adminId, adminName, drillId, ct);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static SpeakingDrillKind ParseDrillKind(string raw)
    {
        if (!Enum.TryParse<SpeakingDrillKind>(raw?.Trim(), ignoreCase: true, out var parsed)
            || !ValidDrillKinds.Contains(raw!.Trim()))
        {
            throw ApiException.Validation("SPEAKING_DRILL_KIND_INVALID",
                $"DrillKind must be one of: {string.Join(", ", ValidDrillKinds)}.");
        }
        return parsed;
    }

    private static void ValidateDrillRequest(string drillKind, string title, string instructionText, string[]? targetCriteria)
    {
        if (string.IsNullOrWhiteSpace(drillKind))
        {
            throw ApiException.Validation("SPEAKING_DRILL_KIND_REQUIRED", "DrillKind is required.");
        }
        if (string.IsNullOrWhiteSpace(title))
        {
            throw ApiException.Validation("SPEAKING_DRILL_TITLE_REQUIRED", "Title is required.");
        }
        if (string.IsNullOrWhiteSpace(instructionText))
        {
            throw ApiException.Validation("SPEAKING_DRILL_INSTRUCTION_REQUIRED", "InstructionText is required.");
        }
        if (targetCriteria is null || targetCriteria.Length == 0)
        {
            throw ApiException.Validation("SPEAKING_DRILL_CRITERIA_REQUIRED",
                "At least one TargetCriteria entry is required.");
        }
    }

    private static string[] ParseTargetCriteria(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            var list = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
            }
            return list.ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string ParseInstructionText(string? detailJson)
    {
        if (string.IsNullOrWhiteSpace(detailJson)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(detailJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return string.Empty;
            if (doc.RootElement.TryGetProperty("instructionText", out var prop)
                && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString() ?? string.Empty;
            }
            return string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static object ProjectSummaryRow(SpeakingDrillItem drill, ContentItem content)
        => new
        {
            drillId = drill.Id,
            contentItemId = content.Id,
            drillKind = drill.DrillKind.ToString(),
            professionId = content.ProfessionId,
            title = content.Title,
            instructionText = ParseInstructionText(content.DetailJson),
            targetCriteria = ParseTargetCriteria(drill.TargetCriteriaJson),
            recommendedAfterSessionScoreBelow = drill.RecommendedAfterSessionScoreBelow,
            status = content.Status.ToString().ToLowerInvariant(),
            createdAt = drill.CreatedAt,
            updatedAt = drill.UpdatedAt,
            publishedAt = content.PublishedAt,
            archivedAt = content.ArchivedAt,
        };

    private static object ProjectDetail(SpeakingDrillItem drill, ContentItem content)
        => new
        {
            drillId = drill.Id,
            contentItemId = content.Id,
            drillKind = drill.DrillKind.ToString(),
            professionId = content.ProfessionId,
            title = content.Title,
            instructionText = ParseInstructionText(content.DetailJson),
            targetCriteria = ParseTargetCriteria(drill.TargetCriteriaJson),
            recommendedAfterSessionScoreBelow = drill.RecommendedAfterSessionScoreBelow,
            status = content.Status.ToString().ToLowerInvariant(),
            createdAt = drill.CreatedAt,
            updatedAt = drill.UpdatedAt,
            publishedAt = content.PublishedAt,
            archivedAt = content.ArchivedAt,
        };
}
