using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Conversation.Tts;

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
        if (!IsConfigured) throw new InvalidOperationException("CosyVoice not configured.");

        var client = httpClientFactory.CreateClient("ConversationCosyVoiceClient");
        if (!string.IsNullOrWhiteSpace(_options.CosyVoiceApiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.CosyVoiceApiKey);

        var url = $"{_options.CosyVoiceBaseUrl.TrimEnd('/')}/tts";
        var payload = JsonSerializer.Serialize(new
        {
            text = request.Text,
            voice = string.IsNullOrWhiteSpace(request.Voice) ? _options.CosyVoiceDefaultVoice : request.Voice,
            language = request.Locale, rate = request.Rate ?? 1.0, pitch = request.Pitch ?? 0.0, format = "mp3",
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("CosyVoice {Status}: {Err}", (int)response.StatusCode, err);
            throw new ConversationTtsException("cosyvoice_error", $"CosyVoice {(int)response.StatusCode}");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var mime = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";
        return new ConversationTtsResult(bytes, mime,
            AzureConversationTtsProvider.ApproxDurationMs(request.Text), Name, "cosyvoice");
    }
}

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
        if (!IsConfigured) throw new InvalidOperationException("ChatTTS not configured.");

        var client = httpClientFactory.CreateClient("ConversationChatTtsClient");
        if (!string.IsNullOrWhiteSpace(_options.ChatTtsApiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ChatTtsApiKey);

        var url = $"{_options.ChatTtsBaseUrl.TrimEnd('/')}/tts";
        var payload = JsonSerializer.Serialize(new
        {
            text = request.Text,
            voice = string.IsNullOrWhiteSpace(request.Voice) ? _options.ChatTtsDefaultVoice : request.Voice,
            language = request.Locale, format = "mp3",
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("ChatTTS {Status}: {Err}", (int)response.StatusCode, err);
            throw new ConversationTtsException("chattts_error", $"ChatTTS {(int)response.StatusCode}");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var mime = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";
        return new ConversationTtsResult(bytes, mime,
            AzureConversationTtsProvider.ApproxDurationMs(request.Text), Name, "chattts");
    }
}

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
        if (!IsConfigured) throw new InvalidOperationException("GPT-SoVITS not configured.");

        var client = httpClientFactory.CreateClient("ConversationGptSoVitsClient");
        if (!string.IsNullOrWhiteSpace(_options.GptSoVitsApiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.GptSoVitsApiKey);

        var url = $"{_options.GptSoVitsBaseUrl.TrimEnd('/')}/tts";
        var payload = JsonSerializer.Serialize(new
        {
            text = request.Text,
            voice = string.IsNullOrWhiteSpace(request.Voice) ? _options.GptSoVitsDefaultVoice : request.Voice,
            language = request.Locale, format = "mp3",
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("GPT-SoVITS {Status}: {Err}", (int)response.StatusCode, err);
            throw new ConversationTtsException("gptsovits_error", $"GPT-SoVITS {(int)response.StatusCode}");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var mime = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";
        return new ConversationTtsResult(bytes, mime,
            AzureConversationTtsProvider.ApproxDurationMs(request.Text), Name, "gptsovits");
    }
}
