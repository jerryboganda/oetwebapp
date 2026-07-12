using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Content;
using Xunit;

namespace OetLearner.Api.Tests;

public sealed class LocalFileStorageTests
{
    [Fact]
    public async Task WriteAsync_RejectsTraversalSegments()
    {
        using var temp = new TempDirectory();
        var storage = CreateStorage(temp.Path);
        await using var payload = new MemoryStream(new byte[] { 1, 2, 3 });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.WriteAsync("uploads/../evil.txt", payload, default));
    }

    [Fact]
    public async Task WriteAsync_AllowsNormalRelativeKeys()
    {
        using var temp = new TempDirectory();
        var storage = CreateStorage(temp.Path);
        await using var payload = new MemoryStream(new byte[] { 1, 2, 3 });

        var bytes = await storage.WriteAsync("uploads/staging/file.bin", payload, default);

        Assert.Equal(3, bytes);
        Assert.True(storage.Exists("uploads/staging/file.bin"));
        Assert.Equal(3, storage.Length("uploads/staging/file.bin"));
    }

    [Fact]
    public async Task OpenReadWithMetadataAsync_ReturnsAsyncSeekableStreamAndLength()
    {
        using var temp = new TempDirectory();
        var storage = CreateStorage(temp.Path);
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        await using (var source = new MemoryStream(payload))
        {
            await storage.WriteAsync("audio/sample.wav", source, default);
        }

        var result = await storage.OpenReadWithMetadataAsync("audio/sample.wav", default);
        await using var stream = result.Stream;

        var fileStream = Assert.IsType<FileStream>(stream);
        Assert.True(fileStream.CanSeek);
        Assert.True(fileStream.IsAsync);
        Assert.Equal(payload.Length, result.Length);
        Assert.Equal(payload, await ReadAllBytesAsync(fileStream));
    }

    [Fact]
    public async Task OpenReadWithMetadataAsync_NormalizesMissingKey()
    {
        using var temp = new TempDirectory();
        var storage = CreateStorage(temp.Path);

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            storage.OpenReadWithMetadataAsync("missing/parent/audio.wav", default));
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        using var destination = new MemoryStream();
        await stream.CopyToAsync(destination);
        return destination.ToArray();
    }

    private static LocalFileStorage CreateStorage(string root)
        => new(new TestWebHostEnvironment(root), Options.Create(new StorageOptions { LocalRootPath = "storage" }));

    private sealed class TestWebHostEnvironment(string root) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"oet-storage-{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
