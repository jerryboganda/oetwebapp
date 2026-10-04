using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// The OpenAI audio-chat leg of the Speaking acoustic judge (owner spec 4 Oct 2026): a clip rides on the
/// first user message as an <c>input_audio</c> part (mp3/wav only), only for an audio chat model, and the
/// audio models take <c>max_completion_tokens</c>. Every other request keeps its old payload byte for byte.
/// </summary>
public sealed class AiProviderPayloadBuilderAudioTests
{
    private static readonly byte[] Clip = [1, 2, 3, 4, 5];

    private static AiProviderRequest Request(
        string model,
        string mimeType = "audio/mpeg",
        byte[]? data = null,
        bool withMessages = true,
        IReadOnlyList<AiChatMessage>? messages = null)
        => new()
        {
            Model = model,
            SystemPrompt = "system text",
            UserPrompt = "user text",
            AudioAttachments = [new AiProviderAudioAttachment { MimeType = mimeType, Data = data ?? Clip }],
            Messages = messages ?? (withMessages
                ? new List<AiChatMessage>
                {
                    new() { Role = "system", Content = "system text" },
                    new() { Role = "user", Content = "user text" },
                }
                : null),
        };

    private static JsonElement[] Messages(AiProviderRequest request)
        => JsonSerializer.SerializeToElement(AiProviderPayloadBuilder.BuildOpenAiMessages(request))
            .EnumerateArray().ToArray();

    [Fact]
    public void AnAudioModel_PutsTheClipOnTheFirstUserMessage_AfterItsText()
    {
        var messages = Messages(Request("gpt-audio-1.5"));

        Assert.Equal(JsonValueKind.String, messages[0].GetProperty("content").ValueKind); // system stays plain text
        var parts = messages[1].GetProperty("content").EnumerateArray().ToArray();
        Assert.Equal(2, parts.Length);
        Assert.Equal("text", parts[0].GetProperty("type").GetString());
        Assert.Equal("user text", parts[0].GetProperty("text").GetString());
        Assert.Equal("input_audio", parts[1].GetProperty("type").GetString());
        var audio = parts[1].GetProperty("input_audio");
        Assert.Equal(Convert.ToBase64String(Clip), audio.GetProperty("data").GetString());
        Assert.Equal("mp3", audio.GetProperty("format").GetString());
    }

    [Fact]
    public void TheTwoMessageShape_CarriesTheClipToo()
    {
        var messages = Messages(Request("gpt-audio", withMessages: false));

        Assert.Equal(2, messages.Length);
        var parts = messages[1].GetProperty("content").EnumerateArray().ToArray();
        Assert.Equal("user text", parts[0].GetProperty("text").GetString());
        Assert.Equal("input_audio", parts[1].GetProperty("type").GetString());
    }

    [Fact]
    public void OnlyTheFirstUserMessage_CarriesTheAudio()
    {
        var messages = Messages(Request("gpt-audio-1.5", messages: new List<AiChatMessage>
        {
            new() { Role = "system", Content = "system text" },
            new() { Role = "user", Content = "first" },
            new() { Role = "assistant", Content = "answer" },
            new() { Role = "user", Content = "second" },
        }));

        Assert.Equal(JsonValueKind.Array, messages[1].GetProperty("content").ValueKind);
        Assert.Equal(JsonValueKind.String, messages[3].GetProperty("content").ValueKind);
        Assert.Equal("second", messages[3].GetProperty("content").GetString());
    }

    [Fact]
    public void AWavClip_IsSentAsWav()
    {
        var messages = Messages(Request("gpt-audio-1.5", mimeType: "audio/x-wav"));

        var audio = messages[1].GetProperty("content").EnumerateArray().Last().GetProperty("input_audio");
        Assert.Equal("wav", audio.GetProperty("format").GetString());
    }

