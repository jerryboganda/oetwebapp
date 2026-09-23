using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Services.FreeSamples;

namespace OetLearner.Api.Services;

/// <summary>
/// The one own-profession check for learner Writing/Speaking surfaces (owner
/// lock, 23 Sep 2026): a learner only ever sees, starts, resumes or submits
/// content of their own registered profession. Professions are compared
/// normalised (<see cref="FreeSampleService.NormalizeProfession"/>), and a
/// learner with no profession set fails closed — they must pick one first.
/// Callers run it BEFORE loading any card / case-note payload and it throws
/// 404 so a probe never learns the content exists for another profession.
/// </summary>
public static class LearnerProfessionGuard
{
    public static Task<string?> GetLearnerProfessionAsync(LearnerDbContext db, string userId, CancellationToken ct)
        => db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.ActiveProfessionId)
            .FirstOrDefaultAsync(ct);

    public static bool Matches(string? learnerProfession, string? contentProfession)
        => !string.IsNullOrWhiteSpace(learnerProfession)
           && !string.IsNullOrWhiteSpace(contentProfession)
           && FreeSampleService.NormalizeProfession(learnerProfession) == FreeSampleService.NormalizeProfession(contentProfession);

    /// <summary>A role-play card whose linked ContentItem exists and carries no
    /// profession is universal. An orphan card (no ContentItem) is never
    /// universal — only its own ProfessionId can admit it.</summary>
    public static bool IsUniversalRolePlayCard(bool hasContentItem, string? contentItemProfessionId)
        => hasContentItem && string.IsNullOrWhiteSpace(contentItemProfessionId);

    public static bool CanAccessRolePlayCard(
        string? learnerProfession,
        string? cardProfession,
        bool hasContentItem,
        string? contentItemProfessionId)
        => !string.IsNullOrWhiteSpace(learnerProfession)
           && (Matches(learnerProfession, cardProfession)
               || IsUniversalRolePlayCard(hasContentItem, contentItemProfessionId));

    /// <summary>Role-play card variant of <see cref="RequireAsync"/> (card
    /// profession, or a universal ContentItem). A missing card also 404s.</summary>
    public static async Task RequireRolePlayCardAsync(
        LearnerDbContext db,
        string userId,
        string? cardId,
        string errorCode,
        string message,
        CancellationToken ct)
    {
        var row = await (
            from card in db.RolePlayCards.AsNoTracking()
            join item in db.ContentItems.AsNoTracking()
                on card.ContentItemId equals item.Id into joined
            from item in joined.DefaultIfEmpty()
            where card.Id == cardId
            select new
            {
                card.ProfessionId,
                HasContentItem = item != null,
                ContentItemProfessionId = item != null ? item.ProfessionId : null,
            })
            .FirstOrDefaultAsync(ct);
        if (row is null
            || !CanAccessRolePlayCard(
                await GetLearnerProfessionAsync(db, userId, ct),
                row.ProfessionId,
                row.HasContentItem,
                row.ContentItemProfessionId))
        {
            throw ApiException.NotFound(errorCode, message);
        }
    }

    public static async Task RequireAsync(
        LearnerDbContext db,
        string userId,
        string? contentProfession,
        string errorCode,
        string message,
        CancellationToken ct)
    {
        if (!Matches(await GetLearnerProfessionAsync(db, userId, ct), contentProfession))
        {
            throw ApiException.NotFound(errorCode, message);
        }
    }
}
