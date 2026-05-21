using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// In-memory <see cref="IFileStorage"/> used by Phase 7 compliance tests
/// so the retention worker has something it can actually delete without
/// touching disk. Reuses a dictionary keyed by storage path.
/// </summary>
internal sealed class StubFileStorage : IFileStorage
{
    private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

    public void AddBlob(string key, byte[] body) => _blobs[key] = body;

    public Task<long> WriteAsync(string key, Stream source, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        source.CopyTo(ms);
        var bytes = ms.ToArray();
        _blobs[key] = bytes;
        return Task.FromResult((long)bytes.Length);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        if (!_blobs.TryGetValue(key, out var bytes))
        {
            throw new FileNotFoundException(key);
        }
        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public Task<Stream> OpenWriteAsync(string key, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        _blobs[key] = Array.Empty<byte>();
        // Simulated write — we discard the data on dispose since tests don't read it back.
        return Task.FromResult<Stream>(buffer);
    }

    public bool Exists(string key) => _blobs.ContainsKey(key);

    public bool Delete(string key) => _blobs.Remove(key);

    public long Length(string key) => _blobs.TryGetValue(key, out var b) ? b.LongLength : 0L;

    public void Move(string sourceKey, string destKey, bool overwrite)
    {
        if (!_blobs.TryGetValue(sourceKey, out var bytes))
        {
            throw new FileNotFoundException(sourceKey);
        }
        if (_blobs.ContainsKey(destKey) && !overwrite)
        {
            return;
        }
        _blobs[destKey] = bytes;
        _blobs.Remove(sourceKey);
    }

    public int DeletePrefix(string prefix)
    {
        var matching = _blobs.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        foreach (var key in matching)
        {
            _blobs.Remove(key);
        }
        return matching.Count;
    }

    public string? TryResolveLocalPath(string key) => null;
}

/// <summary>
/// Synthetic <see cref="IServiceScopeFactory"/> that hands back the same
/// <see cref="LearnerDbContext"/> + <see cref="IFileStorage"/> +
/// <see cref="SpeakingComplianceOptions"/> on every <c>CreateScope</c>
/// call. Lets us drive <see cref="OetLearner.Api.Services.Speaking.SpeakingAudioRetentionWorker"/>
/// without standing up the full DI container in tests.
/// </summary>
internal sealed class SingleInstanceScopeFactory : IServiceScopeFactory, IServiceProvider, IServiceScope
{
    private readonly LearnerDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IOptions<SpeakingComplianceOptions> _options;

    public SingleInstanceScopeFactory(
        LearnerDbContext db,
        IFileStorage storage,
        IOptions<SpeakingComplianceOptions> options)
    {
        _db = db;
        _storage = storage;
        _options = options;
    }

    public IServiceScope CreateScope() => this;

    public IServiceProvider ServiceProvider => this;

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(LearnerDbContext)) return _db;
        if (serviceType == typeof(IFileStorage)) return _storage;
        if (serviceType == typeof(IOptions<SpeakingComplianceOptions>)) return _options;
        return null;
    }

    public void Dispose()
    {
        // The shared LearnerDbContext is owned by the outer test fixture.
    }
}
