using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.FreeSamples;

/// <summary>The learner's free sample for one subtest, as served by
/// <c>GET /v1/free-samples/{subtest}</c> (SFM contract, 23 Sep 2026).</summary>
/// <param name="ProfessionId">Normalised profession id (lower-case, hyphenated).</param>
/// <param name="ContentId">Writing: scenario id ("D"). Speaking: role-play card id.
/// Null only when the learner's profession has no eligible item.</param>
/// <param name="State"><c>available</c> | <c>retry_available</c> | <c>in_progress</c> |
/// <c>grading_failed</c> | <c>completed</c> | <c>unavailable</c></param>
/// <param name="Route">Where the learner goes next (start, revise, or the result
/// being processed); null for <c>completed</c> / <c>unavailable</c>.</param>
/// <param name="SuccessfulCount">Uses that produced a result (0..<see cref="FreeSampleService.SuccessLimit"/>).</param>
/// <param name="LastResultRoute">Result page of the latest successful use.</param>
/// <param name="LastSubmissionId">Resource id of the latest successful use (Writing:
/// the submission to revise, "D"; Speaking: the session / legacy attempt id).</param>
public sealed record FreeSampleOffer(
    string ProfessionId,
    string? ContentId,
    string State,
    string? Route,
    int SuccessfulCount,
    string? LastResultRoute,
    string? LastSubmissionId)
{
    public int Limit => FreeSampleService.SuccessLimit;
    public int Remaining => Math.Max(0, FreeSampleService.SuccessLimit - SuccessfulCount);
}

/// <summary>
/// Free Mocks (owner 2026-09-22, retry addendum 2026-09-23): every learner gets
/// exactly TWO successful AI-graded results per subtest (Speaking, Writing), both
/// on the SAME designated item of their own profession. A use is consumed only
/// when it produces a result — start/exit, mic/upload failure and grading
/// failure never count. The claim pins profession + item forever (no reset via
/// device, session, route or profession change). The server alone decides what
/// is free; the client never sends a "free" flag.
/// </summary>
public interface IFreeSampleService
{
    /// <summary>0 or 1 rows: the learner's own sample with its state, counts and
    /// next route (see <see cref="FreeSampleOffer"/>). Pinned to the claimed
    /// profession once started — never another profession's item.</summary>
    Task<IReadOnlyList<FreeSampleOffer>> ListAsync(string userId, string subtest, CancellationToken ct);

    /// <summary>Read-only: may the learner start (or restart) a free use of
    /// <paramref name="contentRef"/> right now? True only for their own
    /// profession's pinned item, while fewer than two results exist and no use
    /// is being graded. Writing: scenario id. Speaking: card id or content-item id.</summary>
    Task<bool> IsOfferedAsync(string userId, string subtest, string contentRef, CancellationToken ct);

    /// <summary>Binds <paramref name="resourceId"/> as a free use (minting the
    /// claim on first use). Idempotent for an already-bound resource. A started
    /// but unsubmitted use is released (rebound) so restarting costs nothing.
    /// Two racing binds for the last slot: exactly one wins (claim Version token).
    /// <paramref name="resourceKind"/> is one of the <see cref="FreeSampleUse"/> Kind* constants.</summary>
    Task<bool> TryClaimAsync(string userId, string subtest, string contentRef, string resourceKind, string resourceId, CancellationToken ct);

    /// <summary>Grading-time server truth (arms the AI quota bypass): is
    /// <paramref name="resourceId"/> a bound free use that may still produce a
    /// free result (or already did)?</summary>
    Task<bool> IsFreeAttemptAsync(string userId, string subtest, string? resourceId, CancellationToken ct);
}

public sealed class FreeSampleService(LearnerDbContext db) : IFreeSampleService
{
    private static readonly JsonSerializerOptions SelectionJsonOptions = new(JsonSerializerDefaults.Web);

    public const string Writing = "writing";
    public const string Speaking = "speaking";

    /// <summary>Successful results per learner per subtest (owner addendum 23 Sep 2026).</summary>
    public const int SuccessLimit = 2;

