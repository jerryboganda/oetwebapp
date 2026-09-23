using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.FreeSamples;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Free Mocks (owner 2026-09-22) — the free AI-graded Writing / Speaking sample.
///
///  • learner  GET /v1/free-samples/{subtest}: the learner's own sample (0 or 1
///    rows) — state, limit / successfulCount / remaining (two successful results
///    per subtest) and the route to open next. The client never sends a "free"
///    flag — the server decides at start/grade time (see <c>FreeSampleService</c>).
///  • admin    GET/PUT/DELETE /v1/admin/free-samples/...: optionally designate the
///    curated item of a profession (no row = the server auto-picks the
///    lowest-order live item).
/// </summary>
public static class FreeSampleEndpoints
{
    public static IEndpointRouteBuilder MapFreeSampleEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/v1/free-samples")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser")
            .WithTags("FreeSamples");

        learner.MapGet("/{subtest}", async (
            string subtest,
            HttpContext http,
            IFreeSampleService service,
            CancellationToken ct) =>
        {
            var key = NormalizeSubtest(subtest);
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw ApiException.Unauthorized("unauthorized", "Sign in to continue.");
            var offers = await service.ListAsync(userId, key, ct);
            return Results.Ok(offers.Select(o => new
            {
                professionId = o.ProfessionId,
                contentId = o.ContentId,
                state = o.State,
                route = o.Route,
                limit = o.Limit,
                successfulCount = o.SuccessfulCount,
                remaining = o.Remaining,
                lastResultRoute = o.LastResultRoute,
                lastSubmissionId = o.LastSubmissionId,
            }));
        })
        .WithName("ListFreeSamples");

        var admin = app.MapGroup("/v1/admin/free-samples")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser")
            .WithTags("FreeSamplesAdmin");

        admin.MapGet("/{subtest}", async (
            string subtest,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var key = NormalizeSubtest(subtest);
            var rows = await db.FreeSampleDesignations.AsNoTracking()
                .Where(d => d.Subtest == key)
                .OrderBy(d => d.Profession)
                .Select(d => new { professionId = d.Profession, contentId = d.ContentId, d.UpdatedAt, d.UpdatedByAdminId })
                .ToListAsync(ct);
            return Results.Ok(rows);
        })
        .WithAdminRead("AdminContentRead")
        .WithName("ListFreeSampleDesignations");

        admin.MapPut("/{subtest}/{professionId}", async (
            string subtest,
            string professionId,
            FreeSampleDesignationRequest request,
            HttpContext http,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var key = NormalizeSubtest(subtest);
            var profession = FreeSampleService.NormalizeProfession(professionId);
            if (string.IsNullOrWhiteSpace(profession) || profession.Length > 32)
            {
                throw ApiException.Validation("free_sample_profession_invalid", "A valid profession id is required.");
            }
            var contentId = (request.ContentId ?? string.Empty).Trim();
            if (contentId.Length is 0 or > 64)
            {
                throw ApiException.Validation("free_sample_content_invalid", "contentId is required.");
            }

            // The designated item must exist AND belong to that profession, so a
            // typo cannot hand another profession's content to a learner.
            var itemProfession = key == FreeSampleService.Writing
                ? Guid.TryParse(contentId, out var scenarioId)
                    ? await db.WritingScenarios.AsNoTracking().Where(s => s.Id == scenarioId).Select(s => s.Profession).FirstOrDefaultAsync(ct)
                    : null
                : await db.RolePlayCards.AsNoTracking().Where(c => c.Id == contentId).Select(c => c.ProfessionId).FirstOrDefaultAsync(ct);
            if (itemProfession is null || FreeSampleService.NormalizeProfession(itemProfession) != profession)
            {
                throw ApiException.Validation(
                    "free_sample_content_mismatch",
                    "That item does not exist for this subtest and profession.");
            }
            if (key == FreeSampleService.Writing) contentId = Guid.Parse(contentId).ToString("D");

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var row = await db.FreeSampleDesignations.FirstOrDefaultAsync(d => d.Subtest == key && d.Profession == profession, ct);
            var created = row is null;
            row ??= new FreeSampleDesignation { Id = $"fsd-{Guid.NewGuid():N}", Subtest = key, Profession = profession };
            row.ContentId = contentId;
            row.UpdatedByAdminId = adminId;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            if (created) db.FreeSampleDesignations.Add(row);
            db.AuditEvents.Add(Audit(adminId, created ? "FreeSampleDesignationCreated" : "FreeSampleDesignationUpdated", row.Id, $"{key}/{profession} -> {contentId}"));
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { professionId = row.Profession, contentId = row.ContentId, row.UpdatedAt });
        })
        .WithAdminWrite("AdminContentWrite")
        .WithName("UpsertFreeSampleDesignation");

        admin.MapDelete("/{subtest}/{professionId}", async (
            string subtest,
            string professionId,
            HttpContext http,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var key = NormalizeSubtest(subtest);
            var profession = FreeSampleService.NormalizeProfession(professionId);
            var row = await db.FreeSampleDesignations.FirstOrDefaultAsync(d => d.Subtest == key && d.Profession == profession, ct);
            if (row is null) return Results.NoContent();

            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            db.FreeSampleDesignations.Remove(row);
            db.AuditEvents.Add(Audit(adminId, "FreeSampleDesignationRemoved", row.Id, $"{key}/{profession}"));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithAdminWrite("AdminContentWrite")
        .WithName("RemoveFreeSampleDesignation");

        return app;
    }

    private static string NormalizeSubtest(string subtest)
    {
        var key = (subtest ?? string.Empty).Trim().ToLowerInvariant();
        if (!FreeSampleService.IsSupported(key))
        {
            throw ApiException.NotFound("free_sample_subtest_unknown", "Free samples exist for writing and speaking only.");
        }
        return key;
    }

    private static AuditEvent Audit(string adminId, string action, string resourceId, string details) => new()
    {
        Id = $"AUD-{Guid.NewGuid():N}",
        OccurredAt = DateTimeOffset.UtcNow,
        ActorId = adminId,
        ActorName = adminId,
        Action = action,
        ResourceType = "FreeSampleDesignation",
        ResourceId = resourceId,
        Details = details,
    };
}

public sealed record FreeSampleDesignationRequest(string? ContentId);
