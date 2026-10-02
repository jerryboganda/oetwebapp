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

    // ── RULE MAX-ALWAYS-ON (owner, 2 Oct 2026): the Claude Max subscription route is never skipped ──

    [Theory]
    [InlineData("401")]
    [InlineData("403")]
    [InlineData("quota_exhausted")]
    [InlineData("auth")]
    [InlineData("invalid_model")]
    [InlineData("provider_5xx")]
    public async Task MaxSubscriptionProvider_CircuitNeverOpens_WhateverTheFailure(string failureCode)
    {
        for (var i = 0; i < 12; i++)
        {
            await _store.RecordFailureAsync(AiCircuitBreakerStore.KindProvider, "writing-claude-sub", failureCode, default);
            Assert.True(await _store.AllowAsync(AiCircuitBreakerStore.KindProvider, "writing-claude-sub", default));
        }

        // Also when the caller spells it differently.
        await _store.RecordFailureAsync(" provider ", " Writing-Claude-Sub ", failureCode, default);
        Assert.True(await _store.AllowAsync(" provider ", " Writing-Claude-Sub ", default));

        using var scope = _provider.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<LearnerDbContext>()
            .AiCircuitStates.AsNoTracking()
            .AnyAsync(s => s.Key.ToLower() == "writing-claude-sub"));
    }

    [Fact]
    public async Task MaxSubscriptionProvider_AStaleOpenRow_NeverBlocksIt()
    {
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.AiCircuitStates.Add(new AiCircuitState
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = AiCircuitBreakerStore.KindProvider,
                Key = "writing-claude-sub",
                State = AiCircuitBreakerStore.StateOpen,
                OpenedAt = DateTimeOffset.UtcNow,
                OpenUntil = DateTimeOffset.UtcNow.AddHours(6),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        Assert.True(await _store.AllowAsync(AiCircuitBreakerStore.KindProvider, "writing-claude-sub", default));
    }

    [Fact]
    public async Task MaxSubscriptionProvider_IsAllowedEvenWhenTheStoreIsDown_ButOtherProvidersFailClosed()
    {
        var broken = new AiCircuitBreakerStore(new ThrowingScopeFactory(), NullLogger<AiCircuitBreakerStore>.Instance);

        Assert.True(await broken.AllowAsync(AiCircuitBreakerStore.KindProvider, "writing-claude-sub", default));
        Assert.False(await broken.AllowAsync(AiCircuitBreakerStore.KindProvider, "anthropic", default));
    }

    [Fact]
    public void AlwaysOn_CoversOnlyTheMaxProviderKey()
    {
        Assert.True(AiCircuitBreakerStore.IsAlwaysOn(AiCircuitBreakerStore.KindProvider, "writing-claude-sub"));
        Assert.False(AiCircuitBreakerStore.IsAlwaysOn(AiCircuitBreakerStore.KindProvider, "anthropic"));
        Assert.False(AiCircuitBreakerStore.IsAlwaysOn(AiCircuitBreakerStore.KindProvider, "writing-codex-sub"));
        Assert.False(AiCircuitBreakerStore.IsAlwaysOn(AiCircuitBreakerStore.KindCredential, "writing-claude-sub"));
        Assert.False(AiCircuitBreakerStore.IsAlwaysOn(null, null));
    }

    [Fact]
    public async Task Gateway_MaxSubscriptionFailures_NeverShortCircuitTheNextCall()
    {
        var provider = new QuotaExhaustedProvider("writing-claude-sub");
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
            Provider = "writing-claude-sub",
            FeatureCode = AiFeatureCodes.WritingGrade,
        };

        // Every call reaches the Max provider (it errors each time, and fails over inside ITS grade);
        // none is ever refused by an open circuit.
        for (var call = 1; call <= 4; call++)
        {
            var ex = await Assert.ThrowsAsync<AiProviderHttpException>(() => gateway.CompleteAsync(request));
            Assert.Equal(AiProviderErrorClass.QuotaExhausted, ex.ErrorClass);
            Assert.Equal(call, provider.Calls);
        }

        using var scope = _provider.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<LearnerDbContext>()
            .AiCircuitStates.AsNoTracking()
            .AnyAsync(s => s.Key == "writing-claude-sub"));
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("store is down");
    }

    private sealed class QuotaExhaustedProvider(string name = "quota-provider") : IAiModelProvider
    {
        public int Calls { get; private set; }

        public string Name => name;

        public Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
        {
            Calls++;
            throw new AiProviderHttpException(
                "Anthropic", 400, "Bad Request", null,
                new AiProviderError(AiProviderErrorClass.QuotaExhausted, 400, "invalid_request_error", null, null, null, null));
        }
    }
}
