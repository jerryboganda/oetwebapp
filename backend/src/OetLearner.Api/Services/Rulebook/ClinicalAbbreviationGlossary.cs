using System.Text;
using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// The ONE clinical reference glossary for medication abbreviations (owner directive, 9 Oct 2026),
/// shared by the Model Answer generator and validator, the candidate grader and the reviewer.
/// <list type="bullet">
/// <item>Prompts: <see cref="PromptSection"/> is appended once, in AiGatewayService, to every Writing
/// system prompt, so no assessor relies on conversational memory for what QD or QID means.</item>
/// <item>Validator: <see cref="FindFrequencies"/> and <see cref="AreCompatible"/> power
/// <c>medication_frequency_source_mismatch</c> (WritingRuleEngine.MedicationFrequency.cs), and
/// <see cref="ExpansionExtras"/> feeds <c>latin_abbreviations_translated</c>.</item>
/// </list>
/// Two tokens are never fuzzy-matched to each other: QD, QID, QDS, QOD and OD are exact, case, dots and
/// spacing aside. Q4H/Q6H/Q8H stay "every N hours" and are NOT equal to QDS ("four times daily").
/// </summary>
public static class ClinicalAbbreviationGlossary
{
    public sealed record Entry(string Group, string Abbreviations, string Latin, string English, string? Note = null);

    /// <summary>Owner list, 9 Oct 2026. Reference data for prompts and documentation.</summary>
    public static readonly IReadOnlyList<Entry> Entries =
    [
        new("Frequency", "QD / q.d.", "quaque die", "once daily", "never QID (four times daily) or QOD (every other day)"),
        new("Frequency", "QID / q.i.d.", "quater in die", "four times daily"),
        new("Frequency", "QOD / q.o.d.", "quaque altera die", "every other day"),
        new("Frequency", "OD / o.d.", "omni die", "once daily", "OR right eye (oculus dexter): decide from the clinical context"),
        new("Frequency", "BD / BID", "bis die / bis in die", "twice daily"),
        new("Frequency", "TDS / TID", "ter die sumendum / ter in die", "three times daily"),
        new("Frequency", "QDS", "quater die sumendum", "four times daily"),
        new("Frequency", "OM / mane", "", "every morning"),
        new("Frequency", "nocte", "", "every night"),
        new("Frequency", "HS / qHS", "hora somni", "at bedtime"),
        new("Frequency", "PRN", "pro re nata", "as needed"),
        new("Frequency", "STAT", "statim", "immediately"),
        new("Frequency", "SOS", "si opus sit", "if necessary"),
        new("Frequency", "Q4H / Q6H / Q8H", "", "every four / six / eight hours"),
        new("Administration", "AC", "ante cibum", "before meals"),
        new("Administration", "PC", "post cibum", "after meals", "PC may also mean presenting complaint"),
        new("Administration", "PO", "per os", "orally"),
        new("Administration", "SL", "", "sublingually"),
        new("Administration", "IV", "", "intravenously"),
        new("Administration", "IM", "", "intramuscularly"),
        new("Administration", "SC", "", "subcutaneously"),
    ];

    // ---------------------------------------------------------------------
    // Frequency keys: the meaning of a frequency, independent of how it was written.
    // ---------------------------------------------------------------------

    public const string OnceDaily = "1/d";
    public const string TwiceDaily = "2/d";
    public const string ThreeTimesDaily = "3/d";
    public const string FourTimesDaily = "4/d";
    public const string EveryOtherDay = "otherday";
    public const string EveryMorning = "am";
    public const string AtNight = "pm";
    public const string AtBedtime = "hs";
    public const string AsNeeded = "prn";
    public const string Immediately = "stat";
    public const string Weekly = "weekly";

    /// <summary>A frequency found in free text. <c>Key</c> is the meaning, <c>Raw</c> the words as written.</summary>
    public sealed record FrequencyMention(string Key, string Raw, int Index, int Length, bool Abbreviation);

    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // A token ends at a non-letter and is not the start of a longer dotted token ("q.d" inside "q.d.s").
    private static Regex Abbr(string core)
        => new(@"(?<![A-Za-z])" + core + @"(?![A-Za-z])(?!\.[A-Za-z])", Opts);

    // A phrase: words separated by spaces or hyphens ("twice daily", "twice-daily").
    private static string P(string pattern) => pattern.Replace(" ", @"[\s\-]+");

