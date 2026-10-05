using System.Text.Json;

namespace OetLearner.Api.Services.RemoteJobs;

public enum ExtractedTextMergeStatus
{
    /// <summary>The key was added or replaced; <see cref="ExtractedTextMergeResult.Json"/> holds the new payload.</summary>
    Written,

    /// <summary>The key is already cached (and replacement was not asked for, or the text is identical): nothing to write.</summary>
    AlreadyCached,

    /// <summary>The stored payload is not a JSON object: never overwritten blindly.</summary>
    Unreadable,
}

public readonly record struct ExtractedTextMergeResult(ExtractedTextMergeStatus Status, string? Json);

/// <summary>
/// Merges ONE asset key into <c>ContentPaper.ExtractedTextJson</c> (OET-RWP/1 section 6.1.5). The column also holds
/// authored structures (<c>listeningQuestions</c>, <c>writingStructure</c>, <c>speakingStructure</c> ...) that must never
/// be rewritten, so every other member is carried across untouched. Serialisation matches
/// <c>ContentTextExtractionService</c> (a <c>Dictionary&lt;string, JsonElement&gt;</c>), so existing readers see the same shape.
/// </summary>
public static class ExtractedTextMerger
{
    /// <summary>
    /// The asset keys already cached in a payload (an empty set for a blank column), or null when the column is not a
    /// JSON object. Authored members such as <c>listeningQuestions</c> are returned too; callers only test asset ids.
    /// </summary>
    public static HashSet<string>? ReadKeys(string? existingJson)
    {
        if (string.IsNullOrWhiteSpace(existingJson)) return new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(existingJson);
            if (document.RootElement.ValueKind == JsonValueKind.Null) return new HashSet<string>(StringComparer.Ordinal);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            return document.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static ExtractedTextMergeResult Merge(string? existingJson, string assetKey, string text, bool replaceExisting)
    {
        Dictionary<string, JsonElement> existing;
        if (string.IsNullOrWhiteSpace(existingJson))
        {
            existing = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
        else
        {
            try
            {
                existing = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(existingJson)
                           ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            }
            catch (JsonException)
            {
                return new ExtractedTextMergeResult(ExtractedTextMergeStatus.Unreadable, null);
            }
        }

        if (existing.TryGetValue(assetKey, out var current))
        {
            if (!replaceExisting) return new ExtractedTextMergeResult(ExtractedTextMergeStatus.AlreadyCached, null);

            if (current.ValueKind == JsonValueKind.String && string.Equals(current.GetString(), text, StringComparison.Ordinal))
            {
                return new ExtractedTextMergeResult(ExtractedTextMergeStatus.AlreadyCached, null);
            }
        }

        existing[assetKey] = JsonSerializer.SerializeToElement(text);
        return new ExtractedTextMergeResult(ExtractedTextMergeStatus.Written, JsonSerializer.Serialize(existing));
    }
}
