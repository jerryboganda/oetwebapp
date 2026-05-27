using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingExemplarSimilarity(
    Guid ExemplarId,
    double Similarity,
    string LetterType,
    string Profession);

public interface IWritingExemplarEmbeddingService
{
    /// <summary>Embed an exemplar's letter content and persist the vector. Idempotent
    /// — returns the existing embedding when one is current.</summary>
    Task<float[]> EmbedExemplarAsync(string userId, Guid exemplarId, CancellationToken ct);

    /// <summary>Embed a scenario's case notes for similarity matching. Idempotent.</summary>
    Task<float[]> EmbedScenarioAsync(string userId, Guid scenarioId, CancellationToken ct);

    /// <summary>Find the closest published exemplars to a scenario, optionally
    /// constrained to a (profession, letterType) subset. Returns ordered by
    /// descending cosine similarity.</summary>
    Task<IReadOnlyList<WritingExemplarSimilarity>> FindClosestAsync(
        string userId,
        Guid scenarioId,
        int take = 5,
        CancellationToken ct = default);

    /// <summary>One-shot backfill: walk every <see cref="WritingExemplarEmbedding"/>
    /// and <see cref="WritingScenarioEmbedding"/> row with a non-empty
    /// <c>EmbeddingJson</c> but a null pgvector <c>Embedding</c> column, parse
    /// the JSON and populate the native vector. Idempotent — re-running it is
    /// a no-op once every row is migrated.
    /// Returns the count of rows backfilled per table (exemplar, scenario).
    /// Pre-existing JSON remains untouched so the legacy cosine path keeps
    /// working until callers fully switch over.</summary>
    Task<(int Exemplars, int Scenarios)> BackfillFromJsonAsync(CancellationToken ct);
}

