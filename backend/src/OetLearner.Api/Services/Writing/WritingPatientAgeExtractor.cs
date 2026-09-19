using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Extracts the PATIENT's own age from case notes for the Writing rules.
/// Only an age attributable to the patient may feed PatientIsMinor: the live
/// Weir notes ("He is married with 3 children aged 13, 10 and 8") made the
/// old first-match "aged N" scan parse the eldest child's age, class an
/// adult patient as a minor, and fire minor_naming_convention against a
/// correctly-titled adult Re: line — mutually unsatisfiable with the adult
/// naming rule (Rev8 §7.1). Ages inside a list ("aged 13, 10 and 8") or
/// governed by a relative word ("His daughter, aged 8") are never the
/// patient's. No attributable age returns null, which the rule engine reads
/// as adult — the safe direction.
/// </summary>
internal static class WritingPatientAgeExtractor
{
    private static readonly Regex YearsOldRegex =
        new(@"\b(\d{1,3})[\s-]*(?:years?|yrs?)[\s-]*old\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // The (?!...) lookahead rejects ages that continue into a list:
    // "aged 13, 10 and 8" can never be a single patient's age.
    // The (?<!at ...) lookbehinds reject historical past-event references
    // ("appendectomy at age 15", "at the age of 40"): the patient's age AT a
    // past event is not the patient's age now. The leading lookbehind is the
    // load-bearing one — the age label itself sits right after "at " in both
    // phrasings. Without these, adult patients with childhood-event history
    // were classed as minors and minor_naming_convention fired against their
    // correctly-titled Re: line (Sandra Marcus round, 16 Sep 2026).
    private static readonly Regex AgeLabelRegex =
        new(@"(?<!\bat\s+)\b(?:age|aged)\s*:?\s*(\d{1,3})\b(?<!\bat\s+(?:the\s+)?\d)(?!\s*,?(?:\s*(?:and|or)\s*)?\s*\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A relative noun between the start of the current sentence and the age
    // attributes the age to the relative, not the patient. Parents and other
    // relatives are included: "Mother died aged 72" is the mother's age at
    // death, never the patient's (family-history notes are common in the
    // held-letter corpus).
    private const string RelativeNouns =
        @"children|child|sons?|daughters?|wife|husband|partner|brothers?|sisters?|twins?|grandsons?|granddaughters?|grandchildren|grandchild|baby|infant|nephews?|nieces?|mother|father|mum|mom|dad|parents?|aunt|uncle|cousins?|grandmother|grandfather|grandparents?";

    private static readonly Regex RelativeLedRegex =
        new($@"\b(?:{RelativeNouns})\b[^.!?\n]*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Cross-profession repair (18 Sep 2026): the look-back only caught a relative BEFORE the age
    // ("His daughter, aged 8"). In the adjectival form the relative comes AFTER it — Mrs Jane
    // LaPaglia's notes read "Lives with her 80-year-old husband/carer, Joe", so the husband's 80
    // was taken as the patient's age and age_dob_inconsistent fired Critical on her Re: line
    // stating the age her own note and the source PDF give (71). The letter's only passing options
    // were to state a false age or omit it. A relative noun straight after the age owns it.
    // Only the age's own noun phrase counts, so no comma may be crossed and at most one adjective
    // may intervene: "80-year-old husband" and "78 year old wife" are the relative's, while
    // "Mr X, aged 45, has two sons aged 4 and 7" keeps 45 for the patient.
    private static readonly Regex RelativeFollowsRegex =
        new($@"^[\s-]+(?:her|his|their|the|my)?\s*(?:[a-z]+\s+)?(?:{RelativeNouns})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Cross-profession repair (18 Sep 2026): case notes routinely state the age in apposition —
    // "Mr Martin Wilson, 62, was admitted ..." — which neither pattern above reads. The Re: line
    // then could not carry the age the notes DO record: "Re: Mr Martin Wilson, aged 62" was
    // rejected as unsupported by re_line_identity_unsupported. A title or a two-part capitalised
    // name must introduce it, and the number must close the apposition, so a house number or a
    // measurement cannot be read as an age.
    private static readonly Regex AppositionAgeRegex =
        new(@"\b(?:(?:Mr|Mrs|Ms|Miss|Mx|Dr|Prof)\.?\s+[A-Z][A-Za-z'’\-]+(?:\s+[A-Z][A-Za-z'’\-]+){0,2}"
            + @"|[A-Z][A-Za-z'’\-]+\s+(?<last>[A-Z][A-Za-z'’\-]+))\s*,\s*(\d{1,3})\s*(?=[,;)]|\s+(?:years?|yrs?)\b)",
            RegexOptions.Compiled);

    // An address reads exactly like a name in apposition — "Lives at Oakfield Drive, 19,
    // Birmingham." — so a place word may not end the name that owns the number.
    private static readonly HashSet<string> PlaceWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Drive", "Street", "St", "Road", "Rd", "Lane", "Avenue", "Ave", "Court", "Crescent", "Close",
        "Parade", "Terrace", "Place", "Way", "Boulevard", "Highway", "Esplanade", "Square", "Park",
        "Home", "Hospital", "Clinic", "Centre", "Center", "Practice", "Ward", "Unit", "Suite", "Floor",
        "House", "Village", "Estate", "Gardens", "Grove", "Rise", "View", "Hill",
    };

    // Owner decision (19 Sep 2026): an age written out - "Jonathon Apple is a ten-year-old boy" - was
    // not read at all (the digits-only pattern above), so a child was classed as an adult and the
    // validator demanded "Mr" for him in the same run that told the writer to use his first name.
    // Only one to nineteen are read: those are the ages the minor rule turns on, and an adult's
    // spelled-out age changes no verdict. The relative guards below still apply, so "her
    // eighty-year-old husband" is never the patient's.
    private static readonly Dictionary<string, int> AgeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
        ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13,
        ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18,
        ["nineteen"] = 19,
    };

    private static readonly Regex WordYearsOldRegex =
        new(@"\b(one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen)[\s-]*(?:years?|yrs?)[\s-]*old\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Owner decision (19 Sep 2026): an OBJECT has an age too. Optometry's Mr Arthur Reed (DOB 12
    // January 1949) was read as a 4-year-old from "Wears bifocal spectacles; current pair approximately
    // 4 years old", which raised age_dob_inconsistent and two minor-naming findings against a
    // 77-year-old. Same class as the relative and place guards: the age belongs to the noun it
    // describes. Only the noun phrase counts - no comma may be crossed - so "Wears glasses, aged 45"
    // keeps 45 for the patient.
    private const string ObjectNouns =
        @"spectacles?|glasses|lenses|contact lenses|pair|frames?|dentures?|prosthesis|prostheses|implants?|devices?|hearing aids?|appliances?|orthos[ie]s|wheelchairs?|catheters?|stomas?|grafts?|scars?|pacemakers?";

    // The second alternative admits the relative-clause form ("a wheelchair, which is 6 years old"),
    // where a comma separates the object from its age but "which/that" still ties them together.
    private static readonly Regex ObjectLedRegex =
        new($@"\b(?:{ObjectNouns})\b(?:[^,;.!?\n]{{0,30}}|,\s*(?:which|that)\b[^,;.!?\n]{{0,30}})$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static int? Extract(string caseNotes)
    {
        var text = caseNotes ?? string.Empty;
        foreach (var match in YearsOldRegex.Matches(text).Cast<Match>())
            if (TryPatientAge(text, match, out var age)) return age;
        foreach (var match in WordYearsOldRegex.Matches(text).Cast<Match>())
            if (TryPatientAge(text, match, out var age)) return age;
        foreach (var match in AgeLabelRegex.Matches(text).Cast<Match>())
            if (TryPatientAge(text, match, out var age)) return age;
        foreach (var match in AppositionAgeRegex.Matches(text).Cast<Match>())
        {
            if (PlaceWords.Contains(match.Groups["last"].Value)) continue;
            if (TryPatientAge(text, match, out var age)) return age;
        }
        return null;
    }

    private static bool TryPatientAge(string text, Match match, out int age)
    {
        age = 0;
        var raw = match.Groups[1].Value;
        if (!int.TryParse(raw, out var value) && !AgeWords.TryGetValue(raw, out value)) return false;
        if (value is not (> 0 and < 120)) return false;
        // ';' ends the clause too: "Wears bifocal spectacles; current pair ... 4 years old" is two
        // statements, and the second one is about the spectacles.
        var boundary = text.LastIndexOfAny(['.', '!', '?', '\n', ';'], Math.Max(0, match.Index - 1));
        var segment = text[(boundary + 1)..match.Index];
        if (RelativeLedRegex.IsMatch(segment)) return false;
        if (ObjectLedRegex.IsMatch(segment)) return false;
        // The digits alone are captured, so look forward from the end of the whole matched age
        // expression: "80-year-old husband" and "80 years old wife" both hand the age to them.
        var after = match.Index + match.Length;
        var tail = text[after..Math.Min(text.Length, after + 60)];
        if (RelativeFollowsRegex.IsMatch(tail)) return false;
        age = value;
        return true;
    }
}
