using System.Text.Json;

namespace OetLearner.Api.Services.AiTools;

/// <summary>
/// Minimal JSON-Schema-2020-12 subset validator scoped to the small set of
/// features Phase 5 tools actually use: <c>type</c> (object/string/integer/
/// number/boolean/array), <c>properties</c>, <c>required</c>,
/// <c>additionalProperties</c>, <c>enum</c>, <c>minLength</c>,
/// <c>maxLength</c>, <c>minimum</c>, <c>maximum</c>, <c>minItems</c>,
/// <c>maxItems</c>, and <c>items</c> (single-schema form). Intentionally
/// avoids a full JSON-Schema dependency to keep the trust surface tiny.
///
/// Tool authors keep schemas simple. If a tool needs richer validation it
/// must do it inside <c>ExecuteAsync</c>; this validator is the gate, not
/// the contract.
/// </summary>
public static class AiToolArgValidator
{
    public sealed record Result(bool Ok, string? ErrorCode, string? Message)
    {
        public static readonly Result Success = new(true, null, null);
        public static Result Fail(string code, string message) => new(false, code, message);
    }

    public static Result Validate(JsonElement value, string schemaJson)
    {
        JsonDocument schemaDoc;
        try
        {
            schemaDoc = JsonDocument.Parse(schemaJson);
        }
        catch (JsonException ex)
        {
            return Result.Fail("schema_parse", $"schema is not valid JSON: {ex.Message}");
        }

        using (schemaDoc)
        {
            return Check(value, schemaDoc.RootElement, path: "$");
        }
    }

    private static Result Check(JsonElement value, JsonElement schema, string path)
    {
        if (schema.ValueKind != JsonValueKind.Object)
            return Result.Fail("schema_invalid", $"{path}: schema must be object");

        if (schema.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
        {
            var type = typeEl.GetString();
            if (!CheckType(value, type))
                return Result.Fail("type_mismatch", $"{path}: expected {type}, got {value.ValueKind}");
        }

        if (schema.TryGetProperty("enum", out var enumEl) && enumEl.ValueKind == JsonValueKind.Array)
        {
            var matched = false;
            foreach (var allowed in enumEl.EnumerateArray())
            {
                if (JsonElementEquals(value, allowed)) { matched = true; break; }
            }
            if (!matched)
                return Result.Fail("enum_mismatch", $"{path}: value not in enum");
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                if (schema.TryGetProperty("minLength", out var minL) && value.GetString()!.Length < minL.GetInt32())
                    return Result.Fail("min_length", $"{path}: shorter than minLength");
                if (schema.TryGetProperty("maxLength", out var maxL) && value.GetString()!.Length > maxL.GetInt32())
                    return Result.Fail("max_length", $"{path}: longer than maxLength");
                break;

            case JsonValueKind.Number:
                if (schema.TryGetProperty("minimum", out var minN) && value.GetDouble() < minN.GetDouble())
                    return Result.Fail("minimum", $"{path}: below minimum");
                if (schema.TryGetProperty("maximum", out var maxN) && value.GetDouble() > maxN.GetDouble())
                    return Result.Fail("maximum", $"{path}: above maximum");
                break;

            case JsonValueKind.Object:
                {
                    var props = schema.TryGetProperty("properties", out var propsEl) && propsEl.ValueKind == JsonValueKind.Object
                        ? propsEl
                        : default;
                    var required = schema.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.Array
                        ? reqEl
                        : default;
                    var additionalAllowed = !schema.TryGetProperty("additionalProperties", out var apEl)
                                            || apEl.ValueKind != JsonValueKind.False;

                    if (required.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var name in required.EnumerateArray())
                        {
                            var key = name.GetString();
                            if (key is null) continue;
                            if (!value.TryGetProperty(key, out _))
                                return Result.Fail("required_missing", $"{path}.{key}: required");
                        }
                    }

                    foreach (var prop in value.EnumerateObject())
                    {
                        if (props.ValueKind == JsonValueKind.Object && props.TryGetProperty(prop.Name, out var subSchema))
                        {
                            var sub = Check(prop.Value, subSchema, $"{path}.{prop.Name}");
                            if (!sub.Ok) return sub;
                        }
                        else if (!additionalAllowed)
                        {
                            return Result.Fail("additional_property", $"{path}.{prop.Name}: not permitted by schema");
                        }
                    }
                    break;
                }

            case JsonValueKind.Array:
                {
                    if (schema.TryGetProperty("minItems", out var minI) && value.GetArrayLength() < minI.GetInt32())
                        return Result.Fail("min_items", $"{path}: fewer items than minItems");
                    if (schema.TryGetProperty("maxItems", out var maxI) && value.GetArrayLength() > maxI.GetInt32())
                        return Result.Fail("max_items", $"{path}: more items than maxItems");
                    if (schema.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Object)
                    {
                        var idx = 0;
                        foreach (var item in value.EnumerateArray())
                        {
                            var sub = Check(item, itemsEl, $"{path}[{idx}]");
                            if (!sub.Ok) return sub;
                            idx++;
                        }
                    }
                    break;
                }
        }

        return Result.Success;
    }

    private static bool CheckType(JsonElement v, string? type) => type switch
    {
        "object" => v.ValueKind == JsonValueKind.Object,
        "array" => v.ValueKind == JsonValueKind.Array,
        "string" => v.ValueKind == JsonValueKind.String,
        "boolean" => v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False,
        "integer" => v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out _),
        "number" => v.ValueKind == JsonValueKind.Number,
        "null" => v.ValueKind == JsonValueKind.Null,
        _ => true,
    };

    private static bool JsonElementEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        return a.ValueKind switch
        {
            JsonValueKind.String => a.GetString() == b.GetString(),
            JsonValueKind.Number => a.GetRawText() == b.GetRawText(),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            _ => a.GetRawText() == b.GetRawText(),
        };
    }
}
