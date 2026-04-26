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
    private ConversationOptions ReadOptions() => optionsProvider.Snapshot();

    public string Name => "elevenlabs";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ReadOptions().ElevenLabsApiKey);

    public async Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("ElevenLabs not configured.");

        var client = httpClientFactory.CreateClient("ConversationElevenLabsClient");
        var voice = string.IsNullOrWhiteSpace(request.Voice) ? ReadOptions().ElevenLabsDefaultVoiceId : request.Voice;
        var model = string.IsNullOrWhiteSpace(ReadOptions().ElevenLabsModel) ? "eleven_multilingual_v2" : ReadOptions().ElevenLabsModel;
        var url = $"https://api.elevenlabs.io/v1/text-to-speech/{Uri.EscapeDataString(voice)}";

        var payload = JsonSerializer.Serialize(new
        {
            text = request.Text,
            model_id = model,
            voice_settings = new { stability = 0.5, similarity_boost = 0.75, style = 0.0, use_speaker_boost = true },
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("xi-api-key", ReadOptions().ElevenLabsApiKey);
        req.Headers.Add("Accept", "audio/mpeg");
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("ElevenLabs TTS {Status}: {Err}", (int)response.StatusCode, err);
            throw new ConversationTtsException("elevenlabs_tts_error", $"ElevenLabs {(int)response.StatusCode}");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return new ConversationTtsResult(bytes, "audio/mpeg",
            AzureConversationTtsProvider.ApproxDurationMs(request.Text), Name, $"elevenlabs voice={voice}");
    }
}
