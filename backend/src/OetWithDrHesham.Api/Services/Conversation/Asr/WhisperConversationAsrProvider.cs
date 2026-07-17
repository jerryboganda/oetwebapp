using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Ai;
using OetWithDrHesham.Api.Services.Rulebook;
using OetWithDrHesham.Api.Services.Settings;

namespace OetWithDrHesham.Api.Services.Conversation.Asr;

public sealed class WhisperConversationAsrProvider(
    IHttpClientFactory httpClientFactory,
    IConversationOptionsProvider optionsProvider,
    IRuntimeSettingsProvider runtimeSettings,
    IDirectAiCallRecorder usageRecorder,
    TimeProvider clock,
    ILogger<WhisperConversationAsrProvider> logger) : IConversationAsrProvider
{
    private const string WhisperProviderCode = "whisper-asr";

    public string Name => "whisper";
    public bool IsConfigured =>
        IsConversationConfigured(optionsProvider.Current)
        || (runtimeSettings.CurrentSnapshot?.Effective.SpeakingWhisper.IsConfigured ?? false);

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var options = await optionsProvider.GetAsync(ct);
        if (IsConversationConfigured(options)) return true;
        return (await runtimeSettings.GetAsync(ct)).SpeakingWhisper.IsConfigured;
    }

    public async Task<ConversationAsrResult> TranscribeAsync(ConversationAsrRequest request, CancellationToken ct)
    {
        // Resolve credentials: own ConversationOptions → admin-panel SpeakingWhisper (shared key).
        var opts = await optionsProvider.GetAsync(ct);
        var apiKey = string.IsNullOrWhiteSpace(opts.WhisperApiKey) ? null : opts.WhisperApiKey;
        var whisperUrl = string.IsNullOrWhiteSpace(opts.WhisperBaseUrl) ? null : opts.WhisperBaseUrl;
        var whisperModel = string.IsNullOrWhiteSpace(opts.WhisperModel) ? null : opts.WhisperModel;

        if (apiKey is null || whisperUrl is null)
        {
            var adminWhisper = (await runtimeSettings.GetAsync(ct)).SpeakingWhisper;
            apiKey ??= adminWhisper.ApiKey;
            whisperUrl ??= adminWhisper.BaseUrl;
            whisperModel ??= adminWhisper.Model;
        }

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(whisperUrl))
            throw new InvalidOperationException("Whisper provider is not configured.");

        var resolvedModel = whisperModel ?? "whisper-1";
        var client = httpClientFactory.CreateClient("ConversationWhisperClient");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        var url = $"{whisperUrl.TrimEnd('/')}/audio/transcriptions";

        var sttStartedAt = clock.GetUtcNow();
        var sttContext = new AiUsageContext(
            UserId: null, AuthAccountId: null, TenantId: null,
            FeatureCode: AiFeatureCodes.SttConversationTranscribe,
            RulebookVersion: null, PromptTemplateId: null, SystemPrompt: null, UserPrompt: null,
            StartedAt: sttStartedAt);
        int SttLatencyMs() => (int)(clock.GetUtcNow() - sttStartedAt).TotalMilliseconds;

        using var form = new MultipartFormDataContent();
        var audioContent = new StreamContent(request.Audio);
        // MediaRecorder sends parametered mime types (e.g. "audio/webm;codecs=opus").
        // The MediaTypeHeaderValue(string) ctor rejects parameters and throws a
        // FormatException, so parse it (which accepts parameters) and fall back to
        // the base type if that fails — otherwise the whole turn fails before the
        // request is even sent to Whisper.
        audioContent.Headers.ContentType =
            MediaTypeHeaderValue.TryParse(request.AudioMimeType, out var parsedMime)
                ? parsedMime
                : new MediaTypeHeaderValue(request.AudioMimeType.Split(';')[0].Trim() is { Length: > 0 } baseMime
                    ? baseMime
                    : "audio/webm");
        form.Add(audioContent, "file", GuessFileName(request.AudioMimeType));
        form.Add(new StringContent(resolvedModel), "model");
        var locale = request.Locale ?? "en-GB";
        var lang = locale.Length >= 2 ? locale.Substring(0, 2) : "en";
        form.Add(new StringContent(lang), "language");
        form.Add(new StringContent("verbose_json"), "response_format");

        using var response = await client.PostAsync(url, form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Whisper ASR returned status {Status}: {Body}", (int)response.StatusCode, body);
            await usageRecorder.RecordFailureAsync(
                sttContext, WhisperProviderCode, resolvedModel, AiCallOutcome.ProviderError,
                $"http_{(int)response.StatusCode}", body, SttLatencyMs(), "stt.conversation", ct);
            throw new ConversationAsrException("whisper_error", $"Whisper returned {(int)response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var text = root.TryGetProperty("text", out var t) ? (t.GetString() ?? "").Trim() : "";
        double? duration = root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : null;
        double confidence = 0.85;
        var speakerSegments = new List<ConversationSpeakerSegment>();
        if (root.TryGetProperty("segments", out var segs) && segs.ValueKind == JsonValueKind.Array)
        {
            var probs = new List<double>();
            foreach (var s in segs.EnumerateArray())
            {
                if (s.TryGetProperty("avg_logprob", out var lp) && lp.ValueKind == JsonValueKind.Number)
                    probs.Add(Math.Exp(lp.GetDouble()));
                if (request.EnableDiarization)
                {
                    var segmentText = s.TryGetProperty("text", out var st) ? (st.GetString() ?? "").Trim() : "";
                    if (!string.IsNullOrWhiteSpace(segmentText))
                    {
                        var startMs = s.TryGetProperty("start", out var start) && start.ValueKind == JsonValueKind.Number
                            ? (int)(start.GetDouble() * 1000)
                            : 0;
                        var endMs = s.TryGetProperty("end", out var end) && end.ValueKind == JsonValueKind.Number
                            ? (int)(end.GetDouble() * 1000)
                            : startMs;
                        var speaker = s.TryGetProperty("speaker", out var sp) ? sp.GetRawText().Trim('"') : "learner";
                        speakerSegments.Add(new ConversationSpeakerSegment(speaker, segmentText, startMs, endMs, null));
                    }
                }
            }
            if (probs.Count > 0) confidence = Math.Clamp(probs.Average(), 0.0, 1.0);
        }

        await usageRecorder.RecordSuccessAsync(
            sttContext, WhisperProviderCode, resolvedModel, usage: null,
            SttLatencyMs(), $"stt.chars={text.Length}", costEstimateUsd: 0m, ct);

        return new ConversationAsrResult(
            text, confidence, duration.HasValue ? (int)(duration.Value * 1000) : 0,
            lang, Name, $"whisper {text.Length} chars",
            request.EnableDiarization ? speakerSegments : null);
    }

    private static string GuessFileName(string mime)
    {
        // Normalise the parametered mime ("audio/webm;codecs=opus") to its base
        // type before mapping — otherwise the switch misses and we upload
        // "audio.bin", which OpenAI Whisper rejects with HTTP 400 (it detects the
        // format from the file extension). Default to .webm (the MediaRecorder
        // default) rather than .bin so an unlabelled clip still transcodes.
        var baseMime = (mime ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        return baseMime switch
        {
            "audio/wav" or "audio/x-wav" => "audio.wav",
            "audio/webm" => "audio.webm",
            "audio/ogg" => "audio.ogg",
            "audio/mpeg" or "audio/mp3" => "audio.mp3",
            "audio/mp4" => "audio.m4a",
            _ => "audio.webm",
        };
    }

    private static bool IsConversationConfigured(ConversationOptions? options)
        => options is not null
           && !string.IsNullOrWhiteSpace(options.WhisperApiKey)
           && !string.IsNullOrWhiteSpace(options.WhisperBaseUrl);
}
