using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using System.Security.Cryptography;
using System.Text;

namespace OetLearner.Api.Tests;

/// <summary>In-memory file storage for service tests — no disk required.</summary>
internal sealed class InMemoryFileStorage : IFileStorage
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public Task<long> WriteAsync(string key, Stream source, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        source.CopyTo(ms);
        _files[key] = ms.ToArray();
        return Task.FromResult((long)_files[key].Length);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        if (!_files.TryGetValue(key, out var bytes))
        {
            var normalizedKey = NormalizeWhitespace(key);
            var match = _files.FirstOrDefault(entry => NormalizeWhitespace(entry.Key) == normalizedKey);
            bytes = match.Value;
            if (bytes is null)
            {
                var prefix = key[..key.LastIndexOf('/')];
                var fileName = Path.GetFileName(key);
                match = _files.FirstOrDefault(entry =>
                    entry.Key.StartsWith(prefix, StringComparison.Ordinal)
                    && string.Equals(Path.GetFileName(entry.Key), fileName, StringComparison.Ordinal));
                if (match.Value is null)
                {
                    match = _files.FirstOrDefault(entry =>
                        string.Equals(Path.GetFileName(entry.Key), fileName, StringComparison.Ordinal));
                }
                bytes = match.Value ?? throw new KeyNotFoundException(
                    $"The given key '{key}' was not present in the dictionary. Available: {string.Join(" | ", _files.Keys.Order(StringComparer.Ordinal).Take(20))}");
            }
        }

        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public Task<Stream> OpenWriteAsync(string key, CancellationToken ct)
    {
        var ms = new CapturingStream(bytes => _files[key] = bytes);
        return Task.FromResult<Stream>(ms);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_files.ContainsKey(key));
    }

    public bool AnyKeyStartsWith(string prefix) => _files.Keys.Any(key => key.StartsWith(prefix, StringComparison.Ordinal));

    public Task<bool> DeleteAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_files.Remove(key));
    }

    public Task<long> LengthAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult((long)_files[key].Length);
    }

    public Task MoveAsync(string src, string dst, bool overwrite, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_files.ContainsKey(dst) && !overwrite) return Task.CompletedTask;
        _files[dst] = _files[src];
        return Task.CompletedTask;
    }

    public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var keys = _files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        foreach (var k in keys) _files.Remove(k);
        return Task.FromResult(keys.Count);
    }

    private static string NormalizeWhitespace(string value)
        => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    public string? TryResolveLocalPath(string key) => null;
    public Uri? ResolveReadUrl(string key, TimeSpan ttl)
        => string.IsNullOrWhiteSpace(key) ? null : new Uri($"/media/file/{key}", UriKind.Relative);

    private sealed class CapturingStream(Action<byte[]> onClose) : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            var bytes = ToArray();
            base.Dispose(disposing);
            if (disposing) onClose(bytes);
        }
    }
}

