using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class NotificationRuleEndpoints
{
    public static IEndpointRouteBuilder MapNotificationRuleEndpoints(this IEndpointRouteBuilder app)
    {
        var rules = app.MapGroup("/v1/admin/notification-rules")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        rules.MapGet("/", ListRules).WithAdminRead("AdminNotifications");
        rules.MapPost("/", CreateRule).WithAdminWrite("AdminNotifications");
        rules.MapPut("/{id:guid}", UpdateRule).WithAdminWrite("AdminNotifications");
        rules.MapDelete("/{id:guid}", DeleteRule).WithAdminWrite("AdminNotifications");

        return app;
    }

    private static async Task<IResult> ListRules(LearnerDbContext db, CancellationToken ct)
    {
        var rules = await db.Set<NotificationRule>()
            .AsNoTracking()
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.EventKey)
            .ToListAsync(ct);

        return Results.Ok(rules);
    }

    private static async Task<IResult> CreateRule(
        CreateNotificationRuleRequest request,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            EventKey = request.EventKey,
            AudienceRole = request.AudienceRole,
            Channels = request.Channels ?? "InApp,Email,Push",
            Priority = request.Priority ?? 5,
            DelaySeconds = request.DelaySeconds,
            ExpiryMinutes = request.ExpiryMinutes,
            FallbackChannels = request.FallbackChannels,
            RequiredConsentCategory = request.RequiredConsentCategory,
            BypassQuietHours = request.BypassQuietHours ?? false,
            IsActive = request.IsActive ?? true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.Set<NotificationRule>().Add(rule);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/v1/admin/notification-rules/{rule.Id}", rule);
    }

    private static async Task<IResult> UpdateRule(
        Guid id,
        UpdateNotificationRuleRequest request,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var rule = await db.Set<NotificationRule>().FindAsync([id], ct);
        if (rule is null) return Results.NotFound();

        if (request.EventKey is not null) rule.EventKey = request.EventKey;
        if (request.AudienceRole is not null) rule.AudienceRole = request.AudienceRole;
        if (request.Channels is not null) rule.Channels = request.Channels;
        if (request.Priority.HasValue) rule.Priority = request.Priority.Value;
        if (request.DelaySeconds.HasValue) rule.DelaySeconds = request.DelaySeconds.Value;
        if (request.ExpiryMinutes.HasValue) rule.ExpiryMinutes = request.ExpiryMinutes.Value;
        if (request.FallbackChannels is not null) rule.FallbackChannels = request.FallbackChannels;
        if (request.RequiredConsentCategory is not null) rule.RequiredConsentCategory = request.RequiredConsentCategory;
        if (request.BypassQuietHours.HasValue) rule.BypassQuietHours = request.BypassQuietHours.Value;
        if (request.IsActive.HasValue) rule.IsActive = request.IsActive.Value;
        rule.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return Results.Ok(rule);
    }

    private static async Task<IResult> DeleteRule(
        Guid id,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var rule = await db.Set<NotificationRule>().FindAsync([id], ct);
        if (rule is null) return Results.NotFound();

        db.Set<NotificationRule>().Remove(rule);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static string AdminId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated admin id is required.");
}

public sealed record CreateNotificationRuleRequest(
    string EventKey,
    string? AudienceRole = null,
    string? Channels = null,
    int? Priority = null,
    int? DelaySeconds = null,
    int? ExpiryMinutes = null,
    string? FallbackChannels = null,
    string? RequiredConsentCategory = null,
    bool? BypassQuietHours = null,
    bool? IsActive = null);

public sealed record UpdateNotificationRuleRequest(
    string? EventKey = null,
    string? AudienceRole = null,
    string? Channels = null,
    int? Priority = null,
    int? DelaySeconds = null,
    int? ExpiryMinutes = null,
    string? FallbackChannels = null,
    string? RequiredConsentCategory = null,
    bool? BypassQuietHours = null,
    bool? IsActive = null);
