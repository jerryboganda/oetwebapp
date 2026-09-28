using System.Text.Json;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// Shared parsing helpers for AI completions in the draft and explanation services.
/// </summary>
internal static class AiReplyParsing
{
    /// <summary>Accepts a bare JSON object or the first fenced ```json block; otherwise null.</summary>
    public static string? ExtractFencedJsonObject(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("{") && trimmed.EndsWith("}")) return trimmed;
        var fenceStart = trimmed.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (fenceStart < 0) fenceStart = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart < 0) return null;
        var afterFence = trimmed.IndexOf('\n', fenceStart);
        if (afterFence < 0) return null;
        var closeFence = trimmed.IndexOf("```", afterFence + 1, StringComparison.Ordinal);
        if (closeFence < 0) return null;
        var inner = trimmed[(afterFence + 1)..closeFence].Trim();
        return inner.StartsWith("{") && inner.EndsWith("}") ? inner : null;
    }

    /// <summary>Reads a string or number property as text; any other kind is null.</summary>
    public static string? SafeString(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.ToString(),
            _ => null,
        };
    }

    /// <summary>Case-insensitive enum parse with hyphens removed; blank or unknown falls back to Medicine.</summary>
    public static ExamProfession ParseProfessionOrMedicine(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ExamProfession.Medicine;
        return Enum.TryParse<ExamProfession>(raw.Replace("-", ""), ignoreCase: true, out var p)
            ? p
            : ExamProfession.Medicine;
    }
}
