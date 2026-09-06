using System.Text.Json;
using System.Text.RegularExpressions;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingAssessmentRuleFinding(
    string RuleId,
    string Category,
    string Severity,
    string Message,
    string? Quote,
    string? FixSuggestion,
    int? StartOffset,
    int? EndOffset,
    string PrimaryCriterionCode,
    string SecondaryCriterionCodesJson = "[]");

/// <summary>
/// v1.1 adapter around the existing deterministic Writing Rulebook engine.
/// It adds the PDF's punctuation authority and assigns exactly one primary
/// scoring criterion to each finding.
/// </summary>
public sealed class WritingAssessmentV11RuleEngine(WritingRuleEngine ruleEngine)
{
    public IReadOnlyList<WritingAssessmentRuleFinding> Evaluate(WritingLintInput input)
    {
        var findings = ruleEngine.Lint(input)
            .Select(f => ToFinding(f))
            .Concat(EvaluateHouseStyle(input.LetterText))
            .Concat(EvaluateNaming(input.LetterText, null, input.PatientAge))
            .ToList();

        var unique = new List<WritingAssessmentRuleFinding>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var finding in findings)
        {
            var key = $"{finding.RuleId}|{finding.Quote}|{finding.Message}";
            if (seen.Add(key)) unique.Add(finding);
        }
        return unique;
    }

    public IReadOnlyList<WritingAssessmentRuleFinding> Evaluate(
        WritingLintInput input,
        string? caseNotesSnapshot)
    {
        var findings = Evaluate(input).ToList();
        findings.AddRange(EvaluateNaming(input.LetterText, caseNotesSnapshot, input.PatientAge));
        return findings
            .GroupBy(f => $"{f.RuleId}|{f.Quote}|{f.Message}", StringComparer.Ordinal)
            .Select(g => g.First())
            .ToArray();
    }

    private static IReadOnlyList<WritingAssessmentRuleFinding> EvaluateNaming(
        string letterText,
        string? caseNotesSnapshot,
        int? patientAge)
    {
        if (string.IsNullOrWhiteSpace(caseNotesSnapshot)) return [];
        var nameMatch = Regex.Match(
            caseNotesSnapshot,
            @"(?im)^\s*(?:patient\s+name|patient|name)\s*[:\-]\s*(?<first>[A-Za-z][A-Za-z'’-]+)\s+(?<last>[A-Za-z][A-Za-z'’-]+)\b",
            RegexOptions.IgnoreCase);
        if (!nameMatch.Success || patientAge is null) return [];

        var firstName = nameMatch.Groups["first"].Value;
        var lastName = nameMatch.Groups["last"].Value;
        var reLine = Regex.Match(letterText ?? string.Empty, @"(?im)^\s*Re\s*:\s*(?<value>[^\r\n]+)").Groups["value"].Value.Trim();
        var findings = new List<WritingAssessmentRuleFinding>();
        if (reLine.Length > 0)
        {
            if (patientAge < 18)
            {
                if (Regex.IsMatch(reLine, @"\b(?:Mr|Ms|Mrs|Miss|Dr|Master)\b", RegexOptions.IgnoreCase)
                    || !reLine.Contains(firstName, StringComparison.OrdinalIgnoreCase)
                    || !reLine.Contains(lastName, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(CreateFinding(
                        "R06.10",
                        "For patients aged 1 day through 17 years, the Re: line must contain the full name with no title; never use Master.",
                        reLine,
                        "genre_style",
                        Math.Max(0, letterText.IndexOf(reLine, StringComparison.OrdinalIgnoreCase)),
                        Math.Max(0, letterText.IndexOf(reLine, StringComparison.OrdinalIgnoreCase)) + reLine.Length,
                        $"Use 'Re: {firstName} {lastName}' with no title."));
                }
            }
            else if (!Regex.IsMatch(reLine, $@"^(?:Mr|Ms|Mrs|Miss)\s+{Regex.Escape(lastName)}$", RegexOptions.IgnoreCase)
                || Regex.IsMatch(reLine, @"\bMaster\b", RegexOptions.IgnoreCase))
            {
                findings.Add(CreateFinding(
                    "R06.11",
                    "At 18 years and older, use the patient's title plus last name in the Re: line; never use Master.",
                    reLine,
                    "genre_style",
                    Math.Max(0, letterText.IndexOf(reLine, StringComparison.OrdinalIgnoreCase)),
                    Math.Max(0, letterText.IndexOf(reLine, StringComparison.OrdinalIgnoreCase)) + reLine.Length,
                    $"Use 'Re: Mr/Ms/Mrs/Miss {lastName}' with the correct title."));
            }
        }

        if (Regex.IsMatch(letterText ?? string.Empty, @"\bMaster\b", RegexOptions.IgnoreCase))
        {
            findings.Add(CreateFinding(
                "R06.11",
                "Never use Master for a patient.",
                Regex.Match(letterText, @"\bMaster\b", RegexOptions.IgnoreCase).Value,
                "genre_style",
                Regex.Match(letterText, @"\bMaster\b", RegexOptions.IgnoreCase).Index,
                Regex.Match(letterText, @"\bMaster\b", RegexOptions.IgnoreCase).Index + 6,
                "Replace Master with the required patient naming convention."));
        }

        var bodyStart = reLine.Length == 0
            ? 0
            : Math.Max(0, letterText.IndexOf(reLine, StringComparison.OrdinalIgnoreCase) + reLine.Length);
        var body = letterText[bodyStart..];
        var paragraphs = Regex.Split(body, @"\r?\n\s*\r?\n")
            .Where(p => p.Trim().Length > 0)
            .Where(p => !Regex.IsMatch(p, @"^\s*(?:Yours|Kind regards|Regards)", RegexOptions.IgnoreCase));
        var expected = patientAge < 18 ? firstName : $"(?:Mr|Ms|Mrs|Miss)\\s+{Regex.Escape(lastName)}";
        foreach (var paragraph in paragraphs)
        {
            var firstSentence = Regex.Split(paragraph.Trim(), @"(?<=[.!?])\s+").FirstOrDefault() ?? paragraph.Trim();
            if (!Regex.IsMatch(firstSentence, $@"\b{expected}\b", RegexOptions.IgnoreCase))
            {
                findings.Add(CreateFinding(
                    patientAge < 18 ? "R06.10" : "R06.11",
                    "Use the required patient name at the first mention in each body paragraph.",
                    firstSentence,
                    "genre_style",
                    Math.Max(0, letterText.IndexOf(firstSentence, StringComparison.OrdinalIgnoreCase)),
                    Math.Max(0, letterText.IndexOf(firstSentence, StringComparison.OrdinalIgnoreCase)) + firstSentence.Length,
                    patientAge < 18 ? $"Begin the paragraph with {firstName}." : $"Begin the paragraph with the patient's title and last name."));
            }
        }

        return findings;
    }

    public static string PrimaryCriterionFor(string? ruleId)
    {
        var value = (ruleId ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Contains("purpose") || value.Contains("urgent_intro") || value.Contains("urgent_closure"))
            return "purpose";
        if (value.Contains("content") || value.Contains("discharge_omit") || value.Contains("fact"))
            return "content";
        if (value.Contains("conciseness") || value.Contains("length") || value.Contains("linker_density")
            || value.Contains("sentence_length"))
            return "conciseness_clarity";
        if (value.Contains("genre") || value.Contains("register") || value.Contains("jargon")
            || value.Contains("non_medical") || value.Contains("letter_type"))
            return "genre_style";
        if (value.Contains("address") || value.Contains("salutation") || value.Contains("re_line")
            || value.Contains("blank") || value.Contains("paragraph") || value.Contains("structure")
            || value.Contains("closure") || value.Contains("yours") || value.Contains("discharge_intro")
            || value.Contains("no_brackets") || value.Contains("signoff") || value.Contains("dob_age"))
            return "organisation_layout";
        if (value.Contains("language") || value.Contains("grammar") || value.Contains("tense")
            || value.Contains("punctuation") || value.Contains("linker_") || value.Contains("latin")
            || value.Contains("ago_") || value.Contains("present_perfect") || value.StartsWith("r10")
            || value.StartsWith("r11") || value.StartsWith("r12"))
            return "language";
        return "content";
    }

    public static IReadOnlyList<WritingAssessmentRuleFinding> EvaluateHouseStyle(string letterText)
    {
        var findings = new List<WritingAssessmentRuleFinding>();
        AddLinkerFinding(findings, letterText, "however", "R12.9",
            "Use '[clause]; however, [clause].'", requireCommaAfter: true);
        AddLinkerFinding(findings, letterText, "therefore", "R12.10",
            "Use '[clause]; therefore, [clause].'", requireCommaAfter: true);
        AddLinkerFinding(findings, letterText, "thus", "R12.10",
            "Use '[clause]; thus, [clause].'", requireCommaAfter: true);

        foreach (Match match in Regex.Matches(letterText ?? string.Empty, @"\bin addition\b(?!\s+(?:to|together with|along with|as well as)\b)", RegexOptions.IgnoreCase))
        {
            var before = (letterText[..match.Index]).TrimEnd();
            var afterEnd = match.Index + match.Length;
            var after = letterText[afterEnd..].TrimStart();
            if (!before.EndsWith(';') || !after.StartsWith(','))
            {
                findings.Add(CreateFinding(
                    "R12.11",
                    "in addition must use a semicolon before and a comma after when joining full clauses.",
                    match.Value,
                    "language",
                    match.Index,
                    afterEnd,
                    "Use '[clause]; in addition, [clause].'"));
            }
        }

        foreach (Match match in Regex.Matches(letterText ?? string.Empty, @"\bfor which\b", RegexOptions.IgnoreCase))
        {
            var before = (letterText[..match.Index]).TrimEnd();
            if (!before.EndsWith(','))
            {
                findings.Add(CreateFinding(
                    "R12.17",
                    "Precede 'for which' with a comma.",
                    match.Value,
                    "language",
                    match.Index,
                    match.Index + match.Length,
                    "Insert a comma before 'for which'."));
            }
        }

        foreach (Match match in Regex.Matches(letterText ?? string.Empty, @"\bas\s+(?:he|she|it|they|we|you|the patient|the child|the patient’s|the patient's|this|that)\b", RegexOptions.IgnoreCase))
        {
            var before = (letterText[..match.Index]).TrimEnd();
            if (before.Length > 0 && !before.EndsWith(','))
            {
                findings.Add(CreateFinding(
                    "R12.16",
                    "Use a comma before mid-sentence 'as' when it introduces a reason.",
                    match.Value,
                    "language",
                    match.Index,
                    match.Index + match.Length,
                    "Insert a comma before 'as'."));
            }
        }

        return findings;
    }

    private static void AddLinkerFinding(
        ICollection<WritingAssessmentRuleFinding> findings,
        string letterText,
        string linker,
        string ruleId,
        string message,
        bool requireCommaAfter)
    {
        foreach (Match match in Regex.Matches(letterText ?? string.Empty, $@"\b{linker}\b", RegexOptions.IgnoreCase))
        {
            var before = (letterText[..match.Index]).TrimEnd();
            var after = letterText[(match.Index + match.Length)..].TrimStart();
            if (!before.EndsWith(';') || (requireCommaAfter && !after.StartsWith(',')))
            {
                findings.Add(CreateFinding(
                    ruleId,
                    message,
                    match.Value,
                    "language",
                    match.Index,
                    match.Index + match.Length,
                    $"Insert a semicolon before '{linker}' and a comma after it."));
            }
        }
    }

    private static WritingAssessmentRuleFinding ToFinding(LintFinding finding)
    {
        var ruleId = finding.RuleId;
        return new WritingAssessmentRuleFinding(
            ruleId,
            CategoryFor(ruleId),
            finding.Severity.ToString().ToLowerInvariant(),
            finding.Message,
            finding.Quote,
            finding.FixSuggestion,
            finding.Start,
            finding.End,
            PrimaryCriterionFor(ruleId));
    }

    private static WritingAssessmentRuleFinding CreateFinding(
        string ruleId,
        string message,
        string quote,
        string primaryCriterion,
        int start,
        int end,
        string? fixSuggestion = null)
        => new(
            ruleId,
            "punctuation",
            "major",
            message,
            quote,
            fixSuggestion,
            start,
            end,
            primaryCriterion,
            JsonSerializer.Serialize(Array.Empty<string>()));

    private static string CategoryFor(string ruleId)
    {
        var value = ruleId.ToLowerInvariant();
        if (value.Contains("purpose") || value.Contains("urgent")) return "purpose";
        if (value.Contains("content") || value.Contains("discharge_omit")) return "content";
        if (value.Contains("length") || value.Contains("conciseness") || value.Contains("linker_density")) return "irrelevant_excess";
        if (value.Contains("genre") || value.Contains("jargon") || value.Contains("non_medical")) return "register_jargon";
        if (value.Contains("punctuation") || value.StartsWith("r12")) return "punctuation";
        if (value.Contains("address") || value.Contains("salutation") || value.Contains("layout") || value.Contains("blank")
            || value.Contains("no_brackets") || value.Contains("signoff") || value.Contains("dob_age")) return "layout_format";
        return "language";
    }
}
