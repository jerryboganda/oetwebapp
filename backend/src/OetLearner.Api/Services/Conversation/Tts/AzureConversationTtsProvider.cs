using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Conversation.Tts;

/// <summary>
/// Azure Speech neural TTS via the REST "synthesizeToStream" endpoint. Returns
/// MP3 audio. Default voice controlled by <c>Conversation:AzureTtsDefaultVoice</c>.
/// </summary>
public sealed class AzureConversationTtsProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<ConversationOptions> options,
    ILogger<AzureConversationTtsProvider> logger) : IConversationTtsProvider
{
    private readonly ConversationOptions _options = options.Value;

    public string Name => "azure";
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.AzureSpeechKey) &&
        !string.IsNullOrWhiteSpace(_options.AzureSpeechRegion);

    public async Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Azure Speech TTS is not configured.");

        var client = httpClientFactory.CreateClient("ConversationAzureTtsClient");
        var url = $"https://{_options.AzureSpeechRegion}.tts.speech.microsoft.com/cognitiveservices/v1";
        var voice = string.IsNullOrWhiteSpace(request.Voice) ? _options.AzureTtsDefaultVoice : request.Voice;
        var locale = request.Locale ?? "en-GB";

        var ssml = new StringBuilder();
        ssml.Append($"<speak version='1.0' xml:lang='{locale}'>");
        ssml.Append($"<voice xml:lang='{locale}' name='{EscapeXml(voice)}'>");
        var prosodyRate = request.Rate.HasValue ? $"{(int)Math.Round((request.Rate.Value - 1.0) * 100)}%" : "0%";
        var prosodyPitch = request.Pitch.HasValue ? $"{request.Pitch.Value:+0.0;-0.0;0}st" : "0st";
        ssml.Append($"<prosody rate='{prosodyRate}' pitch='{prosodyPitch}'>");
        ssml.Append(EscapeXml(request.Text));
        ssml.Append("</prosody></voice></speak>");

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("Ocp-Apim-Subscription-Key", _options.AzureSpeechKey);
        req.Headers.Add("X-Microsoft-OutputFormat", "audio-24khz-96kbitrate-mono-mp3");
        req.Headers.Add("User-Agent", "OetLearner-Conversation");
        req.Content = new StringContent(ssml.ToString(), Encoding.UTF8, "application/ssml+xml");

        using var response = await client.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Azure TTS returned {Status}: {Err}", (int)response.StatusCode, err);
            throw new ConversationTtsException("azure_tts_error", $"Azure TTS returned {(int)response.StatusCode}");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);

        return new ConversationTtsResult(
            Audio: bytes,
            MimeType: "audio/mpeg",
            DurationMs: ApproxDurationMs(request.Text),
            ProviderName: Name,
            ProviderResponseSummary: $"azure voice={voice}, {bytes.Length} bytes");
    }

    private static string EscapeXml(string s) => System.Security.SecurityElement.Escape(s) ?? "";

    internal static int ApproxDurationMs(string text)
        => Math.Max(300, text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length * 350);
}
