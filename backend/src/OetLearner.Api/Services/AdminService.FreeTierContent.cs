using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public partial class AdminService
{
    private static readonly JsonSerializerOptions FreeTierContentJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<object> GetFreeTierContentAsync(CancellationToken ct)
    {
        var config = await GetOrCreateFreeTierConfigAsync(ct);
        var catalog = await db.SignupProfessionCatalog.AsNoTracking()
            .Where(row => row.IsActive)
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.Id)
            .ToListAsync(ct);
        var writing = await LoadEligibleFreeWritingScenariosAsync(ct);
        var speaking = await LoadEligibleFreeSpeakingCardsAsync(ct);
        return ProjectFreeTierContent(config, catalog, writing, speaking);
    }

    public async Task<object> UpdateFreeTierContentAsync(
        string adminId,
        string adminName,
        AdminFreeTierContentSelectionUpdateRequest request,
        CancellationToken ct)
    {
        var config = await GetOrCreateFreeTierConfigAsync(ct);
        var catalog = await db.SignupProfessionCatalog.AsNoTracking()
            .Where(row => row.IsActive)
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.Id)
            .ToListAsync(ct);
        var writing = await LoadEligibleFreeWritingScenariosAsync(ct);
        var speaking = await LoadEligibleFreeSpeakingCardsAsync(ct);

        var professions = catalog.Select(row => NormalizeProfession(row.Id))
            .Where(row => row is not null)
            .Select(row => row!)
            .ToHashSet(StringComparer.Ordinal);
        var selectedWriting = NormalizeSelectionMap(request.WritingScenarioByProfession);
        var selectedSpeaking = NormalizeSelectionMap(request.SpeakingCardByProfession);

        ValidateSelectionKeys(selectedWriting, professions, "writing");
        ValidateSelectionKeys(selectedSpeaking, professions, "speaking");
        ValidateSelections(selectedWriting, writing, "writing");
        ValidateSelections(selectedSpeaking, speaking, "speaking");

        config.FreeWritingScenarioByProfessionJson = JsonSerializer.Serialize(
            selectedWriting.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            FreeTierContentJsonOptions);
        config.FreeSpeakingCardByProfessionJson = JsonSerializer.Serialize(
            selectedSpeaking.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            FreeTierContentJsonOptions);
        config.UpdatedAt = DateTimeOffset.UtcNow;

        await SyncFreeSampleDesignationsAsync("writing", selectedWriting, adminId, ct);
        await SyncFreeSampleDesignationsAsync("speaking", selectedSpeaking, adminId, ct);

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(
            adminId,
            adminName,
            "Updated",
            "FreeTierFeaturedContent",
            config.Id,
            "Updated free Writing and Speaking selections by profession.",
            ct);

        return ProjectFreeTierContent(config, catalog, writing, speaking);
    }

    private async Task SyncFreeSampleDesignationsAsync(
        string subtest,
        IReadOnlyDictionary<string, string?> selections,
        string adminId,
        CancellationToken ct)
    {
        var desired = selections
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value!.Trim(), StringComparer.Ordinal);
        var rows = await db.FreeSampleDesignations
            .Where(row => row.Subtest == subtest)
            .ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var row in rows)
        {
            if (!desired.TryGetValue(row.Profession, out var contentId))
            {
                db.FreeSampleDesignations.Remove(row);
                continue;
            }

            row.ContentId = contentId;
            row.UpdatedByAdminId = adminId;
            row.UpdatedAt = now;
            desired.Remove(row.Profession);
        }

        foreach (var selection in desired)
        {
            db.FreeSampleDesignations.Add(new FreeSampleDesignation
            {
                Id = $"fsd-{Guid.NewGuid():N}",
                Subtest = subtest,
                Profession = selection.Key,
                ContentId = selection.Value,
                UpdatedByAdminId = adminId,
                UpdatedAt = now,
            });
        }
    }

    private async Task<FreeTierConfig> GetOrCreateFreeTierConfigAsync(CancellationToken ct)
    {
        var config = await db.FreeTierConfigs.FirstOrDefaultAsync(ct);
        if (config is not null) return config;

        config = new FreeTierConfig
        {
            Id = $"FTC-{Guid.NewGuid():N}"[..12],
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.FreeTierConfigs.Add(config);
        await db.SaveChangesAsync(ct);
        return config;
    }

    private async Task<List<WritingScenario>> LoadEligibleFreeWritingScenariosAsync(CancellationToken ct)
    {
        var scenarios = await db.WritingScenarios.AsNoTracking()
            .Where(row => row.Status == "published"
                && (!string.IsNullOrWhiteSpace(row.TaskPromptMarkdown)
                    || !string.IsNullOrWhiteSpace(row.StimulusPdfMediaAssetId)))
            .OrderBy(row => row.Difficulty)
            .ThenBy(row => row.Title)
            .ThenBy(row => row.Id)
            .ToListAsync(ct);
        var ids = scenarios.Select(row => row.Id).ToArray();
        var sentenceCounts = ids.Length == 0
            ? new Dictionary<Guid, int>()
            : await db.WritingScenarioStructuredSentences.AsNoTracking()
                .Where(row => ids.Contains(row.ScenarioId))
                .GroupBy(row => row.ScenarioId)
                .Select(group => new { ScenarioId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(row => row.ScenarioId, row => row.Count, ct);

        return scenarios
            .Where(row => sentenceCounts.GetValueOrDefault(row.Id) > 0)
            .ToList();
    }

    private async Task<List<RolePlayCard>> LoadEligibleFreeSpeakingCardsAsync(CancellationToken ct)
    {
        var cards = await db.RolePlayCards.AsNoTracking()
            .Include(card => card.ContentItem)
            .Where(card => card.Status == ContentStatus.Published
                && card.ContentItem != null
                && card.ContentItem.Status == ContentStatus.Published)
            .OrderBy(card => card.ProfessionId)
            .ThenBy(card => card.PublishedAt ?? card.UpdatedAt)
            .ThenBy(card => card.Id)
            .ToListAsync(ct);

        var cardIds = cards.Select(card => card.Id).ToArray();
        var unresolvedProjectionIds = await db.InterlocutorScripts.AsNoTracking()
            .Where(script => cardIds.Contains(script.RolePlayCardId)
                && script.ContentOrigin == "live_voice_projection"
                && script.NeedsOwnerInput)
            .Select(script => script.RolePlayCardId)
            .ToHashSetAsync(ct);
        return cards.Where(card => !unresolvedProjectionIds.Contains(card.Id)).ToList();
    }

    private static object ProjectFreeTierContent(
        FreeTierConfig config,
        IReadOnlyList<SignupProfessionCatalog> catalog,
        IReadOnlyList<WritingScenario> writing,
        IReadOnlyList<RolePlayCard> speaking)
    {
        var selectedWriting = ReadSelectionMap(config.FreeWritingScenarioByProfessionJson);
        var selectedSpeaking = ReadSelectionMap(config.FreeSpeakingCardByProfessionJson);
        var professions = catalog.Select(row => NormalizeProfession(row.Id))
            .Where(row => row is not null)
            .Select(row => row!)
            .ToHashSet(StringComparer.Ordinal);

        return new
        {
            configId = config.Id,
            updatedAt = config.UpdatedAt,
            professions = catalog.Select(row =>
            {
                var profession = NormalizeProfession(row.Id)!;
                var writingItems = writing
                    .Where(item => string.Equals(NormalizeProfession(item.Profession), profession, StringComparison.Ordinal))
                    .ToList();
                var speakingItems = speaking
                    .Where(item => string.Equals(NormalizeProfession(item.ProfessionId), profession, StringComparison.Ordinal)
                        || string.IsNullOrWhiteSpace(item.ContentItem?.ProfessionId))
                    .OrderBy(item => string.Equals(NormalizeProfession(item.ProfessionId), profession, StringComparison.Ordinal) ? 0 : 1)
                    .ThenBy(item => item.PublishedAt ?? item.UpdatedAt)
                    .ThenBy(item => item.Id)
                    .ToList();
                var selectedWritingId = selectedWriting.GetValueOrDefault(profession);
                var selectedSpeakingId = selectedSpeaking.GetValueOrDefault(profession);
                var selectedWritingGuid = Guid.TryParse(selectedWritingId, out var parsedWritingId)
                    ? parsedWritingId
                    : (Guid?)null;

                return new
                {
                    professionId = profession,
                    professionLabel = row.Label,
                    writingItems = writingItems.Select(item => new
                    {
                        id = item.Id,
                        title = item.Title,
                        letterType = item.LetterType,
                        difficulty = item.Difficulty
                    }).ToArray(),
                    selectedWritingScenarioId = selectedWritingId,
                    resolvedWritingScenarioId = writingItems.FirstOrDefault(item => item.Id == selectedWritingGuid)?.Id
                        ?? writingItems.FirstOrDefault()?.Id,
                    speakingItems = speakingItems.Select(item => new
                    {
                        id = item.Id,
                        title = item.ScenarioTitle,
                        category = item.PrimaryCategory
                    }).ToArray(),
                    selectedSpeakingCardId = selectedSpeakingId,
                    resolvedSpeakingCardId = speakingItems.FirstOrDefault(item => item.Id == selectedSpeakingId)?.Id
                        ?? speakingItems.FirstOrDefault()?.Id
                };
            }).ToArray(),
            ignoredProfessionSelections = new
            {
                writing = selectedWriting.Keys.Where(key => !professions.Contains(key)).ToArray(),
                speaking = selectedSpeaking.Keys.Where(key => !professions.Contains(key)).ToArray()
            }
        };
    }

    private static Dictionary<string, string?> NormalizeSelectionMap(
        IReadOnlyDictionary<string, string?>? input)
        => (input ?? new Dictionary<string, string?>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(
                pair => NormalizeProfession(pair.Key)!,
                pair => string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value!.Trim(),
                StringComparer.Ordinal);

    private static void ValidateSelectionKeys(
        IReadOnlyDictionary<string, string?> selections,
        IReadOnlySet<string> professions,
        string kind)
    {
        var unknown = selections.Keys.Where(key => !professions.Contains(key)).ToArray();
        if (unknown.Length > 0)
        {
            throw ApiException.Validation(
                "free_tier_content_invalid",
                $"Unknown {kind} profession selection: {string.Join(", ", unknown)}.");
        }
    }

    private static void ValidateSelections<T>(
        IReadOnlyDictionary<string, string?> selections,
        IReadOnlyList<T> items,
        string kind)
    {
        foreach (var pair in selections.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)))
        {
            var valid = kind == "writing"
                ? items.OfType<WritingScenario>().Any(item => string.Equals(item.Profession?.Trim(), pair.Key, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.Id.ToString(), pair.Value, StringComparison.OrdinalIgnoreCase))
                : items.OfType<RolePlayCard>().Any(item =>
                    (string.Equals(item.ProfessionId?.Trim(), pair.Key, StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrWhiteSpace(item.ContentItem?.ProfessionId))
                    && string.Equals(item.Id, pair.Value, StringComparison.Ordinal));
            if (!valid)
            {
                throw ApiException.Validation(
                    "free_tier_content_invalid",
                    $"The selected {kind} item for profession '{pair.Key}' is not published, eligible, or owned by that profession.");
            }
        }
    }

    private static Dictionary<string, string?> ReadSelectionMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string?>(StringComparer.Ordinal);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json, FreeTierContentJsonOptions)
                ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }
    }

    private static string? NormalizeProfession(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
