using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Native realtime voice control plane for AI Speaking sessions.
/// Provider media never passes through this API. OpenAI uses a server-created
/// WebRTC session answer; Gemini uses a short-lived, single-use token. These
/// routes never offer a text or batch fallback.
/// </summary>
public static class LiveVoiceEndpoints
{
    public static IEndpointRouteBuilder MapLiveVoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/v1/speaking/realtime/sessions")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("AiLiveSpeaking")
            .WithTags("Speaking realtime voice");

        learner.MapGet("/{id}/preflight", PreflightAsync)
            .WithSummary("Disclose the native realtime voice provider before microphone activation.")
            .Produces<LiveVoicePreflightResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        learner.MapPost("/{id}/openai/offer", OpenAiOfferAsync)
            .WithSummary("Exchange a browser WebRTC offer for an OpenAI Realtime answer.")
            .Produces<LiveVoiceOpenAiOfferResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        learner.MapPost("/{id}/gemini/token", GeminiTokenAsync)
            .WithSummary("Mint a single-use Gemini Live ephemeral token.")
            .Produces<LiveVoiceGeminiTokenResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        learner.MapPost("/{id}/turns", PersistTurnAsync)
            .WithSummary("Persist a completed native provider transcript turn and queue Jev advisory work.")
            .Produces<LiveVoiceTurnResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status409Conflict);

        learner.MapPost("/{id}/transcript", PersistTranscriptAsync)
            .WithSummary("Persist the completed native provider transcript for assessment.")
            .Produces<LiveVoiceTranscriptResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status409Conflict);

        learner.MapPost("/{id}/audio-turns", CaptureCandidateAudioAsync)
            .WithSummary("Persist a consented short microphone clip captured during a live-voice candidate turn.")
            .Produces<LiveVoiceAudioCaptureResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status409Conflict)
            .WithMetadata(
                new RequestSizeLimitAttribute(30L * 1024 * 1024),
                new RequestFormLimitsAttribute { MultipartBodyLengthLimit = 30L * 1024 * 1024 });

        return app;
    }

    private static async Task<IResult> PreflightAsync(
        HttpContext http,
        string id,
        LiveVoiceService liveVoice,
        CancellationToken ct)
    {
        var provider = http.Request.Query["provider"].FirstOrDefault();
        var result = await liveVoice.GetPreflightAsync(ResolveUserId(http), id, provider, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> OpenAiOfferAsync(
        HttpContext http,
        string id,
        [FromBody] LiveVoiceOpenAiOfferRequest request,
        LiveVoiceService liveVoice,
        CancellationToken ct)
    {
        var result = await liveVoice.CreateOpenAiOfferAsync(ResolveUserId(http), id, request, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GeminiTokenAsync(
        HttpContext http,
        string id,
        LiveVoiceService liveVoice,
        CancellationToken ct)
    {
        var result = await liveVoice.CreateGeminiTokenAsync(ResolveUserId(http), id, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> PersistTurnAsync(
        HttpContext http,
        string id,
        [FromBody] LiveVoiceTurnRequest request,
        LiveVoiceService liveVoice,
        CancellationToken ct)
    {
        var result = await liveVoice.PersistTurnAsync(ResolveUserId(http), id, request, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> PersistTranscriptAsync(
        HttpContext http,
        string id,
        [FromBody] LiveVoiceTranscriptRequest request,
        LiveVoiceService liveVoice,
        CancellationToken ct)
    {
        var result = await liveVoice.PersistTranscriptAsync(ResolveUserId(http), id, request, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CaptureCandidateAudioAsync(
        HttpContext http,
        string id,
        LiveVoiceService liveVoice,
        IConversationOptionsProvider conversationOptions,
        CancellationToken ct)
    {
        if (!http.Request.HasFormContentType)
        {
            throw ApiException.Validation(
                "live_voice_audio_multipart_required",
                "Upload candidate audio as multipart/form-data.");
        }

        var form = await http.Request.ReadFormAsync(ct);
        var audioFile = form.Files.GetFile("audio")
            ?? throw ApiException.Validation(
                "live_voice_audio_required",
                "Attach the candidate audio in the audio field.");
        var audioOptions = await conversationOptions.GetAsync(ct);
        if (audioFile.Length <= 0 || audioFile.Length > audioOptions.MaxAudioBytes || audioFile.Length > int.MaxValue)
        {
            throw ApiException.Validation(
                "live_voice_audio_size_invalid",
                "The candidate audio recording is empty or exceeds the configured limit.");
        }
        if (!long.TryParse(form["durationMs"], out var durationMs))
        {
            throw ApiException.Validation(
                "live_voice_audio_duration_invalid",
                "The candidate audio duration is invalid.");
        }

        await using var source = audioFile.OpenReadStream();
        using var buffer = new MemoryStream((int)audioFile.Length);
        await source.CopyToAsync(buffer, ct);
        var stored = await liveVoice.CaptureCandidateAudioAsync(
            ResolveUserId(http),
            id,
            form["providerSessionId"].ToString(),
            buffer.ToArray(),
            audioFile.ContentType,
            durationMs,
            ct);
        return Results.Ok(stored);
    }

    private static string ResolveUserId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw ApiException.Unauthorized(
               "speaking_session_unauthenticated",
               "You must be signed in to use live voice.");
}
