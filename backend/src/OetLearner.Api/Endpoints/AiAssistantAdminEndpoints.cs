using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Domain.AiAssistant;
using OetLearner.Api.Services.AiAssistant;

namespace OetLearner.Api.Endpoints;

public static class AiAssistantAdminEndpoints
{
    public static IEndpointRouteBuilder MapAiAssistantAdmin(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/admin/ai-assistant")
            .RequireAuthorization("AdminAiAssistantManage")
            .RequireRateLimiting("PerUserWrite");

        g.MapGet("/settings", (IAiAssistantSettingsService settings) =>
        {
            var s = settings.Current;
            return Results.Ok(new
            {
                globalEnabled = s.GlobalEnabled,
                requireApprovalAlways = s.RequireApprovalAlways,
                defaultProvider = s.DefaultProvider,
                defaultModel = s.DefaultModel,
                lastKillSwitchAt = settings.LastKillSwitchAt,
                lastKillSwitchActor = settings.LastKillSwitchActor,
            });
        });

        g.MapPost("/kill-switch", async (
            KillSwitchRequest req,
            HttpContext http,
            IAiAssistantSettingsService settings,
            LearnerDbContext db) =>
        {
            var userId = AiAssistantChatEndpoints.ResolveUserId(http);
            settings.SetEnabled(req.Enabled, userId.ToString());
            db.AiAuditEvents.Add(new AiAuditEvent
            {
                Id = Guid.NewGuid(),
                ActorUserId = userId,
                Action = AiAuditAction.KillSwitchToggled,
                MetadataJson = $"{{\"enabled\":{(req.Enabled ? "true" : "false")}}}",
                IpAddress = http.Connection.RemoteIpAddress?.ToString(),
                OccurredAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(http.RequestAborted);
            return Results.Ok(new { globalEnabled = settings.Current.GlobalEnabled });
        });

        g.MapGet("/threads", async (LearnerDbContext db, int? take, int? skip) =>
        {
            var rows = await db.AiChatThreads
                .AsNoTracking()
                .OrderByDescending(t => t.UpdatedAt)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 100, 1, 500))
                .Select(t => new
                {
                    id = t.Id,
                    ownerUserId = t.OwnerUserId,
                    title = t.Title,
                    isArchived = t.IsArchived,
                    createdAt = t.CreatedAt,
                    updatedAt = t.UpdatedAt,
                    messageCount = t.Messages.Count,
                })
                .ToListAsync();
            return Results.Ok(rows);
        });

        g.MapGet("/audit", async (LearnerDbContext db, int? take, int? skip) =>
        {
            var rows = await db.AiAuditEvents
                .AsNoTracking()
                .OrderByDescending(a => a.OccurredAt)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 100, 1, 500))
                .Select(a => new
                {
                    id = a.Id,
                    actorUserId = a.ActorUserId,
                    action = a.Action.ToString(),
                    metadataJson = a.MetadataJson,
                    ipAddress = a.IpAddress,
                    occurredAt = a.OccurredAt,
                })
                .ToListAsync();
            return Results.Ok(rows);
        });

        g.MapGet("/usage", async (LearnerDbContext db, int? take, int? skip) =>
        {
            var rows = await db.AiUsageRecords
                .AsNoTracking()
                .Where(u => u.FeatureCode == AiFeatureCodes.AdminAiChatbot)
                .OrderByDescending(u => u.CreatedAt)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 100, 1, 500))
                .Select(u => new
                {
                    id = u.Id,
                    userId = u.UserId,
                    model = u.Model,
                    promptTokens = u.PromptTokens,
                    completionTokens = u.CompletionTokens,
                    outcome = u.Outcome == AiCallOutcome.Success ? "success" : u.ErrorCode ?? u.Outcome.ToString().ToLowerInvariant(),
                    occurredAt = u.CreatedAt,
                })
                .ToListAsync();
            return Results.Ok(rows);
        });

        // Provider config: V1 exposes the canonical gateway provider registry (read-only).
        // CRUD remains 501; write-enabled management lives under the AI usage provider admin surface.
        g.MapGet("/providers", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var chatbotRoute = await db.AiFeatureRoutes
                .AsNoTracking()
                .Where(r => r.FeatureCode == AiFeatureCodes.AdminAiChatbot && r.IsActive)
                .OrderByDescending(r => r.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            var defaultProviderCode = chatbotRoute?.ProviderCode
                ?? await db.AiProviders
                    .AsNoTracking()
                    .Where(p => p.Category == AiProviderCategory.TextChat && p.IsActive)
                    .OrderBy(p => p.FailoverPriority)
                    .ThenBy(p => p.Code)
                    .Select(p => p.Code)
                    .FirstOrDefaultAsync(ct);
                    var chatbotRouteProviderCode = chatbotRoute?.ProviderCode;
                    var chatbotRouteModel = chatbotRoute?.Model;

            var providers = await db.AiProviders
                .AsNoTracking()
                .Where(p => p.Category == AiProviderCategory.TextChat)
                .OrderBy(p => p.FailoverPriority)
                .ThenBy(p => p.Code)
                .ToListAsync(ct);

            var rows = providers
                .Select(p => new
                {
                    id = p.Id,
                    code = p.Code,
                    kind = p.Dialect.ToString(),
                    displayName = p.Name,
                    endpoint = p.BaseUrl,
                    defaultModel = string.Equals(p.Code, chatbotRouteProviderCode, StringComparison.OrdinalIgnoreCase)
                        ? chatbotRouteModel ?? p.DefaultModel
                        : p.DefaultModel,
                    allowedModelsCsv = p.AllowedModelsCsv,
                    isEnabled = p.IsActive,
                    isDefault = string.Equals(p.Code, defaultProviderCode, StringComparison.OrdinalIgnoreCase),
                    hasSecret = !string.IsNullOrEmpty(p.ApiKeyHint) || !string.IsNullOrEmpty(p.EncryptedApiKey),
                })
                .ToArray();
            return Results.Ok(rows);
        });
        g.MapPost("/providers", () => Results.Problem("Not implemented in V1.", statusCode: StatusCodes.Status501NotImplemented));
        g.MapPut("/providers/{id:guid}", (Guid id) => Results.Problem("Not implemented in V1.", statusCode: StatusCodes.Status501NotImplemented));
        g.MapDelete("/providers/{id:guid}", (Guid id) => Results.Problem("Not implemented in V1.", statusCode: StatusCodes.Status501NotImplemented));
        g.MapGet("/indexing/status", () => Results.Ok(new { state = "not_built", indexedChunkCount = 0 }));
        g.MapPost("/indexing/reindex", () => Results.Problem("Not implemented in V1 (Phase 2).", statusCode: StatusCodes.Status501NotImplemented));
        g.MapPut("/settings", () => Results.Problem("Use /kill-switch in V1.", statusCode: StatusCodes.Status501NotImplemented));

        return app;
    }

    public sealed class KillSwitchRequest
    {
        public bool Enabled { get; set; }
    }
}
