using System.Globalization;
using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Senior Assessor Release Audit (16 Sep 2026), group G3: patient identity.
/// Every detector here is Model Answer only (DECISIONS §B); candidate lanes
/// keep their existing behaviour.
/// <list type="bullet">
/// <item>G3a: minor status comes from the DOB at the letter date
/// (SaG3WithDerivedMinorStatus, called once from Lint), and a minor is never
/// called title + surname in the body.</item>
/// <item>G3b: an adult is never called by the first name alone.</item>
/// <item>G3c: the Re: line title agrees with the letter's own pronouns.</item>
/// <item>G3d: age_dob_inconsistent, an age that contradicts the DOB.</item>
/// <item>G3e: "was born on" + a date in the notes is a DOB.</item>
/// <item>G3g: the sign-off is "Yours sincerely," alone, then ONE designation line.</item>
/// </list>
/// </summary>
public sealed partial class WritingRuleEngine
{
    // ---------------------------------------------------------------------
    // Shared identity helpers: letter date, patient DOB, age at the letter
    // date, and the ages the letter ties to the patient.
    // ---------------------------------------------------------------------

    // "DOB: 12 August 2011", "DOB 9 September 1976", "D.O.B: 08 June 1973",
    // "date of birth 12/08/11" on the Re: line.
    private static readonly Regex SaG3ReLineDobRe = new(
        @"\b(?:D\.?O\.?B\b\.?|date\s+of\s+birth)\s*:?\s*(?<dob>[^,;\n]+)", RegexOptions.IgnoreCase);

