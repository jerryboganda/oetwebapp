using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingFactEvidence(
    string FactText,
    string SourceReference,
    string Classification,
    string CandidateStatus,
    string? CandidateExcerpt,
    string Explanation);

public sealed record WritingFactMap(IReadOnlyList<WritingFactEvidence> Facts)
{
    public IReadOnlyList<WritingFactEvidence> RequiredFacts
        => Facts.Where(x => x.Classification == "required").ToArray();
}

/// <summary>
/// Conservative case-note map. It only treats text from the supplied case notes
/// as factual evidence; candidate-only clinical claims are classified as
/// invented rather than being added to the fact map.
/// </summary>
public static class WritingFactMapService
{
    // Source grounding: a claim is "invented" only when the letter says it and NONE of the forms the case notes use for
    // it is present. Word-bounded ("hives" is not HIV) and synonym-aware ("diabetic", "T2DM", "CVA", "seizures", "LMP").
    private static readonly (string Claim, Regex InLetter, Regex InSource)[] ClinicalClaims =
    [
        ("diabetes", Rx(@"\bdiabet\w*"), Rx(@"\bdiabet\w*|\bt[12]dm\b|\bniddm\b|\biddm\b")),
        ("cancer", Rx(@"\bcancer\w*"), Rx(@"\bcancer\w*|carcinoma|\bmalignan\w*|\bneoplas\w*")),
        ("hiv", Rx(@"\bhiv\b"), Rx(@"\bhiv\b")),
        ("pregnancy", Rx(@"\bpregnan\w*"), Rx(@"\bpregnan\w*|\bgravid\w*|\bantenatal\b|\bgestation\w*")),
        ("renal failure", Rx(@"\brenal\s+failure\b"), Rx(@"\brenal\s+(?:failure|impairment|insufficiency)\b|\bkidney\s+(?:failure|disease)\b|\bckd\b|\besrf\b")),
        ("stroke", Rx(@"\bstroke\b"), Rx(@"\bstroke\b|\bcva\b|cerebrovascular")),
        ("epilepsy", Rx(@"\bepilep\w*"), Rx(@"\bepilep\w*|\bseizure\w*|\bconvuls\w*")),
        ("myocardial infarction", Rx(@"\bmyocardial\s+infarct\w*"), Rx(@"\bmyocardial\s+infarct\w*|\bstemi\b|\bnstemi\b|\bheart\s+attack\b")),
        ("heart attack", Rx(@"\bheart\s+attack\b"), Rx(@"\bheart\s+attack\b|\bmyocardial\s+infarct\w*|\bstemi\b|\bnstemi\b")),
    ];

