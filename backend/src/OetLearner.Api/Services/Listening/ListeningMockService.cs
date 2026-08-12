using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Assessment;

namespace OetLearner.Api.Services.Listening;

internal sealed record ListeningMockOptionSnapshot(
    string Id,
    string OptionKey,
    int DisplayOrder,
    string Text,
    bool IsCorrect,
    int Version,
    ListeningDistractorCategory? DistractorCategory,
    string? WhyWrongMarkdown);

internal sealed record ListeningMockQuestionSnapshot(
    int Version,
    int QuestionNumber,
    int Points,
    ListeningQuestionType QuestionType,
    string CorrectAnswerJson,
    string? AcceptedSynonymsJson,
    bool CaseSensitive,
    string? SubSkillTagsCsv,
    string? Accent,
    string? ExplanationMarkdown,
    string? TranscriptEvidenceText,
    int? TranscriptEvidenceStartMs,
    int? TranscriptEvidenceEndMs,
    IReadOnlyList<ListeningMockOptionSnapshot> Options);

// ═════════════════════════════════════════════════════════════════════════════
// Listening Module Pathway — Phase 5 Mock Test Service
//
// Implements OET_LISTENING_MODULE_PATHWAY.md §9 (mock tests).
//
// Lifecycle per mock template:
//   1. ListAvailableAsync     — published templates, gated by learner stage.
//   2. StartMockAsync         — creates a SessionType="mock" practice session.
//   3. SubmitMockAsync        — persists 42 answers, grades, scales 0–500.
//   4. GetResultsAsync        — recomposes from persisted attempts + scores.
//
// Reuses the Phase 1 building blocks:
//   • ListeningPracticeSession (SessionType="mock")
//   • ListeningQuestionAttempt for per-question answers
//   • IListeningLearnerGradingService.GradeSessionAsync for L1..L8 + accents
//   • IListeningSkillScoringService.UpdateScoresFromSessionAsync to feed the
//     rolling per-skill / per-accent dashboards.
//
// Legacy pathway sessions do not capture a governed conversion-table version.
// They therefore expose raw evidence only; scaled results belong to the
// versioned ListeningAttempt pipeline.
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningMockService
{
    /// <summary>Return published mock templates the learner is eligible to attempt.</summary>
    Task<IReadOnlyList<MockTemplateDto>> ListAvailableAsync(string userId, CancellationToken ct);

    /// <summary>Begin a mock attempt for the requested template. Returns the new session id.</summary>
    Task<StartMockResponse> StartMockAsync(string userId, Guid mockTemplateId, CancellationToken ct);

    /// <summary>Persist all 42 answers, grade them, scale 0–500, and update progress.</summary>
    Task<MockResultResponse> SubmitMockAsync(
        string userId, Guid sessionId, MockSubmitRequest request, CancellationToken ct);

    /// <summary>Recompose the multi-section result envelope from a completed session.</summary>
    Task<MockResultResponse> GetResultsAsync(string userId, Guid sessionId, CancellationToken ct);
}

public sealed class ListeningMockService : IListeningMockService
{
    /// <summary>Canonical mock test length per OET — also matches OetScoring.ListeningReadingRawMax.</summary>
    private const int MockTotalQuestions = 42;

