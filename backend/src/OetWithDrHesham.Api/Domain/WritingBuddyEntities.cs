using System.ComponentModel.DataAnnotations;

namespace OetWithDrHesham.Api.Domain;

/// <summary>
/// Anonymous learner-to-learner pairing for the Writing module Buddy
/// System (spec §23.5). Two learners at a similar level (within ±1 band)
/// and in the same profession are paired together for peer accountability
/// + weekly check-ins. Identities are surfaced only through anonymised
/// display names ("Anonymous Doctor", profession + city).
///
/// Lifecycle: <c>active → paused → ended</c>. A user can only ever have
/// one <c>active</c> pair at a time (enforced by a partial unique index
/// in the DbContext partial).
/// </summary>
public class WritingBuddyPair
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserAId { get; set; } = default!;

    [MaxLength(64)]
    public string UserBId { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    [MaxLength(64)]
    public string? EndedReason { get; set; }

    [MaxLength(64)]
    public string Profession { get; set; } = "medicine";

    /// <summary>
    /// Band string captured at the moment of matching (e.g. "B", "B+").
    /// Used so both learners see the level they were paired at, even if
    /// their bands diverge over time.
    /// </summary>
    [MaxLength(8)]
    public string MatchedAtBand { get; set; } = "B";

    /// <summary>
    /// One of <c>active</c>, <c>paused</c>, <c>ended</c>.
    /// </summary>
    [MaxLength(16)]
    public string Status { get; set; } = "active";
}

/// <summary>
/// One message in a Buddy chat. Markdown is stored verbatim and capped
/// at 500 characters by the service layer; rendering MUST sanitise on
/// the read side. A 10-per-day per-pair rate limit is enforced by the
/// service.
/// </summary>
public class WritingBuddyMessage
{
    public Guid Id { get; set; }

    public Guid PairId { get; set; }

    [MaxLength(64)]
    public string FromUserId { get; set; } = default!;

    [MaxLength(500)]
    public string BodyMarkdown { get; set; } = string.Empty;

    public DateTimeOffset SentAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>
/// One weekly check-in for a Buddy pair (one ISO-week row per pair).
/// Both partners submit a small JSON report (struct decided client-side);
/// <see cref="CompletedAt"/> is set once both halves are in.
/// </summary>
public class WritingBuddyCheckIn
{
    public Guid Id { get; set; }

    public Guid PairId { get; set; }

    public DateOnly WeekStartDate { get; set; }

    public string? UserAReportJson { get; set; }

    public string? UserBReportJson { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
