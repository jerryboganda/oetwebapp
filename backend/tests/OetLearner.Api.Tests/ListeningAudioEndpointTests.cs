using System.Net;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

// ═════════════════════════════════════════════════════════════════════════════
// ListeningAudioEndpointTests — capability-by-SHA diagnostic audio serving.
//
// The diagnostic <audio> element cannot carry a bearer token, so the audio is
// served anonymously keyed by its unguessable SHA-256 content hash. These
// tests pin the contract:
//   • A valid hash that exists in storage streams the bytes (anonymous).
//   • A well-formed but unknown hash returns 404 (no probing leak).
//   • A malformed hash is rejected as 400 (and cannot escape the tts root).
// ═════════════════════════════════════════════════════════════════════════════

public class ListeningAudioEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ListeningAudioEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ServesStoredDiagnosticAudio_Anonymously()
    {
        // 64-char lowercase hex — a valid SHA-256.
        var sha = new string('a', 64);
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        using (var scope = _factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            var key = ContentAddressed.PublishedKey("listening/tts", sha, "wav");
            using var source = new MemoryStream(bytes);
            await storage.WriteAsync(key, source, CancellationToken.None);
        }

        using var anonymousClient = _factory.CreateClient();
        var response = await anonymousClient.GetAsync($"/v1/listening/audio/{sha}.wav");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/wav", response.Content.Headers.ContentType?.MediaType);
        var served = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(bytes, served);
    }

    [Fact]
    public async Task UnknownHash_Returns404()
    {
        var sha = new string('b', 64);
        using var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.GetAsync($"/v1/listening/audio/{sha}.wav");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("not-a-hash.wav")]
    [InlineData("abc.wav")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz.wav")]
    public async Task MalformedHash_Returns400(string fileName)
    {
        using var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.GetAsync($"/v1/listening/audio/{fileName}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
