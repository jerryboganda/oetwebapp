using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Pronunciation;
using OetWithDrHesham.Api.Services.Rulebook;

namespace OetWithDrHesham.Api.Tests;

public sealed class GeminiNativeProviderTests
{
    [Fact]
    public async Task PronunciationAsrProvider_AllowsCleanResponseWithoutProblematicPhonemes()
    {
        var gateway = new StubAiGatewayService("""
            {
              "accuracyScore":92,
              "fluencyScore":88,
              "completenessScore":94,
              "prosodyScore":90,
              "overallScore":91,
              "wordScores":[{"word":"test","accuracyScore":92,"errorType":"None"}],
              "problematicPhonemes":[],
              "fluencyMarkers":{"speechRateWpm":138,"pauseCount":1,"averagePauseDurationMs":260}
            }
            """);
        var provider = new GeminiPronunciationAsrProvider(
            Options.Create(new PronunciationOptions
            {
                GeminiApiKey = "gemini-secret-key",
                GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta",
                GeminiModel = "gemini-3.5-flash",
            }),
            gateway,
            new StubPronunciationCredentialResolver(),
            NullLogger<GeminiPronunciationAsrProvider>.Instance);

        await using var audio = new MemoryStream(new byte[] { 1, 2, 3 });
        var result = await provider.AnalyzeAsync(new AsrRequest(
            UserId: "learner-1",
            Audio: audio,
            AudioMimeType: "audio/webm",
            ReferenceText: "test",
            TargetPhoneme: "t",
            Locale: "en-GB",
            TargetRuleId: "P01.1",
            RulebookProfession: "medicine",
            AudioBytes: 3), CancellationToken.None);

        Assert.Equal(91, result.OverallScore);
        var phoneme = Assert.Single(result.ProblematicPhonemes);
        Assert.Equal("t", phoneme.Phoneme);
        Assert.Equal("P01.1", phoneme.RuleId);
        Assert.Equal("gemini", result.ProviderName);
        Assert.NotNull(gateway.LastRequest);
        Assert.Equal("gemini-pronunciation-audio", gateway.LastRequest!.Provider);
        Assert.NotNull(gateway.LastRequest.AudioAttachments);
        Assert.Equal(new byte[] { 1, 2, 3 }, gateway.LastRequest.AudioAttachments![0].Data);
    }

    [Fact]
    public async Task PronunciationAsrProvider_MapsGatewayConfigurationFailureToUnavailable()
    {
        var provider = new GeminiPronunciationAsrProvider(
            Options.Create(new PronunciationOptions
            {
                GeminiApiKey = "gemini-secret-key",
                GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta",
                GeminiModel = "gemini-3.5-flash",
            }),
            new StubAiGatewayService("{}") { ExceptionToThrow = new InvalidOperationException("missing provider row") },
            new StubPronunciationCredentialResolver(),
            NullLogger<GeminiPronunciationAsrProvider>.Instance);

        await using var audio = new MemoryStream(new byte[] { 1, 2, 3 });
        var ex = await Assert.ThrowsAsync<PronunciationAsrUnavailableException>(() => provider.AnalyzeAsync(new AsrRequest(
            UserId: "learner-1",
            Audio: audio,
            AudioMimeType: "audio/webm",
            ReferenceText: "test",
            TargetPhoneme: "t",
            Locale: "en-GB",
            TargetRuleId: "P01.1",
            RulebookProfession: "medicine",
            AudioBytes: 3), CancellationToken.None));

        Assert.Equal("gemini_unavailable", ex.Code);
    }

