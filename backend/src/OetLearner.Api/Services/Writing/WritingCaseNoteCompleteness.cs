using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Writing;

/// <summary>One task the case-note audit flagged: the codes and their admin-facing sentences.</summary>
public sealed record WritingCaseNoteAuditRow(
    Guid ScenarioId,
    string? InternalCode,
    string Title,
    string Profession,
    string LetterType,
    string Status,
    int CaseNoteRowCount,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Messages);

/// <summary><see cref="Scanned"/> tasks were checked in <see cref="Scope"/> (published, draft, archived or all);
/// <see cref="Flagged"/> of them carry at least one warning and are listed in <see cref="Rows"/>.</summary>
public sealed record WritingCaseNoteAuditReport(
    DateTimeOffset GeneratedAt,
    string Scope,
    int Scanned,
    int Flagged,
    IReadOnlyList<WritingCaseNoteAuditRow> Rows);

/// <summary>
/// Advisory completeness check on a Writing task's STORED case-note rows (owner directive 9 Oct 2026, after the
/// Physiotherapy "DOB / knee flexion not in the notes" incident: the stored rows had silently lost the DOB and the
/// initial-assessment range-of-movement values). It cannot see the source PDF, so every code is a "look at this" signal for
/// an admin, never a block: a task whose source genuinely has no DOB is not wrong. The publish gate shows these as
/// warnings and the case-note audit lists them across the catalogue.
/// Pure: no I/O, never throws.
/// </summary>
public static class WritingCaseNoteCompleteness
{
    public const string NoDob = "case_notes_no_dob";
    public const string DobLabelWithoutDate = "case_notes_dob_label_without_date";
    public const string NoDates = "case_notes_no_dates";
    public const string NoDegreeValues = "case_notes_no_degree_values";
    public const string RomLabelWithoutValue = "case_notes_rom_label_without_value";
    public const string OcrArtifacts = "case_notes_ocr_artifacts";
    public const string VeryShort = "case_notes_very_short";
    public const string RowCapReached = "case_notes_row_cap_reached";

    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly Regex DobLabel = new(@"\b(?:d\.?\s?o\.?\s?b\.?|date\s+of\s+birth|birth\s*date|born)\b", Opts);

    private static readonly Regex AnyDate = new(
        @"\b\d{1,2}\s*[./-]\s*\d{1,2}\s*[./-]\s*\d{2,4}\b"
        + @"|\b\d{1,2}(?:st|nd|rd|th)?\s*[-/.]?\s*(?:jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|june?|july?|aug(?:ust)?|sept?(?:ember)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)\.?\s*[-/.,]?\s*\d{2,4}\b"
        + @"|\b(?:jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|june?|july?|aug(?:ust)?|sept?(?:ember)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)\.?\s+\d{1,2}(?:st|nd|rd|th)?,?\s+\d{4}\b",
        Opts);

    private static readonly Regex RomLabel = new(
        @"\b(?:flexion|extension|abduction|adduction|dorsiflexion|plantarflexion|rotation|range\s+of\s+(?:movement|motion)|rom|goniomet\w*)\b", Opts);

    private static readonly Regex DegreeValue = new(@"\d\s*(?:°|º|˚|degrees?\b|deg\b)", Opts);

    private static readonly Regex OcrArtifact = new(@"\\circ|\^\s*\{|\$\s*\d|\d\s*\$|^\s*\|", Opts);

    /// <summary>Codes for the stored rows of one task; empty when nothing looks lost. <paramref name="profession"/> is the
    /// scenario's profession (any spelling).</summary>
    public static IReadOnlyList<string> Warnings(IReadOnlyList<string> sentences, string? profession)
    {
        var warnings = new List<string>();
        try
        {
            var lines = sentences
                .Select(s => (s ?? string.Empty).Trim())
                .Where(s => s.Length > 0)
                .ToList();
            if (lines.Count == 0) return warnings; // an empty task is already a blocking code elsewhere

            var text = string.Join("\n", lines);
            if (lines.Count < 6 || text.Length < 200) warnings.Add(VeryShort);
            if (sentences.Count >= 500) warnings.Add(RowCapReached);
            if (lines.Any(l => OcrArtifact.IsMatch(l))) warnings.Add(OcrArtifacts);

            // A DOB label whose value is missing: the label survived extraction, the date did not.
            var hasDobLabel = false;
            for (var i = 0; i < lines.Count; i++)
            {
                if (!DobLabel.IsMatch(lines[i])) continue;
                hasDobLabel = true;
                var near = lines[i] + " " + (i + 1 < lines.Count ? lines[i + 1] : string.Empty);
                if (!AnyDate.IsMatch(near)) warnings.Add(DobLabelWithoutDate);
            }

            // No DOB at all: either the source has none (age only) or the line was lost. Only the PDF can tell.
            if (!hasDobLabel) warnings.Add(NoDob);
            if (!AnyDate.IsMatch(text)) warnings.Add(NoDates);

            // A range-of-movement label with no number on it or the next two lines.
            for (var i = 0; i < lines.Count; i++)
            {
                if (!RomLabel.IsMatch(lines[i])) continue;
                var near = string.Join(" ", lines.Skip(i).Take(3));
                if (!near.Any(char.IsDigit))
                {
                    warnings.Add(RomLabelWithoutValue);
                    break;
                }
            }

            var normalised = (profession ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
            if (normalised is "physiotherapy" or "occupational_therapy" && !DegreeValue.IsMatch(text))
            {
                warnings.Add(NoDegreeValues);
            }
        }
        catch (Exception)
        {
            // Advisory only: a failure here must never block a publish or the audit.
        }

        return warnings.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>The admin-facing sentence for a code.</summary>
    public static string Message(string code) => code switch
    {
        NoDob => "No date-of-birth line found in the stored case notes. If the source PDF has one, it was lost in extraction: repair the case notes before publishing.",
        DobLabelWithoutDate => "A date-of-birth label has no date beside it: the value was probably lost in extraction.",
        NoDates => "The stored case notes contain no date at all. Check the source PDF.",
        NoDegreeValues => "No degree values found for a range-of-movement profession. If the source records joint angles, they were lost in extraction.",
        RomLabelWithoutValue => "A range-of-movement label (flexion, extension, ROM ...) has no number beside it: the value was probably lost in extraction.",
        OcrArtifacts => "The stored case notes contain OCR or markup debris ($, \\circ, table pipes). Check that values read correctly.",
        VeryShort => "The stored case notes are very short for a Writing task. The extraction may be incomplete.",
        RowCapReached => "The extraction hit the 500-row storage cap and was truncated.",
        _ => code,
    };
}