    /// <summary>Master switch (dark launch + kill switch): free samples are granted
    /// ONLY while a <c>FeatureFlag</c> row with this key exists and is Enabled.
    /// Absent row or Enabled=false = OFF, so shipping the code changes nothing until
    /// an admin turns it on, and flipping it off stops every new free grant.</summary>
    public const string FeatureFlagKey = "free_samples_enabled";

    public const string StateAvailable = "available";
    public const string StateRetryAvailable = "retry_available";
    public const string StateInProgress = "in_progress";

    /// <summary>The latest use's grade failed (nothing newer): Retry grading re-runs
    /// that SAME submission, so it neither counts nor costs a new use.</summary>
    public const string StateGradingFailed = "grading_failed";
    public const string StateCompleted = "completed";
    public const string StateUnavailable = "unavailable";

    public static bool IsSupported(string? subtest) => subtest is Writing or Speaking;

    /// <summary>Lower-case + hyphenated so "Medicine", "occupational_therapy" and
    /// "occupational-therapy" all compare equal across the three profession vocabularies.</summary>
    public static string NormalizeProfession(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-');

    private static string GradingRoute(string submissionId)
        => $"/writing/submissions/{Uri.EscapeDataString(submissionId)}/grading";

    /// <summary>Where a learner starts a use of the sample.</summary>
    public static string StartRoute(string subtest, string contentId)
        => subtest == Writing
            ? $"/writing/practice/session/{Uri.EscapeDataString(contentId)}"
            // free=1 is a DISPLAY-ONLY hint; the server decides what is free when
            // the Speaking session is created and never reads it.
            : $"/speaking/roleplay/{Uri.EscapeDataString(contentId)}?free=1";

    public async Task<IReadOnlyList<FreeSampleOffer>> ListAsync(string userId, string subtest, CancellationToken ct)
    {
        if (!IsSupported(subtest) || !await IsEnabledAsync(ct)) return [];

        var ownProfession = await GetLearnerProfessionAsync(userId, ct);
        var claim = await GetClaimAsync(userId, subtest, ct);
        if (claim is not null)
        {
            var uses = await LoadUsesAsync(claim, tracking: false, ct);
            var successes = uses.Where(u => u.State == UseState.Done).ToList();
            var last = successes.LastOrDefault();
            var grading = uses.FirstOrDefault(u => u.State == UseState.Grading);
            var failed = uses.LastOrDefault() is { State: UseState.Failed } latest ? latest : null;

            string state;
            string? route = null;
            if (successes.Count >= SuccessLimit)
            {
                state = StateCompleted;
            }
            else if (claim.Profession != ownProfession)
            {
                // Owner decision (23 Sep 2026): a profession change never resets or
                // moves the allowance and never serves another profession's item.
                state = StateUnavailable;
            }
            else if (grading is not null)
            {
                state = StateInProgress;
                // Writing has no result page until graded: wait on the grading page.
                route = subtest == Writing ? GradingRoute(grading.SubmissionId) : grading.ResultRoute;
            }
            else if (failed is not null)
            {
                state = StateGradingFailed;
                route = GradingRoute(failed.SubmissionId);
            }
            else if (last is null)
            {
                state = StateAvailable;
                route = StartRoute(subtest, claim.ContentId);
            }
            else
            {
                state = StateRetryAvailable;
                // Writing's second result is "Revise & Resubmit" of the same letter.
                route = subtest == Writing
                    ? $"/writing/submissions/{Uri.EscapeDataString(last.SubmissionId)}/revise"
                    : StartRoute(subtest, claim.ContentId);
            }
            return [new FreeSampleOffer(
                claim.Profession, claim.ContentId, state, route,
                successes.Count, last?.ResultRoute, last?.SubmissionId)];
        }

        // CRITICAL SECURITY FIX (22 Sep 2026 handoff, item 2): only ever the
        // learner's OWN registered profession, never a cross-profession picker.
        // A learner with no profession yet gets nothing; a profession with no
        // live content gets a controlled "unavailable" row, never another
        // profession's item.
        if (string.IsNullOrWhiteSpace(ownProfession)) return [];

        var picks = await ResolvePicksAsync(subtest, ct);
        return picks.TryGetValue(ownProfession, out var ownPick)
            ? [new FreeSampleOffer(ownProfession, ownPick, StateAvailable, StartRoute(subtest, ownPick), 0, null, null)]
            : [new FreeSampleOffer(ownProfession, null, StateUnavailable, null, 0, null, null)];
    }

    public async Task<bool> IsOfferedAsync(string userId, string subtest, string contentRef, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId) || !IsSupported(subtest) || !await IsEnabledAsync(ct)) return false;
        var content = await ResolveContentAsync(subtest, contentRef, ct);
        if (content is null) return false;

