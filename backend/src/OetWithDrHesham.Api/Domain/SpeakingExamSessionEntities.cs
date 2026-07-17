using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain;

// Speaking module rebuild (2026-06-11 spec).
//
// `SpeakingExamSession` is the orchestrator for the new two-card Speaking exam
// that replaces the legacy `SpeakingMockSession` + 60s-bridge flow. It drives a
// strict server-authoritative state machine:
//
//   intro -> prep_a -> active_a -> prep_b -> active_b -> completed
//
// (terminal alternates: cancelled, expired). There is NO bridge step. Card A
// auto-closes after its 8-minute window (3-min prep + 5-min discussion) and
// Card B auto-reveals.
//
// The exam owns two child `SpeakingSession` rows (slot "a" and slot "b"), one
// per card. Each child session reuses the full speaking engine (recordings,
// transcripts, ConversationHub AI patient, AI/tutor assessment). Credits are
// debited per card at reveal (prep start): AI mode = 1 speaking credit per
// card = 2 per exam. Live-tutor mode = pay-per-session via Stripe booking,
// no credits.

public enum SpeakingExamMode
{
    /// <summary>AI patient via ConversationHub; AI scores the exam.</summary>
    Ai = 0,

    /// <summary>Human tutor plays the patient (booked + paid via Stripe);
    /// the tutor marks the exam. No AI scoring.</summary>
    LiveTutor = 1,
}

public enum SpeakingExamState
{
    /// <summary>Unscored warm-up (Part 1 "INTRO").</summary>
    Intro = 0,
    PrepA = 1,
    ActiveA = 2,
    PrepB = 3,
    ActiveB = 4,
    Completed = 5,
    Cancelled = 6,
    Expired = 7,
}

[Index(nameof(UserId), nameof(State))]
[Index(nameof(BookingId))]
public class SpeakingExamSession
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(32)]
    public string ProfessionId { get; set; } = "medicine";

    public SpeakingExamMode Mode { get; set; } = SpeakingExamMode.Ai;

    public SpeakingExamState State { get; set; } = SpeakingExamState.Intro;

    /// <summary>Provenance when the exam was started from a curated
    /// `SpeakingMockSet` pair. Null for random/profession-picked pairs.</summary>
    [MaxLength(64)]
    public string? MockSetId { get; set; }

    /// <summary>The two role-play cards for this exam. The candidate is the
    /// doctor in both. Resolved at create time and pinned.</summary>
    [MaxLength(64)]
    public string CardAId { get; set; } = default!;

    [MaxLength(64)]
    public string CardBId { get; set; } = default!;

    /// <summary>Child `SpeakingSession` rows, created lazily as each card is
    /// revealed (prep start). Null until that card begins.</summary>
    [MaxLength(64)]
    public string? SessionAId { get; set; }

    [MaxLength(64)]
    public string? SessionBId { get; set; }

    // ── Server-authoritative phase timestamps (UTC). All transitions are
    //    computed from these, never from in-memory timers, so the exam
    //    survives a server restart. ──
    public DateTimeOffset? IntroStartedAt { get; set; }
    public DateTimeOffset? IntroEndedAt { get; set; }
    public DateTimeOffset? PrepAStartedAt { get; set; }
    public DateTimeOffset? ActiveAStartedAt { get; set; }
    public DateTimeOffset? CardAEndedAt { get; set; }
    public DateTimeOffset? PrepBStartedAt { get; set; }
    public DateTimeOffset? ActiveBStartedAt { get; set; }
    public DateTimeOffset? CardBEndedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Rulebook version pinned at create time (profession-specific),
    /// copied onto each child session for post-hoc audit.</summary>
    [MaxLength(32)]
    public string? RulebookVersion { get; set; }

    // ── Credit idempotency anchors (AI mode only). One speaking credit is
    //    debited per card at reveal; the reference is stored so a retried
    //    transition never double-charges. ──
    [MaxLength(96)]
    public string? CreditARefId { get; set; }

    [MaxLength(96)]
    public string? CreditBRefId { get; set; }

    /// <summary>True when this exam's Card A debit was funded from the
    /// account's "Full Mock Speaking Exam Access" allowance
    /// (<c>AiPackageCreditAccount.MockExamsRemaining</c>, one unit = one
    /// whole two-card exam) instead of the per-card AI Speaking Credits
    /// wallet. When true, Card B's debit is a no-op (already covered).</summary>
    public bool FundedByMockCredit { get; set; }

    // ── Aggregate snapshot, computed once both cards are scored. ──
    public double? CombinedScaledSnapshot { get; set; }

    [MaxLength(32)]
    public string? ReadinessBandSnapshot { get; set; }

    /// <summary>FK to `PrivateSpeakingBooking` when Mode = LiveTutor. Null for
    /// AI exams.</summary>
    [MaxLength(64)]
    public string? BookingId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class SpeakingExamModes
{
    public const string Ai = "ai";
    public const string LiveTutor = "live_tutor";

    public static SpeakingExamMode Parse(string? value) => value switch
    {
        LiveTutor => SpeakingExamMode.LiveTutor,
        _ => SpeakingExamMode.Ai,
    };

    public static string ToCode(SpeakingExamMode mode) => mode switch
    {
        SpeakingExamMode.LiveTutor => LiveTutor,
        _ => Ai,
    };
}

public static class SpeakingExamStates
{
    public const string Intro = "intro";
    public const string PrepA = "prep_a";
    public const string ActiveA = "active_a";
    public const string PrepB = "prep_b";
    public const string ActiveB = "active_b";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";

    public static string ToCode(SpeakingExamState state) => state switch
    {
        SpeakingExamState.Intro => Intro,
        SpeakingExamState.PrepA => PrepA,
        SpeakingExamState.ActiveA => ActiveA,
        SpeakingExamState.PrepB => PrepB,
        SpeakingExamState.ActiveB => ActiveB,
        SpeakingExamState.Completed => Completed,
        SpeakingExamState.Cancelled => Cancelled,
        SpeakingExamState.Expired => Expired,
        _ => Intro,
    };

    public static bool IsTerminal(SpeakingExamState state) =>
        state is SpeakingExamState.Completed
            or SpeakingExamState.Cancelled
            or SpeakingExamState.Expired;
}