    // A written date first ("17 September 1960"), then a numeric day-first
    // date with the FindNotesDob two-digit-year pivot (<= 30 is 20xx).
    private static DateTime? SaG3ParseDate(string text, bool atStart = false)
    {
        var written = DateTokenRe.Match(text);
        if (written.Success && (!atStart || written.Index == 0) && TryParseDateToken(written, out var writtenDate))
            return writtenDate;
        var numeric = NumericDateRe.Match(text);
        if (!numeric.Success || (atStart && numeric.Index != 0)) return null;
        if (!int.TryParse(numeric.Groups["d"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var day)
            || !int.TryParse(numeric.Groups["m"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(numeric.Groups["y"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var yearRaw))
            return null;
        var year = yearRaw < 100 ? (yearRaw <= 30 ? 2000 + yearRaw : 1900 + yearRaw) : yearRaw;
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return null;
        return new DateTime(year, month, day);
    }

    private static DateTime? SaG3LetterDate(LetterStructure s)
        => s.DateIndex is null ? null : SaG3ParseDate(s.Lines[s.DateIndex.Value]);

    // G3e: "Mrs Mary Clarke was born on 17 September 1960 and is a patient in
    // your General Practice." / "Patient was born on 6 July 1989." The subject
    // must open the sentence and be the patient ("Patient", or the patient's
    // own name), so a relative's birth ("In July 2018 her daughter was born
    // after a long labour", "His daughter was born on ...", "Mr Robert Taylor
    // was born on ..." for patient David Taylor) never becomes the DOB.
    private static readonly Regex SaG3BornOnRe = new(
        @"(?:^|[.!?]\s+)(?:(?<pt>(?:The\s+)?[Pp]atient)|(?:(?<title>Mr|Mrs|Ms|Miss|Master)\.?\s+)?(?<first>[A-Z][a-zA-Z'’\-]+)(?:\s+(?<last>[A-Z][a-zA-Z'’\-]+))?)\s+was\s+born\s+on\s+",
        RegexOptions.Multiline);

    private static bool SaG3BornOnSubjectIsPatient(Match m, PatientNameForms? name)
    {
        if (m.Groups["pt"].Success) return true;
        if (name?.Last is null) return false;
        var first = m.Groups["first"].Value;
        if (m.Groups["last"].Success)
            return string.Equals(m.Groups["last"].Value, name.Last, StringComparison.OrdinalIgnoreCase)
                && (name.First is null || string.Equals(first, name.First, StringComparison.OrdinalIgnoreCase));
        // One token: an untitled first name ("Amina was born on"), or the
        // patient's own title + surname ("Mrs Clarke was born on").
        return m.Groups["title"].Success
            ? string.Equals(m.Groups["title"].Value, name.Title, StringComparison.Ordinal)
                && string.Equals(first, name.Last, StringComparison.OrdinalIgnoreCase)
            : string.Equals(first, name.First, StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime? SaG3BornOnDob(string notes, PatientNameForms? name)
    {
        foreach (Match m in SaG3BornOnRe.Matches(notes))
        {
            if (!SaG3BornOnSubjectIsPatient(m, name)) continue;
            var start = m.Index + m.Length;
            if (SaG3ParseDate(notes.Substring(start, Math.Min(24, notes.Length - start)), atStart: true) is DateTime dob)
                return dob;
        }
        return null;
    }

    private static DateTime? SaG3NotesDob(WritingLintInput input, LetterStructure s)
        => input.CaseNotesText is { Length: > 0 } notes
            ? FindNotesDob(notes) ?? SaG3BornOnDob(notes, ResolvePatientName(input, s))
            : null;

    // The Re: line DOB first (the letter's own identification), then the
    // canonical notes.
    private static DateTime? SaG3PatientDob(WritingLintInput input, LetterStructure s)
    {
        if (s.ReLineIndex is int reIdx)
        {
            foreach (Match m in SaG3ReLineDobRe.Matches(s.Lines[reIdx]))
            {
                if (SaG3ParseDate(m.Groups["dob"].Value) is DateTime reDob) return reDob;
            }
        }
        return SaG3NotesDob(input, s);
    }

    private static int SaG3AgeAt(DateTime dob, DateTime at)
    {
        var age = at.Year - dob.Year;
        if (dob.AddYears(age) > at) age--;
        return age;
    }

    private static int? SaG3PatientAgeAtLetterDate(WritingLintInput input, LetterStructure s)
        => SaG3LetterDate(s) is DateTime at && SaG3PatientDob(input, s) is DateTime dob && dob <= at
            ? (int?)SaG3AgeAt(dob, at)
            : null;

    private const string SaG3AgeNumber = @"(?<n>\d{1,3}|[A-Za-z]+(?:-[A-Za-z]+)?)";

    // "Re: Mr Jane Browne, aged 18" / "Re: Ms Rosalind Hinds, aged 6 days".
    private static readonly Regex SaG3ReLineAgeRe = new(
        @"\baged\s+" + SaG3AgeNumber + @"(?:\s+(?<unit>day|week|month|year)s?)?\b");

    private static readonly Dictionary<string, int> SaG3AgeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
        ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13,
        ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18,
        ["nineteen"] = 19, ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60,
        ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
    };

    private static int? SaG3ParseAgeNumber(string token)
    {
        if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            return number < 130 ? (int?)number : null;
        var parts = token.Split('-');
        if (parts.Length > 2) return null;
        var total = 0;
        foreach (var part in parts)
        {
            if (!SaG3AgeWords.TryGetValue(part, out var value)) return null;
            total += value;
        }
        return total;
    }

    private static int? SaG3MentionYears(Match m)
    {
        if (SaG3ParseAgeNumber(m.Groups["n"].Value) is not int n) return null;
        var unit = m.Groups["unit"].Success ? m.Groups["unit"].Value : "year";
        return unit switch
        {
            "day" or "week" => 0,
            "month" => n / 12,
            _ => n,
        };
    }

    // Only an age attached to a PATIENT reference counts: "Mr Greenbaum, a
    // 23-year-old lawyer", "Mr Jones, aged 44,", "Amina Ahmed, an
    // eight-year-old girl", "Mr Smith is 45 years old". Relatives ("her
    // 12-year-old sister", "one daughter, aged 28 months", "Patrick Dsouza,
    // aged 73"), past events ("at age 57", "at 15", "Mr Weir was fifteen years
    // old when he started smoking": only "is" counts) and a titled relative
    // with another first name ("Mr Robert Taylor, aged 80") never match. A
    // titled reference must carry the Re: line's own title; for an untitled
    // (child) Re: line the first name is required, because "Mrs Ahmed" is
    // usually a parent.
    private static Regex? SaG3PatientAgeRegex(PatientNameForms? name)
    {
        if (name?.Last is null) return null;
        var last = Regex.Escape(name.Last);
        string reference;
        if (name.Title is null)
        {
            if (name.First is null) return null;
            reference = @"(?:(?:Mr|Mrs|Ms|Miss|Master)\.?\s+)?" + Regex.Escape(name.First) + @"(?:\s+" + last + ")?";
        }
        else if (name.First is null)
        {
            reference = Regex.Escape(name.Title) + @"\.?\s+" + last;
        }
        else
        {
            var first = Regex.Escape(name.First);
            reference = "(?:" + Regex.Escape(name.Title) + @"\.?\s+(?:" + first + @"\s+(?:[A-Z][a-zA-Z'’\-]+\s+)?)?" + last
                + "|" + first + @"(?:\s+" + last + "))";
        }
        var tail = @"(?:,\s*aged\s+" + SaG3AgeNumber + @"(?:\s+(?<unit>day|week|month|year)s?)?\b"
            + @"|,\s*an?\s+" + SaG3AgeNumber + @"[\s-](?<unit>year|month|week|day)s?[\s-]old\b"
            + @"|\s+is\s+(?:an?\s+)?" + SaG3AgeNumber + @"[\s-]?(?<unit>year|month|week|day)s?[\s-]old\b)";
        return new Regex(@"\b(?:" + reference + ")" + tail);
    }

    private static IEnumerable<(string Text, int Years, int? Start)> SaG3PatientAgeMentions(WritingLintInput input, LetterStructure s)
    {
        if (s.ReLineIndex is int reIdx)
        {
            foreach (Match m in SaG3ReLineAgeRe.Matches(s.Lines[reIdx]))
            {
                if (SaG3MentionYears(m) is int reYears) yield return (m.Value, reYears, null);
            }
        }
        if (s.Body.Length == 0 || SaG3PatientAgeRegex(ResolvePatientName(input, s)) is not Regex bodyRe) yield break;
        var bodyOffset = BodyOffset(s);
        foreach (Match m in bodyRe.Matches(s.Body))
        {
            if (SaG3MentionYears(m) is int bodyYears) yield return (m.Value, bodyYears, bodyOffset + m.Index);
        }
    }

    // ---------------------------------------------------------------------
    // G3a — child naming. R06.10: day 1 of life up to and including 17 years
    // is a child (no title in the Re: line, first name in the body); 18 is an
    // adult. WritingPatientAgeExtractor reads only ages written as text, so a
    // DOB-only minor (Amina Ahmed, DOB 12 August 2011, letter 14 October 2019;
    // Sally Webster, DOB 10 November 2003, letter 21 February 2020) reached
    // every naming rule as an adult. Lint() calls this once, right after
    // ParseLetter, so every detector that reads PatientIsMinor (minor naming,
    // re_line_full_name, ResolvePatientName's Child flag) sees the same
    // answer. The DOB at the letter date wins in both directions (OA3-03).
    // Without a DOB and without a notes-stated age, an age the letter ties to
    // the patient ("Ms Hinds, aged 6 days") can only mark a minor.
    // ---------------------------------------------------------------------
    private static WritingLintInput SaG3WithDerivedMinorStatus(WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) return input;
        if (SaG3PatientAgeAtLetterDate(input, s) is int age) return input with { PatientIsMinor = age < 18 };
        if (!input.PatientIsMinor && input.PatientAge is null
            && SaG3PatientAgeMentions(input, s).Any(mention => mention.Years < 18))
            return input with { PatientIsMinor = true };
        return input;
    }

    // Composes with minor_naming_convention (the existing detector checks the
    // Re: line only). A titled Re: line already names the child with that
    // title, so the same title on the surname in the body is the child ("Ms
    // Webster, a 16-year-old high school student"); with an untitled Re: line
    // only a titled FULL name is unambiguous, because "Mrs Webster" is
    // normally a parent.
    private static IEnumerable<LintFinding> DetectSaG3MinorBodyTitledName(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || !input.PatientIsMinor || s.Body.Length == 0) yield break;
        var name = ResolvePatientName(input, s);
        if (name?.First is null || name.Last is null) yield break;
        var first = Regex.Escape(name.First);
        var last = Regex.Escape(name.Last);
        var titled = name.Title is null
            ? @"\b(?:Mr|Mrs|Ms|Miss|Master)\.?\s+" + first + @"\s+" + last + @"\b"
            : @"\b" + Regex.Escape(name.Title) + @"\.?\s+(?:" + first + @"\s+)?" + last + @"\b";
        var m = Regex.Match(s.Body,
            @"(?<!\b(?:mother|father|parents?|guardian|mum|dad|grandmother|grandfather|aunt|uncle)['’]?s?,?\s)" + titled);
        if (!m.Success) yield break;
        var age = SaG3PatientAgeAtLetterDate(input, s);
        var ageText = age is int years ? " (aged " + years + " at the letter date)" : "";
        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
            "The patient is a minor" + ageText + ": in the body a child is referred to by first name (\"" + name.First + "\"), never \"" + m.Value + "\" (R06.10). Only patients aged 18 or over take a title.",
            Quote: m.Value, Start: BodyOffset(s) + m.Index, End: BodyOffset(s) + m.Index + m.Length,
            FixSuggestion: name.First);
    }

    // ---------------------------------------------------------------------
    // G3b — adult first-name use (audited pack, Erika Stone: "I am writing to
    // refer Erika for ...", "Erika fasting sugars remain ..."). Composes with
    // body_uses_last_name_only, which only catches a repeated FULL name. A
    // KNOWN adult only (DOB at the letter date, notes-stated age, or an age
    // the letter ties to the patient); with no derivable age the check stays
    // silent ("Miss Alison Cooper, Year 5 student" is a child).
    // ---------------------------------------------------------------------
    private static readonly HashSet<string> SaG3CalendarWords = new(StringComparer.Ordinal)
    {
        "January", "February", "March", "April", "May", "June", "July", "August", "September", "October",
        "November", "December", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
    };

    private static IEnumerable<LintFinding> DetectSaG3AdultBareFirstName(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.Body.Length == 0) yield break;
        var name = ResolvePatientName(input, s);
        if (name is null || name.Child || name.First is null || name.Last is null
            || name.Title is not ("Mr" or "Mrs" or "Ms" or "Miss")
            || SaG3CalendarWords.Contains(name.First))
            yield break;
        var knownAge = SaG3PatientAgeAtLetterDate(input, s) ?? input.PatientAge;
        if (knownAge is null)
        {
            foreach (var mention in SaG3PatientAgeMentions(input, s))
            {
                knownAge = mention.Years;
                break;
            }
        }
        if (knownAge is not >= 18) yield break;
        // Skips "Ms Isabel Garcia" (titled), "Erika Stone" / "David Smith"
        // (a following capitalised name token), place names ("St David's
        // Hospital"), reversed "Zhang Ming", and a relative who shares the
        // first name ("his brother David").
        var bare = Regex.Match(s.Body,
            @"(?<!\b(?:Mr|Mrs|Ms|Miss|Master|Dr|St|Saint)\.?\s+)"
            + @"(?<!\b(?:brother|sister|son|daughter|husband|wife|partner|father|mother|mum|dad|uncle|aunt|cousin|friend|carer|grandson|granddaughter)['’]?s?,?\s+)"
            + @"(?<!\b" + Regex.Escape(name.Last) + @"\s+)"
            + @"\b" + Regex.Escape(name.First) + @"\b(?!\s+[A-Z])(?!['’]s\s+[A-Z])");
        if (!bare.Success) yield break;
        var fix = name.Title + " " + name.Last;
        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
            "An adult patient is referred to by first name alone (\"" + bare.Value + "\"). Use \"" + fix + "\" at the first mention in each paragraph, then pronouns (R06.7 / OWN-W-010); a first name is used only for a child.",
            Quote: bare.Value, Start: BodyOffset(s) + bare.Index, End: BodyOffset(s) + bare.Index + bare.Length,
            FixSuggestion: fix);
    }

