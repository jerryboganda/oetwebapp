using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Medication-frequency source fidelity (owner directive, 9 Oct 2026). The original case notes are the
/// primary source of truth: a letter must never give a medicine a frequency the notes do not record
/// (Pharmacy ADR, Mrs Daniels: the scan reads "Indapamide 2.5mg 1q.d." = once daily, while the answer said
/// "four times daily"). The check id is <c>medication_frequency_source_mismatch</c>.
/// <list type="bullet">
/// <item>Source-gated: it needs the canonical case notes (<see cref="WritingLintInput.CaseNotesText"/>) and stays
/// silent without them, like the other source-fidelity checks.</item>
/// <item>Medicines are found from the notes (a name directly followed by a dose with a unit); a letter mention is
/// only judged when the notes record a frequency for that medicine.</item>
/// <item>Meaning, not spelling: QD, OD, "once daily" and "1 daily" agree; QD is never QID, QDS is four times daily,
/// QOD is every other day, Q6H is not QDS (<see cref="ClinicalAbbreviationGlossary"/>). OD beside an eye cue is the
/// right eye and carries no frequency.</item>
/// <item>Every frequency the notes give the medicine counts (a regime change lists two); a letter is wrong only
/// when its frequency matches none of them.</item>
/// <item>Model Answer: Critical. A candidate letter, if a caller ever supplies notes, gets a Minor note only: the
/// check is coaching, never an automatic score penalty (the grader judges meaning against the original page).</item>
/// </list>
/// Ceiling: only the first frequency after a medicine mention is judged, brand/generic pairs are matched by the
/// same word only, and a frequency the notes give without a dose is not read.
/// </summary>
public sealed partial class WritingRuleEngine
{
    private static readonly Regex MfDrugRe = new(
        @"(?<![A-Za-z])(?<drug>[A-Za-z][A-Za-z\-]{3,})(?![A-Za-z])[,\s]+(?:(?:one|two|three|half)\s+)?\d+(?:\.\d+)?\s*(?:mg|mcg|µg|g|IU|units?|mL|ml|%)(?![A-Za-z])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> MfExtraStopWords = new(StringComparer.OrdinalIgnoreCase) { "inj", "vial" };

    // A window after a medicine ends at a sentence/line end, a lifestyle word, or the next medication item.
    private static readonly Regex[] MfLenientCuts =
    [
        new Regex(@"\n", RegexOptions.CultureInvariant),
        new Regex(@"[.;](?=\s|$)", RegexOptions.CultureInvariant),
        new Regex(@"\b(?:smok\w*|drink\w*|cigarettes?|alcohol|exercis\w*|walks?|attend\w*|visit\w*|review\w*|appointment\w*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new Regex(@"[,;]\s*[A-Za-z][A-Za-z\-]+,?\s+\d", RegexOptions.CultureInvariant),
    ];

    // The letter side also ends at a clause boundary, so the next clause's frequency never leaks in.
    private static readonly Regex MfClauseCut = new(
        @"\b(?:was|were|has|have|had|is|are|and|but|before|after|until|then|later|when|while|being|which|who|whom)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private const int MfWindowLength = 70;

    internal sealed record MedicationFrequencyMismatch(
        string Drug, string LetterPhrase, string LetterKey, int Index, string NotesPhrases);

    private static string MfWindow(string text, int from, IReadOnlyList<string> otherDrugs, bool letterSide)
    {
        var segment = text.Substring(from, Math.Min(MfWindowLength, text.Length - from));
        var cut = segment.Length;
        foreach (var re in MfLenientCuts)
        {
            var m = re.Match(segment);
            if (m.Success && m.Index < cut) cut = m.Index;
        }
        if (letterSide)
        {
            var clause = MfClauseCut.Match(segment);
            if (clause.Success && clause.Index < cut) cut = clause.Index;
        }
        foreach (var other in otherDrugs)
        {
            var m = Regex.Match(segment, @"(?<![A-Za-z])" + Regex.Escape(other) + @"(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success && m.Index < cut) cut = m.Index;
        }
        return segment[..cut];
    }

    private static MatchCollection MfMentions(string text, string drug)
        => Regex.Matches(text, @"(?<![A-Za-z])" + Regex.Escape(drug) + @"(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static List<MedicationFrequencyMismatch> MedicationFrequencyMismatches(string caseNotes, string letter)
    {
        var result = new List<MedicationFrequencyMismatch>();
        var drugs = MfDrugRe.Matches(caseNotes).Cast<Match>()
            .Select(m => m.Groups["drug"].Value.ToLowerInvariant())
            .Where(d => !MedicationStopWords.Contains(d) && !MfExtraStopWords.Contains(d))
            .Distinct()
            .ToList();
        foreach (var drug in drugs)
        {
            var others = drugs.Where(d => d != drug).ToList();
            var noteFrequencies = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match mention in MfMentions(caseNotes, drug))
            {
                var window = MfWindow(caseNotes, mention.Index + mention.Length, others, letterSide: false);
                foreach (var f in ClinicalAbbreviationGlossary.FindFrequencies(window))
                    noteFrequencies.TryAdd(f.Key, f.Raw.Trim());
            }
            if (noteFrequencies.Count == 0) continue;

            foreach (Match mention in MfMentions(letter, drug))
            {
                var from = mention.Index + mention.Length;
                var first = ClinicalAbbreviationGlossary.FindFrequencies(MfWindow(letter, from, others, letterSide: true)).FirstOrDefault();
                if (first is null) continue;
                if (noteFrequencies.Keys.Any(k => ClinicalAbbreviationGlossary.AreCompatible(first.Key, k))) continue;
                var notes = string.Join(" or ", noteFrequencies.Select(kv =>
                    "\"" + kv.Value + "\" (" + ClinicalAbbreviationGlossary.Describe(kv.Key) + ")"));
                result.Add(new MedicationFrequencyMismatch(drug, first.Raw.Trim(), first.Key, from + first.Index, notes));
            }
        }
        return result;
    }

    private static IEnumerable<LintFinding> DetectMedicationFrequencySourceMismatch(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (string.IsNullOrWhiteSpace(input.CaseNotesText) || s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (var x in MedicationFrequencyMismatches(input.CaseNotesText, s.Body))
        {
            var letterMeaning = ClinicalAbbreviationGlossary.Describe(x.LetterKey);
            var message = input.IsModelAnswer
                ? $"The letter gives {x.Drug} as \"{x.LetterPhrase}\" ({letterMeaning}), but the case notes record {x.NotesPhrases}. The original case notes are the primary source of truth and a Model Answer never substitutes a medication frequency (QD = once daily, QID and QDS = four times daily, QOD = every other day). Check the ORIGINAL case-note page before changing either side."
                : $"The letter gives {x.Drug} as \"{x.LetterPhrase}\" ({letterMeaning}), but the case notes record {x.NotesPhrases}. Coaching note only: compare with the original case notes before treating this as an error.";
            yield return new LintFinding(rule.Id, input.IsModelAnswer ? RuleSeverity.Critical : RuleSeverity.Minor,
                message,
                Quote: x.LetterPhrase,
                Start: bodyOffset + x.Index,
                End: bodyOffset + x.Index + x.LetterPhrase.Length,
                FixSuggestion: x.NotesPhrases);
        }
    }
}
