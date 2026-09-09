using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Companion;

/// <summary>What an output screen found, and whether the turn may be delivered.</summary>
public sealed record CompanionLeakVerdict(
    bool Blocked,
    IReadOnlyList<string> Findings)
{
    public static readonly CompanionLeakVerdict Clean = new(false, []);
}

/// <summary>
/// Last line of defence on the way <b>out</b>.
///
/// <para>
/// Every other control in the companion is an input control: the entitlement
/// prefilter decides what may be retrieved, the extraction budget decides how
/// much may be packed, the prompt tells the model what not to say. All of them
/// act before the model speaks, and none of them can see what it actually said.
/// The Manifest asks for a canary and an output-side leak check precisely
/// because a model can reproduce something it was told not to, and an input
/// control cannot detect that after the fact.
/// </para>
///
/// <para>
/// This is deliberately narrow. It looks for things that are never
/// legitimate in a learner-facing answer, and it does not attempt to judge
/// quality:
/// </para>
/// <list type="number">
///   <item><b>Canary strings</b> planted in proprietary sources. Emitting one
///   verbatim means source text reached the output unparaphrased — a security
///   event, not a formatting slip.</item>
///   <item><b>Acceptance-pack scaffolding</b>, reusing the corpus guard's
///   markers. If a PASS CHECK ever appears in an answer the corpus is
///   contaminated and the whole run is void; better to find out from a blocked
///   turn than from a passing scorecard.</item>
///   <item><b>Credential shapes</b> — API keys, bearer tokens, connection
///   strings. The prompt forbids revealing configuration; this catches the case
///   where it did anyway.</item>
/// </list>
///
/// <para>
/// A finding blocks the turn rather than redacting it. A partially redacted
/// answer built on leaked material is still built on leaked material, and the
/// learner is better served by an honest failure than by a doctored one.
/// </para>
/// </summary>
public static class CompanionLeakDetector
{
    /// <summary>
    /// Credential shapes, not credential values. Matching on shape means this
    /// keeps working when keys are rotated, and needs no secret of its own.
    /// </summary>
    private static readonly (string Label, Regex Pattern)[] SecretPatterns =
    [
        ("openai-style api key", new Regex(@"\bsk-[A-Za-z0-9_-]{20,}", RegexOptions.Compiled)),
        ("anthropic api key", new Regex(@"\bsk-ant-[A-Za-z0-9_-]{20,}", RegexOptions.Compiled)),
        ("aws access key id", new Regex(@"\bAKIA[0-9A-Z]{16}\b", RegexOptions.Compiled)),
        ("bearer token", new Regex(@"\bBearer\s+[A-Za-z0-9._-]{24,}", RegexOptions.Compiled)),
        ("json web token", new Regex(@"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}", RegexOptions.Compiled)),
        ("database connection string", new Regex(@"\b(?:Host|Server|Data\s+Source)\s*=\s*[^;]{1,64};.*?\bPassword\s*=", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("private key block", new Regex(@"-----BEGIN(?:\s+\w+)?\s+PRIVATE KEY-----", RegexOptions.Compiled)),
    ];

    /// <summary>
    /// Screens one composed answer before it reaches the learner.
    /// </summary>
    /// <param name="canaryTags">
    /// Canary strings belonging to the sources that were actually retrieved for
    /// this turn. Passing every canary in the corpus would be both slower and
    /// wrong: a canary the turn never saw cannot have leaked from it.
    /// </param>
    public static CompanionLeakVerdict Screen(string? answer, IEnumerable<string?> canaryTags)
    {
        if (string.IsNullOrWhiteSpace(answer)) return CompanionLeakVerdict.Clean;

        var findings = new List<string>();

        foreach (var tag in canaryTags)
        {
            if (string.IsNullOrWhiteSpace(tag)) continue;
            if (answer.Contains(tag, StringComparison.OrdinalIgnoreCase))
            {
                // The tag itself is not repeated into the finding — that would
                // publish the canary into the logs and burn it.
                findings.Add("canary marker from a proprietary source appeared verbatim in the answer");
            }
        }

        if (CompanionCorpusGuard.FindDisqualifyingMarker(answer) is { } marker)
        {
            findings.Add($"acceptance-test scaffolding (\"{marker}\") appeared in the answer — the corpus is contaminated");
        }

        foreach (var (label, pattern) in SecretPatterns)
        {
            if (pattern.IsMatch(answer)) findings.Add($"possible {label} in the answer");
        }

        return findings.Count == 0
            ? CompanionLeakVerdict.Clean
            : new CompanionLeakVerdict(true, findings);
    }

    /// <summary>
    /// Words of unbroken proprietary text that count as reproduction rather than
    /// quotation. Long enough that a shared clinical phrase or a rule title
    /// quoted to name it does not trip; short enough that a reproduced paragraph
    /// does.
    /// </summary>
    private const int VerbatimSpanWords = 25;

    /// <summary>
    /// Detects the companion reproducing a paid source instead of teaching from
    /// it, by looking for a long unbroken run of the source's own words in the
    /// answer.
    ///
    /// <para>
    /// This is the general form of a canary and needs nothing planted in the
    /// corpus. A canary only fires if the leaked passage happens to contain the
    /// marker; a span check fires on any passage, which is what the "never
    /// reproduce a paid source at length" rule actually means. It also cannot be
    /// defeated by asking for the material a section at a time, because each
    /// turn is screened on its own output.
    /// </para>
    ///
    /// <para>
    /// Comparison is on normalised words, so reformatting — different line
    /// breaks, added bullets, changed capitalisation — does not evade it, while
    /// genuine paraphrase (which changes the words) passes.
    /// </para>
    /// </summary>
    public static CompanionLeakVerdict ScreenVerbatimReuse(
        string? answer,
        IEnumerable<CompanionEvidence> evidence)
    {
        if (string.IsNullOrWhiteSpace(answer)) return CompanionLeakVerdict.Clean;

        var answerWords = Normalise(answer);
        if (answerWords.Length < VerbatimSpanWords) return CompanionLeakVerdict.Clean;

        var answerText = " " + string.Join(' ', answerWords) + " ";

        foreach (var item in evidence)
        {
            if (!item.IsProprietary) continue;

            var sourceWords = Normalise(item.Text);
            if (sourceWords.Length < VerbatimSpanWords) continue;

            for (var start = 0; start + VerbatimSpanWords <= sourceWords.Length; start++)
            {
                var span = " " + string.Join(' ', sourceWords.AsSpan(start, VerbatimSpanWords).ToArray()) + " ";
                if (!answerText.Contains(span, StringComparison.Ordinal)) continue;

                return new CompanionLeakVerdict(true,
                [
                    $"the answer reproduces {VerbatimSpanWords}+ consecutive words of the proprietary source " +
                    $"\"{item.SourceTitle}\" verbatim rather than teaching from it",
                ]);
            }
        }

        return CompanionLeakVerdict.Clean;
    }

    private static readonly char[] WordSeparators =
        [' ', '\t', '\n', '\r', '-', '\u2014', '\u2013', '*', '#', '>', '|'];

    private static readonly char[] WordTrim =
        ['.', ',', ';', ':', '(', ')', '"', '\'', '!', '?'];

    private static string[] Normalise(string text) =>
        text.ToLowerInvariant()
            .Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim(WordTrim))
            .Where(word => word.Length > 0)
            .ToArray();
}
