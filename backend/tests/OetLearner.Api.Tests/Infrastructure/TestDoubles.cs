using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Indexing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Services.Writing.Events;

namespace OetLearner.Api.Tests.Infrastructure;

// Small test doubles that used to be copy-pasted as identical private nested
// classes across many test files. A private nested class with the same name
// still shadows these, so a file that needs a different behaviour keeps its own.

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _utcNow = start;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
}

internal sealed class TestHostEnvironment(string contentRootPath) : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    public string WebRootPath { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = contentRootPath;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
}

/// <summary>
/// Captures the previous value of <c>Auth__UseDevelopmentAuth</c>,
/// flips it on, and restores it on dispose. This mirrors the
/// save/restore pattern used by AdminFlowsTests so the env var
/// cannot leak into other test collections that expect production
/// auth behavior.
/// </summary>
internal sealed class DevAuthEnv : IDisposable
{
    private const string Key = "Auth__UseDevelopmentAuth";
    private readonly string? _previous;
    private DevAuthEnv()
    {
        _previous = Environment.GetEnvironmentVariable(Key);
        Environment.SetEnvironmentVariable(Key, "true");
    }
    public static DevAuthEnv Enable() => new();
    public void Dispose() => Environment.SetEnvironmentVariable(Key, _previous);
}

// No calendar connection is ever seeded, so this factory must never be called.
internal sealed class ThrowingHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
        => throw new InvalidOperationException("HTTP client should not be used without a calendar connection.");
}

/// <summary>Accepts every call; ExistsAsync is always true and read URLs point at /test-media/.</summary>
internal sealed class TestFileStorage : IFileStorage
{
    public Task<long> WriteAsync(string key, Stream source, CancellationToken ct) => Task.FromResult(0L);
    public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream());
    public Task<Stream> OpenWriteAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream());
    public Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    public Task<long> LengthAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(0L);
    }

    public Task MoveAsync(string sourceKey, string destKey, bool overwrite, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(0);
    }
    public string? TryResolveLocalPath(string key) => null;
    public Uri? ResolveReadUrl(string key, TimeSpan ttl) => new($"/test-media/{Uri.EscapeDataString(key)}", UriKind.Relative);
}

internal sealed class AllowAllContentEntitlementService : IContentEntitlementService
{
    public Task<ContentEntitlementResult> AllowAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
        => Task.FromResult(new ContentEntitlementResult(true, "test", "premium", null));

    public Task RequireAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
        => Task.CompletedTask;

    public bool IsAdmin(System.Security.Claims.ClaimsPrincipal? principal) => false;
}

/// <summary>SQLite has no pgvector, so the vector path must never be reached.</summary>
internal sealed class UnusedEmbeddings : IEmbeddingService
{
    public Task<float[]> EmbedAsync(string text, CancellationToken ct) =>
        throw new InvalidOperationException("vector path must not run on SQLite");

    public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
        throw new InvalidOperationException("vector path must not run on SQLite");
}

internal sealed class NoopWritingEventBus : IWritingEventBus
{
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : WritingEvent
        => Task.CompletedTask;
}

internal sealed class EmptyCanonEngine : IWritingCanonEngine
{
    public Task<WritingCanonDetectionResult> DetectViolationsAsync(WritingCanonDetectionRequest request, CancellationToken ct)
        => Task.FromResult(new WritingCanonDetectionResult(request.SubmissionId, Array.Empty<WritingCanonViolation>()));

    public Task<WritingCanonRuleTestResponse?> TestRuleAsync(string adminUserId, string ruleId, WritingCanonRuleTestRequest request, CancellationToken ct)
        => throw new NotImplementedException();
}
