using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Companion;

public sealed record CompanionApprovalResult(bool Ok, string Reason, int SourcesAffected);

public sealed record CompanionReleaseSummary(
    Guid Id,
    string ReleaseVersion,
    string Status,
    int SourceCount,
    int ChunkCount,
    string? ApprovedByUserId,
    DateTimeOffset? PublishedAt,
    Guid? RollbackTargetReleaseId,
    string? Changelog);

public interface ICompanionKnowledgeGovernance
{
    Task<CompanionApprovalResult> ApproveAsync(
        string sourceKey, string? version, string approverUserId, CancellationToken ct);

    Task<CompanionApprovalResult> RetireAsync(
        string sourceKey, string? version, string actorUserId, CancellationToken ct);

    Task<CompanionReleaseSummary> PublishReleaseAsync(
        string releaseVersion, string approverUserId, string? changelog, CancellationToken ct);

    Task<CompanionReleaseSummary?> RollbackAsync(string actorUserId, CancellationToken ct);

    Task<IReadOnlyList<CompanionReleaseSummary>> ListReleasesAsync(int limit, CancellationToken ct);

    Task<CompanionAssetRegister> BuildAssetRegisterAsync(CancellationToken ct);
}

/// <summary>
/// Approval, release and rollback for the knowledge corpus (Manifest 1.F).
///
/// <para>
/// Until now the indexers hardcoded <c>State = Approved</c>, nothing recorded an
/// approver, and <see cref="CompanionKnowledgeRelease"/> was dead schema. That is
/// tolerable while the corpus is one class of already-approved rulebooks and
/// stops being tolerable the moment official exam facts exist: an official fact
/// nobody has checked is worse than no official fact, because the companion
/// states it with the highest authority in the system.
/// </para>
///
/// <para>
/// <b>Rollback is deliberately not a delete.</b> Publishing stamps every chunk
/// with a release id; rolling back re-points the corpus at the previous release
/// and marks the bad one rolled back, so the evidence of what went wrong
/// survives. The source specification treats knowledge rollback and application
/// rollback as independent recovery controls, and a rollback that destroyed its
/// own audit trail would not be one.
/// </para>
/// </summary>
public sealed class CompanionKnowledgeGovernance(
    LearnerDbContext db,
    ILogger<CompanionKnowledgeGovernance> logger) : ICompanionKnowledgeGovernance
{
    public async Task<CompanionApprovalResult> ApproveAsync(
        string sourceKey,
        string? version,
        string approverUserId,
        CancellationToken ct)
    {
        var sources = await FindAsync(sourceKey, version, ct);
        if (sources.Count == 0) return new CompanionApprovalResult(false, "not_found", 0);

        var now = DateTimeOffset.UtcNow;
        var affected = 0;

        foreach (var source in sources)
        {
            // A superseded version stays superseded. Approving it would put two
            // versions of the same source back in front of learners, which is
            // the exact failure the supersede logic exists to prevent.
            if (source.SupersededBySourceId is not null) continue;

            source.State = CompanionSourceState.Approved;
            source.ApprovedByUserId = approverUserId;
            source.ApprovedAt = now;

            // For an official fact, approval IS the verification act: somebody
            // has checked it against SourceUrl on this date and put their name
            // to it. Recording that separately is what lets a later reviewer ask
            // "when was this last checked?" and get an answer.
            if (source.AuthorityClass == CompanionAuthorityClass.OfficialCurrentFact)
            {
                source.VerifiedByUserId = approverUserId;
                source.VerifiedAt = now;
            }

            source.UpdatedAt = now;
            affected++;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Companion knowledge: {Count} source version(s) of {SourceKey} approved by {Approver}.",
            affected, sourceKey, approverUserId);

        return new CompanionApprovalResult(affected > 0, affected > 0 ? "approved" : "all_superseded", affected);
    }

    public async Task<CompanionApprovalResult> RetireAsync(
        string sourceKey,
        string? version,
        string actorUserId,
        CancellationToken ct)
    {
        var sources = await FindAsync(sourceKey, version, ct);
        if (sources.Count == 0) return new CompanionApprovalResult(false, "not_found", 0);

        var now = DateTimeOffset.UtcNow;
        foreach (var source in sources)
        {
            source.State = CompanionSourceState.Retired;
            source.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "Companion knowledge: {Count} source version(s) of {SourceKey} retired by {Actor}; " +
            "they are no longer retrievable.",
            sources.Count, sourceKey, actorUserId);

        return new CompanionApprovalResult(true, "retired", sources.Count);
    }

    public async Task<CompanionReleaseSummary> PublishReleaseAsync(
        string releaseVersion,
        string approverUserId,
        string? changelog,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var liveSources = await db.CompanionSources
            .Where(s => s.State == CompanionSourceState.Approved && s.SupersededBySourceId == null)
            .Select(s => s.Id)
            .ToListAsync(ct);

        var previous = await db.CompanionKnowledgeReleases
            .Where(r => r.Status == "published")
            .OrderByDescending(r => r.PublishedAt)
            .FirstOrDefaultAsync(ct);

        var release = await db.CompanionKnowledgeReleases
            .FirstOrDefaultAsync(r => r.ReleaseVersion == releaseVersion, ct);

        if (release is null)
        {
            release = new CompanionKnowledgeRelease
            {
                Id = Guid.NewGuid(),
                ReleaseVersion = releaseVersion,
                CreatedAt = now,
            };
            db.CompanionKnowledgeReleases.Add(release);
        }

        // Stamp the chunks first, then the release. If this fails halfway the
        // release is still "draft" and nothing claims to have published.
        var stamped = await db.CompanionChunks
            .Where(c => liveSources.Contains(c.SourceId))
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.ReleaseId, release.Id), ct);

        var checksum = await BuildChecksumAsync(ct);

        release.Status = "published";
        release.SourceCount = liveSources.Count;
        release.ChunkCount = stamped;
        release.IndexChecksum = checksum;
        release.ApprovedByUserId = approverUserId;
        release.PublishedAt = now;
        release.RollbackTargetReleaseId = previous?.Id;
        release.Changelog = changelog;
        release.UpdatedAt = now;

        if (previous is not null && previous.Id != release.Id)
        {
            previous.Status = "superseded";
            previous.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Companion knowledge release {Version} published by {Approver}: {Sources} sources, {Chunks} chunks.",
            releaseVersion, approverUserId, liveSources.Count, stamped);

        return ToSummary(release);
    }

    public async Task<CompanionReleaseSummary?> RollbackAsync(string actorUserId, CancellationToken ct)
    {
        var current = await db.CompanionKnowledgeReleases
            .Where(r => r.Status == "published")
            .OrderByDescending(r => r.PublishedAt)
            .FirstOrDefaultAsync(ct);

        if (current?.RollbackTargetReleaseId is not { } targetId) return null;

        var target = await db.CompanionKnowledgeReleases.FirstOrDefaultAsync(r => r.Id == targetId, ct);
        if (target is null) return null;

        var now = DateTimeOffset.UtcNow;

        // Sources introduced by the release being rolled back go back to
        // PendingApproval rather than being deleted: whatever was wrong with
        // them still needs looking at, and a rollback that shreds the evidence
        // makes the incident harder to close, not easier.
        var introduced = await db.CompanionChunks
            .Where(c => c.ReleaseId == current.Id)
            .Select(c => c.SourceId)
            .Distinct()
            .ToListAsync(ct);

        var previouslyKnown = await db.CompanionChunks
            .Where(c => c.ReleaseId == target.Id)
            .Select(c => c.SourceId)
            .Distinct()
            .ToListAsync(ct);

        var newOnly = introduced.Except(previouslyKnown).ToList();

        if (newOnly.Count > 0)
        {
            await db.CompanionSources
                .Where(s => newOnly.Contains(s.Id))
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.State, CompanionSourceState.PendingApproval)
                    .SetProperty(s => s.UpdatedAt, now), ct);
        }

        current.Status = "rolled_back";
        current.UpdatedAt = now;
        target.Status = "published";
        target.UpdatedAt = now;

        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "Companion knowledge rolled back from {From} to {To} by {Actor}; {Count} source(s) returned to PendingApproval.",
            current.ReleaseVersion, target.ReleaseVersion, actorUserId, newOnly.Count);

        return ToSummary(target);
    }

    public async Task<IReadOnlyList<CompanionReleaseSummary>> ListReleasesAsync(int limit, CancellationToken ct) =>
        (await db.CompanionKnowledgeReleases
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct))
        .Select(ToSummary)
        .ToList();

    /// <summary>
    /// The asset register the Manifest §7 requires, plus its more useful half:
    /// the explicit list of what is <b>not</b> ingested and why.
    ///
    /// <para>
    /// A register that lists only what is present cannot be checked against the
    /// Manifest, because the interesting question is always "what is missing?".
    /// Every exclusion here is a deliberate decision with a reason attached, so
    /// the owner can disagree with a specific one instead of re-auditing the
    /// corpus.
    /// </para>
    /// </summary>
    public async Task<CompanionAssetRegister> BuildAssetRegisterAsync(CancellationToken ct)
    {
        var sources = await db.CompanionSources
            .AsNoTracking()
            .OrderBy(s => s.SourceType).ThenBy(s => s.SourceKey)
            .Select(s => new CompanionAssetEntry(
                s.SourceKey,
                s.SourceType,
                s.Title,
                s.AuthorityClass.ToString(),
                s.State.ToString(),
                s.Version,
                s.ProfessionId,
                s.SubtestCode,
                s.RequiredEntitlementScope,
                s.PackageScope,
                s.IsProprietary,
                s.ApprovedByUserId,
                s.ApprovedAt,
                s.VerifiedByUserId,
                s.VerifiedAt,
                s.SourceUrl,
                s.StorageLocator,
                s.SupersededBySourceId != null))
            .ToListAsync(ct);

        return new CompanionAssetRegister(sources, NotIngested);
    }

    /// <summary>
    /// Deliberate exclusions. Each is a decision, not an omission.
    /// </summary>
    private static readonly IReadOnlyList<CompanionNotIngestedEntry> NotIngested =
    [
        new("Course video library",
            "Owner decision: video, its transcripts and timestamp deep links are out of scope for this phase. " +
            "Live-class transcripts are already chunked and embedded in their own table, but into no CompanionSource — " +
            "so they carry no authority class and no entitlement prefilter, and are deliberately left alone rather " +
            "than wired in without one."),

        new("Watermarked Speaking role-play cards",
            "Copyright decision outstanding with the owner. Only the unwatermarked remainder is eligible for ingestion."),

        new("The Tutor Book",
            "External, manually delivered product. The companion says so rather than pretending to hold it."),

        new("The four Final Testing PDFs",
            "Manifest §5, top of the never-index list. Their prompts, PASS CHECKs, scorecards and expected results must " +
            "never be retrievable — if they are, every acceptance result is void rather than merely worse. " +
            "CompanionCorpusGuard enforces this at the corpus boundary for every indexer, and CompanionLeakDetector " +
            "re-checks it on the way out."),

        new("Reading and Listening authoring rulebooks",
            "rulebooks/{reading,listening}/{profession}/ are instructions to content authors (\"the publish gate rejects " +
            "any other shape\"), not learner methodology. The candidate-facing rules are indexed from the _exam-mode " +
            "books instead."),

        new("Remediation rulebooks",
            "Self-declared \"V0 stub for the AI-personalisation feature flag\" — placeholder content."),

        new("Audio materials in the materials library",
            "Would need speech recognition, which is out of scope with video. Counted at index time so the number is real."),

        new("Another learner's profile, memory or attempts",
            "Manifest §5. Candidate evidence is per-learner and is never placed in a shared corpus; it reaches a turn " +
            "only through that learner's own resolved context."),

        new("Raw identifiable patient information",
            "Manifest §5. Never ingested, never stored as a note, and not repeated back if a learner pastes it."),
    ];

    private async Task<List<CompanionSource>> FindAsync(string sourceKey, string? version, CancellationToken ct)
    {
        var query = db.CompanionSources.Where(s => s.SourceKey == sourceKey);
        if (!string.IsNullOrWhiteSpace(version)) query = query.Where(s => s.Version == version);
        return await query.ToListAsync(ct);
    }

    /// <summary>
    /// Checksum over the live source versions, so two environments can be
    /// compared, and a release can be shown to contain what it claims.
    /// </summary>
    private async Task<string> BuildChecksumAsync(CancellationToken ct)
    {
        var parts = await db.CompanionSources
            .AsNoTracking()
            .Where(s => s.State == CompanionSourceState.Approved && s.SupersededBySourceId == null)
            .OrderBy(s => s.SourceKey)
            .Select(s => s.SourceKey + "@" + s.Version)
            .ToListAsync(ct);

        return CompanionIndexWriter.Sha256(string.Join("|", parts))[..32].ToLowerInvariant();
    }

    private static CompanionReleaseSummary ToSummary(CompanionKnowledgeRelease r) =>
        new(r.Id, r.ReleaseVersion, r.Status, r.SourceCount, r.ChunkCount,
            r.ApprovedByUserId, r.PublishedAt, r.RollbackTargetReleaseId, r.Changelog);
}

public sealed record CompanionAssetEntry(
    string SourceKey,
    string SourceType,
    string Title,
    string Authority,
    string State,
    string Version,
    string? ProfessionId,
    string? SubtestCode,
    string? RequiredEntitlementScope,
    string? PackageScope,
    bool IsProprietary,
    string? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    string? VerifiedByUserId,
    DateTimeOffset? VerifiedAt,
    string? SourceUrl,
    string? StorageLocator,
    bool Superseded);

public sealed record CompanionNotIngestedEntry(string Asset, string Reason);

public sealed record CompanionAssetRegister(
    IReadOnlyList<CompanionAssetEntry> Ingested,
    IReadOnlyList<CompanionNotIngestedEntry> NotIngested);
