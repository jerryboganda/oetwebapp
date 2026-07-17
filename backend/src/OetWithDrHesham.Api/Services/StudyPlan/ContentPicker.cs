using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Planner;

public sealed record ResolvedSlotContent(
    string? ContentId,
    string Title,
    string Route,
    int DurationMinutes);

/// <summary>
/// Resolves a template slot into concrete content for a learner.
/// Queries the ContentItem catalog for unattempted, profession-matched material.
/// Falls back gracefully when no candidate exists (returns a generic prompt
/// rather than failing the whole plan).
/// </summary>
public class ContentPicker(LearnerDbContext db)
{
    public async Task<ResolvedSlotContent> ResolveAsync(
        StudyPlanTemplateSlot slot,
        string userId,
        string? professionId,
        int weekIndex,
        ISet<string> alreadyPicked,
        CancellationToken cancellationToken)
    {
        return slot.Kind switch
        {
            StudyPlanSlotKinds.CustomContent
                => await ResolveCustomContentAsync(slot, alreadyPicked, cancellationToken),
            StudyPlanSlotKinds.NextUnattemptedPaper
                => await ResolveNextUnattemptedAsync(slot, userId, professionId, alreadyPicked, cancellationToken),
            StudyPlanSlotKinds.DrillByTag
                => await ResolveDrillByTagAsync(slot, userId, professionId, alreadyPicked, cancellationToken),
            StudyPlanSlotKinds.WeakSkillFocus
                => await ResolveWeakSkillFocusAsync(slot, userId, professionId, alreadyPicked, cancellationToken),
            StudyPlanSlotKinds.FullMock or StudyPlanSlotKinds.MiniMock
                => Fallback(slot, $"{(slot.Kind == StudyPlanSlotKinds.FullMock ? "Full" : "Mini")} mock — {slot.Subtest}", RouteForSubtest(slot.Subtest, null, isMock: true)),
            StudyPlanSlotKinds.ExpertReviewSubmission
                => Fallback(slot, $"Submit a {slot.Subtest} attempt for expert review", $"/{slot.Subtest.ToLowerInvariant()}/expert-request"),
            StudyPlanSlotKinds.SpacedRepReview
                => Fallback(slot, "Spaced-repetition review", "/recalls"),
            StudyPlanSlotKinds.VocabularyFlashcards
                => Fallback(slot, "Vocabulary flashcards", "/vocabulary/flashcards"),
            StudyPlanSlotKinds.PronunciationDrill
                => Fallback(slot, "Pronunciation drill", "/speaking/pronunciation"),
            _ => Fallback(slot, $"{slot.Subtest} task", RouteForSubtest(slot.Subtest, null, isMock: false))
        };
    }