    [Theory]
    [InlineData("audio/webm", 5)]   // a format the API does not accept is left out, never sent
    [InlineData("audio/mpeg", 0)]   // an empty clip is left out
    public void AClipTheApiCannotUse_IsLeftOut_AndTheMessageStaysPlainText(string mimeType, int length)
    {
        var messages = Messages(Request("gpt-audio-1.5", mimeType: mimeType, data: new byte[length]));

        Assert.Equal(JsonValueKind.String, messages[1].GetProperty("content").ValueKind);
        Assert.Equal("user text", messages[1].GetProperty("content").GetString());
    }

    [Theory]
    [InlineData("gpt-4o")]
    [InlineData("claude-sonnet-5")]
    [InlineData("whisper-1")]
    [InlineData("gpt-4o-transcribe")]
    public void AnyOtherModel_KeepsTheOldPayload_ByteForByte(string model)
    {
        var withAudio = JsonSerializer.Serialize(AiProviderPayloadBuilder.BuildOpenAiMessages(Request(model)));
        var withoutAudio = JsonSerializer.Serialize(AiProviderPayloadBuilder.BuildOpenAiMessages(new AiProviderRequest
        {
            Model = model,
            SystemPrompt = "system text",
            UserPrompt = "user text",
            Messages = new List<AiChatMessage>
            {
                new() { Role = "system", Content = "system text" },
                new() { Role = "user", Content = "user text" },
            },
        }));

        Assert.Equal(withoutAudio, withAudio);
    }

    [Theory]
    [InlineData("gpt-audio", true)]
    [InlineData("gpt-audio-1.5", true)]
    [InlineData("GPT-AUDIO-MINI", true)]
    [InlineData("gpt-4o-audio-preview", true)]
    [InlineData("gpt-4o", false)]
    [InlineData("whisper-1", false)]
    [InlineData("gpt-4o-transcribe", false)]
    [InlineData("claude-sonnet-5", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAudioChatModel_RecognisesTheAudioChatFamilyOnly(string? model, bool expected)
        => Assert.Equal(expected, AiProviderPayloadBuilder.IsAudioChatModel(model));

    [Theory]
    [InlineData("gpt-audio-1.5", "max_completion_tokens")]
    [InlineData("gpt-4o-audio-preview", "max_completion_tokens")]
    [InlineData("gpt-4o", "max_tokens")]
    [InlineData("claude-sonnet-5", "max_tokens")]
    [InlineData("whisper-1", "max_tokens")]
    public void TheTokenLimitParameter_FollowsTheModel(string model, string expected)
        => Assert.Equal(expected, AiProviderPayloadBuilder.MaxTokensParameter(model));

    // ── The whole OpenAI-compatible registry leg, against a stub server ───────────────────────

    [Fact]
    public async Task TheRegistryProvider_PostsTheClip_WithTheLiveVoiceKey_AndMaxCompletionTokens()
    {
        string? body = null;
        AuthenticationHeaderValue? auth = null;
        var provider = await NewProviderAsync(
            new StubHandler(async request =>
            {
                body = await request.Content!.ReadAsStringAsync();
                auth = request.Headers.Authorization;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"{\\\"ok\\\":true}\"},\"finish_reason\":\"stop\"}],\"model\":\"gpt-audio-1.5\",\"usage\":{\"prompt_tokens\":42,\"completion_tokens\":7}}",
                        Encoding.UTF8,
                        "application/json"),
                };
            }),
            liveVoiceKey: "sk-live-voice-key");

        var completion = await provider.CompleteAsync(new AiProviderRequest
        {
            ProviderCode = "openai-audio",
            Model = "gpt-audio-1.5",
            SystemPrompt = "system text",
            UserPrompt = "user text",
            Temperature = 0,
            MaxTokens = 900,
            AudioAttachments = [new AiProviderAudioAttachment { MimeType = "audio/mpeg", Data = Clip }],
            Messages = new List<AiChatMessage>
            {
                new() { Role = "system", Content = "system text" },
                new() { Role = "user", Content = "user text" },
            },
        }, CancellationToken.None);

        Assert.Equal("{\"ok\":true}", completion.Text);
        Assert.Equal("Bearer", auth!.Scheme);
        Assert.Equal("sk-live-voice-key", auth.Parameter);

        using var json = JsonDocument.Parse(body!);
        var root = json.RootElement;
        Assert.Equal("gpt-audio-1.5", root.GetProperty("model").GetString());
        Assert.Equal(900, root.GetProperty("max_completion_tokens").GetInt32());
        Assert.False(root.TryGetProperty("max_tokens", out _));
        Assert.False(root.TryGetProperty("response_format", out _));
        var userParts = root.GetProperty("messages")[1].GetProperty("content").EnumerateArray().ToArray();
        Assert.Equal("input_audio", userParts[1].GetProperty("type").GetString());
        Assert.Equal(Convert.ToBase64String(Clip), userParts[1].GetProperty("input_audio").GetProperty("data").GetString());
    }

