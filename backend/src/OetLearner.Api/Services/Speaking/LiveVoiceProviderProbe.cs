using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Speaking;

public sealed record LiveVoiceProviderProbeResult(
    bool Verified,
    string? Reason,
    DateTimeOffset CheckedAt);

public enum LiveVoiceBreakerTransition
{
    None,
    Opened,
    Closed,
}

/// <summary>What one recorded outcome did to a provider's breaker. Only a transition is worth an audit row.</summary>
public readonly record struct LiveVoiceBreakerChange(
    LiveVoiceBreakerTransition Transition,
    DateTimeOffset? OpenUntil,
    int ConsecutiveFailures);

public sealed record LiveVoiceCatalogHealth(bool Verified, string? Reason, DateTimeOffset? CheckedAt);

public sealed record LiveVoiceBreakerHealth(string State, DateTimeOffset? OpenUntil, int ConsecutiveFailures);

public sealed record LiveVoiceLastFailure(
    string Class,
    int? HttpStatus,
    string? ProviderType,
    string? ProviderCode,
    DateTimeOffset At);

public sealed record LiveVoiceProviderHealth(
    string Provider,
    string Model,
    bool Configured,
    bool Primary,
    LiveVoiceCatalogHealth Catalog,
    LiveVoiceBreakerHealth Breaker,
    LiveVoiceLastFailure? LastFailure,
    DateTimeOffset? LastSuccessAt,
    long Attempts,
    long Failures,
    IReadOnlyDictionary<string, long> FailuresByClass);

/// <summary>Admin view of live voice health: never keys, URLs, tokens or provider messages.</summary>
public sealed record LiveVoiceHealthSnapshot(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<string> CandidateOrder,
    IReadOnlyList<LiveVoiceProviderHealth> Providers);

/// <summary>
/// Per-process live voice provider health. Two independent signals per provider:
/// <list type="bullet">
/// <item>the catalog probe (<see cref="Set"/>): the key works and the model is served;</item>
/// <item>a circuit breaker fed by REAL session-creation outcomes (<see cref="RecordFailure"/> /
/// <see cref="RecordSuccess"/>). A passing catalog probe never closes a breaker that real failures
/// opened, because <c>GET /models</c> succeeds while session creation is rate limited.</item>
/// </list>
/// The breaker is closed, open, or in probation (open time elapsed: the first real failure
/// re-opens it, the first real success closes it). It lives in memory on purpose: exactly one API
/// process serves learner traffic, and a restart costs at most one fast failed attempt per broken
/// provider. ponytail: no single-probe gate in probation, add a lease if concurrency grows.
/// </summary>
public sealed class LiveVoiceProviderProbeState(TimeProvider? clock = null)
{
    internal static readonly TimeSpan HardOpenDuration = TimeSpan.FromSeconds(600);
    internal static readonly TimeSpan SoftOpenDuration = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan SoftFailureWindow = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan RateLimitDefault = TimeSpan.FromSeconds(30);
    internal const int SoftFailureThreshold = 2;

    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, LiveVoiceProviderProbeResult> results = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ProviderHealth> health = new(StringComparer.Ordinal);

    private sealed class ProviderHealth
    {
        public readonly object Gate = new();
        public int Consecutive;
        public DateTimeOffset? WindowFailureAt;
        public DateTimeOffset? OpenUntil;
        public DateTimeOffset? LastSuccessAt;
        public long Attempts;
        public long Failures;
        public readonly Dictionary<string, long> ByClass = new(StringComparer.Ordinal);
        public LiveVoiceLastFailure? LastFailure;
    }

    private ProviderHealth For(string provider) => health.GetOrAdd(provider, static _ => new ProviderHealth());

    // ── Catalog probe ────────────────────────────────────────────────

    public void Set(string provider, bool verified, string? reason)
        => results[provider] = new(verified, reason, time.GetUtcNow());

    public LiveVoiceProviderProbeResult? Get(string provider)
        => results.TryGetValue(provider, out var result) ? result : null;

    public bool IsVerified(string provider)
        => Get(provider)?.Verified == true;

    // ── Breaker ──────────────────────────────────────────────────────

    /// <summary>True while the breaker is open. In probation (open time elapsed) it is not.</summary>
    public bool IsOpen(string provider)
    {
        var key = LiveVoiceOptions.NormalizeProvider(provider);
        if (key.Length == 0 || !health.TryGetValue(key, out var h)) return false;
        lock (h.Gate)
        {
            return h.OpenUntil is { } until && until > time.GetUtcNow();
        }
    }

