using System.Security.Claims;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin").RequireAuthorization("AdminOnly");

        // ── Content Management ──────────────────────────────

        admin.MapGet("/content", async (HttpContext http, AdminService service, CancellationToken ct,
            string? type, string? profession, string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetContentListAsync(type, profession, status, search, page ?? 1, pageSize ?? 20, ct)));

        admin.MapGet("/content/{contentId}", async (string contentId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetContentDetailAsync(contentId, ct)));

        admin.MapPost("/content", async (HttpContext http, AdminContentCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateContentAsync(http.AdminId(), http.AdminName(), request, ct)));

        admin.MapPut("/content/{contentId}", async (string contentId, HttpContext http, AdminContentUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateContentAsync(http.AdminId(), http.AdminName(), contentId, request, ct)));

        admin.MapPost("/content/{contentId}/publish", async (string contentId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.PublishContentAsync(http.AdminId(), http.AdminName(), contentId, ct)));

        admin.MapPost("/content/{contentId}/archive", async (string contentId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ArchiveContentAsync(http.AdminId(), http.AdminName(), contentId, ct)));

        admin.MapGet("/content/{contentId}/revisions", async (string contentId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetContentRevisionsAsync(contentId, ct)));

        admin.MapPost("/content/{contentId}/revisions/{revisionId}/restore", async (string contentId, string revisionId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.RestoreRevisionAsync(http.AdminId(), http.AdminName(), contentId, revisionId, ct)));

        // ── Taxonomy (Professions) ──────────────────────────

        admin.MapGet("/taxonomy", async (AdminService service, CancellationToken ct, string? type, string? status)
            => Results.Ok(await service.GetTaxonomyListAsync(type, status, ct)));

        admin.MapPost("/taxonomy", async (HttpContext http, AdminTaxonomyCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateTaxonomyNodeAsync(http.AdminId(), http.AdminName(), request, ct)));

        admin.MapPut("/taxonomy/{professionId}", async (string professionId, HttpContext http, AdminTaxonomyUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateTaxonomyNodeAsync(http.AdminId(), http.AdminName(), professionId, request, ct)));

        admin.MapPost("/taxonomy/{professionId}/archive", async (string professionId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ArchiveTaxonomyNodeAsync(http.AdminId(), http.AdminName(), professionId, ct)));

        // ── Criteria / Rubric ──────────────────────────────

        admin.MapGet("/criteria", async (AdminService service, CancellationToken ct, string? subtest, string? status)
            => Results.Ok(await service.GetCriteriaListAsync(subtest, status, ct)));

        admin.MapPost("/criteria", async (HttpContext http, AdminCriterionCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateCriterionAsync(http.AdminId(), http.AdminName(), request, ct)));

        admin.MapPut("/criteria/{criterionId}", async (string criterionId, HttpContext http, AdminCriterionUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateCriterionAsync(http.AdminId(), http.AdminName(), criterionId, request, ct)));

        // ── AI Evaluation Config ─────────────────────────────

        admin.MapGet("/ai-config", async (AdminService service, CancellationToken ct, string? status)
            => Results.Ok(await service.GetAIConfigListAsync(status, ct)));

        admin.MapPost("/ai-config", async (HttpContext http, AdminAIConfigCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateAIConfigAsync(http.AdminId(), http.AdminName(), request, ct)));

        admin.MapPut("/ai-config/{configId}", async (string configId, HttpContext http, AdminAIConfigUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateAIConfigAsync(http.AdminId(), http.AdminName(), configId, request, ct)));

        // ── Feature Flags ───────────────────────────────────

        admin.MapGet("/flags", async (AdminService service, CancellationToken ct, string? type)
            => Results.Ok(await service.GetFlagListAsync(type, ct)));

        admin.MapPost("/flags", async (HttpContext http, AdminFlagCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateFlagAsync(http.AdminId(), http.AdminName(), request, ct)));

        admin.MapPut("/flags/{flagId}", async (string flagId, HttpContext http, AdminFlagUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateFlagAsync(http.AdminId(), http.AdminName(), flagId, request, ct)));

        // ── Audit Logs ──────────────────────────────────────

        admin.MapGet("/audit-logs", async (AdminService service, CancellationToken ct,
            string? action, string? actor, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetAuditEventsAsync(action, actor, search, page ?? 1, pageSize ?? 20, ct)));

        // ── User Ops ────────────────────────────────────────

        admin.MapGet("/users", async (AdminService service, CancellationToken ct,
            string? role, string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetUserListAsync(role, status, search, page ?? 1, pageSize ?? 20, ct)));

        admin.MapGet("/users/{userId}", async (string userId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetUserDetailAsync(userId, ct)));

        admin.MapPut("/users/{userId}/status", async (string userId, HttpContext http, AdminUserStatusRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateUserStatusAsync(http.AdminId(), http.AdminName(), userId, request, ct)));

        admin.MapPost("/users/{userId}/credits", async (string userId, HttpContext http, AdminUserCreditsRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.AdjustUserCreditsAsync(http.AdminId(), http.AdminName(), userId, request, ct)));

        // ── Billing ─────────────────────────────────────────

        admin.MapGet("/billing/plans", async (AdminService service, CancellationToken ct, string? status)
            => Results.Ok(await service.GetBillingPlansAsync(status, ct)));

        admin.MapPost("/billing/plans", async (HttpContext http, AdminBillingPlanCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateBillingPlanAsync(http.AdminId(), http.AdminName(), request, ct)));

        admin.MapGet("/billing/invoices", async (AdminService service, CancellationToken ct,
            string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetBillingInvoicesAsync(status, search, page ?? 1, pageSize ?? 20, ct)));

        // ── Review Ops ──────────────────────────────────────

        admin.MapGet("/review-ops/summary", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetReviewOpsSummaryAsync(ct)));

        admin.MapGet("/review-ops/queue", async (AdminService service, CancellationToken ct, string? status, string? priority)
            => Results.Ok(await service.GetReviewOpsQueueAsync(status, priority, ct)));

        admin.MapPost("/review-ops/{reviewRequestId}/assign", async (string reviewRequestId, HttpContext http, AdminReviewAssignRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.AssignReviewAsync(http.AdminId(), http.AdminName(), reviewRequestId, request, ct)));

        // ── Quality Analytics ───────────────────────────────

        admin.MapGet("/quality-analytics", async (AdminService service, CancellationToken ct, string? timeRange, string? subtest)
            => Results.Ok(await service.GetQualityAnalyticsAsync(timeRange, subtest, ct)));

        return app;
    }

    private static string AdminId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue("user_id")
           ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated admin id is required.");

    private static string AdminName(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.Name)
           ?? httpContext.User.FindFirstValue("name")
           ?? "Admin";
}
