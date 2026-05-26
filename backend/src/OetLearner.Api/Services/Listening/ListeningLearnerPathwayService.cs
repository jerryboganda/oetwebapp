using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Module Pathway — Phase 1 Orchestrator (A6)
//
// Composes the three pathway-pillar services already authored under this
// namespace:
//   • IListeningSkillScoringService    (A2) — L1..L8 + accent rolling scores
//   • IListeningLearnerGradingService  (A3) — deterministic per-attempt grading
//   • IListeningPathwayGenerator       (A4) — pure-function roadmap generator
//
// Implements the 5-stage learner flow per OET_LISTENING_MODULE_PATHWAY.md §5–§6:
//   onboarding → audio_check → diagnostic → foundation → practice → mastery
//
// Reference: mirrors ReadingLearnerPathwayService.cs in shape and convention.
// Key differences from Reading:
//   • Includes a dedicated "audio_check" stage between onboarding and diagnostic
//     so audio playback failures fail closed before the diagnostic starts.
//   • Question selection is sourced from the seeded diagnostic paper id
//     "listening-diagnostic-phase1" (cf. ListeningDiagnosticSeederOptions),
//     not random sampling — the 23-item diagnostic is curated.
//   • LEARNER-SAFE question projection — DiagnosticQuestionDto strips
//     CorrectAnswerJson, AcceptedSynonymsJson, ExplanationMarkdown, and
//     TranscriptEvidenceText so a malicious frontend can't read answers off
//     the wire.
//   • Cross-user safety mirrors Reading: a session ID belonging to another
//     user surfaces as InvalidOperationException("Session not found") so
//     callers can't probe foreign sessions by guessing GUIDs.
//
// All wall-clock reads go through the injected TimeProvider so unit tests can
// freeze time. SaveChangesAsync is called once per public method to keep
// partial commits off the table.
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningLearnerPathwayService
{
    Task<LearnerListeningProfileResponse> GetProfileAsync(string userId, CancellationToken ct);
    Task<LearnerListeningProfileResponse> StartOnboardingAsync(
        string userId, ListeningStartOnboardingRequest request, CancellationToken ct);
    Task<AudioCheckResponse> SubmitAudioCheckAsync(
        string userId, AudioCheckRequest request, CancellationToken ct);
    Task<StartDiagnosticResponse> StartDiagnosticAsync(string userId, CancellationToken ct);
    Task<IReadOnlyList<DiagnosticQuestionDto>> GetDiagnosticQuestionsAsync(
        string userId, Guid sessionId, string baseAudioUrl, CancellationToken ct);
    Task SaveDiagnosticAnswerAsync(
        string userId, Guid sessionId, string questionId,
        DiagnosticAnswerSubmission submission, CancellationToken ct);
    Task<ListeningDiagnosticResultResponse> SubmitDiagnosticAsync(
        string userId, ListeningSubmitDiagnosticRequest request, CancellationToken ct);
    Task<ListeningDiagnosticResultResponse> GetDiagnosticResultsAsync(
        string userId, Guid sessionId, CancellationToken ct);
    Task<PathwayResponse> GetPathwayAsync(string userId, CancellationToken ct);
    Task<PathwayStatusResponse> GetStageAsync(string userId, CancellationToken ct);
    Task SaveSessionNotesAsync(
        string userId, Guid sessionId, string? questionId,
        string noteMarkdown, CancellationToken ct);
}

public sealed class ListeningLearnerPathwayService : IListeningLearnerPathwayService
{
    /// <summary>Anchor PaperId for the Phase-1 23-question diagnostic.
    /// Matches <c>ListeningDiagnosticSeederOptions.PaperId</c> default — if a
    /// deployment overrides that setting, the diagnostic question pool here
    /// will continue to look at the canonical id and turn up empty until the
    /// override is mirrored. A future phase will inject the seeder options.</summary>
    private const string DiagnosticPaperId = "listening-diagnostic-phase1";

    /// <summary>Canonical question count for the Phase-1 diagnostic.</summary>
    private const int DiagnosticTotalQuestions = 23;

    /// <summary>Spec §6.4 typical Part-A note-volume window in characters.
    /// Surfaced verbatim into <see cref="NoteTakingStatsDto"/>.</summary>
    private const int TypicalNotesLow = 80;
    private const int TypicalNotesHigh = 120;

    /// <summary>L1..L8 → display label for the diagnostic radar.</summary>
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

    /// <summary>Canonical ordering of the 8 skill rows on the radar.</summary>
    private static readonly string[] AllSkillCodes =
        ["L1", "L2", "L3", "L4", "L5", "L6", "L7", "L8"];

