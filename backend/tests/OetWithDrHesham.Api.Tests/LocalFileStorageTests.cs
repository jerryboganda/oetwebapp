using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Services.Content;
using Xunit;

namespace OetWithDrHesham.Api.Tests;

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
        Assert.True(await storage.ExistsAsync("uploads/staging/file.bin", default));
        Assert.Equal(3, await storage.LengthAsync("uploads/staging/file.bin", default));
    }

    [Fact]
    public async Task AsyncOperations_PreserveMoveListDeleteAndMissingSemantics()
    {
        using var temp = new TempDirectory();
        var storage = CreateStorage(temp.Path);
        await WriteBytesAsync(storage, "uploads/source.bin", [1]);
        await WriteBytesAsync(storage, "uploads/destination.bin", [2, 3]);
        await WriteBytesAsync(storage, "uploads/nested/other.bin", [4]);

        await storage.MoveAsync(
            "uploads/source.bin",
            "uploads/destination.bin",
            overwrite: false,
            default);
        Assert.True(await storage.ExistsAsync("uploads/source.bin", default));
        Assert.Equal(2, await storage.LengthAsync("uploads/destination.bin", default));

        await storage.MoveAsync(
            "uploads/source.bin",
            "uploads/destination.bin",
            overwrite: true,
            default);
        Assert.False(await storage.ExistsAsync("uploads/source.bin", default));
        Assert.Equal(1, await storage.LengthAsync("uploads/destination.bin", default));

        var listed = await CollectAsync(storage.ListKeysAsync("uploads", default));
        Assert.Equal(
            ["uploads/destination.bin", "uploads/nested/other.bin"],
            listed.Order(StringComparer.Ordinal));

        Assert.True(await storage.DeleteAsync("uploads/destination.bin", default));
        Assert.False(await storage.DeleteAsync("uploads/destination.bin", default));
        Assert.Equal(1, await storage.DeletePrefixAsync("uploads/nested", default));
        Assert.Equal(0, await storage.DeletePrefixAsync("uploads/missing", default));
        Assert.Empty(await CollectAsync(storage.ListKeysAsync("uploads/missing", default)));
        Assert.False(await storage.ExistsAsync("uploads/missing.bin", default));
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            storage.LengthAsync("uploads/missing.bin", default));
    }

    [Fact]
    public async Task AsyncOperations_HonorPreCanceledTokensWithoutMutation()
    {
        using var temp = new TempDirectory();
        var storage = CreateStorage(temp.Path);
        await WriteBytesAsync(storage, "uploads/source.bin", [1, 2, 3]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            storage.ExistsAsync("uploads/source.bin", cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            storage.LengthAsync("uploads/source.bin", cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            storage.DeleteAsync("uploads/source.bin", cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            storage.MoveAsync("uploads/source.bin", "uploads/moved.bin", true, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            storage.DeletePrefixAsync("uploads", cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CollectAsync(storage.ListKeysAsync("uploads", cts.Token)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            storage.OpenWriteAsync("uploads/new.bin", cts.Token));

        Assert.True(await storage.ExistsAsync("uploads/source.bin", default));
        Assert.False(await storage.ExistsAsync("uploads/moved.bin", default));
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

    private static async Task WriteBytesAsync(
        IFileStorage storage,
        string key,
        byte[] payload)
    {
        await using var source = new MemoryStream(payload);
        await storage.WriteAsync(key, source, default);
    }

    private static async Task<List<string>> CollectAsync(IAsyncEnumerable<string> keys)
    {
        var result = new List<string>();
        await foreach (var key in keys)
            result.Add(key);
        return result;
    }

    private static LocalFileStorage CreateStorage(string root)
        => new(new TestWebHostEnvironment(root), Options.Create(new StorageOptions { LocalRootPath = "storage" }));

    private sealed class TestWebHostEnvironment(string root) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "OetWithDrHesham.Api.Tests";
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
