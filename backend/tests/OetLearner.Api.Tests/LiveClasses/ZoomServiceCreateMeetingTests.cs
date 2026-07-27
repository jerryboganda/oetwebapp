using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests.LiveClasses;

/// <summary>
/// Verifies ZoomMeetingService.CreateMeetingAsync hits the correct Zoom REST
/// path (POST /users/{hostUserId}/meetings) with bearer auth, parses the
/// returned meeting payload, falls back to a sandbox meeting when credentials
/// are missing, and fails loud on API errors / disabled integration.
/// </summary>
public sealed class ZoomServiceCreateMeetingTests
{
    [Fact]
    public async Task CreateMeetingAsync_PostsToHostMeetingsEndpointAndParsesResponse()
    {
        string? capturedBody = null;
        var handler = new RecordingHandler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsoluteUri.Contains("/oauth/token", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("{\"access_token\":\"server-token\",\"token_type\":\"bearer\",\"expires_in\":3600}");
            }

            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return JsonResponse(
                "{\"id\":123456789,\"join_url\":\"https://zoom.test/j/123456789\"," +
                "\"start_url\":\"https://zoom.test/s/123456789?zak=host\",\"password\":\"pw123\"}");
        });
        var service = CreateService(handler);

        var result = await service.CreateMeetingAsync(
            topic: "OET Speaking Mock — Final Readiness",
            startTime: DateTimeOffset.UtcNow.AddDays(3),
            durationMinutes: 20,
            timezone: "UTC",
            CancellationToken.None);

        Assert.Equal(123456789, result.MeetingId);
        Assert.Equal("https://zoom.test/j/123456789", result.JoinUrl);
        Assert.Equal("https://zoom.test/s/123456789?zak=host", result.StartUrl);
        Assert.Equal("pw123", result.Password);

        var meetingRequest = handler.Requests.Single(r =>
            r.RequestUri!.AbsoluteUri.Contains("/meetings", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpMethod.Post, meetingRequest.Method);
        Assert.Contains("/users/platform-host/meetings", meetingRequest.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", meetingRequest.Headers.Authorization?.Scheme);
        Assert.Equal("server-token", meetingRequest.Headers.Authorization?.Parameter);
        Assert.NotNull(capturedBody);
        Assert.Contains("OET Speaking Mock", capturedBody);
        // Snake-case serialization is part of the Zoom contract.
        Assert.Contains("\"start_time\"", capturedBody);
    }

    [Fact]
    public async Task CreateMeetingAsync_ReturnsSandboxMeetingWhenCredentialsMissing()
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

        var result = await service.CreateMeetingAsync("Sandbox topic", DateTimeOffset.UtcNow.AddDays(1), 30, "UTC", CancellationToken.None);

        Assert.StartsWith("https://zoom.us/j/sandbox-", result.JoinUrl);
        Assert.StartsWith("https://zoom.us/s/sandbox-", result.StartUrl);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CreateMeetingAsync_ThrowsOnZoomApiError()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.AbsoluteUri.Contains("/oauth/token", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse("{\"access_token\":\"server-token\",\"token_type\":\"bearer\",\"expires_in\":3600}"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"code\":200,\"message\":\"boom\"}", Encoding.UTF8, "application/json"),
            });
        });
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateMeetingAsync("Error topic", DateTimeOffset.UtcNow.AddDays(1), 30, "UTC", CancellationToken.None));

        Assert.Contains("Zoom API returned 500", ex.Message);
    }

    [Fact]
    public async Task CreateMeetingAsync_ThrowsWhenIntegrationDisabled()
    {
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("should not be called"));
        var service = new ZoomMeetingService(
            new StaticHttpClientFactory(handler),
            TestRuntimeSettingsProvider.FromZoomOptions(new ZoomOptions { Enabled = false }),
            NullLogger<ZoomMeetingService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateMeetingAsync("Disabled topic", DateTimeOffset.UtcNow.AddDays(1), 30, "UTC", CancellationToken.None));
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
