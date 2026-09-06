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
        // "transferred to" / "transfer to" cover real transfer phrasing
        // ("...where he will be transferred to for rehabilitation...") that
        // the narrower phrases miss.
        AddSignal(task, candidates, evidence, "transfer", ["transfer of care", "transfer letter", "transferring care", "transferred to", "transfer to"]);
        AddSignal(task, candidates, evidence, "non_medical_referral", ["occupational therapist", "physiotherapist", "physical therapist", "social worker", "psychologist", "dietitian", "dietician", "speech pathologist", "speech therapist", "speech and language therapist", "speech & language therapist", "podiatrist", "audiologist"]);
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
        // The admin-authored catalogue letter type is authoritative: when the
        // task text corroborates the configured type, background mentions
        // (e.g. "after discharge" inside a transfer letter, "admission" inside
        // a transfer request) are context, not a classification conflict.
        // Conflict fires only when the configured type has no support in the
        // text while other signals disagree.
        var normalizedConfiguredEarly = NormalizeLetterType(configuredLetterType);
        var configuredCorroborated = normalizedConfiguredEarly switch
        {
            "urgent_referral" => candidates.Contains("urgent_referral", StringComparer.Ordinal),
            "discharge" or "transfer" or "non_medical_referral"
                => candidates.Contains(normalizedConfiguredEarly, StringComparer.Ordinal),
            _ => false,
        };
        var conflicting = !configuredCorroborated
            && (distinctStructural.Length > 1
                || (distinctStructural.Length == 1 && candidates.Contains("urgent_referral", StringComparer.Ordinal)));
        if (conflicting)
        {
            return new WritingTaskUnderstandingResult(
                "requires_review",
                null,
                "low",
                evidence,
                RecipientCategory(task, notes),
                urgent ?? string.Empty,
                task,
                DiagnosisOrPlan(task, notes),
                true);
        }

        var normalizedConfigured = NormalizeLetterType(configuredLetterType);
        // A corroborated admin classification wins over heuristic signal
        // order (e.g. a transfer letter mentioning "discharge" classifies as
        // transfer, not whichever keyword appeared first).
        var primary = configuredCorroborated
            ? normalizedConfiguredEarly
            : candidates.FirstOrDefault()
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
            RecipientCategory(task, notes),
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

    private static string RecipientCategory(string task, string notes)
    {
        var fromTask = MatchRecipientCategory(task);
        if (!string.Equals(fromTask, "unknown", StringComparison.Ordinal))
        {
            return fromTask;
        }

        var planOrReferral = ExtractPlanOrReferralLines(notes);
        if (!string.IsNullOrWhiteSpace(planOrReferral))
        {
            var fromPlan = MatchRecipientCategory(planOrReferral);
            if (!string.Equals(fromPlan, "unknown", StringComparison.Ordinal))
            {
                return fromPlan;
            }
        }

        var directCues = ExtractDirectReferralCues(notes);
        if (!string.IsNullOrWhiteSpace(directCues))
        {
            var fromCues = MatchRecipientCategory(directCues);
            if (!string.Equals(fromCues, "unknown", StringComparison.Ordinal))
            {
                return fromCues;
            }
        }

        return "unknown";
    }

    private static string MatchRecipientCategory(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "unknown";

        if (Regex.IsMatch(text, @"\b(?:emergency|ED|casualty)\s+registrar\b", RegexOptions.IgnoreCase)) return "emergency_registrar";
        if (Regex.IsMatch(text, @"\b(?:community|district|home[- ]care|home[- ]health|visiting|domiciliary)\s+(?:health\s+)?nurse\b", RegexOptions.IgnoreCase)) return "community_nurse";
        if (Regex.IsMatch(text, @"\b(?:nurse(?:\s+practitioner)?|charge\s+nurse|nurse[- ]in[- ]charge|registered\s+nurse|practice\s+nurse|triage\s+nurse|nurse\s+unit\s+manager|nurse\s+manager|nursing\s+sister|sister[- ]in[- ]charge|matron|head\s+nurse|staff\s+nurse|clinical\s+nurse\s+specialist|theatre\s+nurse|scrub\s+nurse|school\s+nurse|maternal\s+(?:and|&)\s+child\s+health\s+nurse|child\s+health\s+nurse|infant\s+health\s+nurse|wound\s+care\s+nurse|stoma\s+care\s+nurse|palliative\s+care\s+nurse|mental\s+health\s+nurse|psychiatric\s+nurse|diabetic\s+nurse\s+specialist|diabetes\s+nurse(?:\s+specialist)?|macmillan\s+nurse|breast\s+care\s+nurse|midwife|community\s+midwife|charge\s+midwife|health\s+visitor)\b", RegexOptions.IgnoreCase)) return "nurse";
        if (Regex.IsMatch(text, @"\bSister\s+[A-Z][a-z]+", RegexOptions.IgnoreCase)) return "nurse";
        if (Regex.IsMatch(text, @"\boccupational\s+therapist\b", RegexOptions.IgnoreCase)) return "occupational_therapist";
        if (Regex.IsMatch(text, @"\b(?:physiotherapist|physical\s+therapist|PT|social\s+worker|medical\s+social\s+worker|psychologist|clinical\s+psychologist|psychotherapist|counsellor|counselor|dietitian|dietician|nutritionist|speech\s+pathologist|speech\s+therapist|speech\s+(?:and|&)\s+language\s+therapist|SALT|podiatrist|chiropodist|audiologist|optometrist|optician|pharmacist|clinical\s+pharmacist|community\s+pharmacist|care\s+manager|care\s+facility\s+manager|case\s+manager|care\s+coordinator|welfare\s+officer|social\s+services|child\s+protection\s+officer|chiropractor|osteopath|radiographer|sonographer|paramedic)\b", RegexOptions.IgnoreCase)) return "non_medical_professional";
        if (Regex.IsMatch(text, @"(?:\bGP\b|\bG\.P\.(?!\w)|\bgeneral\s+practitioner\b|\bfamily\s+physician\b|\bfamily\s+doctor\b|\bprimary\s+care\s+physician\b)", RegexOptions.IgnoreCase)) return "gp";
        if (Regex.IsMatch(text, @"\b(?:admissions?\s+officer|admitting\s+officer|intake\s+officer|triage\s+officer)\b", RegexOptions.IgnoreCase)) return "admissions_officer";
        if (Regex.IsMatch(text, @"\b(?:Dr|doctor|consultant|clinician|physician|surgeon|specialist|registrar|resident|intern|medical\s+officer|senior\s+house\s+officer|SHO|house\s+officer|foundation\s+doctor|FY[12]|medical\s+superintendent|cardiologist|endocrinologist|neurologist|dermatologist|rheumatologist|oncologist|paediatrician|pediatrician|psychiatrist|ophthalmologist|gastroenterologist|nephrologist|urologist|radiologist|hematologist|haematologist|pathologist|geriatrician|gerontologist|orthop[ae]dist|anaesthetist|anesthesiologist|orthop[ae]dic\s+surgeon|general\s+surgeon|neurosurgeon|cardiothoracic\s+surgeon|plastic\s+surgeon|ENT\s+specialist|otolaryngologist|obstetrician|gyn[ae]cologist|intensivist|hospitalist|respiratory\s+physician|attending\s+physician|dentist|dental\s+surgeon|orthodontist|periodontist|endodontist|prosthodontist|oral\s+(?:and\s+maxillofacial\s+)?surgeon|veterinarian|veterinary\s+surgeon|vet)\b", RegexOptions.IgnoreCase)) return "named_or_unnamed_clinician";
        if (Regex.IsMatch(text, @"\b(?:emergency\s+department|emergency\s+room|casualty|A&E|ED)\b", RegexOptions.IgnoreCase)) return "emergency_department";
        // High-specificity destination markers: these nouns essentially only
        // appear as the letter's destination (hospice / foundation / board /
        // school / retirement-home / child-health phrasing), including
        // OCR-degraded prompts where the "write to" scaffolding is damaged.
        if (Regex.IsMatch(text, @"\b(?:hospice|palliative\s+care|heart\s+foundation|pharmacy\s+board|primary\s+school|retirement\s+home|child\s+health|data\s+bank|information\s+section)\b", RegexOptions.IgnoreCase)) return "health_facility";
        if (Regex.IsMatch(text, @"\b(?:to|address(?:ing)?\s+(?:(?:a|the)\s+)?(?:letter\s+)?to)\s+(?:the\s+)?(?:(?:director|head|chief|manager|president|chair(?:man|person)?|coordinator|secretary|registrar)\s+of\s+(?:the\s+)?)?(?:(?!at\b)[\w\.'\-]+\s+){0,6}(?:clinic|hospital|centre|center|facility|department|ward|unit|practice|nursing\s+home|care\s+home|retirement\s+home|hospice|rehab(?:ilitation)?\s+centre|team|service|school|foundation|board|association|institute|authority|section)\b", RegexOptions.IgnoreCase)) return "health_facility";
        // Family / lay recipients: parents, guardians, next of kin, carers.
        if (Regex.IsMatch(text, @"\b(?:parents?|guardian|next\s+of\s+kin|family|daughter|son|carer|caregiver|care-giver|spouse)\b", RegexOptions.IgnoreCase)) return "family";
        if (Regex.IsMatch(text, @"\b(?:Mr|Mrs|Ms|Miss|Prof|Professor)\.?\s+[A-Z][a-z]+", RegexOptions.IgnoreCase)) return "named_recipient";
        // OCR tolerance: scanned prompts often render "a" as "@".
        if (Regex.IsMatch(text, @"(?i:\b(?:write|address(?:ing)?|send|refer(?:ral)?)\s+(?:(?:a|the|an|@)\s+)?(?:(?:urgent|routine|formal|discharge|transfer|referral)\s+)?(?:letter\s+(?:of\s+\w+\s+)?)?to\s+(?:the\s+)?(?!(?:the\s+|this\s+|that\s+)?(?:patient|hospital|clinic|centre|center|ward|department)\b))(?-i:[A-Z][a-z]+(?:\s+[A-Z][a-z]+)+)")) return "named_recipient";

        return "unknown";
    }

    private static string ExtractPlanOrReferralLines(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return string.Empty;

        var lines = notes.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var captured = new List<string>();
        var capturing = false;

        var headerRegex = new Regex(
            @"^\s*(?:plan|discharge\s*plan|treatment\s*plan|action\s*plan|management(?:\s*plan)?|further\s*management|recommendations?|assessment\s+and\s+plan|referral(?:\s*letter)?|refer(?:ral)?\s*to|write\s*to|address\s*(?:the\s*letter)?\s*to)\s*[:\-]?(?<inline>.*)$",
            RegexOptions.IgnoreCase);

        var sectionStopRegex = new Regex(
            @"^\s*(?:patient(?:\s*details)?|name|dob|d\.o\.b|address|social\s*history|past\s*medical(?:\s*history)?|medical\s*history|family\s*history|medications?|current\s*drugs|examination|subjective|objective|assessment|diagnosis|notes|writing\s*task)\s*[:\-]",
            RegexOptions.IgnoreCase);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (capturing)
            {
                if (sectionStopRegex.IsMatch(trimmed))
                {
                    break;
                }

                if (trimmed.Length > 0)
                {
                    captured.Add(trimmed.TrimStart('-', '*', '•', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', ' '));
                }
                else if (captured.Count > 0)
                {
                    break;
                }
            }
            else
            {
                var match = headerRegex.Match(trimmed);
                if (match.Success)
                {
                    capturing = true;
                    var inline = match.Groups["inline"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(inline))
                    {
                        captured.Add(inline);
                    }
                }
            }
        }

        return string.Join(" ", captured);
    }

    private static string ExtractDirectReferralCues(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return string.Empty;

        var matches = Regex.Matches(
            notes,
            @"(?im)\b(?:refer(?:ral)?(?:\s+letter)?\s+(?:the\s+patient\s+)?to|write\s+(?:a\s+letter\s+)?to|send\s+(?:a\s+letter\s+)?to)\s+(?<target>[^\r\n,.]+)");

        if (matches.Count > 0)
        {
            return string.Join(" ", matches.Select(m => m.Groups["target"].Value));
        }

        return string.Empty;
    }

    private static string DiagnosisOrPlan(string task, string notes)
    {
        var diagnosis = Regex.Match(task, @"\b(?:diagnosis|diagnosed with|condition)\s*[:\-]?\s*(?<value>[^.\n]+)", RegexOptions.IgnoreCase);
        if (diagnosis.Success) return diagnosis.Groups["value"].Value.Trim();

        var sourceDiagnosis = Regex.Match(notes, @"(?im)^\s*(?:diagnosis|diagnosed with|condition|assessment|impression|presenting\s+complaint|reason\s+for\s+referral)\s*[:\-]\s*(?<value>[^\r\n]+)");
        if (sourceDiagnosis.Success) return sourceDiagnosis.Groups["value"].Value.Trim();

        var plan = Regex.Match(notes, @"(?im)^\s*(?:plan|management|discharge\s*plan|treatment\s*plan|further\s*management|recommendations?)\s*[:\-]?\s*(?<value>[^\r\n]+)");
        if (plan.Success && !string.IsNullOrWhiteSpace(plan.Groups["value"].Value))
            return plan.Groups["value"].Value.Trim();

        // Mid-sentence clinical evidence: real case notes state diagnoses
        // inline ("His diagnosis is Guillain-Barré Syndrome", "Admission
        // diagnosis: multiple leg injuries") rather than under line-anchored
        // headers, so the anchored patterns above miss them.
        var inlineClinical = FindInlineClinicalEvidence(notes);
        if (inlineClinical is not null)
            return inlineClinical;

        var planSection = ExtractPlanOrReferralLines(notes);
        if (!string.IsNullOrWhiteSpace(planSection))
            return planSection.Trim();

        // Broadened final fallback (root-cause fix for diagnosis_or_request_unresolved):
        // the earlier vocabulary only covered clinical-referral phrasing and missed
        // real task prompts written as advice/administrative/non-medical requests
        // (e.g. "provide advice on...", "arrange a home visit", "explain the
        // results", "advise on medication management").
        // "referral" (the OET staple "write a letter of referral"), stated
        // concerns, evaluations, outlines, reports and information purposes
        // all count as clinical purpose/request evidence.
        return Regex.IsMatch(
                task,
                @"\b(?:request(?:ing|ed)?|refer(?:ring|ral)?|review|assess(?:ment)?|manage(?:ment)?|follow[- ]?up|admit|wound|care|dressing|treatment|discharge|transfer" +
                    @"|advis(?:e|ing)|advice|inform(?:ation|ing)?|explain(?:ing)?|arrange(?:ment|ing)?|organis(?:e|ing)|organiz(?:e|ing)|provide|providing|book(?:ing)?|schedul(?:e|ing)" +
                    @"|investigat(?:e|ing|ion)|monitor(?:ing)?|prescrib(?:e|ing)|dispens(?:e|ing)|counsel(?:l?ing)?|educat(?:e|ing|ion)|support|assist(?:ance|ing)?|coordinat(?:e|ing|ion)" +
                    @"|recommend(?:ing|ation)?|address(?:ing)?|update|inquir(?:e|y|ing)|clarif(?:y|ying)|escalat(?:e|ing)|liais(?:e|on|ing)|notify|notification" +
                    @"|concern(?:s)?|evaluation|evaluate|outlin(?:e|ing)|report(?:ing)?|suspect(?:ed)?)\b",
                RegexOptions.IgnoreCase)
            ? task
            : string.Empty;
    }

    /// <summary>
    /// Finds clinical purpose evidence stated mid-sentence in case notes:
    /// inline diagnoses, transfer/referral reasons, suspected adverse drug
    /// reactions, perinatal/palliative context, and scheduled clinical
    /// follow-up. Returns a trimmed evidence excerpt, else null.
    /// </summary>
    private static string? FindInlineClinicalEvidence(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return null;
        var match = Regex.Match(
            notes,
            @"(?i)(?:diagnos(?:is|ed|tic)[^.\n]{0,120}|differential[^.\n]{0,120}|aetiolog[^.\n]{0,120}|etiolog[^.\n]{0,120}|reason\s+for\s+(?:transfer|referral)[^.\n]{0,120}|adverse\s+drug\s+reaction[^.\n]{0,120}|\bsuspected\s+\w+[^.\n]{0,80}|\bADR\b[^.\n]{0,120}|side\s+effect[^.\n]{0,120}|postpartum[^.\n]{0,120}|postnatal[^.\n]{0,120}|antenatal[^.\n]{0,120}|palliat[^.\n]{0,120}|check[- ]?up[^.\n]{0,120}|follow[- ]?up\s+(?:visit|appointment|review|care)[^.\n]{0,120})");
        if (!match.Success) return null;
        var value = match.Value.Trim();
        return value.Length > 200 ? value[..200] : value;
    }

    // Canonical pack vocabulary shared with grading preflight: LT-* catalogue
    // codes (e.g. LT-DG) resolve to the same tokens the signal matchers use
    // (e.g. discharge) instead of falling through to the routine default.
    private static string NormalizeLetterType(string? value)
        => WritingLetterTypeTaxonomy.ToPackLetterType(value);
}
