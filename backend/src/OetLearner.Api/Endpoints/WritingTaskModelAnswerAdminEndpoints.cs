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
        // Hybrid generation route: certifies an offline-drafted letter through
        // the SAME grounding/word-count/rule gate as /generate. Never marks
        // Ready on say-so alone — see WritingTaskModelAnswerService.ImportAsync.
        group.MapPost("/tasks/{id:guid}/model-answer/import", ImportModelAnswer).WithAdminWrite("AdminContentWrite");
        group.MapPost("/tasks/{id:guid}/model-answer/approve", ApproveModelAnswer).WithAdminWrite("AdminContentPublish");
        group.MapPost("/tasks/{id:guid}/model-answer/reject", RejectModelAnswer).WithAdminWrite("AdminContentWrite");
        // Preparation-time backfill: generate the ONE reusable Model Answer
        // for published tasks that lack a fresh approved one. Resumable
        // (re-run with a limit to continue), idempotent (Ready+fresh answers
        // are skipped), strictly sequential and bounded per call so provider
        // rate limits are never burst. Never called from candidate submit.
        group.MapPost("/model-answers/generate-missing", GenerateMissingModelAnswers).WithAdminWrite("AdminContentWrite");

        // Option C background generation: enqueue jobs and return
        // immediately — the worker grinds through long-running max-thinking
        // calls with no HTTP/proxy timeout pressure. Idempotent, resumable,
        // restart-safe (stuck-job recovery), bounded retries, and skips
        // fresh-ready answers with zero provider calls on redelivery.
        group.MapPost("/model-answers/enqueue-missing", EnqueueMissingModelAnswers).WithAdminWrite("AdminContentWrite");

        // Addendum Rev8 (11 Sep 2026) §14: run the FULL Model Answer gate
        // (word count, grounding, every Model-Answer-mode rule, optional
        // semantic validator) on a draft WITHOUT storing it — the
        // "deterministic lint -> repair only the failed rules" loop.
        group.MapPost("/tasks/{id:guid}/model-answer/validate", ValidateModelAnswer).WithAdminWrite("AdminContentWrite");

        // Addendum Rev8 §9.5: revalidate saved candidate-facing answers with
        // the CURRENT validator ("do not trust old VERIFIED flags"). Paged;
        // apply=true stamps passing rows and holds/hides failing ones.
        group.MapPost("/model-answers/revalidate", RevalidateModelAnswers).WithAdminWrite("AdminContentWrite");

        return app;
    }

    private static async Task<IResult> ValidateModelAnswer(
        IWritingTaskModelAnswerService service,
        ClaimsPrincipal user,
        Guid id,
        ValidateModelAnswerRequest body,
        CancellationToken ct)
    {
        var adminId = GetUserId(user) ?? "system";
        var report = await service.ValidateAsync(id, body.LetterText ?? string.Empty, body.IncludeSemantic, adminId, ct);
        return Results.Ok(report);
    }

    private static async Task<IResult> RevalidateModelAnswers(
        IWritingTaskModelAnswerService service,
        ClaimsPrincipal user,
        [FromQuery] bool apply = false,
        [FromQuery] bool includeSemantic = false,
        [FromQuery] string? profession = null,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 250,
        [FromQuery] bool onlyUnverified = false,
        CancellationToken ct = default)
    {
        var adminId = GetUserId(user) ?? "system";
        var result = await service.RevalidateAsync(
            new WritingModelAnswerRevalidationRequest(apply, includeSemantic, profession, offset, limit, onlyUnverified),
            adminId, ct);
        return Results.Ok(result);
    }

    public sealed record ValidateModelAnswerRequest(string? LetterText, bool IncludeSemantic = false);

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

    private static async Task<IResult> ImportModelAnswer(
        IWritingTaskModelAnswerService service,
        ClaimsPrincipal user,
        Guid id,
        ImportModelAnswerRequest body,
        CancellationToken ct)
    {
        var adminId = GetUserId(user) ?? "system";
        var answer = await service.ImportAsync(id, body.LetterText, adminId, ct);
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

    private static async Task<IResult> EnqueueMissingModelAnswers(
        IWritingTaskModelAnswerService service,
        ClaimsPrincipal user,
        [FromQuery] int limit = 25,
        CancellationToken ct = default)
    {
        var adminId = GetUserId(user) ?? "system";
        var result = await service.EnqueueMissingAsync(adminId, limit, ct);
        return Results.Ok(new
        {
            requested = result.Requested,
            enqueued = result.Enqueued,
            skipped = result.Skipped,
            items = result.Items.Select(i => new
            {
                scenarioId = i.ScenarioId,
                title = i.Title,
                outcome = i.Outcome,
                holdReason = i.HoldReason,
            }),
        });
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

    public sealed record ImportModelAnswerRequest(string LetterText);

    private static string? GetUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return string.IsNullOrWhiteSpace(sub) ? null : sub;
    }
}
