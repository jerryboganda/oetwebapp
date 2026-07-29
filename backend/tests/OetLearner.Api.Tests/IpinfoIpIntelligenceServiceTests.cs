using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Security;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Tests;

public sealed class IpinfoIpIntelligenceServiceTests
{
    [Fact]
    public async Task LookupAsync_ConfiguredProvider_MapsPrivacyAndHostingFlags_AndCaches()
    {
        var token = new string('x', 24);
        var handler = new RecordingHandler(
            """
            {
              "anonymous": {
                "is_proxy": false,
                "is_relay": false,
                "is_tor": true,
                "is_vpn": false,
                "is_res_proxy": false
              },
              "is_anonymous": true,
              "is_hosting": true,
              "as": { "type": "hosting" }
            }
            """);
        var service = CreateService(handler, new IpIntelligenceSettings("ipinfo", token));

        var first = await service.LookupAsync("8.8.8.8", CancellationToken.None);
        var second = await service.LookupAsync("8.8.8.8", CancellationToken.None);

        Assert.NotNull(first);
        Assert.True(first.IsAnonymizer);
        Assert.True(first.IsDatacenter);
        Assert.Equal("ipinfo", first.Provider);
        Assert.Equal(first, second);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal(token, handler.AuthorizationParameter);
        Assert.Equal("/lookup/8.8.8.8", handler.RequestPath);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.10")]
    [InlineData("fc00::1")]
    [InlineData("not-an-ip")]
    public async Task LookupAsync_PrivateOrInvalidAddress_DoesNotCallProvider(string address)
    {
        var handler = new RecordingHandler("{}");
        var service = CreateService(
            handler,
            new IpIntelligenceSettings("ipinfo", new string('x', 24)));

        var result = await service.LookupAsync(address, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task LookupAsync_UnconfiguredProvider_DoesNotCallProvider()
    {
        var handler = new RecordingHandler("{}");
        var service = CreateService(handler, IpIntelligenceSettings.Unconfigured);

        var result = await service.LookupAsync("8.8.8.8", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, handler.RequestCount);
    }

    private static IpinfoIpIntelligenceService CreateService(
        RecordingHandler handler,
        IpIntelligenceSettings settings)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.ipinfo.io/"),
            Timeout = TimeSpan.FromSeconds(3),
        };
        var runtimeSettings = new TestRuntimeSettingsProvider(
            TestRuntimeSettingsProvider.Base() with { IpIntelligence = settings });

        return new IpinfoIpIntelligenceService(
            new StubHttpClientFactory(client),
            runtimeSettings,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<IpinfoIpIntelligenceService>.Instance);
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(IpinfoIpIntelligenceService.HttpClientName, name);
            return client;
        }
    }

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? RequestPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            RequestPath = request.RequestUri?.AbsolutePath;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
        }
    }
}
