using System.Text;

namespace Fleet.Manager.Dashboard;

/// <summary>
/// The Add form asks for an address and, optionally, a name. The API needs a stable <c>nodeRef</c>
/// (<c>^[a-z0-9][a-z0-9-]{2,62}$</c>), so the console derives one: <c>helper-</c> and a slug of the name (or the address).
/// </summary>
public static class NodeRefs
{
    public const int MaxSlug = 40;

    /// <summary>Lower-case letters and digits; every other run of characters becomes one hyphen; no hyphen at either end.</summary>
    public static string Slug(string? text)
    {
        var builder = new StringBuilder();
        foreach (var ch in (text ?? string.Empty).Trim().ToLowerInvariant())
        {
            if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }

    /// <summary>The first choice of node reference for a helper. Always valid for <c>InputValidator.ValidateNodeRef</c>.</summary>
    public static string Candidate(string? displayName, string address)
    {
        var slug = Slug(string.IsNullOrWhiteSpace(displayName) ? address : displayName);
        if (slug.Length > MaxSlug)
        {
            slug = slug[..MaxSlug].TrimEnd('-');
        }

        if (slug.Length == 0)
        {
            return "helper";
        }

        return slug.StartsWith("helper", StringComparison.Ordinal) ? slug : "helper-" + slug;
    }

    /// <summary>The candidate with a numeric suffix (<c>-2</c>, <c>-3</c>, ...) for the nth try; the first try is the candidate itself.</summary>
    public static string WithSuffix(string candidate, int attempt) => attempt <= 1 ? candidate : candidate + "-" + attempt;
}
