using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Contracts.AiAssistant;
using OetLearner.Api.Data;
using OetLearner.Api.Domain.AiAssistant;
using OetLearner.Api.Services.AiAssistant;
using OetLearner.Api.Services.AiAssistant.Permissions;

namespace OetLearner.Api.Endpoints;

public static class AiAssistantChatEndpoints
{
    public static IEndpointRouteBuilder MapAiAssistantChat(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/ai-assistant")
            .RequireAuthorization("AdminAiAssistantUse");

        g.MapPost("/threads", async (OetLearner.Api.Contracts.AiAssistant.CreateThreadRequest req, HttpContext http, LearnerDbContext db) =>
        {
            var userId = ResolveUserId(http);
            var now = DateTimeOffset.UtcNow;
            var thread = new AiChatThread
            {
                Id = Guid.NewGuid(),
                OwnerUserId = userId,
                Title = string.IsNullOrWhiteSpace(req.Title) ? "New conversation" : req.Title!.Trim(),
                ProviderConfigId = null,
                Model = null,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.AiChatThreads.Add(thread);
            db.AiAuditEvents.Add(new AiAuditEvent
            {
                Id = Guid.NewGuid(),
                ActorUserId = userId,
                Action = AiAuditAction.ThreadCreated,
                MetadataJson = $"{{\"threadId\":\"{thread.Id}\"}}",
                IpAddress = http.Connection.RemoteIpAddress?.ToString(),
                OccurredAt = now,
            });
            await db.SaveChangesAsync(http.RequestAborted);
            return Results.Created($"/v1/ai-assistant/threads/{thread.Id}", ToDto(thread, 0));
        }).RequireRateLimiting("PerUserWrite");

        g.MapGet("/threads", async (HttpContext http, LearnerDbContext db, int? take, int? skip) =>
        {
            var userId = ResolveUserId(http);
            var query = db.AiChatThreads
                .AsNoTracking()
                .Where(t => t.OwnerUserId == userId && !t.IsArchived)
                .OrderByDescending(t => t.UpdatedAt);
            var rows = await query
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 50, 1, 200))
                .Select(t => new ChatThreadDto
                {
                    Id = t.Id,
                    Title = t.Title,
                    ProviderConfigId = t.ProviderConfigId,
                    Model = t.Model,
                    IsArchived = t.IsArchived,
                    CreatedAt = t.CreatedAt,
                    UpdatedAt = t.UpdatedAt,
                    MessageCount = t.Messages.Count,
                })
                .ToListAsync(http.RequestAborted);
            return Results.Ok(rows);
        });

        g.MapGet("/threads/{id:guid}", async (Guid id, HttpContext http, LearnerDbContext db) =>
        {
            var userId = ResolveUserId(http);
            var thread = await db.AiChatThreads
                .AsNoTracking()
                .SingleOrDefaultAsync(t => t.Id == id && t.OwnerUserId == userId, http.RequestAborted);
            if (thread is null) return Results.NotFound();
            var messages = await db.AiChatMessages
                .AsNoTracking()
                .Where(m => m.ThreadId == id)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new ChatMessageDto
                {
                    Id = m.Id,
                    ThreadId = m.ThreadId,
                    Role = m.Role.ToString().ToLowerInvariant(),
                    Content = m.Content,
                    ToolPayloadJson = m.ToolPayloadJson,
                    PromptTokens = m.PromptTokens,
                    CompletionTokens = m.CompletionTokens,
                    CreatedAt = m.CreatedAt,
                })
                .ToListAsync(http.RequestAborted);
            return Results.Ok(new
            {
                thread = ToDto(thread, messages.Count),
                messages,
            });
        });

        g.MapDelete("/threads/{id:guid}", async (Guid id, HttpContext http, LearnerDbContext db) =>
        {
            var userId = ResolveUserId(http);
            var thread = await db.AiChatThreads
                .SingleOrDefaultAsync(t => t.Id == id && t.OwnerUserId == userId, http.RequestAborted);
            if (thread is null) return Results.NotFound();
            thread.IsArchived = true;
            thread.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(http.RequestAborted);
            return Results.NoContent();
        }).RequireRateLimiting("PerUserWrite");

        g.MapPost("/messages/{id:guid}/cancel", async (Guid id, HttpContext http, LearnerDbContext db, AiAssistantTurnRegistry registry) =>
        {
            var userId = ResolveUserId(http);
            var ownsMessage = await db.AiChatMessages
                .AsNoTracking()
                .AnyAsync(m => m.Id == id && m.Thread != null && m.Thread.OwnerUserId == userId, http.RequestAborted);
            if (!ownsMessage)
            {
                return Results.NotFound();
            }

            var ok = registry.TryCancel(id, userId);
            return Results.Ok(new { cancelled = ok });
        }).RequireRateLimiting("PerUserWrite");

        return app;
    }

    private static ChatThreadDto ToDto(AiChatThread t, int messageCount) => new()
    {
        Id = t.Id,
        Title = t.Title,
        ProviderConfigId = null,
        Model = null,
        IsArchived = t.IsArchived,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
        MessageCount = messageCount,
    };

    internal static Guid ResolveUserId(HttpContext http)
    {
        var raw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("no_user");
        return Guid.TryParse(raw, out var id) ? id : StableGuid(raw);
    }

    internal static Guid StableGuid(string s)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("aiasst:" + s));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
