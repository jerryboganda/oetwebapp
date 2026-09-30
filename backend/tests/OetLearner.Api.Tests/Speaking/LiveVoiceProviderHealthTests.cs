using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// Live voice provider health: the breaker fed by REAL session-creation outcomes, the catalog probe
/// that must never close it, the candidate ordering the browser follows, and the probe schedule.
/// The breaker tests drive time with a mutable clock and never touch a provider.
/// </summary>
public sealed class LiveVoiceProviderHealthTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private static (LiveVoiceProviderProbeState State, MutableTimeProvider Clock) NewState()
    {
        var clock = new MutableTimeProvider(T0);
        return (new LiveVoiceProviderProbeState(clock), clock);
    }

    private static void Fail(
        LiveVoiceProviderProbeState state,
        AiProviderErrorClass errorClass,
        string provider = LiveVoiceProviders.OpenAi,
        TimeSpan? retryAfter = null)
        => state.RecordFailure(provider, errorClass, 429, "some_type", "some_code", retryAfter);

    // ── Breaker ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(AiProviderErrorClass.QuotaExhausted)]
    [InlineData(AiProviderErrorClass.Auth)]
    public void QuotaOrAuth_OpensAtOnce_ForTenMinutes(AiProviderErrorClass errorClass)
    {
        var (state, clock) = NewState();

        var change = state.RecordFailure(LiveVoiceProviders.OpenAi, errorClass, 429, "insufficient_quota", "insufficient_quota", null);

        Assert.Equal(LiveVoiceBreakerTransition.Opened, change.Transition);
        Assert.Equal(T0.AddSeconds(600), change.OpenUntil);
        Assert.True(state.IsOpen(LiveVoiceProviders.OpenAi));
        clock.Advance(TimeSpan.FromSeconds(599));
        Assert.True(state.IsOpen(LiveVoiceProviders.OpenAi));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(state.IsOpen(LiveVoiceProviders.OpenAi));
        Assert.Equal("probation", state.BreakerState(LiveVoiceProviders.OpenAi));
    }

    [Theory]
    [InlineData(null, 30)]
    [InlineData(1, 5)]
    [InlineData(45, 45)]
    [InlineData(600, 120)]
    public void RateLimit_OpensForRetryAfter_ClampedToFiveThroughOneTwentySeconds(int? retryAfterSeconds, int expectedSeconds)
    {
        var (state, clock) = NewState();

        var change = Fail(
            state,
            AiProviderErrorClass.RateLimited,
            retryAfter: retryAfterSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null);

        Assert.Equal(LiveVoiceBreakerTransition.Opened, change.Transition);
        Assert.Equal(T0.AddSeconds(expectedSeconds), change.OpenUntil);
        clock.Advance(TimeSpan.FromSeconds(expectedSeconds - 1));
        Assert.True(state.IsOpen(LiveVoiceProviders.OpenAi));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(state.IsOpen(LiveVoiceProviders.OpenAi));
    }

    [Theory]
    [InlineData(AiProviderErrorClass.ServerError)]
    [InlineData(AiProviderErrorClass.Overloaded)]
    [InlineData(AiProviderErrorClass.Network)]
    [InlineData(AiProviderErrorClass.Unknown)]
    public void SoftFailures_OpenOnlyOnTheSecondWithinAMinute_ForOneMinute(AiProviderErrorClass errorClass)
    {
        var (state, clock) = NewState();

        var first = Fail(state, errorClass);
        Assert.Equal(LiveVoiceBreakerTransition.None, first.Transition);
        Assert.False(state.IsOpen(LiveVoiceProviders.OpenAi));

        clock.Advance(TimeSpan.FromSeconds(30));
        var second = Fail(state, errorClass);

        Assert.Equal(LiveVoiceBreakerTransition.Opened, second.Transition);
        Assert.Equal(T0.AddSeconds(90), second.OpenUntil);
        Assert.True(state.IsOpen(LiveVoiceProviders.OpenAi));
    }

    [Fact]
    public void SoftFailures_MoreThanAMinuteApart_DoNotAccumulate()
    {
        var (state, clock) = NewState();

        Fail(state, AiProviderErrorClass.ServerError);
        clock.Advance(TimeSpan.FromSeconds(61));
        var second = Fail(state, AiProviderErrorClass.ServerError);

        Assert.Equal(LiveVoiceBreakerTransition.None, second.Transition);
        Assert.False(state.IsOpen(LiveVoiceProviders.OpenAi));
        Assert.Equal(1, second.ConsecutiveFailures);
    }

    [Fact]
    public void InvalidRequest_IsCountedButNeverOpens_AndDoesNotCountTowardSoftFailures()
    {
        var (state, _) = NewState();
        var options = LiveVoiceTestKit.DefaultOptions();

        for (var i = 0; i < 5; i++)
        {
            var change = Fail(state, AiProviderErrorClass.InvalidRequest);
            Assert.Equal(LiveVoiceBreakerTransition.None, change.Transition);
        }
        // A malformed request says nothing about provider health: one soft failure after five
        // of them is still only the first soft failure.
        var soft = Fail(state, AiProviderErrorClass.ServerError);

        Assert.False(state.IsOpen(LiveVoiceProviders.OpenAi));
        Assert.Equal(LiveVoiceBreakerTransition.None, soft.Transition);
        var openAi = state.Snapshot(options).Providers.Single(p => p.Provider == LiveVoiceProviders.OpenAi);
        Assert.Equal(6, openAi.Failures);
        Assert.Equal(5, openAi.FailuresByClass["invalid_request"]);
    }

    [Fact]
    public void Success_ClosesTheBreaker_AndResetsTheFailureRun()
    {
        var (state, _) = NewState();
        Fail(state, AiProviderErrorClass.QuotaExhausted);
        Assert.True(state.IsOpen(LiveVoiceProviders.OpenAi));

        var change = state.RecordSuccess(LiveVoiceProviders.OpenAi);

        Assert.Equal(LiveVoiceBreakerTransition.Closed, change.Transition);
        Assert.False(state.IsOpen(LiveVoiceProviders.OpenAi));
        Assert.Equal("closed", state.BreakerState(LiveVoiceProviders.OpenAi));
        // The run restarted: one soft failure does not open.
        Assert.Equal(LiveVoiceBreakerTransition.None, Fail(state, AiProviderErrorClass.ServerError).Transition);
        // A success on a closed breaker is not a transition.
        Assert.Equal(LiveVoiceBreakerTransition.None, state.RecordSuccess(LiveVoiceProviders.Gemini).Transition);
    }

    [Fact]
    public void Probation_ReopensOnTheFirstFailure_AndClosesOnTheFirstSuccess()
    {
        var (state, clock) = NewState();
        Fail(state, AiProviderErrorClass.ServerError);
        Fail(state, AiProviderErrorClass.ServerError);
        Assert.True(state.IsOpen(LiveVoiceProviders.OpenAi));

        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal("probation", state.BreakerState(LiveVoiceProviders.OpenAi));

        // A single soft failure is enough in probation.
        var reopened = Fail(state, AiProviderErrorClass.ServerError);
        Assert.Equal(LiveVoiceBreakerTransition.Opened, reopened.Transition);
        Assert.True(state.IsOpen(LiveVoiceProviders.OpenAi));

        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal("probation", state.BreakerState(LiveVoiceProviders.OpenAi));
        Assert.Equal(LiveVoiceBreakerTransition.Closed, state.RecordSuccess(LiveVoiceProviders.OpenAi).Transition);
        Assert.Equal("closed", state.BreakerState(LiveVoiceProviders.OpenAi));
    }

    [Fact]
    public void FailureWhileAlreadyOpen_IsNotANewTransition_AndNeverShortensTheOpenTime()
    {
        var (state, clock) = NewState();
        var opened = Fail(state, AiProviderErrorClass.QuotaExhausted);
        clock.Advance(TimeSpan.FromSeconds(10));

        // A pinned run bypasses the breaker and fails softly while it is open.
        var during = Fail(state, AiProviderErrorClass.ServerError);

        Assert.Equal(LiveVoiceBreakerTransition.None, during.Transition);
        Assert.Equal(opened.OpenUntil, during.OpenUntil);
    }

    [Fact]
    public void CatalogProbeSuccess_NeverClosesABreakerThatRealFailuresOpened()
    {
        // GET /models returned 200 while every real session creation was rate limited (30 Sep 2026).
        var (state, _) = NewState();
        var options = LiveVoiceTestKit.DefaultOptions();
        state.Set(LiveVoiceProviders.OpenAi, true, null);
        state.Set(LiveVoiceProviders.Gemini, true, null);
        Fail(state, AiProviderErrorClass.QuotaExhausted);

        state.Set(LiveVoiceProviders.OpenAi, true, null);

        Assert.True(state.IsVerified(LiveVoiceProviders.OpenAi));
        Assert.True(state.IsOpen(LiveVoiceProviders.OpenAi));
        Assert.Equal(new[] { LiveVoiceProviders.Gemini }, state.Candidates(options));
    }

    [Fact]
    public void Reset_ClosesTheBreaker_AndReportsWhetherItWasOpen()
    {
        var (state, _) = NewState();
        Fail(state, AiProviderErrorClass.Auth);

        Assert.True(state.Reset(LiveVoiceProviders.OpenAi));
        Assert.False(state.IsOpen(LiveVoiceProviders.OpenAi));
        Assert.False(state.Reset(LiveVoiceProviders.OpenAi));
        Assert.False(state.Reset("not-a-provider"));
    }

    // ── Candidates and availability ──────────────────────────────────

    [Fact]
    public void Candidates_PutThePrimaryFirst_AndTheOtherSecond()
    {
        var (state, _) = NewState();
        state.Set(LiveVoiceProviders.OpenAi, true, null);
        state.Set(LiveVoiceProviders.Gemini, true, null);

        Assert.Equal(
            new[] { LiveVoiceProviders.OpenAi, LiveVoiceProviders.Gemini },
            state.Candidates(LiveVoiceTestKit.DefaultOptions("openai")));
        Assert.Equal(
            new[] { LiveVoiceProviders.Gemini, LiveVoiceProviders.OpenAi },
            state.Candidates(LiveVoiceTestKit.DefaultOptions("gemini")));
        // A blank or unknown primary orders OpenAI first.
        Assert.Equal(
            new[] { LiveVoiceProviders.OpenAi, LiveVoiceProviders.Gemini },
            state.Candidates(LiveVoiceTestKit.DefaultOptions("")));
        Assert.Equal(
            new[] { LiveVoiceProviders.OpenAi, LiveVoiceProviders.Gemini },
            state.Candidates(LiveVoiceTestKit.DefaultOptions("bogus")));
    }

    [Fact]
    public void Candidates_ExcludeUnconfiguredUnverifiedAndOpenProviders()
    {
        var (state, _) = NewState();
        var options = LiveVoiceTestKit.DefaultOptions();

        // Nothing has been verified yet.
        Assert.Empty(state.Candidates(options));

        state.Set(LiveVoiceProviders.OpenAi, true, null);
        state.Set(LiveVoiceProviders.Gemini, true, null);
        Assert.Equal(2, state.Candidates(options).Count);

        // Unconfigured: verified but no key.
        var noGemini = LiveVoiceTestKit.DefaultOptions();
        noGemini.GeminiApiKey = string.Empty;
        Assert.Equal(new[] { LiveVoiceProviders.OpenAi }, state.Candidates(noGemini));

        // Unverified.
        state.Set(LiveVoiceProviders.OpenAi, false, "http_403_auth");
        Assert.Equal(new[] { LiveVoiceProviders.Gemini }, state.Candidates(options));

        // Open.
        state.Set(LiveVoiceProviders.OpenAi, true, null);
        Fail(state, AiProviderErrorClass.QuotaExhausted, LiveVoiceProviders.Gemini);
        Assert.Equal(new[] { LiveVoiceProviders.OpenAi }, state.Candidates(options));
    }

    [Fact]
    public void ProbationProviderIsStillACandidate_SoTheNextRealAttemptDecides()
    {
        var (state, clock) = NewState();
        var options = LiveVoiceTestKit.DefaultOptions();
        state.Set(LiveVoiceProviders.OpenAi, true, null);
        Fail(state, AiProviderErrorClass.RateLimited, retryAfter: TimeSpan.FromSeconds(30));
        Assert.Empty(state.Candidates(options));

        clock.Advance(TimeSpan.FromSeconds(31));

        Assert.Equal(new[] { LiveVoiceProviders.OpenAi }, state.Candidates(options));
    }

    [Fact]
    public void IsLiveVoiceAvailable_IsTrueWhenOnlyTheSecondaryIsHealthy_AndFalseOnlyWhenNoneIs()
    {
        // Regression: the recorder fallback was chosen although the other provider would have worked.
        var (state, _) = NewState();
        var options = LiveVoiceTestKit.DefaultOptions("openai");
        state.Set(LiveVoiceProviders.OpenAi, true, null);
        state.Set(LiveVoiceProviders.Gemini, true, null);
        Assert.True(state.IsLiveVoiceAvailable(options));

        Fail(state, AiProviderErrorClass.QuotaExhausted, LiveVoiceProviders.OpenAi);
        Assert.True(state.IsLiveVoiceAvailable(options));

        Fail(state, AiProviderErrorClass.Auth, LiveVoiceProviders.Gemini);
        Assert.False(state.IsLiveVoiceAvailable(options));
        Assert.False(state.IsLiveVoiceAvailable(null));
    }

    // ── Snapshot ─────────────────────────────────────────────────────

    [Fact]
    public void Snapshot_ReportsBreakerAndFailureClasses_WithoutKeysUrlsOrProviderText()
    {
        var (state, _) = NewState();
        var options = LiveVoiceTestKit.DefaultOptions();
        state.Set(LiveVoiceProviders.OpenAi, true, null);
        state.Set(LiveVoiceProviders.Gemini, false, "http_401_auth");
        state.RecordFailure(
            LiveVoiceProviders.OpenAi,
            AiProviderErrorClass.QuotaExhausted,
            429,
            "insufficient_quota",
            "You exceeded your current quota. Bearer sk-not-a-token",
            null);

        var json = JsonSerializer.Serialize(state.Snapshot(options), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"state\":\"open\"", json, StringComparison.Ordinal);
        Assert.Contains("\"class\":\"quota_exhausted\"", json, StringComparison.Ordinal);
        Assert.Contains("\"providerType\":\"insufficient_quota\"", json, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"http_401_auth\"", json, StringComparison.Ordinal);
        // The free-text "code" is not a token, so it is dropped rather than shown.
        Assert.DoesNotContain("exceeded", json, StringComparison.Ordinal);
        Assert.DoesNotContain("providerCode\":\"You", json, StringComparison.Ordinal);
        Assert.DoesNotContain(LiveVoiceTestKit.OpenAiKey, json, StringComparison.Ordinal);
        Assert.DoesNotContain(LiveVoiceTestKit.GeminiKey, json, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", json, StringComparison.Ordinal);
        Assert.DoesNotContain("wss://", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("insufficient_quota", "insufficient_quota")]
    [InlineData("rate_limit_exceeded", "rate_limit_exceeded")]
    [InlineData("RESOURCE_EXHAUSTED", "RESOURCE_EXHAUSTED")]
    [InlineData("x:y-1.2", "x:y-1.2")]
    [InlineData("models/gemini", null)]
    [InlineData("has a space", null)]
    [InlineData("new\nline", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void SafeToken_KeepsShortProviderTokens_AndDropsEverythingElse(string? value, string? expected)
    {
        Assert.Equal(expected, LiveVoiceProviderProbeState.SafeToken(value));
    }

    [Fact]
    public void SafeToken_DropsAnythingLongerThanSixtyFourCharacters()
    {
        Assert.NotNull(LiveVoiceProviderProbeState.SafeToken(new string('a', 64)));
        Assert.Null(LiveVoiceProviderProbeState.SafeToken(new string('a', 65)));
    }

    // ── Catalog probe ────────────────────────────────────────────────

    private static (LiveVoiceProviderProbe Probe, LiveVoiceProviderProbeState State, ScriptedHttpHandler Handler) NewProbe(
        LiveVoiceOptions? options = null)
    {
        var state = new LiveVoiceProviderProbeState(new MutableTimeProvider(T0));
        var handler = new ScriptedHttpHandler();
        var probe = new LiveVoiceProviderProbe(
            new HandlerHttpClientFactory(handler),
            Options.Create(options ?? LiveVoiceTestKit.DefaultOptions()),
            state,
            NullLogger<LiveVoiceProviderProbe>.Instance);
        return (probe, state, handler);
    }

    private static HttpResponseMessage ModelOk(CapturedProviderRequest request)
        => ScriptedHttpHandler.Reply(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new { id = request.Uri.Host == "api.openai.com" ? "gpt-live-1" : "models/gemini-3.8-live" }));

    [Fact]
    public async Task Probe_VerifiesBothConfiguredProviders_AgainstTheirModelCatalogs()
    {
        var (probe, state, handler) = NewProbe();
        handler.Route(ModelOk);

        await probe.ProbeConfiguredProvidersAsync(CancellationToken.None);

        Assert.True(state.IsVerified(LiveVoiceProviders.OpenAi));
        Assert.True(state.IsVerified(LiveVoiceProviders.Gemini));
        var uris = handler.Requests.Select(r => r.Uri.ToString()).OrderBy(u => u, StringComparer.Ordinal).ToArray();
        Assert.Equal(2, uris.Length);
        Assert.Contains("https://api.openai.com/v1/models/gpt-live-1", uris);
        Assert.Contains("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-live", uris);
        // Each key goes only to its own provider.
        Assert.Equal($"Bearer {LiveVoiceTestKit.OpenAiKey}", handler.Requests.Single(r => r.Uri.Host == "api.openai.com").Authorization);
        Assert.Equal(LiveVoiceTestKit.GeminiKey, handler.Requests.Single(r => r.Uri.Host != "api.openai.com").GoogApiKey);
    }

    [Fact]
    public async Task Probe_SkipsUnconfiguredProviders()
    {
        var options = LiveVoiceTestKit.DefaultOptions();
        options.GeminiApiKey = string.Empty;
        var (probe, state, handler) = NewProbe(options);
        handler.Route(ModelOk);

        await probe.ProbeConfiguredProvidersAsync(CancellationToken.None);

        Assert.True(state.IsVerified(LiveVoiceProviders.OpenAi));
        Assert.Null(state.Get(LiveVoiceProviders.Gemini));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Probe_TransientFailure_KeepsAnAlreadyVerifiedProviderVerified(HttpStatusCode status)
    {
        var (probe, state, handler) = NewProbe();
        handler.Route(ModelOk);
        await probe.ProbeConfiguredProvidersAsync(CancellationToken.None);

        handler.Route(_ => ScriptedHttpHandler.Reply(status, "{}"));
        await probe.ProbeConfiguredProvidersAsync(CancellationToken.None);

        Assert.True(state.IsVerified(LiveVoiceProviders.OpenAi));
        Assert.True(state.IsVerified(LiveVoiceProviders.Gemini));
        Assert.StartsWith("probe_transient_", state.Get(LiveVoiceProviders.OpenAi)!.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_FirstEverTransientFailure_StaysUnverified()
    {
        var (probe, state, handler) = NewProbe();
        handler.Route(_ => ScriptedHttpHandler.Reply(HttpStatusCode.ServiceUnavailable, "<html>bad gateway</html>"));

        await probe.ProbeConfiguredProvidersAsync(CancellationToken.None);

        Assert.False(state.IsVerified(LiveVoiceProviders.OpenAi));
        Assert.False(state.IsVerified(LiveVoiceProviders.Gemini));
        Assert.NotNull(state.Get(LiveVoiceProviders.OpenAi)!.Reason);
    }

    [Fact]
    public async Task Probe_AuthFailure_DropsAVerifiedProvider()
    {
        var (probe, state, handler) = NewProbe();
        handler.Route(ModelOk);
        await probe.ProbeConfiguredProvidersAsync(CancellationToken.None);

        handler.Route(_ => ScriptedHttpHandler.Reply(
            HttpStatusCode.Unauthorized,
            LiveVoiceTestKit.OpenAiError("invalid_api_key", "Incorrect API key provided: sk-live-****")));
        await probe.ProbeConfiguredProvidersAsync(CancellationToken.None);

        Assert.False(state.IsVerified(LiveVoiceProviders.OpenAi));
        Assert.Contains("401", state.Get(LiveVoiceProviders.OpenAi)!.Reason, StringComparison.Ordinal);
        // The reason is our own token, never the provider's message.
        Assert.DoesNotContain("Incorrect", state.Get(LiveVoiceProviders.OpenAi)!.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_ModelMissingFromTheCatalog_IsUnverified_AndNonJsonIsTransient()
    {
        var (probe, state, handler) = NewProbe();
        handler.Route(_ => ScriptedHttpHandler.Reply(HttpStatusCode.OK, JsonSerializer.Serialize(new { id = "some-other-model" })));

        await probe.ProbeConfiguredProvidersAsync(CancellationToken.None);
        Assert.False(state.IsVerified(LiveVoiceProviders.OpenAi));
        Assert.Equal("http_200_model_unverified", state.Get(LiveVoiceProviders.OpenAi)!.Reason);

        handler.Route(ModelOk);
        await probe.ProbeConfiguredProvidersAsync(CancellationToken.None);
        Assert.True(state.IsVerified(LiveVoiceProviders.OpenAi));

        // A 200 that is not JSON must not throw and must not drop a verified provider.
        handler.Route(_ => ScriptedHttpHandler.Reply(HttpStatusCode.OK, "not json at all"));
        await probe.ProbeConfiguredProvidersAsync(CancellationToken.None);
        Assert.True(state.IsVerified(LiveVoiceProviders.OpenAi));
    }

    [Fact]
    public async Task Probe_TransportFault_KeepsVerifiedProviderVerified()
    {
        var (probe, state, handler) = NewProbe();
        handler.Route(ModelOk);
        await probe.ProbeConfiguredProvidersAsync(CancellationToken.None);

        handler.Route(_ => throw new HttpRequestException("connection reset"));
        await probe.ProbeConfiguredProvidersAsync(CancellationToken.None);

        Assert.True(state.IsVerified(LiveVoiceProviders.OpenAi));
        Assert.True(state.IsVerified(LiveVoiceProviders.Gemini));
    }

    [Fact]
    public async Task Probe_NeverTouchesTheBreaker()
    {
        var (probe, state, handler) = NewProbe();
        handler.Route(ModelOk);
        state.RecordFailure(LiveVoiceProviders.OpenAi, AiProviderErrorClass.QuotaExhausted);

        await probe.ProbeConfiguredProvidersAsync(CancellationToken.None);

        Assert.True(state.IsVerified(LiveVoiceProviders.OpenAi));
        Assert.True(state.IsOpen(LiveVoiceProviders.OpenAi));
    }

    // ── Probe schedule and registration ──────────────────────────────

    [Fact]
    public void NextProbeDelay_IsOneMinuteWhileAConfiguredProviderIsUnverifiedOrOpen_ElseFiveMinutes()
    {
        var (state, _) = NewState();
        var options = LiveVoiceTestKit.DefaultOptions();

        // Nothing verified yet: fast.
        Assert.Equal(TimeSpan.FromSeconds(60), LiveVoiceProviderProbe.NextProbeDelay(options, state));

        state.Set(LiveVoiceProviders.OpenAi, true, null);
        state.Set(LiveVoiceProviders.Gemini, true, null);
        Assert.Equal(TimeSpan.FromMinutes(5), LiveVoiceProviderProbe.NextProbeDelay(options, state));

        // A breaker that real failures opened keeps the probe fast so recovery is noticed.
        state.RecordFailure(LiveVoiceProviders.Gemini, AiProviderErrorClass.RateLimited);
        Assert.Equal(TimeSpan.FromSeconds(60), LiveVoiceProviderProbe.NextProbeDelay(options, state));
        state.Reset(LiveVoiceProviders.Gemini);
        Assert.Equal(TimeSpan.FromMinutes(5), LiveVoiceProviderProbe.NextProbeDelay(options, state));

        // An unconfigured provider that is unverified does not matter.
        var onlyOpenAi = LiveVoiceTestKit.DefaultOptions();
        onlyOpenAi.GeminiApiKey = string.Empty;
        state.Set(LiveVoiceProviders.Gemini, false, "probe_network");
        Assert.Equal(TimeSpan.FromMinutes(5), LiveVoiceProviderProbe.NextProbeDelay(onlyOpenAi, state));
    }

    [Fact]
    public void Register_AddsTheProbeLoopOnlyOutsideTheAiWorker_AndAlwaysTheHealthState()
    {
        var api = new ServiceCollection();
        LiveVoiceProviderProbe.Register(api, isWorker: false);
        var worker = new ServiceCollection();
        LiveVoiceProviderProbe.Register(worker, isWorker: true);

        Assert.Contains(api, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(LiveVoiceProviderProbe));
        Assert.DoesNotContain(worker, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(LiveVoiceProviderProbe));
        Assert.Contains(api, d => d.ServiceType == typeof(LiveVoiceProviderProbeState));
        Assert.Contains(worker, d => d.ServiceType == typeof(LiveVoiceProviderProbeState));
    }
}
