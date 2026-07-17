using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// Admin CRUD for the practice-collection labels ("recall set tags") that
/// admins use to categorise vocabulary terms. The 3 canonical codes from the
/// <see cref="RecallSetCodes"/> static registry are seeded into the
/// <c>RecallSetTags</c> table on first boot; admins can then add/edit/archive
/// more from <c>/admin/content/vocabulary/recall-set-tags</c>.
/// </summary>
public static class RecallSetTagsEndpoints
{
    public static IEndpointRouteBuilder MapRecallSetTagsEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin/recall-set-tags")
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUser");

        admin.MapGet("", async (LearnerDbContext db, bool? includeArchived, string? examTypeCode, CancellationToken ct) =>
        {
            var q = db.RecallSetTags.AsNoTracking().AsQueryable();
            if (!(includeArchived ?? false)) q = q.Where(x => x.IsActive);
            if (!string.IsNullOrWhiteSpace(examTypeCode))
            {
                var ex = examTypeCode.ToLowerInvariant();
                q = q.Where(x => x.ExamTypeCode == null || x.ExamTypeCode == ex);
            }
            var rows = await q.OrderBy(x => x.SortOrder).ThenBy(x => x.DisplayName).ToListAsync(ct);
            return Results.Ok(rows.Select(Project));
        });

        admin.MapGet("/{code}", async (string code, LearnerDbContext db, CancellationToken ct) =>
        {
            var row = await db.RecallSetTags.AsNoTracking().FirstOrDefaultAsync(x => x.Code == code.ToLowerInvariant(), ct);
            return row is null ? Results.NotFound() : Results.Ok(Project(row));
        });

        admin.MapPost("", async (
            HttpContext http,
            LearnerDbContext db,
            RecallSetTagCreate dto,
            CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            if (string.IsNullOrWhiteSpace(dto.Code) || dto.Code.Length > 64)
                return Results.BadRequest(new { error = "code required (max 64 chars, lowercase a-z 0-9 -)" });
            var code = dto.Code.Trim().ToLowerInvariant();
            if (!IsValidCode(code))
                return Results.BadRequest(new { error = "code must contain only lowercase letters, digits and hyphens" });
            if (string.IsNullOrWhiteSpace(dto.DisplayName) || dto.DisplayName.Length > 200)
                return Results.BadRequest(new { error = "displayName required (max 200 chars)" });

            var exists = await db.RecallSetTags.AnyAsync(x => x.Code == code, ct);
            if (exists) return Results.Conflict(new { error = $"recall set tag '{code}' already exists" });

            var now = DateTimeOffset.UtcNow;
            var row = new RecallSetTag
            {
                Code = code,
                DisplayName = dto.DisplayName.Trim(),
                ShortLabel = string.IsNullOrWhiteSpace(dto.ShortLabel) ? null : dto.ShortLabel.Trim(),
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description,
                SortOrder = dto.SortOrder ?? 100,
                IsActive = dto.IsActive ?? true,
                ExamTypeCode = string.IsNullOrWhiteSpace(dto.ExamTypeCode) ? null : dto.ExamTypeCode.Trim().ToLowerInvariant(),
                CreatedByUserId = adminId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.RecallSetTags.Add(row);
            await db.SaveChangesAsync(ct);
            return Results.Ok(Project(row));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPut("/{code}", async (
            string code,
            LearnerDbContext db,
            RecallSetTagUpdate dto,
            CancellationToken ct) =>
        {
            var row = await db.RecallSetTags.FirstOrDefaultAsync(x => x.Code == code.ToLowerInvariant(), ct);
            if (row is null) return Results.NotFound();
            if (!string.IsNullOrWhiteSpace(dto.DisplayName)) row.DisplayName = dto.DisplayName.Trim();
            if (dto.ShortLabel is not null) row.ShortLabel = string.IsNullOrWhiteSpace(dto.ShortLabel) ? null : dto.ShortLabel.Trim();
            if (dto.Description is not null) row.Description = dto.Description;
            if (dto.SortOrder.HasValue) row.SortOrder = dto.SortOrder.Value;
            if (dto.IsActive.HasValue) row.IsActive = dto.IsActive.Value;
            if (dto.ExamTypeCode is not null)
                row.ExamTypeCode = string.IsNullOrWhiteSpace(dto.ExamTypeCode) ? null : dto.ExamTypeCode.Trim().ToLowerInvariant();
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(Project(row));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/{code}/archive", async (string code, LearnerDbContext db, CancellationToken ct) =>
        {
            var row = await db.RecallSetTags.FirstOrDefaultAsync(x => x.Code == code.ToLowerInvariant(), ct);
            if (row is null) return Results.NotFound();
            row.IsActive = false;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(Project(row));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/{code}/unarchive", async (string code, LearnerDbContext db, CancellationToken ct) =>
        {
            var row = await db.RecallSetTags.FirstOrDefaultAsync(x => x.Code == code.ToLowerInvariant(), ct);
            if (row is null) return Results.NotFound();
            row.IsActive = true;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(Project(row));
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapDelete("/{code}", async (string code, LearnerDbContext db, CancellationToken ct) =>
        {
            var row = await db.RecallSetTags.FirstOrDefaultAsync(x => x.Code == code.ToLowerInvariant(), ct);
            if (row is null) return Results.NotFound();
            // Refuse to hard-delete canonical codes (Old/2023-2025/2026) — archive instead.
            if (RecallSetCodes.IsKnown(row.Code))
            {
                row.IsActive = false;
                row.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { archived = true, code = row.Code, hardDelete = false });
            }
            // For non-canonical user-created codes, check no terms reference it before hard-delete.
            var referenced = await db.VocabularyTerms.AsNoTracking()
                .Where(v => v.RecallSetCodesJson != null && v.RecallSetCodesJson.Contains(row.Code))
                .Take(1).AnyAsync(ct);
            if (referenced)
            {
                row.IsActive = false;
                row.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { archived = true, code = row.Code, hardDelete = false, reason = "in_use" });
            }
            db.RecallSetTags.Remove(row);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { archived = false, code = row.Code, hardDelete = true });
        })
        .WithAdminWrite("AdminContentWrite");

        return app;
    }

    private static bool IsValidCode(string code)
    {
        if (code.Length == 0) return false;
        foreach (var ch in code)
        {
            if (!(char.IsLower(ch) || char.IsDigit(ch) || ch == '-')) return false;
        }
        return true;
    }

    private static object Project(RecallSetTag r) => new
    {
        r.Code,
        r.DisplayName,
        r.ShortLabel,
        r.Description,
        r.SortOrder,
        r.IsActive,
        r.ExamTypeCode,
        r.CreatedByUserId,
        r.CreatedAt,
        r.UpdatedAt,
        canonical = RecallSetCodes.IsKnown(r.Code),
    };
}

public sealed record RecallSetTagCreate(
    string Code,
    string DisplayName,
    string? ShortLabel,
    string? Description,
    int? SortOrder,
    bool? IsActive,
    string? ExamTypeCode);

public sealed record RecallSetTagUpdate(
    string? DisplayName,
    string? ShortLabel,
    string? Description,
    int? SortOrder,
    bool? IsActive,
    string? ExamTypeCode);