    // ---------------------------------------------------------------------
    // G3c — title/sex fidelity: the Re: line title against the letter's own
    // pronouns. Composes with patient_title_mismatch (which only compares
    // titles inside the letter, so a wrong title used consistently passed).
    // Fires only when the body has three or more pronouns of the opposite sex
    // and NONE of the title's sex, so relatives ("Today, Ms Martin attended
    // with her husband. He reported ...") can never trigger it. The scenario
    // title is catalogue metadata and is never read.
    // ---------------------------------------------------------------------
    private static readonly Regex SaG3MalePronounRe = new(@"\b(?:he|him|his|himself)\b", RegexOptions.IgnoreCase);

    private static readonly Regex SaG3FemalePronounRe = new(@"\b(?:she|her|hers|herself)\b", RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectSaG3TitlePronounSex(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.ReLineIndex is null || s.Body.Length == 0) yield break;
        var reLine = s.Lines[s.ReLineIndex.Value];
        var titled = ReTitledNameRe.Match(reLine);
        if (!titled.Success) yield break;
        var title = titled.Groups[1].Value;
        var male = title is "Mr" or "Master";
        if (!male && title is not ("Mrs" or "Ms" or "Miss")) yield break;
        var maleCount = SaG3MalePronounRe.Matches(s.Body).Count;
        var femaleCount = SaG3FemalePronounRe.Matches(s.Body).Count;
        var opposite = male ? femaleCount : maleCount;
        var own = male ? maleCount : femaleCount;
        if (opposite < 3 || own > 0) yield break;
        var oppositeWords = male ? "female pronouns (she/her)" : "male pronouns (he/him/his)";
        var ownWord = male ? "male" : "female";
        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
            "The Re: line title \"" + title + "\" contradicts the letter's pronouns: the body uses " + opposite + " " + oppositeWords + " and no " + ownWord + " pronoun. The title and the pronouns must match the patient's sex in the source; correct whichever is wrong.",
            Quote: reLine.Trim());
    }

    // ---------------------------------------------------------------------
    // G3d — age_dob_inconsistent (James Greenbaum: "Mr Greenbaum, a
    // 23-year-old lawyer" with DOB 5 October 1994 on 25 September 2020, who is
    // 25; Barry Jones: "Mr Jones, aged 44," with DOB 1 April 1972 on 21 March
    // 2015, who is 42). The DOB-derived age at the letter date is the truth
    // (DECISIONS §C.6): a notes-stated age that contradicts the DOB is never
    // copied. Without a DOB, a letter age is compared with the notes-stated
    // age. Model Answer only.
    // ---------------------------------------------------------------------
    private static IEnumerable<LintFinding> DetectSaG3AgeDobInconsistent(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        var expected = input.PatientAge;
        string? basis = null;
        if (SaG3LetterDate(s) is DateTime at && SaG3PatientDob(input, s) is DateTime dob && dob <= at)
        {
            expected = SaG3AgeAt(dob, at);
            basis = "the date of birth (" + dob.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)
                + ") at the letter date (" + at.ToString("d MMMM yyyy", CultureInfo.InvariantCulture) + ")";
        }
        if (expected is not int expectedAge) yield break;
        foreach (var mention in SaG3PatientAgeMentions(input, s))
        {
            if (mention.Years == expectedAge) continue;
            var message = basis is null
                ? "The stated age (\"" + mention.Text + "\") does not match the age recorded in the case notes (" + expectedAge + "). Copy the source age exactly."
                : "The stated age (\"" + mention.Text + "\") contradicts " + basis + ": the patient is " + expectedAge + ". An age must never contradict the DOB; the DOB takes priority (OA3-03), and a notes-stated age that contradicts it is never copied.";
            var fix = basis is null
                ? "State the age recorded in the case notes (" + expectedAge + ")."
                : "Omit the age (the DOB already identifies the patient) or state the DOB-derived age (" + expectedAge + ").";
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major), message,
                Quote: mention.Text, Start: mention.Start, End: mention.Start + mention.Text.Length,
                FixSuggestion: fix);
            yield break;
        }
    }

    // ---------------------------------------------------------------------
    // G3e — DOB priority when the notes write the DOB as "was born on <date>"
    // (Mrs Mary Clarke: "Mrs Mary Clarke was born on 17 September 1960 and is
    // a patient in your General Practice." with "Re: Mrs Mary Clarke").
    // Composes with re_line_dob_priority, whose FindNotesDob reads only the
    // "DOB" / "date of birth" labels: this emits only when that label reading
    // finds nothing, so the two can never report the same letter. Same message
    // as the existing detector. Nothing patient-specific (the Weir DOB is a
    // source-data fix, not a detector change).
    // ---------------------------------------------------------------------
    private static IEnumerable<LintFinding> DetectSaG3ReLineDobBornOn(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || input.CaseNotesText is not { Length: > 0 } notes || s.ReLineIndex is null) yield break;
        if (FindNotesDob(notes) is not null) yield break;
        if (SaG3BornOnDob(notes, ResolvePatientName(input, s)) is not DateTime dob) yield break;
        var reLine = s.Lines[s.ReLineIndex.Value];
        if (ReLineDobRe.IsMatch(reLine)) yield break;
        var display = dob.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
            "The canonical case notes record the patient's date of birth (" + display + "), and DOB takes priority over age in the Re: line. Write \"Re: <title> <full name>, DOB: " + display + "\" — use \"aged X\" only when the source supplies no DOB.",
            Quote: reLine.Trim());
    }

    // ---------------------------------------------------------------------
    // G3g — sign-off block shape (Somarni Khaze / Louise Geller: "Yours
    // sincerely, Doctor" followed by a second "Doctor"). Composes with
    // signoff_designation_present, which only measures the blank lines before
    // the first designation line. DECISIONS §C.15: the closing phrase alone on
    // its line, one blank line, ONE designation line, end of letter.
    // ---------------------------------------------------------------------
    private static readonly Regex SaG3ClosingTailRe = new(
        @"^\s*(?<phrase>Yours\s+(?:sincerely|faithfully))\s*,?\s*(?<tail>[A-Za-z].*?)\s*$", RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectSaG3SignoffBlockShape(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.YoursIndex is null) yield break;
        var closing = s.Lines[s.YoursIndex.Value].Trim();
        var signature = s.Lines.Skip(s.YoursIndex.Value + 1).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var tail = SaG3ClosingTailRe.Match(closing);
        if (tail.Success)
        {
            var designation = tail.Groups["tail"].Value;
            var phrase = tail.Groups["phrase"].Value;
            var repeated = signature.Any(l => string.Equals(l, designation, StringComparison.OrdinalIgnoreCase));
            var message = repeated
                ? "The sign-off repeats the designation: \"" + closing + "\" is followed by \"" + designation + "\" again. Write the closing phrase alone on its line, leave one blank line, then write the designation once."
                : "Text follows the closing phrase on the same line (\"" + closing + "\"). Write \"" + phrase + ",\" alone on its line, leave one blank line, then write the designation on its own line.";
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Minor), message,
                Quote: closing, FixSuggestion: phrase + ",\n\n" + designation);
            yield break;
        }
        if (signature.Count > 1)
        {
            var joined = string.Join(" / ", signature);
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Minor),
                "The letter must end with ONE designation line after the closing phrase; found " + signature.Count + " lines (\"" + joined + "\"). Keep only the professional designation (e.g. \"Doctor\").",
                Quote: joined, FixSuggestion: signature[0]);
        }
    }
}
