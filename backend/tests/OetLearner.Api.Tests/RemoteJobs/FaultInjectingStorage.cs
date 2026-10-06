using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>
/// An <see cref="IFileStorage"/> that delegates to another one and throws what <see cref="Fault"/> returns for an
/// (operation, key) pair, so a test can make one provider failure (missing object, I/O error, S3 404) happen on demand.
/// Operations: <c>write</c>, <c>read</c>, <c>open-write</c>, <c>exists</c>, <c>delete</c>, <c>length</c>, <c>move</c>.
/// </summary>
internal sealed class FaultInjectingStorage(IFileStorage inner) : IFileStorage
{
    /// <summary>Returns the exception to throw for the operation on the key, or null to let the call through.</summary>
    public Func<string, string, Exception?>? Fault { get; set; }

    private void Check(string operation, string key)
    {
        if (Fault?.Invoke(operation, key) is { } exception) throw exception;
    }

    public Task<long> WriteAsync(string key, Stream source, CancellationToken ct)
    {
        Check("write", key);
        return inner.WriteAsync(key, source, ct);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        Check("read", key);
        return inner.OpenReadAsync(key, ct);
    }

    public Task<Stream> OpenWriteAsync(string key, CancellationToken ct)
    {
        Check("open-write", key);
        return inner.OpenWriteAsync(key, ct);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        Check("exists", key);
        return inner.ExistsAsync(key, ct);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken ct)
    {
        Check("delete", key);
        return inner.DeleteAsync(key, ct);
    }

    public Task<long> LengthAsync(string key, CancellationToken ct)
    {
        Check("length", key);
        return inner.LengthAsync(key, ct);
    }

    /// <summary>
    /// The shared in-memory test double copies on <c>MoveAsync</c> and leaves the source behind; every real provider removes it.
    /// Set this to get the real behaviour.
    /// </summary>
    public bool MoveRemovesSource { get; set; }

    public async Task MoveAsync(string sourceKey, string destKey, bool overwrite, CancellationToken ct)
    {
        Check("move", sourceKey);
        await inner.MoveAsync(sourceKey, destKey, overwrite, ct);
        if (MoveRemovesSource) await inner.DeleteAsync(sourceKey, ct);
    }

    public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct) => inner.DeletePrefixAsync(prefix, ct);

    public string? TryResolveLocalPath(string key) => inner.TryResolveLocalPath(key);

    public Uri? ResolveReadUrl(string key, TimeSpan ttl) => inner.ResolveReadUrl(key, ttl);
}
