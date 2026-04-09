using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Phase 7 — Bulk import from CSV/JSON, content inventory with filters, and media asset management.
/// </summary>
public class ContentImportService(LearnerDbContext db)
{
    /// <summary>
    /// Import content items from a JSON array. Each item maps to ContentItem fields.
    /// Returns the import batch with results.
    /// </summary>
    public async Task<ImportResult> BulkImportAsync(string adminId, string batchTitle, List<ContentImportRow> rows, CancellationToken ct)
    {
        var batch = new ContentImportBatch
        {
            Id = $"batch-{Guid.NewGuid():N}"[..32],
            Title = batchTitle,
            Status = "processing",
            TotalItems = rows.Count,
            CreatedBy = adminId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ContentImportBatches.Add(batch);

        var created = new List<string>();
        var errors = new List<ImportError>();

        foreach (var row in rows)
        {
            try
            {
                // Validate required fields
                if (string.IsNullOrWhiteSpace(row.Title))
                {
                    errors.Add(new ImportError(row.RowIndex, "Title is required."));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(row.SubtestCode))
                {
                    errors.Add(new ImportError(row.RowIndex, "SubtestCode is required."));
                    continue;
                }

                // Validate detail JSON if provided
                if (!string.IsNullOrEmpty(row.DetailJson))
                {
                    var validation = ContentSchemaValidator.ValidateDetailJson(row.SubtestCode, row.DetailJson);
                    if (!validation.IsValid)
                    {
                        errors.Add(new ImportError(row.RowIndex, $"DetailJson invalid: {string.Join("; ", validation.Errors)}"));
                        continue;
                    }
                }

                // Check for duplicate fingerprint
                var item = new ContentItem
                {
                    Id = $"{row.SubtestCode[..2]}-{Guid.NewGuid():N}"[..32],
                    ContentType = row.ContentType ?? "task",
                    SubtestCode = row.SubtestCode,
                    ProfessionId = row.ProfessionId,
                    Title = row.Title,
                    Difficulty = row.Difficulty ?? "intermediate",
                    EstimatedDurationMinutes = row.EstimatedDurationMinutes ?? 15,
                    ScenarioType = row.ScenarioType,
                    DetailJson = row.DetailJson ?? "{}",
                    ModelAnswerJson = row.ModelAnswerJson ?? "{}",
                    PublishedRevisionId = $"rev-{Guid.NewGuid():N}"[..24],
                    Status = ContentStatus.Draft, // Always draft from import
                    InstructionLanguage = row.InstructionLanguage ?? "en",
                    ContentLanguage = row.ContentLanguage ?? "en",
                    SourceProvenance = row.SourceProvenance ?? "original",
                    RightsStatus = row.RightsStatus ?? "owned",
                    FreshnessConfidence = row.FreshnessConfidence ?? "current",
                    CanonicalSourcePath = row.CanonicalSourcePath,
                    ImportBatchId = batch.Id,
                    QualityScore = row.QualityScore ?? 0,
                    CriteriaFocusJson = row.CriteriaFocusJson ?? "[]",
                    ModeSupportJson = row.ModeSupportJson ?? "[\"practice\"]",
                    CreatedBy = adminId,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                // Compute fingerprint for dedup
                var fingerprint = ContentDeduplicationService.ComputeFingerprint(item);
                item.DuplicateGroupId = null; // Will be set by dedup scan

                db.ContentItems.Add(item);
                created.Add(item.Id);
            }
            catch (Exception ex)
            {
                errors.Add(new ImportError(row.RowIndex, ex.Message));
            }
        }

        batch.ProcessedItems = created.Count;
        batch.FailedItems = errors.Count;
        batch.Status = errors.Count == 0 ? "completed" : "completed_with_errors";
        batch.CompletedAt = DateTimeOffset.UtcNow;
        batch.ErrorLogJson = errors.Count > 0 ? JsonSerializer.Serialize(errors) : null;

        await db.SaveChangesAsync(ct);

        return new ImportResult(batch.Id, created.Count, errors.Count, errors, created);
    }

    /// <summary>
    /// Get full content inventory with multi-dimensional filters for admin CMS.
    /// </summary>
    public async Task<object> GetContentInventoryAsync(ContentInventoryQuery query, CancellationToken ct)
    {
        var q = db.ContentItems.AsQueryable();

        if (!string.IsNullOrEmpty(query.SubtestCode))
            q = q.Where(c => c.SubtestCode == query.SubtestCode);
        if (!string.IsNullOrEmpty(query.ProfessionId))
            q = q.Where(c => c.ProfessionId == query.ProfessionId);
        if (!string.IsNullOrEmpty(query.Language))
            q = q.Where(c => c.InstructionLanguage == query.Language);
        if (!string.IsNullOrEmpty(query.Provenance))
            q = q.Where(c => c.SourceProvenance == query.Provenance);
        if (!string.IsNullOrEmpty(query.Freshness))
            q = q.Where(c => c.FreshnessConfidence == query.Freshness);
        if (!string.IsNullOrEmpty(query.QaStatus))
            q = q.Where(c => c.QaStatus == query.QaStatus);
        if (!string.IsNullOrEmpty(query.Status))
        {
            if (Enum.TryParse<ContentStatus>(query.Status, true, out var s))
                q = q.Where(c => c.Status == s);
        }
        if (!string.IsNullOrEmpty(query.PackageId))
        {
            var ruleContentIds = await db.PackageContentRules
                .Where(r => r.PackageId == query.PackageId && r.TargetType == "content_item")
                .Select(r => r.TargetId)
                .ToListAsync(ct);
            q = q.Where(c => ruleContentIds.Contains(c.Id));
        }
        if (!string.IsNullOrEmpty(query.ImportBatchId))
            q = q.Where(c => c.ImportBatchId == query.ImportBatchId);
        if (!string.IsNullOrEmpty(query.Search))
        {
            var lower = query.Search.ToLower();
            q = q.Where(c => c.Title.ToLower().Contains(lower) || c.Id.Contains(lower));
        }

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(c => c.UpdatedAt)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(c => new
            {
                c.Id, c.Title, c.ContentType, c.SubtestCode, c.ProfessionId,
                c.Difficulty, c.InstructionLanguage, c.SourceProvenance,
                c.FreshnessConfidence, c.QaStatus, c.QualityScore,
                status = c.Status.ToString(),
                c.ImportBatchId, c.DuplicateGroupId, c.IsPreviewEligible,
                c.IsMockEligible, c.IsDiagnosticEligible,
                c.CreatedAt, c.UpdatedAt
            })
            .ToListAsync(ct);

        return new { items, total, page = query.Page, pageSize = query.PageSize };
    }

    /// <summary>
    /// Create or update a media asset record.
    /// </summary>
    public async Task<MediaAsset> UpsertMediaAssetAsync(MediaAsset asset, CancellationToken ct)
    {
        var existing = await db.MediaAssets.FindAsync([asset.Id], ct);
        if (existing is not null)
        {
            existing.OriginalFilename = asset.OriginalFilename;
            existing.MimeType = asset.MimeType;
            existing.Format = asset.Format;
            existing.SizeBytes = asset.SizeBytes;
            existing.DurationSeconds = asset.DurationSeconds;
            existing.ThumbnailPath = asset.ThumbnailPath;
            existing.CaptionPath = asset.CaptionPath;
            existing.TranscriptPath = asset.TranscriptPath;
            existing.Status = asset.Status;
            existing.ProcessedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            asset.Id ??= $"media-{Guid.NewGuid():N}"[..32];
            asset.UploadedAt = DateTimeOffset.UtcNow;
            db.MediaAssets.Add(asset);
        }
        await db.SaveChangesAsync(ct);
        return existing ?? asset;
    }

    /// <summary>
    /// List media assets with optional filtering.
    /// </summary>
    public async Task<object> GetMediaAssetsAsync(string? mimeType, string? status, int page, int pageSize, CancellationToken ct)
    {
        var q = db.MediaAssets.AsQueryable();
        if (!string.IsNullOrEmpty(mimeType)) q = q.Where(a => a.MimeType.StartsWith(mimeType));
        if (!string.IsNullOrEmpty(status))
        {
            if (Enum.TryParse<MediaAssetStatus>(status, true, out var s)) q = q.Where(a => a.Status == s);
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.UploadedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);
        return new { items, total, page, pageSize };
    }
}

// ── DTOs ──

public class ContentImportRow
{
    public int RowIndex { get; set; }
    public string Title { get; set; } = default!;
    public string SubtestCode { get; set; } = default!;
    public string? ContentType { get; set; }
    public string? ProfessionId { get; set; }
    public string? Difficulty { get; set; }
    public int? EstimatedDurationMinutes { get; set; }
    public string? ScenarioType { get; set; }
    public string? DetailJson { get; set; }
    public string? ModelAnswerJson { get; set; }
    public string? InstructionLanguage { get; set; }
    public string? ContentLanguage { get; set; }
    public string? SourceProvenance { get; set; }
    public string? RightsStatus { get; set; }
    public string? FreshnessConfidence { get; set; }
    public string? CanonicalSourcePath { get; set; }
    public int? QualityScore { get; set; }
    public string? CriteriaFocusJson { get; set; }
    public string? ModeSupportJson { get; set; }
}

public record ImportResult(string BatchId, int Created, int Failed, List<ImportError> Errors, List<string> CreatedIds);
public record ImportError(int RowIndex, string Message);

public class ContentInventoryQuery
{
    public string? SubtestCode { get; set; }
    public string? ProfessionId { get; set; }
    public string? Language { get; set; }
    public string? Provenance { get; set; }
    public string? Freshness { get; set; }
    public string? QaStatus { get; set; }
    public string? Status { get; set; }
    public string? PackageId { get; set; }
    public string? ImportBatchId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
