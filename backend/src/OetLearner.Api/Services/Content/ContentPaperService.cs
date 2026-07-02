using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Reading;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Services.Content;

// ═════════════════════════════════════════════════════════════════════════════
// ContentPaperService — Slice 3
//
// All writes to ContentPaper / ContentPaperAsset go through here. The service
// layer enforces invariants that the DB can't (e.g. app-level "at most one
// primary per role/part" on non-Postgres providers) and coordinates audit
// events alongside each mutation.
// ═════════════════════════════════════════════════════════════════════════════

public interface IContentPaperService
{
    Task<ContentPaper> CreateAsync(ContentPaperCreate args, string adminId, CancellationToken ct);

    /// <summary>Dedicated entry point for the admin Writing-authoring flow.
    /// Enforces the content-integrity acknowledgement (spec §19) and writes
    /// the acknowledgement metadata onto the resulting paper. Other
    /// creation paths (ZIP import, seeders, generic /papers POST) bypass
    /// this gate, preserving existing behavior.</summary>
    Task<ContentPaper> CreateWritingTaskAsync(WritingTaskCreate args, string adminId, CancellationToken ct);

    Task<ContentPaper> UpdateAsync(string paperId, ContentPaperUpdate args, string adminId, CancellationToken ct);
    Task<ContentPaper?> GetAsync(string paperId, CancellationToken ct);
    Task<IReadOnlyList<ContentPaper>> ListAsync(ContentPaperQuery query, CancellationToken ct);

    /// <summary>Total number of papers matching the same filters as
    /// <see cref="ListAsync"/> (ignoring paging). Lets the admin UI paginate
    /// server-side. Surfaced via the <c>X-Total-Count</c> response header.</summary>
    Task<int> CountAsync(ContentPaperQuery query, CancellationToken ct);

    Task ArchiveAsync(string paperId, string adminId, CancellationToken ct);
    Task HardDeleteAsync(string paperId, string adminId, CancellationToken ct);
    Task PublishAsync(string paperId, string adminId, CancellationToken ct);

    /// <summary>Transitions Published → Draft. Allows an admin to revert a
    /// paper to draft state for further editing. Throws when the paper is
    /// already Draft or Archived.</summary>
    Task UnpublishAsync(string paperId, string adminId, CancellationToken ct);

    /// <summary>Transitions Draft → InReview. Used by the writing authoring
    /// workflow (spec §1E) so a second reviewer can approve a paper before it
    /// goes live. Throws when the paper is not Draft.</summary>
    Task SubmitForReviewAsync(string paperId, string adminId, CancellationToken ct);

    /// <summary>Transitions InReview → Published. Calls into the existing
    /// publish gates (provenance, required assets, structure validation) and
    /// records both the review-approval audit event and the publish event.
    /// Throws when the paper is not InReview.</summary>
    Task ApproveAndPublishAsync(string paperId, string adminId, CancellationToken ct);

    /// <summary>Transitions InReview → Rejected. Records the rejection reason
    /// on the audit trail. Throws when the paper is not InReview.</summary>
    Task RejectAsync(string paperId, string adminId, string reason, CancellationToken ct);

    Task<ContentPaperAsset> AttachAssetAsync(
        string paperId, ContentPaperAssetAttach args, string adminId, CancellationToken ct);
    Task<bool> RemoveAssetAsync(string paperId, string assetId, string adminId, CancellationToken ct);

    /// <summary>Required roles for a given subtest. Publish gate uses this.</summary>
    IReadOnlySet<PaperAssetRole> RequiredRolesFor(string subtestCode);

    /// <summary>
    /// Atomically applies a single workflow <paramref name="action"/>
    /// (archive | publish | unpublish | submit-for-review | approve-publish |
    /// reject) to every paper in <paramref name="ids"/> inside ONE transaction,
    /// delegating to the matching per-item method. Per-item validation/status
    /// failures (<see cref="InvalidOperationException"/>) are recorded in the
    /// result rather than aborting the batch; any other exception rolls the
    /// whole batch back. Exactly one summary audit row is written.
    /// </summary>
    Task<BulkActionResult> BulkAsync(
        string action, string[] ids, string adminId, string? reason, CancellationToken ct);
}

public sealed record ContentPaperCreate(
    string SubtestCode,
    string Title,
    string? Slug,
    string? ProfessionId,
    bool AppliesToAllProfessions,
    string? Difficulty,
    int EstimatedDurationMinutes,
    string? CardType,
    string? LetterType,
    int Priority,
    string? TagsCsv,
    string? SourceProvenance,
    bool IntegrityAcknowledged = false);

/// <summary>Input for <see cref="IContentPaperService.CreateWritingTaskAsync"/>.
/// Carries the writing-specific authoring fields plus the mandatory
/// integrity acknowledgement (spec §19).</summary>
public sealed record WritingTaskCreate(
    string Title,
    string? Slug,
    string ProfessionId,
    string LetterType,
    string? Difficulty,
    int EstimatedDurationMinutes,
    int Priority,
    string? TagsCsv,
    string SourceProvenance,
    bool IntegrityAcknowledged);

public sealed record ContentPaperUpdate(
    string? Title,
    string? ProfessionId,
    bool? AppliesToAllProfessions,
    string? Difficulty,
    int? EstimatedDurationMinutes,
    string? CardType,
    string? LetterType,
    int? Priority,
    string? TagsCsv,
    string? SourceProvenance);

public sealed record ContentPaperQuery(
    string? SubtestCode = null,
    string? ProfessionId = null,
    string? Status = null,
    string? CardType = null,
    string? LetterType = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50);

public sealed record ContentPaperAssetAttach(
    PaperAssetRole Role,
    string MediaAssetId,
    string? Part,
    string? Title,
    int DisplayOrder,
    bool MakePrimary);

