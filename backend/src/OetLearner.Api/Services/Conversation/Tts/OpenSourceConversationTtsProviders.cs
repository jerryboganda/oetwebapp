using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Conversation.Tts;

/// <summary>
/// ChatTTS (2Noise open-source conversational TTS) provider. Self-hosted.
/// See https://github.com/2noise/ChatTTS. Admin configures BaseUrl (e.g.
/// http://localhost:9966) and optional ApiKey.
/// </summary>
public sealed class ChatTtsConversationTtsProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<ConversationOptions> options,
    ILogger<ChatTtsConversationTtsProvider> logger) : IConversationTtsProvider
{
    private readonly ConversationOptions _options = options.Value;
    public string Name => "chattts";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ChatTtsBaseUrl);

    public async Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("ChatTTS is not configured.");

        var client = httpClientFactory.CreateClient("ConversationChatTtsClient");
        if (!string.IsNullOrWhiteSpace(_options.ChatTtsApiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ChatTtsApiKey);

        var url = $"{_options.ChatTtsBaseUrl.TrimEnd('/')}/tts";
        var payload = JsonSerializer.Serialize(new
        {
            text = request.Text,
            voice = string.IsNullOrWhiteSpace(request.Voice) ? _options.ChatTtsDefaultVoice : request.Voice,
            language = request.Locale,
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
            logger.LogWarning("ChatTTS returned {Status}: {Err}", (int)response.StatusCode, err);
            throw new ConversationTtsException("chattts_error", $"ChatTTS returned {(int)response.StatusCode}");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var mime = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";
        return new ConversationTtsResult(
            Audio: bytes, MimeType: mime,
            DurationMs: AzureConversationTtsProvider.ApproxDurationMs(request.Text),
            ProviderName: Name,
            ProviderResponseSummary: $"chattts {bytes.Length} bytes");
    }
}

/// <summary>
/// GPT-SoVITS open-source voice-cloning TTS provider. Self-hosted. See
/// https://github.com/RVC-Boss/GPT-SoVITS. Admin configures BaseUrl.
/// </summary>
public sealed class GptSoVitsConversationTtsProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<ConversationOptions> options,
    ILogger<GptSoVitsConversationTtsProvider> logger) : IConversationTtsProvider
{
    private readonly ConversationOptions _options = options.Value;
    public string Name => "gptsovits";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.GptSoVitsBaseUrl);

    public async Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("GPT-SoVITS is not configured.");

        var client = httpClientFactory.CreateClient("ConversationGptSoVitsClient");
        if (!string.IsNullOrWhiteSpace(_options.GptSoVitsApiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.GptSoVitsApiKey);

        var url = $"{_options.GptSoVitsBaseUrl.TrimEnd('/')}/tts";
        var payload = JsonSerializer.Serialize(new
        {
            text = request.Text,
            voice = string.IsNullOrWhiteSpace(request.Voice) ? _options.GptSoVitsDefaultVoice : request.Voice,
            language = request.Locale,
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
            logger.LogWarning("GPT-SoVITS returned {Status}: {Err}", (int)response.StatusCode, err);
            throw new ConversationTtsException("gptsovits_error", $"GPT-SoVITS returned {(int)response.StatusCode}");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var mime = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";
        return new ConversationTtsResult(
            Audio: bytes, MimeType: mime,
            DurationMs: AzureConversationTtsProvider.ApproxDurationMs(request.Text),
            ProviderName: Name,
            ProviderResponseSummary: $"gptsovits {bytes.Length} bytes");
    }
}
