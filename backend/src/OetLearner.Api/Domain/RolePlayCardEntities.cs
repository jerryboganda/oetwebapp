using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Serialisation helpers for the unbounded task-bullet lists on
/// <see cref="RolePlayCard"/> and <see cref="InterlocutorScript"/>.
///
/// <para>These replaced the fixed <c>Task1..Task5</c> columns: the owner's real
/// OET card sets contain cards with six to eight printed bullets, so a
/// five-slot schema silently dropped printed content at import time.</para>
/// </summary>
public static class RolePlayCardTasks
{
    /// <summary>Null/blank entries are dropped; order and text are preserved.</summary>
    public static string Serialize(IEnumerable<string>? tasks)
    {
        if (tasks is null) return "[]";
        var cleaned = tasks
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .ToArray();
        return cleaned.Length == 0 ? "[]" : JsonSerializer.Serialize(cleaned);
    }

    public static IReadOnlyList<string> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Keep the legacy five columns in step with the first five list entries so
    /// an instance still running the previous build renders a sane (if
    /// truncated) card during a blue/green rollout.
    /// </summary>
    /// <remarks>
    /// The legacy columns are <c>varchar(500)</c> but the authoritative JSON
    /// list is not bounded, and real printed cards carry bullets longer than
    /// that (the longest in the owner's corpus is 602 characters). Writing the
    /// untruncated value straight through made the whole save fail with
    /// "value too long for type character varying(500)", so a perfectly valid
    /// card could not be created at all. Clamping here is safe: nothing reads
    /// these columns any more, <see cref="Serialize"/> has already stored the
    /// full text, and truncation is exactly what the mirror was documented to
    /// do.
    /// </remarks>
    public const int LegacyColumnLength = 500;

