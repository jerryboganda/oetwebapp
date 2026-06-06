using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Reading;

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
    Task ArchiveAsync(string paperId, string adminId, CancellationToken ct);
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

public sealed class ContentPaperService(LearnerDbContext db) : IContentPaperService
{
    /// <summary>Publish gate — each subtest's minimum required roles. Keep
    /// this in sync with §2 of <c>docs/CONTENT-UPLOAD-PLAN.md</c>.</summary>
    private static readonly Dictionary<string, HashSet<PaperAssetRole>> RequiredRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["listening"] = new() { PaperAssetRole.Audio, PaperAssetRole.QuestionPaper, PaperAssetRole.AudioScript, PaperAssetRole.AnswerKey },
        ["reading"] = new() { PaperAssetRole.QuestionPaper },
        ["writing"] = new() { PaperAssetRole.CaseNotes, PaperAssetRole.ModelAnswer },
        ["speaking"] = new()
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
        if (!WritingContentStructure.IsLetterTypeAllowedForProfession(args.ProfessionId, args.LetterType))
            throw new ArgumentException(
                $"Letter type '{args.LetterType}' is not allowed for profession '{args.ProfessionId}'.");
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

    public async Task SubmitForReviewAsync(string paperId, string adminId, CancellationToken ct)
    {
        var paper = await db.ContentPapers.FirstOrDefaultAsync(x => x.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");
        if (paper.Status != ContentStatus.Draft)
            throw new InvalidOperationException(
                $"Paper must be Draft to submit for review (currently {paper.Status}).");

        paper.Status = ContentStatus.InReview;
        paper.UpdatedAt = DateTimeOffset.UtcNow;
        await WriteAuditAsync("ContentPaperSubmittedForReview", paper.Id, paper.Title, adminId, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task ApproveAndPublishAsync(string paperId, string adminId, CancellationToken ct)
    {
        var paper = await db.ContentPapers.FirstOrDefaultAsync(x => x.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");
        if (paper.Status != ContentStatus.InReview)
            throw new InvalidOperationException(
                $"Paper must be InReview to approve & publish (currently {paper.Status}).");

        // Audit the approval before publish runs — keeps the reviewer ID on
        // the trail even if a publish-gate exception aborts the transaction.
        await WriteAuditAsync("ContentPaperApprovedForPublish", paper.Id, paper.Title, adminId, ct);
        await db.SaveChangesAsync(ct);

        // Delegate to the existing publish path so all gate logic (assets,
        // provenance, subtest validators) is reused untouched.
        await PublishAsync(paperId, adminId, ct);
    }

    public async Task RejectAsync(string paperId, string adminId, string reason, CancellationToken ct)
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
        await db.SaveChangesAsync(ct);
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
        var q = db.ContentPapers
            .Include(p => p.Assets)
                .ThenInclude(a => a.MediaAsset)
            .AsNoTracking()
            .AsQueryable();
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
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 200);
        return await q
            .OrderByDescending(p => p.Priority)
            .ThenByDescending(p => p.UpdatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);
    }

    public async Task ArchiveAsync(string paperId, string adminId, CancellationToken ct)
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
        await WriteAuditAsync("ContentPaperArchived", paper.Id, paper.Title, adminId, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UnpublishAsync(string paperId, string adminId, CancellationToken ct)
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
        await WriteAuditAsync("ContentPaperUnpublished", paper.Id, paper.Title, adminId, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task PublishAsync(string paperId, string adminId, CancellationToken ct)
    {
        var paper = await db.ContentPapers
            .Include(p => p.Assets)
            .FirstOrDefaultAsync(x => x.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");

        if (string.IsNullOrWhiteSpace(paper.SourceProvenance))
        {
            throw new InvalidOperationException("SourceProvenance is required before publishing.");
        }

        EnforceRequiredAssetRoles(paper);

        if (string.Equals(paper.SubtestCode, "reading", StringComparison.OrdinalIgnoreCase))
        {
            var report = await new ReadingStructureService(db).ValidatePaperAsync(paper.Id, ct);
            if (!report.IsPublishReady)
            {
                var errors = report.Issues
                    .Where(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase))
                    .Select(issue => issue.Message)
                    .DefaultIfEmpty("Reading paper is not publish-ready.");
                throw new InvalidOperationException("Reading paper is not publish-ready: " + string.Join(" ", errors));
            }
        }

        if (string.Equals(paper.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            var report = SpeakingContentStructure.Validate(paper);
            if (!report.IsPublishReady)
            {
                var errors = report.Issues
                    .Where(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase))
                    .Select(issue => issue.Message)
                    .DefaultIfEmpty("Speaking structure is not publish-ready.");
                throw new InvalidOperationException("Speaking structure is not publish-ready: " + string.Join(" ", errors));
            }
        }

        if (string.Equals(paper.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase))
        {
            var report = WritingContentStructure.Validate(paper);
            if (!report.IsPublishReady)
            {
                var errors = report.Issues
                    .Where(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase))
                    .Select(issue => issue.Message)
                    .DefaultIfEmpty("Writing structure is not publish-ready.");
                throw new InvalidOperationException("Writing structure is not publish-ready: " + string.Join(" ", errors));
            }
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
        await WriteAuditAsync("ContentPaperPublished", paper.Id, paper.Title, adminId, ct);
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
        content.SourceProvenance = string.IsNullOrWhiteSpace(paper.SourceProvenance)
            ? "curated"
            : paper.SourceProvenance!;
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
        content.SourceProvenance = string.IsNullOrWhiteSpace(paper.SourceProvenance)
            ? "curated"
            : paper.SourceProvenance!;
        content.RightsStatus = "owned";
        content.FreshnessConfidence = "current";
        content.UpdatedAt = now;
        content.PublishedAt = now;
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
