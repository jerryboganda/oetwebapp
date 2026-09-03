namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Single source of truth for the candidate-facing Writing catalogue letter-type
/// taxonomy. These are the LT-* codes stored on <c>WritingScenario.LetterType</c>
/// and served by the catalogue, practice-library, onboarding-focus, and pathway
/// filters.
/// <para/>
/// Valid catalogue set (2026-09 revision):
/// <list type="bullet">
/// <item>LT-RR Routine referral (kept)</item>
/// <item>LT-UR Urgent referral (kept)</item>
/// <item>LT-DG Discharge (kept)</item>
/// <item>LT-TR Transfer (kept)</item>
/// <item>LT-NM Non-medical (kept where the profession allows it)</item>
/// <item>LT-OT Other Letters (new universal fallback)</item>
/// </list>
/// <para/>
/// Response (LT-RP) is RETIRED and must never be produced by any active
/// classifier, normalizer, default set, import mapping, or AI prompt.
/// Legacy Response-like inputs ("RESPONSE", "UPDATE", "REPLY", "LT-RP") and any
/// other unclear / unsupported / ambiguous value normalise to
/// <see cref="OtherLetters"/> — they are never forced into an incorrect known
/// category.
/// </summary>
public static class WritingLetterTypeTaxonomy
{
    public const string RoutineReferral = "LT-RR";
    public const string UrgentReferral = "LT-UR";
    public const string Discharge = "LT-DG";
    public const string Transfer = "LT-TR";
    public const string NonMedical = "LT-NM";

    /// <summary>
    /// Universal fallback letter type for Writing cases whose letter type
    /// cannot be clearly and confidently placed into a known category.
    /// Candidate-facing label: "Other Letters". Valid under every profession.
    /// </summary>
    public const string OtherLetters = "LT-OT";

    /// <summary>
    /// The complete valid catalogue set. Response (LT-RP) is absent by design.
    /// </summary>
    public static readonly IReadOnlySet<string> ValidCatalogueLetterTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            RoutineReferral,
            UrgentReferral,
            Discharge,
            Transfer,
            NonMedical,
            OtherLetters,
        };

    /// <summary>
    /// True when the value is one of the six valid catalogue codes
    /// (case-insensitive). "All" is a filter option, never a stored value.
    /// </summary>
    public static bool IsValidCatalogueLetterType(string? value)
        => !string.IsNullOrWhiteSpace(value) && ValidCatalogueLetterTypes.Contains(value.Trim());

    /// <summary>
    /// Normalises any legacy / free-form / import letter-type token to a valid
    /// catalogue code. Retired Response-like tokens and anything unrecognised
    /// fall back to <see cref="OtherLetters"/> — never to a guessed known type.
    /// </summary>
    public static string NormalizeCatalogueLetterType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return OtherLetters;
        }

        return value.Trim().ToUpperInvariant() switch
        {
            "LT-RR" or "ROUTINE_REFERRAL" or "REFERRAL" or "ROUTINE" => RoutineReferral,
            "LT-UR" or "URGENT_REFERRAL" or "URGENT" => UrgentReferral,
            "LT-DG" or "DISCHARGE" or "UPDATE_DISCHARGE" or "UPDATE-DISCHARGE" => Discharge,
            "LT-TR" or "TRANSFER" or "TRANSFER_LETTER" or "TRANSFER-LETTER" => Transfer,
            "LT-NM" or "NON_MEDICAL_REFERRAL" or "NON-MEDICAL" or "NON_MEDICAL" => NonMedical,
            // Retired Response family and explicit "other" aliases: deliberate
            // fallback, never a guessed known category.
            "LT-OT" or "OTHER" or "OTHER_LETTERS" or "OTHER-LETTERS" or "OTHER LETTERS"
                or "LT-RP" or "RESPONSE" or "UPDATE" or "REPLY" => OtherLetters,
            _ => OtherLetters,
        };
    }

    /// <summary>
    /// Like <see cref="NormalizeCatalogueLetterType"/> but preserves empty input
    /// as empty so required-field validation can still fire for truly missing
    /// values on authoring DTOs.
    /// </summary>
    public static string NormalizeCatalogueLetterTypeOrEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : NormalizeCatalogueLetterType(value);

    /// <summary>
    /// Maps a normalised catalogue code to the legacy lowercase token used by
    /// older pathway/profile payloads. Response has no legacy token anymore;
    /// anything resolving to <see cref="OtherLetters"/> maps to "other_letters".
    /// </summary>
    public static string ToLegacyLetterType(string? value) => NormalizeCatalogueLetterType(value) switch
    {
        RoutineReferral => "routine_referral",
        UrgentReferral => "urgent_referral",
        Discharge => "discharge",
        Transfer => "transfer_letter",
        NonMedical => "non_medical_referral",
        _ => "other_letters",
    };

    /// <summary>
    /// Maps ANY letter-type token — a modern LT-* catalogue code, a legacy
    /// ContentPaper id (<c>routine_referral</c>, <c>transfer_letter</c>, …), a
    /// rulebook genre token (<c>discharge</c>, <c>transfer</c>, …), or free-form
    /// text — to the canonical assessment-pack token used by the v1.1 grading
    /// release gate (<see cref="WritingAssessmentPackVersion"/>), the task
    /// understanding parser, and canon-rule scoping. Retired Response-like
    /// tokens and anything unrecognised map to <c>other</c> — never to a
    /// guessed known type, and never to the retired <c>advice_to_patient</c>
    /// genre.
    /// <para/>
    /// This is the single vocabulary bridge between the LT-* catalogue (stored
    /// on <c>WritingScenario.LetterType</c>) and the legacy-token pack/rule
    /// stores. Every pack lookup and every letter-type-sensitive grading input
    /// must go through this method so an LT-* task always resolves the same
    /// pack/rules as its legacy-token equivalent.
    /// </summary>
    public static string ToPackLetterType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "other";
        }

        var normalized = value.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized switch
        {
            // Routine referral family.
            "lt_rr" or "routine_referral" or "routine" or "referral" => "routine_referral",
            // Urgent referral family.
            "lt_ur" or "urgent_referral" or "urgent" => "urgent_referral",
            // Discharge family.
            "lt_dg" or "discharge" or "update_discharge" => "discharge",
            // Transfer family.
            "lt_tr" or "transfer" or "transfer_letter" => "transfer",
            // Non-medical family.
            "lt_nm" or "non_medical_referral" or "non_medical" or "nonmedical" => "non_medical_referral",
            // GP-referral sub-family (kept distinct: detailed packs exist for it).
            "referral_gp" or "gp" or "referral_to_gp" or "gp_referral" => "referral_to_gp",
            // Other Letters + retired Response family + anything unknown.
            _ => "other",
        };
    }
}