    [Fact]
    public async Task WithoutAnyKey_TheAudioProviderFailsClosed_AndNeverCallsOut()
    {
        var called = false;
        var provider = await NewProviderAsync(
            new StubHandler(_ =>
            {
                called = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }),
            liveVoiceKey: "");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new AiProviderRequest
        {
            ProviderCode = "openai-audio",
            Model = "gpt-audio-1.5",
            SystemPrompt = "system text",
            UserPrompt = "user text",
        }, CancellationToken.None));

        Assert.Contains("openai-audio", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(called);
    }

    [Fact]
    public async Task TheRegistryKey_FallsBackToTheLiveVoiceAccount_OnlyForTheAudioJudge_AndAPastedKeyWins()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"audio-key-{Guid.NewGuid():N}").Options;
        await using var db = new LearnerDbContext(options);
        var dp = new EphemeralDataProtectionProvider();
        db.AiProviders.AddRange(
            Row("openai-audio", encryptedKey: string.Empty, active: true),
            Row("other-keyless", encryptedKey: string.Empty, active: true),
            Row("pasted", encryptedKey: dp.CreateProtector("AiProvider.PlatformKey.v1").Protect("sk-pasted-on-row"), active: true),
            Row("openai-audio-off", encryptedKey: string.Empty, active: false));
        await db.SaveChangesAsync();
        var registry = new AiProviderRegistry(db, dp, Options.Create(new LiveVoiceOptions { OpenAiApiKey = " sk-shared " }));

        Assert.Equal("sk-shared", await registry.GetPlatformKeyAsync("openai-audio", default));
        Assert.Null(await registry.GetPlatformKeyAsync("other-keyless", default));
        Assert.Equal("sk-pasted-on-row", await registry.GetPlatformKeyAsync("pasted", default));
        Assert.Null(await registry.GetPlatformKeyAsync("openai-audio-off", default));

        // No live-voice key configured: nothing to fall back to.
        var bare = new AiProviderRegistry(db, dp, Options.Create(new LiveVoiceOptions()));
        Assert.Null(await bare.GetPlatformKeyAsync("openai-audio", default));
        // Two-argument construction (every older caller) is unchanged.
        Assert.Null(await new AiProviderRegistry(db, dp).GetPlatformKeyAsync("openai-audio", default));
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    private static AiProvider Row(string code, string encryptedKey, bool active)
        => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = code,
            Name = code,
            Dialect = AiProviderDialect.OpenAiCompatible,
            Category = AiProviderCategory.Asr,
            BaseUrl = "https://api.openai.com/v1",
            EncryptedApiKey = encryptedKey,
            ApiKeyHint = string.Empty,
            DefaultModel = "gpt-audio-1.5",
            IsActive = active,
            FailoverPriority = 95,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private static async Task<RegistryBackedProvider> NewProviderAsync(HttpMessageHandler handler, string liveVoiceKey)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"audio-provider-{Guid.NewGuid():N}").Options;
        var db = new LearnerDbContext(options);
        db.AiProviders.Add(Row("openai-audio", encryptedKey: string.Empty, active: true));
        await db.SaveChangesAsync();
        var registry = new AiProviderRegistry(
            db,
            new EphemeralDataProtectionProvider(),
            Options.Create(new LiveVoiceOptions { OpenAiApiKey = liveVoiceKey }));
        return new RegistryBackedProvider(
            new StubHttpClientFactory(handler),
            registry,
            Options.Create(new AiProviderOptions()));
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request);
    }
}