        // Server-side enforcement (handoff item 2, "not just UI filtering"): a
        // crafted request for another profession's contentRef is rejected here
        // even if the client were compromised or the UI bug reappeared.
        var ownProfession = await GetLearnerProfessionAsync(userId, ct);
        if (content.Profession != ownProfession) return false;

        var claim = await GetClaimAsync(userId, subtest, ct);
        if (claim is not null)
        {
            // Pinned: only the claimed item (a designation change after the claim
            // must not move or strand it), while a slot is left and nothing of
            // this sample is being graded.
            return claim.ContentId == content.ContentId
                && claim.Profession == ownProfession
                && HasOpenSlot(await LoadUsesAsync(claim, tracking: false, ct), exceptResourceId: null);
        }

        var picks = await ResolvePicksAsync(subtest, ct);
        return picks.TryGetValue(content.Profession, out var pick) && pick == content.ContentId;
    }

    public async Task<bool> TryClaimAsync(
        string userId, string subtest, string contentRef, string resourceKind, string resourceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(resourceId)
            || !IsSupported(subtest)
            || resourceKind is not (FreeSampleUse.KindLegacyAttempt or FreeSampleUse.KindSpeakingSession or FreeSampleUse.KindWritingSubmission))
        {
            return false;
        }
        if (!await IsEnabledAsync(ct))
        {
            // Switching the programme off never re-bills a resource that already took
            // a free use: its Retry stays free (read-only, same slot rules). No new
            // claim or use is ever minted while the programme is off.
            return await IsFreeAttemptAsync(userId, subtest, resourceId, ct);
        }
        var content = await ResolveContentAsync(subtest, contentRef, ct);
        if (content is null) return false;

        var claim = await GetClaimAsync(userId, subtest, ct);
        if (claim is not null) return await BindAsync(claim, content, resourceKind, resourceId, ct);

        // Same own-profession guard as IsOfferedAsync — belt-and-braces since
        // this is the method that actually mints the claim.
        var ownProfession = await GetLearnerProfessionAsync(userId, ct);
        if (content.Profession != ownProfession) return false;

        var picks = await ResolvePicksAsync(subtest, ct);
        if (!picks.TryGetValue(content.Profession, out var pick) || pick != content.ContentId) return false;

        var now = DateTimeOffset.UtcNow;
        var row = new FreeSampleClaim
        {
            Id = $"fsc-{Guid.NewGuid():N}",
            UserId = userId,
            Subtest = subtest,
            Profession = content.Profession,
            ContentId = content.ContentId,
            AttemptId = resourceId,
            ClaimedAt = now,
            UpdatedAt = now,
        };
        db.FreeSampleClaims.Add(row);
        db.FreeSampleUses.Add(NewUse(row, resourceKind, resourceId, now));
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Lost the UNIQUE(UserId, Subtest) race (two tabs / double submit):
            // bind against the winner's claim instead (same slot rules apply).
            DetachFreeSampleEntries();
            var raced = await GetClaimAsync(userId, subtest, ct);
            if (raced is null) throw;
            return await BindAsync(raced, content, resourceKind, resourceId, ct);
        }
    }

    public async Task<bool> IsFreeAttemptAsync(string userId, string subtest, string? resourceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(resourceId) || !IsSupported(subtest)) return false;
        var use = await db.FreeSampleUses.AsNoTracking()
            .FirstOrDefaultAsync(u => u.ResourceId == resourceId && u.UserId == userId && u.Subtest == subtest, ct);
        if (use is null) return false;
        var claim = await db.FreeSampleClaims.AsNoTracking().FirstOrDefaultAsync(c => c.Id == use.ClaimId, ct);
        if (claim is null) return false;

        var uses = await LoadUsesAsync(claim, tracking: false, ct);
        var own = uses.FirstOrDefault(u => u.Use.ResourceId == resourceId);
        // A use that already produced its result stays free (idempotent re-reads);
        // otherwise it may only produce a result while a slot is left and no
        // OTHER use of the sample is being graded — so a retried failed use can
        // never become a third success.
        return own is not null && (own.State == UseState.Done || HasOpenSlot(uses, resourceId));
    }

    /// <summary>Grading gate for a Speaking session: the session itself is a bound
    /// use (shared engine), or it is the attempt→session bridge of a legacy
    /// recorder attempt that is.</summary>
    public static async Task<bool> IsFreeSpeakingSessionAsync(LearnerDbContext db, SpeakingSession session, CancellationToken ct)
    {
        var service = new FreeSampleService(db);
        return await service.IsFreeAttemptAsync(session.UserId, Speaking, session.Id, ct)
            || await service.IsFreeAttemptAsync(session.UserId, Speaking, session.AttemptId, ct);
    }

    /// <summary>The single source of truth for "which item is free" for a
    /// profession (FreeTierContentResolver delegates here). Null when the
    /// profession has no live item.</summary>
    public async Task<string?> ResolvePickAsync(string subtest, string? profession, CancellationToken ct)
    {
        var normalized = NormalizeProfession(profession);
        if (!IsSupported(subtest) || normalized.Length == 0) return null;
        var picks = await ResolvePicksAsync(subtest, ct);
        return picks.TryGetValue(normalized, out var pick) ? pick : null;
    }

    // ── internals ────────────────────────────────────────────────────────────

    /// <summary>The caller's own registered profession, normalised. Writing
    /// content uses a wider 13-id vocabulary than the account's 7-id one; for
    /// the 6 professions where they match 1:1 this resolves correctly, and for
    /// the "other-allied-health" umbrella (which has no Writing/Speaking
    /// content of its own today) it correctly resolves to a profession with no
    /// live pick, producing an "unavailable" offer rather than ever borrowing
    /// another profession's sample.</summary>
    private async Task<string> GetLearnerProfessionAsync(string userId, CancellationToken ct)
        => NormalizeProfession(await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.ActiveProfessionId)
            .FirstOrDefaultAsync(ct));

    /// <summary>PreSubmit = started, nothing submitted (free to abandon/rebind).
    /// Grading = submitted, result pending (queued/requeued rows included).
    /// Done = a result exists (counts). Failed = a Writing grade failed and can
    /// be retried on the same submission. Failed and Dead (abandoned / gone)
    /// never count and never block.</summary>
    private enum UseState { PreSubmit, Grading, Done, Failed, Dead }

    private sealed record UseStatus(FreeSampleUse Use, UseState State, string? ResultRoute, string SubmissionId);

    private sealed record ResolvedContent(string Profession, string ContentId);

    /// <summary>Fewer than <see cref="SuccessLimit"/> results and nothing else of
    /// the sample in grading (<paramref name="exceptResourceId"/> excluded).</summary>
    private static bool HasOpenSlot(IReadOnlyList<UseStatus> uses, string? exceptResourceId)
        => uses.Count(u => u.State == UseState.Done && u.Use.ResourceId != exceptResourceId) < SuccessLimit
            && !uses.Any(u => u.State == UseState.Grading && u.Use.ResourceId != exceptResourceId);

    private async Task<bool> BindAsync(
        FreeSampleClaim claim, ResolvedContent content, string resourceKind, string resourceId, CancellationToken ct)
    {
        if (claim.ContentId != content.ContentId) return false;

        var uses = await LoadUsesAsync(claim, tracking: true, ct);
        var own = uses.FirstOrDefault(u => u.Use.ResourceId == resourceId);
        if (own is { State: UseState.Done }) return true; // idempotent re-entry of a produced result
        if (!HasOpenSlot(uses, resourceId)) return false;

        var now = DateTimeOffset.UtcNow;
        var released = new List<object>();
        if (own is null)
        {
            // A NEW use is only ever started in the pinned profession.
            if (claim.Profession != await GetLearnerProfessionAsync(claim.UserId, ct)) return false;

            // Rebind: a started-but-unsubmitted use is released, so start/exit
            // never costs anything and at most one such use exists at a time.
            // Its session/attempt is closed too — it skipped the credit hold as
            // a free use, so it must never go on to be graded unpaid.
            foreach (var stale in uses.Where(u => u.State == UseState.PreSubmit))
            {
                db.FreeSampleUses.Remove(stale.Use);
                if (stale.Use.ResourceKind == FreeSampleUse.KindSpeakingSession)
                {
                    var session = await db.SpeakingSessions.FirstOrDefaultAsync(s => s.Id == stale.Use.ResourceId, ct);
                    if (session is { SubmittedAt: null })
                    {
                        session.State = SpeakingSessionState.Cancelled;
                        session.UpdatedAt = now;
                        released.Add(session);
                    }
                }
                else if (stale.Use.ResourceKind == FreeSampleUse.KindLegacyAttempt)
                {
                    var attempt = await db.Attempts.FirstOrDefaultAsync(a => a.Id == stale.Use.ResourceId, ct);
                    if (attempt is { State: AttemptState.NotStarted or AttemptState.InProgress or AttemptState.Paused })
                    {
                        attempt.State = AttemptState.Abandoned;
                        released.Add(attempt);
                    }
                }
            }
            db.FreeSampleUses.Add(NewUse(claim, resourceKind, resourceId, now));
        }

        // Concurrency token: of two binds racing for the same slot, the second
        // SaveChanges sees a stale Version and loses.
        claim.Version++;
        claim.UpdatedAt = now;
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            DetachFreeSampleEntries();
            foreach (var entity in released) db.Entry(entity).State = EntityState.Detached;
            // Lost the race: re-decide from the winner's committed state. Only a
            // loss to the SAME resource (double submit) can still be free.
            return await IsFreeAttemptAsync(claim.UserId, claim.Subtest, resourceId, ct);
        }
    }

    private static FreeSampleUse NewUse(FreeSampleClaim claim, string resourceKind, string resourceId, DateTimeOffset now) => new()
    {
        Id = $"fsu-{Guid.NewGuid():N}",
        ClaimId = claim.Id,
        UserId = claim.UserId,
        Subtest = claim.Subtest,
        ResourceKind = resourceKind,
        ResourceId = resourceId,
        CreatedAt = now,
    };

    /// <summary>After a failed bind: drop only this service's pending rows so the
    /// caller's own later SaveChanges does not retry them.</summary>
    private void DetachFreeSampleEntries()
    {
        foreach (var entry in db.ChangeTracker.Entries()
            .Where(e => e.Entity is FreeSampleUse or FreeSampleClaim)
            .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    // ponytail: one status lookup per use (N+1); a claim holds a handful of uses
    // (pre-submit ones are deleted on rebind). Batch per kind if that ever grows.
    private async Task<List<UseStatus>> LoadUsesAsync(FreeSampleClaim claim, bool tracking, CancellationToken ct)
    {
        var query = db.FreeSampleUses.Where(u => u.ClaimId == claim.Id);
        var rows = await (tracking ? query : query.AsNoTracking()).ToListAsync(ct);
        var result = new List<UseStatus>(rows.Count);
        foreach (var use in rows.OrderBy(u => u.CreatedAt).ThenBy(u => u.Id, StringComparer.Ordinal))
        {
            result.Add(await GetUseStatusAsync(use, ct));
        }
        return result;
    }

    /// <summary>Where one use stands, derived from its resource (no completion
    /// hooks): only an actually produced result is Done.</summary>
    private async Task<UseStatus> GetUseStatusAsync(FreeSampleUse use, CancellationToken ct)
    {
        switch (use.ResourceKind)
        {
            case FreeSampleUse.KindWritingSubmission:
            {
                if (!Guid.TryParse(use.ResourceId, out var submissionId))
                {
                    return new UseStatus(use, UseState.Dead, null, use.ResourceId);
                }
                var id = submissionId.ToString("D");
                var row = await db.WritingSubmissions.AsNoTracking()
                    .Where(s => s.Id == submissionId)
                    .Select(s => new { s.Status, s.FailureRetryable })
                    .FirstOrDefaultAsync(ct);
                var state = row?.Status switch
                {
                    null => UseState.Dead,
                    WritingSubmissionStatuses.Graded => UseState.Done,
                    // Failed grading never counts. A retryable failure re-enters the same use through
                    // RetryGradeAsync (the grading gate); a FINAL failure (task not ready, manual review,
                    // letter invalid) can never be graded, so the use is Dead and the learner can start
                    // over instead of being parked on a card with no Retry.
                    WritingSubmissionStatuses.Failed => row!.FailureRetryable == false ? UseState.Dead : UseState.Failed,
                    _ => UseState.Grading, // queued / preflight / grading
                };
                return new UseStatus(use, state, $"/writing/submissions/{id}/results", id);
            }

            case FreeSampleUse.KindSpeakingSession:
            {
                var sessionId = use.ResourceId;
                var route = $"/speaking/sessions/{Uri.EscapeDataString(sessionId)}/results";
                var session = await db.SpeakingSessions.AsNoTracking()
                    .Where(s => s.Id == sessionId)
                    .Select(s => new { s.State, s.SubmittedAt })
                    .FirstOrDefaultAsync(ct);
                if (session is null) return new UseStatus(use, UseState.Dead, null, sessionId);

                var assessed = await db.SpeakingAiAssessments.AsNoTracking()
                        .AnyAsync(a => a.SpeakingSessionId == sessionId, ct)
                    || await db.SpeakingSimulationV11Assessments.AsNoTracking()
                        .AnyAsync(a => a.SpeakingSessionId == sessionId
                            && a.AssessmentKind == "card"
                            && a.Status == SpeakingSimulationV11AssessmentStatus.Complete, ct);
                if (assessed) return new UseStatus(use, UseState.Done, route, sessionId);
                if (session.State is SpeakingSessionState.Cancelled or SpeakingSessionState.Expired)
                {
                    return new UseStatus(use, UseState.Dead, route, sessionId);
                }
                if (session.SubmittedAt is null) return new UseStatus(use, UseState.PreSubmit, route, sessionId);

                // Submitted without a result: grading, unless it failed terminally
                // (then it neither counts nor blocks; ai-assess may still retry it).
                var failed = await db.SpeakingSimulationV11Assessments.AsNoTracking()
                        .AnyAsync(a => a.SpeakingSessionId == sessionId
                            && (a.Status == SpeakingSimulationV11AssessmentStatus.TechnicalReview
                                || a.Status == SpeakingSimulationV11AssessmentStatus.Invalid), ct)
                    || await db.AiOperations.AsNoTracking()
                        .AnyAsync(o => o.ResourceType == "speaking_session"
                            && o.ResourceId == sessionId
                            && o.FeatureCode == AiFeatureCodes.SpeakingGrade
                            && (o.State == AiOperationState.FailedTerminal
                                || o.State == AiOperationState.BlockedBudget
                                || o.State == AiOperationState.Indeterminate
                                || o.State == AiOperationState.SkippedNoEvidence
                                || o.State == AiOperationState.Cancelled), ct);
                return new UseStatus(use, failed ? UseState.Dead : UseState.Grading, route, sessionId);
            }

            default: // legacy_attempt — pre-session recorder attempts (in flight at cut-over)
            {
                var attemptId = use.ResourceId;
                var attemptState = await db.Attempts.AsNoTracking()
                    .Where(a => a.Id == attemptId)
                    .Select(a => (AttemptState?)a.State)
                    .FirstOrDefaultAsync(ct);
                if (attemptState is null or AttemptState.Failed or AttemptState.Abandoned)
                {
                    return new UseStatus(use, UseState.Dead, null, attemptId);
                }
                if (attemptState is AttemptState.NotStarted or AttemptState.InProgress or AttemptState.Paused)
                {
                    return new UseStatus(use, UseState.PreSubmit, null, attemptId);
                }

                // Attempt.State alone is not the truth (P0 22 Sep 2026): the
                // pipeline sets Submitted on BOTH a hand-off to grading AND a
                // terminal transcription/budget failure — the latest Evaluation
                // row decides.
                var evaluation = await db.Evaluations.AsNoTracking()
                    .Where(e => e.AttemptId == attemptId)
                    .OrderByDescending(e => e.LastTransitionAt)
                    .Select(e => new { e.Id, e.State })
                    .FirstOrDefaultAsync(ct);
                var route = evaluation is null ? null : $"/speaking/results/{Uri.EscapeDataString(evaluation.Id)}";
                var state = evaluation?.State switch
                {
                    AsyncState.Completed => UseState.Done,
                    AsyncState.Failed => UseState.Dead,
                    _ => attemptState == AttemptState.Completed ? UseState.Done : UseState.Grading,
                };
                return new UseStatus(use, state, route, attemptId);
            }
        }
    }

    private Task<FreeSampleClaim?> GetClaimAsync(string userId, string subtest, CancellationToken ct)
        => db.FreeSampleClaims.FirstOrDefaultAsync(c => c.UserId == userId && c.Subtest == subtest, ct);

    private async Task<bool> IsEnabledAsync(CancellationToken ct)
    {
        var flag = await db.FeatureFlags.AsNoTracking()
            .Where(f => f.Key == FeatureFlagKey)
            .OrderByDescending(f => f.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        return flag?.Enabled ?? false; // fail CLOSED: a billing bypass must be switched on deliberately
    }

    private async Task<ResolvedContent?> ResolveContentAsync(string subtest, string contentRef, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(contentRef)) return null;

        if (subtest == Writing)
        {
            if (!Guid.TryParse(contentRef, out var scenarioId)) return null;
            var profession = await db.WritingScenarios.AsNoTracking()
                .Where(s => s.Id == scenarioId)
                .Select(s => s.Profession)
                .FirstOrDefaultAsync(ct);
            return profession is null
                ? null
                : new ResolvedContent(NormalizeProfession(profession), scenarioId.ToString("D"));
        }

        var card = await db.RolePlayCards.AsNoTracking()
            .Where(c => c.Id == contentRef || c.ContentItemId == contentRef)
            .Select(c => new { c.Id, c.ProfessionId })
            .FirstOrDefaultAsync(ct);
        return card is null ? null : new ResolvedContent(NormalizeProfession(card.ProfessionId), card.Id);
    }

    /// <summary>profession → the content id offered as its free sample. An admin
    /// designation wins while its item is still live; otherwise (no row, or a stale
    /// one) the lowest-order live item of the profession is auto-picked.</summary>
    private async Task<IReadOnlyDictionary<string, string>> ResolvePicksAsync(string subtest, CancellationToken ct)
    {
        var live = subtest == Writing ? await LoadLiveWritingAsync(ct) : await LoadLiveSpeakingAsync(ct);
        var picks = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var candidate in live)
        {
            picks.TryAdd(candidate.Profession, candidate.ContentId); // `live` is already lowest-order first
        }

        var config = await db.FreeTierConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        var configured = ReadSelectionMap(
            subtest == Writing
                ? config?.FreeWritingScenarioByProfessionJson
                : config?.FreeSpeakingCardByProfessionJson);
        foreach (var selection in configured)
        {
            var profession = NormalizeProfession(selection.Key);
            var contentId = selection.Value?.Trim();
            if (string.IsNullOrWhiteSpace(profession) || string.IsNullOrWhiteSpace(contentId)) continue;
            if (live.Any(candidate => candidate.Profession == profession
                && string.Equals(candidate.ContentId, contentId, StringComparison.OrdinalIgnoreCase)))
            {
                picks[profession] = live.First(candidate => candidate.Profession == profession
                    && string.Equals(candidate.ContentId, contentId, StringComparison.OrdinalIgnoreCase)).ContentId;
            }
        }

        var designations = await db.FreeSampleDesignations.AsNoTracking()
            .Where(d => d.Subtest == subtest)
            .ToListAsync(ct);
        foreach (var designation in designations)
        {
            var profession = NormalizeProfession(designation.Profession);
            if (live.Any(l => l.Profession == profession && l.ContentId == designation.ContentId))
            {
                picks[profession] = designation.ContentId;
            }
        }
        return picks;
    }

    /// <summary>Published + candidate-loadable Writing tasks, in learner-library
    /// order (Difficulty, Title, Id) so the auto-pick is what a learner sees first.</summary>
    private async Task<List<ResolvedContent>> LoadLiveWritingAsync(CancellationToken ct)
    {
        var rows = await db.WritingScenarios.AsNoTracking()
            .Where(s => s.Status == "published")
            .Select(s => new
            {
                s.Id,
                s.Profession,
                s.Difficulty,
                s.Title,
                // Same rule as WritingScenarioService.IsCandidateLoadable.
                HasReadable = (s.TaskPromptMarkdown != null && s.TaskPromptMarkdown.Trim() != "")
                    || (s.StimulusPdfMediaAssetId != null && s.StimulusPdfMediaAssetId.Trim() != ""),
                HasCaseNotes = db.WritingScenarioStructuredSentences.Any(x => x.ScenarioId == s.Id),
            })
            .ToListAsync(ct);
        return rows
            .Where(r => r.HasReadable && r.HasCaseNotes && !string.IsNullOrWhiteSpace(r.Profession))
            .OrderBy(r => r.Difficulty)
            .ThenBy(r => r.Title, StringComparer.Ordinal)
            .ThenBy(r => r.Id)
            .Select(r => new ResolvedContent(NormalizeProfession(r.Profession), r.Id.ToString("D")))
            .ToList();
    }

    /// <summary>Published role-play cards whose content-item shell is also published
    /// (grading needs both), ordered by card number, then creation, then id.</summary>
    private async Task<List<ResolvedContent>> LoadLiveSpeakingAsync(CancellationToken ct)
    {
        var rows = await (
            from card in db.RolePlayCards.AsNoTracking()
            join item in db.ContentItems.AsNoTracking() on card.ContentItemId equals item.Id
            where card.Status == ContentStatus.Published
                && item.Status == ContentStatus.Published
                && item.SubtestCode == Speaking
            select new { card.Id, card.ProfessionId, card.DisplayCardNumber, card.CreatedAt })
            .ToListAsync(ct);
        var cardIds = rows.Select(row => row.Id).ToArray();
        var unresolvedProjectionIds = cardIds.Length == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : await db.InterlocutorScripts.AsNoTracking()
                .Where(script => cardIds.Contains(script.RolePlayCardId)
                    && script.ContentOrigin == "live_voice_projection"
                    && script.NeedsOwnerInput)
                .Select(script => script.RolePlayCardId)
                .ToHashSetAsync(ct);
        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.ProfessionId)
                && !unresolvedProjectionIds.Contains(r.Id))
            .OrderBy(r => r.DisplayCardNumber ?? int.MaxValue)
            .ThenBy(r => r.CreatedAt)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new ResolvedContent(NormalizeProfession(r.ProfessionId), r.Id))
            .ToList();
    }

    private static Dictionary<string, string?> ReadSelectionMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string?>>(json, SelectionJsonOptions);
            return parsed is null
                ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                : parsed.ToDictionary(
                    pair => NormalizeProfession(pair.Key),
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
