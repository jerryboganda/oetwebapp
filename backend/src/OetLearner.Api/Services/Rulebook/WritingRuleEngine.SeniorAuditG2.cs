using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Owner Senior Assessor Release Audit (16 Sep 2026), group G2: language-surface
/// defect classes the validator reported as clean. Every detector here is
/// MODEL ANSWER ONLY (implementation decisions §B): candidate lanes never see
/// these findings, so the parity snapshots and the candidate false-positive
/// firewall stay unchanged.
/// <list type="bullet">
/// <item>New check ids: malformed_today_phrase, missing_possessive_name,
/// typographic_corruption.</item>
/// <item>Extensions composed by the integrator under existing check ids
/// (intro_adverbial_comma, number_style_words_vs_digits, value_unit_spacing,
/// numerical_values_have_units, conditions_lowercase). Each extension emits
/// ONLY the shapes its existing detector never reports, so composing the two
/// never double-reports one defect.</item>
/// </list>
/// Precision over recall: correct clinical English must never fire.
/// </summary>
public sealed partial class WritingRuleEngine
{
    // ---------------------------------------------------------------------
    // G2-a — malformed "today" phrases (audit §3.2; decisions §C.20):
    // "presented on today", "At review on today", "by her last period of
    // today", "By today, attacks had become ...". The repair tooling produced
    // these by swapping a body date for "today" and leaving the preposition.
    // "on/at today's review", "as of today", "from today" stay valid.
    // ---------------------------------------------------------------------