    /// <summary>closed | open | probation.</summary>
    public string BreakerState(string provider)
    {
        var key = LiveVoiceOptions.NormalizeProvider(provider);
        if (key.Length == 0 || !health.TryGetValue(key, out var h)) return "closed";
        lock (h.Gate)
        {
            return StateOf(h, time.GetUtcNow());
        }
    }

    private static string StateOf(ProviderHealth h, DateTimeOffset now)
        => h.OpenUntil is not { } until ? "closed" : until > now ? "open" : "probation";

    /// <summary>
    /// Records a failed REAL session creation. Quota and auth open the breaker for 10 minutes; a
    /// rate limit for its Retry-After (5..120 s, 30 s when the provider gave none); server,
    /// overload, network and unknown failures for 60 s once two land within 60 s (or at once in
    /// probation). A malformed request (<see cref="AiProviderErrorClass.InvalidRequest"/>) is
    /// counted but never opens the breaker: one browser's bad SDP must not take the provider
    /// away from everyone.
    /// </summary>
    public LiveVoiceBreakerChange RecordFailure(
        string provider,
        AiProviderErrorClass errorClass,
        int? httpStatus = null,
        string? providerType = null,
        string? providerCode = null,
        TimeSpan? retryAfter = null)
    {
        var key = LiveVoiceOptions.NormalizeProvider(provider);
        if (key.Length == 0) return default;
        var h = For(key);
        lock (h.Gate)
        {
            var now = time.GetUtcNow();
            var wasOpen = h.OpenUntil is { } current && current > now;
            var probation = h.OpenUntil is not null && !wasOpen;

            var classCode = errorClass.ToCode();
            h.Attempts++;
            h.Failures++;
            h.ByClass[classCode] = h.ByClass.GetValueOrDefault(classCode) + 1;
            h.LastFailure = new LiveVoiceLastFailure(classCode, httpStatus, SafeToken(providerType), SafeToken(providerCode), now);

            TimeSpan? openFor = null;
            if (errorClass != AiProviderErrorClass.InvalidRequest)
            {
                if (!wasOpen && !probation && h.WindowFailureAt is { } last && now - last > SoftFailureWindow)
                {
                    h.Consecutive = 0;
                }
                h.Consecutive++;
                h.WindowFailureAt = now;

                TimeSpan? softOpen = probation || h.Consecutive >= SoftFailureThreshold ? SoftOpenDuration : null;
                openFor = errorClass switch
                {
                    AiProviderErrorClass.QuotaExhausted or AiProviderErrorClass.Auth => HardOpenDuration,
                    AiProviderErrorClass.RateLimited => TimeSpan.FromSeconds(
                        Math.Clamp((retryAfter ?? RateLimitDefault).TotalSeconds, 5, 120)),
                    _ => softOpen,
                };
            }

            if (openFor is { } duration)
            {
                var until = now + duration;
                if (!wasOpen || until > h.OpenUntil)
                {
                    h.OpenUntil = until;
                }
            }

            var opened = !wasOpen && h.OpenUntil is { } openUntil && openUntil > now;
            return new LiveVoiceBreakerChange(
                opened ? LiveVoiceBreakerTransition.Opened : LiveVoiceBreakerTransition.None,
                h.OpenUntil,
                h.Consecutive);
        }
    }

    /// <summary>Records a successful REAL session creation: closes the breaker and resets the failure run.</summary>
    public LiveVoiceBreakerChange RecordSuccess(string provider)
    {
        var key = LiveVoiceOptions.NormalizeProvider(provider);
        if (key.Length == 0) return default;
        var h = For(key);
        lock (h.Gate)
        {
            h.Attempts++;
            h.Consecutive = 0;
            h.WindowFailureAt = null;
            h.LastSuccessAt = time.GetUtcNow();
            var wasSet = h.OpenUntil is not null;
            h.OpenUntil = null;
            return new LiveVoiceBreakerChange(
                wasSet ? LiveVoiceBreakerTransition.Closed : LiveVoiceBreakerTransition.None,
                null,
                0);
        }
    }

    /// <summary>Admin reset: closes the breaker without a success. Returns true when it was open or in probation.</summary>
    public bool Reset(string provider)
    {
        var key = LiveVoiceOptions.NormalizeProvider(provider);
        if (key.Length == 0) return false;
        var h = For(key);
        lock (h.Gate)
        {
            var wasSet = h.OpenUntil is not null;
            h.OpenUntil = null;
            h.Consecutive = 0;
            h.WindowFailureAt = null;
            return wasSet;
        }
    }

    // ── Availability ─────────────────────────────────────────────────

