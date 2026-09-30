using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// Live voice provider failover at the control plane. The server orders the providers, classifies
/// every real session-creation outcome into a breaker and answers a failed creation with one
/// generic, non-retryable 503 that tells the browser to try the next candidate. Provider HTTP is
/// scripted; nothing here reaches the network. The 30 Sep 2026 incident (OpenAI session creation
/// answering 429 for every learner, no fallback) is the scenario behind most of these tests.
/// </summary>
public sealed class LiveVoiceFailoverTests
{
    private const string OpenAiHost = "api.openai.com";
    private const string GeminiHost = "generativelanguage.googleapis.com";
    private const string GenericUnavailable = "The realtime voice provider could not start this conversation. Please retry.";

    private static Task<SeededLiveVoiceSession> SeedAsync(
        LiveVoiceRig rig,
        SpeakingSessionState state = SpeakingSessionState.Active,
        string? marker = null)
        => LiveVoiceTestKit.SeedReadySessionAsync(rig.Db, rig.Clock.GetUtcNow(), state, marker: marker);

    private static Task<LiveVoiceOpenAiOfferResponse> MintOpenAiAsync(LiveVoiceRig rig, SeededLiveVoiceSession session)
        => rig.Service.CreateOpenAiOfferAsync(
            session.UserId,
            session.SessionId,
            new LiveVoiceOpenAiOfferRequest(LiveVoiceTestKit.OfferSdp),
            CancellationToken.None);

    private static Task<LiveVoiceGeminiTokenResponse> MintGeminiAsync(LiveVoiceRig rig, SeededLiveVoiceSession session)
        => rig.Service.CreateGeminiTokenAsync(session.UserId, session.SessionId, CancellationToken.None);

    private static Task<LiveVoicePreflightResponse> PreflightAsync(
        LiveVoiceRig rig,
        SeededLiveVoiceSession session,
        string? provider = null)
        => rig.Service.GetPreflightAsync(session.UserId, session.SessionId, provider, CancellationToken.None);

    /// <summary>One provider answers with an error; the other keeps working.</summary>
    private static void FailProvider(
        LiveVoiceRig rig,
        string host,
        HttpStatusCode status,
        string body,
        params (string Name, string Value)[] headers)
    {
        var happy = new HappyProviderRoute();
        rig.Handler.Route(request => request.Uri.Host == host
            && !request.Uri.AbsolutePath.EndsWith("/hangup", StringComparison.Ordinal)
                ? ScriptedHttpHandler.Reply(status, body, headers)
                : happy.Respond(request));
    }

    private static string QuotaBody()
        => LiveVoiceTestKit.OpenAiError(
            "insufficient_quota",
            "You exceeded your current quota, please check your plan and billing details.");

    private static LiveVoiceProviderHealth Health(LiveVoiceRig rig, string provider)
        => rig.State.Snapshot(rig.Options).Providers.Single(p => p.Provider == provider);

    private static Task<List<SpeakingPatientTurn>> SessionRowsAsync(LiveVoiceRig rig, SeededLiveVoiceSession session)
        => rig.Db.SpeakingPatientTurns.AsNoTracking()
            .Where(t => t.SessionId == session.SessionId && t.Role == LiveVoiceService.LiveVoiceSessionRole)
            .OrderBy(t => t.SequenceNumber)
            .ToListAsync();

    private static Task<List<AuditEvent>> CircuitEventsAsync(LiveVoiceRig rig)
        => rig.Db.AuditEvents.AsNoTracking()
            .Where(e => e.ResourceType == "LiveVoiceProvider")
            .OrderBy(e => e.OccurredAt)
            .ToListAsync();

    private static string InstructionsOf(CapturedProviderRequest request)
    {
        using var document = JsonDocument.Parse(request.Body!);
        var root = document.RootElement;
        return root.TryGetProperty("session", out var session)
            ? session.GetProperty("instructions").GetString()!
            : root.GetProperty("bidiGenerateContentSetup")
                .GetProperty("systemInstruction")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString()!;
    }

    // ── Preflight: ordering, pinning, availability ───────────────────