public class ChunkedUploadServiceTests
{
    private static (LearnerDbContext db, InMemoryFileStorage storage, ChunkedUploadService svc) Build(
        ContentUploadOptions? contentUploadOptions = null,
        IUploadScanner? scanner = null,
        IUploadContentValidator? validator = null)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var storage = new InMemoryFileStorage();
        var opts = Options.Create(new StorageOptions { LocalRootPath = "/tmp", ContentUpload = contentUploadOptions ?? new() });
        var svc = new ChunkedUploadService(db, storage, opts,
            validator, scanner,
            NullLogger<ChunkedUploadService>.Instance);
        return (db, storage, svc);
    }

    /// <summary>Configurable IUploadScanner for the scan-outcome tests.</summary>
    private sealed class StubScanner(bool clean, string? reason) : IUploadScanner
    {
        public Task<(bool clean, string? reason)> ScanAsync(Stream stream, string filename, CancellationToken ct)
            => Task.FromResult((clean, reason));
    }

    [Fact]
    public async Task Happy_path_writes_parts_and_dedups_on_replay()
    {
        var (db, storage, svc) = Build(new ContentUploadOptions { ChunkSizeBytes = 3 });
        var session = await svc.StartAsync(new ChunkedUploadStart(
            "admin-1", "Listening Sample 1 Audio.mp3", "audio/mpeg", 6,
            "Audio"), default);

        await svc.UploadPartAsync("admin-1", session.Id, 1, new MemoryStream(Encoding.ASCII.GetBytes("hel")), default);
        await svc.UploadPartAsync("admin-1", session.Id, 2, new MemoryStream(Encoding.ASCII.GetBytes("lo!")), default);

        var result = await svc.CompleteAsync("admin-1", session.Id, default);

        Assert.False(result.Deduplicated);
        Assert.Equal(6, result.SizeBytes);
        Assert.Equal(64, result.Sha256.Length); // hex sha256
        // published file exists
        Assert.True(await storage.ExistsAsync($"uploads/published/{result.Sha256[..2]}/{result.Sha256.Substring(2, 2)}/{result.Sha256}.mp3", CancellationToken.None));
        // staging is cleaned
        Assert.False(await storage.ExistsAsync($"uploads/staging/admin-1/{session.Id}/00001.bin", CancellationToken.None));

        var media = await db.MediaAssets.SingleAsync();
        Assert.Equal(result.Sha256, media.Sha256);
        Assert.Equal("audio", media.MediaKind);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Same_content_from_two_uploads_dedups_to_one_media_asset()
    {
        var (db, _, svc) = Build();
        var s1 = await svc.StartAsync(new ChunkedUploadStart("admin-1", "a.pdf", "application/pdf", 5, "QuestionPaper"), default);
        await svc.UploadPartAsync("admin-1", s1.Id, 1, new MemoryStream(Encoding.ASCII.GetBytes("hello")), default);
        var r1 = await svc.CompleteAsync("admin-1", s1.Id, default);

        var s2 = await svc.StartAsync(new ChunkedUploadStart("admin-1", "b.pdf", "application/pdf", 5, "QuestionPaper"), default);
        await svc.UploadPartAsync("admin-1", s2.Id, 1, new MemoryStream(Encoding.ASCII.GetBytes("hello")), default);
        var r2 = await svc.CompleteAsync("admin-1", s2.Id, default);

        Assert.False(r1.Deduplicated);
        Assert.True(r2.Deduplicated);
        Assert.Equal(r1.Sha256, r2.Sha256);
        Assert.Equal(r1.MediaAssetId, r2.MediaAssetId);

        var mediaCount = await db.MediaAssets.CountAsync();
        Assert.Equal(1, mediaCount);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UploadPart_rejects_part_numbers_beyond_declared_total()
    {
        var (db, storage, svc) = Build();
        var session = await svc.StartAsync(new ChunkedUploadStart("admin-1", "x.pdf", "application/pdf", 3, "QuestionPaper"), default);

        await Assert.ThrowsAsync<ApiException>(() =>
            svc.UploadPartAsync("admin-1", session.Id, 2, new MemoryStream(Encoding.ASCII.GetBytes("abc")), default));

        Assert.False(await storage.ExistsAsync($"uploads/staging/admin-1/{session.Id}/00002.bin", CancellationToken.None));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UploadPart_rejects_payloads_that_exceed_remaining_declared_bytes()
    {
        var (db, storage, svc) = Build();
        var session = await svc.StartAsync(new ChunkedUploadStart("admin-1", "x.pdf", "application/pdf", 5, "QuestionPaper"), default);

        await Assert.ThrowsAsync<ApiException>(() =>
            svc.UploadPartAsync("admin-1", session.Id, 1, new MemoryStream(Encoding.ASCII.GetBytes("toolong")), default));

        Assert.False(await storage.ExistsAsync($"uploads/staging/admin-1/{session.Id}/00001.bin", CancellationToken.None));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Complete_rejects_missing_declared_parts()
    {
        var (db, _, svc) = Build(new ContentUploadOptions { ChunkSizeBytes = 3 });
        var session = await svc.StartAsync(new ChunkedUploadStart("admin-1", "x.pdf", "application/pdf", 6, "QuestionPaper"), default);
        await svc.UploadPartAsync("admin-1", session.Id, 1, new MemoryStream(Encoding.ASCII.GetBytes("abc")), default);

        await Assert.ThrowsAsync<ApiException>(() => svc.CompleteAsync("admin-1", session.Id, default));

        await db.DisposeAsync();
    }

    [Fact]
    public async Task Start_rejects_oversize_by_role()
    {
        var (db, _, svc) = Build();
        await Assert.ThrowsAsync<ApiException>(() =>
            svc.StartAsync(new ChunkedUploadStart(
                "admin-1", "big.mp3", "audio/mpeg",
                200L * 1024 * 1024, // 200 MB > 150 MB audio cap
                "Audio"), default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Abort_cleans_staging_and_marks_session_aborted()
    {
        var (db, storage, svc) = Build();
        var s = await svc.StartAsync(new ChunkedUploadStart("admin-1", "x.pdf", "application/pdf", 3, "QuestionPaper"), default);
        await svc.UploadPartAsync("admin-1", s.Id, 1, new MemoryStream(Encoding.ASCII.GetBytes("abc")), default);
        await svc.AbortAsync("admin-1", s.Id, default);
        var reload = await db.AdminUploadSessions.FirstAsync();
        Assert.Equal(AdminUploadState.Aborted, reload.State);
        Assert.False(await storage.ExistsAsync($"uploads/staging/admin-1/{s.Id}/00001.bin", CancellationToken.None));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Complete_is_idempotent_on_replay()
    {
        var (db, _, svc) = Build();
        var s = await svc.StartAsync(new ChunkedUploadStart("admin-1", "x.pdf", "application/pdf", 3, "QuestionPaper"), default);
        await svc.UploadPartAsync("admin-1", s.Id, 1, new MemoryStream(Encoding.ASCII.GetBytes("xyz")), default);
        var first = await svc.CompleteAsync("admin-1", s.Id, default);
        var second = await svc.CompleteAsync("admin-1", s.Id, default);
        Assert.Equal(first.MediaAssetId, second.MediaAssetId);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.True(second.Deduplicated);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Other_admin_cannot_mutate_upload_session()
    {
        var (db, storage, svc) = Build();
        var s = await svc.StartAsync(new ChunkedUploadStart("admin-1", "x.pdf", "application/pdf", 3, "QuestionPaper"), default);

        await Assert.ThrowsAsync<ApiException>(() =>
            svc.UploadPartAsync("admin-2", s.Id, 1, new MemoryStream(Encoding.ASCII.GetBytes("abc")), default));
        await Assert.ThrowsAsync<ApiException>(() => svc.CompleteAsync("admin-2", s.Id, default));
        await svc.AbortAsync("admin-2", s.Id, default);

        var afterForeignAbort = await db.AdminUploadSessions.SingleAsync(x => x.Id == s.Id);
        Assert.Equal(AdminUploadState.Started, afterForeignAbort.State);
        Assert.False(storage.AnyKeyStartsWith($"uploads/staging/admin-1/{s.Id}"));

        await svc.UploadPartAsync("admin-1", s.Id, 1, new MemoryStream(Encoding.ASCII.GetBytes("abc")), default);
        await svc.AbortAsync("admin-1", s.Id, default);

        var afterOwnerAbort = await db.AdminUploadSessions.SingleAsync(x => x.Id == s.Id);
        Assert.Equal(AdminUploadState.Aborted, afterOwnerAbort.State);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Complete_with_clean_scanner_publishes_the_asset()
    {
        var (db, _, svc) = Build(new ContentUploadOptions { ChunkSizeBytes = 1024 },
            scanner: new StubScanner(clean: true, reason: null));
        var s = await svc.StartAsync(new ChunkedUploadStart("admin-1", "x.pdf", "application/pdf", 5, "QuestionPaper"), default);
        await svc.UploadPartAsync("admin-1", s.Id, 1, new MemoryStream(Encoding.ASCII.GetBytes("hello")), default);

        var result = await svc.CompleteAsync("admin-1", s.Id, default);

        Assert.False(result.Deduplicated);
        Assert.Equal(1, await db.MediaAssets.CountAsync());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Complete_when_scanner_detects_malware_rejects_with_400_and_purges_staging()
    {
        var (db, storage, svc) = Build(new ContentUploadOptions { ChunkSizeBytes = 1024 },
            scanner: new StubScanner(clean: false, reason: "stream: Win.Test.EICAR_HDB-1 FOUND"));
        var s = await svc.StartAsync(new ChunkedUploadStart("admin-1", "x.pdf", "application/pdf", 5, "QuestionPaper"), default);
        await svc.UploadPartAsync("admin-1", s.Id, 1, new MemoryStream(Encoding.ASCII.GetBytes("hello")), default);

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.CompleteAsync("admin-1", s.Id, default));

        // A real detection IS the file's fault: reject 400 and purge everything.
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("upload_quarantined", ex.ErrorCode);
        var session = await db.AdminUploadSessions.SingleAsync();
        Assert.Equal(AdminUploadState.Aborted, session.State);
        Assert.Null(session.MediaAssetId);
        Assert.False(storage.AnyKeyStartsWith($"uploads/staging/admin-1/{s.Id}"));
        Assert.Equal(0, await db.MediaAssets.CountAsync());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Complete_when_scanner_unavailable_returns_retryable_503_and_preserves_parts_for_retry()
    {
        var uploadOpts = new ContentUploadOptions { ChunkSizeBytes = 1024 };
        var (db, storage, svc) = Build(uploadOpts,
            scanner: new StubScanner(clean: false, reason: "scan_unreachable"));
        var s = await svc.StartAsync(new ChunkedUploadStart("admin-1", "x.pdf", "application/pdf", 5, "QuestionPaper"), default);
        await svc.UploadPartAsync("admin-1", s.Id, 1, new MemoryStream(Encoding.ASCII.GetBytes("hello")), default);

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.CompleteAsync("admin-1", s.Id, default));

        // A scanner outage is NOT the file's fault: retryable 503, session left
        // open with no media asset, and the staged part preserved.
        Assert.Equal(503, ex.StatusCode);
        Assert.Equal("scan_unavailable", ex.ErrorCode);
        Assert.True(ex.Retryable);
        var session = await db.AdminUploadSessions.SingleAsync();
        Assert.NotEqual(AdminUploadState.Aborted, session.State);
        Assert.Null(session.MediaAssetId);
        Assert.True(await storage.ExistsAsync($"uploads/staging/admin-1/{s.Id}/00001.bin", CancellationToken.None));
        Assert.Equal(0, await db.MediaAssets.CountAsync());

        // Once the scanner recovers, retrying the commit publishes the asset
        // from the preserved parts — proving they were not discarded.
        var recovered = new ChunkedUploadService(db, storage,
            Options.Create(new StorageOptions { LocalRootPath = "/tmp", ContentUpload = uploadOpts }),
            validator: null, scanner: new StubScanner(clean: true, reason: null),
            NullLogger<ChunkedUploadService>.Instance);
        var result = await recovered.CompleteAsync("admin-1", s.Id, default);
        Assert.False(result.Deduplicated);
        Assert.Equal(1, await db.MediaAssets.CountAsync());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Complete_awaits_part_opens_in_order_and_streams_with_a_bounded_buffer()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new LearnerDbContext(options);
        var storage = new TrackingFileStorage();
        var uploadOptions = new ContentUploadOptions { ChunkSizeBytes = 1024 * 1024 };
        var service = new ChunkedUploadService(
            db,
            storage,
            Options.Create(new StorageOptions
            {
                LocalRootPath = "/tmp",
                ContentUpload = uploadOptions,
            }),
            validator: null,
            scanner: null,
            NullLogger<ChunkedUploadService>.Instance);
        var first = Enumerable.Repeat((byte)0x11, 1024 * 1024).ToArray();
        var second = Enumerable.Repeat((byte)0x22, 1024 * 1024).ToArray();
        var session = await service.StartAsync(new ChunkedUploadStart(
            "admin-stream", "large.pdf", "application/pdf",
            first.LongLength + second.LongLength, "QuestionPaper"), default);
        await service.UploadPartAsync(
            "admin-stream", session.Id, 1, new MemoryStream(first), default);
        await service.UploadPartAsync(
            "admin-stream", session.Id, 2, new MemoryStream(second), default);

        using var expectedHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        expectedHasher.AppendData(first);
        expectedHasher.AppendData(second);
        var expectedSha = Convert.ToHexString(expectedHasher.GetHashAndReset())
            .ToLowerInvariant();

        var result = await service.CompleteAsync(
            "admin-stream", session.Id, default);

        Assert.Equal(expectedSha, result.Sha256);
        Assert.Equal(
            new[] { "00001.bin", "00002.bin" },
            storage.PartOpenKeys.Select(Path.GetFileName));
        Assert.All(storage.PartStreams, stream => Assert.True(stream.IsDisposed));
        Assert.All(
            storage.PartStreams,
            stream => Assert.InRange(stream.MaxReadRequest, 1, 81920));
    }

    [Fact]
    public async Task Complete_cancellation_while_opening_a_later_part_disposes_opened_streams()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new LearnerDbContext(options);
        var storage = new TrackingFileStorage { BlockOnPartOpen = 2 };
        var service = new ChunkedUploadService(
            db,
            storage,
            Options.Create(new StorageOptions
            {
                LocalRootPath = "/tmp",
                ContentUpload = new ContentUploadOptions { ChunkSizeBytes = 3 },
            }),
            validator: null,
            scanner: null,
            NullLogger<ChunkedUploadService>.Instance);
        var session = await service.StartAsync(new ChunkedUploadStart(
            "admin-cancel", "cancel.pdf", "application/pdf", 6,
            "QuestionPaper"), default);
        await service.UploadPartAsync(
            "admin-cancel", session.Id, 1,
            new MemoryStream(Encoding.ASCII.GetBytes("abc")), default);
        await service.UploadPartAsync(
            "admin-cancel", session.Id, 2,
            new MemoryStream(Encoding.ASCII.GetBytes("def")), default);
        using var cts = new CancellationTokenSource();

        var completing = service.CompleteAsync(
            "admin-cancel", session.Id, cts.Token);
        await storage.BlockedPartOpen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await completing);
        var opened = Assert.Single(storage.PartStreams);
        Assert.True(opened.IsDisposed);
    }

    private sealed class TrackingFileStorage : IFileStorage
    {
        private readonly InMemoryFileStorage _inner = new();
        private int _partOpenCount;

        public int BlockOnPartOpen { get; init; }
        public TaskCompletionSource BlockedPartOpen { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> PartOpenKeys { get; } = [];
        public List<TrackingReadStream> PartStreams { get; } = [];

        public Task<long> WriteAsync(string key, Stream source, CancellationToken ct)
            => _inner.WriteAsync(key, source, ct);

        public async Task<Stream> OpenReadAsync(string key, CancellationToken ct)
        {
            if (key.EndsWith(".bin", StringComparison.Ordinal))
            {
                await Task.Yield();
                PartOpenKeys.Add(key);
                var partOpen = Interlocked.Increment(ref _partOpenCount);
                if (partOpen == BlockOnPartOpen)
                {
                    BlockedPartOpen.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }

                var tracked = new TrackingReadStream(
                    await _inner.OpenReadAsync(key, ct));
                PartStreams.Add(tracked);
                return tracked;
            }

            return await _inner.OpenReadAsync(key, ct);
        }

        public Task<Stream> OpenWriteAsync(string key, CancellationToken ct)
            => _inner.OpenWriteAsync(key, ct);
        public Task<bool> ExistsAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return _inner.ExistsAsync(key, ct);
        }

        public Task<bool> DeleteAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return _inner.DeleteAsync(key, ct);
        }

        public Task<long> LengthAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return _inner.LengthAsync(key, ct);
        }

        public Task MoveAsync(
            string sourceKey,
            string destKey,
            bool overwrite,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return _inner.MoveAsync(sourceKey, destKey, overwrite, ct);
        }

        public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return _inner.DeletePrefixAsync(prefix, ct);
        }
        public string? TryResolveLocalPath(string key) => null;
        public Uri? ResolveReadUrl(string key, TimeSpan ttl)
            => _inner.ResolveReadUrl(key, ttl);
    }

    private sealed class TrackingReadStream(Stream inner) : Stream
    {
        public bool IsDisposed { get; private set; }
        public int MaxReadRequest { get; private set; }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            MaxReadRequest = Math.Max(MaxReadRequest, count);
            return inner.Read(buffer, offset, count);
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            MaxReadRequest = Math.Max(MaxReadRequest, buffer.Length);
            return await inner.ReadAsync(buffer, cancellationToken);
        }

        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin)
            => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                IsDisposed = true;
                inner.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            IsDisposed = true;
            await inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
