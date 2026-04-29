using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Pronunciation;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

/// <summary>
/// Regression: <see cref="WhisperPronunciationAsrProvider"/> previously sent
/// <c>UserId = null</c> through the AI gateway, causing the AI quota /
/// usage recorder to attribute Whisper-driven scoring as anonymous /
/// unmetered. The fix threads the learner id from the attempt through
/// <see cref="AsrRequest.UserId"/> into the gateway request so usage
/// recording (and any future BYOK / quota policy) is correctly attributed.
/// </summary>
public sealed class WhisperPronunciationUserIdTests
{
    [Fact]
    public async Task AsrRequest_PreservesUserId()
    {
        // Structural guard: AsrRequest must carry a UserId so callers can
        // pass the learner id through the ASR pipeline.
        var req = new AsrRequest(
            Audio: new MemoryStream(),
            AudioMimeType: "audio/webm",
            ReferenceText: "x",
            TargetPhoneme: "θ",
            Locale: "en-GB",
            TargetRuleId: "P01.1",
            RulebookProfession: "medicine",
            AudioBytes: 0,
            UserId: "learner-42");
        Assert.Equal("learner-42", req.UserId);
    }

    [Fact]
    public async Task Whisper_GroundedAiCall_PropagatesLearnerId_AndRecordsUsage()
    {
        // ── Arrange a Whisper provider whose HTTP transport returns a canned
        //    transcript, plus a real AiGatewayService with a recorder.
        var whisperResponse = "{\"text\":\"hello world\",\"duration\":1.5,\"words\":[{\"word\":\"hello\",\"start\":0,\"end\":0.5},{\"word\":\"world\",\"start\":0.6,\"end\":1.4}]}";
        var http = new HttpClient(new StubHandler(whisperResponse));
        var factory = new StubHttpClientFactory(http);
        var pronOpts = Options.Create(new PronunciationOptions
        {
            Provider = "whisper",
            WhisperApiKey = "fake-key",
            WhisperBaseUrl = "https://whisper.example.test",
            WhisperModel = "whisper-1"
        });

        var dbOptions = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new LearnerDbContext(dbOptions);
        var recorder = new OetLearner.Api.Services.Rulebook.AiUsageRecorder(
            db, NullLogger<OetLearner.Api.Services.Rulebook.AiUsageRecorder>.Instance);
        var loader = new RulebookLoader();
        var providers = new IAiModelProvider[] { new MockAiProvider() };
        var gateway = new AiGatewayService(loader, providers, recorder);

        var provider = new WhisperPronunciationAsrProvider(
            factory, pronOpts, gateway,
            NullLogger<WhisperPronunciationAsrProvider>.Instance);

        // ── Act ─────────────────────────────────────────────────────────────
        var request = new AsrRequest(
            Audio: new MemoryStream(Encoding.UTF8.GetBytes("fake-audio-bytes")),
            AudioMimeType: "audio/webm",
            ReferenceText: "hello world",
            TargetPhoneme: "h",
            Locale: "en-GB",
            TargetRuleId: "P01.1",
            RulebookProfession: "medicine",
            AudioBytes: 16,
            UserId: "learner-whisper-7");

        var result = await provider.AnalyzeAsync(request, default);

        // ── Assert ──────────────────────────────────────────────────────────
        Assert.NotNull(result);

        // The mock model provider returns text wrapped in a code-fence,
        // which the Whisper provider can either parse or fall back from.
        // Either way, exactly one usage row is recorded by the gateway and
        // it MUST be attributed to the learner id we passed.
        var usage = await db.AiUsageRecords.SingleAsync();
        Assert.Equal("learner-whisper-7", usage.UserId);
        Assert.Equal(AiFeatureCodes.PronunciationScore, usage.FeatureCode);
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(resp);
        }
    }
}