    /// <summary>
    /// The providers a new session may try, in order: primary first, each configured,
    /// catalog-verified and not open (closed or in probation).
    /// </summary>
    public IReadOnlyList<string> Candidates(LiveVoiceOptions? options)
    {
        if (options is null) return Array.Empty<string>();
        var candidates = new List<string>(2);
        foreach (var provider in options.ProviderOrder())
        {
            if (options.IsConfigured(provider) && IsVerified(provider) && !IsOpen(provider))
            {
                candidates.Add(provider);
            }
        }
        return candidates;
    }

    /// <summary>True when at least one provider can start a live AI patient; otherwise the client
    /// uses the recorder fallback.</summary>
    public bool IsLiveVoiceAvailable(LiveVoiceOptions? options)
        => options is not null && Candidates(options).Count > 0;

    public LiveVoiceHealthSnapshot Snapshot(LiveVoiceOptions options)
    {
        var order = options.ProviderOrder();
        var providers = new List<LiveVoiceProviderHealth>(LiveVoiceProviders.All.Count);
        foreach (var provider in LiveVoiceProviders.All)
        {
            var catalog = Get(provider);
            var h = For(provider);
            lock (h.Gate)
            {
                providers.Add(new LiveVoiceProviderHealth(
                    Provider: provider,
                    Model: provider == LiveVoiceProviders.OpenAi ? options.OpenAiModel : options.GeminiModel,
                    Configured: options.IsConfigured(provider),
                    Primary: order[0] == provider,
                    Catalog: new LiveVoiceCatalogHealth(catalog?.Verified == true, catalog?.Reason, catalog?.CheckedAt),
                    Breaker: new LiveVoiceBreakerHealth(StateOf(h, time.GetUtcNow()), h.OpenUntil, h.Consecutive),
                    LastFailure: h.LastFailure,
                    LastSuccessAt: h.LastSuccessAt,
                    Attempts: h.Attempts,
                    Failures: h.Failures,
                    FailuresByClass: new Dictionary<string, long>(h.ByClass, StringComparer.Ordinal)));
            }
        }
        return new LiveVoiceHealthSnapshot(time.GetUtcNow(), Candidates(options), providers);
    }

    /// <summary>
    /// A short provider-defined token (error type or code) that is safe to log and show an admin:
    /// letters, digits and <c>_ . : -</c> only, at most 64 characters. Anything else is dropped.
    /// </summary>
    internal static string? SafeToken(string? value)
        => value is { Length: > 0 and <= 64 }
            && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or ':' or '-')
                ? value
                : null;
}

