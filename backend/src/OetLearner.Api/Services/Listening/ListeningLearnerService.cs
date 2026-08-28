using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Recalls;
using OetLearner.Api.Services.Assessment;

namespace OetLearner.Api.Services.Listening;

public sealed class ListeningLearnerService(
    LearnerDbContext db,
    IContentEntitlementService entitlements,
    IRecallsAutoSeed? autoSeed = null,
    IAiPackageCreditService? aiPackageCreditService = null,
    ListeningGradingService? gradingService = null,
    IAssessmentScoreConversionService? scoreConversionService = null,
    IAssessmentMarkingPolicyService? markingPolicyService = null,
    IListeningPolicyService? listeningPolicyService = null,
    IListeningBackfillService? backfillService = null)
{
    private const string Subtest = "listening";
    private const int CanonicalRawMax = OetScoring.ListeningReadingRawMax;
    private const string SubmitIdempotencyScope = "listening-submit";
    private const int MaxIdempotencyRecordKeyLength = 128;

    /// <summary>
    /// Reserved key under which the monotonic one-way <c>sectionCursor</c> is
    /// persisted inside a generic <see cref="Attempt.AnswersJson"/> map. Generic
    /// attempts have no dedicated <c>NavigationStateJson</c> column (only the
    /// relational <see cref="ListeningAttempt"/> store does), so the cursor
    /// piggybacks on the answers map. The <c>__</c> prefix keeps it from ever
    /// colliding with an authored question id and ensures grading (which keys by
    /// question id) ignores it. It is also filtered out of every answered-count.
    /// </summary>
    private const string GenericSectionCursorKey = "__listeningSectionCursor";
    private const string GenericAudioPlaybackStateKey = "__listeningAudioPlaybackState";
    private const string GenericAudioResumeMsKey = "__listeningAudioResumeMs";
    private const string GenericAudioSectionKey = "__listeningAudioSection";
    private const string GenericAudioQuestionIndexKey = "__listeningAudioQuestionIndex";

    public async Task<object> GetHomeAsync(string userId, CancellationToken ct)
    {
        await EnsureLearnerAsync(userId, ct);
        var profession = await GetLearnerProfessionAsync(userId, ct);
        var (listeningPolicy, _) = await ResolveListeningPolicyAsync(userId, ct);
        var showPastAttempts = listeningPolicy.ShowPastAttempts;

        var papers = await db.ContentPapers.AsNoTracking()
            .Include(p => p.Assets.Where(a => a.IsPrimary))
                .ThenInclude(a => a.MediaAsset)
            .Where(p => p.Status == ContentStatus.Published
                && p.CandidateVisible
                && p.SubtestCode == Subtest
                && (p.AppliesToAllProfessions
                    || (!string.IsNullOrWhiteSpace(profession) && p.ProfessionId == profession)))
            .OrderByDescending(p => p.Priority)
            .ThenByDescending(p => p.PublishedAt)
            .ThenBy(p => p.Title)
            .ToListAsync(ct);

        var legacyTasks = await db.ContentItems.AsNoTracking()
            .Where(x => x.SubtestCode == Subtest && x.Status == ContentStatus.Published)
            .OrderBy(x => x.Title)
            .ToListAsync(ct);

        var contentIds = papers.Select(p => p.Id)
            .Concat(legacyTasks.Select(t => t.Id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var paperIds = papers.Select(p => p.Id).ToList();

        var attempts = contentIds.Count == 0
            ? new List<Attempt>()
            : await db.Attempts.AsNoTracking()
                .Where(a => a.UserId == userId
                    && a.SubtestCode == Subtest
                    && contentIds.Contains(a.ContentId)
                    && a.Mode != "paper")
                .OrderByDescending(a => a.LastClientSyncAt ?? a.SubmittedAt ?? a.StartedAt)
                .ToListAsync(ct);

        var relationalAttempts = paperIds.Count == 0
            ? new List<ListeningAttempt>()
            : await db.ListeningAttempts.AsNoTracking()
                .Where(a => a.UserId == userId
                    && paperIds.Contains(a.PaperId)
                    && a.Mode != ListeningAttemptMode.Paper)
                .OrderByDescending(a => a.LastActivityAt)
                .ToListAsync(ct);

        var relationalAttemptIds = relationalAttempts.Select(a => a.Id).ToList();
        var relationalAnswerCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (relationalAttemptIds.Count > 0)
        {
            var relationalAnswerRows = await db.ListeningAnswers.AsNoTracking()
                .Where(answer => relationalAttemptIds.Contains(answer.ListeningAttemptId))
                .Select(answer => new
                {
                    answer.ListeningAttemptId,
                    answer.UserAnswerJson
                })
                .ToListAsync(ct);
            relationalAnswerCounts = relationalAnswerRows
                .Where(answer => HasAnsweredValue(answer.UserAnswerJson))
                .GroupBy(answer => answer.ListeningAttemptId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        }

        var relationalQuestionRows = paperIds.Count == 0
            ? new List<(string PaperId, ListeningPartCode PartCode)>()
            : (await db.ListeningQuestions.AsNoTracking()
                .Where(q => paperIds.Contains(q.PaperId))
                .Select(q => new { q.PaperId, PartCode = q.Part!.PartCode })
                .ToListAsync(ct))
                .Select(row => (PaperId: row.PaperId, PartCode: row.PartCode))
                .ToList();
        var relationalQuestionCounts = relationalQuestionRows
            .GroupBy(row => row.PaperId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var relationalPartCounts = relationalQuestionRows
            .GroupBy(row => row.PaperId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (
                    A: group.Count(row => ListeningParentPart(row.PartCode) == "A"),
                    B: group.Count(row => ListeningParentPart(row.PartCode) == "B"),
                    C: group.Count(row => ListeningParentPart(row.PartCode) == "C")
                ),
                StringComparer.Ordinal);

        var evaluationAttemptIds = attempts.Select(a => a.Id)
            .Concat(relationalAttempts.Select(a => a.Id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var evaluations = evaluationAttemptIds.Count == 0
            ? new List<Evaluation>()
            : await db.Evaluations.AsNoTracking()
                .Where(e => evaluationAttemptIds.Contains(e.AttemptId))
                .OrderByDescending(e => e.GeneratedAt)
                .ToListAsync(ct);

        var titleByContentId = papers.ToDictionary(p => p.Id, p => p.Title, StringComparer.Ordinal);
        foreach (var task in legacyTasks)
        {
            titleByContentId.TryAdd(task.Id, task.Title);
        }

        var activeAttempts = attempts
            .Where(a => a.State == AttemptState.InProgress)
            .Select(a => new
            {
                attemptId = a.Id,
                paperId = a.ContentId,
                paperTitle = titleByContentId.GetValueOrDefault(a.ContentId, "Listening paper"),
                status = ToApiState(a.State),
                mode = a.Mode,
                a.StartedAt,
                a.LastClientSyncAt,
                // Exclude reserved navigation keys (e.g. the section cursor) so
                // they never inflate the learner-facing answered count.
                answeredCount = DeserializeAnswers(a.AnswersJson).Count(kv => !IsReservedAnswerKey(kv.Key) && HasAnsweredValue(kv.Value)),
                route = $"/listening/player/{Uri.EscapeDataString(a.ContentId)}?attemptId={Uri.EscapeDataString(a.Id)}&mode={Uri.EscapeDataString(a.Mode)}"
            })
            .Concat(relationalAttempts
                .Where(a => a.Status == ListeningAttemptStatus.InProgress)
                .Select(a => new
                {
                    attemptId = a.Id,
                    paperId = a.PaperId,
                    paperTitle = titleByContentId.GetValueOrDefault(a.PaperId, "Listening paper"),
                    status = ToApiState(a.Status),
                    mode = ToApiMode(a.Mode),
                    a.StartedAt,
                    LastClientSyncAt = (DateTimeOffset?)a.LastActivityAt,
                    answeredCount = relationalAnswerCounts.GetValueOrDefault(a.Id),
                    route = RelationalAttemptRoute(a)
                }))
            .OrderByDescending(a => a.LastClientSyncAt ?? a.StartedAt)
            .Take(3)
            .ToList();

        var allResults = attempts
            .Where(a => showPastAttempts && a.State == AttemptState.Completed)
            .Select(a =>
            {
                var evaluation = evaluations.FirstOrDefault(e => e.AttemptId == a.Id);
                var score = ResolveScoreFromEvaluation(evaluation, a.RequiresAdminReview);
                var isPartPractice = false;
                string? partCode = null;
                return new ListeningHomeResultProjection(
                    attemptId: a.Id,
                    paperId: a.ContentId,
                    paperTitle: titleByContentId.GetValueOrDefault(a.ContentId, "Listening paper"),
                    rawScore: score.RawScore,
                    maxRawScore: score.MaxRawScore,
                    scaledScore: score.ScaledScore,
                    grade: score.Grade,
                    passed: score.Passed,
                    submittedAt: a.SubmittedAt,
                    scoreDisplay: FormatScoreDisplay(score),
                    route: $"/listening/results/{Uri.EscapeDataString(a.Id)}",
                    practiceRoute: BuildListeningHistoryPracticeRoute(a.Mode.ToString(), isPartPractice, partCode),
                    mode: a.Mode,
                    attemptKind: isPartPractice ? "part" : "full",
                    partCode: partCode,
                    requiresAdminReview: a.RequiresAdminReview,
                    adminReviewReason: a.AdminReviewReason);
            })
            .Concat(relationalAttempts
                .Where(a => showPastAttempts && a.Status == ListeningAttemptStatus.Submitted)
                .Select(a =>
                {
                    var evaluation = evaluations.FirstOrDefault(e => e.AttemptId == a.Id);
                    var score = ResolveScoreFromRelationalAttempt(a, evaluation);
                    var baseTitle = titleByContentId.GetValueOrDefault(a.PaperId, "Listening paper");
                    var partPractice = ListeningAttemptScope.ReadPartPractice(a.ScopeJson);
                    var partCode = partPractice.IsValid && !string.IsNullOrWhiteSpace(partPractice.PartCode)
                        ? partPractice.PartCode
                        : null;
                    var displayTitle = partPractice.IsValid && !string.IsNullOrWhiteSpace(partPractice.PartCode)
                        ? $"{baseTitle} — Part {partPractice.PartCode} practice"
                        : baseTitle;
                    return new ListeningHomeResultProjection(
                        attemptId: a.Id,
                        paperId: a.PaperId,
                        paperTitle: displayTitle,
                        rawScore: score.RawScore,
                        maxRawScore: score.MaxRawScore,
                        scaledScore: score.ScaledScore,
                        grade: score.Grade,
                        passed: score.Passed,
                        submittedAt: a.SubmittedAt,
                    scoreDisplay: FormatScoreDisplay(score),
                    route: $"/listening/results/{Uri.EscapeDataString(a.Id)}",
                    practiceRoute: BuildListeningHistoryPracticeRoute(ToApiMode(a.Mode), partPractice.IsValid, partCode),
                    mode: ToApiMode(a.Mode),
                    attemptKind: partPractice.IsValid ? "part" : "full",
                    partCode: partCode,
                    requiresAdminReview: a.RequiresAdminReview,
                    adminReviewReason: a.AdminReviewReason);
                }))
            .OrderByDescending(result => result.submittedAt)
            .ToList();
        var recentResults = allResults.ToList();
        var progressScoreDisplay = SelectProgressScoreDisplay(allResults, listeningPolicy.BestScoreDisplay);

        var latestCompletedAttempt = showPastAttempts
            ? attempts.FirstOrDefault(a => a.State == AttemptState.Completed)
            : null;
        var latestRelationalAttempt = showPastAttempts
            ? relationalAttempts.FirstOrDefault(a => a.Status == ListeningAttemptStatus.Submitted)
            : null;
        if (latestCompletedAttempt is not null && latestRelationalAttempt is not null
            && (latestCompletedAttempt.CompletedAt ?? latestCompletedAttempt.SubmittedAt ?? DateTimeOffset.MinValue)
                < (latestRelationalAttempt.SubmittedAt ?? DateTimeOffset.MinValue))
        {
            latestCompletedAttempt = null;
        }
        var latestEvaluation = latestCompletedAttempt is null
            ? latestRelationalAttempt is null ? null : evaluations.FirstOrDefault(e => e.AttemptId == latestRelationalAttempt.Id)
            : evaluations.FirstOrDefault(e => e.AttemptId == latestCompletedAttempt.Id);

        // Hardening: a completed attempt may reference a paper that was later unpublished or whose
        // ExtractedTextJson is malformed. Never let that bubble a 500 through the Listening home endpoint.
        IReadOnlyList<ListeningErrorClusterDto> latestClusters = new List<ListeningErrorClusterDto>();
        if (latestRelationalAttempt is not null && latestCompletedAttempt is null)
        {
            try
            {
                var source = await ResolveSourceAsync(latestRelationalAttempt.PaperId, ct);
                var answers = await LoadRelationalAnswersAsync(latestRelationalAttempt.Id, ct);
                latestClusters = BuildReview(latestRelationalAttempt, source, answers, latestEvaluation).ErrorClusters;
            }
            catch (Exception)
            {
                latestClusters = new List<ListeningErrorClusterDto>();
            }
        }
        else if (latestCompletedAttempt is not null)
        {
            try
            {
                var source = await ResolveSourceAsync(latestCompletedAttempt.ContentId, ct);
                latestClusters = BuildReview(latestCompletedAttempt, source).ErrorClusters;
            }
            catch (Exception)
            {
                latestClusters = new List<ListeningErrorClusterDto>();
            }
        }

        var drillGroups = latestClusters.Count > 0
            ? latestClusters.Select(c => BuildDrill(c.ErrorType, latestCompletedAttempt?.ContentId ?? latestRelationalAttempt?.PaperId, latestCompletedAttempt?.Id ?? latestRelationalAttempt?.Id)).ToList()
            : new List<ListeningDrillDto>
            {
                BuildDrill("distractor_confusion", legacyTasks.FirstOrDefault()?.Id, latestCompletedAttempt?.Id ?? latestRelationalAttempt?.Id),
                BuildDrill("numbers_and_frequencies", legacyTasks.FirstOrDefault()?.Id, latestCompletedAttempt?.Id ?? latestRelationalAttempt?.Id)
            };

        // Hardening: individual paper DTO extraction reads free-form ExtractedTextJson; one malformed
        // paper must not take the whole endpoint down.
        // Per-paper subscription gate signal — projected into the home DTO so the UI can render a
        // "Premium" lock badge without firing /v1/listening/papers/{id}/session and getting a 402.
        var requiresSubscriptionByPaperId = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var paper in papers)
        {
            try
            {
                var access = await entitlements.AllowAccessAsync(userId, paper, ct);
                requiresSubscriptionByPaperId[paper.Id] = !access.Allowed;
            }
            catch (Exception)
            {
                // Fail-closed visually (show lock) rather than fail the whole endpoint.
                requiresSubscriptionByPaperId[paper.Id] = true;
            }
        }

        var paperDtos = new List<object>();
        foreach (var paper in papers)
        {
            try
            {
                var lastGeneric = attempts.FirstOrDefault(a =>
                    a.ContentId == paper.Id
                    && (a.Mode == "exam" || a.Mode == "home"));
                var lastRelational = relationalAttempts.FirstOrDefault(a =>
                    a.PaperId == paper.Id
                    && (a.Mode == ListeningAttemptMode.Exam || a.Mode == ListeningAttemptMode.Home)
                    && !ListeningAttemptScope.ReadPartPractice(a.ScopeJson).IsPartPractice);
                if (!showPastAttempts)
                {
                    if (lastGeneric?.State != AttemptState.InProgress) lastGeneric = null;
                    if (lastRelational?.Status != ListeningAttemptStatus.InProgress) lastRelational = null;
                }
                var partCounts = relationalPartCounts.GetValueOrDefault(paper.Id);
                paperDtos.Add(PaperHomeDto(
                    paper,
                    BuildPaperLastAttemptDto(paper.Id, lastGeneric, lastRelational),
                    relationalQuestionCounts.GetValueOrDefault(paper.Id),
                    requiresSubscriptionByPaperId.GetValueOrDefault(paper.Id, false),
                    partCounts.A,
                    partCounts.B,
                    partCounts.C));
            }
            catch (Exception)
            {
                // Skip malformed paper rather than fail the whole home surface.
            }
        }

        var featuredTasks = new List<object>();
        foreach (var task in legacyTasks)
        {
            try { featuredTasks.Add(LegacyTaskHomeDto(task)); } catch { }
        }

        return new
        {
            intro = "Listening practice emphasises accurate capture of numbers, frequencies, clinical details, and changes in plan.",
            papers = paperDtos,
            featuredTasks,
            activeAttempts,
            recentResults,
            progressScoreDisplay,
            progressScoreDisplayMode = NormalizeBestScoreDisplay(listeningPolicy.BestScoreDisplay),
            partCollections = BuildPartCollections(paperDtos, featuredTasks, papers.FirstOrDefault()?.Id ?? legacyTasks.FirstOrDefault()?.Id),
            transcriptBackedReview = new
            {
                title = "Transcript-backed review",
                route = latestCompletedAttempt is null && latestRelationalAttempt is null ? null : $"/listening/review/{latestCompletedAttempt?.Id ?? latestRelationalAttempt?.Id}",
                availableAfterAttempt = true,
                latestAttemptId = latestCompletedAttempt?.Id ?? latestRelationalAttempt?.Id,
                latestScoreDisplay = latestCompletedAttempt is not null && latestEvaluation is not null
                    ? FormatScoreDisplay(ResolveScoreFromEvaluation(latestEvaluation, latestCompletedAttempt.RequiresAdminReview))
                    : latestRelationalAttempt is not null
                        ? FormatScoreDisplay(ResolveScoreFromRelationalAttempt(latestRelationalAttempt, latestEvaluation))
                        : null
            },
            distractorDrills = drillGroups,
            drillGroups,
            accessPolicyHints = new
            {
                policy = "per_item_post_attempt",
                state = latestCompletedAttempt is null && latestRelationalAttempt is null ? "deferred" : "available",
                rationale = "Use transcript-backed review after an attempt so you can diagnose distractor patterns with real evidence instead of replaying blindly.",
                availableAfterAttempt = true
            },
            mockSets = new[]
            {
                new { id = "full-practice", title = "Full OET Mock", type = "full", subType = (string?)null, mode = "practice", includeReview = false, strictTimer = false, reviewSelection = "none", route = "/mocks" },
                new { id = "full-exam", title = "Full OET Mock", type = "full", subType = (string?)null, mode = "exam", includeReview = false, strictTimer = true, reviewSelection = "none", route = "/mocks" }
            },
            emptyStates = new
            {
                papers = paperDtos.Count == 0 ? "No published Listening papers are ready yet. Use mocks or your study plan until curated Listening papers are published." : null,
                activeAttempts = activeAttempts.Count == 0 ? "No in-progress Listening attempt." : null,
                recentResults = recentResults.Count == 0 ? "Complete a Listening task to unlock transcript-backed review and canonical OET score display." : null
            }
        };
    }

    public Task<object> GetSessionAsync(string userId, string paperId, string? mode, string? attemptId, CancellationToken ct)
        => GetSessionAsync(userId, paperId, mode, attemptId, pathwayStage: null, ct);

    public async Task<object> GetSessionAsync(string userId, string paperId, string? mode, string? attemptId, string? pathwayStage, CancellationToken ct)
    {
        await EnsureLearnerAsync(userId, ct);
        await RequirePaperAccessIfAuthoredAsync(userId, paperId, ct);
        var source = await ResolveSourceAsync(paperId, ct);
        var normalizedMode = NormalizeMode(mode);
        var normalizedPathwayStage = NormalizePathwayStage(pathwayStage);
        ListeningAttempt? relationalAttempt = null;
        Attempt? attempt = null;

        if (!string.IsNullOrWhiteSpace(attemptId))
        {
            relationalAttempt = await TryGetRelationalAttemptOwnedByUserAsync(userId, attemptId, asNoTracking: true, ct);
            if (relationalAttempt is not null)
            {
                if (!string.Equals(relationalAttempt.PaperId, source.Id, StringComparison.Ordinal))
                {
                    throw ApiException.Validation("listening_attempt_mismatch", "This attempt does not belong to the requested Listening paper.");
                }
            }
            else
            {
                attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, ct);
                if (!string.Equals(attempt.ContentId, source.Id, StringComparison.Ordinal))
                {
                    throw ApiException.Validation("listening_attempt_mismatch", "This attempt does not belong to the requested Listening paper.");
                }
            }

            if (relationalAttempt?.Mode == ListeningAttemptMode.Paper
                || string.Equals(attempt?.Mode, "paper", StringComparison.OrdinalIgnoreCase))
            {
                throw PaperModeDisabled();
            }
        }
        else
        {
            var requestedRelationalMode = ToRelationalMode(normalizedMode);
            if (source.UsesRelationalStructure)
            {
                var relationalCandidates = await db.ListeningAttempts.AsNoTracking()
                    .Where(a => a.UserId == userId
                        && a.PaperId == source.Id
                        && a.Mode == requestedRelationalMode
                        && a.Status == ListeningAttemptStatus.InProgress)
                    .OrderByDescending(a => a.LastActivityAt)
                    .ToListAsync(ct);
                relationalAttempt = relationalCandidates.FirstOrDefault(a =>
                    ListeningAttemptScope.MatchesRequestedScope(a.ScopeJson, normalizedPathwayStage));
            }

            if (relationalAttempt is null)
            {
                attempt = await db.Attempts.AsNoTracking()
                    .Where(a => a.UserId == userId
                        && a.SubtestCode == Subtest
                        && a.ContentId == source.Id
                        && a.Mode == normalizedMode
                        && a.State == AttemptState.InProgress)
                    .OrderByDescending(a => a.LastClientSyncAt ?? a.StartedAt)
                    .FirstOrDefaultAsync(ct);
            }
        }

        source = ApplyAttemptScope(source, relationalAttempt);

        var questions = source.Questions.Select(LearnerQuestionDto).ToList();
        var candidate = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                user.DisplayName,
                user.ActiveProfessionId,
                ProfessionLabel = db.Professions
                    .Where(profession => profession.Id == user.ActiveProfessionId)
                    .Select(profession => profession.Label)
                    .FirstOrDefault(),
            })
            .SingleOrDefaultAsync(ct);
        var answers = relationalAttempt is not null
            ? await LoadRelationalAnswersAsync(relationalAttempt.Id, ct)
            : attempt is null ? new Dictionary<string, string?>() : DeserializeAnswers(attempt.AnswersJson);
        var effectiveMode = relationalAttempt is not null
            ? ToApiMode(relationalAttempt.Mode)
            : attempt?.Mode ?? normalizedMode;
        var audioTransport = relationalAttempt is not null
            ? ListeningAudioTransportPolicy.FromSnapshot(effectiveMode, relationalAttempt.PolicySnapshotJson)
            : attempt is not null
                ? ListeningAudioTransportPolicy.FromSnapshot(effectiveMode, attempt.PolicySnapshotJson)
                : await ResolveCurrentAudioTransportPolicyAsync(effectiveMode, ct);
        var countdownWarningsSeconds = await ResolveCountdownWarningsForSessionAsync(
            userId,
            relationalAttempt?.PolicySnapshotJson ?? attempt?.PolicySnapshotJson,
            ct);
        var screenReaderOptimised = await ResolveScreenReaderOptimisedForSessionAsync(
            userId,
            relationalAttempt?.PolicySnapshotJson ?? attempt?.PolicySnapshotJson,
            ct);
        var audioAvailable = !string.IsNullOrWhiteSpace(source.AudioUrl)
            || source.AudioUrlByPart?.Values.Any(url => !string.IsNullOrWhiteSpace(url)) == true;
        var allRequiredAudioAvailable = HasAllRequiredAudioAssets(source);
        var objectiveReady = source.Questions.Count > 0;
        var preflightEligible = objectiveReady && allRequiredAudioAvailable;
        string? preflightEligibilityReason = !objectiveReady
            ? "Structured Listening questions are not ready yet."
            : !allRequiredAudioAvailable
                ? "Scored Listening audio is not available yet."
                : null;
        if (preflightEligible && relationalAttempt is null && attempt is null)
        {
            try
            {
                var (policy, _) = await ResolveListeningPolicyAsync(userId, ct);
                await EnsureAttemptEligibilityAsync(userId, source, normalizedMode, policy, ct);
            }
            catch (ApiException ex)
            {
                preflightEligible = false;
                preflightEligibilityReason = ex.Message;
            }
        }
        return new
        {
            serverNow = DateTimeOffset.UtcNow,
            paper = SourceDto(source),
            attempt = relationalAttempt is not null
                ? RelationalAttemptDto(relationalAttempt, answers)
                : attempt is null ? null : AttemptDto(attempt, answers),
            questions,
            modePolicy = new
            {
                mode = effectiveMode,
                // These values come from the approved policy snapshot for an
                // existing attempt. Before start, the current effective policy
                // is used; unavailable or malformed policy stays strict.
                canPause = audioTransport.CanPause,
                canScrub = audioTransport.CanScrub,
                onePlayOnly = audioTransport.OnePlayOnly,
                countdownWarningsSeconds,
                screenReaderOptimised,
                audioLockMode = audioTransport.LockMode,
                autosave = true,
                transcriptPolicy = "per_item_post_attempt",
                // Phase 9 tail: presentation hints so the player can render
                // the correct computer-based chrome. The graded-integrity
                // invariants stay encoded in onePlayOnly / canScrub / canPause.
                presentationStyle = effectiveMode switch
                {
                    "home" => "kiosk_fullscreen",
                    "exam" => "exam_standard",
                    "diagnostic" => "diagnostic",
                    _ => "practice"
                },
                // Fullscreen and focus are technical guidance signals. They
                // must not block an attempt without an explicit owner-approved
                // exam-rehearsal policy.
                integrityLockRequired = false,
                technicalGuidanceTelemetryEnabled = true,
                printableBooklet = false,
                freeNavigation = effectiveMode == "diagnostic",
                unansweredWarningRequired = IsExamMode(effectiveMode),
                finalReviewAllPartsSeconds = (int?)null
            },
            scoring = new
            {
                maxRawScore = source.Questions.Sum(q => q.Points) > 0
                    ? source.Questions.Sum(q => q.Points)
                    : CanonicalRawMax,
                // Pass thresholds are intentionally absent until an approved
                // owner conversion table is published for this subtest.
                passRawScore = (int?)null,
                passScaledScore = (int?)null,
                conversionPolicy = "owner_managed_exact_table"
            },
            preflight = new
            {
                candidate = new
                {
                    displayName = candidate?.DisplayName ?? "Candidate",
                    professionId = candidate?.ActiveProfessionId,
                    professionLabel = candidate?.ProfessionLabel,
                },
                selectedTest = new
                {
                    id = source.Id,
                    title = source.Title,
                    mode = effectiveMode,
                },
                eligibility = new
                {
                    checkedAtServer = true,
                    eligible = preflightEligible,
                    reason = preflightEligibilityReason,
                },
            },
            readiness = new
            {
                objectiveReady,
                questionCount = source.Questions.Count,
                audioAvailable,
                missingReason = !objectiveReady
                    ? "This paper has media assets but no structured Listening question map yet, so graded attempts are disabled."
                    : !allRequiredAudioAvailable
                        ? "Scored Listening audio is not available yet."
                        : null
            }
        };
    }

    public Task<object> StartAttemptAsync(string userId, string paperId, string? mode, CancellationToken ct)
        => StartAttemptAsync(userId, paperId, mode, pathwayStage: null, ct);

    public async Task<object> StartAttemptAsync(string userId, string paperId, string? mode, string? pathwayStage, CancellationToken ct)
        => await StartAttemptAsync(userId, paperId, mode, pathwayStage, forceNewAttempt: false, ct);

    public async Task<object> StartAttemptAsync(string userId, string paperId, string? mode, string? pathwayStage, bool forceNewAttempt, CancellationToken ct, bool billObjectivePractice = true)
    {
        await EnsureLearnerMutationAllowedAsync(userId, ct);

        await RequirePaperAccessIfAuthoredAsync(userId, paperId, ct);

        var source = await ResolveSourceAsync(paperId, ct);
        if (source.Questions.Count == 0)
        {
            throw ApiException.Validation(
                "listening_questions_missing",
                "This Listening paper cannot start a graded attempt until its structured questions are authored.");
        }

        var normalizedMode = NormalizeMode(mode);
        var normalizedPathwayStage = NormalizePathwayStage(pathwayStage);
        if (source.UsesRelationalStructure)
        {
            return await StartRelationalAttemptAsync(userId, source, normalizedMode, normalizedPathwayStage, forceNewAttempt, ct, billObjectivePractice);
        }

        if (!forceNewAttempt)
        {
            var existing = await db.Attempts
                .Where(a => a.UserId == userId
                    && a.ContentId == source.Id
                    && a.SubtestCode == Subtest
                    && a.Mode == normalizedMode
                    && a.State == AttemptState.InProgress)
                .OrderByDescending(a => a.LastClientSyncAt ?? a.StartedAt)
                .FirstOrDefaultAsync(ct);
            if (existing is not null)
            {
                return AttemptDto(existing, DeserializeAnswers(existing.AnswersJson));
            }
        }

        // Keep the legacy JSON-backed path subject to the same server-owned
        // strict-start gates as relational papers. The client preflight is
        // only a convenience; a direct start request must not bypass the
        // sound check or begin an exam whose scored audio is incomplete.
        if (normalizedMode is "exam" or "home"
            && !await HasValidAudioCheckAsync(userId, DateTimeOffset.UtcNow, ct))
        {
            throw ApiException.Validation(
                "listening_audio_check_required",
                "Pass the Listening sound check before starting this exam. Run the sound check, then return here to begin.");
        }

        if (IsExamMode(normalizedMode) && !HasAllRequiredAudioAssets(source))
        {
            throw ApiException.Conflict(
                "listening_audio_asset_missing",
                "This Listening paper does not have complete audio assets for every scored section. Cannot start exam-mode attempt.");
        }

        var genericAttemptId = $"la-{Guid.NewGuid():N}";

        var markingPolicyResolver = markingPolicyService ?? new AssessmentMarkingPolicyService(db);
        var markingPolicy = await markingPolicyResolver.ResolveAsync("listening", "default", cancellationToken: ct);
        if (!markingPolicy.IsAvailable || markingPolicy.ErrorCode is not null)
        {
            throw ApiException.Conflict(
                "listening_marking_policy_unavailable",
                "Listening attempts are unavailable until an owner-approved marking policy is effective.");
        }
        var conversionResolver = scoreConversionService ?? new AssessmentScoreConversionService(db);
        var scoreConversionAtStart = await conversionResolver.ResolveAsync(
            Subtest,
            rawScore: 0,
            scopeKey: "default",
            cancellationToken: ct);
        var (listeningPolicy, userPolicyOverride) = await ResolveListeningPolicyAsync(userId, ct);
        EnsureAttemptsAllowed(userPolicyOverride);
        await EnsureAttemptEligibilityAsync(userId, source, normalizedMode, listeningPolicy, ct);
        var effectiveSessionPolicy = ListeningPolicyResolver.Resolve(listeningPolicy, userPolicyOverride);
        var audioTransport = ListeningAudioTransportPolicy.FromPolicy(
            normalizedMode,
            markingPolicy.Document,
            listeningPolicy.LearningReplayAllowed);
        var fullPaperTimerMinutes = ResolveFullPaperTimerMinutes(listeningPolicy, userPolicyOverride);
        var startedAt = DateTimeOffset.UtcNow;
        var deadlineAt = IsExamMode(normalizedMode)
            ? startedAt
                .AddMinutes(fullPaperTimerMinutes)
                .AddSeconds(Math.Max(0, listeningPolicy.GracePeriodSeconds))
            : (DateTimeOffset?)null;

        // Listening test-credit allowance (legacy / JSON-backed paper path).
        // Keep this after every start gate, including the owner-controlled
        // marking-policy and score-conversion gates, so an unavailable
        // governance record can never consume a learner credit.
        string? feedbackMessage = null;
        if (billObjectivePractice && aiPackageCreditService is not null)
        {
            var creditResult = await aiPackageCreditService.DeductObjectivePracticeAsync(
                userId, "listening",
                CreditGateExtensions.ObjectivePaperReference("listening", userId, source.Id),
                ct);
            creditResult.EnsureDebited();
            feedbackMessage = creditResult.FeedbackMessage;
        }

        var attempt = new Attempt
        {
            Id = genericAttemptId,
            UserId = userId,
            ContentId = source.Id,
            SubtestCode = Subtest,
            Context = source.SourceKind,
            Mode = normalizedMode,
            State = AttemptState.InProgress,
            StartedAt = startedAt,
            DeviceType = "web",
            ComparisonGroupId = $"listening-{source.Id}",
            AnswersJson = "{}",
            MarkingPolicyVersionId = markingPolicy.PolicyId,
            ScoreConversionSnapshotJson = AssessmentScoreConversionSnapshot
                .Capture(scoreConversionAtStart)
                .Serialize(),
            PolicySnapshotJson = JsonSupport.Serialize(new
            {
                markingPolicy = markingPolicy.Document,
                markingPolicyVersionKey = markingPolicy.PolicyVersionKey,
                markingPolicyErrorCode = markingPolicy.ErrorCode,
                listeningPolicy = new
                {
                    listeningPolicy.Id,
                    fullPaperTimerMinutes,
                    extraTimeEntitlementPct = ResolveExtraTimeEntitlementPct(listeningPolicy, userPolicyOverride),
                    listeningPolicy.GracePeriodSeconds,
                    listeningPolicy.OnExpirySubmitPolicy,
                    countdownWarningsSeconds = ListeningPolicyService.ParseCountdownWarnings(listeningPolicy.CountdownWarningsJson),
                    listeningPolicy.LearningReplayAllowed,
                    listeningPolicy.LearningEvidenceLoopEnabled,
                    shortAnswerNormalisation = listeningPolicy.ShortAnswerNormalisation,
                    listeningPolicy.ShortAnswerAcceptSynonyms,
                    listeningPolicy.ScreenReaderOptimised,
                    listeningPolicy.ShowExplanationsAfterSubmit,
                    listeningPolicy.ShowExplanationsOnlyIfWrong,
                    listeningPolicy.ShowCorrectAnswerOnReview,
                },
                audioLockMode = audioTransport.LockMode,
                canPause = audioTransport.CanPause,
                canScrub = audioTransport.CanScrub,
                onePlayOnly = audioTransport.OnePlayOnly,
                effectiveSessionPolicy,
                deadlineAt,
            })
        };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        // Lock the policy only after the candidate attempt is durable.
        await markingPolicyResolver.MarkUsedAsync(markingPolicy.PolicyId!, ct);
        if (scoreConversionAtStart.TableId is not null && scoreConversionAtStart.IsAvailable)
            await conversionResolver.MarkUsedAsync(scoreConversionAtStart.TableId, ct);
        return AttemptDto(attempt, new Dictionary<string, string?>(), feedbackMessage);
    }

    public async Task<object> StartPartPracticeAttemptAsync(
        string userId,
        string paperId,
        string partCode,
        CancellationToken ct)
    {
        await EnsureLearnerMutationAllowedAsync(userId, ct);
        await RequirePaperAccessIfAuthoredAsync(userId, paperId, ct);

        var normalizedPart = NormalizeListeningParentPart(partCode)
            ?? throw ApiException.Validation("part_code_invalid", "partCode must be A, B, or C.");

        var source = await ResolveSourceAsync(paperId, ct);
        // Auto-backfill (user-transparent): if the paper has authored JSON questions
        // but no relational rows yet, project them now so part-practice grading
        // (which requires ListeningQuestions) succeeds without admin action.
        if (!source.UsesRelationalStructure && source.Questions.Count > 0)
        {
            source = await EnsureRelationalBackfillAsync(paperId, source, ct);
        }

        var scopedQuestions = source.Questions
            .Where(question => string.Equals(
                ListeningParentPartFromCode(question.PartCode),
                normalizedPart,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(question => question.Number)
            .ToList();
        if (scopedQuestions.Count == 0)
        {
            // If we still have no questions for this part but the paper does have
            // relational rows, the part truly has no authored items.
            // If the paper still has no relational structure, the auto-backfill
            // above either failed or the JSON has no questions for this part.
            throw ApiException.Validation(
                "part_practice_no_questions",
                $"No published Listening questions exist for Part {normalizedPart} on this paper.");
        }

        var scopedSource = ApplyQuestionScope(source, scopedQuestions.Select(q => q.Id).ToList());
        var minutes = PartPracticeMinutes(normalizedPart);
        return await StartRelationalAttemptAsync(
            userId,
            scopedSource,
            "practice",
            normalizedPathwayStage: null,
            forceNewAttempt: false,
            ct,
            billObjectivePractice: true,
            partPracticePartCode: normalizedPart,
            partPracticeQuestionIds: scopedQuestions.Select(q => q.Id).ToList(),
            partPracticeMinutes: minutes);
    }

    private async Task<ListeningSource> EnsureRelationalBackfillAsync(string paperId, ListeningSource currentSource, CancellationToken ct)
    {
        var svc = backfillService ?? new ListeningBackfillService(db);
        try
        {
            var report = await svc.BackfillPaperAsync(paperId, "system:auto-backfill", bypassAttemptsGuard: true, ct);
            if (!report.Success)
            {
                return currentSource;
            }

            var refreshed = await ResolveSourceAsync(paperId, ct);
            return refreshed.UsesRelationalStructure ? refreshed : currentSource;
        }
        catch
        {
            return currentSource;
        }
    }

    public async Task<object> GetAttemptAsync(string userId, string attemptId, CancellationToken ct)
    {
        var relationalAttempt = await TryGetRelationalAttemptOwnedByUserAsync(userId, attemptId, asNoTracking: true, ct);
        if (relationalAttempt is not null)
        {
            return RelationalAttemptDto(relationalAttempt, await LoadRelationalAnswersAsync(relationalAttempt.Id, ct));
        }

        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, ct);
        return AttemptDto(attempt, DeserializeAnswers(attempt.AnswersJson));
    }

    public async Task SaveAnswerAsync(string userId, string attemptId, string questionId, ListeningAnswerSaveRequest request, CancellationToken ct)
    {
        await EnsureLearnerMutationAllowedAsync(userId, ct);
        var relationalAttempt = await TryGetRelationalAttemptOwnedByUserAsync(userId, attemptId, asNoTracking: false, ct);
        if (relationalAttempt is not null)
        {
            await SaveRelationalAnswerAsync(userId, relationalAttempt, questionId, request.UserAnswer, ct);
            return;
        }

        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, ct);
        await EnsureGenericAttemptCanMutateAsync(attempt, ct);
        var source = await ResolveSourceAsync(attempt.ContentId, ct);
        var question = source.Questions.FirstOrDefault(q => string.Equals(q.Id, questionId, StringComparison.Ordinal));
        if (question is null)
        {
            throw ApiException.Validation("listening_question_not_found", "This question does not belong to the Listening attempt.");
        }

        // Legacy JSON attempts persist the one-way section cursor in the
        // answer map. Enforce it server-side for both prior and future
        // sections; only the active section may be edited.
        if (IsExamMode(attempt.Mode))
        {
            var currentCursor = ReadGenericSectionCursor(DeserializeAnswers(attempt.AnswersJson));
            var questionCursor = ListeningSectionCursorForPartCode(question.PartCode);
            var isPartCQuestionScope = IsPartCQuestionScope(currentCursor, questionCursor);
            if (!isPartCQuestionScope && (questionCursor < 0 || questionCursor < currentCursor))
            {
                throw ApiException.Validation(
                    "listening_section_locked",
                    "This Listening section is locked and its answers can no longer be changed.");
            }
            if (!isPartCQuestionScope && questionCursor > currentCursor)
            {
                throw ApiException.Validation(
                    "listening_section_not_active",
                    "This Listening section is not active yet.");
            }
        }

        var answers = DeserializeAnswers(attempt.AnswersJson);
        answers[questionId] = request.UserAnswer;
        attempt.AnswersJson = JsonSupport.Serialize(answers);
        attempt.LastClientSyncAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// One-way section navigation. Stores a monotonically non-decreasing
    /// <c>sectionCursor</c> integer, rejecting backward moves and forward
    /// skips. The client also enforces one-way; this is the
    /// server-authoritative guard.
    /// <para>
    /// For a relational <see cref="ListeningAttempt"/> the cursor lives in its
    /// <see cref="ListeningAttempt.NavigationStateJson"/> column (merged
    /// alongside any existing FSM <c>state</c>/<c>locks</c> keys). Generic
    /// <see cref="Attempt"/> rows — which back real exam attempts on
    /// JSON-fallback / legacy papers — have no NavigationStateJson column, so
    /// the cursor is persisted under the reserved
    /// <see cref="GenericSectionCursorKey"/> key inside the attempt's
    /// <c>AnswersJson</c> map (excluded from answered-counts and ignored by the
    /// answer-keyed grader). Both stores apply identical forward-only
    /// validation.
    /// </para>
    /// </summary>
    public async Task<object> AdvanceSectionAsync(
        string userId,
        string attemptId,
        ListeningAdvanceSectionRequest request,
        CancellationToken ct)
    {
        await EnsureLearnerMutationAllowedAsync(userId, ct);

        var requested = request?.SectionCursor ?? 0;
        if (requested < 0)
        {
            throw ApiException.Validation(
                "listening_section_cursor_invalid",
                "Section cursor must be a non-negative integer.");
        }

        var relationalAttempt = await TryGetRelationalAttemptOwnedByUserAsync(userId, attemptId, asNoTracking: false, ct);
        if (relationalAttempt is not null)
        {
            await EnsureRelationalAttemptCanMutateAsync(relationalAttempt, ct);

            var current = ReadSectionCursor(relationalAttempt.NavigationStateJson);
            if (requested < current)
            {
                throw ApiException.Validation(
                    "listening_section_one_way",
                    $"Listening sections advance one-way: cannot move from section {current} back to {requested}.");
            }
            if (requested > current + 1)
            {
                throw ApiException.Validation(
                    "listening_section_sequence_invalid",
                    $"Listening sections must advance one boundary at a time; cannot move from section {current} to {requested}.");
            }

            var nowRelational = DateTimeOffset.UtcNow;
            relationalAttempt.NavigationStateJson = WriteSectionCursor(relationalAttempt.NavigationStateJson, requested);
            relationalAttempt.LastActivityAt = nowRelational;
            relationalAttempt.RowVersion++;
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException)
            {
                throw ApiException.Conflict("listening_attempt_concurrent_update",
                    "This attempt was modified by another process. Please retry.");
            }

            return new { attemptId = relationalAttempt.Id, sectionCursor = requested, lastClientSyncAt = relationalAttempt.LastActivityAt };
        }

        // Generic-store fallback: real exam attempts on JSON-fallback / legacy
        // papers live here (the relational ListeningAttempts table is empty for
        // them). Persist the same monotonic cursor under a reserved AnswersJson
        // key, applying identical forward-only validation.
        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, ct);
        await EnsureGenericAttemptCanMutateAsync(attempt, ct);

        var answers = DeserializeAnswers(attempt.AnswersJson);
        var currentGeneric = ReadGenericSectionCursor(answers);
        if (requested < currentGeneric)
        {
            throw ApiException.Validation(
                "listening_section_one_way",
                $"Listening sections advance one-way: cannot move from section {currentGeneric} back to {requested}.");
        }
        if (requested > currentGeneric + 1)
        {
            throw ApiException.Validation(
                "listening_section_sequence_invalid",
                $"Listening sections must advance one boundary at a time; cannot move from section {currentGeneric} to {requested}.");
        }

        var now = DateTimeOffset.UtcNow;
        answers[GenericSectionCursorKey] = requested.ToString(System.Globalization.CultureInfo.InvariantCulture);
        attempt.AnswersJson = JsonSupport.Serialize(answers);
        attempt.LastClientSyncAt = now;
        await db.SaveChangesAsync(ct);

        return new { attemptId = attempt.Id, sectionCursor = requested, lastClientSyncAt = attempt.LastClientSyncAt };
    }

    /// <summary>Read the monotonic <c>sectionCursor</c> from a generic attempt's
    /// answers map (under <see cref="GenericSectionCursorKey"/>). Returns 0 when
    /// absent or non-numeric.</summary>
    private static int ReadGenericSectionCursor(IReadOnlyDictionary<string, string?> answers)
        => answers.TryGetValue(GenericSectionCursorKey, out var raw)
            && int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var cursor)
            && cursor >= 0
                ? cursor
                : 0;

    /// <summary>Read the monotonic <c>sectionCursor</c> from a navigation-state
    /// JSON object. Returns 0 when absent, null, or malformed.</summary>
    private static int ReadSectionCursor(string? navigationStateJson)
    {
        if (string.IsNullOrWhiteSpace(navigationStateJson)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(navigationStateJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return 0;
            if (!doc.RootElement.TryGetProperty("sectionCursor", out var value)) return 0;
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var cursor) ? cursor : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>Merge a new <c>sectionCursor</c> into the navigation-state JSON,
    /// preserving any sibling FSM keys (<c>state</c>, <c>locks</c>, …). Starts a
    /// fresh object when the existing column is null or not a JSON object.</summary>
    private static string WriteSectionCursor(string? navigationStateJson, int cursor)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(navigationStateJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(navigationStateJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (string.Equals(prop.Name, "sectionCursor", StringComparison.Ordinal)) continue;
                        fields[prop.Name] = prop.Value.Clone();
                    }
                }
            }
            catch (JsonException)
            {
                fields.Clear();
            }
        }

        fields["sectionCursor"] = JsonSerializer.SerializeToElement(cursor);
        return JsonSerializer.Serialize(fields);
    }

    public async Task<object> HeartbeatAsync(string userId, string attemptId, HeartbeatRequest request, CancellationToken ct)
    {
        await EnsureLearnerMutationAllowedAsync(userId, ct);
        var relationalAttempt = await TryGetRelationalAttemptOwnedByUserAsync(userId, attemptId, asNoTracking: false, ct);
        if (relationalAttempt is not null)
        {
            // Heartbeat is best-effort activity-tracking and races the submit
            // POST: a 15s tick can land just after the attempt has been
            // marked Submitted, which previously surfaced a 409 to the
            // browser console. Treat already-finalised attempts as a no-op
            // so the client's unmount race never produces a hard error.
            if (relationalAttempt.Status != ListeningAttemptStatus.InProgress)
            {
                return new { attemptId = relationalAttempt.Id, elapsedSeconds = request.ElapsedSeconds, lastClientSyncAt = relationalAttempt.LastActivityAt };
            }
            await EnsureRelationalAttemptCanMutateAsync(relationalAttempt, ct);
            relationalAttempt.LastActivityAt = DateTimeOffset.UtcNow;
            relationalAttempt.RowVersion++;
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { /* heartbeat is best-effort; retry on next tick */ }
            return new { attemptId = relationalAttempt.Id, elapsedSeconds = request.ElapsedSeconds, lastClientSyncAt = relationalAttempt.LastActivityAt };
        }

        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, ct);
        // Same race-tolerance for the legacy attempt store.
        if (attempt.State is AttemptState.Submitted or AttemptState.Evaluating or AttemptState.Completed)
        {
            return new { attemptId = attempt.Id, attempt.ElapsedSeconds, attempt.LastClientSyncAt };
        }
        await EnsureGenericAttemptCanMutateAsync(attempt, ct);
        attempt.ElapsedSeconds = Math.Max(
            request.ElapsedSeconds,
            (int)Math.Max(0, (DateTimeOffset.UtcNow - attempt.StartedAt).TotalSeconds));
        attempt.LastClientSyncAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.DeviceType)) attempt.DeviceType = request.DeviceType;
        await db.SaveChangesAsync(ct);
        return new { attemptId = attempt.Id, attempt.ElapsedSeconds, attempt.LastClientSyncAt };
    }

    public Task<object> SubmitAsync(string userId, string attemptId, CancellationToken ct) =>
        SubmitAsync(userId, attemptId, null, null, ct);

    public async Task<object> SubmitAsync(
        string userId,
        string attemptId,
        IReadOnlyDictionary<string, string?>? finalAnswers,
        CancellationToken ct)
        => await SubmitAsync(userId, attemptId, finalAnswers, null, ct);

    public async Task<object> SubmitAsync(
        string userId,
        string attemptId,
        IReadOnlyDictionary<string, string?>? finalAnswers,
        string? idempotencyKey,
        CancellationToken ct)
    {
        await EnsureLearnerMutationAllowedAsync(userId, ct);
        var relationalAttempt = await TryGetRelationalAttemptOwnedByUserAsync(userId, attemptId, asNoTracking: false, ct);
        if (relationalAttempt is not null)
        {
            EnsureAttemptNotOnAdminReviewHold(relationalAttempt.RequiresAdminReview, relationalAttempt.AdminReviewReason);
            var relationalKey = BuildSubmitIdempotencyKey(userId, relationalAttempt.Id, idempotencyKey);
            var relationalCached = await GetCachedSubmitAsync(relationalKey, ct);
            if (relationalCached is not null) return relationalCached;
            return await SubmitRelationalAttemptAsync(userId, relationalAttempt, finalAnswers, relationalKey, ct);
        }

        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, ct);
        EnsureAttemptNotOnAdminReviewHold(attempt.RequiresAdminReview, attempt.AdminReviewReason);
        var key = BuildSubmitIdempotencyKey(userId, attempt.Id, idempotencyKey);
        var cached = await GetCachedSubmitAsync(key, ct);
        if (cached is not null) return cached;
        var source = await ResolveSourceAsync(attempt.ContentId, ct);

        if (source.Questions.Count == 0)
        {
            throw ApiException.Validation("listening_questions_missing", "This Listening attempt has no structured questions to grade.");
        }

        var existing = await db.Evaluations.FirstOrDefaultAsync(e => e.AttemptId == attempt.Id, ct);
        if (attempt.State == AttemptState.Completed && existing is not null)
        {
            var existingReview = BuildReview(attempt, source, existing);
            return await PersistSubmitIdempotencyAsync(key, existingReview, ct) ?? existingReview;
        }

        var submitNow = DateTimeOffset.UtcNow;
        var genericDeadlineAt = ReadGenericDeadline(attempt);
        var timedOut = genericDeadlineAt is { } deadline && submitNow > deadline;
        if (!timedOut && finalAnswers is { Count: > 0 })
        {
            ApplyFinalLegacyAnswers(attempt, source, finalAnswers);
        }

        var legacyAnswers = DeserializeAnswers(attempt.AnswersJson);
        var multipleSelectionIssues = FindMultipleSelectionMcqAnswers(source, legacyAnswers);
        if (multipleSelectionIssues.Count > 0)
        {
            // Keep the legacy JSON-attempt path aligned with the relational
            // grader: a single-answer MCQ payload containing more than one
            // selected option is invalid for automated scoring, not simply an
            // incorrect answer. Preserve the attempt for administrator review
            // and never create an automated evaluation or score conversion.
            attempt.RequiresAdminReview = true;
            attempt.AdminReviewReason ??= "multiple_selections_for_single_answer_mcq";
            attempt.AdminReviewFlaggedAt ??= submitNow;
            attempt.State = AttemptState.Submitted;
            attempt.SubmittedAt = submitNow;
            attempt.CompletedAt = null;
            attempt.LastClientSyncAt = submitNow;
            attempt.ElapsedSeconds = (int)Math.Clamp(
                (submitNow - attempt.StartedAt).TotalSeconds,
                0,
                int.MaxValue);
            attempt.DraftVersion++;
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = submitNow,
                ActorId = userId,
                ActorName = userId,
                Action = "listening.mcq.multiple_selection_review_required",
                ResourceType = "Attempt",
                ResourceId = attempt.Id,
                Details = JsonSerializer.Serialize(new
                {
                    reason = "multiple_selections_for_single_answer_mcq",
                    issues = multipleSelectionIssues,
                }),
            });
            await db.SaveChangesAsync(ct);
            throw ApiException.Conflict(
                "listening_attempt_requires_admin_review",
                "This Listening attempt requires administrator review before scoring. Reason: multiple_selections_for_single_answer_mcq.");
        }

        var review = BuildReview(attempt, source);
        var conversionResolver = scoreConversionService ?? new AssessmentScoreConversionService(db);
        var conversion = await AssessmentScoreConversionSnapshotResolver.ResolveAsync(
            conversionResolver,
            Subtest,
            review.RawScore,
            attempt.ScoreConversionSnapshotJson,
            legacyTableId: null,
            scopeKey: "default",
            ct);
        if (conversion.TableId is not null && conversion.IsAvailable)
        {
            await conversionResolver.MarkUsedAsync(conversion.TableId, ct);
        }
        var score = ApplyScoreConversionGate(new ListeningScoreDto(
            review.RawScore,
            review.MaxRawScore,
            conversion.ConvertedScore,
            conversion.Grade ?? "—",
            conversion.Passed), conversion.TableVersionKey);
        var hasApprovedConversion = HasApprovedScoreConversion(
            conversion.TableVersionKey,
            score.ScaledScore,
            score.Passed,
            score.MaxRawScore);

        attempt.State = AttemptState.Completed;
        attempt.SubmittedAt = submitNow;
        attempt.CompletedAt = attempt.SubmittedAt;
        attempt.LastClientSyncAt = submitNow;
        var effectiveEndAt = timedOut && genericDeadlineAt.HasValue
            ? genericDeadlineAt.Value
            : submitNow;
        attempt.ElapsedSeconds = (int)Math.Clamp(
            (effectiveEndAt - attempt.StartedAt).TotalSeconds,
            0,
            int.MaxValue);

        var evaluation = new Evaluation
        {
            Id = $"le-{Guid.NewGuid():N}",
            AttemptId = attempt.Id,
            SubtestCode = Subtest,
            RawScore = score.RawScore,
            MaxRawScore = score.MaxRawScore,
            ScaledScore = score.ScaledScore,
            ScoreConversionTableVersionKey = hasApprovedConversion ? conversion.TableVersionKey : null,
            ScoreConversionGrade = hasApprovedConversion ? conversion.Grade : null,
            ScoreConversionPassed = hasApprovedConversion ? conversion.Passed : null,
            State = AsyncState.Completed,
            ScoreRange = FormatScoreDisplay(score),
            GradeRange = $"Grade {score.Grade}",
            ConfidenceBand = ConfidenceBand.High,
            StrengthsJson = JsonSupport.Serialize(review.Strengths),
            IssuesJson = JsonSupport.Serialize(review.Issues),
            CriterionScoresJson = JsonSupport.Serialize(new[]
            {
                new
                {
                    criterionCode = "listening_accuracy",
                    rawScore = score.RawScore,
                    maxRawScore = score.MaxRawScore,
                    scaledScore = score.ScaledScore,
                    grade = score.Grade,
                    passed = score.Passed,
                    scoreDisplay = FormatScoreDisplay(score)
                }
            }),
            FeedbackItemsJson = JsonSupport.Serialize(review.ItemReview
                .Where(item => !item.IsCorrect && !item.IsInvalid)
                .Select(item => new
                {
                    feedbackItemId = $"{attempt.Id}-{item.QuestionId}",
                    criterionCode = item.ErrorType ?? "detail_capture",
                    type = "answer_feedback",
                    anchor = new { questionId = item.QuestionId },
                    message = item.Explanation,
                    severity = "medium",
                    suggestedFix = item.DistractorExplanation ?? "Review the transcript evidence and repeat the same error type as a short drill."
                })),
            GeneratedAt = DateTimeOffset.UtcNow,
            ModelExplanationSafe = "Listening result is graded deterministically from the authored answer key.",
            LearnerDisclaimer = "Practice result only. This is not an official OET Statement of Results.",
            StatusReasonCode = "completed",
            StatusMessage = "Result ready.",
            LastTransitionAt = DateTimeOffset.UtcNow
        };
        db.Evaluations.Add(evaluation);
        await LearnerWorkflowCoordinator.UpdateDiagnosticProgressAsync(db, attempt, AttemptState.Completed, ct);
        await LearnerWorkflowCoordinator.QueueStudyPlanRegenerationAsync(db, userId, ct);
        attempt.DraftVersion++;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent submit already committed the canonical attempt and
            // evaluation. Reload that winner and return the same review so a
            // duplicate request never becomes a spurious 409 or writes a
            // second evaluation.
            db.ChangeTracker.Clear();
            var winningAttempt = await GetAttemptOwnedByUserAsync(userId, attemptId, ct);
            var winningSource = await ResolveSourceAsync(winningAttempt.ContentId, ct);
            var winningEvaluation = await db.Evaluations.AsNoTracking()
                .Where(e => e.AttemptId == winningAttempt.Id)
                .OrderByDescending(e => e.GeneratedAt)
                .FirstOrDefaultAsync(ct);
            if (winningAttempt.State != AttemptState.Completed || winningEvaluation is null)
            {
                throw ApiException.Conflict(
                    "listening_submit_concurrent_update",
                    "This Listening submit is still being finalized. Please retry with the same Idempotency-Key.");
            }

            var winningReview = BuildReview(winningAttempt, winningSource, winningEvaluation);
            return await PersistSubmitIdempotencyAsync(key, winningReview, ct) ?? winningReview;
        }

        // Recalls auto-seed: turn wrong free-text listening answers into
        // starred SM-2 cards. Best-effort — failures must never break grading.
        if (autoSeed is not null)
        {
            try
            {
                var wrongFreeText = review.ItemReview
                    .Where(item => !item.IsCorrect && !item.IsInvalid && !string.IsNullOrWhiteSpace(item.CorrectAnswer))
                    .Select(item => new RecallsListeningSeedItem(
                        QuestionId: item.QuestionId,
                        Type: item.Type,
                        Prompt: item.Prompt,
                        LearnerAnswer: item.LearnerAnswer,
                        CorrectAnswer: item.CorrectAnswer));
                await autoSeed.SeedFromListeningAsync(userId, attempt.Id, wrongFreeText, ct);
            }
            catch
            {
                // swallow — auto-seed must not break grading
            }
        }

        var completedReview = BuildReview(attempt, source, evaluation);
        return await PersistSubmitIdempotencyAsync(key, completedReview, ct) ?? completedReview;
    }

    public async Task<object> GetReviewAsync(string userId, string attemptId, CancellationToken ct)
    {
        var relationalAttempt = await TryGetRelationalAttemptOwnedByUserAsync(userId, attemptId, asNoTracking: true, ct);
        if (relationalAttempt is not null)
        {
            if (relationalAttempt.Status != ListeningAttemptStatus.Submitted)
            {
                throw ApiException.Validation(
                    "listening_review_unavailable",
                    "Transcript-backed review is available after the Listening attempt is submitted.");
            }

            var relationalSource = ApplyAttemptScope(
                await ResolveSourceAsync(relationalAttempt.PaperId, ct),
                relationalAttempt);
            var relationalEvaluation = await db.Evaluations.AsNoTracking()
                .Where(e => e.AttemptId == relationalAttempt.Id)
                .OrderByDescending(e => e.GeneratedAt)
                .FirstOrDefaultAsync(ct);
            var answers = await LoadRelationalAnswersAsync(relationalAttempt.Id, ct);
            var answerRows = await db.ListeningAnswers.AsNoTracking()
                .Where(answer => answer.ListeningAttemptId == relationalAttempt.Id)
                .ToDictionaryAsync(answer => answer.ListeningQuestionId, StringComparer.Ordinal, ct);
            return BuildReview(
                relationalAttempt,
                relationalSource,
                answers,
                relationalEvaluation,
                answerRows);
        }

        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, ct);
        if (attempt.State != AttemptState.Completed)
        {
            throw ApiException.Validation(
                "listening_review_unavailable",
                "Transcript-backed review is available after the Listening attempt is submitted.");
        }

        var source = await ResolveSourceAsync(attempt.ContentId, ct);
        var evaluation = await db.Evaluations.AsNoTracking()
            .Where(e => e.AttemptId == attempt.Id)
            .OrderByDescending(e => e.GeneratedAt)
            .FirstOrDefaultAsync(ct);
        return BuildReview(attempt, source, evaluation);
    }

    public Task<object> GetDrillAsync(string drillId, string? paperId, string? attemptId, CancellationToken ct)
    {
        var normalized = NormalizeDrillId(drillId);
        return Task.FromResult<object>(BuildDrill(normalized.Replace("listening-drill-", string.Empty, StringComparison.Ordinal), paperId, attemptId));
    }

    public async Task RecordIntegrityEventAsync(
        string userId,
        string attemptId,
        ListeningIntegrityEventRequest request,
        CancellationToken ct)
    {
        await EnsureLearnerMutationAllowedAsync(userId, ct);
        var relationalAttempt = await TryGetRelationalAttemptOwnedByUserAsync(userId, attemptId, asNoTracking: false, ct);
        var attempt = relationalAttempt is null
            ? await GetAttemptOwnedByUserAsync(userId, attemptId, ct)
            : null;

        var rawEventType = string.IsNullOrWhiteSpace(request.EventType)
            ? "unknown"
            : request.EventType.Trim();
        if (rawEventType.Length > 64) rawEventType = rawEventType[..64];
        // §17.11 — recognise the attempt-event stream alongside the existing
        // OET@Home integrity-lock events. Unrecognised types are still
        // recorded (as a length-clamped passthrough) so a client rollout that
        // adds a new event never silently drops data; the `recognized` flag is
        // captured in the payload for downstream filtering.
        var recognized = RecognizedIntegrityEventTypes.Contains(rawEventType);
        var eventType = rawEventType;

        // §17.11 — the new attempt events carry their structured fields
        // (cuePointMs, questionId, …) as a compact JSON `details` string. Pull
        // them back out so they land as first-class fields on the AuditEvent
        // payload, while still preserving the raw details for forward-compat.
        var cuePointMs = ReadJsonInt(request.Details, "cuePointMs");
        var questionId = ReadJsonProperty(request.Details, "questionId");
        var section = ReadJsonProperty(request.Details, "section");
        var questionIndex = ReadJsonInt(request.Details, "questionIndex");

        var now = DateTimeOffset.UtcNow;
        var requiresAdminReview = string.Equals(eventType, "audio_error", StringComparison.Ordinal);
        const string adminReviewReason = "audio_playback_error";
        if (relationalAttempt is not null)
        {
            relationalAttempt.LastActivityAt = now;
            if (requiresAdminReview)
            {
                relationalAttempt.RequiresAdminReview = true;
                relationalAttempt.AdminReviewReason ??= adminReviewReason;
                relationalAttempt.AdminReviewFlaggedAt ??= now;
            }
            // §17.11 — audio lifecycle events also append to the per-attempt
            // audio cue timeline (the column already exists). Append, never
            // overwrite, so the full replay log accumulates across sections.
            if (eventType is "audio_started" or "audio_progress" or "audio_ended")
            {
                relationalAttempt.AudioCueTimelineJson = AppendAudioCueTimelineEntry(
                    relationalAttempt.AudioCueTimelineJson,
                    cue: eventType,
                    atMs: cuePointMs,
                    occurredAt: request.OccurredAt ?? now,
                    section: section,
                    questionIndex: questionIndex);
            }
            // A genuine playback start proves the earlier audio_error was
            // transient (e.g. an autoplay-policy rejection), so release the
            // review hold and let the learner submit normally.
            if (eventType == "audio_started" && relationalAttempt.AdminReviewReason == adminReviewReason)
            {
                relationalAttempt.RequiresAdminReview = false;
                relationalAttempt.AdminReviewReason = null;
                relationalAttempt.AdminReviewFlaggedAt = null;
            }
        }
        else if (attempt is not null)
        {
            attempt.LastClientSyncAt = now;
            if (eventType is "audio_started" or "audio_progress" or "audio_ended")
            {
                var answers = DeserializeAnswers(attempt.AnswersJson);
                SetGenericAudioPlayback(answers, eventType, cuePointMs, section, questionIndex);
                attempt.AnswersJson = JsonSupport.Serialize(answers);
            }
            if (requiresAdminReview)
            {
                attempt.RequiresAdminReview = true;
                attempt.AdminReviewReason ??= adminReviewReason;
                attempt.AdminReviewFlaggedAt ??= now;
            }
            // Mirror the relational path: a real playback start clears a prior
            // audio_playback_error hold so a one-off glitch cannot block submit.
            if (eventType == "audio_started" && attempt.AdminReviewReason == adminReviewReason)
            {
                attempt.RequiresAdminReview = false;
                attempt.AdminReviewReason = null;
                attempt.AdminReviewFlaggedAt = null;
            }
        }

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = request.OccurredAt ?? now,
            ActorId = userId,
            ActorName = userId,
            Action = "ListeningIntegrityEvent",
            ResourceType = relationalAttempt is not null ? "ListeningAttempt" : "Attempt",
            ResourceId = relationalAttempt?.Id ?? attempt!.Id,
            Details = JsonSupport.Serialize(new
            {
                eventType,
                recognized,
                mode = relationalAttempt is not null ? ToApiMode(relationalAttempt.Mode) : attempt!.Mode,
                cuePointMs,
                questionId,
                section,
                questionIndex,
                requiresAdminReview,
                adminReviewReason = requiresAdminReview ? adminReviewReason : null,
                request.Details,
                serverRecordedAt = now,
            }),
        });
        await db.SaveChangesAsync(ct);
    }

    // §17.11 — full recognised event-type set: existing OET@Home integrity
    // lock events plus the attempt-event stream emitted by the player.
    private static readonly HashSet<string> RecognizedIntegrityEventTypes = new(StringComparer.Ordinal)
    {
        // OET@Home integrity-lock events.
        "fullscreen_enter",
        "fullscreen_exit",
        "fullscreen_request_failed",
        "page_hidden",
        "page_visible",
        "window_blur",
        "window_focus",
        "audio_seek_blocked",
        "audio_pause_blocked",
        "audio_replay_blocked",
        "audio_speed_change_blocked",
        // §17.11 attempt-event stream.
        "audio_started",
        "audio_stopped",
        "audio_progress",
        "audio_ended",
        "audio_buffering_start",
        "audio_buffering_end",
        "audio_stalled",
        "audio_error",
        "reading_time_started",
        "reading_time_ended",
        "answer_changed",
        "highlight",
        "strikethrough",
        "section_transition",
        "auto_submit",
    };

    /// <summary>§17.11 — append one compact entry to the attempt's audio cue
    /// timeline (<c>[{"cue":"audio_started","atMs":1234,"at":"..."}]</c>),
    /// preserving any prior entries. Tolerates a null / malformed existing
    /// column by starting a fresh array.</summary>
    private static string AppendAudioCueTimelineEntry(
        string? existingJson,
        string cue,
        int? atMs,
        DateTimeOffset occurredAt,
        string? section,
        int? questionIndex)
    {
        var entries = new List<JsonElement>();
        if (!string.IsNullOrWhiteSpace(existingJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(existingJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        entries.Add(item.Clone());
                    }
                }
            }
            catch (JsonException)
            {
                // Malformed prior timeline — start fresh rather than 500.
                entries.Clear();
            }
        }

        // Keep the replay log bounded: only the latest progress checkpoint is
        // needed to resume the current audio run. Start/end events remain a
        // complete audit history.
        if (string.Equals(cue, "audio_progress", StringComparison.Ordinal))
        {
            entries = entries
                .Where(item => !string.Equals(
                    item.TryGetProperty("cue", out var priorCue) ? priorCue.GetString() : null,
                    "audio_progress",
                    StringComparison.Ordinal))
                .ToList();
        }

        var appended = JsonSerializer.SerializeToElement(new
        {
            cue,
            atMs,
            section,
            questionIndex,
            at = occurredAt,
        });
        entries.Add(appended);
        return JsonSerializer.Serialize(entries);
    }

    private static void SetGenericAudioPlayback(
        Dictionary<string, string?> answers,
        string eventType,
        int? cuePointMs,
        string? section,
        int? questionIndex)
    {
        answers[GenericAudioPlaybackStateKey] = eventType == "audio_ended" ? "ended" : "active";
        answers[GenericAudioResumeMsKey] = eventType == "audio_ended"
            ? null
            : Math.Max(0, cuePointMs ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
        answers[GenericAudioSectionKey] = section;
        answers[GenericAudioQuestionIndexKey] = questionIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed record AudioPlaybackSnapshot(
        string State,
        int? ResumeAtMs,
        string? Section,
        int? QuestionIndex);

    private static readonly string[] ListeningSectionCodes = ["A1", "A2", "B", "C1", "C2"];

    private static int? SaturatingMilliseconds(int seconds)
    {
        if (seconds <= 0) return null;
        var milliseconds = (long)seconds * 1000L;
        return milliseconds >= int.MaxValue ? int.MaxValue : (int)milliseconds;
    }

    private static int? ElapsedMilliseconds(DateTimeOffset startedAt, DateTimeOffset? endedAt)
    {
        if (!endedAt.HasValue || endedAt.Value <= startedAt) return null;
        var milliseconds = (long)Math.Round((endedAt.Value - startedAt).TotalMilliseconds);
        return milliseconds <= 0 ? null : milliseconds >= int.MaxValue ? int.MaxValue : (int)milliseconds;
    }

    // Section values are playback telemetry only. Pair persisted cue positions
    // for completed runs; never turn missing telemetry into an estimate or a
    // scoring input.
    private static IReadOnlyList<ListeningTimeUsedSectionDto> BuildSectionTimeUsed(string? timelineJson)
    {
        var totals = ListeningSectionCodes.ToDictionary(code => code, _ => 0L, StringComparer.Ordinal);
        var openStarts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(timelineJson))
        {
            return ListeningSectionCodes
                .Select(code => new ListeningTimeUsedSectionDto(code, null))
                .ToList();
        }

        try
        {
            using var doc = JsonDocument.Parse(timelineJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (!item.TryGetProperty("cue", out var cueValue)
                        || cueValue.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var cue = cueValue.GetString();
                    if (cue is not ("audio_started" or "audio_ended")) continue;
                    if (!item.TryGetProperty("section", out var sectionValue)
                        || sectionValue.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var section = NormalizeListeningSection(sectionValue.GetString());
                    if (section is null
                        || !item.TryGetProperty("atMs", out var atValue)
                        || atValue.ValueKind != JsonValueKind.Number
                        || !atValue.TryGetInt32(out var atMs))
                    {
                        continue;
                    }

                    atMs = Math.Max(0, atMs);
                    if (cue == "audio_started")
                    {
                        openStarts[section] = atMs;
                        continue;
                    }

                    if (openStarts.Remove(section, out var startMs) && atMs >= startMs)
                    {
                        totals[section] = Math.Min(int.MaxValue, totals[section] + (atMs - startMs));
                    }
                }
            }
        }
        catch (JsonException)
        {
            // A malformed audit timeline must not make results unloadable.
        }

        return ListeningSectionCodes
            .Select(code => new ListeningTimeUsedSectionDto(
                code,
                totals[code] > 0 ? (int?)totals[code] : null))
            .ToList();
    }

    private static string? NormalizeListeningSection(string? rawSection)
    {
        var section = rawSection?.Trim().ToUpperInvariant();
        if (section is "A1" or "A2" or "C1" or "C2") return section;
        return section is not null && section.StartsWith("B", StringComparison.Ordinal) ? "B" : null;
    }

    private static AudioPlaybackSnapshot ReadAudioPlaybackSnapshot(string? timelineJson)
    {
        var snapshot = new AudioPlaybackSnapshot("not_started", null, null, null);
        if (string.IsNullOrWhiteSpace(timelineJson)) return snapshot;
        try
        {
            using var doc = JsonDocument.Parse(timelineJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return snapshot;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("cue", out var cueValue)) continue;
                var cue = cueValue.GetString();
                if (cue is not ("audio_started" or "audio_progress" or "audio_ended")) continue;
                var atMs = item.TryGetProperty("atMs", out var atValue)
                    && atValue.ValueKind == JsonValueKind.Number
                    && atValue.TryGetInt32(out var parsedAt)
                    ? Math.Max(0, parsedAt)
                    : (int?)null;
                var section = item.TryGetProperty("section", out var sectionValue)
                    ? sectionValue.GetString()
                    : null;
                var questionIndex = item.TryGetProperty("questionIndex", out var questionValue)
                    && questionValue.ValueKind == JsonValueKind.Number
                    && questionValue.TryGetInt32(out var parsedQuestion)
                    ? Math.Max(0, parsedQuestion)
                    : (int?)null;
                snapshot = cue == "audio_ended"
                    ? new AudioPlaybackSnapshot("ended", null, section, questionIndex)
                    : new AudioPlaybackSnapshot("active", atMs, section, questionIndex);
            }
        }
        catch (JsonException)
        {
            // A malformed audit timeline must never make an attempt unloadable.
        }
        return snapshot;
    }

    private static AudioPlaybackSnapshot ReadGenericAudioPlayback(
        IReadOnlyDictionary<string, string?> answers)
    {
        var state = answers.TryGetValue(GenericAudioPlaybackStateKey, out var rawState)
            && rawState is "active" or "ended"
            ? rawState!
            : "not_started";
        var resumeAtMs = answers.TryGetValue(GenericAudioResumeMsKey, out var rawMs)
            && int.TryParse(rawMs, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedMs)
            ? Math.Max(0, parsedMs)
            : (int?)null;
        var questionIndex = answers.TryGetValue(GenericAudioQuestionIndexKey, out var rawQuestion)
            && int.TryParse(rawQuestion, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedQuestion)
            ? Math.Max(0, parsedQuestion)
            : (int?)null;
        answers.TryGetValue(GenericAudioSectionKey, out var section);
        return new AudioPlaybackSnapshot(state, state == "active" ? resumeAtMs : null, section, questionIndex);
    }

    private async Task<object> StartRelationalAttemptAsync(
        string userId,
        ListeningSource source,
        string normalizedMode,
        string? normalizedPathwayStage,
        bool forceNewAttempt,
        CancellationToken ct,
        bool billObjectivePractice = true,
        string? partPracticePartCode = null,
        IReadOnlyList<string>? partPracticeQuestionIds = null,
        int? partPracticeMinutes = null)
    {
        var relationalMode = ToRelationalMode(normalizedMode);
        var isPartPractice = !string.IsNullOrWhiteSpace(partPracticePartCode)
            && partPracticeQuestionIds is { Count: > 0 };
        if (!forceNewAttempt)
        {
            var existingCandidates = await db.ListeningAttempts
                .Where(a => a.UserId == userId
                    && a.PaperId == source.Id
                    && a.Mode == relationalMode
                    && a.Status == ListeningAttemptStatus.InProgress)
                .OrderByDescending(a => a.LastActivityAt)
                .ToListAsync(ct);
            var existing = isPartPractice
                ? existingCandidates.FirstOrDefault(a =>
                    ListeningAttemptScope.MatchesRequestedPartPractice(a.ScopeJson, partPracticePartCode!))
                : existingCandidates.FirstOrDefault(a =>
                    ListeningAttemptScope.MatchesRequestedScope(a.ScopeJson, normalizedPathwayStage));
            if (existing is not null)
            {
                var existingAnswers = await LoadRelationalAnswersAsync(existing.Id, ct);
                return isPartPractice
                    ? PartPracticeStartedDto(existing, source, partPracticePartCode!, existingAnswers)
                    : RelationalAttemptDto(existing, existingAnswers);
            }
        }

        var (policy, userPolicyOverride) = await ResolveListeningPolicyAsync(userId, ct);
        EnsureAttemptsAllowed(userPolicyOverride);
        await EnsureAttemptEligibilityAsync(userId, source, normalizedMode, policy, ct);
        var effectiveSessionPolicy = ListeningPolicyResolver.Resolve(policy, userPolicyOverride);
        var fullPaperTimerMinutes = ResolveFullPaperTimerMinutes(policy, userPolicyOverride);
        var now = DateTimeOffset.UtcNow;
        var isExamLike = IsExamMode(normalizedMode);

        // WS2: Strict, one-way-lock Listening exams (Exam / OET@Home) require a
        // passed pathway sound-check before the attempt can be created — so the
        // gate also blocks `startListeningAttempt`, not just the first FSM
        // advance. Practice / Paper / Diagnostic stay ungated even though they
        // are "exam-like" for one-play/audio-asset purposes: only Exam + Home
        // carry OneWayLocks. Mirrors ListeningSessionService's advance gate and
        // shares its TTL.
        if (relationalMode is ListeningAttemptMode.Exam or ListeningAttemptMode.Home
            && !await HasValidAudioCheckAsync(userId, now, ct))
        {
            throw ApiException.Validation(
                "listening_audio_check_required",
                "Pass the Listening sound check before starting this exam. Run the sound check, then return here to begin.");
        }

        // H11: Server-verify audio asset exists before allowing exam-mode attempt start.
        // The client shows audioAvailable but a race or stale cache could let a learner
        // start an attempt for a paper whose audio has been deleted or never uploaded.
        if (isExamLike && !HasAllRequiredAudioAssets(source))
        {
            throw ApiException.Conflict(
                "listening_audio_asset_missing",
                "This Listening paper does not have complete audio assets for every scored section. Cannot start exam-mode attempt.");
        }

        var relationalAttemptId = $"lat-{Guid.NewGuid():N}";

        var markingPolicyResolver = markingPolicyService ?? new AssessmentMarkingPolicyService(db);
        var markingPolicy = await markingPolicyResolver.ResolveAsync("listening", "default", cancellationToken: ct);
        if (!markingPolicy.IsAvailable || markingPolicy.ErrorCode is not null)
        {
            throw ApiException.Conflict(
                "listening_marking_policy_unavailable",
                "Listening attempts are unavailable until an owner-approved marking policy is effective.");
        }
        var conversionResolver = scoreConversionService ?? new AssessmentScoreConversionService(db);
        var scoreConversionAtStart = await conversionResolver.ResolveAsync(
            Subtest,
            rawScore: 0,
            scopeKey: "default",
            cancellationToken: ct);
        var audioTransport = ListeningAudioTransportPolicy.FromPolicy(
            normalizedMode,
            markingPolicy.Document,
            policy.LearningReplayAllowed);

        // Listening test-credit allowance. Governance must be resolved before
        // this debit: failed owner-controlled marking or conversion gates do
        // not create an attempt and therefore must not consume a credit.
        string? feedbackMessage = null;
        if (billObjectivePractice && aiPackageCreditService is not null)
        {
            var creditResult = await aiPackageCreditService.DeductObjectivePracticeAsync(
                userId, "listening",
                CreditGateExtensions.ObjectivePaperReference("listening", userId, source.Id),
                ct);
            creditResult.EnsureDebited();
            feedbackMessage = creditResult.FeedbackMessage;
        }

        var scopedQuestionIds = isPartPractice
            ? partPracticeQuestionIds!.ToHashSet(StringComparer.Ordinal)
            : null;
        var questionVersionMap = await db.ListeningQuestions.AsNoTracking()
            .Where(q => q.PaperId == source.Id && (scopedQuestionIds == null || scopedQuestionIds.Contains(q.Id)))
            .ToDictionaryAsync(q => q.Id, q => q.Version, StringComparer.Ordinal, ct);
        var scopedMaxRaw = source.Questions.Sum(q => q.Points);
        var deadlineAt = isPartPractice
            ? now.AddMinutes(Math.Max(1, partPracticeMinutes ?? PartPracticeMinutes(partPracticePartCode!)))
            : isExamLike
                ? now.AddMinutes(fullPaperTimerMinutes).AddSeconds(policy.GracePeriodSeconds)
                : (DateTimeOffset?)null;
        var attempt = new ListeningAttempt
        {
            Id = relationalAttemptId,
            MarkingPolicyVersionId = markingPolicy.PolicyId,
            ScoreConversionTableId = scoreConversionAtStart.TableId,
            ScoreConversionTableVersionKey = scoreConversionAtStart.TableVersionKey,
            ScoreConversionSnapshotJson = AssessmentScoreConversionSnapshot
                .Capture(scoreConversionAtStart)
                .Serialize(),
            UserId = userId,
            PaperId = source.Id,
            StartedAt = now,
            LastActivityAt = now,
            DeadlineAt = deadlineAt,
            Status = ListeningAttemptStatus.InProgress,
            Mode = relationalMode,
            MaxRawScore = scopedMaxRaw,
            PaperRevisionId = source.PaperRevisionId,
            // The published question revision is immutable for the lifetime of
            // an attempt. Keep the exact version map so a concurrent authoring
            // edit is rejected during grading instead of silently grading a
            // response against a different key.
            LastQuestionVersionMapJson = JsonSerializer.Serialize(questionVersionMap),
            PolicySnapshotJson = JsonSupport.Serialize(new
            {
                markingPolicy = markingPolicy.Document,
                markingPolicyVersionKey = markingPolicy.PolicyVersionKey,
                markingPolicyErrorCode = markingPolicy.ErrorCode,
                policy.Id,
                fullPaperTimerMinutes,
                extraTimeEntitlementPct = ResolveExtraTimeEntitlementPct(policy, userPolicyOverride),
                policy.GracePeriodSeconds,
                policy.OnExpirySubmitPolicy,
                countdownWarningsSeconds = ListeningPolicyService.ParseCountdownWarnings(policy.CountdownWarningsJson),
                policy.LearningReplayAllowed,
                policy.LearningEvidenceLoopEnabled,
                policy.ShortAnswerNormalisation,
                policy.ShortAnswerAcceptSynonyms,
                policy.ScreenReaderOptimised,
                policy.ShowExplanationsAfterSubmit,
                policy.ShowExplanationsOnlyIfWrong,
                policy.ShowCorrectAnswerOnReview,
                mode = normalizedMode,
                audioLockMode = audioTransport.LockMode,
                canPause = audioTransport.CanPause,
                canScrub = audioTransport.CanScrub,
                onePlayOnly = audioTransport.OnePlayOnly,
                effectiveSessionPolicy,
                // Keep the Home visual presentation label for compatibility;
                // the client must not infer fullscreen enforcement from it.
                presentationStyle = normalizedMode == "home"
                    ? "kiosk_fullscreen"
                    : normalizedMode,
            }),
            ScopeJson = isPartPractice
                ? ListeningAttemptScope.BuildPartPractice(
                    partPracticePartCode!,
                    partPracticeQuestionIds!,
                    partPracticeMinutes ?? PartPracticeMinutes(partPracticePartCode!))
                : ListeningAttemptScope.Build(normalizedMode, source.SourceKind, normalizedPathwayStage),
        };

        db.ListeningAttempts.Add(attempt);
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = now,
            ActorId = userId,
            ActorName = userId,
            Action = $"ListeningAttemptStarted_{normalizedMode}",
            ResourceType = "ListeningAttempt",
            ResourceId = attempt.Id,
            Details = $"paper={source.Id}; mode={normalizedMode}; pathwayStage={normalizedPathwayStage ?? "none"}; structure=relational",
        });
        await db.SaveChangesAsync(ct);
        // Lock the policy only after the candidate attempt is durable.
        await markingPolicyResolver.MarkUsedAsync(markingPolicy.PolicyId!, ct);
        if (scoreConversionAtStart.TableId is not null && scoreConversionAtStart.IsAvailable)
            await conversionResolver.MarkUsedAsync(scoreConversionAtStart.TableId, ct);
        return isPartPractice
            ? PartPracticeStartedDto(attempt, source, partPracticePartCode!, new Dictionary<string, string?>(), feedbackMessage)
            : RelationalAttemptDto(attempt, new Dictionary<string, string?>(), feedbackMessage);
    }

    private async Task SaveRelationalAnswerAsync(
        string userId,
        ListeningAttempt attempt,
        string questionId,
        string? userAnswer,
        CancellationToken ct)
    {
        await EnsureRelationalAttemptCanMutateAsync(attempt, ct);
        var question = await db.ListeningQuestions.AsNoTracking()
            .Where(q => q.Id == questionId && q.PaperId == attempt.PaperId)
            .Select(q => new { q.Id, q.Version, q.Part!.PartCode })
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.Validation("listening_question_not_found", "This question does not belong to the Listening attempt.");

        var partPracticeScope = ListeningAttemptScope.ReadPartPractice(attempt.ScopeJson);
        if (partPracticeScope.IsValid
            && !partPracticeScope.QuestionIds.Contains(question.Id, StringComparer.Ordinal))
        {
            throw ApiException.Validation(
                "listening_question_out_of_scope",
                "This question is outside the current Part practice attempt.");
        }

        // H10 fix: In strict/exam mode, reject answer saves for locked sections.
        if (attempt.Mode is ListeningAttemptMode.Exam or ListeningAttemptMode.Home)
        {
            var navState = ParseNavigation(attempt.NavigationStateJson);
            var currentCursor = navState is not null
                ? ListeningSectionCursorForPartCode(ListeningFsmTransitions.PartFor(navState.State))
                : ReadSectionCursor(attempt.NavigationStateJson);
            if (currentCursor >= 0)
            {
                var questionPartString = question.PartCode.ToString();
                var questionCursor = ListeningSectionCursorForPartCode(questionPartString);
                var isPartCQuestionScope = IsPartCQuestionScope(currentCursor, questionCursor);
                if (!isPartCQuestionScope && (questionCursor < 0 || currentCursor < 0 || questionCursor < currentCursor))
                {
                    throw ApiException.Validation(
                        "listening_section_locked",
                        $"Cannot modify answers in part {questionPartString} \u2014 this section is locked in the current exam mode.");
                }
                if (!isPartCQuestionScope && questionCursor > currentCursor)
                {
                    throw ApiException.Validation(
                        "listening_section_not_active",
                        $"Cannot modify answers in part {questionPartString} \u2014 this section is not active yet.");
                }
            }
        }

        var now = DateTimeOffset.UtcNow;
        var row = await db.ListeningAnswers
            .FirstOrDefaultAsync(answer => answer.ListeningAttemptId == attempt.Id && answer.ListeningQuestionId == question.Id, ct);
        if (row is null)
        {
            row = new ListeningAnswer
            {
                Id = $"laa-{Guid.NewGuid():N}",
                ListeningAttemptId = attempt.Id,
                ListeningQuestionId = question.Id,
                UserAnswerJson = JsonSerializer.Serialize(userAnswer ?? string.Empty),
                QuestionVersionSnapshot = question.Version,
                AnsweredAt = now,
            };
            db.ListeningAnswers.Add(row);
        }
        else
        {
            row.UserAnswerJson = JsonSerializer.Serialize(userAnswer ?? string.Empty);
            row.AnsweredAt = now;
            row.IsCorrect = null;
            row.PointsEarned = 0;
            row.SelectedDistractorCategory = null;
        }

        attempt.LastActivityAt = now;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw ApiException.Conflict("listening_attempt_concurrent_update",
                "This attempt was modified by another process. Please retry.");
        }
    }

    private async Task<object> SubmitRelationalAttemptAsync(
        string userId,
        ListeningAttempt attempt,
        IReadOnlyDictionary<string, string?>? finalAnswers,
        string idempotencyKey,
        CancellationToken ct)
    {
        var source = ApplyAttemptScope(await ResolveSourceAsync(attempt.PaperId, ct), attempt);
        if (source.Questions.Count == 0)
        {
            throw ApiException.Validation("listening_questions_missing", "This Listening attempt has no structured questions to grade.");
        }

        var existing = await db.Evaluations.FirstOrDefaultAsync(e => e.AttemptId == attempt.Id, ct);
        if (attempt.Status == ListeningAttemptStatus.Submitted && existing is not null)
        {
            var existingAnswers = await LoadRelationalAnswersAsync(attempt.Id, ct);
            var existingReview = BuildReview(attempt, source, existingAnswers, existing);
            return await PersistSubmitIdempotencyAsync(idempotencyKey, existingReview, ct) ?? existingReview;
        }

        MarkExpiredIfDeadlinePassed(attempt);
        var acceptsFinalAnswers = attempt.Status == ListeningAttemptStatus.InProgress;
        EnsureRelationalAttemptCanSubmit(attempt);

        // Explicit learner submit is allowed from any FSM state. One-way
        // AdvanceSection remains the only lock for section progression; the
        // candidate may still end the attempt early via Submit.

        if (acceptsFinalAnswers && finalAnswers is { Count: > 0 })
        {
            await ApplyFinalRelationalAnswersAsync(attempt, source, finalAnswers, ct);
            // Persist final learner answers before invoking the deterministic
            // grader so it never inserts duplicate answer rows for the same
            // question when it reads from the database context.
            await db.SaveChangesAsync(ct);
        }

        // The relational submit endpoint is the user-visible grading path.
        // Delegate the authoritative score and per-answer state to the same
        // deterministic grader used by the explicit V2 grade endpoint. This
        // prevents the legacy review projection from applying looser matching
        // or an independent scaled-score calculation.
        var grading = gradingService ?? new ListeningGradingService(db);
        var gradingResult = await grading.GradeAsync(attempt.Id, userId, ct);
        var answers = await LoadRelationalAnswersAsync(attempt.Id, ct);
        var answerRows = await db.ListeningAnswers
            .Where(answer => answer.ListeningAttemptId == attempt.Id)
            .ToListAsync(ct);
        var answerByQuestionId = answerRows
            .GroupBy(answer => answer.ListeningQuestionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var review = BuildReview(
            attempt,
            source,
            answers,
            evaluation: null,
            deterministicAnswers: answerByQuestionId,
            persistedConversionErrorCode: gradingResult.ScoreConversionErrorCode);

        var score = ApplyScoreConversionGate(new ListeningScoreDto(
            gradingResult.RawScore,
            gradingResult.MaxRawScore,
            gradingResult.ScaledScore,
            gradingResult.ScoreConversionGrade ?? "—",
            gradingResult.ScoreConversionPassed), review.ScoreConversionTableVersionKey);
        var evaluation = CreateEvaluation(attempt.Id, score, review);
        db.Evaluations.Add(evaluation);
        await LearnerWorkflowCoordinator.QueueStudyPlanRegenerationAsync(db, userId, ct);
        attempt.RowVersion++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            // The first concurrent submit owns the committed attempt/evaluation.
            // Rehydrate that winner and let the idempotency record serve the
            // exact same result to the losing request.
            db.ChangeTracker.Clear();
            var winningAttempt = await TryGetRelationalAttemptOwnedByUserAsync(
                userId, attempt.Id, asNoTracking: true, ct)
                ?? throw ApiException.Conflict(
                    "listening_attempt_concurrent_update",
                    "This Listening submit is still being finalized. Please retry with the same Idempotency-Key.");
            var winningSource = await ResolveSourceAsync(winningAttempt.PaperId, ct);
            var winningEvaluation = await db.Evaluations.AsNoTracking()
                .Where(e => e.AttemptId == winningAttempt.Id)
                .OrderByDescending(e => e.GeneratedAt)
                .FirstOrDefaultAsync(ct);
            if (winningAttempt.Status != ListeningAttemptStatus.Submitted || winningEvaluation is null)
            {
                throw ApiException.Conflict(
                    "listening_attempt_concurrent_update",
                    "This Listening submit is still being finalized. Please retry with the same Idempotency-Key.");
            }

            var winningAnswers = await LoadRelationalAnswersAsync(winningAttempt.Id, ct);
            var winningAnswerRows = await db.ListeningAnswers.AsNoTracking()
                .Where(answer => answer.ListeningAttemptId == winningAttempt.Id)
                .ToListAsync(ct);
            var winningAnswerByQuestionId = winningAnswerRows
                .GroupBy(answer => answer.ListeningQuestionId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
            var winningReview = BuildReview(
                winningAttempt,
                winningSource,
                winningAnswers,
                winningEvaluation,
                winningAnswerByQuestionId);
            return await PersistSubmitIdempotencyAsync(idempotencyKey, winningReview, ct) ?? winningReview;
        }
        var completedReview = BuildReview(
            attempt,
            source,
            answers,
            evaluation,
            answerByQuestionId,
            gradingResult.ScoreConversionErrorCode);
        return await PersistSubmitIdempotencyAsync(idempotencyKey, completedReview, ct) ?? completedReview;
    }

    private static void ApplyFinalLegacyAnswers(
        Attempt attempt,
        ListeningSource source,
        IReadOnlyDictionary<string, string?> finalAnswers)
    {
        var validQuestionIds = source.Questions
            .Select(q => q.Id)
            .ToHashSet(StringComparer.Ordinal);
        var unknown = finalAnswers.Keys.FirstOrDefault(id => !validQuestionIds.Contains(id));
        if (unknown is not null)
        {
            throw ApiException.Validation("listening_question_not_found", "One submitted answer does not belong to this Listening attempt.");
        }

        var answers = DeserializeAnswers(attempt.AnswersJson);
        foreach (var (questionId, answer) in finalAnswers)
        {
            answers[questionId] = answer;
        }
        attempt.AnswersJson = JsonSupport.Serialize(answers);
        attempt.LastClientSyncAt = DateTimeOffset.UtcNow;
    }

    private static IReadOnlyList<LegacyMultipleSelectionIntegrityIssue> FindMultipleSelectionMcqAnswers(
        ListeningSource source,
        IReadOnlyDictionary<string, string?> answers)
    {
        return source.Questions
            .Where(question => IsMultipleChoiceQuestionType(question.Type))
            .Select(question =>
            {
                var raw = answers.GetValueOrDefault(question.Id);
                var selections = ReadStringList(raw)
                    ?.Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .ToArray() ?? Array.Empty<string>();
                return selections.Length > 1
                    ? new LegacyMultipleSelectionIntegrityIssue(question.Id, question.Number, selections)
                    : null;
            })
            .Where(issue => issue is not null)
            .Cast<LegacyMultipleSelectionIntegrityIssue>()
            .ToArray();
    }

    private static bool IsMultipleChoiceQuestionType(string? type)
        => type?.Trim().ToLowerInvariant() is "multiple_choice_3" or "mcq" or "mcq3";

    private async Task ApplyFinalRelationalAnswersAsync(
        ListeningAttempt attempt,
        ListeningSource source,
        IReadOnlyDictionary<string, string?> finalAnswers,
        CancellationToken ct)
    {
        var validQuestionIds = source.Questions
            .Select(q => q.Id)
            .ToHashSet(StringComparer.Ordinal);
        var unknown = finalAnswers.Keys.FirstOrDefault(id => !validQuestionIds.Contains(id));
        if (unknown is not null)
        {
            var partPractice = ListeningAttemptScope.ReadPartPractice(attempt.ScopeJson);
            if (partPractice.IsValid)
            {
                // Fallback: client is posting stale GUIDs (attempt was created
                // before the paper was backfilled and GUIDs were regenerated).
                // Remap by position: old scope order (by Question.Number) →
                // new source order (by Question.Number) for the same parent part.
                var oldIdsInOrder = partPractice.QuestionIds;
                var newQuestionsSorted = source.Questions.OrderBy(q => q.Number).ToList();
                if (oldIdsInOrder.Count == newQuestionsSorted.Count && oldIdsInOrder.Count == finalAnswers.Count)
                {
                    var remapped = new Dictionary<string, string?>(StringComparer.Ordinal);
                    for (var i = 0; i < oldIdsInOrder.Count; i++)
                    {
                        var oldId = oldIdsInOrder[i];
                        if (finalAnswers.TryGetValue(oldId, out var ans))
                        {
                            remapped[newQuestionsSorted[i].Id] = ans;
                        }
                    }
                    finalAnswers = remapped;
                    validQuestionIds = source.Questions.Select(q => q.Id).ToHashSet(StringComparer.Ordinal);
                    unknown = finalAnswers.Keys.FirstOrDefault(id => !validQuestionIds.Contains(id));
                }
            }

            if (unknown is not null)
            {
                throw ApiException.Validation("listening_question_not_found", "One submitted answer does not belong to this Listening attempt.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var rows = await db.ListeningAnswers
            .Where(answer => answer.ListeningAttemptId == attempt.Id)
            .ToDictionaryAsync(answer => answer.ListeningQuestionId, StringComparer.Ordinal, ct);
        foreach (var (questionId, answer) in finalAnswers)
        {
            var questionVersion = await db.ListeningQuestions.AsNoTracking()
                .Where(q => q.Id == questionId && q.PaperId == attempt.PaperId)
                .Select(q => q.Version)
                .SingleAsync(ct);
            if (!rows.TryGetValue(questionId, out var row))
            {
                row = new ListeningAnswer
                {
                    Id = $"laa-{Guid.NewGuid():N}",
                    ListeningAttemptId = attempt.Id,
                    ListeningQuestionId = questionId,
                    UserAnswerJson = JsonSerializer.Serialize(answer ?? string.Empty),
                    QuestionVersionSnapshot = questionVersion,
                    AnsweredAt = now,
                };
                db.ListeningAnswers.Add(row);
            }
            else
            {
                row.UserAnswerJson = JsonSerializer.Serialize(answer ?? string.Empty);
                row.QuestionVersionSnapshot = questionVersion;
                row.AnsweredAt = now;
                row.IsCorrect = null;
                row.PointsEarned = 0;
                row.SelectedDistractorCategory = null;
            }
        }
    }

    private async Task<Dictionary<string, string?>> LoadRelationalAnswersAsync(string attemptId, CancellationToken ct)
    {
        var rows = await db.ListeningAnswers.AsNoTracking()
            .Where(answer => answer.ListeningAttemptId == attemptId)
            .Select(answer => new { answer.ListeningQuestionId, answer.UserAnswerJson, answer.AnsweredAt })
            .ToListAsync(ct);
        return rows
            .GroupBy(row => row.ListeningQuestionId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => DecodeRelationalAnswer(group
                    .OrderByDescending(row => row.AnsweredAt)
                    .First()
                    .UserAnswerJson),
                StringComparer.Ordinal);
    }

    private static string? DecodeRelationalAnswer(string? json)
        => ReadJsonString(json);

    private async Task<(ListeningPolicy Policy, ListeningUserPolicyOverride? UserOverride)> ResolveListeningPolicyAsync(
        string userId,
        CancellationToken ct)
    {
        var policy = listeningPolicyService is not null
            ? await listeningPolicyService.GetGlobalAsync(ct)
            : await db.ListeningPolicies.AsNoTracking().FirstOrDefaultAsync(row => row.Id == "global", ct)
                ?? new ListeningPolicy { Id = "global", FullPaperTimerMinutes = 45, GracePeriodSeconds = 10 };
        var userOverride = listeningPolicyService is not null
            ? await listeningPolicyService.GetUserOverrideAsync(userId, ct)
            : await db.ListeningUserPolicyOverrides.AsNoTracking()
                .FirstOrDefaultAsync(row => row.UserId == userId, ct);

        if (userOverride?.ExpiresAt is DateTimeOffset expiresAt && expiresAt <= DateTimeOffset.UtcNow)
            userOverride = null;

        return (policy, userOverride);
    }

    private static void EnsureAttemptsAllowed(ListeningUserPolicyOverride? userOverride)
    {
        if (userOverride?.BlockAttempts == true)
        {
            throw ApiException.Validation(
                "listening_attempts_blocked",
                userOverride.Reason ?? "Your account is blocked from starting Listening attempts.");
        }
    }

    private static int ResolveFullPaperTimerMinutes(
        ListeningPolicy policy,
        ListeningUserPolicyOverride? userOverride)
    {
        var extraPct = ResolveExtraTimeEntitlementPct(policy, userOverride);
        var baseMinutes = Math.Max(1, policy.FullPaperTimerMinutes);
        return Math.Max(1, (int)Math.Ceiling(baseMinutes * (1m + extraPct / 100m)));
    }

    private static int ResolveExtraTimeEntitlementPct(
        ListeningPolicy policy,
        ListeningUserPolicyOverride? userOverride)
        => Math.Clamp(userOverride?.ExtraTimeEntitlementPct ?? policy.DefaultExtraTimePct, 0, 100);

    private async Task EnsureAttemptEligibilityAsync(
        string userId,
        ListeningSource source,
        string normalizedMode,
        ListeningPolicy policy,
        CancellationToken ct)
    {
        // Practice is intentionally unlimited; exam-like modes consume the
        // owner-configured per-paper allowance and cooldown window.
        if (!IsExamMode(normalizedMode)
            || (policy.AttemptsPerPaperPerUser <= 0 && policy.AttemptCooldownMinutes <= 0))
            return;

        IReadOnlyList<AttemptEligibilityRow> history;
        if (source.UsesRelationalStructure)
        {
            history = await db.ListeningAttempts.AsNoTracking()
                .Where(attempt => attempt.UserId == userId
                    && attempt.PaperId == source.Id
                    && attempt.Status != ListeningAttemptStatus.Abandoned
                    && (attempt.Mode == ListeningAttemptMode.Exam
                        || attempt.Mode == ListeningAttemptMode.Home
                        || attempt.Mode == ListeningAttemptMode.Diagnostic))
                .Select(attempt => new AttemptEligibilityRow(
                    attempt.StartedAt,
                    attempt.SubmittedAt,
                    null))
                .ToListAsync(ct);
        }
        else
        {
            history = await db.Attempts.AsNoTracking()
                .Where(attempt => attempt.UserId == userId
                    && attempt.ContentId == source.Id
                    && attempt.SubtestCode == Subtest
                    && attempt.State != AttemptState.Abandoned
                    && (attempt.Mode == "exam"
                        || attempt.Mode == "home"
                        || attempt.Mode == "diagnostic"))
                .Select(attempt => new AttemptEligibilityRow(
                    attempt.StartedAt,
                    attempt.SubmittedAt,
                    attempt.CompletedAt))
                .ToListAsync(ct);
        }

        if (policy.AttemptsPerPaperPerUser > 0
            && history.Count >= policy.AttemptsPerPaperPerUser)
        {
            throw ApiException.Conflict(
                "listening_attempt_cap_reached",
                $"You have reached the attempt cap ({policy.AttemptsPerPaperPerUser}) for this paper.");
        }

        if (policy.AttemptCooldownMinutes > 0 && history.Count > 0)
        {
            var last = history
                .OrderByDescending(attempt => attempt.SubmittedAt ?? attempt.CompletedAt ?? attempt.StartedAt)
                .First();
            var lastAt = last.SubmittedAt ?? last.CompletedAt ?? last.StartedAt;
            var elapsed = DateTimeOffset.UtcNow - lastAt;
            if (elapsed.TotalMinutes < policy.AttemptCooldownMinutes)
            {
                var remaining = policy.AttemptCooldownMinutes - Math.Floor(elapsed.TotalMinutes);
                throw ApiException.Conflict(
                    "listening_attempt_cooldown",
                    $"Please wait {remaining} minute(s) before retrying this Listening paper.");
            }
        }
    }

    private static bool HasAllRequiredAudioAssets(ListeningSource source)
    {
        // A legacy combined asset is the authoritative source for every cue
        // window, so it satisfies the complete-paper requirement.
        if (!string.IsNullOrWhiteSpace(source.AudioUrl)) return true;

        var requiredSections = source.Questions
            .Select(question => NormalizePartCode(question.PartCode))
            .Concat(source.Extracts.Select(extract => NormalizePartCode(extract.PartCode)))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!.StartsWith('B') ? "B" : code)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (requiredSections.Count == 0 || source.AudioUrlByPart is null) return false;

        return requiredSections.All(section =>
            HasAudioForSection(source.AudioUrlByPart, section));
    }

    private static bool HasAudioForSection(
        IReadOnlyDictionary<string, string> audioByPart,
        string section)
        => !string.IsNullOrWhiteSpace(ResolveUploadedAudioForSection(audioByPart, section));

    private sealed record AttemptEligibilityRow(
        DateTimeOffset StartedAt,
        DateTimeOffset? SubmittedAt,
        DateTimeOffset? CompletedAt);

    private async Task<ListeningAttempt?> TryGetRelationalAttemptOwnedByUserAsync(
        string userId,
        string attemptId,
        bool asNoTracking,
        CancellationToken ct)
    {
        var query = db.ListeningAttempts.Where(a => a.Id == attemptId && a.UserId == userId);
        if (asNoTracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(ct);
    }

    private async Task EnsureRelationalAttemptCanMutateAsync(ListeningAttempt attempt, CancellationToken ct)
    {
        EnsureAttemptNotOnAdminReviewHold(attempt.RequiresAdminReview, attempt.AdminReviewReason);
        if (attempt.Status != ListeningAttemptStatus.InProgress)
        {
            throw ApiException.Validation(
                "listening_attempt_locked",
                "This Listening attempt is already submitted or expired and can no longer be changed.");
        }
        var now = DateTimeOffset.UtcNow;
        if (attempt.DeadlineAt is DateTimeOffset deadline && now > deadline)
        {
            attempt.Status = ListeningAttemptStatus.Expired;
            attempt.LastActivityAt = now;
            await db.SaveChangesAsync(ct);
            throw ApiException.Validation(
                "listening_attempt_deadline_passed",
                "This Listening attempt deadline has passed and answers can no longer be changed.");
        }
    }

    private static bool MarkExpiredIfDeadlinePassed(ListeningAttempt attempt)
    {
        if (attempt.Status != ListeningAttemptStatus.InProgress) return false;

        var now = DateTimeOffset.UtcNow;
        if (attempt.DeadlineAt is not DateTimeOffset deadline || now <= deadline) return false;

        attempt.Status = ListeningAttemptStatus.Expired;
        attempt.SubmittedAt = now;
        attempt.LastActivityAt = now;
        return true;
    }

    private static void EnsureRelationalAttemptCanSubmit(ListeningAttempt attempt)
    {
        EnsureAttemptNotOnAdminReviewHold(attempt.RequiresAdminReview, attempt.AdminReviewReason);
        if (attempt.Status == ListeningAttemptStatus.Expired && attempt.DeadlineAt.HasValue)
        {
            return;
        }

        if (attempt.Status != ListeningAttemptStatus.InProgress)
        {
            throw ApiException.Validation(
                "listening_attempt_locked",
                "This Listening attempt is already submitted or expired and can no longer be changed.");
        }
    }

    /// <summary>Deserialise FSM navigation state for B5/H10 gating.
    /// Returns null if the JSON is missing, empty, or malformed.</summary>
    private static NavigationState? ParseNavigation(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var state = JsonSerializer.Deserialize<NavigationState>(json);
            return state is null || string.IsNullOrWhiteSpace(state.State) || state.Locks is null
                ? null
                : state;
        }
        catch { return null; }
    }

    private static ListeningAttemptMode ToRelationalMode(string mode) => mode switch
    {
        "home" => ListeningAttemptMode.Home,
        // Paper mode is a retained historical enum value only; NormalizeMode
        // rejects new requests before this mapper can be reached.
        "paper" => ListeningAttemptMode.Exam,
        "diagnostic" => ListeningAttemptMode.Diagnostic,
        "practice" => ListeningAttemptMode.Learning,
        _ => ListeningAttemptMode.Exam,
    };

    private static string ToApiMode(ListeningAttemptMode mode) => mode switch
    {
        ListeningAttemptMode.Home => "home",
        ListeningAttemptMode.Paper => "exam",
        ListeningAttemptMode.Learning => "practice",
        ListeningAttemptMode.Drill => "practice",
        ListeningAttemptMode.MiniTest => "practice",
        ListeningAttemptMode.ErrorBank => "practice",
        ListeningAttemptMode.Diagnostic => "diagnostic",
        _ => "exam",
    };

    private static ListeningDistractorCategory? ResolveSelectedDistractorCategory(ListeningReviewItemDto item)
    {
        if (item.IsCorrect || item.OptionAnalysis is null || string.IsNullOrWhiteSpace(item.LearnerAnswer)) return null;
        var selected = item.OptionAnalysis.FirstOrDefault(option =>
            string.Equals(option.OptionText, item.LearnerAnswer, StringComparison.OrdinalIgnoreCase)
            || string.Equals(option.OptionLabel, item.LearnerAnswer, StringComparison.OrdinalIgnoreCase));
        return selected?.DistractorCategory switch
        {
            "too_strong" => ListeningDistractorCategory.TooStrong,
            "too_weak" => ListeningDistractorCategory.TooWeak,
            "wrong_speaker" => ListeningDistractorCategory.WrongSpeaker,
            "opposite_meaning" => ListeningDistractorCategory.OppositeMeaning,
            "reused_keyword" => ListeningDistractorCategory.ReusedKeyword,
            "out_of_scope" => ListeningDistractorCategory.OutOfScope,
            _ => null,
        };
    }

    private static Evaluation CreateEvaluation(string attemptId, ListeningScoreDto score, ListeningReviewDto review)
        => new()
        {
            Id = $"le-{Guid.NewGuid():N}",
            AttemptId = attemptId,
            SubtestCode = Subtest,
            RawScore = score.RawScore,
            MaxRawScore = score.MaxRawScore,
            ScaledScore = score.ScaledScore,
            ScoreConversionTableVersionKey = review.ScoreConversionTableVersionKey,
            ScoreConversionGrade = score.Grade == "—" ? null : score.Grade,
            ScoreConversionPassed = score.Passed,
            State = AsyncState.Completed,
            ScoreRange = FormatScoreDisplay(score),
            GradeRange = $"Grade {score.Grade}",
            ConfidenceBand = ConfidenceBand.High,
            StrengthsJson = JsonSupport.Serialize(review.Strengths),
            IssuesJson = JsonSupport.Serialize(review.Issues),
            CriterionScoresJson = JsonSupport.Serialize(new[]
            {
                new
                {
                    criterionCode = "listening_accuracy",
                    rawScore = score.RawScore,
                    maxRawScore = score.MaxRawScore,
                    scaledScore = score.ScaledScore,
                    grade = score.Grade,
                    passed = score.Passed,
                    scoreDisplay = FormatScoreDisplay(score)
                }
            }),
            FeedbackItemsJson = JsonSupport.Serialize(review.ItemReview
                .Where(item => !item.IsCorrect && !item.IsInvalid)
                .Select(item => new
                {
                    feedbackItemId = $"{attemptId}-{item.QuestionId}",
                    criterionCode = item.ErrorType ?? "detail_capture",
                    type = "answer_feedback",
                    anchor = new { questionId = item.QuestionId },
                    message = item.Explanation,
                    severity = "medium",
                    suggestedFix = item.DistractorExplanation ?? "Review the transcript evidence and repeat the same error type as a short drill."
                })),
            GeneratedAt = DateTimeOffset.UtcNow,
            ModelExplanationSafe = "Listening result is graded deterministically from the authored answer key.",
            LearnerDisclaimer = "Practice result only. This is not an official OET Statement of Results.",
            StatusReasonCode = "completed",
            StatusMessage = "Result ready.",
            LastTransitionAt = DateTimeOffset.UtcNow
        };

    private async Task<ListeningSource> ResolveSourceAsync(string id, CancellationToken ct)
    {
        var paper = await db.ContentPapers.AsNoTracking()
            .Include(p => p.Assets.Where(a => a.IsPrimary))
                .ThenInclude(a => a.MediaAsset)
            .FirstOrDefaultAsync(p => p.Id == id && p.SubtestCode == Subtest && p.Status == ContentStatus.Published, ct);
        if (paper is not null)
        {
            return await BuildPaperSourceAsync(paper, ct);
        }

        var legacy = await db.ContentItems.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.SubtestCode == Subtest && x.Status == ContentStatus.Published, ct)
            ?? throw ApiException.NotFound("listening_paper_not_found", "Listening paper not found.");
        return BuildLegacySource(legacy);
    }

    private async Task RequirePaperAccessIfAuthoredAsync(string userId, string paperId, CancellationToken ct)
    {
        var paper = await db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paperId && p.SubtestCode == Subtest, ct);
        if (paper is null)
        {
            return;
        }

        if (paper.Status != ContentStatus.Published || !paper.CandidateVisible)
        {
            // Hidden test/demo/staging papers are indistinguishable from
            // missing ones: direct URLs and API calls get a 404, never the
            // content.
            throw ApiException.NotFound("listening_paper_not_found", "Listening paper not found.");
        }

        if (!await CanLearnerSeePaperAsync(userId, paper, ct))
        {
            throw ApiException.NotFound("listening_paper_not_found", "Listening paper not found.");
        }

        await entitlements.RequireAccessAsync(userId, paper, ct);
    }

    private async Task<bool> CanLearnerSeePaperAsync(string userId, ContentPaper paper, CancellationToken ct)
    {
        if (paper.AppliesToAllProfessions)
        {
            return true;
        }

        var profession = await GetLearnerProfessionAsync(userId, ct);
        return !string.IsNullOrWhiteSpace(profession)
            && string.Equals(paper.ProfessionId, profession, StringComparison.OrdinalIgnoreCase);
    }

    private Task<string?> GetLearnerProfessionAsync(string userId, CancellationToken ct)
        => db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.ActiveProfessionId)
            .SingleOrDefaultAsync(ct);

    private async Task<ListeningSource> BuildPaperSourceAsync(ContentPaper paper, CancellationToken ct)
    {
        var assets = paper.Assets.Where(a => a.IsPrimary).ToList();
        var assetByRole = assets
            .GroupBy(a => a.Role)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.DisplayOrder).First());
        // A paper-level audio URL is only valid for an unscoped Audio asset.
        // Never expose the first per-section upload (for example Part A) as a
        // full-paper fallback: doing so makes every section play the wrong file
        // when the paper has separate A/B/C assets.
        var fullAudioAsset = assets
            .Where(a => a.Role == PaperAssetRole.Audio
                && a.MediaAsset is not null
                && string.IsNullOrWhiteSpace(a.Part))
            .OrderBy(a => a.DisplayOrder)
            .FirstOrDefault();

        // Per-sub-section uploaded-audio map: at most one primary Audio asset
        // per part code (A1..C2). Mirrors ReadingLearnerEndpoints' per-Part
        // questionPaperAssets. Keyed case-insensitively because the Part column
        // is author-entered free text. Served at /v1/media/{id}/content.
        var audioByPart = assets
            .Where(a => a.Role == PaperAssetRole.Audio
                && a.MediaAsset is not null
                && !string.IsNullOrWhiteSpace(a.Part))
            .GroupBy(a => a.Part!.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => $"/v1/media/{g.OrderBy(a => a.DisplayOrder).First().MediaAsset!.Id}/content",
                StringComparer.Ordinal);

        // Per-part learner-facing QuestionPaper PDFs (Part A/B/C plus optional
        // per-section overrides A1/A2, B1..B6, C1/C2), mirroring audioByPart.
        // Keyed case-insensitively because Part is author-entered free text.
        var questionPaperByPart = assets
            .Where(a => a.Role == PaperAssetRole.QuestionPaper
                && a.MediaAsset is not null
                && !string.IsNullOrWhiteSpace(a.Part))
            .GroupBy(a => a.Part!.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => $"/v1/media/{g.OrderBy(a => a.DisplayOrder).First().MediaAsset!.Id}/content",
                StringComparer.Ordinal);

        var relationalQuestions = await db.ListeningQuestions.AsNoTracking()
            .Include(q => q.Part)
            .Include(q => q.Options)
            .Where(q => q.PaperId == paper.Id)
            .OrderBy(q => q.QuestionNumber)
            .ToListAsync(ct);

        // The relational table is the preferred source for authored metadata,
        // but older imports can contain only a partial Part B/C projection while
        // ExtractedTextJson still has the complete source question set. Parse the
        // JSON once and merge by the authoritative question number below; never
        // let the mere presence of one relational row hide the remaining source
        // questions from the learner.
        var questionMap = JsonSupport.Deserialize<Dictionary<string, object?>>(
            paper.ExtractedTextJson,
            new Dictionary<string, object?>());
        var jsonQuestions = ExtractQuestions(
                questionMap.TryGetValue("listeningQuestions", out var listeningQuestions)
                    ? listeningQuestions
                    : questionMap.GetValueOrDefault("questions"))
            .ToList();

        IReadOnlyList<ListeningQuestion> questions;
        IReadOnlyList<ListeningTranscriptSegmentDto> segments;
        IReadOnlyList<ListeningExtractMetaDto> extracts;
        var usesRelationalStructure = relationalQuestions.Count > 0;

        if (usesRelationalStructure)
        {
            var partRows = await db.ListeningParts.AsNoTracking()
                .Where(part => part.PaperId == paper.Id)
                .ToListAsync(ct);
            var parts = partRows.ToDictionary(part => part.Id, part => part.PartCode);
            // Per-sub-section countdown sourced from ListeningPart.TimeLimitSeconds.
            var timeLimitByPartCode = partRows
                .Where(part => part.TimeLimitSeconds is > 0)
                .GroupBy(part => part.PartCode)
                .ToDictionary(g => g.Key, g => g.First().TimeLimitSeconds);
            var partIds = parts.Keys.ToList();
            var relationalExtracts = partIds.Count == 0
                ? new List<ListeningExtract>()
                : await db.ListeningExtracts.AsNoTracking()
                    .Where(extract => partIds.Contains(extract.ListeningPartId))
                    .OrderBy(extract => extract.DisplayOrder)
                    .ToListAsync(ct);

            questions = MergeRelationalAndJsonQuestions(
                relationalQuestions.Select(MapRelationalQuestion).ToList(),
                jsonQuestions);
            extracts = relationalExtracts
                .Select((extract, index) =>
                {
                    var code = parts.GetValueOrDefault(extract.ListeningPartId);
                    var codeString = PartCodeString(code);
                    return MapRelationalExtract(
                        extract,
                        code,
                        index,
                        ResolveSubSectionAudioUrl(audioByPart, codeString, extract.AudioContentSha),
                        timeLimitByPartCode.GetValueOrDefault(code));
                })
                .OrderBy(extract => PartCodeOrder(extract.PartCode))
                .ThenBy(extract => extract.DisplayOrder)
                .ToList();
            segments = relationalExtracts
                .SelectMany(extract => ExtractTranscriptSegmentsFromJson(
                    extract.TranscriptSegmentsJson,
                    PartCodeString(parts.GetValueOrDefault(extract.ListeningPartId))))
                .OrderBy(segment => segment.StartMs)
                .ToList();

            // A few early content-paper imports created relational questions
            // before their extract rows. Keep the normalized JSON metadata as a
            // fallback for those papers so B/C audio and extract context are not
            // silently discarded along with the question fallback.
            if (relationalExtracts.Count == 0)
            {
                segments = ExtractTranscriptSegments(questionMap.GetValueOrDefault("listeningTranscriptSegments"));
                extracts = ExtractExtractMetadata(questionMap.GetValueOrDefault("listeningExtracts"), audioByPart);
            }
        }
        else
        {
            questions = jsonQuestions;
            segments = ExtractTranscriptSegments(questionMap.GetValueOrDefault("listeningTranscriptSegments"));
            extracts = ExtractExtractMetadata(questionMap.GetValueOrDefault("listeningExtracts"), audioByPart);
        }

        // Per-section audio URLs keyed by the five learner-facing sections the
        // exam player navigates (A1, A2, B, C1, C2 — see LISTENING_SECTION_SEQUENCE).
        // Part B collapses its six question sub-parts to a single section, so it
        // plays one uploaded "B" file (a legacy "B1" upload is accepted as a
        // fallback); every other section plays its own file. Uploaded asset wins,
        // else the representative extract's resolved URL (TTS fallback), else absent.
        var audioUrlBySection = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var section in LearnerAudioSections)
        {
            var uploaded = ResolveUploadedAudioForSection(audioByPart, section);
            var url = uploaded
                ?? extracts.FirstOrDefault(e => SectionForPartCode(e.PartCode) == section)?.AudioUrl;
            if (!string.IsNullOrWhiteSpace(url)) audioUrlBySection[section] = url;
        }

        return new ListeningSource(
            Id: paper.Id,
            SourceKind: "content_paper",
            Title: paper.Title,
            Slug: paper.Slug,
            Difficulty: paper.Difficulty,
            EstimatedDurationMinutes: paper.EstimatedDurationMinutes,
            ScenarioType: "oet_listening",
            AudioUrl: AssetDownloadPath(fullAudioAsset),
            QuestionPaperUrl: AssetDownloadPath(assetByRole.GetValueOrDefault(PaperAssetRole.QuestionPaper)),
            AnswerKeyUrl: AssetDownloadPath(assetByRole.GetValueOrDefault(PaperAssetRole.AnswerKey)),
            AudioScriptUrl: AssetDownloadPath(assetByRole.GetValueOrDefault(PaperAssetRole.AudioScript)),
            Questions: questions,
            AssetReadiness: new ListeningAssetReadiness(
                Audio: assetByRole.ContainsKey(PaperAssetRole.Audio),
                QuestionPaper: assetByRole.ContainsKey(PaperAssetRole.QuestionPaper),
                AnswerKey: assetByRole.ContainsKey(PaperAssetRole.AnswerKey),
                AudioScript: assetByRole.ContainsKey(PaperAssetRole.AudioScript)),
            TranscriptSegments: segments,
            Extracts: extracts,
            UsesRelationalStructure: usesRelationalStructure,
            QuestionPaperUrlByPart: questionPaperByPart,
            AudioUrlByPart: audioUrlBySection,
            PaperRevisionId: paper.PublishedRevisionId);
    }

    private static ListeningSource BuildLegacySource(ContentItem item)
    {
        var detail = JsonSupport.Deserialize<Dictionary<string, object?>>(item.DetailJson, new Dictionary<string, object?>());
        var questions = ExtractQuestions(detail.GetValueOrDefault("questions")).ToList();
        var segments = ExtractTranscriptSegments(detail.GetValueOrDefault("listeningTranscriptSegments"));
        var extracts = ExtractExtractMetadata(detail.GetValueOrDefault("listeningExtracts"));
        return new ListeningSource(
            Id: item.Id,
            SourceKind: "legacy_content_item",
            Title: item.Title,
            Slug: item.Id,
            Difficulty: item.Difficulty,
            EstimatedDurationMinutes: item.EstimatedDurationMinutes,
            ScenarioType: item.ScenarioType ?? "consultation",
            AudioUrl: ReadString(detail.GetValueOrDefault("audioUrl")),
            QuestionPaperUrl: null,
            AnswerKeyUrl: null,
            AudioScriptUrl: null,
            Questions: questions,
            AssetReadiness: new ListeningAssetReadiness(
                Audio: !string.IsNullOrWhiteSpace(ReadString(detail.GetValueOrDefault("audioUrl"))),
                QuestionPaper: false,
                AnswerKey: true,
                AudioScript: questions.Any(q => !string.IsNullOrWhiteSpace(q.TranscriptExcerpt))),
            TranscriptSegments: segments,
            Extracts: extracts,
            UsesRelationalStructure: false);
    }

    /// <summary>
    /// Phase 5: parse a paper-level transcript-segments array from the
    /// authored JSON. Defensive: any malformed payload yields an empty list
    /// rather than poisoning the review response.
    /// </summary>
    private static IReadOnlyList<ListeningTranscriptSegmentDto> ExtractTranscriptSegments(object? raw)
    {
        if (raw is null) return [];
        try
        {
            var list = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(
                System.Text.Json.JsonSerializer.Serialize(raw), new List<Dictionary<string, object?>>());
            var output = new List<ListeningTranscriptSegmentDto>(list.Count);
            foreach (var seg in list)
            {
                var startMs = ReadIntField(seg, "startMs");
                var endMs = ReadIntField(seg, "endMs");
                var text = ReadString(seg.GetValueOrDefault("text")) ?? string.Empty;
                if (startMs < 0 || endMs < startMs || string.IsNullOrWhiteSpace(text)) continue;
                output.Add(new ListeningTranscriptSegmentDto(
                    StartMs: startMs,
                    EndMs: endMs,
                    PartCode: ReadString(seg.GetValueOrDefault("partCode")),
                    SpeakerId: ReadString(seg.GetValueOrDefault("speakerId")),
                    Text: text));
            }
            return output;
        }
        catch
        {
            return [];
        }
    }

    private static int ReadIntField(Dictionary<string, object?> map, string key)
    {
        var raw = map.GetValueOrDefault(key);
        if (raw is null) return -1;
        if (raw is int i) return i;
        if (raw is long l) return (int)l;
        if (raw is double d) return (int)d;
        return int.TryParse(raw.ToString(), out var v) ? v : -1;
    }

    /// <summary>
    /// Phase 5 tail: parse paper-level extract metadata
    /// (<c>listeningExtracts</c>) into typed DTOs. One row per extract:
    ///   A1, A2, B (one per workplace clip), C1, C2.
    /// Each row carries accent + speakers + audio window + extract kind/title.
    /// Defensive: any malformed payload yields an empty list rather than
    /// poisoning the session/review response.
    /// </summary>
    private static IReadOnlyList<ListeningExtractMetaDto> ExtractExtractMetadata(
        object? raw,
        IReadOnlyDictionary<string, string>? audioByPart = null)
    {
        if (raw is null) return [];
        audioByPart ??= new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var list = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(
                System.Text.Json.JsonSerializer.Serialize(raw), new List<Dictionary<string, object?>>());
            if (list.Count == 0) return [];
            var output = new List<ListeningExtractMetaDto>(list.Count);
            for (var i = 0; i < list.Count; i++)
            {
                var seg = list[i];
                var partCode = NormalizePartCode(ReadString(seg.GetValueOrDefault("partCode")));
                if (partCode is null) continue;
                var kind = NormalizeExtractKind(ReadString(seg.GetValueOrDefault("kind")), partCode);
                var title = ReadString(seg.GetValueOrDefault("title")) ?? $"Extract {i + 1}";
                var accentCode = ReadString(seg.GetValueOrDefault("accentCode"));
                var displayOrder = ReadIntField(seg, "displayOrder");
                if (displayOrder < 0) displayOrder = i;
                int? audioStartMs = ReadIntField(seg, "audioStartMs") is var s and >= 0 ? s : null;
                int? audioEndMs = ReadIntField(seg, "audioEndMs") is var e and >= 0 ? e : null;
                if (audioStartMs is int sv && audioEndMs is int ev && ev < sv)
                {
                    audioEndMs = null;
                }
                int? timeLimitSeconds = ReadIntField(seg, "timeLimitSeconds") is var t and > 0 ? t : null;
                var speakers = ParseSpeakers(seg.GetValueOrDefault("speakers"));
                // Part A note-completion body. Only A1/A2 surface one; ignore any
                // stray value on a Part B/C JSON extract.
                var isPartA = partCode.StartsWith("A", StringComparison.OrdinalIgnoreCase);
                var notesBody = isPartA ? ReadString(seg.GetValueOrDefault("notesBody")) : null;
                var authoringMethod = isPartA ? ReadString(seg.GetValueOrDefault("authoringMethod")) : null;
                var overlayBlanks = isPartA ? ReadString(seg.GetValueOrDefault("partAOverlayBlanksJson")) : null;
                // Part B/C scenario line — not gated Part-A-only (unlike notesBody).
                var contextIntro = ReadString(seg.GetValueOrDefault("contextIntro"));
                // JSON-path papers have no TTS extract sha here, so only uploaded
                // per-part Audio assets resolve; parent-part uploads (A/C) and
                // legacy B1..B6 uploads are accepted through the same resolver.
                var audioUrl = ResolveUploadedAudioForSection(audioByPart, partCode);
                output.Add(new ListeningExtractMetaDto(
                    PartCode: partCode,
                    DisplayOrder: displayOrder,
                    Kind: kind,
                    Title: title,
                    AccentCode: accentCode,
                    Speakers: speakers,
                    AudioStartMs: audioStartMs,
                    AudioEndMs: audioEndMs,
                    AudioUrl: audioUrl,
                    TimeLimitSeconds: timeLimitSeconds,
                    NotesBody: notesBody,
                    AuthoringMethod: authoringMethod,
                    PartAOverlayBlanksJson: overlayBlanks,
                    ContextIntro: contextIntro));
            }
            return output
                .OrderBy(e => PartCodeOrder(e.PartCode))
                .ThenBy(e => e.DisplayOrder)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<ListeningSpeakerDto> ParseSpeakers(object? raw)
    {
        if (raw is null) return [];
        try
        {
            var list = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(
                System.Text.Json.JsonSerializer.Serialize(raw), new List<Dictionary<string, object?>>());
            var output = new List<ListeningSpeakerDto>(list.Count);
            for (var i = 0; i < list.Count; i++)
            {
                var s = list[i];
                var id = ReadString(s.GetValueOrDefault("id")) ?? $"s{i + 1}";
                var role = ReadString(s.GetValueOrDefault("role")) ?? "speaker";
                var gender = NormalizeGender(ReadString(s.GetValueOrDefault("gender")));
                var accent = ReadString(s.GetValueOrDefault("accent"));
                output.Add(new ListeningSpeakerDto(id, role, gender, accent));
            }
            return output;
        }
        catch
        {
            return [];
        }
    }

    private static string? NormalizePartCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var normalized = raw.Trim().ToUpperInvariant();
        return normalized switch
        {
            "A1" or "A2"
                or "B1" or "B2" or "B3" or "B4" or "B5" or "B6"
                or "C1" or "C2" => normalized,
            "A" => "A1",
            // Legacy bare-"B" extract metadata floors to the first Part B
            // sub-section so a not-yet-split paper still surfaces something.
            "B" => "B1",
            "C" => "C1",
            _ => null,
        };
    }

    private static int PartCodeOrder(string partCode) => partCode switch
    {
        "A1" => 1,
        "A2" => 2,
        "B1" => 3,
        "B2" => 4,
        "B3" => 5,
        "B4" => 6,
        "B5" => 7,
        "B6" => 8,
        "C1" => 9,
        "C2" => 10,
        _ => 99,
    };

    private static int ListeningSectionCursorForPartCode(string? raw)
    {
        var normalized = NormalizePartCode(raw);
        return normalized switch
        {
            "A1" => 0,
            "A2" => 1,
            "B1" or "B2" or "B3" or "B4" or "B5" or "B6" => 2,
            "C1" => 3,
            "C2" => 4,
            _ => -1,
        };
    }

    private static bool IsPartCQuestionScope(int currentCursor, int questionCursor)
    {
        // Part C audio remains one-way (C1 before C2), while Q31–Q42 share one
        // candidate workspace once Part C is active. This lets a candidate
        // review or correct either extract without reopening or restarting audio.
        return (currentCursor is 3 or 4) && (questionCursor is 3 or 4);
    }

    private static string NormalizeExtractKind(string? raw, string partCode)
    {
        var normalized = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "consultation" or "workplace" or "presentation") return normalized;
        return partCode switch
        {
            "B1" or "B2" or "B3" or "B4" or "B5" or "B6" => "workplace",
            "C1" or "C2" => "presentation",
            _ => "consultation",
        };
    }

    private static string? NormalizeGender(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var normalized = raw.Trim().ToLowerInvariant();
        return normalized is "m" or "f" or "nb" ? normalized : null;
    }

    private static ListeningQuestion MapRelationalQuestion(OetLearner.Api.Domain.ListeningQuestion question)
    {
        var resolvedPartCode = ResolveQuestionPartCode(
            question.Part is null ? null : PartCodeString(question.Part.PartCode),
            question.QuestionNumber);
        var options = question.Options
            .OrderBy(option => option.DisplayOrder)
            .ToList();
        var optionTexts = options.Select(option => CleanListeningOption(option.Text)).ToList();
        var correctOption = options.FirstOrDefault(option => option.IsCorrect);
        var rawCorrect = ReadJsonString(question.CorrectAnswerJson) ?? string.Empty;
        var correctDisplay = correctOption?.Text ?? rawCorrect;
        var accepted = ReadJsonStringList(question.AcceptedSynonymsJson).ToList();
        AddAccepted(accepted, rawCorrect);
        if (correctOption is not null)
        {
            AddAccepted(accepted, correctOption.OptionKey);
            AddAccepted(accepted, correctOption.Text);
        }

        return new ListeningQuestion(
            Id: question.Id,
            Number: question.QuestionNumber,
            // A handful of early imports left the part relationship empty or
            // stored a bare parent code. Resolve those rows from the canonical
            // OET question-number ranges so they cannot silently fall into
            // Part A and disappear from the learner's B/C grouping.
            PartCode: resolvedPartCode,
            // Part B/C learner stems are fail-closed: metadata headings and
            // generic fallback copy must never be rendered as if they were a
            // real question when the source manifest is unavailable. The
            // source JSON merge below can still supply the exact authored stem.
            Text: IsPartBCCode(resolvedPartCode)
                ? SelectQuestionPrompt(question.Stem, question.Stem, resolvedPartCode)
                : CleanListeningPrompt(question.Stem),
            // FillInBlank surfaces to the learner as a text-input gap-fill —
            // identical wire type to ShortAnswer so the answer never leaks via
            // option text and the player renders a free-text box.
            Type: question.QuestionType == ListeningQuestionType.MultipleChoice3 ? "multiple_choice_3" : "short_answer",
            Options: optionTexts,
            CorrectAnswer: correctDisplay,
            AcceptedAnswers: accepted,
            Explanation: question.ExplanationMarkdown,
            SkillTag: question.SkillTag,
            AllowTranscriptReveal: true,
            TranscriptExcerpt: question.TranscriptEvidenceText,
            DistractorExplanation: null,
            Points: question.Points,
            OptionDistractorWhy: options.Select(option => option.WhyWrongMarkdown).ToList(),
            OptionDistractorCategory: options.Select(option => option.DistractorCategory is null ? null : DistractorCategoryString(option.DistractorCategory.Value)).ToList(),
            SpeakerAttitude: question.SpeakerAttitude is null ? null : SpeakerAttitudeString(question.SpeakerAttitude.Value),
            TranscriptEvidenceStartMs: question.TranscriptEvidenceStartMs,
            TranscriptEvidenceEndMs: question.TranscriptEvidenceEndMs);
    }

    /// <summary>
    /// Reconcile the normalized relational graph with the source question
    /// manifest. Relational rows retain their stable IDs (so existing answers
    /// continue to resolve), while missing/placeholder Part B/C stems and
    /// option sets are filled from the same paper's JSON source by question
    /// number. JSON-only B/C rows are appended when an import omitted them from
    /// the relational table. No text is invented here: an unavailable source
    /// remains empty and is rejected by the publish validator.
    /// </summary>
    private static IReadOnlyList<ListeningQuestion> MergeRelationalAndJsonQuestions(
        IReadOnlyList<ListeningQuestion> relational,
        IReadOnlyList<ListeningQuestion> json)
    {
        if (relational.Count == 0) return json.OrderBy(question => question.Number).ToList();
        if (json.Count == 0) return relational.OrderBy(question => question.Number).ToList();

        var jsonByNumber = json
            .Where(question => IsPartBCCode(question.PartCode))
            .GroupBy(question => question.Number)
            .ToDictionary(group => group.Key, group => group.First());
        var representedNumbers = new HashSet<int>();
        var merged = new List<ListeningQuestion>(relational.Count + jsonByNumber.Count);

        foreach (var relationalQuestion in relational)
        {
            // Only a relational B/C row represents a source B/C number. A
            // malformed legacy Part A row can share a number with the source
            // manifest; allowing that row to suppress the source item would
            // recreate the missing-question defect.
            var isRelationalPartBC = IsPartBCCode(relationalQuestion.PartCode);
            if (isRelationalPartBC)
            {
                representedNumbers.Add(relationalQuestion.Number);
            }

            if (!isRelationalPartBC
                || !jsonByNumber.TryGetValue(relationalQuestion.Number, out var sourceQuestion))
            {
                merged.Add(relationalQuestion);
                continue;
            }

            var mergedQuestion = relationalQuestion;
            // The source manifest is the authority for learner-facing Part B/C
            // stems. A relational row can contain a syntactically valid but
            // stale/repeated heading (the production bug this guard addresses),
            // so do not preserve it merely because it passes the generic stem
            // validator. Keep the relational text only when the source has no
            // usable stem at all; the publish gate will reject that paper.
            if (IsUsablePartBCStem(sourceQuestion.Text)
                && !string.Equals(
                    SanitizeQuestionPrompt(mergedQuestion.Text),
                    SanitizeQuestionPrompt(sourceQuestion.Text),
                    StringComparison.Ordinal))
            {
                mergedQuestion = mergedQuestion with { Text = sourceQuestion.Text };
            }

            if (!HasUsableMcqOptions(mergedQuestion.Options)
                && HasUsableMcqOptions(sourceQuestion.Options))
            {
                mergedQuestion = mergedQuestion with
                {
                    Options = sourceQuestion.Options,
                    CorrectAnswer = sourceQuestion.CorrectAnswer,
                    AcceptedAnswers = sourceQuestion.AcceptedAnswers,
                    OptionDistractorWhy = sourceQuestion.OptionDistractorWhy,
                    OptionDistractorCategory = sourceQuestion.OptionDistractorCategory,
                };
            }
            else if (string.IsNullOrWhiteSpace(mergedQuestion.CorrectAnswer)
                && !string.IsNullOrWhiteSpace(sourceQuestion.CorrectAnswer))
            {
                mergedQuestion = mergedQuestion with
                {
                    CorrectAnswer = sourceQuestion.CorrectAnswer,
                    AcceptedAnswers = sourceQuestion.AcceptedAnswers,
                };
            }

            merged.Add(mergedQuestion);
        }

        // Preserve complete source questions that were never imported into the
        // relational table (the historical cause of Full Exam Part B showing a
        // single item). Only B/C rows are eligible: relational A rows remain the
        // canonical note-completion structure for Part A.
        foreach (var sourceQuestion in jsonByNumber.Values)
        {
            if (!representedNumbers.Contains(sourceQuestion.Number)) merged.Add(sourceQuestion);
        }

        return merged
            .OrderBy(question => question.Number)
            .ThenBy(question => question.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsPartBCCode(string? partCode)
    {
        var normalized = (partCode ?? string.Empty).Trim().ToUpperInvariant();
        return normalized.StartsWith('B') || normalized.StartsWith('C');
    }

    /// <summary>
    /// Resolve a learner question's canonical sub-section. The explicit
    /// A1/A2/B1..B6/C1/C2 code always wins. Legacy parent-only values and
    /// missing values are normalized from the authoritative OET number ranges
    /// (B=25..30, C1=31..36, C2=37..42). This is structural mapping only; it
    /// never supplies question text or answer content.
    /// </summary>
    public static string ResolveQuestionPartCode(string? rawPartCode, int questionNumber)
    {
        var normalized = (rawPartCode ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized is "A1" or "A2"
            or "B1" or "B2" or "B3" or "B4" or "B5" or "B6"
            or "C1" or "C2")
        {
            return normalized;
        }

        if (normalized == "B" && questionNumber is >= 25 and <= 30)
        {
            return $"B{questionNumber - 24}";
        }

        if (normalized == "C")
        {
            return questionNumber is >= 37 and <= 42 ? "C2" : "C1";
        }

        if (normalized == "A")
        {
            return questionNumber is >= 13 and <= 24 ? "A2" : "A1";
        }

        return questionNumber switch
        {
            >= 25 and <= 30 => $"B{questionNumber - 24}",
            >= 31 and <= 36 => "C1",
            >= 37 and <= 42 => "C2",
            >= 13 and <= 24 => "A2",
            _ => "A1",
        };
    }

    private static bool HasUsableMcqOptions(IReadOnlyCollection<string> options)
    {
        if (options.Count != 3) return false;
        var cleaned = options.Select(CleanListeningOption).ToList();
        return cleaned.All(option => !string.IsNullOrWhiteSpace(option))
            && cleaned.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3;
    }

    private static void AddAccepted(List<string> accepted, string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return;
        if (!accepted.Any(existing => string.Equals(existing.Trim(), answer.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            accepted.Add(answer.Trim());
        }
    }

    private static ListeningExtractMetaDto MapRelationalExtract(
        ListeningExtract extract,
        ListeningPartCode partCode,
        int index,
        string? audioUrl,
        int? timeLimitSeconds)
        => new(
            PartCode: PartCodeString(partCode),
            DisplayOrder: extract.DisplayOrder,
            Kind: ExtractKindString(extract.Kind),
            Title: string.IsNullOrWhiteSpace(extract.Title) ? $"Extract {index + 1}" : extract.Title,
            AccentCode: extract.AccentCode,
            Speakers: ReadSpeakersJson(extract.SpeakersJson),
            AudioStartMs: extract.AudioStartMs,
            AudioEndMs: extract.AudioEndMs,
            AudioUrl: audioUrl,
            TimeLimitSeconds: timeLimitSeconds,
            NotesBody: extract.NotesBodyMarkdown,
            AuthoringMethod: extract.AuthoringMethod,
            PartAOverlayBlanksJson: extract.PartAOverlayBlanksJson,
            ContextIntro: extract.ContextIntro);

    /// <summary>Resolve the per-sub-section audio URL. Priority: an uploaded
    /// primary <see cref="ContentPaperAsset"/> of role Audio for this part code
    /// (served at <c>/v1/media/{id}/content</c>) → the extract's TTS WAV
    /// (<c>/v1/listening/audio/{sha}.wav</c>, served by
    /// <c>ListeningAudioEndpoints</c>) → null (frontend shows "no audio").</summary>
    private static string? ResolveSubSectionAudioUrl(
        IReadOnlyDictionary<string, string> audioByPart,
        string? partCodeString,
        string? audioContentSha)
    {
        var uploaded = ResolveUploadedAudioForSection(audioByPart, partCodeString);
        if (!string.IsNullOrWhiteSpace(uploaded))
        {
            return uploaded;
        }

        return string.IsNullOrWhiteSpace(audioContentSha)
            ? null
            : $"/v1/listening/audio/{audioContentSha}.wav";
    }

    // Learner-facing Listening sections the exam player navigates, in order.
    // Mirrors the frontend LISTENING_SECTION_SEQUENCE — Part B's six question
    // sub-parts collapse to a single "B" section that plays one shared audio.
    private static readonly string[] LearnerAudioSections = ["A1", "A2", "B", "C1", "C2"];

    // Map an extract part code (A1, A2, B1..B6, legacy "B", C1, C2) to the
    // learner-facing section the exam player navigates. Every Part B sub-part
    // collapses to one "B" section.
    private static string SectionForPartCode(string? partCode)
    {
        var code = (partCode ?? string.Empty).Trim().ToUpperInvariant();
        return code.StartsWith('B') ? "B" : code;
    }

    /// <summary>
    /// Resolve an uploaded Audio asset for a learner-facing section. Exact
    /// sub-section keys win; parent-part uploads (A/C) are valid for both
    /// children; and legacy Part B uploads (B1..B6) are accepted for the
    /// collapsed learner-facing B section. The first non-empty match is the
    /// authored priority, so a specific replacement can safely override a
    /// parent fallback.
    /// </summary>
    private static string? ResolveUploadedAudioForSection(
        IReadOnlyDictionary<string, string> audioByPart,
        string? rawPartCode)
    {
        var code = (rawPartCode ?? string.Empty).Trim().ToUpperInvariant();
        var section = SectionForPartCode(code);
        var candidates = new List<string>();

        void Add(string candidate)
        {
            if (!string.IsNullOrWhiteSpace(candidate)
                && !candidates.Contains(candidate, StringComparer.Ordinal))
            {
                candidates.Add(candidate);
            }
        }

        Add(code);
        Add(section);
        if (section.Length > 1) Add(section[..1]);
        if (section == "B")
        {
            for (var i = 1; i <= 6; i++) Add($"B{i}");
        }

        foreach (var candidate in candidates)
        {
            if (audioByPart.TryGetValue(candidate, out var url)
                && !string.IsNullOrWhiteSpace(url))
            {
                return url;
            }
        }

        return null;
    }

    private static IReadOnlyList<ListeningSpeakerDto> ReadSpeakersJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var speakers = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(json, []);
            return ParseSpeakers(speakers);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyDictionary<string, ListeningHumanScoreOverride> ParseHumanScoreOverrides(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, ListeningHumanScoreOverride>();
        try
        {
            var overrides = JsonSerializer.Deserialize<List<ListeningHumanScoreOverride>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            return overrides
                .Where(scoreOverride => !string.IsNullOrWhiteSpace(scoreOverride.QuestionId))
                .GroupBy(scoreOverride => scoreOverride.QuestionId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, ListeningHumanScoreOverride>();
        }
    }

    private static IReadOnlyList<ListeningTranscriptSegmentDto> ExtractTranscriptSegmentsFromJson(string? json, string? fallbackPartCode)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var raw = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(json, []);
            var output = ExtractTranscriptSegments(raw);
            if (string.IsNullOrWhiteSpace(fallbackPartCode)) return output;
            return output
                .Select(segment => segment.PartCode is null ? segment with { PartCode = fallbackPartCode } : segment)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? ReadJsonString(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.String => doc.RootElement.GetString(),
                JsonValueKind.Number => doc.RootElement.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => doc.RootElement.ToString(),
            };
        }
        catch (JsonException)
        {
            return json;
        }
    }

    /// <summary>§17.11 — read a string property from a JSON-object `details`
    /// string (e.g. <c>questionId</c>). Returns null when the details is not a
    /// JSON object, the property is absent, or parsing fails.</summary>
    private static string? ReadJsonProperty(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty(property, out var value)) return null;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => value.ToString(),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>§17.11 — read an integer property (e.g. <c>cuePointMs</c>) from
    /// a JSON-object `details` string. Returns null when absent or non-numeric.</summary>
    private static int? ReadJsonInt(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty(property, out var value)) return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var asInt)) return asInt;
            if (value.ValueKind == JsonValueKind.String
                && int.TryParse(value.GetString(), out var parsed)) return parsed;
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadJsonStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            return doc.RootElement.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string PartCodeString(ListeningPartCode partCode) => partCode switch
    {
        ListeningPartCode.A1 => "A1",
        ListeningPartCode.A2 => "A2",
        // B1..B6 are distinct navigable sub-sections for the learner; fall
        // through to the enum name ("B1".."B6"). Only analytics rolls them up.
        ListeningPartCode.C1 => "C1",
        ListeningPartCode.C2 => "C2",
        _ => partCode.ToString(),
    };

    private static string ExtractKindString(ListeningExtractKind kind) => kind switch
    {
        ListeningExtractKind.Consultation => "consultation",
        ListeningExtractKind.Workplace => "workplace",
        ListeningExtractKind.Presentation => "presentation",
        _ => "consultation",
    };

    private static string DistractorCategoryString(ListeningDistractorCategory category) => category switch
    {
        ListeningDistractorCategory.TooStrong => "too_strong",
        ListeningDistractorCategory.TooWeak => "too_weak",
        ListeningDistractorCategory.WrongSpeaker => "wrong_speaker",
        ListeningDistractorCategory.OppositeMeaning => "opposite_meaning",
        ListeningDistractorCategory.ReusedKeyword => "reused_keyword",
        ListeningDistractorCategory.OutOfScope => "out_of_scope",
        _ => category.ToString(),
    };

    private static string SpeakerAttitudeString(ListeningSpeakerAttitude attitude) => attitude switch
    {
        ListeningSpeakerAttitude.Concerned => "concerned",
        ListeningSpeakerAttitude.Optimistic => "optimistic",
        ListeningSpeakerAttitude.Doubtful => "doubtful",
        ListeningSpeakerAttitude.Critical => "critical",
        ListeningSpeakerAttitude.Neutral => "neutral",
        ListeningSpeakerAttitude.Other => "other",
        _ => "other",
    };

    private async Task<ListeningReviewDto?> GetCachedSubmitAsync(
        string key,
        CancellationToken ct)
    {
        var record = await db.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Scope == SubmitIdempotencyScope && row.Key == key, ct);
        return record is null ? null : TryDeserializeListeningReview(record.ResponseJson);
    }

    private async Task<ListeningReviewDto?> PersistSubmitIdempotencyAsync(
        string key,
        ListeningReviewDto review,
        CancellationToken ct)
    {
        var record = new IdempotencyRecord
        {
            Id = $"idem-{Guid.NewGuid():N}",
            Scope = SubmitIdempotencyScope,
            Key = key,
            ResponseJson = JsonSerializer.Serialize(review),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.IdempotencyRecords.Add(record);
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException)
        {
            // A concurrent retry won the unique (Scope, Key) insert. Return
            // its exact persisted response instead of grading twice at the
            // API boundary.
            db.Entry(record).State = EntityState.Detached;
            var winner = await db.IdempotencyRecords.AsNoTracking()
                .FirstOrDefaultAsync(row => row.Scope == SubmitIdempotencyScope && row.Key == key, ct);
            return winner is null ? null : TryDeserializeListeningReview(winner.ResponseJson);
        }
    }

    private static ListeningReviewDto? TryDeserializeListeningReview(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ListeningReviewDto>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string BuildSubmitIdempotencyKey(
        string userId,
        string attemptId,
        string? callerSuppliedKey)
    {
        var suffix = string.IsNullOrWhiteSpace(callerSuppliedKey)
            ? "default"
            : callerSuppliedKey.Trim();
        var rawKey = $"{userId}:{attemptId}:{suffix}";
        if (rawKey.Length <= MaxIdempotencyRecordKeyLength)
            return rawKey;

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant();
        var scopedDigestKey = $"{userId}:{attemptId}:sha256:{digest}";
        return scopedDigestKey.Length <= MaxIdempotencyRecordKeyLength
            ? scopedDigestKey
            : $"sha256:{digest}";
    }

    private ListeningReviewDto BuildReview(Attempt attempt, ListeningSource source, Evaluation? evaluation = null)
        => BuildReviewCore(
            AttemptId: attempt.Id,
            CompletedAt: attempt.CompletedAt ?? attempt.SubmittedAt,
            Answers: DeserializeAnswers(attempt.AnswersJson),
            Source: source,
            Evaluation: evaluation,
            ScoreOverrides: new Dictionary<string, ListeningHumanScoreOverride>(),
            PersistedRawScore: evaluation?.RawScore,
            PersistedScaledScore: evaluation?.ScaledScore,
            PersistedMaxRawScore: evaluation?.MaxRawScore,
            ScoreConversionTableVersionKey: evaluation?.ScoreConversionTableVersionKey,
            ScoreConversionErrorCode: evaluation?.ScaledScore is null ? "score_conversion_unavailable" : null,
            PersistedScoreConversionGrade: evaluation?.ScoreConversionGrade,
            PersistedScoreConversionPassed: evaluation?.ScoreConversionPassed,
            EvidenceLoopEnabled: ResolveTranscriptEvidencePolicy(attempt.PolicySnapshotJson),
            ReviewVisibility: ResolveReviewVisibility(attempt.PolicySnapshotJson),
            TotalElapsedMilliseconds: attempt.ElapsedSeconds > 0 ? SaturatingMilliseconds(attempt.ElapsedSeconds) : null,
            RequiresAdminReview: attempt.RequiresAdminReview,
            AdminReviewReason: attempt.AdminReviewReason);

    private ListeningReviewDto BuildReview(
        ListeningAttempt attempt,
        ListeningSource source,
        IReadOnlyDictionary<string, string?> answers,
        Evaluation? evaluation = null,
        IReadOnlyDictionary<string, ListeningAnswer>? deterministicAnswers = null,
        string? persistedConversionErrorCode = null)
        => BuildReviewCore(
            AttemptId: attempt.Id,
            CompletedAt: attempt.SubmittedAt,
            Answers: answers,
            Source: source,
            Evaluation: evaluation,
            ScoreOverrides: ParseHumanScoreOverrides(attempt.HumanScoreOverridesJson),
            PersistedRawScore: attempt.RawScore,
            PersistedScaledScore: attempt.ScaledScore,
            PersistedMaxRawScore: attempt.MaxRawScore,
            ScoreConversionTableVersionKey: attempt.ScoreConversionTableVersionKey,
            ScoreConversionErrorCode: persistedConversionErrorCode
                ?? (attempt.ScaledScore is null ? "score_conversion_unavailable" : null),
            PersistedScoreConversionGrade: attempt.ScoreConversionGrade,
            PersistedScoreConversionPassed: attempt.ScoreConversionPassed,
            DeterministicAnswers: deterministicAnswers,
            EvidenceLoopEnabled: ResolveTranscriptEvidencePolicy(attempt.PolicySnapshotJson),
            ReviewVisibility: ResolveReviewVisibility(attempt.PolicySnapshotJson),
            TotalElapsedMilliseconds: ElapsedMilliseconds(attempt.StartedAt, attempt.SubmittedAt ?? attempt.LastActivityAt),
            AudioCueTimelineJson: attempt.AudioCueTimelineJson,
            RequiresAdminReview: attempt.RequiresAdminReview,
            AdminReviewReason: attempt.AdminReviewReason);

    private ListeningReviewDto BuildReviewCore(
        string AttemptId,
        DateTimeOffset? CompletedAt,
        IReadOnlyDictionary<string, string?> Answers,
        ListeningSource Source,
        Evaluation? Evaluation,
        IReadOnlyDictionary<string, ListeningHumanScoreOverride> ScoreOverrides,
        int? PersistedRawScore = null,
        int? PersistedScaledScore = null,
        int? PersistedMaxRawScore = null,
        string? ScoreConversionTableVersionKey = null,
        string? ScoreConversionErrorCode = null,
        string? PersistedScoreConversionGrade = null,
        bool? PersistedScoreConversionPassed = null,
        IReadOnlyDictionary<string, ListeningAnswer>? DeterministicAnswers = null,
        bool EvidenceLoopEnabled = false,
        ListeningReviewVisibility? ReviewVisibility = null,
        int? TotalElapsedMilliseconds = null,
        string? AudioCueTimelineJson = null,
        bool RequiresAdminReview = false,
        string? AdminReviewReason = null)
    {
        var orderedQuestions = Source.Questions.OrderBy(q => q.Number).ToList();
        var items = orderedQuestions
            .Select(q => ReviewItemDto(
                q,
                Answers.GetValueOrDefault(q.Id),
                orderedQuestions,
                DeterministicAnswers?.GetValueOrDefault(q.Id),
                EvidenceLoopEnabled))
            .Select(item => ApplyHumanScoreOverride(item, ScoreOverrides))
            .Select(item => ApplyReviewVisibility(item, ReviewVisibility ?? ListeningReviewVisibility.Strict))
            .ToList();
        var conversionMaxRaw = PersistedMaxRawScore ?? items.Sum(i => i.MaxPoints);
        var maxRaw = conversionMaxRaw > 0 ? conversionMaxRaw : CanonicalRawMax;
        var raw = PersistedRawScore is int persistedRaw
            ? Math.Clamp(persistedRaw, 0, maxRaw)
            : Math.Clamp(items.Sum(i => i.PointsEarned), 0, maxRaw);
        var hasApprovedConversion = !RequiresAdminReview
            && HasApprovedScoreConversion(ScoreConversionTableVersionKey, PersistedScaledScore, PersistedScoreConversionPassed, conversionMaxRaw);
        var scaled = hasApprovedConversion ? PersistedScaledScore : null;
        var grade = hasApprovedConversion ? PersistedScoreConversionGrade ?? "—" : "—";
        var passed = hasApprovedConversion ? PersistedScoreConversionPassed : null;
        var score = new ListeningScoreDto(
            raw,
            maxRaw,
            scaled,
            grade,
            passed);
        var clusters = BuildErrorClusters(items);
        ListeningDrillDto? recommended = null;
        var allowedTranscriptIds = items
            .Where(item => item.Transcript is not null && item.Transcript.Allowed)
            .Select(item => item.QuestionId)
            .ToList();

        return new ListeningReviewDto(
            EvaluationId: Evaluation?.Id,
            AttemptId: AttemptId,
            Paper: SourceDto(Source, includeAudioScriptUrl: true),
            RawScore: score.RawScore,
            MaxRawScore: score.MaxRawScore,
            ScaledScore: score.ScaledScore,
            Grade: score.Grade,
            Passed: score.Passed,
            ScoreConversionTableVersionKey: hasApprovedConversion ? ScoreConversionTableVersionKey : null,
            ScoreConversionErrorCode: hasApprovedConversion ? ScoreConversionErrorCode : "score_conversion_unavailable",
            ScoreDisplay: FormatScoreDisplay(score),
            CorrectCount: items.Count(i => i.IsCorrect),
            IncorrectCount: items.Count(i => !i.IsCorrect && !i.IsInvalid && !string.IsNullOrWhiteSpace(i.LearnerAnswer)),
            UnansweredCount: items.Count(i => string.IsNullOrWhiteSpace(i.LearnerAnswer)),
            ItemReview: items,
            ErrorClusters: clusters,
            RecommendedNextDrill: recommended,
            TranscriptAccess: new ListeningTranscriptAccessDto(
                Policy: "per_item_post_attempt",
                State: allowedTranscriptIds.Count == 0 ? "restricted" : allowedTranscriptIds.Count == items.Count ? "available" : "partial",
                AllowedQuestionIds: allowedTranscriptIds,
                Reason: "Transcript snippets and answer evidence are revealed only after submit and only for items whose authored policy allows it."),
            TranscriptSegments: Source.TranscriptSegments,
            Strengths: BuildStrengths(score.Passed, items),
            Issues: BuildIssues(items),
            GeneratedAt: Evaluation?.GeneratedAt ?? CompletedAt,
            TimeUsed: new ListeningTimeUsedDto(
                TotalMilliseconds: TotalElapsedMilliseconds,
                Sections: BuildSectionTimeUsed(AudioCueTimelineJson)),
            InvalidCount: items.Count(i => i.IsInvalid),
            RequiresAdminReview: RequiresAdminReview,
            AdminReviewReason: AdminReviewReason);
    }

    private static ListeningReviewItemDto ReviewItemDto(
        ListeningQuestion q,
        string? learnerAnswer,
        IReadOnlyList<ListeningQuestion> allQuestions,
        ListeningAnswer? deterministicAnswer = null,
        bool transcriptEvidenceAllowed = false)
    {
        var isInvalid = IsMultipleChoiceQuestionType(q.Type)
            && deterministicAnswer is not null
            && deterministicAnswer.IsCorrect is null;
        var authoredMatch = q.AcceptedAnswers.Any(answer => MatchesObjectiveAnswer(learnerAnswer, answer));
        var isCorrect = !isInvalid && (deterministicAnswer?.IsCorrect ?? authoredMatch);
        var errorType = isInvalid
            ? null
            : isCorrect
            ? null
            : deterministicAnswer?.MissReason is ListeningMissReason miss
                ? MissReasonErrorType(miss)
                : ObjectiveErrorType(q, learnerAnswer, allQuestions);
        var pointsEarned = isInvalid
            ? 0
            : deterministicAnswer is null
            ? isCorrect ? q.Points : 0
            : Math.Clamp(deterministicAnswer.PointsEarned, 0, Math.Max(0, q.Points));
        var transcript = transcriptEvidenceAllowed && q.AllowTranscriptReveal
            ? new ListeningTranscriptSnippetDto(
                Allowed: true,
                Excerpt: q.TranscriptExcerpt,
                DistractorExplanation: q.DistractorExplanation)
            : null;
        var optionAnalysis = BuildOptionAnalysis(q);
        return new ListeningReviewItemDto(
            QuestionId: q.Id,
            Number: q.Number,
            PartCode: q.PartCode,
            Prompt: CleanListeningPrompt(q.Text),
            Type: q.Type,
            LearnerAnswer: learnerAnswer ?? string.Empty,
            CorrectAnswer: q.CorrectAnswer,
            IsCorrect: isCorrect,
            IsInvalid: isInvalid,
            PointsEarned: pointsEarned,
            MaxPoints: q.Points,
            // Never invent rationale text when an older or otherwise
            // incomplete authored item has no approved explanation. The
            // learner projection carries null so the UI can state the
            // unavailable-evidence condition without implying a reason.
            Explanation: q.Explanation,
            ErrorType: errorType,
            Options: q.Options.Select(CleanListeningOption).ToList(),
            Transcript: transcript,
            DistractorExplanation: q.DistractorExplanation,
            OptionAnalysis: optionAnalysis,
            SpeakerAttitude: q.SpeakerAttitude,
            TranscriptEvidenceStartMs: q.TranscriptEvidenceStartMs,
            TranscriptEvidenceEndMs: q.TranscriptEvidenceEndMs,
            ScoreOverride: null,
            MissReason: deterministicAnswer?.MissReason);
    }

    private static ListeningReviewItemDto ApplyReviewVisibility(
        ListeningReviewItemDto item,
        ListeningReviewVisibility visibility)
    {
        var showExplanation = visibility.ShowExplanationsAfterSubmit
            && (!visibility.ShowExplanationsOnlyIfWrong || !item.IsCorrect);
        return item with
        {
            CorrectAnswer = visibility.ShowCorrectAnswerOnReview && !item.IsInvalid ? item.CorrectAnswer : string.Empty,
            Explanation = showExplanation && !item.IsInvalid ? item.Explanation : null,
            DistractorExplanation = showExplanation && !item.IsInvalid ? item.DistractorExplanation : null,
            OptionAnalysis = showExplanation && visibility.ShowCorrectAnswerOnReview && !item.IsInvalid
                ? item.OptionAnalysis
                : null,
        };
    }

    private static ListeningReviewVisibility ResolveReviewVisibility(string? policySnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(policySnapshotJson)) return ListeningReviewVisibility.LegacyDefault;
        try
        {
            using var document = JsonDocument.Parse(policySnapshotJson);
            var root = document.RootElement;
            var policy = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("listeningPolicy", out var nested)
                && nested.ValueKind == JsonValueKind.Object
                ? nested
                : root;
            return new ListeningReviewVisibility(
                ReadBoolean(policy, "showExplanationsAfterSubmit", fallback: true),
                ReadBoolean(policy, "showExplanationsOnlyIfWrong", fallback: false),
                ReadBoolean(policy, "showCorrectAnswerOnReview", fallback: true));
        }
        catch (JsonException)
        {
            return ListeningReviewVisibility.Strict;
        }
    }

    private static bool ReadBoolean(JsonElement value, string propertyName, bool fallback)
        => value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? property.GetBoolean()
                : fallback;

    private async Task<bool> ResolveScreenReaderOptimisedForSessionAsync(
        string userId,
        string? policySnapshotJson,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(policySnapshotJson))
        {
            try
            {
                using var document = JsonDocument.Parse(policySnapshotJson);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;
                var policy = root.TryGetProperty("listeningPolicy", out var nested)
                    && nested.ValueKind == JsonValueKind.Object
                    ? nested
                    : root;
                if (policy.TryGetProperty("screenReaderOptimised", out var captured))
                {
                    return captured.ValueKind is JsonValueKind.True or JsonValueKind.False
                        && captured.GetBoolean();
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        // Legacy attempts without a captured accessibility value retain the
        // current owner policy; new attempts always carry the immutable value.
        var (currentPolicy, _) = await ResolveListeningPolicyAsync(userId, ct);
        return currentPolicy.ScreenReaderOptimised;
    }

    private static bool ResolveTranscriptEvidencePolicy(string? policySnapshotJson)
    {
        // Post-submit review should always surface authored transcript evidence.
        // The learningEvidenceLoopEnabled flag gates the *learning-mode* replay
        // loop, not the post-submit review. Default to true when the snapshot is
        // missing/malformed so legacy attempts and papers still render their full
        // script and per-question clues. An explicit false is still honoured when
        // present.
        if (string.IsNullOrWhiteSpace(policySnapshotJson)) return true;
        try
        {
            using var document = JsonDocument.Parse(policySnapshotJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return true;

            if (root.TryGetProperty("learningEvidenceLoopEnabled", out var direct)
                && direct.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return direct.GetBoolean();
            }

            if (root.TryGetProperty("listeningPolicy", out var nested)
                && nested.ValueKind == JsonValueKind.Object
                && nested.TryGetProperty("learningEvidenceLoopEnabled", out var captured)
                && captured.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return captured.GetBoolean();
            }
        }
        catch (JsonException)
        {
            return true;
        }

        return true;
    }

    private static string MissReasonErrorType(ListeningMissReason missReason) => missReason switch
    {
        ListeningMissReason.Empty => "empty",
        ListeningMissReason.SpellingError => "spelling",
        ListeningMissReason.WrongNumber => "wrong_number",
        ListeningMissReason.ExtraInfo => "extra_info",
        ListeningMissReason.WrongSection => "wrong_section",
        ListeningMissReason.Paraphrase => "paraphrase",
        _ => "detail_capture",
    };

    private static ListeningReviewItemDto ApplyHumanScoreOverride(
        ListeningReviewItemDto item,
        IReadOnlyDictionary<string, ListeningHumanScoreOverride> overrides)
    {
        if (!overrides.TryGetValue(item.QuestionId, out var scoreOverride)) return item;

        var isCorrect = scoreOverride.Override == 1;
        var message = isCorrect
            ? "Marked correct by human reviewer."
            : "Marked incorrect by human reviewer.";
        return item with
        {
            IsCorrect = isCorrect,
            IsInvalid = false,
            PointsEarned = isCorrect ? item.MaxPoints : 0,
            ErrorType = isCorrect ? null : item.ErrorType ?? "human_override",
            Explanation = message,
            ScoreOverride = new ListeningHumanScoreOverrideDto(
                scoreOverride.Override,
                message)
        };
    }

    /// <summary>
    /// Build per-option analysis for MCQ items (Part B/C). Returns null when
    /// the question has no options or no per-option metadata is authored.
    /// </summary>
    private static IReadOnlyList<ListeningOptionAnalysisDto>? BuildOptionAnalysis(ListeningQuestion q)
    {
        if (q.Options is null || q.Options.Count == 0) return null;
        var hasAny = (q.OptionDistractorWhy?.Any(s => !string.IsNullOrWhiteSpace(s)) ?? false)
            || (q.OptionDistractorCategory?.Any(s => !string.IsNullOrWhiteSpace(s)) ?? false);
        if (!hasAny) return null;

        var labels = new[] { "A", "B", "C", "D", "E", "F" };
        var result = new List<ListeningOptionAnalysisDto>(q.Options.Count);
        for (var i = 0; i < q.Options.Count; i++)
        {
            var label = i < labels.Length ? labels[i] : (i + 1).ToString();
            var optionText = q.Options[i] ?? string.Empty;
            var why = i < (q.OptionDistractorWhy?.Count ?? 0) ? q.OptionDistractorWhy![i] : null;
            var cat = i < (q.OptionDistractorCategory?.Count ?? 0) ? q.OptionDistractorCategory![i] : null;
            var isCorrect = string.Equals(label, q.CorrectAnswer?.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(optionText, q.CorrectAnswer, StringComparison.OrdinalIgnoreCase);
            result.Add(new ListeningOptionAnalysisDto(
                OptionLabel: label,
                OptionText: optionText,
                IsCorrect: isCorrect,
                DistractorCategory: string.IsNullOrWhiteSpace(cat) ? null : cat,
                WhyMarkdown: string.IsNullOrWhiteSpace(why) ? null : why));
        }
        return result;
    }

    private static List<ListeningErrorClusterDto> BuildErrorClusters(IReadOnlyCollection<ListeningReviewItemDto> items)
        => items
            .Where(item => !item.IsCorrect && !item.IsInvalid)
            .GroupBy(item => item.ErrorType ?? "detail_capture")
            .Select(group => new ListeningErrorClusterDto(
                ErrorType: group.Key,
                Label: ObjectiveErrorTypeLabel(group.Key),
                Count: group.Count(),
                AffectedQuestionIds: group.Select(item => item.QuestionId).ToList()))
            .OrderByDescending(cluster => cluster.Count)
            .ThenBy(cluster => cluster.Label, StringComparer.Ordinal)
            .ToList();

    private static List<string> BuildStrengths(bool? passed, IReadOnlyCollection<ListeningReviewItemDto> items)
    {
        if (items.Count == 0) return ["No graded Listening items were available."];
        if (passed == true)
        {
            return ["The approved owner conversion table marks this practice score as passed."];
        }

        var correct = items.Count(i => i.IsCorrect);
        return correct == 0
            ? ["You completed the Listening attempt and now have item-level evidence to review."]
            : [$"You captured {correct} authored Listening item{(correct == 1 ? string.Empty : "s")} correctly."];
    }

    private static List<string> BuildIssues(IReadOnlyCollection<ListeningReviewItemDto> items)
        => items
            .Where(item => !item.IsCorrect && !item.IsInvalid)
            .Take(3)
            .Select(item => item.DistractorExplanation ?? item.Explanation)
            .DefaultIfEmpty("Keep using transcript-backed review to maintain accuracy under exam pressure.")
            .ToList();

    private static IEnumerable<ListeningQuestion> ExtractQuestions(object? source)
    {
        if (source is null) yield break;
        var questions = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(JsonSupport.Serialize(source), []);
        var fallbackNumber = 1;
        foreach (var question in questions)
        {
            var id = ReadString(question.GetValueOrDefault("id")) ?? $"lq-{fallbackNumber}";
            var number = ReadInt(question.GetValueOrDefault("number"))
                ?? ReadInt(question.GetValueOrDefault("displayOrder"))
                ?? fallbackNumber;
            var correct = ReadAnswer(question.GetValueOrDefault("correctAnswer"))
                ?? ReadAnswer(question.GetValueOrDefault("correctAnswerJson"))
                ?? ReadAnswer(question.GetValueOrDefault("answer"))
                ?? string.Empty;
            var accepted = ReadStringList(question.GetValueOrDefault("acceptedAnswers"))
                ?? ReadStringList(question.GetValueOrDefault("acceptedSynonyms"))
                ?? ReadStringList(question.GetValueOrDefault("acceptedSynonymsJson"))
                ?? [];
            if (!string.IsNullOrWhiteSpace(correct)) accepted.Insert(0, correct);

            var type = ReadString(question.GetValueOrDefault("type")) ?? ReadString(question.GetValueOrDefault("questionType")) ?? "short_answer";
            var options = ReadStringList(question.GetValueOrDefault("options")) ?? [];
            var rawPartCode = ReadString(question.GetValueOrDefault("partCode"))
                ?? ReadString(question.GetValueOrDefault("part"));
            var partCode = ResolveQuestionPartCode(rawPartCode, number);
            var rawText = ReadString(question.GetValueOrDefault("text"));
            var rawStem = ReadString(question.GetValueOrDefault("stem"));

            // MCQ (Part B/C): grade by option LETTER *or* TEXT interchangeably. The
            // correct answer may be stored as a letter (fast builder) or as the
            // option text (advanced editor), and the learner may submit either.
            // Add the correct option's letter AND text to the accepted set so the
            // option's display prose is grading-neutral (replacing "Option A/B/C"
            // with real text can never change a score).
            var normalizedType = type.Trim().ToLowerInvariant();
            var isMcq = normalizedType is "multiple_choice_3" or "mcq" or "mcq3";
            if (isMcq && options.Count > 0 && !string.IsNullOrWhiteSpace(correct))
            {
                var trimmedCorrect = correct.Trim();
                var correctIndex = -1;
                if (trimmedCorrect.Length == 1 && char.IsLetter(trimmedCorrect[0]))
                {
                    var candidate = char.ToUpperInvariant(trimmedCorrect[0]) - 'A';
                    if (candidate >= 0 && candidate < options.Count) correctIndex = candidate;
                }
                if (correctIndex < 0)
                {
                    correctIndex = options.FindIndex(o => string.Equals(o?.Trim(), trimmedCorrect, StringComparison.OrdinalIgnoreCase));
                }
                if (correctIndex >= 0 && correctIndex < options.Count)
                {
                    var letter = ((char)('A' + correctIndex)).ToString();
                    if (!accepted.Contains(letter, StringComparer.OrdinalIgnoreCase)) accepted.Add(letter);
                    var text = options[correctIndex];
                    if (!string.IsNullOrWhiteSpace(text) && !accepted.Contains(text, StringComparer.OrdinalIgnoreCase)) accepted.Add(text);
                }
            }

            yield return new ListeningQuestion(
                Id: id,
                Number: number,
                PartCode: partCode,
                Text: SelectQuestionPrompt(rawText, rawStem, partCode),
                Type: type,
                Options: options.Select(CleanListeningOption).ToList(),
                CorrectAnswer: correct,
                AcceptedAnswers: accepted.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Explanation: ReadString(question.GetValueOrDefault("explanation")) ?? ReadString(question.GetValueOrDefault("explanationMarkdown")),
                SkillTag: ReadString(question.GetValueOrDefault("skillTag")),
                AllowTranscriptReveal: ReadBool(question.GetValueOrDefault("allowTranscriptReveal")) ?? true,
                TranscriptExcerpt: ReadString(question.GetValueOrDefault("transcriptExcerpt")),
                DistractorExplanation: ReadString(question.GetValueOrDefault("distractorExplanation")),
                Points: ReadInt(question.GetValueOrDefault("points")) ?? 1,
                OptionDistractorWhy: ReadStringList(question.GetValueOrDefault("optionDistractorWhy"))
                    ?? ReadStringList(question.GetValueOrDefault("perOptionWhy"))
                    ?? new List<string>(),
                OptionDistractorCategory: ReadStringList(question.GetValueOrDefault("optionDistractorCategory"))
                    ?? ReadStringList(question.GetValueOrDefault("perOptionDistractorCategory"))
                    ?? new List<string>(),
                SpeakerAttitude: ReadString(question.GetValueOrDefault("speakerAttitude")),
                TranscriptEvidenceStartMs: ReadInt(question.GetValueOrDefault("transcriptEvidenceStartMs")),
                TranscriptEvidenceEndMs: ReadInt(question.GetValueOrDefault("transcriptEvidenceEndMs")));
            fallbackNumber++;
        }
    }

    private static object LearnerQuestionDto(ListeningQuestion q) => new
    {
        q.Id,
        q.Number,
        q.PartCode,
        text = CleanListeningPrompt(q.Text),
        // Learner wire type. The 3 authored content types collapse to 2 input
        // shapes for the player: MCQ renders options; FillInBlank + ShortAnswer
        // both render a free-text box. Never surface "fill_in_blank" raw — it
        // would have no renderer and could imply a different (answer-leaking)
        // shape. Options are only non-empty for MCQ items.
        type = LearnerWireType(q.Type),
        options = q.Options.Select(CleanListeningOption).ToList(),
        // Positional option keys (A/B/C). The learner card submits the KEY, not
        // the display text, so replacing "Option A/B/C" with real prose is
        // grading-neutral. Empty for non-MCQ (free-text) items.
        optionKeys = q.Options.Select((_, index) => ((char)('A' + index)).ToString()).ToList(),
        q.Points
    };

    /// <summary>Map an authored question type to the learner-facing wire type.
    /// MCQ stays <c>multiple_choice_3</c>; every text-input type (short_answer /
    /// fill_in_blank / gap_fill / note_completion) collapses to
    /// <c>short_answer</c> so the player renders a single free-text input and no
    /// answer detail ever leaks.</summary>
    private static string LearnerWireType(string? authoredType)
    {
        var normalized = (authoredType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "multiple_choice_3" or "mcq" or "mcq3" => "multiple_choice_3",
            _ => "short_answer",
        };
    }

    private static object SourceDto(ListeningSource source, bool includeAudioScriptUrl = false) => new
    {
        id = source.Id,
        sourceKind = source.SourceKind,
        title = source.Title,
        slug = source.Slug,
        difficulty = source.Difficulty,
        estimatedDurationMinutes = source.EstimatedDurationMinutes,
        scenarioType = source.ScenarioType,
        audioUrl = source.AudioUrl,
        questionPaperUrl = source.QuestionPaperUrl,
        questionPaperUrlByPart = source.QuestionPaperUrlByPart ?? new Dictionary<string, string>(),
        // Answer-key assets are an authoring/admin concern. Never include the
        // URL in a learner session or post-submit review projection: the v1.1
        // contract keeps keys hidden until grading and exposes only the
        // policy-controlled item review after submission.
        audioUrlByPart = source.AudioUrlByPart ?? new Dictionary<string, string>(),
        // Full audio script PDF (marker reference). Exposed post-submit ONLY on
        // the review/results surfaces so learners can read the complete transcript
        // alongside time-coded evidence. Null during the timed session so the
        // script cannot be used to cheat. Null when no AudioScript asset is
        // attached. The media endpoint still gates it to entitled learners via
        // MediaAssetAccessService (AudioScript added to learner-visible roles).
        audioScriptUrl = includeAudioScriptUrl ? source.AudioScriptUrl : null,
        audioAvailable = !string.IsNullOrWhiteSpace(source.AudioUrl)
            || (source.AudioUrlByPart?.Count ?? 0) > 0,
        audioUnavailableReason = !string.IsNullOrWhiteSpace(source.AudioUrl)
            || (source.AudioUrlByPart?.Count ?? 0) > 0
            ? null
            : "Audio is not available for this Listening paper yet.",
        source.AssetReadiness,
        transcriptPolicy = "per_item_post_attempt",
        // Phase 5 tail: paper-level extract metadata (accent + speakers +
        // audio window + extract kind/title). Empty list when the authored
        // paper has no metadata yet.
        extracts = source.Extracts.Select(e => new
        {
            partCode = e.PartCode,
            displayOrder = e.DisplayOrder,
            kind = e.Kind,
            title = e.Title,
            accentCode = e.AccentCode,
            speakers = e.Speakers,
            audioStartMs = e.AudioStartMs,
            audioEndMs = e.AudioEndMs,
            // Per-sub-section audio + countdown. audioUrl is uploaded-asset-first
            // with a TTS fallback; null when neither exists. timeLimitSeconds is
            // null when unauthored (frontend applies a default).
            audioUrl = e.AudioUrl,
            timeLimitSeconds = e.TimeLimitSeconds,
            // Part A note-completion document; null for Part B/C and unauthored A.
            notesBody = e.NotesBody,
            // Phase 6: Part A authoring method + PDF-overlay blank placements.
            authoringMethod = e.AuthoringMethod,
            partAOverlayBlanksJson = e.PartAOverlayBlanksJson,
            // Part B/C scenario line, rendered once per extract on the learner card.
            contextIntro = e.ContextIntro,
        }).ToList()
    };

    private static object AttemptDto(
        Attempt attempt,
        IReadOnlyDictionary<string, string?> answers,
        string? feedbackMessage = null)
    {
        var audio = ReadGenericAudioPlayback(answers);
        return new
        {
            serverNow = DateTimeOffset.UtcNow,
            attemptId = attempt.Id,
            paperId = attempt.ContentId,
            state = ToApiState(attempt.State),
            attempt.Mode,
            attempt.StartedAt,
            attempt.SubmittedAt,
            attempt.CompletedAt,
            attempt.ElapsedSeconds,
            attempt.LastClientSyncAt,
            attempt.RequiresAdminReview,
            attempt.AdminReviewReason,
            attempt.AdminReviewFlaggedAt,
            expiresAt = ReadGenericDeadline(attempt),
            // Strip reserved navigation keys (e.g. the one-way section cursor) so
            // they never leak into the player's answer map or get re-submitted as a
            // bogus answer. The cursor is surfaced separately via advance-section.
            answers = StripReservedAnswerKeys(answers),
            sectionCursor = ReadGenericSectionCursor(answers),
            audioPlaybackState = audio.State,
            audioResumeAtMs = audio.ResumeAtMs,
            audioPlaybackSection = audio.Section,
            audioQuestionIndex = audio.QuestionIndex,
            feedbackMessage,
        };
    }

    /// <summary>True for reserved, non-question answer-map keys (currently just
    /// the one-way section cursor). Such keys are persisted in a generic
    /// attempt's <c>AnswersJson</c> but must never be treated as a learner
    /// answer, counted, graded, or echoed back to the player.</summary>
    private static bool IsReservedAnswerKey(string key)
        => key is GenericSectionCursorKey
            or GenericAudioPlaybackStateKey
            or GenericAudioResumeMsKey
            or GenericAudioSectionKey
            or GenericAudioQuestionIndexKey;

    private static Dictionary<string, string?> StripReservedAnswerKeys(IReadOnlyDictionary<string, string?> answers)
        => answers
            .Where(kv => !IsReservedAnswerKey(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

    private static object RelationalAttemptDto(
        ListeningAttempt attempt,
        IReadOnlyDictionary<string, string?> answers,
        string? feedbackMessage = null)
    {
        var audio = ReadAudioPlaybackSnapshot(attempt.AudioCueTimelineJson);
        return new
        {
            serverNow = DateTimeOffset.UtcNow,
            attemptId = attempt.Id,
            paperId = attempt.PaperId,
            state = attempt.Status == ListeningAttemptStatus.Submitted ? "completed" : ToApiState(attempt.Status),
            mode = ToApiMode(attempt.Mode),
            attempt.StartedAt,
            attempt.SubmittedAt,
            completedAt = attempt.SubmittedAt,
            elapsedSeconds = (int)Math.Max(0, (attempt.LastActivityAt - attempt.StartedAt).TotalSeconds),
            lastClientSyncAt = attempt.LastActivityAt,
            requiresAdminReview = attempt.RequiresAdminReview,
            adminReviewReason = attempt.AdminReviewReason,
            adminReviewFlaggedAt = attempt.AdminReviewFlaggedAt,
            expiresAt = attempt.DeadlineAt,
            answers,
            sectionCursor = ReadSectionCursor(attempt.NavigationStateJson),
            audioPlaybackState = audio.State,
            audioResumeAtMs = audio.ResumeAtMs,
            audioPlaybackSection = audio.Section,
            audioQuestionIndex = audio.QuestionIndex,
            feedbackMessage,
        };
    }

    private static object? BuildPaperLastAttemptDto(string paperId, Attempt? genericAttempt, ListeningAttempt? relationalAttempt)
    {
        var genericAt = genericAttempt?.LastClientSyncAt ?? genericAttempt?.SubmittedAt ?? genericAttempt?.StartedAt ?? DateTimeOffset.MinValue;
        var relationalAt = relationalAttempt?.LastActivityAt ?? DateTimeOffset.MinValue;
        if (relationalAttempt is not null && relationalAt >= genericAt)
        {
            var mode = ToApiMode(relationalAttempt.Mode);
            return new
            {
                attemptId = relationalAttempt.Id,
                status = relationalAttempt.Status == ListeningAttemptStatus.Submitted ? "completed" : ToApiState(relationalAttempt.Status),
                relationalAttempt.StartedAt,
                relationalAttempt.SubmittedAt,
                mode,
                route = relationalAttempt.Status == ListeningAttemptStatus.Submitted
                    ? $"/listening/results/{Uri.EscapeDataString(relationalAttempt.Id)}"
                    : RelationalAttemptRoute(relationalAttempt)
            };
        }

        return genericAttempt is null ? null : new
        {
            attemptId = genericAttempt.Id,
            status = ToApiState(genericAttempt.State),
            genericAttempt.StartedAt,
            genericAttempt.SubmittedAt,
            mode = genericAttempt.Mode,
            route = genericAttempt.State == AttemptState.Completed
                ? $"/listening/results/{Uri.EscapeDataString(genericAttempt.Id)}"
                : $"/listening/player/{Uri.EscapeDataString(paperId)}?attemptId={Uri.EscapeDataString(genericAttempt.Id)}&mode={Uri.EscapeDataString(genericAttempt.Mode)}"
        };
    }

    private static object PaperHomeDto(
        ContentPaper paper,
        object? lastAttempt,
        int relationalQuestionCount,
        bool requiresSubscription,
        int relationalPartACount = 0,
        int relationalPartBCount = 0,
        int relationalPartCCount = 0)
    {
        var roles = paper.Assets.Where(a => a.IsPrimary).Select(a => a.Role).ToHashSet();
        var questionMap = JsonSupport.Deserialize<Dictionary<string, object?>>(
            paper.ExtractedTextJson,
            new Dictionary<string, object?>());
        var questions = ExtractQuestions(
            questionMap.TryGetValue("listeningQuestions", out var listeningQuestions)
                ? listeningQuestions
                : questionMap.GetValueOrDefault("questions"))
            .ToList();

        // BuildPaperSourceAsync merges relational rows with this normalized JSON
        // set by the authoritative printed question number. Keep the catalog
        // counts on that same union: an old import can have one relational B/C
        // row while the source JSON already contains all six/twelve items. A
        // relational count must never hide the remaining source questions from
        // the standalone-part dispatcher or the learner-facing home cards.
        var jsonQuestionNumbers = questions.Select(q => q.Number).Distinct().ToHashSet();
        var jsonPartACount = questions
            .Where(q => ListeningParentPartFromCode(q.PartCode) == "A")
            .Select(q => q.Number)
            .Distinct()
            .Count();
        var jsonPartBCount = questions
            .Where(q => ListeningParentPartFromCode(q.PartCode) == "B")
            .Select(q => q.Number)
            .Distinct()
            .Count();
        var jsonPartCCount = questions
            .Where(q => ListeningParentPartFromCode(q.PartCode) == "C")
            .Select(q => q.Number)
            .Distinct()
            .Count();
        var questionCount = Math.Max(relationalQuestionCount, jsonQuestionNumbers.Count);
        var partACount = Math.Max(relationalPartACount, jsonPartACount);
        var partBCount = Math.Max(relationalPartBCount, jsonPartBCount);
        var partCCount = Math.Max(relationalPartCCount, jsonPartCCount);
        return new
        {
            id = paper.Id,
            paper.Title,
            paper.Slug,
            paper.Difficulty,
            paper.EstimatedDurationMinutes,
            paper.PublishedAt,
            tagsCsv = paper.TagsCsv,
            partACount,
            partBCount,
            partCCount,
            route = $"/listening/paper/{Uri.EscapeDataString(paper.Id)}",
            sourceKind = "content_paper",
            objectiveReady = questionCount > 0,
            questionCount,
            requiresSubscription,
            accessTier = ResolveAccessTier(paper.TagsCsv),
            assetReadiness = new
            {
                audio = roles.Contains(PaperAssetRole.Audio),
                questionPaper = roles.Contains(PaperAssetRole.QuestionPaper),
                answerKey = roles.Contains(PaperAssetRole.AnswerKey),
                audioScript = roles.Contains(PaperAssetRole.AudioScript)
            },
            lastAttempt
        };
    }

    /// <summary>
    /// Resolve the learner-facing access tier from a paper's <c>TagsCsv</c>.
    /// Tokens recognised (case-insensitive):
    ///   <c>access:free</c> → <c>"free"</c>,
    ///   <c>access:preview</c> or <c>access:preview-first-extract</c> → <c>"preview"</c>,
    ///   anything else (including null/empty) → <c>"premium"</c>.
    /// This is independent of <see cref="IContentEntitlementService"/>: it
    /// describes the paper's intrinsic catalog tier, not the current user's
    /// entitlement.
    /// </summary>
    public static string ResolveAccessTier(string? tagsCsv)
    {
        if (string.IsNullOrWhiteSpace(tagsCsv)) return "premium";
        foreach (var rawToken in tagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = rawToken.ToLowerInvariant();
            if (token == "access:free") return "free";
            if (token == "access:preview" || token == "access:preview-first-extract") return "preview";
        }
        return "premium";
    }

    private static object LegacyTaskHomeDto(ContentItem item)
    {
        var detail = JsonSupport.Deserialize<Dictionary<string, object?>>(item.DetailJson, new Dictionary<string, object?>());
        var questions = ExtractQuestions(detail.GetValueOrDefault("questions")).ToList();
        return new
        {
            contentId = item.Id,
            id = item.Id,
            item.Title,
            item.Difficulty,
            item.EstimatedDurationMinutes,
            item.ScenarioType,
            route = $"/listening/player/{Uri.EscapeDataString(item.Id)}",
            sourceKind = "legacy_content_item",
            objectiveReady = questions.Count > 0,
            questionCount = questions.Count
        };
    }

    private static List<object> BuildPartCollections(IReadOnlyCollection<object> paperDtos, IReadOnlyCollection<object> featuredTasks, string? firstPaperId)
    {
        var readyCount = paperDtos.Count + featuredTasks.Count;
        var partARoute = readyCount > 0 && firstPaperId is not null
            ? $"/listening/player/{Uri.EscapeDataString(firstPaperId)}?mode=practice&focus=part-a"
            : null;
        var partBCRoute = readyCount > 0 && firstPaperId is not null
            ? $"/listening/player/{Uri.EscapeDataString(firstPaperId)}?mode=practice&focus=parts-bc"
            : null;
        return
        [
            new
            {
                id = "part-a",
                title = "Part A detail capture",
                description = "Consultation-note accuracy, numbers, units, and clinical details.",
                available = readyCount > 0,
                route = partARoute
            },
            new
            {
                id = "parts-b-c",
                title = "Parts B/C decision control",
                description = "Purpose, attitude, distractors, and final recommendation control.",
                available = readyCount > 0,
                route = partBCRoute
            }
        ];
    }

    private static ListeningDrillDto BuildDrill(string errorType, string? paperId, string? attemptId)
    {
        var normalized = string.IsNullOrWhiteSpace(errorType) ? "detail_capture" : errorType.Trim().ToLowerInvariant();
        var (title, focusLabel, description, minutes, highlights) = normalized switch
        {
            "distractor_confusion" => (
                "Distractor Control Drill",
                "Speaker intent and change-of-plan control",
                "Separate what was suggested first from what was finally agreed.",
                12,
                new[]
                {
                    "Track corrected instructions instead of the first option you hear.",
                    "Notice when a clinician rules out a medication or follow-up plan.",
                    "Review transcript evidence only after you commit to an answer."
                }),
            "numbers_and_frequencies" => (
                "Numbers and Frequencies Drill",
                "Medication, dosage, and appointment precision",
                "Practise exact numbers, timings, and dosage language in fast clinical audio.",
                10,
                new[]
                {
                    "Distinguish similar-sounding numbers before replaying.",
                    "Lock onto frequency phrases such as once daily and every second day.",
                    "Use replay snippets to verify quantities, not whole conversations."
                }),
            "spelling" => (
                "Medical Spelling Drill",
                "Part A spelling accuracy",
                "Train precise spelling of medical and clinical vocabulary you commonly hear in consultations.",
                10,
                new[]
                {
                    "Compare your written form against the canonical spelling.",
                    "Build a mental list of high-risk medical roots and endings.",
                    "Slow down on the final two letters — that's where most marks are lost."
                }),
            "grammar_number" => (
                "Grammar and Number Drill",
                "Singular vs plural and article accuracy",
                "Lock onto whether the audio uses a singular or plural noun, and which article precedes it.",
                8,
                new[]
                {
                    "Listen for the determiner: a, an, the, some, any.",
                    "Separate countable from uncountable nouns.",
                    "Match the plural marker exactly as the speaker used it."
                }),
            "paraphrase" => (
                "Use the Speaker's Words Drill",
                "Part A exact-words requirement",
                "Stop rewriting what you heard. Train yourself to write the speaker's exact words inside the gap.",
                10,
                new[]
                {
                    "If the audio said 'sleep apnoea', do not write 'breathing problem'.",
                    "Predict the answer type, then capture the speaker's phrase verbatim.",
                    "After review, mark down where you paraphrased and why."
                }),
            "wrong_section" => (
                "Right Word, Right Gap Drill",
                "Note-completion section discipline",
                "Train your eye to lock the right detail to the right note heading before the audio drifts.",
                12,
                new[]
                {
                    "Read each gap heading aloud before the audio starts.",
                    "Predict which section the next answer belongs to.",
                    "Avoid front-loading: a word may belong to the next bullet, not this one."
                }),
            "extra_info" => (
                "Concise Answers Drill",
                "Avoid extra-words deductions",
                "Keep your gap answers minimal — the audio's exact phrase, nothing more.",
                8,
                new[]
                {
                    "Do not add explanatory phrases the audio never used.",
                    "Strip leading articles unless the speaker used them.",
                    "Stop when the gap is full; one word too many can lose a mark."
                }),
            "empty" => (
                "Don't Leave Gaps Drill",
                "Coverage and educated guessing",
                "Practise filling every gap, even when uncertain — there's no negative marking.",
                8,
                new[]
                {
                    "Keep moving — never freeze on one gap.",
                    "Predict the answer type early so your guess is plausible.",
                    "Cross-check the gap heading: type matters more than wording."
                }),
            _ => (
                "Exact Detail Capture Drill",
                "Referral detail and key-clue accuracy",
                "Rebuild listening accuracy by isolating the exact clinical detail that changed the answer.",
                11,
                new[]
                {
                    "Identify which detail actually answers the question.",
                    "Separate symptoms, plans, and history without blending them.",
                    "Review the transcript clue that justified the correct answer."
                })
        };
        var drillId = NormalizeDrillId($"listening-drill-{normalized}");
        var launchRoute = string.IsNullOrWhiteSpace(paperId)
            ? "/listening"
            : $"/listening/player/{Uri.EscapeDataString(paperId)}?drill={Uri.EscapeDataString(drillId)}";
        var reviewRoute = string.IsNullOrWhiteSpace(attemptId)
            ? "/listening"
            : $"/listening/review/{Uri.EscapeDataString(attemptId)}?drill={Uri.EscapeDataString(drillId)}";
        return new ListeningDrillDto(drillId, title, focusLabel, description, normalized, minutes, highlights, launchRoute, reviewRoute);
    }

    private static readonly System.Text.RegularExpressions.Regex SentinelPattern =
        new(@"^(see pdf|cpdf|pdf|view pdf)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    // Part B/C headings are useful surrounding metadata, but they are never a
    // learner-facing question. Keep this guard centralized so JSON imports,
    // relational rows, and both candidate renderers apply the same rule.
    private static readonly System.Text.RegularExpressions.Regex PartBCMetadataStemPattern =
        new(@"^(?:PART\s+[BC]\b.*|Q(?:UESTION)?\s*\d+\s+PART\s+[BC]\b.*|QUESTION\s+\d+\b.*)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex PartBCGenericStemPattern =
        new(@"^(?:WHAT\s+DOES\s+THE\s+SPEAKER\s+IDENTIFY\s+AS\s+THE\s+MAIN\s+CLINICAL\s+PRIORITY\?|WHAT\s+IS\s+THE\s+SPEAKER(?:'|’)S\s+MAIN\s+POINT\s+IN\s+THIS\s+EXTRACT\?)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex OptionPlaceholderPattern =
        new(@"^Option\s+[ABC]$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex[] ArtifactLinePatterns =
    [
        new(@"^\s*[-=]{2,}\s*PAGE\s*\d+\s*[-=]{2,}\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"^\s*PAGE\s*\d+\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"^\s*Practice Test\s*\d+\s*:?\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"^\s*\uF0B7\s*$", System.Text.RegularExpressions.RegexOptions.Compiled)
    ];

    private static readonly System.Text.RegularExpressions.Regex[] InlineArtifactPatterns =
    [
        new(@"^\s*PAGE\s+\d+\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"^\s*[-=]{2,}\s*PAGE\s*\d+\s*[-=]{2,}\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"^\s*Practice Test\s*\d+\s*[:\-]?\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled)
    ];

    private static readonly System.Text.RegularExpressions.Regex MultipleWhitespacePattern =
        new(@"\s{2,}", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static string SanitizeQuestionPrompt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var text = raw.Trim();
        if (SentinelPattern.IsMatch(text)) return string.Empty;

        var lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        var kept = new List<string>();
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (string.IsNullOrWhiteSpace(t)) continue;
            if (SentinelPattern.IsMatch(t)) continue;
            var isArtifact = false;
            foreach (var pattern in ArtifactLinePatterns)
            {
                if (pattern.IsMatch(t))
                {
                    isArtifact = true;
                    break;
                }
            }
            if (!isArtifact) kept.Add(t);
        }

        if (kept.Count == 0) return string.Empty;

        text = string.Join(" ", kept).Trim();
        foreach (var pattern in InlineArtifactPatterns)
        {
            text = pattern.Replace(text, string.Empty).Trim();
        }

        // Global inline artifacts (trailing " ===== PAGE 4 =====" etc.)
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*={2,}\s*PAGE\s*\d+\s*={2,}\s*", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*-{2,}\s*PAGE\s*\d+\s*-{2,}\s*", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*PAGE\s*\d+\s*", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*o\s*Practice Test\s*\d+\s*:?\s*", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*Practice Test\s*\d+\s*:?\s*", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        text = text.Replace("\uF0B7", " ");

        text = MultipleWhitespacePattern.Replace(text, " ").Trim();
        if (SentinelPattern.IsMatch(text)) return string.Empty;
        return text;
    }

    /// <summary>Returns true only when a Part B/C value is an actual question
    /// sentence rather than a PDF sentinel, generic fallback, or section
    /// heading. This is deliberately fail-closed: if the source is absent the
    /// candidate must not be shown invented content.</summary>
    public static bool IsUsablePartBCStem(string? raw)
    {
        var text = SanitizeQuestionPrompt(raw);
        return !string.IsNullOrWhiteSpace(text)
            && !PartBCMetadataStemPattern.IsMatch(text)
            && !PartBCGenericStemPattern.IsMatch(text);
    }

    private static string SelectQuestionPrompt(string? rawText, string? rawStem, string? partCode)
    {
        if (IsPartBCCode(partCode))
        {
            // The authoring contract calls the field `stem`; `text` remains a
            // supported legacy alias. Prefer a usable stem, then a usable text
            // alias. Never return a metadata heading or generic fallback when
            // both values are unusable: that would make an invalid record look
            // publishable and would hide the missing source from operators.
            foreach (var candidate in new[] { rawStem, rawText })
            {
                if (IsUsablePartBCStem(candidate)) return SanitizeQuestionPrompt(candidate);
            }

            return string.Empty;
        }

        return CleanListeningPrompt(rawText ?? rawStem ?? string.Empty);
    }

    public static string CleanListeningPrompt(string? raw) => SanitizeQuestionPrompt(raw);

    public static string SanitizeOptionText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var text = raw.Trim();
        if (OptionPlaceholderPattern.IsMatch(text)) return string.Empty;
        if (SentinelPattern.IsMatch(text)) return string.Empty;

        var lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        var kept = new List<string>();
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (string.IsNullOrWhiteSpace(t)) continue;
            if (SentinelPattern.IsMatch(t)) continue;
            if (OptionPlaceholderPattern.IsMatch(t)) continue;
            var isArtifact = false;
            foreach (var pattern in ArtifactLinePatterns)
            {
                if (pattern.IsMatch(t))
                {
                    isArtifact = true;
                    break;
                }
            }
            if (!isArtifact) kept.Add(t);
        }

        if (kept.Count == 0) return string.Empty;

        text = string.Join(" ", kept).Trim();
        foreach (var pattern in InlineArtifactPatterns)
        {
            text = pattern.Replace(text, string.Empty).Trim();
        }

        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*={2,}\s*PAGE\s*\d+\s*={2,}\s*", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*-{2,}\s*PAGE\s*\d+\s*-{2,}\s*", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*PAGE\s*\d+\s*", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*o\s*Practice Test\s*\d+\s*:?\s*", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*Practice Test\s*\d+\s*:?\s*", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        text = text.Replace("\uF0B7", " ");

        text = MultipleWhitespacePattern.Replace(text, " ").Trim();
        if (OptionPlaceholderPattern.IsMatch(text)) return string.Empty;
        if (SentinelPattern.IsMatch(text)) return string.Empty;
        return text;
    }

    public static string CleanListeningOption(string? raw) => SanitizeOptionText(raw);

    private static string NormalizeDrillId(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized)) return "listening-drill-detail_capture";
        return normalized.StartsWith("listening-drill-", StringComparison.Ordinal) ? normalized : $"listening-drill-{normalized}";
    }

    private static ListeningScoreDto ResolveScoreFromEvaluation(Evaluation? evaluation, bool requiresAdminReview = false)
    {
        if (evaluation is not null)
        {
            if (evaluation.RawScore is int persistedRaw)
            {
                var conversionMaxRaw = evaluation.MaxRawScore ?? 0;
                var persistedMax = conversionMaxRaw > 0 ? conversionMaxRaw : CanonicalRawMax;
                var hasApprovedConversion = !requiresAdminReview
                    && HasApprovedScoreConversion(evaluation.ScoreConversionTableVersionKey, evaluation.ScaledScore, evaluation.ScoreConversionPassed, conversionMaxRaw);
                return new ListeningScoreDto(
                    Math.Clamp(persistedRaw, 0, persistedMax),
                    persistedMax,
                    hasApprovedConversion ? evaluation.ScaledScore : null,
                    hasApprovedConversion ? evaluation.ScoreConversionGrade ?? "—" : "—",
                    hasApprovedConversion ? evaluation.ScoreConversionPassed : null);
            }
            var rows = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.CriterionScoresJson, []);
            var row = rows.FirstOrDefault();
            var raw = ReadInt(row?.GetValueOrDefault("rawScore"));
            var maxRaw = ReadInt(row?.GetValueOrDefault("maxRawScore"));
            var scaled = ReadInt(row?.GetValueOrDefault("scaledScore"));
            if (raw.HasValue || scaled.HasValue)
            {
                var conversionMaxRaw = maxRaw ?? 0;
                var maxRawValue = conversionMaxRaw > 0 ? conversionMaxRaw : CanonicalRawMax;
                var rawValue = Math.Clamp(raw ?? 0, 0, maxRawValue);
                var hasApprovedConversion = !requiresAdminReview
                    && HasApprovedScoreConversion(evaluation.ScoreConversionTableVersionKey, scaled, evaluation.ScoreConversionPassed, conversionMaxRaw);
                var approvedScaled = hasApprovedConversion ? scaled : null;
                var grade = hasApprovedConversion ? evaluation.ScoreConversionGrade ?? "—" : "—";
                var passed = hasApprovedConversion ? evaluation.ScoreConversionPassed : null;
                return new ListeningScoreDto(
                    rawValue,
                    maxRawValue,
                    approvedScaled,
                    grade,
                    passed);
            }
        }

        return new ListeningScoreDto(0, CanonicalRawMax, null, "—", null);
    }

    private static ListeningScoreDto ApplyScoreConversionGate(ListeningScoreDto score, string? scoreConversionTableVersionKey)
    {
        if (HasApprovedScoreConversion(scoreConversionTableVersionKey, score.ScaledScore, score.Passed, score.MaxRawScore))
            return score;

        return new ListeningScoreDto(score.RawScore, score.MaxRawScore, null, "—", null);
    }

    private static bool HasApprovedScoreConversion(string? scoreConversionTableVersionKey, int? scaledScore, bool? passed, int maxRawScore)
        => maxRawScore == CanonicalRawMax
            && scaledScore.HasValue
            && !string.IsNullOrWhiteSpace(scoreConversionTableVersionKey)
            && passed.HasValue;

    private static ListeningScoreDto ResolveScoreFromRelationalAttempt(ListeningAttempt attempt, Evaluation? evaluation)
    {
        if (evaluation is not null)
        {
            return ResolveScoreFromEvaluation(evaluation, attempt.RequiresAdminReview);
        }

        var rawValue = Math.Clamp(attempt.RawScore ?? 0, 0, CanonicalRawMax);
        var hasApprovedConversion = !attempt.RequiresAdminReview
            && HasApprovedScoreConversion(attempt.ScoreConversionTableVersionKey, attempt.ScaledScore, attempt.ScoreConversionPassed, attempt.MaxRawScore);
        var scaledValue = hasApprovedConversion ? attempt.ScaledScore : null;
        var grade = hasApprovedConversion ? attempt.ScoreConversionGrade ?? "—" : "—";
        var passed = hasApprovedConversion ? attempt.ScoreConversionPassed : null;
        return new ListeningScoreDto(
            rawValue,
            attempt.MaxRawScore > 0 ? attempt.MaxRawScore : CanonicalRawMax,
            scaledValue,
            grade,
            passed);
    }

    private async Task<Attempt> GetAttemptOwnedByUserAsync(string userId, string attemptId, CancellationToken ct)
    {
        var attempt = await db.Attempts.FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId && a.SubtestCode == Subtest, ct)
            ?? throw ApiException.NotFound("listening_attempt_not_found", "Listening attempt not found.");
        return attempt;
    }

    private async Task EnsureLearnerAsync(string userId, CancellationToken ct)
    {
        _ = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw ApiException.NotFound("learner_not_found", "Learner profile not found.");
    }

    private async Task EnsureLearnerMutationAllowedAsync(string userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw ApiException.NotFound("learner_not_found", "Learner profile not found.");
        if (string.Equals(user.AccountStatus, "suspended", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Forbidden("account_suspended", "Your account is suspended and cannot start or submit Listening attempts.");
        }
    }

    /// <summary>WS2 — true when the learner has a pathway sound-check that
    /// passed within <see cref="ListeningSessionService.AudioCheckTtlMs"/>.
    /// Reads <see cref="LearnerListeningProfile.AudioCheckPassedAt"/>, the same
    /// row the Listening pathway flow stamps on a passed check. A learner
    /// with no Listening profile (never onboarded the pathway) fails closed so
    /// strict exams cannot be started without a check.</summary>
    private async Task<bool> HasValidAudioCheckAsync(string userId, DateTimeOffset now, CancellationToken ct)
    {
        var passedAt = await db.LearnerListeningProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.AudioCheckPassedAt)
            .FirstOrDefaultAsync(ct);

        return passedAt is { } at && at.AddMilliseconds(ListeningSessionService.AudioCheckTtlMs) >= now;
    }

    private static Task EnsureGenericAttemptCanMutateAsync(
        Attempt attempt,
        CancellationToken ct)
    {
        EnsureAttemptNotOnAdminReviewHold(attempt.RequiresAdminReview, attempt.AdminReviewReason);
        if (attempt.State == AttemptState.Completed)
        {
            throw ApiException.Conflict("listening_attempt_locked", "This Listening attempt has already been submitted.");
        }

        var deadlineAt = ReadGenericDeadline(attempt);
        if (deadlineAt is DateTimeOffset deadline && DateTimeOffset.UtcNow > deadline)
        {
            throw ApiException.Validation(
                "listening_attempt_deadline_passed",
                "This Listening attempt deadline has passed and answers can no longer be changed.");
        }
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private static void EnsureAttemptNotOnAdminReviewHold(bool requiresAdminReview, string? reason = null)
    {
        if (requiresAdminReview)
        {
            var reviewReason = string.IsNullOrWhiteSpace(reason)
                ? "review_required"
                : reason;
            throw ApiException.Conflict(
                "listening_attempt_requires_admin_review",
                $"This Listening attempt requires administrator review before scoring. Reason: {reviewReason}.");
        }
    }

    private static DateTimeOffset? ReadGenericDeadline(Attempt attempt)
    {
        if (string.IsNullOrWhiteSpace(attempt.PolicySnapshotJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(attempt.PolicySnapshotJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("deadlineAt", out var deadline)
                && deadline.ValueKind == JsonValueKind.String
                && deadline.TryGetDateTimeOffset(out var parsed)
                    ? parsed
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, string?> DeserializeAnswers(string json)
        => JsonSupport.Deserialize<Dictionary<string, string?>>(json, new Dictionary<string, string?>());

    private async Task<ListeningAudioTransportPolicy> ResolveCurrentAudioTransportPolicyAsync(
        string mode,
        CancellationToken ct)
    {
        var resolver = markingPolicyService ?? new AssessmentMarkingPolicyService(db);
        var resolution = await resolver.ResolveAsync("listening", "default", cancellationToken: ct);
        var listeningPolicy = listeningPolicyService is not null
            ? await listeningPolicyService.GetGlobalAsync(ct)
            : await db.ListeningPolicies.AsNoTracking().FirstOrDefaultAsync(row => row.Id == "global", ct);
        return resolution.IsAvailable && resolution.ErrorCode is null
            ? ListeningAudioTransportPolicy.FromPolicy(
                mode,
                resolution.Document,
                listeningPolicy?.LearningReplayAllowed)
            : ListeningAudioTransportPolicy.Strict;
    }

    private async Task<IReadOnlyList<int>> ResolveCountdownWarningsForSessionAsync(
        string userId,
        string? policySnapshotJson,
        CancellationToken ct)
    {
        if (TryReadCapturedCountdownWarnings(policySnapshotJson, out var captured))
            return captured;

        var (policy, _) = await ResolveListeningPolicyAsync(userId, ct);
        return ListeningPolicyService.ParseCountdownWarnings(policy.CountdownWarningsJson);
    }

    private static bool TryReadCapturedCountdownWarnings(
        string? policySnapshotJson,
        out IReadOnlyList<int> warnings)
    {
        warnings = Array.Empty<int>();
        if (string.IsNullOrWhiteSpace(policySnapshotJson)) return false;

        try
        {
            using var document = JsonDocument.Parse(policySnapshotJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var container = root.TryGetProperty("listeningPolicy", out var nested)
                && nested.ValueKind == JsonValueKind.Object
                ? nested
                : root;
            if (!container.TryGetProperty("countdownWarningsSeconds", out var value)) return false;
            if (value.ValueKind != JsonValueKind.Array)
                return true;

            warnings = ListeningPolicyService.ParseCountdownWarnings(value.GetRawText());
            return true;
        }
        catch (JsonException)
        {
            return policySnapshotJson.Contains(
                "countdownWarningsSeconds", StringComparison.Ordinal);
        }
    }

    private static bool HasAnsweredValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using var doc = JsonDocument.Parse(value);
            return HasAnsweredJsonValue(doc.RootElement);
        }
        catch (JsonException)
        {
            return !string.IsNullOrWhiteSpace(value);
        }
    }

    private static bool HasAnsweredJsonValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => !string.IsNullOrWhiteSpace(element.GetString()),
            JsonValueKind.Number => true,
            JsonValueKind.True => true,
            JsonValueKind.False => true,
            JsonValueKind.Array => element.EnumerateArray().Any(HasAnsweredJsonValue),
            JsonValueKind.Object => element.EnumerateObject().Any(property => HasAnsweredJsonValue(property.Value)),
            _ => false
        };

    /// <summary>
    /// Phase 9 tail: accept the learner-visible Listening modes.
    ///
    ///   <c>practice</c> — default. Navigation is permissive, while the
    ///                     current owner audio policy remains non-pausable.
    ///   <c>exam</c>     — one-play, no scrub, no pause. Standard CBT-style.
    ///   <c>home</c>     — OET@Home guidance skin; fullscreen/focus are
    ///                     advisory, while timer + one-play behave like
    ///                     <c>exam</c>.
    ///   <c>diagnostic</c> — fixed-form placement attempt for the pathway.
    ///
    /// Anything else collapses to <c>practice</c> so a malformed query
    /// param can never accidentally promote a learner into a high-stakes
    /// integrity flow.
    /// </summary>
    private static string NormalizeMode(string? mode)
    {
        var normalized = (mode ?? "practice").Trim().ToLowerInvariant();
        if (normalized == "paper")
        {
            throw PaperModeDisabled();
        }

        return normalized switch
        {
            "exam" => "exam",
            "home" => "home",
            "diagnostic" => "diagnostic",
            _ => "practice",
        };
    }

    private static string? NormalizePathwayStage(string? stage)
    {
        if (string.IsNullOrWhiteSpace(stage)) return null;

        var normalized = stage.Trim();
        if (ListeningPathwayProgressService.PathwayStages.Contains(normalized, StringComparer.Ordinal))
        {
            return normalized;
        }

        throw ApiException.Validation(
            "listening_pathway_stage_invalid",
            "The requested Listening pathway stage is not supported.");
    }

    /// <summary>True for any mode that is graded under exam-integrity rules
    /// (one-play, no pause, no scrub). <c>practice</c> is the only non-exam
    /// mode.</summary>
    private static bool IsExamMode(string normalizedMode)
        => normalizedMode is "exam" or "home" or "diagnostic";

    private static ApiException PaperModeDisabled()
        => ApiException.Validation(
            "listening_paper_mode_disabled",
            "Listening is computer-based only; paper simulation is not supported.");

    private static object PartPracticeStartedDto(
        ListeningAttempt attempt,
        ListeningSource source,
        string partCode,
        IReadOnlyDictionary<string, string?> answers,
        string? feedbackMessage = null)
    {
        _ = answers;
        var minutes = ListeningAttemptScope.ReadPartPractice(attempt.ScopeJson).Minutes;
        if (minutes <= 0) minutes = PartPracticeMinutes(partCode);
        return new
        {
            attemptId = attempt.Id,
            playerRoute = RelationalAttemptRoute(attempt),
            questionCount = source.Questions.Count,
            minutes,
            partPractice = new { partCode, title = $"Part {partCode}" },
            feedbackMessage,
        };
    }

    private static ListeningSource ApplyAttemptScope(ListeningSource source, ListeningAttempt? attempt)
    {
        if (attempt is null) return source;
        var partPractice = ListeningAttemptScope.ReadPartPractice(attempt.ScopeJson);
        if (!partPractice.IsValid) return source;

        var scoped = ApplyQuestionScope(source, partPractice.QuestionIds);
        var parent = partPractice.PartCode!.Trim().ToUpperInvariant();
        var expectedPartCount = parent switch
        {
            "B" => 6,
            "C" => 12,
            "A" => 24,
            _ => 0,
        };
        var sourceParentCount = source.Questions.Count(q =>
            string.Equals(ListeningParentPartFromCode(q.PartCode), parent, StringComparison.OrdinalIgnoreCase));
        // A part-practice scope is defined as the complete part. If its stored
        // IDs pre-date a source backfill (or were partially persisted), keeping
        // the matching subset would make Full/standalone B or C appear to have
        // one question. Rebind the scope by parent part so the normalized source
        // can expose all authored items while retaining the existing attempt.
        if (source.Questions.Count > 0
            && (scoped.Questions.Count == 0
                || (expectedPartCount > 0
                    && sourceParentCount >= expectedPartCount
                    && scoped.Questions.Count < expectedPartCount)))
        {
            // Fallback: stored questionIds are stale (random GUIDs regenerated on
            // backfill after the attempt was created). Recover by parent part
            // so part-only review still shows the correct transcript/audio/answers
            // instead of collapsing to an empty paper.
            var fallbackIds = source.Questions
                .Where(q => string.Equals(ListeningParentPartFromCode(q.PartCode), parent, StringComparison.OrdinalIgnoreCase))
                .Select(q => q.Id)
                .ToList();
            if (fallbackIds.Count > 0)
            {
                return ApplyQuestionScope(source, fallbackIds);
            }
        }
        return scoped;
    }

    private static ListeningSource ApplyQuestionScope(ListeningSource source, IReadOnlyCollection<string> questionIds)
    {
        var allowed = questionIds.ToHashSet(StringComparer.Ordinal);
        var questions = source.Questions.Where(q => allowed.Contains(q.Id)).ToList();
        var allowedParts = questions
            .Select(q => NormalizePartCode(q.PartCode) ?? q.PartCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowedParentParts = questions
            .Select(q => ListeningParentPartFromCode(q.PartCode))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extracts = source.Extracts
            .Where(extract => allowedParts.Contains(extract.PartCode)
                || allowedParts.Contains(NormalizePartCode(extract.PartCode) ?? string.Empty)
                || (ListeningParentPartFromCode(extract.PartCode) == "B"
                    && questions.Any(q => ListeningParentPartFromCode(q.PartCode) == "B")))
            .ToList();
        var isFullPaper = allowedParentParts.Count == 3;
        // Filter transcript segments to only the submitted parent parts (A/B/C).
        // This enforces requirement 1: Part A practice shows only Part A script,
        // Full exam shows A+B+C, and hidden parts never leak via transcriptSegments.
        var transcriptSegments = source.TranscriptSegments
            .Where(seg =>
            {
                if (string.IsNullOrWhiteSpace(seg.PartCode))
                    // Unauthored segments: show for full-paper review only; hide for
                    // scoped practice to avoid leaking other parts.
                    return isFullPaper;
                var parent = ListeningParentPartFromCode(seg.PartCode);
                return allowedParentParts.Contains(parent);
            })
            .ToList();
        // Filter per-section audio + question-paper maps to submitted parts only.
        IReadOnlyDictionary<string, string>? audioByPart = null;
        if (source.AudioUrlByPart is not null)
        {
            var filtered = source.AudioUrlByPart
                .Where(kv => allowedParentParts.Contains(ListeningParentPartFromCode(kv.Key))
                    || allowedParts.Contains(kv.Key.Trim().ToUpperInvariant()))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            audioByPart = filtered.Count > 0 ? filtered : null;
        }
        IReadOnlyDictionary<string, string>? questionPaperByPart = null;
        if (source.QuestionPaperUrlByPart is not null)
        {
            var filteredQp = source.QuestionPaperUrlByPart
                .Where(kv => allowedParentParts.Contains(ListeningParentPartFromCode(kv.Key))
                    || allowedParts.Contains(kv.Key.Trim().ToUpperInvariant()))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            questionPaperByPart = filteredQp.Count > 0 ? filteredQp : null;
        }
        // For scoped practice, hide the whole-paper AudioScript PDF and the legacy
        // combined AudioUrl which would otherwise expose the full transcript / full
        // audio of non-submitted parts.
        var audioScriptUrl = isFullPaper ? source.AudioScriptUrl : null;
        var audioUrl = isFullPaper ? source.AudioUrl : null;
        return source with
        {
            Questions = questions,
            Extracts = extracts,
            TranscriptSegments = transcriptSegments,
            AudioUrl = audioUrl,
            AudioUrlByPart = audioByPart is not null ? audioByPart : (isFullPaper ? source.AudioUrlByPart : new Dictionary<string, string>(StringComparer.Ordinal)),
            QuestionPaperUrlByPart = questionPaperByPart is not null ? questionPaperByPart : (isFullPaper ? source.QuestionPaperUrlByPart : new Dictionary<string, string>(StringComparer.Ordinal)),
            AudioScriptUrl = audioScriptUrl
        };
    }

    private static string ListeningParentPart(ListeningPartCode partCode)
        => partCode switch
        {
            ListeningPartCode.A1 or ListeningPartCode.A2 => "A",
            ListeningPartCode.C1 or ListeningPartCode.C2 => "C",
            _ => "B",
        };

    private static string ListeningParentPartFromCode(string? partCode)
    {
        var normalized = (partCode ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.StartsWith('A')) return "A";
        if (normalized.StartsWith('C')) return "C";
        return "B";
    }

    private static string? NormalizeListeningParentPart(string? partCode)
    {
        var normalized = partCode?.Trim().ToUpperInvariant();
        return normalized is "A" or "B" or "C" ? normalized : null;
    }

    private static int PartPracticeMinutes(string partCode) => partCode.ToUpperInvariant() switch
    {
        "A" => 15,
        "B" => 12,
        "C" => 15,
        _ => 15,
    };

    private static string RelationalAttemptRoute(ListeningAttempt attempt)
    {
        var mode = ToApiMode(attempt.Mode);
        var partPractice = ListeningAttemptScope.ReadPartPractice(attempt.ScopeJson);
        if (partPractice.IsValid)
        {
            var part = partPractice.PartCode!;
            var focus = part.ToLowerInvariant() switch
            {
                "a" => "part-a",
                "b" => "part-b",
                "c" => "part-c",
                _ => "part-a",
            };
            return $"/listening/player/{Uri.EscapeDataString(attempt.PaperId)}?attemptId={Uri.EscapeDataString(attempt.Id)}&mode=practice&part={Uri.EscapeDataString(part)}&focus={focus}";
        }

        if (mode is "exam" or "home")
        {
            return $"/listening/paper/{Uri.EscapeDataString(attempt.PaperId)}?attemptId={Uri.EscapeDataString(attempt.Id)}";
        }

        var route = $"/listening/player/{Uri.EscapeDataString(attempt.PaperId)}?attemptId={Uri.EscapeDataString(attempt.Id)}&mode={Uri.EscapeDataString(mode)}";
        var scopedStage = ListeningAttemptScope.ReadPathwayStage(attempt.ScopeJson);
        return scopedStage.HasScope && !string.IsNullOrWhiteSpace(scopedStage.Stage)
            ? $"{route}&pathwayStage={Uri.EscapeDataString(scopedStage.Stage)}"
            : route;
    }

    private static string BuildListeningHistoryPracticeRoute(string mode, bool isPartPractice, string? partCode)
    {
        if (isPartPractice && partCode is not null)
        {
            return $"/listening/practice/{Uri.EscapeDataString(partCode.ToLowerInvariant())}";
        }

        return mode is "exam" or "home" ? "/listening/exam" : "/listening/practice";
    }

    private static string? AssetDownloadPath(ContentPaperAsset? asset)
        => asset?.MediaAsset is null ? null : $"/v1/media/{asset.MediaAsset.Id}/content";

    private static bool MatchesObjectiveAnswer(string? learnerAnswer, string? correctAnswer)
        => ListeningGradingService.StringsMatch(
            learnerAnswer ?? string.Empty,
            correctAnswer ?? string.Empty,
            caseSensitive: true,
            normalisation: ListeningGradingService.DefaultNormalisation);

    private static string NormalizeObjectiveAnswer(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var filtered = new string(value.Trim().ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)).ToArray());
        return System.Text.RegularExpressions.Regex.Replace(filtered, @"\s+", " ");
    }

    /// <summary>
    /// Categorise a wrong answer into one of the spec-defined error types.
    /// Part A (gap fill / short_answer): we run a smart classification on the
    /// learner answer vs the correct answer, with `wrong_section` detected by
    /// checking whether the learner's answer matches any *other* question's
    /// correct answer in the same paper.
    /// Part B / Part C (MCQ): always "distractor_confusion" unless empty.
    /// </summary>
    private static string? ObjectiveErrorType(
        ListeningQuestion question,
        string? learnerAnswer,
        IReadOnlyList<ListeningQuestion> allQuestions)
    {
        var trimmed = (learnerAnswer ?? string.Empty).Trim();
        if (trimmed.Length == 0) return "empty";

        var partUpper = (question.PartCode ?? "A").ToUpperInvariant();
        var isShortAnswer = string.Equals(question.Type, "short_answer", StringComparison.OrdinalIgnoreCase)
            || partUpper.StartsWith("A", StringComparison.Ordinal);

        if (!isShortAnswer)
        {
            return "distractor_confusion";
        }

        var normalizedLearner = NormalizeObjectiveAnswer(trimmed);
        var normalizedCorrect = NormalizeObjectiveAnswer(question.CorrectAnswer);

        if (normalizedLearner.Length == 0) return "empty";
        if (normalizedCorrect.Length == 0) return "detail_capture";

        // Wrong-section: did the learner type the correct answer for a *different* question?
        var matchedAnotherQuestion = allQuestions.Any(other =>
            other.Id != question.Id
            && (other.PartCode ?? string.Empty).StartsWith("A", StringComparison.OrdinalIgnoreCase)
            && other.AcceptedAnswers.Any(ans => MatchesObjectiveAnswer(trimmed, ans)));
        if (matchedAnotherQuestion) return "wrong_section";

        // Grammar / number: differs only by trailing 's' or 'es' (singular/plural)
        // or only by an article (a/an/the).
        if (DiffersByPluralOrArticle(normalizedLearner, normalizedCorrect))
            return "grammar_number";

        // Extra info: learner answer contains the full correct answer plus extra tokens.
        var learnerTokens = normalizedLearner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var correctTokens = normalizedCorrect.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (learnerTokens.Length > correctTokens.Length
            && correctTokens.All(t => learnerTokens.Contains(t)))
        {
            return "extra_info";
        }

        // Spelling: small Levenshtein distance (≤ 2 edits, or ≤ 25% of length).
        var distance = LevenshteinDistance(normalizedLearner, normalizedCorrect);
        var threshold = Math.Max(2, normalizedCorrect.Length / 4);
        if (distance > 0 && distance <= threshold) return "spelling";

        // Fallback: very different word — treat as paraphrase (learner heard the
        // meaning but wrote their own words) when token sets barely overlap.
        var overlap = correctTokens.Count(t => learnerTokens.Contains(t));
        if (overlap == 0 && learnerTokens.Length > 0) return "paraphrase";

        return "detail_capture";
    }

    /// <summary>True when the only difference is a trailing 's'/'es' or a leading article.</summary>
    private static bool DiffersByPluralOrArticle(string learner, string correct)
    {
        if (string.Equals(learner, correct, StringComparison.Ordinal)) return false;

        // Articles
        string Strip(string s)
        {
            foreach (var article in new[] { "the ", "a ", "an " })
            {
                if (s.StartsWith(article, StringComparison.Ordinal)) return s[article.Length..];
            }
            return s;
        }
        var l = Strip(learner);
        var c = Strip(correct);
        if (string.Equals(l, c, StringComparison.Ordinal)) return true;

        // Plural
        bool PluralEqual(string a, string b)
        {
            if (a.Length > b.Length && (a.EndsWith("s", StringComparison.Ordinal) || a.EndsWith("es", StringComparison.Ordinal)))
            {
                var trimmed = a.EndsWith("es", StringComparison.Ordinal) ? a[..^2] : a[..^1];
                if (string.Equals(trimmed, b, StringComparison.Ordinal)) return true;
            }
            return false;
        }
        return PluralEqual(l, c) || PluralEqual(c, l);
    }

    private static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        if (a.Length > 64 || b.Length > 64) return int.MaxValue; // cap
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    private static string ObjectiveErrorTypeLabel(string? errorType) => errorType switch
    {
        "spelling" => "Spelling",
        "grammar_number" => "Grammar / number",
        "paraphrase" => "Paraphrase (use exact words from audio)",
        "wrong_section" => "Right answer, wrong gap",
        "extra_info" => "Extra information",
        "empty" => "Unanswered",
        "distractor_confusion" => "Distractor confusion",
        "numbers_and_frequencies" => "Numbers and frequencies",
        "detail_capture" => "Exact detail capture",
        _ => "Accuracy"
    };

    private static string FormatScoreDisplay(ListeningScoreDto score)
        => score.ScaledScore is int scaled
            ? $"{score.RawScore} / {score.MaxRawScore} \u2022 {scaled} / 500 \u2022 Grade {score.Grade}"
            : $"{score.RawScore} / {score.MaxRawScore} \u2022 scaled score unavailable";

    private static string NormalizeBestScoreDisplay(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "best" => "best",
            "latest" => "latest",
            "average" => "average",
            "first" => "first",
            _ => "latest",
        };

    private static string? SelectProgressScoreDisplay(
        IReadOnlyList<ListeningHomeResultProjection> results,
        string? configuredMode)
    {
        if (results.Count == 0) return null;

        var mode = NormalizeBestScoreDisplay(configuredMode);
        if (mode == "average")
        {
            var averageRaw = results.Average(result => result.rawScore);
            var averageMax = results.Average(result => result.maxRawScore);
            var converted = results
                .Where(result => result.scaledScore.HasValue)
                .Select(result => result.scaledScore!.Value)
                .ToList();
            var average = converted.Count > 0
                ? $"{averageRaw:0.#} / {averageMax:0.#} \u2022 {converted.Average():0.#} / 500 average"
                : $"{averageRaw:0.#} / {averageMax:0.#} average \u2022 scaled score unavailable";
            return average;
        }

        var selected = mode switch
        {
            "first" => results.OrderBy(result => result.submittedAt ?? DateTimeOffset.MaxValue).First(),
            "best" => results
                .OrderByDescending(ProgressScoreRank)
                .ThenByDescending(result => result.submittedAt)
                .First(),
            _ => results[0],
        };
        return selected.scoreDisplay;
    }

    private static decimal ProgressScoreRank(ListeningHomeResultProjection result)
        => result.scaledScore
            // Raw score is the only honest fallback for ordering when the
            // owner-approved conversion table is unavailable. Never derive a
            // synthetic 0-500 value for ranking or display.
            ?? result.rawScore;

    private static string ToApiState(AttemptState state) => state switch
    {
        AttemptState.NotStarted => "not_started",
        AttemptState.InProgress => "in_progress",
        AttemptState.Paused => "paused",
        AttemptState.Submitted => "submitted",
        AttemptState.Evaluating => "evaluating",
        AttemptState.Completed => "completed",
        AttemptState.Failed => "failed",
        AttemptState.Abandoned => "abandoned",
        _ => "unknown"
    };

    private static string ToApiState(ListeningAttemptStatus status) => status switch
    {
        ListeningAttemptStatus.InProgress => "in_progress",
        ListeningAttemptStatus.Submitted => "submitted",
        ListeningAttemptStatus.Expired => "failed",
        ListeningAttemptStatus.Abandoned => "abandoned",
        _ => "unknown"
    };

    private static string? ReadString(object? value) => value switch
    {
        null => null,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        JsonElement { ValueKind: JsonValueKind.Null } => null,
        _ => value.ToString()
    };

    private static int? ReadInt(object? value) => value switch
    {
        null => null,
        int number => number,
        long number => (int)number,
        JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var parsed) => parsed,
        JsonElement { ValueKind: JsonValueKind.String } element when int.TryParse(element.GetString(), out var parsed) => parsed,
        _ when int.TryParse(value.ToString(), out var parsed) => parsed,
        _ => null
    };

    private static bool? ReadBool(object? value) => value switch
    {
        null => null,
        bool boolean => boolean,
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.False } => false,
        JsonElement { ValueKind: JsonValueKind.String } element when bool.TryParse(element.GetString(), out var parsed) => parsed,
        _ when bool.TryParse(value.ToString(), out var parsed) => parsed,
        _ => null
    };

    private static string? ReadAnswer(object? value)
    {
        if (value is null) return null;
        if (value is string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                using var doc = JsonDocument.Parse(text);
                return doc.RootElement.ValueKind == JsonValueKind.String ? doc.RootElement.GetString() : text;
            }
            catch (JsonException)
            {
                return text;
            }
        }

        if (value is JsonElement { ValueKind: JsonValueKind.String } element) return element.GetString();
        return ReadString(value);
    }

    private static List<string>? ReadStringList(object? value)
    {
        if (value is null) return null;
        if (value is JsonElement { ValueKind: JsonValueKind.Array } element)
        {
            return element.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToList();
        }

        if (value is JsonElement { ValueKind: JsonValueKind.String } stringElement)
        {
            return ReadStringList(stringElement.GetString());
        }

        if (value is string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return [];
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    return doc.RootElement.EnumerateArray()
                        .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Cast<string>()
                        .ToList();
                }
            }
            catch (JsonException)
            {
                return [text];
            }
        }

        if (value is IEnumerable<string> strings) return strings.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
        if (value is IEnumerable<object?> objects) return objects.Select(ReadString).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToList();
        return null;
    }

    private sealed record ListeningSource(
        string Id,
        string SourceKind,
        string Title,
        string Slug,
        string Difficulty,
        int EstimatedDurationMinutes,
        string ScenarioType,
        string? AudioUrl,
        string? QuestionPaperUrl,
        string? AnswerKeyUrl,
        string? AudioScriptUrl,
        IReadOnlyList<ListeningQuestion> Questions,
        ListeningAssetReadiness AssetReadiness,
        // Phase 5: paper-level time-coded transcript segments. Empty list when
        // the authored paper has no segment metadata yet.
        IReadOnlyList<ListeningTranscriptSegmentDto> TranscriptSegments,
        // Phase 5 tail: paper-level extract metadata (accent + speakers +
        // audio window + extract kind/title). One row per extract: A1, A2,
        // B (one per workplace clip), C1, C2. Empty list when not authored.
        IReadOnlyList<ListeningExtractMetaDto> Extracts,
        // Authored ContentPaper runtime prefers normalized Listening tables;
        // legacy JSON remains a fallback for papers not backfilled yet.
        bool UsesRelationalStructure,
        // Per-part learner-facing QuestionPaper PDF URLs, keyed by uppercased
        // part/section code (A | B | C and overrides A1/A2 | B1..B6 | C1/C2).
        // Mirrors the Reading module's per-part question paper. Empty when no
        // per-part QuestionPaper assets are attached. The player resolves the
        // current section to a URL (exact section code → parent part fallback).
        IReadOnlyDictionary<string, string>? QuestionPaperUrlByPart = null,
        // Per-section audio URLs keyed by the five learner sections (A1, A2, B,
        // C1, C2). Part B plays one shared file across its sub-parts; every other
        // section plays its own. The exam player loads audioUrlByPart[section]
        // (falling back to the legacy combined AudioUrl). Empty when no per-section
        // audio is attached and the paper relies on the combined AudioUrl.
        IReadOnlyDictionary<string, string>? AudioUrlByPart = null,
        string? PaperRevisionId = null);

    private sealed record ListeningExtractMetaDto(
        string PartCode,                     // A1 | A2 | B1..B6 | C1 | C2
        int DisplayOrder,
        string Kind,                          // consultation | workplace | presentation
        string Title,
        string? AccentCode,                   // e.g. en-GB | en-AU | en-IE | en-US
        IReadOnlyList<ListeningSpeakerDto> Speakers,
        int? AudioStartMs,
        int? AudioEndMs,
        // Per-sub-section audio URL the player should load for this section.
        // Resolution order: uploaded ContentPaperAsset(Audio, partCode) →
        // /v1/media/{id}/content; else the extract's TTS wav; else null.
        string? AudioUrl = null,
        // Per-sub-section countdown (seconds). Null → frontend applies default.
        int? TimeLimitSeconds = null,
        // Part A note-completion document (camelCase `notesBody` on the wire).
        // Null for Part B/C and for papers without an authored body.
        string? NotesBody = null,
        // Phase 6: Part A authoring method ("wysiwyg" default/null | "pdf_overlay")
        // and, for pdf_overlay, the normalized blank placements over the
        // question-paper PDF (the player renders inputs at these coordinates).
        string? AuthoringMethod = null,
        string? PartAOverlayBlanksJson = null,
        // Part B/C printed scenario/intro line, rendered once per extract above
        // the question cards so the question-paper PDF can be dropped.
        string? ContextIntro = null);

    private sealed record ListeningSpeakerDto(
        string Id,
        string Role,                          // e.g. doctor | patient | nurse | presenter
        string? Gender,                       // m | f | nb | null
        string? Accent);                      // optional override of extract accentCode

    // These response graph records are internal so the submit idempotency
    // cache can round-trip an exact review without weakening the learner API
    // surface or exposing answer-key types as public contracts.
    internal sealed record ListeningTranscriptSegmentDto(
        int StartMs,
        int EndMs,
        string? PartCode,        // optional: A1 | A2 | B | C1 | C2
        string? SpeakerId,        // optional: free-form (e.g. "doctor", "patient", "presenter")
        string Text);

    private sealed record ListeningQuestion(
        string Id,
        int Number,
        string PartCode,
        string Text,
        string Type,
        List<string> Options,
        string CorrectAnswer,
        List<string> AcceptedAnswers,
        string? Explanation,
        string? SkillTag,
        bool AllowTranscriptReveal,
        string? TranscriptExcerpt,
        string? DistractorExplanation,
        int Points,
        // Phase 4: per-option "why wrong" explanation (Part B/C). Same length as
        // Options when populated; missing entries fall back to DistractorExplanation.
        IReadOnlyList<string?> OptionDistractorWhy,
        // Phase 4: per-option distractor category enum: too_strong | too_weak |
        // wrong_speaker | opposite_meaning | reused_keyword | out_of_scope. Same length as Options.
        IReadOnlyList<string?> OptionDistractorCategory,
        // Phase 4: speaker attitude on Part C: concerned | optimistic | doubtful |
        // critical | neutral | other. Null on Part A/B.
        string? SpeakerAttitude,
        // Phase 5: time-coded transcript evidence (start/end ms in the section audio).
        int? TranscriptEvidenceStartMs,
        int? TranscriptEvidenceEndMs);

    private sealed record LegacyMultipleSelectionIntegrityIssue(
        string QuestionId,
        int QuestionNumber,
        IReadOnlyList<string> Selections);

    private sealed record ListeningAssetReadiness(bool Audio, bool QuestionPaper, bool AnswerKey, bool AudioScript);

    private sealed record ListeningScoreDto(int RawScore, int MaxRawScore, int? ScaledScore, string Grade, bool? Passed);

    private sealed record ListeningHomeResultProjection(
        string attemptId,
        string paperId,
        string paperTitle,
        int rawScore,
        int maxRawScore,
        int? scaledScore,
        string grade,
        bool? passed,
        DateTimeOffset? submittedAt,
        string scoreDisplay,
        string route,
        string practiceRoute,
        string mode,
        string attemptKind,
        string? partCode,
        bool requiresAdminReview = false,
        string? adminReviewReason = null);

    internal sealed record ListeningTranscriptSnippetDto(bool Allowed, string? Excerpt, string? DistractorExplanation);

    internal sealed record ListeningReviewItemDto(
        string QuestionId,
        int Number,
        string PartCode,
        string Prompt,
        string Type,
        string LearnerAnswer,
        string CorrectAnswer,
        bool IsCorrect,
        bool IsInvalid,
        int PointsEarned,
        int MaxPoints,
        string? Explanation,
        string? ErrorType,
        IReadOnlyList<string> Options,
        ListeningTranscriptSnippetDto? Transcript,
        string? DistractorExplanation,
        // Phase 4: surface per-option distractor analysis to the learner.
        IReadOnlyList<ListeningOptionAnalysisDto>? OptionAnalysis,
        string? SpeakerAttitude,
        // Phase 5: time-coded transcript evidence (ms) so the learner UI can
        // jump directly to the proof segment in the section audio.
        int? TranscriptEvidenceStartMs,
        int? TranscriptEvidenceEndMs,
        ListeningHumanScoreOverrideDto? ScoreOverride,
        ListeningMissReason? MissReason = null);

    private sealed record ListeningReviewVisibility(
        bool ShowExplanationsAfterSubmit,
        bool ShowExplanationsOnlyIfWrong,
        bool ShowCorrectAnswerOnReview)
    {
        public static ListeningReviewVisibility LegacyDefault { get; } =
            new(true, false, true);

        public static ListeningReviewVisibility Strict { get; } =
            new(false, false, false);
    }

    private sealed record ListeningHumanScoreOverride(string QuestionId, int Override, string? By, string? Reason);

    internal sealed record ListeningHumanScoreOverrideDto(int Override, string Message);

    internal sealed record ListeningOptionAnalysisDto(
        string OptionLabel,                 // "A" | "B" | "C"
        string OptionText,
        bool IsCorrect,
        string? DistractorCategory,         // too_strong | too_weak | wrong_speaker | opposite_meaning | reused_keyword | out_of_scope
        string? WhyMarkdown);

    internal sealed record ListeningErrorClusterDto(string ErrorType, string Label, int Count, IReadOnlyList<string> AffectedQuestionIds);

    internal sealed record ListeningTranscriptAccessDto(string Policy, string State, IReadOnlyList<string> AllowedQuestionIds, string Reason);

    internal sealed record ListeningDrillDto(
        string DrillId,
        string Title,
        string FocusLabel,
        string Description,
        string ErrorType,
        int EstimatedMinutes,
        IReadOnlyList<string> Highlights,
        string LaunchRoute,
        string ReviewRoute);

    internal sealed record ListeningReviewDto(
        string? EvaluationId,
        string AttemptId,
        object Paper,
        int RawScore,
        int MaxRawScore,
        int? ScaledScore,
        string Grade,
        bool? Passed,
        string ScoreDisplay,
        int CorrectCount,
        int IncorrectCount,
        int UnansweredCount,
        IReadOnlyList<ListeningReviewItemDto> ItemReview,
        IReadOnlyList<ListeningErrorClusterDto> ErrorClusters,
        ListeningDrillDto? RecommendedNextDrill,
        ListeningTranscriptAccessDto TranscriptAccess,
        // Phase 5: paper-level time-coded transcript segments to power the
        // post-attempt review player's jump-to-evidence UI.
        IReadOnlyList<ListeningTranscriptSegmentDto> TranscriptSegments,
        IReadOnlyList<string> Strengths,
        IReadOnlyList<string> Issues,
        DateTimeOffset? GeneratedAt,
        string? ScoreConversionTableVersionKey = null,
        string? ScoreConversionErrorCode = null,
        ListeningTimeUsedDto? TimeUsed = null,
        int InvalidCount = 0,
        bool RequiresAdminReview = false,
        string? AdminReviewReason = null);

    internal sealed record ListeningTimeUsedDto(
        int? TotalMilliseconds,
        IReadOnlyList<ListeningTimeUsedSectionDto> Sections);

    internal sealed record ListeningTimeUsedSectionDto(
        string SectionCode,
        int? ElapsedMilliseconds);
}

public sealed record ListeningAnswerSaveRequest(string? UserAnswer);
public sealed record ListeningIntegrityEventRequest(string EventType, string? Details, DateTimeOffset? OccurredAt);
public sealed record ListeningAdvanceSectionRequest(int SectionCursor);
