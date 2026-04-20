using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Conversation.Tts;

/// <summary>
/// CosyVoice (Alibaba open-source, zero-shot voice cloning) TTS provider.
/// Assumes a self-hosted HTTP gateway that accepts JSON and returns audio/wav
/// or audio/mpeg. See https://github.com/FunAudioLLM/CosyVoice. Because this
/// is self-hosted, the admin configures BaseUrl + optional ApiKey.
/// </summary>
public sealed class CosyVoiceConversationTtsProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<ConversationOptions> options,
    ILogger<CosyVoiceConversationTtsProvider> logger) : IConversationTtsProvider
{
    private readonly ConversationOptions _options = options.Value;
    public string Name => "cosyvoice";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.CosyVoiceBaseUrl);

    public async Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("CosyVoice TTS is not configured.");

        var client = httpClientFactory.CreateClient("ConversationCosyVoiceClient");
        if (!string.IsNullOrWhiteSpace(_options.CosyVoiceApiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.CosyVoiceApiKey);

        var url = $"{_options.CosyVoiceBaseUrl.TrimEnd('/')}/tts";
        var payload = JsonSerializer.Serialize(new
        {
            text = request.Text,
            voice = string.IsNullOrWhiteSpace(request.Voice) ? _options.CosyVoiceDefaultVoice : request.Voice,
            language = request.Locale,
            rate = request.Rate ?? 1.0,
            pitch = request.Pitch ?? 0.0,
            format = "mp3",
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        using var response = await client.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("CosyVoice TTS returned {Status}: {Err}", (int)response.StatusCode, err);
            throw new ConversationTtsException("cosyvoice_tts_error", $"CosyVoice returned {(int)response.StatusCode}");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var mime = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";
        return new ConversationTtsResult(
            Audio: bytes, MimeType: mime,
            DurationMs: AzureConversationTtsProvider.ApproxDurationMs(request.Text),
            ProviderName: Name,
            ProviderResponseSummary: $"cosyvoice {bytes.Length} bytes");
    }
}
