using Microsoft.AspNetCore.Http;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Content;

namespace OetWithDrHesham.Api.Tests;

public sealed class StoredMediaFileTests
{
    [Fact]
    public async Task OpenStoredMediaFileAsync_UsesSingleCombinedRead()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        IFileStorage storage = new TrackingStorage(
            () => Task.FromResult(new FileStorageReadResult(stream, stream.Length)));

        var result = await storage.OpenStoredMediaFileAsync(
            "speaking/attempt.webm",
            fallbackContentType: null,
            default);

        await using (result.Stream)
        {
            Assert.Same(stream, result.Stream);
            Assert.Equal(3, result.Length);
            Assert.Equal("audio/webm", result.ContentType);
        }

        var tracking = Assert.IsType<TrackingStorage>(storage);
        Assert.Equal(1, tracking.CombinedReadCalls);
        Assert.Equal(0, tracking.ExistsCalls);
        Assert.Equal(0, tracking.LegacyOpenReadCalls);
        Assert.Equal(0, tracking.LengthCalls);
    }

    [Fact]
    public async Task OpenStoredMediaFileAsync_MapsOnlyMissingFileToAudioNotFound()
    {
        IFileStorage storage = new TrackingStorage(
            () => Task.FromException<FileStorageReadResult>(new FileNotFoundException()));

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            storage.OpenStoredMediaFileAsync("speaking/missing.webm", null, default));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        Assert.Equal("audio_not_found", exception.ErrorCode);
    }

    [Fact]
    public async Task OpenStoredMediaFileAsync_PropagatesStorageErrors()
    {
        var expected = new InvalidOperationException("Storage unavailable.");
        IFileStorage storage = new TrackingStorage(
            () => Task.FromException<FileStorageReadResult>(expected));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.OpenStoredMediaFileAsync("speaking/attempt.webm", null, default));

        Assert.Same(expected, exception);
    }

    private sealed class TrackingStorage(Func<Task<FileStorageReadResult>> open) : IFileStorage
    {
        public int CombinedReadCalls { get; private set; }
        public int ExistsCalls { get; private set; }
        public int LegacyOpenReadCalls { get; private set; }
        public int LengthCalls { get; private set; }

        public Task<FileStorageReadResult> OpenReadWithMetadataAsync(string key, CancellationToken ct)
        {
            CombinedReadCalls++;
            return open();
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
        {
            LegacyOpenReadCalls++;
            throw new InvalidOperationException("Legacy open must not be used.");
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ExistsCalls++;
            throw new InvalidOperationException("Exists must not be used.");
        }

        public Task<long> LengthAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            LengthCalls++;
            throw new InvalidOperationException("Length must not be used.");
        }

        public Task<long> WriteAsync(string key, Stream source, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenWriteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            throw new NotSupportedException();
        }

        public Task MoveAsync(string sourceKey, string destKey, bool overwrite, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            throw new NotSupportedException();
        }

        public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            throw new NotSupportedException();
        }
        public string? TryResolveLocalPath(string key) => null;
        public Uri? ResolveReadUrl(string key, TimeSpan ttl) => null;
    }
}
