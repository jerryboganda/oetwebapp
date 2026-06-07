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
                ct)))
            .WithAdminRead("AdminContentRead");

        g.MapPost("/", async (
            WritingScenarioUpsertRequest request,
            HttpContext http,
            IWritingScenarioService service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var actorId = http.WritingV2UserId();
            var scenario = await service.AdminCreateScenarioAsync(actorId, request, ct);
            await audit.LogAsync(actorId, "WritingScenario", scenario.Id.ToString(), "writing.scenario.created", null, ct);
            return Results.Ok(scenario);
        })
            .WithAdminWrite("AdminContentWrite");

        g.MapPost("/generate", async (
            WritingScenarioGenerateRequest request,
            HttpContext http,
            IWritingScenarioGeneratorService service,
            CancellationToken ct)
            => Results.Ok(await service.GenerateScenarioAsync(http.WritingV2UserId(), request, ct)))
            .WithAdminWrite("AdminContentWrite");

        g.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingScenarioService service,
            CancellationToken ct) =>
        {
            var scenario = await service.AdminGetScenarioAsync(http.WritingV2UserId(), id, ct);
            return scenario is null ? Results.NotFound() : Results.Ok(scenario);
        })
            .WithAdminRead("AdminContentRead");

        g.MapPut("/{id:guid}", async (
            Guid id,
            WritingScenarioUpsertRequest request,
            HttpContext http,
            IWritingScenarioService service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var actorId = http.WritingV2UserId();
            var scenario = await service.AdminUpdateScenarioAsync(actorId, id, request, ct);
            if (scenario is not null)
                await audit.LogAsync(actorId, "WritingScenario", id.ToString(), "writing.scenario.updated", null, ct);
            return scenario is null ? Results.NotFound() : Results.Ok(scenario);
        })
            .WithAdminWrite("AdminContentWrite");

        g.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingScenarioService service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var actorId = http.WritingV2UserId();
            var deleted = await service.AdminDeleteScenarioAsync(actorId, id, ct);
            if (deleted)
                await audit.LogAsync(actorId, "WritingScenario", id.ToString(), "writing.scenario.deleted", null, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
            .WithAdminWrite("AdminContentWrite");

        g.MapPost("/{id:guid}/approve", async (
            Guid id,
            HttpContext http,
            IWritingScenarioService service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var actorId = http.WritingV2UserId();
            var scenario = await service.AdminApproveScenarioAsync(actorId, id, ct);
            if (scenario is not null)
                await audit.LogAsync(actorId, "WritingScenario", id.ToString(), "writing.scenario.approved", null, ct);
            return scenario is null ? Results.NotFound() : Results.Ok(scenario);
        })
            .WithAdminWrite("AdminContentPublish");
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
                ct)))
            .WithAdminRead("AdminContentRead");

        g.MapPost("/", async (
            WritingExemplarUpsertRequest request,
            HttpContext http,
            IWritingExemplarService service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var actorId = http.WritingV2UserId();
            var exemplar = await service.AdminCreateExemplarAsync(actorId, request, ct);
            await audit.LogAsync(actorId, "WritingExemplar", exemplar.Id.ToString(), "writing.exemplar.created", null, ct);
            return Results.Ok(exemplar);
        })
            .WithAdminWrite("AdminContentWrite");

        g.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingExemplarService service,
            CancellationToken ct) =>
        {
            var exemplar = await service.AdminGetExemplarAsync(http.WritingV2UserId(), id, ct);
            return exemplar is null ? Results.NotFound() : Results.Ok(exemplar);
        })
            .WithAdminRead("AdminContentRead");

        g.MapPut("/{id:guid}", async (
            Guid id,
            WritingExemplarUpsertRequest request,
            HttpContext http,
            IWritingExemplarService service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var actorId = http.WritingV2UserId();
            var exemplar = await service.AdminUpdateExemplarAsync(actorId, id, request, ct);
            if (exemplar is not null)
                await audit.LogAsync(actorId, "WritingExemplar", id.ToString(), "writing.exemplar.updated", null, ct);
            return exemplar is null ? Results.NotFound() : Results.Ok(exemplar);
        })
            .WithAdminWrite("AdminContentWrite");

        g.MapPost("/{id:guid}/publish", async (
            Guid id,
            HttpContext http,
            IWritingExemplarService service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var actorId = http.WritingV2UserId();
            var exemplar = await service.AdminPublishExemplarAsync(actorId, id, ct);
            if (exemplar is not null)
                await audit.LogAsync(actorId, "WritingExemplar", id.ToString(), "writing.exemplar.published", null, ct);
            return exemplar is null ? Results.NotFound() : Results.Ok(exemplar);
        })
            .WithAdminWrite("AdminContentPublish");

        g.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingExemplarService service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var actorId = http.WritingV2UserId();
            var deleted = await service.AdminDeleteExemplarAsync(actorId, id, ct);
            if (deleted)
                await audit.LogAsync(actorId, "WritingExemplar", id.ToString(), "writing.exemplar.deleted", null, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
            .WithAdminWrite("AdminContentWrite");

        g.MapPost("/{id:guid}/test-grade", async (
            Guid id,
            HttpContext http,
            IWritingExemplarService service,
            CancellationToken ct) =>
        {
            var result = await service.AdminTestGradeExemplarAsync(http.WritingV2UserId(), id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
            // POST mutation verb: needs the per-user write limiter even though
            // authorization is read-scoped (it only test-grades, no mutation).
            .WithAdminRead("AdminContentRead").RequireRateLimiting("PerUserWrite");
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
                ct)))
            .WithAdminRead("AdminContentRead");

        g.MapPost("/", async (
            WritingDrillUpsertRequest request,
            HttpContext http,
            IWritingDrillService service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var actorId = http.WritingV2UserId();
            var drill = await service.AdminCreateDrillAsync(actorId, request, ct);
            await audit.LogAsync(actorId, "WritingDrill", drill.Id.ToString(), "writing.drill.created", null, ct);
            return Results.Ok(drill);
        })
            .WithAdminWrite("AdminContentWrite");

        g.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingDrillService service,
            CancellationToken ct) =>
        {
            var drill = await service.AdminGetDrillAsync(http.WritingV2UserId(), id, ct);
            return drill is null ? Results.NotFound() : Results.Ok(drill);
        })
            .WithAdminRead("AdminContentRead");

        g.MapPut("/{id:guid}", async (
            Guid id,
            WritingDrillUpsertRequest request,
            HttpContext http,
            IWritingDrillService service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var actorId = http.WritingV2UserId();
            var drill = await service.AdminUpdateDrillAsync(actorId, id, request, ct);
            if (drill is not null)
                await audit.LogAsync(actorId, "WritingDrill", id.ToString(), "writing.drill.updated", null, ct);
            return drill is null ? Results.NotFound() : Results.Ok(drill);
        })
            .WithAdminWrite("AdminContentWrite");

        g.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingDrillService service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var actorId = http.WritingV2UserId();
            var deleted = await service.AdminDeleteDrillAsync(actorId, id, ct);
            if (deleted)
                await audit.LogAsync(actorId, "WritingDrill", id.ToString(), "writing.drill.deleted", null, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
            .WithAdminWrite("AdminContentWrite");
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
                ct)))
            .WithAdminRead("AdminContentRead");

        g.MapPost("/", async (
            WritingCanonRuleUpsertRequest request,
            HttpContext http,
            IWritingCanonService service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var actorId = http.WritingV2UserId();
            var rule = await service.AdminCreateCanonRuleAsync(actorId, request, ct);
            await audit.LogAsync(actorId, "WritingCanonRule", rule.Id, "writing.canon.created", null, ct);
            return Results.Ok(rule);
        })
            .WithAdminWrite("AdminContentWrite");

        g.MapGet("/{ruleId}", async (
            string ruleId,
            HttpContext http,
            IWritingCanonService service,
            CancellationToken ct) =>
        {
            var rule = await service.AdminGetCanonRuleAsync(http.WritingV2UserId(), ruleId, ct);
            return rule is null ? Results.NotFound() : Results.Ok(rule);
        })
            .WithAdminRead("AdminContentRead");

        g.MapPut("/{ruleId}", async (
            string ruleId,
            WritingCanonRuleUpsertRequest request,
            HttpContext http,
            IWritingCanonService service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var actorId = http.WritingV2UserId();
            var rule = await service.AdminUpdateCanonRuleAsync(actorId, ruleId, request, ct);
            if (rule is not null)
                await audit.LogAsync(actorId, "WritingCanonRule", ruleId, "writing.canon.updated", null, ct);
            return rule is null ? Results.NotFound() : Results.Ok(rule);
        })
            .WithAdminWrite("AdminContentWrite");

        g.MapDelete("/{ruleId}", async (
            string ruleId,
            HttpContext http,
            IWritingCanonService service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var actorId = http.WritingV2UserId();
            var deleted = await service.AdminDeleteCanonRuleAsync(actorId, ruleId, ct);
            if (deleted)
                await audit.LogAsync(actorId, "WritingCanonRule", ruleId, "writing.canon.deleted", null, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
            .WithAdminWrite("AdminContentWrite");

        g.MapPost("/{ruleId}/test", async (
            string ruleId,
            WritingCanonRuleTestRequest request,
            HttpContext http,
            IWritingCanonEngine engine,
            CancellationToken ct) =>
        {
            var result = await engine.TestRuleAsync(http.WritingV2UserId(), ruleId, request, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
            // POST mutation verb: needs the per-user write limiter even though
            // authorization is read-scoped (it only tests a rule, no mutation).
            .WithAdminRead("AdminContentRead").RequireRateLimiting("PerUserWrite");
    }

    private static void MapLessonRoutes(RouteGroupBuilder g)
    {
        g.MapGet("/", async (
            [FromQuery] string? subSkill,
            [FromQuery] string? status,
            HttpContext http,
            IWritingLessonServiceV2 service,
            CancellationToken ct)
            => Results.Ok(await service.AdminListLessonsAsync(http.WritingV2UserId(), subSkill, status, ct)))
            .WithAdminRead("AdminContentRead");

        g.MapPost("/", async (
            WritingLessonUpsertRequest request,
            HttpContext http,
            IWritingLessonServiceV2 service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var actorId = http.WritingV2UserId();
            var lesson = await service.AdminCreateLessonAsync(actorId, request, ct);
            await audit.LogAsync(actorId, "WritingLesson", lesson.Id.ToString(), "writing.lesson.created", null, ct);
            return Results.Ok(lesson);
        })
            .WithAdminWrite("AdminContentWrite");

        g.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingLessonServiceV2 service,
            CancellationToken ct) =>
        {
            var lesson = await service.AdminGetLessonAsync(http.WritingV2UserId(), id, ct);
            return lesson is null ? Results.NotFound() : Results.Ok(lesson);
        })
            .WithAdminRead("AdminContentRead");

        g.MapPut("/{id:guid}", async (
            Guid id,
            WritingLessonUpsertRequest request,
            HttpContext http,
            IWritingLessonServiceV2 service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var actorId = http.WritingV2UserId();
            var lesson = await service.AdminUpdateLessonAsync(actorId, id, request, ct);
            if (lesson is not null)
                await audit.LogAsync(actorId, "WritingLesson", id.ToString(), "writing.lesson.updated", null, ct);
            return lesson is null ? Results.NotFound() : Results.Ok(lesson);
        })
            .WithAdminWrite("AdminContentWrite");

        g.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingLessonServiceV2 service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var actorId = http.WritingV2UserId();
            var deleted = await service.AdminDeleteLessonAsync(actorId, id, ct);
            if (deleted)
                await audit.LogAsync(actorId, "WritingLesson", id.ToString(), "writing.lesson.deleted", null, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
            .WithAdminWrite("AdminContentWrite");
    }

    private static void MapMistakeRoutes(RouteGroupBuilder g)
    {
        g.MapGet("/", async (
            [FromQuery] string? category,
            [FromQuery] string? subSkill,
            HttpContext http,
            IWritingMistakeService service,
            CancellationToken ct)
            => Results.Ok(await service.AdminListMistakesAsync(http.WritingV2UserId(), category, subSkill, ct)))
            .WithAdminRead("AdminContentRead");

        g.MapPost("/", async (
            WritingMistakeUpsertRequest request,
            HttpContext http,
            IWritingMistakeService service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var actorId = http.WritingV2UserId();
            var mistake = await service.AdminCreateMistakeAsync(actorId, request, ct);
            await audit.LogAsync(actorId, "WritingMistake", mistake.Id.ToString(), "writing.mistake.created", null, ct);
            return Results.Ok(mistake);
        })
            .WithAdminWrite("AdminContentWrite");

        g.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingMistakeService service,
            CancellationToken ct) =>
        {
            var mistake = await service.AdminGetMistakeAsync(http.WritingV2UserId(), id, ct);
            return mistake is null ? Results.NotFound() : Results.Ok(mistake);
        })
            .WithAdminRead("AdminContentRead");

        g.MapPut("/{id:guid}", async (
            Guid id,
            WritingMistakeUpsertRequest request,
            HttpContext http,
            IWritingMistakeService service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var actorId = http.WritingV2UserId();
            var mistake = await service.AdminUpdateMistakeAsync(actorId, id, request, ct);
            if (mistake is not null)
                await audit.LogAsync(actorId, "WritingMistake", id.ToString(), "writing.mistake.updated", null, ct);
            return mistake is null ? Results.NotFound() : Results.Ok(mistake);
        })
            .WithAdminWrite("AdminContentWrite");

        g.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingMistakeService service,
            IWritingContentAuditService audit,
            CancellationToken ct) =>
        {
            var actorId = http.WritingV2UserId();
            var deleted = await service.AdminDeleteMistakeAsync(actorId, id, ct);
            if (deleted)
                await audit.LogAsync(actorId, "WritingMistake", id.ToString(), "writing.mistake.deleted", null, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
            .WithAdminWrite("AdminContentWrite");
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
                ct)))
            .WithAdminRead("AdminAuditLogs");
    }
}
