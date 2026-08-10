using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingTaskUnderstandingResult(
    string Status,
    string? PrimaryLetterType,
    string Confidence,
    IReadOnlyList<string> EvidencePhrases,
    string RecipientCategory,
    string UrgencyReason,
    string PurposeOrRequest,
    string DiagnosisOrPlanEvidence,
    bool ConflictingEvidence);

/// <summary>
/// Deterministic task parser used before AI. It records the triggering phrases
/// and returns requires_review when independent letter-type signals conflict.
/// </summary>
public static class WritingTaskUnderstandingService
{
    public static WritingTaskUnderstandingResult Understand(
        string taskText,
        string caseNotes,
        string configuredLetterType)
    {
        var task = (taskText ?? string.Empty).Trim();
        var notes = caseNotes ?? string.Empty;
        var evidence = new List<string>();
        var candidates = new List<string>();

        AddSignal(task, candidates, evidence, "discharge", ["discharge", "being discharged", "hospital-to-gp"]);
        AddSignal(task, candidates, evidence, "transfer", ["transfer of care", "transfer letter", "transferring care"]);
        AddSignal(task, candidates, evidence, "non_medical_referral", ["occupational therapist", "physiotherapist", "social worker", "psychologist", "dietitian"]);
        var urgent = FindSignal(task, ["urgent", "asap", "admission", "acute management", "suspected cancer"]);
        if (urgent is not null)
        {
            candidates.Add("urgent_referral");
            evidence.Add(urgent);
        }

        var distinctStructural = candidates
            .Where(x => x is "discharge" or "transfer" or "non_medical_referral")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var conflicting = distinctStructural.Length > 1
            || (distinctStructural.Length == 1 && candidates.Contains("urgent_referral", StringComparer.Ordinal));
        if (conflicting)
        {
            return new WritingTaskUnderstandingResult(
                "requires_review",
                null,
                "low",
                evidence,
                RecipientCategory(task),
                urgent ?? string.Empty,
                task,
                DiagnosisOrPlan(task, notes),
                true);
        }

        var normalizedConfigured = NormalizeLetterType(configuredLetterType);
        var primary = candidates.FirstOrDefault()
            ?? (normalizedConfigured is "discharge" or "transfer" or "non_medical_referral" or "urgent_referral"
                ? normalizedConfigured
                : "routine_referral");
        if (primary == "routine_referral" && normalizedConfigured == "referral_to_gp")
        {
            primary = "referral_to_gp";
        }
        if (evidence.Count == 0 && primary == "routine_referral")
        {
            evidence.Add("No urgent, discharge, transfer, or non-medical signal; routine referral default.");
        }

        return new WritingTaskUnderstandingResult(
            "classified",
            primary,
            evidence.Count > 0 ? "high" : "medium",
            evidence,
            RecipientCategory(task),
            urgent ?? string.Empty,
            task,
            DiagnosisOrPlan(task, notes),
            false);
    }

    private static void AddSignal(
        string text,
        ICollection<string> candidates,
        ICollection<string> evidence,
        string type,
        IReadOnlyList<string> signals)
    {
        var match = FindSignal(text, signals);
        if (match is null) return;
        candidates.Add(type);
        evidence.Add(match);
    }

    private static string? FindSignal(string text, IReadOnlyList<string> signals)
        => signals.FirstOrDefault(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase));

    private static string RecipientCategory(string task)
    {
        if (Regex.IsMatch(task, @"\bemergency registrar\b", RegexOptions.IgnoreCase)) return "emergency_registrar";
        if (Regex.IsMatch(task, @"\boccupational therapist\b", RegexOptions.IgnoreCase)) return "occupational_therapist";
        if (Regex.IsMatch(task, @"\b(?:physiotherapist|social worker|psychologist|dietitian)\b", RegexOptions.IgnoreCase)) return "non_medical_professional";
        if (Regex.IsMatch(task, @"\b(?:GP|general practitioner)\b", RegexOptions.IgnoreCase)) return "gp";
        if (Regex.IsMatch(task, @"\b(?:admissions officer|admission officer)\b", RegexOptions.IgnoreCase)) return "admissions_officer";
        if (Regex.IsMatch(task, @"\b(?:Dr|doctor|consultant|clinician)\b", RegexOptions.IgnoreCase)) return "named_or_unnamed_clinician";
        return "unknown";
    }

    private static string DiagnosisOrPlan(string task, string notes)
    {
        var diagnosis = Regex.Match(task, @"\b(?:diagnosis|diagnosed with|condition)\s*[:\-]?\s*(?<value>[^.\n]+)", RegexOptions.IgnoreCase);
        if (diagnosis.Success) return diagnosis.Groups["value"].Value.Trim();

        var sourceDiagnosis = Regex.Match(notes, @"(?im)^\s*(?:diagnosis|diagnosed with|condition)\s*:\s*(?<value>[^\r\n]+)");
        if (sourceDiagnosis.Success) return sourceDiagnosis.Groups["value"].Value.Trim();

        var plan = Regex.Match(notes, @"(?im)^\s*plan\s*:\s*(?<value>[^\r\n]+)");
        if (plan.Success) return plan.Groups["value"].Value.Trim();

        return Regex.IsMatch(task, @"\b(?:request(?:ing)?|refer(?:ring)?|review|assess(?:ment)?|manage(?:ment)?|follow[- ]?up|admit)\b", RegexOptions.IgnoreCase)
            ? task
            : string.Empty;
    }

    private static string NormalizeLetterType(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_') switch
        {
            "routine" => "routine_referral",
            "urgent" => "urgent_referral",
            "non_medical" => "non_medical_referral",
            "referral_gp" or "gp" => "referral_to_gp",
            var normalized => normalized,
        };
}
