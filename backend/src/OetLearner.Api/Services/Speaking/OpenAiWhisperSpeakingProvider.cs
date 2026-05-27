using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// 2026-05-27 audit fix — RULE_40 (Speaking tone of voice). The audit
/// recommended OpenAI Whisper as the transcription provider so we get
/// segment-level timing and confidence we can derive proxy acoustic
/// metrics from (mean confidence, segment cadence, pause spread). The
/// metrics feed <see cref="SpeakingToneAssessor"/> which produces the
/// RULE_40 tone score (0–3 Adept / Competent / Partially effective /
/// Ineffective) on Breaking Bad News cards.
///
/// Configuration:
///   "Speaking:Whisper:ApiKey"   — OpenAI API key (or compatible gateway).
///   "Speaking:Whisper:BaseUrl"  — defaults to https://api.openai.com/v1
///   "Speaking:Whisper:Model"    — defaults to "whisper-1"
///
/// When the API key is missing the provider falls back to advertising
/// itself as unconfigured; DI will continue to bind the Mock provider as
/// before and the pipeline stays usable in tests / local dev.
/// </summary>
public sealed class OpenAiWhisperSpeakingProvider : ISpeakingTranscriptionProvider
{
    private readonly IHttpClientFactory _http;
    private readonly IFileStorage _storage;
    private readonly IRuntimeSettingsProvider _runtimeSettings;
    private readonly ILogger<OpenAiWhisperSpeakingProvider> _logger;

    public OpenAiWhisperSpeakingProvider(
        IHttpClientFactory http,
        IFileStorage storage,
        IRuntimeSettingsProvider runtimeSettings,
        ILogger<OpenAiWhisperSpeakingProvider> logger)
    {
        _http = http;
        _storage = storage;
        _runtimeSettings = runtimeSettings;
        _logger = logger;
    }

    public string ProviderCode => "openai-whisper";

