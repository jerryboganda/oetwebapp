using OetLearner.Api.Services.Content;

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