/// <summary>
/// Verifies the configured production Live API models against the actual
/// provider account before a browser may activate its microphone. A catalog
/// or network failure leaves that provider unavailable, never mocked or text-only.
/// The probe is the catalog gate only; real session-creation outcomes drive the
/// breaker in <see cref="LiveVoiceProviderProbeState"/>.
/// </summary>
public sealed class LiveVoiceProviderProbe(
    IHttpClientFactory httpClientFactory,
    IOptions<LiveVoiceOptions> options,
    LiveVoiceProviderProbeState state,
    ILogger<LiveVoiceProviderProbe> logger) : BackgroundService
{
    internal static readonly TimeSpan FastInterval = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan SteadyInterval = TimeSpan.FromMinutes(5);

    /// <summary>Registers the health state always and the probe loop only outside the ai-worker,
    /// which serves no learner routes and never starts a live voice session.</summary>
    public static IServiceCollection Register(IServiceCollection services, bool isWorker)
    {
        services.TryAddSingleton<LiveVoiceProviderProbeState>();
        if (!isWorker)
        {
            services.AddHostedService<LiveVoiceProviderProbe>();
        }
        return services;
    }

    /// <summary>Re-check every minute while a configured provider is unverified or open (so
    /// recovery is noticed quickly), otherwise every five minutes.</summary>
    internal static TimeSpan NextProbeDelay(LiveVoiceOptions configured, LiveVoiceProviderProbeState state)
        => LiveVoiceProviders.All.Any(p => configured.IsConfigured(p) && (!state.IsVerified(p) || state.IsOpen(p)))
            ? FastInterval
            : SteadyInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProbeConfiguredProvidersAsync(stoppingToken);
            try
            {
                await Task.Delay(NextProbeDelay(options.Value, state), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    internal Task ProbeConfiguredProvidersAsync(CancellationToken ct)
    {
        var configured = options.Value;
        var probes = new List<Task>(2);
        if (configured.IsOpenAiConfigured)
        {
            probes.Add(ProbeOpenAiAsync(configured, ct));
        }
        if (configured.IsGeminiConfigured)
        {
            probes.Add(ProbeGeminiAsync(configured, ct));
        }
        // In parallel: one slow provider must not delay verifying the other at startup.
        return Task.WhenAll(probes);
    }

    private async Task ProbeOpenAiAsync(LiveVoiceOptions configured, CancellationToken ct)
    {
        var endpoint = $"{configured.OpenAiModelsBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(configured.OpenAiModel)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configured.OpenAiApiKey);
        await ProbeAsync(
            LiveVoiceProviders.OpenAi,
            AiProviderErrorDialect.OpenAi,
            configured.OpenAiModel,
            configured.OpenAiApiKey,
            request,
            ct);
    }

    private async Task ProbeGeminiAsync(LiveVoiceOptions configured, CancellationToken ct)
    {
        var model = configured.GeminiModel.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? configured.GeminiModel["models/".Length..]
            : configured.GeminiModel;
        var endpoint = $"{configured.GeminiModelsBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(model)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.TryAddWithoutValidation("x-goog-api-key", configured.GeminiApiKey);
        await ProbeAsync(
            LiveVoiceProviders.Gemini,
            AiProviderErrorDialect.Gemini,
            configured.GeminiModel,
            configured.GeminiApiKey,
            request,
            ct);
    }

    private async Task ProbeAsync(
        string provider,
        AiProviderErrorDialect dialect,
        string model,
        string apiKey,
        HttpRequestMessage request,
        CancellationToken ct)
    {
        try
        {
            using var response = await httpClientFactory.CreateClient("LiveVoiceProvider")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                var error = AiProviderErrorParser.Parse(dialect, status, body, response.Headers, apiKey, retainProviderText: false);
                ApplyFailure(provider, model, error.Class, status);
                return;
            }

            switch (ReadVerdict(body, model))
            {
                case CatalogVerdict.Verified:
                    state.Set(provider, true, null);
                    logger.LogInformation("Live voice provider probe verified {Provider} model {Model}.", provider, model);
                    break;
                case CatalogVerdict.ModelMissing:
                    state.Set(provider, false, $"http_{status}_model_unverified");
                    logger.LogWarning("Live voice provider probe did not verify {Provider} model {Model} (HTTP {Status}).", provider, model, status);
                    break;
                default:
                    // A 200 that is not JSON says nothing about the model: treat it like any transient fault.
                    ApplyFailure(provider, model, AiProviderErrorClass.ServerError, status);
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ApplyFailure(provider, model, AiProviderErrorClass.Network, null);
            logger.LogWarning(ex, "Live voice provider probe failed for {Provider} model {Model}.", provider, model);
        }
    }

    /// <summary>
    /// Transient faults (rate limit, overload, server error, network) keep an already verified
    /// provider verified: a blip on <c>GET /models</c> must not drop a working provider for five
    /// minutes. A first-ever transient failure stays unverified (fail closed). Auth, quota,
    /// invalid-request (for example the model is gone) and anything unclassified verify nothing.
    /// </summary>
    private void ApplyFailure(string provider, string model, AiProviderErrorClass errorClass, int? status)
    {
        var transient = errorClass is AiProviderErrorClass.RateLimited
            or AiProviderErrorClass.Overloaded
            or AiProviderErrorClass.ServerError
            or AiProviderErrorClass.Network;
        var classCode = errorClass.ToCode();
        if (transient && state.IsVerified(provider))
        {
            state.Set(provider, true, $"probe_transient_{classCode}");
            logger.LogWarning(
                "Live voice provider probe for {Provider} model {Model} hit a transient {ErrorClass} fault (HTTP {Status}); keeping it verified.",
                provider, model, classCode, status);
            return;
        }

        state.Set(provider, false, status is { } code ? $"http_{code}_{classCode}" : $"probe_{classCode}");
        logger.LogWarning(
            "Live voice provider probe did not verify {Provider} model {Model} ({ErrorClass}, HTTP {Status}).",
            provider, model, classCode, status);
    }

    private enum CatalogVerdict
    {
        Verified,
        ModelMissing,
        Invalid,
    }

    private static CatalogVerdict ReadVerdict(string body, string expectedModel)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return HasModelId(document, expectedModel) ? CatalogVerdict.Verified : CatalogVerdict.ModelMissing;
        }
        catch (JsonException)
        {
            return CatalogVerdict.Invalid;
        }
    }

    private static bool HasModelId(JsonDocument document, string expectedModel)
    {
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || (!document.RootElement.TryGetProperty("id", out var id)
                && !document.RootElement.TryGetProperty("name", out id))
            || id.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        var actual = id.GetString();
        var normalizedActual = actual?.StartsWith("models/", StringComparison.OrdinalIgnoreCase) == true
            ? actual["models/".Length..]
            : actual;
        var normalizedExpected = expectedModel.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? expectedModel["models/".Length..]
            : expectedModel;
        return string.Equals(normalizedActual, normalizedExpected, StringComparison.OrdinalIgnoreCase);
    }
}
