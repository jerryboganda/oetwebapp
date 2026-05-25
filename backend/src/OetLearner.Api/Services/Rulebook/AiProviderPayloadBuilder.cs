using System.Text.Json;
using OetLearner.Api.Services.AiTools;

namespace OetLearner.Api.Services.Rulebook;

internal static class AiProviderPayloadBuilder
{
    public static List<Dictionary<string, object?>> BuildOpenAiMessages(AiProviderRequest request)
    {
        if (request.Messages is not { Count: > 0 })
        {
            return new List<Dictionary<string, object?>>
            {
                new() { ["role"] = "system", ["content"] = request.SystemPrompt },
                new() { ["role"] = "user", ["content"] = request.UserPrompt },
            };
        }

        return request.Messages.Select(message =>
        {
            var role = (message.Role ?? "user").Trim().ToLowerInvariant();
            if (role == "tool" && string.IsNullOrWhiteSpace(message.ToolCallId))
            {
                throw new InvalidOperationException("AI tool result messages must include a tool call id.");
            }

            var output = new Dictionary<string, object?>
            {
                ["role"] = role,
                ["content"] = message.Content ?? string.Empty,
            };

            if (role == "tool" && !string.IsNullOrWhiteSpace(message.ToolCallId))
            {
                output["tool_call_id"] = message.ToolCallId;
            }

            if (role == "assistant" && message.ToolCalls is { Count: > 0 } calls)
            {
                output["tool_calls"] = calls.Select(call => new Dictionary<string, object?>
                {
                    ["id"] = call.Id,
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object?>
                    {
                        ["name"] = call.ToolCode,
                        ["arguments"] = string.IsNullOrWhiteSpace(call.ArgsJson) ? "{}" : call.ArgsJson,
                    },
                }).ToList();
            }

            return output;
        }).ToList();
    }

    public static List<Dictionary<string, object?>> BuildOpenAiTools(IReadOnlyList<AiToolDefinition>? tools)
    {
        if (tools is not { Count: > 0 }) return new List<Dictionary<string, object?>>();
        return tools.Select(tool => new Dictionary<string, object?>
        {
            ["type"] = "function",
            ["function"] = new Dictionary<string, object?>
            {
                ["name"] = tool.Code,
                ["description"] = string.IsNullOrWhiteSpace(tool.Description) ? tool.Name : tool.Description,
                ["parameters"] = ParseJsonObject(tool.JsonSchemaArgs, $"tool schema for {tool.Code}"),
            },
        }).ToList();
    }

    public static void ReadOpenAiChoiceMessage(JsonElement root, string providerName, out JsonElement choice, out JsonElement message)
    {
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(AiProviderErrorMessages.InvalidResponse(providerName, "missing choices[0]"));
        }

