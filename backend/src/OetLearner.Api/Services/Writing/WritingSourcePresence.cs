using System.Text;
using System.Text.RegularExpressions;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Source-grounding guard (owner directive, Physiotherapy calibration, 9 Oct 2026): a finding may never tell the
/// candidate that a fact is "invented" or "not in the case notes" when that fact IS in the full extracted source.
/// One pure definition of "present", shared by the secondary-review applier, the reviewer prompt and the pipeline
/// fallback, so no layer (primary grader, reviewer, or a stored reply being re-applied) can keep such a claim alive.
/// <para>
/// FAIL-SAFE: <see cref="IsFalseAbsenceClaim"/> only ever removes a claim it can PROVE false, and any doubt keeps the
/// finding, so a real invention is never hidden. It proves only three classes: a date (a date of birth first of all),
/// a range of movement in degrees, and a vital sign or body measurement (mmHg, bpm, degrees Celsius, kg, cm, mm). A
/// dose, a frequency, a duration, a diagnosis or a name is NEVER suppressed here; those go to the reviewer with
/// <see cref="ValueLookup"/> evidence. The proof is strict: the finding must be a plain "this value is absent" claim
/// (no interpretation, no omission wording); the quote must be letter wording whose every word the notes explain, with
/// no unexplained number; every occurrence of the quote must be supported; the value must be in the notes with the SAME
/// label (side, joint, DOB or measurement) and the same event (admission, discharge, active, passive ...); and the
/// message must name nothing beyond that label. A value recorded for another side, joint or event is a real error.
/// </para>
/// Pure: no I/O, no clock, no logging, never throws (any error means "keep the finding"). The facade below also contains
/// a failure of the engine's static initialisation (a type-initialiser exception would otherwise escape at the call site).
/// </summary>
internal static class WritingSourcePresence
{
    internal static bool IsFalseAbsenceClaim(
        string? quote,
        string? message,
        string? letter,
        string? caseNotes,
        string? task,
        out string evidence)
    {
        try
        {
            return WritingSourcePresenceEngine.IsFalseAbsenceClaim(quote, message, letter, caseNotes, task, out evidence);
        }
        catch (Exception)
        {
            evidence = string.Empty;
            return false;
        }
    }

