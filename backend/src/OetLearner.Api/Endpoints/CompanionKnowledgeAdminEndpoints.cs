using System.Security.Claims;
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

        // ── Corpus status and acceptance readiness ──────────────────────────
        //
        // This is deliberately more than a row count. The acceptance packs must
        // run against the candidate-intended build, and the two ways that
        // silently fails are (a) the master flag is off, so the orchestrator
        // serves the generic fallback prompt instead of the composed companion
        // prompt, and (b) the corpus holds only one authority class, so every
        // "official fact" answer is really a teaching rule. Neither is visible
        // from a total chunk count, and both invalidate a whole test run.
        group.MapGet("/status", async (
            LearnerDbContext db,
            ICompanionFeatureFlags flags,
            CancellationToken ct) =>
        {
            var sources = await db.CompanionSources
                .AsNoTracking()
                .GroupBy(s => s.State)
                .Select(g => new { state = g.Key.ToString(), count = g.Count() })
                .ToListAsync(ct);

            // Per authority class, and retrievable-only: a source that is not
            // Approved, is superseded, or is outside its effective window cannot
            // answer a learner, so counting it here would overstate coverage.
            var now = DateTimeOffset.UtcNow;
            var byAuthority = await db.CompanionSources
                .AsNoTracking()
                .Where(s => s.State == CompanionSourceState.Approved)
                .Where(s => s.SupersededBySourceId == null)
                .Where(s => s.EffectiveFrom == null || s.EffectiveFrom <= now)
                .Where(s => s.EffectiveTo == null || s.EffectiveTo >= now)
                .GroupBy(s => s.AuthorityClass)
                .Select(g => new { authority = g.Key.ToString(), sourceCount = g.Count() })
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

            var companionEnabled = await flags.IsEnabledAsync(ct);
            var retrievalEnabled = await flags.IsRetrievalEnabledAsync(ct);
            var actionsEnabled = await flags.AreActionsEnabledAsync(ct);
            var creditsEnabled = await flags.IsCreditConsumptionEnabledAsync(ct);
            var scoreDisplayEnabled = await flags.IsScoreDisplayEnabledAsync(ct);

            // Blocking reasons, not a score. Each one makes a pack run invalid
            // rather than merely worse, so they are listed explicitly for the
            // tester to clear before capturing any first response.
            var blockers = new List<string>();
            if (!companionEnabled)
            {
                blockers.Add("flag ai_learning_companion is OFF — the orchestrator serves the generic fallback prompt, so persona, grounding and boundaries are all absent. Activate flg-026.");
            }
            if (!retrievalEnabled)
            {
                blockers.Add("flag companion_retrieval is OFF — answers are ungrounded and cite nothing. Activate flg-027.");
            }
            if (!actionsEnabled)
            {
                blockers.Add("flag companion_actions is OFF — every Open/Start/Continue scenario degrades to text directions. Activate flg-028.");
            }
            if (chunkCount == 0)
            {
                blockers.Add("the knowledge corpus is empty — run POST /v1/admin/companion/knowledge/reindex.");
            }
            if (!byAuthority.Any(a => a.authority == nameof(CompanionAuthorityClass.OfficialCurrentFact)))
            {
                blockers.Add("no OfficialCurrentFact source is approved — the companion cannot ground any current official exam fact, and authority-conflict detection can never fire.");
            }

            // The contamination check the Manifest requires before every pack
            // run, done here rather than left to the tester to remember.
            //
            // A contaminated corpus does not make the run score worse, it makes
            // the result meaningless — so this is a blocker rather than a
            // warning, and it is checked against the stored chunks rather than
            // by asking the companion, because a model that happens not to
            // surface the material on one phrasing has proved nothing.
            var contaminated = await CompanionCorpusGuard.FindContaminatedChunksAsync(db, ct);
            if (contaminated.Count > 0)
            {
                blockers.Add(
                    $"CORPUS CONTAMINATED: {contaminated.Count} chunk(s) carry acceptance-pack markers " +
                    $"({string.Join(", ", contaminated.Select(c => c.Marker).Distinct())}). Every acceptance " +
                    "result from this corpus is void. Retire the affected sources and reindex before testing.");
            }

            return Results.Ok(new
            {
                sources,
                byAuthority,
                chunkCount,
                embeddedCount,
                vectorSearchAvailable = db.Database.IsNpgsql(),
                lastIndexedAt,
                flags = new
                {
                    companionEnabled,
                    retrievalEnabled,
                    actionsEnabled,
                    creditsEnabled,
                    scoreDisplayEnabled,
                },
                contamination = new
                {
                    clean = contaminated.Count == 0,
                    chunks = contaminated,
                },
                readyForAcceptanceTesting = blockers.Count == 0,
                blockers,
            });
        });

        // ── Rebuild the corpus ──────────────────────────────────────────────
        //
        // Audited, because it writes an approved knowledge corpus and calls a
        // paid embedding provider. "Who reindexed, when, and what came back" is
        // the first question asked after a bad answer reaches a learner.
        group.MapPost("/reindex", async (
            CompanionReindexRequest? request,
            ICompanionCorpusBuilder builder,
            LearnerDbContext db,
            ClaimsPrincipal principal,
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

            var result = await builder.BuildAsync(professions, embed, ct);

            await AuditAsync(db, principal, "CompanionKnowledgeReindex", "CompanionCorpus", null,
                $"professions={(professions.Count == 0 ? "all" : string.Join(",", professions))};embed={embed};" +
                $"sources={result.SourcesWritten};chunks={result.ChunksWritten};unchanged={result.ChunksUnchanged};" +
                $"embedded={result.ChunksEmbedded};warnings={result.Warnings.Count}", ct);

            return Results.Ok(new
            {
                sourcesWritten = result.SourcesWritten,
                chunksWritten = result.ChunksWritten,
                chunksUnchanged = result.ChunksUnchanged,
                chunksEmbedded = result.ChunksEmbedded,
                warnings = result.Warnings,
            });
        });

        // ── Approval gate ───────────────────────────────────────────────────
        //
        // The official-fact layer seeds PendingApproval on purpose: a fact
        // nobody has checked is worse than no fact, because the companion states
        // it with the highest authority in the system. This is where a person
        // takes responsibility for one, and the approver and date are recorded.
        group.MapPost("/sources/{sourceKey}/approve", async (
            string sourceKey,
            CompanionApprovalRequest? request,
            ICompanionKnowledgeGovernance governance,
            LearnerDbContext db,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var result = await governance.ApproveAsync(sourceKey, request?.Version, ActorId(principal), ct);

            if (!result.Ok && result.Reason == "not_found")
            {
                return Results.NotFound(new { error = "unknown_source", sourceKey });
            }

            await AuditAsync(db, principal, "CompanionKnowledgeApprove", "CompanionSource", sourceKey,
                $"version={request?.Version ?? "all"};affected={result.SourcesAffected}", ct);

            return Results.Ok(new
            {
                approved = result.Ok,
                reason = result.Reason,
                sourcesAffected = result.SourcesAffected,
            });
        });

        group.MapPost("/sources/{sourceKey}/retire", async (
            string sourceKey,
            CompanionApprovalRequest? request,
            ICompanionKnowledgeGovernance governance,
            LearnerDbContext db,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var result = await governance.RetireAsync(sourceKey, request?.Version, ActorId(principal), ct);

            if (!result.Ok) return Results.NotFound(new { error = "unknown_source", sourceKey });

            await AuditAsync(db, principal, "CompanionKnowledgeRetire", "CompanionSource", sourceKey,
                $"version={request?.Version ?? "all"};affected={result.SourcesAffected}", ct);

            return Results.Ok(new { retired = true, sourcesAffected = result.SourcesAffected });
        });

        // ── Releases: publish and roll back without an application deploy ───
        group.MapGet("/releases", async (
            ICompanionKnowledgeGovernance governance,
            int? limit,
            CancellationToken ct) =>
            Results.Ok(await governance.ListReleasesAsync(limit ?? 25, ct)));

        group.MapPost("/releases", async (
            CompanionPublishReleaseRequest request,
            ICompanionKnowledgeGovernance governance,
            LearnerDbContext db,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.ReleaseVersion))
            {
                return Results.BadRequest(new { error = "release_version_required" });
            }

            var release = await governance.PublishReleaseAsync(
                request.ReleaseVersion.Trim(), ActorId(principal), request.Changelog, ct);

            await AuditAsync(db, principal, "CompanionKnowledgePublish", "CompanionKnowledgeRelease",
                release.Id.ToString(),
                $"version={release.ReleaseVersion};sources={release.SourceCount};chunks={release.ChunkCount}", ct);

            return Results.Ok(release);
        });

        group.MapPost("/releases/rollback", async (
            ICompanionKnowledgeGovernance governance,
            LearnerDbContext db,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var release = await governance.RollbackAsync(ActorId(principal), ct);

            if (release is null)
            {
                // Having nothing to roll back TO is not a failure — it is the
                // first release. Saying so is more useful than a 500.
                return Results.BadRequest(new { error = "no_rollback_target" });
            }

            await AuditAsync(db, principal, "CompanionKnowledgeRollback", "CompanionKnowledgeRelease",
                release.Id.ToString(), $"restored={release.ReleaseVersion}", ct);

            return Results.Ok(release);
        });

        // ── Asset register, and the list of what is deliberately absent ─────
        group.MapGet("/assets", async (
            ICompanionKnowledgeGovernance governance,
            CancellationToken ct) =>
            Results.Ok(await governance.BuildAssetRegisterAsync(ct)));

        return app;
    }

    private static string ActorId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";

    private static async Task AuditAsync(
        LearnerDbContext db,
        ClaimsPrincipal principal,
        string action,
        string resourceType,
        string? resourceId,
        string details,
        CancellationToken ct)
    {
        var actorId = ActorId(principal);

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            ActorAuthAccountId = actorId,
            ActorName = principal.Identity?.Name ?? actorId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details,
        });

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Approve or retire one version, or every version when omitted.</summary>
public sealed record CompanionApprovalRequest(string? Version = null);

public sealed record CompanionPublishReleaseRequest(string ReleaseVersion, string? Changelog = null);

/// <summary>
/// Reindex options. An empty <see cref="Professions"/> list means every
/// profession, which is the normal case after a rulebook edit. Sources that are
/// not profession-scoped are rebuilt regardless.
/// </summary>
public sealed record CompanionReindexRequest(
    IReadOnlyList<string>? Professions = null,
    bool? Embed = null);
