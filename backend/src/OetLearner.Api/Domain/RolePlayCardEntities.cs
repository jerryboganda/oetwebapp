using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

// Phase 1 of the OET Speaking module roadmap
// (see C:\Users\Dr Faisal Maqsood PC\.claude\plans\1-oet-speaking-module-quirky-kurzweil.md).
//
// `RolePlayCard` is the typed candidate-facing role-play (the "card" the
// student sees during the test). It is paired 1:1 with an `InterlocutorScript`
// (the hidden patient persona used by tutors and the AI interlocutor) and
// hangs off an underlying `ContentItem` row with `SubtestCode = "speaking"`.
//
// Existing speaking content authored on `ContentPaper.ExtractedTextJson` is
// still respected by `Services/Content/SpeakingContentStructure.cs`; the
// `RolePlayCard` schema below replaces that ad-hoc JSON shape with a
// queryable, learner-safe projection. The learner-facing endpoint
// (`/v1/speaking/role-play-cards/{id}`) MUST NOT serialize any field from
// `InterlocutorScript`.

public enum ResistanceLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
}

[Index(nameof(ProfessionId), nameof(Status))]
[Index(nameof(ContentItemId), IsUnique = true)]
public class RolePlayCard
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>1:1 link to a `ContentItem` (subtest=speaking). The
    /// underlying ContentItem holds shared concerns (publish status,
    /// profession scoping, exam-family discriminator); this row holds the
    /// typed speaking-only fields.</summary>
    [MaxLength(64)]
    public string ContentItemId { get; set; } = default!;

    public ContentItem? ContentItem { get; set; }

    /// <summary>Owning profession (`nursing`, `medicine`, etc.). Mirrors
    /// `ContentItem.ProfessionId` for query convenience.</summary>
    [MaxLength(32)]
    public string ProfessionId { get; set; } = "nursing";

    /// <summary>Short label shown above the card (e.g. "Discharge advice
    /// after appendectomy").</summary>
    [MaxLength(200)]
    public string ScenarioTitle { get; set; } = default!;

    /// <summary>Clinical setting (e.g. "Surgical ward", "Community
    /// pharmacy", "Home visit").</summary>
    [MaxLength(160)]
    public string Setting { get; set; } = default!;

    /// <summary>How the card refers to the candidate (e.g. "Nurse",
    /// "Doctor", "Pharmacist").</summary>
    [MaxLength(256)]
    public string CandidateRole { get; set; } = default!;

    /// <summary>How the card refers to the other person (e.g. "Patient",
    /// "Parent", "Carer").</summary>
    [MaxLength(256)]
    public string InterlocutorRole { get; set; } = "Patient";

    /// <summary>Optional patient name shown on the candidate card.</summary>
    [MaxLength(80)]
    public string? PatientName { get; set; }

    /// <summary>Optional patient age (free-text — "48", "48y", "early
    /// 30s") to mirror real OET card phrasing.</summary>
    [MaxLength(32)]
    public string? PatientAge { get; set; }

    /// <summary>Multi-line case background visible on the candidate
    /// card.</summary>
    [MaxLength(4000)]
    public string Background { get; set; } = string.Empty;

    // Up to five task bullets shown on the candidate card. Nullable so a
    // card can carry fewer than five tasks; the publish gate validates
    // that at least three are present.
    [MaxLength(500)] public string? Task1 { get; set; }
    [MaxLength(500)] public string? Task2 { get; set; }
    [MaxLength(500)] public string? Task3 { get; set; }
    [MaxLength(500)] public string? Task4 { get; set; }
    [MaxLength(500)] public string? Task5 { get; set; }

    /// <summary>Whether the candidate may make written notes on the card
    /// during the 3-minute preparation.</summary>
    public bool AllowedNotes { get; set; } = true;

    /// <summary>Preparation window before the role-play starts. Defaults
    /// to 180s (the OET 3-minute prep) and is honoured by the prep timer
    /// at `/speaking/sessions/[id]/prep`.</summary>
    public int PrepTimeSeconds { get; set; } = 180;

    /// <summary>Speaking window. Defaults to 300s (the OET ~5-minute
    /// role-play). Hard-capped by `SpeakingSession` auto-end logic.</summary>
    public int RolePlayTimeSeconds { get; set; } = 300;

    /// <summary>Patient's emotional state (e.g. "worried", "anxious",
    /// "angry", "embarrassed"). Drives interlocutor AI persona.</summary>
    [MaxLength(256)]
    public string PatientEmotion { get; set; } = "neutral";

    /// <summary>Candidate's primary communication goal (e.g. "Reassure",
    /// "Explain", "Persuade", "Inform"). Surfaced to AI scorer.</summary>
    [MaxLength(256)]
    public string CommunicationGoal { get; set; } = "Inform";

    /// <summary>Clinical topic tag (e.g. "Pain management", "Asthma
    /// inhaler use"). Used by analytics and drill recommendation.</summary>
    [MaxLength(256)]
    public string ClinicalTopic { get; set; } = "general";

    /// <summary>Difficulty tier (`core` | `extension` | `exam`). Mirrors
    /// the same enum used by `SpeakingMockSet.Difficulty`.</summary>
    [MaxLength(16)]
    public string Difficulty { get; set; } = "core";

    /// <summary>Speaking module rebuild (2026-06-11). HIDDEN card type
    /// (`SpeakingCardType`). Nullable so existing cards stay untyped until an
    /// admin backfills. MISSION CRITICAL: never serialized to students — it is
    /// surfaced only on admin/tutor paths and to the AI scorer.</summary>
    [MaxLength(64)]
    public string? CardTypeId { get; set; }

    public SpeakingCardType? CardType { get; set; }

    /// <summary>Speaking module rebuild (2026-06-11). The card number printed
    /// on the official OET card faces ("CANDIDATE CARD NO. {n}" /
    /// "ROLEPLAYER CARD NO. {n}"). Nullable; falls back to the slot position
    /// when unset.</summary>
    public int? DisplayCardNumber { get; set; }

    /// <summary>JSON array of criterion codes this card stresses (e.g.
    /// `["informationGiving","patientPerspective"]`). Same codes as
    /// `OetScoring.SpeakingCriterionScores`.</summary>
    public string CriteriaFocusJson { get; set; } = "[]";

    /// <summary>Always-visible advisory shown under the card on the
    /// learner UI (defaults to the practice-estimate disclaimer).</summary>
    [MaxLength(400)]
    public string Disclaimer { get; set; } =
        "Practice estimate only. This is not an official OET score or result.";

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    /// <summary>True when the card is wired into the live-tutor flow
    /// (`PrivateSpeakingBooking`). Cards default to AI-only until an
    /// admin marks them live-tutor eligible.</summary>
    public bool IsLiveTutorEligible { get; set; } = false;

    [MaxLength(64)]
    public string? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

