using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

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

        // Validate underlying content papers exist and are speaking.
        var rolePlay1 = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == mockSet.RolePlay1ContentId, cancellationToken);
        var rolePlay2 = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == mockSet.RolePlay2ContentId, cancellationToken);
        if (rolePlay1 is null || rolePlay2 is null
            || !string.Equals(rolePlay1.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(rolePlay2.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Conflict(
                "speaking_mock_set_invalid",
                "This mock set references content that is missing or not a speaking role-play.");
        }

        // Free-tier rolling-window cap (Q1 of plan §6, locked).
        await EnforceMockSetFreeTierCapAsync(userId, cancellationToken);

        var normalisedMode = string.Equals(mode, "self", StringComparison.OrdinalIgnoreCase) ? "self" : "exam";
        var sessionId = $"sms-{Guid.NewGuid():N}";

        var attempt1 = await CreateSpeakingMockAttemptAsync(userId, sessionId, rolePlay1.Id, normalisedMode, cancellationToken);
        var attempt2 = await CreateSpeakingMockAttemptAsync(userId, sessionId, rolePlay2.Id, normalisedMode, cancellationToken);

        var session = new SpeakingMockSession
        {
            Id = sessionId,
            MockSetId = mockSet.Id,
            UserId = userId,
            Attempt1Id = attempt1.Id,
            Attempt2Id = attempt2.Id,
            Mode = normalisedMode,
            State = SpeakingMockSessionState.InProgress,
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

        return await BuildMockSessionResponseAsync(session, mockSet, cancellationToken);
    }

    private async Task<Attempt> CreateSpeakingMockAttemptAsync(
        string userId,
        string sessionId,
        string contentId,
        string mode,
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
            State = AttemptState.InProgress,
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
        var (attempt1Summary, eval1) = await BuildMockAttemptSummaryAsync(session.Attempt1Id, cancellationToken);
        var (attempt2Summary, eval2) = await BuildMockAttemptSummaryAsync(session.Attempt2Id, cancellationToken);

        var bothFinished = eval1 is { State: AsyncState.Completed } && eval2 is { State: AsyncState.Completed };
        int? combinedScaled = null;
        string combinedBandCode = OetScoring.SpeakingReadinessBandCode(OetScoring.SpeakingReadinessBand.NotReady);

        if (bothFinished)
        {
            var s1 = ReadSpeakingBandFromAnalysis(
                (await db.Attempts.FirstAsync(x => x.Id == session.Attempt1Id, cancellationToken)).AnalysisJson).estimatedScaledScore;
            var s2 = ReadSpeakingBandFromAnalysis(
                (await db.Attempts.FirstAsync(x => x.Id == session.Attempt2Id, cancellationToken)).AnalysisJson).estimatedScaledScore;
            if (s1.HasValue && s2.HasValue)
            {
                combinedScaled = (int)Math.Round((s1.Value + s2.Value) / 2.0, MidpointRounding.AwayFromZero);
                combinedBandCode = OetScoring.SpeakingReadinessBandCode(
                    OetScoring.SpeakingReadinessBandFromScaled(combinedScaled.Value));
            }

            // Persist a snapshot the first time both halves complete so the
            // historical mock session card stays stable even if an expert
            // later moderates one of the underlying evaluations.
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

    private async Task<(object summary, Evaluation? evaluation)> BuildMockAttemptSummaryAsync(
        string attemptId,
        CancellationToken cancellationToken)
    {
        var attempt = await db.Attempts.FirstAsync(x => x.Id == attemptId, cancellationToken);
        var content = await db.ContentItems.FirstAsync(x => x.Id == attempt.ContentId, cancellationToken);
        var evaluation = await db.Evaluations
            .Where(x => x.AttemptId == attemptId)
            .OrderByDescending(x => x.LastTransitionAt)
            .FirstOrDefaultAsync(cancellationToken);

        var (scaled, bandCode, _) = ReadSpeakingBandFromAnalysis(attempt.AnalysisJson);

        return (new
        {
            attemptId = attempt.Id,
            contentId = content.Id,
            title = content.Title,
            scenarioType = content.ScenarioType,
            state = attempt.State.ToString().ToLowerInvariant(),
            evaluationId = evaluation?.Id,
            evaluationState = evaluation?.State.ToString().ToLowerInvariant(),
            estimatedScaledScore = scaled,
            readinessBand = bandCode,
            readinessBandLabel = BuildSpeakingReadinessBandLabel(bandCode),
        }, evaluation);
    }

    private static string[] SplitCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
