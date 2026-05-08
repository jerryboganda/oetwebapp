using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiTools;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin surface for the Phase 5 AI tool-calling subsystem. Tool catalog
/// is read-only (code-defined, idempotently seeded by
/// <c>AiToolCatalogSeederHostedService</c>); per-feature grants are
/// deny-by-default CRUD.
///
/// Authorisation: <c>AdminAiConfig</c>, matching <c>AiUsageAdminEndpoints</c>
/// (the same admins that configure providers and policy also configure
/// which tools each feature may call).
/// </summary>
public static class AiToolsAdminEndpoints
{
    public static IEndpointRouteBuilder MapAiToolsAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/ai-tools")
            .RequireAuthorization("AdminAiConfig")
            .RequireRateLimiting("PerUser");

        // ── Catalog (read-only) ─────────────────────────────────────────────
        group.MapGet("/tools", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var rows = await db.AiTools
                .AsNoTracking()
                .OrderBy(t => t.Code)
                .Select(t => new
                {
                    id = t.Id,
                    code = t.Code,
                    name = t.Name,
                    description = t.Description,
                    category = t.Category.ToString(),
                    jsonSchemaArgs = t.JsonSchemaArgs,
                    isActive = t.IsActive,
                    createdAt = t.CreatedAt,
                    updatedAt = t.UpdatedAt,
                })
                .ToListAsync(ct);
            return Results.Ok(new { tools = rows });
        });

        // ── Grants list (per feature) ───────────────────────────────────────
        group.MapGet("/grants", async (LearnerDbContext db, string? featureCode, CancellationToken ct) =>
        {
            var query = db.AiFeatureToolGrants.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(featureCode))
                query = query.Where(g => g.FeatureCode == featureCode);

            var rows = await (from g in query
                              join t in db.AiTools.AsNoTracking() on g.ToolCode equals t.Code into tj
                              from t in tj.DefaultIfEmpty()
                              orderby g.FeatureCode, g.ToolCode
                              select new
                              {
                                  id = g.Id,
                                  featureCode = g.FeatureCode,
                                  toolCode = g.ToolCode,
                                  toolName = t != null ? t.Name : g.ToolCode,
                                  toolCategory = t != null ? t.Category.ToString() : null,
                                  toolActive = t != null && t.IsActive,
                                  isActive = g.IsActive,
                                  createdAt = g.CreatedAt,
                                  updatedAt = g.UpdatedAt,
                                  updatedByAdminId = g.UpdatedByAdminId,
                              })
                  .ToListAsync(ct);

            return Results.Ok(new { grants = rows });
        });

        // ── Create grant ────────────────────────────────────────────────────
        group.MapPost("/grants", async (
            LearnerDbContext db,
            HttpContext http,
            IAiToolRegistry registry,
            CreateGrantDto dto,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.FeatureCode) || string.IsNullOrWhiteSpace(dto.ToolCode))
                return Results.BadRequest(new { error = "featureCode and toolCode are required" });

            if (!registry.IsKnownToolCode(dto.ToolCode))
                return Results.BadRequest(new { error = $"unknown tool code: {dto.ToolCode}" });

            // Idempotent upsert on (featureCode, toolCode) — re-activate if soft-disabled.
            var existing = await db.AiFeatureToolGrants
                .FirstOrDefaultAsync(g => g.FeatureCode == dto.FeatureCode && g.ToolCode == dto.ToolCode, ct);

            var now = DateTimeOffset.UtcNow;
            var actorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            string id;
            string action;

            if (existing is null)
            {
                var row = new AiFeatureToolGrant
                {
                    Id = Guid.NewGuid().ToString("N"),
                    FeatureCode = dto.FeatureCode.Trim(),
                    ToolCode = dto.ToolCode.Trim(),
                    IsActive = dto.IsActive ?? true,
                    CreatedAt = now,
                    UpdatedAt = now,
                    UpdatedByAdminId = actorId,
                };
                db.AiFeatureToolGrants.Add(row);
                id = row.Id;
                action = "AiToolGrantCreated";
            }
            else
            {
                existing.IsActive = dto.IsActive ?? true;
                existing.UpdatedAt = now;
                existing.UpdatedByAdminId = actorId;
                id = existing.Id;
                action = "AiToolGrantUpdated";
            }

            await SaveWithAuditAsync(db, http, action, id, $"{dto.FeatureCode}/{dto.ToolCode}", ct);
            registry.InvalidateFeature(dto.FeatureCode);

            return Results.Ok(new { id });
        }).RequireRateLimiting("PerUserWrite");

        // ── Toggle grant ────────────────────────────────────────────────────
        group.MapPatch("/grants/{id}", async (
            LearnerDbContext db,
            HttpContext http,
            IAiToolRegistry registry,
            string id,
            UpdateGrantDto dto,
            CancellationToken ct) =>
        {
            var row = await db.AiFeatureToolGrants.FirstOrDefaultAsync(g => g.Id == id, ct);
            if (row is null) return Results.NotFound();

            row.IsActive = dto.IsActive ?? row.IsActive;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedByAdminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);

            await SaveWithAuditAsync(db, http, "AiToolGrantUpdated", row.Id, $"{row.FeatureCode}/{row.ToolCode}", ct);
            registry.InvalidateFeature(row.FeatureCode);

            return Results.Ok(new { id = row.Id, isActive = row.IsActive });
        }).RequireRateLimiting("PerUserWrite");

        // ── Delete grant (hard delete — audit log preserves history) ────────
        group.MapDelete("/grants/{id}", async (
            LearnerDbContext db,
            HttpContext http,
            IAiToolRegistry registry,
            string id,
            CancellationToken ct) =>
        {
            var row = await db.AiFeatureToolGrants.FirstOrDefaultAsync(g => g.Id == id, ct);
            if (row is null) return Results.NotFound();

            var featureCode = row.FeatureCode;
            db.AiFeatureToolGrants.Remove(row);
            await SaveWithAuditAsync(db, http, "AiToolGrantDeleted", row.Id, $"{row.FeatureCode}/{row.ToolCode}", ct);
            registry.InvalidateFeature(featureCode);

            return Results.NoContent();
        }).RequireRateLimiting("PerUserWrite");

        return app;
    }

    private static async Task SaveWithAuditAsync(
        LearnerDbContext db,
        HttpContext http,
        string action,
        string resourceId,
        string? details,
        CancellationToken ct)
    {
        var actorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        var actorName = http.User.FindFirstValue(ClaimTypes.Name) ?? actorId;
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            ActorName = actorName,
            Action = action,
            ResourceType = "AiToolGrant",
            ResourceId = resourceId,
            Details = details,
        });
        await db.SaveChangesAsync(ct);
    }
}

public sealed record CreateGrantDto(string FeatureCode, string ToolCode, bool? IsActive);
public sealed record UpdateGrantDto(bool? IsActive);