    /// <summary>
    /// Synchronous check used at DI bind time + by the pipeline router. Reads
    /// the cached effective settings; the cache TTL is 30s so admin-panel
    /// changes propagate quickly. Falls back to false when settings haven't
    /// finished loading on cold start.
    /// </summary>
    public bool IsConfigured
    {
        get
        {
            try
            {
                var snapshot = _runtimeSettings.GetAsync().GetAwaiter().GetResult();
                return snapshot.SpeakingWhisper.IsConfigured;
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task<SpeakingTranscriptionProviderResult> TranscribeAsync(
        string mediaAssetReference,
        string language,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mediaAssetReference))
            throw new ArgumentException("mediaAssetReference is required.", nameof(mediaAssetReference));

        var settings = (await _runtimeSettings.GetAsync(ct)).SpeakingWhisper;
        if (!settings.IsConfigured)
            throw new InvalidOperationException("OpenAI Whisper Speaking provider is not configured. Set the API key from Admin → Speaking → Whisper, or via Speaking:Whisper:ApiKey in appsettings.");

        using var client = _http.CreateClient("SpeakingWhisperClient");
        var url = $"{settings.BaseUrl.TrimEnd('/')}/audio/transcriptions";

        HttpClient? audioClient = null;
        Stream? audioStream = null;
        HttpResponseMessage? audioResponse = null;
        try
        {
            string mediaType;
            if (Uri.TryCreate(mediaAssetReference, UriKind.Absolute, out var audioUri)
                && (audioUri.Scheme == Uri.UriSchemeHttp || audioUri.Scheme == Uri.UriSchemeHttps))
            {
                audioClient = _http.CreateClient();
                audioResponse = await audioClient.GetAsync(audioUri, HttpCompletionOption.ResponseHeadersRead, ct);
                audioResponse.EnsureSuccessStatusCode();
                audioStream = await audioResponse.Content.ReadAsStreamAsync(ct);
                mediaType = audioResponse.Content.Headers.ContentType?.MediaType
                    ?? GuessMediaTypeFromReference(mediaAssetReference);
            }
            else
            {
                audioStream = await _storage.OpenReadAsync(mediaAssetReference, ct);
                mediaType = GuessMediaTypeFromReference(mediaAssetReference);
            }

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

            using var form = new MultipartFormDataContent();
            var audioContent = new StreamContent(audioStream);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            form.Add(audioContent, "file", GuessFileName(mediaType));
            form.Add(new StringContent(settings.Model), "model");
            form.Add(new StringContent(language.Length >= 2 ? language[..2] : "en"), "language");
            form.Add(new StringContent("verbose_json"), "response_format");
            form.Add(new StringContent("segment"), "timestamp_granularities[]");

            using var response = await client.PostAsync(url, form, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Speaking Whisper returned {Status}: {Body}", (int)response.StatusCode, body);
                throw new InvalidOperationException($"Speaking Whisper returned status {(int)response.StatusCode}.");
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var resolvedLanguage = root.TryGetProperty("language", out var lang) && lang.ValueKind == JsonValueKind.String
                ? lang.GetString() ?? language
                : language;

            var segments = new List<object>();
            var probs = new List<double>();
            var totalWords = 0;
            if (root.TryGetProperty("segments", out var segs) && segs.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in segs.EnumerateArray())
                {
                    var text = s.TryGetProperty("text", out var t) ? (t.GetString() ?? "").Trim() : "";
                    var startMs = s.TryGetProperty("start", out var start) && start.ValueKind == JsonValueKind.Number
                        ? (int)(start.GetDouble() * 1000)
                        : 0;
                    var endMs = s.TryGetProperty("end", out var end) && end.ValueKind == JsonValueKind.Number
                        ? (int)(end.GetDouble() * 1000)
                        : startMs;
                    double? confidence = null;
                    if (s.TryGetProperty("avg_logprob", out var lp) && lp.ValueKind == JsonValueKind.Number)
                    {
                        confidence = Math.Clamp(Math.Exp(lp.GetDouble()), 0.0, 1.0);
                        probs.Add(confidence.Value);
                    }
                    totalWords += text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                    segments.Add(new
                    {
                        speaker = "candidate",
                        startMs,
                        endMs,
                        text,
                        confidence,
                        words = Array.Empty<object>(),
                    });
                }
            }

            var meanConfidence = probs.Count > 0 ? probs.Average() : 0.85;
            var segmentsJson = JsonSerializer.Serialize(segments);

            return new SpeakingTranscriptionProviderResult
            {
                Provider = ProviderCode,
                Language = resolvedLanguage,
                SegmentsJson = segmentsJson,
                WordCount = totalWords,
                MeanConfidence = meanConfidence,
                Model = settings.Model,
            };
        }
        finally
        {
            audioStream?.Dispose();
            audioResponse?.Dispose();
            audioClient?.Dispose();
        }
    }

    private static string GuessMediaTypeFromReference(string reference)
    {
        var path = reference.Split('?', 2)[0].ToLowerInvariant();
        return Path.GetExtension(path) switch
        {
            ".m4a" or ".mp4" => "audio/mp4",
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".ogg" or ".oga" => "audio/ogg",
            ".webm" => "audio/webm",
            _ => "audio/webm",
        };
    }

    private static string GuessFileName(string mediaType) => mediaType switch
    {
        "audio/mp4" or "audio/m4a" => "audio.m4a",
        "audio/wav" or "audio/x-wav" => "audio.wav",
        "audio/mpeg" => "audio.mp3",
        "audio/ogg" => "audio.ogg",
        _ => "audio.webm",
    };
}

/// <summary>
/// Bindable configuration section for the Speaking Whisper provider.
/// Mapped from <c>Speaking:Whisper</c> in appsettings.
/// </summary>
public sealed class WhisperSpeakingOptions
{
    public string? ApiKey { get; set; }
    public string? BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "whisper-1";
}
