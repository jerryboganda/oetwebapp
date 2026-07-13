using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests;

public sealed class RealContentFolderImporterStreamingTests
{
    [Fact]
    public async Task Stage_validates_stages_and_scans_one_large_entry_with_bounded_chunks()
    {
        await using var db = NewDb();
        var storage = new TrackingStorage();
        var scanner = new ReadingScanner();
        var importer = CreateImporter(db, storage, scanner);
        var payload = new byte[4 * 1024 * 1024];
        Random.Shared.NextBytes(payload);
        "%PDF-1.7\n"u8.CopyTo(payload);
        var zip = BuildZip(
            "Listening/Listening Sample 1/Listening Sample 1 Question-Paper.pdf",
            payload);

        var result = await importer.StageAsync(
            "admin-stream", zip, "large.zip", CancellationToken.None);

        Assert.True(zip.IsDisposed);
        Assert.Empty(result.Issues);
        Assert.Single(result.Proposals);
        Assert.Equal(payload.LongLength, scanner.BytesRead);
        Assert.Equal(1, scanner.ScanCalls);
        Assert.Equal(1, storage.OpenWriteCalls);
        Assert.Equal(1, storage.OpenReadCalls);
        Assert.InRange(storage.MaxWriteSize, 1, 81920);
        Assert.Single(storage.Keys);
        Assert.Equal(payload, storage.Read(storage.Keys.Single()));
    }

    [Fact]
    public async Task Stage_cancellation_during_scan_disposes_input_and_removes_quarantine()
    {
        await using var db = NewDb();
        var storage = new TrackingStorage();
        var scanner = new BlockingScanner();
        var importer = CreateImporter(db, storage, scanner);
        var payload = new byte[1024 * 1024];
        Random.Shared.NextBytes(payload);
        "%PDF-1.7\n"u8.CopyTo(payload);
        var zip = BuildZip(
            "Listening/Listening Sample 1/Listening Sample 1 Question-Paper.pdf",
            payload);
        using var cts = new CancellationTokenSource();

        var staging = importer.StageAsync(
            "admin-cancel", zip, "cancel.zip", cts.Token);
        await scanner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await staging);
        Assert.True(zip.IsDisposed);
        Assert.Empty(storage.Keys);
        Assert.True(storage.LastWriteStreamDisposed);
    }

    [Fact]
    public async Task Stage_rejects_traversal_before_creating_staged_files()
    {
        await using var db = NewDb();
        var storage = new TrackingStorage();
        var importer = CreateImporter(db, storage, new ReadingScanner());
        var zip = BuildZip("../evil.pdf", "%PDF-1.7\nbad"u8.ToArray());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            importer.StageAsync(
                "admin-path", zip, "unsafe.zip", CancellationToken.None));

        Assert.Contains("unsafe segment", exception.Message);
        Assert.Empty(storage.Keys);
        Assert.True(zip.IsDisposed);
    }

    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static RealContentFolderImporter CreateImporter(
        LearnerDbContext db,
        IFileStorage storage,
        IUploadScanner scanner)
        => new(
            db,
            storage,
            new MagicByteValidator(),
            scanner,
            Options.Create(new StorageOptions
            {
                ContentUpload = new ContentUploadOptions
                {
                    MaxZipCompressionRatio = 100,
                },
            }),
            readingStructure: null!,
            textExtraction: null!,
            NullLogger<RealContentFolderImporter>.Instance);

    private static TrackingZipStream BuildZip(string path, byte[] payload)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
                   buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
            using var entryStream = entry.Open();
            entryStream.Write(payload);
        }
        buffer.Position = 0;
        return new TrackingZipStream(buffer);
    }

    private sealed class ReadingScanner : IUploadScanner
    {
        public long BytesRead { get; private set; }
        public int ScanCalls { get; private set; }

        public async Task<(bool clean, string? reason)> ScanAsync(
            Stream stream,
            string filename,
            CancellationToken ct)
        {
            ScanCalls++;
            var buffer = new byte[32768];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                BytesRead += read;
            }
            return (true, null);
        }
    }

    private sealed class BlockingScanner : IUploadScanner
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<(bool clean, string? reason)> ScanAsync(
            Stream stream,
            string filename,
            CancellationToken ct)
        {
            var buffer = new byte[1024];
            _ = await stream.ReadAsync(buffer, ct);
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return (true, null);
        }
    }

    private sealed class TrackingStorage : IFileStorage
    {
        private readonly Dictionary<string, byte[]> _files =
            new(StringComparer.Ordinal);

        public int OpenWriteCalls { get; private set; }
        public int OpenReadCalls { get; private set; }
        public int MaxWriteSize { get; private set; }
        public bool LastWriteStreamDisposed { get; private set; }
        public IReadOnlyCollection<string> Keys => _files.Keys;

        public Task<Stream> OpenWriteAsync(
            string key,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            OpenWriteCalls++;
            return Task.FromResult<Stream>(new CapturingWriteStream(
                onWrite: size => MaxWriteSize = Math.Max(MaxWriteSize, size),
                onDispose: bytes =>
                {
                    _files[key] = bytes;
                    LastWriteStreamDisposed = true;
                }));
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            OpenReadCalls++;
            return Task.FromResult<Stream>(
                new MemoryStream(_files[key], writable: false));
        }

        public Task<long> WriteAsync(
            string key,
            Stream source,
            CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<bool> ExistsAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_files.ContainsKey(key));
        }

        public Task<bool> DeleteAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_files.Remove(key));
        }

        public Task<long> LengthAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_files[key].LongLength);
        }

        public Task MoveAsync(string sourceKey, string destKey, bool overwrite, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!_files.ContainsKey(destKey) || overwrite)
            {
                _files[destKey] = _files[sourceKey];
                _files.Remove(sourceKey);
            }

            return Task.CompletedTask;
        }

        public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var keys = _files.Keys
                .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();
            foreach (var key in keys)
            {
                _files.Remove(key);
            }
            return Task.FromResult(keys.Count);
        }

        public string? TryResolveLocalPath(string key) => null;
        public Uri? ResolveReadUrl(string key, TimeSpan ttl) => null;
        public byte[] Read(string key) => _files[key];
    }

    private sealed class CapturingWriteStream(
        Action<int> onWrite,
        Action<byte[]> onDispose) : MemoryStream
    {
        private bool _captured;

        public override void Write(byte[] buffer, int offset, int count)
        {
            onWrite(count);
            base.Write(buffer, offset, count);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            onWrite(buffer.Length);
            return base.WriteAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_captured)
            {
                _captured = true;
                onDispose(ToArray());
            }
            base.Dispose(disposing);
        }
    }

    private sealed class TrackingZipStream(Stream inner) : Stream
    {
        public bool IsDisposed { get; private set; }
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
            => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
            => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin)
            => inner.Seek(offset, origin);
        public override void Flush() => inner.Flush();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                IsDisposed = true;
                inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
