using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Listening;
using System.Security.Cryptography;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// Listening Module — Wave 4 of the OET Listening gap-fill plan. Exercises
/// <c>ListeningTtsService</c> end-to-end with the stub synthesis provider:
/// transcript segments → WAV blob → IFileStorage write → SHA-256 + audit row.
/// </summary>
public class ListeningTtsServiceTests
{
    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static (LearnerDbContext db, InMemoryStorage storage, ListeningTtsService svc) NewServiceStack()
    {
        var db = NewDb();
        var storage = new InMemoryStorage();
        var provider = new StubListeningTtsSynthesisProvider();
        var svc = new ListeningTtsService(db, storage, provider, NullLogger<ListeningTtsService>.Instance);
        return (db, storage, svc);
    }

    private static ListeningExtract SeedExtract(LearnerDbContext db, string segmentsJson)
    {
        var now = DateTimeOffset.UtcNow;
        db.ListeningParts.Add(new ListeningPart
        {
            Id = "part-tts",
            PaperId = "paper-tts",
            PartCode = ListeningPartCode.A1,
            MaxRawScore = 0,
            CreatedAt = now,
            UpdatedAt = now,
        });
        var extract = new ListeningExtract
        {
            Id = "extract-tts",
            ListeningPartId = "part-tts",
            DisplayOrder = 0,
            Kind = ListeningExtractKind.Consultation,
            Title = "E",
            SpeakersJson = "[]",
            TranscriptSegmentsJson = segmentsJson,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ListeningExtracts.Add(extract);
        db.SaveChanges();
        return extract;
    }

    [Fact]
    public async Task SynthesizeAsync_writes_wav_blob_and_audit_event()
    {
        var (db, storage, svc) = NewServiceStack();
        SeedExtract(db, """
            [
              { "startMs": 0,    "endMs": 1500, "speakerId": "s1", "text": "Good morning, doctor." },
              { "startMs": 1500, "endMs": 3500, "speakerId": "s2", "text": "Morning, please come in." }
            ]
        """);

        var result = await svc.SynthesizeAsync("extract-tts", adminId: "admin-1", CancellationToken.None);

        Assert.Equal("extract-tts", result.ExtractId);
        Assert.True(result.ByteLength > 44, "WAV blob must include at least the 44-byte header.");
        Assert.Equal(2, result.SegmentCount);
        Assert.True(result.TotalDurationMs > 0);

        // SHA-256 hex is 64 chars and the storage key follows the
        // content-addressed shard layout.
        Assert.Matches("^[0-9a-f]{64}$", result.Sha256);
        Assert.Contains($"/{result.Sha256}.wav", result.AudioStorageKey);
        Assert.StartsWith("listening/tts/", result.AudioStorageKey);

        var written = storage.Read(result.AudioStorageKey);
        Assert.NotNull(written);
        // RIFF header magic.
        Assert.Equal((byte)'R', written![0]);
        Assert.Equal((byte)'I', written[1]);
        Assert.Equal((byte)'F', written[2]);
        Assert.Equal((byte)'F', written[3]);

        var audit = await db.AuditEvents
            .Where(a => a.Action == "listening.tts.synthesize" && a.ResourceId == "extract-tts")
            .ToListAsync();
        Assert.Single(audit);
        Assert.Equal("admin-1", audit[0].ActorId);
    }

    [Fact]
    public async Task SynthesizeAsync_throws_validation_when_no_segments_authored()
    {
        var (db, _, svc) = NewServiceStack();
        SeedExtract(db, "[]");

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.SynthesizeAsync("extract-tts", "admin-1", CancellationToken.None));
        Assert.Equal("listening_tts_no_segments", ex.ErrorCode);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task SynthesizeAsync_throws_not_found_for_missing_extract()
    {
        var (_, _, svc) = NewServiceStack();
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.SynthesizeAsync("missing-extract", "admin-1", CancellationToken.None));
        Assert.Equal("listening_extract_not_found", ex.ErrorCode);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task SynthesizeAsync_is_idempotent_on_identical_segments()
    {
        var (db, storage, svc) = NewServiceStack();
        SeedExtract(db, """
            [{ "startMs": 0, "endMs": 1000, "speakerId": "s1", "text": "Hello." }]
        """);

        var first  = await svc.SynthesizeAsync("extract-tts", "admin-1", CancellationToken.None);
        var second = await svc.SynthesizeAsync("extract-tts", "admin-1", CancellationToken.None);

        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(first.AudioStorageKey, second.AudioStorageKey);
        Assert.Equal(1, storage.WriteCount); // second call hits storage.ExistsAsync() short-circuit
    }

    [Fact]
    public async Task SynthesizeAsync_rewinds_one_assembly_stream_for_storage_and_owns_its_lifetime()
    {
        var (db, storage, svc) = NewServiceStack();
        SeedExtract(db, """
            [{ "startMs": 0, "endMs": 1000, "speakerId": "s1", "text": "Hello doctor." }]
        """);

        var result = await svc.SynthesizeAsync(
            "extract-tts", "admin-1", CancellationToken.None);

        var source = Assert.IsType<MemoryStream>(storage.LastWriteSource);
        Assert.Equal(0, storage.LastWriteStartPosition);
        Assert.Throws<ObjectDisposedException>(() => _ = source.Position);
        var stored = Assert.IsType<byte[]>(storage.Read(result.AudioStorageKey));
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(stored)).ToLowerInvariant(),
            result.Sha256);
        Assert.Equal(stored.LongLength, result.ByteLength);
    }

    [Fact]
    public async Task AppendSilenceAsync_writes_large_gaps_in_bounded_chunks()
    {
        var destination = new CountingWriteStream();

        var written = await ListeningTtsService.AppendSilenceAsync(
            destination, ms: 5 * 60 * 1000, sampleRate: 16_000,
            CancellationToken.None);

        Assert.Equal(9_600_000, written);
        Assert.Equal(written, destination.BytesWritten);
        Assert.InRange(destination.MaxWriteSize, 1, 81920);
    }

    [Fact]
    public async Task AppendSilenceAsync_propagates_cancellation_without_one_gap_allocation()
    {
        using var cts = new CancellationTokenSource();
        var destination = new CountingWriteStream(() => cts.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ListeningTtsService.AppendSilenceAsync(
                destination, ms: 5 * 60 * 1000, sampleRate: 16_000,
                cts.Token));

        Assert.Equal(81920, destination.BytesWritten);
        Assert.Equal(81920, destination.MaxWriteSize);
    }

    /// <summary>
    /// Hand-rolled in-memory IFileStorage stub. Kept local to this test file
    /// so it can also assert a write-count for the idempotency case.
    /// </summary>
    private sealed class InMemoryStorage : IFileStorage
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
        public int WriteCount { get; private set; }
        public Stream? LastWriteSource { get; private set; }
        public long? LastWriteStartPosition { get; private set; }

        public async Task<long> WriteAsync(string key, Stream source, CancellationToken ct)
        {
            LastWriteSource = source;
            LastWriteStartPosition = source.CanSeek ? source.Position : null;
            using var ms = new MemoryStream();
            await source.CopyToAsync(ms, ct);
            _files[key] = ms.ToArray();
            WriteCount++;
            return _files[key].Length;
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
            => Task.FromResult<Stream>(new MemoryStream(_files[key], writable: false));

        public Task<Stream> OpenWriteAsync(string key, CancellationToken ct)
        {
            var buffer = new MemoryStream();
            WriteCount++;
            _files[key] = buffer.ToArray();
            return Task.FromResult<Stream>(buffer);
        }

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
            return Task.FromResult(_files.TryGetValue(key, out var b) ? b.LongLength : 0);
        }

        public Task MoveAsync(string sourceKey, string destKey, bool overwrite, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_files.TryGetValue(sourceKey, out var bytes))
            {
                _files[destKey] = bytes;
                _files.Remove(sourceKey);
            }

            return Task.CompletedTask;
        }

        public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var keys = _files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            foreach (var k in keys) _files.Remove(k);
            return Task.FromResult(keys.Count);
        }
        public string? TryResolveLocalPath(string key) => null;
        public Uri? ResolveReadUrl(string key, TimeSpan ttl)
            => string.IsNullOrWhiteSpace(key) ? null : new Uri($"/media/file/{key}", UriKind.Relative);

        public byte[]? Read(string key) => _files.TryGetValue(key, out var b) ? b : null;
    }

    private sealed class CountingWriteStream(Action? afterFirstWrite = null) : Stream
    {
        private bool _wrote;
        public long BytesWritten { get; private set; }
        public int MaxWriteSize { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => BytesWritten;
        public override long Position
        {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BytesWritten += buffer.Length;
            MaxWriteSize = Math.Max(MaxWriteSize, buffer.Length);
            if (!_wrote)
            {
                _wrote = true;
                afterFirstWrite?.Invoke();
            }
            return ValueTask.CompletedTask;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            BytesWritten += count;
            MaxWriteSize = Math.Max(MaxWriteSize, count);
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
    }
}
