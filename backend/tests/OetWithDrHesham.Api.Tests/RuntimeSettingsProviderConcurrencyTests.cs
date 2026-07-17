using System.Data.Common;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.AiAssistant;
using OetWithDrHesham.Api.Services.AiTools;
using OetWithDrHesham.Api.Services.Settings;
using OetWithDrHesham.Api.Services.Writing.Configuration;

namespace OetWithDrHesham.Api.Tests;

public sealed class RuntimeSettingsProviderConcurrencyTests
{
    [Fact]
    public async Task GetAsync_OneHundredConcurrentMisses_IssueOneDatabaseLoad()
    {
        var entered = NewSignal();
        var release = NewSignal();
        var interceptor = new RuntimeSettingsQueryInterceptor(async (_, ct) =>
        {
            entered.TrySetResult(true);
            await release.Task.WaitAsync(ct);
        });
        await using var fixture = await RuntimeSettingsFixture.CreateAsync(interceptor);

        var start = NewSignal();
        var reads = Enumerable.Range(0, 100)
            .Select(async _ =>
            {
                await start.Task;
                return await fixture.Provider.GetAsync();
            })
            .ToArray();

        start.TrySetResult(true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await Task.Delay(100);
            Assert.Equal(1, interceptor.RuntimeSettingsReads);
        }
        finally
        {
            release.TrySetResult(true);
        }

        var snapshots = await Task.WhenAll(reads);
        Assert.All(snapshots, snapshot => Assert.Same(snapshots[0], snapshot));
        Assert.Equal(1, interceptor.RuntimeSettingsReads);
    }

