using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Mocks.Results;

namespace OetLearner.Api.Services;

// Wave 3 of docs/SPEAKING-MODULE-PLAN.md.
//
// Speaking mock set = the curatorial pairing of two role-play
// `ContentItem`s (both `SubtestCode = "speaking"`). A learner attempt at
// a mock set is a `SpeakingMockSession` linking two child `Attempt`
// rows that share `ComparisonGroupId == session.Id`.
//
// Decisions §6 of the plan locked Q1: free tier may start at most
// `FreeTierConfig.MaxSpeakingMockSets` distinct sessions per rolling 7
// days (default 1). Cap is enforced server-side with a fail-loud
// `ApiException.Conflict("free_tier_speaking_mock_sets_exceeded", …)`.
//
// Combined readiness band is anchored at `OetScoring`'s 350/500 = Grade
// B threshold; per §0 invariants the comparison `>= 350` is never
// inlined — we always project via `OetScoring.SpeakingReadinessBand…`.
public partial class LearnerService
{
    public async Task<object> ListSpeakingMockSetsAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);

        var sets = await db.SpeakingMockSets
            .Where(x => x.Status == SpeakingMockSetStatus.Published)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Title)
            .ToListAsync(cancellationToken);

        var rolling = await GetSpeakingMockSessionRollingUsageAsync(userId, cancellationToken);

        return new
        {
            mockSets = sets.Select(s => new
            {
                mockSetId = s.Id,
                title = s.Title,
                description = s.Description,
                difficulty = s.Difficulty,
                criteriaFocus = SplitCsv(s.CriteriaFocus),
                tags = SplitCsv(s.Tags),
                rolePlay1ContentId = s.RolePlay1ContentId,
                rolePlay2ContentId = s.RolePlay2ContentId,
                publishedAt = s.PublishedAt,
            }).ToArray(),
            entitlement = rolling,
        };
    }

    public async Task<object> StartSpeakingMockSetAsync(
        string userId,
        string mockSetId,
        string mode,
        CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);

        var mockSet = await db.SpeakingMockSets
            .FirstOrDefaultAsync(x => x.Id == mockSetId, cancellationToken)
            ?? throw ApiException.NotFound("speaking_mock_set_not_found", "That speaking mock set does not exist.");

        if (mockSet.Status != SpeakingMockSetStatus.Published)
        {
            throw ApiException.Conflict(
                "speaking_mock_set_not_published",
                "This mock set is not yet published.");
        }

        // Validate underlying role-play content remains published and safe
        // for learner exposure. Admin publish gates catch this earlier, but
        // this guard protects existing sessions if content is later archived
        // or rights status changes.
        var rolePlay1 = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == mockSet.RolePlay1ContentId, cancellationToken);
        var rolePlay2 = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == mockSet.RolePlay2ContentId, cancellationToken);
        if (rolePlay1 is null || rolePlay2 is null
            || !string.Equals(rolePlay1.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(rolePlay2.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase)
            || rolePlay1.Status != ContentStatus.Published
            || rolePlay2.Status != ContentStatus.Published
            || string.IsNullOrWhiteSpace(rolePlay1.PublishedRevisionId)
            || string.IsNullOrWhiteSpace(rolePlay2.PublishedRevisionId)
            || string.IsNullOrWhiteSpace(rolePlay1.SourceProvenance)
            || string.IsNullOrWhiteSpace(rolePlay2.SourceProvenance)
            || string.Equals(rolePlay1.RightsStatus, "recall_unverified", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rolePlay2.RightsStatus, "recall_unverified", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Conflict(
                "speaking_mock_set_invalid",
                "This mock set references content that is missing, unpublished, or not cleared for learner use.");
        }

        // Free-tier rolling-window cap (Q1 of plan §6, locked).
        await EnforceMockSetFreeTierCapAsync(userId, cancellationToken);

        var normalisedMode = string.Equals(mode, "self", StringComparison.OrdinalIgnoreCase) ? "self" : "exam";
        var sessionId = $"sms-{Guid.NewGuid():N}";

        // RP1 starts in-flight (Prep1/Active1); RP2 stays NotStarted until the
        // learner crosses the bridge and explicitly starts role-play 2. This
        // keeps DeriveOrchestratorState from jumping straight to Active2 the
        // moment RP1 is submitted (docs/speaking/state-machines.md:
        // Prep1->Active1->Finished1->Bridge->Prep2->Active2->Finished2).
        var attempt1 = await CreateSpeakingMockAttemptAsync(userId, sessionId, rolePlay1.Id, normalisedMode, AttemptState.InProgress, cancellationToken);
        var attempt2 = await CreateSpeakingMockAttemptAsync(userId, sessionId, rolePlay2.Id, normalisedMode, AttemptState.NotStarted, cancellationToken);

        var session = new SpeakingMockSession
        {
            Id = sessionId,
            MockSetId = mockSet.Id,
            UserId = userId,
            Attempt1Id = attempt1.Id,
            Attempt2Id = attempt2.Id,
            Mode = normalisedMode,
            State = SpeakingMockSessionState.InProgress,
            OrchestratorState = SpeakingMockOrchestratorStates.Prep1,
            StartedAt = DateTimeOffset.UtcNow,
        };
        db.SpeakingMockSessions.Add(session);

        await RecordEventAsync(userId, "speaking_mock_set_started", new
        {
            mockSessionId = session.Id,
            mockSetId = mockSet.Id,
            attempt1Id = attempt1.Id,
            attempt2Id = attempt2.Id,
            mode = normalisedMode,
        }, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return await BuildMockSessionResponseAsync(session, mockSet, cancellationToken);
    }

    public async Task<object> GetSpeakingMockSessionAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);

        var session = await db.SpeakingMockSessions
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, cancellationToken)
            ?? throw ApiException.NotFound("speaking_mock_session_not_found", "That mock session does not exist.");

        var mockSet = await db.SpeakingMockSets
            .FirstAsync(x => x.Id == session.MockSetId, cancellationToken);

        // Sync OrchestratorState with the actual attempt/evaluation state on
        // every read so the orchestrator UI never gets stuck if the learner
        // walked through the legacy task page that doesn't call our
        // bridge endpoints.
        await SyncOrchestratorStateAsync(session, cancellationToken);

        return await BuildMockSessionResponseAsync(session, mockSet, cancellationToken);
    }

    // P5 - Bridge transitions
    //
    // The orchestrator's strict state machine for the two-role-play mock:
    //
    //   Prep1 -> Active1 -> Finished1 -> [Bridge] -> Prep2 -> Active2 -> Finished2 -> Aggregated
    //
    // We can't drive Prep1/Active1/Finished1/Prep2/Active2/Finished2
    // directly from this service because those transitions happen on the
    // child `Attempt` rows (started/submitted via the legacy speaking
    // endpoints). Instead, `SyncOrchestratorStateAsync` is called on every
    // session read and pushes the orchestrator state forward to match the
    // child attempts. The two explicit transitions we DO own here are the
    // bridge endpoints — they're a learner-driven handoff with no audio
    // attached.

    public async Task<object> StartBridgeAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);

        var session = await db.SpeakingMockSessions
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, cancellationToken)
            ?? throw ApiException.NotFound("speaking_mock_session_not_found", "That mock session does not exist.");

        // Auto-advance Prep1/Active1 -> Finished1 first if the child attempt
        // is already complete; otherwise the guard below will reject.
        await SyncOrchestratorStateAsync(session, cancellationToken);

        if (!string.Equals(session.OrchestratorState, SpeakingMockOrchestratorStates.Finished1, StringComparison.Ordinal)
            && !string.Equals(session.OrchestratorState, SpeakingMockOrchestratorStates.Bridge, StringComparison.Ordinal))
        {
            throw ApiException.Conflict(
                "speaking_mock_bridge_invalid_state",
                $"Cannot start the bridge from state '{session.OrchestratorState}'. Role-play 1 must be finished first.");
        }

        if (string.Equals(session.OrchestratorState, SpeakingMockOrchestratorStates.Finished1, StringComparison.Ordinal))
        {
            session.OrchestratorState = SpeakingMockOrchestratorStates.Bridge;
            session.BridgeStartedAt = DateTimeOffset.UtcNow;
            await RecordEventAsync(userId, "speaking_mock_bridge_started", new
            {
                mockSessionId = session.Id,
                mockSetId = session.MockSetId,
            }, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        var mockSet = await db.SpeakingMockSets.FirstAsync(x => x.Id == session.MockSetId, cancellationToken);
        return await BuildMockSessionResponseAsync(session, mockSet, cancellationToken);
    }

    public async Task<object> FinishBridgeAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);

        var session = await db.SpeakingMockSessions
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, cancellationToken)
            ?? throw ApiException.NotFound("speaking_mock_session_not_found", "That mock session does not exist.");

        if (!string.Equals(session.OrchestratorState, SpeakingMockOrchestratorStates.Bridge, StringComparison.Ordinal))
        {
            throw ApiException.Conflict(
                "speaking_mock_bridge_invalid_state",
                $"Cannot finish the bridge from state '{session.OrchestratorState}'. Start the bridge first.");
        }

        session.OrchestratorState = SpeakingMockOrchestratorStates.Prep2;
        await RecordEventAsync(userId, "speaking_mock_bridge_finished", new
        {
            mockSessionId = session.Id,
            mockSetId = session.MockSetId,
            bridgeDurationSeconds = session.BridgeStartedAt is null
                ? (double?)null
                : (DateTimeOffset.UtcNow - session.BridgeStartedAt.Value).TotalSeconds,
        }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var mockSet = await db.SpeakingMockSets.FirstAsync(x => x.Id == session.MockSetId, cancellationToken);
        return await BuildMockSessionResponseAsync(session, mockSet, cancellationToken);
    }

    /// <summary>
    /// P5 - When both halves have a completed AI assessment, project the
    /// combined readiness band and persist a snapshot. Called by
    /// <see cref="GetSpeakingMockSessionAsync"/> and the bridge transitions
    /// so the result is available as soon as the second half scores.
    /// </summary>
    public async Task<object?> AggregateAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);

        var session = await db.SpeakingMockSessions
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, cancellationToken)
            ?? throw ApiException.NotFound("speaking_mock_session_not_found", "That mock session does not exist.");

        if (mockReportAggregation is null) return null;
        var agg = await mockReportAggregation.AggregateSpeakingMockSessionAsync(session.Id, cancellationToken);
        return new
        {
            mockSessionId = agg.MockSessionId,
            combinedScaledScore = agg.CombinedScaledScore,
            readinessBand = agg.ReadinessBandCode,
            readinessBandLabel = agg.ReadinessBandLabel,
            passThreshold = agg.PassThreshold,
            perCriterion = agg.PerCriterion,
            rolePlay1 = agg.RolePlay1,
            rolePlay2 = agg.RolePlay2,
        };
    }

    private async Task SyncOrchestratorStateAsync(
        SpeakingMockSession session,
        CancellationToken cancellationToken)
    {
        // Reading the child attempts is enough to know which half is in
        // which lifecycle stage. A simple state-only sync keeps the call
        // cheap; the full per-attempt summary already happens in
        // BuildMockSessionResponseAsync.
        var attempt1 = await db.Attempts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == session.Attempt1Id, cancellationToken);
        var attempt2 = await db.Attempts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == session.Attempt2Id, cancellationToken);
        if (attempt1 is null || attempt2 is null) return;

        var prev = session.OrchestratorState;
        var next = DeriveOrchestratorState(prev, attempt1, attempt2);

        // Always run aggregation when we land on Aggregated.
        if (string.Equals(next, SpeakingMockOrchestratorStates.Aggregated, StringComparison.Ordinal)
            && mockReportAggregation is not null)
        {
            try
            {
                await mockReportAggregation.AggregateSpeakingMockSessionAsync(session.Id, cancellationToken);
                // Re-read to capture the snapshot the aggregator wrote.
                await db.Entry(session).ReloadAsync(cancellationToken);
                return; // Aggregator already saved.
            }
            catch (InvalidOperationException)
            {
                // Aggregator can no-op if the AI assessments aren't ready
                // yet — fall through to the plain state assignment below.
            }
        }

        if (!string.Equals(prev, next, StringComparison.Ordinal))
        {
            session.OrchestratorState = next;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static string DeriveOrchestratorState(string current, Attempt a1, Attempt a2)
    {
        // Bridge is learner-driven (must be set by StartBridgeAsync). The
        // sync helper never overwrites it back to Finished1 once entered;
        // it only auto-advances from Bridge to Prep2 if the learner already
        // started attempt 2 (e.g. via the legacy speaking task page).
        var a1Done = IsMockAttemptSubmitted(a1);
        var a2Done = IsMockAttemptSubmitted(a2);
        var a1Graded = a1.State == AttemptState.Completed;
        var a2Graded = a2.State == AttemptState.Completed;
        // Attempt.StartedAt is non-nullable DateTimeOffset, so InProgress
        // is the only "in flight" indicator we need.
        var a2Started = a2.State == AttemptState.InProgress;

        if (a1Graded && a2Graded) return SpeakingMockOrchestratorStates.Aggregated;
        if (a1Done && a2Done) return SpeakingMockOrchestratorStates.Finished2;
        if (a1Done && a2Started) return SpeakingMockOrchestratorStates.Active2;
        if (a1Done)
        {
            // Already in Bridge or Prep2? Don't undo learner-driven moves.
            if (string.Equals(current, SpeakingMockOrchestratorStates.Bridge, StringComparison.Ordinal)) return current;
            if (string.Equals(current, SpeakingMockOrchestratorStates.Prep2, StringComparison.Ordinal)) return current;
            return SpeakingMockOrchestratorStates.Finished1;
        }
        if (a1.State == AttemptState.InProgress) return SpeakingMockOrchestratorStates.Active1;
        return SpeakingMockOrchestratorStates.Prep1;
    }

    private static bool IsMockAttemptSubmitted(Attempt attempt) => attempt.State switch
    {
        AttemptState.Submitted or AttemptState.Evaluating or AttemptState.Completed => true,
        _ => false,
    };

    private async Task<Attempt> CreateSpeakingMockAttemptAsync(
        string userId,
        string sessionId,
        string contentId,
        string mode,
        AttemptState initialState,
        CancellationToken cancellationToken)
    {
        var attempt = new Attempt
        {
            Id = $"sa-{Guid.NewGuid():N}",
            UserId = userId,
            ContentId = contentId,
            SubtestCode = "speaking",
            Context = "mock_set",
            Mode = mode,
            State = initialState,
            StartedAt = DateTimeOffset.UtcNow,
            DeviceType = "web",
            // Sharing the session id as ComparisonGroupId lets us run
            // straightforward "give me both halves of this session"
            // queries without a join through SpeakingMockSession.
            ComparisonGroupId = sessionId,
        };
        db.Attempts.Add(attempt);
        await RecordEventAsync(userId, "task_started", new
        {
            attemptId = attempt.Id,
            contentId = attempt.ContentId,
            subtest = attempt.SubtestCode,
            mode = attempt.Mode,
            context = attempt.Context,
            mockSessionId = sessionId,
        }, cancellationToken);
        return attempt;
    }

    private async Task EnforceMockSetFreeTierCapAsync(string userId, CancellationToken cancellationToken)
    {
        var (cap, sessionsInWindow, windowStart) = await GetSpeakingMockSessionWindowAsync(userId, cancellationToken);
        if (cap <= 0)
        {
            return; // Disabled by admin.
        }
        if (sessionsInWindow < cap)
        {
            return;
        }
        // We only enforce on free-tier subscribers. Paid plans bypass.
        var user = await db.Users.FirstAsync(x => x.Id == userId, cancellationToken);
        if (!string.Equals(user.CurrentPlanId, "free", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw ApiException.Conflict(
            "free_tier_speaking_mock_sets_exceeded",
            $"You've used your {cap} free-tier speaking mock set(s) this week. Upgrade to continue, or wait until {windowStart.AddDays(7).ToString("u", CultureInfo.InvariantCulture)}.");
    }

    private async Task<(int cap, int sessionsInWindow, DateTimeOffset windowStart)> GetSpeakingMockSessionWindowAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var config = await db.FreeTierConfigs.FirstOrDefaultAsync(cancellationToken);
        var cap = config?.MaxSpeakingMockSets ?? 1;
        var since = DateTimeOffset.UtcNow.AddDays(-7);
        var inWindow = await db.SpeakingMockSessions
            .CountAsync(x => x.UserId == userId && x.StartedAt >= since, cancellationToken);
        return (cap, inWindow, since);
    }

    private async Task<object> GetSpeakingMockSessionRollingUsageAsync(string userId, CancellationToken cancellationToken)
    {
        var (cap, used, windowStart) = await GetSpeakingMockSessionWindowAsync(userId, cancellationToken);
        return new
        {
            cap,
            used,
            remaining = Math.Max(0, cap - used),
            windowDays = 7,
            windowStartsAt = windowStart,
        };
    }

    private async Task<object> BuildMockSessionResponseAsync(
        SpeakingMockSession session,
        SpeakingMockSet mockSet,
        CancellationToken cancellationToken)
    {
        var (attempt1Summary, graded1, s1) = await BuildMockAttemptSummaryAsync(session.Attempt1Id, cancellationToken);
        var (attempt2Summary, graded2, s2) = await BuildMockAttemptSummaryAsync(session.Attempt2Id, cancellationToken);

        // Both halves are "finished" only once a human examiner has finalised the
        // tutor assessment for each role-play — mock Speaking is never AI-scored.
        var bothFinished = graded1 && graded2;
        int? combinedScaled = null;
        string combinedBandCode = ScoringService.ReadinessBandCode("oet", 0);

        if (bothFinished)
        {
            var attempt1 = await db.Attempts.FirstAsync(x => x.Id == session.Attempt1Id, cancellationToken);
            var family1 = NormalizeExamFamilyCode(attempt1.ExamFamilyCode);
            if (s1.HasValue && s2.HasValue)
            {
                combinedScaled = (int)Math.Round((s1.Value + s2.Value) / 2.0, MidpointRounding.AwayFromZero);
                combinedBandCode = ScoringService.ReadinessBandCode(family1, combinedScaled.Value);
            }

            // Persist a snapshot the first time both halves complete so the
            // historical mock session card stays stable even if an expert
            // later moderates one of the underlying assessments.
            if (session.State != SpeakingMockSessionState.Completed)
            {
                session.State = SpeakingMockSessionState.Completed;
                session.CompletedAt = DateTimeOffset.UtcNow;
                session.CombinedScaledSnapshot = combinedScaled;
                session.ReadinessBandSnapshot = combinedBandCode;
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                // Already snapshotted — prefer the snapshot for consistency.
                combinedScaled = session.CombinedScaledSnapshot ?? combinedScaled;
                combinedBandCode = session.ReadinessBandSnapshot ?? combinedBandCode;
            }
        }

        return new
        {
            mockSessionId = session.Id,
            mockSetId = mockSet.Id,
            title = mockSet.Title,
            description = mockSet.Description,
            mode = session.Mode,
            state = session.State.ToString().ToLowerInvariant(),
            // P5: orchestrator state drives the frontend strict-flow router.
            orchestratorState = session.OrchestratorState,
            bridgeStartedAt = session.BridgeStartedAt,
            startedAt = session.StartedAt,
            completedAt = session.CompletedAt,
            criteriaFocus = SplitCsv(mockSet.CriteriaFocus),
            tags = SplitCsv(mockSet.Tags),
            rolePlay1 = attempt1Summary,
            rolePlay2 = attempt2Summary,
            combined = new
            {
                bothCompleted = bothFinished,
                estimatedScaledScore = combinedScaled,
                passThreshold = OetScoring.ScaledPassGradeB,
                readinessBand = combinedBandCode,
                readinessBandLabel = BuildSpeakingReadinessBandLabel(combinedBandCode),
            },
        };
    }

    private async Task<(object summary, bool graded, int? scaled)> BuildMockAttemptSummaryAsync(
        string attemptId,
        CancellationToken cancellationToken)
    {
        var attempt = await db.Attempts.FirstAsync(x => x.Id == attemptId, cancellationToken);
        var content = await db.ContentItems.FirstAsync(x => x.Id == attempt.ContentId, cancellationToken);
        var evaluation = await db.Evaluations
            .Where(x => x.AttemptId == attemptId)
            .OrderByDescending(x => x.LastTransitionAt)
            .FirstOrDefaultAsync(cancellationToken);

        // Mock Speaking is graded by a HUMAN examiner — never by AI. The displayed
        // score/band comes from the final SpeakingTutorAssessment (prefer moderated
        // > primary). The AI band in attempt.AnalysisJson is intentionally absent.
        var speakingSession = await db.SpeakingSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.AttemptId == attemptId, cancellationToken);
        SpeakingTutorAssessment? tutor = null;
        if (speakingSession is not null)
        {
            var finals = await db.SpeakingTutorAssessments.AsNoTracking()
                .Where(t => t.IsFinal && t.SpeakingSessionId == speakingSession.Id)
                .ToListAsync(cancellationToken);
            tutor = finals.FirstOrDefault(t => t.MarkerRole == "moderated")
                ?? finals.FirstOrDefault(t => t.MarkerRole == "primary")
                ?? finals.FirstOrDefault();
        }

        var graded = tutor is not null;
        int? scaled = tutor?.EstimatedScaledScore;
        var bandCode = tutor?.ReadinessBand
            ?? OetScoring.SpeakingReadinessBandCode(OetScoring.SpeakingReadinessBand.NotReady);

        return (new
        {
            attemptId = attempt.Id,
            contentId = content.Id,
            title = content.Title,
            scenarioType = content.ScenarioType,
            state = attempt.State.ToString().ToLowerInvariant(),
            evaluationId = evaluation?.Id,
            // "completed" once a human examiner finalises the mark; otherwise the
            // underlying evaluation state (queued = awaiting examiner marking).
            evaluationState = graded ? "completed" : evaluation?.State.ToString().ToLowerInvariant(),
            estimatedScaledScore = scaled,
            readinessBand = bandCode,
            readinessBandLabel = BuildSpeakingReadinessBandLabel(bandCode),
        }, graded, scaled);
    }

    private static string[] SplitCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
