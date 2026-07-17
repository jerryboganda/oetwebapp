using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Services;

namespace OetWithDrHesham.Api.Tests.LiveClasses;

/// <summary>
/// Verifies ZoomMeetingService.GetZakTokenAsync hits the correct Zoom REST
/// path (GET /users/{userId}/token?type=zak), forwards a bearer token via
/// the Authorization header, and parses the returned <c>token</c> field.
/// </summary>
public sealed class ZoomServiceZakTokenTests
{
    [Fact]
    public async Task GetZakTokenAsync_CallsZoomUsersTokenEndpointWithBearerAuth()
    {
        var handler = new RecordingHandler((request, ct) =>
        {
            if (request.RequestUri!.AbsoluteUri.Contains("/oauth/token", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse("{\"access_token\":\"server-token\",\"token_type\":\"bearer\",\"expires_in\":3600}"));
            }

            return Task.FromResult(JsonResponse("{\"token\":\"zak-12345\"}"));
        });
        var service = CreateService(handler);

        var token = await service.GetZakTokenAsync("user-1@example.com", CancellationToken.None);

        Assert.Equal("zak-12345", token);
        var zakRequest = handler.Requests.Single(r => r.RequestUri!.AbsoluteUri.Contains("/users/", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpMethod.Get, zakRequest.Method);
        Assert.Contains("/users/", zakRequest.RequestUri!.AbsoluteUri);
        Assert.Contains("type=zak", zakRequest.RequestUri.Query);
        Assert.Equal("Bearer", zakRequest.Headers.Authorization?.Scheme);
        Assert.Equal("server-token", zakRequest.Headers.Authorization?.Parameter);
        // URL-encoded user id (the '@' is encoded) — guards against double-encoding regressions.
        Assert.Contains("user-1%40example.com", zakRequest.RequestUri.AbsoluteUri);
    }

    [Fact]
    public async Task GetZakTokenAsync_ReturnsNullWhenZoomReturnsNonSuccess()
    {
        var handler = new RecordingHandler((request, ct) =>
        {
            if (request.RequestUri!.AbsoluteUri.Contains("/oauth/token", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse("{\"access_token\":\"server-token\",\"token_type\":\"bearer\",\"expires_in\":3600}"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"code\":1001,\"message\":\"User not found.\"}", Encoding.UTF8, "application/json"),
            });
        });
        var service = CreateService(handler);

        var token = await service.GetZakTokenAsync("missing@example.com", CancellationToken.None);

        Assert.Null(token);
    }

    [Fact]
    public async Task GetZakTokenAsync_ReturnsNullWhenZoomIntegrationDisabled()
    {
        var service = new ZoomMeetingService(
            new StaticHttpClientFactory(new RecordingHandler((_, _) => throw new InvalidOperationException("should not be called"))),
            TestRuntimeSettingsProvider.FromZoomOptions(new ZoomOptions { Enabled = false }),
            NullLogger<ZoomMeetingService>.Instance);

        var token = await service.GetZakTokenAsync("user-1@example.com", CancellationToken.None);

        Assert.Null(token);
    }

    [Fact]
    public async Task GetZakTokenAsync_ReturnsNullWhenUserIdMissing()
    {
        var service = CreateService(new RecordingHandler((_, _) => throw new InvalidOperationException("should not be called")));

        var token = await service.GetZakTokenAsync(string.Empty, CancellationToken.None);

        Assert.Null(token);
    }

    [Fact]
    public async Task GetZakTokenAsync_ReturnsSandboxTokenWhenInSandboxMode()
    {
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("should not be called"));
        var service = new ZoomMeetingService(
            new StaticHttpClientFactory(handler),
            TestRuntimeSettingsProvider.FromZoomOptions(new ZoomOptions
            {
                Enabled = true,
                AllowSandboxFallback = true,
                ClientId = null,
            }),
            NullLogger<ZoomMeetingService>.Instance);

        var token = await service.GetZakTokenAsync("user-1@example.com", CancellationToken.None);

        Assert.NotNull(token);
        Assert.StartsWith("sandbox-zak-", token);
    }

    // -- helpers -----------------------------------------------------------

    private static ZoomMeetingService CreateService(RecordingHandler handler)
        => new(
            new StaticHttpClientFactory(handler),
            TestRuntimeSettingsProvider.FromZoomOptions(new ZoomOptions
            {
                Enabled = true,
                AccountId = "acct",
                ClientId = "client",
                ClientSecret = "secret",
                ApiBaseUrl = "https://api.zoom.test/v2",
                TokenUrl = "https://zoom.test/oauth/token",
                HostUserId = "platform-host",
            }),
            NullLogger<ZoomMeetingService>.Instance);

    private static HttpResponseMessage JsonResponse(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await respond(request, cancellationToken);
        }
    }

    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
