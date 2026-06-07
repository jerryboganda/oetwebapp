using System.Security.Claims;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class AdminCampaignEndpoints
{
    public static IEndpointRouteBuilder MapAdminCampaignEndpoints(this IEndpointRouteBuilder app)
    {
        var campaigns = app.MapGroup("/v1/admin/campaigns")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        campaigns.MapGet("/", async (INotificationCampaignService service, CancellationToken ct,
            int? page, int? pageSize, NotificationCampaignStatus? status)
            => Results.Ok(await service.ListAsync(page ?? 1, pageSize ?? 20, status, ct)))
            .WithAdminRead("AdminNotifications");

        campaigns.MapGet("/{id:guid}", async (Guid id, INotificationCampaignService service, CancellationToken ct) =>
        {
            var campaign = await service.GetAsync(id, ct);
            return campaign is null ? Results.NotFound() : Results.Ok(campaign);
        }).WithAdminRead("AdminNotifications");

        campaigns.MapPost("/", async (HttpContext http,
            CreateCampaignRequest request, INotificationCampaignService service, CancellationToken ct)
            => Results.Ok(await service.CreateAsync(http.AdminId(), request, ct)))
            .WithAdminWrite("AdminNotifications");

        campaigns.MapPut("/{id:guid}", async (Guid id, HttpContext http,
            UpdateCampaignRequest request, INotificationCampaignService service, CancellationToken ct)
            => Results.Ok(await service.UpdateAsync(http.AdminId(), id, request, ct)))
            .WithAdminWrite("AdminNotifications");

        campaigns.MapPost("/{id:guid}/approve", async (Guid id, HttpContext http,
            INotificationCampaignService service, CancellationToken ct)
            => Results.Ok(await service.ApproveAsync(http.AdminId(), id, ct)))
            .WithAdminWrite("AdminNotifications");

        campaigns.MapPost("/{id:guid}/cancel", async (Guid id, HttpContext http,
            INotificationCampaignService service, CancellationToken ct) =>
        {
            await service.CancelAsync(http.AdminId(), id, ct);
            return Results.NoContent();
        }).WithAdminWrite("AdminNotifications");

        campaigns.MapPost("/{id:guid}/evaluate-segment", async (Guid id,
            INotificationCampaignService service, CancellationToken ct)
            => Results.Ok(new { recipientCount = await service.EvaluateSegmentAsync(id, ct) }))
            .WithAdminWrite("AdminNotifications");

        campaigns.MapPost("/{id:guid}/send", async (Guid id,
            INotificationCampaignService service, CancellationToken ct)
            => Results.Ok(await service.SendAsync(id, ct)))
            .WithAdminWrite("AdminNotifications");

        return app;
    }

    private static string AdminId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated admin id is required.");
}
