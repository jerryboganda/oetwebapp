using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;

namespace OetLearner.Api.Tests.Services;

public sealed class AiCircuitBreakerTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly AiCircuitBreakerStore _store;

    public AiCircuitBreakerTests()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<LearnerDbContext>(o =>
            o.UseInMemoryDatabase(dbName)
             .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        _provider = services.BuildServiceProvider(validateScopes: true);
        _store = new AiCircuitBreakerStore(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AiCircuitBreakerStore>.Instance);
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task AllowAsync_AllowsUnknownKey()
    {
        Assert.True(await _store.AllowAsync(AiCircuitBreakerStore.KindProvider, "anthropic", default));
    }

    [Fact]
    public async Task CredentialFailure_OpensImmediately()
    {
        await _store.RecordFailureAsync(AiCircuitBreakerStore.KindCredential, "key-1", "401", default);

        Assert.False(await _store.AllowAsync(AiCircuitBreakerStore.KindCredential, "key-1", default));

        using var scope = _provider.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<LearnerDbContext>()
            .AiCircuitStates.AsNoTracking()
            .SingleAsync(s => s.Key == "key-1");
        Assert.Equal(AiCircuitBreakerStore.StateOpen, row.State);
        Assert.True(row.OpenUntil > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Provider401_OpensImmediatelyEvenOnProviderKind()
    {
        await _store.RecordFailureAsync(AiCircuitBreakerStore.KindProvider, "openai", "401", default);
        Assert.False(await _store.AllowAsync(AiCircuitBreakerStore.KindProvider, "openai", default));
    }

    [Fact]
    public async Task Provider_OpensAfterFiveFailuresInWindow()
    {
        for (var i = 0; i < 4; i++)
        {
            await _store.RecordFailureAsync(AiCircuitBreakerStore.KindProvider, "mistral", "500", default);
            Assert.True(await _store.AllowAsync(AiCircuitBreakerStore.KindProvider, "mistral", default));
        }

        await _store.RecordFailureAsync(AiCircuitBreakerStore.KindProvider, "mistral", "500", default);
        Assert.False(await _store.AllowAsync(AiCircuitBreakerStore.KindProvider, "mistral", default));
    }

    [Fact]
    public async Task OpenUntilElapsed_AllowsExactlyOneProbe()
    {
        await _store.RecordFailureAsync(AiCircuitBreakerStore.KindCredential, "probe-key", "403", default);

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var row = await db.AiCircuitStates.SingleAsync(s => s.Key == "probe-key");
            row.OpenUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        Assert.True(await _store.AllowAsync(AiCircuitBreakerStore.KindCredential, "probe-key", default));
        Assert.False(await _store.AllowAsync(AiCircuitBreakerStore.KindCredential, "probe-key", default));

        using (var scope = _provider.CreateScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<LearnerDbContext>()
                .AiCircuitStates.AsNoTracking()
                .SingleAsync(s => s.Key == "probe-key");
            Assert.Equal(AiCircuitBreakerStore.StateHalfOpen, row.State);
            Assert.True(row.ProbeInFlight);
        }
    }

    [Fact]
    public async Task RecordSuccess_ClosesAndResets()
    {
        await _store.RecordFailureAsync(AiCircuitBreakerStore.KindProvider, "ok", "401", default);
        await _store.RecordSuccessAsync(AiCircuitBreakerStore.KindProvider, "ok", default);

        Assert.True(await _store.AllowAsync(AiCircuitBreakerStore.KindProvider, "ok", default));

        using var scope = _provider.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<LearnerDbContext>()
            .AiCircuitStates.AsNoTracking()
            .SingleAsync(s => s.Key == "ok");
        Assert.Equal(AiCircuitBreakerStore.StateClosed, row.State);
        Assert.Equal(0, row.FailureCount);
        Assert.False(row.ProbeInFlight);
    }

    [Fact]
    public async Task ResetAsync_ClosesForAdmin()
    {
        await _store.RecordFailureAsync(AiCircuitBreakerStore.KindProvider, "reset-me", "402", default);
        await _store.ResetAsync(AiCircuitBreakerStore.KindProvider, "reset-me", default);
        Assert.True(await _store.AllowAsync(AiCircuitBreakerStore.KindProvider, "reset-me", default));
    }
}
