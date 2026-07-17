using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Services.Content;

namespace OetWithDrHesham.Api.Tests;

public sealed class S3CompatibleFileStorageTests
{
    [Fact]
    public async Task WriteAsync_StreamsPayload_ReportsBytes_AndLeavesSourceOpen()
    {
        using var client = new RecordingS3Client();
        var uploaded = new MemoryStream();
        var storage = CreateStorage(
            client,
            async (_, bucket, key, source, ct) =>
            {
                Assert.Equal("test-bucket", bucket);
                Assert.Equal("audio/sample.bin", key);
                await source.CopyToAsync(uploaded, ct);
            });
        var source = new NonSeekableReadStream([1, 2, 3, 4, 5]);

        var count = await storage.WriteAsync("audio/sample.bin", source, default);

        Assert.Equal(5, count);
        Assert.Equal([1, 2, 3, 4, 5], uploaded.ToArray());
        Assert.False(source.IsDisposed);
    }

    [Fact]
    public async Task OpenWriteAsync_AsyncDisposeUploadsOnceAndDeletesTempFile()
    {
        using var temp = new TempDirectory();
        using var client = new RecordingS3Client();
        var tempPath = Path.Combine(temp.Path, "deferred.tmp");
        var uploadCount = 0;
        byte[]? uploaded = null;
        var storage = CreateStorage(
            client,
            async (_, _, _, source, ct) =>
            {
                uploadCount++;
                using var destination = new MemoryStream();
                await source.CopyToAsync(destination, ct);
                uploaded = destination.ToArray();
            },
            () => tempPath);
        var stream = await storage.OpenWriteAsync("deferred/object.bin", default);
        await stream.WriteAsync(new byte[] { 9, 8, 7 });
        Assert.True(File.Exists(tempPath));

        await stream.DisposeAsync();
        await stream.DisposeAsync();

        Assert.Equal(1, uploadCount);
        Assert.Equal([9, 8, 7], uploaded);
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public async Task OpenWriteAsync_SyncDisposeThrowsWithoutUploadingAndDeletesTempFile()
    {
        using var temp = new TempDirectory();
        using var client = new RecordingS3Client();
        var tempPath = Path.Combine(temp.Path, "sync-misuse.tmp");
        var uploadCount = 0;
        var storage = CreateStorage(
            client,
            (_, _, _, _, _) =>
            {
                uploadCount++;
                return Task.CompletedTask;
            },
            () => tempPath);
        var stream = await storage.OpenWriteAsync("deferred/object.bin", default);
        await stream.WriteAsync(new byte[] { 1, 2, 3 });

        var error = Assert.Throws<InvalidOperationException>(stream.Dispose);

        Assert.Contains("disposed asynchronously", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, uploadCount);
        Assert.False(File.Exists(tempPath));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await stream.DisposeAsync());
    }

