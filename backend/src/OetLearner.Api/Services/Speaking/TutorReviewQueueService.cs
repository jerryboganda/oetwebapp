using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

// Phase 4 (B.4) of the OET Speaking module roadmap.
//
// Surfaces the tutor review queue: every finished speaking session that
// has not yet received a final tutor assessment is eligible for claim.
// Claims reuse the existing <see cref="ReviewRequest"/> table — the
// subtest code is fixed at <c>speaking_session</c> so legacy
// writing-only review flows are not affected.
//
// The queue intentionally does NOT pre-filter by tutor specialty: the
// admin UI surfaces all professions and the optional
// <c>professionId</c> query parameter narrows the listing on demand.
public sealed class TutorReviewQueueService(
    LearnerDbContext db,
    ILogger<TutorReviewQueueService> logger,
    TimeProvider clock)
{
    /// <summary>Sentinel value persisted on <see cref="ReviewRequest.SubtestCode"/>
    /// for speaking-session reviews owned by this queue. Keeps the rows
    /// distinct from legacy writing / listening review claims.</summary>
    public const string SubtestCode = "speaking_session";

    /// <summary>Lists finished sessions that have not yet been claimed
    /// AND have no final tutor assessment. Optional
    /// <paramref name="professionId"/> filters by the role-play card's
    /// owning profession.</summary>
    public async Task<IReadOnlyList<TutorReviewQueueItem>> ListQueueAsync(
        string tutorId,
        string? professionId,
        CancellationToken ct)
    {
        var query = from session in db.SpeakingSessions.AsNoTracking()
                    join card in db.RolePlayCards.AsNoTracking()
                        on session.RolePlayCardId equals card.Id
                    where session.State == SpeakingSessionState.Finished
                    select new { session, card };

        if (!string.IsNullOrWhiteSpace(professionId))
        {
            var pid = professionId.Trim().ToLowerInvariant();
            query = query.Where(x => x.card.ProfessionId == pid);
        }

        var raw = await query
            .OrderByDescending(x => x.session.EndedAt)
            .Take(200)
            .ToListAsync(ct);

        if (raw.Count == 0)
        {
            return Array.Empty<TutorReviewQueueItem>();
        }

        var sessionIds = raw.Select(x => x.session.Id).ToHashSet();

        // Finalised assessments — exclude these sessions entirely.
        var finalised = await db.SpeakingTutorAssessments.AsNoTracking()
            .Where(t => t.IsFinal && sessionIds.Contains(t.SpeakingSessionId))
            .Select(t => t.SpeakingSessionId)
            .ToListAsync(ct);
        var finalisedSet = finalised.ToHashSet();

        // Claims — a row in ReviewRequest with the speaking-session
        // subtest code and the session id stored in AttemptId.
        var claims = await db.ReviewRequests.AsNoTracking()
            .Where(r => r.SubtestCode == SubtestCode && sessionIds.Contains(r.AttemptId))
            .Select(r => new { r.AttemptId, r.State, r.Id, r.CreatedAt })
            .ToListAsync(ct);

        // Map session.Id → claim metadata via the audit/details
        // serialisation we do at claim time (see ClaimAsync).
        var claimsLookup = claims
            .GroupBy(c => c.AttemptId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.CreatedAt).First());

        // Look up the assigned tutor id from the audit event we write
        // alongside each claim. Falls back to "claimed by someone" when
        // the metadata can't be resolved.
        var claimAudit = await db.AuditEvents.AsNoTracking()
            .Where(a => a.Action == "SpeakingSessionClaimed" && sessionIds.Contains(a.ResourceId!))
            .Select(a => new { a.ResourceId, a.ActorId, a.OccurredAt })
            .ToListAsync(ct);
        var latestClaimByMe = claimAudit
            .Where(a => a.ActorId == tutorId)
            .GroupBy(a => a.ResourceId!)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.OccurredAt).First());

        var items = new List<TutorReviewQueueItem>(raw.Count);
        foreach (var r in raw)
        {
            if (finalisedSet.Contains(r.session.Id))
            {
                continue;
            }

            var hasClaim = claimsLookup.TryGetValue(r.session.Id, out var claim);
            var mineClaim = latestClaimByMe.TryGetValue(r.session.Id, out _);

            items.Add(new TutorReviewQueueItem(
                SessionId: r.session.Id,
                UserId: r.session.UserId,
                RolePlayCardId: r.session.RolePlayCardId,
                ScenarioTitle: r.card.ScenarioTitle,
                ProfessionId: r.card.ProfessionId,
                EndedAt: r.session.EndedAt,
                ElapsedSeconds: r.session.ElapsedSeconds,
                ClaimedByMe: mineClaim,
                ClaimedBySomeoneElse: hasClaim && !mineClaim));
        }

        return items;
    }

    /// <summary>First-write-wins claim. Inserts a <see cref="ReviewRequest"/>
    /// row anchored to the speaking session. Raises a 409 if the
    /// session is already claimed by another tutor.</summary>
    public async Task ClaimAsync(string tutorId, string sessionId, CancellationToken ct)
    {
        var session = await db.SpeakingSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
        {
            throw ApiException.NotFound("speaking_session_not_found", "Speaking session not found.");
        }
        if (session.State != SpeakingSessionState.Finished)
        {
            throw ApiException.Validation(
                "speaking_session_not_finished",
                "Only finished sessions may be claimed for review.");
        }

        var existing = await db.ReviewRequests
            .FirstOrDefaultAsync(r => r.AttemptId == sessionId && r.SubtestCode == SubtestCode, ct);
        if (existing is not null)
        {
            // If the claim already belongs to the requesting tutor we
            // treat the call as idempotent.
            var auditOwner = await db.AuditEvents.AsNoTracking()
                .Where(a => a.Action == "SpeakingSessionClaimed" && a.ResourceId == sessionId)
                .OrderByDescending(a => a.OccurredAt)
                .Select(a => a.ActorId)
                .FirstOrDefaultAsync(ct);
            if (auditOwner == tutorId)
            {
                return;
            }
            throw ApiException.Conflict(
                "speaking_session_already_claimed",
                "This session has already been claimed by another tutor.");
        }

        var now = clock.GetUtcNow();
        var claimId = Guid.NewGuid().ToString("N");
        db.ReviewRequests.Add(new ReviewRequest
        {
            Id = claimId,
            AttemptId = sessionId,
            SubtestCode = SubtestCode,
            State = ReviewRequestState.InReview,
            TurnaroundOption = "standard",
            PaymentSource = "internal",
            CreatedAt = now,
            FocusAreasJson = "[]",
            EligibilitySnapshotJson = "{}",
        });
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            Action = "SpeakingSessionClaimed",
            ActorId = tutorId,
            ActorName = tutorId,
            ResourceId = sessionId,
            ResourceType = "SpeakingSession",
            Details = $"reviewRequestId={claimId}",
            OccurredAt = now,
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Speaking session claimed tutor={TutorId} session={SessionId} reviewRequestId={ClaimId}",
            tutorId, sessionId, claimId);
    }

    /// <summary>Releases the current tutor's claim on a session. No-op
    /// when the claim does not exist or is owned by someone else.</summary>
    public async Task ReleaseAsync(string tutorId, string sessionId, CancellationToken ct)
    {
        var existing = await db.ReviewRequests
            .FirstOrDefaultAsync(r => r.AttemptId == sessionId && r.SubtestCode == SubtestCode, ct);
        if (existing is null)
        {
            return;
        }

        var auditOwner = await db.AuditEvents.AsNoTracking()
            .Where(a => a.Action == "SpeakingSessionClaimed" && a.ResourceId == sessionId)
            .OrderByDescending(a => a.OccurredAt)
            .Select(a => a.ActorId)
            .FirstOrDefaultAsync(ct);
        if (auditOwner != tutorId)
        {
            throw ApiException.Forbidden(
                "speaking_session_claim_forbidden",
                "Only the claiming tutor may release a claim.");
        }

        db.ReviewRequests.Remove(existing);
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            Action = "SpeakingSessionReleased",
            ActorId = tutorId,
            ActorName = tutorId,
            ResourceId = sessionId,
            ResourceType = "SpeakingSession",
            Details = $"reviewRequestId={existing.Id}",
            OccurredAt = clock.GetUtcNow(),
        });

        await db.SaveChangesAsync(ct);
    }
}
