using System.Collections.Immutable;

namespace OetLearner.Api.Services.Listening;

/// <summary>
/// Canonical skill-tag vocabulary for ListeningQuestion.SkillTag (the column
/// already exists on the entity — see Domain/ListeningEntities.cs). This
/// closed set is enforced at the admin publish gate (WS-A) and by the per-
/// skill-tag analytics aggregation (WS-B). Reserved up-front by
/// PRD-LISTENING-V2.md §5.2 / §5.4 so the authoring UI cannot silently widen
/// the vocabulary (critic finding HIGH #2: undefined ML semantic field).
/// </summary>
internal static class ListeningSkillTags
{
    public const string Purpose = "purpose";
    public const string Gist = "gist";
    public const string Detail = "detail";
    public const string Opinion = "opinion";
    public const string Warning = "warning";
    public const string Attitude = "attitude";
    public const string NoteCompletion = "note_completion";
    public const string Other = "other";

    public static readonly ImmutableHashSet<string> Allowed = ImmutableHashSet.CreateRange(
        StringComparer.OrdinalIgnoreCase,
        new[]
        {
            Purpose,
            Gist,
            Detail,
            Opinion,
            Warning,
            Attitude,
            NoteCompletion,
            Other,
        });

    /// <summary>Returns true when value is null/blank (treated as "untagged")
    /// or matches the canonical vocabulary. Authoring UIs should reject any
    /// value where this returns false.</summary>
    public static bool IsValid(string? value)
        => string.IsNullOrWhiteSpace(value) || Allowed.Contains(value);
}
