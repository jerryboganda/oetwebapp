using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services.Grammar;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin Grammar v2 endpoints. See docs/GRAMMAR.md. These sit alongside the
/// legacy /v1/admin/grammar/lessons CRUD defined in AdminEndpoints.cs — the
/// legacy endpoints are preserved for backward compatibility but the UI
/// uses the v2 endpoints for topic taxonomy and full authoring.
/// </summary>
public static class AdminGrammarEndpoints
{
    public static IEndpointRouteBuilder MapAdminGrammarEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin/grammar");

        // ── Topics ───────────────────────────────────────────────────────

        admin.MapGet("/topics", async (IGrammarAuthoringService svc, [FromQuery] string? examTypeCode, [FromQuery] string? status, CancellationToken ct)
            => Results.Ok(await svc.ListTopicsAsync(examTypeCode, status, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapGet("/topics/{topicId}", async (string topicId, IGrammarAuthoringService svc, CancellationToken ct)
            => Results.Ok(await svc.GetTopicDetailAsync(topicId, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapPost("/topics", async (HttpContext http, AdminGrammarTopicCreateRequest req, IGrammarAuthoringService svc, CancellationToken ct)
            =>
            {
                var id = await svc.CreateTopicAsync(AdminHelpers.AdminId(http), AdminHelpers.AdminName(http), req, ct);
                return Results.Ok(new { id });
            })
            .RequireRateLimiting("PerUserWrite")
            .RequireAuthorization("AdminContentWrite");

        admin.MapPut("/topics/{topicId}", async (string topicId, HttpContext http, AdminGrammarTopicUpdateRequest req, IGrammarAuthoringService svc, CancellationToken ct)
            =>
            {
                await svc.UpdateTopicAsync(AdminHelpers.AdminId(http), AdminHelpers.AdminName(http), topicId, req, ct);
                return Results.Ok(new { updated = true });
            })
            .RequireRateLimiting("PerUserWrite")
            .RequireAuthorization("AdminContentWrite");

        admin.MapPost("/topics/{topicId}/archive", async (string topicId, HttpContext http, IGrammarAuthoringService svc, CancellationToken ct)
            =>
            {
                await svc.ArchiveTopicAsync(AdminHelpers.AdminId(http), AdminHelpers.AdminName(http), topicId, ct);
                return Results.Ok(new { archived = true });
            })
            .RequireRateLimiting("PerUserWrite")
            .RequireAuthorization("AdminContentWrite");

        // ── Lessons (v2 authoring) ───────────────────────────────────────

        admin.MapGet("/v2/lessons", async (IGrammarAuthoringService svc,
            [FromQuery] string? topicId, [FromQuery] string? examTypeCode, [FromQuery] string? status, [FromQuery] string? search,
            [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
            => Results.Ok(await svc.ListLessonsAsync(topicId, examTypeCode, status, search, page ?? 1, pageSize ?? 25, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapGet("/v2/lessons/{lessonId}", async (string lessonId, IGrammarAuthoringService svc, CancellationToken ct)
            => Results.Ok(await svc.GetLessonDetailAsync(lessonId, ct)))
            .RequireAuthorization("AdminContentRead");

        admin.MapPost("/v2/lessons", async (HttpContext http, AdminGrammarLessonFullCreateRequest req, IGrammarAuthoringService svc, CancellationToken ct)
            =>
            {
                var id = await svc.CreateLessonAsync(AdminHelpers.AdminId(http), AdminHelpers.AdminName(http), req, ct);
                return Results.Ok(new { id });
            })
            .RequireRateLimiting("PerUserWrite")
            .RequireAuthorization("AdminContentWrite");

        admin.MapPut("/v2/lessons/{lessonId}", async (string lessonId, HttpContext http, AdminGrammarLessonFullUpdateRequest req, IGrammarAuthoringService svc, CancellationToken ct)
            =>
            {
                await svc.UpdateLessonAsync(AdminHelpers.AdminId(http), AdminHelpers.AdminName(http), lessonId, req, ct);
                return Results.Ok(new { updated = true });
            })
            .RequireRateLimiting("PerUserWrite")
            .RequireAuthorization("AdminContentWrite");

        admin.MapPost("/v2/lessons/{lessonId}/archive", async (string lessonId, HttpContext http, IGrammarAuthoringService svc, CancellationToken ct)
            =>
            {
                await svc.ArchiveLessonAsync(AdminHelpers.AdminId(http), AdminHelpers.AdminName(http), lessonId, ct);
                return Results.Ok(new { archived = true });
            })
            .RequireRateLimiting("PerUserWrite")
            .RequireAuthorization("AdminContentWrite");

        admin.MapPost("/v2/lessons/{lessonId}/publish", async (string lessonId, HttpContext http, IGrammarService svc, CancellationToken ct)
            =>
            {
                await svc.PublishLessonAsync(lessonId, AdminHelpers.AdminId(http), AdminHelpers.AdminName(http), ct);
                return Results.Ok(new { published = true });
            })
            .RequireRateLimiting("PerUserWrite")
            .RequireAuthorization("AdminContentWrite");

        admin.MapPost("/v2/lessons/{lessonId}/unpublish", async (string lessonId, HttpContext http, IGrammarService svc, CancellationToken ct)
            =>
            {
                await svc.UnpublishLessonAsync(lessonId, AdminHelpers.AdminId(http), AdminHelpers.AdminName(http), ct);
                return Results.Ok(new { unpublished = true });
            })
            .RequireRateLimiting("PerUserWrite")
            .RequireAuthorization("AdminContentWrite");

        admin.MapGet("/v2/lessons/{lessonId}/publish-gate", async (string lessonId, IGrammarService svc, CancellationToken ct)
            => Results.Ok(await svc.EvaluatePublishGateAsync(lessonId, ct)))
            .RequireAuthorization("AdminContentRead");

        // ── AI draft generator (grounded) ────────────────────────────────

        admin.MapPost("/ai-draft", async (HttpContext http, AdminGrammarAiDraftRequest req, IGrammarDraftGenerator gen, CancellationToken ct)
            =>
            {
                var result = await gen.GenerateAsync(AdminHelpers.AdminId(http), AdminHelpers.AdminName(http), req, ct);
                return Results.Ok(result);
            })
            .RequireRateLimiting("PerUserWrite")
            .RequireAuthorization("AdminContentWrite");

        // ── Bulk import ──────────────────────────────────────────────────

        admin.MapPost("/imports", async (HttpContext http, AdminGrammarImportRequest req, IGrammarAuthoringService svc, CancellationToken ct)
            =>
            {
                var result = await svc.BulkImportAsync(AdminHelpers.AdminId(http), AdminHelpers.AdminName(http), req, ct);
                return Results.Ok(result);
            })
            .RequireRateLimiting("PerUserWrite")
            .RequireAuthorization("AdminContentWrite");

        return app;
    }
}

internal static class AdminHelpers
{
    public static string AdminId(HttpContext http)
        => http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
           ?? http.User.FindFirst("sub")?.Value
           ?? "unknown-admin";

    public static string AdminName(HttpContext http)
        => http.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
           ?? http.User.FindFirst("name")?.Value
           ?? "Admin";
}
