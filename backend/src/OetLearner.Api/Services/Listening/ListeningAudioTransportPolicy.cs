using System.Text.Json;
using OetLearner.Api.Services.Assessment;

namespace OetLearner.Api.Services.Listening;

/// <summary>
/// Resolves the learner-visible audio transport contract from the immutable
/// marking-policy snapshot captured at attempt start. Unknown or malformed
/// policy data is deliberately strict so a stale or tampered snapshot cannot
/// relax an assessment.
/// </summary>
public sealed record ListeningAudioTransportPolicy(
    string LockMode,
    bool CanPause,
    bool CanScrub,
    bool OnePlayOnly)
{
    public static ListeningAudioTransportPolicy Strict { get; } =
        new("exam", CanPause: false, CanScrub: false, OnePlayOnly: true);

    public static ListeningAudioTransportPolicy FromPolicy(
        string? mode,
        AssessmentMarkingPolicyDocument? markingPolicy,
        bool? learningReplayAllowed = null)
    {
        // The high-stakes computer-based modes are never relaxable by a
        // general practice policy document.
        if (IsStrictMode(mode)) return Strict;

        var lockMode = NormalizeLockMode(markingPolicy?.AudioLockMode);
        if (!string.Equals(lockMode, "practice", StringComparison.Ordinal))
            return Strict;

        var replayAllowed = (learningReplayAllowed ?? markingPolicy?.ListeningAudioReplayAllowed == true)
            && markingPolicy?.ListeningAudioReplayAllowed == true;
        return new(
            lockMode,
            CanPause: replayAllowed,
            CanScrub: replayAllowed,
            OnePlayOnly: !replayAllowed);
    }

    public static ListeningAudioTransportPolicy FromSnapshot(
        string? mode,
        string? policySnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(policySnapshotJson)) return Strict;

        try
        {
            using var document = JsonDocument.Parse(policySnapshotJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("markingPolicy", out var policyElement)
                || policyElement.ValueKind != JsonValueKind.Object)
            {
                return Strict;
            }

            var policy = AssessmentMarkingPolicyDocument.Parse(policyElement.GetRawText());
            var root = document.RootElement;
            var listeningPolicy = root.TryGetProperty("listeningPolicy", out var nestedPolicy)
                && nestedPolicy.ValueKind == JsonValueKind.Object
                ? nestedPolicy
                : root;
            bool? learningReplayAllowed = null;
            if (listeningPolicy.TryGetProperty("learningReplayAllowed", out var replay)
                && replay.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                learningReplayAllowed = replay.GetBoolean();
            }

            return FromPolicy(mode, policy, learningReplayAllowed);
        }
        catch (JsonException)
        {
            return Strict;
        }
        catch (InvalidOperationException)
        {
            return Strict;
        }
    }

    private static bool IsStrictMode(string? mode) =>
        string.Equals(mode, "exam", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, "home", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, "diagnostic", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeLockMode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "exam" : value.Trim().ToLowerInvariant();
}
