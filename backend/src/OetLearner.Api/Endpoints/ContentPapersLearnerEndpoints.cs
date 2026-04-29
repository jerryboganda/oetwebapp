using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Public learner endpoints for the Content Paper subsystem (Slice 6).
/// List / detail are authenticated but not admin-gated.
///
/// For asset download we issue short-lived signed URLs via the existing
/// media-authorized-fetch pattern on the frontend; the endpoint returns
/// the storage key which the `/v1/media/...` pipeline already serves.
/// </summary>
public static class ContentPapersLearnerEndpoints
{
    public static IEndpointRouteBuilder MapContentPapersLearnerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/papers")
            .RequireAuthorization()
            .RequireRateLimiting("PerUser");

        // ── List — filtered by subtest, profession-aware ───────────────────
        group.MapGet("", async (
            LearnerDbContext db, HttpContext http, CancellationToken ct,
            string? subtest, string? cardType, string? letterType,
            string? search, int? page, int? pageSize) =>
        {
            var profession = http.User.FindFirstValue("prof") ?? http.User.FindFirstValue("profession");
            if (string.IsNullOrWhiteSpace(profession))
            {
                var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!string.IsNullOrWhiteSpace(userId))
                {
                    profession = await db.Users
                        .AsNoTracking()
                        .Where(user => user.Id == userId)
                        .Select(user => user.ActiveProfessionId)
                        .SingleOrDefaultAsync(ct);
                }
            }

            var q = db.ContentPapers.AsNoTracking()
                .Where(p => p.Status == ContentStatus.Published);

            // Profession scope: user sees papers that apply to all, or match their profession.
            if (!string.IsNullOrWhiteSpace(profession))
            {
                var prof = profession.Trim().ToLowerInvariant();
                q = q.Where(p => p.AppliesToAllProfessions || p.ProfessionId == prof);
            }
            else
            {
                q = q.Where(p => p.AppliesToAllProfessions);
            }

            if (!string.IsNullOrWhiteSpace(subtest))
                q = q.Where(p => p.SubtestCode == subtest.ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(cardType))
                q = q.Where(p => p.CardType == cardType.ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(letterType))
                q = q.Where(p => p.LetterType == letterType.ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLowerInvariant();
                q = q.Where(p => p.Title.ToLower().Contains(s));
            }

            var p2 = Math.Max(1, page ?? 1);
            var s2 = Math.Clamp(pageSize ?? 50, 1, 100);
            var rows = await q
                .OrderByDescending(p => p.Priority)
                .ThenBy(p => p.Title)
                .Skip((p2 - 1) * s2)
                .Take(s2)
                .Select(p => new
                {
                    p.Id, p.SubtestCode, p.Title, p.Slug,
                    p.ProfessionId, p.AppliesToAllProfessions,
                    p.Difficulty, p.EstimatedDurationMinutes,
                    p.CardType, p.LetterType, p.Priority, p.TagsCsv,
                    p.PublishedAt,
                })
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        // ── Detail — includes primary assets (with storage keys, never raw paths). ─
        group.MapGet("/{slugOrId}", async (
            string slugOrId,
            LearnerDbContext db,
            HttpContext http,
            IContentEntitlementService entitlements,
            MediaAssetAccessService mediaAccess,
            CancellationToken ct) =>
        {
            var paper = await db.ContentPapers.AsNoTracking()
                .Include(p => p.Assets.Where(a => a.IsPrimary))
                    .ThenInclude(a => a.MediaAsset)
                .FirstOrDefaultAsync(p =>
                    (p.Id == slugOrId || p.Slug == slugOrId)
                    && p.Status == ContentStatus.Published, ct);
            if (paper is null) return Results.NotFound();

            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var profession = http.User.FindFirstValue("prof") ?? http.User.FindFirstValue("profession");
            if (string.IsNullOrWhiteSpace(profession) && !string.IsNullOrWhiteSpace(userId))
            {
                profession = await db.Users
                    .AsNoTracking()
                    .Where(user => user.Id == userId)
                    .Select(user => user.ActiveProfessionId)
                    .SingleOrDefaultAsync(ct);
            }

            if (!paper.AppliesToAllProfessions
                && (string.IsNullOrWhiteSpace(profession)
                    || !string.Equals(paper.ProfessionId, profession, StringComparison.OrdinalIgnoreCase)))
            {
                // Paper exists but not visible to this profession.
                return Results.NotFound();
            }

            var isAdmin = entitlements.IsAdmin(http.User);
            if (!isAdmin)
            {
                await entitlements.RequireAccessAsync(userId ?? throw new InvalidOperationException("auth required"), paper, ct);
            }

            var visibleAssets = new List<ContentPaperAsset>();
            foreach (var asset in paper.Assets)
            {
                if (isAdmin)
                {
                    visibleAssets.Add(asset);
                    continue;
                }

                if (!IsLearnerVisiblePaperAssetRole(asset.Role))
                {
                    continue;
                }

                if (asset.MediaAsset is null || await mediaAccess.CanAccessAsync(http.User, asset.MediaAsset, ct))
                {
                    visibleAssets.Add(asset);
                }
            }

            return Results.Ok(new
            {
                paper.Id, paper.SubtestCode, paper.Title, paper.Slug,
                paper.ProfessionId, paper.AppliesToAllProfessions,
                paper.Difficulty, paper.EstimatedDurationMinutes,
                paper.CardType, paper.LetterType, paper.TagsCsv,
                paper.PublishedAt,
                assets = visibleAssets.Select(a => new
                {
                    a.Id, role = a.Role.ToString(), a.Part, a.Title,
                    media = a.MediaAsset is null ? null : new
                    {
                        a.MediaAsset.Id,
                        a.MediaAsset.OriginalFilename,
                        a.MediaAsset.MimeType,
                        a.MediaAsset.Format,
                        a.MediaAsset.SizeBytes,
                        a.MediaAsset.Sha256,
                        // Endpoint path the learner UI hits to stream the file
                        // (proxied via the existing MediaEndpoints pipeline).
                        downloadPath = $"/v1/media/{a.MediaAsset.Id}/content",
                    },
                }),
            });
        });

        return app;
    }

    private static bool IsLearnerVisiblePaperAssetRole(PaperAssetRole role)
        => role is PaperAssetRole.Audio
            or PaperAssetRole.QuestionPaper
            or PaperAssetRole.CaseNotes
            or PaperAssetRole.RoleCard
            or PaperAssetRole.WarmUpQuestions;
}
