using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.OwnerAgent;

/// <summary>
/// Validation of every caller-supplied value that ends up in a sidecar URL
/// (CONTRACT.md: ids are ULIDs; engines are <c>claude</c> | <c>codex</c> | <c>opencode</c>).
/// Nothing reaches <see cref="OwnerAgentClient"/> path building unvalidated,
/// so a crafted id can never traverse to another sidecar route.
/// </summary>
public static partial class OwnerAgentIds
{
    public const string Claude = "claude";
    public const string Codex = "codex";
    public const string OpenCode = "opencode";

    public static readonly IReadOnlySet<string> Engines = new HashSet<string>(StringComparer.Ordinal) { Claude, Codex, OpenCode };
    public static readonly IReadOnlySet<string> Modes = new HashSet<string>(StringComparer.Ordinal) { "read_only", "guarded", "autopilot" };
    public static readonly IReadOnlySet<string> Decisions = new HashSet<string>(StringComparer.Ordinal) { "approve", "deny", "approve_session" };

    public const string AutopilotMode = "autopilot";

    // Crockford base32, 26 chars, first char 0-7 (48-bit timestamp headroom).
    [GeneratedRegex("^[0-7][0-9A-HJKMNP-TV-Z]{25}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex UlidPattern();

    public static bool IsUlid(string? value) => value is { Length: 26 } && UlidPattern().IsMatch(value);

    public static bool IsEngine(string? value) => value is not null && Engines.Contains(value);

    /// <summary>Throws a 400 <c>invalid_{name}</c> unless <paramref name="value"/> is a ULID.</summary>
    public static string RequireUlid(string? value, string name)
        => IsUlid(value)
            ? value!
            : throw ApiException.Validation($"invalid_{name}", $"'{name}' must be a ULID.");

    public static string RequireEngine(string? value)
        => IsEngine(value)
            ? value!
            : throw ApiException.Validation("invalid_engine", "Engine must be 'claude', 'codex' or 'opencode'.");

    public static string RequireMode(string? value)
        => value is not null && Modes.Contains(value)
            ? value
            : throw ApiException.Validation("invalid_mode", "Mode must be 'read_only', 'guarded' or 'autopilot'.");

    /// <summary>
    /// Opaque engine-reported strings (model/effort ids). Never enumerated here —
    /// only bounded and restricted to printable characters.
    /// </summary>
    public static string? OptionalOpaque(string? value, string name, int maxLength = 128)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Length > maxLength || trimmed.Any(char.IsControl))
        {
            throw ApiException.Validation($"invalid_{name}", $"'{name}' must be 1-{maxLength} printable characters.");
        }

        return trimmed;
    }

    public static string RequireOpaque(string? value, string name, int maxLength = 128)
        => OptionalOpaque(value, name, maxLength)
           ?? throw ApiException.Validation($"{name}_required", $"'{name}' is required.");

    /// <summary>Free text (titles, notes, messages): bounded; control characters other than CR/LF/TAB rejected.</summary>
    public static string? OptionalText(string? value, string name, int maxLength)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length > maxLength)
        {
            throw ApiException.Validation($"{name}_too_long", $"'{name}' must be at most {maxLength} characters.");
        }

        if (value.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t'))
        {
            throw ApiException.Validation($"invalid_{name}", $"'{name}' contains control characters.");
        }

        return value;
    }
}