    private static Regex Rx(string pattern)
        => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] RequiredWords =
    ["diagnosis", "symptom", "finding", "management", "medicine", "medication", "treatment", "follow", "referral", "request", "allergy", "asthma", "eczema", "hay fever", "smoking", "alcohol", "drink"];

    public static WritingFactMap Build(
        string caseNotes,
        string candidateLetter,
        string recipientCategory,
        string? letterType = null)
    {
        var source = caseNotes ?? string.Empty;
        var candidate = candidateLetter ?? string.Empty;
        var facts = new List<WritingFactEvidence>();
        var sourceLines = source.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var index = 0; index < sourceLines.Length; index++)
        {
            var factText = sourceLines[index].Trim();
            if (factText.Length == 0) continue;
            var classification = ClassifyFact(factText, recipientCategory, letterType);
            var included = CandidateIncludesFact(factText, candidate);
            var status = classification == "excluded"
                ? included ? "irrelevant" : "not_required"
                : included
                    ? "included_accurately"
                    : classification == "required" ? "missing" : "not_required";
            facts.Add(new WritingFactEvidence(
                factText,
                $"case-note-line:{index + 1}",
                classification,
                status,
                included ? FindCandidateExcerpt(factText, candidate) : null,
                included ? "The candidate included a matching case-note fact." : "The candidate did not include this fact."));
        }

        var atopicCondition = Regex.IsMatch(source, @"\b(?:asthma|eczema|hay fever)\b", RegexOptions.IgnoreCase);
        var hasAllergyFact = Regex.IsMatch(source, @"\ballerg(?:y|ies|ic)\b", RegexOptions.IgnoreCase);
        if (atopicCondition && !hasAllergyFact)
        {
            var included = candidate.Contains("allerg", StringComparison.OrdinalIgnoreCase);
            facts.Add(new WritingFactEvidence(
                "Allergy status (required because an atopic condition is present)",
                "case-note-derived:atopic-condition",
                "required",
                included ? "included_accurately" : "missing",
                included ? FindCandidateExcerpt("allergy", candidate) : null,
                "The specification requires allergy status for asthma, eczema, and hay fever regardless of recipient."));
        }

        foreach (var (claim, inLetter, inSource) in ClinicalClaims)
        {
            if (inSource.IsMatch(source) || !inLetter.IsMatch(candidate)) continue;
            facts.Add(new WritingFactEvidence(
                claim,
                "candidate-letter",
                "prohibited",
                "invented",
                FindCandidateExcerpt(claim, candidate),
                "This clinical claim is not traceable to the supplied case notes."));
        }

        return new WritingFactMap(facts);
    }

    private static string ClassifyFact(string fact, string recipientCategory, string? letterType)
    {
        if (string.Equals(letterType, "discharge", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(fact, @"\bfamily history\b|\bsocial history\b|\bsmoking\b|\balcohol\b|\bdrinking\b|\boccupation\b|\bprevious medical history\b", RegexOptions.IgnoreCase))
            return "excluded";
        if (Regex.IsMatch(fact, @"\bfamily history\b|\boccupation\b|\bsocial history\b", RegexOptions.IgnoreCase))
            return "semi_relevant";
        if (Regex.IsMatch(fact, @"\ballerg(?:y|ies|ic)\b", RegexOptions.IgnoreCase)
            && string.Equals(letterType, "discharge", StringComparison.OrdinalIgnoreCase)
            && !Regex.IsMatch(fact, @"\b(?:treatment|treated|medication|medicine|prescribed|influenced)\b", RegexOptions.IgnoreCase))
            return "semi_relevant";
        if (Regex.IsMatch(fact, @"\bsmoking\b|\balcohol\b|\bdrinking\b", RegexOptions.IgnoreCase)
            && string.Equals(recipientCategory, "occupational_therapist", StringComparison.OrdinalIgnoreCase))
            return "semi_relevant";
        return RequiredWords.Any(word => fact.Contains(word, StringComparison.OrdinalIgnoreCase))
            ? "required"
            : "semi_relevant";
    }

    private static bool CandidateIncludesFact(string fact, string candidate)
    {
        var normalizedFact = fact.ToLowerInvariant();
        if (normalizedFact.Contains("allerg"))
            return candidate.Contains("allerg", StringComparison.OrdinalIgnoreCase);
        var words = Regex.Matches(normalizedFact, @"[a-z]{4,}")
            .Select(match => match.Value)
            // Case-note label words carry no clinical content: a fact whose
            // only remaining keyword is e.g. "asthma" must match a candidate
            // that mentions asthma even without repeating the label.
            .Where(word => word is not ("status" or "negative" or "positive" or "patient" or "history" or "present" or "diagnosis" or "diagnosed"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (words.Length == 0) return candidate.Contains(fact, StringComparison.OrdinalIgnoreCase);
        var hits = words.Count(word => candidate.Contains(word, StringComparison.OrdinalIgnoreCase));
        return hits >= Math.Min(2, words.Length);
    }

    private static string? FindCandidateExcerpt(string fact, string candidate)
    {
        var keyword = Regex.Match(fact, @"[A-Za-z]{4,}").Value;
        if (keyword.Length == 0) return null;
        var match = Regex.Match(candidate, $@"[^.\n]*{Regex.Escape(keyword)}[^.\n]*", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.Trim() : null;
    }
}
