using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Indexing;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin control surface for the codebase index that backs the admin assistant's search tools
/// (owner directive 2026-10-09).
///
/// <para>
/// <b>Why this exists.</b> <c>ICodebaseIndexer.TriggerReindex()</c> and <c>GetStatusAsync()</c> had
/// ZERO callers: indexing only ran from a hosted service, every 6 hours, and nothing anywhere
/// reported whether it had worked. In production it never could work — no source checkout was ever
/// mounted — so the admin assistant answered codebase questions from five consecutive
/// <c>totalMatches: 0</c> results and nobody could find out why. These two endpoints make the
/// index observable and repairable from the admin screen.
/// </para>
///
/// <para>
/// Deliberately mirrors <c>/v1/admin/companion/knowledge/reindex</c>, which has the same shape for
/// the same reason: "who reindexed, when, and what came back" is the first question asked after a
/// bad answer.
/// </para>
/// </summary>
public static class AiCodebaseAdminEndpoints
{
    public static void MapAiCodebaseAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/v1/admin/ai/codebase")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        /// <summary>Current index state, including WHY source may be unavailable.</summary>
        /// <remarks>
        /// <c>sourceAvailable</c> is the field that matters. Without it, "indexed 0 files" reads the
        /// same whether the index is healthy-but-empty or there is no source mounted at all — which
        /// is precisely the ambiguity this whole change set exists to remove.
        /// </remarks>
        group.MapGet("/status", async (
            ICodebaseIndexer indexer,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var status = await indexer.GetStatusAsync(ct);
            var chunkCount = await db.AiCodebaseChunks.CountAsync(ct);
            var lastIndexed = await db.AiCodebaseChunks
                .OrderByDescending(c => c.IndexedAt)
                .Select(c => (DateTimeOffset?)c.IndexedAt)
                .FirstOrDefaultAsync(ct);

            return Results.Ok(new
            {
                sourceAvailable = status.SourceAvailable,
                sourceRoot = status.SourceRoot,
                sourceRootReason = status.SourceRootReason,
                isRunning = status.IsRunning,
                totalFiles = status.TotalFiles,
                indexedFiles = status.IndexedFiles,
                chunkCount,
                lastIndexedAt = lastIndexed ?? status.LastCompleted,
                lastCompleted = status.LastCompleted,
                // The single sentence an operator actually needs.
                summary = status.SourceAvailable
                    ? $"Source available at {status.SourceRoot}; {chunkCount} chunk(s) indexed."
                    : "NO SOURCE MOUNTED. The admin codebase-search tools cannot work. " + status.SourceRootReason,
            });
        });

        /// <summary>Re-index the mounted source tree now, and report what happened.</summary>
        /// <remarks>
        /// Returns 409 when there is no source to index rather than pretending to succeed. That is
        /// the whole point: the previous behaviour was a silent no-op with a once-per-process log
        /// line nobody could reach.
        /// </remarks>
        group.MapPost("/reindex", async (
            ICodebaseIndexer indexer,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var status = await indexer.GetStatusAsync(ct);
            if (!status.SourceAvailable)
            {
                return Results.Json(new
                {
                    code = "codebase_source_unavailable",
                    message = "No project source is mounted, so there is nothing to index.",
                    sourceRootReason = status.SourceRootReason,
                }, statusCode: 409);
            }

            await AuditAsync(db, http, "AiCodebaseReindexRequested", "AiCodebaseChunks", null,
                $"sourceRoot={status.SourceRoot}", ct);

            // Fire-and-report: the run itself is long, and the hosted service owns the work loop.
            // The status endpoint is how progress is observed.
            var run = indexer.IndexFullAsync(CancellationToken.None);

            return Results.Accepted($"/v1/admin/ai/codebase/status", new
            {
                accepted = true,
                sourceRoot = status.SourceRoot,
                message = run.IsCompletedSuccessfully
                    ? "Indexing completed synchronously."
                    : "Indexing started. Poll GET /v1/admin/ai/codebase/status for progress.",
            });
        });
    }

    /// <summary>
    /// Writes the audit row. Field names mirror the companion knowledge audit exactly — an audit
    /// event that uses different column names is an audit event nobody can query.
    /// </summary>
    private static async Task AuditAsync(
        LearnerDbContext db,
        HttpContext http,
        string action,
        string resourceType,
        string? resourceId,
        string details,
        CancellationToken ct)
    {
        var actorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            ActorAuthAccountId = actorId,
            ActorName = http.User.Identity?.Name ?? actorId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details,
        });
        await db.SaveChangesAsync(ct);
    }
}