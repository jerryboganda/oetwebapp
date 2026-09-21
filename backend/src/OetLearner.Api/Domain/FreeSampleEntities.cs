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
/// A learner's single free AI-graded sample for a subtest. The UNIQUE
/// (UserId, Subtest) index is the atomic once-only claim. The claim is spent
/// only once its attempt/submission reaches grading — a claim whose attempt
/// never got that far (or whose grading failed) stays re-usable on the SAME
/// content, so a provider outage cannot burn the learner's only sample.
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

    /// <summary>Speaking: <c>Attempt.Id</c>. Writing: <c>WritingSubmission.Id</c> ("N").
    /// Null until the sample is actually started/submitted.</summary>
    [MaxLength(64)]
    public string? AttemptId { get; set; }

    public DateTimeOffset ClaimedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
