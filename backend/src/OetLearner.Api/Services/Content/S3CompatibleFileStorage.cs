using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Content;

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
    private readonly AmazonS3Client _client;
    private readonly StorageOptions _options;

    public S3CompatibleFileStorage(IOptions<StorageOptions> options)
    {
        _options = options.Value;

        var config = new AmazonS3Config
        {
            ForcePathStyle = !string.IsNullOrWhiteSpace(_options.EndpointUrl), // needed for DO Spaces / R2
        };

        if (!string.IsNullOrWhiteSpace(_options.EndpointUrl))
            config.ServiceURL = _options.EndpointUrl;
        else
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(_options.AwsRegion);

        _client = new AmazonS3Client(
            _options.AccessKeyId ?? throw new InvalidOperationException("Storage:AccessKeyId is required when Provider=s3"),
            _options.SecretAccessKey ?? throw new InvalidOperationException("Storage:SecretAccessKey is required when Provider=s3"),
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
        // Buffer into MemoryStream so we can report byte count (S3 SDK needs seekable stream or known length).
        using var ms = new MemoryStream();
        await source.CopyToAsync(ms, ct);
        ms.Position = 0;

        var req = new PutObjectRequest
        {
            BucketName  = Bucket,
            Key         = key,
            InputStream = ms,
            AutoCloseStream = false,
        };
        await _client.PutObjectAsync(req, ct);
        return ms.Length;
    }

    public async Task<Stream> OpenWriteAsync(string key, CancellationToken ct)
    {
        // S3 doesn't support streaming PUT without known content-length.
        // Return a MemoryStream that flushes to S3 on dispose via a wrapper.
        ValidateKey(key);
        var buffer = new S3DeferredWriteStream(this, key);
        return await Task.FromResult<Stream>(buffer);
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

    public bool Exists(string key)
    {
        ValidateKey(key);
        try
        {
            var req = new GetObjectMetadataRequest { BucketName = Bucket, Key = key };
            // Synchronous — only called in non-hot paths (publish gate, import checks).
#pragma warning disable CA2012
            _client.GetObjectMetadataAsync(req).GetAwaiter().GetResult();
#pragma warning restore CA2012
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public IEnumerable<string> ListKeys(string prefix)
    {
        ValidateKey(prefix);
        var req = new ListObjectsV2Request { BucketName = Bucket, Prefix = prefix };
        do
        {
#pragma warning disable CA2012
            var page = _client.ListObjectsV2Async(req).GetAwaiter().GetResult();
#pragma warning restore CA2012
            foreach (var obj in page.S3Objects)
                yield return obj.Key;
            req.ContinuationToken = page.NextContinuationToken;
        } while (req.ContinuationToken != null);
    }

    public long Length(string key)
    {
        ValidateKey(key);
        var req = new GetObjectMetadataRequest { BucketName = Bucket, Key = key };
#pragma warning disable CA2012
        var meta = _client.GetObjectMetadataAsync(req).GetAwaiter().GetResult();
#pragma warning restore CA2012
        return meta.ContentLength;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Delete / Move
    // ─────────────────────────────────────────────────────────────────────────

    public bool Delete(string key)
    {
        ValidateKey(key);
        if (!Exists(key)) return false;
        var req = new DeleteObjectRequest { BucketName = Bucket, Key = key };
#pragma warning disable CA2012
        _client.DeleteObjectAsync(req).GetAwaiter().GetResult();
#pragma warning restore CA2012
        return true;
    }

    public void Move(string sourceKey, string destKey, bool overwrite)
    {
        ValidateKey(sourceKey);
        ValidateKey(destKey);
        if (!overwrite && Exists(destKey)) return;
        // S3 copy + delete.
        var copy = new CopyObjectRequest
        {
            SourceBucket      = Bucket,
            SourceKey         = sourceKey,
            DestinationBucket = Bucket,
            DestinationKey    = destKey,
        };
#pragma warning disable CA2012
        _client.CopyObjectAsync(copy).GetAwaiter().GetResult();
#pragma warning restore CA2012
        Delete(sourceKey);
    }

    public int DeletePrefix(string prefix)
    {
        ValidateKey(prefix);
        var list = new ListObjectsV2Request { BucketName = Bucket, Prefix = prefix };
        var count = 0;
        do
        {
#pragma warning disable CA2012
            var page = _client.ListObjectsV2Async(list).GetAwaiter().GetResult();
#pragma warning restore CA2012
            foreach (var obj in page.S3Objects)
            {
                var del = new DeleteObjectRequest { BucketName = Bucket, Key = obj.Key };
#pragma warning disable CA2012
                _client.DeleteObjectAsync(del).GetAwaiter().GetResult();
#pragma warning restore CA2012
                count++;
            }
            list.ContinuationToken = page.NextContinuationToken;
        } while (list.ContinuationToken != null);
        return count;
    }

    public string? TryResolveLocalPath(string key) => null; // S3 has no local path

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Storage key is required.");
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await ValueTask.CompletedTask;
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

    // ─────────────────────────────────────────────────────────────────────────
    // Deferred-write stream helper (buffers until flushed to S3)
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class S3DeferredWriteStream(S3CompatibleFileStorage owner, string key) : MemoryStream
    {
        private bool _uploaded;

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_uploaded)
            {
                _uploaded = true;
                Position  = 0;
                owner.WriteAsync(key, this, CancellationToken.None).GetAwaiter().GetResult();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_uploaded)
            {
                _uploaded = true;
                Position  = 0;
                await owner.WriteAsync(key, this, CancellationToken.None);
            }
            await base.DisposeAsync();
        }
    }
}
