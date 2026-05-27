using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin content management for Writing V2 — scenarios, exemplars, drills,
/// canon rules, lessons, mistakes, content audit log. All routes require the
/// existing AdminOnly authorization policy.
/// </summary>
public static class WritingAdminContentEndpoints
{
    public static IEndpointRouteBuilder MapWritingAdminContentEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin/writing")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        MapScenarioRoutes(admin.MapGroup("/scenarios"));
        MapExemplarRoutes(admin.MapGroup("/exemplars"));
        MapDrillRoutes(admin.MapGroup("/drills"));
        MapCanonRoutes(admin.MapGroup("/canon"));
        MapLessonRoutes(admin.MapGroup("/lessons"));
        MapMistakeRoutes(admin.MapGroup("/mistakes"));
        MapAuditRoutes(admin);

        return app;
    }

    private static void MapScenarioRoutes(RouteGroupBuilder g)
    {
        g.MapGet("/", async (
            [FromQuery] string? profession,
            [FromQuery] string? letterType,
            [FromQuery] string? status,
            [FromQuery] string? search,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            HttpContext http,
            IWritingScenarioService service,
            CancellationToken ct)
            => Results.Ok(await service.AdminListScenariosAsync(
                http.WritingV2UserId(),
                profession,
                letterType,
                status,
                search,
                page ?? 1,
                pageSize ?? 20,
                ct)));

        g.MapPost("/", async (
            WritingScenarioUpsertRequest request,
            HttpContext http,
            IWritingScenarioService service,
            CancellationToken ct)
            => Results.Ok(await service.AdminCreateScenarioAsync(http.WritingV2UserId(), request, ct)));

        g.MapPost("/generate", async (
            WritingScenarioGenerateRequest request,
            HttpContext http,
            IWritingScenarioGeneratorService service,
            CancellationToken ct)
            => Results.Ok(await service.GenerateScenarioAsync(http.WritingV2UserId(), request, ct)));

        g.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingScenarioService service,
            CancellationToken ct) =>
        {
            var scenario = await service.AdminGetScenarioAsync(http.WritingV2UserId(), id, ct);
            return scenario is null ? Results.NotFound() : Results.Ok(scenario);
        });

        g.MapPut("/{id:guid}", async (
            Guid id,
            WritingScenarioUpsertRequest request,
            HttpContext http,
            IWritingScenarioService service,
            CancellationToken ct) =>
        {
            var scenario = await service.AdminUpdateScenarioAsync(http.WritingV2UserId(), id, request, ct);
            return scenario is null ? Results.NotFound() : Results.Ok(scenario);
        });

        g.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingScenarioService service,
            CancellationToken ct) =>
        {
            var deleted = await service.AdminDeleteScenarioAsync(http.WritingV2UserId(), id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        g.MapPost("/{id:guid}/approve", async (
            Guid id,
            HttpContext http,
            IWritingScenarioService service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var scenario = await service.AdminApproveScenarioAsync(http.WritingV2UserId(), id, ct);
            return scenario is null ? Results.NotFound() : Results.Ok(scenario);
        });
    }

    private static void MapExemplarRoutes(RouteGroupBuilder g)
    {
        g.MapGet("/", async (
            [FromQuery] string? profession,
            [FromQuery] string? letterType,
            [FromQuery] string? status,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            HttpContext http,
            IWritingExemplarService service,
            CancellationToken ct)
            => Results.Ok(await service.AdminListExemplarsAsync(
                http.WritingV2UserId(),
                profession,
                letterType,
                status,
                page ?? 1,
                pageSize ?? 20,
                ct)));

        g.MapPost("/", async (
            WritingExemplarUpsertRequest request,
            HttpContext http,
            IWritingExemplarService service,
            CancellationToken ct)
            => Results.Ok(await service.AdminCreateExemplarAsync(http.WritingV2UserId(), request, ct)));

        g.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingExemplarService service,
            CancellationToken ct) =>
        {
            var exemplar = await service.AdminGetExemplarAsync(http.WritingV2UserId(), id, ct);
            return exemplar is null ? Results.NotFound() : Results.Ok(exemplar);
        });

        g.MapPut("/{id:guid}", async (
            Guid id,
            WritingExemplarUpsertRequest request,
            HttpContext http,
            IWritingExemplarService service,
            CancellationToken ct) =>
        {
            var exemplar = await service.AdminUpdateExemplarAsync(http.WritingV2UserId(), id, request, ct);
            return exemplar is null ? Results.NotFound() : Results.Ok(exemplar);
        });

        g.MapPost("/{id:guid}/publish", async (
            Guid id,
            HttpContext http,
            IWritingExemplarService service,
            CancellationToken ct) =>
        {
            var exemplar = await service.AdminPublishExemplarAsync(http.WritingV2UserId(), id, ct);
            return exemplar is null ? Results.NotFound() : Results.Ok(exemplar);
        });

        g.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingExemplarService service,
            CancellationToken ct) =>
        {
            var deleted = await service.AdminDeleteExemplarAsync(http.WritingV2UserId(), id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        g.MapPost("/{id:guid}/test-grade", async (
            Guid id,
            HttpContext http,
            IWritingExemplarService service,
            CancellationToken ct) =>
        {
            var result = await service.AdminTestGradeExemplarAsync(http.WritingV2UserId(), id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
    }

    private static void MapDrillRoutes(RouteGroupBuilder g)
    {
        g.MapGet("/", async (
            [FromQuery] string? drillType,
            [FromQuery] string? targetSubSkill,
            [FromQuery] string? status,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            HttpContext http,
            IWritingDrillService service,
            CancellationToken ct)
            => Results.Ok(await service.AdminListDrillsAsync(
                http.WritingV2UserId(),
                drillType,
                targetSubSkill,
                status,
                page ?? 1,
                pageSize ?? 20,
                ct)));

        g.MapPost("/", async (
            WritingDrillUpsertRequest request,
            HttpContext http,
            IWritingDrillService service,
            CancellationToken ct)
            => Results.Ok(await service.AdminCreateDrillAsync(http.WritingV2UserId(), request, ct)));

        g.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingDrillService service,
            CancellationToken ct) =>
        {
            var drill = await service.AdminGetDrillAsync(http.WritingV2UserId(), id, ct);
            return drill is null ? Results.NotFound() : Results.Ok(drill);
        });

        g.MapPut("/{id:guid}", async (
            Guid id,
            WritingDrillUpsertRequest request,
            HttpContext http,
            IWritingDrillService service,
            CancellationToken ct) =>
        {
            var drill = await service.AdminUpdateDrillAsync(http.WritingV2UserId(), id, request, ct);
            return drill is null ? Results.NotFound() : Results.Ok(drill);
        });

        g.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingDrillService service,
            CancellationToken ct) =>
        {
            var deleted = await service.AdminDeleteDrillAsync(http.WritingV2UserId(), id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        });
    }

    private static void MapCanonRoutes(RouteGroupBuilder g)
    {
        g.MapGet("/", async (
            [FromQuery] string? search,
            [FromQuery] string? severity,
            [FromQuery] string? category,
            HttpContext http,
            IWritingCanonService service,
            CancellationToken ct)
            => Results.Ok(await service.AdminListCanonRulesAsync(
                http.WritingV2UserId(),
                search,
                severity,
                category,
                ct)));

        g.MapPost("/", async (
            WritingCanonRuleUpsertRequest request,
            HttpContext http,
            IWritingCanonService service,
            CancellationToken ct)
            => Results.Ok(await service.AdminCreateCanonRuleAsync(http.WritingV2UserId(), request, ct)));

        g.MapGet("/{ruleId}", async (
            string ruleId,
            HttpContext http,
            IWritingCanonService service,
            CancellationToken ct) =>
        {
            var rule = await service.AdminGetCanonRuleAsync(http.WritingV2UserId(), ruleId, ct);
            return rule is null ? Results.NotFound() : Results.Ok(rule);
        });

        g.MapPut("/{ruleId}", async (
            string ruleId,
            WritingCanonRuleUpsertRequest request,
            HttpContext http,
            IWritingCanonService service,
            CancellationToken ct) =>
        {
            var rule = await service.AdminUpdateCanonRuleAsync(http.WritingV2UserId(), ruleId, request, ct);
            return rule is null ? Results.NotFound() : Results.Ok(rule);
        });

        g.MapDelete("/{ruleId}", async (
            string ruleId,
            HttpContext http,
            IWritingCanonService service,
            CancellationToken ct) =>
        {
            var deleted = await service.AdminDeleteCanonRuleAsync(http.WritingV2UserId(), ruleId, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        g.MapPost("/{ruleId}/test", async (
            string ruleId,
            WritingCanonRuleTestRequest request,
            HttpContext http,
            IWritingCanonEngine engine,
            CancellationToken ct) =>
        {
            var result = await engine.TestRuleAsync(http.WritingV2UserId(), ruleId, request, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
    }

    private static void MapLessonRoutes(RouteGroupBuilder g)
    {
        g.MapGet("/", async (
            [FromQuery] string? subSkill,
            [FromQuery] string? status,
            HttpContext http,
            IWritingLessonServiceV2 service,
            CancellationToken ct)
            => Results.Ok(await service.AdminListLessonsAsync(http.WritingV2UserId(), subSkill, status, ct)));

        g.MapPost("/", async (
            WritingLessonUpsertRequest request,
            HttpContext http,
            IWritingLessonServiceV2 service,
            CancellationToken ct)
            => Results.Ok(await service.AdminCreateLessonAsync(http.WritingV2UserId(), request, ct)));

        g.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingLessonServiceV2 service,
            CancellationToken ct) =>
        {
            var lesson = await service.AdminGetLessonAsync(http.WritingV2UserId(), id, ct);
            return lesson is null ? Results.NotFound() : Results.Ok(lesson);
        });

        g.MapPut("/{id:guid}", async (
            Guid id,
            WritingLessonUpsertRequest request,
            HttpContext http,
            IWritingLessonServiceV2 service,
            CancellationToken ct) =>
        {
            var lesson = await service.AdminUpdateLessonAsync(http.WritingV2UserId(), id, request, ct);
            return lesson is null ? Results.NotFound() : Results.Ok(lesson);
        });

        g.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingLessonServiceV2 service,
            CancellationToken ct) =>
        {
            var deleted = await service.AdminDeleteLessonAsync(http.WritingV2UserId(), id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        });
    }

    private static void MapMistakeRoutes(RouteGroupBuilder g)
    {
        g.MapGet("/", async (
            [FromQuery] string? category,
            [FromQuery] string? subSkill,
            HttpContext http,
            IWritingMistakeService service,
            CancellationToken ct)
            => Results.Ok(await service.AdminListMistakesAsync(http.WritingV2UserId(), category, subSkill, ct)));

        g.MapPost("/", async (
            WritingMistakeUpsertRequest request,
            HttpContext http,
            IWritingMistakeService service,
            CancellationToken ct)
            => Results.Ok(await service.AdminCreateMistakeAsync(http.WritingV2UserId(), request, ct)));

        g.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingMistakeService service,
            CancellationToken ct) =>
        {
            var mistake = await service.AdminGetMistakeAsync(http.WritingV2UserId(), id, ct);
            return mistake is null ? Results.NotFound() : Results.Ok(mistake);
        });

        g.MapPut("/{id:guid}", async (
            Guid id,
            WritingMistakeUpsertRequest request,
            HttpContext http,
            IWritingMistakeService service,
            CancellationToken ct) =>
        {
            var mistake = await service.AdminUpdateMistakeAsync(http.WritingV2UserId(), id, request, ct);
            return mistake is null ? Results.NotFound() : Results.Ok(mistake);
        });

        g.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingMistakeService service,
            CancellationToken ct) =>
        {
            var deleted = await service.AdminDeleteMistakeAsync(http.WritingV2UserId(), id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        });
    }

    private static void MapAuditRoutes(RouteGroupBuilder admin)
    {
        admin.MapGet("/audit", async (
            [FromQuery] string? entityType,
            [FromQuery] string? action,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            HttpContext http,
            IWritingContentAuditService service,
            CancellationToken ct)
            => Results.Ok(await service.ListAuditEntriesAsync(
                http.WritingV2UserId(),
                entityType,
                action,
                page ?? 1,
                pageSize ?? 50,
                ct)));
    }
}
