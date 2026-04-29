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

        // ── Freeze Management (billing/user lifecycle) ─────────────────
        admin.MapGet("/freeze/overview", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetFreezeOverviewAsync(ct)))
            .WithAdminRead("AdminFreezeRead");
        admin.MapPut("/freeze/policy", async (HttpContext http, FreezePolicyRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateFreezePolicyAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminFreezeWrite");
        admin.MapPost("/freeze/manual", async (HttpContext http, FreezeManualCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateManualFreezeAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminFreezeWrite");
        admin.MapPost("/freeze/{freezeId}/approve", async (string freezeId, HttpContext http, FreezeActionRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ApproveFreezeAsync(http.AdminId(), http.AdminName(), freezeId, request, ct)))
            .WithAdminWrite("AdminFreezeWrite");
        admin.MapPost("/freeze/{freezeId}/reject", async (string freezeId, HttpContext http, FreezeActionRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.RejectFreezeAsync(http.AdminId(), http.AdminName(), freezeId, request, ct)))
            .WithAdminWrite("AdminFreezeWrite");
        admin.MapPost("/freeze/{freezeId}/end", async (string freezeId, HttpContext http, FreezeActionRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.EndFreezeAsync(http.AdminId(), http.AdminName(), freezeId, request, ct)))
            .WithAdminWrite("AdminFreezeWrite");
        admin.MapPost("/freeze/{freezeId}/force-end", async (string freezeId, HttpContext http, FreezeActionRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ForceEndFreezeAsync(http.AdminId(), http.AdminName(), freezeId, request, ct)))
            .WithAdminWrite("AdminFreezeWrite");

        // ── Content Management ──────────────────────────────

        admin.MapGet("/content", async (HttpContext http, AdminService service, CancellationToken ct,
            string? type, string? profession, string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetContentListAsync(type, profession, status, search, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapGet("/content/{contentId}", async (string contentId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetContentDetailAsync(contentId, ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapPost("/content", async (HttpContext http, AdminContentCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateContentAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapPut("/content/{contentId}", async (string contentId, HttpContext http, AdminContentUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateContentAsync(http.AdminId(), http.AdminName(), contentId, request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/content/{contentId}/publish", async (string contentId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.PublishContentAsync(http.AdminId(), http.AdminName(), contentId, ct)))
            .WithAdminWrite("AdminContentPublish");

        admin.MapPost("/content/{contentId}/archive", async (string contentId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ArchiveContentAsync(http.AdminId(), http.AdminName(), contentId, ct)))
            .WithAdminWrite("AdminContentPublish");

        admin.MapPost("/content/bulk-action", async (HttpContext http, AdminBulkActionRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.BulkActionContentAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapGet("/content/{contentId}/impact", async (string contentId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetContentImpactSummaryAsync(contentId, ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapGet("/content/{contentId}/revisions", async (string contentId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetContentRevisionsAsync(contentId, ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapPost("/content/{contentId}/revisions/{revisionId}/restore", async (string contentId, string revisionId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.RestoreRevisionAsync(http.AdminId(), http.AdminName(), contentId, revisionId, ct)))
            .WithAdminWrite("AdminContentWrite");

        // ── AI Content Generation ─────────────────────────

        admin.MapPost("/content/generate", async (HttpContext http, ContentGenerationRequest request, ContentGenerationService service, CancellationToken ct)
            => Results.Ok(await service.QueueGenerationAsync(http.AdminId(), request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapGet("/content/generation-jobs", async (ContentGenerationService service, CancellationToken ct, int? page, int? pageSize)
            => Results.Ok(await service.GetJobsAsync(page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapGet("/content/generation-jobs/{jobId}", async (string jobId, ContentGenerationService service, CancellationToken ct)
            => Results.Ok(await service.GetJobDetailAsync(jobId, ct)))
            .WithAdminRead("AdminContentRead");

        // ── Taxonomy (Professions) ──────────────────────────

        admin.MapGet("/taxonomy", async (AdminService service, CancellationToken ct, string? type, string? status)
            => Results.Ok(await service.GetTaxonomyListAsync(type, status, ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapPost("/taxonomy", async (HttpContext http, AdminTaxonomyCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateTaxonomyNodeAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapPut("/taxonomy/{professionId}", async (string professionId, HttpContext http, AdminTaxonomyUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateTaxonomyNodeAsync(http.AdminId(), http.AdminName(), professionId, request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/taxonomy/{professionId}/archive", async (string professionId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ArchiveTaxonomyNodeAsync(http.AdminId(), http.AdminName(), professionId, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapGet("/taxonomy/{professionId}/impact", async (string professionId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetTaxonomyImpactSummaryAsync(professionId, ct)))
            .WithAdminRead("AdminContentRead");

        // ── Criteria / Rubric ──────────────────────────────

        admin.MapGet("/criteria", async (AdminService service, CancellationToken ct, string? subtest, string? status)
            => Results.Ok(await service.GetCriteriaListAsync(subtest, status, ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapPost("/criteria", async (HttpContext http, AdminCriterionCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateCriterionAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapPut("/criteria/{criterionId}", async (string criterionId, HttpContext http, AdminCriterionUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateCriterionAsync(http.AdminId(), http.AdminName(), criterionId, request, ct)))
            .WithAdminWrite("AdminContentWrite");

        // ── AI Evaluation Config ─────────────────────────────

        admin.MapGet("/ai-config", async (AdminService service, CancellationToken ct, string? status)
            => Results.Ok(await service.GetAIConfigListAsync(status, ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapPost("/ai-config", async (HttpContext http, AdminAIConfigCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateAIConfigAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapPut("/ai-config/{configId}", async (string configId, HttpContext http, AdminAIConfigUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateAIConfigAsync(http.AdminId(), http.AdminName(), configId, request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/ai-config/{configId}/activate", async (string configId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ActivateAIConfigAsync(http.AdminId(), http.AdminName(), configId, ct)))
            .WithAdminWrite("AdminContentWrite");

        // ── Feature Flags ───────────────────────────────────

        admin.MapGet("/flags", async (AdminService service, CancellationToken ct, string? type)
            => Results.Ok(await service.GetFlagListAsync(type, ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapPost("/flags", async (HttpContext http, AdminFlagCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateFlagAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminSystemAdmin");

        admin.MapPut("/flags/{flagId}", async (string flagId, HttpContext http, AdminFlagUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateFlagAsync(http.AdminId(), http.AdminName(), flagId, request, ct)))
            .WithAdminWrite("AdminSystemAdmin");

        admin.MapPost("/flags/{flagId}/activate", async (string flagId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ActivateFlagAsync(http.AdminId(), http.AdminName(), flagId, ct)))
            .WithAdminWrite("AdminSystemAdmin");

        admin.MapPost("/flags/{flagId}/deactivate", async (string flagId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.DeactivateFlagAsync(http.AdminId(), http.AdminName(), flagId, ct)))
            .WithAdminWrite("AdminSystemAdmin");

        // ── Audit Logs ──────────────────────────────────────

        admin.MapGet("/audit-logs", async (AdminService service, CancellationToken ct,
            string? action, string? actor, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetAuditEventsAsync(action, actor, search, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminSystemAdmin");

        admin.MapGet("/audit-logs/{eventId}", async (string eventId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetAuditEventDetailAsync(eventId, ct)))
            .WithAdminRead("AdminSystemAdmin");

        admin.MapGet("/audit-logs/export", async (AdminService service, CancellationToken ct,
            string? action, string? actor, string? search)
            =>
            {
                var export = await service.ExportAuditEventsCsvAsync(action, actor, search, ct);
                return Results.File(export.Bytes, "text/csv; charset=utf-8", export.FileName);
            })
            .WithAdminRead("AdminSystemAdmin");

        // ── User Ops ────────────────────────────────────────

        admin.MapGet("/users", async (AdminService service, CancellationToken ct,
            string? role, string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetUserListAsync(role, status, search, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminUsersRead");

        admin.MapGet("/users/{userId}", async (string userId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetUserDetailAsync(userId, ct)))
            .WithAdminRead("AdminUsersRead");

        admin.MapPost("/users/invite", async (HttpContext http, AdminUserInviteRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.InviteUserAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminUsersWrite");

        admin.MapPost("/users/import", async (HttpContext http, IFormFile file, AdminService service, CancellationToken ct)
            => Results.Ok(await service.BulkImportUsersAsync(http.AdminId(), http.AdminName(), file, ct)))
            .RequireRateLimiting("PerUserWrite")
            .DisableAntiforgery().WithAdminRead("AdminUsersWrite");

        admin.MapPut("/users/{userId}/status", async (string userId, HttpContext http, AdminUserStatusRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateUserStatusAsync(http.AdminId(), http.AdminName(), userId, request, ct)))
            .WithAdminWrite("AdminUsersWrite");

        admin.MapPost("/users/{userId}/delete", async (string userId, HttpContext http, AdminUserLifecycleRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.DeleteUserAsync(http.AdminId(), http.AdminName(), userId, request, ct)))
            .WithAdminWrite("AdminUsersWrite");

        admin.MapPost("/users/{userId}/restore", async (string userId, HttpContext http, AdminUserLifecycleRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.RestoreUserAsync(http.AdminId(), http.AdminName(), userId, request, ct)))
            .WithAdminWrite("AdminUsersWrite");

        admin.MapPost("/users/{userId}/credits", async (string userId, HttpContext http, AdminUserCreditsRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.AdjustUserCreditsAsync(http.AdminId(), http.AdminName(), userId, request, ct)))
            .WithAdminWrite("AdminUsersWrite");

        admin.MapPost("/users/{userId}/password-reset", async (string userId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.TriggerUserPasswordResetAsync(http.AdminId(), http.AdminName(), userId, ct)))
            .WithAdminWrite("AdminUsersWrite");

        admin.MapPost("/users/{userId}/sessions/revoke", async (string userId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.RevokeUserSessionsAsync(http.AdminId(), http.AdminName(), userId, ct)))
            .WithAdminWrite("AdminUsersWrite");

        admin.MapPost("/users/{userId}/unlock", async (string userId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UnlockUserAsync(http.AdminId(), http.AdminName(), userId, ct)))
            .WithAdminWrite("AdminUsersWrite");

        admin.MapPost("/users/{userId}/resend-invite", async (string userId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ResendUserInviteAsync(http.AdminId(), http.AdminName(), userId, ct)))
            .WithAdminWrite("AdminUsersWrite");

        // ── Billing ─────────────────────────────────────────

        admin.MapGet("/billing/plans", async (AdminService service, CancellationToken ct, string? status)
            => Results.Ok(await service.GetBillingPlansAsync(status, ct)))
            .WithAdminRead("AdminBillingRead");

        admin.MapPost("/billing/plans", async (HttpContext http, AdminBillingPlanCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateBillingPlanAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminBillingWrite");

        admin.MapPut("/billing/plans/{planId}", async (string planId, HttpContext http, AdminBillingPlanUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateBillingPlanAsync(http.AdminId(), http.AdminName(), planId, request, ct)))
            .WithAdminWrite("AdminBillingWrite");

        admin.MapGet("/billing/plans/{planId}/versions", async (string planId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetBillingPlanVersionsAsync(planId, ct)))
            .WithAdminRead("AdminBillingRead");

        admin.MapGet("/billing/add-ons", async (AdminService service, CancellationToken ct, string? status)
            => Results.Ok(await service.GetBillingAddOnsAsync(status, ct)))
            .WithAdminRead("AdminBillingRead");

        admin.MapPost("/billing/add-ons", async (HttpContext http, AdminBillingAddOnCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateBillingAddOnAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminBillingWrite");

        admin.MapPut("/billing/add-ons/{addOnId}", async (string addOnId, HttpContext http, AdminBillingAddOnUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateBillingAddOnAsync(http.AdminId(), http.AdminName(), addOnId, request, ct)))
            .WithAdminWrite("AdminBillingWrite");

        admin.MapGet("/billing/add-ons/{addOnId}/versions", async (string addOnId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetBillingAddOnVersionsAsync(addOnId, ct)))
            .WithAdminRead("AdminBillingRead");

        admin.MapGet("/billing/coupons", async (AdminService service, CancellationToken ct, string? status)
            => Results.Ok(await service.GetBillingCouponsAsync(status, ct)))
            .WithAdminRead("AdminBillingRead");

        admin.MapPost("/billing/coupons", async (HttpContext http, AdminBillingCouponCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateBillingCouponAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminBillingWrite");

        admin.MapPut("/billing/coupons/{couponId}", async (string couponId, HttpContext http, AdminBillingCouponUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateBillingCouponAsync(http.AdminId(), http.AdminName(), couponId, request, ct)))
            .WithAdminWrite("AdminBillingWrite");

        admin.MapGet("/billing/coupons/{couponId}/versions", async (string couponId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetBillingCouponVersionsAsync(couponId, ct)))
            .WithAdminRead("AdminBillingRead");

        admin.MapGet("/billing/subscriptions", async (AdminService service, CancellationToken ct, string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetBillingSubscriptionsAsync(status, search, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminBillingRead");

        admin.MapGet("/billing/redemptions", async (AdminService service, CancellationToken ct, string? couponCode, string? userId, int? page, int? pageSize)
            => Results.Ok(await service.GetBillingCouponRedemptionsAsync(couponCode, userId, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminBillingRead");

        admin.MapGet("/billing/invoices", async (AdminService service, CancellationToken ct,
            string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetBillingInvoicesAsync(status, search, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminBillingRead");

        admin.MapGet("/billing/invoices/{invoiceId}/evidence", async (string invoiceId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetBillingInvoiceEvidenceAsync(invoiceId, ct)))
            .WithAdminRead("AdminBillingRead");

        admin.MapGet("/billing/payment-transactions", async (AdminService service, CancellationToken ct,
            string? status, string? gateway, string? transactionType, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetBillingPaymentTransactionsAsync(status, gateway, transactionType, search, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminBillingRead");

        admin.MapGet("/billing/operations", async (AdminService service, CancellationToken ct,
            string? operationType, string? status, string? userId, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetBillingOperationsAsync(operationType, status, userId, search, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminBillingRead");

        admin.MapPost("/billing/operations", async (HttpContext http, AdminBillingOperationCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateBillingOperationAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminBillingWrite");

        admin.MapPost("/billing/operations/{operationId}/resolve", async (string operationId, HttpContext http,
            AdminBillingOperationResolveRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ResolveBillingOperationAsync(http.AdminId(), http.AdminName(), operationId, request, ct)))
            .WithAdminWrite("AdminBillingWrite");

        // ── Review Ops ──────────────────────────────────────

        admin.MapGet("/review-ops/summary", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetReviewOpsSummaryAsync(ct)))
            .WithAdminRead("AdminReviewOps");

        admin.MapGet("/review-ops/queue", async (AdminService service, CancellationToken ct, string? status, string? priority)
            => Results.Ok(await service.GetReviewOpsQueueAsync(status, priority, ct)))
            .WithAdminRead("AdminReviewOps");

        admin.MapPost("/review-ops/{reviewRequestId}/assign", async (string reviewRequestId, HttpContext http, AdminReviewAssignRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.AssignReviewAsync(http.AdminId(), http.AdminName(), reviewRequestId, request, ct)))
            .WithAdminWrite("AdminReviewOps");

        admin.MapPost("/review-ops/{reviewRequestId}/cancel", async (string reviewRequestId, HttpContext http, AdminReviewCancelRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CancelReviewAsync(http.AdminId(), http.AdminName(), reviewRequestId, request, ct)))
            .WithAdminWrite("AdminReviewOps");

        admin.MapPost("/review-ops/{reviewRequestId}/reopen", async (string reviewRequestId, HttpContext http, AdminReviewReopenRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ReopenReviewAsync(http.AdminId(), http.AdminName(), reviewRequestId, request, ct)))
            .WithAdminWrite("AdminReviewOps");

        admin.MapGet("/review-ops/failures", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetReviewFailuresAsync(ct)))
            .WithAdminRead("AdminReviewOps");

        // ── Quality Analytics ───────────────────────────────

        admin.MapGet("/quality-analytics", async (AdminService service, CancellationToken ct, string? timeRange, string? subtest, string? profession)
            => Results.Ok(await service.GetQualityAnalyticsAsync(timeRange, subtest, profession, ct)))
            .WithAdminRead("AdminContentRead");

        // ── Admin Permissions (RBAC) ────────────────────────

        admin.MapGet("/permissions", (AdminService service)
            => Results.Ok(service.GetAllPermissions()))
            .WithAdminRead("AdminSystemAdmin");

        admin.MapGet("/permissions/{userId}", async (string userId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetAdminPermissionsAsync(userId, ct)))
            .WithAdminRead("AdminSystemAdmin");

        admin.MapPut("/permissions/{userId}", async (string userId, HttpContext http, AdminPermissionUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateAdminPermissionsAsync(http.AdminId(), http.AdminName(), userId, request, ct)))
            .WithAdminWrite("AdminSystemAdmin");

        // ── Permission Templates ────────────────────────────

        admin.MapGet("/permission-templates", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetPermissionTemplatesAsync(ct)))
            .WithAdminRead("AdminSystemAdmin");

        admin.MapPost("/permission-templates", async (HttpContext http, CreatePermissionTemplateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreatePermissionTemplateAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminSystemAdmin");

        admin.MapDelete("/permission-templates/{templateId}", async (string templateId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.DeletePermissionTemplateAsync(http.AdminId(), http.AdminName(), templateId, ct)))
            .WithAdminWrite("AdminSystemAdmin");

        admin.MapPost("/users/{userId}/apply-template/{templateId}", async (string userId, string templateId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ApplyPermissionTemplateAsync(http.AdminId(), http.AdminName(), userId, templateId, ct)))
            .WithAdminWrite("AdminSystemAdmin");

        // ── Content Publishing Workflow ─────────────────────

        admin.MapPost("/content/{contentId}/request-publish", async (string contentId, HttpContext http, AdminPublishRequestPayload request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.RequestContentPublishAsync(http.AdminId(), http.AdminName(), contentId, request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/content/{contentId}/submit-for-review", async (string contentId, HttpContext http, AdminPublishRequestPayload request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.SubmitContentForReviewAsync(http.AdminId(), http.AdminName(), contentId, request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/content/{contentId}/editor-approve", async (string contentId, HttpContext http, AdminEditorReviewPayload request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.EditorApproveContentAsync(http.AdminId(), http.AdminName(), contentId, request, ct)))
            .WithAdminWrite("AdminContentEditorReview");

        admin.MapPost("/content/{contentId}/editor-reject", async (string contentId, HttpContext http, AdminEditorRejectPayload request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.EditorRejectContentAsync(http.AdminId(), http.AdminName(), contentId, request, ct)))
            .WithAdminWrite("AdminContentEditorReview");

        admin.MapPost("/content/{contentId}/publisher-approve", async (string contentId, HttpContext http, AdminPublisherApprovePayload request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.PublisherApproveContentAsync(http.AdminId(), http.AdminName(), contentId, request, ct)))
            .WithAdminWrite("AdminContentPublisherApproval");

        admin.MapPost("/content/{contentId}/publisher-reject", async (string contentId, HttpContext http, AdminPublisherRejectPayload request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.PublisherRejectContentAsync(http.AdminId(), http.AdminName(), contentId, request, ct)))
            .WithAdminWrite("AdminContentPublisherApproval");

        admin.MapGet("/content/pending-review", async (AdminService service, CancellationToken ct, string? stage, int? page, int? pageSize)
            => Results.Ok(await service.GetPendingReviewContentAsync(stage, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapGet("/publish-requests", async (AdminService service, CancellationToken ct, string? status, int? page, int? pageSize)
            => Results.Ok(await service.GetPublishRequestsAsync(status, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminContentPublishRequestsRead");

        admin.MapPost("/publish-requests/{requestId}/approve", async (string requestId, HttpContext http, AdminPublishReviewPayload request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ApprovePublishRequestAsync(http.AdminId(), http.AdminName(), requestId, request, ct)))
            .WithAdminWrite("AdminContentPublish");

        admin.MapPost("/publish-requests/{requestId}/reject", async (string requestId, HttpContext http, AdminPublishReviewPayload request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.RejectPublishRequestAsync(http.AdminId(), http.AdminName(), requestId, request, ct)))
            .WithAdminWrite("AdminContentPublish");

        // ── Webhook Monitoring ──────────────────────────────

        admin.MapGet("/webhooks", async (AdminService service, CancellationToken ct,
            string? gateway, string? status, int? page, int? pageSize)
            => Results.Ok(await service.GetWebhookEventsAsync(gateway, status, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminSystemAdmin");

        admin.MapGet("/webhooks/summary", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetWebhookSummaryAsync(ct)))
            .WithAdminRead("AdminSystemAdmin");

        admin.MapPost("/webhooks/{eventId}/retry", async (string eventId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.RetryWebhookAsync(http.AdminId(), http.AdminName(), eventId, ct)))
            .WithAdminWrite("AdminSystemAdmin");

        // ── Review Escalation (Disagreement Resolution) ─────

        admin.MapGet("/escalations", async (AdminService service, CancellationToken ct,
            string? status, int? page, int? pageSize)
            => Results.Ok(await service.GetReviewEscalationsAsync(status, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminReviewOps");

        admin.MapPost("/escalations/{escalationId}/assign", async (string escalationId, HttpContext http,
            AdminEscalationAssignRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.AssignEscalationReviewerAsync(http.AdminId(), http.AdminName(), escalationId, request, ct)))
            .WithAdminWrite("AdminReviewOps");

        admin.MapPost("/escalations/{escalationId}/resolve", async (string escalationId, HttpContext http,
            AdminEscalationResolveRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ResolveEscalationAsync(http.AdminId(), http.AdminName(), escalationId, request, ct)))
            .WithAdminWrite("AdminReviewOps");

        // ── Score Guarantee Claims ──────────────────────

        admin.MapGet("/score-guarantee-claims", async (AdminService service, CancellationToken ct,
            string? status, int? page, int? pageSize)
            => Results.Ok(await service.GetScoreGuaranteeClaimsAsync(status, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminBillingRead");

        admin.MapPost("/score-guarantee-claims/{pledgeId}/review", async (string pledgeId, HttpContext http,
            AdminScoreGuaranteeReviewRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ReviewScoreGuaranteeClaimAsync(http.AdminId(), http.AdminName(), pledgeId, request, ct)))
            .WithAdminWrite("AdminBillingWrite");

        // ── A4: Content Quality Scoring ─────────────────

        admin.MapGet("/content-quality", async (AdminService service, CancellationToken ct,
            int? page, int? pageSize)
            => Results.Ok(await service.GetContentQualityOverviewAsync(page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapPost("/content-quality/{contentId}/score", async (string contentId, HttpContext http,
            AdminService service, CancellationToken ct)
            => Results.Ok(await service.ScoreContentQualityAsync(http.AdminId(), http.AdminName(), contentId, ct)))
            .WithAdminWrite("AdminContentWrite");

        // ── A6: Bulk Learner Operations ─────────────────

        admin.MapPost("/bulk/credits", async (HttpContext http,
            AdminBulkCreditRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.BulkCreditAdjustmentAsync(http.AdminId(), http.AdminName(), request.UserIds, request.CreditAmount, request.Reason, ct)))
            .WithAdminWrite("AdminUsersWrite");

        admin.MapPost("/bulk/notifications", async (HttpContext http,
            AdminBulkNotificationRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.BulkNotificationAsync(http.AdminId(), http.AdminName(), request.UserIds, request.Title, request.Message, request.Category, ct)))
            .WithAdminWrite("AdminUsersWrite");

        admin.MapPost("/bulk/status", async (HttpContext http,
            AdminBulkStatusRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.BulkStatusChangeAsync(http.AdminId(), http.AdminName(), request.UserIds, request.NewStatus, request.Reason, ct)))
            .WithAdminWrite("AdminUsersWrite");

        // ── B3: Enterprise / Sponsor Channel ────────────

        admin.MapGet("/sponsors", async (AdminService service, CancellationToken ct,
            string? status, int? page, int? pageSize)
            => Results.Ok(await service.GetSponsorsAsync(status, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminUsersRead");

        admin.MapPost("/sponsors", async (HttpContext http,
            SponsorCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateSponsorAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminUsersWrite");

        admin.MapPatch("/sponsors/{sponsorId}", async (string sponsorId, HttpContext http,
            SponsorUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateSponsorAsync(http.AdminId(), http.AdminName(), sponsorId, request, ct)))
            .WithAdminWrite("AdminUsersWrite");

        admin.MapGet("/sponsors/{sponsorId}/learners", async (string sponsorId, AdminService service, CancellationToken ct,
            int? page, int? pageSize)
            => Results.Ok(await service.GetSponsorLearnersAsync(sponsorId, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminUsersRead");

        admin.MapPost("/sponsors/{sponsorId}/learners", async (string sponsorId, HttpContext http,
            CohortMemberAddRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.LinkSponsorLearnerAsync(http.AdminId(), http.AdminName(), sponsorId, request.LearnerId, ct)))
            .WithAdminWrite("AdminUsersWrite");

        admin.MapGet("/cohorts", async (AdminService service, CancellationToken ct,
            string? sponsorId, string? status, int? page, int? pageSize)
            => Results.Ok(await service.GetCohortsAsync(sponsorId, status, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminUsersRead");

        admin.MapPost("/cohorts", async (HttpContext http,
            CohortCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateCohortAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminUsersWrite");

        admin.MapPatch("/cohorts/{cohortId}", async (string cohortId, HttpContext http,
            CohortUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateCohortAsync(http.AdminId(), http.AdminName(), cohortId, request, ct)))
            .WithAdminWrite("AdminUsersWrite");

        admin.MapGet("/cohorts/{cohortId}/members", async (string cohortId, AdminService service, CancellationToken ct,
            int? page, int? pageSize)
            => Results.Ok(await service.GetCohortMembersAsync(cohortId, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminUsersRead");

        admin.MapPost("/cohorts/{cohortId}/members", async (string cohortId, HttpContext http,
            CohortMemberAddRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.AddCohortMemberAsync(http.AdminId(), http.AdminName(), cohortId, request.LearnerId, ct)))
            .WithAdminWrite("AdminUsersWrite");

        // ── AE1: Content Item Analytics ─────────────────

        admin.MapGet("/content-analytics/{contentId}", async (string contentId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetContentItemAnalyticsAsync(contentId, ct)))
            .WithAdminRead("AdminContentRead");

        // ── AE3: SLA Health Check ───────────────────────

        admin.MapGet("/sla-health", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.CheckSlaHealthAsync(ct)))
            .WithAdminRead("AdminSystemAdmin");

        // ── B2: Credit Lifecycle Policy ─────────────────

        admin.MapGet("/credit-lifecycle", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetCreditLifecyclePolicyAsync(ct)))
            .WithAdminRead("AdminBillingRead");

        // ── R1: Learner Cohort Analysis ─────────────────

        admin.MapGet("/analytics/cohort", async ([FromQuery] string? groupBy, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetLearnerCohortAnalysisAsync(groupBy, ct)))
            .WithAdminRead("AdminContentRead");

        // ── R2: Content Effectiveness ───────────────────

        admin.MapGet("/analytics/content-effectiveness", async ([FromQuery] string? subtestCode, [FromQuery] int? top, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetContentEffectivenessAsync(subtestCode, top ?? 50, ct)))
            .WithAdminRead("AdminContentRead");

        // ── R3: Expert Efficiency Report ────────────────

        admin.MapGet("/analytics/expert-efficiency", async ([FromQuery] int? days, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetExpertEfficiencyReportAsync(days ?? 30, ct)))
            .WithAdminRead("AdminContentRead");

        // ── R4: Subscription Health ─────────────────────

        admin.MapGet("/analytics/subscription-health", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetSubscriptionHealthAsync(ct)))
            .WithAdminRead("AdminContentRead");

        // ── Grammar Admin CRUD ────────────────────────

        admin.MapGet("/grammar/lessons", async (AdminService service, CancellationToken ct,
            string? profession, string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetGrammarLessonsAsync(profession, status, search, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapGet("/grammar/lessons/{lessonId}", async (string lessonId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetGrammarLessonDetailAsync(lessonId, ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapPost("/grammar/lessons", async (HttpContext http, AdminGrammarLessonCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateGrammarLessonAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapPut("/grammar/lessons/{lessonId}", async (string lessonId, HttpContext http, AdminGrammarLessonUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateGrammarLessonAsync(http.AdminId(), http.AdminName(), lessonId, request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/grammar/lessons/{lessonId}/archive", async (string lessonId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ArchiveGrammarLessonAsync(http.AdminId(), http.AdminName(), lessonId, ct)))
            .WithAdminWrite("AdminContentWrite");

        // ── Grammar Publish Gate + AI Draft (Grounded) ────────────────

        admin.MapGet("/grammar/lessons/{lessonId}/publish-gate",
            async (string lessonId,
                OetLearner.Api.Services.Grammar.IGrammarPublishGateService gate,
                CancellationToken ct)
            => Results.Ok(await gate.EvaluateAsync(lessonId, ct)))
            .WithAdminRead("AdminContentRead");

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
            .WithAdminWrite("AdminContentPublish");

        admin.MapPost("/grammar/lessons/{lessonId}/unpublish",
            async (string lessonId, HttpContext http,
                OetLearner.Api.Services.Grammar.IGrammarPublishGateService gate,
                CancellationToken ct)
            => Results.Ok(await gate.UnpublishAsync(lessonId, http.AdminId(), http.AdminName(), ct)))
            .WithAdminWrite("AdminContentPublish");

        admin.MapGet("/grammar/lessons/{lessonId}/stats",
            async (string lessonId,
                OetLearner.Api.Services.Grammar.IGrammarPublishGateService gate,
                CancellationToken ct)
            => Results.Ok(await gate.GetStatsAsync(lessonId, ct)))
            .WithAdminRead("AdminContentRead");

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
            .WithAdminWrite("AdminContentWrite");

        // ── Vocabulary Admin CRUD ────────────────────────

        admin.MapGet("/vocabulary/items", async (AdminService service, CancellationToken ct,
            string? profession, string? category, string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetVocabularyItemsAsync(profession, category, status, search, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapGet("/vocabulary/items/{itemId}", async (string itemId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetVocabularyItemDetailAsync(itemId, ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapPost("/vocabulary/items", async (HttpContext http, AdminVocabularyItemCreateRequestV2 request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateVocabularyItemAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapPut("/vocabulary/items/{itemId}", async (string itemId, HttpContext http, AdminVocabularyItemUpdateRequestV2 request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateVocabularyItemAsync(http.AdminId(), http.AdminName(), itemId, request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapDelete("/vocabulary/items/{itemId}", async (string itemId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.DeleteVocabularyItemAsync(http.AdminId(), http.AdminName(), itemId, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapGet("/vocabulary/categories", async (AdminService service, CancellationToken ct,
            string? examTypeCode, string? professionId)
            => Results.Ok(await service.GetVocabularyCategoriesAdminAsync(examTypeCode, professionId, ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapPost("/vocabulary/import/preview", async (HttpContext http, IFormFile file, AdminService service, CancellationToken ct)
            => Results.Ok(await service.PreviewVocabularyImportAsync(file, ct)))
            .RequireRateLimiting("PerUserWrite").DisableAntiforgery().WithAdminRead("AdminContentRead");

        admin.MapPost("/vocabulary/import", async (HttpContext http, IFormFile file, AdminService service, CancellationToken ct, [FromQuery] bool? dryRun)
            => Results.Ok(await service.BulkImportVocabularyV2Async(http.AdminId(), http.AdminName(), file, dryRun ?? false, ct)))
            .RequireRateLimiting("PerUserWrite").DisableAntiforgery().WithAdminRead("AdminContentWrite");

        admin.MapPost("/vocabulary/ai/draft", async (HttpContext http, AdminVocabularyAiDraftRequest request,
            VocabularyDraftService draftSvc, CancellationToken ct) =>
        {
            try
            {
                var result = await draftSvc.GenerateAsync(request, http.AdminId(), http.AdminName(), null, ct);
                return Results.Ok(result);
            }
            catch (OetLearner.Api.Services.AiManagement.AiQuotaDeniedException qex)
            {
                return Results.Json(new { errorCode = qex.ErrorCode, error = qex.Message }, statusCode: 429);
            }
        })
            .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/vocabulary/ai/draft/accept", async (HttpContext http, AdminVocabularyAiDraftAcceptRequest request,
            VocabularyDraftService draftSvc, CancellationToken ct) =>
        {
            var created = await draftSvc.AcceptAsync(request, http.AdminId(), http.AdminName(), ct);
            return Results.Ok(new { createdIds = created, count = created.Count });
        })
            .WithAdminWrite("AdminContentWrite");

        // ── Conversation Template Admin CRUD ────────────────

        admin.MapGet("/conversation/templates", async (AdminService service, CancellationToken ct,
            string? profession, string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetConversationTemplatesAsync(profession, status, search, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapGet("/conversation/templates/{templateId}", async (string templateId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetConversationTemplateDetailAsync(templateId, ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapPost("/conversation/templates", async (HttpContext http, AdminConversationTemplateCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateConversationTemplateAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapPut("/conversation/templates/{templateId}", async (string templateId, HttpContext http, AdminConversationTemplateUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateConversationTemplateAsync(http.AdminId(), http.AdminName(), templateId, request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/conversation/templates/{templateId}/archive", async (string templateId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ArchiveConversationTemplateAsync(http.AdminId(), http.AdminName(), templateId, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/conversation/templates/{templateId}/publish", async (string templateId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.PublishConversationTemplateAsync(http.AdminId(), http.AdminName(), templateId, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapGet("/conversation/settings", async (
                OetLearner.Api.Services.Conversation.IConversationOptionsProvider provider,
                CancellationToken ct) =>
            {
                var merged = await provider.GetAsync(ct);
                // Mask secret material — admins should re-enter to change.
                return Results.Ok(new
                {
                    merged.Enabled,
                    merged.AsrProvider,
                    merged.TtsProvider,
                    merged.AzureSpeechRegion,
                    merged.AzureLocale,
                    merged.AzureTtsDefaultVoice,
                    merged.WhisperBaseUrl,
                    merged.WhisperModel,
                    merged.DeepgramModel,
                    merged.DeepgramLanguage,
                    merged.ElevenLabsDefaultVoiceId,
                    merged.ElevenLabsModel,
                    merged.CosyVoiceBaseUrl,
                    merged.CosyVoiceDefaultVoice,
                    merged.ChatTtsBaseUrl,
                    merged.ChatTtsDefaultVoice,
                    merged.GptSoVitsBaseUrl,
                    merged.GptSoVitsDefaultVoice,
                    merged.MaxAudioBytes,
                    merged.AllowedMimeTypes,
                    merged.AudioRetentionDays,
                    merged.PrepDurationSeconds,
                    merged.MaxSessionDurationSeconds,
                    merged.MaxTurnDurationSeconds,
                    merged.EnabledTaskTypes,
                    merged.FreeTierSessionsLimit,
                    merged.FreeTierWindowDays,
                    merged.ReplyModel,
                    merged.EvaluationModel,
                    merged.ReplyTemperature,
                    merged.EvaluationTemperature,
                    // secret hints only
                    AzureSpeechKeyPresent = !string.IsNullOrEmpty(merged.AzureSpeechKey),
                    WhisperApiKeyPresent = !string.IsNullOrEmpty(merged.WhisperApiKey),
                    DeepgramApiKeyPresent = !string.IsNullOrEmpty(merged.DeepgramApiKey),
                    ElevenLabsApiKeyPresent = !string.IsNullOrEmpty(merged.ElevenLabsApiKey),
                    CosyVoiceApiKeyPresent = !string.IsNullOrEmpty(merged.CosyVoiceApiKey),
                    ChatTtsApiKeyPresent = !string.IsNullOrEmpty(merged.ChatTtsApiKey),
                    GptSoVitsApiKeyPresent = !string.IsNullOrEmpty(merged.GptSoVitsApiKey),
                });
            })
            .WithAdminRead("AdminContentRead");

        admin.MapPut("/conversation/settings", async (
                HttpContext http,
                OetLearner.Api.Contracts.AdminConversationSettingsRequest request,
                OetLearner.Api.Data.LearnerDbContext db,
                OetLearner.Api.Services.Conversation.IConversationOptionsProvider provider,
                CancellationToken ct) =>
            {
                var row = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .FirstOrDefaultAsync(db.ConversationSettings.Where(r => r.Id == "default"), ct);
                var now = DateTimeOffset.UtcNow;
                if (row is null)
                {
                    row = new OetLearner.Api.Domain.ConversationSettingsRow { Id = "default", UpdatedAt = now };
                    db.ConversationSettings.Add(row);
                }

                // Scalar overrides — null in request = leave stored value untouched.
                if (request.Enabled.HasValue) row.Enabled = request.Enabled;
                if (request.AsrProvider is not null) row.AsrProvider = request.AsrProvider;
                if (request.TtsProvider is not null) row.TtsProvider = request.TtsProvider;
                if (request.AzureSpeechRegion is not null) row.AzureSpeechRegion = request.AzureSpeechRegion;
                if (request.AzureLocale is not null) row.AzureLocale = request.AzureLocale;
                if (request.AzureTtsDefaultVoice is not null) row.AzureTtsDefaultVoice = request.AzureTtsDefaultVoice;
                if (request.WhisperBaseUrl is not null) row.WhisperBaseUrl = request.WhisperBaseUrl;
                if (request.WhisperModel is not null) row.WhisperModel = request.WhisperModel;
                if (request.DeepgramModel is not null) row.DeepgramModel = request.DeepgramModel;
                if (request.DeepgramLanguage is not null) row.DeepgramLanguage = request.DeepgramLanguage;
                if (request.ElevenLabsDefaultVoiceId is not null) row.ElevenLabsDefaultVoiceId = request.ElevenLabsDefaultVoiceId;
                if (request.ElevenLabsModel is not null) row.ElevenLabsModel = request.ElevenLabsModel;
                if (request.CosyVoiceBaseUrl is not null) row.CosyVoiceBaseUrl = request.CosyVoiceBaseUrl;
                if (request.CosyVoiceDefaultVoice is not null) row.CosyVoiceDefaultVoice = request.CosyVoiceDefaultVoice;
                if (request.ChatTtsBaseUrl is not null) row.ChatTtsBaseUrl = request.ChatTtsBaseUrl;
                if (request.ChatTtsDefaultVoice is not null) row.ChatTtsDefaultVoice = request.ChatTtsDefaultVoice;
                if (request.GptSoVitsBaseUrl is not null) row.GptSoVitsBaseUrl = request.GptSoVitsBaseUrl;
                if (request.GptSoVitsDefaultVoice is not null) row.GptSoVitsDefaultVoice = request.GptSoVitsDefaultVoice;
                if (request.MaxAudioBytes.HasValue) row.MaxAudioBytes = request.MaxAudioBytes;
                if (request.AudioRetentionDays.HasValue) row.AudioRetentionDays = request.AudioRetentionDays;
                if (request.PrepDurationSeconds.HasValue) row.PrepDurationSeconds = request.PrepDurationSeconds;
                if (request.MaxSessionDurationSeconds.HasValue) row.MaxSessionDurationSeconds = request.MaxSessionDurationSeconds;
                if (request.MaxTurnDurationSeconds.HasValue) row.MaxTurnDurationSeconds = request.MaxTurnDurationSeconds;
                if (request.EnabledTaskTypes is not null) row.EnabledTaskTypesCsv = string.Join(",", request.EnabledTaskTypes);
                if (request.FreeTierSessionsLimit.HasValue) row.FreeTierSessionsLimit = request.FreeTierSessionsLimit;
                if (request.FreeTierWindowDays.HasValue) row.FreeTierWindowDays = request.FreeTierWindowDays;
                if (request.ReplyModel is not null) row.ReplyModel = request.ReplyModel;
                if (request.EvaluationModel is not null) row.EvaluationModel = request.EvaluationModel;
                if (request.ReplyTemperature.HasValue) row.ReplyTemperature = request.ReplyTemperature;
                if (request.EvaluationTemperature.HasValue) row.EvaluationTemperature = request.EvaluationTemperature;

                // Secrets — encrypt on the way in. Empty string = clear; null = leave.
                var optsProvider = (OetLearner.Api.Services.Conversation.ConversationOptionsProvider)provider;
                if (request.AzureSpeechKey is not null) row.AzureSpeechKeyEncrypted = request.AzureSpeechKey.Length == 0 ? null : optsProvider.Protect(request.AzureSpeechKey);
                if (request.WhisperApiKey is not null) row.WhisperApiKeyEncrypted = request.WhisperApiKey.Length == 0 ? null : optsProvider.Protect(request.WhisperApiKey);
                if (request.DeepgramApiKey is not null) row.DeepgramApiKeyEncrypted = request.DeepgramApiKey.Length == 0 ? null : optsProvider.Protect(request.DeepgramApiKey);
                if (request.ElevenLabsApiKey is not null) row.ElevenLabsApiKeyEncrypted = request.ElevenLabsApiKey.Length == 0 ? null : optsProvider.Protect(request.ElevenLabsApiKey);
                if (request.CosyVoiceApiKey is not null) row.CosyVoiceApiKeyEncrypted = request.CosyVoiceApiKey.Length == 0 ? null : optsProvider.Protect(request.CosyVoiceApiKey);
                if (request.ChatTtsApiKey is not null) row.ChatTtsApiKeyEncrypted = request.ChatTtsApiKey.Length == 0 ? null : optsProvider.Protect(request.ChatTtsApiKey);
                if (request.GptSoVitsApiKey is not null) row.GptSoVitsApiKeyEncrypted = request.GptSoVitsApiKey.Length == 0 ? null : optsProvider.Protect(request.GptSoVitsApiKey);

                row.UpdatedAt = now;
                row.UpdatedByUserId = http.AdminId();
                row.UpdatedByUserName = http.AdminName();
                await db.SaveChangesAsync(ct);
                provider.Invalidate();

                return Results.Ok(new { ok = true, updatedAt = now });
            })
            .RequireRateLimiting("PerUserWrite")
            .WithAdminRead("AdminContentWrite");

        // Admin sessions / evaluations operational viewer
        admin.MapGet("/conversation/sessions", async (
                OetLearner.Api.Data.LearnerDbContext db,
                string? userId, string? state, string? taskTypeCode, int? page, int? pageSize,
                CancellationToken ct) =>
            {
                var p = page is null or < 1 ? 1 : page.Value;
                var ps = pageSize is null or < 1 or > 100 ? 25 : pageSize.Value;
                var q = db.ConversationSessions.AsQueryable();
                if (!string.IsNullOrWhiteSpace(userId)) q = q.Where(s => s.UserId == userId);
                if (!string.IsNullOrWhiteSpace(state)) q = q.Where(s => s.State == state);
                if (!string.IsNullOrWhiteSpace(taskTypeCode)) q = q.Where(s => s.TaskTypeCode == taskTypeCode);
                var total = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(q, ct);
                var items = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                    q.OrderByDescending(s => s.CreatedAt).Skip((p - 1) * ps).Take(ps), ct);
                return Results.Ok(new { total, page = p, pageSize = ps, items = items.Select(s => new
                {
                    s.Id, s.UserId, s.TaskTypeCode, s.Profession, s.State, s.TurnCount, s.DurationSeconds,
                    s.TemplateId, s.LastErrorCode, s.CreatedAt, s.StartedAt, s.CompletedAt,
                }) });
            })
            .WithAdminRead("AdminContentRead");

        admin.MapGet("/conversation/sessions/{sessionId}", async (
                string sessionId,
                OetLearner.Api.Data.LearnerDbContext db,
                CancellationToken ct) =>
            {
                var session = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .FirstOrDefaultAsync(db.ConversationSessions.AsQueryable(), s => s.Id == sessionId, ct);
                if (session is null) return Results.NotFound();
                var turns = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                    db.ConversationTurns.Where(t => t.SessionId == sessionId).OrderBy(t => t.TurnNumber), ct);
                var evaluation = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .FirstOrDefaultAsync(db.ConversationEvaluations.AsQueryable(), e => e.SessionId == sessionId, ct);
                var annotations = evaluation is null
                    ? new List<OetLearner.Api.Domain.ConversationTurnAnnotation>()
                    : await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                        db.ConversationTurnAnnotations.Where(a => a.EvaluationId == evaluation.Id).OrderBy(a => a.TurnNumber), ct);
                return Results.Ok(new
                {
                    session = new
                    {
                        session.Id, session.UserId, session.TaskTypeCode, session.Profession, session.State,
                        session.TurnCount, session.DurationSeconds, session.TemplateId, session.LastErrorCode,
                        session.CreatedAt, session.StartedAt, session.CompletedAt, session.ScenarioJson,
                    },
                    turns = turns.Select(t => new
                    {
                        t.TurnNumber, t.Role, t.Content, t.AudioUrl, t.DurationMs,
                        t.TimestampMs, t.ConfidenceScore, t.AiFeatureCode, t.CreatedAt,
                    }),
                    evaluation = evaluation is null ? null : new
                    {
                        evaluation.Id, evaluation.OverallScaled, evaluation.OverallGrade, evaluation.Passed,
                        evaluation.CriteriaJson, evaluation.StrengthsJson, evaluation.ImprovementsJson,
                        evaluation.SuggestedPracticeJson, evaluation.AppliedRuleIdsJson,
                        evaluation.RulebookVersion, evaluation.Advisory, evaluation.CreatedAt,
                    },
                    annotations = annotations.Select(a => new
                    {
                        a.TurnNumber, a.Type, a.Category, a.RuleId, a.Evidence, a.Suggestion, a.CreatedAt,
                    }),
                });
            })
            .WithAdminRead("AdminContentRead");

        admin.MapGet("/conversation/evaluations.csv", async (
                OetLearner.Api.Data.LearnerDbContext db,
                CancellationToken ct) =>
            {
                var evals = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                    db.ConversationEvaluations.OrderByDescending(e => e.CreatedAt).Take(10000), ct);
                var csv = new System.Text.StringBuilder();
                csv.AppendLine("Id,SessionId,UserId,OverallScaled,OverallGrade,Passed,RulebookVersion,CreatedAt");
                foreach (var e in evals)
                    csv.AppendLine($"{e.Id},{e.SessionId},{e.UserId},{e.OverallScaled},{e.OverallGrade},{e.Passed},{e.RulebookVersion},{e.CreatedAt:O}");
                return Results.File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "conversation-evaluations.csv");
            })
            .WithAdminRead("AdminContentRead");

        // TTS voice preview — posts sample text to the currently-configured TTS
        // and returns the audio bytes so admins can hear the configured voice.
        admin.MapPost("/conversation/tts-preview", async (
                OetLearner.Api.Contracts.AdminConversationTtsPreviewRequest request,
                OetLearner.Api.Services.Conversation.Tts.IConversationTtsProviderSelector selector,
                CancellationToken ct) =>
            {
                var provider = await selector.TrySelectAsync(ct);
                if (provider is null) return Results.BadRequest(new { error = "tts_disabled" });
                var text = string.IsNullOrWhiteSpace(request.Text)
                    ? "Good morning. Thank you for coming in today. How can I help you?"
                    : request.Text;
                var voice = request.Voice ?? "";
                var locale = string.IsNullOrWhiteSpace(request.Locale) ? "en-GB" : request.Locale;
                var result = await provider.SynthesizeAsync(
                    new OetLearner.Api.Services.Conversation.Tts.ConversationTtsRequest(text, voice, locale),
                    ct);
                if (result.Audio.Length == 0) return Results.NoContent();
                return Results.File(result.Audio, result.MimeType, "tts-preview.mp3");
            })
            .RequireRateLimiting("PerUserWrite")
            .WithAdminRead("AdminContentRead");

        // ── Pronunciation Drill Admin CRUD ──────────────────

        admin.MapGet("/pronunciation/drills", async (AdminService service, CancellationToken ct,
            string? profession, string? difficulty, string? status, string? search, int? page, int? pageSize)
            => Results.Ok(await service.GetPronunciationDrillsAsync(profession, difficulty, status, search, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapGet("/pronunciation/drills/{drillId}", async (string drillId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetPronunciationDrillDetailAsync(drillId, ct)))
            .WithAdminRead("AdminContentRead");

        admin.MapPost("/pronunciation/drills", async (HttpContext http, AdminPronunciationDrillCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreatePronunciationDrillAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapPut("/pronunciation/drills/{drillId}", async (string drillId, HttpContext http, AdminPronunciationDrillUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdatePronunciationDrillAsync(http.AdminId(), http.AdminName(), drillId, request, ct)))
            .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/pronunciation/drills/{drillId}/archive", async (string drillId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.ArchivePronunciationDrillAsync(http.AdminId(), http.AdminName(), drillId, ct)))
            .WithAdminWrite("AdminContentWrite");

        // AI-assisted draft of a pronunciation drill. Routes through the
        // grounded gateway (Kind=Pronunciation, Task=GeneratePronunciationDrill,
        // FeatureCode=admin.pronunciation_draft). Platform-only by policy.
        admin.MapPost("/pronunciation/drills/ai-draft", async (
            HttpContext http,
            AdminPronunciationDrillAiDraftRequest request,
            OetLearner.Api.Services.Pronunciation.IPronunciationAdminDraftService draftService,
            CancellationToken ct)
            => Results.Ok(await draftService.GenerateDraftAsync(request, http.AdminId(), ct)))
            .WithAdminWrite("AdminContentWrite");

        // ── Notification Template Admin CRUD ────────────────

        admin.MapGet("/notification-templates", async (AdminService service, CancellationToken ct,
            string? channel, string? category, int? page, int? pageSize)
            => Results.Ok(await service.GetNotificationTemplatesAsync(channel, category, page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminSystemAdmin");

        admin.MapGet("/notification-templates/{templateId}", async (string templateId, AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetNotificationTemplateDetailAsync(templateId, ct)))
            .WithAdminRead("AdminSystemAdmin");

        admin.MapPost("/notification-templates", async (HttpContext http, AdminNotificationTemplateCreateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.CreateNotificationTemplateAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminSystemAdmin");

        admin.MapPut("/notification-templates/{templateId}", async (string templateId, HttpContext http, AdminNotificationTemplateUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateNotificationTemplateAsync(http.AdminId(), http.AdminName(), templateId, request, ct)))
            .WithAdminWrite("AdminSystemAdmin");

        admin.MapDelete("/notification-templates/{templateId}", async (string templateId, HttpContext http, AdminService service, CancellationToken ct)
            => Results.Ok(await service.DeleteNotificationTemplateAsync(http.AdminId(), http.AdminName(), templateId, ct)))
            .WithAdminWrite("AdminSystemAdmin");

        // ── Free Tier Management ────────────────────────────

        admin.MapGet("/free-tier", async (AdminService service, CancellationToken ct)
            => Results.Ok(await service.GetFreeTierConfigAsync(ct)))
            .WithAdminRead("AdminBillingRead");

        admin.MapPut("/free-tier", async (HttpContext http, AdminFreeTierConfigUpdateRequest request, AdminService service, CancellationToken ct)
            => Results.Ok(await service.UpdateFreeTierConfigAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithAdminWrite("AdminBillingWrite");

        admin.MapGet("/free-tier/usage-stats", async (AdminService service, CancellationToken ct,
            int? page, int? pageSize)
            => Results.Ok(await service.GetFreeTierUsageStatsAsync(page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminBillingRead");

        // ── Community Moderation ────────────────────────

        admin.MapPatch("/community/threads/{threadId}/pin", async (string threadId,
            AdminCommunityPinRequest request, LearnerDbContext db, CancellationToken ct) =>
        {
            var thread = await db.ForumThreads.FindAsync([threadId], ct);
            if (thread == null) return Results.NotFound(new { error = "THREAD_NOT_FOUND" });
            thread.IsPinned = request.IsPinned;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = thread.Id, isPinned = thread.IsPinned });
        }).WithAdminWrite("AdminSystemAdmin");

        admin.MapPatch("/community/threads/{threadId}/lock", async (string threadId,
            AdminCommunityLockRequest request, LearnerDbContext db, CancellationToken ct) =>
        {
            var thread = await db.ForumThreads.FindAsync([threadId], ct);
            if (thread == null) return Results.NotFound(new { error = "THREAD_NOT_FOUND" });
            thread.IsLocked = request.IsLocked;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = thread.Id, isLocked = thread.IsLocked });
        }).WithAdminWrite("AdminSystemAdmin");

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
        }).WithAdminWrite("AdminSystemAdmin");

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
        }).WithAdminWrite("AdminSystemAdmin");

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
        }).WithAdminRead("AdminSystemAdmin");

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
        }).WithAdminWrite("AdminSystemAdmin");

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
        }).WithAdminWrite("AdminSystemAdmin");

        admin.MapDelete("/roles/{roleId}", async (string roleId, LearnerDbContext db, CancellationToken ct) =>
        {
            var builtInIds = new[] { "system_admin", "content_editor", "reviewer", "billing_admin" };
            if (builtInIds.Contains(roleId))
                return Results.BadRequest(new { error = "CANNOT_DELETE_BUILTIN_ROLE" });
            return Results.Ok(new { deleted = true, roleId });
        }).WithAdminWrite("AdminSystemAdmin");

        admin.MapGet("/roles/{roleId}/users", async (string roleId, LearnerDbContext db, CancellationToken ct) =>
        {
            var users = await db.AdminUsers
                .Where(u => u.Role == roleId)
                .Select(u => new { u.Id, u.DisplayName, u.Email, u.Role, u.IsActive })
                .ToListAsync(ct);
            return Results.Ok(new { roleId, users });
        }).WithAdminRead("AdminSystemAdmin");

        admin.MapPost("/roles/{roleId}/users/{userId}", async (string roleId, string userId, LearnerDbContext db, CancellationToken ct) =>
        {
            var user = await db.AdminUsers.FindAsync([userId], ct);
            if (user == null) return Results.NotFound(new { error = "USER_NOT_FOUND" });
            user.Role = roleId;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { assigned = true, userId, roleId });
        }).WithAdminWrite("AdminSystemAdmin");

        admin.MapDelete("/roles/{roleId}/users/{userId}", async (string roleId, string userId, LearnerDbContext db, CancellationToken ct) =>
        {
            var user = await db.AdminUsers.FindAsync([userId], ct);
            if (user == null) return Results.NotFound(new { error = "USER_NOT_FOUND" });
            if (user.Role != roleId)
                return Results.BadRequest(new { error = "USER_NOT_IN_ROLE" });
            user.Role = "unassigned";
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { removed = true, userId, roleId });
        }).WithAdminWrite("AdminSystemAdmin");

        admin.MapGet("/roles/permissions", (LearnerDbContext db, CancellationToken ct) =>
        {
            var permissions = AdminPermissions.All.Select(p => new
            {
                key = p,
                category = p.Contains(':') ? p.Split(':')[0] : "general",
                name = p.Replace(':', ' ').Replace('_', ' ')
            }).ToArray();
            return Results.Ok(new { permissions });
        }).WithAdminRead("AdminSystemAdmin");

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
            .WithAdminRead("AdminContentRead");

        admin.MapGet("/strategies/{guideId}", async (string guideId, StrategyGuideService service, CancellationToken ct) =>
        {
            var guide = await service.GetAdminGuideAsync(guideId, ct);
            return guide is null ? Results.NotFound(new { error = "NOT_FOUND" }) : Results.Ok(guide);
        })
        .WithName("AdminGetStrategyGuide")
        .WithSummary("Gets a strategy guide draft or published record.")
        .WithAdminRead("AdminContentRead");

        admin.MapPost("/strategies", async (
            HttpContext http,
            StrategyGuideUpsertRequest request,
            StrategyGuideService service,
            CancellationToken ct)
            => Results.Ok(await service.CreateGuideAsync(http.AdminId(), http.AdminName(), request, ct)))
            .WithName("AdminCreateStrategyGuide")
            .WithSummary("Creates a draft strategy guide.")
            .WithAdminWrite("AdminContentWrite");

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
        .WithAdminWrite("AdminContentWrite");

        admin.MapGet("/strategies/{guideId}/publish-gate", async (string guideId, StrategyGuideService service, CancellationToken ct) =>
        {
            var validation = await service.ValidateGuideForPublishAsync(guideId, ct);
            return validation is null ? Results.NotFound(new { error = "NOT_FOUND" }) : Results.Ok(validation);
        })
        .WithName("AdminValidateStrategyGuidePublish")
        .WithSummary("Validates required strategy guide fields before publish.")
        .WithAdminRead("AdminContentRead");

        admin.MapPost("/strategies/{guideId}/publish", async (string guideId, HttpContext http, StrategyGuideService service, CancellationToken ct) =>
        {
            var result = await service.PublishGuideAsync(http.AdminId(), http.AdminName(), guideId, ct);
            return result.Published ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("AdminPublishStrategyGuide")
        .WithSummary("Publishes a valid strategy guide and writes an audit event.")
        .WithAdminWrite("AdminContentPublish");

        admin.MapPost("/strategies/{guideId}/archive", async (string guideId, HttpContext http, StrategyGuideService service, CancellationToken ct) =>
        {
            var guide = await service.ArchiveGuideAsync(http.AdminId(), http.AdminName(), guideId, ct);
            return guide is null ? Results.NotFound(new { error = "NOT_FOUND" }) : Results.Ok(guide);
        })
        .WithName("AdminArchiveStrategyGuide")
        .WithSummary("Archives a strategy guide and writes an audit event.")
        .WithAdminWrite("AdminContentPublish");

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
