using System.Security.Claims;
using OetLearner.Api.Services.VideoLibrary;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Learner surface of the Video Library. Catalog browsing works everywhere;
/// playback sessions are issued ONLY after native-client attestation
/// (see <see cref="Services.VideoLibrary.VideoAttestationService"/>) — a plain
/// browser can never obtain a signed playback URL.
/// </summary>
public static class VideoLibraryEndpoints
{
    public static IEndpointRouteBuilder MapVideoLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/video-library")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        // ── Catalog ────────────────────────────────────────────────────────

        group.MapGet("/", async (
            HttpContext http,
            VideoLibraryLearnerService service,
            CancellationToken ct) =>
        {
            return Results.Ok(await service.GetHomeAsync(http.LearnerId(), ct));
        })
        .WithName("GetVideoLibraryHome")
        .WithSummary("Video Library home: featured, continue-watching, category shelves.");

        group.MapGet("/videos/{videoId}", async (
            HttpContext http,
            string videoId,
            VideoLibraryLearnerService service,
            CancellationToken ct) =>
        {
            var detail = await service.GetDetailAsync(http.LearnerId(), videoId, ct);
            return detail is null
                ? Results.NotFound(new { code = "video_not_found", message = "Video not found." })
                : Results.Ok(new
                {
                    detail.Summary.Id,
                    detail.Summary.Title,
                    detail.Summary.Description,
                    detail.Summary.DurationSeconds,
                    detail.Summary.ThumbnailUrl,
                    detail.Summary.AccessTier,
                    detail.Summary.IsAccessible,
                    detail.Summary.RequiresUpgrade,
                    detail.Summary.LockReason,
                    detail.Summary.SubtestCode,
                    detail.Summary.Difficulty,
                    detail.Summary.Language,
                    detail.Summary.Tags,
                    detail.Summary.IsFeatured,
                    detail.Summary.PublishedAt,
                    detail.Summary.ViewCount,
                    detail.Summary.Progress,
                    detail.Summary.Bookmarked,
                    detail.Summary.CategoryIds,
                    detail.Chapters,
                    detail.Captions,
                    detail.Attachments,
                    detail.PreviousVideoId,
                    detail.NextVideoId,
                });
        })
        .WithName("GetVideoLibraryVideo")
        .WithSummary("Video detail. NEVER includes a playback URL.");

        // ── Progress / bookmark / telemetry ───────────────────────────────

