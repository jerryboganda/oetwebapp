using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        // ── Dashboard (any admin) ──────────────────────────────
        admin.MapGet("/dashboard", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetDashboardSummaryAsync(ct)));

        // ── Freeze Management (content:publish) ─────────────────
        admin.MapGet("/freeze/overview", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetFreezeOverviewAsync(ct)))
            .RequireAuthorization("AdminContentPublish");
        admin.MapPut("/freeze/policy", async (HttpContext http, FreezePolicyRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateFreezePolicyAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentPublish");
        admin.MapPost("/freeze/manual", async (HttpContext http, FreezeManualCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateManualFreezeAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentPublish");
        admin.MapPost("/freeze/{freezeId}/approve", async (string freezeId, HttpContext http, FreezeActionRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ApproveFreezeAsync(http.AdminId(), http.AdminName(), freezeId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentPublish");
        admin.MapPost("/freeze/{freezeId}/reject", async (string freezeId, HttpContext http, FreezeActionRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.RejectFreezeAsync(http.AdminId(), http.AdminName(), freezeId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentPublish");
        admin.MapPost("/freeze/{freezeId}/end", async (string freezeId, HttpContext http, FreezeActionRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.EndFreezeAsync(http.AdminId(), http.AdminName(), freezeId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentPublish");
        admin.MapPost("/freeze/{freezeId}/force-end", async (string freezeId, HttpContext http, FreezeActionRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ForceEndFreezeAsync(http.AdminId(), http.AdminName(), freezeId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentPublish");

        // ── Content Management ──────────────────────────────

        admin.MapGet("/content", async (HttpContext http, AdminService service, CancellationToken ct,
            string? type, string? profession, string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetContentListAsync(type, profession, status, search, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapGet("/content/{contentId}", async (string contentId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetContentDetailAsync(contentId, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapPost("/content", async (HttpContext http, AdminContentCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateContentAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapPut("/content/{contentId}", async (string contentId, HttpContext http, AdminContentUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateContentAsync(http.AdminId(), http.AdminName(), contentId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapPost("/content/{contentId}/publish", async (string contentId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.PublishContentAsync(http.AdminId(), http.AdminName(), contentId, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentPublish");

        admin.MapPost("/content/{contentId}/archive", async (string contentId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ArchiveContentAsync(http.AdminId(), http.AdminName(), contentId, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentPublish");

        admin.MapPost("/content/bulk-action", async (HttpContext http, AdminBulkActionRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.BulkActionContentAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapGet("/content/{contentId}/impact", async (string contentId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetContentImpactSummaryAsync(contentId, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapGet("/content/{contentId}/revisions", async (string contentId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetContentRevisionsAsync(contentId, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapPost("/content/{contentId}/revisions/{revisionId}/restore", async (string contentId, string revisionId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.RestoreRevisionAsync(http.AdminId(), http.AdminName(), contentId, revisionId, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        // ── AI Content Generation ─────────────────────────

        admin.MapPost("/content/generate", async (HttpContext http, ContentGenerationRequest request, ContentGenerationService service, CancellationToken ct)
            => Results.Ok(await service.QueueGenerationAsync(http.AdminId(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapGet("/content/generation-jobs", async (ContentGenerationService service, CancellationToken ct, int? page, int? pageSize)
            => Results.Ok(await service.GetJobsAsync(page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapGet("/content/generation-jobs/{jobId}", async (string jobId, ContentGenerationService service, CancellationToken ct)
            => Results.Ok(await service.GetJobDetailAsync(jobId, ct)))
            .RequireAuthorization("AdminContentRead");

        // ── Taxonomy (Professions) ──────────────────────────

        admin.MapGet("/taxonomy", async (AdminService service, CancellationToken ct, string? type, string? status)
            => Results.Ok(await service.GetTaxonomyListAsync(type, status, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapPost("/taxonomy", async (HttpContext http, AdminTaxonomyCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateTaxonomyNodeAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapPut("/taxonomy/{professionId}", async (string professionId, HttpContext http, AdminTaxonomyUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateTaxonomyNodeAsync(http.AdminId(), http.AdminName(), professionId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapPost("/taxonomy/{professionId}/archive", async (string professionId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ArchiveTaxonomyNodeAsync(http.AdminId(), http.AdminName(), professionId, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapGet("/taxonomy/{professionId}/impact", async (string professionId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetTaxonomyImpactSummaryAsync(professionId, ct)))
            .RequireAuthorization("AdminContentRead");

        // ── Criteria / Rubric ──────────────────────────────

        admin.MapGet("/criteria", async (AdminService service, CancellationToken ct, string? subtest, string? status)
            => Results.Ok(await service.GetCriteriaListAsync(subtest, status, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapPost("/criteria", async (HttpContext http, AdminCriterionCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateCriterionAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapPut("/criteria/{criterionId}", async (string criterionId, HttpContext http, AdminCriterionUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateCriterionAsync(http.AdminId(), http.AdminName(), criterionId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        // ── AI Evaluation Config ─────────────────────────────

        admin.MapGet("/ai-config", async (AdminService service, CancellationToken ct, string? status)
            => Results.Ok(await service.GetAIConfigListAsync(status, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapPost("/ai-config", async (HttpContext http, AdminAIConfigCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateAIConfigAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapPut("/ai-config/{configId}", async (string configId, HttpContext http, AdminAIConfigUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateAIConfigAsync(http.AdminId(), http.AdminName(), configId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapPost("/ai-config/{configId}/activate", async (string configId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ActivateAIConfigAsync(http.AdminId(), http.AdminName(), configId, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        // ── Feature Flags ───────────────────────────────────

        admin.MapGet("/flags", async (AdminService service, CancellationToken ct, string? type)
            => Results.Ok(await service.GetFlagListAsync(type, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapPost("/flags", async (HttpContext http, AdminFlagCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateFlagAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        admin.MapPut("/flags/{flagId}", async (string flagId, HttpContext http, AdminFlagUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateFlagAsync(http.AdminId(), http.AdminName(), flagId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        admin.MapPost("/flags/{flagId}/activate", async (string flagId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ActivateFlagAsync(http.AdminId(), http.AdminName(), flagId, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        admin.MapPost("/flags/{flagId}/deactivate", async (string flagId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.DeactivateFlagAsync(http.AdminId(), http.AdminName(), flagId, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        // ── Audit Logs ──────────────────────────────────────

        admin.MapGet("/audit-logs", async (AdminService service, CancellationToken ct,
            string? action, string? actor, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetAuditEventsAsync(action, actor, search, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminSystemAdmin");

        admin.MapGet("/audit-logs/{eventId}", async (string eventId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetAuditEventDetailAsync(eventId, ct)))
            .RequireAuthorization("AdminSystemAdmin");

        admin.MapGet("/audit-logs/export", async (AdminService service, CancellationToken ct,
            string? action, string? actor, string? search)
            =>
            {
                var export = await service.ExportAuditEventsCsvAsync(action, actor, search, ct);
                return Results.File(export.Bytes, "text/csv; charset=utf-8", export.FileName);
            })
            .RequireAuthorization("AdminSystemAdmin");

        // ── User Ops ────────────────────────────────────────

        admin.MapGet("/users", async (AdminService service, CancellationToken ct,
            string? role, string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetUserListAsync(role, status, search, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminUsersRead");

        admin.MapGet("/users/{userId}", async (string userId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetUserDetailAsync(userId, ct)))
            .RequireAuthorization("AdminUsersRead");

        admin.MapPost("/users/invite", async (HttpContext http, AdminUserInviteRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.InviteUserAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminUsersWrite");

        admin.MapPost("/users/import", async (HttpContext http, IFormFile file, AdminService service, CancellationToken ct)
            => Results.Ok(await service.BulkImportUsersAsync(http.AdminId(), http.AdminName(), file, ct)))
            .RequireRateLimiting("PerUserWrite")
            .DisableAntiforgery().RequireAuthorization("AdminUsersWrite");

        admin.MapPut("/users/{userId}/status", async (string userId, HttpContext http, AdminUserStatusRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateUserStatusAsync(http.AdminId(), http.AdminName(), userId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminUsersWrite");

        admin.MapPost("/users/{userId}/delete", async (string userId, HttpContext http, AdminUserLifecycleRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.DeleteUserAsync(http.AdminId(), http.AdminName(), userId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminUsersWrite");

        admin.MapPost("/users/{userId}/restore", async (string userId, HttpContext http, AdminUserLifecycleRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.RestoreUserAsync(http.AdminId(), http.AdminName(), userId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminUsersWrite");

        admin.MapPost("/users/{userId}/credits", async (string userId, HttpContext http, AdminUserCreditsRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.AdjustUserCreditsAsync(http.AdminId(), http.AdminName(), userId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminUsersWrite");

        admin.MapPost("/users/{userId}/password-reset", async (string userId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.TriggerUserPasswordResetAsync(http.AdminId(), http.AdminName(), userId, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminUsersWrite");

        // ── Billing ─────────────────────────────────────────

        admin.MapGet("/billing/plans", async (AdminService service, CancellationToken ct, string? status)
            => Results.Ok(await service.GetBillingPlansAsync(status, ct)))
            .RequireAuthorization("AdminBillingRead");

        admin.MapPost("/billing/plans", async (HttpContext http, AdminBillingPlanCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateBillingPlanAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminBillingWrite");

        admin.MapPut("/billing/plans/{planId}", async (string planId, HttpContext http, AdminBillingPlanUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateBillingPlanAsync(http.AdminId(), http.AdminName(), planId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminBillingWrite");

        admin.MapGet("/billing/add-ons", async (AdminService service, CancellationToken ct, string? status)
            => Results.Ok(await service.GetBillingAddOnsAsync(status, ct)))
            .RequireAuthorization("AdminBillingRead");

        admin.MapPost("/billing/add-ons", async (HttpContext http, AdminBillingAddOnCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateBillingAddOnAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminBillingWrite");

        admin.MapPut("/billing/add-ons/{addOnId}", async (string addOnId, HttpContext http, AdminBillingAddOnUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateBillingAddOnAsync(http.AdminId(), http.AdminName(), addOnId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminBillingWrite");

        admin.MapGet("/billing/coupons", async (AdminService service, CancellationToken ct, string? status)
            => Results.Ok(await service.GetBillingCouponsAsync(status, ct)))
            .RequireAuthorization("AdminBillingRead");

        admin.MapPost("/billing/coupons", async (HttpContext http, AdminBillingCouponCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateBillingCouponAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminBillingWrite");

        admin.MapPut("/billing/coupons/{couponId}", async (string couponId, HttpContext http, AdminBillingCouponUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateBillingCouponAsync(http.AdminId(), http.AdminName(), couponId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminBillingWrite");

        admin.MapGet("/billing/subscriptions", async (AdminService service, CancellationToken ct, string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetBillingSubscriptionsAsync(status, search, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminBillingRead");

        admin.MapGet("/billing/redemptions", async (AdminService service, CancellationToken ct, string? couponCode, string? userId, int? page, int? pageSize)
            => Results.Ok(await service.GetBillingCouponRedemptionsAsync(couponCode, userId, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminBillingRead");

        admin.MapGet("/billing/invoices", async (AdminService service, CancellationToken ct,
            string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetBillingInvoicesAsync(status, search, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminBillingRead");

        // ── Review Ops ──────────────────────────────────────

        admin.MapGet("/review-ops/summary", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetReviewOpsSummaryAsync(ct)))
            .RequireAuthorization("AdminReviewOps");

        admin.MapGet("/review-ops/queue", async (AdminService service, CancellationToken ct, string? status, string? priority)
            => Results.Ok(await service.GetReviewOpsQueueAsync(status, priority, ct)))
            .RequireAuthorization("AdminReviewOps");

        admin.MapPost("/review-ops/{reviewRequestId}/assign", async (string reviewRequestId, HttpContext http, AdminReviewAssignRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.AssignReviewAsync(http.AdminId(), http.AdminName(), reviewRequestId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminReviewOps");

        admin.MapPost("/review-ops/{reviewRequestId}/cancel", async (string reviewRequestId, HttpContext http, AdminReviewCancelRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CancelReviewAsync(http.AdminId(), http.AdminName(), reviewRequestId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminReviewOps");

        admin.MapPost("/review-ops/{reviewRequestId}/reopen", async (string reviewRequestId, HttpContext http, AdminReviewReopenRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ReopenReviewAsync(http.AdminId(), http.AdminName(), reviewRequestId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminReviewOps");

        admin.MapGet("/review-ops/failures", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetReviewFailuresAsync(ct)))
            .RequireAuthorization("AdminReviewOps");

        // ── Quality Analytics ───────────────────────────────

        admin.MapGet("/quality-analytics", async (AdminService service, CancellationToken ct, string? timeRange, string? subtest, string? profession)
            => Results.Ok(await service.GetQualityAnalyticsAsync(timeRange, subtest, profession, ct)))
            .RequireAuthorization("AdminContentRead");

        // ── Admin Permissions (RBAC) ────────────────────────

        admin.MapGet("/permissions", (AdminService service)
            => Results.Ok(service.GetAllPermissions()))
            .RequireAuthorization("AdminSystemAdmin");

        admin.MapGet("/permissions/{userId}", async (string userId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetAdminPermissionsAsync(userId, ct)))
            .RequireAuthorization("AdminSystemAdmin");

        admin.MapPut("/permissions/{userId}", async (string userId, HttpContext http, AdminPermissionUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateAdminPermissionsAsync(http.AdminId(), http.AdminName(), userId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        // ── Permission Templates ────────────────────────────

        admin.MapGet("/permission-templates", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetPermissionTemplatesAsync(ct)))
            .RequireAuthorization("AdminSystemAdmin");

        admin.MapPost("/permission-templates", async (HttpContext http, CreatePermissionTemplateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreatePermissionTemplateAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        admin.MapDelete("/permission-templates/{templateId}", async (string templateId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.DeletePermissionTemplateAsync(http.AdminId(), http.AdminName(), templateId, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        admin.MapPost("/users/{userId}/apply-template/{templateId}", async (string userId, string templateId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ApplyPermissionTemplateAsync(http.AdminId(), http.AdminName(), userId, templateId, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        // ── Content Publishing Workflow ─────────────────────

        admin.MapPost("/content/{contentId}/request-publish", async (string contentId, HttpContext http, AdminPublishRequestPayload request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.RequestContentPublishAsync(http.AdminId(), http.AdminName(), contentId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapPost("/content/{contentId}/submit-for-review", async (string contentId, HttpContext http, AdminPublishRequestPayload request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.SubmitContentForReviewAsync(http.AdminId(), http.AdminName(), contentId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapPost("/content/{contentId}/editor-approve", async (string contentId, HttpContext http, AdminEditorReviewPayload request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.EditorApproveContentAsync(http.AdminId(), http.AdminName(), contentId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentEditorReview");

        admin.MapPost("/content/{contentId}/editor-reject", async (string contentId, HttpContext http, AdminEditorRejectPayload request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.EditorRejectContentAsync(http.AdminId(), http.AdminName(), contentId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentEditorReview");

        admin.MapPost("/content/{contentId}/publisher-approve", async (string contentId, HttpContext http, AdminPublisherApprovePayload request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.PublisherApproveContentAsync(http.AdminId(), http.AdminName(), contentId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentPublisherApproval");

        admin.MapPost("/content/{contentId}/publisher-reject", async (string contentId, HttpContext http, AdminPublisherRejectPayload request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.PublisherRejectContentAsync(http.AdminId(), http.AdminName(), contentId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentPublisherApproval");

        admin.MapGet("/content/pending-review", async (AdminService service, CancellationToken ct, string? stage, int? page, int? pageSize)
            => Results.Ok(await service.GetPendingReviewContentAsync(stage, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapGet("/publish-requests", async (AdminService service, CancellationToken ct, string? status, int? page, int? pageSize)
            => Results.Ok(await service.GetPublishRequestsAsync(status, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapPost("/publish-requests/{requestId}/approve", async (string requestId, HttpContext http, AdminPublishReviewPayload request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ApprovePublishRequestAsync(http.AdminId(), http.AdminName(), requestId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentPublish");

        admin.MapPost("/publish-requests/{requestId}/reject", async (string requestId, HttpContext http, AdminPublishReviewPayload request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.RejectPublishRequestAsync(http.AdminId(), http.AdminName(), requestId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentPublish");

        // ── Webhook Monitoring ──────────────────────────────

        admin.MapGet("/webhooks", async (AdminService service, CancellationToken ct,
            string? gateway, string? status, int? page, int? pageSize)
            => Results.Ok(await service.GetWebhookEventsAsync(gateway, status, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminSystemAdmin");

        admin.MapGet("/webhooks/summary", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetWebhookSummaryAsync(ct)))
            .RequireAuthorization("AdminSystemAdmin");

        admin.MapPost("/webhooks/{eventId}/retry", async (string eventId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.RetryWebhookAsync(http.AdminId(), http.AdminName(), eventId, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        // ── Review Escalation (Disagreement Resolution) ─────

        admin.MapGet("/escalations", async (AdminService service, CancellationToken ct,
            string? status, int? page, int? pageSize)
            => Results.Ok(await service.GetReviewEscalationsAsync(status, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminReviewOps");

        admin.MapPost("/escalations/{escalationId}/assign", async (string escalationId, HttpContext http,
            AdminEscalationAssignRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.AssignEscalationReviewerAsync(http.AdminId(), http.AdminName(), escalationId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminReviewOps");

        admin.MapPost("/escalations/{escalationId}/resolve", async (string escalationId, HttpContext http,
            AdminEscalationResolveRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ResolveEscalationAsync(http.AdminId(), http.AdminName(), escalationId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminReviewOps");

        // ── Score Guarantee Claims ──────────────────────

        admin.MapGet("/score-guarantee-claims", async (AdminService service, CancellationToken ct,
            string? status, int? page, int? pageSize)
            => Results.Ok(await service.GetScoreGuaranteeClaimsAsync(status, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminBillingRead");

        admin.MapPost("/score-guarantee-claims/{pledgeId}/review", async (string pledgeId, HttpContext http,
            AdminScoreGuaranteeReviewRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ReviewScoreGuaranteeClaimAsync(http.AdminId(), http.AdminName(), pledgeId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminBillingWrite");

        // ── A4: Content Quality Scoring ─────────────────

        admin.MapGet("/content-quality", async (AdminService service, CancellationToken ct,
            int? page, int? pageSize)
            => Results.Ok(await service.GetContentQualityOverviewAsync(page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapPost("/content-quality/{contentId}/score", async (string contentId, HttpContext http,
            AdminService service, CancellationToken ct)
            => Results.Ok(await service.ScoreContentQualityAsync(http.AdminId(), http.AdminName(), contentId, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        // ── A6: Bulk Learner Operations ─────────────────

        admin.MapPost("/bulk/credits", async (HttpContext http,
            AdminBulkCreditRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.BulkCreditAdjustmentAsync(http.AdminId(), http.AdminName(), request.UserIds, request.CreditAmount, request.Reason, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminUsersWrite");

        admin.MapPost("/bulk/notifications", async (HttpContext http,
            AdminBulkNotificationRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.BulkNotificationAsync(http.AdminId(), http.AdminName(), request.UserIds, request.Title, request.Message, request.Category, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminUsersWrite");

        admin.MapPost("/bulk/status", async (HttpContext http,
            AdminBulkStatusRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.BulkStatusChangeAsync(http.AdminId(), http.AdminName(), request.UserIds, request.NewStatus, request.Reason, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminUsersWrite");

        // ── B3: Enterprise / Sponsor Channel ────────────

        admin.MapGet("/sponsors", async (AdminService service, CancellationToken ct,
            string? status, int? page, int? pageSize)
            => Results.Ok(await service.GetSponsorsAsync(status, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminUsersRead");

        admin.MapPost("/sponsors", async (HttpContext http,
            SponsorCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateSponsorAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminUsersWrite");

        admin.MapPatch("/sponsors/{sponsorId}", async (string sponsorId, HttpContext http,
            SponsorUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateSponsorAsync(http.AdminId(), http.AdminName(), sponsorId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminUsersWrite");

        admin.MapGet("/sponsors/{sponsorId}/learners", async (string sponsorId, AdminService service, CancellationToken ct,
            int? page, int? pageSize)
            => Results.Ok(await service.GetSponsorLearnersAsync(sponsorId, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminUsersRead");

        admin.MapPost("/sponsors/{sponsorId}/learners", async (string sponsorId, HttpContext http,
            CohortMemberAddRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.LinkSponsorLearnerAsync(http.AdminId(), http.AdminName(), sponsorId, request.LearnerId, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminUsersWrite");

        admin.MapGet("/cohorts", async (AdminService service, CancellationToken ct,
            string? sponsorId, string? status, int? page, int? pageSize)
            => Results.Ok(await service.GetCohortsAsync(sponsorId, status, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminUsersRead");

        admin.MapPost("/cohorts", async (HttpContext http,
            CohortCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateCohortAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminUsersWrite");

        admin.MapPatch("/cohorts/{cohortId}", async (string cohortId, HttpContext http,
            CohortUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateCohortAsync(http.AdminId(), http.AdminName(), cohortId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminUsersWrite");

        admin.MapGet("/cohorts/{cohortId}/members", async (string cohortId, AdminService service, CancellationToken ct,
            int? page, int? pageSize)
            => Results.Ok(await service.GetCohortMembersAsync(cohortId, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminUsersRead");

        admin.MapPost("/cohorts/{cohortId}/members", async (string cohortId, HttpContext http,
            CohortMemberAddRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.AddCohortMemberAsync(http.AdminId(), http.AdminName(), cohortId, request.LearnerId, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminUsersWrite");

        // ── AE1: Content Item Analytics ─────────────────

        admin.MapGet("/content-analytics/{contentId}", async (string contentId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetContentItemAnalyticsAsync(contentId, ct)))
            .RequireAuthorization("AdminContentRead");

        // ── AE3: SLA Health Check ───────────────────────

        admin.MapGet("/sla-health", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.CheckSlaHealthAsync(ct)))
            .RequireAuthorization("AdminSystemAdmin");

        // ── B2: Credit Lifecycle Policy ─────────────────

        admin.MapGet("/credit-lifecycle", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetCreditLifecyclePolicyAsync(ct)))
            .RequireAuthorization("AdminBillingRead");

        // ── R1: Learner Cohort Analysis ─────────────────

        admin.MapGet("/analytics/cohort", async ([FromQuery] string? groupBy, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetLearnerCohortAnalysisAsync(groupBy, ct)))
            .RequireAuthorization("AdminContentRead");

        // ── R2: Content Effectiveness ───────────────────

        admin.MapGet("/analytics/content-effectiveness", async ([FromQuery] string? subtestCode, [FromQuery] int? top, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetContentEffectivenessAsync(subtestCode, top ?? 50, ct)))
            .RequireAuthorization("AdminContentRead");

        // ── R3: Expert Efficiency Report ────────────────

        admin.MapGet("/analytics/expert-efficiency", async ([FromQuery] int? days, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetExpertEfficiencyReportAsync(days ?? 30, ct)))
            .RequireAuthorization("AdminContentRead");

        // ── R4: Subscription Health ─────────────────────

        admin.MapGet("/analytics/subscription-health", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetSubscriptionHealthAsync(ct)))
            .RequireAuthorization("AdminContentRead");

        // ── Grammar Admin CRUD ────────────────────────

        admin.MapGet("/grammar/lessons", async (AdminService service, CancellationToken ct,
            string? profession, string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetGrammarLessonsAsync(profession, status, search, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapGet("/grammar/lessons/{lessonId}", async (string lessonId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetGrammarLessonDetailAsync(lessonId, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapPost("/grammar/lessons", async (HttpContext http, AdminGrammarLessonCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateGrammarLessonAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapPut("/grammar/lessons/{lessonId}", async (string lessonId, HttpContext http, AdminGrammarLessonUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateGrammarLessonAsync(http.AdminId(), http.AdminName(), lessonId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapPost("/grammar/lessons/{lessonId}/archive", async (string lessonId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ArchiveGrammarLessonAsync(http.AdminId(), http.AdminName(), lessonId, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        // ── Grammar Publish Gate + AI Draft (Grounded) ────────────────

        admin.MapGet("/grammar/lessons/{lessonId}/publish-gate",
            async (string lessonId,
                OetLearner.Api.Services.Grammar.IGrammarPublishGateService gate,
                CancellationToken ct)
            => Results.Ok(await gate.EvaluateAsync(lessonId, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapPost("/grammar/lessons/{lessonId}/publish",
            async (string lessonId, HttpContext http,
                OetLearner.Api.Services.Grammar.IGrammarPublishGateService gate,
                CancellationToken ct)
            =>
            {
                var result = await gate.PublishAsync(lessonId, http.AdminId(), http.AdminName(), ct);
                return result.CanPublish
                    ? Results.Ok(new { published = true, status = "active", errors = Array.Empty<string>() })
                    : Results.Json(new { published = false, errors = result.Errors }, statusCode: 422);
            })
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapPost("/grammar/lessons/{lessonId}/unpublish",
            async (string lessonId, HttpContext http,
                OetLearner.Api.Services.Grammar.IGrammarPublishGateService gate,
                CancellationToken ct)
            => Results.Ok(await gate.UnpublishAsync(lessonId, http.AdminId(), http.AdminName(), ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapGet("/grammar/lessons/{lessonId}/stats",
            async (string lessonId,
                OetLearner.Api.Services.Grammar.IGrammarPublishGateService gate,
                CancellationToken ct)
            => Results.Ok(await gate.GetStatsAsync(lessonId, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapPost("/grammar/ai-draft",
            async (HttpContext http,
                AdminGrammarAiDraftRequest body,
                OetLearner.Api.Services.Grammar.IGrammarDraftService draft,
                CancellationToken ct) =>
            {
                if (body is null || string.IsNullOrWhiteSpace(body.Prompt))
                    return Results.BadRequest(new { error = "prompt is required" });

                try
                {
                    var result = await draft.GenerateAsync(new OetLearner.Api.Services.Grammar.GrammarDraftRequest(
                        ExamTypeCode: body.ExamTypeCode ?? "oet",
                        TopicSlug: body.TopicSlug,
                        Prompt: body.Prompt,
                        Level: body.Level ?? "intermediate",
                        TargetExerciseCount: body.TargetExerciseCount ?? 6,
                        Profession: body.Profession),
                        adminId: http.AdminId(),
                        adminName: http.AdminName(),
                        authAccountId: http.User.FindFirst("aid")?.Value,
                        ct);
                    return Results.Ok(new
                    {
                        lessonId = result.LessonId,
                        title = result.Title,
                        contentBlockCount = result.ContentBlockCount,
                        exerciseCount = result.ExerciseCount,
                        rulebookVersion = result.RulebookVersion,
                        appliedRuleIds = result.AppliedRuleIds,
                        warning = result.Warning,
                    });
                }
                catch (OetLearner.Api.Services.AiManagement.AiQuotaDeniedException qex)
                {
                    return Results.Json(new { errorCode = qex.ErrorCode, error = qex.Message }, statusCode: 429);
                }
            })
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        // ── Vocabulary Admin CRUD ────────────────────────

        admin.MapGet("/vocabulary/items", async (AdminService service, CancellationToken ct,
            string? profession, string? category, string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetVocabularyItemsAsync(profession, category, status, search, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapGet("/vocabulary/items/{itemId}", async (string itemId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetVocabularyItemDetailAsync(itemId, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapPost("/vocabulary/items", async (HttpContext http, AdminVocabularyItemCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateVocabularyItemAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapPut("/vocabulary/items/{itemId}", async (string itemId, HttpContext http, AdminVocabularyItemUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateVocabularyItemAsync(http.AdminId(), http.AdminName(), itemId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapDelete("/vocabulary/items/{itemId}", async (string itemId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.DeleteVocabularyItemAsync(http.AdminId(), http.AdminName(), itemId, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapPost("/vocabulary/import", async (HttpContext http, IFormFile file, AdminService service, CancellationToken ct)
            => Results.Ok(await service.BulkImportVocabularyAsync(http.AdminId(), http.AdminName(), file, ct)))
            .RequireRateLimiting("PerUserWrite").DisableAntiforgery().RequireAuthorization("AdminContentWrite");

        // ── Conversation Template Admin CRUD ────────────────

        admin.MapGet("/conversation/templates", async (AdminService service, CancellationToken ct,
            string? profession, string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetConversationTemplatesAsync(profession, status, search, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapGet("/conversation/templates/{templateId}", async (string templateId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetConversationTemplateDetailAsync(templateId, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapPost("/conversation/templates", async (HttpContext http, AdminConversationTemplateCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateConversationTemplateAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapPut("/conversation/templates/{templateId}", async (string templateId, HttpContext http, AdminConversationTemplateUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateConversationTemplateAsync(http.AdminId(), http.AdminName(), templateId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapPost("/conversation/templates/{templateId}/archive", async (string templateId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ArchiveConversationTemplateAsync(http.AdminId(), http.AdminName(), templateId, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        // ── Pronunciation Drill Admin CRUD ──────────────────

        admin.MapGet("/pronunciation/drills", async (AdminService service, CancellationToken ct,
            string? profession, string? difficulty, string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetPronunciationDrillsAsync(profession, difficulty, status, search, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapGet("/pronunciation/drills/{drillId}", async (string drillId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetPronunciationDrillDetailAsync(drillId, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapPost("/pronunciation/drills", async (HttpContext http, AdminPronunciationDrillCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreatePronunciationDrillAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapPut("/pronunciation/drills/{drillId}", async (string drillId, HttpContext http, AdminPronunciationDrillUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdatePronunciationDrillAsync(http.AdminId(), http.AdminName(), drillId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapPost("/pronunciation/drills/{drillId}/archive", async (string drillId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ArchivePronunciationDrillAsync(http.AdminId(), http.AdminName(), drillId, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        // ── Notification Template Admin CRUD ────────────────

        admin.MapGet("/notification-templates", async (AdminService service, CancellationToken ct,
            string? channel, string? category, int? page, int? pageSize)
            => Results.Ok(await service.GetNotificationTemplatesAsync(channel, category, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminSystemAdmin");

        admin.MapGet("/notification-templates/{templateId}", async (string templateId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetNotificationTemplateDetailAsync(templateId, ct)))
            .RequireAuthorization("AdminSystemAdmin");

        admin.MapPost("/notification-templates", async (HttpContext http, AdminNotificationTemplateCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateNotificationTemplateAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        admin.MapPut("/notification-templates/{templateId}", async (string templateId, HttpContext http, AdminNotificationTemplateUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateNotificationTemplateAsync(http.AdminId(), http.AdminName(), templateId, request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        admin.MapDelete("/notification-templates/{templateId}", async (string templateId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.DeleteNotificationTemplateAsync(http.AdminId(), http.AdminName(), templateId, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        // ── Free Tier Management ────────────────────────────

        admin.MapGet("/free-tier", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetFreeTierConfigAsync(ct)))
            .RequireAuthorization("AdminBillingRead");

        admin.MapPut("/free-tier", async (HttpContext http, AdminFreeTierConfigUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateFreeTierConfigAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminBillingWrite");

        admin.MapGet("/free-tier/usage-stats", async (AdminService service, CancellationToken ct,
            int? page, int? pageSize)
            => Results.Ok(await service.GetFreeTierUsageStatsAsync(page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization("AdminBillingRead");

        // ── Community Moderation ────────────────────────

        admin.MapPatch("/community/threads/{threadId}/pin", async (string threadId,
            AdminCommunityPinRequest request, LearnerDbContext db, CancellationToken ct) =>
        {
            var thread = await db.ForumThreads.FindAsync([threadId], ct);
            if (thread == null) return Results.NotFound(new { error = "THREAD_NOT_FOUND" });
            thread.IsPinned = request.IsPinned;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = thread.Id, isPinned = thread.IsPinned });
        }).RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        admin.MapPatch("/community/threads/{threadId}/lock", async (string threadId,
            AdminCommunityLockRequest request, LearnerDbContext db, CancellationToken ct) =>
        {
            var thread = await db.ForumThreads.FindAsync([threadId], ct);
            if (thread == null) return Results.NotFound(new { error = "THREAD_NOT_FOUND" });
            thread.IsLocked = request.IsLocked;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = thread.Id, isLocked = thread.IsLocked });
        }).RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        admin.MapDelete("/community/threads/{threadId}", async (string threadId,
            LearnerDbContext db, CancellationToken ct) =>
        {
            var thread = await db.ForumThreads.FindAsync([threadId], ct);
            if (thread == null) return Results.NotFound(new { error = "THREAD_NOT_FOUND" });
            var replies = await db.ForumReplies.Where(r => r.ThreadId == threadId).ToListAsync(ct);
            db.ForumReplies.RemoveRange(replies);
            db.ForumThreads.Remove(thread);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { deleted = true });
        }).RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        admin.MapDelete("/community/threads/{threadId}/replies/{replyId}", async (string threadId, string replyId,
            LearnerDbContext db, CancellationToken ct) =>
        {
            var reply = await db.ForumReplies.FirstOrDefaultAsync(r => r.Id == replyId && r.ThreadId == threadId, ct);
            if (reply == null) return Results.NotFound(new { error = "REPLY_NOT_FOUND" });
            db.ForumReplies.Remove(reply);
            var thread = await db.ForumThreads.FindAsync([threadId], ct);
            if (thread != null && thread.ReplyCount > 0) thread.ReplyCount--;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { deleted = true });
        }).RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        // ── Role & Permission Management ────────────────────

        admin.MapGet("/roles", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var builtInRoles = new[]
            {
                new { id = "system_admin", name = "System Admin", description = "Full system access", isBuiltIn = true, permissions = AdminPermissions.All },
                new { id = "content_editor", name = "Content Editor", description = "Content read/write access", isBuiltIn = true, permissions = new[] { AdminPermissions.ContentRead, AdminPermissions.ContentWrite } },
                new { id = "reviewer", name = "Reviewer", description = "Review operations access", isBuiltIn = true, permissions = new[] { AdminPermissions.ContentRead, AdminPermissions.ReviewOps } },
                new { id = "billing_admin", name = "Billing Admin", description = "Billing management access", isBuiltIn = true, permissions = new[] { AdminPermissions.BillingRead, AdminPermissions.BillingWrite } }
            };
            return Results.Ok(new { roles = builtInRoles });
        }).RequireAuthorization("AdminSystemAdmin");

        admin.MapPost("/roles", async (AdminRoleCreateRequest request, LearnerDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "ROLE_NAME_REQUIRED" });
            var invalidPerms = request.Permissions.Except(AdminPermissions.All).ToArray();
            if (invalidPerms.Length > 0)
                return Results.BadRequest(new { error = "INVALID_PERMISSIONS", invalid = invalidPerms });
            var role = new
            {
                id = Guid.NewGuid().ToString(),
                name = request.Name,
                description = request.Description,
                isBuiltIn = false,
                permissions = request.Permissions,
                createdAt = DateTime.UtcNow
            };
            return Results.Ok(new { role });
        }).RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        admin.MapPut("/roles/{roleId}", async (string roleId, AdminRoleUpdateRequest request, LearnerDbContext db, CancellationToken ct) =>
        {
            var builtInIds = new[] { "system_admin", "content_editor", "reviewer", "billing_admin" };
            if (builtInIds.Contains(roleId))
                return Results.BadRequest(new { error = "CANNOT_MODIFY_BUILTIN_ROLE" });
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "ROLE_NAME_REQUIRED" });
            var invalidPerms = request.Permissions.Except(AdminPermissions.All).ToArray();
            if (invalidPerms.Length > 0)
                return Results.BadRequest(new { error = "INVALID_PERMISSIONS", invalid = invalidPerms });
            return Results.Ok(new { id = roleId, name = request.Name, description = request.Description, permissions = request.Permissions, updatedAt = DateTime.UtcNow });
        }).RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        admin.MapDelete("/roles/{roleId}", async (string roleId, LearnerDbContext db, CancellationToken ct) =>
        {
            var builtInIds = new[] { "system_admin", "content_editor", "reviewer", "billing_admin" };
            if (builtInIds.Contains(roleId))
                return Results.BadRequest(new { error = "CANNOT_DELETE_BUILTIN_ROLE" });
            return Results.Ok(new { deleted = true, roleId });
        }).RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        admin.MapGet("/roles/{roleId}/users", async (string roleId, LearnerDbContext db, CancellationToken ct) =>
        {
            var users = await db.AdminUsers
                .Where(u => u.Role == roleId)
                .Select(u => new { u.Id, u.DisplayName, u.Email, u.Role, u.IsActive })
                .ToListAsync(ct);
            return Results.Ok(new { roleId, users });
        }).RequireAuthorization("AdminSystemAdmin");

        admin.MapPost("/roles/{roleId}/users/{userId}", async (string roleId, string userId, LearnerDbContext db, CancellationToken ct) =>
        {
            var user = await db.AdminUsers.FindAsync([userId], ct);
            if (user == null) return Results.NotFound(new { error = "USER_NOT_FOUND" });
            user.Role = roleId;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { assigned = true, userId, roleId });
        }).RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        admin.MapDelete("/roles/{roleId}/users/{userId}", async (string roleId, string userId, LearnerDbContext db, CancellationToken ct) =>
        {
            var user = await db.AdminUsers.FindAsync([userId], ct);
            if (user == null) return Results.NotFound(new { error = "USER_NOT_FOUND" });
            if (user.Role != roleId)
                return Results.BadRequest(new { error = "USER_NOT_IN_ROLE" });
            user.Role = "unassigned";
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { removed = true, userId, roleId });
        }).RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminSystemAdmin");

        admin.MapGet("/roles/permissions", (LearnerDbContext db, CancellationToken ct) =>
        {
            var permissions = AdminPermissions.All.Select(p => new
            {
                key = p,
                category = p.Contains(':') ? p.Split(':')[0] : "general",
                name = p.Replace(':', ' ').Replace('_', ' ')
            }).ToArray();
            return Results.Ok(new { permissions });
        }).RequireAuthorization("AdminSystemAdmin");

        // Strategy Guide Management
        admin.MapGet("/strategies", async (
            StrategyGuideService service,
            CancellationToken ct,
            string? status,
            string? examTypeCode,
            string? search)
            => Results.Ok(await service.ListAdminGuidesAsync(status, examTypeCode, search, ct)))
            .WithName("AdminListStrategyGuides")
            .WithSummary("Lists strategy guides for admin publishing workflows.")
            .RequireAuthorization("AdminContentRead");

        admin.MapGet("/strategies/{guideId}", async (string guideId, StrategyGuideService service, CancellationToken ct) =>
        {
            var guide = await service.GetAdminGuideAsync(guideId, ct);
            return guide is null ? Results.NotFound(new { error = "NOT_FOUND" }) : Results.Ok(guide);
        })
        .WithName("AdminGetStrategyGuide")
        .WithSummary("Gets a strategy guide draft or published record.")
        .RequireAuthorization("AdminContentRead");

        admin.MapPost("/strategies", async (
            HttpContext http,
            StrategyGuideUpsertRequest request,
            StrategyGuideService service,
            CancellationToken ct)
            => Results.Ok(await service.CreateGuideAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithName("AdminCreateStrategyGuide")
            .WithSummary("Creates a draft strategy guide.")
            .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapPut("/strategies/{guideId}", async (
            string guideId,
            HttpContext http,
            StrategyGuideUpsertRequest request,
            StrategyGuideService service,
            CancellationToken ct) =>
        {
            var guide = await service.UpdateGuideAsync(http.AdminId(), http.AdminName(), guideId, request, ct);
            return guide is null ? Results.NotFound(new { error = "NOT_FOUND" }) : Results.Ok(guide);
        })
        .WithName("AdminUpdateStrategyGuide")
        .WithSummary("Updates strategy guide metadata and content.")
        .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentWrite");

        admin.MapGet("/strategies/{guideId}/publish-gate", async (string guideId, StrategyGuideService service, CancellationToken ct) =>
        {
            var validation = await service.ValidateGuideForPublishAsync(guideId, ct);
            return validation is null ? Results.NotFound(new { error = "NOT_FOUND" }) : Results.Ok(validation);
        })
        .WithName("AdminValidateStrategyGuidePublish")
        .WithSummary("Validates required strategy guide fields before publish.")
        .RequireAuthorization("AdminContentRead");

        admin.MapPost("/strategies/{guideId}/publish", async (string guideId, HttpContext http, StrategyGuideService service, CancellationToken ct) =>
        {
            var result = await service.PublishGuideAsync(http.AdminId(), http.AdminName(), guideId, ct);
            return result.Published ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("AdminPublishStrategyGuide")
        .WithSummary("Publishes a valid strategy guide and writes an audit event.")
        .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentPublish");

        admin.MapPost("/strategies/{guideId}/archive", async (string guideId, HttpContext http, StrategyGuideService service, CancellationToken ct) =>
        {
            var guide = await service.ArchiveGuideAsync(http.AdminId(), http.AdminName(), guideId, ct);
            return guide is null ? Results.NotFound(new { error = "NOT_FOUND" }) : Results.Ok(guide);
        })
        .WithName("AdminArchiveStrategyGuide")
        .WithSummary("Archives a strategy guide and writes an audit event.")
        .RequireRateLimiting("PerUserWrite").RequireAuthorization("AdminContentPublish");

        return app;
    }

    private static string AdminId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated admin id is required.");

    private static string AdminName(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Admin";
}

public record AdminCommunityPinRequest(bool IsPinned);
public record AdminCommunityLockRequest(bool IsLocked);
public record AdminRoleCreateRequest(string Name, string Description, string[] Permissions);
public record AdminRoleUpdateRequest(string Name, string Description, string[] Permissions);
