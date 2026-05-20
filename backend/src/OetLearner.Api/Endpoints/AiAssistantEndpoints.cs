using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Services.AiAssistant;

namespace OetLearner.Api.Endpoints;

public static class AiAssistantEndpoints
{
    public static void MapAiAssistantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ai-assistant")
            .RequireAuthorization()
            .WithTags("AI Assistant");

        // Thread CRUD
        group.MapGet("/threads", async (
            [FromServices] IAiAssistantOrchestrator orchestrator,
            HttpContext ctx,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 20,
            CancellationToken ct = default) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

            var threads = await orchestrator.ListThreadsAsync(userId, skip, take, ct);
            return Results.Ok(threads);
        });

        group.MapPost("/threads", async (
            [FromServices] IAiAssistantOrchestrator orchestrator,
            HttpContext ctx,
            [FromBody] CreateAiThreadRequest? req,
            CancellationToken ct = default) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

            var role = GetUserRole(ctx);
            var thread = await orchestrator.CreateThreadAsync(userId, role, req?.Title, ct);
            return Results.Created($"/v1/ai-assistant/threads/{thread.Id}", thread);
        });

        group.MapGet("/threads/{threadId}/messages", async (
            string threadId,
            [FromServices] IAiAssistantOrchestrator orchestrator,
            HttpContext ctx,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 50,
            CancellationToken ct = default) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

            var messages = await orchestrator.GetMessagesAsync(threadId, userId, skip, take, ct);
            return Results.Ok(messages);
        });

        group.MapDelete("/threads/{threadId}", async (
            string threadId,
            [FromServices] IAiAssistantOrchestrator orchestrator,
            HttpContext ctx,
            CancellationToken ct = default) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

            var success = await orchestrator.ArchiveThreadAsync(threadId, userId, ct);
            return success ? Results.NoContent() : Results.NotFound();
        });

        // Admin endpoints for thread monitoring
        var adminGroup = app.MapGroup("/v1/admin/ai-assistant")
            .RequireAuthorization("AdminOnly")
            .WithTags("AI Assistant Admin");

        adminGroup.MapGet("/threads", async (
            [FromServices] LearnerDbContext db,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 20,
            [FromQuery] string? userId = null,
            [FromQuery] string? role = null,
            CancellationToken ct = default) =>
        {
            var query = db.AiAssistantThreads.AsQueryable();
            if (!string.IsNullOrWhiteSpace(userId))
                query = query.Where(t => t.UserId == userId);
            if (!string.IsNullOrWhiteSpace(role))
                query = query.Where(t => t.Role == role);

            var threads = await query
                .OrderByDescending(t => t.UpdatedAt)
                .Skip(skip).Take(take)
                .Select(t => new
                {
                    t.Id, t.UserId, t.Role, t.Title,
                    t.IsArchived, t.CreatedAt, t.UpdatedAt,
                    MessageCount = db.AiAssistantMessages.Count(m => m.ThreadId == t.Id)
                })
                .ToListAsync(ct);

            return Results.Ok(threads);
        });

        adminGroup.MapGet("/threads/{threadId}/messages", async (
            string threadId,
            [FromServices] LearnerDbContext db,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100,
            CancellationToken ct = default) =>
        {
            var messages = await db.AiAssistantMessages
                .Where(m => m.ThreadId == threadId)
                .OrderBy(m => m.CreatedAt)
                .Skip(skip).Take(take)
                .ToListAsync(ct);

            return Results.Ok(messages);
        });

        adminGroup.MapGet("/stats", async (
            [FromServices] LearnerDbContext db,
            CancellationToken ct = default) =>
        {
            var now = DateTimeOffset.UtcNow;
            var today = now.Date;
            var weekAgo = now.AddDays(-7);

            var totalThreads = await db.AiAssistantThreads.CountAsync(ct);
            var activeToday = await db.AiAssistantThreads
                .CountAsync(t => t.UpdatedAt >= today, ct);
            var totalMessages = await db.AiAssistantMessages.CountAsync(ct);
            var messagesToday = await db.AiAssistantMessages
                .CountAsync(m => m.CreatedAt >= today, ct);
            var uniqueUsersWeek = await db.AiAssistantThreads
                .Where(t => t.UpdatedAt >= weekAgo)
                .Select(t => t.UserId)
                .Distinct()
                .CountAsync(ct);

            return Results.Ok(new
            {
                totalThreads, activeToday, totalMessages,
                messagesToday, uniqueUsersWeek,
            });
        });

        // Safety controls
        adminGroup.MapGet("/safety", async (
            [FromServices] LearnerDbContext db,
            CancellationToken ct = default) =>
        {
            // Return current safety settings from global policy
            var policy = await db.AiGlobalPolicies.FirstOrDefaultAsync(ct);
            return Results.Ok(new
            {
                killSwitchEnabled = policy?.KillSwitchEnabled ?? false,
                requireApprovalAlways = true, // Default until admin opts out
            });
        });
    }

    private static string GetUserRole(HttpContext ctx)
    {
        var user = ctx.User;
        if (user.IsInRole("admin") || user.IsInRole("system_admin")) return "admin";
        if (user.IsInRole("expert")) return "expert";
        return "learner";
    }
}

public sealed record CreateAiThreadRequest(string? Title);
