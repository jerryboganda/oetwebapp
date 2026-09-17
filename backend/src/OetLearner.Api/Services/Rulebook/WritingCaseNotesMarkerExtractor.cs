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
            //
            // False-positive fix (2026-09-18, nursing cross-profession repair): a bare
            // "patient requested" fired on ANY topic, so a note reading "patient requested
            // information on simple low-fat recipes for home" marked the whole letter as a
            // patient-INITIATED REFERRAL. DetectClosurePatientRequest then demanded "upon his
            // request" in the closure, which forced an unsupported claim — the patient had asked
            // about recipes, not about the referral being written. The request now only counts
            // when what was requested is the referral/appointment/opinion itself. This marker has
            // exactly one consumer (DetectClosurePatientRequest), so narrowing it can only remove
            // a demand, never raise a new finding on an already-passing letter.
            PatientInitiatedReferral: Regex.IsMatch(text,
                @"\b(?:(?:the )?patient|he|she) (?:requested|asked for) (?:a |an |this )?(?:referral|second opinion|opinion|specialist (?:review|opinion|assessment|appointment)|review|assessment|appointment|consultation|transfer)\b"
                + @"|\b(?:(?:the )?patient|he|she) (?:requested|asked) to (?:be (?:referred|seen|assessed|reviewed)|see)\b"
                + @"|\bupon (?:his|her) request\b"
                + @"|\bat (?:his|her|the patient'?s|patient'?s|[a-z]+'s) (?:own )?request\b"),
            ConsentDocumented: Regex.IsMatch(text, @"\b(consent|fully informed|discussed with patient|safety plan completed)\b"),
            FollowUpDate: followUpDate,
            ResultsEnclosed: Regex.IsMatch(text, @"\b(enclosed|attached|please find enclosed|copy of results|copy of imaging)\b"),
            // Ultimate Final §3.3 / OA2-02 — discharge language is only
            // supported when the canonical notes document a genuine
            // admission EPISODE and a genuine discharge EVENT, not a bare
            // mention of a venue or clinical word. The original version
            // matched bare "hospital", "ward", "theatre", "post-operat*" and
            // any "discharg\w*" anywhere in the notes, so an outpatient
            // referral note reading "seen at City Hospital ... discharge
            // advice leaflet given" armed BOTH markers with no admission or
            // discharge ever having happened (proved by the R2-02b fixtures
            // in WritingOwnerAddendumTwoRegressionFixtureTests — that exact
            // sentence).
            // Admission requires an episode phrase (admitted/admission,
            // inpatient status, hospitalised, an actual ward stay, or the
            // existing "under our/the team/care/management" phrase);
            // discharge requires the verb "discharged", a readiness/fitness
            // statement, or a completed discharge document, not any word
            // beginning "discharg" (which also matches "discharge advice",
            // "discharge planning discussed" and "wound discharge").
            AdmissionDocumented: Regex.IsMatch(text, @"\b(admit(?:ted|s|tance)?|admissions?|(?:was |is |as )?an? inpatient|in-patient|hospitalis(?:ed|ation)|on (?:the|a) ward|overnight (?:stay|admission)|under (?:our|the) (?:team|care|management))\b"),
            DischargeDocumented: Regex.IsMatch(text, @"\b(discharged|(?:fit|ready) for discharge|sent home|returning home|returned home|back to (?:your|his|her|their|our) care|transfer(?:red)? of care|discharge (?:summary|letter) (?:issued|completed|sent))\b"));
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
            // The prepositions need a trailing word boundary: without it the
            // "in" of "follow-up INvestigations (bronchoscopy, biopsy)" read as
            // "follow-up in ...", so a planned investigation became a review
            // appointment and closure_mentions_review_if_required held a
            // correct letter (Mrs Mary Clarke, Senior Assessor audit 16 Sep 2026).
            @"\bfollow[- ]?up\b\s*(?::|(?:in|on|at|after|with|scheduled|booked|arranged)\b)\s*([^\n.]+)",
            @"\breview\b\s*(?::|(?:in|on|at|after|scheduled|booked|arranged)\b)\s*([^\n.]+)",
            // A bare "appointment on <date>" is usually the PAST consultation
            // the letter is about, not a future follow-up instruction. The
            // Weston notes record "Outpatient clinic appointment on 10.06.2018"
            // — the visit the letter is written about — and that set the
            // marker, forcing closure_mentions_review_if_required onto a letter
            // whose task asks for no review at all (Addendum Two, Weston E). A
            // planning word must introduce it, or the clause must itself be
            // forward-looking ("appointment at 6 weeks", "made for 7/9/18").
            @"\b(?:follow[- ]?up|review|next|further|planned|booked|scheduled|arranged|specialist)\s+(?:appointment|clinic visit)\b\s*(?::|(?:on|in|at|for)\b)\s*([^\n.]+)",
            @"\b(?:appointment|clinic visit)\b\s*(?::|(?:in|at|scheduled|booked|arranged|made for)\b)\s*([^\n.]+)",
        })
        {
            var match = Regex.Match(caseNotes, pattern, RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value.Trim();
        }

        return null;
    }
}