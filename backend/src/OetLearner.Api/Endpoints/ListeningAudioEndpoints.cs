using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Endpoints;

// ═════════════════════════════════════════════════════════════════════════════
// Listening diagnostic audio serving — capability-by-SHA.
//
// The diagnostic player renders a bare <audio src={url}> element. A browser
// media element cannot attach the bearer JWT (the access token lives in JS
// memory, not a cookie), and the API only honours `access_token` query params
// on SignalR hub paths — so an authorized media route is unreachable here.
//
// Instead we serve the synthesized diagnostic audio under an *anonymous*
// route keyed by its SHA-256 content hash. The 256-bit hash is itself the
// capability: it is unguessable, content-addressed, and only ever handed to a
// learner who has already been issued the diagnostic question payload (which
// requires LearnerOnly auth to fetch). This mirrors how content-addressed CDNs
// gate access by unguessable URL.
//
// The route is rate-limited (PerUser falls back to per-IP for anonymous
// callers) and only resolves keys under the `listening/tts` published root, so
// it cannot be used to probe arbitrary storage keys.
// ═════════════════════════════════════════════════════════════════════════════

public static class ListeningAudioEndpoints
{
    private const string TtsRootKey = "listening/tts";

    // Candidate extensions probed in priority order. ListeningTtsService writes
    // .wav today; the others are accepted so a future provider that emits a
    // compressed format keeps working without a URL contract change.
    private static readonly string[] CandidateExtensions = ["wav", "mp3", "m4a", "ogg"];

    public static IEndpointRouteBuilder MapListeningAudioEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/listening/audio/{fileName}", async (
                string fileName,
                IFileStorage storage,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    return Results.BadRequest(new { error = "invalid_filename" });

                // Accept both "{sha}" and "{sha}.{ext}". The extension in the URL
                // is advisory — the actual stored extension is discovered by
                // probing the content-addressed shard.
                var dot = fileName.LastIndexOf('.');
                var sha = dot > 0 ? fileName[..dot] : fileName;

                if (sha.Length != 64 || sha.Any(c => !Uri.IsHexDigit(c)))
                    return Results.BadRequest(new { error = "invalid_filename" });

                sha = sha.ToLowerInvariant();

                foreach (var ext in CandidateExtensions)
                {
                    var key = ContentAddressed.PublishedKey(TtsRootKey, sha, ext);
                    if (!storage.Exists(key)) continue;

                    var stream = await storage.OpenReadAsync(key, ct);
                    return Results.Stream(stream, MimeFor(ext), enableRangeProcessing: true);
                }

                return Results.NotFound();
            })
            .AllowAnonymous()
            .RequireRateLimiting("PerUser")
            .WithName("ListeningServeDiagnosticAudio");

        return app;
    }

    private static string MimeFor(string ext) => ext.ToLowerInvariant() switch
    {
        "mp3" => "audio/mpeg",
        "wav" => "audio/wav",
        "m4a" => "audio/mp4",
        "ogg" => "audio/ogg",
        "webm" => "audio/webm",
        _ => "application/octet-stream",
    };
}
