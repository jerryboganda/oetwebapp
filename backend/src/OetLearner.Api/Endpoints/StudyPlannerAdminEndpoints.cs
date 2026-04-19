using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OetLearner.Api.Services.StudyPlanner;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin endpoints for the Study Planner v2 subsystem. All routes under
/// <c>/v1/admin/study-planner/*</c>. Reads require <c>AdminStudyPlannerRead</c>,
/// mutations require <c>AdminStudyPlannerWrite</c>.
/// </summary>
public static class StudyPlannerAdminEndpoints
{
    public static IEndpointRouteBuilder MapStudyPlannerAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var root = app.MapGroup("/v1/admin/study-planner")
            .RequireAuthorization("AdminStudyPlannerRead")
            .RequireRateLimiting("PerUser");

        MapTaskTemplates(root);
        MapPlanTemplates(root);
        MapRules(root);
        MapDriftPolicy(root);
        MapInsights(root);
        MapPerLearner(root);

        return app;
    }

    private static void MapTaskTemplates(RouteGroupBuilder root)
    {
        var g = root.MapGroup("/task-templates");

        g.MapGet("", async (
            IStudyPlannerAdminService svc, CancellationToken ct,
            string? subtest, string? examFamily, bool? includeArchived, string? search, int? page, int? pageSize) =>
        {
            var rows = await svc.ListTaskTemplatesAsync(
                new TaskTemplateListQuery(subtest, examFamily, includeArchived, search, page ?? 1, pageSize ?? 50),
                ct);
            return Results.Ok(rows);
        });

        g.MapGet("/{id}", async (string id, IStudyPlannerAdminService svc, CancellationToken ct) =>
        {
            var row = await svc.GetTaskTemplateAsync(id, ct);
            return row is null ? Results.NotFound() : Results.Ok(row);
        });

        g.MapPost("", async (TaskTemplateCreate dto, IStudyPlannerAdminService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try
            {
                var row = await svc.CreateTaskTemplateAsync(dto, adminId, ct);
                return Results.Created($"/v1/admin/study-planner/task-templates/{row.Id}", row);
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        })
        .RequireAuthorization("AdminStudyPlannerWrite")
        .RequireRateLimiting("PerUserWrite");

        g.MapPut("/{id}", async (string id, TaskTemplateUpdate dto, IStudyPlannerAdminService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try { var row = await svc.UpdateTaskTemplateAsync(id, dto, adminId, ct); return Results.Ok(row); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        })
        .RequireAuthorization("AdminStudyPlannerWrite")
        .RequireRateLimiting("PerUserWrite");

        g.MapDelete("/{id}", async (string id, IStudyPlannerAdminService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try { await svc.ArchiveTaskTemplateAsync(id, adminId, ct); return Results.NoContent(); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        })
        .RequireAuthorization("AdminStudyPlannerWrite")
        .RequireRateLimiting("PerUserWrite");
    }

    private static void MapPlanTemplates(RouteGroupBuilder root)
    {
        var g = root.MapGroup("/plan-templates");

        g.MapGet("", async (IStudyPlannerAdminService svc, bool? includeArchived, CancellationToken ct) =>
        {
            var rows = await svc.ListPlanTemplatesAsync(includeArchived ?? false, ct);
            return Results.Ok(rows);
        });

        g.MapGet("/{id}", async (string id, IStudyPlannerAdminService svc, CancellationToken ct) =>
        {
            var detail = await svc.GetPlanTemplateDetailAsync(id, ct);
            return detail is null ? Results.NotFound() : Results.Ok(new { template = detail.Template, items = detail.Items });
        });

        g.MapPost("", async (PlanTemplateCreate dto, IStudyPlannerAdminService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try
            {
                var row = await svc.CreatePlanTemplateAsync(dto, adminId, ct);
                return Results.Created($"/v1/admin/study-planner/plan-templates/{row.Id}", row);
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        })
        .RequireAuthorization("AdminStudyPlannerWrite")
        .RequireRateLimiting("PerUserWrite");

        g.MapPut("/{id}", async (string id, PlanTemplateUpdate dto, IStudyPlannerAdminService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try { var row = await svc.UpdatePlanTemplateAsync(id, dto, adminId, ct); return Results.Ok(row); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        })
        .RequireAuthorization("AdminStudyPlannerWrite")
        .RequireRateLimiting("PerUserWrite");

        g.MapDelete("/{id}", async (string id, IStudyPlannerAdminService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try { await svc.ArchivePlanTemplateAsync(id, adminId, ct); return Results.NoContent(); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        })
        .RequireAuthorization("AdminStudyPlannerWrite")
        .RequireRateLimiting("PerUserWrite");

        // Replace items (idempotent)
        g.MapPut("/{id}/items", async (string id, ReplaceItemsRequest body, IStudyPlannerAdminService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try
            {
                await svc.ReplacePlanTemplateItemsAsync(id, body.Items, adminId, ct);
                return Results.NoContent();
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        })
        .RequireAuthorization("AdminStudyPlannerWrite")
        .RequireRateLimiting("PerUserWrite");
    }

    private static void MapRules(RouteGroupBuilder root)
    {
        var g = root.MapGroup("/rules");

        g.MapGet("", async (IStudyPlannerAdminService svc, bool? includeInactive, CancellationToken ct) =>
        {
            var rows = await svc.ListRulesAsync(includeInactive ?? false, ct);
            return Results.Ok(rows);
        });

        g.MapGet("/{id}", async (string id, IStudyPlannerAdminService svc, CancellationToken ct) =>
        {
            var row = await svc.GetRuleAsync(id, ct);
            return row is null ? Results.NotFound() : Results.Ok(row);
        });

        g.MapPost("", async (AssignmentRuleCreate dto, IStudyPlannerAdminService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try { var row = await svc.CreateRuleAsync(dto, adminId, ct); return Results.Created($"/v1/admin/study-planner/rules/{row.Id}", row); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        })
        .RequireAuthorization("AdminStudyPlannerWrite")
        .RequireRateLimiting("PerUserWrite");

        g.MapPut("/{id}", async (string id, AssignmentRuleUpdate dto, IStudyPlannerAdminService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try { var row = await svc.UpdateRuleAsync(id, dto, adminId, ct); return Results.Ok(row); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        })
        .RequireAuthorization("AdminStudyPlannerWrite")
        .RequireRateLimiting("PerUserWrite");

        g.MapDelete("/{id}", async (string id, IStudyPlannerAdminService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try { await svc.DeleteRuleAsync(id, adminId, ct); return Results.NoContent(); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        })
        .RequireAuthorization("AdminStudyPlannerWrite")
        .RequireRateLimiting("PerUserWrite");

        // Dry-run preview: given a fake learner context, which template would match?
        g.MapPost("/preview", async (LearnerPlanContext ctx, IStudyPlannerAdminService svc, IStudyPlannerRuleEngine engine, CancellationToken ct) =>
        {
            var rules = await svc.ListRulesAsync(includeInactive: false, ct);
            var match = engine.Match(ctx, rules);
            return Results.Ok(match);
        });
    }

    private static void MapDriftPolicy(RouteGroupBuilder root)
    {
        var g = root.MapGroup("/drift-policies");

        g.MapGet("/{examFamilyCode}", async (string examFamilyCode, IStudyPlannerAdminService svc, CancellationToken ct) =>
        {
            var p = await svc.GetDriftPolicyAsync(examFamilyCode, ct);
            return Results.Ok(p);
        });

        g.MapPut("/{examFamilyCode}", async (string examFamilyCode, DriftPolicyUpdate dto, IStudyPlannerAdminService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try { var p = await svc.UpdateDriftPolicyAsync(examFamilyCode, dto, adminId, ct); return Results.Ok(p); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        })
        .RequireAuthorization("AdminStudyPlannerWrite")
        .RequireRateLimiting("PerUserWrite");
    }

    private static void MapInsights(RouteGroupBuilder root)
    {
        root.MapGet("/insights", async (OetLearner.Api.Data.LearnerDbContext db, CancellationToken ct) =>
        {
            var totalPlans = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .CountAsync(db.StudyPlans, ct);
            var totalItems = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .CountAsync(db.StudyPlanItems, ct);
            var completedItems = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .CountAsync(db.StudyPlanItems.Where(x => x.Status == OetLearner.Api.Domain.StudyPlanItemStatus.Completed), ct);
            var overdueItems = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .CountAsync(db.StudyPlanItems.Where(x =>
                    x.Status == OetLearner.Api.Domain.StudyPlanItemStatus.NotStarted
                    && x.DueDate < DateOnly.FromDateTime(DateTime.UtcNow)), ct);
            var regenLast7d = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .CountAsync(db.StudyPlanGenerationLogs.Where(x => x.CreatedAt > DateTimeOffset.UtcNow.AddDays(-7)), ct);
            return Results.Ok(new
            {
                totalPlans,
                totalItems,
                completedItems,
                overdueItems,
                completionRate = totalItems == 0 ? 0 : (int)Math.Round(100.0 * completedItems / totalItems),
                regenLast7d,
            });
        });
    }

    private static void MapPerLearner(RouteGroupBuilder root)
    {
        // Per-learner admin view + override (minimal, extensible in Phase 8 work beyond)
        root.MapGet("/users/{userId}/plan", async (
            string userId, IStudyPlannerService svc, CancellationToken ct) =>
        {
            var plan = await svc.GetOrCreatePlanAsync(userId, ct);
            var items = await svc.GetItemsAsync(userId, ct);
            return Results.Ok(new { plan, items });
        });

        root.MapPost("/users/{userId}/plan/regenerate", async (
            string userId, IStudyPlannerService svc, OetLearner.Api.Data.LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var plan = await svc.GenerateForLearnerAsync(userId, "admin_override", ct);
            db.StudyPlanAdminOverrides.Add(new OetLearner.Api.Domain.StudyPlanAdminOverride
            {
                Id = $"spao-{Guid.NewGuid():N}",
                UserId = userId,
                AdminId = adminId,
                Action = "regenerate",
                Reason = "admin forced regen",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { plan.Id, plan.Version, plan.State });
        })
        .RequireAuthorization("AdminStudyPlannerWrite")
        .RequireRateLimiting("PerUserWrite");
    }
}

public sealed record ReplaceItemsRequest(IReadOnlyList<PlanTemplateItemUpsert> Items);
