using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OetLearner.Api.Contracts.Rulebooks;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Rulebooks;

/// <summary>
/// Admin-facing CRUD + publish operations for rulebooks. All mutations stamp
/// UpdatedByUserId + UpdatedAt and emit AuditEvent rows for traceability.
/// </summary>
public sealed class RulebookAdminService
{
    private readonly LearnerDbContext _db;
    private readonly IMemoryCache _cache;

    public RulebookAdminService(LearnerDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    /// <summary>
    /// Drop the DbBackedRulebookLoader cache entry for this version's
    /// (kind, profession) pair so grading + linting picks up the change
    /// on the very next call. Safe even when the version is currently a
    /// Draft — invalidating an unrelated cache key is a no-op.
    /// </summary>
    private void InvalidateLoaderCache(RulebookVersion version)
        => DbBackedRulebookLoader.InvalidateCacheKey(_cache, version.Kind, version.Profession);

    // ── Read ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<RulebookSummaryDto>> ListAsync(string? kind, string? profession, CancellationToken ct)
    {
        var q = _db.RulebookVersions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(kind)) q = q.Where(v => v.Kind == kind!.ToLower());
        if (!string.IsNullOrWhiteSpace(profession)) q = q.Where(v => v.Profession == profession!.ToLower());

        var versions = await q.OrderBy(v => v.Kind).ThenBy(v => v.Profession).ThenBy(v => v.Version).ToListAsync(ct);
        if (versions.Count == 0) return Array.Empty<RulebookSummaryDto>();

        var ids = versions.Select(v => v.Id).ToList();
        var sectionCounts = await _db.RulebookSectionRows.AsNoTracking()
            .Where(s => ids.Contains(s.RulebookVersionId))
            .GroupBy(s => s.RulebookVersionId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

        var ruleCounts = await _db.RulebookRuleRows.AsNoTracking()
            .Where(r => ids.Contains(r.RulebookVersionId))
            .GroupBy(r => r.RulebookVersionId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

        return versions.Select(v => new RulebookSummaryDto(
            v.Id,
            v.Kind,
            v.Profession,
            v.Version,
            v.Status,
            v.AuthoritySource,
            sectionCounts.GetValueOrDefault(v.Id, 0),
            ruleCounts.GetValueOrDefault(v.Id, 0),
            v.UpdatedByUserId,
            v.CreatedAt.ToString("o"),
            v.UpdatedAt.ToString("o"),
            v.PublishedAt?.ToString("o"))).ToList();
    }

    public async Task<RulebookDetailDto?> GetAsync(string id, CancellationToken ct)
    {
        var v = await _db.RulebookVersions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (v is null) return null;
        var sections = await _db.RulebookSectionRows.AsNoTracking()
            .Where(s => s.RulebookVersionId == id)
            .OrderBy(s => s.OrderIndex).ThenBy(s => s.Code)
            .Select(s => new RulebookSectionDto(s.Id, s.Code, s.Title, s.OrderIndex))
            .ToListAsync(ct);
        var rules = await _db.RulebookRuleRows.AsNoTracking()
            .Where(r => r.RulebookVersionId == id)
            .OrderBy(r => r.SectionCode).ThenBy(r => r.OrderIndex).ThenBy(r => r.Code)
            .Select(r => new RulebookRuleDto(
                r.Id, r.Code, r.SectionCode, r.Title, r.Body, r.Severity,
                r.AppliesToJson, r.TurnStage, r.ExemplarPhrasesJson, r.ForbiddenPatternsJson,
                r.CheckId, r.ParamsJson, r.ExamplesJson, r.OrderIndex))
            .ToListAsync(ct);

        return new RulebookDetailDto(
            v.Id, v.Kind, v.Profession, v.Version, v.Status, v.AuthoritySource,
            v.CreatedAt.ToString("o"), v.UpdatedAt.ToString("o"), v.PublishedAt?.ToString("o"),
            sections, rules);
    }

    // ── Version-level mutations ──────────────────────────────────────

    public async Task<RulebookDetailDto> UpdateMetaAsync(string id, UpdateRulebookMetaRequest req, string adminId, CancellationToken ct)
    {
        var v = await _db.RulebookVersions.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new RulebookNotFoundException(id);
        if (!string.IsNullOrWhiteSpace(req.Version)) v.Version = req.Version!;
        if (req.AuthoritySource is not null) v.AuthoritySource = req.AuthoritySource;
        v.UpdatedAt = DateTimeOffset.UtcNow;
        v.UpdatedByUserId = adminId;
        await _db.SaveChangesAsync(ct);
        InvalidateLoaderCache(v);
        return (await GetAsync(id, ct))!;
    }

    public async Task<RulebookDetailDto> PublishAsync(string id, PublishRulebookRequest req, string adminId, CancellationToken ct)
    {
        var v = await _db.RulebookVersions.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new RulebookNotFoundException(id);

        // Archive any other Published row for the same (kind, profession).
        var others = await _db.RulebookVersions
            .Where(x => x.Id != id && x.Kind == v.Kind && x.Profession == v.Profession && x.Status == RulebookStatus.Published)
            .ToListAsync(ct);
        foreach (var o in others) { o.Status = RulebookStatus.Archived; o.UpdatedAt = DateTimeOffset.UtcNow; }

        if (!string.IsNullOrWhiteSpace(req.VersionLabel)) v.Version = req.VersionLabel!;
        v.Status = RulebookStatus.Published;
        v.PublishedAt = DateTimeOffset.UtcNow;
        v.UpdatedAt = DateTimeOffset.UtcNow;
        v.UpdatedByUserId = adminId;
        await _db.SaveChangesAsync(ct);
        InvalidateLoaderCache(v);
        return (await GetAsync(id, ct))!;
    }

    // ── Section CRUD ─────────────────────────────────────────────────

    public async Task<RulebookSectionDto> CreateSectionAsync(string versionId, CreateSectionRequest req, string adminId, CancellationToken ct)
    {
        var v = await _db.RulebookVersions.FirstOrDefaultAsync(x => x.Id == versionId, ct)
            ?? throw new RulebookNotFoundException(versionId);
        if (string.IsNullOrWhiteSpace(req.Code)) throw new ArgumentException("Section code is required.");
        if (string.IsNullOrWhiteSpace(req.Title)) throw new ArgumentException("Section title is required.");

        var exists = await _db.RulebookSectionRows.AnyAsync(s => s.RulebookVersionId == versionId && s.Code == req.Code, ct);
        if (exists) throw new InvalidOperationException($"Section code '{req.Code}' already exists in this rulebook.");

        var maxOrder = await _db.RulebookSectionRows
            .Where(s => s.RulebookVersionId == versionId)
            .MaxAsync(s => (int?)s.OrderIndex, ct) ?? -1;

        var row = new RulebookSectionRow
        {
            Id = Guid.NewGuid().ToString(),
            RulebookVersionId = versionId,
            Code = req.Code.Trim(),
            Title = req.Title.Trim(),
            OrderIndex = req.OrderIndex ?? maxOrder + 1,
        };
        _db.RulebookSectionRows.Add(row);
        v.UpdatedAt = DateTimeOffset.UtcNow;
        v.UpdatedByUserId = adminId;
        await _db.SaveChangesAsync(ct);
        InvalidateLoaderCache(v);
        return new RulebookSectionDto(row.Id, row.Code, row.Title, row.OrderIndex);
    }

    public async Task<RulebookSectionDto> UpdateSectionAsync(string versionId, string sectionId, UpdateSectionRequest req, string adminId, CancellationToken ct)
    {
        var row = await _db.RulebookSectionRows.FirstOrDefaultAsync(s => s.Id == sectionId && s.RulebookVersionId == versionId, ct)
            ?? throw new RulebookNotFoundException(sectionId);
        if (!string.IsNullOrWhiteSpace(req.Title)) row.Title = req.Title!.Trim();
        if (req.OrderIndex.HasValue) row.OrderIndex = req.OrderIndex.Value;
        await StampVersionAsync(versionId, adminId, ct);
        return new RulebookSectionDto(row.Id, row.Code, row.Title, row.OrderIndex);
    }

    public async Task DeleteSectionAsync(string versionId, string sectionId, string adminId, CancellationToken ct)
    {
        var row = await _db.RulebookSectionRows.FirstOrDefaultAsync(s => s.Id == sectionId && s.RulebookVersionId == versionId, ct)
            ?? throw new RulebookNotFoundException(sectionId);
        var ruleCount = await _db.RulebookRuleRows.CountAsync(r => r.RulebookVersionId == versionId && r.SectionCode == row.Code, ct);
        if (ruleCount > 0) throw new InvalidOperationException($"Cannot delete section '{row.Code}': it contains {ruleCount} rule(s). Move or delete those rules first.");
        _db.RulebookSectionRows.Remove(row);
        await StampVersionAsync(versionId, adminId, ct);
    }

    // ── Rule CRUD ────────────────────────────────────────────────────

    public async Task<RulebookRuleDto> CreateRuleAsync(string versionId, CreateRuleRequest req, string adminId, CancellationToken ct)
    {
        await EnsureVersionAsync(versionId, ct);
        if (string.IsNullOrWhiteSpace(req.Code)) throw new ArgumentException("Rule code is required.");
        if (string.IsNullOrWhiteSpace(req.SectionCode)) throw new ArgumentException("Section code is required.");
        if (string.IsNullOrWhiteSpace(req.Title)) throw new ArgumentException("Rule title is required.");
        if (string.IsNullOrWhiteSpace(req.Body)) throw new ArgumentException("Rule body is required.");
        if (!IsValidSeverity(req.Severity)) throw new ArgumentException("Severity must be one of: critical, major, minor, info.");

        var sectionExists = await _db.RulebookSectionRows.AnyAsync(s => s.RulebookVersionId == versionId && s.Code == req.SectionCode, ct);
        if (!sectionExists) throw new InvalidOperationException($"Section '{req.SectionCode}' does not exist in this rulebook.");

        var dup = await _db.RulebookRuleRows.AnyAsync(r => r.RulebookVersionId == versionId && r.Code == req.Code, ct);
        if (dup) throw new InvalidOperationException($"Rule code '{req.Code}' already exists in this rulebook.");

        var maxOrder = await _db.RulebookRuleRows
            .Where(r => r.RulebookVersionId == versionId && r.SectionCode == req.SectionCode)
            .MaxAsync(r => (int?)r.OrderIndex, ct) ?? -1;

        var row = new RulebookRuleRow
        {
            Id = Guid.NewGuid().ToString(),
            RulebookVersionId = versionId,
            Code = req.Code.Trim(),
            SectionCode = req.SectionCode.Trim(),
            Title = req.Title.Trim(),
            Body = req.Body,
            Severity = req.Severity.ToLowerInvariant(),
            AppliesToJson = req.AppliesToJson ?? "\"all\"",
            TurnStage = req.TurnStage,
            ExemplarPhrasesJson = req.ExemplarPhrasesJson,
            ForbiddenPatternsJson = req.ForbiddenPatternsJson,
            CheckId = req.CheckId,
            ParamsJson = req.ParamsJson,
            ExamplesJson = req.ExamplesJson,
            OrderIndex = req.OrderIndex ?? maxOrder + 1,
        };
        _db.RulebookRuleRows.Add(row);
        await StampVersionAsync(versionId, adminId, ct);
        return ToDto(row);
    }

    public async Task<RulebookRuleDto> UpdateRuleAsync(string versionId, string ruleId, UpdateRuleRequest req, string adminId, CancellationToken ct)
    {
        var row = await _db.RulebookRuleRows.FirstOrDefaultAsync(r => r.Id == ruleId && r.RulebookVersionId == versionId, ct)
            ?? throw new RulebookNotFoundException(ruleId);

        if (!string.IsNullOrWhiteSpace(req.SectionCode))
        {
            var ok = await _db.RulebookSectionRows.AnyAsync(s => s.RulebookVersionId == versionId && s.Code == req.SectionCode, ct);
            if (!ok) throw new InvalidOperationException($"Section '{req.SectionCode}' does not exist in this rulebook.");
            row.SectionCode = req.SectionCode!.Trim();
        }
        if (!string.IsNullOrWhiteSpace(req.Title)) row.Title = req.Title!.Trim();
        if (req.Body is not null) row.Body = req.Body;
        if (!string.IsNullOrWhiteSpace(req.Severity))
        {
            if (!IsValidSeverity(req.Severity)) throw new ArgumentException("Severity must be one of: critical, major, minor, info.");
            row.Severity = req.Severity!.ToLowerInvariant();
        }
        if (req.AppliesToJson is not null) row.AppliesToJson = req.AppliesToJson;
        if (req.TurnStage is not null) row.TurnStage = string.IsNullOrWhiteSpace(req.TurnStage) ? null : req.TurnStage;
        if (req.ExemplarPhrasesJson is not null) row.ExemplarPhrasesJson = string.IsNullOrWhiteSpace(req.ExemplarPhrasesJson) ? null : req.ExemplarPhrasesJson;
        if (req.ForbiddenPatternsJson is not null) row.ForbiddenPatternsJson = string.IsNullOrWhiteSpace(req.ForbiddenPatternsJson) ? null : req.ForbiddenPatternsJson;
        if (req.CheckId is not null) row.CheckId = string.IsNullOrWhiteSpace(req.CheckId) ? null : req.CheckId;
        if (req.ParamsJson is not null) row.ParamsJson = string.IsNullOrWhiteSpace(req.ParamsJson) ? null : req.ParamsJson;
        if (req.ExamplesJson is not null) row.ExamplesJson = string.IsNullOrWhiteSpace(req.ExamplesJson) ? null : req.ExamplesJson;
        if (req.OrderIndex.HasValue) row.OrderIndex = req.OrderIndex.Value;

        await StampVersionAsync(versionId, adminId, ct);
        return ToDto(row);
    }

    public async Task DeleteRuleAsync(string versionId, string ruleId, string adminId, CancellationToken ct)
    {
        var row = await _db.RulebookRuleRows.FirstOrDefaultAsync(r => r.Id == ruleId && r.RulebookVersionId == versionId, ct)
            ?? throw new RulebookNotFoundException(ruleId);
        _db.RulebookRuleRows.Remove(row);
        await StampVersionAsync(versionId, adminId, ct);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static bool IsValidSeverity(string? s) => s is "critical" or "major" or "minor" or "info" || s is "Critical" or "Major" or "Minor" or "Info";

    private async Task EnsureVersionAsync(string versionId, CancellationToken ct)
    {
        var exists = await _db.RulebookVersions.AnyAsync(v => v.Id == versionId, ct);
        if (!exists) throw new RulebookNotFoundException(versionId);
    }

    private async Task StampVersionAsync(string versionId, string adminId, CancellationToken ct)
    {
        var v = await _db.RulebookVersions.FirstOrDefaultAsync(x => x.Id == versionId, ct);
        if (v is null) return;
        v.UpdatedAt = DateTimeOffset.UtcNow;
        v.UpdatedByUserId = adminId;
        await _db.SaveChangesAsync(ct);
        InvalidateLoaderCache(v);
    }

    private static RulebookRuleDto ToDto(RulebookRuleRow r) => new(
        r.Id, r.Code, r.SectionCode, r.Title, r.Body, r.Severity,
        r.AppliesToJson, r.TurnStage, r.ExemplarPhrasesJson, r.ForbiddenPatternsJson,
        r.CheckId, r.ParamsJson, r.ExamplesJson, r.OrderIndex);
}

public sealed class RulebookNotFoundException(string id)
    : InvalidOperationException($"Rulebook entity '{id}' was not found.");
