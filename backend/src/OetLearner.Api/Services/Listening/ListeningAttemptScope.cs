using System.Text.Json;

namespace OetLearner.Api.Services.Listening;

internal static class ListeningAttemptScope
{
    public const string PartPracticeKind = "part-practice";

    // pathwayStage is the canonical key; stageCode/stage are accepted for
    // older pathway-scoped attempts and manual test fixtures.
    private static readonly string[] PathwayStageKeys = ["pathwayStage", "stageCode", "stage"];

    public static string Build(string normalizedMode, string sourceKind, string? normalizedPathwayStage)
    {
        var scope = new Dictionary<string, object?>
        {
            ["mode"] = normalizedMode,
            ["sourceKind"] = sourceKind,
        };

        if (normalizedPathwayStage is not null)
        {
            scope["pathwayStage"] = normalizedPathwayStage;
        }

        return JsonSupport.Serialize(scope);
    }

    public static string BuildPartPractice(string partCode, IReadOnlyList<string> questionIds, int minutes)
    {
        var scope = new Dictionary<string, object?>
        {
            ["kind"] = PartPracticeKind,
            ["mode"] = "practice",
            ["sourceKind"] = "content_paper",
            ["partCode"] = partCode,
            ["minutes"] = minutes,
            ["questionIds"] = questionIds,
        };

        return JsonSupport.Serialize(scope);
    }

    public static bool MatchesRequestedScope(string? scopeJson, string? requestedPathwayStage)
    {
        // Part-practice attempts are first-class and must never be reused as
        // an unscoped or pathway-scoped full-paper practice attempt.
        if (ReadPartPractice(scopeJson).IsPartPractice) return false;
        return MatchesRequestedPathwayStage(scopeJson, requestedPathwayStage);
    }

    public static bool MatchesRequestedPartPractice(string? scopeJson, string partCode)
    {
        var scope = ReadPartPractice(scopeJson);
        return scope.IsValid
            && string.Equals(scope.PartCode, partCode, StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesRequestedPathwayStage(string? scopeJson, string? requestedPathwayStage)
    {
        var scope = ReadPathwayStage(scopeJson);
        if (requestedPathwayStage is null) return !scope.HasScope;
        return scope.HasScope && string.Equals(scope.Stage, requestedPathwayStage, StringComparison.Ordinal);
    }

    public static PathwayStageScope ReadPathwayStage(string? scopeJson)
    {
        if (string.IsNullOrWhiteSpace(scopeJson)) return PathwayStageScope.None;

        try
        {
            using var doc = JsonDocument.Parse(scopeJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return PathwayStageScope.None;

            foreach (var propertyName in PathwayStageKeys)
            {
                if (!doc.RootElement.TryGetProperty(propertyName, out var value)) continue;
                if (value.ValueKind != JsonValueKind.String) return PathwayStageScope.Invalid;

                var stage = value.GetString();
                return ListeningPathwayProgressService.PathwayStages.Contains(stage, StringComparer.Ordinal)
                    ? new PathwayStageScope(true, stage)
                    : PathwayStageScope.Invalid;
            }
        }
        catch (JsonException)
        {
            return PathwayStageScope.None;
        }

        return PathwayStageScope.None;
    }

    public static PartPracticeScope ReadPartPractice(string? scopeJson)
    {
        if (string.IsNullOrWhiteSpace(scopeJson)) return PartPracticeScope.None;

        try
        {
            using var doc = JsonDocument.Parse(scopeJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return PartPracticeScope.None;
            if (!doc.RootElement.TryGetProperty("kind", out var kind)
                || kind.ValueKind != JsonValueKind.String
                || !string.Equals(kind.GetString(), PartPracticeKind, StringComparison.OrdinalIgnoreCase))
            {
                return PartPracticeScope.None;
            }

            var partCode = doc.RootElement.TryGetProperty("partCode", out var partElement)
                && partElement.ValueKind == JsonValueKind.String
                    ? partElement.GetString()
                    : null;
            var minutes = 0;
            if (doc.RootElement.TryGetProperty("minutes", out var minutesElement)
                && minutesElement.TryGetInt32(out var parsedMinutes))
            {
                minutes = parsedMinutes;
            }

            var questionIds = new List<string>();
            if (doc.RootElement.TryGetProperty("questionIds", out var ids)
                && ids.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in ids.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(item.GetString()))
                    {
                        questionIds.Add(item.GetString()!);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(partCode) || questionIds.Count == 0)
            {
                return PartPracticeScope.Invalid;
            }

            return new PartPracticeScope(true, partCode.Trim().ToUpperInvariant(), questionIds, minutes);
        }
        catch (JsonException)
        {
            return PartPracticeScope.None;
        }
    }

    internal readonly record struct PathwayStageScope(bool HasScope, string? Stage)
    {
        public static PathwayStageScope None { get; } = new(false, null);
        public static PathwayStageScope Invalid { get; } = new(true, null);
    }

    internal readonly record struct PartPracticeScope(
        bool IsPartPractice,
        string? PartCode,
        IReadOnlyList<string> QuestionIds,
        int Minutes)
    {
        public static PartPracticeScope None { get; } = new(false, null, Array.Empty<string>(), 0);
        public static PartPracticeScope Invalid { get; } = new(true, null, Array.Empty<string>(), 0);
        public bool IsValid => IsPartPractice && !string.IsNullOrWhiteSpace(PartCode) && QuestionIds.Count > 0;
    }
}