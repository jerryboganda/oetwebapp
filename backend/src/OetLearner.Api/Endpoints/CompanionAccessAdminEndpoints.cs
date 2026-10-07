using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Operator surface for AI Learning Companion access (owner directive
/// 2026-09-07: fully admin-configurable, no catalog-manifest edit needed).
///
/// <para>
/// Companion access resolves from three durable sources, most specific first:
/// per-USER <see cref="CompanionUserAccess"/> rows (this file's
/// <c>/users/{userId}</c> endpoints, with provenance and an optional expiry), the
/// plan's catalog module list (<see cref="BillingPlan.DashboardModulesJson"/>,
/// rewritten by the catalog seeder on every boot) and per-PLAN
/// <see cref="PlanModuleOverride"/> rows (written here; never touched by the
/// seeder). The learner gate in
/// <see cref="CompanionAccessResolver"/> consults all three and reports both the
/// coarse reason and the provenance source.
/// </para>
///
/// <para>
/// Authorisation matches <see cref="CompanionKnowledgeAdminEndpoints"/>
/// (<c>AdminAiConfig</c>). Every write records an <see cref="AuditEvent"/>.
/// </para>
/// </summary>
public static class CompanionAccessAdminEndpoints
{
    public static IEndpointRouteBuilder MapCompanionAccessAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/companion/access")
            .RequireAuthorization("AdminAiConfig")
            .RequireRateLimiting("PerUser");

        // ── Per-plan effective companion state ────────────────────────────
        group.MapGet("/", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var plans = await db.BillingPlans
                .AsNoTracking()
                .Where(p => p.Status == BillingPlanStatus.Active)
                .OrderBy(p => p.DisplayOrder)
                .ThenBy(p => p.Code)
                .Select(p => new { p.Code, p.Name, p.DashboardModulesJson })
                .ToListAsync(ct);

            var overrides = await db.PlanModuleOverrides
                .AsNoTracking()
                .Where(o => o.ModuleKey == ModuleKeys.AiCompanion)
                .Select(o => new { o.PlanCode, o.Enabled, o.UpdatedByAdminId, o.UpdatedAt })
                .ToListAsync(ct);

            var items = plans.Select(plan =>
            {
                var inManifest = EffectiveEntitlementResolver.ParseDashboardModules(plan.DashboardModulesJson)
                    .Any(m => string.Equals(m, ModuleKeys.AiCompanion, StringComparison.OrdinalIgnoreCase));
                var row = overrides.FirstOrDefault(o =>
                    string.Equals(o.PlanCode, plan.Code, StringComparison.OrdinalIgnoreCase));
                var effective = row?.Enabled ?? inManifest;
                var source = row is not null ? "override" : inManifest ? "manifest" : "none";
                return new CompanionPlanAccessItem(
                    plan.Code,
                    plan.Name,
                    effective,
                    source,
                    inManifest,
                    row?.Enabled,
                    row?.UpdatedByAdminId,
                    row?.UpdatedAt);
            }).ToList();

            return Results.Ok(new { items });
        });

        // ── Grant or revoke (upsert override) ─────────────────────────────
        group.MapPost("/", async (
            CompanionPlanAccessRequest request,
            HttpContext http,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.PlanCode))
            {
                return new ApiErrorResult(400, "plan_code_required", "Plan code is required.");
            }

            var planCode = request.PlanCode.Trim();
            var planExists = await db.BillingPlans
                .AsNoTracking()
                .AnyAsync(p => p.Code.ToLower() == planCode.ToLower(), ct);
            if (!planExists)
            {
                return Results.NotFound(new { code = "unknown_plan", message = $"Plan {planCode} does not exist.", planCode });
            }

            var adminId = AdminId(http);
            var now = DateTimeOffset.UtcNow;
            var existing = await db.PlanModuleOverrides
                .FirstOrDefaultAsync(o => o.PlanCode.ToLower() == planCode.ToLower()
                    && o.ModuleKey == ModuleKeys.AiCompanion, ct);
            if (existing is null)
            {
                db.PlanModuleOverrides.Add(new PlanModuleOverride
                {
                    Id = $"pmo-{Guid.NewGuid():N}"[..32],
                    PlanCode = planCode,
                    ModuleKey = ModuleKeys.AiCompanion,
                    Enabled = request.Enabled,
                    UpdatedByAdminId = adminId,
                    UpdatedAt = now,
                });
            }
            else
            {
                existing.Enabled = request.Enabled;
                existing.UpdatedByAdminId = adminId;
                existing.UpdatedAt = now;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { planCode, enabled = request.Enabled, updatedByAdminId = adminId });
        });

        // ── Per-USER effective companion state (SAMI §9) ──────────────────────
        // Reads the SAME decision the learner sees, so an operator can answer
        // "why can/can't this person talk to Sami" without guessing at plan rows.
        group.MapGet("/users/{userId}", async (
            string userId,
            LearnerDbContext db,
            ICompanionAccessResolver access,
            CancellationToken ct) =>
        {
            var exists = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId, ct);
            if (!exists)
            {
                return Results.NotFound(new { code = "unknown_user", message = $"User {userId} does not exist.", userId });
            }

            return Results.Ok(await BuildUserAccessItemAsync(db, access, userId, ct));
        });

        // ── Per-user enable / disable (upsert, with provenance) ───────────────
        // enabled=true grants even when the package does not include the
        // companion; enabled=false denies even when the package does. Either way
        // the coarse checks underneath (account AI disable, kill switch, quota
        // plan feature allow-list) still apply — see CompanionAccessResolver.
        group.MapPost("/users/{userId}", async (
            string userId,
            CompanionUserAccessRequest request,
            HttpContext http,
            LearnerDbContext db,
            ICompanionAccessResolver access,
            CancellationToken ct) =>
        {
            if (request is null)
            {
                return new ApiErrorResult(400, "request_required", "A request body is required.");
            }

            var exists = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId, ct);
            if (!exists)
            {
                return Results.NotFound(new { code = "unknown_user", message = $"User {userId} does not exist.", userId });
            }

            var now = DateTimeOffset.UtcNow;
            var source = CompanionAccessSources.ManuallyDisabled;
            DateTimeOffset? expiresAt = null;

            if (request.Enabled)
            {
                if (!string.IsNullOrWhiteSpace(request.Source)
                    && !CompanionAccessSources.IsGrantSource(request.Source.Trim()))
                {
                    return new ApiErrorResult(
                        400,
                        "unknown_source",
                        $"source must be {CompanionAccessSources.AdminEnabled} or {CompanionAccessSources.Promotional}.");
                }

                source = CompanionAccessSources.NormalizeGrantSource(request.Source?.Trim());
                expiresAt = request.ExpiresAt?.ToUniversalTime();

                // An already-lapsed grant would be written and read back as
                // "expired" a moment later, which reads like a silent failure.
                // Reject it instead, so the operator sees why.
                if (expiresAt is { } expiry && expiry <= now)
                {
                    return new ApiErrorResult(
                        400,
                        "expiry_in_past",
                        "expiresAt must be in the future; a past expiry would take effect as expired immediately.");
                }
            }

            var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
            if (note is { Length: > 512 })
            {
                return new ApiErrorResult(400, "note_too_long", "note must be 512 characters or fewer.");
            }

            var adminId = AdminId(http);
            var row = await db.CompanionUserAccesses
                .FirstOrDefaultAsync(o => o.UserId == userId && o.ModuleKey == ModuleKeys.AiCompanion, ct);
            if (row is null)
            {
                row = new CompanionUserAccess
                {
                    Id = $"cua-{Guid.NewGuid():N}",
                    UserId = userId,
                    ModuleKey = ModuleKeys.AiCompanion,
                    CreatedAt = now,
                };
                db.CompanionUserAccesses.Add(row);
            }

            row.Enabled = request.Enabled;
            row.Source = source;
            row.ExpiresAt = expiresAt;
            row.Note = note;
            row.UpdatedByAdminId = adminId;
            row.UpdatedAt = now;

            db.AuditEvents.Add(new AuditEvent
            {
                Id = $"AUD-{Guid.NewGuid():N}",
                OccurredAt = now,
                ActorId = adminId,
                ActorAuthAccountId = adminId,
                ActorName = http.User.Identity?.Name ?? adminId,
                Action = "CompanionUserAccessUpdated",
                ResourceType = "CompanionUserAccess",
                ResourceId = row.Id,
                Details = JsonSerializer.Serialize(new
                {
                    userId,
                    enabled = request.Enabled,
                    source = row.Source,
                    expiresAt = row.ExpiresAt,
                    note = row.Note,
                }),
            });

            await db.SaveChangesAsync(ct);
            return Results.Ok(await BuildUserAccessItemAsync(db, access, userId, ct));
        });

        // ── Clear the per-user override (back to package truth) ───────────────
        group.MapDelete("/users/{userId}", async (
            string userId,
            HttpContext http,
            LearnerDbContext db,
            ICompanionAccessResolver access,
            CancellationToken ct) =>
        {
            var row = await db.CompanionUserAccesses
                .FirstOrDefaultAsync(o => o.UserId == userId && o.ModuleKey == ModuleKeys.AiCompanion, ct);
            if (row is null)
            {
                return Results.NotFound(new { code = "no_override", message = $"No per-user companion override exists for {userId}.", userId });
            }

            var adminId = AdminId(http);
            var snapshot = new { enabled = row.Enabled, source = row.Source, expiresAt = row.ExpiresAt };

            db.CompanionUserAccesses.Remove(row);
            db.AuditEvents.Add(new AuditEvent
            {
                Id = $"AUD-{Guid.NewGuid():N}",
                OccurredAt = DateTimeOffset.UtcNow,
                ActorId = adminId,
                ActorAuthAccountId = adminId,
                ActorName = http.User.Identity?.Name ?? adminId,
                Action = "CompanionUserAccessCleared",
                ResourceType = "CompanionUserAccess",
                ResourceId = row.Id,
                Details = JsonSerializer.Serialize(new { userId, previous = snapshot }),
            });

            await db.SaveChangesAsync(ct);
            return Results.Ok(await BuildUserAccessItemAsync(db, access, userId, ct));
        });

        // ── Tutor/support handoff queue (F-123) ────────────────────────────
        group.MapGet("/handoffs", async (
            OetLearner.Api.Services.Companion.ICompanionHandoffService handoffs,
            CancellationToken ct,
            string? route = null, int take = 50)
            => Results.Ok(new { rows = await handoffs.ListOpenAsync(route, Math.Clamp(take, 1, 200), ct) }));

        group.MapPost("/handoffs/{id}/status", async (
            string id,
            CompanionHandoffStatusRequest request,
            OetLearner.Api.Services.Companion.ICompanionHandoffService handoffs,
            HttpContext http,
            CancellationToken ct) =>
        {
            var updated = await handoffs.SetStatusAsync(id, request.Status, http.User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier), ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }).RequireRateLimiting("PerUserWrite");

        // ── Reset to manifest truth (delete override) ─────────────────────
        group.MapDelete("/{planCode}", async (
            string planCode,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var existing = await db.PlanModuleOverrides
                .FirstOrDefaultAsync(o => o.PlanCode.ToLower() == planCode.ToLower()
                    && o.ModuleKey == ModuleKeys.AiCompanion, ct);
            if (existing is null)
            {
                return Results.NotFound(new { code = "no_override", message = $"No AI Companion override exists for plan {planCode}.", planCode });
            }

            db.PlanModuleOverrides.Remove(existing);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { planCode, reset = true });
        });

        return app;
    }

    private static string AdminId(HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated admin id is required.");

    /// <summary>
    /// The learner's effective decision plus the raw per-user row behind it. Both
    /// halves are returned on purpose: the decision is what the learner sees, the
    /// row is what the operator set (including a note and an expiry the learner
    /// must never see).
    /// </summary>
    private static async Task<CompanionUserAccessItem> BuildUserAccessItemAsync(
        LearnerDbContext db,
        ICompanionAccessResolver access,
        string userId,
        CancellationToken ct)
    {
        var decision = await access.ResolveAsync(userId, ct);
        var row = await ReadUserOverrideAsync(db, userId, ct);

        return new CompanionUserAccessItem(
            userId,
            decision.CanChat,
            decision.Source,
            decision.Reason,
            decision.PlanCode,
            decision.PlanName,
            row?.Enabled,
            row?.Source,
            row?.ExpiresAt,
            row?.Note,
            row?.UpdatedByAdminId,
            row?.UpdatedAt);
    }

    /// <summary>
    /// The raw override row, projected (never the whole entity) and anonymous-typed
    /// first so the query translates on every provider. A read failure is NOT
    /// swallowed here — this is an operator read: reporting an override that may not
    /// exist would be worse than an honest error. The learner gate has its own,
    /// deliberately different, fail-safe contract (see
    /// <see cref="CompanionAccessResolver"/>).
    /// </summary>
    private static async Task<CompanionUserAccessRow?> ReadUserOverrideAsync(
        LearnerDbContext db,
        string userId,
        CancellationToken ct)
    {
        var row = await db.CompanionUserAccesses
            .AsNoTracking()
            .Where(o => o.UserId == userId && o.ModuleKey == ModuleKeys.AiCompanion)
            .Select(o => new { o.Enabled, o.Source, o.ExpiresAt, o.Note, o.UpdatedByAdminId, o.UpdatedAt })
            .FirstOrDefaultAsync(ct);

        return row is null
            ? null
            : new CompanionUserAccessRow(
                row.Enabled,
                row.Source,
                row.ExpiresAt,
                row.Note,
                row.UpdatedByAdminId,
                row.UpdatedAt);
    }

    private sealed record CompanionUserAccessRow(
        bool Enabled,
        string Source,
        DateTimeOffset? ExpiresAt,
        string? Note,
        string? UpdatedByAdminId,
        DateTimeOffset UpdatedAt);
}

