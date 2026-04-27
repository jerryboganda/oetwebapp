using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Listening;

public sealed class ListeningLearnerService(
    LearnerDbContext db,
    IContentEntitlementService entitlements)
{
    private const string Subtest = "listening";
    private const int CanonicalRawMax = OetScoring.ListeningReadingRawMax;

    public async Task<object> GetHomeAsync(string userId, CancellationToken ct)
    {
        await EnsureLearnerAsync(userId, ct);

        var papers = await db.ContentPapers.AsNoTracking()
            .Include(p => p.Assets.Where(a => a.IsPrimary))
                .ThenInclude(a => a.MediaAsset)
            .Where(p => p.Status == ContentStatus.Published && p.SubtestCode == Subtest)
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

        var attempts = contentIds.Count == 0
            ? new List<Attempt>()
            : await db.Attempts.AsNoTracking()
                .Where(a => a.UserId == userId && a.SubtestCode == Subtest && contentIds.Contains(a.ContentId))
                .OrderByDescending(a => a.LastClientSyncAt ?? a.SubmittedAt ?? a.StartedAt)
                .ToListAsync(ct);

        var evaluations = attempts.Count == 0
            ? new List<Evaluation>()
            : await db.Evaluations.AsNoTracking()
                .Where(e => attempts.Select(a => a.Id).Contains(e.AttemptId))
                .OrderByDescending(e => e.GeneratedAt)
                .ToListAsync(ct);

        var titleByContentId = papers.ToDictionary(p => p.Id, p => p.Title, StringComparer.Ordinal);
        foreach (var task in legacyTasks)
        {
            titleByContentId.TryAdd(task.Id, task.Title);
        }

        var activeAttempts = attempts
            .Where(a => a.State == AttemptState.InProgress)
            .Take(3)
            .Select(a => new
            {
                attemptId = a.Id,
                paperId = a.ContentId,
                paperTitle = titleByContentId.GetValueOrDefault(a.ContentId, "Listening paper"),
                status = ToApiState(a.State),
                a.Mode,
                a.StartedAt,
                a.LastClientSyncAt,
                answeredCount = DeserializeAnswers(a.AnswersJson).Count(kv => !string.IsNullOrWhiteSpace(kv.Value)),
                route = $"/listening/player/{Uri.EscapeDataString(a.ContentId)}?attemptId={Uri.EscapeDataString(a.Id)}&mode={Uri.EscapeDataString(a.Mode)}"
            })
            .ToList();

        var recentResults = attempts
            .Where(a => a.State == AttemptState.Completed)
            .Take(5)
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
            .ToList();

        var latestCompletedAttempt = attempts.FirstOrDefault(a => a.State == AttemptState.Completed);
        var latestEvaluation = latestCompletedAttempt is null
            ? null
            : evaluations.FirstOrDefault(e => e.AttemptId == latestCompletedAttempt.Id);

        // Hardening: a completed attempt may reference a paper that was later unpublished or whose
        // ExtractedTextJson is malformed. Never let that bubble a 500 through the Listening home endpoint.
        IReadOnlyList<ListeningErrorClusterDto> latestClusters = new List<ListeningErrorClusterDto>();
        if (latestCompletedAttempt is not null)
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
            ? latestClusters.Select(c => BuildDrill(c.ErrorType, latestCompletedAttempt?.ContentId, latestCompletedAttempt?.Id)).ToList()
            : new List<ListeningDrillDto>
            {
                BuildDrill("distractor_confusion", legacyTasks.FirstOrDefault()?.Id, latestCompletedAttempt?.Id),
                BuildDrill("numbers_and_frequencies", legacyTasks.FirstOrDefault()?.Id, latestCompletedAttempt?.Id)
            };

        // Hardening: individual paper DTO extraction reads free-form ExtractedTextJson; one malformed
        // paper must not take the whole endpoint down.
        var paperDtos = new List<object>();
        foreach (var paper in papers)
        {
            try
            {
                paperDtos.Add(PaperHomeDto(paper, attempts.FirstOrDefault(a => a.ContentId == paper.Id)));
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
                route = latestCompletedAttempt is null ? null : $"/listening/review/{latestCompletedAttempt.Id}",
                availableAfterAttempt = true,
                latestAttemptId = latestCompletedAttempt?.Id,
                latestScoreDisplay = latestEvaluation is null ? null : FormatScoreDisplay(ResolveScoreFromEvaluation(latestEvaluation))
            },
            distractorDrills = drillGroups,
            drillGroups,
            accessPolicyHints = new
            {
                policy = "per_item_post_attempt",
                state = latestCompletedAttempt is null ? "deferred" : "available",
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
        var source = await ResolveSourceAsync(paperId, ct);
        var normalizedMode = NormalizeMode(mode);
        Attempt? attempt = null;

        if (!string.IsNullOrWhiteSpace(attemptId))
        {
            attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, ct);
            if (!string.Equals(attempt.ContentId, source.Id, StringComparison.Ordinal))
            {
                throw ApiException.Validation("listening_attempt_mismatch", "This attempt does not belong to the requested Listening paper.");
            }
        }
        else
        {
            attempt = await db.Attempts.AsNoTracking()
                .Where(a => a.UserId == userId
                    && a.SubtestCode == Subtest
                    && a.ContentId == source.Id
                    && a.State == AttemptState.InProgress)
                .OrderByDescending(a => a.LastClientSyncAt ?? a.StartedAt)
                .FirstOrDefaultAsync(ct);
        }

        var questions = source.Questions.Select(LearnerQuestionDto).ToList();
        var answers = attempt is null ? new Dictionary<string, string?>() : DeserializeAnswers(attempt.AnswersJson);
        return new
        {
            paper = SourceDto(source),
            attempt = attempt is null ? null : AttemptDto(attempt, answers),
            questions,
            modePolicy = new
            {
                mode = attempt?.Mode ?? normalizedMode,
                canPause = normalizedMode == "practice",
                canScrub = normalizedMode == "practice",
                onePlayOnly = normalizedMode == "exam",
                autosave = true,
                transcriptPolicy = "per_item_post_attempt"
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

        // Subscription gate (Phase 3). Only applies to authored ContentPaper rows;
        // legacy ContentItem-backed seed/diagnostic tasks (e.g. "lt-001") have no
        // ContentPaper row and remain ungated until they migrate to the paper
        // schema. Free papers (tagged "access:free") and admins bypass automatically.
        var paperEntity = await db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paperId, ct);
        if (paperEntity is not null)
        {
            await entitlements.RequireAccessAsync(userId, paperEntity, ct);
        }

        var source = await ResolveSourceAsync(paperId, ct);
        if (source.Questions.Count == 0)
        {
            throw ApiException.Validation(
                "listening_questions_missing",
                "This Listening paper cannot start a graded attempt until its structured questions are authored.");
        }

        var normalizedMode = NormalizeMode(mode);
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
        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, ct);
        return AttemptDto(attempt, DeserializeAnswers(attempt.AnswersJson));
    }

    public async Task SaveAnswerAsync(string userId, string attemptId, string questionId, ListeningAnswerSaveRequest request, CancellationToken ct)
    {
        await EnsureLearnerMutationAllowedAsync(userId, ct);
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
        return BuildReview(attempt, source, evaluation);
    }

    public async Task<object> GetReviewAsync(string userId, string attemptId, CancellationToken ct)
    {
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

    private async Task<ListeningSource> ResolveSourceAsync(string id, CancellationToken ct)
    {
        var paper = await db.ContentPapers.AsNoTracking()
            .Include(p => p.Assets.Where(a => a.IsPrimary))
                .ThenInclude(a => a.MediaAsset)
            .FirstOrDefaultAsync(p => p.Id == id && p.SubtestCode == Subtest && p.Status == ContentStatus.Published, ct);
        if (paper is not null)
        {
            return BuildPaperSource(paper);
        }

        var legacy = await db.ContentItems.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.SubtestCode == Subtest && x.Status == ContentStatus.Published, ct)
            ?? throw ApiException.NotFound("listening_paper_not_found", "Listening paper not found.");
        return BuildLegacySource(legacy);
    }

    private static ListeningSource BuildPaperSource(ContentPaper paper)
    {
        var assets = paper.Assets.Where(a => a.IsPrimary).ToList();
        var assetByRole = assets
            .GroupBy(a => a.Role)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.DisplayOrder).First());
        var questionMap = JsonSupport.Deserialize<Dictionary<string, object?>>(paper.ExtractedTextJson, new Dictionary<string, object?>());
        var questions = ExtractQuestions(questionMap.TryGetValue("listeningQuestions", out var listeningQuestions)
                ? listeningQuestions
                : questionMap.GetValueOrDefault("questions"))
            .ToList();

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
                AudioScript: assetByRole.ContainsKey(PaperAssetRole.AudioScript)));
    }

    private static ListeningSource BuildLegacySource(ContentItem item)
    {
        var detail = JsonSupport.Deserialize<Dictionary<string, object?>>(item.DetailJson, new Dictionary<string, object?>());
        var questions = ExtractQuestions(detail.GetValueOrDefault("questions")).ToList();
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
                AudioScript: questions.Any(q => !string.IsNullOrWhiteSpace(q.TranscriptExcerpt))));
    }

    private ListeningReviewDto BuildReview(Attempt attempt, ListeningSource source, Evaluation? evaluation = null)
    {
        var answers = DeserializeAnswers(attempt.AnswersJson);
        var items = source.Questions
            .OrderBy(q => q.Number)
            .Select(q => ReviewItemDto(q, answers.GetValueOrDefault(q.Id)))
            .ToList();
        var raw = Math.Clamp(items.Sum(i => i.PointsEarned), 0, CanonicalRawMax);
        var score = OetScoring.GradeListeningReading(Subtest, raw);
        var clusters = BuildErrorClusters(items);
        var recommended = clusters.Count > 0
            ? BuildDrill(clusters[0].ErrorType, source.Id, attempt.Id)
            : BuildDrill("detail_capture", source.Id, attempt.Id);
        var allowedTranscriptIds = items
            .Where(item => item.Transcript is not null && item.Transcript.Allowed)
            .Select(item => item.QuestionId)
            .ToList();

        return new ListeningReviewDto(
            EvaluationId: evaluation?.Id,
            AttemptId: attempt.Id,
            Paper: SourceDto(source),
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
            Strengths: BuildStrengths(score.RawCorrect, items),
            Issues: BuildIssues(items),
            GeneratedAt: evaluation?.GeneratedAt ?? attempt.CompletedAt ?? attempt.SubmittedAt);
    }

    private static ListeningReviewItemDto ReviewItemDto(ListeningQuestion q, string? learnerAnswer)
    {
        var isCorrect = q.AcceptedAnswers.Any(answer => MatchesObjectiveAnswer(learnerAnswer, answer));
        var errorType = isCorrect ? null : ObjectiveErrorType(q);
        var transcript = q.AllowTranscriptReveal
            ? new ListeningTranscriptSnippetDto(
                Allowed: true,
                Excerpt: q.TranscriptExcerpt,
                DistractorExplanation: q.DistractorExplanation)
            : null;
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
            DistractorExplanation: q.DistractorExplanation);
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
                Points: Math.Max(1, ReadInt(question.GetValueOrDefault("points")) ?? 1));
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
        transcriptPolicy = "per_item_post_attempt"
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

    private static object PaperHomeDto(ContentPaper paper, Attempt? lastAttempt)
    {
        var roles = paper.Assets.Where(a => a.IsPrimary).Select(a => a.Role).ToHashSet();
        var questions = ExtractQuestions(JsonSupport.Deserialize<Dictionary<string, object?>>(paper.ExtractedTextJson, new Dictionary<string, object?>()).GetValueOrDefault("listeningQuestions")).ToList();
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
            objectiveReady = questions.Count > 0,
            questionCount = questions.Count,
            assetReadiness = new
            {
                audio = roles.Contains(PaperAssetRole.Audio),
                questionPaper = roles.Contains(PaperAssetRole.QuestionPaper),
                answerKey = roles.Contains(PaperAssetRole.AnswerKey),
                audioScript = roles.Contains(PaperAssetRole.AudioScript)
            },
            lastAttempt = lastAttempt is null ? null : new
            {
                attemptId = lastAttempt.Id,
                status = ToApiState(lastAttempt.State),
                lastAttempt.StartedAt,
                lastAttempt.SubmittedAt,
                route = lastAttempt.State == AttemptState.Completed
                    ? $"/listening/results/{Uri.EscapeDataString(lastAttempt.Id)}"
                    : $"/listening/player/{Uri.EscapeDataString(paper.Id)}?attemptId={Uri.EscapeDataString(lastAttempt.Id)}&mode={Uri.EscapeDataString(lastAttempt.Mode)}"
            }
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

    private static string NormalizeMode(string? mode)
    {
        var normalized = (mode ?? "practice").Trim().ToLowerInvariant();
        return normalized == "exam" ? "exam" : "practice";
    }

    private static string? AssetDownloadPath(ContentPaperAsset? asset)
        => asset?.MediaAsset is null ? null : $"/v1/media/{asset.MediaAsset.Id}/content";

    private static bool MatchesObjectiveAnswer(string? learnerAnswer, string? correctAnswer)
        => string.Equals(NormalizeObjectiveAnswer(learnerAnswer), NormalizeObjectiveAnswer(correctAnswer), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeObjectiveAnswer(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Trim().ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)).ToArray());

    private static string? ObjectiveErrorType(ListeningQuestion question)
    {
        if (!string.IsNullOrWhiteSpace(question.DistractorExplanation)) return "distractor_confusion";
        if (string.Equals(question.SkillTag, "numbers", StringComparison.OrdinalIgnoreCase)
            || string.Equals(question.SkillTag, "frequency", StringComparison.OrdinalIgnoreCase))
        {
            return "numbers_and_frequencies";
        }

        return "detail_capture";
    }

    private static string ObjectiveErrorTypeLabel(string? errorType) => errorType switch
    {
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
        ListeningAssetReadiness AssetReadiness);

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
        int Points);

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
        string? DistractorExplanation);

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
        IReadOnlyList<string> Strengths,
        IReadOnlyList<string> Issues,
        DateTimeOffset? GeneratedAt);
}

public sealed record ListeningAnswerSaveRequest(string? UserAnswer);