    internal static string? ValueLookup(string? quote, string? message, string? caseNotes, string? task)
    {
        try
        {
            return WritingSourcePresenceEngine.ValueLookup(quote, message, caseNotes, task);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>The implementation behind <see cref="WritingSourcePresence"/>; see its summary for the contract.</summary>
internal static class WritingSourcePresenceEngine
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    // The message says the NOTES lack the fact. "the letter does not state ..." is an omission about the letter and is
    // excluded below; "contradicts" is a real error and is deliberately not a cue.
    private static readonly Regex AbsenceCue = new(
        @"\b(?:invent(?:ed|s|ing|ion)?|fabricat\w*|made[\s-]?up|untraceable|not\s+traceable|(?:not|un)\s*supported|no\s+(?:basis|support|evidence|record|source))\b"
        + @"|\bnot\s+(?:\w+\s+){0,3}?(?:in|within)\s+the\s+(?:[\w-]+\s+)?notes\b"
        + @"|\b(?:absent|missing)\s+from\s+the\s+(?:[\w-]+\s+)?notes\b"
        + @"|\bnotes\s+(?:do|does|did)\s+not\s+(?:record|state|mention|document|contain|list|include|show|support|give|provide|specify)\b"
        + @"|\bnowhere\s+in\s+the\s+(?:[\w-]+\s+)?notes\b"
        + @"|\bdoes\s+not\s+appear\s+in\s+the\s+(?:[\w-]+\s+)?notes\b",
        Opts);

    private static readonly Regex OmissionCue = new(
        @"\b(?:omit(?:s|ted)?|omission|fails?\s+to|(?:letter|response|candidate)\s+(?:does|did)\s+not|(?:missing|absent|not\s+(?:\w+\s+)?(?:included|mentioned|stated|given))\s+(?:from|in)\s+the\s+letter)\b",
        Opts);

    // A finding about an interpretation, qualifier or diagnosis attached to a recorded value is not a claim about the
    // value ("88/70 mmHg called hypotension"): never suppressed.
    private static readonly Regex InterpretationCue = new(
        @"\b(?:hypo\w+|hyper\w+|tachy\w+|brady\w+|fever\w*|febrile|normal|abnormal|mild|moderate|severe|indicat\w+|suggest\w*|consistent\s+with|diagnos\w+|interpret\w+|labell?ed|describ\w+\s+as|called)\b",
        Opts);

    private const string Months =
        @"jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|june?|july?|aug(?:ust)?|sept?(?:ember)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?";

    private static readonly Regex LatexDegree = new(@"\^?\s*\{?\\circ\}?", Opts);
    private static readonly Regex DegreeCelsiusSign = new(@"°[ \t]*c(?![a-z])", Opts);
    private static readonly Regex DegreeCelsiusWord = new(@"deg(?:rees?)?\.?\s*(?:c|celsius)(?![a-z])", Opts);
    private static readonly Regex DegreeWord = new(@"\bdegrees?\b", Opts);
    private static readonly Regex DobToken = new(@"\b(?:d\s?\.?\s?o\s?\.?\s?b|date\s+of\s+birth|birth\s*date|born)\b\.?", Opts);
    private static readonly Regex DobWord = new(@"\bdob\b", Opts);
    private static readonly Regex GluedDate = new(
        @"\b(?<d>\d{1,2})(?:st|nd|rd|th)?\s*[-/.]?\s*(?<m>" + Months + @")\.?\s*[-/.,]?\s*(?<y>\d{4})\b", Opts);

    // One spelling per vital sign, so "blood pressure" = "BP" and "weighs" = "weight" on both sides of the comparison.
    private static readonly (Regex Pattern, string Replacement)[] Rewrites =
    [
        (Rx(@"\bblood\s+pressure\b"), " bp "),
        (Rx(@"\b(?:heart|pulse)\s+rate\b|\bpulse\b|\bhr\b"), " pulse "),
        (Rx(@"\b(?:respiratory|resp)\s+rate\b|\brr\b"), " rr "),
        (Rx(@"\boxygen\s+saturations?\b|\bo2\s+sats?\b|\bsats\b|\bspo2\b"), " oxsat "),
        (Rx(@"\bweight\b|\bweighs\b|\bweighing\b|\bwt\b"), " weight "),
        (Rx(@"\bheight\b|\bht\b"), " height "),
        (Rx(@"\btemperature\b|\btemp\b"), " temp "),
    ];

    // The event or condition a value belongs to. A letter and a source that name different ones are not the same fact.
    private static readonly (Regex Pattern, string Tag)[] Events =
    [
        (Rx(@"\badmi(?:ssion|tted|t)\b"), "adm"),
        (Rx(@"\bdischarg\w*"), "dis"),
        (Rx(@"\bpre-?\s?op\w*"), "preop"),
        (Rx(@"\bpost-?\s?op\w*"), "postop"),
        (Rx(@"\bbaseline\b"), "base"),
        (Rx(@"\bfollow-?\s?up\b"), "fu"),
        (Rx(@"\b(?:supine|lying)\b"), "lying"),
        (Rx(@"\b(?:standing|erect|upright)\b"), "standing"),
        (Rx(@"\b(?:sitting|seated)\b"), "sitting"),
        (Rx(@"\b(?:resting|at\s+rest)\b"), "rest"),
        (Rx(@"\bactive\b"), "active"),
        (Rx(@"\bpassive\b"), "passive"),
        (Rx(@"\bassisted\b"), "assisted"),
        (Rx(@"\b(?:previous|prior)\b"), "prior"),
    ];

    // A number with a unit. Only the first group of units is ever proved; the dose units are matched so that a finding
    // about a dose, a percentage or a count is recognised and left alone.
    private static readonly Regex NumUnit = new(
        @"(?<![\d.,/])(?<n>\d{1,4}(?:[.,]\d{1,2})?(?:\s*/\s*\d{1,4})?)\s*(?<u>degc|deg|mmhg|bpm|kg|cm|mm|mcg|mg|ml|iu|units?|g|%)(?![a-z0-9])",
        Opts);

    private static readonly HashSet<string> ProvableUnits = new(StringComparer.Ordinal) { "deg", "degc", "mmhg", "bpm", "kg", "cm", "mm" };

    private static readonly Regex BareNumber = new(@"(?<![\d.,/])(?<n>\d{1,4}(?:[.,]\d{1,2})?)(?![\d/]|[.,]\d)", Opts);

    private static readonly Regex DateAny = new(
        @"\b(?:\d{1,2}(?:st|nd|rd|th)?\s+(?:" + Months + @")\.?\s+\d{4}"
        + @"|(?:" + Months + @")\.?\s+\d{1,2}(?:st|nd|rd|th)?,?\s+\d{4}"
        + @"|\d{1,2}\s*[./-]\s*\d{1,2}\s*[./-]\s*\d{2,4})\b",
        Opts);

    // An age in the Re: line ("aged 56", "56-year-old") is checked by its own detectors, so it is not a stray number in the
    // QUOTE. A duration ("for 3 years") is NOT an age, and a message about an age or a duration is never suppressed.
    private static readonly Regex AgePhrase = new(@"\baged\s+\d{1,3}\b|\b\d{1,3}[\s-]*(?:years?|yrs?)[\s-]*old\b|\b\d{1,3}\s*y\.?o\.?(?!\w)", Opts);
    private static readonly Regex AgeWords = new(@"\b(?:age|ages|aged|old|years?|yrs?|y\.?o\.?)(?!\w)", Opts);

    // An age or a duration/interval word anywhere in the message, or in the quote outside a DOB claim's age phrase, is a
    // claim about something this class does not prove: the finding stays.
    private static readonly Regex AgeOrDuration = new(
        @"\b(?:aged?|old|years?|yrs?|y\.?o\.?|months?|weeks?|days?|hours?|decades?|once|twice|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety|hundred)(?!\w)",
        Opts);

    // A qualifier symbol changes the meaning of a value ("<90°", "~90°", "90°+") and is invisible to every other check.
    // A minus sign directly before a digit (-10°) is a different value from 10°.
    private static readonly Regex QualifierSymbol = new(@"[<>≤≥≈~±]|\d\s*(?:deg|degc|mmhg|bpm|kg|cm|mm)\s*\+|(?<![\w)])-\d", Opts);

    private static readonly Regex AlphaToken = new(@"[a-z]+", Opts);
    private static readonly Regex HorizontalSpace = new(@"[ \t]+", Opts);
    private static readonly Regex MmHg = new(@"\bmm\s*hg\b", Opts);

    // A clause ends at punctuation or a connector; a colon is NOT a break ("DOB: 28 February 1968" keeps its label), and
    // neither is "and"/"or" ("hip and knee flexion were 90°" names both joints; a following number empties the after-words).
    private static readonly Regex ClauseBreak = new(
        @"[,;.()]|\b(?:but|while|whilst|whereas|then|which|although|though|with|vs|versus|than|also|plus|yet|so)\b", Opts);
    private static readonly Regex TrailingSide = new(@"^\s*\(\s*(?<s>right|left|rt|lt|r|l)\s*\)", Opts);

    private static readonly char[] LabelDelimiters = [',', ';', '(', ')', '[', ']'];
    private static readonly char[] Digits = ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9'];

    // Words that carry no clinical identity: function words, months, units, and the neutral verbs a letter uses to
    // report a value. They are ignored in labels and never need to appear in the notes. Negation and direction words
    // (not, without, increased, decreased) are deliberately NOT here: they change the meaning.
    private static readonly HashSet<string> Neutral = new(
        ("the and for are was were has had have with from that this which who whom but also been being then than into onto while "
        + "her his she him he we us me my they them their there here its our per via does did doing re "
        + "of in on at to is by as or an so if it be "
        + "limited reduced restricted measured recorded noted documented reported found showed shows revealed demonstrated observed "
        + "assessed assessment examination exam range motion movement rom reading level value values result results "
        + "aged dear patient patients mrs miss mr ms dr doctor presented presents presenting stated states "
        + "mentioned regarding "
        + "deg degc mmhg bpm kg cm mm mg mcg ml iu units unit").Split(' '),
        StringComparer.Ordinal);

    // The vocabulary of an absence claim itself: it may appear in the message without being a fact the notes must carry.
    // Compared by stem, so "mentioned", "states" and "gives" are covered by "mention", "state" and "give".
    private static readonly HashSet<string> CueStems = new(
        ("not never nowhere anywhere case notes note appear invented invent fabricated fabricate record documented document "
        + "present found listed list included include given give supported support traceable evidence basis source detail details "
        + "fact value figure claim letter candidate date dates birth also actually really seem unsupported untraceable "
        + "absent missing made mention state contain show provide specify report do no up nor any").Split(' ').Select(Stem),
        StringComparer.Ordinal);

    // Whose value it is: a relative's weight, blood pressure or date of birth is not the patient's.
    private static readonly HashSet<string> RelationStems = new(
        ("mother father husband wife son daughter partner spouse sister brother parent child sibling grandmother grandfather")
            .Split(' ').Select(Stem),
        StringComparer.Ordinal);

    // A heading above a value ("Knee:" over "Active flexion 90°") names the joint, so it must agree with the letter.
    private static readonly HashSet<string> AnatomyStems = new(
        ("knee hip shoulder elbow wrist ankle neck spine back foot hand finger thumb toe cervical lumbar thoracic jaw forearm "
        + "thigh leg arm heel hamstring quadricep calf shin groin pelvis").Split(' ').Select(Stem),
        StringComparer.Ordinal);

    /// <summary>
    /// True when <paramref name="message"/> says a fact is invented or absent from the case notes AND the strict proof
    /// holds (see the class summary). <paramref name="evidence"/> is the matching source line, for admin notes and the
    /// reviewer prompt only.
    /// </summary>
    internal static bool IsFalseAbsenceClaim(
        string? quote,
        string? message,
        string? letter,
        string? caseNotes,
        string? task,
        out string evidence)
    {
        evidence = string.Empty;
        try
        {
            // A message about an age or a duration ("63 years old", "for 3 years") is never a claim this class proves.
            if (!AllegesAbsence(message) || InterpretationCue.IsMatch(message!) || AgeWords.IsMatch(message!)) return false;
            var q = (quote ?? string.Empty).Trim();
            if (q.Length < 2 || string.IsNullOrWhiteSpace(letter)) return false;
            var sentences = LetterSentences(letter!, q);
            if (sentences.Count == 0) return false;
            var source = Normalise((caseNotes ?? string.Empty) + "\n" + (task ?? string.Empty));
            if (source.Trim().Length == 0) return false;

            var lines = source.Split('\n');
            var qn = Normalise(q);
            var msg = Normalise(message);

            // An age phrase in the Re: line of a DOB claim is not part of the claim; every other age, duration or
            // number-word, and every qualifier symbol (<, ~, 90°+), keeps the finding.
            var dobClaim = DobWord.IsMatch(msg);
            var quoteChecked = dobClaim ? AgePhrase.Replace(qn, " ") : qn;
            if (AgeOrDuration.IsMatch(quoteChecked) || AgeOrDuration.IsMatch(msg)) return false;
            if (QualifierSymbol.IsMatch(quoteChecked) || QualifierSymbol.IsMatch(msg)) return false;

            // The facts the claim is about: those in the quote and those the message names. Anything that is not a date,
            // a range of movement or a vital sign (a dose, a percentage ...) is not provable here.
            var facts = FactsOf(qn);
            facts.UnionWith(FactsOf(msg));
            if (facts.Count == 0 || facts.Any(f => !IsProvable(f))) return false;
            if (HasStrayDigits(quoteChecked) || HasStrayDigits(msg)) return false;

            var sourceWords = new HashSet<string>(AlphaToken.Matches(source).Select(m => Stem(m.Value)), StringComparer.Ordinal);
            if (!WordsExplained(DateAny.Replace(quoteChecked, " "), sourceWords)) return false;

            var labels = new HashSet<string>(StringComparer.Ordinal);
            var proofs = new List<string>();
            foreach (var raw in sentences)
            {
                var sentence = Normalise(raw);
                if (QualifiedClaimedValue(sentence, facts)) return false;
                if (!facts.IsSubsetOf(FactsOf(sentence))) return false;
                var letterEvents = EventTagsOf(sentence);
                foreach (var fact in facts)
                {
                    var parts = fact.Split('|');
                    var proof = string.Empty;
                    var supported = parts[0] == "d"
                        ? DateSupported(parts[1], sentence, lines, dobClaim, letterEvents, labels, sourceWords, out proof)
                        : MeasureSupported(parts[1], parts[2], sentence, lines, letterEvents, labels, out proof);
                    if (!supported) return false;
                    if (!proofs.Contains(proof)) proofs.Add(proof);
                }
            }

            // The message may name only what the proven source label names (a side, a joint, a measurement, DOB).
            if (!MessageNamesOnly(DateAny.Replace(msg, " "), labels)) return false;

            var joined = string.Join(" | ", proofs);
            evidence = joined.Length > 200 ? joined[..200] : joined;
            return true;
        }
        catch (Exception)
        {
            evidence = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Non-suppressing evidence for the reviewer: when a finding alleges an absent fact, the source lines that carry the
    /// same date or value (doses included), WITHOUT judging attribution. Null when nothing matches.
    /// </summary>
    internal static string? ValueLookup(string? quote, string? message, string? caseNotes, string? task)
    {
        try
        {
            if (!AllegesAbsence(message)) return null;
            var source = Normalise((caseNotes ?? string.Empty) + "\n" + (task ?? string.Empty));
            if (source.Trim().Length == 0) return null;

            var facts = FactsOf(Normalise(quote));
            if (facts.Count == 0) facts = FactsOf(Normalise(message));
            if (facts.Count == 0) return null;

            var lines = source.Split('\n');
            var seen = new List<string>();
            foreach (var fact in facts)
            {
                var parts = fact.Split('|');
                for (var i = 0; i < lines.Length && seen.Count < 3; i++)
                {
                    var has = parts[0] == "d"
                        ? DateHitsOf(lines[i]).Any(h => h.Keys.Contains(parts[1]))
                        : MeasuresOf(lines[i]).Any(m => m.Number == parts[1] && m.Unit == parts[2])
                            || (parts[2] == "deg" && BareHitsOf(lines[i], parts[1]).Count > 0);
                    var text = lines[i].Trim();
                    if (has && text.Length > 0 && !seen.Contains(text)) seen.Add(text.Length > 120 ? text[..120] : text);
                }

                if (seen.Count >= 3) break;
            }

            return seen.Count == 0 ? null : string.Join(" | ", seen);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool AllegesAbsence(string? message)
        => !string.IsNullOrWhiteSpace(message) && AbsenceCue.IsMatch(message) && !OmissionCue.IsMatch(message);

    private static bool IsProvable(string fact)
        => fact.StartsWith("d|", StringComparison.Ordinal)
           || (fact.Split('|') is { Length: 3 } parts && ProvableUnits.Contains(parts[2]));

    /// <summary>Every word of the quote the notes do not explain (an unknown diagnosis, name or qualifier) keeps the finding.</summary>
    private static bool WordsExplained(string quoteNormalised, HashSet<string> sourceWords)
    {
        foreach (Match m in AlphaToken.Matches(quoteNormalised))
        {
            var word = m.Value;
            if (word.Length < 2 || word == "dob" || Neutral.Contains(word)) continue;
            if (!sourceWords.Contains(Stem(word))) return false;
        }

        return true;
    }

    /// <summary>A number that is not part of a proved date or measurement (a duration, a count, a dose) keeps the finding.</summary>
    private static bool HasStrayDigits(string text)
    {
        var s = DateAny.Replace(text, " ");
        s = NumUnit.Replace(s, " ");
        return s.IndexOfAny(Digits) >= 0;
    }

    /// <summary>The message names nothing the proven source label does not name (a diagnosis, an event, a drug keeps the finding).</summary>
    private static bool MessageNamesOnly(string messageNormalised, HashSet<string> labels)
    {
        foreach (Match m in AlphaToken.Matches(messageNormalised))
        {
            var word = m.Value;
            if (word.Length < 2 && word is not ("r" or "l")) continue;
            if (word == "dob" || Neutral.Contains(word)) continue;
            var stem = Stem(word);
            if (CueStems.Contains(stem) || labels.Contains(stem)) continue;
            return false;
        }

        return true;
    }

    private static bool DateSupported(
        string key,
        string sentence,
        string[] lines,
        bool dobClaim,
        HashSet<string> letterEvents,
        HashSet<string> labels,
        HashSet<string> sourceWords,
        out string proof)
    {
        proof = string.Empty;
        var inLetter = DateHitsOf(sentence).Where(h => h.Keys.Contains(key)).ToList();
        if (inLetter.Count == 0) return false;

        // Every place the letter states this date must be backed by a source date with the same label.
        foreach (var hit in inLetter)
        {
            var letterLabel = ClauseWords(sentence, hit.Index, hit.Length);
            if (dobClaim) letterLabel.Add("dob");
            var found = false;
            for (var i = 0; i < lines.Length && !found; i++)
            {
                foreach (var sourceHit in DateHitsOf(lines[i]).Where(h => h.Keys.Contains(key)))
                {
                    var (label, events) = SourceInfo(lines, i, sourceHit.Index, sourceHit.Length, 1);
                    if (!LabelAgrees(label, letterLabel)) continue;
                    if (!label.Contains("dob") && !events.SetEquals(letterEvents)) continue;
                    // Whose date it is: a relative's date never proves the patient's.
                    var lineWords = LineWords(lines, i);
                    if (lineWords.Overlaps(RelationStems) && !letterLabel.Overlaps(RelationStems)) continue;
                    // The words around the date must be on THIS source line (or its heading), not merely somewhere in the notes;
                    // a DOB line is checked against the whole notes (the Re: line names live on another line).
                    if (!ClauseExplained(sentence, hit.Index, hit.Length, label.Contains("dob") ? sourceWords : lineWords)) continue;
                    labels.UnionWith(label);
                    proof = lines[i].Trim();
                    found = true;
                    break;
                }
            }

            if (!found) return false;
        }

        return true;
    }

    private static bool MeasureSupported(
        string number,
        string unit,
        string sentence,
        string[] lines,
        HashSet<string> letterEvents,
        HashSet<string> labels,
        out string proof)
    {
        proof = string.Empty;
        var inLetter = MeasuresOf(sentence).Where(m => m.Number == number && m.Unit == unit).ToList();
        if (inLetter.Count == 0) return false;

        foreach (var hit in inLetter)
        {
            var letterLabel = ClauseWords(sentence, hit.Index, hit.Length);
            var found = false;
            for (var i = 0; i < lines.Length && !found; i++)
            {
                var spots = MeasuresOf(lines[i])
                    .Where(m => m.Number == number && m.Unit == unit)
                    .Select(m => (m.Index, m.Length))
                    .ToList();
                // The degree sign is the glyph extraction loses most often: accept the bare number, label permitting.
                if (unit == "deg") spots.AddRange(BareHitsOf(lines[i], number));

                foreach (var (index, length) in spots)
                {
                    // A source value that is qualified (">90°", "~90°", "-10°", "90°+") is a different fact from a plain one.
                    if (IsQualifiedInSource(lines[i], index, length)) continue;
                    var (label, events) = SourceInfo(lines, i, index, length, unit == "deg" ? 2 : 1);
                    if (!LabelAgrees(label, letterLabel) || !events.SetEquals(letterEvents)) continue;
                    // Whose value it is: a relative's weight or blood pressure never proves the patient's.
                    var lineWords = LineWords(lines, i);
                    if (lineWords.Overlaps(RelationStems) && !letterLabel.Overlaps(RelationStems)) continue;
                    // Every identity word of the letter's clause must be on THIS source line (or its heading).
                    if (!ClauseExplained(sentence, hit.Index, hit.Length, lineWords)) continue;
                    labels.UnionWith(label);
                    proof = lines[i].Trim();
                    found = true;
                    break;
                }
            }

            if (!found) return false;
        }

        return true;
    }

    /// <summary>
    /// True when a value the claim is ABOUT carries a qualifier in the letter sentence ("&lt;90°", "~90°", "-10°", "90°+",
    /// "80-90°"). A sign on ANOTHER value in the same sentence is that value's own ("flexion was 90 degrees with extension
    /// to -5 degrees": the -5 does not qualify the 90), so it no longer blocks the proof for the claimed one.
    /// </summary>
    private static bool QualifiedClaimedValue(string sentence, HashSet<string> facts)
    {
        foreach (var hit in DateHitsOf(sentence))
        {
            if (hit.Keys.Any(k => facts.Contains("d|" + k)) && IsQualifiedInLetter(sentence, hit.Index, hit.Length)) return true;
        }

        foreach (var m in MeasuresOf(sentence))
        {
            if (facts.Contains("m|" + m.Number + "|" + m.Unit) && IsQualifiedInLetter(sentence, m.Index, m.Length)) return true;
        }

        return false;
    }

    /// <summary>A qualifier directly before the value (see <see cref="IsQualifiedInSource"/>) or a plus-minus sign directly after it.</summary>
    private static bool IsQualifiedInLetter(string sentence, int index, int length)
        => IsQualifiedInSource(sentence, index, length) || sentence[(index + length)..].TrimStart().StartsWith('±');

    /// <summary>The stemmed words of a source line plus its heading line (the nearest digit-free line above, when it is
    /// a heading: it ends with a colon or is at most three words - a narrative line above is not a heading).</summary>
    private static HashSet<string> LineWords(string[] lines, int lineIndex)
    {
        var words = new HashSet<string>(AlphaToken.Matches(lines[lineIndex]).Select(m => Stem(m.Value)), StringComparer.Ordinal);
        for (var j = lineIndex - 1; j >= 0; j--)
        {
            if (lines[j].Trim().Length == 0) continue;
            if (!lines[j].Any(char.IsDigit) && IsHeading(lines[j])) words.UnionWith(AlphaToken.Matches(lines[j]).Select(m => Stem(m.Value)));
            break;
        }

        return words;
    }

    private static bool IsHeading(string line)
        => line.TrimEnd().EndsWith(':') || AlphaToken.Matches(line).Count <= 3;

    /// <summary>True when the source value has a qualifier right next to it: a comparison or approximation symbol or a sign
    /// before it, or a plus after it.</summary>
    private static bool IsQualifiedInSource(string line, int index, int length)
    {
        var before = line[..index].TrimEnd();
        if (before.Length > 0 && (before[^1] is '<' or '>' or '≤' or '≥' or '≈' or '~' or '±' or '-' or '+')) return true;
        var after = line[(index + length)..].TrimStart();
        return after.StartsWith('+');
    }

    /// <summary>The source label is fully covered by the letter's clause, the letter names no side or joint the source label
    /// does not, and a DOB claim meets a DOB label. A source value with no usable label proves nothing.</summary>
    private static bool LabelAgrees(HashSet<string> sourceLabel, HashSet<string> letterLabel)
    {
        if (sourceLabel.Count == 0 || !sourceLabel.IsSubsetOf(letterLabel)) return false;
        if (letterLabel.Contains("dob") && !sourceLabel.Contains("dob")) return false;
        if (letterLabel.Any(w => AnatomyStems.Contains(w) && !sourceLabel.Contains(w))) return false;
        return SidesOf(letterLabel).IsSubsetOf(SidesOf(sourceLabel));
    }

    private static HashSet<string> SidesOf(HashSet<string> words)
        => new(words.Where(w => w is "right" or "left"), StringComparer.Ordinal);

    /// <summary>
    /// The label and the events of a source value. Label: up to three identity words directly before it (after the last
    /// comma, bracket, number or clause word), a parenthesised side after it ("90° (R)"), and, when the line carries too
    /// few words, the heading line above ("Right knee" over "Flexion 90°"); a line with a DOB label reduces to just DOB;
    /// fewer than <paramref name="minimumWords"/> words besides a side means no label (the value proves nothing).
    /// Events: admission/discharge/active/passive ... in the words around the value or in the heading.
    /// </summary>
    private static (HashSet<string> Label, HashSet<string> Events) SourceInfo(
        string[] lines,
        int lineIndex,
        int index,
        int length,
        int minimumWords)
    {
        var line = lines[lineIndex];
        var before = line[..index];
        var cut = before.LastIndexOfAny(LabelDelimiters);
        for (var k = before.Length - 1; k > cut; k--)
        {
            if (char.IsDigit(before[k]))
            {
                cut = k;
                break;
            }
        }

        // "Vitals: Temp 37.8" - a colon that has label words after it ends the section prefix; "Knee: 90" keeps "Knee".
        // The prefix still binds the value when it names a side, a joint, a relative or an event ("R knee: flexion 90").
        var prefix = string.Empty;
        var colon = before.LastIndexOf(':');
        if (colon > cut && AlphaToken.IsMatch(before[(colon + 1)..]))
        {
            cut = colon;
            prefix = before[..colon];
        }

        var segment = cut >= 0 ? before[(cut + 1)..] : before;
        var breaks = ClauseBreak.Matches(segment);
        if (breaks.Count > 0) segment = segment[(breaks[breaks.Count - 1].Index + breaks[breaks.Count - 1].Length)..];

        var words = ContentWords(segment);
        if (prefix.Length > 0)
        {
            words.AddRange(ContentWords(prefix).Where(w => w is "right" or "left" || AnatomyStems.Contains(w) || RelationStems.Contains(w)));
        }

        var afterRaw = line[(index + length)..];
        var tailEnd = afterRaw.IndexOfAny(Digits);
        var tail = tailEnd >= 0 ? afterRaw[..tailEnd] : afterRaw;
        var side = TrailingSide.Match(afterRaw);
        if (side.Success) words.Add(Stem(side.Groups["s"].Value));
        var events = EventTagsOf(prefix + " " + segment + " " + tail);

        var heading = string.Empty;
        for (var j = lineIndex - 1; j >= 0; j--)
        {
            if (lines[j].Trim().Length == 0) continue;
            if (!lines[j].Any(char.IsDigit)) heading = lines[j];
            break;
        }

        if (heading.Length > 0)
        {
            var headingWords = ContentWords(heading);
            if (words.Count(w => w is not ("right" or "left")) < minimumWords)
            {
                words.AddRange(headingWords);
            }
            else
            {
                // A heading that names a joint or a side still binds the value beneath it ("Knee:" over "Active flexion 90°").
                words.AddRange(headingWords.Where(w => w is "right" or "left" || AnatomyStems.Contains(w) || RelationStems.Contains(w)));
            }
        }

        events.UnionWith(EventTagsOf(heading));

        var label = new HashSet<string>(words, StringComparer.Ordinal);
        // A DOB line reduces to DOB (the names on it are checked against the Re: line elsewhere), but never loses whose DOB it is.
        if (label.Contains("dob"))
        {
            label = new HashSet<string>(label.Where(w => w == "dob" || RelationStems.Contains(w)), StringComparer.Ordinal);
        }

        if (label.Count(w => w is not ("right" or "left")) < minimumWords) label.Clear();
        return (label, events);
    }

    /// <summary>The last three identity words of a segment plus every side word in it (a long label never loses its side).</summary>
    private static List<string> ContentWords(string segment)
    {
        var all = AlphaToken.Matches(segment)
            .Select(m => m.Value)
            .Where(w => !Neutral.Contains(w) && (w.Length > 1 || w is "r" or "l"))
            .Select(Stem)
            .ToList();
        var rest = all.Where(w => w is not ("right" or "left")).ToList();
        var words = rest.Skip(Math.Max(0, rest.Count - 3)).ToList();
        words.AddRange(all.Where(w => w is "right" or "left").Distinct());
        return words;
    }

    private static HashSet<string> EventTagsOf(string text)
        => new(Events.Where(e => e.Pattern.IsMatch(text)).Select(e => e.Tag), StringComparer.Ordinal);

    /// <summary>The (stemmed) words of the letter's clause around a value.</summary>
    private static HashSet<string> ClauseWords(string text, int index, int length)
        => new(AlphaToken.Matches(ClauseText(text, index, length)).Select(m => Stem(m.Value)), StringComparer.Ordinal);

    /// <summary>
    /// The letter's clause around a value: from the nearest clause break (or the previous number) to the nearest clause
    /// break after it. Words after the value are dropped altogether when another number follows in the clause, because
    /// they then label that next value ("Hip flexion was 90° knee flexion 120°").
    /// </summary>
    private static string ClauseText(string text, int index, int length) => ClauseParts(text, index, length).Text;

    /// <summary>The clause text, and the part of the clause that was set aside because a number sits between it and the
    /// value (it labels that other number, but a stray number in it must still be checked).</summary>
    private static (string Text, string Dropped) ClauseParts(string text, int index, int length)
    {
        var before = text[..index];
        var breaks = ClauseBreak.Matches(before);
        var clauseStart = breaks.Count > 0 ? breaks[breaks.Count - 1].Index + breaks[breaks.Count - 1].Length : 0;
        var start = clauseStart;
        var lastDigit = before.LastIndexOfAny(Digits);
        if (lastDigit >= start) start = lastDigit + 1;
        var after = text[(index + length)..];
        var next = ClauseBreak.Match(after);
        var end = next.Success ? next.Index : after.Length;
        var following = after[..end];
        var dropped = before[clauseStart..start];
        if (following.IndexOfAny(Digits) >= 0)
        {
            dropped += " " + following;
            following = string.Empty;
        }

        return (before[start..] + " " + following, dropped);
    }

    /// <summary>Every identity word in the letter's clause around a value is explained by the notes (a "target", "marked" or
    /// "estimated" qualifier the notes never state keeps the finding), and no unexplained number sits in that clause
    /// ("for 3 years").</summary>
    private static bool ClauseExplained(string text, int index, int length, HashSet<string> sourceWords)
    {
        var (clause, dropped) = ClauseParts(text, index, length);
        if (HasStrayDigits(AgePhrase.Replace(dropped, " "))) return false;
        foreach (Match m in AlphaToken.Matches(clause))
        {
            var word = m.Value;
            if ((word.Length < 2 && word is not ("r" or "l")) || word == "dob" || Neutral.Contains(word)) continue;
            if (!sourceWords.Contains(Stem(word))) return false;
        }

        return true;
    }

    /// <summary>One form per word: r/l = right/left, flexion/flexed = flex, extension = ext, plural and -ed/-ing dropped.
    /// Exact otherwise, so two different drugs or diagnoses never compare equal.</summary>
    private static string Stem(string word)
    {
        var w = word.ToLowerInvariant();
        if (w is "r" or "rt") return "right";
        if (w is "l" or "lt") return "left";
        if (w.StartsWith("flex", StringComparison.Ordinal)) return "flex";
        if (w == "ext" || w.StartsWith("exten", StringComparison.Ordinal)) return "ext";
        // Inline (no static table): Stem runs inside static field initialisers, so it must not depend on field order.
        if (w.Length > 6 && w.EndsWith("ing", StringComparison.Ordinal)) return w[..^3];
        if (w.Length > 5 && (w.EndsWith("ed", StringComparison.Ordinal) || w.EndsWith("es", StringComparison.Ordinal))) return w[..^2];
        if (w.Length > 4 && w.EndsWith('s')) return w[..^1];
        return w;
    }

    /// <summary>"d|day/month/yy" keys for dates and "m|number|unit" keys for unit-bearing measurements.</summary>
    private static HashSet<string> FactsOf(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hit in DateHitsOf(text))
        {
            foreach (var key in hit.Keys) set.Add("d|" + key);
        }

        foreach (var m in MeasuresOf(text)) set.Add("m|" + m.Number + "|" + m.Unit);
        return set;
    }

    private static List<(int Index, int Length, HashSet<string> Keys)> DateHitsOf(string text)
    {
        var list = new List<(int Index, int Length, HashSet<string> Keys)>();
        foreach (Match m in DateAny.Matches(text))
        {
            var keys = WritingRuleEngine.SourceDateKeys(m.Value);
            if (keys.Count > 0) list.Add((m.Index, m.Length, keys));
        }

        return list;
    }

    private static List<(string Number, string Unit, int Index, int Length)> MeasuresOf(string text)
    {
        var list = new List<(string Number, string Unit, int Index, int Length)>();
        foreach (Match m in NumUnit.Matches(text))
        {
            var unit = m.Groups["u"].Value.ToLowerInvariant();
            list.Add((CanonNumber(m.Groups["n"].Value), unit.StartsWith("unit", StringComparison.Ordinal) ? "units" : unit, m.Index, m.Length));
        }

        return list;
    }

    /// <summary>Bare occurrences of a number (no unit glyph) in a line, skipping numbers that carry another unit.</summary>
    private static List<(int Index, int Length)> BareHitsOf(string line, string number)
    {
        var list = new List<(int Index, int Length)>();
        foreach (Match m in BareNumber.Matches(line))
        {
            if (CanonNumber(m.Groups["n"].Value) != number) continue;
            var withUnit = NumUnit.Match(line[m.Index..]);
            if (withUnit.Success && withUnit.Index == 0 && !string.Equals(withUnit.Groups["u"].Value, "deg", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add((m.Index, m.Length));
        }

        return list;
    }

    private static string CanonNumber(string raw)
    {
        var n = raw.Replace(" ", string.Empty).Replace(',', '.');
        if (n.Contains('/') || !n.Contains('.')) return n;
        return n.TrimEnd('0').TrimEnd('.');
    }

    /// <summary>
    /// Lower-cased, one spelling per fact: degree sign/word/OCR LaTeX = "deg" ("degc" for Celsius), D.O.B./date of birth
    /// = "dob", "28-Feb-1968" and "28/FEB/1968" = "28 feb 1968", vital signs in one form, dashes and OCR markdown
    /// stripped. Newlines are kept.
    /// </summary>
    private static string Normalise(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var s = text.Replace('º', '°').Replace('˚', '°').Replace('‐', '-').Replace('‑', '-')
            .Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        s = HorizontalSpace.Replace(s, " ");
        s = s.Replace('–', '-').Replace('—', '-').Replace('−', '-');
        s = MmHg.Replace(s, "mmhg");
        s = LatexDegree.Replace(s, "deg");
        s = s.Replace('$', ' ').Replace('{', ' ').Replace('}', ' ').Replace('^', ' ').Replace('|', ' ').Replace('*', ' ').Replace('#', ' ');
        s = DegreeCelsiusSign.Replace(s, "degc");
        s = DegreeCelsiusWord.Replace(s, "degc");
        s = s.Replace("°", "deg");
        s = DegreeWord.Replace(s, "deg");
        s = DobToken.Replace(s, "dob");
        s = GluedDate.Replace(s, "${d} ${m} ${y}");
        foreach (var (pattern, replacement) in Rewrites) s = pattern.Replace(s, replacement);
        return s;
    }

    private static Regex Rx(string pattern) => new(pattern, Opts);

    /// <summary>Every sentence of the letter that holds the quote (the proof must hold for all of them); empty when the
    /// quote is not letter wording or occurs too often to be tied to one place.</summary>
    private static List<string> LetterSentences(string letter, string quote)
    {
        var result = new List<string>();
        var text = letter;
        var wanted = quote;
        if (text.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) < 0)
        {
            text = Regex.Replace(letter, @"\s+", " ");
            wanted = Regex.Replace(quote, @"\s+", " ");
            if (text.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) < 0) return result;
        }

        var at = text.IndexOf(wanted, StringComparison.OrdinalIgnoreCase);
        var occurrences = 0;
        while (at >= 0)
        {
            if (++occurrences > 6) return new List<string>();
            var start = at;
            while (start > 0 && !IsSentenceEnd(text, start - 1)) start--;
            var end = at + wanted.Length;
            while (end < text.Length && !IsSentenceEnd(text, end)) end++;
            var sentence = text[start..end];
            if (!result.Contains(sentence)) result.Add(sentence);
            at = text.IndexOf(wanted, at + Math.Max(1, wanted.Length), StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static bool IsSentenceEnd(string text, int index)
    {
        var c = text[index];
        return c is '\n' or '?' or '!' or ';'
            || (c == '.' && (index + 1 >= text.Length || char.IsWhiteSpace(text[index + 1])));
    }
}
