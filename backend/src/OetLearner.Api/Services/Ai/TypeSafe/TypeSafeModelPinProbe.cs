using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Ai.TypeSafe;

/// <summary>
/// Phase-2 hardening — model-pin drift probe. On startup (and ONLY when
/// <c>TypeSafe:Enabled</c> is on, so tests and dormant deployments never make
/// a network call) it asks <c>GET /v1/models</c> and warns when the pinned
/// <c>TypeSafe:Model</c> is no longer listed for this account. Judgment
/// thresholds are tuned against a pinned version; if TypeSafe retires that
/// version, every call would start failing 404/422 — this turns that silent
/// time-bomb into a startup warning in the logs. Fail-soft by design: any
/// probe failure is logged and never blocks startup or judging.
/// </summary>
public sealed class TypeSafeModelPinProbe(
    IHttpClientFactory httpClientFactory,
    IOptions<TypeSafeOptions> options,
    ILogger<TypeSafeModelPinProbe> logger,
    IServiceScopeFactory? scopeFactory = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
            return;

        try
        {
            var apiKey = opts.ApiKey;
            if (scopeFactory is not null)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                apiKey = await scope.ServiceProvider.GetRequiredService<IAiProviderRegistry>()
                    .GetPlatformKeyAsync(TypeSafeOptions.ProviderCode, stoppingToken) ?? apiKey;
            }
            if (string.IsNullOrWhiteSpace(apiKey))
                return;

            var client = httpClientFactory.CreateClient(TypeSafeJudgmentClient.HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{opts.BaseUrl.TrimEnd('/')}/v1/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await client.SendAsync(request, stoppingToken);
            var body = await response.Content.ReadAsStringAsync(stoppingToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "TypeSafe model-pin probe could not list models (HTTP {Status}); the pinned model {Model} is unverified.",
                    (int)response.StatusCode, opts.Model);
                return;
            }

            using var doc = JsonDocument.Parse(body);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in models.EnumerateArray())
                {
                    if (m.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && n.GetString() is { } name)
                        names.Add(name);
                }
            }

            if (names.Count == 0)
            {
                logger.LogWarning("TypeSafe model-pin probe found no model entries; the pinned model {Model} is unverified.", opts.Model);
                return;
            }

            if (!names.Contains(opts.Model))
            {
                logger.LogWarning(
                    "TypeSafe model-pin DRIFT: pinned model {Model} is not in the account's model list ({Listed}). " +
                    "Judgment thresholds are tuned against the pin — bump TypeSafe:Model deliberately and re-run tools/typesafe/calibrate.mjs.",
                    opts.Model, string.Join(", ", names.OrderBy(n => n, StringComparer.Ordinal)));
            }
            else
            {
                logger.LogInformation("TypeSafe model-pin verified: {Model} is served to this account.", opts.Model);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown — nothing to verify.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TypeSafe model-pin probe failed; the pinned model {Model} is unverified.", options.Value.Model);
        }
    }
}
