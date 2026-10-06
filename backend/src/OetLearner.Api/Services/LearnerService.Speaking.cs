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

    public async Task<object> GetSpeakingHomeAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        var tasks = await GetTasksBySubtestAsync(userId, "speaking", cancellationToken);
        var attemptIds = await db.Attempts.Where(x => x.UserId == userId && x.SubtestCode == "speaking").Select(x => x.Id).ToListAsync(cancellationToken);
        var wallet = await db.Wallets.FirstAsync(x => x.UserId == userId, cancellationToken);
        var attempts = (await db.Attempts
            .Where(x => x.UserId == userId && x.SubtestCode == "speaking")
            .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.SubmittedAt ?? x.StartedAt)
            .Take(4)
            .ToList();
        var latestEvaluation = (await db.Evaluations
            .Where(x => x.SubtestCode == "speaking" && attemptIds.Contains(x.AttemptId))
            .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.GeneratedAt)
            .FirstOrDefault();
        var commonIssues = latestEvaluation is null
            ? new[] { "Build smoother openings for role plays.", "Keep the professional tone consistent." }
            : JsonSupport.Deserialize<List<string>>(latestEvaluation.IssuesJson, [])
                .DefaultIfEmpty("Build smoother openings for role plays.")
                .ToArray();
        var evaluationByAttempt = (await db.Evaluations
            .Where(x => attemptIds.Contains(x.AttemptId))
            .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.GeneratedAt)
            .ToList();
        var evaluationLookup = evaluationByAttempt
            .GroupBy(x => x.AttemptId)
            .ToDictionary(group => group.Key, group => group.First());
        var phrasingRoute = latestEvaluation is null ? "/speaking/selection" : $"/speaking/phrasing/{latestEvaluation.Id}";
        // The latest-evaluation card is a convenience: an evaluation whose task is gone (or locked to another profession)
        // must not take the whole hub down. A missing task row made GET /v1/speaking/home a 500 in the CI stack.
        object? latestEvaluationSummary = null;
        if (latestEvaluation is not null)
        {
            try
            {
                latestEvaluationSummary = await GetSpeakingEvaluationSummaryAsync(userId, latestEvaluation.Id, cancellationToken);
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Left out: the learner still gets the hub, the tasks and the past attempts.
            }
        }
        return new
        {
            recommendedRolePlay = tasks.FirstOrDefault(),
            commonIssuesToImprove = commonIssues,
            drillGroups = new object[]
            {
                new
                {
                    id = "recalls-audio",
                    title = "Recalls audio drills",
                    items = new[]
                    {
                        new { id = "sp-drill-1", title = "Hear and type important treatment words", route = "/recalls/words" }
                    }
                },
                new
                {
                    id = "empathy_clarification",
                    title = "Empathy and clarification drills",
                    items = new[]
                    {
                        new { id = "sp-drill-2", title = "Clarify concerns without losing structure", route = phrasingRoute }
                    }
                }
            },
            pastAttempts = attempts.Select(attempt =>
            {
                evaluationLookup.TryGetValue(attempt.Id, out var evaluation);
                return new
                {
                    attemptId = attempt.Id,
                    state = ToApiState(attempt.State),
                    scoreEstimate = evaluation?.ScoreRange,
                    route = evaluation?.Id is null ? "/speaking/selection" : $"/speaking/results/{evaluation.Id}"
                };
            }),
            reviewCredits = new
            {
                available = wallet.CreditBalance,
                route = "/reviews",
                billingRoute = "/billing"
            },
            supportEntries = new[]
            {
                new { id = "recalls-audio", title = "Recalls Audio", description = "Click vocabulary words to hear British clinical pronunciation before the next role play.", route = "/recalls/words" },
                new { id = "conversation", title = "AI Conversation Practice", description = "Use the server-authoritative conversation module for interactive AI patient practice.", route = "/conversation" },
                new { id = "private-speaking", title = "Private Speaking Sessions", description = "Book human-led speaking support when you need live coaching.", route = "/private-speaking" }
            },
            featuredTasks = tasks.Take(3),
            latestEvaluation = latestEvaluationSummary,
            tips = new[]
            {
                "Use the mic check before longer speaking sessions.",
                "Prioritise professional tone and smooth transitions."
            }
        };
    }

    public async Task<List<object>> GetSpeakingTasksAsync(string userId, CancellationToken cancellationToken) => await GetTasksBySubtestAsync(userId, "speaking", cancellationToken);

    /// <summary>
    /// CRITICAL SECURITY FIX (22 Sep 2026 handoff, item 2): this is the exact
    /// code path the live role-card UI uses (fetchRoleCard -> GET
    /// /v1/speaking/tasks/{contentId}) — previously had NO userId parameter
    /// and therefore no profession check at all, so any authenticated learner
    /// could read any OTHER profession's full role-play card (patient
    /// name/age, background, tasks) by discovering or guessing a contentId or
    /// RolePlayCard id. Mirrors the existing, correct pattern already used at
    /// CreateAttemptAsync's profession-isolation block: a null
    /// ContentItem.ProfessionId means "applies to all professions" and is
    /// never blocked; otherwise it must match the caller's own
    /// ActiveProfessionId or the request 404s exactly like "not found" (never
    /// leaking that the content exists for someone else).
    /// </summary>
    public async Task<object> GetSpeakingTaskAsync(string userId, string contentId, CancellationToken cancellationToken)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId && x.SubtestCode == "speaking" && x.Status == ContentStatus.Published, cancellationToken);
        if (item is null)
        {
            // Also accept a RolePlayCard id. Learner surfaces that already hold
            // a card (the results page "practise again" link) only know the
            // card's id, not the id of the ContentItem shell behind it.
            var contentItemId = await db.RolePlayCards
                .AsNoTracking()
                .Where(c => c.Id == contentId)
                .Select(c => c.ContentItemId)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrEmpty(contentItemId))
            {
                item = await db.ContentItems.FirstOrDefaultAsync(
                    x => x.Id == contentItemId && x.SubtestCode == "speaking" && x.Status == ContentStatus.Published,
                    cancellationToken);
            }
        }

        if (item is null)
        {
            throw ApiException.NotFound("content_not_found", "Speaking task not found.");
        }

        await RequireOwnProfessionAsync(userId, item.ProfessionId, cancellationToken);

        return BuildLearnerSpeakingTaskPayload(item, await LoadRolePlayCardAsync(item.Id, cancellationToken));
    }

    /// <summary>
    /// Shared profession-isolation check (handoff item 2): a null
    /// <paramref name="contentProfessionId"/> means the item applies to every
    /// profession and is never blocked. Otherwise it must equal the caller's
    /// own <c>ActiveProfessionId</c> (normalised, see
    /// <see cref="LearnerProfessionGuard"/>) — mismatch or a learner with no
    /// profession yet set both 404 as "not found", never revealing that the
    /// content exists for a different profession.
    /// </summary>
    private async Task RequireOwnProfessionAsync(string userId, string? contentProfessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(contentProfessionId)) return;

        await LearnerProfessionGuard.RequireAsync(
            db, userId, contentProfessionId, "content_not_found", "Practice content not found.", cancellationToken);
    }

    public async Task<object> GetLegacyFreeSpeakingTaskAsync(string userId, string contentId, CancellationToken cancellationToken)
    {
        var cardId = await db.RolePlayCards
            .AsNoTracking()
            .Where(card => card.Id == contentId || card.ContentItemId == contentId)
            .Select(card => card.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(cardId))
        {
            throw ApiException.NotFound("content_not_found", "Speaking task not found.");
        }

        await EnsureLegacyFreeSpeakingAccessAsync(userId, cardId, cancellationToken);
        // GetSpeakingTaskAsync's own profession check (handoff item 2) is
        // redundant here — EnsureLegacyFreeSpeakingAccessAsync already proved
        // the card belongs to the caller's own profession (either via
        // FreeSampleService or freeTierContentResolver, both scoped to the
        // caller) — but left in as defense in depth.
        return await GetSpeakingTaskAsync(userId, contentId, cancellationToken);
    }

    public async Task<object> CreateSpeakingAttemptAsync(
        string userId,
        CreateAttemptRequest request,
        CancellationToken cancellationToken)
    {
        var cardId = await db.RolePlayCards.AsNoTracking()
            .Where(card => card.Id == request.ContentId || card.ContentItemId == request.ContentId)
            .Select(card => card.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(cardId))
        {
            throw ApiException.NotFound("speaking_task_not_found", "Speaking task not found.");
        }

        await EnsureLegacyFreeSpeakingAccessAsync(userId, cardId, cancellationToken);
        return await CreateAttemptAsync(userId, request, "speaking", cancellationToken);
    }

    public async Task<object> GetSpeakingAttemptAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        var attempt = await GetSpeakingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        // Profession lock (23 Sep 2026): a resumed attempt from before the
        // lock must not hand back another profession's role card.
        await RequireAttemptContentOwnProfessionAsync(userId, attempt.ContentId, cancellationToken);
        return await GetAttemptAsync(attempt.Id, cancellationToken);
    }

    /// <summary>
    /// Defense in depth for attempt/evaluation reads: ownership is already
    /// checked, but attempts created before the profession lock may point at
    /// another profession's content. 404 before the payload is built.
    /// </summary>
    private async Task RequireAttemptContentOwnProfessionAsync(string userId, string? contentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(contentId)) return;
        var contentProfession = await db.ContentItems.AsNoTracking()
            .Where(x => x.Id == contentId)
            .Select(x => x.ProfessionId)
            .FirstOrDefaultAsync(cancellationToken);
        await RequireOwnProfessionAsync(userId, contentProfession, cancellationToken);
    }

    private async Task EnsureLegacyFreeSpeakingAccessAsync(string userId, string cardId, CancellationToken cancellationToken)
    {
        if (await new FreeSamples.FreeSampleService(db).IsOfferedAsync(
            userId,
            FreeSamples.FreeSampleService.Speaking,
            cardId,
            cancellationToken))
        {
            return;
        }

        var learnerBilling = await db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.CurrentPlanId, user.ActiveProfessionId })
            .FirstOrDefaultAsync(cancellationToken);
        var isDesignatedFreeCard = learnerBilling is not null
            && string.Equals(learnerBilling.CurrentPlanId, "free", StringComparison.OrdinalIgnoreCase)
            && freeTierContentResolver is not null
            && await freeTierContentResolver.IsFeaturedSpeakingCardAsync(
                learnerBilling.ActiveProfessionId,
                cardId,
                cancellationToken);
        if (!isDesignatedFreeCard)
        {
            throw ApiException.Conflict(
                "live_voice_required",
                "Published Speaking cards use native realtime live voice. The legacy recorder is reserved for the designated free Speaking card.");
        }
    }

    public async Task<object> CreateSpeakingUploadSessionAsync(
        string userId,
        string attemptId,
        string? expectedContentId,
        string? expectedMockSessionId,
        CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetSpeakingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        EnsureSpeakingAttemptCanReceiveAudio(attempt);
        await EnsureSpeakingAttemptBindingAsync(attempt, expectedContentId, expectedMockSessionId, cancellationToken);
        var uploadId = $"upload-{Guid.NewGuid():N}";
        var upload = new UploadSession
        {
            Id = uploadId,
            AttemptId = attemptId,
            UploadUrl = platformLinks.BuildApiUrl($"/v1/speaking/upload-sessions/{uploadId}/content"),
            StorageKey = $"audio/{attemptId}/{uploadId}",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
            State = UploadState.Pending
        };
        db.UploadSessions.Add(upload);
        await db.SaveChangesAsync(cancellationToken);
        return new
        {
            uploadSessionId = upload.Id,
            uploadUrl = upload.UploadUrl,
            storageKey = upload.StorageKey,
            expiresAt = upload.ExpiresAt,
            httpMethod = "PUT",
            signed = false,
            requiresAuth = true
        };
    }

    public async Task<object> UploadSpeakingAudioAsync(
        string userId,
        string uploadSessionId,
        Stream content,
        string? contentType,
        CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var upload = await GetUploadSessionOwnedByUserAsync(userId, uploadSessionId, cancellationToken);
        var attempt = await GetSpeakingAttemptOwnedByUserAsync(userId, upload.AttemptId, cancellationToken);
        EnsureSpeakingAttemptCanReceiveAudio(attempt);
        await EnsureSpeakingAttemptBindingAsync(attempt, expectedContentId: null, expectedMockSessionId: null, cancellationToken);
        if (upload.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw ApiException.Conflict(
                "upload_session_expired",
                "This upload session has expired. Create a new upload session and try again.",
                [new ApiFieldError("uploadSessionId", "expired", "Request a new upload session before uploading audio.")]);
        }

        if (!MediaStoragePolicy.IsAllowedAudioContentType(contentType, storageSettings))
        {
            throw ApiException.Validation(
                "unsupported_audio_content_type",
                "Only supported audio formats can be uploaded.",
                [new ApiFieldError("audio", "unsupported_content_type", "Upload a browser-recorded WebM or another supported audio format.")]);
        }

        var bytesWritten = await WriteAudioToStorageAsync(upload.StorageKey, content, cancellationToken);
        if (bytesWritten == 0)
        {
            throw ApiException.Validation(
                "empty_audio_upload",
                "The uploaded audio file was empty.",
                [new ApiFieldError("audio", "empty", "Record some audio before uploading.")]);
        }

        upload.State = UploadState.Uploaded;
        await db.SaveChangesAsync(cancellationToken);

        return new
        {
            uploadSessionId = upload.Id,
            storageKey = upload.StorageKey,
            state = "uploaded",
            sizeBytes = bytesWritten,
            contentType = contentType
        };
    }

    public async Task<object> CompleteSpeakingUploadAsync(
        string userId,
        string attemptId,
        UploadCompleteRequest request,
        string? expectedContentId,
        string? expectedMockSessionId,
        CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetSpeakingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        EnsureSpeakingAttemptCanReceiveAudio(attempt);
        await EnsureSpeakingAttemptBindingAsync(attempt, expectedContentId, expectedMockSessionId, cancellationToken);
        var upload = await GetUploadSessionForCompletionAsync(userId, attemptId, request, cancellationToken);
        if (request.ConsentAccepted != true)
        {
            throw ApiException.Validation(
                "speaking_recording_consent_required",
                "Confirm recording consent before uploading speaking audio.",
                [new ApiFieldError("consent", "required", "Accept the speaking recording consent before submitting audio.")]);
        }
        if (upload.State != UploadState.Uploaded
            || !await fileStorage.ExistsAsync(upload.StorageKey, cancellationToken))
        {
            throw ApiException.Validation(
                "upload_binary_missing",
                "Upload the audio file before marking the speaking upload as complete.",
                [new ApiFieldError("audio", "missing", "Send the recording bytes to the upload URL first.")]);
        }

        var resolvedContentType = string.IsNullOrWhiteSpace(request.ContentType) ? null : request.ContentType;
        var storedLength = await fileStorage.LengthAsync(upload.StorageKey, cancellationToken);
        attempt.AudioUploadState = UploadState.Uploaded;
        attempt.AudioObjectKey = upload.StorageKey;
        attempt.AudioMetadataJson = JsonSupport.Serialize(new
        {
            fileName = request.FileName ?? $"{attemptId}.webm",
            sizeBytes = storedLength,
            reportedSizeBytes = request.SizeBytes,
            durationSeconds = request.DurationSeconds,
            captureMethod = request.CaptureMethod ?? "browser-recording",
            contentType = resolvedContentType,
            consent = new
            {
                accepted = true,
                acceptedAt = request.ConsentAcceptedAt ?? DateTimeOffset.UtcNow,
                consentText = request.ConsentText
            }
        });

        var existingTranscriptionJobs = await db.BackgroundJobs
            .Where(x => x.AttemptId == attemptId && x.Type == JobType.SpeakingTranscription && x.State != AsyncState.Failed)
            .ToListAsync(cancellationToken);
        var existingTranscriptionJob = existingTranscriptionJobs
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();
        if (existingTranscriptionJob is null)
        {
            await QueueJobAsync(JobType.SpeakingTranscription, attemptId: attemptId, resourceId: attemptId, cancellationToken: cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new { attemptId, audioUploadState = "uploaded", processingState = "queued", canSubmit = true };
    }

    public async Task<object> SubmitSpeakingAttemptAsync(string userId, string attemptId, CancellationToken cancellationToken)
        => await SubmitSpeakingAttemptAsync(userId, attemptId, expectedContentId: null, expectedMockSessionId: null, cancellationToken);

    public async Task<object> SubmitSpeakingAttemptAsync(
        string userId,
        string attemptId,
        string? expectedContentId,
        string? expectedMockSessionId,
        CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetSpeakingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        await EnsureSpeakingAttemptBindingAsync(attempt, expectedContentId, expectedMockSessionId, cancellationToken);

        // Idempotent: any existing evaluation (newest first, since a retry
        // adds a row) is the answer to a repeated submit.
        var existing = await LatestSpeakingEvaluationAsync(attemptId, cancellationToken);
        if (existing is not null)
        {
            return new { attemptId, evaluationId = existing.Id, state = ToAsyncState(existing.State) };
        }

        await EnsureSpeakingAudioReadyForSubmissionAsync(attempt, cancellationToken);

        // Two concurrent submits both got here: only the caller whose atomic
        // state flip wins queues an evaluation; the loser returns the
        // winner's evaluation instead of creating a duplicate.
        if (!await TryClaimSpeakingSubmissionAsync(db, attempt.Id, attempt.State, cancellationToken))
        {
            for (var poll = 0; poll < 20; poll++)
            {
                var winner = await LatestSpeakingEvaluationAsync(attemptId, cancellationToken);
                if (winner is not null)
                {
                    return new { attemptId, evaluationId = (string?)winner.Id, state = ToAsyncState(winner.State) };
                }
                await Task.Delay(150, cancellationToken);
            }
            return new { attemptId, evaluationId = (string?)null, state = "queued" };
        }

        return await QueueSpeakingEvaluationAsync(attempt, cancellationToken);
    }

    /// <summary>
    /// Compare-and-swap of the attempt from the state the caller read into
    /// <see cref="AttemptState.Evaluating"/>, as one conditional UPDATE so two
    /// racing submits cannot both win. The InMemory provider (tests only)
    /// cannot run ExecuteUpdate and is single-threaded there, so it always
    /// wins. ponytail: an attempt already stuck in Evaluating with no
    /// evaluation lets both racers through; that is a crash leftover, not a
    /// normal double-click.
    /// </summary>
    public static async Task<bool> TryClaimSpeakingSubmissionAsync(
        LearnerDbContext db,
        string attemptId,
        AttemptState observedState,
        CancellationToken cancellationToken)
    {
        if (db.Database.IsInMemory()) return true;

        var claimed = await db.Attempts
            .Where(x => x.Id == attemptId && x.State == observedState)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.State, AttemptState.Evaluating), cancellationToken);
        return claimed == 1;
    }

    private async Task<Evaluation?> LatestSpeakingEvaluationAsync(string attemptId, CancellationToken cancellationToken)
    {
        // Ordered in memory: SQLite cannot ORDER BY a DateTimeOffset.
        var evaluations = await db.Evaluations.AsNoTracking()
            .Where(x => x.AttemptId == attemptId)
            .ToListAsync(cancellationToken);
        return evaluations.OrderByDescending(x => x.LastTransitionAt).FirstOrDefault();
    }

    /// <summary>
    /// P0 (22 Sep 2026): the only server-side retry path for a Speaking
    /// evaluation that failed for a transient reason (most commonly
    /// <c>speaking_transcription_unavailable</c> — an AI-budget/provider
    /// blip, not a bad recording). Before this existed,
    /// <see cref="SubmitSpeakingAttemptAsync(string,string,string?,string?,CancellationToken)"/>'s
    /// "already submitted" branch echoed the same stale Failed evaluation
    /// back forever — a genuine dead end (spec: "a network failure must not
    /// silently discard the attempt"). Deliberately a SEPARATE method from
    /// SubmitSpeakingAttemptAsync (not a parameter on it) so an ordinary
    /// accidental double-submit keeps its existing safe, idempotent behavior
    /// (return the existing evaluation as-is) and only an explicit "Retry
    /// grading" action re-queues. Requeuing creates a NEW Evaluation row
    /// (fresh <c>LastTransitionAt</c>); every reader of "the current
    /// evaluation for this attempt" already picks the latest by
    /// <c>LastTransitionAt</c> (see <see cref="GetSpeakingProcessingAsync"/>),
    /// so this needs no other wiring. The audio itself is never re-uploaded —
    /// the same persisted recording is re-transcribed — so this cannot
    /// double-charge (Speaking credits are taken once, at card reveal, not
    /// here) and is safe to call as many times as the learner needs.
    /// </summary>
    public async Task<object> RetrySpeakingEvaluationAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetSpeakingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        var existing = await db.Evaluations
            .Where(x => x.AttemptId == attemptId)
            .OrderByDescending(x => x.LastTransitionAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is null || existing.State != AsyncState.Failed || !existing.Retryable)
        {
            throw ApiException.Conflict(
                "speaking_evaluation_not_retryable",
                "This Speaking attempt has no failed evaluation to retry.");
        }

        return await QueueSpeakingEvaluationAsync(attempt, cancellationToken);
    }

    private async Task<object> QueueSpeakingEvaluationAsync(Attempt attempt, CancellationToken cancellationToken)
    {
        attempt.State = AttemptState.Evaluating;
        attempt.SubmittedAt ??= DateTimeOffset.UtcNow;
        await LearnerWorkflowCoordinator.UpdateDiagnosticProgressAsync(db, attempt, AttemptState.Evaluating, cancellationToken);
        var evaluationId = $"se-{Guid.NewGuid():N}";
        // Speaking credits (2026-06-11 rebuild): the single speaking charge is
        // taken once per card at CARD REVEAL (prep start) — see
        // SpeakingSessionService.FinishWarmupAsync (practice) and
        // SpeakingExamService (exam). We deliberately do NOT debit again here at
        // grading time; doing so would double-charge the learner.

        var evaluation = new Evaluation
        {
            Id = evaluationId,
            AttemptId = attempt.Id,
            SubtestCode = "speaking",
            State = AsyncState.Queued,
            ScoreRange = "pending",
            ConfidenceBand = ConfidenceBand.Low,
            StrengthsJson = "[]",
            IssuesJson = "[]",
            CriterionScoresJson = "[]",
            FeedbackItemsJson = "[]",
            ModelExplanationSafe = "Speaking evaluation queued.",
            LearnerDisclaimer = "Training estimate pending.",
            StatusReasonCode = "queued",
            StatusMessage = "Speaking evaluation queued.",
            Retryable = true,
            RetryAfterMs = 2000,
            LastTransitionAt = DateTimeOffset.UtcNow
        };
        db.Evaluations.Add(evaluation);
        await QueueJobAsync(JobType.SpeakingEvaluation, attemptId: attempt.Id, resourceId: evaluation.Id, cancellationToken: cancellationToken);
        await RecordEventAsync(attempt.UserId, "task_submitted", new { attemptId = attempt.Id, evaluationId = evaluation.Id, subtest = "speaking", contentId = attempt.ContentId }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new { attemptId = attempt.Id, evaluationId = evaluation.Id, state = "queued", nextPollAfterMs = 2000 };
    }

    public async Task<object> GetSpeakingProcessingAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        await GetSpeakingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        var evaluations = await db.Evaluations.Where(x => x.AttemptId == attemptId).ToListAsync(cancellationToken);
        var evaluation = evaluations.OrderByDescending(x => x.LastTransitionAt).FirstOrDefault();
        var transcriptionJobs = await db.BackgroundJobs.Where(x => x.AttemptId == attemptId && x.Type == JobType.SpeakingTranscription).ToListAsync(cancellationToken);
        var transcriptionJob = transcriptionJobs.OrderByDescending(x => x.LastTransitionAt).FirstOrDefault();
        return new
        {
            attemptId,
            transcription = transcriptionJob is null
                ? (object)new Dictionary<string, object?> { ["state"] = "idle", ["statusReasonCode"] = null, ["retryAfterMs"] = null }
                : new Dictionary<string, object?> { ["state"] = ToAsyncState(transcriptionJob.State), ["statusReasonCode"] = transcriptionJob.StatusReasonCode, ["retryAfterMs"] = transcriptionJob.RetryAfterMs },
            evaluation = evaluation is null
                ? (object)new Dictionary<string, object?> { ["state"] = "idle", ["evaluationId"] = null, ["statusReasonCode"] = null, ["retryAfterMs"] = null }
                : new Dictionary<string, object?> { ["evaluationId"] = evaluation.Id, ["state"] = ToAsyncState(evaluation.State), ["statusReasonCode"] = evaluation.StatusReasonCode, ["retryAfterMs"] = evaluation.RetryAfterMs }
        };
    }

    // An evaluation can outlive its task (a content row removed, or never seeded): that is a "not found", never a 500.
    private async Task<ContentItem> LoadSpeakingTaskOfAttemptAsync(Attempt attempt, CancellationToken cancellationToken)
        => await db.ContentItems.FirstOrDefaultAsync(x => x.Id == attempt.ContentId, cancellationToken)
            ?? throw ApiException.NotFound("speaking_task_not_found", "The role play for this result is no longer available.");

    public async Task<object> GetSpeakingEvaluationSummaryAsync(string userId, string evaluationId, CancellationToken cancellationToken)
    {
        var evaluation = await GetEvaluationOwnedByUserAsync(userId, evaluationId, cancellationToken);
        var attempt = await db.Attempts.FirstAsync(x => x.Id == evaluation.AttemptId, cancellationToken);
        await RequireAttemptContentOwnProfessionAsync(userId, attempt.ContentId, cancellationToken);
        var content = await LoadSpeakingTaskOfAttemptAsync(attempt, cancellationToken);
        var examFamilyCode = NormalizeExamFamilyCode(attempt.ExamFamilyCode);
        var examFamilyLabel = FormatExamFamilyLabel(examFamilyCode);
        // No server-side "evaluation_viewed" write on this read path: the client tracks the view.

        // Stable Wave 1 contract: criterion-keyed feedback + readiness band.
        // See docs/SPEAKING-MODULE-PLAN.md §3 Wave 1.
        var criteria = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.CriterionScoresJson, []);
        var (estimatedScaledScore, readinessBandCode, criteriaSource) = ReadSpeakingBandFromAnalysis(attempt.AnalysisJson);
        var roleCard = BuildLearnerSpeakingTaskPayload(content, await LoadRolePlayCardAsync(content.Id, cancellationToken));
        var disclaimer = string.IsNullOrWhiteSpace(evaluation.LearnerDisclaimer)
            ? SpeakingContentStructure.PracticeDisclaimer
            : evaluation.LearnerDisclaimer;

        return new
        {
            evaluationId = evaluation.Id,
            attemptId = attempt.Id,
            taskId = content.Id,
            taskTitle = content.Title,
            examFamilyCode,
            examFamilyLabel,
            subtest = "speaking",
            state = ToAsyncState(evaluation.State),
            scoreRange = evaluation.ScoreRange,
            confidenceBand = evaluation.ConfidenceBand.ToString().ToLowerInvariant(),
            confidenceLabel = BuildConfidenceLabel(evaluation.ConfidenceBand),
            strengths = JsonSupport.Deserialize<List<string>>(evaluation.StrengthsJson, []),
            issues = JsonSupport.Deserialize<List<string>>(evaluation.IssuesJson, []),
            // Wave 1 contract additions ↓
            criteria,
            criterionScores = criteria,
            criteriaSource,
            readinessBand = readinessBandCode,
            readinessBandLabel = BuildSpeakingReadinessBandLabel(readinessBandCode),
            estimatedScaledScore,
            passThreshold = OetScoring.ScaledPassGradeB,
            rubricMax = OetScoring.SpeakingRubricMax,
            timing = new
            {
                prepTimeSeconds = roleCard.GetValueOrDefault("prepTimeSeconds"),
                roleplayTimeSeconds = roleCard.GetValueOrDefault("roleplayTimeSeconds"),
                recordedSeconds = attempt.ElapsedSeconds
            },
            workflow = new[]
            {
                "selection",
                "device_check",
                "prep",
                "roleplay_recording",
                "upload_submit",
                "processing_result",
                "transcript_review",
                "phrasing_drill",
                "expert_review_optional"
            },
            statusReasonCode = evaluation.StatusReasonCode,
            statusMessage = evaluation.StatusMessage,
            // True only when "Try grading again" (POST /v1/speaking/attempts/
            // {attemptId}/retry-evaluation) will be accepted; a queued row
            // also carries Retryable=true but is not learner-retryable.
            retryable = evaluation.State == AsyncState.Failed && evaluation.Retryable,
            retryAfterMs = evaluation.RetryAfterMs,
            // Wave 1 contract additions ↑
            generatedAt = evaluation.GeneratedAt,
            nextDrill = new
            {
                id = evaluation.Id,
                title = "Phrasing and transcript drill",
                description = "Review the transcript markers and practise stronger alternatives from this attempt.",
                route = $"/speaking/phrasing/{evaluation.Id}"
            },
            recommendedDrills = new[]
            {
                new
                {
                    id = $"phrasing-{evaluation.Id}",
                    title = "Phrasing and transcript drill",
                    description = "Practise stronger patient-centred alternatives from the marked transcript.",
                    route = $"/speaking/phrasing/{evaluation.Id}"
                },
                new
                {
                    id = "ai-patient-practice",
                    title = "AI patient conversation practice",
                    description = "Launch the existing conversation module for a grounded patient practice session.",
                    route = "/conversation"
                }
            },
            modelExplanationSafe = evaluation.ModelExplanationSafe,
            learnerDisclaimer = disclaimer,
            disclaimer,
            isOfficialScore = false,
            methodLabel = BuildAiMethodLabel("speaking"),
            provenanceLabel = $"{examFamilyLabel} practice estimate",
            humanReviewRecommended = ShouldRecommendHumanReview(evaluation.ConfidenceBand),
            escalationRecommended = ShouldRecommendHumanReview(evaluation.ConfidenceBand)
        };
    }

    private static (int? estimatedScaledScore, string readinessBandCode, string? criteriaSource) ReadSpeakingBandFromAnalysis(string analysisJson, string examFamilyCode)
        => ReadSpeakingBandFromAnalysis(analysisJson);

    private static (int? estimatedScaledScore, string readinessBandCode, string? criteriaSource) ReadSpeakingBandFromAnalysis(string analysisJson)
    {
        if (string.IsNullOrWhiteSpace(analysisJson))
        {
            return (null, OetScoring.SpeakingReadinessBandCode(OetScoring.SpeakingReadinessBand.NotReady), null);
        }
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(analysisJson);
            if (!doc.RootElement.TryGetProperty("speakingBand", out var band)) goto fallback;
            int? scaled = band.TryGetProperty("scaledEstimate", out var s) && s.TryGetInt32(out var v) ? v : null;
            string? readiness = band.TryGetProperty("readinessBand", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.String ? r.GetString() : null;
            string? source = band.TryGetProperty("criteriaSource", out var src) && src.ValueKind == System.Text.Json.JsonValueKind.String ? src.GetString() : null;
            // If readinessBand was not yet persisted (legacy attempts before
            // Wave 1), derive it from the scaled estimate so the contract
            // is always populated.
            readiness ??= scaled is { } sv
                ? OetScoring.SpeakingReadinessBandCode(OetScoring.SpeakingReadinessBandFromScaled(sv))
                : OetScoring.SpeakingReadinessBandCode(OetScoring.SpeakingReadinessBand.NotReady);
            return (scaled, readiness, source);
        }
        catch
        {
        }
    fallback:
        return (null, OetScoring.SpeakingReadinessBandCode(OetScoring.SpeakingReadinessBand.NotReady), null);
    }

    private static string BuildSpeakingReadinessBandLabel(string code) => code switch
    {
        "not_ready"  => "Not ready",
        "developing" => "Developing",
        "borderline" => "Borderline",
        "exam_ready" => "Exam-ready",
        "strong"     => "Strong",
        _             => "Not ready",
    };

    public async Task<object> GetSpeakingReviewAsync(string userId, string evaluationId, CancellationToken cancellationToken)
    {
        var evaluation = await GetEvaluationOwnedByUserAsync(userId, evaluationId, cancellationToken);
        var attempt = await db.Attempts.FirstAsync(x => x.Id == evaluation.AttemptId, cancellationToken);
        await RequireAttemptContentOwnProfessionAsync(userId, attempt.ContentId, cancellationToken);
        var content = await LoadSpeakingTaskOfAttemptAsync(attempt, cancellationToken);
        var disclaimer = string.IsNullOrWhiteSpace(evaluation.LearnerDisclaimer)
            ? SpeakingContentStructure.PracticeDisclaimer
            : evaluation.LearnerDisclaimer;
        return new
        {
            summary = await GetSpeakingEvaluationSummaryAsync(userId, evaluationId, cancellationToken),
            roleCard = BuildLearnerSpeakingTaskPayload(content, await LoadRolePlayCardAsync(content.Id, cancellationToken)),
            disclaimer,
            transcript = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(attempt.TranscriptJson, []),
            analysis = JsonSupport.Deserialize<Dictionary<string, object?>>(attempt.AnalysisJson, new Dictionary<string, object?>()),
            feedbackItems = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.FeedbackItemsJson, []),
            audioAvailable = !string.IsNullOrWhiteSpace(attempt.AudioObjectKey),
            audioUrl = string.IsNullOrWhiteSpace(attempt.AudioObjectKey)
                ? null
                : platformLinks.BuildApiUrl($"/v1/speaking/evaluations/{Uri.EscapeDataString(evaluationId)}/audio")
        };
    }

    public async Task<StoredMediaFile> GetSpeakingEvaluationAudioAsync(string userId, string evaluationId, CancellationToken cancellationToken)
    {
        var evaluation = await GetEvaluationOwnedByUserAsync(userId, evaluationId, cancellationToken);
        var attempt = await db.Attempts.FirstAsync(x => x.Id == evaluation.AttemptId, cancellationToken);
        if (string.IsNullOrWhiteSpace(attempt.AudioObjectKey))
        {
            throw ApiException.NotFound("audio_not_found", "No uploaded audio is available for this speaking evaluation.");
        }

        var metadata = JsonSupport.Deserialize(attempt.AudioMetadataJson, new Dictionary<string, object?>());
        var contentType = metadata.TryGetValue("contentType", out var value) ? value?.ToString() : null;
        return await fileStorage.OpenStoredMediaFileAsync(attempt.AudioObjectKey, contentType, cancellationToken);
    }

    private async Task<long> WriteAudioToStorageAsync(string storageKey, Stream source, CancellationToken cancellationToken)
    {
        var maxBytes = storageSettings.MaxUploadBytes > 0 ? storageSettings.MaxUploadBytes : 25L * 1024 * 1024;
        var buffer = new byte[81920];
        long totalBytes = 0;

        try
        {
            await using var destination = await fileStorage.OpenWriteAsync(storageKey, cancellationToken);
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                totalBytes += read;
                if (totalBytes > maxBytes)
                {
                    throw ApiException.Validation(
                        "audio_file_too_large",
                        $"Audio uploads must be {maxBytes / (1024 * 1024)} MB or smaller.",
                        [new ApiFieldError("audio", "too_large", "Record a shorter file or increase the configured upload limit.")]);
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        catch
        {
            await fileStorage.DeleteAsync(storageKey, cancellationToken);
            throw;
        }

        return totalBytes;
    }

    private async Task<UploadSession> GetUploadSessionOwnedByUserAsync(string userId, string uploadSessionId, CancellationToken cancellationToken)
    {
        var upload = await db.UploadSessions.FirstOrDefaultAsync(x => x.Id == uploadSessionId, cancellationToken)
            ?? throw ApiException.NotFound("upload_session_not_found", "The requested upload session was not found.");

        await GetAttemptOwnedByUserAsync(userId, upload.AttemptId, cancellationToken);
        return upload;
    }

    private async Task<UploadSession> GetUploadSessionForCompletionAsync(
        string userId,
        string attemptId,
        UploadCompleteRequest request,
        CancellationToken cancellationToken)
    {
        UploadSession? upload = null;
        if (!string.IsNullOrWhiteSpace(request.UploadSessionId))
        {
            upload = await GetUploadSessionOwnedByUserAsync(userId, request.UploadSessionId, cancellationToken);
            if (!string.Equals(upload.AttemptId, attemptId, StringComparison.Ordinal))
            {
                throw ApiException.Validation(
                    "upload_attempt_mismatch",
                    "The upload session does not belong to this speaking attempt.",
                    [new ApiFieldError("uploadSessionId", "mismatch", "Use the upload session created for this speaking attempt.")]);
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.StorageKey))
        {
            var uploadSessions = await db.UploadSessions
                .Where(x => x.AttemptId == attemptId && x.StorageKey == request.StorageKey)
                .ToListAsync(cancellationToken);
            upload = uploadSessions
                .OrderByDescending(x => x.ExpiresAt)
                .FirstOrDefault();
        }
        else
        {
            var uploadSessions = await db.UploadSessions
                .Where(x => x.AttemptId == attemptId)
                .ToListAsync(cancellationToken);
            upload = uploadSessions
                .OrderByDescending(x => x.ExpiresAt)
                .FirstOrDefault();
        }

        if (upload is null)
        {
            throw ApiException.Validation(
                "upload_session_required",
                "Create and use an upload session before completing the speaking upload.",
                [new ApiFieldError("uploadSessionId", "required", "Create a speaking upload session first.")]);
        }

        await GetAttemptOwnedByUserAsync(userId, upload.AttemptId, cancellationToken);
        return upload;
    }

    private async Task<Attempt> GetSpeakingAttemptOwnedByUserAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        if (!string.Equals(attempt.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.NotFound("speaking_attempt_not_found", "Speaking attempt not found.");
        }

        return attempt;
    }

    private static void EnsureSpeakingAttemptCanReceiveAudio(Attempt attempt)
    {
        if (attempt.State is AttemptState.Submitted or AttemptState.Evaluating or AttemptState.Completed)
        {
            throw ApiException.Conflict(
                "speaking_attempt_locked",
                "This speaking attempt has already been submitted and cannot receive another recording.",
                [new ApiFieldError("attemptId", "locked", "Start a new speaking attempt before uploading another recording.")]);
        }
    }

    private async Task EnsureSpeakingAudioReadyForSubmissionAsync(
        Attempt attempt,
        CancellationToken cancellationToken)
    {
        if (attempt.AudioUploadState != UploadState.Uploaded || string.IsNullOrWhiteSpace(attempt.AudioObjectKey))
        {
            throw ApiException.Validation(
                "speaking_audio_required",
                "Upload audio before submitting this speaking attempt.",
                [new ApiFieldError("audio", "required", "Complete the audio upload before submission.")]);
        }

        if (!await fileStorage.ExistsAsync(attempt.AudioObjectKey, cancellationToken)
            || await fileStorage.LengthAsync(attempt.AudioObjectKey, cancellationToken) <= 0)
        {
            throw ApiException.Validation(
                "speaking_audio_not_ready",
                "The speaking audio file is not ready for submission yet.",
                [new ApiFieldError("audio", "not_ready", "Upload the recording bytes again before submitting.")]);
        }
    }

    private async Task EnsureSpeakingAttemptBindingAsync(
        Attempt attempt,
        string? expectedContentId,
        string? expectedMockSessionId,
        CancellationToken cancellationToken)
    {
        var normalizedContentId = expectedContentId?.Trim();
        var normalizedMockSessionId = expectedMockSessionId?.Trim();

        if (!string.IsNullOrWhiteSpace(normalizedContentId)
            && !string.Equals(attempt.ContentId, normalizedContentId, StringComparison.Ordinal))
        {
            // The learner UI's upload/complete calls bind by the same id the
            // page URL carries throughout (RolePlayCard.Id for Speaking —
            // see CreateAttemptAsync's matching fallback), which differs
            // from attempt.ContentId (the resolved ContentItem shell id) by
            // design. Accept a RolePlayCard.Id that maps to this attempt's
            // ContentItem as a match instead of hard-mismatching.
            var mapsToSameContent = await db.RolePlayCards
                .AsNoTracking()
                .AnyAsync(c => c.Id == normalizedContentId && c.ContentItemId == attempt.ContentId, cancellationToken);
            if (!mapsToSameContent)
            {
                throw ApiException.Validation(
                    "speaking_attempt_content_mismatch",
                    "This speaking attempt does not belong to the requested task.",
                    [new ApiFieldError("contentId", "mismatch", "Use the attempt created for this speaking task.")]);
            }
        }

        if (!string.Equals(attempt.Context, "mock_set", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(normalizedMockSessionId))
            {
                throw ApiException.Validation(
                    "speaking_attempt_mock_session_mismatch",
                    "This speaking attempt does not belong to the requested mock session.",
                    [new ApiFieldError("mockSessionId", "mismatch", "Use the paired attempt created for this speaking mock session.")]);
            }

            return;
        }

        var sessionId = string.IsNullOrWhiteSpace(normalizedMockSessionId)
            ? attempt.ComparisonGroupId
            : normalizedMockSessionId;
        if (string.IsNullOrWhiteSpace(sessionId)
            || !string.Equals(attempt.ComparisonGroupId, sessionId, StringComparison.Ordinal))
        {
            throw ApiException.Validation(
                "speaking_attempt_mock_session_mismatch",
                "This speaking attempt does not belong to the requested mock session.",
                [new ApiFieldError("mockSessionId", "mismatch", "Use the paired attempt created for this speaking mock session.")]);
        }

        var session = await db.SpeakingMockSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == attempt.UserId, cancellationToken);
        if (session is null || (attempt.Id != session.Attempt1Id && attempt.Id != session.Attempt2Id))
        {
            throw ApiException.Validation(
                "speaking_attempt_mock_session_mismatch",
                "This speaking attempt does not belong to the requested mock session.",
                [new ApiFieldError("mockSessionId", "mismatch", "Use the paired attempt created for this speaking mock session.")]);
        }

    }

    /// <param name="card">
    /// The typed role-play card behind this ContentItem, when one exists.
    /// Every card authored through the admin wizard (and the whole imported
    /// corpus) writes <see cref="RolePlayCard"/> and leaves the ContentItem
    /// shell's <c>DetailJson</c> as <c>{}</c>, so reading DetailJson alone
    /// hands the learner a titled card with an empty background and zero task
    /// bullets. The card is the authoritative source; DetailJson is only
    /// consulted for legacy speaking content that has no card row.
    /// </param>
    private static Dictionary<string, object?> BuildLearnerSpeakingTaskPayload(
        ContentItem item,
        RolePlayCard? card = null)
    {
        var detail = SpeakingContentStructure.ExtractStructure(item.DetailJson);
        var candidate = SpeakingContentStructure.ToDictionary(SpeakingContentStructure.ReadValue(detail, "candidateCard"));

        var role = Trimmed(card?.CandidateRole)
                   ?? SpeakingContentStructure.ReadString(candidate, "candidateRole", "role")
                   ?? SpeakingContentStructure.ReadString(detail, "candidateRole", "role")
                   ?? "Candidate";
        var setting = Trimmed(card?.Setting)
                      ?? SpeakingContentStructure.ReadString(candidate, "setting")
                      ?? SpeakingContentStructure.ReadString(detail, "setting")
                      ?? "Clinical setting";
        var patient = Trimmed(card?.InterlocutorRole)
                      ?? SpeakingContentStructure.ReadString(candidate, "patientRole", "patient")
                      ?? SpeakingContentStructure.ReadString(detail, "patientRole", "patient")
                      ?? "Patient";
        patient = WithPatientIdentity(patient, card);
        var task = SpeakingContentStructure.ReadString(candidate, "task", "brief")
                   ?? SpeakingContentStructure.ReadString(detail, "task", "brief")
                   ?? "Complete the role play using patient-centred communication.";
        var background = Trimmed(card?.Background)
                         ?? SpeakingContentStructure.ReadString(candidate, "background")
                         ?? SpeakingContentStructure.ReadString(detail, "background", "caseNotes")
                         ?? item.CaseNotes
                         ?? string.Empty;
        var tasks = FirstNonEmptyList(
            card?.Tasks.ToList() ?? [],
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(candidate, "tasks")),
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(detail, "tasks")),
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(detail, "roleObjectives")));
        var warmUps = SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(detail, "warmUpQuestions"));
        var criteriaFocus = FirstNonEmptyList(
            JsonSupport.Deserialize<List<string>>(card?.CriteriaFocusJson, []),
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(detail, "criteriaFocus")),
            JsonSupport.Deserialize<List<string>>(item.CriteriaFocusJson, []));
        var prepSeconds = card?.PrepTimeSeconds
                          ?? SpeakingContentStructure.ReadInt(detail, "prepTimeSeconds")
                          ?? SpeakingContentStructure.DefaultPrepTimeSeconds;
        var roleplaySeconds = card?.RolePlayTimeSeconds
                              ?? SpeakingContentStructure.ReadInt(detail, "roleplayTimeSeconds")
                              ?? SpeakingContentStructure.DefaultRoleplayTimeSeconds;
        var disclaimer = Trimmed(card?.Disclaimer)
                         ?? SpeakingContentStructure.ReadString(detail, "disclaimer")
                         ?? SpeakingContentStructure.PracticeDisclaimer;

        var candidateCard = new Dictionary<string, object?>
        {
            ["role"] = role,
            ["candidateRole"] = role,
            ["setting"] = setting,
            ["patient"] = patient,
            ["patientRole"] = patient,
            ["task"] = task,
            ["brief"] = task,
            ["background"] = background,
            ["tasks"] = tasks
        };

        return new Dictionary<string, object?>
        {
            ["contentId"] = item.Id,
            ["contentType"] = item.ContentType,
            ["subtest"] = item.SubtestCode,
            ["title"] = item.Title,
            ["professionId"] = item.ProfessionId,
            ["difficulty"] = item.Difficulty,
            ["estimatedDurationMinutes"] = item.EstimatedDurationMinutes,
            ["criteriaFocus"] = criteriaFocus,
            ["criteriaFocusTags"] = criteriaFocus,
            ["scenarioType"] = item.ScenarioType,
            ["modeSupport"] = JsonSupport.Deserialize<List<string>>(item.ModeSupportJson, []),
            ["publishedRevisionId"] = item.PublishedRevisionId,
            ["status"] = ToContentStatus(item.Status),
            ["caseNotes"] = item.CaseNotes,
            ["candidateCard"] = candidateCard,
            ["role"] = role,
            ["setting"] = setting,
            ["patient"] = patient,
            ["task"] = task,
            ["brief"] = task,
            ["background"] = background,
            ["tasks"] = tasks,
            ["warmUpQuestions"] = warmUps,
            ["prepTimeSeconds"] = prepSeconds,
            ["roleplayTimeSeconds"] = roleplaySeconds,
            // Emotion / Goal / Topic are internal (AI patient prompt only) and
            // never sent to learners (owner, 23 Sep 2026).
            ["disclaimer"] = disclaimer,
            ["sourceAttribution"] = LearnerSafeAttribution(card?.SourceAttribution),
            ["compliance"] = new
            {
                learnerSafe = true,
                // The interlocutor card is intentionally stripped from
                // every learner-facing payload (Wave 2 of
                // docs/SPEAKING-MODULE-PLAN.md). The card lives in
                // ContentPaper.ExtractedTextJson["interlocutorCard"] and
                // is only projected to expert/admin audiences.
                hiddenInterlocutorCard = true,
                sourceProvenanceAvailable = !string.IsNullOrWhiteSpace(item.SourceProvenance),
                officialScore = false
            }
        };
    }

    private static List<string> FirstNonEmptyList(params List<string>[] lists)
        => lists.FirstOrDefault(list => list.Count > 0) ?? [];

    /// <summary>
    /// The rights notice printed on the source card, safe to show a learner.
    /// </summary>
    /// <remarks>
    /// The stored value ends with an internal provenance token naming the
    /// source scan and page — "© Cambridge Boxhill … [Nursing__Cards_p040 p40,41]".
    /// That token is our own bookkeeping and leaks internal file names, so it is
    /// stripped. 51 cards carry the token and nothing else; those return null
    /// rather than an empty notice.
    /// </remarks>
    private static string? LearnerSafeAttribution(string? stored)
        => Trimmed(ProvenanceToken.Replace(Trimmed(stored) ?? string.Empty, string.Empty));

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Folds the card's named patient and age into the interlocutor role line.
    /// The learner card renders <c>patient</c> as free prose under a
    /// "Patient / Client" heading, so this is the slot that identity belongs in
    /// and no extra payload field or render slot is needed.
    /// </summary>
    /// <remarks>
    /// Age is skipped when the name already states it, because several cards
    /// name a third party rather than the interlocutor — "Parent" +
    /// "Lily (8 months, daughter of the parent)" would otherwise repeat the age.
    /// </remarks>
    private static string WithPatientIdentity(string role, RolePlayCard? card)
    {
        var name = Trimmed(card?.PatientName);
        if (name is null)
        {
            return role;
        }

        var age = Trimmed(card?.PatientAge);
        var identity = age is null || name.Contains(age, StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{name}, {age}";

        return role.Contains(identity, StringComparison.OrdinalIgnoreCase)
            ? role
            : $"{role} \u2014 {identity}";
    }

    /// <summary>
    /// The role-play card behind a speaking ContentItem, or null for legacy
    /// speaking content that predates the card schema.
    /// </summary>
    private Task<RolePlayCard?> LoadRolePlayCardAsync(string contentItemId, CancellationToken cancellationToken)
        => db.RolePlayCards
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ContentItemId == contentItemId, cancellationToken);
}
