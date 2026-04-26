using System.Text;
using Microsoft.Extensions.Logging;

using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Conversation.Tts;

public sealed class AzureConversationTtsProvider(
    IHttpClientFactory httpClientFactory,
    IConversationOptionsProvider optionsProvider,
    ILogger<AzureConversationTtsProvider> logger) : IConversationTtsProvider
{
    private ConversationOptions ReadOptions() => optionsProvider.Snapshot();

    public string Name => "azure";
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ReadOptions().AzureSpeechKey) &&
        !string.IsNullOrWhiteSpace(ReadOptions().AzureSpeechRegion);

    public async Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("Azure TTS not configured.");

        var client = httpClientFactory.CreateClient("ConversationAzureTtsClient");
        var url = $"https://{ReadOptions().AzureSpeechRegion}.tts.speech.microsoft.com/cognitiveservices/v1";
        var voice = string.IsNullOrWhiteSpace(request.Voice) ? ReadOptions().AzureTtsDefaultVoice : request.Voice;
        var locale = request.Locale ?? "en-GB";

        var ssml = new StringBuilder();
        ssml.Append($"<speak version='1.0' xml:lang='{locale}'>");
        ssml.Append($"<voice xml:lang='{locale}' name='{EscapeXml(voice)}'>");
        var rate = request.Rate.HasValue ? $"{(int)Math.Round((request.Rate.Value - 1.0) * 100)}%" : "0%";
        var pitch = request.Pitch.HasValue ? $"{request.Pitch.Value:+0.0;-0.0;0}st" : "0st";
        ssml.Append($"<prosody rate='{rate}' pitch='{pitch}'>");
        ssml.Append(EscapeXml(request.Text));
        ssml.Append("</prosody></voice></speak>");

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("Ocp-Apim-Subscription-Key", ReadOptions().AzureSpeechKey);
        req.Headers.Add("X-Microsoft-OutputFormat", "audio-24khz-96kbitrate-mono-mp3");
        req.Headers.Add("User-Agent", "OetLearner-Conversation");
        req.Content = new StringContent(ssml.ToString(), Encoding.UTF8, "application/ssml+xml");

        using var response = await client.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Azure TTS {Status}: {Err}", (int)response.StatusCode, err);
            throw new ConversationTtsException("azure_tts_error", $"Azure TTS {(int)response.StatusCode}");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return new ConversationTtsResult(bytes, "audio/mpeg",
            ApproxDurationMs(request.Text), Name, $"azure voice={voice}");
    }

    private static string EscapeXml(string s) => System.Security.SecurityElement.Escape(s) ?? "";

    internal static int ApproxDurationMs(string text) =>
        Math.Max(300, text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length * 350);
}
