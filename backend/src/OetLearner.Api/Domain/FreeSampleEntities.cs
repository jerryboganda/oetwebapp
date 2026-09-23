using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// Free Mocks (owner 2026-09-22): which Writing case note / Speaking role-play
/// card is THE free sample of a profession. One row per (subtest, profession).
/// Optional: with no row the server auto-picks the lowest-order LIVE item for
/// the profession (see <c>FreeSampleService</c>), so a profession goes live as
/// soon as its content is published — no deploy, no admin step.
/// </summary>
public class FreeSampleDesignation
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>writing | speaking</summary>
    [MaxLength(16)]
    public string Subtest { get; set; } = default!;

    /// <summary>Normalised profession id (lower-case, hyphenated).</summary>
    [MaxLength(32)]
    public string Profession { get; set; } = default!;

    /// <summary>Writing: <c>WritingScenario.Id</c> ("D" guid). Speaking: <c>RolePlayCard.Id</c>.</summary>
    [MaxLength(64)]
    public string ContentId { get; set; } = default!;

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A learner's free AI-graded sample for a subtest (owner addendum 23 Sep
/// 2026: TWO successful results per subtest, both on this SAME item). The
/// UNIQUE (UserId, Subtest) index pins the profession and content forever, so
/// no device, session, route or profession change can reset it. Each started /
/// graded attempt is a <see cref="FreeSampleUse"/> row; only a use that
/// produced a result counts, so start/exit, upload or grading failures never
/// burn the allowance. <see cref="Version"/> is the optimistic-concurrency
/// token that makes two racing binds for the last slot resolve to one winner.
/// The column MUST stay named <c>UserId</c>: UserHardDeleteService purges by
/// that column name.
/// </summary>
public class FreeSampleClaim
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>writing | speaking</summary>
    [MaxLength(16)]
    public string Subtest { get; set; } = default!;

    [MaxLength(32)]
    public string Profession { get; set; } = default!;

    /// <summary>Writing: scenario id ("D"). Speaking: RolePlayCard.Id.</summary>
    [MaxLength(64)]
    public string ContentId { get; set; } = default!;

    /// <summary>Legacy (pre-23 Sep 2026) single binding: the first attempt/submission
    /// the claim was minted for. Kept for provenance only — the migration
    /// backfilled it into <see cref="FreeSampleUse"/>, which is the source of truth.</summary>
    [MaxLength(64)]
    public string? AttemptId { get; set; }

    public DateTimeOffset ClaimedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Optimistic-concurrency token, bumped on every use bind.</summary>
    [ConcurrencyCheck]
    public int Version { get; set; }
}

/// <summary>
/// One attempt at the learner's free sample, bound to exactly one resource
/// (<see cref="ResourceId"/> is UNIQUE). Whether it counts is derived from the
/// resource itself (no completion hooks): a result exists = a success.
/// Purged with the account by UserHardDeleteService (UserId column).
/// </summary>
public class FreeSampleUse
{
    public const string KindLegacyAttempt = "legacy_attempt";
    public const string KindSpeakingSession = "speaking_session";
    public const string KindWritingSubmission = "writing_submission";

    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ClaimId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>writing | speaking</summary>
    [MaxLength(16)]
    public string Subtest { get; set; } = default!;

    /// <summary>legacy_attempt | speaking_session | writing_submission</summary>
    [MaxLength(32)]
    public string ResourceKind { get; set; } = default!;

    /// <summary>legacy_attempt: <c>Attempt.Id</c>. speaking_session:
    /// <c>SpeakingSession.Id</c>. writing_submission: <c>WritingSubmission.Id</c> ("N").</summary>
    [MaxLength(64)]
    public string ResourceId { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
}
