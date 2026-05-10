using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Rulebook;

public static class WritingCaseNotesMarkerExtractor
{
    public static WritingCaseNotesMarkers Derive(string? caseNotes)
    {
        var text = (caseNotes ?? string.Empty).ToLowerInvariant();
        var followUpDate = ExtractFollowUp(caseNotes);

        return new WritingCaseNotesMarkers(
            SmokingMentioned: Regex.IsMatch(text, @"smok|cigarette|tobacco"),
            DrinkingMentioned: Regex.IsMatch(text, @"\b(alcohol|drink(s|ing)?|units per week)\b"),
            AllergyMentioned: Regex.IsMatch(text, @"\b(allerg|nkda|nka)\b"),
            AtopicCondition: Regex.IsMatch(text, @"\b(asthma|eczema|hay fever|allergic rhinitis|atopic)\b"),
            PatientInitiatedReferral: Regex.IsMatch(text, @"\b(patient requested|upon (his|her) request|at .* request)\b"),
            ConsentDocumented: Regex.IsMatch(text, @"\b(consent|fully informed|discussed with patient|safety plan completed)\b"),
            FollowUpDate: followUpDate,
            ResultsEnclosed: Regex.IsMatch(text, @"\b(enclosed|attached|please find enclosed|copy of results|copy of imaging)\b"));
    }

    private static string? ExtractFollowUp(string? caseNotes)
    {
        if (string.IsNullOrWhiteSpace(caseNotes)) return null;
        foreach (var pattern in new[]
        {
            @"follow[- ]?up\s*:?\s*([^\n.]+)",
            @"review\s*:?\s*([^\n.]+)",
            @"appointment\s*:?\s*([^\n.]+)",
        })
        {
            var match = Regex.Match(caseNotes, pattern, RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value.Trim();
        }

        return null;
    }
}