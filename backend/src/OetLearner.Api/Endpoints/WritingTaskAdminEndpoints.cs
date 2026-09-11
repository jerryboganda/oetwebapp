using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Security;
using OetLearner.Api.Services;
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

        // Preparation-status audit: per-task canonical inputs, rulebook
        // resolvability, Model Answer state/staleness, and publish-gate
        // blocking codes. Powers the backfill workflow and reporting.
        group.MapGet("/preparation-status", PreparationStatus).WithAdminRead("AdminContentRead");

        // Full-catalogue release gate (§22/§15): enumerates EVERY published
        // candidate-facing scenario from the live application model and runs
        // the same resolvers candidate grading depends on. A non-zero
        // invalid count BLOCKS release. Machine-readable (§32) for CI/CD.
        group.MapGet("/catalogue-compatibility", CatalogueCompatibility).WithAdminRead("AdminContentRead");

        // 100% task-load integrity gate (Addendum Rev8 §17/§19.6): runs the
        // exact learner task projection plus every load-time dependency for
        // EVERY published task. loadFailed must be 0 before release.
        group.MapGet("/load-integrity", LoadIntegrity).WithAdminRead("AdminContentRead");

        // Quarantine for §23: archives (never deletes) published tasks that
        // cannot safely grade, so learners stop discovering them at submit
        // time. Dry-run by default; ambiguous tasks still need admin repair
        // before re-publication.
        group.MapPost("/catalogue-quarantine", CatalogueQuarantine).WithAdminWrite("AdminContentWrite");

        // Case-note backfill (spec §22 / grading preflight): replaces this task's
        // structured, relevance-labeled case-note sentences without touching any
        // other authored field. See WritingTaskCaseNotesService for why this is a
        // separate narrow write path rather than reusing the scenario upsert.
        group.MapPut("/{id:guid}/case-notes", ReplaceCaseNotes).WithAdminWrite("AdminContentWrite");

        // Preparation-time canonicalization: runs PDF text extraction / OCR on
        // the task's stimulus PDF ONCE at preparation time and stores the
        // result as structured case notes. Candidate grading reads the stored
        // sentences and never touches the PDF — this is what eliminates
        // case_note_pages_unreadable from the candidate runtime.
        group.MapPost("/{id:guid}/case-notes/extract-from-pdf", ExtractCaseNotesFromPdf).WithAdminWrite("AdminContentWrite");

        // Bulk workflow actions (parity with POST /v1/admin/papers/bulk). Permission
        // is split per action inside the handler because a single route can't carry
        // two RequireAuthorization policies: delete/force-delete need system_admin,
        // publish needs content:publish, archive needs content:write. A granular
        // AdminContentRead baseline (mirroring the papers group) gates entry above
        // the bare AdminOnly group policy; the handler still enforces the stricter
        // per-action permission. PerUserWrite applies the mutation rate-limit bucket.
        group.MapPost("/bulk", BulkTasks)
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUserWrite");

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

    private static async Task<IResult> PreparationStatus(
        IWritingTaskAuthoringService service,
        [FromQuery] string? status,
        [FromQuery] string? profession,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var (items, total) = await service.GetPreparationStatusAsync(status, profession, page, pageSize, ct);
        return Results.Ok(new { items, total });
    }

    private static async Task<IResult> CatalogueCompatibility(
        IWritingCataloguePreflightService service,
        CancellationToken ct)
    {
        var report = await service.ScanPublishedCatalogueAsync(ct);
        return Results.Ok(new
        {
            generatedAt = report.GeneratedAt,
            publishedScenarios = report.PublishedScenarios,
            publishReady = report.PublishReady,
            invalid = report.Invalid,
            releaseBlocked = report.Invalid > 0,
            rows = report.Rows.Select(r => new
            {
                scenarioId = r.ScenarioId,
                taskTitle = r.TaskTitle,
                publicationStatus = r.PublicationStatus,
                candidateVisible = r.CandidateVisible,
                profession = r.Profession,
                professionResolutionStatus = r.ProfessionResolutionStatus,
                letterType = r.LetterType,
                otherLetters = r.OtherLetters,
                rulebookId = r.RulePackId,
                rulebookVersion = r.RulePackVersion,
                rulebookApprovalStatus = r.RulePackApprovalStatus,
                sharedRulesVersion = r.SharedRulesVersion,
                canonicalCaseNotesReady = r.CanonicalCaseNotesReady,
                caseNoteSentenceCount = r.CaseNoteSentenceCount,
                exactWritingTaskReady = r.ExactWritingTaskReady,
                recipientMetadataReady = r.RecipientMetadataReady,
                recipientCategory = r.RecipientCategory,
                savedModelAnswerReady = r.SavedModelAnswerReady,
                modelAnswerStatus = r.ModelAnswerStatus,
                publishReady = r.PublishReady,
                blockingCodes = r.BlockingCodes,
                recommendedAction = r.RecommendedAction,
            }),
        });
    }

    // [FromServices]: an unregistered service then fails only this route at
    // request time instead of breaking endpoint-table construction app-wide.
    private static async Task<IResult> LoadIntegrity(
        [FromServices] IWritingTaskLoadIntegrityService service,
        CancellationToken ct)
        => Results.Ok(await service.ScanPublishedAsync(ct));

    private static async Task<IResult> CatalogueQuarantine(
        IWritingCataloguePreflightService service,
        [FromBody] WritingCatalogueQuarantineRequest? request,
        CancellationToken ct)
    {
        var result = await service.QuarantineInvalidPublishedAsync(request?.DryRun ?? true, ct);
        return Results.Ok(new
        {
            scanned = result.Scanned,
            quarantined = result.Quarantined,
            skipped = result.Skipped,
            items = result.Items.Select(i => new
            {
                scenarioId = i.ScenarioId,
                title = i.Title,
                reasons = i.Reasons,
                outcome = i.Outcome,
            }),
        });
    }

    private static async Task<IResult> ExtractCaseNotesFromPdf(
        IWritingTaskCaseNotesService service,
        Guid id,
        CancellationToken ct)
    {
        var result = await service.ExtractFromStimulusPdfAsync(id, ct);
        return result is null
            ? Results.NotFound(new { error = "Writing task or its stimulus PDF was not found." })
            : Results.Ok(result);
    }

    private static async Task<IResult> ReplaceCaseNotes(
        IWritingTaskCaseNotesService service,
        Guid id,
        [FromBody] WritingTaskCaseNotesRequest request)
    {
        if (request.Sentences is null || request.Sentences.Count == 0)
        {
            return Results.BadRequest(new { error = "At least one case-note sentence is required." });
        }

        var dtos = request.Sentences
            .Select((s, i) => new WritingScenarioStructuredSentenceDto(
                s.Ordinal ?? i + 1,
                s.Text ?? string.Empty,
                s.Relevance ?? "relevant",
                s.Notes))
            .ToList();

        var saved = await service.ReplaceAsync(id, dtos);
        return saved is null
            ? Results.NotFound(new { error = "Writing task not found" })
            : Results.Ok(new { scenarioId = id, sentences = saved });
    }

    private static async Task<IResult> BulkTasks(
        IWritingTaskAuthoringService service,
        HttpContext http,
        [FromBody] WritingTaskBulkRequest request,
        CancellationToken ct)
    {
        var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();

        // Per-action permission gating mirrors ContentPapersAdminEndpoints: the
        // permanent-purge actions require system_admin; publish needs content:publish;
        // archive needs content:write.
        var requiresSystemAdmin = action is "delete" or "force-delete";
        var requiresPublish = action is "publish";
        var requiresWrite = action is "archive";
        if (!requiresSystemAdmin && !requiresPublish && !requiresWrite)
        {
            return Results.BadRequest(new { error = $"Unknown bulk action '{request.Action}'." });
        }

        var perms = http.User.FindFirstValue(AuthTokenService.AdminPermissionsClaimType);
        var allowed = requiresSystemAdmin
            ? AdminPermissionEvaluator.HasAny(perms, "system_admin")
            : requiresPublish
                ? AdminPermissionEvaluator.HasAny(perms, "content:publish", "system_admin")
                : AdminPermissionEvaluator.HasAny(perms, "content:write", "system_admin");
        if (!allowed)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        try
        {
            var result = await service.BulkAsync(action, request.Ids ?? Array.Empty<string>(), ct);
            return Results.Ok(result);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

/// <summary>
/// Request body for <c>POST /v1/admin/writing/tasks/bulk</c>. <c>Action</c> must be
/// one of: publish, archive, delete, force-delete.
/// </summary>
public sealed record WritingTaskBulkRequest(string Action, string[] Ids, string? Reason = null);

/// <summary>Request body for <c>PUT /v1/admin/writing/tasks/{id}/case-notes</c>.</summary>
public sealed record WritingTaskCaseNotesRequest(List<WritingTaskCaseNoteSentence> Sentences);

/// <summary>
/// Request body for <c>POST /v1/admin/writing/tasks/catalogue-quarantine</c>.
/// <c>DryRun</c> defaults to true (report only); false archives invalid
/// published tasks.
/// </summary>
public sealed record WritingCatalogueQuarantineRequest(bool DryRun = true);

/// <summary><c>Relevance</c> must be one of relevant | maybe | irrelevant.</summary>
public sealed record WritingTaskCaseNoteSentence(int? Ordinal, string? Text, string? Relevance, string? Notes);