// The hidden interlocutor card. Stored separately so the learner-facing
// serializer can never accidentally leak it: every learner endpoint loads
// `RolePlayCard` without an `Include(s => s.InterlocutorScript)` call, and
// every admin/tutor endpoint loads it explicitly.
[Index(nameof(RolePlayCardId), IsUnique = true)]
public class InterlocutorScript
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string RolePlayCardId { get; set; } = default!;

    public RolePlayCard? RolePlayCard { get; set; }

    // ─────────────────────────────────────────────────────────────────
    // Speaking module rebuild (2026-06-11) — the printed ROLEPLAYER (patient)
    // card face. These mirror the official OET roleplayer card: a SETTING
    // (reused from the candidate card), a PATIENT background paragraph, and up
    // to five TASK bullets. They live here on `InterlocutorScript` so they
    // inherit the existing learner-leakage protection for free (no learner
    // endpoint Includes this entity). The legacy behavioural fields below
    // (OpeningResponse / Prompt1-3 / HiddenInformation / ResistanceLevel /
    // ClosingCue) remain as AI-behaviour extras; the AI persona prompt grounds
    // primarily on PatientBackground + PatientTask*, falling back to the legacy
    // fields when these are blank so old cards keep working.
    // ─────────────────────────────────────────────────────────────────

    /// <summary>The PATIENT background paragraph printed on the roleplayer
    /// card (what the tutor/AI patient knows about themselves).</summary>
    [MaxLength(4000)]
    public string PatientBackground { get; set; } = string.Empty;

    [MaxLength(500)] public string? PatientTask1 { get; set; }
    [MaxLength(500)] public string? PatientTask2 { get; set; }
    [MaxLength(500)] public string? PatientTask3 { get; set; }
    [MaxLength(500)] public string? PatientTask4 { get; set; }
    [MaxLength(500)] public string? PatientTask5 { get; set; }

    /// <summary>Opening line the interlocutor uses to start the
    /// role-play (e.g. "I'm worried these tablets are too strong"). Seeds
    /// the AI patient's first message.</summary>
    [MaxLength(500)]
    public string OpeningResponse { get; set; } = default!;

    // Three cue prompts the interlocutor is expected to surface during
    // the role-play. Order is not strict but the AI uses them as a
    // checklist to drive the conversation toward the assessable moments.
    [MaxLength(500)] public string? Prompt1 { get; set; }
    [MaxLength(500)] public string? Prompt2 { get; set; }
    [MaxLength(500)] public string? Prompt3 { get; set; }

    /// <summary>Patient detail NOT printed on the candidate card. The
    /// interlocutor reveals these on direct questioning (e.g. "I had
    /// nausea after the first dose").</summary>
    [MaxLength(2000)]
    public string HiddenInformation { get; set; } = string.Empty;

    /// <summary>How resistant the patient is to advice. Drives AI
    /// persona temperature and decides whether the closing cue is
    /// reached.</summary>
    public ResistanceLevel ResistanceLevel { get; set; } = ResistanceLevel.Low;

    /// <summary>How the role-play ends if the candidate satisfies the
    /// patient's concerns (e.g. "Accept advice if reassured about
    /// addiction risk").</summary>
    [MaxLength(500)]
    public string ClosingCue { get; set; } = string.Empty;

    /// <summary>Free-text emotional state the interlocutor should
    /// project (e.g. "Worried about taking opioids"). Richer than
    /// `RolePlayCard.PatientEmotion`.</summary>
    [MaxLength(200)]
    public string EmotionalState { get; set; } = string.Empty;

    /// <summary>Optional notes about the interlocutor's relationship to
    /// the patient (e.g. "Patient's nephew", "Daughter who lives nearby").
    /// Only relevant when `RolePlayCard.InterlocutorRole` is not the
    /// patient themselves.</summary>
    [MaxLength(500)]
    public string? ProfessionRoleNotes { get; set; }

    /// <summary>JSON array of jargon terms the AI should trigger lay-
    /// language probing on (e.g. `["hypertension","NSAIDs"]`). Surfaced
    /// to the scorer to mark down candidates who fail to simplify.</summary>
    public string LayLanguageTriggersJson { get; set; } = "[]";

    [MaxLength(64)]
    public string? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class ResistanceLevels
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";

    public static ResistanceLevel Parse(string? value) => value?.ToLowerInvariant() switch
    {
        "medium" or "med" => ResistanceLevel.Medium,
        "high" => ResistanceLevel.High,
        _ => ResistanceLevel.Low,
    };

    public static string ToCode(ResistanceLevel level) => level switch
    {
        ResistanceLevel.Medium => Medium,
        ResistanceLevel.High => High,
        _ => Low,
    };
}