    [Fact]
    public async Task CompleteAsync_SendsInlineAudioAndParsesTextAndUsage()
    {
        string? capturedUrl = null;
        string? capturedApiKeyHeader = null;
        string? capturedBody = null;
        var handler = new StubHandler(async request =>
        {
            capturedUrl = request.RequestUri!.ToString();
            capturedApiKeyHeader = request.Headers.TryGetValues("x-goog-api-key", out var headerValues)
                ? headerValues.SingleOrDefault()
                : null;
            capturedBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
            var responseJson = """
                {
                  "candidates": [
                    {
                      "content": { "parts": [ { "text": "{\"overallScore\":82}" } ] },
                      "finishReason": "STOP"
                    }
                  ],
                  "usageMetadata": { "promptTokenCount": 31, "candidatesTokenCount": 17 }
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        });

        await using var db = await CreateDbAsync();
        var dp = new EphemeralDataProtectionProvider();
        var protector = dp.CreateProtector("AiProvider.PlatformKey.v1");
        db.AiProviders.Add(new AiProvider
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = "gemini-pronunciation-audio",
            Name = "Gemini Native Audio",
            Category = AiProviderCategory.Phoneme,
            Dialect = AiProviderDialect.GeminiNative,
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
            EncryptedApiKey = protector.Protect("gemini-secret-key"),
            ApiKeyHint = "-key",
            DefaultModel = "gemini-3.5-flash",
            AllowedModelsCsv = string.Empty,
            IsActive = true,
            FailoverPriority = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var registry = new AiProviderRegistry(db, dp);
        var provider = new GeminiNativeProvider(
            new StubHttpClientFactory(new HttpClient(handler)),
            registry,
            Options.Create(new PronunciationOptions()));

        var completion = await provider.CompleteAsync(new AiProviderRequest
        {
            ProviderCode = "gemini-pronunciation-audio",
            Model = "gemini-3.5-flash",
            SystemPrompt = "system prompt",
            UserPrompt = "user prompt",
            Temperature = 0.1,
            MaxTokens = 512,
            AudioAttachments = new[]
            {
                new AiProviderAudioAttachment
                {
                    MimeType = "audio/webm",
                    Data = new byte[] { 1, 2, 3, 4 },
                },
            },
        }, CancellationToken.None);

        Assert.Equal("{\"overallScore\":82}", completion.Text);
        Assert.NotNull(completion.Usage);
        Assert.Equal(31, completion.Usage!.PromptTokens);
        Assert.Equal(17, completion.Usage.CompletionTokens);
        Assert.Contains("/models/gemini-3.5-flash:generateContent", capturedUrl);
        Assert.DoesNotContain("key=", capturedUrl);
        Assert.Equal("gemini-secret-key", capturedApiKeyHeader);

        using var doc = JsonDocument.Parse(capturedBody!);
        var contents = doc.RootElement.GetProperty("contents");
        var parts = contents[0].GetProperty("parts");
        Assert.Contains("system prompt", parts[0].GetProperty("text").GetString());
        Assert.Contains("user prompt", parts[0].GetProperty("text").GetString());
        var inline = parts[1].GetProperty("inline_data");
        Assert.Equal("audio/webm", inline.GetProperty("mime_type").GetString());
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }), inline.GetProperty("data").GetString());
    }

    [Fact]
    public async Task CompleteAsync_WithoutAudio_Throws()
    {
        await using var db = await CreateDbAsync();
        var dp = new EphemeralDataProtectionProvider();
        var provider = new GeminiNativeProvider(
            new StubHttpClientFactory(new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))),
            new AiProviderRegistry(db, dp),
            Options.Create(new PronunciationOptions { GeminiApiKey = "gemini-secret-key" }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new AiProviderRequest
        {
            Model = "gemini-3.5-flash",
            SystemPrompt = "system",
            UserPrompt = "user",
        }, CancellationToken.None));
        Assert.Contains("audio attachment", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteAsync_WithEmptyAudio_Throws()
    {
        await using var db = await CreateDbAsync();
        var dp = new EphemeralDataProtectionProvider();
        var provider = new GeminiNativeProvider(
            new StubHttpClientFactory(new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))),
            new AiProviderRegistry(db, dp),
            Options.Create(new PronunciationOptions { GeminiApiKey = "gemini-secret-key" }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new AiProviderRequest
        {
            Model = "gemini-3.5-flash",
            SystemPrompt = "system",
            UserPrompt = "user",
            AudioAttachments = new[]
            {
                new AiProviderAudioAttachment
                {
                    MimeType = "audio/webm",
                    Data = Array.Empty<byte>(),
                },
            },
        }, CancellationToken.None));
        Assert.Contains("non-empty audio attachment", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<LearnerDbContext> CreateDbAsync()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"gemini-native-{Guid.NewGuid():N}")
            .Options;
        var db = new LearnerDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => await responder(request);
    }

    private sealed class StubAiGatewayService(string completion) : IAiGatewayService
    {
        public AiGatewayRequest? LastRequest { get; private set; }
        public Exception? ExceptionToThrow { get; init; }

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            return Task.FromResult(new AiGatewayResult
            {
                Completion = completion,
                Metadata = new AiGroundedPromptMetadata
                {
                    RulebookKind = RuleKind.Pronunciation,
                    Profession = ExamProfession.Medicine,
                    RulebookVersion = "test",
                },
                RulebookVersion = "test",
            });
        }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context) => new()
        {
            SystemPrompt = "OET AI — Rulebook-Grounded System Prompt",
            TaskInstruction = "Score pronunciation.",
            Metadata = new AiGroundedPromptMetadata
            {
                RulebookKind = RuleKind.Pronunciation,
                Profession = ExamProfession.Medicine,
                RulebookVersion = "test",
            },
        };
    }

    private sealed class StubPronunciationCredentialResolver : IPronunciationCredentialResolver
    {
        public Task<PronunciationCredentials?> ResolveAsync(string providerCode, CancellationToken ct) =>
            Task.FromResult<PronunciationCredentials?>(null);

        public bool IsRegistryConfigured(string providerCode) => false;

        public void Invalidate()
        {
        }
    }
}