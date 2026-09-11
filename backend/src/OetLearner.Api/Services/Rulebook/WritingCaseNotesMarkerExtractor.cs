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
            // "allerg" alone could never match "allergy"/"allergic" (\b after it).
            AllergyMentioned: Regex.IsMatch(text, @"\b(allerg\w*|nkda|nka)\b"),
            AtopicCondition: Regex.IsMatch(text, @"\b(asthma|eczema|hay fever|allergic rhinitis|atopic)\b"),
            // "at .* request" matched any "at" followed anywhere later on the
            // line by "request"; only a genuine patient-initiated request counts.
            PatientInitiatedReferral: Regex.IsMatch(text, @"\b(patient requested|(?:he|she) requested (?:a )?referral|upon (his|her) request|at (?:his|her|the patient'?s|patient'?s|[a-z]+'s) (?:own )?request)\b"),
            ConsentDocumented: Regex.IsMatch(text, @"\b(consent|fully informed|discussed with patient|safety plan completed)\b"),
            FollowUpDate: followUpDate,
            ResultsEnclosed: Regex.IsMatch(text, @"\b(enclosed|attached|please find enclosed|copy of results|copy of imaging)\b"));
    }

    private static string? ExtractFollowUp(string? caseNotes)
    {
        if (string.IsNullOrWhiteSpace(caseNotes)) return null;
        // Only an explicit FUTURE follow-up instruction ("Follow-up: 2 weeks",
        // "review in 6 weeks", "appointment on 22 August") sets the marker.
        // The old patterns matched ANY "review..." text, including history
        // such as "reviewed on 7 July" or "On review, ...", which forced a
        // false "closure must reference the review" finding onto correct
        // candidate letters and Model Answers alike (Addendum Rev8 §19.4:
        // never fabricate violations for correct wording).
        foreach (var pattern in new[]
        {
            @"\bfollow[- ]?up\b\s*(?::|in|on|at|after|with|scheduled|booked|arranged)\s*([^\n.]+)",
            @"\breview\b\s*(?::|in|on|at|after|scheduled|booked|arranged)\s*([^\n.]+)",
            @"\b(?:appointment|clinic visit)\b\s*(?::|on|in|at|scheduled|booked|arranged)\s*([^\n.]+)",
        })
        {
            var match = Regex.Match(caseNotes, pattern, RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value.Trim();
        }

        return null;
    }
}