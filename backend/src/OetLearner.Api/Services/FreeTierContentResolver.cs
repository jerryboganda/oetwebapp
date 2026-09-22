using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Resolves the one free Writing scenario and one free Speaking card for a
/// profession. Selection is admin-owned, but stale selections never strand a
/// learner: the first eligible published item is the deterministic fallback.
/// </summary>
public interface IFreeTierContentResolver
{
    Task<WritingScenario?> ResolveWritingScenarioAsync(string? professionId, CancellationToken ct);
    Task<RolePlayCard?> ResolveSpeakingCardAsync(string? professionId, CancellationToken ct);
    Task<bool> IsFeaturedSpeakingCardAsync(string? professionId, string cardId, CancellationToken ct);
}

public sealed class FreeTierContentResolver(LearnerDbContext db) : IFreeTierContentResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WritingScenario?> ResolveWritingScenarioAsync(string? professionId, CancellationToken ct)
    {
        var profession = NormalizeProfession(professionId);
        if (profession is null) return null;

        var config = await db.FreeTierConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        var selectedId = ReadGuidSelection(config?.FreeWritingScenarioByProfessionJson, profession);
        var candidates = await db.WritingScenarios.AsNoTracking()
            .Where(s => s.Status == "published" && s.Profession == profession)
            .OrderBy(s => s.Difficulty)
            .ThenBy(s => s.Title)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);
        if (candidates.Count == 0) return null;

        var candidateIds = candidates.Select(candidate => candidate.Id).ToArray();
        var sentenceCounts = await db.WritingScenarioStructuredSentences.AsNoTracking()
            .Where(s => candidateIds.Contains(s.ScenarioId))
            .GroupBy(s => s.ScenarioId)
            .Select(group => new { ScenarioId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.ScenarioId, row => row.Count, ct);

        var eligible = candidates
            .Where(s => (!string.IsNullOrWhiteSpace(s.TaskPromptMarkdown) || !string.IsNullOrWhiteSpace(s.StimulusPdfMediaAssetId))
                && sentenceCounts.GetValueOrDefault(s.Id) > 0)
            .ToList();
        if (eligible.Count == 0) return null;

        if (selectedId is Guid requested
            && eligible.FirstOrDefault(item => item.Id == requested) is { } selected)
        {
            return selected;
        }

        return eligible[0];
    }

    public async Task<RolePlayCard?> ResolveSpeakingCardAsync(string? professionId, CancellationToken ct)
    {
        var profession = NormalizeProfession(professionId);
        if (profession is null) return null;

        var config = await db.FreeTierConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        var selectedId = ReadStringSelection(config?.FreeSpeakingCardByProfessionJson, profession);
        var cards = await db.RolePlayCards.AsNoTracking()
            .Include(card => card.ContentItem)
            .Where(card => card.Status == ContentStatus.Published
                && card.ContentItem != null
                && card.ContentItem.Status == ContentStatus.Published
                && (card.ProfessionId == profession
                    || string.IsNullOrWhiteSpace(card.ContentItem.ProfessionId)))
            .OrderBy(card => card.ProfessionId == profession ? 0 : 1)
            .ThenBy(card => card.PublishedAt ?? card.UpdatedAt)
            .ThenBy(card => card.Id)
            .ToListAsync(ct);
        if (cards.Count == 0) return null;

        var cardIds = cards.Select(card => card.Id).ToArray();
        var unresolvedProjectionIds = await db.InterlocutorScripts.AsNoTracking()
            .Where(script => cardIds.Contains(script.RolePlayCardId)
                && script.ContentOrigin == "live_voice_projection"
                && script.NeedsOwnerInput)
            .Select(script => script.RolePlayCardId)
            .ToHashSetAsync(ct);
        cards = cards
            .Where(card => !unresolvedProjectionIds.Contains(card.Id))
            .ToList();
        if (cards.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(selectedId)
            && cards.FirstOrDefault(item => string.Equals(item.Id, selectedId, StringComparison.Ordinal)) is { } selected)
        {
            return selected;
        }

        return cards[0];
    }

    public async Task<bool> IsFeaturedSpeakingCardAsync(string? professionId, string cardId, CancellationToken ct)
    {
        var featured = await ResolveSpeakingCardAsync(professionId, ct);
        return featured is not null && string.Equals(featured.Id, cardId, StringComparison.Ordinal);
    }

    private static string? NormalizeProfession(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static Guid? ReadGuidSelection(string? json, string profession)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string?>>(json, JsonOptions);
            if (map is null || !map.TryGetValue(profession, out var value) || !Guid.TryParse(value, out var id))
                return null;
            return id;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadStringSelection(string? json, string profession)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string?>>(json, JsonOptions);
            return map is not null && map.TryGetValue(profession, out var value)
                ? value?.Trim()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
