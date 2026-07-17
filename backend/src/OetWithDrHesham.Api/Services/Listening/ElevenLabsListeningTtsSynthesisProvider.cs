using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OetWithDrHesham.Api.Services.Conversation;
using OetWithDrHesham.Api.Services.Conversation.Tts;

namespace OetWithDrHesham.Api.Services.Listening;

/// <summary>
/// ElevenLabs-backed <see cref="IListeningTtsSynthesisProvider"/>. Requests raw
/// 16 kHz mono PCM (<c>output_format=pcm_16000</c>) so the bytes drop straight
/// into <see cref="ListeningTtsService"/>, which prepends its own WAV header and
/// concatenates segments. The admin-configured default voice + model are used;
/// the pronunciation dictionary (when configured) is applied to every segment.
/// </summary>
public sealed class ElevenLabsListeningTtsSynthesisProvider(
    IHttpClientFactory httpClientFactory,
    IConversationOptionsProvider optionsProvider,
    ILogger<ElevenLabsListeningTtsSynthesisProvider> logger) : IListeningTtsSynthesisProvider
{
    public int SampleRateHz => 16_000;

    public async Task<byte[]> SynthesizeAsync(string text, string? speakerHint, CancellationToken ct)
    {
        // Contract: emit silence (no audio) rather than throwing on empty input.
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<byte>();

        var options = await optionsProvider.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(options.ElevenLabsApiKey))
            throw new InvalidOperationException("ElevenLabs is not configured for listening synthesis (missing API key).");

        var voice = options.ElevenLabsDefaultVoiceId;
        var model = string.IsNullOrWhiteSpace(options.ElevenLabsModel) ? "eleven_multilingual_v2" : options.ElevenLabsModel;
        var baseUrl = ElevenLabsApiEndpoint.NormalizeBaseUrl(options.ElevenLabsTtsBaseUrl);
        var url = $"{baseUrl}/text-to-speech/{Uri.EscapeDataString(voice)}?output_format=pcm_16000";

        var payloadMap = new Dictionary<string, object?>
        {
            ["text"] = text,
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

        var client = httpClientFactory.CreateClient("ConversationElevenLabsClient");
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("xi-api-key", options.ElevenLabsApiKey);
        req.Headers.Add("Accept", "audio/pcm");
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("ElevenLabs listening TTS {Status}: {Err}", (int)response.StatusCode, err);
            throw new InvalidOperationException($"ElevenLabs listening TTS failed ({(int)response.StatusCode}): {err}");
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}