        choice = choices[0];
        if (choice.ValueKind != JsonValueKind.Object
            || !choice.TryGetProperty("message", out message)
            || message.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(AiProviderErrorMessages.InvalidResponse(providerName, "missing choices[0].message"));
        }
    }

    public static IReadOnlyList<AiToolCall>? ReadOpenAiToolCalls(JsonElement message)
    {
        if (!message.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var output = new List<AiToolCall>();
        foreach (var call in calls.EnumerateArray())
        {
            if (!call.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String) continue;
            if (!call.TryGetProperty("function", out var fn) || fn.ValueKind != JsonValueKind.Object) continue;
            if (!fn.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) continue;
            var args = fn.TryGetProperty("arguments", out var arguments)
                ? arguments.ValueKind == JsonValueKind.String ? arguments.GetString() : arguments.GetRawText()
                : "{}";
            output.Add(new AiToolCall
            {
                Id = id.GetString() ?? Guid.NewGuid().ToString("N"),
                ToolCode = name.GetString() ?? string.Empty,
                ArgsJson = string.IsNullOrWhiteSpace(args) ? "{}" : args!,
            });
        }

        return output.Count == 0 ? null : output;
    }

    public static string ReadOpenAiMessageContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content)) return string.Empty;
        if (content.ValueKind == JsonValueKind.String) return content.GetString() ?? string.Empty;
        if (content.ValueKind != JsonValueKind.Array) return content.ToString();

        var parts = new List<string>();
        foreach (var item in content.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                parts.Add(item.GetString() ?? string.Empty);
            }
            else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out var textEl))
            {
                parts.Add(textEl.GetString() ?? string.Empty);
            }
        }

        return string.Join("\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    public static List<Dictionary<string, object?>> BuildAnthropicMessages(AiProviderRequest request)
    {
        var source = request.Messages is { Count: > 0 }
            ? request.Messages.Where(message => !string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase)).ToList()
            : new List<AiChatMessage> { new() { Role = "user", Content = request.UserPrompt } };

        if (source.Count == 0)
        {
            source.Add(new AiChatMessage { Role = "user", Content = request.UserPrompt });
        }

        var output = new List<Dictionary<string, object?>>();
        for (var index = 0; index < source.Count; index++)
        {
            var message = source[index];
            var role = string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";
            if (string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                var toolResults = new List<object>();
                while (index < source.Count && string.Equals(source[index].Role, "tool", StringComparison.OrdinalIgnoreCase))
                {
                    var toolMessage = source[index];
                    if (string.IsNullOrWhiteSpace(toolMessage.ToolCallId))
                    {
                        throw new InvalidOperationException("AI tool result messages must include a tool call id.");
                    }

                    toolResults.Add(new Dictionary<string, object?>
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = toolMessage.ToolCallId,
                        ["content"] = toolMessage.Content ?? string.Empty,
                    });
                    index++;
                }

                index--;
                output.Add(new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = toolResults,
                });
                continue;
            }

            if (string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                && message.ToolCalls is { Count: > 0 } calls)
            {
                var content = new List<object>();
                if (!string.IsNullOrWhiteSpace(message.Content))
                {
                    content.Add(new Dictionary<string, object?> { ["type"] = "text", ["text"] = message.Content });
                }

                content.AddRange(calls.Select(call => new Dictionary<string, object?>
                {
                    ["type"] = "tool_use",
                    ["id"] = call.Id,
                    ["name"] = call.ToolCode,
                    ["input"] = ParseJsonObject(call.ArgsJson, $"tool arguments for {call.ToolCode}"),
                }));

                output.Add(new Dictionary<string, object?> { ["role"] = role, ["content"] = content });
                continue;
            }

            output.Add(new Dictionary<string, object?>
            {
                ["role"] = role,
                ["content"] = message.Content ?? string.Empty,
            });
        }

        return output;
    }

    public static List<Dictionary<string, object?>> BuildAnthropicTools(IReadOnlyList<AiToolDefinition>? tools)
    {
        if (tools is not { Count: > 0 }) return new List<Dictionary<string, object?>>();
        return tools.Select(tool => new Dictionary<string, object?>
        {
            ["name"] = tool.Code,
            ["description"] = string.IsNullOrWhiteSpace(tool.Description) ? tool.Name : tool.Description,
            ["input_schema"] = ParseJsonObject(tool.JsonSchemaArgs, $"tool schema for {tool.Code}"),
        }).ToList();
    }

    public static IReadOnlyList<AiToolCall>? ReadAnthropicToolCalls(JsonElement contentParts)
    {
        if (contentParts.ValueKind != JsonValueKind.Array) return null;
        var output = new List<AiToolCall>();
        foreach (var part in contentParts.EnumerateArray())
        {
            if (!part.TryGetProperty("type", out var type)
                || !string.Equals(type.GetString(), "tool_use", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!part.TryGetProperty("id", out var id) || !part.TryGetProperty("name", out var name)) continue;
            var args = part.TryGetProperty("input", out var input) ? input.GetRawText() : "{}";
            output.Add(new AiToolCall
            {
                Id = id.GetString() ?? Guid.NewGuid().ToString("N"),
                ToolCode = name.GetString() ?? string.Empty,
                ArgsJson = string.IsNullOrWhiteSpace(args) ? "{}" : args,
            });
        }

        return output.Count == 0 ? null : output;
    }

    private static JsonElement ParseJsonObject(string? json, string context)
    {
        if (string.IsNullOrWhiteSpace(json)) json = "{}";
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid AI {context}: expected a JSON object.", ex);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                return doc.RootElement.Clone();
            }
        }

        throw new InvalidOperationException($"Invalid AI {context}: expected a JSON object.");
    }
}