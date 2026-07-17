using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Content;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// Admin CRUD for the Materials library: folders, files, and audience assignment.
/// Uses the existing chunked-upload pipeline — files are uploaded via
/// /v1/admin/uploads first, then attached here with the resulting mediaAssetId.
/// </summary>
public static class MaterialsAdminEndpoints
{
    private const int MaxFolderDepth = 8;

    // Format → material "kind" map. Every recognised format is allowed for every
    // subtest — the Materials library is a general download store (owner directive
    // 2026-07-11: "all file types should be uploadable"). Unknown formats are still
    // rejected here, and the upload pipeline has already magic-byte-validated every
    // byte before it became a MediaAsset (see UploadSecurity.MagicByteValidator).
    private static readonly Dictionary<string, string> FormatKinds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["pdf"] = "pdf",
            ["doc"] = "document", ["docx"] = "document", ["txt"] = "document",
            ["csv"] = "document", ["rtf"] = "document",
            ["xls"] = "document", ["xlsx"] = "document",
            ["ppt"] = "document", ["pptx"] = "document",
            ["jpg"] = "image", ["jpeg"] = "image", ["png"] = "image",
            ["gif"] = "image", ["webp"] = "image",
            ["mp3"] = "audio", ["m4a"] = "audio", ["wav"] = "audio", ["ogg"] = "audio",
            ["mp4"] = "video", ["webm"] = "video", ["mov"] = "video",
        };

    public static IEndpointRouteBuilder MapMaterialsAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin/materials")
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUser");

        // ── Audience Options (plans + cohorts) ──────────────────────────────
        admin.MapGet("/audience-options", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var plans = await db.BillingPlans
                .AsNoTracking()
                .Where(p => p.Status == BillingPlanStatus.Active)
                .OrderBy(p => p.DisplayOrder)
                .Select(p => new { p.Id, p.Code, p.Name })
                .ToListAsync(ct);

            var cohorts = await db.Cohorts
                .AsNoTracking()
                .Where(c => c.Status == "active")
                .OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name, c.SponsorId })
                .ToListAsync(ct);

            var institutions = await db.SponsorAccounts
                .AsNoTracking()
                .OrderBy(s => s.Name)
                .Select(s => new { s.Id, name = s.OrganizationName ?? s.Name })
                .ToListAsync(ct);

            return Results.Ok(new { plans, cohorts, institutions });
        });

        // ── Folder CRUD ──────────────────────────────────────────────────────

        admin.MapGet("/folders", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var folders = await db.MaterialFolders
                .AsNoTracking()
                .Include(f => f.Audiences)
                .OrderBy(f => f.SortOrder)
                .ToListAsync(ct);

            var files = await db.MaterialFiles
                .AsNoTracking()
                .Include(f => f.MediaAsset)
                .OrderBy(f => f.SortOrder)
                .ToListAsync(ct);

            // Build tree rooted at null
            return Results.Ok(BuildAdminTree(folders, files, null));
        });

        admin.MapPost("/folders", async (
            HttpContext http,
            LearnerDbContext db,
            MaterialAccessService access,
            CreateFolderDto dto,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 200)
                return Results.BadRequest(new { code = "invalid_name", message = "Name is required (max 200 chars)." });

            if (dto.ParentFolderId != null)
            {
                var parent = await db.MaterialFolders.FindAsync([dto.ParentFolderId], ct);
                if (parent == null) return Results.NotFound(new { code = "parent_not_found" });

                var depth = await access.GetFolderDepthAsync(dto.ParentFolderId, ct);
                if (depth >= MaxFolderDepth)
                    return Results.BadRequest(new { code = "material_folder_too_deep", message = $"Folders may not be nested more than {MaxFolderDepth} levels deep." });
            }

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var now = DateTimeOffset.UtcNow;
            var folder = new MaterialFolder
            {
                Id = $"mfd_{Guid.NewGuid():N}",
                ParentFolderId = dto.ParentFolderId,
                Name = dto.Name.Trim(),
                Description = dto.Description?.Trim(),
                SubtestCode = dto.SubtestCode?.Trim().ToLowerInvariant(),
                AudienceMode = dto.AudienceMode ?? MaterialAudienceMode.Inherit,
                SortOrder = dto.SortOrder ?? 0,
                Status = ContentStatus.Draft,
                CreatedBy = adminId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.MaterialFolders.Add(folder);
            AddAudit(db, http, "MaterialFolderCreated", "MaterialFolder", folder.Id, folder.Name);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ProjectFolder(folder));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPut("/folders/{id}", async (
            string id,
            HttpContext http,
            LearnerDbContext db,
            UpdateFolderDto dto,
            CancellationToken ct) =>
        {
            var folder = await db.MaterialFolders.FindAsync([id], ct);
            if (folder == null) return Results.NotFound();

            if (dto.Name != null)
            {
                if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 200)
                    return Results.BadRequest(new { code = "invalid_name" });
                folder.Name = dto.Name.Trim();
            }
            if (dto.Description != null) folder.Description = dto.Description.Trim();
            if (dto.SubtestCode != null) folder.SubtestCode = dto.SubtestCode.Trim().ToLowerInvariant();
            if (dto.AudienceMode.HasValue) folder.AudienceMode = dto.AudienceMode.Value;
            if (dto.SortOrder.HasValue) folder.SortOrder = dto.SortOrder.Value;
            if (dto.Status != null)
            {
                if (!TryParseStatus(dto.Status, out var parsed))
                    return Results.BadRequest(new { code = "invalid_status" });
                folder.Status = parsed;
            }
            folder.UpdatedAt = DateTimeOffset.UtcNow;

            AddAudit(db, http, "MaterialFolderUpdated", "MaterialFolder", folder.Id, folder.Name);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ProjectFolder(folder));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/folders/{id}/move", async (
            string id,
            HttpContext http,
            LearnerDbContext db,
            MaterialAccessService access,
            MoveFolderDto dto,
            CancellationToken ct) =>
        {
            var folder = await db.MaterialFolders.FindAsync([id], ct);
            if (folder == null) return Results.NotFound();

            if (dto.ParentFolderId != null)
            {
                if (dto.ParentFolderId == id)
                    return Results.BadRequest(new { code = "cannot_move_into_self" });

                var newParent = await db.MaterialFolders.FindAsync([dto.ParentFolderId], ct);
                if (newParent == null) return Results.NotFound(new { code = "parent_not_found" });

                // Prevent circular nesting
                var ancestorId = dto.ParentFolderId;
                while (ancestorId != null)
                {
                    var ancestor = await db.MaterialFolders.AsNoTracking()
                        .Where(f => f.Id == ancestorId)
                        .Select(f => new { f.ParentFolderId })
                        .FirstOrDefaultAsync(ct);
                    if (ancestor == null) break;
                    if (ancestor.ParentFolderId == id)
                        return Results.BadRequest(new { code = "circular_move", message = "Cannot move a folder into its own descendant." });
                    ancestorId = ancestor.ParentFolderId;
                }

                var depth = await access.GetFolderDepthAsync(dto.ParentFolderId, ct);
                if (depth >= MaxFolderDepth)
                    return Results.BadRequest(new { code = "material_folder_too_deep", message = $"Folders may not be nested more than {MaxFolderDepth} levels deep." });
            }

            folder.ParentFolderId = dto.ParentFolderId;
            folder.SortOrder = dto.SortOrder ?? folder.SortOrder;
            folder.UpdatedAt = DateTimeOffset.UtcNow;
            AddAudit(db, http, "MaterialFolderMoved", "MaterialFolder", folder.Id, folder.Name);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ProjectFolder(folder));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/folders/reorder", async (
            HttpContext http,
            LearnerDbContext db,
            ReorderDto dto,
            CancellationToken ct) =>
        {
            var ids = dto.Items.Select(x => x.Id).ToList();
            var folders = await db.MaterialFolders.Where(f => ids.Contains(f.Id)).ToListAsync(ct);
            foreach (var item in dto.Items)
            {
                var folder = folders.FirstOrDefault(f => f.Id == item.Id);
                if (folder != null) folder.SortOrder = item.SortOrder;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { updated = folders.Count });
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapDelete("/folders/{id}", async (
            string id,
            HttpContext http,
            LearnerDbContext db,
            bool? recursive,
            CancellationToken ct) =>
        {
            var folder = await db.MaterialFolders.FindAsync([id], ct);
            if (folder == null) return Results.NotFound();

            var hasChildren = await db.MaterialFolders.AnyAsync(f => f.ParentFolderId == id, ct);
            var hasFiles = await db.MaterialFiles.AnyAsync(f => f.FolderId == id, ct);

            if ((hasChildren || hasFiles) && recursive != true)
                return Results.Json(
                    new { code = "folder_not_empty", message = "Folder is not empty. Pass ?recursive=true to delete recursively." },
                    statusCode: StatusCodes.Status409Conflict);

            if (recursive == true)
                await DeleteFolderRecursiveAsync(db, id, ct);
            else
                db.MaterialFolders.Remove(folder);

            AddAudit(db, http, "MaterialFolderDeleted", "MaterialFolder", id, folder.Name);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { deleted = true, id });
        })
        .WithAdminWrite("AdminContentWrite");

        // ── Folder Audience ──────────────────────────────────────────────────

        admin.MapPut("/folders/{id}/audience", async (
            string id,
            HttpContext http,
            LearnerDbContext db,
            SetAudienceDto dto,
            CancellationToken ct) =>
        {
            var folder = await db.MaterialFolders
                .Include(f => f.Audiences)
                .FirstOrDefaultAsync(f => f.Id == id, ct);
            if (folder == null) return Results.NotFound();

            folder.AudienceMode = dto.AudienceMode;
            folder.UpdatedAt = DateTimeOffset.UtcNow;

            // Replace audience rows
            db.MaterialFolderAudiences.RemoveRange(folder.Audiences);
            if (dto.AudienceMode == MaterialAudienceMode.Restricted && dto.Audiences != null)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var aud in dto.Audiences)
                {
                    db.MaterialFolderAudiences.Add(new MaterialFolderAudience
                    {
                        Id = $"mau_{Guid.NewGuid():N}",
                        FolderId = id,
                        TargetType = aud.TargetType,
                        TargetId = aud.TargetId,
                        CreatedAt = now,
                    });
                }
            }

            AddAudit(db, http, "MaterialFolderAudienceSet", "MaterialFolder", id,
                $"mode={dto.AudienceMode};rows={dto.Audiences?.Count ?? 0}");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id, mode = dto.AudienceMode.ToString() });
        })
        .WithAdminWrite("AdminContentWrite");

        // ── File CRUD ────────────────────────────────────────────────────────

        admin.MapGet("/files", async (
            LearnerDbContext db,
            CancellationToken ct,
            string? folderId,
            string? subtest,
            string? status,
            int? page,
            int? pageSize) =>
        {
            var effectivePage = Math.Max(1, page ?? 1);
            var effectivePageSize = Math.Clamp(pageSize ?? 20, 1, 100);

            var query = db.MaterialFiles
                .AsNoTracking()
                .Include(f => f.MediaAsset)
                .AsQueryable();

            if (folderId != null) query = query.Where(f => f.FolderId == folderId);
            if (!string.IsNullOrWhiteSpace(subtest)) query = query.Where(f => f.SubtestCode == subtest.ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ContentStatus>(status, true, out var parsedStatus))
                query = query.Where(f => f.Status == parsedStatus);

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderBy(f => f.SortOrder)
                .Skip((effectivePage - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                items = items.Select(ProjectFile),
                total,
                page = effectivePage,
                pageSize = effectivePageSize,
            });
        });

        admin.MapPost("/files", async (
            HttpContext http,
            LearnerDbContext db,
            CreateFileDto dto,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.MediaAssetId))
                return Results.BadRequest(new { code = "media_asset_required" });
            if (string.IsNullOrWhiteSpace(dto.SubtestCode))
                return Results.BadRequest(new { code = "subtest_required" });
            if (string.IsNullOrWhiteSpace(dto.Title) || dto.Title.Length > 200)
                return Results.BadRequest(new { code = "invalid_title" });

            var asset = await db.MediaAssets.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == dto.MediaAssetId && a.Status == MediaAssetStatus.Ready, ct);
            if (asset == null)
                return Results.NotFound(new { code = "media_asset_not_found" });

            var subtestNorm = dto.SubtestCode.Trim().ToLowerInvariant();
            var kindResult = DeriveKindAndValidate(asset, subtestNorm);
            if (kindResult is null)
                return Results.BadRequest(new
                {
                    code = "material_file_type_invalid",
                    message = $"Unsupported file format '{asset.Format}'. Allowed: PDF, documents " +
                              "(doc, docx, txt, csv, rtf, xls, xlsx, ppt, pptx), images (jpg, png, gif, webp), " +
                              "audio (mp3, m4a, wav, ogg) and video (mp4, webm, mov).",
                });

            if (dto.FolderId != null)
            {
                var folder = await db.MaterialFolders.FindAsync([dto.FolderId], ct);
                if (folder == null) return Results.NotFound(new { code = "folder_not_found" });
            }

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var now = DateTimeOffset.UtcNow;
            var file = new MaterialFile
            {
                Id = $"mfl_{Guid.NewGuid():N}",
                FolderId = dto.FolderId,
                MediaAssetId = dto.MediaAssetId,
                SubtestCode = subtestNorm,
                Kind = kindResult,
                Title = dto.Title.Trim(),
                Description = dto.Description?.Trim(),
                SortOrder = dto.SortOrder ?? 0,
                Status = ContentStatus.Draft,
                CreatedBy = adminId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.MaterialFiles.Add(file);
            AddAudit(db, http, "MaterialFileCreated", "MaterialFile", file.Id,
                $"media={dto.MediaAssetId};subtest={subtestNorm};kind={kindResult}");
            await db.SaveChangesAsync(ct);
            await db.Entry(file).Reference(x => x.MediaAsset).LoadAsync(ct);
            return Results.Ok(ProjectFile(file));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPut("/files/{id}", async (
            string id,
            HttpContext http,
            LearnerDbContext db,
            UpdateFileDto dto,
            CancellationToken ct) =>
        {
            var file = await db.MaterialFiles
                .Include(f => f.MediaAsset)
                .FirstOrDefaultAsync(f => f.Id == id, ct);
            if (file == null) return Results.NotFound();

            // Replace asset if requested
            if (dto.MediaAssetId != null && dto.MediaAssetId != file.MediaAssetId)
            {
                var asset = await db.MediaAssets.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == dto.MediaAssetId && a.Status == MediaAssetStatus.Ready, ct);
                if (asset == null)
                    return Results.NotFound(new { code = "media_asset_not_found" });

                var subtestToCheck = (dto.SubtestCode?.Trim().ToLowerInvariant()) ?? file.SubtestCode;
                var kindResult = DeriveKindAndValidate(asset, subtestToCheck);
                if (kindResult is null)
                    return Results.BadRequest(new { code = "material_file_type_invalid" });

                file.MediaAssetId = dto.MediaAssetId;
                file.Kind = kindResult;
                file.MediaAsset = asset;
            }
            else if (dto.SubtestCode != null)
            {
                // Re-validate current asset against new subtest
                var asset = file.MediaAsset ?? await db.MediaAssets.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == file.MediaAssetId, ct);
                if (asset != null)
                {
                    var subtestNorm = dto.SubtestCode.Trim().ToLowerInvariant();
                    var kindResult = DeriveKindAndValidate(asset, subtestNorm);
                    if (kindResult is null)
                        return Results.BadRequest(new { code = "material_file_type_invalid" });
                    file.SubtestCode = subtestNorm;
                    file.Kind = kindResult;
                }
            }

            if (dto.Title != null)
            {
                if (string.IsNullOrWhiteSpace(dto.Title) || dto.Title.Length > 200)
                    return Results.BadRequest(new { code = "invalid_title" });
                file.Title = dto.Title.Trim();
            }
            if (dto.Description != null) file.Description = dto.Description.Trim();
            if (dto.FolderId != null)
            {
                var folder = await db.MaterialFolders.FindAsync([dto.FolderId], ct);
                if (folder == null) return Results.NotFound(new { code = "folder_not_found" });
                file.FolderId = dto.FolderId;
            }
            if (dto.SortOrder.HasValue) file.SortOrder = dto.SortOrder.Value;
            if (dto.Status != null)
            {
                if (!TryParseStatus(dto.Status, out var parsed))
                    return Results.BadRequest(new { code = "invalid_status" });
                file.Status = parsed;
            }
            file.UpdatedAt = DateTimeOffset.UtcNow;

            AddAudit(db, http, "MaterialFileUpdated", "MaterialFile", file.Id, file.Title);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ProjectFile(file));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/files/reorder", async (
            HttpContext http,
            LearnerDbContext db,
            ReorderDto dto,
            CancellationToken ct) =>
        {
            var ids = dto.Items.Select(x => x.Id).ToList();
            var files = await db.MaterialFiles.Where(f => ids.Contains(f.Id)).ToListAsync(ct);
            foreach (var item in dto.Items)
            {
                var file = files.FirstOrDefault(f => f.Id == item.Id);
                if (file != null) file.SortOrder = item.SortOrder;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { updated = files.Count });
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapDelete("/files/{id}", async (
            string id,
            HttpContext http,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var file = await db.MaterialFiles.FindAsync([id], ct);
            if (file == null) return Results.NotFound();

            db.MaterialFiles.Remove(file);
            AddAudit(db, http, "MaterialFileDeleted", "MaterialFile", id, file.Title);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { deleted = true, id });
        })
        .WithAdminWrite("AdminContentWrite");

        return app;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a status string (e.g. "Published", case-insensitive) into a
    /// ContentStatus. The DTOs bind Status as a string because ContentStatus
    /// carries no JsonStringEnumConverter; this is the manual parse step.
    /// </summary>
    private static bool TryParseStatus(string? raw, out ContentStatus status) =>
        Enum.TryParse(raw, ignoreCase: true, out status) && Enum.IsDefined(status);

    /// <summary>
    /// Maps the MediaAsset format to its material "kind"
    /// ("pdf" | "document" | "image" | "audio" | "video"), or null for an
    /// unrecognised format. All recognised kinds are allowed for every subtest;
    /// <paramref name="subtestNorm"/> is retained for future per-subtest rules.
    /// </summary>
    private static string? DeriveKindAndValidate(MediaAsset asset, string subtestNorm)
    {
        _ = subtestNorm;
        var fmt = (asset.Format ?? string.Empty).Trim().ToLowerInvariant();
        return FormatKinds.TryGetValue(fmt, out var kind) ? kind : null;
    }

    private static async Task DeleteFolderRecursiveAsync(LearnerDbContext db, string folderId, CancellationToken ct)
    {
        var childFolderIds = await db.MaterialFolders
            .AsNoTracking()
            .Where(f => f.ParentFolderId == folderId)
            .Select(f => f.Id)
            .ToListAsync(ct);

        foreach (var childId in childFolderIds)
            await DeleteFolderRecursiveAsync(db, childId, ct);

        var files = await db.MaterialFiles.Where(f => f.FolderId == folderId).ToListAsync(ct);
        db.MaterialFiles.RemoveRange(files);

        var folder = await db.MaterialFolders.FindAsync([folderId], ct);
        if (folder != null) db.MaterialFolders.Remove(folder);
    }

    private static IReadOnlyList<object> BuildAdminTree(
        List<MaterialFolder> folders,
        List<MaterialFile> files,
        string? parentId)
    {
        return folders
            .Where(f => f.ParentFolderId == parentId)
            .Select(f => (object)new
            {
                f.Id,
                f.ParentFolderId,
                f.Name,
                f.Description,
                f.SubtestCode,
                audienceMode = f.AudienceMode.ToString(),
                f.SortOrder,
                status = f.Status.ToString(),
                f.CreatedBy,
                f.CreatedAt,
                f.UpdatedAt,
                audiences = f.Audiences.Select(a => new
                {
                    a.Id,
                    a.TargetType,
                    a.TargetId,
                }),
                folders = BuildAdminTree(folders, files, f.Id),
                files = files.Where(x => x.FolderId == f.Id).Select(ProjectFile).ToList(),
            })
            .ToList();
    }

    private static object ProjectFolder(MaterialFolder f) => new
    {
        f.Id,
        f.ParentFolderId,
        f.Name,
        f.Description,
        f.SubtestCode,
        audienceMode = f.AudienceMode.ToString(),
        f.SortOrder,
        status = f.Status.ToString(),
        f.CreatedBy,
        f.CreatedAt,
        f.UpdatedAt,
    };

    private static object ProjectFile(MaterialFile f) => new
    {
        f.Id,
        f.FolderId,
        f.MediaAssetId,
        f.SubtestCode,
        f.Kind,
        f.Title,
        f.Description,
        f.SortOrder,
        status = f.Status.ToString(),
        f.CreatedBy,
        f.CreatedAt,
        f.UpdatedAt,
        media = f.MediaAsset is null ? null : new
        {
            f.MediaAsset.Id,
            f.MediaAsset.OriginalFilename,
            f.MediaAsset.MimeType,
            f.MediaAsset.SizeBytes,
            f.MediaAsset.Format,
        },
    };

    private static void AddAudit(LearnerDbContext db, HttpContext http, string action, string resourceType, string? resourceId, string? details)
    {
        var actorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            ActorName = http.User.Identity?.Name ?? actorId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details,
        });
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

