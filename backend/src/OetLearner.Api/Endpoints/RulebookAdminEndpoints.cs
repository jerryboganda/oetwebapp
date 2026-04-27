using System.Security.Claims;
using OetLearner.Api.Contracts.Rulebooks;
using OetLearner.Api.Services.Rulebooks;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin-only CRUD endpoints for the DB-managed rulebooks.
/// Mounted at /v1/admin/rulebooks. The public read of the canonical rulebook
/// (used by the AI gateway + grading engines) still lives in
/// <see cref="RulebookEndpoints"/> and reads the embedded JSON; admin changes
/// in DB are exposed back to the runtime via <c>RulebookAdminService.GetAsync</c>
/// for future runtime overlay (out of scope for this slice).
/// </summary>
public static class RulebookAdminEndpoints
{
    public static IEndpointRouteBuilder MapRulebookAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/rulebooks")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        // Static metadata for admin UI dropdowns (kinds, professions, severities, statuses).
        group.MapGet("/_metadata", (RulebookAdminService svc)
                => Results.Ok(svc.GetMetadata()))
            .WithAdminRead("AdminContentRead");

        // List versions (filterable).
        group.MapGet("", async (string? kind, string? profession, RulebookAdminService svc, CancellationToken ct)
                => Results.Ok(await svc.ListAsync(kind, profession, ct)))
            .WithAdminRead("AdminContentRead");

        // Detail (sections + rules embedded).
        group.MapGet("/{id}", async (string id, RulebookAdminService svc, CancellationToken ct) =>
            {
                var dto = await svc.GetAsync(id, ct);
                return dto is null ? Results.NotFound() : Results.Ok(dto);
            })
            .WithAdminRead("AdminContentRead");

        // Export rulebook as canonical JSON.
        group.MapGet("/{id}/export", async (string id, RulebookAdminService svc, CancellationToken ct)
                => Results.Ok(await svc.ExportAsync(id, ct)))
            .WithAdminRead("AdminContentRead");

        // Create new rulebook (Draft).
        group.MapPost("", async (CreateRulebookRequest req, RulebookAdminService svc, HttpContext http, CancellationToken ct)
                => Results.Ok(await svc.CreateAsync(req, AdminId(http), ct)))
            .WithAdminWrite("AdminContentWrite");

        // Clone an existing rulebook into a new Draft.
        group.MapPost("/{id}/clone", async (string id, CloneRulebookRequest req, RulebookAdminService svc, HttpContext http, CancellationToken ct)
                => Results.Ok(await svc.CloneAsync(id, req, AdminId(http), ct)))
            .WithAdminWrite("AdminContentWrite");

        // Import rulebook from JSON (create | replace).
        group.MapPost("/import", async (ImportRulebookRequest req, RulebookAdminService svc, HttpContext http, CancellationToken ct)
                => Results.Ok(await svc.ImportAsync(req, AdminId(http), ct)))
            .WithAdminWrite("AdminContentWrite");

        // Update meta (version label, authority source).
        group.MapPut("/{id}", async (string id, UpdateRulebookMetaRequest req, RulebookAdminService svc, HttpContext http, CancellationToken ct)
                => Results.Ok(await svc.UpdateMetaAsync(id, req, AdminId(http), ct)))
            .WithAdminWrite("AdminContentWrite");

        // Publish (promote to current; archive prior published row).
        group.MapPost("/{id}/publish", async (string id, PublishRulebookRequest? req, RulebookAdminService svc, HttpContext http, CancellationToken ct)
                => Results.Ok(await svc.PublishAsync(id, req ?? new PublishRulebookRequest(null), AdminId(http), ct)))
            .WithAdminWrite("AdminContentPublish");

        // Unpublish (Published → Draft).
        group.MapPost("/{id}/unpublish", async (string id, RulebookAdminService svc, HttpContext http, CancellationToken ct)
                => Results.Ok(await svc.UnpublishAsync(id, AdminId(http), ct)))
            .WithAdminWrite("AdminContentPublish");

        // Delete a Draft or Archived rulebook.
        group.MapDelete("/{id}", async (string id, RulebookAdminService svc, HttpContext http, CancellationToken ct) =>
            {
                await svc.DeleteAsync(id, AdminId(http), ct);
                return Results.NoContent();
            })
            .WithAdminWrite("AdminContentWrite");

        // Section CRUD.
        group.MapPost("/{id}/sections", async (string id, CreateSectionRequest req, RulebookAdminService svc, HttpContext http, CancellationToken ct)
                => Results.Ok(await svc.CreateSectionAsync(id, req, AdminId(http), ct)))
            .WithAdminWrite("AdminContentWrite");

        group.MapPut("/{id}/sections/{sectionId}", async (string id, string sectionId, UpdateSectionRequest req, RulebookAdminService svc, HttpContext http, CancellationToken ct)
                => Results.Ok(await svc.UpdateSectionAsync(id, sectionId, req, AdminId(http), ct)))
            .WithAdminWrite("AdminContentWrite");

        group.MapDelete("/{id}/sections/{sectionId}", async (string id, string sectionId, RulebookAdminService svc, HttpContext http, CancellationToken ct) =>
            {
                await svc.DeleteSectionAsync(id, sectionId, AdminId(http), ct);
                return Results.NoContent();
            })
            .WithAdminWrite("AdminContentWrite");

        // Rule CRUD.
        group.MapPost("/{id}/rules", async (string id, CreateRuleRequest req, RulebookAdminService svc, HttpContext http, CancellationToken ct)
                => Results.Ok(await svc.CreateRuleAsync(id, req, AdminId(http), ct)))
            .WithAdminWrite("AdminContentWrite");

        group.MapPut("/{id}/rules/{ruleId}", async (string id, string ruleId, UpdateRuleRequest req, RulebookAdminService svc, HttpContext http, CancellationToken ct)
                => Results.Ok(await svc.UpdateRuleAsync(id, ruleId, req, AdminId(http), ct)))
            .WithAdminWrite("AdminContentWrite");

        group.MapDelete("/{id}/rules/{ruleId}", async (string id, string ruleId, RulebookAdminService svc, HttpContext http, CancellationToken ct) =>
            {
                await svc.DeleteRuleAsync(id, ruleId, AdminId(http), ct);
                return Results.NoContent();
            })
            .WithAdminWrite("AdminContentWrite");

        return app;
    }

    private static string AdminId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
}
