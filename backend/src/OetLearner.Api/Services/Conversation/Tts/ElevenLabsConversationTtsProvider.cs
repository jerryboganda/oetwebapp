using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Conversation.Tts;

public sealed class ElevenLabsConversationTtsProvider(
    IHttpClientFactory httpClientFactory,
    IConversationOptionsProvider optionsProvider,
    ILogger<ElevenLabsConversationTtsProvider> logger) : IConversationTtsProvider
{
    public string Name => "elevenlabs";
    public bool IsConfigured => IsConfiguredWith(optionsProvider.Current);

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
        => IsConfiguredWith(await optionsProvider.GetAsync(ct));

    public async Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct)
    {
        var options = await optionsProvider.GetAsync(ct);
        if (!IsConfiguredWith(options)) throw new InvalidOperationException("ElevenLabs not configured.");
        var client = httpClientFactory.CreateClient("ConversationElevenLabsClient");
        var voice = string.IsNullOrWhiteSpace(request.Voice) ? options.ElevenLabsDefaultVoiceId : request.Voice;
        var model = string.IsNullOrWhiteSpace(request.ModelVariant)
            ? (string.IsNullOrWhiteSpace(options.ElevenLabsModel) ? "eleven_multilingual_v2" : options.ElevenLabsModel)
            : request.ModelVariant;
        var outputFormat = NormalizeMp3OutputFormat(options.ElevenLabsOutputFormat);
        var baseUrl = ElevenLabsApiEndpoint.NormalizeBaseUrl(options.ElevenLabsTtsBaseUrl);
        var url = $"{baseUrl}/text-to-speech/{Uri.EscapeDataString(voice)}?output_format={Uri.EscapeDataString(outputFormat)}";

        var payloadMap = new Dictionary<string, object?>
        {
            ["text"] = request.Text,
            ["model_id"] = model,
            ["voice_settings"] = new
            {
                stability = options.ElevenLabsStability,
                similarity_boost = options.ElevenLabsSimilarityBoost,
                style = options.ElevenLabsStyle,
                use_speaker_boost = options.ElevenLabsUseSpeakerBoost,
            },
        };

        var locator = ElevenLabsPronunciationLocator.Build(
            options.ElevenLabsPronunciationDictionaryId,
            options.ElevenLabsPronunciationDictionaryVersionId);
        if (locator is not null)
        {
            payloadMap["pronunciation_dictionary_locators"] = new[] { locator };
        }

        var payload = JsonSerializer.Serialize(payloadMap);

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("xi-api-key", options.ElevenLabsApiKey);
        req.Headers.Add("Accept", "audio/mpeg");
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("ElevenLabs TTS returned status {Status}", (int)response.StatusCode);
            throw new ConversationTtsException("elevenlabs_tts_error", $"ElevenLabs {(int)response.StatusCode}");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return new ConversationTtsResult(bytes, "audio/mpeg",
            ConversationTtsDuration.ApproxDurationMs(request.Text), Name, $"elevenlabs voice={voice}");
    }

    private static string NormalizeMp3OutputFormat(string? outputFormat)
        => !string.IsNullOrWhiteSpace(outputFormat)
           && outputFormat.StartsWith("mp3_", StringComparison.OrdinalIgnoreCase)
            ? outputFormat.Trim()
            : "mp3_44100_128";

    private static bool IsConfiguredWith(ConversationOptions? options)
        => options is not null && !string.IsNullOrWhiteSpace(options.ElevenLabsApiKey);
}
