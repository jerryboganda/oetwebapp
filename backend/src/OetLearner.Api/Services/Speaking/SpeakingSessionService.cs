using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Phase 2 (B.3) of the OET Speaking module roadmap.
///
/// Owns the typed Speaking session lifecycle (prep → active → finished)
/// for both <c>ai_self_practice</c> / <c>ai_exam</c> and (eventually)
/// <c>live_tutor</c> modes. Every transition writes through the new
/// <see cref="SpeakingSession"/> table AND mirrors into a legacy
/// <see cref="Attempt"/> row so the existing learner history / analytics
/// pipelines keep working until Phase 4 retires the dual write.
///
/// The card surface emitted here is intentionally identical to
/// <c>LearnerService.SpeakingRolePlayCards.GetSpeakingRolePlayCardForLearnerAsync</c>
/// so there is one source of truth for the candidate-card schema. The
/// projection NEVER touches <see cref="InterlocutorScript"/>.
/// </summary>
public sealed class SpeakingSessionService(LearnerDbContext db)
{
    private const string DefaultConsentVersion = "recording.v1";

    public async Task<CreateSpeakingSessionResponse> CreateSessionAsync(
        string userId,
        CreateSpeakingSessionRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw ApiException.Unauthorized("speaking_session_unauthenticated",
                "You must be signed in to start a Speaking session.");
        }
        if (req is null || string.IsNullOrWhiteSpace(req.RolePlayCardId))
        {
            throw ApiException.Validation("ROLE_PLAY_CARD_ID_REQUIRED",
                "Role-play card id is required.");
        }

