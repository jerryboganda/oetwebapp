using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai.TypeSafe;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Phase 2 (D.1) of the OET Speaking module roadmap.
///
/// Synchronously scores a finished <see cref="SpeakingSession"/> via the
/// rulebook-grounded <see cref="IAiGatewayService"/>. The output is an
/// advisory <see cref="SpeakingAiAssessment"/> row — the canonical
/// scaled score is ALWAYS recomputed via
/// <see cref="OetScoring.SpeakingReportedScaled(OetScoring.SpeakingCriterionScores)"/>
/// rather than trusting the AI's own number. Per-criterion scores are
/// clamped to the OET rubric (linguistic 0–6, clinical 0–3).
///
/// Evidence quotes from the AI are best-effort verified against the
/// transcript so the per-criterion drawer can render highlighted
/// segments without trusting the AI to fabricate substrings.
///
/// Optional Jev (TypeSafe) layer, default off and fail-soft: a readiness check
/// runs before the grade chain call and a criterion cross-check after the grade
/// is parsed and scaled. Both only record an advisory, may lower
/// <see cref="SpeakingAiAssessment.ConfidenceBand"/> to <c>low</c> and flag tutor
/// review; no score, band or scaled score is ever changed. Skipped for mocks;
/// <c>judgments</c> null (e.g. the corpus harness) means no Jev call at all.
/// </summary>
public sealed partial class SpeakingAiAssessmentService(
    LearnerDbContext db,
    IAiGatewayService aiGateway,
    ILogger<SpeakingAiAssessmentService> logger,
    SpeakingSimulationV11EvidenceCaptureService? v11EvidenceCapture = null,
    Microsoft.Extensions.Options.IOptions<OetLearner.Api.Configuration.SpeakingGradingOptions>? gradingOptions = null,
    ISpeakingAudioEvidenceService? audioEvidence = null,
    ITypeSafeJudgmentService? judgments = null,
    Microsoft.Extensions.Options.IOptions<OetLearner.Api.Configuration.TypeSafeOptions>? typeSafeOptions = null,
    // AI Pipeline Control Center: owner-saved provider order for grading and the reviewer. Optional LAST parameter.
    OetLearner.Api.Services.AiPipeline.IAiPipelineStore? pipelineStore = null,
    // Subscription-account rotation (owner directive 2026-10-10): permutes the hops of one engine group
    // per run by remaining quota. Optional LAST parameters, absent = the saved order runs as saved.
    OetLearner.Api.Services.AiPipeline.ISubscriptionAccountPool? accountPool = null,
    OetLearner.Api.Services.AiPipeline.ISubscriptionAccountStateProvider? accountState = null,
    // Promotional-credit protection (owner directive 2026-10-09): demotes/drops the paid API hop when the
    // credit grant runs low. Optional LAST parameter, absent = the plan runs exactly as resolved.
    OetLearner.Api.Services.AiPipeline.IAiCreditGuard? creditGuard = null)
{
    // v3 (4 Oct 2026): the system prompt now carries the official OET band descriptors and the
    // "rules guide, never deduct" principles; the model is no longer asked for a readiness band
    // (the server derives it, with the score and grade, from the nine criterion scores).
    // v4 (8 Oct 2026, owner guardrails): no native-speaker standard (a noticeable first-language accent is never a penalty in
    // itself and never a Grammar/Appropriateness problem), the transcript is machine speech recognition, one stumble is one
    // Fluency event (counted once), and a transcript-only Intelligibility never drops for accent or a mis-recognised word. A
    // different prompt is a different grader: the version starts uncalibrated again.
    internal const string PromptTemplateId = "speaking.score.v4";

    /// <summary>Version of the audio stage that fed Intelligibility; "audio-none" = transcript only.</summary>
    internal const string AudioStageVersion = "audio-none";

    /// <summary>
    /// The exact grader behind a score: prompt template | raw→reported mapping | audio stage. A score is
    /// provisional until THIS string (with the grading model) has passed calibration, so changing any of the
    /// three starts an uncalibrated version.
    /// </summary>
    internal static string GraderVersion
        => $"{PromptTemplateId}|{OetScoring.SpeakingMappingVersion}|{AudioStageVersion}";

    /// <summary>The grader version of a grade whose Intelligibility was judged from audio by <paramref name="model"/>:
    /// a different audio model is a different grader and must be calibrated on its own.</summary>
    internal static string GraderVersionWithAudio(string? model)
        => $"{PromptTemplateId}|{OetScoring.SpeakingMappingVersion}|{SpeakingAudioEvidenceService.StageVersion(model)}";

    private const string ProviderName = "ai_gateway";
    private const string ModelId = "gateway-default";

    // ---------------------------------------------------------------------
    // Prompt template — appended to the rulebook-grounded system prompt
    // as the user/task content. The gateway itself supplies the rulebook
    // header so this template focuses on the JSON contract the AI must
    // return for the speaking-grade feature.
    // ---------------------------------------------------------------------
    private const string PROMPT_TEMPLATE_V4 = """
You are an OET Speaking examiner scoring a single role-play session against the official
OET band descriptors given in the system prompt.
Return ONLY a strict JSON object with this exact shape (no markdown, no
prose, no code fences):

{
  "criterionScores": {
    "intelligibility":      { "score": 0, "rationale": "", "evidenceQuotes": [] },
    "fluency":              { "score": 0, "rationale": "", "evidenceQuotes": [] },
    "appropriateness":      { "score": 0, "rationale": "", "evidenceQuotes": [] },
    "grammarExpression":    { "score": 0, "rationale": "", "evidenceQuotes": [] },
    "relationshipBuilding": { "score": 0, "rationale": "", "evidenceQuotes": [] },
    "patientPerspective":   { "score": 0, "rationale": "", "evidenceQuotes": [] },
    "structure":            { "score": 0, "rationale": "", "evidenceQuotes": [] },
    "informationGathering": { "score": 0, "rationale": "", "evidenceQuotes": [] },
    "informationGiving":    { "score": 0, "rationale": "", "evidenceQuotes": [] }
  },
  "overallSummary": "",
  "confidenceBand": "low|medium|high",
  "strengths": [
    { "criterion": "relationshipBuilding", "text": "", "quote": "" }
  ],
  "priorityWeaknesses": [
    { "criterion": "patientPerspective", "text": "", "quote": "", "action": "" }
  ],
  "drills": [
    { "title": "", "criterion": "patientPerspective", "weakPoint": "", "practise": "", "example": "" }
  ]
}

Coaching rules (the candidate reads all of this — plain language, no rule IDs):
  * `strengths`: 2 to 4 specific things the candidate did well, each tied to a criterion code and, where
    possible, a verbatim `quote` of 3–10 words from the transcript.
  * `priorityWeaknesses`: 2 to 5 issues that most affected the score, most important first. `text` says what
    happened and why it mattered; `quote` is the candidate's own words where possible; `action` is ONE
    concrete thing to do differently next attempt. Every weakness must end with an `action`.
  * `drills`: 2 to 5 practice drills built from THIS attempt. `weakPoint` is what the candidate did,
    `practise` is what to rehearse, `example` is a short example phrase or question they could use.
  * Use only the criterion codes from the scoring rubric. Do not invent evidence: if there is no quote, leave
    `quote` empty.

Scoring rules:
  * Linguistic criteria (intelligibility, fluency, appropriateness,
    grammarExpression) use the OET 0–6 band scale.
  * Clinical communication criteria (relationshipBuilding,
    patientPerspective, structure, informationGathering,
    informationGiving) use the OET 0–3 band scale.
  * For each criterion pick the band whose descriptor best fits the WHOLE
    performance. One event lowers at most one criterion.
  * Each `evidenceQuotes` entry MUST be a verbatim substring of the
    candidate's transcript turns. Quote 3–10 words.
  * `rationale` must explain WHY the score was awarded, in plain language the
    candidate can read, citing what the band descriptor expects. Never write
    rule IDs or internal codes in it.
  * `overallSummary` is 2–4 sentences of advisory feedback. Never claim
    this is an official OET score, and never state a score out of 500 or a
    grade: the server derives both from your nine criterion scores.
  * Anything said before the role-play begins, or about the connection or
    equipment, is not part of the performance — ignore it.
  * Do not use a native-speaker standard. The candidate is an international
    healthcare professional: a noticeable first-language accent is not a
    penalty in itself, and it is never a Grammar or Appropriateness problem.
  * The transcript is automatic speech recognition. An odd, misspelt or
    ungrammatical word may be a recognition error caused by accent or audio
    quality, not the candidate's wording.
  * Candidate transcript segments marked `"interrupted": true` mean the
    candidate started speaking BEFORE the patient had finished their
    response (they cut the patient off). Weigh repeated or abrupt
    interruptions under `relationshipBuilding` (use `appropriateness`
    instead only when the problem is the wording of what was said,
    never both for the same interruption) —
    judge severity in context (a single empathetic clarifying
    interjection differs from persistently talking over the patient);
    never apply a mechanical per-interruption deduction. When
    interruptions occurred, include an `improvements` entry in the
    spirit of: "You interrupted the patient before they completed their
    response. In OET, you should allow the patient to finish before
    speaking."
""";

    public async Task<SpeakingAiAssessmentProjection> RunAssessmentAsync(
        string sessionId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw ApiException.Validation("SPEAKING_SESSION_ID_REQUIRED",
                "Speaking session id is required.");
        }

        // ── Load session + card + interlocutor + latest transcript ──
        var session = await db.SpeakingSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw ApiException.NotFound("speaking_session_not_found",
                "That Speaking session does not exist.");

        // Live-tutor (non-mock) stays human-marked. W8: mock Speaking — even
        // when historically forced into LiveTutor mode — is AI-graded on the
        // already-consumed Mock Attempt. Human review is an optional
        // escalation, never a release dependency.
        var isMock = !string.IsNullOrWhiteSpace(session.MockSetId)
            || !string.IsNullOrWhiteSpace(session.MockSessionId);
        if (session.Mode == SpeakingSessionMode.LiveTutor && !isMock)
        {
            logger.LogInformation(
                "Speaking session {SessionId} is live-tutor — AI assessment skipped; routed to human examiner marking.",
                sessionId);
            return new SpeakingAiAssessmentProjection(
                AssessmentId: string.Empty,
                Provider: "human_examiner",
                ModelId: string.Empty,
                PromptTemplateId: string.Empty,
                CriterionScores: new Dictionary<string, CriterionScore>(),
                EstimatedScaledScore: 0,
                ReadinessBand: "awaiting_human_review",
                OverallSummary: "Live-tutor Speaking is marked by a human examiner. Your result is released after marking.",
                ConfidenceBand: "pending",
                GeneratedAt: DateTimeOffset.UtcNow,
                IsAdvisory: false);
        }

        var card = await db.RolePlayCards.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == session.RolePlayCardId, ct)
            ?? throw ApiException.NotFound("role_play_card_not_found",
                "That role-play card does not exist.");

        var script = await db.InterlocutorScripts.AsNoTracking()
            .FirstOrDefaultAsync(s => s.RolePlayCardId == card.Id, ct);

        // Hidden card type — fed to the scorer as marking guidance, never to
        // the learner. Null when the card is untyped.
        SpeakingCardType? cardTypeRow = null;
        if (!string.IsNullOrWhiteSpace(card.CardTypeId))
        {
            cardTypeRow = await db.SpeakingCardTypes.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == card.CardTypeId, ct);
        }

        var transcript = await db.SpeakingTranscripts.AsNoTracking()
            .Where(t => t.SpeakingSessionId == sessionId && t.IsLatest)
            .OrderByDescending(t => t.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        if (v11EvidenceCapture is not null)
        {
            await v11EvidenceCapture.CaptureAsync(sessionId, ct);
        }

        if (transcript is null)
        {
            throw ApiException.Conflict("speaking_session_no_transcript",
                "An AI assessment requires a transcript. Wait for transcription to complete and try again.");
        }

        var transcriptHash = SpeakingCanonicalAssessmentService.HashTranscript(transcript.SegmentsJson);
        var identityHash = SpeakingCanonicalAssessmentService.HashIdentity(
            sessionId,
            session.RolePlayCardId,
            transcriptHash,
            session.RulebookVersion ?? string.Empty,
            PromptTemplateId);
        var reused = await db.SpeakingAiAssessments.AsNoTracking()
            .Where(a => a.IdentityHash == identityHash)
            .OrderBy(a => a.GeneratedAt)
            .FirstOrDefaultAsync(ct);
        if (reused is not null)
        {
            return ProjectAssessment(reused, RehydrateCriterionScores(reused));
        }

        // ── Build grounded prompt via the canonical gateway ──
        var profession = ParseProfession(card.ProfessionId);
        AiGroundedPrompt prompt;
        try
        {
            prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Speaking,
                Profession = profession,
                Task = AiTaskMode.Score,
                CardType = RulebookCardToken(card),
            });
        }
        catch (PromptNotGroundedException)
        {
            throw;
        }

        // Free Mocks: is this session a use of the learner's free sample?
        // Derived server-side from the bound use — the session itself (shared
        // engine) or its legacy recorder attempt (attempt→session bridge) —
        // never from the request, so only that grading call skips the
        // plan/token gate (kill switches still win in AiQuotaService).
        var freeSample = await FreeSamples.FreeSampleService.IsFreeSpeakingSessionAsync(db, session, ct);
        // Free sample OR paid with AI credits: both skip the plan feature gate / token counters (kill switches
        // still win). The audio stage below uses the same grant, so a funded session is never refused there.
        var gradeGrant = freeSample || await SpeakingCreditSettlement.IsCreditFundedAsync(db, session, ct);
        // ONLY a genuine mock (curated Mock Set / full mock bundle: MockSetId/MockSessionId set) is Mock context;
        // a plain ExamSessionId does NOT make a session a mock (every two-card exam card has one).
        var assessmentContext =
            (!string.IsNullOrWhiteSpace(session.MockSetId) || !string.IsNullOrWhiteSpace(session.MockSessionId))
                ? AiAssessmentContext.Mock
                : AiAssessmentContext.Practice;

        // ── Acoustic evidence (admin-flagged, fail-soft) ──
        // null = the audio stage did not run for this grade (flag off, or no service): grading is exactly as
        // before. When it runs it never throws for an audio problem: the result says "unavailable" and why.
        SpeakingAudioEvidence? audio = null;
        if (audioEvidence is not null && await audioEvidence.IsEnabledAsync(ct))
        {
            audio = await audioEvidence.AssessAsync(new SpeakingAudioAssessRequest(
                sessionId,
                session.UserId,
                card.ProfessionId,
                RulebookCardToken(card),
                SpeakingTranscriptEvidence.StripConnectivityChatter(transcript.SegmentsJson),
                gradeGrant,
                assessmentContext), ct);
        }

        var userInput = BuildUserInput(card, script, transcript, cardTypeRow, audio);

        // ── Jev readiness (advisory, flag-gated, fail-soft, <= 3 s) ──
        // Strictly BEFORE the grade chain and sequential with it (the scoped DbContext is not
        // thread-safe). It never skips, delays beyond its time box or reroutes the pinned grade;
        // a flag only records an advisory and flags tutor review. Mocks are skipped (live-tutor
        // sessions returned above), and `judgments` null (corpus harness) means zero Jev calls.
        var jevOptions = isMock ? null : typeSafeOptions?.Value;
        var jevActive = judgments is not null && JevSpeakingAdvisor.AnyActive(jevOptions);
        var jevTranscript = jevActive
            ? JevSpeakingAdvisor.TranscriptFromSegmentsJson(SpeakingTranscriptEvidence.StripConnectivityChatter(transcript.SegmentsJson))
            : string.Empty;
        var jevCard = jevActive
            ? JevSpeakingAdvisor.CardSummary(card.ScenarioTitle, card.Setting, card.CandidateRole, card.ClinicalTopic, card.Tasks)
            : string.Empty;
        SpeakingReadinessAdvisory? jevReadiness = null;
        if (jevActive)
        {
            jevReadiness = await JevSpeakingAdvisor.CheckReadinessAsync(
                judgments!, jevOptions!, jevTranscript, jevCard, session.UserId, sessionId, ct, logger: logger);
        }

        // ── Grade (the shared core: the combined Full Mock judgement uses the same one) ──
        // SpeakingGradeChain pins the Claude subscription sidecar first and falls back to the default route;
        // with no pinned provider configured it is one plain gateway call.
        var outcome = await GradeCoreAsync(new SpeakingGradeInput(
            LogKey: sessionId,
            UserId: session.UserId,
            Prompt: prompt,
            TemplateId: PromptTemplateId,
            UserInput: userInput,
            TranscriptText: ExtractTranscriptText(SpeakingTranscriptEvidence.StripConnectivityChatter(transcript.SegmentsJson)),
            FreeSampleGrant: gradeGrant,
            Context: assessmentContext,
            Audio: audio), ct);
        var rubricScores = outcome.Scores;
        var scaled = outcome.ReportedScaled;
        var readinessBand = outcome.ReadinessBand;
        var confidenceBand = outcome.ConfidenceBand;

        // ── Jev cross-check (advisory, flag-gated, fail-soft, <= 3 s) ──
        // After the grade is parsed and scaled, never inside the grade chain. It can only lower the
        // stored ConfidenceBand to "low" (the existing tutor-review-recommended value); every score,
        // band and the scaled score above are final and untouched. Text-only: the audio-bound
        // criteria (intelligibility, fluency) are not part of the Jev questions.
        SpeakingCrosscheckAdvisory? jevCrosscheck = null;
        if (jevActive)
        {
            jevCrosscheck = await JevSpeakingAdvisor.CrosscheckAsync(
                judgments!, jevOptions!, SpeakingCrosscheckSchema.Classic, jevTranscript, jevCard,
                outcome.CriterionScores
                    .Select(kv => new SpeakingCrosscheckCriterion(
                        kv.Key,
                        Math.Clamp(kv.Value.Score, 0, IsLinguisticCriterion(kv.Key) ? 6 : 3),
                        kv.Value.Rationale,
                        kv.Value.EvidenceQuotes))
                    .ToList(),
                session.UserId, sessionId, ct, logger: logger);
            if (jevCrosscheck?.RequiresReview == true) confidenceBand = "low";
        }

        // ── Persist ──
        var now = DateTimeOffset.UtcNow;
        var assessmentId = $"spa_{Guid.NewGuid():N}";

        // The criterion rationales, the acoustic evidence and the coaching report ride in one JSON object (built by the
        // grader core); the Jev advisory lives beside them (RulebookFindingsJson is a List<string> read by analytics, so
        // it cannot hold it). ReadRationales and the projection only look up the nine criterion codes, so the extra
        // entries are inert.
        var rationalesPayload = outcome.RationalesPayload;
        var jevPayload = JevSpeakingAdvisor.AdvisoryPayload(jevReadiness, jevCrosscheck);
        if (jevPayload is not null) rationalesPayload[JevSpeakingAdvisor.AdvisoryKey] = jevPayload;

        var row = new SpeakingAiAssessment
        {
            Id = assessmentId,
            SpeakingSessionId = sessionId,
            TranscriptId = transcript.Id,
            // Real provenance (which grader actually ran), cut to the column limits (32 / 96).
            Provider = outcome.Provider,
            ModelId = outcome.ModelId,
            PromptTemplateId = PromptTemplateId,
            GraderVersion = outcome.GraderVersion,
            Intelligibility = rubricScores.Intelligibility,
            Fluency = rubricScores.Fluency,
            Appropriateness = rubricScores.Appropriateness,
            GrammarExpression = rubricScores.GrammarExpression,
            RelationshipBuilding = rubricScores.RelationshipBuilding,
            PatientPerspective = rubricScores.PatientPerspective,
            Structure = rubricScores.Structure,
            InformationGathering = rubricScores.InformationGathering,
            InformationGiving = rubricScores.InformationGiving,
            EstimatedScaledScore = scaled,
            ReadinessBand = readinessBand,
            PerCriterionRationalesJson = JsonSerializer.Serialize(rationalesPayload),
            OverallSummary = outcome.OverallSummary ?? string.Empty,
            ConfidenceBand = confidenceBand,
            GeneratedAt = now,
            RulebookFindingsJson = "[]",
            IsAdvisory = session.Mode != SpeakingSessionMode.AiExam,
            IdentityHash = identityHash,
            TranscriptHash = transcriptHash,
            RubricVersion = session.RulebookVersion,
            CardId = session.RolePlayCardId,
            ClaimedAt = now,
            ClaimOwner = Environment.MachineName,
        };

        var existingCanonical = await db.SpeakingAiAssessments
            .FirstOrDefaultAsync(a => a.IdentityHash == identityHash, ct);
        if (existingCanonical is not null)
            return ProjectAssessment(existingCanonical, RehydrateCriterionScores(existingCanonical));

        db.SpeakingAiAssessments.Add(row);

        // Tutor review hand-off: the review queue itself is derived (finished non-AI-exam sessions
        // without a final tutor assessment), so a flag adds no queue row. The audit event is the
        // traceable record, saved atomically with the assessment.
        AuditEvent? jevReviewAudit = null;
        if (jevPayload is not null && (jevReadiness?.RequiresReview == true || jevCrosscheck?.RequiresReview == true))
        {
            jevReviewAudit = JevSpeakingAdvisor.ReviewEvent(
                JevSpeakingAdvisor.ReviewFlaggedAction, "SpeakingSession", sessionId, now, jevPayload);
            db.AuditEvents.Add(jevReviewAudit);
            logger.LogInformation(
                "Jev flagged Speaking session {SessionId} for tutor review (readiness={Readiness}, crosscheck={Crosscheck}).",
                sessionId, jevReadiness?.RequiresReview == true, jevCrosscheck?.RequiresReview == true);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.Entry(row).State = EntityState.Detached;
            if (jevReviewAudit is not null) db.Entry(jevReviewAudit).State = EntityState.Detached;
            var raced = await db.SpeakingAiAssessments.AsNoTracking()
                .FirstOrDefaultAsync(a => a.IdentityHash == identityHash, ct);
            if (raced is not null)
            {
                return ProjectAssessment(raced, RehydrateCriterionScores(raced));
            }

            throw;
        }

        // Project from the stored row so the response a learner gets straight after grading is exactly what
        // every later read shows (clamped scores, internal rule IDs scrubbed from the text).
        return ProjectAssessment(row, RehydrateCriterionScores(row));
    }

    // ─────────────────────────────────────────────────────────────────
    // Grader core: one performance in, one graded outcome out, nothing persisted
    // ─────────────────────────────────────────────────────────────────

    /// <summary>Everything the grader needs to grade ONE performance: a single card, or a whole two-card test.</summary>
    /// <param name="LogKey">The session or exam id, for log lines.</param>
    /// <param name="UserId">The learner; null for a harness run (no learner, no plan gate).</param>
    /// <param name="Prompt">The grounded system prompt, built first so an ungrounded prompt fails before any audio spend.</param>
    /// <param name="TemplateId">The prompt template id: the card template, or its combined variant.</param>
    /// <param name="UserInput">The full user message: template, card(s), transcript(s) and acoustic evidence.</param>
    /// <param name="TranscriptText">The transcript text the grader's verbatim quotes are checked against.</param>
    /// <param name="Audio">The acoustic evidence the grader was given; null = the audio stage did not run.</param>
    internal sealed record SpeakingGradeInput(
        string LogKey,
        string? UserId,
        AiGroundedPrompt Prompt,
        string TemplateId,
        string UserInput,
        string TranscriptText,
        bool FreeSampleGrant,
        AiAssessmentContext Context,
        SpeakingAudioEvidence? Audio);

    /// <summary>A graded performance, ready to persist or to report. <c>RationalesPayload</c> is the JSON object a
    /// card row stores in <c>PerCriterionRationalesJson</c>: the criterion rationales, the acoustic evidence and the
    /// coaching report.</summary>
    internal sealed record SpeakingGradeOutcome(
        IReadOnlyDictionary<string, CriterionScore> CriterionScores,
        string? OverallSummary,
        string ConfidenceBand,
        SpeakingFeedbackReport Report,
        OetScoring.SpeakingCriterionScores Scores,
        int ReportedScaled,
        string ReadinessBand,
        string Provider,
        string ModelId,
        string GraderVersion,
        Dictionary<string, object?> RationalesPayload,
        /// <summary>What the secondary review did (Claude's scores before it, the reviewer's, every change); null only for outcomes built by older callers.</summary>
        SpeakingReviewTrace? Review = null,
        /// <summary>The audio evidence of each card behind a combined (Full Mock) outcome, set by the calibration harness; null otherwise.</summary>
        IReadOnlyList<SpeakingAudioEvidence?>? CardAudio = null);

    internal async Task<SpeakingGradeOutcome> GradeCoreAsync(SpeakingGradeInput input, CancellationToken ct)
    {
        var audio = input.Audio;

        // ── Invoke gateway (mirror SpeakingEvaluationPipeline pattern) ──
        AiGatewayResult aiResult;
        AiGatewayRequest gradeRequest;
        try
        {
            gradeRequest = new AiGatewayRequest
            {
                Prompt = input.Prompt,
                UserInput = input.UserInput,
                Model = string.Empty,
                Temperature = 0.1,
                MaxTokens = 4096,
                FeatureCode = AiFeatureCodes.SpeakingGrade,
                FreeSampleGrant = input.FreeSampleGrant,
                UserId = input.UserId,
                PromptTemplateId = input.TemplateId,
                // Tag the assessment context for the audit trail + gateway backstop. ONLY a genuine mock (curated Mock
                // Set / full mock bundle: MockSetId/MockSessionId set) is Mock context; a plain ExamSessionId does NOT
                // make a session a mock (every two-card exam card has one), so random AI exams are Practice and keep
                // AI marking. Mock Speaking never reaches here (it is human-marked above) — this tag just arms the
                // gateway's mock_assessment_forbidden backstop if a future caller ever bypasses the guard.
                AssessmentContext = input.Context,
            };
            aiResult = await SpeakingGradeChain.CompleteAsync(
                aiGateway, gradeRequest, gradingOptions?.Value, pipelineStore, logger, ct, accountPool, accountState, creditGuard);
        }
        catch (PromptNotGroundedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Speaking AI assessment failed for {LogKey}; surfacing retryable error.",
                input.LogKey);
            // Fail loud so the caller can retry. The free-tier counter is
            // not consumed because the AI gateway records the failure as
            // AiUsageRecord.Outcome=ProviderError (mirrors Q3 fail-loud in
            // SpeakingEvaluationPipeline). DO NOT swallow.
            throw ApiException.Conflict("speaking_ai_unavailable",
                "We couldn't reach the AI scoring service. Please retry shortly — your free-tier counter has not been consumed.");
        }

        // ── Parse, clamp, and validate evidence quotes ──
        // GPT-6.1 Sol reviews Claude's grade (bounded +-1 band per criterion; any failure keeps Claude's grade).
        // Only when the primary grade is itself readable, so an unparseable Claude reply still fails loud below.
        string completion;
        SpeakingReviewTrace review;
        // Owner-saved reviewer stage (admin Pipeline page): its own switch, steps and order. Absent store = legacy behaviour.
        var reviewPlan = pipelineStore is null ? null : await pipelineStore.ResolvePlanAsync(OetLearner.Api.Services.AiPipeline.AiPipelineStageKeys.SpeakingReview, ct);
        if (reviewPlan is not null && accountPool is not null && accountState is not null)
        {
            reviewPlan = await accountPool.OrderAsync(accountState, reviewPlan, ct);
        }
        if (ParseAssessment(aiResult.Completion) is null || reviewPlan is { StageEnabled: false } or { Hops.Count: 0 })
        {
            completion = aiResult.Completion;
            review = SpeakingReviewTrace.Skipped(SpeakingGradeReviewer.ScoresOf(aiResult.Completion));
        }
        else
        {
            var reviewed = await SpeakingGradeReviewer.ReviewAsync(aiGateway, gradeRequest, aiResult, logger, ct, input.LogKey, plan: reviewPlan);
            completion = reviewed.Completion;
            review = reviewed.Trace;
        }

        var parsed = ParseAssessment(completion);
        if (parsed is null)
        {
            logger.LogWarning(
                "Speaking AI assessment returned an unparseable or incomplete payload for {LogKey}.",
                input.LogKey);
            throw ApiException.Conflict("speaking_ai_unparseable",
                "The AI scoring service returned an invalid response. Please retry.");
        }

        foreach (var (code, criterion) in parsed.CriterionScores)
        {
            foreach (var quote in criterion.EvidenceQuotes)
            {
                if (!ContainsNormalised(input.TranscriptText, quote))
                {
                    logger.LogWarning(
                        "Speaking AI assessment quote not found in transcript for {LogKey} criterion {Criterion}: {Quote}",
                        input.LogKey, code, quote);
                }
            }
        }

        // The audio judge's verified Intelligibility replaces the grader's text-only estimate: it was reached
        // from the sound of the recording (and only that), against the same official band descriptors.
        if (audio is { IsAudio: true, IntelligibilityScore: { } audioIntelligibility })
        {
            parsed.CriterionScores["intelligibility"] = new CriterionScore(
                Math.Clamp(audioIntelligibility, 0, 6),
                6,
                audio.IntelligibilityRationale ?? "Judged from the sound of your recording.",
                Array.Empty<string>());
        }

        // ── Canonical scaled score: ALWAYS recomputed via OetScoring ──
        var rubricScores = new OetScoring.SpeakingCriterionScores(
            Intelligibility:      ScoreOf(parsed, "intelligibility",      0, 6),
            Fluency:              ScoreOf(parsed, "fluency",              0, 6),
            Appropriateness:      ScoreOf(parsed, "appropriateness",      0, 6),
            GrammarExpression:    ScoreOf(parsed, "grammarExpression",    0, 6),
            RelationshipBuilding: ScoreOf(parsed, "relationshipBuilding", 0, 3),
            PatientPerspective:   ScoreOf(parsed, "patientPerspective",   0, 3),
            Structure:            ScoreOf(parsed, "structure",            0, 3),
            InformationGathering: ScoreOf(parsed, "informationGathering", 0, 3),
            InformationGiving:    ScoreOf(parsed, "informationGiving",    0, 3));

        // The REPORTED score (0–500, multiple of 10): the one number the grade, readiness band
        // and pass line all derive from, so a score shown as 350 can never read "Borderline".
        var scaled = OetScoring.SpeakingReportedScaled(rubricScores);
        var readinessBand = OetScoring.SpeakingReadinessBandCode(
            OetScoring.SpeakingReadinessBandFromScaled(scaled));
        var confidenceBand = NormaliseConfidenceBand(parsed.ConfidenceBand);
        // Owner decision 4 Oct 2026: when the audio stage ran but there was no usable audio, the grade is still
        // given, Intelligibility is labelled as estimated from the transcript, and confidence is low.
        if (audio is not null && (!audio.IsAudio || audio.Confidence == "low")) confidenceBand = "low";

        var rationalesPayload = new Dictionary<string, object?>();
        foreach (var (code, criterion) in parsed.CriterionScores)
        {
            rationalesPayload[code] = new
            {
                rationale = criterion.Rationale,
                evidenceQuotes = criterion.EvidenceQuotes,
            };
        }

        // What the audio judge heard, or why there was no audio evidence, lives beside the rationales under a
        // reserved key (no migration); nothing that looks up a criterion code sees it.
        if (audio is not null) rationalesPayload[AcousticKey] = JsonSerializer.SerializeToElement(AcousticPayload(audio), ReportJson);

        // What the secondary review did (Claude's own scores before it, the reviewer's, every change): scores only, never shown
        // to a candidate, so a grade can be explained after the fact without database archaeology.
        rationalesPayload[ReviewKey] = JsonSerializer.SerializeToElement(new
        {
            status = review.Status,
            model = review.Model,
            // Which route ran the review and, when the shared reviewer pipeline left Codex, why.
            provider = review.Provider,
            fallbackReason = review.FallbackReason,
            primaryScores = review.PrimaryScores,
            reviewerScores = review.ReviewerScores,
            changes = review.Changes,
            // The audio judge's Intelligibility replaced the grader's (and the reviewer's) afterwards.
            intelligibilityFromAudio = audio is { IsAudio: true },
        }, ReportJson);

        // The coaching report (strengths, priority weaknesses, drills) rides in the same JSON under a
        // reserved key, exactly as stored here; it is scrubbed of internal IDs only when a candidate reads it.
        var report = parsed.Report;
        if (report.Strengths.Count > 0 || report.PriorityWeaknesses.Count > 0 || report.Drills.Count > 0)
        {
            rationalesPayload[ReportKey] = JsonSerializer.SerializeToElement(
                new { version = 1, strengths = report.Strengths, priorityWeaknesses = report.PriorityWeaknesses, drills = report.Drills },
                ReportJson);
        }

        return new SpeakingGradeOutcome(
            parsed.CriterionScores,
            parsed.OverallSummary,
            confidenceBand,
            report,
            rubricScores,
            scaled,
            readinessBand,
            // Real provenance (which grader actually ran), cut to the column limits (32 / 96).
            Truncate(string.IsNullOrWhiteSpace(aiResult.ResolvedProvider) ? ProviderName : aiResult.ResolvedProvider.Trim(), 32),
            Truncate(string.IsNullOrWhiteSpace(aiResult.ResolvedModel) ? ModelId : aiResult.ResolvedModel.Trim(), 96),
            GraderVersionFor(input.TemplateId, audio),
            rationalesPayload,
            review);
    }

    /// <summary><c>{template}|{mapping}|{audio stage}</c> for any prompt template (the card grader or its combined variant).</summary>
    private static string GraderVersionFor(string templateId, SpeakingAudioEvidence? audio)
        => $"{templateId}|{OetScoring.SpeakingMappingVersion}|{(audio is { IsAudio: true } ? SpeakingAudioEvidenceService.StageVersion(audio.Model) : AudioStageVersion)}";

    /// <summary>
    /// The rulebook's card-scoped rule token for a card. The Speaking rulebooks scope some rules to
    /// <c>breaking_bad_news</c>, <c>follow_up</c> and <c>already_known_patient</c> cards; the grader
    /// always passed the generic <c>role_play</c> token, so those rules never reached the cards they
    /// describe. Everything else keeps <c>role_play</c> (the rules that apply to every card).
    /// </summary>
    internal static string RulebookCardToken(RolePlayCard card)
    {
        static bool Is(string? value, string expected)
            => string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

        if (Is(card.PrimaryCategory, "Breaking Bad News") || HasSecondaryTag(card.SecondaryTagsJson, "Breaking Bad News"))
            return "breaking_bad_news";
        if (Is(card.PrimaryCategory, "Second Visit / Follow-up")) return "follow_up";
        if (Is(card.PrimaryCategory, "Already Known Patient")) return "already_known_patient";
        return "role_play";
    }

    private static bool HasSecondaryTag(string? secondaryTagsJson, string tag)
    {
        if (string.IsNullOrWhiteSpace(secondaryTagsJson)) return false;
        try
        {
            var tags = JsonSerializer.Deserialize<string[]>(secondaryTagsJson);
            return tags is not null
                && tags.Any(t => string.Equals(t?.Trim(), tag, StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task<SpeakingAiAssessmentProjection?> GetLatestAsync(
        string sessionId,
        CancellationToken ct)
    {
        var row = await db.SpeakingAiAssessments.AsNoTracking()
            .Where(a => a.SpeakingSessionId == sessionId)
            .OrderByDescending(a => a.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;
        return ProjectAssessment(row, RehydrateCriterionScores(row));
    }

    // ─────────────────────────────────────────────────────────────────
    // Projection
    // ─────────────────────────────────────────────────────────────────

    private static SpeakingAiAssessmentProjection ProjectAssessment(
        SpeakingAiAssessment row,
        IDictionary<string, CriterionScore> criterionScores)
    {
        // A legacy row stored the unrounded heuristic number (e.g. 345 → shown as 350) and a band
        // computed on it; the grade and band are always recomputed from the reported value so the
        // three can never disagree.
        var reported = OetScoring.OetReportedScaledScore(row.EstimatedScaledScore);
        return new SpeakingAiAssessmentProjection(
            AssessmentId: row.Id,
            Provider: row.Provider,
            ModelId: row.ModelId,
            PromptTemplateId: row.PromptTemplateId,
            CriterionScores: criterionScores,
            EstimatedScaledScore: reported,
            ReadinessBand: OetScoring.SpeakingReadinessBandCode(OetScoring.SpeakingReadinessBandFromScaled(reported)),
            // Internal rule IDs never reach a candidate (the stored text stays as the model wrote it).
            OverallSummary: SpeakingLearnerText.ScrubRuleIds(row.OverallSummary),
            ConfidenceBand: row.ConfidenceBand,
            GeneratedAt: row.GeneratedAt,
            IsAdvisory: row.IsAdvisory,
            Grade: OetScoring.OetGradeLetterFromScaled(reported),
            // Provisional until this exact grader version (with its model) has passed calibration;
            // a legacy row has no version and is always provisional.
            ScoreLabel: OetScoring.SpeakingScoreLabel(row.GraderVersion, row.ModelId),
            Report: ReadStoredReport(row.PerCriterionRationalesJson),
            IntelligibilityEvidence: ReadIntelligibilityEvidence(row.PerCriterionRationalesJson));
    }

    internal static IDictionary<string, CriterionScore> RehydrateCriterionScores(SpeakingAiAssessment row)
    {
        var rationales = ReadRationales(row.PerCriterionRationalesJson);
        IDictionary<string, CriterionScore> result = new Dictionary<string, CriterionScore>(StringComparer.OrdinalIgnoreCase)
        {
            ["intelligibility"]      = Build(row.Intelligibility,      6, "intelligibility"),
            ["fluency"]              = Build(row.Fluency,              6, "fluency"),
            ["appropriateness"]      = Build(row.Appropriateness,      6, "appropriateness"),
            ["grammarExpression"]    = Build(row.GrammarExpression,    6, "grammarExpression"),
            ["relationshipBuilding"] = Build(row.RelationshipBuilding, 3, "relationshipBuilding"),
            ["patientPerspective"]   = Build(row.PatientPerspective,   3, "patientPerspective"),
            ["structure"]            = Build(row.Structure,            3, "structure"),
            ["informationGathering"] = Build(row.InformationGathering, 3, "informationGathering"),
            ["informationGiving"]    = Build(row.InformationGiving,    3, "informationGiving"),
        };
        return result;

        CriterionScore Build(int score, int max, string code)
        {
            var found = rationales.TryGetValue(code, out var packed);
            return new CriterionScore(
                Score: score,
                MaxScore: max,
                Rationale: found ? SpeakingLearnerText.ScrubRuleIds(packed.Rationale) : string.Empty,
                EvidenceQuotes: found ? (packed.EvidenceQuotes ?? Array.Empty<string>()) : Array.Empty<string>());
        }
    }

    private static Dictionary<string, (string Rationale, string[] EvidenceQuotes)> ReadRationales(string? json)
    {
        var map = new Dictionary<string, (string, string[])>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return map;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return map;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                var rationale = prop.Value.TryGetProperty("rationale", out var r) && r.ValueKind == JsonValueKind.String
                    ? r.GetString() ?? string.Empty
                    : string.Empty;
                var quotes = new List<string>();
                if (prop.Value.TryGetProperty("evidenceQuotes", out var quotesEl)
                    && quotesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var q in quotesEl.EnumerateArray())
                    {
                        if (q.ValueKind == JsonValueKind.String)
                        {
                            var s = q.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) quotes.Add(s!);
                        }
                    }
                }
                map[prop.Name] = (rationale, quotes.ToArray());
            }
        }
        catch
        {
            // Tolerate corrupt JSON — fall back to empty rationales.
        }

        return map;
    }

    // ─────────────────────────────────────────────────────────────────
    // Prompt assembly
    // ─────────────────────────────────────────────────────────────────

    private static string BuildUserInput(
        RolePlayCard card,
        InterlocutorScript? script,
        SpeakingTranscript transcript,
        SpeakingCardType? cardType,
        SpeakingAudioEvidence? audio = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(PROMPT_TEMPLATE_V4);
        sb.AppendLine();
        AppendCardSections(sb, card, script, cardType);
        AppendEvidenceNote(sb, IsLiveVoiceTranscript(transcript), plural: false, audio);
        AppendTranscriptSections(sb, transcript, string.Empty);
        sb.AppendLine("Now produce the strict JSON object specified above.");
        return sb.ToString();
    }

    private static bool IsLiveVoiceTranscript(SpeakingTranscript transcript)
        => transcript.Provider.StartsWith(LiveVoiceService.TranscriptProviderPrefix, StringComparison.Ordinal);

    /// <summary>The hidden card type (marking guidance), the candidate-facing card and the hidden patient script.</summary>
    private static void AppendCardSections(
        StringBuilder sb,
        RolePlayCard card,
        InterlocutorScript? script,
        SpeakingCardType? cardType)
    {
        // Hidden card type — marking guidance only. NEVER shown to the learner.
        if (cardType is not null)
        {
            sb.AppendLine("---- CARD TYPE (hidden marking guidance) ----");
            sb.AppendLine(JsonSerializer.Serialize(new
            {
                name = cardType.Name,
                description = cardType.Description,
            }));
            sb.AppendLine();
        }
        sb.AppendLine("---- ROLE PLAY CARD (candidate-facing) ----");
        sb.AppendLine(JsonSerializer.Serialize(new
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
            tasks = card.Tasks.ToArray(),
            patientEmotion = card.PatientEmotion,
            communicationGoal = card.CommunicationGoal,
            clinicalTopic = card.ClinicalTopic,
        }));
        sb.AppendLine();
        sb.AppendLine("---- INTERLOCUTOR SCRIPT (hidden patient persona) ----");
        sb.AppendLine(script is null
            ? "{}"
            : JsonSerializer.Serialize(new
            {
                patientBackground = script.PatientBackground,
                patientTasks = script.PatientTasks.ToArray(),
                openingResponse = script.OpeningResponse,
                prompts = new[] { script.Prompt1, script.Prompt2, script.Prompt3 }
                    .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToArray(),
                hiddenInformation = script.HiddenInformation,
                resistanceLevel = ResistanceLevels.ToCode(script.ResistanceLevel),
                emotionalState = script.EmotionalState,
                closingCue = script.ClosingCue,
            }));
        sb.AppendLine();
    }

    /// <summary>What the grader may rely on for Intelligibility: the audio judge's findings, or the plain statement
    /// that it has none (and so must say its estimate comes from the transcript).</summary>
    private static void AppendEvidenceNote(StringBuilder sb, bool liveVoiceTranscript, bool plural, SpeakingAudioEvidence? audio)
    {
        if (audio is null)
        {
            // The audio stage did not run. A live voice role-play then has no audio the grader can use, so the
            // feedback must not send the candidate to a recording. Added to the input only: the template, rubric
            // and schema are unchanged.
            if (liveVoiceTranscript)
            {
                sb.AppendLine(plural
                    ? "NOTE: These role-plays were live voice conversations with no audio recording (transcripts only), so the feedback text must never tell the candidate to listen to or check a recording."
                    : "NOTE: This role-play was a live voice conversation with no audio recording (transcript only), so the feedback text must never tell the candidate to listen to or check a recording.");
                sb.AppendLine();
            }
        }
        else if (audio.IsAudio)
        {
            AppendAcousticEvidence(sb, audio);
        }
        else
        {
            // The audio stage ran but there was nothing usable: judge Intelligibility from the transcript, and say so.
            sb.AppendLine($"NOTE: No audio evidence could be used for this attempt ({SpeakingAudioEvidenceService.ReasonText(audio.Reason)}). Estimate Intelligibility from the transcript alone, say in its rationale that no audio evidence was available, and keep its score within what a transcript can support. A transcript cannot show accent or pronunciation: do not lower Intelligibility for accent or for words that merely look mis-recognised; lower it only where the patient asks the candidate to repeat or clarify what they said. The feedback text must never tell the candidate to listen to or check a recording.");
            sb.AppendLine();
        }
    }

    /// <summary>The graded transcript and the server-computed interaction signals. <paramref name="label"/> names the
    /// role-play in a combined test (empty for a single card).</summary>
    private static void AppendTranscriptSections(StringBuilder sb, SpeakingTranscript transcript, string label)
    {
        // The graded evidence starts at the real role-play: the opening connection check ("can you hear
        // me" / "go ahead") is stripped here, while the stored segments and their hash are untouched.
        var gradedSegments = SpeakingTranscriptEvidence.StripConnectivityChatter(transcript.SegmentsJson);
        sb.AppendLine($"---- TRANSCRIPT{label} (latest revision) ----");
        sb.AppendLine(gradedSegments);
        sb.AppendLine();
        // Server-computed so sparse per-segment flags cannot be overlooked
        // in a long transcript.
        var (interruptionCount, interruptedAtMs) = ComputeInteractionSignals(gradedSegments);
        sb.AppendLine($"---- INTERACTION SIGNALS{label} (server-computed) ----");
        sb.AppendLine(JsonSerializer.Serialize(new
        {
            interruptionCount,
            interruptedAtMs,
        }));
        sb.AppendLine();
    }

    /// <summary>Scans the transcript segments for candidate turns flagged
    /// <c>interrupted: true</c> (the candidate barged in while the patient
    /// was still speaking). Malformed JSON yields zero signals — grading
    /// must never fail on transcript-shape drift.</summary>
    internal static (int InterruptionCount, long[] InterruptedAtMs) ComputeInteractionSignals(
        string? segmentsJson)
    {
        if (string.IsNullOrWhiteSpace(segmentsJson)) return (0, Array.Empty<long>());
        try
        {
            using var doc = JsonDocument.Parse(segmentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return (0, Array.Empty<long>());
            var atMs = new List<long>();
            foreach (var s in doc.RootElement.EnumerateArray())
            {
                if (s.ValueKind != JsonValueKind.Object) continue;
                var isCandidate = s.TryGetProperty("speaker", out var sp)
                    && string.Equals(sp.GetString(), "candidate", StringComparison.Ordinal);
                var interrupted = s.TryGetProperty("interrupted", out var ir)
                    && ir.ValueKind == JsonValueKind.True;
                if (!isCandidate || !interrupted) continue;
                atMs.Add(s.TryGetProperty("startMs", out var st) && st.ValueKind == JsonValueKind.Number
                    ? st.GetInt64()
                    : 0L);
            }
            return (atMs.Count, atMs.ToArray());
        }
        catch (JsonException)
        {
            return (0, Array.Empty<long>());
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Response parsing
    // ─────────────────────────────────────────────────────────────────

    private sealed class ParsedAssessment
    {
        public Dictionary<string, CriterionScore> CriterionScores { get; init; } =
            new(StringComparer.OrdinalIgnoreCase);
        public string? OverallSummary { get; init; }
        public string? ConfidenceBand { get; init; }
        public SpeakingFeedbackReport Report { get; init; } = EmptyReport;
    }

    // ─────────────────────────────────────────────────────────────────
    // Coaching report (strengths, priority weaknesses, drills)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>Reserved key beside the nine criterion rationales in <c>PerCriterionRationalesJson</c> (the same
    /// pattern as the Jev advisory); no migration, and nothing that looks up a criterion code sees it.</summary>
    private const string ReportKey = "_report";

    /// <summary>Reserved key (same pattern as <see cref="ReportKey"/>) holding what the audio judge heard, or why
    /// there was no audio evidence. Nothing that looks up a criterion code sees it.</summary>
    private const string AcousticKey = "_acoustic";

    /// <summary>Reserved key (same pattern) holding what the secondary review did to the grade. Never shown to a candidate.</summary>
    private const string ReviewKey = "_review";

    /// <summary>The acoustic evidence as the grader sees it. Authoritative for Intelligibility; the fluency
    /// observations inform, but never replace, the grader's own Fluency judgement.</summary>
    private static void AppendAcousticEvidence(StringBuilder sb, SpeakingAudioEvidence audio)
    {
        sb.AppendLine("---- ACOUSTIC EVIDENCE (from the candidate's audio; authoritative for Intelligibility) ----");
        sb.AppendLine($"Intelligibility judged from the audio: {audio.IntelligibilityScore}/6 (audio quality: {audio.AudioQuality}; confidence: {audio.Confidence}).");
        if (!string.IsNullOrWhiteSpace(audio.IntelligibilityRationale)) sb.AppendLine($"Why: {audio.IntelligibilityRationale}");
        foreach (var observation in audio.Observations)
        {
            sb.AppendLine($"- Heard at about {observation.ApproxSecond}s (clip {observation.Clip}): {observation.Issue}"
                + (string.IsNullOrWhiteSpace(observation.Example) ? string.Empty : $" ({observation.Example})"));
        }

        if (audio.Fluency is { } fluency)
        {
            sb.AppendLine(
                $"Fluency evidence: speech rate {(fluency.SpeechRateWpm is { } wpm ? $"about {wpm} words per minute" : "not measured")}; "
                + $"{fluency.LongPauses} long pause(s); {fluency.HesitationCount} hesitation(s); {fluency.FillerCount} filler(s); {fluency.RestartCount} restart(s).");
            foreach (var note in fluency.Observations) sb.AppendLine($"- {note}");
        }

        sb.AppendLine("The Intelligibility score above is final: report it as your Intelligibility score. Use the fluency evidence to inform your own Fluency judgement.");
        sb.AppendLine("---- END ACOUSTIC EVIDENCE ----");
        sb.AppendLine();
    }

    private static object AcousticPayload(SpeakingAudioEvidence audio)
        => audio.IsAudio
            ? new
            {
                source = "audio",
                model = audio.Model,
                confidence = audio.Confidence,
                audioQuality = audio.AudioQuality,
                patientVoiceBleed = audio.PatientVoiceBleed,
                clips = audio.ClipCount,
                durationMs = audio.DurationMs,
                audioMs = audio.AudioMs,
                speechMs = audio.SpeechMs,
                turns = audio.Turns,
                turnsWithClip = audio.TurnsWithClip,
                coverage = audio.Coverage,
                observations = audio.Observations,
                fluency = audio.Fluency,
            }
            : new
            {
                source = "transcript_only",
                reason = audio.Reason,
                model = (string?)null,
                confidence = audio.Confidence,
                audioQuality = audio.AudioQuality,
                patientVoiceBleed = false,
                // What was stored and what was judged against it, so a thin or missing recording is visible in the grade itself.
                clips = audio.ClipCount,
                durationMs = audio.DurationMs,
                audioMs = audio.AudioMs,
                speechMs = audio.SpeechMs,
                turns = audio.Turns,
                turnsWithClip = audio.TurnsWithClip,
                coverage = audio.Coverage,
                observations = Array.Empty<SpeakingAudioObservation>(),
                fluency = (SpeakingFluencyEvidence?)null,
            };

    /// <summary>
    /// What Intelligibility was judged from, for the candidate. A grade without a stored audio judgement (the stage
    /// off, an older grade, or no usable audio) is always honestly labelled as estimated from the transcript only.
    /// </summary>
    internal static SpeakingIntelligibilityEvidence ReadIntelligibilityEvidence(string? rationalesJson)
    {
        var transcriptOnly = new SpeakingIntelligibilityEvidence("transcript_only", null, null, "low", Array.Empty<SpeakingAudioObservation>());
        if (string.IsNullOrWhiteSpace(rationalesJson)) return transcriptOnly;
        try
        {
            using var doc = JsonDocument.Parse(rationalesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty(AcousticKey, out var stored)
                || stored.ValueKind != JsonValueKind.Object)
            {
                return transcriptOnly;
            }

            var source = TryReadString(stored, "source");
            var reason = TryReadString(stored, "reason");
            if (source != "audio")
            {
                return new SpeakingIntelligibilityEvidence(
                    "transcript_only", reason, reason is null ? null : SpeakingAudioEvidenceService.ReasonText(reason), "low", Array.Empty<SpeakingAudioObservation>());
            }

            var observations = new List<SpeakingAudioObservation>();
            if (stored.TryGetProperty("observations", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var issue = TryReadString(item, "issue");
                    if (string.IsNullOrWhiteSpace(issue)) continue;
                    observations.Add(new SpeakingAudioObservation(
                        TryReadInt(item, "clip") ?? 1,
                        TryReadInt(item, "approxSecond") ?? 0,
                        SpeakingLearnerText.ScrubRuleIds(issue),
                        TryReadString(item, "example") is { } example ? SpeakingLearnerText.ScrubRuleIds(example) : null));
                }
            }

            return new SpeakingIntelligibilityEvidence(
                "audio", null, null, TryReadString(stored, "confidence") ?? "medium", observations);
        }
        catch (JsonException)
        {
            return transcriptOnly;
        }
    }

    private const int MaxStrengths = 4;
    private const int MaxWeaknesses = 5;
    private const int MaxDrills = 5;
    private const int MaxTextLength = 500;
    private const int MaxQuoteLength = 240;

    private static readonly SpeakingFeedbackReport EmptyReport = new(
        Array.Empty<SpeakingFeedbackItem>(),
        Array.Empty<SpeakingFeedbackItem>(),
        Array.Empty<SpeakingReportDrill>());

    private static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web);

    private static SpeakingFeedbackReport ParseReport(JsonElement root)
        => new(
            ReadFeedbackItems(root, "strengths", MaxStrengths, withAction: false),
            ReadFeedbackItems(root, "priorityWeaknesses", MaxWeaknesses, withAction: true),
            ReadDrills(root, "drills", MaxDrills));

    private static List<SpeakingFeedbackItem> ReadFeedbackItems(JsonElement root, string property, int max, bool withAction)
    {
        var items = new List<SpeakingFeedbackItem>();
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(property, out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        foreach (var element in array.EnumerateArray())
        {
            if (items.Count >= max) break;
            string? text;
            string? quote = null;
            string? action = null;
            var criterion = "overall";
            if (element.ValueKind == JsonValueKind.String)
            {
                text = element.GetString();
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                text = TryReadString(element, "text");
                quote = TryReadString(element, "quote");
                action = TryReadString(element, "action");
                criterion = ReportCriterion(TryReadString(element, "criterion"));
            }
            else
            {
                continue;
            }

            var clippedText = Clip(text, MaxTextLength);
            if (clippedText is null) continue;
            items.Add(new SpeakingFeedbackItem(
                criterion,
                clippedText,
                Clip(quote, MaxQuoteLength),
                withAction ? Clip(action, MaxTextLength) : null));
        }

        return items;
    }

    private static List<SpeakingReportDrill> ReadDrills(JsonElement root, string property, int max)
    {
        var drills = new List<SpeakingReportDrill>();
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(property, out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return drills;
        }

        foreach (var element in array.EnumerateArray())
        {
            if (drills.Count >= max) break;
            if (element.ValueKind != JsonValueKind.Object) continue;

            var weakPoint = Clip(TryReadString(element, "weakPoint"), MaxTextLength);
            var practise = Clip(TryReadString(element, "practise") ?? TryReadString(element, "practice"), MaxTextLength);
            if (weakPoint is null || practise is null) continue;

            var title = Clip(TryReadString(element, "title"), 120) ?? Clip(practise, 80)!;
            drills.Add(new SpeakingReportDrill(
                title,
                ReportCriterion(TryReadString(element, "criterion")),
                weakPoint,
                practise,
                Clip(TryReadString(element, "example"), MaxTextLength)));
        }

        return drills;
    }

    /// <summary>One of the nine criterion codes (canonical spelling), or <c>overall</c>.</summary>
    private static string ReportCriterion(string? raw)
    {
        var code = CanonicalCriterionCode(raw?.Trim() ?? string.Empty);
        return RequiredCriteria.FirstOrDefault(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)) ?? "overall";
    }

    private static string? Clip(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    /// <summary>The stored report, read back for a candidate: internal rule IDs scrubbed from every sentence
    /// (the quotes are the candidate's own words and stay as spoken). Null when the grade has none.</summary>
    internal static SpeakingFeedbackReport? ReadStoredReport(string? rationalesJson)
    {
        if (string.IsNullOrWhiteSpace(rationalesJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(rationalesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty(ReportKey, out var stored))
            {
                return null;
            }

            var report = ParseReport(stored);
            if (report.Strengths.Count == 0 && report.PriorityWeaknesses.Count == 0 && report.Drills.Count == 0) return null;

            static string S(string value) => SpeakingLearnerText.ScrubRuleIds(value);
            static string? SN(string? value) => value is null ? null : SpeakingLearnerText.ScrubRuleIds(value);
            return new SpeakingFeedbackReport(
                report.Strengths.Select(i => i with { Text = S(i.Text) }).ToList(),
                report.PriorityWeaknesses.Select(i => i with { Text = S(i.Text), Action = SN(i.Action) }).ToList(),
                report.Drills.Select(d => d with
                {
                    Title = S(d.Title),
                    WeakPoint = S(d.WeakPoint),
                    Practise = S(d.Practise),
                    Example = SN(d.Example),
                }).ToList());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The nine criteria every reply must score. A missing or non-numeric one makes the
    /// reply unparseable (learner-retryable) rather than a silent zero grade.</summary>
    private static readonly string[] RequiredCriteria =
    [
        "intelligibility", "fluency", "appropriateness", "grammarExpression",
        "relationshipBuilding", "patientPerspective", "structure",
        "informationGathering", "informationGiving",
    ];

    /// <summary>The grounded system prompt names two criteria differently from the JSON template in
    /// <see cref="PROMPT_TEMPLATE_V2"/> (<c>grammar</c> / <c>providingStructure</c> vs
    /// <c>grammarExpression</c> / <c>structure</c>), so a model may follow either; both spellings
    /// map to the canonical code.</summary>
    internal static string CanonicalCriterionCode(string name)
    {
        if (string.Equals(name, "grammar", StringComparison.OrdinalIgnoreCase)) return "grammarExpression";
        if (string.Equals(name, "providingStructure", StringComparison.OrdinalIgnoreCase)) return "structure";
        return name;
    }

    private static ParsedAssessment? ParseAssessment(string? completion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;
        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        try
        {
            using var doc = JsonDocument.Parse(completion[start..(end + 1)]);
            var root = doc.RootElement;

            var scores = new Dictionary<string, CriterionScore>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("criterionScores", out var critEl)
                && critEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in critEl.EnumerateObject())
                {
                    var code = CanonicalCriterionCode(prop.Name);
                    // When both spellings are present the canonical one wins, whatever the order.
                    var isAlias = !string.Equals(code, prop.Name, StringComparison.Ordinal);
                    if (isAlias && scores.ContainsKey(code)) continue;

                    int? rawScore;
                    var rationale = string.Empty;
                    var quotes = Array.Empty<string>();
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        rawScore = TryReadInt(prop.Value, "score");
                        rationale = TryReadString(prop.Value, "rationale") ?? string.Empty;
                        quotes = ReadStringArray(prop.Value, "evidenceQuotes");
                    }
                    else
                    {
                        // A bare number is a legitimate way to state a score.
                        rawScore = ScalarInt(prop.Value);
                    }

                    // Non-numeric = missing: never default to 0 and persist it as a real grade.
                    if (rawScore is null) continue;
                    var max = IsLinguisticCriterion(code) ? 6 : 3;
                    scores[code] = new CriterionScore(rawScore.Value, max, rationale, quotes);
                }
            }

            foreach (var required in RequiredCriteria)
            {
                if (!scores.ContainsKey(required)) return null;
            }

            return new ParsedAssessment
            {
                CriterionScores = scores,
                OverallSummary = TryReadString(root, "overallSummary"),
                ConfidenceBand = TryReadString(root, "confidenceBand"),
                // Coaching content is best-effort: a reply without it still grades, it just has no report.
                Report = ParseReport(root),
            };
        }
        catch
        {
            return null;
        }
    }

    private static int ScoreOf(ParsedAssessment parsed, string code, int min, int max)
    {
        if (!parsed.CriterionScores.TryGetValue(code, out var cs)) return 0;
        return Math.Clamp(cs.Score, min, max);
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private static string NormaliseConfidenceBand(string? raw) => (raw ?? "medium").Trim().ToLowerInvariant() switch
    {
        "low" => "low",
        "high" => "high",
        _ => "medium",
    };

    private static bool IsLinguisticCriterion(string code) => code switch
    {
        "intelligibility" or "fluency" or "appropriateness" or "grammarExpression" => true,
        _ => false,
    };

    private static int? TryReadInt(JsonElement el, string property)
        => el.TryGetProperty(property, out var v) ? ScalarInt(v) : null;

    private static int? ScalarInt(JsonElement v)
        => v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var i) => i,
            JsonValueKind.Number => (int)Math.Round(v.GetDouble()),
            JsonValueKind.String when int.TryParse(v.GetString(), out var s) => s,
            _ => null,
        };

    private static string? TryReadString(JsonElement el, string property)
        => el.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string[] ReadStringArray(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s!);
            }
        }
        return list.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────
    // Quote verification
    // ─────────────────────────────────────────────────────────────────

    private static string ExtractTranscriptText(string segmentsJson)
    {
        if (string.IsNullOrWhiteSpace(segmentsJson)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(segmentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return string.Empty;
            var sb = new StringBuilder();
            foreach (var segment in doc.RootElement.EnumerateArray())
            {
                if (segment.ValueKind != JsonValueKind.Object) continue;
                if (segment.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                {
                    sb.Append(textEl.GetString());
                    sb.Append(' ');
                }
            }
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool ContainsNormalised(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle)) return false;
        var h = NormaliseWhitespace(haystack);
        var n = NormaliseWhitespace(needle);
        return h.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string NormaliseWhitespace(string value)
        => Regex.Replace(value.Trim(), @"\s+", " ");

    // ─────────────────────────────────────────────────────────────────
    // Profession parsing (mirrors SpeakingEvaluationPipeline)
    // ─────────────────────────────────────────────────────────────────

    private static ExamProfession ParseProfession(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ExamProfession.Medicine;
        var normalised = raw
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);
        return Enum.TryParse<ExamProfession>(normalised, ignoreCase: true, out var parsed)
            ? parsed
            : ExamProfession.Medicine;
    }
}
