using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Rulebook;

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

    [Theory]
    [InlineData("quota_exhausted")]
    [InlineData("auth")]
    public async Task ProviderQuotaOrAuthClass_OpensImmediately(string failureCode)
    {
        var key = $"class-{failureCode}";

        await _store.RecordFailureAsync(AiCircuitBreakerStore.KindProvider, key, failureCode, default);

        Assert.False(await _store.AllowAsync(AiCircuitBreakerStore.KindProvider, key, default));
        using var scope = _provider.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<LearnerDbContext>()
            .AiCircuitStates.AsNoTracking()
            .SingleAsync(s => s.Key == key);
        Assert.Equal(AiCircuitBreakerStore.StateOpen, row.State);
        Assert.Equal(failureCode, row.LastFailureCode);
    }

    [Fact]
    public async Task ProviderInvalidRequest_DoesNotOpenImmediately_AndNeedsFiveFailures()
    {
        // One bad request (a content specific 400) must never shut a provider shared by every feature.
        for (var i = 0; i < 4; i++)
        {
            await _store.RecordFailureAsync(AiCircuitBreakerStore.KindProvider, "bad-requests", "provider_invalid_request", default);
            Assert.True(await _store.AllowAsync(AiCircuitBreakerStore.KindProvider, "bad-requests", default));
        }

        await _store.RecordFailureAsync(AiCircuitBreakerStore.KindProvider, "bad-requests", "provider_invalid_request", default);
        Assert.False(await _store.AllowAsync(AiCircuitBreakerStore.KindProvider, "bad-requests", default));
    }

    [Fact]
    public async Task Gateway_TypedQuotaFailure_OpensTheProviderCircuit_AndShortCircuitsTheNextCall()
    {
        var provider = new QuotaExhaustedProvider();
        var gateway = new AiGatewayService(
            new RulebookLoader(), new IAiModelProvider[] { provider }, circuitBreaker: _store);
        var request = new AiGatewayRequest
        {
            Prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                Profession = ExamProfession.Medicine,
                Task = AiTaskMode.Score,
                LetterType = "routine_referral",
            }),
            Provider = "quota-provider",
            FeatureCode = AiFeatureCodes.WritingGrade,
        };

        // First call: quota exhausted, quarantined (no retry), circuit opens at once.
        var typed = await Assert.ThrowsAsync<AiProviderHttpException>(() => gateway.CompleteAsync(request));
        Assert.Equal(AiProviderErrorClass.QuotaExhausted, typed.ErrorClass);
        Assert.Equal(1, provider.Calls);

        // Second call: refused by the open circuit without touching the provider.
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.CompleteAsync(request));
        Assert.Equal("HTTP 503 Provider circuit is open.", refused.Message);
        Assert.Equal(1, provider.Calls);

        using var scope = _provider.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<LearnerDbContext>()
            .AiCircuitStates.AsNoTracking()
            .SingleAsync(s => s.Key == "quota-provider");
        Assert.Equal(AiCircuitBreakerStore.StateOpen, row.State);
        Assert.Equal("quota_exhausted", row.LastFailureCode);
    }

    private sealed class QuotaExhaustedProvider : IAiModelProvider
    {
        public int Calls { get; private set; }

        public string Name => "quota-provider";

        public Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
        {
            Calls++;
            throw new AiProviderHttpException(
                "Anthropic", 400, "Bad Request", null,
                new AiProviderError(AiProviderErrorClass.QuotaExhausted, 400, "invalid_request_error", null, null, null, null));
        }
    }
}
