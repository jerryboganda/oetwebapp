using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin Documentation Center — the evidence-grade platform documentation system
/// (Master Dossier + 15 specialist reports, each carrying its own Evidence Register,
/// version history and sign-off). Owner-only: gated on <c>AdminSystemAdmin</c>
/// everywhere, matching Settings/Webhooks/Launch Readiness (there is no separate
/// "Owner" role in this codebase — SystemAdmin already carries <see cref="AdminPermissions.All"/>).
/// </summary>
public static class DocumentationCenterAdminEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapDocumentationCenterAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/documentation-center")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser")
            .WithTags("Admin – Documentation Center");

        group.MapGet("/modules", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var modules = await db.DocumentationModules.AsNoTracking()
                .OrderBy(m => m.SortOrder)
                .Select(m => new
                {
                    m.Id,
                    m.Title,
                    m.Description,
                    Current = m.Versions.Where(v => v.IsCurrent).Select(v => new
                    {
                        v.VersionNumber,
                        v.Status,
                        v.GeneratedAt,
                        v.ApprovedByName,
                        v.ApprovedAt,
                    }).FirstOrDefault(),
                })
                .ToListAsync(ct);

            return Results.Ok(modules.Select(m => new DocumentationModuleSummary(
                m.Id, m.Title, m.Description,
                m.Current?.VersionNumber ?? 0,
                m.Current?.Status.ToString() ?? "NotYetAvailable",
                m.Current?.GeneratedAt,
                m.Current?.ApprovedByName,
                ImmigrationReady: m.Current is { Status: DocumentationVersionStatus.Published })));
        }).WithAdminRead("AdminSystemAdmin");

        group.MapGet("/modules/{code}", async (string code, LearnerDbContext db, CancellationToken ct) =>
        {
            var module = await db.DocumentationModules.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == code, ct);
            if (module is null) return Results.NotFound();

            var current = await db.DocumentationVersions.AsNoTracking()
                .Where(v => v.ModuleId == code && v.IsCurrent)
                .FirstOrDefaultAsync(ct);
            var versions = await db.DocumentationVersions.AsNoTracking()
                .Where(v => v.ModuleId == code)
                .OrderByDescending(v => v.VersionNumber)
                .Select(v => new DocumentationVersionSummary(v.Id, v.VersionNumber, v.Status.ToString(), v.GeneratedAt, v.ApprovedByName, v.ApprovedAt))
                .ToListAsync(ct);
            var evidence = await db.DocumentationEvidenceItems.AsNoTracking()
                .Where(e => e.ModuleId == code)
                .OrderBy(e => e.EvidenceId)
                .Select(e => new DocumentationEvidenceSummary(e.EvidenceId, e.EvidenceType.ToString(), e.Description, e.SourceReference, e.IsInternalOnly))
                .ToListAsync(ct);

            var sections = current is null
                ? []
                : JsonSerializer.Deserialize<List<DocumentationSectionBlock>>(current.ContentJson, JsonOpts) ?? [];

            return Results.Ok(new DocumentationModuleDetail(
                module.Id, module.Title, module.Description,
                current?.VersionNumber ?? 0,
                current?.Status.ToString() ?? "NotYetAvailable",
                sections.Select(s => new DocumentationSectionSummary(s.Heading, s.BodyMarkdown, s.IsInternalOnly)).ToList(),
                evidence, versions));
        }).WithAdminRead("AdminSystemAdmin");

        group.MapGet("/evidence", async (string? q, string? moduleId, LearnerDbContext db, CancellationToken ct) =>
        {
            var query = db.DocumentationEvidenceItems.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(moduleId)) query = query.Where(e => e.ModuleId == moduleId);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(e => EF.Functions.ILike(e.Description, $"%{term}%")
                    || EF.Functions.ILike(e.EvidenceId, $"%{term}%")
                    || EF.Functions.ILike(e.SourceReference, $"%{term}%"));
            }
            var items = await query.OrderBy(e => e.EvidenceId)
                .Select(e => new DocumentationEvidenceRow(e.EvidenceId, e.ModuleId, e.EvidenceType.ToString(), e.Description, e.SourceReference, e.IsInternalOnly))
                .ToListAsync(ct);
            return Results.Ok(items);
        }).WithAdminRead("AdminSystemAdmin");

        group.MapGet("/exports", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var exports = await db.DocumentationExports.AsNoTracking()
                .OrderByDescending(e => e.GeneratedAt)
                .Take(200)
                .Select(e => new DocumentationExportRow(e.Id, e.ExportType.ToString(), e.Mode.ToString(), e.ModuleId, e.GeneratedAt, e.GeneratedByName, e.Sha256Hash))
                .ToListAsync(ct);
            return Results.Ok(exports);
        }).WithAdminRead("AdminSystemAdmin");

        group.MapGet("/modules/{code}/pdf", async (
                string code, string? mode, HttpContext http, LearnerDbContext db, IDocumentationPdfService pdf, IFileStorage storage, CancellationToken ct) =>
            {
                var exportMode = ParseMode(mode);
                var module = await db.DocumentationModules.AsNoTracking().FirstOrDefaultAsync(m => m.Id == code, ct);
                if (module is null) return Results.NotFound();

                var version = await db.DocumentationVersions.AsNoTracking()
                    .FirstOrDefaultAsync(v => v.ModuleId == code && v.IsCurrent, ct);
                if (version is null) return Results.NotFound("This module has no published version yet.");

                var evidence = await LoadEvidence(db, exportMode, code, ct);
                var pdfModel = BuildPdfModel(
                    reportName: module.Title,
                    modeLabel: DescribeMode(exportMode),
                    modules: [BuildModuleModel(module, version, exportMode)],
                    evidence: evidence,
                    revisionHistory: await BuildRevisionHistory(db, code, ct),
                    sourceRepoCommitSha: version.SourceRepoCommitSha);

                var artifact = pdf.Generate(pdfModel);
                await RecordExport(db, storage, http, DocumentationExportType.Module, exportMode, code, [version.Id], artifact, ct);
                WritePdfHeaders(http, artifact.Filename);
                return Results.File(artifact.Bytes, "application/pdf", artifact.Filename);
            })
            .WithAdminRead("AdminSystemAdmin");

        group.MapGet("/master/pdf", async (
                string? mode, HttpContext http, LearnerDbContext db, IDocumentationPdfService pdf, IFileStorage storage, CancellationToken ct) =>
            {
                var exportMode = ParseMode(mode);
                var modules = await db.DocumentationModules.AsNoTracking().OrderBy(m => m.SortOrder).ToListAsync(ct);
                var currentVersions = await db.DocumentationVersions.AsNoTracking()
                    .Where(v => v.IsCurrent).ToDictionaryAsync(v => v.ModuleId, ct);

                var moduleModels = new List<DocumentationPdfModuleModel>();
                var includedVersionIds = new List<string>();
                foreach (var module in modules)
                {
                    if (!currentVersions.TryGetValue(module.Id, out var version)) continue;
                    moduleModels.Add(BuildModuleModel(module, version, exportMode));
                    includedVersionIds.Add(version.Id);
                }

                var evidence = await LoadEvidence(db, exportMode, moduleId: null, ct);
                var pdfModel = BuildPdfModel(
                    reportName: "Master Technical & Innovation Dossier — Complete Evidence Pack",
                    modeLabel: DescribeMode(exportMode),
                    modules: moduleModels,
                    evidence: evidence,
                    revisionHistory: await BuildRevisionHistory(db, moduleId: null, ct),
                    sourceRepoCommitSha: moduleModels.Count > 0 ? currentVersions.Values.First().SourceRepoCommitSha : null,
                    includeSignOff: true);

                var artifact = pdf.Generate(pdfModel);
                await RecordExport(db, storage, http, DocumentationExportType.Master, exportMode, moduleId: null, includedVersionIds, artifact, ct);
                WritePdfHeaders(http, artifact.Filename);
                return Results.File(artifact.Bytes, "application/pdf", artifact.Filename);
            })
            .WithAdminRead("AdminSystemAdmin");

        group.MapGet("/evidence/pdf", async (
                string? mode, HttpContext http, LearnerDbContext db, IDocumentationPdfService pdf, IFileStorage storage, CancellationToken ct) =>
            {
                var exportMode = ParseMode(mode);
                var evidence = await LoadEvidence(db, exportMode, moduleId: null, ct);
                var pdfModel = BuildPdfModel(
                    reportName: "Technical Evidence Annex",
                    modeLabel: DescribeMode(exportMode),
                    modules: [],
                    evidence: evidence,
                    revisionHistory: [],
                    sourceRepoCommitSha: null);

                var artifact = pdf.Generate(pdfModel);
                await RecordExport(db, storage, http, DocumentationExportType.EvidenceAnnex, exportMode, moduleId: null, [], artifact, ct);
                WritePdfHeaders(http, artifact.Filename);
                return Results.File(artifact.Bytes, "application/pdf", artifact.Filename);
            })
            .WithAdminRead("AdminSystemAdmin");

        group.MapGet("/exports/{id}/download", async (
                string id, HttpContext http, LearnerDbContext db, IFileStorage storage, CancellationToken ct) =>
            {
                var export = await db.DocumentationExports.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
                if (export?.MediaAssetId is null) return Results.NotFound();

                var asset = await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(m => m.Id == export.MediaAssetId, ct);
                if (asset is null) return Results.NotFound();

                var stream = await storage.OpenReadAsync(asset.StoragePath, ct);
                WritePdfHeaders(http, asset.OriginalFilename);
                return Results.Stream(stream, asset.MimeType);
            })
            .WithAdminRead("AdminSystemAdmin");

        return app;
    }

    // ── Assembly helpers ────────────────────────────────────────────

    private static DocumentationExportMode ParseMode(string? mode)
        => string.Equals(mode, "external", StringComparison.OrdinalIgnoreCase)
            ? DocumentationExportMode.External
            : DocumentationExportMode.Internal;

    private static string DescribeMode(DocumentationExportMode mode)
        => mode == DocumentationExportMode.External
            ? "External — Immigration / Innovation Review"
            : "Internal — Confidential, Owner/Super-Admin Only";

    private static DocumentationPdfModuleModel BuildModuleModel(DocumentationModule module, DocumentationVersion version, DocumentationExportMode mode)
    {
        var sections = JsonSerializer.Deserialize<List<DocumentationSectionBlock>>(version.ContentJson, JsonOpts) ?? [];
        var visible = mode == DocumentationExportMode.External ? sections.Where(s => !s.IsInternalOnly) : sections;
        return new DocumentationPdfModuleModel(
            module.Id, module.Title, version.VersionNumber, version.GeneratedAt,
            visible.Select(s => new DocumentationPdfSectionModel(s.Heading, s.BodyMarkdown)).ToList());
    }

    private static async Task<List<DocumentationPdfEvidenceEntry>> LoadEvidence(LearnerDbContext db, DocumentationExportMode mode, string? moduleId, CancellationToken ct)
    {
        var query = db.DocumentationEvidenceItems.AsNoTracking().AsQueryable();
        if (moduleId is not null) query = query.Where(e => e.ModuleId == moduleId);
        if (mode == DocumentationExportMode.External) query = query.Where(e => !e.IsInternalOnly);
        return await query.OrderBy(e => e.EvidenceId)
            .Select(e => new DocumentationPdfEvidenceEntry(e.EvidenceId, e.EvidenceType.ToString(), e.Description, e.SourceReference))
            .ToListAsync(ct);
    }

    private static async Task<List<DocumentationPdfRevisionRow>> BuildRevisionHistory(LearnerDbContext db, string? moduleId, CancellationToken ct)
    {
        var query = db.DocumentationVersions.AsNoTracking().AsQueryable();
        if (moduleId is not null) query = query.Where(v => v.ModuleId == moduleId);
        var rows = await query.OrderBy(v => v.ModuleId).ThenByDescending(v => v.VersionNumber)
            .Select(v => new
            {
                v.ModuleId,
                v.VersionNumber,
                v.GeneratedAt,
                v.ApprovedByName,
                v.Status,
            })
            .ToListAsync(ct);

        return rows.Select(v => new DocumentationPdfRevisionRow(
            $"{v.ModuleId} v{v.VersionNumber}",
            v.GeneratedAt.UtcDateTime.ToString("dd MMM yyyy"),
            "Technical lead",
            v.ApprovedByName ?? "—",
            v.Status.ToString(),
            v.Status == DocumentationVersionStatus.Published ? "Published" : "Superseded")).ToList();
    }

    private static DocumentationPdfModel BuildPdfModel(
        string reportName,
        string modeLabel,
        IReadOnlyList<DocumentationPdfModuleModel> modules,
        IReadOnlyList<DocumentationPdfEvidenceEntry> evidence,
        IReadOnlyList<DocumentationPdfRevisionRow> revisionHistory,
        string? sourceRepoCommitSha,
        bool includeSignOff = false)
    {
        List<DocumentationPdfSignOffRow> signOffs = includeSignOff
            ?
            [
                new DocumentationPdfSignOffRow("Founder / Product Owner", "Dr Ahmed Hesham", "—"),
                new DocumentationPdfSignOffRow("Technical Lead", "Dr Faisal Maqsood Anwar", "—"),
            ]
            : [];

        return new DocumentationPdfModel(
            ReportName: reportName,
            Owner: "Dr Ahmed Hesham / OET with Dr Ahmed Hesham",
            Version: DateTimeOffset.UtcNow.ToString("yyyy.MM.dd"),
            ReportDate: DateTimeOffset.UtcNow,
            DocumentStatus: "Published",
            DocumentId: $"DOC-{Guid.NewGuid():N}"[..24].ToUpperInvariant(),
            SourceRepoCommitSha: sourceRepoCommitSha,
            ModeLabel: modeLabel,
            Modules: modules,
            EvidenceRegister: evidence,
            RevisionHistory: revisionHistory,
            SignOffs: signOffs);
    }

    private static async Task RecordExport(
        LearnerDbContext db, IFileStorage storage, HttpContext http,
        DocumentationExportType type, DocumentationExportMode mode, string? moduleId,
        IReadOnlyList<string> includedVersionIds, DocumentationPdfArtifact artifact, CancellationToken ct)
    {
        var hash = Convert.ToHexString(SHA256.HashData(artifact.Bytes)).ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var exportId = $"DOCEXP-{Guid.NewGuid():N}";

        // Persist the exact bytes so "download a prior version" (spec §3/§15) is real,
        // not just an audit-log line — mirrors the MediaAsset/IFileStorage pattern used
        // for every other user-facing document in this codebase.
        var storageKey = $"documentation-exports/{exportId}.pdf";
        await using (var writeStream = await storage.OpenWriteAsync(storageKey, ct))
        {
            await writeStream.WriteAsync(artifact.Bytes, ct);
        }
        var mediaAssetId = $"DOCMEDIA-{Guid.NewGuid():N}";
        db.MediaAssets.Add(new MediaAsset
        {
            Id = mediaAssetId,
            OriginalFilename = artifact.Filename,
            MimeType = "application/pdf",
            Format = "pdf",
            SizeBytes = artifact.Bytes.LongLength,
            StoragePath = storageKey,
            Status = MediaAssetStatus.Ready,
            Sha256 = hash,
            MediaKind = "document",
            UploadedBy = http.AdminId(),
            UploadedAt = now,
            ProcessedAt = now,
        });

        db.DocumentationExports.Add(new DocumentationExport
        {
            Id = exportId,
            ExportType = type,
            Mode = mode,
            ModuleId = moduleId,
            GeneratedAt = now,
            GeneratedByUserId = http.AdminId(),
            GeneratedByName = http.AdminName(),
            Sha256Hash = hash,
            MediaAssetId = mediaAssetId,
            IncludedVersionIdsJson = JsonSerializer.Serialize(includedVersionIds, JsonOpts),
        });
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = now,
            ActorId = http.AdminId(),
            ActorName = http.AdminName(),
            Action = "DocumentationCenter.GeneratedPdf",
            ResourceType = type.ToString(),
            ResourceId = moduleId ?? type.ToString(),
            Details = JsonSerializer.Serialize(new { mode = mode.ToString(), sha256 = hash }, JsonOpts),
        });
        http.Response.Headers["X-Document-Sha256"] = hash;
        await db.SaveChangesAsync(ct);
    }

    private static void WritePdfHeaders(HttpContext http, string filename)
    {
        http.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        http.Response.Headers["Cache-Control"] = "private, no-store";
    }

    private static string AdminId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated admin id is required.");

    private static string AdminName(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Admin";
}