    [Fact]
    public async Task GetAsync_CanceledWaiter_DoesNotCancelOrDuplicateSharedLoad()
    {
        var entered = NewSignal();
        var release = NewSignal();
        var interceptor = new RuntimeSettingsQueryInterceptor(async (_, ct) =>
        {
            entered.TrySetResult(true);
            await release.Task.WaitAsync(ct);
        });
        await using var fixture = await RuntimeSettingsFixture.CreateAsync(interceptor);

        var primary = fixture.Provider.GetAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var cts = new CancellationTokenSource();
        var canceledWaiter = fixture.Provider.GetAsync(cts.Token);
        cts.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await canceledWaiter);
            Assert.False(primary.IsCompleted);
            Assert.Equal(1, interceptor.RuntimeSettingsReads);
        }
        finally
        {
            release.TrySetResult(true);
        }

        await primary;
        await fixture.Provider.GetAsync();
        Assert.Equal(1, interceptor.RuntimeSettingsReads);
    }

    [Fact]
    public async Task GetAsync_FailedLoad_IsRemovedSoNextCallRetries()
    {
        var interceptor = new RuntimeSettingsQueryInterceptor((read, _) =>
            read == 1
                ? Task.FromException(new InvalidOperationException("simulated settings load failure"))
                : Task.CompletedTask);
        await using var fixture = await RuntimeSettingsFixture.CreateAsync(interceptor);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Provider.GetAsync());

        var recovered = await fixture.Provider.GetAsync();

        Assert.NotNull(recovered);
        Assert.Equal(2, interceptor.RuntimeSettingsReads);
    }

    [Fact]
    public async Task Invalidate_NextReadPublishesRuntimeUpdateOverEnvironmentFallback()
    {
        await using var fixture = await RuntimeSettingsFixture.CreateAsync();
        await fixture.SetPublicWebBaseUrlAsync("https://db-v1.example");

        var first = await fixture.Provider.GetAsync();
        Assert.Equal("https://db-v1.example", first.Platform.PublicWebBaseUrl);
        Assert.Equal(
            "https://db-v1.example",
            fixture.Provider.CurrentSnapshot.Effective.Platform.PublicWebBaseUrl);

        await fixture.SetPublicWebBaseUrlAsync("https://db-v2.example");
        fixture.Provider.Invalidate();

        // Invalidation is I/O-free and retains a safe last-known view until an
        // asynchronous request refreshes it.
        Assert.Equal(
            "https://db-v1.example",
            fixture.Provider.CurrentSnapshot.Effective.Platform.PublicWebBaseUrl);

        var refreshed = await fixture.Provider.GetAsync();
        Assert.Equal("https://db-v2.example", refreshed.Platform.PublicWebBaseUrl);
        Assert.Equal(
            "https://db-v2.example",
            fixture.Provider.CurrentSnapshot.Effective.Platform.PublicWebBaseUrl);
    }

    [Fact]
    public async Task Invalidate_DelayedCacheHitCannotOverwriteNewerSnapshot()
    {
        var cache = new BlockingMemoryCache();
        await using var fixture = await RuntimeSettingsFixture.CreateAsync(cache: cache);
        await fixture.SetPublicWebBaseUrlAsync("https://db-v1.example");
        await fixture.Provider.GetAsync();

        cache.BlockNextHit();
        var delayedRead = Task.Run(() => fixture.Provider.GetAsync());
        await cache.HitObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            await fixture.SetPublicWebBaseUrlAsync("https://db-v2.example");
            fixture.Provider.Invalidate();
            var refreshed = await fixture.Provider.GetAsync();
            Assert.Equal("https://db-v2.example", refreshed.Platform.PublicWebBaseUrl);
        }
        finally
        {
            cache.ReleaseHit();
        }

        var delayedResult = await delayedRead;
        Assert.Equal("https://db-v2.example", delayedResult.Platform.PublicWebBaseUrl);
        Assert.Equal(
            "https://db-v2.example",
            fixture.Provider.CurrentSnapshot.Effective.Platform.PublicWebBaseUrl);
    }

    private static TaskCompletionSource<bool> NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class RuntimeSettingsQueryInterceptor(
        Func<int, CancellationToken, Task> beforeRead) : DbCommandInterceptor
    {
        private int _runtimeSettingsReads;

        public int RuntimeSettingsReads => Volatile.Read(ref _runtimeSettingsReads);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"RuntimeSettings\"", StringComparison.Ordinal))
            {
                var read = Interlocked.Increment(ref _runtimeSettingsReads);
                await beforeRead(read, cancellationToken);
            }

            return result;
        }
    }

    private sealed class RuntimeSettingsFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _anchor;
        private readonly ServiceProvider _services;
        private readonly IMemoryCache _cache;

        private RuntimeSettingsFixture(
            SqliteConnection anchor,
            ServiceProvider services,
            IMemoryCache cache,
            RuntimeSettingsProvider provider)
        {
            _anchor = anchor;
            _services = services;
            _cache = cache;
            Provider = provider;
        }

        public RuntimeSettingsProvider Provider { get; }

        public static async Task<RuntimeSettingsFixture> CreateAsync(
            DbCommandInterceptor? interceptor = null,
            IMemoryCache? cache = null)
        {
            var databaseName = $"runtime-settings-{Guid.NewGuid():N}";
            var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";
            var anchor = new SqliteConnection(connectionString);
            await anchor.OpenAsync();

            var services = new ServiceCollection();
            services.AddDbContext<LearnerDbContext>(options =>
            {
                options.UseSqlite(connectionString);
                if (interceptor is not null) options.AddInterceptors(interceptor);
            });
            services.AddDataProtection();
            var serviceProvider = services.BuildServiceProvider();

            await using (var scope = serviceProvider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
                await db.Database.EnsureCreatedAsync();
            }

            cache ??= new MemoryCache(new MemoryCacheOptions());
            var provider = BuildProvider(serviceProvider, cache);
            return new RuntimeSettingsFixture(anchor, serviceProvider, cache, provider);
        }

        public async Task SetPublicWebBaseUrlAsync(string url)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var row = await db.RuntimeSettings.SingleOrDefaultAsync(r => r.Id == "default");
            if (row is null)
            {
                row = new RuntimeSettingsRow { Id = "default" };
                db.RuntimeSettings.Add(row);
            }

            row.PublicWebBaseUrl = url;
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _cache.Dispose();
            await _services.DisposeAsync();
            await _anchor.DisposeAsync();
        }

        private static RuntimeSettingsProvider BuildProvider(
            IServiceProvider services,
            IMemoryCache cache)
            => new(
                services.GetRequiredService<IServiceScopeFactory>(),
                cache,
                services.GetRequiredService<IDataProtectionProvider>(),
                Options.Create(new BrevoOptions()),
                Options.Create(new BillingOptions()),
                Options.Create(new ExternalAuthOptions()),
                Options.Create(new UploadScannerOptions()),
                Options.Create(new WebPushOptions()),
                Options.Create(new ZoomOptions()),
                Options.Create(new SoketiOptions()),
                Options.Create(new DataRetentionOptions()),
                Options.Create(new ExpertAutoAssignmentOptions()),
                Options.Create(new PasswordPolicyOptions()),
                Options.Create(new AiAssistantOptions()),
                Options.Create(new AiProviderOptions()),
                Options.Create(new AiToolOptions()),
                Options.Create(new WritingV2Options()),
                Options.Create(new PlatformOptions
                {
                    PublicWebBaseUrl = "https://env.example",
                }),
                Options.Create(new TwilioOptions()),
                Options.Create(new WhatsAppOptions()),
                Options.Create(new FxOptions()),
                Options.Create(new StorageOptions()),
                Options.Create(new PdfExtractionOptions()),
                Options.Create(new PronunciationOptions()),
                Options.Create(new AuthTokenOptions()),
                new StaticOptionsMonitor<SmtpOptions>(new SmtpOptions()),
                new ConfigurationBuilder().Build(),
                new TestHostEnvironment());
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class BlockingMemoryCache : IMemoryCache
    {
        private readonly MemoryCache _inner = new(new MemoryCacheOptions());
        private readonly TaskCompletionSource<bool> _hitObserved = NewSignal();
        private readonly ManualResetEventSlim _release = new(initialState: false);
        private int _blockNextHit;

        public TaskCompletionSource<bool> HitObserved => _hitObserved;

        public void BlockNextHit() => Volatile.Write(ref _blockNextHit, 1);
        public void ReleaseHit() => _release.Set();

        public bool TryGetValue(object key, out object? value)
        {
            var found = _inner.TryGetValue(key, out value);
            if (found && Interlocked.Exchange(ref _blockNextHit, 0) == 1)
            {
                _hitObserved.TrySetResult(true);
                if (!_release.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("Timed out waiting to release the blocked cache hit.");
            }

            return found;
        }

        public ICacheEntry CreateEntry(object key) => _inner.CreateEntry(key);
        public void Remove(object key) => _inner.Remove(key);

        public void Dispose()
        {
            _release.Set();
            _release.Dispose();
            _inner.Dispose();
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "OetWithDrHesham.Api.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
