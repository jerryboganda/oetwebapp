using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Recalls;

namespace OetLearner.Api.Services.Listening;

public sealed class ListeningLearnerService(
    LearnerDbContext db,
    IContentEntitlementService entitlements,
    IRecallsAutoSeed? autoSeed = null)
{
    private const string Subtest = "listening";
    private const int CanonicalRawMax = OetScoring.ListeningReadingRawMax;

    public async Task<object> GetHomeAsync(string userId, CancellationToken ct)
    {
        await EnsureLearnerAsync(userId, ct);
        var profession = await GetLearnerProfessionAsync(userId, ct);

        var papers = await db.ContentPapers.AsNoTracking()
            .Include(p => p.Assets.Where(a => a.IsPrimary))
                .ThenInclude(a => a.MediaAsset)
            .Where(p => p.Status == ContentStatus.Published
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
                .Where(a => a.UserId == userId && a.SubtestCode == Subtest && contentIds.Contains(a.ContentId))
                .OrderByDescending(a => a.LastClientSyncAt ?? a.SubmittedAt ?? a.StartedAt)
                .ToListAsync(ct);

        var relationalAttempts = paperIds.Count == 0
            ? new List<ListeningAttempt>()
            : await db.ListeningAttempts.AsNoTracking()
                .Where(a => a.UserId == userId && paperIds.Contains(a.PaperId))
                .OrderByDescending(a => a.LastActivityAt)
                .ToListAsync(ct);

        var relationalAttemptIds = relationalAttempts.Select(a => a.Id).ToList();
        var relationalAnswerCounts = relationalAttemptIds.Count == 0
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : await db.ListeningAnswers.AsNoTracking()
                .Where(answer => relationalAttemptIds.Contains(answer.ListeningAttemptId))
                .GroupBy(answer => answer.ListeningAttemptId)
                .ToDictionaryAsync(group => group.Key, group => group.Count(), StringComparer.Ordinal, ct);

        var relationalQuestionCounts = paperIds.Count == 0
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : await db.ListeningQuestions.AsNoTracking()
                .Where(q => paperIds.Contains(q.PaperId))
                .GroupBy(q => q.PaperId)
                .ToDictionaryAsync(group => group.Key, group => group.Count(), StringComparer.Ordinal, ct);

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
                answeredCount = DeserializeAnswers(a.AnswersJson).Count(kv => !string.IsNullOrWhiteSpace(kv.Value)),
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
                    route = $"/listening/player/{Uri.EscapeDataString(a.PaperId)}?attemptId={Uri.EscapeDataString(a.Id)}&mode={Uri.EscapeDataString(ToApiMode(a.Mode))}"
                }))
            .OrderByDescending(a => a.LastClientSyncAt ?? a.StartedAt)
            .Take(3)
            .ToList();

        var recentResults = attempts
            .Where(a => a.State == AttemptState.Completed)
            .Select(a =>
            {
                var evaluation = evaluations.FirstOrDefault(e => e.AttemptId == a.Id);
                var score = ResolveScoreFromEvaluation(evaluation);
                return new
                {
                    attemptId = a.Id,
                    paperId = a.ContentId,
                    paperTitle = titleByContentId.GetValueOrDefault(a.ContentId, "Listening paper"),
                    rawScore = score.RawScore,
                    maxRawScore = score.MaxRawScore,
                    scaledScore = score.ScaledScore,
                    grade = score.Grade,
                    passed = score.Passed,
                    submittedAt = a.SubmittedAt,
                    scoreDisplay = FormatScoreDisplay(score),
                    route = $"/listening/results/{Uri.EscapeDataString(a.Id)}"
                };
            })
            .Concat(relationalAttempts
                .Where(a => a.Status == ListeningAttemptStatus.Submitted)
                .Select(a =>
                {
                    var evaluation = evaluations.FirstOrDefault(e => e.AttemptId == a.Id);
                    var score = ResolveScoreFromRelationalAttempt(a, evaluation);
                    return new
                    {
                        attemptId = a.Id,
                        paperId = a.PaperId,
                        paperTitle = titleByContentId.GetValueOrDefault(a.PaperId, "Listening paper"),
                        rawScore = score.RawScore,
                        maxRawScore = score.MaxRawScore,
                        scaledScore = score.ScaledScore,
                        grade = score.Grade,
                        passed = score.Passed,
                        submittedAt = a.SubmittedAt,
                        scoreDisplay = FormatScoreDisplay(score),
                        route = $"/listening/results/{Uri.EscapeDataString(a.Id)}"
                    };
                }))
            .OrderByDescending(result => result.submittedAt)
            .Take(5)
            .ToList();

        var latestCompletedAttempt = attempts.FirstOrDefault(a => a.State == AttemptState.Completed);
        var latestRelationalAttempt = relationalAttempts.FirstOrDefault(a => a.Status == ListeningAttemptStatus.Submitted);
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
        var paperDtos = new List<object>();
        foreach (var paper in papers)
        {
            try
            {
                var lastGeneric = attempts.FirstOrDefault(a => a.ContentId == paper.Id);
                var lastRelational = relationalAttempts.FirstOrDefault(a => a.PaperId == paper.Id);
                paperDtos.Add(PaperHomeDto(
                    paper,
                    BuildPaperLastAttemptDto(paper.Id, lastGeneric, lastRelational),
                    relationalQuestionCounts.GetValueOrDefault(paper.Id)));
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
            partCollections = BuildPartCollections(paperDtos, featuredTasks),
            transcriptBackedReview = new
            {
                title = "Transcript-backed review",
                route = latestCompletedAttempt is null && latestRelationalAttempt is null ? null : $"/listening/review/{latestCompletedAttempt?.Id ?? latestRelationalAttempt?.Id}",
                availableAfterAttempt = true,
                latestAttemptId = latestCompletedAttempt?.Id ?? latestRelationalAttempt?.Id,
                latestScoreDisplay = latestEvaluation is not null
                    ? FormatScoreDisplay(ResolveScoreFromEvaluation(latestEvaluation))
                    : latestRelationalAttempt is null ? null : FormatScoreDisplay(ResolveScoreFromRelationalAttempt(latestRelationalAttempt, null))
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
                papers = paperDtos.Count == 0 ? "No published Listening papers are ready yet. The demo task remains available until real papers are published." : null,
                activeAttempts = activeAttempts.Count == 0 ? "No in-progress Listening attempt." : null,
                recentResults = recentResults.Count == 0 ? "Complete a Listening task to unlock transcript-backed review and canonical OET score display." : null
            }
        };
    }

    public async Task<object> GetSessionAsync(string userId, string paperId, string? mode, string? attemptId, CancellationToken ct)
    {
        await EnsureLearnerAsync(userId, ct);
        await RequirePaperAccessIfAuthoredAsync(userId, paperId, ct);
        var source = await ResolveSourceAsync(paperId, ct);
        var normalizedMode = NormalizeMode(mode);
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
        }
        else
        {
            var requestedRelationalMode = ToRelationalMode(normalizedMode);
            if (source.UsesRelationalStructure)
            {
                relationalAttempt = await db.ListeningAttempts.AsNoTracking()
                    .Where(a => a.UserId == userId
                        && a.PaperId == source.Id
                        && a.Mode == requestedRelationalMode
                        && a.Status == ListeningAttemptStatus.InProgress)
                    .OrderByDescending(a => a.LastActivityAt)
                    .FirstOrDefaultAsync(ct);
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

        var questions = source.Questions.Select(LearnerQuestionDto).ToList();
        var answers = relationalAttempt is not null
            ? await LoadRelationalAnswersAsync(relationalAttempt.Id, ct)
            : attempt is null ? new Dictionary<string, string?>() : DeserializeAnswers(attempt.AnswersJson);
        var effectiveMode = relationalAttempt is not null
            ? ToApiMode(relationalAttempt.Mode)
            : attempt?.Mode ?? normalizedMode;
        return new
        {
            paper = SourceDto(source),
            attempt = relationalAttempt is not null
                ? RelationalAttemptDto(relationalAttempt, answers)
                : attempt is null ? null : AttemptDto(attempt, answers),
            questions,
            modePolicy = new
            {
                mode = effectiveMode,
                canPause = !IsExamMode(effectiveMode),
                canScrub = !IsExamMode(effectiveMode),
                onePlayOnly = IsExamMode(effectiveMode),
                autosave = true,
                transcriptPolicy = "per_item_post_attempt",
                // Phase 9 tail: presentation hints so the player can render
                // the correct chrome (kiosk on home, printable booklet on
                // paper). The graded-integrity invariants stay encoded in
                // onePlayOnly / canScrub / canPause above.
                presentationStyle = effectiveMode switch
                {
                    "home" => "kiosk_fullscreen",
                    "paper" => "printable_booklet",
                    "exam" => "exam_standard",
                    _ => "practice"
                },
                integrityLockRequired = effectiveMode == "home",
                printableBooklet = effectiveMode == "paper"
            },
            scoring = new
            {
                maxRawScore = CanonicalRawMax,
                passRawScore = OetScoring.ListeningReadingRawPass,
                passScaledScore = OetScoring.ScaledPassGradeB
            },
            readiness = new
            {
                objectiveReady = source.Questions.Count > 0,
                questionCount = source.Questions.Count,
                audioAvailable = !string.IsNullOrWhiteSpace(source.AudioUrl),
                missingReason = source.Questions.Count == 0
                    ? "This paper has media assets but no structured Listening question map yet, so graded attempts are disabled."
                    : null
            }
        };
    }

    public async Task<object> StartAttemptAsync(string userId, string paperId, string? mode, CancellationToken ct)
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
        if (source.UsesRelationalStructure)
        {
            return await StartRelationalAttemptAsync(userId, source, normalizedMode, ct);
        }

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

        var attempt = new Attempt
        {
            Id = $"la-{Guid.NewGuid():N}",
            UserId = userId,
            ContentId = source.Id,
            SubtestCode = Subtest,
            Context = source.SourceKind,
            Mode = normalizedMode,
            State = AttemptState.InProgress,
            StartedAt = DateTimeOffset.UtcNow,
            DeviceType = "web",
            ComparisonGroupId = $"listening-{source.Id}",
            AnswersJson = "{}"
        };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        return AttemptDto(attempt, new Dictionary<string, string?>());
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
        EnsureAttemptCanMutate(attempt);
        var source = await ResolveSourceAsync(attempt.ContentId, ct);
        if (!source.Questions.Any(q => string.Equals(q.Id, questionId, StringComparison.Ordinal)))
        {
            throw ApiException.Validation("listening_question_not_found", "This question does not belong to the Listening attempt.");
        }

        var answers = DeserializeAnswers(attempt.AnswersJson);
        answers[questionId] = request.UserAnswer;
        attempt.AnswersJson = JsonSupport.Serialize(answers);
        attempt.LastClientSyncAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<object> HeartbeatAsync(string userId, string attemptId, HeartbeatRequest request, CancellationToken ct)
    {
        await EnsureLearnerMutationAllowedAsync(userId, ct);
        var relationalAttempt = await TryGetRelationalAttemptOwnedByUserAsync(userId, attemptId, asNoTracking: false, ct);
        if (relationalAttempt is not null)
        {
            EnsureRelationalAttemptCanMutate(relationalAttempt);
            relationalAttempt.LastActivityAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return new { attemptId = relationalAttempt.Id, elapsedSeconds = request.ElapsedSeconds, lastClientSyncAt = relationalAttempt.LastActivityAt };
        }

        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, ct);
        EnsureAttemptCanMutate(attempt);
        attempt.ElapsedSeconds = request.ElapsedSeconds;
        attempt.LastClientSyncAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.DeviceType)) attempt.DeviceType = request.DeviceType;
        await db.SaveChangesAsync(ct);
        return new { attemptId = attempt.Id, attempt.ElapsedSeconds, attempt.LastClientSyncAt };
    }

    public async Task<object> SubmitAsync(string userId, string attemptId, CancellationToken ct)
    {
        await EnsureLearnerMutationAllowedAsync(userId, ct);
        var relationalAttempt = await TryGetRelationalAttemptOwnedByUserAsync(userId, attemptId, asNoTracking: false, ct);
        if (relationalAttempt is not null)
        {
            return await SubmitRelationalAttemptAsync(userId, relationalAttempt, ct);
        }

        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, ct);
        var source = await ResolveSourceAsync(attempt.ContentId, ct);

        if (source.Questions.Count == 0)
        {
            throw ApiException.Validation("listening_questions_missing", "This Listening attempt has no structured questions to grade.");
        }

        var existing = await db.Evaluations.FirstOrDefaultAsync(e => e.AttemptId == attempt.Id, ct);
        if (attempt.State == AttemptState.Completed && existing is not null)
        {
            return BuildReview(attempt, source, existing);
        }

        var review = BuildReview(attempt, source);
        var score = new ListeningScoreDto(
            review.RawScore,
            review.MaxRawScore,
            review.ScaledScore,
            review.Grade,
            review.Passed);

        attempt.State = AttemptState.Completed;
        attempt.SubmittedAt = DateTimeOffset.UtcNow;
        attempt.CompletedAt = attempt.SubmittedAt;
        attempt.LastClientSyncAt = DateTimeOffset.UtcNow;

        var evaluation = new Evaluation
        {
            Id = $"le-{Guid.NewGuid():N}",
            AttemptId = attempt.Id,
            SubtestCode = Subtest,
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
                .Where(item => !item.IsCorrect)
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
        await db.SaveChangesAsync(ct);

        // Recalls auto-seed: turn wrong free-text listening answers into
        // starred SM-2 cards. Best-effort — failures must never break grading.
        if (autoSeed is not null)
        {
            try
            {
                var wrongFreeText = review.ItemReview
                    .Where(item => !item.IsCorrect && !string.IsNullOrWhiteSpace(item.CorrectAnswer))
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

        return BuildReview(attempt, source, evaluation);
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

            var relationalSource = await ResolveSourceAsync(relationalAttempt.PaperId, ct);
            var relationalEvaluation = await db.Evaluations.AsNoTracking()
                .Where(e => e.AttemptId == relationalAttempt.Id)
                .OrderByDescending(e => e.GeneratedAt)
                .FirstOrDefaultAsync(ct);
            var answers = await LoadRelationalAnswersAsync(relationalAttempt.Id, ct);
            return BuildReview(relationalAttempt, relationalSource, answers, relationalEvaluation);
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

        var eventType = string.IsNullOrWhiteSpace(request.EventType)
            ? "unknown"
            : request.EventType.Trim();
        if (eventType.Length > 64) eventType = eventType[..64];

        var now = DateTimeOffset.UtcNow;
        if (relationalAttempt is not null)
        {
            relationalAttempt.LastActivityAt = now;
        }
        else if (attempt is not null)
        {
            attempt.LastClientSyncAt = now;
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
                mode = relationalAttempt is not null ? ToApiMode(relationalAttempt.Mode) : attempt!.Mode,
                request.Details,
                serverRecordedAt = now,
            }),
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task<object> StartRelationalAttemptAsync(string userId, ListeningSource source, string normalizedMode, CancellationToken ct)
    {
        var relationalMode = ToRelationalMode(normalizedMode);
        var existing = await db.ListeningAttempts
            .Where(a => a.UserId == userId
                && a.PaperId == source.Id
                && a.Mode == relationalMode
                && a.Status == ListeningAttemptStatus.InProgress)
            .OrderByDescending(a => a.LastActivityAt)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            return RelationalAttemptDto(existing, await LoadRelationalAnswersAsync(existing.Id, ct));
        }

        var policy = await ResolveListeningPolicyAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var isExamLike = IsExamMode(normalizedMode);
        var attempt = new ListeningAttempt
        {
            Id = $"lat-{Guid.NewGuid():N}",
            UserId = userId,
            PaperId = source.Id,
            StartedAt = now,
            LastActivityAt = now,
            DeadlineAt = isExamLike ? now.AddMinutes(Math.Max(1, policy.FullPaperTimerMinutes)).AddSeconds(policy.GracePeriodSeconds) : null,
            Status = ListeningAttemptStatus.InProgress,
            Mode = relationalMode,
            MaxRawScore = Math.Clamp(source.Questions.Sum(q => q.Points), 1, CanonicalRawMax),
            PolicySnapshotJson = JsonSupport.Serialize(new
            {
                policy.Id,
                policy.FullPaperTimerMinutes,
                policy.GracePeriodSeconds,
                mode = normalizedMode,
                onePlayOnly = IsExamMode(normalizedMode),
                presentationStyle = normalizedMode == "home"
                    ? "kiosk_fullscreen"
                    : normalizedMode == "paper" ? "printable_booklet" : normalizedMode,
            }),
            ScopeJson = JsonSupport.Serialize(new { mode = normalizedMode, sourceKind = source.SourceKind }),
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
            Details = $"paper={source.Id}; mode={normalizedMode}; structure=relational",
        });
        await db.SaveChangesAsync(ct);
        return RelationalAttemptDto(attempt, new Dictionary<string, string?>());
    }

    private async Task SaveRelationalAnswerAsync(
        string userId,
        ListeningAttempt attempt,
        string questionId,
        string? userAnswer,
        CancellationToken ct)
    {
        EnsureRelationalAttemptCanMutate(attempt);
        var question = await db.ListeningQuestions.AsNoTracking()
            .Where(q => q.Id == questionId && q.PaperId == attempt.PaperId)
            .Select(q => new { q.Id })
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.Validation("listening_question_not_found", "This question does not belong to the Listening attempt.");

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
        await db.SaveChangesAsync(ct);
    }

    private async Task<object> SubmitRelationalAttemptAsync(string userId, ListeningAttempt attempt, CancellationToken ct)
    {
        var source = await ResolveSourceAsync(attempt.PaperId, ct);
        if (source.Questions.Count == 0)
        {
            throw ApiException.Validation("listening_questions_missing", "This Listening attempt has no structured questions to grade.");
        }

        var existing = await db.Evaluations.FirstOrDefaultAsync(e => e.AttemptId == attempt.Id, ct);
        var answers = await LoadRelationalAnswersAsync(attempt.Id, ct);
        if (attempt.Status == ListeningAttemptStatus.Submitted && existing is not null)
        {
            return BuildReview(attempt, source, answers, existing);
        }

        EnsureRelationalAttemptCanMutate(attempt);
        var review = BuildReview(attempt, source, answers);
        var itemByQuestionId = review.ItemReview.ToDictionary(item => item.QuestionId, StringComparer.Ordinal);
        var answerRows = await db.ListeningAnswers
            .Where(answer => answer.ListeningAttemptId == attempt.Id)
            .ToListAsync(ct);
        foreach (var row in answerRows)
        {
            if (!itemByQuestionId.TryGetValue(row.ListeningQuestionId, out var item)) continue;
            row.IsCorrect = item.IsCorrect;
            row.PointsEarned = item.PointsEarned;
            row.SelectedDistractorCategory = ResolveSelectedDistractorCategory(item);
        }

        var now = DateTimeOffset.UtcNow;
        attempt.Status = ListeningAttemptStatus.Submitted;
        attempt.SubmittedAt = now;
        attempt.LastActivityAt = now;
        attempt.RawScore = review.RawScore;
        attempt.ScaledScore = review.ScaledScore;

        var score = new ListeningScoreDto(
            review.RawScore,
            review.MaxRawScore,
            review.ScaledScore,
            review.Grade,
            review.Passed);
        var evaluation = CreateEvaluation(attempt.Id, score, review);
        db.Evaluations.Add(evaluation);
        await LearnerWorkflowCoordinator.QueueStudyPlanRegenerationAsync(db, userId, ct);
        await db.SaveChangesAsync(ct);
        return BuildReview(attempt, source, answers, evaluation);
    }

    private async Task<Dictionary<string, string?>> LoadRelationalAnswersAsync(string attemptId, CancellationToken ct)
    {
        var rows = await db.ListeningAnswers.AsNoTracking()
            .Where(answer => answer.ListeningAttemptId == attemptId)
            .Select(answer => new { answer.ListeningQuestionId, answer.UserAnswerJson })
            .ToListAsync(ct);
        return rows.ToDictionary(
            row => row.ListeningQuestionId,
            row => DecodeRelationalAnswer(row.UserAnswerJson),
            StringComparer.Ordinal);
    }

    private static string? DecodeRelationalAnswer(string? json)
        => ReadJsonString(json);

    private async Task<ListeningPolicy> ResolveListeningPolicyAsync(CancellationToken ct)
        => await db.ListeningPolicies.AsNoTracking().FirstOrDefaultAsync(policy => policy.Id == "global", ct)
            ?? new ListeningPolicy { Id = "global", FullPaperTimerMinutes = 45, GracePeriodSeconds = 30 };

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

    private static void EnsureRelationalAttemptCanMutate(ListeningAttempt attempt)
    {
        if (attempt.Status != ListeningAttemptStatus.InProgress)
        {
            throw ApiException.Validation(
                "listening_attempt_locked",
                "This Listening attempt is already submitted or expired and can no longer be changed.");
        }
    }

    private static ListeningAttemptMode ToRelationalMode(string mode) => mode switch
    {
        "home" => ListeningAttemptMode.Home,
        "paper" => ListeningAttemptMode.Paper,
        "practice" => ListeningAttemptMode.Learning,
        _ => ListeningAttemptMode.Exam,
    };

    private static string ToApiMode(ListeningAttemptMode mode) => mode switch
    {
        ListeningAttemptMode.Home => "home",
        ListeningAttemptMode.Paper => "paper",
        ListeningAttemptMode.Learning => "practice",
        ListeningAttemptMode.Drill => "practice",
        ListeningAttemptMode.MiniTest => "practice",
        ListeningAttemptMode.ErrorBank => "practice",
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
            _ => null,
        };
    }

    private static Evaluation CreateEvaluation(string attemptId, ListeningScoreDto score, ListeningReviewDto review)
        => new()
        {
            Id = $"le-{Guid.NewGuid():N}",
            AttemptId = attemptId,
            SubtestCode = Subtest,
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
                .Where(item => !item.IsCorrect)
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

        if (paper.Status != ContentStatus.Published)
        {
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

        var relationalQuestions = await db.ListeningQuestions.AsNoTracking()
            .Include(q => q.Part)
            .Include(q => q.Options)
            .Where(q => q.PaperId == paper.Id)
            .OrderBy(q => q.QuestionNumber)
            .ToListAsync(ct);

        IReadOnlyList<ListeningQuestion> questions;
        IReadOnlyList<ListeningTranscriptSegmentDto> segments;
        IReadOnlyList<ListeningExtractMetaDto> extracts;
        var usesRelationalStructure = relationalQuestions.Count > 0;

        if (usesRelationalStructure)
        {
            var parts = await db.ListeningParts.AsNoTracking()
                .Where(part => part.PaperId == paper.Id)
                .ToDictionaryAsync(part => part.Id, part => part.PartCode, ct);
            var partIds = parts.Keys.ToList();
            var relationalExtracts = partIds.Count == 0
                ? new List<ListeningExtract>()
                : await db.ListeningExtracts.AsNoTracking()
                    .Where(extract => partIds.Contains(extract.ListeningPartId))
                    .OrderBy(extract => extract.DisplayOrder)
                    .ToListAsync(ct);

            questions = relationalQuestions.Select(MapRelationalQuestion).ToList();
            extracts = relationalExtracts
                .Select((extract, index) => MapRelationalExtract(extract, parts.GetValueOrDefault(extract.ListeningPartId), index))
                .OrderBy(extract => PartCodeOrder(extract.PartCode))
                .ThenBy(extract => extract.DisplayOrder)
                .ToList();
            segments = relationalExtracts
                .SelectMany(extract => ExtractTranscriptSegmentsFromJson(
                    extract.TranscriptSegmentsJson,
                    PartCodeString(parts.GetValueOrDefault(extract.ListeningPartId))))
                .OrderBy(segment => segment.StartMs)
                .ToList();
        }
        else
        {
            var questionMap = JsonSupport.Deserialize<Dictionary<string, object?>>(paper.ExtractedTextJson, new Dictionary<string, object?>());
            questions = ExtractQuestions(questionMap.TryGetValue("listeningQuestions", out var listeningQuestions)
                    ? listeningQuestions
                    : questionMap.GetValueOrDefault("questions"))
                .ToList();
            segments = ExtractTranscriptSegments(questionMap.GetValueOrDefault("listeningTranscriptSegments"));
            extracts = ExtractExtractMetadata(questionMap.GetValueOrDefault("listeningExtracts"));
        }

        return new ListeningSource(
            Id: paper.Id,
            SourceKind: "content_paper",
            Title: paper.Title,
            Slug: paper.Slug,
            Difficulty: paper.Difficulty,
            EstimatedDurationMinutes: paper.EstimatedDurationMinutes,
            ScenarioType: "oet_listening",
            AudioUrl: AssetDownloadPath(assetByRole.GetValueOrDefault(PaperAssetRole.Audio)),
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
            UsesRelationalStructure: usesRelationalStructure);
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
    private static IReadOnlyList<ListeningExtractMetaDto> ExtractExtractMetadata(object? raw)
    {
        if (raw is null) return [];
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
                var speakers = ParseSpeakers(seg.GetValueOrDefault("speakers"));
                output.Add(new ListeningExtractMetaDto(
                    PartCode: partCode,
                    DisplayOrder: displayOrder,
                    Kind: kind,
                    Title: title,
                    AccentCode: accentCode,
                    Speakers: speakers,
                    AudioStartMs: audioStartMs,
                    AudioEndMs: audioEndMs));
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
            "A1" or "A2" or "B" or "C1" or "C2" => normalized,
            "A" => "A1",
            "C" => "C1",
            _ => null,
        };
    }

    private static int PartCodeOrder(string partCode) => partCode switch
    {
        "A1" => 1,
        "A2" => 2,
        "B" => 3,
        "C1" => 4,
        "C2" => 5,
        _ => 99,
    };

    private static string NormalizeExtractKind(string? raw, string partCode)
    {
        var normalized = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "consultation" or "workplace" or "presentation") return normalized;
        return partCode switch
        {
            "B" => "workplace",
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
        var options = question.Options
            .OrderBy(option => option.DisplayOrder)
            .ToList();
        var optionTexts = options.Select(option => option.Text).ToList();
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
            PartCode: PartCodeString(question.Part?.PartCode ?? ListeningPartCode.A1),
            Text: question.Stem,
            Type: question.QuestionType == ListeningQuestionType.MultipleChoice3 ? "multiple_choice_3" : "short_answer",
            Options: optionTexts,
            CorrectAnswer: correctDisplay,
            AcceptedAnswers: accepted,
            Explanation: question.ExplanationMarkdown,
            SkillTag: question.SkillTag,
            AllowTranscriptReveal: true,
            TranscriptExcerpt: question.TranscriptEvidenceText,
            DistractorExplanation: null,
            Points: Math.Max(1, question.Points),
            OptionDistractorWhy: options.Select(option => option.WhyWrongMarkdown).ToList(),
            OptionDistractorCategory: options.Select(option => option.DistractorCategory is null ? null : DistractorCategoryString(option.DistractorCategory.Value)).ToList(),
            SpeakerAttitude: question.SpeakerAttitude is null ? null : SpeakerAttitudeString(question.SpeakerAttitude.Value),
            TranscriptEvidenceStartMs: question.TranscriptEvidenceStartMs,
            TranscriptEvidenceEndMs: question.TranscriptEvidenceEndMs);
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
        int index)
        => new(
            PartCode: PartCodeString(partCode),
            DisplayOrder: extract.DisplayOrder,
            Kind: ExtractKindString(extract.Kind),
            Title: string.IsNullOrWhiteSpace(extract.Title) ? $"Extract {index + 1}" : extract.Title,
            AccentCode: extract.AccentCode,
            Speakers: ReadSpeakersJson(extract.SpeakersJson),
            AudioStartMs: extract.AudioStartMs,
            AudioEndMs: extract.AudioEndMs);

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
                _ => doc.RootElement.ToString(),
            };
        }
        catch (JsonException)
        {
            return json;
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
        ListeningPartCode.B => "B",
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

    private ListeningReviewDto BuildReview(Attempt attempt, ListeningSource source, Evaluation? evaluation = null)
        => BuildReviewCore(
            AttemptId: attempt.Id,
            CompletedAt: attempt.CompletedAt ?? attempt.SubmittedAt,
            Answers: DeserializeAnswers(attempt.AnswersJson),
            Source: source,
            Evaluation: evaluation);

    private ListeningReviewDto BuildReview(
        ListeningAttempt attempt,
        ListeningSource source,
        IReadOnlyDictionary<string, string?> answers,
        Evaluation? evaluation = null)
        => BuildReviewCore(
            AttemptId: attempt.Id,
            CompletedAt: attempt.SubmittedAt,
            Answers: answers,
            Source: source,
            Evaluation: evaluation);

    private ListeningReviewDto BuildReviewCore(
        string AttemptId,
        DateTimeOffset? CompletedAt,
        IReadOnlyDictionary<string, string?> Answers,
        ListeningSource Source,
        Evaluation? Evaluation)
    {
        var orderedQuestions = Source.Questions.OrderBy(q => q.Number).ToList();
        var items = orderedQuestions
            .Select(q => ReviewItemDto(q, Answers.GetValueOrDefault(q.Id), orderedQuestions))
            .ToList();
        var raw = Math.Clamp(items.Sum(i => i.PointsEarned), 0, CanonicalRawMax);
        var score = OetScoring.GradeListeningReading(Subtest, raw);
        var clusters = BuildErrorClusters(items);
        var recommended = clusters.Count > 0
            ? BuildDrill(clusters[0].ErrorType, Source.Id, AttemptId)
            : BuildDrill("detail_capture", Source.Id, AttemptId);
        var allowedTranscriptIds = items
            .Where(item => item.Transcript is not null && item.Transcript.Allowed)
            .Select(item => item.QuestionId)
            .ToList();

        return new ListeningReviewDto(
            EvaluationId: Evaluation?.Id,
            AttemptId: AttemptId,
            Paper: SourceDto(Source),
            RawScore: score.RawCorrect,
            MaxRawScore: score.RawMax,
            ScaledScore: score.ScaledScore,
            Grade: score.Grade,
            Passed: score.Passed,
            ScoreDisplay: $"{score.RawCorrect} / {score.RawMax} \u2022 {score.ScaledScore} / 500 \u2022 Grade {score.Grade}",
            CorrectCount: items.Count(i => i.IsCorrect),
            IncorrectCount: items.Count(i => !i.IsCorrect && !string.IsNullOrWhiteSpace(i.LearnerAnswer)),
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
            Strengths: BuildStrengths(score.RawCorrect, items),
            Issues: BuildIssues(items),
            GeneratedAt: Evaluation?.GeneratedAt ?? CompletedAt);
    }

    private static ListeningReviewItemDto ReviewItemDto(ListeningQuestion q, string? learnerAnswer, IReadOnlyList<ListeningQuestion> allQuestions)
    {
        var isCorrect = q.AcceptedAnswers.Any(answer => MatchesObjectiveAnswer(learnerAnswer, answer));
        var errorType = isCorrect ? null : ObjectiveErrorType(q, learnerAnswer, allQuestions);
        var transcript = q.AllowTranscriptReveal
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
            Prompt: q.Text,
            Type: q.Type,
            LearnerAnswer: learnerAnswer ?? string.Empty,
            CorrectAnswer: q.CorrectAnswer,
            IsCorrect: isCorrect,
            PointsEarned: isCorrect ? q.Points : 0,
            MaxPoints: q.Points,
            Explanation: q.Explanation ?? (isCorrect ? "Correct." : "Review the transcript clue and answer key."),
            ErrorType: errorType,
            Options: q.Options,
            Transcript: transcript,
            DistractorExplanation: q.DistractorExplanation,
            OptionAnalysis: optionAnalysis,
            SpeakerAttitude: q.SpeakerAttitude,
            TranscriptEvidenceStartMs: q.TranscriptEvidenceStartMs,
            TranscriptEvidenceEndMs: q.TranscriptEvidenceEndMs);
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
            .Where(item => !item.IsCorrect)
            .GroupBy(item => item.ErrorType ?? "detail_capture")
            .Select(group => new ListeningErrorClusterDto(
                ErrorType: group.Key,
                Label: ObjectiveErrorTypeLabel(group.Key),
                Count: group.Count(),
                AffectedQuestionIds: group.Select(item => item.QuestionId).ToList()))
            .OrderByDescending(cluster => cluster.Count)
            .ThenBy(cluster => cluster.Label, StringComparer.Ordinal)
            .ToList();

    private static List<string> BuildStrengths(int rawScore, IReadOnlyCollection<ListeningReviewItemDto> items)
    {
        if (items.Count == 0) return ["No graded Listening items were available."];
        if (rawScore >= OetScoring.ListeningReadingRawPass)
        {
            return ["Your raw Listening score is at or above the OET Grade B practice threshold."];
        }

        var correct = items.Count(i => i.IsCorrect);
        return correct == 0
            ? ["You completed the Listening attempt and now have item-level evidence to review."]
            : [$"You captured {correct} authored Listening item{(correct == 1 ? string.Empty : "s")} correctly."];
    }

    private static List<string> BuildIssues(IReadOnlyCollection<ListeningReviewItemDto> items)
        => items
            .Where(item => !item.IsCorrect)
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

            yield return new ListeningQuestion(
                Id: id,
                Number: number,
                PartCode: ReadString(question.GetValueOrDefault("partCode")) ?? ReadString(question.GetValueOrDefault("part")) ?? "A",
                Text: ReadString(question.GetValueOrDefault("text")) ?? ReadString(question.GetValueOrDefault("stem")) ?? string.Empty,
                Type: ReadString(question.GetValueOrDefault("type")) ?? ReadString(question.GetValueOrDefault("questionType")) ?? "short_answer",
                Options: ReadStringList(question.GetValueOrDefault("options")) ?? [],
                CorrectAnswer: correct,
                AcceptedAnswers: accepted.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Explanation: ReadString(question.GetValueOrDefault("explanation")) ?? ReadString(question.GetValueOrDefault("explanationMarkdown")),
                SkillTag: ReadString(question.GetValueOrDefault("skillTag")),
                AllowTranscriptReveal: ReadBool(question.GetValueOrDefault("allowTranscriptReveal")) ?? true,
                TranscriptExcerpt: ReadString(question.GetValueOrDefault("transcriptExcerpt")),
                DistractorExplanation: ReadString(question.GetValueOrDefault("distractorExplanation")),
                Points: Math.Max(1, ReadInt(question.GetValueOrDefault("points")) ?? 1),
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
        text = q.Text,
        q.Type,
        options = q.Options,
        q.Points
    };

    private static object SourceDto(ListeningSource source) => new
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
        audioAvailable = !string.IsNullOrWhiteSpace(source.AudioUrl),
        audioUnavailableReason = string.IsNullOrWhiteSpace(source.AudioUrl)
            ? "Audio is not available for this Listening paper yet."
            : null,
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
        }).ToList()
    };

    private static object AttemptDto(Attempt attempt, Dictionary<string, string?> answers) => new
    {
        attemptId = attempt.Id,
        paperId = attempt.ContentId,
        state = ToApiState(attempt.State),
        attempt.Mode,
        attempt.StartedAt,
        attempt.SubmittedAt,
        attempt.CompletedAt,
        attempt.ElapsedSeconds,
        attempt.LastClientSyncAt,
        answers
    };

    private static object RelationalAttemptDto(ListeningAttempt attempt, Dictionary<string, string?> answers) => new
    {
        attemptId = attempt.Id,
        paperId = attempt.PaperId,
        state = attempt.Status == ListeningAttemptStatus.Submitted ? "completed" : ToApiState(attempt.Status),
        mode = ToApiMode(attempt.Mode),
        attempt.StartedAt,
        attempt.SubmittedAt,
        completedAt = attempt.SubmittedAt,
        elapsedSeconds = (int)Math.Max(0, (attempt.LastActivityAt - attempt.StartedAt).TotalSeconds),
        lastClientSyncAt = attempt.LastActivityAt,
        answers
    };

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
                    : $"/listening/player/{Uri.EscapeDataString(paperId)}?attemptId={Uri.EscapeDataString(relationalAttempt.Id)}&mode={Uri.EscapeDataString(mode)}"
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

    private static object PaperHomeDto(ContentPaper paper, object? lastAttempt, int relationalQuestionCount)
    {
        var roles = paper.Assets.Where(a => a.IsPrimary).Select(a => a.Role).ToHashSet();
        var questions = ExtractQuestions(JsonSupport.Deserialize<Dictionary<string, object?>>(paper.ExtractedTextJson, new Dictionary<string, object?>()).GetValueOrDefault("listeningQuestions")).ToList();
        var questionCount = relationalQuestionCount > 0 ? relationalQuestionCount : questions.Count;
        return new
        {
            id = paper.Id,
            paper.Title,
            paper.Slug,
            paper.Difficulty,
            paper.EstimatedDurationMinutes,
            paper.PublishedAt,
            route = $"/listening/player/{Uri.EscapeDataString(paper.Id)}",
            sourceKind = "content_paper",
            objectiveReady = questionCount > 0,
            questionCount,
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

    private static List<object> BuildPartCollections(IReadOnlyCollection<object> paperDtos, IReadOnlyCollection<object> featuredTasks)
    {
        var readyCount = paperDtos.Count + featuredTasks.Count;
        return
        [
            new
            {
                id = "part-a",
                title = "Part A detail capture",
                description = "Consultation-note accuracy, numbers, units, and clinical details.",
                available = readyCount > 0,
                route = readyCount > 0 ? "/listening" : null
            },
            new
            {
                id = "parts-b-c",
                title = "Parts B/C decision control",
                description = "Purpose, attitude, distractors, and final recommendation control.",
                available = readyCount > 0,
                route = readyCount > 0 ? "/listening" : null
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

    private static string NormalizeDrillId(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized)) return "listening-drill-detail_capture";
        return normalized.StartsWith("listening-drill-", StringComparison.Ordinal) ? normalized : $"listening-drill-{normalized}";
    }

    private static ListeningScoreDto ResolveScoreFromEvaluation(Evaluation? evaluation)
    {
        if (evaluation is not null)
        {
            var rows = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.CriterionScoresJson, []);
            var row = rows.FirstOrDefault();
            var raw = ReadInt(row?.GetValueOrDefault("rawScore"));
            var scaled = ReadInt(row?.GetValueOrDefault("scaledScore"));
            if (raw.HasValue || scaled.HasValue)
            {
                var rawValue = Math.Clamp(raw ?? 0, 0, CanonicalRawMax);
                var scaledValue = scaled ?? OetScoring.OetRawToScaled(rawValue);
                return new ListeningScoreDto(
                    rawValue,
                    CanonicalRawMax,
                    scaledValue,
                    OetScoring.OetGradeLetterFromScaled(scaledValue),
                    OetScoring.IsListeningReadingPassByScaled(scaledValue));
            }
        }

        var defaultScore = OetScoring.GradeListeningReading(Subtest, 0);
        return new ListeningScoreDto(defaultScore.RawCorrect, defaultScore.RawMax, defaultScore.ScaledScore, defaultScore.Grade, defaultScore.Passed);
    }

    private static ListeningScoreDto ResolveScoreFromRelationalAttempt(ListeningAttempt attempt, Evaluation? evaluation)
    {
        if (evaluation is not null)
        {
            return ResolveScoreFromEvaluation(evaluation);
        }

        var rawValue = Math.Clamp(attempt.RawScore ?? 0, 0, CanonicalRawMax);
        var scaledValue = attempt.ScaledScore ?? OetScoring.OetRawToScaled(rawValue);
        return new ListeningScoreDto(
            rawValue,
            attempt.MaxRawScore > 0 ? attempt.MaxRawScore : CanonicalRawMax,
            scaledValue,
            OetScoring.OetGradeLetterFromScaled(scaledValue),
            OetScoring.IsListeningReadingPassByScaled(scaledValue));
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

    private static void EnsureAttemptCanMutate(Attempt attempt)
    {
        if (attempt.State == AttemptState.Completed)
        {
            throw ApiException.Conflict("listening_attempt_locked", "This Listening attempt has already been submitted.");
        }
    }

    private static Dictionary<string, string?> DeserializeAnswers(string json)
        => JsonSupport.Deserialize<Dictionary<string, string?>>(json, new Dictionary<string, string?>());

    /// <summary>
    /// Phase 9 tail: accept the four learner-visible Listening modes.
    ///
    ///   <c>practice</c> — default. Free replay, scrubbable, pause allowed.
    ///   <c>exam</c>     — one-play, no scrub, no pause. Standard CBT-style.
    ///   <c>home</c>     — OET@Home: kiosk full-screen + integrity prompt;
    ///                     timer + one-play behave like <c>exam</c>.
    ///   <c>paper</c>    — paper-simulation: printable booklet UI; timer +
    ///                     one-play behave like <c>exam</c>.
    ///
    /// Anything else collapses to <c>practice</c> so a malformed query
    /// param can never accidentally promote a learner into a high-stakes
    /// integrity flow.
    /// </summary>
    private static string NormalizeMode(string? mode)
    {
        var normalized = (mode ?? "practice").Trim().ToLowerInvariant();
        return normalized switch
        {
            "exam" => "exam",
            "home" => "home",
            "paper" => "paper",
            _ => "practice",
        };
    }

    /// <summary>True for any mode that is graded under exam-integrity rules
    /// (one-play, no pause, no scrub). <c>practice</c> is the only non-exam
    /// mode.</summary>
    private static bool IsExamMode(string normalizedMode)
        => normalizedMode is "exam" or "home" or "paper";

    private static string? AssetDownloadPath(ContentPaperAsset? asset)
        => asset?.MediaAsset is null ? null : $"/v1/media/{asset.MediaAsset.Id}/content";

    private static bool MatchesObjectiveAnswer(string? learnerAnswer, string? correctAnswer)
        => string.Equals(NormalizeObjectiveAnswer(learnerAnswer), NormalizeObjectiveAnswer(correctAnswer), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeObjectiveAnswer(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Trim().ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)).ToArray());

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
        => $"{score.RawScore} / {score.MaxRawScore} \u2022 {score.ScaledScore} / 500 \u2022 Grade {score.Grade}";

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
        bool UsesRelationalStructure);

    private sealed record ListeningExtractMetaDto(
        string PartCode,                     // A1 | A2 | B | C1 | C2
        int DisplayOrder,
        string Kind,                          // consultation | workplace | presentation
        string Title,
        string? AccentCode,                   // e.g. en-GB | en-AU | en-IE | en-US
        IReadOnlyList<ListeningSpeakerDto> Speakers,
        int? AudioStartMs,
        int? AudioEndMs);

    private sealed record ListeningSpeakerDto(
        string Id,
        string Role,                          // e.g. doctor | patient | nurse | presenter
        string? Gender,                       // m | f | nb | null
        string? Accent);                      // optional override of extract accentCode

    private sealed record ListeningTranscriptSegmentDto(
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
        // wrong_speaker | opposite_meaning | reused_keyword. Same length as Options.
        IReadOnlyList<string?> OptionDistractorCategory,
        // Phase 4: speaker attitude on Part C: concerned | optimistic | doubtful |
        // critical | neutral | other. Null on Part A/B.
        string? SpeakerAttitude,
        // Phase 5: time-coded transcript evidence (start/end ms in the section audio).
        int? TranscriptEvidenceStartMs,
        int? TranscriptEvidenceEndMs);

    private sealed record ListeningAssetReadiness(bool Audio, bool QuestionPaper, bool AnswerKey, bool AudioScript);

    private sealed record ListeningScoreDto(int RawScore, int MaxRawScore, int ScaledScore, string Grade, bool Passed);

    private sealed record ListeningTranscriptSnippetDto(bool Allowed, string? Excerpt, string? DistractorExplanation);

    private sealed record ListeningReviewItemDto(
        string QuestionId,
        int Number,
        string PartCode,
        string Prompt,
        string Type,
        string LearnerAnswer,
        string CorrectAnswer,
        bool IsCorrect,
        int PointsEarned,
        int MaxPoints,
        string Explanation,
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
        int? TranscriptEvidenceEndMs);

    private sealed record ListeningOptionAnalysisDto(
        string OptionLabel,                 // "A" | "B" | "C"
        string OptionText,
        bool IsCorrect,
        string? DistractorCategory,         // too_strong | too_weak | wrong_speaker | opposite_meaning | reused_keyword
        string? WhyMarkdown);

    private sealed record ListeningErrorClusterDto(string ErrorType, string Label, int Count, IReadOnlyList<string> AffectedQuestionIds);

    private sealed record ListeningTranscriptAccessDto(string Policy, string State, IReadOnlyList<string> AllowedQuestionIds, string Reason);

    private sealed record ListeningDrillDto(
        string DrillId,
        string Title,
        string FocusLabel,
        string Description,
        string ErrorType,
        int EstimatedMinutes,
        IReadOnlyList<string> Highlights,
        string LaunchRoute,
        string ReviewRoute);

    private sealed record ListeningReviewDto(
        string? EvaluationId,
        string AttemptId,
        object Paper,
        int RawScore,
        int MaxRawScore,
        int ScaledScore,
        string Grade,
        bool Passed,
        string ScoreDisplay,
        int CorrectCount,
        int IncorrectCount,
        int UnansweredCount,
        IReadOnlyList<ListeningReviewItemDto> ItemReview,
        IReadOnlyList<ListeningErrorClusterDto> ErrorClusters,
        ListeningDrillDto RecommendedNextDrill,
        ListeningTranscriptAccessDto TranscriptAccess,
        // Phase 5: paper-level time-coded transcript segments to power the
        // post-attempt review player's jump-to-evidence UI.
        IReadOnlyList<ListeningTranscriptSegmentDto> TranscriptSegments,
        IReadOnlyList<string> Strengths,
        IReadOnlyList<string> Issues,
        DateTimeOffset? GeneratedAt);
}

public sealed record ListeningAnswerSaveRequest(string? UserAnswer);
public sealed record ListeningIntegrityEventRequest(string EventType, string? Details, DateTimeOffset? OccurredAt);
