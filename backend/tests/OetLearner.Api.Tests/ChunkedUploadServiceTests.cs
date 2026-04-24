using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
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
        => Task.FromResult<Stream>(new MemoryStream(_files[key], writable: false));

    public Task<Stream> OpenWriteAsync(string key, CancellationToken ct)
    {
        var ms = new CapturingStream(bytes => _files[key] = bytes);
        return Task.FromResult<Stream>(ms);
    }

    public bool Exists(string key) => _files.ContainsKey(key);
    public bool AnyKeyStartsWith(string prefix) => _files.Keys.Any(key => key.StartsWith(prefix, StringComparison.Ordinal));
    public bool Delete(string key) => _files.Remove(key);
    public long Length(string key) => _files[key].Length;
    public void Move(string src, string dst, bool overwrite)
    {
        if (_files.ContainsKey(dst) && !overwrite) return;
        _files[dst] = _files[src];
        _files.Remove(src);
    }
    public int DeletePrefix(string prefix)
    {
        var keys = _files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        foreach (var k in keys) _files.Remove(k);
        return keys.Count;
    }
    public string? TryResolveLocalPath(string key) => null;

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
    private static (LearnerDbContext db, InMemoryFileStorage storage, ChunkedUploadService svc) Build(ContentUploadOptions? contentUploadOptions = null)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var storage = new InMemoryFileStorage();
        var opts = Options.Create(new StorageOptions { LocalRootPath = "/tmp", ContentUpload = contentUploadOptions ?? new() });
        var svc = new ChunkedUploadService(db, storage, opts,
            validator: null, scanner: null,
            NullLogger<ChunkedUploadService>.Instance);
        return (db, storage, svc);
    }

    [Fact]
    public async Task Happy_path_writes_parts_and_dedups_on_replay()
    {
        var (db, storage, svc) = Build(new ContentUploadOptions { ChunkSizeBytes = 3 });
        var session = await svc.StartAsync(new ChunkedUploadStart(
            "admin-1", "Listening Sample 1 Audio.mp3", "audio/mpeg", 6,
            "Audio"), default);

        await svc.UploadPartAsync(session.Id, 1, new MemoryStream(Encoding.ASCII.GetBytes("hel")), default);
        await svc.UploadPartAsync(session.Id, 2, new MemoryStream(Encoding.ASCII.GetBytes("lo!")), default);

        var result = await svc.CompleteAsync(session.Id, default);

        Assert.False(result.Deduplicated);
        Assert.Equal(6, result.SizeBytes);
        Assert.Equal(64, result.Sha256.Length); // hex sha256
        // published file exists
        Assert.True(storage.Exists($"uploads/published/{result.Sha256[..2]}/{result.Sha256.Substring(2, 2)}/{result.Sha256}.mp3"));
        // staging is cleaned
        Assert.False(storage.Exists($"uploads/staging/admin-1/{session.Id}/00001.bin"));

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
        await svc.UploadPartAsync(s1.Id, 1, new MemoryStream(Encoding.ASCII.GetBytes("hello")), default);
        var r1 = await svc.CompleteAsync(s1.Id, default);

        var s2 = await svc.StartAsync(new ChunkedUploadStart("admin-1", "b.pdf", "application/pdf", 5, "QuestionPaper"), default);
        await svc.UploadPartAsync(s2.Id, 1, new MemoryStream(Encoding.ASCII.GetBytes("hello")), default);
        var r2 = await svc.CompleteAsync(s2.Id, default);

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

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UploadPartAsync(session.Id, 2, new MemoryStream(Encoding.ASCII.GetBytes("abc")), default));

        Assert.False(storage.Exists($"uploads/staging/admin-1/{session.Id}/00002.bin"));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UploadPart_rejects_payloads_that_exceed_remaining_declared_bytes()
    {
        var (db, storage, svc) = Build();
        var session = await svc.StartAsync(new ChunkedUploadStart("admin-1", "x.pdf", "application/pdf", 5, "QuestionPaper"), default);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UploadPartAsync(session.Id, 1, new MemoryStream(Encoding.ASCII.GetBytes("toolong")), default));

        Assert.False(storage.Exists($"uploads/staging/admin-1/{session.Id}/00001.bin"));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Complete_rejects_missing_declared_parts()
    {
        var (db, _, svc) = Build(new ContentUploadOptions { ChunkSizeBytes = 3 });
        var session = await svc.StartAsync(new ChunkedUploadStart("admin-1", "x.pdf", "application/pdf", 6, "QuestionPaper"), default);
        await svc.UploadPartAsync(session.Id, 1, new MemoryStream(Encoding.ASCII.GetBytes("abc")), default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CompleteAsync(session.Id, default));

        await db.DisposeAsync();
    }

    [Fact]
    public async Task Start_rejects_oversize_by_role()
    {
        var (db, _, svc) = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
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
        await svc.UploadPartAsync(s.Id, 1, new MemoryStream(Encoding.ASCII.GetBytes("abc")), default);
        await svc.AbortAsync(s.Id, default);
        var reload = await db.AdminUploadSessions.FirstAsync();
        Assert.Equal(AdminUploadState.Aborted, reload.State);
        Assert.False(storage.Exists($"uploads/staging/admin-1/{s.Id}/00001.bin"));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Complete_is_idempotent_on_replay()
    {
        var (db, _, svc) = Build();
        var s = await svc.StartAsync(new ChunkedUploadStart("admin-1", "x.pdf", "application/pdf", 3, "QuestionPaper"), default);
        await svc.UploadPartAsync(s.Id, 1, new MemoryStream(Encoding.ASCII.GetBytes("xyz")), default);
        var first = await svc.CompleteAsync(s.Id, default);
        var second = await svc.CompleteAsync(s.Id, default);
        Assert.Equal(first.MediaAssetId, second.MediaAssetId);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.True(second.Deduplicated);
        await db.DisposeAsync();
    }
}