    public async Task<IReadOnlyList<ResolvedSlotContent>> ResolveAlternativesAsync(
        StudyPlanItem original,
        string? professionId,
        int count,
        CancellationToken cancellationToken)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(original.SourceContentId)) taken.Add(original.SourceContentId);
        if (!string.IsNullOrWhiteSpace(original.ContentId)) taken.Add(original.ContentId);

        var candidates = await BaseQuery(original.SubtestCode, professionId)
            .Where(c => !taken.Contains(c.Id))
            .Where(c => c.EstimatedDurationMinutes >= Math.Max(5, original.DurationMinutes - 10)
                     && c.EstimatedDurationMinutes <= original.DurationMinutes + 15)
            .OrderBy(c => c.DifficultyRating)
            .Take(count)
            .Select(c => new { c.Id, c.Title, c.EstimatedDurationMinutes, c.SubtestCode })
            .ToListAsync(cancellationToken);

        return candidates
            .Select(c => new ResolvedSlotContent(
                c.Id,
                c.Title,
                RouteForSubtest(c.SubtestCode, c.Id, isMock: false),
                c.EstimatedDurationMinutes <= 0 ? original.DurationMinutes : c.EstimatedDurationMinutes))
            .ToList();
    }

    private async Task<ResolvedSlotContent> ResolveCustomContentAsync(
        StudyPlanTemplateSlot slot,
        ISet<string> alreadyPicked,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slot.ContentId))
        {
            return Fallback(slot, "Coach-recommended task", RouteForSubtest(slot.Subtest, null, isMock: false));
        }

        var item = await db.ContentItems
            .AsNoTracking()
            .Where(c => c.Id == slot.ContentId)
            .Select(c => new { c.Id, c.Title, c.SubtestCode, c.EstimatedDurationMinutes })
            .FirstOrDefaultAsync(cancellationToken);

        if (item is null)
        {
            return Fallback(slot, "Coach-recommended task", RouteForSubtest(slot.Subtest, null, isMock: false));
        }

        alreadyPicked.Add(item.Id);
        return new ResolvedSlotContent(
            item.Id,
            item.Title,
            RouteForSubtest(item.SubtestCode, item.Id, isMock: false),
            item.EstimatedDurationMinutes <= 0 ? slot.Minutes : item.EstimatedDurationMinutes);
    }

    private async Task<ResolvedSlotContent> ResolveNextUnattemptedAsync(
        StudyPlanTemplateSlot slot,
        string userId,
        string? professionId,
        ISet<string> alreadyPicked,
        CancellationToken cancellationToken)
    {
        var attemptedIds = await db.Attempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.SubtestCode == slot.Subtest)
            .Select(a => a.ContentId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var skip = new HashSet<string>(attemptedIds, StringComparer.OrdinalIgnoreCase);
        foreach (var id in alreadyPicked) skip.Add(id);

        var pick = await BaseQuery(slot.Subtest, professionId)
            .Where(c => !skip.Contains(c.Id))
            .OrderBy(c => c.DifficultyRating)
            .ThenBy(c => c.CreatedAt)
            .Select(c => new { c.Id, c.Title, c.SubtestCode, c.EstimatedDurationMinutes })
            .FirstOrDefaultAsync(cancellationToken);

        if (pick is null)
        {
            return Fallback(slot, $"Next {slot.Subtest} paper", RouteForSubtest(slot.Subtest, null, isMock: false));
        }

        alreadyPicked.Add(pick.Id);
        return new ResolvedSlotContent(
            pick.Id,
            pick.Title,
            RouteForSubtest(pick.SubtestCode, pick.Id, isMock: false),
            pick.EstimatedDurationMinutes <= 0 ? slot.Minutes : pick.EstimatedDurationMinutes);
    }

    private async Task<ResolvedSlotContent> ResolveDrillByTagAsync(
        StudyPlanTemplateSlot slot,
        string userId,
        string? professionId,
        ISet<string> alreadyPicked,
        CancellationToken cancellationToken)
    {
        // No first-class tag column on ContentItem yet; match against ScenarioType
        // and Difficulty as a stand-in for tag-based filtering.
        var query = BaseQuery(slot.Subtest, professionId);
        if (slot.Tags is { Count: > 0 })
        {
            var tagSet = slot.Tags.Select(t => t.ToLowerInvariant()).ToList();
            query = query.Where(c =>
                c.ScenarioType != null && tagSet.Contains(c.ScenarioType.ToLower()));
        }

        var attemptedIds = await db.Attempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.SubtestCode == slot.Subtest)
            .Select(a => a.ContentId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var skip = new HashSet<string>(attemptedIds, StringComparer.OrdinalIgnoreCase);
        foreach (var id in alreadyPicked) skip.Add(id);

        var pick = await query
            .Where(c => !skip.Contains(c.Id))
            .OrderBy(c => c.DifficultyRating)
            .Select(c => new { c.Id, c.Title, c.SubtestCode, c.EstimatedDurationMinutes })
            .FirstOrDefaultAsync(cancellationToken);

        if (pick is null)
        {
            return Fallback(slot, $"{slot.Subtest} drill", RouteForSubtest(slot.Subtest, null, isMock: false));
        }

        alreadyPicked.Add(pick.Id);
        return new ResolvedSlotContent(
            pick.Id,
            pick.Title,
            RouteForSubtest(pick.SubtestCode, pick.Id, isMock: false),
            pick.EstimatedDurationMinutes <= 0 ? slot.Minutes : pick.EstimatedDurationMinutes);
    }

    private async Task<ResolvedSlotContent> ResolveWeakSkillFocusAsync(
        StudyPlanTemplateSlot slot,
        string userId,
        string? professionId,
        ISet<string> alreadyPicked,
        CancellationToken cancellationToken)
    {
        // Treat weak-skill-focus like next-unattempted but bias toward easier
        // material so the learner can rebuild confidence.
        var attemptedIds = await db.Attempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.SubtestCode == slot.Subtest)
            .Select(a => a.ContentId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var skip = new HashSet<string>(attemptedIds, StringComparer.OrdinalIgnoreCase);
        foreach (var id in alreadyPicked) skip.Add(id);

        var pick = await BaseQuery(slot.Subtest, professionId)
            .Where(c => !skip.Contains(c.Id))
            .OrderBy(c => c.DifficultyRating)
            .Select(c => new { c.Id, c.Title, c.SubtestCode, c.EstimatedDurationMinutes })
            .FirstOrDefaultAsync(cancellationToken);

        if (pick is null)
        {
            return Fallback(slot, $"{slot.Subtest} focus practice", RouteForSubtest(slot.Subtest, null, isMock: false));
        }

        alreadyPicked.Add(pick.Id);
        return new ResolvedSlotContent(
            pick.Id,
            pick.Title,
            RouteForSubtest(pick.SubtestCode, pick.Id, isMock: false),
            pick.EstimatedDurationMinutes <= 0 ? slot.Minutes : pick.EstimatedDurationMinutes);
    }

    private IQueryable<ContentItem> BaseQuery(string subtest, string? professionId)
    {
        var q = db.ContentItems
            .AsNoTracking()
            .Where(c => c.SubtestCode == subtest)
            .Where(c => c.Status == ContentStatus.Published);

        if (!string.IsNullOrWhiteSpace(professionId))
        {
            q = q.Where(c => c.ProfessionId == null || c.ProfessionId == professionId);
        }

        return q;
    }

    private static ResolvedSlotContent Fallback(StudyPlanTemplateSlot slot, string title, string route)
        => new(null, title, route, slot.Minutes);

    private static string RouteForSubtest(string subtest, string? contentId, bool isMock)
    {
        var lower = (subtest ?? string.Empty).ToLowerInvariant();
        if (isMock)
        {
            return lower switch
            {
                "reading" => "/reading/mocks",
                "listening" => "/listening/mocks",
                "speaking" => "/speaking/mocks",
                "writing" => "/writing/mocks",
                _ => "/mocks"
            };
        }

        if (string.IsNullOrWhiteSpace(contentId))
        {
            return $"/{lower}";
        }

        return lower switch
        {
            "reading" => $"/reading/paper/{Uri.EscapeDataString(contentId)}",
            "listening" => $"/listening/player/{Uri.EscapeDataString(contentId)}",
            "writing" => "/writing/practice/library",
            "speaking" => $"/speaking/task/{Uri.EscapeDataString(contentId)}",
            "vocabulary" => "/vocabulary",
            _ => $"/{lower}"
        };
    }
}
