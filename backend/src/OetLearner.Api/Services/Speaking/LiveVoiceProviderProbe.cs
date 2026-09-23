using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Speaking;

public sealed record LiveVoiceProviderProbeResult(
    bool Verified,
    string? Reason,
    DateTimeOffset CheckedAt);

public sealed class LiveVoiceProviderProbeState
{
    private readonly ConcurrentDictionary<string, LiveVoiceProviderProbeResult> results = new(StringComparer.Ordinal);

    public void Set(string provider, bool verified, string? reason)
        => results[provider] = new(verified, reason, DateTimeOffset.UtcNow);

    public LiveVoiceProviderProbeResult? Get(string provider)
        => results.TryGetValue(provider, out var result) ? result : null;

    public bool IsVerified(string provider)
        => Get(provider)?.Verified == true;

    /// <summary>True only when the primary realtime voice provider is both
    /// configured and probe-verified, i.e. the learner can actually start a
    /// live AI patient. Otherwise the client uses the recorder fallback.</summary>
    public bool IsLiveVoiceAvailable(LiveVoiceOptions? options)
    {
        if (options is null) return false;
        var provider = LiveVoiceOptions.NormalizeProvider(options.PrimaryProvider);
        return provider.Length > 0 && options.IsConfigured(provider) && IsVerified(provider);
    }
}

/// <summary>
/// Verifies the configured production Live API models against the actual
/// provider account before a browser may activate its microphone. A catalog
/// or network failure leaves that provider unavailable, never mocked or text-only.
/// </summary>
public sealed class LiveVoiceProviderProbe(
    IHttpClientFactory httpClientFactory,
    IOptions<LiveVoiceOptions> options,
    LiveVoiceProviderProbeState state,
    ILogger<LiveVoiceProviderProbe> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProbeConfiguredProvidersAsync(stoppingToken);
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task ProbeConfiguredProvidersAsync(CancellationToken ct)
    {
        var configured = options.Value;
        if (configured.IsOpenAiConfigured)
        {
            await ProbeOpenAiAsync(configured, ct);
        }
        if (configured.IsGeminiConfigured)
        {
            await ProbeGeminiAsync(configured, ct);
        }
    }

    private async Task ProbeOpenAiAsync(LiveVoiceOptions configured, CancellationToken ct)
    {
        var endpoint = $"{configured.OpenAiModelsBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(configured.OpenAiModel)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configured.OpenAiApiKey);
        await ProbeAsync(
            LiveVoiceProviders.OpenAi,
            configured.OpenAiModel,
            request,
            (body, statusOk) => statusOk
                && HasModelId(body, configured.OpenAiModel),
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
            configured.GeminiModel,
            request,
            (body, statusOk) => statusOk
                && HasModelId(body, configured.GeminiModel),
            ct);
    }

    private async Task ProbeAsync(
        string provider,
        string model,
        HttpRequestMessage request,
        Func<JsonDocument, bool, bool> verifier,
        CancellationToken ct)
    {
        try
        {
            using var response = await httpClientFactory.CreateClient("LiveVoiceProvider")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(body);
            var verified = verifier(document, response.IsSuccessStatusCode);
            state.Set(provider, verified, verified ? null : $"http_{(int)response.StatusCode}_model_unverified");
            if (verified)
            {
                logger.LogInformation("Live voice provider probe verified {Provider} model {Model}.", provider, model);
            }
            else
            {
                logger.LogWarning("Live voice provider probe did not verify {Provider} model {Model} (HTTP {Status}).", provider, model, (int)response.StatusCode);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            state.Set(provider, false, "probe_failed");
            logger.LogWarning(ex, "Live voice provider probe failed for {Provider} model {Model}.", provider, model);
        }
    }

    private static bool HasModelId(JsonDocument document, string expectedModel)
    {
        if (!document.RootElement.TryGetProperty("id", out var id)
            && !document.RootElement.TryGetProperty("name", out id))
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