public sealed class ContentPaperService(
    LearnerDbContext db,
    IWritingTaskProjectionService? writingProjection = null) : IContentPaperService
{
    /// <summary>Upper bound on ids accepted by a single bulk call (mirrors the
    /// vocabulary bulk cap). Keeps one transaction from growing unbounded.</summary>
    private const int BulkIdLimit = 2000;

    /// <summary>Errors list cap so a huge failing batch doesn't bloat the
    /// response / audit detail.</summary>
    private const int BulkErrorCap = 20;

    private static readonly string[] BulkActions =
        ["archive", "publish", "unpublish", "submit-for-review", "approve-publish", "reject", "delete", "force-delete"];
    /// <summary>Publish gate — each subtest's minimum required roles. Keep
    /// this in sync with §2 of <c>docs/CONTENT-UPLOAD-PLAN.md</c>.</summary>
    private static readonly Dictionary<string, HashSet<PaperAssetRole>> RequiredRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["listening"] = new() { PaperAssetRole.Audio, PaperAssetRole.QuestionPaper, PaperAssetRole.AudioScript, PaperAssetRole.AnswerKey },
        ["reading"]   = new() { PaperAssetRole.QuestionPaper },
        ["writing"]   = new() { PaperAssetRole.CaseNotes, PaperAssetRole.ModelAnswer },
        ["speaking"]  = new()
        {
            PaperAssetRole.RoleCard,
            PaperAssetRole.AssessmentCriteria,
            PaperAssetRole.WarmUpQuestions,
        },
    };

    public IReadOnlySet<PaperAssetRole> RequiredRolesFor(string subtestCode)
        => RequiredRoles.TryGetValue(subtestCode, out var set)
            ? set
            : new HashSet<PaperAssetRole>();

    public async Task<ContentPaper> CreateAsync(ContentPaperCreate args, string adminId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.SubtestCode)) throw new ArgumentException("SubtestCode required.");
        if (string.IsNullOrWhiteSpace(args.Title)) throw new ArgumentException("Title required.");
        if (args.AppliesToAllProfessions && !string.IsNullOrWhiteSpace(args.ProfessionId))
            throw new ArgumentException("A paper is either profession-scoped or applies-to-all; pick one.");

        var subtest = args.SubtestCode.Trim().ToLowerInvariant();

        var slug = NormalizeSlug(args.Slug ?? args.Title);
        await EnsureSlugUnique(slug, ct);

        var now = DateTimeOffset.UtcNow;
        var paper = new ContentPaper
        {
            Id = Guid.NewGuid().ToString("N"),
            SubtestCode = subtest,
            Title = args.Title.Trim(),
            Slug = slug,
            ProfessionId = string.IsNullOrWhiteSpace(args.ProfessionId) ? null : args.ProfessionId.Trim().ToLowerInvariant(),
            AppliesToAllProfessions = args.AppliesToAllProfessions,
            Difficulty = args.Difficulty?.Trim() ?? "standard",
            EstimatedDurationMinutes = args.EstimatedDurationMinutes,
            CardType = args.CardType?.Trim().ToLowerInvariant(),
            LetterType = args.LetterType?.Trim().ToLowerInvariant(),
            Priority = args.Priority,
            TagsCsv = args.TagsCsv ?? string.Empty,
            SourceProvenance = args.SourceProvenance,
            Status = ContentStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByAdminId = adminId,
            IntegrityAcknowledgedByAdminId = args.IntegrityAcknowledged ? adminId : null,
            IntegrityAcknowledgedAt = args.IntegrityAcknowledged ? now : null,
        };
        db.ContentPapers.Add(paper);
        await WriteAuditAsync("ContentPaperCreated", paper.Id, paper.Title, adminId, ct);
        await db.SaveChangesAsync(ct);
        return paper;
    }

    public async Task<ContentPaper> CreateWritingTaskAsync(WritingTaskCreate args, string adminId, CancellationToken ct)
    {
        if (!args.IntegrityAcknowledged)
        {
            throw new ContentIntegrityAcknowledgementRequiredException(
                "Writing tasks require an integrity acknowledgement before creation.");
        }
        if (string.IsNullOrWhiteSpace(args.Title))
            throw new ArgumentException("Title required.");
        if (string.IsNullOrWhiteSpace(args.ProfessionId))
            throw new ArgumentException("Profession id required for writing tasks.");
        if (string.IsNullOrWhiteSpace(args.SourceProvenance))
            throw new ArgumentException("SourceProvenance required for writing tasks.");

        var paper = await CreateAsync(new ContentPaperCreate(
            SubtestCode: "writing",
            Title: args.Title,
            Slug: args.Slug,
            ProfessionId: args.ProfessionId,
            AppliesToAllProfessions: false,
            Difficulty: args.Difficulty,
            EstimatedDurationMinutes: args.EstimatedDurationMinutes,
            CardType: null,
            LetterType: args.LetterType,
            Priority: args.Priority,
            TagsCsv: args.TagsCsv,
            SourceProvenance: args.SourceProvenance,
            IntegrityAcknowledged: true), adminId, ct);

        return paper;
    }

    public Task SubmitForReviewAsync(string paperId, string adminId, CancellationToken ct)
        => SubmitForReviewAsync(paperId, adminId, writeAudit: true, ct);

    private async Task SubmitForReviewAsync(string paperId, string adminId, bool writeAudit, CancellationToken ct)
    {
        var paper = await db.ContentPapers.FirstOrDefaultAsync(x => x.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");
        if (paper.Status != ContentStatus.Draft)
            throw new InvalidOperationException(
                $"Paper must be Draft to submit for review (currently {paper.Status}).");

        paper.Status = ContentStatus.InReview;
        paper.UpdatedAt = DateTimeOffset.UtcNow;
        if (writeAudit) await WriteAuditAsync("ContentPaperSubmittedForReview", paper.Id, paper.Title, adminId, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task ApproveAndPublishAsync(string paperId, string adminId, CancellationToken ct)
        => ApproveAndPublishAsync(paperId, adminId, writeAudit: true, ct);

    private async Task ApproveAndPublishAsync(string paperId, string adminId, bool writeAudit, CancellationToken ct)
    {
        var paper = await db.ContentPapers.FirstOrDefaultAsync(x => x.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");
        if (paper.Status != ContentStatus.InReview)
            throw new InvalidOperationException(
                $"Paper must be InReview to approve & publish (currently {paper.Status}).");

        // Audit the approval before publish runs — keeps the reviewer ID on
        // the trail even if a publish-gate exception aborts the transaction.
        if (writeAudit) await WriteAuditAsync("ContentPaperApprovedForPublish", paper.Id, paper.Title, adminId, ct);
        await db.SaveChangesAsync(ct);

        // Delegate to the existing publish path so all gate logic (assets,
        // provenance, subtest validators) is reused untouched.
        await PublishAsync(paperId, adminId, writeAudit, ct);
    }

    public Task RejectAsync(string paperId, string adminId, string reason, CancellationToken ct)
        => RejectAsync(paperId, adminId, reason, writeAudit: true, ct);

    private async Task RejectAsync(string paperId, string adminId, string reason, bool writeAudit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Rejection reason is required.", nameof(reason));

        var paper = await db.ContentPapers.FirstOrDefaultAsync(x => x.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");
        if (paper.Status != ContentStatus.InReview)
            throw new InvalidOperationException(
                $"Paper must be InReview to reject (currently {paper.Status}).");

        var now = DateTimeOffset.UtcNow;
        paper.Status = ContentStatus.Rejected;
        paper.UpdatedAt = now;
        if (writeAudit)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = now,
                ActorId = adminId,
                ActorName = adminId,
                Action = "ContentPaperRejected",
                ResourceType = "ContentPaper",
                ResourceId = paper.Id,
                Details = $"{paper.Title} — {reason.Trim()}",
            });
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<BulkActionResult> BulkAsync(
        string action, string[] ids, string adminId, string? reason, CancellationToken ct)
    {
        var normalized = (action ?? string.Empty).Trim().ToLowerInvariant();
        if (!BulkActions.Contains(normalized))
            throw new ArgumentException($"Unknown bulk action '{action}'.", nameof(action));
        if (string.Equals(normalized, "reject", StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Rejection reason is required.", nameof(reason));

        var distinctIds = (ids ?? Array.Empty<string>())
            .Select(id => id?.Trim() ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (distinctIds.Count > BulkIdLimit)
            throw new ArgumentException(
                $"Bulk action is limited to {BulkIdLimit} papers at a time.", nameof(ids));

        var totalRequested = ids?.Length ?? 0;
        var succeeded = 0;
        var skipped = 0;
        var errors = new List<string>();

        // Single transaction across all per-item mutations so a mid-batch fatal
        // (non-validation) error rolls the whole batch back. The InMemory test
        // provider doesn't support transactions, so guard the call.
        var supportsTransactions = !string.Equals(
            db.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.InMemory",
            StringComparison.Ordinal);
        await using var tx = supportsTransactions ? await db.Database.BeginTransactionAsync(ct) : null;

        // Papers that successfully published via approve-publish and need the
        // writing→scenario projection bridge replayed after the batch commits.
        var writingToProject = new List<string>();

        foreach (var id in distinctIds)
        {
            try
            {
                var outcome = await ApplyOneAsync(normalized, id, adminId, reason, ct);
                switch (outcome)
                {
                    case BulkItemOutcome.Succeeded:
                        succeeded++;
                        // Only approve-publish replicates the writing→scenario
                        // bridge, matching the single-item endpoint (the plain
                        // /publish endpoint does not run the projection).
                        if (string.Equals(normalized, "approve-publish", StringComparison.Ordinal))
                        {
                            writingToProject.Add(id);
                        }
                        break;
                    case BulkItemOutcome.Skipped:
                        skipped++;
                        break;
                }
            }
            catch (InvalidOperationException ex)
            {
                // Validation / status-gate failure for this id — record and continue.
                if (errors.Count < BulkErrorCap)
                    errors.Add($"{id}: {ex.Message}");
                else if (errors.Count == BulkErrorCap)
                    errors.Add($"… and more (cap {BulkErrorCap} reached).");
            }
        }

        // One summary audit row for the whole bulk op. The affected ids go in
        // the unbounded `Details` (text) column — never in ResourceId, which is
        // varchar(64) and overflows once more than one ~32-char id is joined.
        await WriteAuditAsync(
            "ContentPaperBulkAction",
            "bulk",
            $"action={normalized}; requested={totalRequested}; succeeded={succeeded}; skipped={skipped}; failed={errors.Count}; ids={string.Join(",", distinctIds.Take(50))}",
            adminId, ct);
        await db.SaveChangesAsync(ct);

        if (tx is not null) await tx.CommitAsync(ct);

        // WS-B2 bridge: replicate the single-item approve-publish behavior for
        // each successfully published writing paper. Runs AFTER commit so a
        // projection failure can't roll back the publish (matches the single
        // endpoint, which logs-and-swallows).
        if (writingProjection is not null && writingToProject.Count > 0)
        {
            foreach (var id in writingToProject)
            {
                var paper = await db.ContentPapers.Include(p => p.Assets)
                    .FirstOrDefaultAsync(p => p.Id == id, ct);
                if (paper is null
                    || !string.Equals(paper.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                try { await writingProjection.ProjectFromContentPaperAsync(paper, ct); }
                catch { /* logged-and-swallowed by the projection service; publish stands. */ }
            }
        }

        return new BulkActionResult(
            totalRequested, succeeded, skipped, errors.Count, errors.ToArray());
    }

    private enum BulkItemOutcome { Succeeded, Skipped }

    /// <summary>Applies one workflow action to one paper, with per-item audit
    /// suppressed (the bulk op writes a single summary row). Returns whether the
    /// paper was mutated or was already in the target state (Skipped).</summary>
    private async Task<BulkItemOutcome> ApplyOneAsync(
        string action, string id, string adminId, string? reason, CancellationToken ct)
    {
        switch (action)
        {
            case "archive":
            {
                var status = await GetStatusAsync(id, ct);
                if (status == ContentStatus.Archived) return BulkItemOutcome.Skipped;
                await ArchiveAsync(id, adminId, writeAudit: false, ct);
                return BulkItemOutcome.Succeeded;
            }
            case "publish":
            {
                var status = await GetStatusAsync(id, ct);
                if (status == ContentStatus.Published) return BulkItemOutcome.Skipped;
                await PublishAsync(id, adminId, writeAudit: false, ct);
                return BulkItemOutcome.Succeeded;
            }
            case "unpublish":
            {
                var status = await GetStatusAsync(id, ct);
                if (status == ContentStatus.Draft) return BulkItemOutcome.Skipped;
                await UnpublishAsync(id, adminId, writeAudit: false, ct);
                return BulkItemOutcome.Succeeded;
            }
            case "submit-for-review":
            {
                var status = await GetStatusAsync(id, ct);
                if (status == ContentStatus.InReview) return BulkItemOutcome.Skipped;
                await SubmitForReviewAsync(id, adminId, writeAudit: false, ct);
                return BulkItemOutcome.Succeeded;
            }
            case "approve-publish":
                await ApproveAndPublishAsync(id, adminId, writeAudit: false, ct);
                return BulkItemOutcome.Succeeded;
            case "delete":
                await HardDeleteAsync(id, adminId, force: false, writeAudit: false, ct);
                return BulkItemOutcome.Succeeded;
            case "force-delete":
                await HardDeleteAsync(id, adminId, force: true, writeAudit: false, ct);
                return BulkItemOutcome.Succeeded;
            case "reject":
                await RejectAsync(id, adminId, reason!, writeAudit: false, ct);
                return BulkItemOutcome.Succeeded;
            default:
                throw new ArgumentException($"Unknown bulk action '{action}'.", nameof(action));
        }
    }

    private async Task<ContentStatus> GetStatusAsync(string id, CancellationToken ct)
    {
        var paper = await db.ContentPapers
            .Select(p => new { p.Id, p.Status })
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new InvalidOperationException("Paper not found.");
        return paper.Status;
    }

    public async Task<ContentPaper> UpdateAsync(string paperId, ContentPaperUpdate args, string adminId, CancellationToken ct)
    {
        var paper = await db.ContentPapers
            .Include(p => p.Assets)
                .ThenInclude(a => a.MediaAsset)
            .FirstOrDefaultAsync(x => x.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");

        if (args.Title is not null) paper.Title = args.Title.Trim();
        if (args.ProfessionId is not null)
            paper.ProfessionId = string.IsNullOrWhiteSpace(args.ProfessionId) ? null : args.ProfessionId.Trim().ToLowerInvariant();
        if (args.AppliesToAllProfessions is bool all) paper.AppliesToAllProfessions = all;
        if (args.Difficulty is not null) paper.Difficulty = args.Difficulty.Trim();
        if (args.EstimatedDurationMinutes is int mins) paper.EstimatedDurationMinutes = mins;
        if (args.CardType is not null) paper.CardType = args.CardType.Trim().ToLowerInvariant();
        if (args.LetterType is not null) paper.LetterType = args.LetterType.Trim().ToLowerInvariant();
        if (args.Priority is int p) paper.Priority = p;
        if (args.TagsCsv is not null) paper.TagsCsv = args.TagsCsv;
        if (args.SourceProvenance is not null) paper.SourceProvenance = args.SourceProvenance;

        if (paper.AppliesToAllProfessions && !string.IsNullOrWhiteSpace(paper.ProfessionId))
            throw new ArgumentException("A paper is either profession-scoped or applies-to-all; pick one.");

        paper.UpdatedAt = DateTimeOffset.UtcNow;
        await WriteAuditAsync("ContentPaperUpdated", paper.Id, paper.Title, adminId, ct);
        await db.SaveChangesAsync(ct);
        return paper;
    }

    public Task<ContentPaper?> GetAsync(string paperId, CancellationToken ct)
        => db.ContentPapers
            .Include(p => p.Assets)
                .ThenInclude(a => a.MediaAsset)
            .FirstOrDefaultAsync(x => x.Id == paperId, ct);

    public async Task<IReadOnlyList<ContentPaper>> ListAsync(ContentPaperQuery query, CancellationToken ct)
    {
        var q = ApplyFilters(
            db.ContentPapers
                .Include(p => p.Assets)
                    .ThenInclude(a => a.MediaAsset)
                .AsNoTracking()
                .AsQueryable(),
            query);
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 200);
        return await q
            .OrderByDescending(p => p.Priority)
            .ThenByDescending(p => p.UpdatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);
    }

    public Task<int> CountAsync(ContentPaperQuery query, CancellationToken ct)
        => ApplyFilters(db.ContentPapers.AsNoTracking().AsQueryable(), query).CountAsync(ct);

    private static IQueryable<ContentPaper> ApplyFilters(IQueryable<ContentPaper> q, ContentPaperQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.SubtestCode))
            q = q.Where(p => p.SubtestCode == query.SubtestCode!.ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(query.ProfessionId))
            q = q.Where(p => p.ProfessionId == query.ProfessionId!.ToLowerInvariant()
                          || p.AppliesToAllProfessions);
        if (!string.IsNullOrWhiteSpace(query.Status)
            && Enum.TryParse<ContentStatus>(query.Status, ignoreCase: true, out var st))
            q = q.Where(p => p.Status == st);
        if (!string.IsNullOrWhiteSpace(query.CardType))
            q = q.Where(p => p.CardType == query.CardType!.ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(query.LetterType))
            q = q.Where(p => p.LetterType == query.LetterType!.ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToLowerInvariant();
            q = q.Where(p => p.Title.ToLower().Contains(s) || p.Slug.Contains(s));
        }
        return q;
    }

    public Task ArchiveAsync(string paperId, string adminId, CancellationToken ct)
        => ArchiveAsync(paperId, adminId, writeAudit: true, ct);

    public Task HardDeleteAsync(string paperId, string adminId, CancellationToken ct)
        => HardDeleteAsync(paperId, adminId, force: false, writeAudit: true, ct);

    /// <summary>
    /// Permanently removes a paper and its authoring children. Two safety gates
    /// keep this from destroying live or learner-touched content:
    ///   1. the paper must always already be Archived (forces an explicit archive-first step);
    ///   2. unless <paramref name="force"/> is set, it must have no learner attempts
    ///      (deleting those would orphan history).
    /// When <paramref name="force"/> is set the attempt gate is replaced by an
    /// explicit cascade: the paper's learner attempts and every child row keyed to
    /// them are removed first (see <see cref="PurgeLearnerAttemptsAsync"/>). The
    /// archive gate is never bypassed.
    /// Authoring grandchildren (sections/texts/questions/options) are removed by the
    /// database's ON DELETE CASCADE off the PaperId-keyed parent rows.
    /// </summary>
    private async Task HardDeleteAsync(string paperId, string adminId, bool force, bool writeAudit, CancellationToken ct)
    {
        var paper = await db.ContentPapers
            .Include(p => p.Assets)
            .FirstOrDefaultAsync(x => x.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");

        if (paper.Status != ContentStatus.Archived)
            throw new InvalidOperationException(
                "Only archived papers can be permanently deleted. Archive the paper first.");

        var hasReadingAttempts = await db.ReadingAttempts.AnyAsync(a => a.PaperId == paperId, ct);
        var hasListeningAttempts = await db.ListeningAttempts.AnyAsync(a => a.PaperId == paperId, ct);
        if (hasReadingAttempts || hasListeningAttempts)
        {
            if (!force)
                throw new InvalidOperationException(
                    "Paper has learner attempts and cannot be permanently deleted.");
            await PurgeLearnerAttemptsAsync(paperId, hasReadingAttempts, hasListeningAttempts, ct);
            // Flush the attempt/answer deletes BEFORE removing the paper structure
            // below. ReadingAnswer has a RESTRICT FK to ReadingQuestion, and the
            // questions are dropped via the ReadingParts ON DELETE CASCADE. EF
            // happens to order the answer deletes first today (they are marked
            // Deleted earlier), but that ordering isn't contractual for otherwise-
            // independent entities — flushing here makes it deterministic so a
            // future EF reorder can't make Postgres reject the question cascade.
            // Still inside the bulk transaction, so atomicity is preserved.
            await db.SaveChangesAsync(ct);
        }

        // A ContentPaper used inside a mock bundle is protected by the
        // MockBundleSection.ContentPaperId RESTRICT FK — without this branch the
        // delete (even the old plain delete) would hit a raw FK violation -> 500.
        // Without force it is a hard block with a clear reason; with force we unwind
        // the bundle wiring AND the learner mock-section attempts that reference
        // those sections (themselves a RESTRICT FK), after nulling the optional
        // MockProctoringEvent link so that delete is not blocked either.
        var mockSectionIds = await db.MockBundleSections
            .Where(s => s.ContentPaperId == paperId).Select(s => s.Id).ToListAsync(ct);
        if (mockSectionIds.Count > 0)
        {
            if (!force)
                throw new InvalidOperationException(
                    $"Paper is used in {mockSectionIds.Count} mock bundle section(s); remove it from those mock bundles first.");

            var sectionAttempts = await db.MockSectionAttempts
                .Where(a => mockSectionIds.Contains(a.MockBundleSectionId)).ToListAsync(ct);
            if (sectionAttempts.Count > 0)
            {
                var sectionAttemptIds = sectionAttempts.Select(a => a.Id).ToList();
                var proctoring = await db.MockProctoringEvents
                    .Where(e => e.MockSectionAttemptId != null
                             && sectionAttemptIds.Contains(e.MockSectionAttemptId))
                    .ToListAsync(ct);
                foreach (var e in proctoring) e.MockSectionAttemptId = null;
                db.MockSectionAttempts.RemoveRange(sectionAttempts);
            }
            db.MockBundleSections.RemoveRange(
                await db.MockBundleSections.Where(s => s.ContentPaperId == paperId).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
        }

        // Authoring children keyed directly by PaperId. Grandchildren (reading
        // sections/texts/questions, listening extracts/options) cascade from these
        // at the DB level. Load + RemoveRange (rather than ExecuteDelete) so the
        // InMemory test provider, which has no ExecuteDelete, behaves identically.
        var readingParts = await db.ReadingParts.Where(x => x.PaperId == paperId).ToListAsync(ct);
        var readingDrafts = await db.ReadingExtractionDrafts.Where(x => x.PaperId == paperId).ToListAsync(ct);
        var listeningParts = await db.ListeningParts.Where(x => x.PaperId == paperId).ToListAsync(ct);
        var listeningQuestions = await db.ListeningQuestions.Where(x => x.PaperId == paperId).ToListAsync(ct);
        var listeningDrafts = await db.ListeningExtractionDrafts.Where(x => x.PaperId == paperId).ToListAsync(ct);
        db.ReadingParts.RemoveRange(readingParts);
        db.ReadingExtractionDrafts.RemoveRange(readingDrafts);
        db.ListeningParts.RemoveRange(listeningParts);
        db.ListeningQuestions.RemoveRange(listeningQuestions);
        db.ListeningExtractionDrafts.RemoveRange(listeningDrafts);

        // The parallel ContentItem runtime row (writing/speaking projection).
        var content = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == paperId, ct);
        if (content is not null) db.ContentItems.Remove(content);

        db.ContentPaperAssets.RemoveRange(paper.Assets);
        db.ContentPapers.Remove(paper);

        if (writeAudit) await WriteAuditAsync("ContentPaperDeleted", paper.Id, paper.Title, adminId, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Force-purge path for <see cref="HardDeleteAsync"/>: removes every learner
    /// attempt for the paper plus all rows keyed to those attempts. The attempt
    /// tables carry only a <c>PaperId</c> column (no FK to ContentPaper), so a paper
    /// delete neither cascades to nor is blocked by them — they must be removed
    /// explicitly or they orphan. Children are loaded + RemoveRange'd (not
    /// ExecuteDelete) so the InMemory test provider behaves identically to Postgres;
    /// EF deletes the cascade-configured dependents (answers/notes/feedback) before
    /// the attempts, so the DB-level ON DELETE CASCADE never double-deletes. The
    /// caller flushes (SaveChanges) immediately after this returns, before deleting
    /// the paper structure, so these rows never collide with the parts cascade.
    /// </summary>
    private async Task PurgeLearnerAttemptsAsync(
        string paperId, bool hasReadingAttempts, bool hasListeningAttempts, CancellationToken ct)
    {
        if (hasReadingAttempts)
        {
            var readingAttemptIds = await db.ReadingAttempts
                .Where(a => a.PaperId == paperId).Select(a => a.Id).ToListAsync(ct);

            db.ReadingAnswers.RemoveRange(
                await db.ReadingAnswers.Where(x => readingAttemptIds.Contains(x.ReadingAttemptId)).ToListAsync(ct));
            db.ReadingAnswerRevisions.RemoveRange(
                await db.ReadingAnswerRevisions.Where(x => readingAttemptIds.Contains(x.ReadingAttemptId)).ToListAsync(ct));
            db.ReadingAttemptFeedbacks.RemoveRange(
                await db.ReadingAttemptFeedbacks.Where(x => readingAttemptIds.Contains(x.ReadingAttemptId)).ToListAsync(ct));
            db.ReadingAttempts.RemoveRange(
                await db.ReadingAttempts.Where(a => a.PaperId == paperId).ToListAsync(ct));
        }

        if (hasListeningAttempts)
        {
            var listeningAttemptIds = await db.ListeningAttempts
                .Where(a => a.PaperId == paperId).Select(a => a.Id).ToListAsync(ct);

            db.ListeningAnswers.RemoveRange(
                await db.ListeningAnswers.Where(x => listeningAttemptIds.Contains(x.ListeningAttemptId)).ToListAsync(ct));
            db.ListeningAttemptNotes.RemoveRange(
                await db.ListeningAttemptNotes.Where(x => listeningAttemptIds.Contains(x.ListeningAttemptId)).ToListAsync(ct));
            db.ListeningExpertFeedbacks.RemoveRange(
                await db.ListeningExpertFeedbacks.Where(x => listeningAttemptIds.Contains(x.AttemptId)).ToListAsync(ct));
            db.ListeningAttempts.RemoveRange(
                await db.ListeningAttempts.Where(a => a.PaperId == paperId).ToListAsync(ct));
        }
    }

    private async Task ArchiveAsync(string paperId, string adminId, bool writeAudit, CancellationToken ct)
    {
        var paper = await db.ContentPapers.FirstOrDefaultAsync(x => x.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");
        paper.Status = ContentStatus.Archived;
        paper.ArchivedAt = DateTimeOffset.UtcNow;
        paper.UpdatedAt = paper.ArchivedAt.Value;
        if (string.Equals(paper.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(paper.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            var content = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == paper.Id, ct);
            if (content is not null)
            {
                content.Status = ContentStatus.Archived;
                content.ArchivedAt = paper.ArchivedAt;
                content.UpdatedAt = paper.UpdatedAt;
            }
        }
        if (writeAudit) await WriteAuditAsync("ContentPaperArchived", paper.Id, paper.Title, adminId, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task UnpublishAsync(string paperId, string adminId, CancellationToken ct)
        => UnpublishAsync(paperId, adminId, writeAudit: true, ct);

    private async Task UnpublishAsync(string paperId, string adminId, bool writeAudit, CancellationToken ct)
    {
        var paper = await db.ContentPapers.FirstOrDefaultAsync(x => x.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");
        if (paper.Status == ContentStatus.Draft)
            throw new InvalidOperationException("Paper is already in Draft status.");
        if (paper.Status == ContentStatus.Archived)
            throw new InvalidOperationException("Cannot unpublish an archived paper. Restore it first.");
        paper.Status = ContentStatus.Draft;
        paper.UpdatedAt = DateTimeOffset.UtcNow;
        if (string.Equals(paper.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(paper.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            var content = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == paper.Id, ct);
            if (content is not null)
            {
                content.Status = ContentStatus.Draft;
                content.UpdatedAt = paper.UpdatedAt;
            }
        }
        if (writeAudit) await WriteAuditAsync("ContentPaperUnpublished", paper.Id, paper.Title, adminId, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task PublishAsync(string paperId, string adminId, CancellationToken ct)
        => PublishAsync(paperId, adminId, writeAudit: true, ct);

    private async Task PublishAsync(string paperId, string adminId, bool writeAudit, CancellationToken ct)
    {
        var paper = await db.ContentPapers
            .Include(p => p.Assets)
            .FirstOrDefaultAsync(x => x.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");

        // Listening publishes with NO content constraints (owner decision): any
        // paper can go live as-is and the learner surface renders friendly
        // empty-state messages for missing parts. The source-provenance and
        // required-asset-role gates are skipped for Listening only; other subtests
        // keep them. The advisory ListeningStructureService report still surfaces
        // what's incomplete to authors without blocking the publish.
        var isListening = string.Equals(paper.SubtestCode, "listening", StringComparison.OrdinalIgnoreCase);

        if (!isListening && string.IsNullOrWhiteSpace(paper.SourceProvenance))
        {
            throw new InvalidOperationException("SourceProvenance is required before publishing.");
        }

        if (!isListening)
        {
            EnforceRequiredAssetRoles(paper);
        }

        // Decision 2 — publishing is NEVER blocked on rule-conformance grounds.
        // Structural problems (e.g. a 4-option Part B, a non-canonical shape)
        // are recorded as a NON-BLOCKING conformance-warning audit and surfaced
        // read-only on /admin/conformance; publishing always proceeds.
        //
        // The conformance pass is purely advisory, so a failure to *compute* it
        // (e.g. a transient query error while validating a freshly-authored
        // paper) must also never block publishing. Wrap it defensively: if the
        // check can't run, publish proceeds and the conformance dashboard simply
        // lacks fresh warnings for this revision.
        try
        {
            if (string.Equals(paper.SubtestCode, "reading", StringComparison.OrdinalIgnoreCase))
            {
                var report = await new ReadingStructureService(db).ValidatePaperAsync(paper.Id, ct);
                await RecordPublishConformanceWarningsAsync(paper, report.Issues.Select(i => $"[{i.Severity}] {i.Message}"), adminId, ct);
            }
            else if (string.Equals(paper.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
            {
                var report = SpeakingContentStructure.Validate(paper);
                await RecordPublishConformanceWarningsAsync(paper, report.Issues.Select(i => $"[{i.Severity}] {i.Message}"), adminId, ct);
            }
            else if (string.Equals(paper.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase))
            {
                var report = WritingContentStructure.Validate(paper);
                await RecordPublishConformanceWarningsAsync(paper, report.Issues.Select(i => $"[{i.Severity}] {i.Message}"), adminId, ct);
            }
        }
        catch (Exception)
        {
            // Advisory only — never block the publish on a conformance-check failure.
        }

        var now = DateTimeOffset.UtcNow;
        paper.Status = ContentStatus.Published;
        paper.PublishedAt = now;
        paper.UpdatedAt = now;
        if (string.Equals(paper.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            await UpsertSpeakingContentItemAsync(paper, adminId, now, ct);
        }
        if (string.Equals(paper.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase))
        {
            await UpsertWritingContentItemAsync(paper, adminId, now, ct);
        }
        if (writeAudit) await WriteAuditAsync("ContentPaperPublished", paper.Id, paper.Title, adminId, ct);
        await db.SaveChangesAsync(ct);
    }

    private void EnforceRequiredAssetRoles(ContentPaper paper)
    {
        if (!RequiredRoles.TryGetValue(paper.SubtestCode, out var requiredRoles) || requiredRoles.Count == 0)
        {
            return;
        }

        var presentRoles = paper.Assets.Select(a => a.Role).ToHashSet();
        var missingRoles = requiredRoles.Where(role => !presentRoles.Contains(role)).ToList();
        if (missingRoles.Count > 0)
        {
            throw new InvalidOperationException(
                "Content paper is missing required asset roles before publishing: "
                + string.Join(", ", missingRoles));
        }
    }

    public async Task<ContentPaperAsset> AttachAssetAsync(
        string paperId, ContentPaperAssetAttach args, string adminId, CancellationToken ct)
    {
        var paper = await db.ContentPapers
            .Include(p => p.Assets)
            .FirstOrDefaultAsync(x => x.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");
        var media = await db.MediaAssets.FirstOrDefaultAsync(x => x.Id == args.MediaAssetId, ct)
            ?? throw new InvalidOperationException("Media asset not found.");

        // If MakePrimary, flip any existing primary for the same (role, part)
        // to non-primary first. Application-level guard that also works on
        // the in-memory provider.
        if (args.MakePrimary)
        {
            foreach (var existing in paper.Assets
                .Where(a => a.Role == args.Role && a.Part == args.Part && a.IsPrimary))
            {
                existing.IsPrimary = false;
            }
        }

        var now = DateTimeOffset.UtcNow;
        var row = new ContentPaperAsset
        {
            Id = Guid.NewGuid().ToString("N"),
            PaperId = paper.Id,
            Role = args.Role,
            Part = args.Part,
            MediaAssetId = media.Id,
            Title = args.Title,
            DisplayOrder = args.DisplayOrder,
            IsPrimary = args.MakePrimary,
            CreatedAt = now,
        };
        db.ContentPaperAssets.Add(row);
        paper.UpdatedAt = now;
        await WriteAuditAsync("ContentPaperAssetAttached", paper.Id,
            $"{args.Role}:{args.Part ?? "-"}:{media.Id}", adminId, ct);
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<bool> RemoveAssetAsync(string paperId, string assetId, string adminId, CancellationToken ct)
    {
        var row = await db.ContentPaperAssets
            .FirstOrDefaultAsync(x => x.PaperId == paperId && x.Id == assetId, ct);
        if (row is null) return false;
        db.ContentPaperAssets.Remove(row);
        await WriteAuditAsync("ContentPaperAssetRemoved", paperId, assetId, adminId, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task EnsureSlugUnique(string slug, CancellationToken ct)
    {
        if (await db.ContentPapers.AnyAsync(p => p.Slug == slug, ct))
            throw new InvalidOperationException($"Slug '{slug}' already exists.");
    }

    private async Task UpsertSpeakingContentItemAsync(ContentPaper paper, string adminId, DateTimeOffset now, CancellationToken ct)
    {
        var detail = SpeakingContentStructure.BuildContentItemDetail(paper);
        var criteriaFocus = SpeakingContentStructure.ReadStringList(
            SpeakingContentStructure.ReadValue(detail, "criteriaFocus"));
        var content = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == paper.Id, ct);
        if (content is null)
        {
            content = new ContentItem
            {
                Id = paper.Id,
                ContentType = "roleplay",
                SubtestCode = "speaking",
                CreatedAt = now,
                CreatedBy = adminId,
            };
            db.ContentItems.Add(content);
        }

        content.Title = paper.Title;
        content.ProfessionId = paper.AppliesToAllProfessions ? null : paper.ProfessionId;
        content.Difficulty = paper.Difficulty;
        content.EstimatedDurationMinutes = paper.EstimatedDurationMinutes;
        content.CriteriaFocusJson = JsonSupport.Serialize(criteriaFocus);
        content.ScenarioType = paper.CardType ?? SpeakingContentStructure.ReadString(detail, "clinicalTopic");
        content.ModeSupportJson = JsonSupport.Serialize(new[] { "self", "exam" });
        content.PublishedRevisionId = paper.PublishedRevisionId ?? paper.Id;
        content.Status = ContentStatus.Published;
        content.CaseNotes = SpeakingContentStructure.ReadString(detail, "background");
        content.DetailJson = JsonSupport.Serialize(detail);
        content.SourceType = "content-paper";
        content.SourceProvenance = MapProvenanceCode(paper.SourceProvenance);
        content.RightsStatus = "owned";
        content.FreshnessConfidence = "current";
        content.UpdatedAt = now;
        content.PublishedAt = now;
    }

    private async Task UpsertWritingContentItemAsync(ContentPaper paper, string adminId, DateTimeOffset now, CancellationToken ct)
    {
        var detail = WritingContentStructure.BuildContentItemDetail(paper);
        var criteriaFocus = SpeakingContentStructure.ReadStringList(
            SpeakingContentStructure.ReadValue(detail, "criteriaFocus"));
        var content = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == paper.Id, ct);
        if (content is null)
        {
            content = new ContentItem
            {
                Id = paper.Id,
                ContentType = "writing_task",
                SubtestCode = "writing",
                CreatedAt = now,
                CreatedBy = adminId,
            };
            db.ContentItems.Add(content);
        }

        content.Title = paper.Title;
        content.ProfessionId = paper.AppliesToAllProfessions ? null : paper.ProfessionId;
        content.Difficulty = paper.Difficulty;
        content.EstimatedDurationMinutes = paper.EstimatedDurationMinutes;
        content.CriteriaFocusJson = JsonSupport.Serialize(criteriaFocus);
        content.ScenarioType = paper.LetterType;
        content.ModeSupportJson = JsonSupport.Serialize(new[] { "learning", "exam" });
        content.PublishedRevisionId = paper.PublishedRevisionId ?? paper.Id;
        content.Status = ContentStatus.Published;
        content.CaseNotes = WritingContentStructure.BuildCaseNotesText(detail);
        content.DetailJson = JsonSupport.Serialize(detail);
        content.ModelAnswerJson = JsonSupport.Serialize(WritingContentStructure.BuildModelAnswerPayload(paper));
        content.SourceType = "content-paper";
        content.SourceProvenance = MapProvenanceCode(paper.SourceProvenance);
        content.RightsStatus = "owned";
        content.FreshnessConfidence = "current";
        content.UpdatedAt = now;
        content.PublishedAt = now;
    }

    // ContentItem.SourceProvenance is a coded varchar(32) (original,
    // official_sample, recall, benchmark, contributed, curated) while
    // ContentPaper.SourceProvenance is free-text citation prose. Copying the
    // prose verbatim overflowed the column, so every Writing/Speaking publish
    // 500-failed once the citation passed 32 characters. The full citation
    // stays on the paper row; the item only carries the coded class.
    private static string MapProvenanceCode(string? paperProvenance)
    {
        if (string.IsNullOrWhiteSpace(paperProvenance)) return "curated";
        var normalized = paperProvenance.Trim().ToLowerInvariant();
        return normalized switch
        {
            "original" or "official_sample" or "recall" or "benchmark" or "contributed" or "curated" => normalized,
            _ => "curated",
        };
    }

    private static string NormalizeSlug(string input)
    {
        var lower = input.Trim().ToLowerInvariant();
        var sb = new System.Text.StringBuilder();
        foreach (var c in lower)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c is ' ' or '-' or '_' or '/' or '.') sb.Append('-');
        }
        var slug = sb.ToString();
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }

    // Decision 2 — record (never block) rule-conformance problems found at
    // publish time. Issues are written to the unbounded Details column and
    // surfaced read-only on /admin/conformance; publishing always proceeds.
    private async Task RecordPublishConformanceWarningsAsync(
        ContentPaper paper,
        IEnumerable<string> issues,
        string adminId,
        CancellationToken ct)
    {
        var messages = issues.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
        if (messages.Count == 0) return;
        await WriteAuditAsync(
            "ContentPaperPublishConformanceWarning",
            paper.Id,
            $"{paper.Title} — {messages.Count} conformance warning(s): {string.Join(" | ", messages.Take(50))}",
            adminId, ct);
    }

    private Task WriteAuditAsync(string action, string resourceId, string? details, string adminId, CancellationToken ct)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorName = adminId,
            Action = action,
            ResourceType = "ContentPaper",
            ResourceId = resourceId,
            Details = details,
        });
        return Task.CompletedTask;
    }
}

/// <summary>Thrown by <see cref="ContentPaperService.CreateAsync"/> when a
/// Writing task is created without the required integrity acknowledgement.
/// The admin endpoint catches this and converts it into a structured
/// <c>400 integrity_acknowledgement_required</c> response.</summary>
public sealed class ContentIntegrityAcknowledgementRequiredException(string message)
    : InvalidOperationException(message);
