using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    [Fact]
    public async Task CandidateProbe_UsesCombinedReadWithoutExists_AndContinuesOnMissing()
    {
        var sha = new string('c', 64);
        var bytes = new byte[] { 9, 8, 7 };
        var expectedWavKey = ContentAddressed.PublishedKey("listening/tts", sha, "wav");
        var expectedMp3Key = ContentAddressed.PublishedKey("listening/tts", sha, "mp3");
        var storage = new CandidateProbeStorage(expectedMp3Key, bytes);
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFileStorage>();
                services.AddSingleton<IFileStorage>(storage);
            });
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/v1/listening/audio/{sha}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/mpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal([expectedWavKey, expectedMp3Key], storage.OpenedKeys);
        Assert.Equal(0, storage.ExistsCalls);
        Assert.Equal(0, storage.LegacyOpenReadCalls);
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

    private sealed class CandidateProbeStorage(string successKey, byte[] bytes) : IFileStorage
    {
        public List<string> OpenedKeys { get; } = [];
        public int ExistsCalls { get; private set; }
        public int LegacyOpenReadCalls { get; private set; }

        public Task<FileStorageReadResult> OpenReadWithMetadataAsync(string key, CancellationToken ct)
        {
            OpenedKeys.Add(key);
            if (!string.Equals(key, successKey, StringComparison.Ordinal))
            {
                throw new FileNotFoundException("Candidate does not exist.", key);
            }

            Stream stream = new MemoryStream(bytes, writable: false);
            return Task.FromResult(new FileStorageReadResult(stream, bytes.LongLength));
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
        {
            LegacyOpenReadCalls++;
            throw new InvalidOperationException("Legacy open must not be used by the listening audio endpoint.");
        }

        public bool Exists(string key)
        {
            ExistsCalls++;
            throw new InvalidOperationException("Exists must not be used by the listening audio endpoint.");
        }

        public Task<long> WriteAsync(string key, Stream source, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenWriteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public bool Delete(string key) => throw new NotSupportedException();
        public long Length(string key) => throw new NotSupportedException();
        public void Move(string sourceKey, string destKey, bool overwrite) => throw new NotSupportedException();
        public int DeletePrefix(string prefix) => throw new NotSupportedException();
        public string? TryResolveLocalPath(string key) => null;
        public Uri? ResolveReadUrl(string key, TimeSpan ttl) => null;
    }
}