    private static readonly (string Token, string Key, Regex Re)[] AbbreviationSpecs =
    [
        ("qid", FourTimesDaily, Abbr(@"q\.?i\.?d")),
        ("qds", FourTimesDaily, Abbr(@"q\.?d\.?s")),
        ("qod", EveryOtherDay, Abbr(@"q\.?o\.?d")),
        ("qhs", AtBedtime, Abbr(@"q\.?h\.?s")),
        ("qd", OnceDaily, Abbr(@"q\.?d")),
        ("od", OnceDaily, Abbr(@"o\.?d")),
        ("bd", TwiceDaily, Abbr(@"b\.?(?:i\.?)?d")),
        ("tds", ThreeTimesDaily, Abbr(@"t\.?(?:i\.?d|d\.?s)")),
        ("om", EveryMorning, Abbr(@"(?:o\.?m|mane)")),
        ("nocte", AtNight, new Regex(@"(?<![A-Za-z])noct(?:e)?(?![A-Za-z])", Opts)),
        ("hs", AtBedtime, Abbr(@"h\.?s")),
        ("prn", AsNeeded, Abbr(@"(?:p\.?r\.?n|s\.?o\.?s)")),
        ("stat", Immediately, new Regex(@"(?<![A-Za-z])stat(?![A-Za-z])", Opts)),
    ];

    private static readonly (string Key, Regex Re)[] EnglishSpecs =
    [
        (FourTimesDaily, new Regex(
            P("four times (?:a|per|each) day") + "|" + P("four times daily") + "|4\\s*(?:x|times)\\s*(?:/|a |per )?\\s*day|"
            + P("quater in die") + "|" + P("quater die sumendum"), Opts)),
        (ThreeTimesDaily, new Regex(
            P("three times (?:a|per|each) day") + "|" + P("three times daily") + "|" + P("thrice daily")
            + "|3\\s*(?:x|times)\\s*(?:/|a |per )?\\s*day|" + P("ter in die") + "|" + P("ter die sumendum"), Opts)),
        (TwiceDaily, new Regex(
            P("twice (?:a|per|each) day") + "|" + P("twice daily") + "|" + P("two times (?:a|per) day")
            + "|2\\s*(?:x|times)\\s*(?:/|a |per )?\\s*day|" + P("bis in die") + "|" + P("bis die"), Opts)),
        (EveryOtherDay, new Regex(
            P("every other day") + "|" + P("on alternate days") + "|" + P("alternate days")
            + "|" + P("every second day") + "|" + P("quaque altera die"), Opts)),
        (Weekly, new Regex(
            P("once (?:a|per) week") + "|" + P("once weekly") + "|weekly|" + P("every week"), Opts)),
        (OnceDaily, new Regex(
            P("once (?:a|per|each|every) day") + "|" + P("once daily") + "|" + P("one time (?:a|per) day")
            + "|" + P("(?:every|each) day") + "|(?<![A-Za-z])daily(?![A-Za-z])|" + P("quaque die"), Opts)),
        (EveryMorning, new Regex(P("(?:every|each|in the) morning"), Opts)),
        (AtNight, new Regex(
            P("(?:every|each) (?:night|evening)") + "|" + P("at night") + "|nightly|" + P("(?:in|of) the evening"), Opts)),
        (AtBedtime, new Regex(P("at bedtime") + "|" + P("before bed") + "|" + P("hora somni"), Opts)),
        (AsNeeded, new Regex(
            P("as (?:needed|required)") + "|" + P("when (?:required|needed|necessary)") + "|" + P("if (?:required|needed|necessary)")
            + "|" + P("pro re nata") + "|" + P("si opus sit"), Opts)),
        (Immediately, new Regex("immediately|statim", Opts)),
    ];

    // "q6h", "every six hours", "four-hourly": an interval, kept distinct from "four times daily".
    private static readonly Regex HoursRe = new(
        @"(?<![A-Za-z])q\.?\s?(?<n>\d{1,2})\s?h(?:rs?)?(?![A-Za-z])"
        + @"|every[\s\-]+(?<n>\d{1,2}|four|six|eight|twelve)[\s\-]+hours?"
        + @"|(?<![A-Za-z\d])(?<n>\d{1,2}|four|six|eight|twelve)[\s\-]hourly", Opts);

    // "1500 mg daily divided every 8 hours": "daily" is the total daily dose, not the dosing frequency.
    private static readonly Regex TotalDailyDoseRe = new(@"^\s*,?\s*(?:in\s+)?(?:divided|split)", Opts);

