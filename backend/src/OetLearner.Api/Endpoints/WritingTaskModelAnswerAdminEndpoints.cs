using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin endpoints for the pre-generated, reusable Writing Model Answer per
/// task ("generate once, save permanently, reuse" — see
/// <see cref="WritingTaskModelAnswerService"/>). Never called from the
/// candidate submit path.
/// </summary>
public static class WritingTaskModelAnswerAdminEndpoints
{
    public static IEndpointRouteBuilder MapWritingTaskModelAnswerAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/v1/admin/writing")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser")
            .WithTags("WritingTaskModelAnswerAdmin");

        group.MapGet("/model-answers", ListModelAnswers).WithAdminRead("AdminContentRead");
        group.MapGet("/tasks/{id:guid}/model-answer", GetModelAnswer).WithAdminRead("AdminContentRead");
        group.MapPost("/tasks/{id:guid}/model-answer/generate", GenerateModelAnswer).WithAdminWrite("AdminContentWrite");
        group.MapPost("/tasks/{id:guid}/model-answer/approve", ApproveModelAnswer).WithAdminWrite("AdminContentPublish");
        group.MapPost("/tasks/{id:guid}/model-answer/reject", RejectModelAnswer).WithAdminWrite("AdminContentWrite");
        // Preparation-time backfill: generate the ONE reusable Model Answer
        // for published tasks that lack a fresh approved one. Resumable
        // (re-run with a limit to continue), idempotent (Ready+fresh answers
        // are skipped), strictly sequential and bounded per call so provider
        // rate limits are never burst. Never called from candidate submit.
        group.MapPost("/model-answers/generate-missing", GenerateMissingModelAnswers).WithAdminWrite("AdminContentWrite");

        return app;
    }

    private static async Task<IResult> ListModelAnswers(
        IWritingTaskModelAnswerService service,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var (items, total) = await service.ListAsync(status, page, pageSize);
        return Results.Ok(new { items, total });
    }

    private static async Task<IResult> GetModelAnswer(IWritingTaskModelAnswerService service, Guid id)
    {
        var answer = await service.GetAsync(id);
        return answer is null
            ? Results.NotFound(new { error = "No model answer has been generated for this task yet." })
            : Results.Ok(answer);
    }

    private static async Task<IResult> GenerateModelAnswer(
        IWritingTaskModelAnswerService service,
        ClaimsPrincipal user,
        Guid id,
        CancellationToken ct)
    {
        var adminId = GetUserId(user) ?? "system";
        var answer = await service.GenerateAsync(id, adminId, ct);
        return Results.Ok(answer);
    }

    private static async Task<IResult> GenerateMissingModelAnswers(
        IWritingTaskModelAnswerService service,
        ClaimsPrincipal user,
        [FromQuery] int limit = 5,
        [FromQuery] bool includeStale = false,
        CancellationToken ct = default)
    {
        var adminId = GetUserId(user) ?? "system";
        var result = await service.GenerateMissingAsync(adminId, limit, includeStale, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> ApproveModelAnswer(
        IWritingTaskModelAnswerService service,
        ClaimsPrincipal user,
        Guid id,
        CancellationToken ct)
    {
        var adminId = GetUserId(user) ?? "system";
        var answer = await service.ApproveAsync(id, adminId, ct);
        return answer is null
            ? Results.NotFound(new { error = "No model answer has been generated for this task yet." })
            : Results.Ok(answer);
    }

    private static async Task<IResult> RejectModelAnswer(
        IWritingTaskModelAnswerService service,
        ClaimsPrincipal user,
        Guid id,
        CancellationToken ct)
    {
        var adminId = GetUserId(user) ?? "system";
        var answer = await service.RejectAsync(id, adminId, ct);
        return answer is null
            ? Results.NotFound(new { error = "No model answer has been generated for this task yet." })
            : Results.Ok(answer);
    }

    private static string? GetUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return string.IsNullOrWhiteSpace(sub) ? null : sub;
    }
}