        group.MapPost("/videos/{videoId}/progress", async (
            HttpContext http,
            string videoId,
            VideoProgressUpdateRequest request,
            VideoLibraryLearnerService service,
            CancellationToken ct) =>
        {
            var result = await service.UpdateProgressAsync(http.LearnerId(), videoId, request.PositionSeconds, ct);
            return result is null
                ? Results.NotFound(new { code = "video_not_found", message = "Video not found." })
                : Results.Ok(result);
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("UpdateVideoLibraryProgress");

        group.MapPost("/videos/{videoId}/bookmark", async (
            HttpContext http,
            string videoId,
            VideoLibraryLearnerService service,
            CancellationToken ct) =>
        {
            var bookmarked = await service.ToggleBookmarkAsync(http.LearnerId(), videoId, ct);
            return bookmarked is null
                ? Results.NotFound(new { code = "video_not_found", message = "Video not found." })
                : Results.Ok(new { bookmarked });
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("ToggleVideoLibraryBookmark");

        group.MapPost("/events", async (
            HttpContext http,
            VideoPlaybackEventRequest request,
            VideoLibraryLearnerService service,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.VideoId))
            {
                return Results.BadRequest(new { code = "video_id_required", message = "videoId is required." });
            }
            var eventType = request.EventType?.Trim().ToLowerInvariant();
            if (eventType is not ("play" or "pause" or "seek" or "heartbeat" or "complete"
                or "error" or "quality_changed" or "session_renewed"))
            {
                return Results.BadRequest(new { code = "invalid_event_type", message = "Unsupported playback event type." });
            }

            var recorded = await service.RecordEventAsync(
                http.LearnerId(), request.VideoId, request.SessionId, eventType, request.PositionSeconds, ct);
            return recorded
                ? Results.NoContent()
                : Results.NotFound(new { code = "video_not_found", message = "Video not found." });
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("RecordVideoLibraryEvent");

        // ── Attested playback (native clients only) ───────────────────────

        group.MapPost("/attestation/challenge", async (
            HttpContext http,
            IVideoAttestationService attestation,
            CancellationToken ct) =>
        {
            var challenge = await attestation.IssueChallengeAsync(http.LearnerId(), ct);
            return Results.Ok(new { nonce = challenge.Nonce, expiresAt = challenge.ExpiresAt });
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("IssueVideoAttestationChallenge");

        group.MapPost("/videos/{videoId}/playback-session", async (
            HttpContext http,
            string videoId,
            PlaybackSessionRequest request,
            VideoLibraryLearnerService learnerService,
            IVideoAttestationService attestation,
            IVideoEntitlementService entitlements,
            IVideoPlaybackSessionService sessions,
            CancellationToken ct) =>
        {
            var userId = http.LearnerId();
            var ip = http.Connection.RemoteIpAddress?.ToString();

            var video = await learnerService.FindVisibleVideoAsync(userId, videoId, DateTimeOffset.UtcNow, ct);
            if (video is null)
            {
                return Results.NotFound(new { code = "video_not_found", message = "Video not found." });
            }

            // (a)–(c) nonce consume + key lookup + HMAC compare (403s + audit inside).
            await attestation.VerifyAsync(
                userId, videoId, request.Nonce, request.Platform, request.KeyId, request.Signature, ip, ct);

            // (d) entitlement — 402 content_locked / 403 frozen|expired.
            await entitlements.RequireAccessAsync(userId, video, ct);

            // (e)+(f) concurrency + Bunny signing.
            try
            {
                var result = await sessions.IssueAsync(
                    userId, video,
                    request.Platform?.Trim() ?? string.Empty,
                    request.KeyId?.Trim() ?? string.Empty,
                    ip,
                    http.Request.Headers.UserAgent.ToString(),
                    ct);
                return Results.Ok(ToPlaybackResponse(result));
            }
            catch (BunnyNotConfiguredException)
            {
                return BunnyNotConfigured();
            }
        })
        .WithName("IssueVideoPlaybackSession")
        .WithSummary("Issues a token-signed playback session to an attested native client.");

        group.MapPost("/playback-sessions/{sessionId}/renew", async (
            HttpContext http,
            string sessionId,
            IVideoPlaybackSessionService sessions,
            CancellationToken ct) =>
        {
            try
            {
                var result = await sessions.RenewAsync(http.LearnerId(), sessionId, ct);
                return Results.Ok(ToPlaybackResponse(result));
            }
            catch (BunnyNotConfiguredException)
            {
                return BunnyNotConfigured();
            }
        })
        .WithName("RenewVideoPlaybackSession");

        return app;
    }

    private static object ToPlaybackResponse(PlaybackSessionResult result) => new
    {
        sessionId = result.SessionId,
        playbackUrl = result.PlaybackUrl,
        expiresAt = result.ExpiresAt,
        watermarkText = result.WatermarkText,
        captions = result.Captions.Select(c => new { languageCode = c.LanguageCode, label = c.Label }),
    };

    private static IResult BunnyNotConfigured()
        => Results.Json(
            new { code = "bunny_not_configured", message = "Video streaming is not configured yet." },
            statusCode: StatusCodes.Status503ServiceUnavailable);

    private static string LearnerId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}

public sealed record VideoProgressUpdateRequest(int PositionSeconds);

public sealed record VideoPlaybackEventRequest(
    string? VideoId,
    string? SessionId,
    string? EventType,
    int PositionSeconds);

public sealed record PlaybackSessionRequest(
    string? Nonce,
    string? Platform,
    string? KeyId,
    string? Signature);
