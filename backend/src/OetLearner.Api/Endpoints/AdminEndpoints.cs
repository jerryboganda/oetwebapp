using System.Security.Claims;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        admin.MapGet("/dashboard", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetDashboardSummaryAsync(ct)));
        admin.MapGet("/freeze/overview", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetFreezeOverviewAsync(ct)));
        admin.MapPut("/freeze/policy", async (HttpContext http, FreezePolicyRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateFreezePolicyAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite");
        admin.MapPost("/freeze/manual", async (HttpContext http, FreezeManualCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateManualFreezeAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite");
        admin.MapPost("/freeze/{freezeId}/approve", async (string freezeId, HttpContext http, FreezeActionRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ApproveFreezeAsync(http.AdminId(), http.AdminName(), freezeId, request, ct)))
            .RequireRateLimiting("PerUserWrite");
        admin.MapPost("/freeze/{freezeId}/reject", async (string freezeId, HttpContext http, FreezeActionRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.RejectFreezeAsync(http.AdminId(), http.AdminName(), freezeId, request, ct)))
            .RequireRateLimiting("PerUserWrite");
        admin.MapPost("/freeze/{freezeId}/end", async (string freezeId, HttpContext http, FreezeActionRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.EndFreezeAsync(http.AdminId(), http.AdminName(), freezeId, request, ct)))
            .RequireRateLimiting("PerUserWrite");
        admin.MapPost("/freeze/{freezeId}/force-end", async (string freezeId, HttpContext http, FreezeActionRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ForceEndFreezeAsync(http.AdminId(), http.AdminName(), freezeId, request, ct)))
            .RequireRateLimiting("PerUserWrite");

        // ── Content Management ──────────────────────────────

        admin.MapGet("/content", async (HttpContext http, AdminService service, CancellationToken ct,
            string? type, string? profession, string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetContentListAsync(type, profession, status, search, page ?? 1, pageSize ?? 20, ct)));

        admin.MapGet("/content/{contentId}", async (string contentId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetContentDetailAsync(contentId, ct)));

        admin.MapPost("/content", async (HttpContext http, AdminContentCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateContentAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPut("/content/{contentId}", async (string contentId, HttpContext http, AdminContentUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateContentAsync(http.AdminId(), http.AdminName(), contentId, request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPost("/content/{contentId}/publish", async (string contentId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.PublishContentAsync(http.AdminId(), http.AdminName(), contentId, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPost("/content/{contentId}/archive", async (string contentId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ArchiveContentAsync(http.AdminId(), http.AdminName(), contentId, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPost("/content/bulk-action", async (HttpContext http, AdminBulkActionRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.BulkActionContentAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapGet("/content/{contentId}/impact", async (string contentId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetContentImpactSummaryAsync(contentId, ct)));

        admin.MapGet("/content/{contentId}/revisions", async (string contentId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetContentRevisionsAsync(contentId, ct)));

        admin.MapPost("/content/{contentId}/revisions/{revisionId}/restore", async (string contentId, string revisionId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.RestoreRevisionAsync(http.AdminId(), http.AdminName(), contentId, revisionId, ct)))
            .RequireRateLimiting("PerUserWrite");

        // ── AI Content Generation ─────────────────────────

        admin.MapPost("/content/generate", async (HttpContext http, ContentGenerationRequest request, ContentGenerationService service, CancellationToken ct)
            => Results.Ok(await service.QueueGenerationAsync(http.AdminId(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapGet("/content/generation-jobs", async (ContentGenerationService service, CancellationToken ct, int? page, int? pageSize)
            => Results.Ok(await service.GetJobsAsync(page ?? 1, pageSize ?? 20, ct)));

        admin.MapGet("/content/generation-jobs/{jobId}", async (string jobId, ContentGenerationService service, CancellationToken ct)
            => Results.Ok(await service.GetJobDetailAsync(jobId, ct)));

        // ── Taxonomy (Professions) ──────────────────────────

        admin.MapGet("/taxonomy", async (AdminService service, CancellationToken ct, string? type, string? status)
            => Results.Ok(await service.GetTaxonomyListAsync(type, status, ct)));

        admin.MapPost("/taxonomy", async (HttpContext http, AdminTaxonomyCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateTaxonomyNodeAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPut("/taxonomy/{professionId}", async (string professionId, HttpContext http, AdminTaxonomyUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateTaxonomyNodeAsync(http.AdminId(), http.AdminName(), professionId, request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPost("/taxonomy/{professionId}/archive", async (string professionId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ArchiveTaxonomyNodeAsync(http.AdminId(), http.AdminName(), professionId, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapGet("/taxonomy/{professionId}/impact", async (string professionId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetTaxonomyImpactSummaryAsync(professionId, ct)));

        // ── Criteria / Rubric ──────────────────────────────

        admin.MapGet("/criteria", async (AdminService service, CancellationToken ct, string? subtest, string? status)
            => Results.Ok(await service.GetCriteriaListAsync(subtest, status, ct)));

        admin.MapPost("/criteria", async (HttpContext http, AdminCriterionCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateCriterionAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPut("/criteria/{criterionId}", async (string criterionId, HttpContext http, AdminCriterionUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateCriterionAsync(http.AdminId(), http.AdminName(), criterionId, request, ct)))
            .RequireRateLimiting("PerUserWrite");

        // ── AI Evaluation Config ─────────────────────────────

        admin.MapGet("/ai-config", async (AdminService service, CancellationToken ct, string? status)
            => Results.Ok(await service.GetAIConfigListAsync(status, ct)));

        admin.MapPost("/ai-config", async (HttpContext http, AdminAIConfigCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateAIConfigAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPut("/ai-config/{configId}", async (string configId, HttpContext http, AdminAIConfigUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateAIConfigAsync(http.AdminId(), http.AdminName(), configId, request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPost("/ai-config/{configId}/activate", async (string configId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ActivateAIConfigAsync(http.AdminId(), http.AdminName(), configId, ct)))
            .RequireRateLimiting("PerUserWrite");

        // ── Feature Flags ───────────────────────────────────

        admin.MapGet("/flags", async (AdminService service, CancellationToken ct, string? type)
            => Results.Ok(await service.GetFlagListAsync(type, ct)));

        admin.MapPost("/flags", async (HttpContext http, AdminFlagCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateFlagAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPut("/flags/{flagId}", async (string flagId, HttpContext http, AdminFlagUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateFlagAsync(http.AdminId(), http.AdminName(), flagId, request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPost("/flags/{flagId}/activate", async (string flagId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ActivateFlagAsync(http.AdminId(), http.AdminName(), flagId, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPost("/flags/{flagId}/deactivate", async (string flagId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.DeactivateFlagAsync(http.AdminId(), http.AdminName(), flagId, ct)))
            .RequireRateLimiting("PerUserWrite");

        // ── Audit Logs ──────────────────────────────────────

        admin.MapGet("/audit-logs", async (AdminService service, CancellationToken ct,
            string? action, string? actor, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetAuditEventsAsync(action, actor, search, page ?? 1, pageSize ?? 20, ct)));

        admin.MapGet("/audit-logs/{eventId}", async (string eventId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetAuditEventDetailAsync(eventId, ct)));

        admin.MapGet("/audit-logs/export", async (AdminService service, CancellationToken ct,
            string? action, string? actor, string? search)
            =>
            {
                var export = await service.ExportAuditEventsCsvAsync(action, actor, search, ct);
                return Results.File(export.Bytes, "text/csv; charset=utf-8", export.FileName);
            });

        // ── User Ops ────────────────────────────────────────

        admin.MapGet("/users", async (AdminService service, CancellationToken ct,
            string? role, string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetUserListAsync(role, status, search, page ?? 1, pageSize ?? 20, ct)));

        admin.MapGet("/users/{userId}", async (string userId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetUserDetailAsync(userId, ct)));

        admin.MapPost("/users/invite", async (HttpContext http, AdminUserInviteRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.InviteUserAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPut("/users/{userId}/status", async (string userId, HttpContext http, AdminUserStatusRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateUserStatusAsync(http.AdminId(), http.AdminName(), userId, request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPost("/users/{userId}/delete", async (string userId, HttpContext http, AdminUserLifecycleRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.DeleteUserAsync(http.AdminId(), http.AdminName(), userId, request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPost("/users/{userId}/restore", async (string userId, HttpContext http, AdminUserLifecycleRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.RestoreUserAsync(http.AdminId(), http.AdminName(), userId, request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPost("/users/{userId}/credits", async (string userId, HttpContext http, AdminUserCreditsRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.AdjustUserCreditsAsync(http.AdminId(), http.AdminName(), userId, request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPost("/users/{userId}/password-reset", async (string userId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.TriggerUserPasswordResetAsync(http.AdminId(), http.AdminName(), userId, ct)))
            .RequireRateLimiting("PerUserWrite");

        // ── Billing ─────────────────────────────────────────

        admin.MapGet("/billing/plans", async (AdminService service, CancellationToken ct, string? status)
            => Results.Ok(await service.GetBillingPlansAsync(status, ct)));

        admin.MapPost("/billing/plans", async (HttpContext http, AdminBillingPlanCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateBillingPlanAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPut("/billing/plans/{planId}", async (string planId, HttpContext http, AdminBillingPlanUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateBillingPlanAsync(http.AdminId(), http.AdminName(), planId, request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapGet("/billing/add-ons", async (AdminService service, CancellationToken ct, string? status)
            => Results.Ok(await service.GetBillingAddOnsAsync(status, ct)));

        admin.MapPost("/billing/add-ons", async (HttpContext http, AdminBillingAddOnCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateBillingAddOnAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPut("/billing/add-ons/{addOnId}", async (string addOnId, HttpContext http, AdminBillingAddOnUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateBillingAddOnAsync(http.AdminId(), http.AdminName(), addOnId, request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapGet("/billing/coupons", async (AdminService service, CancellationToken ct, string? status)
            => Results.Ok(await service.GetBillingCouponsAsync(status, ct)));

        admin.MapPost("/billing/coupons", async (HttpContext http, AdminBillingCouponCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateBillingCouponAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPut("/billing/coupons/{couponId}", async (string couponId, HttpContext http, AdminBillingCouponUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateBillingCouponAsync(http.AdminId(), http.AdminName(), couponId, request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapGet("/billing/subscriptions", async (AdminService service, CancellationToken ct, string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetBillingSubscriptionsAsync(status, search, page ?? 1, pageSize ?? 20, ct)));

        admin.MapGet("/billing/redemptions", async (AdminService service, CancellationToken ct, string? couponCode, string? userId, int? page, int? pageSize)
            => Results.Ok(await service.GetBillingCouponRedemptionsAsync(couponCode, userId, page ?? 1, pageSize ?? 20, ct)));

        admin.MapGet("/billing/invoices", async (AdminService service, CancellationToken ct,
            string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetBillingInvoicesAsync(status, search, page ?? 1, pageSize ?? 20, ct)));

        // ── Review Ops ──────────────────────────────────────

        admin.MapGet("/review-ops/summary", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetReviewOpsSummaryAsync(ct)));

        admin.MapGet("/review-ops/queue", async (AdminService service, CancellationToken ct, string? status, string? priority)
            => Results.Ok(await service.GetReviewOpsQueueAsync(status, priority, ct)));

        admin.MapPost("/review-ops/{reviewRequestId}/assign", async (string reviewRequestId, HttpContext http, AdminReviewAssignRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.AssignReviewAsync(http.AdminId(), http.AdminName(), reviewRequestId, request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPost("/review-ops/{reviewRequestId}/cancel", async (string reviewRequestId, HttpContext http, AdminReviewCancelRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CancelReviewAsync(http.AdminId(), http.AdminName(), reviewRequestId, request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPost("/review-ops/{reviewRequestId}/reopen", async (string reviewRequestId, HttpContext http, AdminReviewReopenRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ReopenReviewAsync(http.AdminId(), http.AdminName(), reviewRequestId, request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapGet("/review-ops/failures", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetReviewFailuresAsync(ct)));

        // ── Quality Analytics ───────────────────────────────

        admin.MapGet("/quality-analytics", async (AdminService service, CancellationToken ct, string? timeRange, string? subtest, string? profession)
            => Results.Ok(await service.GetQualityAnalyticsAsync(timeRange, subtest, profession, ct)));

        return app;
    }

    private static string AdminId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated admin id is required.");

    private static string AdminName(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Admin";
}
