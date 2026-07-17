using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;

namespace OetWithDrHesham.Api.Services.Content;

// ═════════════════════════════════════════════════════════════════════════════
// S3CompatibleFileStorage — Wave 4 deferred.
//
// IFileStorage backed by any S3-compatible object store:
//   • AWS S3                 — omit EndpointUrl; set AwsRegion
//   • DigitalOcean Spaces    — EndpointUrl = "https://{region}.digitaloceanspaces.com"
//   • Cloudflare R2          — EndpointUrl = "https://{accountId}.r2.cloudflarestorage.com"
//
// Activate by setting appsettings.json / env:
//   Storage:Provider          = "s3"
//   Storage:BucketName        = "oet-media"
//   Storage:EndpointUrl       = "https://ams3.digitaloceanspaces.com"   (optional)
//   Storage:AccessKeyId       = "..."
//   Storage:SecretAccessKey   = "..."
//   Storage:AwsRegion         = "us-east-1"                             (optional, default)
//   Storage:SignedReadTtlSeconds = 3600                                  (optional)
//
// Key convention: the same POSIX-style opaque keys used by LocalFileStorage
// map directly to S3 object keys. No path traversal risk — S3 keys are
// arbitrary strings, but we still validate the key isn't empty.
// ═════════════════════════════════════════════════════════════════════════════

public sealed class S3CompatibleFileStorage : IFileStorage, IAsyncDisposable
{
    private const long MultipartPartSizeBytes = 8L * 1024 * 1024;
    private readonly IAmazonS3 _client;
    private readonly StorageOptions _options;
    private readonly bool _ownsClient;
    private readonly Func<IAmazonS3, string, string, Stream, CancellationToken, Task> _uploadAsync;
    private readonly Func<string> _tempPathFactory;

    public S3CompatibleFileStorage(IOptions<StorageOptions> options)
    {
        _options = options.Value;
        _client = CreateClient(_options);
        _ownsClient = true;
        _uploadAsync = UploadWithTransferUtilityAsync;
        _tempPathFactory = CreateTempPath;
    }

    internal S3CompatibleFileStorage(
        IOptions<StorageOptions> options,
        IAmazonS3 client,
        Func<IAmazonS3, string, string, Stream, CancellationToken, Task>? uploadAsync = null,
        Func<string>? tempPathFactory = null)
    {
        _options = options.Value;
        _client = client;
        _uploadAsync = uploadAsync ?? UploadWithTransferUtilityAsync;
        _tempPathFactory = tempPathFactory ?? CreateTempPath;
    }

