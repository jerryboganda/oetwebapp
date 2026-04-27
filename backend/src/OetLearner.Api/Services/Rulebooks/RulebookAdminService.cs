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
using System.Text.Json;

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

    // ── Classification: canonical enums (proper classification on insert) ──

    public static readonly IReadOnlyList<string> ValidKinds = new[]
    {
        "writing", "speaking", "grammar", "pronunciation", "vocabulary", "conversation",
    };

    public static readonly IReadOnlyList<string> ValidProfessions = new[]
    {
        "medicine", "nursing", "dentistry", "pharmacy", "physiotherapy", "veterinary",
        "optometry", "radiography", "occupationaltherapy", "speechpathology", "podiatry", "dietetics",
    };

    public static readonly IReadOnlyList<string> ValidSeverities = new[]
    {
        "critical", "major", "minor", "info",
    };

    public static readonly IReadOnlyList<string> ValidStatuses = new[]
    {
        RulebookStatus.Draft, RulebookStatus.Published, RulebookStatus.Archived,
    };

    public RulebookMetadataDto GetMetadata() =>
        new(ValidKinds, ValidProfessions, ValidSeverities, ValidStatuses);

    private static string NormalizeKind(string? raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant();
        if (!ValidKinds.Contains(v))
            throw ApiException.Validation("invalid_kind", $"Invalid kind '{raw}'. Must be one of: {string.Join(", ", ValidKinds)}.");
        return v;
    }

    private static string NormalizeProfession(string? raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant().Replace("-", "").Replace("_", "").Replace(" ", "");
        if (!ValidProfessions.Contains(v))
            throw ApiException.Validation("invalid_profession", $"Invalid profession '{raw}'. Must be one of: {string.Join(", ", ValidProfessions)}.");
        return v;
    }

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

    public async Task<RulebookDetailDto> CreateAsync(CreateRulebookRequest req, string adminId, CancellationToken ct)
    {
        var kind = NormalizeKind(req.Kind);
        var profession = NormalizeProfession(req.Profession);
        if (string.IsNullOrWhiteSpace(req.Version))
            throw ApiException.Validation("version_required", "Version label is required.");

        var version = req.Version.Trim();
        var id = $"rb_{kind}_{profession}_{version}".ToLowerInvariant().Replace(" ", "-");

        var existing = await _db.RulebookVersions.AnyAsync(v => v.Id == id, ct);
        if (existing)
            throw ApiException.Conflict("rulebook_exists", $"A rulebook with id '{id}' already exists. Pick a different version label.");

        var now = DateTimeOffset.UtcNow;
        var row = new RulebookVersion
        {
            Id = id,
            Kind = kind,
            Profession = profession,
            Version = version,
            Status = RulebookStatus.Draft,
            AuthoritySource = req.AuthoritySource,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByUserId = adminId,
        };
        _db.RulebookVersions.Add(row);
        await _db.SaveChangesAsync(ct);
        InvalidateLoaderCache(row);
        return (await GetAsync(id, ct))!;
    }

    public async Task<RulebookDetailDto> CloneAsync(string sourceId, CloneRulebookRequest req, string adminId, CancellationToken ct)
    {
        var src = await _db.RulebookVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == sourceId, ct)
            ?? throw new RulebookNotFoundException(sourceId);

        var newKind = string.IsNullOrWhiteSpace(req.Kind) ? src.Kind : NormalizeKind(req.Kind);
        var newProfession = string.IsNullOrWhiteSpace(req.Profession) ? src.Profession : NormalizeProfession(req.Profession);
        var newVersion = string.IsNullOrWhiteSpace(req.Version) ? $"{src.Version}-clone" : req.Version!.Trim();
        var newId = $"rb_{newKind}_{newProfession}_{newVersion}".ToLowerInvariant().Replace(" ", "-");

        if (string.Equals(newId, sourceId, StringComparison.OrdinalIgnoreCase))
            throw ApiException.Validation("clone_target_identical", "Clone target is identical to source. Change kind, profession, or version label.");

        var dup = await _db.RulebookVersions.AnyAsync(v => v.Id == newId, ct);
        if (dup)
            throw ApiException.Conflict("rulebook_exists", $"A rulebook with id '{newId}' already exists. Pick a different version label.");

        var now = DateTimeOffset.UtcNow;
        var clonedVersion = new RulebookVersion
        {
            Id = newId,
            Kind = newKind,
            Profession = newProfession,
            Version = newVersion,
            Status = RulebookStatus.Draft,
            AuthoritySource = req.AuthoritySource ?? src.AuthoritySource,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByUserId = adminId,
        };
        _db.RulebookVersions.Add(clonedVersion);

        var sections = await _db.RulebookSectionRows.AsNoTracking()
            .Where(s => s.RulebookVersionId == sourceId)
            .ToListAsync(ct);
        foreach (var s in sections)
        {
            _db.RulebookSectionRows.Add(new RulebookSectionRow
            {
                Id = Guid.NewGuid().ToString(),
                RulebookVersionId = newId,
                Code = s.Code,
                Title = s.Title,
                OrderIndex = s.OrderIndex,
            });
        }

        var rules = await _db.RulebookRuleRows.AsNoTracking()
            .Where(r => r.RulebookVersionId == sourceId)
            .ToListAsync(ct);
        foreach (var r in rules)
        {
            _db.RulebookRuleRows.Add(new RulebookRuleRow
            {
                Id = Guid.NewGuid().ToString(),
                RulebookVersionId = newId,
                Code = r.Code,
                SectionCode = r.SectionCode,
                Title = r.Title,
                Body = r.Body,
                Severity = r.Severity,
                AppliesToJson = r.AppliesToJson,
                TurnStage = r.TurnStage,
                ExemplarPhrasesJson = r.ExemplarPhrasesJson,
                ForbiddenPatternsJson = r.ForbiddenPatternsJson,
                CheckId = r.CheckId,
                ParamsJson = r.ParamsJson,
                ExamplesJson = r.ExamplesJson,
                OrderIndex = r.OrderIndex,
            });
        }

        await _db.SaveChangesAsync(ct);
        InvalidateLoaderCache(clonedVersion);
        return (await GetAsync(newId, ct))!;
    }

    public async Task<RulebookDetailDto> UnpublishAsync(string id, string adminId, CancellationToken ct)
    {
        var v = await _db.RulebookVersions.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new RulebookNotFoundException(id);
        if (v.Status != RulebookStatus.Published)
            throw ApiException.Validation("not_published", $"Rulebook is not Published (current: {v.Status}).");
        v.Status = RulebookStatus.Draft;
        v.UpdatedAt = DateTimeOffset.UtcNow;
        v.UpdatedByUserId = adminId;
        await _db.SaveChangesAsync(ct);
        InvalidateLoaderCache(v);
        return (await GetAsync(id, ct))!;
    }

    public async Task DeleteAsync(string id, string adminId, CancellationToken ct)
    {
        var v = await _db.RulebookVersions.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new RulebookNotFoundException(id);
        if (v.Status == RulebookStatus.Published)
            throw ApiException.Validation("published_cannot_delete", "Cannot delete a Published rulebook. Unpublish it first.");

        var sectionRows = await _db.RulebookSectionRows.Where(s => s.RulebookVersionId == id).ToListAsync(ct);
        var ruleRows = await _db.RulebookRuleRows.Where(r => r.RulebookVersionId == id).ToListAsync(ct);
        _db.RulebookRuleRows.RemoveRange(ruleRows);
        _db.RulebookSectionRows.RemoveRange(sectionRows);
        _db.RulebookVersions.Remove(v);
        await _db.SaveChangesAsync(ct);
        InvalidateLoaderCache(v);
    }

    /// <summary>
    /// Export the current DB rows as the canonical rulebook JSON shape
    /// (the same shape stored in <c>/rulebooks/{kind}/{profession}/rulebook.v1.json</c>).
    /// </summary>
    public async Task<JsonElement> ExportAsync(string id, CancellationToken ct)
    {
        var detail = await GetAsync(id, ct) ?? throw new RulebookNotFoundException(id);

        var sections = detail.Sections
            .Select(s => new Dictionary<string, object?>
            {
                ["id"] = s.Code,
                ["title"] = s.Title,
                ["order"] = s.OrderIndex,
            })
            .ToList();

        var rules = detail.Rules
            .Select(r =>
            {
                var dict = new Dictionary<string, object?>
                {
                    ["id"] = r.Code,
                    ["section"] = r.SectionCode,
                    ["title"] = r.Title,
                    ["body"] = r.Body,
                    ["severity"] = r.Severity,
                    ["appliesTo"] = ParseOrString(r.AppliesToJson),
                };
                if (!string.IsNullOrWhiteSpace(r.TurnStage)) dict["turnStage"] = r.TurnStage;
                if (!string.IsNullOrWhiteSpace(r.ExemplarPhrasesJson)) dict["exemplarPhrases"] = ParseOrString(r.ExemplarPhrasesJson);
                if (!string.IsNullOrWhiteSpace(r.ForbiddenPatternsJson)) dict["forbiddenPatterns"] = ParseOrString(r.ForbiddenPatternsJson);
                if (!string.IsNullOrWhiteSpace(r.CheckId)) dict["checkId"] = r.CheckId;
                if (!string.IsNullOrWhiteSpace(r.ParamsJson)) dict["params"] = ParseOrString(r.ParamsJson);
                if (!string.IsNullOrWhiteSpace(r.ExamplesJson)) dict["examples"] = ParseOrString(r.ExamplesJson);
                return dict;
            })
            .ToList();

        var payload = new Dictionary<string, object?>
        {
            ["version"] = detail.Version,
            ["kind"] = detail.Kind,
            ["profession"] = detail.Profession,
            ["status"] = detail.Status,
            ["authoritySource"] = detail.AuthoritySource,
            ["publishedAt"] = detail.PublishedAt,
            ["sections"] = sections,
            ["rules"] = rules,
        };
        return JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Import a rulebook from canonical JSON. Mode "create" fails if a row
    /// for (kind, profession, version) already exists; mode "replace" wipes
    /// the existing version and re-imports from the JSON payload (preserving
    /// the row's id and Status). Always validates required fields and
    /// classification (kind, profession, severity) before any DB writes.
    /// </summary>
    public async Task<RulebookDetailDto> ImportAsync(ImportRulebookRequest req, string adminId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Json))
            throw ApiException.Validation("json_required", "JSON payload is required.");
        var mode = (req.Mode ?? "create").Trim().ToLowerInvariant();
        if (mode != "create" && mode != "replace")
            throw ApiException.Validation("invalid_mode", "Mode must be 'create' or 'replace'.");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(req.Json); }
        catch (JsonException ex) { throw ApiException.Validation("invalid_json", $"Invalid JSON: {ex.Message}"); }

        using (doc)
        {
            var root = doc.RootElement;
            var kind = NormalizeKind(GetStringOrThrow(root, "kind"));
            var profession = NormalizeProfession(GetStringOrThrow(root, "profession"));
            var version = GetStringOrThrow(root, "version").Trim();
            var authoritySource = root.TryGetProperty("authoritySource", out var asEl) && asEl.ValueKind == JsonValueKind.String
                ? asEl.GetString() : null;

            if (!root.TryGetProperty("rules", out var rulesEl) || rulesEl.ValueKind != JsonValueKind.Array)
                throw ApiException.Validation("rules_required", "Payload must contain a 'rules' array.");

            // Validate classification on every rule before any write.
            var sectionCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("sections", out var sectionsEl) && sectionsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in sectionsEl.EnumerateArray())
                {
                    var code = GetStringOrThrow(s, "id");
                    if (!sectionCodes.Add(code))
                        throw ApiException.Validation("duplicate_section", $"Duplicate section code '{code}'.");
                }
            }

            var ruleCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rulesEl.EnumerateArray())
            {
                var code = GetStringOrThrow(r, "id");
                if (!ruleCodes.Add(code))
                    throw ApiException.Validation("duplicate_rule", $"Duplicate rule code '{code}'.");
                _ = GetStringOrThrow(r, "title");
                _ = GetStringOrThrow(r, "body");
                var sevRaw = GetStringOrThrow(r, "severity").Trim().ToLowerInvariant();
                if (!ValidSeverities.Contains(sevRaw))
                    throw ApiException.Validation("invalid_severity", $"Rule '{code}' has invalid severity '{sevRaw}'.");
                var sec = GetStringOrThrow(r, "section");
                if (sectionCodes.Count > 0 && !sectionCodes.Contains(sec))
                    throw ApiException.Validation("unknown_section", $"Rule '{code}' references unknown section '{sec}'.");
            }

            var id = $"rb_{kind}_{profession}_{version}".ToLowerInvariant().Replace(" ", "-");
            var existing = await _db.RulebookVersions.FirstOrDefaultAsync(v => v.Id == id, ct);

            if (existing is not null && mode == "create")
                throw ApiException.Conflict("rulebook_exists", $"Rulebook '{id}' already exists. Use mode='replace' to overwrite.");
            if (existing is null && mode == "replace")
                throw ApiException.Validation("rulebook_not_found", $"Rulebook '{id}' does not exist; cannot replace. Use mode='create'.");

            RulebookVersion target;
            var now = DateTimeOffset.UtcNow;
            if (existing is null)
            {
                target = new RulebookVersion
                {
                    Id = id,
                    Kind = kind,
                    Profession = profession,
                    Version = version,
                    Status = RulebookStatus.Draft,
                    AuthoritySource = authoritySource,
                    CreatedAt = now,
                    UpdatedAt = now,
                    UpdatedByUserId = adminId,
                };
                _db.RulebookVersions.Add(target);
            }
            else
            {
                target = existing;
                target.AuthoritySource = authoritySource ?? target.AuthoritySource;
                target.UpdatedAt = now;
                target.UpdatedByUserId = adminId;
                // Wipe existing children for replace.
                var oldRules = await _db.RulebookRuleRows.Where(r => r.RulebookVersionId == id).ToListAsync(ct);
                var oldSections = await _db.RulebookSectionRows.Where(s => s.RulebookVersionId == id).ToListAsync(ct);
                _db.RulebookRuleRows.RemoveRange(oldRules);
                _db.RulebookSectionRows.RemoveRange(oldSections);
            }

            // Insert sections.
            if (root.TryGetProperty("sections", out var importSections) && importSections.ValueKind == JsonValueKind.Array)
            {
                var idx = 0;
                foreach (var s in importSections.EnumerateArray())
                {
                    var code = s.GetProperty("id").GetString()!;
                    var title = s.GetProperty("title").GetString() ?? code;
                    var order = s.TryGetProperty("order", out var oEl) && oEl.ValueKind == JsonValueKind.Number
                        ? oEl.GetInt32() : idx;
                    _db.RulebookSectionRows.Add(new RulebookSectionRow
                    {
                        Id = Guid.NewGuid().ToString(),
                        RulebookVersionId = id,
                        Code = code,
                        Title = title,
                        OrderIndex = order,
                    });
                    idx++;
                }
            }

            // Insert rules.
            var ridx = 0;
            foreach (var r in rulesEl.EnumerateArray())
            {
                var sectionCode = r.GetProperty("section").GetString()!;
                var orderIndex = r.TryGetProperty("order", out var oEl) && oEl.ValueKind == JsonValueKind.Number
                    ? oEl.GetInt32() : ridx;
                _db.RulebookRuleRows.Add(new RulebookRuleRow
                {
                    Id = Guid.NewGuid().ToString(),
                    RulebookVersionId = id,
                    Code = r.GetProperty("id").GetString()!,
                    SectionCode = sectionCode,
                    Title = r.GetProperty("title").GetString()!,
                    Body = r.GetProperty("body").GetString() ?? "",
                    Severity = r.GetProperty("severity").GetString()!.Trim().ToLowerInvariant(),
                    AppliesToJson = r.TryGetProperty("appliesTo", out var atEl) ? atEl.GetRawText() : "\"all\"",
                    TurnStage = r.TryGetProperty("turnStage", out var tsEl) && tsEl.ValueKind == JsonValueKind.String ? tsEl.GetString() : null,
                    ExemplarPhrasesJson = r.TryGetProperty("exemplarPhrases", out var epEl) ? epEl.GetRawText() : null,
                    ForbiddenPatternsJson = r.TryGetProperty("forbiddenPatterns", out var fpEl) ? fpEl.GetRawText() : null,
                    CheckId = r.TryGetProperty("checkId", out var ciEl) && ciEl.ValueKind == JsonValueKind.String ? ciEl.GetString() : null,
                    ParamsJson = r.TryGetProperty("params", out var pEl) ? pEl.GetRawText() : null,
                    ExamplesJson = r.TryGetProperty("examples", out var exEl) ? exEl.GetRawText() : null,
                    OrderIndex = orderIndex,
                });
                ridx++;
            }

            await _db.SaveChangesAsync(ct);
            InvalidateLoaderCache(target);
            return (await GetAsync(id, ct))!;
        }
    }

    private static string GetStringOrThrow(JsonElement el, string field)
    {
        if (!el.TryGetProperty(field, out var v) || v.ValueKind != JsonValueKind.String)
            throw ApiException.Validation("missing_field", $"Missing or non-string '{field}'.");
        var s = v.GetString();
        if (string.IsNullOrWhiteSpace(s))
            throw ApiException.Validation("empty_field", $"'{field}' must not be empty.");
        return s!;
    }

    private static object? ParseOrString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            using var d = JsonDocument.Parse(raw);
            return JsonSerializer.Deserialize<object?>(d.RootElement.GetRawText());
        }
        catch
        {
            return raw;
        }
    }

    // ── Section CRUD ─────────────────────────────────────────────────

    public async Task<RulebookSectionDto> CreateSectionAsync(string versionId, CreateSectionRequest req, string adminId, CancellationToken ct)
    {
        var v = await _db.RulebookVersions.FirstOrDefaultAsync(x => x.Id == versionId, ct)
            ?? throw new RulebookNotFoundException(versionId);
        if (string.IsNullOrWhiteSpace(req.Code)) throw ApiException.Validation("section_code_required", "Section code is required.");
        if (string.IsNullOrWhiteSpace(req.Title)) throw ApiException.Validation("section_title_required", "Section title is required.");

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
        if (string.IsNullOrWhiteSpace(req.Code)) throw ApiException.Validation("rule_code_required", "Rule code is required.");
        if (string.IsNullOrWhiteSpace(req.SectionCode)) throw ApiException.Validation("section_code_required", "Section code is required.");
        if (string.IsNullOrWhiteSpace(req.Title)) throw ApiException.Validation("rule_title_required", "Rule title is required.");
        if (string.IsNullOrWhiteSpace(req.Body)) throw ApiException.Validation("rule_body_required", "Rule body is required.");
        if (!IsValidSeverity(req.Severity)) throw ApiException.Validation("invalid_severity", "Severity must be one of: critical, major, minor, info.");

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
            if (!IsValidSeverity(req.Severity)) throw ApiException.Validation("invalid_severity", "Severity must be one of: critical, major, minor, info.");
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

    private static bool IsValidSeverity(string? s)
        => !string.IsNullOrWhiteSpace(s) && ValidSeverities.Contains(s.Trim().ToLowerInvariant());

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