    [Fact]
    public async Task Preflight_Unpinned_ReturnsThePrimaryFirstWithBothCandidates_AndMakesNoProviderCall()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);

        var preflight = await PreflightAsync(rig, session);

        Assert.Equal(LiveVoiceProviders.OpenAi, preflight.Provider);
        Assert.Equal(new[] { LiveVoiceProviders.OpenAi, LiveVoiceProviders.Gemini }, preflight.Candidates!);
        Assert.False(preflight.Pinned);
        Assert.Empty(rig.Handler.Requests);
    }

    [Fact]
    public async Task Preflight_GeminiPrimary_OrdersGeminiFirst()
    {
        using var rig = LiveVoiceTestKit.Create(LiveVoiceTestKit.DefaultOptions("gemini"));
        var session = await SeedAsync(rig);

        var preflight = await PreflightAsync(rig, session);

        Assert.Equal(LiveVoiceProviders.Gemini, preflight.Provider);
        Assert.Equal(new[] { LiveVoiceProviders.Gemini, LiveVoiceProviders.OpenAi }, preflight.Candidates!);
        Assert.Equal("Google Gemini Live", preflight.ProviderDisplayName);
    }

    [Fact]
    public async Task Preflight_SkipsAnOpenProvider_ButPinnedBypassesTheBreaker()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        rig.State.RecordFailure(LiveVoiceProviders.OpenAi, AiProviderErrorClass.QuotaExhausted);

        var unpinned = await PreflightAsync(rig, session);
        var pinned = await PreflightAsync(rig, session, "openai");

        Assert.Equal(LiveVoiceProviders.Gemini, unpinned.Provider);
        Assert.Equal(new[] { LiveVoiceProviders.Gemini }, unpinned.Candidates!);
        Assert.False(unpinned.Pinned);
        // The owner's apples-to-apples runs pin a provider: exactly that one, no failover.
        Assert.Equal(LiveVoiceProviders.OpenAi, pinned.Provider);
        Assert.Equal(new[] { LiveVoiceProviders.OpenAi }, pinned.Candidates!);
        Assert.True(pinned.Pinned);
    }

    [Fact]
    public async Task Preflight_DisclosureNamesEveryProviderTheLearnerMayBeRoutedTo_InTheOrderTheyAreTried()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);

        var both = await PreflightAsync(rig, session);
        var pinned = await PreflightAsync(rig, session, "gemini");
        rig.State.RecordFailure(LiveVoiceProviders.OpenAi, AiProviderErrorClass.QuotaExhausted);
        var onlyGemini = await PreflightAsync(rig, session);

        // Failover can send the microphone to either provider, so the learner is told about both.
        var openAiAt = both.Disclosure.IndexOf("OpenAI GPT-Live", StringComparison.Ordinal);
        var geminiAt = both.Disclosure.IndexOf("Google Gemini Live", StringComparison.Ordinal);
        Assert.True(openAiAt >= 0 && geminiAt > openAiAt, both.Disclosure);
        Assert.Contains(rig.Options.OpenAiModel, both.Disclosure, StringComparison.Ordinal);
        Assert.Contains(rig.Options.GeminiModel, both.Disclosure, StringComparison.Ordinal);
        Assert.Contains("may switch to", both.Disclosure, StringComparison.Ordinal);
        Assert.Contains("retain", both.Disclosure, StringComparison.Ordinal);
        // A single candidate (pinned, or the only healthy provider) names only itself: no failover.
        Assert.Contains("Google Gemini Live", pinned.Disclosure, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenAI", pinned.Disclosure, StringComparison.Ordinal);
        Assert.DoesNotContain("may switch to", pinned.Disclosure, StringComparison.Ordinal);
        Assert.Equal(pinned.Disclosure, onlyGemini.Disclosure);
    }

    [Fact]
    public async Task Preflight_Pinned_StillRequiresAConfiguredVerifiedProvider()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        rig.State.Set(LiveVoiceProviders.Gemini, false, "http_403_auth");

        var unverified = await Assert.ThrowsAsync<ApiException>(() => PreflightAsync(rig, session, "gemini"));
        var unknown = await Assert.ThrowsAsync<ApiException>(() => PreflightAsync(rig, session, "bogus"));

        Assert.Equal("live_voice_provider_unverified", unverified.ErrorCode);
        Assert.True(unverified.Retryable);
        Assert.Equal("live_voice_provider_not_configured", unknown.ErrorCode);
        Assert.False(unknown.Retryable);
    }

    [Fact]
    public async Task Preflight_LeavesOutAProviderThatIsNotVerified_AndNeverCallsIt()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        rig.State.Set(LiveVoiceProviders.OpenAi, false, "http_404_invalid_request");

        var preflight = await PreflightAsync(rig, session);

        Assert.Equal(LiveVoiceProviders.Gemini, preflight.Provider);
        Assert.Equal(new[] { LiveVoiceProviders.Gemini }, preflight.Candidates!);
        Assert.Empty(rig.Handler.Requests);
    }

    [Fact]
    public async Task Preflight_WithNoConfiguredProvider_IsNotConfiguredAndNotRetryable()
    {
        using var rig = LiveVoiceTestKit.Create(new LiveVoiceOptions());
        var session = await SeedAsync(rig);

        var ex = await Assert.ThrowsAsync<ApiException>(() => PreflightAsync(rig, session));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ex.StatusCode);
        Assert.Equal("live_voice_provider_not_configured", ex.ErrorCode);
        Assert.False(ex.Retryable);
    }

    [Fact]
    public async Task Preflight_WhenConfiguredProvidersAreUnverifiedOrOpen_IsUnavailableAndRetryable()
    {
        using var unverified = LiveVoiceTestKit.Create(verified: false);
        var first = await SeedAsync(unverified);
        var notYet = await Assert.ThrowsAsync<ApiException>(() => PreflightAsync(unverified, first));
        Assert.Equal("live_voice_provider_unavailable", notYet.ErrorCode);
        Assert.True(notYet.Retryable);

        using var open = LiveVoiceTestKit.Create();
        var second = await SeedAsync(open);
        open.State.RecordFailure(LiveVoiceProviders.OpenAi, AiProviderErrorClass.QuotaExhausted);
        open.State.RecordFailure(LiveVoiceProviders.Gemini, AiProviderErrorClass.QuotaExhausted);
        var bothDown = await Assert.ThrowsAsync<ApiException>(() => PreflightAsync(open, second));
        Assert.Equal("live_voice_provider_unavailable", bothDown.ErrorCode);
        Assert.True(bothDown.Retryable);
        // The DTO flag the page reads: recorder mode only when no provider can start.
        Assert.False(open.State.IsLiveVoiceAvailable(open.Options));
    }

    // ── Failure handling on session creation ─────────────────────────

    [Fact]
    public async Task OpenAi429InsufficientQuota_Returns503NonRetryable_WritesNoSessionRow_OpensTheBreaker_AndFailsOverToGemini()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        FailProvider(rig, OpenAiHost, HttpStatusCode.TooManyRequests, QuotaBody());

        var ex = await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, session));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ex.StatusCode);
        Assert.Equal("live_voice_provider_unavailable", ex.ErrorCode);
        Assert.False(ex.Retryable);
        Assert.Equal(GenericUnavailable, ex.Message);
        Assert.Empty(await SessionRowsAsync(rig, session));
        Assert.True(rig.State.IsOpen(LiveVoiceProviders.OpenAi));
        Assert.Equal(AiProviderErrorClass.QuotaExhausted.ToCode(), Health(rig, LiveVoiceProviders.OpenAi).LastFailure!.Class);

        // The browser's next call: preflight now leads with Gemini, and the Gemini leg works.
        var next = await PreflightAsync(rig, session);
        Assert.Equal(LiveVoiceProviders.Gemini, next.Provider);
        Assert.Equal(new[] { LiveVoiceProviders.Gemini }, next.Candidates!);
        var token = await MintGeminiAsync(rig, session);
        Assert.Equal(LiveVoiceProviders.Gemini, token.Provider);
        Assert.Single(await SessionRowsAsync(rig, session));
    }

    [Theory]
    [InlineData(401, "invalid_api_key", true)]
    [InlineData(403, "permission_denied", true)]
    [InlineData(429, "rate_limit_exceeded", true)]
    [InlineData(500, "server_error", false)]
    [InlineData(503, "service_unavailable", false)]
    public async Task ProviderStatusFailures_MapToAGeneric503_AndFeedTheBreaker(int status, string code, bool opensAtOnce)
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        FailProvider(rig, OpenAiHost, (HttpStatusCode)status, LiveVoiceTestKit.OpenAiError(code, "provider said no"));

        var ex = await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, session));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ex.StatusCode);
        Assert.Equal("live_voice_provider_unavailable", ex.ErrorCode);
        Assert.False(ex.Retryable);
        Assert.Equal(GenericUnavailable, ex.Message);
        Assert.Equal(opensAtOnce, rig.State.IsOpen(LiveVoiceProviders.OpenAi));
        Assert.Equal(1, Health(rig, LiveVoiceProviders.OpenAi).Failures);
        Assert.Empty(await SessionRowsAsync(rig, session));
    }

    [Fact]
    public async Task SecondSoftFailureWithinAMinute_OpensTheBreaker()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        FailProvider(rig, OpenAiHost, HttpStatusCode.InternalServerError, LiveVoiceTestKit.OpenAiError("server_error", "boom"));

        await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, session));
        Assert.False(rig.State.IsOpen(LiveVoiceProviders.OpenAi));
        rig.Clock.Advance(TimeSpan.FromSeconds(20));
        await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, session));

        Assert.True(rig.State.IsOpen(LiveVoiceProviders.OpenAi));
    }

    [Fact]
    public async Task InvalidRequest400_Returns503_ButNeverOpensTheBreaker_AndNeverLogsTheProviderText()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        // A 400 can echo the request, which carries the hidden card instructions.
        FailProvider(
            rig,
            OpenAiHost,
            HttpStatusCode.BadRequest,
            LiveVoiceTestKit.OpenAiError("invalid_request_error", $"Invalid value: HIDDEN-{session.Marker}"));

        for (var i = 0; i < 3; i++)
        {
            var ex = await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, session));
            Assert.Equal("live_voice_provider_unavailable", ex.ErrorCode);
            Assert.False(ex.Retryable);
        }

        Assert.False(rig.State.IsOpen(LiveVoiceProviders.OpenAi));
        Assert.Equal(3, Health(rig, LiveVoiceProviders.OpenAi).FailuresByClass[AiProviderErrorClass.InvalidRequest.ToCode()]);
        var logs = rig.Log.AllText;
        Assert.Contains($"class={AiProviderErrorClass.InvalidRequest.ToCode()}", logs, StringComparison.Ordinal);
        // A malformed request is the learner's doing: a Warning, never an Error that reaches Sentry.
        Assert.Contains(rig.Log.Entries, e => e.Level == LogLevel.Warning
            && e.Text.Contains($"class={AiProviderErrorClass.InvalidRequest.ToCode()}", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Entries, e => e.Level == LogLevel.Error);
        Assert.DoesNotContain("HIDDEN-", logs, StringComparison.Ordinal);
        Assert.Empty(await CircuitEventsAsync(rig));
    }

    [Theory]
    [InlineData(400, "invalid_request_error", LogLevel.Warning)]
    [InlineData(422, "invalid_request_error", LogLevel.Warning)]
    // A request the provider will never accept whoever the learner is: an operator has to act.
    [InlineData(404, "model_not_found", LogLevel.Error)]
    [InlineData(415, "invalid_request_error", LogLevel.Error)]
    [InlineData(401, "invalid_api_key", LogLevel.Error)]
    [InlineData(429, "insufficient_quota", LogLevel.Error)]
    // Everything the breaker and the next candidate absorb.
    [InlineData(429, "rate_limit_exceeded", LogLevel.Warning)]
    [InlineData(500, "server_error", LogLevel.Warning)]
    [InlineData(503, "service_unavailable", LogLevel.Warning)]
    public async Task ProviderFailures_LogAtErrorOnlyWhereAnOperatorMustAct(int status, string code, LogLevel expected)
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        FailProvider(rig, OpenAiHost, (HttpStatusCode)status, LiveVoiceTestKit.OpenAiError(code, "provider said no"));

        await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, session));

        var failureLog = Assert.Single(rig.Log.Entries, e => e.Text.Contains("session creation failed", StringComparison.Ordinal));
        Assert.Equal(expected, failureLog.Level);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(422)]
    public async Task AGeminiInvalidRequest_LogsAtError_BecauseTheTokenBodyIsBuiltOnTheServer(int status)
    {
        // Unlike OpenAI's offer (which carries the learner's SDP), the Gemini token request has no
        // learner input: a 400/422 means our own body or configuration is wrong for every learner.
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        FailProvider(
            rig,
            GeminiHost,
            (HttpStatusCode)status,
            "{\"error\":{\"code\":" + status + ",\"message\":\"Invalid value\",\"status\":\"INVALID_ARGUMENT\"}}");

        await Assert.ThrowsAsync<ApiException>(() => MintGeminiAsync(rig, session));

        var failureLog = Assert.Single(rig.Log.Entries, e => e.Text.Contains("session creation failed", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Error, failureLog.Level);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(413)]
    [InlineData(415)]
    [InlineData(422)]
    public async Task ClientErrorStatuses_NeverLogTheProviderText_BecauseTheyCanEchoTheRequest(int status)
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        FailProvider(
            rig,
            OpenAiHost,
            (HttpStatusCode)status,
            LiveVoiceTestKit.OpenAiError("invalid_request_error", $"Could not parse HIDDEN-{session.Marker} in the request"));

        await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, session));

        var logs = rig.Log.AllText;
        Assert.Contains($"http={status}", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("HIDDEN-", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("Could not parse", logs, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(500, "server_error")]
    [InlineData(503, "service_unavailable")]
    public async Task ServerFaults_LogTheVendorText_ThatTheyNeverEcho(int status, string code)
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        FailProvider(rig, OpenAiHost, (HttpStatusCode)status, LiveVoiceTestKit.OpenAiError(code, "The server had an error while processing your request"));

        await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, session));

        var logs = rig.Log.AllText;
        Assert.Contains("The server had an error while processing your request", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(LiveVoiceTestKit.OpenAiKey, logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderTimeout_MapsToTheTimeoutCode_AndCountsAsANetworkFailure()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        rig.Handler.Route(_ => throw new TaskCanceledException("timed out", new TimeoutException()));

        var ex = await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, session));

        Assert.Equal("live_voice_provider_timeout", ex.ErrorCode);
        Assert.Equal("The realtime voice provider did not respond in time.", ex.Message);
        Assert.False(ex.Retryable);
        Assert.Equal(1, Health(rig, LiveVoiceProviders.OpenAi).FailuresByClass[AiProviderErrorClass.Network.ToCode()]);
        Assert.Empty(await SessionRowsAsync(rig, session));
    }

    [Fact]
    public async Task SlowProvider_IsCutOffAtTheConfiguredProviderTimeout()
    {
        var options = LiveVoiceTestKit.DefaultOptions();
        options.ProviderRequestTimeoutSeconds = 2;
        using var rig = LiveVoiceTestKit.Create(options);
        var session = await SeedAsync(rig);
        rig.Handler.Script = async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return ScriptedHttpHandler.Reply(HttpStatusCode.OK, "{}");
        };

        var ex = await Assert.ThrowsAsync<ApiException>(() => MintGeminiAsync(rig, session));

        Assert.Equal("live_voice_provider_timeout", ex.ErrorCode);
        Assert.False(ex.Retryable);
    }

    [Theory]
    [InlineData(int.MinValue, 2)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(10, 10)]
    [InlineData(20, 20)]
    [InlineData(21, 20)]
    [InlineData(30, 20)]
    [InlineData(int.MaxValue, 20)]
    public void ProviderTimeout_IsClampedBetweenTwoAndTwentySeconds_SoItStaysBelowTheBrowsersCreateCallTimeout(
        int configured,
        int expectedSeconds)
    {
        var options = new LiveVoiceOptions { ProviderRequestTimeoutSeconds = configured };

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), options.ProviderRequestTimeout());
        // The browser's create call gives up after 22 s (lib/api/speaking-live-voice.ts): the server
        // must always answer with its own 503 before that, or a failover turns into a client timeout.
        Assert.True(options.ProviderRequestTimeout() < TimeSpan.FromSeconds(22));
    }

    [Fact]
    public async Task TransportFault_MapsToUnavailableWithTheCouldNotBeReachedMessage()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        rig.Handler.Route(_ => throw new HttpRequestException("connection refused"));

        var ex = await Assert.ThrowsAsync<ApiException>(() => MintGeminiAsync(rig, session));

        Assert.Equal("live_voice_provider_unavailable", ex.ErrorCode);
        Assert.Equal("The realtime voice provider could not be reached.", ex.Message);
        Assert.False(ex.Retryable);
        Assert.Equal(1, Health(rig, LiveVoiceProviders.Gemini).FailuresByClass[AiProviderErrorClass.Network.ToCode()]);
    }

    [Fact]
    public async Task CallerCancellation_IsNotAProviderFailure()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        // The handler signals when the provider call is in flight, so the test cancels exactly then
        // instead of polling on a timer.
        var providerCallStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Handler.Script = async (_, ct) =>
        {
            providerCallStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return ScriptedHttpHandler.Reply(HttpStatusCode.OK, "{}");
        };
        using var cts = new CancellationTokenSource();

        var pending = rig.Service.CreateOpenAiOfferAsync(
            session.UserId,
            session.SessionId,
            new LiveVoiceOpenAiOfferRequest(LiveVoiceTestKit.OfferSdp),
            cts.Token);
        await providerCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(rig.Handler.Requests);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        var health = Health(rig, LiveVoiceProviders.OpenAi);
        Assert.Equal(0, health.Failures);
        Assert.Equal(0, health.Attempts);
        Assert.False(rig.State.IsOpen(LiveVoiceProviders.OpenAi));
    }

    [Fact]
    public async Task UnusableSuccessBody_MapsToInvalidResponse_WritesNoSessionRow_AndCountsAServerFault()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        FailProvider(rig, OpenAiHost, HttpStatusCode.OK, "{}");

        var openAi = await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, session));

        Assert.Equal("live_voice_provider_invalid_response", openAi.ErrorCode);
        Assert.Equal("The realtime voice provider returned an invalid session response.", openAi.Message);
        Assert.False(openAi.Retryable);
        Assert.Equal(1, Health(rig, LiveVoiceProviders.OpenAi).FailuresByClass[AiProviderErrorClass.ServerError.ToCode()]);

        FailProvider(rig, GeminiHost, HttpStatusCode.OK, "this is not json");
        var gemini = await Assert.ThrowsAsync<ApiException>(() => MintGeminiAsync(rig, session));

        Assert.Equal("live_voice_provider_invalid_response", gemini.ErrorCode);
        Assert.Equal("The realtime voice provider returned an invalid token response.", gemini.Message);
        Assert.Empty(await SessionRowsAsync(rig, session));
    }

    [Fact]
    public async Task PolicyAndStateErrors_NeverCallAProvider_AndNeverTouchTheBreaker()
    {
        using var rig = LiveVoiceTestKit.Create();
        var noConsent = await SeedAsync(rig);
        var tracked = await rig.Db.SpeakingSessions.SingleAsync(s => s.Id == noConsent.SessionId);
        tracked.ConsentAcceptedAt = null;
        await rig.Db.SaveChangesAsync();
        var inPrep = await SeedAsync(rig, SpeakingSessionState.Prep);
        var ready = await SeedAsync(rig);

        var consent = await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, noConsent));
        var notActive = await Assert.ThrowsAsync<ApiException>(() => MintGeminiAsync(rig, inPrep));
        var noSdp = await Assert.ThrowsAsync<ApiException>(() => rig.Service.CreateOpenAiOfferAsync(
            ready.UserId, ready.SessionId, new LiveVoiceOpenAiOfferRequest(" "), CancellationToken.None));
        var stranger = await Assert.ThrowsAsync<ApiException>(() => rig.Service.CreateGeminiTokenAsync(
            "someone-else", ready.SessionId, CancellationToken.None));

        Assert.Equal("live_voice_consent_required", consent.ErrorCode);
        Assert.Equal(StatusCodes.Status409Conflict, consent.StatusCode);
        Assert.Equal("live_voice_session_not_active", notActive.ErrorCode);
        Assert.Equal("live_voice_sdp_required", noSdp.ErrorCode);
        Assert.Equal(StatusCodes.Status400BadRequest, noSdp.StatusCode);
        Assert.Equal("speaking_session_not_found", stranger.ErrorCode);
        Assert.Empty(rig.Handler.Requests);
        Assert.Equal(0, Health(rig, LiveVoiceProviders.OpenAi).Attempts);
        Assert.Equal(0, Health(rig, LiveVoiceProviders.Gemini).Attempts);
    }

    [Fact]
    public async Task BothProvidersFailing_LeavesNoCandidate_AndTheDtoFlagFalse()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        FailProvider(rig, OpenAiHost, HttpStatusCode.TooManyRequests, QuotaBody());
        await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, session));
        FailProvider(rig, GeminiHost, HttpStatusCode.Unauthorized, LiveVoiceTestKit.OpenAiError("invalid_api_key", "bad"));
        await Assert.ThrowsAsync<ApiException>(() => MintGeminiAsync(rig, session));

        var ex = await Assert.ThrowsAsync<ApiException>(() => PreflightAsync(rig, session));

        Assert.Equal("live_voice_provider_unavailable", ex.ErrorCode);
        Assert.True(ex.Retryable);
        Assert.False(rig.State.IsLiveVoiceAvailable(rig.Options));
    }

    // ── What may and may not be logged ───────────────────────────────

    [Fact]
    public async Task A401_LogsClassAndStatus_ButNeverTheProviderMessageKeysSdpOrInstructions()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        const string keyFragment = "sk-live-****FRAG9";
        FailProvider(
            rig,
            OpenAiHost,
            HttpStatusCode.Unauthorized,
            LiveVoiceTestKit.OpenAiError(
                "invalid_api_key",
                $"Incorrect API key provided: {keyFragment}. You can find your API key at the dashboard."));

        var ex = await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, session));

        var logs = rig.Log.AllText;
        Assert.Contains($"class={AiProviderErrorClass.Auth.ToCode()}", logs, StringComparison.Ordinal);
        Assert.Contains("http=401", logs, StringComparison.Ordinal);
        Assert.Contains("code=invalid_api_key", logs, StringComparison.Ordinal);
        foreach (var secret in new[]
                 {
                     keyFragment,
                     "FRAG9",
                     "Incorrect API key",
                     LiveVoiceTestKit.OpenAiKey,
                     LiveVoiceTestKit.GeminiKey,
                     LiveVoiceTestKit.OfferSdp,
                     "HIDDEN-",
                     "PATIENT-BACKGROUND-",
                 })
        {
            Assert.DoesNotContain(secret, logs, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, ex.Message, StringComparison.Ordinal);
        }
        Assert.Contains(rig.Log.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task A429_LogsTheVendorTextThatExplainsIt_WithoutAnySecret()
    {
        // The whole point of capturing the exact error: the owner can finally read why 429.
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        FailProvider(rig, OpenAiHost, HttpStatusCode.TooManyRequests, QuotaBody());

        await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, session));

        var logs = rig.Log.AllText;
        Assert.Contains($"class={AiProviderErrorClass.QuotaExhausted.ToCode()}", logs, StringComparison.Ordinal);
        Assert.Contains("http=429", logs, StringComparison.Ordinal);
        Assert.Contains("code=insufficient_quota", logs, StringComparison.Ordinal);
        Assert.Contains("exceeded your current quota", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(LiveVoiceTestKit.OpenAiKey, logs, StringComparison.Ordinal);
        Assert.DoesNotContain("HIDDEN-", logs, StringComparison.Ordinal);
    }

    // ── Breaker audit ────────────────────────────────────────────────

    [Fact]
    public async Task BreakerTransitions_WriteOneAuditEventEach_AndNeverOnePerFailure()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        FailProvider(rig, OpenAiHost, HttpStatusCode.TooManyRequests, QuotaBody());

        await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, session));
        var afterOpen = await CircuitEventsAsync(rig);
        // A pinned run keeps failing while the breaker is open: still just the one Opened event.
        await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, session));
        await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, session));
        var afterMoreFailures = await CircuitEventsAsync(rig);
        // The provider recovers and the pinned attempt succeeds: the Closed event.
        rig.Clock.Advance(TimeSpan.FromSeconds(5));
        rig.Handler.Route(new HappyProviderRoute().Respond);
        await MintOpenAiAsync(rig, session);
        var afterClose = await CircuitEventsAsync(rig);

        var opened = Assert.Single(afterOpen);
        Assert.Equal("LiveVoiceProviderCircuitOpened", opened.Action);
        Assert.Equal("system", opened.ActorId);
        Assert.Equal("LiveVoiceProvider", opened.ResourceType);
        Assert.Equal(LiveVoiceProviders.OpenAi, opened.ResourceId);
        using (var details = JsonDocument.Parse(opened.Details!))
        {
            Assert.Equal(AiProviderErrorClass.QuotaExhausted.ToCode(), details.RootElement.GetProperty("kind").GetString());
            Assert.Equal(429, details.RootElement.GetProperty("httpStatus").GetInt32());
            Assert.Equal("insufficient_quota", details.RootElement.GetProperty("providerCode").GetString());
            Assert.NotEqual(JsonValueKind.Null, details.RootElement.GetProperty("openUntil").ValueKind);
        }
        Assert.Single(afterMoreFailures);
        Assert.Equal(2, afterClose.Count);
        Assert.Equal("LiveVoiceProviderCircuitClosed", afterClose[1].Action);
        Assert.False(rig.State.IsOpen(LiveVoiceProviders.OpenAi));
        // Postgres limits the in-memory provider never enforces.
        foreach (var audit in afterClose)
        {
            Assert.True(audit.Action.Length <= MaxLength<AuditEvent>(nameof(AuditEvent.Action)));
            Assert.True(audit.ActorId.Length <= MaxLength<AuditEvent>(nameof(AuditEvent.ActorId)));
            Assert.True(audit.ActorName.Length <= MaxLength<AuditEvent>(nameof(AuditEvent.ActorName)));
            Assert.True(audit.ResourceType.Length <= MaxLength<AuditEvent>(nameof(AuditEvent.ResourceType)));
            Assert.True(audit.ResourceId!.Length <= MaxLength<AuditEvent>(nameof(AuditEvent.ResourceId)));
        }
    }

    [Fact]
    public async Task AuditWriteFault_NeverMasksTheProviderFailure()
    {
        using var rig = LiveVoiceTestKit.Create(faultingDb: true);
        var session = await SeedAsync(rig);
        rig.FaultingDb.FailAuditWrites = true;
        FailProvider(rig, OpenAiHost, HttpStatusCode.TooManyRequests, QuotaBody());

        var ex = await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, session));

        Assert.Equal("live_voice_provider_unavailable", ex.ErrorCode);
        Assert.Equal(1, rig.FaultingDb.AuditWriteFailures);
        Assert.True(rig.State.IsOpen(LiveVoiceProviders.OpenAi));
        Assert.Contains(rig.Log.Entries, e => e.Level == LogLevel.Warning
            && e.Text.Contains("audit event", StringComparison.Ordinal));
        rig.FaultingDb.FailAuditWrites = false;
        Assert.Empty(await CircuitEventsAsync(rig));
    }

    // ── Recorded sessions ────────────────────────────────────────────

    [Fact]
    public async Task SuccessfulCreation_RecordsOneSessionRow_StoresTheRawIdOnlyForOpenAi_AndReturnsTheHardStop()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);

        var offer = await MintOpenAiAsync(rig, session);
        var token = await MintGeminiAsync(rig, session);

        // Started now, 300 s card, 30 s grace.
        Assert.Equal(rig.Clock.GetUtcNow().AddSeconds(330), offer.HardStopAt);
        Assert.Equal(rig.Clock.GetUtcNow().AddSeconds(330), token.HardStopAt);
        var rows = await SessionRowsAsync(rig, session);
        Assert.Equal(2, rows.Count);
        using var openAiRow = JsonDocument.Parse(rows[0].ResponseJson);
        Assert.Equal(LiveVoiceProviders.OpenAi, openAiRow.RootElement.GetProperty("provider").GetString());
        Assert.Equal(offer.ProviderSessionId, openAiRow.RootElement.GetProperty("providerSessionId").GetString());
        Assert.NotEmpty(openAiRow.RootElement.GetProperty("providerSessionIdHash").GetString()!);
        // The Gemini "session id" is the ephemeral token, a bearer credential: never stored raw.
        using var geminiRow = JsonDocument.Parse(rows[1].ResponseJson);
        Assert.Equal(LiveVoiceProviders.Gemini, geminiRow.RootElement.GetProperty("provider").GetString());
        Assert.Equal(JsonValueKind.Null, geminiRow.RootElement.GetProperty("providerSessionId").ValueKind);
        Assert.DoesNotContain(token.ProviderSessionId, rows[1].ResponseJson, StringComparison.Ordinal);
        // Postgres limits the in-memory provider never enforces.
        Assert.All(rows, row =>
        {
            Assert.True(row.Role.Length <= MaxLength<SpeakingPatientTurn>(nameof(SpeakingPatientTurn.Role)));
            Assert.True(row.ClientTurnId!.Length <= MaxLength<SpeakingPatientTurn>(nameof(SpeakingPatientTurn.ClientTurnId)));
        });
    }

    [Fact]
    public async Task PinnedSuccess_ClosesAStaleBreaker_AndTheProviderIsACandidateAgain()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        rig.State.RecordFailure(LiveVoiceProviders.OpenAi, AiProviderErrorClass.QuotaExhausted);
        Assert.Equal(new[] { LiveVoiceProviders.Gemini }, (await PreflightAsync(rig, session)).Candidates!);

        // A pinned run bypasses the breaker; its outcome is still recorded.
        var pinned = await PreflightAsync(rig, session, "openai");
        await MintOpenAiAsync(rig, session);

        Assert.True(pinned.Pinned);
        Assert.False(rig.State.IsOpen(LiveVoiceProviders.OpenAi));
        Assert.Equal(
            new[] { LiveVoiceProviders.OpenAi, LiveVoiceProviders.Gemini },
            (await PreflightAsync(rig, session)).Candidates!);
    }

    [Fact]
    public async Task Keys_NeverCrossProviders_AndNeverAppearInABody()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);

        await MintOpenAiAsync(rig, session);
        await MintGeminiAsync(rig, session);

        var openAi = rig.Handler.Requests.Single(r => r.Uri.Host == OpenAiHost);
        var gemini = rig.Handler.Requests.Single(r => r.Uri.Host == GeminiHost);
        Assert.Equal($"Bearer {LiveVoiceTestKit.OpenAiKey}", openAi.Authorization);
        Assert.Null(openAi.GoogApiKey);
        Assert.Null(gemini.Authorization);
        Assert.Equal(LiveVoiceTestKit.GeminiKey, gemini.GoogApiKey);
        Assert.DoesNotContain(LiveVoiceTestKit.OpenAiKey, openAi.Body!, StringComparison.Ordinal);
        Assert.DoesNotContain(LiveVoiceTestKit.GeminiKey, gemini.Body!, StringComparison.Ordinal);
        Assert.DoesNotContain(LiveVoiceTestKit.GeminiKey, LiveVoiceTestKit.AllText(new[] { openAi }), StringComparison.Ordinal);
        Assert.DoesNotContain(LiveVoiceTestKit.OpenAiKey, LiveVoiceTestKit.AllText(new[] { gemini }), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BothProviders_GetTheSameInstructions_ForOneSession()
    {
        // A failover mid-session must not change the patient.
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);

        await MintOpenAiAsync(rig, session);
        await MintGeminiAsync(rig, session);

        var requests = rig.Handler.Requests;
        var openAi = InstructionsOf(requests.Single(r => r.Uri.Host == OpenAiHost));
        var gemini = InstructionsOf(requests.Single(r => r.Uri.Host == GeminiHost));
        Assert.Equal(openAi, gemini);
        Assert.Contains($"HIDDEN-{session.Marker}", openAi, StringComparison.Ordinal);
    }

    // ── Recorded provider wins ───────────────────────────────────────

    [Fact]
    public async Task TurnsAndTranscript_UseTheRecordedProvider_IgnoringAMismatchingClientLabel()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        var token = await MintGeminiAsync(rig, session);

        // The client believes it is on OpenAI (a state bug after a switch); the id says Gemini.
        var turn = await rig.Service.PersistTurnAsync(
            session.UserId,
            session.SessionId,
            new LiveVoiceTurnRequest("openai", token.ProviderSessionId, "Hello doctor", "Hello", "voice-turn:1", 1),
            CancellationToken.None);
        var transcript = await rig.Service.PersistTranscriptAsync(
            session.UserId,
            session.SessionId,
            new LiveVoiceTranscriptRequest(
                "openai",
                token.ProviderSessionId,
                new[] { new LiveVoiceTranscriptSegment("candidate", 0, 1000, "Hello doctor") }),
            CancellationToken.None);
        var blankLabel = await rig.Service.PersistTranscriptAsync(
            session.UserId,
            session.SessionId,
            new LiveVoiceTranscriptRequest(
                string.Empty,
                token.ProviderSessionId,
                new[] { new LiveVoiceTranscriptSegment("candidate", 0, 1000, "Hello again doctor") }),
            CancellationToken.None);

        Assert.False(turn.Duplicate);
        Assert.Equal("realtime-gemini", transcript.Provider);
        Assert.Equal("realtime-gemini", blankLabel.Provider);
        var turnRow = await rig.Db.SpeakingPatientTurns.AsNoTracking()
            .SingleAsync(t => t.SessionId == session.SessionId && t.Role == "realtime_turn");
        using var turnJson = JsonDocument.Parse(turnRow.ResponseJson);
        Assert.Equal(LiveVoiceProviders.Gemini, turnJson.RootElement.GetProperty("provider").GetString());
        var stored = await rig.Db.SpeakingTranscripts.AsNoTracking()
            .Where(t => t.SpeakingSessionId == session.SessionId)
            .ToListAsync();
        Assert.All(stored, t => Assert.Equal("realtime-gemini", t.Provider));
    }

    [Fact]
    public async Task ALongUninterruptedSegment_IsClampedNotRejected_SoTheWholeTranscriptIsStillSaved()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        var token = await MintGeminiAsync(rig, session);

        // One speaker talking for 3 minutes merges into ONE segment on the client. Rejecting it would
        // fail the whole save, and a client that fails open on a 4xx would then drop the transcript.
        var saved = await rig.Service.PersistTranscriptAsync(
            session.UserId,
            session.SessionId,
            new LiveVoiceTranscriptRequest(
                "gemini",
                token.ProviderSessionId,
                new[]
                {
                    new LiveVoiceTranscriptSegment("candidate", 1_000, 181_000, "A long, uninterrupted explanation"),
                    new LiveVoiceTranscriptSegment("patient", int.MaxValue - 10, int.MaxValue, "A segment at the integer limit"),
                    new LiveVoiceTranscriptSegment("candidate", 5_000, 2_000, "A segment that ends before it starts"),
                }),
            CancellationToken.None);

        var stored = await rig.Db.SpeakingTranscripts.AsNoTracking().SingleAsync(t => t.Id == saved.TranscriptId);
        using var segments = JsonDocument.Parse(stored.SegmentsJson);
        var timing = segments.RootElement.EnumerateArray()
            .Select(segment => (segment.GetProperty("startMs").GetInt32(), segment.GetProperty("endMs").GetInt32()))
            .ToArray();
        Assert.Equal(
            new[] { (1_000, 121_000), (int.MaxValue - 10, int.MaxValue), (5_000, 5_000) },
            timing);
        Assert.True(saved.WordCount > 0);
    }

    [Fact]
    public async Task AProviderSessionFromCardA_IsRejectedForCardB_AndEachSessionKeepsOnlyItsOwnCard()
    {
        using var rig = LiveVoiceTestKit.Create();
        var cardA = await SeedAsync(rig, marker: "AAAA1111");
        var cardB = await SeedAsync(rig, marker: "BBBB2222");

        var offerA = await MintOpenAiAsync(rig, cardA);
        var offerB = await MintOpenAiAsync(rig, cardB);

        var instructionsA = InstructionsOf(rig.Handler.Requests[0]);
        var instructionsB = InstructionsOf(rig.Handler.Requests[1]);
        Assert.Contains("AAAA1111", instructionsA, StringComparison.Ordinal);
        Assert.DoesNotContain("BBBB2222", instructionsA, StringComparison.Ordinal);
        Assert.Contains("BBBB2222", instructionsB, StringComparison.Ordinal);
        Assert.DoesNotContain("AAAA1111", instructionsB, StringComparison.Ordinal);

        var turnForB = new LiveVoiceTurnRequest("openai", offerA.ProviderSessionId, "Hello", "Hi", "voice-turn:1", 1);
        var crossTurn = await Assert.ThrowsAsync<ApiException>(() =>
            rig.Service.PersistTurnAsync(cardB.UserId, cardB.SessionId, turnForB, CancellationToken.None));
        var crossTranscript = await Assert.ThrowsAsync<ApiException>(() =>
            rig.Service.PersistTranscriptAsync(
                cardB.UserId,
                cardB.SessionId,
                new LiveVoiceTranscriptRequest(
                    "openai",
                    offerA.ProviderSessionId,
                    new[] { new LiveVoiceTranscriptSegment("candidate", 0, 500, "Hello") }),
                CancellationToken.None));
        Assert.Equal("live_voice_provider_session_mismatch", crossTurn.ErrorCode);
        Assert.Equal(StatusCodes.Status409Conflict, crossTurn.StatusCode);
        Assert.Equal("live_voice_provider_session_mismatch", crossTranscript.ErrorCode);

        // The same client turn id persists independently in each session.
        var turnA = await rig.Service.PersistTurnAsync(
            cardA.UserId,
            cardA.SessionId,
            new LiveVoiceTurnRequest("openai", offerA.ProviderSessionId, "Hello A", "Hi A", "voice-turn:1", 1),
            CancellationToken.None);
        var turnB = await rig.Service.PersistTurnAsync(
            cardB.UserId,
            cardB.SessionId,
            new LiveVoiceTurnRequest("openai", offerB.ProviderSessionId, "Hello B", "Hi B", "voice-turn:1", 1),
            CancellationToken.None);
        Assert.False(turnA.Duplicate);
        Assert.False(turnB.Duplicate);
        Assert.Equal(cardA.SessionId, turnA.SessionId);
        Assert.Equal(cardB.SessionId, turnB.SessionId);

        // Each session's latest transcript holds only its own words.
        await rig.Service.PersistTranscriptAsync(
            cardA.UserId,
            cardA.SessionId,
            new LiveVoiceTranscriptRequest(
                "openai",
                offerA.ProviderSessionId,
                new[] { new LiveVoiceTranscriptSegment("candidate", 0, 500, "Only card A speaks here") }),
            CancellationToken.None);
        await rig.Service.PersistTranscriptAsync(
            cardB.UserId,
            cardB.SessionId,
            new LiveVoiceTranscriptRequest(
                "openai",
                offerB.ProviderSessionId,
                new[] { new LiveVoiceTranscriptSegment("candidate", 0, 500, "Only card B speaks here") }),
            CancellationToken.None);
        var latest = await rig.Db.SpeakingTranscripts.AsNoTracking().Where(t => t.IsLatest).ToListAsync();
        Assert.Equal(2, latest.Count);
        Assert.Contains("Only card A", latest.Single(t => t.SpeakingSessionId == cardA.SessionId).SegmentsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("card B", latest.Single(t => t.SpeakingSessionId == cardA.SessionId).SegmentsJson, StringComparison.Ordinal);
        Assert.Contains("Only card B", latest.Single(t => t.SpeakingSessionId == cardB.SessionId).SegmentsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("card A", latest.Single(t => t.SpeakingSessionId == cardB.SessionId).SegmentsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASessionIdTheServerNeverIssued_IsRejected_ForTurnsAndTranscript()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        await MintGeminiAsync(rig, session);

        var turn = await Assert.ThrowsAsync<ApiException>(() => rig.Service.PersistTurnAsync(
            session.UserId,
            session.SessionId,
            new LiveVoiceTurnRequest("gemini", "auth_tokens/forged", "Hello", "Hi", "voice-turn:1", 1),
            CancellationToken.None));
        var blank = await Assert.ThrowsAsync<ApiException>(() => rig.Service.PersistTurnAsync(
            session.UserId,
            session.SessionId,
            new LiveVoiceTurnRequest("gemini", " ", "Hello", "Hi", "voice-turn:1", 1),
            CancellationToken.None));

        Assert.Equal("live_voice_provider_session_mismatch", turn.ErrorCode);
        Assert.Equal(StatusCodes.Status409Conflict, turn.StatusCode);
        Assert.Equal("live_voice_provider_session_required", blank.ErrorCode);
        Assert.Equal(StatusCodes.Status400BadRequest, blank.StatusCode);
    }

    private static int MaxLength<T>(string property)
        => typeof(T).GetProperty(property)!
            .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.MaxLengthAttribute), false)
            .Cast<System.ComponentModel.DataAnnotations.MaxLengthAttribute>()
            .Single()
            .Length;
}