    public static void MirrorToLegacyColumns(
        IEnumerable<string>? tasks,
        params Action<string?>[] setters)
    {
        var list = (tasks ?? Enumerable.Empty<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Select(t => t.Length <= LegacyColumnLength ? t : t[..LegacyColumnLength])
            .ToArray();
        for (var i = 0; i < setters.Length; i++)
        {
            setters[i](i < list.Length ? list[i] : null);
        }
    }

    /// <summary>
    /// Read the effective task list for a card that may pre-date
    /// <c>TasksJson</c>: prefer the JSON list, fall back to the legacy columns.
    /// </summary>
    public static IReadOnlyList<string> Effective(string? json, params string?[] legacy)
    {
        var fromJson = Deserialize(json);
        if (fromJson.Count > 0) return fromJson;
        return legacy.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).ToArray();
    }
}

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

    /// <summary>
    /// AUTHORITATIVE ordered list of candidate task bullets, as a JSON array of
    /// strings. Unbounded: real OET cards routinely carry six, seven or eight
    /// bullets, and a bullet may itself contain indented sub-items (kept inside
    /// the parent string, one per line).
    ///
    /// <para>Use <see cref="Tasks"/> to read/write this. The legacy
    /// <c>Task1..Task5</c> columns below are a MIRROR of the first five entries,
    /// kept populated only so that instances running the previous build keep
    /// working across a blue/green rollout; nothing reads them any more and a
    /// later migration drops them.</para>
    /// </summary>
    public string TasksJson { get; set; } = "[]";

    /// <summary>Ordered candidate task bullets. Backed by <see cref="TasksJson"/>.</summary>
    [NotMapped]
    public IReadOnlyList<string> Tasks
    {
        get => RolePlayCardTasks.Effective(TasksJson, Task1, Task2, Task3, Task4, Task5);
        set
        {
            TasksJson = RolePlayCardTasks.Serialize(value);
            RolePlayCardTasks.MirrorToLegacyColumns(value, s => Task1 = s, s => Task2 = s,
                s => Task3 = s, s => Task4 = s, s => Task5 = s);
        }
    }

    // LEGACY mirror of the first five entries of `Tasks` — see TasksJson.
    // Do not read these; do not add a Task6. Kept for rollout compatibility.
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

    /// <summary>Provenance of the printed source this card was transcribed
    /// from — verbatim footer/watermark text such as
    /// "© Cambridge Boxhill Language Assessment / SEPTEMBER 2015".
    ///
    /// ADMIN/TUTOR ONLY. Deliberately absent from every learner-facing
    /// projection (<c>RolePlayCardLearnerDetail</c>) so the card face renders
    /// clean, while the record itself never loses the rights-holder notice
    /// attached to the source material. Do not repurpose this as a display
    /// field, and do not strip it on import.</summary>
    [MaxLength(400)]
    public string? SourceAttribution { get; set; }

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    /// <summary>True when the card is wired into the live-tutor flow
    /// (`PrivateSpeakingBooking`). Cards default to AI-only until an
    /// admin marks them live-tutor eligible.</summary>
    public bool IsLiveTutorEligible { get; set; } = false;

    /// <summary>FINAL 2026-09-06 — candidate-visible primary card category
    /// (First Visit, Second Visit / Follow-up, Already Known Patient,
    /// Examination Card, Emergency / Emergency Department, Breaking Bad
    /// News, Angry Patient, Reluctant Patient, Other Cards). This is the
    /// main catalogue filter and may appear as a chip on learner cards.
    /// Distinct from the HIDDEN admin `CardTypeId` (scorer guidance, never
    /// serialized to students).</summary>
    [MaxLength(64)]
    public string PrimaryCategory { get; set; } = "Other Cards";

    /// <summary>Optional secondary behavioural tags (Angry, Reluctant,
    /// Breaking Bad News) stored as a JSON string array. They coexist with
    /// a stronger encounter-type primary category.</summary>
    public string SecondaryTagsJson { get; set; } = "[]";

    /// <summary>True when the classifier fell through to Other Cards and a
    /// human should review the category. A visible Other Cards result is
    /// safer than a forced wrong category.</summary>
    public bool CategoryNeedsReview { get; set; } = false;

    /// <summary>Provenance of <see cref="PrimaryCategory"/>: <c>legacy</c>
    /// (pre-provenance row, machine-classified with no record kept),
    /// <c>classifier</c> (the deterministic §8B engine set it),
    /// <c>reviewed</c> (a human confirmed a classifier/legacy value during
    /// the corpus repair sweep), <c>manual</c> (an admin explicitly picked
    /// it), or <c>seed</c> (hand-authored system seed data). Drives whether
    /// a content edit is allowed to silently reclassify the row.</summary>
    [MaxLength(16)]
    public string CategorySource { get; set; } = "legacy";

    /// <summary>Which <see cref="SpeakingCardClassifier.ClassifierVersion"/>
    /// produced the current category, when <see cref="CategorySource"/> is
    /// <c>classifier</c>. Null for manual/reviewed/seed/legacy rows.</summary>
    [MaxLength(32)]
    public string? CategoryClassifierVersion { get; set; }

    /// <summary>When the classifier last set <see cref="PrimaryCategory"/>.
    /// Null for manual/reviewed/seed/legacy rows.</summary>
    public DateTimeOffset? CategoryClassifiedAt { get; set; }

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

    /// <summary>
    /// AUTHORITATIVE ordered list of roleplayer task bullets, as a JSON array of
    /// strings. Unbounded — see <see cref="RolePlayCard.TasksJson"/> for why.
    /// </summary>
    public string PatientTasksJson { get; set; } = "[]";

    /// <summary>Ordered roleplayer task bullets. Backed by <see cref="PatientTasksJson"/>.</summary>
    [NotMapped]
    public IReadOnlyList<string> PatientTasks
    {
        get => RolePlayCardTasks.Effective(PatientTasksJson,
            PatientTask1, PatientTask2, PatientTask3, PatientTask4, PatientTask5);
        set
        {
            PatientTasksJson = RolePlayCardTasks.Serialize(value);
            RolePlayCardTasks.MirrorToLegacyColumns(value, s => PatientTask1 = s, s => PatientTask2 = s,
                s => PatientTask3 = s, s => PatientTask4 = s, s => PatientTask5 = s);
        }
    }

    // LEGACY mirror of the first five entries of `PatientTasks`.
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

    /// <summary>When true, an explicitly authored Card B may reference a
    /// selected subset of Card A facts after the candidate gives the
    /// configured second-visit indicator.</summary>
    public bool AllowsSecondVisit { get; set; }

    [MaxLength(500)]
    public string? SecondVisitIndicator { get; set; }

    /// <summary>JSON array of fact keys approved for the explicit second visit.</summary>
    public string SecondVisitCarryFactsJson { get; set; } = "[]";

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
