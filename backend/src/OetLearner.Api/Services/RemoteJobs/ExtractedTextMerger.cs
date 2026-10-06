using System.Text.Json;
using OetLearner.Api.Services.Content;

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
    /// The asset keys that need no automatic extraction in a payload (an empty set for a blank column), or null when the
    /// column is not a JSON object. That is every top-level member (cached asset texts; authored members such as
    /// <c>listeningQuestions</c> are returned too, callers only test asset ids) plus the ids listed under
    /// <see cref="ContentTextExtractionService.ExhaustedKey"/>, whose automatic attempts are used up: the worker's own SQL
    /// pre-filter treats "id appears as a key" as "nothing left to do" for both, so a remote job must not be sent for an
    /// asset the local pass has deliberately given up on until a forced extraction succeeds.
    /// </summary>
    public static HashSet<string>? ReadKeys(string? existingJson)
    {
        if (string.IsNullOrWhiteSpace(existingJson)) return new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(existingJson);
            if (document.RootElement.ValueKind == JsonValueKind.Null) return new HashSet<string>(StringComparer.Ordinal);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in document.RootElement.EnumerateObject())
            {
                keys.Add(member.Name);
                if (member.Name == ContentTextExtractionService.ExhaustedKey && member.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var exhausted in member.Value.EnumerateObject()) keys.Add(exhausted.Name);
                }
            }

            return keys;
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
        ClearFailureMarkers(existing, assetKey);
        return new ExtractedTextMergeResult(ExtractedTextMergeStatus.Written, JsonSerializer.Serialize(existing));
    }

    /// <summary>
    /// An asset that now has text has neither a failure marker nor an exhausted entry, exactly as the local pass leaves it
    /// (<c>ContentTextExtractionService</c> drops both when it caches a result). Only this asset's entries are touched; the
    /// reserved keys disappear when they empty out, and a value of an unexpected shape is left as it is.
    /// </summary>
    private static void ClearFailureMarkers(Dictionary<string, JsonElement> payload, string assetKey)
    {
        if (payload.TryGetValue(ContentTextExtractionService.FailuresKey, out var failures) && failures.ValueKind == JsonValueKind.Array)
        {
            var kept = failures.EnumerateArray()
                .Where(item => !(item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("assetId", out var id)
                    && id.ValueKind == JsonValueKind.String
                    && string.Equals(id.GetString(), assetKey, StringComparison.Ordinal)))
                .ToList();
            if (kept.Count == 0) payload.Remove(ContentTextExtractionService.FailuresKey);
            else if (kept.Count != failures.GetArrayLength()) payload[ContentTextExtractionService.FailuresKey] = JsonSerializer.SerializeToElement(kept);
        }

        if (payload.TryGetValue(ContentTextExtractionService.ExhaustedKey, out var exhausted)
            && exhausted.ValueKind == JsonValueKind.Object
            && exhausted.TryGetProperty(assetKey, out _))
        {
            var kept = exhausted.EnumerateObject()
                .Where(member => !string.Equals(member.Name, assetKey, StringComparison.Ordinal))
                .ToDictionary(member => member.Name, member => member.Value, StringComparer.Ordinal);
            if (kept.Count == 0) payload.Remove(ContentTextExtractionService.ExhaustedKey);
            else payload[ContentTextExtractionService.ExhaustedKey] = JsonSerializer.SerializeToElement(kept);
        }
    }
}
