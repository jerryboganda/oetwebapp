using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// The owner's listening check for the four OpenAI live-patient voices (owner decision 2026-10-05): a short admin-only GPT-Live
/// preview per voice. The key stays on the server, only the four configured voices can be asked for, a refused session shows
/// the status and never the provider's message, and a preview never touches the learners' provider circuit breaker.
/// </summary>
public sealed class LiveVoicePreviewServiceTests : IDisposable
{
    private const string ApiKey = "sk-test-preview-key-0000";

    private readonly LearnerDbContext _db = new(new DbContextOptionsBuilder<LearnerDbContext>()
        .UseInMemoryDatabase($"live-voice-preview-{Guid.NewGuid():N}")
        .Options);

    public void Dispose() => _db.Dispose();

    private static LiveVoiceOptions Options() => new()
    {
        OpenAiApiKey = ApiKey,
        GeminiApiKey = "gemini-test-key",
    };

    private LiveVoicePreviewService Service(StubHandler handler, LiveVoiceOptions? options = null, LiveVoiceProviderProbeState? probe = null)
        => new(new StubFactory(handler), Microsoft.Extensions.Options.Options.Create(options ?? Options()),
            probe ?? new LiveVoiceProviderProbeState(), _db, TimeProvider.System);

    [Fact]
    public void ListsTheFourConfiguredVoices_OpenAiFirst_WithGeminiAsTheFallback()
    {
        var list = Service(new StubHandler(HttpStatusCode.OK, "{}")).List();

        Assert.Equal("openai", list.PrimaryProvider);
        Assert.Equal(new[] { "female-younger", "female-older", "male-younger", "male-older" }, list.Cells.Select(c => c.Key).ToArray());
        Assert.Equal(new[] { "quartz", "willow", "ripple", "vesper" }, list.Cells.Select(c => c.OpenAiVoice).ToArray());
        Assert.Equal(new[] { "Leda", "Kore", "Orus", "Charon" }, list.Cells.Select(c => c.GeminiVoice).ToArray());
        Assert.Equal(new[] { "Australian", "Irish", "Australian", "British" }, list.Cells.Select(c => c.AccentNote).ToArray());
        Assert.DoesNotContain(ApiKey, JsonSerializer.Serialize(list));
    }

    [Fact]
    public void TheDefaultPrimaryProvider_IsOpenAi_AndTheOpenAiPoolIsQuartzWillowRippleVesper()
    {
        var defaults = new LiveVoiceOptions();

        Assert.Equal("openai", defaults.PrimaryProvider);
        Assert.Equal(new[] { "openai", "gemini" }, defaults.ProviderOrder().ToArray());
        Assert.Equal(
            new[] { "quartz", "willow", "ripple", "vesper" },
            new[] { defaults.OpenAiVoiceFemaleYounger, defaults.OpenAiVoiceFemaleOlder, defaults.OpenAiVoiceMaleYounger, defaults.OpenAiVoiceMaleOlder });
    }

    [Fact]
    public async Task APreview_RelaysTheOffer_WithTheCellsVoice_AndTheServerSideKey_AndIsAudited()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            "{\"session\":{\"id\":\"sess_1\"},\"transport\":{\"sdp\":\"v=0 answer\"}}");

        var answer = await Service(handler).CreateOfferAsync(
            "admin-1", "Dr Hesham", new LiveVoicePreviewOfferRequest("male-older", "v=0 offer"), CancellationToken.None);

        Assert.Equal("vesper", answer.Voice);
        Assert.Equal("sess_1", answer.ProviderSessionId);
        Assert.Equal("v=0 answer", answer.AnswerSdp);
        Assert.Equal(LiveVoicePreviewService.MaxSeconds, answer.MaxSeconds);
        Assert.DoesNotContain(ApiKey, JsonSerializer.Serialize(answer));

        Assert.Equal($"Bearer {ApiKey}", handler.Authorization);
        using var sent = JsonDocument.Parse(handler.Body!);
        Assert.Equal("vesper", sent.RootElement.GetProperty("session").GetProperty("audio").GetProperty("output").GetProperty("voice").GetString());
        Assert.Equal("gpt-live-1", sent.RootElement.GetProperty("session").GetProperty("model").GetString());
        Assert.Equal("webrtc", sent.RootElement.GetProperty("transport").GetProperty("type").GetString());
        Assert.Equal("v=0 offer", sent.RootElement.GetProperty("transport").GetProperty("sdp").GetString());
        Assert.Contains(LiveVoicePreviewService.SampleText, sent.RootElement.GetProperty("session").GetProperty("instructions").GetString());

        var audit = await _db.AuditEvents.AsNoTracking().SingleAsync();
        Assert.Equal("LiveVoicePreviewStarted", audit.Action);
        Assert.Equal("admin-1", audit.ActorId);
        Assert.DoesNotContain(ApiKey, audit.Details ?? string.Empty);
    }

    [Theory]
    [InlineData("alloy")]
    [InlineData("")]
    [InlineData(null)]
    public async Task OnlyTheFourConfiguredVoicesCanBeAskedFor(string? cell)
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");

        var error = await Assert.ThrowsAsync<ApiException>(() => Service(handler).CreateOfferAsync(
            "admin-1", "Dr Hesham", new LiveVoicePreviewOfferRequest(cell, "v=0 offer"), CancellationToken.None));

        Assert.Equal("live_voice_preview_cell_invalid", error.ErrorCode);
        Assert.Null(handler.Body); // nothing was sent to the provider
    }

    [Fact]
    public async Task ARefusedSession_ShowsTheStatusOnly_NeverTheProvidersMessage_AndLeavesTheCircuitBreakerAlone()
    {
        var probe = new LiveVoiceProviderProbeState();
        var handler = new StubHandler(HttpStatusCode.TooManyRequests, "{\"error\":{\"message\":\"secret provider detail sk-leak\"}}");

        var error = await Assert.ThrowsAsync<ApiException>(() => Service(handler, probe: probe).CreateOfferAsync(
            "admin-1", "Dr Hesham", new LiveVoicePreviewOfferRequest("female-younger", "v=0 offer"), CancellationToken.None));

        Assert.Equal("live_voice_preview_failed", error.ErrorCode);
        Assert.Contains("429", error.Message);
        Assert.DoesNotContain("secret provider detail", error.Message);
        Assert.DoesNotContain("sk-leak", error.Message);
        Assert.Equal(0, probe.Snapshot(Options()).Providers.Single(p => p.Provider == "openai").Failures);
        Assert.Empty(await _db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task WithoutAnOpenAiKey_ThePreviewSaysSoPlainly()
    {
        var error = await Assert.ThrowsAsync<ApiException>(() => Service(new StubHandler(HttpStatusCode.OK, "{}"), new LiveVoiceOptions()).CreateOfferAsync(
            "admin-1", "Dr Hesham", new LiveVoicePreviewOfferRequest("female-younger", "v=0 offer"), CancellationToken.None));

        Assert.Equal("live_voice_provider_not_configured", error.ErrorCode);
    }

    private sealed class StubHandler(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Authorization = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
