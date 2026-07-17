using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Endpoints;
using OetWithDrHesham.Api.Services.Conversation;
using OetWithDrHesham.Api.Services.Conversation.Tts;
using OetWithDrHesham.Api.Services.Listening;
using Xunit;

namespace OetWithDrHesham.Api.Tests;

/// <summary>
/// Covers the ElevenLabs-only TTS path: the pronunciation-dictionary locator
/// (regression — an explicit null version_id breaks every dictionary-backed
/// generation), the conversation TTS request shape, the listening synthesis
/// provider (raw pcm_16000), and the Voice Design voice/error helpers.
/// </summary>
public sealed class ElevenLabsTtsTests
{
    // ─── Locator (pure) ───────────────────────────────────────────────────

    [Fact]
    public void Locator_Null_WhenNoDictionaryId()
    {
        Assert.Null(ElevenLabsPronunciationLocator.Build(null, "v1"));
        Assert.Null(ElevenLabsPronunciationLocator.Build("   ", "v1"));
    }

    [Fact]
    public void Locator_OmitsVersionId_WhenVersionMissing()
    {
        var locator = ElevenLabsPronunciationLocator.Build("dict-1", null);
        Assert.NotNull(locator);
        Assert.Equal("dict-1", locator!["pronunciation_dictionary_id"]);
        Assert.False(locator.ContainsKey("version_id"));
    }

    [Fact]
    public void Locator_IncludesVersionId_WhenPresent()
    {
        var locator = ElevenLabsPronunciationLocator.Build("dict-1", "ver-9");
        Assert.NotNull(locator);
        Assert.Equal("ver-9", locator!["version_id"]);
    }

    // ─── Conversation TTS provider ────────────────────────────────────────

    [Fact]
    public async Task ConversationTts_OmitsVersionId_WhenNull()
    {
        var (provider, captured) = BuildConversationProvider(new ConversationOptions
        {
            ElevenLabsApiKey = "key",
            ElevenLabsDefaultVoiceId = "default-voice",
            ElevenLabsPronunciationDictionaryId = "dict-1",
            ElevenLabsPronunciationDictionaryVersionId = null,
        });

        await provider.SynthesizeAsync(new ConversationTtsRequest("hello", "", "en-GB"), CancellationToken.None);

        using var doc = JsonDocument.Parse(captured.Body!);
        var locator = doc.RootElement.GetProperty("pronunciation_dictionary_locators")[0];
        Assert.Equal("dict-1", locator.GetProperty("pronunciation_dictionary_id").GetString());
        Assert.False(locator.TryGetProperty("version_id", out _));
    }

    [Fact]
    public async Task ConversationTts_IncludesVersionId_WhenPresent()
    {
        var (provider, captured) = BuildConversationProvider(new ConversationOptions
        {
            ElevenLabsApiKey = "key",
            ElevenLabsPronunciationDictionaryId = "dict-1",
            ElevenLabsPronunciationDictionaryVersionId = "ver-9",
        });

        await provider.SynthesizeAsync(new ConversationTtsRequest("hi", "v", "en-GB"), CancellationToken.None);

        using var doc = JsonDocument.Parse(captured.Body!);
        var locator = doc.RootElement.GetProperty("pronunciation_dictionary_locators")[0];
        Assert.Equal("ver-9", locator.GetProperty("version_id").GetString());
    }

    [Fact]
    public async Task ConversationTts_NoDictionary_OmitsLocators()
    {
        var (provider, captured) = BuildConversationProvider(new ConversationOptions
        {
            ElevenLabsApiKey = "key",
            ElevenLabsDefaultVoiceId = "default-voice",
        });

        await provider.SynthesizeAsync(new ConversationTtsRequest("hi", "v", "en-GB"), CancellationToken.None);

        using var doc = JsonDocument.Parse(captured.Body!);
        Assert.False(doc.RootElement.TryGetProperty("pronunciation_dictionary_locators", out _));
    }

    [Fact]
    public async Task ConversationTts_UsesRequestVoice_OverDefault()
    {
        var (provider, captured) = BuildConversationProvider(new ConversationOptions
        {
            ElevenLabsApiKey = "key",
            ElevenLabsDefaultVoiceId = "default-voice",
        });

        await provider.SynthesizeAsync(new ConversationTtsRequest("hi", "chosen-voice", "en-GB"), CancellationToken.None);

        Assert.Contains("/text-to-speech/chosen-voice", captured.Url);
    }

    [Fact]
    public async Task ConversationTts_NonSuccess_Throws()
    {
        var (provider, _) = BuildConversationProvider(
            new ConversationOptions { ElevenLabsApiKey = "key", ElevenLabsDefaultVoiceId = "v" },
            statusCode: HttpStatusCode.UnprocessableEntity,
            responseBody: "{\"detail\":\"bad voice\"}");

        await Assert.ThrowsAsync<ConversationTtsException>(() =>
            provider.SynthesizeAsync(new ConversationTtsRequest("hi", "v", "en-GB"), CancellationToken.None));
    }

    // ─── Listening synthesis provider ─────────────────────────────────────

