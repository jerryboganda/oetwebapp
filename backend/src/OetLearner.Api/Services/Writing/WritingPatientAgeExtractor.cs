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
    private static readonly Regex RelativeFollowsRegex =
        new($@"^[\s-]*(?:year|yr)?s?[\s-]*(?:old)?[\s,-]*(?:her|his|their|the|my)?\s*(?:[a-z]+\s+){{0,2}}(?:{RelativeNouns})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static int? Extract(string caseNotes)
    {
        var text = caseNotes ?? string.Empty;
        foreach (var match in YearsOldRegex.Matches(text).Cast<Match>())
            if (TryPatientAge(text, match, out var age)) return age;
        foreach (var match in AgeLabelRegex.Matches(text).Cast<Match>())
            if (TryPatientAge(text, match, out var age)) return age;
        return null;
    }

    private static bool TryPatientAge(string text, Match match, out int age)
    {
        age = 0;
        if (!int.TryParse(match.Groups[1].Value, out var value) || value is not (> 0 and < 120)) return false;
        var boundary = text.LastIndexOfAny(['.', '!', '?', '\n'], Math.Max(0, match.Index - 1));
        var segment = text[(boundary + 1)..match.Index];
        if (RelativeLedRegex.IsMatch(segment)) return false;
        // The digits alone are captured, so look forward from the end of the whole matched age
        // expression: "80-year-old husband" and "80 years old wife" both hand the age to them.
        var after = match.Index + match.Length;
        var tail = text[after..Math.Min(text.Length, after + 60)];
        if (RelativeFollowsRegex.IsMatch(tail)) return false;
        age = value;
        return true;
    }
}
