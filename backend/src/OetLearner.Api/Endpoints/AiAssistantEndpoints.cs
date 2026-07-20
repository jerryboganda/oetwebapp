using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant;
using OetLearner.Api.Services.AiAssistant.SystemPrompts;
using OetLearner.Api.Services.Settings;

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
            .RequireAuthorization("AdminAiConfig")
            .WithTags("AI Assistant Admin");

        // Paged envelope + display fields matching the admin threads console
        // (its only consumer). It sends page/pageSize/user/role/from/to and
        // reads items/totalCount, so a bare array left the page permanently
        // empty. skip/take are still accepted for direct/API callers.
        adminGroup.MapGet("/threads", async (
            [FromServices] LearnerDbContext db,
            [FromQuery] int? skip = null,
            [FromQuery] int? take = null,
            [FromQuery] int? page = null,
            [FromQuery] int? pageSize = null,
            [FromQuery] string? userId = null,
            [FromQuery] string? user = null,
            [FromQuery] string? role = null,
            [FromQuery] DateTimeOffset? from = null,
            [FromQuery] DateTimeOffset? to = null,
            CancellationToken ct = default) =>
        {
            var size = Math.Clamp(pageSize ?? take ?? 20, 1, 200);
            var pageNumber = Math.Max(page ?? 1, 1);
            var offset = skip ?? (pageNumber - 1) * size;

            var query = db.AiAssistantThreads.AsQueryable();
            var userFilter = string.IsNullOrWhiteSpace(userId) ? user : userId;
            if (!string.IsNullOrWhiteSpace(userFilter))
                query = query.Where(t => t.UserId == userFilter);
            if (!string.IsNullOrWhiteSpace(role))
                query = query.Where(t => t.Role == role);
            if (from is not null)
                query = query.Where(t => t.UpdatedAt >= from);
            if (to is not null)
                query = query.Where(t => t.UpdatedAt <= to);

            var totalCount = await query.CountAsync(ct);
            var threads = await query
                .OrderByDescending(t => t.UpdatedAt)
                .Skip(offset).Take(size)
                .Select(t => new
                {
                    t.Id, t.UserId, t.Role, t.Title,
                    t.IsArchived, t.CreatedAt, t.UpdatedAt,
                    MessageCount = db.AiAssistantMessages.Count(m => m.ThreadId == t.Id)
                })
                .ToListAsync(ct);

            var names = await ResolveUserDisplayAsync(db, threads.Select(t => t.UserId), ct);

            return Results.Ok(new
            {
                items = threads.Select(t => new
                {
                    id = t.Id,
                    userId = t.UserId,
                    userName = names.TryGetValue(t.UserId, out var display) ? display.Name : t.UserId,
                    userRole = t.Role,
                    title = t.Title,
                    messageCount = t.MessageCount,
                    createdAt = t.CreatedAt,
                    lastActiveAt = t.UpdatedAt,
                    status = t.IsArchived ? "archived" : "active",
                }),
                totalCount,
                page = pageNumber,
                pageSize = size,
            });
        });

        // Thread detail — the console links to it from every row; without it
        // the detail page could never load.
        adminGroup.MapGet("/threads/{threadId}", async (
            string threadId,
            [FromServices] LearnerDbContext db,
            CancellationToken ct = default) =>
        {
            var thread = await db.AiAssistantThreads.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == threadId, ct);
            if (thread is null) return Results.NotFound();

            var names = await ResolveUserDisplayAsync(db, [thread.UserId], ct);
            var display = names.TryGetValue(thread.UserId, out var found) ? found : (Name: thread.UserId, Email: string.Empty);

            return Results.Ok(new
            {
                id = thread.Id,
                userId = thread.UserId,
                userName = display.Name,
                userEmail = display.Email,
                userRole = thread.Role,
                title = thread.Title,
                status = thread.IsArchived ? "archived" : "active",
                createdAt = thread.CreatedAt,
                lastActiveAt = thread.UpdatedAt,
                messageCount = await db.AiAssistantMessages.CountAsync(m => m.ThreadId == threadId, ct),
            });
        });

        adminGroup.MapGet("/threads/{threadId}/messages", async (
            string threadId,
            [FromServices] LearnerDbContext db,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100,
            CancellationToken ct = default) =>
        {
            var messages = await db.AiAssistantMessages
                .AsNoTracking()
                .Where(m => m.ThreadId == threadId)
                .OrderBy(m => m.CreatedAt)
                .Skip(skip).Take(take)
                .ToListAsync(ct);

            // The console reads `{ items: [...] }`; a bare array rendered nothing.
            return Results.Ok(new
            {
                items = messages.Select(m => new
                {
                    id = m.Id,
                    role = m.Role,
                    content = m.Content ?? string.Empty,
                    createdAt = m.CreatedAt,
                    toolCalls = ParseToolCalls(m.ToolCallsJson),
                }),
            });
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

        // Admin thread deletion. Mirrors the learner-scoped delete above
        // (soft archive — messages are retained for audit) but deliberately
        // omits the ownership filter: admins may archive any user's thread.
        // Authorisation is still enforced by the "AdminAiConfig" policy on
        // adminGroup.
        adminGroup.MapDelete("/threads/{threadId}", async (
            string threadId,
            [FromServices] LearnerDbContext db,
            CancellationToken ct = default) =>
        {
            var thread = await db.AiAssistantThreads
                .FirstOrDefaultAsync(t => t.Id == threadId, ct);
            if (thread == null) return Results.NotFound();

            // Idempotent: archiving an already-archived thread is a no-op.
            if (!thread.IsArchived)
            {
                thread.IsArchived = true;
                thread.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            return Results.NoContent();
        });

        // Engagement / cost / reliability analytics for the assistant.
        // Aggregated from real rows: AiAssistantThreads, AiAssistantMessages,
        // AiUsageRecords and AiToolInvocations (the latter two filtered to the
        // three ai_assistant.* feature codes so unrelated AI traffic — writing
        // grading, speaking, etc. — never pollutes these numbers).
        adminGroup.MapGet("/stats/analytics", async (
            [FromServices] LearnerDbContext db,
            [FromQuery] int days = 30,
            CancellationToken ct = default) =>
        {
            var windowDays = Math.Clamp(days, 1, 365);
            var now = DateTimeOffset.UtcNow;
            var firstDay = now.UtcDateTime.Date.AddDays(-(windowDays - 1));
            var windowStart = new DateTimeOffset(firstDay, TimeSpan.Zero);
            var weekAgo = now.AddDays(-7);

            // Bucket message timestamps in memory rather than relying on
            // provider-specific date-part translation for DateTimeOffset.
            var messageTimestamps = await db.AiAssistantMessages
                .AsNoTracking()
                .Where(m => m.CreatedAt >= windowStart)
                .Select(m => m.CreatedAt)
                .ToListAsync(ct);

            var countsByDay = messageTimestamps
                .GroupBy(ts => ts.UtcDateTime.Date)
                .ToDictionary(g => g.Key, g => g.Count());

            // Dense series: every day in the window, zero-filled, so the chart
            // renders a stable 30-bar axis instead of collapsing empty days.
            var messagesPerDay = Enumerable.Range(0, windowDays)
                .Select(offset =>
                {
                    var day = firstDay.AddDays(offset);
                    return new
                    {
                        date = day.ToString("yyyy-MM-dd"),
                        count = countsByDay.TryGetValue(day, out var c) ? c : 0,
                    };
                })
                .ToArray();

            var usageInWindow = db.AiUsageRecords
                .AsNoTracking()
                .Where(u => u.CreatedAt >= windowStart
                            && AssistantFeatureCodes.Contains(u.FeatureCode));

            var totalRequests = await usageInWindow.CountAsync(ct);
            var totalErrors = await usageInWindow
                .CountAsync(u => u.Outcome != AiCallOutcome.Success, ct);

            // Latency is only meaningful for calls that actually reached a
            // provider; refused/errored calls would drag the average to zero.
            var avgResponseTimeMs = await usageInWindow
                .Where(u => u.Outcome == AiCallOutcome.Success)
                .Select(u => (double?)u.LatencyMs)
                .AverageAsync(ct) ?? 0d;

            var tokenCostEstimate = await usageInWindow
                .Select(u => (decimal?)u.CostEstimateUsd)
                .SumAsync(ct) ?? 0m;

            var toolUsageRaw = await db.AiToolInvocations
                .AsNoTracking()
                .Where(i => i.CreatedAt >= windowStart
                            && AssistantFeatureCodes.Contains(i.FeatureCode))
                .GroupBy(i => i.ToolCode)
                .Select(g => new
                {
                    tool = g.Key,
                    callCount = g.Count(),
                    avgDurationMs = g.Average(x => x.LatencyMs),
                    errorCount = g.Count(x => x.Outcome != AiToolOutcome.Success),
                })
                .ToListAsync(ct);

            var toolUsage = toolUsageRaw
                .Select(t => new
                {
                    t.tool,
                    t.callCount,
                    avgDurationMs = (int)Math.Round(t.avgDurationMs),
                    t.errorCount,
                })
                .ToArray();

            // Engagement ratios are lifetime figures (the UI labels them
            // "Avg Threads per User" / "Avg Messages per Thread", not windowed).
            var totalThreads = await db.AiAssistantThreads.CountAsync(ct);
            var totalMessages = await db.AiAssistantMessages.CountAsync(ct);
            var totalUsers = await db.AiAssistantThreads
                .Select(t => t.UserId).Distinct().CountAsync(ct);
            var activeUsers7d = await db.AiAssistantThreads
                .Where(t => t.UpdatedAt >= weekAgo)
                .Select(t => t.UserId).Distinct().CountAsync(ct);

            return Results.Ok(new
            {
                messagesPerDay,
                avgResponseTimeMs = (int)Math.Round(avgResponseTimeMs),
                toolUsage,
                errorRate = totalRequests > 0 ? (double)totalErrors / totalRequests : 0d,
                totalErrors,
                totalRequests,
                tokenCostEstimate = (double)tokenCostEstimate,
                threadsPerUser = totalUsers > 0 ? (double)totalThreads / totalUsers : 0d,
                messagesPerThread = totalThreads > 0 ? (double)totalMessages / totalThreads : 0d,
                activeUsers7d,
                totalUsers,
            });
        });

        // Effective per-role assistant configuration, assembled from the
        // sources that actually drive runtime behaviour:
        //   model        → AiFeatureRoute.Model for the role's feature code
        //   enabledTools → active AiFeatureToolGrant rows for that feature code
        //   maxIterations→ the DB-over-env runtime setting the ReAct loop reads
        //   systemPrompt → ISystemPromptProvider (compiled-in prompt text)
        //   maxTokensPerRequest → AiAssistantConfiguration.RoleDefaults
        // Fields with no backing store anywhere (maxTokensPerDay,
        // rateLimitPerMinute, rateLimitPerHour) are reported as 0 rather than
        // invented — see the POST handler below.
        adminGroup.MapGet("/config", async (
            [FromServices] LearnerDbContext db,
            [FromServices] ISystemPromptProvider systemPrompts,
            [FromServices] IRuntimeSettingsProvider settingsProvider,
            CancellationToken ct = default) =>
        {
            var effectiveMaxIterations = (await settingsProvider.GetAsync(ct)).AiAssistant.MaxIterations;

            var routes = await db.AiFeatureRoutes
                .AsNoTracking()
                .Where(r => AssistantFeatureCodes.Contains(r.FeatureCode))
                .ToListAsync(ct);

            var grants = await db.AiFeatureToolGrants
                .AsNoTracking()
                .Where(g => g.IsActive && AssistantFeatureCodes.Contains(g.FeatureCode))
                .Select(g => new { g.FeatureCode, g.ToolCode })
                .ToListAsync(ct);

            var catalogTools = await db.AiTools
                .AsNoTracking()
                .Where(t => t.IsActive)
                .Select(t => t.Code)
                .ToListAsync(ct);

            var providerModels = await db.AiProviders
                .AsNoTracking()
                .Where(p => p.IsActive && p.Category == AiProviderCategory.TextChat)
                .Select(p => p.DefaultModel)
                .ToListAsync(ct);

            var roles = AssistantRoles.Select(role =>
            {
                var featureCode = AssistantFeatureCodeForRole(role);
                var defaults = AiAssistantConfiguration.RoleDefaults[role];
                var routedModel = routes
                    .FirstOrDefault(r => r.FeatureCode == featureCode && r.IsActive)?.Model;

                return new
                {
                    role,
                    model = string.IsNullOrWhiteSpace(routedModel) ? defaults.DefaultModel : routedModel,
                    maxTokensPerRequest = defaults.MaxTokens,
                    maxTokensPerDay = 0,
                    systemPrompt = systemPrompts.GetSystemPrompt(role, "{userId}"),
                    maxIterations = effectiveMaxIterations,
                    enabledTools = grants
                        .Where(g => g.FeatureCode == featureCode)
                        .Select(g => g.ToolCode)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    rateLimitPerMinute = 0,
                    rateLimitPerHour = 0,
                };
            }).ToArray();

            // Union in the currently-routed and compiled-in defaults so the
            // <select> always contains the value it is asked to display.
            var availableModels = providerModels
                .Concat(routes.Select(r => r.Model ?? string.Empty))
                .Concat(AiAssistantConfiguration.RoleDefaults.Values.Select(d => d.DefaultModel))
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var availableTools = catalogTools
                .Concat(grants.Select(g => g.ToolCode))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Results.Ok(new { roles, availableModels, availableTools });
        });

        // NOT IMPLEMENTED BY DESIGN.
        //
        // Six of the eight per-role fields the admin UI edits have no backing
        // store in this schema and cannot be added without an EF migration:
        //   maxTokensPerRequest, maxTokensPerDay, systemPrompt, maxIterations
        //   (per-role; only a single global column exists),
        //   rateLimitPerMinute, rateLimitPerHour.
        //
        // Returning 200 here would make the UI report "saved successfully"
        // while silently discarding token caps and rate limits — i.e. an admin
        // could believe they had capped spend or throttled abuse when they had
        // not. That failure mode is worse than an honest error, so this route
        // refuses until the storage exists. Wire it up in the same change that
        // adds the columns.
        // Persists only the two settings that have real storage: the per-role
        // model (AiFeatureRoute.Model) and tool access (AiFeatureToolGrant).
        // The remaining fields the editor renders — maxTokensPerRequest,
        // maxTokensPerDay, systemPrompt, per-role maxIterations, and the rate
        // limits — have no column in this schema, so they are reported back as
        // `ignoredFields` rather than silently dropped. Silently accepting a
        // spend cap or rate limit that is not stored would be worse than
        // refusing it.
        adminGroup.MapPost("/config", async (
            [FromBody] AssistantConfigSaveRequest request,
            HttpContext http,
            [FromServices] LearnerDbContext db,
            CancellationToken ct = default) =>
        {
            if (request?.Roles is null || request.Roles.Count == 0)
            {
                return Results.BadRequest(new { error = "At least one role configuration is required." });
            }

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var now = DateTimeOffset.UtcNow;
            var applied = new List<string>();

            foreach (var roleConfig in request.Roles)
            {
                var role = (roleConfig.Role ?? string.Empty).Trim().ToLowerInvariant();
                if (!AssistantRoles.Contains(role))
                {
                    return Results.BadRequest(new { error = $"Unknown assistant role '{roleConfig.Role}'." });
                }

                var featureCode = AssistantFeatureCodeForRole(role);

                if (!string.IsNullOrWhiteSpace(roleConfig.Model))
                {
                    var route = await db.AiFeatureRoutes
                        .FirstOrDefaultAsync(r => r.FeatureCode == featureCode && r.IsActive, ct);
                    if (route is not null && !string.Equals(route.Model, roleConfig.Model, StringComparison.Ordinal))
                    {
                        route.Model = roleConfig.Model!.Trim();
                        route.UpdatedAt = now;
                        route.UpdatedByAdminId = adminId;
                        applied.Add($"{role}.model");
                    }
                }

                if (roleConfig.EnabledTools is not null)
                {
                    var desired = roleConfig.EnabledTools
                        .Where(code => !string.IsNullOrWhiteSpace(code))
                        .Select(code => code.Trim())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var existing = await db.AiFeatureToolGrants
                        .Where(g => g.FeatureCode == featureCode)
                        .ToListAsync(ct);

                    var changed = false;
                    foreach (var grant in existing)
                    {
                        var shouldBeActive = desired.Contains(grant.ToolCode);
                        if (grant.IsActive != shouldBeActive)
                        {
                            grant.IsActive = shouldBeActive;
                            grant.UpdatedAt = now;
                            grant.UpdatedByAdminId = adminId;
                            changed = true;
                        }
                        desired.Remove(grant.ToolCode);
                    }

                    foreach (var toolCode in desired)
                    {
                        db.AiFeatureToolGrants.Add(new AiFeatureToolGrant
                        {
                            Id = $"aftg-{Guid.NewGuid():N}",
                            FeatureCode = featureCode,
                            ToolCode = toolCode,
                            IsActive = true,
                            CreatedAt = now,
                            UpdatedAt = now,
                            UpdatedByAdminId = adminId,
                        });
                        changed = true;
                    }

                    if (changed)
                    {
                        applied.Add($"{role}.enabledTools");
                    }
                }
            }

            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                applied = applied.ToArray(),
                ignoredFields = UnstorableAssistantConfigFields,
                message = applied.Count == 0
                    ? "No changes to apply."
                    : $"Saved {applied.Count} change(s). Token limits, system prompt, per-role iterations and rate limits are not stored in this schema and were not applied.",
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

    /// <summary>Roles the assistant exposes, in admin-UI display order.</summary>
    private static readonly string[] AssistantRoles = ["admin", "expert", "learner"];

    /// <summary>
    /// Fields the config editor renders that have no column in this schema.
    /// Returned to the caller on save so the UI can say plainly what was not
    /// persisted instead of implying a successful write.
    /// </summary>
    private static readonly string[] UnstorableAssistantConfigFields =
        ["maxTokensPerRequest", "maxTokensPerDay", "systemPrompt", "maxIterations", "rateLimitPerMinute", "rateLimitPerHour"];

    /// <summary>
    /// The three gateway feature codes that belong to the AI Assistant. Used to
    /// scope usage/tool telemetry so unrelated AI features are excluded.
    /// </summary>
    private static readonly string[] AssistantFeatureCodes =
    [
        AiFeatureCodes.AiAssistantAdmin,
        AiFeatureCodes.AiAssistantExpert,
        AiFeatureCodes.AiAssistantLearner,
    ];

    /// <summary>
    /// Mirrors <c>AiAssistantOrchestrator.GetFeatureCode</c> so config reads
    /// resolve the same route/grant rows the runtime loop resolves.
    /// </summary>
    /// <summary>
    /// Display name/email for thread owners. Threads belong to learners
    /// (<c>Users</c>) as well as experts/admins (<c>ApplicationUserAccounts</c>),
    /// so both are consulted and the id is used as a last resort.
    /// </summary>
    private static async Task<Dictionary<string, (string Name, string Email)>> ResolveUserDisplayAsync(
        LearnerDbContext db, IEnumerable<string> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
        var resolved = new Dictionary<string, (string Name, string Email)>(StringComparer.Ordinal);
        if (ids.Count == 0) return resolved;

        var learners = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToListAsync(ct);
        foreach (var learner in learners)
        {
            resolved[learner.Id] = (learner.DisplayName, learner.Email);
        }

        var missing = ids.Where(id => !resolved.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            var accounts = await db.ApplicationUserAccounts.AsNoTracking()
                .Where(a => missing.Contains(a.Id))
                .Select(a => new { a.Id, a.Email })
                .ToListAsync(ct);
            foreach (var account in accounts)
            {
                resolved[account.Id] = (account.Email, account.Email);
            }
        }

        return resolved;
    }

    /// <summary>
    /// Best-effort parse of OpenAI-format tool calls for the admin transcript.
    /// Returns null when absent or unparseable — the field is optional in the
    /// console, so a malformed row must not fail the whole request.
    /// </summary>
    private static object[]? ParseToolCalls(string? toolCallsJson)
    {
        if (string.IsNullOrWhiteSpace(toolCallsJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(toolCallsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;

            return document.RootElement.EnumerateArray().Select(element => (object)new
            {
                id = element.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                name = element.TryGetProperty("function", out var fn) && fn.TryGetProperty("name", out var fnName)
                    ? fnName.GetString() ?? string.Empty
                    : element.TryGetProperty("name", out var direct) ? direct.GetString() ?? string.Empty : string.Empty,
                arguments = element.TryGetProperty("function", out var fn2) && fn2.TryGetProperty("arguments", out var args)
                    ? args.ToString()
                    : string.Empty,
            }).ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string AssistantFeatureCodeForRole(string role) => role switch
    {
        "admin" => AiFeatureCodes.AiAssistantAdmin,
        "expert" => AiFeatureCodes.AiAssistantExpert,
        _ => AiFeatureCodes.AiAssistantLearner,
    };

    private static string GetUserRole(HttpContext ctx)
    {
        var user = ctx.User;
        if (user.IsInRole("admin") || user.IsInRole("system_admin")) return "admin";
        if (user.IsInRole("expert")) return "expert";
        return "learner";
    }
}

public sealed record CreateAiThreadRequest(string? Title);

/// <summary>Per-role slice of the admin AI Assistant config editor.</summary>
public sealed record AssistantRoleConfigSave(
    string? Role,
    string? Model,
    List<string>? EnabledTools);

/// <summary>Body of <c>POST /v1/admin/ai-assistant/config</c>.</summary>
public sealed record AssistantConfigSaveRequest(List<AssistantRoleConfigSave>? Roles);