    /// <summary>L1..L8 → display label. Duplicated with
    /// <see cref="ListeningLearnerPathwayService"/> so this service doesn't
    /// depend on that one's private dictionary.</summary>
    private static readonly IReadOnlyDictionary<string, string> SkillLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["L1"] = "Detail capture",
            ["L2"] = "Note-taking speed",
            ["L3"] = "Spelling accuracy",
            ["L4"] = "Gist comprehension",
            ["L5"] = "Distractor recognition",
            ["L6"] = "Inference",
            ["L7"] = "Speaker stance",
            ["L8"] = "Accent adaptation",
        };

    private static readonly string[] AllSkillCodes =
        ["L1", "L2", "L3", "L4", "L5", "L6", "L7", "L8"];

    private static readonly IReadOnlyDictionary<string, string> AccentLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["british"] = "British (UK)",
            ["australian"] = "Australian",
            ["us"] = "North American",
            ["non_native"] = "Non-native",
        };

    private static readonly string[] AllAccents =
        ["british", "australian", "us", "non_native"];

    /// <summary>Learner stages from which a mock attempt is allowed. The
    /// learner must have completed (or at least started) the diagnostic so
    /// that a baseline exists for L1..L8 + accent scoring.</summary>
    private static readonly HashSet<string> MockEligibleStages =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "foundation",
            "practice",
            "mastery",
        };

    private readonly LearnerDbContext _db;
    private readonly IListeningLearnerGradingService _grading;
    private readonly IListeningSkillScoringService _scoring;
    private readonly TimeProvider _clock;
    private readonly ILogger<ListeningMockService> _logger;
    private readonly IAssessmentScoreConversionService _scoreConversion;
    private readonly IAssessmentMarkingPolicyService _markingPolicy;

    public ListeningMockService(
        LearnerDbContext db,
        IListeningLearnerGradingService grading,
        IListeningSkillScoringService scoring,
        TimeProvider clock,
        ILogger<ListeningMockService> logger,
        IAssessmentScoreConversionService? scoreConversion = null,
        IAssessmentMarkingPolicyService? markingPolicy = null)
    {
        _db = db;
        _grading = grading;
        _scoring = scoring;
        _clock = clock;
        _logger = logger;
        _scoreConversion = scoreConversion ?? new AssessmentScoreConversionService(db);
        _markingPolicy = markingPolicy ?? new AssessmentMarkingPolicyService(db);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Catalog
    // ─────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<MockTemplateDto>> ListAvailableAsync(
        string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        // Listing is safe pre-diagnostic — surfacing the catalog never reveals
        // answers and the actual start gate is enforced in StartMockAsync.
        var templates = await _db.ListeningMockTemplates
            .AsNoTracking()
            .Where(t => t.IsPublished)
            .OrderBy(t => t.Difficulty)
            .ThenBy(t => t.Title)
            .ToListAsync(ct);

        var result = new List<MockTemplateDto>(templates.Count);
        foreach (var template in templates)
        {
            var questionCount = CountQuestionIds(template.QuestionIdsJson);
            result.Add(new MockTemplateDto(
                Id: template.Id,
                Title: template.Title,
                Difficulty: template.Difficulty,
                DurationSeconds: template.DurationSeconds,
                TotalQuestions: questionCount));
        }
        return result;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Start
    // ─────────────────────────────────────────────────────────────────────

    public async Task<StartMockResponse> StartMockAsync(
        string userId, Guid mockTemplateId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (mockTemplateId == Guid.Empty)
        {
            throw new ArgumentException(
                "mockTemplateId is required.", nameof(mockTemplateId));
        }

        // ── 1. Stage gate ────────────────────────────────────────────────
        var profile = await _db.LearnerListeningProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? throw new InvalidOperationException(
                "Listening profile not found — start the listening flow first.");

        if (!MockEligibleStages.Contains(profile.CurrentStage))
        {
            throw new InvalidOperationException(
                $"Mock cannot be started from stage '{profile.CurrentStage}'. " +
                "Complete the diagnostic first.");
        }

        // ── 2. Template lookup ───────────────────────────────────────────
        var template = await _db.ListeningMockTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == mockTemplateId, ct)
            ?? throw new InvalidOperationException("Mock template not found");

        if (!template.IsPublished)
        {
            // Unpublished templates surface as "not found" so the wire shape
            // matches an unknown id — prevents probing of draft content.
            throw new InvalidOperationException("Mock template not found");
        }

        var questionIds = DeserializeQuestionIds(template.QuestionIdsJson);
        if (questionIds.Count == 0)
        {
            throw new InvalidOperationException(
                $"Mock template '{template.Title}' has no questions.");
        }

        if (questionIds.Count != MockTotalQuestions
            || questionIds.Distinct(StringComparer.Ordinal).Count() != MockTotalQuestions
            || questionIds.Any(string.IsNullOrWhiteSpace))
        {
            throw ApiException.Conflict(
                "listening_mock_structure_invalid",
                "A Listening mock must contain exactly 42 distinct questions.");
        }

        var authoredQuestions = await _db.ListeningQuestions
            .AsNoTracking()
            .Include(q => q.Part)
            .Include(q => q.Options)
            .Where(q => questionIds.Contains(q.Id))
            .ToListAsync(ct);
        ValidateMockQuestionSet(authoredQuestions, questionIds, template.Title);
        var questionSnapshots = authoredQuestions.ToDictionary(
            q => q.Id,
            BuildQuestionSnapshot,
            StringComparer.Ordinal);

        // ── 3. Resolve audio extract ids for the session metadata ───────
        // Mirrors ListeningLearnerPathwayService.StartDiagnosticAsync so the
        // audio scoping query can still locate extracts during review.
        var extractIds = await _db.ListeningQuestions
            .AsNoTracking()
            .Where(q => questionIds.Contains(q.Id) && q.ListeningExtractId != null)
            .Select(q => q.ListeningExtractId!)
            .Distinct()
            .ToListAsync(ct);

        // ── 4. Create the session row ────────────────────────────────────
        var now = _clock.GetUtcNow();
        var deadlineAt = now.AddSeconds(Math.Max(1, template.DurationSeconds));
        var markingPolicyAtStart = await _markingPolicy.ResolveAsync(
            "listening",
            "default",
            cancellationToken: ct);
        if (!markingPolicyAtStart.IsAvailable || markingPolicyAtStart.ErrorCode is not null)
        {
            throw ApiException.Conflict(
                "listening_marking_policy_unavailable",
                "Listening mocks are unavailable until an owner-approved marking policy is effective.");
        }
        var scoreConversionAtStart = await _scoreConversion.ResolveAsync(
            "listening",
            rawScore: 0,
            scopeKey: "default",
            cancellationToken: ct);
        var scoreConversionSnapshot = AssessmentScoreConversionSnapshot
            .Capture(scoreConversionAtStart)
            .Serialize();
        var audioTransport = ListeningAudioTransportPolicy.FromPolicy(
            "practice",
            markingPolicyAtStart.Document);
        var session = new ListeningPracticeSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SessionType = "mock",
            FocusSkill = null,
            FocusAccent = null,
            // Copy the ordered question list onto the session so the player
            // can iterate even if the template is later re-ordered.
            QuestionIdsJson = JsonSerializer.Serialize(questionIds),
            AudioAssetIdsJson = JsonSerializer.Serialize(extractIds),
            StartedAt = now,
            TotalQuestions = questionIds.Count,
            MetadataJson = JsonSerializer.Serialize(new
            {
                mockTemplateId = template.Id,
                templateTitle = template.Title,
                durationSeconds = template.DurationSeconds,
                difficulty = template.Difficulty,
                deadlineAt,
                scoreConversionSnapshotJson = scoreConversionSnapshot,
                scoreConversionTableVersionKey = scoreConversionAtStart.TableVersionKey,
                markingPolicyVersionId = markingPolicyAtStart.PolicyId,
                markingPolicyVersionKey = markingPolicyAtStart.PolicyVersionKey,
                markingPolicy = markingPolicyAtStart.Document,
                audioLockMode = audioTransport.LockMode,
                canPause = audioTransport.CanPause,
                canScrub = audioTransport.CanScrub,
                onePlayOnly = audioTransport.OnePlayOnly,
                questionSnapshots,
            }),
        };
        _db.ListeningPracticeSessions.Add(session);
        await _db.SaveChangesAsync(ct);
        if (scoreConversionAtStart.TableId is not null && scoreConversionAtStart.IsAvailable)
        {
            await _scoreConversion.MarkUsedAsync(scoreConversionAtStart.TableId, ct);
        }
        await _markingPolicy.MarkUsedAsync(markingPolicyAtStart.PolicyId!, ct);

        return new StartMockResponse(
            SessionId: session.Id,
            TotalQuestions: questionIds.Count,
            DurationSeconds: template.DurationSeconds,
            DeadlineAt: deadlineAt);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Submit
    // ─────────────────────────────────────────────────────────────────────

    public async Task<MockResultResponse> SubmitMockAsync(
        string userId, Guid sessionId, MockSubmitRequest request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(request);

        var session = await _db.ListeningPracticeSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw new InvalidOperationException("Session not found");

        if (!string.Equals(session.SessionType, "mock", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Session is not a mock (type={session.SessionType}).");
        }

        // ── Idempotent re-submit ─────────────────────────────────────────
        if (session.CompletedAt is not null)
        {
            return await GetResultsAsync(userId, session.Id, ct);
        }

        var now = _clock.GetUtcNow();
        var metadata = ReadSessionMetadata(session);
        var timedOut = GetSessionDeadlineAt(session, metadata) is { } deadlineAt && now >= deadlineAt;

        // ── 1. Upsert all submitted answers ──────────────────────────────
        if (!timedOut && request.Answers is not null && request.Answers.Count > 0)
        {
            await UpsertAnswersAsync(userId, session.Id, request.Answers, now, ct);
        }

        // ── 2. Load attempts + questions for grading ─────────────────────
        var attempts = await _db.ListeningQuestionAttempts
            .Where(a => a.UserId == userId && a.PracticeSessionId == session.Id)
            .ToListAsync(ct);

        var questionIds = attempts.Select(a => a.ListeningQuestionId).Distinct().ToList();
        var questions = await _db.ListeningQuestions
            .AsNoTracking()
            .Where(q => questionIds.Contains(q.Id))
            .ToListAsync(ct);
        var questionsById = BuildSnapshotQuestions(questions, metadata.QuestionSnapshots);

        // ── 3. Grade each attempt then roll up ───────────────────────────
        foreach (var attempt in attempts)
        {
            ct.ThrowIfCancellationRequested();
            if (!questionsById.TryGetValue(attempt.ListeningQuestionId, out var question)
                || question is null)
            {
                _logger.LogWarning(
                    "Mock submit skipped attempt {AttemptId}: question {QuestionId} missing.",
                    attempt.Id, attempt.ListeningQuestionId);
                continue;
            }
            await _grading.GradeAttemptAsync(attempt, question, ct, metadata.MarkingPolicy);
        }

        var grading = await _grading.GradeSessionAsync(attempts, questionsById, ct);

        // ── 4. Close out the session row ─────────────────────────────────
        session.CompletedAt = now;
        session.Score = grading.CorrectCount;
        session.TotalQuestions = MockTotalQuestions;
        session.DurationSeconds = ComputeDurationSeconds(
            session.StartedAt,
            now,
            timedOut ? 0 : request.TotalDurationSeconds);

        await _db.SaveChangesAsync(ct);

        // ── 5. Roll up per-skill / per-accent rolling scores ────────────
        // UpdateScoresFromSessionAsync owns its own SaveChangesAsync so the
        // session-close write above must happen first to avoid contention.
        await _scoring.UpdateScoresFromSessionAsync(userId, session.Id, ct);

        // ── 6. Refresh readiness on the learner profile ─────────────────
        await RefreshReadinessAsync(userId, ct);

        return await BuildResultResponseAsync(userId, session, grading, timedOut, ct);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Recompose results
    // ─────────────────────────────────────────────────────────────────────

    public async Task<MockResultResponse> GetResultsAsync(
        string userId, Guid sessionId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var session = await _db.ListeningPracticeSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw new InvalidOperationException("Session not found");

        if (!string.Equals(session.SessionType, "mock", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Session is not a mock (type={session.SessionType}).");
        }

        if (session.CompletedAt is null)
        {
            throw new InvalidOperationException("Mock has not been submitted yet.");
        }

        var attempts = await _db.ListeningQuestionAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.PracticeSessionId == session.Id)
            .ToListAsync(ct);

        var questionIds = attempts.Select(a => a.ListeningQuestionId).Distinct().ToList();
        var questions = await _db.ListeningQuestions
            .AsNoTracking()
            .Where(q => questionIds.Contains(q.Id))
            .ToListAsync(ct);
        var metadata = ReadSessionMetadata(session);
        var questionsById = BuildSnapshotQuestions(questions, metadata.QuestionSnapshots);

        // Persisted attempts already carry IsCorrect flags from the submit
        // path, so we only roll up the session-level buckets here.
        var grading = await _grading.GradeSessionAsync(attempts, questionsById, ct);
        var timedOut = GetSessionDeadlineAt(session, metadata) is { } deadlineAt
            && session.CompletedAt is { } completedAt
            && completedAt >= deadlineAt;

        return await BuildResultResponseAsync(userId, session, grading, timedOut, ct);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Upsert the submitted answers into ListeningQuestionAttempts.
    /// Mirrors the diagnostic submission path so a partial auto-saved set is
    /// merged rather than overwritten.</summary>
    private async Task UpsertAnswersAsync(
        string userId,
        Guid sessionId,
        IReadOnlyList<DiagnosticAnswerSubmission> answers,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var questionIds = answers
            .Where(a => !string.IsNullOrWhiteSpace(a.QuestionId))
            .Select(a => a.QuestionId)
            .Distinct()
            .ToList();

        var existingByQuestionId = await _db.ListeningQuestionAttempts
            .Where(a => a.UserId == userId
                && a.PracticeSessionId == sessionId
                && questionIds.Contains(a.ListeningQuestionId))
            .ToDictionaryAsync(a => a.ListeningQuestionId, StringComparer.Ordinal, ct);

        foreach (var ans in answers)
        {
            if (string.IsNullOrWhiteSpace(ans.QuestionId)) continue;

            if (!existingByQuestionId.TryGetValue(ans.QuestionId, out var attempt))
            {
                attempt = new ListeningQuestionAttempt
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    ListeningQuestionId = ans.QuestionId,
                    PracticeSessionId = sessionId,
                    AttemptedAt = now,
                    IsCorrect = false,
                    IsSpellingCorrectMeaningWrong = false,
                    IsMeaningCorrectSpellingWrong = false,
                    InReviewQueue = false,
                    ReviewIntervalIndex = 0,
                    ConsecutiveCorrect = 0,
                };
                _db.ListeningQuestionAttempts.Add(attempt);
                existingByQuestionId[ans.QuestionId] = attempt;
            }

            attempt.SelectedOption = ans.SelectedOption;
            attempt.LearnerAnswer = ans.LearnerAnswer;
            attempt.IsUnknown = ans.IsUnknown;
            attempt.TimeSpentSeconds = ans.TimeSpentSeconds;
            attempt.ReplaysUsed = ans.ReplaysUsed;
            attempt.MarkedForReview = ans.MarkedForReview;
            attempt.AttemptedAt = now;
        }
    }

    /// <summary>Build the result envelope (raw, scaled, grade, radar, accents,
    /// prediction band) shared between the submit + GET paths.</summary>
    private async Task<MockResultResponse> BuildResultResponseAsync(
        string userId,
        ListeningPracticeSession session,
        DiagnosticGradingResult grading,
        bool timedOut,
        CancellationToken ct)
    {
        // ── Scoring ─────────────────────────────────────────────────────
        var rawScore = ClampInt(grading.CorrectCount, 0, MockTotalQuestions);
        var metadata = ReadSessionMetadata(session);
        var conversion = string.IsNullOrWhiteSpace(metadata.ScoreConversionSnapshotJson)
            ? AssessmentScoreConversionResult.Unavailable(
                "listening",
                "default",
                rawScore,
                "score_conversion_snapshot_missing")
            : await AssessmentScoreConversionSnapshotResolver.ResolveAsync(
                _scoreConversion,
                "listening",
                rawScore,
                metadata.ScoreConversionSnapshotJson,
                legacyTableId: null,
                scopeKey: "default",
                cancellationToken: ct);
        var hasApprovedConversion = conversion.ConvertedScore.HasValue
            && !string.IsNullOrWhiteSpace(conversion.TableVersionKey)
            && conversion.Passed.HasValue;
        int? scaledScore = hasApprovedConversion ? conversion.ConvertedScore : null;
        var gradeLabel = hasApprovedConversion ? conversion.Grade ?? "—" : "Scaled score unavailable";
        int? predictedLow = null;
        int? predictedHigh = null;

        // ── Skill radar — pull rolling rows + overlay this mock's baseline ─
        var (skillRows, accentRows) = await _scoring.GetScoresAsync(userId, ct);
        var skillByCode = skillRows.ToDictionary(s => s.SkillCode, StringComparer.OrdinalIgnoreCase);
        var accentByCode = accentRows.ToDictionary(a => a.Accent, StringComparer.OrdinalIgnoreCase);

        var radar = new List<SkillScoreDto>(AllSkillCodes.Length);
        foreach (var code in AllSkillCodes)
        {
            var current = skillByCode.TryGetValue(code, out var row) ? row.CurrentScore : 0m;
            var diagnostic = skillByCode.TryGetValue(code, out var d) ? d.DiagnosticScore : current;
            radar.Add(new SkillScoreDto
            {
                SkillCode = code,
                Label = SkillLabels.TryGetValue(code, out var label) ? label : code,
                CurrentScore = current,
                DiagnosticScore = diagnostic,
                QuestionsAttempted = row?.QuestionsAttempted ?? 0,
                QuestionsCorrect = row?.QuestionsCorrect ?? 0,
            });
        }

        var accents = new List<AccentProgressDto>(AllAccents.Length);
        foreach (var code in AllAccents)
        {
            var row = accentByCode.TryGetValue(code, out var r) ? r : null;
            accents.Add(new AccentProgressDto
            {
                Accent = code,
                Label = AccentLabels.TryGetValue(code, out var label) ? label : code,
                AccuracyPercentage = row?.AccuracyPercentage ?? 0m,
                QuestionsAttempted = row?.QuestionsAttempted ?? 0,
                MinutesListened = row?.MinutesListened ?? 0,
                SelfConfidenceRating = row?.SelfConfidenceRating ?? 0,
            });
        }

        return new MockResultResponse(
            SessionId: session.Id,
            RawScore: rawScore,
            ScaledScore: scaledScore,
            GradeLabel: gradeLabel,
            SkillRadar: radar,
            AccentChart: accents,
            PredictedScoreLow: predictedLow,
            PredictedScoreHigh: predictedHigh,
            SubmittedAt: session.CompletedAt ?? _clock.GetUtcNow(),
            DeadlineAt: GetSessionDeadlineAt(session, metadata),
            TimedOut: timedOut,
            ScoreConversionTableVersionKey: hasApprovedConversion ? conversion.TableVersionKey : null,
            ScoreConversionErrorCode: hasApprovedConversion ? conversion.ErrorCode : "score_conversion_unavailable",
            MarkingPolicyVersionKey: metadata.MarkingPolicyVersionKey,
            ScoreConversionPassed: hasApprovedConversion ? conversion.Passed : null);
    }

    private static MockSessionMetadata ReadSessionMetadata(ListeningPracticeSession session)
    {
        try
        {
            return JsonSerializer.Deserialize<MockSessionMetadata>(
                session.MetadataJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? new MockSessionMetadata();
        }
        catch (JsonException)
        {
            return new MockSessionMetadata();
        }
    }

    private static void ValidateMockQuestionSet(
        IReadOnlyList<ListeningQuestion> questions,
        IReadOnlyList<string> questionIds,
        string templateTitle)
    {
        if (questions.Count != MockTotalQuestions
            || questions.Select(q => q.Id).Distinct(StringComparer.Ordinal).Count() != MockTotalQuestions)
        {
            throw ApiException.Conflict(
                "listening_mock_structure_invalid",
                $"Published Listening mock '{templateTitle}' references missing or duplicate questions.");
        }

        var questionNumbers = questions
            .Select(q => q.QuestionNumber)
            .OrderBy(number => number)
            .ToArray();
        if (!Enumerable.Range(1, MockTotalQuestions).SequenceEqual(questionNumbers))
        {
            throw ApiException.Conflict(
                "listening_mock_structure_invalid",
                "Listening mock questions must cover question numbers 1 through 42 exactly once.");
        }

        var partA = questions.Where(IsPartA).ToArray();
        var partBC = questions.Where(q => !IsPartA(q)).ToArray();
        var partB = questions.Where(IsPartB).ToArray();
        var partC = questions.Where(IsPartC).ToArray();
        if (partA.Length != 24 || partB.Length != 6 || partC.Length != 12
            || partBC.Length != 18
            || questionIds.Count != questions.Count)
        {
            throw ApiException.Conflict(
                "listening_mock_structure_invalid",
                "Listening mock structure must contain Part A 24, Part B 6, and Part C 12 questions.");
        }

        foreach (var question in questions)
        {
            var canonical = TryReadString(question.CorrectAnswerJson)?.Trim();
            if (question.Points != 1 || string.IsNullOrWhiteSpace(canonical))
            {
                throw ApiException.Conflict(
                    "listening_mock_structure_invalid",
                    "Every Listening mock question must be one mark and have a canonical answer.");
            }

            if (IsPartA(question))
            {
                if (question.QuestionType is not (ListeningQuestionType.ShortAnswer or ListeningQuestionType.FillInBlank))
                {
                    throw ApiException.Conflict(
                        "listening_mock_structure_invalid",
                        "Listening Part A mock questions must be typed responses.");
                }

                continue;
            }

            var options = question.Options
                .OrderBy(option => option.DisplayOrder)
                .ToArray();
            var expectedOptionCount = IsPartC(question) ? 4 : 3;
            if (question.QuestionType != (expectedOptionCount == 4
                    ? ListeningQuestionType.MultipleChoice4
                    : ListeningQuestionType.MultipleChoice3)
                || options.Length != expectedOptionCount
                || options.Any(option => string.IsNullOrWhiteSpace(option.OptionKey))
                || options.Select(option => option.OptionKey.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).Count() != expectedOptionCount
                || options.Count(option => option.IsCorrect) != 1
                || options.Count(option => string.Equals(
                    option.OptionKey.Trim(),
                    canonical,
                    StringComparison.OrdinalIgnoreCase)) != 1)
            {
                throw ApiException.Conflict(
                    "listening_mock_structure_invalid",
                    $"Listening Part B mock questions must have exactly three options and Part C mock questions exactly four options, with one correct key.");
            }
        }
    }

    private static bool IsPartA(ListeningQuestion question) =>
        question.Part?.PartCode is ListeningPartCode.A1 or ListeningPartCode.A2;

    private static bool IsPartB(ListeningQuestion question) =>
        question.Part?.PartCode is ListeningPartCode.B1
            or ListeningPartCode.B2
            or ListeningPartCode.B3
            or ListeningPartCode.B4
            or ListeningPartCode.B5
            or ListeningPartCode.B6;

    private static bool IsPartC(ListeningQuestion question) =>
        question.Part?.PartCode is ListeningPartCode.C1 or ListeningPartCode.C2;

    private static ListeningMockQuestionSnapshot BuildQuestionSnapshot(ListeningQuestion question)
        => new(
            question.Version,
            question.QuestionNumber,
            question.Points,
            question.QuestionType,
            question.CorrectAnswerJson,
            question.AcceptedSynonymsJson,
            question.CaseSensitive,
            question.SubSkillTagsCsv,
            question.Accent,
            question.ExplanationMarkdown,
            question.TranscriptEvidenceText,
            question.TranscriptEvidenceStartMs,
            question.TranscriptEvidenceEndMs,
            question.Options
                .OrderBy(option => option.DisplayOrder)
                .Select(option => new ListeningMockOptionSnapshot(
                    option.Id,
                    option.OptionKey,
                    option.DisplayOrder,
                    option.Text,
                    option.IsCorrect,
                    option.Version,
                    option.DistractorCategory,
                    option.WhyWrongMarkdown))
                .ToArray());

    private static Dictionary<string, ListeningQuestion> BuildSnapshotQuestions(
        IReadOnlyList<ListeningQuestion> currentQuestions,
        IReadOnlyDictionary<string, ListeningMockQuestionSnapshot>? snapshots)
    {
        if (snapshots is null)
        {
            throw ApiException.Conflict(
                "listening_mock_snapshot_missing",
                "This Listening mock has no immutable question snapshot and cannot be graded.");
        }

        var result = new Dictionary<string, ListeningQuestion>(StringComparer.Ordinal);
        foreach (var current in currentQuestions)
        {
            if (!snapshots.TryGetValue(current.Id, out var snapshot))
            {
                throw ApiException.Conflict(
                    "listening_mock_snapshot_missing",
                    "A Listening mock question snapshot is incomplete; grading is held for review.");
            }

            result[current.Id] = new ListeningQuestion
            {
                Id = current.Id,
                QuestionNumber = snapshot.QuestionNumber,
                Points = snapshot.Points,
                QuestionType = snapshot.QuestionType,
                CorrectAnswerJson = snapshot.CorrectAnswerJson,
                AcceptedSynonymsJson = snapshot.AcceptedSynonymsJson,
                CaseSensitive = snapshot.CaseSensitive,
                SubSkillTagsCsv = snapshot.SubSkillTagsCsv,
                Accent = snapshot.Accent,
            };
        }

        return result;
    }

    private static DateTimeOffset? GetSessionDeadlineAt(
        ListeningPracticeSession session,
        MockSessionMetadata metadata)
    {
        if (metadata.DeadlineAt is { } deadlineAt)
            return deadlineAt;
        if (metadata.DurationSeconds is int durationSeconds && durationSeconds > 0)
            return session.StartedAt.AddSeconds(durationSeconds);
        return null;
    }

    private sealed record MockSessionMetadata(
        DateTimeOffset? DeadlineAt = null,
        string? ScoreConversionSnapshotJson = null,
        int? DurationSeconds = null,
        string? ScoreConversionTableVersionKey = null,
        string? MarkingPolicyVersionId = null,
        string? MarkingPolicyVersionKey = null,
        AssessmentMarkingPolicyDocument? MarkingPolicy = null,
        IReadOnlyDictionary<string, ListeningMockQuestionSnapshot>? QuestionSnapshots = null);

    /// <summary>Recompute the learner's readiness score on
    /// LearnerListeningProfile based on the latest skill + accent state. This
    /// is a lightweight echo of <see cref="IListeningPathwayAnalyticsService"/>
    /// — full computation lives there, but mocks should leave a fresh value
    /// on the profile row so other surfaces (dashboard chips) see it
    /// immediately without a separate trip through the analytics service.</summary>
    private async Task RefreshReadinessAsync(string userId, CancellationToken ct)
    {
        var profile = await _db.LearnerListeningProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null) return;

        // Pull skills + most recent mock + accent confidence in parallel-safe
        // queries (no shared cursors across awaits on DbContext).
        var skillRows = await _db.LearnerListeningSkillScores
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);

        var accentRows = await _db.LearnerAccentProgresses
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .ToListAsync(ct);

        var lastMockScore = await _db.ListeningPracticeSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId
                && s.SessionType == "mock"
                && s.CompletedAt != null
                && s.Score != null)
            .OrderByDescending(s => s.CompletedAt)
            .Select(s => (int?)s.Score)
            .FirstOrDefaultAsync(ct);

        // Components (each 0–100, weights per spec §9.6):
        //  • mock avg score (40%)
        //  • lowest sub-skill out of 10 → /10 *100 (20%)
        //  • accent confidence avg out of 100 (15%)
        //  • days-until-exam pacing (5%) — neutral if no exam date
        //  • consistency (10%) — left to analytics service; we use 50 here
        //  • pronunciation retention (10%) — pulled lazily
        var mockComponent = lastMockScore.HasValue
            ? Math.Clamp(lastMockScore.Value / (double)MockTotalQuestions * 100.0, 0, 100)
            : 0.0;

        var lowestSkill = skillRows.Count > 0
            ? (double)skillRows.Min(s => s.CurrentScore) * 10.0
            : 0.0;

        var accentAvg = accentRows.Count > 0
            ? (double)accentRows.Average(a => a.AccuracyPercentage)
            : 0.0;

        var examComponent = 50.0; // neutral — analytics service refines

        var pronunciationAvg = await _db.LearnerPronunciationCards
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => (double?)c.RetentionScore)
            .AverageAsync(ct) ?? 0.0;

        var consistencyComponent = 50.0; // neutral — analytics service refines

        var readiness =
              0.40 * mockComponent
            + 0.20 * lowestSkill
            + 0.15 * accentAvg
            + 0.05 * examComponent
            + 0.10 * consistencyComponent
            + 0.10 * pronunciationAvg;

        profile.CurrentReadinessScore = (int)Math.Round(
            Math.Clamp(readiness, 0, 100), MidpointRounding.AwayFromZero);
        profile.UpdatedAt = _clock.GetUtcNow();

        await _db.SaveChangesAsync(ct);
    }

    private static int CountQuestionIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return 0;
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return list?.Count ?? 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static string? TryReadString(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<string>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<string> DeserializeQuestionIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return new List<string>();
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return list ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static int ComputeDurationSeconds(
        DateTimeOffset startedAt,
        DateTimeOffset now,
        int clientReportedSeconds)
    {
        // Mirrors the diagnostic path's heuristic: trust the client-reported
        // value only when it falls within a sane window relative to wall time.
        var serverDelta = (int)Math.Max(0, (now - startedAt).TotalSeconds);
        if (clientReportedSeconds <= 0) return serverDelta;
        if (clientReportedSeconds > serverDelta + 300) return serverDelta;
        return clientReportedSeconds;
    }

    private static int ClampInt(int value, int min, int max)
        => value < min ? min : value > max ? max : value;
}