    private static AmazonS3Client CreateClient(StorageOptions options)
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = !string.IsNullOrWhiteSpace(options.EndpointUrl), // needed for DO Spaces / R2
        };

        if (!string.IsNullOrWhiteSpace(options.EndpointUrl))
            config.ServiceURL = options.EndpointUrl;
        else
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.AwsRegion);

        return new AmazonS3Client(
            options.AccessKeyId ?? throw new InvalidOperationException("Storage:AccessKeyId is required when Provider=s3"),
            options.SecretAccessKey ?? throw new InvalidOperationException("Storage:SecretAccessKey is required when Provider=s3"),
            config);
    }

    private string Bucket => _options.BucketName
        ?? throw new InvalidOperationException("Storage:BucketName is required when Provider=s3");

    // ─────────────────────────────────────────────────────────────────────────
    // Write
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<long> WriteAsync(string key, Stream source, CancellationToken ct)
    {
        ValidateKey(key);
        ct.ThrowIfCancellationRequested();

        var countedSource = new CountingReadStream(source);
        await _uploadAsync(_client, Bucket, key, countedSource, ct);
        return countedSource.PayloadLength;
    }

    public Task<Stream> OpenWriteAsync(string key, CancellationToken ct)
    {
        ValidateKey(key);
        ct.ThrowIfCancellationRequested();
        Stream stream = new S3DeferredWriteStream(this, key, _tempPathFactory(), ct);
        return Task.FromResult(stream);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Read
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        var result = await OpenReadWithMetadataAsync(key, ct);
        return result.Stream;
    }

    public async Task<FileStorageReadResult> OpenReadWithMetadataAsync(string key, CancellationToken ct)
    {
        ValidateKey(key);
        var req = new GetObjectRequest { BucketName = Bucket, Key = key };
        try
        {
            var response = await _client.GetObjectAsync(req, ct);
            return new FileStorageReadResult(
                new S3ResponseStream(response),
                response.ContentLength);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException($"Storage object '{key}' was not found.", key, ex);
        }
    }

    public Uri? ResolveReadUrl(string key, TimeSpan ttl)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var req = new GetPreSignedUrlRequest
        {
            BucketName = Bucket,
            Key        = key,
            Verb       = HttpVerb.GET,
            Expires    = DateTime.UtcNow.Add(ttl),
        };
        var url = _client.GetPreSignedURL(req);
        return new Uri(url, UriKind.Absolute);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Existence / metadata
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        ValidateKey(key);
        try
        {
            var req = new GetObjectMetadataRequest { BucketName = Bucket, Key = key };
            await _client.GetObjectMetadataAsync(req, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async IAsyncEnumerable<string> ListKeysAsync(
        string prefix,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ValidateKey(prefix);
        var req = new ListObjectsV2Request { BucketName = Bucket, Prefix = prefix };
        while (true)
        {
            var page = await _client.ListObjectsV2Async(req, ct);
            foreach (var obj in page.S3Objects)
            {
                ct.ThrowIfCancellationRequested();
                yield return obj.Key;
            }

            if (!page.IsTruncated)
                yield break;

            req.ContinuationToken = GetNextContinuationToken(
                req.ContinuationToken,
                page.NextContinuationToken);
        }
    }

    public async Task<long> LengthAsync(string key, CancellationToken ct)
    {
        ValidateKey(key);
        var req = new GetObjectMetadataRequest { BucketName = Bucket, Key = key };
        var meta = await _client.GetObjectMetadataAsync(req, ct);
        return meta.ContentLength;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Delete / Move
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<bool> DeleteAsync(string key, CancellationToken ct)
    {
        ValidateKey(key);
        if (!await ExistsAsync(key, ct)) return false;

        var req = new DeleteObjectRequest { BucketName = Bucket, Key = key };
        await _client.DeleteObjectAsync(req, ct);
        return true;
    }

    public async Task MoveAsync(
        string sourceKey,
        string destKey,
        bool overwrite,
        CancellationToken ct)
    {
        ValidateKey(sourceKey);
        ValidateKey(destKey);
        if (!overwrite && await ExistsAsync(destKey, ct)) return;

        // S3 copy + delete.
        var copy = new CopyObjectRequest
        {
            SourceBucket      = Bucket,
            SourceKey         = sourceKey,
            DestinationBucket = Bucket,
            DestinationKey    = destKey,
        };
        await _client.CopyObjectAsync(copy, ct);
        await _client.DeleteObjectAsync(
            new DeleteObjectRequest { BucketName = Bucket, Key = sourceKey },
            ct);
    }

    public async Task<int> DeletePrefixAsync(string prefix, CancellationToken ct)
    {
        ValidateKey(prefix);
        var list = new ListObjectsV2Request
        {
            BucketName = Bucket,
            Prefix = prefix,
            MaxKeys = 1000,
        };
        var count = 0;
        while (true)
        {
            var page = await _client.ListObjectsV2Async(list, ct);
            for (var offset = 0; offset < page.S3Objects.Count; offset += 1000)
            {
                var objects = page.S3Objects
                    .Skip(offset)
                    .Take(1000)
                    .Select(obj => new KeyVersion { Key = obj.Key })
                    .ToList();
                var response = await _client.DeleteObjectsAsync(
                    new DeleteObjectsRequest
                    {
                        BucketName = Bucket,
                        Objects = objects,
                    },
                    ct);

                if (response.DeleteErrors.Count > 0)
                {
                    var first = response.DeleteErrors[0];
                    throw new AmazonS3Exception(
                        $"S3 failed to delete object '{first.Key}' ({first.Code}).");
                }

                count += objects.Count;
            }

            if (!page.IsTruncated)
                return count;

            list.ContinuationToken = GetNextContinuationToken(
                list.ContinuationToken,
                page.NextContinuationToken);
        }
    }

    public string? TryResolveLocalPath(string key) => null; // S3 has no local path

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task UploadWithTransferUtilityAsync(
        IAmazonS3 client,
        string bucket,
        string key,
        Stream source,
        CancellationToken ct)
    {
        using var transfer = new TransferUtility(client);
        var request = new TransferUtilityUploadRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = source,
            PartSize = MultipartPartSizeBytes,
            AutoCloseStream = false,
            AutoResetStreamPosition = false,
        };
        await transfer.UploadAsync(request, ct);
    }

    private static string GetNextContinuationToken(string? current, string? next)
    {
        if (string.IsNullOrWhiteSpace(next) ||
            string.Equals(current, next, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "S3 returned a truncated listing without a usable continuation token.");
        }

        return next;
    }

    private static string CreateTempPath()
        => Path.Combine(Path.GetTempPath(), $"oet-s3-{Guid.NewGuid():N}.tmp");

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Storage key is required.");
    }

    public ValueTask DisposeAsync()
    {
        if (_ownsClient)
            _client.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Keeps the GetObjectResponse alive for the lifetime of its response stream
    /// and releases both when ASP.NET disposes the returned stream.
    /// </summary>
    private sealed class S3ResponseStream(GetObjectResponse response) : Stream
    {
        private readonly Stream _inner = response.ResponseStream;
        private int _disposed;

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken)
            => _inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count)
            => _inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
            => _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count)
            => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                response.Dispose();
            }

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CountingReadStream(Stream inner) : Stream
    {
        private long _bytesRead;
        private readonly long? _knownPayloadLength = TryGetRemainingLength(inner);

        public long PayloadLength => _knownPayloadLength ?? Interlocked.Read(ref _bytesRead);
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken)
            => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count)
            => RecordRead(inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer)
            => RecordRead(inner.Read(buffer));

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
            => RecordRead(await inner.ReadAsync(buffer, offset, count, cancellationToken));

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
            => RecordRead(await inner.ReadAsync(buffer, cancellationToken));

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            // The caller owns the source stream.
            base.Dispose(disposing);
        }

        private int RecordRead(int count)
        {
            Interlocked.Add(ref _bytesRead, count);
            return count;
        }

        private static long? TryGetRemainingLength(Stream source)
        {
            if (!source.CanSeek)
                return null;

            try
            {
                return Math.Max(0, source.Length - source.Position);
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Deferred-write stream helper (bounded disk spool; upload on async dispose)
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class S3DeferredWriteStream : Stream
    {
        private readonly S3CompatibleFileStorage _owner;
        private readonly string _key;
        private readonly string _tempPath;
        private readonly CancellationToken _ct;
        private readonly FileStream _inner;
        private readonly object _disposeGate = new();
        private Task? _disposeTask;
        private bool _syncDisposed;

        public S3DeferredWriteStream(
            S3CompatibleFileStorage owner,
            string key,
            string tempPath,
            CancellationToken ct)
        {
            _owner = owner;
            _key = key;
            _tempPath = tempPath;
            _ct = ct;
            _inner = new FileStream(
                tempPath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.None,
                    BufferSize = 81920,
                    Options = FileOptions.Asynchronous |
                              FileOptions.SequentialScan |
                              FileOptions.DeleteOnClose,
                });
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken)
            => _inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count)
            => _inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _inner.Read(buffer);
        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
            => _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count)
            => _inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);
        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
            => _inner.WriteAsync(buffer, offset, count, cancellationToken);
        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
            => _inner.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                base.Dispose(false);
                return;
            }

            lock (_disposeGate)
            {
                if (_disposeTask is not null)
                {
                    if (!_disposeTask.IsCompleted)
                    {
                        throw new InvalidOperationException(
                            "Asynchronous disposal of this S3 write stream is still in progress.");
                    }

                    return;
                }

                if (_syncDisposed)
                    return;

                _syncDisposed = true;
            }

            try
            {
                _inner.Dispose();
            }
            finally
            {
                TryDeleteTempFile();
            }

            throw new InvalidOperationException(
                "S3 write streams must be disposed asynchronously with 'await using'; " +
                "the synchronous disposal did not upload the object.");
        }

        public override ValueTask DisposeAsync()
        {
            lock (_disposeGate)
            {
                if (_syncDisposed)
                {
                    return new ValueTask(Task.FromException(new InvalidOperationException(
                        "The S3 write stream was synchronously disposed and cannot be uploaded.")));
                }

                _disposeTask ??= CommitAndCleanupAsync();
                return new ValueTask(_disposeTask);
            }
        }

        private async Task CommitAndCleanupAsync()
        {
            try
            {
                await _inner.FlushAsync(_ct);
                _inner.Position = 0;
                await _owner.WriteAsync(_key, _inner, _ct);
            }
            finally
            {
                try
                {
                    await _inner.DisposeAsync();
                }
                finally
                {
                    TryDeleteTempFile();
                }
            }
        }

        private void TryDeleteTempFile()
        {
            try
            {
                File.Delete(_tempPath);
            }
            catch (IOException)
            {
                // DeleteOnClose remains the primary cleanup guarantee.
            }
            catch (UnauthorizedAccessException)
            {
                // DeleteOnClose remains the primary cleanup guarantee.
            }
        }
    }
}
