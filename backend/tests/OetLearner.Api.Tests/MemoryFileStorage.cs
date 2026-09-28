using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests;

internal sealed class MemoryFileStorage : IFileStorage
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public async Task<long> WriteAsync(string key, Stream source, CancellationToken ct)
    {
        await using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, ct);
        _files[key] = buffer.ToArray();
        return _files[key].LongLength;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
        => Task.FromResult<Stream>(new MemoryStream(_files[key], writable: false));

    public Task<Stream> OpenWriteAsync(string key, CancellationToken ct)
        => Task.FromResult<Stream>(new MemoryStream());

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
        return Task.FromResult(_files.TryGetValue(key, out var data) ? data.LongLength : 0);
    }

    public Task MoveAsync(string sourceKey, string destKey, bool overwrite, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_files.TryGetValue(sourceKey, out var data)) return Task.CompletedTask;
        if (!overwrite && _files.ContainsKey(destKey)) return Task.CompletedTask;
        _files[destKey] = data;
        _files.Remove(sourceKey);
        return Task.CompletedTask;
    }

    public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var keys = _files.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        foreach (var key in keys) _files.Remove(key);
        return Task.FromResult(keys.Count);
    }

    public string? TryResolveLocalPath(string key) => null;
    public Uri? ResolveReadUrl(string key, TimeSpan ttl) => null;
}
