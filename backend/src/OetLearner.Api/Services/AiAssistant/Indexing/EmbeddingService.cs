using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.AiAssistant.Indexing;

/// <summary>
/// Generates embeddings via the OpenAI-compatible embeddings endpoint.
/// Falls back to a deterministic hash-based local embedding when no
/// embedding provider is configured.
///
/// Docker/Migration Notes:
/// - PostgreSQL needs pgvector extension: CREATE EXTENSION IF NOT EXISTS vector;
/// - Docker image should be pgvector/pgvector:pg17 instead of postgres:17-alpine
/// - NuGet package needed: Pgvector.EntityFrameworkCore
/// - The embedding column type in EF migration should be: .HasColumnType("vector(1536)")
/// </summary>
public sealed class EmbeddingService : IEmbeddingService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<AiProviderOptions> _options;
    private readonly IRuntimeSettingsProvider _settingsProvider;
    private readonly ILogger<EmbeddingService> _logger;
    private readonly IDirectAiCallRecorder? _usageRecorder;

    private const int DefaultDimension = 1536;
    private const int BatchSize = 20;

    public EmbeddingService(
        IHttpClientFactory httpClientFactory,
        IOptions<AiProviderOptions> options,
        IRuntimeSettingsProvider settingsProvider,
        ILogger<EmbeddingService> logger,
        IDirectAiCallRecorder? usageRecorder = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _usageRecorder = usageRecorder;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new float[DefaultDimension];

        var results = await EmbedBatchAsync(new[] { text }, ct);
        return results[0];
    }

    public async Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (texts == null || texts.Count == 0)
            return new List<float[]>();

        var opts = _options.Value;
        // DB-over-env: BaseUrl + embedding model come from the merged runtime
        // view (admin-configurable). The provider API key stays env/registry-
        // managed via AiProviderOptions — never overridden from RuntimeSettings.
        var effective = await _settingsProvider.GetAsync(ct);
        var baseUrl = effective.AiGateway.BaseUrl;
        var embeddingModel = effective.AiAssistant.EmbeddingModel;
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            _logger.LogWarning("No embedding provider configured — falling back to local hash-based embeddings.");
            return texts.Select(t => GenerateLocalEmbedding(t)).ToList();
        }

        var allEmbeddings = new List<float[]>(texts.Count);

        for (int i = 0; i < texts.Count; i += BatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = texts.Skip(i).Take(BatchSize).ToList();
            try
            {
                var batchResults = await CallEmbeddingApiAsync(batch, baseUrl, opts.ApiKey, embeddingModel, ct);
                allEmbeddings.AddRange(batchResults);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Embedding API call failed for batch starting at index {Index}. Falling back to local embeddings.", i);
                allEmbeddings.AddRange(batch.Select(t => GenerateLocalEmbedding(t)));
            }
        }

        return allEmbeddings;
    }

    private async Task<List<float[]>> CallEmbeddingApiAsync(
        List<string> texts, string baseUrl, string apiKey, string model, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("AiEmbedding");
        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        client.BaseAddress = new Uri(normalizedBaseUrl + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            model,
            input = texts,
            dimensions = DefaultDimension
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "embeddings")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        DirectAiOperationLease? lease = null;
        if (_usageRecorder is not null)
        {
            var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            lease = await _usageRecorder.BeginOperationAsync(new DirectAiOperationRequest
            {
                FeatureCode = AiFeatureCodes.EmbeddingsGenerate,
                Module = "embeddings",
                ResourceId = $"{model}:{requestHash}",
                ResourceType = "embedding_batch",
                RequestHash = requestHash,
                OperationClass = AiOperationClass.AdminBatch,
                AllowRetryAfterFailure = true,
            }, ct);
            if (!lease.CanProceed)
                throw new InvalidOperationException($"Embeddings are unavailable ({lease.Reason}).");
        }

        var startedAt = DateTimeOffset.UtcNow;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        HttpResponseMessage response;
        if (_usageRecorder is not null && lease is not null)
        {
            response = await DirectAiOperationReconciler.RunAsync(
                _usageRecorder,
                lease,
                "openai-embeddings",
                async () => await client.SendAsync(request, ct),
                ct);
        }
        else
        {
            response = await client.SendAsync(request, ct);
        }

        if (!response.IsSuccessStatusCode)
        {
            sw.Stop();
            if (_usageRecorder is not null && lease is not null)
            {
                await _usageRecorder.RecordFailureAsync(
                    new AiUsageContext(
                        UserId: null, AuthAccountId: null, TenantId: null,
                        FeatureCode: AiFeatureCodes.EmbeddingsGenerate,
                        RulebookVersion: null, PromptTemplateId: null,
                        SystemPrompt: null, UserPrompt: null, StartedAt: startedAt),
                    "openai-embeddings", model, AiCallOutcome.ProviderError,
                    $"http_{(int)response.StatusCode}", "embedding_http_error",
                    (int)sw.ElapsedMilliseconds, "embeddings.failed", ct,
                    operationId: lease.OperationId, attemptNumber: lease.AttemptNumber);
            }
            response.EnsureSuccessStatusCode();
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);

        var data = doc.RootElement.GetProperty("data");
        var results = new List<float[]>(texts.Count);

        foreach (var item in data.EnumerateArray().OrderBy(e => e.GetProperty("index").GetInt32()))
        {
            var embeddingArray = item.GetProperty("embedding");
            var embedding = new float[embeddingArray.GetArrayLength()];
            int idx = 0;
            foreach (var val in embeddingArray.EnumerateArray())
            {
                embedding[idx++] = val.GetSingle();
            }
            results.Add(embedding);
        }

        sw.Stop();
        if (_usageRecorder is not null && lease is not null)
        {
            var approxTokens = texts.Sum(t => Math.Max(1, t.Length / 4));
            var costUsd = Math.Max(0.00001m, approxTokens * 0.00000002m);
            var usageId = await _usageRecorder.RecordSuccessAsync(
                new AiUsageContext(
                    UserId: null, AuthAccountId: null, TenantId: null,
                    FeatureCode: AiFeatureCodes.EmbeddingsGenerate,
                    RulebookVersion: null, PromptTemplateId: null,
                    SystemPrompt: null, UserPrompt: null, StartedAt: startedAt),
                "openai-embeddings", model, usage: null,
                (int)sw.ElapsedMilliseconds, $"embeddings.n={texts.Count}", costUsd, ct,
                operationId: lease.OperationId, attemptNumber: lease.AttemptNumber);
            if (lease.OperationId is not null)
            {
                await _usageRecorder.CompleteOperationAsync(
                    lease.OperationId,
                    AiOperationState.Completed,
                    usageId,
                    "openai-embeddings",
                    model,
                    CancellationToken.None,
                    lease.BudgetReservation);
            }
        }

        return results;
    }

    /// <summary>
    /// Generates a deterministic local embedding using SHA-512 hash expanded
    /// to the target dimension. Not semantically meaningful but provides a
    /// stable vector for deduplication and basic distance calculations.
    /// </summary>
    private static float[] GenerateLocalEmbedding(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new float[DefaultDimension];

        var embedding = new float[DefaultDimension];
        var bytes = Encoding.UTF8.GetBytes(text.ToLowerInvariant().Trim());

        // Generate enough hash bytes to fill the embedding
        int offset = 0;
        int iteration = 0;
        while (offset < DefaultDimension)
        {
            var input = new byte[bytes.Length + 4];
            Buffer.BlockCopy(bytes, 0, input, 0, bytes.Length);
            BitConverter.GetBytes(iteration).CopyTo(input, bytes.Length);

            var hash = SHA256.HashData(input);
            for (int i = 0; i < hash.Length && offset < DefaultDimension; i += 4)
            {
                // Convert 4 bytes to float in [-1, 1] range
                var intVal = BitConverter.ToInt32(hash, i);
                embedding[offset++] = intVal / (float)int.MaxValue;
            }
            iteration++;
        }

        // Normalize to unit vector
        var magnitude = MathF.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
                embedding[i] /= magnitude;
        }

        return embedding;
    }
}