public sealed class WritingExemplarEmbeddingService(
    LearnerDbContext db,
    IAiGatewayService aiGateway,
    TimeProvider clock,
    ILogger<WritingExemplarEmbeddingService> logger) : IWritingExemplarEmbeddingService
{
    private const int VectorDimensions = 1536;
    private const string ModelId = "text-embedding-3-small";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex VectorRegex = new("-?\\d+(\\.\\d+)?", RegexOptions.Compiled);

    public async Task<float[]> EmbedExemplarAsync(string userId, Guid exemplarId, CancellationToken ct)
    {
        var exemplar = await db.WritingExemplars.AsNoTracking().FirstOrDefaultAsync(e => e.Id == exemplarId, ct)
            ?? throw ApiException.NotFound("writing_exemplar_not_found", "Exemplar was not found.");
        var existing = await db.WritingExemplarEmbeddings.FirstOrDefaultAsync(e => e.ExemplarId == exemplarId, ct);
        if (existing is not null && IsValid(existing.EmbeddingJson))
        {
            return DeserializeVector(existing.EmbeddingJson);
        }
        var vector = await CallEmbedAsync(exemplar.LetterContent, userId, ct);
        await PersistExemplarEmbeddingAsync(exemplarId, vector, ct);
        return vector;
    }

    public async Task<float[]> EmbedScenarioAsync(string userId, Guid scenarioId, CancellationToken ct)
    {
        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId, ct)
            ?? throw ApiException.NotFound("writing_scenario_not_found", "Scenario was not found.");
        var existing = await db.WritingScenarioEmbeddings.FirstOrDefaultAsync(e => e.ScenarioId == scenarioId, ct);
        if (existing is not null && IsValid(existing.EmbeddingJson))
        {
            return DeserializeVector(existing.EmbeddingJson);
        }
        var input = string.IsNullOrWhiteSpace(scenario.Title)
            ? scenario.CaseNotesMarkdown
            : $"{scenario.Title}\n\n{scenario.CaseNotesMarkdown}";
        var vector = await CallEmbedAsync(input, userId, ct);
        await PersistScenarioEmbeddingAsync(scenarioId, vector, ct);
        return vector;
    }

    public async Task<IReadOnlyList<WritingExemplarSimilarity>> FindClosestAsync(
        string userId,
        Guid scenarioId,
        int take = 5,
        CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId, ct);
        if (scenario is null) return Array.Empty<WritingExemplarSimilarity>();

        float[] scenarioVector;
        try
        {
            scenarioVector = await EmbedScenarioAsync(userId, scenarioId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Scenario embed failed; falling back to letter-type filter only.");
            scenarioVector = Array.Empty<float>();
        }

        var candidates = await db.WritingExemplars.AsNoTracking()
            .Where(e => e.Status == "published"
                        && e.LetterType == scenario.LetterType
                        && e.Profession == scenario.Profession)
            .Select(e => new { e.Id, e.LetterType, e.Profession })
            .ToListAsync(ct);
        if (candidates.Count == 0) return Array.Empty<WritingExemplarSimilarity>();

        var candidateIds = candidates.Select(c => c.Id).ToList();

        // Prefer pgvector cosine when running on Postgres AND we already have a
        // populated scenario vector. The HNSW index on Embedding turns this
        // into an O(log N) approximate nearest-neighbour scan; falling back to
        // the legacy C# cosine over JSON keeps the SQLite/in-memory test
        // harness working unchanged.
        if (scenarioVector.Length == VectorDimensions && db.Database.IsNpgsql())
        {
            try
            {
                var query = new Vector(scenarioVector);
                var pgResults = await db.WritingExemplarEmbeddings.AsNoTracking()
                    .Where(e => candidateIds.Contains(e.ExemplarId) && e.Embedding != null)
                    .OrderBy(e => e.Embedding!.CosineDistance(query))
                    .Take(take)
                    .Select(e => new
                    {
                        e.ExemplarId,
                        Distance = e.Embedding!.CosineDistance(query),
                    })
                    .ToListAsync(ct);

                if (pgResults.Count > 0)
                {
                    var byId = candidates.ToDictionary(c => c.Id);
                    return pgResults
                        .Select(r =>
                        {
                            var c = byId[r.ExemplarId];
                            // pgvector returns cosine *distance* (0 = identical, 2 = opposite).
                            // We expose cosine *similarity* (1 = identical, -1 = opposite) so
                            // the band ordering matches the legacy JSON path. Round to 6 dp
                            // to match CosineSimilarity().
                            var similarity = Math.Round(1.0 - r.Distance, 6);
                            return new WritingExemplarSimilarity(c.Id, similarity, c.LetterType, c.Profession);
                        })
                        .ToList();
                }
                // Otherwise fall through to the JSON-cosine path so freshly-
                // added rows that have not been backfilled yet still rank.
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "pgvector nearest-neighbour query failed; falling back to JSON cosine.");
            }
        }

        var embeddings = await db.WritingExemplarEmbeddings.AsNoTracking()
            .Where(e => candidateIds.Contains(e.ExemplarId))
            .ToDictionaryAsync(e => e.ExemplarId, e => e.EmbeddingJson, ct);

        var similarities = new List<WritingExemplarSimilarity>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var similarity = 0.0;
            if (scenarioVector.Length == VectorDimensions && embeddings.TryGetValue(candidate.Id, out var vectorJson) && IsValid(vectorJson))
            {
                similarity = CosineSimilarity(scenarioVector, DeserializeVector(vectorJson));
            }
            similarities.Add(new WritingExemplarSimilarity(candidate.Id, similarity, candidate.LetterType, candidate.Profession));
        }
        return similarities.OrderByDescending(s => s.Similarity).Take(take).ToList();
    }

    public async Task<(int Exemplars, int Scenarios)> BackfillFromJsonAsync(CancellationToken ct)
    {
        // No-op on non-Postgres test providers — Embedding is unmapped there.
        if (!db.Database.IsNpgsql())
        {
            return (0, 0);
        }

        var exemplarRows = await db.WritingExemplarEmbeddings
            .Where(e => e.Embedding == null && e.EmbeddingJson != null && e.EmbeddingJson != "[]")
            .ToListAsync(ct);
        var exemplarUpdated = 0;
        foreach (var row in exemplarRows)
        {
            var vec = DeserializeVector(row.EmbeddingJson);
            if (vec.Length != VectorDimensions) continue;
            row.Embedding = new Vector(vec);
            exemplarUpdated++;
        }
        if (exemplarUpdated > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        var scenarioRows = await db.WritingScenarioEmbeddings
            .Where(e => e.Embedding == null && e.EmbeddingJson != null && e.EmbeddingJson != "[]")
            .ToListAsync(ct);
        var scenarioUpdated = 0;
        foreach (var row in scenarioRows)
        {
            var vec = DeserializeVector(row.EmbeddingJson);
            if (vec.Length != VectorDimensions) continue;
            row.Embedding = new Vector(vec);
            scenarioUpdated++;
        }
        if (scenarioUpdated > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        if (exemplarUpdated > 0 || scenarioUpdated > 0)
        {
            logger.LogInformation(
                "pgvector backfill populated {ExemplarCount} exemplar + {ScenarioCount} scenario embeddings from EmbeddingJson",
                exemplarUpdated, scenarioUpdated);
        }
        return (exemplarUpdated, scenarioUpdated);
    }

    private async Task<float[]> CallEmbedAsync(string input, string? userId, CancellationToken ct)
    {
        var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            Task = AiTaskMode.GenerateContent,
        });
        var result = await aiGateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = prompt,
            UserInput = input,
            FeatureCode = AiFeatureCodes.WritingExemplarEmbedV1,
            PromptTemplateId = "writing.exemplar.embed.v1",
            Temperature = 0.0,
            UserId = userId,
        }, ct);
        return ParseVector(result.Completion);
    }

    internal static float[] ParseVector(string completion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return Array.Empty<float>();
        try
        {
            using var doc = JsonDocument.Parse(completion);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return doc.RootElement.EnumerateArray().Select(e => (float)e.GetDouble()).ToArray();
            }
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("embedding", out var arr)
                && arr.ValueKind == JsonValueKind.Array)
            {
                return arr.EnumerateArray().Select(e => (float)e.GetDouble()).ToArray();
            }
        }
        catch (JsonException)
        {
            // Fall through to regex parsing.
        }
        var matches = VectorRegex.Matches(completion);
        if (matches.Count == 0) return Array.Empty<float>();
        var floats = new List<float>(matches.Count);
        foreach (Match m in matches)
        {
            if (float.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                floats.Add(v);
            }
        }
        return floats.ToArray();
    }

    private async Task PersistExemplarEmbeddingAsync(Guid exemplarId, float[] vector, CancellationToken ct)
    {
        var serialized = JsonSerializer.Serialize(vector, JsonOptions);
        // Only build the pgvector mirror under Postgres — on SQLite / in-memory
        // the Embedding property is unmapped via .Ignore(), and assigning a
        // non-null Vector to an ignored property is harmless but allocates.
        Vector? pg = (vector.Length == VectorDimensions && db.Database.IsNpgsql())
            ? new Vector(vector)
            : null;
        var entity = await db.WritingExemplarEmbeddings.FirstOrDefaultAsync(e => e.ExemplarId == exemplarId, ct);
        var now = clock.GetUtcNow();
        if (entity is null)
        {
            db.WritingExemplarEmbeddings.Add(new WritingExemplarEmbedding
            {
                Id = Guid.NewGuid(),
                ExemplarId = exemplarId,
                ModelId = ModelId,
                Dimensions = vector.Length,
                EmbeddingJson = serialized,
                Embedding = pg,
                CreatedAt = now,
            });
        }
        else
        {
            entity.ModelId = ModelId;
            entity.Dimensions = vector.Length;
            entity.EmbeddingJson = serialized;
            entity.Embedding = pg;
            entity.CreatedAt = now;
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task PersistScenarioEmbeddingAsync(Guid scenarioId, float[] vector, CancellationToken ct)
    {
        var serialized = JsonSerializer.Serialize(vector, JsonOptions);
        Vector? pg = (vector.Length == VectorDimensions && db.Database.IsNpgsql())
            ? new Vector(vector)
            : null;
        var entity = await db.WritingScenarioEmbeddings.FirstOrDefaultAsync(e => e.ScenarioId == scenarioId, ct);
        var now = clock.GetUtcNow();
        if (entity is null)
        {
            db.WritingScenarioEmbeddings.Add(new WritingScenarioEmbedding
            {
                Id = Guid.NewGuid(),
                ScenarioId = scenarioId,
                ModelId = ModelId,
                Dimensions = vector.Length,
                EmbeddingJson = serialized,
                Embedding = pg,
                CreatedAt = now,
            });
        }
        else
        {
            entity.ModelId = ModelId;
            entity.Dimensions = vector.Length;
            entity.EmbeddingJson = serialized;
            entity.Embedding = pg;
            entity.CreatedAt = now;
        }
        await db.SaveChangesAsync(ct);
    }

    private static bool IsValid(string? json)
        => !string.IsNullOrWhiteSpace(json) && json != "[]";

    private static float[] DeserializeVector(string json)
    {
        try { return JsonSerializer.Deserialize<float[]>(json, JsonOptions) ?? Array.Empty<float>(); }
        catch (JsonException) { return Array.Empty<float>(); }
    }

    internal static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length) return 0.0;
        double dot = 0, sumA = 0, sumB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            sumA += a[i] * a[i];
            sumB += b[i] * b[i];
        }
        var denom = Math.Sqrt(sumA) * Math.Sqrt(sumB);
        return denom == 0 ? 0 : Math.Round(dot / denom, 6);
    }
}
