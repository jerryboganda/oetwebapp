using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Conversation;

namespace OetLearner.Api.Endpoints;

public static class ConversationEndpoints
{
    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");
        var conv = v1.MapGroup("/conversations");

        conv.MapGet("/task-types", (ConversationService svc) =>
            Results.Ok(svc.GetTaskTypeCatalog()));

        conv.MapGet("/entitlement", async (HttpContext http, ConversationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetEntitlementAsync(http.UserId(), ct)));

        conv.MapPost("/", async (HttpContext http, ConversationCreateSessionRequest request, ConversationService svc, CancellationToken ct) =>
            Results.Ok(await svc.CreateSessionAsync(http.UserId(), request, ct)));

        conv.MapGet("/{sessionId}", async (string sessionId, HttpContext http, ConversationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetSessionAsync(http.UserId(), sessionId, ct)));

        conv.MapPost("/{sessionId}/resume", async (string sessionId, ConversationResumeSessionRequest request, HttpContext http, ConversationService svc, CancellationToken ct) =>
            Results.Ok(await svc.ResumeSessionAsync(http.UserId(), sessionId, request, ct)));

        conv.MapGet("/{sessionId}/transcript/export", async (string sessionId, [FromQuery] string? format, HttpContext http, ConversationService svc, IConversationTranscriptExportService exporter, CancellationToken ct) =>
        {
            var export = await svc.ExportTranscriptAsync(http.UserId(), sessionId, format ?? "txt", exporter, ct);
            return Results.File(export.Content, export.ContentType, export.FileName);
        });

        conv.MapPost("/{sessionId}/complete", async (string sessionId, HttpContext http, ConversationService svc, CancellationToken ct) =>
            Results.Ok(await svc.CompleteSessionAsync(http.UserId(), sessionId, ct)));

        conv.MapGet("/{sessionId}/evaluation", async (string sessionId, HttpContext http, ConversationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetEvaluationAsync(http.UserId(), sessionId, ct)));

        conv.MapGet("/history", async (HttpContext http, [FromQuery] int page, [FromQuery] int pageSize, ConversationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetHistoryAsync(http.UserId(), page <= 0 ? 1 : page, pageSize <= 0 ? 10 : pageSize, ct)));

        conv.MapGet("/media/{fileName}", async (string fileName, IConversationAudioService audio, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(fileName) || !fileName.Contains('.'))
                return Results.BadRequest(new { error = "invalid_filename" });
            var dot = fileName.LastIndexOf('.');
            var sha = fileName[..dot];
            var ext = fileName[(dot + 1)..];
            if (sha.Length < 4 || sha.Any(c => !Uri.IsHexDigit(c)))
                return Results.BadRequest(new { error = "invalid_filename" });
            var key = $"conversation/audio/{sha[..2]}/{sha.Substring(2, 2)}/{sha}.{ext}";
            var stream = await audio.OpenReadAsync(key, ct);
            if (stream is null) return Results.NotFound();
            var mime = ext.ToLowerInvariant() switch
            {
                "mp3" => "audio/mpeg", "webm" => "audio/webm", "ogg" => "audio/ogg",
                "wav" => "audio/wav", "m4a" => "audio/mp4", _ => "application/octet-stream",
            };
            return Results.Stream(stream, mime);
        });

        // Backwards-compatibility alias kept for clients that shipped the singular
        // route during the phase-2 rollout planning window.
        v1.MapPost("/conversation/sessions/{sessionId}/resume", async (string sessionId, ConversationResumeSessionRequest request, HttpContext http, ConversationService svc, CancellationToken ct) =>
            Results.Ok(await svc.ResumeSessionAsync(http.UserId(), sessionId, request, ct)));

        return app;
    }
}

file static class ConversationHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
