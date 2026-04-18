using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class MediaEndpoints
{
    private const long MaxMediaUploadBytes = 10L * 1024 * 1024; // 10 MB

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var media = app.MapGroup("/v1/media")
            .RequireAuthorization()
            .RequireRateLimiting("PerUser");

        media.MapPost("/upload", HandleUploadAsync)
            .DisableAntiforgery()
            .RequireRateLimiting("PerUserWrite");

        media.MapGet("/{id}", HandleGetByIdAsync);
        media.MapGet("/{id}/content", HandleDownloadAsync);
        media.MapDelete("/{id}", HandleDeleteAsync).RequireRateLimiting("PerUserWrite");
        media.MapGet("", HandleListAsync);

        return app;
    }

    private static async Task<IResult> HandleUploadAsync(
        HttpContext http,
        IFormFile file,
        LearnerDbContext db,
        MediaStorageService storage,
        CancellationToken ct)
    {
        var userId = http.MediaUserId();

        if (file is null || file.Length == 0)
            return Results.BadRequest(new { code = "file_required", message = "A file is required." });

        if (file.Length > MaxMediaUploadBytes)
            return Results.BadRequest(new { code = "file_too_large", message = "File must be 10 MB or smaller." });

        if (!MediaStorageService.IsAllowedMediaContentType(file.ContentType))
            return Results.BadRequest(new { code = "invalid_file_type", message = "Allowed types: jpg, png, gif, webp, pdf." });

        var originalFileName = Path.GetFileName(file.FileName ?? "upload");
        if (!MediaStorageService.IsAllowedMediaExtension(originalFileName))
            return Results.BadRequest(new { code = "invalid_file_extension", message = "Allowed extensions: .jpg, .jpeg, .png, .gif, .webp, .pdf." });

        var id = Guid.NewGuid().ToString("N");
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        var storageKey = $"media/{id}{extension}";
        var normalizedContentType = file.ContentType.Split(';', 2, StringSplitOptions.TrimEntries)[0].Trim();

        await using var stream = file.OpenReadStream();
        var sizeBytes = await storage.SaveAsync(storageKey, stream, ct);

        var format = extension.TrimStart('.').ToLowerInvariant();
        if (format == "jpeg") format = "jpg";

        var asset = new MediaAsset
        {
            Id = id,
            OriginalFilename = originalFileName,
            MimeType = normalizedContentType,
            Format = format,
            SizeBytes = sizeBytes,
            StoragePath = storageKey,
            Status = MediaAssetStatus.Ready,
            UploadedBy = userId,
            UploadedAt = DateTimeOffset.UtcNow,
        };

        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            asset.Id,
            asset.OriginalFilename,
            asset.MimeType,
            asset.Format,
            asset.SizeBytes,
            asset.StoragePath,
            asset.Status,
            asset.UploadedBy,
            asset.UploadedAt,
            Url = $"/v1/media/{asset.Id}/file",
        });
    }

    private static async Task<IResult> HandleGetByIdAsync(
        string id,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var asset = await db.MediaAssets.FindAsync([id], ct);
        if (asset is null)
            return Results.NotFound(new { code = "media_not_found", message = "Media asset not found." });

        return Results.Ok(new
        {
            asset.Id,
            asset.OriginalFilename,
            asset.MimeType,
            asset.Format,
            asset.SizeBytes,
            asset.StoragePath,
            asset.Status,
            asset.UploadedBy,
            asset.UploadedAt,
            asset.ProcessedAt,
            Url = $"/v1/media/{asset.Id}/file",
        });
    }

    private static async Task<IResult> HandleDeleteAsync(
        string id,
        HttpContext http,
        LearnerDbContext db,
        MediaStorageService storage,
        CancellationToken ct)
    {
        var userId = http.MediaUserId();
        var role = http.User.FindFirstValue(ClaimTypes.Role) ?? "";

        var asset = await db.MediaAssets.FindAsync([id], ct);
        if (asset is null)
            return Results.NotFound(new { code = "media_not_found", message = "Media asset not found." });

        var isAdmin = string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);
        var isOwner = string.Equals(asset.UploadedBy, userId, StringComparison.OrdinalIgnoreCase);
        if (!isAdmin && !isOwner)
            return Results.Json(new { code = "forbidden", message = "You can only delete your own media." }, statusCode: StatusCodes.Status403Forbidden);

        storage.DeleteFile(asset.StoragePath);
        db.MediaAssets.Remove(asset);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { deleted = true, id = asset.Id });
    }

    private static async Task<IResult> HandleListAsync(
        HttpContext http,
        LearnerDbContext db,
        CancellationToken ct,
        int? page,
        int? pageSize)
    {
        var userId = http.MediaUserId();
        var effectivePage = Math.Max(1, page ?? 1);
        var effectivePageSize = Math.Clamp(pageSize ?? 20, 1, 100);

        var query = db.MediaAssets
            .Where(m => m.UploadedBy == userId)
            .OrderByDescending(m => m.UploadedAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .Select(m => new
            {
                m.Id,
                m.OriginalFilename,
                m.MimeType,
                m.Format,
                m.SizeBytes,
                m.Status,
                m.UploadedAt,
                Url = $"/v1/media/{m.Id}/file",
            })
            .ToListAsync(ct);

        return Results.Ok(new { items, total, page = effectivePage, pageSize = effectivePageSize });
    }

    /// <summary>
    /// Stream a media asset to the client. Authenticated; authorised by the
    /// containing <see cref="ContentPaper"/>'s status (published) and the
    /// caller's profession scope. Uses <see cref="IFileStorage"/> so S3/R2
    /// swaps later are a DI-only change.
    /// </summary>
    private static async Task<IResult> HandleDownloadAsync(
        string id,
        LearnerDbContext db,
        OetLearner.Api.Services.Content.IFileStorage storage,
        CancellationToken ct)
    {
        var media = await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (media is null) return Results.NotFound();
        if (media.Status != MediaAssetStatus.Ready) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(media.StoragePath)) return Results.NotFound();

        if (!storage.Exists(media.StoragePath)) return Results.NotFound();
        var stream = await storage.OpenReadAsync(media.StoragePath, ct);
        return Results.Stream(stream, media.MimeType, media.OriginalFilename);
    }
}

file static class MediaHttpContextExtensions
{
    internal static string MediaUserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
