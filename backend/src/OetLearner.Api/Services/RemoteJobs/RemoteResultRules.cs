using System.Text.Json;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>
/// Character rules for text a node returns (OET-RWP/1 section 6.1.3). A violation is CONTENT-class: the job ends
/// <c>Failed(content_rejected)</c>, the node is not struck, and the local path handles the asset. (Consequence, by
/// design: a PDF whose in-process extraction contains such characters is always handled on the primary.)
/// </summary>
public static class RemoteTextRules
{
    /// <summary>
    /// Forbidden: U+0000-U+0008, U+000B, U+000C, U+000E-U+001F, U+007F-U+009F, U+FFFE, U+FFFF and unpaired surrogates.
    /// Allowed: TAB, LF, CR, U+FFFD, U+00A0 and everything else.
    /// </summary>
    public static bool HasForbiddenCharacter(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    i++;
                    continue;
                }

                return true;
            }

            if (char.IsLowSurrogate(c)) return true;
            if (c <= 0x08 || c == 0x0B || c == 0x0C || (c >= 0x0E && c <= 0x1F) || (c >= 0x7F && c <= 0x9F)
                || c == 0xFFFE || c == 0xFFFF)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Small strict readers over a <see cref="JsonElement"/> object used by the per-kind result parsers.</summary>
internal readonly struct StrictJson
{
    private readonly JsonElement root;

    public StrictJson(JsonElement element)
    {
        root = element;
    }

    public bool IsObject => root.ValueKind == JsonValueKind.Object;

    public bool TryString(string name, out string? value, bool allowNull = false)
    {
        value = null;
        if (!root.TryGetProperty(name, out var element)) return false;
        if (element.ValueKind == JsonValueKind.Null) return allowNull;
        if (element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString();
        return value is not null;
    }

    public bool TryInt(string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }

    public bool TryBool(string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out var element)) return false;
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = element.GetBoolean();
        return true;
    }

    /// <summary>An array of strings; <paramref name="allowNull"/> lets a JSON null mean "absent".</summary>
    public bool TryStringArray(string name, out List<string>? values, bool allowNull)
    {
        values = null;
        if (!root.TryGetProperty(name, out var element)) return false;
        if (element.ValueKind == JsonValueKind.Null) return allowNull;
        if (element.ValueKind != JsonValueKind.Array) return false;

        var list = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) return false;
            var text = item.GetString();
            if (text is null) return false;
            list.Add(text);
        }

        values = list;
        return true;
    }

    /// <summary>The raw objects of an array of objects (each wrapped for further strict reading).</summary>
    public bool TryObjectArray(string name, out List<JsonElement>? values)
    {
        values = null;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Array) return false;
        var list = new List<JsonElement>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) return false;
            list.Add(item);
        }

        values = list;
        return true;
    }
}

/// <summary>Reads the job parameters the handlers need from <c>RemoteJobs.ParamsJson</c>.</summary>
internal static class RemoteJobParams
{
    public static string Mode(string paramsJson) => ReadString(paramsJson, "mode") ?? "flat";

    public static int MinTextLength(string paramsJson) => ReadInt(paramsJson, "minTextLength") ?? 50;

    public static bool IncludePages(string paramsJson) => ReadBool(paramsJson, "includePages") ?? true;

    public static bool ReplaceExisting(string paramsJson) => ReadBool(paramsJson, "replaceExisting") ?? false;

    public static int? ReadInt(string paramsJson, string name)
    {
        if (!TryProperty(paramsJson, name, out var value)) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed) ? parsed : null;
    }

    public static bool? ReadBool(string paramsJson, string name)
    {
        if (!TryProperty(paramsJson, name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        return null;
    }

    public static string? ReadString(string paramsJson, string name)
    {
        if (!TryProperty(paramsJson, name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool TryProperty(string json, string name, out JsonElement value)
    {
        value = default;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!document.RootElement.TryGetProperty(name, out var property)) return false;
            value = property.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
