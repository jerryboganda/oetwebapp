using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Assessment;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Services;

public partial class LearnerService
{

    public async Task<object> HeartbeatSpeakingAttemptAsync(string userId, string attemptId, HeartbeatRequest request, CancellationToken cancellationToken)
        => await HeartbeatSubtestAttemptAsync(userId, attemptId, request, "speaking", cancellationToken);

    public async Task<object> HeartbeatListeningAttemptAsync(string userId, string attemptId, HeartbeatRequest request, CancellationToken cancellationToken)
        => await HeartbeatSubtestAttemptAsync(userId, attemptId, request, "listening", cancellationToken);

    public async Task<object> HeartbeatWritingAttemptAsync(string userId, string attemptId, HeartbeatRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetWritingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        return await HeartbeatAttemptCoreAsync(attempt, request, cancellationToken);
    }

    private async Task<object> HeartbeatSubtestAttemptAsync(string userId, string attemptId, HeartbeatRequest request, string subtest, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetSubtestAttemptOwnedByUserAsync(userId, attemptId, subtest, $"{subtest}_attempt_not_found", $"{ToDisplaySubtest(subtest)} attempt not found.", cancellationToken);
        return await HeartbeatAttemptCoreAsync(attempt, request, cancellationToken);
    }

    private async Task<object> HeartbeatAttemptCoreAsync(Attempt attempt, HeartbeatRequest request, CancellationToken cancellationToken)
    {
        attempt.ElapsedSeconds = request.ElapsedSeconds;
        attempt.LastClientSyncAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.DeviceType)) attempt.DeviceType = request.DeviceType;
        await db.SaveChangesAsync(cancellationToken);
        return new { attemptId = attempt.Id, elapsedSeconds = attempt.ElapsedSeconds, lastClientSyncAt = attempt.LastClientSyncAt };
    }

    public async Task<object> GetListeningHomeAsync(CancellationToken cancellationToken)
    {
        // Listening is all-professions (handoff item 2 covers Writing/Speaking
        // only), so GetTasksBySubtestAsync never consults userId for this
        // subtest — the empty string is unused.
        var tasks = await GetTasksBySubtestAsync(string.Empty, "listening", cancellationToken);
        return new
        {
            featuredTasks = tasks,
            intro = "Listening practice emphasises accurate capture of numbers, frequencies, and changes in plan.",
            partCollections = new[]
            {
                new { id = "listening-practice", title = "Practice sets", route = "/listening/player/lt-001" }
            },
            transcriptBackedReview = new
            {
                title = "Transcript-backed review",
                route = "/listening/review/lt-001",
                availableAfterAttempt = true
            },
            distractorDrills = new[]
            {
                new { id = "listening-drill-distractor_confusion", title = "Frequency distractor drill", route = "/listening/drills/listening-drill-distractor_confusion" }
            },
            accessPolicyHints = new
            {
                rationale = "Use transcript-backed review after an attempt so you can diagnose distractor patterns with real evidence instead of replaying blindly.",
                availableAfterAttempt = true
            },
            mockSets = new[]
            {
                new { id = "full-practice", title = "Full OET Mock", type = "full", subType = (string?)null, mode = "practice", includeReview = false, strictTimer = false, reviewSelection = "none" },
                new { id = "full-exam", title = "Full OET Mock", type = "full", subType = (string?)null, mode = "exam", includeReview = false, strictTimer = true, reviewSelection = "none" },
                new { id = "writing-only", title = "Writing-only Mock", type = "sub", subType = (string?)"writing", mode = "exam", includeReview = true, strictTimer = true, reviewSelection = "current_subtest" }
            }
        };
    }

    public async Task<object> GetListeningTaskAsync(string contentId, CancellationToken cancellationToken) => await GetGenericTaskAsync(contentId, "listening", cancellationToken);
    public async Task<object> CreateListeningAttemptAsync(string userId, CreateAttemptRequest request, CancellationToken cancellationToken) => await CreateAttemptAsync(userId, request, "listening", cancellationToken);
    public async Task<object> GetListeningAttemptAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        var attempt = await GetSubtestAttemptOwnedByUserAsync(userId, attemptId, "listening", "listening_attempt_not_found", "Listening attempt not found.", cancellationToken);
        return await GetAttemptAsync(attempt.Id, cancellationToken);
    }
    public async Task<object> UpdateListeningAnswersAsync(string userId, string attemptId, AnswersUpdateRequest request, CancellationToken cancellationToken) => await UpdateAnswersAsync(userId, attemptId, request, "listening", cancellationToken);
    public async Task<object> SubmitListeningAttemptAsync(string userId, string attemptId, CancellationToken cancellationToken) => await SubmitObjectiveAttemptAsync(userId, attemptId, "listening", cancellationToken);
    public async Task<object> GetListeningEvaluationAsync(string userId, string evaluationId, CancellationToken cancellationToken) => await GetObjectiveEvaluationAsync(userId, evaluationId, "listening", cancellationToken);
    public Task<object> GetListeningDrillAsync(string drillId, CancellationToken cancellationToken) => Task.FromResult(BuildListeningDrill(drillId));

    private async Task<List<object>> GetTasksBySubtestAsync(string userId, string subtest, CancellationToken cancellationToken)
    {
        // Handoff item 2 is scoped to Writing/Speaking content specifically —
        // Reading and Listening papers stay all-professions (Free Mocks plan,
        // unchanged here). Only filter the two subtests the handoff covers so
        // GetListeningHomeAsync (which has no userId to
        // give us) keeps its existing, correct, unfiltered behaviour.
        var isProfessionScoped = string.Equals(subtest, "writing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(subtest, "speaking", StringComparison.OrdinalIgnoreCase);
        string? normalizedProfession = null;
        if (isProfessionScoped)
        {
            var learnerProfession = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.ActiveProfessionId)
                .FirstOrDefaultAsync(cancellationToken);
            normalizedProfession = learnerProfession?.ToLower();
        }
        var items = await db.ContentItems
            .AsNoTracking()
            .Where(x => x.SubtestCode == subtest && x.Status == ContentStatus.Published
                && (!isProfessionScoped || x.ProfessionId == null || x.ProfessionId.ToLower() == normalizedProfession))
            .OrderBy(x => x.Title)
            .ToListAsync(cancellationToken);
        if (string.Equals(subtest, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            // Batch the card lookup: this list is the whole published speaking
            // corpus, so a per-item query would be ~400 round trips.
            var itemIds = items.Select(x => x.Id).ToList();
            var cards = await db.RolePlayCards
                .AsNoTracking()
                .Where(c => itemIds.Contains(c.ContentItemId))
                .ToDictionaryAsync(c => c.ContentItemId, c => c, cancellationToken);
            return items
                .Select(item => (object)BuildLearnerSpeakingTaskPayload(
                    item,
                    cards.GetValueOrDefault(item.Id)))
                .ToList();
        }

        return items.Select(item => (object)new
        {
            contentId = item.Id,
            contentType = item.ContentType,
            subtest = item.SubtestCode,
            professionId = item.ProfessionId,
            title = item.Title,
            difficulty = item.Difficulty,
            estimatedDurationMinutes = item.EstimatedDurationMinutes,
            criteriaFocus = JsonSupport.Deserialize<List<string>>(item.CriteriaFocusJson, []),
            scenarioType = item.ScenarioType,
            modeSupport = JsonSupport.Deserialize<List<string>>(item.ModeSupportJson, []),
            publishedRevisionId = item.PublishedRevisionId,
            status = ToContentStatus(item.Status)
        }).ToList();
    }

    private async Task<object> CreateAttemptAsync(string userId, CreateAttemptRequest request, string subtest, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var contentForAttempt = await db.ContentItems
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.ContentId, cancellationToken);
        // Speaking submissions are keyed by RolePlayCard.Id throughout the
        // learner UI (Selection -> roleplay -> task, and submitSpeakingRecording
        // ultimately calls this with that id as ContentId) — RolePlayCard.Id
        // and its ContentItem shell's own Id are minted as two distinct,
        // differently-prefixed values (rpc-... vs ci-...), so the strict
        // lookup above 404s for every Speaking card. Mirrors the identical
        // fallback GetSpeakingTaskAsync already uses for card previews.
        // Without this, attempt creation — and therefore every downstream
        // grading step — never runs for a real Speaking submission
        // (confirmed live via full acceptance testing, 2026-09-09).
        var resolvedContentId = request.ContentId;
        if (contentForAttempt is null && string.Equals(subtest, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            var fallbackContentItemId = await db.RolePlayCards
                .AsNoTracking()
                .Where(c => c.Id == request.ContentId)
                .Select(c => c.ContentItemId)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrEmpty(fallbackContentItemId))
            {
                contentForAttempt = await db.ContentItems
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == fallbackContentItemId, cancellationToken);
                if (contentForAttempt is not null)
                {
                    resolvedContentId = fallbackContentItemId;
                }
            }
        }
        if (contentForAttempt is null)
        {
            throw ApiException.NotFound("content_not_found", "Practice content not found.");
        }
        if (!string.Equals(contentForAttempt.SubtestCode, subtest, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.NotFound("content_not_found", "Practice content not found.");
        }
        if (contentForAttempt.Status != ContentStatus.Published)
        {
            throw ApiException.Conflict("content_not_available", "This practice content is not currently available.");
        }

        // Free Mocks (owner 2026-09-22): the learner's ONE free AI-graded Speaking
        // sample. Decided here, server-side, from the profession's designated card
        // and the learner's once-only claim — never from the request. It must be
        // known BEFORE the credit gate below, because the free sample may hold no
        // credits at all.
        //
        // CRITICAL SECURITY FIX (22 Sep 2026 handoff, item 2): FreeSampleService
        // itself now only ever offers the CALLER'S OWN registered profession's
        // designated card (no cross-profession picker anywhere) — so IsOfferedAsync
        // below already returns false for another profession's card, and the
        // profession-isolation check right after this correctly still applies and
        // 404s. There is no bypass to remove here; freeSample only ever fires for
        // the learner's own profession.
        var freeSample = string.Equals(subtest, "speaking", StringComparison.OrdinalIgnoreCase)
            && await new FreeSamples.FreeSampleService(db).IsOfferedAsync(
                userId, FreeSamples.FreeSampleService.Speaking, request.ContentId, cancellationToken);
        if (string.Equals(subtest, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            var cardId = await db.RolePlayCards
                .AsNoTracking()
                .Where(card => card.Id == request.ContentId || card.ContentItemId == resolvedContentId)
                .Select(card => card.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(cardId))
            {
                throw ApiException.Conflict(
                    "live_voice_required",
                    "Published Speaking cards use native realtime live voice. The legacy recorder is reserved for the designated free Speaking card.");
            }

            await EnsureLegacyFreeSpeakingAccessAsync(userId, cardId, cancellationToken);
            if (!freeSample)
            {
                await EnsureLegacyFreeSpeakingAccessAsync(userId, cardId, cancellationToken);
            }
        }

        // Master Catalogue §5 profession isolation: a candidate must never open
        // another profession's content through a direct URL/API call. A null
        // ContentItem.ProfessionId means the item applies to all professions.
        if (!freeSample && !string.IsNullOrWhiteSpace(contentForAttempt.ProfessionId))
        {
            var learnerProfession = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.ActiveProfessionId)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.Equals(contentForAttempt.ProfessionId, learnerProfession, StringComparison.OrdinalIgnoreCase))
            {
                throw ApiException.NotFound("content_not_found", "Practice content not found.");
            }
        }

        // Master Catalogue §5 authorization model: starting a graded Writing or
        // Speaking activity requires an applicable balance (dedicated pool,
        // Flexible W/S, or Shared at the subtest rate) or an active unlimited
        // entitlement. NOTE: this is a read-only pre-check — for a single-card
        // Speaking attempt no debit happens downstream today (verified 2026-09-22),
        // so this is the only credit gate on that path.
        if (!freeSample && (subtest is "writing" or "speaking") && aiPackageCreditService is not null)
        {
            var eligible = await aiPackageCreditService.CheckGradingCreditAsync(userId, subtest, 1, cancellationToken);
            if (!eligible.Debited && !eligible.Bypassed)
            {
                throw ApiException.PaymentRequired(
                    eligible.ErrorCode ?? "no_ai_package_credits",
                    eligible.ErrorMessage ?? "You do not have enough credits to start this activity.");
            }
        }

        var context = request.Context ?? "practice";
        var mode = request.Mode ?? (subtest is "reading" or "listening" ? "exam" : "practice");
        var existingAttempts = await db.Attempts
            .Where(x => x.UserId == userId
                        && x.ContentId == resolvedContentId
                        && x.SubtestCode == subtest
                        && x.Context == context
                        && x.State == AttemptState.InProgress)
            .ToListAsync(cancellationToken);
        var existing = existingAttempts
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefault();
        if (existing is not null)
        {
            return await GetAttemptAsync(existing.Id, cancellationToken);
        }

        AssessmentMarkingPolicyResolution? markingPolicy = null;
        AssessmentScoreConversionResult? scoreConversionAtStart = null;
        if (subtest is "listening" or "reading")
        {
            var markingPolicyResolver = markingPolicyService ?? new AssessmentMarkingPolicyService(db);
            markingPolicy = await markingPolicyResolver.ResolveAsync(
                subtest,
                "default",
                cancellationToken: cancellationToken);
            if (!markingPolicy.IsAvailable || markingPolicy.ErrorCode is not null)
            {
                throw ApiException.Conflict(
                    $"{subtest}_marking_policy_unavailable",
                    $"{ToDisplaySubtest(subtest)} attempts are unavailable until an owner-approved marking policy is effective.");
            }

            var conversionResolver = scoreConversionService ?? new AssessmentScoreConversionService(db);
            scoreConversionAtStart = await conversionResolver.ResolveAsync(
                subtest,
                rawScore: 0,
                scopeKey: "default",
                cancellationToken: cancellationToken);
        }

        var attempt = new Attempt
        {
            Id = $"{subtest[..1]}a-{Guid.NewGuid():N}",
            UserId = userId,
            ContentId = resolvedContentId,
            SubtestCode = subtest,
            Context = context,
            Mode = mode,
            State = AttemptState.InProgress,
            StartedAt = DateTimeOffset.UtcNow,
            DeviceType = request.DeviceType ?? "web",
            ParentAttemptId = request.ParentAttemptId,
            ComparisonGroupId = $"{subtest}-{resolvedContentId}",
            MarkingPolicyVersionId = markingPolicy?.PolicyId,
            ScoreConversionSnapshotJson = scoreConversionAtStart is null
                ? null
                : AssessmentScoreConversionSnapshot.Capture(scoreConversionAtStart).Serialize(),
            PolicySnapshotJson = markingPolicy is null
                ? "{}"
                : JsonSupport.Serialize(new
                {
                    markingPolicy = markingPolicy.Document,
                    markingPolicyVersionKey = markingPolicy.PolicyVersionKey,
                    markingPolicyErrorCode = markingPolicy.ErrorCode,
                })
        };
        if (freeSample)
        {
            // Free sample retry addendum (owner 23 Sep 2026): NEW free Speaking
            // uses run on the shared Speaking session engine
            // (SpeakingSessionService.CreateSessionAsync binds them). The legacy
            // recorder only resumes a free attempt already in flight (returned
            // above as the existing in-progress attempt); it never mints a new one.
            throw ApiException.Conflict(
                "free_speaking_session_required",
                "Your free Speaking sample now runs in the Speaking session player. Open it from the Speaking page.");
        }
        db.Attempts.Add(attempt);
        await LearnerWorkflowCoordinator.AttachAttemptToDiagnosticAsync(db, attempt, cancellationToken);
        await RecordEventAsync(userId, "task_started", new { attemptId = attempt.Id, contentId = attempt.ContentId, subtest = attempt.SubtestCode, mode = attempt.Mode, context = attempt.Context, freeSample }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (markingPolicy is not null)
        {
            var markingPolicyResolver = markingPolicyService ?? new AssessmentMarkingPolicyService(db);
            await markingPolicyResolver.MarkUsedAsync(markingPolicy.PolicyId!, cancellationToken);
        }
        if (scoreConversionAtStart is { TableId: not null, IsAvailable: true } conversionAtStart)
        {
            var conversionResolver = scoreConversionService ?? new AssessmentScoreConversionService(db);
            await conversionResolver.MarkUsedAsync(conversionAtStart.TableId, cancellationToken);
        }
        var created = await GetAttemptAsync(attempt.Id, cancellationToken);
        return freeSample
            ? MergeWritingAttemptWithFeedback(created, ContentEntitlementService.FreeSampleFeedback)
            : created;
    }

    private async Task<object> GetAttemptAsync(string attemptId, CancellationToken cancellationToken)
    {
        var attempt = await db.Attempts.FirstAsync(x => x.Id == attemptId, cancellationToken);
        var content = await db.ContentItems.FirstAsync(x => x.Id == attempt.ContentId, cancellationToken);
        var detail = JsonSupport.Deserialize<Dictionary<string, object?>>(content.DetailJson, new Dictionary<string, object?>());
        var contentPayload = string.Equals(content.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase)
            ? BuildLearnerSpeakingTaskPayload(content, await LoadRolePlayCardAsync(content.Id, cancellationToken))
            : Merge(new Dictionary<string, object?>
            {
                ["contentId"] = content.Id,
                ["title"] = content.Title,
                ["subtest"] = content.SubtestCode,
                ["professionId"] = content.ProfessionId,
                ["difficulty"] = content.Difficulty,
                ["estimatedDurationMinutes"] = content.EstimatedDurationMinutes,
                ["caseNotes"] = content.CaseNotes,
                ["scenarioType"] = content.ScenarioType,
                ["criteriaFocus"] = JsonSupport.Deserialize<List<string>>(content.CriteriaFocusJson, [])
            }, detail);

        return new
        {
            attemptId = attempt.Id,
            userId = attempt.UserId,
            contentId = attempt.ContentId,
            subtest = attempt.SubtestCode,
            context = attempt.Context,
            mode = attempt.Mode,
            state = ToApiState(attempt.State),
            startedAt = attempt.StartedAt,
            submittedAt = attempt.SubmittedAt,
            completedAt = attempt.CompletedAt,
            elapsedSeconds = attempt.ElapsedSeconds,
            draftVersion = attempt.DraftVersion,
            parentAttemptId = attempt.ParentAttemptId,
            comparisonGroupId = attempt.ComparisonGroupId,
            deviceType = attempt.DeviceType,
            lastClientSyncAt = attempt.LastClientSyncAt,
            draftContent = attempt.DraftContent,
            scratchpad = attempt.Scratchpad,
            checklist = JsonSupport.Deserialize<Dictionary<string, bool>>(attempt.ChecklistJson, new Dictionary<string, bool>()),
            answers = JsonSupport.Deserialize<Dictionary<string, string?>>(attempt.AnswersJson, new Dictionary<string, string?>()),
            audioUploadState = ToUploadState(attempt.AudioUploadState),
            transcript = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(attempt.TranscriptJson, []),
            analysis = JsonSupport.Deserialize<Dictionary<string, object?>>(attempt.AnalysisJson, new Dictionary<string, object?>()),
            content = contentPayload,
            feedbackMessage = (string?)null
        };
    }

    private async Task<object> GetGenericTaskAsync(string contentId, string subtest, CancellationToken cancellationToken)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId && x.SubtestCode == subtest, cancellationToken)
                   ?? throw ApiException.NotFound("content_not_found", $"{ToDisplaySubtest(subtest)} task not found.");
        var detail = JsonSupport.Deserialize<Dictionary<string, object?>>(item.DetailJson, new Dictionary<string, object?>());
        if (string.Equals(subtest, "reading", StringComparison.OrdinalIgnoreCase))
        {
            detail = RedactLegacyReadingTask(detail);
        }
        else if (string.Equals(subtest, "listening", StringComparison.OrdinalIgnoreCase))
        {
            detail = RedactLegacyListeningTask(detail);
        }
        return Merge(new Dictionary<string, object?>
        {
            ["contentId"] = item.Id,
            ["title"] = item.Title,
            ["difficulty"] = item.Difficulty,
            ["estimatedDurationMinutes"] = item.EstimatedDurationMinutes,
            ["subtest"] = item.SubtestCode,
            ["scenarioType"] = item.ScenarioType
        }, detail);
    }

    private async Task<object> UpdateAnswersAsync(string userId, string attemptId, AnswersUpdateRequest request, string subtest, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetSubtestAttemptOwnedByUserAsync(userId, attemptId, subtest, $"{subtest}_attempt_not_found", $"{ToDisplaySubtest(subtest)} attempt not found.", cancellationToken);
        EnsureObjectiveAnswersEditable(attempt);
        var current = JsonSupport.Deserialize<Dictionary<string, string?>>(attempt.AnswersJson, new Dictionary<string, string?>());
        foreach (var (key, value) in request.Answers)
        {
            current[key] = value;
        }

        attempt.AnswersJson = JsonSupport.Serialize(current);
        attempt.LastClientSyncAt = DateTimeOffset.UtcNow;
        attempt.State = AttemptState.InProgress;
        await db.SaveChangesAsync(cancellationToken);
        return new { attemptId = attempt.Id, answers = current, lastClientSyncAt = attempt.LastClientSyncAt };
    }

    private static void EnsureObjectiveAnswersEditable(Attempt attempt)
    {
        if (attempt.State is AttemptState.NotStarted or AttemptState.InProgress or AttemptState.Paused)
        {
            return;
        }

        throw ApiException.Conflict(
            "objective_attempt_locked",
            "This attempt has already been submitted and cannot be edited.",
            [new ApiFieldError("attemptId", "locked", "Start a new attempt before editing another response.")]);
    }

    private async Task<object> SubmitObjectiveAttemptAsync(string userId, string attemptId, string subtest, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetSubtestAttemptOwnedByUserAsync(userId, attemptId, subtest, $"{subtest}_attempt_not_found", $"{ToDisplaySubtest(subtest)} attempt not found.", cancellationToken);
        if (attempt.State == AttemptState.Completed)
        {
            var existing = await db.Evaluations.FirstAsync(x => x.AttemptId == attempt.Id, cancellationToken);
            return new { attemptId = attempt.Id, evaluationId = existing.Id, state = "completed" };
        }

        if (aiPackageCreditService is not null)
        {
            // The paper (sample / content item) is the billing unit, matching the
            // part-practice and full-paper start gates: the reference is per
            // (user, paper), so the first submission for this paper content
            // debits exactly one test and every later submission of the same
            // paper content is free. Using attempt.Id here would debit a credit
            // for every re-submission.
            var debit = await aiPackageCreditService.DeductObjectivePracticeAsync(
                userId, subtest,
                CreditGateExtensions.ObjectivePaperReference(subtest, userId, attempt.ContentId),
                cancellationToken);
            if (!debit.Debited)
            {
                throw ApiException.PaymentRequired(
                    debit.ErrorCode ?? $"no_{subtest}_tests",
                    debit.ErrorMessage ?? $"You have no {ToDisplaySubtest(subtest)} practice tests remaining. Purchase a package to continue.");
            }
        }

        attempt.State = AttemptState.Completed;
        attempt.SubmittedAt = DateTimeOffset.UtcNow;
        attempt.CompletedAt = DateTimeOffset.UtcNow;
        var content = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == attempt.ContentId && x.SubtestCode == subtest, cancellationToken)
            ?? throw ApiException.NotFound("content_not_found", $"{ToDisplaySubtest(subtest)} task not found.");
        var questions = ObjectiveQuestionsForContent(content);
        var answers = JsonSupport.Deserialize<Dictionary<string, string?>>(attempt.AnswersJson, new Dictionary<string, string?>());
        var rawScore = ObjectiveRawScore(questions, answers);
        var maxRawScore = OetScoring.ListeningReadingRawMax;
        var conversionResolver = scoreConversionService ?? new AssessmentScoreConversionService(db);
        var conversion = await AssessmentScoreConversionSnapshotResolver.ResolveAsync(
            conversionResolver,
            subtest,
            rawScore,
            attempt.ScoreConversionSnapshotJson,
            legacyTableId: null,
            scopeKey: "default",
            cancellationToken: cancellationToken);
        var hasApprovedConversion = conversion.ConvertedScore.HasValue
            && !string.IsNullOrWhiteSpace(conversion.TableVersionKey)
            && conversion.Passed.HasValue;
        if (hasApprovedConversion && conversion.TableId is not null && conversion.IsAvailable)
        {
            await conversionResolver.MarkUsedAsync(conversion.TableId, cancellationToken);
        }
        var scaledScore = hasApprovedConversion ? conversion.ConvertedScore : null;
        var grade = hasApprovedConversion ? conversion.Grade ?? "—" : "—";
        var scoreDisplay = scaledScore is int converted
            ? $"{rawScore} / {maxRawScore} \u2022 {converted} / 500 \u2022 Grade {grade}"
            : $"{rawScore} / {maxRawScore} \u2022 Practice score unavailable ({conversion.ErrorCode ?? "score_conversion_unavailable"})";
        var incorrectItems = questions
            .Where(question =>
            {
                var questionId = question.GetValueOrDefault("id")?.ToString() ?? string.Empty;
                return !MatchesObjectiveAnswer(answers.GetValueOrDefault(questionId), question.GetValueOrDefault("correctAnswer")?.ToString());
            })
            .ToList();

        var evaluation = new Evaluation
        {
            Id = $"{subtest[..1]}e-{Guid.NewGuid():N}",
            AttemptId = attempt.Id,
            SubtestCode = subtest,
            State = AsyncState.Completed,
            ScoreRange = scoreDisplay,
            RawScore = rawScore,
            MaxRawScore = maxRawScore,
            ScaledScore = scaledScore,
            ScoreConversionTableVersionKey = hasApprovedConversion ? conversion.TableVersionKey : null,
            ScoreConversionGrade = hasApprovedConversion ? conversion.Grade : null,
            ScoreConversionPassed = hasApprovedConversion ? conversion.Passed : null,
            GradeRange = scaledScore is null ? "Practice score unavailable" : $"Grade {grade}",
            ConfidenceBand = ConfidenceBand.High,
            StrengthsJson = JsonSupport.Serialize(hasApprovedConversion && conversion.Passed is true
                ? new[] { $"Your {ToDisplaySubtest(subtest)} raw score is at or above the OET Grade B practice threshold.", "Your answer flow remained controlled under time pressure." }
                : new[] { $"You completed the {ToDisplaySubtest(subtest)} attempt and now have item-level evidence to review." }),
            IssuesJson = JsonSupport.Serialize(incorrectItems
                .Take(3)
                .Select(question => question.GetValueOrDefault("distractorExplanation")?.ToString()
                    ?? question.GetValueOrDefault("explanation")?.ToString()
                    ?? "Review exact detail evidence before your next attempt.")
                .DefaultIfEmpty("Keep using evidence-backed review to maintain objective accuracy.")),
            CriterionScoresJson = JsonSupport.Serialize(new[]
            {
                new
                {
                    criterionCode = $"{subtest}_accuracy",
                    rawScore,
                    maxRawScore,
                    scaledScore,
                    grade,
                    passed = hasApprovedConversion ? conversion.Passed : null,
                    scoreConversionTableVersionKey = hasApprovedConversion ? conversion.TableVersionKey : null,
                    scoreConversionErrorCode = hasApprovedConversion ? conversion.ErrorCode : "score_conversion_unavailable",
                    scoreDisplay,
                    confidenceBand = "high",
                    explanation = "Objective score graded from the authored answer key."
                }
            }),
            FeedbackItemsJson = JsonSupport.Serialize(incorrectItems.Select(question =>
            {
                var questionId = question.GetValueOrDefault("id")?.ToString() ?? string.Empty;
                return new
                {
                    feedbackItemId = $"{attempt.Id}-{questionId}",
                    criterionCode = ObjectiveErrorType(subtest, question),
                    type = "answer_feedback",
                    anchor = new { questionId },
                    message = question.GetValueOrDefault("explanation")?.ToString() ?? "Review the source evidence for this answer.",
                    severity = "medium",
                    suggestedFix = question.GetValueOrDefault("distractorExplanation")?.ToString() ?? "Repeat a short focused drill for this error type."
                };
            })),
            GeneratedAt = DateTimeOffset.UtcNow,
            ModelExplanationSafe = "This objective result is based on answer accuracy only.",
            LearnerDisclaimer = "Practice estimate only.",
            StatusReasonCode = "completed",
            StatusMessage = "Result ready.",
            LastTransitionAt = DateTimeOffset.UtcNow
        };
        db.Evaluations.Add(evaluation);
        await RecordEventAsync(attempt.UserId, "task_submitted", new { attemptId = attempt.Id, evaluationId = evaluation.Id, subtest, contentId = attempt.ContentId }, cancellationToken);
        await LearnerWorkflowCoordinator.UpdateDiagnosticProgressAsync(db, attempt, AttemptState.Completed, cancellationToken);
        await LearnerWorkflowCoordinator.QueueStudyPlanRegenerationAsync(db, attempt.UserId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new { attemptId = attempt.Id, evaluationId = evaluation.Id, state = "completed" };
    }

    private async Task<object> GetObjectiveEvaluationAsync(string userId, string evaluationId, string subtest, CancellationToken cancellationToken)
    {
        var evaluation = await GetEvaluationOwnedByUserAsync(userId, evaluationId, cancellationToken);
        if (!string.Equals(evaluation.SubtestCode, subtest, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.NotFound($"{subtest}_evaluation_not_found", $"{ToDisplaySubtest(subtest)} evaluation not found.");
        }

        var attempt = await db.Attempts.FirstAsync(x => x.Id == evaluation.AttemptId, cancellationToken);
        if (!string.Equals(attempt.SubtestCode, subtest, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.NotFound($"{subtest}_evaluation_not_found", $"{ToDisplaySubtest(subtest)} evaluation not found.");
        }

        var content = await db.ContentItems.FirstAsync(x => x.Id == attempt.ContentId, cancellationToken);
        await RecordEventAsync(userId, "evaluation_viewed", new { evaluationId = evaluation.Id, attemptId = attempt.Id, subtest = evaluation.SubtestCode }, cancellationToken);
        var detail = JsonSupport.Deserialize<Dictionary<string, object?>>(content.DetailJson, new Dictionary<string, object?>());
        var questions = detail.TryGetValue("questions", out var questionsValue)
            ? JsonSupport.Deserialize<List<Dictionary<string, object?>>>(JsonSupport.Serialize(questionsValue), [])
            : [];
        var answers = JsonSupport.Deserialize<Dictionary<string, string?>>(attempt.AnswersJson, new Dictionary<string, string?>());
        var itemReview = questions
            .Select(question => ObjectiveItemReviewDto(content.SubtestCode, question, answers))
            .ToList();
        var rawScore = evaluation.RawScore ?? ObjectiveRawScore(questions, answers);
        var maxRawScore = evaluation.MaxRawScore ?? OetScoring.ListeningReadingRawMax;
        var hasApprovedConversion = (!IsListeningOrReading(subtest)
                || maxRawScore == OetScoring.ListeningReadingRawMax)
            && evaluation.ScaledScore.HasValue
            && !string.IsNullOrWhiteSpace(evaluation.ScoreConversionTableVersionKey)
            && evaluation.ScoreConversionPassed.HasValue;
        var scaledScore = hasApprovedConversion ? evaluation.ScaledScore : null;
        var grade = hasApprovedConversion ? evaluation.ScoreConversionGrade ?? "—" : "—";
        var scoreDisplay = scaledScore is int converted
            ? $"{rawScore} / {maxRawScore} \u2022 {converted} / 500 \u2022 Grade {grade}"
            : $"{rawScore} / {maxRawScore} \u2022 Practice score unavailable (score_conversion_unavailable)";
        var errorClusters = ObjectiveErrorClusters(content.SubtestCode, itemReview);
        return new
        {
            evaluationId = evaluation.Id,
            attemptId = attempt.Id,
            taskId = content.Id,
            title = content.Title,
            subtest = content.SubtestCode,
            score = scoreDisplay,
            rawScore,
            maxRawScore,
            scaledScore,
            scoreConversionTableVersionKey = hasApprovedConversion ? evaluation.ScoreConversionTableVersionKey : null,
            scoreConversionErrorCode = hasApprovedConversion ? null : "score_conversion_unavailable",
            grade,
            passed = hasApprovedConversion ? evaluation.ScoreConversionPassed : null,
            gradeRange = hasApprovedConversion ? $"Grade {grade}" : "Practice score unavailable",
            state = ToAsyncState(evaluation.State),
            strengths = JsonSupport.Deserialize<List<string>>(evaluation.StrengthsJson, []),
            issues = JsonSupport.Deserialize<List<string>>(evaluation.IssuesJson, []),
            feedbackItems = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.FeedbackItemsJson, []),
            itemReview,
            errorClusters,
            recommendedNextDrill = ObjectiveRecommendedDrill(content.SubtestCode, errorClusters),
            transcriptAccess = content.SubtestCode == "listening"
                ? ObjectiveTranscriptAccess(itemReview)
                : null,
            generatedAt = evaluation.GeneratedAt
        };
    }

    private static Dictionary<string, object?> RedactLegacyReadingTask(Dictionary<string, object?> detail)
    {
        var safe = new Dictionary<string, object?>(detail, StringComparer.OrdinalIgnoreCase);
        safe.Remove("correctAnswer");
        safe.Remove("explanation");
        safe.Remove("acceptedSynonyms");

        if (safe.TryGetValue("questions", out var questions))
        {
            safe["questions"] = RedactLegacyReadingQuestions(questions);
        }

        return safe;
    }

    private static object? RedactLegacyReadingQuestions(object? questions)
    {
        if (questions is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Select(RedactLegacyReadingQuestion).ToList();
        }

        return questions;
    }

    private static Dictionary<string, object?> RedactLegacyReadingQuestion(JsonElement question)
    {
        var safe = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in question.EnumerateObject())
        {
            if (property.NameEquals("correctAnswer")
                || property.NameEquals("explanation")
                || property.NameEquals("acceptedSynonyms"))
            {
                continue;
            }

            safe[property.Name] = property.Value.Clone();
        }

        return safe;
    }

    private static Dictionary<string, object?> RedactLegacyListeningTask(Dictionary<string, object?> detail)
    {
        var safe = new Dictionary<string, object?>(detail, StringComparer.OrdinalIgnoreCase);
        safe.Remove("correctAnswer");
        safe.Remove("explanation");
        safe.Remove("acceptedSynonyms");
        safe.Remove("transcriptExcerpt");
        safe.Remove("distractorExplanation");

        if (safe.TryGetValue("questions", out var questions))
        {
            safe["questions"] = RedactLegacyListeningQuestions(questions);
        }

        return safe;
    }

    private static object? RedactLegacyListeningQuestions(object? questions)
    {
        if (questions is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Select(RedactLegacyListeningQuestion).ToList();
        }

        return questions;
    }

    private static Dictionary<string, object?> RedactLegacyListeningQuestion(JsonElement question)
    {
        var safe = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in question.EnumerateObject())
        {
            if (property.NameEquals("correctAnswer")
                || property.NameEquals("explanation")
                || property.NameEquals("acceptedSynonyms")
                || property.NameEquals("transcriptExcerpt")
                || property.NameEquals("distractorExplanation"))
            {
                continue;
            }

            safe[property.Name] = property.Value.Clone();
        }

        return safe;
    }

    private static object BuildListeningDrill(string drillId)
    {
        var normalizedId = (drillId ?? string.Empty).Trim().ToLowerInvariant();
        var detail = normalizedId switch
        {
            "listening-drill-distractor_confusion" => new
            {
                drillId = normalizedId,
                title = "Distractor Control Drill",
                focusLabel = "Speaker intent and change-of-plan control",
                description = "Use short consultation clips to separate what was suggested first from what was finally agreed.",
                errorType = "distractor_confusion",
                estimatedMinutes = 12,
                highlights = new[]
                {
                    "Track corrected instructions instead of the first option you hear.",
                    "Notice when a clinician rules out a medication or follow-up plan.",
                    "Review transcript evidence only after you commit to an answer."
                }
            },
            "listening-drill-numbers_and_frequencies" => new
            {
                drillId = normalizedId,
                title = "Numbers and Frequencies Drill",
                focusLabel = "Medication, dosage, and appointment precision",
                description = "Practise capturing exact numbers, timings, and dosage language in fast clinical audio.",
                errorType = "numbers_and_frequencies",
                estimatedMinutes = 10,
                highlights = new[]
                {
                    "Distinguish similar-sounding numbers before replaying.",
                    "Lock onto frequency phrases such as once daily and every second day.",
                    "Use replay snippets to verify quantities, not whole conversations."
                }
            },
            _ => new
            {
                drillId = string.IsNullOrWhiteSpace(normalizedId) ? "listening-drill-detail_capture" : normalizedId,
                title = "Exact Detail Capture Drill",
                focusLabel = "Referral detail and key-clue accuracy",
                description = "Rebuild listening accuracy by isolating the exact clinical detail that changed the answer.",
                errorType = "detail_capture",
                estimatedMinutes = 11,
                highlights = new[]
                {
                    "Identify which detail actually answers the question.",
                    "Separate symptoms, plans, and history without blending them.",
                    "Review the transcript clue that justified the correct answer."
                }
            }
        };

        return new
        {
            detail.drillId,
            detail.title,
            detail.focusLabel,
            detail.description,
            detail.errorType,
            detail.estimatedMinutes,
            detail.highlights,
            launchRoute = $"/listening/player/lt-001?drill={Uri.EscapeDataString(detail.drillId)}",
            reviewRoute = $"/listening/review/lt-001?drill={Uri.EscapeDataString(detail.drillId)}"
        };
    }

    private static object ObjectiveItemReviewDto(string subtest, Dictionary<string, object?> question, Dictionary<string, string?> answers)
    {
        var questionId = question.GetValueOrDefault("id")?.ToString() ?? string.Empty;
        var learnerAnswer = answers.GetValueOrDefault(questionId);
        var correctAnswer = question.GetValueOrDefault("correctAnswer")?.ToString();
        var isCorrect = MatchesObjectiveAnswer(learnerAnswer, correctAnswer);
        var transcriptAllowed = question.TryGetValue("allowTranscriptReveal", out var allowTranscriptRevealValue) && ReadBool(allowTranscriptRevealValue) == true;
        return new
        {
            questionId,
            number = ReadInt(question.GetValueOrDefault("number")) ?? 0,
            prompt = question.GetValueOrDefault("text")?.ToString(),
            type = question.GetValueOrDefault("type")?.ToString(),
            learnerAnswer,
            correctAnswer,
            isCorrect,
            explanation = question.GetValueOrDefault("explanation")?.ToString(),
            errorType = isCorrect ? (string?)null : ObjectiveErrorType(subtest, question),
            options = ReadStringList(question.GetValueOrDefault("options")) ?? [],
            transcript = subtest == "listening" && transcriptAllowed
                ? new
                {
                    allowed = true,
                    excerpt = question.GetValueOrDefault("transcriptExcerpt")?.ToString(),
                    distractorExplanation = question.GetValueOrDefault("distractorExplanation")?.ToString()
                }
                : null
        };
    }

    private static List<Dictionary<string, object?>> ObjectiveQuestionsForContent(ContentItem content)
    {
        var detail = JsonSupport.Deserialize<Dictionary<string, object?>>(content.DetailJson, new Dictionary<string, object?>());
        return detail.TryGetValue("questions", out var questionsValue)
            ? JsonSupport.Deserialize<List<Dictionary<string, object?>>>(JsonSupport.Serialize(questionsValue), [])
            : [];
    }

    private static int ObjectiveRawScore(IEnumerable<Dictionary<string, object?>> questions, Dictionary<string, string?> answers)
    {
        var raw = questions.Count(question =>
        {
            var questionId = question.GetValueOrDefault("id")?.ToString() ?? string.Empty;
            var correctAnswer = question.GetValueOrDefault("correctAnswer")?.ToString();
            return MatchesObjectiveAnswer(answers.GetValueOrDefault(questionId), correctAnswer);
        });

        return Math.Clamp(raw, 0, OetScoring.ListeningReadingRawMax);
    }

    private static IEnumerable<object> ObjectiveErrorClusters(string subtest, IEnumerable<object> itemReview)
    {
        var clusterSeed = itemReview
            .Select(item => JsonSupport.Deserialize<Dictionary<string, object?>>(JsonSupport.Serialize(item), new Dictionary<string, object?>()))
            .Where(item => item.GetValueOrDefault("isCorrect")?.ToString() == bool.FalseString || string.Equals(item.GetValueOrDefault("isCorrect")?.ToString(), "false", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.GetValueOrDefault("errorType")?.ToString() ?? "accuracy")
            .Select(group => new
            {
                errorType = group.Key,
                label = ObjectiveErrorTypeLabel(group.Key),
                count = group.Count(),
                subtest,
                affectedQuestionIds = group.Select(item => item.GetValueOrDefault("questionId")?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x))
            });

        return clusterSeed.Any()
            ? clusterSeed
            : new object[]
            {
                new
                {
                    errorType = subtest == "listening" ? "distractor_confusion" : "detail_capture",
                    label = ObjectiveErrorTypeLabel(subtest == "listening" ? "distractor_confusion" : "detail_capture"),
                    count = 0,
                    subtest,
                    affectedQuestionIds = Array.Empty<string>()
                }
            };
    }

    private static object ObjectiveRecommendedDrill(string subtest, IEnumerable<object> errorClusters)
    {
        var firstCluster = errorClusters
            .Select(cluster => JsonSupport.Deserialize<Dictionary<string, object?>>(JsonSupport.Serialize(cluster), new Dictionary<string, object?>()))
            .FirstOrDefault();
        var errorType = firstCluster?.GetValueOrDefault("errorType")?.ToString() ?? (subtest == "listening" ? "distractor_confusion" : "detail_capture");
        var listeningDrillId = $"listening-drill-{errorType}";
        return new
        {
            id = subtest == "listening" ? listeningDrillId : $"{subtest}-drill-{errorType}",
            title = subtest == "listening" ? "Listening distractor drill" : "Reading exact-detail drill",
            rationale = $"Focus next on {ObjectiveErrorTypeLabel(errorType).ToLowerInvariant()} to strengthen your {ToDisplaySubtest(subtest)} accuracy.",
            route = subtest == "listening"
                ? $"/listening/drills/{Uri.EscapeDataString(listeningDrillId)}"
                : "/reading"
        };
    }

    private static object ObjectiveTranscriptAccess(IEnumerable<object> itemReview)
    {
        var items = itemReview
            .Select(item => JsonSupport.Deserialize<Dictionary<string, object?>>(JsonSupport.Serialize(item), new Dictionary<string, object?>()))
            .ToList();
        var allowedQuestionIds = items
            .Where(item =>
            {
                var transcript = ReadObject(item.GetValueOrDefault("transcript"));
                return transcript?.GetValueOrDefault("allowed")?.ToString() == bool.TrueString || string.Equals(transcript?.GetValueOrDefault("allowed")?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
            })
            .Select(item => item.GetValueOrDefault("questionId")?.ToString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();

        return new
        {
            policy = "per_item_post_attempt",
            state = allowedQuestionIds.Count == 0 ? "restricted" : "partial",
            allowedQuestionIds,
            reason = "Transcript snippets are revealed only on items that explicitly allow post-attempt transcript support."
        };
    }

    private static bool MatchesObjectiveAnswer(string? learnerAnswer, string? correctAnswer)
        => string.Equals(NormalizeObjectiveAnswer(learnerAnswer), NormalizeObjectiveAnswer(correctAnswer), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeObjectiveAnswer(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Trim().ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)).ToArray());

    private static string ObjectiveErrorType(string subtest, Dictionary<string, object?> question)
    {
        var type = question.GetValueOrDefault("type")?.ToString();
        if (subtest == "listening" && !string.IsNullOrWhiteSpace(question.GetValueOrDefault("distractorExplanation")?.ToString()))
        {
            return "distractor_confusion";
        }

        return (subtest, type) switch
        {
            ("reading", "mcq") => "named_concept_miss",
            ("reading", _) => "detail_capture",
            ("listening", _) => "detail_capture",
            _ => "accuracy"
        };
    }

    private static string ObjectiveErrorTypeLabel(string errorType) => errorType switch
    {
        "named_concept_miss" => "Named concept recognition",
        "distractor_confusion" => "Distractor control",
        "detail_capture" => "Exact detail capture",
        _ => "Accuracy"
    };
}
