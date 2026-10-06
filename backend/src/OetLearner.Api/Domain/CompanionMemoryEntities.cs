using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OetLearner.Api.Domain;

/// <summary>
/// One durable fact in the learner's companion memory (F-041/F-042/F-043, SAMI §3.2).
///
/// Three layers on one table:
/// <c>conversation</c> — transient working context for the active chat;
/// <c>learning</c> — confirmed scores, errors, mastery, preferences, completed work;
/// <c>journey</c> — longitudinal markers across exam dates, interventions and resits.
///
/// Every row is namespaced to <see cref="UserId"/> — cross-user reads are impossible by
/// construction because every query filters on it. Consequential facts (scores, exam
/// dates) carry <see cref="ConfirmedAt"/>; the confirm-before-save contract (SAMI §3.2,
/// UAT Pack 3 Test 01/02) is enforced by the tools, which only persist after the learner
/// explicitly agrees to the echoed-back values.
/// </summary>
public class CompanionMemoryEntry
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary><see cref="CompanionMemoryLayers"/>: Conversation=0, Learning=1, Journey=2.</summary>
    public int Layer { get; set; }

    /// <summary>Free-form kind: score, exam_date, preference, note, weakness, intervention, journey_marker…</summary>
    [MaxLength(48)]
    public string Kind { get; set; } = default!;

    /// <summary>reading | writing | listening | speaking | general. Always lowercase.</summary>
    [MaxLength(16)]
    public string Subtest { get; set; } = "general";

    /// <summary>Human-readable statement of the fact, in the learner's own words where possible.</summary>
    [MaxLength(1024)]
    public string Content { get; set; } = default!;

    /// <summary>Optional structured payload (e.g. the four sub-test scores of one result).</summary>
    public string? DataJson { get; set; }

    /// <summary>chat | result | attempt | tutor_note | upload | system_event.</summary>
    [MaxLength(24)]
    public string ProvenanceType { get; set; } = "chat";

    /// <summary>Thread id, attempt id, report id… whatever pins this fact to its origin.</summary>
    [MaxLength(64)]
    public string? ProvenanceId { get; set; }

    /// <summary>Set once the learner explicitly confirmed the value (scores, exam dates). Null = pending or not consequential.</summary>
    public DateTimeOffset? ConfirmedAt { get; set; }

    /// <summary>Non-null when a newer entry superseded this one: history stays, current reads skip it.</summary>
    public DateTimeOffset? SupersededAt { get; set; }

    /// <summary>Journey this entry belongs to (journey layer); null for global facts.</summary>
    [MaxLength(64)]
    public string? JourneyId { get; set; }

    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class CompanionMemoryLayers
{
    public const int Conversation = 0;
    public const int Learning = 1;
    public const int Journey = 2;
}

/// <summary>
/// One recurring, evidenced weakness (F-044 Error DNA, SAMI §5.3). Entries are created ONLY
/// from observed evidence — a graded writing finding, a missed listening word, a wrong
/// reading answer — never from a guess about what the learner probably struggles with.
/// Spaced reinforcement (F-046) schedules <see cref="NextReviewAt"/>; each successful review
/// raises <see cref="MasteryScore"/>, each miss lowers it and shortens the interval.
/// </summary>
public class ErrorDnaEntry
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>grammar | writing_content | writing_language | speaking | reading | listening | timing | vocabulary | strategy.</summary>
    [MaxLength(24)]
    public string Category { get; set; } = default!;

    /// <summary>Stable human-readable pattern, e.g. "article before singular countable noun".</summary>
    [MaxLength(256)]
    public string Pattern { get; set; } = default!;

    /// <summary>SHA-256 of (category + lowercased pattern), for upsert matching.</summary>
    [MaxLength(64)]
    public string PatternKey { get; set; } = default!;

    /// <summary>reading | writing | listening | speaking | general.</summary>
    [MaxLength(16)]
    public string Subtest { get; set; } = "general";

    public int EvidenceCount { get; set; } = 1;

    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>0..100. Below 30 = unaddressed; 100 = demonstrated mastery (spaced reviews passed).</summary>
    public int MasteryScore { get; set; }

    public int ReviewCount { get; set; }

    public DateTimeOffset? NextReviewAt { get; set; }

    /// <summary>Where the evidence came from (writing_grade, listening_answer, reading_answer, speaking_assess, tutor_note).</summary>
    [MaxLength(32)]
    public string? SourceKind { get; set; }

    [MaxLength(64)]
    public string? SourceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// One exam-preparation journey (F-012/F-043, UAT Pack 2 Test 20). A resit opens a NEW
/// journey and keeps the previous ones readable, so trend and intervention comparisons
/// survive the transition. Exactly one journey per learner is active at a time.
/// </summary>
public class CompanionJourney
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(128)]
    public string Label { get; set; } = default!;

    public DateOnly? TargetExamDate { get; set; }

    /// <summary>Sub-tests this journey focuses on (a Writing-only resit = ["writing"]). Empty = all.</summary>
    public string FocusSubtestsJson { get; set; } = "[]";

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Outcome when closed: passed | partially_met | not_passed | withdrawn.</summary>
    [MaxLength(24)]
    public string? Outcome { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// When and how long this learner can study (F-009, SAMI §3.1), plus travel mode. One row per
/// learner. The plan generator and the next-best-action engine read this before fitting tasks
/// into days — night shifts on Monday mean Monday is a rest day, not a 45-minute day.
/// </summary>
public class CompanionAvailability
{
    [Key]
    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>Study minutes available per weekday, Monday-first: [Mon..Sun]. 0 = no study that day.</summary>
    public string DailyMinutesJson { get; set; } = "[0,0,0,0,0,0,0]";

    /// <summary>Weekday indices (0=Monday) the learner works a NIGHT shift — those days end in recovery.</summary>
    public string NightShiftDaysJson { get; set; } = "[]";

    /// <summary>Weekday indices (0=Monday) the learner works a long DAY shift — capacity is halved afterwards.</summary>
    public string LongDayShiftDaysJson { get; set; } = "[]";

    /// <summary>Travel/holiday mode: while true, replanning uses TravelModeMinutesPerDay and avoids new content.</summary>
    public bool TravelMode { get; set; }

    public int TravelModeMinutesPerDay { get; set; } = 20;

    public DateOnly? TravelUntil { get; set; }

    /// <summary>Preferred reminder time (24h hh:mm) or null. Quiet hours respect it (F-119).</summary>
    [MaxLength(5)]
    public string? PreferredStudyTime { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
