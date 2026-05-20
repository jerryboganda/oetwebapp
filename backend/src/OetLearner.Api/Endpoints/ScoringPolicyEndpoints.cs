using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// CRUD for the singleton scoring-policy document. Admins edit; learners
/// read the active version on their dashboard "How am I graded?" card.
/// </summary>
public static class ScoringPolicyEndpoints
{
    public static IEndpointRouteBuilder MapScoringPolicyEndpoints(this IEndpointRouteBuilder app)
    {
        // Admin (read + write).
        var admin = app.MapGroup("/v1/admin/scoring-policy")
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUser");

        admin.MapGet("", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var active = await db.ScoringPolicies.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(ct);
            return Results.Ok(active is null ? null : Project(active));
        });

        admin.MapPut("", async (
            HttpContext http,
            LearnerDbContext db,
            ScoringPolicyUpdate dto,
            CancellationToken ct) =>
        {
            var actorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";

            var policyJson = string.IsNullOrWhiteSpace(dto.PolicyJson) ? "{}" : dto.PolicyJson;
            var validationError = ScoringPolicyValidation.ValidateCanonicalPolicyJson(policyJson);
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            var supportsTransactions = !string.Equals(
                db.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.InMemory",
                StringComparison.Ordinal);
            await using var transaction = supportsTransactions ? await db.Database.BeginTransactionAsync(ct) : null;

            var existing = await db.ScoringPolicies.Where(x => x.IsActive).ToListAsync(ct);
            foreach (var row in existing) row.IsActive = false;
            if (existing.Count > 0)
            {
                await db.SaveChangesAsync(ct);
            }

            var now = DateTimeOffset.UtcNow;
            var policy = new ScoringPolicy
            {
                Id = $"scr_{Guid.NewGuid():N}",
                BodyMarkdown = dto.BodyMarkdown ?? string.Empty,
                PolicyJson = policyJson,
                IsActive = true,
                UpdatedByUserId = actorId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.ScoringPolicies.Add(policy);
            db.AuditEvents.Add(new AuditEvent
            {
                Id = $"audit-{Guid.NewGuid():N}",
                OccurredAt = now,
                ActorId = actorId,
                ActorName = http.User.Identity?.Name ?? actorId,
                Action = "ScoringPolicyPublished",
                ResourceType = "ScoringPolicy",
                ResourceId = policy.Id,
                Details = "Active scoring policy updated",
            });
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
            {
                await transaction.CommitAsync(ct);
            }
            return Results.Ok(Project(policy));
        })
        .RequireAuthorization("AdminContentPublish");

        admin.MapGet("/history", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var rows = await db.ScoringPolicies.AsNoTracking()
                .OrderByDescending(x => x.UpdatedAt)
                .Take(50)
                .ToListAsync(ct);
            return Results.Ok(rows.Select(Project));
        });

        // Learner (read-only).
        var learner = app.MapGroup("/v1/scoring-policy")
            .RequireAuthorization()
            .RequireRateLimiting("PerUser");

        learner.MapGet("", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var active = await db.ScoringPolicies.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(ct);
            return Results.Ok(active is null ? null : new
            {
                active.Id,
                active.BodyMarkdown,
                active.PolicyJson,
                active.UpdatedAt,
            });
        });

        return app;
    }

    private static object Project(ScoringPolicy p) => new
    {
        p.Id,
        p.BodyMarkdown,
        p.PolicyJson,
        p.IsActive,
        p.UpdatedByUserId,
        p.CreatedAt,
        p.UpdatedAt,
    };

}

public sealed record ScoringPolicyUpdate(string? BodyMarkdown, string? PolicyJson);
