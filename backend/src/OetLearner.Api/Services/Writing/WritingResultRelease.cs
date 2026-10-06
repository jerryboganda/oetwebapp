using System.Collections.Frozen;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// The permanent owner/testing allowlist for Writing (owner handoff, 6 Oct 2026).
/// These five accounts are exempt from BOTH the 15-minute result-release window and the
/// copy/paste restrictions, forever, unless the owner explicitly changes the list.
///
/// Hard-coded and immutable on purpose: no runtime setting, database row or admin control can
/// widen or narrow it, so an ordinary candidate can never discover or toggle it. Matching is an
/// exact, case-insensitive, trimmed comparison. There is NO Gmail dot/plus/alias folding, which is
/// the platform convention (<c>AuthEmailAddress</c> only trims and upper-cases).
/// </summary>
public static class WritingUnrestrictedAccounts
{
    private static readonly FrozenSet<string> Emails = new[]
    {
        "drahmedheshamuk2025@gmail.com",
        "drahmedhesham9595@gmail.com",
        "ahmedibrahimabdrabuibrahim@gmail.com",
        "drahmedhesham.work@gmail.com",
        "tutorcommerceacademy2026@gmail.com",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="email"/> is one of the five allowlisted accounts.</summary>
    public static bool IsUnrestricted(string? email)
        => !string.IsNullOrWhiteSpace(email) && Emails.Contains(email.Trim());

    /// <summary>
    /// Resolves the exemption for a learner id. Checks both the profile email and the sign-in
    /// account email (mirrors <c>AuthService.AreAnyDeviceVerificationExempt</c>), so either form
    /// of the owner's address is recognised. Server-authoritative: the client never supplies it.
    /// </summary>
    public static async Task<bool> IsUnrestrictedAsync(LearnerDbContext db, string? userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId)) return false;

        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.AuthAccountId })
            .FirstOrDefaultAsync(ct);
        if (user is null) return false;
        if (IsUnrestricted(user.Email)) return true;
        if (string.IsNullOrEmpty(user.AuthAccountId)) return false;

        var accountEmail = await db.ApplicationUserAccounts.AsNoTracking()
            .Where(a => a.Id == user.AuthAccountId)
            .Select(a => a.Email)
            .FirstOrDefaultAsync(ct);
        return IsUnrestricted(accountEmail);
    }
}

/// <summary>
/// The single result-release rule used by every learner read path (owner handoff section 5).
///
/// The release instant is derived at read time as <c>SubmittedAt + 15 minutes</c> from the row that
/// is persisted before any grading provider call and never rewritten (a technical retry keeps the
/// original <c>SubmittedAt</c>). No column, timer or hosted service is involved, so a refresh, app
/// close, browser close, lost connection or device restart can neither reset nor duplicate the
/// countdown or the submission. A result is released when BOTH the grade/review workflow is complete
/// (raw status <c>graded</c>, which the grading pipeline only sets after the secondary review) AND the
/// window has elapsed. If grading is still running when the window elapses, the state simply stays
/// <c>processing</c>: nothing is fabricated. Allowlisted accounts are released as soon as the real
/// workflow completes.
/// </summary>
public static class WritingResultRelease
{
    /// <summary>Grading/review is not complete yet (queued, preflight, grading, awaiting review, failed).</summary>
    public const string Processing = "processing";

    /// <summary>The grade is complete but the deliberate release window has not elapsed.</summary>
    public const string Held = "held";

    /// <summary>The final result may be shown to this learner.</summary>
    public const string Released = "released";

    /// <summary>
    /// <paramref name="State"/> is processing | held | released. <paramref name="ReleaseAt"/> is null for
    /// allowlisted accounts and failed rows. <paramref name="ServerNow"/> is the server clock used for
    /// the decision (clients anchor the countdown to the difference, never to their own clock).
    /// <paramref name="EffectiveStatus"/> is the learner-facing status: a held <c>graded</c> row reads
    /// <c>grading</c> so every existing "graded" consumer keeps waiting.
    /// </summary>
    public readonly record struct WritingReleaseView(
        string State,
        DateTimeOffset? ReleaseAt,
        DateTimeOffset ServerNow,
        string EffectiveStatus);

    public static WritingReleaseView Describe(
        string rawStatus,
        DateTimeOffset submittedAt,
        bool unrestricted,
        DateTimeOffset now)
    {
        var releaseAt = unrestricted || rawStatus == WritingSubmissionStatuses.Failed
            ? (DateTimeOffset?)null
            : submittedAt + WritingGradeTimings.ResultReleaseWindow;

        if (rawStatus != WritingSubmissionStatuses.Graded)
        {
            return new WritingReleaseView(Processing, releaseAt, now, rawStatus);
        }

        if (unrestricted || releaseAt is null || now >= releaseAt.Value)
        {
            return new WritingReleaseView(Released, releaseAt, now, WritingSubmissionStatuses.Graded);
        }

        return new WritingReleaseView(Held, releaseAt, now, WritingSubmissionStatuses.Grading);
    }

    /// <summary>
    /// Submissions with <c>SubmittedAt</c> at or before this instant are past the window. For
    /// IQueryable filters of the form
    /// <c>s.Status == Graded &amp;&amp; (unrestricted || s.SubmittedAt &lt;= Cutoff(now))</c>.
    /// </summary>
    public static DateTimeOffset Cutoff(DateTimeOffset now) => now - WritingGradeTimings.ResultReleaseWindow;

    /// <summary>
    /// The learner's submissions whose result may be shown: graded AND (allowlisted OR past the window).
    /// Inner side of every "read a grade for stats" join (analytics, readiness, pathway), so a letter still
    /// inside its window can never move a band label, score or weakness early. The allowlist and the cutoff
    /// are evaluated into locals first, so the predicate translates to plain SQL parameters.
    /// </summary>
    public static async Task<IQueryable<WritingSubmission>> ReleasedSubmissionsAsync(
        LearnerDbContext db,
        string userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var unrestricted = await WritingUnrestrictedAccounts.IsUnrestrictedAsync(db, userId, ct);
        var cutoff = Cutoff(now);
        return db.WritingSubmissions.AsNoTracking()
            .Where(s => s.UserId == userId
                && s.Status == WritingSubmissionStatuses.Graded
                && (unrestricted || s.SubmittedAt <= cutoff));
    }

    /// <summary>
    /// True when the learner may see the final result for this submission. False when the row is
    /// missing, not owned by <paramref name="userId"/>, still processing or held.
    /// </summary>
    public static async Task<bool> IsReleasedAsync(
        LearnerDbContext db,
        string userId,
        Guid submissionId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var row = await db.WritingSubmissions.AsNoTracking()
            .Where(s => s.Id == submissionId && s.UserId == userId)
            .Select(s => new { s.Status, s.SubmittedAt })
            .FirstOrDefaultAsync(ct);
        if (row is null || row.Status != WritingSubmissionStatuses.Graded) return false;

        // The allowlist lookup is only needed while the window is still open.
        if (now >= row.SubmittedAt + WritingGradeTimings.ResultReleaseWindow) return true;
        var unrestricted = await WritingUnrestrictedAccounts.IsUnrestrictedAsync(db, userId, ct);
        return Describe(row.Status, row.SubmittedAt, unrestricted, now).State == Released;
    }
}