/// <summary>Grant (true) or revoke (false) companion access for one plan.</summary>
public sealed record CompanionPlanAccessRequest(string PlanCode, bool Enabled);

/// <summary>
/// Per-user companion override request. <paramref name="Source"/> is only read when
/// enabling and must be <c>admin_enabled</c> (default) or <c>promotional</c>;
/// <paramref name="ExpiresAt"/> is optional and must be in the future — a lapsed
/// grant stops granting but never revokes what the package grants, so it is accepted
/// as a state the gate reports, not as something an operator should be writing.
/// </summary>
public sealed record CompanionUserAccessRequest(
    bool Enabled,
    string? Source = null,
    DateTimeOffset? ExpiresAt = null,
    string? Note = null);

/// <summary>
/// One learner's effective companion state and the override behind it.
/// <see cref="Effective"/> is what the learner sees (canChat);
/// <see cref="Source"/> is the provenance (package_included, admin_enabled,
/// promotional, manually_disabled, expired, none); <see cref="Reason"/> is the
/// coarse block code, identical to <c>access.reason</c> on the learner session.
/// </summary>
public sealed record CompanionUserAccessItem(
    string UserId,
    bool Effective,
    string? Source,
    string Reason,
    string? PlanCode,
    string? PlanName,
    bool? OverrideEnabled,
    string? OverrideSource,
    DateTimeOffset? OverrideExpiresAt,
    string? OverrideNote,
    string? UpdatedByAdminId,
    DateTimeOffset? UpdatedAt);

/// <summary>One plan's effective companion access and where it comes from.</summary>
public sealed record CompanionPlanAccessItem(
    string PlanCode,
    string PlanName,
    bool Effective,
    string Source,
    bool InManifest,
    bool? OverrideEnabled,
    string? UpdatedByAdminId,
    DateTimeOffset? UpdatedAt);

public sealed record CompanionHandoffStatusRequest(string Status);

