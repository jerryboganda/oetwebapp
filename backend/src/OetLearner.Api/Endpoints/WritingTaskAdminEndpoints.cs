using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// WS-B2: Admin endpoints for authoring the unified writing task
/// (enriched scenario + content checklist + model-answer exemplar).
/// Paths conform to the contract in <c>lib/writing/exam-api.ts</c>.
/// </summary>
public static class WritingTaskAdminEndpoints
{
    public static IEndpointRouteBuilder MapWritingTaskAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // Mirrors WritingAdminContentEndpoints: AdminOnly policy on the group, plus
        // per-route ContentRead/ContentWrite/ContentPublish permission gating.
        var group = app
            .MapGroup("/v1/admin/writing/tasks")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser")
            .WithTags("WritingTaskAdmin");

        group.MapGet("", ListTasks).WithAdminRead("AdminContentRead");
        group.MapGet("/{id:guid}", GetTask).WithAdminRead("AdminContentRead");
        group.MapGet("/{id:guid}/validate", ValidateTask).WithAdminRead("AdminContentRead");
        group.MapGet("/{id:guid}/export", ExportTask).WithAdminRead("AdminContentRead");

        group.MapPost("", CreateTask).WithAdminWrite("AdminContentWrite");
        group.MapPut("/{id:guid}", UpdateTask).WithAdminWrite("AdminContentWrite");
        group.MapPost("/{id:guid}/archive", ArchiveTask).WithAdminWrite("AdminContentWrite");
        group.MapPost("/{id:guid}/clone", CloneTask).WithAdminWrite("AdminContentWrite");
        group.MapPost("/import", ImportTask).WithAdminWrite("AdminContentWrite");

        group.MapPost("/{id:guid}/publish", PublishTask).WithAdminWrite("AdminContentPublish");

        return app;
    }

    private static async Task<IResult> ListTasks(
        IWritingTaskAuthoringService service,
        [FromQuery] string? profession,
        [FromQuery] string? letterType,
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var (items, total) = await service.ListAsync(profession, letterType, status, search, page, pageSize);
        return Results.Ok(new { items, total });
    }

    private static async Task<IResult> GetTask(IWritingTaskAuthoringService service, Guid id)
    {
        var task = await service.GetAsync(id);
        return task is null
            ? Results.NotFound(new { error = "Writing task not found" })
            : Results.Ok(task);
    }

    private static async Task<IResult> CreateTask(
        IWritingTaskAuthoringService service,
        ClaimsPrincipal user,
        [FromBody] WritingTaskUpsertDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Results.BadRequest(new { error = "Title is required" });
        }

        var task = await service.CreateAsync(request, user);
        return Results.Created($"/v1/admin/writing/tasks/{task.Id}", task);
    }

    private static async Task<IResult> UpdateTask(
        IWritingTaskAuthoringService service,
        ClaimsPrincipal user,
        Guid id,
        [FromBody] WritingTaskUpsertDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Results.BadRequest(new { error = "Title is required" });
        }

        var task = await service.UpdateAsync(id, request, user);
        return task is null
            ? Results.NotFound(new { error = "Writing task not found" })
            : Results.Ok(task);
    }

    private static async Task<IResult> ValidateTask(IWritingTaskAuthoringService service, Guid id)
    {
        var result = await service.ValidateAsync(id);
        return result is null
            ? Results.NotFound(new { error = "Writing task not found" })
            : Results.Ok(result);
    }

    private static async Task<IResult> PublishTask(IWritingTaskAuthoringService service, Guid id)
    {
        var (task, validation) = await service.PublishAsync(id);
        if (task is null && validation is null)
        {
            return Results.NotFound(new { error = "Writing task not found" });
        }

        if (task is null)
        {
            return Results.BadRequest(new
            {
                error = "Writing task is not publish-ready",
                issues = validation!.Issues,
            });
        }

        return Results.Ok(task);
    }

    private static async Task<IResult> ArchiveTask(IWritingTaskAuthoringService service, Guid id)
    {
        var task = await service.ArchiveAsync(id);
        return task is null
            ? Results.NotFound(new { error = "Writing task not found" })
            : Results.Ok(task);
    }

    private static async Task<IResult> CloneTask(
        IWritingTaskAuthoringService service,
        ClaimsPrincipal user,
        Guid id)
    {
        var task = await service.CloneAsync(id, user);
        return task is null
            ? Results.NotFound(new { error = "Writing task not found" })
            : Results.Ok(task);
    }

    private static async Task<IResult> ImportTask(
        IWritingTaskAuthoringService service,
        ClaimsPrincipal user,
        [FromBody] WritingTaskImportJson import)
    {
        var task = await service.ImportAsync(import, user);
        return Results.Created($"/v1/admin/writing/tasks/{task.Id}", task);
    }

    private static async Task<IResult> ExportTask(IWritingTaskAuthoringService service, Guid id)
    {
        var export = await service.ExportAsync(id);
        return export is null
            ? Results.NotFound(new { error = "Writing task not found" })
            : Results.Ok(export);
    }
}
