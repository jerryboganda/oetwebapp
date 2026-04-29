using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Content;

public sealed class MediaAssetAccessService(LearnerDbContext db, IContentEntitlementService contentEntitlements)
{
    public async Task<bool> CanAccessAsync(ClaimsPrincipal principal, string mediaAssetId, CancellationToken ct)
    {
        var media = await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(asset => asset.Id == mediaAssetId, ct);
        return media is not null && await CanAccessAsync(principal, media, ct);
    }

    public async Task<bool> CanAccessAsync(ClaimsPrincipal principal, MediaAsset media, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        var role = principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (string.Equals(role, ApplicationUserRoles.Admin, StringComparison.OrdinalIgnoreCase)
            || string.Equals(media.UploadedBy, userId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(role, ApplicationUserRoles.Learner, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (await IsPublishedFreePreviewMediaAsync(media.Id, ct))
        {
            return true;
        }

        var profession = principal.FindFirstValue("prof") ?? principal.FindFirstValue("profession");
        if (string.IsNullOrWhiteSpace(profession))
        {
            profession = await db.Users
                .AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => user.ActiveProfessionId)
                .SingleOrDefaultAsync(ct);
        }

        var normalizedProfession = profession?.Trim().ToLowerInvariant();
        var learnerVisibleRoles = LearnerVisiblePaperAssetRoles;
        var candidatePapers = await db.ContentPaperAssets
            .AsNoTracking()
            .Where(asset => asset.MediaAssetId == media.Id
                && asset.IsPrimary
                && learnerVisibleRoles.Contains(asset.Role)
                && asset.Paper != null
                && asset.Paper.Status == ContentStatus.Published
                && (asset.Paper.AppliesToAllProfessions
                    || (!string.IsNullOrWhiteSpace(normalizedProfession)
                        && asset.Paper.ProfessionId == normalizedProfession)))
            .Select(asset => asset.Paper!)
            .ToListAsync(ct);

        foreach (var paper in candidatePapers)
        {
            var entitlement = await contentEntitlements.AllowAccessAsync(userId, paper, ct);
            if (entitlement.Allowed)
            {
                return true;
            }
        }

        return false;
    }

    private static readonly PaperAssetRole[] LearnerVisiblePaperAssetRoles =
    [
        PaperAssetRole.Audio,
        PaperAssetRole.QuestionPaper,
        PaperAssetRole.CaseNotes,
        PaperAssetRole.RoleCard,
        PaperAssetRole.WarmUpQuestions,
    ];

    private Task<bool> IsPublishedFreePreviewMediaAsync(string mediaAssetId, CancellationToken ct)
        => db.FreePreviewAssets
            .AsNoTracking()
            .AnyAsync(preview =>
                preview.MediaAssetId == mediaAssetId
                && preview.Status == ContentStatus.Published, ct);
}
