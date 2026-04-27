using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var notifications = app.MapGroup("/v1/notifications")
            .RequireAuthorization()
            .RequireRateLimiting("PerUser");

        notifications.MapGet("/", async (
            HttpContext http,
            NotificationService service,
            CancellationToken ct,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] bool unreadOnly = false,
            [FromQuery] string? category = null,
            [FromQuery] string? channel = null) =>
            Results.Ok(await service.GetFeedAsync(
                http.AuthAccountId(),
                new NotificationFeedQuery(page, pageSize, unreadOnly, category, channel),
                ct)));

        notifications.MapPost("/{notificationId}/read", async (
            HttpContext http,
            string notificationId,
            NotificationService service,
            CancellationToken ct) =>
        {
            await service.MarkReadAsync(http.AuthAccountId(), notificationId, ct);
            return Results.Ok(new { ok = true });
        });

        notifications.MapPost("/read-all", async (
            HttpContext http,
            NotificationService service,
            CancellationToken ct) =>
        {
            await service.MarkAllReadAsync(http.AuthAccountId(), ct);
            return Results.Ok(new { ok = true });
        });

        notifications.MapGet("/preferences", async (
            HttpContext http,
            NotificationService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetPreferencesAsync(http.AuthAccountId(), http.UserRole(), ct)));

        notifications.MapPatch("/preferences", async (
            HttpContext http,
            NotificationPreferencePatchRequest request,
            NotificationService service,
            CancellationToken ct) =>
            Results.Ok(await service.PatchPreferencesAsync(http.AuthAccountId(), http.UserRole(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        notifications.MapPost("/push-subscriptions", async (
            HttpContext http,
            PushSubscriptionPayload payload,
            NotificationService service,
            CancellationToken ct) =>
            Results.Ok(await service.UpsertPushSubscriptionAsync(http.AuthAccountId(), payload, ct)))
            .RequireRateLimiting("PerUserWrite");

        notifications.MapDelete("/push-subscriptions/{subscriptionId:guid}", async (
            HttpContext http,
            Guid subscriptionId,
            NotificationService service,
            CancellationToken ct) =>
        {
            await service.DeletePushSubscriptionAsync(http.AuthAccountId(), subscriptionId, ct);
            return Results.Ok(new { ok = true });
        }).RequireRateLimiting("PerUserWrite");

        notifications.MapPost("/push-token", async (
            HttpContext http,
            RegisterPushTokenRequest request,
            NotificationService service,
            CancellationToken ct) =>
            Results.Ok(await service.RegisterPushTokenAsync(http.AuthAccountId(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        var admin = app.MapGroup("/v1/admin/notifications")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        admin.MapGet("/catalog", async (NotificationService service, CancellationToken ct)
            => Results.Ok(await service.GetAdminCatalogAsync(ct)));

        admin.MapGet("/policies", async (NotificationService service, CancellationToken ct)
            => Results.Ok(await service.GetAdminPoliciesAsync(ct)));

        admin.MapPut("/policies/{audienceRole}/{eventKey}", async (
            HttpContext http,
            string audienceRole,
            string eventKey,
            AdminNotificationPolicyUpdateRequest request,
            NotificationService service,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateAdminPolicyAsync(http.AdminId(), http.AdminName(), audienceRole, eventKey, request, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapDelete("/policies/{audienceRole}/{eventKey}", async (
            HttpContext http,
            string audienceRole,
            string eventKey,
            NotificationService service,
            CancellationToken ct) =>
            Results.Ok(await service.ResetAdminPolicyOverrideAsync(http.AdminId(), http.AdminName(), audienceRole, eventKey, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapGet("/health", async (NotificationService service, CancellationToken ct)
            => Results.Ok(await service.GetAdminHealthAsync(ct)));

        admin.MapGet("/deliveries", async (
            NotificationService service,
            CancellationToken ct,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? status = null,
            [FromQuery] string? channel = null,
            [FromQuery] string? audienceRole = null,
            [FromQuery] string? eventKey = null) =>
            Results.Ok(await service.GetAdminDeliveriesAsync(page, pageSize, status, channel, audienceRole, eventKey, ct)));

        admin.MapPost("/test-email", async (
            HttpContext http,
            AdminNotificationTestEmailRequest request,
            NotificationService service,
            CancellationToken ct) =>
        {
            await service.SendTestEmailAsync(http.AdminId(), http.AdminName(), request, ct);
            return Results.Ok(new { ok = true });
        }).RequireRateLimiting("PerUserWrite");

        admin.MapPost("/proof/trigger", async (
            HttpContext http,
            AdminNotificationProofTriggerRequest request,
            NotificationService service,
            CancellationToken ct) =>
            Results.Ok(await service.TriggerProofNotificationAsync(http.AdminId(), http.AdminName(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        return app;
    }

    private static string AuthAccountId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(AuthTokenService.AuthAccountIdClaimType)
           ?? throw new InvalidOperationException("Authenticated auth account id is required.");

    private static string UserRole(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.Role)
           ?? throw new InvalidOperationException("Authenticated role is required.");

    private static string AdminId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated admin id is required.");

    private static string AdminName(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Admin";
}
