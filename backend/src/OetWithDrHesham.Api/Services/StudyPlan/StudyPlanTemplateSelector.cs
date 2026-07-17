using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Planner;

public sealed record TemplateSelection(StudyPlanTemplate Template, StudyPlanTemplateBody Body);

/// <summary>
/// Picks the best matching active template for a learner given total weeks,
/// tier, weak skills, target band, and profession. Falls back to any free-tier
/// active template if no scoped match exists, so the generator never returns
/// without a template to materialise.
/// </summary>
public class StudyPlanTemplateSelector(LearnerDbContext db)
{
    public async Task<TemplateSelection?> SelectAsync(
        int totalWeeks,
        string tier,
        IReadOnlyCollection<string> weakSubtests,
        string? targetBand,
        string? professionId,
        CancellationToken cancellationToken)
    {
        var allowedTemplateIds = await db.StudyPlanTemplateTiers
            .AsNoTracking()
            .Where(t => t.TierCode == tier)
            .Select(t => t.TemplateId)
            .ToListAsync(cancellationToken);

        var ids = allowedTemplateIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Round totalWeeks into [MinWeeks, MaxWeeks] window; templates that
        // span the learner's runway qualify.
        var candidates = await db.StudyPlanTemplates
            .AsNoTracking()
            .Where(t => t.IsActive)
            .Where(t => t.MinWeeks <= totalWeeks && t.MaxWeeks >= totalWeeks)
            .ToListAsync(cancellationToken);

        var inTier = candidates.Where(c => ids.Contains(c.Id)).ToList();
        if (inTier.Count == 0)
        {
            // Tier fallback: any free-tier template still qualifies (so paid
            // tiers always have a baseline before their templates are seeded).
            var freeIds = await db.StudyPlanTemplateTiers
                .AsNoTracking()
                .Where(t => t.TierCode == StudyPlanEntitlementResolver.FreeTier)
                .Select(t => t.TemplateId)
                .ToListAsync(cancellationToken);

            var freeIdSet = freeIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            inTier = candidates.Where(c => freeIdSet.Contains(c.Id)).ToList();
        }

        if (inTier.Count == 0)
        {
            // Total fallback: pick any active template in the week range.
            inTier = candidates;
        }

        if (inTier.Count == 0)
        {
            return null;
        }

        // Score: profession match (+3), band match (+2), focus tag matching weak (+1 each).
        StudyPlanTemplate? best = null;
        var bestScore = int.MinValue;
        foreach (var c in inTier)
        {
            var score = 0;
            if (!string.IsNullOrWhiteSpace(c.ProfessionId)
                && string.Equals(c.ProfessionId, professionId, StringComparison.OrdinalIgnoreCase))
            {
                score += 3;
            }

            if (!string.IsNullOrWhiteSpace(c.TargetBand)
                && string.Equals(c.TargetBand, targetBand, StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
            }

            var focusTags = ParseTags(c.FocusTagsJson);
            score += focusTags.Count(tag => weakSubtests.Any(w =>
                tag.Contains(w, StringComparison.OrdinalIgnoreCase)));

            // Prefer narrower week windows (tighter match).
            score -= (c.MaxWeeks - c.MinWeeks);

            if (score > bestScore || (score == bestScore && best is { } b && c.UpdatedAt > b.UpdatedAt))
            {
                best = c;
                bestScore = score;
            }
        }

        if (best is null) return null;

        var body = ParseBody(best.TemplateBodyJson);
        return new TemplateSelection(best, body);
    }

    private static IReadOnlyList<string> ParseTags(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static StudyPlanTemplateBody ParseBody(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new StudyPlanTemplateBody();
        try
        {
            return JsonSerializer.Deserialize<StudyPlanTemplateBody>(json) ?? new StudyPlanTemplateBody();
        }
        catch
        {
            return new StudyPlanTemplateBody();
        }
    }
}