    // "weaned by 5 mg weekly": a rate of change, not a dosing frequency.
    private static readonly Regex RateOfChangeRe = new(@"\bby\s+\d+(?:\.\d+)?\s*(?:mg|mcg|µg|g|IU|units?|mL|ml)\s*$", Opts);

    // OD is "right eye" beside an eye cue ("latanoprost 0.005% eye drops OD nocte"), never once daily there.
    private static readonly Regex EyeCueRe = new(
        @"\b(?:eyes?|ocular|ophthalm\w*|optom\w*|intra-?ocular|IOP|glaucoma|OS|OU|visual\s+acuity|spectacles?|intravitreal|latanoprost|timolol|brimonidine)\b", Opts);

    public static bool IsEyeContext(string text, int index)
    {
        var from = Math.Max(0, index - 80);
        var to = Math.Min(text.Length, index + 80);
        return EyeCueRe.IsMatch(text.Substring(from, to - from));
    }

    private static int HourCount(string word) => word.ToLowerInvariant() switch
    {
        "four" => 4,
        "six" => 6,
        "eight" => 8,
        "twelve" => 12,
        var digits => int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>Every frequency in <paramref name="text"/>, leftmost first, longest match wins ("three times daily" is not "daily").</summary>
    public static IReadOnlyList<FrequencyMention> FindFrequencies(string text)
    {
        var all = new List<FrequencyMention>();
        foreach (var (token, key, re) in AbbreviationSpecs)
        {
            foreach (Match m in re.Matches(text))
            {
                if (token == "od" && IsEyeContext(text, m.Index)) continue;
                all.Add(new FrequencyMention(key, m.Value, m.Index, m.Length, true));
            }
        }
        foreach (var (key, re) in EnglishSpecs)
        {
            foreach (Match m in re.Matches(text))
            {
                var end = m.Index + m.Length;
                var before = Math.Max(0, m.Index - 25);
                if (RateOfChangeRe.IsMatch(text.Substring(before, m.Index - before))) continue;
                if (key == OnceDaily && TotalDailyDoseRe.IsMatch(text.Substring(end, Math.Min(25, text.Length - end)))) continue;
                all.Add(new FrequencyMention(key, m.Value, m.Index, m.Length, false));
            }
        }
        foreach (Match m in HoursRe.Matches(text))
            all.Add(new FrequencyMention("q" + HourCount(m.Groups["n"].Value) + "h", m.Value, m.Index, m.Length, false));

        var ordered = all.OrderBy(f => f.Index).ThenByDescending(f => f.Length).ToList();
        var result = new List<FrequencyMention>();
        var covered = -1;
        foreach (var f in ordered)
        {
            if (f.Index < covered) continue;
            result.Add(f);
            covered = f.Index + f.Length;
        }
        return result;
    }

    /// <summary>
    /// True when a letter's frequency is the same clinical instruction as a note's frequency. Once daily,
    /// every morning, at night and at bedtime are one family (a slot may be added or dropped), but morning
    /// versus night conflict. Every other key must match exactly: QD is never QID, q6h is never QDS.
    /// </summary>
    public static bool AreCompatible(string letterKey, string noteKey)
    {
        if (letterKey == noteKey) return true;
        string[] family = [OnceDaily, EveryMorning, AtNight, AtBedtime];
        if (!family.Contains(letterKey) || !family.Contains(noteKey)) return false;
        static bool Night(string k) => k == AtNight || k == AtBedtime;
        return !((letterKey == EveryMorning && Night(noteKey)) || (noteKey == EveryMorning && Night(letterKey)));
    }

    /// <summary>The plain-English form of a frequency key (what a Model Answer writes).</summary>
    public static string Describe(string key) => key switch
    {
        OnceDaily => "once daily",
        TwiceDaily => "twice daily",
        ThreeTimesDaily => "three times daily",
        FourTimesDaily => "four times daily",
        EveryOtherDay => "every other day",
        EveryMorning => "every morning",
        AtNight => "at night",
        AtBedtime => "at bedtime",
        AsNeeded => "as needed",
        Immediately => "immediately",
        Weekly => "once weekly",
        _ when key.StartsWith('q') && key.EndsWith('h') => "every " + key[1..^1] + " hours",
        _ => key,
    };

    /// <summary>
    /// Abbreviations a Model Answer must write in full that the built-in Latin map and the legacy
    /// rulebook maps did not list. QD and QOD are the ones medication-safety guidance forbids
    /// (QD is misread as QID). "hs" is deliberately absent: it also means heart sounds.
    /// </summary>
    public static readonly IReadOnlyList<(string Token, string English)> ExpansionExtras =
    [
        ("qd", "once daily"),
        ("q.d", "once daily"),
        ("o.d", "once daily"),
        ("qod", "every other day"),
        ("q.o.d", "every other day"),
        ("qhs", "at bedtime"),
        ("q.h.s", "at bedtime"),
        ("sos", "if necessary"),
        ("s.o.s", "if necessary"),
    ];

    // ---------------------------------------------------------------------
    // Prompt block
    // ---------------------------------------------------------------------

    /// <summary>
    /// The glossary and the source-truth rules, for the system prompt of every Writing AI call.
    /// <paramref name="modelAnswer"/> selects the Model Answer variant (generator and validator);
    /// otherwise the candidate variant (grader and reviewer).
    /// </summary>
    public static string PromptSection(bool modelAnswer)
    {
        var sb = new StringBuilder();
        sb.AppendLine("12. Clinical abbreviations and source truth (owner directive, 9 Oct 2026) — MANDATORY for every Writing generation, validation, grading and review call:");
        sb.AppendLine("   A. Glossary. Capitalisation, dots and spacing vary (QD = Q.D. = q.d. = qd); read each token exactly as written and never swap it for a different one:");
        foreach (var group in Entries.GroupBy(e => e.Group))
        {
            sb.Append("      ").Append(group.Key).Append(": ");
            sb.AppendLine(string.Join(" | ", group.Select(e =>
                e.Abbreviations + (e.Latin.Length > 0 ? " (" + e.Latin + ")" : "") + " = " + e.English + (e.Note is null ? "" : " [" + e.Note + "]"))));
        }
        sb.AppendLine("   B. Never confuse: QD = once daily; QID = four times daily; QDS = four times daily; QOD = every other day. An interval (Q6H, every six hours) is not a count (QDS, four times daily).");
        sb.AppendLine("      OD is context-dependent: omni die (once daily) beside an oral/systemic dose, but oculus dexter (right eye) in ophthalmology or optometry or beside eye drops, OS or OU. Decide from the surrounding case notes; if the context does not decide it, say so — do not guess.");
        sb.AppendLine("   C. The ORIGINAL case notes (the scanned page or PDF the case-note list was taken from) are the primary source of truth for every medicine name, dose, frequency and route, every diagnosis and history item, symptom, finding and result, every date and timeline, every allergy or adverse reaction, whether an action is already completed or still required, and the recipient and purpose. A Model Answer, a sample answer or a candidate's wording never overrides or replaces them. When the extracted note list and the original page disagree, the original page wins and the discrepancy is REPORTED, never smoothed over.");
        sb.AppendLine("   D. An unclear, faint or poorly scanned abbreviation is never guessed. Do not read QD as QID (or the reverse) because of image quality or an OCR slip: check the original page or PDF, and if it is still unresolved say so plainly so the owner can review it.");
        sb.AppendLine("   E. Completed versus required: an imperative note (\"Notify the patient's doctor\", \"Refer\", \"Arrange\") is a PENDING action; a past-tense note (\"the patient's doctor has already been notified\") is a COMPLETED action. Never present a pending action as done, never present a done action as still to do, and never invent an action the notes do not record.");
        if (modelAnswer)
        {
            sb.AppendLine("   F. Model Answer: write every frequency in full plain English. QD and QOD must NEVER appear as abbreviations (medication safety: QD is misread as QID): write \"once daily\" and \"every other day\". The frequency must be the one the case notes record (QD = once daily, never \"four times daily\"). If the notes' token is unresolved, report that in whyThisWorks instead of choosing one.");
        }
        else
        {
            sb.AppendLine("   F. Grading and review: never penalise clinically correct wording that differs from the Model Answer or from the notes' abbreviation (once daily = QD = OD; twice daily = BD = BID). A candidate who writes a frequency abbreviation is at most coached to write it in full, never marked as a wrong fact. Penalise a frequency only when its MEANING contradicts the original case notes (for example \"four times daily\" for QD). Never penalise an assumption you cannot trace to the case notes. If a candidate is right against the original page and the extracted note text is the one that looks wrong, do not penalise the candidate: report the discrepancy.");
        }
        return sb.ToString();
    }
}
