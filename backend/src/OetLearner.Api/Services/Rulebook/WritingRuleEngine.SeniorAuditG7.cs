using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Senior Assessor Release Audit (16 Sep 2026), group G7 — layout and
/// identity additions from the Model Answer render audit: the Re: line age
/// when the source has no DOB, the recipient block copied exactly from the
/// task, and the role salutation for a role recipient written without "The".
/// Every detector here is Model-Answer-only (DECISIONS §B) and source-gated,
/// so candidate lanes (which never pass CaseNotesText/TaskText) stay silent.
/// </summary>
public sealed partial class WritingRuleEngine
{
    // ---------------------------------------------------------------------
    // re_line_age_when_no_dob (new) — OA3-03 / OA4 §1: "aged X" is the Re:
    // line identification when the source gives an age but no DOB. Live
    // misses: "Re: Mr Allen Mathis" (notes "61 years old"), "Re: Miss Cathy
    // Jones" (25), "Re: Mr Zach Foster" (22), "Re: Mrs Karen Jackson" (36),
    // "Re: Ms Betty Johnson" (81), "Re: Ms Louise Geller" (25), "Re: Mr David
    // Taylor" (38). The patient's age comes from WritingPatientAgeExtractor,
    // so relatives' ages, age lists and past-event ages never count.
    // ---------------------------------------------------------------------

    // Any date-of-birth marker in the notes hands the Re: line to
    // re_line_dob_priority (DOB has priority), including "born on" wording
    // that FindNotesDob does not parse.
    private static readonly Regex SaG7NotesDobMarkerRe = new(
        @"\bD\.?O\.?B\b|\bdate\s+of\s+birth\b|\bbirth\s*date\b|\bborn\s+on\b", RegexOptions.IgnoreCase);

    private static readonly Regex SaG7ReLineDobMarkerRe = new(
        @"\bD\.?O\.?B\b|\bdate\s+of\s+birth\b|\bborn\b", RegexOptions.IgnoreCase);

    private static readonly Regex SaG7ReLineAgedRe = new(
        @"\baged\s+(?<n>\d{1,3})\b", RegexOptions.IgnoreCase);

    // Every age form a repair must remove before appending ", aged N".
    private static readonly Regex SaG7ReLineAgeFormRe = new(
        @",?\s*\b(?:aged\s+\d{1,3}(?:\s*(?:years?|yrs?)(?:\s*old)?)?|age\s*:?\s*\d{1,3}|\d{1,3}[\s-]*(?:years?|yrs?)[\s-]*old)\b",
        RegexOptions.IgnoreCase);

    private const string SaG7RelativeNouns =
        "children|child|sons?|daughters?|wife|husband|partner|brothers?|sisters?|twins?|grandsons?|granddaughters?|grandchildren|grandchild|baby|infant|nephews?|nieces?|mother|father|mum|mom|dad|parents?|aunt|uncle|cousins?|grandmother|grandfather|grandparents?";