    [Fact]
    public async Task OpenWriteAsync_FailedOrCanceledUploadStillDeletesTempFile()
    {
        using var temp = new TempDirectory();
        using var client = new RecordingS3Client();
        var failedPath = Path.Combine(temp.Path, "failed.tmp");
        var failedStorage = CreateStorage(
            client,
            (_, _, _, _, _) => Task.FromException(new IOException("upload failed")),
            () => failedPath);
        var failedStream = await failedStorage.OpenWriteAsync("deferred/failed.bin", default);
        await failedStream.WriteAsync(new byte[] { 1 });

        await Assert.ThrowsAsync<IOException>(async () => await failedStream.DisposeAsync());
        Assert.False(File.Exists(failedPath));

        var canceledPath = Path.Combine(temp.Path, "canceled.tmp");
        using var cts = new CancellationTokenSource();
        var canceledStorage = CreateStorage(
            client,
            (_, _, _, _, _) => throw new InvalidOperationException("Upload should not start."),
            () => canceledPath);
        var canceledStream = await canceledStorage.OpenWriteAsync(
            "deferred/canceled.bin",
            cts.Token);
        await canceledStream.WriteAsync(new byte[] { 2 });
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await canceledStream.DisposeAsync());
        Assert.False(File.Exists(canceledPath));
    }

    [Fact]
    public async Task ListKeysAsync_PaginatesWithCancellationToken()
    {
        using var client = new RecordingS3Client();
        client.ListResponses.Enqueue(new ListObjectsV2Response
        {
            S3Objects = [new S3Object { Key = "prefix/a" }],
            IsTruncated = true,
            NextContinuationToken = "page-2",
        });
        client.ListResponses.Enqueue(new ListObjectsV2Response
        {
            S3Objects = [new S3Object { Key = "prefix/b" }],
            IsTruncated = false,
        });
        var storage = CreateStorage(client);
        using var cts = new CancellationTokenSource();

        var keys = await CollectAsync(storage.ListKeysAsync("prefix", cts.Token));

        Assert.Equal(["prefix/a", "prefix/b"], keys);
        Assert.Equal([null, "page-2"], client.ListContinuationTokens);
        Assert.All(client.ObservedTokens, token => Assert.Equal(cts.Token, token));
    }

    [Fact]
    public async Task DeletePrefixAsync_UsesProviderLimitBatches()
    {
        using var client = new RecordingS3Client();
        client.ListResponses.Enqueue(new ListObjectsV2Response
        {
            S3Objects = Enumerable.Range(0, 2001)
                .Select(index => new S3Object { Key = $"prefix/{index:D4}" })
                .ToList(),
            IsTruncated = false,
        });
        var storage = CreateStorage(client);

        var count = await storage.DeletePrefixAsync("prefix", default);

        Assert.Equal(2001, count);
        Assert.Equal([1000, 1000, 1], client.DeleteBatchSizes);
        Assert.Equal([1000], client.ListMaxKeys);
    }

    [Fact]
    public async Task MetadataDeleteAndMove_PreserveHeadAndCopyDeleteSemantics()
    {
        using var client = new RecordingS3Client();
        var storage = CreateStorage(client);
        client.MetadataHandler = (_, _) => Task.FromException<GetObjectMetadataResponse>(
            new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound });

        Assert.False(await storage.ExistsAsync("missing.bin", default));
        Assert.False(await storage.DeleteAsync("missing.bin", default));
        Assert.Equal(2, client.MetadataRequestCount);
        Assert.Equal(0, client.DeleteObjectKeys.Count);

        client.MetadataHandler = (_, _) => Task.FromResult(
            new GetObjectMetadataResponse { ContentLength = 42 });
        Assert.Equal(42, await storage.LengthAsync("source.bin", default));
        Assert.True(await storage.DeleteAsync("source.bin", default));
        Assert.Equal(["source.bin"], client.DeleteObjectKeys);

        var headsBeforeMove = client.MetadataRequestCount;
        await storage.MoveAsync("source.bin", "destination.bin", overwrite: true, default);
        Assert.Equal(headsBeforeMove, client.MetadataRequestCount);
        Assert.Equal(("source.bin", "destination.bin"), Assert.Single(client.CopyPairs));
        Assert.Equal(["source.bin", "source.bin"], client.DeleteObjectKeys);
    }

    [Fact]
    public async Task ExistsAsync_PropagatesCancellationToHeadRequest()
    {
        using var client = new RecordingS3Client();
        client.MetadataHandler = (_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new GetObjectMetadataResponse());
        };
        var storage = CreateStorage(client);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            storage.ExistsAsync("object.bin", cts.Token));
    }

    private static S3CompatibleFileStorage CreateStorage(
        IAmazonS3 client,
        Func<IAmazonS3, string, string, Stream, CancellationToken, Task>? uploadAsync = null,
        Func<string>? tempPathFactory = null)
        => new(
            Options.Create(new StorageOptions { BucketName = "test-bucket" }),
            client,
            uploadAsync,
            tempPathFactory);

    private static async Task<List<string>> CollectAsync(IAsyncEnumerable<string> source)
    {
        var items = new List<string>();
        await foreach (var item in source)
            items.Add(item);
        return items;
    }

    private sealed class RecordingS3Client : AmazonS3Client
    {
        public RecordingS3Client()
            : base(
                new AnonymousAWSCredentials(),
                new AmazonS3Config
                {
                    ServiceURL = "http://127.0.0.1",
                    ForcePathStyle = true,
                })
        {
        }

        public Queue<ListObjectsV2Response> ListResponses { get; } = new();
        public List<string?> ListContinuationTokens { get; } = [];
        public List<int> ListMaxKeys { get; } = [];
        public List<int> DeleteBatchSizes { get; } = [];
        public List<string> DeleteObjectKeys { get; } = [];
        public List<(string Source, string Destination)> CopyPairs { get; } = [];
        public List<CancellationToken> ObservedTokens { get; } = [];
        public int MetadataRequestCount { get; private set; }
        public Func<GetObjectMetadataRequest, CancellationToken, Task<GetObjectMetadataResponse>>?
            MetadataHandler { get; set; }

        public override Task<ListObjectsV2Response> ListObjectsV2Async(
            ListObjectsV2Request request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ListContinuationTokens.Add(request.ContinuationToken);
            ListMaxKeys.Add(request.MaxKeys);
            ObservedTokens.Add(cancellationToken);
            return Task.FromResult(ListResponses.Dequeue());
        }

        public override Task<DeleteObjectsResponse> DeleteObjectsAsync(
            DeleteObjectsRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteBatchSizes.Add(request.Objects.Count);
            ObservedTokens.Add(cancellationToken);
            return Task.FromResult(new DeleteObjectsResponse { DeleteErrors = [] });
        }

        public override Task<GetObjectMetadataResponse> GetObjectMetadataAsync(
            GetObjectMetadataRequest request,
            CancellationToken cancellationToken)
        {
            MetadataRequestCount++;
            ObservedTokens.Add(cancellationToken);
            return MetadataHandler?.Invoke(request, cancellationToken)
                ?? Task.FromResult(new GetObjectMetadataResponse());
        }

        public override Task<DeleteObjectResponse> DeleteObjectAsync(
            DeleteObjectRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteObjectKeys.Add(request.Key);
            ObservedTokens.Add(cancellationToken);
            return Task.FromResult(new DeleteObjectResponse());
        }

        public override Task<CopyObjectResponse> CopyObjectAsync(
            CopyObjectRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopyPairs.Add((request.SourceKey, request.DestinationKey));
            ObservedTokens.Add(cancellationToken);
            return Task.FromResult(new CopyObjectResponse());
        }
    }

    private sealed class NonSeekableReadStream(byte[] payload) : Stream
    {
        private readonly MemoryStream _inner = new(payload, writable: false);

        public bool IsDisposed { get; private set; }
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
            => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                IsDisposed = true;
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"oet-s3-tests-{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
