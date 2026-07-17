using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Services.Content;
using OetWithDrHesham.Api.Services.Pronunciation;
using OetWithDrHesham.Api.Services.Settings;

namespace OetWithDrHesham.Api.Services.Speaking;

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
///
/// <para>
/// Unified STT resolution (one key → all speech-to-text): the canonical
/// source is the <c>whisper-asr</c> row in Admin → AI Providers, resolved via
/// the shared <see cref="IPronunciationCredentialResolver"/> (registry-first,
/// 30s-cached, invalidated on every provider mutation). When that row has no
/// key the provider falls back to the legacy <c>Speaking:Whisper</c> /
/// <c>SpeakingWhisper</c> runtime settings so existing deployments are
/// unaffected. Pronunciation and Conversation ASR already read the same row.
/// </para>
/// </summary>
public sealed class OpenAiWhisperSpeakingProvider : ISpeakingTranscriptionProvider
{
    private const string WhisperProviderCode = "whisper-asr";
    private const string DefaultBaseUrl = "https://api.openai.com/v1";
    private const string DefaultModel = "whisper-1";

    private readonly IHttpClientFactory _http;
    private readonly IFileStorage _storage;
    private readonly IRuntimeSettingsProvider _runtimeSettings;
    private readonly IPronunciationCredentialResolver _registryCredentials;
    private readonly OetWithDrHesham.Api.Services.Ai.IDirectAiCallRecorder _usageRecorder;
    private readonly TimeProvider _clock;
    private readonly ILogger<OpenAiWhisperSpeakingProvider> _logger;

    public OpenAiWhisperSpeakingProvider(
        IHttpClientFactory http,
        IFileStorage storage,
        IRuntimeSettingsProvider runtimeSettings,
        IPronunciationCredentialResolver registryCredentials,
        OetWithDrHesham.Api.Services.Ai.IDirectAiCallRecorder usageRecorder,
        TimeProvider clock,
        ILogger<OpenAiWhisperSpeakingProvider> logger)
    {
        _http = http;
        _storage = storage;
        _runtimeSettings = runtimeSettings;
        _registryCredentials = registryCredentials;
        _usageRecorder = usageRecorder;
        _clock = clock;
        _logger = logger;
    }

    public string ProviderCode => "openai-whisper";

    private readonly record struct WhisperCreds(string ApiKey, string BaseUrl, string Model);

    /// <summary>Registry-first / legacy-settings-fallback credential resolution.
    /// Returns null when neither source has a usable key.</summary>
    private async Task<WhisperCreds?> ResolveCredentialsAsync(CancellationToken ct)
    {
        var registry = await _registryCredentials.ResolveAsync(WhisperProviderCode, ct);
        if (registry is not null && !string.IsNullOrWhiteSpace(registry.ApiKey))
        {
            return new WhisperCreds(
                registry.ApiKey,
                string.IsNullOrWhiteSpace(registry.BaseUrl) ? DefaultBaseUrl : registry.BaseUrl!,
                string.IsNullOrWhiteSpace(registry.DefaultModel) ? DefaultModel : registry.DefaultModel!);
        }

        var legacy = (await _runtimeSettings.GetAsync(ct)).SpeakingWhisper;
        if (legacy.IsConfigured)
            return new WhisperCreds(legacy.ApiKey, legacy.BaseUrl, legacy.Model);

        return null;
    }

    /// <summary>
    /// Synchronous check used at DI bind time + by the pipeline router. Reads
    /// only last-known in-memory snapshots; it never performs DB/network I/O.
    /// </summary>
    public bool IsConfigured
        => _registryCredentials.IsRegistryConfigured(WhisperProviderCode)
           || (_runtimeSettings.CurrentSnapshot?.Effective.SpeakingWhisper.IsConfigured ?? false);

    public async Task<SpeakingTranscriptionProviderResult> TranscribeAsync(
        string mediaAssetReference,
        string language,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mediaAssetReference))
            throw new ArgumentException("mediaAssetReference is required.", nameof(mediaAssetReference));

        var creds = await ResolveCredentialsAsync(ct)
            ?? throw new InvalidOperationException("Whisper speech-to-text is not configured. Paste an API key into the 'whisper-asr' row in Admin → AI Providers (covers all speech-to-text), or set Speaking:Whisper:ApiKey in appsettings.");

        using var client = _http.CreateClient("SpeakingWhisperClient");
        var url = $"{creds.BaseUrl.TrimEnd('/')}/audio/transcriptions";

        var startedAt = _clock.GetUtcNow();
        var usageContext = new OetWithDrHesham.Api.Services.Rulebook.AiUsageContext(
            UserId: null, AuthAccountId: null, TenantId: null,
            FeatureCode: Domain.AiFeatureCodes.SttSpeakingTranscribe,
            RulebookVersion: null, PromptTemplateId: null, SystemPrompt: null, UserPrompt: null,
            StartedAt: startedAt);
        int LatencyMs() => (int)(_clock.GetUtcNow() - startedAt).TotalMilliseconds;

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

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", creds.ApiKey);

            using var form = new MultipartFormDataContent();
            var audioContent = new StreamContent(audioStream);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            form.Add(audioContent, "file", GuessFileName(mediaType));
            form.Add(new StringContent(creds.Model), "model");
            form.Add(new StringContent(language.Length >= 2 ? language[..2] : "en"), "language");
            form.Add(new StringContent("verbose_json"), "response_format");
            form.Add(new StringContent("segment"), "timestamp_granularities[]");

            using var response = await client.PostAsync(url, form, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Speaking Whisper returned {Status}: {Body}", (int)response.StatusCode, body);
                await _usageRecorder.RecordFailureAsync(
                    usageContext, WhisperProviderCode, creds.Model,
                    Domain.AiCallOutcome.ProviderError, $"http_{(int)response.StatusCode}",
                    body, LatencyMs(), "stt.speaking", ct);
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

            await _usageRecorder.RecordSuccessAsync(
                usageContext, WhisperProviderCode, creds.Model, usage: null,
                LatencyMs(), $"stt.words={totalWords}", costEstimateUsd: 0m, ct);

            return new SpeakingTranscriptionProviderResult
            {
                Provider = ProviderCode,
                Language = resolvedLanguage,
                SegmentsJson = segmentsJson,
                WordCount = totalWords,
                MeanConfidence = meanConfidence,
                Model = creds.Model,
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