    private static IEnumerable<LintFinding> DetectSaG7ReLineAgeWhenNoDob(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        if (input.CaseNotesText is not { Length: > 0 } notes || s.ReLineIndex is null) yield break;
        if (FindNotesDob(notes) is not null || SaG7NotesDobMarkerRe.IsMatch(notes)) yield break;
        if (global::OetLearner.Api.Services.Writing.WritingPatientAgeExtractor.Extract(notes) is not int age) yield break;
        var n = age.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Ambiguous sources stay silent (precision over recall): an age in
        // months/weeks/days ("aged 18 months"), or the same number describing
        // a relative ("his 12-year-old son") that the extractor cannot see
        // because the relative noun follows the number.
        if (Regex.IsMatch(notes, @"\bage[ds]?\s*:?\s*" + n + @"\s*(?:months?|mths?|weeks?|wks?|days?)\b", RegexOptions.IgnoreCase)) yield break;
        if (Regex.IsMatch(notes, @"\b" + n + @"[\s-]*(?:years?|yrs?)[\s-]*old\s+(?:" + SaG7RelativeNouns + @")\b", RegexOptions.IgnoreCase)) yield break;

        var reLine = s.Lines[s.ReLineIndex.Value].TrimEnd();
        // A DOB on the Re: line that the notes lack is
        // re_line_identity_unsupported's finding; a different "aged M" is
        // re_line_identity_unsupported's / the age-consistency check's finding.
        if (SaG7ReLineDobMarkerRe.IsMatch(reLine)) yield break;
        var stated = SaG7ReLineAgedRe.Match(reLine);
        if (stated.Success && stated.Groups["n"].Value != n) yield break;
        if (Regex.IsMatch(reLine, @",\s*aged\s+" + n + @"$")) yield break;

        var fix = SaG7ReLineAgeFormRe.Replace(reLine, string.Empty).TrimEnd(' ', ',', '.') + ", aged " + n;
        var message = stated.Success
            ? "The Re: line states the age but not in the canonical form. The case notes record the patient's age (" + n + ") and no date of birth, so the Re: line ends \", aged " + n + "\": \"" + fix.Trim() + "\"."
            : "The case notes record the patient's age (" + n + ") and no date of birth, so the Re: line must identify the patient with the age: \"" + fix.Trim() + "\". \"aged X\" is the Re: line identification whenever the source has no DOB (OA3-03).";
        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major), message,
            Quote: reLine.Trim(), FixSuggestion: fix.Trim());
    }

    // ---------------------------------------------------------------------
    // address_content_unsupported (new) — DECISIONS §C16: the address block is
    // exactly the task's recipient content. (a) Every line before the date
    // must be supported by the Writing Task or the case notes (case- and
    // punctuation-insensitive token containment): "Dr Helena Rao", "City Eye
    // Centre" and "45 Bridge Street" for a task that only says "a
    // neuro-ophthalmologist" are invented, while a role line such as
    // "Neuro-Ophthalmologist" is supported. (b) A recipient name,
    // post-nominal or postcode the task gives must not be dropped ("Dr Anne
    // Childers MBBS FRANZCOG" -> "Dr Anne Childers"; "London, NW1 2TG" ->
    // "London"). Source-gated on a task that actually addresses a letter to
    // someone ("letter ... to", "write to"), so a task with no recipient
    // instruction can never prove an address invented.
    // ---------------------------------------------------------------------

    private static readonly Regex SaG7LetterToRe = new(
        @"\b(?:letter\b[^.\n]{0,80}?|write\s+)\bto\b", RegexOptions.IgnoreCase);

    private static readonly Regex SaG7AddressToRe = new(
        @"\baddress\s+(?:the|your)\s+letter\s+to\b", RegexOptions.IgnoreCase);

    private static readonly Regex SaG7RegionEndRe = new(
        @"\bIn your answer\b|\n[ \t]*\n|(?<!\b(?:Dr|Mr|Mrs|Ms|St|Rd|Mt|Prof|No))\.(?=\s|$)",
        RegexOptions.IgnoreCase);

    // The task's recipient, when the recipient instruction opens with a titled
    // name: "Dr Anne Childers MBBS FRANZCOG", "Dr S Leyshon", "Dr. Helena Vance".
    // Post-nominals are all-capital tokens (MBBS, FRANZCOG, MRCP(UK)) or PhD.
    private static readonly Regex SaG7TaskRecipientNameRe = new(
        @"^(?:Dr|Mr|Mrs|Ms|Miss|Prof|Professor)\.?[ \t]+(?<name>[A-Z][A-Za-z'’\-]*(?:[ \t]+[A-Z][A-Za-z'’\-]*){1,2}?)(?<post>(?:[ \t]+(?:[A-Z]{2,}[A-Z()]*|PhD))*)(?=[ \t]*(?:[,;\n.]|$))");

    private static readonly Regex SaG7UkPostcodeRe = new(
        @"\b[A-Z]{1,2}\d[A-Z\d]?[ \t]+\d[A-Z]{2}\b");

    // A four-digit postcode ends an address component ("Vic 3185,",
    // "Chermside 4352", "Western Australia 6542"); a street number ("1414
    // Wickham Tce") is followed by a word, and a year follows a month.
    private static readonly Regex SaG7NumericPostcodeRe = new(
        @"(?<=[A-Za-z][ \t,]+)(?<!\b(?i:jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec|january|february|march|april|june|july|august|september|october|november|december|in|since|from|of|by|until|year)[ \t,]+)\b\d{4}\b(?=[ \t]*(?:[,;.\n]|$))");

    private static readonly Regex SaG7TitledLineRe = new(@"^(?:Dr|Mr|Mrs|Ms|Miss|Prof|Professor)\b");

    private static readonly HashSet<string> SaG7AddressStopWords = new(StringComparer.Ordinal)
    {
        "the", "of", "and", "at", "for", "in", "on", "to", "an", "dr", "mr", "mrs", "ms", "miss", "prof", "professor",
    };

    // Standard address abbreviations and their expansions are the same
    // component ("GP" / "General Practitioner", "St" / "Street").
    private static readonly Dictionary<string, string[]> SaG7TokenAliases = new(StringComparer.Ordinal)
    {
        ["st"] = ["street", "saint"],
        ["street"] = ["st"],
        ["saint"] = ["st"],
        ["rd"] = ["road"],
        ["road"] = ["rd"],
        ["tce"] = ["terrace"],
        ["terrace"] = ["tce"],
        ["ave"] = ["avenue"],
        ["avenue"] = ["ave"],
        ["mt"] = ["mount"],
        ["mount"] = ["mt"],
        ["dept"] = ["department"],
        ["department"] = ["dept"],
        ["hosp"] = ["hospital"],
        ["hospital"] = ["hosp"],
        ["centre"] = ["center"],
        ["center"] = ["centre"],
        ["gp"] = ["general", "practitioner"],
        ["general"] = ["gp"],
        ["practitioner"] = ["gp"],
        ["ed"] = ["emergency"],
        ["emergency"] = ["ed"],
    };

    private static string SaG7Normalise(string text)
        => Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();

    // Single letters (initials, the "s" of a possessive) carry no identity;
    // numbers always do.
    private static IEnumerable<string> SaG7Tokens(string text)
        => SaG7Normalise(text).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1 || char.IsDigit(t[0]));

    private static string SaG7TaskRecipientRegion(string task)
    {
        var start = SaG7AddressToRe.Match(task);
        if (!start.Success) start = SaG7LetterToRe.Match(task);
        if (!start.Success) return string.Empty;
        var rest = task[(start.Index + start.Length)..].TrimStart(' ', '\t', ':', '\n');
        var end = SaG7RegionEndRe.Match(rest);
        return end.Success ? rest[..end.Index] : rest;
    }

    // Mirrors DetectRecipientNameMismatch exactly: the index of the recipient
    // line that rule already reports as misspelled, so this detector never
    // double-reports it. -1 when recipient_name_mismatch stays silent.
    private static int SaG7RecipientNameMismatchLine(string task, List<string> blockLines)
    {
        if (!Regex.IsMatch(task, @"\baddress the letter to\b", RegexOptions.IgnoreCase)) return -1;
        var normalisedTask = Regex.Replace(task, @"[^A-Za-z0-9'’\-]+", " ").ToLowerInvariant();
        for (var i = 0; i < blockLines.Count; i++)
        {
            if (!Regex.IsMatch(blockLines[i], @"^(?:Dr|Mr|Mrs|Ms|Miss)\b")) continue;
            var nameMatch = Regex.Match(blockLines[i], @"^(?:Dr|Mr|Mrs|Ms|Miss)\.?\s+(?<name>.+)$");
            if (!nameMatch.Success) continue;
            var nameTokens = Regex.Replace(nameMatch.Groups["name"].Value, @"[^A-Za-z0-9'’\-]+", " ").Trim().ToLowerInvariant();
            if (nameTokens.Length == 0) continue;
            return normalisedTask.Contains(nameTokens, StringComparison.Ordinal) ? -1 : i;
        }
        return -1;
    }

    private static IEnumerable<LintFinding> DetectSaG7AddressContentUnsupported(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        if (input.TaskText is not { Length: > 0 } rawTask) yield break;
        var task = rawTask.Replace("\r\n", "\n");
        if (!SaG7LetterToRe.IsMatch(task)) yield break;
        if (s.DateIndex is not int boundary || boundary == 0) yield break;

        var blockLines = new List<string>();
        for (var i = 0; i < boundary && i < s.Lines.Length; i++)
        {
            var trimmed = s.Lines[i].Trim();
            if (trimmed.Length > 0) blockLines.Add(trimmed);
        }
        if (blockLines.Count == 0) yield break;

        var source = new HashSet<string>(SaG7Tokens(task + "\n" + (input.CaseNotesText ?? string.Empty)), StringComparer.Ordinal);
        var mismatchLine = SaG7RecipientNameMismatchLine(task, blockLines);
        var titledLineFlagged = false;

        // (a) invented content: every significant token must come from the source.
        for (var i = 0; i < blockLines.Count; i++)
        {
            if (i == mismatchLine) continue;
            var line = blockLines[i];
            var missing = SaG7Tokens(line)
                .Where(t => !SaG7AddressStopWords.Contains(t))
                .Where(t => !source.Contains(t) && !(SaG7TokenAliases.TryGetValue(t, out var alts) && alts.Any(source.Contains)))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (missing.Count == 0) continue;
            if (SaG7TitledLineRe.IsMatch(line)) titledLineFlagged = true;
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "The recipient line \"" + line + "\" is not supported by the Writing Task or the case notes (unsupported: " + string.Join(", ", missing) + "). Copy the recipient block exactly from the task — never invent a recipient name, department or address.",
                Quote: line);
        }

        // (b) dropped content: the task's recipient name, post-nominals and postcode.
        var region = SaG7TaskRecipientRegion(task);
        if (region.Length == 0) yield break;
        var block = " " + SaG7Normalise(string.Join(" ", blockLines)) + " ";

        var recipient = SaG7TaskRecipientNameRe.Match(region);
        // "a letter to Mr Smith's GP" names the patient, not the recipient.
        if (recipient.Success && !Regex.IsMatch(recipient.Value, @"['’]s\b"))
        {
            var name = recipient.Groups["name"].Value;
            var post = recipient.Groups["post"].Value.Replace('\t', ' ').Trim();
            if (!block.Contains(" " + SaG7Normalise(name) + " ", StringComparison.Ordinal))
            {
                if (mismatchLine < 0 && !titledLineFlagged)
                {
                    yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                        "The Writing Task names the recipient \"" + recipient.Value.Trim() + "\", but the address block does not carry that name. Copy the recipient's name exactly as the task gives it.",
                        Quote: blockLines[0], FixSuggestion: recipient.Value.Trim());
                }
            }
            else if (post.Length > 0)
            {
                var droppedPost = post.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(p => !block.Contains(" " + SaG7Normalise(p) + " ", StringComparison.Ordinal))
                    .ToList();
                if (droppedPost.Count > 0)
                {
                    var surname = SaG7Normalise(name).Split(' ')[^1];
                    var nameLine = blockLines.FirstOrDefault(l => (" " + SaG7Normalise(l) + " ").Contains(" " + surname + " ", StringComparison.Ordinal)) ?? blockLines[0];
                    yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                        "The Writing Task gives the recipient as \"" + recipient.Value.Trim() + "\", but the address block drops \"" + string.Join(" ", droppedPost) + "\". Keep the recipient's post-nominals exactly as the task gives them.",
                        Quote: nameLine, FixSuggestion: recipient.Value.Trim());
                }
            }
        }

        foreach (var postcode in SaG7UkPostcodeRe.Matches(region).Concat(SaG7NumericPostcodeRe.Matches(region)))
        {
            if (block.Contains(" " + SaG7Normalise(postcode.Value) + " ", StringComparison.Ordinal)) continue;
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "The Writing Task's recipient address includes the postcode \"" + postcode.Value + "\", but the address block drops it. Copy every address component from the task, including the postcode, onto its own line.",
                Quote: blockLines[^1]);
        }
    }

    // ---------------------------------------------------------------------
    // role_salutation_matches_task (extension, Model Answer only) — OA2-17 /
    // HouseStyle rule 4a: "When the task gives a named ROLE but no person
    // name, the salutation uses that exact role." The existing detector only
    // reads a recipient line that starts "The <Role>"; the live Cathy Jones
    // letter addressed "Gynaecology Registrar" (no "The") and still wrote
    // "Dear Doctor,". This branch emits ONLY the bare-role shape: the first
    // recipient line is the role itself, the task corroborates the role and
    // names no person holding it.
    // ---------------------------------------------------------------------

    private static readonly Regex SaG7BareRoleRecipientLineRe = new(
        @"^(?!The\b)(?<role>[A-Z][A-Za-z]+(?:\s+[A-Za-z]+){0,3}?\s+(?:Officer|Manager|Coordinator|Co-ordinator|Registrar|Director|Secretary|Lead|Practitioner))\s*$");

    private static IEnumerable<LintFinding> DetectSaG7RoleSalutationBareRole(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        if (input.TaskText is not { Length: > 0 } task || s.SalutationIndex is null) yield break;
        var boundary = s.DateIndex ?? s.SalutationIndex.Value;
        var first = -1;
        for (var i = 0; i < boundary && i < s.Lines.Length; i++)
        {
            var trimmed = s.Lines[i].Trim();
            // A "The <Role>" line belongs to DetectRoleSalutationMatchesTask.
            if (RoleRecipientLineRe.IsMatch(trimmed)) yield break;
            if (first < 0 && trimmed.Length > 0) first = i;
        }
        if (first < 0) yield break;
        var m = SaG7BareRoleRecipientLineRe.Match(s.Lines[first].Trim());
        if (!m.Success) yield break;
        var role = m.Groups["role"].Value.Trim();
        if (!task.Contains(role, StringComparison.OrdinalIgnoreCase)) yield break;
        // "Dr Phillip Wright, a surgical registrar": the task names the person,
        // so the salutation is the person's name, not the role.
        if (Regex.IsMatch(task, @"\b(?:Dr|Mr|Mrs|Ms|Miss|Prof)\.?\s+[A-Z][A-Za-z'’\-]*(?:\s+[A-Z][A-Za-z'’\-]*){0,2}\s*,?\s*(?:(?i:a|an|the)\s+)?(?i:" + Regex.Escape(role) + @")")) yield break;
        var salutation = s.Lines[s.SalutationIndex.Value].Trim();
        if (Regex.IsMatch(salutation, @"^Dear\s+" + Regex.Escape(role) + @"\s*,?\s*$", RegexOptions.IgnoreCase)) yield break;
        yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Critical),
            "The task addresses the role \"" + role + "\" and names no person, so the salutation uses that exact role: \"Dear " + role + ",\". Do not write \"Dear Doctor,\" or \"Dear Sir/Madam,\" when a usable role is supplied (OA2-17).",
            Quote: salutation, FixSuggestion: "Dear " + role + ",");
    }
}
