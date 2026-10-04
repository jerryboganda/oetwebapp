using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// What of a saved Speaking transcript counts as the candidate's performance.
///
/// A live conversation can open with connection-check chatter ("Hi, can you hear me?" / "Yeah, I hear you.
/// Go ahead.") before the role-play starts. That is not part of the assessed performance, and left in it
/// distorts Relationship Building, Fluency and timing (owner spec 4 Oct 2026, section 7.2). The stored
/// segments, and their hash, are never altered or deleted — this only decides what the grader and the
/// candidate-facing marked transcript are shown.
///
/// Only the LEADING chatter is removed: it stops at the first sentence that is not a connection check, so a
/// real greeting ("Hello, I'm Dr ...") and anything said later in the consultation is always kept.
/// </summary>
public static class SpeakingTranscriptEvidence
{
    /// <summary>A connection-check sentence is short; anything longer is never chatter.</summary>
    private const int MaxChatterWords = 8;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Regex SentenceBreak = new(
        @"(?<=[.!?…])\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex NotWordOrSpace = new(
        @"[^\p{L}\p{N}\s']",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex Spaces = new(
        @"\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private const string Opener = @"((hi|hello|hey|okay|ok|so|right|alright) )*";
    private const string Ack = @"((yes|yeah|yep|yup|okay|ok|sure|alright|alrighty) )*";

    // Matched against a lower-cased sentence with punctuation removed.
    private static readonly Regex[] ConnectivityPatterns =
    [
        // "Can you hear me?" / "Hi, can you hear me okay?"
        Pattern($"^{Opener}(can|could) you (hear|see) me( (okay|ok|clearly|now|properly|well|alright|fine))*$"),
        // "Is this working?" / "Is my mic on?"
        Pattern($"^{Opener}(is|are) (this|it|the line|the audio|the sound|my mic|my microphone|the mic|the microphone) (working|on|fine|ok|okay|clear)( now)?$"),
        // "Testing, testing." / "Mic check."
        Pattern($"^{Opener}(testing|test|check|mic check|sound check|audio check)( (one|two|three|1|2|3|testing|check))*$"),
        // "Am I audible?"
        Pattern(@"^am i (audible|coming through|loud enough)( (okay|ok|clearly|now))?$"),
        // The acknowledgement: "Yeah, I hear you." / "Loud and clear."
        Pattern($"^{Ack}(i )?(can )?hear you( (loud and clear|clearly|fine|well|okay|ok))?$"),
        Pattern($"^{Ack}loud and clear$"),
        // The go-ahead that follows a connection check.
        Pattern($"^{Ack}(please )?(go ahead|go on|carry on)( doctor| please)*$"),
    ];

    private static Regex Pattern(string expression)
        => new(expression, RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    /// <summary>True for a single connection-check sentence (either side of the call).</summary>
    public static bool IsConnectivitySentence(string? sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence)) return false;
        var normalised = Normalise(sentence);
        if (normalised.Length == 0) return false;
        if (normalised.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > MaxChatterWords) return false;

        foreach (var pattern in ConnectivityPatterns)
        {
            try
            {
                if (pattern.IsMatch(normalised)) return true;
            }
            catch (RegexMatchTimeoutException)
            {
                // A pathological input is simply not chatter.
            }
        }

        return false;
    }

    /// <summary>
    /// The segments JSON with the LEADING connection-check chatter removed. Returns the input unchanged
    /// (the same string) when there is none, or when it cannot be read — grading must never fail on a
    /// transcript-shape drift.
    /// </summary>
    public static string StripConnectivityChatter(string? segmentsJson)
    {
        if (string.IsNullOrWhiteSpace(segmentsJson)) return segmentsJson ?? string.Empty;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(segmentsJson);
        }
        catch (JsonException)
        {
            return segmentsJson;
        }

        if (root is not JsonArray segments) return segmentsJson;

        var changed = false;
        var inLeadingZone = true;
        var kept = new JsonArray();
        foreach (var node in segments)
        {
            if (!inLeadingZone || node is not JsonObject segment || !TryReadText(segment, out var text))
            {
                kept.Add(node?.DeepClone());
                continue;
            }

            var sentences = SentenceBreak.Split(text.Trim());
            var firstReal = 0;
            while (firstReal < sentences.Length && IsConnectivitySentence(sentences[firstReal])) firstReal++;

            if (firstReal == sentences.Length)
            {
                // The whole segment was a connection check: drop it and stay in the leading zone.
                changed = true;
                continue;
            }

            inLeadingZone = false;
            if (firstReal == 0)
            {
                kept.Add(segment.DeepClone());
                continue;
            }

            // Chatter and the real opening share one segment: keep only the real part.
            var clone = (JsonObject)segment.DeepClone();
            clone["text"] = string.Join(" ", sentences.Skip(firstReal));
            kept.Add(clone);
            changed = true;
        }

        return changed ? kept.ToJsonString() : segmentsJson;
    }

    private static bool TryReadText(JsonObject segment, out string text)
    {
        text = string.Empty;
        if (segment["text"] is not JsonValue value || !value.TryGetValue<string>(out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        text = raw;
        return true;
    }

    private static string Normalise(string sentence)
    {
        var lower = sentence.Trim().ToLowerInvariant();
        var stripped = NotWordOrSpace.Replace(lower, " ");
        return Spaces.Replace(stripped, " ").Trim();
    }
}
