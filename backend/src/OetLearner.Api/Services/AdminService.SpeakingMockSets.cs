using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

// Wave 3 of docs/SPEAKING-MODULE-PLAN.md.
//
// Admin CRUD for `SpeakingMockSet`. Mirrors `CreateContentAsync`/`UpdateContentAsync`
// in shape (audit-logged, transactional) but is tightly scoped to the
// curatorial speaking-mock-set entity — the actual role-play
// `ContentItem` rows are managed via the existing /v1/admin/content
// endpoints.
//
// Publish gate (mirrors `IContentPaperService.RequiredRolesFor` philosophy):
//   - Both referenced ContentItem ids must exist and have
//     SubtestCode == "speaking".
//   - Title must be non-empty (already enforced by request validation).
//
// Permissions reuse the existing `AdminContentRead` / `AdminContentWrite`
// / `AdminContentPublish` grants — mock sets are a curatorial
// composition over content the AdminContent permission already governs.
public partial class AdminService
{
    public async Task<object> ListSpeakingMockSetsAsync(
        string? status,
        string? professionId,
        CancellationToken ct)
    {
        var q = db.SpeakingMockSets.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(professionId))
        {
            q = q.Where(x => x.ProfessionId == professionId);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (Enum.TryParse<SpeakingMockSetStatus>(status, ignoreCase: true, out var parsed))
            {
                q = q.Where(x => x.Status == parsed);
            }
        }
        var rows = await q
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Title)
            .ToListAsync(ct);

        // Resolve referenced role-play titles in a single batched query so
        // the admin list can render "Pneumonia handover ↔ Diabetes review"
        // without N+1.
        var ids = rows.SelectMany(r => new[] { r.RolePlay1ContentId, r.RolePlay2ContentId })
            .Distinct()
            .ToArray();
        var contentLookup = await db.ContentItems
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Title, x.SubtestCode, x.Status })
            .ToDictionaryAsync(x => x.Id, ct);

        return new
        {
            mockSets = rows.Select(r => new
            {
                mockSetId = r.Id,
                title = r.Title,
                description = r.Description,
                professionId = r.ProfessionId,
                difficulty = r.Difficulty,
                status = r.Status.ToString().ToLowerInvariant(),
                criteriaFocus = string.IsNullOrWhiteSpace(r.CriteriaFocus) ? Array.Empty<string>() : r.CriteriaFocus.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                tags = string.IsNullOrWhiteSpace(r.Tags) ? Array.Empty<string>() : r.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                sortOrder = r.SortOrder,
                rolePlay1 = new
                {
                    contentId = r.RolePlay1ContentId,
                    title = contentLookup.TryGetValue(r.RolePlay1ContentId, out var c1) ? c1.Title : "(missing)",
                    isSpeaking = c1 is not null && string.Equals(c1.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase),
                },
                rolePlay2 = new
                {
                    contentId = r.RolePlay2ContentId,
                    title = contentLookup.TryGetValue(r.RolePlay2ContentId, out var c2) ? c2.Title : "(missing)",
                    isSpeaking = c2 is not null && string.Equals(c2.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase),
                },
                createdAt = r.CreatedAt,
                updatedAt = r.UpdatedAt,
                publishedAt = r.PublishedAt,
            }).ToArray(),
        };
    }

    public async Task<object> GetSpeakingMockSetAsync(string mockSetId, CancellationToken ct)
    {
        var row = await db.SpeakingMockSets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == mockSetId, ct)
            ?? throw ApiException.NotFound("speaking_mock_set_not_found", "That speaking mock set does not exist.");
        return await ProjectMockSetDetailAsync(row, ct);
    }

    public async Task<object> CreateSpeakingMockSetAsync(
        string adminId,
        string adminName,
        AdminSpeakingMockSetCreateRequest request,
        CancellationToken ct)
    {
        ValidateMockSetReferences(request.Title, request.RolePlay1ContentId, request.RolePlay2ContentId);
        await EnsureMockSetReferencesAreSpeakingAsync(request.RolePlay1ContentId, request.RolePlay2ContentId, ct);

        var now = DateTimeOffset.UtcNow;
        var id = $"sms-{Guid.NewGuid():N}";
        var entity = new SpeakingMockSet
        {
            Id = id,
            Title = request.Title.Trim(),
            ProfessionId = string.IsNullOrWhiteSpace(request.ProfessionId) ? "nursing" : request.ProfessionId.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            RolePlay1ContentId = request.RolePlay1ContentId.Trim(),
            RolePlay2ContentId = request.RolePlay2ContentId.Trim(),
            Difficulty = string.IsNullOrWhiteSpace(request.Difficulty) ? "core" : request.Difficulty.Trim(),
            CriteriaFocus = NormaliseCsv(request.CriteriaFocus),
            Tags = NormaliseCsv(request.Tags),
            SortOrder = request.SortOrder ?? 0,
            Status = SpeakingMockSetStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        db.SpeakingMockSets.Add(entity);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Created", "SpeakingMockSet", id,
            $"Created mock set: {entity.Title}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return await ProjectMockSetDetailAsync(entity, ct);
    }

    public async Task<object> UpdateSpeakingMockSetAsync(
        string adminId,
        string adminName,
        string mockSetId,
        AdminSpeakingMockSetUpdateRequest request,
        CancellationToken ct)
    {
        var entity = await db.SpeakingMockSets.FirstOrDefaultAsync(x => x.Id == mockSetId, ct)
            ?? throw ApiException.NotFound("speaking_mock_set_not_found", "That speaking mock set does not exist.");

        if (entity.Status == SpeakingMockSetStatus.Archived)
        {
            throw ApiException.Conflict("speaking_mock_set_archived",
                "Archived mock sets are read-only.");
        }

        if (request.Title is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                throw ApiException.Validation("SPEAKING_MOCK_SET_TITLE_REQUIRED", "Title is required.");
            }
            entity.Title = request.Title.Trim();
        }
        if (request.Description is not null) entity.Description = request.Description.Trim();
        if (request.ProfessionId is not null && !string.IsNullOrWhiteSpace(request.ProfessionId))
        {
            entity.ProfessionId = request.ProfessionId.Trim();
        }
        if (request.Difficulty is not null && !string.IsNullOrWhiteSpace(request.Difficulty))
        {
            entity.Difficulty = request.Difficulty.Trim();
        }
        if (request.CriteriaFocus is not null) entity.CriteriaFocus = NormaliseCsv(request.CriteriaFocus);
        if (request.Tags is not null) entity.Tags = NormaliseCsv(request.Tags);
        if (request.SortOrder.HasValue) entity.SortOrder = request.SortOrder.Value;

        var refsChanged = false;
        if (request.RolePlay1ContentId is not null && request.RolePlay1ContentId != entity.RolePlay1ContentId)
        {
            entity.RolePlay1ContentId = request.RolePlay1ContentId.Trim();
            refsChanged = true;
        }
        if (request.RolePlay2ContentId is not null && request.RolePlay2ContentId != entity.RolePlay2ContentId)
        {
            entity.RolePlay2ContentId = request.RolePlay2ContentId.Trim();
            refsChanged = true;
        }
        if (refsChanged)
        {
            await EnsureMockSetReferencesAreSpeakingAsync(entity.RolePlay1ContentId, entity.RolePlay2ContentId, ct);
        }

        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "SpeakingMockSet", mockSetId,
            $"Updated mock set: {entity.Title}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return await ProjectMockSetDetailAsync(entity, ct);
    }

    public async Task<object> PublishSpeakingMockSetAsync(
        string adminId,
        string adminName,
        string mockSetId,
        CancellationToken ct)
    {
        var entity = await db.SpeakingMockSets.FirstOrDefaultAsync(x => x.Id == mockSetId, ct)
            ?? throw ApiException.NotFound("speaking_mock_set_not_found", "That speaking mock set does not exist.");

        if (entity.Status == SpeakingMockSetStatus.Archived)
        {
            throw ApiException.Conflict("speaking_mock_set_archived",
                "Archived mock sets cannot be published.");
        }

        // Re-validate at the publish gate — admins may have edited the role-play
        // content or its subtest code in between create/update and publish.
        await EnsureMockSetReferencesAreSpeakingAsync(entity.RolePlay1ContentId, entity.RolePlay2ContentId, ct);

        entity.Status = SpeakingMockSetStatus.Published;
        entity.PublishedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = entity.PublishedAt.Value;

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Published", "SpeakingMockSet", mockSetId,
            $"Published mock set: {entity.Title}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return await ProjectMockSetDetailAsync(entity, ct);
    }

    public async Task<object> ArchiveSpeakingMockSetAsync(
        string adminId,
        string adminName,
        string mockSetId,
        CancellationToken ct)
    {
        var entity = await db.SpeakingMockSets.FirstOrDefaultAsync(x => x.Id == mockSetId, ct)
            ?? throw ApiException.NotFound("speaking_mock_set_not_found", "That speaking mock set does not exist.");

        if (entity.Status == SpeakingMockSetStatus.Archived)
        {
            return await ProjectMockSetDetailAsync(entity, ct);
        }

        entity.Status = SpeakingMockSetStatus.Archived;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Archived", "SpeakingMockSet", mockSetId,
            $"Archived mock set: {entity.Title}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return await ProjectMockSetDetailAsync(entity, ct);
    }

    private static void ValidateMockSetReferences(string title, string rolePlay1Id, string rolePlay2Id)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw ApiException.Validation("SPEAKING_MOCK_SET_TITLE_REQUIRED", "Title is required.");
        }
        if (string.IsNullOrWhiteSpace(rolePlay1Id) || string.IsNullOrWhiteSpace(rolePlay2Id))
        {
            throw ApiException.Validation("SPEAKING_MOCK_SET_ROLE_PLAYS_REQUIRED",
                "Both role-play content ids are required.");
        }
        if (string.Equals(rolePlay1Id.Trim(), rolePlay2Id.Trim(), StringComparison.Ordinal))
        {
            throw ApiException.Validation("SPEAKING_MOCK_SET_DUPLICATE_ROLE_PLAYS",
                "Role-play 1 and role-play 2 must reference different content items.");
        }
    }

    private async Task EnsureMockSetReferencesAreSpeakingAsync(string r1, string r2, CancellationToken ct)
    {
        var contents = await db.ContentItems
            .AsNoTracking()
            .Where(x => x.Id == r1 || x.Id == r2)
            .Select(x => new { x.Id, x.SubtestCode })
            .ToListAsync(ct);
        if (contents.Count != 2)
        {
            throw ApiException.Validation("SPEAKING_MOCK_SET_CONTENT_NOT_FOUND",
                "One or both referenced role-play content items do not exist.");
        }
        foreach (var c in contents)
        {
            if (!string.Equals(c.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
            {
                throw ApiException.Validation("SPEAKING_MOCK_SET_NOT_SPEAKING",
                    $"Content '{c.Id}' is not a speaking role-play.");
            }
        }
    }

    private static string NormaliseCsv(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return string.Join(',', raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private async Task<object> ProjectMockSetDetailAsync(SpeakingMockSet entity, CancellationToken ct)
    {
        var refs = await db.ContentItems.AsNoTracking()
            .Where(x => x.Id == entity.RolePlay1ContentId || x.Id == entity.RolePlay2ContentId)
            .Select(x => new { x.Id, x.Title, x.SubtestCode, x.Status })
            .ToListAsync(ct);
        var byId = refs.ToDictionary(x => x.Id);
        return new
        {
            mockSetId = entity.Id,
            title = entity.Title,
            description = entity.Description,
            professionId = entity.ProfessionId,
            difficulty = entity.Difficulty,
            status = entity.Status.ToString().ToLowerInvariant(),
            criteriaFocus = string.IsNullOrWhiteSpace(entity.CriteriaFocus) ? Array.Empty<string>() : entity.CriteriaFocus.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            tags = string.IsNullOrWhiteSpace(entity.Tags) ? Array.Empty<string>() : entity.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            sortOrder = entity.SortOrder,
            rolePlay1 = new
            {
                contentId = entity.RolePlay1ContentId,
                title = byId.TryGetValue(entity.RolePlay1ContentId, out var c1) ? c1.Title : "(missing)",
                isSpeaking = c1 is not null && string.Equals(c1.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase),
            },
            rolePlay2 = new
            {
                contentId = entity.RolePlay2ContentId,
                title = byId.TryGetValue(entity.RolePlay2ContentId, out var c2) ? c2.Title : "(missing)",
                isSpeaking = c2 is not null && string.Equals(c2.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase),
            },
            createdAt = entity.CreatedAt,
            updatedAt = entity.UpdatedAt,
            publishedAt = entity.PublishedAt,
        };
    }
}