// ── Response DTOs ───────────────────────────────────────────────────

public sealed record DocumentationModuleSummary(
    string Code, string Title, string Description,
    int VersionNumber, string Status, DateTimeOffset? GeneratedAt, string? ApprovedByName, bool ImmigrationReady);

public sealed record DocumentationVersionSummary(
    string Id, int VersionNumber, string Status, DateTimeOffset GeneratedAt, string? ApprovedByName, DateTimeOffset? ApprovedAt);

public sealed record DocumentationSectionSummary(string Heading, string BodyMarkdown, bool IsInternalOnly);

public sealed record DocumentationEvidenceSummary(string EvidenceId, string EvidenceType, string Description, string SourceReference, bool IsInternalOnly);

public sealed record DocumentationModuleDetail(
    string Code, string Title, string Description, int VersionNumber, string Status,
    IReadOnlyList<DocumentationSectionSummary> Sections,
    IReadOnlyList<DocumentationEvidenceSummary> Evidence,
    IReadOnlyList<DocumentationVersionSummary> Versions);

public sealed record DocumentationEvidenceRow(string EvidenceId, string ModuleId, string EvidenceType, string Description, string SourceReference, bool IsInternalOnly);

public sealed record DocumentationExportRow(string Id, string ExportType, string Mode, string? ModuleId, DateTimeOffset GeneratedAt, string GeneratedByName, string Sha256Hash);
