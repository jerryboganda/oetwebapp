using System.Globalization;
using System.Text.Json;

namespace OetLearner.Api.Services.Writing.Review;

/// <summary>
/// Tolerant reader of the reviewer's reply. The model may wrap the JSON in prose, emit more than one brace span, embed
/// raw newlines in strings, send numbers as strings or leave a member out: none of that is a failure. Only a reply with
/// no JSON object carrying a string <c>decision</c> is unreadable (the attempt then fails over like any provider failure).
/// Unknown finding ids are ignored and recorded; the applier, not this parser, decides what is accepted.
/// </summary>
public static class WritingReviewDecisionParser
{
    private static readonly JsonDocumentOptions Lenient = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <param name="completion">The raw model completion.</param>
    /// <param name="knownFindingIds">Ids of the finding table (f1..fn); verdicts for any other id are dropped.</param>
    public static bool TryParse(
        string? completion,
        IReadOnlyCollection<string> knownFindingIds,
        out WritingReviewDecision decision)
    {
        decision = Empty;
        if (string.IsNullOrWhiteSpace(completion)) return false;

        // The longest balanced object first; the first span that carries a string "decision" wins.
        foreach (var span in WritingSubmissionEvaluationPipeline.ExtractJsonObjectSpans(completion))
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(
                    WritingSubmissionEvaluationPipeline.EscapeControlCharsInStrings(span),
                    Lenient);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                var verdict = Text(root, "decision");
                if (string.IsNullOrWhiteSpace(verdict)) continue;

                decision = Read(root, verdict.Trim().ToLowerInvariant(), knownFindingIds, span);
                return true;
            }
        }

        return false;
    }

    internal static readonly WritingReviewDecision Empty = new(
        "agree",
        null,
        [],
        [],
        [],
        null,
        [],
        null,
        [],
        null,
        [],
        string.Empty);

    private static WritingReviewDecision Read(
        JsonElement root,
        string decision,
        IReadOnlyCollection<string> knownFindingIds,
        string rawJson)
    {
        var anomalies = new List<string>();
        if (decision is not ("agree" or "wording_only" or "corrected"))
        {
            anomalies.Add("unknown_decision:" + decision);
        }

        var known = new HashSet<string>(knownFindingIds, StringComparer.OrdinalIgnoreCase);
        var verdicts = new List<WritingReviewFindingVerdict>();
        foreach (var item in Items(root, "findings"))
        {
            var id = Text(item, "id")?.Trim();
            if (string.IsNullOrEmpty(id)) continue;
            if (!known.Contains(id))
            {
                anomalies.Add("unknown_finding_id:" + id);
                continue;
            }

            var verdict = (Text(item, "verdict") ?? "confirmed").Trim().ToLowerInvariant();
            if (verdict is not ("confirmed" or "false_positive" or "severity_change" or "advisory" or "duplicate"))
            {
                // An unknown verdict never removes or changes anything: it reads as "confirmed".
                anomalies.Add($"unknown_verdict:{id}:{verdict}");
                verdict = "confirmed";
            }

            verdicts.Add(new WritingReviewFindingVerdict(
                id,
                verdict,
                Text(item, "severity"),
                Text(item, "duplicateOf"),
                Text(item, "message"),
                Text(item, "fix"),
                Text(item, "reason")));
        }

        var added = new List<WritingReviewAddedFinding>();
        foreach (var item in Items(root, "added"))
        {
            added.Add(new WritingReviewAddedFinding(
                Text(item, "criterion") ?? string.Empty,
                Text(item, "severity") ?? string.Empty,
                Text(item, "quote") ?? string.Empty,
                Text(item, "message") ?? string.Empty,
                Text(item, "fix"),
                Text(item, "evidence")));
        }

        var omissions = new List<WritingReviewOmissionRuling>();
        foreach (var item in Items(root, "omissions"))
        {
            var factRef = Text(item, "factRef")?.Trim();
            if (string.IsNullOrEmpty(factRef)) continue;
            omissions.Add(new WritingReviewOmissionRuling(factRef, Flag(item, "material") == true, Text(item, "reason")));
        }

        Dictionary<string, int>? scores = null;
        if (Member(root, "criterionScores") is { ValueKind: JsonValueKind.Object } scoreBlock)
        {
            scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in scoreBlock.EnumerateObject())
            {
                if (Number(property.Value) is { } value) scores[property.Name.Trim()] = value;
            }
        }

        var changes = new List<WritingReviewScoreChange>();
        foreach (var item in Items(root, "scoreChanges"))
        {
            var criterion = Text(item, "criterion")?.Trim();
            if (string.IsNullOrEmpty(criterion)) continue;
            var ids = new List<string>();
            foreach (var idElement in Items(item, "findingIds"))
            {
                if (idElement.ValueKind == JsonValueKind.String && idElement.GetString() is { Length: > 0 } cited)
                {
                    ids.Add(cited.Trim());
                }
            }

            changes.Add(new WritingReviewScoreChange(
                criterion,
                Number(Member(item, "from")) ?? 0,
                Number(Member(item, "to")) ?? 0,
                ids));
        }

        var unsupported = new List<string>();
        foreach (var element in Items(root, "unsupportedFixIds"))
        {
            if (element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 } fixId)
            {
                unsupported.Add(fixId.Trim());
            }
        }

        var enhancedNotes = Member(root, "enhanced") is { ValueKind: JsonValueKind.Object } enhanced
            ? Text(enhanced, "notes")
            : null;

        return new WritingReviewDecision(
            decision,
            Text(root, "summary"),
            verdicts,
            added,
            omissions,
            scores,
            changes,
            Number(Member(root, "estimatedScaledScore")),
            unsupported,
            enhancedNotes,
            anomalies,
            rawJson);
    }

    private static JsonElement? Member(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        }

        return null;
    }

    private static IEnumerable<JsonElement> Items(JsonElement element, string name)
    {
        if (Member(element, name) is { ValueKind: JsonValueKind.Array } array)
        {
            foreach (var item in array.EnumerateArray()) yield return item;
        }
    }

    private static string? Text(JsonElement element, string name)
    {
        if (Member(element, name) is not { } value) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static bool? Flag(JsonElement element, string name)
    {
        if (Member(element, name) is not { } value) return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) ? parsed : (bool?)null,
            _ => null,
        };
    }

    private static int? Number(JsonElement? element)
    {
        if (element is not { } value) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return (int)Math.Round(number, MidpointRounding.AwayFromZero);
        }

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return (int)Math.Round(parsed, MidpointRounding.AwayFromZero);
        }

        return null;
    }
}
