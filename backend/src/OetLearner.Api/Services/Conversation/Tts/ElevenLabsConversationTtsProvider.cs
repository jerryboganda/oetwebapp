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
    private ConversationOptions ReadOptions() => optionsProvider.GetAsync().GetAwaiter().GetResult();

    public string Name => "elevenlabs";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ReadOptions().ElevenLabsApiKey);

    public async Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("ElevenLabs not configured.");

        var options = ReadOptions();
        var client = httpClientFactory.CreateClient("ConversationElevenLabsClient");
        var voice = string.IsNullOrWhiteSpace(request.Voice) ? options.ElevenLabsDefaultVoiceId : request.Voice;
        var model = string.IsNullOrWhiteSpace(request.ModelVariant)
            ? (string.IsNullOrWhiteSpace(options.ElevenLabsModel) ? "eleven_multilingual_v2" : options.ElevenLabsModel)
            : request.ModelVariant;
        var outputFormat = NormalizeMp3OutputFormat(options.ElevenLabsOutputFormat);
        var url = $"https://api.elevenlabs.io/v1/text-to-speech/{Uri.EscapeDataString(voice)}?output_format={Uri.EscapeDataString(outputFormat)}";

        var dictionaryLocators = !string.IsNullOrWhiteSpace(options.ElevenLabsPronunciationDictionaryId)
            ? new[]
            {
                new
                {
                    pronunciation_dictionary_id = options.ElevenLabsPronunciationDictionaryId,
                    version_id = string.IsNullOrWhiteSpace(options.ElevenLabsPronunciationDictionaryVersionId)
                        ? null
                        : options.ElevenLabsPronunciationDictionaryVersionId,
                },
            }
            : null;

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
        if (dictionaryLocators is not null)
        {
            payloadMap["pronunciation_dictionary_locators"] = dictionaryLocators;
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
            AzureConversationTtsProvider.ApproxDurationMs(request.Text), Name, $"elevenlabs voice={voice}");
    }

    private static string NormalizeMp3OutputFormat(string? outputFormat)
        => !string.IsNullOrWhiteSpace(outputFormat)
           && outputFormat.StartsWith("mp3_", StringComparison.OrdinalIgnoreCase)
            ? outputFormat.Trim()
            : "mp3_44100_128";
}
