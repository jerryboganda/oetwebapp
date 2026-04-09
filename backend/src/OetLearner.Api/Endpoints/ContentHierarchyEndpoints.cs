using System.Security.Claims;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class ContentHierarchyEndpoints
{
    public static IEndpointRouteBuilder MapContentHierarchyEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Admin: Programs, Tracks, Modules, Packages ──

        var admin = app.MapGroup("/v1/admin")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        admin.MapGet("/programs", async (ContentHierarchyService service, CancellationToken ct,
            string? type, string? language, string? status, int? page, int? pageSize)
            => Results.Ok(await service.GetProgramsAsync(type, language, status, page ?? 1, pageSize ?? 20, ct)));

        admin.MapGet("/programs/{programId}", async (string programId, ContentHierarchyService service, CancellationToken ct)
            => await service.GetProgramAsync(programId, ct) is { } p ? Results.Ok(p) : Results.NotFound());

        admin.MapPost("/programs", async (HttpContext http, ContentProgram program, ContentHierarchyService service, CancellationToken ct)
            => Results.Ok(await service.CreateProgramAsync(AdminId(http), program, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPut("/programs/{programId}", async (string programId, ContentProgram update, ContentHierarchyService service, CancellationToken ct)
            => await service.UpdateProgramAsync(programId, update, ct) is { } p ? Results.Ok(p) : Results.NotFound())
            .RequireRateLimiting("PerUserWrite");

        admin.MapGet("/programs/{programId}/tracks", async (string programId, ContentHierarchyService service, CancellationToken ct)
            => Results.Ok(await service.GetTracksAsync(programId, ct)));

        admin.MapPost("/tracks", async (ContentTrack track, ContentHierarchyService service, CancellationToken ct)
            => Results.Ok(await service.CreateTrackAsync(track, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPut("/tracks/{trackId}", async (string trackId, ContentTrack update, ContentHierarchyService service, CancellationToken ct)
            => await service.UpdateTrackAsync(trackId, update, ct) is { } t ? Results.Ok(t) : Results.NotFound())
            .RequireRateLimiting("PerUserWrite");

        admin.MapGet("/tracks/{trackId}/modules", async (string trackId, ContentHierarchyService service, CancellationToken ct)
            => Results.Ok(await service.GetModulesAsync(trackId, ct)));

        admin.MapPost("/modules", async (ContentModule module, ContentHierarchyService service, CancellationToken ct)
            => Results.Ok(await service.CreateModuleAsync(module, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPut("/modules/{moduleId}", async (string moduleId, ContentModule update, ContentHierarchyService service, CancellationToken ct)
            => await service.UpdateModuleAsync(moduleId, update, ct) is { } m ? Results.Ok(m) : Results.NotFound())
            .RequireRateLimiting("PerUserWrite");

        admin.MapGet("/modules/{moduleId}/lessons", async (string moduleId, ContentHierarchyService service, CancellationToken ct)
            => Results.Ok(await service.GetLessonsAsync(moduleId, ct)));

        admin.MapPost("/lessons", async (ContentLesson lesson, ContentHierarchyService service, CancellationToken ct)
            => Results.Ok(await service.CreateLessonAsync(lesson, ct)))
            .RequireRateLimiting("PerUserWrite");

        // ── Packages ──

        admin.MapGet("/packages", async (ContentHierarchyService service, CancellationToken ct,
            string? type, string? status, int? page, int? pageSize)
            => Results.Ok(await service.GetPackagesAsync(type, status, page ?? 1, pageSize ?? 20, ct)));

        admin.MapGet("/packages/{packageId}", async (string packageId, ContentHierarchyService service, CancellationToken ct)
            => await service.GetPackageAsync(packageId, ct) is { } p ? Results.Ok(p) : Results.NotFound());

        admin.MapPost("/packages", async (ContentPackage package, ContentHierarchyService service, CancellationToken ct)
            => Results.Ok(await service.CreatePackageAsync(package, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPut("/packages/{packageId}", async (string packageId, ContentPackage update, ContentHierarchyService service, CancellationToken ct)
            => await service.UpdatePackageAsync(packageId, update, ct) is { } p ? Results.Ok(p) : Results.NotFound())
            .RequireRateLimiting("PerUserWrite");

        admin.MapGet("/packages/{packageId}/rules", async (string packageId, ContentHierarchyService service, CancellationToken ct)
            => Results.Ok(await service.GetPackageRulesAsync(packageId, ct)));

        admin.MapPost("/packages/{packageId}/rules", async (string packageId, PackageContentRule rule, ContentHierarchyService service, CancellationToken ct) =>
        {
            rule.PackageId = packageId;
            return Results.Ok(await service.AddPackageRuleAsync(rule, ct));
        }).RequireRateLimiting("PerUserWrite");

        admin.MapDelete("/packages/rules/{ruleId}", async (string ruleId, ContentHierarchyService service, CancellationToken ct)
            => await service.RemovePackageRuleAsync(ruleId, ct) ? Results.Ok() : Results.NotFound())
            .RequireRateLimiting("PerUserWrite");

        // ── Import Batches ──

        admin.MapGet("/import-batches", async (ContentHierarchyService service, CancellationToken ct, int? page, int? pageSize)
            => Results.Ok(await service.GetImportBatchesAsync(page ?? 1, pageSize ?? 20, ct)));

        admin.MapPost("/import-batches", async (HttpContext http, ImportBatchCreateRequest request, ContentHierarchyService service, CancellationToken ct)
            => Results.Ok(await service.CreateImportBatchAsync(AdminId(http), request.Title, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPost("/import-batches/{batchId}/rollback", async (string batchId, ContentHierarchyService service, CancellationToken ct)
            => await service.RollbackImportBatchAsync(batchId, ct) ? Results.Ok() : Results.NotFound())
            .RequireRateLimiting("PerUserWrite");

        // ── Testimonials ──

        admin.MapGet("/testimonials", async (ContentHierarchyService service, CancellationToken ct, int? page, int? pageSize)
            => Results.Ok(await service.GetTestimonialsAsync(page ?? 1, pageSize ?? 20, ct)));

        admin.MapPost("/testimonials", async (TestimonialAsset testimonial, ContentHierarchyService service, CancellationToken ct)
            => Results.Ok(await service.CreateTestimonialAsync(testimonial, ct)))
            .RequireRateLimiting("PerUserWrite");

        // ── Foundation Resources ──

        admin.MapGet("/foundation-resources", async (ContentHierarchyService service, CancellationToken ct, string? type)
            => Results.Ok(await service.GetFoundationResourcesAsync(type, ct)));

        admin.MapPost("/foundation-resources", async (FoundationResource resource, ContentHierarchyService service, CancellationToken ct)
            => Results.Ok(await service.CreateFoundationResourceAsync(resource, ct)))
            .RequireRateLimiting("PerUserWrite");

        // ── Dedup Review Queue ──

        admin.MapPost("/dedup/scan", async (ContentDeduplicationService dedupService, CancellationToken ct)
            => Results.Ok(await dedupService.ScanForDuplicatesAsync(ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapGet("/dedup/groups", async (ContentDeduplicationService dedupService, CancellationToken ct,
            int? page, int? pageSize)
            => Results.Ok(await dedupService.GetDuplicateGroupsAsync(page ?? 1, pageSize ?? 20, ct)));

        admin.MapGet("/dedup/groups/{groupId}", async (string groupId, ContentDeduplicationService dedupService, CancellationToken ct)
            => await dedupService.GetDuplicateGroupAsync(groupId, ct) is { } g ? Results.Ok(g) : Results.NotFound());

        admin.MapPost("/dedup/groups/{groupId}/designate-canonical", async (string groupId, DesignateCanonicalRequest request,
            ContentDeduplicationService dedupService, CancellationToken ct)
            => await dedupService.DesignateCanonicalAsync(groupId, request.CanonicalItemId, ct)
                ? Results.Ok() : Results.NotFound())
            .RequireRateLimiting("PerUserWrite");

        admin.MapPost("/dedup/items/{itemId}/remove-from-group", async (string itemId,
            ContentDeduplicationService dedupService, CancellationToken ct)
            => await dedupService.RemoveFromGroupAsync(itemId, ct)
                ? Results.Ok() : Results.NotFound())
            .RequireRateLimiting("PerUserWrite");

        // ── Content Validation ──

        admin.MapPost("/content/validate", (ContentValidateRequest request) =>
        {
            var detailResult = ContentSchemaValidator.ValidateDetailJson(request.SubtestCode, request.DetailJson);
            var modelResult = ContentSchemaValidator.ValidateModelAnswerJson(request.SubtestCode, request.ModelAnswerJson ?? "{}");
            return Results.Ok(new
            {
                detailValid = detailResult.IsValid,
                detailErrors = detailResult.Errors,
                modelAnswerValid = modelResult.IsValid,
                modelAnswerErrors = modelResult.Errors,
                isValid = detailResult.IsValid && modelResult.IsValid
            });
        });

        // ── Learner: Program Browser (public, auth required) ──

        var learner = app.MapGroup("/v1")
            .RequireAuthorization()
            .RequireRateLimiting("PerUser");

        learner.MapGet("/programs", async (ContentHierarchyService service, CancellationToken ct,
            string? type, string? language, int? page, int? pageSize)
            => Results.Ok(await service.GetProgramsAsync(type, language, "Published", page ?? 1, pageSize ?? 20, ct)));

        learner.MapGet("/programs/{programId}", async (string programId, ContentHierarchyService service, CancellationToken ct)
            => await service.GetProgramAsync(programId, ct) is { } p && p.Status == ContentStatus.Published
                ? Results.Ok(p) : Results.NotFound());

        learner.MapGet("/programs/{programId}/tracks", async (string programId, ContentHierarchyService service, CancellationToken ct)
            => Results.Ok(await service.GetTracksAsync(programId, ct)));

        learner.MapGet("/packages", async (ContentHierarchyService service, CancellationToken ct,
            string? type, int? page, int? pageSize)
            => Results.Ok(await service.GetPackagesAsync(type, "Published", page ?? 1, pageSize ?? 20, ct)));

        learner.MapGet("/packages/{packageId}", async (string packageId, ContentHierarchyService service, CancellationToken ct)
            => await service.GetPackageAsync(packageId, ct) is { } p && p.Status == ContentStatus.Published
                ? Results.Ok(p) : Results.NotFound());

        learner.MapGet("/free-previews", async (ContentHierarchyService service, CancellationToken ct)
            => Results.Ok(await service.GetFreePreviewAssetsAsync(ct)));

        learner.MapGet("/foundation-resources", async (ContentHierarchyService service, CancellationToken ct, string? type)
            => Results.Ok(await service.GetFoundationResourcesAsync(type, ct)));

        // ── Content Browser (access-aware) ──

        learner.MapGet("/content-browser", async (HttpContext http, ContentAccessService accessService, CancellationToken ct,
            string? subtest, string? profession, string? difficulty, string? language, string? provenance,
            int? page, int? pageSize)
            => Results.Ok(await accessService.BrowseContentAsync(
                UserId(http), subtest, profession, difficulty, language, provenance,
                page ?? 1, pageSize ?? 20, ct)));

        learner.MapGet("/content-browser/{contentId}/access", async (string contentId, HttpContext http,
            ContentAccessService accessService, CancellationToken ct)
            => Results.Ok(await accessService.CheckAccessAsync(UserId(http), contentId, ct)));

        learner.MapGet("/programs-browser", async (HttpContext http, ContentAccessService accessService, CancellationToken ct,
            string? type, string? language, int? page, int? pageSize)
            => Results.Ok(await accessService.BrowseProgramsWithAccessAsync(
                UserId(http), type, language, page ?? 1, pageSize ?? 20, ct)));

        // ── Phase 6: Mock / Diagnostic / Readiness ──

        admin.MapPost("/mock/assemble", async (MockAssembleRequest req, MockDiagnosticService service, CancellationToken ct)
            => Results.Ok(await service.AssembleMockExamAsync(req.ProfessionId, req.Language ?? "en", ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPost("/diagnostic/generate", async (DiagnosticGenerateRequest req, MockDiagnosticService service, CancellationToken ct)
            => Results.Ok(await service.GenerateDiagnosticAsync(req.ProfessionId, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPatch("/content/{contentId}/eligibility", async (string contentId, EligibilityPatchRequest req,
            MockDiagnosticService service, CancellationToken ct)
            => await service.UpdateEligibilityAsync(contentId, req.IsMockEligible, req.IsDiagnosticEligible, ct)
                ? Results.Ok() : Results.NotFound())
            .RequireRateLimiting("PerUserWrite");

        learner.MapGet("/readiness", async (HttpContext http, MockDiagnosticService service, CancellationToken ct)
            => Results.Ok(await service.CalculateReadinessAsync(UserId(http), ct)));

        learner.MapGet("/content/by-skill", async (MockDiagnosticService service, CancellationToken ct,
            string? skillTag, string subtest, int? page, int? pageSize)
            => Results.Ok(await service.GetContentBySkillTagAsync(subtest, skillTag, page ?? 1, pageSize ?? 20, ct)));

        // ── Phase 7: Admin Bulk Import & Inventory ──

        admin.MapPost("/content/bulk-import", async (HttpContext http, BulkImportRequest request,
            ContentImportService importService, CancellationToken ct)
            => Results.Ok(await importService.BulkImportAsync(AdminId(http), request.BatchTitle ?? "Import", request.Rows, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapGet("/content/inventory", async (ContentImportService importService, CancellationToken ct,
            string? subtest, string? profession, string? language, string? provenance,
            string? freshness, string? qaStatus, string? status, string? packageId,
            string? importBatchId, string? search, int? page, int? pageSize)
            => Results.Ok(await importService.GetContentInventoryAsync(
                new ContentInventoryQuery
                {
                    SubtestCode = subtest, ProfessionId = profession, Language = language, Provenance = provenance,
                    Freshness = freshness, QaStatus = qaStatus, Status = status, PackageId = packageId,
                    ImportBatchId = importBatchId, Search = search, Page = page ?? 1, PageSize = pageSize ?? 20
                }, ct)));

        admin.MapPost("/media-assets", async (MediaAsset asset, ContentImportService importService, CancellationToken ct)
            => Results.Ok(await importService.UpsertMediaAssetAsync(asset, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapGet("/media-assets", async (ContentImportService importService, CancellationToken ct,
            string? mimeType, string? status, int? page, int? pageSize)
            => Results.Ok(await importService.GetMediaAssetsAsync(mimeType, status, page ?? 1, pageSize ?? 20, ct)));

        // ── Phase 9: Search & Recommendations ──

        learner.MapGet("/search", async (ContentSearchService searchService, CancellationToken ct,
            string? q, string? subtest, string? profession, string? difficulty,
            string? language, string? provenance, string? contentType,
            int? minQuality, bool? mockEligible, bool? previewEligible,
            int? page, int? pageSize)
            => Results.Ok(await searchService.SearchContentAsync(
                new ContentSearchQuery
                {
                    Text = q, SubtestCode = subtest, ProfessionId = profession, Difficulty = difficulty,
                    Language = language, Provenance = provenance, ContentType = contentType,
                    MinQuality = minQuality ?? 0, MockEligibleOnly = mockEligible ?? false,
                    PreviewEligibleOnly = previewEligible ?? false,
                    Page = page ?? 1, PageSize = pageSize ?? 20
                }, ct)));

        learner.MapGet("/search/facets", async (ContentSearchService searchService, CancellationToken ct)
            => Results.Ok(await searchService.GetSearchFacetsAsync(ct)));

        learner.MapGet("/recommendations", async (HttpContext http, ContentSearchService searchService, CancellationToken ct,
            int? count)
            => Results.Ok(await searchService.GetRecommendationsAsync(UserId(http), count ?? 10, ct)));

        // ── Phase 11: Media Normalization ──

        learner.MapGet("/media/{assetId}/url", async (string assetId, HttpContext http,
            MediaNormalizationService mediaService, CancellationToken ct)
            => Results.Ok(await mediaService.GetSignedMediaUrlAsync(assetId, UserId(http), ct)));

        admin.MapPost("/media/{assetId}/process", async (string assetId, MediaNormalizationService mediaService, CancellationToken ct)
            => Results.Ok(await mediaService.EnqueueForProcessingAsync(assetId, ct)))
            .RequireRateLimiting("PerUserWrite");

        admin.MapPost("/media/{assetId}/complete-processing", async (string assetId, CompleteProcessingRequest req,
            MediaNormalizationService mediaService, CancellationToken ct)
            => (await mediaService.CompleteProcessingAsync(assetId, req.ThumbnailPath, req.CaptionPath, req.TranscriptPath, ct)) is not null
                ? Results.Ok() : Results.NotFound())
            .RequireRateLimiting("PerUserWrite");

        admin.MapGet("/media/audit", async (MediaNormalizationService mediaService, CancellationToken ct)
            => Results.Ok(await mediaService.AuditMediaAssetsAsync(ct)));

        admin.MapGet("/media/normalization-plan", (string mimeType)
            => Results.Ok(MediaNormalizationService.GetNormalizationPlan(mimeType)));

        return app;
    }

    private static string AdminId(HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated admin id is required.");

    private static string UserId(HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}

public record ImportBatchCreateRequest(string Title);
public record DesignateCanonicalRequest(string CanonicalItemId);
public record ContentValidateRequest(string SubtestCode, string DetailJson, string? ModelAnswerJson);
public record MockAssembleRequest(string? ProfessionId, string? Language);
public record DiagnosticGenerateRequest(string? ProfessionId);
public record EligibilityPatchRequest(bool? IsMockEligible, bool? IsDiagnosticEligible);
public record CompleteProcessingRequest(string? ThumbnailPath, string? CaptionPath, string? TranscriptPath);
public record BulkImportRequest(string? BatchTitle, List<ContentImportRow> Rows);
