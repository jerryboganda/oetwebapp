using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
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
    Task<ContentPaper> UpdateAsync(string paperId, ContentPaperUpdate args, string adminId, CancellationToken ct);
    Task<ContentPaper?> GetAsync(string paperId, CancellationToken ct);
    Task<IReadOnlyList<ContentPaper>> ListAsync(ContentPaperQuery query, CancellationToken ct);
    Task ArchiveAsync(string paperId, string adminId, CancellationToken ct);
    Task PublishAsync(string paperId, string adminId, CancellationToken ct);

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
    string? SourceProvenance);

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
        ["reading"]   = new() { PaperAssetRole.QuestionPaper, PaperAssetRole.AnswerKey },
        ["writing"]   = new() { PaperAssetRole.CaseNotes },
        ["speaking"]  = new() { PaperAssetRole.RoleCard },
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

        var slug = NormalizeSlug(args.Slug ?? args.Title);
        await EnsureSlugUnique(slug, ct);

        var now = DateTimeOffset.UtcNow;
        var paper = new ContentPaper
        {
            Id = Guid.NewGuid().ToString("N"),
            SubtestCode = args.SubtestCode.Trim().ToLowerInvariant(),
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
        };
        db.ContentPapers.Add(paper);
        await WriteAuditAsync("ContentPaperCreated", paper.Id, paper.Title, adminId, ct);
        await db.SaveChangesAsync(ct);
        return paper;
    }

    public async Task<ContentPaper> UpdateAsync(string paperId, ContentPaperUpdate args, string adminId, CancellationToken ct)
    {
        var paper = await db.ContentPapers.FirstOrDefaultAsync(x => x.Id == paperId, ct)
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
        var q = db.ContentPapers.AsNoTracking().AsQueryable();
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
        await WriteAuditAsync("ContentPaperArchived", paper.Id, paper.Title, adminId, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task PublishAsync(string paperId, string adminId, CancellationToken ct)
    {
        var paper = await db.ContentPapers
            .Include(p => p.Assets)
            .FirstOrDefaultAsync(x => x.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");

        // Gate 1: provenance is required before publish.
        if (string.IsNullOrWhiteSpace(paper.SourceProvenance))
            throw new InvalidOperationException("SourceProvenance is required before publishing.");

        // Gate 2: required roles must be present and primary.
        var required = RequiredRolesFor(paper.SubtestCode);
        var present = paper.Assets
            .Where(a => a.IsPrimary)
            .Select(a => a.Role)
            .ToHashSet();
        var missing = required.Except(present).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Missing required primary asset(s) for {paper.SubtestCode}: {string.Join(", ", missing)}.");

        if (string.Equals(paper.SubtestCode, "reading", StringComparison.OrdinalIgnoreCase))
        {
            var readingValidation = await new ReadingStructureService(db).ValidatePaperAsync(paper.Id, ct);
            if (!readingValidation.IsPublishReady)
            {
                var blockers = readingValidation.Issues
                    .Where(i => string.Equals(i.Severity, "error", StringComparison.OrdinalIgnoreCase))
                    .Select(i => $"{i.Code}: {i.Message}")
                    .ToList();
                throw new InvalidOperationException(
                    $"Reading structure is not publish-ready: {string.Join("; ", blockers)}");
            }
        }

        var now = DateTimeOffset.UtcNow;
        paper.Status = ContentStatus.Published;
        paper.PublishedAt = now;
        paper.UpdatedAt = now;
        await WriteAuditAsync("ContentPaperPublished", paper.Id, paper.Title, adminId, ct);
        await db.SaveChangesAsync(ct);
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
