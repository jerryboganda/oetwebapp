using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Operator surface for the AI Learning Companion knowledge store.
///
/// <para>
/// <see cref="CompanionRulebookIndexer"/> builds the Stage 1 corpus, but until
/// something calls it the store is empty and the companion — correctly, and
/// uselessly — answers that it has no verified information. This is that
/// something: an explicit, auditable operator action rather than an implicit
/// startup job, because indexing writes an approved knowledge corpus and calls
/// a paid embedding provider.
/// </para>
///
/// <para>
/// Authorisation matches <see cref="AiToolsAdminEndpoints"/>
/// (<c>AdminAiConfig</c>) — the admins who configure AI providers and tool
/// grants are the ones who own the corpus.
/// </para>
/// </summary>
public static class CompanionKnowledgeAdminEndpoints
{
    public static IEndpointRouteBuilder MapCompanionKnowledgeAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/companion/knowledge")
            .RequireAuthorization("AdminAiConfig")
            .RequireRateLimiting("PerUser");

        // ── Corpus status ───────────────────────────────────────────────────
        group.MapGet("/status", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var sources = await db.CompanionSources
                .AsNoTracking()
                .GroupBy(s => s.State)
                .Select(g => new { state = g.Key.ToString(), count = g.Count() })
                .ToListAsync(ct);

            var chunkCount = await db.CompanionChunks.AsNoTracking().CountAsync(ct);

            // Chunks without an embedding still answer through the keyword path,
            // so this is a quality signal rather than an outage.
            var embeddedCount = db.Database.IsNpgsql()
                ? await db.CompanionChunks.AsNoTracking().CountAsync(c => c.Embedding != null, ct)
                : 0;

            var lastIndexedAt = await db.CompanionSources
                .AsNoTracking()
                .OrderByDescending(s => s.UpdatedAt)
                .Select(s => (DateTimeOffset?)s.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            return Results.Ok(new
            {
                sources,
                chunkCount,
                embeddedCount,
                vectorSearchAvailable = db.Database.IsNpgsql(),
                lastIndexedAt,
            });
        });

        // ── Reindex the rulebook corpus ─────────────────────────────────────
        group.MapPost("/reindex", async (
            CompanionReindexRequest? request,
            ICompanionRulebookIndexer indexer,
            CancellationToken ct) =>
        {
            var professions = new List<ExamProfession>();
            foreach (var raw in request?.Professions ?? [])
            {
                if (!Enum.TryParse<ExamProfession>(raw, ignoreCase: true, out var parsed))
                {
                    return Results.BadRequest(new { error = "unknown_profession", value = raw });
                }
                professions.Add(parsed);
            }

            // Embedding defaults ON: keyword-only retrieval works but ranks worse,
            // and an operator who wants the cheap pass has to ask for it.
            var embed = request?.Embed ?? true;

            var result = await indexer.IndexAsync(professions, embed, ct);

            return Results.Ok(new
            {
                sourcesWritten = result.SourcesWritten,
                chunksWritten = result.ChunksWritten,
                chunksUnchanged = result.ChunksUnchanged,
                chunksEmbedded = result.ChunksEmbedded,
                warnings = result.Warnings,
            });
        });

        return app;
    }
}

/// <summary>
/// Reindex options. An empty <see cref="Professions"/> list means every
/// profession, which is the normal case after a rulebook edit.
/// </summary>
public sealed record CompanionReindexRequest(
    IReadOnlyList<string>? Professions = null,
    bool? Embed = null);
