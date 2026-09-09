using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Companion;

/// <summary>
/// Screens text for identifying detail before it is written to durable storage.
///
/// <para>
/// Candidates practising OET write about real patients, because that is what
/// they have to hand. The prompt already tells the companion not to repeat such
/// detail back — but a prompt is guidance, and a saved note is a database row
/// that outlives the conversation, gets re-read into later turns, and appears in
/// an export. Testing Pack 3 scenario 16 targets this exact path, and the
/// tool that writes notes had no check at all.
/// </para>
///
/// <para>
/// <b>Deliberately conservative about what it claims.</b> This finds patterns —
/// national identifiers, medical record numbers, dates of birth, phone numbers,
/// email addresses — not "personal data" in the legal sense. It cannot tell a
/// real patient's name from a textbook one and does not try, because a name
/// detector on clinical text would reject almost every legitimate note. The
/// answer to a hit is to refuse the save and tell the learner to anonymise,
/// which is safe when it is wrong: the cost is retyping a note, and the cost of
/// the opposite mistake is storing a real patient's identifiers.
/// </para>
/// </summary>
public static class CompanionPiiScreen
{
    private static readonly (string Label, Regex Pattern)[] Patterns =
    [
        ("an email address", new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled)),

        // Long digit runs: NHS numbers, national IDs, MRNs, card numbers. Ten or
        // more digits (allowing spaces and dashes as separators) is well past
        // anything a clinical note needs — doses, times and ages are short.
        ("a long identification number", new Regex(@"\b(?:\d[ -]?){10,}\b", RegexOptions.Compiled)),

        ("a labelled record or patient number", new Regex(
            @"\b(?:MRN|NHS|SSN|NRIC|hospital\s+(?:no|number)|patient\s+(?:id|no|number)|medicare|passport)\b\s*[:#]?\s*[A-Za-z0-9-]{4,}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase)),

        ("a date of birth", new Regex(
            @"\b(?:d\.?o\.?b\.?|date\s+of\s+birth)\b\s*[:\-]?\s*\d{1,4}[/\-.]\d{1,2}[/\-.]\d{1,4}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase)),

        ("a phone number", new Regex(@"(?:\+\d{1,3}[ -]?)?(?:\(\d{2,4}\)[ -]?)?\d{3,4}[ -]\d{3,4}[ -]?\d{0,4}", RegexOptions.Compiled)),

        ("a street address", new Regex(
            @"\b\d{1,5}\s+[A-Za-z][A-Za-z.'-]*(?:\s+[A-Za-z][A-Za-z.'-]*)*\s+(?:street|st|road|rd|avenue|ave|lane|ln|drive|dr|close|court|ct|boulevard|blvd)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase)),
    ];

    /// <summary>
    /// Returns a description of what was found, or <c>null</c> when the text is
    /// safe to persist. The description names the <i>kind</i> of detail and never
    /// echoes the value, so the finding can be logged and shown to the learner
    /// without copying the identifier into a second place.
    /// </summary>
    public static string? FindIdentifyingDetail(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        foreach (var (label, pattern) in Patterns)
        {
            if (pattern.IsMatch(text)) return label;
        }

        return null;
    }

    public static bool ContainsIdentifyingDetail(string? text) =>
        FindIdentifyingDetail(text) is not null;
}