    private static readonly Regex SaG2MalformedTodayRe = new(
        @"(?<!\b(?:later|carry|carried|carries|carrying)\s)\b(?:on|at|since)\s+today\b(?!['’])" +
        @"|\b(?:period|visit|review|presentation|admission|examination|appointment|consultation|assessment)\s+of\s+today\b(?!['’])" +
        @"|\bby\s+today\b,?\s+(?:[A-Za-z'’]+\s+){1,4}?(?<past>was|were|had)\b" +
        @"|(?:^|(?<=[.!?]\s+))(?<start>By\s+today)\b(?!['’])",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static IEnumerable<LintFinding> DetectSaG2MalformedTodayPhrase(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in SaG2MalformedTodayRe.Matches(s.Body))
        {
            var message = m.Groups["past"].Success
                ? "\"" + m.Value + "\" pairs \"by today\" with past-tense narration. Use the source date (\"By 4 January 2018, the attacks had become ...\") or write \"today\" alone with the correct tense."
                : m.Groups["start"].Success
                    ? "\"" + m.Value + "\" is not a Model Answer form. Write \"today\" alone or restore the real earlier source date."
                    : "\"" + m.Value + "\" is malformed: \"today\" is an adverb and takes no preposition. Write \"presented today\" or \"at today's review\"; if the event happened at an EARLIER visit, restore the source date instead of \"today\".";
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major), message,
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length);
        }
    }

    // ---------------------------------------------------------------------
    // G2-b — missing possessive after a patient name (audit §3.3): "Mrs
    // Clarke temperature", "Mrs MacIntyre history", "Ms Johnson right total
    // knee replacement", "Erika fasting sugars". A lowercase possessed noun
    // phrase from a closed lexicon must follow the name directly. Nouns that
    // double as third-person verbs (needs, notes, records, hands, arms,
    // supplements) are never a bare head, and right/left/back/home are
    // modifiers only, so "Mr X needs", "Mr X left work" and "call Mr X back"
    // never match. Causative/perception/double-object verbs before the name
    // ("help Mrs X care", "gave Mr X ...") and coordinated subjects are
    // excluded by lookbehind.
    // ponytail: closed lexicon; possessed nouns outside it need human review.
    // ---------------------------------------------------------------------

    private const string SaG2PossHeads =
        "temperature|pulse|blood|weight|height|oxygen|observations|glucose|sugars?|cholesterol|haemoglobin|urine|history|conditions?|diagnosis|symptoms?"
        + "|depression|anxiety|diabetes|asthma|hypertension|epilepsy|pain|mood|sleep|appetite|memory|mobility|vision|hearing|speech|swallowing|cough"
        + "|wound|ulcers?|injury|injuries|fracture|infection|recovery|progress|prognosis|death|illness|disease|cancer|pregnancy|labour|delivery"
        + "|surgery|operation|procedure|replacement|admission|discharge|treatment|management|care|medications?|medicines|therapy|rehabilitation"
        + "|regimen|regime|dose|adherence|compliance|results|investigations|safety|wellbeing|independence|function|health|lifestyle|diet|intake"
        + "|family|husband|wife|partner|daughter|son|mother|father|parents|children|child|baby|sister|brother|aunt|uncle|carer|employer|workplace"
        + "|difficulty|difficulties|requirements?|arrangements|circumstances|plan|programme|appointment|device|strategies|smile|cream";

    private const string SaG2PossBody =
        "arm|legs?|foot|feet|ankles?|knees?|hips?|chest|abdomen|eye|hand|wrists?|shoulder|elbows?|neck|head|toe|fingers?|thumbs?|nipples?|breasts?"
        + "|lungs?|liver|kidneys?|heart|skin|spine|face|mouth|teeth|tooth|throat|ears?|nose|scalp|groin";

    private const string SaG2PossMods =
        "right|left|recent|current|usual|previous|existing|ongoing|pre-existing|underlying|(?:poorly|well)[ -]controlled|poorly|long-term|short-term"
        + "|chronic|acute|fasting|regular|home|total|follow-up|safe|mental|physical|oral|nutritional|urinary|tract|intrauterine|rheumatoid|emergency"
        + "|allergy|living|life|coping|occupational|type|one|two|steroid|insulin|hair|removal|over-the-counter|dietary|medical|surgical|social"
        + "|first|second|third|back";

    private const string SaG2PossToken = "(?:" + SaG2PossMods + "|" + SaG2PossHeads + "|" + SaG2PossBody + ")";

    private const string SaG2PossNp =
        "(?:(?:" + SaG2PossToken + @"[ \t]+){0,4}(?:" + SaG2PossHeads + "|" + SaG2PossBody + ")"
        + "|(?:" + SaG2PossToken + @"[ \t]+){1,4}supplements)" + @"\b(?![-'’])";

    private const string SaG2PossContext =
        @"(?<!\b(?:the|gave|give|gives|given|giving|offer|offered|send|sent|show|showed|shown|teach|taught|tell|told|bring|brought|grant|granted"
        + @"|prescribe|prescribed|allow|allowed|wish|wished|ask|asked|help|helps|helped|helping|let|lets|make|makes|made|see|saw|seen|watch|watched"
        + @"|notice|noticed|observe|observed|find|found|hear|heard|keep|kept|leave|supply|supplied|issue|issued|lend|lent|award|awarded"
        + @"|have|having|get|getting|got)\s+)"
        + @"(?<!\b(?:Mr|Mrs|Ms|Miss|Dr)\.?(?:\s+[A-Z][A-Za-z'’-]*)?\s+and\s+)";

    private static readonly Regex SaG2MissingPossessiveTitledRe = new(
        SaG2PossContext
        + @"\b(?<name>(?:Mr|Mrs|Ms|Miss|Master|Dr)\.?[ \t]+[A-Z][A-Za-z]*(?:['’-][A-Z][A-Za-z]*)*(?:[ \t]+[A-Z][A-Za-z]*(?:['’-][A-Z][A-Za-z]*)*)?)"
        + @"(?!['’A-Za-z])[ \t]+(?<np>" + SaG2PossNp + ")");

    // First names that are also months or common words are never used for the
    // first-name branch ("May", "Will", "Grace" ...).
    private static readonly HashSet<string> SaG2AmbiguousFirstNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December",
        "Will", "Grace", "Rose", "Faith", "Hope", "Joy", "Mark", "Bill", "Pat", "Sue", "Ray", "Dawn", "Summer", "Iris", "Ruby",
        "Jack", "Carol", "Frank", "Guy", "Art", "Ivy", "Holly", "Amber", "Chase", "Hunter", "Sunny", "Rich", "Earl", "Page",
    };

    private static IEnumerable<LintFinding> DetectSaG2MissingPossessiveName(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        var hits = SaG2MissingPossessiveTitledRe.Matches(s.Body).Cast<Match>().ToList();
        var first = ResolvePatientName(input, s)?.First;
        if (first is { Length: > 1 } && !SaG2AmbiguousFirstNames.Contains(first))
        {
            var firstNameRe = new Regex(
                SaG2PossContext
                + @"(?<!\b(?:[Ii]n|[Oo]n|[Bb]y|[Ss]ince|[Dd]uring|[Uu]ntil|[Ff]rom|[Oo]f|Mr|Mrs|Ms|Miss|Master|Dr)\.?[ \t])"
                + @"\b(?<name>" + Regex.Escape(first) + @")(?!['’A-Za-z])[ \t]+(?<np>" + SaG2PossNp + ")");
            hits.AddRange(firstNameRe.Matches(s.Body).Cast<Match>());
        }
        var reported = new HashSet<int>();
        foreach (var m in hits.OrderBy(h => h.Index))
        {
            if (!reported.Add(m.Index)) continue;
            var name = m.Groups["name"].Value;
            var np = m.Groups["np"].Value;
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "Missing possessive: \"" + name + " " + np + "\" should be \"" + name + "'s " + np + "\" (or \"his/her " + np + "\").",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: name + "'s " + np);
        }
    }

    // ---------------------------------------------------------------------
    // G2-c — intro_adverbial_comma extension (OA4-02 + decisions §C.11). The
    // legacy IntroAdverbialNoCommaRe only fires when the NEXT word is
    // capitalised and only knows month-first dates, so "On examination she
    // ...", "On admission Ms ...", "On 23 November Mr ...", "In August 2019
    // Mr ..." and "The following morning Mr ..." passed. A continuation of the
    // phrase ("On review in July 2019,", "On examination today,", "In 2011
    // and 2012,") never fires: the next word must be capitalised or a
    // clause-subject word. Legacy matches are skipped (reported once).
    // ---------------------------------------------------------------------

    private const string SaG2Months = "January|February|March|April|May|June|July|August|September|October|November|December";

    private static readonly Regex SaG2IntroAdverbialNoCommaRe = new(
        @"(?:^|(?<=[.!?])[ \t]+)(?<phrase>Today(?!['’])|Overnight|Initially|Later on(?![ \t]+(?:the|that|this)\b)"
        + @"|On (?:examination|presentation|admission|arrival|discharge|review|assessment)(?: today)?"
        + @"|On today['’]s (?:review|visit|presentation|assessment|examination)|At (?:today['’]s )?review"
        + @"|On the following (?:day|morning|visit)|On subsequent (?:visits|reviews)"
        + @"|The following (?:day|morning|afternoon|evening|week|visit)|That (?:morning|afternoon|evening|night|day)"
        + @"|Over the (?:past|last|following|preceding) (?:(?:few|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|\d{1,2}) )?(?:days|weeks|months|years)"
        + @"|(?:On|By|At review on) \d{1,2}(?:st|nd|rd|th)? (?:" + SaG2Months + @")(?: \d{4})?"
        + @"|On (?:" + SaG2Months + @") \d{1,2}(?:st|nd|rd|th)?(?:, \d{4})?"
        + @"|In (?:early |mid-|late )?(?:" + SaG2Months + @")(?: \d{4})?|In \d{4})"
        + @"(?!,)[ \t]+(?!the[ \t]+(?:next|following|same|previous)\b)"
        + @"(?=[A-Z]|(?:he|she|they|it|there|the|his|her|their|its|a|an|this|these|those|both|no|all)\b)",
        RegexOptions.Multiline);

    private static IEnumerable<LintFinding> DetectSaG2IntroAdverbialComma(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        var legacyStarts = IntroAdverbialNoCommaRe.Matches(s.Body).Cast<Match>().Select(x => x.Groups[1].Index).ToHashSet();
        foreach (Match m in SaG2IntroAdverbialNoCommaRe.Matches(s.Body))
        {
            var phrase = m.Groups["phrase"];
            if (legacyStarts.Contains(phrase.Index)) continue;
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "Introductory time phrases take a comma: \"" + phrase.Value + ", ...\"",
                Quote: phrase.Value, Start: bodyOffset + phrase.Index, End: bodyOffset + phrase.Index + phrase.Length,
                FixSuggestion: phrase.Value + ",");
        }
    }

    // ---------------------------------------------------------------------
    // G2-d — number_style_words_vs_digits extension (OA2-15 + decisions §C.8,
    // §C.9): mixed digit/word ranges ("30 to thirty-five cigarettes",
    // "5-six"), digit ranges on descriptive counts ("after 20-30 minutes"),
    // alcohol quantities ("40 units of alcohol"), habit/comparative durations
    // ("an ex-smoker of 35 years", "less than 15 minutes"), sick leave ("30
    // days off work") and an age at a PAST EVENT ("had an appendectomy at 15"
    // -> "at age fifteen"; identification ages such as "aged 61" stay digits).
    // Doses, dates, vitals, lab values, gestation and measurements never match.
    // Any span the legacy DescriptiveNumberRes already covers is skipped.
    // ---------------------------------------------------------------------

    private const string SaG2NumberWord =
        "(?:one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen"
        + "|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety)(?:-(?:one|two|three|four|five|six|seven|eight|nine))?";

    private const string SaG2DescriptiveCountNoun =
        @"(?:cigarettes?|cigars?|standard\s+drinks?|drinks|beers|glasses(?:\s+of\s+wine)?|cans|bottles|pints|units\s+of\s+alcohol"
        + @"|times|episodes|attacks|occasions|minutes|hours|days|weeks|months|years)";

    private static readonly (Regex Re, string Message)[] SaG2DescriptiveNumberRes =
    {
        (new Regex(@"\b(?:\d{1,3}\s*(?:-|–|to|or)\s*" + SaG2NumberWord + "|" + SaG2NumberWord + @"\s*(?:-|–|to|or)\s*\d{1,3})\b(?=\s+(?:[a-z]+\s+)?" + SaG2DescriptiveCountNoun + @"\b)", RegexOptions.IgnoreCase),
            "Mixed digit/word range: write both numbers in words (e.g. \"thirty to thirty-five cigarettes daily\", \"five to six cigarettes daily\")."),
        (new Regex(@"(?<!\b(?:aged|age|at)\s)(?<![\d.,/])\b\d{1,2}\s*(?:-|–|to)\s*\d{1,2}\s+" + SaG2DescriptiveCountNoun + @"\b(?!\s+(?:of\s+)?gestation)", RegexOptions.IgnoreCase),
            "Descriptive range written in digits: write it in words (e.g. \"twenty to thirty minutes\", \"four to five times daily\")."),
        (new Regex(@"\b\d{1,3}\s+(?:standard\s+)?(?:units\s+of\s+alcohol|alcohol\s+units|beers|glasses\s+of\s+wine|alcoholic\s+drinks)\b", RegexOptions.IgnoreCase),
            "Alcohol quantity written in digits: write it in words (e.g. \"forty units of alcohol weekly\")."),
        (new Regex(@"\b(?:(?:ex-|non-)?smok\w*|drink\w*|history)\s+of\s+\d{1,2}\s+years?\b|(?<!\b(?:older|younger)\s)\b(?:less|more|fewer|longer|shorter)\s+than\s+\d{1,2}\s+(?:minutes|hours|days|weeks|months|years|times|episodes)\b", RegexOptions.IgnoreCase),
            "Descriptive duration written in digits: write it in words (e.g. \"for thirty-five years\", \"less than fifteen minutes\")."),
        (new Regex(@"\b\d{1,3}\s+(?:days?|weeks?|months?)\s+(?:off\s+work|of\s+(?:sick\s+)?leave|sick\s+leave)\b", RegexOptions.IgnoreCase),
            "Descriptive period written in digits: write it in words (e.g. \"thirty days off work\")."),
        (new Regex(@"\b(?:died|diagnosed|had|underwent|developed|began|started|stopped|quit|emigrated|married|occurred|removed|excised)\b"
            + @"(?:(?!\b(?:level|count|pressure|rate|HbA1c|glucose|INR|CRP|ESR|score|reading|result|weight|BMI|temperature|pulse|saturation|dose)\b)[^.;!?\n]){0,60}?"
            + @"(?<q>\bat\s+(?:the\s+age\s+of\s+|age\s+)?\d{1,3})\b(?![\d/:%]|[.,]\d)"
            + @"(?!\s*(?:am|pm|units?|mg|mcg|g|kg|mL|L|mmHg|bpm|degrees|°C|cm|mm|weeks?|days?|months?|years?|hours?|minutes?|times)\b)"
            + @"(?=\s*[.,;]|\s+(?:and|in|after|following|when|while|with|from|but)\b)", RegexOptions.IgnoreCase),
            "An age at a past event is descriptive: write it in words (\"at age fifteen\"); identification ages (\"aged 61\", \"a 20-year-old\") stay digits."),
    };

    private static IEnumerable<LintFinding> DetectSaG2NumberStyle(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        var spans = DescriptiveNumberRes
            .SelectMany(legacyRe => legacyRe.Matches(s.Body).Cast<Match>())
            .Select(x => (Start: x.Index, End: x.Index + x.Length))
            .ToList();
        foreach (var (re, message) in SaG2DescriptiveNumberRes)
        {
            foreach (Match m in re.Matches(s.Body))
            {
                var matchEnd = m.Index + m.Length;
                if (spans.Any(sp => m.Index < sp.End && sp.Start < matchEnd)) continue;
                spans.Add((m.Index, matchEnd));
                var quoted = m.Groups["q"].Success ? m.Groups["q"] : (Group)m;
                yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                    message + " Found: \"" + quoted.Value.Trim() + "\".",
                    Quote: quoted.Value.Trim(), Start: bodyOffset + quoted.Index, End: bodyOffset + quoted.Index + quoted.Length);
            }
        }
    }

    // ---------------------------------------------------------------------
    // G2-e + G2-f2 — value_unit_spacing extension (decisions §C.10): the time
    // "8am" -> "8 am" and ASCII exponents "kg/m2" -> "kg/m²", "per mm3" ->
    // "per mm³". The legacy ValueUnitNoSpaceRe knows neither shape.
    // ---------------------------------------------------------------------

    private static readonly Regex SaG2TimeNoSpaceRe = new(
        @"(?<![\w.:])(?<value>\d{1,2}(?:[.:]\d{2})?)(?<suffix>am|pm)\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex SaG2AsciiExponentUnitRe = new(
        @"(?:(?<=\d[ \t]?)(?:kg/m|mm|cm|m)|\bper[ \t]+(?:mm|cm|m)|(?<=/)(?:mm|cm|m))(?<exp>[23])\b");

    private static IEnumerable<LintFinding> DetectSaG2ValueUnitSpacing(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in SaG2TimeNoSpaceRe.Matches(s.Body))
        {
            var fix = m.Groups["value"].Value + " " + m.Groups["suffix"].Value.ToLowerInvariant();
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "Write the time with a space before am/pm (\"" + fix + "\", not \"" + m.Value + "\").",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length, FixSuggestion: fix);
        }
        foreach (Match m in SaG2AsciiExponentUnitRe.Matches(s.Body))
        {
            var fix = m.Value[..^1] + (m.Groups["exp"].Value == "2" ? "²" : "³");
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "Write the unit with a superscript exponent (\"" + fix + "\", not \"" + m.Value + "\").",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length, FixSuggestion: fix);
        }
    }

    // ---------------------------------------------------------------------
    // G2-f1 — numerical_values_have_units extension (OA-15 + decisions
    // §C.10): in a Model Answer every vital sign carries its unit directly
    // after the value — blood pressure mmHg, pulse/heart rate bpm, respiratory
    // rate breaths/min, temperature °C. The legacy detector accepts a bare
    // "148/98" as a unit and searches a 48-character window, so "Pulse 96,
    // blood pressure 110/70" passed on the NEXT vital's ratio. "degrees
    // Celsius" / "38 C" are a unit-style question, never a missing unit.
    // Occurrences the legacy window already reports are skipped.
    // ---------------------------------------------------------------------

    private static readonly Regex SaG2VitalValueRe = new(
        @"\b(?<vital>blood pressure|BP|pulse(?: rate)?|heart rate|respiratory rate|temperature)\b"
        + @"(?:[ \t]+(?:was|is|were|remained|remains|has|had|have|been|should|be|since|now|then|initially|still|fallen|fell|risen|rose|increased"
        + @"|decreased|improved|returned|dropped|elevated|raised|reduced|recorded|measured|controlled|of|at|to|or|and|around|approximately|about"
        + @"|below|above|under|over)){0,6}"
        + @"[ \t]*:?[ \t]*(?<value>\d{1,3}(?:\.\d+)?(?:[ \t]*/[ \t]*\d{1,3})?)(?![\d/]|[.,]\d)"
        + @"(?![ \t]*(?:mmHg|mm[ \t]?Hg|bpm|beats|breaths|°[ \t]?C|C\b|degrees|Celsius|/min|per[ \t]+min|%))"
        + @"(?=[ \t]*(?:[.,;]|$)|[ \t]+(?:and|with|on|in|at|after|before|despite|while|when|which|but|during)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    // The legacy DetectNumericalValuesHaveUnits keyword window and unit test,
    // replicated only to know which occurrences it already reports.
    private static readonly Regex SaG2LegacyVitalWindowRe = new(
        @"\G(?:blood pressure|pulse|heart rate|respiratory rate|temperature)\b(?:\.(?=\d)|[^.\n]){0,48}",
        RegexOptions.IgnoreCase);

    private static readonly Regex SaG2LegacyUnitRe = new(
        @"(mmol\/l|mg\/dl|g\/dl|g\/l|kg\/m|\bmg\b|\bkg\b|\bg\b|\bcm\b|\bmm\b|\bmL\b|mmHg|bpm|breaths\/min|\/min|°c|celsius|mmol|%|\d+\s*\/\s*\d+)",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectSaG2VitalSignUnits(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in SaG2VitalValueRe.Matches(s.Body))
        {
            var vitalGroup = m.Groups["vital"];
            var legacy = SaG2LegacyVitalWindowRe.Match(s.Body, vitalGroup.Index);
            if (legacy.Success && Regex.IsMatch(legacy.Value, @"\d") && !SaG2LegacyUnitRe.IsMatch(legacy.Value)) continue;
            var vital = vitalGroup.Value.ToLowerInvariant();
            var value = m.Groups["value"].Value;
            var unit = vital is "blood pressure" or "bp" ? "mmHg"
                : vital.StartsWith("respiratory", StringComparison.Ordinal) ? "breaths/min"
                : vital == "temperature" ? "°C"
                : "bpm";
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
                "The " + vital + " value \"" + value + "\" has no unit. Model Answers write every vital sign with its unit: \"" + value + " " + unit + "\".",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: value + " " + unit);
        }
    }

    // ---------------------------------------------------------------------
    // G2-g — conditions_lowercase extension (house rule 19 "generic names
    // lowercase"; audit §4 bullet 22 "Indomethacin"). A curated generic
    // lexicon in Title case, mid-sentence only (after a lowercase word,
    // digit, comma, semicolon or closing bracket), never before another
    // capitalised word ("Paracetamol Osteo" is a product name). Trade names
    // are not in the lexicon.
    // ponytail: closed lexicon; generics outside it need human review.
    // ---------------------------------------------------------------------

    private static readonly string[] SaG2GenericDrugNames =
    {
        "paracetamol", "ibuprofen", "aspirin", "indomethacin", "colchicine", "allopurinol", "frusemide", "furosemide", "digoxin",
        "metformin", "glipizide", "gliclazide", "insulin", "atorvastatin", "rosuvastatin", "simvastatin", "amlodipine", "ramipril",
        "perindopril", "lisinopril", "candesartan", "irbesartan", "metoprolol", "atenolol", "bisoprolol", "warfarin", "heparin",
        "enoxaparin", "dalteparin", "tinzaparin", "clopidogrel", "salbutamol", "fluticasone", "budesonide", "prednisolone",
        "prednisone", "dexamethasone", "hydrocortisone", "amoxicillin", "amoxycillin", "flucloxacillin", "cefalexin", "cephalexin",
        "ceftriaxone", "benzylpenicillin", "penicillin", "doxycycline", "trimethoprim", "ciprofloxacin", "clindamycin", "lymecycline",
        "metronidazole", "linezolid", "vancomycin", "gentamicin", "isoniazid", "rifampicin", "rifampin", "pyrazinamide", "ethambutol",
        "morphine", "oxycodone", "codeine", "tramadol", "pethidine", "ketamine", "diazepam", "temazepam", "lorazepam", "sertraline",
        "fluoxetine", "citalopram", "escitalopram", "paroxetine", "mirtazapine", "amitriptyline", "venlafaxine", "olanzapine",
        "risperidone", "haloperidol", "quetiapine", "lithium", "valproate", "carbamazepine", "levetiracetam", "phenytoin", "gabapentin",
        "pregabalin", "metoclopramide", "ondansetron", "pantoprazole", "omeprazole", "esomeprazole", "ranitidine", "loperamide",
        "lactulose", "clonidine", "prazosin", "hydrochlorothiazide", "spironolactone", "nifedipine", "levothyroxine", "thyroxine",
        "carbimazole", "eflornithine", "clotrimazole", "methotrexate", "sulfasalazine", "alendronate", "sitagliptin", "captopril",
        "nitrofurantoin", "azithromycin", "erythromycin", "aciclovir", "acyclovir", "naproxen", "diclofenac", "celecoxib", "meloxicam",
        "eletriptan", "sumatriptan", "donepezil",
    };

    private static readonly Regex SaG2CapitalisedGenericRe = new(
        @"(?<=[a-z0-9,;)][ \t])(?<drug>"
        + string.Join("|", SaG2GenericDrugNames.Select(g => char.ToUpperInvariant(g[0]) + g[1..]))
        + @")\b(?![ \t]+[A-Z])");

    private static IEnumerable<LintFinding> DetectSaG2GenericDrugCapitalised(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in SaG2CapitalisedGenericRe.Matches(s.Body))
        {
            var lower = m.Value.ToLowerInvariant();
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "Generic medicine names are lowercase in running text: \"" + lower + "\", not \"" + m.Value + "\" (capitalise trade names only).",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length, FixSuggestion: lower);
        }
    }

    // ---------------------------------------------------------------------
    // G2-h — typographic corruption left by blind find/replace repairs:
    // a space before punctuation ("medial aspect ."), a doubled word
    // ("diabetes mellitus mellitus"; "had had" and "that that" are valid) and
    // a lowercase sentence start ("symptoms. today, Ms Day ..."). Decimals,
    // ellipses, "e.g./i.e./a.m." and camel-case tokens (eGFR, pH) never fire.
    // Word-form artefacts ("ex-smokes", "social drinks alcohol") belong to the
    // G1 malformed_word_form detector and are deliberately not repeated here.
    // ---------------------------------------------------------------------

    private static readonly Regex SaG2SpaceBeforePunctuationRe = new(
        @"(?<word>\S*[A-Za-z0-9%)\]])[ \t]+(?<p>[.,;!?])(?=\s|$)",
        RegexOptions.Multiline);

    private static readonly Regex SaG2DoubledWordRe = new(
        @"\b(?<w>[A-Za-z]{2,})[ \t]+\k<w>\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex SaG2LowercaseSentenceStartRe = new(
        @"(?:^|(?<!\b(?i:e\.g|i\.e|etc|approx|vs|cf|al|no|a\.m|p\.m)\.)(?<=[.!?])[ \t]+)(?<w>[a-z][a-z'’-]*)\b",
        RegexOptions.Multiline);

    private static IEnumerable<LintFinding> DetectSaG2TypographicCorruption(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in SaG2SpaceBeforePunctuationRe.Matches(s.Body))
        {
            var word = m.Groups["word"].Value;
            var p = m.Groups["p"].Value;
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "Remove the space before \"" + p + "\" (\"" + word + p + "\", not \"" + m.Value + "\").",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length, FixSuggestion: word + p);
        }
        foreach (Match m in SaG2DoubledWordRe.Matches(s.Body))
        {
            var w = m.Groups["w"].Value;
            if (w.Equals("had", StringComparison.OrdinalIgnoreCase) || w.Equals("that", StringComparison.OrdinalIgnoreCase)) continue;
            // A capitalised reduplicated place name ("Wagga Wagga") is correct.
            if (char.IsUpper(m.Value[0]) && char.IsUpper(m.Value[m.Length - w.Length])) continue;
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "Doubled word \"" + m.Value + "\": write \"" + w + "\" once.",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length, FixSuggestion: w);
        }
        foreach (Match m in SaG2LowercaseSentenceStartRe.Matches(s.Body))
        {
            var w = m.Groups["w"];
            var fix = char.ToUpperInvariant(w.Value[0]) + w.Value[1..];
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "A sentence must start with a capital letter (\"" + fix + "\", not \"" + w.Value + "\").",
                Quote: w.Value, Start: bodyOffset + w.Index, End: bodyOffset + w.Index + w.Length, FixSuggestion: fix);
        }
    }
}