    [Fact]
    public async Task Listening_RequestsPcm16000_AndReturnsRawBytes()
    {
        var pcm = new byte[] { 1, 2, 3, 4, 5, 6 };
        var (provider, captured) = BuildListeningProvider(
            new ConversationOptions { ElevenLabsApiKey = "key", ElevenLabsDefaultVoiceId = "lv" },
            responseBytes: pcm);

        Assert.Equal(16_000, provider.SampleRateHz);

        var bytes = await provider.SynthesizeAsync("good morning", null, CancellationToken.None);

        Assert.Equal(pcm, bytes);
        Assert.Contains("output_format=pcm_16000", captured.Url);
        Assert.Contains("/text-to-speech/lv", captured.Url);
        Assert.Contains("audio/pcm", captured.AcceptHeader);
    }

    [Fact]
    public async Task Listening_EmptyText_ReturnsEmpty_NoHttpCall()
    {
        var (provider, captured) = BuildListeningProvider(
            new ConversationOptions { ElevenLabsApiKey = "key", ElevenLabsDefaultVoiceId = "lv" },
            responseBytes: new byte[] { 9 });

        var bytes = await provider.SynthesizeAsync("   ", null, CancellationToken.None);

        Assert.Empty(bytes);
        Assert.Null(captured.Url); // handler never invoked
    }

    [Fact]
    public async Task Listening_AppliesDictionary_OmittingNullVersion()
    {
        var (provider, captured) = BuildListeningProvider(
            new ConversationOptions
            {
                ElevenLabsApiKey = "key",
                ElevenLabsDefaultVoiceId = "lv",
                ElevenLabsPronunciationDictionaryId = "dict-7",
            },
            responseBytes: new byte[] { 1 });

        await provider.SynthesizeAsync("dyspnoea", null, CancellationToken.None);

        using var doc = JsonDocument.Parse(captured.Body!);
        var locator = doc.RootElement.GetProperty("pronunciation_dictionary_locators")[0];
        Assert.Equal("dict-7", locator.GetProperty("pronunciation_dictionary_id").GetString());
        Assert.False(locator.TryGetProperty("version_id", out _));
    }

    // ─── Voice / error helpers (Voice Design endpoints) ───────────────────

    [Fact]
    public void ParseElevenLabsVoices_MapsCatalogue()
    {
        const string json = """
            { "voices": [
              { "voice_id": "v1", "name": "Aria", "category": "premade",
                "preview_url": "https://x/p.mp3", "labels": { "accent": "british" } },
              { "name": "missing-id-skipped" }
            ] }
            """;
        var voices = VoiceDesignAdminEndpoints.ParseElevenLabsVoices(json);
        var v = Assert.Single(voices);
        Assert.Equal("v1", v.VoiceId);
        Assert.Equal("Aria", v.Name);
        Assert.Equal("premade", v.Category);
        Assert.Equal("british", v.Labels!["accent"]);
    }

    [Fact]
    public void ParseElevenLabsVoices_MalformedJson_ReturnsEmpty()
        => Assert.Empty(VoiceDesignAdminEndpoints.ParseElevenLabsVoices("not json"));

    [Fact]
    public void Truncate_CapsLength()
    {
        Assert.Equal("abc", VoiceDesignAdminEndpoints.Truncate("abcdef", 3));
        Assert.Equal("abc", VoiceDesignAdminEndpoints.Truncate("abc", 10));
        Assert.Equal(string.Empty, VoiceDesignAdminEndpoints.Truncate(null, 10));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static (ElevenLabsConversationTtsProvider, Captured) BuildConversationProvider(
        ConversationOptions options,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? responseBody = null)
    {
        var captured = new Captured();
        var handler = new StubHandler(async req =>
        {
            captured.Method = req.Method;
            captured.Url = req.RequestUri!.ToString();
            captured.Body = req.Content is null ? null : await req.Content.ReadAsStringAsync();
            return new HttpResponseMessage(statusCode)
            {
                Content = responseBody is null
                    ? new ByteArrayContent(new byte[] { 1, 2, 3 })
                    : new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        });
        var provider = new ElevenLabsConversationTtsProvider(
            new SingleClientFactory(new HttpClient(handler)),
            new StubOptionsProvider(options),
            NullLogger<ElevenLabsConversationTtsProvider>.Instance);
        return (provider, captured);
    }

    private static (ElevenLabsListeningTtsSynthesisProvider, Captured) BuildListeningProvider(
        ConversationOptions options,
        byte[] responseBytes)
    {
        var captured = new Captured();
        var handler = new StubHandler(async req =>
        {
            captured.Method = req.Method;
            captured.Url = req.RequestUri!.ToString();
            captured.AcceptHeader = string.Join(",", req.Headers.Accept.Select(a => a.ToString()));
            captured.Body = req.Content is null ? null : await req.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(responseBytes) };
        });
        var provider = new ElevenLabsListeningTtsSynthesisProvider(
            new SingleClientFactory(new HttpClient(handler)),
            new StubOptionsProvider(options),
            NullLogger<ElevenLabsListeningTtsSynthesisProvider>.Instance);
        return (provider, captured);
    }

    private sealed class Captured
    {
        public HttpMethod? Method;
        public string? Url;
        public string? Body;
        public string AcceptHeader = string.Empty;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubOptionsProvider(ConversationOptions opts) : IConversationOptionsProvider
    {
        public Task<ConversationOptions> GetAsync(CancellationToken ct = default) => Task.FromResult(opts);
        public void Invalidate() { }
    }
}
