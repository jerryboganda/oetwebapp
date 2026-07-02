using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Settings;
using OetLearner.Api.Services.VideoLibrary;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Bunny Stream encode webhook (mirrors StripeWebhookEndpoints wiring).
/// Bunny cannot sign requests, so authentication is a shared secret carried
/// as a query parameter and compared in constant time. While the webhook
/// secret is unconfigured (dormant feature) every call is 401.
/// </summary>
public static class VideoLibraryWebhookEndpoints
{
    public static IEndpointRouteBuilder MapVideoLibraryWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/webhooks/bunny-stream", HandleBunnyWebhook).AllowAnonymous();
        return app;
    }

    private static async Task<IResult> HandleBunnyWebhook(HttpContext http, CancellationToken ct)
    {
        var settingsProvider = http.RequestServices.GetRequiredService<IRuntimeSettingsProvider>();
        var logger = http.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("VideoLibraryWebhookEndpoints");

        var settings = (await settingsProvider.GetAsync(ct)).BunnyStream;
        var providedSecret = http.Request.Query["secret"].ToString();
        if (string.IsNullOrWhiteSpace(settings.WebhookSecret)
            || !FixedTimeEquals(settings.WebhookSecret, providedSecret))
        {
            logger.LogWarning("Rejected Bunny webhook: missing/invalid secret (dormant={Dormant}).",
                string.IsNullOrWhiteSpace(settings.WebhookSecret));
            return Results.Unauthorized();
        }

        // Parse leniently — Bunny sends {VideoLibraryId, VideoGuid, Status}.
        string? videoGuid = null;
        string? libraryId = null;
        int? status = null;
        try
        {
            using var doc = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                libraryId = ReadFlexibleString(doc.RootElement, "VideoLibraryId", "videoLibraryId");
                videoGuid = ReadFlexibleString(doc.RootElement, "VideoGuid", "videoGuid");
                status = ReadFlexibleInt(doc.RootElement, "Status", "status");
            }
        }
        catch (JsonException)
        {
            logger.LogWarning("Bunny webhook payload was not valid JSON; acknowledging with 200.");
            return Results.Ok(new { processed = false, reason = "invalid_payload" });
        }

        if (string.IsNullOrWhiteSpace(videoGuid) || status is null)
        {
            logger.LogWarning("Bunny webhook payload missing VideoGuid/Status; acknowledging with 200.");
            return Results.Ok(new { processed = false, reason = "missing_fields" });
        }

        if (!string.Equals(libraryId, settings.LibraryId, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Bunny webhook library mismatch: got {Got}, expected {Expected}; ignoring.",
                libraryId, settings.LibraryId);
            return Results.Ok(new { processed = false, reason = "library_mismatch" });
        }

        var db = http.RequestServices.GetRequiredService<LearnerDbContext>();
        var video = await db.LibraryVideos.FirstOrDefaultAsync(v => v.BunnyVideoId == videoGuid, ct);
        if (video is null)
        {
            logger.LogWarning("Bunny webhook for unknown video guid {VideoGuid}; acknowledging with 200.", videoGuid);
            return Results.Ok(new { processed = false, reason = "unknown_video" });
        }

        var mapped = VideoLibraryAdminService.MapBunnyStatus(status.Value);
        video.EncodeStatus = mapped;
        video.EncodeError = mapped == VideoEncodeStatus.Failed
            ? "Bunny Stream reported the encode as failed."
            : null;

        if (mapped == VideoEncodeStatus.Ready)
        {
            // Pull full metadata (duration, thumbnail, resolutions) on Ready.
            var bunny = http.RequestServices.GetRequiredService<IBunnyStreamClient>();
            try
            {
                var info = await bunny.GetVideoAsync(videoGuid, ct);
                VideoLibraryAdminService.ApplyBunnyInfo(video, info);
            }
            catch (Exception ex) when (ex is BunnyNotConfiguredException or ApiException)
            {
                logger.LogWarning(ex,
                    "Bunny webhook marked video {VideoId} Ready but the metadata pull failed; the reconciliation worker will retry.",
                    video.Id);
                video.EncodeProgress = 100;
            }
        }

        video.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Bunny webhook processed for video {VideoId}: bunny status {Status} → {EncodeStatus}.",
            video.Id, status, video.EncodeStatus);
        return Results.Ok(new { processed = true });
    }

    private static bool FixedTimeEquals(string expected, string? provided)
    {
        if (string.IsNullOrEmpty(provided)) return false;
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    private static string? ReadFlexibleString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var el))
            {
                if (el.ValueKind == JsonValueKind.String) return el.GetString();
                if (el.ValueKind == JsonValueKind.Number) return el.GetRawText();
            }
        }
        return null;
    }

    private static int? ReadFlexibleInt(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var el))
            {
                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i)) return i;
                if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var s)) return s;
            }
        }
        return null;
    }
}