        var card = await db.RolePlayCards.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == req.RolePlayCardId, ct)
            ?? throw ApiException.NotFound("role_play_card_not_found",
                "That role-play card does not exist.");

        if (card.Status != ContentStatus.Published)
        {
            throw ApiException.Conflict("role_play_card_not_published",
                "That role-play card is not currently available for practice.");
        }

        var mode = SpeakingSessionModes.Parse(req.Mode);
        var consentVersion = string.IsNullOrWhiteSpace(req.ConsentVersion)
            ? DefaultConsentVersion
            : req.ConsentVersion!.Trim();

        var now = DateTimeOffset.UtcNow;
        var sessionId = $"sps_{Guid.NewGuid():N}";
        var attemptId = $"att_{Guid.NewGuid():N}";

        var attempt = new Attempt
        {
            Id = attemptId,
            UserId = userId,
            ContentId = card.ContentItemId,
            SubtestCode = "speaking",
            Context = "practice",
            Mode = SpeakingSessionModes.ToCode(mode),
            State = AttemptState.InProgress,
            StartedAt = now,
            CreatedAt = now,
            ExamFamilyCode = "oet",
            ExamTypeCode = "oet",
        };

        var session = new SpeakingSession
        {
            Id = sessionId,
            UserId = userId,
            RolePlayCardId = card.Id,
            MockSetId = string.IsNullOrWhiteSpace(req.MockSetId) ? null : req.MockSetId,
            Mode = mode,
            State = SpeakingSessionState.Prep,
            AttemptId = attemptId,
            PrepStartedAt = now,
            ConsentVersion = consentVersion,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Attempts.Add(attempt);
        db.SpeakingSessions.Add(session);
        await db.SaveChangesAsync(ct);

        var prepEndsAt = now.AddSeconds(card.PrepTimeSeconds);
        var rolePlayEndsAt = prepEndsAt.AddSeconds(card.RolePlayTimeSeconds);

        return new CreateSpeakingSessionResponse(
            SessionId: sessionId,
            PrepStartedAt: now,
            PrepEndsAt: prepEndsAt,
            RolePlayEndsAt: rolePlayEndsAt,
            ConsentVersion: consentVersion,
            Card: ProjectLearnerCard(card));
    }

    public async Task<SpeakingSessionDetail> GetSessionForLearnerAsync(
        string userId,
        string sessionId,
        CancellationToken ct)
    {
        var session = await LoadOwnedSessionAsync(userId, sessionId, ct);
        var card = await db.RolePlayCards.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == session.RolePlayCardId, ct)
            ?? throw ApiException.NotFound("role_play_card_not_found",
                "That role-play card does not exist.");

        return new SpeakingSessionDetail(
            SessionId: session.Id,
            Mode: SpeakingSessionModes.ToCode(session.Mode),
            State: SpeakingSessionStates.ToCode(session.State),
            RolePlayCardId: session.RolePlayCardId,
            PrepStartedAt: session.PrepStartedAt,
            RolePlayStartedAt: session.RolePlayStartedAt,
            EndedAt: session.EndedAt,
            ElapsedSeconds: session.ElapsedSeconds,
            ConsentVersion: session.ConsentVersion,
            Card: ProjectLearnerCard(card));
    }

    public async Task<SpeakingSessionDetail> StartRolePlayAsync(
        string userId,
        string sessionId,
        CancellationToken ct)
    {
        var session = await LoadOwnedSessionAsync(userId, sessionId, ct, tracking: true);
        if (session.State != SpeakingSessionState.Prep)
        {
            throw ApiException.Conflict("speaking_session_invalid_state",
                $"Role-play can only start from the prep state (current: {SpeakingSessionStates.ToCode(session.State)}).");
        }

        var now = DateTimeOffset.UtcNow;
        session.State = SpeakingSessionState.Active;
        session.RolePlayStartedAt = now;
        session.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return await GetSessionForLearnerAsync(userId, sessionId, ct);
    }

    public async Task<SpeakingSessionDetail> EndSessionAsync(
        string userId,
        string sessionId,
        CancellationToken ct)
    {
        var session = await LoadOwnedSessionAsync(userId, sessionId, ct, tracking: true);
        if (session.State != SpeakingSessionState.Active)
        {
            throw ApiException.Conflict("speaking_session_invalid_state",
                $"Only an active session can be ended (current: {SpeakingSessionStates.ToCode(session.State)}).");
        }

        var now = DateTimeOffset.UtcNow;
        session.State = SpeakingSessionState.Finished;
        session.EndedAt = now;
        session.UpdatedAt = now;
        var roleplayStart = session.RolePlayStartedAt ?? now;
        session.ElapsedSeconds = (int)Math.Max(0, (now - roleplayStart).TotalSeconds);

        // Mirror to legacy Attempt so existing dashboards and history
        // surfaces continue to see the completed attempt.
        if (!string.IsNullOrWhiteSpace(session.AttemptId))
        {
            var attempt = await db.Attempts.FirstOrDefaultAsync(a => a.Id == session.AttemptId, ct);
            if (attempt is not null)
            {
                attempt.State = AttemptState.Submitted;
                attempt.SubmittedAt = now;
                attempt.ElapsedSeconds = session.ElapsedSeconds;
            }
        }

        await db.SaveChangesAsync(ct);
        return await GetSessionForLearnerAsync(userId, sessionId, ct);
    }

    public async Task<SpeakingSessionDetail> MarkConsentAsync(
        string userId,
        string sessionId,
        string consentVersion,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(consentVersion))
        {
            throw ApiException.Validation("CONSENT_VERSION_REQUIRED",
                "Consent version is required.");
        }

        var session = await LoadOwnedSessionAsync(userId, sessionId, ct, tracking: true);
        var now = DateTimeOffset.UtcNow;
        session.ConsentVersion = consentVersion.Trim();
        session.ConsentAcceptedAt = now;
        session.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return await GetSessionForLearnerAsync(userId, sessionId, ct);
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private async Task<SpeakingSession> LoadOwnedSessionAsync(
        string userId,
        string sessionId,
        CancellationToken ct,
        bool tracking = false)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw ApiException.Unauthorized("speaking_session_unauthenticated",
                "You must be signed in to interact with a Speaking session.");
        }
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw ApiException.Validation("SPEAKING_SESSION_ID_REQUIRED",
                "Speaking session id is required.");
        }

        var q = tracking
            ? db.SpeakingSessions
            : db.SpeakingSessions.AsNoTracking();
        var session = await q.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw ApiException.NotFound("speaking_session_not_found",
                "That Speaking session does not exist.");

        if (!string.Equals(session.UserId, userId, StringComparison.Ordinal))
        {
            // Use NotFound rather than Forbidden so we do not leak which
            // session ids exist for other users (IDOR guard).
            throw ApiException.NotFound("speaking_session_not_found",
                "That Speaking session does not exist.");
        }

        return session;
    }

    /// <summary>
    /// Learner-safe card projection. Mirrors
    /// <c>LearnerService.SpeakingRolePlayCards.GetSpeakingRolePlayCardForLearnerAsync</c>
    /// exactly so both endpoints share one wire shape. NEVER serialises
    /// any field from <see cref="InterlocutorScript"/>.
    /// </summary>
    public static object ProjectLearnerCard(RolePlayCard card)
    {
        var tasks = new[] { card.Task1, card.Task2, card.Task3, card.Task4, card.Task5 }
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .ToArray();

        var criteriaFocus = AdminService.DeserializeCriteriaFocus(card.CriteriaFocusJson);

        return new
        {
            cardId = card.Id,
            professionId = card.ProfessionId,
            scenarioTitle = card.ScenarioTitle,
            setting = card.Setting,
            candidateRole = card.CandidateRole,
            interlocutorRole = card.InterlocutorRole,
            patientName = card.PatientName,
            patientAge = card.PatientAge,
            background = card.Background,
            tasks,
            allowedNotes = card.AllowedNotes,
            prepTimeSeconds = card.PrepTimeSeconds,
            rolePlayTimeSeconds = card.RolePlayTimeSeconds,
            patientEmotion = card.PatientEmotion,
            communicationGoal = card.CommunicationGoal,
            clinicalTopic = card.ClinicalTopic,
            difficulty = card.Difficulty,
            criteriaFocus,
            disclaimer = card.Disclaimer,
        };
    }
}
