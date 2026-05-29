using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OetLearner.Api.Services.Reading;

/// <summary>
/// Generates deterministic stable option IDs for Reading MCQ options.
/// Used by the backfill migration and by authoring flows that write new questions.
/// ID shape: "opt-{first 12 hex chars of SHA256(questionId + ':' + displayOrder)}".
/// </summary>
public static class ReadingOptionIdHelper
{
    private const string Prefix = "opt-";

    /// <summary>
    /// Generates a stable option ID from the question ID and the 0-based option index.
    /// </summary>
    public static string GenerateOptionId(string questionId, int optionIndex)
    {
        var input = $"{questionId}:{optionIndex}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Prefix + Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    /// <summary>
    /// Takes the existing OptionsJson (plain string array or object array without IDs)
    /// and returns a new JSON string with stable IDs and letter keys injected.
    /// Returns null if the input is not a valid MCQ options array.
    /// </summary>
    public static string? EnrichOptionsWithIds(string questionId, string optionsJson)
    {
        JsonArray? options;
        try
        {
            var node = JsonNode.Parse(optionsJson);
            options = node?.AsArray();
        }
        catch
        {
            return null;
        }

        if (options is null || options.Count == 0)
            return null;

        var letters = new[] { "A", "B", "C", "D", "E", "F" };
        var result = new JsonArray();

        for (int i = 0; i < options.Count; i++)
        {
            var item = options[i];
            var id = GenerateOptionId(questionId, i);
            var letter = i < letters.Length ? letters[i] : $"{i + 1}";

            if (item is JsonValue val && val.GetValueKind() == JsonValueKind.String)
            {
                // Plain string option → convert to object with id, text, letter
                result.Add(new JsonObject
                {
                    ["id"] = id,
                    ["text"] = val.GetValue<string>(),
                    ["letter"] = letter,
                });
            }
            else if (item is JsonObject obj)
            {
                // Already an object — add id and letter if missing
                if (!obj.ContainsKey("id"))
                    obj["id"] = id;
                if (!obj.ContainsKey("letter"))
                    obj["letter"] = letter;
                result.Add(obj.DeepClone());
            }
            else
            {
                // Unexpected shape — skip this row
                return null;
            }
        }

        return result.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}