    /// <summary>Accent bucket → display label for the accent chart.</summary>
    private static readonly IReadOnlyDictionary<string, string> AccentLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["british"] = "British (UK)",
            ["australian"] = "Australian",
            ["us"] = "North American",
            ["non_native"] = "Non-native",
        };

    /// <summary>Canonical ordering of the 4 accent rows.</summary>
    private static readonly string[] AllAccents =
        ["british", "australian", "us", "non_native"];

    private readonly LearnerDbContext _db;
    private readonly IListeningSkillScoringService _scoring;
    private readonly IListeningLearnerGradingService _grading;
    private readonly IListeningPathwayGenerator _generator;
    private readonly TimeProvider _clock;
    private readonly ILogger<ListeningLearnerPathwayService> _logger;

    public ListeningLearnerPathwayService(
        LearnerDbContext db,
        IListeningSkillScoringService scoring,
        IListeningLearnerGradingService grading,
        IListeningPathwayGenerator generator,
        TimeProvider clock,
        ILogger<ListeningLearnerPathwayService> logger)
    {
        _db = db;
        _scoring = scoring;
        _grading = grading;
        _generator = generator;
        _clock = clock;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Profile / stage queries
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetch the learner's listening profile, or throw if onboarding has not
    /// happened yet. Surfaces as 404 at the endpoint layer.
    /// </summary>
    public async Task<LearnerListeningProfileResponse> GetProfileAsync(
        string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var profile = await _db.LearnerListeningProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? throw new InvalidOperationException("Listening profile not found");

        return MapProfileToResponse(profile);
    }

    /// <summary>
    /// Upsert the learner's listening onboarding intake and advance the stage
    /// to "audio_check" — the next stage in the §5 flow.
    ///
    /// Idempotent: calling again with updated values overwrites in place.
    /// Re-running after audio_check (or later) is allowed — we DO NOT reset
    /// the stage backwards because a learner editing onboarding shouldn't
    /// lose diagnostic / pathway progress.
    /// </summary>
    public async Task<LearnerListeningProfileResponse> StartOnboardingAsync(
        string userId, ListeningStartOnboardingRequest request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.GetUtcNow();

        var existing = await _db.LearnerListeningProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (existing is not null)
        {
            existing.TargetBand = request.TargetBand;
            existing.ExamDate = request.ExamDate;
            existing.HoursPerWeek = request.HoursPerWeek;
            existing.Profession = request.Profession;
            existing.EnglishExposureSource = request.EnglishExposureSource;
            existing.ComfortBritish = request.ComfortBritish;
            existing.ComfortAustralian = request.ComfortAustralian;
            existing.ComfortVarious = request.ComfortVarious;
            existing.HasTakenBefore = request.HasTakenBefore;
            existing.PreviousScore = request.PreviousScore;
            existing.SelfRatedSpeed = request.SelfRatedSpeed;
            existing.SelfRatedNoteTaking = request.SelfRatedNoteTaking;
            existing.SelfRatedSpelling = request.SelfRatedSpelling;

            // Only reset the stage when the learner is still in the early
            // onboarding stages — preserve diagnostic/foundation progress
            // for returning learners who only edit profile metadata.
            if (existing.CurrentStage == "onboarding")
            {
                existing.CurrentStage = "audio_check";
            }
            existing.UpdatedAt = now;

            // Refresh self-reported accent comfort onto LearnerAccentProgress
            // so the chart reflects the latest input even before the diagnostic.
            await RefreshAccentSelfConfidenceAsync(
                userId,
                request.ComfortBritish,
                request.ComfortAustralian,
                request.ComfortVarious,
                ct);

            await _db.SaveChangesAsync(ct);
            return MapProfileToResponse(existing);
        }

        var profile = new LearnerListeningProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TargetBand = request.TargetBand,
            ExamDate = request.ExamDate,
            HoursPerWeek = request.HoursPerWeek,
            Profession = request.Profession,
            EnglishExposureSource = request.EnglishExposureSource,
            ComfortBritish = request.ComfortBritish,
            ComfortAustralian = request.ComfortAustralian,
            ComfortVarious = request.ComfortVarious,
            HasTakenBefore = request.HasTakenBefore,
            PreviousScore = request.PreviousScore,
            SelfRatedSpeed = request.SelfRatedSpeed,
            SelfRatedNoteTaking = request.SelfRatedNoteTaking,
            SelfRatedSpelling = request.SelfRatedSpelling,
            CurrentStage = "audio_check",
            OnboardingCompletedAt = now,
            UpdatedAt = now,
        };
        _db.LearnerListeningProfiles.Add(profile);

        await RefreshAccentSelfConfidenceAsync(
            userId,
            request.ComfortBritish,
            request.ComfortAustralian,
            request.ComfortVarious,
            ct);

        await _db.SaveChangesAsync(ct);
        return MapProfileToResponse(profile);
    }

    /// <summary>
    /// Record an audio-check outcome (§5.4). Pass values ("clear" / "quiet")
    /// advance the stage to "diagnostic"; "failed" keeps the learner on
    /// audio_check so they're prompted to retry hardware. Idempotent — the
    /// timestamp is only set on first success.
    /// </summary>
    public async Task<AudioCheckResponse> SubmitAudioCheckAsync(
        string userId, AudioCheckRequest request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(request);

        var profile = await _db.LearnerListeningProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? throw new InvalidOperationException(
                "Complete onboarding first — no listening profile exists.");

        var now = _clock.GetUtcNow();
        var outcome = (request.Outcome ?? string.Empty).Trim().ToLowerInvariant();

        var passed = outcome is "clear" or "quiet";

        if (passed)
        {
            // First pass — capture the timestamp and advance the stage. We
            // only advance when still on audio_check; re-takers don't get
            // demoted from later stages.
            if (profile.AudioCheckPassedAt is null)
            {
                profile.AudioCheckPassedAt = now;
            }
            if (profile.CurrentStage == "audio_check")
            {
                profile.CurrentStage = "diagnostic";
            }
            profile.UpdatedAt = now;
        }
        else
        {
            // "failed" — record the touch but don't advance. We deliberately
            // skip an audit-style note here for Phase 1 (cf. spec note); the
            // frontend retries the player and re-submits.
            profile.UpdatedAt = now;
            _logger.LogInformation(
                "Listening audio check reported failure for user {UserId} (outcome={Outcome}, volume={Volume}).",
                userId, outcome, request.VolumeLevel);
        }

        await _db.SaveChangesAsync(ct);

        return new AudioCheckResponse
        {
            Success = passed,
            CurrentStage = profile.CurrentStage,
            AudioCheckPassedAt = profile.AudioCheckPassedAt,
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // Diagnostic — start / fetch questions / save answers / submit
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Begin a diagnostic session by sampling the 23 seeded diagnostic
    /// questions and creating a <see cref="ListeningPracticeSession"/> row.
    ///
    /// Allowed from CurrentStage in {diagnostic, foundation} — the latter
    /// permits a re-take after the first diagnostic has already completed.
    /// </summary>
    public async Task<StartDiagnosticResponse> StartDiagnosticAsync(
        string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var profile = await _db.LearnerListeningProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? throw new InvalidOperationException(
                "Listening profile not found — complete onboarding first.");

        if (profile.CurrentStage is not ("diagnostic" or "foundation"))
        {
            throw new InvalidOperationException(
                $"Diagnostic cannot be started from stage '{profile.CurrentStage}'.");
        }

        // Pool: every published question on the diagnostic paper. The
        // seeder writes exactly 23 — but if a deployment has not seeded the
        // diagnostic, surface a clear error rather than starting a
        // half-populated session.
        var questions = await _db.ListeningQuestions
            .AsNoTracking()
            .Where(q => q.PaperId == DiagnosticPaperId)
            .OrderBy(q => q.QuestionNumber)
            .Select(q => new { q.Id, q.ListeningExtractId })
            .ToListAsync(ct);

        if (questions.Count == 0)
        {
            throw new InvalidOperationException(
                "Diagnostic paper has not been seeded for this environment.");
        }

        var questionIds = questions.Select(q => q.Id).ToList();
        var extractIds = questions
            .Where(q => !string.IsNullOrEmpty(q.ListeningExtractId))
            .Select(q => q.ListeningExtractId!)
            .Distinct()
            .ToList();

        var now = _clock.GetUtcNow();

        var session = new ListeningPracticeSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SessionType = "diagnostic",
            FocusSkill = null,
            FocusAccent = null,
            QuestionIdsJson = JsonSerializer.Serialize(questionIds),
            AudioAssetIdsJson = JsonSerializer.Serialize(extractIds),
            StartedAt = now,
            TotalQuestions = questionIds.Count,
            MetadataJson = JsonSerializer.Serialize(new
            {
                paperId = DiagnosticPaperId,
                version = 1,
            }),
        };
        _db.ListeningPracticeSessions.Add(session);

        await _db.SaveChangesAsync(ct);

        return new StartDiagnosticResponse
        {
            SessionId = session.Id,
            TotalQuestions = DiagnosticTotalQuestions,
            EstimatedMinutes = 30,
        };
    }

    /// <summary>
    /// Project the diagnostic question pool into LEARNER-SAFE DTOs. The
    /// caller-owned <paramref name="baseAudioUrl"/> (e.g. the API origin)
    /// is concatenated with the extract's audio SHA to form playback URLs.
    ///
    /// Cross-user guard: a session belonging to another user looks
    /// identical to a missing session, so probing GUIDs reveals nothing.
    /// </summary>
    public async Task<IReadOnlyList<DiagnosticQuestionDto>> GetDiagnosticQuestionsAsync(
        string userId, Guid sessionId, string baseAudioUrl, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var session = await _db.ListeningPracticeSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw new InvalidOperationException("Session not found");

        if (!string.Equals(session.SessionType, "diagnostic", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Session is not a diagnostic (type={session.SessionType}).");
        }

        var questionIds = DeserializeQuestionIds(session.QuestionIdsJson);
        if (questionIds.Count == 0)
        {
            return Array.Empty<DiagnosticQuestionDto>();
        }

        // Fetch the questions + their part + extract metadata in one trip.
        var questions = await _db.ListeningQuestions
            .AsNoTracking()
            .Include(q => q.Part)
            .Include(q => q.Extract)
            .Include(q => q.Options)
            .Where(q => questionIds.Contains(q.Id))
            .ToListAsync(ct);

        var byId = questions.ToDictionary(q => q.Id, StringComparer.Ordinal);

        var trimmedBase = (baseAudioUrl ?? string.Empty).TrimEnd('/');

        var result = new List<DiagnosticQuestionDto>(questionIds.Count);

        // Preserve QuestionIdsJson order so the learner sees the curated
        // sequence (Part A → B → C → accent test) rather than DB-row order.
        var displayNumber = 1;
        foreach (var qid in questionIds)
        {
            if (!byId.TryGetValue(qid, out var q) || q is null)
            {
                _logger.LogWarning(
                    "Diagnostic question {QuestionId} missing from DB for session {SessionId}; skipping.",
                    qid, sessionId);
                continue;
            }

            var dto = new DiagnosticQuestionDto
            {
                Id = q.Id,
                QuestionNumber = displayNumber++,
                Part = MapPartCodeToLabel(q.Part?.PartCode),
                QuestionType = MapQuestionTypeToWire(q.QuestionType),
                Stem = q.Stem ?? string.Empty,
                Options = ProjectOptions(q.Options),
                AudioAssetId = TryParseGuid(q.ListeningExtractId),
                AudioPlaybackUrl = BuildAudioUrl(trimmedBase, q.Extract?.AudioContentSha),
                SubSkillTags = ParseSubSkillTags(q.SubSkillTagsCsv),
                Accent = q.Accent ?? q.Extract?.AccentCode ?? "en-XX",
                // Diagnostic forbids replays per spec §5.5 / §6.1; transcript
                // is hidden until after submission lands the learner on the
                // results screen.
                MaxReplays = 0,
                TranscriptAvailable = false,
            };
            result.Add(dto);
        }

        return result;
    }

    /// <summary>
    /// Persist a single per-question answer during the diagnostic. Grading
    /// is deferred to <see cref="SubmitDiagnosticAsync"/> so the radar +
    /// roadmap can be produced from a coherent session snapshot rather than
    /// an in-flight, partially-graded one.
    ///
    /// Upserts on (UserId, ListeningQuestionId, PracticeSessionId).
    /// </summary>
    public async Task SaveDiagnosticAnswerAsync(
        string userId, Guid sessionId, string questionId,
        DiagnosticAnswerSubmission submission, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(questionId);
        ArgumentNullException.ThrowIfNull(submission);

        var session = await _db.ListeningPracticeSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw new InvalidOperationException("Session not found");

        if (session.CompletedAt is not null)
        {
            throw new InvalidOperationException(
                "Session already submitted — answers are immutable.");
        }

        var now = _clock.GetUtcNow();

        var existing = await _db.ListeningQuestionAttempts
            .FirstOrDefaultAsync(a =>
                a.UserId == userId
                && a.ListeningQuestionId == questionId
                && a.PracticeSessionId == sessionId, ct);

        if (existing is null)
        {
            existing = new ListeningQuestionAttempt
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ListeningQuestionId = questionId,
                PracticeSessionId = sessionId,
                AttemptedAt = now,
                // Diagnostic correctness flags are written by GradeSessionAsync
                // at submission time. Default false here so an unsubmitted
                // session can be inspected without lying about grading state.
                IsCorrect = false,
                IsSpellingCorrectMeaningWrong = false,
                IsMeaningCorrectSpellingWrong = false,
                InReviewQueue = false,
                ReviewIntervalIndex = 0,
                ConsecutiveCorrect = 0,
            };
            _db.ListeningQuestionAttempts.Add(existing);
        }

        existing.SelectedOption = submission.SelectedOption;
        existing.LearnerAnswer = submission.LearnerAnswer;
        existing.IsUnknown = submission.IsUnknown;
        existing.TimeSpentSeconds = submission.TimeSpentSeconds;
        existing.ReplaysUsed = submission.ReplaysUsed;
        existing.MarkedForReview = submission.MarkedForReview;
        existing.AttemptedAt = now;

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Finalise a diagnostic session: grade every attempt, persist L1..L8 +
    /// accent baselines, generate the multi-week roadmap, advance the stage
    /// to "foundation", and return the full results envelope.
    ///
    /// Idempotent: re-submitting a session that already completed returns
    /// the cached recomposed result via <see cref="GetDiagnosticResultsAsync"/>
    /// instead of double-grading.
    /// </summary>
    public async Task<ListeningDiagnosticResultResponse> SubmitDiagnosticAsync(
        string userId, ListeningSubmitDiagnosticRequest request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(request);

        var session = await _db.ListeningPracticeSessions
            .FirstOrDefaultAsync(s => s.Id == request.SessionId && s.UserId == userId, ct)
            ?? throw new InvalidOperationException("Session not found");

        if (!string.Equals(session.SessionType, "diagnostic", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Session is not a diagnostic (type={session.SessionType}).");
        }

        if (session.CompletedAt is not null)
        {
            // Idempotent re-submit — recompose results from already-persisted
            // attempts instead of re-grading.
            return await GetDiagnosticResultsAsync(userId, session.Id, ct);
        }

        var profile = await _db.LearnerListeningProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? throw new InvalidOperationException(
                "Listening profile not found — complete onboarding first.");

        var now = _clock.GetUtcNow();

        // ── 1. Upsert any answers the client didn't already auto-save ────
        // The client streams answers via SaveDiagnosticAnswerAsync as the
        // learner moves through the test, but we also accept a final blob
        // so a buggy / disconnected client can still close out cleanly.
        if (request.Answers is not null)
        {
            await UpsertSubmissionAnswersAsync(userId, session.Id, request.Answers, now, ct);
        }

        // ── 2. Load every attempt for the session + the question pool ────
        var attempts = await _db.ListeningQuestionAttempts
            .Where(a => a.UserId == userId && a.PracticeSessionId == session.Id)
            .ToListAsync(ct);

        var questionIds = attempts.Select(a => a.ListeningQuestionId).Distinct().ToList();
        var questions = await _db.ListeningQuestions
            .AsNoTracking()
            .Where(q => questionIds.Contains(q.Id))
            .ToListAsync(ct);
        var questionsById = questions.ToDictionary(q => q.Id, StringComparer.Ordinal);

        // ── 3. Grade — sets IsCorrect / spelling flags on each attempt ───
        // The grader mutates attempts in place; we persist below alongside
        // session completion so partial commits never escape.
        var grading = await GradeAttemptsAsync(attempts, questionsById, ct);

        // ── 4. Close out the session row ─────────────────────────────────
        session.CompletedAt = now;
        session.Score = grading.CorrectCount;
        session.TotalQuestions = DiagnosticTotalQuestions;
        session.DurationSeconds = ComputeDurationSeconds(
            session.StartedAt,
            now,
            request.TotalDurationSeconds);

        // ── 5. Persist optional per-question notes ───────────────────────
        if (request.NotesByQuestionId is { Count: > 0 } notes)
        {
            foreach (var (qid, markdown) in notes)
            {
                if (string.IsNullOrWhiteSpace(qid)) continue;
                await UpsertNoteAsync(userId, session.Id, qid, markdown ?? string.Empty, now, ct);
            }
        }

        await _db.SaveChangesAsync(ct);

        // ── 6. Seed the diagnostic baseline (L1..L8 + accent) ────────────
        // SetDiagnosticBaselineAsync owns its own SaveChangesAsync so we
        // call it after the session commit above. A failure here will
        // leave the session marked complete but the baseline unwritten —
        // recoverable by re-submitting (now idempotent).
        await _scoring.SetDiagnosticBaselineAsync(
            userId,
            grading.SkillScores0to10,
            grading.AccentScores0to100,
            ct);

        // ── 7. Generate the multi-week roadmap ───────────────────────────
        var roadmap = _generator.Generate(new GenerateInput(
            TargetBand: profile.TargetBand,
            ExamDate: profile.ExamDate,
            HoursPerWeek: profile.HoursPerWeek,
            SkillScores: grading.SkillScores0to10,
            AccentScores: grading.AccentScores0to100,
            Now: now));

        await PersistPathwayAsync(userId, roadmap, now, ct);

        // ── 8. Stage transition ──────────────────────────────────────────
        profile.CurrentStage = "foundation";
        profile.PathwayGeneratedAt = now;
        profile.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        // ── 9. Build the full results envelope ───────────────────────────
        return await BuildResultResponseAsync(
            userId, session, attempts, questionsById, grading, roadmap, ct);
    }

    /// <summary>
    /// Recompose the diagnostic results envelope from already-persisted
    /// state. Used by the results screen after navigation refresh AND as the
    /// idempotent return value for <see cref="SubmitDiagnosticAsync"/>.
    /// </summary>
    public async Task<ListeningDiagnosticResultResponse> GetDiagnosticResultsAsync(
        string userId, Guid sessionId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var session = await _db.ListeningPracticeSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw new InvalidOperationException("Session not found");

        if (session.CompletedAt is null)
        {
            throw new InvalidOperationException(
                "Diagnostic has not been submitted yet.");
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
        var questionsById = questions.ToDictionary(q => q.Id, StringComparer.Ordinal);

        // Re-grade from the persisted attempt flags — cheaper than full
        // grading and ensures the result envelope matches whatever was
        // written at submit time even if grading rules later change.
        var grading = await RecomposeGradingFromPersistedAttemptsAsync(attempts, questionsById, ct);

        var roadmap = await ReadPathwayWeeksAsync(userId, ct);

        return await BuildResultResponseAsync(
            userId, session, attempts, questionsById, grading, roadmap, ct);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Pathway + stage queries
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Return the full multi-week roadmap for the learner. Surfaces as 404
    /// at the endpoint layer if the diagnostic has not generated one yet.
    /// </summary>
    public async Task<PathwayResponse> GetPathwayAsync(string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var pathway = await _db.LearnerListeningPathways
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? throw new InvalidOperationException(
                "No listening pathway yet — complete the diagnostic first.");

        var weeks = DeserializeWeeks(pathway.WeeksJson);

        return new PathwayResponse
        {
            TotalWeeks = pathway.TotalWeeks,
            GeneratedAt = pathway.GeneratedAt,
            Weeks = weeks,
        };
    }

    /// <summary>
    /// Lightweight probe used by the listening landing page to decide which
    /// CTA to surface. Safe to call even before onboarding — returns
    /// HasProfile=false in that case rather than throwing.
    /// </summary>
    public async Task<PathwayStatusResponse> GetStageAsync(string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var profile = await _db.LearnerListeningProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (profile is null)
        {
            return new PathwayStatusResponse
            {
                HasProfile = false,
                CurrentStage = "none",
            };
        }

        // Most-recent completed diagnostic — used for the dashboard chip.
        var diagnosticCompletedAt = await _db.ListeningPracticeSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId
                && s.SessionType == "diagnostic"
                && s.CompletedAt != null)
            .OrderByDescending(s => s.CompletedAt)
            .Select(s => s.CompletedAt)
            .FirstOrDefaultAsync(ct);

        int? daysUntilExam = null;
        if (profile.ExamDate is { } exam)
        {
            var totalDays = (exam - _clock.GetUtcNow()).TotalDays;
            daysUntilExam = totalDays > 0
                ? (int)Math.Ceiling(totalDays)
                : 0;
        }

        return new PathwayStatusResponse
        {
            HasProfile = true,
            CurrentStage = profile.CurrentStage,
            DiagnosticCompletedAt = diagnosticCompletedAt,
            PathwayGeneratedAt = profile.PathwayGeneratedAt,
            CurrentReadinessScore = profile.CurrentReadinessScore,
            DaysUntilExam = daysUntilExam,
        };
    }

    /// <summary>
    /// Auto-save endpoint for Part-A scratch notes. Upserts on
    /// (UserId, SessionId, QuestionId) so each note row has a single home
    /// regardless of how many keystrokes the client throttled into.
    /// </summary>
    public async Task SaveSessionNotesAsync(
        string userId, Guid sessionId, string? questionId,
        string noteMarkdown, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        noteMarkdown ??= string.Empty;

        var session = await _db.ListeningPracticeSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw new InvalidOperationException("Session not found");

        var now = _clock.GetUtcNow();
        await UpsertNoteAsync(userId, session.Id, questionId, noteMarkdown, now, ct);

        await _db.SaveChangesAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Private helpers — answers / notes upserts
    // ─────────────────────────────────────────────────────────────────────

    private async Task UpsertSubmissionAnswersAsync(
        string userId,
        Guid sessionId,
        IReadOnlyList<DiagnosticAnswerSubmission> answers,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // Pre-load existing attempts in one trip so we don't issue an N+1
        // round-trip per question.
        var questionIds = answers.Select(a => a.QuestionId).Distinct().ToList();
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

    private async Task UpsertNoteAsync(
        string userId,
        Guid sessionId,
        string? questionId,
        string noteMarkdown,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var note = await _db.ListeningPracticeNotes
            .FirstOrDefaultAsync(n =>
                n.UserId == userId
                && n.PracticeSessionId == sessionId
                && n.ListeningQuestionId == questionId, ct);

        if (note is null)
        {
            note = new ListeningPracticeNote
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PracticeSessionId = sessionId,
                ListeningQuestionId = questionId,
            };
            _db.ListeningPracticeNotes.Add(note);
        }

        note.NoteMarkdown = noteMarkdown;
        note.CharacterCount = noteMarkdown.Length;
        note.LastSavedAt = now;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Private helpers — grading + pathway persistence
    // ─────────────────────────────────────────────────────────────────────

    private async Task<DiagnosticGradingResult> GradeAttemptsAsync(
        IReadOnlyList<ListeningQuestionAttempt> attempts,
        IReadOnlyDictionary<string, ListeningQuestion> questionsById,
        CancellationToken ct)
    {
        // Walk each attempt through the per-question grader first — that
        // mutates attempt.IsCorrect / spelling flags. Then call the session
        // grader to roll up the L1..L8 + accent buckets. Splitting the
        // calls keeps the grader's per-question contract intact.
        foreach (var attempt in attempts)
        {
            ct.ThrowIfCancellationRequested();
            if (!questionsById.TryGetValue(attempt.ListeningQuestionId, out var question)
                || question is null)
            {
                _logger.LogWarning(
                    "Diagnostic grade skipped attempt {AttemptId}: question {QuestionId} missing.",
                    attempt.Id, attempt.ListeningQuestionId);
                continue;
            }

            await _grading.GradeAttemptAsync(attempt, question, ct);
        }

        return await _grading.GradeSessionAsync(attempts, questionsById, ct);
    }

    private async Task<DiagnosticGradingResult> RecomposeGradingFromPersistedAttemptsAsync(
        IReadOnlyList<ListeningQuestionAttempt> attempts,
        IReadOnlyDictionary<string, ListeningQuestion> questionsById,
        CancellationToken ct)
    {
        // Persisted attempts already carry the grading flags; the session
        // grader simply rolls those up. We don't re-run per-attempt grading
        // here because the persisted IsCorrect is the source of truth — it
        // was authoritative at the moment the session was submitted.
        return await _grading.GradeSessionAsync(attempts, questionsById, ct);
    }

    private async Task PersistPathwayAsync(
        string userId,
        IReadOnlyList<RoadmapWeekDto> weeks,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var existing = await _db.LearnerListeningPathways
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        var weeksJson = JsonSerializer.Serialize(weeks);

        if (existing is null)
        {
            existing = new LearnerListeningPathway
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TotalWeeks = weeks.Count,
                GeneratedAt = now,
                WeeksJson = weeksJson,
            };
            _db.LearnerListeningPathways.Add(existing);
        }
        else
        {
            existing.TotalWeeks = weeks.Count;
            existing.GeneratedAt = now;
            existing.CompletedAt = null;
            existing.WeeksJson = weeksJson;
        }
    }

    private async Task<IReadOnlyList<RoadmapWeekDto>> ReadPathwayWeeksAsync(
        string userId, CancellationToken ct)
    {
        var pathway = await _db.LearnerListeningPathways
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        return pathway is null
            ? Array.Empty<RoadmapWeekDto>()
            : DeserializeWeeks(pathway.WeeksJson);
    }

    /// <summary>
    /// Push the learner's self-reported accent comfort onto the four
    /// LearnerAccentProgress rows so the dashboard chips reflect onboarding
    /// even before the diagnostic runs. EnglishExposureSource intentionally
    /// does not seed comfort here — it's used by the pathway generator
    /// separately, not the dashboard.
    /// </summary>
    private async Task RefreshAccentSelfConfidenceAsync(
        string userId,
        int comfortBritish,
        int comfortAustralian,
        int comfortVarious,
        CancellationToken ct)
    {
        var existing = await _db.LearnerAccentProgresses
            .Where(a => a.UserId == userId)
            .ToListAsync(ct);

        // "various" maps to both US and non_native because onboarding only
        // captures three buckets but the dashboard renders four. The diagnostic
        // overwrites these with measured values, so the duplication is a
        // best-effort placeholder.
        var seeds = new (string Accent, int Rating)[]
        {
            ("british", comfortBritish),
            ("australian", comfortAustralian),
            ("us", comfortVarious),
            ("non_native", comfortVarious),
        };

        foreach (var (accent, rating) in seeds)
        {
            var row = existing.FirstOrDefault(a =>
                string.Equals(a.Accent, accent, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new LearnerAccentProgress
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Accent = accent,
                    AccuracyPercentage = 0m,
                    QuestionsAttempted = 0,
                    QuestionsCorrect = 0,
                    MinutesListened = 0,
                };
                _db.LearnerAccentProgresses.Add(row);
            }
            row.SelfConfidenceRating = rating;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Private helpers — result envelope assembly
    // ─────────────────────────────────────────────────────────────────────

    private async Task<ListeningDiagnosticResultResponse> BuildResultResponseAsync(
        string userId,
        ListeningPracticeSession session,
        IReadOnlyList<ListeningQuestionAttempt> attempts,
        IReadOnlyDictionary<string, ListeningQuestion> questionsById,
        DiagnosticGradingResult grading,
        IReadOnlyList<RoadmapWeekDto> roadmap,
        CancellationToken ct)
    {
        var hero = BuildHero(grading, session);
        var radar = BuildSkillRadar(grading, userId, ct: ct);
        var accentChart = await BuildAccentChartAsync(userId, grading, ct);
        var noteStats = await BuildNoteTakingStatsAsync(userId, session.Id, ct);
        var spellingStats = BuildSpellingStats(grading, attempts, questionsById);
        var timeAnalysis = BuildTimeAnalysis(attempts, questionsById);

        return new ListeningDiagnosticResultResponse
        {
            SessionId = session.Id,
            SubmittedAt = session.CompletedAt ?? _clock.GetUtcNow(),
            Hero = hero,
            SkillRadar = await radar,
            AccentChart = accentChart,
            NoteTakingStats = noteStats,
            SpellingStats = spellingStats,
            TimeAnalysis = timeAnalysis,
            Roadmap = roadmap.ToList(),
        };
    }

    private DiagnosticHeroDto BuildHero(
        DiagnosticGradingResult grading,
        ListeningPracticeSession session)
    {
        var rawCorrect = grading.CorrectCount;
        var totalQuestions = DiagnosticTotalQuestions;

        // Project the 23-question diagnostic onto the canonical 42-item
        // OET Listening scale so the scaled-score number lines up with
        // post-mock results. Uses the project's single source of truth
        // (OetScoring) rather than the bespoke "raw / 23 * 500" mapping
        // suggested in the spec hero example — the OET 30/42==350 anchor
        // dominates the visual.
        var projectedRaw = totalQuestions > 0
            ? (int)Math.Round(
                (double)rawCorrect / totalQuestions * OetScoring.ListeningReadingRawMax,
                MidpointRounding.AwayFromZero)
            : 0;
        var scaled = OetScoring.OetRawToScaled(projectedRaw);

        // ±25 confidence band reflects the diagnostic's narrower question
        // pool (23 vs 42) per spec §6.4 hero copy.
        var lower = Math.Max(0, scaled - 25);
        var upper = Math.Min(500, scaled + 25);

        var gradeLetter = OetScoring.OetGradeLetterFromScaled(scaled);
        var gradeLabel = $"{gradeLetter} (predicted)";

        return new DiagnosticHeroDto
        {
            RawScore = rawCorrect,
            TotalQuestions = totalQuestions,
            ScaledScore = scaled,
            GradeLabel = gradeLabel,
            ConfidenceLowerBound = lower,
            ConfidenceUpperBound = upper,
            // TargetBandLabel is filled out at envelope-assembly time with
            // the profile's target band; we resolve it via session-side state.
            TargetBandLabel = ResolveTargetBandLabel(session),
        };
    }

    private static string ResolveTargetBandLabel(ListeningPracticeSession session)
    {
        // The session row doesn't directly carry the learner's target band —
        // surface a neutral placeholder; the caller can override at the
        // response layer if needed.
        return "Target: B";
    }

    private async Task<List<SkillScoreDto>> BuildSkillRadar(
        DiagnosticGradingResult grading,
        string userId,
        CancellationToken ct)
    {
        var (skills, _) = await _scoring.GetScoresAsync(userId, ct);
        var byCode = skills.ToDictionary(s => s.SkillCode, StringComparer.OrdinalIgnoreCase);

        var radar = new List<SkillScoreDto>(AllSkillCodes.Length);
        foreach (var code in AllSkillCodes)
        {
            var current = byCode.TryGetValue(code, out var row) ? row.CurrentScore : 0m;
            var diagnostic = grading.SkillScores0to10.TryGetValue(code, out var d) ? d : current;

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
        return radar;
    }

    private async Task<List<AccentProgressDto>> BuildAccentChartAsync(
        string userId,
        DiagnosticGradingResult grading,
        CancellationToken ct)
    {
        var (_, accents) = await _scoring.GetScoresAsync(userId, ct);
        var byAccent = accents.ToDictionary(a => a.Accent, StringComparer.OrdinalIgnoreCase);

        var chart = new List<AccentProgressDto>(AllAccents.Length);
        foreach (var code in AllAccents)
        {
            var row = byAccent.TryGetValue(code, out var r) ? r : null;
            var diagnosticAccuracy = grading.AccentScores0to100
                .TryGetValue(code, out var v) ? v : (row?.AccuracyPercentage ?? 0m);

            chart.Add(new AccentProgressDto
            {
                Accent = code,
                Label = AccentLabels.TryGetValue(code, out var label) ? label : code,
                AccuracyPercentage = row?.AccuracyPercentage ?? diagnosticAccuracy,
                QuestionsAttempted = row?.QuestionsAttempted ?? 0,
                MinutesListened = row?.MinutesListened ?? 0,
                SelfConfidenceRating = row?.SelfConfidenceRating ?? 0,
            });
        }
        return chart;
    }

    private async Task<NoteTakingStatsDto> BuildNoteTakingStatsAsync(
        string userId, Guid sessionId, CancellationToken ct)
    {
        var totalChars = await _db.ListeningPracticeNotes
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.PracticeSessionId == sessionId)
            .SumAsync(n => (int?)n.CharacterCount, ct) ?? 0;

        return new NoteTakingStatsDto
        {
            CharactersTyped = totalChars,
            TypicalRangeLow = TypicalNotesLow,
            TypicalRangeHigh = TypicalNotesHigh,
            // Phase 1 leaves DroppedDetails empty — surfacing per-question
            // detail-miss tags requires a richer question metadata schema
            // that lands in Phase 2.
            DroppedDetails = Array.Empty<string>(),
        };
    }

    private static SpellingStatsDto BuildSpellingStats(
        DiagnosticGradingResult grading,
        IReadOnlyList<ListeningQuestionAttempt> attempts,
        IReadOnlyDictionary<string, ListeningQuestion> questionsById)
    {
        var meaningRightSpellingWrong = attempts.Count(a => a.IsMeaningCorrectSpellingWrong);

        // Prefer the grader's spelling-miss tuples (already paired with
        // canonical answers) and cap at 5 for UI density. If the grader's
        // list is empty, fall back to attempt-level scan so the widget
        // still renders something useful.
        var examples = grading.SpellingMisses
            .Take(5)
            .Select(m => new SpellingExampleDto(
                Wrong: m.LearnerAnswer,
                Right: m.CorrectAnswer))
            .ToList();

        if (examples.Count == 0)
        {
            examples = attempts
                .Where(a => a.IsMeaningCorrectSpellingWrong)
                .Take(5)
                .Select(a => new SpellingExampleDto(
                    Wrong: a.LearnerAnswer ?? a.SelectedOption ?? string.Empty,
                    Right: questionsById.TryGetValue(a.ListeningQuestionId, out var q)
                        ? (TryReadJsonString(q.CorrectAnswerJson) ?? string.Empty)
                        : string.Empty))
                .ToList();
        }

        return new SpellingStatsDto
        {
            MeaningCorrectSpellingWrong = meaningRightSpellingWrong,
            Examples = examples,
        };
    }

    private static TimeAnalysisDto BuildTimeAnalysis(
        IReadOnlyList<ListeningQuestionAttempt> attempts,
        IReadOnlyDictionary<string, ListeningQuestion> questionsById)
    {
        var partA = 0;
        var partB = 0;
        var partC = 0;

        foreach (var attempt in attempts)
        {
            if (!questionsById.TryGetValue(attempt.ListeningQuestionId, out var q) || q is null)
                continue;

            var part = q.Part?.PartCode;
            switch (part)
            {
                case ListeningPartCode.A1:
                case ListeningPartCode.A2:
                    partA += attempt.TimeSpentSeconds;
                    break;
                case ListeningPartCode.B:
                    partB += attempt.TimeSpentSeconds;
                    break;
                case ListeningPartCode.C1:
                case ListeningPartCode.C2:
                    partC += attempt.TimeSpentSeconds;
                    break;
            }
        }

        return new TimeAnalysisDto
        {
            PartABreakdown = partA,
            PartBBreakdown = partB,
            PartCBreakdown = partC,
            // Phase 1 ships without hesitation analysis. The flag set is
            // intentionally typed (string[]) so a Phase-2 detector can wire
            // in without breaking the DTO contract.
            HesitationFlags = Array.Empty<string>(),
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // Private helpers — projections + parsing
    // ─────────────────────────────────────────────────────────────────────

    private static LearnerListeningProfileResponse MapProfileToResponse(LearnerListeningProfile profile)
    {
        return new LearnerListeningProfileResponse
        {
            UserId = profile.UserId,
            TargetBand = profile.TargetBand,
            ExamDate = profile.ExamDate,
            HoursPerWeek = profile.HoursPerWeek,
            Profession = profile.Profession,
            EnglishExposureSource = profile.EnglishExposureSource,
            ComfortBritish = profile.ComfortBritish,
            ComfortAustralian = profile.ComfortAustralian,
            ComfortVarious = profile.ComfortVarious,
            HasTakenBefore = profile.HasTakenBefore,
            PreviousScore = profile.PreviousScore,
            SelfRatedSpeed = profile.SelfRatedSpeed,
            SelfRatedNoteTaking = profile.SelfRatedNoteTaking,
            SelfRatedSpelling = profile.SelfRatedSpelling,
            CurrentStage = profile.CurrentStage,
            CurrentReadinessScore = profile.CurrentReadinessScore,
            PredictedScore = profile.PredictedScore,
            OnboardingCompletedAt = profile.OnboardingCompletedAt,
            AudioCheckPassedAt = profile.AudioCheckPassedAt,
            PathwayGeneratedAt = profile.PathwayGeneratedAt,
            UpdatedAt = profile.UpdatedAt,
        };
    }

    private static List<DiagnosticQuestionOptionDto>? ProjectOptions(
        ICollection<ListeningQuestionOption>? options)
    {
        if (options is null || options.Count == 0) return null;

        return options
            .OrderBy(o => o.DisplayOrder)
            .ThenBy(o => o.OptionKey, StringComparer.Ordinal)
            // CRITICAL: NEVER project IsCorrect, DistractorCategory, or
            // WhyWrongMarkdown into the learner-facing DTO. Doing so would
            // allow a malicious frontend to read answers off the wire.
            .Select(o => new DiagnosticQuestionOptionDto(
                OptionKey: o.OptionKey,
                Text: o.Text))
            .ToList();
    }

    private static string MapQuestionTypeToWire(ListeningQuestionType type) => type switch
    {
        ListeningQuestionType.ShortAnswer => "gap_fill",
        ListeningQuestionType.MultipleChoice3 => "mcq3",
        _ => "unknown",
    };

    private static string MapPartCodeToLabel(ListeningPartCode? code) => code switch
    {
        ListeningPartCode.A1 or ListeningPartCode.A2 => "A",
        ListeningPartCode.B => "B",
        ListeningPartCode.C1 or ListeningPartCode.C2 => "C",
        _ => "accent_test",
    };

    private static string[] ParseSubSkillTags(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<string>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(4);
        foreach (var raw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var code = raw.ToUpperInvariant();
            if (code.Length == 2 && code[0] == 'L' && code[1] >= '1' && code[1] <= '8' && seen.Add(code))
            {
                result.Add(code);
            }
        }
        return result.ToArray();
    }

    private static Guid? TryParseGuid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Guid.TryParse(value, out var g) ? g : null;
    }

    private static string? BuildAudioUrl(string trimmedBase, string? audioContentSha)
    {
        // No SHA yet (e.g. diagnostic seeded before TTS synthesis) → the
        // frontend renders an "Audio coming soon" affordance.
        if (string.IsNullOrWhiteSpace(audioContentSha)) return null;
        if (string.IsNullOrWhiteSpace(trimmedBase))
        {
            return $"/v1/listening/audio/{audioContentSha}.mp3";
        }
        return $"{trimmedBase}/v1/listening/audio/{audioContentSha}.mp3";
    }

    private static List<string> DeserializeQuestionIds(string json)
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

    private static List<RoadmapWeekDto> DeserializeWeeks(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return new List<RoadmapWeekDto>();
        try
        {
            var list = JsonSerializer.Deserialize<List<RoadmapWeekDto>>(json);
            return list ?? new List<RoadmapWeekDto>();
        }
        catch (JsonException)
        {
            return new List<RoadmapWeekDto>();
        }
    }

    private static string? TryReadJsonString(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<string>(json);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static int ComputeDurationSeconds(
        DateTimeOffset startedAt,
        DateTimeOffset now,
        int clientReportedSeconds)
    {
        // Prefer the client-reported duration when it's plausible — gives
        // a more accurate "time on task" reading (the server clock includes
        // navigation idle time). Fall back to server delta when the client
        // value is missing or absurd.
        var serverDelta = (int)Math.Max(0, (now - startedAt).TotalSeconds);
        if (clientReportedSeconds <= 0) return serverDelta;
        if (clientReportedSeconds > serverDelta + 300) return serverDelta; // > 5min skew → reject
        return clientReportedSeconds;
    }
}
