using System.Globalization;
using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Senior Assessor Release Audit (16 Sep 2026), group G6: chronology and
/// owner-required facts. Every detector here runs for MODEL ANSWERS ONLY
/// (implementation decisions §B); candidate grading is unchanged.
/// <list type="bullet">
/// <item><c>DetectSaG6LetterDateUnsupported</c> — extra shapes for the existing
/// <c>letter_date_unsupported</c> check (composed with
/// <c>DetectLetterDateUnsupported</c>): numeric note dates (21/03/15,
/// 09.08.14, 7/9/18), an explicit "Today's date" (notes, task or the
/// scenario's TodayDate), a floor (a letter may not pre-date an encounter the
/// notes document) and the numeric-date ceiling the written-date detector
/// cannot see.</item>
/// <item><c>DetectSaG6NarratedChronologyContradiction</c> —
/// <c>narrated_chronology_contradiction</c>: "on today and presented four days
/// later", "her last period of today", and a past event dated after the
/// letter date ("warfarin was recommenced on 26 February" in a letter dated
/// 25 February 2015).</item>
/// <item><c>DetectSaG6OwnerRequiredFactMissing</c> —
/// <c>owner_required_fact_missing</c>: owner-confirmed must-keep facts
/// (OA2-14 Weir blood pressure 88/70 mmHg).</item>
/// </list>
/// </summary>
public sealed partial class WritingRuleEngine
{
    // ---------------------------------------------------------------------
    // Shared date reading (G6a/G6b/G6d)
    // ---------------------------------------------------------------------

    // dd/mm/yy, dd.mm.yy, d/m/yyyy. The same separator must appear twice, so
    // ranges ("03-05/03/15"), times ("10.30am"), ratios and blood pressures
    // ("88/70", "20/10") and decimals ("36.8") never read as dates. A full stop
    // straight after the year is allowed ("discharged on 09/04/19.").
    private static readonly Regex SaG6NumericDateRe = new(
        @"(?<![\d./-])(?<d>\d{1,2})(?<sep>[./])(?<m>\d{1,2})\k<sep>(?<y>\d{4}|\d{2})(?![\d/]|[.-]\d)");

    // An explicit today's date: "Today's Date: 24/08/19", "Assume that
    // today's date is 7 January 2023", "**Today's date:** 24 August 2019".
    // Only a label at the start of a line or sentence counts; inline visit
    // markers ("Today (21/06/14) pain is again less severe", "presented today
    // (06/10/19)") are not labels.
    private static readonly Regex SaG6TodayLabelRe = new(
        @"(?:^|(?<=[.;|]\s{1,3}))[\s\-•*#>_]*(?:assume\s+(?:that\s+)?)?(?:today['’]?s\s+date|date\s+today|current\s+date)[*_]*\s*(?:is\b|[:\-–])?[*_]*\s*(?<rest>[^\n]{0,40})",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    // A note date whose clause marks it as planned, future or not an
    // encounter never sets the floor: appointments, bookings, plans, due
    // doses, labelled discharge/transfer/admission dates, pregnancy anchors
    // (LMP/EDD) and the end of a range ("... to 06/03/15").
    private static readonly Regex SaG6NonEncounterPrefixRe = new(
        // Cross-profession repair (18 Sep 2026): a date introduced by "from"/"commencing" starts an
        // arrangement rather than recording an event, so it is a planned date under rule 39 ("
        // appointments, planned or due dates and the LMP never count"). Mrs Mellors's note
        // "Physiotherapy outpatients three times weekly from 15 May 2018" was read as a documented
        // encounter and set the floor, which rejected her letter dated on the 14 May transfer that
        // both her notes and her task describe — and forced the physiotherapy start to be written
        // as "from today".
        @"\b(?:appointments?|appt|scheduled|booked|planned|plans?|due|next|arranged|made\s+for|expected|until|till|tomorrow|to\s+be|will|shall|pending|awaiting|upcoming|intended|proposed|return|follow[- ]?up|from|commencing|commences?|starting|starts?|beginning|begins?|effective|LMP|LNMP|last\s+(?:normal\s+)?menstrual\s+period|EDD|estimated\s+date|(?:discharge|transfer|admission|surgery|operation|review)\s+date|within|in\s+(?:\d+|one|two|three|four|five|six|seven|eight|a|a\s+few)\s+(?:days?|weeks?|months?))\b|\b(?:to|discharge|transfer)\s*[:\-–]?\s*$",
        RegexOptions.IgnoreCase);

    // Words straight after the date that make it a booking, not an encounter
    // ("14/08/19: appointment with the cardiologist"). Deliberately narrow and
    // limited to the first 40 characters, so a later "discharge was planned"
    // or "the upcoming move" never removes a real encounter.
    private static readonly Regex SaG6NonEncounterSuffixRe = new(
        @"^.{0,40}?\b(?:appointments?|appt|scheduled|booked|planned|arranged|expected|to\s+be|will|pending|awaiting)\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex SaG6LmpRe = new(
        @"\b(?:LMP|LNMP|last\s+(?:normal\s+)?menstrual\s+period)\b",
        RegexOptions.IgnoreCase);

    // Identical to the forward-timeline window of DetectLetterDateUnsupported.
    private static readonly Regex SaG6ForwardTimelineRe = new(
        @"\b(?:review|appointment|follow[- ]?up|see|seen|recheck)\b[^.;\n]{0,60}\bin\s+(?:\d+|one|two|three|four|five|six|seven|a\s+few|a)\s+(?:days?|weeks?|months?)\b",
        RegexOptions.IgnoreCase);

    private static readonly HashSet<string> SaG6Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dr", "Mr", "Mrs", "Ms", "Mx", "St", "Prof", "approx", "vs", "No",
    };

