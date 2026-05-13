using System.Text.Json;

namespace OetLearner.Api.Services.Listening;

internal static class ListeningAttemptScope
{
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

    internal readonly record struct PathwayStageScope(bool HasScope, string? Stage)
    {
        public static PathwayStageScope None { get; } = new(false, null);
        public static PathwayStageScope Invalid { get; } = new(true, null);
    }
}