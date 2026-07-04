using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.VideoLibrary;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Live Bunny Stream collection management, mapped inside the
/// <c>/v1/admin/video-library</c> group by <see cref="VideoLibraryAdminEndpoints"/>.
/// Reads collections and their videos straight from Bunny (the source of truth
/// for membership) and bridges Bunny-native videos into the catalog via import.
/// Irreversible operations (collection delete, direct Bunny video delete)
/// require the system-admin policy and are audited. This surface never mints or
/// exposes a playback URL — playback stays attested-only.
/// </summary>
internal static class VideoLibraryCollectionEndpoints
{
    internal static RouteGroupBuilder MapVideoLibraryCollectionEndpoints(this RouteGroupBuilder admin)
    {
        // ── Collections ─────────────────────────────────────────────────────

        admin.MapGet("/collections", async (
            [FromQuery] int? page,
            [FromQuery] int? itemsPerPage,
            [FromQuery] string? search,
            BunnyCollectionAdminService service,
            CancellationToken ct) =>
        {
            var pageNumber = Math.Max(1, page ?? 1);
            var perPage = itemsPerPage is null or < 1 or > 100 ? 100 : itemsPerPage.Value;
            try
            {
                return Results.Ok(await service.ListAsync(pageNumber, perPage, search, ct));
            }
            catch (BunnyNotConfiguredException)
            {
                return BunnyNotConfigured();
            }
        })
        .WithAdminRead("AdminContentRead");

        admin.MapPost("/collections", async (
            AdminCollectionNameRequest request,
            BunnyCollectionAdminService service,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.CreateAsync(request.Name ?? string.Empty, ct));
            }
            catch (BunnyNotConfiguredException)
            {
                return BunnyNotConfigured();
            }
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/collections/{collectionId}", async (
            string collectionId,
            AdminCollectionNameRequest request,
            BunnyCollectionAdminService service,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.RenameAsync(collectionId, request.Name ?? string.Empty, ct));
            }
            catch (BunnyNotConfiguredException)
            {
                return BunnyNotConfigured();
            }
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapDelete("/collections/{collectionId}", async (
            HttpContext http,
            string collectionId,
            BunnyCollectionAdminService service,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            try
            {
                await service.DeleteAsync(collectionId, ct);
                db.AuditEvents.Add(new AuditEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ActorId = http.AdminId(),
                    ActorName = http.AdminName(),
                    Action = "BunnyCollectionDeleted",
                    ResourceType = "bunny_collection",
                    ResourceId = collectionId,
                    OccurredAt = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { deleted = true });
            }
            catch (BunnyNotConfiguredException)
            {
                return BunnyNotConfigured();
            }
        })
        .WithAdminWrite("AdminSystemAdmin");

        // ── Videos inside a collection ───────────────────────────────────────

        admin.MapGet("/collections/{collectionId}/videos", async (
            string collectionId,
            [FromQuery] int? page,
            [FromQuery] int? itemsPerPage,
            [FromQuery] string? search,
            BunnyCollectionAdminService service,
            CancellationToken ct) =>
        {
            var pageNumber = Math.Max(1, page ?? 1);
            var perPage = itemsPerPage is null or < 1 or > 100 ? 50 : itemsPerPage.Value;
            try
            {
                return Results.Ok(await service.ListVideosAsync(collectionId, pageNumber, perPage, search, ct));
            }
            catch (BunnyNotConfiguredException)
            {
                return BunnyNotConfigured();
            }
        })
        .WithAdminRead("AdminContentRead");

        admin.MapPost("/collections/videos/{bunnyVideoId}/move", async (
            string bunnyVideoId,
            AdminCollectionMoveRequest request,
            BunnyCollectionAdminService service,
            CancellationToken ct) =>
        {
            try
            {
                var target = string.IsNullOrWhiteSpace(request.CollectionId) ? null : request.CollectionId.Trim();
                await service.MoveVideoAsync(bunnyVideoId, target, ct);
                return Results.Ok(new { moved = true });
            }
            catch (BunnyNotConfiguredException)
            {
                return BunnyNotConfigured();
            }
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/collections/videos/{bunnyVideoId}/import", async (
            HttpContext http,
            string bunnyVideoId,
            AdminCollectionImportRequest request,
            BunnyCollectionAdminService service,
            CancellationToken ct) =>
        {
            try
            {
                var detail = await service.ImportFromBunnyAsync(
                    http.AdminId(), bunnyVideoId, request.Title, request.CollectionId, ct);
                return Results.Ok(detail);
            }
            catch (BunnyNotConfiguredException)
            {
                return BunnyNotConfigured();
            }
        })
        .WithAdminWrite("AdminContentWrite");

        admin.MapPost("/collections/videos/{bunnyVideoId}/bunny-delete", async (
            HttpContext http,
            string bunnyVideoId,
            AdminCollectionBunnyDeleteRequest request,
            BunnyCollectionAdminService service,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            if (!request.Force)
            {
                return Results.BadRequest(new
                {
                    code = "force_required",
                    message = "Set force=true to permanently delete this video from Bunny.",
                });
            }
            try
            {
                await service.DeleteBunnyVideoAsync(bunnyVideoId, ct);
                db.AuditEvents.Add(new AuditEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ActorId = http.AdminId(),
                    ActorName = http.AdminName(),
                    Action = "BunnyCollectionVideoDeleted",
                    ResourceType = "bunny_collection",
                    ResourceId = bunnyVideoId,
                    OccurredAt = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { deleted = true });
            }
            catch (BunnyNotConfiguredException)
            {
                return BunnyNotConfigured();
            }
        })
        .WithAdminWrite("AdminSystemAdmin");

        return admin;
    }

    private static IResult BunnyNotConfigured()
        => Results.Json(
            new { code = "bunny_not_configured", message = "Configure Bunny Stream in Admin → Settings first." },
            statusCode: StatusCodes.Status503ServiceUnavailable);

    // Private (not internal) to match the sibling endpoint classes — each keeps
    // its own copy so http.AdminId() is unambiguous across the namespace.
    private static string AdminId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated admin id is required.");

    private static string AdminName(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Admin";
}

public sealed record AdminCollectionNameRequest(string? Name);
public sealed record AdminCollectionMoveRequest(string? CollectionId);
public sealed record AdminCollectionImportRequest(string? Title, string? CollectionId);
public sealed record AdminCollectionBunnyDeleteRequest(bool Force);
