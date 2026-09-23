using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// B9: live tutor rooms record audio only — the StartEgress request is an
/// audio-only room composite (no layout) writing an OGG file.
/// </summary>
public sealed class LiveKitCloudGatewayEgressTests
{
    [Fact]
    public async Task StartEgress_SendsAudioOnlyRoomCompositeWithOggFile()
    {
        var handler = new CapturingHandler();
        var gateway = new LiveKitCloudGateway(
            Options.Create(new LiveKitOptions
            {
                ApiKey = "api-key-test",
                ApiSecret = "api-secret-test-0123456789-0123456789-abcdef",
                WebhookSigningSecret = "webhook-secret-test",
                WssUrl = "wss://livekit.test",
            }),
            new SingleClientFactory(handler),
            TimeProvider.System,
            NullLogger<LiveKitCloudGateway>.Instance);

        var egressId = await gateway.StartEgressAsync(
            "oet-speaking-room-1",
            "s3://oet-bucket/oet-speaking/oet-speaking-room-1.ogg",
            CancellationToken.None);

        Assert.Equal("egress-1", egressId);
        Assert.EndsWith("/twirp/livekit.Egress/StartEgress", handler.LastUri!.AbsolutePath);
        using var body = JsonDocument.Parse(handler.LastBody!);
        var root = body.RootElement;
        Assert.Equal("oet-speaking-room-1", root.GetProperty("room_name").GetString());
        var template = root.GetProperty("template");
        Assert.True(template.GetProperty("audio_only").GetBoolean());
        Assert.False(template.TryGetProperty("layout", out _));
        var file = root.GetProperty("outputs")[0].GetProperty("file");
        Assert.Equal("OGG", file.GetProperty("file_type").GetString());
        Assert.Equal("oet-speaking/oet-speaking-room-1.ogg", file.GetProperty("filepath").GetString());
        Assert.Equal("oet-bucket", root.GetProperty("storage").GetProperty("s3").GetProperty("bucket").GetString());
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"egress_id\":\"egress-1\",\"room_id\":\"RM_1\",\"status\":\"EGRESS_STARTING\"}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