    private sealed record SaG6NoteDate(DateTime Date, int Index, bool Numeric, string Prefix, string Suffix);

    private static string SaG6Format(DateTime date) => date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

    private static bool SaG6TryParseNumericDate(Match m, out DateTime date)
    {
        date = default;
        if (!int.TryParse(m.Groups["d"].Value, out var day)
            || !int.TryParse(m.Groups["m"].Value, out var month)
            || !int.TryParse(m.Groups["y"].Value, out var year)) return false;
        if (year < 100) year += year <= 30 ? 2000 : 1900;
        if (year < 1900 || month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return false;
        date = new DateTime(year, month, day);
        return true;
    }

    // The same exclusions DetectLetterDateUnsupported applies to written
    // dates: a date introduced by a DOB/birth label (look-back bounded by the
    // previous sentence or line), and a bare date line with no words, are never
    // treatment dates.
    private static bool SaG6IsTreatmentDateCandidate(string notes, Match m)
    {
        var lookBack = Math.Max(0, m.Index - 60);
        var windowStart = lookBack;
        for (var i = m.Index - 1; i >= lookBack; i--)
        {
            if (notes[i] is '.' or '\n' or '\r' or ';' or '|') { windowStart = i + 1; break; }
        }
        var window = notes.Substring(windowStart, m.Index - windowStart);
        if (Regex.IsMatch(window, @"\b(?:dob|date\s+of\s+birth|birth\s+date|born)\b", RegexOptions.IgnoreCase)) return false;
        var lineStart = notes.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
        var lineEnd = notes.IndexOf('\n', m.Index + m.Length);
        if (lineEnd < 0) lineEnd = notes.Length;
        var line = notes.Substring(lineStart, lineEnd - lineStart).Trim();
        return line.Length == 0 || Regex.IsMatch(line, @"[A-Za-z]");
    }

    private static bool SaG6IsClauseBoundary(string text, int i)
    {
        var c = text[i];
        if (c is '\n' or '\r' or ';' or '|') return true;
        if (c is not ('.' or '!' or '?')) return false;
        if (i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1])) return false;
        var wordStart = i;
        while (wordStart > 0 && char.IsLetter(text[wordStart - 1])) wordStart--;
        return !SaG6Abbreviations.Contains(text.Substring(wordStart, i - wordStart));
    }

    private static List<SaG6NoteDate> SaG6NoteDates(string notes)
    {
        var dates = new List<SaG6NoteDate>();
        foreach (Match m in DateTokenRe.Matches(notes))
        {
            if (TryParseDateToken(m, out var writtenDate) && SaG6IsTreatmentDateCandidate(notes, m))
                dates.Add(SaG6Describe(notes, m, writtenDate, isNumeric: false));
        }
        foreach (Match m in SaG6NumericDateRe.Matches(notes))
        {
            if (SaG6TryParseNumericDate(m, out var numericDate) && SaG6IsTreatmentDateCandidate(notes, m))
                dates.Add(SaG6Describe(notes, m, numericDate, isNumeric: true));
        }
        dates.Sort((a, b) => a.Index.CompareTo(b.Index));
        return dates;
    }

    private static SaG6NoteDate SaG6Describe(string notes, Match m, DateTime date, bool isNumeric)
    {
        var start = m.Index;
        while (start > 0 && !SaG6IsClauseBoundary(notes, start - 1)) start--;
        var end = m.Index + m.Length;
        while (end < notes.Length && !SaG6IsClauseBoundary(notes, end)) end++;
        return new SaG6NoteDate(date, m.Index, isNumeric,
            notes.Substring(start, m.Index - start),
            notes.Substring(m.Index + m.Length, end - (m.Index + m.Length)));
    }

    // A stated date directly follows its label (atStart); the TodayDate field
    // is the date itself.
    private static void SaG6AddStatedDate(string text, HashSet<DateTime> into, bool atStart)
    {
        var written = DateTokenRe.Match(text);
        if (written.Success && (!atStart || written.Index <= 2) && TryParseDateToken(written, out var w))
        {
            into.Add(w);
            return;
        }
        var numeric = SaG6NumericDateRe.Match(text);
        if (numeric.Success && (!atStart || numeric.Index <= 2) && SaG6TryParseNumericDate(numeric, out var n))
            into.Add(n);
    }

    // letter_date_unsupported — additional Model Answer shapes. Order:
    // A (explicit today's date) > D (floor) > C (numeric ceiling). At most one
    // finding. Branch C only runs when the notes carry no usable WRITTEN date,
    // because DetectLetterDateUnsupported already owns that ceiling.
    private static IEnumerable<LintFinding> DetectSaG6LetterDateUnsupported(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.DateIndex is not int dateIndex) yield break;
        var dateLine = s.Lines[dateIndex].Trim();
        if (!TryParseDateToken(DateTokenRe.Match(dateLine), out var letterDate)) yield break;
        var notes = input.CaseNotesText ?? "";
        var severity = ModeSeverity(input, RuleSeverity.Critical);

        // A — the source states today's date: the letter carries that date.
        var stated = new HashSet<DateTime>();
        if (!string.IsNullOrWhiteSpace(input.TodayDate)) SaG6AddStatedDate(input.TodayDate, stated, atStart: false);
        foreach (var source in new[] { notes, input.TaskText ?? "" })
        {
            foreach (Match label in SaG6TodayLabelRe.Matches(source))
                SaG6AddStatedDate(label.Groups["rest"].Value, stated, atStart: true);
        }
        if (stated.Count > 1) yield break; // conflicting source dates prove nothing
        if (stated.Count == 1)
        {
            var today = stated.First();
            if (letterDate != today)
                yield return new LintFinding(rule.Id, severity,
                    "The letter is dated " + SaG6Format(letterDate) + ", but the source states today's date as " + SaG6Format(today) + ". Date the letter on the stated day.",
                    Quote: dateLine, FixSuggestion: SaG6Format(today));
            yield break;
        }

        if (notes.Length == 0) yield break;
        var dates = SaG6NoteDates(notes);
        if (dates.Count == 0) yield break;

        // D — floor: the letter may not pre-date the latest documented
        // encounter. Planned/future dates never count, and a date followed
        // later in the notes by an EARLIER encounter is out of sequence (a
        // source typo), so it never sets the floor either.
        var encounters = dates
            .Where(d => !SaG6NonEncounterPrefixRe.IsMatch(d.Prefix) && !SaG6NonEncounterSuffixRe.IsMatch(d.Suffix))
            .ToList();
        DateTime? floor = null;
        for (var i = 0; i < encounters.Count; i++)
        {
            var encounterDate = encounters[i].Date;
            if (encounters.Skip(i + 1).Any(next => next.Date < encounterDate)) continue;
            if (floor is null || encounterDate > floor.Value) floor = encounterDate;
        }
        if (floor is DateTime floorDate && letterDate < floorDate)
        {
            yield return new LintFinding(rule.Id, severity,
                "The letter is dated " + SaG6Format(letterDate) + ", but the case notes document an encounter on " + SaG6Format(floorDate) + ". A letter cannot pre-date the events it reports; date it on the latest documented encounter (" + SaG6Format(floorDate) + ") unless the source states another today's date.",
                Quote: dateLine, FixSuggestion: SaG6Format(floorDate));
            yield break;
        }

        // C — numeric ceiling (same semantics as the written-date ceiling:
        // later than every documented date, 31-day forward-timeline window). A
        // last menstrual period is never the consultation, so it is no ceiling.
        if (dates.Any(d => !d.Numeric)) yield break;
        DateTime? ceiling = null;
        foreach (var d in dates)
        {
            if (SaG6LmpRe.IsMatch(d.Prefix)) continue;
            if (ceiling is null || d.Date > ceiling.Value) ceiling = d.Date;
        }
        if (ceiling is not DateTime ceilingDate || letterDate <= ceilingDate) yield break;
        if (SaG6ForwardTimelineRe.IsMatch(notes) && (letterDate - ceilingDate).TotalDays <= 31) yield break;
        yield return new LintFinding(rule.Id, severity,
            "The letter date (" + SaG6Format(letterDate) + ") is later than every date documented in the case notes (" + SaG6Format(ceilingDate) + " at the latest), so it is an unsupported, invented date. Use the source-supported date of treatment.",
            Quote: dateLine, FixSuggestion: SaG6Format(ceilingDate));
    }

    // ---------------------------------------------------------------------
    // narrated_chronology_contradiction (G6b-body, G6c)
    // ---------------------------------------------------------------------

    // Decimal-safe sentence splitter (same as DetectSemicolonOveruse).
    private static readonly Regex SaG6SentenceRe = new(@"[^.!?\n]+(?:\.(?=\d)[^.!?\n]*)*[.!?]?");

    // "... at work on today and presented four days later ...": an event
    // anchored to today, coordinated by "and" with a second event N days
    // later. "today's review" is a reporting frame, not an event date, and
    // "presented today, four days later," (appositive) has no "and".
    private static readonly Regex SaG6TodayThenLaterRe = new(
        @"\btoday\b(?!['’]s)\s*,?\s+and\s+(?:[a-z]+\s+){0,4}?(?:\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|several|a\s+few)\s+(?:days?|weeks?|months?|years?)\s+later\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex SaG6LmpTodayRe = new(
        @"\b(?:last\s+(?:normal\s+)?(?:menstrual\s+)?period|LMP)\b[^.!?;]{0,15}?\b(?:of|on|was|is)\s+today\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex SaG6PregnancyContextRe = new(@"\bweeks?\b|\bpregnan", RegexOptions.IgnoreCase);

    // Future, planned or conditional sentences never narrate a past event.
    // "may" is matched in lower case only, so the month "May" is not a modal.
    private static readonly Regex SaG6FutureContextRe = new(
        @"\b(?:will|shall|should|must|would|could|might|(?-i:may)|needs?\s+to|is\s+being|are\s+being|to\s+be|is\s+to|are\s+to|going\s+to|tomorrow|due|scheduled|booked|appointments?|planned|plans?|next|arranged|until|expected|pending|awaiting|upcoming|intends?|intended)\b",
        RegexOptions.IgnoreCase);

    // A finite past event bound to "on/by <day> <Month> [year]" in the same
    // clause: "was/were + participle" or a simple past verb that is not part
    // of a present, perfect, modal, infinitive or negated form. The second
    // alternative is a dated finding whose verb follows the date ("Dietetic
    // assessment on 14 August found ..."); "was/were" after the date is not
    // accepted there ("His review on 3 March was cancelled" is correct).
    private const string SaG6DayMonthDate =
        @"(?<date>(?<d>\d{1,2})(?:st|nd|rd|th)?\s+(?<mon>January|February|March|April|May|June|July|August|September|October|November|December)(?:\s+(?<y>\d{4}))?)\b";

    private static readonly Regex SaG6PastEventDateRe = new(
        @"(?:\b(?:was|were)\s+(?:[a-z]+ly\s+)?[a-z]+(?:ed|en)\b" +
        @"|(?<!\b(?:is|are|am|be|been|being|will|shall|to|has|have|would|should|may|might|can|could|must|not|never)\s+(?:[a-z]+ly\s+)?)" +
        @"\b(?:presented|underwent|received|developed|occurred|commenced|recommenced|restarted|started|stopped|ceased|discontinued|transferred|admitted|discharged|diagnosed|reviewed|performed|showed|revealed|confirmed|sustained|attended|returned|collapsed|arrived|reported|complained|fell|rose|began|died))" +
        @"(?<gap>[^,;:.!?]{0,60}?)\b(?:on|by)\s+" + SaG6DayMonthDate +
        @"|\bon\s+" + SaG6DayMonthDate + @"\s+(?:[a-z]+ly\s+)?(?:found|showed|revealed|confirmed|demonstrated)\b",
        RegexOptions.IgnoreCase);

    // The date belongs to a planned action, not the past verb: "was advised
    // to return on 3 March", "was discharged with a review on 3 March".
    private static readonly Regex SaG6GapFutureRe = new(
        @"\bto\s+(?:be|return|re-?attend|attend|start|commence|begin|continue|stop|cease|see|have|undergo|come|restart|resume|complete|take|visit|remain|receive|follow)\b|\b(?:review|appointment|appt|follow[- ]?up|clinic|visit|surgery|operation|procedure|scan|test|injection|dose|session)\s*$",
        RegexOptions.IgnoreCase);

    private static readonly Regex SaG6YearRe = new(@"\b(?:19|20)\d{2}\b");

    private static IEnumerable<LintFinding> DetectSaG6NarratedChronologyContradiction(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        var severity = ModeSeverity(input, RuleSeverity.Major);
        DateTime? letterDate = null;
        if (s.DateIndex is int dateIndex && TryParseDateToken(DateTokenRe.Match(s.Lines[dateIndex]), out var parsedLetterDate))
            letterDate = parsedLetterDate;

        foreach (Match sentenceMatch in SaG6SentenceRe.Matches(s.Body))
        {
            var sentence = sentenceMatch.Value;
            if (sentence.Trim().Length == 0) continue;
            var future = SaG6FutureContextRe.IsMatch(sentence);

            var todayThenLater = SaG6TodayThenLaterRe.Match(sentence);
            if (todayThenLater.Success && !future)
            {
                var at = bodyOffset + sentenceMatch.Index + todayThenLater.Index;
                yield return new LintFinding(rule.Id, severity,
                    "This sentence places one event today and a second event \"" + todayThenLater.Value + "\", which cannot both be true on the letter date. Date the earlier event from the case notes (e.g. \"hurt his back on 17 March and presented today, four days later\").",
                    Quote: todayThenLater.Value, Start: at, End: at + todayThenLater.Length,
                    FixSuggestion: "Give the earlier event its documented date and keep \"today\" for the letter-date encounter.");
                continue;
            }

            var lmpToday = SaG6LmpTodayRe.Match(sentence);
            if (lmpToday.Success && SaG6PregnancyContextRe.IsMatch(sentence))
            {
                var at = bodyOffset + sentenceMatch.Index + lmpToday.Index;
                yield return new LintFinding(rule.Id, severity,
                    "The last menstrual period cannot be \"today\" in a pregnancy dated in weeks. State the LMP date from the case notes (e.g. \"her last menstrual period was on 26 June 2019\").",
                    Quote: lmpToday.Value, Start: at, End: at + lmpToday.Length,
                    FixSuggestion: "her last menstrual period was on <date from the case notes>");
                continue;
            }

            if (letterDate is not DateTime letter || future) continue;
            foreach (Match eventMatch in SaG6PastEventDateRe.Matches(sentence))
            {
                if (SaG6GapFutureRe.IsMatch(eventMatch.Groups["gap"].Value)) continue;
                var hasYear = eventMatch.Groups["y"].Success;
                // A year-less date beside an explicit year ("on 1 June and
                // 1 September 2010") cannot be attributed safely.
                if (!hasYear && SaG6YearRe.IsMatch(sentence)) continue;
                var year = hasYear ? int.Parse(eventMatch.Groups["y"].Value, CultureInfo.InvariantCulture) : letter.Year;
                if (!DateTime.TryParse(eventMatch.Groups["d"].Value + " " + eventMatch.Groups["mon"].Value + " " + year,
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var eventDate)) continue;
                // A year-less date more than a month ahead is last year's
                // ("By 20 November" in a letter dated 1 January 2024).
                if (!hasYear && (eventDate - letter).TotalDays > 31) eventDate = eventDate.AddYears(-1);
                if (eventDate <= letter) continue;
                var dateGroup = eventMatch.Groups["date"];
                var at = bodyOffset + sentenceMatch.Index + dateGroup.Index;
                yield return new LintFinding(rule.Id, severity,
                    "\"" + dateGroup.Value + "\" is narrated as a past event, but it is later than the letter date (" + SaG6Format(letter) + "). A letter cannot report events that had not yet happened; correct the letter date or the event date against the case notes.",
                    Quote: dateGroup.Value, Start: at, End: at + dateGroup.Length,
                    FixSuggestion: "Date the letter on or after " + SaG6Format(eventDate) + ", or correct the event date.");
                break;
            }
        }
    }

    // ---------------------------------------------------------------------
    // owner_required_fact_missing (G6e)
    // ---------------------------------------------------------------------

    // Owner-confirmed must-keep facts. Never inferred: a new entry needs an
    // owner decision. An entry applies when the patient surname is on the Re:
    // line or in the notes AND the notes carry the fact; the letter body must
    // then state it.
    private sealed record SaG6OwnerFact(string OwnerRule, string PatientSurname, Regex NotesTrigger, Regex Required, string Fact, string Fix);

    private static readonly SaG6OwnerFact[] SaG6OwnerFacts =
    {
        new("OA2-14", "Weir",
            new Regex(@"\b88\s*/\s*70\b"),
            new Regex(@"\b88\s*/\s*70\s*mmHg\b"),
            "a blood pressure of 88/70 (material to his dizziness and blackouts)",
            "His blood pressure was 88/70 mmHg."),
    };

    private static IEnumerable<LintFinding> DetectSaG6OwnerRequiredFactMissing(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer || input.CaseNotesText is not { Length: > 0 } notes || s.Body.Length == 0) yield break;
        var reLine = s.ReLineIndex is int reIndex ? s.Lines[reIndex].Trim() : "";
        foreach (var fact in SaG6OwnerFacts)
        {
            var surname = @"\b" + Regex.Escape(fact.PatientSurname) + @"\b";
            if (!Regex.IsMatch(reLine, surname) && !Regex.IsMatch(notes, surname)) continue;
            if (!fact.NotesTrigger.IsMatch(notes) || fact.Required.IsMatch(s.Body)) continue;
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
                "Owner-required fact missing (" + fact.OwnerRule + "): the case notes record " + fact.Fact + ", and this Model Answer must state it. The owner confirmed it as material to the task.",
                Quote: reLine.Length > 0 ? reLine : null, FixSuggestion: fact.Fix);
        }
    }
}
