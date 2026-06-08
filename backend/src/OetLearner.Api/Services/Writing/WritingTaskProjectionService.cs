using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// WS-B2 bridge: projects a published writing <see cref="ContentPaper"/> into the
/// authoritative <see cref="WritingScenario"/>. The upsert is keyed on
/// <see cref="WritingScenario.SourceContentPaperId"/> and is safe to call
/// repeatedly (idempotent): the scenario is updated in place.
/// </summary>
public interface IWritingTaskProjectionService
{
    Task<WritingScenario> ProjectFromContentPaperAsync(ContentPaper paper, CancellationToken ct = default);
}

public sealed class WritingTaskProjectionService(LearnerDbContext db, ILogger<WritingTaskProjectionService> logger)
    : IWritingTaskProjectionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WritingScenario> ProjectFromContentPaperAsync(ContentPaper paper, CancellationToken ct = default)
    {
        // WritingContentStructure exposes a dictionary view over the authored
        // writingStructure blob (ContentPaper.ExtractedTextJson["writingStructure"]).
        var structure = WritingContentStructure.ExtractStructure(paper.ExtractedTextJson);
        var now = DateTimeOffset.UtcNow;

        var scenario = await db.WritingScenarios
            .FirstOrDefaultAsync(s => s.SourceContentPaperId == paper.Id, ct);

        var isNew = scenario is null;
        if (scenario is null)
        {
            scenario = new WritingScenario
            {
                Id = Guid.NewGuid(),
                SourceContentPaperId = paper.Id,
                AuthorId = paper.CreatedByAdminId ?? "system",
                Version = 1,
                CreatedAt = now,
            };
            db.WritingScenarios.Add(scenario);
        }

        scenario.Title = paper.Title;
        scenario.Profession = (paper.ProfessionId ?? scenario.Profession ?? string.Empty).Trim();
        scenario.LetterType = (paper.LetterType ?? scenario.LetterType ?? string.Empty).Trim();
        scenario.InternalCode = ReadString(structure, "internalCode", "taskCode") ?? scenario.InternalCode;
        scenario.TaskPromptMarkdown = ReadString(structure, "taskPrompt", "task", "brief", "scenario");
        scenario.WriterRole = ReadString(structure, "writerRole", "candidateRole");
        scenario.TodayDate = ReadString(structure, "todayDate", "taskDate", "date");
        scenario.ExpectedPurpose = ReadString(structure, "expectedPurpose", "purpose");
        scenario.ExpectedAction = ReadString(structure, "expectedAction", "action", "request");
        scenario.FixedInstructionsJson = SerializeFixedInstructions(structure);

        var (wordMin, wordMax) = ReadWordGuide(structure);
        if (wordMin is { } min and > 0) scenario.WordGuideMin = min;
        if (wordMax is { } max and > 0) scenario.WordGuideMax = max;

        var simulationModes = ReadString(structure, "simulationModes", "simulationMode");
        if (!string.IsNullOrWhiteSpace(simulationModes)) scenario.SimulationModes = simulationModes.Trim();

        var markingMode = ReadString(structure, "markingMode");
        if (!string.IsNullOrWhiteSpace(markingMode)) scenario.MarkingMode = markingMode.Trim();

        scenario.SourceProvenance = paper.SourceProvenance ?? scenario.SourceProvenance;
        scenario.IntegrityAcknowledgedById = paper.IntegrityAcknowledgedByAdminId ?? scenario.IntegrityAcknowledgedById;
        scenario.IntegrityAcknowledgedAt = paper.IntegrityAcknowledgedAt ?? scenario.IntegrityAcknowledgedAt;
        scenario.ContentOwnerId ??= paper.CreatedByAdminId;
        scenario.Status = "published";
        scenario.PublishedAt ??= now;
        scenario.UpdatedAt = now;

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Projected writing ContentPaper {PaperId} into scenario {ScenarioId} ({Action})",
            paper.Id,
            scenario.Id,
            isNew ? "created" : "updated");

        return scenario;
    }

    // ----- serialization helpers (camelCase, mirrors the authoring service columns) -----

    private static string SerializeFixedInstructions(IReadOnlyDictionary<string, object?> structure)
    {
        var instructions = ReadStringList(structure, "fixedInstructions");
        return JsonSerializer.Serialize(instructions, JsonOptions);
    }

    private static (int? Min, int? Max) ReadWordGuide(IReadOnlyDictionary<string, object?> structure)
    {
        if (ReadValue(structure, "wordGuide") is { } guideValue
            && ToDictionary(guideValue) is { Count: > 0 } guide)
        {
            return (ReadInt(guide, "min"), ReadInt(guide, "max"));
        }

        return (ReadInt(structure, "wordGuideMin"), ReadInt(structure, "wordGuideMax"));
    }

    // ----- dictionary readers (the structure is a JSON-derived object graph) -----

    private static object? ReadValue(IReadOnlyDictionary<string, object?> source, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (source.TryGetValue(key, out var value) && value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> source, params string[] keys)
    {
        var value = ReadValue(source, keys);
        return value switch
        {
            null => null,
            string s => string.IsNullOrWhiteSpace(s) ? null : s,
            JsonElement { ValueKind: JsonValueKind.String } el => el.GetString(),
            JsonElement { ValueKind: JsonValueKind.Null } => null,
            JsonElement el => el.ToString(),
            _ => value.ToString(),
        };
    }

    private static int? ReadInt(IReadOnlyDictionary<string, object?> source, params string[] keys)
    {
        var value = ReadValue(source, keys);
        return value switch
        {
            null => null,
            int i => i,
            long l => (int)l,
            double d => (int)d,
            JsonElement { ValueKind: JsonValueKind.Number } el when el.TryGetInt32(out var n) => n,
            JsonElement { ValueKind: JsonValueKind.String } el when int.TryParse(el.GetString(), out var n) => n,
            string s when int.TryParse(s, out var n) => n,
            _ => null,
        };
    }

    private static List<string> ReadStringList(IReadOnlyDictionary<string, object?> source, params string[] keys)
    {
        var value = ReadValue(source, keys);
        if (value is null)
        {
            return new List<string>();
        }

        if (value is JsonElement { ValueKind: JsonValueKind.Array } array)
        {
            return array.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim())
                .ToList();
        }

        if (value is IEnumerable<object?> objects)
        {
            return objects
                .Select(o => o?.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim())
                .ToList();
        }

        return new List<string>();
    }

    private static Dictionary<string, object?> ToDictionary(object? value)
    {
        switch (value)
        {
            case null:
                return new Dictionary<string, object?>();
            case IDictionary<string, object?> dict:
                return new Dictionary<string, object?>(dict);
            case JsonElement { ValueKind: JsonValueKind.Object } element:
            {
                var result = new Dictionary<string, object?>();
                foreach (var prop in element.EnumerateObject())
                {
                    result[prop.Name] = prop.Value;
                }

                return result;
            }
            default:
                return new Dictionary<string, object?>();
        }
    }
}
