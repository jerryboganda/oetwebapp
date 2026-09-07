using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Operator surface for AI Learning Companion plan access (owner directive
/// 2026-09-07: fully admin-configurable, no catalog-manifest edit needed).
///
/// <para>
/// Companion access resolves from two durable sources: the plan's catalog
/// module list (<see cref="BillingPlan.DashboardModulesJson"/>, rewritten by
/// the catalog seeder on every boot) and <see cref="PlanModuleOverride"/>
/// rows written here (never touched by the seeder). The learner gate in
/// <see cref="CompanionLearnerEndpoints"/> consults both.
/// </para>
///
/// <para>
/// Authorisation matches <see cref="CompanionKnowledgeAdminEndpoints"/>
/// (<c>AdminAiConfig</c>).
/// </para>
/// </summary>
public static class CompanionAccessAdminEndpoints
{
    public static IEndpointRouteBuilder MapCompanionAccessAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/companion/access")
            .RequireAuthorization("AdminAiConfig")
            .RequireRateLimiting("PerUser");

        // ── Per-plan effective companion state ────────────────────────────
        group.MapGet("/", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var plans = await db.BillingPlans
                .AsNoTracking()
                .Where(p => p.Status == BillingPlanStatus.Active)
                .OrderBy(p => p.DisplayOrder)
                .ThenBy(p => p.Code)
                .Select(p => new { p.Code, p.Name, p.DashboardModulesJson })
                .ToListAsync(ct);

            var overrides = await db.PlanModuleOverrides
                .AsNoTracking()
                .Where(o => o.ModuleKey == ModuleKeys.AiCompanion)
                .Select(o => new { o.PlanCode, o.Enabled, o.UpdatedByAdminId, o.UpdatedAt })
                .ToListAsync(ct);

            var items = plans.Select(plan =>
            {
                var inManifest = EffectiveEntitlementResolver.ParseDashboardModules(plan.DashboardModulesJson)
                    .Any(m => string.Equals(m, ModuleKeys.AiCompanion, StringComparison.OrdinalIgnoreCase));
                var row = overrides.FirstOrDefault(o =>
                    string.Equals(o.PlanCode, plan.Code, StringComparison.OrdinalIgnoreCase));
                var effective = row?.Enabled ?? inManifest;
                var source = row is not null ? "override" : inManifest ? "manifest" : "none";
                return new CompanionPlanAccessItem(
                    plan.Code,
                    plan.Name,
                    effective,
                    source,
                    inManifest,
                    row?.Enabled,
                    row?.UpdatedByAdminId,
                    row?.UpdatedAt);
            }).ToList();

            return Results.Ok(new { items });
        });

        // ── Grant or revoke (upsert override) ─────────────────────────────
        group.MapPost("/", async (
            CompanionPlanAccessRequest request,
            HttpContext http,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.PlanCode))
            {
                return Results.BadRequest(new { error = "plan_code_required" });
            }

            var planCode = request.PlanCode.Trim();
            var planExists = await db.BillingPlans
                .AsNoTracking()
                .AnyAsync(p => p.Code.ToLower() == planCode.ToLower(), ct);
            if (!planExists)
            {
                return Results.NotFound(new { error = "unknown_plan", planCode });
            }

            var adminId = AdminId(http);
            var now = DateTimeOffset.UtcNow;
            var existing = await db.PlanModuleOverrides
                .FirstOrDefaultAsync(o => o.PlanCode.ToLower() == planCode.ToLower()
                    && o.ModuleKey == ModuleKeys.AiCompanion, ct);
            if (existing is null)
            {
                db.PlanModuleOverrides.Add(new PlanModuleOverride
                {
                    Id = $"pmo-{Guid.NewGuid():N}"[..32],
                    PlanCode = planCode,
                    ModuleKey = ModuleKeys.AiCompanion,
                    Enabled = request.Enabled,
                    UpdatedByAdminId = adminId,
                    UpdatedAt = now,
                });
            }
            else
            {
                existing.Enabled = request.Enabled;
                existing.UpdatedByAdminId = adminId;
                existing.UpdatedAt = now;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { planCode, enabled = request.Enabled, updatedByAdminId = adminId });
        });

        // ── Reset to manifest truth (delete override) ─────────────────────
        group.MapDelete("/{planCode}", async (
            string planCode,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var existing = await db.PlanModuleOverrides
                .FirstOrDefaultAsync(o => o.PlanCode.ToLower() == planCode.ToLower()
                    && o.ModuleKey == ModuleKeys.AiCompanion, ct);
            if (existing is null)
            {
                return Results.NotFound(new { error = "no_override", planCode });
            }

            db.PlanModuleOverrides.Remove(existing);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { planCode, reset = true });
        });

        return app;
    }

    private static string AdminId(HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated admin id is required.");
}

/// <summary>Grant (true) or revoke (false) companion access for one plan.</summary>
public sealed record CompanionPlanAccessRequest(string PlanCode, bool Enabled);

/// <summary>One plan's effective companion access and where it comes from.</summary>
public sealed record CompanionPlanAccessItem(
    string PlanCode,
    string PlanName,
    bool Effective,
    string Source,
    bool InManifest,
    bool? OverrideEnabled,
    string? UpdatedByAdminId,
    DateTimeOffset? UpdatedAt);
