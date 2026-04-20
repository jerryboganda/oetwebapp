using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Conversation.Tts;

/// <summary>
/// ElevenLabs TTS for premium / high-realism voices. Uses text-to-speech
/// endpoint returning MP3 by default.
/// </summary>
public sealed class ElevenLabsConversationTtsProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<ConversationOptions> options,
    ILogger<ElevenLabsConversationTtsProvider> logger) : IConversationTtsProvider
{
    private readonly ConversationOptions _options = options.Value;

    public string Name => "elevenlabs";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ElevenLabsApiKey);

    public async Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("ElevenLabs TTS is not configured.");

        var client = httpClientFactory.CreateClient("ConversationElevenLabsClient");
        var voice = string.IsNullOrWhiteSpace(request.Voice) ? _options.ElevenLabsDefaultVoiceId : request.Voice;
        var model = string.IsNullOrWhiteSpace(_options.ElevenLabsModel) ? "eleven_multilingual_v2" : _options.ElevenLabsModel;
        var url = $"https://api.elevenlabs.io/v1/text-to-speech/{Uri.EscapeDataString(voice)}";

        var payload = JsonSerializer.Serialize(new
        {
            text = request.Text,
            model_id = model,
            voice_settings = new { stability = 0.5, similarity_boost = 0.75, style = 0.0, use_speaker_boost = true },
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("xi-api-key", _options.ElevenLabsApiKey);
        req.Headers.Add("Accept", "audio/mpeg");
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("ElevenLabs TTS returned {Status}: {Err}", (int)response.StatusCode, err);
            throw new ConversationTtsException("elevenlabs_tts_error", $"ElevenLabs TTS returned {(int)response.StatusCode}");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);

        return new ConversationTtsResult(
            Audio: bytes,
            MimeType: "audio/mpeg",
            DurationMs: AzureConversationTtsProvider.ApproxDurationMs(request.Text),
            ProviderName: Name,
            ProviderResponseSummary: $"elevenlabs voice={voice}, {bytes.Length} bytes");
    }
}