file sealed record CreateFolderDto(
    string? ParentFolderId,
    string Name,
    string? Description,
    string? SubtestCode,
    MaterialAudienceMode? AudienceMode,
    int? SortOrder);

file sealed record UpdateFolderDto(
    string? Name,
    string? Description,
    string? SubtestCode,
    MaterialAudienceMode? AudienceMode,
    int? SortOrder,
    // Bound as string (not ContentStatus) because ContentStatus has no
    // JsonStringEnumConverter and there is no global one — a raw enum property
    // would reject the JSON string "Published" with a 400 at model-binding time.
    string? Status);

file sealed record MoveFolderDto(
    string? ParentFolderId,
    int? SortOrder);

file sealed record SetAudienceDto(
    MaterialAudienceMode AudienceMode,
    List<AudienceRowDto>? Audiences);

file sealed record AudienceRowDto(string TargetType, string TargetId);

file sealed record CreateFileDto(
    string? FolderId,
    string MediaAssetId,
    string SubtestCode,
    string Title,
    string? Description,
    int? SortOrder);

file sealed record UpdateFileDto(
    string? FolderId,
    string? MediaAssetId,
    string? SubtestCode,
    string? Title,
    string? Description,
    int? SortOrder,
    // Bound as string (see UpdateFolderDto.Status) to avoid the enum-binding 400.
    string? Status);

file sealed record ReorderDto(List<ReorderItem> Items);
file sealed record ReorderItem(string Id, int SortOrder);
