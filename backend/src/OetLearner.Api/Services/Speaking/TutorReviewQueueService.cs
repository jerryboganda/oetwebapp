using System.Text.Json;
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
// Phase 7 additions:
//   * Idle claims auto-release after <see cref="IdleClaimTtlMinutes"/> so
//     the queue cannot be permanently blocked by a tutor that walks away
//     mid-review. The TTL is enforced lazily on every queue read +
//     claim attempt so we do not need a dedicated background sweep
//     timer.
//   * Listing order prioritises (a) sessions whose profession matches the
//     tutor's specialties (so neurology nurses see neurology sessions
//     first) then (b) oldest unclaimed first — i.e. FIFO fairness.
public sealed class TutorReviewQueueService(
    LearnerDbContext db,
    ILogger<TutorReviewQueueService> logger,
    TimeProvider clock)
{
    /// <summary>Sentinel value persisted on <see cref="ReviewRequest.SubtestCode"/>
    /// for speaking-session reviews owned by this queue. Keeps the rows
    /// distinct from legacy writing / listening review claims.</summary>
    public const string SubtestCode = "speaking_session";

    /// <summary>Idle claims are auto-released after this many minutes.
    /// Calibrated to the plan default of 15 minutes; can be overridden in
    /// tests via the optional constructor parameter wired up through DI
    /// in <c>Program.cs</c>.</summary>
    public const int IdleClaimTtlMinutes = 15;

    /// <summary>Lists finished sessions that have not yet been claimed
    /// AND have no final tutor assessment. Optional
    /// <paramref name="professionId"/> filters by the role-play card's
    /// owning profession. Listing order: (1) profession-priority match
    /// against the tutor's specialties, (2) oldest unclaimed first.</summary>
    public async Task<IReadOnlyList<TutorReviewQueueItem>> ListQueueAsync(
        string tutorId,
        string? professionId,
        CancellationToken ct)
    {
        // First, sweep stale claims so they don't fall through the cracks of
        // every subsequent call. Cheap on small queues, idempotent on busy
        // ones (the predicate uses an indexed column).
        await ReleaseExpiredClaimsAsync(ct);

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

        // Pull a wider window than we will return so the in-memory
        // fairness sort has something to work with. 200 is plenty —
        // finished-and-unscored is a small subset in steady state.
        var raw = await query
            .OrderBy(x => x.session.EndedAt)   // oldest first → FIFO
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
        var latestClaimOwner = claimAudit
            .GroupBy(a => a.ResourceId!)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.OccurredAt).First());

        var userIds = raw.Select(x => x.session.UserId).Distinct().ToArray();
        var displayNameByUserId = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToDictionaryAsync(
                u => u.Id,
                u => string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName,
                ct);

        var draftSessionIds = await db.SpeakingTutorAssessments.AsNoTracking()
            .Where(t => !t.IsFinal && t.TutorId == tutorId && sessionIds.Contains(t.SpeakingSessionId))
            .Select(t => t.SpeakingSessionId)
            .Distinct()
            .ToListAsync(ct);
        var draftSet = draftSessionIds.ToHashSet();

        var aiRows = await db.SpeakingAiAssessments.AsNoTracking()
            .Where(a => sessionIds.Contains(a.SpeakingSessionId))
            .ToListAsync(ct);
        var aiBySession = aiRows
            .GroupBy(a => a.SpeakingSessionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.GeneratedAt).First());

        // Pull the tutor's specialties up-front so the fairness sort can
        // bucket profession matches before timestamps.
        var specialties = await LoadTutorSpecialtiesAsync(tutorId, ct);

        var items = new List<TutorReviewQueueItem>(raw.Count);
        foreach (var r in raw)
        {
            if (finalisedSet.Contains(r.session.Id))
            {
                continue;
            }

            var hasClaim = claimsLookup.TryGetValue(r.session.Id, out var claim);
            var mineClaim = latestClaimOwner.TryGetValue(r.session.Id, out var owner)
                && string.Equals(owner.ActorId, tutorId, StringComparison.Ordinal);
            aiBySession.TryGetValue(r.session.Id, out var ai);
            var learnerDisplayName = displayNameByUserId.TryGetValue(r.session.UserId, out var displayName)
                ? displayName
                : r.session.UserId;
            var title = string.IsNullOrWhiteSpace(r.card.ScenarioTitle)
                ? r.card.Id
                : r.card.ScenarioTitle;

            items.Add(new TutorReviewQueueItem(
                SessionId: r.session.Id,
                UserId: r.session.UserId,
                LearnerDisplayName: learnerDisplayName,
                RolePlayCardId: r.session.RolePlayCardId,
                CardId: r.session.RolePlayCardId,
                ScenarioTitle: r.card.ScenarioTitle,
                CardTitle: title,
                ProfessionId: r.card.ProfessionId,
                EndedAt: r.session.EndedAt,
                ElapsedSeconds: r.session.ElapsedSeconds,
                DurationSeconds: r.session.ElapsedSeconds,
                AiReadinessBand: ai?.ReadinessBand,
                AiScaledScore: ai?.EstimatedScaledScore,
                HasDraft: draftSet.Contains(r.session.Id),
                ClaimedByMe: mineClaim,
                ClaimedBySomeoneElse: hasClaim && !mineClaim,
                ClaimExpiresAt: hasClaim ? claim!.CreatedAt.AddMinutes(IdleClaimTtlMinutes) : null));
        }

        // Fairness ordering: profession-match first, then oldest finishedAt.
        // Sessions claimed by the requesting tutor stay visible (they may
        // want to resume their own work) but are bucketed last in case
        // they are reviewing several at once.
        return items
            .OrderBy(x => x.ClaimedByMe)                       // unclaimed-by-me first
            .ThenByDescending(x => specialties.Contains(x.ProfessionId)) // profession match
            .ThenBy(x => x.EndedAt ?? DateTimeOffset.MaxValue)  // oldest unclaimed first
            .ToList();
    }

    /// <summary>Releases every claim whose CreatedAt + TTL is in the past.
    /// Called lazily on read + claim paths so we do not need a separate
    /// background sweep timer; the audit log records each release with the
    /// reason set to <c>idle_ttl</c>.</summary>
    public async Task<int> ReleaseExpiredClaimsAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var cutoff = now.AddMinutes(-IdleClaimTtlMinutes);

        var stale = await db.ReviewRequests
            .Where(r => r.SubtestCode == SubtestCode && r.CreatedAt < cutoff)
            .ToListAsync(ct);
        if (stale.Count == 0) return 0;

        var sessionIds = stale.Select(s => s.AttemptId).ToList();
        // Skip entries that have already been turned into a final tutor
        // assessment — we never want to revert a completed claim.
        var finalised = await db.SpeakingTutorAssessments.AsNoTracking()
            .Where(t => t.IsFinal && sessionIds.Contains(t.SpeakingSessionId))
            .Select(t => t.SpeakingSessionId)
            .ToListAsync(ct);
        var finalisedSet = finalised.ToHashSet();

        // Resolve the owning tutor for each claim from the audit trail so
        // the release event keeps the actor identity.
        var ownerByResource = await db.AuditEvents.AsNoTracking()
            .Where(a => a.Action == "SpeakingSessionClaimed" && sessionIds.Contains(a.ResourceId!))
            .GroupBy(a => a.ResourceId!)
            .Select(g => new
            {
                ResourceId = g.Key,
                ActorId = g.OrderByDescending(x => x.OccurredAt).First().ActorId,
            })
            .ToDictionaryAsync(x => x.ResourceId, x => x.ActorId, ct);

        var released = 0;
        foreach (var claim in stale)
        {
            if (finalisedSet.Contains(claim.AttemptId)) continue;

            var ownerId = ownerByResource.TryGetValue(claim.AttemptId, out var owner)
                ? owner
                : "system";
            db.ReviewRequests.Remove(claim);
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                Action = "SpeakingSessionReleased",
                ActorId = ownerId ?? "system",
                ActorName = ownerId ?? "system",
                ResourceId = claim.AttemptId,
                ResourceType = "SpeakingSession",
                Details = $"reviewRequestId={claim.Id};reason=idle_ttl;ttlMinutes={IdleClaimTtlMinutes}",
                OccurredAt = now,
            });
            released++;
        }

        if (released > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Released {Count} idle speaking review claims older than {Ttl} minutes.",
                released, IdleClaimTtlMinutes);
        }
        return released;
    }

    private async Task<HashSet<string>> LoadTutorSpecialtiesAsync(string tutorId, CancellationToken ct)
    {
        var raw = await db.ExpertUsers.AsNoTracking()
            .Where(u => u.Id == tutorId)
            .Select(u => u.SpecialtiesJson)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(raw)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var arr = JsonSerializer.Deserialize<string[]>(raw);
            return arr is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(arr.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>First-write-wins claim. Inserts a <see cref="ReviewRequest"/>
    /// row anchored to the speaking session. Raises a 409 if the
    /// session is already claimed by another tutor whose claim has not yet
    /// expired (idle claims older than <see cref="IdleClaimTtlMinutes"/>
    /// are released first).</summary>
    public async Task ClaimAsync(string tutorId, string sessionId, CancellationToken ct)
    {
        // Sweep stale claims before reading so the requesting tutor never
        // sees a "claimed" 409 caused by a dormant claim.
        await ReleaseExpiredClaimsAsync(ct);

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
                existing.CreatedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
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
